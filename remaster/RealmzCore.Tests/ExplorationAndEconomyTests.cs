using System.Collections.Generic;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using Xunit;

namespace RealmzCore.Tests
{
    public class ExplorationAndEconomyTests
    {
        private static Dictionary<int, ItemDefinition> BuildTestCatalog()
        {
            return new Dictionary<int, ItemDefinition>
            {
                [1] = new ItemDefinition(1, "Longsword +1", "Fine blade", "Weapon", "Weapon", "1d8+1", weaponBonus: 1, basePrice: 120),
                [2] = new ItemDefinition(2, "Greatsword", "Heavy two-handed blade", "Weapon", "Weapon", "2d6+2", weaponBonus: 2, basePrice: 240, isTwoHanded: true),
                [3] = new ItemDefinition(3, "Chain Mail", "Sturdy mail", "Armor", "Armor", armorClassBonus: 8, movementMod: -1, basePrice: 150),
                [4] = new ItemDefinition(4, "Oak Shield", "Wooden shield", "Shield", "Shield", armorClassBonus: 3, basePrice: 40),
                [5] = new ItemDefinition(5, "Shortbow", "Hunting bow", "Weapon", "MissileWeapon", "1d6", range: 6, basePrice: 80),
                [6] = new ItemDefinition(6, "Healing Potion", "Restores health", "Potion", "", healDice: "2d6", basePrice: 50),
                [7] = new ItemDefinition(7, "Goblin Ear", "Trophy junk", "Junk", "", basePrice: 10, isJunk: true),
                [8] = new ItemDefinition(8, "Rusty Spoon", "Worthless junk", "Junk", "", basePrice: 4, isJunk: true)
            };
        }

        [Fact]
        public void AStarPathfinder_NavigatesAroundObstaclesAndPrefersLowerTerrainCost()
        {
            // 5x5 grid:
            // Col 2 has a wall at y=0,1,2,3; only y=4 is open.
            // Row 1 on x=1 is forest (moveCost=4) vs plain (moveCost=1).
            int[] tiles = new int[25];
            for (int i = 0; i < 25; i++) tiles[i] = 61;
            tiles[0 * 5 + 2] = -1092;
            tiles[1 * 5 + 2] = -1092;
            tiles[2 * 5 + 2] = -1092;
            tiles[3 * 5 + 2] = -1092;
            tiles[1 * 5 + 1] = 156;

            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule>
            {
                [61] = new TileRule(61, passable: true, moveCost: 1),
                [156] = new TileRule(156, passable: true, moveCost: 4, isForest: true)
            };

            TileMapData map = new TileMapData(
                "test_map",
                "Bywater",
                0,
                isDungeon: false,
                width: 5,
                height: 5,
                tiles: tiles,
                startSpawn: new GridCoord(0, 0),
                tileRules: rules,
                solidNegativeIds: new[] { -1092 });

            PathResult res = AStarPathfinder.FindPath(map, new GridCoord(0, 0), new GridCoord(4, 0));
            Assert.True(res.Found);
            Assert.Equal(new GridCoord(4, 0), res.Steps[res.Steps.Count - 1]);
            // Must pass through the only opening at (2, 4) or (1,4)->(2,4)->(3,4)
            Assert.Contains(new GridCoord(2, 4), res.Steps);
            Assert.DoesNotContain(new GridCoord(2, 1), res.Steps);

            // Budget cap rejects paths that exceed MaxCost.
            PathResult budgetFail = AStarPathfinder.FindPath(map, new GridCoord(0, 0), new GridCoord(4, 0), maxCost: 40);
            Assert.False(budgetFail.Found);
        }

