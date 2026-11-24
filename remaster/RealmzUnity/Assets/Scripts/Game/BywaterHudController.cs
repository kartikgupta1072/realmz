using System;
using System.Collections.Generic;
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
using UnityEngine.UI;

namespace Realmz.Game
{
    public enum CombatActionMode
    {
        Smart = 0,
        Move = 0,
        Melee = 1,
        Ranged = 2,
        Spell = 3,
        Potion = 4,
        Shove = 5
    }

    /// <summary>
    /// Builds and manages the responsive 1920x1080 Canvas UI for City of Bywater
    /// exploration, party/stash/camp management, town shops, and tactical combat.
    /// </summary>
    public sealed partial class BywaterHudController : MonoBehaviour
    {
        public const CombatActionMode DefaultSmartMode = CombatActionMode.Smart;

        private sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action OnEnter;
            public Action OnExit;

            public void OnPointerEnter(PointerEventData eventData)
            {
                OnEnter?.Invoke();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                OnExit?.Invoke();
            }
        }

        private sealed class HeroCardRefs
        {
            public RectTransform Root;
            public Image Background;
            public Image Portrait;
            public Image[] ConditionBadges;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI ClassText;
            public Image HpFill;
            public TextMeshProUGUI HpText;
            public Image SpFill;
            public TextMeshProUGUI SpText;
            public Image XpFill;
            public Button LvlUpBadgeBtn;
            public bool LastVisible;
            public string LastMemberId;
            public int LastLevel = -1;
            public int LastXp = -1;
            public int LastPendingLevels = -1;
            public int LastHp = -1;
            public int LastMaxHp = -1;
            public int LastSp = -1;
            public int LastMaxSp = -1;
            public int LastCondCount = -1;
            public bool LastSelected;
            public bool HasCachedState;
        }

        private sealed class InitiativeCardRefs
        {
            public RectTransform Root;
            public LayoutElement Layout;
            public Image Background;
            public Image Portrait;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI MetaText;
        }

        private sealed class RoundDividerRefs
        {
            public GameObject Root;
            public TextMeshProUGUI Label;
        }

        private sealed class CombatPartyCardRefs
        {
            public GameObject Root;
            public Button Button;
            public CanvasGroup CanvasGroup;
            public Image Background;
            public Image Portrait;
            public Image[] ConditionBadges;
            public TextMeshProUGUI NameText;
            public Image HpFill;
            public Image SpFill;
            public string BoundCombatantId;
        }

        private sealed class HotbarSlotRefs
        {
            public GameObject Root;
            public Button Button;
            public Image Background;
            public Image Icon;
            public TextMeshProUGUI HotkeyText;
            public TextMeshProUGUI LabelText;
            public TextMeshProUGUI SpText;
            public LayoutElement Layout;
            public string ActionId;
            public CombatActionMode BoundMode;
            public int BoundSpellId;
            public string TooltipTitle;
            public string TooltipBody;
        }

        private sealed class EquipSlotRefs
        {
            public EquipmentSlot Slot;
            public Image ItemIcon;
            public TextMeshProUGUI ItemLabel;
            public Button UnequipBtn;
        }

        private sealed class StashRowRefs
        {
            public GameObject Root;
            public Image ItemIcon;
            public GameObject UpgradePill;
            public TextMeshProUGUI InfoText;
            public Button EquipBtn;
            public Button UseBtn;
            public int BoundStashIndex;
        }

        private sealed class ShopBuyRowRefs
        {
            public GameObject Root;
            public Image ItemIcon;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI StatsText;
            public GameObject UpgradeBadge;
            public TextMeshProUGUI PriceText;
            public Button BuyBtn;
            public string BoundShopId;
            public int BoundItemId;
        }

        private sealed class ShopSellRowRefs
        {
            public GameObject Root;
            public Image ItemIcon;
            public TextMeshProUGUI ItemText;
            public TextMeshProUGUI ValueText;
            public Button SellBtn;
            public int BoundStashIndex;
        }

        private sealed class SpellRowRefs
        {
            public GameObject Root;
            public Button Button;
            public TextMeshProUGUI Label;
            public LayoutElement Layout;
            public int BoundSpellId;
        }

        public const int MaxLogLines = 40;
        public const int MaxHotbarSlots = 8;

        private BywaterAssetCatalog _catalog;
        private TMP_FontAsset _font;
        private bool _uiBuilt;
        private bool _isCombatHudActive;
        private bool _isDevToolsVisible;
        private string _contextPoiLabel;
        private Action _contextPoiAction;

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private GameObject _exploreRoot;
        private GameObject _combatRoot;

        // Top HUD bar
        private TextMeshProUGUI _topMapText;
        private TextMeshProUGUI _topGoldText;
        private TextMeshProUGUI _topTorchText;
        private TextMeshProUGUI _topTurnText;
        private GameObject _topSpacer;
        private Button _skirmishBtn;
        private Button _partyBtn;
        private Button _shopBtn;
        private Button _campBtn;
        private Button _shrineBtn;
        private Button _warpBtn;
        private TextMeshProUGUI _warpBtnLabel;
        private Button _contextPoiBtn;
        private TextMeshProUGUI _contextPoiBtnLabel;
        private Button _saveBtn;
        private Button _loadBtn;
        private Button _menuBtn;
        private Button _autoCombatBtn;
        private TextMeshProUGUI _autoCombatBtnLabel;
        private Button _combatSpeedBtn;
        private TextMeshProUGUI _combatSpeedLabel;
        private Button _combatAudioBtn;
        private TextMeshProUGUI _combatAudioBtnLabel;
        private Button _combatGridBtn;
        private Button _combatMenuBtn;

        // Explore HUD
        private readonly List<HeroCardRefs> _heroCards = new List<HeroCardRefs>(6);
        private TextMeshProUGUI _hoverCoordText;
        private TextMeshProUGUI _hoverDetailText;

        // Party / Stash / Camp Drawer
        private GameObject _partyDrawerRoot;
        private readonly List<Button> _partyHeroSelectorBtns = new List<Button>(6);
        private Image _drawerHeroPortrait;
        private TextMeshProUGUI _drawerHeroTitle;
        private TextMeshProUGUI _drawerHeroStats;
        private TextMeshProUGUI _drawerHeroConditions;
        private TextMeshProUGUI _drawerHeroSpells;
        private TextMeshProUGUI _drawerGoldTorchLabel;
        private readonly List<EquipSlotRefs> _equipSlots = new List<EquipSlotRefs>(5);
        private ScrollRect _partyStashScroll;
        private TextMeshProUGUI _stashEmptyLabel;
        private readonly List<StashRowRefs> _stashRowPool = new List<StashRowRefs>(16);
        private int _selectedHeroIndex;
        private PartyState _cachedParty;
        private BywaterM1Bundle _cachedBundle;
        private bool _cachedSafeZone = true;

        // Town Shop Modal (Single-Venue)
        private GameObject _shopModalRoot;
        private Image _shopPortraitImage;
        private TextMeshProUGUI _shopHeaderTitle;
        private TextMeshProUGUI _shopGreetingLabel;
        private TextMeshProUGUI _shopGoldLabel;
        private readonly Dictionary<string, Button> _shopCategoryBtns = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private Toggle _shopAffordableToggle;
        private TMP_InputField _shopSearchInput;
        private ScrollRect _shopBuyScroll;
        private TextMeshProUGUI _shopBuyEmptyLabel;
        private readonly List<ShopBuyRowRefs> _shopBuyRowPool = new List<ShopBuyRowRefs>(16);
        private ScrollRect _shopSellScroll;
        private TextMeshProUGUI _shopSellEmptyLabel;
        private readonly List<ShopSellRowRefs> _shopSellRowPool = new List<ShopSellRowRefs>(16);
        private string _activeShopId = "1";
        private string _activeShopCategory = "All";
        private bool _lastShopAffordableOnly;

        // Temple Shrine Modal
        private GameObject _templeModalRoot;
        private Image _templePortraitImage;
        private TextMeshProUGUI _templeGoldLabel;
        private TextMeshProUGUI _templeStatusLabel;
        private Button _templeHealBtn;
        private Button _templeCureBtn;
        private Button _templeRechargeBtn;

        // Combat HUD
        private readonly List<InitiativeCardRefs> _initiativeCards = new List<InitiativeCardRefs>(8);
        private readonly List<RoundDividerRefs> _roundDividers = new List<RoundDividerRefs>(7);
        private readonly List<CombatPartyCardRefs> _combatPartyCards = new List<CombatPartyCardRefs>(6);
        private Image _combatActorPortrait;
        private TextMeshProUGUI _combatActorName;
        private TextMeshProUGUI _combatActorVitals;
        private readonly List<Image> _movePipSegments = new List<Image>(10);
        private TextMeshProUGUI _combatMovePip;
        private Image _combatStdPipIcon;
        private TextMeshProUGUI _combatStdPip;
        private Image _combatSwiftPipIcon;
        private TextMeshProUGUI _combatSwiftPip;
        private readonly List<HotbarSlotRefs> _hotbarSlots = new List<HotbarSlotRefs>(MaxHotbarSlots);
        private readonly List<string> _visibleHotbarActionIds = new List<string>(16);
        private readonly List<string> _badgeKeysScratch = new List<string>(4);
        private Button _combatUndoBtn;
        private Image _combatUndoBtnIcon;
        private Button _combatPotionBtn;
        private Image _combatPotionBtnIcon;
        private Button _combatShoveBtn;
        private Image _combatShoveBtnIcon;
        private Image _combatShoveSwiftBadgeBg;
        private TextMeshProUGUI _combatShoveSwiftBadgeText;
        private Button _combatGuardBtn;
        private Image _combatGuardBtnIcon;
        private Button _combatEndTurnBtn;
        private Image _combatEndTurnBtnIcon;
        private bool _pulseEndTurn;
        private GameObject _spellPickerBar;
        private readonly List<SpellRowRefs> _spellRowPool = new List<SpellRowRefs>(12);
        private GameObject _targetingBannerRoot;
        private Image _targetingBannerBg;
        private TextMeshProUGUI _targetingBannerText;

        // Dirty-check caches for zero-hitch exploration HUD updates
        private bool _hasAppliedHudActive;
        private bool _hasAppliedTopBar;
        private bool _appliedTopBarCombat;
        private bool _appliedTopBarDev;
        private bool _appliedTopBarHasPoi;
        private int _lastExploreX = int.MinValue;
        private int _lastExploreY = int.MinValue;
        private bool _lastExploreIsDungeon;
        private int _lastExploreGold = int.MinValue;
        private int _lastExploreTorch = int.MinValue;
        private int _lastExploreTurn = int.MinValue;
        private bool _hasLastExploreHover;
        private GridCoord _lastExploreHoverCoord;
        private BywaterTileKind _lastExploreHoverKind;
        private int _lastExploreHoverSteps = int.MinValue;
        private string _lastExploreHoverPoi = string.Empty;
        private bool _lastExploreHoverReachable;

        // Cursor-Following Floating Tooltip
        private GameObject _cursorTooltipRoot;
        private RectTransform _tooltipPanel;
        private TextMeshProUGUI _tooltipTitle;
        private RectTransform _tooltipBadgeRoot;
        private Image _tooltipBadgeBg;
        private TextMeshProUGUI _tooltipBadgeText;
        private TextMeshProUGUI _tooltipBody;
        private bool _hasManualTooltipPos;

        // Collapsible 3-Line Ticker + Expandable History Drawer
        private GameObject _combatTickerRoot;
        private TextMeshProUGUI _tickerToggleBadge;
        private readonly List<TextMeshProUGUI> _tickerLines = new List<TextMeshProUGUI>(3);
        private GameObject _combatLogDrawerRoot;
        private ScrollRect _combatLogScroll;
        private readonly List<TextMeshProUGUI> _combatLogRows = new List<TextMeshProUGUI>(MaxLogLines);
        private int _logRingHead;
        private int _lastLogCount = -1;
        private TacticalCombatSession _lastLogSession;

        // Settings & Pause Modal ([Esc])
        private GameObject _settingsModalRoot;
        private Button _settingsAutoCombatBtn;
        private TextMeshProUGUI _settingsAutoCombatLabel;
        private Button _settingsAutoEndTurnBtn;
        private TextMeshProUGUI _settingsAutoEndTurnLabel;
        private readonly Button[] _settingsSpeedBtns = new Button[3];
        private Slider _settingsMasterSlider;
        private TextMeshProUGUI _settingsMasterValueText;
        private Slider _settingsMusicSlider;
        private TextMeshProUGUI _settingsMusicValueText;
        private Slider _settingsSfxSlider;
        private TextMeshProUGUI _settingsSfxValueText;
        private Button _settingsMuteBtn;
        private TextMeshProUGUI _settingsMuteLabel;
        private Slider _settingsGridOpacitySlider;
        private TextMeshProUGUI _settingsGridOpacityValueText;
        private Button _settingsNameplatesBtn;
        private TextMeshProUGUI _settingsNameplatesLabel;
        private Button _settingsDevToolsBtn;
        private TextMeshProUGUI _settingsDevToolsLabel;
        private Button _settingsSaveBtn;
        private Button _settingsLoadBtn;
        private Button _settingsFleeBtn;
        private Button _settingsQuitBtn;
        private TextMeshProUGUI _settingsQuitBtnLabel;
        private bool _isAutoCombatOn;
        private bool _isAutoEndTurnOn;
        private int _battleSpeedIndex;
        private bool _isMuted;
        private bool _nameplatesAlwaysOn;
        private bool _suppressSettingsCallbacks;
        private bool _quitConfirmArmed;

        // Pre-Battle Quick Resolve Modal
        private GameObject _quickResolveModalRoot;
        private TextMeshProUGUI _quickResolveTitle;
        private TextMeshProUGUI _quickResolveSummary;

        // Outcome Modal
        private GameObject _outcomeModalRoot;
        private TextMeshProUGUI _outcomeTitle;
        private GameObject _outcomeLevelUpBanner;
        private TextMeshProUGUI _outcomeLevelUpText;
        private TextMeshProUGUI _outcomeSummary;
        private Button _outcomeContinueBtn;
        private Button _outcomeRetryBtn;
        private Button _outcomeReviveBtn;

        // Toast Banner
        private GameObject _toastRoot;
        private CanvasGroup _toastCanvasGroup;
        private TextMeshProUGUI _toastText;
        private float _toastTimer;
        private float _toastFadeDuration = 0.6f;

        // Events
        public event Action OnSkirmishRequested;
        public event Action OnTogglePartyRequested;
        public event Action OnToggleShopRequested;
        public event Action OnCampButtonRequested;
        public event Action OnShrineRequested;
        public event Action OnWarpPortalRequested;
        public event Action OnContextPoiRequested;
        public event Action OnSaveRequested;
        public event Action OnLoadRequested;

        public event Action<int> OnHeroCardClicked;
        public event Action<int> OnPartyHeroSelected;
        public event Action<int, EquipmentSlot> OnUnequipRequested;
        public event Action<int, int> OnEquipFromStashRequested;
        public event Action<int, int> OnUseStashItemRequested;
        public event Action<CampMode> OnCampModeRequested;

        public event Action<string, int> OnBuyItemRequested;
        public event Action<int> OnSellItemRequested;
        public event Action OnSellAllJunkRequested;
        public event Action OnIdentifyAllRequested;
        public event Action OnTempleHealRequested;
        public event Action OnTempleCureRequested;
        public event Action OnTempleRechargeRequested;

        public event Action<CombatActionMode> OnCombatModeSelected;
        public event Action OnUndoMoveRequested;
        public event Action OnGuardRequested;
        public event Action OnEndTurnRequested;
        public event Action OnCycleSpeedRequested;
        public event Action<int> OnSpellSelected;
        public event Action<string> OnCombatPartyAllyClicked;
        public event Action<string, string, string> OnHotbarSlotHovered;
        public event Action OnHotbarSlotHoverExited;

        public event Action<bool> OnAutoCombatToggled;
        public event Action<bool> OnAutoEndTurnToggled;
        public event Action<int> OnBattleSpeedSelected;
        public event Action<float> OnMasterVolumeChanged;
        public event Action<float> OnMusicVolumeChanged;
        public event Action<float> OnSfxVolumeChanged;
        public event Action<bool> OnMuteToggled;
        public event Action<float> OnGridOpacityChanged;
        public event Action OnCombatGridToggleRequested;
        public event Action<bool> OnNameplatesAlwaysOnChanged;
        public event Action<bool> OnDevToolsToggled;
        public event Action OnFleeCombatRequested;
        public event Action OnQuitRequested;

        public event Action OnQuickResolveFightRequested;
        public event Action OnQuickResolveAutoWinRequested;

        public event Action OnContinueAfterVictoryRequested;
        public event Action OnRetrySkirmishRequested;
        public event Action OnReviveAtShrineRequested;

        public bool IsPartyOpen => _partyDrawerRoot != null && _partyDrawerRoot.activeSelf;
        public bool IsShopOpen => _shopModalRoot != null && _shopModalRoot.activeSelf;
        public bool IsTempleOpen => _templeModalRoot != null && _templeModalRoot.activeSelf;
        public bool IsOutcomeOpen => _outcomeModalRoot != null && _outcomeModalRoot.activeSelf;
        public bool IsSettingsOpen => _settingsModalRoot != null && _settingsModalRoot.activeSelf;
        public bool IsSettingsModalOpen => IsSettingsOpen;
        public bool IsQuickResolveOpen => _quickResolveModalRoot != null && _quickResolveModalRoot.activeSelf;
        public bool IsModalOpen => IsPartyOpen || IsShopOpen || IsTempleOpen || IsOutcomeOpen || IsSettingsOpen || IsQuickResolveOpen || IsLevelUpModalOpen || IsEncounterInterceptOpen;
        public bool IsAnyModalOpen => IsModalOpen;
        public bool IsDevToolsVisible => _isDevToolsVisible;
        public bool IsCursorTooltipActive => _cursorTooltipRoot != null && _cursorTooltipRoot.activeSelf;
        public bool IsLogDrawerExpanded => _combatLogDrawerRoot != null && _combatLogDrawerRoot.activeSelf;
        public bool IsAutoCombatVisualOn => _isAutoCombatOn;
        public bool AreExploreButtonsHiddenInCombat =>
            _isCombatHudActive &&
            (_skirmishBtn == null || !_skirmishBtn.gameObject.activeSelf) &&
            (_partyBtn == null || !_partyBtn.gameObject.activeSelf) &&
            (_shopBtn == null || !_shopBtn.gameObject.activeSelf) &&
            (_campBtn == null || !_campBtn.gameObject.activeSelf) &&
            (_shrineBtn == null || !_shrineBtn.gameObject.activeSelf) &&
            (_warpBtn == null || !_warpBtn.gameObject.activeSelf) &&
            (_contextPoiBtn == null || !_contextPoiBtn.gameObject.activeSelf) &&
            (_saveBtn == null || !_saveBtn.gameObject.activeSelf) &&
            (_loadBtn == null || !_loadBtn.gameObject.activeSelf) &&
            (_topGoldText == null || !_topGoldText.gameObject.activeSelf) &&
            (_topTorchText == null || !_topTorchText.gameObject.activeSelf) &&
            (_topClockPill == null || !_topClockPill.activeSelf) &&
            (_topThreatPill == null || !_topThreatPill.activeSelf);
        public bool IsRightSidebarRemoved =>
            _uiBuilt &&
            _combatRoot != null &&
            _combatRoot.transform.Find("CombatTooltipCard") == null &&
            _combatRoot.transform.Find("CombatLogPanel") == null;
        public string ActiveShopId => _activeShopId;
        public int PooledLogRowCount => _combatLogRows.Count;
        public int PooledStashRowCount => _stashRowPool.Count;
        public int PooledShopBuyRowCount => _shopBuyRowPool.Count;
        public int PooledShopSellRowCount => _shopSellRowPool.Count;
        public int PooledShopRowCount => _shopBuyRowPool.Count + _shopSellRowPool.Count;
        public int PooledSpellRowCount => _spellRowPool.Count;

        public IReadOnlyList<string> GetVisibleHotbarActionIds()
        {
            return _visibleHotbarActionIds;
        }

