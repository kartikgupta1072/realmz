using System;
using System.Collections.Generic;
using RealmzCore.Rules;
using Xunit;

namespace RealmzCore.Tests
{
    public class DiceAndCombatTests
    {
        [Theory]
        [InlineData("2d6+3", 2, 6, 3, 5, 15, 10.0)]
        [InlineData("1d20", 1, 20, 0, 1, 20, 10.5)]
        [InlineData("d8-1", 1, 8, -1, 0, 7, 3.5)]
        [InlineData(" 3D10 + 4 ", 3, 10, 4, 7, 34, 20.5)]
        [InlineData("12", 0, 0, 12, 12, 12, 12.0)]
        [InlineData("-3", 0, 0, -3, -3, -3, -3.0)]
        public void DiceSpec_Parse_ValidExpressions(
            string expr,
            int expectedCount,
            int expectedSides,
            int expectedMod,
            int expectedMin,
            int expectedMax,
            double expectedAvg)
        {
            DiceSpec spec = DiceSpec.Parse(expr);
            Assert.Equal(expectedCount, spec.Count);
            Assert.Equal(expectedSides, spec.Sides);
            Assert.Equal(expectedMod, spec.Modifier);
            Assert.Equal(expectedMin, spec.Min);
            Assert.Equal(expectedMax, spec.Max);
            Assert.Equal(expectedAvg, spec.ExpectedAverage, precision: 6);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("0d6")]
        [InlineData("-2d6")]
        [InlineData("2d0")]
        [InlineData("2d6+")]
        [InlineData("2d6+1+2")]
        [InlineData("2dd6")]
        [InlineData("d")]
        public void DiceSpec_Parse_InvalidExpressionsThrow(string expr)
        {
            Assert.False(DiceSpec.TryParse(expr, out _));
            Assert.Throws<FormatException>(() => DiceSpec.Parse(expr));
        }

        [Fact]
        public void DeterministicDiceRoller_IsReproducibleForSameSeed()
        {
            DeterministicDiceRoller a = new DeterministicDiceRoller(123456789UL);
            DeterministicDiceRoller b = new DeterministicDiceRoller(123456789UL);
            DiceSpec spec = DiceSpec.Parse("3d6+2");

            for (int i = 0; i < 50; i++)
            {
                int rollA = spec.Roll(a);
                int rollB = spec.Roll(b);
                Assert.Equal(rollA, rollB);
                Assert.InRange(rollA, spec.Min, spec.Max);
            }
        }

        [Fact]
        public void CombatActionEconomy_MoveUndoAndStandardLockWork()
        {
            CombatActionEconomy econ = new CombatActionEconomy(moveBudget: 14);
            Assert.Equal(14, econ.MoveRemaining);
            Assert.True(econ.HasStandardAction);
            Assert.True(econ.HasSwiftAction);
            Assert.False(econ.CanUndoMove);

            Assert.True(econ.TryStep(3));
            Assert.Equal(11, econ.MoveRemaining);
            Assert.True(econ.CanUndoMove);

            Assert.True(econ.TryStep(4));
            Assert.Equal(7, econ.MoveRemaining);
            Assert.False(econ.TryStep(8));
            Assert.Equal(7, econ.MoveRemaining);

            // Swift action does not prevent move undo before standard action.
            Assert.True(econ.CommitSwiftAction());
            Assert.False(econ.HasSwiftAction);
            Assert.False(econ.CommitSwiftAction());
            Assert.True(econ.CanUndoMove);

            // Undo one step.
            Assert.True(econ.UndoMove());
            Assert.Equal(11, econ.MoveRemaining);
            Assert.True(econ.CanUndoMove);

            // Commit standard action locks move undo.
            Assert.True(econ.CommitStandardAction());
            Assert.False(econ.HasStandardAction);
            Assert.False(econ.CanUndoMove);
            Assert.False(econ.UndoMove());
            Assert.False(econ.CommitStandardAction());

            // Reset restores turn budget.
            econ.ResetTurn(12);
            Assert.Equal(12, econ.MoveBudget);
            Assert.Equal(12, econ.MoveRemaining);
            Assert.True(econ.HasStandardAction);
            Assert.True(econ.HasSwiftAction);
            Assert.False(econ.CanUndoMove);
        }

