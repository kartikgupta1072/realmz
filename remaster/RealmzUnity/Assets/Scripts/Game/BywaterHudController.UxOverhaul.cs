using System;
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
            public Image FrameBg;
            public Image Icon;
            public TextMeshProUGUI QtyText;
            public GameObject QtyBadge;
            public GameObject UpgradeBadge;
            public TextMeshProUGUI NameText;
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
        private Button _victoryEquipUpgradesBtn;
        private TextMeshProUGUI _victoryEquipUpgradesLabel;
        private Button _victoryLevelUpNowBtn;
        private TextMeshProUGUI _victoryLevelUpNowLabel;
        private CombatVictorySummary _cachedVictorySummary;
        private readonly List<int> _victoryUpgradeStashIndices = new List<int>(8);

        public bool IsLevelUpModalOpen => _levelUpModalRoot != null && _levelUpModalRoot.activeSelf;

        public event Action<int> OnEquipBestHeroRequested;
        public event Action OnEquipBestPartyRequested;
        public event Action<int, AttributeAllocation, int?> OnConfirmLevelUpRequested;

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
            le.preferredWidth = 86f;
            le.minWidth = 82f;
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
            CreateFillerSpacer(armCol);

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
            _paperDollSummaryText = BywaterUiHelpers.CreateLabel(
                hintCard,
                "HintText",
                "Click slot: Filter stash\nRight-click / 2x click: Unequip",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.Center,
                wrap: true);
            _paperDollSummaryText.rectTransform.anchorMin = Vector2.zero;
            _paperDollSummaryText.rectTransform.anchorMax = Vector2.one;
            _paperDollSummaryText.rectTransform.offsetMin = new Vector2(6f, 4f);
            _paperDollSummaryText.rectTransform.offsetMax = new Vector2(-6f, -4f);

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
            _charSheetDerivedText.gameObject.AddComponent<LayoutElement>().preferredHeight = 195f;

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
            slotLbl.overflowMode = TextOverflowModes.Truncate;
            slotLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            TextMeshProUGUI itemLbl = BywaterUiHelpers.CreateLabel(textCol.transform, "ItemName", "Empty", _font, 15f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.TopLeft, wrap: true);
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
                    return ($"{slotRef.BoundDef.Name} ({slotRef.DisplayTitle})",
                        $"{stats}\n{slotRef.BoundDef.Description}\n\n" +
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
        }

        private void ClosePotionPicker()
        {
            _pendingPotionStashIdx = -1;
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
                _campRestBadge.text = isSafeZone ? "Free" : $"{party.GetTotalTorchCharges()}t";
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
                    string stats = FormatItemStats(sr.BoundDef);
                    sr.ItemLabel.text = string.IsNullOrEmpty(stats) ? sr.BoundDef.Name : $"{sr.BoundDef.Name}\n{stats}";
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

                sr.Background.color = isFilterActive
                    ? BywaterUiHelpers.CardSelectedBg
                    : (sr.BoundDef != null ? BywaterUiHelpers.CardBg : new Color(0.055f, 0.078f, 0.138f, 0.90f));
                BywaterUiHelpers.SetBorderColor(
                    sr.Root,
                    isFilterActive
                        ? BywaterUiHelpers.BrassHighlight
                        : (sr.BoundDef != null ? BywaterUiHelpers.BrassAccent : new Color(0.28f, 0.34f, 0.45f, 0.85f)));
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
                _charSheetExtraText.text =
                    $"Hero Carry Cap: {carry} wt (Party Avg Luck Gold: +{party.GetGoldLootBonusPercent()}%)\n" +
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

        private string BuildGearComparisonTooltip(PartyMemberState hero, ItemDefinition candidate, IReadOnlyDictionary<int, ItemDefinition> items)
        {
            if (hero == null || candidate == null)
            {
                return string.Empty;
            }

            string stats = FormatItemStats(candidate);
            if (!PartyMemberState.TryParseSlot(candidate.Slot, out EquipmentSlot slot))
            {
                return $"{stats}\n{candidate.Description}";
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

            return $"{stats}\n{compareLine}\n\n{candidate.Description}";
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

            _levelUpTitle = BywaterUiHelpers.CreateLabel(titleCol.transform, "LevelUpTitle", "LEVEL UP!", _font, 22f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_levelUpTitle);
            _levelUpPointsLabel = BywaterUiHelpers.CreateLabel(titleCol.transform, "LevelUpPoints", "Allocate 2 Attribute Points", _font, 16f, BywaterUiHelpers.SuccessGreen, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);

            Button closeBtn = BywaterUiHelpers.CreateButton(hdrRow.transform, "CloseBtn", "Later", _font, CloseLevelUpModal, 15.5f);
            closeBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;

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
        }

        public void CloseLevelUpModal()
        {
            if (_levelUpModalRoot != null)
            {
                _levelUpModalRoot.SetActive(false);
            }
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
            ibHlg.spacing = 12f;
            ibHlg.childControlWidth = true;
            ibHlg.childControlHeight = true;
            ibHlg.childForceExpandWidth = false;

            _victorySubtitleText = BywaterUiHelpers.CreateLabel(infoBar.transform, "VictorySubtitle", "Skirmish Cleared", _font, 16f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _victorySubtitleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Sprite goldSprite = _catalog != null ? (_catalog.GetHotbarIcon("gold_coins") ?? _catalog.GetHotbarIcon("gold")) : null;
            _victoryGoldIcon = BywaterUiHelpers.CreateImage(infoBar.transform, "GoldIcon", goldSprite ?? BywaterUiHelpers.GetProceduralIconSprite("gold"), Color.white, preserveAspect: true);
            _victoryGoldIcon.raycastTarget = false;
            LayoutElement giLe = _victoryGoldIcon.gameObject.AddComponent<LayoutElement>();
            giLe.preferredWidth = 26f;
            giLe.preferredHeight = 26f;

            _victoryGoldText = BywaterUiHelpers.CreateLabel(infoBar.transform, "GoldText", "+0g", _font, 16.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineRight, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_victoryGoldText);
            _victoryGoldText.gameObject.AddComponent<LayoutElement>().preferredWidth = 280f;

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

                TextMeshProUGUI lvlLbl = BywaterUiHelpers.CreateLabel(topRow.transform, "Lv", "Lv1", _font, 15f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.MidlineLeft, wrap: false);
                lvlLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 46f;

                RectTransform tagRt = BywaterUiHelpers.CreatePanel(topRow.transform, "StatusTag", new Color(0.28f, 0.21f, 0.05f, 0.96f), withBrassOutline: true, outlinePx: 1f);
                tagRt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                TextMeshProUGUI tagLbl = BywaterUiHelpers.CreateLabel(tagRt, "TagText", string.Empty, _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
                tagLbl.rectTransform.anchorMin = Vector2.zero;
                tagLbl.rectTransform.anchorMax = Vector2.one;
                tagLbl.rectTransform.offsetMin = Vector2.zero;
                tagLbl.rectTransform.offsetMax = Vector2.zero;
                tagRt.gameObject.SetActive(false);

                // Dual-segment XP progress bar
                RectTransform xpTrack = BywaterUiHelpers.CreatePanel(right.transform, "XpTrack", BywaterUiHelpers.BarTrackColor, withBrassOutline: true, outlinePx: 1f);
                xpTrack.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

                Image baseFill = BywaterUiHelpers.CreateImage(xpTrack, "XpBaseFill", null, new Color(0.24f, 0.42f, 0.72f, 1f));
                baseFill.rectTransform.anchorMin = Vector2.zero;
                baseFill.rectTransform.anchorMax = new Vector2(0.2f, 1f);
                baseFill.rectTransform.offsetMin = Vector2.zero;
                baseFill.rectTransform.offsetMax = Vector2.zero;

                Image gainFill = BywaterUiHelpers.CreateImage(xpTrack, "XpGainFill", null, BywaterUiHelpers.BrassHighlight);
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
                    StatusTagText = tagLbl,
                    StatusTagGo = tagRt.gameObject,
                    XpBaseFill = baseFill,
                    XpGainFill = gainFill,
                    XpText = xpTxt,
                    PerfText = perfLbl
                });
            }

            // Loot Header + Scrollable Stacked Icon Grid
            TextMeshProUGUI lootHdr = BywaterUiHelpers.CreateLabel(richRoot.transform, "VictoryLootHeader", "Spoils of Battle (Hover icons for stats & upgrade comparisons)", _font, 15.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            LayoutElement lhLe = lootHdr.gameObject.AddComponent<LayoutElement>();
            lhLe.preferredHeight = 22f;
            lhLe.flexibleHeight = 0f;

            RectTransform lootBox = BywaterUiHelpers.CreatePanel(richRoot.transform, "VictoryLootTray", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            LayoutElement lootBoxLe = lootBox.gameObject.AddComponent<LayoutElement>();
            lootBoxLe.minHeight = 190f;
            lootBoxLe.preferredHeight = 208f;
            lootBoxLe.flexibleHeight = 1f;

            _victoryLootScroll = BywaterUiHelpers.CreateScrollView(lootBox, "VictoryLootScroll", _font, spacing: 6f);
            RectTransform lootScrollRt = _victoryLootScroll.GetComponent<RectTransform>();
            lootScrollRt.anchorMin = Vector2.zero;
            lootScrollRt.anchorMax = Vector2.one;
            lootScrollRt.offsetMin = new Vector2(6f, 6f);
            lootScrollRt.offsetMax = new Vector2(-6f, -6f);

            VerticalLayoutGroup oldVlg = _victoryLootScroll.content.GetComponent<VerticalLayoutGroup>();
            if (oldVlg != null) DestroyImmediate(oldVlg);
            GridLayoutGroup lootGrid = _victoryLootScroll.content.gameObject.AddComponent<GridLayoutGroup>();
            lootGrid.cellSize = new Vector2(142f, 84f);
            lootGrid.spacing = new Vector2(8f, 8f);
            lootGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            lootGrid.constraintCount = 6;
            lootGrid.padding = new RectOffset(4, 4, 4, 4);

            _victoryLootEmptyLabel = BywaterUiHelpers.CreateLabel(lootBox, "EmptyLootLabel", "No physical item drops from this skirmish.", _font, 15.5f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.Center, wrap: false).gameObject;
            RectTransform elRt = _victoryLootEmptyLabel.GetComponent<RectTransform>();
            elRt.anchorMin = Vector2.zero;
            elRt.anchorMax = Vector2.one;
            elRt.offsetMin = Vector2.zero;
            elRt.offsetMax = Vector2.zero;

            // Extra Footer Action Buttons
            _victoryEquipUpgradesBtn = BywaterUiHelpers.CreateButton(buttonsRow, "TakeAndEquipBtn", "Equip Upgrades", _font, HandleVictoryEquipUpgradesClick, 15.5f);
            _victoryEquipUpgradesLabel = _victoryEquipUpgradesBtn.GetComponentInChildren<TextMeshProUGUI>();

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

            _cachedVictorySummary = summary;
            _cachedParty = party ?? _cachedParty;
            _cachedBundle = bundle ?? _cachedBundle;

            if (_outcomeModalRect != null)
            {
                _outcomeModalRect.sizeDelta = new Vector2(980f, 690f);
            }

            _outcomeTitle.text = "VICTORY!";
            _outcomeTitle.color = BywaterUiHelpers.BrassHighlight;
            if (_outcomeSummary != null) _outcomeSummary.gameObject.SetActive(false);
            if (_outcomeRichVictoryRoot != null) _outcomeRichVictoryRoot.SetActive(true);

            bool anyReady = summary.AnyReadyToLevelUp;
            if (_outcomeLevelUpBanner != null)
            {
                _outcomeLevelUpBanner.SetActive(anyReady);
                if (anyReady && _outcomeLevelUpText != null)
                {
                    _outcomeLevelUpText.text = summary.ReadyToLevelUpCount == 1
                        ? "LEVEL UP READY! 1 Hero crossed an XP threshold."
                        : $"LEVEL UP READY! {summary.ReadyToLevelUpCount} Heroes crossed XP thresholds.";
                }
            }

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

                int span = Math.Max(1, h.NextLevelXp - h.CurrentLevelFloorXp);
                float baseFrac = Mathf.Clamp01((h.XpBefore - h.CurrentLevelFloorXp) / (float)span);
                float afterFrac = Mathf.Clamp01((h.XpAfter - h.CurrentLevelFloorXp) / (float)span);
                if (afterFrac < baseFrac) baseFrac = 0f;

                card.XpBaseFill.rectTransform.anchorMin = Vector2.zero;
                card.XpBaseFill.rectTransform.anchorMax = new Vector2(baseFrac, 1f);
                card.XpGainFill.rectTransform.anchorMin = new Vector2(baseFrac, 0f);
                card.XpGainFill.rectTransform.anchorMax = new Vector2(afterFrac, 1f);
                card.XpText.text = $"+{h.XpAwarded} XP  ({h.XpAfter}/{h.NextLevelXp})";
                card.PerfText.text = $"Dmg: {h.DamageDealt}   Kills: {h.Kills}   Heal: {h.HealingDone}";

                if (h.ReadyToLevelUp)
                {
                    card.StatusTagGo.SetActive(true);
                    card.StatusTagText.text = "LEVEL UP!";
                    card.StatusTagText.color = BywaterUiHelpers.BrassHighlight;
                    BywaterUiHelpers.SetBorderColor(card.Root.transform, BywaterUiHelpers.BrassHighlight);
                }
                else if (h.WasFallenRevived)
                {
                    card.StatusTagGo.SetActive(true);
                    card.StatusTagText.text = "Revived @ 1 HP";
                    card.StatusTagText.color = BywaterUiHelpers.InfoCyan;
                    BywaterUiHelpers.SetBorderColor(card.Root.transform, BywaterUiHelpers.InfoCyan);
                }
                else if (h.IsMvp)
                {
                    card.StatusTagGo.SetActive(true);
                    card.StatusTagText.text = "MVP";
                    card.StatusTagText.color = BywaterUiHelpers.SuccessGreen;
                    BywaterUiHelpers.SetBorderColor(card.Root.transform, BywaterUiHelpers.BrassAccent);
                }
                else
                {
                    card.StatusTagGo.SetActive(false);
                    BywaterUiHelpers.SetBorderColor(card.Root.transform, BywaterUiHelpers.BrassAccent);
                }
            }

            RefreshVictoryLootGrid(summary, _cachedParty, _cachedBundle);

            _outcomeContinueBtn.gameObject.SetActive(true);
            TextMeshProUGUI contLbl = _outcomeContinueBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (contLbl != null) contLbl.text = "Take All & Continue";
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
        }

        private void RefreshVictoryLootGrid(CombatVictorySummary summary, PartyState party, BywaterM1Bundle bundle)
        {
            _victoryUpgradeStashIndices.Clear();
            IReadOnlyDictionary<int, ItemDefinition> items = bundle != null ? bundle.Items : null;
            int upgradeCount = 0;

            if (_victoryLootEmptyLabel != null)
            {
                _victoryLootEmptyLabel.SetActive(summary.LootStacks.Count == 0);
            }

            for (int i = 0; i < summary.LootStacks.Count; i++)
            {
                LootStackSummary ls = summary.LootStacks[i];
                VictoryLootTileRefs tile = GetOrCreateVictoryLootTile(i);
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

                string bestHeroName = null;
                bool isUpgrade = false;
                if (party != null && def != null && items != null)
                {
                    for (int h = 0; h < party.Members.Count; h++)
                    {
                        if (IsStashEntryUpgradeForHero(party.Members[h], def, items))
                        {
                            isUpgrade = true;
                            bestHeroName = party.Members[h].Name;
                            break;
                        }
                    }
                }

                if (isUpgrade) upgradeCount++;
                tile.UpgradeBadge.SetActive(isUpgrade);

                Color border = ls.BasePrice >= 500
                    ? BywaterUiHelpers.BrassHighlight
                    : (isUpgrade ? BywaterUiHelpers.SuccessGreen : (ls.IsJunk ? BywaterUiHelpers.MutedSlate : BywaterUiHelpers.BrassAccent));
                BywaterUiHelpers.SetBorderColor(tile.Root.transform, border);

                string tipTitle = ls.Name + (ls.Quantity > 1 ? $" x{ls.Quantity}" : string.Empty);
                string tipBody = def != null
                    ? $"{FormatItemStats(def)}\nValue: {ls.BasePrice}g" + (bestHeroName != null ? $"\n[UPGRADE for {bestHeroName}]" : string.Empty) + $"\n{def.Description}"
                    : $"Item #{ls.ItemId}";
                AttachTooltipCallbacks(tile.Root, () => (tipTitle, tipBody));
            }

            for (int i = summary.LootStacks.Count; i < _victoryLootTiles.Count; i++)
            {
                _victoryLootTiles[i].Root.SetActive(false);
            }

            if (_victoryEquipUpgradesBtn != null)
            {
                _victoryEquipUpgradesBtn.gameObject.SetActive(upgradeCount > 0);
                if (_victoryEquipUpgradesLabel != null)
                {
                    _victoryEquipUpgradesLabel.text = $"Equip Upgrades ({upgradeCount})";
                }
            }
        }

        private VictoryLootTileRefs GetOrCreateVictoryLootTile(int idx)
        {
            if (idx < _victoryLootTiles.Count)
            {
                return _victoryLootTiles[idx];
            }

            RectTransform tile = BywaterUiHelpers.CreatePanel(_victoryLootScroll.content, $"LootTile_{idx}", BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1.4f);
            VerticalLayoutGroup vlg = tile.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 4);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            GameObject topRow = new GameObject("TopRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            topRow.transform.SetParent(tile, false);
            topRow.AddComponent<LayoutElement>().preferredHeight = 40f;
            HorizontalLayoutGroup th = topRow.GetComponent<HorizontalLayoutGroup>();
            th.spacing = 4f;
            th.childControlWidth = true;
            th.childControlHeight = true;
            th.childForceExpandWidth = false;

            Image iconImg = BywaterUiHelpers.CreateImage(topRow.transform, "Icon", null, Color.white, preserveAspect: true);
            iconImg.raycastTarget = false;
            LayoutElement iLe = iconImg.gameObject.AddComponent<LayoutElement>();
            iLe.preferredWidth = 36f;
            iLe.preferredHeight = 36f;

            GameObject badgesCol = new GameObject("Badges", typeof(RectTransform), typeof(VerticalLayoutGroup));
            badgesCol.transform.SetParent(topRow.transform, false);
            badgesCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            VerticalLayoutGroup bv = badgesCol.GetComponent<VerticalLayoutGroup>();
            bv.spacing = 2f;
            bv.childControlWidth = true;
            bv.childControlHeight = true;

            TextMeshProUGUI qtyLbl = BywaterUiHelpers.CreateLabel(badgesCol.transform, "Qty", "x1", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineRight, FontStyles.Bold, wrap: false);
            TextMeshProUGUI upgLbl = BywaterUiHelpers.CreateLabel(badgesCol.transform, "Upg", "UPG", _font, 15f, BywaterUiHelpers.SuccessGreen, TextAlignmentOptions.MidlineRight, FontStyles.Bold, wrap: false);

            TextMeshProUGUI nameLbl = BywaterUiHelpers.CreateLabel(tile, "Name", string.Empty, _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, wrap: false);
            nameLbl.overflowMode = TextOverflowModes.Truncate;
            nameLbl.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            VictoryLootTileRefs tRef = new VictoryLootTileRefs
            {
                Root = tile.gameObject,
                FrameBg = tile.GetComponent<Image>(),
                Icon = iconImg,
                QtyText = qtyLbl,
                QtyBadge = qtyLbl.gameObject,
                UpgradeBadge = upgLbl.gameObject,
                NameText = nameLbl
            };
            _victoryLootTiles.Add(tRef);
            return tRef;
        }

        private void HandleVictoryEquipUpgradesClick()
        {
            OnEquipBestPartyRequested?.Invoke();
            if (_cachedVictorySummary != null)
            {
                RefreshVictoryLootGrid(_cachedVictorySummary, _cachedParty, _cachedBundle);
            }
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
