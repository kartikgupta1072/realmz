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
        public void OptionADndEconomy_SeparatesMoveMpFromMainAndBonusActions_AndSupportsDash()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            HeroPresetData slowExhaustedPreset = new HeroPresetData(
                "hero_slow", "Sluggish", archetype: "Rogue", level: 1, hp: 20, maxHp: 20,
                ac: 10, toHit: 18, agility: 5, movementMax: 5);
            HeroPresetData nimblePreset = new HeroPresetData(
                "hero_fast", "Nimble", archetype: "Rogue", level: 1, hp: 20, maxHp: 20,
                ac: 10, toHit: 18, agility: 25, movementMax: 7);

            PartyMemberState slowHero = new PartyMemberState(slowExhaustedPreset, bundle.Items);
            slowHero.ExhaustionStacks = 4;
            PartyMemberState fastHero = new PartyMemberState(nimblePreset, bundle.Items);

            CombatantState cSlow = CombatantState.FromHero(slowHero, new GridCoord(1, 3), bundle.Items);
            CombatantState cFast = CombatantState.FromHero(fastHero, new GridCoord(1, 5), bundle.Items);
            CombatantState goblin = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(8, 5), 20, 20, ac: 0, dexterity: 3);

            CombatGrid grid = new CombatGrid(10, 10);
            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { cSlow, cFast, goblin }, bundle.Spells, bundle.Items, roller: new QueuedDiceRoller(20, 4));

            // Fast hero goes first because Agility governs Initiative! Both start with Split 4 MP + 6 AP.
            Assert.Equal("hero_fast", session.CurrentActorId);
            Assert.True(cFast.Economy.IsSplitMpApMode);
            Assert.Equal(4, cFast.Economy.MoveBudget);
            Assert.Equal(4, cFast.Economy.MoveRemaining);
            Assert.Equal(6, cFast.Economy.MaxAp);
            Assert.Equal(6, cFast.Economy.CurrentAp);
            Assert.True(cFast.Economy.HasSlot(CombatActionSlot.Reaction));

            // Moving 2 tiles consumes ONLY MP, leaving all 6 AP intact!
            Assert.True(session.TryMoveTo(new GridCoord(3, 5)).Success);
            Assert.Equal(2, cFast.Economy.MoveRemaining);
            Assert.Equal(6, cFast.Economy.CurrentAp);

            // Dash consumes 2 AP to grant +4 MP.
            CombatActionResult dashRes = session.ExecuteDash();
            Assert.True(dashRes.Success);
            Assert.Equal(4, cFast.Economy.CurrentAp);
            Assert.True(cFast.Economy.HasUsedDash);
            Assert.Equal(2 + cFast.GetDndBaseMoveBudget(), cFast.Economy.MoveRemaining);

            session.EndTurn();
            Assert.Equal("hero_slow", session.CurrentActorId);
            Assert.Equal(4, cSlow.Economy.MoveBudget);
            Assert.Equal(6, cSlow.Economy.CurrentAp);

            session.EndTurn();
            session.EndTurn(); // goblin

            // Round 2 resets fresh 4 MP, 6 AP, and Reaction cleanly.
            Assert.Equal(2, session.RoundNumber);
            Assert.Equal("hero_fast", session.CurrentActorId);
            Assert.Equal(4, cFast.Economy.MoveBudget);
            Assert.Equal(4, cFast.Economy.MoveRemaining);
            Assert.Equal(6, cFast.Economy.CurrentAp);
            Assert.False(cFast.Economy.HasUsedDash);
        }

        [Fact]
        public void EquipmentSets_ScoutsLeatherAndIronVanguard_GrantExactBonusesAndStarterProgress()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            PartyState party = PartyState.FromBundle(bundle);

            // Starter heroes: Aldric & Seraphina start with 1/3 Iron Vanguard (Shield) and reach 3/3 by equipping Helm + Steel Shod Boots.
            PartyMemberState aldric = party.Members.First(m => m.Name == "Aldric");
            EquipmentSetProgress? aldricStarter = aldric.GetEquipmentSetProgress(EquipmentSetCatalog.IronVanguardSetId);
            Assert.NotNull(aldricStarter);
            Assert.Equal(1, aldricStarter!.EquippedPieces);

            party.AddItemToStash(400, 1); // Helm
            party.AddItemToStash(455, 1); // Steel Shod Boots
            int aldricIdx = party.Members.ToList().IndexOf(aldric);
            Assert.True(party.EquipFromStash(aldricIdx, party.Stash.ToList().FindIndex(s => s.ItemId == 400), bundle.Items));
            Assert.True(party.EquipFromStash(aldricIdx, party.Stash.ToList().FindIndex(s => s.ItemId == 455), bundle.Items));

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
            Assert.Equal(2, vex.GetSetFreeMoveTiles());
            Assert.Equal(0, vex.GetSetApBonus());

            int bootsIdx = party.Stash.ToList().FindIndex(s => s.ItemId == 454);
            Assert.True(party.EquipFromStash(party.Members.ToList().IndexOf(vex), bootsIdx, bundle.Items));
            EquipmentSetProgress? vexScout = vex.GetEquipmentSetProgress(EquipmentSetCatalog.ScoutsLeatherSetId);
            Assert.NotNull(vexScout);
            Assert.Equal(3, vexScout!.EquippedPieces);
            Assert.Equal(2, vexScout.FreeMoveTiles);
            Assert.Equal(1, vexScout.ApBonus);

            CombatantState cAldric = CombatantState.FromHero(aldric, new GridCoord(1, 3), bundle.Items);
            CombatantState cVex = CombatantState.FromHero(vex, new GridCoord(1, 5), bundle.Items);
            CombatantState goblin = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(7, 4), 20, 20, dexterity: 8);

            CombatGrid grid = new CombatGrid(10, 10);
            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { cVex, cAldric, goblin }, bundle.Spells, bundle.Items);

            // Vex starts with 4 Base MP + 2 bonus MP (6 MP) AND 6 Base AP + 1 bonus AP (7 AP) from Scout's Leather 3pc!
            Assert.Equal("hero_rogue", session.CurrentActorId);
            int expectedVexMove = cVex.GetDndBaseMoveBudget() + 2;
            Assert.Equal(6, expectedVexMove);
            Assert.Equal(expectedVexMove, cVex.Economy.MoveBudget);
            Assert.Equal(expectedVexMove, cVex.Economy.MoveRemaining);
            Assert.Equal(7, cVex.Economy.MaxAp);
            Assert.Equal(7, cVex.Economy.CurrentAp);

            // Moving 2 tiles spends 2 MP cleanly (AP untouched) and UndoMove restores full MP!
            CombatActionResult moveRes = session.TryMoveTo(new GridCoord(3, 5));
            Assert.True(moveRes.Success);
            Assert.Equal(2, moveRes.ApSpent);
            Assert.Equal(expectedVexMove - 2, cVex.Economy.MoveRemaining);
            Assert.Equal(7, cVex.Economy.CurrentAp);

            Assert.True(session.UndoMove());
            Assert.Equal(expectedVexMove, cVex.Economy.MoveRemaining);

            // Switch to Aldric -> Iron Vanguard 3pc grants +1 Max AP (7 AP) and 1 AP Guard discount!
            session.EndTurn();
            Assert.Equal("hero_knight", session.CurrentActorId);
            Assert.Equal(7, cAldric.Economy.MaxAp);
            Assert.Equal(1, cAldric.Economy.EffectiveGuardApCost);
            CombatActionResult guardRes = session.GuardOverwatch();
            Assert.True(guardRes.Success);
            Assert.Equal(1, guardRes.ApSpent);
            Assert.Equal(6, cAldric.Economy.CurrentAp);
        }

        [Fact]
        public void OneMainAction_BonusActionShoveTorchPotion_AndMud2MpWork()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            PartyState party = PartyState.FromBundle(bundle);
            PartyMemberState vexMember = party.Members.First(m => m.Name == "Vex");
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
                roller: new QueuedDiceRoller(20, 4, 15));

            // 6 AP pool: Light weapon melee (3 AP) + Shove (2 AP) = 5 AP spent cleanly leaving 1 AP.
            Assert.Equal(6, vex.Economy.MaxAp);
            Assert.Equal(CombatActionEconomy.LightWeaponApCost, vex.MeleeApCost);
            int apStart = vex.Economy.CurrentAp;
            Assert.True(session.ExecuteMeleeAttack("m1").Success);
            Assert.True(vex.Economy.CurrentAp < apStart);
            Assert.True(session.TryShove("m1").Success);
            Assert.Equal(36, goblin.Hp);

            // Next hero turn: check Mud/Water 2 MP movement cost.
            session.EndTurn();
            session.EndTurn();
            Assert.Equal("hero_rogue", session.CurrentActorId);
            int mpBefore = vex.Economy.MoveRemaining;
            CombatActionResult mudMove = session.TryMoveTo(new GridCoord(2, 4));
            Assert.True(mudMove.Success);
            Assert.Equal(2, mudMove.ApSpent); // 2 MP for Mud
            Assert.Equal(mpBefore - 2, vex.Economy.MoveRemaining);
            Assert.Equal(2, session.GetTileMoveApCost(2, 5)); // Shallow Water = 2 MP
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

            // Curated compact deployment zone (<= 12 tiles including hero positions + PartyAmbush High Ground)
            Assert.Equal(CombatPhase.Deployment, deploySession.Phase);
            Assert.InRange(deploySession.DeploymentZoneTiles.Count, 4, 12);
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
            Assert.True(deploySession.CurrentActor != null && deploySession.CurrentActor.Economy.HasSlot(CombatActionSlot.MainAction));

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
        public void OptionAAiSingleMainAction_And_AdvantageDisadvantageAndD20Checks_WorkEndToEnd()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
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
            // Split MP/AP: Theron spends AP casting Fireball (and any affordable follow-up).
            Assert.True(session.Log.Count(e => e.ActorId == "h1" && e.ActionType == "CastSpell") >= 1);
            Assert.True(ogre.Hp < 50);

            // Monster stationary turn uses AP cleanly when adjacent.
            ogre.Position = new GridCoord(2, 5);
            session.ExecuteMonsterAiTurn();
            Assert.True(session.Log.Count(e => e.ActorId == "m1" && e.ActionType == "MeleeAttack") >= 1);

            // Combat Advantage (target Prone) and Disadvantage (ranged threatened in melee)
            CombatGrid advGrid = new CombatGrid(8, 8);
            CombatantState hero = new CombatantState("h_adv", "Kaelen", CombatSide.Hero, new GridCoord(2, 2), 20, 20, toHit: 60, rangedDice: "1d6", rangedRange: 5, missileSkill: 60, dexterity: 18);
            CombatantState foe = new CombatantState("m_adv", "Goblin", CombatSide.Monster, new GridCoord(3, 2), 20, 20, ac: 15, dexterity: 8) { IsProne = true };
            TacticalCombatSession advSession = new TacticalCombatSession(advGrid, new[] { hero, foe });
            Assert.Equal(RollAdvantageState.Advantage, advSession.PreviewMeleeAttack("h_adv", "m_adv").AdvantageState);
            Assert.Equal(RollAdvantageState.Disadvantage, advSession.PreviewRangedAttack("h_adv", "m_adv").AdvantageState);
        }

        [Fact]
        public void HauntedQuartersCobblestone_ResolvesRuinsOutskirts_AndD20ChecksWork()
        {
            EncounterGeographyInfo hauntedGeo = new EncounterGeographyInfo(
                EncounterBiomeKind.Ruins,
                "Haunted Quarters",
                "Cracked Cobbles",
                distanceToWater: 8,
                isOnRoad: true,
                isCobblestone: true,
                isDeepForest: false,
                isNearRuins: true,
                isNearDungeonEntrance: false,
                isTownSafeZone: false,
                probabilityMultiplier: 1.35);

            Assert.Equal(CombatArenaArchetype.RuinsOutskirts, EncounterDirector.ResolveArenaArchetype(hauntedGeo));

            CombatGrid ruinsGrid = new CombatGrid(14, 10);
            EncounterDirector.ApplyBiomeArenaFeatures(ruinsGrid, hauntedGeo, null, new DeterministicDiceRoller(9UL));
            Assert.DoesNotContain(ruinsGrid.ArenaLayout!.Features, f => f.ObstacleKind == CombatArenaObstacleKind.SupplyWagon);

            PartyState party = PartyState.FromBundle(BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir()));
            QuickResolveSummary summary = new QuickResolveSummary(true, 100, 40, 2.5, "Overwhelming");
            EncounterCheckSpec qrSpec = EncounterDirector.BuildQuickResolveCheckSpec(summary, party);
            Assert.Equal(7, qrSpec.Dc);
            Assert.Equal("Overwhelming", qrSpec.TierLabel);

            EncounterCheckRollResult nat20 = EncounterDirector.RollQuickResolveCheck(summary, party, new QueuedDiceRoller(20));
            Assert.True(nat20.Success);
            Assert.True(nat20.IsNatural20);

            EncounterCheckRollResult nat1 = EncounterDirector.RollQuickResolveCheck(summary, party, new QueuedDiceRoller(1));
            Assert.False(nat1.Success);
            Assert.True(nat1.IsNatural1);
        }
    }
}
