using System;
using System.Collections.Generic;
using RealmzCore.Data;
using RealmzCore.Rules;

namespace RealmzCore.Exploration
{
    public enum FormationRank
    {
        Front = 0,
        Mid = 1,
        Back = 2
    }

    public sealed class EquipmentSetBonusTier
    {
        public EquipmentSetBonusTier(
            int requiredPieces,
            int acBonus = 0,
            int apBonus = 0,
            int freeMoveTiles = 0,
            int guardApDiscount = 0,
            string summary = "",
            int critBonusPct = 0,
            int rangedRangeBonus = 0,
            bool ignoreHalfCover = false,
            int spCostDiscount = 0,
            int spellDamageBonus = 0,
            int healingBonus = 0,
            int supportSpellApDiscount = 0,
            int undeadDamageBonusPct = 0)
        {
            RequiredPieces = Math.Max(1, requiredPieces);
            AcBonus = acBonus;
            ApBonus = apBonus;
            FreeMoveTiles = freeMoveTiles;
            GuardApDiscount = guardApDiscount;
            CritBonusPct = critBonusPct;
            RangedRangeBonus = rangedRangeBonus;
            IgnoreHalfCover = ignoreHalfCover;
            SpCostDiscount = spCostDiscount;
            SpellDamageBonus = spellDamageBonus;
            HealingBonus = healingBonus;
            SupportSpellApDiscount = supportSpellApDiscount;
            UndeadDamageBonusPct = undeadDamageBonusPct;
            Summary = summary ?? string.Empty;
        }

        public int RequiredPieces { get; }
        public int AcBonus { get; }
        public int ApBonus { get; }
        public int FreeMoveTiles { get; }
        public int GuardApDiscount { get; }
        public int CritBonusPct { get; }
        public int RangedRangeBonus { get; }
        public bool IgnoreHalfCover { get; }
        public int SpCostDiscount { get; }
        public int SpellDamageBonus { get; }
        public int HealingBonus { get; }
        public int SupportSpellApDiscount { get; }
        public int UndeadDamageBonusPct { get; }
        public string Summary { get; }
    }

    public sealed class EquipmentSetDefinition
    {
        private readonly HashSet<int> _itemIdSet;

        public EquipmentSetDefinition(
            string setId,
            string name,
            IReadOnlyList<int> itemIds,
            IReadOnlyList<EquipmentSetBonusTier> tiers,
            int displayMaxPieces = 3,
            bool isPreview = false)
        {
            SetId = setId ?? throw new ArgumentNullException(nameof(setId));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ItemIds = itemIds ?? Array.Empty<int>();
            _itemIdSet = new HashSet<int>(ItemIds);
            Tiers = tiers ?? Array.Empty<EquipmentSetBonusTier>();
            DisplayMaxPieces = Math.Max(1, displayMaxPieces);
            IsPreview = isPreview;
        }

        public string SetId { get; }
        public string Name { get; }
        public IReadOnlyList<int> ItemIds { get; }
        public IReadOnlyList<EquipmentSetBonusTier> Tiers { get; }
        public int DisplayMaxPieces { get; }
        public bool IsPreview { get; }

        public bool ContainsItem(int itemId) => _itemIdSet.Contains(itemId);
    }

    public sealed class EquipmentSetProgress
    {
        public EquipmentSetProgress(
            EquipmentSetDefinition definition,
            int equippedPieces,
            int acBonus,
            int apBonus,
            int freeMoveTiles,
            int guardApDiscount,
            int critBonusPct = 0,
            int rangedRangeBonus = 0,
            bool ignoreHalfCover = false,
            int spCostDiscount = 0,
            int spellDamageBonus = 0,
            int healingBonus = 0,
            int supportSpellApDiscount = 0,
            int undeadDamageBonusPct = 0)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            EquippedPieces = Math.Max(0, equippedPieces);
            AcBonus = acBonus;
            ApBonus = apBonus;
            FreeMoveTiles = freeMoveTiles;
            GuardApDiscount = guardApDiscount;
            CritBonusPct = critBonusPct;
            RangedRangeBonus = rangedRangeBonus;
            IgnoreHalfCover = ignoreHalfCover;
            SpCostDiscount = spCostDiscount;
            SpellDamageBonus = spellDamageBonus;
            HealingBonus = healingBonus;
            SupportSpellApDiscount = supportSpellApDiscount;
            UndeadDamageBonusPct = undeadDamageBonusPct;
        }

