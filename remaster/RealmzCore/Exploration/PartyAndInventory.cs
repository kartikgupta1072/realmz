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
        Helm = 4,
        Hands = 5,
        Feet = 6,
        Accessory1 = 7,
        Accessory2 = 8
    }

    public sealed class AttributeAllocation
    {
        public int BrawnDelta { get; set; }
        public int KnowledgeDelta { get; set; }
        public int JudgmentDelta { get; set; }
        public int AgilityDelta { get; set; }
        public int VitalityDelta { get; set; }
        public int LuckDelta { get; set; }

        public int TotalPoints =>
            BrawnDelta + KnowledgeDelta + JudgmentDelta + AgilityDelta + VitalityDelta + LuckDelta;
    }

    public sealed class LevelUpPreview
    {
        public LevelUpPreview(
            int currentLevel,
            int targetLevel,
            int hpGain,
            int spGain,
            int toHitGain,
            int attributePointsToSpend,
            IReadOnlyList<int> unlockableSpellIds)
        {
            CurrentLevel = currentLevel;
            TargetLevel = targetLevel;
            HpGain = hpGain;
            SpGain = spGain;
            ToHitGain = toHitGain;
            AttributePointsToSpend = attributePointsToSpend;
            UnlockableSpellIds = unlockableSpellIds ?? Array.Empty<int>();
        }

        public int CurrentLevel { get; }
        public int TargetLevel { get; }
        public int HpGain { get; }
        public int SpGain { get; }
        public int ToHitGain { get; }
        public int AttributePointsToSpend { get; }
        public IReadOnlyList<int> UnlockableSpellIds { get; }
    }

    public enum CampMode
    {
        QuickBandage = 0,
        FullRest = 1
    }

    public enum PartyFatigueState
    {
        Fresh = 0,
        Winded = 1,
        Exhausted = 2
    }

    public enum OutOfCombatSpellKind
    {
        HealParty = 0,
        CureParty = 1,
        ConjureLight = 2,
        CampSentry = 3,
        RefreshParty = 4
    }

    public sealed class OutOfCombatSpellResult
    {
        public OutOfCombatSpellResult(
            bool success,
            string message,
            string casterName = "",
            int spSpent = 0,
            int totalHealing = 0,
            bool torchLit = false,
            bool sentryActivated = false,
            double fatigueReduced = 0.0,
            int conditionsCured = 0)
        {
            Success = success;
            Message = message ?? string.Empty;
            CasterName = casterName ?? string.Empty;
            SpSpent = spSpent;
            TotalHealing = totalHealing;
            TorchLit = torchLit;
            SentryActivated = sentryActivated;
            FatigueReduced = fatigueReduced;
            ConditionsCured = conditionsCured;
        }

        public bool Success { get; }
        public string Message { get; }
        public string CasterName { get; }
        public int SpSpent { get; }
        public int TotalHealing { get; }
        public bool TorchLit { get; }
        public bool SentryActivated { get; }
        public double FatigueReduced { get; }
        public int ConditionsCured { get; }
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
            FormationRank = ResolveDefaultFormationRank(Id, Name, 0);

            if (catalog != null && preset.EquippedIds.Count > 0)
            {
                for (int i = 0; i < preset.EquippedIds.Count; i++)
                {
                    int itemId = preset.EquippedIds[i];
                    if (catalog.TryGetValue(itemId, out ItemDefinition? item) &&
                        TryParseSlot(item.Slot, out EquipmentSlot slot))
                    {
                        if ((slot == EquipmentSlot.Accessory1 || slot == EquipmentSlot.Accessory2) &&
                            _equipped.ContainsKey(slot))
                        {
                            EquipmentSlot fallbackSlot = slot == EquipmentSlot.Accessory1
                                ? EquipmentSlot.Accessory2
                                : EquipmentSlot.Accessory1;
                            if (!_equipped.ContainsKey(fallbackSlot))
                            {
                                slot = fallbackSlot;
                            }
                        }

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
        public int PendingLevelUps { get; set; }
        public bool HasPendingLevelUp => PendingLevelUps > 0 && Level < MaxLevel;
        public int EffectiveTargetLevel => Math.Min(MaxLevel, Level + PendingLevelUps);
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
        public int Brawn { get; set; }
        public int Knowledge { get; set; }
        public int Judgment { get; set; }
        public int Agility { get; set; }
        public int Vitality { get; set; }
        public int Luck { get; set; }
        public int ExhaustionStacks { get; set; }
        public int CurseStacks { get; set; }
        public FormationRank FormationRank { get; set; } = FormationRank.Mid;
        public ConditionTracker Conditions { get; }
        public IReadOnlyList<int> KnownSpellIds => _knownSpellIds;
        public IReadOnlyDictionary<EquipmentSlot, InventoryItem> Equipped => _equipped;
        public bool IsAlive => Hp > 0;

        public int EffectiveAc => GetEffectiveAc(_catalog);
        public int EffectiveMeleeToHit => GetEffectiveMeleeToHit(_catalog);
        public int EffectiveRangedToHit => GetEffectiveRangedToHit(_catalog);

        public int GetMeleeDamageAttributeBonus() => Math.Max(0, (Brawn - 12) / 3);
        public int GetCarryCapacityContribution() => 120 + Math.Max(1, Brawn) * 25;
        public int GetArcaneSpellPowerBonus() => Math.Max(0, (Knowledge - 10) / 4);
        public int GetSpellHitDcBonusPercent() => Knowledge - 10;
        public int GetDivineHealingBonus() => Math.Max(0, (Judgment - 10) / 4);
        public int GetMagicResistancePercent() => Math.Max(0, (Judgment - 10) * 2) + GetAllSavesLuckBonusPercent();
        public int GetDodgeAcBonus() => Math.Max(0, (Agility - 12) / 3);
        public int GetRangedHitAgilityBonusPercent() => Math.Max(0, (Agility - 12) * 2);
        public int GetConditionSaveBonusPercent() => Math.Max(0, (Vitality - 10) * 2) + GetAllSavesLuckBonusPercent();
        public int GetCritChancePercent() => 5 + Math.Max(0, (Luck - 10) / 2);
        public int GetAllSavesLuckBonusPercent() => Math.Max(0, Luck - 10);

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
                ApplySingleLevelUpGrowth();
                levelsGained++;
            }

            if (PendingLevelUps > 0)
            {
                PendingLevelUps = Math.Max(0, PendingLevelUps - levelsGained);
            }

            return levelsGained;
        }

        public int AwardXp(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            Xp += amount;
            int newlyCrossed = 0;
            while (Level + PendingLevelUps < MaxLevel &&
                   Xp >= GetXpRequiredForLevel(Level + PendingLevelUps + 1))
            {
                PendingLevelUps++;
                newlyCrossed++;
            }

            return newlyCrossed;
        }

        public LevelUpPreview GetLevelUpPreview(AttributeAllocation? alloc = null)
        {
            int targetLevel = Math.Min(MaxLevel, Level + 1);
            int vit = Math.Min(25, Vitality + (alloc != null ? Math.Max(0, alloc.VitalityDelta) : 0));
            int kn = Math.Min(25, Knowledge + (alloc != null ? Math.Max(0, alloc.KnowledgeDelta) : 0));
            int ju = Math.Min(25, Judgment + (alloc != null ? Math.Max(0, alloc.JudgmentDelta) : 0));

            int hpGain = ComputeLevelHpGain(Archetype, vit);
            int spGain = ComputeLevelSpGain(Archetype, MaxSp, kn, ju);
            int toHitGain = 2;
            List<int> unlocks = GetUnlockedSpellCandidatesForLevel(targetLevel);
            return new LevelUpPreview(
                currentLevel: Level,
                targetLevel: targetLevel,
                hpGain: hpGain,
                spGain: spGain,
                toHitGain: toHitGain,
                attributePointsToSpend: 2,
                unlockableSpellIds: unlocks);
        }

        public bool ApplyPendingLevelUp(AttributeAllocation alloc, int? learnedSpellId = null)
        {
            if (alloc == null || !HasPendingLevelUp)
            {
                return false;
            }

            if (alloc.BrawnDelta < 0 || alloc.KnowledgeDelta < 0 || alloc.JudgmentDelta < 0 ||
                alloc.AgilityDelta < 0 || alloc.VitalityDelta < 0 || alloc.LuckDelta < 0)
            {
                return false;
            }

            if (alloc.TotalPoints != 2)
            {
                return false;
            }

            if (Brawn + alloc.BrawnDelta > 25 || Knowledge + alloc.KnowledgeDelta > 25 ||
                Judgment + alloc.JudgmentDelta > 25 || Agility + alloc.AgilityDelta > 25 ||
                Vitality + alloc.VitalityDelta > 25 || Luck + alloc.LuckDelta > 25)
            {
                return false;
            }

            Brawn += alloc.BrawnDelta;
            Knowledge += alloc.KnowledgeDelta;
            Judgment += alloc.JudgmentDelta;
            Agility += alloc.AgilityDelta;
            Vitality += alloc.VitalityDelta;
            Luck += alloc.LuckDelta;

            ApplySingleLevelUpGrowth();
            PendingLevelUps--;

            if (learnedSpellId.HasValue && learnedSpellId.Value > 0 && !_knownSpellIds.Contains(learnedSpellId.Value))
            {
                _knownSpellIds.Add(learnedSpellId.Value);
            }
            else
            {
                List<int> autoUnlocks = GetUnlockedSpellCandidatesForLevel(Level);
                if (autoUnlocks.Count > 0 && !_knownSpellIds.Contains(autoUnlocks[0]))
                {
                    _knownSpellIds.Add(autoUnlocks[0]);
                }
            }

            return true;
        }

        public AttributeAllocation GetRecommendedAttributeAllocation()
        {
            string arch = (Archetype ?? string.Empty).Trim().ToLowerInvariant();
            if (arch == "cleric" || arch == "priest")
            {
                return new AttributeAllocation { JudgmentDelta = 1, VitalityDelta = 1 };
            }
            if (arch == "mage" || arch == "sorcerer" || arch == "wizard")
            {
                return Knowledge <= 23
                    ? new AttributeAllocation { KnowledgeDelta = 2 }
                    : new AttributeAllocation { KnowledgeDelta = 1, AgilityDelta = 1 };
            }
            if (arch == "ranger" || arch == "archer")
            {
                return new AttributeAllocation { AgilityDelta = 1, BrawnDelta = 1 };
            }
            if (arch == "paladin" || arch == "crusader")
            {
                return new AttributeAllocation { BrawnDelta = 1, JudgmentDelta = 1 };
            }
            if (arch == "rogue" || arch == "thief")
            {
                return new AttributeAllocation { AgilityDelta = 1, LuckDelta = 1 };
            }
            return new AttributeAllocation { BrawnDelta = 1, VitalityDelta = 1 };
        }

        public void AddKnownSpell(int spellId)
        {
            if (spellId > 0 && !_knownSpellIds.Contains(spellId))
            {
                _knownSpellIds.Add(spellId);
            }
        }

        public List<int> GetUnlockedSpellCandidatesForLevel(int level)
        {
            List<int> result = new List<int>();
            string arch = (Archetype ?? string.Empty).Trim().ToLowerInvariant();
            int[] pool;
            if (arch == "mage" || arch == "sorcerer")
            {
                pool = new[] { 1108, 1104, 1102, 1111, 3202, 1306 };
            }
            else if (arch == "cleric" || arch == "priest")
            {
                pool = new[] { 2105, 2106, 2103, 2101, 2207, 2310 };
            }
            else if (arch == "paladin" || arch == "crusader")
            {
                pool = level >= 2 ? new[] { 2105, 2106, 2207, 2101 } : new[] { 2105, 2106 };
            }
            else
            {
                return result;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                if (!_knownSpellIds.Contains(pool[i]))
                {
                    result.Add(pool[i]);
                }
            }

            return result;
        }

        private void ApplySingleLevelUpGrowth()
        {
            Level++;
            int hpGain = ComputeLevelHpGain(Archetype, Vitality);
            MaxHp += hpGain;
            if (Hp > 0)
            {
                Hp = Math.Min(MaxHp, Hp + hpGain);
            }

            int spGain = ComputeLevelSpGain(Archetype, MaxSp, Knowledge, Judgment);
            if (spGain > 0)
            {
                MaxSp += spGain;
                Sp = Math.Min(MaxSp, Sp + spGain);
            }

            BaseToHit += 2;
            if (BaseMissile > 0)
            {
                BaseMissile += 2;
            }
        }

        private static int ComputeLevelHpGain(string archetype, int vitality)
        {
            string arch = (archetype ?? string.Empty).Trim().ToLowerInvariant();
            int baseHp = arch switch
            {
                "knight" or "fighter" or "paladin" or "crusader" => 6,
                "ranger" or "archer" => 5,
                "mage" or "sorcerer" or "wizard" => 3,
                _ => 4
            };
            return Math.Max(2, baseHp + Math.Max(0, (vitality - 10) / 2));
        }

        private static int ComputeLevelSpGain(string archetype, int currentMaxSp, int knowledge, int judgment)
        {
            string arch = (archetype ?? string.Empty).Trim().ToLowerInvariant();
            bool isCaster = currentMaxSp > 0 || arch is "mage" or "sorcerer" or "wizard" or "cleric" or "priest" or "paladin" or "crusader";
            if (!isCaster)
            {
                return 0;
            }

            int casterStat = arch is "mage" or "sorcerer" or "wizard" ? knowledge : Math.Max(knowledge, judgment);
            int baseSp = arch is "paladin" or "crusader" ? 2 : 3;
            return Math.Max(2, baseSp + Math.Max(0, (casterStat - 10) / 2));
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
            int ac = BaseAc + GetDodgeAcBonus();
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

            ac += GetSetAcBonus();
            ac -= ExhaustionStacks + CurseStacks;
            return ac;
        }

        public static FormationRank ResolveDefaultFormationRank(string? id, string? name, int partyIndex)
        {
            string key = (id ?? string.Empty).Trim();
            string label = (name ?? string.Empty).Trim();
            if (string.Equals(key, "hero_knight", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "hero_templar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "Aldric", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "Seraphina", StringComparison.OrdinalIgnoreCase))
            {
                return FormationRank.Front;
            }

            if (string.Equals(key, "hero_cleric", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "hero_rogue", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "Liora", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "Vex", StringComparison.OrdinalIgnoreCase))
            {
                return FormationRank.Mid;
            }

            if (string.Equals(key, "hero_archer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "hero_mage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "Kaelen", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "Theron", StringComparison.OrdinalIgnoreCase))
            {
                return FormationRank.Back;
            }

            return partyIndex < 2
                ? FormationRank.Front
                : (partyIndex < 4 ? FormationRank.Mid : FormationRank.Back);
        }

        public IReadOnlyList<EquipmentSetProgress> GetActiveEquipmentSets(bool includeZeroPieces = false)
        {
            return EquipmentSetCatalog.EvaluateHeroSets(_equipped, includeZeroPieces);
        }

        public EquipmentSetProgress? GetEquipmentSetProgress(string setId)
        {
            for (int i = 0; i < EquipmentSetCatalog.AllSets.Count; i++)
            {
                if (string.Equals(EquipmentSetCatalog.AllSets[i].SetId, setId, StringComparison.OrdinalIgnoreCase))
                {
                    return EquipmentSetCatalog.EvaluateSet(EquipmentSetCatalog.AllSets[i], _equipped);
                }
            }

            return null;
        }

        public int GetSetAcBonus()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].AcBonus;
            }
            return bonus;
        }

        public int GetSetApBonus()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].ApBonus;
            }
            return bonus;
        }

        public int GetSetFreeMoveTiles()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].FreeMoveTiles;
            }
            return bonus;
        }

        public int GetSetGuardApDiscount()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].GuardApDiscount;
            }
            return bonus;
        }

        public int GetSetCritBonusPct()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].CritBonusPct;
            }
            return bonus;
        }

        public int GetSetRangedRangeBonus()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].RangedRangeBonus;
            }
            return bonus;
        }

        public bool GetSetIgnoreHalfCover()
        {
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i].IgnoreHalfCover)
                {
                    return true;
                }
            }
            return false;
        }

        public int GetSetSpCostDiscount()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].SpCostDiscount;
            }
            return bonus;
        }

        public int GetSetSpellDamageBonus()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].SpellDamageBonus;
            }
            return bonus;
        }

        public int GetSetHealingBonus()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].HealingBonus;
            }
            return bonus;
        }

        public int GetSetSupportSpellApDiscount()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].SupportSpellApDiscount;
            }
            return bonus;
        }

        public int GetSetUndeadDamageBonusPct()
        {
            int bonus = 0;
            IReadOnlyList<EquipmentSetProgress> sets = GetActiveEquipmentSets();
            for (int i = 0; i < sets.Count; i++)
            {
                bonus += sets[i].UndeadDamageBonusPct;
            }
            return bonus;
        }

        public int GetMeleeWeaponApCost(IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            if (lookup != null && _equipped.TryGetValue(EquipmentSlot.Weapon, out InventoryItem? weapon) &&
                lookup.TryGetValue(weapon.ItemId, out ItemDefinition? item))
            {
                return CombatWeaponCosts.GetWeaponApCost(item, isRangedSlot: false);
            }

            return Rules.CombatActionEconomy.StandardWeaponApCost;
        }

        public int GetRangedWeaponApCost(IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            IReadOnlyDictionary<int, ItemDefinition>? lookup = catalog ?? _catalog;
            if (lookup != null && _equipped.TryGetValue(EquipmentSlot.MissileWeapon, out InventoryItem? missile) &&
                lookup.TryGetValue(missile.ItemId, out ItemDefinition? item))
            {
                return CombatWeaponCosts.GetWeaponApCost(item, isRangedSlot: true);
            }

            return Rules.CombatActionEconomy.StandardWeaponApCost;
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
            int toHit = (BaseMissile > 0 ? BaseMissile : BaseToHit) + GetRangedHitAgilityBonusPercent();
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
                return item.Range + GetSetRangedRangeBonus();
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

        public int GetEffectiveMovementMax(IReadOnlyDictionary<int, ItemDefinition>? catalog, bool encumbered = false)
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

            if (encumbered)
            {
                move -= 2;
            }

            return Math.Max(1, move);
        }

        public static bool IsAccessorySlot(EquipmentSlot slot)
        {
            return slot == EquipmentSlot.Accessory1 || slot == EquipmentSlot.Accessory2;
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
                case "hands":
                case "hand":
                case "glove":
                case "gloves":
                case "gauntlets":
                    slot = EquipmentSlot.Hands;
                    return true;
                case "feet":
                case "foot":
                case "boot":
                case "boots":
                case "shoes":
                case "greaves":
                    slot = EquipmentSlot.Feet;
                    return true;
                case "accessory1":
                case "accessory_1":
                case "accessory":
                case "ring":
                case "amulet":
                case "trinket":
                case "luckstone":
                    slot = EquipmentSlot.Accessory1;
                    return true;
                case "accessory2":
                case "accessory_2":
                case "cloak":
                case "cape":
                case "mantle":
                case "back":
                case "quiver":
                    slot = EquipmentSlot.Accessory2;
                    return true;
                default:
                    return false;
            }
        }
    }

    public sealed class RestResult
    {
        public RestResult(
            CampMode mode,
            int totalHpRestored,
            int totalSpRestored,
            int exhaustionCleared,
            bool torchLit,
            bool success = true,
            int turnsElapsed = 0,
            int torchChargesConsumed = 0,
            int quickBandagesRemaining = 2,
            bool ambushed = false,
            string reason = "",
            bool consumedRation = false,
            int foodRationsRemaining = 0)
        {
            Mode = mode;
            TotalHpRestored = totalHpRestored;
            TotalSpRestored = totalSpRestored;
            ExhaustionCleared = exhaustionCleared;
            TorchLit = torchLit;
            Success = success;
            TurnsElapsed = turnsElapsed;
            TorchChargesConsumed = torchChargesConsumed;
            QuickBandagesRemaining = quickBandagesRemaining;
            Ambushed = ambushed;
            Reason = reason ?? string.Empty;
            ConsumedRation = consumedRation;
            FoodRationsRemaining = Math.Max(0, foodRationsRemaining);
        }

        public CampMode Mode { get; }
        public int TotalHpRestored { get; }
        public int TotalSpRestored { get; }
        public int ExhaustionCleared { get; }
        public bool TorchLit { get; }
        public bool Success { get; }
        public int TurnsElapsed { get; }
        public int TorchChargesConsumed { get; }
        public int QuickBandagesRemaining { get; }
        public bool Ambushed { get; }
        public string Reason { get; }
        public bool ConsumedRation { get; }
        public int FoodRationsRemaining { get; }
    }

    public sealed class HeroUpgradeRecommendation
    {
        public HeroUpgradeRecommendation(
            int heroIndex,
            PartyMemberState hero,
            double candidateScore,
            double currentScore,
            double scoreDelta)
        {
            HeroIndex = heroIndex;
            Hero = hero ?? throw new ArgumentNullException(nameof(hero));
            CandidateScore = candidateScore;
            CurrentScore = currentScore;
            ScoreDelta = scoreDelta;
        }

        public int HeroIndex { get; }
        public PartyMemberState Hero { get; }
        public double CandidateScore { get; }
        public double CurrentScore { get; }
        public double ScoreDelta { get; }
    }

    public sealed class PartyState
    {
        public const int MaxQuickBandageCharges = 2;
        public const int TorchItemId = 805;
        public const int RationItemId = 807;
        public const int DefaultFoodRations = 6;
        public const int DefaultTorchDurationMinutes = 120;
        public const double WindedFatigueThreshold = 70.0;
        public const double ExhaustedFatigueThreshold = 100.0;
        public const int ConjureLightSpCost = 4;
        public const int CurePartySpCost = 8;
        public const int CampSentrySpCost = 6;
        public const int RefreshPartySpCost = 6;
        public const double ConjureLightDurationMinutes = 60.0;

        private readonly List<PartyMemberState> _members;
        private readonly List<InventoryItem> _stash;

        public PartyState(
            IEnumerable<PartyMemberState> members,
            IEnumerable<InventoryItem>? stash = null,
            int gold = 0,
            bool hasBoat = false,
            int torchTurnsRemaining = 0,
            int quickBandageCharges = MaxQuickBandageCharges,
            bool deferLevelUp = false,
            int foodRations = DefaultFoodRations)
        {
            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            _members = new List<PartyMemberState>(members);
            for (int i = 0; i < _members.Count; i++)
            {
                _members[i].FormationRank = PartyMemberState.ResolveDefaultFormationRank(_members[i].Id, _members[i].Name, i);
            }
            _stash = stash != null ? new List<InventoryItem>(stash) : new List<InventoryItem>();
            Gold = Math.Max(0, gold);
            HasBoat = hasBoat;
            TorchTurnsRemaining = Math.Max(0, torchTurnsRemaining);
            QuickBandageCharges = Math.Clamp(quickBandageCharges, 0, MaxQuickBandageCharges);
            DeferLevelUp = deferLevelUp;
            FoodRations = Math.Max(0, foodRations);
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

            PartyState party = new PartyState(
                members,
                gold: bundle.InitialGold,
                quickBandageCharges: MaxQuickBandageCharges,
                deferLevelUp: true);
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
        public double TorchMinutesRemaining { get; set; }
        public int TorchTurnsRemaining
        {
            get => (int)Math.Ceiling(Math.Max(0.0, TorchMinutesRemaining));
            set => TorchMinutesRemaining = Math.Max(0, value);
        }

        public bool BurnTorchMinutes(double minutes)
        {
            if (minutes <= 0.0 || TorchMinutesRemaining <= 0.0)
            {
                return false;
            }

            TorchMinutesRemaining = Math.Max(0.0, TorchMinutesRemaining - minutes);
            return TorchMinutesRemaining <= 0.0;
        }

        public static string FormatTorchDuration(double minutes)
        {
            int totalMins = Math.Max(0, (int)Math.Ceiling(minutes));
            if (totalMins <= 0)
            {
                return "Out";
            }

            int hours = totalMins / 60;
            int mins = totalMins % 60;
            return hours > 0 ? $"{hours}h {mins:00}m" : $"{mins}m";
        }
        public int QuickBandageCharges { get; set; }
        public bool DeferLevelUp { get; set; }
        public int FoodRations { get; set; }
        public double PartyFatigue { get; set; }
        public int PartyFatiguePercent => (int)Math.Round(Math.Clamp(PartyFatigue, 0.0, ExhaustedFatigueThreshold));
        public PartyFatigueState FatigueState =>
            PartyFatigue >= ExhaustedFatigueThreshold
                ? PartyFatigueState.Exhausted
                : (PartyFatigue >= WindedFatigueThreshold ? PartyFatigueState.Winded : PartyFatigueState.Fresh);
        public bool CampSentryActive { get; set; }

        public bool AddPartyFatigue(double amount)
        {
            if (amount <= 0.0)
            {
                return false;
            }

            bool wasExhausted = PartyFatigue >= ExhaustedFatigueThreshold;
            PartyFatigue = Math.Clamp(PartyFatigue + amount, 0.0, ExhaustedFatigueThreshold);
            if (!wasExhausted && PartyFatigue >= ExhaustedFatigueThreshold)
            {
                for (int i = 0; i < _members.Count; i++)
                {
                    if (_members[i].IsAlive)
                    {
                        _members[i].ExhaustionStacks = Math.Min(3, _members[i].ExhaustionStacks + 1);
                    }
                }
                return true;
            }

            return false;
        }

        public double ReducePartyFatigue(double amount)
        {
            if (amount <= 0.0 || PartyFatigue <= 0.0)
            {
                return 0.0;
            }

            double before = Math.Clamp(PartyFatigue, 0.0, ExhaustedFatigueThreshold);
            PartyFatigue = Math.Max(0.0, before - amount);
            return before - PartyFatigue;
        }

        public void ClearPartyFatigue()
        {
            PartyFatigue = 0.0;
        }

        public OutOfCombatSpellResult CastOutOfCombatUtility(
            OutOfCombatSpellKind kind,
            int targetHeroIndex,
            IReadOnlyDictionary<int, SpellDefinition>? spells = null,
            IDiceRoller? roller = null)
        {
            IDiceRoller rng = roller ?? new DeterministicDiceRoller(0x43414D504D47UL);
            int preferredIdx = Math.Clamp(targetHeroIndex, 0, Math.Max(0, _members.Count - 1));

            switch (kind)
            {
                case OutOfCombatSpellKind.HealParty:
                {
                    int targetIdx = -1;
                    if (_members.Count > 0 && preferredIdx < _members.Count && _members[preferredIdx].IsAlive && _members[preferredIdx].Hp < _members[preferredIdx].MaxHp)
                    {
                        targetIdx = preferredIdx;
                    }
                    else
                    {
                        double lowestFrac = 1.0;
                        for (int i = 0; i < _members.Count; i++)
                        {
                            PartyMemberState m = _members[i];
                            if (m.IsAlive && m.Hp < m.MaxHp)
                            {
                                double frac = m.MaxHp > 0 ? m.Hp / (double)m.MaxHp : 1.0;
                                if (targetIdx < 0 || frac < lowestFrac)
                                {
                                    lowestFrac = frac;
                                    targetIdx = i;
                                }
                            }
                        }
                    }

                    if (targetIdx < 0)
                    {
                        return new OutOfCombatSpellResult(false, "All living heroes are already at full HP.");
                    }

                    SpellDefinition? healSpell = null;
                    int casterIdx = FindOutOfCombatHealerIndex(preferredIdx, spells, out healSpell);
                    if (casterIdx < 0)
                    {
                        return new OutOfCombatSpellResult(false, "No conscious healer has enough SP to cast a healing spell.");
                    }

                    PartyMemberState caster = _members[casterIdx];
                    int spCost = healSpell != null ? Math.Max(1, healSpell.SpCost - caster.GetSetSpCostDiscount()) : 10;
                    if (caster.Sp < spCost)
                    {
                        return new OutOfCombatSpellResult(false, $"{caster.Name} needs {spCost} SP to heal.");
                    }

                    string diceText = healSpell != null && !string.IsNullOrWhiteSpace(healSpell.EffectDice) ? healSpell.EffectDice : "1d8+2";
                    DiceSpec healSpec = DiceSpec.TryParse(diceText, out DiceSpec parsedSpec) ? parsedSpec : new DiceSpec(1, 8, 2);
                    int divineBonus = Math.Max(0, (caster.Judgment - 10) / 2) + caster.GetSetHealingBonus();
                    int roll = Math.Max(1, healSpec.Roll(rng) + divineBonus);

                    caster.Sp -= spCost;
                    PartyMemberState target = _members[targetIdx];
                    int before = target.Hp;
                    target.Hp = Math.Min(target.MaxHp, target.Hp + roll);
                    int gained = Math.Max(0, target.Hp - before);
                    string spellLabel = healSpell != null ? healSpell.Name : "Heal Wounds";
                    return new OutOfCombatSpellResult(
                        true,
                        $"{caster.Name} casts {spellLabel} on {target.Name} (+{gained} HP, {spCost} SP).",
                        casterName: caster.Name,
                        spSpent: spCost,
                        totalHealing: gained);
                }

                case OutOfCombatSpellKind.CureParty:
                {
                    int targetIdx = -1;
                    if (preferredIdx >= 0 && preferredIdx < _members.Count && HasFieldCurableAilment(_members[preferredIdx]))
                    {
                        targetIdx = preferredIdx;
                    }
                    else
                    {
                        for (int i = 0; i < _members.Count; i++)
                        {
                            if (HasFieldCurableAilment(_members[i]))
                            {
                                targetIdx = i;
                                break;
                            }
                        }
                    }

                    if (targetIdx < 0)
                    {
                        return new OutOfCombatSpellResult(false, "No living hero has Poison, Disease, Curse, or Exhaustion to purify.");
                    }

                    int casterIdx = FindOutOfCombatCasterIndex(preferredIdx, CurePartySpCost, divinePreferred: true);
                    if (casterIdx < 0)
                    {
                        return new OutOfCombatSpellResult(false, $"Requires a conscious healer with {CurePartySpCost} SP.");
                    }

                    PartyMemberState caster = _members[casterIdx];
                    PartyMemberState target = _members[targetIdx];
                    caster.Sp -= CurePartySpCost;
                    int cured = PurgeFieldAilments(target);
                    return new OutOfCombatSpellResult(
                        true,
                        $"{caster.Name} purifies {target.Name} (-{CurePartySpCost} SP; cleared {cured} affliction(s)).",
                        casterName: caster.Name,
                        spSpent: CurePartySpCost,
                        conditionsCured: cured);
                }

                case OutOfCombatSpellKind.ConjureLight:
                {
                    int casterIdx = FindOutOfCombatCasterIndex(preferredIdx, ConjureLightSpCost, divinePreferred: false);
                    if (casterIdx < 0)
                    {
                        return new OutOfCombatSpellResult(false, $"Requires a conscious spellcaster with {ConjureLightSpCost} SP.");
                    }

                    PartyMemberState caster = _members[casterIdx];
                    caster.Sp -= ConjureLightSpCost;
                    TorchMinutesRemaining += ConjureLightDurationMinutes;
                    return new OutOfCombatSpellResult(
                        true,
                        $"{caster.Name} casts Conjure Light (+1h 00m torchlight; {FormatTorchDuration(TorchMinutesRemaining)} active, {ConjureLightSpCost} SP).",
                        casterName: caster.Name,
                        spSpent: ConjureLightSpCost,
                        torchLit: true);
                }

                case OutOfCombatSpellKind.CampSentry:
                {
                    if (CampSentryActive)
                    {
                        return new OutOfCombatSpellResult(false, "Camp Sentry ward is already active for your next Full Rest.");
                    }

                    int casterIdx = FindOutOfCombatCasterIndex(preferredIdx, CampSentrySpCost, divinePreferred: false);
                    if (casterIdx < 0)
                    {
                        return new OutOfCombatSpellResult(false, $"Requires a conscious spellcaster with {CampSentrySpCost} SP.");
                    }

                    PartyMemberState caster = _members[casterIdx];
                    caster.Sp -= CampSentrySpCost;
                    CampSentryActive = true;
                    return new OutOfCombatSpellResult(
                        true,
                        $"{caster.Name} sets a Camp Sentry ward (next Full Rest ambush-safe, {CampSentrySpCost} SP).",
                        casterName: caster.Name,
                        spSpent: CampSentrySpCost,
                        sentryActivated: true);
                }

                case OutOfCombatSpellKind.RefreshParty:
                {
                    bool anyExhausted = false;
                    for (int i = 0; i < _members.Count; i++)
                    {
                        if (_members[i].IsAlive && _members[i].ExhaustionStacks > 0)
                        {
                            anyExhausted = true;
                            break;
                        }
                    }

                    if (PartyFatigue <= 0.01 && !anyExhausted)
                    {
                        return new OutOfCombatSpellResult(false, "Party is already well-rested.");
                    }

                    int casterIdx = FindOutOfCombatCasterIndex(preferredIdx, RefreshPartySpCost, divinePreferred: true);
                    if (casterIdx < 0)
                    {
                        return new OutOfCombatSpellResult(false, $"Requires a conscious caster with {RefreshPartySpCost} SP.");
                    }

                    PartyMemberState caster = _members[casterIdx];
                    caster.Sp -= RefreshPartySpCost;
                    double reduced = ReducePartyFatigue(40.0);
                    int clearedStacks = 0;
                    if (PartyFatigue < WindedFatigueThreshold)
                    {
                        for (int i = 0; i < _members.Count; i++)
                        {
                            if (_members[i].IsAlive && _members[i].ExhaustionStacks > 0)
                            {
                                _members[i].ExhaustionStacks--;
                                clearedStacks++;
                            }
                        }
                    }

                    string exNote = clearedStacks > 0 ? $" & eased {clearedStacks} exhaustion" : string.Empty;
                    return new OutOfCombatSpellResult(
                        true,
                        $"{caster.Name} casts Invigorate (-{(int)Math.Round(reduced)}% fatigue{exNote}; now {PartyFatiguePercent}%, {RefreshPartySpCost} SP).",
                        casterName: caster.Name,
                        spSpent: RefreshPartySpCost,
                        fatigueReduced: reduced);
                }

                default:
                    return new OutOfCombatSpellResult(false, "Unsupported field ritual.");
            }
        }

        public OutOfCombatSpellResult CastOutOfCombatKnownSpell(
            int casterIndex,
            int spellId,
            int targetHeroIndex,
            IReadOnlyDictionary<int, SpellDefinition> spells,
            IDiceRoller? roller = null)
        {
            if (spells == null || !spells.TryGetValue(spellId, out SpellDefinition? spell))
            {
                return new OutOfCombatSpellResult(false, "Spell definition not found.");
            }

            if (casterIndex < 0 || casterIndex >= _members.Count)
            {
                return new OutOfCombatSpellResult(false, "Caster not found.");
            }

            PartyMemberState caster = _members[casterIndex];
            if (!caster.IsAlive)
            {
                return new OutOfCombatSpellResult(false, $"{caster.Name} is unconscious.");
            }

            bool knowsSpell = false;
            for (int i = 0; i < caster.KnownSpellIds.Count; i++)
            {
                if (caster.KnownSpellIds[i] == spellId)
                {
                    knowsSpell = true;
                    break;
                }
            }
            if (!knowsSpell)
            {
                return new OutOfCombatSpellResult(false, $"{caster.Name} does not know {spell.Name}.");
            }

            int spCost = spell.SpCost > 0 ? Math.Max(1, spell.SpCost - caster.GetSetSpCostDiscount()) : 0;
            if (caster.Sp < spCost)
            {
                return new OutOfCombatSpellResult(false, $"{caster.Name} needs {spCost} SP for {spell.Name}.");
            }

            IDiceRoller rng = roller ?? new DeterministicDiceRoller(0x4649454C44UL);
            string kind = spell.EffectKind ?? string.Empty;
            int targetIdx = Math.Clamp(targetHeroIndex, 0, _members.Count - 1);
            PartyMemberState target = _members[targetIdx];

            if (string.Equals(kind, "Heal", StringComparison.OrdinalIgnoreCase))
            {
                if (!target.IsAlive || target.Hp >= target.MaxHp)
                {
                    int fallbackIdx = -1;
                    for (int i = 0; i < _members.Count; i++)
                    {
                        if (_members[i].IsAlive && _members[i].Hp < _members[i].MaxHp)
                        {
                            fallbackIdx = i;
                            break;
                        }
                    }
                    if (fallbackIdx < 0)
                    {
                        return new OutOfCombatSpellResult(false, "No injured living hero needs healing.");
                    }
                    target = _members[fallbackIdx];
                }

                DiceSpec healSpec = !string.IsNullOrWhiteSpace(spell.EffectDice) && DiceSpec.TryParse(spell.EffectDice, out DiceSpec pSpec)
                    ? pSpec
                    : new DiceSpec(1, 8, 2);
                int divineBonus = Math.Max(0, (caster.Judgment - 10) / 2) + caster.GetSetHealingBonus();
                int heal = Math.Max(1, healSpec.Roll(rng) + divineBonus);
                caster.Sp -= spCost;
                int before = target.Hp;
                target.Hp = Math.Min(target.MaxHp, target.Hp + heal);
                int gained = Math.Max(0, target.Hp - before);
                return new OutOfCombatSpellResult(
                    true,
                    $"{caster.Name} casts {spell.Name} on {target.Name} (+{gained} HP, {spCost} SP).",
                    casterName: caster.Name,
                    spSpent: spCost,
                    totalHealing: gained);
            }

            if (string.Equals(kind, "Cure", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasFieldCurableAilment(target))
                {
                    return new OutOfCombatSpellResult(false, $"{target.Name} has no ailments to cure.");
                }

                caster.Sp -= spCost;
                int cured = PurgeFieldAilments(target);
                return new OutOfCombatSpellResult(
                    true,
                    $"{caster.Name} casts {spell.Name} on {target.Name} (cured {cured}, {spCost} SP).",
                    casterName: caster.Name,
                    spSpent: spCost,
                    conditionsCured: cured);
            }

            if (string.Equals(kind, "Buff", StringComparison.OrdinalIgnoreCase) && spell.AppliedCondition.HasValue)
            {
                int duration = spell.ConditionDuration > 0 ? spell.ConditionDuration : 5;
                caster.Sp -= spCost;
                if (string.Equals(spell.TargetMode, "AllAllies", StringComparison.OrdinalIgnoreCase))
                {
                    int buffedCount = 0;
                    for (int i = 0; i < _members.Count; i++)
                    {
                        if (_members[i].IsAlive)
                        {
                            _members[i].Conditions.Apply(
                                spell.AppliedCondition.Value,
                                magnitude: 1,
                                turnsRemaining: duration,
                                baseRecoveryChancePercent: 0,
                                canRecoverBySave: false);
                            buffedCount++;
                        }
                    }
                    return new OutOfCombatSpellResult(
                        true,
                        $"{caster.Name} casts {spell.Name} on the party ({buffedCount} heroes buffed for next battle, {spCost} SP).",
                        casterName: caster.Name,
                        spSpent: spCost);
                }

                PartyMemberState buffTarget = string.Equals(spell.TargetMode, "Self", StringComparison.OrdinalIgnoreCase) || !target.IsAlive
                    ? caster
                    : target;
                buffTarget.Conditions.Apply(
                    spell.AppliedCondition.Value,
                    magnitude: 1,
                    turnsRemaining: duration,
                    baseRecoveryChancePercent: 0,
                    canRecoverBySave: false);
                return new OutOfCombatSpellResult(
                    true,
                    $"{caster.Name} casts {spell.Name} on {buffTarget.Name} (ready for next battle, {spCost} SP).",
                    casterName: caster.Name,
                    spSpent: spCost);
            }

            return new OutOfCombatSpellResult(false, $"{spell.Name} can only be cast during tactical combat.");
        }

        private int FindOutOfCombatHealerIndex(
            int preferredIdx,
            IReadOnlyDictionary<int, SpellDefinition>? spells,
            out SpellDefinition? bestHealSpell)
        {
            bestHealSpell = null;
            int bestIdx = -1;
            int bestScore = int.MinValue;

            for (int i = 0; i < _members.Count; i++)
            {
                PartyMemberState m = _members[i];
                if (!m.IsAlive || m.Sp <= 0)
                {
                    continue;
                }

                SpellDefinition? cheapestHeal = null;
                if (spells != null)
                {
                    for (int ks = 0; ks < m.KnownSpellIds.Count; ks++)
                    {
                        if (spells.TryGetValue(m.KnownSpellIds[ks], out SpellDefinition? candidate) &&
                            string.Equals(candidate.EffectKind, "Heal", StringComparison.OrdinalIgnoreCase))
                        {
                            int cost = Math.Max(1, candidate.SpCost - m.GetSetSpCostDiscount());
                            if (m.Sp >= cost && (cheapestHeal == null || candidate.SpCost < cheapestHeal.SpCost))
                            {
                                cheapestHeal = candidate;
                            }
                        }
                    }
                }

                bool isDivineClass = string.Equals(m.Archetype, "Cleric", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(m.Archetype, "Paladin", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(m.Archetype, "Priest", StringComparison.OrdinalIgnoreCase);
                if (cheapestHeal == null && !(isDivineClass && m.Sp >= 10))
                {
                    continue;
                }

                int score = m.Sp + (i == preferredIdx ? 1000 : 0) + (string.Equals(m.Archetype, "Cleric", StringComparison.OrdinalIgnoreCase) ? 100 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                    bestHealSpell = cheapestHeal;
                }
            }

            return bestIdx;
        }

        private int FindOutOfCombatCasterIndex(int preferredIdx, int spCost, bool divinePreferred)
        {
            int bestIdx = -1;
            int bestScore = int.MinValue;
            for (int i = 0; i < _members.Count; i++)
            {
                PartyMemberState m = _members[i];
                if (!m.IsAlive || m.MaxSp <= 0 || m.Sp < spCost)
                {
                    continue;
                }

                bool isDivine = string.Equals(m.Archetype, "Cleric", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(m.Archetype, "Paladin", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(m.Archetype, "Priest", StringComparison.OrdinalIgnoreCase);
                int score = m.Sp + (i == preferredIdx ? 1000 : 0) + (divinePreferred && isDivine ? 150 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private static bool HasFieldCurableAilment(PartyMemberState m)
        {
            return m.IsAlive && (
                m.ExhaustionStacks > 0 ||
                m.CurseStacks > 0 ||
                m.Conditions.Has(CharConditionId.Poisoned) ||
                m.Conditions.Has(CharConditionId.Diseased) ||
                m.Conditions.Has(CharConditionId.Cursed));
        }

        private static int PurgeFieldAilments(PartyMemberState m)
        {
            int count = 0;
            if (m.Conditions.Remove(CharConditionId.Poisoned)) count++;
            if (m.Conditions.Remove(CharConditionId.Diseased)) count++;
            if (m.Conditions.Remove(CharConditionId.Cursed)) count++;
            if (m.CurseStacks > 0)
            {
                count += m.CurseStacks;
                m.CurseStacks = 0;
            }
            if (m.ExhaustionStacks > 0)
            {
                m.ExhaustionStacks--;
                count++;
            }
            return count;
        }

        public bool TryConsumeFoodRation()
        {
            if (FoodRations <= 0)
            {
                return false;
            }

            FoodRations--;
            return true;
        }

        public void AddFoodRations(int count)
        {
            if (count > 0)
            {
                FoodRations += count;
            }
        }

        public FormationRank GetFormationRank(int memberIndex)
        {
            if (memberIndex < 0 || memberIndex >= _members.Count)
            {
                return FormationRank.Mid;
            }

            return _members[memberIndex].FormationRank;
        }

        public bool SetFormationRank(int memberIndex, FormationRank rank)
        {
            if (memberIndex < 0 || memberIndex >= _members.Count)
            {
                return false;
            }

            _members[memberIndex].FormationRank = rank;
            return true;
        }

        public bool CycleFormationRank(int memberIndex, int delta)
        {
            if (memberIndex < 0 || memberIndex >= _members.Count)
            {
                return false;
            }

            int next = Math.Clamp((int)_members[memberIndex].FormationRank + delta, (int)FormationRank.Front, (int)FormationRank.Back);
            _members[memberIndex].FormationRank = (FormationRank)next;
            return true;
        }

        public bool MoveMemberInFormationOrder(int memberIndex, int delta)
        {
            if (memberIndex < 0 || memberIndex >= _members.Count || delta == 0)
            {
                return false;
            }

            int targetIndex = memberIndex + Math.Sign(delta);
            if (targetIndex < 0 || targetIndex >= _members.Count)
            {
                return false;
            }

            PartyMemberState moving = _members[memberIndex];
            _members[memberIndex] = _members[targetIndex];
            _members[targetIndex] = moving;
            return true;
        }

        public IReadOnlyList<int> GetOrderedFormationMemberIndices()
        {
            List<int> ordered = new List<int>(_members.Count);
            for (int rank = (int)FormationRank.Front; rank <= (int)FormationRank.Back; rank++)
            {
                for (int i = 0; i < _members.Count; i++)
                {
                    if ((int)_members[i].FormationRank == rank)
                    {
                        ordered.Add(i);
                    }
                }
            }
            return ordered;
        }

        public bool TryLightTorchFromStash(out string feedbackMessage, IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            if (!TryConsumeItemCharge(TorchItemId, out _, catalog))
            {
                feedbackMessage = "No Torches remaining in shared stash.";
                return false;
            }

            TorchMinutesRemaining += DefaultTorchDurationMinutes;
            feedbackMessage = $"Lit a Torch (+{FormatTorchDuration(DefaultTorchDurationMinutes)}; {FormatTorchDuration(TorchMinutesRemaining)} active).";
            return true;
        }

        public int GetTotalTorchCharges()
        {
            int total = 0;
            for (int i = 0; i < _stash.Count; i++)
            {
                InventoryItem item = _stash[i];
                if (item.ItemId == TorchItemId && item.Quantity > 0)
                {
                    int perUnit = item.RemainingCharges > 0 ? item.RemainingCharges : 1;
                    total += perUnit * item.Quantity;
                }
            }
            return total;
        }

        public int GetHeroCarryCapacity(int heroIndex)
        {
            if (heroIndex < 0 || heroIndex >= _members.Count)
            {
                return 0;
            }
            return _members[heroIndex].GetCarryCapacityContribution();
        }

        public int GetPartyCarryCapacity()
        {
            int total = 0;
            for (int i = 0; i < _members.Count; i++)
            {
                total += _members[i].GetCarryCapacityContribution();
            }
            return Math.Max(120, total);
        }

        public int GetTotalCarriedWeight(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            if (catalog == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < _members.Count; i++)
            {
                foreach (InventoryItem eq in _members[i].Equipped.Values)
                {
                    if (catalog.TryGetValue(eq.ItemId, out ItemDefinition? def))
                    {
                        total += Math.Max(0, def.Weight);
                    }
                }
            }

            for (int i = 0; i < _stash.Count; i++)
            {
                InventoryItem entry = _stash[i];
                if (catalog.TryGetValue(entry.ItemId, out ItemDefinition? def))
                {
                    total += Math.Max(0, def.Weight) * Math.Max(1, entry.Quantity);
                }
            }

            return total;
        }

        public bool IsEncumbered(IReadOnlyDictionary<int, ItemDefinition>? catalog)
        {
            return catalog != null && GetTotalCarriedWeight(catalog) > GetPartyCarryCapacity();
        }

        public double GetAverageLuck()
        {
            if (_members.Count == 0)
            {
                return 10.0;
            }
            int sum = 0;
            for (int i = 0; i < _members.Count; i++)
            {
                sum += _members[i].Luck;
            }
            return (double)sum / _members.Count;
        }

        public int GetGoldLootBonusPercent()
        {
            return Math.Max(0, (int)Math.Round(GetAverageLuck() - 10.0, MidpointRounding.AwayFromZero));
        }

        public void AddItemToStash(int itemId, int quantity = 1, bool isIdentified = true, int charges = 0)
        {
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }

            int normalizedCharges = Math.Max(0, charges);
            for (int i = 0; i < _stash.Count; i++)
            {
                InventoryItem existing = _stash[i];
                int existingCharges = Math.Max(0, existing.RemainingCharges);
                if (existing.ItemId == itemId &&
                    existing.IsIdentified == isIdentified &&
                    existingCharges == normalizedCharges)
                {
                    existing.Quantity += quantity;
                    return;
                }
            }

            _stash.Add(new InventoryItem(itemId, quantity, isIdentified, normalizedCharges));
        }

        public bool TryConsumeItemCharge(int itemId, out string itemName, IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            itemName = string.Empty;
            int bestIdx = -1;
            int lowestPosCharges = int.MaxValue;
            for (int i = 0; i < _stash.Count; i++)
            {
                InventoryItem item = _stash[i];
                if (item.ItemId != itemId || item.Quantity <= 0)
                {
                    continue;
                }
                int chg = Math.Max(0, item.RemainingCharges);
                if (chg > 0 && chg < lowestPosCharges)
                {
                    lowestPosCharges = chg;
                    bestIdx = i;
                }
                else if (bestIdx < 0)
                {
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
            {
                return false;
            }

            if (catalog != null && catalog.TryGetValue(itemId, out ItemDefinition? def))
            {
                itemName = def.Name;
            }

            InventoryItem target = _stash[bestIdx];
            int perUnitCharges = target.RemainingCharges;
            if (perUnitCharges <= 0)
            {
                return RemoveItemFromStash(bestIdx, 1);
            }

            int after = perUnitCharges - 1;
            bool isId = target.IsIdentified;
            if (!RemoveItemFromStash(bestIdx, 1))
            {
                return false;
            }

            if (after > 0)
            {
                AddItemToStash(itemId, 1, isId, after);
            }

            return true;
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
            return EquipFromStash(heroIndex, stashIndex, catalog, preferredSlot: null);
        }

        public bool EquipFromStash(
            int heroIndex,
            int stashIndex,
            IReadOnlyDictionary<int, ItemDefinition> catalog,
            EquipmentSlot? preferredSlot)
        {
            return EquipFromStash(heroIndex, stashIndex, catalog, preferredSlot, out _);
        }

        public bool EquipFromStash(
            int heroIndex,
            int stashIndex,
            IReadOnlyDictionary<int, ItemDefinition> catalog,
            EquipmentSlot? preferredSlot,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (heroIndex < 0 || heroIndex >= _members.Count || stashIndex < 0 || stashIndex >= _stash.Count)
            {
                failureReason = "Invalid hero or stash slot.";
                return false;
            }

            InventoryItem stashEntry = _stash[stashIndex];
            if (!catalog.TryGetValue(stashEntry.ItemId, out ItemDefinition? def) ||
                !PartyMemberState.TryParseSlot(def.Slot, out EquipmentSlot slot))
            {
                failureReason = "Item cannot be equipped.";
                return false;
            }

            PartyMemberState hero = _members[heroIndex];
            if (!CanHeroEquip(hero, def, out failureReason))
            {
                return false;
            }

            if (preferredSlot.HasValue)
            {
                if (PartyMemberState.IsAccessorySlot(slot) && PartyMemberState.IsAccessorySlot(preferredSlot.Value))
                {
                    slot = preferredSlot.Value;
                }
                else if (preferredSlot.Value != slot)
                {
                    failureReason = $"{def.Name} does not fit the {preferredSlot.Value} slot.";
                    return false;
                }
            }
            else if (PartyMemberState.IsAccessorySlot(slot) && hero.Equipped.ContainsKey(slot))
            {
                EquipmentSlot sibling = slot == EquipmentSlot.Accessory1
                    ? EquipmentSlot.Accessory2
                    : EquipmentSlot.Accessory1;
                if (!hero.Equipped.ContainsKey(sibling))
                {
                    slot = sibling;
                }
            }

            // Extract single item from stash.
            InventoryItem toEquip = new InventoryItem(stashEntry.ItemId, 1, stashEntry.IsIdentified, stashEntry.RemainingCharges);
            if (!RemoveItemFromStash(stashIndex, 1))
            {
                failureReason = "Item missing from stash.";
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

        private enum HeroClassProfile
        {
            Knight,
            Paladin,
            Cleric,
            Ranger,
            Mage,
            Rogue
        }

        private static HeroClassProfile ResolveClassProfile(string? archetype)
        {
            string arch = (archetype ?? string.Empty).Trim().ToLowerInvariant();
            if (arch.Contains("paladin") || arch.Contains("templar")) return HeroClassProfile.Paladin;
            if (arch.Contains("cleric") || arch.Contains("priest") || arch.Contains("healer") || arch.Contains("monk")) return HeroClassProfile.Cleric;
            if (arch.Contains("ranger") || arch.Contains("archer") || arch.Contains("hunter") || arch.Contains("woods")) return HeroClassProfile.Ranger;
            if (arch.Contains("mage") || arch.Contains("wizard") || arch.Contains("sorcer") || arch.Contains("enchant") || arch.Contains("warlock")) return HeroClassProfile.Mage;
            if (arch.Contains("rogue") || arch.Contains("thief") || arch.Contains("assassin") || arch.Contains("burglar")) return HeroClassProfile.Rogue;
            return HeroClassProfile.Knight;
        }

        public static bool CanHeroEquip(PartyMemberState? hero, ItemDefinition? item)
        {
            return CanHeroEquip(hero, item, out _);
        }

        public static bool CanHeroEquip(PartyMemberState? hero, ItemDefinition? item, out string reason)
        {
            if (hero == null || item == null)
            {
                reason = "Missing hero or item definition.";
                return false;
            }

            if (!PartyMemberState.TryParseSlot(item.Slot, out EquipmentSlot slot))
            {
                reason = $"{item.Name} is not equippable gear.";
                return false;
            }

            HeroClassProfile profile = ResolveClassProfile(hero.Archetype);
            string archLabel = string.IsNullOrWhiteSpace(hero.Archetype) ? profile.ToString() : hero.Archetype;
            string name = (item.Name ?? string.Empty).ToLowerInvariant();

            switch (slot)
            {
                case EquipmentSlot.Weapon:
                {
                    bool isStaff = name.Contains("staff");
                    bool isDagger = name.Contains("dagger");
                    bool isBlunt = !isStaff && (
                        name.Contains("mace") || name.Contains("flail") || name.Contains("morning star") ||
                        name.Contains("hammer") || name.Contains("club") || name.Contains("cudgel") ||
                        name.Contains("nunchuka"));
                    bool isLightBlade = name.Contains("short sword") || name.Contains("shortsword") ||
                        name.Contains("scimitar") || name.Contains("rapier") || name.Contains("dirk");

                    if (isStaff)
                    {
                        if (profile == HeroClassProfile.Mage || profile == HeroClassProfile.Cleric)
                        {
                            reason = string.Empty;
                            return true;
                        }
                        reason = $"Class restricted: {archLabel} cannot equip staves (Mage/Cleric only).";
                        return false;
                    }

                    if (isBlunt)
                    {
                        if (profile == HeroClassProfile.Knight || profile == HeroClassProfile.Paladin || profile == HeroClassProfile.Cleric)
                        {
                            reason = string.Empty;
                            return true;
                        }
                        reason = $"Class restricted: {archLabel} cannot wield heavy blunt weapons.";
                        return false;
                    }

                    if (isDagger)
                    {
                        if (profile == HeroClassProfile.Cleric)
                        {
                            reason = "Class restricted: Clerics are restricted to blunt weapons (no edged blades).";
                            return false;
                        }
                        reason = string.Empty;
                        return true;
                    }

                    if (isLightBlade)
                    {
                        if (profile == HeroClassProfile.Cleric)
                        {
                            reason = "Class restricted: Clerics cannot wield edged swords.";
                            return false;
                        }
                        if (profile == HeroClassProfile.Mage)
                        {
                            reason = "Class restricted: Mages may only wield staves and daggers.";
                            return false;
                        }
                        reason = string.Empty;
                        return true;
                    }

                    // Martial swords, axes, spears, pike-axes
                    if (profile == HeroClassProfile.Knight || profile == HeroClassProfile.Paladin)
                    {
                        reason = string.Empty;
                        return true;
                    }
                    if (profile == HeroClassProfile.Cleric)
                    {
                        reason = "Class restricted: Clerics are restricted to blunt weapons and staves.";
                        return false;
                    }
                    if (profile == HeroClassProfile.Mage)
                    {
                        reason = "Class restricted: Mages may only wield staves and daggers.";
                        return false;
                    }
                    reason = $"Class restricted: {archLabel} cannot wield heavy martial weapons.";
                    return false;
                }

                case EquipmentSlot.MissileWeapon:
                {
                    if (profile == HeroClassProfile.Cleric)
                    {
                        reason = "Class restricted: Clerics cannot wield bows or missile weapons.";
                        return false;
                    }
                    if (profile == HeroClassProfile.Mage)
                    {
                        reason = "Class restricted: Mages cannot equip missile weapons.";
                        return false;
                    }
                    bool isBow = name.Contains("bow");
                    if (isBow && profile == HeroClassProfile.Rogue)
                    {
                        reason = "Class restricted: Bows require Ranger or martial archery proficiency (Rogues use darts and thrown blades).";
                        return false;
                    }
                    reason = string.Empty;
                    return true;
                }

                case EquipmentSlot.Shield:
                    if (profile == HeroClassProfile.Knight || profile == HeroClassProfile.Paladin || profile == HeroClassProfile.Cleric)
                    {
                        reason = string.Empty;
                        return true;
                    }
                    reason = $"Class restricted: {archLabel} cannot equip shields.";
                    return false;

                case EquipmentSlot.Armor:
                {
                    bool isHeavyPlate = name.Contains("banded") || name.Contains("plate");
                    bool isMediumChain = name.Contains("chain") || name.Contains("mail") || name.Contains("scale");
                    bool isLeather = name.Contains("leather") || name.Contains("hide") || name.Contains("studded");

                    if (profile == HeroClassProfile.Knight || profile == HeroClassProfile.Paladin)
                    {
                        reason = string.Empty;
                        return true;
                    }
                    if (profile == HeroClassProfile.Cleric)
                    {
                        if (isHeavyPlate)
                        {
                            reason = "Class restricted: Clerics are limited to light and medium armor (no heavy plate).";
                            return false;
                        }
                        reason = string.Empty;
                        return true;
                    }
                    if (profile == HeroClassProfile.Ranger || profile == HeroClassProfile.Rogue)
                    {
                        if (isHeavyPlate || isMediumChain)
                        {
                            reason = $"Class restricted: {archLabel}s cannot wear medium chain or heavy armor.";
                            return false;
                        }
                        reason = string.Empty;
                        return true;
                    }
                    // Mage
                    if (isHeavyPlate || isMediumChain || isLeather)
                    {
                        reason = "Class restricted: Mages may only wear robes (no leather or metal armor).";
                        return false;
                    }
                    reason = string.Empty;
                    return true;
                }

                case EquipmentSlot.Helm:
                {
                    if (profile == HeroClassProfile.Mage)
                    {
                        reason = "Class restricted: Mages cannot wear helms or headgear.";
                        return false;
                    }
                    bool isLightCap = name.Contains("cap") || name.Contains("hood") || name.Contains("circlet") || item.ArmorClassBonus <= 2;
                    if ((profile == HeroClassProfile.Ranger || profile == HeroClassProfile.Rogue) && !isLightCap)
                    {
                        reason = $"Class restricted: {archLabel}s cannot wear heavy metal helms.";
                        return false;
                    }
                    reason = string.Empty;
                    return true;
                }

                case EquipmentSlot.Feet:
                {
                    bool isMetalBoots = name.Contains("steel") || name.Contains("iron") || name.Contains("plate") || name.Contains("greave");
                    if (isMetalBoots && profile != HeroClassProfile.Knight && profile != HeroClassProfile.Paladin && profile != HeroClassProfile.Cleric)
                    {
                        reason = $"Class restricted: {archLabel}s cannot wear heavy steel-shod boots.";
                        return false;
                    }
                    reason = string.Empty;
                    return true;
                }

                case EquipmentSlot.Hands:
                {
                    bool isMetalGauntlets = name.Contains("gauntlet") || name.Contains("plate") || name.Contains("steel");
                    if (isMetalGauntlets && profile != HeroClassProfile.Knight && profile != HeroClassProfile.Paladin && profile != HeroClassProfile.Cleric)
                    {
                        reason = $"Class restricted: {archLabel}s cannot wear heavy metal gauntlets.";
                        return false;
                    }
                    reason = string.Empty;
                    return true;
                }

                case EquipmentSlot.Accessory1:
                case EquipmentSlot.Accessory2:
                {
                    if (name.Contains("quiver") && (profile == HeroClassProfile.Mage || profile == HeroClassProfile.Cleric))
                    {
                        reason = $"Class restricted: {archLabel}s do not equip archery quivers.";
                        return false;
                    }
                    reason = string.Empty;
                    return true;
                }

                default:
                    reason = string.Empty;
                    return true;
            }
        }

        public static double ScoreItemForHero(PartyMemberState hero, ItemDefinition item)
        {
            if (hero == null || item == null)
            {
                return 0.0;
            }

            if (!PartyMemberState.TryParseSlot(item.Slot, out EquipmentSlot slot))
            {
                return -1.0;
            }

            if (!CanHeroEquip(hero, item, out _))
            {
                return -1.0;
            }

            double dmgAvg = 0.0;
            if (!string.IsNullOrWhiteSpace(item.DamageDice) && DiceSpec.TryParse(item.DamageDice, out DiceSpec spec))
            {
                dmgAvg = (spec.Min + spec.Max) * 0.5;
            }

            HeroClassProfile profile = ResolveClassProfile(hero.Archetype);
            string name = (item.Name ?? string.Empty).ToLowerInvariant();
            bool prefersRanged = profile == HeroClassProfile.Ranger;
            bool lightArmorRole = profile == HeroClassProfile.Mage || profile == HeroClassProfile.Rogue;
            bool isFrontline = profile == HeroClassProfile.Knight || profile == HeroClassProfile.Paladin;

            double score = 0.0;
            if (slot == EquipmentSlot.Weapon)
            {
                score = item.WeaponBonus * 14.0 + dmgAvg * 2.5 + item.ArmorClassBonus * 3.0;
                if (isFrontline && (name.Contains("sword") || name.Contains("axe") || name.Contains("morning star") || name.Contains("flail")))
                {
                    score += 8.0;
                }
                else if (profile == HeroClassProfile.Cleric && (name.Contains("mace") || name.Contains("flail") || name.Contains("morning star") || name.Contains("hammer") || name.Contains("staff")))
                {
                    score += 10.0;
                }
                else if (profile == HeroClassProfile.Mage && (name.Contains("staff") || name.Contains("spell") || name.Contains("shielding")))
                {
                    score += 16.0;
                }
                else if (profile == HeroClassProfile.Rogue && (name.Contains("dagger") || name.Contains("short sword")))
                {
                    score += 10.0;
                }
                else if (profile == HeroClassProfile.Ranger && name.Contains("short sword"))
                {
                    score += 4.0;
                }

                if (item.IsTwoHanded && hero.Equipped.ContainsKey(EquipmentSlot.Shield))
                {
                    score -= 4.0;
                }
            }
            else if (slot == EquipmentSlot.MissileWeapon)
            {
                score = item.WeaponBonus * 14.0 + dmgAvg * (prefersRanged ? 3.5 : 1.5) + (prefersRanged ? 22.0 : 4.0);
                if (name.Contains("bow") && profile == HeroClassProfile.Ranger)
                {
                    score += 12.0;
                }
            }
            else
            {
                score = item.ArmorClassBonus * 6.0 + item.WeaponBonus * 4.0;
                if (slot == EquipmentSlot.Armor || slot == EquipmentSlot.Shield || slot == EquipmentSlot.Helm)
                {
                    if (isFrontline) score += item.ArmorClassBonus * 1.5;
                    else if (profile == HeroClassProfile.Cleric) score += item.ArmorClassBonus * 1.0;
                    else if (profile == HeroClassProfile.Mage && name.Contains("robe")) score += 6.0;
                    else if (profile == HeroClassProfile.Ranger && name.Contains("leather")) score += 4.0;
                    else if (profile == HeroClassProfile.Rogue && name.Contains("leather")) score += 4.0;

                    if (isFrontline && name.Contains("robe"))
                    {
                        score *= 0.35;
                    }
                }
                else if (slot == EquipmentSlot.Accessory1 || slot == EquipmentSlot.Accessory2)
                {
                    if (name.Contains("quiver"))
                    {
                        score += profile == HeroClassProfile.Ranger ? 22.0 : (profile == HeroClassProfile.Rogue ? 4.0 : 0.0);
                    }
                    else if (name.Contains("luck"))
                    {
                        if (profile == HeroClassProfile.Rogue) score += 18.0;
                        else if (profile == HeroClassProfile.Mage) score += 12.0;
                        else if (profile == HeroClassProfile.Ranger || profile == HeroClassProfile.Cleric) score += 6.0;
                    }
                    else if (name.Contains("cloak") || name.Contains("robe"))
                    {
                        if (profile == HeroClassProfile.Ranger || profile == HeroClassProfile.Mage || profile == HeroClassProfile.Rogue)
                        {
                            score += 9.0;
                        }
                    }
                }

                if (lightArmorRole && item.MovementMod < 0)
                {
                    score += item.MovementMod * 2.5;
                }
                else if (item.MovementMod != 0)
                {
                    score += item.MovementMod * 0.8;
                }

                // Class-smart Equipment Set affinity (keeps Arcanist Signet with Mages, Silver Sun Amulet with Clerics, etc.)
                IReadOnlyList<EquipmentSetDefinition> itemSets = EquipmentSetCatalog.GetSetsForItem(item.Id);
                for (int s = 0; s < itemSets.Count; s++)
                {
                    EquipmentSetDefinition setDef = itemSets[s];
                    string setId = setDef.SetId;
                    if (string.Equals(setId, EquipmentSetCatalog.BywaterArcanistSetId, StringComparison.OrdinalIgnoreCase))
                    {
                        score += profile == HeroClassProfile.Mage ? 28.0 : (profile == HeroClassProfile.Cleric ? 4.0 : 0.0);
                    }
                    else if (string.Equals(setId, EquipmentSetCatalog.SanctuaryAcolyteSetId, StringComparison.OrdinalIgnoreCase))
                    {
                        score += profile == HeroClassProfile.Cleric ? 28.0 : (profile == HeroClassProfile.Paladin ? 10.0 : 0.0);
                    }
                    else if (string.Equals(setId, EquipmentSetCatalog.ThornwoodStalkerSetId, StringComparison.OrdinalIgnoreCase))
                    {
                        score += profile == HeroClassProfile.Ranger ? 28.0 : (profile == HeroClassProfile.Rogue ? 12.0 : 0.0);
                    }
                    else if (string.Equals(setId, EquipmentSetCatalog.ScoutsLeatherSetId, StringComparison.OrdinalIgnoreCase))
                    {
                        score += (profile == HeroClassProfile.Rogue || profile == HeroClassProfile.Ranger) ? 24.0 : 4.0;
                    }
                    else if (string.Equals(setId, EquipmentSetCatalog.IronVanguardSetId, StringComparison.OrdinalIgnoreCase))
                    {
                        score += (profile == HeroClassProfile.Knight || profile == HeroClassProfile.Paladin) ? 26.0 : 0.0;
                    }
                    else if (string.Equals(setId, EquipmentSetCatalog.CryptbreakerSetId, StringComparison.OrdinalIgnoreCase))
                    {
                        score += profile == HeroClassProfile.Paladin ? 28.0 : (profile == HeroClassProfile.Knight || profile == HeroClassProfile.Cleric ? 14.0 : 0.0);
                    }

                    // Slight bonus for completing a set this hero already has pieces equipped in
                    foreach (InventoryItem eq in hero.Equipped.Values)
                    {
                        if (setDef.ContainsItem(eq.ItemId))
                        {
                            score += 10.0;
                            break;
                        }
                    }
                }
            }

            return score + item.BasePrice * 0.001;
        }

        public static HeroUpgradeRecommendation? FindBestUpgradeRecipient(
            PartyState? party,
            ItemDefinition? item,
            IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            if (party == null || item == null)
            {
                return null;
            }

            if (!PartyMemberState.TryParseSlot(item.Slot, out EquipmentSlot slot))
            {
                return null;
            }

            HeroUpgradeRecommendation? best = null;
            double bestPriority = double.MinValue;

            for (int i = 0; i < party.Members.Count; i++)
            {
                PartyMemberState hero = party.Members[i];
                if (hero == null || !hero.IsAlive)
                {
                    continue;
                }

                if (!CanHeroEquip(hero, item, out _))
                {
                    continue;
                }

                // Role-weighted upgrade recommendation: keep Bows reserved for Rangers/Archers and Robes for casters
                HeroClassProfile recProfile = ResolveClassProfile(hero.Archetype);
                string recName = (item.Name ?? string.Empty).ToLowerInvariant();
                if (slot == EquipmentSlot.MissileWeapon && recName.Contains("bow") && recProfile != HeroClassProfile.Ranger)
                {
                    continue;
                }
                if (slot == EquipmentSlot.Armor && recName.Contains("robe") && recProfile != HeroClassProfile.Mage)
                {
                    continue;
                }

                double candScore = ScoreItemForHero(hero, item);
                if (candScore <= 0.0)
                {
                    continue;
                }

                double currentScore = 0.0;
                bool isEmptySlotUpgrade = false;
                if (PartyMemberState.IsAccessorySlot(slot))
                {
                    bool has1 = hero.Equipped.TryGetValue(EquipmentSlot.Accessory1, out InventoryItem? acc1);
                    bool has2 = hero.Equipped.TryGetValue(EquipmentSlot.Accessory2, out InventoryItem? acc2);
                    if (!has1 || !has2)
                    {
                        isEmptySlotUpgrade = true;
                        currentScore = 0.0;
                    }
                    else
                    {
                        double s1 = (catalog != null && acc1 != null && catalog.TryGetValue(acc1.ItemId, out ItemDefinition? d1))
                            ? ScoreItemForHero(hero, d1)
                            : 0.0;
                        double s2 = (catalog != null && acc2 != null && catalog.TryGetValue(acc2.ItemId, out ItemDefinition? d2))
                            ? ScoreItemForHero(hero, d2)
                            : 0.0;
                        currentScore = Math.Min(s1, s2);
                    }
                }
                else
                {
                    if (!hero.Equipped.TryGetValue(slot, out InventoryItem? cur))
                    {
                        isEmptySlotUpgrade = true;
                        currentScore = 0.0;
                    }
                    else if (catalog != null && catalog.TryGetValue(cur.ItemId, out ItemDefinition? curDef))
                    {
                        currentScore = Math.Max(0.0, ScoreItemForHero(hero, curDef));
                    }
                    else
                    {
                        // Without catalog lookup, assume same item id matches; otherwise cannot assume blind upgrade
                        if (cur.ItemId == item.Id)
                        {
                            continue;
                        }
                        currentScore = candScore * 0.85;
                    }
                }

                if (slot == EquipmentSlot.Weapon && item.IsTwoHanded &&
                    hero.Equipped.TryGetValue(EquipmentSlot.Shield, out InventoryItem? shieldItem) &&
                    catalog != null &&
                    catalog.TryGetValue(shieldItem.ItemId, out ItemDefinition? shieldDef))
                {
                    double shieldScore = Math.Max(0.0, ScoreItemForHero(hero, shieldDef));
                    if (candScore - shieldScore <= currentScore + 0.01)
                    {
                        continue;
                    }
                }

                double delta = candScore - currentScore;
                if (delta <= 0.01)
                {
                    continue;
                }

                double priority = candScore + delta * 0.5 + (isEmptySlotUpgrade ? 3.0 : 0.0);
                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    best = new HeroUpgradeRecommendation(i, hero, candScore, currentScore, delta);
                }
            }

            return best;
        }

        public int EquipBestForHero(int heroIndex, IReadOnlyDictionary<int, ItemDefinition> catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }
            if (heroIndex < 0 || heroIndex >= _members.Count)
            {
                return 0;
            }

            PartyMemberState hero = _members[heroIndex];
            int equippedCount = 0;
            EquipmentSlot[] slots =
            {
                EquipmentSlot.Weapon,
                EquipmentSlot.MissileWeapon,
                EquipmentSlot.Armor,
                EquipmentSlot.Shield,
                EquipmentSlot.Helm,
                EquipmentSlot.Hands,
                EquipmentSlot.Feet,
                EquipmentSlot.Accessory1,
                EquipmentSlot.Accessory2
            };

            for (int s = 0; s < slots.Length; s++)
            {
                EquipmentSlot slot = slots[s];
                double currentScore = -0.01;
                if (hero.Equipped.TryGetValue(slot, out InventoryItem? curEq) &&
                    catalog.TryGetValue(curEq.ItemId, out ItemDefinition? curDef))
                {
                    currentScore = ScoreItemForHero(hero, curDef);
                }

                int bestIdx = -1;
                double bestScore = currentScore;
                for (int i = 0; i < _stash.Count; i++)
                {
                    InventoryItem candidate = _stash[i];
                    if (candidate.Quantity <= 0 ||
                        !catalog.TryGetValue(candidate.ItemId, out ItemDefinition? candDef) ||
                        !PartyMemberState.TryParseSlot(candDef.Slot, out EquipmentSlot candSlot) ||
                        !CanHeroEquip(hero, candDef, out _))
                    {
                        continue;
                    }

                    bool compatible = candSlot == slot ||
                        (PartyMemberState.IsAccessorySlot(slot) && PartyMemberState.IsAccessorySlot(candSlot));
                    if (!compatible)
                    {
                        continue;
                    }

                    // Keep Ranger arrows / shield tradeoffs smart
                    if (candSlot == EquipmentSlot.Weapon && candDef.IsTwoHanded &&
                        hero.Equipped.TryGetValue(EquipmentSlot.Shield, out InventoryItem? shieldItem) &&
                        catalog.TryGetValue(shieldItem.ItemId, out ItemDefinition? shieldDef))
                    {
                        double netWeaponWithShieldDrop = ScoreItemForHero(hero, candDef) - ScoreItemForHero(hero, shieldDef);
                        if (netWeaponWithShieldDrop <= currentScore)
                        {
                            continue;
                        }
                    }

                    double sc = ScoreItemForHero(hero, candDef);
                    if (sc > bestScore + 0.01)
                    {
                        bestScore = sc;
                        bestIdx = i;
                    }
                }

                if (bestIdx >= 0 && EquipFromStash(heroIndex, bestIdx, catalog, slot))
                {
                    equippedCount++;
                }
            }

            return equippedCount;
        }

        public int EquipBestForParty(IReadOnlyDictionary<int, ItemDefinition> catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            int total = 0;
            bool changed = true;
            int safety = 0;
            while (changed && safety < 16)
            {
                changed = false;
                safety++;
                for (int i = 0; i < _members.Count; i++)
                {
                    int c = EquipBestForHero(i, catalog);
                    if (c > 0)
                    {
                        total += c;
                        changed = true;
                    }
                }
            }
            return total;
        }

        public bool TryUseConsumableOnHero(
            int heroIndex,
            int stashIndex,
            IReadOnlyDictionary<int, ItemDefinition> catalog,
            IDiceRoller? roller,
            out string feedbackMessage)
        {
            feedbackMessage = string.Empty;
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }
            if (heroIndex < 0 || heroIndex >= _members.Count || stashIndex < 0 || stashIndex >= _stash.Count)
            {
                return false;
            }

            InventoryItem entry = _stash[stashIndex];
            if (!catalog.TryGetValue(entry.ItemId, out ItemDefinition? def))
            {
                return false;
            }

            PartyMemberState hero = _members[heroIndex];
            IDiceRoller rng = roller ?? new DeterministicDiceRoller(0x555345UL);

            if (entry.ItemId == TorchItemId)
            {
                if (!ConsumeStashChargeAt(stashIndex))
                {
                    return false;
                }
                TorchMinutesRemaining += DefaultTorchDurationMinutes;
                feedbackMessage = $"Lit a Torch (+{FormatTorchDuration(DefaultTorchDurationMinutes)}; {FormatTorchDuration(TorchMinutesRemaining)} active).";
                return true;
            }

            bool isConsumable = string.Equals(def.Slot, "Consumable", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(def.Category, "Potion", StringComparison.OrdinalIgnoreCase);
            if (!isConsumable && string.IsNullOrWhiteSpace(def.HealDice))
            {
                return false;
            }

            string nameLow = (def.Name ?? string.Empty).ToLowerInvariant();
            if (entry.ItemId == 603 || nameLow.Contains("heat resist"))
            {
                if (!ConsumeStashChargeAt(stashIndex))
                {
                    return false;
                }
                hero.Conditions.Apply(CharConditionId.FireProtection, magnitude: 1, turnsRemaining: 10, baseRecoveryChancePercent: 0, canRecoverBySave: false);
                feedbackMessage = $"{hero.Name} drank {def.Name}: Fire Protection (+50% Heat Res, 10t).";
                return true;
            }

            if (entry.ItemId == 723 || nameLow.Contains("refresh"))
            {
                if (!ConsumeStashChargeAt(stashIndex))
                {
                    return false;
                }
                int spGain = Math.Min(hero.MaxSp - hero.Sp, 8);
                hero.Sp += Math.Max(0, spGain);
                int clearedEx = hero.ExhaustionStacks;
                hero.ExhaustionStacks = 0;
                double fatReduced = ReducePartyFatigue(45.0);
                int hpHeal = 0;
                if (!string.IsNullOrWhiteSpace(def.HealDice) && DiceSpec.TryParse(def.HealDice, out DiceSpec rSpec))
                {
                    int beforeHp = hero.Hp;
                    hero.Hp = Math.Min(hero.MaxHp, Math.Max(1, hero.Hp) + Math.Max(1, rSpec.Roll(rng)));
                    hpHeal = Math.Max(0, hero.Hp - beforeHp);
                }
                string fatNote = fatReduced > 0.5 ? $", -{(int)Math.Round(fatReduced)}% fatigue" : string.Empty;
                feedbackMessage = $"{hero.Name} drank {def.Name}: +{hpHeal} HP, +{spGain} SP, cleared {clearedEx} Exhaustion{fatNote}.";
                return true;
            }

            if (entry.ItemId == 602 || nameLow.Contains("poison"))
            {
                if (!ConsumeStashChargeAt(stashIndex))
                {
                    return false;
                }
                hero.Conditions.Remove(CharConditionId.Poisoned);
                hero.Conditions.Remove(CharConditionId.Diseased);
                int hpHeal = 0;
                if (hero.Hp > 0 && !string.IsNullOrWhiteSpace(def.HealDice) && DiceSpec.TryParse(def.HealDice, out DiceSpec pSpec))
                {
                    int before = hero.Hp;
                    hero.Hp = Math.Min(hero.MaxHp, hero.Hp + Math.Max(1, pSpec.Roll(rng)));
                    hpHeal = Math.Max(0, hero.Hp - before);
                }
                feedbackMessage = $"{hero.Name} drank {def.Name}: Poison/Disease cured (+{hpHeal} HP).";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(def.HealDice) && DiceSpec.TryParse(def.HealDice, out DiceSpec healSpec))
            {
                if (!ConsumeStashChargeAt(stashIndex))
                {
                    return false;
                }
                int healAmt = Math.Max(1, healSpec.Roll(rng));
                int before = hero.Hp;
                hero.Hp = Math.Min(hero.MaxHp, hero.Hp + healAmt);
                feedbackMessage = $"{hero.Name} used {def.Name} (+{Math.Max(0, hero.Hp - before)} HP).";
                return true;
            }

            return false;
        }

        private bool ConsumeStashChargeAt(int stashIndex)
        {
            if (stashIndex < 0 || stashIndex >= _stash.Count)
            {
                return false;
            }
            InventoryItem entry = _stash[stashIndex];
            if (entry.Quantity <= 0)
            {
                return false;
            }
            int chg = entry.RemainingCharges;
            if (chg <= 0)
            {
                return RemoveItemFromStash(stashIndex, 1);
            }
            int after = chg - 1;
            int itemId = entry.ItemId;
            bool isId = entry.IsIdentified;
            if (entry.Quantity == 1 && after > 0)
            {
                bool hasTargetStack = false;
                for (int i = 0; i < _stash.Count; i++)
                {
                    if (i != stashIndex &&
                        _stash[i].ItemId == itemId &&
                        _stash[i].IsIdentified == isId &&
                        Math.Max(0, _stash[i].RemainingCharges) == after)
                    {
                        hasTargetStack = true;
                        break;
                    }
                }
                if (!hasTargetStack)
                {
                    entry.RemainingCharges = after;
                    return true;
                }
            }
            if (!RemoveItemFromStash(stashIndex, 1))
            {
                return false;
            }
            if (after > 0)
            {
                AddItemToStash(itemId, 1, isId, after);
            }
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
            return Rest(mode, isSafeZone: true, inHostileZone: false, roller: null);
        }

        public RestResult Rest(
            CampMode mode,
            bool isSafeZone,
            bool inHostileZone = false,
            IDiceRoller? roller = null)
        {
            if (mode == CampMode.QuickBandage)
            {
                if (QuickBandageCharges <= 0)
                {
                    return new RestResult(
                        mode,
                        0,
                        0,
                        0,
                        TorchTurnsRemaining > 0,
                        success: false,
                        turnsElapsed: 0,
                        torchChargesConsumed: 0,
                        quickBandagesRemaining: QuickBandageCharges,
                        reason: "No Quick Bandage charges left. Full Rest or visit Temple to refill.",
                        consumedRation: false,
                        foodRationsRemaining: FoodRations);
                }

                bool anyLivingInjured = false;
                for (int i = 0; i < _members.Count; i++)
                {
                    if (_members[i].IsAlive && _members[i].Hp < _members[i].MaxHp)
                    {
                        anyLivingInjured = true;
                        break;
                    }
                }

                if (!anyLivingInjured)
                {
                    return new RestResult(
                        mode,
                        0,
                        0,
                        0,
                        TorchTurnsRemaining > 0,
                        success: false,
                        turnsElapsed: 0,
                        torchChargesConsumed: 0,
                        quickBandagesRemaining: QuickBandageCharges,
                        reason: "No injured living heroes need field bandaging.",
                        consumedRation: false,
                        foodRationsRemaining: FoodRations);
                }

                QuickBandageCharges--;
                int healedTotal = 0;
                for (int i = 0; i < _members.Count; i++)
                {
                    PartyMemberState m = _members[i];
                    if (m.IsAlive && m.Hp < m.MaxHp)
                    {
                        int before = m.Hp;
                        int healAmt = Math.Max(3, (int)Math.Ceiling(m.MaxHp * 0.35));
                        m.Hp = Math.Min(m.MaxHp, m.Hp + healAmt);
                        healedTotal += Math.Max(0, m.Hp - before);
                    }
                }

                return new RestResult(
                    mode,
                    healedTotal,
                    0,
                    0,
                    TorchTurnsRemaining > 0,
                    success: true,
                    turnsElapsed: 1,
                    torchChargesConsumed: 0,
                    quickBandagesRemaining: QuickBandageCharges,
                    consumedRation: false,
                    foodRationsRemaining: FoodRations);
            }

            // CampMode.FullRest
            int torchChargesConsumed = 0;
            bool burnedNewStashTorch = false;
            if (!isSafeZone)
            {
                if (TryConsumeItemCharge(TorchItemId, out _))
                {
                    torchChargesConsumed = 1;
                    burnedNewStashTorch = true;
                }
                else if (TorchMinutesRemaining >= 30.0)
                {
                    TorchMinutesRemaining = Math.Max(0.0, TorchMinutesRemaining - 30.0);
                }
                else
                {
                    return new RestResult(
                        mode,
                        0,
                        0,
                        0,
                        TorchTurnsRemaining > 0,
                        success: false,
                        turnsElapsed: 0,
                        torchChargesConsumed: 0,
                        quickBandagesRemaining: QuickBandageCharges,
                        reason: "Full Rest outside Bywater town requires 1 Torch charge (or >= 30m active torch).",
                        consumedRation: false,
                        foodRationsRemaining: FoodRations);
                }
            }

            if (burnedNewStashTorch)
            {
                TorchMinutesRemaining = Math.Max(TorchMinutesRemaining, 60.0);
            }

            if (inHostileZone && !CampSentryActive && roller != null && roller.NextInt(1, 101) <= 15)
            {
                return new RestResult(
                    mode,
                    0,
                    0,
                    0,
                    TorchTurnsRemaining > 0,
                    success: false,
                    turnsElapsed: 4,
                    torchChargesConsumed: torchChargesConsumed,
                    quickBandagesRemaining: QuickBandageCharges,
                    ambushed: true,
                    reason: "Camp ambushed by wandering monsters!",
                    consumedRation: false,
                    foodRationsRemaining: FoodRations);
            }

            CampSentryActive = false;
            ClearPartyFatigue();

            bool consumedRation = false;
            if (!isSafeZone && FoodRations > 0)
            {
                consumedRation = TryConsumeFoodRation();
            }

            int totalHp = 0;
            int totalSp = 0;
            int totalExhaustionCleared = 0;
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
                // Keep persistent Poisoned, Diseased, and Cursed for potions/Temple; clear transient combat effects.
                m.Conditions.Remove(CharConditionId.Slow);
                m.Conditions.Remove(CharConditionId.Tangled);
                m.Conditions.Remove(CharConditionId.Blind);
                m.Conditions.Remove(CharConditionId.Confused);
                m.Conditions.Remove(CharConditionId.Helpless);
                m.Conditions.Remove(CharConditionId.Silenced);
            }

            QuickBandageCharges = MaxQuickBandageCharges;
            return new RestResult(
                mode,
                totalHp,
                totalSp,
                totalExhaustionCleared,
                TorchTurnsRemaining > 0,
                success: true,
                turnsElapsed: 8,
                torchChargesConsumed: torchChargesConsumed,
                quickBandagesRemaining: QuickBandageCharges,
                consumedRation: consumedRation,
                foodRationsRemaining: FoodRations);
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

                if (!string.IsNullOrWhiteSpace(category) && !MatchesShopCategory(def.Category, category!))
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

        private static bool MatchesShopCategory(string itemCategory, string filterCategory)
        {
            string cat = (itemCategory ?? string.Empty).Trim();
            string filt = (filterCategory ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(filt) || string.Equals(filt, "All", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(filt, "Armor", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(cat, "Armor", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(cat, "Helm", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(cat, "Gloves", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(cat, "Boots", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(cat, "Cloak", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(cat, "Shield", StringComparison.OrdinalIgnoreCase);
            }
            if (string.Equals(filt, "Magic", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filt, "Accessory", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(cat, "Magic", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(cat, "Accessory", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(cat, "Cloak", StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(cat, filt, StringComparison.OrdinalIgnoreCase);
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
