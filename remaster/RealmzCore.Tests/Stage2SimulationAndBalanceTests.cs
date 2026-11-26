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
    public class Stage2SimulationAndBalanceTests
    {
        private static string ResolveRepoRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "extracted", "bywater_m1")) &&
                    Directory.Exists(Path.Combine(current.FullName, "RealmzUnity")))
                {
                    return current.FullName;
                }

                string remasterCandidate = Path.Combine(current.FullName, "remaster");
                if (Directory.Exists(Path.Combine(remasterCandidate, "extracted", "bywater_m1")))
                {
                    return remasterCandidate;
                }

                current = current.Parent;
            }

            return @"C:\Users\karti\realmz\remaster";
        }

        private static TileMapData BuildOpenTileMap(
            string mapId,
            int width,
            int height,
            bool isDungeon,
            GridCoord startSpawn,
            IReadOnlyDictionary<int, TileRule>? rules = null,
            int[]? overrideTiles = null)
        {
            int[] tiles = overrideTiles ?? new int[width * height];
            if (overrideTiles == null)
            {
                int fill = isDungeon ? 1 : 61;
                for (int i = 0; i < tiles.Length; i++)
                {
                    tiles[i] = fill;
                }
            }

            Dictionary<int, TileRule> resolvedRules = rules != null
                ? new Dictionary<int, TileRule>(rules)
                : new Dictionary<int, TileRule>
                {
                    [1] = new TileRule(1, passable: true, moveCost: 1, blocksLos: false),
                    [61] = new TileRule(61, passable: true, moveCost: 1, blocksLos: false)
                };

            return new TileMapData(
                mapId: mapId,
                scenario: "Bywater",
                level: 0,
                isDungeon: isDungeon,
                width: width,
                height: height,
                tiles: tiles,
                startSpawn: startSpawn,
                tileRules: resolvedRules);
        }

        private static PartyState BuildSingleHeroParty(
            string archetype = "Knight",
            string name = "Aldric",
            string id = "hero_knight",
            int gold = 100,
            int torchTurns = 0,
            int foodRations = PartyState.DefaultFoodRations)
        {
            HeroPresetData preset = new HeroPresetData(
                id: id,
                name: name,
                archetype: archetype,
                level: 1,
                hp: 30,
                maxHp: 30,
                sp: 10,
                maxSp: 10,
                ac: 12,
                toHit: 20,
                dodge: 10,
                missile: 15,
                movementMax: 6);
            PartyMemberState member = new PartyMemberState(preset);
            return new PartyState(
                new[] { member },
                gold: gold,
                torchTurnsRemaining: torchTurns,
                foodRations: foodRations);
        }

        // =====================================================================
        // 1. Fog-of-War & Vision Radii
        // =====================================================================

        [Fact]
        public void FogOfWar_VisibilityStateTransitions_DowngradeToExploredAndRestoreCurrentlyVisible()
        {
            Assert.Equal((byte)0, (byte)FogVisibilityState.Unseen);
            Assert.Equal((byte)1, (byte)FogVisibilityState.Explored);
            Assert.Equal((byte)2, (byte)FogVisibilityState.CurrentlyVisible);

            TileMapData land = BuildOpenTileMap("land_0", 40, 40, isDungeon: false, startSpawn: new GridCoord(5, 5));
            PartyState party = BuildSingleHeroParty();
            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["land_0"] = land },
                "land_0",
                party,
                initialWorldClockTurns: WorldClock.DefaultStartTurns);

            FogOfWarMap fog = session.FogOfWar;
            Assert.Equal(11, session.CurrentVisionRadius);
            Assert.Equal(FogVisibilityState.CurrentlyVisible, fog.GetState(5, 5));
            Assert.Equal(FogVisibilityState.CurrentlyVisible, fog.GetState(10, 5));
            Assert.Equal(FogVisibilityState.Unseen, fog.GetState(38, 38));

            // Move 20 tiles east so (5, 5) falls outside the 11-tile Day vision radius.
            ExplorationMoveResult moveEast = session.MoveAlongPath(new GridCoord(25, 5));
            Assert.Equal(ExplorationHaltReason.Arrived, moveEast.HaltReason);
            Assert.Equal(FogVisibilityState.Explored, fog.GetState(5, 5));
            Assert.True(fog.IsExplored(5, 5));
            Assert.False(fog.IsVisible(5, 5));
            Assert.NotEqual(FogVisibilityState.Unseen, fog.GetState(5, 5));
            Assert.Equal(FogVisibilityState.CurrentlyVisible, fog.GetState(25, 5));

            // Stepping back within radius restores CurrentlyVisible on (5, 5).
            ExplorationMoveResult moveBack = session.MoveAlongPath(new GridCoord(10, 5));
            Assert.Equal(ExplorationHaltReason.Arrived, moveBack.HaltReason);
            Assert.Equal(FogVisibilityState.CurrentlyVisible, fog.GetState(5, 5));
            Assert.True(fog.IsVisible(5, 5));
        }

        [Fact]
        public void FogOfWar_BlocksLineOfSightAndPreventsDiagonalCornerClipping()
        {
            const int wallTile = 999;
            int[] tiles = new int[15 * 15];
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = 61;
            }

            // Straight wall at (6, 4) blocking tiles east of it.
            tiles[4 * 15 + 6] = wallTile;

            // Two adjacent diagonal corner blockers around (3, 10): (4, 10) and (3, 11).
            tiles[10 * 15 + 4] = wallTile;
            tiles[11 * 15 + 3] = wallTile;

            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule>
            {
                [61] = new TileRule(61, passable: true, moveCost: 1, blocksLos: false),
                [wallTile] = new TileRule(wallTile, passable: false, moveCost: 1, blocksLos: true)
            };

            TileMapData map = BuildOpenTileMap("los_test", 15, 15, isDungeon: false, startSpawn: new GridCoord(3, 4), rules: rules, overrideTiles: tiles);
            FogOfWarMap fog = new FogOfWarMap("los_test", 15, 15);

            // Straight LOS check from (3, 4) across wall at (6, 4).
            fog.RecomputeVisibility(map, new GridCoord(3, 4), visionRadius: 6);
            Assert.Equal(FogVisibilityState.CurrentlyVisible, fog.GetState(5, 4));
            Assert.Equal(FogVisibilityState.CurrentlyVisible, fog.GetState(6, 4));
            Assert.Equal(FogVisibilityState.Unseen, fog.GetState(7, 4));
            Assert.Equal(FogVisibilityState.Unseen, fog.GetState(8, 4));

            // Diagonal corner clipping check from (3, 10) across (4, 10) + (3, 11) toward (4, 11) and (5, 12).
            fog.RecomputeVisibility(map, new GridCoord(3, 10), visionRadius: 4);
            Assert.Equal(FogVisibilityState.CurrentlyVisible, fog.GetState(4, 10));
            Assert.Equal(FogVisibilityState.CurrentlyVisible, fog.GetState(3, 11));
            Assert.Equal(FogVisibilityState.Unseen, fog.GetState(4, 11));
            Assert.Equal(FogVisibilityState.Unseen, fog.GetState(5, 12));
            Assert.False(fog.IsExplored(4, 11));
        }

        [Fact]
        public void ExplorationSession_VisionRadii_RespectTimeOfDayScoutsTorchesAndDungeon()
        {
            TileMapData land = BuildOpenTileMap("land_0", 35, 35, isDungeon: false, startSpawn: new GridCoord(17, 17));
            TileMapData dungeon = BuildOpenTileMap("dungeon_0", 35, 35, isDungeon: true, startSpawn: new GridCoord(17, 17));
            Dictionary<string, TileMapData> maps = new Dictionary<string, TileMapData>
            {
                ["land_0"] = land,
                ["dungeon_0"] = dungeon
            };

            // 1. Dawn (turn 0) & Day (turn 18): base 11 without Ranger/Rogue.
            PartyState nonScoutParty = BuildSingleHeroParty(archetype: "Knight", name: "Aldric", id: "hero_knight");
            ExplorationSession dawnSession = new ExplorationSession(maps, "land_0", nonScoutParty, initialWorldClockTurns: 0);
            Assert.Equal(WorldTimePhase.Dawn, dawnSession.CurrentWorldClock.Phase);
            Assert.Equal(11, dawnSession.ComputeVisibilityRadius());
            Assert.Equal(11, dawnSession.CurrentVisionRadius);

            ExplorationSession daySession = new ExplorationSession(maps, "land_0", nonScoutParty, initialWorldClockTurns: WorldClock.TurnsPerPhase);
            Assert.Equal(WorldTimePhase.Day, daySession.CurrentWorldClock.Phase);
            Assert.Equal(11, daySession.ComputeVisibilityRadius());

            // 2. Conscious Ranger or Rogue upgrades Dawn/Day radius to 13; incapacitated scout drops back to 11.
            PartyState rangerParty = BuildSingleHeroParty(archetype: "Ranger", name: "Kaelen", id: "hero_archer");
            ExplorationSession rangerDay = new ExplorationSession(maps, "land_0", rangerParty, initialWorldClockTurns: WorldClock.TurnsPerPhase);
            Assert.Equal(13, rangerDay.ComputeVisibilityRadius());
            Assert.Equal(13, rangerDay.CurrentVisionRadius);

            PartyState rogueParty = BuildSingleHeroParty(archetype: "Rogue", name: "Vex", id: "hero_rogue");
            ExplorationSession rogueDawn = new ExplorationSession(maps, "land_0", rogueParty, initialWorldClockTurns: 0);
            Assert.Equal(13, rogueDawn.ComputeVisibilityRadius());

            rogueParty.Members[0].Conditions.Apply(CharConditionId.Helpless);
            rogueDawn.RefreshFogOfWar();
            Assert.Equal(11, rogueDawn.CurrentVisionRadius);

            // 3. Dusk (turn 36): 8 unlit, 10 lit Torch.
            PartyState torchParty = BuildSingleHeroParty(archetype: "Knight", name: "Aldric", id: "hero_knight");
            torchParty.AddItemToStash(PartyState.TorchItemId, 3);
            ExplorationSession duskSession = new ExplorationSession(maps, "land_0", torchParty, initialWorldClockTurns: WorldClock.TurnsPerPhase * 2);
            Assert.Equal(WorldTimePhase.Dusk, duskSession.CurrentWorldClock.Phase);
            Assert.Equal(8, duskSession.ComputeVisibilityRadius());
            Assert.Equal(8, duskSession.CurrentVisionRadius);

            Assert.True(duskSession.TryLightTorchFromStash(out _));
            Assert.Equal(10, duskSession.ComputeVisibilityRadius());
            Assert.Equal(10, duskSession.CurrentVisionRadius);

            // 4. Night (turn 54): 4 unlit, 9 lit Torch.
            duskSession.ExtinguishTorch();
            duskSession.AdvanceWorldClockTurns(WorldClock.TurnsPerPhase);
            Assert.Equal(WorldTimePhase.Night, duskSession.CurrentWorldClock.Phase);
            Assert.Equal(4, duskSession.ComputeVisibilityRadius());
            Assert.Equal(4, duskSession.CurrentVisionRadius);

            Assert.True(duskSession.TryLightTorchFromStash(out _));
            Assert.Equal(9, duskSession.ComputeVisibilityRadius());
            Assert.Equal(9, duskSession.CurrentVisionRadius);

            // 5. Underground dungeon_0: 3 unlit, 8 lit Torch.
            PartyState dungeonParty = BuildSingleHeroParty(archetype: "Ranger", name: "Kaelen", id: "hero_archer");
            dungeonParty.AddItemToStash(PartyState.TorchItemId, 1);
            ExplorationSession dungeonSession = new ExplorationSession(maps, "dungeon_0", dungeonParty, initialWorldClockTurns: WorldClock.TurnsPerPhase);
            Assert.Equal(3, dungeonSession.ComputeVisibilityRadius());
            Assert.Equal(3, dungeonSession.CurrentVisionRadius);

            Assert.True(dungeonSession.TryLightTorchFromStash(out _));
            Assert.Equal(8, dungeonSession.ComputeVisibilityRadius());
            Assert.Equal(8, dungeonSession.CurrentVisionRadius);
        }

        [Fact]
        public void FogOfWar_PersistenceExportImport_RoundtripsExploredBitsAcrossSessions()
        {
            TileMapData land = BuildOpenTileMap("land_0", 20, 20, isDungeon: false, startSpawn: new GridCoord(3, 3));
            PartyState partyA = BuildSingleHeroParty();
            ExplorationSession sessionA = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["land_0"] = land },
                "land_0",
                partyA,
                initialWorldClockTurns: WorldClock.TurnsPerPhase * 3); // Night unlit radius 4

            Assert.Equal(ExplorationHaltReason.Arrived, sessionA.MoveAlongPath(new GridCoord(15, 3)).HaltReason);

            byte[] exportedStates = sessionA.FogOfWar.ExportVisibilityStates();
            Assert.Equal(400, exportedStates.Length);
            Assert.Equal((byte)FogVisibilityState.Explored, exportedStates[3 * 20 + 3]);
            Assert.Equal((byte)FogVisibilityState.CurrentlyVisible, exportedStates[3 * 20 + 15]);
            Assert.Equal((byte)FogVisibilityState.Unseen, exportedStates[19 * 20 + 19]);

            string base64 = sessionA.FogOfWar.ExportExploredBitsetBase64();
            Assert.False(string.IsNullOrWhiteSpace(base64));

            // Import into a brand new ExplorationSession positioned at (18, 18) with Night radius 4.
            PartyState partyB = BuildSingleHeroParty();
            ExplorationSession sessionB = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["land_0"] = land },
                "land_0",
                partyB,
                startPosition: new GridCoord(18, 18),
                initialWorldClockTurns: WorldClock.TurnsPerPhase * 3);

            Assert.Equal(FogVisibilityState.Unseen, sessionB.FogOfWar.GetState(3, 3));
            sessionB.FogOfWar.ImportExploredBitsetBase64(base64);

            Assert.Equal(FogVisibilityState.Explored, sessionB.FogOfWar.GetState(3, 3));
            Assert.Equal(FogVisibilityState.Explored, sessionB.FogOfWar.GetState(15, 3));
            Assert.Equal(FogVisibilityState.CurrentlyVisible, sessionB.FogOfWar.GetState(18, 18));
            Assert.Equal(FogVisibilityState.Unseen, sessionB.FogOfWar.GetState(0, 19));
        }

        // =====================================================================
        // 2. Weather & Survival Economy
        // =====================================================================

        [Fact]
        public void Weather_ExplorationAndCombatIntegration_UpdatesWeatherSeedsPuddlesAndReducesFireDuration()
        {
            TileMapData land = BuildOpenTileMap("land_0", 12, 12, isDungeon: false, startSpawn: new GridCoord(2, 2));
            PartyState party = BuildSingleHeroParty();
            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["land_0"] = land },
                "land_0",
                party);

            Assert.Equal(WeatherState.Clear, session.Weather);
            session.SetWeather(WeatherState.LightRain);
            Assert.Equal(WeatherState.LightRain, session.Weather);
            session.SetWeather(WeatherState.Storm);
            Assert.Equal(WeatherState.Storm, session.Weather);
            session.SetWeather(WeatherState.Clear);
            Assert.Equal(WeatherState.Clear, session.Weather);

            // Arena generation under LightRain / Storm seeds Water puddles outdoors, while Clear and DungeonChamber do not.
            GridCoord[] reserved = { new GridCoord(1, 5), new GridCoord(8, 5) };
            CombatGrid clearRoad = new CombatGrid(10, 10);
            EncounterDirector.ApplyArenaArchetype(clearRoad, CombatArenaArchetype.RoadAmbush, reserved, new DeterministicDiceRoller(7UL), weather: WeatherState.Clear);
            int clearWaterCount = clearRoad.GetActiveSurfaceCells().Count(c => c.Surface == SurfaceType.Water);

            CombatGrid rainRoad = new CombatGrid(10, 10);
            EncounterDirector.ApplyArenaArchetype(rainRoad, CombatArenaArchetype.RoadAmbush, reserved, new DeterministicDiceRoller(7UL), weather: WeatherState.LightRain);
            int rainWaterCount = rainRoad.GetActiveSurfaceCells().Count(c => c.Surface == SurfaceType.Water);
            Assert.True(rainWaterCount > clearWaterCount);

            CombatGrid clearDungeon = new CombatGrid(10, 10);
            EncounterDirector.ApplyArenaArchetype(clearDungeon, CombatArenaArchetype.DungeonChamber, reserved, new DeterministicDiceRoller(7UL), weather: WeatherState.Clear);
            int clearDungeonWaterCount = clearDungeon.GetActiveSurfaceCells().Count(c => c.Surface == SurfaceType.Water);

            CombatGrid stormDungeon = new CombatGrid(10, 10);
            EncounterDirector.ApplyArenaArchetype(stormDungeon, CombatArenaArchetype.DungeonChamber, reserved, new DeterministicDiceRoller(7UL), weather: WeatherState.Storm);
            int stormDungeonWaterCount = stormDungeon.GetActiveSurfaceCells().Count(c => c.Surface == SurfaceType.Water);
            Assert.Equal(clearDungeonWaterCount, stormDungeonWaterCount);

            // Combat fire-creating spell has duration reduced by 1 round under outdoor rain/storm (3 -> 2), vs 3 in clear weather.
            SpellDefinition firePatch = new SpellDefinition(
                id: 901,
                key: "flame_patch",
                name: "Flame Patch",
                spCost: 4,
                range: 6,
                targetMode: "Single",
                effectKind: "Damage",
                effectDice: "1d4",
                damageType: "Heat",
                saveKind: "None",
                createdSurface: "Fire",
                createdSurfaceRounds: 3);

            CombatantState clearMage = new CombatantState("h1", "Theron", CombatSide.Hero, new GridCoord(1, 4), 25, 25, sp: 20, maxSp: 20, dexterity: 20, spellIds: new[] { 901 });
            CombatantState clearOrc = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(5, 4), 30, 30, dexterity: 10);
            TacticalCombatSession clearCombat = new TacticalCombatSession(
                new CombatGrid(8, 8),
                new[] { clearMage, clearOrc },
                spells: new Dictionary<int, SpellDefinition> { [901] = firePatch },
                roller: new QueuedDiceRoller(2),
                weather: WeatherState.Clear);
            Assert.True(clearCombat.CastSpell("flame_patch", new GridCoord(4, 4)).Success);
            Assert.Equal(SurfaceType.Fire, clearCombat.Grid.GetSurface(4, 4));
            Assert.Equal(3, clearCombat.Grid.GetSurfaceRoundsRemaining(4, 4));

            CombatantState stormMage = new CombatantState("h1", "Theron", CombatSide.Hero, new GridCoord(1, 4), 25, 25, sp: 20, maxSp: 20, dexterity: 20, spellIds: new[] { 901 });
            CombatantState stormOrc = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(5, 4), 30, 30, dexterity: 10);
            TacticalCombatSession stormCombat = new TacticalCombatSession(
                new CombatGrid(8, 8),
                new[] { stormMage, stormOrc },
                spells: new Dictionary<int, SpellDefinition> { [901] = firePatch },
                roller: new QueuedDiceRoller(2),
                weather: WeatherState.Storm);
            Assert.True(stormCombat.CastSpell("flame_patch", new GridCoord(4, 4)).Success);
            Assert.Equal(SurfaceType.Fire, stormCombat.Grid.GetSurface(4, 4));
            Assert.Equal(2, stormCombat.Grid.GetSurfaceRoundsRemaining(4, 4));
        }

        [Fact]
        public void SurvivalEconomy_TorchRationPrices_BuyFoodRations_AndCampConsumption()
        {
            BalanceOverrides defaults = BalanceOverrides.CreateDefault();
            Assert.Equal(12, defaults.Survival.TorchPrice);
            Assert.Equal(5, defaults.Survival.RationPrice);

            int[] landTiles = new int[20 * 20];
            for (int i = 0; i < landTiles.Length; i++)
            {
                landTiles[i] = 61;
            }
            EncounterZoneData wildZone = new EncounterZoneData(1, top: 14, left: 14, bottom: 16, right: 16, percent: 100, battleIds: new[] { 1 });
            TileMapData land = new TileMapData(
                "land_0", "Bywater", 0, isDungeon: false, width: 20, height: 20,
                tiles: landTiles, startSpawn: new GridCoord(15, 15),
                tileRules: new Dictionary<int, TileRule> { [61] = new TileRule(61, passable: true, moveCost: 1) },
                encounterZones: new[] { wildZone });
            PartyState party = BuildSingleHeroParty(gold: 25);
            Assert.Equal(PartyState.DefaultFoodRations, party.FoodRations);
            Assert.Equal(6, party.FoodRations);

            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["land_0"] = land },
                "land_0",
                party,
                roller: new QueuedDiceRoller(99),
                balanceOverrides: defaults);

            // Invalid or unaffordable purchases fail cleanly without mutating Gold or FoodRations.
            Assert.False(session.BuyFoodRations(0));
            Assert.False(session.BuyFoodRations(-2));
            Assert.False(session.BuyFoodRations(6)); // 30g > 25g
            Assert.Equal(25, party.Gold);
            Assert.Equal(6, party.FoodRations);

            // Buy 3 rations at 5g each -> 15g spent, 10g left, 9 rations.
            Assert.True(session.BuyFoodRations(3));
            Assert.Equal(10, party.Gold);
            Assert.Equal(9, party.FoodRations);

            // QuickBandage does not consume a food ration.
            party.Members[0].Hp = 15;
            RestResult quick = party.Rest(CampMode.QuickBandage, isSafeZone: false);
            Assert.True(quick.Success);
            Assert.False(quick.ConsumedRation);
            Assert.Equal(9, party.FoodRations);

            // FullRest in safe zone does not consume a food ration.
            party.Members[0].Hp = 15;
            RestResult safeRest = party.Rest(CampMode.FullRest, isSafeZone: true);
            Assert.True(safeRest.Success);
            Assert.False(safeRest.ConsumedRation);
            Assert.Equal(9, party.FoodRations);

            // FullRest outside safe zone consumes 1 Torch charge and 1 FoodRation.
            party.AddItemToStash(PartyState.TorchItemId, 1);
            party.Members[0].Hp = 10;
            RestResult wildRest = session.TryCampRest(CampMode.FullRest, out _, out _);
            Assert.True(wildRest.Success);
            Assert.True(wildRest.ConsumedRation);
            Assert.Equal(8, wildRest.FoodRationsRemaining);
            Assert.Equal(8, party.FoodRations);
            Assert.Equal(30, party.Members[0].Hp);
        }

        // =====================================================================
        // 3. Interactive & Destructible Combat Props + Locked 6 AP Base
        // =====================================================================

        [Fact]
        public void OptionADndEconomy_RemainsCleanAcrossTurnsDespiteAgilityOrFatigue()
        {
            HeroPresetData slowPreset = new HeroPresetData("h_slow", "Sluggish", archetype: "Knight", hp: 25, maxHp: 25, agility: 4, movementMax: 5);
            HeroPresetData fastPreset = new HeroPresetData("h_fast", "Quick", archetype: "Rogue", hp: 25, maxHp: 25, agility: 24, movementMax: 7);
            PartyMemberState slow = new PartyMemberState(slowPreset);
            slow.ExhaustionStacks = 5;
            PartyMemberState fast = new PartyMemberState(fastPreset);

            CombatantState cSlow = CombatantState.FromHero(slow, new GridCoord(1, 3));
            CombatantState cFast = CombatantState.FromHero(fast, new GridCoord(1, 5));
            CombatantState dummy = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(8, 4), 20, 20, dexterity: 2);

            TacticalCombatSession session = new TacticalCombatSession(new CombatGrid(10, 10), new[] { cSlow, cFast, dummy });

            Assert.Equal("h_fast", session.CurrentActorId);
            Assert.True(cFast.Economy.IsDnd5eMode);
            Assert.Equal(7, cFast.Economy.MoveBudget);
            Assert.True(cFast.Economy.HasSlot(CombatActionSlot.MainAction));
            Assert.True(cFast.Economy.HasSlot(CombatActionSlot.BonusAction));
            session.EndTurn();

            Assert.Equal("h_slow", session.CurrentActorId);
            Assert.Equal(5, cSlow.Economy.MoveBudget);
            Assert.True(cSlow.Economy.HasSlot(CombatActionSlot.MainAction));
            Assert.True(cSlow.Economy.HasSlot(CombatActionSlot.BonusAction));
            session.EndTurn();
            session.EndTurn(); // dummy

            Assert.Equal(2, session.RoundNumber);
            Assert.Equal(7, cFast.Economy.MoveRemaining);
            Assert.True(cFast.Economy.HasSlot(CombatActionSlot.MainAction));
            Assert.True(cFast.Economy.HasSlot(CombatActionSlot.BonusAction));
        }

        [Fact]
        public void OilKegCluster_DetonatesOnPhysicalOrHeat_RespectsSavesAndFireProt_AndChainsKegsAndOilPools()
        {
            CombatGrid grid = new CombatGrid(12, 12);
            // Contiguous Oil pool touching keg1's 3x3 blast at (5, 4) -> (6, 4) -> (7, 4),
            // and keg2 adjacent to the end of the Oil pool at (8, 4).
            grid.SetSurface(5, 4, SurfaceType.Oil, -1);
            grid.SetSurface(6, 4, SurfaceType.Oil, -1);
            grid.SetSurface(7, 4, SurfaceType.Oil, -1);

            CombatantState attacker = new CombatantState("h1", "Kaelen", CombatSide.Hero, new GridCoord(1, 4), 40, 40, weaponDice: "1d4", rangedDice: "1d4", rangedRange: 6, dexterity: 20);
            // Enemy A in keg1 3x3 blast with no FireProtection, fails Dex save (roll 90 > save).
            CombatantState enemyA = new CombatantState("m1", "OrcA", CombatSide.Monster, new GridCoord(3, 5), 30, 30, dexterity: 10);
            // Enemy B in keg1 3x3 blast with FireProtection + passes Dex save (roll 5 <= save).
            CombatantState enemyB = new CombatantState("m2", "OrcB", CombatSide.Monster, new GridCoord(4, 5), 30, 30, dexterity: 14, saves: new[] { 40, 0, 0, 0, 0, 0 });
            enemyB.Conditions.Apply(CharConditionId.FireProtection, magnitude: 1, turnsRemaining: 5, baseRecoveryChancePercent: 0, canRecoverBySave: false);
            // Enemy C standing on connected oil pool at (7, 4) inside keg2 blast.
            CombatantState enemyC = new CombatantState("m3", "OrcC", CombatSide.Monster, new GridCoord(7, 4), 30, 30, dexterity: 10);

            // Rolls:
            // TryAttackProp physical dmg (1d4): 3
            // keg1 DetonateOilKeg:
            //   enemyA (3,5): 2d6 (5, 5 = 10) + Dex save roll 90 (> 5 fail -> 10 dmg)
            //   enemyB (4,5): 2d6 (6, 6 = 12) + Dex save roll 5 (<= 52 pass -> 6), FireProt -> 3 dmg
            // Oil pool burst on enemyC at (7,4): 1d6 (4 -> 4 dmg)
            // keg2 chain DetonateOilKeg:
            //   enemyC (7,4): 2d6 (3, 4 = 7) + Dex save roll 90 (> 5 fail -> 7 dmg)
            QueuedDiceRoller roller = new QueuedDiceRoller(3, 5, 5, 90, 6, 6, 5, 4, 3, 4, 90);
            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { attacker, enemyA, enemyB, enemyC }, roller: roller);

            CombatDestructibleProp keg1 = session.AddDestructibleProp("keg1", new GridCoord(4, 4), CombatArenaObstacleKind.OilKegCluster);
            CombatDestructibleProp keg2 = session.AddDestructibleProp("keg2", new GridCoord(8, 4), CombatArenaObstacleKind.OilKegCluster);
            Assert.Equal(1, keg1.MaxHp);
            Assert.Equal(1, keg1.Hp);

            List<CombatExplosionEvent> explosions = new List<CombatExplosionEvent>();
            session.PropDetonated += explosions.Add;

            CombatActionResult res = session.TryAttackProp("keg1");
            Assert.True(res.Success);
            Assert.True(keg1.IsDestroyed);
            Assert.True(keg2.IsDestroyed);
            Assert.Equal(2, explosions.Count);
            Assert.Equal("keg1", explosions[0].PropId);
            Assert.False(explosions[0].IsChainReaction);
            Assert.Equal("keg2", explosions[1].PropId);
            Assert.True(explosions[1].IsChainReaction);

            // 3x3 Fire around keg1 and chained Oil pool converted to Fire (>= 2 turns).
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(4, 4));
            Assert.True(grid.GetSurfaceRoundsRemaining(4, 4) >= 2);
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(6, 4));
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(7, 4));
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(8, 4));

            // Exact damage verification: enemyA -10, enemyB -3 (Dex save + FireProtection), enemyC -4 (oil) -7 (keg2) = -11.
            Assert.Equal(20, enemyA.Hp);
            Assert.Equal(27, enemyB.Hp);
            Assert.Equal(19, enemyC.Hp);

            // Also verify direct Heat damage detonates an OilKegCluster immediately.
            CombatGrid heatGrid = new CombatGrid(8, 8);
            CombatantState mage = new CombatantState("h1", "Theron", CombatSide.Hero, new GridCoord(1, 4), 20, 20, dexterity: 20);
            CombatantState orc = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(4, 5), 30, 30, dexterity: 10);
            TacticalCombatSession heatSession = new TacticalCombatSession(heatGrid, new[] { mage, orc }, roller: new QueuedDiceRoller(4, 4, 99));
            CombatDestructibleProp heatKeg = heatSession.AddDestructibleProp("heat_keg", new GridCoord(4, 4), CombatArenaObstacleKind.OilKegCluster);
            bool heatEventFired = false;
            heatSession.PropDetonated += _ => heatEventFired = true;

            CombatActionResult heatDet = heatSession.DamageDestructibleProp("heat_keg", 1, "Heat", "h1");
            Assert.True(heatDet.Success);
            Assert.True(heatEventFired);
            Assert.True(heatKeg.IsDestroyed);
            Assert.Equal(22, orc.Hp);
            Assert.Equal(SurfaceType.Fire, heatGrid.GetSurface(4, 4));
        }

        [Fact]
        public void BarrelsCratesAndHayBale_BreakAt4Hp_AndHeatIgnitesHayBaleIntoFire()
        {
            CombatGrid grid = new CombatGrid(8, 8);
            CombatantState hero = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(2, 4), 30, 30, weaponDice: "1d4", damageBonus: 1, dexterity: 20);
            CombatantState orc = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(7, 7), 20, 20, dexterity: 10);

            // Rolls: 1+1 = 2 physical dmg, then 2+1 = 3 physical dmg (>= remaining 2 HP -> destroys BarrelsCrates).
            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { hero, orc }, roller: new QueuedDiceRoller(1, 2));
            CombatDestructibleProp crate = session.AddDestructibleProp("crate1", new GridCoord(3, 4), CombatArenaObstacleKind.BarrelsCrates);
            CombatDestructibleProp hay = session.AddDestructibleProp("hay1", new GridCoord(4, 4), CombatArenaObstacleKind.HayBale);

            Assert.Equal(4, crate.MaxHp);
            Assert.Equal(4, hay.MaxHp);
            Assert.True(grid.ProvidesCover(3, 4));
            Assert.True(grid.ProvidesCover(4, 4));

            CombatActionResult hit1 = session.TryAttackProp("crate1");
            Assert.True(hit1.Success);
            Assert.False(crate.IsDestroyed);
            Assert.Equal(2, crate.Hp);

            // End hero and orc turns to refresh hero's Main Action cleanly in Option A.
            session.EndTurn();
            session.EndTurn();

            CombatActionResult hit2 = session.TryAttackProp("crate1");
            Assert.True(hit2.Success);
            Assert.True(crate.IsDestroyed);
            Assert.Equal(0, crate.Hp);
            Assert.False(grid.ProvidesCover(3, 4));
            Assert.True(grid.IsWalkable(3, 4));

            // Heat damage ignites HayBale immediately into SurfaceType.Fire.
            CombatActionResult igniteRes = session.DamageDestructibleProp("hay1", 1, "Heat", "h1");
            Assert.True(igniteRes.Success);
            Assert.True(hay.IsDestroyed);
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(4, 4));
            Assert.Equal(2, grid.GetSurfaceRoundsRemaining(4, 4));
        }

        [Fact]
        public void LitBrazier_ShoveSlamBurnsEnemy_ShoveKegDetonates_AndColdExtinguishesToSteamCloud()
        {
            // 1. Shove enemy into LitBrazier -> 1d6 Heat + Burning (1 round)
            CombatGrid grid1 = new CombatGrid(8, 8);
            CombatantState hero1 = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(2, 4), 30, 30, brawn: 16, dexterity: 20);
            CombatantState orc1 = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(3, 4), 25, 25, brawn: 10, dexterity: 10);
            // Roll 1 (shove success), 5 (1d6 Heat from LitBrazier)
            TacticalCombatSession s1 = new TacticalCombatSession(grid1, new[] { hero1, orc1 }, roller: new QueuedDiceRoller(1, 5));
            s1.AddDestructibleProp("brazier1", new GridCoord(4, 4), CombatArenaObstacleKind.LitBrazier);

            CombatActionResult shoveRes = s1.TryShove("m1");
            Assert.True(shoveRes.Success);
            Assert.Equal(5, shoveRes.DamageDealt);
            Assert.Equal(20, orc1.Hp);
            Assert.True(orc1.Conditions.Has(CharConditionId.Burning));
            ConditionTracker.ConditionEntry burningEntry = orc1.Conditions.ExportEntries().First(e => e.Id == CharConditionId.Burning);
            Assert.Equal(1, burningEntry.TurnsRemaining);

            // 2. Shove OilKegCluster into LitBrazier -> detonates keg
            CombatGrid grid2 = new CombatGrid(8, 8);
            CombatantState hero2 = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(2, 4), 30, 30, dexterity: 20);
            CombatantState orc2 = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(3, 5), 25, 25, dexterity: 10);
            TacticalCombatSession s2 = new TacticalCombatSession(grid2, new[] { hero2, orc2 }, roller: new QueuedDiceRoller(4, 4, 90, 4, 4, 90));
            CombatDestructibleProp keg = s2.AddDestructibleProp("keg1", new GridCoord(3, 4), CombatArenaObstacleKind.OilKegCluster);
            s2.AddDestructibleProp("brazier2", new GridCoord(4, 4), CombatArenaObstacleKind.LitBrazier);

            CombatExplosionEvent? firedExplosion = null;
            s2.PropDetonated += ev => firedExplosion = ev;

            CombatActionResult shoveKegRes = s2.TryShoveProp("keg1");
            Assert.True(shoveKegRes.Success);
            Assert.True(keg.IsDestroyed);
            Assert.NotNull(firedExplosion);
            Assert.Equal("ShoveIntoBrazier", firedExplosion!.Cause);
            Assert.Equal(22, hero2.Hp);
            Assert.Equal(17, orc2.Hp);

            // 3. Cold damage extinguishes LitBrazier and creates SteamCloud (2 rounds)
            CombatDestructibleProp brazier2 = s2.GetDestructibleProp("brazier2")!;
            Assert.True(brazier2.IsLightSource);
            CombatActionResult coldRes = s2.DamageDestructibleProp("brazier2", 5, "Cold", "h1");
            Assert.True(coldRes.Success);
            Assert.False(brazier2.IsLightSource);
            Assert.Equal(SurfaceType.SteamCloud, grid2.GetSurface(4, 4));
            Assert.Equal(2, grid2.GetSurfaceRoundsRemaining(4, 4));
        }

        [Fact]
        public void LightningChained_ElectricSpellOnWaterFloodsConnectedWaterAndFiresEvent()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            grid.SetSurface(4, 4, SurfaceType.Water, -1);
            grid.SetSurface(5, 4, SurfaceType.Water, -1);
            grid.SetSurface(6, 4, SurfaceType.Water, -1);

            SpellDefinition shockBolt = new SpellDefinition(
                id: 902,
                key: "shock_bolt",
                name: "Shock Bolt",
                spCost: 4,
                range: 6,
                targetMode: "Single",
                effectKind: "Damage",
                effectDice: "1d6",
                damageType: "Electric",
                saveKind: "None");

            CombatantState mage = new CombatantState(
                "h1", "Theron", CombatSide.Hero, new GridCoord(1, 4), 25, 25,
                sp: 20, maxSp: 20, dexterity: 20, spellIds: new[] { 902 });
            CombatantState orc1 = new CombatantState("m1", "Orc1", CombatSide.Monster, new GridCoord(4, 4), 30, 30, dexterity: 10);
            CombatantState orc2 = new CombatantState("m2", "Orc2", CombatSide.Monster, new GridCoord(6, 4), 30, 30, dexterity: 5);

            // Rolls: 4 (shock_bolt on orc1), 3 (water shock on orc1), 2 (water shock on orc2)
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { mage, orc1, orc2 },
                spells: new Dictionary<int, SpellDefinition> { [902] = shockBolt },
                roller: new QueuedDiceRoller(4, 3, 2));

            CombatChainLightningEvent? chainEvent = null;
            session.LightningChained += ev => chainEvent = ev;

            CombatActionResult res = session.CastSpell("shock_bolt", new GridCoord(4, 4));
            Assert.True(res.Success);
            Assert.NotNull(chainEvent);
            Assert.Equal(new GridCoord(4, 4), chainEvent!.Origin);
            Assert.Equal(3, chainEvent.HopCoords.Count);
            Assert.Contains("m1", chainEvent.AffectedUnitIds);
            Assert.Contains("m2", chainEvent.AffectedUnitIds);
            Assert.Equal(5, chainEvent.TotalDamageDealt);

            Assert.Equal(SurfaceType.ElectrifiedWater, grid.GetSurface(4, 4));
            Assert.Equal(SurfaceType.ElectrifiedWater, grid.GetSurface(5, 4));
            Assert.Equal(SurfaceType.ElectrifiedWater, grid.GetSurface(6, 4));
            Assert.Equal(23, orc1.Hp);
            Assert.Equal(28, orc2.Hp);
        }

        [Fact]
        public void TacticalShovePreview_HazardFlagsAndBadges_PopulateAccurately()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            CombatantState shover = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(4, 4), 30, 30, dexterity: 20);
            CombatantState targetKeg = new CombatantState("m1", "Goblin1", CombatSide.Monster, new GridCoord(5, 4), 20, 20);
            CombatantState targetBrazier = new CombatantState("m2", "Goblin2", CombatSide.Monster, new GridCoord(3, 4), 20, 20);
            CombatantState targetCover = new CombatantState("m3", "Goblin3", CombatSide.Monster, new GridCoord(4, 5), 20, 20);
            CombatantState targetSurface = new CombatantState("m4", "Goblin4", CombatSide.Monster, new GridCoord(4, 3), 20, 20);

            grid.SetSurface(4, 2, SurfaceType.Fire, 2);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { shover, targetKeg, targetBrazier, targetCover, targetSurface });

            session.AddDestructibleProp("keg", new GridCoord(6, 4), CombatArenaObstacleKind.OilKegCluster);
            session.AddDestructibleProp("brazier", new GridCoord(2, 4), CombatArenaObstacleKind.LitBrazier);
            session.AddDestructibleProp("crate", new GridCoord(4, 6), CombatArenaObstacleKind.BarrelsCrates);

            TacticalShovePreview kegPreview = session.PreviewShove("h1", "m1");
            Assert.True(kegPreview.CanExecute);
            Assert.True(kegPreview.TriggersKegExplosion);
            Assert.False(kegPreview.IgnitesOnBrazier);
            Assert.Equal("KEG BLAST 2d6!", kegPreview.HazardBadgeText);

            TacticalShovePreview brazierPreview = session.PreviewShove("h1", "m2");
            Assert.True(brazierPreview.CanExecute);
            Assert.True(brazierPreview.IgnitesOnBrazier);
            Assert.False(brazierPreview.TriggersKegExplosion);
            Assert.Equal("BRAZIER +1d6 FIRE", brazierPreview.HazardBadgeText);

            TacticalShovePreview coverPreview = session.PreviewShove("h1", "m3");
            Assert.True(coverPreview.CanExecute);
            Assert.True(coverPreview.DestroysCover);
            Assert.Equal("CRUSH COVER", coverPreview.HazardBadgeText);

            TacticalShovePreview surfacePreview = session.PreviewShove("h1", "m4");
            Assert.True(surfacePreview.CanExecute);
            Assert.True(surfacePreview.EntersSurface);
            Assert.Equal(SurfaceType.Fire, surfacePreview.DestinationSurface);
            Assert.Equal("FIRE +1d4", surfacePreview.HazardBadgeText);
        }

        // =====================================================================
        // 4. Monster HP, Tactical Roles, Equipment Sets & Quest Catalog
        // =====================================================================

        [Fact]
        public void BalanceCatalog_LoadsBywaterM1AndOverrides_WithMonsterHpSetsAndBounties()
        {
            string repoRoot = ResolveRepoRoot();
            string m1Dir = Path.Combine(repoRoot, "extracted", "bywater_m1");
            string overridesPath = Path.Combine(repoRoot, "RealmzUnity", "Assets", "StreamingAssets", "bywater_m1", "balance_overrides.json");

            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(m1Dir);
            BalanceOverrides overrides = BalanceOverrides.LoadFromFile(overridesPath);
            List<MonsterDefinition> monsters = overrides.ApplyToMonsters(bundle.Monsters);

            MonsterDefinition orc = monsters.First(m => m.Id == "orc_warrior");
            Assert.Equal(18, orc.HpMin);
            Assert.Equal(24, orc.HpMax);
            Assert.Equal(21, orc.HpDefault);
            Assert.Equal(65, orc.XpReward);
            Assert.True(orc.HasSpecialAbility("cleave_2tile"));

            MonsterDefinition frog = monsters.First(m => m.Id == "giant_frog");
            Assert.Equal(15, frog.HpMin);
            Assert.Equal(19, frog.HpMax);
            Assert.Equal(17, frog.HpDefault);
            Assert.Equal(70, frog.XpReward);
            Assert.True(frog.HasSpecialAbility("tongue_pull"));

            MonsterDefinition goblin = monsters.First(m => m.Id == "goblin");
            Assert.Equal(8, goblin.HpMin);
            Assert.Equal(11, goblin.HpMax);
            Assert.Equal(9, goblin.HpDefault);

            MonsterDefinition archer = monsters.First(m => m.Id == "goblin_archer");
            Assert.Equal(6, archer.HpMin);
            Assert.Equal(9, archer.HpMax);
            Assert.Equal(8, archer.HpDefault);
            Assert.True(archer.HasSpecialAbility("high_ground_seeker"));
            CombatantState archerState = CombatantState.FromMonster(archer, "archer_1", new GridCoord(1, 1));
            Assert.True(archerState.PrefersRanged);

            // Survival prices & real-time clock rate from balance_overrides.json
            Assert.Equal(12, overrides.Survival.TorchPrice);
            Assert.Equal(5, overrides.Survival.RationPrice);
            Assert.Equal(1.0f, overrides.Survival.MinutesPerRealSecond, 3);

            BalanceOverrides customSpeed = BalanceOverrides.FromJson(
                "{\"version\":1,\"survival\":{\"minutes_per_real_second\":2.5}}");
            Assert.Equal(2.5f, customSpeed.Survival.MinutesPerRealSecond, 3);
            BalanceOverrides roundTrip = BalanceOverrides.FromJson(customSpeed.ToJson());
            Assert.Equal(2.5f, roundTrip.Survival.MinutesPerRealSecond, 3);

            BalanceOverrides clampedHigh = BalanceOverrides.FromJson(
                "{\"version\":1,\"survival\":{\"minutes_per_real_second\":25.0}}");
            Assert.Equal(10.0f, clampedHigh.Survival.MinutesPerRealSecond, 3);

            // Equipment sets: M1 intact + M2 preview sets
            Assert.Equal(4, overrides.EquipmentSets.Count);
            EquipmentSetDefinition scouts = overrides.EquipmentSets.First(s => s.SetId == EquipmentSetCatalog.ScoutsLeatherSetId);
            EquipmentSetDefinition vanguard = overrides.EquipmentSets.First(s => s.SetId == EquipmentSetCatalog.IronVanguardSetId);
            EquipmentSetDefinition thornwood = overrides.EquipmentSets.First(s => s.SetId == EquipmentSetCatalog.ThornwoodStalkerSetId);
            EquipmentSetDefinition cryptbreaker = overrides.EquipmentSets.First(s => s.SetId == EquipmentSetCatalog.CryptbreakerSetId);

            Assert.False(scouts.IsPreview);
            Assert.False(vanguard.IsPreview);
            Assert.True(thornwood.IsPreview);
            Assert.True(cryptbreaker.IsPreview);
            Assert.Equal(new[] { 9101, 9102, 9103 }, thornwood.ItemIds);
            Assert.Equal(new[] { 9201, 9202, 9203 }, cryptbreaker.ItemIds);

            // Notice board bounties: all 4 quests present in both QuestCatalog and BalanceOverrides.Quests
            IReadOnlyList<QuestDefinition> bounties = QuestCatalog.GetNoticeBoardBounties(overrides);
            string[] expectedQuestIds =
            {
                "bounty_river_frogs",
                "bounty_road_goblins",
                "bounty_thornwood_orcs",
                "bounty_cavern_undead"
            };

            Assert.Equal(4, bounties.Count);
            foreach (string qid in expectedQuestIds)
            {
                Assert.Contains(bounties, q => q.QuestId == qid && q.Source == "BywaterNoticeBoard" && q.IsNoticeBoardBounty);
                Assert.NotNull(QuestCatalog.GetById(qid));
            }
        }

        [Fact]
        public void OrcWarriorCleave_AndGiantFrogTonguePull_ExecuteTacticalAbilitiesCleanly()
        {
            // 1. Orc Warrior ExecuteCleaveAttack hits both adjacent heroes (2-tile Cleave).
            MonsterDefinition orcDef = new MonsterDefinition(
                index: 91,
                id: "orc_warrior",
                name: "Orc Warrior",
                hitDice: 3,
                hpBonus: 3,
                hpMin: 18,
                hpMax: 24,
                hpDefault: 21,
                ac: 25,
                toHitBonus: 30,
                dexterity: 20,
                attackDice: "1d6",
                xpReward: 65,
                role: "Bruiser",
                specialAbilities: new[] { "cleave_2tile" });

            CombatantState orc = CombatantState.FromMonster(orcDef, "orc_1", new GridCoord(4, 4));
            CombatantState heroA = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(3, 4), 30, 30, ac: 0, dexterity: 10);
            CombatantState heroB = new CombatantState("h2", "Seraphina", CombatSide.Hero, new GridCoord(3, 5), 30, 30, ac: 0, dexterity: 8);

            // Rolls: primary hit (20), primary dmg (4), secondary hit (20), secondary dmg (5)
            TacticalCombatSession cleaveSession = new TacticalCombatSession(
                new CombatGrid(8, 8),
                new[] { orc, heroA, heroB },
                roller: new QueuedDiceRoller(20, 4, 20, 5));

            Assert.Equal("orc_1", cleaveSession.CurrentActorId);
            CombatActionResult cleaveRes = cleaveSession.ExecuteCleaveAttack("h1");
            Assert.True(cleaveRes.Success);
            Assert.Equal(9, cleaveRes.DamageDealt);
            Assert.Equal(26, heroA.Hp);
            Assert.Equal(25, heroB.Hp);
            Assert.Contains(cleaveSession.Log, e => e.ActionType == "Cleave" && e.TargetIds.Count == 2);

            // 2. Giant Frog ExecuteTonguePull pulls target from range 2..4 onto hazard/lower ground without AoO.
            MonsterDefinition frogDef = new MonsterDefinition(
                index: 0,
                id: "giant_frog",
                name: "Giant Frog",
                hitDice: 3,
                hpMin: 15,
                hpMax: 19,
                hpDefault: 17,
                ac: 15,
                toHitBonus: 15,
                dexterity: 20,
                xpReward: 70,
                role: "Controller",
                specialAbilities: new[] { "tongue_pull" });

            CombatGrid pullGrid = new CombatGrid(10, 10);
            pullGrid.SetElevation(6, 4, 2); // High ground perch at (6, 4)
            pullGrid.SetSurface(4, 4, SurfaceType.Fire, 2); // Fire hazard along pull path

            CombatantState frog = CombatantState.FromMonster(frogDef, "frog_1", new GridCoord(2, 4));
            CombatantState archer = new CombatantState("h1", "Kaelen", CombatSide.Hero, new GridCoord(6, 4), 30, 30, dexterity: 10);
            CombatantState guardian = new CombatantState("h2", "Aldric", CombatSide.Hero, new GridCoord(6, 5), 30, 30, toHit: 50, dexterity: 8);

            // Rolls: 3 (1d4 pull dmg), 4 (1d4 Fire surface dmg at (4, 4))
            TacticalCombatSession pullSession = new TacticalCombatSession(
                pullGrid,
                new[] { frog, archer, guardian },
                roller: new QueuedDiceRoller(3, 4));

            Assert.Equal("frog_1", pullSession.CurrentActorId);
            CombatActionResult pullRes = pullSession.ExecuteTonguePull("h1");
            Assert.True(pullRes.Success);
            Assert.False(pullRes.AoOTriggered);
            Assert.Equal(new GridCoord(4, 4), archer.Position);
            Assert.Equal(7, pullRes.DamageDealt);
            Assert.Equal(23, archer.Hp);
            Assert.True(guardian.Economy.HasReaction);
            Assert.Contains(pullSession.Log, e => e.ActionType == "TonguePull");
        }

        // =====================================================================
        // 5. Save Bitset Format Compat & Resilience
        // =====================================================================

        [Fact]
        public void FogOfWar_BitsetBase64_HandlesNonByteAlignedGridsAndCorruptInputsResiliently()
        {
            // 7x5 = 35 tiles (non-multiple-of-8 bit length -> 5 bytes with 5 padding bits).
            const int width = 7;
            const int height = 5;
            TileMapData map = BuildOpenTileMap("odd_grid", width, height, isDungeon: false, startSpawn: new GridCoord(0, 0));
            FogOfWarMap source = new FogOfWarMap("odd_grid", width, height);

            source.RecomputeVisibility(map, new GridCoord(0, 0), visionRadius: 1);
            source.RecomputeVisibility(map, new GridCoord(6, 4), visionRadius: 1); // Includes last bit index 34

            string packed = source.ExportExploredBitsetBase64();
            Assert.Equal(5, Convert.FromBase64String(packed).Length);

            FogOfWarMap restored = new FogOfWarMap("odd_grid", width, height);
            restored.ImportExploredBitsetBase64(packed);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool expectedExplored = source.IsExplored(x, y);
                    Assert.Equal(expectedExplored, restored.IsExplored(x, y));
                    Assert.Equal(
                        expectedExplored ? FogVisibilityState.Explored : FogVisibilityState.Unseen,
                        restored.GetState(x, y));
                }
            }

            // Corrupt, null, empty, whitespace, and truncated Base64 inputs must never throw.
            FogOfWarMap resilient = new FogOfWarMap("odd_grid", width, height);
            resilient.ImportExploredBitsetBase64(null);
            resilient.ImportExploredBitsetBase64(string.Empty);
            resilient.ImportExploredBitsetBase64("   ");
            resilient.ImportExploredBitsetBase64("not_valid_base64!!!");
            Assert.All(resilient.ExportVisibilityStates(), b => Assert.Equal((byte)FogVisibilityState.Unseen, b));

            // Truncated 1-byte Base64 only sets bits within the first 8 tiles without throwing.
            string oneByteBase64 = Convert.ToBase64String(new byte[] { 0x05 }); // bits 0 and 2
            resilient.ImportExploredBitsetBase64(oneByteBase64);
            Assert.Equal(FogVisibilityState.Explored, resilient.GetState(0, 0));
            Assert.Equal(FogVisibilityState.Unseen, resilient.GetState(1, 0));
            Assert.Equal(FogVisibilityState.Explored, resilient.GetState(2, 0));
            Assert.Equal(FogVisibilityState.Unseen, resilient.GetState(6, 4));
        }
    }
}
