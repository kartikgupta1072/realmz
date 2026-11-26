using System;
using System.Collections.Generic;
using RealmzCore.Data;

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
            string summary = "")
        {
            RequiredPieces = Math.Max(1, requiredPieces);
            AcBonus = acBonus;
            ApBonus = apBonus;
            FreeMoveTiles = freeMoveTiles;
            GuardApDiscount = guardApDiscount;
            Summary = summary ?? string.Empty;
        }

        public int RequiredPieces { get; }
        public int AcBonus { get; }
        public int ApBonus { get; }
        public int FreeMoveTiles { get; }
        public int GuardApDiscount { get; }
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
            int guardApDiscount)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            EquippedPieces = Math.Max(0, equippedPieces);
            AcBonus = acBonus;
            ApBonus = apBonus;
            FreeMoveTiles = freeMoveTiles;
            GuardApDiscount = guardApDiscount;
        }

        public EquipmentSetDefinition Definition { get; }
        public int EquippedPieces { get; }
        public int TargetPieces => Definition.DisplayMaxPieces;
        public int AcBonus { get; }
        public int ApBonus { get; }
        public int FreeMoveTiles { get; }
        public int GuardApDiscount { get; }
        public bool HasAnyActiveTier => AcBonus != 0 || ApBonus != 0 || FreeMoveTiles != 0 || GuardApDiscount != 0;

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
        public const string CryptbreakerSetId = "cryptbreaker";

        public static readonly EquipmentSetDefinition ScoutsLeather = new EquipmentSetDefinition(
            setId: ScoutsLeatherSetId,
            name: "Scout's Leather",
            itemIds: new[] { 209, 210, 211, 450, 452, 454 },
            tiers: new[]
            {
                new EquipmentSetBonusTier(
                    requiredPieces: 2,
                    freeMoveTiles: 2,
                    summary: "+2 MP"),
                new EquipmentSetBonusTier(
                    requiredPieces: 3,
                    apBonus: 1,
                    summary: "+1 Max AP")
            },
            displayMaxPieces: 3);

        public static readonly EquipmentSetDefinition IronVanguard = new EquipmentSetDefinition(
            setId: IronVanguardSetId,
            name: "Iron Vanguard",
            itemIds: new[] { 400, 418, 455, 20, 10, 11 },
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
            itemIds: new[] { 9101, 9102, 9103 },
            tiers: new[]
            {
                new EquipmentSetBonusTier(
                    requiredPieces: 2,
                    acBonus: 1,
                    freeMoveTiles: 1,
                    summary: "+1 AC & +1 MP"),
                new EquipmentSetBonusTier(
                    requiredPieces: 3,
                    apBonus: 1,
                    summary: "+1 Max AP")
            },
            displayMaxPieces: 3,
            isPreview: true);

        public static readonly EquipmentSetDefinition Cryptbreaker = new EquipmentSetDefinition(
            setId: CryptbreakerSetId,
            name: "Cryptbreaker",
            itemIds: new[] { 9201, 9202, 9203 },
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
            displayMaxPieces: 3,
            isPreview: true);

        public static readonly IReadOnlyList<EquipmentSetDefinition> AllSets = new[]
        {
            ScoutsLeather,
            IronVanguard,
            ThornwoodStalker,
            Cryptbreaker
        };

        public static readonly IReadOnlyList<EquipmentSetDefinition> PreviewSets = new[]
        {
            ThornwoodStalker,
            Cryptbreaker
        };

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
            for (int i = 0; i < definition.Tiers.Count; i++)
            {
                EquipmentSetBonusTier tier = definition.Tiers[i];
                if (pieces >= tier.RequiredPieces)
                {
                    ac += tier.AcBonus;
                    ap += tier.ApBonus;
                    freeMove += tier.FreeMoveTiles;
                    guardDiscount += tier.GuardApDiscount;
                }
            }

            return new EquipmentSetProgress(definition, pieces, ac, ap, freeMove, guardDiscount);
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
                return isRangedSlot ? 3 : 2;
            }

            return IsLightWeaponName(item.Name) ? 2 : 3;
        }

        public static int GetSpellApCost(SpellDefinition? spell)
        {
            if (spell == null)
            {
                return 3;
            }

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
                return 2;
            }

            if (spell.Id == 1306 ||
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
                return 4;
            }

            return 3;
        }
    }
}
