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
using UnityEngine.UI;

namespace Realmz.Game
{
    public enum CombatActionMode
    {
        Move = 0,
        Melee = 1,
        Ranged = 2,
        Spell = 3,
        Potion = 4
    }

    /// <summary>
    /// Builds and manages the responsive 1920x1080 Canvas UI for City of Bywater
    /// exploration, party/stash/camp management, town shops, and tactical combat.
    /// </summary>
    public sealed class BywaterHudController : MonoBehaviour
    {
        private sealed class HeroCardRefs
        {
            public RectTransform Root;
            public Image Background;
            public Image Portrait;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI ClassText;
            public Image HpFill;
            public TextMeshProUGUI HpText;
            public Image SpFill;
            public TextMeshProUGUI SpText;
        }

        private sealed class InitiativeCardRefs
        {
            public RectTransform Root;
            public Image Background;
            public Image Portrait;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI MetaText;
        }

        private sealed class EquipSlotRefs
        {
            public EquipmentSlot Slot;
            public TextMeshProUGUI ItemLabel;
            public Button UnequipBtn;
        }

        private BywaterAssetCatalog _catalog;
        private TMP_FontAsset _font;
        private bool _uiBuilt;

        private Canvas _canvas;
        private GameObject _exploreRoot;
        private GameObject _combatRoot;

        // Top HUD bar
        private TextMeshProUGUI _topMapText;
        private TextMeshProUGUI _topGoldText;
        private TextMeshProUGUI _topTorchText;
        private TextMeshProUGUI _topTurnText;
        private Button _skirmishBtn;
        private Button _partyBtn;
        private Button _shopBtn;
        private Button _campBtn;
        private Button _shrineBtn;
        private Button _warpBtn;
        private Button _saveBtn;
        private Button _loadBtn;

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
        private int _selectedHeroIndex;
        private PartyState _cachedParty;
        private BywaterM1Bundle _cachedBundle;

        // Town Shop Modal
        private GameObject _shopModalRoot;
        private TextMeshProUGUI _shopHeaderTitle;
        private TextMeshProUGUI _shopGoldLabel;
        private Button _shopTabArmory;
        private Button _shopTabTannery;
        private Button _shopTabApothecary;
        private readonly Dictionary<string, Button> _shopCategoryBtns = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private Toggle _shopAffordableToggle;
        private TMP_InputField _shopSearchInput;
        private ScrollRect _shopBuyScroll;
        private ScrollRect _shopSellScroll;
        private string _activeShopId = "1";
        private string _activeShopCategory = "All";

        // Combat HUD
        private readonly List<InitiativeCardRefs> _initiativeCards = new List<InitiativeCardRefs>(8);
        private Image _combatActorPortrait;
        private TextMeshProUGUI _combatActorName;
        private TextMeshProUGUI _combatActorVitals;
        private TextMeshProUGUI _combatMovePip;
        private TextMeshProUGUI _combatStdPip;
        private TextMeshProUGUI _combatSwiftPip;
        private Button _combatMoveBtn;
        private Button _combatUndoBtn;
        private Button _combatMeleeBtn;
        private Button _combatRangedBtn;
        private Button _combatSpellBtn;
        private Button _combatPotionBtn;
        private Button _combatGuardBtn;
        private Button _combatEndTurnBtn;
        private Button _combatSpeedBtn;
        private TextMeshProUGUI _combatSpeedLabel;
        private GameObject _spellPickerBar;
        private Transform _spellPickerContent;
        private TextMeshProUGUI _tooltipTitle;
        private TextMeshProUGUI _tooltipBody;
        private ScrollRect _combatLogScroll;
        private int _lastLogCount = -1;

        // Outcome Modal
        private GameObject _outcomeModalRoot;
        private TextMeshProUGUI _outcomeTitle;
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
        public event Action OnSaveRequested;
        public event Action OnLoadRequested;

        public event Action<int> OnHeroCardClicked;
        public event Action<int> OnPartyHeroSelected;
        public event Action<int, EquipmentSlot> OnUnequipRequested;
        public event Action<int, int> OnEquipFromStashRequested;
        public event Action<int, int> OnUseStashItemRequested;
        public event Action<CampMode> OnCampModeRequested;

        public event Action<string> OnShopSelected;
        public event Action<string, int> OnBuyItemRequested;
        public event Action<int> OnSellItemRequested;
        public event Action OnSellAllJunkRequested;
        public event Action OnIdentifyAllRequested;
        public event Action OnTempleHealRequested;

        public event Action<CombatActionMode> OnCombatModeSelected;
        public event Action OnUndoMoveRequested;
        public event Action OnGuardRequested;
        public event Action OnEndTurnRequested;
        public event Action OnCycleSpeedRequested;
        public event Action<int> OnSpellSelected;

        public event Action OnContinueAfterVictoryRequested;
        public event Action OnRetrySkirmishRequested;
        public event Action OnReviveAtShrineRequested;

        public bool IsPartyOpen => _partyDrawerRoot != null && _partyDrawerRoot.activeSelf;
        public bool IsShopOpen => _shopModalRoot != null && _shopModalRoot.activeSelf;
        public bool IsOutcomeOpen => _outcomeModalRoot != null && _outcomeModalRoot.activeSelf;
        public bool IsModalOpen => IsPartyOpen || IsShopOpen || IsOutcomeOpen;
        public string ActiveShopId => _activeShopId;

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
            BuildOutcomeModal(_canvas.transform);
            BuildToastBanner(_canvas.transform);

            SetCombatHudActive(false);
            _partyDrawerRoot.SetActive(false);
            _shopModalRoot.SetActive(false);
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

