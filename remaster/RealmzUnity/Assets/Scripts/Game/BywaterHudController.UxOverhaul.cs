using System;
using System.Collections;
using System.Collections.Generic;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Realmz.Game
{
    public sealed partial class BywaterHudController
    {
        public sealed class PointerActionRelay : MonoBehaviour, IPointerClickHandler
        {
            public Action OnLeftClick;
            public Action OnRightClick;
            public Action OnDoubleClick;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData == null)
                {
                    return;
                }

                if (eventData.button == PointerEventData.InputButton.Right)
                {
                    OnRightClick?.Invoke();
                    return;
                }

                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    if (eventData.clickCount >= 2 && OnDoubleClick != null)
                    {
                        OnDoubleClick.Invoke();
                    }
                    else
                    {
                        OnLeftClick?.Invoke();
                    }
                }
            }
        }

        private sealed class PaperDollSlotRefs
        {
            public EquipmentSlot Slot;
            public string DisplayTitle;
            public RectTransform Root;
            public Image Background;
            public Image Icon;
            public TextMeshProUGUI SlotLabel;
            public TextMeshProUGUI ItemLabel;
            public ItemDefinition BoundDef;
            public InventoryItem BoundEntry;
        }

        private sealed class StatBadgeRefs
        {
            public string Name;
            public Button Button;
            public TextMeshProUGUI Label;
        }

        private sealed class VictoryHeroCardRefs
        {
            public GameObject Root;
            public Image Background;
            public Image Portrait;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI LevelBadgeText;
            public GameObject LvlUpIconGo;
            public Image LvlUpIcon;
            public GameObject ReviveIconGo;
            public Image ReviveIcon;
            public TextMeshProUGUI StatusTagText;
            public GameObject StatusTagGo;
            public Image XpBaseFill;
            public Image XpGainFill;
            public TextMeshProUGUI XpText;
            public TextMeshProUGUI PerfText;
        }

        private sealed class VictoryLootTileRefs
        {
            public GameObject Root;
            public Button CardButton;
            public Image FrameBg;
            public Image SelectBoxBg;
            public Image CheckmarkIcon;
            public Image Icon;
            public TextMeshProUGUI QtyText;
            public GameObject QtyBadge;
            public GameObject UpgradeBadge;
            public TextMeshProUGUI UpgradeHeroText;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI StatsText;
            public int BoundStackIndex;
        }

        private sealed class LevelUpStatRowRefs
        {
            public string Key;
            public string Label;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI ValueText;
            public TextMeshProUGUI DeltaText;
            public Button MinusBtn;
            public Button PlusBtn;
        }

        private enum StashFilterKind
        {
            All = 0,
            Gear = 1,
            Potions = 2,
            Supplies = 3
        }

        private enum StashSortKind
        {
            Upgrade = 0,
            Slot = 1,
            Value = 2
        }

        // UX Overhaul Header Camp & Party Sheet State
        private RectTransform _partyModalRect;
        private Button _campBandageHeaderBtn;
        private Image _campBandageIcon;
        private TextMeshProUGUI _campBandageBadge;
        private Button _campRestHeaderBtn;
        private Image _campRestIcon;
        private TextMeshProUGUI _campRestBadge;

        private Button _tabEquipBtn;
        private Button _tabCharSheetBtn;
        private GameObject _paperDollPanel;
        private GameObject _charSheetPanel;
        private bool _showCharSheetTab;
        private readonly List<PaperDollSlotRefs> _paperDollSlots = new List<PaperDollSlotRefs>(9);
        private TextMeshProUGUI _paperDollSummaryText;
        private TextMeshProUGUI _drawerCarryWeightText;
        private Button _drawerLvlUpBtn;
        private Image _drawerXpFill;
        private TextMeshProUGUI _drawerXpText;

        // Character Sheet refs
        private readonly List<StatBadgeRefs> _charSheetStatRows = new List<StatBadgeRefs>(6);
        private TextMeshProUGUI _charSheetDerivedText;
        private TextMeshProUGUI _charSheetExtraText;

        // Shared Stash Filter / Sort / Bulk Action / Inline Potion Target Picker
        private StashFilterKind _stashFilterKind = StashFilterKind.All;
        private StashSortKind _stashSortKind = StashSortKind.Upgrade;
        private EquipmentSlot? _stashSlotFilter;
        private readonly Dictionary<StashFilterKind, Button> _stashFilterBtns = new Dictionary<StashFilterKind, Button>();
        private Button _stashSortBtn;
        private TextMeshProUGUI _stashSortBtnLabel;
        private Button _stashSlotClearBtn;
        private TextMeshProUGUI _stashSlotClearLabel;
        private Button _equipBestHeroBtn;
        private Button _equipBestPartyBtn;
        private GameObject _potionPickerBar;
        private TextMeshProUGUI _potionPickerTitle;
        private readonly List<Button> _potionPickerHeroBtns = new List<Button>(6);
        private int _pendingPotionStashIdx = -1;
        private readonly List<int> _stashDisplayOrder = new List<int>(32);

        // Level-Up Modal State
        private GameObject _levelUpModalRoot;
        private Image _levelUpPortrait;
        private TextMeshProUGUI _levelUpTitle;
        private TextMeshProUGUI _levelUpPointsLabel;
        private readonly List<LevelUpStatRowRefs> _levelUpStatRows = new List<LevelUpStatRowRefs>(6);
        private TextMeshProUGUI _levelUpPreviewText;
        private TextMeshProUGUI _levelUpSpellText;
        private Button _levelUpRecommendedBtn;
        private Button _levelUpConfirmBtn;
        private int _levelUpHeroIndex;
        private readonly int[] _levelUpAllocDeltas = new int[6];
        private int? _levelUpChosenSpellId;

        // Rich 980x690 Tactical Victory UI Refs
        private RectTransform _outcomeModalRect;
        private GameObject _outcomeRichVictoryRoot;
        private TextMeshProUGUI _victorySubtitleText;
        private Image _victoryGoldIcon;
        private TextMeshProUGUI _victoryGoldText;
        private readonly List<VictoryHeroCardRefs> _victoryHeroCards = new List<VictoryHeroCardRefs>(6);
        private ScrollRect _victoryLootScroll;
        private GameObject _victoryLootEmptyLabel;
        private readonly List<VictoryLootTileRefs> _victoryLootTiles = new List<VictoryLootTileRefs>(16);
        private readonly HashSet<int> _selectedVictoryLootIndices = new HashSet<int>();
        private Button _victoryLevelUpNowBtn;
        private TextMeshProUGUI _victoryLevelUpNowLabel;
        private CombatVictorySummary _cachedVictorySummary;
        private bool _outcomeInRichVictoryMode;
        private Coroutine _victoryAnimCoroutine;
        private Coroutine _modalPopCoroutine;
        private GameObject _modalPopTarget;

        private struct VictoryHeroAnimEntry
        {
            public VictoryHeroCardRefs Card;
            public float BaseFrac;
            public float AfterFrac;
            public bool ReadyToLevelUp;
        }

        public bool IsLevelUpModalOpen => _levelUpModalRoot != null && _levelUpModalRoot.activeSelf;

        public event Action<int> OnEquipBestHeroRequested;
        public event Action OnEquipBestPartyRequested;
        public event Action<int, AttributeAllocation, int?> OnConfirmLevelUpRequested;
        public event Action<int[]> OnVictoryTakeLootRequested;

        private void BuildHeaderCampPills(Transform headerTransform)
        {
            // Bandage Compact Icon+Badge Button
            _campBandageHeaderBtn = CreateCompactCampButton(
                headerTransform,
                "QuickBandageBtn",
                "heal",
                "2/2",
                out _campBandageIcon,
                out _campBandageBadge,
                () => OnCampModeRequested?.Invoke(CampMode.QuickBandage));

            AttachTooltipCallbacks(
                _campBandageHeaderBtn.gameObject,
                () =>
                {
                    int charges = _cachedParty != null ? _cachedParty.QuickBandageCharges : PartyState.MaxQuickBandageCharges;
                    return ("Quick Bandage (Field Rest)",
                        $"Charges: {charges}/{PartyState.MaxQuickBandageCharges}\n" +
                        "Heals living heroes +35% HP (+1 turn).\n" +
                        "Does not restore SP or revive fallen heroes.\n" +
                        "Refills on Full Rest, Safe Town entry, or Temple Heal.");
                });

            // Torch Full Rest Compact Icon+Badge Button
            _campRestHeaderBtn = CreateCompactCampButton(
                headerTransform,
                "FullRestBtn",
                "torch",
                "Free",
                out _campRestIcon,
                out _campRestBadge,
                () => OnCampModeRequested?.Invoke(CampMode.FullRest));

            AttachTooltipCallbacks(
                _campRestHeaderBtn.gameObject,
                () =>
                {
                    int torches = _cachedParty != null ? _cachedParty.GetTotalTorchCharges() : 0;
                    int activeT = _cachedParty != null ? _cachedParty.TorchTurnsRemaining : 0;
                    return ("Full Rest (+8 Turns)",
                        $"Available Torches: {torches} charge(s) | Active Light: {activeT}t\n" +
                        "Restores 100% HP & SP, clears Exhaustion, refills Bandages (2/2).\n" +
                        "Free in Bywater Town Safe Zone; costs 1 Torch charge & carries 15% ambush risk in uncleared dungeon/wilds.\n" +
                        "Poison/Disease/Curse require Potions or the Temple.");
                });
        }

        private Button CreateCompactCampButton(
            Transform parent,
            string name,
            string iconKey,
            string initialBadge,
            out Image iconImg,
            out TextMeshProUGUI badgeLbl,
            Action onClick)
        {
            RectTransform rt = BywaterUiHelpers.CreatePanel(parent, name, BywaterUiHelpers.ButtonBg, withBrassOutline: true, outlinePx: 1.4f);
            LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 96f;
            le.minWidth = 88f;
            le.flexibleWidth = 0f;
            le.preferredHeight = 40f;

            Button btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = rt.GetComponent<Image>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            HorizontalLayoutGroup hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 4, 4);
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;

            Sprite sp = ResolveCampSprite(iconKey);
            iconImg = BywaterUiHelpers.CreateImage(rt, "Icon", sp, Color.white, preserveAspect: true);
            iconImg.raycastTarget = false;
            LayoutElement iconLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 28f;
            iconLe.preferredHeight = 28f;
            iconLe.minWidth = 28f;
            iconLe.minHeight = 28f;

            badgeLbl = BywaterUiHelpers.CreateLabel(
                rt,
                "Badge",
                initialBadge,
                _font,
                15f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            BywaterUiHelpers.AddTextDropShadow(badgeLbl);
            badgeLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return btn;
        }

        private Sprite ResolveCampSprite(string key)
        {
            if (_catalog != null)
            {
                if (string.Equals(key, "torch", StringComparison.OrdinalIgnoreCase))
                {
                    Sprite torch = _catalog.GetItemIcon(805, "Torch") ?? _catalog.GetHotbarIcon("torch");
                    if (torch != null) return torch;
                }
                Sprite s = _catalog.GetHotbarIcon(key);
                if (s != null) return s;
            }
            return BywaterUiHelpers.GetProceduralIconSprite(key);
        }

        private void AttachTooltipCallbacks(GameObject target, Func<(string title, string body)> builder)
        {
            if (target == null || builder == null)
            {
                return;
            }

            HoverRelay relay = target.GetComponent<HoverRelay>() ?? target.AddComponent<HoverRelay>();
            relay.OnEnter = () =>
            {
                (string title, string body) = builder();
                if (!string.IsNullOrEmpty(title))
                {
                    SetCombatHoverPreview(title, body, null);
                }
            };
            relay.OnExit = ClearCombatHoverPreview;
        }

        private void BuildPaperDollAndCharSheet(Transform leftCol)
        {
            // XP Bar + Level Up pill right below HeroStats
            GameObject xpRow = new GameObject("DrawerXpRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            xpRow.transform.SetParent(leftCol, false);
            xpRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            HorizontalLayoutGroup xpHlg = xpRow.GetComponent<HorizontalLayoutGroup>();
            xpHlg.spacing = 8f;
            xpHlg.childControlWidth = true;
            xpHlg.childControlHeight = true;
            xpHlg.childForceExpandWidth = false;

            _drawerXpFill = BywaterUiHelpers.CreateProgressBar(xpRow.transform, "DrawerXpBar", new Color(0.58f, 0.42f, 0.14f, 1f), out RectTransform xpBarRt);
            LayoutElement xpLe = xpBarRt.gameObject.AddComponent<LayoutElement>();
            xpLe.flexibleWidth = 1f;
            xpLe.preferredHeight = 24f;
            _drawerXpText = BywaterUiHelpers.CreateLabel(xpBarRt, "DrawerXpText", "XP 0 / 200", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_drawerXpText);
            _drawerXpText.rectTransform.anchorMin = Vector2.zero;
            _drawerXpText.rectTransform.anchorMax = Vector2.one;
            _drawerXpText.rectTransform.offsetMin = Vector2.zero;
            _drawerXpText.rectTransform.offsetMax = Vector2.zero;

            _drawerLvlUpBtn = BywaterUiHelpers.CreateButton(
                xpRow.transform,
                "DrawerLvlUpBtn",
                "LVL UP!",
                _font,
                () => OpenLevelUpModal(_selectedHeroIndex),
                15f);
            BywaterUiHelpers.SetButtonColors(_drawerLvlUpBtn, true);
            _drawerLvlUpBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 104f;
            _drawerLvlUpBtn.gameObject.SetActive(false);

            // Tab switcher: [Equipment] | [Character Sheet]
            GameObject tabRow = new GameObject("DrawerModeTabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tabRow.transform.SetParent(leftCol, false);
            tabRow.AddComponent<LayoutElement>().preferredHeight = 34f;
            HorizontalLayoutGroup tabHlg = tabRow.GetComponent<HorizontalLayoutGroup>();
            tabHlg.spacing = 8f;
            tabHlg.childControlWidth = true;
            tabHlg.childControlHeight = true;
            tabHlg.childForceExpandWidth = true;

            _tabEquipBtn = BywaterUiHelpers.CreateButton(tabRow.transform, "TabEquipmentBtn", "Equipment (9 Slots)", _font, () => SwitchHeroSheetTab(false), 15.5f);
            _tabCharSheetBtn = BywaterUiHelpers.CreateButton(tabRow.transform, "TabCharSheetBtn", "Character Sheet", _font, () => SwitchHeroSheetTab(true), 15.5f);

            // Paper-Doll 9-slot container (3 anatomical columns, 386px height, ZERO unequip buttons)
            RectTransform dollRt = BywaterUiHelpers.CreatePanel(leftCol, "PaperDollPanel", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1f);
            LayoutElement dollLe = dollRt.gameObject.AddComponent<LayoutElement>();
            dollLe.preferredHeight = 404f;
            dollLe.flexibleHeight = 1f;
            _paperDollPanel = dollRt.gameObject;

            VerticalLayoutGroup dollVlg = dollRt.gameObject.AddComponent<VerticalLayoutGroup>();
            dollVlg.padding = new RectOffset(8, 8, 8, 8);
            dollVlg.spacing = 6f;
            dollVlg.childControlWidth = true;
            dollVlg.childControlHeight = true;
            dollVlg.childForceExpandHeight = false;

            GameObject colsRow = new GameObject("AnatomyCols", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            colsRow.transform.SetParent(dollRt, false);
            colsRow.AddComponent<LayoutElement>().flexibleHeight = 1f;
            HorizontalLayoutGroup colsHlg = colsRow.GetComponent<HorizontalLayoutGroup>();
            colsHlg.spacing = 8f;
            colsHlg.childControlWidth = true;
            colsHlg.childControlHeight = true;
            colsHlg.childForceExpandWidth = true;

            Transform armCol = CreateAnatomyColumn(colsRow.transform, "ArmsCol");
            CreatePaperDollSlotCard(armCol, EquipmentSlot.Weapon, "Main Hand");
            CreatePaperDollSlotCard(armCol, EquipmentSlot.Shield, "Off-Hand");
            CreatePaperDollSlotCard(armCol, EquipmentSlot.MissileWeapon, "Ranged");
            RectTransform controlsHintCard = BywaterUiHelpers.CreatePanel(armCol, "PaperDollControlsHint", BywaterUiHelpers.CardBg, withBrassOutline: false);
            controlsHintCard.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            TextMeshProUGUI controlsHintTxt = BywaterUiHelpers.CreateLabel(
                controlsHintCard,
                "ControlsHintText",
                "Click slot: Filter stash\nRight-click: Unequip",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.Center,
                wrap: true);
            controlsHintTxt.enableAutoSizing = true;
            controlsHintTxt.fontSizeMin = 11f;
            controlsHintTxt.fontSizeMax = 15f;
            controlsHintTxt.rectTransform.anchorMin = Vector2.zero;
            controlsHintTxt.rectTransform.anchorMax = Vector2.one;
            controlsHintTxt.rectTransform.offsetMin = new Vector2(6f, 4f);
            controlsHintTxt.rectTransform.offsetMax = new Vector2(-6f, -4f);

            Transform torsoCol = CreateAnatomyColumn(colsRow.transform, "TorsoCol");
            CreatePaperDollSlotCard(torsoCol, EquipmentSlot.Helm, "Head");
            CreatePaperDollSlotCard(torsoCol, EquipmentSlot.Armor, "Body");
            CreatePaperDollSlotCard(torsoCol, EquipmentSlot.Hands, "Hands");
            CreatePaperDollSlotCard(torsoCol, EquipmentSlot.Feet, "Feet");

            Transform accCol = CreateAnatomyColumn(colsRow.transform, "AccessoryCol");
            CreatePaperDollSlotCard(accCol, EquipmentSlot.Accessory1, "Accessory 1");
            CreatePaperDollSlotCard(accCol, EquipmentSlot.Accessory2, "Accessory 2");
            RectTransform hintCard = BywaterUiHelpers.CreatePanel(accCol, "PaperDollTip", BywaterUiHelpers.CardBg, withBrassOutline: false);
            hintCard.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            hintCard.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            _paperDollSummaryText = BywaterUiHelpers.CreateLabel(
                hintCard,
                "HintText",
                "No active set bonuses",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.Center,
                wrap: true);
            _paperDollSummaryText.enableAutoSizing = true;
            _paperDollSummaryText.fontSizeMin = 10.5f;
            _paperDollSummaryText.fontSizeMax = 15f;
            _paperDollSummaryText.overflowMode = TextOverflowModes.Ellipsis;
            _paperDollSummaryText.rectTransform.anchorMin = Vector2.zero;
            _paperDollSummaryText.rectTransform.anchorMax = Vector2.one;
            _paperDollSummaryText.rectTransform.offsetMin = new Vector2(6f, 2f);
            _paperDollSummaryText.rectTransform.offsetMax = new Vector2(-6f, -2f);

            // Character Sheet View (initially hidden)
            RectTransform csRt = BywaterUiHelpers.CreatePanel(leftCol, "CharacterSheetPanel", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1f);
            LayoutElement csLe = csRt.gameObject.AddComponent<LayoutElement>();
            csLe.preferredHeight = 404f;
            csLe.flexibleHeight = 1f;
            _charSheetPanel = csRt.gameObject;

            VerticalLayoutGroup csVlg = csRt.gameObject.AddComponent<VerticalLayoutGroup>();
            csVlg.padding = new RectOffset(10, 10, 10, 10);
            csVlg.spacing = 8f;
            csVlg.childControlWidth = true;
            csVlg.childControlHeight = true;
            csVlg.childForceExpandHeight = false;

            GameObject csColumns = new GameObject("SheetColumns", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            csColumns.transform.SetParent(csRt, false);
            csColumns.AddComponent<LayoutElement>().flexibleHeight = 1f;
            HorizontalLayoutGroup csColHlg = csColumns.GetComponent<HorizontalLayoutGroup>();
            csColHlg.spacing = 12f;
            csColHlg.childControlWidth = true;
            csColHlg.childControlHeight = true;
            csColHlg.childForceExpandWidth = false;

            // Left sub-col: 6 Attributes with hover breakdowns
            RectTransform attrCol = BywaterUiHelpers.CreatePanel(csColumns.transform, "AttrCol", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            attrCol.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;
            VerticalLayoutGroup attrVlg = attrCol.gameObject.AddComponent<VerticalLayoutGroup>();
            attrVlg.padding = new RectOffset(8, 8, 8, 8);
            attrVlg.spacing = 6f;
            attrVlg.childControlWidth = true;
            attrVlg.childControlHeight = true;

            TextMeshProUGUI attrHdr = BywaterUiHelpers.CreateLabel(attrCol, "AttrHeader", "Core Attributes (Hover)", _font, 15.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            attrHdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            string[] statNames = { "Brawn", "Knowledge", "Judgment", "Agility", "Vitality", "Luck" };
            for (int i = 0; i < statNames.Length; i++)
            {
                string sName = statNames[i];
                RectTransform rowRt = BywaterUiHelpers.CreatePanel(attrCol, $"Stat_{sName}", BywaterUiHelpers.CardBg, withBrassOutline: false);
                rowRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
                Button btn = rowRt.gameObject.AddComponent<Button>();
                btn.targetGraphic = rowRt.GetComponent<Image>();

                TextMeshProUGUI lbl = BywaterUiHelpers.CreateLabel(rowRt, "Label", sName, _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                lbl.rectTransform.anchorMin = Vector2.zero;
                lbl.rectTransform.anchorMax = Vector2.one;
                lbl.rectTransform.offsetMin = new Vector2(10f, 2f);
                lbl.rectTransform.offsetMax = new Vector2(-10f, -2f);

                _charSheetStatRows.Add(new StatBadgeRefs { Name = sName, Button = btn, Label = lbl });

                int statIdx = i;
                AttachTooltipCallbacks(rowRt.gameObject, () => BuildAttributeTooltip(statIdx));
            }

            // Right sub-col: Combat Derived Stats + Weight + Conditions + Spells
            RectTransform derivedCol = BywaterUiHelpers.CreatePanel(csColumns.transform, "DerivedCol", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            derivedCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            VerticalLayoutGroup drvVlg = derivedCol.gameObject.AddComponent<VerticalLayoutGroup>();
            drvVlg.padding = new RectOffset(10, 10, 8, 8);
            drvVlg.spacing = 6f;
            drvVlg.childControlWidth = true;
            drvVlg.childControlHeight = true;

            TextMeshProUGUI drvHdr = BywaterUiHelpers.CreateLabel(derivedCol, "DerivedHeader", "Derived Combat & Exploration Stats", _font, 15.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            drvHdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            _charSheetDerivedText = BywaterUiHelpers.CreateLabel(derivedCol, "DerivedBody", string.Empty, _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.TopLeft, wrap: true);
            LayoutElement derivedBodyLe = _charSheetDerivedText.gameObject.AddComponent<LayoutElement>();
            derivedBodyLe.minHeight = 140f;
            derivedBodyLe.preferredHeight = 150f;

            _charSheetExtraText = BywaterUiHelpers.CreateLabel(derivedCol, "ExtraBody", string.Empty, _font, 15f, BywaterUiHelpers.InfoCyan, TextAlignmentOptions.TopLeft, wrap: true);
            _charSheetExtraText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            SwitchHeroSheetTab(false);
        }

        private Transform CreateAnatomyColumn(Transform parent, string name)
        {
            GameObject col = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            col.transform.SetParent(parent, false);
            col.AddComponent<LayoutElement>().flexibleWidth = 1f;
            VerticalLayoutGroup vlg = col.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            return col.transform;
        }

        private void CreateFillerSpacer(Transform parent)
        {
            GameObject spacer = new GameObject("Spacer", typeof(RectTransform));
            spacer.transform.SetParent(parent, false);
            spacer.AddComponent<LayoutElement>().flexibleHeight = 1f;
        }

        private void CreatePaperDollSlotCard(Transform parent, EquipmentSlot slot, string label)
        {
            RectTransform card = BywaterUiHelpers.CreatePanel(parent, $"PaperDollSlot_{slot}", BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1.2f);
            LayoutElement le = card.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 86f;

            HorizontalLayoutGroup hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 4, 4);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;

            RectTransform iconFrame = BywaterUiHelpers.CreatePanel(card, "IconBox", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1f);
            LayoutElement iconBoxLe = iconFrame.gameObject.AddComponent<LayoutElement>();
            iconBoxLe.preferredWidth = 44f;
            iconBoxLe.minWidth = 44f;
            iconBoxLe.preferredHeight = 44f;

            Image icon = BywaterUiHelpers.CreateImage(iconFrame, "Icon", null, Color.white, preserveAspect: true);
            icon.raycastTarget = false;
            icon.rectTransform.anchorMin = new Vector2(0.1f, 0.1f);
            icon.rectTransform.anchorMax = new Vector2(0.9f, 0.9f);
            icon.rectTransform.offsetMin = Vector2.zero;
            icon.rectTransform.offsetMax = Vector2.zero;
            icon.gameObject.SetActive(false);

            GameObject textCol = new GameObject("TextCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            textCol.transform.SetParent(card, false);
            textCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            VerticalLayoutGroup tvlg = textCol.GetComponent<VerticalLayoutGroup>();
            tvlg.spacing = 2f;
            tvlg.childControlWidth = true;
            tvlg.childControlHeight = true;

            TextMeshProUGUI slotLbl = BywaterUiHelpers.CreateLabel(textCol.transform, "SlotName", label, _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            slotLbl.enableAutoSizing = true;
            slotLbl.fontSizeMin = 11f;
            slotLbl.fontSizeMax = 15f;
            slotLbl.overflowMode = TextOverflowModes.Truncate;
            slotLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            TextMeshProUGUI itemLbl = BywaterUiHelpers.CreateLabel(textCol.transform, "ItemName", "Empty", _font, 15f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.TopLeft, wrap: true);
            itemLbl.enableAutoSizing = true;
            itemLbl.fontSizeMin = 11f;
            itemLbl.fontSizeMax = 15f;
            itemLbl.overflowMode = TextOverflowModes.Ellipsis;
            itemLbl.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            PaperDollSlotRefs slotRef = new PaperDollSlotRefs
            {
                Slot = slot,
                DisplayTitle = label,
                Root = card,
                Background = card.GetComponent<Image>(),
                Icon = icon,
                SlotLabel = slotLbl,
                ItemLabel = itemLbl
            };
            _paperDollSlots.Add(slotRef);

            PointerActionRelay clickRelay = card.gameObject.AddComponent<PointerActionRelay>();
            clickRelay.OnLeftClick = () => ToggleStashSlotFilter(slot);
            clickRelay.OnRightClick = () =>
            {
                if (slotRef.BoundEntry != null)
                {
                    OnUnequipRequested?.Invoke(_selectedHeroIndex, slot);
                }
            };
            clickRelay.OnDoubleClick = () =>
            {
                if (slotRef.BoundEntry != null)
                {
                    OnUnequipRequested?.Invoke(_selectedHeroIndex, slot);
                }
            };

            AttachTooltipCallbacks(card.gameObject, () =>
            {
                if (slotRef.BoundDef != null)
                {
                    string stats = FormatItemStats(slotRef.BoundDef);
                    PartyMemberState hero = _cachedParty != null && _selectedHeroIndex >= 0 && _selectedHeroIndex < _cachedParty.Members.Count
                        ? _cachedParty.Members[_selectedHeroIndex]
                        : null;
                    string setTag = FormatItemSetProgressTag(hero, slotRef.BoundDef);
                    string titlePrefix = string.IsNullOrEmpty(setTag) ? slotRef.BoundDef.Name : $"{slotRef.BoundDef.Name} {setTag}";
                    string setInfo = FormatItemSetTooltipSection(hero, slotRef.BoundDef);
                    string setBlock = string.IsNullOrEmpty(setInfo) ? string.Empty : $"\n{setInfo}";
                    return ($"{titlePrefix} ({slotRef.DisplayTitle})",
                        $"{stats}{setBlock}\n{slotRef.BoundDef.Description}\n\n" +
                        "Left-click: Filter stash for this slot\nRight-click or Double-click: Unequip to Stash");
                }

                return ($"{slotRef.DisplayTitle} Slot (Empty)",
                    $"No item equipped in {slotRef.DisplayTitle}.\nLeft-click to filter Shared Stash for matching gear.");
            });
        }

        private void SwitchHeroSheetTab(bool showCharSheet)
        {
            _showCharSheetTab = showCharSheet;
            if (_paperDollPanel != null) _paperDollPanel.SetActive(!showCharSheet);
            if (_charSheetPanel != null) _charSheetPanel.SetActive(showCharSheet);
            if (_tabEquipBtn != null) BywaterUiHelpers.SetButtonColors(_tabEquipBtn, !showCharSheet);
            if (_tabCharSheetBtn != null) BywaterUiHelpers.SetButtonColors(_tabCharSheetBtn, showCharSheet);
        }

        private void BuildStashControls(Transform rightCol)
        {
            // Header Title + Carry Weight Row
            GameObject headerRow = new GameObject("StashTopRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            headerRow.transform.SetParent(rightCol, false);
            LayoutElement hdrLe = headerRow.AddComponent<LayoutElement>();
            hdrLe.preferredHeight = 30f;
            hdrLe.flexibleHeight = 0f;
            HorizontalLayoutGroup hHlg = headerRow.GetComponent<HorizontalLayoutGroup>();
            hHlg.spacing = 8f;
            hHlg.childControlWidth = true;
            hHlg.childControlHeight = true;
            hHlg.childForceExpandWidth = false;

            TextMeshProUGUI title = BywaterUiHelpers.CreateLabel(headerRow.transform, "StashTitle", "Shared Stash", _font, 17f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(title);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _drawerCarryWeightText = BywaterUiHelpers.CreateLabel(headerRow.transform, "CarryWeight", "Wt: 0 / 1200", _font, 15f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.MidlineRight, FontStyles.Bold, wrap: false);
            _drawerCarryWeightText.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;

            // Filter Strip: All | Gear | Potions | Supplies + Clear Slot Filter
            GameObject filterRow = new GameObject("StashFilterRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            filterRow.transform.SetParent(rightCol, false);
            LayoutElement filtLe = filterRow.AddComponent<LayoutElement>();
            filtLe.preferredHeight = 34f;
            filtLe.flexibleHeight = 0f;
            HorizontalLayoutGroup fHlg = filterRow.GetComponent<HorizontalLayoutGroup>();
            fHlg.spacing = 6f;
            fHlg.childControlWidth = true;
            fHlg.childControlHeight = true;
            fHlg.childForceExpandWidth = false;

            AddStashFilterButton(filterRow.transform, StashFilterKind.All, "All", 72f);
            AddStashFilterButton(filterRow.transform, StashFilterKind.Gear, "Gear", 82f);
            AddStashFilterButton(filterRow.transform, StashFilterKind.Potions, "Potions", 96f);
            AddStashFilterButton(filterRow.transform, StashFilterKind.Supplies, "Supplies", 98f);

            _stashSlotClearBtn = BywaterUiHelpers.CreateButton(
                filterRow.transform,
                "ClearSlotFilterBtn",
                "Slot x",
                _font,
                () =>
                {
                    _stashSlotFilter = null;
                    if (_cachedParty != null && _cachedBundle != null)
                    {
                        RefreshPartyDrawer(_cachedParty, _cachedBundle, _selectedHeroIndex);
                    }
                },
                15f);
            BywaterUiHelpers.SetButtonColors(_stashSlotClearBtn, true);
            _stashSlotClearLabel = _stashSlotClearBtn.GetComponentInChildren<TextMeshProUGUI>();
            _stashSlotClearBtn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _stashSlotClearBtn.gameObject.SetActive(false);

            // Sort + Equip Best Row
            GameObject actionRow = new GameObject("StashActionRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            actionRow.transform.SetParent(rightCol, false);
            LayoutElement actLe = actionRow.AddComponent<LayoutElement>();
            actLe.preferredHeight = 36f;
            actLe.flexibleHeight = 0f;
            HorizontalLayoutGroup aHlg = actionRow.GetComponent<HorizontalLayoutGroup>();
            aHlg.spacing = 8f;
            aHlg.childControlWidth = true;
            aHlg.childControlHeight = true;
            aHlg.childForceExpandWidth = false;

            _stashSortBtn = BywaterUiHelpers.CreateButton(
                actionRow.transform,
                "StashSortBtn",
                "Sort: Upgrade",
                _font,
                () =>
                {
                    _stashSortKind = (StashSortKind)(((int)_stashSortKind + 1) % 3);
                    if (_cachedParty != null && _cachedBundle != null)
                    {
                        RefreshPartyDrawer(_cachedParty, _cachedBundle, _selectedHeroIndex);
                    }
                },
                15f);
            _stashSortBtnLabel = _stashSortBtn.GetComponentInChildren<TextMeshProUGUI>();
            _stashSortBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 160f;

            _equipBestHeroBtn = BywaterUiHelpers.CreateButton(
                actionRow.transform,
                "EquipBestHeroBtn",
                "Equip Best (Hero)",
                _font,
                () => OnEquipBestHeroRequested?.Invoke(_selectedHeroIndex),
                15f);
            _equipBestHeroBtn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _equipBestPartyBtn = BywaterUiHelpers.CreateButton(
                actionRow.transform,
                "EquipBestPartyBtn",
                "Equip Best (Party)",
                _font,
                () => OnEquipBestPartyRequested?.Invoke(),
                15f);
            _equipBestPartyBtn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Inline "Who drinks?" Target Picker Bar (hidden until player clicks Use on a potion)
            RectTransform pickerRt = BywaterUiHelpers.CreatePanel(rightCol, "PotionTargetPickerBar", BywaterUiHelpers.ButtonActiveBg, withBrassOutline: true, outlinePx: 1.4f);
            LayoutElement pickerLe = pickerRt.gameObject.AddComponent<LayoutElement>();
            pickerLe.preferredHeight = 68f;
            pickerLe.flexibleHeight = 0f;
            _potionPickerBar = pickerRt.gameObject;

            VerticalLayoutGroup pVlg = pickerRt.gameObject.AddComponent<VerticalLayoutGroup>();
            pVlg.padding = new RectOffset(8, 8, 4, 4);
            pVlg.spacing = 4f;
            pVlg.childControlWidth = true;
            pVlg.childControlHeight = true;

            GameObject pickerHdrRow = new GameObject("PickerHeaderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            pickerHdrRow.transform.SetParent(pickerRt, false);
            pickerHdrRow.AddComponent<LayoutElement>().preferredHeight = 22f;
            HorizontalLayoutGroup phHlg = pickerHdrRow.GetComponent<HorizontalLayoutGroup>();
            phHlg.spacing = 6f;
            phHlg.childControlWidth = true;
            phHlg.childControlHeight = true;
            phHlg.childForceExpandWidth = false;

            _potionPickerTitle = BywaterUiHelpers.CreateLabel(pickerHdrRow.transform, "PickerTitle", "Who uses Potion?", _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _potionPickerTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Button cancelPicker = BywaterUiHelpers.CreateButton(pickerHdrRow.transform, "CancelPickerBtn", "Cancel", _font, ClosePotionPicker, 15f);
            cancelPicker.gameObject.AddComponent<LayoutElement>().preferredWidth = 80f;

            GameObject heroBtnRow = new GameObject("PickerHeroesRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            heroBtnRow.transform.SetParent(pickerRt, false);
            heroBtnRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            HorizontalLayoutGroup hrHlg = heroBtnRow.GetComponent<HorizontalLayoutGroup>();
            hrHlg.spacing = 4f;
            hrHlg.childControlWidth = true;
            hrHlg.childControlHeight = true;
            hrHlg.childForceExpandWidth = true;

            for (int i = 0; i < 6; i++)
            {
                int heroIdx = i;
                Button hb = BywaterUiHelpers.CreateButton(
                    heroBtnRow.transform,
                    $"PotionHeroBtn_{i}",
                    $"H{i + 1}",
                    _font,
                    () =>
                    {
                        int idxToUse = _pendingPotionStashIdx;
                        ClosePotionPicker();
                        if (idxToUse >= 0)
                        {
                            OnUseStashItemRequested?.Invoke(heroIdx, idxToUse);
                        }
                    },
                    15f);
                TextMeshProUGUI hLbl = hb.GetComponentInChildren<TextMeshProUGUI>();
                if (hLbl != null) hLbl.overflowMode = TextOverflowModes.Truncate;
                _potionPickerHeroBtns.Add(hb);
            }

            _potionPickerBar.SetActive(false);
        }

        private void AddStashFilterButton(Transform parent, StashFilterKind kind, string label, float width)
        {
            Button btn = BywaterUiHelpers.CreateButton(
                parent,
                $"Filter_{kind}",
                label,
                _font,
                () =>
                {
                    _stashFilterKind = kind;
                    if (_cachedParty != null && _cachedBundle != null)
                    {
                        RefreshPartyDrawer(_cachedParty, _cachedBundle, _selectedHeroIndex);
                    }
                },
                15f);
            btn.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
            _stashFilterBtns[kind] = btn;
        }

        private void ToggleStashSlotFilter(EquipmentSlot slot)
        {
            _stashSlotFilter = _stashSlotFilter == slot ? (EquipmentSlot?)null : slot;
            if (_stashSlotFilter.HasValue && _stashFilterKind != StashFilterKind.All && _stashFilterKind != StashFilterKind.Gear)
            {
                _stashFilterKind = StashFilterKind.Gear;
            }
            if (_cachedParty != null && _cachedBundle != null)
            {
                RefreshPartyDrawer(_cachedParty, _cachedBundle, _selectedHeroIndex);
            }
        }

        private void OpenPotionPicker(int stashIndex, string itemName)
        {
            if (_potionPickerBar == null || _cachedParty == null)
            {
                OnUseStashItemRequested?.Invoke(_selectedHeroIndex, stashIndex);
                return;
            }

            _pendingPotionStashIdx = stashIndex;
            if (_potionPickerTitle != null)
            {
                _potionPickerTitle.text = $"Who drinks / uses {itemName}?";
            }

            for (int i = 0; i < _potionPickerHeroBtns.Count; i++)
            {
                Button btn = _potionPickerHeroBtns[i];
                if (i >= _cachedParty.Members.Count)
                {
                    btn.gameObject.SetActive(false);
                    continue;
                }

                btn.gameObject.SetActive(true);
                PartyMemberState m = _cachedParty.Members[i];
                TextMeshProUGUI lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null)
                {
                    lbl.text = $"{m.Name} {m.Hp}/{m.MaxHp}";
                }
                bool injured = m.IsAlive && m.Hp < m.MaxHp;
                BywaterUiHelpers.SetButtonColors(btn, injured || i == _selectedHeroIndex);
            }

            _potionPickerBar.SetActive(true);
            PlayModalPopIn(_potionPickerBar, 0.94f, 0.12f);
        }

        private void ClosePotionPicker()
        {
            _pendingPotionStashIdx = -1;
            ResetModalPopIn(_potionPickerBar);
            if (_potionPickerBar != null)
            {
                _potionPickerBar.SetActive(false);
            }
        }

        private void RefreshUxOverhaulDrawer(PartyState party, BywaterM1Bundle bundle, PartyMemberState hero, bool isSafeZone)
        {
            IReadOnlyDictionary<int, ItemDefinition> items = bundle != null ? bundle.Items : null;

            // Header Camp Pill Badges
            int charges = party.QuickBandageCharges;
            if (_campBandageBadge != null)
            {
                _campBandageBadge.text = $"{charges}/{PartyState.MaxQuickBandageCharges}";
                _campBandageBadge.color = charges > 0 ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.DangerRed;
            }
            if (_campRestBadge != null)
            {
                _campRestBadge.text = isSafeZone ? "Free" : $"{party.GetTotalTorchCharges()}x";
                _campRestBadge.color = isSafeZone || party.GetTotalTorchCharges() > 0 || party.TorchTurnsRemaining >= 15
                    ? BywaterUiHelpers.BrassHighlight
                    : BywaterUiHelpers.DangerRed;
            }

            // Hero Selector Tabs with [LVL+] badge indicator
            for (int i = 0; i < _partyHeroSelectorBtns.Count; i++)
            {
                if (i >= party.Members.Count) continue;
                PartyMemberState m = party.Members[i];
                TextMeshProUGUI lbl = _partyHeroSelectorBtns[i].GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null)
                {
                    lbl.text = m.HasPendingLevelUp ? $"{m.Name} [LVL+]" : m.Name;
                    lbl.color = m.HasPendingLevelUp ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.ParchmentText;
                }
            }

            // Hero XP Progress & Level Up Button
            int curFloor = PartyMemberState.GetXpRequiredForLevel(hero.Level);
            int nextReq = hero.XpForNextLevel;
            int span = Math.Max(1, nextReq - curFloor);
            float xpRatio = hero.Level >= PartyMemberState.MaxLevel
                ? 1f
                : Mathf.Clamp01((hero.Xp - curFloor) / (float)span);
            if (_drawerXpFill != null)
            {
                _drawerXpFill.rectTransform.anchorMax = new Vector2(xpRatio, 1f);
            }
            if (_drawerXpText != null)
            {
                _drawerXpText.text = hero.HasPendingLevelUp
                    ? $"XP {hero.Xp} / {nextReq} - LEVEL UP READY!"
                    : $"XP {hero.Xp} / {nextReq} (Next Lv {Math.Min(PartyMemberState.MaxLevel, hero.Level + 1)})";
            }
            if (_drawerLvlUpBtn != null)
            {
                _drawerLvlUpBtn.gameObject.SetActive(hero.HasPendingLevelUp);
            }

            // Carry Weight / Encumbrance
            int wt = party.GetTotalCarriedWeight(items);
            int cap = party.GetPartyCarryCapacity();
            bool encumbered = items != null && party.IsEncumbered(items);
            if (_drawerCarryWeightText != null)
            {
                _drawerCarryWeightText.text = encumbered
                    ? $"ENCUMBERED {wt}/{cap} (-2 MV)"
                    : $"Wt: {wt} / {cap}";
                _drawerCarryWeightText.color = encumbered ? BywaterUiHelpers.DangerRed : BywaterUiHelpers.MutedSlate;
            }

            // 9-Slot Paper-Doll Refresh
            for (int i = 0; i < _paperDollSlots.Count; i++)
            {
                PaperDollSlotRefs sr = _paperDollSlots[i];
                InventoryItem eq = hero.GetEquipped(sr.Slot);
                sr.BoundEntry = eq;
                sr.BoundDef = eq != null && items != null && items.TryGetValue(eq.ItemId, out ItemDefinition foundDef) ? foundDef : null;

                bool isFilterActive = _stashSlotFilter.HasValue && _stashSlotFilter.Value == sr.Slot;
                if (sr.BoundDef != null)
                {
                    string setTag = FormatItemSetProgressTag(hero, sr.BoundDef);
                    string titleLine = string.IsNullOrEmpty(setTag) ? sr.BoundDef.Name : $"{sr.BoundDef.Name} {setTag}";
                    string stats = FormatItemStats(sr.BoundDef);
                    sr.ItemLabel.text = string.IsNullOrEmpty(stats) ? titleLine : $"{titleLine}\n{stats}";
                    sr.ItemLabel.color = BywaterUiHelpers.ParchmentText;
                    Sprite sp = _catalog != null ? _catalog.GetItemIcon(sr.BoundDef.Id, sr.BoundDef.Name) : null;
                    sr.Icon.sprite = sp;
                    sr.Icon.gameObject.SetActive(sp != null);
                }
                else
                {
                    sr.ItemLabel.text = "- Empty -";
                    sr.ItemLabel.color = BywaterUiHelpers.MutedSlate;
                    sr.Icon.sprite = null;
                    sr.Icon.gameObject.SetActive(false);
                }

                if (BywaterUiHelpers.HasChromeSprites)
                {
                    if (isFilterActive)
                    {
                        BywaterUiHelpers.ApplySurfaceState(sr.Background, true);
                    }
                    else if (sr.BoundDef != null)
                    {
                        BywaterUiHelpers.ApplySurfaceState(sr.Background, false);
                    }
                    else
                    {
                        sr.Background.sprite = BywaterUiHelpers.StoneWellSprite ?? BywaterUiHelpers.StoneBarSprite;
                        sr.Background.type = Image.Type.Sliced;
                        sr.Background.pixelsPerUnitMultiplier = BywaterUiHelpers.ChromePpuMultiplier;
                        sr.Background.color = Color.white;
                    }
                }
                else
                {
                    sr.Background.color = isFilterActive
                        ? BywaterUiHelpers.CardSelectedBg
                        : (sr.BoundDef != null ? BywaterUiHelpers.CardBg : new Color(0.055f, 0.078f, 0.138f, 0.90f));
                }
                BywaterUiHelpers.SetBorderColor(
                    sr.Root,
                    isFilterActive
                        ? BywaterUiHelpers.BrassHighlight
                        : (sr.BoundDef != null ? BywaterUiHelpers.BrassAccent : new Color(0.28f, 0.34f, 0.45f, 0.85f)));
            }

            if (_paperDollSummaryText != null)
            {
                IReadOnlyList<EquipmentSetProgress> activeSets = hero.GetActiveEquipmentSets();
                if (activeSets != null && activeSets.Count > 0)
                {
                    _paperDollSummaryText.text = FormatEquipmentSetsCompact(hero);
                    _paperDollSummaryText.color = BywaterUiHelpers.BrassHighlight;
                    _paperDollSummaryText.alignment = TextAlignmentOptions.TopLeft;
                }
                else
                {
                    _paperDollSummaryText.text = "No active set bonuses";
                    _paperDollSummaryText.color = BywaterUiHelpers.MutedSlate;
                    _paperDollSummaryText.alignment = TextAlignmentOptions.Center;
                }
            }

            // Character Sheet Tab Refresh
            RefreshCharacterSheet(party, bundle, hero, encumbered);

            // Stash Filter / Sort Buttons State
            foreach (KeyValuePair<StashFilterKind, Button> kvp in _stashFilterBtns)
            {
                BywaterUiHelpers.SetButtonColors(kvp.Value, kvp.Key == _stashFilterKind);
            }
            if (_stashSortBtnLabel != null)
            {
                _stashSortBtnLabel.text = $"Sort: {_stashSortKind}";
            }
            if (_stashSlotClearBtn != null)
            {
                bool hasSlotFilt = _stashSlotFilter.HasValue;
                _stashSlotClearBtn.gameObject.SetActive(hasSlotFilt);
                if (hasSlotFilt && _stashSlotClearLabel != null)
                {
                    _stashSlotClearLabel.text = $"{_stashSlotFilter.Value} x";
                }
            }
        }

        private void RefreshCharacterSheet(PartyState party, BywaterM1Bundle bundle, PartyMemberState hero, bool encumbered)
        {
            IReadOnlyDictionary<int, ItemDefinition> items = bundle != null ? bundle.Items : null;
            int[] values = { hero.Brawn, hero.Knowledge, hero.Judgment, hero.Agility, hero.Vitality, hero.Luck };
            for (int i = 0; i < _charSheetStatRows.Count && i < values.Length; i++)
            {
                _charSheetStatRows[i].Label.text = $"{_charSheetStatRows[i].Name}: {values[i]}";
            }

            if (_charSheetDerivedText != null)
            {
                int ac = hero.GetEffectiveAc(items);
                int dodgeAc = hero.GetDodgeAcBonus();
                int meleeHit = hero.GetEffectiveMeleeToHit(items);
                int rangedHit = hero.GetEffectiveRangedToHit(items);
                int meleeDmgBonus = hero.GetMeleeDamageAttributeBonus();
                string dice = hero.GetEffectiveDamageDice(items);
                int move = hero.GetEffectiveMovementMax(items, encumbered);
                int crit = hero.GetCritChancePercent();
                int magRes = hero.GetMagicResistancePercent();
                int condSave = hero.GetConditionSaveBonusPercent();
                int spellPow = hero.GetArcaneSpellPowerBonus();
                int healBonus = hero.GetDivineHealingBonus();

                _charSheetDerivedText.text =
                    $"AC: {ac} (Base {hero.BaseAc} + Dodge +{dodgeAc} + Gear)\n" +
                    $"Melee Hit: +{meleeHit}  |  Dmg: {dice}{(meleeDmgBonus > 0 ? "+" + meleeDmgBonus : "")}\n" +
                    $"Ranged Hit: +{rangedHit}  |  Move: {move}{(encumbered ? " (-2 Enc)" : "")}\n" +
                    $"Attacks/Rnd: {hero.AttacksPerRound:0.0}  |  Crit: {crit}%\n" +
                    $"Arcane Pwr: +{spellPow}  |  Divine Heal: +{healBonus}\n" +
                    $"Magic Res: {magRes}%  |  Status Save: +{condSave}%";
            }

            if (_charSheetExtraText != null)
            {
                int carry = hero.GetCarryCapacityContribution();
                string setsLine = FormatEquipmentSetsCompact(hero);
                _charSheetExtraText.text =
                    $"Hero Carry Cap: {carry} wt (Party Avg Luck Gold: +{party.GetGoldLootBonusPercent()}%)\n" +
                    $"{setsLine}\n" +
                    $"{_drawerHeroConditions.text}\n{_drawerHeroSpells.text}";
            }
        }

        private (string, string) BuildAttributeTooltip(int statIdx)
        {
            if (_cachedParty == null || _selectedHeroIndex < 0 || _selectedHeroIndex >= _cachedParty.Members.Count)
            {
                return (string.Empty, string.Empty);
            }

            PartyMemberState h = _cachedParty.Members[_selectedHeroIndex];
            switch (statIdx)
            {
                case 0:
                    return ($"Brawn ({h.Brawn})",
                        $"Melee Damage Bonus: +{h.GetMeleeDamageAttributeBonus()}\n" +
                        $"Carry Weight Contribution: +{h.GetCarryCapacityContribution()} wt\n" +
                        "Improves Shove force and heavy gear handling.");
                case 1:
                    return ($"Knowledge ({h.Knowledge})",
                        $"Arcane Spell Power: +{h.GetArcaneSpellPowerBonus()}\n" +
                        $"Spell Hit / Save DC Shift: {(h.GetSpellHitDcBonusPercent() >= 0 ? "+" : "")}{h.GetSpellHitDcBonusPercent()}%\n" +
                        "Governs Sorcerer/Mage SP growth per level.");
                case 2:
                    return ($"Judgment ({h.Judgment})",
                        $"Divine Healing Bonus: +{h.GetDivineHealingBonus()}\n" +
                        $"Magic Resistance Bonus: +{Math.Max(0, (h.Judgment - 10) * 2)}%\n" +
                        "Governs Cleric/Paladin SP growth per level.");
                case 3:
                    return ($"Agility ({h.Agility})",
                        $"Combat Initiative: {10 + h.Agility}\n" +
                        $"Dodge AC Bonus: +{h.GetDodgeAcBonus()}\n" +
                        $"Ranged Hit Bonus: +{h.GetRangedHitAgilityBonusPercent()}%");
                case 4:
                    return ($"Vitality ({h.Vitality})",
                        $"HP per Level Bonus: +{Math.Max(0, (h.Vitality - 10) / 2)}\n" +
                        $"Poison / Condition Save Bonus: +{Math.Max(0, (h.Vitality - 10) * 2)}%");
                default:
                    return ($"Luck ({h.Luck})",
                        $"Critical Hit Chance: {h.GetCritChancePercent()}%\n" +
                        $"All-Saves Bonus: +{h.GetAllSavesLuckBonusPercent()}%\n" +
                        $"Party Gold Find Bonus: +{_cachedParty.GetGoldLootBonusPercent()}%");
            }
        }

        private List<int> BuildFilteredSortedStashIndices(PartyState party, IReadOnlyDictionary<int, ItemDefinition> items, PartyMemberState hero)
        {
            _stashDisplayOrder.Clear();
            for (int i = 0; i < party.Stash.Count; i++)
            {
                InventoryItem entry = party.Stash[i];
                ItemDefinition def = items != null && items.TryGetValue(entry.ItemId, out ItemDefinition found) ? found : null;

                if (_stashSlotFilter.HasValue)
                {
                    if (def == null || !PartyMemberState.TryParseSlot(def.Slot, out EquipmentSlot eqSlot))
                    {
                        continue;
                    }
                    bool match = eqSlot == _stashSlotFilter.Value ||
                        (PartyMemberState.IsAccessorySlot(eqSlot) && PartyMemberState.IsAccessorySlot(_stashSlotFilter.Value));
                    if (!match) continue;
                }

                if (_stashFilterKind != StashFilterKind.All && def != null)
                {
                    bool isGear = PartyMemberState.TryParseSlot(def.Slot, out _);
                    bool isPotion = string.Equals(def.Category, "Potion", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(def.Slot, "Consumable", StringComparison.OrdinalIgnoreCase);
                    if (_stashFilterKind == StashFilterKind.Gear && !isGear) continue;
                    if (_stashFilterKind == StashFilterKind.Potions && !isPotion) continue;
                    if (_stashFilterKind == StashFilterKind.Supplies && (isGear || isPotion)) continue;
                }

                _stashDisplayOrder.Add(i);
            }

            _stashDisplayOrder.Sort((a, b) =>
            {
                InventoryItem ea = party.Stash[a];
                InventoryItem eb = party.Stash[b];
                ItemDefinition da = items != null && items.TryGetValue(ea.ItemId, out ItemDefinition fa) ? fa : null;
                ItemDefinition db = items != null && items.TryGetValue(eb.ItemId, out ItemDefinition fb) ? fb : null;

                if (_stashSortKind == StashSortKind.Upgrade)
                {
                    bool ua = IsStashEntryUpgradeForHero(hero, da, items);
                    bool ub = IsStashEntryUpgradeForHero(hero, db, items);
                    int cmpUpg = ub.CompareTo(ua);
                    if (cmpUpg != 0) return cmpUpg;
                    double sa = da != null ? PartyState.ScoreItemForHero(hero, da) : 0.0;
                    double sb = db != null ? PartyState.ScoreItemForHero(hero, db) : 0.0;
                    int cmpScore = sb.CompareTo(sa);
                    if (cmpScore != 0) return cmpScore;
                }
                else if (_stashSortKind == StashSortKind.Slot)
                {
                    string sla = da != null ? (da.Slot ?? "Z") : "Z";
                    string slb = db != null ? (db.Slot ?? "Z") : "Z";
                    int cmpSlot = string.Compare(sla, slb, StringComparison.OrdinalIgnoreCase);
                    if (cmpSlot != 0) return cmpSlot;
                }
                else if (_stashSortKind == StashSortKind.Value)
                {
                    int pa = da != null ? da.BasePrice : 0;
                    int pb = db != null ? db.BasePrice : 0;
                    int cmpVal = pb.CompareTo(pa);
                    if (cmpVal != 0) return cmpVal;
                }

                return string.Compare(da != null ? da.Name : string.Empty, db != null ? db.Name : string.Empty, StringComparison.OrdinalIgnoreCase);
            });

            return _stashDisplayOrder;
        }

        private static bool IsStashEntryUpgradeForHero(PartyMemberState hero, ItemDefinition candidate, IReadOnlyDictionary<int, ItemDefinition> items)
        {
            if (hero == null || candidate == null || items == null)
            {
                return false;
            }
            if (!PartyMemberState.TryParseSlot(candidate.Slot, out EquipmentSlot slot))
            {
                return false;
            }
            if (!PartyState.CanHeroEquip(hero, candidate, out _))
            {
                return false;
            }

            EquipmentSlot targetSlot = slot;
            if (PartyMemberState.IsAccessorySlot(slot) && hero.GetEquipped(slot) != null)
            {
                EquipmentSlot sibling = slot == EquipmentSlot.Accessory1 ? EquipmentSlot.Accessory2 : EquipmentSlot.Accessory1;
                if (hero.GetEquipped(sibling) == null)
                {
                    targetSlot = sibling;
                }
            }

            double candScore = PartyState.ScoreItemForHero(hero, candidate);
            if (candScore <= 0.01)
            {
                return false;
            }

            InventoryItem eq = hero.GetEquipped(targetSlot);
            if (eq == null)
            {
                return true;
            }
            if (!items.TryGetValue(eq.ItemId, out ItemDefinition current))
            {
                return true;
            }

            return candScore > PartyState.ScoreItemForHero(hero, current) + 0.01;
        }

        private static string FormatEquipmentSetsCompact(PartyMemberState hero)
        {
            if (hero == null)
            {
                return "Sets: None";
            }

            IReadOnlyList<EquipmentSetProgress> activeSets = hero.GetActiveEquipmentSets();
            if (activeSets == null || activeSets.Count == 0)
            {
                return "Sets: None";
            }

            List<string> entries = new List<string>(activeSets.Count);
            for (int i = 0; i < activeSets.Count; i++)
            {
                EquipmentSetProgress prog = activeSets[i];
                List<string> unlocked = new List<string>(prog.Definition.Tiers.Count);
                List<string> pending = new List<string>(prog.Definition.Tiers.Count);
                for (int t = 0; t < prog.Definition.Tiers.Count; t++)
                {
                    EquipmentSetBonusTier tier = prog.Definition.Tiers[t];
                    if (prog.EquippedPieces >= tier.RequiredPieces)
                    {
                        unlocked.Add(tier.Summary);
                    }
                    else
                    {
                        pending.Add($"{tier.RequiredPieces}pc: {tier.Summary}");
                    }
                }

                string header = $"{prog.Definition.Name} [{prog.EquippedPieces}/{prog.TargetPieces}]";
                if (unlocked.Count > 0 && pending.Count > 0)
                {
                    entries.Add($"{header}: {string.Join(", ", unlocked)} (Next {pending[0]})");
                }
                else if (unlocked.Count > 0)
                {
                    entries.Add($"{header}: {string.Join(", ", unlocked)}");
                }
                else if (pending.Count > 0)
                {
                    entries.Add($"{header}: Next {pending[0]}");
                }
            }

            return string.Join("\n", entries);
        }

        private static string FormatItemSetProgressTag(PartyMemberState hero, ItemDefinition item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            IReadOnlyList<EquipmentSetDefinition> itemSets = EquipmentSetCatalog.GetSetsForItem(item.Id);
            if (itemSets == null || itemSets.Count == 0)
            {
                return string.Empty;
            }

            if (itemSets.Count == 1)
            {
                EquipmentSetDefinition def = itemSets[0];
                EquipmentSetProgress prog = hero != null ? hero.GetEquipmentSetProgress(def.SetId) : null;
                int equipped = prog != null ? prog.EquippedPieces : 0;
                int target = prog != null ? prog.TargetPieces : def.DisplayMaxPieces;
                return $"[{equipped}/{target}]";
            }

            List<string> tags = new List<string>(itemSets.Count);
            for (int i = 0; i < itemSets.Count; i++)
            {
                EquipmentSetDefinition def = itemSets[i];
                EquipmentSetProgress prog = hero != null ? hero.GetEquipmentSetProgress(def.SetId) : null;
                int equipped = prog != null ? prog.EquippedPieces : 0;
                int target = prog != null ? prog.TargetPieces : def.DisplayMaxPieces;
                tags.Add($"[{equipped}/{target}]");
            }

            return string.Join(" ", tags);
        }

        private static string FormatItemSetTooltipSection(PartyMemberState hero, ItemDefinition item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            IReadOnlyList<EquipmentSetDefinition> itemSets = EquipmentSetCatalog.GetSetsForItem(item.Id);
            if (itemSets == null || itemSets.Count == 0)
            {
                return string.Empty;
            }

            List<string> lines = new List<string>(itemSets.Count);
            for (int i = 0; i < itemSets.Count; i++)
            {
                EquipmentSetDefinition setDef = itemSets[i];
                EquipmentSetProgress prog = hero != null ? hero.GetEquipmentSetProgress(setDef.SetId) : null;
                int equippedCount = prog != null ? prog.EquippedPieces : 0;
                int targetPieces = prog != null ? prog.TargetPieces : setDef.DisplayMaxPieces;

                List<string> tierParts = new List<string>(setDef.Tiers.Count);
                for (int t = 0; t < setDef.Tiers.Count; t++)
                {
                    EquipmentSetBonusTier tier = setDef.Tiers[t];
                    string activeMark = equippedCount >= tier.RequiredPieces ? " [ON]" : string.Empty;
                    tierParts.Add($"{tier.RequiredPieces}pc: {tier.Summary}{activeMark}");
                }

                string progressLabel = hero != null
                    ? $"[{equippedCount}/{targetPieces}]"
                    : $"[{targetPieces} pcs]";
                lines.Add($"Set: {setDef.Name} {progressLabel}\n  {string.Join(" | ", tierParts)}");
            }

            return string.Join("\n", lines);
        }

        private string BuildGearComparisonTooltip(PartyMemberState hero, ItemDefinition candidate, IReadOnlyDictionary<int, ItemDefinition> items)
        {
            if (hero == null || candidate == null)
            {
                return string.Empty;
            }

            string stats = FormatItemStats(candidate);
            string setInfo = FormatItemSetTooltipSection(hero, candidate);
            string setBlock = string.IsNullOrEmpty(setInfo) ? string.Empty : $"\n{setInfo}";
            if (!PartyMemberState.TryParseSlot(candidate.Slot, out EquipmentSlot slot))
            {
                return $"{stats}{setBlock}\n{candidate.Description}";
            }

            if (!PartyState.CanHeroEquip(hero, candidate, out string restrictReason))
            {
                return $"{stats}\n[{restrictReason}]{setBlock}\n\n{candidate.Description}";
            }

            InventoryItem eq = hero.GetEquipped(slot);
            if (PartyMemberState.IsAccessorySlot(slot) && eq != null)
            {
                EquipmentSlot sibling = slot == EquipmentSlot.Accessory1 ? EquipmentSlot.Accessory2 : EquipmentSlot.Accessory1;
                if (hero.GetEquipped(sibling) == null)
                {
                    eq = null;
                    slot = sibling;
                }
            }

            string compareLine;
            if (eq == null || items == null || !items.TryGetValue(eq.ItemId, out ItemDefinition current))
            {
                compareLine = $"Equipped ({slot}): Empty\n[UPGRADE for {hero.Name}]";
            }
            else
            {
                int acDelta = candidate.ArmorClassBonus - current.ArmorClassBonus;
                int hitDelta = (candidate.WeaponBonus - current.WeaponBonus) * 5;
                int mvDelta = candidate.MovementMod - current.MovementMod;
                bool isUpg = IsStashEntryUpgradeForHero(hero, candidate, items);
                compareLine = $"vs {hero.Name}'s {current.Name}:\n" +
                    $"  AC {(acDelta >= 0 ? "+" : "")}{acDelta}  |  Hit {(hitDelta >= 0 ? "+" : "")}{hitDelta}%  |  Move {(mvDelta >= 0 ? "+" : "")}{mvDelta}" +
                    (isUpg ? $"\n[UPGRADE for {hero.Name}]" : string.Empty);
            }

            return $"{stats}\n{compareLine}{setBlock}\n\n{candidate.Description}";
        }

        // =====================================================================
        // Level-Up Modal (#9)
        // =====================================================================
        private void BuildLevelUpModal(Transform parent)
        {
            RectTransform modal = BywaterUiHelpers.CreatePanel(parent, "LevelUpModal", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 2.2f);
            modal.anchorMin = new Vector2(0.5f, 0.5f);
            modal.anchorMax = new Vector2(0.5f, 0.5f);
            modal.pivot = new Vector2(0.5f, 0.5f);
            modal.anchoredPosition = Vector2.zero;
            modal.sizeDelta = new Vector2(720f, 620f);
            _levelUpModalRoot = modal.gameObject;

            VerticalLayoutGroup vlg = modal.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 18, 18);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            GameObject hdrRow = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            hdrRow.transform.SetParent(modal, false);
            hdrRow.AddComponent<LayoutElement>().preferredHeight = 76f;
            HorizontalLayoutGroup hHlg = hdrRow.GetComponent<HorizontalLayoutGroup>();
            hHlg.spacing = 12f;
            hHlg.childControlWidth = true;
            hHlg.childControlHeight = true;
            hHlg.childForceExpandWidth = false;

            BywaterUiHelpers.CreatePortraitFrame(hdrRow.transform, "LevelUpPortrait", 72f, out _levelUpPortrait);

            GameObject titleCol = new GameObject("TitleCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            titleCol.transform.SetParent(hdrRow.transform, false);
            titleCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            VerticalLayoutGroup tvlg = titleCol.GetComponent<VerticalLayoutGroup>();
            tvlg.spacing = 4f;
            tvlg.childControlWidth = true;
            tvlg.childControlHeight = true;

            _levelUpTitle = BywaterUiHelpers.CreateDisplayLabel(titleCol.transform, "LevelUpTitle", "LEVEL UP!", _font, 22f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _levelUpPointsLabel = BywaterUiHelpers.CreateLabel(titleCol.transform, "LevelUpPoints", "Allocate 2 Attribute Points", _font, 16f, BywaterUiHelpers.SuccessGreen, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);

            Button closeBtn = BywaterUiHelpers.CreateButton(hdrRow.transform, "CloseBtn", "Later", _font, CloseLevelUpModal, 15.5f);
            closeBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;

            BywaterUiHelpers.CreateHeaderDivider(modal, "LevelUpHeaderRule");

            string[] keys = { "Brawn", "Knowledge", "Judgment", "Agility", "Vitality", "Luck" };
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                RectTransform row = BywaterUiHelpers.CreatePanel(modal, $"LevelUpStat_{keys[i]}", BywaterUiHelpers.CardBg, withBrassOutline: false);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
                HorizontalLayoutGroup rHlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                rHlg.padding = new RectOffset(10, 10, 4, 4);
                rHlg.spacing = 10f;
                rHlg.childControlWidth = true;
                rHlg.childControlHeight = true;
                rHlg.childForceExpandWidth = false;

                TextMeshProUGUI nLbl = BywaterUiHelpers.CreateLabel(row, "StatName", keys[i], _font, 16f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                nLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                TextMeshProUGUI valLbl = BywaterUiHelpers.CreateLabel(row, "StatValue", "10", _font, 16f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
                valLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 64f;

                Button minusBtn = BywaterUiHelpers.CreateButton(row, "MinusBtn", "-", _font, () => AdjustLevelUpPoint(idx, -1), 17f);
                minusBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;

                Button plusBtn = BywaterUiHelpers.CreateButton(row, "PlusBtn", "+", _font, () => AdjustLevelUpPoint(idx, 1), 17f);
                plusBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;

                TextMeshProUGUI dLbl = BywaterUiHelpers.CreateLabel(row, "StatDelta", "+0", _font, 16f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
                dLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 56f;

                _levelUpStatRows.Add(new LevelUpStatRowRefs
                {
                    Key = keys[i],
                    Label = keys[i],
                    NameText = nLbl,
                    ValueText = valLbl,
                    DeltaText = dLbl,
                    MinusBtn = minusBtn,
                    PlusBtn = plusBtn
                });
            }

            RectTransform previewBox = BywaterUiHelpers.CreatePanel(modal, "LevelUpPreviewBox", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            previewBox.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            VerticalLayoutGroup pVlg = previewBox.gameObject.AddComponent<VerticalLayoutGroup>();
            pVlg.padding = new RectOffset(12, 12, 8, 8);
            pVlg.spacing = 4f;
            pVlg.childControlWidth = true;
            pVlg.childControlHeight = true;

            _levelUpPreviewText = BywaterUiHelpers.CreateLabel(previewBox, "PreviewGains", string.Empty, _font, 15.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.TopLeft, wrap: true);
            _levelUpPreviewText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            _levelUpSpellText = BywaterUiHelpers.CreateLabel(previewBox, "SpellUnlock", string.Empty, _font, 15.5f, BywaterUiHelpers.InfoCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: true);
            _levelUpSpellText.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            GameObject footer = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            footer.transform.SetParent(modal, false);
            footer.AddComponent<LayoutElement>().preferredHeight = 46f;
            HorizontalLayoutGroup fHlg = footer.GetComponent<HorizontalLayoutGroup>();
            fHlg.spacing = 12f;
            fHlg.childControlWidth = true;
            fHlg.childControlHeight = true;
            fHlg.childForceExpandWidth = true;

            _levelUpRecommendedBtn = BywaterUiHelpers.CreateButton(footer.transform, "RecommendedBtn", "Recommended Auto-Fill", _font, ApplyRecommendedLevelUpPoints, 15.5f);
            _levelUpConfirmBtn = BywaterUiHelpers.CreateButton(footer.transform, "ConfirmLevelUpBtn", "Confirm Level Up", _font, ConfirmLevelUpSelection, 16f);
            BywaterUiHelpers.SetButtonColors(_levelUpConfirmBtn, true);

            _levelUpModalRoot.SetActive(false);
        }

        public void OpenLevelUpModal(int heroIndex)
        {
            if (!_uiBuilt || _levelUpModalRoot == null || _cachedParty == null ||
                heroIndex < 0 || heroIndex >= _cachedParty.Members.Count)
            {
                return;
            }

            PartyMemberState hero = _cachedParty.Members[heroIndex];
            if (!hero.HasPendingLevelUp)
            {
                return;
            }

            _levelUpHeroIndex = heroIndex;
            Array.Clear(_levelUpAllocDeltas, 0, _levelUpAllocDeltas.Length);
            ApplyRecommendedLevelUpPoints();

            if (_partyDrawerRoot != null) _partyDrawerRoot.SetActive(false);
            if (_outcomeModalRoot != null) _outcomeModalRoot.SetActive(false);
            _levelUpModalRoot.SetActive(true);
            _levelUpModalRoot.transform.SetAsLastSibling();
            RefreshLevelUpModal();
            PlayModalPopIn(_levelUpModalRoot, 0.90f, 0.16f);
            SyncModalScrim();
        }

        public void CloseLevelUpModal()
        {
            ResetModalPopIn(_levelUpModalRoot);
            if (_levelUpModalRoot != null)
            {
                _levelUpModalRoot.SetActive(false);
            }
            SyncModalScrim();
        }

        private void ApplyRecommendedLevelUpPoints()
        {
            if (_cachedParty == null || _levelUpHeroIndex < 0 || _levelUpHeroIndex >= _cachedParty.Members.Count)
            {
                return;
            }
            PartyMemberState hero = _cachedParty.Members[_levelUpHeroIndex];
            AttributeAllocation rec = hero.GetRecommendedAttributeAllocation();
            _levelUpAllocDeltas[0] = rec.BrawnDelta;
            _levelUpAllocDeltas[1] = rec.KnowledgeDelta;
            _levelUpAllocDeltas[2] = rec.JudgmentDelta;
            _levelUpAllocDeltas[3] = rec.AgilityDelta;
            _levelUpAllocDeltas[4] = rec.VitalityDelta;
            _levelUpAllocDeltas[5] = rec.LuckDelta;
            RefreshLevelUpModal();
        }

        private void AdjustLevelUpPoint(int statIdx, int delta)
        {
            if (_cachedParty == null || _levelUpHeroIndex < 0 || _levelUpHeroIndex >= _cachedParty.Members.Count)
            {
                return;
            }

            int total = 0;
            for (int i = 0; i < 6; i++) total += _levelUpAllocDeltas[i];
            if (delta > 0 && total >= 2) return;
            if (delta < 0 && _levelUpAllocDeltas[statIdx] <= 0) return;

            PartyMemberState hero = _cachedParty.Members[_levelUpHeroIndex];
            int[] baseVals = { hero.Brawn, hero.Knowledge, hero.Judgment, hero.Agility, hero.Vitality, hero.Luck };
            if (delta > 0 && baseVals[statIdx] + _levelUpAllocDeltas[statIdx] >= 25) return;

            _levelUpAllocDeltas[statIdx] += delta;
            RefreshLevelUpModal();
        }

        private AttributeAllocation BuildCurrentLevelUpAllocation()
        {
            return new AttributeAllocation
            {
                BrawnDelta = _levelUpAllocDeltas[0],
                KnowledgeDelta = _levelUpAllocDeltas[1],
                JudgmentDelta = _levelUpAllocDeltas[2],
                AgilityDelta = _levelUpAllocDeltas[3],
                VitalityDelta = _levelUpAllocDeltas[4],
                LuckDelta = _levelUpAllocDeltas[5]
            };
        }

        private void RefreshLevelUpModal()
        {
            if (_cachedParty == null || _levelUpHeroIndex < 0 || _levelUpHeroIndex >= _cachedParty.Members.Count)
            {
                return;
            }

            PartyMemberState hero = _cachedParty.Members[_levelUpHeroIndex];
            int spent = 0;
            for (int i = 0; i < 6; i++) spent += _levelUpAllocDeltas[i];
            int remaining = Math.Max(0, 2 - spent);

            _levelUpPortrait.sprite = ResolveHeroPortrait(hero.Id, hero.PortraitId, hero.Archetype);
            _levelUpPortrait.preserveAspect = true;
            _levelUpTitle.text = $"{hero.Name} - Level {hero.Level} -> Level {hero.Level + 1} ({hero.Archetype})";
            _levelUpPointsLabel.text = remaining > 0
                ? $"Points Left: {remaining} / 2"
                : "Points Left: 0 (Ready to Confirm)";
            _levelUpPointsLabel.color = remaining == 0 ? BywaterUiHelpers.SuccessGreen : BywaterUiHelpers.BrassHighlight;

            int[] baseVals = { hero.Brawn, hero.Knowledge, hero.Judgment, hero.Agility, hero.Vitality, hero.Luck };
            for (int i = 0; i < _levelUpStatRows.Count && i < 6; i++)
            {
                LevelUpStatRowRefs r = _levelUpStatRows[i];
                int curVal = baseVals[i] + _levelUpAllocDeltas[i];
                r.ValueText.text = curVal.ToString();
                r.DeltaText.text = _levelUpAllocDeltas[i] > 0 ? $"+{_levelUpAllocDeltas[i]}" : "+0";
                r.DeltaText.color = _levelUpAllocDeltas[i] > 0 ? BywaterUiHelpers.SuccessGreen : BywaterUiHelpers.MutedSlate;
                r.MinusBtn.interactable = _levelUpAllocDeltas[i] > 0;
                r.PlusBtn.interactable = remaining > 0 && curVal < 25;
            }

            AttributeAllocation alloc = BuildCurrentLevelUpAllocation();
            LevelUpPreview prev = hero.GetLevelUpPreview(alloc);
            _levelUpPreviewText.text =
                $"Level Gains:   Max HP +{prev.HpGain} ({hero.MaxHp} -> {hero.MaxHp + prev.HpGain})   " +
                $"Max SP +{prev.SpGain}   To-Hit +{prev.ToHitGain}%\n" +
                $"New Stats: Brawn {hero.Brawn + alloc.BrawnDelta} | Knowledge {hero.Knowledge + alloc.KnowledgeDelta} | " +
                $"Judgment {hero.Judgment + alloc.JudgmentDelta} | Agility {hero.Agility + alloc.AgilityDelta} | " +
                $"Vitality {hero.Vitality + alloc.VitalityDelta} | Luck {hero.Luck + alloc.LuckDelta}";

            _levelUpChosenSpellId = prev.UnlockableSpellIds.Count > 0 ? prev.UnlockableSpellIds[0] : (int?)null;
            if (_levelUpChosenSpellId.HasValue && _cachedBundle != null &&
                _cachedBundle.Spells.TryGetValue(_levelUpChosenSpellId.Value, out SpellDefinition unlockedSpell))
            {
                _levelUpSpellText.text = $"Spell Unlocked: {unlockedSpell.Name} ({unlockedSpell.SpCost} SP) - {unlockedSpell.Description}";
            }
            else
            {
                _levelUpSpellText.text = "Spell Progression: No new spell this level.";
            }

            if (_levelUpConfirmBtn != null)
            {
                _levelUpConfirmBtn.interactable = remaining == 0 && hero.HasPendingLevelUp;
            }
        }

        private void ConfirmLevelUpSelection()
        {
            if (_cachedParty == null || _levelUpHeroIndex < 0 || _levelUpHeroIndex >= _cachedParty.Members.Count)
            {
                return;
            }

            AttributeAllocation alloc = BuildCurrentLevelUpAllocation();
            if (alloc.TotalPoints != 2)
            {
                return;
            }

            OnConfirmLevelUpRequested?.Invoke(_levelUpHeroIndex, alloc, _levelUpChosenSpellId);
        }

        // =====================================================================
        // Rich 980x690 Victory Screen (#10)
        // =====================================================================
        private void BuildRichVictoryLayout(RectTransform modal, Transform buttonsRow)
        {
            _outcomeModalRect = modal;
            GameObject richRoot = new GameObject("RichVictoryRoot", typeof(RectTransform), typeof(VerticalLayoutGroup));
            richRoot.transform.SetParent(modal, false);
            richRoot.transform.SetSiblingIndex(buttonsRow.GetSiblingIndex());
            richRoot.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _outcomeRichVictoryRoot = richRoot;

            VerticalLayoutGroup rv = richRoot.GetComponent<VerticalLayoutGroup>();
            rv.spacing = 6f;
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandHeight = false;

            // Banner Sub-Row: Encounter Info + Gold Readout
            GameObject infoBar = new GameObject("VictoryBannerRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            infoBar.transform.SetParent(richRoot.transform, false);
            LayoutElement ibLe = infoBar.AddComponent<LayoutElement>();
            ibLe.preferredHeight = 28f;
            ibLe.flexibleHeight = 0f;
            HorizontalLayoutGroup ibHlg = infoBar.GetComponent<HorizontalLayoutGroup>();
            ibHlg.spacing = 6f;
            ibHlg.childControlWidth = true;
            ibHlg.childControlHeight = true;
            ibHlg.childForceExpandWidth = false;

            _victorySubtitleText = BywaterUiHelpers.CreateLabel(infoBar.transform, "VictorySubtitle", "Skirmish Cleared", _font, 16f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _victorySubtitleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Sprite goldSprite = _catalog != null ? (_catalog.GetHotbarIcon("gold_coins") ?? _catalog.GetHotbarIcon("gold")) : null;
            _victoryGoldIcon = BywaterUiHelpers.CreateImage(infoBar.transform, "GoldIcon", goldSprite ?? BywaterUiHelpers.GetProceduralIconSprite("gold_coins"), Color.white, preserveAspect: true);
            _victoryGoldIcon.raycastTarget = false;
            LayoutElement giLe = _victoryGoldIcon.gameObject.AddComponent<LayoutElement>();
            giLe.preferredWidth = 28f;
            giLe.preferredHeight = 28f;

            _victoryGoldText = BywaterUiHelpers.CreateLabel(infoBar.transform, "GoldText", "+0g", _font, 16.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_victoryGoldText);
            ContentSizeFitter goldCsf = _victoryGoldText.gameObject.AddComponent<ContentSizeFitter>();
            goldCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            goldCsf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            // 6-Hero Portrait & Dual-Segment XP Grid (3 rows x 2 cols)
            GameObject heroGrid = new GameObject("VictoryHeroGrid", typeof(RectTransform), typeof(GridLayoutGroup));
            heroGrid.transform.SetParent(richRoot.transform, false);
            LayoutElement hgLe = heroGrid.AddComponent<LayoutElement>();
            hgLe.preferredHeight = 256f;
            hgLe.flexibleHeight = 0f;
            GridLayoutGroup hg = heroGrid.GetComponent<GridLayoutGroup>();
            hg.cellSize = new Vector2(462f, 80f);
            hg.spacing = new Vector2(12f, 6f);
            hg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            hg.constraintCount = 2;

            for (int i = 0; i < 6; i++)
            {
                RectTransform card = BywaterUiHelpers.CreatePanel(heroGrid.transform, $"VictoryHero_{i}", BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1.4f);
                HorizontalLayoutGroup ch = card.gameObject.AddComponent<HorizontalLayoutGroup>();
                ch.padding = new RectOffset(8, 8, 4, 4);
                ch.spacing = 8f;
                ch.childControlWidth = true;
                ch.childControlHeight = true;
                ch.childForceExpandWidth = false;

                BywaterUiHelpers.CreatePortraitFrame(card, "Portrait", 60f, out Image pImg);

                GameObject right = new GameObject("Right", typeof(RectTransform), typeof(VerticalLayoutGroup));
                right.transform.SetParent(card, false);
                right.AddComponent<LayoutElement>().flexibleWidth = 1f;
                VerticalLayoutGroup rVlg = right.GetComponent<VerticalLayoutGroup>();
                rVlg.spacing = 2f;
                rVlg.childControlWidth = true;
                rVlg.childControlHeight = true;
                rVlg.childForceExpandHeight = false;

                GameObject topRow = new GameObject("Top", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                topRow.transform.SetParent(right.transform, false);
                topRow.AddComponent<LayoutElement>().preferredHeight = 22f;
                HorizontalLayoutGroup th = topRow.GetComponent<HorizontalLayoutGroup>();
                th.spacing = 6f;
                th.childControlWidth = true;
                th.childControlHeight = true;
                th.childForceExpandWidth = false;

                TextMeshProUGUI nameLbl = BywaterUiHelpers.CreateLabel(topRow.transform, "Name", $"Hero {i + 1}", _font, 15.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                nameLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 108f;

                TextMeshProUGUI lvlLbl = BywaterUiHelpers.CreateLabel(topRow.transform, "Lv", "Lv1", _font, 15f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                lvlLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;

                Image lvlUpIcon = BywaterUiHelpers.CreateImage(topRow.transform, "LvlUpArrowBadge", BywaterUiHelpers.GetProceduralIconSprite("level_up"), BywaterUiHelpers.BrassHighlight, preserveAspect: true);
                lvlUpIcon.raycastTarget = true;
                LayoutElement lvlUpLe = lvlUpIcon.gameObject.AddComponent<LayoutElement>();
                lvlUpLe.preferredWidth = 20f;
                lvlUpLe.preferredHeight = 20f;
                AttachTooltipCallbacks(lvlUpIcon.gameObject, () => ("Level Up Ready!", "This hero earned enough XP to advance a level."));
                lvlUpIcon.gameObject.SetActive(false);

                Image reviveIcon = BywaterUiHelpers.CreateImage(topRow.transform, "ReviveBadgeIcon", BywaterUiHelpers.GetProceduralIconSprite("revive"), Color.white, preserveAspect: true);
                reviveIcon.raycastTarget = true;
                LayoutElement revLe = reviveIcon.gameObject.AddComponent<LayoutElement>();
                revLe.preferredWidth = 20f;
                revLe.preferredHeight = 20f;
                AttachTooltipCallbacks(reviveIcon.gameObject, () => ("Revived After Battle", "Fallen in battle - revived at 1 HP (50% XP)"));
                reviveIcon.gameObject.SetActive(false);

                GameObject topSpacer = new GameObject("TopSpacer", typeof(RectTransform), typeof(LayoutElement));
                topSpacer.transform.SetParent(topRow.transform, false);
                topSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

                RectTransform tagRt = BywaterUiHelpers.CreatePanel(topRow.transform, "StatusTag", new Color(0.28f, 0.21f, 0.05f, 0.96f), withBrassOutline: true, outlinePx: 1f);
                tagRt.gameObject.AddComponent<LayoutElement>().preferredWidth = 1f;
                TextMeshProUGUI tagLbl = BywaterUiHelpers.CreateLabel(tagRt, "TagText", string.Empty, _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
                tagLbl.rectTransform.anchorMin = Vector2.zero;
                tagLbl.rectTransform.anchorMax = Vector2.one;
                tagLbl.rectTransform.offsetMin = Vector2.zero;
                tagLbl.rectTransform.offsetMax = Vector2.zero;
                tagRt.gameObject.SetActive(false);

                // Dual-segment XP progress bar
                RectTransform xpTrack = BywaterUiHelpers.CreatePanel(right.transform, "XpTrack", BywaterUiHelpers.BarTrackColor, withBrassOutline: true, outlinePx: 1f);
                xpTrack.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

                Image baseFill = BywaterUiHelpers.CreateImage(xpTrack, "XpBaseFill", BywaterUiHelpers.BarFillSprite, new Color(0.24f, 0.42f, 0.72f, 1f), preserveAspect: false);
                if (BywaterUiHelpers.BarFillSprite != null)
                {
                    baseFill.type = Image.Type.Sliced;
                    baseFill.pixelsPerUnitMultiplier = BywaterUiHelpers.ChromePpuMultiplier;
                }
                baseFill.rectTransform.anchorMin = Vector2.zero;
                baseFill.rectTransform.anchorMax = new Vector2(0.2f, 1f);
                baseFill.rectTransform.offsetMin = Vector2.zero;
                baseFill.rectTransform.offsetMax = Vector2.zero;

                Image gainFill = BywaterUiHelpers.CreateImage(xpTrack, "XpGainFill", BywaterUiHelpers.BarFillSprite, BywaterUiHelpers.BrassHighlight, preserveAspect: false);
                if (BywaterUiHelpers.BarFillSprite != null)
                {
                    gainFill.type = Image.Type.Sliced;
                    gainFill.pixelsPerUnitMultiplier = BywaterUiHelpers.ChromePpuMultiplier;
                }
                gainFill.rectTransform.anchorMin = new Vector2(0.2f, 0f);
                gainFill.rectTransform.anchorMax = new Vector2(0.4f, 1f);
                gainFill.rectTransform.offsetMin = Vector2.zero;
                gainFill.rectTransform.offsetMax = Vector2.zero;

                TextMeshProUGUI xpTxt = BywaterUiHelpers.CreateLabel(xpTrack, "XpText", "+0 XP", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
                BywaterUiHelpers.AddTextDropShadow(xpTxt);
                xpTxt.rectTransform.anchorMin = Vector2.zero;
                xpTxt.rectTransform.anchorMax = Vector2.one;
                xpTxt.rectTransform.offsetMin = Vector2.zero;
                xpTxt.rectTransform.offsetMax = Vector2.zero;

                TextMeshProUGUI perfLbl = BywaterUiHelpers.CreateLabel(right.transform, "Perf", string.Empty, _font, 15f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.MidlineLeft, wrap: false);
                perfLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

                _victoryHeroCards.Add(new VictoryHeroCardRefs
                {
                    Root = card.gameObject,
                    Background = card.GetComponent<Image>(),
                    Portrait = pImg,
                    NameText = nameLbl,
                    LevelBadgeText = lvlLbl,
                    LvlUpIconGo = lvlUpIcon.gameObject,
                    LvlUpIcon = lvlUpIcon,
                    ReviveIconGo = reviveIcon.gameObject,
                    ReviveIcon = reviveIcon,
                    StatusTagText = tagLbl,
                    StatusTagGo = tagRt.gameObject,
                    XpBaseFill = baseFill,
                    XpGainFill = gainFill,
                    XpText = xpTxt,
                    PerfText = perfLbl
                });
            }

            // Loot Header + Scrollable Self-Contained Selectable Cards
            TextMeshProUGUI lootHdr = BywaterUiHelpers.CreateLabel(richRoot.transform, "VictoryLootHeader", "Loot", _font, 16f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            LayoutElement lhLe = lootHdr.gameObject.AddComponent<LayoutElement>();
            lhLe.preferredHeight = 24f;
            lhLe.flexibleHeight = 0f;

            RectTransform lootBox = BywaterUiHelpers.CreatePanel(richRoot.transform, "VictoryLootTray", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            LayoutElement lootBoxLe = lootBox.gameObject.AddComponent<LayoutElement>();
            lootBoxLe.minHeight = 238f;
            lootBoxLe.preferredHeight = 244f;
            lootBoxLe.flexibleHeight = 1f;

            _victoryLootScroll = BywaterUiHelpers.CreateScrollView(lootBox, "VictoryLootScroll", _font, spacing: 6f);
            RectTransform lootScrollRt = _victoryLootScroll.GetComponent<RectTransform>();
            lootScrollRt.anchorMin = Vector2.zero;
            lootScrollRt.anchorMax = Vector2.one;
            lootScrollRt.offsetMin = new Vector2(6f, 2f);
            lootScrollRt.offsetMax = new Vector2(-6f, -2f);

            VerticalLayoutGroup oldVlg = _victoryLootScroll.content.GetComponent<VerticalLayoutGroup>();
            if (oldVlg != null) DestroyImmediate(oldVlg);
            GridLayoutGroup lootGrid = _victoryLootScroll.content.gameObject.AddComponent<GridLayoutGroup>();
            lootGrid.cellSize = new Vector2(216f, 76f);
            lootGrid.spacing = new Vector2(8f, 3f);
            lootGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            lootGrid.constraintCount = 4;
            lootGrid.padding = new RectOffset(6, 6, 2, 2);

            _victoryLootEmptyLabel = BywaterUiHelpers.CreateLabel(lootBox, "EmptyLootLabel", "No physical item drops from this skirmish.", _font, 15.5f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.Center, wrap: false).gameObject;
            RectTransform elRt = _victoryLootEmptyLabel.GetComponent<RectTransform>();
            elRt.anchorMin = Vector2.zero;
            elRt.anchorMax = Vector2.one;
            elRt.offsetMin = Vector2.zero;
            elRt.offsetMax = Vector2.zero;

            // Extra Footer Action Button (Option 2: only Level Up Now + Take Selected)
            _victoryLevelUpNowBtn = BywaterUiHelpers.CreateButton(buttonsRow, "LevelUpNowBtn", "Level Up Now!", _font, HandleVictoryLevelUpNowClick, 15.5f);
            BywaterUiHelpers.SetButtonColors(_victoryLevelUpNowBtn, true);
            _victoryLevelUpNowLabel = _victoryLevelUpNowBtn.GetComponentInChildren<TextMeshProUGUI>();

            richRoot.SetActive(false);
        }

        public void ShowVictoryOutcome(CombatVictorySummary summary, PartyState party, BywaterM1Bundle bundle)
        {
            if (!_uiBuilt || _outcomeModalRoot == null || summary == null)
            {
                return;
            }

            StopVictoryAnimation();

            _cachedVictorySummary = summary;
            _cachedParty = party ?? _cachedParty;
            _cachedBundle = bundle ?? _cachedBundle;
            _outcomeInRichVictoryMode = true;

            _selectedVictoryLootIndices.Clear();
            for (int i = 0; i < summary.LootStacks.Count; i++)
            {
                _selectedVictoryLootIndices.Add(i);
            }

            if (_outcomeModalRect != null)
            {
                _outcomeModalRect.sizeDelta = new Vector2(980f, 690f);
            }

            _outcomeTitle.text = "VICTORY!";
            _outcomeTitle.color = BywaterUiHelpers.BrassHighlight;
            if (_outcomeSummary != null) _outcomeSummary.gameObject.SetActive(false);
            if (_outcomeRichVictoryRoot != null) _outcomeRichVictoryRoot.SetActive(true);
            if (_outcomeLevelUpBanner != null) _outcomeLevelUpBanner.SetActive(false);

            bool anyReady = summary.AnyReadyToLevelUp;
            bool immediate = Application.isBatchMode || !Application.isPlaying || !gameObject.activeInHierarchy || SkipAnimations;
            List<VictoryHeroAnimEntry> animEntries = immediate ? null : new List<VictoryHeroAnimEntry>(summary.Heroes.Count);

            if (_victorySubtitleText != null)
            {
                _victorySubtitleText.text = $"Total Party XP: +{summary.TotalXpEarned}   |   Rounds: {Math.Max(1, summary.RoundsTaken)}";
            }
            if (_victoryGoldText != null)
            {
                _victoryGoldText.text = summary.LuckBonusGold > 0
                    ? $"+{summary.TotalGoldLoot}g (+{summary.LuckBonusGold}g Luck)  |  Total: {summary.PartyGoldAfter}g"
                    : $"+{summary.TotalGoldLoot}g  |  Total: {summary.PartyGoldAfter}g";
            }

            Sprite goldCoinSprite = _catalog != null ? (_catalog.GetHotbarIcon("gold_coins") ?? _catalog.GetHotbarIcon("gold")) : null;
            if (_victoryGoldIcon != null)
            {
                _victoryGoldIcon.sprite = goldCoinSprite ?? BywaterUiHelpers.GetProceduralIconSprite("gold_coins");
                _victoryGoldIcon.color = Color.white;
            }

            for (int i = 0; i < _victoryHeroCards.Count; i++)
            {
                VictoryHeroCardRefs card = _victoryHeroCards[i];
                if (i >= summary.Heroes.Count)
                {
                    card.Root.SetActive(false);
                    continue;
                }

                card.Root.SetActive(true);
                HeroVictorySummary h = summary.Heroes[i];
                card.Portrait.sprite = ResolveHeroPortrait(h.HeroId, h.PortraitId, h.Archetype);
                card.Portrait.preserveAspect = true;
                card.NameText.text = h.Name;
                card.LevelBadgeText.text = $"Lv{h.LevelAfter}";
                card.LevelBadgeText.color = h.ReadyToLevelUp ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.MutedSlate;

                if (card.LvlUpIconGo != null)
                {
                    card.LvlUpIconGo.SetActive(h.ReadyToLevelUp);
                    card.LvlUpIconGo.transform.localScale = (!immediate && h.ReadyToLevelUp)
                        ? new Vector3(0.4f, 0.4f, 1f)
                        : Vector3.one;
                }
                if (card.ReviveIconGo != null)
                {
                    card.ReviveIconGo.SetActive(h.WasFallenRevived);
                }
                if (card.StatusTagGo != null)
                {
                    card.StatusTagGo.SetActive(false);
                }

                int rawSpan = h.NextLevelXp - h.CurrentLevelFloorXp;
                float baseFrac = rawSpan > 0
                    ? Mathf.Clamp01((float)(h.XpBefore - h.CurrentLevelFloorXp) / rawSpan)
                    : (h.XpBefore >= h.CurrentLevelFloorXp ? 1f : 0f);
                float afterFrac = rawSpan > 0
                    ? Mathf.Clamp01((float)(h.XpAfter - h.CurrentLevelFloorXp) / rawSpan)
                    : (h.XpAfter >= h.CurrentLevelFloorXp ? 1f : 0f);
                if (afterFrac < baseFrac) baseFrac = 0f;

                card.XpBaseFill.rectTransform.anchorMin = Vector2.zero;
                card.XpBaseFill.rectTransform.anchorMax = new Vector2(baseFrac, 1f);
                card.XpGainFill.rectTransform.anchorMin = new Vector2(baseFrac, 0f);
                card.XpGainFill.rectTransform.anchorMax = new Vector2(immediate ? afterFrac : baseFrac, 1f);
                card.XpText.text = $"+{h.XpAwarded} XP  ({h.XpAfter}/{h.NextLevelXp})";
                card.PerfText.text = $"Dmg: {h.DamageDealt}   Kills: {h.Kills}   Heal: {h.HealingDone}";

                Color cardBorder = h.ReadyToLevelUp
                    ? BywaterUiHelpers.BrassHighlight
                    : (h.WasFallenRevived ? BywaterUiHelpers.InfoCyan : BywaterUiHelpers.BrassAccent);
                BywaterUiHelpers.SetBorderColor(card.Root.transform, cardBorder);

                if (!immediate)
                {
                    animEntries.Add(new VictoryHeroAnimEntry
                    {
                        Card = card,
                        BaseFrac = baseFrac,
                        AfterFrac = afterFrac,
                        ReadyToLevelUp = h.ReadyToLevelUp
                    });
                }
            }

            RefreshVictoryLootGrid(summary, _cachedParty, _cachedBundle);

            _outcomeContinueBtn.gameObject.SetActive(true);
            RefreshVictoryTakeSelectedButtonLabel();
            _outcomeRetryBtn.gameObject.SetActive(false);
            _outcomeReviveBtn.gameObject.SetActive(false);

            if (_victoryLevelUpNowBtn != null)
            {
                _victoryLevelUpNowBtn.gameObject.SetActive(anyReady);
                if (anyReady && _victoryLevelUpNowLabel != null)
                {
                    _victoryLevelUpNowLabel.text = $"Level Up Now ({summary.ReadyToLevelUpCount})";
                }
            }

            _outcomeModalRoot.SetActive(true);
            _outcomeModalRoot.transform.SetAsLastSibling();
            PlayModalPopIn(_outcomeModalRoot, 0.90f, 0.18f);
            SyncModalScrim();

            if (immediate)
            {
                if (anyReady)
                {
                    OnVictoryLevelUpPopped?.Invoke();
                }
            }
            else
            {
                _victoryAnimCoroutine = StartCoroutine(AnimateVictoryOutcomeCoroutine(animEntries, anyReady));
            }
        }

        private IEnumerator AnimateVictoryOutcomeCoroutine(List<VictoryHeroAnimEntry> entries, bool anyReady)
        {
            const float xpFillDuration = 0.65f;
            const float popDuration = 0.24f;

            float elapsed = 0f;
            while (elapsed < xpFillDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / xpFillDuration));
                for (int i = 0; i < entries.Count; i++)
                {
                    VictoryHeroAnimEntry e = entries[i];
                    if (e.Card?.XpGainFill != null)
                    {
                        float x = Mathf.Lerp(e.BaseFrac, e.AfterFrac, t);
                        e.Card.XpGainFill.rectTransform.anchorMax = new Vector2(x, 1f);
                    }
                }
                yield return null;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                VictoryHeroAnimEntry e = entries[i];
                if (e.Card?.XpGainFill != null)
                {
                    e.Card.XpGainFill.rectTransform.anchorMax = new Vector2(e.AfterFrac, 1f);
                }
            }

            if (anyReady)
            {
                elapsed = 0f;
                float halfPop = popDuration * 0.5f;
                while (elapsed < popDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float scale;
                    if (elapsed <= halfPop)
                    {
                        float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / halfPop));
                        scale = Mathf.Lerp(0.4f, 1.28f, u);
                    }
                    else
                    {
                        float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - halfPop) / halfPop));
                        scale = Mathf.Lerp(1.28f, 1.0f, u);
                    }

                    Vector3 vScale = new Vector3(scale, scale, 1f);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        VictoryHeroAnimEntry e = entries[i];
                        if (e.ReadyToLevelUp && e.Card?.LvlUpIconGo != null)
                        {
                            e.Card.LvlUpIconGo.transform.localScale = vScale;
                        }
                    }
                    yield return null;
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                VictoryHeroAnimEntry e = entries[i];
                if (e.Card?.LvlUpIconGo != null)
                {
                    e.Card.LvlUpIconGo.transform.localScale = Vector3.one;
                }
            }

            _victoryAnimCoroutine = null;
            if (anyReady)
            {
                OnVictoryLevelUpPopped?.Invoke();
            }
        }

        private void StopVictoryAnimation()
        {
            ResetModalPopIn(_outcomeModalRoot);
            if (_victoryAnimCoroutine != null)
            {
                StopCoroutine(_victoryAnimCoroutine);
                _victoryAnimCoroutine = null;
            }

            for (int i = 0; i < _victoryHeroCards.Count; i++)
            {
                if (_victoryHeroCards[i]?.LvlUpIconGo != null)
                {
                    _victoryHeroCards[i].LvlUpIconGo.transform.localScale = Vector3.one;
                }
            }
        }

        private void PlayModalPopIn(GameObject modalRoot, float startScale = 0.90f, float duration = 0.16f)
        {
            if (modalRoot == null)
            {
                return;
            }

            if (_modalPopCoroutine != null)
            {
                StopCoroutine(_modalPopCoroutine);
                _modalPopCoroutine = null;
            }
            if (_modalPopTarget != null && _modalPopTarget != modalRoot)
            {
                _modalPopTarget.transform.localScale = Vector3.one;
            }
            _modalPopTarget = modalRoot;

            bool immediate = Application.isBatchMode || !Application.isPlaying || !gameObject.activeInHierarchy || SkipAnimations;
            if (immediate)
            {
                modalRoot.transform.localScale = Vector3.one;
                _modalPopTarget = null;
                return;
            }

            modalRoot.transform.localScale = new Vector3(startScale, startScale, 1f);
            _modalPopCoroutine = StartCoroutine(ModalPopInRoutine(modalRoot, startScale, duration));
        }

        private void ResetModalPopIn(GameObject modalRoot)
        {
            if (_modalPopTarget == modalRoot && _modalPopCoroutine != null)
            {
                StopCoroutine(_modalPopCoroutine);
                _modalPopCoroutine = null;
                _modalPopTarget = null;
            }
            if (modalRoot != null)
            {
                modalRoot.transform.localScale = Vector3.one;
            }
        }

        private IEnumerator ModalPopInRoutine(GameObject modalRoot, float startScale, float duration)
        {
            float safeDuration = Mathf.Max(0.01f, duration);
            float elapsed = 0f;
            while (elapsed < safeDuration)
            {
                if (modalRoot == null || !modalRoot.activeSelf || SkipAnimations)
                {
                    if (modalRoot != null)
                    {
                        modalRoot.transform.localScale = Vector3.one;
                    }
                    _modalPopCoroutine = null;
                    _modalPopTarget = null;
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                float eased = EaseOutBack(t);
                float s = Mathf.LerpUnclamped(startScale, 1.0f, eased);
                modalRoot.transform.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            if (modalRoot != null)
            {
                modalRoot.transform.localScale = Vector3.one;
            }
            _modalPopCoroutine = null;
            _modalPopTarget = null;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        public bool ToggleVictoryLootSelection(int stackIndex)
        {
            if (_cachedVictorySummary == null || stackIndex < 0 || stackIndex >= _cachedVictorySummary.LootStacks.Count)
            {
                return false;
            }

            bool isSelected;
            if (_selectedVictoryLootIndices.Contains(stackIndex))
            {
                _selectedVictoryLootIndices.Remove(stackIndex);
                isSelected = false;
            }
            else
            {
                _selectedVictoryLootIndices.Add(stackIndex);
                isSelected = true;
            }

            if (stackIndex < _victoryLootTiles.Count)
            {
                UpdateVictoryLootTileSelectionVisual(_victoryLootTiles[stackIndex], isSelected,
                    _cachedVictorySummary.LootStacks[stackIndex].BasePrice >= 500,
                    _victoryLootTiles[stackIndex].UpgradeBadge != null && _victoryLootTiles[stackIndex].UpgradeBadge.activeSelf);
            }

            RefreshVictoryTakeSelectedButtonLabel();
            return isSelected;
        }

        public int[] GetSelectedVictoryLootIndices()
        {
            int[] arr = new int[_selectedVictoryLootIndices.Count];
            int idx = 0;
            foreach (int i in _selectedVictoryLootIndices)
            {
                arr[idx++] = i;
            }
            Array.Sort(arr);
            return arr;
        }

        private void RefreshVictoryTakeSelectedButtonLabel()
        {
            if (_outcomeContinueBtn == null)
            {
                return;
            }

            TextMeshProUGUI contLbl = _outcomeContinueBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (contLbl == null)
            {
                return;
            }

            int selectedCount = 0;
            if (_cachedVictorySummary != null)
            {
                for (int i = 0; i < _cachedVictorySummary.LootStacks.Count; i++)
                {
                    if (_selectedVictoryLootIndices.Contains(i))
                    {
                        selectedCount += Math.Max(1, _cachedVictorySummary.LootStacks[i].Quantity);
                    }
                }
            }

            contLbl.text = (_cachedVictorySummary != null && _cachedVictorySummary.LootStacks.Count > 0)
                ? $"Take Selected ({selectedCount})"
                : "Continue";
        }

        private static string FormatCompactVictoryStats(ItemDefinition def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(def.DamageDice)) parts.Add($"{def.DamageDice} Dmg");
            if (def.WeaponBonus != 0) parts.Add($"+{def.WeaponBonus} Hit");
            if (def.ArmorClassBonus != 0) parts.Add($"+{def.ArmorClassBonus} AC");
            if (def.Range > 1) parts.Add($"Rng {def.Range}");
            if (!string.IsNullOrWhiteSpace(def.HealDice)) parts.Add($"Heal {def.HealDice}");
            if (def.Charges > 0 && parts.Count < 3) parts.Add($"{def.Charges} chg");
            if (def.Weight > 0) parts.Add($"Wt {def.Weight}");
            return parts.Count > 0 ? string.Join(" | ", parts) : (def.Category ?? "Item");
        }

        private void UpdateVictoryLootTileSelectionVisual(VictoryLootTileRefs tile, bool isSelected, bool isHighValue, bool isUpgrade)
        {
            if (tile == null)
            {
                return;
            }

            if (tile.CheckmarkIcon != null)
            {
                tile.CheckmarkIcon.enabled = isSelected;
            }
            if (tile.SelectBoxBg != null)
            {
                if (BywaterUiHelpers.HasChromeSprites && BywaterUiHelpers.GemPipEmeraldSprite != null)
                {
                    tile.SelectBoxBg.sprite = isSelected
                        ? BywaterUiHelpers.GemPipEmeraldSprite
                        : (BywaterUiHelpers.GemPipOffSprite ?? BywaterUiHelpers.StoneWellSprite);
                    bool useSimple = isSelected || BywaterUiHelpers.GemPipOffSprite != null;
                    tile.SelectBoxBg.type = useSimple ? Image.Type.Simple : Image.Type.Sliced;
                    tile.SelectBoxBg.preserveAspect = useSimple;
                    tile.SelectBoxBg.color = Color.white;
                }
                else
                {
                    tile.SelectBoxBg.color = isSelected
                        ? new Color(0.14f, 0.38f, 0.20f, 0.98f)
                        : new Color(0.06f, 0.08f, 0.13f, 0.94f);
                }
                BywaterUiHelpers.SetBorderColor(tile.SelectBoxBg.transform, isSelected ? BywaterUiHelpers.SuccessGreen : BywaterUiHelpers.MutedSlate);
            }
            if (tile.FrameBg != null)
            {
                BywaterUiHelpers.ApplySurfaceState(tile.FrameBg, isSelected);
            }
            Color border = !isSelected
                ? new Color(0.28f, 0.32f, 0.40f, 0.85f)
                : (isHighValue ? BywaterUiHelpers.BrassHighlight : (isUpgrade ? BywaterUiHelpers.SuccessGreen : BywaterUiHelpers.BrassAccent));
            BywaterUiHelpers.SetBorderColor(tile.Root.transform, border);
        }

        private void RefreshVictoryLootGrid(CombatVictorySummary summary, PartyState party, BywaterM1Bundle bundle)
        {
            IReadOnlyDictionary<int, ItemDefinition> items = bundle != null ? bundle.Items : null;

            if (_victoryLootEmptyLabel != null)
            {
                _victoryLootEmptyLabel.SetActive(summary.LootStacks.Count == 0);
            }

            for (int i = 0; i < summary.LootStacks.Count; i++)
            {
                LootStackSummary ls = summary.LootStacks[i];
                VictoryLootTileRefs tile = GetOrCreateVictoryLootTile(i);
                tile.BoundStackIndex = i;
                tile.Root.SetActive(true);

                ItemDefinition def = items != null && items.TryGetValue(ls.ItemId, out ItemDefinition found) ? found : null;
                Sprite sp = _catalog != null ? _catalog.GetItemIcon(ls.ItemId, ls.Name) : null;
                tile.Icon.sprite = sp;
                tile.Icon.gameObject.SetActive(sp != null);
                tile.NameText.text = ls.Name;

                if (ls.Quantity > 1)
                {
                    tile.QtyBadge.SetActive(true);
                    tile.QtyText.text = $"x{ls.Quantity}";
                }
                else
                {
                    tile.QtyBadge.SetActive(false);
                }

                if (tile.StatsText != null)
                {
                    tile.StatsText.text = def != null ? FormatCompactVictoryStats(def) : ls.Category;
                }

                HeroUpgradeRecommendation rec = (party != null && def != null)
                    ? PartyState.FindBestUpgradeRecipient(party, def, items)
                    : null;
                bool isUpgrade = rec != null;
                string bestHeroName = rec != null ? rec.Hero.Name : null;

                if (tile.UpgradeBadge != null)
                {
                    tile.UpgradeBadge.SetActive(isUpgrade);
                    if (isUpgrade && tile.UpgradeHeroText != null)
                    {
                        tile.UpgradeHeroText.text = bestHeroName;
                    }
                }

                bool isSelected = _selectedVictoryLootIndices.Contains(i);
                UpdateVictoryLootTileSelectionVisual(tile, isSelected, ls.BasePrice >= 500, isUpgrade);

                PartyMemberState tooltipHero = rec != null
                    ? rec.Hero
                    : (party != null && _selectedHeroIndex >= 0 && _selectedHeroIndex < party.Members.Count
                        ? party.Members[_selectedHeroIndex]
                        : null);
                string setInfo = def != null ? FormatItemSetTooltipSection(tooltipHero, def) : string.Empty;
                string setLine = string.IsNullOrEmpty(setInfo) ? string.Empty : $"\n{setInfo}";
                string tipTitle = ls.Name + (ls.Quantity > 1 ? $" x{ls.Quantity}" : string.Empty);
                string tipBody = def != null
                    ? $"{FormatItemStats(def)}\nValue: {ls.BasePrice}g" + (bestHeroName != null ? $"\nUpgrade for {bestHeroName}" : string.Empty) + setLine + $"\n{def.Description}\n(Click card to toggle loot pickup)"
                    : $"Item #{ls.ItemId}";
                AttachTooltipCallbacks(tile.Root, () => (tipTitle, tipBody));
            }

            for (int i = summary.LootStacks.Count; i < _victoryLootTiles.Count; i++)
            {
                _victoryLootTiles[i].Root.SetActive(false);
            }

            RefreshVictoryTakeSelectedButtonLabel();
        }

        private VictoryLootTileRefs GetOrCreateVictoryLootTile(int idx)
        {
            if (idx < _victoryLootTiles.Count)
            {
                return _victoryLootTiles[idx];
            }

            RectTransform tile = BywaterUiHelpers.CreatePanel(_victoryLootScroll.content, $"LootTile_{idx}", BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1.5f);
            Button btn = tile.gameObject.AddComponent<Button>();
            btn.targetGraphic = tile.GetComponent<Image>();

            HorizontalLayoutGroup hlg = tile.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 2, 2);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;

            // Left Column: Compact Icon + Optional xQty (locked exact 34px width, flexibleWidth = 0)
            GameObject leftCol = new GameObject("LeftCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            leftCol.transform.SetParent(tile, false);
            LayoutElement leftLe = leftCol.AddComponent<LayoutElement>();
            leftLe.minWidth = 34f;
            leftLe.preferredWidth = 34f;
            leftLe.flexibleWidth = 0f;
            VerticalLayoutGroup lVlg = leftCol.GetComponent<VerticalLayoutGroup>();
            lVlg.spacing = 1f;
            lVlg.childAlignment = TextAnchor.UpperCenter;
            lVlg.childControlWidth = true;
            lVlg.childControlHeight = true;
            lVlg.childForceExpandHeight = false;

            Image iconImg = BywaterUiHelpers.CreateImage(leftCol.transform, "Icon", null, Color.white, preserveAspect: true);
            iconImg.raycastTarget = false;
            LayoutElement iLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iLe.preferredWidth = 34f;
            iLe.preferredHeight = 34f;
            iLe.flexibleWidth = 0f;

            TextMeshProUGUI qtyLbl = BywaterUiHelpers.CreateLabel(leftCol.transform, "Qty", "x1", _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
            LayoutElement qLe = qtyLbl.gameObject.AddComponent<LayoutElement>();
            qLe.preferredWidth = 34f;
            qLe.minHeight = 19f;
            qLe.preferredHeight = 19f;
            qLe.flexibleWidth = 0f;

            // Right Body: Wrapped Full Name + 18x18 Checkbox | Inline Stats | Upgrade Tag (Green Arrow + Hero)
            GameObject bodyCol = new GameObject("BodyCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            bodyCol.transform.SetParent(tile, false);
            LayoutElement bodyLe = bodyCol.AddComponent<LayoutElement>();
            bodyLe.minWidth = 148f;
            bodyLe.preferredWidth = 164f;
            bodyLe.flexibleWidth = 1f;
            VerticalLayoutGroup bVlg = bodyCol.GetComponent<VerticalLayoutGroup>();
            bVlg.spacing = 1f;
            bVlg.childAlignment = TextAnchor.UpperLeft;
            bVlg.childControlWidth = true;
            bVlg.childControlHeight = true;
            bVlg.childForceExpandWidth = true;
            bVlg.childForceExpandHeight = false;

            GameObject titleRow = new GameObject("TitleRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            titleRow.transform.SetParent(bodyCol.transform, false);
            LayoutElement trLe = titleRow.AddComponent<LayoutElement>();
            trLe.minHeight = 20f;
            trLe.flexibleWidth = 1f;
            HorizontalLayoutGroup trHlg = titleRow.GetComponent<HorizontalLayoutGroup>();
            trHlg.spacing = 4f;
            trHlg.childAlignment = TextAnchor.UpperLeft;
            trHlg.childControlWidth = true;
            trHlg.childControlHeight = true;
            trHlg.childForceExpandWidth = false;

            TextMeshProUGUI nameLbl = BywaterUiHelpers.CreateLabel(titleRow.transform, "Name", string.Empty, _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.TopLeft, FontStyles.Bold, wrap: true);
            nameLbl.lineSpacing = -8f;
            nameLbl.overflowMode = TextOverflowModes.Overflow;
            LayoutElement nameLe = nameLbl.gameObject.AddComponent<LayoutElement>();
            nameLe.minWidth = 120f;
            nameLe.flexibleWidth = 1f;

            RectTransform checkFrame = BywaterUiHelpers.CreatePanel(titleRow.transform, "SelectBox", new Color(0.14f, 0.38f, 0.20f, 0.98f), withBrassOutline: true, outlinePx: 1.2f);
            LayoutElement chkLe = checkFrame.gameObject.AddComponent<LayoutElement>();
            chkLe.minWidth = 18f;
            chkLe.preferredWidth = 18f;
            chkLe.flexibleWidth = 0f;
            chkLe.preferredHeight = 18f;

            Image chkIcon = BywaterUiHelpers.CreateImage(checkFrame, "Checkmark", BywaterUiHelpers.GetProceduralIconSprite("checkmark"), Color.white, preserveAspect: true);
            chkIcon.raycastTarget = false;
            chkIcon.rectTransform.anchorMin = new Vector2(0.12f, 0.12f);
            chkIcon.rectTransform.anchorMax = new Vector2(0.88f, 0.88f);
            chkIcon.rectTransform.offsetMin = Vector2.zero;
            chkIcon.rectTransform.offsetMax = Vector2.zero;

            TextMeshProUGUI statsLbl = BywaterUiHelpers.CreateLabel(bodyCol.transform, "Stats", string.Empty, _font, 15f, new Color(0.84f, 0.88f, 0.95f, 1f), TextAlignmentOptions.MidlineLeft, wrap: false);
            statsLbl.enableAutoSizing = true;
            statsLbl.fontSizeMin = 9.5f;
            statsLbl.fontSizeMax = 15f;
            statsLbl.overflowMode = TextOverflowModes.Ellipsis;
            LayoutElement statsLe = statsLbl.gameObject.AddComponent<LayoutElement>();
            statsLe.minHeight = 20f;
            statsLe.preferredHeight = 20f;
            statsLe.flexibleHeight = 0f;

            GameObject upgRow = new GameObject("UpgRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            upgRow.transform.SetParent(bodyCol.transform, false);
            LayoutElement upgLe = upgRow.AddComponent<LayoutElement>();
            upgLe.minHeight = 18f;
            upgLe.preferredHeight = 18f;
            upgLe.flexibleHeight = 0f;
            HorizontalLayoutGroup uHlg = upgRow.GetComponent<HorizontalLayoutGroup>();
            uHlg.spacing = 3f;
            uHlg.childControlWidth = true;
            uHlg.childControlHeight = true;
            uHlg.childForceExpandWidth = false;

            Image upgArrow = BywaterUiHelpers.CreateImage(upgRow.transform, "UpgArrow", BywaterUiHelpers.GetProceduralIconSprite("level_up"), BywaterUiHelpers.SuccessGreen, preserveAspect: true);
            upgArrow.raycastTarget = false;
            LayoutElement uaLe = upgArrow.gameObject.AddComponent<LayoutElement>();
            uaLe.preferredWidth = 14f;
            uaLe.preferredHeight = 14f;

            TextMeshProUGUI upgLbl = BywaterUiHelpers.CreateLabel(upgRow.transform, "UpgHero", string.Empty, _font, 15f, BywaterUiHelpers.SuccessGreen, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            upgLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            VictoryLootTileRefs tRef = new VictoryLootTileRefs
            {
                Root = tile.gameObject,
                CardButton = btn,
                FrameBg = tile.GetComponent<Image>(),
                SelectBoxBg = checkFrame.GetComponent<Image>(),
                CheckmarkIcon = chkIcon,
                Icon = iconImg,
                QtyText = qtyLbl,
                QtyBadge = qtyLbl.gameObject,
                UpgradeBadge = upgRow,
                UpgradeHeroText = upgLbl,
                NameText = nameLbl,
                StatsText = statsLbl,
                BoundStackIndex = idx
            };
            btn.onClick.AddListener(() => ToggleVictoryLootSelection(tRef.BoundStackIndex));
            _victoryLootTiles.Add(tRef);
            return tRef;
        }

        private void HandleVictoryLevelUpNowClick()
        {
            if (_cachedParty == null) return;
            for (int i = 0; i < _cachedParty.Members.Count; i++)
            {
                if (_cachedParty.Members[i].HasPendingLevelUp)
                {
                    OpenLevelUpModal(i);
                    return;
                }
            }
        }
    }
}
