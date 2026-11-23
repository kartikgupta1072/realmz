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
        public void All44M1ArtPngs_AreImportedWithCrispPointAndNormalSettings()
        {
            string m1Root = Path.GetFullPath(Path.Combine(Application.dataPath, "Art", "M1"));
            string[] pngs = Directory.GetFiles(m1Root, "*.png", SearchOption.AllDirectories);
            Assert.That(pngs.Length, Is.EqualTo(44), "Expected exactly 44 M1 art PNGs under Assets/Art/M1.");

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
                    Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(64f), "Expected 64 PPU on " + rel);
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
    }
}
