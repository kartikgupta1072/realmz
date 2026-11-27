using System.Collections.Generic;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Rules;
using Xunit;

namespace RealmzCore.Tests
{
    public class TacticalCombatVmTests
    {
        private static Dictionary<int, SpellDefinition> BuildTestSpells()
        {
            // 3x3 fireball template around center (dx: -1..1, dy: -1..1).
            List<GridCoord> box3x3 = new List<GridCoord>();
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    box3x3.Add(new GridCoord(dx, dy));
                }
            }

            SpellDefinition fireball = new SpellDefinition(
                id: 1,
                key: "fireball",
                name: "Fireball",
                school: "Sorcerer",
                level: 3,
                spCost: 8,
                range: 8,
                targetMode: "Area",
                spellAreaIndex: 0,
                aoeOffsets: box3x3,
                effectKind: "Damage",
                effectDice: "3d6",
                damageType: "Fire",
                appliedCondition: null,
                conditionDuration: 0,
                baseRecoveryPct: 20,
                saveKind: "Magic");

            SpellDefinition sandman = new SpellDefinition(
                id: 2,
                key: "sandman",
                name: "Sandman",
                school: "Sorcerer",
                level: 2,
                spCost: 6,
                range: 6,
                targetMode: "Area",
                spellAreaIndex: 0,
                aoeOffsets: new[] { new GridCoord(0, 0), new GridCoord(1, 0), new GridCoord(0, 1) },
                effectKind: "Condition",
                effectDice: "",
                damageType: "Mental",
                appliedCondition: CharConditionId.Helpless,
                conditionDuration: -1,
                baseRecoveryPct: 20,
                saveKind: "Mental");

