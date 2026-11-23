using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Realmz.Data;
using Realmz.EditorTools;
using Realmz.Foundry;
using Realmz.Rendering;
using Realmz.World;
using RealmzCore.Data;
using UnityEditor;
using UnityEngine;

namespace Realmz.EditorTests
{
    public sealed class RealmzFoundryEditorTests
    {
        private const string TestAssetId = "__foundry_unit_test";
        private const string TestTerrainFolder = "terrain___foundry_terrain_test";
        private const string TestTerrainId = "__foundry_terrain_test";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        [TearDown]
        public void CleanupSyntheticTestAssets()
        {
            DeleteIfExists(RealmzAssetInboxPostprocessor.PrefabsFoundryRoot + "/" + TestAssetId + "_HD2D.prefab");
            DeleteIfExists(RealmzAssetInboxPostprocessor.PrefabsFoundryRoot + "/" + TestAssetId + "_3D.prefab");
            DeleteIfExists(RealmzAssetInboxPostprocessor.PrefabsFoundryRoot + "/" + TestAssetId + "_Controller.controller");
            DeleteIfExists(RealmzAssetInboxPostprocessor.MaterialsFoundryRoot + "/" + TestAssetId + "_HD2D.mat");
            DeleteIfExists(RealmzAssetInboxPostprocessor.MaterialsFoundryRoot + "/" + TestAssetId + "_Stylized3D.mat");
            DeleteIfExists(RealmzAssetInboxPostprocessor.InboxRoot + "/" + TestAssetId);
            DeleteIfExists(RealmzAssetInboxPostprocessor.MaterialsFoundryRoot + "/" + TestTerrainId + ".mat");
            DeleteIfExists(RealmzAssetInboxPostprocessor.InboxRoot + "/" + TestTerrainFolder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void FoundryManifest_JsonRoundtrip_PreservesSchemaFields()
        {
            FoundryAssetManifestData original = new FoundryAssetManifestData
            {
                schema_version = "1.0",
                id = "salmon_river_unit",
                name = "River Salmon",
                category = "Monster",
                rig_profile = "Aquatic",
                rigged = true,
                rig_source = "blender_auto",
                bones = new[] { "root", "spine_01", "tail_01" },
                animations = new[] { "Idle", "Move", "Attack" },
                bounds_m = new FoundryBoundsData { width = 0.45f, height = 0.62f, depth = 1.35f },
                pivot = "bottom_center",
                forward_axis = "+Z",
                triangle_count = 4120,
                target_scale_m = 1.35f,
                preferred_mode = "Both",
                hd2d_frame_size = 128,
                hd2d_frame_count = 4,
                hd2d_ppu = 128,
                source_image = "salmon.png",
                peak_vram_mib = 5420.5f,
                timestamp = "2026-11-23T17:00:00Z"
            };

            string json = JsonUtility.ToJson(original);
            FoundryAssetManifestData roundtrip = JsonUtility.FromJson<FoundryAssetManifestData>(json);

            Assert.That(roundtrip, Is.Not.Null);
            Assert.That(roundtrip.id, Is.EqualTo("salmon_river_unit"));
            Assert.That(roundtrip.category, Is.EqualTo("Monster"));
            Assert.That(roundtrip.rig_profile, Is.EqualTo("Aquatic"));
            Assert.That(roundtrip.rigged, Is.True);
            Assert.That(roundtrip.bones.Length, Is.EqualTo(3));
            Assert.That(roundtrip.animations.Length, Is.EqualTo(3));
            Assert.That(roundtrip.bounds_m.ToVector3().z, Is.EqualTo(1.35f).Within(0.001f));
            Assert.That(roundtrip.triangle_count, Is.EqualTo(4120));
            Assert.That(roundtrip.hd2d_frame_count, Is.EqualTo(4));
            Assert.That(roundtrip.hd2d_ppu, Is.EqualTo(128));
        }

        [Test]
        public void Stylized3DShader_ExistsAndCompilesCleanly()
        {
            Shader shader = Shader.Find(RealmzAssetInboxPostprocessor.Stylized3DShaderName);
            Assert.That(shader, Is.Not.Null, "Shader 'Realmz/Stylized3D' not found.");
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, "Shader 'Realmz/Stylized3D' has compile errors.");

            string[] expectedProps =
            {
                "_BaseMap",
                "_BaseColor",
                "_BumpMap",
                "_BumpScale",
                "_Smoothness",
                "_RampSteps",
                "_ShadowWarmth",
                "_RimColor",
                "_RimPower",
                "_RimStrength",
                "_EmissionColor",
                "_Cutoff"
            };

            for (int i = 0; i < expectedProps.Length; i++)
            {
                int idx = shader.FindPropertyIndex(expectedProps[i]);
                Assert.That(idx, Is.GreaterThanOrEqualTo(0), "Missing shader property: " + expectedProps[i]);
            }
        }

        [Test]
        public void InboxPostprocessor_ProcessesSyntheticHd2dAsset_AndGeneratesReceipt()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absFolder = Path.Combine(projectRoot, "Assets", "Inbox", TestAssetId);
            Directory.CreateDirectory(absFolder);

            WriteSolidPng(Path.Combine(absFolder, TestAssetId + "_sheet.png"), 16, 16, new Color32(220, 120, 60, 255));
            WriteSolidPng(Path.Combine(absFolder, TestAssetId + "_sheet_n.png"), 16, 16, new Color32(128, 128, 255, 255));

            FoundryAssetManifestData manifest = new FoundryAssetManifestData
            {
                schema_version = "1.0",
                id = TestAssetId,
                name = "Synthetic Foundry Unit",
                category = "Monster",
                rig_profile = "Aquatic",
                rigged = false,
                rig_source = "none",
                bones = new string[0],
                animations = new[] { "Idle" },
                bounds_m = new FoundryBoundsData { width = 0.9f, height = 1.2f, depth = 0.9f },
                pivot = "bottom_center",
                forward_axis = "+Z",
                triangle_count = 2,
                target_scale_m = 1.2f,
                preferred_mode = "HD2D",
                hd2d_frame_size = 16,
                hd2d_frame_count = 1,
                hd2d_ppu = 128,
                source_image = "synthetic.png",
                peak_vram_mib = 0f,
                timestamp = "2026-11-23T17:00:00Z"
            };

            File.WriteAllText(
                Path.Combine(absFolder, TestAssetId + ".asset.json"),
                JsonUtility.ToJson(manifest, true),
                Utf8NoBom);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            FoundryImportReceipt receipt = RealmzAssetInboxPostprocessor.ProcessAsset(TestAssetId);
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt.success, Is.True, receipt.notes);

