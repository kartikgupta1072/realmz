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

            if (Directory.Exists(@"C:\Users\karti\realmz\remaster\extracted\bywater_m1"))
            {
                return @"C:\Users\karti\realmz\remaster\extracted\bywater_m1";
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

            // Clean campaign starts at Day 1 - 08:00 AM (Morning) (480.0 min -> Phase Day)
            Assert.Equal(1, session.CurrentWorldClock.DayNumber);
            Assert.Equal(WorldTimePhase.Day, session.CurrentWorldClock.Phase);
            Assert.Equal(WorldClock.DefaultStartMinutes, session.WorldClockMinutes, 3);
            Assert.Equal("Day 1 - 08:00 AM (Morning)", session.CurrentWorldClock.DisplayLabel);

            // Full Rest in safe town adds +8 turns to TurnCount/WorldClockTurns and +480 minutes (8h) to WorldClockMinutes
            party.Members[0].Hp = 1;
            RestResult rest = session.TryCampRest(CampMode.FullRest, out _, out _);
            Assert.True(rest.Success);
            Assert.Equal(8, rest.TurnsElapsed);
            Assert.Equal(8, session.TurnCount);
            Assert.Equal(WorldClock.DefaultStartTurns + 8, session.WorldClockTurns);
            Assert.Equal(WorldClock.DefaultStartMinutes + WorldClock.FullRestMinutes, session.WorldClockMinutes, 3);
            Assert.Equal("Day 1 - 04:00 PM (Afternoon)", session.CurrentWorldClock.DisplayLabel);

            // Advance into Dusk (+10 turns -> +200 min -> 1160 min = 07:20 PM) then Night (+18 turns -> +360 min -> Day 2 01:20 AM)
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

            // Evade roll + ApplyStealthEvade advances 3 turns / 15 minutes and grants stealth grace without HP loss
            Assert.True(EncounterDirector.RollEvade(litReport, new QueuedDiceRoller(5)));
            int clockBeforeEvade = session.WorldClockTurns;
            double clockMinutesBeforeEvade = session.WorldClockMinutes;
            session.ApplyStealthEvade();
            Assert.Equal(clockBeforeEvade + 3, session.WorldClockTurns);
            Assert.Equal(clockMinutesBeforeEvade + 15.0, session.WorldClockMinutes, 3);
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

        [Fact]
        public void WorldClock_Continuous1440MinuteClock_LabelsProgressAndSessionStepRestWorkAccurately()
        {
            Assert.Equal(1440, WorldClock.MinutesPerDay);
            Assert.Equal(2, WorldClock.MinutesPerStep);
            Assert.Equal(480, WorldClock.FullRestMinutes);
            Assert.Equal(480.0, WorldClock.DefaultStartMinutes, 3);

            WorldClock morning = WorldClock.FromMinutes(480.0);
            Assert.Equal(1, morning.DayNumber);
            Assert.Equal(8, morning.Hour24);
            Assert.Equal(0, morning.MinuteOfHour);
            Assert.Equal(WorldTimePhase.Day, morning.Phase);
            Assert.Equal("Morning", morning.SubPhaseLabel);
            Assert.Equal("08:00 AM", morning.FormattedTime12h);
            Assert.Equal("Day 1 - 08:00 AM (Morning)", morning.DisplayLabel);
            Assert.Equal(570, morning.MinutesUntilNextPhase);
            Assert.Equal(285, morning.TurnsUntilNextPhase);

            WorldClock noon = WorldClock.FromMinutes(720.0);
            Assert.Equal("12:00 PM", noon.FormattedTime12h);
            Assert.Equal("Noon", noon.SubPhaseLabel);
            Assert.Equal(0.5f, noon.NormalizedTimeOfDay, 4);
            Assert.Equal((720.0f - 450.0f) / 600.0f, noon.PhaseProgress01, 4);

            WorldClock dusk = WorldClock.FromMinutes(1095.0); // 06:15 PM
            Assert.Equal(WorldTimePhase.Dusk, dusk.Phase);
            Assert.Equal("06:15 PM", dusk.FormattedTime12h);
            Assert.Equal("Day 1 - 06:15 PM (Dusk)", dusk.DisplayLabel);
            Assert.Equal(75, dusk.MinutesUntilNextPhase);
            Assert.Equal(38, dusk.TurnsUntilNextPhase);

            // Legacy 72-turn conversion preserves day & phase boundaries cleanly
            WorldClock legacyDawn = WorldClock.FromTurns(0);
            WorldClock legacyDay = WorldClock.FromTurns(18);
            WorldClock legacyDusk = WorldClock.FromTurns(36);
            WorldClock legacyNight58 = WorldClock.FromTurns(58);
            WorldClock legacyDay2Dawn = WorldClock.FromTurns(72);

            Assert.Equal(WorldTimePhase.Dawn, legacyDawn.Phase);
            Assert.Equal(morning, legacyDay);
            Assert.Equal(WorldTimePhase.Dusk, legacyDusk.Phase);
            Assert.Equal(1, legacyNight58.DayNumber);
            Assert.Equal(WorldTimePhase.Night, legacyNight58.Phase);
            Assert.Equal(2, legacyDay2Dawn.DayNumber);
            Assert.Equal(WorldTimePhase.Dawn, legacyDay2Dawn.Phase);

            // Session step (+2 min), AdvanceWorldClockMinutes phase crossing, and persistence restore
            int[] tiles = new int[400];
            Array.Fill(tiles, 156);
            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule> { [156] = new TileRule(156, passable: true) };
            EncounterZoneData wildZone = new EncounterZoneData(1, 0, 0, 19, 19, percent: 500, battleIds: new[] { 9 });
            TileMapData wildMap = new TileMapData("wild", "Bywater", 0, false, 20, 20, tiles, new GridCoord(0, 0), rules, encounterZones: new[] { wildZone });

            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["wild"] = wildMap },
                "wild",
                new PartyState(new[] { new PartyMemberState(new HeroPresetData("h1", "Aldric", hp: 20, maxHp: 20)) }),
                new DeterministicDiceRoller(99UL),
                initialWorldClockMinutes: 1040.0); // 05:20 PM Day

            Assert.Equal(WorldTimePhase.Day, session.CurrentWorldClock.Phase);
            ExplorationStepResult step = session.StepOneTile(new GridCoord(1, 0), autoHaltOnEvents: false);
            Assert.True(step.Moved);
            Assert.Equal(1042.0, session.WorldClockMinutes, 3);

            // Crossing into Dusk updates fog of war and clears regional zone cooldowns
            session.CompleteEncounterZone(wildZone, graceSteps: 0);
            Assert.True(session.IsEncounterZoneOnCooldown("wild", wildZone));
            Assert.False(session.AdvanceWorldClockMinutes(4.0)); // 1046.0 -> still Day
            Assert.True(session.IsEncounterZoneOnCooldown("wild", wildZone));
            Assert.True(session.AdvanceWorldClockMinutes(5.0)); // 1051.0 -> Dusk!
            Assert.Equal(WorldTimePhase.Dusk, session.CurrentWorldClock.Phase);
            Assert.False(session.IsEncounterZoneOnCooldown("wild", wildZone));
            Assert.Equal(8, session.CurrentVisionRadius);

            session.RestorePersistenceState(turnCount: 4, clearedEncounterIds: null, worldClockMinutes: 1380.0);
            Assert.Equal(1380.0, session.WorldClockMinutes, 3);
            Assert.Equal(WorldTimePhase.Night, session.CurrentWorldClock.Phase);
            Assert.Equal("Day 1 - 11:00 PM (Night)", session.CurrentWorldClock.DisplayLabel);
        }

        private static EncounterGeographyInfo MakeGeo(
            EncounterBiomeKind biome,
            int x = 24,
            int y = 37,
            int distanceToWater = 10,
            bool isOnRoad = false,
            bool isDeepForest = false,
            bool isNearRuins = false,
            bool isNearDungeonEntrance = false,
            bool isTownSafeZone = false)
        {
            return new EncounterGeographyInfo(
                biomeKind: biome,
                locationLabel: biome.ToString(),
                tileTerrainLabel: biome.ToString(),
                distanceToWater: distanceToWater,
                isOnRoad: isOnRoad,
                isCobblestone: false,
                isDeepForest: isDeepForest,
                isNearRuins: isNearRuins,
                isNearDungeonEntrance: isNearDungeonEntrance,
                isTownSafeZone: isTownSafeZone,
                probabilityMultiplier: 1.0);
        }

        private static bool HasCleanHorizontalSightCorridor(CombatGrid grid, GridCoord target, out GridCoord west, out GridCoord east)
        {
            west = new GridCoord(target.X - 2, target.Y);
            east = new GridCoord(target.X + 2, target.Y);
            if (!grid.InBounds(west) || !grid.InBounds(east))
            {
                return false;
            }

            for (int x = west.X; x <= east.X; x++)
            {
                if (x == target.X)
                {
                    continue;
                }

                if (grid.BlocksLos(x, target.Y) || grid.ProvidesCover(x, target.Y))
                {
                    return false;
                }
            }

            return !grid.GetCell(west).IsBlocked && !grid.GetCell(east).IsBlocked;
        }

        private static bool HasCleanVerticalSightCorridor(CombatGrid grid, GridCoord target, out GridCoord north, out GridCoord south)
        {
            north = new GridCoord(target.X, target.Y - 2);
            south = new GridCoord(target.X, target.Y + 2);
            if (!grid.InBounds(north) || !grid.InBounds(south))
            {
                return false;
            }

            for (int y = north.Y; y <= south.Y; y++)
            {
                if (y == target.Y)
                {
                    continue;
                }

                if (grid.BlocksLos(target.X, y) || grid.ProvidesCover(target.X, y))
                {
                    return false;
                }
            }

            return !grid.GetCell(north).IsBlocked && !grid.GetCell(south).IsBlocked;
        }

        [Fact]
        public void ResolveArenaArchetype_AndApplyBiomeFeatures_CoverAllFiveArchetypes()
        {
            GridCoord[] reserved =
            {
                new GridCoord(1, 4), new GridCoord(1, 5),
                new GridCoord(12, 4), new GridCoord(12, 5)
            };

            EncounterGeographyInfo roadGeo = MakeGeo(EncounterBiomeKind.Road, isOnRoad: true);
            EncounterGeographyInfo riverGeo = MakeGeo(EncounterBiomeKind.Riverbank, distanceToWater: 2);
            EncounterGeographyInfo nearWaterGeo = MakeGeo(EncounterBiomeKind.WildMeadow, distanceToWater: 2, isOnRoad: false);
            EncounterGeographyInfo woodsGeo = MakeGeo(EncounterBiomeKind.DeepForest, isDeepForest: true);
            EncounterGeographyInfo ruinsGeo = MakeGeo(EncounterBiomeKind.Ruins, isNearRuins: true);
            EncounterGeographyInfo approachGeo = MakeGeo(EncounterBiomeKind.DungeonApproach, isNearDungeonEntrance: true);
            EncounterGeographyInfo dungeonGeo = MakeGeo(EncounterBiomeKind.Dungeon, isNearDungeonEntrance: true);
            EncounterGeographyInfo townGeo = MakeGeo(EncounterBiomeKind.TownSafe, isTownSafeZone: true);

            Assert.Equal(CombatArenaArchetype.RoadAmbush, EncounterDirector.ResolveArenaArchetype(null));
            Assert.Equal(CombatArenaArchetype.RoadAmbush, EncounterDirector.ResolveArenaArchetype(roadGeo));
            Assert.Equal(CombatArenaArchetype.RoadAmbush, EncounterDirector.ResolveArenaArchetype(townGeo));
            Assert.Equal(CombatArenaArchetype.RiverbankCrossing, EncounterDirector.ResolveArenaArchetype(riverGeo));
            Assert.Equal(CombatArenaArchetype.RiverbankCrossing, EncounterDirector.ResolveArenaArchetype(nearWaterGeo));
            Assert.Equal(CombatArenaArchetype.DeepWoods, EncounterDirector.ResolveArenaArchetype(woodsGeo));
            Assert.Equal(CombatArenaArchetype.RuinsOutskirts, EncounterDirector.ResolveArenaArchetype(ruinsGeo));
            Assert.Equal(CombatArenaArchetype.RuinsOutskirts, EncounterDirector.ResolveArenaArchetype(approachGeo));
            Assert.Equal(CombatArenaArchetype.DungeonChamber, EncounterDirector.ResolveArenaArchetype(dungeonGeo));

            // RoadAmbush via ApplyBiomeArenaFeatures
            CombatGrid roadGrid = new CombatGrid(14, 10);
            EncounterDirector.ApplyBiomeArenaFeatures(roadGrid, roadGeo, reserved, new DeterministicDiceRoller(11UL));
            Assert.NotNull(roadGrid.ArenaLayout);
            Assert.Equal(CombatArenaArchetype.RoadAmbush, roadGrid.ArenaLayout!.Archetype);
            Assert.Contains(roadGrid.ArenaLayout.Features, f => f.IsRoadTile);
            Assert.Contains(roadGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.SupplyWagon);
            Assert.Contains(roadGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.StoneFence);
            Assert.Contains(roadGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.BarrelsCrates);
            Assert.Contains(roadGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.LitBrazier);
            Assert.Contains(roadGrid.ArenaLayout.Features, f => f.Surface == SurfaceType.Oil);
            Assert.Contains(roadGrid.ArenaLayout.Features, f => f.ElevationTier == 1);

            // RiverbankCrossing via ApplyBiomeArenaFeatures
            CombatGrid riverGrid = new CombatGrid(14, 10);
            EncounterDirector.ApplyBiomeArenaFeatures(riverGrid, riverGeo, reserved, new DeterministicDiceRoller(12UL));
            Assert.NotNull(riverGrid.ArenaLayout);
            Assert.Equal(CombatArenaArchetype.RiverbankCrossing, riverGrid.ArenaLayout!.Archetype);
            Assert.Contains(riverGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.BridgePlank && f.IsBridgeTile);
            Assert.Contains(riverGrid.ArenaLayout.Features, f => f.Surface == SurfaceType.Water);
            Assert.Contains(riverGrid.ArenaLayout.Features, f => f.IsMudBank && f.MoveCost == 2);
            Assert.Contains(riverGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.BrushThicket);
            Assert.Contains(riverGrid.ArenaLayout.Features, f => f.ElevationTier == 1);

            // DeepWoods via ApplyBiomeArenaFeatures
            CombatGrid woodsGrid = new CombatGrid(14, 10);
            EncounterDirector.ApplyBiomeArenaFeatures(woodsGrid, woodsGeo, reserved, new DeterministicDiceRoller(13UL));
            Assert.NotNull(woodsGrid.ArenaLayout);
            Assert.Equal(CombatArenaArchetype.DeepWoods, woodsGrid.ArenaLayout!.Archetype);
            Assert.Contains(woodsGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.TreeTrunk);
            Assert.Contains(woodsGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.BoulderRock);
            Assert.Contains(woodsGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.BrushThicket);
            Assert.Contains(woodsGrid.ArenaLayout.Features, f => f.ElevationTier == 1);

            // RuinsOutskirts via ApplyBiomeArenaFeatures
            CombatGrid ruinsGrid = new CombatGrid(14, 10);
            EncounterDirector.ApplyBiomeArenaFeatures(ruinsGrid, ruinsGeo, reserved, new DeterministicDiceRoller(14UL));
            Assert.NotNull(ruinsGrid.ArenaLayout);
            Assert.Equal(CombatArenaArchetype.RuinsOutskirts, ruinsGrid.ArenaLayout!.Archetype);
            Assert.Contains(ruinsGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.RuinedWall);
            Assert.Contains(ruinsGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.LitBrazier);
            Assert.Contains(ruinsGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.BarrelsCrates);
            Assert.Contains(ruinsGrid.ArenaLayout.Features, f => f.Surface == SurfaceType.Oil);
            Assert.Contains(ruinsGrid.ArenaLayout.Features, f => f.Surface == SurfaceType.Poison);
            Assert.Contains(ruinsGrid.ArenaLayout.Features, f => f.ElevationTier == 1 && f.IsRoadTile);

            // DungeonChamber via ApplyBiomeArenaFeatures
            CombatGrid dungeonGrid = new CombatGrid(14, 10);
            EncounterDirector.ApplyBiomeArenaFeatures(dungeonGrid, dungeonGeo, reserved, new DeterministicDiceRoller(15UL));
            Assert.NotNull(dungeonGrid.ArenaLayout);
            Assert.Equal(CombatArenaArchetype.DungeonChamber, dungeonGrid.ArenaLayout!.Archetype);
            Assert.Contains(dungeonGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.RuinedWall);
            Assert.Contains(dungeonGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.StonePillar);
            Assert.Contains(dungeonGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.WallSconce);
            Assert.Contains(dungeonGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.LitBrazier);
            Assert.Contains(dungeonGrid.ArenaLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.IronGrate);
            Assert.Contains(dungeonGrid.ArenaLayout.Features, f => f.ElevationTier == 1);
        }

        [Fact]
        public void ArenaArchetype_DeterministicSeeding_ProducesIdenticalLayouts()
        {
            GridCoord[] reserved =
            {
                new GridCoord(1, 3), new GridCoord(1, 6),
                new GridCoord(12, 3), new GridCoord(12, 6)
            };

            foreach (CombatArenaArchetype archetype in Enum.GetValues(typeof(CombatArenaArchetype)))
            {
                EncounterGeographyInfo geo = MakeGeo(EncounterBiomeKind.WildMeadow, x: 41, y: 63);
                CombatGrid gridA = new CombatGrid(14, 10);
                CombatGrid gridB = new CombatGrid(14, 10);

                CombatArenaLayout layoutA = EncounterDirector.ApplyArenaArchetype(
                    gridA, archetype, reserved, new DeterministicDiceRoller(987654321UL), geo);
                CombatArenaLayout layoutB = EncounterDirector.ApplyArenaArchetype(
                    gridB, archetype, reserved, new DeterministicDiceRoller(987654321UL), geo);

                Assert.Equal(layoutA.Archetype, layoutB.Archetype);
                Assert.Equal(layoutA.Seed, layoutB.Seed);
                Assert.Equal(layoutA.VerifiedLaneCount, layoutB.VerifiedLaneCount);
                Assert.Equal(layoutA.AllSpawnsConnected, layoutB.AllSpawnsConnected);
                Assert.Equal(layoutA.Features.Count, layoutB.Features.Count);

                for (int y = 0; y < 10; y++)
                {
                    for (int x = 0; x < 14; x++)
                    {
                        CombatGridCell cellA = gridA.GetCell(x, y);
                        CombatGridCell cellB = gridB.GetCell(x, y);
                        Assert.Equal(cellA.IsBlocked, cellB.IsBlocked);
                        Assert.Equal(cellA.BlocksLos, cellB.BlocksLos);
                        Assert.Equal(cellA.ProvidesCover, cellB.ProvidesCover);
                        Assert.Equal(cellA.ElevationTier, cellB.ElevationTier);
                        Assert.Equal(cellA.MoveCost, cellB.MoveCost);
                        Assert.Equal(cellA.Surface, cellB.Surface);
                        Assert.Equal(cellA.SurfaceRoundsRemaining, cellB.SurfaceRoundsRemaining);

                        CombatArenaFeature? featA = layoutA.GetFeatureAt(x, y);
                        CombatArenaFeature? featB = layoutB.GetFeatureAt(x, y);
                        Assert.Equal(featA == null, featB == null);
                        if (featA != null && featB != null)
                        {
                            Assert.Equal(featA.ObstacleKind, featB.ObstacleKind);
                            Assert.Equal(featA.IsRoadTile, featB.IsRoadTile);
                            Assert.Equal(featA.IsMudBank, featB.IsMudBank);
                            Assert.Equal(featA.IsBridgeTile, featB.IsBridgeTile);
                            Assert.Equal(featA.BlocksFoliage, featB.BlocksFoliage);
                        }
                    }
                }
            }
        }

        [Fact]
        public void ArenaObstaclesAndCover_AffectCheckLineOfSightProperly()
        {
            GridCoord[] reserved =
            {
                new GridCoord(1, 4), new GridCoord(12, 4)
            };

            CombatArenaArchetype[] solidArchetypes =
            {
                CombatArenaArchetype.RoadAmbush,
                CombatArenaArchetype.DeepWoods,
                CombatArenaArchetype.RuinsOutskirts,
                CombatArenaArchetype.DungeonChamber
            };
            CombatArenaObstacleKind[] expectedSolidKinds =
            {
                CombatArenaObstacleKind.SupplyWagon,
                CombatArenaObstacleKind.TreeTrunk,
                CombatArenaObstacleKind.RuinedWall,
                CombatArenaObstacleKind.StonePillar
            };

            for (int i = 0; i < solidArchetypes.Length; i++)
            {
                CombatGrid grid = new CombatGrid(14, 10);
                CombatArenaLayout layout = EncounterDirector.ApplyArenaArchetype(
                    grid, solidArchetypes[i], reserved, new DeterministicDiceRoller(100UL + (ulong)i));

                CombatArenaFeature solid = layout.Features.First(f => f.ObstacleKind == expectedSolidKinds[i]);
                Assert.True(solid.BlocksMovement);
                Assert.True(solid.BlocksLos);
                Assert.True(grid.GetCell(solid.X, solid.Y).IsBlocked);
                Assert.True(grid.BlocksLos(solid.X, solid.Y));

                TacticalCombatSession session = new TacticalCombatSession(
                    grid,
                    new[]
                    {
                        new CombatantState("h1", "Hero", CombatSide.Hero, new GridCoord(1, 4), 20, 20),
                        new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(12, 4), 10, 10)
                    },
                    roller: new DeterministicDiceRoller(55UL));

                GridCoord target = new GridCoord(solid.X, solid.Y);
                if (HasCleanHorizontalSightCorridor(grid, target, out GridCoord a, out GridCoord b) ||
                    HasCleanVerticalSightCorridor(grid, target, out a, out b))
                {
                    LosResult los = session.CheckLineOfSight(a, b);
                    Assert.Equal(LosKind.Blocked, los.Kind);
                    Assert.Equal((GridCoord?)target, los.BlockedBy);
                }
                else
                {
                    // Fallback: ray from one step left to one step right through solid tile.
                    GridCoord left = new GridCoord(solid.X - 1, solid.Y);
                    GridCoord right = new GridCoord(solid.X + 1, solid.Y);
                    LosResult los = session.CheckLineOfSight(left, right);
                    Assert.Equal(LosKind.Blocked, los.Kind);
                    Assert.Equal((GridCoord?)target, los.BlockedBy);
                }
            }

            // Verify cover obstacles (BarrelsCrates in RoadAmbush, BrushThicket in DeepWoods) keep walkability + PartialCover LOS.
            (CombatArenaArchetype arch, CombatArenaObstacleKind kind)[] coverCases =
            {
                (CombatArenaArchetype.RoadAmbush, CombatArenaObstacleKind.BarrelsCrates),
                (CombatArenaArchetype.DeepWoods, CombatArenaObstacleKind.BrushThicket)
            };

            for (int i = 0; i < coverCases.Length; i++)
            {
                CombatGrid grid = new CombatGrid(14, 10);
                CombatArenaLayout layout = EncounterDirector.ApplyArenaArchetype(
                    grid, coverCases[i].arch, reserved, new DeterministicDiceRoller(200UL + (ulong)i));

                CombatArenaFeature cover = layout.Features.First(f =>
                {
                    if (f.ObstacleKind != coverCases[i].kind)
                    {
                        return false;
                    }

                    GridCoord coord = new GridCoord(f.X, f.Y);
                    return HasCleanHorizontalSightCorridor(grid, coord, out _, out _) ||
                           HasCleanVerticalSightCorridor(grid, coord, out _, out _);
                });

                Assert.False(cover.BlocksMovement);
                Assert.False(cover.BlocksLos);
                Assert.True(cover.ProvidesCover);
                Assert.Equal(2, cover.MoveCost);

                TacticalCombatSession session = new TacticalCombatSession(
                    grid,
                    new[]
                    {
                        new CombatantState("h1", "Hero", CombatSide.Hero, new GridCoord(1, 4), 20, 20),
                        new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(12, 4), 10, 10)
                    },
                    roller: new DeterministicDiceRoller(77UL));

                GridCoord target = new GridCoord(cover.X, cover.Y);
                bool found = HasCleanHorizontalSightCorridor(grid, target, out GridCoord from, out GridCoord to) ||
                             HasCleanVerticalSightCorridor(grid, target, out from, out to);
                Assert.True(found);

                LosResult los = session.CheckLineOfSight(from, to);
                Assert.Equal(LosKind.PartialCover, los.Kind);
                Assert.Equal(15, los.CoverPenaltyPct);
            }
        }

        [Fact]
        public void ArenaHighGroundElevation_GrantsHitAndRangedReachBonuses()
        {
            GridCoord[] reserved =
            {
                new GridCoord(1, 4), new GridCoord(12, 4)
            };

            // Verify every archetype generates ElevationTier == 1 high-ground tiles.
            foreach (CombatArenaArchetype archetype in Enum.GetValues(typeof(CombatArenaArchetype)))
            {
                CombatGrid g = new CombatGrid(14, 10);
                CombatArenaLayout layout = EncounterDirector.ApplyArenaArchetype(
                    g, archetype, reserved, new DeterministicDiceRoller(300UL));
                Assert.Contains(layout.Features, f => f.ElevationTier == 1 && !f.BlocksMovement);
            }

            // Use RuinsOutskirts dais (ElevationTier == 1) to test melee +15 hit and ranged +15 hit / +1 reach.
            CombatGrid ruinsGrid = new CombatGrid(14, 10);
            CombatArenaLayout ruinsLayout = EncounterDirector.ApplyArenaArchetype(
                ruinsGrid, CombatArenaArchetype.RuinsOutskirts, reserved, new DeterministicDiceRoller(305UL));

            CombatArenaFeature highTile = ruinsLayout.Features.First(f =>
                f.ElevationTier == 1 &&
                !f.BlocksMovement &&
                ruinsGrid.InBounds(f.X + 1, f.Y) &&
                !ruinsGrid.GetCell(f.X + 1, f.Y).IsBlocked &&
                ruinsGrid.GetElevation(f.X + 1, f.Y) == 0);

            GridCoord highPos = new GridCoord(highTile.X, highTile.Y);
            GridCoord lowAdjPos = new GridCoord(highTile.X + 1, highTile.Y);

            CombatantState hero = new CombatantState(
                "h1", "Kaelen", CombatSide.Hero, highPos,
                hp: 30, maxHp: 30, ac: 14, toHit: 40, missileSkill: 50,
                dexterity: 18, weaponDice: "1d8", rangedDice: "1d6", rangedRange: 4);
            CombatantState adjGoblin = new CombatantState(
                "m_adj", "GoblinAdj", CombatSide.Monster, lowAdjPos,
                hp: 15, maxHp: 15, ac: 12, dexterity: 8);

            TacticalCombatSession meleeSession = new TacticalCombatSession(
                ruinsGrid,
                new[] { hero, adjGoblin },
                roller: new DeterministicDiceRoller(19UL));

            TacticalAttackPreview meleePreview = meleeSession.PreviewMeleeAttack("h1", "m_adj");
            HitPreview flatBaseline = CombatRules.CalculateHitPreview(
                attackerToHit: hero.EffectiveToHit,
                defenderAc: adjGoblin.EffectiveAc,
                attackerElevation: 0,
                defenderElevation: 0);

            Assert.True(meleePreview.CanExecute);
            Assert.Equal(15, meleePreview.ElevationModifierPct);
            Assert.Contains("High Ground +15%", meleePreview.ConditionTags);
            Assert.Equal(flatBaseline.UncappedHitScore + 15, meleePreview.UncappedHitScore);

            // Ranged +1 reach & +15 hit test on clean DungeonChamber / open corridor with a generated altar tile.
            CombatGrid dungeonGrid = new CombatGrid(14, 10);
            CombatArenaLayout dungeonLayout = EncounterDirector.ApplyArenaArchetype(
                dungeonGrid, CombatArenaArchetype.DungeonChamber, reserved, new DeterministicDiceRoller(309UL));
            CombatArenaFeature altarTile = dungeonLayout.Features.First(f => f.ElevationTier == 1 && !f.BlocksMovement);

            // Find a tier-0 tile at Chebyshev distance 5 (= RangedRange + 1 when RangedRange == 4) with clear LOS from altarTile.
            GridCoord altarPos = new GridCoord(altarTile.X, altarTile.Y);
            GridCoord? farTargetPos = null;
            for (int y = 1; y < dungeonGrid.Height - 1 && farTargetPos == null; y++)
            {
                for (int x = 1; x < dungeonGrid.Width - 1; x++)
                {
                    GridCoord candidate = new GridCoord(x, y);
                    if (altarPos.ChebyshevDistance(candidate) == 5 &&
                        !dungeonGrid.GetCell(candidate).IsBlocked &&
                        dungeonGrid.GetElevation(candidate) == 0)
                    {
                        TacticalCombatSession probe = new TacticalCombatSession(
                            dungeonGrid,
                            new[]
                            {
                                new CombatantState("p1", "Archer", CombatSide.Hero, altarPos, 20, 20),
                                new CombatantState("p2", "Target", CombatSide.Monster, candidate, 10, 10)
                            },
                            roller: new DeterministicDiceRoller(1UL));
                        if (probe.CheckLineOfSight(altarPos, candidate).Kind != LosKind.Blocked)
                        {
                            farTargetPos = candidate;
                            break;
                        }
                    }
                }
            }

            Assert.NotNull(farTargetPos);

            CombatantState archer = new CombatantState(
                "h_rng", "Archer", CombatSide.Hero, altarPos,
                hp: 24, maxHp: 24, missileSkill: 50, dexterity: 18,
                rangedDice: "1d6", rangedRange: 4);
            CombatantState farGoblin = new CombatantState(
                "m_far", "GoblinFar", CombatSide.Monster, farTargetPos!.Value,
                hp: 15, maxHp: 15, ac: 12, dexterity: 8);

            TacticalCombatSession rangedSession = new TacticalCombatSession(
                dungeonGrid,
                new[] { archer, farGoblin },
                roller: new DeterministicDiceRoller(23UL));

            TacticalAttackPreview highRanged = rangedSession.PreviewRangedAttack("h_rng", "m_far");
            Assert.True(highRanged.CanExecute);
            Assert.Equal(15, highRanged.ElevationModifierPct);
            Assert.Contains("High Ground +15%", highRanged.ConditionTags);

            // Drop altar tile to ElevationTier == 0: distance 5 now exceeds RangedRange 4!
            dungeonGrid.SetElevation(altarPos.X, altarPos.Y, 0);
            TacticalAttackPreview flatRanged = rangedSession.PreviewRangedAttack("h_rng", "m_far");
            Assert.False(flatRanged.CanExecute);
            Assert.Equal("Target out of ranged weapon range.", flatRanged.Reason);
        }

        [Fact]
        public void ArenaElementalSurfaces_AndInteractions_WorkAcrossTemplates()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            GridCoord[] reserved =
            {
                new GridCoord(1, 4), new GridCoord(12, 4)
            };

            // 1. RoadAmbush: Oil + LitBrazier context, Heat fireball ignites Oil to Fire.
            CombatGrid roadGrid = new CombatGrid(14, 10);
            CombatArenaLayout roadLayout = EncounterDirector.ApplyArenaArchetype(
                roadGrid, CombatArenaArchetype.RoadAmbush, reserved, new DeterministicDiceRoller(401UL));
            Assert.Contains(roadLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.LitBrazier);
            CombatArenaFeature roadOil = roadLayout.Features.First(f => f.Surface == SurfaceType.Oil);

            // Position Theron on an unobstructed south flank so SupplyWagon cover doesn't block Fireball LOS.
            GridCoord casterStart = FindClearLineCasterSpot(
                roadGrid,
                new GridCoord(roadOil.X, roadOil.Y),
                new GridCoord(12, 4),
                bundle.Spells,
                new GridCoord(Math.Max(1, roadOil.X - 2), Math.Min(roadGrid.Height - 2, roadOil.Y + 1)));

            CombatantState roadMage = new CombatantState(
                "h_mage", "Theron", CombatSide.Hero, casterStart,
                hp: 20, maxHp: 20, sp: 30, maxSp: 30, dexterity: 20,
                spellIds: new[] { 1306 });
            CombatantState roadGoblin = new CombatantState(
                "m1", "Goblin", CombatSide.Monster, new GridCoord(12, 4),
                hp: 15, maxHp: 15, dexterity: 6);
            TacticalCombatSession roadSession = new TacticalCombatSession(
                roadGrid,
                new[] { roadMage, roadGoblin },
                spells: bundle.Spells,
                roller: new DeterministicDiceRoller(401UL));

            CombatActionResult igniteRes = roadSession.CastSpell("fireball", new GridCoord(roadOil.X, roadOil.Y));
            Assert.True(igniteRes.Success);
            Assert.Equal(SurfaceType.Fire, roadGrid.GetSurface(roadOil.X, roadOil.Y));

            // 2. RiverbankCrossing: Water stream + 2 dry bridge/ford lanes + MudBank + Cold freezes Water to Ice + Heat fireball reaction.
            CombatGrid riverGrid = new CombatGrid(14, 10);
            CombatArenaLayout riverLayout = EncounterDirector.ApplyArenaArchetype(
                riverGrid, CombatArenaArchetype.RiverbankCrossing, reserved, new DeterministicDiceRoller(402UL));

            List<int> bridgeRows = riverLayout.Features
                .Where(f => f.IsBridgeTile && f.ObstacleKind == CombatArenaObstacleKind.BridgePlank)
                .Select(f => f.Y)
                .Distinct()
                .ToList();
            Assert.Equal(2, bridgeRows.Count);
            Assert.All(
                riverLayout.Features.Where(f => f.IsBridgeTile),
                b =>
                {
                    Assert.Equal(SurfaceType.None, riverGrid.GetSurface(b.X, b.Y));
                    Assert.Equal(1, riverGrid.GetMoveCost(b.X, b.Y));
                });
            Assert.Contains(riverLayout.Features, f => f.IsMudBank && f.MoveCost == 2);

            CombatArenaFeature riverWater = riverLayout.Features.First(f => f.Surface == SurfaceType.Water);
            Dictionary<int, SpellDefinition> comboSpells = new Dictionary<int, SpellDefinition>(bundle.Spells)
            {
                [9901] = new SpellDefinition(
                    id: 9901,
                    key: "frost_bolt",
                    name: "Frost Bolt",
                    school: "Sorcerer",
                    level: 1,
                    spCost: 3,
                    range: 10,
                    targetMode: "Single",
                    spellAreaIndex: 0,
                    aoeOffsets: new[] { new GridCoord(0, 0) },
                    effectKind: "Damage",
                    effectDice: "1d6",
                    damageType: "Cold",
                    baseRecoveryPct: 0,
                    saveKind: "Magic")
            };

            GridCoord frostStart = FindClearLineCasterSpot(
                riverGrid,
                new GridCoord(riverWater.X, riverWater.Y),
                new GridCoord(12, 4),
                comboSpells,
                new GridCoord(1, 4));

            CombatantState frostMage = new CombatantState(
                "h_frost", "Theron", CombatSide.Hero, frostStart,
                hp: 20, maxHp: 20, sp: 30, maxSp: 30, dexterity: 20,
                spellIds: new[] { 9901, 1306 });
            CombatantState riverFrog = new CombatantState(
                "m_frog", "GiantFrog", CombatSide.Monster, new GridCoord(12, 4),
                hp: 15, maxHp: 15, dexterity: 6);
            TacticalCombatSession riverSession = new TacticalCombatSession(
                riverGrid,
                new[] { frostMage, riverFrog },
                spells: comboSpells,
                roller: new DeterministicDiceRoller(402UL));

            CombatActionResult freezeRes = riverSession.CastSpell("frost_bolt", new GridCoord(riverWater.X, riverWater.Y));
            Assert.True(freezeRes.Success);
            Assert.Equal(SurfaceType.Ice, riverGrid.GetSurface(riverWater.X, riverWater.Y));

            // Next turn, cast Heat fireball on frozen Ice tile -> melts back to Water!
            riverSession.EndTurn();
            riverSession.EndTurn();
            CombatActionResult meltRes = riverSession.CastSpell("fireball", new GridCoord(riverWater.X, riverWater.Y));
            Assert.True(meltRes.Success);
            Assert.Equal(SurfaceType.Water, riverGrid.GetSurface(riverWater.X, riverWater.Y));

            // 3. RuinsOutskirts: Oil and Poison puddles present.
            CombatGrid ruinsGrid = new CombatGrid(14, 10);
            CombatArenaLayout ruinsLayout = EncounterDirector.ApplyArenaArchetype(
                ruinsGrid, CombatArenaArchetype.RuinsOutskirts, reserved, new DeterministicDiceRoller(403UL));
            Assert.Contains(ruinsLayout.Features, f => f.Surface == SurfaceType.Oil);
            Assert.Contains(ruinsLayout.Features, f => f.Surface == SurfaceType.Poison);

            // 4. DungeonChamber: IronGrate Oil/Water tiles, plus Ice grate on variant == 2.
            CombatGrid dungeonGrid = new CombatGrid(14, 10);
            CombatArenaLayout dungeonLayout = EncounterDirector.ApplyArenaArchetype(
                dungeonGrid, CombatArenaArchetype.DungeonChamber, reserved, new DeterministicDiceRoller(404UL));
            Assert.Contains(dungeonLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.IronGrate && f.Surface == SurfaceType.Oil);
            Assert.Contains(dungeonLayout.Features, f => f.ObstacleKind == CombatArenaObstacleKind.IronGrate && f.Surface == SurfaceType.Water);

            CombatArenaLayout? iceDungeonLayout = null;
            for (ulong seed = 1; seed <= 30; seed++)
            {
                CombatGrid probeGrid = new CombatGrid(14, 10);
                CombatArenaLayout probeLayout = EncounterDirector.ApplyArenaArchetype(
                    probeGrid, CombatArenaArchetype.DungeonChamber, reserved, new DeterministicDiceRoller(seed));
                if (probeLayout.Seed % 3UL == 2UL)
                {
                    iceDungeonLayout = probeLayout;
                    break;
                }
            }

            Assert.NotNull(iceDungeonLayout);
            Assert.Contains(iceDungeonLayout!.Features, f => f.ObstacleKind == CombatArenaObstacleKind.IronGrate && f.Surface == SurfaceType.Ice);
        }

        [Fact]
        public void ArenaMultiLaneBfsConnectivity_PreventsTrappedUnits_EvenWithPreBlockedTiles()
        {
            GridCoord[] reserved =
            {
                new GridCoord(1, 3), new GridCoord(1, 5),
                new GridCoord(12, 3), new GridCoord(12, 5)
            };

            foreach (CombatArenaArchetype archetype in Enum.GetValues(typeof(CombatArenaArchetype)))
            {
                CombatGrid grid = new CombatGrid(14, 10);

                // Deliberately block reserved spawn tiles and a full vertical wall across x = 6 before applying archetype.
                grid.SetBlocked(1, 3, blocksLos: true);
                grid.SetBlocked(12, 5, blocksLos: true);
                for (int y = 0; y < 10; y++)
                {
                    grid.SetBlocked(6, y, blocksLos: true);
                }

                CombatArenaLayout layout = EncounterDirector.ApplyArenaArchetype(
                    grid, archetype, reserved, new DeterministicDiceRoller(777UL));

                Assert.True(layout.AllSpawnsConnected);
                Assert.True(layout.VerifiedLaneCount >= 2);

                foreach (GridCoord spawn in reserved)
                {
                    Assert.True(grid.IsWalkable(spawn.X, spawn.Y));
                }

                // Independent BFS from every west spawn to every east spawn.
                GridCoord[] westSpawns = { reserved[0], reserved[1] };
                GridCoord[] eastSpawns = { reserved[2], reserved[3] };
                foreach (GridCoord w in westSpawns)
                {
                    foreach (GridCoord e in eastSpawns)
                    {
                        Assert.True(HasWalkablePath(grid, w, e, out _));
                    }
                }

                // Verify at least 2 distinct interior crossing lanes.
                Assert.True(HasWalkablePath(grid, reserved[0], reserved[2], out List<GridCoord> firstLane));
                HashSet<GridCoord> blockedInterior = new HashSet<GridCoord>(
                    firstLane.Where(c => c.X >= 2 && c.X <= grid.Width - 3 && !reserved.Contains(c)));
                Assert.True(HasWalkablePath(grid, reserved[0], reserved[2], out _, blockedInterior));
            }
        }

        private static bool HasWalkablePath(
            CombatGrid grid,
            GridCoord start,
            GridCoord goal,
            out List<GridCoord> path,
            HashSet<GridCoord>? extraForbidden = null)
        {
            path = new List<GridCoord>();
            if (!grid.IsWalkable(start.X, start.Y) || !grid.IsWalkable(goal.X, goal.Y))
            {
                return false;
            }

            Dictionary<GridCoord, GridCoord> parent = new Dictionary<GridCoord, GridCoord>();
            Queue<GridCoord> q = new Queue<GridCoord>();
            parent[start] = start;
            q.Enqueue(start);

            int[] dxs = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dys = { 0, 0, 1, -1, 1, -1, 1, -1 };

            while (q.Count > 0)
            {
                GridCoord cur = q.Dequeue();
                if (cur.Equals(goal))
                {
                    GridCoord p = cur;
                    while (!p.Equals(start))
                    {
                        path.Add(p);
                        p = parent[p];
                    }
                    path.Add(start);
                    path.Reverse();
                    return true;
                }

                for (int d = 0; d < 8; d++)
                {
                    int nx = cur.X + dxs[d];
                    int ny = cur.Y + dys[d];
                    if (!grid.InBounds(nx, ny))
                    {
                        continue;
                    }

                    GridCoord next = new GridCoord(nx, ny);
                    if (parent.ContainsKey(next) || !grid.IsWalkable(nx, ny))
                    {
                        continue;
                    }

                    if (extraForbidden != null && extraForbidden.Contains(next))
                    {
                        continue;
                    }

                    if (Math.Abs(grid.GetElevation(nx, ny) - grid.GetElevation(cur.X, cur.Y)) > 1)
                    {
                        continue;
                    }

                    if (d >= 4 && (!grid.IsWalkable(cur.X + dxs[d], cur.Y) || !grid.IsWalkable(cur.X, cur.Y + dys[d])))
                    {
                        continue;
                    }

                    parent[next] = cur;
                    q.Enqueue(next);
                }
            }

            return false;
        }

        [Theory]
        [InlineData(CombatArenaArchetype.RoadAmbush, "goblin", "goblin_archer", "goblin")]
        [InlineData(CombatArenaArchetype.RiverbankCrossing, "giant_frog", "giant_frog", "goblin")]
        [InlineData(CombatArenaArchetype.DeepWoods, "giant_spider", "goblin_archer", "giant_rat")]
        [InlineData(CombatArenaArchetype.RuinsOutskirts, "zombie", "skeletal_warrior", "giant_rat")]
        [InlineData(CombatArenaArchetype.DungeonChamber, "orc_warrior", "orc_archer", "skeletal_warrior")]
        public void ArenaAiCombatStability_AllFiveArchetypes_CompleteOrProgressCleanly(
            CombatArenaArchetype archetype,
            string mobId1,
            string mobId2,
            string mobId3)
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            PartyState party = PartyState.FromBundle(bundle);
            DeterministicDiceRoller roller = new DeterministicDiceRoller(2026UL + (ulong)archetype);

            GridCoord[] heroSpawns =
            {
                new GridCoord(1, 3), new GridCoord(1, 4), new GridCoord(1, 5), new GridCoord(1, 6)
            };
            GridCoord[] mobSpawns =
            {
                new GridCoord(12, 3), new GridCoord(12, 5), new GridCoord(12, 6)
            };
            GridCoord[] reserved = heroSpawns.Concat(mobSpawns).ToArray();

            CombatGrid grid = new CombatGrid(14, 10);
            CombatArenaLayout layout = EncounterDirector.ApplyArenaArchetype(grid, archetype, reserved, roller);
            Assert.True(layout.AllSpawnsConnected);
            Assert.True(layout.VerifiedLaneCount >= 2);

            List<CombatantState> units = new List<CombatantState>();
            for (int i = 0; i < 4; i++)
            {
                units.Add(CombatantState.FromHero(party.Members[i], heroSpawns[i], bundle.Items));
            }

            string[] mobIds = { mobId1, mobId2, mobId3 };
            for (int i = 0; i < mobIds.Length; i++)
            {
                MonsterDefinition def = bundle.Monsters.First(m => m.Id == mobIds[i]);
                units.Add(CombatantState.FromMonster(def, $"mob_{i}", mobSpawns[i], roller));
            }

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                units,
                bundle.Spells,
                bundle.Items,
                roller,
                RulesetMode.ModernBalanced,
                party);

            int steps = 0;
            int prevTurn = session.TurnNumber;
            while (session.Outcome == CombatOutcome.InProgress && steps < 180)
            {
                steps++;
                CombatActionResult res = session.StepAutoCombatTurn();
                Assert.NotNull(res);
                Assert.True(session.TurnNumber > prevTurn || session.Outcome != CombatOutcome.InProgress);
                prevTurn = session.TurnNumber;
            }

            Assert.True(steps > 0);
            Assert.NotEmpty(session.Log);
            Assert.NotEqual(CombatOutcome.InProgress, session.Outcome);
        }

        private static GridCoord FindClearLineCasterSpot(
            CombatGrid grid,
            GridCoord target,
            GridCoord enemySpot,
            IReadOnlyDictionary<int, SpellDefinition> spells,
            GridCoord fallback)
        {
            for (int ty = 1; ty < grid.Height - 1; ty++)
            {
                for (int tx = 1; tx < grid.Width - 1; tx++)
                {
                    GridCoord cand = new GridCoord(tx, ty);
                    if (grid.IsWalkable(tx, ty) &&
                        !cand.Equals(target) &&
                        !cand.Equals(enemySpot) &&
                        cand.ChebyshevDistance(target) <= 4 &&
                        !grid.BlocksLos(tx, ty))
                    {
                        var probeSession = new TacticalCombatSession(
                            grid,
                            new[]
                            {
                                new CombatantState("p", "P", CombatSide.Hero, cand, 10, 10),
                                new CombatantState("e", "E", CombatSide.Monster, enemySpot, 10, 10)
                            },
                            spells: spells,
                            roller: new DeterministicDiceRoller(1UL));
                        if (probeSession.CheckLineOfSight(cand, target).Kind == LosKind.Clear)
                        {
                            return cand;
                        }
                    }
                }
            }
            return fallback;
        }
    }
}
