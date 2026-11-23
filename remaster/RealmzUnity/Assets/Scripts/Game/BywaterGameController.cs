using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Realmz.Data;
using Realmz.World;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Realmz.Game
{
    /// <summary>
    /// Top-level orchestrator for City of Bywater: binds RealmzCore ExplorationSession,
    /// TownShopService, BywaterSaveService, and TacticalCombatSession to the 3D HD-2D world,
    /// overlays, camera rig, and Canvas HUD.
    /// </summary>
    public sealed class BywaterGameController : MonoBehaviour
    {
        private static readonly Color HeroRingColor = new Color(0.220f, 0.741f, 0.973f, 0.92f);    // #38BDF8
        private static readonly Color MonsterRingColor = new Color(0.937f, 0.267f, 0.267f, 0.92f); // #EF4444
        private static readonly float[] SpeedDelays = { 0.45f, 0.22f, 0.08f, 0.0f };
        private static readonly string[] SpeedLabels = { "1x", "2x", "4x", "Instant" };

        [Header("Scene References")]
        public BywaterAssetCatalog assetCatalog;
        public BywaterWorldRenderer worldRenderer;
        public BywaterCameraRig cameraRig;
        public BywaterGridOverlays gridOverlays;
        public BywaterHudController hudController;
        public BywaterBiomeAtmosphere biomeAtmosphere;

        private IDiceRoller _roller;
        private BywaterUnitView _exploreLeaderView;
        private readonly Dictionary<string, BywaterUnitView> _combatUnitViews =
            new Dictionary<string, BywaterUnitView>(StringComparer.Ordinal);
        private Transform _unitsContainer;

        private bool _isInCombat;
        private int _selectedPartyHeroIndex;
        private GridCoord _lastExploreHover = new GridCoord(-1, -1);
        private bool _exploreHoverDirty = true;

        private int _arenaOriginX;
        private int _arenaOriginY;
        private int _arenaWidth = 12;
        private int _arenaHeight = 10;
        private CombatActionMode _activeCombatMode = CombatActionMode.Move;
        private int _selectedSpellId = -1;
        private GridCoord _lastCombatHover = new GridCoord(-1, -1);
        private bool _combatHoverDirty = true;
        private int _speedIndex;
        private Coroutine _aiCoroutine;
        private bool _outcomeShown;
        private bool _freezeHoverForShowcase;
        private Vector2 _showcaseMouseAnchor;

        public bool IsInCombat => _isInCombat;
        public ExplorationSession ExploreSession { get; private set; }
        public TacticalCombatSession CombatSession { get; private set; }
        public BywaterM1Bundle Bundle { get; private set; }

        private string SaveFilePath => Path.Combine(Application.persistentDataPath, "bywater_save.json");

        private void Awake()
        {
            ResolveSceneReferences();
        }

        private void Start()
        {
            ResolveSceneReferences();

            _roller = new DeterministicDiceRoller((ulong)(DateTime.UtcNow.Ticks & 0x7FFFFFFFFFFFFFFFL) | 1UL);

            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            Bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            PartyState party = PartyState.FromBundle(Bundle);
            ExploreSession = new ExplorationSession(Bundle.Maps, "land_0", party, _roller);

            if (worldRenderer != null) worldRenderer.Initialize(assetCatalog);
            if (gridOverlays != null) gridOverlays.Initialize(assetCatalog);
            if (hudController != null)
            {
                hudController.BuildUi(assetCatalog);
                WireHudEvents();
            }

            EnterExplorationView(immediateCamera: true);
            hudController?.ShowToast("Welcome to City of Bywater! Left-click to explore or click Skirmish.", 3.5f);
        }

        private void Update()
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            HandleKeyboardShortcuts();

            if (hudController != null && (hudController.IsModalOpen || hudController.IsPointerOverUi()))
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || cameraRig == null)
            {
                return;
            }

            Vector2 mousePos = mouse.position.ReadValue();
            bool leftClick = mouse.leftButton.wasPressedThisFrame;
            if (_freezeHoverForShowcase)
            {
                if (leftClick || (mousePos - _showcaseMouseAnchor).sqrMagnitude > 25f)
                {
                    _freezeHoverForShowcase = false;
                }
                else
                {
                    return;
                }
            }

            if (!cameraRig.ScreenPointToGridCoord(mousePos, out GridCoord worldCoord))
            {
                return;
            }

            if (_isInCombat)
            {
                UpdateCombatInput(worldCoord, leftClick);
            }
            else
            {
                UpdateExplorationInput(worldCoord, leftClick);
            }
        }

        public void TriggerDemoSkirmish()
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            List<MonsterDefinition> monsters = SelectSkirmishMonsters(ExploreSession.CurrentMap.IsDungeon);
            StartTacticalCombat(monsters, "Bywater Skirmish");
        }

        public void StageCombatOverlayShowcaseForCapture()
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            TriggerDemoSkirmish();

            if (CombatSession == null)
            {
                return;
            }

            StopAiCoroutine();

            // Advance turns until it is Theron's (hero_mage) turn, letting vanguard heroes move and strike so Combat Log, AoO, and Fireball AoE all showcase live.
            int guard = 0;
            while (CombatSession.Outcome == CombatOutcome.InProgress && guard < 16)
            {
                guard++;
                CombatantState actor = CombatSession.CurrentActor;
                if (actor == null)
                {
                    break;
                }

                if (actor.Side == CombatSide.Monster)
                {
                    CombatSession.ExecuteMonsterAiTurn();
                    CombatSession.EndTurn();
                    continue;
                }

                if (string.Equals(actor.Id, "hero_mage", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                CombatantState nearestEnemy = FindNearestLivingEnemy(actor);
                if (nearestEnemy != null)
                {
                    IReadOnlyList<ReachableTileInfo> reach = CombatSession.GetReachableTiles(actor.Id);
                    GridCoord bestTile = actor.Position;
                    int bestDist = actor.Position.ChebyshevDistance(nearestEnemy.Position);
                    for (int i = 0; i < reach.Count; i++)
                    {
                        int d = reach[i].Coord.ChebyshevDistance(nearestEnemy.Position);
                        if (d >= 1 && d < bestDist)
                        {
                            bestDist = d;
                            bestTile = reach[i].Coord;
                        }
                    }

                    if (bestTile != actor.Position)
                    {
                        CombatSession.TryMoveTo(bestTile);
                    }

                    if (actor.Position.ChebyshevDistance(nearestEnemy.Position) <= 1)
                    {
                        CombatSession.ExecuteMeleeAttack(nearestEnemy.Id);
                    }
                }

                CombatSession.EndTurn();
            }

            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState unit = CombatSession.Combatants[i];
                if (_combatUnitViews.TryGetValue(unit.Id, out BywaterUnitView uView) && uView != null)
                {
                    uView.SetWorldPosition(BywaterCameraRig.GridToWorld(LocalToWorld(unit.Position)));
                }
            }

            SyncAllCombatUnitViews();

            CombatantState current = CombatSession.CurrentActor;
            if (current == null || current.Side != CombatSide.Hero)
            {
                return;
            }

            int showcaseSpellId = -1;
            if (ContainsInt(current.SpellIds, 1306))
            {
                showcaseSpellId = 1306;
            }
            else if (ContainsInt(current.SpellIds, 1104))
            {
                showcaseSpellId = 1104;
            }
            else if (current.SpellIds.Count > 0)
            {
                showcaseSpellId = current.SpellIds[0];
            }

            _activeCombatMode = showcaseSpellId >= 0 ? CombatActionMode.Spell : CombatActionMode.Melee;
            _selectedSpellId = showcaseSpellId;

            GridCoord bestTarget = current.Position;
            if (showcaseSpellId >= 0)
            {
                int bestEnemyHits = -1;
                for (int y = 0; y < CombatSession.Grid.Height; y++)
                {
                    for (int x = 0; x < CombatSession.Grid.Width; x++)
                    {
                        GridCoord candidate = new GridCoord(x, y);
                        TacticalSpellPreview preview = CombatSession.PreviewSpell(current.Id, showcaseSpellId.ToString(), candidate);
                        if (preview.AoeFootprint.InRange && preview.AoeFootprint.AffectedEnemyIds.Count > bestEnemyHits)
                        {
                            bestEnemyHits = preview.AoeFootprint.AffectedEnemyIds.Count;
                            bestTarget = candidate;
                        }
                    }
                }
            }

            RefreshCombatStaticOverlays(current);
            UpdateCombatHoverVisuals(bestTarget);

            if (cameraRig != null)
            {
                GridCoord mid = new GridCoord(_arenaOriginX + _arenaWidth / 2, _arenaOriginY + _arenaHeight / 2);
                cameraRig.SetFollowTarget(BywaterCameraRig.GridToWorld(mid) + new Vector3(1.1f, 0f, -0.1f), immediate: true);
            }

            _showcaseMouseAnchor = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            _freezeHoverForShowcase = true;

            RefreshCombatHudState();
            hudController?.HideToast();
        }

        public void WarpToOverworldTownShowcase()
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            if (_isInCombat)
            {
                ExitCombatToExploration(syncPartyFromCombat: false);
            }

            hudController?.CloseShopModal();
            hudController?.TogglePartyPanel(false);
            hudController?.HideOutcomeModal();
            hudController?.HideToast();

            if (ExploreSession.CurrentMap.IsDungeon)
            {
                ExploreSession = new ExplorationSession(
                    Bundle.Maps,
                    "land_0",
                    ExploreSession.Party,
                    _roller,
                    new GridCoord(10, 9));
            }
            else
            {
                ExploreSession.MoveAlongPath(new GridCoord(10, 9), autoHaltOnEvents: false);
            }

            EnterExplorationView(immediateCamera: true);

            // Stage a crisp A* hover path preview along the cobblestone plaza road past the torch toward Bywater Fine Tavern
            GridCoord previewTarget = new GridCoord(10, 13);
            if (!ExploreSession.CurrentMap.IsPassable(previewTarget.X, previewTarget.Y, ExploreSession.Party.HasBoat))
            {
                previewTarget = new GridCoord(10, 12);
            }

            _lastExploreHover = new GridCoord(-1, -1);
            _exploreHoverDirty = true;
            UpdateExplorationHoverVisuals(previewTarget);

            _showcaseMouseAnchor = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            _freezeHoverForShowcase = true;
        }

        private void ResolveSceneReferences()
        {
            if (worldRenderer == null) worldRenderer = FindFirstObjectByType<BywaterWorldRenderer>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<BywaterCameraRig>();
            if (gridOverlays == null) gridOverlays = FindFirstObjectByType<BywaterGridOverlays>();
            if (hudController == null) hudController = FindFirstObjectByType<BywaterHudController>();
            if (biomeAtmosphere == null)
            {
                biomeAtmosphere = FindFirstObjectByType<BywaterBiomeAtmosphere>();
                if (biomeAtmosphere == null)
                {
                    biomeAtmosphere = gameObject.AddComponent<BywaterBiomeAtmosphere>();
                }
            }

            if (_unitsContainer == null)
            {
                Transform existing = transform.Find("Units");
                if (existing != null)
                {
                    _unitsContainer = existing;
                }
                else
                {
                    GameObject go = new GameObject("Units");
                    go.transform.SetParent(transform, false);
                    _unitsContainer = go.transform;
                }
            }
        }

        private void WireHudEvents()
        {
            hudController.OnSkirmishRequested += TriggerDemoSkirmish;
            hudController.OnTogglePartyRequested += () => hudController.TogglePartyPanel();
            hudController.OnToggleShopRequested += HandleToggleShop;
            hudController.OnCampButtonRequested += () => hudController.TogglePartyPanel(true);
            hudController.OnShrineRequested += HandleQuickShrine;
            hudController.OnWarpPortalRequested += HandleQuickWarpPortal;
            hudController.OnSaveRequested += HandleSaveGame;
            hudController.OnLoadRequested += HandleLoadGame;

            hudController.OnHeroCardClicked += idx =>
            {
                _selectedPartyHeroIndex = idx;
                hudController.TogglePartyPanel(true);
                hudController.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
            };
            hudController.OnPartyHeroSelected += idx =>
            {
                _selectedPartyHeroIndex = idx;
                hudController.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
            };
            hudController.OnUnequipRequested += HandleUnequip;
            hudController.OnEquipFromStashRequested += HandleEquipFromStash;
            hudController.OnUseStashItemRequested += HandleUseStashItem;
            hudController.OnCampModeRequested += HandleCampRest;

            hudController.OnShopSelected += shopId => hudController.RefreshShopModal(ExploreSession.Party, Bundle);
            hudController.OnBuyItemRequested += HandleBuyItem;
            hudController.OnSellItemRequested += HandleSellItem;
            hudController.OnSellAllJunkRequested += HandleSellAllJunk;
            hudController.OnIdentifyAllRequested += HandleIdentifyAll;
            hudController.OnTempleHealRequested += HandleTempleHeal;

            hudController.OnCombatModeSelected += HandleCombatModeSelected;
            hudController.OnUndoMoveRequested += HandleUndoMove;
            hudController.OnGuardRequested += HandleGuard;
            hudController.OnEndTurnRequested += HandleEndTurn;
            hudController.OnCycleSpeedRequested += HandleCycleSpeed;
            hudController.OnSpellSelected += HandleSpellSelected;

            hudController.OnContinueAfterVictoryRequested += () => ExitCombatToExploration(syncPartyFromCombat: true);
            hudController.OnRetrySkirmishRequested += HandleRetrySkirmish;
            hudController.OnReviveAtShrineRequested += HandleReviveAtShrine;
        }

        private void HandleKeyboardShortcuts()
        {
            if (IsTypingInInputField())
            {
                return;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (hudController != null && hudController.IsShopOpen)
                {
                    hudController.CloseShopModal();
                }
                else if (hudController != null && hudController.IsPartyOpen)
                {
                    hudController.TogglePartyPanel(false);
                }
                else if (_isInCombat && _activeCombatMode != CombatActionMode.Move)
                {
                    HandleCombatModeSelected(CombatActionMode.Move);
                }
                return;
            }

            if (_isInCombat)
            {
                return;
            }

            if (kb.pKey.wasPressedThisFrame)
            {
                hudController?.TogglePartyPanel();
            }
            else if (kb.bKey.wasPressedThisFrame)
            {
                HandleToggleShop();
            }
            else if (kb.f5Key.wasPressedThisFrame)
            {
                HandleSaveGame();
            }
            else if (kb.f9Key.wasPressedThisFrame)
            {
                HandleLoadGame();
            }
        }

        private void EnterExplorationView(bool immediateCamera)
        {
            _isInCombat = false;
            _outcomeShown = false;

            if (worldRenderer != null)
            {
                worldRenderer.SetCombatArenaFrame(0, 0, 0, 0, false);
                worldRenderer.RenderMap(ExploreSession.CurrentMap, ExploreSession.Position, forceRebuild: true);
            }

            EnsureExploreLeaderView();
            Vector3 leaderWorldPos = BywaterCameraRig.GridToWorld(ExploreSession.Position);
            if (_exploreLeaderView != null)
            {
                _exploreLeaderView.gameObject.SetActive(true);
                _exploreLeaderView.SetWorldPosition(leaderWorldPos);
            }

            if (worldRenderer != null)
            {
                worldRenderer.SetPartyTorchPosition(leaderWorldPos, ExploreSession.CurrentMap.IsDungeon);
            }

            biomeAtmosphere?.ApplyBiome(ExploreSession.CurrentMap.IsDungeon, immediate: true);
            biomeAtmosphere?.UpdateFocusPosition(leaderWorldPos);

            if (cameraRig != null)
            {
                cameraRig.SetCombatMode(false);
                cameraRig.SetFollowTarget(leaderWorldPos, immediateCamera);
            }

            if (gridOverlays != null)
            {
                gridOverlays.ClearAll();
            }

            _exploreHoverDirty = true;
            _lastExploreHover = new GridCoord(-1, -1);
            hudController?.HideOutcomeModal();
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
        }

        private void EnsureExploreLeaderView()
        {
            if (_exploreLeaderView == null)
            {
                GameObject go = new GameObject("PartyLeaderView");
                go.transform.SetParent(_unitsContainer, false);
                _exploreLeaderView = go.AddComponent<BywaterUnitView>();
            }

            if (ExploreSession.Party.Members.Count > 0)
            {
                PartyMemberState leader = ExploreSession.Party.Members[0];
                BywaterAssetCatalog.HeroArtEntry art = assetCatalog != null
                    ? assetCatalog.GetHeroArt(leader.Id, leader.PortraitId, leader.Archetype)
                    : null;

                _exploreLeaderView.Configure(
                    leader.Id,
                    leader.Name,
                    art != null ? art.billboardMaterial : null,
                    HeroRingColor,
                    assetCatalog != null ? assetCatalog.uiFont : null,
                    spriteSize: 1.42f,
                    showHud: false);
                _exploreLeaderView.UpdateStats(leader.Hp, leader.MaxHp, leader.Sp, leader.MaxSp, string.Empty, true, true);
            }
        }

        private void UpdateExplorationInput(GridCoord worldCoord, bool leftClick)
        {
            TileMapData map = ExploreSession.CurrentMap;
            if (!map.InBounds(worldCoord))
            {
                return;
            }

            if (worldCoord != _lastExploreHover || _exploreHoverDirty)
            {
                UpdateExplorationHoverVisuals(worldCoord);
            }

            if (!leftClick)
            {
                return;
            }

            GridCoord prevPos = ExploreSession.Position;
            ExplorationMoveResult res = ExploreSession.MoveAlongPath(worldCoord, autoHaltOnEvents: true);
            if (res.MovedSteps > 0)
            {
                List<Vector3> worldPoints = new List<Vector3>(res.PathTaken.Count);
                for (int i = 0; i < res.PathTaken.Count; i++)
                {
                    worldPoints.Add(BywaterCameraRig.GridToWorld(res.PathTaken[i]));
                }

                if (_exploreLeaderView != null)
                {
                    _exploreLeaderView.MoveAlongWorldPath(worldPoints, 0.08f);
                }

                Vector3 finalWorld = BywaterCameraRig.GridToWorld(res.FinalPosition);
                worldRenderer?.UpdateFocusCenter(res.FinalPosition);
                worldRenderer?.SetPartyTorchPosition(finalWorld, ExploreSession.CurrentMap.IsDungeon);
                biomeAtmosphere?.UpdateFocusPosition(finalWorld);
                cameraRig?.SetFollowTarget(finalWorld, immediate: false);
            }
            else if (res.HaltReason == ExplorationHaltReason.Blocked && worldCoord != prevPos)
            {
                hudController?.ShowToast("Path blocked.", 1.6f);
            }

            _exploreHoverDirty = true;
            HandleExplorationHalt(res);
            if (!_isInCombat)
            {
                hudController?.RefreshExploreHud(ExploreSession, Bundle, worldCoord, BywaterTileClassifier.Classify(ExploreSession.CurrentMap, worldCoord.X, worldCoord.Y), 0, string.Empty, true);
            }
        }

        private void UpdateExplorationHoverVisuals(GridCoord hoverCoord)
        {
            TileMapData map = ExploreSession.CurrentMap;
            _lastExploreHover = hoverCoord;
            _exploreHoverDirty = false;

            if (!map.InBounds(hoverCoord))
            {
                return;
            }

            PathResult path = AStarPathfinder.FindPath(
                map,
                ExploreSession.Position,
                hoverCoord,
                allowDiagonal: true,
                hasBoat: ExploreSession.Party.HasBoat);

            bool reachable = hoverCoord == ExploreSession.Position || path.Found;
            gridOverlays?.SetExplorationHoverPath(path.Found ? path.Steps : null, hoverCoord, reachable);

            BywaterTileKind kind = BywaterTileClassifier.Classify(map, hoverCoord.X, hoverCoord.Y);
            string poiHint = string.Empty;

            MapPortal portal = map.GetPortalAt(hoverCoord.X, hoverCoord.Y);
            if (portal != null)
            {
                poiHint = !string.IsNullOrEmpty(portal.Label) ? portal.Label : $"Portal -> {portal.DestMapId}";
            }
            else
            {
                MapPointOfInterest poi = map.GetPointOfInterestAt(hoverCoord.X, hoverCoord.Y);
                if (poi != null)
                {
                    poiHint = !string.IsNullOrEmpty(poi.Label) ? poi.Label : poi.Kind;
                }
            }

            hudController?.RefreshExploreHud(
                ExploreSession,
                Bundle,
                hoverCoord,
                kind,
                path.Found ? path.Steps.Count : 0,
                poiHint,
                reachable);
        }

        private void HandleExplorationHalt(ExplorationMoveResult res)
        {
            switch (res.HaltReason)
            {
                case ExplorationHaltReason.Portal:
                    if (ExploreSession.UsePortalAtCurrentTile())
                    {
                        EnterExplorationView(immediateCamera: true);
                        string destName = ExploreSession.CurrentMap.IsDungeon ? "Bywater Cavern" : "City of Bywater";
                        hudController?.ShowToast($"Entered {destName} ({ExploreSession.Position.X}, {ExploreSession.Position.Y})", 2.8f);
                    }
                    break;

                case ExplorationHaltReason.PointOfInterest:
                    if (res.PointOfInterest != null)
                    {
                        HandlePoiArrival(res.PointOfInterest);
                    }
                    break;

                case ExplorationHaltReason.EncounterZone:
                    List<MonsterDefinition> zoneMonsters = SelectMonstersFromEncounterZone(res.TriggeredZone);
                    hudController?.ShowToast("Ambush! Tactical encounter triggered!", 2.5f);
                    StartTacticalCombat(zoneMonsters, "Encounter Zone Battle");
                    break;
            }
        }

        private void HandlePoiArrival(MapPointOfInterest poi)
        {
            string kind = (poi.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind == "shop" && !string.IsNullOrEmpty(poi.ShopId))
            {
                hudController?.OpenShop(poi.ShopId, ExploreSession.Party, Bundle);
                hudController?.ShowToast($"Entered {poi.Label}", 2.2f);
            }
            else if (kind == "temple")
            {
                HandleTempleHeal();
            }
            else if (kind == "tavern")
            {
                hudController?.TogglePartyPanel(true);
                hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
                hudController?.ShowToast($"Arrived at {poi.Label} - Rest & Resupply", 2.4f);
            }
            else if (!string.IsNullOrEmpty(poi.Label))
            {
                hudController?.ShowToast(poi.Label, 2.2f);
            }
        }

        private void HandleToggleShop()
        {
            if (hudController == null)
            {
                return;
            }

            if (hudController.IsShopOpen)
            {
                hudController.CloseShopModal();
            }
            else
            {
                hudController.OpenShop(hudController.ActiveShopId, ExploreSession.Party, Bundle);
            }
        }

        private void HandleQuickShrine()
        {
            if (_isInCombat || ExploreSession == null)
            {
                return;
            }

            if (ExploreSession.CurrentMap.IsDungeon)
            {
                hudController?.ShowToast("Return to Bywater surface to visit the Shrine.", 2.4f);
                return;
            }

            GridCoord shrineSpot = new GridCoord(9, 17);
            if (!ExploreSession.CurrentMap.IsPassable(shrineSpot.X, shrineSpot.Y, ExploreSession.Party.HasBoat))
            {
                shrineSpot = new GridCoord(10, 16);
            }

            ExploreSession.MoveAlongPath(shrineSpot, autoHaltOnEvents: false);
            EnterExplorationView(immediateCamera: true);
            HandleTempleHeal();
        }

        private void HandleQuickWarpPortal()
        {
            if (_isInCombat || ExploreSession == null)
            {
                return;
            }

            if (ExploreSession.CurrentMap.Portals.Count == 0)
            {
                hudController?.ShowToast("No portal on current map.", 2.0f);
                return;
            }

            MapPortal portal = ExploreSession.CurrentMap.Portals[0];
            ExploreSession.MoveAlongPath(portal.Coord, autoHaltOnEvents: false);
            if (ExploreSession.UsePortalAtCurrentTile())
            {
                EnterExplorationView(immediateCamera: true);
                string destName = ExploreSession.CurrentMap.IsDungeon ? "Bywater Cavern (Dungeon 0)" : "City of Bywater";
                hudController?.ShowToast($"Warped to {destName} ({ExploreSession.Position.X}, {ExploreSession.Position.Y})", 2.8f);
            }
            else
            {
                EnterExplorationView(immediateCamera: true);
                hudController?.ShowToast("Moved toward portal.", 2.0f);
            }
        }

        private void HandleSaveGame()
        {
            if (_isInCombat || ExploreSession == null)
            {
                return;
            }

            bool ok = BywaterSaveService.Save(SaveFilePath, ExploreSession);
            hudController?.ShowToast(ok ? "Game saved (F5)." : "Failed to save game.", 2.4f);
        }

        private void HandleLoadGame()
        {
            if (_isInCombat || Bundle == null)
            {
                return;
            }

            if (BywaterSaveService.Load(SaveFilePath, Bundle, out ExplorationSession loaded, out string status))
            {
                ExploreSession = loaded;
                EnterExplorationView(immediateCamera: true);
                hudController?.ShowToast(status, 2.8f);
            }
            else
            {
                hudController?.ShowToast(status, 2.6f);
            }
        }

        private void HandleUnequip(int heroIdx, EquipmentSlot slot)
        {
            if (ExploreSession.Party.UnequipToStash(heroIdx, slot, Bundle.Items))
            {
                hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            }
        }

        private void HandleEquipFromStash(int heroIdx, int stashIdx)
        {
            if (ExploreSession.Party.EquipFromStash(heroIdx, stashIdx, Bundle.Items))
            {
                hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                hudController?.ShowToast("Equipped item.", 1.6f);
            }
        }

        private void HandleUseStashItem(int heroIdx, int stashIdx)
        {
            PartyState party = ExploreSession.Party;
            if (stashIdx < 0 || stashIdx >= party.Stash.Count || heroIdx < 0 || heroIdx >= party.Members.Count)
            {
                return;
            }

            InventoryItem entry = party.Stash[stashIdx];
            if (!Bundle.Items.TryGetValue(entry.ItemId, out ItemDefinition def))
            {
                return;
            }

            if (string.Equals(def.Name, "Torch", StringComparison.OrdinalIgnoreCase) || entry.ItemId == 805)
            {
                party.RemoveItemFromStash(stashIdx, 1);
                party.TorchTurnsRemaining += 30;
                hudController?.RefreshPartyDrawer(party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                hudController?.ShowToast("Lit a Torch (+30 turns).", 2.2f);
                return;
            }

            PartyMemberState hero = party.Members[heroIdx];
            if (!string.IsNullOrWhiteSpace(def.HealDice) && DiceSpec.TryParse(def.HealDice, out DiceSpec spec))
            {
                party.RemoveItemFromStash(stashIdx, 1);
                int healed = Math.Max(1, spec.Roll(_roller));
                int before = hero.Hp;
                hero.Hp = Math.Min(hero.MaxHp, Math.Max(1, hero.Hp) + healed);
                if (def.Name.IndexOf("Poison", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hero.Conditions.Remove(CharConditionId.Poisoned);
                }

                hudController?.RefreshPartyDrawer(party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                hudController?.ShowToast($"{hero.Name} used {def.Name} (+{hero.Hp - before} HP).", 2.2f);
            }
        }

        private void HandleCampRest(CampMode mode)
        {
            RestResult res = ExploreSession.Party.Rest(mode);
            hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            string label = mode == CampMode.FullRest ? "Full Rest" : "Quick Bandage";
            hudController?.ShowToast($"{label}: +{res.TotalHpRestored} HP, +{res.TotalSpRestored} SP.", 2.6f);
        }

        private void HandleBuyItem(string shopId, int itemId)
        {
            if (Bundle.Shops.TryGetValue(shopId, out ShopDefinition shop) &&
                TownShopService.BuyItem(ExploreSession.Party, shop, itemId, 1, Bundle.Items))
            {
                hudController?.RefreshShopModal(ExploreSession.Party, Bundle);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                if (Bundle.Items.TryGetValue(itemId, out ItemDefinition def))
                {
                    hudController?.ShowToast($"Purchased {def.Name}.", 1.8f);
                }
            }
            else
            {
                hudController?.ShowToast("Cannot buy item (insufficient gold).", 1.8f);
            }
        }

        private void HandleSellItem(int stashIdx)
        {
            if (TownShopService.SellItem(ExploreSession.Party, stashIdx, 1, Bundle.Items))
            {
                hudController?.RefreshShopModal(ExploreSession.Party, Bundle);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            }
        }

        private void HandleSellAllJunk()
        {
            JunkSaleResult res = TownShopService.SellCommonJunk(ExploreSession.Party, Bundle.Items);
            hudController?.RefreshShopModal(ExploreSession.Party, Bundle);
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            hudController?.ShowToast(res.ItemsSold > 0 ? $"Sold {res.ItemsSold} junk item(s) for {res.GoldEarned}g." : "No junk items in stash.", 2.2f);
        }

        private void HandleIdentifyAll()
        {
            IdentifyResult res = TownShopService.IdentifyAll(ExploreSession.Party, 10);
            hudController?.RefreshShopModal(ExploreSession.Party, Bundle);
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            hudController?.ShowToast(res.ItemsIdentified > 0 ? $"Identified {res.ItemsIdentified} item(s) ({res.GoldSpent}g)." : "All stash items already identified.", 2.2f);
        }

        private void HandleTempleHeal()
        {
            bool needsHeal = false;
            for (int i = 0; i < ExploreSession.Party.Members.Count; i++)
            {
                PartyMemberState m = ExploreSession.Party.Members[i];
                if (m.Hp < m.MaxHp || m.ExhaustionStacks > 0 || m.CurseStacks > 0 || m.Conditions.Count > 0)
                {
                    needsHeal = true;
                    break;
                }
            }

            if (!needsHeal)
            {
                hudController?.ShowToast("Bywater Shrine: All party members are already at full health.", 2.4f);
                return;
            }

            if (ExploreSession.TriggerTempleHeal(25))
            {
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                if (hudController != null && hudController.IsShopOpen)
                {
                    hudController.RefreshShopModal(ExploreSession.Party, Bundle);
                }
                hudController?.ShowToast("Bywater Shrine blessing restored full HP and cleared ailments (-25g).", 2.8f);
            }
            else
            {
                hudController?.ShowToast("Not enough gold for Shrine blessing (25g required).", 2.4f);
            }
        }

        private void StartTacticalCombat(List<MonsterDefinition> monstersToSpawn, string title)
        {
            if (monstersToSpawn == null || monstersToSpawn.Count == 0)
            {
                monstersToSpawn = SelectSkirmishMonsters(ExploreSession.CurrentMap.IsDungeon);
            }

            StopAiCoroutine();
            ClearCombatUnitViews();

            TileMapData map = ExploreSession.CurrentMap;
            GridCoord partyPos = ExploreSession.Position;
            _arenaWidth = 12;
            _arenaHeight = 10;
            _arenaOriginX = Mathf.Clamp(partyPos.X - _arenaWidth / 2, 0, Math.Max(0, map.Width - _arenaWidth));
            _arenaOriginY = Mathf.Clamp(partyPos.Y - _arenaHeight / 2, 0, Math.Max(0, map.Height - _arenaHeight));

            CombatGrid grid = CombatGrid.FromTileMapSlice(
                map,
                _arenaOriginX,
                _arenaOriginY,
                _arenaWidth,
                _arenaHeight,
                ExploreSession.Party.HasBoat);

            GridCoord localCenter = new GridCoord(
                Mathf.Clamp(partyPos.X - _arenaOriginX, 0, _arenaWidth - 1),
                Mathf.Clamp(partyPos.Y - _arenaOriginY, 0, _arenaHeight - 1));

            List<GridCoord> connectedWalkable = CollectConnectedWalkableTiles(grid, localCenter);
            if (connectedWalkable.Count < 16)
            {
                // Ensure open fighting space if triggered in a tight 1-tile doorway
                for (int dy = -3; dy <= 3; dy++)
                {
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int gx = Mathf.Clamp(localCenter.X + dx, 0, _arenaWidth - 1);
                        int gy = Mathf.Clamp(localCenter.Y + dy, 0, _arenaHeight - 1);
                        if (!grid.IsWalkable(gx, gy))
                        {
                            grid = EnsureWalkableTileInGrid(grid, gx, gy);
                        }
                    }
                }
                connectedWalkable = CollectConnectedWalkableTiles(grid, localCenter);
            }

            HashSet<GridCoord> occupied = new HashSet<GridCoord>();
            List<CombatantState> combatants = new List<CombatantState>();

            // Preferred staggered 2-column Vanguard / Backline Hero slots (prevents nameplate overlap)
            GridCoord[] preferredHeroSlots =
            {
                new GridCoord(3, 2),
                new GridCoord(2, 4),
                new GridCoord(3, 6),
                new GridCoord(1, 3),
                new GridCoord(3, 4),
                new GridCoord(2, 7)
            };

            for (int i = 0; i < ExploreSession.Party.Members.Count; i++)
            {
                PartyMemberState member = ExploreSession.Party.Members[i];
                if (member.Hp <= 0)
                {
                    member.Hp = 1;
                }

                GridCoord targetSlot = preferredHeroSlots[i % preferredHeroSlots.Length];
                GridCoord spot = PickNearestFreeWalkable(connectedWalkable, occupied, targetSlot);
                occupied.Add(spot);
                combatants.Add(CombatantState.FromHero(member, spot, Bundle.Items));
            }

            // Preferred staggered East Monster formation (visible in center-right arena, clear of right UI panels)
            GridCoord[] preferredMonsterSlots =
            {
                new GridCoord(7, 2),
                new GridCoord(8, 4),
                new GridCoord(7, 6),
                new GridCoord(9, 3),
                new GridCoord(8, 6),
                new GridCoord(9, 5)
            };

            for (int i = 0; i < monstersToSpawn.Count; i++)
            {
                MonsterDefinition def = monstersToSpawn[i];
                GridCoord targetSlot = preferredMonsterSlots[i % preferredMonsterSlots.Length];
                GridCoord spot = PickNearestFreeWalkable(connectedWalkable, occupied, targetSlot);
                occupied.Add(spot);

                string instanceId = $"mon_{i}_{def.Id}";
                combatants.Add(CombatantState.FromMonster(def, instanceId, spot, _roller));
            }

            CombatSession = new TacticalCombatSession(
                grid,
                combatants,
                Bundle.Spells,
                Bundle.Items,
                _roller,
                RulesetMode.ModernBalanced,
                ExploreSession.Party);

            _isInCombat = true;
            _outcomeShown = false;
            _activeCombatMode = CombatActionMode.Move;
            _selectedSpellId = -1;
            _combatHoverDirty = true;
            _lastCombatHover = new GridCoord(-1, -1);

            if (_exploreLeaderView != null)
            {
                _exploreLeaderView.gameObject.SetActive(false);
            }

            GridCoord mid = new GridCoord(_arenaOriginX + _arenaWidth / 2, _arenaOriginY + _arenaHeight / 2);
            Vector3 midWorld = BywaterCameraRig.GridToWorld(mid);

            if (worldRenderer != null)
            {
                worldRenderer.UpdateFocusCenter(mid);
                worldRenderer.SetCombatArenaFrame(_arenaOriginX, _arenaOriginY, _arenaWidth, _arenaHeight, true);
                worldRenderer.SetPartyTorchPosition(midWorld, map.IsDungeon);
            }

            biomeAtmosphere?.ApplyBiome(map.IsDungeon, immediate: true);
            biomeAtmosphere?.UpdateFocusPosition(midWorld);

            if (cameraRig != null)
            {
                cameraRig.SetCombatMode(true);
                cameraRig.SetFollowTarget(midWorld + new Vector3(1.1f, 0f, -0.1f), immediate: true);
            }

            SpawnCombatUnitViews();
            hudController?.CloseShopModal();
            hudController?.TogglePartyPanel(false);
            hudController?.HideOutcomeModal();
            hudController?.ShowToast($"{title} started!", 2.4f);

            BeginCurrentCombatTurn();
        }

        private void SpawnCombatUnitViews()
        {
            TMP_FontAsset font = assetCatalog != null ? assetCatalog.uiFont : null;
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                GameObject go = new GameObject($"Unit_{c.Id}");
                go.transform.SetParent(_unitsContainer, false);
                BywaterUnitView view = go.AddComponent<BywaterUnitView>();

                Material mat = null;
                Color ringColor = c.Side == CombatSide.Hero ? HeroRingColor : MonsterRingColor;
                if (assetCatalog != null)
                {
                    if (c.Side == CombatSide.Hero)
                    {
                        BywaterAssetCatalog.HeroArtEntry heroArt = assetCatalog.GetHeroArt(c.Id, c.PortraitId, string.Empty);
                        mat = heroArt != null ? heroArt.billboardMaterial : null;
                    }
                    else
                    {
                        BywaterAssetCatalog.MonsterArtEntry monArt = assetCatalog.GetMonsterArt(c.Id, c.IconId);
                        mat = monArt != null ? monArt.billboardMaterial : null;
                    }
                }

                view.Configure(c.Id, c.Name, mat, ringColor, font, spriteSize: 1.42f, showHud: true);
                view.SetWorldPosition(BywaterCameraRig.GridToWorld(LocalToWorld(c.Position)));
                _combatUnitViews[c.Id] = view;
            }

            SyncAllCombatUnitViews();
        }

        private void ClearCombatUnitViews()
        {
            foreach (BywaterUnitView view in _combatUnitViews.Values)
            {
                if (view != null)
                {
                    Destroy(view.gameObject);
                }
            }

            _combatUnitViews.Clear();
        }

        private void SyncAllCombatUnitViews()
        {
            if (CombatSession == null)
            {
                return;
            }

            string currentId = CombatSession.CurrentActorId;
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (!_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView view) || view == null)
                {
                    continue;
                }

                IReadOnlyList<ConditionDisplayEffect> conds = c.Conditions.GetDisplayEffects();
                string badge = conds.Count > 0 ? conds[0].DisplayName : (c.IsGuarding ? "GUARD" : string.Empty);
                view.UpdateStats(c.Hp, c.MaxHp, c.Sp, c.MaxSp, badge, c.Id == currentId && c.IsAlive, c.IsAlive);
            }
        }

        private void BeginCurrentCombatTurn()
        {
            if (CombatSession == null)
            {
                return;
            }

            if (CombatSession.Outcome != CombatOutcome.InProgress)
            {
                HandleCombatOutcome();
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor == null)
            {
                return;
            }

            SyncAllCombatUnitViews();
            cameraRig?.SetFollowTarget(BywaterCameraRig.GridToWorld(LocalToWorld(actor.Position)), immediate: false);

            if (actor.Side == CombatSide.Monster)
            {
                gridOverlays?.ClearAll();
                hudController?.SetCombatTooltip($"{actor.Name}'s Turn", "Enemy AI acting...");
                RefreshCombatHudState();
                StopAiCoroutine();
                _aiCoroutine = StartCoroutine(RunMonsterTurnsRoutine());
                return;
            }

            if (!actor.IsConscious)
            {
                hudController?.ShowToast($"{actor.Name} is incapacitated and skips turn.", 2.0f);
                CombatSession.EndTurn();
                BeginCurrentCombatTurn();
                return;
            }

            _activeCombatMode = actor.Economy.MoveRemaining > 0
                ? CombatActionMode.Move
                : CombatActionMode.Melee;

            if (actor.SpellIds.Count > 0 && !ContainsInt(actor.SpellIds, _selectedSpellId))
            {
                _selectedSpellId = actor.SpellIds[0];
            }
            else if (actor.SpellIds.Count == 0)
            {
                _selectedSpellId = -1;
            }

            RefreshCombatStaticOverlays(actor);
            hudController?.SetCombatTooltip("Tactical Preview", GetIdleModeInstructions(_activeCombatMode));
            RefreshCombatHudState();
        }

        private IEnumerator RunMonsterTurnsRoutine()
        {
            while (CombatSession != null &&
                   CombatSession.Outcome == CombatOutcome.InProgress &&
                   CombatSession.CurrentActor != null &&
                   CombatSession.CurrentActor.Side == CombatSide.Monster)
            {
                float delay = SpeedDelays[_speedIndex];
                if (delay > 0.001f)
                {
                    yield return new WaitForSeconds(delay);
                }
                else
                {
                    yield return null;
                }

                if (CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
                {
                    break;
                }

                CombatantState monster = CombatSession.CurrentActor;
                if (monster == null || monster.Side != CombatSide.Monster)
                {
                    break;
                }

                GridCoord oldPos = monster.Position;
                Dictionary<string, int> oldHps = CaptureCombatantHps();

                CombatActionResult res = CombatSession.ExecuteMonsterAiTurn();

                if (monster.Position != oldPos && _combatUnitViews.TryGetValue(monster.Id, out BywaterUnitView mView) && mView != null)
                {
                    Vector3 targetWorld = BywaterCameraRig.GridToWorld(LocalToWorld(monster.Position));
                    if (delay <= 0.001f)
                    {
                        mView.SetWorldPosition(targetWorld);
                    }
                    else
                    {
                        mView.MoveAlongWorldPath(new[] { targetWorld }, Mathf.Min(0.12f, delay * 0.5f));
                    }
                }

                ShowHpDeltaFloatingText(oldHps);
                SyncAllCombatUnitViews();
                RefreshCombatHudState();

                if (CombatSession.Outcome != CombatOutcome.InProgress)
                {
                    break;
                }

                CombatSession.EndTurn();
                SyncAllCombatUnitViews();
                RefreshCombatHudState();
            }

            _aiCoroutine = null;
            BeginCurrentCombatTurn();
        }

        private void UpdateCombatInput(GridCoord worldCoord, bool leftClick)
        {
            if (CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
            {
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor == null || actor.Side != CombatSide.Hero)
            {
                return;
            }

            GridCoord localCoord = WorldToLocal(worldCoord);
            if (!CombatSession.Grid.InBounds(localCoord))
            {
                return;
            }

            if (localCoord != _lastCombatHover || _combatHoverDirty)
            {
                UpdateCombatHoverVisuals(localCoord);
            }

            if (!leftClick)
            {
                return;
            }

            ExecuteCombatClick(actor, localCoord);
        }

        private void RefreshCombatStaticOverlays(CombatantState actor)
        {
            if (gridOverlays == null || CombatSession == null || actor == null)
            {
                return;
            }

            gridOverlays.ClearAll();

            // 1. Reachable tiles (cyan safe / orange AoO)
            if (actor.Economy.MoveRemaining > 0)
            {
                IReadOnlyList<ReachableTileInfo> localReach = CombatSession.GetReachableTiles(actor.Id);
                gridOverlays.SetCombatReachable(OffsetReachableTiles(localReach));
            }

            // 2. Enemy threatened 3x3 rings
            HashSet<GridCoord> threatenedWorld = new HashSet<GridCoord>();
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (!c.IsAlive || c.Side == actor.Side)
                {
                    continue;
                }

                IReadOnlyList<GridCoord> tiles = CombatSession.GetThreatenedTiles(c.Id);
                for (int t = 0; t < tiles.Count; t++)
                {
                    threatenedWorld.Add(LocalToWorld(tiles[t]));
                }
            }

            gridOverlays.SetCombatThreatened(new List<GridCoord>(threatenedWorld));
        }

        private void UpdateCombatHoverVisuals(GridCoord localCoord)
        {
            _lastCombatHover = localCoord;
            _combatHoverDirty = false;

            if (CombatSession == null)
            {
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor == null || actor.Side != CombatSide.Hero)
            {
                return;
            }

            // Reset transient hover layers while keeping static reachable & threatened overlays
            gridOverlays?.SetCombatHoverPath(null, false);
            gridOverlays?.SetLosPreview(null, default, default);
            gridOverlays?.SetSpellAoeFootprint(null, null, null, false);
            gridOverlays?.SetFlankIndicator(null, null);

            CombatantState hoveredUnit = GetLivingUnitAt(localCoord);

            switch (_activeCombatMode)
            {
                case CombatActionMode.Move:
                {
                    if (hoveredUnit != null && hoveredUnit.Side == CombatSide.Monster)
                    {
                        TacticalAttackPreview meleePreview = CombatSession.PreviewMeleeAttack(actor.Id, hoveredUnit.Id);
                        if (meleePreview.CanExecute)
                        {
                            ShowAttackTooltip($"Quick Melee: {hoveredUnit.Name}", meleePreview, isRanged: false);
                            UpdateFlankOverlay(actor, hoveredUnit, meleePreview.IsFlanking);
                            break;
                        }
                    }

                    IReadOnlyList<ReachableTileInfo> reach = CombatSession.GetReachableTiles(actor.Id);
                    ReachableTileInfo match = null;
                    for (int i = 0; i < reach.Count; i++)
                    {
                        if (reach[i].Coord == localCoord)
                        {
                            match = reach[i];
                            break;
                        }
                    }

                    if (match != null)
                    {
                        List<GridCoord> worldPath = new List<GridCoord>(match.Path.Count);
                        for (int i = 0; i < match.Path.Count; i++)
                        {
                            worldPath.Add(LocalToWorld(match.Path[i]));
                        }

                        gridOverlays?.SetCombatHoverPath(worldPath, match.ProvokesAoO);
                        string aooNote = match.ProvokesAoO
                            ? "WARNING: Path provokes Attack of Opportunity (AoO)!"
                            : "Safe path (no AoO provoked).";
                        hudController?.SetCombatTooltip(
                            $"Move to ({localCoord.X}, {localCoord.Y})",
                            $"Move Cost: {match.PathCost} / {actor.Economy.MoveRemaining} remaining\n{aooNote}\n\nClick to step.");
                    }
                    else
                    {
                        hudController?.SetCombatTooltip("Move Mode", GetIdleModeInstructions(CombatActionMode.Move));
                    }
                    break;
                }

                case CombatActionMode.Melee:
                {
                    if (hoveredUnit != null && hoveredUnit.Side == CombatSide.Monster)
                    {
                        TacticalAttackPreview preview = CombatSession.PreviewMeleeAttack(actor.Id, hoveredUnit.Id);
                        ShowAttackTooltip($"Melee vs {hoveredUnit.Name}", preview, isRanged: false);
                        UpdateFlankOverlay(actor, hoveredUnit, preview.IsFlanking);
                    }
                    else
                    {
                        hudController?.SetCombatTooltip("Melee Mode", GetIdleModeInstructions(CombatActionMode.Melee));
                    }
                    break;
                }

                case CombatActionMode.Ranged:
                {
                    if (hoveredUnit != null && hoveredUnit.Side == CombatSide.Monster)
                    {
                        TacticalAttackPreview preview = CombatSession.PreviewRangedAttack(actor.Id, hoveredUnit.Id);
                        ShowAttackTooltip($"Ranged vs {hoveredUnit.Name}", preview, isRanged: true);
                        if (preview.LosResult != null)
                        {
                            gridOverlays?.SetLosPreview(
                                OffsetLosResult(preview.LosResult),
                                LocalToWorld(actor.Position),
                                LocalToWorld(hoveredUnit.Position));
                        }
                    }
                    else
                    {
                        hudController?.SetCombatTooltip("Ranged Mode", GetIdleModeInstructions(CombatActionMode.Ranged));
                    }
                    break;
                }

                case CombatActionMode.Spell:
                {
                    if (_selectedSpellId < 0 || !Bundle.Spells.TryGetValue(_selectedSpellId, out SpellDefinition spell))
                    {
                        hudController?.SetCombatTooltip("Spell Mode", "Select a spell from the picker bar above the action bar.");
                        break;
                    }

                    TacticalSpellPreview preview = CombatSession.PreviewSpell(actor.Id, spell.Id.ToString(), localCoord);
                    HashSet<GridCoord> allyWorld = new HashSet<GridCoord>();
                    HashSet<GridCoord> enemyWorld = new HashSet<GridCoord>();
                    for (int i = 0; i < preview.AoeFootprint.AffectedAllyIds.Count; i++)
                    {
                        CombatantState ally = CombatSession.GetCombatant(preview.AoeFootprint.AffectedAllyIds[i]);
                        if (ally != null) allyWorld.Add(LocalToWorld(ally.Position));
                    }
                    for (int i = 0; i < preview.AoeFootprint.AffectedEnemyIds.Count; i++)
                    {
                        CombatantState foe = CombatSession.GetCombatant(preview.AoeFootprint.AffectedEnemyIds[i]);
                        if (foe != null) enemyWorld.Add(LocalToWorld(foe.Position));
                    }

                    bool isBeneficial = string.Equals(spell.EffectKind, "Heal", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(spell.EffectKind, "Buff", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(spell.EffectKind, "Cure", StringComparison.OrdinalIgnoreCase);

                    gridOverlays?.SetSpellAoeFootprint(
                        OffsetSpellFootprint(preview.AoeFootprint),
                        allyWorld,
                        enemyWorld,
                        isBeneficial);

                    string condText = preview.ConditionTags.Count > 0 ? string.Join(", ", preview.ConditionTags) : "None";
                    string statusLine = preview.CanExecute ? "READY TO CAST (Click to fire)" : $"Blocked: {preview.Reason}";
                    hudController?.SetCombatTooltip(
                        $"Spell: {spell.Name} ({spell.SpCost} SP)",
                        $"Effect ({spell.EffectKind}): {preview.MinAmount} - {preview.MaxAmount}\n" +
                        $"Save%: {preview.SaveChancePct}%\n" +
                        $"AoE Hits: {preview.AoeFootprint.AffectedEnemyIds.Count} Foe(s) vs {preview.AoeFootprint.AffectedAllyIds.Count} Ally(s)\n" +
                        $"Tags: {condText}\n\n{statusLine}");
                    break;
                }

                case CombatActionMode.Potion:
                {
                    if (TryFindCombatHealPotion(out ItemDefinition potionDef))
                    {
                        string targetText = hoveredUnit != null && hoveredUnit.Side == CombatSide.Hero
                            ? $"Target: {hoveredUnit.Name} ({hoveredUnit.Hp}/{hoveredUnit.MaxHp} HP)"
                            : "Click any conscious or injured ally to heal.";
                        hudController?.SetCombatTooltip(
                            $"Swift Potion: {potionDef.Name}",
                            $"Heal Dice: {potionDef.HealDice}\nCost: 1 Swift Action\n\n{targetText}");
                    }
                    else
                    {
                        hudController?.SetCombatTooltip("Swift Potion", "No healing potions left in party stash.");
                    }
                    break;
                }
            }
        }

        private void ExecuteCombatClick(CombatantState actor, GridCoord localCoord)
        {
            CombatantState clickedUnit = GetLivingUnitAt(localCoord);
            Dictionary<string, int> oldHps = CaptureCombatantHps();

            switch (_activeCombatMode)
            {
                case CombatActionMode.Move:
                {
                    if (clickedUnit != null && clickedUnit.Side == CombatSide.Monster)
                    {
                        TacticalAttackPreview melee = CombatSession.PreviewMeleeAttack(actor.Id, clickedUnit.Id);
                        if (melee.CanExecute)
                        {
                            CombatActionResult atkRes = CombatSession.ExecuteMeleeAttack(clickedUnit.Id);
                            FinishPlayerCombatAction(atkRes, oldHps);
                            return;
                        }
                    }

                    GridCoord oldPos = actor.Position;
                    CombatActionResult moveRes = CombatSession.TryMoveTo(localCoord);
                    if (moveRes.Success)
                    {
                        if (actor.Position != oldPos && _combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView view) && view != null)
                        {
                            view.MoveAlongWorldPath(new[] { BywaterCameraRig.GridToWorld(LocalToWorld(actor.Position)) }, 0.08f);
                        }

                        if (moveRes.AoOTriggered && _combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView hitView) && hitView != null)
                        {
                            hitView.ShowFloatingText($"AoO -{moveRes.DamageDealt}!", BywaterUiHelpers.DangerRed);
                        }

                        RefreshCombatStaticOverlays(actor);
                        _combatHoverDirty = true;
                        SyncAllCombatUnitViews();
                        RefreshCombatHudState();
                        if (CombatSession.Outcome != CombatOutcome.InProgress)
                        {
                            HandleCombatOutcome();
                        }
                    }
                    else
                    {
                        hudController?.ShowToast(moveRes.Message, 1.8f);
                    }
                    break;
                }

                case CombatActionMode.Melee:
                {
                    if (clickedUnit == null || clickedUnit.Side != CombatSide.Monster)
                    {
                        hudController?.ShowToast("Click an adjacent enemy to melee attack.", 1.6f);
                        return;
                    }

                    CombatActionResult res = CombatSession.ExecuteMeleeAttack(clickedUnit.Id);
                    FinishPlayerCombatAction(res, oldHps);
                    break;
                }

                case CombatActionMode.Ranged:
                {
                    if (clickedUnit == null || clickedUnit.Side != CombatSide.Monster)
                    {
                        hudController?.ShowToast("Click an enemy in line of sight to fire.", 1.6f);
                        return;
                    }

                    CombatActionResult res = CombatSession.ExecuteRangedAttack(clickedUnit.Id);
                    FinishPlayerCombatAction(res, oldHps);
                    break;
                }

                case CombatActionMode.Spell:
                {
                    if (_selectedSpellId < 0)
                    {
                        hudController?.ShowToast("Select a spell first.", 1.6f);
                        return;
                    }

                    CombatActionResult res = CombatSession.CastSpell(_selectedSpellId.ToString(), localCoord);
                    FinishPlayerCombatAction(res, oldHps);
                    break;
                }

                case CombatActionMode.Potion:
                {
                    if (!TryFindCombatHealPotion(out ItemDefinition potionDef))
                    {
                        hudController?.ShowToast("No healing potions remaining.", 1.8f);
                        return;
                    }

                    string targetId = clickedUnit != null && clickedUnit.Side == CombatSide.Hero ? clickedUnit.Id : actor.Id;
                    CombatActionResult res = CombatSession.UseSwiftPotion(potionDef.Id, targetId);
                    FinishPlayerCombatAction(res, oldHps);
                    break;
                }
            }
        }

        private void FinishPlayerCombatAction(CombatActionResult res, Dictionary<string, int> oldHps)
        {
            if (!res.Success)
            {
                hudController?.ShowToast(res.Message, 2.0f);
                return;
            }

            ShowHpDeltaFloatingText(oldHps);
            SyncAllCombatUnitViews();

            if (CombatSession.Outcome != CombatOutcome.InProgress)
            {
                RefreshCombatHudState();
                HandleCombatOutcome();
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor != null)
            {
                if (!actor.Economy.HasStandardAction && actor.Economy.MoveRemaining > 0)
                {
                    _activeCombatMode = CombatActionMode.Move;
                }

                RefreshCombatStaticOverlays(actor);
                _combatHoverDirty = true;
            }

            RefreshCombatHudState();
        }

        private void HandleCombatModeSelected(CombatActionMode mode)
        {
            if (!_isInCombat || CombatSession == null)
            {
                return;
            }

            _activeCombatMode = mode;
            CombatantState actor = CombatSession.CurrentActor;
            if (actor != null && mode == CombatActionMode.Spell && _selectedSpellId < 0 && actor.SpellIds.Count > 0)
            {
                _selectedSpellId = actor.SpellIds[0];
            }

            RefreshCombatStaticOverlays(actor);
            _combatHoverDirty = true;
            hudController?.SetCombatTooltip(mode.ToString() + " Mode", GetIdleModeInstructions(mode));
            RefreshCombatHudState();
        }

        private void HandleSpellSelected(int spellId)
        {
            _selectedSpellId = spellId;
            _activeCombatMode = CombatActionMode.Spell;
            _combatHoverDirty = true;
            RefreshCombatHudState();
        }

        private void HandleUndoMove()
        {
            if (!_isInCombat || CombatSession == null)
            {
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (CombatSession.UndoMove() && actor != null)
            {
                if (_combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView view) && view != null)
                {
                    view.SetWorldPosition(BywaterCameraRig.GridToWorld(LocalToWorld(actor.Position)));
                }

                RefreshCombatStaticOverlays(actor);
                _combatHoverDirty = true;
                RefreshCombatHudState();
                hudController?.ShowToast($"{actor.Name} undid movement.", 1.6f);
            }
        }

        private void HandleGuard()
        {
            if (!_isInCombat || CombatSession == null)
            {
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            CombatActionResult res = CombatSession.GuardOverwatch();
            if (res.Success && actor != null)
            {
                if (_combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView view) && view != null)
                {
                    view.ShowFloatingText("GUARD +4 AC", BywaterUiHelpers.InfoCyan);
                }

                SyncAllCombatUnitViews();
                RefreshCombatHudState();
            }
            else
            {
                hudController?.ShowToast(res.Message, 1.8f);
            }
        }

        private void HandleEndTurn()
        {
            if (!_isInCombat || CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
            {
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor == null || actor.Side != CombatSide.Hero)
            {
                return;
            }

            CombatSession.EndTurn();
            BeginCurrentCombatTurn();
        }

        private void HandleCycleSpeed()
        {
            _speedIndex = (_speedIndex + 1) % SpeedDelays.Length;
            RefreshCombatHudState();
        }

        private void HandleCombatOutcome()
        {
            if (_outcomeShown || CombatSession == null)
            {
                return;
            }

            _outcomeShown = true;
            StopAiCoroutine();
            gridOverlays?.ClearAll();

            if (CombatSession.Outcome == CombatOutcome.Victory)
            {
                SyncHeroesBackToParty(floorDeadToOne: true);
            }

            hudController?.ShowOutcomeModal(CombatSession.Outcome, CombatSession.TotalXpEarned, CombatSession.TotalGoldLoot);
        }

        private void ExitCombatToExploration(bool syncPartyFromCombat)
        {
            StopAiCoroutine();

            if (syncPartyFromCombat && CombatSession != null)
            {
                SyncHeroesBackToParty(floorDeadToOne: true);
            }

            ClearCombatUnitViews();
            CombatSession = null;
            EnterExplorationView(immediateCamera: true);
        }

        private void HandleRetrySkirmish()
        {
            for (int i = 0; i < ExploreSession.Party.Members.Count; i++)
            {
                PartyMemberState m = ExploreSession.Party.Members[i];
                m.Hp = m.MaxHp;
                m.Sp = m.MaxSp;
                m.Conditions.Clear();
            }

            hudController?.HideOutcomeModal();
            TriggerDemoSkirmish();
        }

        private void HandleReviveAtShrine()
        {
            for (int i = 0; i < ExploreSession.Party.Members.Count; i++)
            {
                PartyMemberState m = ExploreSession.Party.Members[i];
                m.Hp = m.MaxHp;
                m.Sp = m.MaxSp;
                m.ExhaustionStacks = 0;
                m.CurseStacks = 0;
                m.Conditions.Clear();
            }

            ExitCombatToExploration(syncPartyFromCombat: false);
            if (ExploreSession.CurrentMap.IsDungeon)
            {
                ExploreSession = new ExplorationSession(Bundle.Maps, "land_0", ExploreSession.Party, _roller, new GridCoord(10, 16));
                EnterExplorationView(immediateCamera: true);
            }
            else
            {
                ExploreSession.MoveAlongPath(new GridCoord(10, 16), autoHaltOnEvents: false);
                EnterExplorationView(immediateCamera: true);
            }

            hudController?.ShowToast("Revived at Bywater Shrine with full health and spell points.", 3.0f);
        }

        private void SyncHeroesBackToParty(bool floorDeadToOne)
        {
            if (CombatSession == null || ExploreSession == null)
            {
                return;
            }

            for (int i = 0; i < ExploreSession.Party.Members.Count; i++)
            {
                PartyMemberState member = ExploreSession.Party.Members[i];
                CombatantState c = CombatSession.GetCombatant(member.Id);
                if (c == null)
                {
                    continue;
                }

                member.Hp = floorDeadToOne ? Math.Max(1, Math.Min(member.MaxHp, c.Hp)) : Mathf.Clamp(c.Hp, 0, member.MaxHp);
                member.Sp = Mathf.Clamp(c.Sp, 0, member.MaxSp);
                member.ExhaustionStacks = c.ExhaustionStacks;
                member.CurseStacks = c.CurseStacks;
            }
        }

        private void ShowAttackTooltip(string title, TacticalAttackPreview preview, bool isRanged)
        {
            string tags = preview.ConditionTags.Count > 0 ? string.Join(", ", preview.ConditionTags) : "None";
            string losLine = isRanged && preview.LosResult != null
                ? $"LOS: {preview.LosResult.Kind} (Cover -{preview.LosResult.CoverPenaltyPct}%)\n"
                : string.Empty;
            string flankLine = preview.IsFlanking ? "FLANKING (+20% Hit)\n" : string.Empty;
            string aooLine = preview.ProvokesAoO ? "AoO WARNING!\n" : string.Empty;
            string actionHint = preview.CanExecute ? "Click target to attack." : $"Blocked: {preview.Reason}";

            hudController?.SetCombatTooltip(
                title,
                $"Hit%: {preview.HitChancePct}% (Uncapped {preview.UncappedHitScore}%)\n" +
                $"Dmg: {preview.MinDamage} - {preview.MaxDamage}   |   Defender AC: {preview.DefenderAc}\n" +
                $"Crit%: {preview.CritChancePct}%   |   Graze%: {preview.GrazeChancePct}%\n" +
                losLine + flankLine + aooLine +
                $"Tags: {tags}\n\n{actionHint}");
        }

        private void UpdateFlankOverlay(CombatantState actor, CombatantState target, bool isFlanking)
        {
            if (gridOverlays == null || !isFlanking)
            {
                return;
            }

            List<GridCoord> flankingAllies = new List<GridCoord>();
            int attDx = actor.Position.X - target.Position.X;
            int attDy = actor.Position.Y - target.Position.Y;

            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState ally = CombatSession.Combatants[i];
                if (ally.Id == actor.Id || ally.Side != actor.Side || !ally.IsConscious)
                {
                    continue;
                }

                if (ally.Position.ChebyshevDistance(target.Position) <= 1)
                {
                    int allyDx = ally.Position.X - target.Position.X;
                    int allyDy = ally.Position.Y - target.Position.Y;
                    if ((allyDx * attDx) + (allyDy * attDy) <= 0)
                    {
                        flankingAllies.Add(LocalToWorld(ally.Position));
                    }
                }
            }

            gridOverlays.SetFlankIndicator(LocalToWorld(target.Position), flankingAllies);
        }

        private void RefreshCombatHudState()
        {
            hudController?.RefreshCombatHud(
                CombatSession,
                Bundle,
                _activeCombatMode,
                _selectedSpellId,
                SpeedLabels[_speedIndex]);
        }

        private List<MonsterDefinition> SelectSkirmishMonsters(bool isDungeon)
        {
            string[] desiredIds = isDungeon
                ? new[] { "skeletal_warrior", "giant_rat", "zombie", "monstrous_rat" }
                : new[] { "giant_frog", "goblin", "goblin_archer", "orc_warrior" };

            List<MonsterDefinition> picked = new List<MonsterDefinition>(4);
            for (int i = 0; i < desiredIds.Length; i++)
            {
                MonsterDefinition def = FindMonsterById(desiredIds[i]);
                if (def != null)
                {
                    picked.Add(def);
                }
            }

            if (picked.Count == 0 && Bundle.Monsters.Count > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    picked.Add(Bundle.Monsters[i % Bundle.Monsters.Count]);
                }
            }

            return picked;
        }

        private List<MonsterDefinition> SelectMonstersFromEncounterZone(EncounterZoneData zone)
        {
            List<MonsterDefinition> list = new List<MonsterDefinition>();
            if (zone != null && zone.MonsterIndices.Count > 0)
            {
                for (int i = 0; i < zone.MonsterIndices.Count && list.Count < 4; i++)
                {
                    int idx = zone.MonsterIndices[i];
                    MonsterDefinition def = FindMonsterByIndex(idx);
                    if (def != null)
                    {
                        list.Add(def);
                    }
                }
            }

            if (list.Count == 0)
            {
                return SelectSkirmishMonsters(ExploreSession.CurrentMap.IsDungeon);
            }

            while (list.Count < 3)
            {
                list.Add(list[0]);
            }

            return list;
        }

        private MonsterDefinition FindMonsterById(string id)
        {
            for (int i = 0; i < Bundle.Monsters.Count; i++)
            {
                if (string.Equals(Bundle.Monsters[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return Bundle.Monsters[i];
                }
            }

            return null;
        }

        private MonsterDefinition FindMonsterByIndex(int index)
        {
            for (int i = 0; i < Bundle.Monsters.Count; i++)
            {
                if (Bundle.Monsters[i].Index == index)
                {
                    return Bundle.Monsters[i];
                }
            }

            return null;
        }

        private bool TryFindCombatHealPotion(out ItemDefinition potionDef)
        {
            potionDef = null;
            if (ExploreSession == null || Bundle == null)
            {
                return false;
            }

            int bestScore = -1;
            for (int i = 0; i < ExploreSession.Party.Stash.Count; i++)
            {
                InventoryItem item = ExploreSession.Party.Stash[i];
                if (item.Quantity <= 0 || !Bundle.Items.TryGetValue(item.ItemId, out ItemDefinition def))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(def.HealDice))
                {
                    continue;
                }

                int score = DiceSpec.TryParse(def.HealDice, out DiceSpec spec) ? spec.Max : 1;
                if (score > bestScore)
                {
                    bestScore = score;
                    potionDef = def;
                }
            }

            return potionDef != null;
        }

        private CombatantState GetLivingUnitAt(GridCoord localCoord)
        {
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (c.IsAlive && c.Position == localCoord)
                {
                    return c;
                }
            }

            return null;
        }

        private CombatantState FindNearestLivingEnemy(CombatantState actor)
        {
            CombatantState best = null;
            int bestDist = int.MaxValue;
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (!c.IsAlive || c.Side == actor.Side)
                {
                    continue;
                }

                int d = actor.Position.ChebyshevDistance(c.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }

            return best;
        }

        private Dictionary<string, int> CaptureCombatantHps()
        {
            Dictionary<string, int> hps = new Dictionary<string, int>(CombatSession.Combatants.Count, StringComparer.Ordinal);
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                hps[c.Id] = c.Hp;
            }
            return hps;
        }

        private void ShowHpDeltaFloatingText(Dictionary<string, int> oldHps)
        {
            if (CombatSession == null || oldHps == null)
            {
                return;
            }

            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (!oldHps.TryGetValue(c.Id, out int before) || c.Hp == before)
                {
                    continue;
                }

                if (!_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView view) || view == null)
                {
                    continue;
                }

                int delta = c.Hp - before;
                if (delta < 0)
                {
                    view.ShowFloatingText($"{delta}", BywaterUiHelpers.DangerRed);
                }
                else
                {
                    view.ShowFloatingText($"+{delta}", BywaterUiHelpers.SuccessGreen);
                }
            }
        }

        private void StopAiCoroutine()
        {
            if (_aiCoroutine != null)
            {
                StopCoroutine(_aiCoroutine);
                _aiCoroutine = null;
            }
        }

        private GridCoord LocalToWorld(GridCoord local)
        {
            return new GridCoord(_arenaOriginX + local.X, _arenaOriginY + local.Y);
        }

        private GridCoord WorldToLocal(GridCoord world)
        {
            return new GridCoord(world.X - _arenaOriginX, world.Y - _arenaOriginY);
        }

        private List<ReachableTileInfo> OffsetReachableTiles(IReadOnlyList<ReachableTileInfo> localTiles)
        {
            List<ReachableTileInfo> result = new List<ReachableTileInfo>(localTiles.Count);
            for (int i = 0; i < localTiles.Count; i++)
            {
                ReachableTileInfo t = localTiles[i];
                List<GridCoord> worldPath = new List<GridCoord>(t.Path.Count);
                for (int p = 0; p < t.Path.Count; p++)
                {
                    worldPath.Add(LocalToWorld(t.Path[p]));
                }

                result.Add(new ReachableTileInfo(
                    LocalToWorld(t.Coord),
                    t.PathCost,
                    worldPath,
                    t.ProvokesAoO,
                    t.ThreateningEnemyIds));
            }
            return result;
        }

        private LosResult OffsetLosResult(LosResult los)
        {
            if (los == null)
            {
                return null;
            }

            List<GridCoord> worldCells = new List<GridCoord>(los.RayCells.Count);
            for (int i = 0; i < los.RayCells.Count; i++)
            {
                worldCells.Add(LocalToWorld(los.RayCells[i]));
            }

            GridCoord? blocker = los.BlockedBy.HasValue ? LocalToWorld(los.BlockedBy.Value) : (GridCoord?)null;
            return new LosResult(los.Kind, los.CoverPenaltyPct, blocker, worldCells);
        }

        private SpellAoeFootprint OffsetSpellFootprint(SpellAoeFootprint fp)
        {
            if (fp == null)
            {
                return null;
            }

            List<GridCoord> affected = new List<GridCoord>(fp.AffectedTiles.Count);
            for (int i = 0; i < fp.AffectedTiles.Count; i++)
            {
                affected.Add(LocalToWorld(fp.AffectedTiles[i]));
            }

            List<GridCoord> blocked = new List<GridCoord>(fp.BlockedTiles.Count);
            for (int i = 0; i < fp.BlockedTiles.Count; i++)
            {
                blocked.Add(LocalToWorld(fp.BlockedTiles[i]));
            }

            return new SpellAoeFootprint(
                fp.InRange,
                LocalToWorld(fp.Center),
                affected,
                fp.AffectedAllyIds,
                fp.AffectedEnemyIds,
                blocked);
        }

        private static List<GridCoord> CollectConnectedWalkableTiles(CombatGrid grid, GridCoord start)
        {
            List<GridCoord> list = new List<GridCoord>();
            GridCoord seed = start;
            if (!grid.IsWalkable(seed.X, seed.Y))
            {
                bool found = false;
                for (int r = 1; r <= Math.Max(grid.Width, grid.Height) && !found; r++)
                {
                    for (int dy = -r; dy <= r && !found; dy++)
                    {
                        for (int dx = -r; dx <= r && !found; dx++)
                        {
                            int nx = start.X + dx;
                            int ny = start.Y + dy;
                            if (grid.IsWalkable(nx, ny))
                            {
                                seed = new GridCoord(nx, ny);
                                found = true;
                            }
                        }
                    }
                }

                if (!found)
                {
                    return list;
                }
            }

            bool[] visited = new bool[grid.Width * grid.Height];
            Queue<GridCoord> q = new Queue<GridCoord>();
            q.Enqueue(seed);
            visited[seed.Y * grid.Width + seed.X] = true;

            int[] dx8 = { 0, 1, 0, -1, 1, 1, -1, -1 };
            int[] dy8 = { -1, 0, 1, 0, -1, 1, 1, -1 };

            while (q.Count > 0)
            {
                GridCoord cur = q.Dequeue();
                list.Add(cur);

                for (int d = 0; d < 8; d++)
                {
                    int nx = cur.X + dx8[d];
                    int ny = cur.Y + dy8[d];
                    if (!grid.IsWalkable(nx, ny))
                    {
                        continue;
                    }

                    int idx = ny * grid.Width + nx;
                    if (!visited[idx])
                    {
                        visited[idx] = true;
                        q.Enqueue(new GridCoord(nx, ny));
                    }
                }
            }

            return list;
        }

        private static CombatGrid EnsureWalkableTileInGrid(CombatGrid source, int openX, int openY)
        {
            CombatGrid copy = new CombatGrid(source.Width, source.Height);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (x == openX && y == openY)
                    {
                        copy.SetMoveCost(x, y, 1);
                        continue;
                    }

                    if (!source.IsWalkable(x, y))
                    {
                        copy.SetBlocked(x, y, source.BlocksLos(x, y));
                    }
                    else
                    {
                        copy.SetMoveCost(x, y, source.GetMoveCost(x, y));
                        if (source.ProvidesCover(x, y))
                        {
                            copy.SetCover(x, y, source.GetMoveCost(x, y), source.BlocksLos(x, y));
                        }
                    }
                }
            }

            return copy;
        }

        private static string GetIdleModeInstructions(CombatActionMode mode)
        {
            switch (mode)
            {
                case CombatActionMode.Move:
                    return "Hover highlighted tiles to preview movement & AoO risk. Click to step (or click an adjacent enemy to quick-melee).";
                case CombatActionMode.Melee:
                    return "Hover an adjacent enemy to inspect Hit%, Crit%, Graze%, Damage, and Flanking bonus. Click to strike.";
                case CombatActionMode.Ranged:
                    return "Hover a distant enemy to inspect Line-of-Sight ray, Cover penalty, and Hit%. Click to shoot.";
                case CombatActionMode.Spell:
                    return "Pick a spell above the action bar, hover the arena to preview AoE footprint & Save%, then click to cast.";
                case CombatActionMode.Potion:
                    return "Click any ally (or yourself) to drink a Swift healing potion.";
                default:
                    return string.Empty;
            }
        }

        private static GridCoord PickNearestFreeWalkable(
            List<GridCoord> candidates,
            HashSet<GridCoord> occupied,
            GridCoord preferred)
        {
            GridCoord best = preferred;
            int bestDist = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                GridCoord c = candidates[i];
                if (occupied.Contains(c))
                {
                    continue;
                }

                int d = c.ManhattanDistance(preferred);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }

            return best;
        }

        private static bool ContainsInt(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTypingInInputField()
        {
            EventSystem es = EventSystem.current;
            return es != null &&
                   es.currentSelectedGameObject != null &&
                   es.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        }
    }
}
