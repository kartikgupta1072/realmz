using System;
using System.Collections.Generic;

namespace RealmzCore.Rules
{
    public sealed class CombatActionEconomy
    {
        private readonly Stack<int> _moveHistory = new Stack<int>();

        public CombatActionEconomy(int moveBudget)
        {
            ResetTurn(moveBudget);
        }

        public int MoveBudget { get; private set; }
        public int MoveRemaining { get; private set; }
        public bool HasStandardAction { get; private set; }
        public bool HasSwiftAction { get; private set; }
        public bool CanUndoMove => _moveHistory.Count > 0 && HasStandardAction;

        public void ResetTurn(int moveBudget)
        {
            if (moveBudget < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(moveBudget));
            }

            MoveBudget = moveBudget;
            MoveRemaining = moveBudget;
            HasStandardAction = true;
            HasSwiftAction = true;
            _moveHistory.Clear();
        }

        public bool TryStep(int cost = 1)
        {
            if (cost <= 0 || MoveRemaining < cost)
            {
                return false;
            }

            MoveRemaining -= cost;
            _moveHistory.Push(cost);
            return true;
        }

        public bool UndoMove()
        {
            if (!CanUndoMove)
            {
                return false;
            }

            int lastCost = _moveHistory.Pop();
            MoveRemaining += lastCost;
            return true;
        }

        public bool CommitStandardAction()
        {
            if (!HasStandardAction)
            {
                return false;
            }

            HasStandardAction = false;
            _moveHistory.Clear();
            return true;
        }