        public void TogglePartyPanel(bool? forceOpen = null)
        {
            if (!_uiBuilt || _partyDrawerRoot == null)
            {
                return;
            }

            bool nextState = forceOpen ?? !_partyDrawerRoot.activeSelf;
            if (nextState && _shopModalRoot != null)
            {
                _shopModalRoot.SetActive(false);
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

        public void RefreshExploreHud(
            ExplorationSession session,
            BywaterM1Bundle bundle,
            GridCoord? hoveredCoord,
            BywaterTileKind? hoveredKind,
            int hoverSteps,
            string poiHint,
            bool isReachable)
        {
            if (!_uiBuilt || session == null)
            {
                return;
            }

            _cachedParty = session.Party;
            _cachedBundle = bundle;
            SetCombatHudActive(false);

            string mapTitle = session.CurrentMap.IsDungeon ? "Bywater Cavern (Dungeon 0)" : "City of Bywater";
            _topMapText.text = $"{mapTitle} ({session.Position.X}, {session.Position.Y})";
            _topGoldText.text = $"Gold: {session.Party.Gold}g";
            _topTorchText.text = $"Torch: {session.Party.TorchTurnsRemaining}t";
            _topTurnText.text = $"Turn: {session.TurnCount}";

            if (_warpBtn != null)
            {
                TextMeshProUGUI warpLabel = _warpBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (warpLabel != null)
                {
                    warpLabel.text = session.CurrentMap.IsDungeon ? "Exit Cavern" : "Warp Portal";
                }
            }

            // Update 6 party strip cards
            IReadOnlyList<PartyMemberState> members = session.Party.Members;
            for (int i = 0; i < _heroCards.Count; i++)
            {
                HeroCardRefs card = _heroCards[i];
                if (i >= members.Count)
                {
                    card.Root.gameObject.SetActive(false);
                    continue;
                }

                card.Root.gameObject.SetActive(true);
                PartyMemberState m = members[i];
                BywaterAssetCatalog.HeroArtEntry art = _catalog != null
                    ? _catalog.GetHeroArt(m.Id, m.PortraitId, m.Archetype)
                    : null;

                card.Portrait.sprite = art != null ? (art.portrait != null ? art.portrait : art.sprite) : null;
                card.Portrait.color = card.Portrait.sprite != null ? Color.white : new Color(0.2f, 0.25f, 0.35f, 1f);
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

                bool selected = IsPartyOpen && i == _selectedHeroIndex;
                card.Background.color = selected ? BywaterUiHelpers.CardSelectedBg : BywaterUiHelpers.CardBg;
                BywaterUiHelpers.SetBorderColor(card.Root, selected ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.BrassAccent);
            }

            // Hover footer chip
            if (hoveredCoord.HasValue && hoveredKind.HasValue)
            {
                string terrainLabel = FormatTileKind(hoveredKind.Value);
                _hoverCoordText.text = $"Tile ({hoveredCoord.Value.X}, {hoveredCoord.Value.Y}) - {terrainLabel}";
                if (!string.IsNullOrEmpty(poiHint))
                {
                    _hoverDetailText.text = isReachable
                        ? $"{poiHint} | {hoverSteps} step(s) (Click to move)"
                        : $"{poiHint} | Blocked";
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
                _hoverDetailText.text = "Left-Click: Walk | Right-Drag / WASD: Pan | Scroll: Zoom | C: Recenter";
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
            _drawerGoldTorchLabel.text = $"Gold: {party.Gold}g   |   Torch: {party.TorchTurnsRemaining} turns";

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
            BywaterAssetCatalog.HeroArtEntry art = _catalog != null
                ? _catalog.GetHeroArt(hero.Id, hero.PortraitId, hero.Archetype)
                : null;
            _drawerHeroPortrait.sprite = art != null ? (art.portrait != null ? art.portrait : art.sprite) : null;
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

            // 5 equipment slots
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
                }
                else
                {
                    slotRef.ItemLabel.text = "- Empty -";
                    slotRef.ItemLabel.color = BywaterUiHelpers.MutedSlate;
                    slotRef.UnequipBtn.interactable = false;
                }
            }

            // Shared Stash rows
            ClearChildren(_partyStashScroll.content);
            if (party.Stash.Count == 0)
            {
                TextMeshProUGUI empty = BywaterUiHelpers.CreateLabel(
                    _partyStashScroll.content,
                    "EmptyStash",
                    "Party stash is empty.",
                    _font,
                    14f,
                    BywaterUiHelpers.MutedSlate);
                LayoutElement le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 30f;
                return;
            }

            for (int i = 0; i < party.Stash.Count; i++)
            {
                int stashIdx = i;
                InventoryItem entry = party.Stash[i];
                ItemDefinition def = items != null && items.TryGetValue(entry.ItemId, out ItemDefinition found) ? found : null;
                string itemName = def != null ? def.Name : $"Item #{entry.ItemId}";
                if (!entry.IsIdentified)
                {
                    itemName += " [Unidentified]";
                }

                string stats = def != null ? FormatItemStats(def) : string.Empty;
                int sellPrice = def != null ? TownShopService.GetSellPrice(def) : 1;

                RectTransform row = BywaterUiHelpers.CreatePanel(
                    _partyStashScroll.content,
                    $"StashRow_{i}",
                    BywaterUiHelpers.CardBg,
                    withBrassOutline: false);
                LayoutElement rowLe = row.gameObject.AddComponent<LayoutElement>();
                rowLe.preferredHeight = 46f;

                HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(8, 8, 4, 4);
                hlg.spacing = 6f;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;

                TextMeshProUGUI info = BywaterUiHelpers.CreateLabel(
                    row,
                    "Info",
                    $"{itemName} x{entry.Quantity}  [{sellPrice}g]{(string.IsNullOrEmpty(stats) ? "" : " - " + stats)}",
                    _font,
                    13.5f,
                    BywaterUiHelpers.ParchmentText,
                    TextAlignmentOptions.MidlineLeft,
                    wrap: false);
                LayoutElement infoLe = info.gameObject.AddComponent<LayoutElement>();
                infoLe.flexibleWidth = 1f;

                bool canEquip = def != null && PartyMemberState.TryParseSlot(def.Slot, out _);
                if (canEquip)
                {
                    Button eqBtn = BywaterUiHelpers.CreateButton(
                        row,
                        "EquipBtn",
                        "Equip",
                        _font,
                        () => OnEquipFromStashRequested?.Invoke(_selectedHeroIndex, stashIdx),
                        13f);
                    LayoutElement eqLe = eqBtn.gameObject.AddComponent<LayoutElement>();
                    eqLe.preferredWidth = 72f;
                }

                bool canUse = def != null && (!string.IsNullOrWhiteSpace(def.HealDice) ||
                    string.Equals(def.Category, "Potion", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(def.Name, "Torch", StringComparison.OrdinalIgnoreCase));
                if (canUse)
                {
                    Button useBtn = BywaterUiHelpers.CreateButton(
                        row,
                        "UseBtn",
                        "Use",
                        _font,
                        () => OnUseStashItemRequested?.Invoke(_selectedHeroIndex, stashIdx),
                        13f);
                    LayoutElement useLe = useBtn.gameObject.AddComponent<LayoutElement>();
                    useLe.preferredWidth = 64f;
                }
            }
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

            _shopHeaderTitle.text = $"{shop.Name} (Shop #{shop.ShopId})";
            _shopGoldLabel.text = $"Party Gold: {party.Gold}g";

            BywaterUiHelpers.SetButtonColors(_shopTabArmory, _activeShopId == "1");
            BywaterUiHelpers.SetButtonColors(_shopTabTannery, _activeShopId == "4");
            BywaterUiHelpers.SetButtonColors(_shopTabApothecary, _activeShopId == "10");

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
            ClearChildren(_shopBuyScroll.content);

            if (stock.Count == 0)
            {
                TextMeshProUGUI empty = BywaterUiHelpers.CreateLabel(
                    _shopBuyScroll.content,
                    "EmptyStock",
                    "No items match the current filter.",
                    _font,
                    14f,
                    BywaterUiHelpers.MutedSlate);
                LayoutElement le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 32f;
            }
            else
            {
                for (int i = 0; i < stock.Count; i++)
                {
                    ShopStockView entry = stock[i];
                    int itemId = entry.Item.Id;
                    string stats = FormatItemStats(entry.Item);

                    RectTransform row = BywaterUiHelpers.CreatePanel(
                        _shopBuyScroll.content,
                        $"BuyRow_{itemId}_{i}",
                        BywaterUiHelpers.CardBg,
                        withBrassOutline: false);
                    LayoutElement rowLe = row.gameObject.AddComponent<LayoutElement>();
                    rowLe.preferredHeight = 48f;

                    HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    hlg.padding = new RectOffset(8, 8, 4, 4);
                    hlg.spacing = 8f;
                    hlg.childControlWidth = true;
                    hlg.childControlHeight = true;
                    hlg.childForceExpandWidth = false;
                    hlg.childForceExpandHeight = true;

                    TextMeshProUGUI label = BywaterUiHelpers.CreateLabel(
                        row,
                        "Name",
                        $"{entry.Item.Name}{(string.IsNullOrEmpty(stats) ? "" : " (" + stats + ")")}",
                        _font,
                        13.5f,
                        entry.CanAfford ? BywaterUiHelpers.ParchmentText : BywaterUiHelpers.MutedSlate,
                        TextAlignmentOptions.MidlineLeft,
                        wrap: false);
                    LayoutElement labelLe = label.gameObject.AddComponent<LayoutElement>();
                    labelLe.flexibleWidth = 1f;

                    TextMeshProUGUI priceLbl = BywaterUiHelpers.CreateLabel(
                        row,
                        "Price",
                        $"{entry.EffectivePrice}g",
                        _font,
                        14f,
                        entry.CanAfford ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.DangerRed,
                        TextAlignmentOptions.MidlineRight,
                        FontStyles.Bold,
                        wrap: false);
                    LayoutElement priceLe = priceLbl.gameObject.AddComponent<LayoutElement>();
                    priceLe.preferredWidth = 68f;

                    Button buyBtn = BywaterUiHelpers.CreateButton(
                        row,
                        "BuyBtn",
                        "Buy",
                        _font,
                        () => OnBuyItemRequested?.Invoke(shop.ShopId, itemId),
                        13f);
                    buyBtn.interactable = entry.CanAfford;
                    LayoutElement buyLe = buyBtn.gameObject.AddComponent<LayoutElement>();
                    buyLe.preferredWidth = 70f;
                }
            }

            // Sell Stash list
            ClearChildren(_shopSellScroll.content);
            if (party.Stash.Count == 0)
            {
                TextMeshProUGUI empty = BywaterUiHelpers.CreateLabel(
                    _shopSellScroll.content,
                    "EmptySell",
                    "Stash is empty.",
                    _font,
                    14f,
                    BywaterUiHelpers.MutedSlate);
                LayoutElement le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 32f;
            }
            else
            {
                for (int i = 0; i < party.Stash.Count; i++)
                {
                    int stashIdx = i;
                    InventoryItem entry = party.Stash[i];
                    ItemDefinition def = bundle.Items.TryGetValue(entry.ItemId, out ItemDefinition found) ? found : null;
                    string itemName = def != null ? def.Name : $"Item #{entry.ItemId}";
                    if (!entry.IsIdentified) itemName += " [Unid]";
                    int sellPrice = def != null ? TownShopService.GetSellPrice(def) : 1;

                    RectTransform row = BywaterUiHelpers.CreatePanel(
                        _shopSellScroll.content,
                        $"SellRow_{i}",
                        BywaterUiHelpers.CardBg,
                        withBrassOutline: false);
                    LayoutElement rowLe = row.gameObject.AddComponent<LayoutElement>();
                    rowLe.preferredHeight = 46f;

                    HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    hlg.padding = new RectOffset(8, 8, 4, 4);
                    hlg.spacing = 6f;
                    hlg.childControlWidth = true;
                    hlg.childControlHeight = true;
                    hlg.childForceExpandWidth = false;
                    hlg.childForceExpandHeight = true;

                    TextMeshProUGUI lbl = BywaterUiHelpers.CreateLabel(
                        row,
                        "Item",
                        $"{itemName} x{entry.Quantity}",
                        _font,
                        13.5f,
                        BywaterUiHelpers.ParchmentText,
                        TextAlignmentOptions.MidlineLeft,
                        wrap: false);
                    LayoutElement lblLe = lbl.gameObject.AddComponent<LayoutElement>();
                    lblLe.flexibleWidth = 1f;

                    TextMeshProUGUI valLbl = BywaterUiHelpers.CreateLabel(
                        row,
                        "Val",
                        $"+{sellPrice}g",
                        _font,
                        13.5f,
                        BywaterUiHelpers.SuccessGreen,
                        TextAlignmentOptions.MidlineRight,
                        FontStyles.Bold,
                        wrap: false);
                    LayoutElement valLe = valLbl.gameObject.AddComponent<LayoutElement>();
                    valLe.preferredWidth = 62f;

                    Button sellBtn = BywaterUiHelpers.CreateButton(
                        row,
                        "SellBtn",
                        "Sell 1",
                        _font,
                        () => OnSellItemRequested?.Invoke(stashIdx),
                        13f);
                    LayoutElement sellLe = sellBtn.gameObject.AddComponent<LayoutElement>();
                    sellLe.preferredWidth = 70f;
                }
            }
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

            // Top bar keeps gold/turn live
            _topMapText.text = $"Tactical Combat - Round {combat.RoundNumber}";
            if (combat.Party != null)
            {
                _topGoldText.text = $"Gold: {combat.Party.Gold}g";
                _topTorchText.text = $"Torch: {combat.Party.TorchTurnsRemaining}t";
            }
            _topTurnText.text = $"Turn: {combat.TurnNumber}";

            // 1. Top Initiative Ribbon (8 cards)
            IReadOnlyList<InitiativeEntry> ribbon = combat.GetInitiativeRibbon(8);
            for (int i = 0; i < _initiativeCards.Count; i++)
            {
                InitiativeCardRefs card = _initiativeCards[i];
                if (i >= ribbon.Count)
                {
                    card.Root.gameObject.SetActive(false);
                    continue;
                }

                card.Root.gameObject.SetActive(true);
                InitiativeEntry entry = ribbon[i];
                CombatantState unit = combat.GetCombatant(entry.CombatantId);

                Sprite sprite = null;
                if (unit != null && _catalog != null)
                {
                    if (unit.Side == CombatSide.Hero)
                    {
                        BywaterAssetCatalog.HeroArtEntry heroArt = _catalog.GetHeroArt(unit.Id, unit.PortraitId, string.Empty);
                        sprite = heroArt != null ? (heroArt.portrait != null ? heroArt.portrait : heroArt.sprite) : null;
                    }
                    else
                    {
                        BywaterAssetCatalog.MonsterArtEntry monArt = _catalog.GetMonsterArt(unit.Id, unit.IconId);
                        sprite = monArt != null ? monArt.sprite : null;
                    }
                }

                card.Portrait.sprite = sprite;
                card.Portrait.color = sprite != null ? Color.white : new Color(0.25f, 0.25f, 0.35f, 1f);
                card.NameText.text = entry.Name;
                card.MetaText.text = $"R{entry.RoundNumber} | DEX {entry.EffectiveDexterity}";

                if (entry.IsCurrentTurn)
                {
                    card.Background.color = new Color(0.290f, 0.220f, 0.080f, 0.98f);
                    BywaterUiHelpers.SetBorderColor(card.Root, BywaterUiHelpers.BrassHighlight);
                }
                else
                {
                    card.Background.color = entry.Side == CombatSide.Hero
                        ? new Color(0.070f, 0.115f, 0.200f, 0.94f)
                        : new Color(0.180f, 0.075f, 0.095f, 0.94f);
                    BywaterUiHelpers.SetBorderColor(
                        card.Root,
                        entry.Side == CombatSide.Hero
                            ? new Color(0.24f, 0.56f, 0.80f, 0.90f)
                            : new Color(0.75f, 0.26f, 0.24f, 0.90f));
                }
            }

            // 2. Bottom Tactical Action Bar
            CombatantState actor = combat.CurrentActor;
            bool isHeroTurn = combat.Outcome == CombatOutcome.InProgress && actor != null && actor.Side == CombatSide.Hero;

            if (actor != null)
            {
                Sprite actorSprite = null;
                if (_catalog != null)
                {
                    if (actor.Side == CombatSide.Hero)
                    {
                        BywaterAssetCatalog.HeroArtEntry heroArt = _catalog.GetHeroArt(actor.Id, actor.PortraitId, string.Empty);
                        actorSprite = heroArt != null ? (heroArt.portrait != null ? heroArt.portrait : heroArt.sprite) : null;
                    }
                    else
                    {
                        BywaterAssetCatalog.MonsterArtEntry monArt = _catalog.GetMonsterArt(actor.Id, actor.IconId);
                        actorSprite = monArt != null ? monArt.sprite : null;
                    }
                }

                _combatActorPortrait.sprite = actorSprite;
                _combatActorName.text = isHeroTurn ? actor.Name : $"{actor.Name} (AI)";
                _combatActorVitals.text = $"HP {actor.Hp}/{actor.MaxHp}  |  SP {actor.Sp}/{actor.MaxSp}  |  AC {actor.EffectiveAc}";

                _combatMovePip.text = $"Move: {actor.Economy.MoveRemaining}/{actor.Economy.MoveBudget}";
                _combatMovePip.color = actor.Economy.MoveRemaining > 0 ? BywaterUiHelpers.InfoCyan : BywaterUiHelpers.MutedSlate;

                _combatStdPip.text = actor.Economy.HasStandardAction ? "Std: READY" : "Std: USED";
                _combatStdPip.color = actor.Economy.HasStandardAction ? BywaterUiHelpers.SuccessGreen : BywaterUiHelpers.MutedSlate;

                _combatSwiftPip.text = actor.Economy.HasSwiftAction ? "Swift: READY" : "Swift: USED";
                _combatSwiftPip.color = actor.Economy.HasSwiftAction ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.MutedSlate;
            }

            _combatMoveBtn.interactable = isHeroTurn && actor.Economy.MoveRemaining > 0;
            _combatUndoBtn.interactable = isHeroTurn && combat.CanUndoMove;
            _combatMeleeBtn.interactable = isHeroTurn && actor.Economy.HasStandardAction;
            _combatRangedBtn.interactable = isHeroTurn && actor.Economy.HasStandardAction && actor.RangedRange > 0 && !string.IsNullOrWhiteSpace(actor.RangedDice);
            _combatSpellBtn.interactable = isHeroTurn && actor.Economy.HasStandardAction && actor.SpellIds.Count > 0;
            _combatPotionBtn.interactable = isHeroTurn && actor.Economy.HasSwiftAction;
            _combatGuardBtn.interactable = isHeroTurn && actor.Economy.HasStandardAction;
            _combatEndTurnBtn.interactable = isHeroTurn;

            BywaterUiHelpers.SetButtonColors(_combatMoveBtn, isHeroTurn && activeMode == CombatActionMode.Move);
            BywaterUiHelpers.SetButtonColors(_combatMeleeBtn, isHeroTurn && activeMode == CombatActionMode.Melee);
            BywaterUiHelpers.SetButtonColors(_combatRangedBtn, isHeroTurn && activeMode == CombatActionMode.Ranged);
            BywaterUiHelpers.SetButtonColors(_combatSpellBtn, isHeroTurn && activeMode == CombatActionMode.Spell);
            BywaterUiHelpers.SetButtonColors(_combatPotionBtn, isHeroTurn && activeMode == CombatActionMode.Potion);

            _combatSpeedLabel.text = $"Speed: {speedLabel}";

            // 3. Compact Spell Picker Bar
            bool showSpells = isHeroTurn && actor.SpellIds.Count > 0 && (activeMode == CombatActionMode.Spell);
            _spellPickerBar.SetActive(showSpells);
            if (showSpells && bundle != null)
            {
                ClearChildren(_spellPickerContent);
                for (int i = 0; i < actor.SpellIds.Count; i++)
                {
                    int spellId = actor.SpellIds[i];
                    if (!bundle.Spells.TryGetValue(spellId, out SpellDefinition spell))
                    {
                        continue;
                    }

                    bool isAoe = spell.AoeOffsets.Count > 1 ||
                                 string.Equals(spell.TargetMode, "AreaOfEffect", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(spell.TargetMode, "AllEnemies", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(spell.TargetMode, "AllAllies", StringComparison.OrdinalIgnoreCase);
                    string scopeTag = isAoe ? "AoE" : "Single";
                    bool enoughSp = actor.Sp >= spell.SpCost;
                    string btnText = $"{spell.Name} ({spell.SpCost}sp, {scopeTag})";

                    Button spBtn = BywaterUiHelpers.CreateButton(
                        _spellPickerContent,
                        $"SpellBtn_{spell.Id}",
                        btnText,
                        _font,
                        () => OnSpellSelected?.Invoke(spell.Id),
                        13f);
                    spBtn.interactable = enoughSp;
                    BywaterUiHelpers.SetButtonColors(spBtn, spell.Id == selectedSpellId);

                    LayoutElement le = spBtn.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = Mathf.Max(148f, btnText.Length * 8.2f + 22f);
                    le.preferredHeight = 36f;
                }
            }

            // 4. Right-Lower Combat Log
            if (combat.Log.Count != _lastLogCount)
            {
                _lastLogCount = combat.Log.Count;
                RefreshCombatLog(combat);
            }
        }

        public void SetCombatTooltip(string title, string body)
        {
            if (!_uiBuilt || _tooltipTitle == null || _tooltipBody == null)
            {
                return;
            }

            _tooltipTitle.text = string.IsNullOrEmpty(title) ? "Tactical Preview" : title;
            _tooltipBody.text = string.IsNullOrEmpty(body)
                ? "Hover a tile or target to inspect Hit%, Damage, Crit%, Graze%, LOS, Flanking, AoO, or Spell AoE."
                : body;
        }

        public void ShowOutcomeModal(CombatOutcome outcome, int xpEarned, int goldEarned)
        {
            if (!_uiBuilt || _outcomeModalRoot == null)
            {
                return;
            }

            bool isVictory = outcome == CombatOutcome.Victory;
            _outcomeTitle.text = isVictory ? "VICTORY!" : "DEFEAT";
            _outcomeTitle.color = isVictory ? BywaterUiHelpers.BrassHighlight : BywaterUiHelpers.DangerRed;
            _outcomeSummary.text = isVictory
                ? $"Enemies defeated!\n\nXP Earned: +{xpEarned}\nGold Looted: +{goldEarned}g\n\nFallen allies recover at 1 HP."
                : "All party members have fallen in battle.\n\nRetry the skirmish or revive the vanguard at the Bywater Shrine.";

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

        private void SetCombatHudActive(bool active)
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
            }
            else
            {
                _lastLogCount = -1;
            }
        }

        private void RefreshCombatLog(TacticalCombatSession combat)
        {
            ClearChildren(_combatLogScroll.content);
            int start = Mathf.Max(0, combat.Log.Count - 40);
            for (int i = start; i < combat.Log.Count; i++)
            {
                CombatLogEntry entry = combat.Log[i];
                CombatantState actor = combat.GetCombatant(entry.ActorId);

                Color entryColor = BywaterUiHelpers.ParchmentText;
                if (string.Equals(entry.ActionType, "AoO", StringComparison.OrdinalIgnoreCase))
                {
                    entryColor = new Color(1.0f, 0.72f, 0.24f, 1f);
                }
                else if (entry.HealingDone > 0)
                {
                    entryColor = BywaterUiHelpers.SuccessGreen;
                }
                else if (actor != null && actor.Side == CombatSide.Monster)
                {
                    entryColor = new Color(0.96f, 0.56f, 0.52f, 1f);
                }
                else if (string.Equals(entry.ActionType, "CastSpell", StringComparison.OrdinalIgnoreCase))
                {
                    entryColor = BywaterUiHelpers.InfoCyan;
                }
                else if (string.Equals(entry.ActionType, "Move", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.ActionType, "UndoMove", StringComparison.OrdinalIgnoreCase))
                {
                    entryColor = BywaterUiHelpers.MutedSlate;
                }

                TextMeshProUGUI lbl = BywaterUiHelpers.CreateLabel(
                    _combatLogScroll.content,
                    $"Log_{i}",
                    $"[R{entry.RoundNumber}] {entry.Message}",
                    _font,
                    13f,
                    entryColor,
                    TextAlignmentOptions.TopLeft,
                    wrap: true);
                ContentSizeFitter csf = lbl.gameObject.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            Canvas.ForceUpdateCanvases();
            _combatLogScroll.verticalNormalizedPosition = 0f;
        }

        private void BuildTopHudBar(Transform parent)
        {
            RectTransform bar = BywaterUiHelpers.CreatePanel(parent, "TopHudBar", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.5f);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, 52f);

            HorizontalLayoutGroup hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(16, 16, 6, 6);
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _topMapText = BywaterUiHelpers.CreateLabel(bar, "MapNameText", "City of Bywater (2, 1)", _font, 16f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            LayoutElement mapLe = _topMapText.gameObject.AddComponent<LayoutElement>();
            mapLe.preferredWidth = 310f;

            _topGoldText = BywaterUiHelpers.CreateLabel(bar, "GoldText", "Gold: 400g", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            LayoutElement goldLe = _topGoldText.gameObject.AddComponent<LayoutElement>();
            goldLe.preferredWidth = 135f;

            _topTorchText = BywaterUiHelpers.CreateLabel(bar, "TorchText", "Torch: 0t", _font, 15f, new Color(1.0f, 0.72f, 0.32f, 1f), TextAlignmentOptions.MidlineLeft, wrap: false);
            LayoutElement torchLe = _topTorchText.gameObject.AddComponent<LayoutElement>();
            torchLe.preferredWidth = 120f;

            _topTurnText = BywaterUiHelpers.CreateLabel(bar, "TurnText", "Turn: 0", _font, 15f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.MidlineLeft, wrap: false);
            LayoutElement turnLe = _topTurnText.gameObject.AddComponent<LayoutElement>();
            turnLe.flexibleWidth = 1f;

            _skirmishBtn = AddTopButton(bar, "SkirmishBtn", "Skirmish", 104f, () => OnSkirmishRequested?.Invoke());
            _partyBtn = AddTopButton(bar, "PartyBtn", "Party (P)", 104f, () => OnTogglePartyRequested?.Invoke());
            _shopBtn = AddTopButton(bar, "ShopBtn", "Shop (B)", 100f, () => OnToggleShopRequested?.Invoke());
            _campBtn = AddTopButton(bar, "CampBtn", "Camp", 86f, () => OnCampButtonRequested?.Invoke());
            _shrineBtn = AddTopButton(bar, "ShrineBtn", "Shrine", 90f, () => OnShrineRequested?.Invoke());
            _warpBtn = AddTopButton(bar, "WarpBtn", "Warp Portal", 122f, () => OnWarpPortalRequested?.Invoke());
            _saveBtn = AddTopButton(bar, "SaveBtn", "Save (F5)", 104f, () => OnSaveRequested?.Invoke());
            _loadBtn = AddTopButton(bar, "LoadBtn", "Load (F9)", 104f, () => OnLoadRequested?.Invoke());
        }

        private Button AddTopButton(Transform parent, string name, string label, float width, UnityEngine.Events.UnityAction onClick)
        {
            Button btn = BywaterUiHelpers.CreateButton(parent, name, label, _font, onClick, 13.5f);
            LayoutElement le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            return btn;
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
            hoverChip.anchoredPosition = new Vector2(16f, 14f);
            hoverChip.sizeDelta = new Vector2(410f, 84f);

            VerticalLayoutGroup hoverVlg = hoverChip.gameObject.AddComponent<VerticalLayoutGroup>();
            hoverVlg.padding = new RectOffset(12, 12, 8, 8);
            hoverVlg.spacing = 4f;
            hoverVlg.childControlWidth = true;
            hoverVlg.childControlHeight = true;

            _hoverCoordText = BywaterUiHelpers.CreateLabel(hoverChip, "HoverCoord", "Tile (2, 1) - Cobblestone", _font, 14.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _hoverDetailText = BywaterUiHelpers.CreateLabel(hoverChip, "HoverDetail", "Click to move", _font, 13f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.MidlineLeft, wrap: true);

            // Explore Bottom Party Strip (6 Hero Cards)
            RectTransform strip = BywaterUiHelpers.CreatePanel(_exploreRoot.transform, "ExplorePartyStrip", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1.5f);
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.offsetMin = new Vector2(440f, 12f);
            strip.offsetMax = new Vector2(-16f, 108f);

            HorizontalLayoutGroup stripHlg = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            stripHlg.padding = new RectOffset(8, 8, 6, 6);
            stripHlg.spacing = 8f;
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
                cardHlg.padding = new RectOffset(6, 6, 5, 5);
                cardHlg.spacing = 8f;
                cardHlg.childControlWidth = true;
                cardHlg.childControlHeight = true;
                cardHlg.childForceExpandWidth = false;
                cardHlg.childForceExpandHeight = false;
                cardHlg.childAlignment = TextAnchor.MiddleLeft;

                Image portrait = BywaterUiHelpers.CreateImage(cardRt, "Portrait");
                LayoutElement portLe = portrait.gameObject.AddComponent<LayoutElement>();
                portLe.preferredWidth = 64f;
                portLe.preferredHeight = 64f;

                GameObject infoCol = new GameObject("Info", typeof(RectTransform), typeof(VerticalLayoutGroup));
                infoCol.transform.SetParent(cardRt, false);
                LayoutElement infoLe = infoCol.AddComponent<LayoutElement>();
                infoLe.flexibleWidth = 1f;
                infoLe.preferredHeight = 72f;

                VerticalLayoutGroup infoVlg = infoCol.GetComponent<VerticalLayoutGroup>();
                infoVlg.spacing = 2f;
                infoVlg.childControlWidth = true;
                infoVlg.childControlHeight = true;

                TextMeshProUGUI nameTxt = BywaterUiHelpers.CreateLabel(infoCol.transform, "Name", $"Hero {i + 1}", _font, 14f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                TextMeshProUGUI classTxt = BywaterUiHelpers.CreateLabel(infoCol.transform, "Class", "Fighter", _font, 12.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, wrap: false);

                Image hpFill = BywaterUiHelpers.CreateProgressBar(infoCol.transform, "HpBar", BywaterUiHelpers.HpFillColor, out RectTransform hpBarRt);
                LayoutElement hpBarLe = hpBarRt.gameObject.AddComponent<LayoutElement>();
                hpBarLe.preferredHeight = 14f;
                TextMeshProUGUI hpTxt = BywaterUiHelpers.CreateLabel(hpBarRt, "HpText", "HP 10/10", _font, 11.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
                hpTxt.rectTransform.anchorMin = Vector2.zero;
                hpTxt.rectTransform.anchorMax = Vector2.one;
                hpTxt.rectTransform.offsetMin = Vector2.zero;
                hpTxt.rectTransform.offsetMax = Vector2.zero;

                Image spFill = BywaterUiHelpers.CreateProgressBar(infoCol.transform, "SpBar", BywaterUiHelpers.SpFillColor, out RectTransform spBarRt);
                LayoutElement spBarLe = spBarRt.gameObject.AddComponent<LayoutElement>();
                spBarLe.preferredHeight = 13f;
                TextMeshProUGUI spTxt = BywaterUiHelpers.CreateLabel(spBarRt, "SpText", "SP 0/0", _font, 11f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
                spTxt.rectTransform.anchorMin = Vector2.zero;
                spTxt.rectTransform.anchorMax = Vector2.one;
                spTxt.rectTransform.offsetMin = Vector2.zero;
                spTxt.rectTransform.offsetMax = Vector2.zero;

                _heroCards.Add(new HeroCardRefs
                {
                    Root = cardRt,
                    Background = cardRt.GetComponent<Image>(),
                    Portrait = portrait,
                    NameText = nameTxt,
                    ClassText = classTxt,
                    HpFill = hpFill,
                    HpText = hpTxt,
                    SpFill = spFill,
                    SpText = spTxt
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
            modal.sizeDelta = new Vector2(1320f, 780f);
            _partyDrawerRoot = modal.gameObject;

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
            headerLe.preferredHeight = 38f;
            HorizontalLayoutGroup headerHlg = header.GetComponent<HorizontalLayoutGroup>();
            headerHlg.spacing = 12f;
            headerHlg.childControlWidth = true;
            headerHlg.childControlHeight = true;
            headerHlg.childForceExpandWidth = false;

            TextMeshProUGUI title = BywaterUiHelpers.CreateLabel(header.transform, "Title", "Vanguard Party, Equipment, Stash & Camp", _font, 18f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _drawerGoldTorchLabel = BywaterUiHelpers.CreateLabel(header.transform, "GoldTorch", "Gold: 400g | Torch: 0 turns", _font, 14.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineRight, wrap: false);
            _drawerGoldTorchLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 280f;

            Button closeBtn = BywaterUiHelpers.CreateButton(header.transform, "CloseBtn", "Close", _font, () => TogglePartyPanel(false), 14f);
            closeBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;

            // Hero Selector Strip
            GameObject selectorRow = new GameObject("HeroSelectorRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            selectorRow.transform.SetParent(modal, false);
            selectorRow.AddComponent<LayoutElement>().preferredHeight = 38f;
            HorizontalLayoutGroup selHlg = selectorRow.GetComponent<HorizontalLayoutGroup>();
            selHlg.spacing = 8f;
            selHlg.childControlWidth = true;
            selHlg.childControlHeight = true;
            selHlg.childForceExpandWidth = true;

            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                Button btn = BywaterUiHelpers.CreateButton(selectorRow.transform, $"SelectHero_{i}", $"Hero {i + 1}", _font, () => OnPartyHeroSelected?.Invoke(idx), 14f);
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
            leftCol.gameObject.AddComponent<LayoutElement>().preferredWidth = 580f;
            VerticalLayoutGroup leftVlg = leftCol.gameObject.AddComponent<VerticalLayoutGroup>();
            leftVlg.padding = new RectOffset(12, 12, 12, 12);
            leftVlg.spacing = 8f;
            leftVlg.childControlWidth = true;
            leftVlg.childControlHeight = true;
            leftVlg.childForceExpandHeight = false;

            GameObject heroTop = new GameObject("HeroTop", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            heroTop.transform.SetParent(leftCol, false);
            heroTop.AddComponent<LayoutElement>().preferredHeight = 82f;
            HorizontalLayoutGroup htHlg = heroTop.GetComponent<HorizontalLayoutGroup>();
            htHlg.spacing = 12f;
            htHlg.childControlWidth = true;
            htHlg.childControlHeight = true;
            htHlg.childForceExpandWidth = false;

            _drawerHeroPortrait = BywaterUiHelpers.CreateImage(heroTop.transform, "Portrait");
            LayoutElement hpLe = _drawerHeroPortrait.gameObject.AddComponent<LayoutElement>();
            hpLe.preferredWidth = 80f;
            hpLe.preferredHeight = 80f;

            _drawerHeroTitle = BywaterUiHelpers.CreateLabel(heroTop.transform, "HeroTitle", "Aldric", _font, 16f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _drawerHeroTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _drawerHeroStats = BywaterUiHelpers.CreateLabel(leftCol, "HeroStats", string.Empty, _font, 14f, BywaterUiHelpers.ParchmentText);
            _drawerHeroStats.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;

            _drawerHeroConditions = BywaterUiHelpers.CreateLabel(leftCol, "HeroConditions", "Conditions: Healthy", _font, 13.5f, BywaterUiHelpers.InfoCyan);
            _drawerHeroConditions.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            _drawerHeroSpells = BywaterUiHelpers.CreateLabel(leftCol, "HeroSpells", "Known Spells: None", _font, 13.5f, BywaterUiHelpers.MutedSlate);
            _drawerHeroSpells.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;

            BywaterUiHelpers.CreateLabel(leftCol, "EquipHeader", "Equipped Gear (5 Slots)", _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold).gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            EquipmentSlot[] slots =
            {
                EquipmentSlot.Weapon,
                EquipmentSlot.MissileWeapon,
                EquipmentSlot.Armor,
                EquipmentSlot.Shield,
                EquipmentSlot.Helm
            };

            for (int i = 0; i < slots.Length; i++)
            {
                EquipmentSlot slot = slots[i];
                RectTransform slotRow = BywaterUiHelpers.CreatePanel(leftCol, $"Slot_{slot}", BywaterUiHelpers.CardBg, withBrassOutline: false);
                slotRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
                HorizontalLayoutGroup sHlg = slotRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                sHlg.padding = new RectOffset(8, 8, 4, 4);
                sHlg.spacing = 8f;
                sHlg.childControlWidth = true;
                sHlg.childControlHeight = true;
                sHlg.childForceExpandWidth = false;

                TextMeshProUGUI slotTag = BywaterUiHelpers.CreateLabel(slotRow, "SlotName", slot.ToString(), _font, 13.5f, BywaterUiHelpers.BrassAccent, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                slotTag.gameObject.AddComponent<LayoutElement>().preferredWidth = 115f;

                TextMeshProUGUI itemLbl = BywaterUiHelpers.CreateLabel(slotRow, "ItemName", "- Empty -", _font, 13.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, wrap: false);
                itemLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                Button unequipBtn = BywaterUiHelpers.CreateButton(slotRow, "UnequipBtn", "Unequip", _font, () => OnUnequipRequested?.Invoke(_selectedHeroIndex, slot), 13f);
                unequipBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 86f;

                _equipSlots.Add(new EquipSlotRefs
                {
                    Slot = slot,
                    ItemLabel = itemLbl,
                    UnequipBtn = unequipBtn
                });
            }

            // Right Column: Camp & Shared Stash
            RectTransform rightCol = BywaterUiHelpers.CreatePanel(body.transform, "StashCampCol", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            rightCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            VerticalLayoutGroup rightVlg = rightCol.gameObject.AddComponent<VerticalLayoutGroup>();
            rightVlg.padding = new RectOffset(12, 12, 12, 12);
            rightVlg.spacing = 8f;
            rightVlg.childControlWidth = true;
            rightVlg.childControlHeight = true;
            rightVlg.childForceExpandHeight = false;

            GameObject campBar = new GameObject("CampBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            campBar.transform.SetParent(rightCol, false);
            campBar.AddComponent<LayoutElement>().preferredHeight = 42f;
            HorizontalLayoutGroup campHlg = campBar.GetComponent<HorizontalLayoutGroup>();
            campHlg.spacing = 10f;
            campHlg.childControlWidth = true;
            campHlg.childControlHeight = true;
            campHlg.childForceExpandWidth = false;

            TextMeshProUGUI stashTitle = BywaterUiHelpers.CreateLabel(campBar.transform, "StashTitle", "Shared Stash & Camp", _font, 15.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            stashTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Button bandageBtn = BywaterUiHelpers.CreateButton(campBar.transform, "QuickBandageBtn", "Quick Bandage", _font, () => OnCampModeRequested?.Invoke(CampMode.QuickBandage), 13.5f);
            bandageBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 148f;

            Button fullRestBtn = BywaterUiHelpers.CreateButton(campBar.transform, "FullRestBtn", "Full Rest", _font, () => OnCampModeRequested?.Invoke(CampMode.FullRest), 13.5f);
            fullRestBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;

            _partyStashScroll = BywaterUiHelpers.CreateScrollView(rightCol, "StashScroll", _font, spacing: 5f);
            _partyStashScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
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

            // Header
            GameObject header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            header.transform.SetParent(modal, false);
            header.AddComponent<LayoutElement>().preferredHeight = 40f;
            HorizontalLayoutGroup hHlg = header.GetComponent<HorizontalLayoutGroup>();
            hHlg.spacing = 10f;
            hHlg.childControlWidth = true;
            hHlg.childControlHeight = true;
            hHlg.childForceExpandWidth = false;

            _shopHeaderTitle = BywaterUiHelpers.CreateLabel(header.transform, "ShopTitle", "Bywater Armory", _font, 18f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _shopHeaderTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _shopGoldLabel = BywaterUiHelpers.CreateLabel(header.transform, "ShopGold", "Party Gold: 400g", _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineRight, FontStyles.Bold, wrap: false);
            _shopGoldLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;

            Button templeBtn = BywaterUiHelpers.CreateButton(header.transform, "TempleHealBtn", "Temple Heal (25g)", _font, () => OnTempleHealRequested?.Invoke(), 13.5f);
            templeBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 165f;

            Button closeBtn = BywaterUiHelpers.CreateButton(header.transform, "CloseShopBtn", "Close", _font, CloseShopModal, 14f);
            closeBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;

            // Shop Tabs
            GameObject tabsRow = new GameObject("ShopTabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tabsRow.transform.SetParent(modal, false);
            tabsRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            HorizontalLayoutGroup tabsHlg = tabsRow.GetComponent<HorizontalLayoutGroup>();
            tabsHlg.spacing = 8f;
            tabsHlg.childControlWidth = true;
            tabsHlg.childControlHeight = true;
            tabsHlg.childForceExpandWidth = false;

            _shopTabArmory = BywaterUiHelpers.CreateButton(tabsRow.transform, "TabArmory", "Armory #1", _font, () => SelectShop("1"), 14f);
            _shopTabArmory.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
            _shopTabTannery = BywaterUiHelpers.CreateButton(tabsRow.transform, "TabTannery", "Tannery #4", _font, () => SelectShop("4"), 14f);
            _shopTabTannery.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
            _shopTabApothecary = BywaterUiHelpers.CreateButton(tabsRow.transform, "TabApothecary", "Apothecary #10", _font, () => SelectShop("10"), 14f);
            _shopTabApothecary.gameObject.AddComponent<LayoutElement>().preferredWidth = 170f;

            // Filter Bar
            GameObject filterRow = new GameObject("FilterRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            filterRow.transform.SetParent(modal, false);
            filterRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            HorizontalLayoutGroup fHlg = filterRow.GetComponent<HorizontalLayoutGroup>();
            fHlg.spacing = 6f;
            fHlg.childControlWidth = true;
            fHlg.childControlHeight = true;
            fHlg.childForceExpandWidth = false;

            string[] categories = { "All", "Weapon", "Armor", "Helm", "Shield", "Potion", "Supply" };
            for (int i = 0; i < categories.Length; i++)
            {
                string cat = categories[i];
                Button catBtn = BywaterUiHelpers.CreateButton(filterRow.transform, $"Cat_{cat}", cat, _font, () =>
                {
                    _activeShopCategory = cat;
                    RefreshShopModal(_cachedParty, _cachedBundle);
                }, 13f);
                catBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 84f;
                _shopCategoryBtns[cat] = catBtn;
            }

            _shopAffordableToggle = CreateAffordableToggle(filterRow.transform);
            _shopAffordableToggle.gameObject.AddComponent<LayoutElement>().preferredWidth = 148f;
            _shopAffordableToggle.onValueChanged.AddListener(_ => RefreshShopModal(_cachedParty, _cachedBundle));

            _shopSearchInput = BywaterUiHelpers.CreateInputField(filterRow.transform, "ShopSearchInput", "Search items...", _font, 14f);
            _shopSearchInput.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _shopSearchInput.onValueChanged.AddListener(_ => RefreshShopModal(_cachedParty, _cachedBundle));

            // Body columns (Buy Stock vs Sell Stash)
            GameObject body = new GameObject("ShopBody", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            body.transform.SetParent(modal, false);
            body.AddComponent<LayoutElement>().flexibleHeight = 1f;
            HorizontalLayoutGroup bHlg = body.GetComponent<HorizontalLayoutGroup>();
            bHlg.spacing = 14f;
            bHlg.childControlWidth = true;
            bHlg.childControlHeight = true;
            bHlg.childForceExpandWidth = false;

            // Buy Column
            RectTransform buyCol = BywaterUiHelpers.CreatePanel(body.transform, "BuyCol", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1f);
            buyCol.gameObject.AddComponent<LayoutElement>().preferredWidth = 680f;
            VerticalLayoutGroup buyVlg = buyCol.gameObject.AddComponent<VerticalLayoutGroup>();
            buyVlg.padding = new RectOffset(10, 10, 10, 10);
            buyVlg.spacing = 8f;
            buyVlg.childControlWidth = true;
            buyVlg.childControlHeight = true;
            buyVlg.childForceExpandHeight = false;

            BywaterUiHelpers.CreateLabel(buyCol, "BuyTitle", "Shop Stock", _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold).gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            _shopBuyScroll = BywaterUiHelpers.CreateScrollView(buyCol, "ShopBuyScroll", _font, spacing: 5f);
            _shopBuyScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

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
            sellHeader.AddComponent<LayoutElement>().preferredHeight = 32f;
            HorizontalLayoutGroup shHlg = sellHeader.GetComponent<HorizontalLayoutGroup>();
            shHlg.spacing = 8f;
            shHlg.childControlWidth = true;
            shHlg.childControlHeight = true;
            shHlg.childForceExpandWidth = false;

            TextMeshProUGUI sellTitle = BywaterUiHelpers.CreateLabel(sellHeader.transform, "SellTitle", "Party Stash (Sell)", _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            sellTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Button junkBtn = BywaterUiHelpers.CreateButton(sellHeader.transform, "SellJunkBtn", "Sell All Junk", _font, () => OnSellAllJunkRequested?.Invoke(), 13f);
            junkBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 118f;

            Button idBtn = BywaterUiHelpers.CreateButton(sellHeader.transform, "IdentifyAllBtn", "Identify All", _font, () => OnIdentifyAllRequested?.Invoke(), 13f);
            idBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 112f;

            _shopSellScroll = BywaterUiHelpers.CreateScrollView(sellCol, "ShopSellScroll", _font, spacing: 5f);
            _shopSellScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        }

        private Toggle CreateAffordableToggle(Transform parent)
        {
            RectTransform root = BywaterUiHelpers.CreatePanel(parent, "AffordableToggle", BywaterUiHelpers.ButtonBg, withBrassOutline: true, outlinePx: 1.2f);
            Toggle toggle = root.gameObject.AddComponent<Toggle>();
            toggle.isOn = false;

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

            TextMeshProUGUI lbl = BywaterUiHelpers.CreateLabel(root, "Label", "Affordable", _font, 13.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            lbl.rectTransform.anchorMin = Vector2.zero;
            lbl.rectTransform.anchorMax = Vector2.one;
            lbl.rectTransform.offsetMin = new Vector2(36f, 0f);
            lbl.rectTransform.offsetMax = new Vector2(-6f, 0f);

            toggle.targetGraphic = root.GetComponent<Image>();
            toggle.graphic = checkMark;
            return toggle;
        }

        private void SelectShop(string shopId)
        {
            _activeShopId = shopId;
            OnShopSelected?.Invoke(shopId);
            RefreshShopModal(_cachedParty, _cachedBundle);
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

            // 1. Top Initiative Ribbon (8 cards)
            RectTransform ribbonPanel = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "InitiativeRibbon", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1.5f);
            ribbonPanel.anchorMin = new Vector2(0.5f, 1f);
            ribbonPanel.anchorMax = new Vector2(0.5f, 1f);
            ribbonPanel.pivot = new Vector2(0.5f, 1f);
            ribbonPanel.anchoredPosition = new Vector2(-175f, -58f);
            ribbonPanel.sizeDelta = new Vector2(1380f, 76f);

            HorizontalLayoutGroup ribHlg = ribbonPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            ribHlg.padding = new RectOffset(8, 8, 6, 6);
            ribHlg.spacing = 6f;
            ribHlg.childControlWidth = true;
            ribHlg.childControlHeight = true;
            ribHlg.childForceExpandWidth = true;
            ribHlg.childForceExpandHeight = true;

            for (int i = 0; i < 8; i++)
            {
                RectTransform cardRt = BywaterUiHelpers.CreatePanel(ribbonPanel, $"InitCard_{i}", BywaterUiHelpers.CardBg, withBrassOutline: true, outlinePx: 1.4f);
                HorizontalLayoutGroup ch = cardRt.gameObject.AddComponent<HorizontalLayoutGroup>();
                ch.padding = new RectOffset(6, 6, 4, 4);
                ch.spacing = 6f;
                ch.childControlWidth = true;
                ch.childControlHeight = true;
                ch.childForceExpandWidth = false;
                ch.childForceExpandHeight = false;
                ch.childAlignment = TextAnchor.MiddleLeft;

                Image portrait = BywaterUiHelpers.CreateImage(cardRt, "Portrait");
                LayoutElement pLe = portrait.gameObject.AddComponent<LayoutElement>();
                pLe.preferredWidth = 46f;
                pLe.preferredHeight = 46f;

                GameObject txtCol = new GameObject("TextCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
                txtCol.transform.SetParent(cardRt, false);
                txtCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
                VerticalLayoutGroup tv = txtCol.GetComponent<VerticalLayoutGroup>();
                tv.spacing = 2f;
                tv.childControlWidth = true;
                tv.childControlHeight = true;

                TextMeshProUGUI nameLbl = BywaterUiHelpers.CreateLabel(txtCol.transform, "Name", $"Unit {i + 1}", _font, 13f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
                TextMeshProUGUI metaLbl = BywaterUiHelpers.CreateLabel(txtCol.transform, "Meta", "R1 | DEX 10", _font, 11.5f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.MidlineLeft, wrap: false);

                _initiativeCards.Add(new InitiativeCardRefs
                {
                    Root = cardRt,
                    Background = cardRt.GetComponent<Image>(),
                    Portrait = portrait,
                    NameText = nameLbl,
                    MetaText = metaLbl
                });
            }

            // 2. Right-Side Combat Tooltip Card
            RectTransform tooltipPanel = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "CombatTooltipCard", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1.5f);
            tooltipPanel.anchorMin = new Vector2(1f, 1f);
            tooltipPanel.anchorMax = new Vector2(1f, 1f);
            tooltipPanel.pivot = new Vector2(1f, 1f);
            tooltipPanel.anchoredPosition = new Vector2(-14f, -60f);
            tooltipPanel.sizeDelta = new Vector2(335f, 290f);

            VerticalLayoutGroup ttVlg = tooltipPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            ttVlg.padding = new RectOffset(12, 12, 10, 10);
            ttVlg.spacing = 6f;
            ttVlg.childControlWidth = true;
            ttVlg.childControlHeight = true;
            ttVlg.childForceExpandHeight = false;

            _tooltipTitle = BywaterUiHelpers.CreateLabel(tooltipPanel, "TooltipTitle", "Tactical Preview", _font, 15.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _tooltipTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            _tooltipBody = BywaterUiHelpers.CreateLabel(tooltipPanel, "TooltipBody", "Hover a tile or target to inspect tactical odds.", _font, 13.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.TopLeft, wrap: true);
            _tooltipBody.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            // 3. Right-Lower Scrollable Combat Log
            RectTransform logPanel = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "CombatLogPanel", BywaterUiHelpers.PanelBg, withBrassOutline: true, outlinePx: 1.5f);
            logPanel.anchorMin = new Vector2(1f, 0f);
            logPanel.anchorMax = new Vector2(1f, 1f);
            logPanel.pivot = new Vector2(1f, 0.5f);
            logPanel.offsetMin = new Vector2(-349f, 118f);
            logPanel.offsetMax = new Vector2(-14f, -360f);

            VerticalLayoutGroup logVlg = logPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            logVlg.padding = new RectOffset(10, 10, 8, 8);
            logVlg.spacing = 6f;
            logVlg.childControlWidth = true;
            logVlg.childControlHeight = true;
            logVlg.childForceExpandHeight = false;

            BywaterUiHelpers.CreateLabel(logPanel, "LogHeader", "Combat Log", _font, 14.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false).gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            _combatLogScroll = BywaterUiHelpers.CreateScrollView(logPanel, "CombatLogScroll", _font, spacing: 4f);
            _combatLogScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            // 4. Compact Spell Picker Bar (above bottom action bar)
            RectTransform spellBar = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "SpellPickerBar", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.5f);
            spellBar.anchorMin = new Vector2(0.5f, 0f);
            spellBar.anchorMax = new Vector2(0.5f, 0f);
            spellBar.pivot = new Vector2(0.5f, 0f);
            spellBar.anchoredPosition = new Vector2(-175f, 116f);
            spellBar.sizeDelta = new Vector2(1260f, 48f);
            _spellPickerBar = spellBar.gameObject;

            HorizontalLayoutGroup spHlg = spellBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            spHlg.padding = new RectOffset(8, 8, 5, 5);
            spHlg.spacing = 8f;
            spHlg.childControlWidth = true;
            spHlg.childControlHeight = true;
            spHlg.childForceExpandWidth = false;
            spHlg.childAlignment = TextAnchor.MiddleLeft;
            _spellPickerContent = spellBar;
            _spellPickerBar.SetActive(false);

            // 5. Bottom Tactical Action Bar
            RectTransform actionBar = BywaterUiHelpers.CreatePanel(_combatRoot.transform, "TacticalActionBar", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.8f);
            actionBar.anchorMin = new Vector2(0.5f, 0f);
            actionBar.anchorMax = new Vector2(0.5f, 0f);
            actionBar.pivot = new Vector2(0.5f, 0f);
            actionBar.anchoredPosition = new Vector2(-175f, 14f);
            actionBar.sizeDelta = new Vector2(1420f, 94f);

            HorizontalLayoutGroup abHlg = actionBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            abHlg.padding = new RectOffset(10, 10, 8, 8);
            abHlg.spacing = 8f;
            abHlg.childControlWidth = true;
            abHlg.childControlHeight = true;
            abHlg.childForceExpandWidth = false;
            abHlg.childAlignment = TextAnchor.MiddleLeft;

            _combatActorPortrait = BywaterUiHelpers.CreateImage(actionBar, "ActorPortrait");
            LayoutElement apLe = _combatActorPortrait.gameObject.AddComponent<LayoutElement>();
            apLe.preferredWidth = 72f;
            apLe.preferredHeight = 72f;

            GameObject actorInfo = new GameObject("ActorInfo", typeof(RectTransform), typeof(VerticalLayoutGroup));
            actorInfo.transform.SetParent(actionBar, false);
            actorInfo.AddComponent<LayoutElement>().preferredWidth = 250f;
            VerticalLayoutGroup aiVlg = actorInfo.GetComponent<VerticalLayoutGroup>();
            aiVlg.spacing = 2f;
            aiVlg.childControlWidth = true;
            aiVlg.childControlHeight = true;

            _combatActorName = BywaterUiHelpers.CreateLabel(actorInfo.transform, "ActorName", "Aldric", _font, 16f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _combatActorVitals = BywaterUiHelpers.CreateLabel(actorInfo.transform, "ActorVitals", "HP 17/17 | SP 0/0 | AC 29", _font, 13.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.MidlineLeft, wrap: false);

            GameObject pipsRow = new GameObject("PipsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            pipsRow.transform.SetParent(actorInfo.transform, false);
            HorizontalLayoutGroup pHlg = pipsRow.GetComponent<HorizontalLayoutGroup>();
            pHlg.spacing = 8f;
            pHlg.childControlWidth = true;
            pHlg.childControlHeight = true;

            _combatMovePip = BywaterUiHelpers.CreateLabel(pipsRow.transform, "MovePip", "Move: 6", _font, 12.5f, BywaterUiHelpers.InfoCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _combatStdPip = BywaterUiHelpers.CreateLabel(pipsRow.transform, "StdPip", "Std: READY", _font, 12.5f, BywaterUiHelpers.SuccessGreen, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);
            _combatSwiftPip = BywaterUiHelpers.CreateLabel(pipsRow.transform, "SwiftPip", "Swift: READY", _font, 12.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, wrap: false);

            _combatMoveBtn = AddCombatButton(actionBar, "MoveBtn", "Move", 88f, () => OnCombatModeSelected?.Invoke(CombatActionMode.Move));
            _combatUndoBtn = AddCombatButton(actionBar, "UndoMoveBtn", "Undo Move", 108f, () => OnUndoMoveRequested?.Invoke());
            _combatMeleeBtn = AddCombatButton(actionBar, "MeleeBtn", "Melee", 88f, () => OnCombatModeSelected?.Invoke(CombatActionMode.Melee));
            _combatRangedBtn = AddCombatButton(actionBar, "RangedBtn", "Ranged", 94f, () => OnCombatModeSelected?.Invoke(CombatActionMode.Ranged));
            _combatSpellBtn = AddCombatButton(actionBar, "SpellBtn", "Spell", 88f, () => OnCombatModeSelected?.Invoke(CombatActionMode.Spell));
            _combatPotionBtn = AddCombatButton(actionBar, "PotionBtn", "Potion", 90f, () => OnCombatModeSelected?.Invoke(CombatActionMode.Potion));
            _combatGuardBtn = AddCombatButton(actionBar, "GuardBtn", "Guard", 88f, () => OnGuardRequested?.Invoke());
            _combatEndTurnBtn = AddCombatButton(actionBar, "EndTurnBtn", "End Turn", 104f, () => OnEndTurnRequested?.Invoke());

            _combatSpeedBtn = AddCombatButton(actionBar, "SpeedBtn", "Speed: 1x", 122f, () => OnCycleSpeedRequested?.Invoke());
            _combatSpeedLabel = _combatSpeedBtn.GetComponentInChildren<TextMeshProUGUI>();
        }

        private Button AddCombatButton(Transform parent, string name, string label, float width, UnityEngine.Events.UnityAction onClick)
        {
            Button btn = BywaterUiHelpers.CreateButton(parent, name, label, _font, onClick, 14f);
            LayoutElement le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 58f;
            return btn;
        }

        private void BuildOutcomeModal(Transform parent)
        {
            RectTransform modal = BywaterUiHelpers.CreatePanel(parent, "OutcomeModal", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 2.2f);
            modal.anchorMin = new Vector2(0.5f, 0.5f);
            modal.anchorMax = new Vector2(0.5f, 0.5f);
            modal.pivot = new Vector2(0.5f, 0.5f);
            modal.anchoredPosition = Vector2.zero;
            modal.sizeDelta = new Vector2(540f, 320f);
            _outcomeModalRoot = modal.gameObject;

            VerticalLayoutGroup vlg = modal.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(22, 22, 20, 20);
            vlg.spacing = 14f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            _outcomeTitle = BywaterUiHelpers.CreateLabel(modal, "OutcomeTitle", "VICTORY!", _font, 26f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold);
            _outcomeTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

            _outcomeSummary = BywaterUiHelpers.CreateLabel(modal, "OutcomeSummary", string.Empty, _font, 15f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, wrap: true);
            _outcomeSummary.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            GameObject btnRow = new GameObject("OutcomeButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            btnRow.transform.SetParent(modal, false);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 46f;
            HorizontalLayoutGroup bHlg = btnRow.GetComponent<HorizontalLayoutGroup>();
            bHlg.spacing = 14f;
            bHlg.childControlWidth = true;
            bHlg.childControlHeight = true;
            bHlg.childForceExpandWidth = true;

            _outcomeContinueBtn = BywaterUiHelpers.CreateButton(btnRow.transform, "ContinueBtn", "Continue Exploring", _font, () => OnContinueAfterVictoryRequested?.Invoke(), 15f);
            _outcomeRetryBtn = BywaterUiHelpers.CreateButton(btnRow.transform, "RetryBtn", "Retry Skirmish", _font, () => OnRetrySkirmishRequested?.Invoke(), 15f);
            _outcomeReviveBtn = BywaterUiHelpers.CreateButton(btnRow.transform, "ReviveBtn", "Revive at Shrine", _font, () => OnReviveAtShrineRequested?.Invoke(), 15f);
        }

        private void BuildToastBanner(Transform parent)
        {
            RectTransform toast = BywaterUiHelpers.CreatePanel(parent, "ToastBanner", BywaterUiHelpers.PanelDarkBg, withBrassOutline: true, outlinePx: 1.5f);
            toast.anchorMin = new Vector2(0.5f, 1f);
            toast.anchorMax = new Vector2(0.5f, 1f);
            toast.pivot = new Vector2(0.5f, 1f);
            toast.anchoredPosition = new Vector2(0f, -144f);
            toast.sizeDelta = new Vector2(640f, 44f);

            Image bg = toast.GetComponent<Image>();
            bg.raycastTarget = false;

            _toastCanvasGroup = toast.gameObject.AddComponent<CanvasGroup>();
            _toastCanvasGroup.blocksRaycasts = false;
            _toastCanvasGroup.interactable = false;

            _toastText = BywaterUiHelpers.CreateLabel(toast, "ToastText", string.Empty, _font, 15f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
            _toastText.rectTransform.anchorMin = Vector2.zero;
            _toastText.rectTransform.anchorMax = Vector2.one;
            _toastText.rectTransform.offsetMin = new Vector2(12f, 4f);
            _toastText.rectTransform.offsetMax = new Vector2(-12f, -4f);

            _toastRoot = toast.gameObject;
        }

        private static void ClearChildren(Transform container)
        {
            if (container == null)
            {
                return;
            }

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
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

            List<string> parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(item.DamageDice)) parts.Add("Dmg " + item.DamageDice);
            if (item.WeaponBonus != 0) parts.Add($"+{item.WeaponBonus} Hit");
            if (item.ArmorClassBonus != 0) parts.Add($"+{item.ArmorClassBonus} AC");
            if (item.Range > 1) parts.Add($"Rng {item.Range}");
            if (!string.IsNullOrWhiteSpace(item.HealDice)) parts.Add("Heal " + item.HealDice);
            return string.Join(", ", parts);
        }
    }
}