            return new Dictionary<int, SpellDefinition>
            {
                [1] = fireball,
                [2] = sandman
            };
        }

        [Fact]
        public void InitiativeRibbon_OrdersSixHeroesAndEnemiesDeterministically()
        {
            CombatGrid grid = new CombatGrid(12, 12);
            List<CombatantState> units = new List<CombatantState>
            {
                new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(1, 1), 20, 20, dexterity: 14),
                new CombatantState("h2", "Liora", CombatSide.Hero, new GridCoord(1, 2), 16, 16, dexterity: 10),
                new CombatantState("h3", "Kaelen", CombatSide.Hero, new GridCoord(1, 3), 15, 15, dexterity: 18),
                new CombatantState("h4", "Theron", CombatSide.Hero, new GridCoord(1, 4), 12, 12, dexterity: 15),
                new CombatantState("h5", "Seraphina", CombatSide.Hero, new GridCoord(1, 5), 22, 22, dexterity: 11),
                new CombatantState("h6", "Vex", CombatSide.Hero, new GridCoord(1, 6), 14, 14, dexterity: 20),
                new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(9, 3), 10, 10, dexterity: 18),
                new CombatantState("m2", "Frog", CombatSide.Monster, new GridCoord(9, 5), 14, 14, dexterity: 16)
            };

            // Apply Speedy (+4 Dex) to Liora (10 -> 14) and Slow (-4 Dex) to Kaelen (18 -> 14).
            units[1].Conditions.Apply(CharConditionId.Speedy);
            units[2].Conditions.Apply(CharConditionId.Slow);

            TacticalCombatSession session = new TacticalCombatSession(grid, units);
            IReadOnlyList<InitiativeEntry> ribbon = session.GetInitiativeRibbon(8);

            Assert.Equal(8, ribbon.Count);
            Assert.True(ribbon[0].IsCurrentTurn);
            Assert.Equal("h6", ribbon[0].CombatantId); // Vex Dex 20
            Assert.Equal("m1", ribbon[1].CombatantId); // Goblin Dex 18
            Assert.Equal("m2", ribbon[2].CombatantId); // Frog Dex 16
            Assert.Equal("h4", ribbon[3].CombatantId); // Theron Dex 15
            // Next 3 are Dex 14 (h1, h2, h3 sorted by Id)
            Assert.Equal("h1", ribbon[4].CombatantId);
            Assert.Equal("h2", ribbon[5].CombatantId);
            Assert.Equal("h3", ribbon[6].CombatantId);
            Assert.Equal("h5", ribbon[7].CombatantId); // Seraphina Dex 11
        }

        [Fact]
        public void MoveAndUndo_WorksBeforeStandardSwiftOrAoOAndLocksAfterwards()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            Dictionary<int, ItemDefinition> items = new Dictionary<int, ItemDefinition>
            {
                [10] = new ItemDefinition(10, "Potion", category: "Potion", healDice: "1d4+2")
            };

            CombatantState hero = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(2, 2), 15, 25, movementMax: 6, dexterity: 20);
            CombatantState enemy = new CombatantState("m1", "Frog", CombatSide.Monster, new GridCoord(7, 7), 12, 12, dexterity: 10);

            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { hero, enemy }, items: items);

            // Free move + undo before Standard/Swift/AoO (4 base MP -> 2 MP remaining after 2-tile step).
            Assert.False(session.CanUndoMove);
            Assert.True(session.TryMoveTo(new GridCoord(4, 2)).Success);
            Assert.Equal(new GridCoord(4, 2), hero.Position);
            Assert.Equal(2, hero.Economy.MoveRemaining);
            Assert.True(session.CanUndoMove);

            Assert.True(session.UndoMove());
            Assert.Equal(new GridCoord(2, 2), hero.Position);
            Assert.Equal(4, hero.Economy.MoveRemaining);
            Assert.False(session.CanUndoMove);

            // Swift action locks session-level UndoMove.
            Assert.True(session.TryMoveTo(new GridCoord(3, 2)).Success);
            Assert.True(session.CanUndoMove);
            Assert.True(session.UseSwiftPotion(10).Success);
            Assert.False(session.CanUndoMove);
            Assert.False(session.UndoMove());

            // Next turn: Standard action (GuardOverwatch) locks UndoMove.
            session.EndTurn(); // m1 turn
            session.ExecuteMonsterAiTurn(); // advances back to h1
            Assert.Equal("h1", session.CurrentActorId);
            Assert.True(session.TryMoveTo(new GridCoord(4, 2)).Success);
            Assert.True(session.GuardOverwatch().Success);
            Assert.False(session.CanUndoMove);
        }

        [Fact]
        public void AoO_TriggersWhenDisengagingWithoutInvisibilityAndSkipsWhenInvisible()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            CombatantState hero = new CombatantState("h1", "Vex", CombatSide.Hero, new GridCoord(3, 3), 25, 25, ac: 0, movementMax: 6, dexterity: 20);
            CombatantState goblin = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(4, 3), 20, 20, toHit: 40, dexterity: 10, weaponDice: "1d6+2");

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero, goblin },
                roller: new QueuedDiceRoller(20, 4)); // AoO hit roll 20, dmg 4 (+2 = 6)

            // Verify threatened tiles around goblin (4,3).
            IReadOnlyList<GridCoord> threatened = session.GetThreatenedTiles("m1");
            Assert.Equal(8, threatened.Count);
            Assert.Contains(new GridCoord(3, 3), threatened);

            // Verify reachable tile out of goblin ring flags ProvokesAoO = true.
            IReadOnlyList<ReachableTileInfo> reachable = session.GetReachableTiles("h1");
            ReachableTileInfo? disengageTile = null;
            for (int i = 0; i < reachable.Count; i++)
            {
                if (reachable[i].Coord == new GridCoord(2, 3))
                {
                    disengageTile = reachable[i];
                    break;
                }
            }

            Assert.NotNull(disengageTile);
            Assert.True(disengageTile!.ProvokesAoO);
            Assert.Contains("m1", disengageTile.ThreateningEnemyIds);

            // Stepping out of goblin's 3x3 ring triggers AoO and locks UndoMove!
            CombatActionResult moveRes = session.TryMoveTo(new GridCoord(2, 3));
            Assert.True(moveRes.Success);
            Assert.True(moveRes.AoOTriggered);
            Assert.Equal(6, moveRes.DamageDealt);
            Assert.Equal(19, hero.Hp);
            Assert.False(session.CanUndoMove);

            // Next hero turn with Invisible -> disengaging does NOT provoke AoO.
            session.EndTurn();
            session.EndTurn();
            hero.Conditions.Apply(CharConditionId.Invisible);
            Assert.False(session.WouldStepProvokeAoO("h1", hero.Position, new GridCoord(1, 3)));
            Assert.True(session.TryMoveTo(new GridCoord(3, 3)).Success); // step adjacent to m1
            CombatActionResult safeDisengage = session.TryMoveTo(new GridCoord(2, 3));
            Assert.False(safeDisengage.AoOTriggered);
        }

        [Fact]
        public void LineOfSight_DetectsClearPartialCoverAndBlockedByWall()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            grid.SetBlocked(4, 2, blocksLos: true);     // Wall blocks LOS
            grid.SetCover(4, 5, moveCost: 2, blocksLos: false); // Forest/cover gives 15% partial cover

            CombatantState archer = new CombatantState("h1", "Kaelen", CombatSide.Hero, new GridCoord(1, 2), 15, 15, missileSkill: 20, rangedDice: "1d6", rangedRange: 8);
            CombatantState behindWall = new CombatantState("m1", "Orc1", CombatSide.Monster, new GridCoord(7, 2), 15, 15);
            CombatantState behindForest = new CombatantState("m2", "Orc2", CombatSide.Monster, new GridCoord(7, 5), 15, 15);
            CombatantState openTarget = new CombatantState("m3", "Orc3", CombatSide.Monster, new GridCoord(1, 7), 15, 15);

            // Shift archer y to test unobstructed ray to m3 at (1, 7) and partial cover to m2.
            archer.Position = new GridCoord(1, 5);
            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { archer, behindWall, behindForest, openTarget });

            LosResult clear = session.CheckLineOfSight(archer.Position, openTarget.Position);
            Assert.Equal(LosKind.Clear, clear.Kind);
            Assert.Equal(0, clear.CoverPenaltyPct);

            LosResult cover = session.CheckLineOfSight(archer.Position, behindForest.Position);
            Assert.Equal(LosKind.PartialCover, cover.Kind);
            Assert.Equal(15, cover.CoverPenaltyPct);
            TacticalAttackPreview coverPreview = session.PreviewRangedAttack("h1", "m2");
            Assert.True(coverPreview.CanExecute);
            Assert.Contains("PARTIAL_COVER", coverPreview.ConditionTags);

            archer.Position = new GridCoord(1, 2);
            LosResult blocked = session.CheckLineOfSight(archer.Position, behindWall.Position);
            Assert.Equal(LosKind.Blocked, blocked.Kind);
            Assert.Equal(new GridCoord(4, 2), blocked.BlockedBy);
            TacticalAttackPreview blockedPreview = session.PreviewRangedAttack("h1", "m1");
            Assert.False(blockedPreview.CanExecute);
        }

        [Fact]
        public void FlankingAndCombatPreview_MatchActualSeededResolution()
        {
            CombatGrid grid = new CombatGrid(8, 8);
            CombatantState rogue = new CombatantState("h1", "Vex", CombatSide.Hero, new GridCoord(3, 4), 16, 16, toHit: 10, dexterity: 20, weaponDice: "1d6+1", weaponBonus: 1);
            CombatantState knight = new CombatantState("h2", "Aldric", CombatSide.Hero, new GridCoord(5, 4), 20, 20, toHit: 15, dexterity: 12);
            CombatantState orc = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(4, 4), 20, 20, ac: 15, dexterity: 10);

            rogue.Conditions.Apply(CharConditionId.Strong); // +15 hit, +3 dmg

            // Seeded roller: roll 70 (base 50 + 10 toHit + 20 flank + 5 weapon + 15 strong - 15 AC = 85% -> 70 is a Hit!), dmg roll 4 (+1 + 3 Strong = 8).
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { rogue, knight, orc },
                roller: new QueuedDiceRoller(70, 4));

            Assert.True(session.IsFlanking("h1", "m1"));
            TacticalAttackPreview preview = session.PreviewMeleeAttack("h1", "m1");
            Assert.True(preview.CanExecute);
            Assert.True(preview.IsFlanking);
            Assert.Equal(85, preview.HitChancePct);
            Assert.Equal(5, preview.CritChancePct);
            Assert.Equal(5, preview.MinDamage);  // 1d6+1 (min 2) + 3 Strong = 5
            Assert.Equal(10, preview.MaxDamage); // 1d6+1 (max 7) + 3 Strong = 10
            Assert.Contains("FLANKING", preview.ConditionTags);
            Assert.Contains("COND_STRONG", preview.ConditionTags);

            CombatActionResult res = session.ExecuteMeleeAttack("m1");
            Assert.True(res.Success);
            Assert.Equal(AttackOutcomeKind.Hit, res.AttackOutcomes[0].Kind);
            Assert.Equal(8, res.DamageDealt);
            Assert.InRange(res.DamageDealt, preview.MinDamage, preview.MaxDamage);
        }

        [Fact]
        public void SpellAoeFootprint_ClassifiesAlliesAndEnemiesAndBlocksWalls()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            grid.SetBlocked(6, 5, blocksLos: true);

            Dictionary<int, SpellDefinition> spells = BuildTestSpells();
            CombatantState mage = new CombatantState("h1", "Theron", CombatSide.Hero, new GridCoord(2, 5), 12, 12, sp: 20, maxSp: 20, spellIds: new[] { 1, 2 }, dexterity: 20);
            CombatantState knight = new CombatantState("h2", "Aldric", CombatSide.Hero, new GridCoord(5, 4), 20, 20, dexterity: 15);
            CombatantState goblin1 = new CombatantState("m1", "Goblin1", CombatSide.Monster, new GridCoord(5, 5), 10, 10, dexterity: 12);
            CombatantState goblin2 = new CombatantState("m2", "Goblin2", CombatSide.Monster, new GridCoord(6, 6), 10, 10, dexterity: 11);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { mage, knight, goblin1, goblin2 },
                spells: spells,
                roller: new QueuedDiceRoller(50, 3, 4, 5, 50, 2, 2, 2, 50, 3, 3, 3));

            SpellAoeFootprint footprint = session.GetSpellAoeFootprint("fireball", new GridCoord(5, 5), "h1");
            Assert.True(footprint.InRange);
            Assert.Contains("h2", footprint.AffectedAllyIds);
            Assert.Contains("m1", footprint.AffectedEnemyIds);
            Assert.Contains("m2", footprint.AffectedEnemyIds);
            Assert.Contains(new GridCoord(6, 5), footprint.BlockedTiles);

            TacticalSpellPreview preview = session.PreviewSpell("h1", "fireball", new GridCoord(5, 5));
            Assert.True(preview.CanExecute);
            Assert.Equal(8, preview.SpCost);
            Assert.Equal(3, preview.MinAmount);
            Assert.Equal(18, preview.MaxAmount);
        }

        [Fact]
        public void CrowdControlEscalationAndSoftCurse_BehavePerRulesetMode()
        {
            CombatGrid grid = new CombatGrid(8, 8);
            Dictionary<int, SpellDefinition> spells = BuildTestSpells();

            CombatantState modernMage = new CombatantState("h1", "Theron", CombatSide.Hero, new GridCoord(1, 1), 15, 15, sp: 20, maxSp: 20, spellIds: new[] { 2 }, dexterity: 20);
            CombatantState modernTarget = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(3, 1), 20, 20, ac: 15, toHit: 10, dexterity: 10);
            TacticalCombatSession modern = new TacticalCombatSession(grid, new[] { modernMage, modernTarget }, spells: spells, roller: new QueuedDiceRoller(50, 25), mode: RulesetMode.ModernBalanced);

            // Sandman applies Helpless (CC -> +15%/round escalation).
            Assert.True(modern.CastSpell("sandman", modernTarget.Position).Success);
            Assert.True(modernTarget.Conditions.Has(CharConditionId.Helpless));
            Assert.Equal(20, modernTarget.Conditions.GetRecoverySaveChance(CharConditionId.Helpless, RulesetMode.ModernBalanced));
            Assert.Contains("COND_HELPLESS", modernTarget.Conditions.GetDisplayEffects()[0].Code);

            modern.EndTurn(); // h1 ends
            modern.EndTurn(); // m1 fails save on roll 25 (>20), ElapsedRounds becomes 1
            Assert.Equal(35, modernTarget.Conditions.GetRecoverySaveChance(CharConditionId.Helpless, RulesetMode.ModernBalanced));
            Assert.Equal(20, modernTarget.Conditions.GetRecoverySaveChance(CharConditionId.Helpless, RulesetMode.Classic1994Strict));

            // Softer Curse in ModernBalanced vs permanent drain in Classic1994Strict.
            modern.ApplyDrainOrAgeCurse("h1", stacks: 2);
            Assert.Equal(2, modernMage.CurseStacks);
            Assert.Equal(15, modernMage.MaxHp); // MaxHp untouched in ModernBalanced

            CombatantState classicHero = new CombatantState("h1", "Theron", CombatSide.Hero, new GridCoord(1, 1), 15, 15, ac: 10, toHit: 10, dexterity: 20);
            CombatantState classicMob = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(3, 1), 20, 20, dexterity: 10);
            TacticalCombatSession classic = new TacticalCombatSession(grid, new[] { classicHero, classicMob }, mode: RulesetMode.Classic1994Strict);
            classic.ApplyDrainOrAgeCurse("h1", stacks: 2);
            Assert.Equal(0, classicHero.CurseStacks);
            Assert.Equal(11, classicHero.MaxHp);
            Assert.Equal(8, classicHero.Ac);
            Assert.Equal(6, classicHero.ToHit);
        }

        [Fact]
        public void PowerLevelOvercasting_ScalesSpCostDiceDuration_AndRejectsUnaffordablePower()
        {
            CombatGrid grid = new CombatGrid(8, 8);
            Dictionary<int, SpellDefinition> spells = BuildTestSpells();

            CombatantState mage = new CombatantState("h1", "Theron", CombatSide.Hero, new GridCoord(1, 3), 15, 15, sp: 24, maxSp: 24, spellIds: new[] { 1, 2 }, dexterity: 20);
            CombatantState gob = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(4, 3), 40, 40, ac: 10, magicResistance: 30, dexterity: 10);
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { mage, gob },
                spells: spells,
                roller: new QueuedDiceRoller(90, 4, 4, 4, 4, 4)); // high roll fails save; 5d6 damage rolls

            TacticalSpellPreview p1 = session.PreviewSpell("h1", "fireball", gob.Position, powerLevel: 1);
            TacticalSpellPreview p2 = session.PreviewSpell("h1", "fireball", gob.Position, powerLevel: 2);
            TacticalSpellPreview p4 = session.PreviewSpell("h1", "fireball", gob.Position, powerLevel: 4); // clamped to 3x (24 SP)

            Assert.Equal(1, p1.PowerLevel);
            Assert.Equal(8, p1.SpCost);
            Assert.Equal(3, p1.MinAmount);
            Assert.Equal(18, p1.MaxAmount);

            Assert.Equal(2, p2.PowerLevel);
            Assert.Equal(16, p2.SpCost);
            Assert.Equal(5, p2.MinAmount);  // 3d6 -> +2d6 = 5d6
            Assert.Equal(30, p2.MaxAmount);
            Assert.Contains("Power 2x", p2.ConditionTags);
            Assert.True(p2.SaveChancePct < p1.SaveChancePct); // +8% DC bonus

            Assert.Equal(3, p4.PowerLevel);
            Assert.Equal(24, p4.SpCost);
            Assert.Equal(7, p4.MinAmount);

            // Casting 2x spends 16 SP and deals 5d6 (20) damage cleanly.
            CombatActionResult castRes = session.CastSpell("fireball", gob.Position, powerLevel: 2);
            Assert.True(castRes.Success);
            Assert.Contains("[2x]", castRes.Message);
            Assert.Equal(8, mage.Sp);
            Assert.Equal(20, gob.Hp);

            // Casting 2x again (needs 16 SP, only 8 left) is rejected cleanly.
            CombatActionResult failRes = session.CastSpell("fireball", gob.Position, powerLevel: 2);
            Assert.False(failRes.Success);
            Assert.Contains("SP", failRes.Message);
        }
    }
}
