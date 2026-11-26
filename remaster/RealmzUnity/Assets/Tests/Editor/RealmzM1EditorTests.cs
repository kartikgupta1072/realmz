using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Realmz.Audio;
using Realmz.Cartography;
using Realmz.Data;
using Realmz.EditorTools;
using Realmz.Game;
using Realmz.World;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
                    bool isUnifiedCharacterOrPortrait =
                        rel.Contains("/Heroes/") ||
                        rel.Contains("/Monsters/") ||
                        rel.Contains("/Portraits/");
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                    float expectedPpu = isUnifiedCharacterOrPortrait
                        ? 64f
                        : ((tex != null && tex.width >= 256)
                            ? 256f
                            : ((tex != null && tex.width >= 128) ? 128f : 64f));
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
            Assert.That(catalog.monsters.Count, Is.EqualTo(7));
            Assert.That(catalog.hotbarIcons.Count, Is.EqualTo(21));
            Assert.That(catalog.cartographyUnitSheets.Count, Is.EqualTo(13));
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

            Scene scene = EditorSceneManager.OpenScene(M1BywaterSceneBuilder.ScenePath, OpenSceneMode.Single);
            Assert.That(scene.IsValid(), Is.True);

            Camera cam = Camera.main;
            Assert.That(cam, Is.Not.Null);
            Assert.That(cam.allowHDR, Is.True);
            Assert.That(cam.GetComponent<AudioListener>(), Is.Not.Null, "Main Camera must carry the single AudioListener.");
            Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1), "Expected exactly 1 AudioListener in M1_Bywater_Slice.");
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
            Assert.That(ctrl.audioDirector, Is.Not.Null);

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
                Assert.That(atmo.HasFirefliesActive, Is.False, "Dungeon should switch motes to CaveSpores, not Fireflies.");
                Assert.That(atmo.sunMoteParticles.isPlaying, Is.True, "Dungeon should emit subterranean CaveSpores motes.");
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
            Assert.That(catalog.shallowWaterMaterial.shader.name, Is.EqualTo("Realmz/CartographyWater"));

            Assert.That(catalog.cartographyTerrainMaterial, Is.Not.Null, "cartographyTerrainMaterial missing.");
            Assert.That(catalog.cartographyWaterMaterial, Is.Not.Null, "cartographyWaterMaterial missing.");
            Assert.That(catalog.cartographyFoliageMaterial, Is.Not.Null, "cartographyFoliageMaterial missing.");

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

        [Test]
        public void SaveService_AtomicRoundTrip_PreservesConditionsChargesXpTurnsClearedZonesAndRngSeed()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            PartyState party = PartyState.FromBundle(bundle);
            DeterministicDiceRoller roller = new DeterministicDiceRoller(987654321UL);
            roller.NextInt(1, 100);

            ExplorationSession session = new ExplorationSession(bundle.Maps, "land_0", party, roller);
            GridCoord stepNeighbor = FindAdjacentPassableTile(session.CurrentMap, session.Position, session.Party.HasBoat);
            GridCoord startPos = session.Position;
            Assert.That(session.StepOneTile(stepNeighbor, autoHaltOnEvents: false).Moved, Is.True);
            Assert.That(session.StepOneTile(startPos, autoHaltOnEvents: false).Moved, Is.True);
            Assert.That(session.TurnCount, Is.EqualTo(2));

            PartyMemberState hero = session.Party.Members[0];
            hero.Conditions.RestoreEntry(CharConditionId.Poisoned, 2, 5, 25, true, 10, 3);

            InventoryItem eqWeapon = hero.GetEquipped(EquipmentSlot.Weapon);
            Assert.That(eqWeapon, Is.Not.Null, "Expected hero_0 to have an equipped weapon.");
            eqWeapon.RemainingCharges = 2;

            ItemDefinition chargedStashDef = null;
            foreach (ItemDefinition def in bundle.Items.Values)
            {
                if (def.Charges > 2)
                {
                    chargedStashDef = def;
                    break;
                }
            }
            Assert.That(chargedStashDef, Is.Not.Null, "Expected at least one item with Charges > 2 in bundle.");
            int reducedStashCharges = chargedStashDef.Charges - 2;
            session.Party.AddItemToStash(chargedStashDef.Id, 1, isIdentified: true, charges: reducedStashCharges);

            int levelsGained = hero.AddXp(500);
            Assert.That(levelsGained, Is.GreaterThanOrEqualTo(1));
            int expectedXp = hero.Xp;
            int expectedLevel = hero.Level;

            const string clearedKey = "land_0:zone:0";
            session.MarkEncounterCleared(clearedKey);

            ulong expectedSeed = roller.State;
            int expectedNextRoll = new DeterministicDiceRoller(expectedSeed).NextInt(1, 10000);

            string tempPath = Path.Combine(Path.GetTempPath(), "realmz_wave_b_atomic_save_test.json");
            try
            {
                BywaterSaveData snapshot = BywaterSaveService.CaptureSnapshot(session, new[] { "[R1] Test combat log line" });
                Assert.That(BywaterSaveService.TryWriteSave(tempPath, snapshot), Is.True);
                Assert.That(File.Exists(tempPath + ".tmp"), Is.False, "No lingering .tmp file should remain after atomic save.");

                Assert.That(BywaterSaveService.TryLoadSave(tempPath, out BywaterSaveData loadedData, out string status), Is.True, status);

                PartyState freshParty = PartyState.FromBundle(bundle);
                ExplorationSession loadedSession = new ExplorationSession(
                    bundle.Maps,
                    loadedData.mapId,
                    freshParty,
                    new DeterministicDiceRoller(1UL),
                    new GridCoord(loadedData.x, loadedData.y),
                    loadedData.facing);
                BywaterSaveService.ApplySnapshot(loadedData, loadedSession, bundle.Items, out ulong restoredSeed);

                PartyMemberState loadedHero = loadedSession.Party.Members[0];
                Assert.That(loadedHero.Conditions.Has(CharConditionId.Poisoned), Is.True, "Poisoned condition must survive F5->F9.");
                Assert.That(loadedHero.Conditions.GetElapsedRounds(CharConditionId.Poisoned), Is.EqualTo(3));

                InventoryItem loadedEqWeapon = loadedHero.GetEquipped(EquipmentSlot.Weapon);
                Assert.That(loadedEqWeapon, Is.Not.Null);
                Assert.That(loadedEqWeapon.RemainingCharges, Is.EqualTo(2), "Equipped charges must not refill on load.");

                InventoryItem loadedStashItem = null;
                for (int i = 0; i < loadedSession.Party.Stash.Count; i++)
                {
                    if (loadedSession.Party.Stash[i].ItemId == chargedStashDef.Id)
                    {
                        loadedStashItem = loadedSession.Party.Stash[i];
                        break;
                    }
                }
                Assert.That(loadedStashItem, Is.Not.Null, "Charged stash item missing after load.");
                Assert.That(loadedStashItem.RemainingCharges, Is.EqualTo(reducedStashCharges), "Stash charges must not refill on load.");

                Assert.That(loadedHero.Xp, Is.EqualTo(expectedXp));
                Assert.That(loadedHero.Level, Is.EqualTo(expectedLevel));
                Assert.That(loadedSession.TurnCount, Is.EqualTo(session.TurnCount));
                Assert.That(loadedSession.IsEncounterCleared(clearedKey), Is.True);
                Assert.That(restoredSeed, Is.EqualTo(expectedSeed));
                Assert.That(new DeterministicDiceRoller(restoredSeed).NextInt(1, 10000), Is.EqualTo(expectedNextRoll));

                // Corrupt JSON load test
                File.WriteAllText(tempPath, "{ corrupt json !!");
                LogAssert.Expect(LogType.Warning, new Regex("BywaterSaveService"));
                Assert.That(BywaterSaveService.TryLoadSave(tempPath, out _, out _), Is.False);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (File.Exists(tempPath + ".tmp")) File.Delete(tempPath + ".tmp");
                if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
            }
        }

        [Test]
        public void AudioDirector_ManifestLoadsAll11MusicSuitesAnd145PlusSfx()
        {
            GameObject host = new GameObject("TempAudioDirectorTest");
            try
            {
                BywaterAudioDirector director = host.AddComponent<BywaterAudioDirector>();
                Assert.That(director.HasLoadedManifest, Is.True, "Audio manifest should load from Resources.");
                Assert.That(director.MusicTrackCount, Is.EqualTo(11), "Expected 11 music suites in audio manifest.");
                Assert.That(director.SfxCount, Is.GreaterThanOrEqualTo(145), "Expected >= 145 SFX entries in audio manifest.");

                Assert.That(director.TryGetSfxResourcePath(82, out string roadSfx), Is.True, "Walk road SFX 82 missing.");
                Assert.That(roadSfx, Is.Not.Empty);
                Assert.That(director.TryGetSfxResourcePath(83, out string caveSfx), Is.True, "Walk cave SFX 83 missing.");
                Assert.That(caveSfx, Is.Not.Empty);

                Assert.That(director.TryGetMusicResourcePath("outdoor", out _), Is.True, "Music suite 'outdoor' missing.");
                Assert.That(director.TryGetMusicResourcePath("dungeon", out _), Is.True, "Music suite 'dungeon' missing.");
                Assert.That(director.TryGetMusicResourcePath("battle", out _), Is.True, "Music suite 'battle' missing.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void HudController_PoolsRowsWithoutChurnAndExposesShoveAndVictoryRewards()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            PartyState party = PartyState.FromBundle(bundle);
            int added = 0;
            foreach (ItemDefinition item in bundle.Items.Values)
            {
                party.AddItemToStash(item.Id, 1, true, item.Charges);
                added++;
                if (added >= 6)
                {
                    break;
                }
            }

            PartyMemberState magePreset = party.Members.Count > 3 ? party.Members[3] : party.Members[0];
            CombatantState hero = new CombatantState(
                magePreset.Id,
                magePreset.Name,
                CombatSide.Hero,
                new GridCoord(1, 1),
                hp: 9999,
                maxHp: 9999,
                sp: 99,
                maxSp: 99,
                spellIds: magePreset.KnownSpellIds);
            CombatantState monster = CombatantState.FromMonster(bundle.Monsters[0], "mon_0_test", new GridCoord(4, 4), new DeterministicDiceRoller(77UL));

            CombatGrid grid = new CombatGrid(6, 6);
            TacticalCombatSession combat = new TacticalCombatSession(
                grid,
                new[] { hero, monster },
                bundle.Spells,
                bundle.Items,
                new DeterministicDiceRoller(77UL),
                RulesetMode.ModernBalanced,
                party);

            int safety = 0;
            while (combat.Log.Count < 45 && combat.Outcome == CombatOutcome.InProgress && safety < 120)
            {
                safety++;
                if (combat.CurrentActor != null && combat.CurrentActor.Side == CombatSide.Hero)
                {
                    combat.GuardOverwatch();
                }
                combat.EndTurn();
            }
            Assert.That(combat.Log.Count, Is.GreaterThanOrEqualTo(45), "Expected >= 45 combat log lines to test ring-buffer pooling.");

            int spellId = hero.SpellIds.Count > 0 ? hero.SpellIds[0] : -1;
            bool hadEventSystem = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject tempGo = new GameObject("TempHudPoolingTest");

            try
            {
                BywaterHudController hud = tempGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas canvas = tempGo.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);

                hud.RefreshPartyDrawer(party, bundle, 0);
                hud.RefreshShopModal(party, bundle);
                hud.RefreshCombatHud(combat, bundle, CombatActionMode.Spell, spellId, "1x");

                Assert.That(hud.PooledLogRowCount, Is.EqualTo(40), "Expected PooledLogRowCount == 40.");

                Transform stashContent = FindDescendantByPathSuffix(canvas.transform, "StashScroll/Viewport/Content");
                Transform shopBuyContent = FindDescendantByPathSuffix(canvas.transform, "ShopBuyScroll/Viewport/Content");
                Transform shopSellContent = FindDescendantByPathSuffix(canvas.transform, "ShopSellScroll/Viewport/Content");
                Transform logContent = FindDescendantByPathSuffix(canvas.transform, "CombatLogScroll/Viewport/Content");
                Transform spellBar = FindDescendantByName(canvas.transform, "SpellPickerBar");

                int stashChildrenBefore = stashContent.childCount;
                int buyChildrenBefore = shopBuyContent.childCount;
                int sellChildrenBefore = shopSellContent.childCount;
                int logChildrenBefore = logContent.childCount;
                int spellChildrenBefore = spellBar.childCount;
                int pooledStashBefore = hud.PooledStashRowCount;
                int pooledShopBefore = hud.PooledShopRowCount;
                int pooledSpellBefore = hud.PooledSpellRowCount;

                // Second refresh must reuse pooled rows with zero GameObject churn
                hud.RefreshPartyDrawer(party, bundle, 0);
                hud.RefreshShopModal(party, bundle);
                hud.RefreshCombatHud(combat, bundle, CombatActionMode.Spell, spellId, "1x");

                Assert.That(stashContent.childCount, Is.EqualTo(stashChildrenBefore));
                Assert.That(shopBuyContent.childCount, Is.EqualTo(buyChildrenBefore));
                Assert.That(shopSellContent.childCount, Is.EqualTo(sellChildrenBefore));
                Assert.That(logContent.childCount, Is.EqualTo(logChildrenBefore));
                Assert.That(spellBar.childCount, Is.EqualTo(spellChildrenBefore));
                Assert.That(hud.PooledStashRowCount, Is.EqualTo(pooledStashBefore));
                Assert.That(hud.PooledShopRowCount, Is.EqualTo(pooledShopBefore));
                Assert.That(hud.PooledSpellRowCount, Is.EqualTo(pooledSpellBefore));
                Assert.That(hud.PooledLogRowCount, Is.EqualTo(40));

                Transform shoveBtn = FindDescendantByName(canvas.transform, "ShoveBtn");
                Assert.That(shoveBtn, Is.Not.Null, "ShoveBtn missing from Combat Action Bar.");

                hud.SetCombatHoverPreview("Melee vs Goblin", "Hit%: 85%", "[HIGH GROUND +15% / +1 RNG]");
                Transform badgeRoot = FindDescendantByName(canvas.transform, "TooltipBadge");
                Transform badgeTextTf = FindDescendantByName(canvas.transform, "TooltipBadgeText");
                Assert.That(badgeRoot, Is.Not.Null);
                Assert.That(badgeRoot.gameObject.activeSelf, Is.True, "High Ground badge should be visible.");
                Assert.That(badgeTextTf.GetComponent<TextMeshProUGUI>().text, Does.Contain("HIGH GROUND"));

                hud.ShowOutcomeModal(CombatOutcome.Victory, 350, 2, 120, new[] { "Iron Longsword", "Healing Potion" });
                Transform lvlBanner = FindDescendantByName(canvas.transform, "OutcomeLevelUpBanner");
                Transform lvlTextTf = FindDescendantByName(canvas.transform, "OutcomeLevelUpText");
                Transform summaryTf = FindDescendantByName(canvas.transform, "OutcomeSummary");
                Assert.That(lvlBanner.gameObject.activeSelf, Is.True, "Level Up banner should show when VictoryLevelsGained > 0.");
                Assert.That(lvlTextTf.GetComponent<TextMeshProUGUI>().text, Does.Contain("2"));
                Assert.That(summaryTf.GetComponent<TextMeshProUGUI>().text, Does.Contain("Iron Longsword"));
                Assert.That(summaryTf.GetComponent<TextMeshProUGUI>().text, Does.Contain("Healing Potion"));
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

        [Test]
        public void CombatAndWorldOverlays_IntegrateHighGroundShoveSurfacesAndEnemyIntent()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            GameObject overlaysGo = new GameObject("TempGridOverlaysTest");
            GameObject camGo = new GameObject("TempCameraRigTest", typeof(Camera));
            try
            {
                BywaterCameraRig camRig = camGo.AddComponent<BywaterCameraRig>();
                Assert.That(camRig, Is.Not.Null);

                Vector3 expectedWorld = new Vector3(5.5f, 0.55f, -5.5f);
                Assert.That(BywaterCameraRig.GridToWorld(new GridCoord(5, 5), 0.55f), Is.EqualTo(expectedWorld));

                BywaterGridOverlays overlays = overlaysGo.AddComponent<BywaterGridOverlays>();
                overlays.Initialize(catalog);
                overlays.SetElevationResolver((x, y) => (x == 5 && y == 5) ? 0.55f : 0f);

                overlays.SetExplorationHoverPath(new[] { new GridCoord(5, 5) }, new GridCoord(5, 5), reachable: true);
                Transform exploreLayer = FindDescendantByName(overlays.transform, "ExplorationHoverPath");
                Assert.That(exploreLayer, Is.Not.Null);
                bool foundElevatedQuad = false;
                MeshRenderer[] hoverQuads = exploreLayer.GetComponentsInChildren<MeshRenderer>(false);
                for (int i = 0; i < hoverQuads.Length; i++)
                {
                    if (hoverQuads[i].gameObject.activeSelf && hoverQuads[i].transform.position.y >= 0.55f + 0.02f)
                    {
                        foundElevatedQuad = true;
                        break;
                    }
                }
                Assert.That(foundElevatedQuad, Is.True, "Expected elevated overlay quad above 0.55f + 0.02f.");

                overlays.SetSurfaceCells(new[]
                {
                    new CombatGridCell(5, 5, surface: SurfaceType.Fire, surfaceRoundsRemaining: 3),
                    new CombatGridCell(6, 5, surface: SurfaceType.Water, surfaceRoundsRemaining: -1)
                });
                Transform surfaceLayer = FindDescendantByName(overlays.transform, "ElementalSurfaces");
                Assert.That(surfaceLayer, Is.Not.Null);
                Assert.That(surfaceLayer.GetComponentsInChildren<MeshRenderer>(false).Length, Is.GreaterThanOrEqualTo(2));

                overlays.ShowEnemyIntentArcs(new[]
                {
                    new EnemyIntentArcData { From = new GridCoord(7, 5), To = new GridCoord(5, 5), IsSpell = false }
                });
                LineRenderer[] intentLines = overlays.GetComponentsInChildren<LineRenderer>(false);
                Assert.That(intentLines.Length, Is.GreaterThanOrEqualTo(1));
                Assert.That(intentLines[0].positionCount, Is.GreaterThan(0));

                CombatGrid grid = new CombatGrid(10, 10);
                grid.SetElevation(5, 5, 1);
                grid.SetElevation(6, 5, 0);
                grid.SetSurface(6, 5, SurfaceType.Water, -1);

                PartyState party = PartyState.FromBundle(bundle);
                CombatantState hero = CombatantState.FromHero(party.Members[0], new GridCoord(5, 5), bundle.Items);
                CombatantState monster = CombatantState.FromMonster(bundle.Monsters[0], "mon_0_overlay", new GridCoord(6, 5), new DeterministicDiceRoller(99UL));

                TacticalCombatSession session = new TacticalCombatSession(
                    grid,
                    new[] { hero, monster },
                    bundle.Spells,
                    bundle.Items,
                    new DeterministicDiceRoller(99UL),
                    RulesetMode.ModernBalanced,
                    party);

                TacticalAttackPreview melee = session.PreviewMeleeAttack(hero.Id, monster.Id);
                Assert.That(melee.ElevationModifierPct, Is.GreaterThan(0), "Expected High Ground ElevationModifierPct > 0.");

                TacticalShovePreview shove = session.PreviewShove(hero.Id, monster.Id);
                Assert.That(shove.CanExecute, Is.True);
                Assert.That(shove.Destination, Is.EqualTo(new GridCoord(7, 5)));

                EnemyIntentPrediction intent = session.PredictNextIntent(monster.Id);
                Assert.That(intent, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(overlaysGo);
                Object.DestroyImmediate(camGo);
            }
        }

        [Test]
        public void CombatHud_HidesExplorationAndRightSidebar_AndUsesContextualHotbarAndPips()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession explore = new ExplorationSession(bundle.Maps, "land_0", party, new DeterministicDiceRoller(42UL));

            PartyMemberState aldricPreset = party.Members[0];
            PartyMemberState theronPreset = party.Members[3];
            PartyMemberState vexPreset = party.Members[5];
            Assert.That(aldricPreset.MaxSp, Is.EqualTo(0));
            Assert.That(aldricPreset.KnownSpellIds.Count, Is.EqualTo(0));
            Assert.That(vexPreset.MaxSp, Is.EqualTo(0));
            Assert.That(vexPreset.KnownSpellIds.Count, Is.EqualTo(0));
            Assert.That(theronPreset.MaxSp, Is.GreaterThan(0));
            Assert.That(theronPreset.KnownSpellIds.Count, Is.GreaterThan(0));

            CombatGrid grid = new CombatGrid(8, 8);
            CombatantState vex = CombatantState.FromHero(vexPreset, new GridCoord(1, 2), bundle.Items);
            CombatantState aldric = CombatantState.FromHero(aldricPreset, new GridCoord(2, 2), bundle.Items);
            CombatantState theron = CombatantState.FromHero(theronPreset, new GridCoord(1, 4), bundle.Items);
            CombatantState monster = CombatantState.FromMonster(bundle.Monsters[0], "mon_0_hud", new GridCoord(6, 6), new DeterministicDiceRoller(101UL));

            TacticalCombatSession combat = new TacticalCombatSession(
                grid,
                new[] { vex, aldric, theron, monster },
                bundle.Spells,
                bundle.Items,
                new DeterministicDiceRoller(101UL),
                RulesetMode.ModernBalanced,
                party);

            bool hadEventSystem = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject tempGo = new GameObject("TempTrack2CombatHudTest");

            try
            {
                BywaterHudController hud = tempGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas canvas = tempGo.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);

                // Verify RefreshExploreHud hides SkirmishBtn and WarpBtn until SetDevToolsVisible(true)
                hud.RefreshExploreHud(explore, bundle, null, null, 0, null, isReachable: true);
                Transform skirmishBtn = FindDescendantByName(canvas.transform, "SkirmishBtn");
                Transform warpBtn = FindDescendantByName(canvas.transform, "WarpBtn");
                Assert.That(skirmishBtn, Is.Not.Null);
                Assert.That(warpBtn, Is.Not.Null);
                Assert.That(skirmishBtn.gameObject.activeSelf, Is.False, "SkirmishBtn should be hidden by default.");
                Assert.That(warpBtn.gameObject.activeSelf, Is.False, "WarpBtn should be hidden by default.");

                hud.SetDevToolsVisible(true);
                Assert.That(skirmishBtn.gameObject.activeSelf, Is.True, "SkirmishBtn should show after SetDevToolsVisible(true).");
                Assert.That(warpBtn.gameObject.activeSelf, Is.True, "WarpBtn should show after SetDevToolsVisible(true).");

                // 1. Rogue / Knight without spells (Vex / Aldric)
                AdvanceCombatTurnToActor(combat, vex.Id);
                Assert.That(combat.CurrentActor, Is.Not.Null);
                Assert.That(combat.CurrentActor.Id, Is.EqualTo(vex.Id));
                Assert.That(combat.CurrentActor.MaxSp, Is.EqualTo(0));
                Assert.That(combat.CurrentActor.SpellIds.Count, Is.EqualTo(0));
                Assert.That(combat.CanUndoMove, Is.False);

                hud.RefreshCombatHud(combat, bundle, CombatActionMode.Smart, -1, "1x");

                Assert.That(hud.AreExploreButtonsHiddenInCombat, Is.True);
                string[] hiddenExploreNames =
                {
                    "SkirmishBtn", "PartyBtn", "ShopBtn", "CampBtn", "ShrineBtn",
                    "WarpBtn", "ContextPoiBtn", "SaveBtn", "LoadBtn", "GoldText", "TorchText"
                };
                for (int i = 0; i < hiddenExploreNames.Length; i++)
                {
                    Transform tf = FindDescendantByName(canvas.transform, hiddenExploreNames[i]);
                    Assert.That(tf, Is.Not.Null, hiddenExploreNames[i] + " missing from HUD.");
                    Assert.That(tf.gameObject.activeSelf, Is.False, hiddenExploreNames[i] + " should be inactive in combat.");
                }

                Assert.That(hud.IsRightSidebarRemoved, Is.True);
                Assert.That(FindDescendantByName(canvas.transform, "CombatTooltipCard"), Is.Null);
                Assert.That(FindDescendantByName(canvas.transform, "CombatLogPanel"), Is.Null);
                Assert.That(FindDescendantByName(canvas.transform, "MoveBtn"), Is.Null);

                Transform undoMoveBtn = FindDescendantByName(canvas.transform, "UndoMoveBtn");
                Assert.That(undoMoveBtn, Is.Not.Null);
                Assert.That(undoMoveBtn.gameObject.activeSelf, Is.False, "UndoMoveBtn should be inactive when !combat.CanUndoMove.");

                IReadOnlyList<string> nonCasterActions = hud.GetVisibleHotbarActionIds();
                for (int i = 0; i < nonCasterActions.Count; i++)
                {
                    Assert.That(nonCasterActions[i], Does.Not.StartWith("spell:"), "Non-caster hotbar should not expose spell slots.");
                }
                Assert.That(FindDescendantByName(canvas.transform, "SpellBtn"), Is.Null);
                Transform actionBar = FindDescendantByName(canvas.transform, "TacticalActionBar");
                Assert.That(actionBar, Is.Not.Null);
                UnityEngine.UI.Button[] actionButtons = actionBar.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                for (int i = 0; i < actionButtons.Length; i++)
                {
                    TextMeshProUGUI btnLbl = actionButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
                    if (btnLbl != null)
                    {
                        Assert.That(btnLbl.text, Is.Not.EqualTo("Spell"), "Legacy Spell submenu button should not exist.");
                    }
                }

                Assert.That(FindDescendantByName(canvas.transform, "ApDiamondsRow"), Is.Not.Null);
                Assert.That(FindDescendantByName(canvas.transform, "ApValueLabel"), Is.Not.Null);
                Assert.That(FindDescendantByName(canvas.transform, "ApDiamond_0"), Is.Not.Null);
                Assert.That(FindDescendantByName(canvas.transform, "ApDiamond_5"), Is.Not.Null);
                Assert.That(FindDescendantByName(canvas.transform, "CombatPartyStrip"), Is.Not.Null);
                Assert.That(FindDescendantByName(canvas.transform, "CombatTickerPanel"), Is.Not.Null);

                // Verify Knight (Aldric) also hides spell slots
                AdvanceCombatTurnToActor(combat, aldric.Id);
                Assert.That(combat.CurrentActor.Id, Is.EqualTo(aldric.Id));
                hud.RefreshCombatHud(combat, bundle, CombatActionMode.Smart, -1, "1x");
                IReadOnlyList<string> knightActions = hud.GetVisibleHotbarActionIds();
                for (int i = 0; i < knightActions.Count; i++)
                {
                    Assert.That(knightActions[i], Does.Not.StartWith("spell:"));
                }

                TextMeshProUGUI[] allLabels = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                Assert.That(allLabels.Length, Is.GreaterThan(0));
                for (int i = 0; i < allLabels.Length; i++)
                {
                    Assert.That(allLabels[i].fontSize, Is.GreaterThanOrEqualTo(15f), $"Label '{allLabels[i].gameObject.name}' has fontSize < 15f.");
                }

                // 2. Mage (Theron): spell items directly on main hotbar + PooledLogRowCount == 40 + zero churn
                AdvanceCombatTurnToActor(combat, theron.Id);
                Assert.That(combat.CurrentActor.Id, Is.EqualTo(theron.Id));
                int firstSpellId = theron.SpellIds[0];
                hud.RefreshCombatHud(combat, bundle, CombatActionMode.Spell, firstSpellId, "1x");

                IReadOnlyList<string> mageActions = hud.GetVisibleHotbarActionIds();
                bool hasSpellSlot = false;
                for (int i = 0; i < mageActions.Count; i++)
                {
                    if (mageActions[i].StartsWith("spell:"))
                    {
                        hasSpellSlot = true;
                        break;
                    }
                }
                Assert.That(hasSpellSlot, Is.True, "Mage hotbar should expose spell: items directly.");
                Assert.That(hud.PooledLogRowCount, Is.EqualTo(40));

                Transform logContent = FindDescendantByPathSuffix(canvas.transform, "CombatLogScroll/Viewport/Content");
                Transform partyStrip = FindDescendantByName(canvas.transform, "CombatPartyStrip");
                int logChildrenBefore = logContent.childCount;
                int barChildrenBefore = actionBar.childCount;
                int stripChildrenBefore = partyStrip.childCount;

                hud.RefreshCombatHud(combat, bundle, CombatActionMode.Spell, firstSpellId, "1x");
                Assert.That(hud.PooledLogRowCount, Is.EqualTo(40));
                Assert.That(logContent.childCount, Is.EqualTo(logChildrenBefore));
                Assert.That(actionBar.childCount, Is.EqualTo(barChildrenBefore));
                Assert.That(partyStrip.childCount, Is.EqualTo(stripChildrenBefore));
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

        [Test]
        public void CombatUx_HoverOnlyIntentArcsDefaultFalse_SettingsAndAutoCombatToggle()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            bool hadEventSystem = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject gameControllerGo = new GameObject("TempGameControllerIntentTest");
            GameObject overlaysGo = new GameObject("TempGridOverlaysIntentTest");
            GameObject hudGo = new GameObject("TempHudSettingsTest");

            try
            {
                BywaterGameController controller = gameControllerGo.AddComponent<BywaterGameController>();
                Assert.That(controller.ShowAllEnemyIntentArcs, Is.False, "ShowAllEnemyIntentArcs should default to false (hover-only).");

                BywaterGridOverlays overlays = overlaysGo.AddComponent<BywaterGridOverlays>();
                overlays.Initialize(catalog);
                overlays.ClearAll();

                Transform intentLayer = FindDescendantByName(overlays.transform, "EnemyIntentArcs");
                Assert.That(intentLayer, Is.Not.Null);
                Assert.That(intentLayer.GetComponentsInChildren<LineRenderer>(false).Length, Is.EqualTo(0), "No active LineRenderers expected before hover.");

                overlays.ShowEnemyIntentArcs(new[]
                {
                    new EnemyIntentArcData { From = new GridCoord(7, 5), To = new GridCoord(5, 5), IsSpell = false }
                });
                LineRenderer[] activeArcs = intentLayer.GetComponentsInChildren<LineRenderer>(false);
                Assert.That(activeArcs.Length, Is.EqualTo(1));
                Assert.That(activeArcs[0].positionCount, Is.GreaterThan(0));

                overlays.ClearAll();
                Assert.That(intentLayer.GetComponentsInChildren<LineRenderer>(false).Length, Is.EqualTo(0), "ClearAll should hide intent LineRenderers.");

                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas canvas = hudGo.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);

                Assert.That(hud.IsSettingsOpen, Is.False);
                hud.ToggleSettingsModal(true);
                Assert.That(hud.IsSettingsOpen, Is.True);
                hud.CloseSettingsModal();
                Assert.That(hud.IsSettingsOpen, Is.False);

                hud.ToggleSettingsModal(true);
                Assert.That(hud.IsSettingsOpen, Is.True);

                bool? autoCombatState = null;
                bool? autoEndTurnState = null;
                float? gridOpacityValue = null;
                bool? nameplatesAlwaysOnState = null;

                hud.OnAutoCombatToggled += v => autoCombatState = v;
                hud.OnAutoEndTurnToggled += v => autoEndTurnState = v;
                hud.OnGridOpacityChanged += v => gridOpacityValue = v;
                hud.OnNameplatesAlwaysOnChanged += v => nameplatesAlwaysOnState = v;

                Transform autoCombatBtnTf = FindDescendantByPathSuffix(canvas.transform, "SettingsModal/AutoCombatRow/ToggleBtn");
                Transform autoEndTurnBtnTf = FindDescendantByPathSuffix(canvas.transform, "SettingsModal/AutoEndTurnRow/ToggleBtn");
                Transform gridOpacitySliderTf = FindDescendantByPathSuffix(canvas.transform, "SettingsModal/GridOpacityRow/Slider");
                Transform nameplatesBtnTf = FindDescendantByPathSuffix(canvas.transform, "SettingsModal/NameplatesRow/ToggleBtn");

                Assert.That(autoCombatBtnTf, Is.Not.Null);
                Assert.That(autoEndTurnBtnTf, Is.Not.Null);
                Assert.That(gridOpacitySliderTf, Is.Not.Null);
                Assert.That(nameplatesBtnTf, Is.Not.Null);

                autoCombatBtnTf.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.That(autoCombatState, Is.EqualTo(true));
                Assert.That(hud.IsAutoCombatVisualOn, Is.True);

                autoEndTurnBtnTf.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.That(autoEndTurnState, Is.EqualTo(true));

                gridOpacitySliderTf.GetComponent<UnityEngine.UI.Slider>().value = 0.35f;
                Assert.That(gridOpacityValue.HasValue, Is.True);
                Assert.That(gridOpacityValue.Value, Is.EqualTo(0.35f).Within(0.001f));

                nameplatesBtnTf.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.That(nameplatesAlwaysOnState, Is.EqualTo(true));

                Assert.That(hud.IsQuickResolveOpen, Is.False);
                hud.ShowQuickResolveModal("Goblin Ambush", "Goblins ahead.");
                Assert.That(hud.IsQuickResolveOpen, Is.True);
                hud.HideQuickResolveModal();
                Assert.That(hud.IsQuickResolveOpen, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameControllerGo);
                Object.DestroyImmediate(overlaysGo);
                Object.DestroyImmediate(hudGo);
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

        [Test]
        public void CombatCameraRig_AutoFramesAllCombatantsIncludingEdgeSpawnsWithinSafeViewport()
        {
            GameObject camGo = new GameObject("TempCombatFramingCamera", typeof(Camera));
            try
            {
                Camera cam = camGo.GetComponent<Camera>();
                cam.aspect = 16f / 9f;
                cam.fieldOfView = 34f;

                BywaterCameraRig rig = camGo.AddComponent<BywaterCameraRig>();

                Vector3[] combatantPositions =
                {
                    new Vector3(0.5f, 0f, -1.5f),   // Far-left edge Vex
                    new Vector3(1.5f, 0f, -9.5f),   // Far-bottom-left hero
                    new Vector3(2.5f, 0f, -3.5f),
                    new Vector3(3.5f, 0f, -5.5f),
                    new Vector3(4.5f, 0f, -2.5f),
                    new Vector3(5.5f, 0f, -7.5f),
                    new Vector3(7.5f, 0f, -4.5f),
                    new Vector3(9.5f, 0f, -6.5f),
                    new Vector3(11.5f, 0f, -2.5f),  // Far-right edge Orc Warrior
                    new Vector3(11.5f, 0f, -8.5f)   // Far-right edge Orc Archer
                };

                rig.FrameCombatantsInSafeViewport(combatantPositions, immediate: true);

                Rect safe = BywaterCameraRig.DefaultSafeViewportRect;
                Assert.That(safe.xMin, Is.EqualTo(0.11f).Within(0.001f));
                Assert.That(safe.xMax, Is.EqualTo(0.96f).Within(0.001f));
                Assert.That(safe.yMin, Is.EqualTo(0.16f).Within(0.001f));
                Assert.That(safe.yMax, Is.EqualTo(0.85f).Within(0.001f));

                for (int i = 0; i < combatantPositions.Length; i++)
                {
                    Vector3 foot = combatantPositions[i];
                    Vector3 head = foot + Vector3.up * 1.55f;

                    Vector3 footVp = cam.WorldToViewportPoint(foot);
                    Vector3 headVp = cam.WorldToViewportPoint(head);

                    Assert.That(footVp.z, Is.GreaterThan(0f), $"Foot[{i}] behind camera.");
                    Assert.That(footVp.x, Is.InRange(safe.xMin, safe.xMax), $"Foot[{i}] x={footVp.x} outside safe viewport.");
                    Assert.That(footVp.y, Is.InRange(safe.yMin, safe.yMax), $"Foot[{i}] y={footVp.y} outside safe viewport.");

                    Assert.That(headVp.z, Is.GreaterThan(0f), $"Head[{i}] behind camera.");
                    Assert.That(headVp.x, Is.InRange(safe.xMin, safe.xMax), $"Head[{i}] x={headVp.x} outside safe viewport.");
                    Assert.That(headVp.y, Is.InRange(safe.yMin, safe.yMax), $"Head[{i}] y={headVp.y} outside safe viewport.");
                }
            }
            finally
            {
                Object.DestroyImmediate(camGo);
            }
        }

        [Test]
        public void UnitViewsAndWorldRenderer_SupportMicroHpBarsHoverNameplatesAndTexturedPortal()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            GameObject unitGo1 = new GameObject("TempUnitView1");
            GameObject unitGo2 = new GameObject("TempUnitView2");
            GameObject camGo = new GameObject("TempUnitViewCamera", typeof(Camera));
            GameObject worldGo = new GameObject("TempWorldRendererPortalTest");

            try
            {
                Camera cam = camGo.GetComponent<Camera>();
                cam.aspect = 16f / 9f;
                cam.fieldOfView = 34f;
                cam.transform.position = new Vector3(5.5f, 8.5f, -12.5f);
                cam.transform.rotation = Quaternion.Euler(48f, 0f, 0f);

                BywaterUnitView view1 = unitGo1.AddComponent<BywaterUnitView>();
                view1.transform.position = new Vector3(5.5f, 0f, -5.5f);
                view1.Configure("hero_rogue", "Vex", null, Color.cyan, catalog.uiFont, isAllyOverride: true);

                Transform microHud = FindDescendantByName(view1.transform, "MicroHud");
                Transform contactShadow = FindDescendantByName(view1.transform, "ContactShadow");
                Transform underRing = FindDescendantByName(view1.transform, "UnderRing");
                Transform turnChevron = FindDescendantByName(view1.transform, "TurnChevron");

                Assert.That(microHud, Is.Not.Null);
                Assert.That(microHud.gameObject.activeSelf, Is.True);
                Assert.That(view1.IsNameplateVisible, Is.False);
                Assert.That(contactShadow, Is.Not.Null);
                Assert.That(contactShadow.gameObject.activeSelf, Is.True);
                Assert.That(underRing, Is.Not.Null);
                Assert.That(underRing.gameObject.activeSelf, Is.False);
                Assert.That(turnChevron, Is.Not.Null);
                Assert.That(turnChevron.gameObject.activeSelf, Is.False);

                view1.SetTurnHighlight(true);
                Assert.That(underRing.gameObject.activeSelf, Is.True);
                Assert.That(turnChevron.gameObject.activeSelf, Is.True);

                view1.SetNameplateVisible(true, "HP 14/14 | AC 14");
                Assert.That(view1.IsNameplateVisible, Is.True);
                Transform worldHud1 = FindDescendantByName(view1.transform, "WorldHud");
                Transform detailText1 = FindDescendantByName(view1.transform, "DetailText");
                Assert.That(worldHud1, Is.Not.Null);
                Assert.That(worldHud1.gameObject.activeSelf, Is.True);
                Assert.That(detailText1, Is.Not.Null);
                Assert.That(detailText1.GetComponent<TextMeshProUGUI>().text, Is.EqualTo("HP 14/14 | AC 14"));

                BywaterUnitView view2 = unitGo2.AddComponent<BywaterUnitView>();
                view2.transform.position = new Vector3(5.6f, 0f, -5.5f);
                view2.Configure("hero_knight", "Aldric", null, Color.cyan, catalog.uiFont, isAllyOverride: true);
                view2.SetNameplateVisible(true, "HP 17/17 | AC 11");
                Transform worldHud2 = FindDescendantByName(view2.transform, "WorldHud");
                Assert.That(worldHud2, Is.Not.Null);

                BywaterUnitView.ResolveNameplateStagger(new[] { view1, view2 }, cam);
                float maxY = Mathf.Max(worldHud1.localPosition.y, worldHud2.localPosition.y);
                float minY = Mathf.Min(worldHud1.localPosition.y, worldHud2.localPosition.y);
                Assert.That(maxY - minY, Is.GreaterThan(0.02f), "ResolveNameplateStagger should vertically separate overlapping nameplates.");

                BywaterWorldRenderer renderer = worldGo.AddComponent<BywaterWorldRenderer>();
                renderer.Initialize(catalog);
                TileMapData dungeon = bundle.Maps["dungeon_0"];
                renderer.RenderMap(dungeon, new GridCoord(33, 72), forceRebuild: true);

                Transform runeFloor = FindDescendantByPathSuffix(renderer.transform, "PortalRune_0/RuneFloor");
                Assert.That(runeFloor, Is.Not.Null, "PortalRune_0/RuneFloor missing on dungeon_0.");
                MeshRenderer runeMr = runeFloor.GetComponent<MeshRenderer>();
                Assert.That(runeMr, Is.Not.Null);
                Assert.That(runeMr.sharedMaterial, Is.Not.Null);
                Assert.That(runeMr.sharedMaterial.mainTexture, Is.Not.Null, "PortalRune_0/RuneFloor must have a textured material.");
            }
            finally
            {
                Object.DestroyImmediate(unitGo1);
                Object.DestroyImmediate(unitGo2);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(worldGo);
            }
        }

        [Test]
        public void UnifiedRosterAndIcons_ResolveAll6Heroes7Enemies21IconsAndGoblinArcher()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null, "BywaterAssetCatalog missing.");

            string[] heroIds =
            {
                "hero_knight", "hero_cleric", "hero_ranger",
                "hero_mage", "hero_paladin", "hero_rogue"
            };
            for (int i = 0; i < heroIds.Length; i++)
            {
                string id = heroIds[i];
                AssertUnifiedUnitSheet(id, isHero: true);

                BywaterAssetCatalog.HeroArtEntry entry = catalog.GetHeroArt(id, 0, string.Empty);
                Assert.That(entry, Is.Not.Null, "Missing HeroArtEntry for " + id);
                Assert.That(entry.id, Is.EqualTo(id));
                Assert.That(entry.sprite, Is.Not.Null, "Hero sprite null: " + id);
                Assert.That(entry.sprite.rect.size, Is.EqualTo(new Vector2(128f, 128f)), "Hero sprite frame must be 128x128: " + id);
                Assert.That(entry.portrait, Is.Not.Null, "Hero portrait null: " + id);
                Assert.That(entry.unitSheetMaterial, Is.Not.Null, "Hero unitSheetMaterial null: " + id);

                Material resolved = catalog.ResolveCartographyUnitMaterial(id, null);
                Assert.That(resolved, Is.Not.Null, "Resolved unit material null for " + id);
                Assert.That(resolved.shader.name, Is.EqualTo("Realmz/CartographyBillboard"));
                Assert.That(resolved.GetFloat("_Cols"), Is.EqualTo(4f));
                Assert.That(resolved.GetFloat("_FrameCount"), Is.EqualTo(4f));
                Assert.That(resolved.GetFloat("_IsTwoBankAtlas"), Is.EqualTo(0f));
                Assert.That(resolved.GetTexture("_BaseMap").name, Is.EqualTo($"{id}_sheet"));
                Assert.That(resolved.GetTexture("_BumpMap"), Is.Not.Null, "Normal map missing on material for " + id);
            }

            string[] monsterIds =
            {
                "monster_giant_frog", "monster_goblin_raider", "monster_goblin_archer",
                "monster_orc_warrior", "monster_giant_rat", "monster_skeleton_warrior",
                "monster_bandit_cutthroat"
            };
            for (int i = 0; i < monsterIds.Length; i++)
            {
                string id = monsterIds[i];
                AssertUnifiedUnitSheet(id, isHero: false);

                BywaterAssetCatalog.MonsterArtEntry entry = catalog.GetMonsterArt(id, 0);
                Assert.That(entry, Is.Not.Null, "Missing MonsterArtEntry for " + id);
                Assert.That(entry.key, Is.EqualTo(id));
                Assert.That(entry.sprite, Is.Not.Null, "Monster sprite null: " + id);
                Assert.That(entry.sprite.rect.size, Is.EqualTo(new Vector2(128f, 128f)), "Monster sprite frame must be 128x128: " + id);
                Assert.That(entry.unitSheetMaterial, Is.Not.Null, "Monster unitSheetMaterial null: " + id);

                Material resolved = catalog.ResolveCartographyUnitMaterial(id, null);
                Assert.That(resolved, Is.Not.Null, "Resolved unit material null for " + id);
                Assert.That(resolved.shader.name, Is.EqualTo("Realmz/CartographyBillboard"));
                Assert.That(resolved.GetFloat("_Cols"), Is.EqualTo(4f));
                Assert.That(resolved.GetFloat("_FrameCount"), Is.EqualTo(4f));
                Assert.That(resolved.GetFloat("_IsTwoBankAtlas"), Is.EqualTo(0f));
                Assert.That(resolved.GetTexture("_BaseMap").name, Is.EqualTo($"{id}_sheet"));
                Assert.That(resolved.GetTexture("_BumpMap"), Is.Not.Null, "Normal map missing on material for " + id);
            }

            // Explicit Goblin Archer convergence guard (must NEVER resolve to legacy kobold_spearman lizard)
            BywaterAssetCatalog.MonsterArtEntry goblinArcher = catalog.GetMonsterArt("goblin_archer", 388);
            Assert.That(goblinArcher, Is.Not.Null);
            Assert.That(goblinArcher.key, Is.EqualTo("monster_goblin_archer"));
            Assert.That(goblinArcher.sprite, Is.Not.Null);
            Assert.That(goblinArcher.sprite.rect.size, Is.EqualTo(new Vector2(128f, 128f)));

            BywaterAssetCatalog.MonsterArtEntry combatGoblinArcher = catalog.GetMonsterArt("mon_2_goblin_archer", 388);
            Assert.That(combatGoblinArcher, Is.Not.Null);
            Assert.That(combatGoblinArcher.key, Is.EqualTo("monster_goblin_archer"));

            Material goblinArcherMat = catalog.ResolveCartographyUnitMaterial("goblin_archer", null);
            Assert.That(goblinArcherMat, Is.Not.Null);
            Assert.That(goblinArcherMat.GetTexture("_BaseMap").name, Is.EqualTo("monster_goblin_archer_sheet"));

            BywaterAssetCatalog.MonsterArtEntry orcWarrior = catalog.GetMonsterArt("mon_3_orc_warrior", 515);
            Assert.That(orcWarrior, Is.Not.Null);
            Assert.That(orcWarrior.key, Is.EqualTo("monster_orc_warrior"));

            // Verify all 21 32x32 pixel hotbar & utility icons
            string[] expectedIconKeys =
            {
                "sword", "bow", "dagger", "staff", "fireball", "flame_hands", "magic_darts",
                "heal", "buff", "daze", "shove", "potion", "guard", "undo", "end_turn",
                "auto_combat", "speed", "audio", "grid", "settings", "flee"
            };
            Assert.That(expectedIconKeys.Length, Is.EqualTo(21));

            for (int i = 0; i < expectedIconKeys.Length; i++)
            {
                string key = expectedIconKeys[i];
                string iconPath = $"Assets/Art/Cartography/Icons/icon_{key}.png";
                TextureImporter importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
                Assert.That(importer, Is.Not.Null, "Missing icon importer: " + iconPath);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
                Assert.That(importer.mipmapEnabled, Is.False);

                Sprite sprite = catalog.GetHotbarIcon(key);
                Assert.That(sprite, Is.Not.Null, "Catalog GetHotbarIcon null for " + key);
                Assert.That(sprite.texture.width, Is.EqualTo(32), "Icon width != 32 for " + key);
                Assert.That(sprite.texture.height, Is.EqualTo(32), "Icon height != 32 for " + key);
                Assert.That(sprite.texture.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(catalog.GetHotbarIcon("icon_" + key), Is.SameAs(sprite));
            }

            // Verify HUD binds real 32x32 pixel icons on both hotbar slots and utility/skill buttons with >= 15pt text
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            PartyState party = PartyState.FromBundle(bundle);
            CombatGrid grid = new CombatGrid(8, 8);
            CombatantState theron = CombatantState.FromHero(party.Members[3], new GridCoord(1, 3), bundle.Items);
            CombatantState goblin = CombatantState.FromMonster(bundle.Monsters[4], "mon_0_goblin_archer", new GridCoord(5, 3), new DeterministicDiceRoller(55UL));
            TacticalCombatSession combat = new TacticalCombatSession(
                grid,
                new[] { theron, goblin },
                bundle.Spells,
                bundle.Items,
                new DeterministicDiceRoller(55UL),
                RulesetMode.ModernBalanced,
                party);

            bool hadEventSystem = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject tempGo = new GameObject("TempConvergenceHudIconTest");
            try
            {
                BywaterHudController hud = tempGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                AdvanceCombatTurnToActor(combat, theron.Id);
                hud.RefreshCombatHud(combat, bundle, CombatActionMode.Spell, theron.SpellIds[0], "1x");

                Canvas canvas = tempGo.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);

                string[] iconButtons =
                {
                    "HotbarSlot_0", "HotbarSlot_1", "HotbarSlot_2", "HotbarSlot_3",
                    "UndoMoveBtn", "PotionBtn", "ShoveBtn", "GuardBtn", "EndTurnBtn",
                    "AutoCombatBtn", "SpeedBtn", "CombatAudioBtn", "CombatGridBtn", "CombatMenuBtn",
                    "SettingsFleeBtn"
                };
                for (int i = 0; i < iconButtons.Length; i++)
                {
                    Transform btnTf = FindDescendantByName(canvas.transform, iconButtons[i]);
                    Assert.That(btnTf, Is.Not.Null, "Missing HUD button: " + iconButtons[i]);
                    Transform iconTf = FindDescendantByName(btnTf, "Icon");
                    Assert.That(iconTf, Is.Not.Null, "Missing Icon child on " + iconButtons[i]);
                    UnityEngine.UI.Image img = iconTf.GetComponent<UnityEngine.UI.Image>();
                    Assert.That(img, Is.Not.Null);
                    Assert.That(img.preserveAspect, Is.True, "preserveAspect must be true on " + iconButtons[i]);
                    Assert.That(img.sprite, Is.Not.Null, "Sprite null on " + iconButtons[i]);
                    Assert.That(img.sprite.name, Does.Not.StartWith("proc_icon_"), "Procedural fallback leaked onto " + iconButtons[i]);
                    Assert.That(img.sprite.texture.width, Is.EqualTo(32));
                    Assert.That(img.sprite.texture.height, Is.EqualTo(32));
                }

                TextMeshProUGUI[] labels = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < labels.Length; i++)
                {
                    Assert.That(labels[i].fontSize, Is.GreaterThanOrEqualTo(15f), $"Label '{labels[i].gameObject.name}' below 15pt.");
                }
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

        [Test]
        public void Pack2UnifiedPortraits_All12HeroPortraitsAre128x128PointAndResolveViaCatalog()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null, "BywaterAssetCatalog missing.");
            Assert.That(catalog.heroes.Count, Is.EqualTo(6), "Preserve 6 core heroes.");
            Assert.That(catalog.unifiedPortraits.Count, Is.EqualTo(16), "Expected 16 unified bronze portraits (12 heroes + 4 town NPCs).");
            Assert.That(catalog.extraHeroes.Count, Is.GreaterThanOrEqualTo(6), "Expected >= 6 extraHeroes entries.");

            for (int i = 0; i < catalog.heroes.Count; i++)
            {
                Assert.That(catalog.heroes[i].portrait, Is.Not.Null, "Hero portrait missing on heroes[" + i + "].");
            }

            for (int i = 0; i < catalog.extraHeroes.Count; i++)
            {
                Assert.That(catalog.extraHeroes[i].portrait, Is.Not.Null, "Extra hero portrait missing on extraHeroes[" + i + "].");
            }

            string[] all12Archetypes =
            {
                "knight", "cleric", "ranger", "mage", "paladin", "rogue",
                "archer", "assassin", "barbarian", "druid", "enchanter", "monk"
            };

            for (int i = 0; i < all12Archetypes.Length; i++)
            {
                string key = all12Archetypes[i];
                string path = $"Assets/Art/Cartography/Portraits/portrait_{key}.png";
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, "Missing portrait importer: " + path);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), "Expected Point filter on " + path);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), "Expected Uncompressed on " + path);
                Assert.That(importer.mipmapEnabled, Is.False, "Expected mipmaps off on " + path);

                Sprite sprite = catalog.GetUnifiedPortrait(key);
                Assert.That(sprite, Is.Not.Null, "GetUnifiedPortrait returned null for " + key);
                Assert.That(sprite.texture, Is.Not.Null);
                Assert.That(sprite.texture.width, Is.EqualTo(128), "Portrait width != 128 for " + key);
                Assert.That(sprite.texture.height, Is.EqualTo(128), "Portrait height != 128 for " + key);
                Assert.That(sprite.texture.filterMode, Is.EqualTo(FilterMode.Point));
            }
        }

        [Test]
        public void Pack2MonsterArchetypes_All133MappingsResolveUnitSheetsAndZoomedPortraitCrops()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null, "BywaterAssetCatalog missing.");
            Assert.That(catalog.monsters.Count, Is.EqualTo(7), "Preserve 7 core monsters.");
            Assert.That(catalog.expandedMonsters.Count, Is.GreaterThanOrEqualTo(36), "Expected >= 36 expanded monster entries.");
            Assert.That(catalog.archetypeMappings.Count, Is.EqualTo(133), "Expected all 133 archetype mappings.");

            string mappingPath = Path.Combine(Application.streamingAssetsPath, "bywater_m1", "archetype_mapping.json");
            Assert.That(File.Exists(mappingPath), Is.True, "archetype_mapping.json missing.");
            string json = File.ReadAllText(mappingPath);
            MatchCollection matches = Regex.Matches(
                json,
                "\"name\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"icon_id\"\\s*:\\s*(-?\\d+)",
                RegexOptions.Singleline);
            Assert.That(matches.Count, Is.EqualTo(133), "Expected 133 monster records in archetype_mapping.json.");

            for (int i = 0; i < matches.Count; i++)
            {
                string monsterName = matches[i].Groups[1].Value;
                int iconId = int.Parse(matches[i].Groups[2].Value);

                bool resolved = catalog.TryResolveMonsterArchetype(monsterName, iconId, out string archetypeKey);
                Assert.That(resolved, Is.True, $"TryResolveMonsterArchetype failed for '{monsterName}' (iconId={iconId}).");
                Assert.That(archetypeKey, Is.Not.Null.And.Not.Empty);

                Material unitMat = catalog.ResolveCartographyUnitMaterial(archetypeKey, null);
                Assert.That(unitMat, Is.Not.Null, $"ResolveCartographyUnitMaterial null for '{archetypeKey}' ({monsterName}).");

                Sprite crop = catalog.GetMonsterPortrait(monsterName, iconId);
                Assert.That(crop, Is.Not.Null, $"GetMonsterPortrait null for '{monsterName}' (iconId={iconId}).");
                Assert.That(crop.rect.width, Is.GreaterThan(0f));
                Assert.That(crop.rect.height, Is.GreaterThan(0f));
                Assert.That(crop.rect.width, Is.LessThanOrEqualTo(128f));
                Assert.That(crop.rect.height, Is.LessThanOrEqualTo(128f));
                Assert.That(crop.rect.width < 128f || crop.rect.height < 128f, Is.True,
                    $"Expected zoomed portraitPixelRect crop smaller than 128x128 for '{monsterName}', got {crop.rect.size}.");
            }

            string[] spotlightNames =
            {
                "Kobold", "Bear", "Spider", "Wraith", "Ogre",
                "Troll", "Town Guard", "Minotaur", "Demon Lord", "Dragon Wyrm"
            };
            for (int i = 0; i < spotlightNames.Length; i++)
            {
                string name = spotlightNames[i];
                Assert.That(catalog.TryResolveMonsterArchetype(name, 0, out string arch), Is.True, "Spotlight resolve failed: " + name);
                Assert.That(catalog.ResolveCartographyUnitMaterial(arch, null), Is.Not.Null, "Spotlight material null: " + name);
                Assert.That(catalog.GetMonsterPortrait(name, 0), Is.Not.Null, "Spotlight portrait crop null: " + name);
            }
        }

        [Test]
        public void Pack5IconsAndHud_All58Items12SpellsAnd4ConditionBadgesResolveAt32x32Point()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null, "BywaterAssetCatalog missing.");
            Assert.That(catalog.itemIdIconMappings.Count, Is.EqualTo(58), "Expected 58 itemIdIconMappings.");
            Assert.That(catalog.spellIdIconMappings.Count, Is.EqualTo(12), "Expected 12 spellIdIconMappings.");

            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            Assert.That(bundle.Items.Count, Is.EqualTo(58));
            Assert.That(bundle.Spells.Count, Is.EqualTo(12));

            foreach (ItemDefinition item in bundle.Items.Values)
            {
                Sprite icon = catalog.GetItemIcon(item.Id, item.Name);
                Assert.That(icon, Is.Not.Null, $"GetItemIcon null for item {item.Id} ({item.Name}).");
                Assert.That(icon.texture.width, Is.EqualTo(32), $"Item icon width != 32 for {item.Name}.");
                Assert.That(icon.texture.height, Is.EqualTo(32), $"Item icon height != 32 for {item.Name}.");
                Assert.That(icon.texture.filterMode, Is.EqualTo(FilterMode.Point));
            }

            foreach (SpellDefinition spell in bundle.Spells.Values)
            {
                Sprite icon = catalog.GetSpellIcon(spell.Id, spell.Key);
                Assert.That(icon, Is.Not.Null, $"GetSpellIcon null for spell {spell.Id} ({spell.Key}).");
                Assert.That(icon.texture.width, Is.EqualTo(32), $"Spell icon width != 32 for {spell.Key}.");
                Assert.That(icon.texture.height, Is.EqualTo(32), $"Spell icon height != 32 for {spell.Key}.");
                Assert.That(icon.texture.filterMode, Is.EqualTo(FilterMode.Point));
            }

            string[] badgeKeys = { "poison", "burn", "stun", "bless" };
            for (int i = 0; i < badgeKeys.Length; i++)
            {
                Sprite badge = catalog.GetConditionBadgeIcon(badgeKeys[i]);
                Assert.That(badge, Is.Not.Null, "GetConditionBadgeIcon null for " + badgeKeys[i]);
                Assert.That(badge.texture.width, Is.EqualTo(32));
                Assert.That(badge.texture.height, Is.EqualTo(32));
                Assert.That(badge.texture.filterMode, Is.EqualTo(FilterMode.Point));
            }

            // Verify HUD binds shop, stash, equipment, and condition badge icons with zero churn and >= 15pt font floor.
            PartyState party = PartyState.FromBundle(bundle);
            party.Members[0].Conditions.Apply(CharConditionId.Poisoned, 1, 3, 25, true, 10);
            foreach (ItemDefinition item in bundle.Items.Values)
            {
                party.AddItemToStash(item.Id, 1, true, item.Charges);
                if (party.Stash.Count >= 4) break;
            }

            ExplorationSession explore = new ExplorationSession(bundle.Maps, "land_0", party, new DeterministicDiceRoller(91UL));
            CombatGrid grid = new CombatGrid(8, 8);
            CombatantState hero = CombatantState.FromHero(party.Members[0], new GridCoord(2, 4), bundle.Items);
            hero.Conditions.Apply(CharConditionId.Poisoned, 1, 3, 25, true, 10);
            CombatantState monster = CombatantState.FromMonster(bundle.Monsters[0], "mon_0_badge", new GridCoord(6, 4), new DeterministicDiceRoller(91UL));
            TacticalCombatSession combat = new TacticalCombatSession(
                grid,
                new[] { hero, monster },
                bundle.Spells,
                bundle.Items,
                new DeterministicDiceRoller(91UL),
                RulesetMode.ModernBalanced,
                party);

            bool hadEventSystem = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject tempGo = new GameObject("TempPack5HudIconsTest");
            try
            {
                BywaterHudController hud = tempGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas canvas = tempGo.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);

                hud.RefreshExploreHud(explore, bundle, null, null, 0, null, isReachable: true);
                hud.RefreshPartyDrawer(party, bundle, 0);
                hud.RefreshShopModal(party, bundle);

                Transform heroBadge0 = FindDescendantByPathSuffix(canvas.transform, "HeroCard_0/PortraitFrame/ConditionBadges/ConditionBadge_0");
                Assert.That(heroBadge0, Is.Not.Null, "Explore HeroCard_0 ConditionBadge_0 missing.");
                Assert.That(heroBadge0.gameObject.activeSelf, Is.True, "Poisoned hero should display ConditionBadge_0.");
                Assert.That(heroBadge0.GetComponent<UnityEngine.UI.Image>().sprite, Is.Not.Null);

                Transform buyIcon0 = FindDescendantByPathSuffix(canvas.transform, "BuyRow_0/Icon");
                Transform sellIcon0 = FindDescendantByPathSuffix(canvas.transform, "SellRow_0/Icon");
                Transform stashIcon0 = FindDescendantByPathSuffix(canvas.transform, "StashRow_0/Icon");
                Assert.That(buyIcon0, Is.Not.Null, "Shop BuyRow_0 Icon missing.");
                Assert.That(buyIcon0.GetComponent<UnityEngine.UI.Image>().sprite, Is.Not.Null);
                Assert.That(sellIcon0, Is.Not.Null, "Shop SellRow_0 Icon missing.");
                Assert.That(sellIcon0.GetComponent<UnityEngine.UI.Image>().sprite, Is.Not.Null);
                Assert.That(stashIcon0, Is.Not.Null, "Party StashRow_0 Icon missing.");
                Assert.That(stashIcon0.GetComponent<UnityEngine.UI.Image>().sprite, Is.Not.Null);

                hud.RefreshCombatHud(combat, bundle, CombatActionMode.Smart, -1, "1x");
                Transform combatBadge0 = FindDescendantByPathSuffix(canvas.transform, "CombatPartyCard_0/PortraitFrame/ConditionBadges/ConditionBadge_0");
                Assert.That(combatBadge0, Is.Not.Null, "CombatPartyCard_0 ConditionBadge_0 missing.");
                Assert.That(combatBadge0.gameObject.activeSelf, Is.True, "Poisoned combat hero should display ConditionBadge_0.");

                Transform buyContent = FindDescendantByPathSuffix(canvas.transform, "ShopBuyScroll/Viewport/Content");
                int buyChildrenBefore = buyContent.childCount;
                hud.RefreshShopModal(party, bundle);
                Assert.That(buyContent.childCount, Is.EqualTo(buyChildrenBefore), "2nd RefreshShopModal must not churn GameObjects.");

                TextMeshProUGUI[] labels = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < labels.Length; i++)
                {
                    Assert.That(labels[i].fontSize, Is.GreaterThanOrEqualTo(15f), $"Label '{labels[i].gameObject.name}' below 15pt.");
                }
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

        [Test]
        public void Pack5SurfacesAndCombatVfx_All7SurfacesAnd4VfxResolve512x128StripsAndPlayCleanly()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null, "BywaterAssetCatalog missing.");
            Assert.That(catalog.combatSurfaces.Count, Is.EqualTo(7), "Expected 7 Pack 5 combat surfaces.");
            Assert.That(catalog.combatVfx.Count, Is.EqualTo(4), "Expected 4 Pack 5 combat VFX.");

            SurfaceType[] expectedSurfaces =
            {
                SurfaceType.Water,
                SurfaceType.Oil,
                SurfaceType.Fire,
                SurfaceType.Ice,
                SurfaceType.Poison,
                SurfaceType.ElectrifiedWater,
                SurfaceType.SteamCloud
            };
            for (int i = 0; i < expectedSurfaces.Length; i++)
            {
                var entry = catalog.GetCombatSurfaceEntry(expectedSurfaces[i].ToString());
                Assert.That(entry, Is.Not.Null, "GetCombatSurfaceEntry null for " + expectedSurfaces[i]);
            }

            string[] expectedVfx =
            {
                "fireball_impact",
                "heal_sparkle",
                "slash_arc",
                "arrow_bolt"
            };
            for (int i = 0; i < expectedVfx.Length; i++)
            {
                var entry = catalog.GetCombatVfxEntry(expectedVfx[i]);
                Assert.That(entry, Is.Not.Null, "GetCombatVfxEntry null for " + expectedVfx[i]);
            }

            string[] stripPngPaths =
            {
                "Assets/Art/Cartography/Surfaces/surface_water.png",
                "Assets/Art/Cartography/Surfaces/surface_oil.png",
                "Assets/Art/Cartography/Surfaces/surface_fire.png",
                "Assets/Art/Cartography/Surfaces/surface_ice.png",
                "Assets/Art/Cartography/Surfaces/surface_poison.png",
                "Assets/Art/Cartography/Surfaces/surface_electrified_water.png",
                "Assets/Art/Cartography/Surfaces/surface_steam_cloud.png",
                "Assets/Art/Cartography/Vfx/vfx_fireball_impact.png",
                "Assets/Art/Cartography/Vfx/vfx_heal_sparkle.png",
                "Assets/Art/Cartography/Vfx/vfx_slash_arc.png",
                "Assets/Art/Cartography/Vfx/vfx_arrow_bolt.png"
            };

            for (int i = 0; i < stripPngPaths.Length; i++)
            {
                string path = stripPngPaths[i];
                TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(imp, Is.Not.Null, "Missing importer for " + path);
                Assert.That(imp.filterMode, Is.EqualTo(FilterMode.Point), "Expected Point filter on " + path);
                Assert.That(imp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), "Expected Uncompressed on " + path);
                Assert.That(imp.mipmapEnabled, Is.False, "Expected mipmaps off on " + path);

                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.That(tex, Is.Not.Null, "Missing Texture2D for " + path);
                Assert.That(tex.width, Is.EqualTo(512), "Expected 512px width (4x128 frames) on " + path);
                Assert.That(tex.height, Is.EqualTo(128), "Expected 128px height on " + path);
            }

            GameObject overlaysGo = new GameObject("TempPack5VfxOverlaysTest");
            GameObject unitGo = new GameObject("TempPack5BadgeUnitTest");
            try
            {
                BywaterGridOverlays overlays = overlaysGo.AddComponent<BywaterGridOverlays>();
                overlays.Initialize(catalog);
                overlays.PlayCombatVfx("fireball_impact", Vector3.zero, new Vector3(2f, 0f, -2f), isProjectile: false);
                overlays.PlayCombatVfx("heal_sparkle", Vector3.zero, Vector3.zero, isProjectile: false);
                overlays.PlayCombatVfx("slash_arc", Vector3.zero, new Vector3(1f, 0f, -1f), isProjectile: false);
                overlays.PlayCombatVfx("arrow_bolt", Vector3.zero, new Vector3(4f, 0f, -3f), isProjectile: true);
                overlays.ClearCombatVfx();

                BywaterUnitView unitView = unitGo.AddComponent<BywaterUnitView>();
                unitView.Configure("hero_knight", "Aldric", null, Color.cyan, catalog.uiFont, isAllyOverride: true);
                unitView.UpdateConditionBadgeIcon(catalog.GetConditionBadgeIcon("poison"));
                unitView.UpdateConditionBadgeIcon(null);
            }
            finally
            {
                Object.DestroyImmediate(overlaysGo);
                Object.DestroyImmediate(unitGo);
            }
        }

        private static void AssertUnifiedUnitSheet(string unitId, bool isHero)
        {
            string sheetPath = $"Assets/Art/Cartography/units/{unitId}_sheet.png";
            string normalPath = $"Assets/Art/Cartography/units/{unitId}_sheet_n.png";

            TextureImporter sheetImp = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            Assert.That(sheetImp, Is.Not.Null, "Missing unit sheet importer: " + sheetPath);
            Assert.That(sheetImp.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(sheetImp.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple), "Expected Multiple 4-frame slice on " + sheetPath);
            Assert.That(sheetImp.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(sheetImp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(sheetImp.mipmapEnabled, Is.False);
            Assert.That(sheetImp.spritePixelsPerUnit, Is.EqualTo(64f));

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
            Assert.That(tex, Is.Not.Null);
            Assert.That(tex.width, Is.EqualTo(512), "Expected 512 width on " + sheetPath);
            Assert.That(tex.height, Is.EqualTo(128), "Expected 128 height on " + sheetPath);

            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(sheetPath);
            int spriteCount = 0;
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is Sprite s)
                {
                    spriteCount++;
                    Assert.That(s.rect.size, Is.EqualTo(new Vector2(128f, 128f)), "Expected 128x128 frame rect on " + sheetPath);
                }
            }
            Assert.That(spriteCount, Is.EqualTo(4), $"Expected 4 sliced frames on {(isHero ? "hero" : "enemy")} {sheetPath}");

            TextureImporter normImp = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            Assert.That(normImp, Is.Not.Null, "Missing unit normal importer: " + normalPath);
            Assert.That(normImp.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(normImp.sRGBTexture, Is.False);
            Assert.That(normImp.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(normImp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(normImp.mipmapEnabled, Is.False);
        }

        private static void AdvanceCombatTurnToActor(TacticalCombatSession combat, string actorId)
        {
            int safety = 0;
            while (combat.Outcome == CombatOutcome.InProgress &&
                   combat.CurrentActor != null &&
                   !string.Equals(combat.CurrentActor.Id, actorId, System.StringComparison.Ordinal) &&
                   safety < 24)
            {
                safety++;
                combat.EndTurn();
            }
        }

        [Test]
        public void PlayabilityFixes_AllSixBugsFixedAcrossCoreWorldHudAndUnits()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(bundle, Is.Not.Null);
            Assert.That(catalog, Is.Not.Null);

            // Fix #1: Prop footprint blocking (StoneWell (13,10) blocked while spawn (10,9), roads, POIs, portal open)
            CartographyWorldData landWorld = CartographyWorldData.BuildFromTileMap(bundle.Land0);
            bundle.Land0.SetBlockedPropFootprints(landWorld.ExportBlockedPropGridCoords());
            Assert.That(bundle.Land0.BlockedPropTileCount, Is.GreaterThan(0), "Blocked prop footprints should be populated.");
            Assert.That(bundle.Land0.IsPassable(13, 10, hasBoat: false), Is.False, "StoneWell plaza tile (13,10) must block walkability.");
            Assert.That(bundle.Land0.IsPassable(10, 9, hasBoat: false), Is.True, "Bywater plaza spawn (10,9) must remain passable.");

            PartyState exploreParty = PartyState.FromBundle(bundle);
            ExplorationSession session = new ExplorationSession(bundle.Maps, "land_0", exploreParty, new DeterministicDiceRoller(777UL), new GridCoord(10, 9));
            PathResult wellPath = session.PlanPath(new GridCoord(13, 10), allowDiagonal: true);
            Assert.That(wellPath.Found, Is.False, "Pathfinding to StoneWell (13,10) must be blocked.");

            for (int i = 0; i < bundle.Land0.PointsOfInterest.Count; i++)
            {
                MapPointOfInterest poi = bundle.Land0.PointsOfInterest[i];
                Assert.That(bundle.Land0.IsPassable(poi.X, poi.Y, hasBoat: false), Is.True, $"POI {poi.Id} must remain passable.");
                PathResult poiPath = session.PlanPath(poi.Coord, allowDiagonal: true);
                Assert.That(poiPath.Found, Is.True, $"POI {poi.Id} at {poi.Coord} must remain reachable from (10,9).");
            }
            for (int i = 0; i < bundle.Land0.Portals.Count; i++)
            {
                MapPortal portal = bundle.Land0.Portals[i];
                Assert.That(bundle.Land0.IsPassable(portal.X, portal.Y, hasBoat: false), Is.True, $"Portal {portal.Id} must remain passable.");
                PathResult portalPath = session.PlanPath(portal.Coord, allowDiagonal: true);
                Assert.That(portalPath.Found, Is.True, $"Portal {portal.Id} at {portal.Coord} must remain reachable from (10,9).");
            }

            // Enclosed shopkeepers/priestess live inside modals and are opened via walkable shop/temple doorways.
            Assert.That(landWorld.Npcs.Count, Is.EqualTo(0), "Enclosed town merchants must live inside Shop/Temple modals.");

            // Fix #2: Shader has zero Bayer checkerboard clip and supports clean transparent fade
            string shaderText = File.ReadAllText(Path.GetFullPath("Assets/Shaders/CartographyBillboard.shader"));
            Assert.That(shaderText.Contains("kBayer4x4"), Is.False, "Bayer dither array must be removed from CartographyBillboard.shader.");
            Assert.That(shaderText.Contains("ComputeBayer4x4Threshold"), Is.False, "Bayer dither function must be removed from CartographyBillboard.shader.");

            GameObject worldGo = new GameObject("TempPlayabilityWorldRenderer");
            GameObject camGo = new GameObject("TempPlayabilityCamRig");
            GameObject ctrlGo = new GameObject("TempPlayabilityCtrl");
            GameObject hudGo = new GameObject("TempPlayabilityHud");
            GameObject unitGo = new GameObject("TempPlayabilityUnitView");
            try
            {
                BywaterWorldRenderer renderer = worldGo.AddComponent<BywaterWorldRenderer>();
                renderer.Initialize(catalog);
                renderer.RenderMap(bundle.Land0, new GridCoord(10, 9), forceRebuild: true);
                Assert.That(renderer.ActiveNpcCount, Is.EqualTo(0));

                // Fix #4: Wide zoom ceiling >= 32m & manual pan preservation across non-recentering target updates
                Camera cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
                BywaterCameraRig rig = camGo.AddComponent<BywaterCameraRig>();
                Assert.That(rig.EffectiveExploreMaxDistance, Is.GreaterThanOrEqualTo(32f), "Explore maxDistance should be >= 32m.");
                Assert.That(rig.MaxCombatDistance, Is.GreaterThanOrEqualTo(34f), "Combat maxDistance should be >= 34m.");

                // Fix #3: UnitAnimState transitions & ground-locked root transform
                BywaterUnitView unit = unitGo.AddComponent<BywaterUnitView>();
                unit.Configure("hero_knight", "Aldric", null, Color.yellow, catalog.uiFont, 2.0f, false, true, catalog);
                unit.SetWorldPosition(new Vector3(10.5f, 0f, -9.5f));
                unit.SetAnimState(UnitAnimState.Walk);
                Assert.That(unit.CurrentAnimState, Is.EqualTo(UnitAnimState.Walk));
                unit.SetFacingRight(false);
                Assert.That(unit.FacingRight, Is.False);
                unit.SetAnimState(UnitAnimState.Attack, 0.45f);
                Assert.That(unit.CurrentAnimState, Is.EqualTo(UnitAnimState.Attack));
                unit.SetAnimState(UnitAnimState.Idle);
                Assert.That(unit.CurrentAnimState, Is.EqualTo(UnitAnimState.Idle));

                // Fix #3 Option A: Overworld leader-only (1 view), Combat/Camp all 6
                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Assert.That(hud.IsDevToolsVisible, Is.False, "Developer Tools must default OFF.");

                Canvas canvas = hudGo.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);

                // Fix #5 Single-Venue Shop Layout (>= 65% ScrollView body, zero ShopTabs, zero TempleHealBtn inside Shop)
                hud.OpenShop("1", exploreParty, bundle);
                Canvas.ForceUpdateCanvases();
                Transform shopModal = FindDescendantByName(canvas.transform, "TownShopModal");
                Assert.That(shopModal, Is.Not.Null);
                Assert.That(FindDescendantByName(shopModal, "ShopTabs"), Is.Null, "Cross-shop switcher bar must be removed.");
                Assert.That(FindDescendantByName(shopModal, "TempleHealBtn"), Is.Null, "Temple button must not live inside Shop modal.");

                Transform shopBody = FindDescendantByName(shopModal, "ShopBody");
                Transform shopHeader = FindDescendantByName(shopModal, "Header");
                Transform filterRow = FindDescendantByName(shopModal, "FilterRow");
                Transform sellHeader = FindDescendantByName(shopModal, "SellHeader");
                Assert.That(shopBody, Is.Not.Null);
                Assert.That(shopHeader, Is.Not.Null);
                Assert.That(filterRow, Is.Not.Null);
                Assert.That(sellHeader, Is.Not.Null);

                RectTransform shopRt = shopModal.GetComponent<RectTransform>();
                RectTransform bodyRt = shopBody.GetComponent<RectTransform>();
                RectTransform headerRt = shopHeader.GetComponent<RectTransform>();
                RectTransform filterRt = filterRow.GetComponent<RectTransform>();
                RectTransform sellHeaderRt = sellHeader.GetComponent<RectTransform>();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(shopRt);
                float bodyRatio = bodyRt.rect.height / Mathf.Max(1f, shopRt.rect.height);
                Assert.That(bodyRatio, Is.GreaterThanOrEqualTo(0.65f), $"ShopBody occupied only {bodyRatio:P1} of TownShopModal height.");
                Assert.That(headerRt.rect.height, Is.LessThanOrEqualTo(76f), $"Shop Header stretched too tall: {headerRt.rect.height}");
                Assert.That(filterRt.rect.height, Is.LessThanOrEqualTo(44f), $"Shop FilterRow stretched too tall: {filterRt.rect.height}");
                Assert.That(sellHeaderRt.rect.height, Is.LessThanOrEqualTo(42f), $"Shop SellHeader stretched too tall: {sellHeaderRt.rect.height}");

                Transform shopPortrait = FindDescendantByPathSuffix(shopHeader, "ShopkeeperPortraitFrame/Portrait");
                Assert.That(shopPortrait, Is.Not.Null, "ShopkeeperPortraitFrame/Portrait should exist in Shop Header.");
                Assert.That(shopPortrait.GetComponent<UnityEngine.UI.Image>().preserveAspect, Is.True);

                // Fix #2 check: Narrow foreground occlusion fade switches ONLY directly occluding tree to Transparent
                IReadOnlyList<CartographyPropPlacement> worldProps = landWorld.Props;
                Vector3 testTreePos = Vector3.zero;
                bool foundTree = false;
                for (int i = 0; i < worldProps.Count; i++)
                {
                    if (worldProps[i].Kind == CartographyPropKind.TreeOakA ||
                        worldProps[i].Kind == CartographyPropKind.TreePineA)
                    {
                        testTreePos = worldProps[i].WorldPosition;
                        foundTree = true;
                        break;
                    }
                }
                Assert.That(foundTree, Is.True, "Expected at least one oak/pine tree prop in land_0.");
                renderer.UpdateTreeOcclusionFade(new[] { new Vector3(testTreePos.x, testTreePos.y, testTreePos.z + 1.0f) });
                MeshRenderer[] propRenderers = renderer.GetComponentsInChildren<MeshRenderer>(false);
                bool sawTransparentFaded = false;
                bool sawOpaqueCutout = false;
                for (int i = 0; i < propRenderers.Length; i++)
                {
                    Material mat = propRenderers[i].sharedMaterial;
                    if (mat == null) continue;
                    if (mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent &&
                        mat.name.EndsWith("_FadeTransparent", System.StringComparison.Ordinal))
                    {
                        sawTransparentFaded = true;
                    }
                    else if (mat.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent)
                    {
                        sawOpaqueCutout = true;
                    }
                }
                Assert.That(sawTransparentFaded, Is.True, "Occluding foreground tree should swap cleanly to _FadeTransparent material.");
                Assert.That(sawOpaqueCutout, Is.True, "Non-occluding trees and props should stay crisp AlphaTest cutout.");

                // Fix #5 Temple Shrine Modal isolated from Shop
                hud.OpenTempleModal(exploreParty, bundle);
                Assert.That(hud.IsTempleOpen, Is.True);
                Assert.That(hud.IsShopOpen, Is.False);
                Transform templeModal = FindDescendantByName(canvas.transform, "TempleShrineModal");
                Assert.That(templeModal, Is.Not.Null);
                Assert.That(FindDescendantByName(templeModal, "TempleHealBtn"), Is.Not.Null);
                Assert.That(FindDescendantByName(templeModal, "TempleCureBtn"), Is.Not.Null);
                Assert.That(FindDescendantByName(templeModal, "TempleRechargeBtn"), Is.Not.Null);
                hud.CloseTempleModal();

                // Fix #6 Pause Menu: Quit to Desktop (2-step confirm), no SettingsResumeBtn, clear Sound: ON/MUTED
                hud.ToggleSettingsModal(true);
                Transform settingsModal = FindDescendantByName(canvas.transform, "SettingsModal");
                Assert.That(settingsModal, Is.Not.Null);
                Assert.That(FindDescendantByName(settingsModal, "SettingsResumeBtn"), Is.Null, "Duplicate bottom SettingsResumeBtn must be removed.");
                Transform quitBtnTf = FindDescendantByName(settingsModal, "SettingsQuitBtn");
                Assert.That(quitBtnTf, Is.Not.Null, "SettingsQuitBtn must exist.");

                bool quitFired = false;
                hud.OnQuitRequested += () => quitFired = true;
                UnityEngine.UI.Button quitBtn = quitBtnTf.GetComponent<UnityEngine.UI.Button>();
                quitBtn.onClick.Invoke();
                Assert.That(quitFired, Is.False, "First click arms quit confirmation.");
                quitBtn.onClick.Invoke();
                Assert.That(quitFired, Is.True, "Second click confirms Quit to Desktop.");

                Transform muteLabelTf = FindDescendantByPathSuffix(settingsModal, "MuteRow/RowLabel");
                Transform muteStateTf = FindDescendantByPathSuffix(settingsModal, "MuteRow/ToggleBtn/Label");
                Assert.That(muteLabelTf, Is.Not.Null);
                Assert.That(muteStateTf, Is.Not.Null);
                Assert.That(muteLabelTf.GetComponent<TMPro.TextMeshProUGUI>().text, Is.EqualTo("Sound"));
                Assert.That(muteStateTf.GetComponent<TMPro.TextMeshProUGUI>().text, Is.EqualTo("ON"));
                FindDescendantByPathSuffix(settingsModal, "MuteRow/ToggleBtn").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.That(muteStateTf.GetComponent<TMPro.TextMeshProUGUI>().text, Is.EqualTo("MUTED"));

                // Verify Option A Explore Hero View count via BywaterGameController
                BywaterGameController ctrl = ctrlGo.AddComponent<BywaterGameController>();
                ctrl.worldRenderer = renderer;
                ctrl.hudController = hud;
                ctrl.cameraRig = rig;
                ctrl.InitializeForTests(bundle, session, 777UL);
                typeof(BywaterGameController)
                    .GetMethod("EnterExplorationView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ctrl, new object[] { true });
                Assert.That(ctrl.ActiveExploreViewCount, Is.EqualTo(1), "Option A must render only 1 leader view in overworld exploration.");

                typeof(BywaterGameController)
                    .GetMethod("EnterCampMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ctrl, null);
                Assert.That(ctrl.ActiveExploreViewCount, Is.EqualTo(6), "Camp mode must spawn all 6 living heroes around campfire.");

                typeof(BywaterGameController)
                    .GetMethod("ExitCampMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ctrl, null);
                Assert.That(ctrl.ActiveExploreViewCount, Is.EqualTo(1), "Exiting camp must return to 1 leader view.");
            }
            finally
            {
                Object.DestroyImmediate(unitGo);
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(ctrlGo);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(worldGo);
            }
        }

        [Test]
        public void W240AnimationsAndShopkeepers_All26WalkAttack4NpcSheetsAnd4PortraitsLoadCleanly()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null, "BywaterAssetCatalog missing.");
            Assert.That(catalog.cartographyNpcSheets.Count, Is.EqualTo(4), "Expected 4 Bywater storefront NPC sheets in catalog.");

            string[] unitIds =
            {
                "hero_knight", "hero_cleric", "hero_ranger",
                "hero_mage", "hero_paladin", "hero_rogue",
                "monster_giant_frog", "monster_goblin_raider", "monster_goblin_archer",
                "monster_orc_warrior", "monster_giant_rat", "monster_skeleton_warrior",
                "monster_bandit_cutthroat"
            };
            string[] animStates = { "walk", "attack" };

            for (int i = 0; i < unitIds.Length; i++)
            {
                string unitId = unitIds[i];
                for (int a = 0; a < animStates.Length; a++)
                {
                    string anim = animStates[a];
                    string sheetPath = $"Assets/Art/Cartography/units/{unitId}_{anim}_sheet.png";
                    string normalPath = $"Assets/Art/Cartography/units/{unitId}_{anim}_sheet_n.png";

                    AssertFourFrameUnitSheet(sheetPath, normalPath, $"{unitId}_{anim}_sheet");

                    UnitAnimState state = anim == "walk" ? UnitAnimState.Walk : UnitAnimState.Attack;
                    Material stateMat = catalog.ResolveCartographyUnitMaterial(unitId, state, null, out bool hasDedicated);
                    Assert.That(hasDedicated, Is.True, $"Expected dedicated {anim} sheet for {unitId}.");
                    Assert.That(stateMat, Is.Not.Null, $"Material null for {unitId} {anim}.");
                    Assert.That(stateMat.shader.name, Is.EqualTo("Realmz/CartographyBillboard"));
                    Assert.That(stateMat.GetFloat("_Cols"), Is.EqualTo(4f));
                    Assert.That(stateMat.GetFloat("_FrameCount"), Is.EqualTo(4f));
                    Assert.That(stateMat.GetTexture("_BaseMap"), Is.Not.Null);
                    Assert.That(stateMat.GetTexture("_BaseMap").name, Is.EqualTo($"{unitId}_{anim}_sheet"));
                    Assert.That(stateMat.GetTexture("_BumpMap"), Is.Not.Null, $"Normal map missing on material for {unitId}_{anim}.");
                }
            }

            string[] npcIds = { "npc_blacksmith", "npc_tanner", "npc_apothecary", "npc_priestess" };
            for (int i = 0; i < npcIds.Length; i++)
            {
                string npcId = npcIds[i];
                string sheetPath = $"Assets/Art/Cartography/units/{npcId}_sheet.png";
                string normalPath = $"Assets/Art/Cartography/units/{npcId}_sheet_n.png";
                AssertFourFrameUnitSheet(sheetPath, normalPath, $"{npcId}_sheet");

                Material npcMat = catalog.ResolveCartographyUnitMaterial(npcId, UnitAnimState.Idle, null, out bool hasNpc);
                Assert.That(hasNpc, Is.True, $"Expected dedicated idle sheet for {npcId}.");
                Assert.That(npcMat, Is.Not.Null, $"NPC material null for {npcId}.");
                Assert.That(npcMat.GetTexture("_BaseMap").name, Is.EqualTo($"{npcId}_sheet"));
                Assert.That(npcMat.GetTexture("_BumpMap"), Is.Not.Null, $"NPC normal map missing on material for {npcId}.");

                string portraitPath = $"Assets/Art/Cartography/Portraits/portrait_{npcId}.png";
                string portraitNormalPath = $"Assets/Art/Cartography/Portraits/portrait_{npcId}_n.png";

                TextureImporter pImp = AssetImporter.GetAtPath(portraitPath) as TextureImporter;
                Assert.That(pImp, Is.Not.Null, "Missing portrait importer: " + portraitPath);
                Assert.That(pImp.textureType, Is.EqualTo(TextureImporterType.Sprite));
                Assert.That(pImp.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(pImp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
                Assert.That(pImp.mipmapEnabled, Is.False);
                Assert.That(pImp.spritePixelsPerUnit, Is.EqualTo(64f));

                TextureImporter pnImp = AssetImporter.GetAtPath(portraitNormalPath) as TextureImporter;
                Assert.That(pnImp, Is.Not.Null, "Missing portrait normal importer: " + portraitNormalPath);
                Assert.That(pnImp.textureType, Is.EqualTo(TextureImporterType.NormalMap));
                Assert.That(pnImp.sRGBTexture, Is.False);
                Assert.That(pnImp.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(pnImp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
                Assert.That(pnImp.mipmapEnabled, Is.False);

                Sprite portrait = catalog.GetUnifiedPortrait(npcId);
                Assert.That(portrait, Is.Not.Null, "GetUnifiedPortrait null for " + npcId);
                Assert.That(portrait.texture, Is.Not.Null);
                Assert.That(portrait.texture.width, Is.EqualTo(128));
                Assert.That(portrait.texture.height, Is.EqualTo(128));
                Assert.That(portrait.texture.name, Is.EqualTo($"portrait_{npcId}"));
            }

            // Verify BywaterUnitView swaps actual Walk/Attack/Idle materials and HUD binds all 4 NPC portraits
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            PartyState party = PartyState.FromBundle(bundle);
            bool hadEventSystem = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject unitGo = new GameObject("TempW240UnitAnimSwapTest");
            GameObject hudGo = new GameObject("TempW240NpcHudPortraitTest");
            try
            {
                BywaterUnitView unitView = unitGo.AddComponent<BywaterUnitView>();
                unitView.Configure("hero_knight", "Aldric", null, Color.cyan, catalog.uiFont, 2.0f, false, true, catalog);
                Transform spriteQuad = FindDescendantByName(unitView.transform, "SpriteQuad");
                Assert.That(spriteQuad, Is.Not.Null);
                MeshRenderer sr = spriteQuad.GetComponent<MeshRenderer>();
                Assert.That(sr.sharedMaterial.GetTexture("_BaseMap").name, Is.EqualTo("hero_knight_sheet"));

                unitView.SetAnimState(UnitAnimState.Walk);
                Assert.That(sr.sharedMaterial.GetTexture("_BaseMap").name, Is.EqualTo("hero_knight_walk_sheet"));

                unitView.SetAnimState(UnitAnimState.Attack, 0.45f);
                Assert.That(sr.sharedMaterial.GetTexture("_BaseMap").name, Is.EqualTo("hero_knight_attack_sheet"));

                unitView.SetAnimState(UnitAnimState.Idle);
                Assert.That(sr.sharedMaterial.GetTexture("_BaseMap").name, Is.EqualTo("hero_knight_sheet"));

                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas canvas = hudGo.GetComponentInChildren<Canvas>(true);

                (string shopId, string expectedTex)[] shopChecks =
                {
                    ("1", "portrait_npc_blacksmith"),
                    ("4", "portrait_npc_tanner"),
                    ("10", "portrait_npc_apothecary")
                };
                for (int i = 0; i < shopChecks.Length; i++)
                {
                    hud.OpenShop(shopChecks[i].shopId, party, bundle);
                    Transform portraitTf = FindDescendantByPathSuffix(canvas.transform, "ShopkeeperPortraitFrame/Portrait");
                    Assert.That(portraitTf, Is.Not.Null);
                    UnityEngine.UI.Image img = portraitTf.GetComponent<UnityEngine.UI.Image>();
                    Assert.That(img.preserveAspect, Is.True);
                    Assert.That(img.sprite, Is.Not.Null);
                    Assert.That(img.sprite.texture.name, Is.EqualTo(shopChecks[i].expectedTex));
                }

                hud.OpenTempleModal(party, bundle);
                Transform templePortraitTf = FindDescendantByPathSuffix(canvas.transform, "PriestessPortraitFrame/Portrait");
                Assert.That(templePortraitTf, Is.Not.Null);
                UnityEngine.UI.Image templeImg = templePortraitTf.GetComponent<UnityEngine.UI.Image>();
                Assert.That(templeImg.preserveAspect, Is.True);
                Assert.That(templeImg.sprite, Is.Not.Null);
                Assert.That(templeImg.sprite.texture.name, Is.EqualTo("portrait_npc_priestess"));
            }
            finally
            {
                Object.DestroyImmediate(unitGo);
                Object.DestroyImmediate(hudGo);
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

        private static void AssertFourFrameUnitSheet(string sheetPath, string normalPath, string expectedBaseName)
        {
            TextureImporter sheetImp = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            Assert.That(sheetImp, Is.Not.Null, "Missing unit sheet importer: " + sheetPath);
            Assert.That(sheetImp.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(sheetImp.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple), "Expected Multiple 4-frame slice on " + sheetPath);
            Assert.That(sheetImp.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(sheetImp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(sheetImp.mipmapEnabled, Is.False);
            Assert.That(sheetImp.spritePixelsPerUnit, Is.EqualTo(64f));

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
            Assert.That(tex, Is.Not.Null, "Missing Texture2D at " + sheetPath);
            Assert.That(tex.width, Is.EqualTo(512), "Expected 512 width on " + sheetPath);
            Assert.That(tex.height, Is.EqualTo(128), "Expected 128 height on " + sheetPath);

            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(sheetPath);
            int spriteCount = 0;
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is Sprite s)
                {
                    spriteCount++;
                    Assert.That(s.rect.size, Is.EqualTo(new Vector2(128f, 128f)), "Expected 128x128 frame rect on " + sheetPath);
                    Assert.That(s.name, Does.StartWith(expectedBaseName + "_"));
                }
            }
            Assert.That(spriteCount, Is.EqualTo(4), "Expected 4 sliced frames on " + sheetPath);

            TextureImporter normImp = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            Assert.That(normImp, Is.Not.Null, "Missing unit normal importer: " + normalPath);
            Assert.That(normImp.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(normImp.sRGBTexture, Is.False);
            Assert.That(normImp.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(normImp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(normImp.mipmapEnabled, Is.False);
        }

        private static GridCoord FindAdjacentPassableTile(TileMapData map, GridCoord origin, bool hasBoat)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = origin.X + dx;
                    int ny = origin.Y + dy;
                    if (map.InBounds(nx, ny) && map.IsPassable(nx, ny, hasBoat))
                    {
                        return new GridCoord(nx, ny);
                    }
                }
            }

            return origin;
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

        [Test]
        public void Bug2AndBug7_CameraPanDampedComfortably_AndAllModalsLockZoomPanAndPointerClicks()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            PartyState party = PartyState.FromBundle(bundle);
            Assert.That(catalog, Is.Not.Null);

            bool hadEs = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject camGo = new GameObject("TempBug27CamRig");
            GameObject hudGo = new GameObject("TempBug27Hud");
            try
            {
                Camera cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
                BywaterCameraRig rig = camGo.AddComponent<BywaterCameraRig>();
                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();

                typeof(BywaterCameraRig).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(rig, null);
                hud.BuildUi(catalog);
                rig.SetHudController(hud);
                rig.SetWorldBounds(90, 90);
                rig.SetFollowTarget(new Vector3(10.5f, 0f, -9.5f), immediate: true);

                // Bug #2: Tactical pan speed + gentle tap nudge
                Assert.That(rig.KeyPanSpeed, Is.LessThanOrEqualTo(5.5f), "Keyboard pan speed must be slowed to comfortable tactical pace.");
                Vector3 nudge = rig.ApplyKeyboardPanInput(Vector2.up, shiftHeld: false, 0.05f);
                Assert.That(nudge.magnitude, Is.GreaterThan(0.01f).And.LessThan(0.25f), "Quick key tap should gently nudge camera.");
                Vector3 hold = rig.ApplyKeyboardPanInput(Vector2.up, shiftHeld: false, 0.25f);
                Vector3 shiftHold = rig.ApplyKeyboardPanInput(Vector2.up, shiftHeld: true, 0.25f);
                Assert.That(shiftHold.magnitude, Is.GreaterThan(hold.magnitude), "Shift modifier should apply modest boost.");
                rig.Recenter(immediate: true);

                // Verify normal scroll works when no modal is open
                float baselineDist = rig.TargetDistance;
                float openScrollDelta = rig.ApplyScrollZoomDelta(-120f);
                Assert.That(openScrollDelta, Is.Not.EqualTo(0f), "Scroll should zoom when no modal is open.");
                float activeDist = rig.TargetDistance;
                Assert.That(activeDist, Is.Not.EqualTo(baselineDist));

                // Bug #7: Party/Stash modal locks zoom (0.0f delta), keyboard pan, and right-drag pan
                hud.TogglePartyPanel(true);
                Assert.That(hud.IsPartyOpen, Is.True);
                Assert.That(rig.ApplyScrollZoomDelta(240f), Is.EqualTo(0f), "Scroll zoom must be 0.0f delta when Party/Stash modal is open.");
                Assert.That(rig.ApplyScrollZoomDelta(-240f), Is.EqualTo(0f), "Scroll out must also be 0.0f delta when Party/Stash modal is open.");
                Assert.That(rig.TargetDistance, Is.EqualTo(activeDist), "TargetDistance must not change during Party/Stash modal scroll.");
                Assert.That(rig.ApplyKeyboardPanInput(Vector2.one, false, 0.25f), Is.EqualTo(Vector3.zero), "Keyboard pan must be locked while Party/Stash modal is open.");
                Assert.That(rig.ApplyPointerDragPan(new Vector2(60f, -40f), true), Is.EqualTo(Vector3.zero), "Right-drag pan must be locked while Party/Stash modal is open.");
                hud.TogglePartyPanel(false);

                // Shop modal lock
                hud.OpenShop("1", party, bundle);
                Assert.That(hud.IsShopOpen, Is.True);
                Assert.That(rig.ApplyScrollZoomDelta(120f), Is.EqualTo(0f));
                Assert.That(rig.ApplyKeyboardPanInput(Vector2.right, false, 0.2f), Is.EqualTo(Vector3.zero));
                Assert.That(rig.TargetDistance, Is.EqualTo(activeDist));
                hud.CloseShopModal();

                // Temple modal lock
                hud.OpenTempleModal(party, bundle);
                Assert.That(hud.IsTempleOpen, Is.True);
                Assert.That(rig.ApplyScrollZoomDelta(120f), Is.EqualTo(0f));
                Assert.That(rig.ApplyKeyboardPanInput(Vector2.left, false, 0.2f), Is.EqualTo(Vector3.zero));
                hud.CloseTempleModal();

                // Settings / Pause modal lock
                hud.ToggleSettingsModal(true);
                Assert.That(hud.IsSettingsOpen, Is.True);
                Assert.That(rig.ApplyScrollZoomDelta(-120f), Is.EqualTo(0f));
                Assert.That(rig.ApplyKeyboardPanInput(Vector2.down, false, 0.2f), Is.EqualTo(Vector3.zero));
                hud.CloseSettingsModal();

                // Quick Resolve modal lock
                hud.ShowQuickResolveModal("Skirmish", "Ambushed!");
                Assert.That(hud.IsQuickResolveOpen, Is.True);
                Assert.That(rig.ApplyScrollZoomDelta(120f), Is.EqualTo(0f));
                hud.HideQuickResolveModal();

                // Closing modals restores scroll zoom
                Assert.That(rig.ApplyScrollZoomDelta(120f), Is.Not.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(hudGo);
                if (!hadEs)
                {
                    EventSystem created = Object.FindFirstObjectByType<EventSystem>();
                    if (created != null) Object.DestroyImmediate(created.gameObject);
                }
            }
        }

        [Test]
        public void Bug4_AllFourNpcsBlocked_AdjacentApproachOpensShopAndTemple_AndPromptIsCompactCanvasPill()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            PartyState party = PartyState.FromBundle(bundle);

            bool hadEs = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject worldGo = new GameObject("TempBug4World");
            GameObject ctrlGo = new GameObject("TempBug4Ctrl");
            GameObject hudGo = new GameObject("TempBug4Hud");
            GameObject camGo = new GameObject("TempBug4Cam");
            try
            {
                BywaterWorldRenderer renderer = worldGo.AddComponent<BywaterWorldRenderer>();
                renderer.Initialize(catalog);
                Camera cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
                BywaterCameraRig rig = camGo.AddComponent<BywaterCameraRig>();
                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);

                ExplorationSession session = new ExplorationSession(bundle.Maps, "land_0", party, new DeterministicDiceRoller(555UL), new GridCoord(10, 9));
                BywaterGameController ctrl = ctrlGo.AddComponent<BywaterGameController>();
                ctrl.assetCatalog = catalog;
                ctrl.worldRenderer = renderer;
                ctrl.hudController = hud;
                ctrl.cameraRig = rig;
                ctrl.InitializeForTests(bundle, session, 555UL);
                typeof(BywaterGameController).GetMethod("EnterExplorationView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(ctrl, new object[] { true });

                Assert.That(renderer.ActiveNpcCount, Is.EqualTo(0));

                // Test clicking a Shop #1 doorway POI tile walks to doorway and opens Shop #1 modal.
                MapPointOfInterest shop1Poi = bundle.Land0.PointsOfInterest.First(p => string.Equals(p.ShopId, "1", System.StringComparison.OrdinalIgnoreCase));
                typeof(BywaterGameController).GetMethod("UpdateExplorationInput", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { shop1Poi.Coord, true });
                ctrl.ForceCompleteWalkForTest();
                Assert.That(session.Position.ChebyshevDistance(shop1Poi.Coord), Is.LessThanOrEqualTo(1), "Leader must arrive at Shop #1 doorway.");
                Assert.That(hud.IsShopOpen, Is.True, "Clicking Shop #1 doorway must open Shop #1 modal.");
                hud.CloseShopModal();
            }
            finally
            {
                Object.DestroyImmediate(worldGo);
                Object.DestroyImmediate(ctrlGo);
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(camGo);
                if (!hadEs)
                {
                    EventSystem created = Object.FindFirstObjectByType<EventSystem>();
                    if (created != null) Object.DestroyImmediate(created.gameObject);
                }
            }
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

        [Test]
        public void Bugs1To6_TownNpcCombatHide_SpeedConstants_DirectionalIntent_FlatCorpse_TargetingCancel_AndHoverCache()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            PartyState party = PartyState.FromBundle(bundle);

            bool hadEs = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject worldGo = new GameObject("TempBugPassWorld");
            GameObject ctrlGo = new GameObject("TempBugPassCtrl");
            GameObject hudGo = new GameObject("TempBugPassHud");
            GameObject camGo = new GameObject("TempBugPassCam");
            GameObject overlaysGo = new GameObject("TempBugPassOverlays");
            try
            {
                BywaterWorldRenderer renderer = worldGo.AddComponent<BywaterWorldRenderer>();
                renderer.Initialize(catalog);
                Camera cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
                BywaterCameraRig rig = camGo.AddComponent<BywaterCameraRig>();
                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                BywaterGridOverlays overlays = overlaysGo.AddComponent<BywaterGridOverlays>();
                overlays.Initialize(catalog);

                ExplorationSession session = new ExplorationSession(bundle.Maps, "land_0", party, new DeterministicDiceRoller(777UL), new GridCoord(10, 9));
                BywaterGameController ctrl = ctrlGo.AddComponent<BywaterGameController>();
                ctrl.assetCatalog = catalog;
                ctrl.worldRenderer = renderer;
                ctrl.hudController = hud;
                ctrl.cameraRig = rig;
                ctrl.gridOverlays = overlays;
                ctrl.InitializeForTests(bundle, session, 777UL);
                typeof(BywaterGameController).GetMethod("EnterExplorationView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(ctrl, new object[] { true });

                // Bug #6: Overland hover A* cache + non-rebuilding step pathing
                var hoverM = typeof(BywaterGameController).GetMethod("UpdateExplorationHoverVisuals", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var pathField = typeof(BywaterGameController).GetField("_explorePathfinder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                AStarPathfinder pathfinder = pathField?.GetValue(ctrl) as AStarPathfinder;
                Assert.That(pathfinder, Is.Not.Null);
                hoverM?.Invoke(ctrl, new object[] { new GridCoord(12, 9) });
                long searchesAfterFirst = pathfinder.SearchCount;
                hoverM?.Invoke(ctrl, new object[] { new GridCoord(12, 9) });
                hoverM?.Invoke(ctrl, new object[] { new GridCoord(12, 9) });
                Assert.That(pathfinder.SearchCount, Is.EqualTo(searchesAfterFirst), "Identical hover tile must reuse cached path without re-running A*.");

                // Bug #1: Enter combat in town square -> zero standing town shopkeepers
                Assert.That(renderer.ActiveNpcCount, Is.EqualTo(0));
                Assert.That(renderer.VisibleTownNpcCount, Is.EqualTo(0));
                ctrl.TriggerDemoSkirmish();
                Assert.That(ctrl.IsInCombat, Is.True);
                Assert.That(renderer.AreTownNpcsHiddenForCombat, Is.True);
                Assert.That(renderer.VisibleTownNpcCount, Is.EqualTo(0));

                // Bug #2: Combat speed timing checks (1x, 2x, Instant)
                Assert.That(ctrl.SpeedIndex, Is.EqualTo(0));
                Assert.That(ctrl.GetCombatMoveTileDuration(), Is.InRange(0.22f, 0.28f));
                Assert.That(ctrl.GetCombatAttackDuration(), Is.InRange(0.35f, 0.45f));
                typeof(BywaterGameController).GetMethod("HandleCycleSpeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(ctrl, null);
                Assert.That(ctrl.GetCombatMoveTileDuration(), Is.InRange(0.11f, 0.14f));
                Assert.That(ctrl.GetCombatAttackDuration(), Is.InRange(0.17f, 0.23f));
                typeof(BywaterGameController).GetMethod("HandleCycleSpeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(ctrl, null);
                Assert.That(ctrl.GetCombatMoveTileDuration(), Is.EqualTo(0.0f));
                Assert.That(ctrl.GetCombatAttackDuration(), Is.EqualTo(0.0f));
                typeof(BywaterGameController).GetMethod("HandleCycleSpeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(ctrl, null);
                Assert.That(ctrl.SpeedIndex, Is.EqualTo(0));

                // M1.5 Bug #3 update: Red enemy intent arcs and telegraphs are intentionally hidden on enemy hover.
                CombatantState activeHero = ctrl.CombatSession.CurrentActor;
                CombatantState firstMonster = null;
                for (int i = 0; i < ctrl.CombatSession.Combatants.Count; i++)
                {
                    if (ctrl.CombatSession.Combatants[i].Side == CombatSide.Monster && ctrl.CombatSession.Combatants[i].IsAlive)
                    {
                        firstMonster = ctrl.CombatSession.Combatants[i];
                        break;
                    }
                }
                Assert.That(activeHero, Is.Not.Null);
                Assert.That(firstMonster, Is.Not.Null);
                typeof(BywaterGameController).GetMethod("UpdateCombatHoverVisuals", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { firstMonster.Position });
                Assert.That(overlays.ActiveIntentArrowheadCount, Is.EqualTo(0), "Enemy intent arcs must remain hidden on enemy hover.");

                // Bug #4: Dead combatant flat fallen corpse with ZWrite Off & renderQueue strictly beneath living units
                var viewsField = typeof(BywaterGameController).GetField("_combatUnitViews", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var viewDict = viewsField?.GetValue(ctrl) as Dictionary<string, BywaterUnitView>;
                Assert.That(viewDict, Is.Not.Null);
                Assert.That(viewDict.TryGetValue(firstMonster.Id, out BywaterUnitView monView), Is.True);
                firstMonster.Hp = 0;
                typeof(BywaterGameController).GetMethod("SyncAllCombatUnitViews", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(ctrl, null);
                Assert.That(monView.IsCorpseSettled, Is.True);
                Assert.That(monView.SpriteMeshRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(monView.SpriteMeshRenderer.sortingOrder, Is.LessThan(0));
                Assert.That(monView.DeadCorpseRenderQueue, Is.EqualTo((int)RenderQueue.Transparent - 50), "Fallen corpse must render cleanly after Terrain with ZWrite Off.");
                Assert.That(monView.SpriteMeshRenderer.sharedMaterial.GetFloat("_ZWrite"), Is.EqualTo(0f));
                Transform sq = monView.transform.Find("SpriteBillboard/SpriteQuad");
                Assert.That(sq, Is.Not.Null);
                Assert.That(sq.localEulerAngles.x, Is.InRange(85f, 95f), "Fallen corpse quad must lie horizontally on the ground.");
                Assert.That(monView.transform.Find("SpriteBillboard").GetComponent<Realmz.Rendering.PixelBillboard>().enabled, Is.False);

                // Verify clean restoration if unit view returns to alive state
                monView.UpdateStats(firstMonster.MaxHp, firstMonster.MaxHp, 0, 0, string.Empty, false, isAlive: true);
                Assert.That(monView.SpriteMeshRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
                Assert.That(monView.SpriteMeshRenderer.sortingOrder, Is.EqualTo(0));
                Assert.That(monView.transform.Find("SpriteBillboard").GetComponent<Realmz.Rendering.PixelBillboard>().enabled, Is.True);

                // Bug #5: Melee toggle OFF, [Esc] cancel without pause menu, cancel method, and smart click empty tile to move
                if (ctrl.CombatSession.CurrentActor.Side != CombatSide.Hero)
                {
                    ctrl.CombatSession.EndTurn();
                }
                CombatantState heroActor = ctrl.CombatSession.CurrentActor;
                Assert.That(heroActor.Side, Is.EqualTo(CombatSide.Hero));
                ctrl.SelectCombatModeForUi(CombatActionMode.Melee);
                Assert.That(ctrl.ActiveCombatMode, Is.EqualTo(CombatActionMode.Melee));
                Assert.That(hud.IsTargetingBannerVisible, Is.True);
                Assert.That(hud.TargetingBannerText, Does.Contain("Melee Mode"));
                ctrl.SelectCombatModeForUi(CombatActionMode.Melee);
                Assert.That(ctrl.ActiveCombatMode, Is.EqualTo(CombatActionMode.Smart));
                Assert.That(hud.IsTargetingBannerVisible, Is.False);

                ctrl.SelectCombatModeForUi(CombatActionMode.Melee);
                ctrl.EscapeKeyForTest();
                Assert.That(ctrl.ActiveCombatMode, Is.EqualTo(CombatActionMode.Smart));
                Assert.That(hud.IsSettingsOpen, Is.False, "[Esc] from Melee mode must cancel targeting instead of opening Pause menu.");

                ctrl.SelectCombatModeForUi(CombatActionMode.Melee);
                ctrl.CancelCombatTargetingMode();
                Assert.That(ctrl.ActiveCombatMode, Is.EqualTo(CombatActionMode.Smart));

                IReadOnlyList<ReachableTileInfo> heroReach = ctrl.CombatSession.GetReachableTiles(heroActor.Id);
                GridCoord emptyReachable = heroActor.Position;
                for (int r = 0; r < heroReach.Count; r++)
                {
                    GridCoord candidate = heroReach[r].Coord;
                    bool occupied = false;
                    for (int c = 0; c < ctrl.CombatSession.Combatants.Count; c++)
                    {
                        CombatantState unit = ctrl.CombatSession.Combatants[c];
                        if (unit.IsAlive && unit.Position == candidate)
                        {
                            occupied = true;
                            break;
                        }
                    }
                    if (candidate != heroActor.Position && !occupied)
                    {
                        emptyReachable = candidate;
                        break;
                    }
                }
                Assert.That(emptyReachable, Is.Not.EqualTo(heroActor.Position));
                ctrl.SelectCombatModeForUi(CombatActionMode.Melee);
                ctrl.ClickCombatCoordForTest(emptyReachable);
                Assert.That(heroActor.Position, Is.EqualTo(emptyReachable), "Smart-clicking an empty reachable tile in Melee mode must move the hero.");
                Assert.That(ctrl.ActiveCombatMode, Is.EqualTo(CombatActionMode.Smart), "Smart-clicking an empty reachable tile in Melee mode must auto-exit Melee mode.");

                // Bug #1 exit combat -> exploration view cleanly restored
                typeof(BywaterGameController).GetMethod("ExitCombatToExploration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(ctrl, new object[] { false });
                Assert.That(ctrl.IsInCombat, Is.False);
                Assert.That(renderer.AreTownNpcsHiddenForCombat, Is.False);
                Assert.That(renderer.VisibleTownNpcCount, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(worldGo);
                Object.DestroyImmediate(ctrlGo);
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(overlaysGo);
                if (!hadEs)
                {
                    EventSystem created = Object.FindFirstObjectByType<EventSystem>();
                    if (created != null) Object.DestroyImmediate(created.gameObject);
                }
            }
        }

        [Test]
        public void UxOverhaul_PaperDoll_CampIcons_LevelUpModal_SaveV3_And_RichVictoryScreen_MeetContracts()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "bywater_m1"));
            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession session = new ExplorationSession(bundle.Maps, "land_0", party);

            // Verify 12 exported item & hero stat fixes on StreamingAssets bundle.
            Assert.That(bundle.Items[452].Slot, Is.EqualTo("Hands"));
            Assert.That(bundle.Items[454].Slot, Is.EqualTo("Feet"));
            Assert.That(bundle.Items[455].Slot, Is.EqualTo("Feet"));
            Assert.That(bundle.Items[250].Slot, Is.EqualTo("Accessory2"));
            Assert.That(bundle.Items[622].Slot, Is.EqualTo("Accessory1"));
            Assert.That(bundle.Items[675].Slot, Is.EqualTo("Accessory2"));
            Assert.That(bundle.Items[603].Category, Is.EqualTo("Potion"));
            Assert.That(bundle.Items[747].Category, Is.EqualTo("Supply"));
            Assert.That(bundle.Items[748].Category, Is.EqualTo("Supply"));
            Assert.That(bundle.Items[102].Name, Is.EqualTo("Sparring Quarter Staff"));
            Assert.That(bundle.Items[2].ArmorClassBonus, Is.EqualTo(2));
            Assert.That(bundle.Party[1].AttacksPerRound, Is.EqualTo(1.0)); // Liora fixed from 49.0

            // Stacked starter consumables (4x Heal Small Wounds, 2x Torch)
            party.AddItemToStash(452, 1);
            party.AddItemToStash(455, 1);
            party.AddItemToStash(250, 1);
            party.AddItemToStash(622, 1);
            Assert.That(party.EquipBestForHero(0, bundle.Items), Is.GreaterThanOrEqualTo(4));

            GameObject hudGo = new GameObject("TempUxHudTest");
            bool hadEs = Object.FindFirstObjectByType<EventSystem>() != null;
            try
            {
                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);

                // Strict >= 15f font floor across ALL TMP labels in entire HUD (including new UX panels).
                TextMeshProUGUI[] allTexts = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < allTexts.Length; i++)
                {
                    Assert.That(allTexts[i].fontSize, Is.GreaterThanOrEqualTo(15f),
                        $"Font floor violated (<15pt) on {allTexts[i].name}: {allTexts[i].fontSize}");
                }

                // Header Camp Icon+Badge buttons exist in Header (NOT inside StashCampCol).
                Transform header = FindDescendantByPathSuffix(canvas.transform, "PartyDrawerModal/Header");
                Transform stashCol = FindDescendantByPathSuffix(canvas.transform, "PartyDrawerModal/Body/StashCampCol");
                Assert.That(header.Find("QuickBandageBtn"), Is.Not.Null);
                Assert.That(header.Find("FullRestBtn"), Is.Not.Null);
                Assert.That(stashCol.Find("CampBar"), Is.Null, "Old CampBar must be removed from Stash column.");

                // All 9 anatomical Paper-Doll slots exist cleanly with ZERO Unequip buttons.
                EquipmentSlot[] expectedSlots = (EquipmentSlot[])System.Enum.GetValues(typeof(EquipmentSlot));
                for (int i = 0; i < expectedSlots.Length; i++)
                {
                    Transform slotTf = FindDescendantByName(canvas.transform, $"PaperDollSlot_{expectedSlots[i]}");
                    Assert.That(slotTf, Is.Not.Null, $"Missing PaperDollSlot_{expectedSlots[i]}");
                    Assert.That(slotTf.Find("UnequipBtn"), Is.Null, $"Redundant UnequipBtn found on {expectedSlots[i]}");
                }

                // Refresh drawer + check zero row churn + XP/Level-up badge + Level-Up modal.
                party.Members[0].AwardXp(250);
                hud.RefreshExploreHud(session, bundle, null, null, 0, string.Empty, false);
                hud.TogglePartyPanel(true);
                int pooledBefore = hud.PooledStashRowCount;
                hud.RefreshPartyDrawer(party, bundle, 0);
                Assert.That(hud.PooledStashRowCount, Is.EqualTo(pooledBefore));

                Transform badge0 = FindDescendantByPathSuffix(canvas.transform, "HeroCard_0/PortraitFrame/LvlUpBadge");
                Assert.That(badge0 != null && badge0.gameObject.activeSelf, Is.True, "LVL UP! badge should show for Aldric.");

                bool confirmFired = false;
                hud.OnConfirmLevelUpRequested += (heroIdx, alloc, spellId) =>
                {
                    confirmFired = heroIdx == 0 && alloc.TotalPoints == 2;
                    party.Members[heroIdx].ApplyPendingLevelUp(alloc, spellId);
                };
                hud.OpenLevelUpModal(0);
                Assert.That(hud.IsLevelUpModalOpen, Is.True);
                Assert.That(hud.IsAnyModalOpen, Is.True);
                Transform confirmBtnTf = FindDescendantByPathSuffix(canvas.transform, "LevelUpModal/Footer/ConfirmLevelUpBtn");
                confirmBtnTf.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.That(confirmFired, Is.True);
                Assert.That(party.Members[0].Level, Is.EqualTo(2));

                // Save v4 round-trip preserves QuickBandageCharges, attributes, PendingLevelUps, KnownSpellIds, and FormationRank.
                party.QuickBandageCharges = 1;
                party.SetFormationRank(4, FormationRank.Front);
                party.Members[1].AwardXp(250);
                string tempSave = Path.Combine(Path.GetTempPath(), "realmz_v4_test.json");
                try
                {
                    BywaterSaveData snap = BywaterSaveService.CaptureSnapshot(session);
                    Assert.That(snap.version, Is.EqualTo(4));
                    Assert.That(BywaterSaveService.TryWriteSave(tempSave, snap), Is.True);
                    Assert.That(BywaterSaveService.TryLoadSave(tempSave, out BywaterSaveData loaded, out string _), Is.True);

                    ExplorationSession restored = new ExplorationSession(bundle.Maps, "land_0", PartyState.FromBundle(bundle));
                    BywaterSaveService.ApplySnapshot(loaded, restored, bundle.Items, out ulong _);
                    Assert.That(restored.Party.QuickBandageCharges, Is.EqualTo(1));
                    Assert.That(restored.Party.Members[0].Level, Is.EqualTo(2));
                    Assert.That(restored.Party.Members[1].PendingLevelUps, Is.EqualTo(1));
                    Assert.That(restored.Party.GetFormationRank(4), Is.EqualTo(FormationRank.Front));
                }
                finally
                {
                    if (File.Exists(tempSave)) File.Delete(tempSave);
                }

                // Rich 980x690 Victory screen renders all 6 hero XP cards & un-truncated stacked loot tiles.
                List<HeroVictorySummary> hv = new List<HeroVictorySummary>();
                for (int i = 0; i < party.Members.Count; i++)
                {
                    PartyMemberState pm = party.Members[i];
                    hv.Add(new HeroVictorySummary(
                        pm.Id, pm.Name, pm.Archetype, pm.IconId, pm.PortraitId,
                        pm.Level, pm.Level, System.Math.Max(0, pm.Xp - 60), pm.Xp, 60,
                        PartyMemberState.GetXpRequiredForLevel(pm.Level), pm.XpForNextLevel,
                        pm.HasPendingLevelUp, pm.PendingLevelUps, i == 5, 1, 20, 4, i == 0));
                }
                List<LootStackSummary> loot = new List<LootStackSummary>
                {
                    new LootStackSummary(452, "Leather Gloves", "Gloves", "Hands", 2, 0, 5, false),
                    new LootStackSummary(622, "Speckled Luck Stone +2", "Accessory", "Accessory1", 1, 0, 750, false),
                    new LootStackSummary(611, "Heal Small Wounds", "Potion", "Consumable", 3, 3, 50, false),
                    new LootStackSummary(250, "Padded Cloak", "Cloak", "Accessory2", 1, 0, 25, false),
                };
                CombatVictorySummary summary = new CombatVictorySummary(360, 40, 6, 46, party.Gold, 3, hv, loot);
                hud.ShowVictoryOutcome(summary, party, bundle);

                RectTransform outcomeRt = FindDescendantByName(canvas.transform, "OutcomeModal").GetComponent<RectTransform>();
                Assert.That(outcomeRt.sizeDelta, Is.EqualTo(new Vector2(980f, 690f)));
                Assert.That(FindDescendantByName(canvas.transform, "LootTile_3").gameObject.activeSelf, Is.True);
                Assert.That(FindDescendantByName(canvas.transform, "OutcomeSummary").gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
                if (!hadEs)
                {
                    EventSystem created = Object.FindFirstObjectByType<EventSystem>();
                    if (created != null) Object.DestroyImmediate(created.gameObject);
                }
            }
        }

        [Test]
        public void EncounterDirector_SaveV3PersistsClockGraceAndCooldowns_AndHandlesLegacySaves()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "bywater_m1"));
            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession session = new ExplorationSession(
                bundle.Maps,
                "land_0",
                party,
                new DeterministicDiceRoller(42UL),
                startPosition: new GridCoord(41, 54),
                initialWorldClockTurns: 58);

            // Trigger wilderness cooldown and post-combat grace steps
            IReadOnlyList<EncounterZoneData> zonesAtBank = session.CurrentMap.GetZonesAt(41, 54);
            Assert.That(zonesAtBank.Count, Is.GreaterThan(0));
            session.CompleteEncounterZone(zonesAtBank[0], graceSteps: 10);
            Assert.That(session.PostCombatGraceStepsRemaining, Is.EqualTo(10));
            Assert.That(session.ZoneCooldownUntilTurns.Count, Is.EqualTo(1));

            string tempSave = Path.Combine(Path.GetTempPath(), "realmz_encounter_v3_test.json");
            try
            {
                BywaterSaveData snap = BywaterSaveService.CaptureSnapshot(session);
                Assert.That(snap.hasWorldClock, Is.True);
                Assert.That(snap.worldTimeTurns, Is.EqualTo(58));
                Assert.That(snap.hasWorldClockMinutes, Is.True);
                Assert.That(snap.worldTimeMinutes, Is.EqualTo(WorldClock.LegacyTurnsToMinutes(58)).Within(0.01));
                Assert.That(snap.postCombatGraceSteps, Is.EqualTo(10));
                Assert.That(snap.zoneCooldowns.Length, Is.EqualTo(1));
                Assert.That(BywaterSaveService.TryWriteSave(tempSave, snap), Is.True);

                Assert.That(BywaterSaveService.TryLoadSave(tempSave, out BywaterSaveData loaded, out string _), Is.True);
                ExplorationSession restored = new ExplorationSession(bundle.Maps, "land_0", PartyState.FromBundle(bundle));
                BywaterSaveService.ApplySnapshot(loaded, restored, bundle.Items, out ulong _);

                Assert.That(restored.WorldClockTurns, Is.EqualTo(58));
                Assert.That(restored.WorldClockMinutes, Is.EqualTo(WorldClock.LegacyTurnsToMinutes(58)).Within(0.01));
                Assert.That(restored.CurrentWorldClock.Phase, Is.EqualTo(WorldTimePhase.Night));
                Assert.That(restored.CurrentWorldClock.DisplayLabel, Is.EqualTo("Day 1 - 08:29 PM (Night)"));
                Assert.That(restored.PostCombatGraceStepsRemaining, Is.EqualTo(10));
                Assert.That(restored.IsEncounterZoneOnCooldown("land_0", zonesAtBank[0]), Is.True);

                // Backward compatibility with legacy saves missing world clock fields (worldTimeTurns = -1, hasWorldClock = false)
                BywaterSaveData legacyData = new BywaterSaveData
                {
                    version = 2,
                    mapId = "land_0",
                    x = 10,
                    y = 9,
                    turnCount = 7,
                    hasWorldClock = false,
                    worldTimeTurns = -1,
                    hasWorldClockMinutes = false,
                    worldTimeMinutes = -1.0
                };
                Assert.That(BywaterSaveService.ResolveRestoredWorldClockTurns(legacyData),
                    Is.EqualTo(WorldClock.DefaultStartTurns + 7));
                Assert.That(BywaterSaveService.ResolveRestoredWorldClockMinutes(legacyData),
                    Is.EqualTo(WorldClock.LegacyTurnsToMinutes(WorldClock.DefaultStartTurns + 7)).Within(0.01));
            }
            finally
            {
                if (File.Exists(tempSave)) File.Delete(tempSave);
            }
        }

        [Test]
        public void EncounterDirector_AtmospherePhasesAndAldricTorchlight_ToggleOnNightAndDark()
        {
            AmbientMode prevMode = RenderSettings.ambientMode;
            Color prevSky = RenderSettings.ambientSkyColor;
            Color prevEquator = RenderSettings.ambientEquatorColor;
            Color prevGround = RenderSettings.ambientGroundColor;

            GameObject camGo = new GameObject("EdCam", typeof(Camera));
            GameObject sunGo = new GameObject("EdSun", typeof(Light));
            GameObject volGo = new GameObject("EdVol", typeof(Volume));
            GameObject atmoGo = new GameObject("EdAtmo");
            GameObject worldGo = new GameObject("EdWorld");
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();

            try
            {
                camGo.tag = "MainCamera";
                Light sun = sunGo.GetComponent<Light>();
                sun.type = LightType.Directional;
                Volume vol = volGo.GetComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;

                BywaterBiomeAtmosphere atmo = atmoGo.AddComponent<BywaterBiomeAtmosphere>();
                atmo.targetCamera = camGo.GetComponent<Camera>();
                atmo.directionalSun = sun;
                atmo.globalVolume = vol;

                atmo.ApplyBiome(false, WorldTimePhase.Dawn, 0.5f, false, immediate: true);
                float dawnIntensity = sun.intensity;
                atmo.ApplyBiome(false, WorldTimePhase.Day, 0.5f, false, immediate: true);
                float dayIntensity = sun.intensity;
                atmo.ApplyBiome(false, WorldTimePhase.Dusk, 0.5f, false, immediate: true);
                float duskIntensity = sun.intensity;
                atmo.ApplyBiome(false, WorldTimePhase.Night, 0.5f, true, immediate: true);
                float nightIntensity = sun.intensity;

                Assert.That(dayIntensity, Is.GreaterThan(dawnIntensity));
                Assert.That(dawnIntensity, Is.GreaterThan(duskIntensity));
                Assert.That(nightIntensity, Is.LessThan(duskIntensity));
                Assert.That(RenderSettings.ambientSkyColor.b, Is.GreaterThan(RenderSettings.ambientSkyColor.r));

                BywaterWorldRenderer renderer = worldGo.AddComponent<BywaterWorldRenderer>();
                BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
                renderer.Initialize(catalog);

                // Day: party lantern remains off even if carrying torches
                renderer.SetPartyTorchPosition(Vector3.zero, isDungeon: false, isNightOrDark: false, hasLitTorch: true);
                Assert.That(renderer.IsPartyLanternEnabled, Is.False);

                // Night without lit torch: party lantern off
                renderer.SetPartyTorchPosition(Vector3.zero, isDungeon: false, isNightOrDark: true, hasLitTorch: false);
                Assert.That(renderer.IsPartyLanternEnabled, Is.False);

                // Night with lit torch: warm Aldric torchlight enabled (> 8.0 intensity)
                renderer.SetPartyTorchPosition(Vector3.zero, isDungeon: false, isNightOrDark: true, hasLitTorch: true);
                Assert.That(renderer.IsPartyLanternEnabled, Is.True);
                Assert.That(renderer.PartyLanternIntensity, Is.GreaterThan(8.0f));
            }
            finally
            {
                Object.DestroyImmediate(worldGo);
                Object.DestroyImmediate(atmoGo);
                Object.DestroyImmediate(volGo);
                Object.DestroyImmediate(sunGo);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(profile);
                RenderSettings.ambientMode = prevMode;
                RenderSettings.ambientSkyColor = prevSky;
                RenderSettings.ambientEquatorColor = prevEquator;
                RenderSettings.ambientGroundColor = prevGround;
            }
        }

        [Test]
        public void EncounterDirector_HudClockThreatPills_InterceptModalAndBug7ScrollLock()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "bywater_m1"));
            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession session = new ExplorationSession(
                bundle.Maps,
                "land_0",
                party,
                new DeterministicDiceRoller(9UL),
                startPosition: new GridCoord(41, 54),
                initialWorldClockTurns: 58); // Night

            GameObject hudGo = new GameObject("TempInterceptHud");
            GameObject camGo = new GameObject("TempInterceptCam", typeof(Camera));
            bool hadEs = Object.FindFirstObjectByType<EventSystem>() != null;
            try
            {
                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
                BywaterCameraRig rig = camGo.AddComponent<BywaterCameraRig>();
                rig.SetHudController(hud);

                // Night with unlit torch shows DARK DANGER! threat pill & Night clock pill
                party.TorchTurnsRemaining = 0;
                hud.RefreshExploreHud(session, bundle, null, null, 0, string.Empty, false);
                TextMeshProUGUI clockTxt = FindDescendantByName(canvas.transform, "ClockText").GetComponent<TextMeshProUGUI>();
                TextMeshProUGUI threatTxt = FindDescendantByName(canvas.transform, "ThreatText").GetComponent<TextMeshProUGUI>();
                Assert.That(clockTxt.text, Is.EqualTo(session.CurrentWorldClock.DisplayLabel));
                Assert.That(clockTxt.text, Is.EqualTo("Day 1 - 08:29 PM (Night)"));
                Assert.That(threatTxt.text, Is.EqualTo("DARK DANGER!"));

                // Lighting torch resolves DARK DANGER! -> HIGH / MODERATE
                party.TorchTurnsRemaining = 24;
                hud.RefreshExploreHud(session, bundle, null, null, 0, string.Empty, false);
                Assert.That(threatTxt.text, Is.Not.EqualTo("DARK DANGER!"));

                // Show Encounter Intercept Modal -> Bug #7 scroll lock engaged + correct buttons
                EncounterZoneData zone = session.CurrentMap.GetZonesAt(41, 54)[0];
                EncounterGeographyInfo geo = session.GetCurrentGeography();
                List<MonsterDefinition> pack = EncounterDirector.BuildEcologyMonsterPack(
                    session.CurrentMap, session.Position, zone, geo, session.CurrentWorldClock, bundle.Monsters, session.Roller);
                EncounterScoutingReport report = session.CreateScoutingReport(zone, pack);

                hud.ShowEncounterInterceptModal(report, canQuickResolve: false);
                Assert.That(hud.IsEncounterInterceptOpen, Is.True);
                Assert.That(hud.IsModalOpen, Is.True);
                Assert.That(rig.ApplyScrollZoomDelta(180f), Is.EqualTo(0f), "Bug #7: zoom must lock while Encounter Intercept is open.");
                Assert.That(rig.ApplyKeyboardPanInput(Vector2.up, false, 0.2f), Is.EqualTo(Vector3.zero), "Bug #7: pan must lock while Intercept open.");

                // Dark Ambush disables Evade and labels primary action Defend Yourself
                party.TorchTurnsRemaining = 0;
                EncounterScoutingReport darkReport = session.CreateScoutingReport(zone, pack);
                Assert.That(darkReport.IsAmbushedInDark, Is.True);
                hud.ShowEncounterInterceptModal(darkReport, canQuickResolve: true, quickResolveRatioText: "3.0x");
                UnityEngine.UI.Button evadeBtn = FindDescendantByName(canvas.transform, "InterceptEvadeBtn").GetComponent<UnityEngine.UI.Button>();
                UnityEngine.UI.Button engageBtn = FindDescendantByName(canvas.transform, "InterceptEngageBtn").GetComponent<UnityEngine.UI.Button>();
                Assert.That(evadeBtn.interactable, Is.False);
                Assert.That(engageBtn.GetComponentInChildren<TextMeshProUGUI>(true).text, Is.EqualTo("Defend Yourself"));
                Assert.That(FindDescendantByName(canvas.transform, "InterceptQuickResolveBtn").gameObject.activeSelf, Is.False);

                bool engaged = false;
                hud.OnEncounterEngageRequested += () => engaged = true;
                engageBtn.onClick.Invoke();
                Assert.That(engaged, Is.True);
                Assert.That(hud.IsEncounterInterceptOpen, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(hudGo);
                if (!hadEs)
                {
                    EventSystem created = Object.FindFirstObjectByType<EventSystem>();
                    if (created != null) Object.DestroyImmediate(created.gameObject);
                }
            }
        }

        [Test]
        public void W249_GoldCoinsIcon_NightCombatLighting_ForegroundClear_And_Option2VictoryLoot_MeetContracts()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "bywater_m1"));

            // 1. 32x32 Point Gold Coins icon exists cleanly without inflating 21 hotbar icon contract
            Assert.That(catalog.hotbarIcons.Count, Is.EqualTo(21));
            string coinPath = "Assets/Art/Cartography/Icons/icon_gold_coins.png";
            Texture2D coinTex = AssetDatabase.LoadAssetAtPath<Texture2D>(coinPath);
            Assert.That(coinTex, Is.Not.Null, "icon_gold_coins.png missing at " + coinPath);
            Assert.That(coinTex.width, Is.EqualTo(32));
            Assert.That(coinTex.height, Is.EqualTo(32));
            TextureImporter coinImporter = AssetImporter.GetAtPath(coinPath) as TextureImporter;
            Assert.That(coinImporter, Is.Not.Null);
            Assert.That(coinImporter.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(coinImporter.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(catalog.GetHotbarIcon("gold_coins") ?? BywaterUiHelpers.GetProceduralIconSprite("gold_coins"), Is.Not.Null);

            // 2. Night Combat lighting preserves legible cool moonlight floor + party-side Torchlight
            AmbientMode prevMode = RenderSettings.ambientMode;
            Color prevSky = RenderSettings.ambientSkyColor;
            Color prevEquator = RenderSettings.ambientEquatorColor;
            Color prevGround = RenderSettings.ambientGroundColor;
            GameObject camGo = new GameObject("W249Cam", typeof(Camera));
            GameObject sunGo = new GameObject("W249Sun", typeof(Light));
            GameObject volGo = new GameObject("W249Vol", typeof(Volume));
            GameObject atmoGo = new GameObject("W249Atmo");
            GameObject worldGo = new GameObject("W249World");
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Tonemapping tm = profile.Add<Tonemapping>(true);

            bool hadEs = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject hudGo = new GameObject("W249Hud");
            try
            {
                camGo.GetComponent<Camera>().tag = "MainCamera";
                Light sun = sunGo.GetComponent<Light>();
                sun.type = LightType.Directional;
                volGo.GetComponent<Volume>().sharedProfile = profile;
                volGo.GetComponent<Volume>().isGlobal = true;

                BywaterBiomeAtmosphere atmo = atmoGo.AddComponent<BywaterBiomeAtmosphere>();
                atmo.targetCamera = camGo.GetComponent<Camera>();
                atmo.directionalSun = sun;
                atmo.globalVolume = volGo.GetComponent<Volume>();

                atmo.ApplyBiome(false, WorldTimePhase.Night, 0.5f, hasLitTorch: false, immediate: true, isCombat: true);
                Assert.That(atmo.IsCombatActive, Is.True);
                Assert.That(sun.intensity, Is.InRange(0.65f, 0.85f), "Night Combat sun key must stay within chiaroscuro [0.65, 0.85].");
                Assert.That(RenderSettings.ambientIntensity, Is.InRange(0.95f, 1.12f), "Night Combat chiaroscuro ambient floor must stay within [0.95, 1.12].");
                Assert.That(RenderSettings.ambientSkyColor.b, Is.GreaterThan(RenderSettings.ambientSkyColor.r), "Night Combat must retain cool silver-blue tone.");
                Assert.That(tm.mode.value, Is.EqualTo(TonemappingMode.ACES));
                Assert.That(Shader.GetGlobalFloat("_GlobalNightRimStrength"), Is.GreaterThanOrEqualTo(0.50f));
                Assert.That(atmo.CurrentNightRimStrength, Is.GreaterThanOrEqualTo(0.50f));

                // 3. Combat Foreground Clear Buffer hides non-arena props in arena + 5-tile south buffer & restores cleanly
                TileMapData landMap = bundle.Maps["land_0"];
                BywaterWorldRenderer renderer = worldGo.AddComponent<BywaterWorldRenderer>();
                renderer.Initialize(catalog);
                renderer.RenderMap(landMap, new GridCoord(44, 52), forceRebuild: true);

                int originX = 38;
                int originY = 48;
                int arenaW = 12;
                int arenaH = 8;
                renderer.ClearCombatForegroundProps(originX, originY, arenaW, arenaH);
                Assert.That(renderer.IsCombatForegroundClearActive, Is.True);
                Assert.That(renderer.HiddenCombatForegroundPropCount, Is.GreaterThan(0),
                    "Should hide overworld forest trees in southern 5-tile buffer + arena.");

                int minClearX = originX - 2;
                int maxClearX = originX + arenaW - 1 + 2;
                int minClearY = originY;
                int maxClearY = originY + arenaH - 1 + 5;
                foreach (Transform child in renderer.PropsRoot)
                {
                    if (!child.gameObject.activeSelf) continue;
                    MeshFilter mf = child.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Cartography_RuntimeGroundQuad")
                    {
                        continue;
                    }
                    CartographyWorldData.WorldToGrid(child.position, out int px, out int py);
                    bool inBuffer = px >= minClearX && px <= maxClearX && py >= minClearY && py <= maxClearY;
                    Assert.That(inBuffer, Is.False, $"Non-arena prop at ({px},{py}) remained visible inside Combat Foreground Clear Buffer.");
                }

                renderer.SetCombatArenaFrame(0, 0, 0, 0, false);
                Assert.That(renderer.IsCombatForegroundClearActive, Is.False);
                Assert.That(renderer.HiddenCombatForegroundPropCount, Is.EqualTo(0));

                // 4. Option 2 Victory & Loot Screen: zero MVP, gold arrow & revive badges, Loot header, gold coin icon, self-contained cards, Take Selected (X)
                PartyState party = PartyState.FromBundle(bundle);
                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas canvas = hud.GetComponentInChildren<Canvas>(true);

                party.Members[0].AwardXp(250); // Aldric ready to level up
                List<HeroVictorySummary> hv = new List<HeroVictorySummary>();
                for (int i = 0; i < party.Members.Count; i++)
                {
                    PartyMemberState pm = party.Members[i];
                    hv.Add(new HeroVictorySummary(
                        pm.Id, pm.Name, pm.Archetype, pm.IconId, pm.PortraitId,
                        pm.Level, pm.Level, System.Math.Max(0, pm.Xp - 60), pm.Xp, 60,
                        PartyMemberState.GetXpRequiredForLevel(pm.Level), pm.XpForNextLevel,
                        pm.HasPendingLevelUp, pm.PendingLevelUps,
                        wasFallenRevived: i == 5, kills: i == 4 ? 2 : 0, damageDealt: 20, healingDone: 4, isMvp: i == 4));
                }

                List<LootStackSummary> loot = new List<LootStackSummary>
                {
                    new LootStackSummary(10, bundle.Items[10].Name, bundle.Items[10].Category, bundle.Items[10].Slot, 1, 0, bundle.Items[10].BasePrice, false),
                    new LootStackSummary(418, bundle.Items[418].Name, bundle.Items[418].Category, bundle.Items[418].Slot, 1, 0, bundle.Items[418].BasePrice, false),
                    new LootStackSummary(622, bundle.Items[622].Name, bundle.Items[622].Category, bundle.Items[622].Slot, 1, 0, bundle.Items[622].BasePrice, false),
                    new LootStackSummary(611, bundle.Items[611].Name, bundle.Items[611].Category, bundle.Items[611].Slot, 2, 3, bundle.Items[611].BasePrice, false)
                };
                CombatVictorySummary summary = new CombatVictorySummary(240, 22, 2, 24, party.Gold, 2, hv, loot);
                hud.ShowVictoryOutcome(summary, party, bundle);

                Transform outcomeModal = FindDescendantByName(canvas.transform, "OutcomeModal");
                Assert.That(FindDescendantByName(outcomeModal, "TakeAndEquipBtn"), Is.Null, "Option 2 must remove Equip Upgrades button.");
                Assert.That(FindDescendantByName(outcomeModal, "VictoryLootHeader").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("Loot"));
                Assert.That(FindDescendantByName(outcomeModal, "GoldIcon").GetComponent<UnityEngine.UI.Image>().sprite, Is.Not.Null);

                // Zero MVP, LEVEL UP!, or Revived @ 1 HP text banners; Crisp icon badges active
                TextMeshProUGUI[] outcomeTexts = outcomeModal.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < outcomeTexts.Length; i++)
                {
                    if (!outcomeTexts[i].gameObject.activeInHierarchy) continue;
                    Assert.That(outcomeTexts[i].fontSize, Is.GreaterThanOrEqualTo(15f));
                    Assert.That(outcomeTexts[i].text, Does.Not.Contain("MVP"));
                    Assert.That(outcomeTexts[i].text, Does.Not.Contain("Revived @ 1 HP"));
                    Assert.That(outcomeTexts[i].text, Is.Not.EqualTo("LEVEL UP!"));
                }

                Assert.That(FindDescendantByPathSuffix(outcomeModal, "VictoryHero_0/Right/Top/LvlUpArrowBadge").gameObject.activeSelf, Is.True);
                Assert.That(FindDescendantByPathSuffix(outcomeModal, "VictoryHero_5/Right/Top/ReviveBadgeIcon").gameObject.activeSelf, Is.True);

                // Loot card 2 ("Speckled Luck Stone +2"): untruncated full name, inline stats, green upgrade hero Vex
                Transform card2 = FindDescendantByName(outcomeModal, "LootTile_2");
                Assert.That(FindDescendantByName(card2, "Name").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("Speckled Luck Stone +2"));
                Assert.That(FindDescendantByName(card2, "Stats").GetComponent<TextMeshProUGUI>().text, Does.Contain("+2 AC"));
                Assert.That(FindDescendantByName(card2, "UpgRow").gameObject.activeSelf, Is.True);
                Assert.That(FindDescendantByName(card2, "UpgHero").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("Vex"));

                // Loot card 1 ("Shield"): all shield users already have shields, non-shield classes cannot equip -> UpgRow hidden!
                Transform card1 = FindDescendantByName(outcomeModal, "LootTile_1");
                Assert.That(FindDescendantByName(card1, "UpgRow").gameObject.activeSelf, Is.False,
                    "Shield must not tag incompatible classes as upgrade.");

                // All 5 loot items selected by default (1+1+1+2 = 5) -> deselect Shield (card 1) -> Take Selected (4)
                TextMeshProUGUI contTxt = FindDescendantByName(outcomeModal, "ContinueBtn").GetComponentInChildren<TextMeshProUGUI>();
                Assert.That(contTxt.text, Is.EqualTo("Take Selected (5)"));
                card1.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.That(contTxt.text, Is.EqualTo("Take Selected (4)"));

                int[] takenIndices = null;
                hud.OnVictoryTakeLootRequested += arr => takenIndices = arr;
                FindDescendantByName(outcomeModal, "ContinueBtn").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.That(takenIndices, Is.EqualTo(new[] { 0, 2, 3 }));
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(worldGo);
                Object.DestroyImmediate(atmoGo);
                Object.DestroyImmediate(volGo);
                Object.DestroyImmediate(sunGo);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(profile);
                RenderSettings.ambientMode = prevMode;
                RenderSettings.ambientSkyColor = prevSky;
                RenderSettings.ambientEquatorColor = prevEquator;
                RenderSettings.ambientGroundColor = prevGround;
                if (!hadEs)
                {
                    EventSystem created = Object.FindFirstObjectByType<EventSystem>();
                    if (created != null) Object.DestroyImmediate(created.gameObject);
                }
            }
        }

        [Test]
        public void W253_Compact4AcrossLoot_FormationUi_DeploymentBanner_And_ApHudContractsHold()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            PartyState party = PartyState.FromBundle(bundle);

            bool hadEs = Object.FindFirstObjectByType<EventSystem>() != null;
            GameObject hudGo = new GameObject("W253HudTest");
            try
            {
                BywaterHudController hud = hudGo.AddComponent<BywaterHudController>();
                hud.BuildUi(catalog);
                Canvas canvas = hud.GetComponentInChildren<Canvas>(true);

                // 1. Party [P] Formation rank buttons + Iron Vanguard set progress in CharSheet
                hud.TogglePartyPanel(true);
                hud.RefreshPartyDrawer(party, bundle, 0);
                Transform charExtra = FindDescendantByName(canvas.transform, "ExtraBody");
                Assert.That(charExtra, Is.Not.Null);
                Assert.That(charExtra.GetComponent<TextMeshProUGUI>().text, Does.Contain("Iron Vanguard [3/3]"));

                FormationRank changedRank = FormationRank.Front;
                int changedIdx = -1;
                hud.OnHeroFormationChanged += (idx, rank) =>
                {
                    changedIdx = idx;
                    changedRank = rank;
                    party.SetFormationRank(idx, rank);
                };
                FindDescendantByName(canvas.transform, "FormationRankBtn_Back").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.That(changedIdx, Is.EqualTo(0));
                Assert.That(changedRank, Is.EqualTo(FormationRank.Back));
                hud.TogglePartyPanel(false);

                // 2. Pre-Battle Deployment banner + Start Battle CTA toggle cleanly vs TacticalActionBar + AP Diamonds
                CombatGrid grid = new CombatGrid(12, 10);
                List<CombatantState> units = new List<CombatantState>
                {
                    CombatantState.FromHero(party.Members[0], new GridCoord(2, 4), bundle.Items),
                    new CombatantState("m1", "Slow Goblin", CombatSide.Monster, new GridCoord(8, 4), 20, 20, dexterity: 4)
                };
                TacticalCombatSession session = new TacticalCombatSession(
                    grid, units, bundle.Spells, bundle.Items, party: party, startInDeployment: true);
                hud.RefreshCombatHud(session, bundle, CombatActionMode.Smart, -1, "1x");

                Transform deployBanner = FindDescendantByName(canvas.transform, "DeploymentBanner");
                Transform actionBar = FindDescendantByName(canvas.transform, "TacticalActionBar");
                Assert.That(deployBanner.gameObject.activeSelf, Is.True);
                Assert.That(actionBar.gameObject.activeSelf, Is.False);

                Assert.That(session.StartBattleFromDeployment(), Is.True);
                hud.RefreshCombatHud(session, bundle, CombatActionMode.Smart, -1, "1x");
                Assert.That(deployBanner.gameObject.activeSelf, Is.False);
                Assert.That(actionBar.gameObject.activeSelf, Is.True);
                Assert.That(FindDescendantByName(actionBar, "ApValueLabel").GetComponent<TextMeshProUGUI>().text, Does.Contain("MP"));
                Assert.That(FindDescendantByName(actionBar, "DashBtn"), Is.Not.Null);
                Assert.That(FindDescendantByPathSuffix(actionBar, "GuardBtn/TopRow/ApBadge/ApBadgeLabel").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("Bonus"));
                Assert.That(FindDescendantByPathSuffix(actionBar, "HotbarSlot_0/TopRow/CostCol/ApCost").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("Action"));

                // 3. Compact 4-across Victory Loot grid (216x72, constraintCount == 4, 12 items = 3 visible rows)
                List<HeroVictorySummary> hv = new List<HeroVictorySummary>();
                for (int i = 0; i < party.Members.Count; i++)
                {
                    PartyMemberState pm = party.Members[i];
                    hv.Add(new HeroVictorySummary(
                        pm.Id, pm.Name, pm.Archetype, pm.IconId, pm.PortraitId,
                        pm.Level, pm.Level, pm.Xp, pm.Xp + 40, 40,
                        PartyMemberState.GetXpRequiredForLevel(pm.Level), pm.XpForNextLevel,
                        false, 0, false, 0, 10, 0, false));
                }
                int[] itemIds = { 10, 418, 622, 611, 250, 108, 450, 452, 454, 455, 209, 20 };
                List<LootStackSummary> loot = new List<LootStackSummary>();
                for (int i = 0; i < itemIds.Length; i++)
                {
                    ItemDefinition item = bundle.Items[itemIds[i]];
                    loot.Add(new LootStackSummary(item.Id, item.Name, item.Category, item.Slot, 1, item.Charges, item.BasePrice, false));
                }
                hud.ShowVictoryOutcome(new CombatVictorySummary(240, 20, 1, 20, party.Gold, 0, hv, loot), party, bundle);
                Transform lootScrollContent = FindDescendantByPathSuffix(canvas.transform, "VictoryLootScroll/Viewport/Content");
                UnityEngine.UI.GridLayoutGroup gridLayout = lootScrollContent.GetComponent<UnityEngine.UI.GridLayoutGroup>();
                Assert.That(gridLayout, Is.Not.Null);
                Assert.That(gridLayout.constraintCount, Is.EqualTo(4));
                Assert.That(gridLayout.cellSize.x, Is.LessThanOrEqualTo(222f));
                Assert.That(gridLayout.cellSize.y, Is.LessThanOrEqualTo(76f));

                TextMeshProUGUI[] allLabels = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < allLabels.Length; i++)
                {
                    Assert.That(allLabels[i].fontSize, Is.GreaterThanOrEqualTo(15f), $"Label '{allLabels[i].gameObject.name}' has fontSize < 15f.");
                }
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
                if (!hadEs)
                {
                    EventSystem created = Object.FindFirstObjectByType<EventSystem>();
                    if (created != null) Object.DestroyImmediate(created.gameObject);
                }
            }
        }

        [Test]
        public void Stage2_BalanceOverrides_MonsterHp_CatalogAsset_AndShopkeeperScale_VerifyContracts()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            MonsterDefinition orcWarrior = null;
            MonsterDefinition giantFrog = null;
            for (int i = 0; i < bundle.Monsters.Count; i++)
            {
                MonsterDefinition monster = bundle.Monsters[i];
                if (monster == null)
                {
                    continue;
                }
                if (string.Equals(monster.Name, "Orc Warrior", System.StringComparison.OrdinalIgnoreCase))
                {
                    orcWarrior = monster;
                }
                else if (string.Equals(monster.Name, "Giant Frog", System.StringComparison.OrdinalIgnoreCase))
                {
                    giantFrog = monster;
                }
            }

            Assert.That(orcWarrior, Is.Not.Null, "Orc Warrior missing from bundle.Monsters.");
            Assert.That(orcWarrior.HpMin, Is.EqualTo(18));
            Assert.That(orcWarrior.HpMax, Is.EqualTo(24));

            Assert.That(giantFrog, Is.Not.Null, "Giant Frog missing from bundle.Monsters.");
            Assert.That(giantFrog.HpMin, Is.EqualTo(15));
            Assert.That(giantFrog.HpMax, Is.EqualTo(19));

            RealmzBalanceCatalog balanceCatalog = Resources.Load<RealmzBalanceCatalog>("RealmzBalanceCatalog");
            Assert.That(balanceCatalog, Is.Not.Null, "Assets/Resources/RealmzBalanceCatalog.asset must load via Resources.Load.");
            Assert.That(balanceCatalog.survival, Is.Not.Null);
            Assert.That(balanceCatalog.survival.minutesPerRealSecond, Is.EqualTo(1.0f).Within(0.001f));

            bool foundOrcOverride = false;
            bool foundFrogOverride = false;
            for (int i = 0; i < balanceCatalog.monsters.Length; i++)
            {
                RealmzBalanceCatalog.MonsterBalanceEntry entry = balanceCatalog.monsters[i];
                if (string.Equals(entry.name, "Orc Warrior", System.StringComparison.OrdinalIgnoreCase))
                {
                    foundOrcOverride = true;
                    Assert.That(entry.hpMin, Is.EqualTo(18));
                    Assert.That(entry.hpMax, Is.EqualTo(24));
                }
                else if (string.Equals(entry.name, "Giant Frog", System.StringComparison.OrdinalIgnoreCase))
                {
                    foundFrogOverride = true;
                    Assert.That(entry.hpMin, Is.EqualTo(15));
                    Assert.That(entry.hpMax, Is.EqualTo(19));
                }
            }
            Assert.That(foundOrcOverride, Is.True);
            Assert.That(foundFrogOverride, Is.True);

            BywaterAssetCatalog assetCatalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(assetCatalog, Is.Not.Null);

            GameObject worldGo = new GameObject("TempStage2ShopkeeperScaleWorld");
            try
            {
                BywaterWorldRenderer renderer = worldGo.AddComponent<BywaterWorldRenderer>();
                renderer.Initialize(assetCatalog);
                renderer.RenderMap(bundle.Land0, new GridCoord(10, 9), forceRebuild: true);

                // M1.5 Decision #3: Enclosed shopkeepers stay indoors behind clickable shop doorways (0 outdoor shopkeeper views);
                // outdoor quest NPCs render at 1.85f human-parity scale when present.
                BywaterUnitView[] unitViews = worldGo.GetComponentsInChildren<BywaterUnitView>(true);
                int checkedShopkeepers = 0;
                for (int i = 0; i < unitViews.Length; i++)
                {
                    BywaterUnitView uv = unitViews[i];
                    if (uv == null || string.IsNullOrEmpty(uv.UnitId) || !uv.UnitId.StartsWith("npc_", System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Transform quad = uv.transform.Find("SpriteBillboard/SpriteQuad");
                    Assert.That(quad, Is.Not.Null, $"NPC {uv.UnitId} missing SpriteQuad.");
                    Assert.That(quad.localScale.x, Is.EqualTo(1.85f).Within(0.02f), $"Outdoor NPC {uv.UnitId} width scale must match hero 1.85f scale.");
                    Assert.That(quad.localScale.y, Is.EqualTo(1.85f).Within(0.02f), $"Outdoor NPC {uv.UnitId} height scale must match hero 1.85f scale.");
                    checkedShopkeepers++;
                }

                Assert.That(checkedShopkeepers, Is.EqualTo(0), "Enclosed shopkeepers must reside inside shop/temple modals, not outdoors.");
                Assert.That(bundle.Land0.PointsOfInterest.Any(p => string.Equals(p.Kind, "shop", System.StringComparison.OrdinalIgnoreCase)), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(worldGo);
            }
        }

        [Test]
        public void Stage2_ContinuousDayNightArc_SmoothKeyframes_AndDuskNightCombatCrossfade()
        {
            AmbientMode prevMode = RenderSettings.ambientMode;
            Color prevSky = RenderSettings.ambientSkyColor;
            Color prevEquator = RenderSettings.ambientEquatorColor;
            Color prevGround = RenderSettings.ambientGroundColor;
            float prevAmbientIntensity = RenderSettings.ambientIntensity;

            GameObject camGo = new GameObject("Stage2ArcCam", typeof(Camera));
            GameObject sunGo = new GameObject("Stage2ArcSun", typeof(Light));
            GameObject volGo = new GameObject("Stage2ArcVol", typeof(Volume));
            GameObject atmoGo = new GameObject("Stage2ArcAtmo");
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Tonemapping tm = profile.Add<Tonemapping>(true);
            ColorAdjustments ca = profile.Add<ColorAdjustments>(true);

            try
            {
                camGo.tag = "MainCamera";
                Light sun = sunGo.GetComponent<Light>();
                sun.type = LightType.Directional;
                Volume vol = volGo.GetComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;

                BywaterBiomeAtmosphere atmo = atmoGo.AddComponent<BywaterBiomeAtmosphere>();
                atmo.targetCamera = camGo.GetComponent<Camera>();
                atmo.directionalSun = sun;
                atmo.globalVolume = vol;

                WorldClock c0800 = WorldClock.FromMinutes(8 * 60);
                WorldClock c1230 = WorldClock.FromMinutes(12 * 60 + 30);
                WorldClock c1730 = WorldClock.FromMinutes(17 * 60 + 30);

                atmo.ApplyBiome(false, c0800.NormalizedTimeOfDay, c0800.Phase, c0800.PhaseProgress01, hasLitTorch: false, immediate: true, isCombat: false);
                Quaternion q0800 = atmo.CurrentSunRotation;
                atmo.ApplyBiome(false, c1230.NormalizedTimeOfDay, c1230.Phase, c1230.PhaseProgress01, hasLitTorch: false, immediate: true, isCombat: false);
                Quaternion q1230 = atmo.CurrentSunRotation;
                atmo.ApplyBiome(false, c1730.NormalizedTimeOfDay, c1730.Phase, c1730.PhaseProgress01, hasLitTorch: false, immediate: true, isCombat: false);
                Quaternion q1730 = atmo.CurrentSunRotation;

                Assert.That(Quaternion.Angle(q0800, q1730), Is.GreaterThan(45f), "Sun arc from 08:00 to 17:30 must rotate > 45 deg.");
                Assert.That(Quaternion.Angle(q0800, q1230), Is.GreaterThan(15f), "Sun arc from 08:00 to 12:30 must rotate > 15 deg.");

                float firstAmbient = 0f;
                float firstExposure = 0f;
                float prevAmbient = 0f;
                float prevExposure = 0f;

                for (int m = 0; m < WorldClock.MinutesPerDay; m += 15)
                {
                    WorldClock c = WorldClock.FromMinutes(m);
                    atmo.ApplyBiome(false, c.NormalizedTimeOfDay, c.Phase, c.PhaseProgress01, hasLitTorch: false, immediate: true, isCombat: false);
                    float curAmbient = RenderSettings.ambientIntensity;
                    float curExposure = ca.postExposure.value;

                    if (m == 0)
                    {
                        firstAmbient = curAmbient;
                        firstExposure = curExposure;
                    }
                    else
                    {
                        Assert.That(Mathf.Abs(curAmbient - prevAmbient), Is.LessThan(0.20f),
                            $"Ambient intensity jumped >= 0.20 between {m - 15}m and {m}m.");
                        Assert.That(Mathf.Abs(curExposure - prevExposure), Is.LessThan(0.20f),
                            $"Post-exposure jumped >= 0.20 between {m - 15}m and {m}m.");
                    }

                    prevAmbient = curAmbient;
                    prevExposure = curExposure;
                }

                Assert.That(Mathf.Abs(firstAmbient - prevAmbient), Is.LessThan(0.20f),
                    "Midnight wrap ambient intensity delta must stay < 0.20.");
                Assert.That(Mathf.Abs(firstExposure - prevExposure), Is.LessThan(0.20f),
                    "Midnight wrap post-exposure delta must stay < 0.20.");

                // Dusk (18:30) -> Night (00:00) crossfade preserves chiaroscuro Combat Night [0.95..1.12]
                WorldClock c1830 = WorldClock.FromMinutes(18 * 60 + 30);
                atmo.ApplyBiome(false, c1830.NormalizedTimeOfDay, c1830.Phase, c1830.PhaseProgress01, hasLitTorch: false, immediate: true, isCombat: true);
                float duskAmbient = RenderSettings.ambientIntensity;
                Assert.That(duskAmbient, Is.InRange(0.70f, 1.12f));

                WorldClock c0000 = WorldClock.FromMinutes(0);
                atmo.ApplyBiome(false, c0000.NormalizedTimeOfDay, c0000.Phase, c0000.PhaseProgress01, hasLitTorch: false, immediate: true, isCombat: true);
                Assert.That(RenderSettings.ambientIntensity, Is.InRange(0.95f, 1.12f));
                Assert.That(tm.mode.value, Is.EqualTo(TonemappingMode.ACES));
                Assert.That(Shader.GetGlobalFloat("_GlobalNightRimStrength"), Is.GreaterThanOrEqualTo(0.50f));
            }
            finally
            {
                Object.DestroyImmediate(atmoGo);
                Object.DestroyImmediate(volGo);
                Object.DestroyImmediate(sunGo);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(profile);
                RenderSettings.ambientMode = prevMode;
                RenderSettings.ambientSkyColor = prevSky;
                RenderSettings.ambientEquatorColor = prevEquator;
                RenderSettings.ambientGroundColor = prevGround;
                RenderSettings.ambientIntensity = prevAmbientIntensity;
            }
        }

        [Test]
        public void Stage2_SaveV4_FogOfWar3State_WeatherStorm_AndWorldClockMinutes_RoundTrip()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "bywater_m1"));
            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession session = new ExplorationSession(
                bundle.Maps,
                "land_0",
                party,
                new DeterministicDiceRoller(1234UL),
                startPosition: new GridCoord(10, 9));

            session.MoveAlongPath(new GridCoord(15, 9), autoHaltOnEvents: false);
            session.FogMaps["dungeon_0"].RecomputeVisibility(bundle.Dungeon0, new GridCoord(33, 72), 6);
            session.SetWeather(WeatherState.Storm);
            session.AdvanceWorldClockMinutes(137.5);

            string tempSave = Path.Combine(Path.GetTempPath(), "realmz_stage2_v4_fog_weather_test.json");
            try
            {
                BywaterSaveData snap = BywaterSaveService.CaptureSnapshot(session);
                Assert.That(snap.version, Is.EqualTo(4));
                Assert.That(snap.hasWorldClockMinutes, Is.True);
                Assert.That(snap.worldTimeMinutes, Is.EqualTo(session.WorldClockMinutes).Within(0.01));
                Assert.That(BywaterSaveService.ExtractWeatherState(snap), Is.EqualTo("Storm"));
                Assert.That(BywaterSaveService.ExtractFogForMap(snap, "land_0"), Is.Not.Null);
                Assert.That(BywaterSaveService.ExtractFogForMap(snap, "dungeon_0"), Is.Not.Null);
                Assert.That(BywaterSaveService.TryWriteSave(tempSave, snap), Is.True);

                Assert.That(BywaterSaveService.Load(tempSave, bundle, out ExplorationSession restored, out string loadStatus), Is.True, loadStatus);

                Assert.That(restored.Weather, Is.EqualTo(WeatherState.Storm));
                Assert.That(restored.WorldClockMinutes, Is.EqualTo(session.WorldClockMinutes).Within(0.01));
                Assert.That(restored.FogMaps["land_0"].IsVisible(15, 9), Is.True);
                Assert.That(restored.FogMaps["land_0"].IsExplored(10, 9), Is.True);
                Assert.That(restored.FogMaps["land_0"].IsExplored(88, 88), Is.False);
                Assert.That(restored.FogMaps["dungeon_0"].IsExplored(33, 72), Is.True);
                Assert.That(restored.FogMaps["dungeon_0"].IsExplored(5, 5), Is.False);
            }
            finally
            {
                if (File.Exists(tempSave)) File.Delete(tempSave);
            }
        }

        [Test]
        public void Stage2_RealmzMasterMixer_GroupsParamsSnapshots_AndBindMixerWithoutWarnings()
        {
            List<string> warningsAndErrors = new List<string>();
            void LogHandler(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    warningsAndErrors.Add($"[{type}] {condition}");
                }
            }

            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/RealmzMasterMixer.mixer");
            Assert.That(mixer, Is.Not.Null, "Assets/Audio/RealmzMasterMixer.mixer missing.");

            string[] requiredGroups = { "Master", "Music", "SFX", "Ambience", "UI" };
            for (int i = 0; i < requiredGroups.Length; i++)
            {
                AudioMixerGroup[] matches = mixer.FindMatchingGroups(requiredGroups[i]);
                Assert.That(matches != null && matches.Length > 0, Is.True, $"Missing AudioMixer group '{requiredGroups[i]}'.");
            }

            Assert.That(mixer.FindSnapshot("Overworld"), Is.Not.Null, "Missing 'Overworld' AudioMixerSnapshot.");
            Assert.That(mixer.FindSnapshot("Dungeon"), Is.Not.Null, "Missing 'Dungeon' AudioMixerSnapshot.");

            HashSet<string> serializedExposedParams = new HashSet<string>(System.StringComparer.Ordinal);
            SerializedObject mixerSo = new SerializedObject(mixer);
            SerializedProperty exposedProp = mixerSo.FindProperty("m_ExposedParameters");
            if (exposedProp != null && exposedProp.isArray)
            {
                for (int i = 0; i < exposedProp.arraySize; i++)
                {
                    SerializedProperty elem = exposedProp.GetArrayElementAtIndex(i);
                    SerializedProperty nameProp = elem.FindPropertyRelative("name");
                    if (nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue))
                    {
                        serializedExposedParams.Add(nameProp.stringValue);
                    }
                }
            }

            string[] requiredParams = { "MusicVolumeDb", "SfxVolumeDb", "AmbienceVolumeDb", "UiVolumeDb" };
            for (int i = 0; i < requiredParams.Length; i++)
            {
                string param = requiredParams[i];
                bool hasParam = mixer.GetFloat(param, out _) || serializedExposedParams.Contains(param);
                Assert.That(hasParam, Is.True, $"Missing exposed AudioMixer parameter '{param}'.");
            }

            GameObject audioGo = new GameObject("TempStage2AudioDirector");
            Application.logMessageReceived += LogHandler;
            try
            {
                BywaterAudioDirector director = audioGo.AddComponent<BywaterAudioDirector>();
                director.BindMixer(mixer);
                Assert.That(director.MasterMixer, Is.EqualTo(mixer));

                director.ApplyEnvironmentSnapshot(true, 0f);
                director.ApplyEnvironmentSnapshot(false, 0f);

                AudioSource[] sources = audioGo.GetComponents<AudioSource>();
                Assert.That(sources.Length, Is.GreaterThanOrEqualTo(2));
                for (int i = 0; i < sources.Length; i++)
                {
                    Assert.That(sources[i].outputAudioMixerGroup, Is.Not.Null,
                        $"AudioSource #{i} on BywaterAudioDirector was not routed to a mixer group.");
                }

                Assert.That(warningsAndErrors, Is.Empty,
                    "BywaterAudioDirector.BindMixer logged warnings/errors:\n" + string.Join("\n", warningsAndErrors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
                Object.DestroyImmediate(audioGo);
            }
        }
    }
}