            string sheetRel = RealmzAssetInboxPostprocessor.InboxRoot + "/" + TestAssetId + "/" + TestAssetId + "_sheet.png";
            TextureImporter sheetImp = AssetImporter.GetAtPath(sheetRel) as TextureImporter;
            Assert.That(sheetImp, Is.Not.Null);
            Assert.That(sheetImp.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(sheetImp.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(sheetImp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(sheetImp.mipmapEnabled, Is.False);
            Assert.That(sheetImp.spritePixelsPerUnit, Is.EqualTo(128f));

            string normalRel = RealmzAssetInboxPostprocessor.InboxRoot + "/" + TestAssetId + "/" + TestAssetId + "_sheet_n.png";
            TextureImporter normImp = AssetImporter.GetAtPath(normalRel) as TextureImporter;
            Assert.That(normImp, Is.Not.Null);
            Assert.That(normImp.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(normImp.sRGBTexture, Is.False);
            Assert.That(normImp.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(normImp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(normImp.mipmapEnabled, Is.False);

            string matRel = RealmzAssetInboxPostprocessor.MaterialsFoundryRoot + "/" + TestAssetId + "_HD2D.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matRel);
            Assert.That(mat, Is.Not.Null, "Expected HD-2D material at " + matRel);

            string prefabRel = RealmzAssetInboxPostprocessor.PrefabsFoundryRoot + "/" + TestAssetId + "_HD2D.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabRel);
            Assert.That(prefab, Is.Not.Null, "Expected HD-2D prefab at " + prefabRel);
            Assert.That(prefab.GetComponent<FoundryAssetMetadata>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BywaterFoundryUnit>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<PixelBillboard>(true), Is.Not.Null);

            string receiptAbs = Path.Combine(absFolder, TestAssetId + ".import_receipt.json");
            Assert.That(File.Exists(receiptAbs), Is.True, "Expected import receipt JSON.");
            FoundryImportReceipt diskReceipt = JsonUtility.FromJson<FoundryImportReceipt>(File.ReadAllText(receiptAbs, Encoding.UTF8));
            Assert.That(diskReceipt, Is.Not.Null);
            Assert.That(diskReceipt.success, Is.True);
            Assert.That(diskReceipt.prefab_hd2d_path, Is.EqualTo(prefabRel));
        }

        [Test]
        public void TerrainShaders_ExistAndCompileCleanly()
        {
            Shader blendShader = Shader.Find(RealmzAssetInboxPostprocessor.TerrainBlendShaderName);
            Assert.That(blendShader, Is.Not.Null, "Shader 'Realmz/TerrainBlend' not found.");
            Assert.That(ShaderUtil.ShaderHasError(blendShader), Is.False, "Shader 'Realmz/TerrainBlend' has compile errors.");

            string[] blendProps =
            {
                "_SplatMap",
                "_WorldBounds",
                "_WorldTileScale",
                "_BlendContrast",
                "_BumpScale",
                "_MacroScale",
                "_MacroStrength",
                "_GridHintStrength",
                "_Layer0Albedo",
                "_Layer0Normal",
                "_Layer0Mask",
                "_Layer0Color",
                "_Layer1Albedo",
                "_Layer2Albedo",
                "_Layer3Albedo"
            };

            for (int i = 0; i < blendProps.Length; i++)
            {
                Assert.That(
                    blendShader.FindPropertyIndex(blendProps[i]),
                    Is.GreaterThanOrEqualTo(0),
                    "Missing Realmz/TerrainBlend shader property: " + blendProps[i]);
            }

            Shader grassShader = Shader.Find(RealmzAssetInboxPostprocessor.GrassTuftShaderName);
            Assert.That(grassShader, Is.Not.Null, "Shader 'Realmz/GrassTuft' not found.");
            Assert.That(ShaderUtil.ShaderHasError(grassShader), Is.False, "Shader 'Realmz/GrassTuft' has compile errors.");

            string[] grassProps =
            {
                "_BaseColor",
                "_TipColor",
                "_WindSpeed",
                "_WindStrength"
            };

            for (int i = 0; i < grassProps.Length; i++)
            {
                Assert.That(
                    grassShader.FindPropertyIndex(grassProps[i]),
                    Is.GreaterThanOrEqualTo(0),
                    "Missing Realmz/GrassTuft shader property: " + grassProps[i]);
            }
        }

        [Test]
        public void InboxPostprocessor_ProcessesSyntheticTerrainAsset_AndGeneratesReceipt()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absFolder = Path.Combine(projectRoot, "Assets", "Inbox", TestTerrainFolder);
            Directory.CreateDirectory(absFolder);

            WriteSolidPng(Path.Combine(absFolder, TestTerrainId + "_albedo.png"), 16, 16, new Color32(110, 150, 72, 255));
            WriteSolidPng(Path.Combine(absFolder, TestTerrainId + "_normal.png"), 16, 16, new Color32(128, 128, 255, 255));
            WriteSolidPng(Path.Combine(absFolder, TestTerrainId + "_height.png"), 16, 16, new Color32(140, 140, 140, 255));
            WriteSolidPng(Path.Combine(absFolder, TestTerrainId + "_roughness.png"), 16, 16, new Color32(210, 210, 210, 255));
            WriteSolidPng(Path.Combine(absFolder, TestTerrainId + "_ao.png"), 16, 16, new Color32(230, 230, 230, 255));
            WriteSolidPng(Path.Combine(absFolder, TestTerrainId + "_mask.png"), 16, 16, new Color32(0, 230, 140, 45));

            FoundryAssetManifestData manifest = new FoundryAssetManifestData
            {
                schema_version = "1.0",
                id = TestTerrainId,
                name = "Synthetic Foundry Terrain",
                category = "Terrain",
                asset_kind = "terrain",
                resolution = 16,
                style = "painterly_pbr",
                tile_scale_m = 1.0f,
                maps = new FoundryTerrainMapsData
                {
                    albedo = TestTerrainId + "_albedo.png",
                    normal = TestTerrainId + "_normal.png",
                    height = TestTerrainId + "_height.png",
                    roughness = TestTerrainId + "_roughness.png",
                    ao = TestTerrainId + "_ao.png",
                    mask = TestTerrainId + "_mask.png"
                },
                timestamp = "2026-11-23T17:00:00Z"
            };

            File.WriteAllText(
                Path.Combine(absFolder, "terrain.asset.json"),
                JsonUtility.ToJson(manifest, true),
                Utf8NoBom);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            FoundryImportReceipt receipt = RealmzAssetInboxPostprocessor.ProcessAsset(TestTerrainFolder);
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt.success, Is.True, receipt.notes);

            string folderRel = RealmzAssetInboxPostprocessor.InboxRoot + "/" + TestTerrainFolder;
            AssertTerrainTextureImport(folderRel + "/" + TestTerrainId + "_albedo.png", isNormal: false, expectSrgb: true);
            AssertTerrainTextureImport(folderRel + "/" + TestTerrainId + "_normal.png", isNormal: true, expectSrgb: false);
            AssertTerrainTextureImport(folderRel + "/" + TestTerrainId + "_height.png", isNormal: false, expectSrgb: false);
            AssertTerrainTextureImport(folderRel + "/" + TestTerrainId + "_roughness.png", isNormal: false, expectSrgb: false);
            AssertTerrainTextureImport(folderRel + "/" + TestTerrainId + "_ao.png", isNormal: false, expectSrgb: false);
            AssertTerrainTextureImport(folderRel + "/" + TestTerrainId + "_mask.png", isNormal: false, expectSrgb: false);

            string matRel = RealmzAssetInboxPostprocessor.MaterialsFoundryRoot + "/" + TestTerrainId + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matRel);
            Assert.That(mat, Is.Not.Null, "Expected terrain material at " + matRel);

            string receiptAbs = Path.Combine(absFolder, "import_receipt.json");
            Assert.That(File.Exists(receiptAbs), Is.True, "Expected terrain import_receipt.json at " + receiptAbs);
            FoundryImportReceipt diskReceipt = JsonUtility.FromJson<FoundryImportReceipt>(File.ReadAllText(receiptAbs, Encoding.UTF8));
            Assert.That(diskReceipt, Is.Not.Null);
            Assert.That(diskReceipt.success, Is.True);
        }

        [Test]
        public void BywaterGroundScatter_PlacesDeterministicallyAndExcludesRoadsWaterDungeon()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            Assert.That(bundle.Land0, Is.Not.Null);
            Assert.That(bundle.Dungeon0, Is.Not.Null);

            GameObject go = new GameObject("TempBywaterGroundScatterTest");
            try
            {
                BywaterGroundScatter scatter = go.AddComponent<BywaterGroundScatter>();
                GridCoord focus = new GridCoord(10, 9);
                scatter.RebuildForMap(bundle.Land0, focus, windowRadius: 16);

                int firstTuftCount = scatter.ActiveTuftCount;
                int firstFlowerCount = scatter.ActiveFlowerCount;
                int firstPebbleCount = scatter.ActivePebbleCount;
                Assert.That(firstTuftCount, Is.GreaterThan(0), "Expected grass tufts on Bywater land_0.");

                bool foundOpenGrassWithScatter = scatter.HasScatterAtTile(2, 1);
                int checkedCobble = 0;
                int checkedWater = 0;
                int checkedBuilding = 0;
                var firstSnapshot = new List<bool>();

                int minX = Mathf.Max(0, focus.X - 16);
                int maxX = Mathf.Min(bundle.Land0.Width - 1, focus.X + 16);
                int minY = Mathf.Max(0, focus.Y - 16);
                int maxY = Mathf.Min(bundle.Land0.Height - 1, focus.Y + 16);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        bool hasScatter = scatter.HasScatterAtTile(x, y);
                        firstSnapshot.Add(hasScatter);

                        BywaterTileKind kind = BywaterTileClassifier.Classify(bundle.Land0, x, y);
                        if (kind == BywaterTileKind.Grass && hasScatter)
                        {
                            foundOpenGrassWithScatter = true;
                        }
                        else if (kind == BywaterTileKind.Cobblestone)
                        {
                            checkedCobble++;
                            Assert.That(hasScatter, Is.False, $"Cobblestone tile ({x}, {y}) must not have scatter.");
                        }
                        else if (kind == BywaterTileKind.ShallowWater)
                        {
                            checkedWater++;
                            Assert.That(hasScatter, Is.False, $"ShallowWater tile ({x}, {y}) must not have scatter.");
                        }
                        else if (kind == BywaterTileKind.TownBuildingSolid || kind == BywaterTileKind.WallBlock)
                        {
                            checkedBuilding++;
                            Assert.That(hasScatter, Is.False, $"Solid building/wall tile ({x}, {y}) must not have scatter.");
                        }
                        else if (kind == BywaterTileKind.TownBuildingDoor || kind == BywaterTileKind.DirtPath)
                        {
                            Assert.That(hasScatter, Is.False, $"Non-grass tile {kind} at ({x}, {y}) must not have scatter.");
                        }
                    }
                }

                Assert.That(foundOpenGrassWithScatter, Is.True, "Expected at least one open grass tile in window to have scatter.");
                Assert.That(checkedCobble, Is.GreaterThan(0), "Expected Cobblestone tiles in town window.");
                Assert.That(checkedBuilding, Is.GreaterThan(0), "Expected TownBuildingSolid/WallBlock tiles in town window.");

                // Verify deterministic repeatability across two rebuilds
                scatter.RebuildForMap(bundle.Land0, focus, windowRadius: 16);
                Assert.That(scatter.ActiveTuftCount, Is.EqualTo(firstTuftCount));
                Assert.That(scatter.ActiveFlowerCount, Is.EqualTo(firstFlowerCount));
                Assert.That(scatter.ActivePebbleCount, Is.EqualTo(firstPebbleCount));

                int idx = 0;
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Assert.That(
                            scatter.HasScatterAtTile(x, y),
                            Is.EqualTo(firstSnapshot[idx++]),
                            $"Scatter placement not deterministic at ({x}, {y}).");
                    }
                }