        private void Update()
        {
            if (_toastRoot != null && _toastRoot.activeSelf)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastTimer <= 0f)
                {
                    _toastRoot.SetActive(false);
                }
                else if (_toastTimer < _toastFadeDuration && _toastCanvasGroup != null)
                {
                    _toastCanvasGroup.alpha = Mathf.Clamp01(_toastTimer / _toastFadeDuration);
                }
            }

            if (_isCombatHudActive && _pulseEndTurn && _combatEndTurnBtn != null && _combatEndTurnBtn.interactable)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.2f);
                Image btnImg = _combatEndTurnBtn.GetComponent<Image>();
                if (btnImg != null)
                {
                    btnImg.color = Color.Lerp(BywaterUiHelpers.ButtonActiveBg, new Color(0.55f, 0.41f, 0.12f, 0.98f), pulse);
                }
                BywaterUiHelpers.SetBorderColor(
                    _combatEndTurnBtn.transform,
                    Color.Lerp(BywaterUiHelpers.BrassAccent, BywaterUiHelpers.BrassHighlight, pulse));
            }

            if (_cursorTooltipRoot != null && _cursorTooltipRoot.activeSelf && !_hasManualTooltipPos && Mouse.current != null)
            {
                UpdateCursorTooltipPosition(Mouse.current.position.ReadValue());
            }
        }

        public bool IsPointerOverUi()
        {
            EventSystem es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        public void BuildUi(BywaterAssetCatalog catalog)
        {
            if (_uiBuilt)
            {
                return;
            }

            _uiBuilt = true;
            _catalog = catalog;
            _font = BywaterUiHelpers.ResolveFont(catalog != null ? catalog.uiFont : null);

            BywaterUiHelpers.EnsureEventSystemWithInputSystem();

            GameObject canvasGo = new GameObject("BywaterHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            _canvasRect = canvasGo.GetComponent<RectTransform>();

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            BuildTopHudBar(_canvas.transform);
            BuildExploreHud(_canvas.transform);
            BuildCombatHud(_canvas.transform);
            BuildPartyDrawer(_canvas.transform);
            BuildShopModal(_canvas.transform);
            BuildTempleModal(_canvas.transform);
            BuildSettingsModal(_canvas.transform);
            BuildQuickResolveModal(_canvas.transform);
            BuildEncounterInterceptModal(_canvas.transform);
            BuildOutcomeModal(_canvas.transform);
            BuildLevelUpModal(_canvas.transform);
            BuildToastBanner(_canvas.transform);

            SetCombatHudActive(false);
            _partyDrawerRoot.SetActive(false);
            _shopModalRoot.SetActive(false);
            _templeModalRoot.SetActive(false);
            _settingsModalRoot.SetActive(false);
            _quickResolveModalRoot.SetActive(false);
            if (_encounterInterceptModalRoot != null) _encounterInterceptModalRoot.SetActive(false);
            _outcomeModalRoot.SetActive(false);
            _toastRoot.SetActive(false);
        }

        public void ShowToast(string message, float duration = 2.8f)
        {
            if (!_uiBuilt || _toastRoot == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            _toastText.text = message;
            _toastTimer = Mathf.Max(0.8f, duration);
            if (_toastCanvasGroup != null)
            {
                _toastCanvasGroup.alpha = 1f;
            }

            _toastRoot.SetActive(true);
            _toastRoot.transform.SetAsLastSibling();
        }

        public void HideToast()
        {
            _toastTimer = 0f;
            if (_toastRoot != null)
            {
                _toastRoot.SetActive(false);
            }
        }

        public bool IsTargetingBannerVisible => _targetingBannerRoot != null && _targetingBannerRoot.activeSelf;
        public string TargetingBannerText => _targetingBannerText != null ? _targetingBannerText.text : string.Empty;

        public void SetDevToolsVisible(bool visible)
        {
            _isDevToolsVisible = visible;
            ApplyTopBarVisibility();
            RefreshSettingsModalControls();
        }

        public void SetContextualExplorePoi(string label, Action onClick = null)
        {
            _contextPoiLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            _contextPoiAction = onClick;
            if (_contextPoiBtnLabel != null)
            {
                _contextPoiBtnLabel.text = _contextPoiLabel ?? string.Empty;
            }
            ApplyTopBarVisibility();
        }

        public void SetAutoCombatVisual(bool enabled)
        {
            _isAutoCombatOn = enabled;
            if (_autoCombatBtnLabel != null)
            {
                _autoCombatBtnLabel.text = enabled ? "[A] Auto ON" : "[A] Auto";
            }
            if (_autoCombatBtn != null)
            {
                BywaterUiHelpers.SetButtonColors(_autoCombatBtn, enabled);
            }
            RefreshSettingsModalControls();
        }

        public void ToggleCombatLogDrawer(bool? forceExpanded = null)
        {
            if (!_uiBuilt || _combatLogDrawerRoot == null)
            {
                return;
            }

            bool nextState = forceExpanded ?? !_combatLogDrawerRoot.activeSelf;
            _combatLogDrawerRoot.SetActive(nextState);
            if (_tickerToggleBadge != null)
            {
                _tickerToggleBadge.text = nextState ? "[-]" : "[+]";
            }
            if (nextState)
            {
                _combatLogDrawerRoot.transform.SetAsLastSibling();
                if (_combatLogScroll != null)
                {
                    Canvas.ForceUpdateCanvases();
                    _combatLogScroll.verticalNormalizedPosition = 0f;
                }
            }
        }

        public void ToggleSettingsModal(bool? forceOpen = null)
        {
            if (!_uiBuilt || _settingsModalRoot == null)
            {
                return;
            }

            bool nextState = forceOpen ?? !_settingsModalRoot.activeSelf;
            if (nextState)
            {
                if (_partyDrawerRoot != null) _partyDrawerRoot.SetActive(false);
                if (_shopModalRoot != null) _shopModalRoot.SetActive(false);
                if (_templeModalRoot != null) _templeModalRoot.SetActive(false);
                if (_quickResolveModalRoot != null) _quickResolveModalRoot.SetActive(false);
                if (_encounterInterceptModalRoot != null) _encounterInterceptModalRoot.SetActive(false);
                _quitConfirmArmed = false;
                RefreshSettingsModalControls();
                _settingsModalRoot.SetActive(true);
                _settingsModalRoot.transform.SetAsLastSibling();
            }
            else
            {
                _quitConfirmArmed = false;
                RefreshSettingsModalControls();
                _settingsModalRoot.SetActive(false);
            }
        }

        public void OpenSettingsModal()
        {
            ToggleSettingsModal(true);
        }

        public void CloseSettingsModal()
        {
            _quitConfirmArmed = false;
            ToggleSettingsModal(false);
        }

        public void SyncSettingsState(
            bool autoCombat,
            bool autoEndTurn,
            int battleSpeedIndex,
            float masterVol,
            float musicVol,
            float sfxVol,
            bool muted,
            float gridOpacity,
            bool nameplatesAlwaysOn,
            bool devToolsVisible)
        {
            _isAutoCombatOn = autoCombat;
            _isAutoEndTurnOn = autoEndTurn;
            _battleSpeedIndex = Mathf.Clamp(battleSpeedIndex, 0, 2);
            _isMuted = muted;
            _nameplatesAlwaysOn = nameplatesAlwaysOn;
            _isDevToolsVisible = devToolsVisible;

            _suppressSettingsCallbacks = true;
            try
            {
                if (_settingsMasterSlider != null) _settingsMasterSlider.SetValueWithoutNotify(Mathf.Clamp01(masterVol));
                if (_settingsMusicSlider != null) _settingsMusicSlider.SetValueWithoutNotify(Mathf.Clamp01(musicVol));
                if (_settingsSfxSlider != null) _settingsSfxSlider.SetValueWithoutNotify(Mathf.Clamp01(sfxVol));
                if (_settingsGridOpacitySlider != null) _settingsGridOpacitySlider.SetValueWithoutNotify(Mathf.Clamp01(gridOpacity));
            }
            finally
            {
                _suppressSettingsCallbacks = false;
            }

            SetAutoCombatVisual(autoCombat);
            ApplyTopBarVisibility();
            RefreshSettingsModalControls();
        }

        public void ShowQuickResolveModal(string encounterTitle, string summaryText)
        {
            if (!_uiBuilt || _quickResolveModalRoot == null)
            {
                return;
            }

            if (_partyDrawerRoot != null) _partyDrawerRoot.SetActive(false);
            if (_shopModalRoot != null) _shopModalRoot.SetActive(false);
            if (_templeModalRoot != null) _templeModalRoot.SetActive(false);
            if (_settingsModalRoot != null) _settingsModalRoot.SetActive(false);
            if (_encounterInterceptModalRoot != null) _encounterInterceptModalRoot.SetActive(false);

            _quickResolveTitle.text = string.IsNullOrWhiteSpace(encounterTitle) ? "Encounter Ahead" : encounterTitle;
            _quickResolveSummary.text = string.IsNullOrWhiteSpace(summaryText)
                ? "Hostiles block the path. Command the battle tactically or let the vanguard resolve it quickly."
                : summaryText;

            _quickResolveModalRoot.SetActive(true);
            _quickResolveModalRoot.transform.SetAsLastSibling();
        }

        public void HideQuickResolveModal()
        {
            if (_quickResolveModalRoot != null)
            {
                _quickResolveModalRoot.SetActive(false);
            }
        }

        public bool TriggerHotbarSlot(int oneBasedIndex)
        {
            int idx = oneBasedIndex - 1;
            if (idx < 0 || idx >= _hotbarSlots.Count)
            {
                return false;
            }

            HotbarSlotRefs slot = _hotbarSlots[idx];
            if (!slot.Root.activeSelf || !slot.Button.interactable)
            {
                return false;
            }

            slot.Button.onClick.Invoke();
            return true;
        }

        public void TogglePartyPanel(bool? forceOpen = null)
        {
            if (!_uiBuilt || _partyDrawerRoot == null)
            {
                return;
            }

            bool nextState = forceOpen ?? !_partyDrawerRoot.activeSelf;
            if (nextState)
            {
                if (_shopModalRoot != null) _shopModalRoot.SetActive(false);
                if (_templeModalRoot != null) _templeModalRoot.SetActive(false);
                if (_settingsModalRoot != null) _settingsModalRoot.SetActive(false);
                if (_encounterInterceptModalRoot != null) _encounterInterceptModalRoot.SetActive(false);
            }

            _partyDrawerRoot.SetActive(nextState);
            if (nextState)
            {
                _partyDrawerRoot.transform.SetAsLastSibling();
                if (_cachedParty != null && _cachedBundle != null)
                {
                    RefreshPartyDrawer(_cachedParty, _cachedBundle, _selectedHeroIndex);
                }
            }
        }

        public void CloseAllModals()
        {
            if (_partyDrawerRoot != null) _partyDrawerRoot.SetActive(false);
            if (_shopModalRoot != null) _shopModalRoot.SetActive(false);
            if (_templeModalRoot != null) _templeModalRoot.SetActive(false);
            if (_settingsModalRoot != null) _settingsModalRoot.SetActive(false);
            if (_quickResolveModalRoot != null) _quickResolveModalRoot.SetActive(false);
            if (_encounterInterceptModalRoot != null) _encounterInterceptModalRoot.SetActive(false);
            if (_outcomeModalRoot != null) _outcomeModalRoot.SetActive(false);
            if (_levelUpModalRoot != null) _levelUpModalRoot.SetActive(false);
            _quitConfirmArmed = false;
        }

        public void OpenShop(string shopId, PartyState party, BywaterM1Bundle bundle)
        {
            if (!_uiBuilt)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(shopId))
            {
                _activeShopId = shopId;
            }

            _cachedParty = party;
            _cachedBundle = bundle;

            if (_partyDrawerRoot != null)
            {
                _partyDrawerRoot.SetActive(false);
            }
            if (_templeModalRoot != null)
            {
                _templeModalRoot.SetActive(false);
            }
            if (_settingsModalRoot != null)
            {
                _settingsModalRoot.SetActive(false);
            }
            if (_encounterInterceptModalRoot != null)
            {
                _encounterInterceptModalRoot.SetActive(false);
            }

            if (_shopAffordableToggle != null)
            {
                _lastShopAffordableOnly = false;
                _shopAffordableToggle.SetIsOnWithoutNotify(false);
            }

            _shopModalRoot.SetActive(true);
            _shopModalRoot.transform.SetAsLastSibling();
            RefreshShopModal(party, bundle);
        }

        public void CloseShopModal()
        {
            if (_shopModalRoot != null)
            {
                _shopModalRoot.SetActive(false);
            }
        }

        public void OpenTempleModal(PartyState party, BywaterM1Bundle bundle)
        {
            if (!_uiBuilt || _templeModalRoot == null)
            {
                return;
            }

            _cachedParty = party;
            _cachedBundle = bundle;

            if (_partyDrawerRoot != null)
            {
                _partyDrawerRoot.SetActive(false);
            }
            if (_shopModalRoot != null)
            {
                _shopModalRoot.SetActive(false);
            }
            if (_settingsModalRoot != null)
            {
                _settingsModalRoot.SetActive(false);
            }
            if (_quickResolveModalRoot != null)
            {
                _quickResolveModalRoot.SetActive(false);
            }
            if (_encounterInterceptModalRoot != null)
            {
                _encounterInterceptModalRoot.SetActive(false);
            }

            _templeModalRoot.SetActive(true);
            _templeModalRoot.transform.SetAsLastSibling();
            RefreshTempleModal(party, bundle);
        }

        public void CloseTempleModal()
        {
            if (_templeModalRoot != null)
            {
                _templeModalRoot.SetActive(false);
            }
        }

        public void RefreshTempleModal(PartyState party, BywaterM1Bundle bundle)
        {
            if (!_uiBuilt || _templeModalRoot == null)
            {
                return;
            }

            if (party != null)
            {
                _cachedParty = party;
            }
            if (bundle != null)
            {
                _cachedBundle = bundle;
            }

            if (_templePortraitImage != null)
            {
                ApplyNpcPortrait(_templePortraitImage, "npc_priestess", "heal");
            }

            if (party == null)
            {
                if (_templeGoldLabel != null) _templeGoldLabel.text = "Party Gold: 0g";
                if (_templeStatusLabel != null)
                {
                    _templeStatusLabel.text = "Party Status: Ready for shrine blessings.";
                    _templeStatusLabel.color = BywaterUiHelpers.ParchmentText;
                }
                return;
            }

            if (_templeGoldLabel != null)
            {
                _templeGoldLabel.text = $"Party Gold: {party.Gold}g";
            }

            int injuredCount = 0;
            int fallenCount = 0;
            int afflictedCount = 0;
            int lowChargeCount = 0;

            for (int i = 0; i < party.Members.Count; i++)
            {
                PartyMemberState m = party.Members[i];
                if (m == null) continue;
                if (m.Hp <= 0) fallenCount++;
                else if (m.Hp < m.MaxHp) injuredCount++;
                if (m.CurseStacks > 0 || m.ExhaustionStacks > 0 || m.Conditions.Count > 0) afflictedCount++;

                if (bundle != null && bundle.Items != null)
                {
                    foreach (InventoryItem eq in m.Equipped.Values)
                    {
                        if (eq != null && bundle.Items.TryGetValue(eq.ItemId, out ItemDefinition def) &&
                            def.Charges > 0 && eq.RemainingCharges < def.Charges)
                        {
                            lowChargeCount++;
                        }
                    }
                }
            }

            if (bundle != null && bundle.Items != null)
            {
                for (int i = 0; i < party.Stash.Count; i++)
                {
                    InventoryItem st = party.Stash[i];
                    if (st != null && bundle.Items.TryGetValue(st.ItemId, out ItemDefinition def) &&
                        def.Charges > 0 && st.RemainingCharges < def.Charges)
                    {
                        lowChargeCount++;
                    }
                }
            }

            if (_templeStatusLabel != null)
            {
                if (fallenCount == 0 && injuredCount == 0 && afflictedCount == 0 && lowChargeCount == 0)
                {
                    _templeStatusLabel.text = "Party Status: All heroes healthy, uncursed, and relics fully charged.";
                    _templeStatusLabel.color = BywaterUiHelpers.SuccessGreen;
                }
                else
                {
                    List<string> summaryParts = new List<string>(4);
                    if (fallenCount > 0) summaryParts.Add($"{fallenCount} fallen");
                    if (injuredCount > 0) summaryParts.Add($"{injuredCount} injured");
                    if (afflictedCount > 0) summaryParts.Add($"{afflictedCount} cursed/ailing");
                    if (lowChargeCount > 0) summaryParts.Add($"{lowChargeCount} low-charge relic(s)");
                    _templeStatusLabel.text = "Party Status: " + string.Join(" | ", summaryParts);
                    _templeStatusLabel.color = BywaterUiHelpers.BrassHighlight;
                }
            }
        }

        public void RefreshExploreHud(
            ExplorationSession session,
            BywaterM1Bundle bundle,
            GridCoord? hoveredCoord,
            BywaterTileKind? hoveredKind,
            int hoverSteps,
            string poiHint,
            bool isReachable)
        {
            RefreshExploreHudInternal(session, bundle, hoveredCoord, hoveredKind, hoverSteps, poiHint, isReachable, null, updateContextPoi: false);
        }

        public void RefreshExploreHud(
            ExplorationSession session,
            BywaterM1Bundle bundle,
            GridCoord? hoveredCoord,
            BywaterTileKind? hoveredKind,
            int hoverSteps,
            string poiHint,
            bool isReachable,
            string contextPoiLabel)
        {
            RefreshExploreHudInternal(session, bundle, hoveredCoord, hoveredKind, hoverSteps, poiHint, isReachable, contextPoiLabel, updateContextPoi: true);
        }

        private void RefreshExploreHudInternal(
            ExplorationSession session,
            BywaterM1Bundle bundle,
            GridCoord? hoveredCoord,
            BywaterTileKind? hoveredKind,
            int hoverSteps,
            string poiHint,
            bool isReachable,
            string contextPoiLabel,
            bool updateContextPoi)
        {
            if (!_uiBuilt || session == null)
            {
                return;
            }

            _cachedParty = session.Party;
            _cachedBundle = bundle;
            _cachedSafeZone = session.IsInSafeZone();
            if (updateContextPoi)
            {
                _contextPoiLabel = string.IsNullOrWhiteSpace(contextPoiLabel) ? null : contextPoiLabel.Trim();
                if (_contextPoiBtnLabel != null)
                {
                    _contextPoiBtnLabel.text = _contextPoiLabel ?? string.Empty;
                }
            }
            SetCombatHudActive(false);

            string mapTitle = session.CurrentMap.IsDungeon ? "Bywater Cavern (Dungeon 0)" : "City of Bywater";
            if (_lastExploreX != session.Position.X || _lastExploreY != session.Position.Y || _lastExploreIsDungeon != session.CurrentMap.IsDungeon)
            {
                _lastExploreX = session.Position.X;
                _lastExploreY = session.Position.Y;
                _lastExploreIsDungeon = session.CurrentMap.IsDungeon;
                _topMapText.text = $"{mapTitle} ({session.Position.X}, {session.Position.Y})";
                if (_warpBtnLabel != null)
                {
                    _warpBtnLabel.text = session.CurrentMap.IsDungeon ? "Exit Cavern" : "Warp Portal";
                }
            }

            if (_lastExploreGold != session.Party.Gold)
            {
                _lastExploreGold = session.Party.Gold;
                _topGoldText.text = $"Gold: {session.Party.Gold}g";
            }

            if (_lastExploreTorch != session.Party.TorchTurnsRemaining)
            {
                _lastExploreTorch = session.Party.TorchTurnsRemaining;
                _topTorchText.text = $"Torch: {session.Party.TorchTurnsRemaining}t";
            }

            if (_lastExploreTurn != session.TurnCount)
            {
                _lastExploreTurn = session.TurnCount;
                _topTurnText.text = $"Turn: {session.TurnCount}";
            }

            RefreshEncounterDirectorExploreHud(session);

            // Update 6 party strip cards (only touch TMP/RectTransform when stats/selection changed)
            IReadOnlyList<PartyMemberState> members = session.Party.Members;
            bool partyDrawerOpen = IsPartyOpen;
            for (int i = 0; i < _heroCards.Count; i++)
            {
                HeroCardRefs card = _heroCards[i];
                if (i >= members.Count)
                {
                    if (!card.HasCachedState || card.LastVisible)
                    {
                        card.Root.gameObject.SetActive(false);
                        card.LastVisible = false;
                        card.HasCachedState = true;
                    }
                    continue;
                }

                PartyMemberState m = members[i];
                bool selected = partyDrawerOpen && i == _selectedHeroIndex;
                int condCount = m.Conditions.Count;

                if (card.HasCachedState &&
                    card.LastVisible &&
                    string.Equals(card.LastMemberId, m.Id, StringComparison.Ordinal) &&
                    card.LastLevel == m.Level &&
                    card.LastXp == m.Xp &&
                    card.LastPendingLevels == m.PendingLevelUps &&
                    card.LastHp == m.Hp &&
                    card.LastMaxHp == m.MaxHp &&
                    card.LastSp == m.Sp &&
                    card.LastMaxSp == m.MaxSp &&
                    card.LastCondCount == condCount &&
                    card.LastSelected == selected)
                {
                    continue;
                }

                if (!card.HasCachedState || !card.LastVisible)
                {
                    card.Root.gameObject.SetActive(true);
                }

                card.LastVisible = true;
                card.LastMemberId = m.Id;
                card.LastLevel = m.Level;
                card.LastXp = m.Xp;
                card.LastPendingLevels = m.PendingLevelUps;
                card.LastHp = m.Hp;
                card.LastMaxHp = m.MaxHp;
                card.LastSp = m.Sp;
                card.LastMaxSp = m.MaxSp;
                card.LastCondCount = condCount;
                card.LastSelected = selected;
                card.HasCachedState = true;

                card.Portrait.sprite = ResolveHeroPortrait(m.Id, m.PortraitId, m.Archetype);
                card.Portrait.preserveAspect = true;
                card.Portrait.color = card.Portrait.sprite != null ? Color.white : new Color(0.2f, 0.25f, 0.35f, 1f);
                RefreshConditionBadges(card.ConditionBadges, m.Conditions, isGuarding: false, onFireSurface: false);
                card.NameText.text = m.Name;
                card.ClassText.text = $"{m.Archetype} Lv{m.Level}";

                float hpRatio = Mathf.Clamp01(m.Hp / (float)Mathf.Max(1, m.MaxHp));
                card.HpFill.rectTransform.anchorMax = new Vector2(hpRatio, 1f);
                card.HpFill.color = hpRatio > 0.35f ? BywaterUiHelpers.HpFillColor : BywaterUiHelpers.HpLowColor;
                card.HpText.text = $"HP {m.Hp}/{m.MaxHp}";

                if (m.MaxSp > 0)
                {
                    float spRatio = Mathf.Clamp01(m.Sp / (float)m.MaxSp);
                    card.SpFill.rectTransform.anchorMax = new Vector2(spRatio, 1f);
                    card.SpText.text = $"SP {m.Sp}/{m.MaxSp}";
                }
                else
                {
                    card.SpFill.rectTransform.anchorMax = new Vector2(0f, 1f);
                    card.SpText.text = "SP -";
                }

                if (card.XpFill != null)
                {
                    int curFloor = PartyMemberState.GetXpRequiredForLevel(m.Level);
                    int span = Math.Max(1, m.XpForNextLevel - curFloor);
                    float xpRatio = m.Level >= PartyMemberState.MaxLevel ? 1f : Mathf.Clamp01((m.Xp - curFloor) / (float)span);
                    card.XpFill.rectTransform.anchorMax = new Vector2(xpRatio, 1f);
                }

                if (card.LvlUpBadgeBtn != null)
                {
                    card.LvlUpBadgeBtn.gameObject.SetActive(m.HasPendingLevelUp);
                }

                card.Background.color = selected ? BywaterUiHelpers.CardSelectedBg : BywaterUiHelpers.CardBg;
                BywaterUiHelpers.SetBorderColor(card.Root, selected ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.BrassAccent);
            }

            // Hover footer chip
            bool hasHover = hoveredCoord.HasValue && hoveredKind.HasValue;
            GridCoord hCoord = hasHover ? hoveredCoord.Value : default;
            BywaterTileKind hKind = hasHover ? hoveredKind.Value : BywaterTileKind.Grass;
            string safePoi = poiHint ?? string.Empty;

            if (_hasLastExploreHover == hasHover &&
                (!hasHover ||
                 (hCoord == _lastExploreHoverCoord &&
                  hKind == _lastExploreHoverKind &&
                  hoverSteps == _lastExploreHoverSteps &&
                  isReachable == _lastExploreHoverReachable &&
                  string.Equals(safePoi, _lastExploreHoverPoi, StringComparison.Ordinal))))
            {
                return;
            }

            _hasLastExploreHover = hasHover;
            _lastExploreHoverCoord = hCoord;
            _lastExploreHoverKind = hKind;
            _lastExploreHoverSteps = hoverSteps;
            _lastExploreHoverPoi = safePoi;
            _lastExploreHoverReachable = isReachable;

            if (hasHover)
            {
                string terrainLabel = FormatTileKind(hKind);
                _hoverCoordText.text = $"Tile ({hCoord.X}, {hCoord.Y}) - {terrainLabel}";
                if (!string.IsNullOrEmpty(safePoi))
                {
                    _hoverDetailText.text = isReachable
                        ? $"{safePoi} | {hoverSteps} step(s) (Click to move)"
                        : $"{safePoi} | Blocked";
                    _hoverDetailText.color = isReachable ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.DangerRed;
                }
                else
                {
                    _hoverDetailText.text = isReachable
                        ? (hoverSteps > 0 ? $"A* Path: {hoverSteps} step(s) - Click to move" : "Party Position")
                        : "Impassable terrain";
                    _hoverDetailText.color = isReachable ? BywaterUiHelpers.MutedSlate : BywaterUiHelpers.DangerRed;
                }
            }
            else
            {
                _hoverCoordText.text = "Hover map to inspect terrain";
                _hoverDetailText.text = "Left-Click: Walk | WASD/Arrows: Move | Right-Drag: Pan | Scroll: Zoom";
                _hoverDetailText.color = BywaterUiHelpers.MutedSlate;
            }
        }

        public void RefreshPartyDrawer(PartyState party, BywaterM1Bundle bundle, int selectedHeroIndex)
        {
            if (!_uiBuilt || party == null)
            {
                return;
            }

            _cachedParty = party;
            _cachedBundle = bundle;

            if (party.Members.Count == 0)
            {
                return;
            }

            _selectedHeroIndex = Mathf.Clamp(selectedHeroIndex, 0, party.Members.Count - 1);
            _drawerGoldTorchLabel.text = $"Gold: {party.Gold}g  |  Torch: {party.TorchTurnsRemaining}t";

            for (int i = 0; i < _partyHeroSelectorBtns.Count; i++)
            {
                Button btn = _partyHeroSelectorBtns[i];
                if (i >= party.Members.Count)
                {
                    btn.gameObject.SetActive(false);
                    continue;
                }

                btn.gameObject.SetActive(true);
                TextMeshProUGUI lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null)
                {
                    lbl.text = party.Members[i].Name;
                }
                BywaterUiHelpers.SetButtonColors(btn, i == _selectedHeroIndex);
            }

            PartyMemberState hero = party.Members[_selectedHeroIndex];
            _drawerHeroPortrait.sprite = ResolveHeroPortrait(hero.Id, hero.PortraitId, hero.Archetype);
            _drawerHeroPortrait.preserveAspect = true;
            _drawerHeroPortrait.color = _drawerHeroPortrait.sprite != null ? Color.white : new Color(0.2f, 0.25f, 0.35f, 1f);
            _drawerHeroTitle.text = $"{hero.Name} - Lv{hero.Level} {hero.RaceName} {hero.Archetype} ({hero.CasteName})";

            IReadOnlyDictionary<int, ItemDefinition> items = bundle != null ? bundle.Items : null;
            int effAc = hero.GetEffectiveAc(items);
            int effToHit = hero.GetEffectiveToHit(items);
            int effMove = hero.GetEffectiveMovementMax(items);
            string effDice = hero.GetEffectiveDamageDice(items);

            _drawerHeroStats.text =
                $"HP: {hero.Hp}/{hero.MaxHp}   SP: {hero.Sp}/{hero.MaxSp}   AC: {effAc}   ToHit: +{effToHit}   Move: {effMove}   Weapon: {effDice}\n" +
                $"Brawn {hero.Brawn}  |  Knowledge {hero.Knowledge}  |  Judgment {hero.Judgment}  |  Agility {hero.Agility}  |  Vitality {hero.Vitality}  |  Luck {hero.Luck}";

            IReadOnlyList<ConditionDisplayEffect> conds = hero.Conditions.GetDisplayEffects();
            if (conds.Count == 0 && hero.ExhaustionStacks == 0 && hero.CurseStacks == 0)
            {
                _drawerHeroConditions.text = "Conditions: Healthy";
            }
            else
            {
                List<string> condNames = new List<string>();
                for (int i = 0; i < conds.Count; i++)
                {
                    condNames.Add(conds[i].DisplayName);
                }
                if (hero.ExhaustionStacks > 0) condNames.Add($"Exhaustion x{hero.ExhaustionStacks}");
                if (hero.CurseStacks > 0) condNames.Add($"Curse x{hero.CurseStacks}");
                _drawerHeroConditions.text = "Conditions: " + string.Join(", ", condNames);
            }

            if (hero.KnownSpellIds.Count == 0 || bundle == null)
            {
                _drawerHeroSpells.text = "Known Spells: None";
            }
            else
            {
                List<string> spellNames = new List<string>();
                for (int i = 0; i < hero.KnownSpellIds.Count; i++)
                {
                    if (bundle.Spells.TryGetValue(hero.KnownSpellIds[i], out SpellDefinition spell))
                    {
                        spellNames.Add($"{spell.Name} ({spell.SpCost}sp)");
                    }
                }
                _drawerHeroSpells.text = "Known Spells: " + (spellNames.Count > 0 ? string.Join(", ", spellNames) : "None");
            }

            // Legacy 5-row compat loop (empty in new Paper-Doll mode) & 9-slot Paper-Doll + Character Sheet
            for (int i = 0; i < _equipSlots.Count; i++)
            {
                EquipSlotRefs slotRef = _equipSlots[i];
                InventoryItem equipped = hero.GetEquipped(slotRef.Slot);
                if (equipped != null && items != null && items.TryGetValue(equipped.ItemId, out ItemDefinition itemDef))
                {
                    string bonus = FormatItemStats(itemDef);
                    slotRef.ItemLabel.text = string.IsNullOrEmpty(bonus) ? itemDef.Name : $"{itemDef.Name} ({bonus})";
                    slotRef.ItemLabel.color = BywaterUiHelpers.ParchmentText;
                    slotRef.UnequipBtn.interactable = true;
                    Sprite eqSprite = _catalog != null ? _catalog.GetItemIcon(itemDef.Id, itemDef.Name) : null;
                    if (slotRef.ItemIcon != null)
                    {
                        slotRef.ItemIcon.sprite = eqSprite;
                        slotRef.ItemIcon.preserveAspect = true;
                        slotRef.ItemIcon.gameObject.SetActive(eqSprite != null);
                    }
                }
                else
                {
                    slotRef.ItemLabel.text = "- Empty -";
                    slotRef.ItemLabel.color = BywaterUiHelpers.MutedSlate;
                    slotRef.UnequipBtn.interactable = false;
                    if (slotRef.ItemIcon != null)
                    {
                        slotRef.ItemIcon.sprite = null;
                        slotRef.ItemIcon.gameObject.SetActive(false);
                    }
                }
            }

            RefreshUxOverhaulDrawer(party, bundle, hero, _cachedSafeZone);

            // Filtered & sorted Shared Stash rows (pooled)
            List<int> order = BuildFilteredSortedStashIndices(party, items, hero);
            if (_stashEmptyLabel != null)
            {
                _stashEmptyLabel.gameObject.SetActive(order.Count == 0);
            }

            for (int rowIdx = 0; rowIdx < order.Count; rowIdx++)
            {
                int i = order[rowIdx];
                InventoryItem entry = party.Stash[i];
                ItemDefinition def = items != null && items.TryGetValue(entry.ItemId, out ItemDefinition found) ? found : null;
                string itemName = def != null ? def.Name : $"Item #{entry.ItemId}";
                if (!entry.IsIdentified)
                {
                    itemName += " [Unidentified]";
                }

                string stats = def != null ? FormatItemStats(def) : string.Empty;
                int sellPrice = def != null ? TownShopService.GetSellPrice(def) : 1;
                bool canEquip = def != null && PartyMemberState.TryParseSlot(def.Slot, out _);
                bool canUse = def != null && (!string.IsNullOrWhiteSpace(def.HealDice) ||
                    string.Equals(def.Category, "Potion", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(def.Slot, "Consumable", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(def.Name, "Torch", StringComparison.OrdinalIgnoreCase) ||
                    entry.ItemId == PartyState.TorchItemId);
                bool isUpgrade = def != null && IsStashEntryUpgradeForHero(hero, def, items);

                string chgTag = entry.RemainingCharges > 0 ? $" ({entry.RemainingCharges} chg)" : string.Empty;
                StashRowRefs rowRef = GetOrCreateStashRow(rowIdx);
                rowRef.BoundStashIndex = i;
                Sprite stashSprite = _catalog != null
                    ? _catalog.GetItemIcon(def != null ? def.Id : entry.ItemId, def != null ? def.Name : itemName)
                    : null;
                if (rowRef.ItemIcon != null)
                {
                    rowRef.ItemIcon.sprite = stashSprite;
                    rowRef.ItemIcon.preserveAspect = true;
                    rowRef.ItemIcon.gameObject.SetActive(stashSprite != null);
                }
                if (rowRef.UpgradePill != null)
                {
                    rowRef.UpgradePill.SetActive(isUpgrade);
                }
                rowRef.InfoText.text = $"{itemName} x{entry.Quantity}{chgTag} [{sellPrice}g]{(string.IsNullOrEmpty(stats) ? "" : " - " + stats)}";
                rowRef.EquipBtn.gameObject.SetActive(canEquip);
                rowRef.UseBtn.gameObject.SetActive(canUse);

                ItemDefinition capturedDef = def;
                AttachTooltipCallbacks(rowRef.Root, () =>
                {
                    if (capturedDef == null) return (itemName, string.Empty);
                    string body = canEquip
                        ? BuildGearComparisonTooltip(hero, capturedDef, items)
                        : $"{stats}{(entry.RemainingCharges > 0 ? $" | {entry.RemainingCharges} charges left" : "")}\nValue: {sellPrice}g sell\n\n{capturedDef.Description}";
                    return ($"{itemName} x{entry.Quantity}", body);
                });

                rowRef.Root.SetActive(true);
            }

            for (int r = order.Count; r < _stashRowPool.Count; r++)
            {
                _stashRowPool[r].Root.SetActive(false);
            }
        }

        private StashRowRefs GetOrCreateStashRow(int index)
        {
            if (index < _stashRowPool.Count)
            {
                return _stashRowPool[index];
            }

            RectTransform row = BywaterUiHelpers.CreatePanel(
                _partyStashScroll.content,
                $"StashRow_{index}",
                BywaterUiHelpers.CardBg,
                withBrassOutline: false);
            LayoutElement rowLe = row.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 48f;

            HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            Image iconImg = BywaterUiHelpers.CreateImage(row, "Icon", null, Color.white, preserveAspect: true);
            iconImg.raycastTarget = false;
            LayoutElement iconLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 28f;
            iconLe.preferredHeight = 28f;
            iconLe.minWidth = 28f;
            iconLe.minHeight = 28f;
            iconImg.gameObject.SetActive(false);

            TextMeshProUGUI info = BywaterUiHelpers.CreateLabel(
                row,
                "Info",
                string.Empty,
                _font,
                15f,
                BywaterUiHelpers.ParchmentText,
                TextAlignmentOptions.MidlineLeft,
                wrap: false);
            info.overflowMode = TextOverflowModes.Truncate;
            LayoutElement infoLe = info.gameObject.AddComponent<LayoutElement>();
            infoLe.flexibleWidth = 1f;

            TextMeshProUGUI upgLbl = BywaterUiHelpers.CreateLabel(
                row,
                "UpgradePill",
                "UPG",
                _font,
                15f,
                BywaterUiHelpers.SuccessGreen,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            upgLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;
            upgLbl.gameObject.SetActive(false);

            StashRowRefs rowRef = new StashRowRefs
            {
                Root = row.gameObject,
                ItemIcon = iconImg,
                UpgradePill = upgLbl.gameObject,
                InfoText = info,
                BoundStashIndex = index
            };

            Button eqBtn = BywaterUiHelpers.CreateButton(
                row,
                "EquipBtn",
                "Equip",
                _font,
                () => OnEquipFromStashRequested?.Invoke(_selectedHeroIndex, rowRef.BoundStashIndex),
                15f);
            LayoutElement eqLe = eqBtn.gameObject.AddComponent<LayoutElement>();
            eqLe.preferredWidth = 82f;
            rowRef.EquipBtn = eqBtn;

            Button useBtn = BywaterUiHelpers.CreateButton(
                row,
                "UseBtn",
                "Use",
                _font,
                () =>
                {
                    if (_cachedParty != null && rowRef.BoundStashIndex >= 0 && rowRef.BoundStashIndex < _cachedParty.Stash.Count)
                    {
                        InventoryItem itemEntry = _cachedParty.Stash[rowRef.BoundStashIndex];
                        if (itemEntry.ItemId == PartyState.TorchItemId)
                        {
                            OnUseStashItemRequested?.Invoke(_selectedHeroIndex, rowRef.BoundStashIndex);
                        }
                        else
                        {
                            string itemLabel = _cachedBundle != null && _cachedBundle.Items.TryGetValue(itemEntry.ItemId, out ItemDefinition d)
                                ? d.Name
                                : "Potion";
                            OpenPotionPicker(rowRef.BoundStashIndex, itemLabel);
                        }
                    }
                },
                15f);
            LayoutElement useLe = useBtn.gameObject.AddComponent<LayoutElement>();
            useLe.preferredWidth = 72f;
            rowRef.UseBtn = useBtn;

            _stashRowPool.Add(rowRef);
            return rowRef;
        }

        public void RefreshShopModal(PartyState party, BywaterM1Bundle bundle)
        {
            if (!_uiBuilt || party == null || bundle == null)
            {
                return;
            }

            _cachedParty = party;
            _cachedBundle = bundle;

            if (!bundle.Shops.TryGetValue(_activeShopId, out ShopDefinition shop))
            {
                foreach (ShopDefinition first in bundle.Shops.Values)
                {
                    shop = first;
                    _activeShopId = first.ShopId;
                    break;
                }
            }

            if (shop == null)
            {
                return;
            }

            ResolveShopVenueMeta(shop, out string venueTitle, out string venueGreeting, out string npcPortraitKey, out string fallbackIconKey);
            if (_shopPortraitImage != null)
            {
                ApplyNpcPortrait(_shopPortraitImage, npcPortraitKey, fallbackIconKey);
            }
            if (_shopHeaderTitle != null)
            {
                _shopHeaderTitle.text = venueTitle;
            }
            if (_shopGreetingLabel != null)
            {
                _shopGreetingLabel.text = venueGreeting;
            }
            if (_shopGoldLabel != null)
            {
                _shopGoldLabel.text = $"Party Gold: {party.Gold}g";
            }

            foreach (KeyValuePair<string, Button> kvp in _shopCategoryBtns)
            {
                BywaterUiHelpers.SetButtonColors(kvp.Value, string.Equals(kvp.Key, _activeShopCategory, StringComparison.OrdinalIgnoreCase));
            }

            ShopFilter filter = new ShopFilter
            {
                Category = string.Equals(_activeShopCategory, "All", StringComparison.OrdinalIgnoreCase) ? null : _activeShopCategory,
                AffordableOnly = _shopAffordableToggle != null && _shopAffordableToggle.isOn,
                SearchText = _shopSearchInput != null ? _shopSearchInput.text : null
            };

            IReadOnlyList<ShopStockView> stock = TownShopService.FilterStock(shop, bundle.Items, party, filter);
            if (_shopBuyEmptyLabel != null)
            {
                _shopBuyEmptyLabel.gameObject.SetActive(stock.Count == 0);
            }

            for (int i = 0; i < stock.Count; i++)
            {
                ShopStockView entry = stock[i];
                string stats = FormatItemStats(entry.Item);
                if (string.IsNullOrEmpty(stats) && !string.IsNullOrWhiteSpace(entry.Item.Category))
                {
                    stats = entry.Item.Category;
                }
                bool isUpgrade = IsShopItemUpgrade(party, entry.Item, bundle.Items);

                ShopBuyRowRefs rowRef = GetOrCreateShopBuyRow(i);
                rowRef.BoundShopId = shop.ShopId;
                rowRef.BoundItemId = entry.Item.Id;
                Sprite buySprite = _catalog != null ? _catalog.GetItemIcon(entry.Item.Id, entry.Item.Name) : null;
                if (rowRef.ItemIcon != null)
                {
                    rowRef.ItemIcon.sprite = buySprite;
                    rowRef.ItemIcon.preserveAspect = true;
                    rowRef.ItemIcon.color = entry.CanAfford ? Color.white : new Color(0.65f, 0.65f, 0.70f, 0.85f);
                    rowRef.ItemIcon.gameObject.SetActive(buySprite != null);
                }
                rowRef.NameText.text = entry.Item.Name;
                rowRef.NameText.color = entry.CanAfford ? BywaterUiHelpers.ParchmentText : BywaterUiHelpers.MutedSlate;
                if (rowRef.StatsText != null)
                {
                    rowRef.StatsText.text = stats;
                    rowRef.StatsText.gameObject.SetActive(!string.IsNullOrEmpty(stats));
                }
                if (rowRef.UpgradeBadge != null)
                {
                    rowRef.UpgradeBadge.SetActive(isUpgrade);
                }
                rowRef.PriceText.text = $"{entry.EffectivePrice}g";
                rowRef.PriceText.color = entry.CanAfford ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.DangerRed;
                rowRef.BuyBtn.interactable = entry.CanAfford;
                rowRef.Root.SetActive(true);
            }

            for (int i = stock.Count; i < _shopBuyRowPool.Count; i++)
            {
                _shopBuyRowPool[i].Root.SetActive(false);
            }

            // Sell Stash list (pooled)
            if (_shopSellEmptyLabel != null)
            {
                _shopSellEmptyLabel.gameObject.SetActive(party.Stash.Count == 0);
            }

            for (int i = 0; i < party.Stash.Count; i++)
            {
                InventoryItem entry = party.Stash[i];
                ItemDefinition def = bundle.Items.TryGetValue(entry.ItemId, out ItemDefinition found) ? found : null;
                string itemName = def != null ? def.Name : $"Item #{entry.ItemId}";
                if (!entry.IsIdentified) itemName += " [Unid]";
                int sellPrice = def != null ? TownShopService.GetSellPrice(def) : 1;

                ShopSellRowRefs rowRef = GetOrCreateShopSellRow(i);
                rowRef.BoundStashIndex = i;
                Sprite sellSprite = _catalog != null
                    ? _catalog.GetItemIcon(def != null ? def.Id : entry.ItemId, def != null ? def.Name : itemName)
                    : null;
                if (rowRef.ItemIcon != null)
                {
                    rowRef.ItemIcon.sprite = sellSprite;
                    rowRef.ItemIcon.preserveAspect = true;
                    rowRef.ItemIcon.color = Color.white;
                    rowRef.ItemIcon.gameObject.SetActive(sellSprite != null);
                }
                rowRef.ItemText.text = $"{itemName} x{entry.Quantity}";
                rowRef.ValueText.text = $"+{sellPrice}g";
                rowRef.Root.SetActive(true);
            }

            for (int i = party.Stash.Count; i < _shopSellRowPool.Count; i++)
            {
                _shopSellRowPool[i].Root.SetActive(false);
            }
        }

        private ShopBuyRowRefs GetOrCreateShopBuyRow(int index)
        {
            if (index < _shopBuyRowPool.Count)
            {
                return _shopBuyRowPool[index];
            }

            RectTransform row = BywaterUiHelpers.CreatePanel(
                _shopBuyScroll.content,
                $"BuyRow_{index}",
                BywaterUiHelpers.CardBg,
                withBrassOutline: false);
            LayoutElement rowLe = row.gameObject.AddComponent<LayoutElement>();
            rowLe.minHeight = 54f;
            rowLe.preferredHeight = 56f;
            rowLe.flexibleHeight = 0f;

            HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            Image iconImg = BywaterUiHelpers.CreateImage(row, "Icon", null, Color.white, preserveAspect: true);
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            LayoutElement iconLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 32f;
            iconLe.preferredHeight = 32f;
            iconLe.minWidth = 32f;
            iconLe.minHeight = 32f;
            iconLe.flexibleHeight = 0f;
            iconImg.gameObject.SetActive(false);

            GameObject infoCol = new GameObject("InfoCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            infoCol.transform.SetParent(row, false);
            LayoutElement infoLe = infoCol.AddComponent<LayoutElement>();
            infoLe.flexibleWidth = 1f;
            infoLe.preferredHeight = 46f;
            infoLe.flexibleHeight = 0f;

            VerticalLayoutGroup infoVlg = infoCol.GetComponent<VerticalLayoutGroup>();
            infoVlg.spacing = 1f;
            infoVlg.childControlWidth = true;
            infoVlg.childControlHeight = true;
            infoVlg.childForceExpandWidth = true;
            infoVlg.childForceExpandHeight = false;
            infoVlg.childAlignment = TextAnchor.MiddleLeft;

            TextMeshProUGUI label = BywaterUiHelpers.CreateLabel(
                infoCol.transform,
                "Name",
                string.Empty,
                _font,
                15.5f,
                BywaterUiHelpers.ParchmentText,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                wrap: false);
            label.overflowMode = TextOverflowModes.Truncate;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 23f;

            TextMeshProUGUI statsLbl = BywaterUiHelpers.CreateLabel(
                infoCol.transform,
                "Stats",
                string.Empty,
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.MidlineLeft,
                wrap: false);
            statsLbl.overflowMode = TextOverflowModes.Truncate;
            statsLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            RectTransform upgradeBadgeRt = BywaterUiHelpers.CreatePanel(
                row,
                "UpgradeBadge",
                new Color(0.08f, 0.25f, 0.13f, 0.96f),
                withBrassOutline: true,
                outlinePx: 1.2f);
            BywaterUiHelpers.SetBorderColor(upgradeBadgeRt, BywaterUiHelpers.SuccessGreen);
            LayoutElement badgeLe = upgradeBadgeRt.gameObject.AddComponent<LayoutElement>();
            badgeLe.preferredWidth = 96f;
            badgeLe.minWidth = 96f;
            badgeLe.preferredHeight = 26f;
            badgeLe.flexibleWidth = 0f;
            badgeLe.flexibleHeight = 0f;

            TextMeshProUGUI upgradeLbl = BywaterUiHelpers.CreateLabel(
                upgradeBadgeRt,
                "UpgradeLabel",
                "[UPGRADE]",
                _font,
                15f,
                BywaterUiHelpers.SuccessGreen,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            upgradeLbl.rectTransform.anchorMin = Vector2.zero;
            upgradeLbl.rectTransform.anchorMax = Vector2.one;
            upgradeLbl.rectTransform.offsetMin = Vector2.zero;
            upgradeLbl.rectTransform.offsetMax = Vector2.zero;
            upgradeBadgeRt.gameObject.SetActive(false);

            TextMeshProUGUI priceLbl = BywaterUiHelpers.CreateLabel(
                row,
                "Price",
                string.Empty,
                _font,
                15.5f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.MidlineRight,
                FontStyles.Bold,
                wrap: false);
            LayoutElement priceLe = priceLbl.gameObject.AddComponent<LayoutElement>();
            priceLe.preferredWidth = 68f;
            priceLe.preferredHeight = 32f;
            priceLe.flexibleHeight = 0f;

            ShopBuyRowRefs rowRef = new ShopBuyRowRefs
            {
                Root = row.gameObject,
                ItemIcon = iconImg,
                NameText = label,
                StatsText = statsLbl,
                UpgradeBadge = upgradeBadgeRt.gameObject,
                PriceText = priceLbl
            };

            Button buyBtn = BywaterUiHelpers.CreateButton(
                row,
                "BuyBtn",
                "Buy",
                _font,
                () => OnBuyItemRequested?.Invoke(rowRef.BoundShopId, rowRef.BoundItemId),
                15f);
            LayoutElement buyLe = buyBtn.gameObject.AddComponent<LayoutElement>();
            buyLe.preferredWidth = 74f;
            buyLe.preferredHeight = 36f;
            buyLe.flexibleHeight = 0f;
            rowRef.BuyBtn = buyBtn;

            _shopBuyRowPool.Add(rowRef);
            return rowRef;
        }

        private ShopSellRowRefs GetOrCreateShopSellRow(int index)
        {
            if (index < _shopSellRowPool.Count)
            {
                return _shopSellRowPool[index];
            }

            RectTransform row = BywaterUiHelpers.CreatePanel(
                _shopSellScroll.content,
                $"SellRow_{index}",
                BywaterUiHelpers.CardBg,
                withBrassOutline: false);
            LayoutElement rowLe = row.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 48f;

            HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            Image iconImg = BywaterUiHelpers.CreateImage(row, "Icon", null, Color.white, preserveAspect: true);
            iconImg.raycastTarget = false;
            LayoutElement iconLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 28f;
            iconLe.preferredHeight = 28f;
            iconLe.minWidth = 28f;
            iconLe.minHeight = 28f;
            iconImg.gameObject.SetActive(false);

            TextMeshProUGUI lbl = BywaterUiHelpers.CreateLabel(
                row,
                "Item",
                string.Empty,
                _font,
                15f,
                BywaterUiHelpers.ParchmentText,
                TextAlignmentOptions.MidlineLeft,
                wrap: false);
            lbl.overflowMode = TextOverflowModes.Truncate;
            LayoutElement lblLe = lbl.gameObject.AddComponent<LayoutElement>();
            lblLe.flexibleWidth = 1f;

            TextMeshProUGUI valLbl = BywaterUiHelpers.CreateLabel(
                row,
                "Val",
                string.Empty,
                _font,
                15.5f,
                BywaterUiHelpers.SuccessGreen,
                TextAlignmentOptions.MidlineRight,
                FontStyles.Bold,
                wrap: false);
            LayoutElement valLe = valLbl.gameObject.AddComponent<LayoutElement>();
            valLe.preferredWidth = 70f;

            ShopSellRowRefs rowRef = new ShopSellRowRefs
            {
                Root = row.gameObject,
                ItemIcon = iconImg,
                ItemText = lbl,
                ValueText = valLbl,
                BoundStashIndex = index
            };

            Button sellBtn = BywaterUiHelpers.CreateButton(
                row,
                "SellBtn",
                "Sell 1",
                _font,
                () => OnSellItemRequested?.Invoke(rowRef.BoundStashIndex),
                15f);
            LayoutElement sellLe = sellBtn.gameObject.AddComponent<LayoutElement>();
            sellLe.preferredWidth = 78f;
            rowRef.SellBtn = sellBtn;

            _shopSellRowPool.Add(rowRef);
            return rowRef;
        }

        public void RefreshCombatHud(
            TacticalCombatSession combat,
            BywaterM1Bundle bundle,
            CombatActionMode activeMode,
            int selectedSpellId,
            string speedLabel)
        {
            if (!_uiBuilt || combat == null)
            {
                return;
            }

            _cachedBundle = bundle;
            SetCombatHudActive(true);

            // Top bar: concise Round | Turn on left + utility pills on right
            _topMapText.text = $"Round {combat.RoundNumber} | Turn {combat.TurnNumber}";

            // 1. Top-Center Initiative Ribbon (up to 8 cards + clean round dividers)
            IReadOnlyList<InitiativeEntry> ribbon = combat.GetInitiativeRibbon(8);
            int prevRound = -1;
            for (int i = 0; i < _initiativeCards.Count; i++)
            {
                InitiativeCardRefs card = _initiativeCards[i];
                if (i >= ribbon.Count)
                {
                    card.Root.gameObject.SetActive(false);
                    if (i > 0 && i - 1 < _roundDividers.Count)
                    {
                        _roundDividers[i - 1].Root.SetActive(false);
                    }
                    continue;
                }

                InitiativeEntry entry = ribbon[i];
                if (i > 0 && i - 1 < _roundDividers.Count)
                {
                    RoundDividerRefs div = _roundDividers[i - 1];
                    bool showDivider = prevRound >= 0 && entry.RoundNumber != prevRound;
                    div.Root.SetActive(showDivider);
                    if (showDivider)
                    {
                        div.Label.text = $"R{entry.RoundNumber}";
                    }
                }
                prevRound = entry.RoundNumber;

                card.Root.gameObject.SetActive(true);
                CombatantState unit = combat.GetCombatant(entry.CombatantId);

                Sprite sprite = ResolveCombatantPortrait(unit);
                card.Portrait.sprite = sprite;
                card.Portrait.preserveAspect = true;
                card.Portrait.color = sprite != null ? Color.white : new Color(0.25f, 0.25f, 0.35f, 1f);
                card.NameText.text = entry.Name;

                if (entry.IsCurrentTurn)
                {
                    card.MetaText.text = "TURN";
                    card.MetaText.color = BywaterUiHelpers.BrassHighlight;
                    card.Background.color = new Color(0.290f, 0.220f, 0.080f, 0.98f);
                    BywaterUiHelpers.SetBorderColor(card.Root, BywaterUiHelpers.BrassHighlight);
                    card.Root.localScale = new Vector3(1.10f, 1.18f, 1f);
                    if (card.Layout != null)
                    {
                        card.Layout.preferredWidth = 174f;
                    }
                }
                else
                {
                    card.MetaText.text = unit != null ? $"HP {unit.Hp}/{unit.MaxHp}" : "Ready";
                    card.MetaText.color = BywaterUiHelpers.ParchmentText;
                    card.Background.color = entry.Side == CombatSide.Hero
                        ? new Color(0.070f, 0.115f, 0.200f, 0.94f)
                        : new Color(0.180f, 0.075f, 0.095f, 0.94f);
                    BywaterUiHelpers.SetBorderColor(
                        card.Root,
                        entry.Side == CombatSide.Hero
                            ? new Color(0.24f, 0.56f, 0.80f, 0.90f)
                            : new Color(0.75f, 0.26f, 0.24f, 0.90f));
                    card.Root.localScale = Vector3.one;
                    if (card.Layout != null)
                    {
                        card.Layout.preferredWidth = 160f;
                    }
                }
            }

            // 2. Left-Edge Combat Party Mini-Portraits (up to 6 heroes)
            CombatantState actor = combat.CurrentActor;
            RefreshCombatPartyStrip(combat, actor);

            // 3. Bottom Tactical Action Bar + Visual Action Pips + Contextual Icon Hotbar
            bool isHeroTurn = combat.Outcome == CombatOutcome.InProgress && actor != null && actor.Side == CombatSide.Hero;

            if (actor != null)
            {
                Sprite actorSprite = ResolveCombatantPortrait(actor);
                _combatActorPortrait.sprite = actorSprite;
                _combatActorPortrait.preserveAspect = true;
                _combatActorPortrait.color = actorSprite != null ? Color.white : new Color(0.25f, 0.25f, 0.35f, 1f);
                _combatActorName.text = isHeroTurn ? actor.Name : $"{actor.Name} (AI)";
                _combatActorVitals.text = $"HP {actor.Hp}/{actor.MaxHp}  |  SP {actor.Sp}/{actor.MaxSp}  |  AC {actor.EffectiveAc}";

                RefreshVisualActionPips(actor);
            }

            RefreshContextualHotbar(combat, bundle, actor, isHeroTurn, activeMode, selectedSpellId);

            if (_combatSpeedLabel != null)
            {
                string compactSpeed = string.Equals(speedLabel, "Instant", StringComparison.OrdinalIgnoreCase)
                    ? "Inst"
                    : (string.IsNullOrWhiteSpace(speedLabel) ? "1x" : speedLabel);
                _combatSpeedLabel.text = compactSpeed;
            }

            // Keep legacy SpellPickerBar inactive with 0 pooled rows
            if (_spellPickerBar != null)
            {
                _spellPickerBar.SetActive(false);
            }

            string selectedSpellName = null;
            if (activeMode == CombatActionMode.Spell &&
                selectedSpellId >= 0 &&
                bundle != null &&
                bundle.Spells != null &&
                bundle.Spells.TryGetValue(selectedSpellId, out SpellDefinition activeSpellDef))
            {
                selectedSpellName = activeSpellDef.Name;
            }
            SetCombatTargetingBanner(isHeroTurn ? activeMode : CombatActionMode.Smart, selectedSpellName);

            // 4. Bottom-Right 3-Line Ticker + Expandable 40-Row History Drawer
            if (_lastLogSession != combat || combat.Log.Count != _lastLogCount)
            {
                RefreshCombatLog(combat);
            }
        }

        private static Image[] CreateConditionBadgeStrip(RectTransform portraitFrameRt, float iconSize)
        {
            const int maxBadges = 4;
            Image[] badges = new Image[maxBadges];
            if (portraitFrameRt == null)
            {
                return badges;
            }

            GameObject stripGo = new GameObject("ConditionBadges", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            stripGo.transform.SetParent(portraitFrameRt, false);

            LayoutElement stripLe = stripGo.GetComponent<LayoutElement>();
            stripLe.ignoreLayout = true;

            RectTransform stripRt = stripGo.GetComponent<RectTransform>();
            stripRt.anchorMin = new Vector2(1f, 0f);
            stripRt.anchorMax = new Vector2(1f, 0f);
            stripRt.pivot = new Vector2(1f, 0f);
            stripRt.anchoredPosition = new Vector2(-2f, 2f);
            stripRt.sizeDelta = new Vector2((iconSize + 2f) * maxBadges, iconSize);

            HorizontalLayoutGroup hlg = stripGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.LowerRight;

            for (int i = 0; i < maxBadges; i++)
            {
                Image badgeImg = BywaterUiHelpers.CreateImage(stripGo.transform, $"ConditionBadge_{i}", null, Color.white, preserveAspect: true);
                badgeImg.raycastTarget = false;
                LayoutElement le = badgeImg.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = iconSize;
                le.preferredHeight = iconSize;
                le.minWidth = iconSize;
                le.minHeight = iconSize;
                badgeImg.gameObject.SetActive(false);
                badges[i] = badgeImg;
            }

            return badges;
        }

        private void RefreshConditionBadges(Image[] badges, ConditionTracker conditions, bool isGuarding, bool onFireSurface)
        {
            if (badges == null || badges.Length == 0)
            {
                return;
            }

            _badgeKeysScratch.Clear();
            CollectConditionBadgeKeys(conditions, isGuarding, onFireSurface, _badgeKeysScratch);

            int slotIdx = 0;
            for (int i = 0; i < _badgeKeysScratch.Count && slotIdx < badges.Length; i++)
            {
                Sprite sprite = _catalog != null ? _catalog.GetConditionBadgeIcon(_badgeKeysScratch[i]) : null;
                if (sprite == null)
                {
                    continue;
                }

                Image img = badges[slotIdx++];
                if (img != null)
                {
                    img.sprite = sprite;
                    img.preserveAspect = true;
                    img.color = Color.white;
                    img.gameObject.SetActive(true);
                }
            }

            for (int i = slotIdx; i < badges.Length; i++)
            {
                if (badges[i] != null)
                {
                    badges[i].sprite = null;
                    badges[i].gameObject.SetActive(false);
                }
            }
        }

        private static void CollectConditionBadgeKeys(ConditionTracker conditions, bool isGuarding, bool onFireSurface, List<string> results)
        {
            if (results == null)
            {
                return;
            }

            bool hasPoison = false;
            bool hasBurn = onFireSurface;
            bool hasStun = false;
            bool hasBless = isGuarding;

            if (conditions != null)
            {
                if (conditions.Has(CharConditionId.Poisoned) || conditions.Has(CharConditionId.Diseased))
                {
                    hasPoison = true;
                }

                if (conditions.Has(CharConditionId.Helpless) ||
                    conditions.Has(CharConditionId.Tangled) ||
                    conditions.Has(CharConditionId.Slow) ||
                    conditions.Has(CharConditionId.Stupid) ||
                    conditions.Has(CharConditionId.Confused) ||
                    conditions.Has(CharConditionId.Blind) ||
                    conditions.Has(CharConditionId.TurnedToStone) ||
                    conditions.Has(CharConditionId.Silenced) ||
                    conditions.Has(CharConditionId.RunsAway))
                {
                    hasStun = true;
                }

                if (conditions.Has(CharConditionId.Strong) ||
                    conditions.Has(CharConditionId.AttackBonus) ||
                    conditions.Has(CharConditionId.ShieldFromHits) ||
                    conditions.Has(CharConditionId.ShieldFromProjectiles) ||
                    conditions.Has(CharConditionId.Regenerating) ||
                    conditions.Has(CharConditionId.MagicAura) ||
                    conditions.Has(CharConditionId.DefenseBonus) ||
                    conditions.Has(CharConditionId.Speedy) ||
                    conditions.Has(CharConditionId.ProtectionFromEvil))
                {
                    hasBless = true;
                }

                IReadOnlyList<ConditionDisplayEffect> effects = conditions.GetDisplayEffects();
                for (int i = 0; i < effects.Count; i++)
                {
                    ConditionDisplayEffect eff = effects[i];
                    if (eff == null)
                    {
                        continue;
                    }

                    string codeOrName = string.Concat(eff.Code ?? string.Empty, " ", eff.DisplayName ?? string.Empty);
                    if (codeOrName.IndexOf("burn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        codeOrName.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        codeOrName.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasBurn = true;
                    }
                    else if (codeOrName.IndexOf("poison", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasPoison = true;
                    }
                    else if (codeOrName.IndexOf("stun", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasStun = true;
                    }
                    else if (codeOrName.IndexOf("bless", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasBless = true;
                    }
                }
            }

            if (hasPoison) results.Add("poison");
            if (hasBurn) results.Add("burn");
            if (hasStun) results.Add("stun");
            if (hasBless) results.Add("bless");
        }

        private Sprite ResolveHeroPortrait(string heroId, int portraitId, string archetype)
        {
            if (_catalog == null)
            {
                return null;
            }

            Sprite unified = _catalog.GetUnifiedPortrait(heroId);
            if (unified == null && !string.IsNullOrWhiteSpace(archetype))
            {
                unified = _catalog.GetUnifiedPortrait(archetype);
            }

            if (unified != null)
            {
                return unified;
            }

            BywaterAssetCatalog.HeroArtEntry heroArt = _catalog.GetHeroArt(heroId, portraitId, archetype);
            return heroArt != null ? (heroArt.portrait != null ? heroArt.portrait : heroArt.sprite) : null;
        }

        private Sprite ResolveCombatantPortrait(CombatantState unit)
        {
            if (unit == null || _catalog == null)
            {
                return null;
            }

            if (unit.Side == CombatSide.Hero)
            {
                string archetype = string.Empty;
                if (_cachedParty != null)
                {
                    for (int i = 0; i < _cachedParty.Members.Count; i++)
                    {
                        PartyMemberState m = _cachedParty.Members[i];
                        if (m != null && string.Equals(m.Id, unit.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            archetype = m.Archetype;
                            break;
                        }
                    }
                }

                Sprite heroPortrait = ResolveHeroPortrait(unit.Id, unit.PortraitId, archetype);
                if (heroPortrait == null && !string.IsNullOrWhiteSpace(unit.Name))
                {
                    heroPortrait = _catalog.GetUnifiedPortrait(unit.Name);
                }
                return heroPortrait;
            }

            Sprite monsterPortrait = _catalog.GetMonsterPortrait(unit.Id, unit.IconId);
            if (monsterPortrait == null && !string.IsNullOrWhiteSpace(unit.Name))
            {
                monsterPortrait = _catalog.GetMonsterPortrait(unit.Name, unit.IconId);
            }
            if (monsterPortrait != null)
            {
                return monsterPortrait;
            }

            BywaterAssetCatalog.MonsterArtEntry monArt = _catalog.GetMonsterArt(unit.Id, unit.IconId);
            return monArt != null ? monArt.sprite : null;
        }

        private void RefreshCombatPartyStrip(TacticalCombatSession combat, CombatantState currentActor)
        {
            int heroSlot = 0;
            IReadOnlyList<CombatantState> all = combat.Combatants;
            for (int i = 0; i < all.Count && heroSlot < _combatPartyCards.Count; i++)
            {
                CombatantState unit = all[i];
                if (unit == null || unit.Side != CombatSide.Hero)
                {
                    continue;
                }

                CombatPartyCardRefs card = _combatPartyCards[heroSlot];
                card.BoundCombatantId = unit.Id;
                card.Root.SetActive(true);

                Sprite sprite = ResolveCombatantPortrait(unit);
                card.Portrait.sprite = sprite;
                card.Portrait.preserveAspect = true;
                card.Portrait.color = sprite != null ? Color.white : new Color(0.22f, 0.26f, 0.36f, 1f);
                bool onFireSurface = combat.Grid != null &&
                                     combat.Grid.InBounds(unit.Position) &&
                                     combat.Grid.GetSurface(unit.Position) == SurfaceType.Fire;
                RefreshConditionBadges(card.ConditionBadges, unit.Conditions, unit.IsGuarding, onFireSurface);
                card.NameText.text = unit.Name;

                float hpRatio = Mathf.Clamp01(unit.Hp / (float)Mathf.Max(1, unit.MaxHp));
                card.HpFill.rectTransform.anchorMax = new Vector2(hpRatio, 1f);
                card.HpFill.color = hpRatio > 0.35f ? BywaterUiHelpers.HpFillColor : BywaterUiHelpers.HpLowColor;

                float spRatio = unit.MaxSp > 0 ? Mathf.Clamp01(unit.Sp / (float)unit.MaxSp) : 0f;
                card.SpFill.rectTransform.anchorMax = new Vector2(spRatio, 1f);

                bool isCurrent = currentActor != null && string.Equals(currentActor.Id, unit.Id, StringComparison.Ordinal);
                bool isFallen = !unit.IsAlive;
                card.CanvasGroup.alpha = isFallen ? 0.45f : 1f;
                card.Background.color = isCurrent ? BywaterUiHelpers.CardSelectedBg : BywaterUiHelpers.CardBg;
                BywaterUiHelpers.SetBorderColor(
                    card.Root.transform,
                    isCurrent ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.BrassAccent);

                heroSlot++;
            }

            for (int i = heroSlot; i < _combatPartyCards.Count; i++)
            {
                _combatPartyCards[i].Root.SetActive(false);
                _combatPartyCards[i].BoundCombatantId = null;
            }
        }

        private void RefreshVisualActionPips(CombatantState actor)
        {
            int moveMax = Mathf.Max(1, actor.Economy.MoveBudget);
            int moveRem = Mathf.Clamp(actor.Economy.MoveRemaining, 0, moveMax);
            int segCount = _movePipSegments.Count;
            int litSegments = moveRem <= 0 ? 0 : Mathf.CeilToInt((moveRem / (float)moveMax) * segCount);

            Color moveLit = new Color(0.18f, 0.84f, 0.44f, 1f);
            Color moveSpent = new Color(0.16f, 0.20f, 0.26f, 0.90f);
            for (int i = 0; i < segCount; i++)
            {
                _movePipSegments[i].color = i < litSegments ? moveLit : moveSpent;
            }

            if (_combatMovePip != null)
            {
                _combatMovePip.text = $"{moveRem}/{moveMax}";
                _combatMovePip.color = moveRem > 0 ? moveLit : BywaterUiHelpers.MutedSlate;
            }

            Color dimCharcoal = new Color(0.22f, 0.25f, 0.32f, 0.95f);
            Color actionGold = new Color(0.98f, 0.80f, 0.08f, 1f); // #FACC15
            Color swiftCyan = new Color(0.22f, 0.74f, 0.97f, 1f);  // #38BDF8

            if (_combatStdPipIcon != null)
            {
                _combatStdPipIcon.color = actor.Economy.HasStandardAction ? actionGold : dimCharcoal;
            }
            if (_combatStdPip != null)
            {
                _combatStdPip.color = actor.Economy.HasStandardAction ? actionGold : BywaterUiHelpers.MutedSlate;
            }

            if (_combatSwiftPipIcon != null)
            {
                _combatSwiftPipIcon.color = actor.Economy.HasSwiftAction ? swiftCyan : dimCharcoal;
            }
            if (_combatSwiftPip != null)
            {
                _combatSwiftPip.color = actor.Economy.HasSwiftAction ? swiftCyan : BywaterUiHelpers.MutedSlate;
            }
        }

        private void RefreshContextualHotbar(
            TacticalCombatSession combat,
            BywaterM1Bundle bundle,
            CombatantState actor,
            bool isHeroTurn,
            CombatActionMode activeMode,
            int selectedSpellId)
        {
            _visibleHotbarActionIds.Clear();

            int slotCursor = 0;
            bool hasStd = isHeroTurn && actor != null && actor.Economy.HasStandardAction;
            bool hasSwift = isHeroTurn && actor != null && actor.Economy.HasSwiftAction;

            if (actor != null && actor.IsAlive)
            {
                // Slot 1: Melee (always for living hero)
                string meleeLabel = ResolveEquippedWeaponLabel(combat, bundle, actor, EquipmentSlot.Weapon, "Strike", out string meleeIconKey, out int meleeItemId, out string meleeItemName);
                Sprite meleeSprite = _catalog != null && (meleeItemId > 0 || !string.IsNullOrEmpty(meleeItemName))
                    ? _catalog.GetItemIcon(meleeItemId, meleeItemName)
                    : null;
                ConfigureHotbarSlot(
                    slotCursor++,
                    actionId: "melee",
                    mode: CombatActionMode.Melee,
                    spellId: -1,
                    hotkeyIndex: slotCursor,
                    iconKey: meleeIconKey,
                    label: meleeLabel,
                    spCost: 0,
                    interactable: hasStd,
                    selected: isHeroTurn && activeMode == CombatActionMode.Melee,
                    tipTitle: $"{meleeLabel} [Melee]",
                    tipBody: $"Weapon: {actor.WeaponDice} (+{actor.EffectiveToHit} Hit). Standard action.",
                    directIconSprite: meleeSprite);

                // Slot 2: Ranged (ONLY if actor has ranged weapon/range)
                if (actor.RangedRange > 0 && !string.IsNullOrWhiteSpace(actor.RangedDice))
                {
                    string rangedLabel = ResolveEquippedWeaponLabel(combat, bundle, actor, EquipmentSlot.MissileWeapon, "Ranged", out _, out int rangedItemId, out string rangedItemName);
                    Sprite rangedSprite = _catalog != null && (rangedItemId > 0 || !string.IsNullOrEmpty(rangedItemName))
                        ? _catalog.GetItemIcon(rangedItemId, rangedItemName)
                        : null;
                    ConfigureHotbarSlot(
                        slotCursor++,
                        actionId: "ranged",
                        mode: CombatActionMode.Ranged,
                        spellId: -1,
                        hotkeyIndex: slotCursor,
                        iconKey: "bow",
                        label: rangedLabel,
                        spCost: 0,
                        interactable: hasStd,
                        selected: isHeroTurn && activeMode == CombatActionMode.Ranged,
                        tipTitle: $"{rangedLabel} [Ranged]",
                        tipBody: $"Damage: {actor.RangedDice}, Range {actor.RangedRange}. Standard action.",
                        directIconSprite: rangedSprite);
                }

                // Direct Spell Slots (NO Spell submenu button, 0 slots for non-casters)
                if (actor.SpellIds.Count > 0 && bundle != null)
                {
                    for (int i = 0; i < actor.SpellIds.Count && slotCursor < MaxHotbarSlots; i++)
                    {
                        int spellId = actor.SpellIds[i];
                        if (!bundle.Spells.TryGetValue(spellId, out SpellDefinition spell))
                        {
                            continue;
                        }

                        bool enoughSp = actor.Sp >= spell.SpCost;
                        bool isSelectedSpell = isHeroTurn && activeMode == CombatActionMode.Spell && spell.Id == selectedSpellId;
                        string spellIcon = ResolveSpellIconKey(spell);
                        Sprite spellSprite = _catalog != null
                            ? (_catalog.GetSpellIcon(spell.Id, spell.Key) ?? _catalog.GetSpellIcon(spell.Id, spell.Name))
                            : null;
                        string scope = (spell.AoeOffsets.Count > 1 ||
                            string.Equals(spell.TargetMode, "AreaOfEffect", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(spell.TargetMode, "AllEnemies", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(spell.TargetMode, "AllAllies", StringComparison.OrdinalIgnoreCase))
                            ? "AoE"
                            : "Single";

                        ConfigureHotbarSlot(
                            slotCursor++,
                            actionId: $"spell:{spell.Id}",
                            mode: CombatActionMode.Spell,
                            spellId: spell.Id,
                            hotkeyIndex: slotCursor,
                            iconKey: spellIcon,
                            label: ShortenSpellName(spell.Name),
                            spCost: spell.SpCost,
                            interactable: hasStd && enoughSp,
                            selected: isSelectedSpell,
                            tipTitle: $"{spell.Name} ({spell.SpCost} SP)",
                            tipBody: $"{scope} | Range {spell.Range} | {(string.IsNullOrWhiteSpace(spell.EffectDice) ? spell.EffectKind : spell.EffectDice)}. {(string.IsNullOrWhiteSpace(spell.Description) ? string.Empty : spell.Description)}",
                            directIconSprite: spellSprite);
                    }
                }
            }

            for (int i = slotCursor; i < _hotbarSlots.Count; i++)
            {
                _hotbarSlots[i].Root.SetActive(false);
            }

            // Fixed Tactical Skills
            Color enabledIconTint = Color.white;
            Color disabledIconTint = new Color(0.55f, 0.55f, 0.60f, 0.85f);

            bool showUndo = isHeroTurn && combat.CanUndoMove;
            if (_combatUndoBtn != null)
            {
                _combatUndoBtn.gameObject.SetActive(showUndo);
                _combatUndoBtn.interactable = showUndo;
                if (_combatUndoBtnIcon != null) _combatUndoBtnIcon.color = showUndo ? enabledIconTint : disabledIconTint;
                if (showUndo) _visibleHotbarActionIds.Add("undo_move");
            }

            bool hasPotion = PartyHasHealingPotion(combat, bundle);
            if (_combatPotionBtn != null)
            {
                _combatPotionBtn.gameObject.SetActive(hasPotion);
                _combatPotionBtn.interactable = hasPotion && hasSwift;
                if (_combatPotionBtnIcon != null) _combatPotionBtnIcon.color = (hasPotion && hasSwift) ? enabledIconTint : disabledIconTint;
                BywaterUiHelpers.SetButtonColors(_combatPotionBtn, isHeroTurn && activeMode == CombatActionMode.Potion);
                if (hasPotion) _visibleHotbarActionIds.Add("potion");
            }

            if (_combatShoveBtn != null)
            {
                _combatShoveBtn.interactable = hasSwift;
                if (_combatShoveBtnIcon != null) _combatShoveBtnIcon.color = hasSwift ? enabledIconTint : disabledIconTint;
                BywaterUiHelpers.SetButtonColors(_combatShoveBtn, isHeroTurn && activeMode == CombatActionMode.Shove);
                _visibleHotbarActionIds.Add("shove");
            }
            if (_combatShoveSwiftBadgeText != null)
            {
                _combatShoveSwiftBadgeText.color = hasSwift ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.MutedSlate;
            }
            if (_combatShoveSwiftBadgeBg != null)
            {
                _combatShoveSwiftBadgeBg.color = hasSwift
                    ? new Color(0.32f, 0.22f, 0.04f, 0.96f)
                    : new Color(0.10f, 0.12f, 0.18f, 0.90f);
                BywaterUiHelpers.SetBorderColor(_combatShoveSwiftBadgeBg.transform, hasSwift ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.MutedSlate);
            }

            if (_combatGuardBtn != null)
            {
                _combatGuardBtn.interactable = hasStd;
                if (_combatGuardBtnIcon != null) _combatGuardBtnIcon.color = hasStd ? enabledIconTint : disabledIconTint;
                _visibleHotbarActionIds.Add("guard");
            }

            if (_combatEndTurnBtn != null)
            {
                _combatEndTurnBtn.interactable = isHeroTurn;
                if (_combatEndTurnBtnIcon != null) _combatEndTurnBtnIcon.color = isHeroTurn ? enabledIconTint : disabledIconTint;
                _pulseEndTurn = isHeroTurn && actor != null && !actor.Economy.HasStandardAction && !actor.Economy.HasSwiftAction;
                if (!_pulseEndTurn)
                {
                    BywaterUiHelpers.SetButtonColors(_combatEndTurnBtn, false);
                }
                _visibleHotbarActionIds.Add("end_turn");
            }
        }

        private void ConfigureHotbarSlot(
            int slotIndex,
            string actionId,
            CombatActionMode mode,
            int spellId,
            int hotkeyIndex,
            string iconKey,
            string label,
            int spCost,
            bool interactable,
            bool selected,
            string tipTitle,
            string tipBody,
            Sprite directIconSprite = null)
        {
            if (slotIndex < 0 || slotIndex >= _hotbarSlots.Count)
            {
                return;
            }

            HotbarSlotRefs slot = _hotbarSlots[slotIndex];
            slot.ActionId = actionId;
            slot.BoundMode = mode;
            slot.BoundSpellId = spellId;
            slot.TooltipTitle = tipTitle;
            slot.TooltipBody = tipBody;
            slot.HotkeyText.text = hotkeyIndex <= 6 ? hotkeyIndex.ToString() : string.Empty;
            slot.LabelText.text = label;
            slot.Icon.sprite = directIconSprite != null ? directIconSprite : LoadHotbarIcon(iconKey);
            slot.Icon.preserveAspect = true;
            slot.Icon.color = interactable ? Color.white : new Color(0.55f, 0.55f, 0.60f, 0.85f);

            if (spCost > 0)
            {
                slot.SpText.gameObject.SetActive(true);
                slot.SpText.text = $"{spCost}sp";
            }
            else
            {
                slot.SpText.gameObject.SetActive(false);
            }

            slot.Button.interactable = interactable;
            BywaterUiHelpers.SetButtonColors(slot.Button, selected);
            slot.Root.SetActive(true);
            _visibleHotbarActionIds.Add(actionId);
        }

        private static string ResolveEquippedWeaponLabel(
            TacticalCombatSession combat,
            BywaterM1Bundle bundle,
            CombatantState actor,
            EquipmentSlot eqSlot,
            string fallback,
            out string iconKey,
            out int itemId,
            out string itemName)
        {
            iconKey = eqSlot == EquipmentSlot.MissileWeapon ? "bow" : "sword";
            itemId = 0;
            itemName = null;
            if (combat?.Party != null && bundle?.Items != null && actor != null)
            {
                for (int i = 0; i < combat.Party.Members.Count; i++)
                {
                    PartyMemberState member = combat.Party.Members[i];
                    if (member != null && string.Equals(member.Id, actor.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        InventoryItem eq = member.GetEquipped(eqSlot);
                        if (eq != null && bundle.Items.TryGetValue(eq.ItemId, out ItemDefinition def) && !string.IsNullOrWhiteSpace(def.Name))
                        {
                            itemId = def.Id;
                            itemName = def.Name;
                            string lower = def.Name.ToLowerInvariant();
                            if (lower.Contains("dagger") || lower.Contains("dirk") || lower.Contains("knife")) iconKey = "dagger";
                            else if (lower.Contains("staff") || lower.Contains("wand") || lower.Contains("rod")) iconKey = "staff";
                            else if (lower.Contains("bow") || lower.Contains("crossbow") || lower.Contains("sling")) iconKey = "bow";
                            else iconKey = "sword";
                            return ShortenWeaponName(def.Name);
                        }
                        break;
                    }
                }
            }

            return fallback;
        }

        private static string ShortenSpellName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return "Spell";
            }

            string trimmed = fullName.Trim();
            if (trimmed.Equals("Magic Darts", StringComparison.OrdinalIgnoreCase)) return "Darts";
            if (trimmed.Equals("Flame Hands", StringComparison.OrdinalIgnoreCase)) return "Flame";
            if (trimmed.Equals("Enchanted Blade", StringComparison.OrdinalIgnoreCase)) return "Ench.Blade";
            if (trimmed.Equals("Sparkling Armor", StringComparison.OrdinalIgnoreCase)) return "Spark Armor";
            if (trimmed.Equals("Cure Light Wounds", StringComparison.OrdinalIgnoreCase)) return "Cure Light";
            if (trimmed.Equals("Protection from Evil", StringComparison.OrdinalIgnoreCase)) return "Prot. Evil";

            if (trimmed.Length <= 11)
            {
                return trimmed;
            }

            int space = trimmed.LastIndexOf(' ');
            if (space > 0 && space < trimmed.Length - 1)
            {
                string tail = trimmed.Substring(space + 1);
                if (tail.Length <= 11)
                {
                    return tail;
                }
            }

            return trimmed.Substring(0, 10) + ".";
        }

        private static string ShortenWeaponName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return "Strike";
            }

            string trimmed = fullName.Trim();
            if (trimmed.Length <= 11)
            {
                return trimmed;
            }

            int lastSpace = trimmed.LastIndexOf(' ');
            if (lastSpace > 0 && lastSpace < trimmed.Length - 1)
            {
                return trimmed.Substring(lastSpace + 1);
            }

            return trimmed.Substring(0, 11);
        }

        private static string ResolveSpellIconKey(SpellDefinition spell)
        {
            if (spell == null)
            {
                return "staff";
            }

            string name = (spell.Name ?? string.Empty).ToLowerInvariant();
            string dmg = (spell.DamageType ?? string.Empty).ToLowerInvariant();
            string effect = (spell.EffectKind ?? string.Empty).ToLowerInvariant();

            if (effect.Contains("heal") || name.Contains("heal") || name.Contains("cure")) return "heal";
            if (name.Contains("flame")) return "flame_hands";
            if (name.Contains("fire") || name.Contains("brimstone") || dmg.Contains("heat") || dmg.Contains("fire")) return "fireball";
            if (name.Contains("dart") || name.Contains("bolt") || name.Contains("missile")) return "magic_darts";
            if (effect.Contains("buff") || name.Contains("shield") || name.Contains("armor") || name.Contains("aura") || name.Contains("blade")) return "buff";
            if (effect.Contains("crowd") || effect.Contains("control") || dmg.Contains("mental") || name.Contains("daze") || name.Contains("sandman") || name.Contains("fear")) return "daze";
            return "staff";
        }

        private static bool PartyHasHealingPotion(TacticalCombatSession combat, BywaterM1Bundle bundle)
        {
            if (combat?.Party == null || bundle?.Items == null)
            {
                return true;
            }

            IReadOnlyList<InventoryItem> stash = combat.Party.Stash;
            for (int i = 0; i < stash.Count; i++)
            {
                InventoryItem item = stash[i];
                if (item == null || item.Quantity <= 0)
                {
                    continue;
                }

                if (bundle.Items.TryGetValue(item.ItemId, out ItemDefinition def) &&
                    (!string.IsNullOrWhiteSpace(def.HealDice) ||
                     string.Equals(def.Category, "Potion", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private Sprite LoadHotbarIcon(string key)
        {
            string normalized = string.IsNullOrWhiteSpace(key) ? "sword" : key.Trim().ToLowerInvariant();
            if (_catalog != null)
            {
                Sprite catalogSprite = _catalog.GetHotbarIcon(normalized);
                if (catalogSprite != null)
                {
                    return catalogSprite;
                }
            }

            string baseKey = normalized.StartsWith("icon_", StringComparison.Ordinal) ? normalized.Substring(5) : normalized;
            Sprite resSprite = Resources.Load<Sprite>($"Art/Cartography/Icons/icon_{baseKey}")
                ?? Resources.Load<Sprite>($"Icons/icon_{baseKey}");
            if (resSprite != null)
            {
                return resSprite;
            }

#if UNITY_EDITOR
            Sprite editorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Cartography/Icons/icon_{baseKey}.png");
            if (editorSprite != null)
            {
                return editorSprite;
            }
#endif

            return BywaterUiHelpers.GetProceduralIconSprite(baseKey);
        }

        public void SetCombatTooltip(string title, string body)
        {
            SetCombatHoverPreview(title, body, null);
        }

        public void SetHoverInfo(string title, string body, string elevationBadge = null)
        {
            SetCombatHoverPreview(title, body, elevationBadge);
        }

        public void SetCombatHoverPreview(string title, string details, string elevationBadge = null)
        {
            if (!_uiBuilt || _cursorTooltipRoot == null || _tooltipTitle == null || _tooltipBody == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(details) && string.IsNullOrWhiteSpace(elevationBadge))
            {
                ClearCombatHoverPreview();
                return;
            }

            _tooltipTitle.text = string.IsNullOrEmpty(title) ? "Tactical Preview" : title;
            string rawDetails = string.IsNullOrEmpty(details) ? string.Empty : details;

            ApplyCombatTooltipStyling(title, rawDetails, elevationBadge);
            _tooltipBody.text = HighlightCombatCallouts(rawDetails);

            _cursorTooltipRoot.SetActive(true);
            _cursorTooltipRoot.transform.SetAsLastSibling();
            if (!_hasManualTooltipPos && Mouse.current != null)
            {
                UpdateCursorTooltipPosition(Mouse.current.position.ReadValue());
            }
        }

        public void ClearCombatHoverPreview()
        {
            _hasManualTooltipPos = false;
            if (_cursorTooltipRoot != null)
            {
                _cursorTooltipRoot.SetActive(false);
            }
        }

        public void UpdateCursorTooltipPosition(Vector2 screenPos)
        {
            if (!_uiBuilt || _tooltipPanel == null || _canvasRect == null)
            {
                return;
            }

            _hasManualTooltipPos = true;
            Camera uiCam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, uiCam, out Vector2 localPoint))
            {
                return;
            }

            Rect canvasBounds = _canvasRect.rect;
            float tipWidth = Mathf.Max(260f, _tooltipPanel.sizeDelta.x);
            float tipHeight = Mathf.Max(110f, LayoutUtility.GetPreferredHeight(_tooltipPanel));
            float xMin = canvasBounds.xMin + 12f;
            float xMax = canvasBounds.xMax - tipWidth - 12f;
            float yMin = canvasBounds.yMin + tipHeight + 12f;
            float yMax = canvasBounds.yMax - 56f;

            float x = localPoint.x + 18f;
            if (x > xMax)
            {
                x = localPoint.x - tipWidth - 14f;
            }

            float y = localPoint.y - 16f;
            if (y < yMin)
            {
                y = localPoint.y + tipHeight + 14f;
            }

            _tooltipPanel.anchoredPosition = new Vector2(
                Mathf.Clamp(x, xMin, Mathf.Max(xMin, xMax)),
                Mathf.Clamp(y, Mathf.Min(yMin, yMax), yMax));
        }

        public void ClampTooltipToActiveCanvas()
        {
            if (!_uiBuilt || _tooltipPanel == null || _canvasRect == null || _cursorTooltipRoot == null || !_cursorTooltipRoot.activeSelf)
            {
                return;
            }

            Rect canvasBounds = _canvasRect.rect;
            float tipWidth = Mathf.Max(260f, _tooltipPanel.sizeDelta.x);
            float tipHeight = Mathf.Max(110f, LayoutUtility.GetPreferredHeight(_tooltipPanel));
            float xMin = canvasBounds.xMin + 12f;
            float xMax = canvasBounds.xMax - tipWidth - 12f;
            float yMin = canvasBounds.yMin + tipHeight + 12f;
            float yMax = canvasBounds.yMax - 56f;

            Vector2 pos = _tooltipPanel.anchoredPosition;
            _tooltipPanel.anchoredPosition = new Vector2(
                Mathf.Clamp(pos.x, xMin, Mathf.Max(xMin, xMax)),
                Mathf.Clamp(pos.y, Mathf.Min(yMin, yMax), yMax));
        }

        private void ApplyCombatTooltipStyling(string title, string rawDetails, string explicitBadge)
        {
            string combined = string.Concat(title ?? string.Empty, " ", rawDetails ?? string.Empty, " ", explicitBadge ?? string.Empty);
            bool hasLedgeFall = combined.IndexOf("LEDGE FALL", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasWallCrush = combined.IndexOf("WALL CRUSH", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasHighGround = combined.IndexOf("HIGH GROUND", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasLowGround = combined.IndexOf("LOW GROUND", StringComparison.OrdinalIgnoreCase) >= 0;

            string badgeLabel = !string.IsNullOrWhiteSpace(explicitBadge) ? explicitBadge.Trim() : null;
            if (string.IsNullOrEmpty(badgeLabel))
            {
                if (hasLedgeFall && hasWallCrush)
                {
                    badgeLabel = "LEDGE FALL +1d6/tier | WALL CRUSH";
                }
                else if (hasLedgeFall)
                {
                    badgeLabel = "LEDGE FALL +1d6/tier";
                }
                else if (hasWallCrush)
                {
                    badgeLabel = "WALL CRUSH";
                }
                else if (hasHighGround)
                {
                    badgeLabel = "[HIGH GROUND +15% / +1 RNG]";
                }
                else if (hasLowGround)
                {
                    badgeLabel = "[LOW GROUND -15%]";
                }
            }

            Color borderCol = BywaterUiHelpers.BrassAccent;
            if (hasLedgeFall || hasWallCrush)
            {
                borderCol = BywaterUiHelpers.DangerRed;
                if (_tooltipBadgeText != null) _tooltipBadgeText.color = BywaterUiHelpers.DangerRed;
                if (_tooltipBadgeBg != null) _tooltipBadgeBg.color = new Color(0.28f, 0.06f, 0.07f, 0.96f);
            }
            else if (hasHighGround)
            {
                borderCol = BywaterUiHelpers.BrassHighlight;
                if (_tooltipBadgeText != null) _tooltipBadgeText.color = BywaterUiHelpers.BrassHighlight;
                if (_tooltipBadgeBg != null) _tooltipBadgeBg.color = new Color(0.26f, 0.20f, 0.05f, 0.96f);
            }
            else if (hasLowGround)
            {
                Color amberWarning = new Color(0.95f, 0.60f, 0.22f, 1f);
                borderCol = amberWarning;
                if (_tooltipBadgeText != null) _tooltipBadgeText.color = amberWarning;
                if (_tooltipBadgeBg != null) _tooltipBadgeBg.color = new Color(0.28f, 0.14f, 0.04f, 0.96f);
            }
            else if (!string.IsNullOrEmpty(badgeLabel))
            {
                borderCol = BywaterUiHelpers.BrassHighlight;
                if (_tooltipBadgeText != null) _tooltipBadgeText.color = BywaterUiHelpers.BrassHighlight;
                if (_tooltipBadgeBg != null) _tooltipBadgeBg.color = new Color(0.22f, 0.17f, 0.06f, 0.96f);
            }

            if (_tooltipPanel != null)
            {
                BywaterUiHelpers.SetBorderColor(_tooltipPanel, borderCol);
            }

            if (_tooltipBadgeRoot != null)
            {
                bool showBadge = !string.IsNullOrEmpty(badgeLabel);
                _tooltipBadgeRoot.gameObject.SetActive(showBadge);
                if (showBadge)
                {
                    _tooltipBadgeText.text = badgeLabel;
                    BywaterUiHelpers.SetBorderColor(_tooltipBadgeRoot, borderCol);
                }
            }
        }

        private static string HighlightCombatCallouts(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string result = text;
            result = ReplaceCaseInsensitive(result, "[HIGH GROUND +15% / +1 RNG]", "<color=#F5CC63><b>[HIGH GROUND +15% / +1 RNG]</b></color>");
            if (result.IndexOf("[HIGH GROUND +15% / +1 RNG]", StringComparison.OrdinalIgnoreCase) < 0)
            {
                result = ReplaceCaseInsensitive(result, "HIGH GROUND", "<color=#F5CC63><b>HIGH GROUND</b></color>");
            }

            result = ReplaceCaseInsensitive(result, "[LOW GROUND -15%]", "<color=#F29938><b>[LOW GROUND -15%]</b></color>");
            if (result.IndexOf("[LOW GROUND -15%]", StringComparison.OrdinalIgnoreCase) < 0)
            {
                result = ReplaceCaseInsensitive(result, "LOW GROUND", "<color=#F29938><b>LOW GROUND</b></color>");
            }

            result = ReplaceCaseInsensitive(result, "LEDGE FALL +1d6/tier", "<color=#EB5247><b>LEDGE FALL +1d6/tier</b></color>");
            if (result.IndexOf("LEDGE FALL +1d6/tier", StringComparison.OrdinalIgnoreCase) < 0)
            {
                result = ReplaceCaseInsensitive(result, "LEDGE FALL", "<color=#EB5247><b>LEDGE FALL</b></color>");
            }

            result = ReplaceCaseInsensitive(result, "WALL CRUSH", "<color=#EB5247><b>WALL CRUSH</b></color>");
            return result;
        }

        private static string ReplaceCaseInsensitive(string source, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldValue))
            {
                return source;
            }

            int idx = source.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return source;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(source.Length + 32);
            int cursor = 0;
            while (idx >= 0)
            {
                sb.Append(source, cursor, idx - cursor);
                sb.Append(newValue);
                cursor = idx + oldValue.Length;
                idx = source.IndexOf(oldValue, cursor, StringComparison.OrdinalIgnoreCase);
            }

            sb.Append(source, cursor, source.Length - cursor);
            return sb.ToString();
        }

        public void ShowOutcomeModal(CombatOutcome outcome, int xpEarned, int goldEarned)
        {
            ShowOutcomeModal(outcome, xpEarned, 0, goldEarned, null);
        }

        public void ShowOutcomeModal(
            CombatOutcome outcome,
            int totalXp,
            int levelsGained,
            int gold,
            IReadOnlyList<string> lootNames)
        {
            if (!_uiBuilt || _outcomeModalRoot == null)
            {
                return;
            }

            if (_outcomeModalRect != null)
            {
                _outcomeModalRect.sizeDelta = new Vector2(600f, 430f);
            }
            if (_outcomeRichVictoryRoot != null) _outcomeRichVictoryRoot.SetActive(false);
            if (_outcomeSummary != null) _outcomeSummary.gameObject.SetActive(true);
            if (_victoryEquipUpgradesBtn != null) _victoryEquipUpgradesBtn.gameObject.SetActive(false);
            if (_victoryLevelUpNowBtn != null) _victoryLevelUpNowBtn.gameObject.SetActive(false);

            bool isVictory = outcome == CombatOutcome.Victory;
            _outcomeTitle.text = isVictory ? "VICTORY!" : "DEFEAT";
            _outcomeTitle.color = isVictory ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.DangerRed;

            bool showLevelUp = isVictory && levelsGained > 0;
            if (_outcomeLevelUpBanner != null)
            {
                _outcomeLevelUpBanner.SetActive(showLevelUp);
                if (showLevelUp && _outcomeLevelUpText != null)
                {
                    _outcomeLevelUpText.text = levelsGained == 1
                        ? "LEVEL UP! (+1 Party Level Gained)"
                        : $"LEVEL UP! (+{levelsGained} Party Levels Gained)";
                }
            }

            if (isVictory)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
                sb.Append("Enemies defeated!\n\n");
                sb.Append($"XP Earned: +{totalXp}\n");
                sb.Append($"Gold Looted: +{gold}g\n");
                if (levelsGained > 0)
                {
                    sb.Append($"Levels Gained: +{levelsGained}\n");
                }

                if (lootNames != null && lootNames.Count > 0)
                {
                    sb.Append("\nLooted Items:\n");
                    int listed = 0;
                    for (int i = 0; i < lootNames.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lootNames[i]))
                        {
                            continue;
                        }

                        if (listed >= 6)
                        {
                            int remaining = lootNames.Count - i;
                            sb.Append($"  - ...and {remaining} more\n");
                            break;
                        }

                        sb.Append($"  - {lootNames[i]}\n");
                        listed++;
                    }
                }
                else
                {
                    sb.Append("\nLooted Items: None\n");
                }

                sb.Append("\nFallen allies recover at 1 HP.");
                _outcomeSummary.text = sb.ToString();
            }
            else
            {
                _outcomeSummary.text = "All party members have fallen in battle.\n\nRetry the skirmish or revive the vanguard at the Bywater Shrine.";
            }

            _outcomeContinueBtn.gameObject.SetActive(isVictory);
            _outcomeRetryBtn.gameObject.SetActive(!isVictory);
            _outcomeReviveBtn.gameObject.SetActive(!isVictory);

            _outcomeModalRoot.SetActive(true);
            _outcomeModalRoot.transform.SetAsLastSibling();
        }

        public void HideOutcomeModal()
        {
            if (_outcomeModalRoot != null)
            {
                _outcomeModalRoot.SetActive(false);
            }
        }

        public void SetCombatTargetingBanner(CombatActionMode mode, string spellName = null)
        {
            if (!_uiBuilt || _targetingBannerRoot == null || _targetingBannerText == null)
            {
                return;
            }

            if (!_isCombatHudActive || mode == CombatActionMode.Smart)
            {
                if (_targetingBannerRoot.activeSelf)
                {
                    _targetingBannerRoot.SetActive(false);
                }
                return;
            }

            string msg;
            Color accent = BywaterUiHelpers.BrassHighlight;
            switch (mode)
            {
                case CombatActionMode.Melee:
                    msg = "Melee Mode - Click enemy, or click tile / Right-Click / [Esc] to Move";
                    accent = BywaterUiHelpers.DangerRed;
                    break;
                case CombatActionMode.Ranged:
                    msg = "Ranged Mode - Click enemy in LOS, or Right-Click / [Esc] to Move";
                    accent = BywaterUiHelpers.BrassHighlight;
                    break;
                case CombatActionMode.Spell:
                    string label = string.IsNullOrWhiteSpace(spellName) ? "Spell" : spellName;
                    msg = $"Spell Mode ({label}) - Click target, or Right-Click / [Esc] to Move";
                    accent = BywaterUiHelpers.InfoCyan;
                    break;
                case CombatActionMode.Shove:
                    msg = "Shove Mode [Swift] - Click adjacent unit, or Right-Click / [Esc] to Move";
                    accent = BywaterUiHelpers.BrassHighlight;
                    break;
                case CombatActionMode.Potion:
                    msg = "Potion Mode [Swift] - Click ally to heal, or Right-Click / [Esc] to Move";
                    accent = BywaterUiHelpers.SuccessGreen;
                    break;
                default:
                    _targetingBannerRoot.SetActive(false);
                    return;
            }

            _targetingBannerText.text = msg;
            _targetingBannerText.color = accent;
            BywaterUiHelpers.SetBorderColor(_targetingBannerRoot.transform, accent);
            if (!_targetingBannerRoot.activeSelf)
            {
                _targetingBannerRoot.SetActive(true);
            }
        }

        private void SetCombatHudActive(bool active)
        {
            bool changed = !_hasAppliedHudActive || _isCombatHudActive != active;
            _hasAppliedHudActive = true;
            _isCombatHudActive = active;

            if (changed)
            {
                if (_exploreRoot != null)
                {
                    _exploreRoot.SetActive(!active);
                }

                if (_combatRoot != null)
                {
                    _combatRoot.SetActive(active);
                }

                if (active)
                {
                    if (_partyDrawerRoot != null) _partyDrawerRoot.SetActive(false);
                    if (_shopModalRoot != null) _shopModalRoot.SetActive(false);
                    if (_templeModalRoot != null) _templeModalRoot.SetActive(false);
                    if (_quickResolveModalRoot != null) _quickResolveModalRoot.SetActive(false);
                }
                else
                {
                    _lastLogCount = -1;
                    _lastLogSession = null;
                    _pulseEndTurn = false;
                    ClearCombatHoverPreview();
                    if (_targetingBannerRoot != null)
                    {
                        _targetingBannerRoot.SetActive(false);
                    }
                    if (_combatLogDrawerRoot != null)
                    {
                        _combatLogDrawerRoot.SetActive(false);
                    }
                    if (_tickerToggleBadge != null)
                    {
                        _tickerToggleBadge.text = "[+]";
                    }
                    for (int i = 0; i < _tickerLines.Count; i++)
                    {
                        _tickerLines[i].text = string.Empty;
                    }
                }
            }

            ApplyTopBarVisibility();
        }

        private void ApplyTopBarVisibility()
        {
            if (!_uiBuilt)
            {
                return;
            }

            bool inCombat = _isCombatHudActive;
            bool hasContextPoi = !string.IsNullOrWhiteSpace(_contextPoiLabel);
            if (_hasAppliedTopBar &&
                _appliedTopBarCombat == inCombat &&
                _appliedTopBarDev == _isDevToolsVisible &&
                _appliedTopBarHasPoi == hasContextPoi)
            {
                return;
            }

            _hasAppliedTopBar = true;
            _appliedTopBarCombat = inCombat;
            _appliedTopBarDev = _isDevToolsVisible;
            _appliedTopBarHasPoi = hasContextPoi;

            if (_topGoldText != null) _topGoldText.gameObject.SetActive(!inCombat);
            if (_topTorchText != null) _topTorchText.gameObject.SetActive(!inCombat);
            if (_topTurnText != null) _topTurnText.gameObject.SetActive(!inCombat);
            ApplyEncounterDirectorTopBarVisibility(inCombat);
            if (_topSpacer != null) _topSpacer.SetActive(true);

            // Explore buttons: completely hidden in Combat
            if (_skirmishBtn != null) _skirmishBtn.gameObject.SetActive(!inCombat && _isDevToolsVisible);
            if (_warpBtn != null) _warpBtn.gameObject.SetActive(!inCombat && _isDevToolsVisible);
            if (_shopBtn != null) _shopBtn.gameObject.SetActive(false);
            if (_shrineBtn != null) _shrineBtn.gameObject.SetActive(false);
            if (_contextPoiBtn != null) _contextPoiBtn.gameObject.SetActive(!inCombat && hasContextPoi);
            if (_partyBtn != null) _partyBtn.gameObject.SetActive(!inCombat);
            if (_campBtn != null) _campBtn.gameObject.SetActive(!inCombat);
            if (_saveBtn != null) _saveBtn.gameObject.SetActive(!inCombat);
            if (_loadBtn != null) _loadBtn.gameObject.SetActive(!inCombat);
            if (_menuBtn != null) _menuBtn.gameObject.SetActive(!inCombat);

            // Combat utility pills: shown ONLY in Combat
            if (_autoCombatBtn != null) _autoCombatBtn.gameObject.SetActive(inCombat);
            if (_combatSpeedBtn != null) _combatSpeedBtn.gameObject.SetActive(inCombat);
            if (_combatAudioBtn != null) _combatAudioBtn.gameObject.SetActive(inCombat);
            if (_combatGridBtn != null) _combatGridBtn.gameObject.SetActive(inCombat);
            if (_combatMenuBtn != null) _combatMenuBtn.gameObject.SetActive(inCombat);
        }

        private void RefreshCombatLog(TacticalCombatSession combat)
        {
            if (_combatLogRows.Count == 0 || combat == null)
            {
                return;
            }

            if (_lastLogSession != combat || _lastLogCount < 0 || combat.Log.Count < _lastLogCount)
            {
                for (int i = 0; i < _combatLogRows.Count; i++)
                {
                    _combatLogRows[i].gameObject.SetActive(false);
                }
                _logRingHead = 0;
                _lastLogCount = 0;
                _lastLogSession = combat;
            }

            int totalCount = combat.Log.Count;
            if (totalCount == _lastLogCount)
            {
                return;
            }

            int newCount = totalCount - _lastLogCount;
            int startIndex = newCount >= MaxLogLines ? totalCount - MaxLogLines : _lastLogCount;
            if (newCount >= MaxLogLines)
            {
                _logRingHead = 0;
            }

            for (int i = startIndex; i < totalCount; i++)
            {
                CombatLogEntry entry = combat.Log[i];
                Color entryColor = ResolveCombatLogColor(combat, entry);

                TextMeshProUGUI slot = _combatLogRows[_logRingHead];
                slot.text = $"[R{entry.RoundNumber}] {entry.Message}";
                slot.color = entryColor;
                if (!slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(true);
                }
                slot.rectTransform.SetAsLastSibling();
                _logRingHead = (_logRingHead + 1) % MaxLogLines;
            }

            // Update compact 3-line ticker with latest 3 entries
            int tickerCount = _tickerLines.Count;
            int tailStart = Mathf.Max(0, totalCount - tickerCount);
            for (int t = 0; t < tickerCount; t++)
            {
                int logIdx = tailStart + t;
                TextMeshProUGUI tickerLine = _tickerLines[t];
                if (logIdx < totalCount)
                {
                    CombatLogEntry entry = combat.Log[logIdx];
                    tickerLine.text = $"[R{entry.RoundNumber}] {entry.Message}";
                    tickerLine.color = ResolveCombatLogColor(combat, entry);
                }
                else
                {
                    tickerLine.text = string.Empty;
                }
            }

            _lastLogCount = totalCount;
            _lastLogSession = combat;

            if (_combatLogDrawerRoot != null && _combatLogDrawerRoot.activeSelf && _combatLogScroll != null)
            {
                Canvas.ForceUpdateCanvases();
                _combatLogScroll.verticalNormalizedPosition = 0f;
            }
        }

        private static Color ResolveCombatLogColor(TacticalCombatSession combat, CombatLogEntry entry)
        {
            CombatantState actor = combat.GetCombatant(entry.ActorId);
            if (string.Equals(entry.ActionType, "AoO", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(1.0f, 0.72f, 0.24f, 1f);
            }
            if (string.Equals(entry.ActionType, "Shove", StringComparison.OrdinalIgnoreCase))
            {
                return BywaterUiHelpers.BrassHighlight;
            }
            if (entry.HealingDone > 0)
            {
                return BywaterUiHelpers.SuccessGreen;
            }
            if (actor != null && actor.Side == CombatSide.Monster)
            {
                return new Color(0.96f, 0.56f, 0.52f, 1f);
            }
            if (string.Equals(entry.ActionType, "CastSpell", StringComparison.OrdinalIgnoreCase))
            {
                return BywaterUiHelpers.InfoCyan;
            }
            if (string.Equals(entry.ActionType, "Move", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.ActionType, "UndoMove", StringComparison.OrdinalIgnoreCase))
            {
                return BywaterUiHelpers.MutedSlate;
            }
            return BywaterUiHelpers.ParchmentText;
        }

        private void BuildTopHudBar(Transform parent)
        {
            RectTransform bar = BywaterUiHelpers.CreatePanel(parent, "TopHudBar", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.5f);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, 46f);

            HorizontalLayoutGroup hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(14, 14, 4, 4);
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _topMapText = BywaterUiHelpers.CreateLabel(bar, "MapNameText", "City of Bywater (2, 1)", _font, 18f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _topMapText.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_topMapText);
            LayoutElement mapLe = _topMapText.gameObject.AddComponent<LayoutElement>();
            mapLe.preferredWidth = 315f;

            _topGoldText = BywaterUiHelpers.CreateLabel(bar, "GoldText", "Gold: 400g", _font, 16f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_topGoldText);
            LayoutElement goldLe = _topGoldText.gameObject.AddComponent<LayoutElement>();
            goldLe.preferredWidth = 112f;

            _topTorchText = BywaterUiHelpers.CreateLabel(bar, "TorchText", "Torch: 0t", _font, 16f, new Color(1.0f, 0.72f, 0.32f, 1f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_topTorchText);
            LayoutElement torchLe = _topTorchText.gameObject.AddComponent<LayoutElement>();
            torchLe.preferredWidth = 96f;

            _topTurnText = BywaterUiHelpers.CreateLabel(bar, "TurnText", "Turn: 0", _font, 15.5f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.MidlineLeft, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_topTurnText);
            LayoutElement turnLe = _topTurnText.gameObject.AddComponent<LayoutElement>();
            turnLe.preferredWidth = 92f;

            BuildEncounterDirectorTopBarPills(bar);

            _topSpacer = new GameObject("TopSpacer", typeof(RectTransform), typeof(LayoutElement));
            _topSpacer.transform.SetParent(bar, false);
            _topSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

            // Explore buttons
            _skirmishBtn = AddTopButton(bar, "SkirmishBtn", "Skirmish", 104f, () => OnSkirmishRequested?.Invoke());
            _warpBtn = AddTopButton(bar, "WarpBtn", "Warp Portal", 122f, () => OnWarpPortalRequested?.Invoke());
            _warpBtnLabel = _warpBtn.GetComponentInChildren<TextMeshProUGUI>(true);
            _shopBtn = AddTopButton(bar, "ShopBtn", "Shop (B)", 98f, () => OnToggleShopRequested?.Invoke());
            _shrineBtn = AddTopButton(bar, "ShrineBtn", "Shrine", 84f, () => OnShrineRequested?.Invoke());
            _contextPoiBtn = AddTopButton(bar, "ContextPoiBtn", "Enter Shop", 196f, () =>
            {
                if (_contextPoiAction != null) _contextPoiAction.Invoke();
                else OnContextPoiRequested?.Invoke();
            });
            _contextPoiBtnLabel = _contextPoiBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (_contextPoiBtnLabel != null)
            {
                _contextPoiBtnLabel.enableAutoSizing = true;
                _contextPoiBtnLabel.fontSizeMin = 10.5f;
                _contextPoiBtnLabel.fontSizeMax = 14.5f;
                _contextPoiBtnLabel.overflowMode = TextOverflowModes.Truncate;
            }
            BywaterUiHelpers.SetButtonColors(_contextPoiBtn, true);

            _partyBtn = AddTopButton(bar, "PartyBtn", "Party (P)", 104f, () => OnTogglePartyRequested?.Invoke());
            _campBtn = AddTopButton(bar, "CampBtn", "Camp (C)", 100f, () => OnCampButtonRequested?.Invoke());
            _saveBtn = AddTopButton(bar, "SaveBtn", "Save (F5)", 104f, () => OnSaveRequested?.Invoke());
            _loadBtn = AddTopButton(bar, "LoadBtn", "Load (F9)", 104f, () => OnLoadRequested?.Invoke());
            _menuBtn = AddTopIconButton(bar, "MenuBtn", "settings", "[Esc] Menu", 136f, () => ToggleSettingsModal());

            // Combat utility pills (compact right-aligned, W-234 32x32 pixel icons)
            _autoCombatBtn = AddTopIconButton(bar, "AutoCombatBtn", "auto_combat", "[A] Auto", 136f, () =>
            {
                bool next = !_isAutoCombatOn;
                SetAutoCombatVisual(next);
                OnAutoCombatToggled?.Invoke(next);
            });
            _autoCombatBtnLabel = _autoCombatBtn.GetComponentInChildren<TextMeshProUGUI>();

            _combatSpeedBtn = AddTopIconButton(bar, "SpeedBtn", "speed", "1x", 90f, () => OnCycleSpeedRequested?.Invoke());
            _combatSpeedLabel = _combatSpeedBtn.GetComponentInChildren<TextMeshProUGUI>();

            _combatAudioBtn = AddTopIconButton(bar, "CombatAudioBtn", "audio", "Audio", 108f, () =>
            {
                _isMuted = !_isMuted;
                UpdateCombatAudioPillVisual();
                RefreshSettingsModalControls();
                OnMuteToggled?.Invoke(_isMuted);
            });
            _combatAudioBtnLabel = _combatAudioBtn.GetComponentInChildren<TextMeshProUGUI>();

            _combatGridBtn = AddTopIconButton(bar, "CombatGridBtn", "grid", "Grid", 94f, () => OnCombatGridToggleRequested?.Invoke());
            _combatMenuBtn = AddTopIconButton(bar, "CombatMenuBtn", "settings", "[Esc] Menu", 138f, () => ToggleSettingsModal());
        }

        private void UpdateCombatAudioPillVisual()
        {
            if (_combatAudioBtnLabel != null)
            {
                _combatAudioBtnLabel.text = _isMuted ? "Muted" : "Audio";
            }
            if (_combatAudioBtn != null)
            {
                BywaterUiHelpers.SetButtonColors(_combatAudioBtn, _isMuted);
            }
        }

        private Button AddTopButton(Transform parent, string name, string label, float width, UnityEngine.Events.UnityAction onClick)
        {
            Button btn = BywaterUiHelpers.CreateButton(parent, name, label, _font, onClick, 15f);
            LayoutElement le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            return btn;
        }

        private Button AddTopIconButton(Transform parent, string name, string iconKey, string label, float width, UnityEngine.Events.UnityAction onClick)
        {
            Button btn = AddTopButton(parent, name, label, width, onClick);
            AttachHorizontalIconToButton(btn, iconKey, 20f);
            return btn;
        }

        private void AttachHorizontalIconToButton(Button btn, string iconKey, float iconSize)
        {
            if (btn == null)
            {
                return;
            }

            HorizontalLayoutGroup hlg = btn.gameObject.GetComponent<HorizontalLayoutGroup>()
                ?? btn.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(7, 7, 2, 2);
            hlg.spacing = 5f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;

            TextMeshProUGUI lbl = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lbl != null)
            {
                LayoutElement lblLe = lbl.GetComponent<LayoutElement>() ?? lbl.gameObject.AddComponent<LayoutElement>();
                lblLe.flexibleWidth = 0f;
                lbl.alignment = TextAlignmentOptions.MidlineLeft;
            }

            Image iconImg = BywaterUiHelpers.CreateImage(btn.transform, "Icon", LoadHotbarIcon(iconKey), Color.white, preserveAspect: true);
            iconImg.raycastTarget = false;
            iconImg.transform.SetAsFirstSibling();
            LayoutElement iconLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = iconSize;
            iconLe.preferredHeight = iconSize;
            iconLe.minWidth = iconSize;
            iconLe.minHeight = iconSize;
        }

        private void BuildExploreHud(Transform parent)
        {
            _exploreRoot = new GameObject("ExploreHudRoot", typeof(RectTransform));
            _exploreRoot.transform.SetParent(parent, false);
            RectTransform rootRt = _exploreRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // Explore Hover Footer Chip (bottom-left)
            RectTransform hoverChip = BywaterUiHelpers.CreatePanel(_exploreRoot.transform, "ExploreHoverChip", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1.5f);
            hoverChip.anchorMin = new Vector2(0f, 0f);
            hoverChip.anchorMax = new Vector2(0f, 0f);
            hoverChip.pivot = new Vector2(0f, 0f);
            hoverChip.anchoredPosition = new Vector2(12f, 10f);
            hoverChip.sizeDelta = new Vector2(388f, 84f);

            VerticalLayoutGroup hoverVlg = hoverChip.gameObject.AddComponent<VerticalLayoutGroup>();
            hoverVlg.padding = new RectOffset(12, 12, 8, 8);
            hoverVlg.spacing = 4f;
            hoverVlg.childControlWidth = true;
            hoverVlg.childControlHeight = true;

            _hoverCoordText = BywaterUiHelpers.CreateLabel(hoverChip, "HoverCoord", "Tile (2, 1) - Cobblestone", _font, 16f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _hoverCoordText.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_hoverCoordText);
            _hoverDetailText = BywaterUiHelpers.CreateLabel(hoverChip, "HoverDetail", "Click to move", _font, 15f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.MidlineLeft, wrap: true);
            _hoverDetailText.overflowMode = TextOverflowModes.Truncate;

            // Explore Bottom Party Strip (6 Hero Cards)
            RectTransform strip = BywaterUiHelpers.CreatePanel(_exploreRoot.transform, "ExplorePartyStrip", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1.5f);
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.offsetMin = new Vector2(412f, 10f);
            strip.offsetMax = new Vector2(-12f, 102f);

            HorizontalLayoutGroup stripHlg = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            stripHlg.padding = new RectOffset(6, 6, 4, 4);
            stripHlg.spacing = 6f;
            stripHlg.childControlWidth = true;
            stripHlg.childControlHeight = true;
            stripHlg.childForceExpandWidth = true;
            stripHlg.childForceExpandHeight = true;

            for (int i = 0; i < 6; i++)
            {
                int heroIdx = i;
                RectTransform cardRt = BywaterUiHelpers.CreatePanel(strip, $"HeroCard_{i}", BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1.4f);
                Button cardBtn = cardRt.gameObject.AddComponent<Button>();
                cardBtn.targetGraphic = cardRt.GetComponent<Image>();
                cardBtn.onClick.AddListener(() => OnHeroCardClicked?.Invoke(heroIdx));

                HorizontalLayoutGroup cardHlg = cardRt.gameObject.AddComponent<HorizontalLayoutGroup>();
                cardHlg.padding = new RectOffset(6, 6, 4, 4);
                cardHlg.spacing = 6f;
                cardHlg.childControlWidth = true;
                cardHlg.childControlHeight = true;
                cardHlg.childForceExpandWidth = false;
                cardHlg.childForceExpandHeight = false;
                cardHlg.childAlignment = TextAnchor.MiddleLeft;

                RectTransform portraitFrameRt = BywaterUiHelpers.CreatePortraitFrame(cardRt, "PortraitFrame", 74f, out Image portrait);
                portrait.preserveAspect = true;
                Image[] conditionBadges = CreateConditionBadgeStrip(portraitFrameRt, 18f);

                GameObject infoCol = new GameObject("Info", typeof(RectTransform), typeof(VerticalLayoutGroup));
                infoCol.transform.SetParent(cardRt, false);
                LayoutElement infoLe = infoCol.AddComponent<LayoutElement>();
                infoLe.flexibleWidth = 1f;
                infoLe.preferredHeight = 76f;

                VerticalLayoutGroup infoVlg = infoCol.GetComponent<VerticalLayoutGroup>();
                infoVlg.spacing = 2f;
                infoVlg.childControlWidth = true;
                infoVlg.childControlHeight = true;

                TextMeshProUGUI nameTxt = BywaterUiHelpers.CreateLabel(infoCol.transform, "Name", $"Hero {i + 1}", _font, 16f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                nameTxt.overflowMode = TextOverflowModes.Truncate;
                BywaterUiHelpers.AddTextDropShadow(nameTxt);
                TextMeshProUGUI classTxt = BywaterUiHelpers.CreateLabel(infoCol.transform, "Class", "Fighter", _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, wrap: false);
                classTxt.overflowMode = TextOverflowModes.Truncate;

                Image hpFill = BywaterUiHelpers.CreateProgressBar(infoCol.transform, "HpBar", BywaterUiHelpers.HpFillColor, out RectTransform hpBarRt);
                LayoutElement hpBarLe = hpBarRt.gameObject.AddComponent<LayoutElement>();
                hpBarLe.preferredHeight = 18f;
                TextMeshProUGUI hpTxt = BywaterUiHelpers.CreateLabel(hpBarRt, "HpText", "HP 10/10", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
                BywaterUiHelpers.AddTextDropShadow(hpTxt);
                hpTxt.rectTransform.anchorMin = Vector2.zero;
                hpTxt.rectTransform.anchorMax = Vector2.one;
                hpTxt.rectTransform.offsetMin = Vector2.zero;
                hpTxt.rectTransform.offsetMax = Vector2.zero;

                Image spFill = BywaterUiHelpers.CreateProgressBar(infoCol.transform, "SpBar", BywaterUiHelpers.SpFillColor, out RectTransform spBarRt);
                LayoutElement spBarLe = spBarRt.gameObject.AddComponent<LayoutElement>();
                spBarLe.preferredHeight = 16f;
                TextMeshProUGUI spTxt = BywaterUiHelpers.CreateLabel(spBarRt, "SpText", "SP 0/0", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
                BywaterUiHelpers.AddTextDropShadow(spTxt);
                spTxt.rectTransform.anchorMin = Vector2.zero;
                spTxt.rectTransform.anchorMax = Vector2.one;
                spTxt.rectTransform.offsetMin = Vector2.zero;
                spTxt.rectTransform.offsetMax = Vector2.zero;

                Image xpFill = BywaterUiHelpers.CreateProgressBar(infoCol.transform, "XpBar", BywaterUiHelpers.BrassHighlight, out RectTransform xpBarRt);
                LayoutElement xpBarLe = xpBarRt.gameObject.AddComponent<LayoutElement>();
                xpBarLe.preferredHeight = 5f;

                Button lvlUpBtn = BywaterUiHelpers.CreateButton(portraitFrameRt, "LvlUpBadge", "LVL UP!", _font, () => OpenLevelUpModal(heroIdx), 15f);
                BywaterUiHelpers.SetButtonColors(lvlUpBtn, true);
                RectTransform lvlRt = lvlUpBtn.GetComponent<RectTransform>();
                lvlRt.anchorMin = new Vector2(0f, 1f);
                lvlRt.anchorMax = new Vector2(1f, 1f);
                lvlRt.pivot = new Vector2(0.5f, 1f);
                lvlRt.anchoredPosition = new Vector2(0f, 2f);
                lvlRt.sizeDelta = new Vector2(0f, 22f);
                lvlUpBtn.gameObject.SetActive(false);

                _heroCards.Add(new HeroCardRefs
                {
                    Root = cardRt,
                    Background = cardRt.GetComponent<Image>(),
                    Portrait = portrait,
                    ConditionBadges = conditionBadges,
                    NameText = nameTxt,
                    ClassText = classTxt,
                    HpFill = hpFill,
                    HpText = hpTxt,
                    SpFill = spFill,
                    SpText = spTxt,
                    XpFill = xpFill,
                    LvlUpBadgeBtn = lvlUpBtn
                });
            }
        }

        private void BuildPartyDrawer(Transform parent)
        {
            RectTransform modal = BywaterUiHelpers.CreatePanel(parent, "PartyDrawerModal", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 2f);
            modal.anchorMin = new Vector2(0.5f, 0.5f);
            modal.anchorMax = new Vector2(0.5f, 0.5f);
            modal.pivot = new Vector2(0.5f, 0.5f);
            modal.anchoredPosition = new Vector2(0f, 8f);
            modal.sizeDelta = new Vector2(1380f, 840f);
            _partyDrawerRoot = modal.gameObject;
            _partyModalRect = modal;

            VerticalLayoutGroup rootVlg = modal.gameObject.AddComponent<VerticalLayoutGroup>();
            rootVlg.padding = new RectOffset(16, 16, 14, 14);
            rootVlg.spacing = 10f;
            rootVlg.childControlWidth = true;
            rootVlg.childControlHeight = true;
            rootVlg.childForceExpandHeight = false;

            // Header
            GameObject header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            header.transform.SetParent(modal, false);
            LayoutElement headerLe = header.AddComponent<LayoutElement>();
            headerLe.preferredHeight = 42f;
            HorizontalLayoutGroup headerHlg = header.GetComponent<HorizontalLayoutGroup>();
            headerHlg.spacing = 12f;
            headerHlg.childControlWidth = true;
            headerHlg.childControlHeight = true;
            headerHlg.childForceExpandWidth = false;

            TextMeshProUGUI title = BywaterUiHelpers.CreateLabel(header.transform, "Title", "Vanguard Party, Equipment, Stash & Camp", _font, 21f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(title);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _drawerGoldTorchLabel = BywaterUiHelpers.CreateLabel(header.transform, "GoldTorch", "Gold: 400g  |  Torch: 0t", _font, 15.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineRight, wrap: false);
            LayoutElement gtLe = _drawerGoldTorchLabel.gameObject.AddComponent<LayoutElement>();
            gtLe.preferredWidth = 230f;
            gtLe.flexibleHeight = 0f;

            BuildHeaderCampPills(header.transform);

            Button closeBtn = BywaterUiHelpers.CreateButton(header.transform, "CloseBtn", "Close", _font, () => TogglePartyPanel(false), 15.5f);
            LayoutElement closeLe = closeBtn.gameObject.AddComponent<LayoutElement>();
            closeLe.preferredWidth = 90f;
            closeLe.flexibleHeight = 0f;

            // Hero Selector Strip
            GameObject selectorRow = new GameObject("HeroSelectorRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            selectorRow.transform.SetParent(modal, false);
            selectorRow.AddComponent<LayoutElement>().preferredHeight = 40f;
            HorizontalLayoutGroup selHlg = selectorRow.GetComponent<HorizontalLayoutGroup>();
            selHlg.spacing = 8f;
            selHlg.childControlWidth = true;
            selHlg.childControlHeight = true;
            selHlg.childForceExpandWidth = true;

            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                Button btn = BywaterUiHelpers.CreateButton(selectorRow.transform, $"SelectHero_{i}", $"Hero {i + 1}", _font, () => OnPartyHeroSelected?.Invoke(idx), 15.5f);
                _partyHeroSelectorBtns.Add(btn);
            }

            // Two-column body
            GameObject body = new GameObject("Body", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            body.transform.SetParent(modal, false);
            LayoutElement bodyLe = body.AddComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            HorizontalLayoutGroup bodyHlg = body.GetComponent<HorizontalLayoutGroup>();
            bodyHlg.spacing = 14f;
            bodyHlg.childControlWidth = true;
            bodyHlg.childControlHeight = true;
            bodyHlg.childForceExpandWidth = false;

            // Left Column: Hero Sheet & Equipment
            RectTransform leftCol = BywaterUiHelpers.CreatePanel(body.transform, "HeroSheetCol", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            leftCol.gameObject.AddComponent<LayoutElement>().preferredWidth = 590f;
            VerticalLayoutGroup leftVlg = leftCol.gameObject.AddComponent<VerticalLayoutGroup>();
            leftVlg.padding = new RectOffset(12, 12, 12, 12);
            leftVlg.spacing = 8f;
            leftVlg.childControlWidth = true;
            leftVlg.childControlHeight = true;
            leftVlg.childForceExpandHeight = false;

            GameObject heroTop = new GameObject("HeroTop", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            heroTop.transform.SetParent(leftCol, false);
            heroTop.AddComponent<LayoutElement>().preferredHeight = 132f;
            HorizontalLayoutGroup htHlg = heroTop.GetComponent<HorizontalLayoutGroup>();
            htHlg.spacing = 14f;
            htHlg.childControlWidth = true;
            htHlg.childControlHeight = true;
            htHlg.childForceExpandWidth = false;
            htHlg.childAlignment = TextAnchor.MiddleLeft;

            BywaterUiHelpers.CreatePortraitFrame(heroTop.transform, "DrawerPortraitFrame", 128f, out _drawerHeroPortrait);

            _drawerHeroTitle = BywaterUiHelpers.CreateLabel(heroTop.transform, "HeroTitle", "Aldric", _font, 18.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            BywaterUiHelpers.AddTextDropShadow(_drawerHeroTitle);
            _drawerHeroTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _drawerHeroStats = BywaterUiHelpers.CreateLabel(leftCol, "HeroStats", string.Empty, _font, 15.5f, BywaterUiHelpers.ParchmentText);
            _drawerHeroStats.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;

            _drawerHeroConditions = BywaterUiHelpers.CreateLabel(leftCol, "HeroConditions", "Conditions: Healthy", _font, 15f, BywaterUiHelpers.InfoCyan);
            _drawerHeroConditions.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            _drawerHeroSpells = BywaterUiHelpers.CreateLabel(leftCol, "HeroSpells", "Known Spells: None", _font, 15f, BywaterUiHelpers.MutedSlate);
            _drawerHeroSpells.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

            BuildPaperDollAndCharSheet(leftCol);

            // Right Column: Shared Stash (full height, Camp buttons moved cleanly into Header)
            RectTransform rightCol = BywaterUiHelpers.CreatePanel(body.transform, "StashCampCol", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            rightCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            VerticalLayoutGroup rightVlg = rightCol.gameObject.AddComponent<VerticalLayoutGroup>();
            rightVlg.padding = new RectOffset(12, 12, 12, 12);
            rightVlg.spacing = 6f;
            rightVlg.childControlWidth = true;
            rightVlg.childControlHeight = true;
            rightVlg.childForceExpandHeight = false;

            BuildStashControls(rightCol);

            _partyStashScroll = BywaterUiHelpers.CreateScrollView(rightCol, "StashScroll", _font, spacing: 5f);
            LayoutElement stashScrollLe = _partyStashScroll.gameObject.AddComponent<LayoutElement>();
            stashScrollLe.minHeight = 320f;
            stashScrollLe.preferredHeight = 520f;
            stashScrollLe.flexibleHeight = 1f;

            _stashEmptyLabel = BywaterUiHelpers.CreateLabel(
                _partyStashScroll.content,
                "EmptyStash",
                "Party stash is empty.",
                _font,
                15.5f,
                BywaterUiHelpers.MutedSlate);
            _stashEmptyLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            _stashEmptyLabel.gameObject.SetActive(false);
        }

        private void BuildShopModal(Transform parent)
        {
            RectTransform modal = BywaterUiHelpers.CreatePanel(parent, "TownShopModal", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 2f);
            modal.anchorMin = new Vector2(0.5f, 0.5f);
            modal.anchorMax = new Vector2(0.5f, 0.5f);
            modal.pivot = new Vector2(0.5f, 0.5f);
            modal.anchoredPosition = new Vector2(0f, 8f);
            modal.sizeDelta = new Vector2(1360f, 800f);
            _shopModalRoot = modal.gameObject;

            VerticalLayoutGroup rootVlg = modal.gameObject.AddComponent<VerticalLayoutGroup>();
            rootVlg.padding = new RectOffset(16, 16, 14, 14);
            rootVlg.spacing = 10f;
            rootVlg.childControlWidth = true;
            rootVlg.childControlHeight = true;
            rootVlg.childForceExpandHeight = false;

            // Header (max 68px height)
            GameObject header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            header.transform.SetParent(modal, false);
            LayoutElement headerLe = header.AddComponent<LayoutElement>();
            headerLe.minHeight = 64f;
            headerLe.preferredHeight = 68f;
            headerLe.flexibleHeight = 0f;
            HorizontalLayoutGroup hHlg = header.GetComponent<HorizontalLayoutGroup>();
            hHlg.spacing = 12f;
            hHlg.childControlWidth = true;
            hHlg.childControlHeight = true;
            hHlg.childForceExpandWidth = false;
            hHlg.childForceExpandHeight = false;
            hHlg.childAlignment = TextAnchor.MiddleLeft;

            RectTransform portraitFrame = BywaterUiHelpers.CreatePortraitFrame(header.transform, "ShopkeeperPortraitFrame", 56f, out _shopPortraitImage);
            _shopPortraitImage.preserveAspect = true;
            LayoutElement portraitFrameLe = portraitFrame.GetComponent<LayoutElement>();
            if (portraitFrameLe != null)
            {
                portraitFrameLe.flexibleHeight = 0f;
            }

            GameObject titleStack = new GameObject("TitleStack", typeof(RectTransform), typeof(VerticalLayoutGroup));
            titleStack.transform.SetParent(header.transform, false);
            LayoutElement titleStackLe = titleStack.AddComponent<LayoutElement>();
            titleStackLe.flexibleWidth = 1f;
            titleStackLe.minHeight = 52f;
            titleStackLe.preferredHeight = 60f;
            titleStackLe.flexibleHeight = 0f;
            VerticalLayoutGroup titleVlg = titleStack.GetComponent<VerticalLayoutGroup>();
            titleVlg.spacing = 2f;
            titleVlg.childControlWidth = true;
            titleVlg.childControlHeight = true;
            titleVlg.childForceExpandWidth = true;
            titleVlg.childForceExpandHeight = false;
            titleVlg.childAlignment = TextAnchor.MiddleLeft;

            _shopHeaderTitle = BywaterUiHelpers.CreateLabel(
                titleStack.transform,
                "ShopTitle",
                "Bywater Armory - Master Blacksmith Borin",
                _font,
                20f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                wrap: false);
            _shopHeaderTitle.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_shopHeaderTitle);
            LayoutElement shopTitleLe = _shopHeaderTitle.gameObject.AddComponent<LayoutElement>();
            shopTitleLe.preferredHeight = 30f;
            shopTitleLe.flexibleHeight = 0f;

            _shopGreetingLabel = BywaterUiHelpers.CreateLabel(
                titleStack.transform,
                "ShopGreeting",
                "Fine steel, shields, and field provisions for the road.",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.MidlineLeft,
                wrap: false);
            _shopGreetingLabel.overflowMode = TextOverflowModes.Truncate;
            LayoutElement greetingLe = _shopGreetingLabel.gameObject.AddComponent<LayoutElement>();
            greetingLe.preferredHeight = 22f;
            greetingLe.flexibleHeight = 0f;

            _shopGoldLabel = BywaterUiHelpers.CreateLabel(header.transform, "ShopGold", "Party Gold: 400g", _font, 16.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineRight, FontStyles.Bold, wrap: false);
            LayoutElement goldLe = _shopGoldLabel.gameObject.AddComponent<LayoutElement>();
            goldLe.preferredWidth = 185f;
            goldLe.preferredHeight = 40f;
            goldLe.flexibleHeight = 0f;

            Button closeBtn = BywaterUiHelpers.CreateButton(header.transform, "CloseShopBtn", "Close", _font, CloseShopModal, 15.5f);
            LayoutElement closeLe = closeBtn.gameObject.AddComponent<LayoutElement>();
            closeLe.preferredWidth = 94f;
            closeLe.preferredHeight = 40f;
            closeLe.flexibleHeight = 0f;

            // Filter Bar (36px fixed height)
            GameObject filterRow = new GameObject("FilterRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            filterRow.transform.SetParent(modal, false);
            LayoutElement filterLe = filterRow.AddComponent<LayoutElement>();
            filterLe.minHeight = 36f;
            filterLe.preferredHeight = 36f;
            filterLe.flexibleHeight = 0f;
            HorizontalLayoutGroup fHlg = filterRow.GetComponent<HorizontalLayoutGroup>();
            fHlg.spacing = 6f;
            fHlg.childControlWidth = true;
            fHlg.childControlHeight = true;
            fHlg.childForceExpandWidth = false;
            fHlg.childForceExpandHeight = false;
            fHlg.childAlignment = TextAnchor.MiddleLeft;

            string[] categories = { "All", "Weapon", "Armor", "Shield", "Potion" };
            for (int i = 0; i < categories.Length; i++)
            {
                string cat = categories[i];
                Button catBtn = BywaterUiHelpers.CreateButton(filterRow.transform, $"Cat_{cat}", cat, _font, () =>
                {
                    _activeShopCategory = cat;
                    RefreshShopModal(_cachedParty, _cachedBundle);
                }, 15f);
                LayoutElement catLe = catBtn.gameObject.AddComponent<LayoutElement>();
                catLe.preferredWidth = 90f;
                catLe.preferredHeight = 32f;
                catLe.flexibleHeight = 0f;
                _shopCategoryBtns[cat] = catBtn;
            }

            _shopAffordableToggle = CreateAffordableToggle(filterRow.transform);
            LayoutElement affLe = _shopAffordableToggle.gameObject.AddComponent<LayoutElement>();
            affLe.preferredWidth = 156f;
            affLe.preferredHeight = 32f;
            affLe.flexibleHeight = 0f;
            _shopAffordableToggle.onValueChanged.AddListener(isOn =>
            {
                if (CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics())
                {
                    return;
                }
                if (isOn == _lastShopAffordableOnly)
                {
                    return;
                }
                _lastShopAffordableOnly = isOn;
                RefreshShopModal(_cachedParty, _cachedBundle);
            });

            _shopSearchInput = BywaterUiHelpers.CreateInputField(filterRow.transform, "ShopSearchInput", "Search items...", _font, 15.5f);
            LayoutElement searchLe = _shopSearchInput.gameObject.AddComponent<LayoutElement>();
            searchLe.flexibleWidth = 1f;
            searchLe.preferredHeight = 32f;
            searchLe.flexibleHeight = 0f;
            _shopSearchInput.onValueChanged.AddListener(_ => RefreshShopModal(_cachedParty, _cachedBundle));

            // Body columns (Buy Stock vs Sell Stash, >= 70% of 800px modal height)
            GameObject body = new GameObject("ShopBody", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            body.transform.SetParent(modal, false);
            LayoutElement bodyLe = body.AddComponent<LayoutElement>();
            bodyLe.minHeight = 520f;
            bodyLe.preferredHeight = 630f;
            bodyLe.flexibleHeight = 1f;
            HorizontalLayoutGroup bHlg = body.GetComponent<HorizontalLayoutGroup>();
            bHlg.spacing = 14f;
            bHlg.childControlWidth = true;
            bHlg.childControlHeight = true;
            bHlg.childForceExpandWidth = false;
            bHlg.childForceExpandHeight = true;

            // Buy Column
            RectTransform buyCol = BywaterUiHelpers.CreatePanel(body.transform, "BuyCol", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            buyCol.gameObject.AddComponent<LayoutElement>().preferredWidth = 680f;
            VerticalLayoutGroup buyVlg = buyCol.gameObject.AddComponent<VerticalLayoutGroup>();
            buyVlg.padding = new RectOffset(10, 10, 10, 10);
            buyVlg.spacing = 8f;
            buyVlg.childControlWidth = true;
            buyVlg.childControlHeight = true;
            buyVlg.childForceExpandHeight = false;

            TextMeshProUGUI buyTitle = BywaterUiHelpers.CreateLabel(buyCol, "BuyTitle", "Shop Stock", _font, 17f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            BywaterUiHelpers.AddTextDropShadow(buyTitle);
            buyTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            _shopBuyScroll = BywaterUiHelpers.CreateScrollView(buyCol, "ShopBuyScroll", _font, spacing: 5f);
            _shopBuyScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            _shopBuyEmptyLabel = BywaterUiHelpers.CreateLabel(
                _shopBuyScroll.content,
                "EmptyStock",
                "No items match the current filter.",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate);
            _shopBuyEmptyLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            _shopBuyEmptyLabel.gameObject.SetActive(false);

            // Sell Column
            RectTransform sellCol = BywaterUiHelpers.CreatePanel(body.transform, "SellCol", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            sellCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            VerticalLayoutGroup sellVlg = sellCol.gameObject.AddComponent<VerticalLayoutGroup>();
            sellVlg.padding = new RectOffset(10, 10, 10, 10);
            sellVlg.spacing = 8f;
            sellVlg.childControlWidth = true;
            sellVlg.childControlHeight = true;
            sellVlg.childForceExpandHeight = false;

            GameObject sellHeader = new GameObject("SellHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            sellHeader.transform.SetParent(sellCol, false);
            LayoutElement sellHeaderLe = sellHeader.AddComponent<LayoutElement>();
            sellHeaderLe.minHeight = 36f;
            sellHeaderLe.preferredHeight = 36f;
            sellHeaderLe.flexibleHeight = 0f;
            HorizontalLayoutGroup shHlg = sellHeader.GetComponent<HorizontalLayoutGroup>();
            shHlg.spacing = 8f;
            shHlg.childControlWidth = true;
            shHlg.childControlHeight = true;
            shHlg.childForceExpandWidth = false;
            shHlg.childForceExpandHeight = false;
            shHlg.childAlignment = TextAnchor.MiddleLeft;

            TextMeshProUGUI sellTitle = BywaterUiHelpers.CreateLabel(sellHeader.transform, "SellTitle", "Party Stash (Sell)", _font, 17f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(sellTitle);
            LayoutElement sellTitleLe = sellTitle.gameObject.AddComponent<LayoutElement>();
            sellTitleLe.flexibleWidth = 1f;
            sellTitleLe.preferredHeight = 32f;
            sellTitleLe.flexibleHeight = 0f;

            Button junkBtn = BywaterUiHelpers.CreateButton(sellHeader.transform, "SellJunkBtn", "Sell All Junk", _font, () => OnSellAllJunkRequested?.Invoke(), 15f);
            LayoutElement junkLe = junkBtn.gameObject.AddComponent<LayoutElement>();
            junkLe.preferredWidth = 132f;
            junkLe.preferredHeight = 32f;
            junkLe.flexibleHeight = 0f;

            Button idBtn = BywaterUiHelpers.CreateButton(sellHeader.transform, "IdentifyAllBtn", "Identify All", _font, () => OnIdentifyAllRequested?.Invoke(), 15f);
            LayoutElement idLe = idBtn.gameObject.AddComponent<LayoutElement>();
            idLe.preferredWidth = 126f;
            idLe.preferredHeight = 32f;
            idLe.flexibleHeight = 0f;

            _shopSellScroll = BywaterUiHelpers.CreateScrollView(sellCol, "ShopSellScroll", _font, spacing: 5f);
            _shopSellScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            _shopSellEmptyLabel = BywaterUiHelpers.CreateLabel(
                _shopSellScroll.content,
                "EmptySell",
                "Stash is empty.",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate);
            _shopSellEmptyLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            _shopSellEmptyLabel.gameObject.SetActive(false);
        }

        private void BuildTempleModal(Transform parent)
        {
            RectTransform modal = BywaterUiHelpers.CreatePanel(parent, "TempleShrineModal", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 2f);
            modal.anchorMin = new Vector2(0.5f, 0.5f);
            modal.anchorMax = new Vector2(0.5f, 0.5f);
            modal.pivot = new Vector2(0.5f, 0.5f);
            modal.anchoredPosition = new Vector2(0f, 8f);
            modal.sizeDelta = new Vector2(800f, 476f);
            _templeModalRoot = modal.gameObject;

            VerticalLayoutGroup rootVlg = modal.gameObject.AddComponent<VerticalLayoutGroup>();
            rootVlg.padding = new RectOffset(16, 16, 14, 14);
            rootVlg.spacing = 10f;
            rootVlg.childControlWidth = true;
            rootVlg.childControlHeight = true;
            rootVlg.childForceExpandHeight = false;

            // Header (64-68px height)
            GameObject header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            header.transform.SetParent(modal, false);
            LayoutElement headerLe = header.AddComponent<LayoutElement>();
            headerLe.minHeight = 64f;
            headerLe.preferredHeight = 68f;
            headerLe.flexibleHeight = 0f;
            HorizontalLayoutGroup hHlg = header.GetComponent<HorizontalLayoutGroup>();
            hHlg.spacing = 12f;
            hHlg.childControlWidth = true;
            hHlg.childControlHeight = true;
            hHlg.childForceExpandWidth = false;
            hHlg.childForceExpandHeight = false;
            hHlg.childAlignment = TextAnchor.MiddleLeft;

            RectTransform portraitFrame = BywaterUiHelpers.CreatePortraitFrame(header.transform, "PriestessPortraitFrame", 56f, out _templePortraitImage);
            _templePortraitImage.preserveAspect = true;
            LayoutElement portraitFrameLe = portraitFrame.GetComponent<LayoutElement>();
            if (portraitFrameLe != null)
            {
                portraitFrameLe.flexibleHeight = 0f;
            }
            ApplyNpcPortrait(_templePortraitImage, "npc_priestess", "heal");

            GameObject titleStack = new GameObject("TitleStack", typeof(RectTransform), typeof(VerticalLayoutGroup));
            titleStack.transform.SetParent(header.transform, false);
            LayoutElement titleStackLe = titleStack.AddComponent<LayoutElement>();
            titleStackLe.flexibleWidth = 1f;
            titleStackLe.minHeight = 52f;
            titleStackLe.preferredHeight = 60f;
            titleStackLe.flexibleHeight = 0f;
            VerticalLayoutGroup titleVlg = titleStack.GetComponent<VerticalLayoutGroup>();
            titleVlg.spacing = 2f;
            titleVlg.childControlWidth = true;
            titleVlg.childControlHeight = true;
            titleVlg.childForceExpandWidth = true;
            titleVlg.childForceExpandHeight = false;
            titleVlg.childAlignment = TextAnchor.MiddleLeft;

            TextMeshProUGUI titleLbl = BywaterUiHelpers.CreateLabel(
                titleStack.transform,
                "TempleTitle",
                "Bywater Magistrate & Shrine",
                _font,
                20f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                wrap: false);
            titleLbl.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(titleLbl);
            LayoutElement titleLblLe = titleLbl.gameObject.AddComponent<LayoutElement>();
            titleLblLe.preferredHeight = 30f;
            titleLblLe.flexibleHeight = 0f;

            TextMeshProUGUI greetingLbl = BywaterUiHelpers.CreateLabel(
                titleStack.transform,
                "TempleGreeting",
                "High Priestess Elara offers sacred restoration, cleansing, or arcane renewal.",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.MidlineLeft,
                wrap: false);
            greetingLbl.overflowMode = TextOverflowModes.Truncate;
            LayoutElement greetingLe = greetingLbl.gameObject.AddComponent<LayoutElement>();
            greetingLe.preferredHeight = 22f;
            greetingLe.flexibleHeight = 0f;

            _templeGoldLabel = BywaterUiHelpers.CreateLabel(header.transform, "TempleGold", "Party Gold: 400g", _font, 16.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineRight, FontStyles.Bold, wrap: false);
            LayoutElement goldLe = _templeGoldLabel.gameObject.AddComponent<LayoutElement>();
            goldLe.preferredWidth = 165f;
            goldLe.preferredHeight = 40f;
            goldLe.flexibleHeight = 0f;

            Button closeBtn = BywaterUiHelpers.CreateButton(header.transform, "CloseTempleBtn", "Close", _font, CloseTempleModal, 15.5f);
            LayoutElement closeLe = closeBtn.gameObject.AddComponent<LayoutElement>();
            closeLe.preferredWidth = 94f;
            closeLe.preferredHeight = 40f;
            closeLe.flexibleHeight = 0f;

            // Body
            RectTransform body = BywaterUiHelpers.CreatePanel(modal, "TempleBody", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            LayoutElement bodyLe = body.gameObject.AddComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            VerticalLayoutGroup bodyVlg = body.gameObject.AddComponent<VerticalLayoutGroup>();
            bodyVlg.padding = new RectOffset(14, 14, 12, 12);
            bodyVlg.spacing = 10f;
            bodyVlg.childControlWidth = true;
            bodyVlg.childControlHeight = true;
            bodyVlg.childForceExpandWidth = true;
            bodyVlg.childForceExpandHeight = false;

            RectTransform statusBar = BywaterUiHelpers.CreatePanel(body, "PartyConditionStatus", BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1f);
            LayoutElement statusLe = statusBar.gameObject.AddComponent<LayoutElement>();
            statusLe.preferredHeight = 40f;
            statusLe.flexibleHeight = 0f;
            _templeStatusLabel = BywaterUiHelpers.CreateLabel(
                statusBar,
                "StatusText",
                "Party Status: All heroes healthy, uncursed, and relics fully charged.",
                _font,
                15.5f,
                BywaterUiHelpers.InfoCyan,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                wrap: false);
            _templeStatusLabel.overflowMode = TextOverflowModes.Truncate;
            _templeStatusLabel.rectTransform.anchorMin = Vector2.zero;
            _templeStatusLabel.rectTransform.anchorMax = Vector2.one;
            _templeStatusLabel.rectTransform.offsetMin = new Vector2(12f, 2f);
            _templeStatusLabel.rectTransform.offsetMax = new Vector2(-12f, -2f);

            _templeHealBtn = AddTempleServiceCard(
                body,
                "TempleHealCard",
                "Sacred Restoration",
                "Restores full HP and revives fallen heroes at the shrine.",
                "TempleHealBtn",
                "Blessing of Restoration (25g)",
                () => OnTempleHealRequested?.Invoke());

            _templeCureBtn = AddTempleServiceCard(
                body,
                "TempleCureCard",
                "Purification Rite",
                "Cleanses curses, exhaustion stacks, and lingering conditions.",
                "TempleCureBtn",
                "Cleanse Curse & Ailments (20g)",
                () => OnTempleCureRequested?.Invoke());

            _templeRechargeBtn = AddTempleServiceCard(
                body,
                "TempleRechargeCard",
                "Arcane Conduit",
                "Restores spent charges on enchanted staves, wands, and relics.",
                "TempleRechargeBtn",
                "Arcane Recharge (30g)",
                () => OnTempleRechargeRequested?.Invoke());
        }

        private Button AddTempleServiceCard(
            Transform parent,
            string cardName,
            string serviceTitle,
            string serviceDesc,
            string buttonName,
            string buttonLabel,
            UnityEngine.Events.UnityAction onClick)
        {
            RectTransform card = BywaterUiHelpers.CreatePanel(parent, cardName, BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1.2f);
            LayoutElement cardLe = card.gameObject.AddComponent<LayoutElement>();
            cardLe.preferredHeight = 82f;
            cardLe.flexibleHeight = 0f;

            HorizontalLayoutGroup hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(14, 14, 10, 10);
            hlg.spacing = 14f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            GameObject textCol = new GameObject("TextCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            textCol.transform.SetParent(card, false);
            LayoutElement textColLe = textCol.AddComponent<LayoutElement>();
            textColLe.flexibleWidth = 1f;
            textColLe.preferredHeight = 58f;
            textColLe.flexibleHeight = 0f;

            VerticalLayoutGroup vlg = textCol.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            TextMeshProUGUI titleLbl = BywaterUiHelpers.CreateLabel(
                textCol.transform,
                "ServiceTitle",
                serviceTitle,
                _font,
                17f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                wrap: false);
            BywaterUiHelpers.AddTextDropShadow(titleLbl);
            titleLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            TextMeshProUGUI descLbl = BywaterUiHelpers.CreateLabel(
                textCol.transform,
                "ServiceDesc",
                serviceDesc,
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.MidlineLeft,
                wrap: true);
            descLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            Button btn = BywaterUiHelpers.CreateButton(card, buttonName, buttonLabel, _font, onClick, 15.5f);
            LayoutElement btnLe = btn.gameObject.AddComponent<LayoutElement>();
            btnLe.preferredWidth = 280f;
            btnLe.preferredHeight = 46f;
            btnLe.flexibleHeight = 0f;
            return btn;
        }

        private Toggle CreateAffordableToggle(Transform parent)
        {
            RectTransform root = BywaterUiHelpers.CreatePanel(parent, "AffordableToggle", BywaterUiHelpers.ButtonBg, withBrassOutline: true, outlinePx: 1.2f);
            Toggle toggle = root.gameObject.AddComponent<Toggle>();

            RectTransform box = BywaterUiHelpers.CreatePanel(root, "CheckBox", BywaterUiHelpers.InputBg, withBrassOutline: true, outlinePx: 1f);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.anchoredPosition = new Vector2(10f, 0f);
            box.sizeDelta = new Vector2(20f, 20f);

            Image checkMark = BywaterUiHelpers.CreateImage(box, "Checkmark", null, BywaterUiHelpers.BrassHighlight, preserveAspect: false);
            checkMark.rectTransform.anchorMin = new Vector2(0.18f, 0.18f);
            checkMark.rectTransform.anchorMax = new Vector2(0.82f, 0.82f);
            checkMark.rectTransform.offsetMin = Vector2.zero;
            checkMark.rectTransform.offsetMax = Vector2.zero;

            TextMeshProUGUI lbl = BywaterUiHelpers.CreateLabel(root, "Label", "Affordable", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            lbl.rectTransform.anchorMin = Vector2.zero;
            lbl.rectTransform.anchorMax = Vector2.one;
            lbl.rectTransform.offsetMin = new Vector2(36f, 0f);
            lbl.rectTransform.offsetMax = new Vector2(-6f, 0f);

            toggle.targetGraphic = root.GetComponent<Image>();
            toggle.graphic = checkMark;
            toggle.SetIsOnWithoutNotify(false);
            return toggle;
        }

        private void BuildCombatHud(Transform parent)
        {
            _combatRoot = new GameObject("CombatHudRoot", typeof(RectTransform));
            _combatRoot.transform.SetParent(parent, false);
            RectTransform rootRt = _combatRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // 1. Top-Center Clean Initiative Ribbon (8 cards + clean R1/R2 round dividers)
            RectTransform ribbonPanel = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "InitiativeRibbon", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1.5f);
            ribbonPanel.anchorMin = new Vector2(0.5f, 1f);
            ribbonPanel.anchorMax = new Vector2(0.5f, 1f);
            ribbonPanel.pivot = new Vector2(0.5f, 1f);
            ribbonPanel.anchoredPosition = new Vector2(0f, -52f);
            ribbonPanel.sizeDelta = new Vector2(1500f, 82f);

            HorizontalLayoutGroup ribHlg = ribbonPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            ribHlg.padding = new RectOffset(8, 8, 6, 6);
            ribHlg.spacing = 8f;
            ribHlg.childControlWidth = true;
            ribHlg.childControlHeight = true;
            ribHlg.childForceExpandWidth = false;
            ribHlg.childForceExpandHeight = false;
            ribHlg.childAlignment = TextAnchor.MiddleCenter;

            _initiativeCards.Clear();
            _roundDividers.Clear();
            for (int i = 0; i < 8; i++)
            {
                if (i > 0)
                {
                    RectTransform divRt = BywaterUiHelpers.CreatePanel(
                        ribbonPanel,
                        $"RoundDivider_{i - 1}",
                        new Color(0.18f, 0.14f, 0.05f, 0.94f),
                        withBrassOutline: true,
                        outlinePx: 1.2f);
                    BywaterUiHelpers.SetBorderColor(divRt, BywaterUiHelpers.BrassAccent);
                    LayoutElement divLe = divRt.gameObject.AddComponent<LayoutElement>();
                    divLe.preferredWidth = 36f;
                    divLe.preferredHeight = 54f;

                    TextMeshProUGUI divLbl = BywaterUiHelpers.CreateLabel(
                        divRt,
                        "RoundLabel",
                        $"R{i}",
                        _font,
                        15f,
                        BywaterUiHelpers.BrassHighlight,
                        TextAlignmentOptions.Center,
                        FontStyles.Bold,
                        wrap: false);
                    divLbl.rectTransform.anchorMin = Vector2.zero;
                    divLbl.rectTransform.anchorMax = Vector2.one;
                    divLbl.rectTransform.offsetMin = Vector2.zero;
                    divLbl.rectTransform.offsetMax = Vector2.zero;
                    BywaterUiHelpers.AddTextDropShadow(divLbl);
                    divRt.gameObject.SetActive(false);

                    _roundDividers.Add(new RoundDividerRefs
                    {
                        Root = divRt.gameObject,
                        Label = divLbl
                    });
                }

                RectTransform cardRt = BywaterUiHelpers.CreatePanel(ribbonPanel, $"InitCard_{i}", BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1.5f);
                LayoutElement cardLe = cardRt.gameObject.AddComponent<LayoutElement>();
                cardLe.preferredWidth = 160f;
                cardLe.preferredHeight = 56f;

                HorizontalLayoutGroup ch = cardRt.gameObject.AddComponent<HorizontalLayoutGroup>();
                ch.padding = new RectOffset(6, 6, 4, 4);
                ch.spacing = 6f;
                ch.childControlWidth = true;
                ch.childControlHeight = true;
                ch.childForceExpandWidth = false;
                ch.childForceExpandHeight = false;
                ch.childAlignment = TextAnchor.MiddleLeft;

                BywaterUiHelpers.CreatePortraitFrame(cardRt, "PortraitFrame", 46f, out Image portrait);

                GameObject txtCol = new GameObject("TextCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
                txtCol.transform.SetParent(cardRt, false);
                txtCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
                VerticalLayoutGroup tv = txtCol.GetComponent<VerticalLayoutGroup>();
                tv.spacing = 1f;
                tv.childControlWidth = true;
                tv.childControlHeight = true;

                TextMeshProUGUI nameLbl = BywaterUiHelpers.CreateLabel(txtCol.transform, "Name", $"Unit {i + 1}", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                nameLbl.overflowMode = TextOverflowModes.Truncate;
                BywaterUiHelpers.AddTextDropShadow(nameLbl);
                TextMeshProUGUI metaLbl = BywaterUiHelpers.CreateLabel(txtCol.transform, "Meta", "HP 10/10", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, wrap: false);
                metaLbl.overflowMode = TextOverflowModes.Truncate;

                _initiativeCards.Add(new InitiativeCardRefs
                {
                    Root = cardRt,
                    Layout = cardLe,
                    Background = cardRt.GetComponent<Image>(),
                    Portrait = portrait,
                    NameText = nameLbl,
                    MetaText = metaLbl
                });
            }

            // 2. Left-Edge Compact Party Mini-Portraits
            RectTransform partyStrip = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "CombatPartyStrip", new Color(0.04f, 0.06f, 0.11f, 0.88f), withBrassOutline: true, outlinePx: 1.4f);
            partyStrip.anchorMin = new Vector2(0f, 0.5f);
            partyStrip.anchorMax = new Vector2(0f, 0.5f);
            partyStrip.pivot = new Vector2(0f, 0.5f);
            partyStrip.anchoredPosition = new Vector2(12f, 20f);
            partyStrip.sizeDelta = new Vector2(160f, 430f);

            VerticalLayoutGroup psVlg = partyStrip.gameObject.AddComponent<VerticalLayoutGroup>();
            psVlg.padding = new RectOffset(6, 6, 6, 6);
            psVlg.spacing = 6f;
            psVlg.childControlWidth = true;
            psVlg.childControlHeight = true;
            psVlg.childForceExpandWidth = true;
            psVlg.childForceExpandHeight = false;

            ContentSizeFitter psCsf = partyStrip.gameObject.AddComponent<ContentSizeFitter>();
            psCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _combatPartyCards.Clear();
            for (int i = 0; i < 6; i++)
            {
                RectTransform pCardRt = BywaterUiHelpers.CreatePanel(partyStrip, $"CombatPartyCard_{i}", BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1.3f);
                pCardRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;
                CanvasGroup cg = pCardRt.gameObject.AddComponent<CanvasGroup>();
                Button pBtn = pCardRt.gameObject.AddComponent<Button>();
                pBtn.targetGraphic = pCardRt.GetComponent<Image>();

                HorizontalLayoutGroup pcHlg = pCardRt.gameObject.AddComponent<HorizontalLayoutGroup>();
                pcHlg.padding = new RectOffset(5, 5, 4, 4);
                pcHlg.spacing = 6f;
                pcHlg.childControlWidth = true;
                pcHlg.childControlHeight = true;
                pcHlg.childForceExpandWidth = false;
                pcHlg.childForceExpandHeight = false;
                pcHlg.childAlignment = TextAnchor.MiddleLeft;

                RectTransform miniPortraitFrame = BywaterUiHelpers.CreatePortraitFrame(pCardRt, "PortraitFrame", 42f, out Image miniPortrait);
                Image[] miniConditionBadges = CreateConditionBadgeStrip(miniPortraitFrame, 16f);

                GameObject infoCol = new GameObject("InfoCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
                infoCol.transform.SetParent(pCardRt, false);
                infoCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
                VerticalLayoutGroup icVlg = infoCol.GetComponent<VerticalLayoutGroup>();
                icVlg.spacing = 3f;
                icVlg.childControlWidth = true;
                icVlg.childControlHeight = true;

                TextMeshProUGUI pName = BywaterUiHelpers.CreateLabel(infoCol.transform, "Name", $"Hero {i + 1}", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                pName.overflowMode = TextOverflowModes.Truncate;
                BywaterUiHelpers.AddTextDropShadow(pName);

                Image hpFill = BywaterUiHelpers.CreateProgressBar(infoCol.transform, "HpBar", BywaterUiHelpers.HpFillColor, out RectTransform hpBarRt);
                hpBarRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 9f;

                Image spFill = BywaterUiHelpers.CreateProgressBar(infoCol.transform, "SpBar", BywaterUiHelpers.SpFillColor, out RectTransform spBarRt);
                spBarRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 7f;

                CombatPartyCardRefs cardRef = new CombatPartyCardRefs
                {
                    Root = pCardRt.gameObject,
                    Button = pBtn,
                    CanvasGroup = cg,
                    Background = pCardRt.GetComponent<Image>(),
                    Portrait = miniPortrait,
                    ConditionBadges = miniConditionBadges,
                    NameText = pName,
                    HpFill = hpFill,
                    SpFill = spFill
                };
                pBtn.onClick.AddListener(() =>
                {
                    if (!string.IsNullOrEmpty(cardRef.BoundCombatantId))
                    {
                        OnCombatPartyAllyClicked?.Invoke(cardRef.BoundCombatantId);
                    }
                });
                _combatPartyCards.Add(cardRef);
            }

            // 3. Bottom-Right Collapsible 3-Line Combat Ticker + Expandable 40-Row History Drawer
            RectTransform tickerPanel = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "CombatTickerPanel", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.5f);
            tickerPanel.anchorMin = new Vector2(1f, 0f);
            tickerPanel.anchorMax = new Vector2(1f, 0f);
            tickerPanel.pivot = new Vector2(1f, 0f);
            tickerPanel.anchoredPosition = new Vector2(-12f, 10f);
            tickerPanel.sizeDelta = new Vector2(340f, 108f);
            _combatTickerRoot = tickerPanel.gameObject;

            VerticalLayoutGroup tkVlg = tickerPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            tkVlg.padding = new RectOffset(10, 10, 6, 6);
            tkVlg.spacing = 3f;
            tkVlg.childControlWidth = true;
            tkVlg.childControlHeight = true;
            tkVlg.childForceExpandHeight = false;

            GameObject tickerHeaderGo = new GameObject("CombatLogHeaderBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            tickerHeaderGo.transform.SetParent(tickerPanel, false);
            tickerHeaderGo.GetComponent<Image>().color = new Color(0.11f, 0.15f, 0.24f, 0.92f);
            tickerHeaderGo.AddComponent<LayoutElement>().preferredHeight = 26f;
            Button tickerBtn = tickerHeaderGo.GetComponent<Button>();
            tickerBtn.targetGraphic = tickerHeaderGo.GetComponent<Image>();
            tickerBtn.onClick.AddListener(() => ToggleCombatLogDrawer());

            HorizontalLayoutGroup thHlg = tickerHeaderGo.AddComponent<HorizontalLayoutGroup>();
            thHlg.padding = new RectOffset(8, 8, 1, 1);
            thHlg.spacing = 6f;
            thHlg.childControlWidth = true;
            thHlg.childControlHeight = true;
            thHlg.childForceExpandWidth = false;

            TextMeshProUGUI tickerTitle = BywaterUiHelpers.CreateLabel(tickerHeaderGo.transform, "TickerTitle", "Combat Log [L]", _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(tickerTitle);
            tickerTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _tickerToggleBadge = BywaterUiHelpers.CreateLabel(tickerHeaderGo.transform, "TickerToggleBadge", "[+]", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineRight, FontStyles.Bold, wrap: false);
            _tickerToggleBadge.gameObject.AddComponent<LayoutElement>().preferredWidth = 34f;

            _tickerLines.Clear();
            for (int i = 0; i < 3; i++)
            {
                TextMeshProUGUI line = BywaterUiHelpers.CreateLabel(tickerPanel, $"TickerLine_{i}", string.Empty, _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, wrap: false);
                line.overflowMode = TextOverflowModes.Truncate;
                line.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
                _tickerLines.Add(line);
            }

            RectTransform drawerPanel = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "CombatLogDrawer", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.6f);
            drawerPanel.anchorMin = new Vector2(1f, 0f);
            drawerPanel.anchorMax = new Vector2(1f, 0f);
            drawerPanel.pivot = new Vector2(1f, 0f);
            drawerPanel.anchoredPosition = new Vector2(-12f, 126f);
            drawerPanel.sizeDelta = new Vector2(400f, 360f);
            _combatLogDrawerRoot = drawerPanel.gameObject;

            VerticalLayoutGroup drVlg = drawerPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            drVlg.padding = new RectOffset(10, 10, 8, 8);
            drVlg.spacing = 6f;
            drVlg.childControlWidth = true;
            drVlg.childControlHeight = true;
            drVlg.childForceExpandHeight = false;

            GameObject drHeader = new GameObject("DrawerHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            drHeader.transform.SetParent(drawerPanel, false);
            drHeader.AddComponent<LayoutElement>().preferredHeight = 30f;
            HorizontalLayoutGroup drhHlg = drHeader.GetComponent<HorizontalLayoutGroup>();
            drhHlg.spacing = 6f;
            drhHlg.childControlWidth = true;
            drhHlg.childControlHeight = true;
            drhHlg.childForceExpandWidth = false;

            TextMeshProUGUI logHeader = BywaterUiHelpers.CreateLabel(drHeader.transform, "LogHeader", "Combat History [L]", _font, 16f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(logHeader);
            logHeader.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Button closeLogBtn = BywaterUiHelpers.CreateButton(drHeader.transform, "CloseLogBtn", "Close", _font, () => ToggleCombatLogDrawer(false), 15f);
            closeLogBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

            _combatLogScroll = BywaterUiHelpers.CreateScrollView(drawerPanel, "CombatLogScroll", _font, spacing: 5f);
            _combatLogScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            _combatLogRows.Clear();
            for (int i = 0; i < MaxLogLines; i++)
            {
                TextMeshProUGUI lbl = BywaterUiHelpers.CreateLabel(
                    _combatLogScroll.content,
                    $"Log_{i}",
                    string.Empty,
                    _font,
                    15f,
                    BywaterUiHelpers.ParchmentText,
                    TextAlignmentOptions.TopLeft,
                    wrap: true);
                ContentSizeFitter csf = lbl.gameObject.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                lbl.gameObject.SetActive(false);
                _combatLogRows.Add(lbl);
            }
            _logRingHead = 0;
            _combatLogDrawerRoot.SetActive(false);

            // 4. Compact Cursor-Following Floating Tooltip (hidden when idle, zero raycasts)
            RectTransform tooltipPanel = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "CursorTooltipPanel", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.6f);
            tooltipPanel.anchorMin = new Vector2(0.5f, 0.5f);
            tooltipPanel.anchorMax = new Vector2(0.5f, 0.5f);
            tooltipPanel.pivot = new Vector2(0f, 1f);
            tooltipPanel.anchoredPosition = new Vector2(-420f, 180f);
            tooltipPanel.sizeDelta = new Vector2(360f, 150f);
            _tooltipPanel = tooltipPanel;
            _cursorTooltipRoot = tooltipPanel.gameObject;

            CanvasGroup tipCg = tooltipPanel.gameObject.AddComponent<CanvasGroup>();
            tipCg.blocksRaycasts = false;
            tipCg.interactable = false;

            VerticalLayoutGroup ttVlg = tooltipPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            ttVlg.padding = new RectOffset(12, 12, 10, 10);
            ttVlg.spacing = 6f;
            ttVlg.childControlWidth = true;
            ttVlg.childControlHeight = true;
            ttVlg.childForceExpandHeight = false;

            ContentSizeFitter ttCsf = tooltipPanel.gameObject.AddComponent<ContentSizeFitter>();
            ttCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            ttCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _tooltipTitle = BywaterUiHelpers.CreateLabel(tooltipPanel, "TooltipTitle", "Tactical Preview", _font, 17f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _tooltipTitle.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_tooltipTitle);
            _tooltipTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            _tooltipBadgeRoot = BywaterUiHelpers.CreatePanel(
                tooltipPanel,
                "TooltipBadge",
                new Color(0.26f, 0.20f, 0.05f, 0.96f),
                withBrassOutline: true,
                outlinePx: 1f);
            _tooltipBadgeBg = _tooltipBadgeRoot.GetComponent<Image>();
            _tooltipBadgeRoot.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            _tooltipBadgeText = BywaterUiHelpers.CreateLabel(
                _tooltipBadgeRoot,
                "TooltipBadgeText",
                string.Empty,
                _font,
                15f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            _tooltipBadgeText.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_tooltipBadgeText);
            _tooltipBadgeText.rectTransform.anchorMin = Vector2.zero;
            _tooltipBadgeText.rectTransform.anchorMax = Vector2.one;
            _tooltipBadgeText.rectTransform.offsetMin = new Vector2(6f, 0f);
            _tooltipBadgeText.rectTransform.offsetMax = new Vector2(-6f, 0f);
            _tooltipBadgeRoot.gameObject.SetActive(false);

            _tooltipBody = BywaterUiHelpers.CreateLabel(tooltipPanel, "TooltipBody", string.Empty, _font, 15.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.TopLeft, wrap: true);
            ContentSizeFitter bodyCsf = _tooltipBody.gameObject.AddComponent<ContentSizeFitter>();
            bodyCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BywaterUiHelpers.SetRaycastTargetRecursive(tooltipPanel, false);
            _cursorTooltipRoot.SetActive(false);

            // Legacy inactive SpellPickerBar container (0 children) preserved for Editor test contract
            GameObject legacySpellBar = new GameObject("SpellPickerBar", typeof(RectTransform));
            legacySpellBar.transform.SetParent(_combatRoot.transform, false);
            _spellPickerBar = legacySpellBar;
            _spellPickerBar.SetActive(false);

            // 5. Bottom Tactical Action Bar (Visual Action Pips + Contextual Icon Hotbar 1-6 + Skills)
            RectTransform actionBar = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "TacticalActionBar", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.8f);
            actionBar.anchorMin = new Vector2(0.5f, 0f);
            actionBar.anchorMax = new Vector2(0.5f, 0f);
            actionBar.pivot = new Vector2(0.5f, 0f);
            actionBar.anchoredPosition = new Vector2(-180f, 10f);
            actionBar.sizeDelta = new Vector2(1200f, 88f);

            HorizontalLayoutGroup abHlg = actionBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            abHlg.padding = new RectOffset(10, 10, 6, 6);
            abHlg.spacing = 6f;
            abHlg.childControlWidth = true;
            abHlg.childControlHeight = true;
            abHlg.childForceExpandWidth = false;
            abHlg.childAlignment = TextAnchor.MiddleLeft;

            ContentSizeFitter abCsf = actionBar.gameObject.AddComponent<ContentSizeFitter>();
            abCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            abCsf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            BywaterUiHelpers.CreatePortraitFrame(actionBar, "ActorPortraitFrame", 74f, out _combatActorPortrait);

            GameObject actorInfo = new GameObject("ActorInfo", typeof(RectTransform), typeof(VerticalLayoutGroup));
            actorInfo.transform.SetParent(actionBar, false);
            actorInfo.AddComponent<LayoutElement>().preferredWidth = 228f;
            VerticalLayoutGroup aiVlg = actorInfo.GetComponent<VerticalLayoutGroup>();
            aiVlg.spacing = 2f;
            aiVlg.childControlWidth = true;
            aiVlg.childControlHeight = true;

            _combatActorName = BywaterUiHelpers.CreateLabel(actorInfo.transform, "ActorName", "Aldric", _font, 18f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _combatActorName.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_combatActorName);
            _combatActorVitals = BywaterUiHelpers.CreateLabel(actorInfo.transform, "ActorVitals", "HP 17/17 | SP 0/0 | AC 29", _font, 15.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, wrap: false);
            _combatActorVitals.overflowMode = TextOverflowModes.Truncate;

            // BG3-style Visual Action Pips Row
            GameObject pipsRow = new GameObject("PipsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            pipsRow.transform.SetParent(actorInfo.transform, false);
            pipsRow.AddComponent<LayoutElement>().preferredHeight = 24f;
            HorizontalLayoutGroup pHlg = pipsRow.GetComponent<HorizontalLayoutGroup>();
            pHlg.spacing = 4f;
            pHlg.childControlWidth = true;
            pHlg.childControlHeight = true;
            pHlg.childForceExpandWidth = false;
            pHlg.childAlignment = TextAnchor.MiddleLeft;

            GameObject moveSegGroup = new GameObject("MoveSegments", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            moveSegGroup.transform.SetParent(pipsRow.transform, false);
            moveSegGroup.AddComponent<LayoutElement>().preferredWidth = 60f;
            HorizontalLayoutGroup msHlg = moveSegGroup.GetComponent<HorizontalLayoutGroup>();
            msHlg.spacing = 2f;
            msHlg.childControlWidth = true;
            msHlg.childControlHeight = true;
            msHlg.childForceExpandWidth = true;
            msHlg.childAlignment = TextAnchor.MiddleLeft;

            _movePipSegments.Clear();
            for (int i = 0; i < 6; i++)
            {
                Image seg = BywaterUiHelpers.CreateImage(moveSegGroup.transform, $"MoveSeg_{i}", null, new Color(0.18f, 0.84f, 0.44f, 1f), preserveAspect: false);
                seg.raycastTarget = false;
                LayoutElement segLe = seg.gameObject.AddComponent<LayoutElement>();
                segLe.preferredWidth = 8f;
                segLe.preferredHeight = 14f;
                _movePipSegments.Add(seg);
            }

            _combatMovePip = BywaterUiHelpers.CreateLabel(pipsRow.transform, "MovePip", "6/6", _font, 15f, new Color(0.18f, 0.84f, 0.44f, 1f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _combatMovePip.gameObject.AddComponent<LayoutElement>().preferredWidth = 34f;

            _combatStdPipIcon = BywaterUiHelpers.CreateImage(pipsRow.transform, "StdPipIcon", BywaterUiHelpers.GetProceduralIconSprite("circle"), new Color(0.98f, 0.80f, 0.08f, 1f), preserveAspect: true);
            _combatStdPipIcon.preserveAspect = true;
            _combatStdPipIcon.raycastTarget = false;
            LayoutElement stdIconLe = _combatStdPipIcon.gameObject.AddComponent<LayoutElement>();
            stdIconLe.preferredWidth = 17f;
            stdIconLe.preferredHeight = 17f;
            _combatStdPip = BywaterUiHelpers.CreateLabel(pipsRow.transform, "StdPip", "Act", _font, 15f, new Color(0.98f, 0.80f, 0.08f, 1f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _combatStdPip.gameObject.AddComponent<LayoutElement>().preferredWidth = 32f;

            _combatSwiftPipIcon = BywaterUiHelpers.CreateImage(pipsRow.transform, "SwiftPipIcon", BywaterUiHelpers.GetProceduralIconSprite("diamond"), new Color(0.22f, 0.74f, 0.97f, 1f), preserveAspect: true);
            _combatSwiftPipIcon.preserveAspect = true;
            _combatSwiftPipIcon.raycastTarget = false;
            LayoutElement swiftIconLe = _combatSwiftPipIcon.gameObject.AddComponent<LayoutElement>();
            swiftIconLe.preferredWidth = 17f;
            swiftIconLe.preferredHeight = 17f;
            _combatSwiftPip = BywaterUiHelpers.CreateLabel(pipsRow.transform, "SwiftPip", "Swf", _font, 15f, new Color(0.22f, 0.74f, 0.97f, 1f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _combatSwiftPip.gameObject.AddComponent<LayoutElement>().preferredWidth = 34f;

            // Contextual Hotbar Slots (pooled 0..7)
            _hotbarSlots.Clear();
            for (int i = 0; i < MaxHotbarSlots; i++)
            {
                _hotbarSlots.Add(CreateHotbarSlot(actionBar, i));
            }

            // Fixed Tactical Skills (MoveBtn removed; UndoMove only when CanUndoMove; W-234 32x32 pixel icons)
            _combatUndoBtn = AddCombatSkillButton(actionBar, "UndoMoveBtn", "undo", "Undo", 80f, () => OnUndoMoveRequested?.Invoke(), out _combatUndoBtnIcon);
            AttachHoverTooltip(_combatUndoBtn.gameObject, "undo_move", "Undo Move", "Rewind your last movement step this turn.");

            _combatPotionBtn = AddCombatSkillButton(actionBar, "PotionBtn", "potion", "Potion", 84f, () => OnCombatModeSelected?.Invoke(CombatActionMode.Potion), out _combatPotionBtnIcon);
            AttachHoverTooltip(_combatPotionBtn.gameObject, "potion", "Healing Potion [Swift]", "Drink or throw a healing potion. Uses Swift action.");

            _combatShoveBtn = AddShoveCombatButton(actionBar, "ShoveBtn", 124f, () => OnCombatModeSelected?.Invoke(CombatActionMode.Shove), out _combatShoveBtnIcon);
            AttachHoverTooltip(_combatShoveBtn.gameObject, "shove", "Shove [V] [Swift]", "Push an adjacent enemy 1 tile (off ledges or into walls). Uses Swift action.");

            _combatGuardBtn = AddCombatSkillButton(actionBar, "GuardBtn", "guard", "Guard", 82f, () => OnGuardRequested?.Invoke(), out _combatGuardBtnIcon);
            AttachHoverTooltip(_combatGuardBtn.gameObject, "guard", "Guard / Overwatch [G]", "+4 AC until next turn and prepare reaction strike. Uses Standard action.");

            _combatEndTurnBtn = AddCombatSkillButton(actionBar, "EndTurnBtn", "end_turn", "End Turn", 104f, () => OnEndTurnRequested?.Invoke(), out _combatEndTurnBtnIcon);
            AttachHoverTooltip(_combatEndTurnBtn.gameObject, "end_turn", "End Turn [Space]", "Finish the current hero's turn and advance initiative.");

            // Compact non-blocking targeting banner above action bar when in Melee / Ranged / Spell / Shove / Potion mode
            RectTransform bannerRt = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "TargetingModeBanner", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.4f);
            bannerRt.anchorMin = new Vector2(0.5f, 0f);
            bannerRt.anchorMax = new Vector2(0.5f, 0f);
            bannerRt.pivot = new Vector2(0.5f, 0f);
            bannerRt.anchoredPosition = new Vector2(-180f, 104f);
            bannerRt.sizeDelta = new Vector2(680f, 34f);
            _targetingBannerRoot = bannerRt.gameObject;
            _targetingBannerBg = bannerRt.GetComponent<Image>();

            _targetingBannerText = BywaterUiHelpers.CreateLabel(
                bannerRt,
                "BannerText",
                string.Empty,
                _font,
                15f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            _targetingBannerText.overflowMode = TextOverflowModes.Truncate;
            _targetingBannerText.rectTransform.anchorMin = Vector2.zero;
            _targetingBannerText.rectTransform.anchorMax = Vector2.one;
            _targetingBannerText.rectTransform.offsetMin = new Vector2(12f, 2f);
            _targetingBannerText.rectTransform.offsetMax = new Vector2(-12f, -2f);
            BywaterUiHelpers.AddTextDropShadow(_targetingBannerText);
            BywaterUiHelpers.SetRaycastTargetRecursive(bannerRt, false);
            _targetingBannerRoot.SetActive(false);
        }

        private HotbarSlotRefs CreateHotbarSlot(Transform parent, int index)
        {
            GameObject go = new GameObject($"HotbarSlot_{index}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image bg = go.GetComponent<Image>();
            bg.color = BywaterUiHelpers.ButtonBg;
            BywaterUiHelpers.AddBrassBorder(go.GetComponent<RectTransform>(), BywaterUiHelpers.BrassAccent, 1.3f);

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.12f, 0.96f, 1f);
            colors.pressedColor = new Color(0.82f, 0.78f, 0.70f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.58f, 0.65f);
            colors.colorMultiplier = 1f;
            btn.colors = colors;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 96f;
            le.preferredHeight = 68f;

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 3, 3);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            GameObject topRow = new GameObject("TopRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            topRow.transform.SetParent(go.transform, false);
            topRow.AddComponent<LayoutElement>().preferredHeight = 34f;
            HorizontalLayoutGroup topHlg = topRow.GetComponent<HorizontalLayoutGroup>();
            topHlg.spacing = 2f;
            topHlg.childControlWidth = true;
            topHlg.childControlHeight = true;
            topHlg.childForceExpandWidth = false;
            topHlg.childAlignment = TextAnchor.MiddleCenter;

            TextMeshProUGUI hotkeyLbl = BywaterUiHelpers.CreateLabel(topRow.transform, "Hotkey", (index + 1).ToString(), _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.TopLeft, FontStyles.Bold, wrap: false);
            hotkeyLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;

            Image iconImg = BywaterUiHelpers.CreateImage(topRow.transform, "Icon", BywaterUiHelpers.GetProceduralIconSprite("sword"), Color.white, preserveAspect: true);
            iconImg.raycastTarget = false;
            LayoutElement iconLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 28f;
            iconLe.preferredHeight = 28f;

            TextMeshProUGUI spLbl = BywaterUiHelpers.CreateLabel(topRow.transform, "SpCost", string.Empty, _font, 15f, BywaterUiHelpers.InfoCyan, TextAlignmentOptions.TopRight, FontStyles.Bold, wrap: false);
            spLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 32f;

            TextMeshProUGUI nameLbl = BywaterUiHelpers.CreateLabel(go.transform, "SkillLabel", string.Empty, _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
            nameLbl.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(nameLbl);
            nameLbl.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            HotbarSlotRefs slotRef = new HotbarSlotRefs
            {
                Root = go,
                Button = btn,
                Background = bg,
                Icon = iconImg,
                HotkeyText = hotkeyLbl,
                LabelText = nameLbl,
                SpText = spLbl,
                Layout = le
            };

            btn.onClick.AddListener(() =>
            {
                if (slotRef.BoundMode == CombatActionMode.Spell && slotRef.BoundSpellId >= 0)
                {
                    OnSpellSelected?.Invoke(slotRef.BoundSpellId);
                }
                else
                {
                    OnCombatModeSelected?.Invoke(slotRef.BoundMode);
                }
            });

            HoverRelay relay = go.AddComponent<HoverRelay>();
            relay.OnEnter = () =>
            {
                if (!string.IsNullOrEmpty(slotRef.TooltipTitle))
                {
                    OnHotbarSlotHovered?.Invoke(slotRef.ActionId, slotRef.TooltipTitle, slotRef.TooltipBody);
                    SetCombatHoverPreview(slotRef.TooltipTitle, slotRef.TooltipBody, null);
                }
            };
            relay.OnExit = () =>
            {
                OnHotbarSlotHoverExited?.Invoke();
                ClearCombatHoverPreview();
            };

            go.SetActive(false);
            return slotRef;
        }

        private void AttachHoverTooltip(GameObject target, string actionId, string title, string body)
        {
            if (target == null)
            {
                return;
            }

            HoverRelay relay = target.AddComponent<HoverRelay>();
            relay.OnEnter = () =>
            {
                OnHotbarSlotHovered?.Invoke(actionId, title, body);
                SetCombatHoverPreview(title, body, null);
            };
            relay.OnExit = () =>
            {
                OnHotbarSlotHoverExited?.Invoke();
                ClearCombatHoverPreview();
            };
        }

        private Button AddShoveCombatButton(
            Transform parent,
            string name,
            float width,
            UnityEngine.Events.UnityAction onClick,
            out Image iconImg)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = BywaterUiHelpers.ButtonBg;

            RectTransform btnRt = go.GetComponent<RectTransform>();
            BywaterUiHelpers.AddBrassBorder(btnRt, BywaterUiHelpers.BrassAccent, 1.2f);

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.12f, 0.96f, 1f);
            colors.pressedColor = new Color(0.82f, 0.78f, 0.70f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.58f, 0.65f);
            colors.colorMultiplier = 1f;
            btn.colors = colors;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 68f;

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 3, 3);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            GameObject topRow = new GameObject("TopRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            topRow.transform.SetParent(go.transform, false);
            topRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            HorizontalLayoutGroup topHlg = topRow.GetComponent<HorizontalLayoutGroup>();
            topHlg.spacing = 4f;
            topHlg.childControlWidth = true;
            topHlg.childControlHeight = true;
            topHlg.childForceExpandWidth = false;
            topHlg.childForceExpandHeight = false;
            topHlg.childAlignment = TextAnchor.MiddleCenter;

            iconImg = BywaterUiHelpers.CreateImage(topRow.transform, "Icon", LoadHotbarIcon("shove"), Color.white, preserveAspect: true);
            iconImg.raycastTarget = false;
            LayoutElement iconLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 26f;
            iconLe.preferredHeight = 26f;
            iconLe.minWidth = 26f;
            iconLe.minHeight = 26f;
            iconLe.flexibleWidth = 0f;

            RectTransform badgeRt = BywaterUiHelpers.CreatePanel(
                topRow.transform,
                "SwiftBadge",
                new Color(0.32f, 0.22f, 0.04f, 0.96f),
                withBrassOutline: true,
                outlinePx: 1f);
            _combatShoveSwiftBadgeBg = badgeRt.GetComponent<Image>();
            _combatShoveSwiftBadgeBg.raycastTarget = false;
            BywaterUiHelpers.SetBorderColor(badgeRt, BywaterUiHelpers.BrassHighlight);
            LayoutElement badgeLe = badgeRt.gameObject.AddComponent<LayoutElement>();
            badgeLe.preferredWidth = 76f;
            badgeLe.minWidth = 76f;
            badgeLe.flexibleWidth = 0f;
            badgeLe.preferredHeight = 22f;

            _combatShoveSwiftBadgeText = BywaterUiHelpers.CreateLabel(
                badgeRt,
                "SwiftBadgeLabel",
                "[SWIFT]",
                _font,
                15f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_combatShoveSwiftBadgeText);
            _combatShoveSwiftBadgeText.rectTransform.anchorMin = Vector2.zero;
            _combatShoveSwiftBadgeText.rectTransform.anchorMax = Vector2.one;
            _combatShoveSwiftBadgeText.rectTransform.offsetMin = Vector2.zero;
            _combatShoveSwiftBadgeText.rectTransform.offsetMax = Vector2.zero;

            TextMeshProUGUI mainLbl = BywaterUiHelpers.CreateLabel(
                go.transform,
                "Label",
                "Shove [V]",
                _font,
                15f,
                BywaterUiHelpers.ParchmentText,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            BywaterUiHelpers.AddTextDropShadow(mainLbl);
            mainLbl.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            return btn;
        }

        private Button AddCombatSkillButton(
            Transform parent,
            string name,
            string iconKey,
            string label,
            float width,
            UnityEngine.Events.UnityAction onClick,
            out Image iconImg)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image bg = go.GetComponent<Image>();
            bg.color = BywaterUiHelpers.ButtonBg;
            BywaterUiHelpers.AddBrassBorder(go.GetComponent<RectTransform>(), BywaterUiHelpers.BrassAccent, 1.2f);

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.12f, 0.96f, 1f);
            colors.pressedColor = new Color(0.82f, 0.78f, 0.70f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.58f, 0.65f);
            colors.colorMultiplier = 1f;
            btn.colors = colors;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 68f;

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 3);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            iconImg = BywaterUiHelpers.CreateImage(go.transform, "Icon", LoadHotbarIcon(iconKey), Color.white, preserveAspect: true);
            iconImg.raycastTarget = false;
            LayoutElement iconLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 28f;
            iconLe.preferredHeight = 28f;
            iconLe.minWidth = 28f;
            iconLe.minHeight = 28f;

            TextMeshProUGUI lbl = BywaterUiHelpers.CreateLabel(
                go.transform,
                "Label",
                label,
                _font,
                15f,
                BywaterUiHelpers.ParchmentText,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            lbl.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(lbl);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            return btn;
        }

        private Button AddCombatButton(Transform parent, string name, string label, float width, UnityEngine.Events.UnityAction onClick)
        {
            Button btn = BywaterUiHelpers.CreateButton(parent, name, label, _font, onClick, 15f);
            LayoutElement le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 60f;
            return btn;
        }

        private void BuildSettingsModal(Transform parent)
        {
            RectTransform modal = BywaterUiHelpers.CreatePanel(parent, "SettingsModal", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 2.2f);
            modal.anchorMin = new Vector2(0.5f, 0.5f);
            modal.anchorMax = new Vector2(0.5f, 0.5f);
            modal.pivot = new Vector2(0.5f, 0.5f);
            modal.anchoredPosition = Vector2.zero;
            modal.sizeDelta = new Vector2(760f, 700f);
            _settingsModalRoot = modal.gameObject;

            VerticalLayoutGroup vlg = modal.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(22, 22, 18, 18);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            GameObject header = new GameObject("SettingsHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            header.transform.SetParent(modal, false);
            header.AddComponent<LayoutElement>().preferredHeight = 40f;
            HorizontalLayoutGroup hHlg = header.GetComponent<HorizontalLayoutGroup>();
            hHlg.spacing = 10f;
            hHlg.childControlWidth = true;
            hHlg.childControlHeight = true;
            hHlg.childForceExpandWidth = false;

            TextMeshProUGUI title = BywaterUiHelpers.CreateLabel(header.transform, "Title", "Game Options & Pause Menu", _font, 21f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(title);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Button closeBtn = BywaterUiHelpers.CreateButton(header.transform, "CloseSettingsBtn", "Resume [Esc]", _font, CloseSettingsModal, 15.5f);
            closeBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 136f;

            // Toggles row 1: Auto-Combat & Auto-End Turn
            _settingsAutoCombatBtn = AddSettingsToggleRow(modal, "AutoCombatRow", "Auto-Combat [A]", out _settingsAutoCombatLabel, () =>
            {
                bool next = !_isAutoCombatOn;
                SetAutoCombatVisual(next);
                OnAutoCombatToggled?.Invoke(next);
            });

            _settingsAutoEndTurnBtn = AddSettingsToggleRow(modal, "AutoEndTurnRow", "Auto-End Turn (When No Actions)", out _settingsAutoEndTurnLabel, () =>
            {
                _isAutoEndTurnOn = !_isAutoEndTurnOn;
                RefreshSettingsModalControls();
                OnAutoEndTurnToggled?.Invoke(_isAutoEndTurnOn);
            });

            // Battle Speed Selector
            GameObject speedRow = CreateSettingsRow(modal, "BattleSpeedRow", "Battle Speed");
            string[] speedLabels = { "1x", "2x", "Instant" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                Button spBtn = BywaterUiHelpers.CreateButton(speedRow.transform, $"SpeedOpt_{i}", speedLabels[i], _font, () =>
                {
                    _battleSpeedIndex = idx;
                    RefreshSettingsModalControls();
                    OnBattleSpeedSelected?.Invoke(idx);
                }, 15f);
                spBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 98f;
                _settingsSpeedBtns[i] = spBtn;
            }

            // Audio Sliders + Mute
            _settingsMasterSlider = AddSettingsSliderRow(modal, "MasterVolRow", "Master Volume", 0.8f, out _settingsMasterValueText, val =>
            {
                if (!_suppressSettingsCallbacks) OnMasterVolumeChanged?.Invoke(val);
            });
            _settingsMusicSlider = AddSettingsSliderRow(modal, "MusicVolRow", "Music Volume", 0.7f, out _settingsMusicValueText, val =>
            {
                if (!_suppressSettingsCallbacks) OnMusicVolumeChanged?.Invoke(val);
            });
            _settingsSfxSlider = AddSettingsSliderRow(modal, "SfxVolRow", "SFX Volume", 0.85f, out _settingsSfxValueText, val =>
            {
                if (!_suppressSettingsCallbacks) OnSfxVolumeChanged?.Invoke(val);
            });

            _settingsMuteBtn = AddSettingsToggleRow(modal, "MuteRow", "Sound", out _settingsMuteLabel, () =>
            {
                _isMuted = !_isMuted;
                UpdateCombatAudioPillVisual();
                RefreshSettingsModalControls();
                OnMuteToggled?.Invoke(_isMuted);
            });

            // Tactical Grid Opacity
            _settingsGridOpacitySlider = AddSettingsSliderRow(modal, "GridOpacityRow", "Tactical Grid Opacity", 0.7f, out _settingsGridOpacityValueText, val =>
            {
                if (!_suppressSettingsCallbacks) OnGridOpacityChanged?.Invoke(val);
            });

            // Nameplates Mode & Dev Tools
            _settingsNameplatesBtn = AddSettingsToggleRow(modal, "NameplatesRow", "Unit Nameplates", out _settingsNameplatesLabel, () =>
            {
                _nameplatesAlwaysOn = !_nameplatesAlwaysOn;
                RefreshSettingsModalControls();
                OnNameplatesAlwaysOnChanged?.Invoke(_nameplatesAlwaysOn);
            });

            _settingsDevToolsBtn = AddSettingsToggleRow(modal, "DevToolsRow", "Developer Tools [F12]", out _settingsDevToolsLabel, () =>
            {
                bool next = !_isDevToolsVisible;
                SetDevToolsVisible(next);
                OnDevToolsToggled?.Invoke(next);
            });

            // Actions Row
            GameObject actionRow = new GameObject("SettingsActionsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            actionRow.transform.SetParent(modal, false);
            actionRow.AddComponent<LayoutElement>().preferredHeight = 46f;
            HorizontalLayoutGroup arHlg = actionRow.GetComponent<HorizontalLayoutGroup>();
            arHlg.spacing = 10f;
            arHlg.childControlWidth = true;
            arHlg.childControlHeight = true;
            arHlg.childForceExpandWidth = true;

            _settingsSaveBtn = BywaterUiHelpers.CreateButton(actionRow.transform, "SettingsSaveBtn", "Save (F5)", _font, () =>
            {
                CloseSettingsModal();
                OnSaveRequested?.Invoke();
            }, 15.5f);
            _settingsLoadBtn = BywaterUiHelpers.CreateButton(actionRow.transform, "SettingsLoadBtn", "Load (F9)", _font, () =>
            {
                CloseSettingsModal();
                OnLoadRequested?.Invoke();
            }, 15.5f);
            _settingsFleeBtn = BywaterUiHelpers.CreateButton(actionRow.transform, "SettingsFleeBtn", "Flee / Retreat", _font, () =>
            {
                CloseSettingsModal();
                OnFleeCombatRequested?.Invoke();
            }, 15.5f);
            AttachHorizontalIconToButton(_settingsFleeBtn, "flee", 20f);
            _settingsQuitBtn = BywaterUiHelpers.CreateButton(actionRow.transform, "SettingsQuitBtn", "Quit to Desktop", _font, HandleQuitButtonClicked, 15.5f);
            _settingsQuitBtnLabel = _settingsQuitBtn.GetComponentInChildren<TextMeshProUGUI>();

            // Keybinds Legend (15pt floor)
            RectTransform legendBox = BywaterUiHelpers.CreatePanel(modal, "KeybindsLegend", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            legendBox.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;
            TextMeshProUGUI legend = BywaterUiHelpers.CreateLabel(
                legendBox,
                "LegendText",
                "Shortcuts: [1-6] Abilities | [V] Shove | [G] Guard | [Space] End Turn | [A] Auto-Combat\n[L] Combat Log | [Alt] Nameplates | [P] Party | [C] Camp | [F5/F9] Save/Load | [F12] Dev | [Esc] Menu",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.Center,
                wrap: true);
            legend.rectTransform.anchorMin = Vector2.zero;
            legend.rectTransform.anchorMax = Vector2.one;
            legend.rectTransform.offsetMin = new Vector2(10f, 4f);
            legend.rectTransform.offsetMax = new Vector2(-10f, -4f);

            RefreshSettingsModalControls();
        }

        private GameObject CreateSettingsRow(Transform parent, string name, string label)
        {
            RectTransform row = BywaterUiHelpers.CreatePanel(parent, name, BywaterUiHelpers.CardBg, withBrassOutline: false);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 12, 4, 4);
            hlg.spacing = 10f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;

            TextMeshProUGUI lbl = BywaterUiHelpers.CreateLabel(row, "RowLabel", label, _font, 15.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return row.gameObject;
        }

        private Button AddSettingsToggleRow(Transform parent, string name, string label, out TextMeshProUGUI valueLabel, UnityEngine.Events.UnityAction onClick)
        {
            GameObject row = CreateSettingsRow(parent, name, label);
            Button btn = BywaterUiHelpers.CreateButton(row.transform, "ToggleBtn", "OFF", _font, onClick, 15f);
            btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 178f;
            valueLabel = btn.GetComponentInChildren<TextMeshProUGUI>();
            return btn;
        }

        private Slider AddSettingsSliderRow(
            Transform parent,
            string name,
            string label,
            float initialValue,
            out TextMeshProUGUI valueText,
            UnityEngine.Events.UnityAction<float> onChanged)
        {
            GameObject row = CreateSettingsRow(parent, name, label);
            TextMeshProUGUI pctLbl = BywaterUiHelpers.CreateLabel(row.transform, "PctValue", $"{Mathf.RoundToInt(initialValue * 100f)}%", _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineRight, FontStyles.Bold, wrap: false);
            pctLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

            Slider slider = BywaterUiHelpers.CreateSlider(row.transform, "Slider", 0f, 1f, initialValue, val =>
            {
                pctLbl.text = $"{Mathf.RoundToInt(val * 100f)}%";
                onChanged?.Invoke(val);
            });
            slider.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;
            pctLbl.transform.SetAsLastSibling();
            valueText = pctLbl;
            return slider;
        }

        private void RefreshSettingsModalControls()
        {
            if (!_uiBuilt || _settingsModalRoot == null)
            {
                return;
            }

            if (_settingsAutoCombatLabel != null) _settingsAutoCombatLabel.text = _isAutoCombatOn ? "ON" : "OFF";
            if (_settingsAutoCombatBtn != null) BywaterUiHelpers.SetButtonColors(_settingsAutoCombatBtn, _isAutoCombatOn);

            if (_settingsAutoEndTurnLabel != null) _settingsAutoEndTurnLabel.text = _isAutoEndTurnOn ? "ON" : "OFF";
            if (_settingsAutoEndTurnBtn != null) BywaterUiHelpers.SetButtonColors(_settingsAutoEndTurnBtn, _isAutoEndTurnOn);

            for (int i = 0; i < _settingsSpeedBtns.Length; i++)
            {
                if (_settingsSpeedBtns[i] != null)
                {
                    BywaterUiHelpers.SetButtonColors(_settingsSpeedBtns[i], i == _battleSpeedIndex);
                }
            }

            if (_settingsMasterValueText != null && _settingsMasterSlider != null)
            {
                _settingsMasterValueText.text = $"{Mathf.RoundToInt(_settingsMasterSlider.value * 100f)}%";
            }
            if (_settingsMusicValueText != null && _settingsMusicSlider != null)
            {
                _settingsMusicValueText.text = $"{Mathf.RoundToInt(_settingsMusicSlider.value * 100f)}%";
            }
            if (_settingsSfxValueText != null && _settingsSfxSlider != null)
            {
                _settingsSfxValueText.text = $"{Mathf.RoundToInt(_settingsSfxSlider.value * 100f)}%";
            }
            if (_settingsGridOpacityValueText != null && _settingsGridOpacitySlider != null)
            {
                _settingsGridOpacityValueText.text = $"{Mathf.RoundToInt(_settingsGridOpacitySlider.value * 100f)}%";
            }

            if (_settingsMuteLabel != null)
            {
                _settingsMuteLabel.text = _isMuted ? "MUTED" : "ON";
                _settingsMuteLabel.color = _isMuted ? BywaterUiHelpers.DangerRed : BywaterUiHelpers.BrassHighlight;
            }
            if (_settingsMuteBtn != null)
            {
                BywaterUiHelpers.SetButtonColors(_settingsMuteBtn, !_isMuted);
                if (_isMuted)
                {
                    Image muteBg = _settingsMuteBtn.GetComponent<Image>();
                    if (muteBg != null)
                    {
                        muteBg.color = new Color(0.24f, 0.09f, 0.11f, 0.96f);
                    }
                    BywaterUiHelpers.SetBorderColor(_settingsMuteBtn.transform, BywaterUiHelpers.DangerRed);
                }
            }

            if (_settingsNameplatesLabel != null) _settingsNameplatesLabel.text = _nameplatesAlwaysOn ? "Always On" : "Hover / Hold Alt";
            if (_settingsNameplatesBtn != null) BywaterUiHelpers.SetButtonColors(_settingsNameplatesBtn, _nameplatesAlwaysOn);

            if (_settingsDevToolsLabel != null) _settingsDevToolsLabel.text = _isDevToolsVisible ? "ON" : "OFF";
            if (_settingsDevToolsBtn != null) BywaterUiHelpers.SetButtonColors(_settingsDevToolsBtn, _isDevToolsVisible);

            if (_settingsSaveBtn != null) _settingsSaveBtn.interactable = !_isCombatHudActive;
            if (_settingsLoadBtn != null) _settingsLoadBtn.interactable = !_isCombatHudActive;
            if (_settingsFleeBtn != null)
            {
                _settingsFleeBtn.gameObject.SetActive(_isCombatHudActive);
                _settingsFleeBtn.interactable = _isCombatHudActive;
            }
            if (_settingsQuitBtnLabel != null)
            {
                _settingsQuitBtnLabel.text = _quitConfirmArmed ? "Confirm Quit?" : "Quit to Desktop";
                _settingsQuitBtnLabel.color = _quitConfirmArmed ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.ParchmentText;
            }
            if (_settingsQuitBtn != null)
            {
                BywaterUiHelpers.SetButtonColors(_settingsQuitBtn, false);
                if (_quitConfirmArmed)
                {
                    Image quitBg = _settingsQuitBtn.GetComponent<Image>();
                    if (quitBg != null)
                    {
                        quitBg.color = new Color(0.38f, 0.11f, 0.12f, 0.98f);
                    }
                    BywaterUiHelpers.SetBorderColor(_settingsQuitBtn.transform, BywaterUiHelpers.BrassHighlight);
                }
            }
        }

        private void HandleQuitButtonClicked()
        {
            if (!_quitConfirmArmed)
            {
                _quitConfirmArmed = true;
                RefreshSettingsModalControls();
                return;
            }

            _quitConfirmArmed = false;
            RefreshSettingsModalControls();
            OnQuitRequested?.Invoke();
        }

        private void BuildQuickResolveModal(Transform parent)
        {
            RectTransform modal = BywaterUiHelpers.CreatePanel(parent, "QuickResolveModal", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 2.2f);
            modal.anchorMin = new Vector2(0.5f, 0.5f);
            modal.anchorMax = new Vector2(0.5f, 0.5f);
            modal.pivot = new Vector2(0.5f, 0.5f);
            modal.anchoredPosition = Vector2.zero;
            modal.sizeDelta = new Vector2(580f, 340f);
            _quickResolveModalRoot = modal.gameObject;

            VerticalLayoutGroup vlg = modal.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 20, 20);
            vlg.spacing = 14f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            _quickResolveTitle = BywaterUiHelpers.CreateLabel(modal, "QuickResolveTitle", "Encounter Ahead", _font, 22f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold);
            BywaterUiHelpers.AddTextDropShadow(_quickResolveTitle);
            _quickResolveTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

            _quickResolveSummary = BywaterUiHelpers.CreateLabel(
                modal,
                "QuickResolveSummary",
                "Hostiles block the path. Command the battle tactically or quick-resolve it.",
                _font,
                16f,
                BywaterUiHelpers.ParchmentText,
                TextAlignmentOptions.Center,
                wrap: true);
            _quickResolveSummary.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            GameObject btnRow = new GameObject("QuickResolveButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            btnRow.transform.SetParent(modal, false);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 52f;
            HorizontalLayoutGroup bHlg = btnRow.GetComponent<HorizontalLayoutGroup>();
            bHlg.spacing = 16f;
            bHlg.childControlWidth = true;
            bHlg.childControlHeight = true;
            bHlg.childForceExpandWidth = true;

            Button fightBtn = BywaterUiHelpers.CreateButton(btnRow.transform, "FightTacticallyBtn", "Fight Tactically", _font, () =>
            {
                HideQuickResolveModal();
                OnQuickResolveFightRequested?.Invoke();
            }, 16f);
            BywaterUiHelpers.SetButtonColors(fightBtn, true);

            BywaterUiHelpers.CreateButton(btnRow.transform, "QuickResolveBtn", "Quick Resolve", _font, () =>
            {
                HideQuickResolveModal();
                OnQuickResolveAutoWinRequested?.Invoke();
            }, 16f);
        }

        private void BuildOutcomeModal(Transform parent)
        {
            RectTransform modal = BywaterUiHelpers.CreatePanel(parent, "OutcomeModal", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 2.2f);
            modal.anchorMin = new Vector2(0.5f, 0.5f);
            modal.anchorMax = new Vector2(0.5f, 0.5f);
            modal.pivot = new Vector2(0.5f, 0.5f);
            modal.anchoredPosition = Vector2.zero;
            modal.sizeDelta = new Vector2(600f, 430f);
            _outcomeModalRoot = modal.gameObject;

            VerticalLayoutGroup vlg = modal.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 14, 14);
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            _outcomeTitle = BywaterUiHelpers.CreateLabel(modal, "OutcomeTitle", "VICTORY!", _font, 26f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold);
            BywaterUiHelpers.AddTextDropShadow(_outcomeTitle);
            LayoutElement titleLe = _outcomeTitle.gameObject.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 36f;
            titleLe.flexibleHeight = 0f;

            RectTransform lvlBanner = BywaterUiHelpers.CreatePanel(
                modal,
                "OutcomeLevelUpBanner",
                new Color(0.28f, 0.21f, 0.05f, 0.96f),
                withBrassOutline: true,
                outlinePx: 1.5f);
            BywaterUiHelpers.SetBorderColor(lvlBanner, BywaterUiHelpers.BrassHighlight);
            LayoutElement lvlBannerLe = lvlBanner.gameObject.AddComponent<LayoutElement>();
            lvlBannerLe.preferredHeight = 30f;
            lvlBannerLe.flexibleHeight = 0f;
            _outcomeLevelUpBanner = lvlBanner.gameObject;

            _outcomeLevelUpText = BywaterUiHelpers.CreateLabel(
                lvlBanner,
                "OutcomeLevelUpText",
                "LEVEL UP!",
                _font,
                18f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_outcomeLevelUpText);
            _outcomeLevelUpText.rectTransform.anchorMin = Vector2.zero;
            _outcomeLevelUpText.rectTransform.anchorMax = Vector2.one;
            _outcomeLevelUpText.rectTransform.offsetMin = new Vector2(8f, 2f);
            _outcomeLevelUpText.rectTransform.offsetMax = new Vector2(-8f, -2f);
            _outcomeLevelUpBanner.SetActive(false);

            _outcomeSummary = BywaterUiHelpers.CreateLabel(modal, "OutcomeSummary", string.Empty, _font, 16.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, wrap: true);
            _outcomeSummary.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            GameObject btnRow = new GameObject("OutcomeButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            btnRow.transform.SetParent(modal, false);
            LayoutElement btnRowLe = btnRow.AddComponent<LayoutElement>();
            btnRowLe.preferredHeight = 44f;
            btnRowLe.flexibleHeight = 0f;
            HorizontalLayoutGroup bHlg = btnRow.GetComponent<HorizontalLayoutGroup>();
            bHlg.spacing = 14f;
            bHlg.childControlWidth = true;
            bHlg.childControlHeight = true;
            bHlg.childForceExpandWidth = true;

            _outcomeContinueBtn = BywaterUiHelpers.CreateButton(btnRow.transform, "ContinueBtn", "Continue Exploring", _font, () => OnContinueAfterVictoryRequested?.Invoke(), 16f);
            _outcomeRetryBtn = BywaterUiHelpers.CreateButton(btnRow.transform, "RetryBtn", "Retry Skirmish", _font, () => OnRetrySkirmishRequested?.Invoke(), 16f);
            _outcomeReviveBtn = BywaterUiHelpers.CreateButton(btnRow.transform, "ReviveBtn", "Revive at Shrine", _font, () => OnReviveAtShrineRequested?.Invoke(), 16f);

            BuildRichVictoryLayout(modal, btnRow.transform);
        }

        private void BuildToastBanner(Transform parent)
        {
            RectTransform toast = BywaterUiHelpers.CreatePanel(parent, "ToastBanner", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.5f);
            toast.anchorMin = new Vector2(0.5f, 1f);
            toast.anchorMax = new Vector2(0.5f, 1f);
            toast.pivot = new Vector2(0.5f, 1f);
            toast.anchoredPosition = new Vector2(0f, -60f);
            toast.sizeDelta = new Vector2(620f, 38f);

            Image bg = toast.GetComponent<Image>();
            bg.raycastTarget = false;

            _toastCanvasGroup = toast.gameObject.AddComponent<CanvasGroup>();
            _toastCanvasGroup.blocksRaycasts = false;
            _toastCanvasGroup.interactable = false;

            _toastText = BywaterUiHelpers.CreateLabel(toast, "ToastText", string.Empty, _font, 16.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_toastText);
            _toastText.rectTransform.anchorMin = Vector2.zero;
            _toastText.rectTransform.anchorMax = Vector2.one;
            _toastText.rectTransform.offsetMin = new Vector2(12f, 4f);
            _toastText.rectTransform.offsetMax = new Vector2(-12f, -4f);

            _toastRoot = toast.gameObject;
        }

        private static string FormatTileKind(BywaterTileKind kind)
        {
            switch (kind)
            {
                case BywaterTileKind.Cobblestone: return "Cobblestone";
                case BywaterTileKind.DirtPath: return "Dirt Road";
                case BywaterTileKind.ShallowWater: return "Shallow Water";
                case BywaterTileKind.TownBuildingSolid: return "Town Wall";
                case BywaterTileKind.TownBuildingDoor: return "Doorway";
                case BywaterTileKind.ForestCover: return "Woods (Cover)";
                case BywaterTileKind.WallBlock: return "Stone Wall";
                case BywaterTileKind.DungeonFloor: return "Cavern Floor";
                case BywaterTileKind.DungeonWall: return "Cavern Wall";
                case BywaterTileKind.DungeonDoor: return "Cavern Door";
                case BywaterTileKind.DungeonStairs: return "Stairs";
                default: return "Grassland";
            }
        }

        private static string FormatItemStats(ItemDefinition item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>(5);
            if (!string.IsNullOrWhiteSpace(item.DamageDice)) parts.Add("Dmg " + item.DamageDice);
            if (item.WeaponBonus != 0) parts.Add($"+{item.WeaponBonus} Hit");
            if (item.ArmorClassBonus != 0) parts.Add($"+{item.ArmorClassBonus} AC");
            if (item.Range > 1) parts.Add($"Rng {item.Range}");
            if (!string.IsNullOrWhiteSpace(item.HealDice)) parts.Add("Heal " + item.HealDice);
            if (item.Weight > 0) parts.Add($"Wt {item.Weight}");
            return string.Join(", ", parts);
        }

        private static void ResolveShopVenueMeta(
            ShopDefinition shop,
            out string title,
            out string greeting,
            out string portraitKey,
            out string fallbackIconKey)
        {
            string shopId = shop != null ? shop.ShopId : string.Empty;
            string shopName = shop != null && !string.IsNullOrWhiteSpace(shop.Name) ? shop.Name : "Bywater Merchant";
            switch (shopId)
            {
                case "1":
                    title = "Bywater Armory - Master Blacksmith Borin";
                    greeting = "Forged blades, shields, and sturdy gear for the road.";
                    portraitKey = "npc_blacksmith";
                    fallbackIconKey = "sword";
                    break;
                case "4":
                    title = "Bywater Tannery - Tanner Mira";
                    greeting = "Hardened leather, hides, and rugged trail gear.";
                    portraitKey = "npc_tanner";
                    fallbackIconKey = "shield";
                    break;
                case "10":
                    title = "Bywater Apothecary - Alchemist Orin";
                    greeting = "Elixirs, poultices, and warding draughts brewed fresh.";
                    portraitKey = "npc_apothecary";
                    fallbackIconKey = "potion";
                    break;
                default:
                    title = shopName;
                    greeting = "Browse shop stock or sell unneeded items from your stash.";
                    portraitKey = "npc_blacksmith";
                    fallbackIconKey = "sword";
                    break;
            }
        }

        private void ApplyNpcPortrait(Image target, string npcKey, string fallbackProceduralKey)
        {
            if (target == null)
            {
                return;
            }

            Sprite portrait = null;
            if (_catalog != null && !string.IsNullOrWhiteSpace(npcKey))
            {
                portrait = _catalog.GetUnifiedPortrait(npcKey) ?? _catalog.GetHotbarIcon(npcKey);
            }

            target.preserveAspect = true;
            if (portrait != null)
            {
                target.sprite = portrait;
                target.color = Color.white;
            }
            else
            {
                target.sprite = BywaterUiHelpers.GetProceduralIconSprite(fallbackProceduralKey);
                target.color = BywaterUiHelpers.BrassHighlight;
            }
        }

        private static bool IsShopItemUpgrade(
            PartyState party,
            ItemDefinition candidate,
            IReadOnlyDictionary<int, ItemDefinition> items)
        {
            if (party == null || candidate == null || items == null)
            {
                return false;
            }

            if (!PartyMemberState.TryParseSlot(candidate.Slot, out _))
            {
                return false;
            }

            for (int i = 0; i < party.Members.Count; i++)
            {
                PartyMemberState member = party.Members[i];
                if (member == null || !member.IsAlive)
                {
                    continue;
                }

                if (IsStashEntryUpgradeForHero(member, candidate, items))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
