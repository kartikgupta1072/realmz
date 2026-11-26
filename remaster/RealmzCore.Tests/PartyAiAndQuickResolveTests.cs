using System.Collections.Generic;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using Xunit;

namespace RealmzCore.Tests
{
    public class PartyAiAndQuickResolveTests
    {
        private static Dictionary<int, SpellDefinition> BuildCombatSpells()
        {
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
                targetMode: "AreaOfEffect",
                spellAreaIndex: 0,
                aoeOffsets: box3x3,
                effectKind: "Damage",
                effectDice: "3d6",
                damageType: "Fire",
                baseRecoveryPct: 20,
                saveKind: "Magic");

            SpellDefinition magicDarts = new SpellDefinition(
                id: 2,
                key: "magic_darts",
                name: "Magic Darts",
                school: "Sorcerer",
                level: 1,
                spCost: 2,
                range: 8,
                targetMode: "Single",
                spellAreaIndex: 0,
                aoeOffsets: new[] { new GridCoord(0, 0) },
                effectKind: "Damage",
                effectDice: "1d4+1",
                damageType: "Magic",
                baseRecoveryPct: 0,
                saveKind: "Magic");

            SpellDefinition heal = new SpellDefinition(
                id: 3,
                key: "heal",
                name: "Minor Heal",
                school: "Priest",
                level: 1,
                spCost: 4,
                range: 6,
                targetMode: "Single",
                spellAreaIndex: 0,
                aoeOffsets: new[] { new GridCoord(0, 0) },
                effectKind: "Heal",
                effectDice: "2d4+2",
                damageType: "",
                baseRecoveryPct: 0,
                saveKind: "Magic");

            SpellDefinition cure = new SpellDefinition(
                id: 4,
                key: "cure",
                name: "Cure Affliction",
                school: "Priest",
                level: 1,
                spCost: 3,
                range: 6,
                targetMode: "Single",
                spellAreaIndex: 0,
                aoeOffsets: new[] { new GridCoord(0, 0) },
                effectKind: "Cure",
                effectDice: "",
                damageType: "",
                baseRecoveryPct: 0,
                saveKind: "Magic");

            return new Dictionary<int, SpellDefinition>
            {
                [1] = fireball,
                [2] = magicDarts,
                [3] = heal,
                [4] = cure
            };
        }

        [Fact]
        public void Cleric_HealsAllyBelow65PctHp_InsteadOfAttacking()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            Dictionary<int, SpellDefinition> spells = BuildCombatSpells();

            CombatantState cleric = new CombatantState(
                "h1", "Liora", CombatSide.Hero, new GridCoord(2, 4),
                hp: 20, maxHp: 20, sp: 12, maxSp: 12,
                toHit: 25, dexterity: 20, weaponDice: "1d6+1",
                spellIds: new[] { 3 });

            // Knight is at 12/24 HP (50% < 65%), standing adjacent to both cleric and goblin.
            CombatantState knight = new CombatantState(
                "h2", "Aldric", CombatSide.Hero, new GridCoord(3, 4),
                hp: 12, maxHp: 24, dexterity: 10);