                // Locate ShallowWater tiles on land_0 and verify zero scatter on water tiles
                GridCoord? firstWater = null;
                for (int y = 0; y < bundle.Land0.Height && !firstWater.HasValue; y++)
                {
                    for (int x = 0; x < bundle.Land0.Width; x++)
                    {
                        if (BywaterTileClassifier.Classify(bundle.Land0, x, y) == BywaterTileKind.ShallowWater)
                        {
                            firstWater = new GridCoord(x, y);
                            break;
                        }
                    }
                }

                Assert.That(firstWater.HasValue, Is.True, "Expected at least one ShallowWater tile on land_0.");
                scatter.RebuildForMap(bundle.Land0, firstWater.Value, windowRadius: 16);
                int wMinX = Mathf.Max(0, firstWater.Value.X - 16);
                int wMaxX = Mathf.Min(bundle.Land0.Width - 1, firstWater.Value.X + 16);
                int wMinY = Mathf.Max(0, firstWater.Value.Y - 16);
                int wMaxY = Mathf.Min(bundle.Land0.Height - 1, firstWater.Value.Y + 16);
                for (int y = wMinY; y <= wMaxY; y++)
                {
                    for (int x = wMinX; x <= wMaxX; x++)
                    {
                        if (BywaterTileClassifier.Classify(bundle.Land0, x, y) == BywaterTileKind.ShallowWater)
                        {
                            checkedWater++;
                            Assert.That(scatter.HasScatterAtTile(x, y), Is.False, $"ShallowWater tile ({x}, {y}) must not have scatter.");
                        }
                    }
                }
                Assert.That(checkedWater, Is.GreaterThan(0), "Expected ShallowWater tiles in water window.");

