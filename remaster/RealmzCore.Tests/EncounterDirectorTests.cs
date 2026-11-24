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
    public class EncounterDirectorTests
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

            if (Directory.Exists(@"C:\realmz\remaster\extracted\bywater_m1"))
            {
                return @"C:\realmz\remaster\extracted\bywater_m1";
            }

            throw new DirectoryNotFoundException("Could not locate extracted/bywater_m1.");
        }

        [Fact]
        public void BasisPointScaling_AndTownSafeZones_Prevent100PctTriggers()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession session = new ExplorationSession(bundle.Maps, "land_0", party, new DeterministicDiceRoller(12345UL));

            // 1. Walking inside Bywater Town (slot 0, slot 8, and city limits) triggers 0 hostile encounters across 50 steps.
            GridCoord[] townPatrol =
            {
                new GridCoord(10, 9), new GridCoord(10, 10), new GridCoord(9, 10), new GridCoord(8, 10),
                new GridCoord(8, 11), new GridCoord(9, 11), new GridCoord(10, 11), new GridCoord(10, 12)
            };

            for (int i = 0; i < 40; i++)
            {
                GridCoord next = townPatrol[i % townPatrol.Length];
                if (session.Position.ChebyshevDistance(next) <= 1 && session.CurrentMap.IsPassable(next.X, next.Y, false))
                {
                    ExplorationStepResult step = session.StepOneTile(next);
                    Assert.NotEqual(ExplorationHaltReason.EncounterZone, step.HaltReason);
                }
                else
                {
                    session.MoveAlongPath(next, autoHaltOnEvents: true);
                    Assert.True(session.IsInSafeZone());
                }
            }

            // 2. Any zone with battle_ids == [0] and empty monster_indices (like slot 6/7/8/9) never fires.
            TileMapData land0 = bundle.Land0!;
            EncounterZoneData slot0 = land0.EncounterZones.First(z => z.Slot == 0);
            EncounterZoneData slot7 = land0.EncounterZones.First(z => z.Slot == 7);
            Assert.Empty(slot0.MonsterIndices);
            Assert.All(slot0.BattleIds, id => Assert.Equal(0, id));
            Assert.Empty(slot7.MonsterIndices);

            // 3. Basis-point scaling: a zone with percent = 60 (0.6%) only fires when roller returns 1..60 out of 1..10000.
            int[] tiles = new int[25];
            Array.Fill(tiles, 156); // grass
            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule> { [156] = new TileRule(156, passable: true) };
            EncounterZoneData lowPctZone = new EncounterZoneData(1, 0, 0, 4, 4, percent: 60, battleIds: new[] { 9 }, monsterIndices: new[] { 92 });
            TileMapData wildMap = new TileMapData("wild_test", "Bywater", 0, false, 5, 5, tiles, new GridCoord(0, 0), rules, encounterZones: new[] { lowPctZone });

            // Roll 100 (> 60) -> does NOT trigger (previously 100 <= 100 fired 100%!).
            ExplorationSession missSession = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["wild_test"] = wildMap },
                "wild_test",
                PartyState.FromBundle(bundle),
                new QueuedDiceRoller(100, 100, 100));
            ExplorationStepResult missStep = missSession.StepOneTile(new GridCoord(1, 0));
            Assert.True(missStep.Moved);
            Assert.Equal(ExplorationHaltReason.None, missStep.HaltReason);

            // Roll 42 (<= 60) -> triggers!
            ExplorationSession hitSession = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["wild_test"] = wildMap },
                "wild_test",
                PartyState.FromBundle(bundle),
                new QueuedDiceRoller(42, 0));
            ExplorationStepResult hitStep = hitSession.StepOneTile(new GridCoord(1, 0));
            Assert.True(hitStep.Moved);
            Assert.Equal(ExplorationHaltReason.EncounterZone, hitStep.HaltReason);
            Assert.Equal(9, hitStep.TriggeredBattleId);
        }

        [Fact]
        public void PostCombatGraceSteps_AndLandmarkVsWildernessCooldown_WorkCleanly()
        {
            int[] tiles = new int[100];
            Array.Fill(tiles, 156);
            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule> { [156] = new TileRule(156, passable: true) };

            // Large wilderness zone (area 400 > 150, 500 bp = 5%) vs small landmark (4x4 = 16 <= 150)
            int[] bigTiles = new int[400];
            Array.Fill(bigTiles, 156);
            EncounterZoneData wildZone = new EncounterZoneData(1, 0, 0, 19, 19, percent: 500, battleIds: new[] { 9 }, monsterIndices: new[] { 92 });
            EncounterZoneData landmarkZone = new EncounterZoneData(3, 0, 0, 3, 3, percent: 10000, battleIds: new[] { 24 }, monsterIndices: new[] { 70 });

            TileMapData wildMap = new TileMapData("wild", "Bywater", 0, false, 20, 20, bigTiles, new GridCoord(0, 0), rules, encounterZones: new[] { wildZone });
            TileMapData landmarkMap = new TileMapData("landmark", "Bywater", 0, false, 20, 20, bigTiles, new GridCoord(0, 0), rules, encounterZones: new[] { landmarkZone });

            PartyMemberState hero = new PartyMemberState(new HeroPresetData("h1", "Aldric", hp: 20, maxHp: 20));
            ExplorationSession wildSession = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["wild"] = wildMap },
                "wild",
                new PartyState(new[] { hero }),
                new QueuedDiceRoller(1, 0, 1, 0, 1, 0));

            // 1. Grant 10 post-combat grace steps -> steps 1..10 never trigger even when roll=1 (would hit 500 bp).
            wildSession.GrantPostCombatGrace(10);
            Assert.Equal(10, wildSession.PostCombatGraceStepsRemaining);
            for (int i = 1; i <= 10; i++)
            {
                ExplorationStepResult step = wildSession.StepOneTile(new GridCoord(i, 0));
                Assert.True(step.Moved);
                Assert.Equal(ExplorationHaltReason.None, step.HaltReason);
                Assert.Equal(10 - i, wildSession.PostCombatGraceStepsRemaining);
            }

            // Step 11 -> grace expired -> roll 1 <= 500 -> triggers!
            ExplorationStepResult triggerStep = wildSession.StepOneTile(new GridCoord(11, 0));
            Assert.Equal(ExplorationHaltReason.EncounterZone, triggerStep.HaltReason);

            // 2. Completing wilderness zone puts it on regional cooldown until next Day/Night shift.
            wildSession.CompleteEncounterZone(wildZone, graceSteps: 0);
            Assert.True(wildSession.IsEncounterZoneOnCooldown("wild", wildZone));
            Assert.False(wildSession.IsEncounterCleared("wild:zone:1"));

            // Stepping while still in current phase -> no encounter.
            ExplorationStepResult cooledStep = wildSession.StepOneTile(new GridCoord(12, 0));
            Assert.Equal(ExplorationHaltReason.None, cooledStep.HaltReason);

            // Advance past next phase boundary (into Dusk) -> wilderness zone respawns!
            int turnsToNextPhase = wildSession.CurrentWorldClock.TurnsUntilNextPhase;
            wildSession.AdvanceWorldClockTurns(turnsToNextPhase);
            Assert.False(wildSession.IsEncounterZoneOnCooldown("wild", wildZone));

            ExplorationStepResult respawnStep = wildSession.StepOneTile(new GridCoord(13, 0));
            Assert.Equal(ExplorationHaltReason.EncounterZone, respawnStep.HaltReason);

            // 3. Landmark zone is permanently cleared once won, even across multiple Day/Night shifts.
            ExplorationSession landmarkSession = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["landmark"] = landmarkMap },
                "landmark",
                new PartyState(new[] { new PartyMemberState(new HeroPresetData("h1", "Aldric")) }),
                new DeterministicDiceRoller(11UL));
            landmarkSession.CompleteEncounterZone(landmarkZone, graceSteps: 0);
            Assert.True(landmarkSession.IsEncounterCleared("landmark:zone:3"));
            landmarkSession.AdvanceWorldClockTurns(WorldClock.TurnsPerDay * 2);
            ExplorationStepResult afterDays = landmarkSession.StepOneTile(new GridCoord(1, 0));
            Assert.Equal(ExplorationHaltReason.None, afterDays.HaltReason);
        }

        [Fact]
        public void GeographyAndEcology_DifferentiateWaterRoadForestRuinsAndNight()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            TileMapData land0 = bundle.Land0!;

            // Verify real Bywater geography points
            // (10, 9) Town Square -> TownSafe, 0.0x
            EncounterGeographyInfo townGeo = EncounterDirector.EvaluateGeography(land0, new GridCoord(10, 9), isInSafeZone: true);
            Assert.Equal(EncounterBiomeKind.TownSafe, townGeo.BiomeKind);
            Assert.Equal(0.0, townGeo.ProbabilityMultiplier);

            // Find a riverbank tile (<= 3 tiles from water, not road) on land_0
            GridCoord? riverCoord = null;
            GridCoord? roadCoord = null;
            GridCoord? forestCoord = null;
            for (int y = 25; y < 85; y++)
            {
                for (int x = 1; x < 88; x++)
                {
                    if (!land0.IsPassable(x, y, false)) continue;
                    EncounterGeographyInfo g = EncounterDirector.EvaluateGeography(land0, new GridCoord(x, y), false);
                    if (riverCoord == null && g.BiomeKind == EncounterBiomeKind.Riverbank) riverCoord = new GridCoord(x, y);
                    if (roadCoord == null && g.BiomeKind == EncounterBiomeKind.Road) roadCoord = new GridCoord(x, y);
                    if (forestCoord == null && g.BiomeKind == EncounterBiomeKind.DeepForest) forestCoord = new GridCoord(x, y);
                }
            }

            Assert.NotNull(riverCoord);
            Assert.NotNull(roadCoord);
            Assert.NotNull(forestCoord);

            EncounterGeographyInfo riverGeo = EncounterDirector.EvaluateGeography(land0, riverCoord!.Value, false);
            EncounterGeographyInfo roadGeo = EncounterDirector.EvaluateGeography(land0, roadCoord!.Value, false);
            EncounterGeographyInfo forestGeo = EncounterDirector.EvaluateGeography(land0, forestCoord!.Value, false);
            EncounterGeographyInfo dungeonApproachGeo = EncounterDirector.EvaluateGeography(land0, new GridCoord(88, 48), false);

            Assert.True(riverGeo.IsNearWater);
            Assert.True(roadGeo.ProbabilityMultiplier < forestGeo.ProbabilityMultiplier);
            Assert.Equal(0.50, roadGeo.ProbabilityMultiplier);
            Assert.Equal(1.50, forestGeo.ProbabilityMultiplier);
            Assert.Equal(EncounterBiomeKind.DungeonApproach, dungeonApproachGeo.BiomeKind);
            Assert.Equal("Dungeon Approach", dungeonApproachGeo.LocationLabel);

            // Ecology monster packs match biomes
            WorldClock dayClock = new WorldClock(WorldClock.DefaultStartTurns);
            WorldClock nightClock = new WorldClock(WorldClock.TurnsPerPhase * 3); // Night phase

            List<MonsterDefinition> riverPack = EncounterDirector.BuildEcologyMonsterPack(
                land0, riverCoord.Value, null, riverGeo, dayClock, bundle.Monsters, new DeterministicDiceRoller(5UL));
            Assert.Contains(riverPack, m => m.Id == "giant_frog");

            List<MonsterDefinition> roadPack = EncounterDirector.BuildEcologyMonsterPack(
                land0, roadCoord.Value, null, roadGeo, dayClock, bundle.Monsters, new DeterministicDiceRoller(5UL));
            Assert.Contains(roadPack, m => m.Id.StartsWith("goblin", StringComparison.OrdinalIgnoreCase));

            List<MonsterDefinition> approachPack = EncounterDirector.BuildEcologyMonsterPack(
                land0, new GridCoord(88, 48), null, dungeonApproachGeo, nightClock, bundle.Monsters, new DeterministicDiceRoller(5UL));
            Assert.Contains(approachPack, m => m.IsUndead || m.Id == "skeletal_warrior");

            // Night raises effective basis points by ~1.4x outdoors
            EncounterZoneData sampleZone = new EncounterZoneData(2, 0, 0, 89, 89, percent: 250, battleIds: new[] { 14 });
            int dayBps = EncounterDirector.GetEffectiveBasisPoints(sampleZone, forestGeo, dayClock, isDungeon: false);
            int nightBps = EncounterDirector.GetEffectiveBasisPoints(sampleZone, forestGeo, nightClock, isDungeon: false);
            Assert.True(nightBps > dayBps);

            // Riverbank CombatGrid feature bias injects water surfaces for elemental combos
            CombatGrid riverArena = new CombatGrid(12, 10);
            EncounterDirector.ApplyBiomeArenaFeatures(riverArena, riverGeo, Array.Empty<GridCoord>(), new DeterministicDiceRoller(1UL));
            Assert.True(riverArena.GetActiveSurfaceCells().Count >= 4);
        }

        [Fact]
        public void WorldClock_DayNightCycle_TorchlightSurprise_AndNightRewards_WorkEndToEnd()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession session = new ExplorationSession(bundle.Maps, "land_0", party, new DeterministicDiceRoller(77UL));

            // Clean campaign starts at Day 1 - Noon (Turn 18 -> Phase Day)
            Assert.Equal(1, session.CurrentWorldClock.DayNumber);
            Assert.Equal(WorldTimePhase.Day, session.CurrentWorldClock.Phase);
            Assert.Equal("Day 1 - Noon", session.CurrentWorldClock.DisplayLabel);

            // Full Rest in safe town adds +8 turns to both TurnCount and WorldClockTurns
            party.Members[0].Hp = 1;
            RestResult rest = session.TryCampRest(CampMode.FullRest, out _, out _);
            Assert.True(rest.Success);
            Assert.Equal(8, rest.TurnsElapsed);
            Assert.Equal(8, session.TurnCount);
            Assert.Equal(WorldClock.DefaultStartTurns + 8, session.WorldClockTurns);

            // Advance into Dusk (+10 turns -> 36) then Night (+18 turns -> 54)
            session.AdvanceWorldClockTurns(10);
            Assert.Equal(WorldTimePhase.Dusk, session.CurrentWorldClock.Phase);
            session.AdvanceWorldClockTurns(18);
            Assert.Equal(WorldTimePhase.Night, session.CurrentWorldClock.Phase);

            // Night with TorchTurnsRemaining <= 0 triggers AmbushedInDark + skips Evade + grants +25% bonus XP/Gold!
            party.TorchTurnsRemaining = 0;
            EncounterScoutingReport darkReport = session.CreateScoutingReport(
                bundle.Land0!.EncounterZones.First(z => z.Slot == 2),
                new[] { bundle.Monsters.First(m => m.Id == "goblin") });
            Assert.True(darkReport.IsAmbushedInDark);
            Assert.False(darkReport.CanEvade);
            Assert.False(darkReport.CanAmbush);
            Assert.Equal(25, darkReport.RewardBonusPct);
            Assert.Equal(CombatInitiativeSurpriseMode.EnemySurprisedParty, darkReport.ResolveEngageSurpriseMode());

            // Lighting a torch removes AmbushedInDark and allows Scouting Ambush & Evade!
            party.AddItemToStash(PartyState.TorchItemId, 1, charges: 6);
            Assert.True(party.TryLightTorchFromStash(out _));
            Assert.True(party.TorchTurnsRemaining > 0);

            EncounterScoutingReport litReport = EncounterDirector.EvaluateScouting(
                bundle.Land0!,
                new GridCoord(60, 20),
                party,
                session.CurrentWorldClock,
                null,
                string.Empty,
                false,
                new[] { bundle.Monsters.First(m => m.Id == "zombie") },
                new QueuedDiceRoller(1)); // roll 1 <= ambushChancePct -> CanAmbush!
            Assert.False(litReport.IsAmbushedInDark);
            Assert.True(litReport.CanAmbush);
            Assert.True(litReport.CanEvade);
            Assert.InRange(litReport.EvadeChancePct, 15, 90);
            Assert.Equal(CombatInitiativeSurpriseMode.PartyAmbush, litReport.ResolveEngageSurpriseMode());

            // Evade roll + ApplyStealthEvade advances 3 turns and grants stealth grace without HP loss
            Assert.True(EncounterDirector.RollEvade(litReport, new QueuedDiceRoller(5)));
            int clockBeforeEvade = session.WorldClockTurns;
            session.ApplyStealthEvade();
            Assert.Equal(clockBeforeEvade + 3, session.WorldClockTurns);
            Assert.Equal(ExplorationSession.DefaultStealthEvadeGraceSteps, session.PostCombatGraceStepsRemaining);

            // Verify Round 1 SurpriseMode ordering + +25% Night Bonus in TacticalCombatSession
            MonsterDefinition slowMob = new MonsterDefinition(99, "slow_goblin", "Slow Goblin", hpMin: 4, hpMax: 4, hpDefault: 4, ac: 0, dexterity: 4, xpReward: 100, goldMin: 40, goldMax: 40);
            CombatantState hero = CombatantState.FromHero(party.Members[2], new GridCoord(2, 2), bundle.Items); // Kaelen high dex
            hero.ToHit = 95;
            hero.WeaponDice = "2d6+8";
            CombatantState enemy = CombatantState.FromMonster(slowMob, "m1", new GridCoord(3, 2));

            // Surprised in dark: slow goblin goes FIRST on Round 1 despite lower Dex!
            TacticalCombatSession surprisedCombat = new TacticalCombatSession(
                new CombatGrid(8, 8),
                new[] { hero, enemy },
                bundle.Spells,
                bundle.Items,
                new QueuedDiceRoller(20, 6, 6),
                RulesetMode.ModernBalanced,
                party,
                CombatInitiativeSurpriseMode.EnemySurprisedParty,
                rewardBonusPercent: 25);
            Assert.Equal("m1", surprisedCombat.CurrentActorId);
            Assert.Equal(CombatSide.Monster, surprisedCombat.CurrentActor!.Side);

            // Party Ambush: hero goes first AND Night +25% bonus increases 100 XP -> 125 XP, 40g -> 50g (+ Luck bonus)
            CombatantState hero2 = CombatantState.FromHero(party.Members[2], new GridCoord(2, 2), bundle.Items);
            hero2.ToHit = 95;
            hero2.WeaponDice = "2d6+8";
            CombatantState enemy2 = CombatantState.FromMonster(slowMob, "m2", new GridCoord(3, 2));
            TacticalCombatSession ambushCombat = new TacticalCombatSession(
                new CombatGrid(8, 8),
                new[] { hero2, enemy2 },
                bundle.Spells,
                bundle.Items,
                new QueuedDiceRoller(20, 6, 6),
                RulesetMode.ModernBalanced,
                party,
                CombatInitiativeSurpriseMode.PartyAmbush,
                rewardBonusPercent: 25);
            Assert.Equal(hero2.Id, ambushCombat.CurrentActorId);
            CombatActionResult atk = ambushCombat.ExecuteMeleeAttack("m2");
            Assert.True(atk.Success);
            Assert.Equal(CombatOutcome.Victory, ambushCombat.Outcome);
            Assert.Equal(125, ambushCombat.TotalXpEarned);
            Assert.True(ambushCombat.TotalGoldLoot >= 50);
        }
    }
}
