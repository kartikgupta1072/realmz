using System.IO;
using System.Text;
using NUnit.Framework;
using Realmz.EditorTools;
using Realmz.Foundry;
using Realmz.Rendering;
using UnityEditor;
using UnityEngine;

namespace Realmz.EditorTests
{
    public sealed class RealmzFoundryEditorTests
    {
        private const string TestAssetId = "__foundry_unit_test";
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