        [Fact]
        public void ExplorationSession_HaltsOnPoiAndEncounterAndTransitionsLandDungeonPortals()
        {
            int width = 92;
            int height = 75;
            int[] landTiles = new int[width * height];
            int[] dungeonTiles = new int[width * height];
            for (int i = 0; i < landTiles.Length; i++)
            {
                landTiles[i] = 61;
                dungeonTiles[i] = 1;
            }

            MapPortal toDungeon = new MapPortal("bywater_dungeon_entry", 89, 48, "dungeon_0", 33, 72, "North", "Crypt Descent");
            MapPortal toLand = new MapPortal("bywater_dungeon_exit", 33, 72, "land_0", 89, 48, "South", "Surface Stairs");
            MapPointOfInterest shopPoi = new MapPointOfInterest("bywater_shop", "shop", 86, 48, "Bywater Outfitter", "general");
            EncounterZoneData zone = new EncounterZoneData(0, top: 70, left: 34, bottom: 74, right: 38, percent: 10000, battleIds: new[] { 42 });

            TileMapData land0 = new TileMapData(
                "land_0", "City of Bywater", 0, false, width, height, landTiles,
                new GridCoord(85, 48),
                portals: new[] { toDungeon },
                pointsOfInterest: new[] { shopPoi });

            TileMapData dungeon0 = new TileMapData(
                "dungeon_0", "City of Bywater", 0, true, width, height, dungeonTiles,
                new GridCoord(33, 72),
                portals: new[] { toLand },
                encounterZones: new[] { zone });

            Dictionary<string, TileMapData> maps = new Dictionary<string, TileMapData>
            {
                ["land_0"] = land0,
                ["dungeon_0"] = dungeon0
            };

            HeroPresetData hero = new HeroPresetData("kevlar", "Kevlar", hp: 20, maxHp: 30, sp: 5, maxSp: 10, ac: 12, toHit: 25);
            PartyState party = new PartyState(new[] { new PartyMemberState(hero) }, gold: 100);
            party.Members[0].ExhaustionStacks = 2;
            party.Members[0].CurseStacks = 1;

            ExplorationSession session = new ExplorationSession(maps, "land_0", party, new DeterministicDiceRoller(7UL));

            // 1. Move east across shop POI at (86, 48) -> halts cleanly on POI.
            ExplorationMoveResult poiStop = session.MoveAlongPath(new GridCoord(89, 48));
            Assert.Equal(ExplorationHaltReason.PointOfInterest, poiStop.HaltReason);
            Assert.Equal(new GridCoord(86, 48), poiStop.FinalPosition);
            Assert.Equal("bywater_shop", poiStop.PointOfInterest!.Id);

            // 2. Continue to portal at (89, 48) -> halts on Portal, then transitions to dungeon_0 (33, 72).
            ExplorationMoveResult portalStop = session.MoveAlongPath(new GridCoord(89, 48));
            Assert.Equal(ExplorationHaltReason.Portal, portalStop.HaltReason);
            Assert.Equal(new GridCoord(89, 48), portalStop.FinalPosition);

            Assert.True(session.UsePortalAtCurrentTile());
            Assert.Equal("dungeon_0", session.CurrentMap.MapId);
            Assert.Equal(new GridCoord(33, 72), session.Position);
            Assert.Equal("North", session.Facing);

            // 3. Step into guaranteed encounter zone at (35, 72) -> halts on EncounterZone.
            ExplorationMoveResult encounterStop = session.MoveAlongPath(new GridCoord(36, 72));
            Assert.Equal(ExplorationHaltReason.EncounterZone, encounterStop.HaltReason);
            Assert.Equal(42, encounterStop.TriggeredBattleId);

            // 4. Temple heal cures HP, ExhaustionStacks, and CurseStacks for 25 gold.
            Assert.True(session.TriggerTempleHeal(25));
            Assert.Equal(75, party.Gold);
            Assert.Equal(30, party.Members[0].Hp);
            Assert.Equal(0, party.Members[0].ExhaustionStacks);
            Assert.Equal(0, party.Members[0].CurseStacks);

            // 5. Return via portal back to land_0 (89, 48).
            ExplorationMoveResult backToExit = session.MoveAlongPath(new GridCoord(33, 72), autoHaltOnEvents: true);
            Assert.Equal(ExplorationHaltReason.Portal, backToExit.HaltReason);
            Assert.True(session.UsePortalAtCurrentTile());
            Assert.Equal("land_0", session.CurrentMap.MapId);
            Assert.Equal(new GridCoord(89, 48), session.Position);
        }

