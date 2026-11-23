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
            EncounterZoneData zone = new EncounterZoneData(0, top: 70, left: 34, bottom: 74, right: 38, percent: 100, battleIds: new[] { 42 });

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

            // QuickBandage heals partial HP and lights torch; FullRest restores full HP/SP and removes ExhaustionStacks.
            RestResult quick = party.Rest(CampMode.QuickBandage);
            Assert.True(quick.TorchLit);
            Assert.Equal(30, party.TorchTurnsRemaining);
            Assert.True(hero.Hp > 12 && hero.Hp < 30);

            RestResult full = party.Rest(CampMode.FullRest);
            Assert.Equal(30, hero.Hp);
            Assert.Equal(12, hero.Sp);
            Assert.Equal(0, hero.ExhaustionStacks);
            Assert.Equal(2, full.ExhaustionCleared);
        }
    }
}
