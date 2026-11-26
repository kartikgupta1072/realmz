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
using UnityEngine.Audio;
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
        private static readonly float[] SpeedDelays = { 0.42f, 0.18f, 0.0f };
        private static readonly float[] CombatMoveStepDurations = { 0.25f, 0.125f, 0.0f };
        private static readonly float[] CombatAttackDurations = { 0.38f, 0.19f, 0.0f };
        private static readonly string[] SpeedLabels = { "1x", "2x", "Instant" };
        private const float ExploreStepDurationSeconds = 0.085f;
        private const float CampSemicircleRadius = 1.15f;
        private const int MaxTrailingPartyViews = 6;
        private const int MaxSavedLogLines = 40;
        private const float EnemyIntentFlashSeconds = 0.30f;
        private const float RightClickMaxDragPixelsSq = 36f;

        [Header("Scene References")]
        public BywaterAssetCatalog assetCatalog;
        public BywaterWorldRenderer worldRenderer;
        public BywaterCameraRig cameraRig;
        public BywaterGridOverlays gridOverlays;
        public BywaterHudController hudController;
        public BywaterBiomeAtmosphere biomeAtmosphere;
        public BywaterAudioDirector audioDirector;

        [Header("Stage 2 Assets")]
        public AudioMixer masterMixer;
        public RealmzBalanceCatalog balanceCatalog;

        private bool _isMasterMixerBound;
        private float _lastAppliedNormalizedTime = -1f;
        private int _lastSyncedClockMinute = -1;
        private readonly HashSet<GridCoord> _destroyedArenaPropCoords = new HashSet<GridCoord>();
        private string _hoveredRimUnitId;
        private bool _hasHoverInteractable;
        private string _cachedAmbientMapId = string.Empty;
        private readonly List<Vector3> _cachedRiverWorldPositions = new List<Vector3>(128);
        private readonly List<Vector3> _cachedForgeWorldPositions = new List<Vector3>(8);
        private readonly List<Vector3> _cachedFireWorldPositions = new List<Vector3>(32);

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
        private CombatActionMode _activeCombatMode = CombatActionMode.Smart;
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
        private EncounterZoneData _activeEncounterZone;
        private CombatInitiativeSurpriseMode _activeCombatSurpriseMode;
        private int _activeCombatRewardBonusPct;
        private EncounterScoutingReport _pendingScoutingReport;
        private bool _hasAppliedLightingState;
        private WorldTimePhase _lastLightingPhase;
        private bool _lastLightingDungeon;
        private bool _lastLightingTorch;
        private bool _lastLightingCombat;
        private TacticalCombatSession _pendingQuickResolveVictorySession;

        private bool _autoCombatEnabled;
        private bool _autoEndTurn;
        private float _masterVolume = 1.0f;
        private float _musicVolume = 0.55f;
        private float _sfxVolume = 0.85f;
        private bool _isMuted;
        private float _gridOpacity = 1.0f;
        private bool _nameplatesAlwaysOn;
        private bool _devToolsVisible;
        private bool _showAllEnemyIntentArcs;
        private bool _isHoveringHotbarUi;
        private string _hoveredCombatUnitId;
        private bool _lastAltHeld;
        private string _lastContextPoiLabel = string.Empty;
        private GridCoord _preCombatExplorePos = new GridCoord(-1, -1);
        private bool _hasPreCombatExplorePos;
        private List<MonsterDefinition> _pendingQuickResolveMonsters;
        private CombatArenaArchetype? _forcedCombatArenaArchetype;
        private string _pendingQuickResolveTitle = string.Empty;
        private string _pendingQuickResolveZoneKey = string.Empty;
        private EncounterZoneData _pendingQuickResolveZone;
        private bool _rightMouseDownPendingClick;
        private Vector2 _rightMouseDownPos;
        private readonly List<Vector3> _treeFadePositionsScratch = new List<Vector3>(16);
        private readonly List<BywaterUnitView> _nameplateStaggerViewsScratch = new List<BywaterUnitView>(16);
        private bool _wasTempleOpen;
        private CartographyNpcPlacement? _pendingNpcInteraction;
        private MapPointOfInterest _pendingPoiInteraction;
        private readonly List<Vector3> _combatPathWorldScratch = new List<Vector3>(16);
        private readonly List<GridCoord> _aiPathStepsScratch = new List<GridCoord>(32);
        private readonly AStarPathfinder _combatAiPathfinder = new AStarPathfinder(16, 16);
        private float _keyboardStepCooldown;
        private int _lastHeldMoveDx;
        private int _lastHeldMoveDy;
        private int _bufferedExploreDx;
        private int _bufferedExploreDy;
        private bool _hasBufferedExploreStep;
        private EventSystem _cachedEventSystem;
        private bool _hasCachedMouseRay;
        private Vector2 _cachedRayMousePos;
        private Vector3 _cachedRayCamPos;
        private Quaternion _cachedRayCamRot;
        private bool _cachedRayCombat;
        private bool _cachedRayValid;
        private GridCoord _cachedRayGridCoord;
        private float _combatPendingEndTurnWaitUntil;
        private Coroutine _delayedTurnRoutine;
        private string _selectedDeploymentHeroId;
        private int _lastCombatRoundForTorch;
        private int _lastSyncedTorchMinute = -1;

        public bool IsInCombat => _isInCombat;
        public int ActiveExploreViewCount => _exploreHeroViews.Count;
        public IReadOnlyList<BywaterUnitView> ExploreHeroViews => _exploreHeroViews;
        public bool ShowAllEnemyIntentArcs => _showAllEnemyIntentArcs;
        public GridCoord ArenaOrigin => new GridCoord(_arenaOriginX, _arenaOriginY);
        public int SpeedIndex => _speedIndex;
        public CombatActionMode ActiveCombatMode => _activeCombatMode;
        public int SelectedSpellId => _selectedSpellId;
        public float GetCombatMoveTileDuration() => CombatMoveStepDurations[Mathf.Clamp(_speedIndex, 0, CombatMoveStepDurations.Length - 1)];
        public float GetCombatAttackDuration() => CombatAttackDurations[Mathf.Clamp(_speedIndex, 0, CombatAttackDurations.Length - 1)];
        public ExplorationSession ExploreSession { get; private set; }
        public TacticalCombatSession CombatSession { get; private set; }
        public BywaterM1Bundle Bundle { get; private set; }

        public void SelectCombatModeForUi(CombatActionMode mode) => HandleCombatModeSelected(mode);
        public void SelectSpellForUi(int spellId) => HandleSpellSelected(spellId);
        public void CancelCombatTargetingMode()
        {
            if (_isInCombat && _activeCombatMode != CombatActionMode.Smart)
            {
                HandleCombatModeSelected(CombatActionMode.Smart);
            }
        }
        public void ClickCombatCoordForTest(GridCoord localCoord)
        {
            if (!_isInCombat || CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
            {
                return;
            }
            if (CombatSession.Phase == CombatPhase.Deployment)
            {
                HandleDeploymentClick(localCoord);
                return;
            }
            CombatantState actor = CombatSession.CurrentActor;
            if (actor != null && actor.Side == CombatSide.Hero && CombatSession.Grid.InBounds(localCoord))
            {
                ExecuteCombatClick(actor, localCoord);
            }
        }
        public void StartBattleFromDeploymentForTest() => HandleStartBattleFromDeployment();
        public void EscapeKeyForTest() => HandleEscapeKey();

        public void InitializeForTests(BywaterM1Bundle bundle, ExplorationSession exploreSession, ulong seed = 12345UL)
        {
            ResolveSceneReferences();
            if (cameraRig != null)
            {
                cameraRig.SetHudController(hudController);
            }
            Bundle = bundle;
            ExploreSession = exploreSession;
            _roller = new DeterministicDiceRoller(seed == 0UL ? 1UL : seed);
        }

        private string SaveFilePath => Path.Combine(Application.persistentDataPath, "bywater_save.json");

        /// <summary>
        /// Uses REALMZ_RNG_SEED when set (reproducible playtests and bug repros); otherwise a clock-based seed.
        /// </summary>
        private static ulong ResolveStartupSeed()
        {
            string raw = Environment.GetEnvironmentVariable("REALMZ_RNG_SEED");
            if (!string.IsNullOrWhiteSpace(raw) && ulong.TryParse(raw.Trim(), out ulong envSeed))
            {
                Debug.Log($"[BywaterGameController] Using REALMZ_RNG_SEED={envSeed}");
                return envSeed == 0UL ? 1UL : envSeed;
            }
            return (ulong)(DateTime.UtcNow.Ticks & 0x7FFFFFFFFFFFFFFFL) | 1UL;
        }

        /// <summary>Replaces the dice roller used by subsequently created sessions (tests only).</summary>
        public void SetRollerSeedForTest(ulong seed)
        {
            _roller = new DeterministicDiceRoller(seed == 0UL ? 1UL : seed);
        }

        private void Awake()
        {
            ResolveSceneReferences();
            if (cameraRig != null)
            {
                cameraRig.SetHudController(hudController);
            }
        }

        private void Start()
        {
            ResolveSceneReferences();

            _roller = new DeterministicDiceRoller(ResolveStartupSeed());

            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            Bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            PartyState party = PartyState.FromBundle(Bundle);
            BalanceOverrides activeOverrides = Bundle.BalanceOverrides ??
                                               (balanceCatalog != null ? balanceCatalog.ApplyToBalanceOverrides() : null);
            ExploreSession = new ExplorationSession(Bundle.Maps, "land_0", party, _roller, balanceOverrides: activeOverrides);

            if (worldRenderer != null)
            {
                worldRenderer.Initialize(assetCatalog);
            }

            if (gridOverlays != null)
            {
                gridOverlays.Initialize(assetCatalog);
                gridOverlays.SetElevationResolver(worldRenderer != null ? (Func<int, int, float>)worldRenderer.GetTileElevation : null);
                gridOverlays.SetGridOpacity(_gridOpacity);
            }

            audioDirector?.EnsureInitialized();
            if (masterMixer != null && audioDirector != null && !_isMasterMixerBound)
            {
                audioDirector.BindMixer(masterMixer);
                _isMasterMixerBound = true;
            }
            ApplyAudioSettings();

            if (hudController != null)
            {
                hudController.BuildUi(assetCatalog);
                WireHudEvents();
                hudController.SetDevToolsVisible(_devToolsVisible);
                SyncHudSettings();
            }

            if (cameraRig != null)
            {
                cameraRig.SetHudController(hudController);
            }

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
            TickContinuousOverlandClock();

            Mouse mouse = Mouse.current;
            bool isModalOpen = hudController != null && hudController.IsModalOpen;
            if (_cachedEventSystem == null)
            {
                _cachedEventSystem = EventSystem.current;
            }

            if (!isModalOpen && mouse != null)
            {
                // Bug #5: Allow right-click targeting cancel anywhere on screen, including over HUD/hotbar.
                HandleCombatRightClickCancel(mouse, mouse.position.ReadValue());
            }

            bool pointerOverUi = (hudController != null && hudController.IsPointerOverUi()) ||
                                 (_cachedEventSystem != null && _cachedEventSystem.IsPointerOverGameObject());
            if (isModalOpen || pointerOverUi)
            {
                if (isModalOpen || !pointerOverUi)
                {
                    _isHoveringHotbarUi = false;
                }

                if (!isModalOpen && hudController != null && hudController.IsCursorTooltipActive && mouse != null)
                {
                    hudController.UpdateCursorTooltipPosition(mouse.position.ReadValue());
                }
                else if (isModalOpen)
                {
                    hudController?.ClearCombatHoverPreview();
                    ClearExploreInteractableHover();
                    if (!_isInCombat)
                    {
                        gridOverlays?.SetExplorationHoverPath(null, new GridCoord(-1, -1), false);
                        _exploreHoverDirty = true;
                    }
                    _rightMouseDownPendingClick = false;
                }

                return;
            }

            _isHoveringHotbarUi = false;

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

            Transform camTf = cameraRig.transform;
            Vector3 camPos = camTf.position;
            Quaternion camRot = camTf.rotation;
            bool needRaycast = leftClick ||
                               !_hasCachedMouseRay ||
                               _cachedRayCombat != _isInCombat ||
                               (mousePos - _cachedRayMousePos).sqrMagnitude > 0.25f ||
                               (camPos - _cachedRayCamPos).sqrMagnitude > 0.00001f ||
                               Quaternion.Angle(camRot, _cachedRayCamRot) > 0.01f;

            GridCoord worldCoord;
            bool rayHit;
            bool deploymentEmptyTilePriority = false;
            if (_isInCombat &&
                CombatSession != null &&
                CombatSession.Phase == CombatPhase.Deployment &&
                !string.IsNullOrEmpty(_selectedDeploymentHeroId) &&
                cameraRig.ScreenPointToGridCoord(mousePos, out GridCoord deployRayWorld))
            {
                GridCoord deployLocal = WorldToLocal(deployRayWorld);
                if (CombatSession.IsValidDeploymentTile(deployLocal) && GetLivingUnitAt(deployLocal) == null)
                {
                    rayHit = true;
                    worldCoord = deployRayWorld;
                    deploymentEmptyTilePriority = true;
                }
                else if (TryPickCombatUnitUnderScreenPoint(mousePos, out GridCoord pickedUnitWorld))
                {
                    rayHit = true;
                    worldCoord = pickedUnitWorld;
                    deploymentEmptyTilePriority = true;
                }
                else
                {
                    rayHit = false;
                    worldCoord = default;
                }
            }
            else if (_isInCombat && TryPickCombatUnitUnderScreenPoint(mousePos, out GridCoord pickedUnitWorld))
            {
                rayHit = true;
                worldCoord = pickedUnitWorld;
            }
            else if (needRaycast && !deploymentEmptyTilePriority)
            {
                rayHit = cameraRig.ScreenPointToGridCoord(mousePos, out worldCoord);
                _hasCachedMouseRay = true;
                _cachedRayMousePos = mousePos;
                _cachedRayCamPos = camPos;
                _cachedRayCamRot = camRot;
                _cachedRayCombat = _isInCombat;
                _cachedRayValid = rayHit;
                _cachedRayGridCoord = worldCoord;
            }
            else
            {
                rayHit = _cachedRayValid;
                worldCoord = _cachedRayGridCoord;
            }

            if (!rayHit)
            {
                if (_isInCombat && !_isHoveringHotbarUi)
                {
                    ClearCombatTransientHoverVisuals();
                }
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

        private void LateUpdate()
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            UpdateActiveTreeOcclusionFade();

            if (_isInCombat && CombatSession != null)
            {
                Keyboard kb = Keyboard.current;
                bool altHeld = kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
                if (altHeld || altHeld != _lastAltHeld)
                {
                    _lastAltHeld = altHeld;
                    RefreshUnitNameplates();
                }
            }
        }

        public void TriggerDemoSkirmish()
        {
            TriggerDemoSkirmish(enterDeployment: false);
        }

        public void TriggerDemoSkirmish(bool enterDeployment)
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            _activeEncounterZone = null;
            _activeEncounterZoneKey = string.Empty;
            _activeCombatSurpriseMode = CombatInitiativeSurpriseMode.None;
            _activeCombatRewardBonusPct = 0;
            List<MonsterDefinition> monsters = SelectSkirmishMonsters(ExploreSession.CurrentMap.IsDungeon);
            StartTacticalCombat(monsters, "Bywater Skirmish", CombatInitiativeSurpriseMode.None, 0, startInDeployment: enterDeployment);
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
            TryCompletePendingNpcInteraction();
        }

        public void StageCombatOverlayShowcaseForCapture()
        {
            StageCombatArenaArchetypeShowcaseForCapture(
                CombatArenaArchetype.RuinsOutskirts,
                new GridCoord(18, 54));
        }

        public void StageCombatArenaArchetypeShowcaseForCapture(
            CombatArenaArchetype archetype,
            GridCoord partyPos)
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            if (_isInCombat)
            {
                ExitCombatToExploration(syncPartyFromCombat: false);
            }

            string targetMap = archetype == CombatArenaArchetype.DungeonChamber ? "dungeon_0" : "land_0";
            ExploreSession = new ExplorationSession(
                Bundle.Maps,
                targetMap,
                ExploreSession.Party,
                _roller,
                partyPos);
            _hasAppliedLightingState = false;
            worldRenderer?.RenderMap(ExploreSession.CurrentMap, ExploreSession.Position, forceRebuild: true);

            _forcedCombatArenaArchetype = archetype;
            try
            {
                TriggerDemoSkirmish();
            }
            finally
            {
                _forcedCombatArenaArchetype = null;
            }

            if (CombatSession == null)
            {
                return;
            }

            StopAiCoroutine();

            RefreshCombatStaticOverlays(CombatSession.CurrentActor);

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

            _activeCombatMode = CombatActionMode.Smart;
            _selectedSpellId = current.SpellIds.Count > 0 ? current.SpellIds[0] : -1;
            _hoveredCombatUnitId = null;

            RefreshCombatStaticOverlays(current);
            gridOverlays?.ClearEnemyIntentArcs();

            CollectActiveUnitWorldPositions(_treeFadePositionsScratch);
            if (cameraRig != null)
            {
                cameraRig.FrameCombatantsInSafeViewport(_treeFadePositionsScratch, immediate: true);
            }
            worldRenderer?.UpdateTreeOcclusionFade(_treeFadePositionsScratch, fadeCenterBuildingsInCombat: true);

            GridCoord bestHoverTile = FindBestShowcaseHoverTile(current);
            if (CombatSession.Grid.InBounds(bestHoverTile) &&
                CombatSession.Grid.GetSurface(bestHoverTile.X, bestHoverTile.Y) == SurfaceType.None)
            {
                UpdateCombatHoverVisuals(bestHoverTile);
            }
            else
            {
                gridOverlays?.SetCombatHoverPath(null, false);
            }

            gridOverlays?.ClearEnemyIntentArcs();
            hudController?.ClearCombatHoverPreview();
            RefreshUnitNameplates();

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

            hudController?.CloseAllModals();
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

        public void StageShopShowcase(string shopId = "1")
        {
            if (ExploreSession == null || Bundle == null || hudController == null)
            {
                return;
            }

            if (_isInCombat)
            {
                ExitCombatToExploration(syncPartyFromCombat: false);
            }

            if (_isCampActive)
            {
                ExitCampMode();
            }

            hudController.CloseAllModals();
            if (ExploreSession.CurrentMap.IsDungeon)
            {
                ExploreSession = new ExplorationSession(Bundle.Maps, "land_0", ExploreSession.Party, _roller, new GridCoord(7, 11));
            }
            else
            {
                ExploreSession.MoveAlongPath(new GridCoord(7, 11), autoHaltOnEvents: false);
            }

            ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
            EnterExplorationView(immediateCamera: true);
            hudController.OpenShop(shopId, ExploreSession.Party, Bundle);
            RefreshNpcPrompts();
        }

        public void StagePauseMenuShowcase()
        {
            if (ExploreSession == null || Bundle == null || hudController == null)
            {
                return;
            }

            WarpToOverworldTownShowcase();
            hudController.CloseAllModals();
            SyncHudSettings();
            hudController.ToggleSettingsModal(true);
            RefreshNpcPrompts();
        }

        public void WarpToDungeonShowcase()
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
            hudController?.CloseSettingsModal();
            hudController?.HideQuickResolveModal();
            hudController?.HideOutcomeModal();
            hudController?.HideToast();

            ExploreSession = new ExplorationSession(
                Bundle.Maps,
                "dungeon_0",
                ExploreSession.Party,
                _roller,
                new GridCoord(33, 72));

            ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
            EnterExplorationView(immediateCamera: true);

            _showcaseMouseAnchor = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            _freezeHoverForShowcase = true;
        }

        public void StageNightTorchExploreShowcaseForCapture(
            GridCoord? coord = null,
            int worldClockTurns = 58,
            int torchTurnsRemaining = 36)
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            if (_isInCombat)
            {
                ExitCombatToExploration(syncPartyFromCombat: false);
            }

            if (_isCampActive)
            {
                ExitCampMode();
            }

            hudController?.CloseAllModals();
            hudController?.HideOutcomeModal();
            hudController?.HideToast();

            GridCoord targetPos = coord ?? new GridCoord(53, 62);
            if (!Bundle.Maps.TryGetValue("land_0", out TileMapData landMap) ||
                !landMap.InBounds(targetPos) ||
                !landMap.IsPassable(targetPos.X, targetPos.Y, ExploreSession.Party.HasBoat))
            {
                targetPos = new GridCoord(10, 12);
            }

            ExploreSession.Party.TorchTurnsRemaining = Math.Max(0, torchTurnsRemaining);
            ExploreSession = new ExplorationSession(
                Bundle.Maps,
                "land_0",
                ExploreSession.Party,
                _roller,
                startPosition: targetPos,
                initialFacing: "East",
                initialTurnCount: ExploreSession.TurnCount,
                clearedEncounterIds: ExploreSession.ClearedEncounterIds,
                initialWorldClockTurns: Math.Max(0, worldClockTurns),
                initialPostCombatGraceSteps: 0,
                initialZoneCooldowns: ExploreSession.ZoneCooldownUntilTurns);
            _hasAppliedLightingState = false;

            ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
            EnterExplorationView(immediateCamera: true);

            _lastExploreHoverCoord = new GridCoord(-1, -1);
            _exploreHoverDirty = true;
            hudController?.RefreshExploreHud(
                ExploreSession,
                Bundle,
                ExploreSession.Position,
                BywaterTileClassifier.Classify(ExploreSession.CurrentMap, ExploreSession.Position.X, ExploreSession.Position.Y),
                0,
                ExploreSession.GetCurrentGeography().LocationLabel,
                true,
                _lastContextPoiLabel);
            _showcaseMouseAnchor = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            _freezeHoverForShowcase = true;
        }

        public void StageScoutInterceptShowcaseForCapture(
            GridCoord? coord = null,
            int worldClockTurns = 24,
            int torchTurnsRemaining = 24)
        {
            if (ExploreSession == null || Bundle == null || hudController == null)
            {
                return;
            }

            GridCoord targetPos = coord ?? new GridCoord(53, 62);
            StageNightTorchExploreShowcaseForCapture(targetPos, worldClockTurns, torchTurnsRemaining);

            TileMapData map = ExploreSession.CurrentMap;
            IReadOnlyList<EncounterZoneData> zones = map.GetZonesAt(ExploreSession.Position.X, ExploreSession.Position.Y);
            EncounterZoneData zone = zones.Count > 0 ? zones[0] : (map.EncounterZones.Count > 0 ? map.EncounterZones[0] : null);
            string zoneKey = zone != null ? ExplorationSession.GetEncounterZoneKey(map.MapId, zone) : string.Empty;
            EncounterGeographyInfo geo = ExploreSession.GetCurrentGeography();
            List<MonsterDefinition> zoneMonsters = EncounterDirector.BuildEcologyMonsterPack(
                map,
                ExploreSession.Position,
                zone,
                geo,
                ExploreSession.CurrentWorldClock,
                Bundle.Monsters,
                _roller);

            EncounterScoutingReport report = ExploreSession.CreateScoutingReport(zone, zoneMonsters);
            QuickResolveSummary qrSummary = null;
            bool canQr = !report.IsAmbushedInDark && CanOfferQuickResolveForEncounter(zoneMonsters, out qrSummary);
            string qrRatio = canQr && qrSummary != null ? $"{qrSummary.PowerRatio:F1}x" : string.Empty;

            _pendingScoutingReport = report;
            _pendingQuickResolveMonsters = zoneMonsters;
            _pendingQuickResolveTitle = $"{report.Geography.LocationLabel} Encounter";
            _pendingQuickResolveZoneKey = zoneKey;
            _pendingQuickResolveZone = zone;

            EncounterCheckSpec evadeSpec = EncounterDirector.BuildEvadeCheckSpec(report, ExploreSession.Party);
            EncounterCheckSpec qrSpec = canQr ? EncounterDirector.BuildQuickResolveCheckSpec(qrSummary, ExploreSession.Party) : null;
            hudController.ShowEncounterInterceptModal(report, canQr, qrRatio, evadeSpec, qrSpec);
        }

        private void ResolveSceneReferences()
        {
            if (worldRenderer == null) worldRenderer = FindFirstObjectByType<BywaterWorldRenderer>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<BywaterCameraRig>();
            if (gridOverlays == null) gridOverlays = FindFirstObjectByType<BywaterGridOverlays>();
            if (hudController == null) hudController = FindFirstObjectByType<BywaterHudController>();
            if (cameraRig != null && hudController != null)
            {
                cameraRig.SetHudController(hudController);
            }
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

            if (balanceCatalog == null)
            {
                balanceCatalog = Resources.Load<RealmzBalanceCatalog>("RealmzBalanceCatalog");
            }

            if (masterMixer != null && audioDirector != null && !_isMasterMixerBound)
            {
                audioDirector.BindMixer(masterMixer);
                _isMasterMixerBound = true;
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
                TriggerDemoSkirmish(enterDeployment: true);
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
            hudController.OnContextPoiRequested += HandleContextPoiRequested;
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
            hudController.OnEquipBestHeroRequested += HandleEquipBestHero;
            hudController.OnEquipBestPartyRequested += HandleEquipBestParty;
            hudController.OnConfirmLevelUpRequested += HandleConfirmLevelUp;

            hudController.OnBuyItemRequested += HandleBuyItem;
            hudController.OnSellItemRequested += HandleSellItem;
            hudController.OnSellAllJunkRequested += HandleSellAllJunk;
            hudController.OnIdentifyAllRequested += HandleIdentifyAll;
            hudController.OnTempleHealRequested += HandleTempleHeal;
            hudController.OnTempleCureRequested += HandleTempleCure;
            hudController.OnTempleRechargeRequested += HandleTempleRecharge;
            hudController.OnQuitRequested += HandleQuitToDesktop;

            hudController.OnCombatModeSelected += HandleCombatModeSelected;
            hudController.OnUndoMoveRequested += HandleUndoMove;
            hudController.OnDashRequested += HandleDash;
            hudController.OnGuardRequested += HandleGuard;
            hudController.OnEndTurnRequested += HandleEndTurn;
            hudController.OnCycleSpeedRequested += HandleCycleSpeed;
            hudController.OnSpellSelected += HandleSpellSelected;
            hudController.OnCombatPartyAllyClicked += HandleCombatPartyAllyClicked;
            hudController.OnHeroFormationChanged += HandleHeroFormationChanged;
            hudController.OnStartBattleRequested += HandleStartBattleFromDeployment;
            hudController.OnHotbarSlotHovered += (actionId, title, body) =>
            {
                _isHoveringHotbarUi = true;
            };
            hudController.OnHotbarSlotHoverExited += () =>
            {
                _isHoveringHotbarUi = false;
            };

            hudController.OnAutoCombatToggled += enabled => SetAutoCombatEnabled(enabled);
            hudController.OnAutoEndTurnToggled += enabled =>
            {
                _autoEndTurn = enabled;
                SyncHudSettings();
            };
            hudController.OnBattleSpeedSelected += idx =>
            {
                audioDirector?.PlayUiClick();
                _speedIndex = Mathf.Clamp(idx, 0, SpeedDelays.Length - 1);
                RefreshCombatHudState();
                SyncHudSettings();
            };
            hudController.OnMasterVolumeChanged += val =>
            {
                _masterVolume = Mathf.Clamp01(val);
                ApplyAudioSettings();
            };
            hudController.OnMusicVolumeChanged += val =>
            {
                _musicVolume = Mathf.Clamp01(val);
                ApplyAudioSettings();
            };
            hudController.OnSfxVolumeChanged += val =>
            {
                _sfxVolume = Mathf.Clamp01(val);
                ApplyAudioSettings();
            };
            hudController.OnMuteToggled += muted =>
            {
                _isMuted = muted;
                ApplyAudioSettings();
                SyncHudSettings();
            };
            hudController.OnGridOpacityChanged += val =>
            {
                _gridOpacity = Mathf.Clamp01(val);
                gridOverlays?.SetGridOpacity(_gridOpacity);
            };
            hudController.OnCombatGridToggleRequested += HandleCombatGridToggle;
            hudController.OnNameplatesAlwaysOnChanged += alwaysOn =>
            {
                _nameplatesAlwaysOn = alwaysOn;
                RefreshUnitNameplates();
                SyncHudSettings();
            };
            hudController.OnDevToolsToggled += visible =>
            {
                _devToolsVisible = visible;
                hudController?.SetDevToolsVisible(visible);
                SyncHudSettings();
            };
            hudController.OnFleeCombatRequested += HandleFleeCombat;

            hudController.OnQuickResolveFightRequested += HandleQuickResolveFight;
            hudController.OnQuickResolveAutoWinRequested += HandleQuickResolveAutoWin;
            hudController.OnEncounterEngageRequested += HandleEncounterInterceptEngage;
            hudController.OnEncounterEvadeRequested += HandleEncounterInterceptEvade;
            hudController.OnEncounterQuickResolveRequested += HandleEncounterInterceptQuickResolve;
            hudController.OnLightTorchFromHudRequested += HandleLightTorchFromHud;
            hudController.OnTacticalPitchToggleRequested += HandleTacticalPitchToggle;
            hudController.OnVictoryLevelUpPopped += () => audioDirector?.PlayLevelUpFanfare();

            hudController.OnVictoryTakeLootRequested += HandleTakeVictoryLoot;
            hudController.OnContinueAfterVictoryRequested += () =>
            {
                audioDirector?.PlayUiClick();
                if (CombatSession != null && CombatSession.HasPendingLootDeposit)
                {
                    CombatSession.TakeLootItems(ExploreSession?.Party, null);
                }
                if (_pendingQuickResolveVictorySession != null && _pendingQuickResolveVictorySession.HasPendingLootDeposit)
                {
                    _pendingQuickResolveVictorySession.TakeLootItems(ExploreSession?.Party, null);
                }
                _pendingQuickResolveVictorySession = null;
                if (_isInCombat)
                {
                    ExitCombatToExploration(syncPartyFromCombat: true);
                }
                else
                {
                    hudController?.HideOutcomeModal();
                    hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false, _lastContextPoiLabel);
                }
            };
            hudController.OnRetrySkirmishRequested += HandleRetrySkirmish;
            hudController.OnReviveAtShrineRequested += HandleReviveAtShrine;
        }

        private void SyncExternalModalCloses()
        {
            bool modalClosed = false;
            bool shopOpen = hudController != null && hudController.IsShopOpen;
            if (_wasShopOpen && !shopOpen)
            {
                modalClosed = true;
                if (!_isInCombat && !_isCampActive)
                {
                    PlayExploreMapMusic();
                }
            }
            _wasShopOpen = shopOpen;

            bool templeOpen = hudController != null && hudController.IsTempleOpen;
            if (_wasTempleOpen && !templeOpen)
            {
                modalClosed = true;
                if (!_isInCombat && !_isCampActive)
                {
                    PlayExploreMapMusic();
                }
            }
            _wasTempleOpen = templeOpen;

            if (_isCampActive && (hudController == null || !hudController.IsPartyOpen))
            {
                ExitCampMode();
                modalClosed = true;
            }

            if (modalClosed)
            {
                RefreshNpcPrompts();
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

            if (kb.f12Key.wasPressedThisFrame)
            {
                audioDirector?.PlayUiClick();
                _devToolsVisible = !_devToolsVisible;
                hudController?.SetDevToolsVisible(_devToolsVisible);
                SyncHudSettings();
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame)
            {
                HandleEscapeKey();
                return;
            }

            if (_isInCombat)
            {
                if (hudController != null && hudController.IsModalOpen)
                {
                    return;
                }

                if (kb.lKey.wasPressedThisFrame)
                {
                    audioDirector?.PlayUiClick();
                    hudController?.ToggleCombatLogDrawer();
                    return;
                }

                if (kb.tKey.wasPressedThisFrame)
                {
                    HandleTacticalPitchToggle();
                    return;
                }

                if (kb.aKey.wasPressedThisFrame &&
                    CombatSession != null &&
                    CombatSession.Outcome == CombatOutcome.InProgress)
                {
                    SetAutoCombatEnabled(!_autoCombatEnabled);
                    return;
                }

                if (CombatSession != null && CombatSession.Phase == CombatPhase.Deployment)
                {
                    if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                    {
                        HandleStartBattleFromDeployment();
                        return;
                    }
                    return;
                }

                if (CombatSession != null && CombatSession.Outcome == CombatOutcome.InProgress)
                {
                    if (kb.digit1Key.wasPressedThisFrame)
                    {
                        hudController?.TriggerHotbarSlot(1);
                        return;
                    }
                    if (kb.digit2Key.wasPressedThisFrame)
                    {
                        hudController?.TriggerHotbarSlot(2);
                        return;
                    }
                    if (kb.digit3Key.wasPressedThisFrame)
                    {
                        hudController?.TriggerHotbarSlot(3);
                        return;
                    }
                    if (kb.digit4Key.wasPressedThisFrame)
                    {
                        hudController?.TriggerHotbarSlot(4);
                        return;
                    }
                    if (kb.digit5Key.wasPressedThisFrame)
                    {
                        hudController?.TriggerHotbarSlot(5);
                        return;
                    }
                    if (kb.digit6Key.wasPressedThisFrame)
                    {
                        hudController?.TriggerHotbarSlot(6);
                        return;
                    }

                    if (kb.vKey.wasPressedThisFrame)
                    {
                        HandleCombatModeSelected(CombatActionMode.Shove);
                        return;
                    }

                    if (kb.dKey.wasPressedThisFrame)
                    {
                        HandleDash();
                        return;
                    }

                    if (kb.gKey.wasPressedThisFrame)
                    {
                        HandleGuard();
                        return;
                    }

                    if (kb.spaceKey.wasPressedThisFrame &&
                        CombatSession.CurrentActor != null &&
                        CombatSession.CurrentActor.Side == CombatSide.Hero &&
                        !_autoCombatEnabled)
                    {
                        HandleEndTurn();
                        return;
                    }
                }
                return;
            }

            bool modalOpen = hudController != null && hudController.IsModalOpen;
            if (kb.pKey.wasPressedThisFrame && (!modalOpen || hudController.IsPartyOpen))
            {
                audioDirector?.PlayUiClick();
                hudController?.TogglePartyPanel();
                return;
            }
            if (kb.bKey.wasPressedThisFrame && (!modalOpen || hudController.IsShopOpen || hudController.IsTempleOpen))
            {
                HandleToggleShop();
                return;
            }
            if (modalOpen)
            {
                return;
            }

            if (kb.eKey.wasPressedThisFrame)
            {
                TryInteractNearbyNpcOrPoi();
                return;
            }
            if (kb.cKey.wasPressedThisFrame)
            {
                if (cameraRig != null && cameraRig.HasManualPanOffset)
                {
                    audioDirector?.PlayUiClick();
                    cameraRig.Recenter();
                    hudController?.ShowToast("Camera centered on party.", 1.5f);
                    return;
                }

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

            bool shiftHeld = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (shiftHeld)
            {
                _keyboardStepCooldown = 0f;
                _lastHeldMoveDx = 0;
                _lastHeldMoveDy = 0;
                return;
            }

            int dx = 0;
            int dy = 0;
            bool anyPressedThisFrame = false;

            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)
            {
                dy -= 1;
                anyPressedThisFrame |= kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            }
            else if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
            {
                dy += 1;
                anyPressedThisFrame |= kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;
            }

            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)
            {
                dx -= 1;
                anyPressedThisFrame |= kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame;
            }
            else if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
            {
                dx += 1;
                anyPressedThisFrame |= kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame;
            }

            if (dx != 0 || dy != 0)
            {
                bool dirChanged = dx != _lastHeldMoveDx || dy != _lastHeldMoveDy;
                _lastHeldMoveDx = dx;
                _lastHeldMoveDy = dy;

                if (_keyboardStepCooldown > 0f)
                {
                    if (anyPressedThisFrame || dirChanged)
                    {
                        _bufferedExploreDx = dx;
                        _bufferedExploreDy = dy;
                        _hasBufferedExploreStep = true;
                    }

                    _keyboardStepCooldown -= Time.unscaledDeltaTime;
                    if (_keyboardStepCooldown > 0f)
                    {
                        return;
                    }
                }

                if (_hasBufferedExploreStep)
                {
                    dx = _bufferedExploreDx;
                    dy = _bufferedExploreDy;
                    _hasBufferedExploreStep = false;
                }
            }
            else if (_hasBufferedExploreStep)
            {
                _keyboardStepCooldown -= Time.unscaledDeltaTime;
                if (_keyboardStepCooldown > 0f)
                {
                    return;
                }

                dx = _bufferedExploreDx;
                dy = _bufferedExploreDy;
                _hasBufferedExploreStep = false;
            }
            else
            {
                _keyboardStepCooldown = 0f;
                _lastHeldMoveDx = 0;
                _lastHeldMoveDy = 0;
                return;
            }

            _keyboardStepCooldown = ExploreStepDurationSeconds;
            _pendingNpcInteraction = null;
            _pendingPoiInteraction = null;
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

        private void HandleEscapeKey()
        {
            if (hudController != null && hudController.IsEncounterInterceptOpen)
            {
                return;
            }
            if (hudController != null && hudController.IsQuickResolveOpen)
            {
                hudController.HideQuickResolveModal();
                ClearPendingQuickResolve();
            }
            else if (hudController != null && hudController.IsSettingsOpen)
            {
                hudController.CloseSettingsModal();
            }
            else if (hudController != null && hudController.IsShopOpen)
            {
                hudController.CloseShopModal();
                if (!_isInCombat)
                {
                    PlayExploreMapMusic();
                }
                RefreshNpcPrompts();
            }
            else if (hudController != null && hudController.IsTempleOpen)
            {
                hudController.CloseTempleModal();
                if (!_isInCombat)
                {
                    PlayExploreMapMusic();
                }
                RefreshNpcPrompts();
            }
            else if (hudController != null && hudController.IsPartyOpen)
            {
                hudController.TogglePartyPanel(false);
                if (_isCampActive)
                {
                    ExitCampMode();
                }
            }
            else if (_isInCombat && _autoCombatEnabled)
            {
                SetAutoCombatEnabled(false);
            }
            else if (_isInCombat && _activeCombatMode != CombatActionMode.Smart)
            {
                HandleCombatModeSelected(CombatActionMode.Smart);
            }
            else if (hudController != null && !hudController.IsOutcomeOpen)
            {
                SyncHudSettings();
                hudController.ToggleSettingsModal(true);
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
                worldRenderer.SetTownNpcsHiddenForCombat(false);
                worldRenderer.SetCampfireActive(false, Vector3.zero);
                worldRenderer.SetCombatArenaFrame(0, 0, 0, 0, false);
                worldRenderer.SetTerrainGridHint(0.0f);
                worldRenderer.RenderMap(ExploreSession.CurrentMap, ExploreSession.Position, forceRebuild: true);
                if (worldRenderer.CartographyData != null)
                {
                    ExploreSession.SetBlockedPropFootprints(worldRenderer.CartographyData.ExportBlockedPropGridCoords());
                }
            }

            if (gridOverlays != null)
            {
                gridOverlays.SetElevationResolver(worldRenderer != null ? (Func<int, int, float>)worldRenderer.GetTileElevation : null);
                gridOverlays.ClearAll();
            }

            if (cameraRig != null)
            {
                cameraRig.SetHudController(hudController);
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
            worldRenderer?.UpdateHeroWorldPosition(leaderWorldPos);
            ClearExploreInteractableHover();
            RebuildAmbientPoiCache(force: true);
            _hasAppliedLightingState = false;
            _lastAppliedNormalizedTime = -1f;
            ApplyAtmosphereAndTorchLighting(leaderWorldPos, forceImmediate: true);
            SyncFogOfWarAndWeatherVisuals();

            if (cameraRig != null)
            {
                cameraRig.SetCombatMode(false);
                cameraRig.SetFollowTarget(leaderWorldPos, immediateCamera, recenterManualPan: immediateCamera);
            }

            PlayExploreMapMusic();

            _exploreHoverDirty = true;
            _lastExploreHoverCoord = new GridCoord(-1, -1);
            hudController?.HideOutcomeModal();
            hudController?.HideQuickResolveModal();
            hudController?.HideEncounterInterceptModal();
            ClearPendingScoutingReport();
            RefreshContextualExplorePoi();
            RefreshNpcPrompts();
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false, _lastContextPoiLabel);
            UpdateActiveTreeOcclusionFade();
        }

        private void PlayExploreMapMusic()
        {
            if (audioDirector == null || ExploreSession?.CurrentMap == null)
            {
                return;
            }

            string suite;
            if (ExploreSession.CurrentMap.IsDungeon)
            {
                suite = "dungeon";
            }
            else
            {
                EncounterGeographyInfo geo = ExploreSession.GetCurrentGeography();
                suite = geo != null && geo.IsTownSafeZone ? "treasure" : "outdoor";
            }

            if (!string.Equals(audioDirector.CurrentMusicKey, suite, StringComparison.OrdinalIgnoreCase))
            {
                audioDirector.PlayMusic(suite, 0.9f);
            }
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
            int maxViews = _isCampActive ? MaxTrailingPartyViews : 1;
            int livingViewIdx = 0;

            for (int i = 0; i < members.Count && livingViewIdx < maxViews; i++)
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
                Material heroMat = art != null ? art.billboardMaterial : null;
                if (assetCatalog != null)
                {
                    heroMat = assetCatalog.ResolveCartographyUnitMaterial(member.Id, heroMat);
                }

                view.gameObject.SetActive(true);
                view.Configure(
                    member.Id,
                    member.Name,
                    heroMat,
                    HeroRingColor,
                    font,
                    spriteSize: 2.0f,
                    showHud: false,
                    isAllyOverride: true,
                    catalog: assetCatalog);
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

            if (worldRenderer != null && worldRenderer.TryGetNpcAtCoord(worldCoord, out CartographyNpcPlacement clickedNpc))
            {
                GridCoord npcCoord = new GridCoord(clickedNpc.TileX, clickedNpc.TileY);
                if (ExploreSession.Position.ChebyshevDistance(npcCoord) <= 1)
                {
                    _pendingNpcInteraction = null;
                    InteractWithNpc(clickedNpc);
                    return;
                }

                if (TryFindAdjacentPassableTile(map, ExploreSession.Position, npcCoord, ExploreSession.Party.HasBoat, out GridCoord approachCoord))
                {
                    _pendingNpcInteraction = clickedNpc;
                    StartWalkToExploreTile(approachCoord);
                    return;
                }

                _pendingNpcInteraction = null;
                hudController?.ShowToast("Path blocked.", 1.6f);
                return;
            }

            _pendingNpcInteraction = null;
            _pendingPoiInteraction = null;

            if (TryFindClickableExplorePoi(map, worldCoord, out MapPointOfInterest clickedPoi))
            {
                if (ExploreSession.Position.ChebyshevDistance(clickedPoi.Coord) <= 1)
                {
                    HandlePoiArrival(clickedPoi);
                    return;
                }

                if (map.IsPassable(clickedPoi.X, clickedPoi.Y, ExploreSession.Party.HasBoat))
                {
                    _pendingPoiInteraction = clickedPoi;
                    StartWalkToExploreTile(clickedPoi.Coord);
                    return;
                }

                if (TryFindAdjacentPassableTile(map, ExploreSession.Position, clickedPoi.Coord, ExploreSession.Party.HasBoat, out GridCoord poiApproach))
                {
                    _pendingPoiInteraction = clickedPoi;
                    StartWalkToExploreTile(poiApproach);
                    return;
                }
            }

            if (worldCoord == ExploreSession.Position)
            {
                return;
            }

            StartWalkToExploreTile(worldCoord);
        }

        private static bool TryFindClickableExplorePoi(TileMapData map, GridCoord clickedCoord, out MapPointOfInterest matchedPoi)
        {
            matchedPoi = null;
            if (map == null || map.PointsOfInterest == null)
            {
                return false;
            }

            for (int i = 0; i < map.PointsOfInterest.Count; i++)
            {
                MapPointOfInterest poi = map.PointsOfInterest[i];
                if (poi == null) continue;
                string kind = (poi.Kind ?? string.Empty).Trim().ToLowerInvariant();
                if (kind != "shop" && kind != "temple" && kind != "tavern")
                {
                    continue;
                }

                if (poi.Coord.ChebyshevDistance(clickedCoord) <= 1)
                {
                    matchedPoi = poi;
                    return true;
                }
            }

            return false;
        }

        private void StartWalkToExploreTile(GridCoord destCoord)
        {
            StopExploreWalkCoroutine();

            if (worldRenderer != null && worldRenderer.TryGetNpcAtCoord(destCoord, out _))
            {
                _pendingNpcInteraction = null;
                hudController?.ShowToast("Path blocked.", 1.6f);
                return;
            }

            TileMapData map = ExploreSession?.CurrentMap;
            if (map == null || !map.InBounds(destCoord) || !map.IsPassable(destCoord.X, destCoord.Y, ExploreSession.Party.HasBoat))
            {
                _pendingNpcInteraction = null;
                hudController?.ShowToast("Path blocked.", 1.6f);
                return;
            }

            _explorePreviewSteps.Clear();
            bool found = _explorePathfinder.FindPathSteps(
                map,
                ExploreSession.Position,
                destCoord,
                _explorePreviewSteps,
                out _,
                allowDiagonal: true,
                hasBoat: ExploreSession.Party.HasBoat);
            if (!found || _explorePreviewSteps.Count == 0)
            {
                _pendingNpcInteraction = null;
                hudController?.ShowToast("Path blocked.", 1.6f);
                return;
            }

            _activeMoveSteps.Clear();
            for (int i = 0; i < _explorePreviewSteps.Count; i++)
            {
                _activeMoveSteps.Add(_explorePreviewSteps[i]);
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
            float accumulator = 0f;
            while (_activeMoveStepIndex < _activeMoveSteps.Count && !_isInCombat && ExploreSession != null)
            {
                GridCoord nextStep = _activeMoveSteps[_activeMoveStepIndex++];
                bool hasMoreSteps = _activeMoveStepIndex < _activeMoveSteps.Count;
                bool halted = ExecuteSingleExploreStep(nextStep, animate: true, out ExplorationStepResult stepRes, markHoverDirty: !hasMoreSteps);
                if (halted)
                {
                    if (stepRes != null && stepRes.Moved)
                    {
                        float waitRem = ExploreStepDurationSeconds;
                        while (waitRem > 0f)
                        {
                            yield return null;
                            waitRem -= Time.deltaTime;
                        }
                    }

                    _activeMoveSteps.Clear();
                    _activeMoveStepIndex = 0;
                    _exploreWalkCoroutine = null;
                    _pendingNpcInteraction = null;
                    _exploreHoverDirty = true;
                    HandleExplorationStepHalt(stepRes);
                    yield break;
                }

                accumulator += ExploreStepDurationSeconds;
                while (accumulator > 0f)
                {
                    yield return null;
                    accumulator -= Time.deltaTime;
                }
            }

            _activeMoveSteps.Clear();
            _activeMoveStepIndex = 0;
            _exploreWalkCoroutine = null;
            _exploreHoverDirty = true;
            TryCompletePendingNpcInteraction();
            if (_pendingPoiInteraction != null && ExploreSession != null)
            {
                MapPointOfInterest arrivedPoi = _pendingPoiInteraction;
                _pendingPoiInteraction = null;
                if (ExploreSession.Position.ChebyshevDistance(arrivedPoi.Coord) <= 1)
                {
                    HandlePoiArrival(arrivedPoi);
                }
            }
        }

        private bool ExecuteSingleExploreStep(GridCoord nextTile, bool animate, out ExplorationStepResult stepRes, bool markHoverDirty = true)
        {
            stepRes = null;
            if (ExploreSession == null || ExploreSession.CurrentMap == null)
            {
                return true;
            }

            TileMapData map = ExploreSession.CurrentMap;
            GridCoord prevPos = ExploreSession.Position;
            if (worldRenderer != null && worldRenderer.TryGetNpcAtCoord(nextTile, out _))
            {
                stepRes = new ExplorationStepResult(false, prevPos, ExplorationHaltReason.Blocked);
                if (nextTile != prevPos)
                {
                    hudController?.ShowToast("Path blocked.", 1.4f);
                }
                return true;
            }

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
                !string.Equals(audioDirector.CurrentMusicKey, "battle", StringComparison.OrdinalIgnoreCase))
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
            ApplyAtmosphereAndTorchLighting(leaderPos, forceImmediate: false);
            SyncFogOfWarAndWeatherVisuals();
            cameraRig?.SetFollowTarget(leaderPos, immediate: !doAnimate, recenterManualPan: true);

            if (stepRes.TorchExpiredThisStep)
            {
                hudController?.ShowToast("Your torch has burned out!", 2.4f);
            }

            if (markHoverDirty)
            {
                _exploreHoverDirty = true;
            }
            RefreshContextualExplorePoi();
            RefreshNpcPrompts();
            hudController?.RefreshExploreHud(
                ExploreSession,
                Bundle,
                stepRes.Position,
                BywaterTileClassifier.Classify(map, stepRes.Position.X, stepRes.Position.Y),
                0,
                string.Empty,
                true,
                _lastContextPoiLabel);

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
            bool isInteractableHover = kind == BywaterTileKind.TownBuildingDoor ||
                                       (worldRenderer != null && worldRenderer.TryGetNpcAtCoord(hoverCoord, out _));

            MapPortal portal = map.GetPortalAt(hoverCoord.X, hoverCoord.Y);
            if (portal != null)
            {
                isInteractableHover = true;
                poiHint = !string.IsNullOrEmpty(portal.Label) ? portal.Label : $"Portal -> {portal.DestMapId}";
            }
            else
            {
                MapPointOfInterest poi = map.GetPointOfInterestAt(hoverCoord.X, hoverCoord.Y);
                if (poi != null)
                {
                    poiHint = !string.IsNullOrEmpty(poi.Label) ? poi.Label : poi.Kind;
                    if (!string.IsNullOrEmpty(poi.Kind) &&
                        (string.Equals(poi.Kind, "shop", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(poi.Kind, "temple", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(poi.Kind, "tavern", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(poi.Kind, "shrine", StringComparison.OrdinalIgnoreCase)))
                    {
                        isInteractableHover = true;
                    }
                }
            }

            if (isInteractableHover)
            {
                worldRenderer?.SetHoveredInteractable(GridToWorldAtElevation(hoverCoord), BywaterUiHelpers.BrassHighlight);
                _hasHoverInteractable = true;
            }
            else
            {
                ClearExploreInteractableHover();
            }

            RefreshContextualExplorePoi();
            hudController?.RefreshExploreHud(
                ExploreSession,
                Bundle,
                hoverCoord,
                kind,
                _lastExploreHoverStepsCount,
                poiHint,
                reachable,
                _lastContextPoiLabel);
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
                        audioDirector?.PlayDoorInteract();
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
                    EncounterZoneData zone = res.TriggeredZone;
                    string zoneKey = (zone != null && ExploreSession.CurrentMap != null)
                        ? ExplorationSession.GetEncounterZoneKey(ExploreSession.CurrentMap.MapId, zone)
                        : string.Empty;
                    EncounterGeographyInfo geo = ExploreSession.GetCurrentGeography();
                    List<MonsterDefinition> zoneMonsters = zone != null && Bundle != null
                        ? EncounterDirector.BuildEcologyMonsterPack(
                            ExploreSession.CurrentMap,
                            ExploreSession.Position,
                            zone,
                            geo,
                            ExploreSession.CurrentWorldClock,
                            Bundle.Monsters,
                            _roller)
                        : SelectMonstersFromEncounterZone(zone);
                    EncounterScoutingReport scoutingReport = ExploreSession.CreateScoutingReport(zone, zoneMonsters);

                    QuickResolveSummary qrSummary = null;
                    bool canQr = !scoutingReport.IsAmbushedInDark &&
                                 CanOfferQuickResolveForEncounter(zoneMonsters, out qrSummary);
                    string qrRatioText = canQr && qrSummary != null ? $"{qrSummary.PowerRatio:F1}x" : string.Empty;

                    if (!Application.isBatchMode && hudController != null)
                    {
                        _pendingScoutingReport = scoutingReport;
                        _pendingQuickResolveMonsters = zoneMonsters;
                        _pendingQuickResolveTitle = $"{scoutingReport.Geography.LocationLabel} Encounter";
                        _pendingQuickResolveZoneKey = zoneKey;
                        _pendingQuickResolveZone = zone;
                        EncounterCheckSpec evadeSpec = EncounterDirector.BuildEvadeCheckSpec(scoutingReport, ExploreSession.Party);
                        EncounterCheckSpec qrSpec = canQr ? EncounterDirector.BuildQuickResolveCheckSpec(qrSummary, ExploreSession.Party) : null;
                        hudController.ShowEncounterInterceptModal(scoutingReport, canQr, qrRatioText, evadeSpec, qrSpec);
                        if (scoutingReport.IsAmbushedInDark)
                        {
                            audioDirector?.PlaySpell("fire");
                        }
                        break;
                    }

                    _activeEncounterZone = zone;
                    _activeEncounterZoneKey = zoneKey;
                    _activeCombatSurpriseMode = scoutingReport.ResolveEngageSurpriseMode();
                    _activeCombatRewardBonusPct = scoutingReport.RewardBonusPct;
                    hudController?.ShowToast("Ambush! Tactical encounter triggered!", 2.5f);
                    StartTacticalCombat(
                        zoneMonsters,
                        $"{scoutingReport.Geography.LocationLabel} Battle",
                        _activeCombatSurpriseMode,
                        _activeCombatRewardBonusPct);
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
                RefreshNpcPrompts();
                hudController?.ShowToast($"Entered {poi.Label}", 2.2f);
            }
            else if (kind == "temple")
            {
                hudController?.OpenTempleModal(ExploreSession.Party, Bundle);
                audioDirector?.PlayMusic("temple", 0.6f);
                RefreshNpcPrompts();
                hudController?.ShowToast($"Entered {poi.Label}", 2.2f);
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
                EnsureExploreHeroViews();
                SyncExploreHeroViewsToTrail(immediate: true);
                PlayExploreMapMusic();
                RefreshNpcPrompts();
            }
        }

        private void HandleToggleShop()
        {
            if (hudController == null || ExploreSession == null)
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
                RefreshNpcPrompts();
                return;
            }

            if (hudController.IsTempleOpen)
            {
                hudController.CloseTempleModal();
            }

            if (TryFindNearbyVenue(out string nearbyVenueKind, out string nearbyShopId, out string nearbyLabel))
            {
                if (string.Equals(nearbyVenueKind, "temple", StringComparison.OrdinalIgnoreCase))
                {
                    hudController.OpenTempleModal(ExploreSession.Party, Bundle);
                    audioDirector?.PlayMusic("temple", 0.6f);
                }
                else if (!string.IsNullOrEmpty(nearbyShopId))
                {
                    hudController.OpenShop(nearbyShopId, ExploreSession.Party, Bundle);
                    audioDirector?.PlayMusic("shop", 0.7f);
                }
                RefreshNpcPrompts();
                if (!string.IsNullOrEmpty(nearbyLabel))
                {
                    hudController.ShowToast($"Trading with {nearbyLabel}.", 2.0f);
                }
                return;
            }

            if (ExploreSession.CurrentMap != null &&
                !ExploreSession.CurrentMap.IsDungeon &&
                worldRenderer != null &&
                worldRenderer.TryFindNearestNpc(ExploreSession.Position, out CartographyNpcPlacement nearestNpc, out _))
            {
                GridCoord npcCoord = new GridCoord(nearestNpc.TileX, nearestNpc.TileY);
                if (TryFindAdjacentPassableTile(
                    ExploreSession.CurrentMap,
                    ExploreSession.Position,
                    npcCoord,
                    ExploreSession.Party.HasBoat,
                    out GridCoord approachCoord))
                {
                    _pendingNpcInteraction = nearestNpc;
                    StartWalkToExploreTile(approachCoord);
                    hudController.ShowToast($"Heading to {nearestNpc.DisplayName}...", 2.2f);
                    return;
                }
            }

            hudController.ShowToast("Walk to the Blacksmith, Tannery stall, Apothecary, or Shrine to trade.", 2.6f);
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
            hudController?.OpenTempleModal(ExploreSession.Party, Bundle);
            audioDirector?.PlayMusic("temple", 0.6f);
            RefreshNpcPrompts();
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
                audioDirector?.PlayDoorInteract();
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
            hudController?.CloseAllModals();
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

            BalanceOverrides activeOverrides = Bundle.BalanceOverrides ??
                                               (balanceCatalog != null ? balanceCatalog.ApplyToBalanceOverrides() : null);
            double restoredMinutes = BywaterSaveService.ResolveRestoredWorldClockMinutes(data);

            ExploreSession = new ExplorationSession(
                Bundle.Maps,
                targetMap.MapId,
                restored.Party,
                _roller,
                startPosition: startPos,
                initialFacing: data.facing,
                initialTurnCount: restored.TurnCount,
                clearedEncounterIds: restored.ClearedEncounterIds,
                initialWorldClockTurns: restored.WorldClockTurns,
                initialPostCombatGraceSteps: restored.PostCombatGraceStepsRemaining,
                initialZoneCooldowns: restored.ZoneCooldownUntilTurns,
                initialWeather: restored.Weather,
                balanceOverrides: activeOverrides,
                initialWorldClockMinutes: restoredMinutes);

            if (ExploreSession.FogMaps != null)
            {
                foreach (KeyValuePair<string, FogOfWarMap> kvp in ExploreSession.FogMaps)
                {
                    SavedFogMapEntry savedFog = BywaterSaveService.ExtractFogForMap(data, kvp.Key);
                    if (savedFog != null && !string.IsNullOrWhiteSpace(savedFog.packedExploredBase64))
                    {
                        kvp.Value.ImportExploredBitsetBase64(savedFog.packedExploredBase64);
                    }
                    else if (restored.FogMaps != null &&
                             restored.FogMaps.TryGetValue(kvp.Key, out FogOfWarMap restoredFog) &&
                             restoredFog != null)
                    {
                        kvp.Value.ImportExploredBitsetBase64(restoredFog.ExportExploredBitsetBase64());
                    }
                }
            }
            ExploreSession.RefreshFogOfWar();

            _hasAppliedLightingState = false;
            _lastAppliedNormalizedTime = -1f;
            _lastSyncedClockMinute = -1;

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

        private void HandleHeroFormationChanged(int heroIdx, FormationRank rank)
        {
            if (ExploreSession?.Party == null || Bundle == null)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            if (ExploreSession.Party.SetFormationRank(heroIdx, rank))
            {
                hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, heroIdx);
                hudController?.ShowToast($"{ExploreSession.Party.Members[heroIdx].Name} assigned to {rank} rank.", 1.5f);
            }
        }

        private void HandleEquipFromStash(int heroIdx, int stashIdx)
        {
            audioDirector?.PlayUiClick();
            if (ExploreSession.Party.EquipFromStash(heroIdx, stashIdx, Bundle.Items, null, out string reason))
            {
                hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                hudController?.ShowToast("Equipped item.", 1.6f);
            }
            else if (!string.IsNullOrWhiteSpace(reason))
            {
                hudController?.ShowToast(reason, 2.5f);
            }
        }

        private void HandleUseStashItem(int heroIdx, int stashIdx)
        {
            audioDirector?.PlayUiClick();
            PartyState party = ExploreSession.Party;
            if (party.TryUseConsumableOnHero(heroIdx, stashIdx, Bundle.Items, _roller, out string feedback))
            {
                audioDirector?.PlaySpell("heal");
                hudController?.RefreshPartyDrawer(party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                if (!string.IsNullOrEmpty(feedback))
                {
                    hudController?.ShowToast(feedback, 2.4f);
                }
            }
        }

        private void HandleEquipBestHero(int heroIdx)
        {
            audioDirector?.PlayUiClick();
            int equipped = ExploreSession.Party.EquipBestForHero(heroIdx, Bundle.Items);
            hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, heroIdx);
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            hudController?.ShowToast(
                equipped > 0 ? $"Equipped {equipped} upgrade(s) for hero." : "No upgrades available in stash.",
                2.0f);
        }

        private void HandleEquipBestParty()
        {
            audioDirector?.PlayUiClick();
            int equipped = ExploreSession.Party.EquipBestForParty(Bundle.Items);
            hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            hudController?.ShowToast(
                equipped > 0 ? $"Equipped {equipped} party upgrade(s) from stash." : "Party already wears the best gear in stash.",
                2.2f);
        }

        private void HandleConfirmLevelUp(int heroIdx, AttributeAllocation alloc, int? learnedSpellId)
        {
            PartyState party = ExploreSession.Party;
            if (heroIdx < 0 || heroIdx >= party.Members.Count)
            {
                return;
            }

            PartyMemberState hero = party.Members[heroIdx];
            if (hero.ApplyPendingLevelUp(alloc, learnedSpellId))
            {
                audioDirector?.PlaySpell("heal");
                hudController?.ShowToast($"{hero.Name} reached Level {hero.Level}!", 2.8f);
                hudController?.CloseLevelUpModal();

                // Auto-advance to next hero with a pending level-up, otherwise return to drawer or exploration.
                for (int i = 0; i < party.Members.Count; i++)
                {
                    if (party.Members[i].HasPendingLevelUp)
                    {
                        hudController?.RefreshPartyDrawer(party, Bundle, i);
                        hudController?.OpenLevelUpModal(i);
                        hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                        return;
                    }
                }

                hudController?.RefreshPartyDrawer(party, Bundle, heroIdx);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                TacticalCombatSession activeVic = CombatSession ?? _pendingQuickResolveVictorySession;
                if (hudController != null && hudController.IsOutcomeOpen && activeVic?.LastVictorySummary != null)
                {
                    hudController.ShowVictoryOutcome(activeVic.LastVictorySummary, party, Bundle);
                }
            }
        }

        private void HandleCampRest(CampMode mode)
        {
            audioDirector?.PlayUiClick();
            RestResult res = ExploreSession.TryCampRest(mode, out EncounterZoneData ambushZone, out int _);
            if (res.Ambushed && ambushZone != null)
            {
                audioDirector?.PlaySpell("fire");
                hudController?.CloseAllModals();
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                hudController?.ShowToast("Ambush while resting in the wilds!", 2.8f);
                EncounterGeographyInfo campGeo = ExploreSession.GetCurrentGeography();
                List<MonsterDefinition> monsters = Bundle != null
                    ? EncounterDirector.BuildEcologyMonsterPack(
                        ExploreSession.CurrentMap,
                        ExploreSession.Position,
                        ambushZone,
                        campGeo,
                        ExploreSession.CurrentWorldClock,
                        Bundle.Monsters,
                        _roller)
                    : SelectMonstersFromEncounterZone(ambushZone);
                _activeEncounterZone = ambushZone;
                _activeEncounterZoneKey = ExplorationSession.GetEncounterZoneKey(ExploreSession.CurrentMap.MapId, ambushZone);
                _activeCombatSurpriseMode = CombatInitiativeSurpriseMode.EnemySurprisedParty;
                _activeCombatRewardBonusPct = ExploreSession.CurrentWorldClock.IsNight ? EncounterDirector.NightRewardBonusPercent : 0;
                StartTacticalCombat(monsters, "Camp Ambush!", _activeCombatSurpriseMode, _activeCombatRewardBonusPct);
                return;
            }

            if (!res.Success)
            {
                hudController?.ShowToast(string.IsNullOrEmpty(res.Reason) ? "Cannot rest here right now." : res.Reason, 2.8f);
                hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                return;
            }

            audioDirector?.PlaySpell("heal");
            EnsureExploreHeroViews();
            ApplyAtmosphereAndTorchLighting(GridToWorldAtElevation(ExploreSession.Position), forceImmediate: false);
            SyncFogOfWarAndWeatherVisuals();
            hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
            string label = mode == CampMode.FullRest ? "Full Rest" : "Quick Bandage";
            string extra = res.TorchChargesConsumed > 0 ? " (-1 Torch)" : string.Empty;
            hudController?.ShowToast($"{label}{extra}: +{res.TotalHpRestored} HP, +{res.TotalSpRestored} SP.", 2.6f);
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
                if (m.Hp < m.MaxHp || m.Sp < m.MaxSp || m.ExhaustionStacks > 0 || m.CurseStacks > 0 || m.Conditions.Count > 0)
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
                if (hudController != null && hudController.IsTempleOpen)
                {
                    hudController.RefreshTempleModal(ExploreSession.Party, Bundle);
                }
                hudController?.ShowToast("Bywater Shrine blessing restored full HP and cleared ailments (-25g).", 2.8f);
            }
            else
            {
                audioDirector?.PlayUiClick();
                hudController?.ShowToast("Not enough gold for Shrine blessing (25g required).", 2.4f);
            }
        }

        private void HandleTempleCure()
        {
            if (ExploreSession == null)
            {
                return;
            }

            if (ExploreSession.TriggerTempleCureCurse(20))
            {
                audioDirector?.PlaySpell("heal");
                EnsureExploreHeroViews();
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                hudController?.RefreshTempleModal(ExploreSession.Party, Bundle);
                hudController?.ShowToast("Sacred cleansing removed curses, exhaustion, and ailments (-20g).", 2.6f);
            }
            else
            {
                audioDirector?.PlayUiClick();
                string msg = ExploreSession.Party.Gold < 20
                    ? "Not enough gold for sacred cleansing (20g required)."
                    : "No curses, exhaustion, or detrimental ailments affect the party.";
                hudController?.ShowToast(msg, 2.4f);
            }
        }

        private void HandleTempleRecharge()
        {
            if (ExploreSession == null || Bundle == null)
            {
                return;
            }

            int recharged = ExploreSession.TriggerTempleRechargeItems(Bundle.Items, 30);
            if (recharged > 0)
            {
                audioDirector?.PlaySpell("heal");
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false);
                hudController?.RefreshTempleModal(ExploreSession.Party, Bundle);
                hudController?.ShowToast($"Recharged {recharged} enchanted item(s) (-30g).", 2.6f);
            }
            else if (recharged < 0)
            {
                audioDirector?.PlayUiClick();
                hudController?.ShowToast("Not enough gold for Arcane Recharge (30g required).", 2.4f);
            }
            else
            {
                audioDirector?.PlayUiClick();
                hudController?.ShowToast("All charged relics and staves are already full.", 2.2f);
            }
        }

        private void HandleQuitToDesktop()
        {
            audioDirector?.PlayUiClick();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void StartTacticalCombat(
            List<MonsterDefinition> monstersToSpawn,
            string title,
            CombatInitiativeSurpriseMode surpriseMode = CombatInitiativeSurpriseMode.None,
            int rewardBonusPercent = -1,
            bool startInDeployment = true)
        {
            if (monstersToSpawn == null || monstersToSpawn.Count == 0)
            {
                monstersToSpawn = SelectSkirmishMonsters(ExploreSession.CurrentMap.IsDungeon);
            }

            if (rewardBonusPercent < 0 && ExploreSession != null)
            {
                rewardBonusPercent = ExploreSession.CurrentWorldClock.IsNight
                    ? EncounterDirector.NightRewardBonusPercent
                    : 0;
            }
            int resolvedRewardBonusPct = Math.Max(0, rewardBonusPercent);
            _activeCombatSurpriseMode = surpriseMode;
            _activeCombatRewardBonusPct = resolvedRewardBonusPct;

            StopExploreWalkCoroutine();
            hudController?.CloseAllModals();
            if (_isCampActive)
            {
                _isCampActive = false;
                worldRenderer?.SetCampfireActive(false, Vector3.zero);
            }

            StopAiCoroutine();
            UnsubscribeCombatEvents();
            ClearCombatUnitViews();
            ClearExploreInteractableHover();
            _destroyedArenaPropCoords.Clear();
            gridOverlays?.ClearCombatVfx();
            worldRenderer?.SetTownNpcsHiddenForCombat(true);

            TileMapData map = ExploreSession.CurrentMap;
            GridCoord partyPos = ExploreSession.Position;
            _preCombatExplorePos = partyPos;
            _hasPreCombatExplorePos = true;
            _arenaWidth = 12;
            _arenaHeight = 10;
            GridCoord arenaOrigin = FindCleanArenaOrigin(map, partyPos, _arenaWidth, _arenaHeight);
            _arenaOriginX = arenaOrigin.X;
            _arenaOriginY = arenaOrigin.Y;

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

            bool isAmbushedInDark = surpriseMode == CombatInitiativeSurpriseMode.EnemySurprisedParty;
            GridCoord[] frontSlots = { new GridCoord(3, 4), new GridCoord(3, 5), new GridCoord(3, 3), new GridCoord(3, 6) };
            GridCoord[] midSlots = { new GridCoord(2, 4), new GridCoord(2, 5), new GridCoord(2, 3), new GridCoord(2, 6) };
            GridCoord[] backSlots = { new GridCoord(1, 4), new GridCoord(1, 5), new GridCoord(0, 4), new GridCoord(0, 5) };
            GridCoord[] surprisedHeroSlots =
            {
                new GridCoord(3, 4),
                new GridCoord(2, 4),
                new GridCoord(3, 5),
                new GridCoord(2, 5),
                new GridCoord(4, 4),
                new GridCoord(4, 5)
            };

            int frontIdx = 0;
            int midIdx = 0;
            int backIdx = 0;
            IReadOnlyList<int> orderedHeroIndices = ExploreSession.Party.GetOrderedFormationMemberIndices();
            for (int order = 0; order < orderedHeroIndices.Count; order++)
            {
                PartyMemberState member = ExploreSession.Party.Members[orderedHeroIndices[order]];
                if (member.Hp <= 0)
                {
                    member.Hp = 1;
                }

                GridCoord targetSlot;
                if (isAmbushedInDark)
                {
                    targetSlot = surprisedHeroSlots[order % surprisedHeroSlots.Length];
                }
                else
                {
                    switch (member.FormationRank)
                    {
                        case FormationRank.Front:
                            targetSlot = frontSlots[frontIdx++ % frontSlots.Length];
                            break;
                        case FormationRank.Back:
                            targetSlot = backSlots[backIdx++ % backSlots.Length];
                            break;
                        default:
                            targetSlot = midSlots[midIdx++ % midSlots.Length];
                            break;
                    }
                }

                GridCoord spot = PickNearestFreeWalkable(connectedWalkable, occupied, targetSlot, map, _arenaOriginX, _arenaOriginY);
                occupied.Add(spot);
                combatants.Add(CombatantState.FromHero(member, spot, Bundle.Items));
            }

            GridCoord[] preferredMonsterSlots = isAmbushedInDark
                ? new[]
                {
                    new GridCoord(5, 4),
                    new GridCoord(6, 5),
                    new GridCoord(5, 6),
                    new GridCoord(5, 3),
                    new GridCoord(7, 4),
                    new GridCoord(7, 5)
                }
                : new[]
                {
                    new GridCoord(7, 4),
                    new GridCoord(8, 5),
                    new GridCoord(7, 6),
                    new GridCoord(9, 4),
                    new GridCoord(8, 6),
                    new GridCoord(9, 6)
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
                GridCoord spot = PickNearestFreeWalkable(connectedWalkable, occupied, targetSlot, map, _arenaOriginX, _arenaOriginY);
                occupied.Add(spot);

                string instanceId = $"mon_{i}_{def.Id}";
                combatants.Add(CombatantState.FromMonster(def, instanceId, spot, _roller));
            }

            EncounterGeographyInfo combatGeo = ExploreSession.GetCurrentGeography();
            if (_forcedCombatArenaArchetype.HasValue)
            {
                EncounterDirector.ApplyArenaArchetype(grid, _forcedCombatArenaArchetype.Value, occupied, _roller, combatGeo);
            }
            else
            {
                EncounterDirector.ApplyBiomeArenaFeatures(grid, combatGeo, occupied, _roller);
            }

            bool isCombatDark = map.IsDungeon || (ExploreSession != null && ExploreSession.CurrentWorldClock.IsNight);

            CombatSession = new TacticalCombatSession(
                grid,
                combatants,
                Bundle.Spells,
                Bundle.Items,
                _roller,
                RulesetMode.ModernBalanced,
                ExploreSession.Party,
                _activeCombatSurpriseMode,
                resolvedRewardBonusPct,
                isAmbientDarkness: isCombatDark,
                deferLootDeposit: true,
                startInDeployment: startInDeployment && !isAmbushedInDark,
                weather: ExploreSession != null ? ExploreSession.Weather : WeatherState.Clear);
            CombatSession.SurfaceChanged += OnCombatSurfaceChanged;
            CombatSession.OnArenaIlluminationChanged += OnCombatArenaIlluminationChanged;
            CombatSession.PropDetonated += OnCombatPropDetonated;
            CombatSession.LightningChained += OnCombatLightningChained;

            _isInCombat = true;
            _outcomeShown = false;
            _pendingQuickResolveVictorySession = null;
            _activeCombatMode = CombatActionMode.Smart;
            _selectedSpellId = -1;
            _hoveredCombatUnitId = null;
            _hoveredRimUnitId = null;
            _selectedDeploymentHeroId = null;
            _isHoveringHotbarUi = false;
            _combatHoverDirty = true;
            _lastCombatHover = new GridCoord(-1, -1);

            HideExploreHeroViews();

            GridCoord mid = new GridCoord(_arenaOriginX + _arenaWidth / 2, _arenaOriginY + _arenaHeight / 2);

            if (worldRenderer != null)
            {
                worldRenderer.ApplyCombatArenaVisuals(map, _arenaOriginX, _arenaOriginY, grid);
                worldRenderer.SetCombatArenaFrame(_arenaOriginX, _arenaOriginY, _arenaWidth, _arenaHeight, true);
                worldRenderer.SetTerrainGridHint(0.08f);
            }

            Vector3 midWorld = GridToWorldAtElevation(mid);
            if (worldRenderer != null)
            {
                worldRenderer.UpdateHeroWorldPosition(midWorld);
            }

            if (gridOverlays != null)
            {
                gridOverlays.SetElevationResolver(worldRenderer != null ? (Func<int, int, float>)worldRenderer.GetTileElevation : null);
                gridOverlays.SetWaterTileResolver((wx, wy) =>
                    worldRenderer != null &&
                    worldRenderer.CartographyData != null &&
                    worldRenderer.CartographyData.GetTile(wx, wy).IsWater);
                gridOverlays.SetWaterSurfaceHeight(CartographyWorldData.TileMapWaterSurfaceElevation);
                gridOverlays.SetGridOpacity(_gridOpacity);
            }

            _hasAppliedLightingState = false;
            _lastAppliedNormalizedTime = -1f;
            ApplyAtmosphereAndTorchLighting(GetCombatPartyTorchWorldPos(), forceImmediate: true);
            SyncFogOfWarAndWeatherVisuals();

            if (cameraRig != null)
            {
                cameraRig.SetHudController(hudController);
                cameraRig.SetTerrainCollider(worldRenderer != null ? worldRenderer.TerrainCollider : null);
                cameraRig.SetWorldBounds(map.Width, map.Height);
                cameraRig.SetCombatMode(true);
            }

            SpawnCombatUnitViews();
            CollectActiveUnitWorldPositions(_treeFadePositionsScratch);
            if (cameraRig != null)
            {
                cameraRig.FrameCombatantsInSafeViewport(_treeFadePositionsScratch, immediate: true);
            }
            worldRenderer?.UpdateTreeOcclusionFade(_treeFadePositionsScratch, fadeCenterBuildingsInCombat: true);
            RefreshUnitNameplates();

            hudController?.CloseShopModal();
            hudController?.TogglePartyPanel(false);
            hudController?.HideQuickResolveModal();
            hudController?.HideEncounterInterceptModal();
            hudController?.HideOutcomeModal();
            string combatStartToast = $"{title} started!";
            if (_activeCombatSurpriseMode == CombatInitiativeSurpriseMode.PartyAmbush)
            {
                combatStartToast = $"{title}: Ambush Advantage (+Round 1 Init)!";
            }
            else if (_activeCombatSurpriseMode == CombatInitiativeSurpriseMode.EnemySurprisedParty)
            {
                combatStartToast = $"{title}: Surprised in Darkness!";
            }
            hudController?.ShowToast(combatStartToast, 2.5f);

            audioDirector?.PlayMusic("battle", 0.7f);
            if (spawnedGiantFrog)
            {
                audioDirector?.PlayFrogCroak();
            }

            if (CombatSession.Phase == CombatPhase.Deployment)
            {
                RefreshDeploymentOverlays();
                SyncAllCombatUnitViews();
                RefreshUnitNameplates();
                RefreshCombatHudState();
            }
            else
            {
                BeginCurrentCombatTurn();
            }
        }

        private void RefreshDeploymentOverlays()
        {
            if (gridOverlays == null || CombatSession == null || CombatSession.Phase != CombatPhase.Deployment)
            {
                return;
            }

            gridOverlays.ClearAll();
            gridOverlays.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
            IReadOnlyList<GridCoord> localZone = CombatSession.DeploymentZoneTiles;
            List<GridCoord> worldZone = new List<GridCoord>(localZone.Count);
            for (int i = 0; i < localZone.Count; i++)
            {
                worldZone.Add(LocalToWorld(localZone[i]));
            }

            GridCoord? selectedWorld = null;
            if (!string.IsNullOrEmpty(_selectedDeploymentHeroId))
            {
                CombatantState sel = CombatSession.GetCombatant(_selectedDeploymentHeroId);
                if (sel != null)
                {
                    selectedWorld = LocalToWorld(sel.Position);
                }
            }

            gridOverlays.SetDeploymentZone(worldZone, selectedWorld);
        }

        private void HandleDeploymentClick(GridCoord localCoord)
        {
            if (CombatSession == null || CombatSession.Phase != CombatPhase.Deployment)
            {
                return;
            }

            CombatantState clicked = GetLivingUnitAt(localCoord);
            if (string.IsNullOrEmpty(_selectedDeploymentHeroId))
            {
                if (clicked != null && clicked.Side == CombatSide.Hero)
                {
                    _selectedDeploymentHeroId = clicked.Id;
                    audioDirector?.PlayUiClick();
                    SyncAllCombatUnitViews();
                    RefreshDeploymentOverlays();
                    hudController?.ShowToast($"Selected {clicked.Name}. Click another hero to swap or a cyan tile to move.", 2.2f);
                }
                return;
            }

            if (clicked != null && string.Equals(clicked.Id, _selectedDeploymentHeroId, StringComparison.Ordinal))
            {
                _selectedDeploymentHeroId = null;
                SyncAllCombatUnitViews();
                RefreshDeploymentOverlays();
                return;
            }

            if (CombatSession.SwapOrMoveHeroInDeployment(_selectedDeploymentHeroId, localCoord))
            {
                audioDirector?.PlayUiClick();
                _selectedDeploymentHeroId = null;
                SyncDeploymentHeroPositions(immediate: false);
                SyncAllCombatUnitViews();
                RefreshUnitNameplates();
                RefreshDeploymentOverlays();
                RefreshCombatHudState();
                hudController?.ShowToast("Deployment updated. Press [Start Battle] or [Space] when ready.", 1.6f);
                return;
            }

            if (clicked != null && clicked.Side == CombatSide.Hero)
            {
                _selectedDeploymentHeroId = clicked.Id;
                audioDirector?.PlayUiClick();
                SyncAllCombatUnitViews();
                RefreshDeploymentOverlays();
                hudController?.ShowToast($"Selected {clicked.Name}.", 1.8f);
            }
            else
            {
                hudController?.ShowToast("Place heroes on highlighted cyan deployment tiles.", 1.6f);
            }
        }

        private void SyncDeploymentHeroPositions(bool immediate)
        {
            if (CombatSession == null)
            {
                return;
            }

            bool snapNow = immediate || Application.isBatchMode || !Application.isPlaying || GetCombatMoveTileDuration() <= 0.001f;
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (c == null || c.Side != CombatSide.Hero || !c.IsAlive)
                {
                    continue;
                }

                if (!_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView view) || view == null)
                {
                    continue;
                }

                Vector3 targetWorld = GridToWorldAtElevation(LocalToWorld(c.Position));
                if (snapNow)
                {
                    view.SetWorldPosition(targetWorld);
                }
                else if ((view.transform.position - targetWorld).sqrMagnitude > 0.0004f)
                {
                    view.MoveAlongWorldPath(new[] { targetWorld }, 0.14f);
                }
            }
        }

        private void HandleStartBattleFromDeployment()
        {
            if (!_isInCombat || CombatSession == null || CombatSession.Phase != CombatPhase.Deployment)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            _selectedDeploymentHeroId = null;
            SyncDeploymentHeroPositions(immediate: true);
            gridOverlays?.ClearDeploymentZone();
            if (CombatSession.StartBattleFromDeployment())
            {
                hudController?.ShowToast("Battle begins!", 1.8f);
                BeginCurrentCombatTurn();
            }
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
                CombatSession.OnArenaIlluminationChanged -= OnCombatArenaIlluminationChanged;
                CombatSession.PropDetonated -= OnCombatPropDetonated;
                CombatSession.LightningChained -= OnCombatLightningChained;
            }
        }

        private void OnCombatArenaIlluminationChanged(bool illuminated)
        {
            ApplyAtmosphereAndTorchLighting(GetCombatPartyTorchWorldPos(), forceImmediate: true);
            RefreshCombatHudState();
            if (illuminated && _isInCombat)
            {
                hudController?.ShowToast("Firelight illuminates the battlefield! In the Dark lifted.", 2.4f);
            }
        }

        private Vector3 GetCombatPartyTorchWorldPos()
        {
            if (CombatSession != null)
            {
                float sumX = 0f;
                float sumY = 0f;
                int count = 0;
                for (int i = 0; i < CombatSession.Combatants.Count; i++)
                {
                    CombatantState c = CombatSession.Combatants[i];
                    if (c.Side == CombatSide.Hero && c.IsAlive)
                    {
                        GridCoord world = LocalToWorld(c.Position);
                        sumX += world.X + 0.5f;
                        sumY += world.Y + 0.5f;
                        count++;
                    }
                }
                if (count > 0)
                {
                    float avgX = sumX / count;
                    float avgY = sumY / count;
                    float elev = worldRenderer != null
                        ? worldRenderer.GetTileElevation(Mathf.RoundToInt(avgX), Mathf.RoundToInt(avgY))
                        : 0f;
                    return new Vector3(avgX, elev, -avgY);
                }
            }

            GridCoord fallbackPartySide = new GridCoord(_arenaOriginX + 2, _arenaOriginY + _arenaHeight / 2);
            return GridToWorldAtElevation(fallbackPartySide);
        }

        private void HandleTakeVictoryLoot(int[] selectedIndices)
        {
            audioDirector?.PlayUiClick();
            TacticalCombatSession targetSession = CombatSession ?? _pendingQuickResolveVictorySession;
            if (targetSession != null && ExploreSession?.Party != null)
            {
                int taken = targetSession.TakeLootItems(ExploreSession.Party, selectedIndices);
                if (taken > 0)
                {
                    audioDirector?.PlayTakeItem();
                }
            }
            _pendingQuickResolveVictorySession = null;

            if (_isInCombat)
            {
                ExitCombatToExploration(syncPartyFromCombat: true);
            }
            else
            {
                hudController?.HideOutcomeModal();
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false, _lastContextPoiLabel);
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
                        mat = assetCatalog.ResolveCartographyUnitMaterial(c.Id, mat);
                    }
                    else
                    {
                        BywaterAssetCatalog.MonsterArtEntry monArt = assetCatalog.GetMonsterArt(c.Id, c.IconId);
                        mat = monArt != null ? monArt.billboardMaterial : null;
                        string monSheetKey = monArt != null && !string.IsNullOrEmpty(monArt.key) ? monArt.key : c.Id;
                        mat = assetCatalog.ResolveCartographyUnitMaterial(monSheetKey, mat);
                    }
                }

                view.Configure(
                    c.Id,
                    c.Name,
                    mat,
                    ringColor,
                    font,
                    spriteSize: 2.0f,
                    showHud: true,
                    isAllyOverride: c.Side == CombatSide.Hero,
                    catalog: assetCatalog);
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
                    if (Application.isPlaying)
                    {
                        Destroy(view.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(view.gameObject);
                    }
                }
            }

            _combatUnitViews.Clear();
            _hoveredRimUnitId = null;
        }

        private void SyncAllCombatUnitViews()
        {
            if (CombatSession == null)
            {
                return;
            }

            bool inDeployment = CombatSession.Phase == CombatPhase.Deployment;
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
                bool isHighlighted = inDeployment
                    ? (!string.IsNullOrEmpty(_selectedDeploymentHeroId) && string.Equals(c.Id, _selectedDeploymentHeroId, StringComparison.Ordinal) && c.IsAlive)
                    : (c.Id == currentId && c.IsAlive);
                view.UpdateStats(c.Hp, c.MaxHp, c.Sp, c.MaxSp, badge, isHighlighted, c.IsAlive);
                view.UpdateConditionBadgeIcon(ResolveUnitConditionBadgeSprite(c));
            }
        }

        private Sprite ResolveUnitConditionBadgeSprite(CombatantState unit)
        {
            if (unit == null || assetCatalog == null || !unit.IsAlive)
            {
                return null;
            }

            if (unit.Conditions.Has(CharConditionId.Poisoned) || unit.Conditions.Has(CharConditionId.Diseased))
            {
                return assetCatalog.GetConditionBadgeIcon("poison");
            }

            if (CombatSession?.Grid != null &&
                CombatSession.Grid.InBounds(unit.Position) &&
                CombatSession.Grid.GetSurface(unit.Position) == SurfaceType.Fire)
            {
                return assetCatalog.GetConditionBadgeIcon("burn");
            }

            IReadOnlyList<ConditionDisplayEffect> effects = unit.Conditions.GetDisplayEffects();
            for (int i = 0; i < effects.Count; i++)
            {
                ConditionDisplayEffect eff = effects[i];
                if (eff == null) continue;
                string codeOrName = string.Concat(eff.Code ?? string.Empty, " ", eff.DisplayName ?? string.Empty);
                if (codeOrName.IndexOf("burn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    codeOrName.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    codeOrName.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return assetCatalog.GetConditionBadgeIcon("burn");
                }
            }

            if (unit.IsProne ||
                unit.Conditions.Has(CharConditionId.Helpless) ||
                unit.Conditions.Has(CharConditionId.Tangled) ||
                unit.Conditions.Has(CharConditionId.Slow) ||
                unit.Conditions.Has(CharConditionId.Stupid) ||
                unit.Conditions.Has(CharConditionId.Confused) ||
                unit.Conditions.Has(CharConditionId.Blind) ||
                unit.Conditions.Has(CharConditionId.TurnedToStone) ||
                unit.Conditions.Has(CharConditionId.Silenced) ||
                unit.Conditions.Has(CharConditionId.RunsAway))
            {
                return assetCatalog.GetConditionBadgeIcon("stun");
            }

            if (unit.IsGuarding ||
                unit.Conditions.Has(CharConditionId.Strong) ||
                unit.Conditions.Has(CharConditionId.AttackBonus) ||
                unit.Conditions.Has(CharConditionId.ShieldFromHits) ||
                unit.Conditions.Has(CharConditionId.ShieldFromProjectiles) ||
                unit.Conditions.Has(CharConditionId.Regenerating) ||
                unit.Conditions.Has(CharConditionId.MagicAura) ||
                unit.Conditions.Has(CharConditionId.DefenseBonus) ||
                unit.Conditions.Has(CharConditionId.Speedy) ||
                unit.Conditions.Has(CharConditionId.ProtectionFromEvil))
            {
                return assetCatalog.GetConditionBadgeIcon("bless");
            }

            return null;
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

            if (_lastCombatRoundForTorch <= 0)
            {
                _lastCombatRoundForTorch = CombatSession.RoundNumber;
            }
            else if (CombatSession.RoundNumber > _lastCombatRoundForTorch)
            {
                _lastCombatRoundForTorch = CombatSession.RoundNumber;
                if (ExploreSession?.Party != null && ExploreSession.Party.BurnTorchMinutes(1.0))
                {
                    hudController?.ShowToast("Your torch flickered out!", 2.4f);
                    ApplyAtmosphereAndTorchLighting(GetCombatPartyTorchWorldPos(), forceImmediate: false);
                }
            }

            SyncAllCombatUnitViews();
            RefreshUnitNameplates();
            Vector3 actorWorld = GridToWorldAtElevation(LocalToWorld(actor.Position));
            if (cameraRig != null && !cameraRig.IsWorldPointInSafeViewport(actorWorld))
            {
                cameraRig.EnsureWorldPointVisible(actorWorld);
            }
            if (!_isInCombat)
            {
                worldRenderer?.UpdateHeroWorldPosition(actorWorld);
            }
            else
            {
                ApplyAtmosphereAndTorchLighting(GetCombatPartyTorchWorldPos(), forceImmediate: false);
            }

            if (actor.Side == CombatSide.Monster)
            {
                gridOverlays?.ClearAll();
                gridOverlays?.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
                if (_showAllEnemyIntentArcs)
                {
                    RefreshEnemyIntentArcs();
                }
                else
                {
                    gridOverlays?.ClearEnemyIntentArcs();
                }
                hudController?.ClearCombatHoverPreview();
                RefreshCombatHudState();
                StopAiCoroutine();
                if (Application.isPlaying && gameObject.activeInHierarchy)
                {
                    _aiCoroutine = StartCoroutine(RunMonsterTurnsRoutine());
                }
                return;
            }

            if (!actor.IsConscious)
            {
                hudController?.ShowToast($"{actor.Name} is incapacitated and skips turn.", 2.0f);
                CombatSession.EndTurn();
                BeginCurrentCombatTurn();
                return;
            }

            if (_autoCombatEnabled)
            {
                gridOverlays?.ClearAll();
                gridOverlays?.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
                if (_showAllEnemyIntentArcs)
                {
                    RefreshEnemyIntentArcs();
                }
                else
                {
                    gridOverlays?.ClearEnemyIntentArcs();
                }
                hudController?.ClearCombatHoverPreview();
                RefreshCombatHudState();
                StopAiCoroutine();
                if (Application.isPlaying && gameObject.activeInHierarchy)
                {
                    _aiCoroutine = StartCoroutine(RunAutoCombatHeroTurnRoutine());
                }
                return;
            }

            _activeCombatMode = CombatActionMode.Smart;

            if (actor.SpellIds.Count > 0 && !ContainsInt(actor.SpellIds, _selectedSpellId))
            {
                _selectedSpellId = actor.SpellIds[0];
            }
            else if (actor.SpellIds.Count == 0)
            {
                _selectedSpellId = -1;
            }

            RefreshCombatStaticOverlays(actor);
            hudController?.ClearCombatHoverPreview();
            RefreshCombatHudState();
        }

        private IEnumerator RunMonsterTurnsRoutine()
        {
            while (CombatSession != null &&
                   CombatSession.Outcome == CombatOutcome.InProgress &&
                   CombatSession.CurrentActor != null &&
                   CombatSession.CurrentActor.Side == CombatSide.Monster)
            {
                CombatantState monster = CombatSession.CurrentActor;
                float delay = SpeedDelays[_speedIndex];
                if (delay > 0.001f)
                {
                    float flashDuration = Mathf.Min(EnemyIntentFlashSeconds, delay);
                    float leadIn = Mathf.Max(0f, delay - flashDuration);
                    if (leadIn > 0.001f)
                    {
                        yield return new WaitForSeconds(leadIn);
                    }

                    if (CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
                    {
                        break;
                    }

                    monster = CombatSession.CurrentActor;
                    if (monster == null || monster.Side != CombatSide.Monster)
                    {
                        break;
                    }

                    if (!_showAllEnemyIntentArcs)
                    {
                        ShowSingleEnemyIntentArc(monster);
                    }
                    yield return new WaitForSeconds(flashDuration);
                }
                else
                {
                    yield return null;
                }

                if (CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
                {
                    break;
                }

                monster = CombatSession.CurrentActor;
                if (monster == null || monster.Side != CombatSide.Monster)
                {
                    break;
                }

                if (!_showAllEnemyIntentArcs)
                {
                    gridOverlays?.ClearEnemyIntentArcs();
                }

                if (IsGiantFrog(monster.Id, monster.IconId))
                {
                    audioDirector?.PlayFrogCroak();
                }

                GridCoord oldPos = monster.Position;
                Dictionary<string, int> oldHps = CaptureCombatantHps();
                float moveDur = GetCombatMoveTileDuration();
                float attackDur = GetCombatAttackDuration();

                CombatActionResult res = CombatSession.ExecuteMonsterAiTurn();

                if (monster.Position != oldPos && _combatUnitViews.TryGetValue(monster.Id, out BywaterUnitView mView) && mView != null)
                {
                    if (moveDur <= 0.001f)
                    {
                        mView.SetWorldPosition(GridToWorldAtElevation(LocalToWorld(monster.Position)));
                    }
                    else
                    {
                        BuildCombatMoveWorldPath(oldPos, monster.Position, _combatPathWorldScratch);
                        mView.MoveAlongWorldPath(_combatPathWorldScratch, moveDur);
                        float walkWait = _combatPathWorldScratch.Count * moveDur;
                        if (walkWait > 0.001f)
                        {
                            yield return new WaitForSeconds(walkWait);
                        }
                    }
                }

                if (CombatSession == null)
                {
                    break;
                }

                ApplyMonsterActionJuice(monster, res, oldHps);
                SyncDestructibleArenaProps();
                ShowHpDeltaFloatingText(oldHps);
                SyncAllCombatUnitViews();
                RefreshUnitNameplates();
                gridOverlays?.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
                if (_showAllEnemyIntentArcs)
                {
                    RefreshEnemyIntentArcs();
                }
                RefreshCombatHudState();

                if (attackDur > 0.001f &&
                    (res.AttackOutcomes.Count > 0 || res.HealingDone > 0 ||
                     res.Message.IndexOf("casts ", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    yield return new WaitForSeconds(attackDur);
                }

                if (CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
                {
                    break;
                }
            }

            _aiCoroutine = null;
            BeginCurrentCombatTurn();
        }

        private IEnumerator RunAutoCombatHeroTurnRoutine()
        {
            while (_autoCombatEnabled &&
                   CombatSession != null &&
                   CombatSession.Outcome == CombatOutcome.InProgress &&
                   CombatSession.CurrentActor != null &&
                   CombatSession.CurrentActor.Side == CombatSide.Hero)
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

                if (!_autoCombatEnabled || CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
                {
                    break;
                }

                CombatantState hero = CombatSession.CurrentActor;
                if (hero == null || hero.Side != CombatSide.Hero)
                {
                    break;
                }

                GridCoord oldPos = hero.Position;
                Dictionary<string, int> oldHps = CaptureCombatantHps();
                float moveDur = GetCombatMoveTileDuration();
                float attackDur = GetCombatAttackDuration();

                CombatActionResult res = CombatSession.ExecutePartyAiTurn();

                if (hero.Position != oldPos && _combatUnitViews.TryGetValue(hero.Id, out BywaterUnitView hView) && hView != null)
                {
                    Vector3 targetWorld = GridToWorldAtElevation(LocalToWorld(hero.Position));
                    if (moveDur <= 0.001f)
                    {
                        hView.SetWorldPosition(targetWorld);
                    }
                    else
                    {
                        BuildCombatMoveWorldPath(oldPos, hero.Position, _combatPathWorldScratch);
                        hView.MoveAlongWorldPath(_combatPathWorldScratch, moveDur);
                        float walkWait = _combatPathWorldScratch.Count * moveDur;
                        if (walkWait > 0.001f)
                        {
                            yield return new WaitForSeconds(walkWait);
                        }
                    }

                    TileMapData map = ExploreSession.CurrentMap;
                    GridCoord worldStep = LocalToWorld(hero.Position);
                    TileRule rule = map.GetTileRule(map.GetTile(worldStep.X, worldStep.Y));
                    audioDirector?.PlayFootstep(rule.SoundId, map.IsDungeon);
                    worldRenderer?.UpdateHeroWorldPosition(targetWorld);
                }

                if (CombatSession == null)
                {
                    break;
                }

                ApplyHeroAutoActionJuice(hero, res, oldHps);
                SyncDestructibleArenaProps();
                ShowHpDeltaFloatingText(oldHps);
                SyncAllCombatUnitViews();
                RefreshUnitNameplates();
                gridOverlays?.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
                RefreshCombatHudState();

                if (attackDur > 0.001f &&
                    (res.AttackOutcomes.Count > 0 || res.HealingDone > 0 ||
                     res.Message.IndexOf("casts ", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    yield return new WaitForSeconds(attackDur);
                }

                if (CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
                {
                    break;
                }
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
            CombatantState healedAlly = null;
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (!oldHps.TryGetValue(c.Id, out int before))
                {
                    continue;
                }

                if (c.Side == CombatSide.Hero && c.Hp < before)
                {
                    if (hitHero == null)
                    {
                        hitHero = c;
                    }
                    if (_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView hView) && hView != null)
                    {
                        hView.PlayHitFlash(0.055f);
                    }
                }
                else if (c.Side == CombatSide.Monster && c.Hp > before)
                {
                    healedAlly = c;
                }
            }

            CombatantState targetUnit = hitHero ?? healedAlly ?? ResolveLastLogTargetCombatant(monster.Id);
            Vector3 startWorld = GridToWorldAtElevation(LocalToWorld(monster.Position));
            Vector3 targetWorld = targetUnit != null
                ? GridToWorldAtElevation(LocalToWorld(targetUnit.Position))
                : startWorld;

            bool isSpell = res.Message.IndexOf("casts ", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isSpell && gridOverlays != null)
            {
                string spellVfx = ResolveSpellVfxKeyFromMessage(res.Message, res.HealingDone > 0, out bool isProjectile);
                gridOverlays.PlayCombatVfx(spellVfx, startWorld, targetWorld, isProjectile);
            }
            else if (res.HealingDone > 0 && gridOverlays != null)
            {
                gridOverlays.PlayCombatVfx("heal_sparkle", startWorld, targetWorld, isProjectile: false);
            }

            if (hitHero != null)
            {
                if (_combatUnitViews.TryGetValue(monster.Id, out BywaterUnitView mView) && mView != null)
                {
                    mView.PlayAttackLunge(GridToWorldAtElevation(LocalToWorld(hitHero.Position)), 0.28f, GetCombatAttackDuration());
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

                bool isRanged = res.Message.IndexOf("fires at", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (monster.PrefersRanged && monster.Position.ChebyshevDistance(hitHero.Position) > 1);
                if (!isSpell && gridOverlays != null)
                {
                    gridOverlays.PlayCombatVfx(
                        isRanged ? "arrow_bolt" : "slash_arc",
                        startWorld,
                        targetWorld,
                        isProjectile: isRanged);
                }

                audioDirector?.PlayAttack(isRanged, isCrit);
                cameraRig?.PlayImpactImpulse(isCrit ? 0.18f : 0.12f, 0.14f, targetWorld - startWorld);
                TriggerHitStop(isCrit ? 0.055f : 0.040f);
            }
            else if (!isSpell && res.AttackOutcomes.Count > 0 && targetUnit != null && gridOverlays != null)
            {
                bool isRanged = res.Message.IndexOf("fires at", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (monster.PrefersRanged && monster.Position.ChebyshevDistance(targetUnit.Position) > 1);
                gridOverlays.PlayCombatVfx(
                    isRanged ? "arrow_bolt" : "slash_arc",
                    startWorld,
                    targetWorld,
                    isProjectile: isRanged);
            }
        }

        private bool TryPickCombatUnitUnderScreenPoint(Vector2 screenPos, out GridCoord worldCoord)
        {
            worldCoord = default;
            if (CombatSession == null || cameraRig == null)
            {
                return false;
            }

            Camera cam = cameraRig.GetComponentInChildren<Camera>();
            if (cam == null)
            {
                cam = Camera.main;
            }
            if (cam == null)
            {
                return false;
            }

            float closestDistSq = float.MaxValue;
            GridCoord bestCoord = default;
            bool found = false;

            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (c == null || !c.IsAlive)
                {
                    continue;
                }

                if (!_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView view) || view == null)
                {
                    continue;
                }

                if (view.TryGetScreenBounds(cam, out Rect rect) && rect.Contains(screenPos))
                {
                    float distSq = (view.transform.position - cam.transform.position).sqrMagnitude;
                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        bestCoord = LocalToWorld(c.Position);
                        found = true;
                    }
                }
            }

            if (found)
            {
                worldCoord = bestCoord;
                return true;
            }

            return false;
        }

        private void UpdateCombatInput(GridCoord worldCoord, bool leftClick)
        {
            if (CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
            {
                return;
            }

            if (CombatSession.Phase == CombatPhase.Deployment)
            {
                GridCoord deployLocal = WorldToLocal(worldCoord);
                if (!CombatSession.Grid.InBounds(deployLocal))
                {
                    if (!_isHoveringHotbarUi)
                    {
                        hudController?.ClearCombatHoverPreview();
                    }
                    return;
                }

                if (deployLocal != _lastCombatHover || _combatHoverDirty)
                {
                    _lastCombatHover = deployLocal;
                    _combatHoverDirty = false;
                    CombatantState hoverUnit = GetLivingUnitAt(deployLocal);
                    if (hoverUnit != null && hoverUnit.Side == CombatSide.Hero)
                    {
                        string hint = string.IsNullOrEmpty(_selectedDeploymentHeroId)
                            ? "Click to select hero for swap/move."
                            : (hoverUnit.Id == _selectedDeploymentHeroId ? "Currently selected. Click to deselect." : "Click to swap hero positions.");
                        hudController?.SetCombatHoverPreview(
                            $"{hoverUnit.Name} ({hoverUnit.FormationRank} Rank)",
                            $"HP {hoverUnit.Hp}/{hoverUnit.MaxHp} | AC {hoverUnit.EffectiveAc} | {hoverUnit.Economy.MoveBudget} MP\n{hint}");
                    }
                    else if (CombatSession.IsValidDeploymentTile(deployLocal))
                    {
                        string tileHint = string.IsNullOrEmpty(_selectedDeploymentHeroId)
                            ? "Select a hero first, then click this cyan tile to reposition."
                            : "Click cyan tile to deploy selected hero here.";
                        hudController?.SetCombatHoverPreview(
                            $"Deployment Tile ({deployLocal.X}, {deployLocal.Y})",
                            $"{tileHint}\nPress [Start Battle] or [Space] to begin.");
                    }
                    else
                    {
                        hudController?.ClearCombatHoverPreview();
                    }
                }

                if (leftClick)
                {
                    HandleDeploymentClick(deployLocal);
                }
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor == null || actor.Side != CombatSide.Hero || _autoCombatEnabled)
            {
                return;
            }

            GridCoord localCoord = WorldToLocal(worldCoord);
            if (!CombatSession.Grid.InBounds(localCoord))
            {
                if (!_isHoveringHotbarUi)
                {
                    ClearCombatTransientHoverVisuals();
                }
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

            if (CombatSession.Phase == CombatPhase.Deployment)
            {
                RefreshDeploymentOverlays();
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
            if (_showAllEnemyIntentArcs)
            {
                RefreshEnemyIntentArcs();
            }
            else
            {
                gridOverlays.ClearEnemyIntentArcs();
            }
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
                if (TryBuildEnemyIntentArc(c, out EnemyIntentArcData arc))
                {
                    arcs.Add(arc);
                }
            }

            gridOverlays.ShowEnemyIntentArcs(arcs);
        }

        private bool TryBuildEnemyIntentArc(CombatantState enemy, out EnemyIntentArcData arc)
        {
            arc = default;
            if (CombatSession == null || enemy == null || !enemy.IsAlive || !enemy.IsConscious || enemy.Side != CombatSide.Monster)
            {
                return false;
            }

            EnemyIntentPrediction intent = CombatSession.PredictNextIntent(enemy.Id);
            if (intent == null ||
                intent.ActionKind == CombatIntentActionType.Hold ||
                intent.ActionKind == CombatIntentActionType.Guard ||
                intent.ActionKind == CombatIntentActionType.Flee)
            {
                return false;
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

            if (dest == enemy.Position)
            {
                return false;
            }

            bool isSpell = intent.ActionKind == CombatIntentActionType.CastSpell ||
                           intent.ActionKind == CombatIntentActionType.MoveAndCast;
            arc = new EnemyIntentArcData
            {
                From = LocalToWorld(enemy.Position),
                To = LocalToWorld(dest),
                IsSpell = isSpell
            };
            return true;
        }

        public string FormatEnemyIntentSummary(CombatantState enemy)
        {
            if (CombatSession == null || enemy == null || !enemy.IsAlive || enemy.Side != CombatSide.Monster)
            {
                return string.Empty;
            }

            if (!enemy.IsConscious)
            {
                return "Next Enemy Intent: Incapacitated";
            }

            EnemyIntentPrediction intent = CombatSession.PredictNextIntent(enemy.Id);
            if (intent == null)
            {
                return string.Empty;
            }

            string targetLabel = string.Empty;
            if (!string.IsNullOrEmpty(intent.TargetId))
            {
                CombatantState tgt = CombatSession.GetCombatant(intent.TargetId);
                if (tgt != null)
                {
                    targetLabel = tgt.Name;
                }
            }

            string actionLabel;
            switch (intent.ActionKind)
            {
                case CombatIntentActionType.MeleeAttack:
                    actionLabel = "Melee Strike";
                    break;
                case CombatIntentActionType.MoveAndAttack:
                    actionLabel = "Move + Melee";
                    break;
                case CombatIntentActionType.RangedAttack:
                    actionLabel = "Ranged Shot";
                    break;
                case CombatIntentActionType.MoveAndRanged:
                    actionLabel = "Move + Ranged";
                    break;
                case CombatIntentActionType.CastSpell:
                case CombatIntentActionType.MoveAndCast:
                case CombatIntentActionType.HealAlly:
                    string spellTitle = intent.SpellKey;
                    if (Bundle?.Spells != null)
                    {
                        foreach (SpellDefinition s in Bundle.Spells.Values)
                        {
                            if (string.Equals(s.Key, intent.SpellKey, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(s.Id.ToString(), intent.SpellKey, StringComparison.OrdinalIgnoreCase))
                            {
                                spellTitle = s.Name;
                                break;
                            }
                        }
                    }
                    string baseVerb = intent.ActionKind == CombatIntentActionType.HealAlly ? "Heal" : "Cast";
                    string prefix = intent.ActionKind == CombatIntentActionType.MoveAndCast ? "Move + " : string.Empty;
                    actionLabel = string.IsNullOrWhiteSpace(spellTitle)
                        ? $"{prefix}{baseVerb}"
                        : $"{prefix}{baseVerb} {spellTitle}";
                    break;
                case CombatIntentActionType.Guard:
                    actionLabel = intent.HasPlannedMove ? "Move + Guard" : "Guard";
                    break;
                case CombatIntentActionType.Flee:
                    return "Next Enemy Intent: Flee";
                case CombatIntentActionType.Hold:
                    return "Next Enemy Intent: Hold Position";
                default:
                    actionLabel = string.IsNullOrWhiteSpace(intent.ActionType) ? "Hold" : intent.ActionType;
                    break;
            }

            return !string.IsNullOrEmpty(targetLabel)
                ? $"Next Enemy Intent: {actionLabel} -> {targetLabel}"
                : $"Next Enemy Intent: {actionLabel}";
        }

        private void ShowSingleEnemyIntentArc(CombatantState enemy)
        {
            if (gridOverlays == null)
            {
                return;
            }

            if (TryBuildEnemyIntentArc(enemy, out EnemyIntentArcData arc))
            {
                gridOverlays.ShowEnemyIntentArcs(new[] { arc });
            }
            else if (!_showAllEnemyIntentArcs)
            {
                gridOverlays.ClearEnemyIntentArcs();
            }
        }

        private void ShowAoOThreatArcs(IReadOnlyList<string> threateningEnemyIds, GridCoord aooTileLocal)
        {
            if (gridOverlays == null || CombatSession == null || threateningEnemyIds == null || threateningEnemyIds.Count == 0)
            {
                return;
            }

            List<EnemyIntentArcData> arcs = new List<EnemyIntentArcData>(threateningEnemyIds.Count);
            GridCoord worldDest = LocalToWorld(aooTileLocal);
            for (int i = 0; i < threateningEnemyIds.Count; i++)
            {
                CombatantState enemy = CombatSession.GetCombatant(threateningEnemyIds[i]);
                if (enemy != null && enemy.IsAlive && enemy.IsConscious && enemy.Position != aooTileLocal)
                {
                    arcs.Add(new EnemyIntentArcData
                    {
                        From = LocalToWorld(enemy.Position),
                        To = worldDest,
                        IsSpell = false
                    });
                }
            }

            if (arcs.Count > 0)
            {
                gridOverlays.ShowEnemyIntentArcs(arcs);
            }
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

            if (!_isHoveringHotbarUi)
            {
                gridOverlays?.SetCombatHoverPath(null, false);
                gridOverlays?.SetLosPreview(null, default, default);
                gridOverlays?.SetSpellAoeFootprint(null, null, null, false);
                gridOverlays?.SetFlankIndicator(null, null);
                gridOverlays?.ClearShovePreview();
                gridOverlays?.SetHoveredAttackTarget(null);
                if (!_showAllEnemyIntentArcs)
                {
                    gridOverlays?.ClearEnemyIntentArcs();
                }
            }

            CombatantState hoveredUnit = GetLivingUnitAt(localCoord);
            _hoveredCombatUnitId = hoveredUnit != null ? hoveredUnit.Id : null;
            SetHoveredCombatUnitRim(hoveredUnit);
            RefreshUnitNameplates();

            string hoveredEnemyIntentLine = string.Empty;
            if (hoveredUnit != null &&
                hoveredUnit.Side == CombatSide.Monster &&
                (_activeCombatMode == CombatActionMode.Smart ||
                 _activeCombatMode == CombatActionMode.Melee ||
                 _activeCombatMode == CombatActionMode.Ranged ||
                 _activeCombatMode == CombatActionMode.Shove))
            {
                gridOverlays?.SetHoveredAttackTarget(LocalToWorld(hoveredUnit.Position));
            }

            switch (_activeCombatMode)
            {
                case CombatActionMode.Smart:
                {
                    if (hoveredUnit != null && hoveredUnit.Side == CombatSide.Monster)
                    {
                        bool canMeleeAction = actor.Economy.HasSlot(CombatActionSlot.MainAction);
                        bool canRangedAction = actor.Economy.HasSlot(CombatActionSlot.MainAction);
                        bool isAdjacent = actor.Position.ChebyshevDistance(hoveredUnit.Position) <= 1;
                        bool hasRanged = actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice);
                        TacticalAttackPreview meleePreview = CombatSession.PreviewMeleeAttack(actor.Id, hoveredUnit.Id);
                        TacticalAttackPreview rangedPreview = hasRanged ? CombatSession.PreviewRangedAttack(actor.Id, hoveredUnit.Id) : null;
                        ReachableTileInfo approachTile = null;
                        TacticalAttackPreview approachMelee = null;
                        bool canApproachMelee = canMeleeAction &&
                                                actor.Economy.MoveRemaining > 0 &&
                                                FindBestSmartApproachTile(actor, hoveredUnit, out approachTile, out approachMelee);

                        if (isAdjacent && canMeleeAction)
                        {
                            ShowAttackTooltip($"[{actor.Name}] Melee vs {hoveredUnit.Name}", meleePreview, isRanged: false, suffixLine: hoveredEnemyIntentLine);
                            UpdateFlankOverlay(actor, hoveredUnit, meleePreview.IsFlanking);
                        }
                        else if (hasRanged && canRangedAction && rangedPreview != null && rangedPreview.CanExecute && (!canApproachMelee || actor.PrefersRanged))
                        {
                            ShowAttackTooltip($"[{actor.Name}] Ranged vs {hoveredUnit.Name}", rangedPreview, isRanged: true, suffixLine: hoveredEnemyIntentLine);
                            if (rangedPreview.LosResult != null)
                            {
                                gridOverlays?.SetLosPreview(
                                    OffsetLosResult(rangedPreview.LosResult),
                                    LocalToWorld(actor.Position),
                                    LocalToWorld(hoveredUnit.Position),
                                    rangedPreview.AdvantageState == RollAdvantageState.Disadvantage);
                            }
                        }
                        else if (canApproachMelee && approachTile != null && approachMelee != null &&
                                 approachTile.PathCost <= actor.Economy.MoveRemaining)
                        {
                            List<GridCoord> worldPath = new List<GridCoord>(approachTile.Path.Count);
                            for (int i = 0; i < approachTile.Path.Count; i++)
                            {
                                worldPath.Add(LocalToWorld(approachTile.Path[i]));
                            }
                            gridOverlays?.SetCombatHoverPath(worldPath, approachTile.ProvokesAoO);
                            ShowAttackTooltip(
                                $"[{actor.Name}] Approach & Melee: {hoveredUnit.Name}",
                                approachMelee,
                                isRanged: false,
                                prefixLine: $"Move: {approachTile.PathCost} MP + {approachMelee.ApCost} AP Strike\n",
                                suffixLine: hoveredEnemyIntentLine);
                            UpdateFlankOverlayFrom(approachTile.Coord, actor, hoveredUnit, approachMelee.IsFlanking);
                        }
                        else if (hasRanged && rangedPreview != null)
                        {
                            ShowAttackTooltip($"[{actor.Name}] Ranged vs {hoveredUnit.Name}", rangedPreview, isRanged: true, suffixLine: hoveredEnemyIntentLine);
                            if (rangedPreview.LosResult != null)
                            {
                                gridOverlays?.SetLosPreview(
                                    OffsetLosResult(rangedPreview.LosResult),
                                    LocalToWorld(actor.Position),
                                    LocalToWorld(hoveredUnit.Position),
                                    rangedPreview.AdvantageState == RollAdvantageState.Disadvantage);
                            }
                        }
                        else
                        {
                            ShowAttackTooltip($"[{actor.Name}] Melee vs {hoveredUnit.Name}", meleePreview, isRanged: false, suffixLine: hoveredEnemyIntentLine);
                        }
                        break;
                    }

                    if (hoveredUnit != null && hoveredUnit.Side == CombatSide.Hero)
                    {
                        hudController?.SetCombatHoverPreview(
                            $"{hoveredUnit.Name} (Ally)",
                            $"HP {hoveredUnit.Hp}/{hoveredUnit.MaxHp} | SP {hoveredUnit.Sp}/{hoveredUnit.MaxSp} | AC {hoveredUnit.EffectiveAc}");
                        break;
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
                        if (match.ProvokesAoO && !_showAllEnemyIntentArcs && !_isHoveringHotbarUi)
                        {
                            ShowAoOThreatArcs(match.ThreateningEnemyIds, localCoord);
                        }

                        string aooNote = match.ProvokesAoO
                            ? "WARNING: Path provokes Attack of Opportunity (AoO)!"
                            : "Safe path (no AoO provoked).";
                        string tileCostTag = CombatSession.GetTileMoveApCost(localCoord.X, localCoord.Y) >= 2 ? " (Mud/Water 2 MP)" : string.Empty;
                        hudController?.SetCombatHoverPreview(
                            $"Move {actor.Name} to ({localCoord.X}, {localCoord.Y})",
                            $"Cost: {match.PathCost} MP / {actor.Economy.MoveRemaining} MP remaining{tileCostTag}\n{aooNote}\n\nClick to step.");
                    }
                    else if (!_isHoveringHotbarUi)
                    {
                        hudController?.ClearCombatHoverPreview();
                    }
                    break;
                }

                case CombatActionMode.Melee:
                {
                    if (hoveredUnit != null && hoveredUnit.Side == CombatSide.Monster)
                    {
                        TacticalAttackPreview preview = CombatSession.PreviewMeleeAttack(actor.Id, hoveredUnit.Id);
                        ShowAttackTooltip($"[{actor.Name}] Melee vs {hoveredUnit.Name}", preview, isRanged: false, suffixLine: hoveredEnemyIntentLine);
                        UpdateFlankOverlay(actor, hoveredUnit, preview.IsFlanking);
                    }
                    else if (hoveredUnit == null && TryFindReachableTileInfo(actor.Id, localCoord, out ReachableTileInfo meleeMoveMatch))
                    {
                        List<GridCoord> worldPath = new List<GridCoord>(meleeMoveMatch.Path.Count);
                        for (int i = 0; i < meleeMoveMatch.Path.Count; i++)
                        {
                            worldPath.Add(LocalToWorld(meleeMoveMatch.Path[i]));
                        }

                        gridOverlays?.SetCombatHoverPath(worldPath, meleeMoveMatch.ProvokesAoO);
                        string aooNote = meleeMoveMatch.ProvokesAoO
                            ? "WARNING: Path provokes Attack of Opportunity (AoO)!"
                            : "Safe path.";
                        hudController?.SetCombatHoverPreview(
                            $"Move {actor.Name} to ({localCoord.X}, {localCoord.Y})",
                            $"Cost: {meleeMoveMatch.PathCost} MP / {actor.Economy.MoveRemaining} MP remaining\n{aooNote}\n\nClick tile to exit Melee and move.");
                    }
                    else if (!_isHoveringHotbarUi)
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
                        ShowAttackTooltip($"[{actor.Name}] Ranged vs {hoveredUnit.Name}", preview, isRanged: true, suffixLine: hoveredEnemyIntentLine);
                        if (preview.LosResult != null)
                        {
                            gridOverlays?.SetLosPreview(
                                OffsetLosResult(preview.LosResult),
                                LocalToWorld(actor.Position),
                                LocalToWorld(hoveredUnit.Position),
                                preview.AdvantageState == RollAdvantageState.Disadvantage);
                        }
                    }
                    else
                    {
                        LosResult tileLos = CombatSession.CheckLineOfSight(actor.Position, localCoord, includeSteamObscuration: true);
                        bool hasDisadv = CombatSession.IsInTheDark;
                        gridOverlays?.SetLosPreview(
                            OffsetLosResult(tileLos),
                            LocalToWorld(actor.Position),
                            LocalToWorld(localCoord),
                            hasDisadv);
                        if (!_isHoveringHotbarUi)
                        {
                            string losText = tileLos.Kind == LosKind.Blocked
                                ? "Blocked Sightline (Obstacle in path)"
                                : (tileLos.Kind == LosKind.PartialCover || hasDisadv ? "Penalized Sightline (Cover / Darkness)" : "Clear Line of Sight");
                            hudController?.SetCombatTooltip("Ranged Mode", $"{losText}\n{GetIdleModeInstructions(CombatActionMode.Ranged)}");
                        }
                    }
                    break;
                }

                case CombatActionMode.Spell:
                {
                    if (_selectedSpellId < 0 || !Bundle.Spells.TryGetValue(_selectedSpellId, out SpellDefinition spell))
                    {
                        if (!_isHoveringHotbarUi)
                        {
                            hudController?.SetCombatTooltip("Spell Mode", "Select a spell from the hotbar.");
                        }
                        break;
                    }

                    TacticalSpellPreview preview = CombatSession.PreviewSpell(actor.Id, spell.Id.ToString(), localCoord);
                    if (spell.Range > 0)
                    {
                        LosResult spellLos = CombatSession.CheckLineOfSight(actor.Position, localCoord, includeSteamObscuration: false);
                        gridOverlays?.SetLosPreview(
                            OffsetLosResult(spellLos),
                            LocalToWorld(actor.Position),
                            LocalToWorld(localCoord),
                            hasDisadvantagePenalty: false);
                    }
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
                    string statusLine = preview.CanExecute ? $"Cost: Main Action | READY TO CAST (Click to fire)" : $"Blocked: {preview.Reason}";
                    hudController?.SetCombatTooltip(
                        $"Spell: {spell.Name} (Action | {spell.SpCost} SP)",
                        $"Cost: Main Action   |   Mana: {spell.SpCost} SP\n" +
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
                            $"Healing Potion: {potionDef.Name} (Bonus Action)",
                            $"Heal Dice: {potionDef.HealDice}\nCost: Bonus Action\n\n{targetText}");
                    }
                    else if (!_isHoveringHotbarUi)
                    {
                        hudController?.SetCombatTooltip("Healing Potion", "No healing potions left in party stash.");
                    }
                    break;
                }

                case CombatActionMode.Shove:
                {
                    if (hoveredUnit != null && hoveredUnit.Id != actor.Id)
                    {
                        TacticalShovePreview preview = CombatSession.PreviewShove(actor.Id, hoveredUnit.Id);
                        ShoveHazardVisualHint hazardHint = new ShoveHazardVisualHint
                        {
                            TriggersKegExplosion = preview.TriggersKegExplosion,
                            IgnitesOnBrazier = preview.IgnitesOnBrazier,
                            CrushesWall = preview.HitsWallOrBounds || !string.IsNullOrEmpty(preview.CollidingUnitId),
                            PushesOffLedge = preview.PushesOffLedge,
                            DestroysCover = preview.DestroysCover,
                            EntersSurface = preview.DestinationSurface,
                            HazardBadgeText = preview.HazardBadgeText
                        };
                        gridOverlays?.ShowShovePreview(
                            LocalToWorld(actor.Position),
                            LocalToWorld(hoveredUnit.Position),
                            OffsetShovePreview(preview),
                            hazardHint);

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
                        string statusLine = preview.CanExecute ? $"Cost: {preview.ApCost} AP | Click to shove." : $"Blocked: {preview.Reason}";

                        string intentSuffix = string.IsNullOrEmpty(hoveredEnemyIntentLine) ? string.Empty : $"\n{hoveredEnemyIntentLine}";
                        hudController?.SetCombatHoverPreview(
                            $"Shove: {hoveredUnit.Name} ({preview.ApCost} AP)",
                            $"Cost: {preview.ApCost} AP\n{chanceLine}{hazardLine}{surfaceLine}{intentSuffix}\n\n{statusLine}",
                            badge);
                    }
                    else if (!_isHoveringHotbarUi)
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
                case CombatActionMode.Smart:
                {
                    if (clickedUnit != null && clickedUnit.Side == CombatSide.Monster)
                    {
                        bool isAdjacent = actor.Position.ChebyshevDistance(clickedUnit.Position) <= 1;
                        if (isAdjacent)
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

                        bool hasRanged = actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice);
                        TacticalAttackPreview rangedPreview = hasRanged
                            ? CombatSession.PreviewRangedAttack(actor.Id, clickedUnit.Id)
                            : null;
                        ReachableTileInfo bestTile = null;
                        bool canApproachMelee = actor.Economy.HasSlot(CombatActionSlot.MainAction) &&
                                                actor.Economy.MoveRemaining > 0 &&
                                                FindBestSmartApproachTile(actor, clickedUnit, out bestTile, out _);

                        if (hasRanged && rangedPreview != null && rangedPreview.CanExecute && (!canApproachMelee || actor.PrefersRanged))
                        {
                            CombatActionResult rangedRes = CombatSession.ExecuteRangedAttack(clickedUnit.Id);
                            if (rangedRes.Success)
                            {
                                PlayAttackMicroJuice(actor, clickedUnit, rangedRes, isRanged: true, oldHps, out string critId);
                                FinishPlayerCombatAction(rangedRes, oldHps, critId);
                            }
                            else
                            {
                                hudController?.ShowToast(rangedRes.Message, 2.0f);
                            }
                            return;
                        }

                        if (canApproachMelee && bestTile != null && bestTile.PathCost <= actor.Economy.MoveRemaining)
                        {
                            CombatActionResult stepRes = ExecuteCombatMoveStep(actor, bestTile.Coord);
                            if (!stepRes.Success)
                            {
                                hudController?.ShowToast(stepRes.Message, 1.8f);
                                return;
                            }

                            if (CombatSession.Outcome != CombatOutcome.InProgress)
                            {
                                HandleCombatOutcome();
                                return;
                            }

                            if (actor.IsConscious &&
                                actor.Position.ChebyshevDistance(clickedUnit.Position) <= 1 &&
                                actor.Economy.HasSlot(CombatActionSlot.MainAction) &&
                                clickedUnit.IsAlive)
                            {
                                Dictionary<string, int> postMoveHps = CaptureCombatantHps();
                                CombatActionResult atkRes = CombatSession.ExecuteMeleeAttack(clickedUnit.Id);
                                if (atkRes.Success)
                                {
                                    PlayAttackMicroJuice(actor, clickedUnit, atkRes, isRanged: false, postMoveHps, out string critId);
                                    FinishPlayerCombatAction(atkRes, postMoveHps, critId);
                                    return;
                                }
                                else
                                {
                                    hudController?.ShowToast(atkRes.Message, 2.0f);
                                }
                            }

                            TryAutoEndTurnIfExhausted(actor);
                            return;
                        }

                        if (hasRanged && rangedPreview != null)
                        {
                            hudController?.ShowToast(!string.IsNullOrEmpty(rangedPreview.Reason) ? rangedPreview.Reason : "Target out of range.", 1.8f);
                        }
                        else
                        {
                            TacticalAttackPreview melee = CombatSession.PreviewMeleeAttack(actor.Id, clickedUnit.Id);
                            hudController?.ShowToast(!string.IsNullOrEmpty(melee.Reason) ? melee.Reason : "Target out of reach.", 1.8f);
                        }
                        return;
                    }

                    if (clickedUnit != null && clickedUnit.Side == CombatSide.Hero)
                    {
                        hudController?.SetCombatHoverPreview(
                            $"{clickedUnit.Name} (Ally)",
                            $"HP {clickedUnit.Hp}/{clickedUnit.MaxHp} | SP {clickedUnit.Sp}/{clickedUnit.MaxSp} | AC {clickedUnit.EffectiveAc}");
                        return;
                    }

                    CombatActionResult moveRes = ExecuteCombatMoveStep(actor, localCoord);
                    if (!moveRes.Success)
                    {
                        hudController?.ShowToast(moveRes.Message, 1.8f);
                    }
                    else if (CombatSession.Outcome != CombatOutcome.InProgress)
                    {
                        HandleCombatOutcome();
                    }
                    else
                    {
                        TryAutoEndTurnIfExhausted(actor);
                    }
                    break;
                }

                case CombatActionMode.Melee:
                {
                    if (clickedUnit == null && TryFindReachableTileInfo(actor.Id, localCoord, out _))
                    {
                        // Bug #5: clicking an empty reachable tile in Melee mode exits Melee and walks there.
                        _activeCombatMode = CombatActionMode.Smart;
                        CombatActionResult moveRes = ExecuteCombatMoveStep(actor, localCoord);
                        if (!moveRes.Success)
                        {
                            hudController?.ShowToast(moveRes.Message, 1.8f);
                            RefreshCombatHudState();
                        }
                        else if (CombatSession.Outcome != CombatOutcome.InProgress)
                        {
                            HandleCombatOutcome();
                        }
                        else
                        {
                            TryAutoEndTurnIfExhausted(actor);
                        }
                        return;
                    }

                    if (clickedUnit == null || clickedUnit.Side != CombatSide.Monster)
                    {
                        hudController?.ShowToast("Click an enemy to strike, a reachable tile to walk, or Right-Click / [Esc] to cancel.", 1.8f);
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
                        string vfxKey = ResolveSpellVfxKey(spell, out bool isProjectile);
                        Vector3 startWorld = GridToWorldAtElevation(LocalToWorld(actor.Position));
                        Vector3 targetWorld = GridToWorldAtElevation(LocalToWorld(localCoord));
                        if (gridOverlays != null)
                        {
                            gridOverlays.PlayCombatVfx(vfxKey, startWorld, targetWorld, isProjectile);
                        }
                        if (_combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView casterView) && casterView != null)
                        {
                            casterView.PlayAttackLunge(targetWorld, 0.28f, 0.07f);
                        }

                        int damagedCount = 0;
                        for (int i = 0; i < CombatSession.Combatants.Count; i++)
                        {
                            CombatantState c = CombatSession.Combatants[i];
                            if (oldHps.TryGetValue(c.Id, out int before) && c.Hp < before)
                            {
                                damagedCount++;
                                if (_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView v) && v != null)
                                {
                                    v.PlayHitFlash(0.055f);
                                }
                            }
                        }

                        if (damagedCount > 0)
                        {
                            bool isHeavyFireball = string.Equals(vfxKey, "fireball_impact", StringComparison.Ordinal) && damagedCount >= 2;
                            if (isHeavyFireball)
                            {
                                cameraRig?.PlayFocusBeat(targetWorld, -1.4f, 0.35f);
                            }
                            cameraRig?.PlayImpactImpulse(isHeavyFireball ? 0.20f : 0.15f, 0.16f, targetWorld - startWorld);
                            TriggerHitStop(isHeavyFireball ? 0.055f : 0.040f);
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
                        if (gridOverlays != null)
                        {
                            CombatantState targetUnit = CombatSession.GetCombatant(targetId) ?? actor;
                            Vector3 startWorld = GridToWorldAtElevation(LocalToWorld(actor.Position));
                            Vector3 targetWorld = GridToWorldAtElevation(LocalToWorld(targetUnit.Position));
                            gridOverlays.PlayCombatVfx("heal_sparkle", startWorld, targetWorld, isProjectile: false);
                        }
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

                        Vector3 shoveDir = GridToWorldAtElevation(LocalToWorld(prevTargetPos)) -
                                           GridToWorldAtElevation(LocalToWorld(actor.Position));
                        cameraRig?.PlayImpactImpulse(0.14f, 0.16f, shoveDir);
                        TriggerHitStop(0.040f);
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
                float atkDur = GetCombatAttackDuration();
                attView.PlayAttackLunge(GridToWorldAtElevation(LocalToWorld(defender.Position)), 0.28f, atkDur);
                if (atkDur > 0.001f)
                {
                    _combatPendingEndTurnWaitUntil = Mathf.Max(_combatPendingEndTurnWaitUntil, Time.time + atkDur);
                }
            }

            Vector3 startWorld = GridToWorldAtElevation(LocalToWorld(attacker.Position));
            Vector3 targetWorld = GridToWorldAtElevation(LocalToWorld(defender.Position));
            if (gridOverlays != null)
            {
                gridOverlays.PlayCombatVfx(
                    isRanged ? "arrow_bolt" : "slash_arc",
                    startWorld,
                    targetWorld,
                    isProjectile: isRanged);
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

                    cameraRig?.PlayImpactImpulse(isCrit ? 0.20f : 0.13f, isCrit ? 0.18f : 0.14f, targetWorld - startWorld);
                    TriggerHitStop(isCrit ? 0.055f : 0.040f);
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
            SyncDestructibleArenaProps();
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
                if (_activeCombatMode == CombatActionMode.Melee && !actor.Economy.HasSlot(CombatActionSlot.MainAction))
                {
                    _activeCombatMode = CombatActionMode.Smart;
                }
                else if (_activeCombatMode == CombatActionMode.Ranged && !actor.Economy.HasSlot(CombatActionSlot.MainAction))
                {
                    _activeCombatMode = CombatActionMode.Smart;
                }
                else if (_activeCombatMode == CombatActionMode.Spell)
                {
                    int spCost = 0;
                    if (_selectedSpellId >= 0 && Bundle != null && Bundle.Spells.TryGetValue(_selectedSpellId, out SpellDefinition spDef))
                    {
                        spCost = spDef.SpCost;
                    }
                    if (!actor.Economy.HasSlot(CombatActionSlot.MainAction) || actor.Sp < spCost)
                    {
                        _activeCombatMode = CombatActionMode.Smart;
                    }
                }
                else if ((_activeCombatMode == CombatActionMode.Shove || _activeCombatMode == CombatActionMode.Potion) &&
                         !actor.Economy.HasSlot(CombatActionSlot.BonusAction))
                {
                    _activeCombatMode = CombatActionMode.Smart;
                }

                if (TryAutoEndTurnIfExhausted(actor))
                {
                    return;
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
            if (mode != CombatActionMode.Smart && _activeCombatMode == mode)
            {
                mode = CombatActionMode.Smart;
            }

            _activeCombatMode = mode;
            CombatantState actor = CombatSession.CurrentActor;
            if (actor != null && mode == CombatActionMode.Spell && _selectedSpellId < 0 && actor.SpellIds.Count > 0)
            {
                _selectedSpellId = actor.SpellIds[0];
            }

            RefreshCombatStaticOverlays(actor);
            _combatHoverDirty = true;
            if (mode == CombatActionMode.Smart)
            {
                hudController?.ClearCombatHoverPreview();
            }
            else
            {
                hudController?.SetCombatTooltip(mode.ToString() + " Mode", GetIdleModeInstructions(mode));
            }
            RefreshCombatHudState();
        }

        private void HandleSpellSelected(int spellId)
        {
            audioDirector?.PlayUiClick();
            if (_activeCombatMode == CombatActionMode.Spell && _selectedSpellId == spellId)
            {
                _activeCombatMode = CombatActionMode.Smart;
                _selectedSpellId = -1;
                RefreshCombatStaticOverlays(CombatSession?.CurrentActor);
                _combatHoverDirty = true;
                hudController?.ClearCombatHoverPreview();
                RefreshCombatHudState();
                return;
            }

            _selectedSpellId = spellId;
            _activeCombatMode = CombatActionMode.Spell;
            RefreshCombatStaticOverlays(CombatSession?.CurrentActor);
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

        private void HandleDash()
        {
            if (!_isInCombat || CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
            {
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor == null || actor.Side != CombatSide.Hero || _autoCombatEnabled)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            CombatActionResult res = CombatSession.ExecuteDash();
            if (res.Success)
            {
                if (_combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView view) && view != null)
                {
                    view.ShowArcingFloatingText($"DASH (+{actor.GetDndBaseMoveBudget()} MP)", BywaterUiHelpers.InfoCyan, isCrit: false);
                }

                _activeCombatMode = CombatActionMode.Smart;
                RefreshCombatStaticOverlays(actor);
                _combatHoverDirty = true;
                SyncAllCombatUnitViews();
                RefreshCombatHudState();
            }
            else
            {
                hudController?.ShowToast(res.Message, 1.8f);
            }
        }

        private void HandleGuard()
        {
            if (!_isInCombat || CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
            {
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor == null || actor.Side != CombatSide.Hero || _autoCombatEnabled)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            CombatActionResult res = CombatSession.GuardOverwatch();
            if (res.Success)
            {
                if (_combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView view) && view != null)
                {
                    view.ShowArcingFloatingText("OVERWATCH (+4 AC)", BywaterUiHelpers.InfoCyan, isCrit: false);
                }

                SyncAllCombatUnitViews();
                if (_activeCombatMode == CombatActionMode.Melee ||
                    _activeCombatMode == CombatActionMode.Ranged ||
                    _activeCombatMode == CombatActionMode.Spell)
                {
                    _activeCombatMode = CombatActionMode.Smart;
                }

                if (!TryAutoEndTurnIfExhausted(actor))
                {
                    RefreshCombatStaticOverlays(actor);
                    RefreshCombatHudState();
                }
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
            if (Application.isPlaying && gameObject.activeInHierarchy &&
                _speedIndex < 2 &&
                Time.time < _combatPendingEndTurnWaitUntil)
            {
                float waitSecs = Mathf.Clamp(_combatPendingEndTurnWaitUntil - Time.time, 0f, 1.2f);
                if (_delayedTurnRoutine != null)
                {
                    StopCoroutine(_delayedTurnRoutine);
                }
                _delayedTurnRoutine = StartCoroutine(DelayedBeginCurrentCombatTurn(waitSecs));
                return;
            }

            BeginCurrentCombatTurn();
        }

        private IEnumerator DelayedBeginCurrentCombatTurn(float delaySeconds)
        {
            if (delaySeconds > 0.001f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }
            _delayedTurnRoutine = null;
            BeginCurrentCombatTurn();
        }

        private void HandleCycleSpeed()
        {
            audioDirector?.PlayUiClick();
            _speedIndex = (_speedIndex + 1) % SpeedDelays.Length;
            RefreshCombatHudState();
            SyncHudSettings();
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
                if (_activeEncounterZone != null)
                {
                    ExploreSession?.CompleteEncounterZone(_activeEncounterZone);
                }
                else
                {
                    ExploreSession?.GrantPostCombatGrace();
                    if (!string.IsNullOrEmpty(_activeEncounterZoneKey))
                    {
                        ExploreSession?.MarkEncounterCleared(_activeEncounterZoneKey);
                    }
                }
                _activeEncounterZone = null;
                _activeEncounterZoneKey = string.Empty;
                _activeCombatSurpriseMode = CombatInitiativeSurpriseMode.None;
                _activeCombatRewardBonusPct = 0;

                ExploreSession?.SyncAfterCombat(CombatSession, floorDeadToOne: true);

                audioDirector?.PlayMusic("treasure", 0.7f);
                audioDirector?.PlayCoin();
                if (CombatSession.LastVictorySummary != null)
                {
                    hudController?.ShowVictoryOutcome(CombatSession.LastVictorySummary, ExploreSession?.Party, Bundle);
                }
                else
                {
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

                    hudController?.ShowOutcomeModal(
                        CombatOutcome.Victory,
                        CombatSession.TotalXpEarned,
                        CombatSession.VictoryLevelsGained,
                        CombatSession.TotalGoldLoot,
                        lootNames);
                }
            }
            else
            {
                _activeEncounterZone = null;
                _activeEncounterZoneKey = string.Empty;
                _activeCombatSurpriseMode = CombatInitiativeSurpriseMode.None;
                _activeCombatRewardBonusPct = 0;
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
            gridOverlays?.ClearCombatVfx();
            CombatSession = null;
            _activeEncounterZone = null;
            _activeEncounterZoneKey = string.Empty;
            _activeCombatSurpriseMode = CombatInitiativeSurpriseMode.None;
            _activeCombatRewardBonusPct = 0;
            _hoveredCombatUnitId = null;
            _hoveredRimUnitId = null;
            _destroyedArenaPropCoords.Clear();
            _isHoveringHotbarUi = false;
            _hasPreCombatExplorePos = false;
            _lastAppliedNormalizedTime = -1f;
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

        private void ShowAttackTooltip(string title, TacticalAttackPreview preview, bool isRanged, string prefixLine = null, string suffixLine = null)
        {
            string tags = preview.ConditionTags.Count > 0 ? string.Join(", ", preview.ConditionTags) : "None";
            string losLine = isRanged && preview.LosResult != null
                ? $"LOS: {preview.LosResult.Kind} (Cover -{preview.LosResult.CoverPenaltyPct}%)\n"
                : string.Empty;
            string advLine = preview.AdvantageState == RollAdvantageState.Advantage
                ? "ADVANTAGE (Roll 2x, Keep Best)\n"
                : (preview.AdvantageState == RollAdvantageState.Disadvantage ? "DISADVANTAGE (Roll 2x, Keep Worst)\n" : string.Empty);
            string flankLine = preview.IsFlanking ? "FLANKING (+20% Hit)\n" : string.Empty;
            string aooLine = preview.ProvokesAoO ? "AoO WARNING!\n" : string.Empty;
            string intentBlock = !string.IsNullOrWhiteSpace(suffixLine) ? $"\n{suffixLine}" : string.Empty;
            string actionHint = preview.CanExecute ? "Click target to attack." : $"Blocked: {preview.Reason}";

            string elevationBadge = null;
            if (preview.AdvantageState == RollAdvantageState.Advantage)
            {
                elevationBadge = "[ADVANTAGE]";
            }
            else if (preview.AdvantageState == RollAdvantageState.Disadvantage)
            {
                elevationBadge = "[DISADVANTAGE]";
            }
            else if (preview.ElevationModifierPct > 0)
            {
                elevationBadge = "[HIGH GROUND +15% / +1 RNG]";
            }
            else if (preview.ElevationModifierPct < 0)
            {
                elevationBadge = "[LOW GROUND -15%]";
            }

            string costLabel = preview.ApCost > 0 ? $"{preview.ApCost} AP" : "Main Action";
            hudController?.SetCombatHoverPreview(
                title,
                (prefixLine ?? string.Empty) +
                $"Cost: {costLabel}   |   Hit%: {preview.HitChancePct}% (Uncapped {preview.UncappedHitScore}%)\n" +
                $"Dmg: {preview.MinDamage} - {preview.MaxDamage}   |   Defender AC: {preview.DefenderAc}\n" +
                $"Crit%: {preview.CritChancePct}%   |   Graze%: {preview.GrazeChancePct}%\n" +
                advLine + losLine + flankLine + aooLine +
                $"Tags: {tags}{intentBlock}\n\n{actionHint}",
                elevationBadge);
        }

        private static string ResolveShoveBadge(TacticalShovePreview preview)
        {
            if (preview == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(preview.HazardBadgeText))
            {
                return preview.HazardBadgeText;
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
            UpdateFlankOverlayFrom(actor.Position, actor, target, isFlanking);
        }

        private void UpdateFlankOverlayFrom(GridCoord attackerPos, CombatantState actor, CombatantState target, bool isFlanking)
        {
            if (gridOverlays == null || !isFlanking || CombatSession == null || actor == null || target == null)
            {
                return;
            }

            List<GridCoord> flankingAllies = new List<GridCoord>();
            int attDx = attackerPos.X - target.Position.X;
            int attDy = attackerPos.Y - target.Position.Y;

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
            if (!Application.isPlaying || Application.isBatchMode || !gameObject.activeInHierarchy || _speedIndex == 2 || durationSeconds <= 0.001f)
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
            if (_delayedTurnRoutine != null)
            {
                StopCoroutine(_delayedTurnRoutine);
                _delayedTurnRoutine = null;
            }
            _combatPendingEndTurnWaitUntil = 0f;
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
                case CombatActionMode.Smart:
                    return "Hover highlighted tiles to step, or hover/click enemies to Melee, Ranged, or Approach & Melee.";
                case CombatActionMode.Melee:
                    return "Melee Mode - Click adjacent enemy to strike, or click reachable tile / Right-Click / [Esc] to Move.";
                case CombatActionMode.Ranged:
                    return "Ranged Mode - Click enemy in LOS to shoot, or Right-Click / [Esc] / click button again to cancel.";
                case CombatActionMode.Spell:
                    return "Spell Mode - Hover arena to preview AoE & Save%, click to cast, or Right-Click / [Esc] to cancel.";
                case CombatActionMode.Potion:
                    return "Potion Mode - Click any ally (or yourself) to heal [2 AP], or Right-Click / [Esc] to cancel.";
                case CombatActionMode.Shove:
                    return "Shove Mode - Hover adjacent unit to preview push direction, click to shove [2 AP], or Right-Click / [Esc] to cancel.";
                default:
                    return string.Empty;
            }
        }

        private void ApplyAudioSettings()
        {
            if (audioDirector == null)
            {
                return;
            }

            float master = _isMuted ? 0f : Mathf.Clamp01(_masterVolume);
            audioDirector.MusicVolume = Mathf.Clamp01(master * _musicVolume);
            audioDirector.SfxVolume = Mathf.Clamp01(master * _sfxVolume);
        }

        private void SyncHudSettings()
        {
            hudController?.SyncSettingsState(
                _autoCombatEnabled,
                _autoEndTurn,
                _speedIndex,
                _masterVolume,
                _musicVolume,
                _sfxVolume,
                _isMuted,
                _gridOpacity,
                _nameplatesAlwaysOn,
                _devToolsVisible);
        }

        private void SetAutoCombatEnabled(bool enabled)
        {
            _autoCombatEnabled = enabled;
            hudController?.SetAutoCombatVisual(enabled);
            SyncHudSettings();

            if (!_isInCombat || CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress)
            {
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            if (actor == null || actor.Side != CombatSide.Hero)
            {
                return;
            }

            if (enabled)
            {
                if (_aiCoroutine == null)
                {
                    gridOverlays?.ClearAll();
                    gridOverlays?.SetSurfaceCells(OffsetSurfaceCells(CombatSession.GetActiveSurfaceCells()));
                    if (_showAllEnemyIntentArcs)
                    {
                        RefreshEnemyIntentArcs();
                    }
                    hudController?.ClearCombatHoverPreview();
                    RefreshCombatHudState();
                    if (Application.isPlaying && gameObject.activeInHierarchy)
                    {
                        _aiCoroutine = StartCoroutine(RunAutoCombatHeroTurnRoutine());
                    }
                }
                hudController?.ShowToast("Auto-Combat ON ([A] or [Esc] to stop).", 1.8f);
            }
            else
            {
                StopAiCoroutine();
                RefreshCombatStaticOverlays(actor);
                _combatHoverDirty = true;
                RefreshCombatHudState();
                hudController?.ShowToast("Auto-Combat OFF.", 1.6f);
            }
        }

        private void HandleCombatGridToggle()
        {
            audioDirector?.PlayUiClick();
            if (_gridOpacity > 0.75f)
            {
                _gridOpacity = 0.5f;
            }
            else if (_gridOpacity > 0.25f)
            {
                _gridOpacity = 0.0f;
            }
            else
            {
                _gridOpacity = 1.0f;
            }

            gridOverlays?.SetGridOpacity(_gridOpacity);
            SyncHudSettings();
            hudController?.ShowToast($"Tactical Grid: {Mathf.RoundToInt(_gridOpacity * 100f)}%", 1.4f);
        }

        private void HandleFleeCombat()
        {
            if (!_isInCombat)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            GridCoord returnPos = _preCombatExplorePos;
            bool hasReturnPos = _hasPreCombatExplorePos;
            ExploreSession?.GrantPostCombatGrace();
            ExitCombatToExploration(syncPartyFromCombat: true);

            if (hasReturnPos &&
                ExploreSession != null &&
                ExploreSession.CurrentMap != null &&
                ExploreSession.CurrentMap.InBounds(returnPos) &&
                ExploreSession.CurrentMap.IsPassable(returnPos.X, returnPos.Y, ExploreSession.Party.HasBoat) &&
                ExploreSession.Position != returnPos)
            {
                ExploreSession.MoveAlongPath(returnPos, autoHaltOnEvents: false);
                ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
                EnterExplorationView(immediateCamera: true);
            }

            hudController?.ShowToast("Retreated from tactical combat.", 2.2f);
        }

        private void HandleCombatRightClickCancel(Mouse mouse, Vector2 mousePos)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                _rightMouseDownPendingClick = true;
                _rightMouseDownPos = mousePos;
            }

            if (_rightMouseDownPendingClick && mouse.rightButton.isPressed)
            {
                if ((mousePos - _rightMouseDownPos).sqrMagnitude >= RightClickMaxDragPixelsSq)
                {
                    _rightMouseDownPendingClick = false;
                }
            }

            if (mouse.rightButton.wasReleasedThisFrame)
            {
                bool wasShortClick = _rightMouseDownPendingClick &&
                                     (mousePos - _rightMouseDownPos).sqrMagnitude < RightClickMaxDragPixelsSq;
                _rightMouseDownPendingClick = false;

                if (wasShortClick &&
                    _isInCombat &&
                    _activeCombatMode != CombatActionMode.Smart &&
                    CombatSession != null &&
                    CombatSession.Outcome == CombatOutcome.InProgress)
                {
                    HandleCombatModeSelected(CombatActionMode.Smart);
                    hudController?.ShowToast("Targeting cancelled (Move mode).", 1.2f);
                }
            }
        }

        private void ClearCombatTransientHoverVisuals()
        {
            if (gridOverlays != null)
            {
                gridOverlays.SetCombatHoverPath(null, false);
                gridOverlays.SetLosPreview(null, default, default);
                gridOverlays.SetSpellAoeFootprint(null, null, null, false);
                gridOverlays.SetFlankIndicator(null, null);
                gridOverlays.ClearShovePreview();
                gridOverlays.SetHoveredAttackTarget(null);
                if (!_showAllEnemyIntentArcs)
                {
                    gridOverlays.ClearEnemyIntentArcs();
                }
            }

            if (!string.IsNullOrEmpty(_hoveredCombatUnitId) || !string.IsNullOrEmpty(_hoveredRimUnitId))
            {
                _hoveredCombatUnitId = null;
                SetHoveredCombatUnitRim(null);
                RefreshUnitNameplates();
            }

            _lastCombatHover = new GridCoord(-1, -1);
            _combatHoverDirty = true;
            hudController?.ClearCombatHoverPreview();
        }

        private void RefreshContextualExplorePoi()
        {
            if (ExploreSession == null || ExploreSession.CurrentMap == null)
            {
                _lastContextPoiLabel = string.Empty;
                hudController?.SetContextualExplorePoi(null, null);
                return;
            }

            FindContextualExplorePoi(out string label, out Action action);
            _lastContextPoiLabel = label ?? string.Empty;
            hudController?.SetContextualExplorePoi(string.IsNullOrEmpty(label) ? null : label, action);
        }

        private void HandleContextPoiRequested()
        {
            FindContextualExplorePoi(out _, out Action action);
            action?.Invoke();
        }

        private void FindContextualExplorePoi(out string label, out Action action)
        {
            label = string.Empty;
            action = null;
            if (ExploreSession == null || ExploreSession.CurrentMap == null)
            {
                return;
            }

            TileMapData map = ExploreSession.CurrentMap;
            GridCoord pos = ExploreSession.Position;

            for (int maxDist = 0; maxDist <= 1; maxDist++)
            {
                for (int i = 0; i < map.Portals.Count; i++)
                {
                    MapPortal portal = map.Portals[i];
                    if (portal.Coord.ChebyshevDistance(pos) == maxDist)
                    {
                        MapPortal capturedPortal = portal;
                        label = map.IsDungeon ? "Exit Cavern" : "Enter Cavern";
                        action = () => TriggerContextualPortal(capturedPortal);
                        return;
                    }
                }

                for (int i = 0; i < map.PointsOfInterest.Count; i++)
                {
                    MapPointOfInterest poi = map.PointsOfInterest[i];
                    if (poi.Coord.ChebyshevDistance(pos) != maxDist)
                    {
                        continue;
                    }

                    string kind = (poi.Kind ?? string.Empty).Trim().ToLowerInvariant();
                    MapPointOfInterest capturedPoi = poi;
                    if (kind == "shop")
                    {
                        label = "Enter Shop";
                        action = () =>
                        {
                            audioDirector?.PlayUiClick();
                            HandlePoiArrival(capturedPoi);
                        };
                        return;
                    }
                    if (kind == "temple")
                    {
                        label = "Visit Shrine";
                        action = () =>
                        {
                            audioDirector?.PlayUiClick();
                            HandlePoiArrival(capturedPoi);
                        };
                        return;
                    }
                    if (kind == "tavern")
                    {
                        label = "Rest at Tavern";
                        action = () =>
                        {
                            audioDirector?.PlayUiClick();
                            HandlePoiArrival(capturedPoi);
                        };
                        return;
                    }
                }
            }

            if (worldRenderer != null && !map.IsDungeon)
            {
                IReadOnlyList<CartographyNpcPlacement> npcs = worldRenderer.ActiveNpcs;
                for (int i = 0; i < npcs.Count; i++)
                {
                    CartographyNpcPlacement npc = npcs[i];
                    if (Math.Max(Math.Abs(npc.TileX - pos.X), Math.Abs(npc.TileY - pos.Y)) <= 1)
                    {
                        CartographyNpcPlacement capturedNpc = npc;
                        label = string.Equals(npc.VenueKind, "temple", StringComparison.OrdinalIgnoreCase)
                            ? $"Visit {npc.DisplayName}"
                            : $"Trade: {npc.DisplayName}";
                        action = () => InteractWithNpc(capturedNpc);
                        return;
                    }
                }
            }
        }

        private void TryInteractNearbyNpcOrPoi()
        {
            if (_isInCombat || ExploreSession == null || ExploreSession.CurrentMap == null)
            {
                return;
            }

            if (TryFindNearbyNpc(out CartographyNpcPlacement npc))
            {
                InteractWithNpc(npc);
                return;
            }

            FindContextualExplorePoi(out _, out Action action);
            if (action != null)
            {
                action.Invoke();
                return;
            }

            hudController?.ShowToast("Move closer to a merchant or doorway to interact [E].", 1.8f);
        }

        private void InteractWithNpc(CartographyNpcPlacement npc)
        {
            if (hudController == null || ExploreSession == null)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            if (_isCampActive)
            {
                ExitCampMode();
            }

            if (string.Equals(npc.VenueKind, "temple", StringComparison.OrdinalIgnoreCase))
            {
                hudController.OpenTempleModal(ExploreSession.Party, Bundle);
                audioDirector?.PlayMusic("temple", 0.6f);
            }
            else if (!string.IsNullOrEmpty(npc.ShopId))
            {
                hudController.OpenShop(npc.ShopId, ExploreSession.Party, Bundle);
                audioDirector?.PlayMusic("shop", 0.7f);
            }

            RefreshNpcPrompts();
        }

        private void TryCompletePendingNpcInteraction()
        {
            if (!_pendingNpcInteraction.HasValue || ExploreSession == null)
            {
                return;
            }

            CartographyNpcPlacement target = _pendingNpcInteraction.Value;
            _pendingNpcInteraction = null;
            GridCoord npcCoord = new GridCoord(target.TileX, target.TileY);
            if (ExploreSession.Position.ChebyshevDistance(npcCoord) <= 1)
            {
                InteractWithNpc(target);
            }
        }

        private bool TryFindNearbyNpc(out CartographyNpcPlacement npc)
        {
            if (worldRenderer != null &&
                ExploreSession != null &&
                ExploreSession.CurrentMap != null &&
                !ExploreSession.CurrentMap.IsDungeon)
            {
                IReadOnlyList<CartographyNpcPlacement> npcs = worldRenderer.ActiveNpcs;
                GridCoord pos = ExploreSession.Position;
                for (int i = 0; i < npcs.Count; i++)
                {
                    CartographyNpcPlacement candidate = npcs[i];
                    if (Math.Max(Math.Abs(candidate.TileX - pos.X), Math.Abs(candidate.TileY - pos.Y)) <= 1)
                    {
                        npc = candidate;
                        return true;
                    }
                }
            }

            npc = default;
            return false;
        }

        private bool TryFindNearbyVenue(out string venueKind, out string shopId, out string label)
        {
            venueKind = string.Empty;
            shopId = string.Empty;
            label = string.Empty;
            if (ExploreSession == null || ExploreSession.CurrentMap == null)
            {
                return false;
            }

            if (TryFindNearbyNpc(out CartographyNpcPlacement nearbyNpc))
            {
                venueKind = nearbyNpc.VenueKind;
                shopId = nearbyNpc.ShopId;
                label = nearbyNpc.DisplayName;
                return true;
            }

            TileMapData map = ExploreSession.CurrentMap;
            GridCoord pos = ExploreSession.Position;
            for (int maxDist = 0; maxDist <= 1; maxDist++)
            {
                for (int i = 0; i < map.PointsOfInterest.Count; i++)
                {
                    MapPointOfInterest poi = map.PointsOfInterest[i];
                    if (poi.Coord.ChebyshevDistance(pos) != maxDist)
                    {
                        continue;
                    }

                    string kind = (poi.Kind ?? string.Empty).Trim().ToLowerInvariant();
                    if (kind == "shop" && !string.IsNullOrEmpty(poi.ShopId))
                    {
                        venueKind = "shop";
                        shopId = poi.ShopId;
                        label = poi.Label;
                        return true;
                    }
                    if (kind == "temple")
                    {
                        venueKind = "temple";
                        shopId = string.Empty;
                        label = poi.Label;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryFindAdjacentPassableTile(
            TileMapData map,
            GridCoord from,
            GridCoord center,
            bool hasBoat,
            out GridCoord bestCoord)
        {
            bestCoord = default;
            if (map == null)
            {
                return false;
            }

            int bestScore = int.MaxValue;
            bool found = false;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    GridCoord candidate = new GridCoord(center.X + dx, center.Y + dy);
                    if (!map.InBounds(candidate) || !map.IsPassable(candidate.X, candidate.Y, hasBoat))
                    {
                        continue;
                    }

                    if (worldRenderer != null && worldRenderer.TryGetNpcAtCoord(candidate, out _))
                    {
                        continue;
                    }

                    if (_explorePathfinder != null)
                    {
                        if (!_explorePathfinder.FindPathSteps(map, from, candidate, _explorePreviewSteps, out _, allowDiagonal: true, hasBoat: hasBoat))
                        {
                            continue;
                        }
                    }

                    int score = _explorePathfinder != null ? _explorePreviewSteps.Count : candidate.ManhattanDistance(from);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestCoord = candidate;
                        found = true;
                    }
                }
            }

            return found;
        }

        private void RefreshNpcPrompts()
        {
            if (worldRenderer == null || ExploreSession == null)
            {
                return;
            }

            bool hidePrompts = _isInCombat || _isCampActive || (hudController != null && hudController.IsModalOpen);
            Vector3 heroPos = GridToWorldAtElevation(ExploreSession.Position);
            worldRenderer.UpdateNpcProximityPrompts(ExploreSession.Position, heroPos, hidePrompts);
        }

        private void TriggerContextualPortal(MapPortal portal)
        {
            if (_isInCombat || ExploreSession == null || portal == null)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            if (_isCampActive)
            {
                ExitCampMode();
            }
            StopExploreWalkCoroutine();

            if (ExploreSession.Position != portal.Coord)
            {
                ExploreSession.MoveAlongPath(portal.Coord, autoHaltOnEvents: false);
            }

            if (ExploreSession.UsePortalAtCurrentTile())
            {
                audioDirector?.PlayDoorInteract();
                ResetPartyTrailHistory(ExploreSession.Position, ExploreSession.CurrentMap, ExploreSession.Facing);
                EnterExplorationView(immediateCamera: true);
                string destName = ExploreSession.CurrentMap.IsDungeon ? "Bywater Cavern" : "City of Bywater";
                hudController?.ShowToast($"Entered {destName} ({ExploreSession.Position.X}, {ExploreSession.Position.Y})", 2.8f);
            }
        }

        private static bool IsArenaOverhangObstacle(BywaterTileKind kind)
        {
            return kind == BywaterTileKind.TownBuildingSolid ||
                   kind == BywaterTileKind.TownBuildingDoor ||
                   kind == BywaterTileKind.WallBlock ||
                   kind == BywaterTileKind.DungeonWall;
        }

        public static bool IsSpawnTileNorthClear(TileMapData map, int worldX, int worldY)
        {
            if (map == null || !map.InBounds(worldX, worldY - 1))
            {
                return false;
            }

            BywaterTileKind northKind = BywaterTileClassifier.Classify(map, worldX, worldY - 1);
            return !IsArenaOverhangObstacle(northKind);
        }

        public GridCoord FindCleanArenaOrigin(TileMapData map, GridCoord partyPos, int width, int height)
        {
            int maxOx = Math.Max(0, map.Width - width);
            int maxOy = Math.Max(0, map.Height - height);
            int baseOx = Mathf.Clamp(partyPos.X - width / 2, 0, maxOx);
            int baseOy = Mathf.Clamp(partyPos.Y - height / 2, 0, maxOy);
            bool hasBoat = ExploreSession != null && ExploreSession.Party.HasBoat;

            int bestScore = int.MaxValue;
            GridCoord bestOrigin = new GridCoord(baseOx, baseOy);

            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int ox = Mathf.Clamp(baseOx + dx, 0, maxOx);
                    int oy = Mathf.Clamp(baseOy + dy, 0, maxOy);

                    bool partyInsideSafeMargin =
                        partyPos.X >= ox + 1 &&
                        partyPos.X <= ox + width - 2 &&
                        partyPos.Y >= oy + 1 &&
                        partyPos.Y <= oy + height - 2;

                    if (!partyInsideSafeMargin)
                    {
                        bool partyOnWalkableEdge =
                            map.InBounds(partyPos) &&
                            map.IsPassable(partyPos.X, partyPos.Y, hasBoat) &&
                            partyPos.X >= ox && partyPos.X < ox + width &&
                            partyPos.Y >= oy && partyPos.Y < oy + height;
                        if (!partyOnWalkableEdge)
                        {
                            continue;
                        }
                    }

                    int score = partyInsideSafeMargin ? 0 : 250;

                    int northOutsideY = oy - 1;
                    for (int lx = 0; lx < width; lx++)
                    {
                        int wx = ox + lx;
                        if (!map.InBounds(wx, northOutsideY))
                        {
                            score += 220;
                        }
                        else
                        {
                            BywaterTileKind northKind = BywaterTileClassifier.Classify(map, wx, northOutsideY);
                            if (IsArenaOverhangObstacle(northKind))
                            {
                                score += 450;
                            }
                            else if (northKind == BywaterTileKind.TownBuildingDoor || northKind == BywaterTileKind.DungeonDoor)
                            {
                                score += 160;
                            }
                        }
                    }

                    for (int ly = 0; ly < height; ly++)
                    {
                        int wy = oy + ly;
                        bool isNorthRow = ly <= 1;
                        bool isSpawnBand = ly >= 2 && ly <= height - 3;

                        for (int lx = 0; lx < width; lx++)
                        {
                            int wx = ox + lx;
                            if (!map.InBounds(wx, wy))
                            {
                                score += isNorthRow ? 300 : 120;
                                continue;
                            }

                            BywaterTileKind kind = BywaterTileClassifier.Classify(map, wx, wy);
                            bool isSolidWall = IsArenaOverhangObstacle(kind);
                            bool isShopNpcTile = worldRenderer != null &&
                                                 worldRenderer.CartographyData != null &&
                                                 worldRenderer.CartographyData.IsNpcBlockedTile(wx, wy);

                            if (isNorthRow)
                            {
                                if (isSolidWall)
                                {
                                    score += 500;
                                }
                                else if (isShopNpcTile)
                                {
                                    score += 220;
                                }
                                else if (kind == BywaterTileKind.TownBuildingDoor ||
                                         kind == BywaterTileKind.DungeonDoor ||
                                         !map.IsPassable(wx, wy, hasBoat))
                                {
                                    score += 180;
                                }
                            }
                            else if (isSpawnBand && lx >= 2 && lx <= width - 3)
                            {
                                if (isSolidWall)
                                {
                                    score += 100;
                                }
                                else if (isShopNpcTile)
                                {
                                    score += 350;
                                }
                                else if (kind == BywaterTileKind.TownBuildingDoor ||
                                         kind == BywaterTileKind.DungeonDoor ||
                                         !map.IsPassable(wx, wy, hasBoat))
                                {
                                    score += 45;
                                }
                            }
                            else if (isShopNpcTile)
                            {
                                score += 150;
                            }
                        }
                    }

                    score += Math.Abs(ox - baseOx) + Math.Abs(oy - baseOy);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestOrigin = new GridCoord(ox, oy);
                    }
                }
            }

            return bestOrigin;
        }

        private bool FindBestSmartApproachTile(
            CombatantState actor,
            CombatantState target,
            out ReachableTileInfo bestTile,
            out TacticalAttackPreview projectedMelee)
        {
            bestTile = null;
            projectedMelee = null;
            if (CombatSession == null || actor == null || target == null)
            {
                return false;
            }

            IReadOnlyList<ReachableTileInfo> reach = CombatSession.GetReachableTiles(actor.Id);
            int bestScore = int.MinValue;

            for (int i = 0; i < reach.Count; i++)
            {
                ReachableTileInfo candidate = reach[i];
                if (candidate.Coord == actor.Position || candidate.Coord.ChebyshevDistance(target.Position) != 1)
                {
                    continue;
                }

                TacticalAttackPreview preview = CombatSession.PreviewMeleeAttack(actor.Id, target.Id, candidate.Coord);
                if (!preview.CanExecute)
                {
                    continue;
                }

                int score = 0;
                if (!candidate.ProvokesAoO)
                {
                    score += 100;
                }

                SurfaceType surf = CombatSession.Grid.GetSurface(candidate.Coord);
                bool hazardous = surf == SurfaceType.Fire ||
                                 surf == SurfaceType.ElectrifiedWater ||
                                 surf == SurfaceType.Poison;
                if (hazardous && target.Hp > preview.MaxDamage)
                {
                    score -= 200;
                }

                if (preview.IsFlanking)
                {
                    score += 50;
                }

                if (preview.ElevationModifierPct > 0)
                {
                    score += 25;
                }

                score -= candidate.PathCost * 2;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = candidate;
                    projectedMelee = preview;
                }
            }

            return bestTile != null;
        }

        private bool TryFindReachableTileInfo(string combatantId, GridCoord targetLocalCoord, out ReachableTileInfo info)
        {
            info = null;
            if (CombatSession == null || string.IsNullOrEmpty(combatantId))
            {
                return false;
            }

            IReadOnlyList<ReachableTileInfo> reach = CombatSession.GetReachableTiles(combatantId);
            for (int i = 0; i < reach.Count; i++)
            {
                if (reach[i].Coord == targetLocalCoord)
                {
                    info = reach[i];
                    return true;
                }
            }
            return false;
        }

        private void BuildCombatMoveWorldPath(GridCoord fromLocal, GridCoord toLocal, List<Vector3> worldOut)
        {
            worldOut.Clear();
            if (CombatSession?.Grid != null &&
                _combatAiPathfinder.FindPathSteps(
                    CombatSession.Grid.Width,
                    CombatSession.Grid.Height,
                    fromLocal,
                    toLocal,
                    (x, y) => CombatSession.Grid.IsWalkable(x, y) && ((x == toLocal.X && y == toLocal.Y) || GetLivingUnitAt(new GridCoord(x, y)) == null),
                    (x, y) => CombatSession.Grid.GetMoveCost(x, y),
                    _aiPathStepsScratch,
                    out _,
                    allowDiagonal: true) &&
                _aiPathStepsScratch.Count > 0)
            {
                for (int i = 0; i < _aiPathStepsScratch.Count; i++)
                {
                    worldOut.Add(GridToWorldAtElevation(LocalToWorld(_aiPathStepsScratch[i])));
                }
                return;
            }

            worldOut.Add(GridToWorldAtElevation(LocalToWorld(toLocal)));
        }

        private CombatActionResult ExecuteCombatMoveStep(CombatantState actor, GridCoord targetLocalCoord)
        {
            GridCoord oldPos = actor.Position;
            _combatPathWorldScratch.Clear();
            if (TryFindReachableTileInfo(actor.Id, targetLocalCoord, out ReachableTileInfo reachMatch) &&
                reachMatch.Path != null &&
                reachMatch.Path.Count > 0)
            {
                for (int i = 0; i < reachMatch.Path.Count; i++)
                {
                    GridCoord step = reachMatch.Path[i];
                    if (step == oldPos && i == 0)
                    {
                        continue;
                    }
                    _combatPathWorldScratch.Add(GridToWorldAtElevation(LocalToWorld(step)));
                }
            }

            CombatActionResult moveRes = CombatSession.TryMoveTo(targetLocalCoord);
            if (!moveRes.Success)
            {
                return moveRes;
            }

            Vector3 destWorld = GridToWorldAtElevation(LocalToWorld(actor.Position));
            if (actor.Position != oldPos && _combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView view) && view != null)
            {
                if (_combatPathWorldScratch.Count == 0 ||
                    _combatPathWorldScratch[_combatPathWorldScratch.Count - 1] != destWorld)
                {
                    _combatPathWorldScratch.Clear();
                    _combatPathWorldScratch.Add(destWorld);
                }

                float moveDur = GetCombatMoveTileDuration();
                view.MoveAlongWorldPath(_combatPathWorldScratch, moveDur);
                if (moveDur > 0.001f)
                {
                    _combatPendingEndTurnWaitUntil = Mathf.Max(
                        _combatPendingEndTurnWaitUntil,
                        Time.time + _combatPathWorldScratch.Count * moveDur);
                }
            }

            TileMapData map = ExploreSession.CurrentMap;
            GridCoord worldStep = LocalToWorld(actor.Position);
            TileRule rule = map.GetTileRule(map.GetTile(worldStep.X, worldStep.Y));
            audioDirector?.PlayFootstep(rule.SoundId, map.IsDungeon);
            worldRenderer?.UpdateHeroWorldPosition(destWorld);

            if (actor.Side == CombatSide.Hero && _combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView stepView) && stepView != null)
            {
                string stepCostLabel = moveRes.ApSpent > 0 ? $"-{moveRes.ApSpent} AP" : "FREE MOVE";
                Color stepCostCol = moveRes.ApSpent > 0 ? new Color(0.98f, 0.80f, 0.14f, 1f) : BywaterUiHelpers.InfoCyan;
                stepView.ShowArcingFloatingText(stepCostLabel, stepCostCol, isCrit: false);
            }

            if (moveRes.AoOTriggered && _combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView hitView) && hitView != null)
            {
                bool wasOverwatch = CombatSession.Log.Count > 0 && string.Equals(CombatSession.Log[CombatSession.Log.Count - 1].ActionType, "Overwatch", StringComparison.Ordinal);
                hitView.PlayHitFlash(0.055f);
                hitView.ShowArcingFloatingText(wasOverwatch ? $"OVERWATCH -{moveRes.DamageDealt}!" : $"AoO -{moveRes.DamageDealt}!", BywaterUiHelpers.DangerRed, isCrit: false);
                cameraRig?.PlayImpactImpulse(0.12f, 0.14f, Vector3.down);
            }

            if (actor.IsProne && _combatUnitViews.TryGetValue(actor.Id, out BywaterUnitView proneView) && proneView != null)
            {
                proneView.ShowArcingFloatingText("PRONE", BywaterUiHelpers.BrassHighlight, isCrit: false);
            }

            RefreshCombatStaticOverlays(actor);
            _combatHoverDirty = true;
            SyncAllCombatUnitViews();
            RefreshUnitNameplates();
            RefreshCombatHudState();
            return moveRes;
        }

        private bool TryAutoEndTurnIfExhausted(CombatantState actor)
        {
            if (_autoEndTurn &&
                !_autoCombatEnabled &&
                CombatSession != null &&
                CombatSession.Outcome == CombatOutcome.InProgress &&
                CombatSession.CurrentActor == actor &&
                actor != null &&
                !actor.Economy.HasSlot(CombatActionSlot.MainAction) &&
                !actor.Economy.HasSlot(CombatActionSlot.BonusAction) &&
                actor.Economy.MoveRemaining == 0)
            {
                HandleEndTurn();
                return true;
            }

            return false;
        }

        private void HandleCombatPartyAllyClicked(string allyId)
        {
            if (!_isInCombat || CombatSession == null || CombatSession.Outcome != CombatOutcome.InProgress || string.IsNullOrEmpty(allyId))
            {
                return;
            }

            CombatantState actor = CombatSession.CurrentActor;
            CombatantState ally = CombatSession.GetCombatant(allyId);
            if (actor == null || actor.Side != CombatSide.Hero || ally == null || ally.Side != CombatSide.Hero || _autoCombatEnabled)
            {
                return;
            }

            if (_activeCombatMode == CombatActionMode.Potion)
            {
                Dictionary<string, int> oldHps = CaptureCombatantHps();
                if (!TryFindCombatHealPotion(out ItemDefinition potionDef))
                {
                    hudController?.ShowToast("No healing potions remaining.", 1.8f);
                    return;
                }

                CombatActionResult res = CombatSession.UseSwiftPotion(potionDef.Id, ally.Id);
                if (res.Success)
                {
                    audioDirector?.PlaySpell("heal");
                    if (gridOverlays != null)
                    {
                        Vector3 startWorld = GridToWorldAtElevation(LocalToWorld(actor.Position));
                        Vector3 targetWorld = GridToWorldAtElevation(LocalToWorld(ally.Position));
                        gridOverlays.PlayCombatVfx("heal_sparkle", startWorld, targetWorld, isProjectile: false);
                    }
                }
                FinishPlayerCombatAction(res, oldHps);
                return;
            }

            if (_activeCombatMode == CombatActionMode.Spell &&
                _selectedSpellId >= 0 &&
                Bundle != null &&
                Bundle.Spells.TryGetValue(_selectedSpellId, out SpellDefinition spell) &&
                (string.Equals(spell.EffectKind, "Heal", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(spell.EffectKind, "Buff", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(spell.EffectKind, "Cure", StringComparison.OrdinalIgnoreCase)))
            {
                ExecuteCombatClick(actor, ally.Position);
                return;
            }

            if (_activeCombatMode == CombatActionMode.Shove && ally.Id != actor.Id)
            {
                ExecuteCombatClick(actor, ally.Position);
                return;
            }

            Vector3 allyWorld = GridToWorldAtElevation(LocalToWorld(ally.Position));
            cameraRig?.EnsureWorldPointVisible(allyWorld);
            hudController?.SetCombatHoverPreview(
                $"{ally.Name} (Ally)",
                $"HP {ally.Hp}/{ally.MaxHp} | SP {ally.Sp}/{ally.MaxSp} | AC {ally.EffectiveAc}");
        }

        private void ApplyHeroAutoActionJuice(CombatantState hero, CombatActionResult res, Dictionary<string, int> oldHps)
        {
            if (hero == null || res == null || CombatSession == null)
            {
                return;
            }

            CombatantState hitEnemy = null;
            CombatantState healedHero = null;
            bool anyHealed = false;
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (!oldHps.TryGetValue(c.Id, out int before))
                {
                    continue;
                }

                if (c.Side == CombatSide.Monster && c.Hp < before)
                {
                    if (hitEnemy == null)
                    {
                        hitEnemy = c;
                    }
                    if (_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView eView) && eView != null)
                    {
                        eView.PlayHitFlash(0.055f);
                    }
                }
                else if (c.Side == CombatSide.Hero && c.Hp > before)
                {
                    anyHealed = true;
                    if (healedHero == null)
                    {
                        healedHero = c;
                    }
                }
            }

            CombatantState targetUnit = hitEnemy ?? healedHero ?? ResolveLastLogTargetCombatant(hero.Id);
            Vector3 startWorld = GridToWorldAtElevation(LocalToWorld(hero.Position));
            Vector3 targetWorld = targetUnit != null
                ? GridToWorldAtElevation(LocalToWorld(targetUnit.Position))
                : startWorld;

            bool isSpell = res.Message.IndexOf("casts ", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isSpell && gridOverlays != null)
            {
                string spellVfx = ResolveSpellVfxKeyFromMessage(res.Message, anyHealed || res.HealingDone > 0, out bool isProjectile);
                gridOverlays.PlayCombatVfx(spellVfx, startWorld, targetWorld, isProjectile);
            }
            else if ((anyHealed || res.HealingDone > 0) && gridOverlays != null)
            {
                gridOverlays.PlayCombatVfx("heal_sparkle", startWorld, targetWorld, isProjectile: false);
            }

            if (hitEnemy != null)
            {
                if (_combatUnitViews.TryGetValue(hero.Id, out BywaterUnitView hView) && hView != null)
                {
                    hView.PlayAttackLunge(GridToWorldAtElevation(LocalToWorld(hitEnemy.Position)), 0.28f, GetCombatAttackDuration());
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

                bool isRanged = res.Message.IndexOf("fires at", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (hero.PrefersRanged && hero.Position.ChebyshevDistance(hitEnemy.Position) > 1);
                if (!isSpell && gridOverlays != null)
                {
                    gridOverlays.PlayCombatVfx(
                        isRanged ? "arrow_bolt" : "slash_arc",
                        startWorld,
                        targetWorld,
                        isProjectile: isRanged);
                }

                audioDirector?.PlayAttack(isRanged, isCrit);
                cameraRig?.PlayImpactImpulse(isCrit ? 0.18f : 0.12f, 0.14f, targetWorld - startWorld);
                TriggerHitStop(isCrit ? 0.055f : 0.040f);
            }
            else if (!isSpell && res.AttackOutcomes.Count > 0 && targetUnit != null && gridOverlays != null)
            {
                bool isRanged = res.Message.IndexOf("fires at", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (hero.PrefersRanged && hero.Position.ChebyshevDistance(targetUnit.Position) > 1);
                gridOverlays.PlayCombatVfx(
                    isRanged ? "arrow_bolt" : "slash_arc",
                    startWorld,
                    targetWorld,
                    isProjectile: isRanged);
            }
            else if (anyHealed)
            {
                audioDirector?.PlaySpell("heal");
            }
        }

        private CombatantState ResolveLastLogTargetCombatant(string actorId)
        {
            if (CombatSession == null || CombatSession.Log == null || CombatSession.Log.Count == 0)
            {
                return null;
            }

            for (int i = CombatSession.Log.Count - 1; i >= 0; i--)
            {
                CombatLogEntry entry = CombatSession.Log[i];
                if (entry == null || !string.Equals(entry.ActorId, actorId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.TargetIds != null && entry.TargetIds.Count > 0)
                {
                    return CombatSession.GetCombatant(entry.TargetIds[0]);
                }

                break;
            }

            return null;
        }

        private static string ResolveSpellVfxKey(SpellDefinition spell, out bool isProjectile)
        {
            isProjectile = false;
            if (spell == null)
            {
                return "fireball_impact";
            }

            string combined = string.Concat(
                spell.Key ?? string.Empty, " ",
                spell.Name ?? string.Empty, " ",
                spell.DamageType ?? string.Empty, " ",
                spell.EffectKind ?? string.Empty);

            if (combined.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("cure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(spell.EffectKind, "Buff", StringComparison.OrdinalIgnoreCase))
            {
                return "heal_sparkle";
            }

            if (combined.IndexOf("frost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("cold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "frost_shatter";
            }

            if (combined.IndexOf("lightning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("shock", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "chain_lightning";
            }

            if (combined.IndexOf("dart", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("bolt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("missile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isProjectile = true;
                return "arrow_bolt";
            }

            return "fireball_impact";
        }

        private static string ResolveSpellVfxKeyFromMessage(string message, bool healed, out bool isProjectile)
        {
            isProjectile = false;
            if (healed ||
                (!string.IsNullOrEmpty(message) &&
                 (message.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  message.IndexOf("cure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  message.IndexOf("bless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  message.IndexOf("armor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  message.IndexOf("blade", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return "heal_sparkle";
            }

            if (!string.IsNullOrEmpty(message) &&
                (message.IndexOf("frost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("cold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "frost_shatter";
            }

            if (!string.IsNullOrEmpty(message) &&
                (message.IndexOf("lightning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("shock", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "chain_lightning";
            }

            if (!string.IsNullOrEmpty(message) &&
                (message.IndexOf("dart", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("bolt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("missile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                isProjectile = true;
                return "arrow_bolt";
            }

            return "fireball_impact";
        }

        private bool CanOfferQuickResolveForEncounter(List<MonsterDefinition> monsters, out QuickResolveSummary summary)
        {
            summary = null;
            if (ExploreSession == null || Bundle == null || monsters == null || monsters.Count == 0)
            {
                return false;
            }

            List<CombatantState> previewCombatants = new List<CombatantState>(ExploreSession.Party.Members.Count + monsters.Count);
            for (int i = 0; i < ExploreSession.Party.Members.Count; i++)
            {
                PartyMemberState m = ExploreSession.Party.Members[i];
                if (m != null && m.Hp > 0)
                {
                    previewCombatants.Add(CombatantState.FromHero(m, new GridCoord(2, i % 6), Bundle.Items));
                }
            }

            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterDefinition def = monsters[i];
                if (def != null)
                {
                    previewCombatants.Add(CombatantState.FromMonster(def, $"qr_{i}_{def.Id}", new GridCoord(8, i % 6), _roller));
                }
            }

            if (!TacticalCombatSession.ShouldOfferQuickResolve(previewCombatants))
            {
                return false;
            }

            summary = TacticalCombatSession.EvaluateQuickResolve(previewCombatants);
            return summary.CanQuickResolve;
        }

        private static string BuildQuickResolveSummaryText(List<MonsterDefinition> monsters, QuickResolveSummary summary)
        {
            int count = monsters != null ? monsters.Count : 0;
            string ratioText = summary != null ? $"{summary.PowerRatio:F1}x" : "Dominant";
            return $"Your vanguard holds a decisive advantage ({ratioText} power over {count} hostile(s)).\n\nCommand the battle tactically or Quick Resolve for instant victory & loot.";
        }

        private void ClearPendingQuickResolve()
        {
            _pendingQuickResolveMonsters = null;
            _pendingQuickResolveTitle = string.Empty;
            _pendingQuickResolveZoneKey = string.Empty;
        }

        private void ClearPendingScoutingReport()
        {
            _pendingScoutingReport = null;
            _pendingQuickResolveZone = null;
        }

        private void HandleEncounterInterceptEngage()
        {
            audioDirector?.PlayUiClick();
            EncounterScoutingReport report = _pendingScoutingReport;
            List<MonsterDefinition> monsters = _pendingQuickResolveMonsters;
            string title = !string.IsNullOrWhiteSpace(_pendingQuickResolveTitle)
                ? _pendingQuickResolveTitle
                : (report?.Geography != null ? $"{report.Geography.LocationLabel} Battle" : "Encounter Zone Battle");
            string zoneKey = _pendingQuickResolveZoneKey;
            EncounterZoneData zone = _pendingQuickResolveZone;

            ClearPendingQuickResolve();
            ClearPendingScoutingReport();

            if (monsters == null || monsters.Count == 0)
            {
                monsters = SelectSkirmishMonsters(ExploreSession != null && ExploreSession.CurrentMap.IsDungeon);
            }

            _activeEncounterZone = zone;
            _activeEncounterZoneKey = zoneKey;
            CombatInitiativeSurpriseMode surprise = report != null
                ? report.ResolveEngageSurpriseMode()
                : CombatInitiativeSurpriseMode.None;
            int bonusPct = report != null
                ? report.RewardBonusPct
                : (ExploreSession != null && ExploreSession.CurrentWorldClock.IsNight ? EncounterDirector.NightRewardBonusPercent : 0);

            StartTacticalCombat(monsters, title, surprise, bonusPct);
        }

        private void HandleEncounterInterceptEvade()
        {
            audioDirector?.PlayUiClick();
            EncounterScoutingReport report = _pendingScoutingReport;
            List<MonsterDefinition> monsters = _pendingQuickResolveMonsters;
            string title = !string.IsNullOrWhiteSpace(_pendingQuickResolveTitle)
                ? _pendingQuickResolveTitle
                : (report?.Geography != null ? $"{report.Geography.LocationLabel} Battle" : "Encounter Zone Battle");
            string zoneKey = _pendingQuickResolveZoneKey;
            EncounterZoneData zone = _pendingQuickResolveZone;

            ClearPendingQuickResolve();
            ClearPendingScoutingReport();

            if (ExploreSession == null)
            {
                return;
            }

            if (report != null && report.IsAmbushedInDark)
            {
                _activeEncounterZone = zone;
                _activeEncounterZoneKey = zoneKey;
                StartTacticalCombat(monsters, title, CombatInitiativeSurpriseMode.EnemySurprisedParty, report.RewardBonusPct);
                return;
            }

            EncounterCheckSpec evadeSpec = EncounterDirector.BuildEvadeCheckSpec(report, ExploreSession.Party);
            EncounterCheckRollResult rollRes = EncounterDirector.RollEvadeCheck(report, ExploreSession.Party, _roller);

            void ApplyEvadeOutcome()
            {
                if (rollRes.Success)
                {
                    ExploreSession.ApplyStealthEvade();
                    ApplyAtmosphereAndTorchLighting(GridToWorldAtElevation(ExploreSession.Position), forceImmediate: false);
                    hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false, _lastContextPoiLabel);
                    hudController?.ShowToast(
                        $"Evaded the patrol! (Rolled {rollRes.Total} vs DC {rollRes.Dc}, +{ExplorationSession.DefaultStealthEvadeTurns} turns).",
                        2.6f);
                    return;
                }

                _activeEncounterZone = zone;
                _activeEncounterZoneKey = zoneKey;
                int bonusPct = report != null
                    ? report.RewardBonusPct
                    : (ExploreSession.CurrentWorldClock.IsNight ? EncounterDirector.NightRewardBonusPercent : 0);
                hudController?.ShowToast($"Evade failed ({rollRes.Total} < DC {rollRes.Dc}) - patrol engages!", 2.4f);
                StartTacticalCombat(monsters, title, CombatInitiativeSurpriseMode.None, bonusPct);
            }

            if (hudController != null && Application.isPlaying && !Application.isBatchMode)
            {
                hudController.ShowDiceRollCheck(evadeSpec, rollRes, ApplyEvadeOutcome);
            }
            else
            {
                ApplyEvadeOutcome();
            }
        }

        private void HandleEncounterInterceptQuickResolve()
        {
            audioDirector?.PlayUiClick();
            EncounterScoutingReport report = _pendingScoutingReport;
            List<MonsterDefinition> monsters = _pendingQuickResolveMonsters;
            string title = !string.IsNullOrWhiteSpace(_pendingQuickResolveTitle)
                ? _pendingQuickResolveTitle
                : (report?.Geography != null ? $"{report.Geography.LocationLabel} Battle" : "Encounter Zone Battle");

            QuickResolveSummary qrSummary = null;
            if (monsters != null && monsters.Count > 0)
            {
                CanOfferQuickResolveForEncounter(monsters, out qrSummary);
            }

            EncounterCheckSpec qrSpec = EncounterDirector.BuildQuickResolveCheckSpec(qrSummary, ExploreSession?.Party);
            EncounterCheckRollResult rollRes = EncounterDirector.RollQuickResolveCheck(qrSummary, ExploreSession?.Party, _roller);

            if (hudController != null && Application.isPlaying && !Application.isBatchMode)
            {
                hudController.ShowDiceRollCheck(qrSpec, rollRes, () =>
                {
                    if (rollRes.Success)
                    {
                        HandleQuickResolveAutoWin();
                    }
                    else
                    {
                        hudController.ShowToast($"Tactical rout failed ({rollRes.Total} < DC {rollRes.Dc}) - engage!", 2.4f);
                        HandleEncounterInterceptEngage();
                    }
                });
            }
            else if (rollRes.Success)
            {
                HandleQuickResolveAutoWin();
            }
            else
            {
                HandleEncounterInterceptEngage();
            }
        }

        private void HandleLightTorchFromHud()
        {
            if (ExploreSession?.Party == null)
            {
                return;
            }

            if (_isInCombat && CombatSession != null)
            {
                CombatActionResult combatRes = CombatSession.TryLightTorchInCombat();
                if (combatRes.Success)
                {
                    audioDirector?.PlaySpell("fire");
                    ApplyAtmosphereAndTorchLighting(GetCombatPartyTorchWorldPos(), forceImmediate: true);
                    SyncFogOfWarAndWeatherVisuals();
                    RefreshCombatHudState();
                }
                else
                {
                    audioDirector?.PlayUiClick();
                }
                if (!string.IsNullOrWhiteSpace(combatRes.Message))
                {
                    hudController?.ShowToast(combatRes.Message, 2.4f);
                }
                return;
            }

            bool lit = ExploreSession.Party.TryLightTorchFromStash(out string feedback, Bundle?.Items);
            if (lit)
            {
                audioDirector?.PlaySpell("fire");
            }
            else
            {
                audioDirector?.PlayUiClick();
            }

            ApplyAtmosphereAndTorchLighting(GridToWorldAtElevation(ExploreSession.Position), forceImmediate: false);
            SyncFogOfWarAndWeatherVisuals();
            hudController?.RefreshPartyDrawer(ExploreSession.Party, Bundle, _selectedPartyHeroIndex);
            hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false, _lastContextPoiLabel);
            if (!string.IsNullOrWhiteSpace(feedback))
            {
                hudController?.ShowToast(feedback, 2.4f);
            }
        }

        private void ApplyAtmosphereAndTorchLighting(Vector3 focusWorldPos, bool forceImmediate, bool allowContinuousNormalizedUpdate = false)
        {
            if (ExploreSession == null || ExploreSession.CurrentMap == null)
            {
                return;
            }

            TileMapData map = ExploreSession.CurrentMap;
            WorldClock clock = ExploreSession.CurrentWorldClock;
            bool combatFireLit = _isInCombat && CombatSession != null && CombatSession.IsCombatFireIlluminated;
            bool hasLitTorch = (ExploreSession.Party != null && ExploreSession.Party.TorchTurnsRemaining > 0) || combatFireLit;
            bool isNightOrDark = map.IsDungeon || clock.IsNight;
            bool isCombat = _isInCombat;

            bool stateChanged = !_hasAppliedLightingState ||
                                _lastLightingPhase != clock.Phase ||
                                _lastLightingDungeon != map.IsDungeon ||
                                _lastLightingTorch != hasLitTorch ||
                                _lastLightingCombat != isCombat;

            float normalizedTime = clock.NormalizedTimeOfDay;
            bool timeArcChanged = allowContinuousNormalizedUpdate &&
                                  !map.IsDungeon &&
                                  (_lastAppliedNormalizedTime < 0f ||
                                   Mathf.Abs(normalizedTime - _lastAppliedNormalizedTime) >= 0.00025f);

            if (forceImmediate || stateChanged)
            {
                _hasAppliedLightingState = true;
                _lastLightingPhase = clock.Phase;
                _lastLightingDungeon = map.IsDungeon;
                _lastLightingTorch = hasLitTorch;
                _lastLightingCombat = isCombat;
                _lastAppliedNormalizedTime = normalizedTime;

                biomeAtmosphere?.ApplyBiome(map.IsDungeon, normalizedTime, clock.Phase, clock.PhaseProgress01, hasLitTorch, forceImmediate, isCombat);
                worldRenderer?.ApplyTimeOfDayLighting(normalizedTime, clock.Phase, map.IsDungeon, hasLitTorch);
            }
            else if (timeArcChanged)
            {
                _lastAppliedNormalizedTime = normalizedTime;
                biomeAtmosphere?.ApplyBiome(map.IsDungeon, normalizedTime, clock.Phase, clock.PhaseProgress01, hasLitTorch, immediate: true, isCombat);
                worldRenderer?.ApplyTimeOfDayLighting(normalizedTime, clock.Phase, map.IsDungeon, hasLitTorch);
            }

            Vector3 resolvedTorchWorld = _isInCombat ? GetCombatPartyTorchWorldPos() : focusWorldPos;
            biomeAtmosphere?.UpdateFocusPosition(resolvedTorchWorld);
            worldRenderer?.SetPartyTorchPosition(resolvedTorchWorld, map.IsDungeon, isNightOrDark, hasLitTorch);
        }

        private void HandleQuickResolveFight()
        {
            audioDirector?.PlayUiClick();
            EncounterScoutingReport report = _pendingScoutingReport;
            List<MonsterDefinition> monsters = _pendingQuickResolveMonsters;
            string title = string.IsNullOrWhiteSpace(_pendingQuickResolveTitle) ? "Encounter Zone Battle" : _pendingQuickResolveTitle;
            string zoneKey = _pendingQuickResolveZoneKey;
            EncounterZoneData zone = _pendingQuickResolveZone;
            ClearPendingQuickResolve();
            ClearPendingScoutingReport();

            _activeEncounterZone = zone;
            _activeEncounterZoneKey = zoneKey;
            CombatInitiativeSurpriseMode surprise = report != null
                ? report.ResolveEngageSurpriseMode()
                : CombatInitiativeSurpriseMode.None;
            int bonusPct = report != null
                ? report.RewardBonusPct
                : (ExploreSession != null && ExploreSession.CurrentWorldClock.IsNight ? EncounterDirector.NightRewardBonusPercent : 0);
            StartTacticalCombat(monsters, title, surprise, bonusPct);
        }

        private void HandleQuickResolveAutoWin()
        {
            audioDirector?.PlayUiClick();
            EncounterScoutingReport report = _pendingScoutingReport;
            List<MonsterDefinition> monsters = _pendingQuickResolveMonsters;
            string zoneKey = _pendingQuickResolveZoneKey;
            EncounterZoneData zone = _pendingQuickResolveZone;
            ClearPendingQuickResolve();
            ClearPendingScoutingReport();

            if (ExploreSession == null || Bundle == null || monsters == null || monsters.Count == 0)
            {
                return;
            }

            TileMapData map = ExploreSession.CurrentMap;
            GridCoord partyPos = ExploreSession.Position;
            int width = 12;
            int height = 10;
            GridCoord origin = FindCleanArenaOrigin(map, partyPos, width, height);
            CombatGrid grid = CombatGrid.FromTileMapSlice(
                map,
                origin.X,
                origin.Y,
                width,
                height,
                ExploreSession.Party.HasBoat,
                elevationResolver: (wx, wy) => ElevationMetersToTier(worldRenderer != null ? worldRenderer.GetTileElevation(wx, wy) : 0f),
                surfaceResolver: (wx, wy) => ResolveInitialCombatSurface(map, wx, wy));

            GridCoord localCenter = new GridCoord(
                Mathf.Clamp(partyPos.X - origin.X, 0, width - 1),
                Mathf.Clamp(partyPos.Y - origin.Y, 0, height - 1));
            List<GridCoord> connectedWalkable = CollectConnectedWalkableTiles(grid, localCenter);
            if (connectedWalkable.Count == 0)
            {
                connectedWalkable.Add(localCenter);
            }

            HashSet<GridCoord> occupied = new HashSet<GridCoord>();
            List<CombatantState> combatants = new List<CombatantState>();
            GridCoord[] heroSlots =
            {
                new GridCoord(2, 4), new GridCoord(1, 5), new GridCoord(3, 5),
                new GridCoord(2, 6), new GridCoord(3, 4), new GridCoord(1, 3)
            };
            for (int i = 0; i < ExploreSession.Party.Members.Count; i++)
            {
                PartyMemberState m = ExploreSession.Party.Members[i];
                if (m.Hp <= 0) m.Hp = 1;
                GridCoord spot = PickNearestFreeWalkable(connectedWalkable, occupied, heroSlots[i % heroSlots.Length], map, origin.X, origin.Y);
                occupied.Add(spot);
                combatants.Add(CombatantState.FromHero(m, spot, Bundle.Items));
            }

            GridCoord[] monsterSlots =
            {
                new GridCoord(7, 4), new GridCoord(8, 5), new GridCoord(7, 6),
                new GridCoord(9, 4), new GridCoord(8, 6), new GridCoord(9, 6)
            };
            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterDefinition def = monsters[i];
                GridCoord spot = PickNearestFreeWalkable(connectedWalkable, occupied, monsterSlots[i % monsterSlots.Length], map, origin.X, origin.Y);
                occupied.Add(spot);
                combatants.Add(CombatantState.FromMonster(def, $"mon_{i}_{def.Id}", spot, _roller));
            }

            EncounterDirector.ApplyBiomeArenaFeatures(grid, ExploreSession.GetCurrentGeography(), occupied, _roller);
            int rewardBonusPct = report != null
                ? report.RewardBonusPct
                : (ExploreSession.CurrentWorldClock.IsNight ? EncounterDirector.NightRewardBonusPercent : 0);

            bool qrDark = ExploreSession.CurrentMap.IsDungeon || ExploreSession.CurrentWorldClock.IsNight;
            TacticalCombatSession qrSession = new TacticalCombatSession(
                grid,
                combatants,
                Bundle.Spells,
                Bundle.Items,
                _roller,
                RulesetMode.ModernBalanced,
                ExploreSession.Party,
                CombatInitiativeSurpriseMode.None,
                rewardBonusPct,
                isAmbientDarkness: qrDark,
                deferLootDeposit: true);

            CombatOutcome outcome = qrSession.QuickResolveCombat();
            if (outcome == CombatOutcome.Victory)
            {
                _pendingQuickResolveVictorySession = qrSession;
                if (zone != null)
                {
                    ExploreSession.CompleteEncounterZone(zone);
                }
                else
                {
                    ExploreSession.GrantPostCombatGrace();
                    if (!string.IsNullOrEmpty(zoneKey))
                    {
                        ExploreSession.MarkEncounterCleared(zoneKey);
                    }
                }

                ExploreSession.SyncAfterCombat(qrSession, floorDeadToOne: true);

                EnsureExploreHeroViews();
                RefreshContextualExplorePoi();
                ApplyAtmosphereAndTorchLighting(GridToWorldAtElevation(ExploreSession.Position), forceImmediate: false);
                hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false, _lastContextPoiLabel);
                audioDirector?.PlayMusic("treasure", 0.7f);
                audioDirector?.PlayCoin();
                if (qrSession.LastVictorySummary != null)
                {
                    hudController?.ShowVictoryOutcome(qrSession.LastVictorySummary, ExploreSession.Party, Bundle);
                }
                else
                {
                    List<string> lootNames = new List<string>(qrSession.CollectedLootItemIds.Count);
                    for (int i = 0; i < qrSession.CollectedLootItemIds.Count; i++)
                    {
                        int itemId = qrSession.CollectedLootItemIds[i];
                        if (Bundle.Items != null && Bundle.Items.TryGetValue(itemId, out ItemDefinition def))
                        {
                            lootNames.Add(def.Name);
                        }
                        else
                        {
                            lootNames.Add($"Item #{itemId}");
                        }
                    }

                    hudController?.ShowOutcomeModal(
                        CombatOutcome.Victory,
                        qrSession.TotalXpEarned,
                        qrSession.VictoryLevelsGained,
                        qrSession.TotalGoldLoot,
                        lootNames);
                }
            }
            else if (outcome == CombatOutcome.Defeat)
            {
                ExploreSession.SyncAfterCombat(qrSession, floorDeadToOne: true);
                hudController?.ShowOutcomeModal(CombatOutcome.Defeat, 0, 0, 0, null);
            }
            else
            {
                _activeEncounterZone = zone;
                _activeEncounterZoneKey = zoneKey;
                StartTacticalCombat(monsters, "Encounter Zone Battle", CombatInitiativeSurpriseMode.None, rewardBonusPct);
            }
        }

        private void CollectActiveUnitWorldPositions(List<Vector3> results)
        {
            results.Clear();
            if (_isInCombat && CombatSession != null)
            {
                for (int i = 0; i < CombatSession.Combatants.Count; i++)
                {
                    CombatantState c = CombatSession.Combatants[i];
                    if (c == null || !c.IsAlive)
                    {
                        continue;
                    }

                    if (_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView view) && view != null)
                    {
                        results.Add(view.transform.position);
                    }
                    else
                    {
                        results.Add(GridToWorldAtElevation(LocalToWorld(c.Position)));
                    }
                }
            }
            else
            {
                for (int i = 0; i < _exploreHeroViews.Count; i++)
                {
                    BywaterUnitView view = _exploreHeroViews[i];
                    if (view != null && view.gameObject.activeSelf)
                    {
                        results.Add(view.transform.position);
                    }
                }
            }
        }

        private void UpdateActiveTreeOcclusionFade()
        {
            if (worldRenderer == null)
            {
                return;
            }

            CollectActiveUnitWorldPositions(_treeFadePositionsScratch);
            worldRenderer.UpdateTreeOcclusionFade(_treeFadePositionsScratch, fadeCenterBuildingsInCombat: _isInCombat);
        }

        private void RefreshUnitNameplates()
        {
            if (!_isInCombat || CombatSession == null)
            {
                return;
            }

            Keyboard kb = Keyboard.current;
            bool altHeld = kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
            bool showAll = _nameplatesAlwaysOn || altHeld;

            _nameplateStaggerViewsScratch.Clear();
            for (int i = 0; i < CombatSession.Combatants.Count; i++)
            {
                CombatantState c = CombatSession.Combatants[i];
                if (c == null || !_combatUnitViews.TryGetValue(c.Id, out BywaterUnitView view) || view == null)
                {
                    continue;
                }

                bool visible = c.IsAlive && (showAll || string.Equals(c.Id, _hoveredCombatUnitId, StringComparison.Ordinal));
                if (visible)
                {
                    string detail = c.MaxSp > 0
                        ? $"HP {c.Hp}/{c.MaxHp} | SP {c.Sp}/{c.MaxSp}"
                        : $"HP {c.Hp}/{c.MaxHp} | AC {c.EffectiveAc}";
                    view.SetNameplateVisible(true, detail);
                    _nameplateStaggerViewsScratch.Add(view);
                }
                else
                {
                    view.SetNameplateVisible(false);
                }
            }

            if (_nameplateStaggerViewsScratch.Count > 1)
            {
                BywaterUnitView.ResolveNameplateStagger(_nameplateStaggerViewsScratch, Camera.main);
            }
        }

        private GridCoord FindBestShowcaseHoverTile(CombatantState actor)
        {
            if (CombatSession == null || actor == null)
            {
                return new GridCoord(-1, -1);
            }

            IReadOnlyList<ReachableTileInfo> reach = CombatSession.GetReachableTiles(actor.Id);
            GridCoord best = new GridCoord(-1, -1);
            int bestScore = int.MinValue;

            for (int i = 0; i < reach.Count; i++)
            {
                ReachableTileInfo tile = reach[i];
                if (tile.Coord == actor.Position || tile.ProvokesAoO || GetLivingUnitAt(tile.Coord) != null)
                {
                    continue;
                }

                int elev = CombatSession.Grid.GetElevation(tile.Coord);
                int score = (elev * 100) + tile.PathCost;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = tile.Coord;
                }
            }

            return best;
        }

        private static GridCoord PickNearestFreeWalkable(
            List<GridCoord> candidates,
            HashSet<GridCoord> occupied,
            GridCoord preferred,
            TileMapData map = null,
            int originX = 0,
            int originY = 0)
        {
            GridCoord best = preferred;
            int bestTier = int.MaxValue;
            int bestDist = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                GridCoord c = candidates[i];
                if (occupied.Contains(c))
                {
                    continue;
                }

                bool southOfNorthRows = c.Y >= 3;
                bool northClear = map == null || IsSpawnTileNorthClear(map, originX + c.X, originY + c.Y);
                int tier = (southOfNorthRows && northClear)
                    ? 0
                    : (northClear ? 1 : (southOfNorthRows ? 2 : 3));

                int d = c.ManhattanDistance(preferred);
                if (tier < bestTier || (tier == bestTier && d < bestDist))
                {
                    bestTier = tier;
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

        private void TickContinuousOverlandClock()
        {
            if (ExploreSession == null || _isInCombat || _isCampActive || Application.isBatchMode)
            {
                return;
            }

            if (hudController != null && hudController.IsModalOpen)
            {
                return;
            }

            TileMapData map = ExploreSession.CurrentMap;
            if (map == null || Time.timeScale <= 0.001f)
            {
                return;
            }

            float minutesPerRealSecond = ExploreSession.BalanceOverrides?.Survival?.MinutesPerRealSecond
                ?? (balanceCatalog != null && balanceCatalog.survival != null ? balanceCatalog.survival.minutesPerRealSecond : 1.0f);
            if (minutesPerRealSecond <= 0.0001f)
            {
                return;
            }

            double deltaMinutes = Mathf.Min(Time.unscaledDeltaTime, 0.25f) * minutesPerRealSecond;
            if (map.IsDungeon)
            {
                bool dungeonTorchExpired = ExploreSession.Party.BurnTorchMinutes(deltaMinutes);
                int torchMin = ExploreSession.Party.TorchTurnsRemaining;
                if (dungeonTorchExpired)
                {
                    ExploreSession.RefreshFogOfWar();
                    hudController?.ShowToast("Your torch flickered out!", 2.4f);
                }

                if (dungeonTorchExpired || torchMin != _lastSyncedTorchMinute)
                {
                    _lastSyncedTorchMinute = torchMin;
                    ApplyAtmosphereAndTorchLighting(GridToWorldAtElevation(ExploreSession.Position), forceImmediate: false, allowContinuousNormalizedUpdate: true);
                    SyncFogOfWarAndWeatherVisuals();
                    if (_exploreWalkCoroutine == null)
                    {
                        hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false, _lastContextPoiLabel);
                    }
                }
                return;
            }

            bool phaseChanged = ExploreSession.AdvanceWorldClockMinutes(deltaMinutes);
            bool torchExpired = ExploreSession.LastClockAdvanceExpiredTorch;
            if (torchExpired)
            {
                hudController?.ShowToast("Your torch flickered out!", 2.4f);
            }

            Vector3 focusPos = GridToWorldAtElevation(ExploreSession.Position);
            ApplyAtmosphereAndTorchLighting(focusPos, forceImmediate: false, allowContinuousNormalizedUpdate: true);

            int currentMinute = (int)Math.Floor(ExploreSession.CurrentWorldClock.TotalMinutes);
            int curTorchMin = ExploreSession.Party.TorchTurnsRemaining;
            if (phaseChanged || torchExpired || currentMinute != _lastSyncedClockMinute || curTorchMin != _lastSyncedTorchMinute)
            {
                _lastSyncedClockMinute = currentMinute;
                _lastSyncedTorchMinute = curTorchMin;
                SyncFogOfWarAndWeatherVisuals();
                if (_exploreWalkCoroutine == null)
                {
                    hudController?.RefreshExploreHud(ExploreSession, Bundle, null, null, 0, string.Empty, false, _lastContextPoiLabel);
                }
            }
        }

        private void SyncFogOfWarAndWeatherVisuals()
        {
            if (ExploreSession == null)
            {
                return;
            }

            TileMapData map = ExploreSession.CurrentMap;
            if (worldRenderer != null && ExploreSession.FogOfWar != null)
            {
                bool enableFog = !_isInCombat && map != null;
                FogOfWarMap fog = ExploreSession.FogOfWar;
                byte[] fogStates = fog.ExportVisibilityStates();
                worldRenderer.SetFogOfWarData(fogStates, fog.Width, fog.Height, enableFog);

                float nightFactor = 0f;
                if (ExploreSession.CurrentWorldClock.IsNight)
                {
                    nightFactor = 1f;
                }
                else if (ExploreSession.CurrentWorldClock.Phase == WorldTimePhase.Dusk ||
                         ExploreSession.CurrentWorldClock.Phase == WorldTimePhase.Dawn)
                {
                    nightFactor = 0.45f;
                }
                worldRenderer.SetAtmosphereFogAndNight(fogStates, fog.Width, fog.Height, enableFog, nightFactor);
            }

            WeatherState weather = ExploreSession.Weather;
            bool isRaining = weather == WeatherState.LightRain || weather == WeatherState.Storm;
            float windScale = weather == WeatherState.Storm ? 2.2f : (weather == WeatherState.LightRain ? 1.55f : 1.0f);
            worldRenderer?.SetWeatherVisuals(isRaining, windScale);

            string weatherLabel = FormatWeatherLabel(weather);
            hudController?.SetSurvivalBannerState(weatherLabel, ExploreSession.CurrentVisionRadius);

            bool isDungeonOrIndoor = map != null && map.IsDungeon;
            audioDirector?.ApplyEnvironmentSnapshot(isDungeonOrIndoor);
            SyncAmbientAudioContext();
        }

        private void RebuildAmbientPoiCache(bool force)
        {
            TileMapData map = ExploreSession?.CurrentMap;
            CartographyWorldData carto = worldRenderer != null ? worldRenderer.CartographyData : null;
            string mapId = map != null ? (map.MapId ?? string.Empty) : string.Empty;
            if (!force && string.Equals(_cachedAmbientMapId, mapId, StringComparison.Ordinal))
            {
                return;
            }

            _cachedAmbientMapId = mapId;
            _cachedRiverWorldPositions.Clear();
            _cachedForgeWorldPositions.Clear();
            _cachedFireWorldPositions.Clear();

            if (map == null || carto == null)
            {
                return;
            }

            for (int y = 0; y < map.Height; y += 2)
            {
                for (int x = 0; x < map.Width; x += 2)
                {
                    CartographyTile tile = carto.GetTile(x, y);
                    if (tile.IsWater)
                    {
                        _cachedRiverWorldPositions.Add(CartographyWorldData.GridToWorld(new GridCoord(x, y), tile.Elevation));
                    }
                }
            }

            IReadOnlyList<CartographyPropPlacement> props = carto.Props;
            if (props != null)
            {
                for (int i = 0; i < props.Count; i++)
                {
                    CartographyPropPlacement prop = props[i];
                    if (prop.Kind == CartographyPropKind.Blacksmith)
                    {
                        _cachedForgeWorldPositions.Add(prop.WorldPosition);
                    }
                    else if (prop.Kind == CartographyPropKind.Campfire4f ||
                             prop.Kind == CartographyPropKind.TorchBrazier ||
                             prop.Kind == CartographyPropKind.DungeonWallSconce4f)
                    {
                        _cachedFireWorldPositions.Add(prop.WorldPosition);
                    }
                }
            }
        }

        private void SyncAmbientAudioContext()
        {
            if (audioDirector == null || ExploreSession == null)
            {
                return;
            }

            RebuildAmbientPoiCache(force: false);

            TileMapData map = ExploreSession.CurrentMap;
            Vector3 listenerWorldPos = _isInCombat
                ? GetCombatPartyTorchWorldPos()
                : GridToWorldAtElevation(ExploreSession.Position);

            Vector3? nearestRiver = FindNearestWorldPosition(_cachedRiverWorldPositions, listenerWorldPos);
            Vector3? nearestForge = FindNearestWorldPosition(_cachedForgeWorldPositions, listenerWorldPos);
            Vector3? nearestFire = FindNearestWorldPosition(_cachedFireWorldPositions, listenerWorldPos);
            bool isIndoorOrDungeon = map != null && map.IsDungeon;
            WorldTimePhase phase = ExploreSession.CurrentWorldClock.Phase;
            bool isDayOrDawn = phase == WorldTimePhase.Day || phase == WorldTimePhase.Dawn;
            string weatherLabel = FormatWeatherLabel(ExploreSession.Weather);

            audioDirector.UpdateAmbientContext(
                listenerWorldPos,
                nearestRiver,
                nearestForge,
                nearestFire,
                isIndoorOrDungeon,
                isDayOrDawn,
                weatherLabel,
                _isInCombat);
        }

        private static Vector3? FindNearestWorldPosition(List<Vector3> candidates, Vector3 listenerPos)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            Vector3 best = candidates[0];
            float bestDistSq = (best - listenerPos).sqrMagnitude;
            for (int i = 1; i < candidates.Count; i++)
            {
                float dSq = (candidates[i] - listenerPos).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = candidates[i];
                }
            }

            return best;
        }

        private void ClearExploreInteractableHover()
        {
            if (_hasHoverInteractable)
            {
                _hasHoverInteractable = false;
                worldRenderer?.SetHoveredInteractable(null, default);
            }
        }

        private void SetHoveredCombatUnitRim(CombatantState hoveredUnit)
        {
            string nextId = hoveredUnit != null && hoveredUnit.IsAlive ? hoveredUnit.Id : null;
            if (string.Equals(_hoveredRimUnitId, nextId, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(_hoveredRimUnitId) &&
                _combatUnitViews.TryGetValue(_hoveredRimUnitId, out BywaterUnitView oldView) &&
                oldView != null)
            {
                oldView.ShowHoverRim(false, default);
            }

            _hoveredRimUnitId = nextId;
            if (!string.IsNullOrEmpty(_hoveredRimUnitId) &&
                hoveredUnit != null &&
                _combatUnitViews.TryGetValue(_hoveredRimUnitId, out BywaterUnitView newView) &&
                newView != null)
            {
                Color rimColor = hoveredUnit.Side == CombatSide.Monster ? MonsterRingColor : HeroRingColor;
                newView.ShowHoverRim(true, rimColor);
            }
        }

        private void HandleTacticalPitchToggle()
        {
            if (!_isInCombat)
            {
                return;
            }

            audioDirector?.PlayUiClick();
            cameraRig?.ToggleTacticalPitch();
        }

        private void OnCombatLightningChained(CombatChainLightningEvent e)
        {
            if (e == null)
            {
                return;
            }

            int count = e.HopCoords != null ? e.HopCoords.Count : 0;
            List<Vector3> worldHops = new List<Vector3>(count + 1);
            Vector3 originWorld = GridToWorldAtElevation(LocalToWorld(e.Origin));
            if (count == 0 || e.HopCoords[0] != e.Origin)
            {
                worldHops.Add(originWorld);
            }

            for (int i = 0; i < count; i++)
            {
                worldHops.Add(GridToWorldAtElevation(LocalToWorld(e.HopCoords[i])));
            }

            if (worldHops.Count >= 2)
            {
                GetOrEnsureVfxDirector()?.PlayChainLightning(worldHops);
            }
            audioDirector?.PlayLightningChain();
        }

        private void OnCombatPropDetonated(CombatExplosionEvent e)
        {
            if (e == null)
            {
                return;
            }

            GridCoord worldOrigin = LocalToWorld(e.Origin);
            _destroyedArenaPropCoords.Add(e.Origin);
            worldRenderer?.MarkArenaObstacleDestroyed(worldOrigin, leaveScorch: true);

            Vector3 blastWorld = GridToWorldAtElevation(worldOrigin);
            GetOrEnsureVfxDirector()?.PlayKegExplosion(blastWorld, e.IsChainReaction ? 2.2f : 1.75f);
            audioDirector?.PlayExplosion(e.IsChainReaction || (e.AffectedTiles != null && e.AffectedTiles.Count >= 9));
            cameraRig?.PlayFocusBeat(blastWorld, -1.6f, 0.42f);
            cameraRig?.PlayImpactImpulse(0.24f, 0.22f, Vector3.down);
            TriggerHitStop(0.055f);
            SyncDestructibleArenaProps();
        }

        private void SyncDestructibleArenaProps()
        {
            if (CombatSession == null || CombatSession.DestructibleProps == null || worldRenderer == null)
            {
                return;
            }

            for (int i = 0; i < CombatSession.DestructibleProps.Count; i++)
            {
                CombatDestructibleProp prop = CombatSession.DestructibleProps[i];
                if (prop != null && prop.IsDestroyed && _destroyedArenaPropCoords.Add(prop.Coord))
                {
                    bool leaveScorch = prop.ObstacleKind == CombatArenaObstacleKind.OilKegCluster;
                    worldRenderer.MarkArenaObstacleDestroyed(LocalToWorld(prop.Coord), leaveScorch);
                }
            }
        }

        private BywaterCombatVfxDirector GetOrEnsureVfxDirector()
        {
            if (gridOverlays == null)
            {
                return null;
            }

            BywaterCombatVfxDirector director = gridOverlays.GetComponent<BywaterCombatVfxDirector>();
            if (director == null)
            {
                director = gridOverlays.gameObject.AddComponent<BywaterCombatVfxDirector>();
            }
            return director;
        }

        private static string FormatWeatherLabel(WeatherState weather)
        {
            switch (weather)
            {
                case WeatherState.Storm:
                    return "Storm";
                case WeatherState.LightRain:
                    return "Light Rain";
                default:
                    return "Clear";
            }
        }
    }
}
