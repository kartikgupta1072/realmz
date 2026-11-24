using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Realmz.Audio;
using Realmz.Cartography;
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
    /// TownShopService, BywaterSaveService, BywaterAudioDirector, and TacticalCombatSession
    /// to the Cartography 3D HD-2D world, overlays, camera rig, and Canvas HUD.
    /// </summary>
    public sealed class BywaterGameController : MonoBehaviour
    {
        private static readonly Color HeroRingColor = new Color(0.220f, 0.741f, 0.973f, 0.92f);
        private static readonly Color MonsterRingColor = new Color(0.937f, 0.267f, 0.267f, 0.92f);
        private static readonly float[] SpeedDelays = { 0.45f, 0.22f, 0.08f, 0.0f };
        private static readonly string[] SpeedLabels = { "1x", "2x", "4x", "Instant" };
        private const float ExploreStepDurationSeconds = 0.11f;
        private const float CampSemicircleRadius = 1.15f;
        private const int MaxTrailingPartyViews = 6;
        private const int MaxSavedLogLines = 40;

        [Header("Scene References")]
        public BywaterAssetCatalog assetCatalog;
        public BywaterWorldRenderer worldRenderer;
        public BywaterCameraRig cameraRig;
        public BywaterGridOverlays gridOverlays;
        public BywaterHudController hudController;
        public BywaterBiomeAtmosphere biomeAtmosphere;
        public BywaterAudioDirector audioDirector;

        private IDiceRoller _roller;
        private readonly AStarPathfinder _explorePathfinder = new AStarPathfinder(90, 90);
        private readonly List<GridCoord> _explorePreviewSteps = new List<GridCoord>(256);
        private readonly List<GridCoord> _activeMoveSteps = new List<GridCoord>(256);
        private int _activeMoveStepIndex;

        private readonly List<BywaterUnitView> _exploreHeroViews = new List<BywaterUnitView>(MaxTrailingPartyViews);
        private readonly List<BywaterUnitView> _exploreViewPool = new List<BywaterUnitView>(MaxTrailingPartyViews);
        private readonly List<GridCoord> _partyTrailHistory = new List<GridCoord>(MaxTrailingPartyViews);
        private readonly Vector3[] _singleStepWorldScratch = new Vector3[1];

        private readonly Dictionary<string, BywaterUnitView> _combatUnitViews =
            new Dictionary<string, BywaterUnitView>(StringComparer.Ordinal);
        private readonly List<string> _combatLogLines = new List<string>(64);
        private Transform _unitsContainer;

        private bool _isInCombat;
        private bool _isCampActive;
        private bool _wasShopOpen;
        private int _selectedPartyHeroIndex;

        private GridCoord _lastExploreHoverCoord = new GridCoord(-1, -1);
        private GridCoord _lastExploreHoverOrigin = new GridCoord(-1, -1);
        private string _lastExploreHoverMapId = string.Empty;
        private bool _lastExploreHoverHasBoat;
        private bool _lastExploreHoverPathFound;
        private int _lastExploreHoverStepsCount;
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
        private float _speedMultiplier = 1f;
        private Coroutine _exploreWalkCoroutine;
        private Coroutine _aiCoroutine;
        private Coroutine _hitStopCoroutine;
        private bool _outcomeShown;
        private bool _freezeHoverForShowcase;
        private Vector2 _showcaseMouseAnchor;
        private string _activeEncounterZoneKey = string.Empty;

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

            if (worldRenderer != null)
            {
                worldRenderer.Initialize(assetCatalog);
            }

            if (gridOverlays != null)
            {
                gridOverlays.Initialize(assetCatalog);
                gridOverlays.SetElevationResolver(worldRenderer != null ? (Func<int, int, float>)worldRenderer.GetTileElevation : null);
            }

            if (hudController != null)
            {
                hudController.BuildUi(assetCatalog);
                WireHudEvents();
            }

            audioDirector?.EnsureInitialized();
            ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);

            EnterExplorationView(immediateCamera: true);
            hudController?.ShowToast("Welcome to City of Bywater! Left-click or WASD to explore.", 3.5f);
        }

        private void OnDisable()
        {
            StopExploreWalkCoroutine();
            StopAiCoroutine();
            StopHitStopAndRestoreTimeScale();
        }

        private void OnDestroy()
        {
            UnsubscribeCombatEvents();
            StopHitStopAndRestoreTimeScale();
        }

        private void Update()
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            SyncExternalModalCloses();
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

            _activeEncounterZoneKey = string.Empty;
            List<MonsterDefinition> monsters = SelectSkirmishMonsters(ExploreSession.CurrentMap.IsDungeon);
            StartTacticalCombat(monsters, "Bywater Skirmish");
        }

        public void ForceCompleteWalkForTest()
        {
            StopExploreWalkCoroutine();

            if (ExploreSession == null || _isInCombat || _activeMoveSteps.Count == 0)
            {
                _activeMoveSteps.Clear();
                _activeMoveStepIndex = 0;
                return;
            }

            while (_activeMoveStepIndex < _activeMoveSteps.Count && !_isInCombat && ExploreSession != null)
            {
                GridCoord nextStep = _activeMoveSteps[_activeMoveStepIndex++];
                bool halted = ExecuteSingleExploreStep(nextStep, animate: false, out ExplorationStepResult stepRes);
                if (halted)
                {
                    _activeMoveSteps.Clear();
                    _activeMoveStepIndex = 0;
                    HandleExplorationStepHalt(stepRes);
                    return;
                }
            }

            _activeMoveSteps.Clear();
            _activeMoveStepIndex = 0;
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
                    uView.SetWorldPosition(GridToWorldAtElevation(LocalToWorld(unit.Position)));
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
                cameraRig.SetFollowTarget(GridToWorldAtElevation(mid) + new Vector3(1.1f, 0f, -0.1f), immediate: true);
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

            ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
            EnterExplorationView(immediateCamera: true);

            GridCoord previewTarget = new GridCoord(10, 13);
            if (!ExploreSession.CurrentMap.IsPassable(previewTarget.X, previewTarget.Y, ExploreSession.Party.HasBoat))
            {
                previewTarget = new GridCoord(10, 12);
            }

            _lastExploreHoverCoord = new GridCoord(-1, -1);
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

            if (audioDirector == null)
            {
                audioDirector = GetComponentInChildren<BywaterAudioDirector>();
                if (audioDirector == null)
                {
                    audioDirector = FindFirstObjectByType<BywaterAudioDirector>();
                }

                if (audioDirector == null)
                {
                    GameObject audioGo = new GameObject("BywaterAudioDirector");
                    audioGo.transform.SetParent(transform, false);
                    audioDirector = audioGo.AddComponent<BywaterAudioDirector>();
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
            hudController.OnSkirmishRequested += () =>
            {
                audioDirector?.PlayUiClick();
                TriggerDemoSkirmish();
            };
            hudController.OnTogglePartyRequested += () =>
            {
                audioDirector?.PlayUiClick();
                hudController.TogglePartyPanel();
            };
            hudController.OnToggleShopRequested += HandleToggleShop;
            hudController.OnCampButtonRequested += EnterCampMode;
            hudController.OnShrineRequested += HandleQuickShrine;
            hudController.OnWarpPortalRequested += HandleQuickWarpPortal;
            hudController.OnSaveRequested += HandleSaveGame;
            hudController.OnLoadRequested += HandleLoadGame;

            hudController.OnHeroCardClicked += idx =>
            {
                audioDirector?.PlayUiClick();
                _selectedPartyHeroIndex = idx;
                hudController.TogglePartyPanel(true);
                hudController.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
            };
            hudController.OnPartyHeroSelected += idx =>
            {
                audioDirector?.PlayUiClick();
                _selectedPartyHeroIndex = idx;
                hudController.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
            };
            hudController.OnUnequipRequested += HandleUnequip;
            hudController.OnEquipFromStashRequested += HandleEquipFromStash;
            hudController.OnUseStashItemRequested += HandleUseStashItem;
            hudController.OnCampModeRequested += HandleCampRest;

            hudController.OnShopSelected += shopId =>
            {
                audioDirector?.PlayUiClick();
                hudController.RefreshShopModal(ExploreSession.Party, Bundle);
            };
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

            hudController.OnContinueAfterVictoryRequested += () =>
            {
                audioDirector?.PlayUiClick();
                ExitCombatToExploration(syncPartyFromCombat: true);
            };
            hudController.OnRetrySkirmishRequested += HandleRetrySkirmish;
            hudController.OnReviveAtShrineRequested += HandleReviveAtShrine;
        }

        private void SyncExternalModalCloses()
        {
            bool shopOpen = hudController != null && hudController.IsShopOpen;
            if (_wasShopOpen && !shopOpen && !_isInCombat && !_isCampActive)
            {
                PlayExploreMapMusic();
            }
            _wasShopOpen = shopOpen;

            if (_isCampActive && (hudController == null || !hudController.IsPartyOpen))
            {
                ExitCampMode();
            }
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
                    if (!_isInCombat)
                    {
                        PlayExploreMapMusic();
                    }
                }
                else if (hudController != null && hudController.IsPartyOpen)
                {
                    hudController.TogglePartyPanel(false);
                    if (_isCampActive)
                    {
                        ExitCampMode();
                    }
                }
                else if (_isInCombat && _activeCombatMode != CombatActionMode.Move)
                {
                    HandleCombatModeSelected(CombatActionMode.Move);
                }
                return;
            }

            if (_isInCombat)
            {
                if (kb.vKey.wasPressedThisFrame &&
                    CombatSession != null &&
                    CombatSession.Outcome == CombatOutcome.InProgress)
                {
                    HandleCombatModeSelected(CombatActionMode.Shove);
                }
                return;
            }

            if (kb.pKey.wasPressedThisFrame)
            {
                audioDirector?.PlayUiClick();
                hudController?.TogglePartyPanel();
                return;
            }
            if (kb.bKey.wasPressedThisFrame)
            {
                HandleToggleShop();
                return;
            }
            if (kb.cKey.wasPressedThisFrame)
            {
                if (_isCampActive)
                {
                    hudController?.TogglePartyPanel(false);
                    ExitCampMode();
                }
                else
                {
                    EnterCampMode();
                }
                return;
            }
            if (kb.f5Key.wasPressedThisFrame)
            {
                HandleSaveGame();
                return;
            }
            if (kb.f9Key.wasPressedThisFrame)
            {
                HandleLoadGame();
                return;
            }

            if (hudController != null && hudController.IsModalOpen)
            {
                return;
            }

            int dx = 0;
            int dy = 0;
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
            {
                dy -= 1;
            }
            else if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)
            {
                dy += 1;
            }

            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
            {
                dx -= 1;
            }
            else if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
            {
                dx += 1;
            }

            if (dx != 0 || dy != 0)
            {
                if (_isCampActive)
                {
                    ExitCampMode();
                }

                StopExploreWalkCoroutine();
                GridCoord nextTile = new GridCoord(ExploreSession.Position.X + dx, ExploreSession.Position.Y + dy);
                if (ExecuteSingleExploreStep(nextTile, animate: true, out ExplorationStepResult stepRes))
                {
                    HandleExplorationStepHalt(stepRes);
                }
            }
        }

        private void EnterExplorationView(bool immediateCamera)
        {
            _isInCombat = false;
            _outcomeShown = false;
            _isCampActive = false;
            StopExploreWalkCoroutine();

            if (worldRenderer != null)
            {
                worldRenderer.SetCampfireActive(false, Vector3.zero);
                worldRenderer.SetCombatArenaFrame(0, 0, 0, 0, false);
                worldRenderer.SetTerrainGridHint(0.0f);
                worldRenderer.RenderMap(ExploreSession.CurrentMap, ExploreSession.Position, forceRebuild: true);
            }

            if (gridOverlays != null)
            {
                gridOverlays.SetElevationResolver(worldRenderer != null ? (Func<int, int, float>)worldRenderer.GetTileElevation : null);
                gridOverlays.ClearAll();
            }

            if (cameraRig != null)
            {
                cameraRig.SetTerrainCollider(worldRenderer != null ? worldRenderer.TerrainCollider : null);
                if (ExploreSession.CurrentMap != null)
                {
                    cameraRig.SetWorldBounds(ExploreSession.CurrentMap.Width, ExploreSession.CurrentMap.Height);
                }
            }

            EnsureTrailHistoryAligned();
            EnsureExploreHeroViews();
            SyncExploreHeroViewsToTrail(immediate: true);

            Vector3 leaderWorldPos = GridToWorldAtElevation(ExploreSession.Position);
            if (worldRenderer != null)
            {
                worldRenderer.UpdateHeroWorldPosition(leaderWorldPos);
                worldRenderer.SetPartyTorchPosition(leaderWorldPos, ExploreSession.CurrentMap.IsDungeon);
            }

            biomeAtmosphere?.ApplyBiome(ExploreSession.CurrentMap.IsDungeon, immediate: true);
            biomeAtmosphere?.UpdateFocusPosition(leaderWorldPos);

            if (cameraRig != null)
            {
                cameraRig.SetCombatMode(false);
                cameraRig.SetFollowTarget(leaderWorldPos, immediateCamera);
            }

            PlayExploreMapMusic();

            _exploreHoverDirty = true;
            _lastExploreHoverCoord = new GridCoord(-1, -1);
            hudController?.HideOutcomeModal();
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
        }

        private void PlayExploreMapMusic()
        {
            if (audioDirector == null || ExploreSession?.CurrentMap == null)
            {
                return;
            }

            string suite = ExploreSession.CurrentMap.IsDungeon ? "dungeon" : "outdoor";
            audioDirector.PlayMusic(suite, 0.9f);
        }

        private void EnsureExploreHeroViews()
        {
            _exploreHeroViews.Clear();

            if (ExploreSession == null)
            {
                for (int i = 0; i < _exploreViewPool.Count; i++)
                {
                    if (_exploreViewPool[i] != null)
                    {
                        _exploreViewPool[i].gameObject.SetActive(false);
                    }
                }
                return;
            }

            TMP_FontAsset font = assetCatalog != null ? assetCatalog.uiFont : null;
            IReadOnlyList<PartyMemberState> members = ExploreSession.Party.Members;
            int livingViewIdx = 0;

            for (int i = 0; i < members.Count && livingViewIdx < MaxTrailingPartyViews; i++)
            {
                PartyMemberState member = members[i];
                if (!member.IsAlive)
                {
                    continue;
                }

                BywaterUnitView view;
                if (livingViewIdx < _exploreViewPool.Count && _exploreViewPool[livingViewIdx] != null)
                {
                    view = _exploreViewPool[livingViewIdx];
                }
                else
                {
                    GameObject go = new GameObject($"PartyHeroView_{livingViewIdx}");
                    go.transform.SetParent(_unitsContainer, false);
                    view = go.AddComponent<BywaterUnitView>();
                    if (livingViewIdx < _exploreViewPool.Count)
                    {
                        _exploreViewPool[livingViewIdx] = view;
                    }
                    else
                    {
                        _exploreViewPool.Add(view);
                    }
                }

                BywaterAssetCatalog.HeroArtEntry art = assetCatalog != null
                    ? assetCatalog.GetHeroArt(member.Id, member.PortraitId, member.Archetype)
                    : null;

                view.gameObject.SetActive(true);
                view.Configure(
                    member.Id,
                    member.Name,
                    art != null ? art.billboardMaterial : null,
                    HeroRingColor,
                    font,
                    spriteSize: 1.42f,
                    showHud: false);
                view.UpdateStats(member.Hp, member.MaxHp, member.Sp, member.MaxSp, string.Empty, livingViewIdx == 0, true);
                _exploreHeroViews.Add(view);
                livingViewIdx++;
            }

            for (int i = livingViewIdx; i < _exploreViewPool.Count; i++)
            {
                if (_exploreViewPool[i] != null)
                {
                    _exploreViewPool[i].gameObject.SetActive(false);
                }
            }
        }

        private void HideExploreHeroViews()
        {
            for (int i = 0; i < _exploreViewPool.Count; i++)
            {
                if (_exploreViewPool[i] != null)
                {
                    _exploreViewPool[i].gameObject.SetActive(false);
                }
            }
        }

        private void EnsureTrailHistoryAligned()
        {
            if (ExploreSession == null || ExploreSession.CurrentMap == null)
            {
                return;
            }

            if (_partyTrailHistory.Count == 0 ||
                _partyTrailHistory[0] != ExploreSession.Position)
            {
                ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
            }
        }

        private void ResetPartyTrailHistory(GridCoord leaderPos, TileMapData map, string facing)
        {
            _partyTrailHistory.Clear();
            _partyTrailHistory.Add(leaderPos);

            if (map == null)
            {
                for (int i = 1; i < MaxTrailingPartyViews; i++)
                {
                    _partyTrailHistory.Add(leaderPos);
                }
                return;
            }

            int backDx = 0;
            int backDy = -1;
            if (string.Equals(facing, "North", StringComparison.OrdinalIgnoreCase))
            {
                backDx = 0;
                backDy = 1;
            }
            else if (string.Equals(facing, "East", StringComparison.OrdinalIgnoreCase))
            {
                backDx = -1;
                backDy = 0;
            }
            else if (string.Equals(facing, "West", StringComparison.OrdinalIgnoreCase))
            {
                backDx = 1;
                backDy = 0;
            }

            bool hasBoat = ExploreSession != null && ExploreSession.Party.HasBoat;
            GridCoord cursor = leaderPos;
            for (int i = 1; i < MaxTrailingPartyViews; i++)
            {
                GridCoord candidate = new GridCoord(cursor.X + backDx, cursor.Y + backDy);
                if (map.InBounds(candidate) && map.IsPassable(candidate.X, candidate.Y, hasBoat))
                {
                    cursor = candidate;
                    _partyTrailHistory.Add(cursor);
                    continue;
                }

                bool foundFallback = false;
                for (int dy = -1; dy <= 1 && !foundFallback; dy++)
                {
                    for (int dx = -1; dx <= 1 && !foundFallback; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        GridCoord alt = new GridCoord(cursor.X + dx, cursor.Y + dy);
                        if (map.InBounds(alt) &&
                            map.IsPassable(alt.X, alt.Y, hasBoat) &&
                            !_partyTrailHistory.Contains(alt))
                        {
                            cursor = alt;
                            foundFallback = true;
                        }
                    }
                }

                _partyTrailHistory.Add(cursor);
            }
        }

        private void PushPartyTrailPosition(GridCoord newLeaderPos)
        {
            _partyTrailHistory.Insert(0, newLeaderPos);
            while (_partyTrailHistory.Count > MaxTrailingPartyViews)
            {
                _partyTrailHistory.RemoveAt(_partyTrailHistory.Count - 1);
            }
        }

        private GridCoord ResolveTrailTileForFollower(int followerIndex, TileMapData map, bool hasBoat, GridCoord safeFallback)
        {
            if (followerIndex >= 0 && followerIndex < _partyTrailHistory.Count)
            {
                GridCoord candidate = _partyTrailHistory[followerIndex];
                if (map != null && map.InBounds(candidate) && map.IsPassable(candidate.X, candidate.Y, hasBoat))
                {
                    return candidate;
                }
            }

            return safeFallback;
        }

        private void SyncExploreHeroViewsToTrail(bool immediate)
        {
            if (ExploreSession == null || _exploreHeroViews.Count == 0)
            {
                return;
            }

            TileMapData map = ExploreSession.CurrentMap;
            bool hasBoat = ExploreSession.Party.HasBoat;
            GridCoord leaderCoord = ExploreSession.Position;

            for (int k = 0; k < _exploreHeroViews.Count; k++)
            {
                BywaterUnitView view = _exploreHeroViews[k];
                if (view == null)
                {
                    continue;
                }

                GridCoord targetCoord = k == 0
                    ? leaderCoord
                    : ResolveTrailTileForFollower(k, map, hasBoat, leaderCoord);
                Vector3 worldPos = GridToWorldAtElevation(targetCoord);
                if (immediate || !Application.isPlaying)
                {
                    view.SetWorldPosition(worldPos);
                }
                else
                {
                    _singleStepWorldScratch[0] = worldPos;
                    view.MoveAlongWorldPath(_singleStepWorldScratch, ExploreStepDurationSeconds);
                }
            }
        }

        private void UpdateExplorationInput(GridCoord worldCoord, bool leftClick)
        {
            TileMapData map = ExploreSession.CurrentMap;
            if (!map.InBounds(worldCoord))
            {
                return;
            }

            UpdateExplorationHoverVisuals(worldCoord);

            if (!leftClick)
            {
                return;
            }

            if (_isCampActive)
            {
                ExitCampMode();
            }

            if (worldCoord == ExploreSession.Position)
            {
                return;
            }

            StopExploreWalkCoroutine();

            PathResult path = ExploreSession.PlanPath(worldCoord, allowDiagonal: true);
            if (!path.Found || path.Steps.Count == 0)
            {
                hudController?.ShowToast("Path blocked.", 1.6f);
                return;
            }

            _activeMoveSteps.Clear();
            for (int i = 0; i < path.Steps.Count; i++)
            {
                _activeMoveSteps.Add(path.Steps[i]);
            }
            _activeMoveStepIndex = 0;

            if (!Application.isPlaying || !gameObject.activeInHierarchy)
            {
                ForceCompleteWalkForTest();
            }
            else
            {
                _exploreWalkCoroutine = StartCoroutine(ExploreWalkRoutine());
            }
        }

        private IEnumerator ExploreWalkRoutine()
        {
            while (_activeMoveStepIndex < _activeMoveSteps.Count && !_isInCombat && ExploreSession != null)
            {
                GridCoord nextStep = _activeMoveSteps[_activeMoveStepIndex++];
                bool halted = ExecuteSingleExploreStep(nextStep, animate: true, out ExplorationStepResult stepRes);
                if (halted)
                {
                    if (stepRes != null && stepRes.Moved)
                    {
                        yield return new WaitForSeconds(ExploreStepDurationSeconds);
                    }

                    _activeMoveSteps.Clear();
                    _activeMoveStepIndex = 0;
                    _exploreWalkCoroutine = null;
                    HandleExplorationStepHalt(stepRes);
                    yield break;
                }

                yield return new WaitForSeconds(ExploreStepDurationSeconds);
            }

            _activeMoveSteps.Clear();
            _activeMoveStepIndex = 0;
            _exploreWalkCoroutine = null;
        }

        private bool ExecuteSingleExploreStep(GridCoord nextTile, bool animate, out ExplorationStepResult stepRes)
        {
            stepRes = null;
            if (ExploreSession == null || ExploreSession.CurrentMap == null)
            {
                return true;
            }

            TileMapData map = ExploreSession.CurrentMap;
            GridCoord prevPos = ExploreSession.Position;
            stepRes = ExploreSession.StepOneTile(nextTile, autoHaltOnEvents: true);
            if (!stepRes.Moved)
            {
                if (stepRes.HaltReason == ExplorationHaltReason.Blocked && nextTile != prevPos)
                {
                    hudController?.ShowToast("Path blocked.", 1.4f);
                }
                return true;
            }

            if (audioDirector != null &&
                string.Equals(audioDirector.CurrentMusicKey, "temple", StringComparison.OrdinalIgnoreCase))
            {
                PlayExploreMapMusic();
            }

            PushPartyTrailPosition(stepRes.Position);
            if (_exploreHeroViews.Count == 0)
            {
                EnsureExploreHeroViews();
            }

            Vector3 leaderPos = GridToWorldAtElevation(stepRes.Position);
            bool doAnimate = animate && Application.isPlaying && gameObject.activeInHierarchy;

            for (int k = 0; k < _exploreHeroViews.Count; k++)
            {
                BywaterUnitView view = _exploreHeroViews[k];
                if (view == null)
                {
                    continue;
                }

                GridCoord targetCoord = k == 0
                    ? stepRes.Position
                    : ResolveTrailTileForFollower(k, map, ExploreSession.Party.HasBoat, prevPos);
                Vector3 targetWorld = GridToWorldAtElevation(targetCoord);
                if (doAnimate)
                {
                    _singleStepWorldScratch[0] = targetWorld;
                    view.MoveAlongWorldPath(_singleStepWorldScratch, ExploreStepDurationSeconds);
                }
                else
                {
                    view.SetWorldPosition(targetWorld);
                }
            }

            TileRule tileRule = map.GetTileRule(map.GetTile(stepRes.Position.X, stepRes.Position.Y));
            audioDirector?.PlayFootstep(tileRule.SoundId, map.IsDungeon);

            worldRenderer?.UpdateFocusCenter(stepRes.Position);
            worldRenderer?.UpdateHeroWorldPosition(leaderPos);
            worldRenderer?.SetPartyTorchPosition(leaderPos, map.IsDungeon);
            biomeAtmosphere?.UpdateFocusPosition(leaderPos);
            cameraRig?.SetFollowTarget(leaderPos, immediate: !doAnimate);

            if (stepRes.TorchExpiredThisStep)
            {
                hudController?.ShowToast("Your torch has burned out!", 2.4f);
            }

            _exploreHoverDirty = true;
            hudController?.RefreshExploreHud(
                ExploreSession,
                Bundle,
                stepRes.Position,
                BywaterTileClassifier.Classify(map, stepRes.Position.X, stepRes.Position.Y),
                0,
                string.Empty,
                true);

            return stepRes.ShouldHalt;
        }

        private void StopExploreWalkCoroutine()
        {
            if (_exploreWalkCoroutine != null)
            {
                StopCoroutine(_exploreWalkCoroutine);
                _exploreWalkCoroutine = null;
            }
        }

        private void UpdateExplorationHoverVisuals(GridCoord hoverCoord)
        {
            TileMapData map = ExploreSession.CurrentMap;
            if (map == null || !map.InBounds(hoverCoord))
            {
                return;
            }

            GridCoord origin = ExploreSession.Position;
            string mapId = map.MapId ?? string.Empty;
            bool hasBoat = ExploreSession.Party.HasBoat;

            if (!_exploreHoverDirty &&
                hoverCoord == _lastExploreHoverCoord &&
                origin == _lastExploreHoverOrigin &&
                string.Equals(mapId, _lastExploreHoverMapId, StringComparison.Ordinal) &&
                hasBoat == _lastExploreHoverHasBoat)
            {
                return;
            }

            _lastExploreHoverCoord = hoverCoord;
            _lastExploreHoverOrigin = origin;
            _lastExploreHoverMapId = mapId;
            _lastExploreHoverHasBoat = hasBoat;
            _exploreHoverDirty = false;

            if (hoverCoord == origin)
            {
                _explorePreviewSteps.Clear();
                _lastExploreHoverPathFound = true;
                _lastExploreHoverStepsCount = 0;
            }
            else
            {
                _lastExploreHoverPathFound = _explorePathfinder.FindPathSteps(
                    map,
                    origin,
                    hoverCoord,
                    _explorePreviewSteps,
                    out _,
                    allowDiagonal: true,
                    hasBoat: hasBoat);
                _lastExploreHoverStepsCount = _lastExploreHoverPathFound ? _explorePreviewSteps.Count : 0;
            }

            bool reachable = hoverCoord == origin || _lastExploreHoverPathFound;
            gridOverlays?.SetExplorationHoverPath(_lastExploreHoverPathFound ? _explorePreviewSteps : null, hoverCoord, reachable);

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
                _lastExploreHoverStepsCount,
                poiHint,
                reachable);
        }

        private void HandleExplorationStepHalt(ExplorationStepResult res)
        {
            if (res == null)
            {
                return;
            }

            switch (res.HaltReason)
            {
                case ExplorationHaltReason.Portal:
                    if (ExploreSession.UsePortalAtCurrentTile())
                    {
                        ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
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
                    _activeEncounterZoneKey = (res.TriggeredZone != null && ExploreSession.CurrentMap != null)
                        ? ExplorationSession.GetEncounterZoneKey(ExploreSession.CurrentMap.MapId, res.TriggeredZone)
                        : string.Empty;
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
                audioDirector?.PlayMusic("shop", 0.7f);
                hudController?.ShowToast($"Entered {poi.Label}", 2.2f);
            }
            else if (kind == "temple")
            {
                HandleTempleHeal();
            }
            else if (kind == "tavern")
            {
                EnterCampMode();
                hudController?.ShowToast($"Arrived at {poi.Label} - Rest & Resupply", 2.4f);
            }
            else if (!string.IsNullOrEmpty(poi.Label))
            {
                hudController?.ShowToast(poi.Label, 2.2f);
            }
        }

        private void EnterCampMode()
        {
            if (_isInCombat || ExploreSession == null || ExploreSession.CurrentMap == null)
            {
                return;
            }

            StopExploreWalkCoroutine();
            _isCampActive = true;
            audioDirector?.PlayUiClick();

            hudController?.TogglePartyPanel(true);
            hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
            hudController?.ShowToast("Camp pitched - Rest & Resupply around the campfire.", 2.6f);

            EnsureExploreHeroViews();
            TileMapData map = ExploreSession.CurrentMap;
            Vector3 campCenter = GridToWorldAtElevation(ExploreSession.Position);
            worldRenderer?.SetCampfireActive(true, campCenter);

            int count = _exploreHeroViews.Count;
            for (int i = 0; i < count; i++)
            {
                BywaterUnitView view = _exploreHeroViews[i];
                if (view == null)
                {
                    continue;
                }

                float t = count > 1 ? i / (float)(count - 1) : 0.5f;
                float angle = Mathf.Lerp(160f, 20f, t) * Mathf.Deg2Rad;
                float wx = Mathf.Clamp(campCenter.x + Mathf.Cos(angle) * CampSemicircleRadius, 0.25f, map.Width - 0.25f);
                float wz = Mathf.Clamp(campCenter.z + Mathf.Sin(angle) * CampSemicircleRadius, -map.Height + 0.25f, -0.25f);

                CartographyWorldData.WorldToGrid(new Vector3(wx, 0f, wz), out int gx, out int gy);
                gx = Mathf.Clamp(gx, 0, map.Width - 1);
                gy = Mathf.Clamp(gy, 0, map.Height - 1);
                float elev = worldRenderer != null ? worldRenderer.GetTileElevation(gx, gy) : 0f;
                Vector3 slotPos = new Vector3(wx, elev, wz);

                if (Application.isPlaying && gameObject.activeInHierarchy)
                {
                    _singleStepWorldScratch[0] = slotPos;
                    view.MoveAlongWorldPath(_singleStepWorldScratch, 0.16f);
                }
                else
                {
                    view.SetWorldPosition(slotPos);
                }
            }

            audioDirector?.PlayMusic("camp", 0.8f);
        }

        private void ExitCampMode()
        {
            if (!_isCampActive)
            {
                return;
            }

            _isCampActive = false;
            worldRenderer?.SetCampfireActive(false, Vector3.zero);
            if (!_isInCombat)
            {
                SyncExploreHeroViewsToTrail(immediate: false);
                PlayExploreMapMusic();
            }
        }

        private void HandleToggleShop()
        {
            if (hudController == null)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            if (_isCampActive)
            {
                ExitCampMode();
            }

            if (hudController.IsShopOpen)
            {
                hudController.CloseShopModal();
                if (!_isInCombat)
                {
                    PlayExploreMapMusic();
                }
            }
            else
            {
                hudController.OpenShop(hudController.ActiveShopId, ExploreSession.Party, Bundle);
                audioDirector?.PlayMusic("shop", 0.7f);
            }
        }

        private void HandleQuickShrine()
        {
            if (_isInCombat || ExploreSession == null)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            if (_isCampActive)
            {
                ExitCampMode();
            }
            StopExploreWalkCoroutine();

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
            ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
            EnterExplorationView(immediateCamera: true);
            HandleTempleHeal();
        }

        private void HandleQuickWarpPortal()
        {
            if (_isInCombat || ExploreSession == null)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            if (_isCampActive)
            {
                ExitCampMode();
            }
            StopExploreWalkCoroutine();

            if (ExploreSession.CurrentMap.Portals.Count == 0)
            {
                hudController?.ShowToast("No portal on current map.", 2.0f);
                return;
            }

            MapPortal portal = ExploreSession.CurrentMap.Portals[0];
            ExploreSession.MoveAlongPath(portal.Coord, autoHaltOnEvents: false);
            if (ExploreSession.UsePortalAtCurrentTile())
            {
                ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
                EnterExplorationView(immediateCamera: true);
                string destName = ExploreSession.CurrentMap.IsDungeon ? "Bywater Cavern (Dungeon 0)" : "City of Bywater";
                hudController?.ShowToast($"Warped to {destName} ({ExploreSession.Position.X}, {ExploreSession.Position.Y})", 2.8f);
            }
            else
            {
                ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
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

            audioDirector?.PlayUiClick();
            BywaterSaveData snapshot = BywaterSaveService.CaptureSnapshot(ExploreSession, _combatLogLines);
            bool ok = BywaterSaveService.TryWriteSave(SaveFilePath, snapshot);
            if (ok)
            {
                Debug.Log("[BywaterGameController] Saved snapshot to " + SaveFilePath);
            }
            hudController?.ShowToast(ok ? "Game saved (F5)." : "Failed to save game.", 2.4f);
        }

        private void HandleLoadGame()
        {
            if (_isInCombat || Bundle == null)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            if (!BywaterSaveService.TryLoadSave(SaveFilePath, out BywaterSaveData data, out string status))
            {
                hudController?.ShowToast(status, 2.6f);
                return;
            }

            string mapId = string.IsNullOrWhiteSpace(data.mapId) ? "land_0" : data.mapId;
            if (!Bundle.Maps.TryGetValue(mapId, out TileMapData targetMap))
            {
                hudController?.ShowToast("Saved map not found in bundle: " + mapId, 2.6f);
                return;
            }

            StopExploreWalkCoroutine();
            if (_isCampActive)
            {
                _isCampActive = false;
                worldRenderer?.SetCampfireActive(false, Vector3.zero);
            }

            GridCoord startPos = targetMap.InBounds(data.x, data.y)
                ? new GridCoord(data.x, data.y)
                : targetMap.StartSpawn;

            PartyState party = PartyState.FromBundle(Bundle);
            ExplorationSession restored = new ExplorationSession(
                Bundle.Maps,
                targetMap.MapId,
                party,
                new DeterministicDiceRoller(1UL),
                startPosition: startPos,
                initialFacing: data.facing,
                initialTurnCount: data.turnCount,
                clearedEncounterIds: data.clearedEncounterIds);

            BywaterSaveService.ApplySnapshot(data, restored, Bundle.Items, out ulong restoredSeed);
            _roller = new DeterministicDiceRoller(restoredSeed);

            ExploreSession = new ExplorationSession(
                Bundle.Maps,
                targetMap.MapId,
                restored.Party,
                _roller,
                startPosition: startPos,
                initialFacing: data.facing,
                initialTurnCount: restored.TurnCount,
                clearedEncounterIds: restored.ClearedEncounterIds);

            _combatLogLines.Clear();
            if (data.combatLogLines != null)
            {
                for (int i = 0; i < data.combatLogLines.Length; i++)
                {
                    _combatLogLines.Add(data.combatLogLines[i] ?? string.Empty);
                }
            }

            ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
            EnterExplorationView(immediateCamera: true);
            hudController?.ShowToast($"Loaded save at {targetMap.MapId} ({startPos.X}, {startPos.Y}).", 2.8f);
        }

        private void HandleUnequip(int heroIdx, EquipmentSlot slot)
        {
            audioDirector?.PlayUiClick();
            if (ExploreSession.Party.UnequipToStash(heroIdx, slot, Bundle.Items))
            {
                hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            }
        }

        private void HandleEquipFromStash(int heroIdx, int stashIdx)
        {
            audioDirector?.PlayUiClick();
            if (ExploreSession.Party.EquipFromStash(heroIdx, stashIdx, Bundle.Items))
            {
                hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                hudController?.ShowToast("Equipped item.", 1.6f);
            }
        }

        private void HandleUseStashItem(int heroIdx, int stashIdx)
        {
            audioDirector?.PlayUiClick();
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

                audioDirector?.PlaySpell("heal");
                hudController?.RefreshPartyDrawer(party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                hudController?.ShowToast($"{hero.Name} used {def.Name} (+{hero.Hp - before} HP).", 2.2f);
            }
        }

        private void HandleCampRest(CampMode mode)
        {
            audioDirector?.PlayUiClick();
            RestResult res = ExploreSession.Party.Rest(mode);
            audioDirector?.PlaySpell("heal");
            EnsureExploreHeroViews();
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
                audioDirector?.PlayCoin();
                hudController?.RefreshShopModal(ExploreSession.Party, Bundle);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                if (Bundle.Items.TryGetValue(itemId, out ItemDefinition def))
                {
                    hudController?.ShowToast($"Purchased {def.Name}.", 1.8f);
                }
            }
            else
            {
                audioDirector?.PlayUiClick();
                hudController?.ShowToast("Cannot buy item (insufficient gold).", 1.8f);
            }
        }

        private void HandleSellItem(int stashIdx)
        {
            if (TownShopService.SellItem(ExploreSession.Party, stashIdx, 1, Bundle.Items))
            {
                audioDirector?.PlayCoin();
                hudController?.RefreshShopModal(ExploreSession.Party, Bundle);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            }
        }

        private void HandleSellAllJunk()
        {
            JunkSaleResult res = TownShopService.SellCommonJunk(ExploreSession.Party, Bundle.Items);
            if (res.ItemsSold > 0)
            {
                audioDirector?.PlayCoin();
            }
            else
            {
                audioDirector?.PlayUiClick();
            }

            hudController?.RefreshShopModal(ExploreSession.Party, Bundle);
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            hudController?.ShowToast(res.ItemsSold > 0 ? $"Sold {res.ItemsSold} junk item(s) for {res.GoldEarned}g." : "No junk items in stash.", 2.2f);
        }

        private void HandleIdentifyAll()
        {
            audioDirector?.PlayUiClick();
            IdentifyResult res = TownShopService.IdentifyAll(ExploreSession.Party, 10);
            if (res.ItemsIdentified > 0)
            {
                audioDirector?.PlayCoin();
            }
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
                audioDirector?.PlayUiClick();
                hudController?.ShowToast("Bywater Shrine: All party members are already at full health.", 2.4f);
                return;
            }

            if (ExploreSession.TriggerTempleHeal(25))
            {
                audioDirector?.PlayMusic("temple", 0.6f);
                audioDirector?.PlaySpell("heal");
                EnsureExploreHeroViews();
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                if (hudController != null && hudController.IsShopOpen)
                {
                    hudController.RefreshShopModal(ExploreSession.Party, Bundle);
                }
                hudController?.ShowToast("Bywater Shrine blessing restored full HP and cleared ailments (-25g).", 2.8f);
            }
            else
            {
                audioDirector?.PlayUiClick();
                hudController?.ShowToast("Not enough gold for Shrine blessing (25g required).", 2.4f);
            }
        }

        private void StartTacticalCombat(List<MonsterDefinition> monstersToSpawn, string title)
        {
            if (monstersToSpawn == null || monstersToSpawn.Count == 0)
            {
                monstersToSpawn = SelectSkirmishMonsters(ExploreSession.CurrentMap.IsDungeon);
            }

            StopExploreWalkCoroutine();
            if (_isCampActive)
            {
                _isCampActive = false;
                worldRenderer?.SetCampfireActive(false, Vector3.zero);
            }

            StopAiCoroutine();
            UnsubscribeCombatEvents();
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
                ExploreSession.Party.HasBoat,
                elevationResolver: (wx, wy) => ElevationMetersToTier(worldRenderer != null ? worldRenderer.GetTileElevation(wx, wy) : 0f),
                surfaceResolver: (wx, wy) => ResolveInitialCombatSurface(map, wx, wy));

            GridCoord localCenter = new GridCoord(
                Mathf.Clamp(partyPos.X - _arenaOriginX, 0, _arenaWidth - 1),
                Mathf.Clamp(partyPos.Y - _arenaOriginY, 0, _arenaHeight - 1));

            List<GridCoord> connectedWalkable = CollectConnectedWalkableTiles(grid, localCenter);
            if (connectedWalkable.Count < 16)
            {
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

            GridCoord[] preferredMonsterSlots =
            {
                new GridCoord(7, 2),
                new GridCoord(8, 4),
                new GridCoord(7, 6),
                new GridCoord(9, 3),
                new GridCoord(8, 6),
                new GridCoord(9, 5)
            };

            bool spawnedGiantFrog = false;
            for (int i = 0; i < monstersToSpawn.Count; i++)
            {
                MonsterDefinition def = monstersToSpawn[i];
                if (IsGiantFrog(def.Id, def.IconId))
                {
                    spawnedGiantFrog = true;
                }

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
            CombatSession.SurfaceChanged += OnCombatSurfaceChanged;

            _isInCombat = true;
            _outcomeShown = false;
            _activeCombatMode = CombatActionMode.Move;
            _selectedSpellId = -1;
            _combatHoverDirty = true;
            _lastCombatHover = new GridCoord(-1, -1);

            HideExploreHeroViews();

            GridCoord mid = new GridCoord(_arenaOriginX + _arenaWidth / 2, _arenaOriginY + _arenaHeight / 2);
            Vector3 midWorld = GridToWorldAtElevation(mid);

            if (worldRenderer != null)
            {
                worldRenderer.UpdateFocusCenter(mid);
                worldRenderer.SetCombatArenaFrame(_arenaOriginX, _arenaOriginY, _arenaWidth, _arenaHeight, true);
                worldRenderer.SetTerrainGridHint(0.08f);
                worldRenderer.UpdateHeroWorldPosition(midWorld);
                worldRenderer.SetPartyTorchPosition(midWorld, map.IsDungeon);
            }

            if (gridOverlays != null)
            {
                gridOverlays.SetElevationResolver(worldRenderer != null ? (Func<int, int, float>)worldRenderer.GetTileElevation : null);
            }

            biomeAtmosphere?.ApplyBiome(map.IsDungeon, immediate: true);
            biomeAtmosphere?.UpdateFocusPosition(midWorld);

            if (cameraRig != null)
            {
                cameraRig.SetTerrainCollider(worldRenderer != null ? worldRenderer.TerrainCollider : null);
                cameraRig.SetWorldBounds(map.Width, map.Height);
                cameraRig.SetCombatMode(true);
                cameraRig.SetFollowTarget(midWorld + new Vector3(1.1f, 0f, -0.1f), immediate: true);
            }

            SpawnCombatUnitViews();
            hudController?.CloseShopModal();
            hudController?.TogglePartyPanel(false);
            hudController?.HideOutcomeModal();
            hudController?.ShowToast($"{title} started!", 2.4f);

            audioDirector?.PlayMusic("battle", 0.7f);
            if (spawnedGiantFrog)
            {
                audioDirector?.PlayFrogCroak();
            }

            BeginCurrentCombatTurn();
        }

        private void OnCombatSurfaceChanged(IReadOnlyList<SurfaceChangeEvent> changes)
        {
            if (gridOverlays == null || CombatSession == null)
            {
                return;
            }

            gridOverlays.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
        }

        private void UnsubscribeCombatEvents()
        {
            if (CombatSession != null)
            {
                CombatSession.SurfaceChanged -= OnCombatSurfaceChanged;
            }
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
                view.SetWorldPosition(GridToWorldAtElevation(LocalToWorld(c.Position)));
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
                string badge = c.IsProne
                    ? "PRONE"
                    : (conds.Count > 0 ? conds[0].DisplayName : (c.IsGuarding ? "GUARD" : string.Empty));
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
            Vector3 actorWorld = GridToWorldAtElevation(LocalToWorld(actor.Position));
            cameraRig?.SetFollowTarget(actorWorld, immediate: false);
            worldRenderer?.UpdateHeroWorldPosition(actorWorld);

            if (actor.Side == CombatSide.Monster)
            {
                gridOverlays?.ClearAll();
                gridOverlays?.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
                RefreshEnemyIntentArcs();
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

                if (IsGiantFrog(monster.Id, monster.IconId))
                {
                    audioDirector?.PlayFrogCroak();
                }

                GridCoord oldPos = monster.Position;
                Dictionary<string, int> oldHps = CaptureCombatantHps();

                CombatActionResult res = CombatSession.ExecuteMonsterAiTurn();

                if (monster.Position != oldPos && _combatUnitViews.TryGetValue(monster.Id, out BywaterUnitView mView) && mView != null)
                {
                    Vector3 targetWorld = GridToWorldAtElevation(LocalToWorld(monster.Position));
                    if (delay <= 0.001f)
                    {
                        mView.SetWorldPosition(targetWorld);
                    }
                    else
                    {
                        _singleStepWorldScratch[0] = targetWorld;
                        mView.MoveAlongWorldPath(_singleStepWorldScratch, Mathf.Min(0.12f, delay * 0.5f));
                    }
                }

                ApplyMonsterActionJuice(monster, res, oldHps);
                ShowHpDeltaFloatingText(oldHps);
                SyncAllCombatUnitViews();
                gridOverlays?.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
                RefreshEnemyIntentArcs();
                RefreshCombatHudState();

                if (CombatSession.Outcome != CombatOutcome.InProgress)
                {
                    break;
                }

                CombatSession.EndTurn();
                SyncAllCombatUnitViews();
                RefreshEnemyIntentArcs();
                RefreshCombatHudState();
            }

            _aiCoroutine = null;
            BeginCurrentCombatTurn();
        }

        private void ApplyMonsterActionJuice(CombatantState monster, CombatActionResult res, Dictionary<string, int> oldHps)
        {
            if (monster == null || res == null || CombatSession == null)
            {
                return;
            }

            CombatantState hitHero = null;
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (c.Side == CombatSide.Hero && oldHps.TryGetValue(c.Id, out int before) && c.Hp < before)
                {
                    hitHero = c;
                    if (_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView hView) && hView != null)
                    {
                        hView.PlayHitFlash(0.055f);
                    }
                }
            }

            if (hitHero != null)
            {
                if (_combatUnitViews.TryGetValue(monster.Id, out BywaterUnitView mView) && mView != null)
                {
                    mView.PlayAttackLunge(GridToWorldAtElevation(LocalToWorld(hitHero.Position)), 0.28f, 0.07f);
                }

                bool isCrit = false;
                for (int i = 0; i < res.AttackOutcomes.Count; i++)
                {
                    if (res.AttackOutcomes[i].Kind == AttackOutcomeKind.CriticalHit)
                    {
                        isCrit = true;
                        break;
                    }
                }

                bool isRanged = monster.PrefersRanged && monster.Position.ChebyshevDistance(hitHero.Position) > 1;
                audioDirector?.PlayAttack(isRanged, isCrit);
                cameraRig?.PlayImpactImpulse(isCrit ? 0.18f : 0.12f, 0.14f);
                TriggerHitStop(isCrit ? 0.060f : 0.045f);
            }
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
            gridOverlays.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));

            if (actor.Economy.MoveRemaining > 0)
            {
                IReadOnlyList<ReachableTileInfo> localReach = CombatSession.GetReachableTiles(actor.Id);
                gridOverlays.SetCombatReachable(OffsetReachableTiles(localReach));
            }

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
            RefreshEnemyIntentArcs();
        }

        private void RefreshEnemyIntentArcs()
        {
            if (gridOverlays == null || CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
            {
                gridOverlays?.ClearEnemyIntentArcs();
                return;
            }

            List<EnemyIntentArcData> arcs = new List<EnemyIntentArcData>(8);
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (!c.IsAlive || !c.IsConscious || c.Side != CombatSide.Monster)
                {
                    continue;
                }

                EnemyIntentPrediction intent = CombatSession.PredictNextIntent(c.Id);
                if (intent == null ||
                    intent.ActionKind == CombatIntentActionType.Hold ||
                    intent.ActionKind == CombatIntentActionType.Guard ||
                    intent.ActionKind == CombatIntentActionType.Flee)
                {
                    continue;
                }

                GridCoord dest = intent.TargetCoord;
                if (!string.IsNullOrEmpty(intent.TargetId))
                {
                    CombatantState targetUnit = CombatSession.GetCombatant(intent.TargetId);
                    if (targetUnit != null && targetUnit.IsAlive)
                    {
                        dest = targetUnit.Position;
                    }
                }

                if (dest == c.Position)
                {
                    continue;
                }

                bool isSpell = intent.ActionKind == CombatIntentActionType.CastSpell ||
                               intent.ActionKind == CombatIntentActionType.MoveAndCast;
                arcs.Add(new EnemyIntentArcData
                {
                    From = LocalToWorld(c.Position),
                    To = LocalToWorld(dest),
                    IsSpell = isSpell
                });
            }

            gridOverlays.ShowEnemyIntentArcs(arcs);
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

            gridOverlays?.SetCombatHoverPath(null, false);
            gridOverlays?.SetLosPreview(null, default, default);
            gridOverlays?.SetSpellAoeFootprint(null, null, null, false);
            gridOverlays?.SetFlankIndicator(null, null);
            gridOverlays?.ClearShovePreview();

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

                case CombatActionMode.Shove:
                {
                    if (hoveredUnit != null && hoveredUnit.Id != actor.Id)
                    {
                        TacticalShovePreview preview = CombatSession.PreviewShove(actor.Id, hoveredUnit.Id);
                        gridOverlays?.ShowShovePreview(
                            LocalToWorld(actor.Position),
                            LocalToWorld(hoveredUnit.Position),
                            OffsetShovePreview(preview));

                        string badge = ResolveShoveBadge(preview);
                        string chanceLine = preview.IsAllyShove
                            ? "ALLY DISENGAGE (100% safe push)"
                            : $"Push Chance: {preview.SuccessChancePct}%";
                        string hazardLine = string.Empty;
                        if (preview.PushesOffLedge)
                        {
                            hazardLine = $"\nLEDGE FALL +1d6/tier ({preview.FallElevationDrop} tier drop, Prone)";
                        }
                        else if (preview.HitsWallOrBounds || !string.IsNullOrEmpty(preview.CollidingUnitId))
                        {
                            hazardLine = "\nWALL CRUSH (1d4 crush damage)";
                        }

                        string surfaceLine = preview.DestinationSurface != SurfaceType.None
                            ? $"\nSurface Hazard: {preview.DestinationSurface}"
                            : string.Empty;
                        string statusLine = preview.CanExecute ? "SWIFT ACTION: Click to shove." : $"Blocked: {preview.Reason}";

                        hudController?.SetCombatHoverPreview(
                            $"Shove: {hoveredUnit.Name}",
                            $"{chanceLine}{hazardLine}{surfaceLine}\n\n{statusLine}",
                            badge);
                    }
                    else
                    {
                        hudController?.SetCombatHoverPreview(
                            "Shove [V] Mode",
                            GetIdleModeInstructions(CombatActionMode.Shove),
                            null);
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
                            if (atkRes.Success)
                            {
                                PlayAttackMicroJuice(actor, clickedUnit, atkRes, isRanged: false, oldHps, out string critId);
                                FinishPlayerCombatAction(atkRes, oldHps, critId);
                            }
                            else
                            {
                                hudController?.ShowToast(atkRes.Message, 2.0f);
                            }
                            return;
                        }
                    }

                    GridCoord oldPos = actor.Position;
                    CombatActionResult moveRes = CombatSession.TryMoveTo(localCoord);
                    if (moveRes.Success)
                    {
                        Vector3 destWorld = GridToWorldAtElevation(LocalToWorld(actor.Position));
                        if (actor.Position != oldPos && _combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView view) && view != null)
                        {
                            _singleStepWorldScratch[0] = destWorld;
                            view.MoveAlongWorldPath(_singleStepWorldScratch, 0.09f);
                        }

                        TileMapData map = ExploreSession.CurrentMap;
                        GridCoord worldStep = LocalToWorld(actor.Position);
                        TileRule rule = map.GetTileRule(map.GetTile(worldStep.X, worldStep.Y));
                        audioDirector?.PlayFootstep(rule.SoundId, map.IsDungeon);
                        worldRenderer?.UpdateHeroWorldPosition(destWorld);

                        if (moveRes.AoOTriggered && _combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView hitView) && hitView != null)
                        {
                            hitView.PlayHitFlash(0.055f);
                            hitView.ShowArcingFloatingText($"AoO -{moveRes.DamageDealt}!", BywaterUiHelpers.DangerRed, isCrit: false);
                            cameraRig?.PlayImpactImpulse(0.12f, 0.14f);
                        }

                        if (actor.IsProne && _combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView proneView) && proneView != null)
                        {
                            proneView.ShowArcingFloatingText("PRONE", BywaterUiHelpers.BrassHighlight, isCrit: false);
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
                    if (res.Success)
                    {
                        PlayAttackMicroJuice(actor, clickedUnit, res, isRanged: false, oldHps, out string critId);
                        FinishPlayerCombatAction(res, oldHps, critId);
                    }
                    else
                    {
                        hudController?.ShowToast(res.Message, 2.0f);
                    }
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
                    if (res.Success)
                    {
                        PlayAttackMicroJuice(actor, clickedUnit, res, isRanged: true, oldHps, out string critId);
                        FinishPlayerCombatAction(res, oldHps, critId);
                    }
                    else
                    {
                        hudController?.ShowToast(res.Message, 2.0f);
                    }
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
                    if (res.Success)
                    {
                        SpellDefinition spell = Bundle.Spells.TryGetValue(_selectedSpellId, out SpellDefinition s) ? s : null;
                        audioDirector?.PlaySpell(spell?.EffectKind);
                        if (_combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView casterView) && casterView != null)
                        {
                            casterView.PlayAttackLunge(GridToWorldAtElevation(LocalToWorld(localCoord)), 0.28f, 0.07f);
                        }

                        bool anyDamaged = false;
                        for (int i = 0; i < CombatSession.Combatants.Count; i++)
                        {
                            CombatantState c = CombatSession.Combatants[i];
                            if (oldHps.TryGetValue(c.Id, out int before) && c.Hp < before)
                            {
                                anyDamaged = true;
                                if (_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView v) && v != null)
                                {
                                    v.PlayHitFlash(0.055f);
                                }
                            }
                        }

                        if (anyDamaged)
                        {
                            cameraRig?.PlayImpactImpulse(0.15f, 0.16f);
                            TriggerHitStop(0.045f);
                        }
                        else if (res.Message.IndexOf("Immune:", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                 clickedUnit != null &&
                                 _combatUnitViews.TryGetValue(clickedUnit.Id, out BywaterUnitView immView) &&
                                 immView != null)
                        {
                            immView.ShowArcingFloatingText("IMMUNE", BywaterUiHelpers.InfoCyan, isCrit: false);
                        }

                        FinishPlayerCombatAction(res, oldHps);
                    }
                    else
                    {
                        hudController?.ShowToast(res.Message, 2.0f);
                    }
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
                    if (res.Success)
                    {
                        audioDirector?.PlaySpell("heal");
                    }
                    FinishPlayerCombatAction(res, oldHps);
                    break;
                }

                case CombatActionMode.Shove:
                {
                    if (clickedUnit == null || clickedUnit.Id == actor.Id)
                    {
                        hudController?.ShowToast("Click an adjacent unit to shove.", 1.6f);
                        return;
                    }

                    GridCoord prevTargetPos = clickedUnit.Position;
                    bool wasProne = clickedUnit.IsProne;

                    CombatActionResult res = CombatSession.TryShove(clickedUnit.Id);
                    if (!res.Success)
                    {
                        hudController?.ShowToast(res.Message, 2.0f);
                        return;
                    }

                    audioDirector?.PlayShove();
                    if (_combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView attView) && attView != null)
                    {
                        attView.PlayAttackLunge(GridToWorldAtElevation(LocalToWorld(prevTargetPos)), 0.28f, 0.07f);
                    }

                    if (_combatUnitViews.TryGetValue(clickedUnit.Id, out BywaterUnitView tgtView) && tgtView != null)
                    {
                        if (clickedUnit.Position != prevTargetPos)
                        {
                            _singleStepWorldScratch[0] = GridToWorldAtElevation(LocalToWorld(clickedUnit.Position));
                            tgtView.MoveAlongWorldPath(_singleStepWorldScratch, 0.10f);
                            tgtView.ShowArcingFloatingText("SHOVED", BywaterUiHelpers.BrassHighlight, isCrit: false);
                        }

                        if (!wasProne && clickedUnit.IsProne)
                        {
                            tgtView.ShowArcingFloatingText("PRONE", BywaterUiHelpers.DangerRed, isCrit: false);
                        }
                    }

                    if (res.DamageDealt > 0)
                    {
                        for (int i = 0; i < CombatSession.Combatants.Count; i++)
                        {
                            CombatantState c = CombatSession.Combatants[i];
                            if (oldHps.TryGetValue(c.Id, out int before) && c.Hp < before &&
                                _combatUnitViews.TryGetValue(c.Id, out BywaterUnitView dmgView) && dmgView != null)
                            {
                                dmgView.PlayHitFlash(0.055f);
                            }
                        }

                        cameraRig?.PlayImpactImpulse(0.14f, 0.16f);
                        TriggerHitStop(0.045f);
                    }

                    FinishPlayerCombatAction(res, oldHps);
                    break;
                }
            }
        }

        private void PlayAttackMicroJuice(
            CombatantState attacker,
            CombatantState defender,
            CombatActionResult res,
            bool isRanged,
            Dictionary<string, int> oldHps,
            out string critTargetId)
        {
            critTargetId = string.Empty;
            if (attacker == null || defender == null || res == null)
            {
                return;
            }

            bool isCrit = false;
            for (int i = 0; i < res.AttackOutcomes.Count; i++)
            {
                if (res.AttackOutcomes[i].Kind == AttackOutcomeKind.CriticalHit)
                {
                    isCrit = true;
                    break;
                }
            }

            if (_combatUnitViews.TryGetValue(attacker.Id, out BywaterUnitView attView) && attView != null)
            {
                attView.PlayAttackLunge(GridToWorldAtElevation(LocalToWorld(defender.Position)), 0.28f, 0.07f);
            }

            audioDirector?.PlayAttack(isRanged, isCrit);

            if (_combatUnitViews.TryGetValue(defender.Id, out BywaterUnitView defView) && defView != null)
            {
                if (res.DamageDealt > 0)
                {
                    defView.PlayHitFlash(0.055f);
                    if (isCrit)
                    {
                        critTargetId = defender.Id;
                        int delta = oldHps.TryGetValue(defender.Id, out int before) ? Math.Max(1, before - defender.Hp) : res.DamageDealt;
                        defView.ShowArcingFloatingText($"CRIT -{delta}!", BywaterUiHelpers.BrassHighlight, isCrit: true);
                    }

                    cameraRig?.PlayImpactImpulse(isCrit ? 0.20f : 0.13f, isCrit ? 0.18f : 0.14f);
                    TriggerHitStop(isCrit ? 0.060f : 0.045f);
                }
                else
                {
                    string label = res.Message.IndexOf("immune", StringComparison.OrdinalIgnoreCase) >= 0 ? "IMMUNE" : "MISS";
                    Color col = label == "IMMUNE" ? BywaterUiHelpers.InfoCyan : BywaterUiHelpers.MutedSlate;
                    defView.ShowArcingFloatingText(label, col, isCrit: false);
                }
            }
        }

        private void FinishPlayerCombatAction(CombatActionResult res, Dictionary<string, int> oldHps, string skipCritTargetId = null)
        {
            if (!res.Success)
            {
                hudController?.ShowToast(res.Message, 2.0f);
                return;
            }

            ShowHpDeltaFloatingText(oldHps, skipCritTargetId);
            SyncAllCombatUnitViews();

            if (CombatSession.Outcome != CombatOutcome.InProgress)
            {
                gridOverlays?.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
                RefreshCombatHudState();
                HandleCombatOutcome();
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor != null)
            {
                if (!actor.Economy.HasStandardAction &&
                    _activeCombatMode != CombatActionMode.Shove &&
                    _activeCombatMode != CombatActionMode.Potion &&
                    actor.Economy.MoveRemaining > 0)
                {
                    _activeCombatMode = CombatActionMode.Move;
                }
                else if (_activeCombatMode == CombatActionMode.Shove && !actor.Economy.HasSwiftAction)
                {
                    _activeCombatMode = actor.Economy.MoveRemaining > 0 ? CombatActionMode.Move : CombatActionMode.Melee;
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

            audioDirector?.PlayUiClick();
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
            audioDirector?.PlayUiClick();
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

            audioDirector?.PlayUiClick();
            CombatantState actor = CombatSession.CurrentActor;
            if (CombatSession.UndoMove() && actor != null)
            {
                Vector3 pos = GridToWorldAtElevation(LocalToWorld(actor.Position));
                if (_combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView view) && view != null)
                {
                    view.SetWorldPosition(pos);
                }

                worldRenderer?.UpdateHeroWorldPosition(pos);
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

            audioDirector?.PlayUiClick();
            CombatantState actor = CombatSession.CurrentActor;
            CombatActionResult res = CombatSession.GuardOverwatch();
            if (res.Success && actor != null)
            {
                if (_combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView view) && view != null)
                {
                    view.ShowArcingFloatingText("GUARD +4 AC", BywaterUiHelpers.InfoCyan, isCrit: false);
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

            audioDirector?.PlayUiClick();
            CombatSession.EndTurn();
            BeginCurrentCombatTurn();
        }

        private void HandleCycleSpeed()
        {
            audioDirector?.PlayUiClick();
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
            CaptureRecentCombatLogLines();

            if (CombatSession.Outcome == CombatOutcome.Victory)
            {
                if (!string.IsNullOrEmpty(_activeEncounterZoneKey))
                {
                    ExploreSession?.MarkEncounterCleared(_activeEncounterZoneKey);
                    _activeEncounterZoneKey = string.Empty;
                }

                ExploreSession?.SyncAfterCombat(CombatSession, floorDeadToOne: true);

                List<string> lootNames = new List<string>(CombatSession.CollectedLootItemIds.Count);
                for (int i = 0; i < CombatSession.CollectedLootItemIds.Count; i++)
                {
                    int itemId = CombatSession.CollectedLootItemIds[i];
                    if (Bundle != null && Bundle.Items != null && Bundle.Items.TryGetValue(itemId, out ItemDefinition def))
                    {
                        lootNames.Add(def.Name);
                    }
                    else
                    {
                        lootNames.Add($"Item #{itemId}");
                    }
                }

                audioDirector?.PlayMusic("treasure", 0.7f);
                audioDirector?.PlayCoin();
                hudController?.ShowOutcomeModal(
                    CombatOutcome.Victory,
                    CombatSession.TotalXpEarned,
                    CombatSession.VictoryLevelsGained,
                    CombatSession.TotalGoldLoot,
                    lootNames);
            }
            else
            {
                _activeEncounterZoneKey = string.Empty;
                hudController?.ShowOutcomeModal(CombatSession.Outcome, 0, 0, 0, null);
            }
        }

        private void CaptureRecentCombatLogLines()
        {
            if (CombatSession == null || CombatSession.Log.Count == 0)
            {
                return;
            }

            _combatLogLines.Clear();
            int start = Math.Max(0, CombatSession.Log.Count - MaxSavedLogLines);
            for (int i = start; i < CombatSession.Log.Count; i++)
            {
                CombatLogEntry entry = CombatSession.Log[i];
                _combatLogLines.Add($"[R{entry.RoundNumber}] {entry.Message}");
            }
        }

        private void ExitCombatToExploration(bool syncPartyFromCombat)
        {
            StopAiCoroutine();
            StopHitStopAndRestoreTimeScale();

            if (syncPartyFromCombat && CombatSession != null)
            {
                ExploreSession?.SyncAfterCombat(CombatSession, floorDeadToOne: true);
            }

            UnsubscribeCombatEvents();
            ClearCombatUnitViews();
            CombatSession = null;
            _activeEncounterZoneKey = string.Empty;
            EnterExplorationView(immediateCamera: true);
        }

        private void HandleRetrySkirmish()
        {
            audioDirector?.PlayUiClick();
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
            audioDirector?.PlayUiClick();
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
                ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
                EnterExplorationView(immediateCamera: true);
            }
            else
            {
                ExploreSession.MoveAlongPath(new GridCoord(10, 16), autoHaltOnEvents: false);
                ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
                EnterExplorationView(immediateCamera: true);
            }

            audioDirector?.PlaySpell("heal");
            hudController?.ShowToast("Revived at Bywater Shrine with full health and spell points.", 3.0f);
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

            string elevationBadge = null;
            if (preview.ElevationModifierPct > 0)
            {
                elevationBadge = "[HIGH GROUND +15% / +1 RNG]";
            }
            else if (preview.ElevationModifierPct < 0)
            {
                elevationBadge = "[LOW GROUND -15%]";
            }

            hudController?.SetCombatHoverPreview(
                title,
                $"Hit%: {preview.HitChancePct}% (Uncapped {preview.UncappedHitScore}%)\n" +
                $"Dmg: {preview.MinDamage} - {preview.MaxDamage}   |   Defender AC: {preview.DefenderAc}\n" +
                $"Crit%: {preview.CritChancePct}%   |   Graze%: {preview.GrazeChancePct}%\n" +
                losLine + flankLine + aooLine +
                $"Tags: {tags}\n\n{actionHint}",
                elevationBadge);
        }

        private static string ResolveShoveBadge(TacticalShovePreview preview)
        {
            if (preview == null)
            {
                return null;
            }

            if (preview.PushesOffLedge)
            {
                return "LEDGE FALL +1d6/tier";
            }

            if (preview.HitsWallOrBounds || !string.IsNullOrEmpty(preview.CollidingUnitId))
            {
                return "WALL CRUSH";
            }

            if (preview.IsAllyShove)
            {
                return "ALLY DISENGAGE";
            }

            return null;
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

        private void TriggerHitStop(float durationSeconds)
        {
            if (!Application.isPlaying || Application.isBatchMode || !gameObject.activeInHierarchy || durationSeconds <= 0.001f)
            {
                return;
            }

            StopHitStopAndRestoreTimeScale();
            _hitStopCoroutine = StartCoroutine(HitStopRoutine(durationSeconds));
        }

        private IEnumerator HitStopRoutine(float durationSeconds)
        {
            try
            {
                Time.timeScale = 0.05f;
                yield return new WaitForSecondsRealtime(durationSeconds);
            }
            finally
            {
                Time.timeScale = _speedMultiplier;
                _hitStopCoroutine = null;
            }
        }

        private void StopHitStopAndRestoreTimeScale()
        {
            if (_hitStopCoroutine != null)
            {
                StopCoroutine(_hitStopCoroutine);
                _hitStopCoroutine = null;
            }

            Time.timeScale = _speedMultiplier;
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

        private void ShowHpDeltaFloatingText(Dictionary<string, int> oldHps, string skipCritTargetId = null)
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

                if (!string.IsNullOrEmpty(skipCritTargetId) && string.Equals(c.Id, skipCritTargetId, StringComparison.Ordinal))
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
                    view.ShowArcingFloatingText($"{delta}", BywaterUiHelpers.DangerRed, isCrit: false);
                }
                else
                {
                    view.ShowArcingFloatingText($"+{delta}", BywaterUiHelpers.SuccessGreen, isCrit: false);
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

        private List<CombatGridCell> OffsetSurfaceCells(IReadOnlyList<CombatGridCell> localCells)
        {
            if (localCells == null || localCells.Count == 0)
            {
                return new List<CombatGridCell>(0);
            }

            List<CombatGridCell> worldCells = new List<CombatGridCell>(localCells.Count);
            for (int i = 0; i < localCells.Count; i++)
            {
                CombatGridCell cell = localCells[i];
                if (cell == null || cell.Surface == SurfaceType.None)
                {
                    continue;
                }

                worldCells.Add(new CombatGridCell(
                    _arenaOriginX + cell.X,
                    _arenaOriginY + cell.Y,
                    cell.IsBlocked,
                    cell.BlocksLos,
                    cell.ProvidesCover,
                    cell.MoveCost,
                    cell.ElevationTier,
                    cell.Surface,
                    cell.SurfaceRoundsRemaining));
            }

            return worldCells;
        }

        private TacticalShovePreview OffsetShovePreview(TacticalShovePreview preview)
        {
            if (preview == null)
            {
                return null;
            }

            return new TacticalShovePreview(
                preview.CanExecute,
                preview.Reason,
                preview.SuccessChancePct,
                preview.PushDirection,
                LocalToWorld(preview.Destination),
                preview.HitsWallOrBounds,
                preview.CollidingUnitId,
                preview.PushesOffLedge,
                preview.FallElevationDrop,
                preview.DestinationSurface,
                preview.IsAllyShove);
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
            return new LosResult(los.Kind, los.CoverPenaltyPct, blocker, worldCells, los.IsSteamObscured);
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
                blocked,
                fp.HasLineOfSight);
        }

        private static short ElevationMetersToTier(float meters)
        {
            if (meters <= -0.35f) return -1;
            if (meters < 0.35f) return 0;
            if (meters < 0.70f) return 1;
            if (meters < 1.35f) return 2;
            return 3;
        }

        private SurfaceType ResolveInitialCombatSurface(TileMapData map, int wx, int wy)
        {
            if (map == null || !map.InBounds(wx, wy))
            {
                return SurfaceType.None;
            }

            BywaterTileKind kind = BywaterTileClassifier.Classify(map, wx, wy);
            TileRule rule = map.GetTileRule(map.GetTile(wx, wy));
            if (kind == BywaterTileKind.ShallowWater || rule.NeedsBoat)
            {
                return SurfaceType.Water;
            }

            if (worldRenderer != null &&
                worldRenderer.CartographyData != null &&
                worldRenderer.CartographyData.InBounds(wx, wy) &&
                worldRenderer.CartographyData.GetTile(wx, wy).IsWater)
            {
                return SurfaceType.Water;
            }

            return SurfaceType.None;
        }

        private static bool IsGiantFrog(string monsterId, int iconId)
        {
            return iconId == 392 ||
                   (!string.IsNullOrEmpty(monsterId) &&
                    monsterId.IndexOf("giant_frog", StringComparison.OrdinalIgnoreCase) >= 0);
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
                    copy.SetElevation(x, y, source.GetElevation(x, y));
                    SurfaceType surf = source.GetSurface(x, y);
                    if (surf != SurfaceType.None)
                    {
                        copy.SetSurface(x, y, surf, source.GetSurfaceRoundsRemaining(x, y));
                    }

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
                    return "Hover an adjacent enemy to inspect Hit%, Crit%, Graze%, Damage, High Ground, and Flanking bonus. Click to strike.";
                case CombatActionMode.Ranged:
                    return "Hover a distant enemy to inspect Line-of-Sight ray, Cover penalty, High Ground, and Hit%. Click to shoot.";
                case CombatActionMode.Spell:
                    return "Pick a spell above the action bar, hover the arena to preview AoE footprint & Save%, then click to cast.";
                case CombatActionMode.Potion:
                    return "Click any ally (or yourself) to drink a Swift healing potion.";
                case CombatActionMode.Shove:
                    return "Hover an adjacent unit to preview push direction, ledge fall, or wall crush. Click to shove (Swift action).";
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

        private Vector3 GridToWorldAtElevation(GridCoord worldCoord, float yOffset = 0f)
        {
            float elev = worldRenderer != null ? worldRenderer.GetTileElevation(worldCoord.X, worldCoord.Y) : 0f;
            return CartographyWorldData.GridToWorld(worldCoord, elev + yOffset);
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