            // Cleric is also adjacent to a goblin, so melee attack would be legal, yet Heal must take priority.
            CombatantState goblin = new CombatantState(
                "m1", "Goblin", CombatSide.Monster, new GridCoord(2, 5),
                hp: 10, maxHp: 10, dexterity: 8);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { cleric, knight, goblin },
                spells: spells,
                roller: new QueuedDiceRoller(3, 4)); // 2d4+2 = 9 healing

            CombatActionResult res = session.ExecutePartyAiTurn();
            Assert.True(res.Success);
            Assert.Equal(21, knight.Hp);
            Assert.Equal(8, cleric.Sp);
            Assert.Equal(10, goblin.Hp); // Did not attack goblin
            Assert.Equal("h2", session.CurrentActorId);
        }

        [Fact]
        public void NonCaster_UsesSwiftPotion_OnCriticallyInjuredAllyBelow50Pct()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            Dictionary<int, ItemDefinition> items = new Dictionary<int, ItemDefinition>
            {
                [10] = new ItemDefinition(10, "Healing Potion", category: "Potion", healDice: "1d4+4")
            };

            PartyMemberState m1 = new PartyMemberState(new HeroPresetData("h1", "Aldric", hp: 24, maxHp: 24, toHit: 25));
            PartyMemberState m2 = new PartyMemberState(new HeroPresetData("h2", "Vex", hp: 8, maxHp: 20));
            PartyState party = new PartyState(new[] { m1, m2 });
            party.AddItemToStash(10, 1);

            CombatantState knight = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(2, 4), 24, 24, toHit: 25, dexterity: 20, weaponDice: "1d6");
            CombatantState rogue = new CombatantState("h2", "Vex", CombatSide.Hero, new GridCoord(5, 5), 8, 20, dexterity: 10);
            CombatantState goblin = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(3, 4), 20, 20, ac: 5, dexterity: 8);

            // Swift potion roll (3 + 4 = 7), then melee attack roll (hit 50, dmg 4).
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { knight, rogue, goblin },
                items: items,
                roller: new QueuedDiceRoller(3, 50, 4),
                party: party);

            CombatActionResult res = session.ExecutePartyAiTurn();
            Assert.True(res.Success);
            Assert.Equal(15, rogue.Hp);
            Assert.Empty(party.Stash);
            Assert.Equal(16, goblin.Hp); // Still executed standard melee attack after swift potion
        }

        [Fact]
        public void Cleric_CuresAfflictedAlly_WhenNoUrgentHealOrAoe()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            Dictionary<int, SpellDefinition> spells = BuildCombatSpells();

            CombatantState cleric = new CombatantState(
                "h1", "Liora", CombatSide.Hero, new GridCoord(2, 2),
                hp: 20, maxHp: 20, sp: 10, maxSp: 10, dexterity: 20,
                spellIds: new[] { 3, 4 });
            CombatantState rogue = new CombatantState(
                "h2", "Vex", CombatSide.Hero, new GridCoord(3, 2),
                hp: 20, maxHp: 20, dexterity: 12);
            rogue.Conditions.Apply(CharConditionId.Poisoned, magnitude: 2, turnsRemaining: 3);

            CombatantState orc = new CombatantState(
                "m1", "Orc", CombatSide.Monster, new GridCoord(8, 8),
                hp: 20, maxHp: 20, dexterity: 8);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { cleric, rogue, orc },
                spells: spells,
                roller: new DeterministicDiceRoller(11UL));

            CombatActionResult res = session.ExecutePartyAiTurn();
            Assert.True(res.Success);
            Assert.False(rogue.Conditions.Has(CharConditionId.Poisoned));
            Assert.Equal(7, cleric.Sp);
        }

        [Fact]
        public void Mage_CastsFireballWhenZeroAlliesInAoe_AndFallsBackToMagicDartsWhenFlanked()
        {
            Dictionary<int, SpellDefinition> spells = BuildCombatSpells();

            // Case 1: Clean 2-enemy cluster far from allies -> Fireball!
            CombatGrid cleanGrid = new CombatGrid(12, 12);
            CombatantState mage1 = new CombatantState(
                "h1", "Theron", CombatSide.Hero, new GridCoord(1, 5),
                hp: 15, maxHp: 15, sp: 20, maxSp: 20, dexterity: 20,
                spellIds: new[] { 1, 2 });
            CombatantState knight1 = new CombatantState(
                "h2", "Aldric", CombatSide.Hero, new GridCoord(1, 4),
                hp: 25, maxHp: 25, dexterity: 12);
            CombatantState goblin1 = new CombatantState("m1", "Goblin1", CombatSide.Monster, new GridCoord(7, 5), 14, 14, dexterity: 8);
            CombatantState goblin2 = new CombatantState("m2", "Goblin2", CombatSide.Monster, new GridCoord(8, 5), 14, 14, dexterity: 7);

            TacticalCombatSession cleanSession = new TacticalCombatSession(
                cleanGrid,
                new[] { mage1, knight1, goblin1, goblin2 },
                spells: spells,
                roller: new DeterministicDiceRoller(42UL));

            CombatActionResult fireballRes = cleanSession.ExecutePartyAiTurn();
            Assert.True(fireballRes.Success);
            Assert.Contains("Fireball", fireballRes.Message);
            Assert.True(mage1.Sp <= 12); // Cast Fireball (8 SP) plus optional follow-up spell from 6 AP pool
            Assert.Equal(25, knight1.Hp); // Zero friendly fire

            // Case 2: Two heroes flank the enemies on all sides so any 3x3 Fireball hits an ally -> Magic Darts!
            CombatGrid flankedGrid = new CombatGrid(12, 12);
            CombatantState mage2 = new CombatantState(
                "h1", "Theron", CombatSide.Hero, new GridCoord(1, 5),
                hp: 15, maxHp: 15, sp: 20, maxSp: 20, dexterity: 20,
                spellIds: new[] { 1, 2 });
            // Place heroes around (6,5) and (7,5) so every 3x3 center hitting either goblin includes at least one ally.
            CombatantState heroNorth = new CombatantState("h2", "Aldric", CombatSide.Hero, new GridCoord(6, 4), 25, 25, dexterity: 12);
            CombatantState heroSouth = new CombatantState("h3", "Vex", CombatSide.Hero, new GridCoord(7, 6), 20, 20, dexterity: 11);
            CombatantState flankedGoblin1 = new CombatantState("m1", "Goblin1", CombatSide.Monster, new GridCoord(6, 5), 14, 14, dexterity: 8);
            CombatantState flankedGoblin2 = new CombatantState("m2", "Goblin2", CombatSide.Monster, new GridCoord(7, 5), 14, 14, dexterity: 7);

            TacticalCombatSession flankedSession = new TacticalCombatSession(
                flankedGrid,
                new[] { mage2, heroNorth, heroSouth, flankedGoblin1, flankedGoblin2 },
                spells: spells,
                roller: new DeterministicDiceRoller(42UL));

            CombatActionResult dartsRes = flankedSession.ExecutePartyAiTurn();
            Assert.True(dartsRes.Success);
            Assert.Contains("Magic Darts", dartsRes.Message);
            Assert.Equal(16, mage2.Sp); // Paid 4 SP for two 3-AP Magic Darts casts from 6 AP pool
            Assert.Equal(25, heroNorth.Hp);
            Assert.Equal(20, heroSouth.Hp);
        }

        [Fact]
        public void Mage_KitesAwaySafely_WhenOutOfSpAndAdjacentToEnemy()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            Dictionary<int, SpellDefinition> spells = BuildCombatSpells();

            CombatantState mage = new CombatantState(
                "h1", "Theron", CombatSide.Hero, new GridCoord(5, 5),
                hp: 14, maxHp: 14, sp: 0, maxSp: 20, movementMax: 6, dexterity: 20,
                spellIds: new[] { 1, 2 });
            // Make mage Invisible (or spend enemy reaction) so !ProvokesAoO kite tile is available.
            mage.Conditions.Apply(CharConditionId.Invisible);

            CombatantState orc = new CombatantState(
                "m1", "Orc", CombatSide.Monster, new GridCoord(6, 5),
                hp: 20, maxHp: 20, dexterity: 10);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { mage, orc },
                spells: spells,
                roller: new DeterministicDiceRoller(99UL));

            CombatActionResult res = session.ExecutePartyAiTurn();
            Assert.True(res.Success);
            Assert.True(mage.Position.ChebyshevDistance(orc.Position) >= 2);
            Assert.True(mage.IsGuarding);
        }

        [Fact]
        public void MeleeHero_AvoidsFireUnlessKillingBlow_AndPrefersFlanking()
        {
            // Case 1: Avoid Fire tile when non-killing blow and safe tile exists; also prefer Flanking!
            CombatGrid grid = new CombatGrid(10, 10);
            grid.SetSurface(5, 4, SurfaceType.Fire, -1); // Fire tile adjacent to enemy

            // Rogue at (4,3), Knight ally at (4,5) -> Orc at (5,5).
            // (6,5) is opposite (4,5) across Orc (5,5) -> Flanking! And it is safe (no Fire).
            // (5,4) is Fire (must be avoided for non-killing blow).
            CombatantState rogue = new CombatantState(
                "h1", "Vex", CombatSide.Hero, new GridCoord(4, 3),
                hp: 18, maxHp: 18, toHit: 20, movementMax: 6, dexterity: 20, weaponDice: "1d6", brawn: 2);
            CombatantState knight = new CombatantState(
                "h2", "Aldric", CombatSide.Hero, new GridCoord(4, 5),
                hp: 24, maxHp: 24, dexterity: 12);
            CombatantState orc = new CombatantState(
                "m1", "Orc", CombatSide.Monster, new GridCoord(5, 5),
                hp: 30, maxHp: 30, ac: 5, dexterity: 10, brawn: 16);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { rogue, knight, orc },
                roller: new DeterministicDiceRoller(77UL));

            CombatActionResult res = session.ExecutePartyAiTurn();
            Assert.True(res.Success);
            Assert.Equal(18, rogue.Hp); // Did not step in Fire
            Assert.NotEqual(new GridCoord(5, 4), rogue.Position);
            Assert.True(session.IsFlanking("h1", "m1")); // Moved to flanking position
            Assert.True(orc.Hp < 30);

            // Case 2: Only reachable attack tile is Fire, AND enemy has 3 HP (killing blow!).
            CombatGrid fireOnlyGrid = new CombatGrid(8, 8);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int tx = 5 + dx;
                    int ty = 5 + dy;
                    if (tx == 4 && ty == 5)
                    {
                        fireOnlyGrid.SetSurface(tx, ty, SurfaceType.Fire, -1);
                    }
                    else
                    {
                        fireOnlyGrid.SetBlocked(tx, ty);
                    }
                }
            }

            CombatantState finisher = new CombatantState(
                "h1", "Aldric", CombatSide.Hero, new GridCoord(2, 5),
                hp: 25, maxHp: 25, toHit: 30, movementMax: 6, dexterity: 20, weaponDice: "1d8+2");
            CombatantState dyingOrc = new CombatantState(
                "m1", "Orc", CombatSide.Monster, new GridCoord(5, 5),
                hp: 3, maxHp: 20, ac: 5, dexterity: 10);

            TacticalCombatSession finishSession = new TacticalCombatSession(
                fireOnlyGrid,
                new[] { finisher, dyingOrc },
                roller: new DeterministicDiceRoller(55UL));

            CombatActionResult finishRes = finishSession.ExecutePartyAiTurn();
            Assert.True(finishRes.Success);
            Assert.Equal(new GridCoord(4, 5), finisher.Position);
            Assert.False(dyingOrc.IsAlive);
            Assert.Equal(CombatOutcome.Victory, finishSession.Outcome);
        }

        [Fact]
        public void MeleeHero_UsesSwiftShove_ToPushEnemyOffLedgeOrIntoFire()
        {
            // Case 1: Push enemy off a High Ground ledge!
            CombatGrid ledgeGrid = new CombatGrid(10, 10);
            ledgeGrid.SetElevation(3, 4, 1);
            ledgeGrid.SetElevation(3, 5, 1);
            ledgeGrid.SetElevation(3, 6, 1);
            ledgeGrid.SetElevation(4, 5, 1);
            ledgeGrid.SetElevation(5, 4, 1);
            ledgeGrid.SetElevation(5, 6, 1);
            ledgeGrid.SetElevation(5, 5, 0); // Unique ledge drop at (5,5)!

            CombatantState knight = new CombatantState(
                "h1", "Aldric", CombatSide.Hero, new GridCoord(2, 5),
                hp: 25, maxHp: 25, toHit: 25, movementMax: 6, dexterity: 20,
                weaponDice: "1d6", brawn: 25); // High brawn ensures >= 50% shove chance
            CombatantState goblin = new CombatantState(
                "m1", "Goblin", CombatSide.Monster, new GridCoord(4, 5),
                hp: 20, maxHp: 20, ac: 5, dexterity: 10, brawn: 8);

            // Roll 10 (shove succeeds <= 85%), fall 1d6 = 4, then if still reachable melee rolls.
            TacticalCombatSession ledgeSession = new TacticalCombatSession(
                ledgeGrid,
                new[] { knight, goblin },
                roller: new DeterministicDiceRoller(7UL));

            CombatActionResult ledgeRes = ledgeSession.ExecutePartyAiTurn();
            Assert.True(ledgeRes.Success);
            Assert.Equal(new GridCoord(5, 5), goblin.Position);
            Assert.Contains(ledgeSession.Log, e => e.ActionType == "Shove");

            // Case 2: Push enemy into Fire!
            CombatGrid fireGrid = new CombatGrid(10, 10);
            fireGrid.SetSurface(5, 5, SurfaceType.Fire, -1);

            CombatantState paladin = new CombatantState(
                "h1", "Seraphina", CombatSide.Hero, new GridCoord(2, 5),
                hp: 25, maxHp: 25, toHit: 25, movementMax: 6, dexterity: 20,
                weaponDice: "1d6", brawn: 25);
            CombatantState orc = new CombatantState(
                "m1", "Orc", CombatSide.Monster, new GridCoord(4, 5),
                hp: 20, maxHp: 20, ac: 5, dexterity: 10, brawn: 8);

            TacticalCombatSession fireSession = new TacticalCombatSession(
                fireGrid,
                new[] { paladin, orc },
                roller: new DeterministicDiceRoller(19UL));

            CombatActionResult fireRes = fireSession.ExecutePartyAiTurn();
            Assert.True(fireRes.Success);
            Assert.Equal(new GridCoord(5, 5), orc.Position);
            Assert.Contains(fireSession.Log, e => e.ActionType == "Shove");
            Assert.True(orc.Hp < 20);
        }

        [Fact]
        public void Ranger_MovesToHighGround_AndKitesAwayFromAdjacentFoeToShoot()
        {
            // Case 1: Ranger moves to reachable High Ground (+15% elevation + clear LOS) and shoots!
            CombatGrid hgGrid = new CombatGrid(12, 12);
            hgGrid.SetElevation(3, 5, 1); // High ground reachable from (1,5)

            CombatantState ranger = new CombatantState(
                "h1", "Kaelen", CombatSide.Hero, new GridCoord(1, 5),
                hp: 18, maxHp: 18, missileSkill: 25, movementMax: 6, dexterity: 20,
                rangedDice: "1d8+1", rangedRange: 8);
            CombatantState orc = new CombatantState(
                "m1", "Orc", CombatSide.Monster, new GridCoord(8, 5),
                hp: 20, maxHp: 20, ac: 5, dexterity: 10);

            TacticalCombatSession hgSession = new TacticalCombatSession(
                hgGrid,
                new[] { ranger, orc },
                roller: new QueuedDiceRoller(40, 5, 45, 4));

            CombatActionResult hgRes = hgSession.ExecutePartyAiTurn();
            Assert.True(hgRes.Success);
            Assert.Equal(new GridCoord(3, 5), ranger.Position);
            Assert.Equal(1, hgGrid.GetElevation(ranger.Position));
            Assert.True(orc.Hp < 20);
            Assert.Contains(hgSession.Log, e => e.ActionType == "RangedAttack");

            // Case 2: Ranger adjacent to foe kites away cleanly (without AoO) and shoots!
            CombatGrid kiteGrid = new CombatGrid(12, 12);
            CombatantState pinnedRanger = new CombatantState(
                "h1", "Kaelen", CombatSide.Hero, new GridCoord(5, 5),
                hp: 18, maxHp: 18, missileSkill: 25, movementMax: 6, dexterity: 20,
                rangedDice: "1d8", rangedRange: 8);
            CombatantState adjacentGoblin = new CombatantState(
                "m1", "Goblin", CombatSide.Monster, new GridCoord(6, 5),
                hp: 15, maxHp: 15, ac: 5, dexterity: 10);
            // Goblin already spent its reaction -> !ProvokesAoO kite is available.
            adjacentGoblin.Economy.CommitReaction();

            TacticalCombatSession kiteSession = new TacticalCombatSession(
                kiteGrid,
                new[] { pinnedRanger, adjacentGoblin },
                roller: new QueuedDiceRoller(40, 6, 42, 4));

            CombatActionResult kiteRes = kiteSession.ExecutePartyAiTurn();
            Assert.True(kiteRes.Success);
            Assert.True(pinnedRanger.Position.ChebyshevDistance(adjacentGoblin.Position) >= 2);
            Assert.Equal(5, adjacentGoblin.Hp); // 15 - (6 + 4) across two 3-AP ranged shots from 6 AP pool
            Assert.Contains(kiteSession.Log, e => e.ActionType == "RangedAttack");
        }

        [Fact]
        public void QuickResolve_EvaluatesCorrectly_StepsAutoCombat_AndGrantsVictoryRewards()
        {
            // Case 1: Strong party vs 2 weak rats -> ShouldOfferQuickResolve == true & QuickResolveCombat -> Victory + XP/Gold/Loot!
            CombatGrid easyGrid = new CombatGrid(10, 10);
            Dictionary<int, ItemDefinition> items = new Dictionary<int, ItemDefinition>
            {
                [50] = new ItemDefinition(50, "Rat Tail", category: "Junk", isJunk: true)
            };

            PartyMemberState pm1 = new PartyMemberState(new HeroPresetData("h1", "Aldric", hp: 30, maxHp: 30, toHit: 24));
            PartyMemberState pm2 = new PartyMemberState(new HeroPresetData("h2", "Kaelen", hp: 24, maxHp: 24, toHit: 22));
            PartyState party = new PartyState(new[] { pm1, pm2 }, gold: 10);

            CombatantState hero1 = new CombatantState(
                "h1", "Aldric", CombatSide.Hero, new GridCoord(1, 4),
                hp: 30, maxHp: 30, ac: 16, toHit: 24, movementMax: 6, dexterity: 18,
                numAttacks: 2, weaponDice: "1d8+2");
            CombatantState hero2 = new CombatantState(
                "h2", "Kaelen", CombatSide.Hero, new GridCoord(1, 6),
                hp: 24, maxHp: 24, ac: 15, missileSkill: 22, movementMax: 6, dexterity: 16,
                rangedDice: "1d8+1", rangedRange: 8);
            CombatantState rat1 = new CombatantState(
                "m1", "Giant Rat 1", CombatSide.Monster, new GridCoord(7, 4),
                hp: 5, maxHp: 5, ac: 8, toHit: 4, movementMax: 5, dexterity: 12,
                weaponDice: "1d4", xpReward: 25, goldReward: 4, lootItemIds: new[] { 50 });
            CombatantState rat2 = new CombatantState(
                "m2", "Giant Rat 2", CombatSide.Monster, new GridCoord(7, 6),
                hp: 5, maxHp: 5, ac: 8, toHit: 4, movementMax: 5, dexterity: 10,
                weaponDice: "1d4", xpReward: 25, goldReward: 6, lootItemIds: new[] { 50 });

            TacticalCombatSession easySession = new TacticalCombatSession(
                easyGrid,
                new[] { hero1, hero2, rat1, rat2 },
                items: items,
                roller: new DeterministicDiceRoller(12345UL),
                party: party);

            QuickResolveSummary easySummary = easySession.EvaluateQuickResolve();
            Assert.True(easySummary.CanQuickResolve);
            Assert.True(easySession.ShouldOfferQuickResolve());
            Assert.True(TacticalCombatSession.ShouldOfferQuickResolve(easySession.Combatants));
            Assert.True(easySummary.PowerRatio >= 2.1);

            // Step one turn manually via StepAutoCombatTurn, then finish via QuickResolveCombat.
            CombatActionResult firstStep = easySession.StepAutoCombatTurn();
            Assert.True(firstStep.Success);

            CombatOutcome outcome = easySession.QuickResolveCombat();
            Assert.Equal(CombatOutcome.Victory, outcome);
            Assert.Equal(50, easySession.TotalXpEarned);
            Assert.Equal(10, easySession.TotalGoldLoot);
            Assert.Equal(20, party.Gold);
            Assert.Equal(25, pm1.Xp);
            Assert.Equal(25, pm2.Xp);
            Assert.Single(party.Stash);
            Assert.Equal(50, party.Stash[0].ItemId);
            Assert.Equal(2, party.Stash[0].Quantity);

            // Case 2: Balanced encounter -> ShouldOfferQuickResolve == false
            CombatantState balHero = new CombatantState(
                "h1", "Aldric", CombatSide.Hero, new GridCoord(1, 4),
                hp: 24, maxHp: 24, ac: 14, toHit: 15, weaponDice: "1d8+1");
            CombatantState balOrc1 = new CombatantState(
                "m1", "Orc Warrior", CombatSide.Monster, new GridCoord(6, 4),
                hp: 22, maxHp: 22, ac: 13, toHit: 14, weaponDice: "1d8+1");
            CombatantState balOrc2 = new CombatantState(
                "m2", "Orc Shaman", CombatSide.Monster, new GridCoord(6, 6),
                hp: 18, maxHp: 18, ac: 12, toHit: 12, weaponDice: "1d6+1", maxSp: 20);

            TacticalCombatSession balancedSession = new TacticalCombatSession(
                new CombatGrid(10, 10),
                new[] { balHero, balOrc1, balOrc2 });

            QuickResolveSummary balSummary = balancedSession.EvaluateQuickResolve();
            Assert.False(balSummary.CanQuickResolve);
            Assert.False(balancedSession.ShouldOfferQuickResolve());
            Assert.False(TacticalCombatSession.ShouldOfferQuickResolve(balancedSession.Combatants));

            // Case 3: High burst enemy (>= 55% of lowest hero MaxHP) when ratio is 2.1x..2.59x -> false
            CombatantState glassHero1 = new CombatantState("h1", "H1", CombatSide.Hero, new GridCoord(1, 1), 16, 16, ac: 13, toHit: 12, weaponDice: "1d8");
            CombatantState glassHero2 = new CombatantState("h2", "H2", CombatSide.Hero, new GridCoord(1, 2), 16, 16, ac: 13, toHit: 12, weaponDice: "1d8");
            CombatantState glassHero3 = new CombatantState("h3", "H3", CombatSide.Hero, new GridCoord(1, 3), 16, 16, ac: 13, toHit: 12, weaponDice: "1d8");
            CombatantState burstAssassin = new CombatantState("m1", "Assassin", CombatSide.Monster, new GridCoord(5, 5), 14, 14, ac: 10, toHit: 10, weaponDice: "1d12+4"); // Burst 16 >= 9 (55% of 16)
            QuickResolveSummary burstSummary = TacticalCombatSession.EvaluateQuickResolve(new[] { glassHero1, glassHero2, glassHero3, burstAssassin });
            Assert.True(burstSummary.PowerRatio >= 2.1 && burstSummary.PowerRatio < 2.6);
            Assert.False(burstSummary.CanQuickResolve);
        }
    }
}
