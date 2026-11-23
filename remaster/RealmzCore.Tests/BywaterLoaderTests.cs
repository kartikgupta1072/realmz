using System;
using System.Collections.Generic;
using System.IO;
using RealmzCore.Data;
using RealmzCore.Rules;
using RealmzCore.Serialization;
using Xunit;

namespace RealmzCore.Tests
{
    public class BywaterLoaderTests
    {
        private static string? TryFindBywaterM1Dir()
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

            return null;
        }

        [Fact]
        public void GridCoord_DistancesAndEqualityWork()
        {
            GridCoord a = new GridCoord(2, 3);
            GridCoord b = new GridCoord(5, 7);
            GridCoord aCopy = new GridCoord(2, 3);

            Assert.Equal(4, a.ChebyshevDistance(b));
            Assert.Equal(7, a.ManhattanDistance(b));
            Assert.True(a == aCopy);
            Assert.True(a != b);
            Assert.Equal(a.GetHashCode(), aCopy.GetHashCode());
        }

        [Fact]
        public void MiniJsonWriter_RoundtripsNestedStructures()
        {
            Dictionary<string, object?> payload = new Dictionary<string, object?>
            {
                ["name"] = "Bywater \"North\"\nGate",
                ["count"] = 12,
                ["ratio"] = 2.5,
                ["flag"] = true,
                ["none"] = null,
                ["items"] = new object?[] { 1, "two", false }
            };

            string json = MiniJsonWriter.Serialize(payload, indented: true);
            IReadOnlyDictionary<string, object?> parsed = MiniJson.AsObject(MiniJson.Parse(json));

            Assert.Equal("Bywater \"North\"\nGate", MiniJson.GetString(parsed, "name"));
            Assert.Equal(12, MiniJson.GetInt(parsed, "count"));
            Assert.Equal(2.5, MiniJson.GetDouble(parsed, "ratio"), precision: 5);
            Assert.True(MiniJson.GetBool(parsed, "flag"));
            Assert.True(MiniJson.TryGetValue(parsed, "none", out object? n) && n == null);
            Assert.Equal(3, MiniJson.AsArray(parsed["items"]).Count);
        }

        [Fact]
        public void LoadMap_HandlesLandAndDungeonTileRuleResolution()
        {
            const string landJson = @"{
                ""map_id"": ""land_0"",
                ""scenario"": ""City of Bywater"",
                ""level"": 0,
                ""is_dungeon"": false,
                ""width"": 4,
                ""height"": 4,
                ""start_spawn"": { ""x"": 1, ""y"": 1 },
                ""solid_negative_ids"": [-1092],
                ""tile_rules"": {
                    ""61"": { ""tile_id"": 61, ""passable"": true, ""move_cost"": 1, ""blocks_los"": false, ""is_path"": true },
                    ""156"": { ""tile_id"": 156, ""passable"": true, ""move_cost"": 2, ""blocks_los"": false, ""is_forest"": true },
                    ""90"": { ""tile_id"": 90, ""passable"": false, ""move_cost"": 1, ""blocks_los"": false, ""needs_boat"": true }
                },
                ""portals"": [
                    { ""id"": ""to_dungeon"", ""x"": 3, ""y"": 2, ""dest_map_id"": ""dungeon_0"", ""dest_x"": 1, ""dest_y"": 2, ""dest_facing"": ""East"", ""label"": ""Dungeon Stairs"" }
                ],
                ""points_of_interest"": [
                    { ""id"": ""shop_1"", ""kind"": ""shop"", ""x"": 2, ""y"": 1, ""label"": ""General Store"", ""shop_id"": ""bywater_shop"" }
                ],
                ""encounter_zones"": [
                    { ""slot"": 0, ""top"": 0, ""left"": 2, ""bottom"": 3, ""right"": 3, ""percent"": 50, ""battle_ids"": [10, 11], ""monster_indices"": [0, 1] }
                ],
                ""tiles_yx"": [
                    [61, 61, 156, 90],
                    [61, 61, 61, -1092],
                    [61, 1156, 61, 61],
                    [61, 61, 61, 61]
                ]
            }";

            TileMapData land = BywaterJsonLoader.LoadMap(landJson);
            Assert.Equal("land_0", land.MapId);
            Assert.False(land.IsDungeon);
            Assert.Equal(new GridCoord(1, 1), land.StartSpawn);
            Assert.True(land.IsPassable(0, 0));
            Assert.Equal(2, land.GetMoveCost(2, 0));
            Assert.True(land.GetTileRule(1156).IsForest);
            Assert.False(land.IsPassable(3, 0, hasBoat: false));
            Assert.True(land.IsPassable(3, 0, hasBoat: true));
            Assert.False(land.IsPassable(3, 1));
            Assert.True(land.BlocksLineOfSight(3, 1));