        public bool CommitSwiftAction()
        {
            if (!HasSwiftAction)
            {
                return false;
            }

            HasSwiftAction = false;
            return true;
        }
    }

    public readonly struct HitPreview
    {
        public HitPreview(
            int hitChancePercent,
            int uncappedHitScore,
            int grazeThresholdPercent,
            int critThresholdPercent,
            bool targetIsHelpless)
        {
            HitChancePercent = hitChancePercent;
            UncappedHitScore = uncappedHitScore;
            GrazeThresholdPercent = grazeThresholdPercent;
            CritThresholdPercent = critThresholdPercent;
            TargetIsHelpless = targetIsHelpless;
        }

        public int HitChancePercent { get; }
        public int UncappedHitScore { get; }
        public int GrazeThresholdPercent { get; }
        public int CritThresholdPercent { get; }
        public bool TargetIsHelpless { get; }
    }

    public enum AttackOutcomeKind
    {
        Miss = 0,
        Graze = 1,
        Hit = 2,
        CriticalHit = 3
    }

    public sealed class AttackOutcome
    {
        public AttackOutcome(
            AttackOutcomeKind kind,
            int roll,
            int hitChancePercent,
            int baseDamageRolled,
            int damageDealt)
        {
            Kind = kind;
            Roll = roll;
            HitChancePercent = hitChancePercent;
            BaseDamageRolled = baseDamageRolled;
            DamageDealt = damageDealt;
        }

        public AttackOutcomeKind Kind { get; }
        public int Roll { get; }
        public int HitChancePercent { get; }
        public int BaseDamageRolled { get; }
        public int DamageDealt { get; }
        public bool IsHit => Kind != AttackOutcomeKind.Miss;
    }

    public static class CombatRules
    {
        public static HitPreview CalculateHitPreview(
            int attackerToHit,
            int defenderAc,
            bool isFlanking = false,
            int equippedWeaponBonus = 0,
            bool defenderIsEvil = false,
            ConditionTracker? attackerConditions = null,
            ConditionTracker? defenderConditions = null,
            RulesetMode mode = RulesetMode.ModernBalanced)
        {
            int score = 50 + attackerToHit + (isFlanking ? 20 : 0) + (5 * equippedWeaponBonus) - defenderAc;

            if (attackerConditions != null)
            {
                if (attackerConditions.Has(CharConditionId.Strong))
                {
                    score += 15;
                }

                if (attackerConditions.Has(CharConditionId.Slow))
                {
                    score -= 15;
                }

                if (attackerConditions.Has(CharConditionId.Blind))
                {
                    score -= 15;
                }

                if (attackerConditions.Has(CharConditionId.Confused))
                {
                    score -= 10;
                }

                if (attackerConditions.Has(CharConditionId.MagicAura))
                {
                    score += 5;
                }

                if (attackerConditions.Has(CharConditionId.Cursed))
                {
                    score -= 5;
                }

                if (defenderIsEvil && attackerConditions.Has(CharConditionId.ProtectionFromEvil))
                {
                    score += 10;
                }
            }

            bool targetIsHelpless = false;
            if (defenderConditions != null)
            {
                targetIsHelpless = defenderConditions.Has(CharConditionId.Helpless);

                if (defenderConditions.Has(CharConditionId.Slow))
                {
                    score += 15;
                }

                if (defenderConditions.Has(CharConditionId.Blind))
                {
                    score += 15;
                }

                if (defenderConditions.Has(CharConditionId.Confused))
                {
                    score += 10;
                }

                if (defenderConditions.Has(CharConditionId.MagicAura))
                {
                    score -= 5;
                }

                if (defenderConditions.Has(CharConditionId.Cursed))
                {
                    score += 5;
                }

                if (defenderConditions.Has(CharConditionId.Invisible))
                {
                    score -= 10;
                }
            }

            int clamped = Math.Clamp(score, 5, 95);
            int critThreshold = mode == RulesetMode.ModernBalanced ? 5 : 0;
            int grazeThreshold = mode == RulesetMode.ModernBalanced
                ? Math.Min(95, clamped + 10)
                : clamped;

            return new HitPreview(
                hitChancePercent: clamped,
                uncappedHitScore: score,
                grazeThresholdPercent: grazeThreshold,
                critThresholdPercent: critThreshold,
                targetIsHelpless: targetIsHelpless);
        }

        public static AttackOutcome ResolveMeleeAttack(
            IDiceRoller roller,
            DiceSpec damageDice,
            int attackerToHit,
            int attackerDamageBonus,
            int defenderAc,
            bool isFlanking = false,
            int equippedWeaponBonus = 0,
            bool defenderIsEvil = false,
            ConditionTracker? attackerConditions = null,
            ConditionTracker? defenderConditions = null,
            RulesetMode mode = RulesetMode.ModernBalanced)
        {
            if (roller == null)
            {
                throw new ArgumentNullException(nameof(roller));
            }

            HitPreview preview = CalculateHitPreview(
                attackerToHit: attackerToHit,
                defenderAc: defenderAc,
                isFlanking: isFlanking,
                equippedWeaponBonus: equippedWeaponBonus,
                defenderIsEvil: defenderIsEvil,
                attackerConditions: attackerConditions,
                defenderConditions: defenderConditions,
                mode: mode);

            int roll = roller.NextInt(1, 101);
            AttackOutcomeKind kind;

            if (preview.TargetIsHelpless)
            {
                kind = mode == RulesetMode.ModernBalanced && roll <= preview.CritThresholdPercent
                    ? AttackOutcomeKind.CriticalHit
                    : AttackOutcomeKind.Hit;
            }
            else if (mode == RulesetMode.ModernBalanced && roll <= preview.CritThresholdPercent)
            {
                kind = AttackOutcomeKind.CriticalHit;
            }
            else if (roll <= preview.HitChancePercent)
            {
                kind = AttackOutcomeKind.Hit;
            }
            else if (mode == RulesetMode.ModernBalanced && roll <= preview.GrazeThresholdPercent)
            {
                kind = AttackOutcomeKind.Graze;
            }
            else
            {
                return new AttackOutcome(
                    kind: AttackOutcomeKind.Miss,
                    roll: roll,
                    hitChancePercent: preview.HitChancePercent,
                    baseDamageRolled: 0,
                    damageDealt: 0);
            }

            int strongBonus = attackerConditions != null && attackerConditions.Has(CharConditionId.Strong) ? 3 : 0;
            int rawDamage = Math.Max(1, damageDice.Roll(roller) + attackerDamageBonus + strongBonus);

            int finalDamage = kind switch
            {
                AttackOutcomeKind.CriticalHit => rawDamage * 2,
                AttackOutcomeKind.Graze => Math.Max(1, rawDamage / 2),
                _ => rawDamage
            };

            return new AttackOutcome(
                kind: kind,
                roll: roll,
                hitChancePercent: preview.HitChancePercent,
                baseDamageRolled: rawDamage,
                damageDealt: finalDamage);
        }
    }
}