        [Fact]
        public void PartyInventoryShopAndRest_WorkEndToEnd()
        {
            Dictionary<int, ItemDefinition> catalog = BuildTestCatalog();
            HeroPresetData preset = new HeroPresetData(
                "kevlar", "Kevlar",
                hp: 12, maxHp: 30, sp: 4, maxSp: 12,
                ac: 10, toHit: 20, missile: 15, movementMax: 8);

            PartyMemberState hero = new PartyMemberState(preset, catalog);
            hero.ExhaustionStacks = 2;
            hero.CurseStacks = 1;

            PartyState party = new PartyState(new[] { hero }, gold: 300);
            party.AddItemToStash(1, 1); // Longsword +1
            party.AddItemToStash(3, 1); // Chain Mail
            party.AddItemToStash(4, 1); // Oak Shield
            party.AddItemToStash(5, 1); // Shortbow
            party.AddItemToStash(7, 2); // Goblin Ear x2 (junk)
            party.AddItemToStash(8, 1, isIdentified: false); // Unidentified Rusty Spoon (junk)

            // Baseline derived stats reflect exhaustion/curse penalties.
            Assert.Equal(10 - 3, hero.GetEffectiveAc(catalog));
            Assert.Equal(20 - 6, hero.GetEffectiveToHit(catalog));

            // Equip Longsword +1, Chain Mail, Oak Shield, Shortbow.
            Assert.True(party.EquipFromStash(0, 0, catalog)); // Longsword +1 (index 0)
            Assert.True(party.EquipFromStash(0, 0, catalog)); // Chain Mail (now index 0)
            Assert.True(party.EquipFromStash(0, 0, catalog)); // Oak Shield (now index 0)
            Assert.True(party.EquipFromStash(0, 0, catalog)); // Shortbow (now index 0)

            Assert.Equal(10 + 8 + 3 - 3, hero.GetEffectiveAc(catalog)); // 18
            Assert.Equal(20 + 5 - 6, hero.GetEffectiveToHit(catalog)); // 19
            Assert.Equal("1d8+1", hero.GetEffectiveDamageDice(catalog));
            Assert.Equal(1, hero.GetEffectiveWeaponBonus(catalog));
            Assert.Equal(6, hero.GetEffectiveRangedRange(catalog));
            Assert.Equal("1d6", hero.GetEffectiveRangedDice(catalog));
            Assert.Equal(7, hero.GetEffectiveMovementMax(catalog)); // 8 - 1 from Chain Mail

            // Equipping two-handed Greatsword swaps out both Longsword and Shield back to stash.
            party.AddItemToStash(2, 1);
            int greatswordIdx = party.Stash.Count - 1;
            Assert.True(party.EquipFromStash(0, greatswordIdx, catalog));
            Assert.Null(hero.GetEquipped(EquipmentSlot.Shield));
            Assert.Equal(2, hero.GetEffectiveWeaponBonus(catalog));

            // Town shop filtering + buy/sell/junk/identify.
            ShopDefinition shop = new ShopDefinition(
                "general", "Bywater Outfitter", inflationPct: 10,
                stock: new[]
                {
                    new ShopStockItem(6, 0, 5),   // 55g after 10% inflation on BasePrice=50
                    new ShopStockItem(2, 330, 1) // 330g (unaffordable with 300g)
                });

            IReadOnlyList<ShopStockView> potions = TownShopService.FilterStock(
                shop, catalog, party, new ShopFilter { Category = "Potion", AffordableOnly = true, SearchText = "Heal" });
            Assert.Single(potions);
            Assert.Equal(55, potions[0].EffectivePrice);

            Assert.True(TownShopService.BuyItem(party, shop, 6, 2, catalog)); // -110g -> 190g
            Assert.Equal(190, party.Gold);

            IdentifyResult idRes = TownShopService.IdentifyAll(party, costPerItem: 10);
            Assert.Equal(1, idRes.ItemsIdentified);
            Assert.Equal(180, party.Gold);

            JunkSaleResult junkRes = TownShopService.SellCommonJunk(party, catalog);
            Assert.Equal(3, junkRes.ItemsSold); // 2 Goblin Ears (5g each) + 1 Rusty Spoon (2g) = 12g
            Assert.Equal(192, party.Gold);

            // QuickBandage heals living HP using 1 charge (no free torch); FullRest restores full HP/SP, removes ExhaustionStacks, and refills bandages.
            RestResult quick = party.Rest(CampMode.QuickBandage);
            Assert.True(quick.Success);
            Assert.False(quick.TorchLit);
            Assert.Equal(0, party.TorchTurnsRemaining);
            Assert.Equal(1, party.QuickBandageCharges);
            Assert.Equal(1, quick.TurnsElapsed);
            Assert.True(hero.Hp > 12 && hero.Hp < 30);

            RestResult full = party.Rest(CampMode.FullRest);
            Assert.True(full.Success);
            Assert.Equal(30, hero.Hp);
            Assert.Equal(12, hero.Sp);
            Assert.Equal(0, hero.ExhaustionStacks);
            Assert.Equal(2, full.ExhaustionCleared);
            Assert.Equal(PartyState.MaxQuickBandageCharges, party.QuickBandageCharges);
            Assert.Equal(8, full.TurnsElapsed);
        }