        public EquipmentSetDefinition Definition { get; }
        public int EquippedPieces { get; }
        public int TargetPieces => Definition.DisplayMaxPieces;
        public int AcBonus { get; }
        public int ApBonus { get; }
        public int FreeMoveTiles { get; }
        public int GuardApDiscount { get; }
        public int CritBonusPct { get; }
        public int RangedRangeBonus { get; }
        public bool IgnoreHalfCover { get; }
        public int SpCostDiscount { get; }
        public int SpellDamageBonus { get; }
        public int HealingBonus { get; }
        public int SupportSpellApDiscount { get; }
        public int UndeadDamageBonusPct { get; }
        public bool HasAnyActiveTier =>
            AcBonus != 0 ||
            ApBonus != 0 ||
            FreeMoveTiles != 0 ||
            GuardApDiscount != 0 ||
            CritBonusPct != 0 ||
            RangedRangeBonus != 0 ||
            IgnoreHalfCover ||
            SpCostDiscount != 0 ||
            SpellDamageBonus != 0 ||
            HealingBonus != 0 ||
            SupportSpellApDiscount != 0 ||
            UndeadDamageBonusPct != 0;

        public string FormatProgressTag()
        {
            return $"{Definition.Name} ({EquippedPieces}/{TargetPieces} pcs)";
        }