        [Fact]
        public void ConditionTracker_EscalatingSaveInModernVsFixedInClassic()
        {
            ConditionTracker modern = new ConditionTracker();
            modern.Apply(CharConditionId.Confused, magnitude: 1, turnsRemaining: -1, baseRecoveryChancePercent: 20);

            ConditionTracker classic = new ConditionTracker();
            classic.Apply(CharConditionId.Confused, magnitude: 1, turnsRemaining: -1, baseRecoveryChancePercent: 20);

            Assert.Equal(20, modern.GetRecoverySaveChance(CharConditionId.Confused, RulesetMode.ModernBalanced));
            Assert.Equal(20, classic.GetRecoverySaveChance(CharConditionId.Confused, RulesetMode.Classic1994Strict));

            // Fail save on roll 25 in round 0.
            QueuedDiceRoller failRound0 = new QueuedDiceRoller(25, 25);
            Assert.Empty(modern.TickEndOfTurn(failRound0, RulesetMode.ModernBalanced));
            Assert.Empty(classic.TickEndOfTurn(failRound0, RulesetMode.Classic1994Strict));

            // Round 1: ModernBalanced escalated to 30%, Classic1994Strict stays 20%.
            Assert.Equal(30, modern.GetRecoverySaveChance(CharConditionId.Confused, RulesetMode.ModernBalanced));
            Assert.Equal(20, classic.GetRecoverySaveChance(CharConditionId.Confused, RulesetMode.Classic1994Strict));

            // Roll 28 recovers in ModernBalanced, stays confused in Classic1994Strict.
            IReadOnlyList<CharConditionId> modernCleared = modern.TickEndOfTurn(
                new QueuedDiceRoller(28),
                RulesetMode.ModernBalanced);
            IReadOnlyList<CharConditionId> classicCleared = classic.TickEndOfTurn(
                new QueuedDiceRoller(28),
                RulesetMode.Classic1994Strict);

            Assert.Contains(CharConditionId.Confused, modernCleared);
            Assert.False(modern.Has(CharConditionId.Confused));
            Assert.Empty(classicCleared);
            Assert.True(classic.Has(CharConditionId.Confused));
        }

        [Fact]
        public void CombatRules_CalculateHitPreview_MatchesAttackCModifiers()
        {
            ConditionTracker attacker = new ConditionTracker();
            attacker.Apply(CharConditionId.Strong);             // +15
            attacker.Apply(CharConditionId.MagicAura);          // +5
            attacker.Apply(CharConditionId.ProtectionFromEvil); // +10 vs evil

            ConditionTracker defender = new ConditionTracker();
            defender.Apply(CharConditionId.Slow);               // +15
            defender.Apply(CharConditionId.Invisible);          // -10

            // Base: 50 + 20 (ToHit) + 20 (Flanking) + 10 (Weapon+2*5) - 35 (DefenderAC) = 65
            // Attacker: +15 + 5 + 10 = +30 => 95
            // Defender: +15 - 10 = +5 => 100, clamped to 95.
            HitPreview clampedHigh = CombatRules.CalculateHitPreview(
                attackerToHit: 20,
                defenderAc: 35,
                isFlanking: true,
                equippedWeaponBonus: 2,
                defenderIsEvil: true,
                attackerConditions: attacker,
                defenderConditions: defender,
                mode: RulesetMode.ModernBalanced);

            Assert.Equal(100, clampedHigh.UncappedHitScore);
            Assert.Equal(95, clampedHigh.HitChancePercent);
            Assert.Equal(5, clampedHigh.CritThresholdPercent);

            // Test negative modifiers and floor clamp at 5%.
            ConditionTracker blindedAttacker = new ConditionTracker();
            blindedAttacker.Apply(CharConditionId.Slow);     // -15
            blindedAttacker.Apply(CharConditionId.Blind);    // -15
            blindedAttacker.Apply(CharConditionId.Confused); // -10
            blindedAttacker.Apply(CharConditionId.Cursed);   // -5

            ConditionTracker blessedDefender = new ConditionTracker();
            blessedDefender.Apply(CharConditionId.MagicAura); // -5
            blessedDefender.Apply(CharConditionId.Invisible); // -10

            // 50 + 0 - 40 (AC) - 45 - 15 = -50 -> clamped to 5.
            HitPreview clampedLow = CombatRules.CalculateHitPreview(
                attackerToHit: 0,
                defenderAc: 40,
                isFlanking: false,
                equippedWeaponBonus: 0,
                defenderIsEvil: false,
                attackerConditions: blindedAttacker,
                defenderConditions: blessedDefender,
                mode: RulesetMode.Classic1994Strict);

            Assert.Equal(-50, clampedLow.UncappedHitScore);
            Assert.Equal(5, clampedLow.HitChancePercent);
            Assert.Equal(0, clampedLow.CritThresholdPercent);
        }

