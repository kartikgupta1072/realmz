using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RealmzCore.Data;
using RealmzCore.Serialization;
using Xunit;

namespace RealmzCore.Tests
{
    public class ScenarioLoaderTests
    {
        private static string FindRealmzDataRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "base", "Realmz");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            if (Directory.Exists(@"C:\Users\karti\realmz\base\Realmz"))
            {
                return @"C:\Users\karti\realmz\base\Realmz";
            }

            throw new DirectoryNotFoundException("Could not locate base/Realmz directory from AppContext.BaseDirectory.");
        }

        [Fact]
        public void LoadCharacterFromBytes_KevlarMatchesExpectedStats()
        {
            string kevlarPath = Path.Combine(FindRealmzDataRoot(), "Character Files", "Kevlar");
            byte[] bytes = File.ReadAllBytes(kevlarPath);

            HeroCharacterData kevlar = RealmzBinaryLoader.LoadCharacterFromBytes(bytes);

            Assert.Equal("Kevlar", kevlar.Name);
            Assert.Equal(3, kevlar.Level);
            Assert.Equal(275, kevlar.PortraitId);
            Assert.Equal(9018, kevlar.IconId);
            Assert.Equal(30, kevlar.Stamina);
            Assert.Equal(30, kevlar.StaminaMax);
            Assert.Equal(23, kevlar.Strength);
            Assert.Equal(17, kevlar.Constitution);
            Assert.Equal(2.0, kevlar.AttacksPerRound, precision: 3);
        }

        [Fact]
        public void LoadLandMapFromDataLd_BywaterLand0MatchesExpectedMetadataAndTiles()
        {
            string bywaterDir = Path.Combine(FindRealmzDataRoot(), "Scenarios", "City of Bywater");
            byte[] dataLd = File.ReadAllBytes(Path.Combine(bywaterDir, "Data LD"));
            byte[] dataRd = File.ReadAllBytes(Path.Combine(bywaterDir, "Data RD"));

            LandMapData land0 = RealmzBinaryLoader.LoadLandMapFromDataLd(dataLd, 0, dataRd);

            Assert.Equal(90, land0.Width);
            Assert.Equal(90, land0.Height);
            Assert.Equal(8100, land0.Tiles.Count);
            Assert.Equal(0, land0.LandLookId);
            Assert.False(land0.IsDark);
            Assert.False(land0.UseLineOfSight);
            Assert.Contains(land0.Tiles, t => t != 0);
            Assert.Equal(61, land0.GetTile(0, 0));
            Assert.Equal(60, LandMapData.DecodeTerrainTileIndex(land0.GetTile(0, 0)));
        }

        [Fact]
        public void DecodeTerrainTileIndex_HandlesMarkersAndOffsets()
        {
            Assert.Equal(-1, LandMapData.DecodeTerrainTileIndex(-1092));
            Assert.Equal(60, LandMapData.DecodeTerrainTileIndex(61));
            Assert.Equal(155, LandMapData.DecodeTerrainTileIndex(1156));
            Assert.Equal(39, LandMapData.DecodeTerrainTileIndex(2040));
            Assert.Equal(187, LandMapData.DecodeTerrainTileIndex(3188));
            // Note (0x4000) and path (0x2000) bit markers cleared before index decode.
            Assert.Equal(60, LandMapData.DecodeTerrainTileIndex(61 | 0x4000 | 0x2000));
            // Values > 200 after offset stripping are CIcon IDs, not terrain sheet tiles.
            Assert.Equal(-1, LandMapData.DecodeTerrainTileIndex(205));
        }

        [Fact]
        public void LoadMonstersFromDataMd_BywaterLoads155MonstersWithGiantFrogFirst()
        {
            string dataMdPath = Path.Combine(FindRealmzDataRoot(), "Scenarios", "City of Bywater", "Data MD");
            byte[] dataMd = File.ReadAllBytes(dataMdPath);

            IReadOnlyList<MonsterTemplateData> monsters = RealmzBinaryLoader.LoadMonstersFromDataMd(dataMd);

            Assert.Equal(155, monsters.Count);
            Assert.Equal(0, monsters[0].Index);
            Assert.Equal("Giant Frog", monsters[0].Name);
            Assert.Equal(392, monsters[0].IconId);
            Assert.Equal(3, monsters[0].HitDice);
            Assert.Equal(15, monsters[0].ArmorClass);
        }

        [Fact]
        public void MiniJson_ParsesNestedTypesEscapesAndNumbers()
        {
            const string json = @"{
                ""name"": ""City of Bywater\nMap 0"",
                ""unicode"": ""\u0041\u0042"",
                ""count"": 42,
                ""negative"": -7,
                ""ratio"": 1.5,
                ""flag"": true,
                ""empty"": null,
                ""items"": [1, ""two"", false, { ""nested"": 9 }]
            }";

            IReadOnlyDictionary<string, object?> root = MiniJson.AsObject(MiniJson.Parse(json));
            Assert.Equal("City of Bywater\nMap 0", MiniJson.GetString(root, "name"));
            Assert.Equal("AB", MiniJson.GetString(root, "unicode"));
            Assert.Equal(42, MiniJson.GetInt(root, "count"));
            Assert.Equal(-7, MiniJson.GetInt(root, "negative"));
            Assert.Equal(1.5, MiniJson.GetDouble(root, "ratio"), precision: 5);
            Assert.True(MiniJson.GetBool(root, "flag"));
            Assert.True(MiniJson.TryGetValue(root, "empty", out object? emptyVal) && emptyVal == null);

            IReadOnlyList<object?> items = MiniJson.AsArray(root["items"]);
            Assert.Equal(4, items.Count);
            Assert.Equal(9, MiniJson.GetInt(MiniJson.AsObject(items[3]), "nested"));
        }

        [Fact]
        public void LoadLandMapFromJson_RoundtripsLand0Slice()
        {
            string bywaterDir = Path.Combine(FindRealmzDataRoot(), "Scenarios", "City of Bywater");
            byte[] dataLd = File.ReadAllBytes(Path.Combine(bywaterDir, "Data LD"));
            byte[] dataRd = File.ReadAllBytes(Path.Combine(bywaterDir, "Data RD"));
            LandMapData binaryLand0 = RealmzBinaryLoader.LoadLandMapFromDataLd(dataLd, 0, dataRd, startX: 12, startY: 34);

            string tileList = string.Join(",", binaryLand0.Tiles);
            string json = $@"{{
                ""width"": {binaryLand0.Width},
                ""height"": {binaryLand0.Height},
                ""landLookId"": {binaryLand0.LandLookId},
                ""isDark"": false,
                ""useLineOfSight"": false,
                ""startX"": {binaryLand0.StartX},
                ""startY"": {binaryLand0.StartY},
                ""tiles"": [{tileList}]
            }}";

            LandMapData jsonLand0 = RealmzBinaryLoader.LoadLandMapFromJson(json);

            Assert.Equal(binaryLand0.Width, jsonLand0.Width);
            Assert.Equal(binaryLand0.Height, jsonLand0.Height);
            Assert.Equal(binaryLand0.LandLookId, jsonLand0.LandLookId);
            Assert.Equal(binaryLand0.IsDark, jsonLand0.IsDark);
            Assert.Equal(binaryLand0.UseLineOfSight, jsonLand0.UseLineOfSight);
            Assert.Equal(12, jsonLand0.StartX);
            Assert.Equal(34, jsonLand0.StartY);
            Assert.Equal(binaryLand0.GetTile(0, 0), jsonLand0.GetTile(0, 0));
            Assert.Equal(binaryLand0.GetTile(45, 45), jsonLand0.GetTile(45, 45));
            Assert.Equal(binaryLand0.GetTile(89, 89), jsonLand0.GetTile(89, 89));
        }

        [Fact]
        public void LoadLandMapFromJson_RealExtractedBywaterLand0MatchesBinary()
        {
            string dataRoot = FindRealmzDataRoot();
            string repoRoot = Directory.GetParent(Directory.GetParent(dataRoot)!.FullName)!.FullName;
            string extractedJsonPath = Path.Combine(repoRoot, "remaster", "extracted", "bywater_land_0.json");
            Assert.True(File.Exists(extractedJsonPath), $"Missing extracted JSON at {extractedJsonPath}");

            LandMapData jsonLand0 = RealmzBinaryLoader.LoadLandMapFromJson(File.ReadAllText(extractedJsonPath));
            byte[] dataLd = File.ReadAllBytes(Path.Combine(dataRoot, "Scenarios", "City of Bywater", "Data LD"));
            byte[] dataRd = File.ReadAllBytes(Path.Combine(dataRoot, "Scenarios", "City of Bywater", "Data RD"));
            LandMapData binaryLand0 = RealmzBinaryLoader.LoadLandMapFromDataLd(dataLd, 0, dataRd);

            Assert.Equal(90, jsonLand0.Width);
            Assert.Equal(90, jsonLand0.Height);
            Assert.Equal(0, jsonLand0.LandLookId);
            Assert.Equal(5, jsonLand0.StartX);
            Assert.Equal(5, jsonLand0.StartY);
            Assert.Equal(binaryLand0.GetTile(0, 0), jsonLand0.GetTile(0, 0));
            Assert.Equal(binaryLand0.GetTile(12, 34), jsonLand0.GetTile(12, 34));
            Assert.Equal(binaryLand0.GetTile(89, 89), jsonLand0.GetTile(89, 89));
        }
    }
}