        public string FormatSummaryLine()
        {
            List<string> parts = new List<string>(Definition.Tiers.Count);
            for (int i = 0; i < Definition.Tiers.Count; i++)
            {
                EquipmentSetBonusTier tier = Definition.Tiers[i];
                bool unlocked = EquippedPieces >= tier.RequiredPieces;
                string mark = unlocked ? "[ON]" : $"[{tier.RequiredPieces}pc]";
                parts.Add($"{mark} {tier.Summary}");
            }

            return $"{Definition.Name} ({EquippedPieces}/{TargetPieces} pcs): {string.Join(" | ", parts)}";
        }
    }

    public static class EquipmentSetCatalog
    {
        public const string ScoutsLeatherSetId = "scouts_leather";
        public const string IronVanguardSetId = "iron_vanguard";
        public const string ThornwoodStalkerSetId = "thornwood_stalker";
        public const string BywaterArcanistSetId = "bywater_arcanist";
        public const string SanctuaryAcolyteSetId = "sanctuary_acolyte";
        public const string CryptbreakerSetId = "cryptbreaker";

        public static readonly EquipmentSetDefinition ScoutsLeather = new EquipmentSetDefinition(
            setId: ScoutsLeatherSetId,
            name: "Scout's Leather",
            itemIds: new[] { 209, 210, 211, 450, 452, 454, 902 },
            tiers: new[]
            {
                new EquipmentSetBonusTier(
                    requiredPieces: 2,
                    freeMoveTiles: 2,
                    summary: "+2 MP"),
                new EquipmentSetBonusTier(
                    requiredPieces: 3,
                    apBonus: 1,
                    critBonusPct: 10,
                    summary: "+1 Max AP & +10% Crit")
            },
            displayMaxPieces: 3);

        public static readonly EquipmentSetDefinition IronVanguard = new EquipmentSetDefinition(
            setId: IronVanguardSetId,
            name: "Iron Vanguard",
            itemIds: new[] { 400, 418, 455, 219 },
            tiers: new[]
            {
                new EquipmentSetBonusTier(
                    requiredPieces: 2,
                    acBonus: 2,
                    summary: "+2 AC"),
                new EquipmentSetBonusTier(
                    requiredPieces: 3,
                    apBonus: 1,
                    guardApDiscount: 1,
                    summary: "Guard costs 1 AP & +1 Max AP")
            },
            displayMaxPieces: 3);

        public static readonly EquipmentSetDefinition ThornwoodStalker = new EquipmentSetDefinition(
            setId: ThornwoodStalkerSetId,
            name: "Thornwood Stalker",
            itemIds: new[] { 903, 904, 905 },
            tiers: new[]
            {
                new EquipmentSetBonusTier(
                    requiredPieces: 2,
                    freeMoveTiles: 1,
                    rangedRangeBonus: 1,
                    summary: "+1 MP & +1 Ranged Range"),
                new EquipmentSetBonusTier(
                    requiredPieces: 3,
                    apBonus: 1,
                    ignoreHalfCover: true,
                    summary: "+1 Max AP & Ignore Half-Cover")
            },
            displayMaxPieces: 3);

        public static readonly EquipmentSetDefinition BywaterArcanist = new EquipmentSetDefinition(
            setId: BywaterArcanistSetId,
            name: "Bywater Arcanist",
            itemIds: new[] { 906, 907, 908 },
            tiers: new[]
            {
                new EquipmentSetBonusTier(
                    requiredPieces: 2,
                    freeMoveTiles: 1,
                    spCostDiscount: 1,
                    summary: "-1 SP Cost & +1 MP"),
                new EquipmentSetBonusTier(
                    requiredPieces: 3,
                    apBonus: 1,
                    spellDamageBonus: 2,
                    summary: "+1 Max AP & +2 Spell Damage")
            },
            displayMaxPieces: 3);

        public static readonly EquipmentSetDefinition SanctuaryAcolyte = new EquipmentSetDefinition(
            setId: SanctuaryAcolyteSetId,
            name: "Sanctuary Acolyte",
            itemIds: new[] { 909, 910, 911 },
            tiers: new[]
            {
                new EquipmentSetBonusTier(
                    requiredPieces: 2,
                    acBonus: 1,
                    healingBonus: 2,
                    summary: "+2 Healing & +1 AC"),
                new EquipmentSetBonusTier(
                    requiredPieces: 3,
                    apBonus: 1,
                    supportSpellApDiscount: 1,
                    summary: "+1 Max AP & Cure/Bless -1 AP")
            },
            displayMaxPieces: 3);

        public static readonly EquipmentSetDefinition Cryptbreaker = new EquipmentSetDefinition(
            setId: CryptbreakerSetId,
            name: "Cryptbreaker",
            itemIds: new[] { 912, 913, 914 },
            tiers: new[]
            {
                new EquipmentSetBonusTier(
                    requiredPieces: 2,
                    acBonus: 2,
                    undeadDamageBonusPct: 25,
                    summary: "+2 AC & +25% vs Undead"),
                new EquipmentSetBonusTier(
                    requiredPieces: 3,
                    apBonus: 1,
                    freeMoveTiles: 1,
                    summary: "+1 Max AP & +1 MP")
            },
            displayMaxPieces: 3);

        public static readonly IReadOnlyList<EquipmentSetDefinition> AllSets = new[]
        {
            ScoutsLeather,
            IronVanguard,
            ThornwoodStalker,
            BywaterArcanist,
            SanctuaryAcolyte,
            Cryptbreaker
        };

        public static readonly IReadOnlyList<EquipmentSetDefinition> PreviewSets = Array.Empty<EquipmentSetDefinition>();

        public static IReadOnlyList<EquipmentSetDefinition> GetSetsForItem(int itemId)
        {
            List<EquipmentSetDefinition>? matches = null;
            for (int i = 0; i < AllSets.Count; i++)
            {
                if (AllSets[i].ContainsItem(itemId))
                {
                    matches ??= new List<EquipmentSetDefinition>(1);
                    matches.Add(AllSets[i]);
                }
            }

            return matches ?? (IReadOnlyList<EquipmentSetDefinition>)Array.Empty<EquipmentSetDefinition>();
        }

        public static EquipmentSetProgress EvaluateSet(
            EquipmentSetDefinition definition,
            IEnumerable<KeyValuePair<EquipmentSlot, InventoryItem>> equippedItems)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            int pieces = 0;
            HashSet<EquipmentSlot> countedSlots = new HashSet<EquipmentSlot>();
            if (equippedItems != null)
            {
                foreach (KeyValuePair<EquipmentSlot, InventoryItem> kvp in equippedItems)
                {
                    if (kvp.Value != null &&
                        definition.ContainsItem(kvp.Value.ItemId) &&
                        countedSlots.Add(kvp.Key))
                    {
                        pieces++;
                    }
                }
            }

            int ac = 0;
            int ap = 0;
            int freeMove = 0;
            int guardDiscount = 0;
            int critBonusPct = 0;
            int rangedRangeBonus = 0;
            bool ignoreHalfCover = false;
            int spCostDiscount = 0;
            int spellDamageBonus = 0;
            int healingBonus = 0;
            int supportSpellApDiscount = 0;
            int undeadDamageBonusPct = 0;

            for (int i = 0; i < definition.Tiers.Count; i++)
            {
                EquipmentSetBonusTier tier = definition.Tiers[i];
                if (pieces >= tier.RequiredPieces)
                {
                    ac += tier.AcBonus;
                    ap += tier.ApBonus;
                    freeMove += tier.FreeMoveTiles;
                    guardDiscount += tier.GuardApDiscount;
                    critBonusPct += tier.CritBonusPct;
                    rangedRangeBonus += tier.RangedRangeBonus;
                    ignoreHalfCover |= tier.IgnoreHalfCover;
                    spCostDiscount += tier.SpCostDiscount;
                    spellDamageBonus += tier.SpellDamageBonus;
                    healingBonus += tier.HealingBonus;
                    supportSpellApDiscount += tier.SupportSpellApDiscount;
                    undeadDamageBonusPct += tier.UndeadDamageBonusPct;
                }
            }

            return new EquipmentSetProgress(
                definition,
                pieces,
                ac,
                ap,
                freeMove,
                guardDiscount,
                critBonusPct,
                rangedRangeBonus,
                ignoreHalfCover,
                spCostDiscount,
                spellDamageBonus,
                healingBonus,
                supportSpellApDiscount,
                undeadDamageBonusPct);
        }

        public static IReadOnlyList<EquipmentSetProgress> EvaluateHeroSets(
            IEnumerable<KeyValuePair<EquipmentSlot, InventoryItem>> equippedItems,
            bool includeInactiveWithZeroPieces = false)
        {
            List<EquipmentSetProgress> results = new List<EquipmentSetProgress>(AllSets.Count);
            for (int i = 0; i < AllSets.Count; i++)
            {
                EquipmentSetProgress progress = EvaluateSet(AllSets[i], equippedItems);
                if (includeInactiveWithZeroPieces || progress.EquippedPieces > 0)
                {
                    results.Add(progress);
                }
            }

            return results;
        }
    }

    public static class CombatWeaponCosts
    {
        public static bool IsLightWeaponName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return name.IndexOf("Dagger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Short Sword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Shortsword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Short Bow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Shortbow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Dart", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Knife", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Throwing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Sling", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int GetWeaponApCost(ItemDefinition? item, bool isRangedSlot = false)
        {
            if (item == null)
            {
                return isRangedSlot
                    ? CombatActionEconomy.StandardWeaponApCost
                    : CombatActionEconomy.LightWeaponApCost;
            }

            return IsLightWeaponName(item.Name)
                ? CombatActionEconomy.LightWeaponApCost
                : CombatActionEconomy.StandardWeaponApCost;
        }

        public static bool IsSupportSpell(SpellDefinition? spell)
        {
            if (spell == null)
            {
                return false;
            }

            string effectKind = spell.EffectKind ?? string.Empty;
            string name = spell.Name ?? string.Empty;
            return string.Equals(effectKind, "Heal", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(effectKind, "Buff", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(effectKind, "Cure", StringComparison.OrdinalIgnoreCase) ||
                   name.IndexOf("Heal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Cure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Bless", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int GetSpellApCost(SpellDefinition? spell, int supportSpellApDiscount = 0)
        {
            if (spell == null)
            {
                return 4;
            }

            int baseCost;
            string key = spell.Key ?? string.Empty;
            string name = spell.Name ?? string.Empty;
            if (spell.Id == 1108 ||
                spell.Id == 2105 ||
                string.Equals(key, "magic_missile", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "magic_darts", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "heal_light_wounds", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "heal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "grease", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "oil", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("Magic Darts", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Minor Heal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Heal Small", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                baseCost = 3;
            }
            else if (spell.Id == 1306 ||
                     spell.Id == 3202 ||
                     spell.Id == 2310 ||
                     string.Equals(key, "fireball", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(key, "hold", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(key, "daze", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(key, "sleep", StringComparison.OrdinalIgnoreCase) ||
                     name.IndexOf("Fireball", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("Daze", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("Sandman", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                baseCost = 5;
            }
            else
            {
                baseCost = 4;
            }

            if (supportSpellApDiscount > 0 && IsSupportSpell(spell))
            {
                return Math.Max(2, baseCost - supportSpellApDiscount);
            }

            return baseCost;
        }
    }
}