        [Fact]
        public void CombatRules_ResolveMeleeAttack_CritHitGrazeMiss()
        {
            DiceSpec weapon = DiceSpec.Parse("1d8+2");

            // Base hit chance: 50 + 10 - 0 = 60% (Crit <= 5, Hit <= 60, Graze 61..70, Miss 71..100).
            // 1. Critical hit on roll 4, damage roll 6 (+2 = 8, doubled = 16).
            AttackOutcome crit = CombatRules.ResolveMeleeAttack(
                new QueuedDiceRoller(4, 6),
                weapon,
                attackerToHit: 10,
                attackerDamageBonus: 0,
                defenderAc: 0,
                mode: RulesetMode.ModernBalanced);
            Assert.Equal(AttackOutcomeKind.CriticalHit, crit.Kind);
            Assert.Equal(16, crit.DamageDealt);

            // 2. Normal hit on roll 45, damage roll 5 (+2 = 7).
            AttackOutcome hit = CombatRules.ResolveMeleeAttack(
                new QueuedDiceRoller(45, 5),
                weapon,
                attackerToHit: 10,
                attackerDamageBonus: 0,
                defenderAc: 0,
                mode: RulesetMode.ModernBalanced);
            Assert.Equal(AttackOutcomeKind.Hit, hit.Kind);
            Assert.Equal(7, hit.DamageDealt);

            // 3. Grazing hit on roll 68 (within 10 of 60), damage roll 6 (+2 = 8, halved = 4).
            AttackOutcome graze = CombatRules.ResolveMeleeAttack(
                new QueuedDiceRoller(68, 6),
                weapon,
                attackerToHit: 10,
                attackerDamageBonus: 0,
                defenderAc: 0,
                mode: RulesetMode.ModernBalanced);
            Assert.Equal(AttackOutcomeKind.Graze, graze.Kind);
            Assert.Equal(4, graze.DamageDealt);

            // 4. Same roll 68 in Classic1994Strict is a Miss.
            AttackOutcome classicMiss = CombatRules.ResolveMeleeAttack(
                new QueuedDiceRoller(68),
                weapon,
                attackerToHit: 10,
                attackerDamageBonus: 0,
                defenderAc: 0,
                mode: RulesetMode.Classic1994Strict);
            Assert.Equal(AttackOutcomeKind.Miss, classicMiss.Kind);
            Assert.Equal(0, classicMiss.DamageDealt);

            // 5. Miss on roll 80 in ModernBalanced.
            AttackOutcome modernMiss = CombatRules.ResolveMeleeAttack(
                new QueuedDiceRoller(80),
                weapon,
                attackerToHit: 10,
                attackerDamageBonus: 0,
                defenderAc: 0,
                mode: RulesetMode.ModernBalanced);
            Assert.Equal(AttackOutcomeKind.Miss, modernMiss.Kind);
            Assert.Equal(0, modernMiss.DamageDealt);
        }
    }
}