        [Fact]
        public void TileMapData_BlockedPropFootprints_BlocksPassabilityPathingAndClearsCleanly()
        {
            int[] tiles = new int[25];
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = 61;
            }

            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule>
            {
                [61] = new TileRule(61, passable: true, moveCost: 1)
            };

            TileMapData map = new TileMapData(
                "prop_test",
                "Bywater",
                0,
                isDungeon: false,
                width: 5,
                height: 5,
                tiles: tiles,
                startSpawn: new GridCoord(0, 0),
                tileRules: rules);

            Assert.Equal(0, map.BlockedPropTileCount);
            Assert.False(map.IsBlockedByProp(2, 0));
            Assert.True(map.IsPassable(2, 0));

            map.SetBlockedPropFootprints(new[]
            {
                new GridCoord(2, 0),
                new GridCoord(2, 1),
                new GridCoord(-1, 0),
                new GridCoord(10, 10)
            });

            Assert.Equal(2, map.BlockedPropTileCount);
            Assert.True(map.IsBlockedByProp(2, 0));
            Assert.True(map.IsBlockedByProp(2, 1));
            Assert.False(map.IsPassable(2, 0));
            Assert.False(map.IsPassable(2, 1));
            Assert.True(map.IsPassable(2, 2));

            PathResult path = AStarPathfinder.FindPath(map, new GridCoord(0, 0), new GridCoord(4, 0));
            Assert.True(path.Found);
            Assert.DoesNotContain(new GridCoord(2, 0), path.Steps);
            Assert.DoesNotContain(new GridCoord(2, 1), path.Steps);