            MapPortal? portal = land.GetPortalAt(3, 2);
            Assert.NotNull(portal);
            Assert.Equal("dungeon_0", portal!.DestMapId);

            MapPointOfInterest? poi = land.GetPointOfInterestAt(2, 1);
            Assert.NotNull(poi);
            Assert.Equal("bywater_shop", poi!.ShopId);

            IReadOnlyList<EncounterZoneData> zones = land.GetZonesAt(2, 2);
            Assert.Single(zones);
            Assert.True(zones[0].Contains(3, 3));
            Assert.False(zones[0].Contains(1, 1));

            // Dungeon raw bitfield tile rule lookup checks direct raw match before terrain decode.
            const string dungeonJson = @"{
                ""map_id"": ""dungeon_0"",
                ""level"": 0,
                ""is_dungeon"": true,
                ""width"": 2,
                ""height"": 2,
                ""tile_rules"": {
                    ""16385"": { ""tile_id"": 16385, ""passable"": false, ""move_cost"": 1, ""blocks_los"": true },
                    ""1"": { ""tile_id"": 1, ""passable"": true, ""move_cost"": 1, ""blocks_los"": false }
                },
                ""tiles"": [16385, 1, 1, 1]
            }";

            TileMapData dungeon = BywaterJsonLoader.LoadMap(dungeonJson);
            Assert.True(dungeon.IsDungeon);
            Assert.False(dungeon.IsPassable(0, 0));
            Assert.True(dungeon.BlocksLineOfSight(0, 0));
            Assert.True(dungeon.IsPassable(1, 0));
        }

        [Fact]
        public void LoadFromDirectory_ParsesCompleteFiveFileFixtureBundle()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "realmz_bywater_fixture_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                File.WriteAllText(Path.Combine(tempDir, "land_0.json"), @"{
                    ""map_id"": ""land_0"", ""scenario"": ""City of Bywater"", ""level"": 0, ""is_dungeon"": false,
                    ""width"": 3, ""height"": 3, ""start_spawn"": { ""x"": 0, ""y"": 0 },
                    ""portals"": [{ ""id"": ""gate"", ""x"": 2, ""y"": 2, ""dest_map_id"": ""dungeon_0"", ""dest_x"": 0, ""dest_y"": 0 }],
                    ""tiles"": [61, 61, 61, 61, 61, 61, 61, 61, 61]
                }");

                File.WriteAllText(Path.Combine(tempDir, "dungeon_0.json"), @"{
                    ""map_id"": ""dungeon_0"", ""scenario"": ""City of Bywater"", ""level"": 0, ""is_dungeon"": true,
                    ""width"": 3, ""height"": 3, ""start_spawn"": { ""x"": 0, ""y"": 0 },
                    ""portals"": [{ ""id"": ""exit"", ""x"": 0, ""y"": 0, ""dest_map_id"": ""land_0"", ""dest_x"": 2, ""dest_y"": 2 }],
                    ""tiles"": [1, 1, 1, 1, 1, 1, 1, 1, 1]
                }");

                File.WriteAllText(Path.Combine(tempDir, "party.json"), @"{
                    ""gold"": 250,
                    ""stash"": [101, 102],
                    ""heroes"": [
                        {
                            ""id"": ""kevlar"", ""name"": ""Kevlar"", ""archetype"": ""Warrior"", ""level"": 3,
                            ""hp"": 30, ""max_hp"": 30, ""sp"": 0, ""max_sp"": 0, ""ac"": 14, ""to_hit"": 28,
                            ""dodge"": 16, ""missile"": 10, ""movement_max"": 8, ""attacks_per_round"": 2.0,
                            ""brawn"": 20, ""knowledge"": 9, ""judgment"": 8, ""agility"": 14, ""vitality"": 17, ""luck"": 13,
                            ""equipped_ids"": [100], ""known_spell_ids"": []
                        }
                    ]
                }");

                File.WriteAllText(Path.Combine(tempDir, "monsters.json"), @"{
                    ""monsters"": [
                        {
                            ""index"": 0, ""id"": ""giant_frog"", ""name"": ""Giant Frog"", ""icon_id"": 392,
                            ""hit_dice"": 3, ""hp_min"": 10, ""hp_max"": 18, ""hp_default"": 14, ""ac"": 15,
                            ""to_hit_bonus"": 12, ""dexterity"": 16, ""movement_max"": 6, ""num_attacks"": 1,
                            ""attack_dice"": ""1d6+1"", ""saves"": [10, 10, 10, 10, 10, 10],
                            ""spell_immunities"": [false, false, false, false, false, false],
                            ""xp_reward"": 45, ""gold_min"": 5, ""gold_max"": 10
                        }
                    ]
                }");

                File.WriteAllText(Path.Combine(tempDir, "spells_and_items.json"), @"{
                    ""spell_areas"": [
                        {
                            ""index"": 0,
                            ""grid_7x7"": [
                                [0,0,0,0,0,0,0],
                                [0,0,0,0,0,0,0],
                                [0,0,1,1,1,0,0],
                                [0,0,1,1,1,0,0],
                                [0,0,1,1,1,0,0],
                                [0,0,0,0,0,0,0],
                                [0,0,0,0,0,0,0]
                            ]
                        }
                    ],
                    ""spells"": [
                        {
                            ""id"": 1, ""key"": ""fireball"", ""name"": ""Fireball"", ""school"": ""Sorcerer"",
                            ""level"": 3, ""sp_cost"": 8, ""range"": 6, ""target_mode"": ""Area"",
                            ""spell_area_index"": 0, ""effect_kind"": ""Damage"", ""effect_dice"": ""3d6"",
                            ""damage_type"": ""Fire"", ""save_kind"": ""Magic""
                        }
                    ],
                    ""items"": [
                        { ""id"": 100, ""name"": ""Iron Broadsword"", ""category"": ""Weapon"", ""slot"": ""Weapon"", ""damage_dice"": ""1d8+1"", ""weapon_bonus"": 1, ""base_price"": 120 },
                        { ""id"": 101, ""name"": ""Healing Potion"", ""category"": ""Potion"", ""slot"": """", ""heal_dice"": ""2d4+2"", ""base_price"": 50 },
                        { ""id"": 102, ""name"": ""Tattered Pelt"", ""category"": ""Junk"", ""slot"": """", ""base_price"": 12, ""is_junk"": true }
                    ],
                    ""shops"": [
                        {
                            ""shop_id"": ""bywater_general"", ""name"": ""Bywater Supply"", ""inflation_pct"": 10,
                            ""stock"": [{ ""item_id"": 100, ""price"": 120, ""quantity"": -1 }, { ""item_id"": 101, ""price"": 50, ""quantity"": 5 }]
                        }
                    ]
                }");

                BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(tempDir);

                Assert.NotNull(bundle.Land0);
                Assert.NotNull(bundle.Dungeon0);
                Assert.Single(bundle.Party);
                Assert.Equal(250, bundle.InitialGold);
                Assert.Equal(2, bundle.InitialStashItemIds.Count);
                Assert.Single(bundle.Monsters);
                Assert.Single(bundle.SpellAreas);
                Assert.Equal(9, bundle.SpellAreas[0].Offsets.Count);
                Assert.True(bundle.SpellsByKey.TryGetValue("fireball", out SpellDefinition? fireball));
                Assert.Equal(9, fireball!.AoeOffsets.Count);
                Assert.Equal(3, bundle.Items.Count);
                Assert.True(bundle.Shops.ContainsKey("bywater_general"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void LoadFromDirectory_LoadsRealExtractedBywaterM1Bundle()
        {
            string? dir = TryFindBywaterM1Dir();
            Assert.NotNull(dir);
            Assert.Equal(5, Directory.GetFiles(dir!, "*.json").Length);

            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(dir!);

            Assert.NotNull(bundle.Land0);
            Assert.NotNull(bundle.Dungeon0);
            Assert.Equal(90, bundle.Land0!.Width);
            Assert.Equal(90, bundle.Land0.Height);
            Assert.Equal(90, bundle.Dungeon0!.Width);
            Assert.Equal(90, bundle.Dungeon0.Height);

            MapPortal? landPortal = bundle.Land0.GetPortalAt(89, 48);
            Assert.NotNull(landPortal);
            Assert.Equal("dungeon_0", landPortal!.DestMapId);
            Assert.Equal(new GridCoord(33, 72), landPortal.DestCoord);

            MapPortal? dungeonPortal = bundle.Dungeon0.GetPortalAt(32, 72);
            Assert.NotNull(dungeonPortal);
            Assert.Equal("land_0", dungeonPortal!.DestMapId);
            Assert.Equal(new GridCoord(88, 48), dungeonPortal.DestCoord);

            Assert.Equal(6, bundle.Land0.PointsOfInterest.Count);
            Assert.Equal(6, bundle.Party.Count);
            Assert.Equal(400, bundle.InitialGold);
            Assert.Equal(12, bundle.Monsters.Count);
            Assert.True(bundle.SpellAreas.Count > 0);
            Assert.True(bundle.Spells.Count > 0);
            Assert.True(bundle.Items.Count > 0);
            Assert.True(bundle.Shops.Count > 0);
        }
    }
}
