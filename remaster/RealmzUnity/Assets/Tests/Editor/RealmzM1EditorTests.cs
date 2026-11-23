using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Realmz.Data;
using Realmz.EditorTools;
using Realmz.Game;
using Realmz.World;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Realmz.EditorTests
{
    public sealed class RealmzM1EditorTests
    {
        [Test]
        public void StreamingAssetsBundle_LoadsAllBywaterM1Data()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            Assert.That(Directory.Exists(bundleDir), Is.True, "StreamingAssets/bywater_m1 missing.");

            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            Assert.That(bundle.Land0, Is.Not.Null);
            Assert.That(bundle.Dungeon0, Is.Not.Null);
            Assert.That(bundle.Land0.Width, Is.EqualTo(90));
            Assert.That(bundle.Land0.Height, Is.EqualTo(90));
            Assert.That(bundle.Dungeon0.Width, Is.EqualTo(90));
            Assert.That(bundle.Dungeon0.Height, Is.EqualTo(90));
            Assert.That(bundle.Party.Count, Is.EqualTo(6));
            Assert.That(bundle.Monsters.Count, Is.EqualTo(12));
            Assert.That(bundle.Spells.Count, Is.EqualTo(12));
            Assert.That(bundle.Items.Count, Is.EqualTo(58));
            Assert.That(bundle.Shops.Count, Is.EqualTo(3));

            MapPortal portal = bundle.Land0.GetPortalAt(89, 48);
            Assert.That(portal, Is.Not.Null);
            Assert.That(portal.DestMapId, Is.EqualTo("dungeon_0"));
            Assert.That(portal.DestCoord, Is.EqualTo(new GridCoord(33, 72)));
        }

        [Test]
        public void All68M15ArtPngs_AreImportedWithCrispPointAndNormalSettings()
        {
            string m1Root = Path.GetFullPath(Path.Combine(Application.dataPath, "Art", "M1"));
            string[] pngs = Directory.GetFiles(m1Root, "*.png", SearchOption.AllDirectories);
            Assert.That(pngs.Length, Is.EqualTo(68), "Expected all 68 M1.5 art PNGs under Assets/Art/M1.");

            foreach (string fullPath in pngs)
            {
                string rel = "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');
                TextureImporter importer = AssetImporter.GetAtPath(rel) as TextureImporter;
                Assert.That(importer, Is.Not.Null, "Missing TextureImporter for " + rel);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), "Expected Point filter on " + rel);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), "Expected Uncompressed on " + rel);
                Assert.That(importer.mipmapEnabled, Is.False, "Expected mipmaps off on " + rel);

                if (rel.EndsWith("_n.png"))
                {
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap), "Expected NormalMap on " + rel);
                    Assert.That(importer.sRGBTexture, Is.False, "Expected linear sRGB=false on " + rel);
                }
                else if (rel.Contains("/Tiles/"))
                {
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), "Expected Default textureType on tile " + rel);
                    Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat), "Expected Repeat wrapMode on tile " + rel);
                }
                else
                {
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), "Expected Sprite textureType on " + rel);
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                    float expectedPpu = (tex != null && tex.width >= 256)
                        ? 256f
                        : ((tex != null && tex.width >= 128) ? 128f : 64f);
                    Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(expectedPpu), "Expected " + expectedPpu + " PPU on " + rel);
                }
            }
        }

        [Test]
        public void AssetCatalog_HasAllHeroesMonstersTilesPropsAndUiFont()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null, "BywaterAssetCatalog asset missing from Assets/Resources.");
            Assert.That(catalog.heroes.Count, Is.EqualTo(6));
            Assert.That(catalog.monsters.Count, Is.EqualTo(6));
            Assert.That(catalog.grassMaterial, Is.Not.Null);
            Assert.That(catalog.cobblestoneMaterial, Is.Not.Null);
            Assert.That(catalog.dirtPathMaterial, Is.Not.Null);
            Assert.That(catalog.dungeonFloorMaterial, Is.Not.Null);
            Assert.That(catalog.dungeonWallMaterial, Is.Not.Null);
            Assert.That(catalog.shallowWaterMaterial, Is.Not.Null);
            Assert.That(catalog.torchPropMaterial, Is.Not.Null);
            Assert.That(catalog.torchEmberMaterial, Is.Not.Null);
            Assert.That(catalog.portalBeaconMaterial, Is.Not.Null);
            Assert.That(catalog.overlayBaseMaterial, Is.Not.Null);
            Assert.That(catalog.torchSprite, Is.Not.Null);
            Assert.That(catalog.uiFont, Is.Not.Null);

            for (int i = 0; i < catalog.heroes.Count; i++)
            {
                Assert.That(catalog.heroes[i].billboardMaterial, Is.Not.Null, "Hero material missing: " + catalog.heroes[i].id);
                Assert.That(catalog.heroes[i].sprite, Is.Not.Null, "Hero sprite missing: " + catalog.heroes[i].id);
                Assert.That(catalog.heroes[i].portrait, Is.Not.Null, "Hero portrait missing: " + catalog.heroes[i].id);
            }

            for (int i = 0; i < catalog.monsters.Count; i++)
            {
                Assert.That(catalog.monsters[i].billboardMaterial, Is.Not.Null, "Monster material missing: " + catalog.monsters[i].key);
                Assert.That(catalog.monsters[i].sprite, Is.Not.Null, "Monster sprite missing: " + catalog.monsters[i].key);
            }
        }

        [Test]
        public void BywaterScene_IsBuiltWiredAndRegisteredInBuildSettings()
        {
            Assert.That(File.Exists(Path.GetFullPath(M1BywaterSceneBuilder.ScenePath)), Is.True, "M1_Bywater_Slice.unity missing.");
            Assert.That(File.Exists(Path.GetFullPath("Assets/Scenes/M0_HD2D_SmokeScene.unity")), Is.True, "M0_HD2D_SmokeScene.unity must remain intact.");

            Scene scene = EditorSceneManager.OpenScene(M1BywaterSceneBuilder.ScenePath, OpenSceneMode.Single);
            Assert.That(scene.IsValid(), Is.True);

            Camera cam = Camera.main;
            Assert.That(cam, Is.Not.Null);
            Assert.That(cam.allowHDR, Is.True);
            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            Assert.That(camData, Is.Not.Null);
            Assert.That(camData.renderPostProcessing, Is.True);

            Volume vol = Object.FindFirstObjectByType<Volume>();
            Assert.That(vol, Is.Not.Null);
            Assert.That(vol.sharedProfile, Is.Not.Null);
            Assert.That(vol.sharedProfile.Has<Tonemapping>(), Is.True);
            Assert.That(vol.sharedProfile.Has<Bloom>(), Is.True);

            EventSystem es = Object.FindFirstObjectByType<EventSystem>();
            Assert.That(es, Is.Not.Null);
            Assert.That(es.GetComponent<InputSystemUIInputModule>(), Is.Not.Null);

            BywaterGameController ctrl = Object.FindFirstObjectByType<BywaterGameController>();
            Assert.That(ctrl, Is.Not.Null);
            Assert.That(ctrl.assetCatalog, Is.Not.Null);
            Assert.That(ctrl.worldRenderer, Is.Not.Null);
            Assert.That(ctrl.cameraRig, Is.Not.Null);
            Assert.That(ctrl.gridOverlays, Is.Not.Null);
            Assert.That(ctrl.hudController, Is.Not.Null);

            Assert.That(EditorBuildSettings.scenes.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(M1BywaterSceneBuilder.ScenePath));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
        }

        [Test]
        public void TileClassifierAndSaveService_BehaveDeterministically()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            Assert.That(BywaterTileClassifier.Classify(bundle.Land0, 2, 1), Is.EqualTo(BywaterTileKind.Grass));
            Assert.That(BywaterTileClassifier.Classify(bundle.Land0, 7, 11), Is.EqualTo(BywaterTileKind.TownBuildingDoor));
            Assert.That(BywaterTileClassifier.Classify(bundle.Dungeon0, 32, 72), Is.EqualTo(BywaterTileKind.DungeonStairs));
            Assert.That(BywaterTileClassifier.Classify(bundle.Dungeon0, 33, 72), Is.EqualTo(BywaterTileKind.DungeonFloor));
            Assert.That(BywaterTileClassifier.Classify(bundle.Dungeon0, 30, 70), Is.EqualTo(BywaterTileKind.DungeonWall));

            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession session = new ExplorationSession(bundle.Maps, "land_0", party, new DeterministicDiceRoller(1234UL));
            session.MoveAlongPath(new GridCoord(8, 11), autoHaltOnEvents: false);
            session.Party.Gold = 777;
            session.Party.Members[0].Hp = 9;

            string tempPath = Path.Combine(Path.GetTempPath(), "realmz_m1_save_test.json");
            try
            {
                Assert.That(BywaterSaveService.Save(tempPath, session), Is.True);
                Assert.That(BywaterSaveService.Load(tempPath, bundle, out ExplorationSession loaded, out string status), Is.True, status);
                Assert.That(loaded.CurrentMap.MapId, Is.EqualTo("land_0"));
                Assert.That(loaded.Position, Is.EqualTo(new GridCoord(8, 11)));
                Assert.That(loaded.Party.Gold, Is.EqualTo(777));
                Assert.That(loaded.Party.Members[0].Hp, Is.EqualTo(9));
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        [Test]
        public void DualBiomeAtmosphere_SwapsDaylightAndDungeonSettings()
        {
            AmbientMode prevMode = RenderSettings.ambientMode;
            Color prevAmbient = RenderSettings.ambientLight;
            Color prevSky = RenderSettings.ambientSkyColor;
            Color prevEquator = RenderSettings.ambientEquatorColor;
            Color prevGround = RenderSettings.ambientGroundColor;

            GameObject camGo = null;
            GameObject sunGo = null;
            GameObject volGo = null;
            GameObject atmoGo = null;
            VolumeProfile tempProfile = null;

            try
            {
                camGo = new GameObject("TempMainCamera", typeof(Camera));
                camGo.tag = "MainCamera";
                Camera cam = camGo.GetComponent<Camera>();
                cam.allowHDR = true;
                UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
                if (camData != null)
                {
                    camData.renderPostProcessing = true;
                }

                sunGo = new GameObject("TempDirectionalSun", typeof(Light));
                Light sun = sunGo.GetComponent<Light>();
                sun.type = LightType.Directional;

                tempProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                Tonemapping tonemapping = tempProfile.Add<Tonemapping>(true);
                tonemapping.mode.overrideState = true;
                tonemapping.mode.value = TonemappingMode.ACES;
                Bloom bloom = tempProfile.Add<Bloom>(true);
                bloom.intensity.overrideState = true;
                bloom.intensity.value = 0.45f;

                volGo = new GameObject("TempGlobalVolume", typeof(Volume));
                Volume vol = volGo.GetComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = tempProfile;

                atmoGo = new GameObject("TempBiomeAtmosphere");
                BywaterBiomeAtmosphere atmo = atmoGo.AddComponent<BywaterBiomeAtmosphere>();
                atmo.targetCamera = cam;
                atmo.directionalSun = sun;
                atmo.globalVolume = vol;

                // 1. Daylight (land_0)
                atmo.ApplyBiome(false, true);
                Assert.That(sun.intensity, Is.InRange(1.8f, 2.2f), "Daylight sun intensity should be in [1.8, 2.2].");
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight), "Daylight should use Trilight ambient.");
                Assert.That(RenderSettings.ambientSkyColor.b, Is.GreaterThan(0.7f), "Daylight ambientSkyColor.b should be > 0.7.");
                Assert.That(tonemapping.mode.value, Is.EqualTo(TonemappingMode.Neutral), "Daylight tonemapping should be Neutral.");
                Assert.That(atmo.sunMoteParticles, Is.Not.Null, "sunMoteParticles ParticleSystem missing.");
                Assert.That(atmo.sunMoteParticles.isPlaying, Is.True, "sunMoteParticles should be playing in daylight.");

                // 2. Dungeon (dungeon_0)
                atmo.ApplyBiome(true, true);
                Assert.That(sun.intensity, Is.LessThan(0.5f), "Dungeon sun intensity should be < 0.5.");
                Assert.That(RenderSettings.ambientSkyColor.r, Is.LessThan(0.25f), "Dungeon ambientSkyColor.r should be < 0.25.");
                Assert.That(tonemapping.mode.value, Is.EqualTo(TonemappingMode.ACES), "Dungeon tonemapping should be ACES.");
                Assert.That(atmo.sunMoteParticles.isPlaying, Is.False, "sunMoteParticles should stop playing in dungeon.");
            }
            finally
            {
                if (atmoGo != null) Object.DestroyImmediate(atmoGo);
                if (volGo != null) Object.DestroyImmediate(volGo);
                if (sunGo != null) Object.DestroyImmediate(sunGo);
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (tempProfile != null) Object.DestroyImmediate(tempProfile);

                RenderSettings.ambientMode = prevMode;
                RenderSettings.ambientLight = prevAmbient;
                RenderSettings.ambientSkyColor = prevSky;
                RenderSettings.ambientEquatorColor = prevEquator;
                RenderSettings.ambientGroundColor = prevGround;
            }
        }

        [Test]
        public void TileClassifier_DeterministicVariantsAndPropKinds_KillOutdoorGrayCubes()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            HashSet<int> variants = new HashSet<int>();
            for (int x = 0; x <= 25; x++)
            {
                for (int y = 0; y <= 25; y++)
                {
                    int first = BywaterTileClassifier.GetTileVariantIndex(x, y, 3);
                    int second = BywaterTileClassifier.GetTileVariantIndex(x, y, 3);
                    Assert.That(first, Is.EqualTo(second), $"Variant not deterministic at ({x}, {y}).");
                    Assert.That(first, Is.InRange(0, 2), $"Variant index out of range at ({x}, {y}).");
                    variants.Add(first);
                }
            }

            Assert.That(variants.Count, Is.GreaterThan(1), "Expected multiple tile variants across town coordinates.");

            int checkedSolidWalls = 0;
            int checkedCottages = 0;
            for (int x = 0; x < bundle.Land0.Width; x++)
            {
                for (int y = 0; y < bundle.Land0.Height; y++)
                {
                    BywaterTileKind kind = BywaterTileClassifier.Classify(bundle.Land0, x, y);
                    if (kind == BywaterTileKind.TownBuildingSolid || kind == BywaterTileKind.WallBlock)
                    {
                        BywaterOutdoorPropKind propKind = BywaterTileClassifier.ClassifyOutdoorProp(bundle.Land0, x, y, out _);
                        Assert.That(propKind, Is.Not.EqualTo(BywaterOutdoorPropKind.None), $"Outdoor wall/building at ({x}, {y}) should map to a 2.5D prop instead of a gray cube.");
                        checkedSolidWalls++;
                        if (propKind == BywaterOutdoorPropKind.CottageFacade)
                        {
                            checkedCottages++;
                        }
                    }
                }
            }

            Assert.That(checkedSolidWalls, Is.GreaterThan(0), "Expected outdoor WallBlock or TownBuildingSolid tiles on land_0.");
            Assert.That(checkedCottages, Is.GreaterThan(0), "Expected town building/wall tiles on land_0 to map to CottageFacade.");
            Assert.That(BywaterTileClassifier.ClassifyOutdoorProp(bundle.Land0, 0, 4, out _), Is.EqualTo(BywaterOutdoorPropKind.CottageFacade), "Town gate tile (0, 4) should map to CottageFacade.");
            Assert.That(BywaterTileClassifier.ClassifyOutdoorProp(bundle.Land0, 12, 12, out _), Is.EqualTo(BywaterOutdoorPropKind.StoneFence), "Perimeter wall (12, 12) should map to StoneFence.");

            BywaterOutdoorPropKind forestProp = BywaterTileClassifier.ClassifyOutdoorProp(bundle.Land0, 14, 9, out _);
            Assert.That(forestProp == BywaterOutdoorPropKind.OakTree || forestProp == BywaterOutdoorPropKind.PineTree, Is.True, "Contiguous forest (14, 9) should map to OakTree or PineTree, got: " + forestProp);
            Assert.That(BywaterTileClassifier.ClassifyOutdoorProp(bundle.Land0, 6, 8, out _), Is.EqualTo(BywaterOutdoorPropKind.WildflowerBush), "Isolated town shrub (6, 8) should map to low WildflowerBush.");

            Assert.That(BywaterTileClassifier.ClassifyOutdoorProp(bundle.Dungeon0, 30, 70, out _), Is.EqualTo(BywaterOutdoorPropKind.None), "Dungeon wall should return None to preserve stone blocks.");
        }

        [Test]
        public void AssetCatalog_HasTileVariantsWaterShaderAndPropSlots()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null, "BywaterAssetCatalog asset missing from Assets/Resources.");

            AssertVariantArray(catalog.grassVariantMaterials, nameof(catalog.grassVariantMaterials));
            AssertVariantArray(catalog.cobblestoneVariantMaterials, nameof(catalog.cobblestoneVariantMaterials));
            AssertVariantArray(catalog.dirtPathVariantMaterials, nameof(catalog.dirtPathVariantMaterials));
            AssertVariantArray(catalog.dungeonFloorVariantMaterials, nameof(catalog.dungeonFloorVariantMaterials));
            AssertVariantArray(catalog.dungeonWallVariantMaterials, nameof(catalog.dungeonWallVariantMaterials));

            Assert.That(catalog.shallowWaterMaterial, Is.Not.Null);
            Assert.That(catalog.shallowWaterMaterial.shader, Is.Not.Null);
            Assert.That(catalog.shallowWaterMaterial.shader.name, Is.EqualTo("Realmz/BywaterShimmerWater"));

            Material[] propMaterials =
            {
                catalog.cottageWallMaterial,
                catalog.cottageRoofTerracottaMaterial,
                catalog.cottageRoofSlateMaterial,
                catalog.cottageRoofThatchMaterial,
                catalog.timberTrimMaterial,
                catalog.warmWindowGlowMaterial,
                catalog.oakFoliageMaterial,
                catalog.pineFoliageMaterial,
                catalog.treeTrunkMaterial,
                catalog.boulderMaterial,
                catalog.stoneFenceMaterial,
                catalog.crateWoodMaterial,
                catalog.ironTrimMaterial,
                catalog.wildflowerMaterial
            };

            Assert.That(propMaterials.Length, Is.EqualTo(14));
            for (int i = 0; i < propMaterials.Length; i++)
            {
                Assert.That(propMaterials[i], Is.Not.Null, $"M1.5 modular prop material at index {i} is null.");
            }

            Assert.That(catalog.cottageBillboardSprite, Is.Not.Null, "cottageBillboardSprite missing.");
            Assert.That(catalog.cottageBillboardMaterial, Is.Not.Null, "cottageBillboardMaterial missing.");
            Assert.That(catalog.oakTreeBillboardSprite, Is.Not.Null, "oakTreeBillboardSprite missing.");
            Assert.That(catalog.oakTreeBillboardMaterial, Is.Not.Null, "oakTreeBillboardMaterial missing.");
            Assert.That(catalog.pineTreeBillboardSprite, Is.Not.Null, "pineTreeBillboardSprite missing.");
            Assert.That(catalog.pineTreeBillboardMaterial, Is.Not.Null, "pineTreeBillboardMaterial missing.");
            Assert.That(catalog.stoneFenceBillboardSprite, Is.Not.Null, "stoneFenceBillboardSprite missing.");
            Assert.That(catalog.stoneFenceBillboardMaterial, Is.Not.Null, "stoneFenceBillboardMaterial missing.");
            Assert.That(catalog.marketCratesBillboardSprite, Is.Not.Null, "marketCratesBillboardSprite missing.");
            Assert.That(catalog.marketCratesBillboardMaterial, Is.Not.Null, "marketCratesBillboardMaterial missing.");
            Assert.That(catalog.wildflowerBushBillboardSprite, Is.Not.Null, "wildflowerBushBillboardSprite missing.");
            Assert.That(catalog.wildflowerBushBillboardMaterial, Is.Not.Null, "wildflowerBushBillboardMaterial missing.");
            Assert.That(catalog.brazierBillboardSprite, Is.Not.Null, "brazierBillboardSprite missing.");
            Assert.That(catalog.brazierBillboardMaterial, Is.Not.Null, "brazierBillboardMaterial missing.");
        }

        [Test]
        public void HudOverhaul_EnforcesReadable15PtFloorAndPortraitFrames()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            bool hadEventSystem = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject tempGo = new GameObject("TempBywaterHudHost");

            try
            {
                BywaterHudController hud = tempGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas.ForceUpdateCanvases();

                Canvas canvas = tempGo.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null, "BywaterHudCanvas was not created by BuildUi.");

                TextMeshProUGUI[] labels = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                Assert.That(labels.Length, Is.GreaterThan(0), "Expected TextMeshProUGUI components under BywaterHudCanvas.");

                for (int i = 0; i < labels.Length; i++)
                {
                    TextMeshProUGUI tmp = labels[i];
                    Assert.That(tmp.fontSize, Is.GreaterThanOrEqualTo(15f), $"Label '{tmp.gameObject.name}' has sub-15pt fontSize ({tmp.fontSize}).");
                }

                Transform heroPortraitFrame = FindDescendantByPathSuffix(canvas.transform, "HeroCard_0/PortraitFrame");
                Assert.That(heroPortraitFrame, Is.Not.Null, "HeroCard_0/PortraitFrame not found under Canvas.");
                AssertPortraitFrameSize(heroPortraitFrame, 74f);

                Transform drawerPortraitFrame = FindDescendantByName(canvas.transform, "DrawerPortraitFrame");
                Assert.That(drawerPortraitFrame, Is.Not.Null, "DrawerPortraitFrame not found under Canvas.");
                AssertPortraitFrameSize(drawerPortraitFrame, 128f);
            }
            finally
            {
                Object.DestroyImmediate(tempGo);
                if (!hadEventSystem)
                {
                    EventSystem createdEs = Object.FindFirstObjectByType<EventSystem>();
                    if (createdEs != null)
                    {
                        Object.DestroyImmediate(createdEs.gameObject);
                    }
                }
            }
        }

        private static void AssertVariantArray(Material[] mats, string fieldName)
        {
            Assert.That(mats, Is.Not.Null, fieldName + " is null.");
            Assert.That(mats.Length, Is.EqualTo(3), fieldName + " should have exactly 3 materials.");
            for (int i = 0; i < mats.Length; i++)
            {
                Assert.That(mats[i], Is.Not.Null, $"{fieldName}[{i}] is null.");
            }
        }

        private static Transform FindDescendantByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendantByName(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        private static Transform FindDescendantByPathSuffix(Transform root, string pathSuffix)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string path = GetRelativePath(root, all[i]);
                if (path.EndsWith(pathSuffix, System.StringComparison.Ordinal))
                {
                    return all[i];
                }
            }

            return null;
        }

        private static string GetRelativePath(Transform root, Transform node)
        {
            List<string> parts = new List<string>();
            Transform cur = node;
            while (cur != null && cur != root)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void AssertPortraitFrameSize(Transform frame, float minSize)
        {
            RectTransform rt = frame.GetComponent<RectTransform>();
            Assert.That(rt, Is.Not.Null);
            UnityEngine.UI.LayoutElement le = frame.GetComponent<UnityEngine.UI.LayoutElement>();
            float w = Mathf.Max(rt.rect.width, le != null ? le.preferredWidth : 0f, rt.sizeDelta.x);
            float h = Mathf.Max(rt.rect.height, le != null ? le.preferredHeight : 0f, rt.sizeDelta.y);
            Assert.That(w, Is.GreaterThanOrEqualTo(minSize), $"{frame.name} width {w} < {minSize}.");
            Assert.That(h, Is.GreaterThanOrEqualTo(minSize), $"{frame.name} height {h} < {minSize}.");
        }
    }
}
