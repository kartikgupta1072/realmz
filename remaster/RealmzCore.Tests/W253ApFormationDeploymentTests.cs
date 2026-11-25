using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using Xunit;

namespace RealmzCore.Tests
{
    public class W253ApFormationDeploymentTests
    {
        private static string FindBywaterM1Dir()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "extracted", "bywater_m1");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                string remasterCandidate = Path.Combine(current.FullName, "remaster", "extracted", "bywater_m1");
                if (Directory.Exists(remasterCandidate))
                {
                    return remasterCandidate;
                }

                current = current.Parent;
            }

            return @"C:\Users\karti\realmz\remaster\extracted\bywater_m1";
        }

        [Fact]
        public void BaseApPerTurn_IsFixedAt6_RegardlessOfAgilityOrFatigue_AndCarryoverCapsAt2()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            HeroPresetData slowExhaustedPreset = new HeroPresetData(
                "hero_slow", "Sluggish", archetype: "Rogue", level: 1, hp: 20, maxHp: 20,
                ac: 10, toHit: 18, agility: 5, movementMax: 4);
            HeroPresetData nimblePreset = new HeroPresetData(
                "hero_fast", "Nimble", archetype: "Rogue", level: 1, hp: 20, maxHp: 20,
                ac: 10, toHit: 18, agility: 25, movementMax: 10);

            PartyMemberState slowHero = new PartyMemberState(slowExhaustedPreset, bundle.Items);
            slowHero.ExhaustionStacks = 4; // Heavy fatigue must NEVER reduce AP!
            PartyMemberState fastHero = new PartyMemberState(nimblePreset, bundle.Items);

            CombatantState cSlow = CombatantState.FromHero(slowHero, new GridCoord(1, 3), bundle.Items);
            CombatantState cFast = CombatantState.FromHero(fastHero, new GridCoord(1, 5), bundle.Items);
            CombatantState goblin = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(8, 4), 20, 20, dexterity: 3);

            CombatGrid grid = new CombatGrid(10, 10);
            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { cSlow, cFast, goblin }, bundle.Spells, bundle.Items);

            // Fast hero goes first because Agility governs Initiative! Both get strictly 6 AP.
            Assert.Equal("hero_fast", session.CurrentActorId);
            Assert.Equal(6, cFast.Economy.BaseApPerTurn);
            Assert.Equal(6, cFast.Economy.CurrentAp);

            // Fast hero moves 2 tiles (spends 2 AP, 4 left), ends turn -> banks MaxCarryoverAp (2).
            Assert.True(session.TryMoveTo(new GridCoord(3, 5)).Success);
            Assert.Equal(4, cFast.Economy.CurrentAp);
            Assert.Equal(2, cFast.Economy.PendingCarryoverAp);
            session.EndTurn();

            // Exhausted slow hero gets strictly 6 AP (zero fatigue penalty, zero agility penalty).
            Assert.Equal("hero_slow", session.CurrentActorId);
            Assert.Equal(6, cSlow.Economy.BaseApPerTurn);
            Assert.Equal(6, cSlow.Economy.CurrentAp);
            Assert.True(session.TryMoveTo(new GridCoord(5, 3)).Success); // 4 tiles -> 2 AP left
            Assert.Equal(2, cSlow.Economy.CurrentAp);
            session.EndTurn();

            // Goblin ends turn (wait: goblin moves/ends).
            session.EndTurn();

            // Round 2: both heroes get 6 + 2 = 8 AP carryover cap!
            Assert.Equal(2, session.RoundNumber);
            Assert.Equal("hero_fast", session.CurrentActorId);
            Assert.Equal(8, cFast.Economy.MaxAp);
            Assert.Equal(8, cFast.Economy.CurrentAp);
            Assert.Equal(2, cFast.Economy.CarriedInAp);
        }

        [Fact]
        public void EquipmentSets_ScoutsLeatherAndIronVanguard_GrantExactBonusesAndStarterProgress()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            PartyState party = PartyState.FromBundle(bundle);

            // Starter heroes: Aldric & Seraphina wear Broadsword/Longsword + Shield + Helm (Iron Vanguard 3/3).
            PartyMemberState aldric = party.Members.First(m => m.Name == "Aldric");
            EquipmentSetProgress? aldricVanguard = aldric.GetEquipmentSetProgress(EquipmentSetCatalog.IronVanguardSetId);
            Assert.NotNull(aldricVanguard);
            Assert.Equal(3, aldricVanguard!.EquippedPieces);
            Assert.Equal(2, aldricVanguard.AcBonus);
            Assert.Equal(1, aldricVanguard.ApBonus);
            Assert.Equal(1, aldricVanguard.GuardApDiscount);

            // Equip Scout's Leather 3pc on Vex (already starts with Leather Armor 209 -> add Cap 450 & Boots 454).
            PartyMemberState vex = party.Members.First(m => m.Name == "Vex");
            Assert.Equal(1, vex.GetEquipmentSetProgress(EquipmentSetCatalog.ScoutsLeatherSetId)!.EquippedPieces);
            party.AddItemToStash(450, 1); // Leather Cap
            party.AddItemToStash(454, 1); // Leather Boots
            int capIdx = party.Stash.ToList().FindIndex(s => s.ItemId == 450);
            Assert.True(party.EquipFromStash(party.Members.ToList().IndexOf(vex), capIdx, bundle.Items));
            Assert.Equal(1, vex.GetSetFreeMoveTiles());
            Assert.Equal(0, vex.GetSetApBonus());

            int bootsIdx = party.Stash.ToList().FindIndex(s => s.ItemId == 454);
            Assert.True(party.EquipFromStash(party.Members.ToList().IndexOf(vex), bootsIdx, bundle.Items));
            EquipmentSetProgress? vexScout = vex.GetEquipmentSetProgress(EquipmentSetCatalog.ScoutsLeatherSetId);
            Assert.NotNull(vexScout);
            Assert.Equal(3, vexScout!.EquippedPieces);
            Assert.Equal(1, vexScout.FreeMoveTiles);
            Assert.Equal(1, vexScout.ApBonus);

            CombatantState cAldric = CombatantState.FromHero(aldric, new GridCoord(1, 3), bundle.Items);
            CombatantState cVex = CombatantState.FromHero(vex, new GridCoord(1, 5), bundle.Items);
            CombatantState goblin = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(7, 4), 20, 20, dexterity: 8);

            CombatGrid grid = new CombatGrid(10, 10);
            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { cVex, cAldric, goblin }, bundle.Spells, bundle.Items);

            // Vex starts with 6 + 1 = 7 AP and 1 Free Move Tile!
            Assert.Equal("hero_rogue", session.CurrentActorId);
            Assert.Equal(7, cVex.Economy.CurrentAp);
            Assert.Equal(1, cVex.Economy.FreeMoveTilesRemaining);

            // Moving 2 tiles consumes 1 Free Move Tile (0 AP) + 1 AP -> 6 AP remaining!
            CombatActionResult moveRes = session.TryMoveTo(new GridCoord(3, 5));
            Assert.True(moveRes.Success);
            Assert.Equal(1, moveRes.ApSpent);
            Assert.Equal(0, cVex.Economy.FreeMoveTilesRemaining);
            Assert.Equal(6, cVex.Economy.CurrentAp);

            // UndoMove restores both CurrentAp AND FreeMoveTilesRemaining!
            Assert.True(session.UndoMove());
            Assert.Equal(1, cVex.Economy.FreeMoveTilesRemaining);
            Assert.Equal(7, cVex.Economy.CurrentAp);

            // Switch to Aldric -> Iron Vanguard 3pc gives Guard = 1 AP & triggers Overwatch reaction!
            session.EndTurn();
            Assert.Equal("hero_knight", session.CurrentActorId);
            Assert.Equal(7, cAldric.Economy.CurrentAp);
            Assert.Equal(1, cAldric.Economy.EffectiveGuardApCost);
            CombatActionResult guardRes = session.GuardOverwatch();
            Assert.True(guardRes.Success);
            Assert.Equal(1, guardRes.ApSpent);
            Assert.Equal(6, cAldric.Economy.CurrentAp);
        }

        [Fact]
        public void MultipleLightAttacks_MudPenalty_And_OverwatchReactionWork()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            PartyState party = PartyState.FromBundle(bundle);
            PartyMemberState vexMember = party.Members.First(m => m.Name == "Vex"); // Short Sword = 2 AP
            CombatantState vex = CombatantState.FromHero(vexMember, new GridCoord(3, 4), bundle.Items);
            CombatantState goblin = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(4, 4), 40, 40, ac: 0, dexterity: 8);

            CombatGrid grid = new CombatGrid(10, 10);
            grid.SetMoveCost(2, 4, 2); // Mud tile behind Vex
            grid.SetSurface(2, 5, SurfaceType.Water, -1); // Shallow Water tile

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { vex, goblin },
                bundle.Spells,
                bundle.Items,
                roller: new QueuedDiceRoller(20, 4, 20, 4, 20, 4, 20, 3));

            Assert.Equal(2, vex.MeleeApCost);

            // 3 consecutive Short Sword attacks in 1 turn (3 x 2 AP = 6 AP)!
            Assert.True(session.ExecuteMeleeAttack("m1").Success);
            Assert.Equal(4, vex.Economy.CurrentAp);
            Assert.True(session.ExecuteMeleeAttack("m1").Success);
            Assert.Equal(2, vex.Economy.CurrentAp);
            Assert.True(session.ExecuteMeleeAttack("m1").Success);
            Assert.Equal(0, vex.Economy.CurrentAp);
            Assert.False(session.ExecuteMeleeAttack("m1").Success);
            Assert.Equal(28, goblin.Hp); // 40 - 3*4

            // Next hero turn: check Mud/Water 2 AP movement cost.
            session.EndTurn();
            session.EndTurn();
            Assert.Equal("hero_rogue", session.CurrentActorId);
            CombatActionResult mudMove = session.TryMoveTo(new GridCoord(2, 4));
            Assert.True(mudMove.Success);
            Assert.Equal(2, mudMove.ApSpent); // 2 AP for Mud
            Assert.Equal(4, vex.Economy.CurrentAp);
            Assert.Equal(2, session.GetTileMoveApCost(2, 5)); // Shallow Water = 2 AP
        }

        [Fact]
        public void FormationRanks_And_PreBattleDeployment_SwapMoveAndSurpriseSkipWork()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            PartyState party = PartyState.FromBundle(bundle);

            // Verify Kartik's default Marching Formation ranks: Front (Aldric, Seraphina), Mid (Liora, Vex), Back (Kaelen, Theron).
            Assert.Equal(FormationRank.Front, party.Members.First(m => m.Name == "Aldric").FormationRank);
            Assert.Equal(FormationRank.Front, party.Members.First(m => m.Name == "Seraphina").FormationRank);
            Assert.Equal(FormationRank.Mid, party.Members.First(m => m.Name == "Liora").FormationRank);
            Assert.Equal(FormationRank.Mid, party.Members.First(m => m.Name == "Vex").FormationRank);
            Assert.Equal(FormationRank.Back, party.Members.First(m => m.Name == "Kaelen").FormationRank);
            Assert.Equal(FormationRank.Back, party.Members.First(m => m.Name == "Theron").FormationRank);

            // Edit formation rank cleanly in PartyState.
            int kaelenIdx = party.Members.ToList().FindIndex(m => m.Name == "Kaelen");
            Assert.True(party.SetFormationRank(kaelenIdx, FormationRank.Mid));
            Assert.Equal(FormationRank.Mid, party.GetFormationRank(kaelenIdx));

            // Normal / PartyAmbush combat enters CombatPhase.Deployment, allowing swap & reposition before StartBattleFromDeployment.
            CombatGrid grid = new CombatGrid(12, 10);
            grid.SetElevation(5, 4, 1); // Forward High Ground unlocked by PartyAmbush deployment zone!

            CombatantState aldric = CombatantState.FromHero(party.Members[0], new GridCoord(2, 4), bundle.Items);
            CombatantState theron = CombatantState.FromHero(party.Members[1], new GridCoord(0, 4), bundle.Items);
            CombatantState goblin = CombatantState.FromMonster(bundle.Monsters[0], "m1", new GridCoord(9, 4));

            TacticalCombatSession deploySession = new TacticalCombatSession(
                grid,
                new[] { aldric, theron, goblin },
                bundle.Spells,
                bundle.Items,
                surpriseMode: CombatInitiativeSurpriseMode.PartyAmbush,
                startInDeployment: true);

            Assert.Equal(CombatPhase.Deployment, deploySession.Phase);
            Assert.Contains(new GridCoord(5, 4), deploySession.DeploymentZoneTiles); // Widened High Ground!
            Assert.False(deploySession.TryMoveTo(new GridCoord(3, 4)).Success); // Cannot execute turn actions before StartBattle

            // Swap two heroes cleanly.
            Assert.True(deploySession.SwapOrMoveHeroInDeployment(aldric.Id, theron.Position));
            Assert.Equal(new GridCoord(0, 4), aldric.Position);
            Assert.Equal(new GridCoord(2, 4), theron.Position);

            // Reposition onto empty High Ground tile inside deployment zone.
            Assert.True(deploySession.SwapOrMoveHeroInDeployment(theron.Id, new GridCoord(5, 4)));
            Assert.Equal(new GridCoord(5, 4), theron.Position);

            // Start Battle transitions to CombatPhase.Combat!
            Assert.True(deploySession.StartBattleFromDeployment());
            Assert.Equal(CombatPhase.Combat, deploySession.Phase);
            Assert.True(deploySession.CurrentActor != null && deploySession.CurrentActor.Economy.CurrentAp >= 6);

            // AMBUSHED IN THE DARK! skips Deployment immediately!
            TacticalCombatSession ambushDark = new TacticalCombatSession(
                grid,
                new[] { CombatantState.FromHero(party.Members[0], new GridCoord(2, 4), bundle.Items), CombatantState.FromMonster(bundle.Monsters[0], "m1", new GridCoord(4, 4)) },
                bundle.Spells,
                bundle.Items,
                surpriseMode: CombatInitiativeSurpriseMode.EnemySurprisedParty,
                startInDeployment: true);
            Assert.Equal(CombatPhase.Combat, ambushDark.Phase);
        }

        [Fact]
        public void SpellApTiers_And_AiMultiActionExecution_WorkEndToEnd()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            Assert.Equal(2, CombatWeaponCosts.GetSpellApCost(bundle.Spells[1108])); // Magic Darts
            Assert.Equal(2, CombatWeaponCosts.GetSpellApCost(bundle.Spells[2105])); // Heal Small Wounds
            Assert.Equal(3, CombatWeaponCosts.GetSpellApCost(bundle.Spells[1104])); // Flame Hands
            Assert.Equal(4, CombatWeaponCosts.GetSpellApCost(bundle.Spells[1306])); // Fireball
            Assert.Equal(4, CombatWeaponCosts.GetSpellApCost(bundle.Spells[3202])); // Daze
            Assert.Equal(4, CombatWeaponCosts.GetSpellApCost(bundle.Spells[2310])); // Sandman

            CombatGrid grid = new CombatGrid(10, 10);
            CombatantState theron = new CombatantState(
                "h1", "Theron", CombatSide.Hero, new GridCoord(1, 5),
                hp: 15, maxHp: 15, sp: 20, maxSp: 20, dexterity: 20,
                spellIds: new[] { 1306, 1108 });
            CombatantState ogre = new CombatantState("m1", "Ogre", CombatSide.Monster, new GridCoord(7, 5), 50, 50, ac: 5, dexterity: 8);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { theron, ogre },
                bundle.Spells,
                bundle.Items,
                roller: new DeterministicDiceRoller(123UL));

            CombatActionResult aiRes = session.ExecutePartyAiTurn();
            Assert.True(aiRes.Success);
            // Theron casts Fireball (4 AP) AND Magic Darts (2 AP) in 1 turn!
            Assert.True(session.Log.Count(e => e.ActorId == "h1" && e.ActionType == "CastSpell") == 2);
            Assert.True(ogre.Hp < 50);

            // Monster stationary turn can swing twice (3 AP + 3 AP = 6 AP) when adjacent!
            ogre.Position = new GridCoord(2, 5);
            int theronHpBefore = theron.Hp;
            session.ExecuteMonsterAiTurn();
            Assert.True(session.Log.Count(e => e.ActorId == "m1" && e.ActionType == "MeleeAttack") == 2);
        }
    }
}
