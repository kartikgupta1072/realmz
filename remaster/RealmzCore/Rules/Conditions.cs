using System;
using System.Collections.Generic;

namespace RealmzCore.Rules
{
    /// <summary>
    /// Character condition IDs matching structs.h enum CharCondition (0..39).
    /// </summary>
    public enum CharConditionId
    {
        RunsAway = 0,
        Helpless = 1,
        Tangled = 2,
        Cursed = 3,
        MagicAura = 4,
        Stupid = 5,
        Slow = 6,
        ShieldFromHits = 7,
        ShieldFromProjectiles = 8,
        Poisoned = 9,
        Regenerating = 10,
        FireProtection = 11,
        ColdProtection = 12,
        ElectricalProtection = 13,
        ChemicalProtection = 14,
        MentalProtection = 15,
        FirstLevelSpellProtection = 16,
        SecondLevelSpellProtection = 17,
        ThirdLevelSpellProtection = 18,
        FourthLevelSpellProtection = 19,
        FifthLevelSpellProtection = 20,
        Strong = 21,
        ProtectionFromEvil = 22,
        Speedy = 23,
        Invisible = 24,
        Animated = 25,
        TurnedToStone = 26,
        Blind = 27,
        Diseased = 28,
        Confused = 29,
        ReflectingSpells = 30,
        ReflectingAttacks = 31,
        AttackBonus = 32,
        AbsorbingEnergy = 33,
        EnergyDrain = 34,
        AbsorbingEnergyFromAttacks = 35,
        HinderedAttacks = 36,
        HinderedDefense = 37,
        DefenseBonus = 38,
        Silenced = 39
    }

    public enum RulesetMode
    {
        ModernBalanced = 0,
        Classic1994Strict = 1
    }

    public sealed class ConditionTracker
    {
        public sealed class ConditionEntry
        {
            public ConditionEntry(
                CharConditionId id,
                int magnitude,
                int turnsRemaining,
                int baseRecoveryChancePercent,
                bool canRecoverBySave)
            {
                Id = id;
                Magnitude = magnitude;
                TurnsRemaining = turnsRemaining;
                BaseRecoveryChancePercent = baseRecoveryChancePercent;
                CanRecoverBySave = canRecoverBySave;
                ElapsedRounds = 0;
            }

            public CharConditionId Id { get; }
            public int Magnitude { get; set; }
            public int TurnsRemaining { get; set; }
            public int BaseRecoveryChancePercent { get; set; }
            public bool CanRecoverBySave { get; set; }
            public int ElapsedRounds { get; set; }
        }

        private readonly Dictionary<CharConditionId, ConditionEntry> _entries =
            new Dictionary<CharConditionId, ConditionEntry>();

        public int Count => _entries.Count;

        public void Apply(
            CharConditionId condition,
            int magnitude = 1,
            int turnsRemaining = -1,
            int baseRecoveryChancePercent = 20,
            bool canRecoverBySave = true)
        {
            if (baseRecoveryChancePercent < 0 || baseRecoveryChancePercent > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(baseRecoveryChancePercent));
            }

            if (_entries.TryGetValue(condition, out ConditionEntry? existing))
            {
                existing.Magnitude = magnitude;
                existing.TurnsRemaining = turnsRemaining;
                existing.BaseRecoveryChancePercent = baseRecoveryChancePercent;
                existing.CanRecoverBySave = canRecoverBySave;
                existing.ElapsedRounds = 0;
            }
            else
            {
                _entries[condition] = new ConditionEntry(
                    condition,
                    magnitude,
                    turnsRemaining,
                    baseRecoveryChancePercent,
                    canRecoverBySave);
            }
        }

        public bool Remove(CharConditionId condition)
        {
            return _entries.Remove(condition);
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public bool Has(CharConditionId condition)
        {
            return _entries.ContainsKey(condition);
        }

        public int GetMagnitude(CharConditionId condition)
        {
            return _entries.TryGetValue(condition, out ConditionEntry? entry) ? entry.Magnitude : 0;
        }

        public int GetElapsedRounds(CharConditionId condition)
        {
            return _entries.TryGetValue(condition, out ConditionEntry? entry) ? entry.ElapsedRounds : 0;
        }

        public int GetRecoverySaveChance(CharConditionId condition, RulesetMode mode)
        {
            if (!_entries.TryGetValue(condition, out ConditionEntry? entry) || !entry.CanRecoverBySave)
            {
                return 0;
            }

            if (mode == RulesetMode.Classic1994Strict)
            {
                return Math.Clamp(entry.BaseRecoveryChancePercent, 0, 100);
            }

            // ModernBalanced adds +10% per elapsed round to avoid indefinite stunlocks.
            int escalated = entry.BaseRecoveryChancePercent + (entry.ElapsedRounds * 10);
            return Math.Clamp(escalated, 0, 95);
        }

        public IReadOnlyList<CharConditionId> TickEndOfTurn(IDiceRoller roller, RulesetMode mode)
        {
            if (roller == null)
            {
                throw new ArgumentNullException(nameof(roller));
            }

            List<CharConditionId> cleared = new List<CharConditionId>();
            if (_entries.Count == 0)
            {
                return cleared;
            }

            List<CharConditionId> keys = new List<CharConditionId>(_entries.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                CharConditionId id = keys[i];
                ConditionEntry entry = _entries[id];

                if (entry.TurnsRemaining > 0)
                {
                    entry.TurnsRemaining--;
                    if (entry.TurnsRemaining == 0)
                    {
                        _entries.Remove(id);
                        cleared.Add(id);
                        continue;
                    }
                }

                if (entry.CanRecoverBySave)
                {
                    int chance = GetRecoverySaveChance(id, mode);
                    if (chance > 0)
                    {
                        int roll = roller.NextInt(1, 101);
                        if (roll <= chance)
                        {
                            _entries.Remove(id);
                            cleared.Add(id);
                            continue;
                        }
                    }
                }

                entry.ElapsedRounds++;
            }

            return cleared;
        }
    }
}