            HeroPresetData hero = new HeroPresetData("kevlar", "Kevlar", hp: 20, maxHp: 20, sp: 5, maxSp: 5, ac: 10, toHit: 10);
            PartyState party = new PartyState(new[] { new PartyMemberState(hero) }, gold: 50);
            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["prop_test"] = map },
                "prop_test",
                party);

            PathResult sessionPlan = session.PlanPath(new GridCoord(4, 0));
            Assert.True(sessionPlan.Found);
            Assert.DoesNotContain(new GridCoord(2, 0), sessionPlan.Steps);

            ExplorationStepResult step1 = session.StepOneTile(new GridCoord(1, 0));
            Assert.True(step1.Moved);

            ExplorationStepResult blockedStep = session.StepOneTile(new GridCoord(2, 0));
            Assert.False(blockedStep.Moved);
            Assert.Equal(ExplorationHaltReason.Blocked, blockedStep.HaltReason);
            Assert.Equal(new GridCoord(1, 0), session.Position);

            map.ClearBlockedPropFootprints();
            Assert.Equal(0, map.BlockedPropTileCount);
            Assert.False(map.IsBlockedByProp(2, 0));
            Assert.True(map.IsPassable(2, 0));

            PathResult unblockedPlan = session.PlanPath(new GridCoord(4, 0));
            Assert.True(unblockedPlan.Found);

            ExplorationStepResult step2 = session.StepOneTile(new GridCoord(2, 0));
            Assert.True(step2.Moved);
            Assert.Equal(new GridCoord(2, 0), session.Position);
        }

        [Fact]
        public void ExplorationSession_TempleCureCurseAndRechargeItems_SpendGoldAndRestoreProperly()
        {
            int[] tiles = new int[9];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = 61;

            TileMapData map = new TileMapData(
                "temple_test",
                "Bywater",
                0,
                isDungeon: false,
                width: 3,
                height: 3,
                tiles: tiles,
                startSpawn: new GridCoord(1, 1),
                tileRules: new Dictionary<int, TileRule> { [61] = new TileRule(61, passable: true, moveCost: 1) });

            Dictionary<int, ItemDefinition> catalog = BuildTestCatalog();
            catalog[9] = new ItemDefinition(9, "Wand of Sparks", "Charged wand", "Weapon", "Weapon", "1d6", basePrice: 200, charges: 10);
            catalog[10] = new ItemDefinition(10, "Amulet of Ward", "Charged amulet", "Misc", "", basePrice: 150, charges: 5);

            HeroPresetData hero = new HeroPresetData("kevlar", "Kevlar", hp: 20, maxHp: 30, sp: 5, maxSp: 10, ac: 10, toHit: 20);
            PartyMemberState member = new PartyMemberState(hero, catalog);
            PartyState party = new PartyState(new[] { member }, gold: 10);

            party.AddItemToStash(9, 1, isIdentified: true, charges: 3);
            Assert.True(party.EquipFromStash(0, 0, catalog));
            party.AddItemToStash(10, 1, isIdentified: true, charges: 1);
            party.AddItemToStash(10, 1, isIdentified: true, charges: 5); // Already full

            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["temple_test"] = map },
                "temple_test",
                party);

            // Nothing to cure yet.
            Assert.False(session.TriggerTempleCureCurse(20));
            Assert.Equal(10, party.Gold);

            member.CurseStacks = 2;
            member.ExhaustionStacks = 1;
            member.Conditions.Apply(CharConditionId.Cursed);
            member.Conditions.Apply(CharConditionId.Poisoned);
            member.Conditions.Apply(CharConditionId.Silenced);
            member.Conditions.Apply(CharConditionId.HinderedAttacks);
            member.Conditions.Apply(CharConditionId.MagicAura);

            // Insufficient gold leaves gold, curses, and items unchanged.
            Assert.False(session.TriggerTempleCureCurse(20));
            Assert.Equal(-1, session.TriggerTempleRechargeItems(catalog, 30));
            Assert.Equal(10, party.Gold);
            Assert.Equal(2, member.CurseStacks);
            Assert.Equal(3, member.GetEquipped(EquipmentSlot.Weapon)!.RemainingCharges);

            party.Gold = 100;

            Assert.True(session.TriggerTempleCureCurse(20));
            Assert.Equal(80, party.Gold);
            Assert.Equal(0, member.CurseStacks);
            Assert.Equal(0, member.ExhaustionStacks);
            Assert.False(member.Conditions.Has(CharConditionId.Cursed));
            Assert.False(member.Conditions.Has(CharConditionId.Poisoned));
            Assert.False(member.Conditions.Has(CharConditionId.Silenced));
            Assert.False(member.Conditions.Has(CharConditionId.HinderedAttacks));
            Assert.True(member.Conditions.Has(CharConditionId.MagicAura));

            int recharged = session.TriggerTempleRechargeItems(catalog, 30);
            Assert.Equal(2, recharged);
            Assert.Equal(50, party.Gold);
            Assert.Equal(10, member.GetEquipped(EquipmentSlot.Weapon)!.RemainingCharges);
            Assert.Equal(5, party.Stash[0].RemainingCharges);
            Assert.Equal(5, party.Stash[1].RemainingCharges);

            // Recharging again with full charges does nothing and spends no gold.
            Assert.Equal(0, session.TriggerTempleRechargeItems(catalog, 30));
            Assert.Equal(50, party.Gold);
        }

        [Fact]
        public void ExplorationSession_SetBlockedPropFootprints_RoutesAroundBuildingFootprintAndHaltsDirectStep()
        {
            const int width = 10;
            const int height = 10;
            int[] tiles = new int[width * height];
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = 61;
            }

            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule>
            {
                [61] = new TileRule(61, passable: true, moveCost: 1)
            };

            TileMapData mainMap = new TileMapData(
                "town_main",
                "Bywater",
                0,
                isDungeon: false,
                width: width,
                height: height,
                tiles: tiles,
                startSpawn: new GridCoord(1, 4),
                tileRules: rules);

            TileMapData cellarMap = new TileMapData(
                "town_cellar",
                "Bywater Cellar",
                0,
                isDungeon: true,
                width: width,
                height: height,
                tiles: (int[])tiles.Clone(),
                startSpawn: new GridCoord(0, 0),
                tileRules: rules);

            HeroPresetData hero = new HeroPresetData("kevlar", "Kevlar", hp: 20, maxHp: 20, sp: 5, maxSp: 5, ac: 10, toHit: 10);
            PartyState party = new PartyState(new[] { new PartyMemberState(hero) }, gold: 50);
            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData>
                {
                    ["town_main"] = mainMap,
                    ["town_cellar"] = cellarMap
                },
                "town_main",
                party);

            // Realistic 4x3 building wall/interior footprint covering x=3..6, y=3..5.
            List<GridCoord> buildingFootprint = new List<GridCoord>();
            for (int y = 3; y <= 5; y++)
            {
                for (int x = 3; x <= 6; x++)
                {
                    buildingFootprint.Add(new GridCoord(x, y));
                }
            }

            session.SetBlockedPropFootprints(buildingFootprint);
            Assert.Equal(12, mainMap.BlockedPropTileCount);

            // PlanPath to the doorway/destination on the opposite side must route around the exterior perimeter.
            GridCoord doorwayDest = new GridCoord(7, 4);
            PathResult path = session.PlanPath(doorwayDest);
            Assert.True(path.Found);
            Assert.Equal(doorwayDest, path.Steps[path.Steps.Count - 1]);
            for (int i = 0; i < buildingFootprint.Count; i++)
            {
                Assert.DoesNotContain(buildingFootprint[i], path.Steps);
            }

            // Step up to the west exterior wall at (2, 4), then attempt to step into building tile (3, 4).
            ExplorationStepResult approachStep = session.StepOneTile(new GridCoord(2, 4));
            Assert.True(approachStep.Moved);
            Assert.Equal(new GridCoord(2, 4), session.Position);

            ExplorationStepResult blockedStep = session.StepOneTile(new GridCoord(3, 4));
            Assert.False(blockedStep.Moved);
            Assert.Equal(ExplorationHaltReason.Blocked, blockedStep.HaltReason);
            Assert.Equal(new GridCoord(2, 4), session.Position);

            // Map-targeted overload updates a non-current map and validates mapId.
            session.SetBlockedPropFootprints("town_cellar", new[] { new GridCoord(4, 4) });
            Assert.Equal(1, cellarMap.BlockedPropTileCount);
            Assert.True(cellarMap.IsBlockedByProp(4, 4));
            Assert.Throws<System.ArgumentException>(() => session.SetBlockedPropFootprints(" ", buildingFootprint));
            Assert.Throws<KeyNotFoundException>(() => session.SetBlockedPropFootprints("missing_map", buildingFootprint));

            // ClearBlockedPropFootprints clears current map footprints without affecting other maps.
            session.ClearBlockedPropFootprints();
            Assert.Equal(0, mainMap.BlockedPropTileCount);
            Assert.Equal(1, cellarMap.BlockedPropTileCount);
        }

        [Fact]
        public void ExplorationSession_SetBlockedPropFootprints_BlocksNpcTileWhileKeepingAdjacentTilesReachable()
        {
            const int width = 7;
            const int height = 7;
            int[] tiles = new int[width * height];
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = 61;
            }

            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule>
            {
                [61] = new TileRule(61, passable: true, moveCost: 1)
            };

            TileMapData map = new TileMapData(
                "npc_test",
                "Bywater",
                0,
                isDungeon: false,
                width: width,
                height: height,
                tiles: tiles,
                startSpawn: new GridCoord(1, 3),
                tileRules: rules);

            HeroPresetData hero = new HeroPresetData("kevlar", "Kevlar", hp: 20, maxHp: 20, sp: 5, maxSp: 5, ac: 10, toHit: 10);
            PartyState party = new PartyState(new[] { new PartyMemberState(hero) }, gold: 50);
            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["npc_test"] = map },
                "npc_test",
                party);

            GridCoord npcTile = new GridCoord(3, 3);
            session.SetBlockedPropFootprints(new[] { npcTile });

            // PlanPath directly onto the occupied NPC tile must fail.
            PathResult directPath = session.PlanPath(npcTile);
            Assert.False(directPath.Found);

            // Step onto adjacent west approach tile (2, 3), then verify StepOneTile onto NPC tile halts as Blocked.
            ExplorationStepResult westApproach = session.StepOneTile(new GridCoord(2, 3));
            Assert.True(westApproach.Moved);
            Assert.Equal(new GridCoord(2, 3), session.Position);

            ExplorationStepResult stepOntoNpc = session.StepOneTile(npcTile);
            Assert.False(stepOntoNpc.Moved);
            Assert.Equal(ExplorationHaltReason.Blocked, stepOntoNpc.HaltReason);
            Assert.Equal(new GridCoord(2, 3), session.Position);

            // Adjacent approach tiles around the NPC remain reachable and PlanPath routes cleanly around the NPC.
            GridCoord[] adjacentTiles =
            {
                new GridCoord(3, 2),
                new GridCoord(4, 3),
                new GridCoord(3, 4)
            };

            for (int i = 0; i < adjacentTiles.Length; i++)
            {
                PathResult approachPath = session.PlanPath(adjacentTiles[i]);
                Assert.True(approachPath.Found);
                Assert.Equal(adjacentTiles[i], approachPath.Steps[approachPath.Steps.Count - 1]);
                Assert.DoesNotContain(npcTile, approachPath.Steps);
            }
        }
    }
}
