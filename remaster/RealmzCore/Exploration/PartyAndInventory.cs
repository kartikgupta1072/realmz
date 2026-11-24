using System;
using System.Collections.Generic;
using RealmzCore.Data;
using RealmzCore.Rules;

namespace RealmzCore.Exploration
{
    public enum EquipmentSlot
    {
        Weapon = 0,
        MissileWeapon = 1,
        Armor = 2,
        Shield = 3,
        Helm = 4
    }

    public enum CampMode
    {
        QuickBandage = 0,
        FullRest = 1
    }

    public sealed class InventoryItem
    {
        public InventoryItem(int itemId, int quantity = 1, bool isIdentified = true, int remainingCharges = 0)
        {
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }

            ItemId = itemId;
            Quantity = quantity;
            IsIdentified = isIdentified;
            RemainingCharges = remainingCharges;
        }

        public int ItemId { get; }
        public int Quantity { get; set; }
        public bool IsIdentified { get; set; }
        public int RemainingCharges { get; set; }
    }

    public sealed class PartyMemberState
    {
        public const int MaxLevel = 30;

        private readonly Dictionary<EquipmentSlot, InventoryItem> _equipped =
            new Dictionary<EquipmentSlot, InventoryItem>();
        private readonly List<int> _knownSpellIds;
        private IReadOnlyDictionary<int, ItemDefinition>? _catalog;

        public PartyMemberState(HeroPresetData preset, IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            _catalog = catalog;
            Id = preset.Id;
            Name = preset.Name;
            Archetype = preset.Archetype;
            RaceId = preset.RaceId;
            RaceName = preset.RaceName;
            CasteId = preset.CasteId;
            CasteName = preset.CasteName;
            Level = Math.Max(1, preset.Level);
            Xp = Math.Max(GetXpRequiredForLevel(Level), preset.Xp);
            Hp = preset.Hp;
            MaxHp = Math.Max(1, preset.MaxHp);
            Sp = preset.Sp;
            MaxSp = Math.Max(0, preset.MaxSp);
            BaseAc = preset.Ac;
            BaseToHit = preset.ToHit;
            Dodge = preset.Dodge;
            BaseMissile = preset.Missile;
            BaseMovementMax = Math.Max(1, preset.MovementMax);
            AttacksPerRound = preset.AttacksPerRound;
            IconId = preset.IconId;
            PortraitId = preset.PortraitId;
            Brawn = preset.Brawn;
            Knowledge = preset.Knowledge;
            Judgment = preset.Judgment;
            Agility = preset.Agility;
            Vitality = preset.Vitality;
            Luck = preset.Luck;
            _knownSpellIds = new List<int>(preset.KnownSpellIds);
            Conditions = new ConditionTracker();

            if (catalog != null && preset.EquippedIds.Count > 0)
            {
                for (int i = 0; i < preset.EquippedIds.Count; i++)
                {
                    int itemId = preset.EquippedIds[i];
                    if (catalog.TryGetValue(itemId, out ItemDefinition? item) &&
                        TryParseSlot(item.Slot, out EquipmentSlot slot))
                    {
                        _equipped[slot] = new InventoryItem(itemId, 1, isIdentified: true, remainingCharges: item.Charges);
                    }
                }
            }
        }

        public string Id { get; }
        public string Name { get; }
        public string Archetype { get; }
        public int RaceId { get; }
        public string RaceName { get; }
        public int CasteId { get; }
        public string CasteName { get; }
        public int Level { get; set; }
        public int Xp { get; set; }
        public int XpForNextLevel => Level >= MaxLevel ? GetXpRequiredForLevel(MaxLevel) : GetXpRequiredForLevel(Level + 1);
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Sp { get; set; }
        public int MaxSp { get; set; }
        public int BaseAc { get; }
        public int BaseToHit { get; set; }
        public int Dodge { get; }
        public int BaseMissile { get; set; }
        public int BaseMovementMax { get; }
        public double AttacksPerRound { get; }
        public int IconId { get; }
        public int PortraitId { get; }
        public int Brawn { get; }
        public int Knowledge { get; }
        public int Judgment { get; }
        public int Agility { get; }
        public int Vitality { get; }
        public int Luck { get; }
        public int ExhaustionStacks { get; set; }
        public int CurseStacks { get; set; }
        public ConditionTracker Conditions { get; }
        public IReadOnlyList<int> KnownSpellIds => _knownSpellIds;
        public IReadOnlyDictionary<EquipmentSlot, InventoryItem> Equipped => _equipped;
        public bool IsAlive => Hp > 0;

        public int EffectiveAc => GetEffectiveAc(_catalog);
        public int EffectiveMeleeToHit => GetEffectiveMeleeToHit(_catalog);
        public int EffectiveRangedToHit => GetEffectiveRangedToHit(_catalog);

        public static int GetXpRequiredForLevel(int level)
        {
            if (level <= 1)
            {
                return 0;
            }

            return (level - 1) * level * 100;
        }

        public int AddXp(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            Xp += amount;
            int levelsGained = 0;
            while (Level < MaxLevel && Xp >= GetXpRequiredForLevel(Level + 1))
            {
                Level++;
                levelsGained++;

                int hpGain = Math.Max(2, 4 + Math.Max(0, (Vitality - 10) / 2));
                MaxHp += hpGain;
                if (Hp > 0)
                {
                    Hp = Math.Min(MaxHp, Hp + hpGain);
                }

                if (MaxSp > 0)
                {
                    int casterStat = Math.Max(Knowledge, Judgment);
                    int spGain = Math.Max(2, 3 + Math.Max(0, (casterStat - 10) / 2));
                    MaxSp += spGain;
                    Sp = Math.Min(MaxSp, Sp + spGain);
                }

                BaseToHit += 2;
                if (BaseMissile > 0)
                {
                    BaseMissile += 2;
                }
            }

            return levelsGained;
        }

        public InventoryItem? GetEquipped(EquipmentSlot slot)
        {
            return _equipped.TryGetValue(slot, out InventoryItem? item) ? item : null;
        }

        internal void SetEquippedInternal(EquipmentSlot slot, InventoryItem item, IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            if (catalog != null)
            {
                _catalog = catalog;
            }

            _equipped[slot] = item;
        }

        internal bool RemoveEquippedInternal(EquipmentSlot slot, IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            if (catalog != null)
            {
                _catalog = catalog;
            }

            return _equipped.Remove(slot);
        }

        public int GetEffectiveAc(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            int ac = BaseAc;
            if (lookup != null)
            {
                foreach (InventoryItem equipped in _equipped.Values)
                {
                    if (lookup.TryGetValue(equipped.ItemId, out ItemDefinition? item))
                    {
                        ac += item.ArmorClassBonus;
                    }
                }
            }

            ac -= ExhaustionStacks + CurseStacks;
            return ac;
        }

        public int GetEffectiveToHit(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            return GetEffectiveMeleeToHit(catalog);
        }

        public int GetEffectiveMeleeToHit(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            int toHit = BaseToHit;
            if (lookup != null && _equipped.TryGetValue(EquipmentSlot.Weapon, out InventoryItem? weapon) &&
                lookup.TryGetValue(weapon.ItemId, out ItemDefinition? item))
            {
                toHit += item.WeaponBonus * 5;
            }

            toHit -= (ExhaustionStacks + CurseStacks) * 2;
            return toHit;
        }

        public int GetEffectiveRangedToHit(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            int toHit = BaseMissile > 0 ? BaseMissile : BaseToHit;
            if (lookup != null && _equipped.TryGetValue(EquipmentSlot.MissileWeapon, out InventoryItem? missile) &&
                lookup.TryGetValue(missile.ItemId, out ItemDefinition? item))
            {
                toHit += item.WeaponBonus * 5;
            }

            toHit -= (ExhaustionStacks + CurseStacks) * 2;
            return toHit;
        }

        public int GetEffectiveWeaponBonus(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            if (lookup != null && _equipped.TryGetValue(EquipmentSlot.Weapon, out InventoryItem? weapon) &&
                lookup.TryGetValue(weapon.ItemId, out ItemDefinition? item))
            {
                return item.WeaponBonus;
            }

            return 0;
        }

        public int GetEffectiveRangedWeaponBonus(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            if (lookup != null && _equipped.TryGetValue(EquipmentSlot.MissileWeapon, out InventoryItem? missile) &&
                lookup.TryGetValue(missile.ItemId, out ItemDefinition? item))
            {
                return item.WeaponBonus;
            }

            return 0;
        }

        public string GetEffectiveDamageDice(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            if (lookup != null && _equipped.TryGetValue(EquipmentSlot.Weapon, out InventoryItem? weapon) &&
                lookup.TryGetValue(weapon.ItemId, out ItemDefinition? item) &&
                !string.IsNullOrWhiteSpace(item.DamageDice))
            {
                return item.DamageDice;
            }

            return "1d4";
        }

        public int GetEffectiveRangedRange(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            if (lookup != null && _equipped.TryGetValue(EquipmentSlot.MissileWeapon, out InventoryItem? missile) &&
                lookup.TryGetValue(missile.ItemId, out ItemDefinition? item) &&
                item.Range > 1)
            {
                return item.Range;
            }

            return 0;
        }

        public string GetEffectiveRangedDice(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            if (lookup != null && _equipped.TryGetValue(EquipmentSlot.MissileWeapon, out InventoryItem? missile) &&
                lookup.TryGetValue(missile.ItemId, out ItemDefinition? item) &&
                !string.IsNullOrWhiteSpace(item.DamageDice))
            {
                return item.DamageDice;
            }

            return string.Empty;
        }

        public int GetEffectiveMovementMax(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            int move = BaseMovementMax;
            if (lookup != null)
            {
                foreach (InventoryItem equipped in _equipped.Values)
                {
                    if (lookup.TryGetValue(equipped.ItemId, out ItemDefinition? item))
                    {
                        move += item.MovementMod;
                    }
                }
            }

            return Math.Max(1, move);
        }

        public static bool TryParseSlot(string slotName, out EquipmentSlot slot)
        {
            slot = EquipmentSlot.Weapon;
            if (string.IsNullOrWhiteSpace(slotName))
            {
                return false;
            }

            switch (slotName.Trim().ToLowerInvariant())
            {
                case "weapon":
                case "melee":
                case "mainhand":
                case "main_hand":
                    slot = EquipmentSlot.Weapon;
                    return true;
                case "missileweapon":
                case "missile_weapon":
                case "missile":
                case "ranged":
                case "bow":
                    slot = EquipmentSlot.MissileWeapon;
                    return true;
                case "armor":
                case "body":
                case "chest":
                    slot = EquipmentSlot.Armor;
                    return true;
                case "shield":
                case "offhand":
                case "off_hand":
                    slot = EquipmentSlot.Shield;
                    return true;
                case "helm":
                case "helmet":
                case "head":
                    slot = EquipmentSlot.Helm;
                    return true;
                default:
                    return false;
            }
        }
    }

    public sealed class RestResult
    {
        public RestResult(CampMode mode, int totalHpRestored, int totalSpRestored, int exhaustionCleared, bool torchLit)
        {
            Mode = mode;
            TotalHpRestored = totalHpRestored;
            TotalSpRestored = totalSpRestored;
            ExhaustionCleared = exhaustionCleared;
            TorchLit = torchLit;
        }

        public CampMode Mode { get; }
        public int TotalHpRestored { get; }
        public int TotalSpRestored { get; }
        public int ExhaustionCleared { get; }
        public bool TorchLit { get; }
    }

    public sealed class PartyState
    {
        private readonly List<PartyMemberState> _members;
        private readonly List<InventoryItem> _stash;

        public PartyState(
            IEnumerable<PartyMemberState> members,
            IEnumerable<InventoryItem>? stash = null,
            int gold = 0,
            bool hasBoat = false,
            int torchTurnsRemaining = 0)
        {
            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            _members = new List<PartyMemberState>(members);
            _stash = stash != null ? new List<InventoryItem>(stash) : new List<InventoryItem>();
            Gold = Math.Max(0, gold);
            HasBoat = hasBoat;
            TorchTurnsRemaining = Math.Max(0, torchTurnsRemaining);
        }

        public static PartyState FromBundle(BywaterM1Bundle bundle)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            List<PartyMemberState> members = new List<PartyMemberState>(bundle.Party.Count);
            for (int i = 0; i < bundle.Party.Count; i++)
            {
                members.Add(new PartyMemberState(bundle.Party[i], bundle.Items));
            }

            PartyState party = new PartyState(members, gold: bundle.InitialGold);
            for (int i = 0; i < bundle.InitialStashItemIds.Count; i++)
            {
                int itemId = bundle.InitialStashItemIds[i];
                int charges = bundle.Items.TryGetValue(itemId, out ItemDefinition? def) ? def.Charges : 0;
                party.AddItemToStash(itemId, 1, isIdentified: true, charges: charges);
            }

            return party;
        }

        public IReadOnlyList<PartyMemberState> Members => _members;
        public IReadOnlyList<InventoryItem> Stash => _stash;
        public int Gold { get; set; }
        public bool HasBoat { get; set; }
        public int TorchTurnsRemaining { get; set; }

        public void AddItemToStash(int itemId, int quantity = 1, bool isIdentified = true, int charges = 0)
        {
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }

            if (charges <= 0)
            {
                for (int i = 0; i < _stash.Count; i++)
                {
                    InventoryItem existing = _stash[i];
                    if (existing.ItemId == itemId && existing.IsIdentified == isIdentified && existing.RemainingCharges <= 0)
                    {
                        existing.Quantity += quantity;
                        return;
                    }
                }
            }

            _stash.Add(new InventoryItem(itemId, quantity, isIdentified, charges));
        }

        public bool RemoveItemFromStash(int stashIndex, int quantity = 1)
        {
            if (stashIndex < 0 || stashIndex >= _stash.Count || quantity <= 0)
            {
                return false;
            }

            InventoryItem item = _stash[stashIndex];
            if (item.Quantity < quantity)
            {
                return false;
            }

            item.Quantity -= quantity;
            if (item.Quantity == 0)
            {
                _stash.RemoveAt(stashIndex);
            }

            return true;
        }

        public bool EquipFromStash(int heroIndex, int stashIndex, IReadOnlyDictionary<int, ItemDefinition> catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (heroIndex < 0 || heroIndex >= _members.Count || stashIndex < 0 || stashIndex >= _stash.Count)
            {
                return false;
            }

            InventoryItem stashEntry = _stash[stashIndex];
            if (!catalog.TryGetValue(stashEntry.ItemId, out ItemDefinition? def) ||
                !PartyMemberState.TryParseSlot(def.Slot, out EquipmentSlot slot))
            {
                return false;
            }

            PartyMemberState hero = _members[heroIndex];

            // Extract single item from stash.
            InventoryItem toEquip = new InventoryItem(stashEntry.ItemId, 1, stashEntry.IsIdentified, stashEntry.RemainingCharges);
            if (!RemoveItemFromStash(stashIndex, 1))
            {
                return false;
            }

            // Return any currently equipped item in target slot.
            if (hero.Equipped.TryGetValue(slot, out InventoryItem? previous))
            {
                AddItemToStash(previous.ItemId, previous.Quantity, previous.IsIdentified, previous.RemainingCharges);
            }

            // Two-handed weapons unequip any equipped shield; equipping a shield unequips a two-handed weapon.
            if (slot == EquipmentSlot.Weapon && def.IsTwoHanded &&
                hero.Equipped.TryGetValue(EquipmentSlot.Shield, out InventoryItem? prevShield))
            {
                hero.RemoveEquippedInternal(EquipmentSlot.Shield, catalog);
                AddItemToStash(prevShield.ItemId, prevShield.Quantity, prevShield.IsIdentified, prevShield.RemainingCharges);
            }
            else if (slot == EquipmentSlot.Shield &&
                     hero.Equipped.TryGetValue(EquipmentSlot.Weapon, out InventoryItem? prevWeapon) &&
                     catalog.TryGetValue(prevWeapon.ItemId, out ItemDefinition? prevWeaponDef) &&
                     prevWeaponDef.IsTwoHanded)
            {
                hero.RemoveEquippedInternal(EquipmentSlot.Weapon, catalog);
                AddItemToStash(prevWeapon.ItemId, prevWeapon.Quantity, prevWeapon.IsIdentified, prevWeapon.RemainingCharges);
            }

            hero.SetEquippedInternal(slot, toEquip, catalog);
            return true;
        }

        public bool UnequipToStash(int heroIndex, EquipmentSlot slot, IReadOnlyDictionary<int, ItemDefinition> catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (heroIndex < 0 || heroIndex >= _members.Count)
            {
                return false;
            }

            PartyMemberState hero = _members[heroIndex];
            if (!hero.Equipped.TryGetValue(slot, out InventoryItem? equipped))
            {
                return false;
            }

            hero.RemoveEquippedInternal(slot, catalog);
            AddItemToStash(equipped.ItemId, equipped.Quantity, equipped.IsIdentified, equipped.RemainingCharges);
            return true;
        }

        public RestResult Rest(CampMode mode)
        {
            int totalHp = 0;
            int totalSp = 0;
            int totalExhaustionCleared = 0;
            bool torchLit = false;

            if (mode == CampMode.FullRest)
            {
                for (int i = 0; i < _members.Count; i++)
                {
                    PartyMemberState m = _members[i];
                    if (m.Hp <= 0)
                    {
                        m.Hp = 1;
                    }

                    int hpGain = Math.Max(0, m.MaxHp - m.Hp);
                    int spGain = Math.Max(0, m.MaxSp - m.Sp);
                    m.Hp = m.MaxHp;
                    m.Sp = m.MaxSp;
                    totalHp += hpGain;
                    totalSp += spGain;
                    totalExhaustionCleared += m.ExhaustionStacks;
                    m.ExhaustionStacks = 0;
                    m.Conditions.Clear();
                }
            }
            else
            {
                for (int i = 0; i < _members.Count; i++)
                {
                    PartyMemberState m = _members[i];
                    if (m.Hp <= 0)
                    {
                        continue;
                    }

                    int healAmount = Math.Max(2, m.MaxHp / 3);
                    int before = m.Hp;
                    m.Hp = Math.Min(m.MaxHp, m.Hp + healAmount);
                    totalHp += Math.Max(0, m.Hp - before);
                }

                if (TorchTurnsRemaining <= 0)
                {
                    TorchTurnsRemaining = 30;
                    torchLit = true;
                }
            }

            return new RestResult(mode, totalHp, totalSp, totalExhaustionCleared, torchLit);
        }
    }

    public sealed class ShopFilter
    {
        public string? Category { get; set; }
        public bool AffordableOnly { get; set; }
        public string? SearchText { get; set; }
    }

    public sealed class ShopStockView
    {
        public ShopStockView(ShopStockItem stockItem, ItemDefinition item, int effectivePrice, bool canAfford)
        {
            StockItem = stockItem;
            Item = item;
            EffectivePrice = effectivePrice;
            CanAfford = canAfford;
        }

        public ShopStockItem StockItem { get; }
        public ItemDefinition Item { get; }
        public int EffectivePrice { get; }
        public bool CanAfford { get; }
    }

    public sealed class JunkSaleResult
    {
        public JunkSaleResult(int itemsSold, int goldEarned)
        {
            ItemsSold = itemsSold;
            GoldEarned = goldEarned;
        }

        public int ItemsSold { get; }
        public int GoldEarned { get; }
    }

    public sealed class IdentifyResult
    {
        public IdentifyResult(int itemsIdentified, int goldSpent)
        {
            ItemsIdentified = itemsIdentified;
            GoldSpent = goldSpent;
        }

        public int ItemsIdentified { get; }
        public int GoldSpent { get; }
    }

    public static class TownShopService
    {
        public static int GetEffectiveBuyPrice(ShopStockItem stock, ItemDefinition item, ShopDefinition shop)
        {
            if (stock.Price > 0)
            {
                return stock.Price;
            }

            if (shop.InflationPct == 0)
            {
                return Math.Max(1, item.BasePrice);
            }

            double multiplier = 1.0 + (shop.InflationPct / 100.0);
            return Math.Max(1, (int)Math.Round(item.BasePrice * multiplier, MidpointRounding.AwayFromZero));
        }

        public static int GetSellPrice(ItemDefinition item)
        {
            return Math.Max(1, item.BasePrice / 2);
        }

        public static IReadOnlyList<ShopStockView> FilterStock(
            ShopDefinition shop,
            IReadOnlyDictionary<int, ItemDefinition> catalog,
            PartyState party,
            ShopFilter? filter = null)
        {
            if (shop == null)
            {
                throw new ArgumentNullException(nameof(shop));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            List<ShopStockView> results = new List<ShopStockView>();
            string? category = filter?.Category;
            string? search = filter?.SearchText;
            bool affordableOnly = filter?.AffordableOnly ?? false;

            for (int i = 0; i < shop.Stock.Count; i++)
            {
                ShopStockItem stockItem = shop.Stock[i];
                if (stockItem.Quantity == 0)
                {
                    continue;
                }

                if (!catalog.TryGetValue(stockItem.ItemId, out ItemDefinition? def))
                {
                    continue;
                }

                int price = GetEffectiveBuyPrice(stockItem, def, shop);
                bool canAfford = party.Gold >= price;
                if (affordableOnly && !canAfford)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(category) &&
                    !string.Equals(def.Category, category!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(search) &&
                    def.Name.IndexOf(search!.Trim(), StringComparison.OrdinalIgnoreCase) < 0 &&
                    def.Description.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                results.Add(new ShopStockView(stockItem, def, price, canAfford));
            }

            return results;
        }

        public static bool BuyItem(
            PartyState party,
            ShopDefinition shop,
            int itemId,
            int qty,
            IReadOnlyDictionary<int, ItemDefinition> catalog)
        {
            if (party == null || shop == null || catalog == null || qty <= 0)
            {
                return false;
            }

            ShopStockItem? match = null;
            for (int i = 0; i < shop.Stock.Count; i++)
            {
                if (shop.Stock[i].ItemId == itemId)
                {
                    match = shop.Stock[i];
                    break;
                }
            }

            if (match == null || !catalog.TryGetValue(itemId, out ItemDefinition? def))
            {
                return false;
            }

            if (match.Quantity >= 0 && match.Quantity < qty)
            {
                return false;
            }

            int unitPrice = GetEffectiveBuyPrice(match, def, shop);
            long totalCost = (long)unitPrice * qty;
            if (totalCost > party.Gold)
            {
                return false;
            }

            party.Gold -= (int)totalCost;
            party.AddItemToStash(itemId, qty, isIdentified: true, charges: def.Charges);
            return true;
        }

        public static bool SellItem(
            PartyState party,
            int stashIndex,
            int qty,
            IReadOnlyDictionary<int, ItemDefinition> catalog)
        {
            if (party == null || catalog == null || qty <= 0 || stashIndex < 0 || stashIndex >= party.Stash.Count)
            {
                return false;
            }

            InventoryItem entry = party.Stash[stashIndex];
            if (entry.Quantity < qty || !catalog.TryGetValue(entry.ItemId, out ItemDefinition? def))
            {
                return false;
            }

            int unitPrice = GetSellPrice(def);
            if (!party.RemoveItemFromStash(stashIndex, qty))
            {
                return false;
            }

            party.Gold += unitPrice * qty;
            return true;
        }

        public static JunkSaleResult SellCommonJunk(
            PartyState party,
            IReadOnlyDictionary<int, ItemDefinition> catalog)
        {
            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            int totalSold = 0;
            int totalGold = 0;

            for (int i = party.Stash.Count - 1; i >= 0; i--)
            {
                InventoryItem entry = party.Stash[i];
                if (!catalog.TryGetValue(entry.ItemId, out ItemDefinition? def))
                {
                    continue;
                }

                bool isJunk = def.IsJunk || string.Equals(def.Category, "Junk", StringComparison.OrdinalIgnoreCase);
                if (!isJunk)
                {
                    continue;
                }

                int qty = entry.Quantity;
                int gold = GetSellPrice(def) * qty;
                if (party.RemoveItemFromStash(i, qty))
                {
                    totalSold += qty;
                    totalGold += gold;
                    party.Gold += gold;
                }
            }

            return new JunkSaleResult(totalSold, totalGold);
        }

        public static IdentifyResult IdentifyAll(PartyState party, int costPerItem)
        {
            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            int unitCost = Math.Max(0, costPerItem);
            int identified = 0;
            int spent = 0;

            for (int i = 0; i < party.Stash.Count; i++)
            {
                InventoryItem item = party.Stash[i];
                if (item.IsIdentified)
                {
                    continue;
                }

                if (party.Gold < unitCost)
                {
                    break;
                }

                party.Gold -= unitCost;
                item.IsIdentified = true;
                identified++;
                spent += unitCost;
            }

            return new IdentifyResult(identified, spent);
        }
    }
}