                // Verify dungeon produces zero scatter
                scatter.RebuildForMap(bundle.Dungeon0, new GridCoord(33, 72), windowRadius: 16);
                Assert.That(scatter.ActiveTuftCount, Is.EqualTo(0), "Dungeon must have 0 grass tufts.");
                Assert.That(scatter.ActiveFlowerCount, Is.EqualTo(0), "Dungeon must have 0 wildflowers.");
                Assert.That(scatter.ActivePebbleCount, Is.EqualTo(0), "Dungeon must have 0 pebbles.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void AssertTerrainTextureImport(string assetPath, bool isNormal, bool expectSrgb)
        {
            TextureImporter imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(imp, Is.Not.Null, "Missing TextureImporter for " + assetPath);
            Assert.That(imp.filterMode, Is.EqualTo(FilterMode.Trilinear), "Expected Trilinear filterMode on " + assetPath);
            Assert.That(imp.anisoLevel, Is.EqualTo(8), "Expected anisoLevel == 8 on " + assetPath);
            Assert.That(imp.wrapMode, Is.EqualTo(TextureWrapMode.Repeat), "Expected Repeat wrapMode on " + assetPath);
            Assert.That(imp.sRGBTexture, Is.EqualTo(expectSrgb), "Unexpected sRGBTexture on " + assetPath);
            Assert.That(
                imp.textureType,
                Is.EqualTo(isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default),
                "Unexpected textureType on " + assetPath);
        }

        private static void WriteSolidPng(string absPath, int width, int height, Color32 color)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                Color32[] pixels = new Color32[width * height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = color;
                }

                tex.SetPixels32(pixels);
                tex.Apply(false, false);
                File.WriteAllBytes(absPath, tex.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        private static void DeleteIfExists(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath) || File.Exists(Path.GetFullPath(assetPath)))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }
    }
}
