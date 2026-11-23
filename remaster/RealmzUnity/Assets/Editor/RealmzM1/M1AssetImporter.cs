using System;
using System.Collections.Generic;
using System.Reflection;
using Realmz.Data;
using Realmz.World;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.EditorTools
{
    /// <summary>
    /// Configures M1 pixel/normal/portrait/tile texture imports, generates URP Lit/Unlit materials,
    /// ensures a valid TMP_FontAsset, and populates Assets/Resources/BywaterAssetCatalog.asset.
    /// </summary>
    public static class M1AssetImporter
    {
        private const string M1ArtRoot = "Assets/Art/M1";
        private const string HeroesDir = M1ArtRoot + "/Heroes";
        private const string MonstersDir = M1ArtRoot + "/Monsters";
        private const string PortraitsDir = M1ArtRoot + "/Portraits";
        private const string PropsDir = M1ArtRoot + "/Props";
        private const string TilesDir = M1ArtRoot + "/Tiles";
        private const string MaterialsDir = M1ArtRoot + "/Materials";
        private const string InboxRoot = "Assets/Inbox";
        private const string FoundryMaterialsDir = "Assets/Materials/Foundry";
        private const string TerrainBlendShaderName = "Realmz/TerrainBlend";
        private const string ResourcesDir = "Assets/Resources";
        private const string CatalogAssetPath = ResourcesDir + "/BywaterAssetCatalog.asset";
        private const string UiFontAssetPath = M1ArtRoot + "/RealmzUiFont.asset";

        private struct HeroSpec
        {
            public string Key;
            public string Id;
            public string Archetype;
            public int IconId;
            public int PortraitId;
        }

        private struct MonsterSpec
        {
            public string Key;
            public string DisplayName;
        }

        private static readonly HeroSpec[] HeroSpecs =
        {
            new HeroSpec { Key = "knight", Id = "hero_knight", Archetype = "Knight", IconId = 9018, PortraitId = 275 },
            new HeroSpec { Key = "cleric", Id = "hero_cleric", Archetype = "Cleric", IconId = 9072, PortraitId = 329 },
            new HeroSpec { Key = "ranger", Id = "hero_ranger", Archetype = "Ranger", IconId = 9047, PortraitId = 304 },
            new HeroSpec { Key = "mage", Id = "hero_mage", Archetype = "Mage", IconId = 9084, PortraitId = 341 },
            new HeroSpec { Key = "paladin", Id = "hero_paladin", Archetype = "Paladin", IconId = 9054, PortraitId = 311 },
            new HeroSpec { Key = "rogue", Id = "hero_rogue", Archetype = "Rogue", IconId = 9106, PortraitId = 363 }
        };

        private static readonly MonsterSpec[] MonsterSpecs =
        {
            new MonsterSpec { Key = "giant_frog", DisplayName = "Giant Frog" },
            new MonsterSpec { Key = "goblin_raider", DisplayName = "Goblin Raider" },
            new MonsterSpec { Key = "kobold_spearman", DisplayName = "Kobold Spearman" },
            new MonsterSpec { Key = "giant_sewer_rat", DisplayName = "Giant Sewer Rat" },
            new MonsterSpec { Key = "skeleton_warrior", DisplayName = "Skeleton Warrior" },
            new MonsterSpec { Key = "bandit_cutthroat", DisplayName = "Bandit Cutthroat" }
        };

        private static readonly string[] TileKeys =
        {
            "bywater_grass",
            "cobblestone_road",
            "dirt_path",
            "dungeon_floor",
            "dungeon_wall",
            "shallow_water"
        };

        private static readonly string[] VariantSuffixes = { "_a", "_b", "_c" };

        private static readonly Color[] VariantTints =
        {
            Color.white,
            new Color(0.95f, 0.99f, 0.94f, 1f),
            new Color(1.03f, 0.98f, 0.92f, 1f)
        };

        private static readonly string[] OptionalPropKeys =
        {
            "cottage_medieval",
            "cottage",
            "tavern",
            "tree_oak",
            "tree_pine",
            "oak_tree",
            "pine_tree",
            "stone_fence",
            "market_crates",
            "wildflower_bush",
            "torch_brazier",
            "brazier"
        };

        [MenuItem("Realmz/M1/Import & Build Art Catalog")]
        public static BywaterAssetCatalog ImportAndBuildCatalog()
        {
            EnsureFolder(MaterialsDir);
            EnsureFolder(ResourcesDir);

            ConfigureAllTextures();
            AssetDatabase.Refresh();

            BywaterAssetCatalog catalog = AssetDatabase.LoadAssetAtPath<BywaterAssetCatalog>(CatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BywaterAssetCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
            }

            // 1. Heroes (albedo, normal, portrait, billboard material)
            catalog.heroes = new List<BywaterAssetCatalog.HeroArtEntry>(HeroSpecs.Length);
            for (int i = 0; i < HeroSpecs.Length; i++)
            {
                HeroSpec spec = HeroSpecs[i];
                string albedoPath = HeroesDir + "/" + spec.Key + ".png";
                string normalPath = HeroesDir + "/" + spec.Key + "_n.png";
                string portraitPath = PortraitsDir + "/" + spec.Key + "_portrait.png";
                string matPath = MaterialsDir + "/M1_Hero_" + spec.Key + ".mat";

                Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(albedoPath);
                Sprite portrait = AssetDatabase.LoadAssetAtPath<Sprite>(portraitPath);

                Material mat = CreateOrUpdateBillboardMaterial(matPath, albedo, normal, 0.18f, subtleTorchEmission: false);
                catalog.heroes.Add(new BywaterAssetCatalog.HeroArtEntry
                {
                    id = spec.Id,
                    archetype = spec.Archetype,
                    iconId = spec.IconId,
                    portraitId = spec.PortraitId,
                    billboardMaterial = mat,
                    sprite = sprite,
                    portrait = portrait
                });
            }

            // 2. Monsters (albedo, normal, billboard material)
            catalog.monsters = new List<BywaterAssetCatalog.MonsterArtEntry>(MonsterSpecs.Length);
            for (int i = 0; i < MonsterSpecs.Length; i++)
            {
                MonsterSpec spec = MonsterSpecs[i];
                string albedoPath = MonstersDir + "/" + spec.Key + ".png";
                string normalPath = MonstersDir + "/" + spec.Key + "_n.png";
                string matPath = MaterialsDir + "/M1_Monster_" + spec.Key + ".mat";

                Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(albedoPath);

                Material mat = CreateOrUpdateBillboardMaterial(matPath, albedo, normal, 0.18f, subtleTorchEmission: false);
                catalog.monsters.Add(new BywaterAssetCatalog.MonsterArtEntry
                {
                    key = spec.Key,
                    displayName = spec.DisplayName,
                    billboardMaterial = mat,
                    sprite = sprite
                });
            }

            // 3. Props (torch/brazier billboard + ember + portal beacon)
            string torchAlbedoPath = FirstExistingPropPath("torch_brazier", "torch");
            string torchNormalPath = FirstExistingPropPath("torch_brazier_n", "torch_n");
            Texture2D torchAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(torchAlbedoPath);
            Texture2D torchNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(torchNormalPath);
            catalog.torchSprite = AssetDatabase.LoadAssetAtPath<Sprite>(torchAlbedoPath);
            catalog.torchPropMaterial = CreateOrUpdateBillboardMaterial(
                MaterialsDir + "/M1_Prop_torch.mat",
                torchAlbedo,
                torchNormal,
                0.18f,
                subtleTorchEmission: false);

            catalog.torchEmberMaterial = CreateOrUpdateEmissiveMaterial(
                MaterialsDir + "/M1_Prop_torch_ember.mat",
                ParseHexColor("#FF7A1A", new Color(1f, 0.478f, 0.102f, 1f)),
                3.2f,
                0.20f);

            catalog.portalBeaconMaterial = CreateOrUpdateEmissiveMaterial(
                MaterialsDir + "/M1_Prop_portal_beacon.mat",
                ParseHexColor("#38BDF8", new Color(0.220f, 0.741f, 0.973f, 1f)),
                2.4f,
                0.35f);

            // 4. Tiles & World Structure Materials
            LoadTileBaseTextures("bywater_grass", out Texture2D grassTex, out Texture2D grassNorm);
            LoadTileBaseTextures("cobblestone_road", out Texture2D cobbleTex, out Texture2D cobbleNorm);
            LoadTileBaseTextures("dirt_path", out Texture2D dirtTex, out Texture2D dirtNorm);
            LoadTileBaseTextures("dungeon_floor", out Texture2D dFloorTex, out Texture2D dFloorNorm);
            LoadTileBaseTextures("dungeon_wall", out Texture2D dWallTex, out Texture2D dWallNorm);
            LoadTileBaseTextures("shallow_water", out Texture2D waterTex, out Texture2D waterNorm);

            catalog.grassMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Tile_bywater_grass.mat", grassTex, grassNorm, Color.white, 0.08f);
            catalog.cobblestoneMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Tile_cobblestone_road.mat", cobbleTex, cobbleNorm, Color.white, 0.08f);
            catalog.dirtPathMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Tile_dirt_path.mat", dirtTex, dirtNorm, Color.white, 0.08f);
            catalog.dungeonFloorMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Tile_dungeon_floor.mat", dFloorTex, dFloorNorm, Color.white, 0.08f);
            catalog.dungeonWallMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Tile_dungeon_wall.mat", dWallTex, dWallNorm, Color.white, 0.08f);
            catalog.shallowWaterMaterial = CreateOrUpdateShallowWaterMaterial(
                MaterialsDir + "/M1_Tile_shallow_water.mat", waterTex, waterNorm);

            catalog.grassVariantMaterials = CreateOrUpdateTileVariantMaterials("bywater_grass", grassTex, grassNorm);
            catalog.cobblestoneVariantMaterials = CreateOrUpdateTileVariantMaterials("cobblestone_road", cobbleTex, cobbleNorm);
            catalog.dirtPathVariantMaterials = CreateOrUpdateTileVariantMaterials("dirt_path", dirtTex, dirtNorm);
            catalog.dungeonFloorVariantMaterials = CreateOrUpdateTileVariantMaterials("dungeon_floor", dFloorTex, dFloorNorm);
            catalog.dungeonWallVariantMaterials = CreateOrUpdateTileVariantMaterials("dungeon_wall", dWallTex, dWallNorm);

            catalog.buildingWallMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_World_BuildingWall.mat",
                dWallTex,
                dWallNorm,
                new Color(0.86f, 0.78f, 0.66f, 1f),
                0.08f);
            catalog.doorArchMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_World_DoorArch.mat",
                cobbleTex,
                cobbleNorm,
                new Color(0.74f, 0.55f, 0.34f, 1f),
                0.08f);
            catalog.treeCoverMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_World_TreeCover.mat",
                grassTex,
                grassNorm,
                new Color(0.36f, 0.56f, 0.34f, 1f),
                0.08f);

            // 4b. M1.5 Modular 2.5D Medieval Prop Materials
            catalog.cottageWallMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_cottage_wall.mat",
                null,
                dirtNorm,
                new Color(0.76f, 0.70f, 0.58f, 1f),
                0.06f,
                metallic: 0f,
                bumpScale: 0.35f);
            catalog.cottageRoofTerracottaMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_roof_terracotta.mat",
                null,
                null,
                new Color(0.58f, 0.22f, 0.15f, 1f),
                0.10f);
            catalog.cottageRoofSlateMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_roof_slate.mat",
                null,
                null,
                new Color(0.22f, 0.28f, 0.38f, 1f),
                0.14f);
            catalog.cottageRoofThatchMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_roof_thatch.mat",
                null,
                null,
                new Color(0.62f, 0.49f, 0.24f, 1f),
                0.06f);
            catalog.timberTrimMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_timber_trim.mat",
                null,
                null,
                new Color(0.24f, 0.14f, 0.08f, 1f),
                0.12f);
            catalog.warmWindowGlowMaterial = CreateOrUpdateEmissiveMaterial(
                MaterialsDir + "/M1_Prop_window_glow.mat",
                new Color(1.0f, 0.78f, 0.35f, 1f),
                new Color(1.0f, 0.72f, 0.28f, 1f) * 1.15f,
                0.22f);
            catalog.oakFoliageMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_oak_foliage.mat",
                null,
                grassNorm,
                new Color(0.16f, 0.40f, 0.15f, 1f),
                0.06f,
                metallic: 0f,
                bumpScale: 0.55f);
            catalog.pineFoliageMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_pine_foliage.mat",
                null,
                grassNorm,
                new Color(0.11f, 0.30f, 0.16f, 1f),
                0.06f,
                metallic: 0f,
                bumpScale: 0.50f);
            catalog.treeTrunkMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_tree_trunk.mat",
                null,
                dirtNorm,
                new Color(0.28f, 0.17f, 0.10f, 1f),
                0.08f,
                metallic: 0f,
                bumpScale: 0.50f);
            catalog.boulderMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_boulder.mat",
                null,
                dWallNorm,
                new Color(0.35f, 0.37f, 0.36f, 1f),
                0.08f,
                metallic: 0f,
                bumpScale: 0.65f);
            catalog.stoneFenceMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_stone_fence.mat",
                cobbleTex,
                cobbleNorm,
                new Color(0.48f, 0.47f, 0.44f, 1f),
                0.08f,
                metallic: 0f,
                bumpScale: 0.90f);
            catalog.crateWoodMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_crate_wood.mat",
                null,
                null,
                new Color(0.42f, 0.27f, 0.14f, 1f),
                0.12f);
            catalog.ironTrimMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_iron_trim.mat",
                null,
                null,
                new Color(0.22f, 0.24f, 0.28f, 1f),
                0.42f,
                metallic: 0.45f);
            catalog.wildflowerMaterial = CreateOrUpdateTileMaterial(
                MaterialsDir + "/M1_Prop_wildflower.mat",
                null,
                null,
                new Color(0.94f, 0.80f, 0.28f, 1f),
                0.10f);

            // 4c. Optional W-222 Hi-Bit Prop Billboards
            TryLoadPropBillboardWithFallback("cottage_medieval", "cottage", out catalog.cottageBillboardMaterial, out catalog.cottageBillboardSprite);
            TryLoadPropBillboard("tavern", out catalog.tavernBillboardMaterial, out catalog.tavernBillboardSprite);
            TryLoadPropBillboardWithFallback("tree_oak", "oak_tree", out catalog.oakTreeBillboardMaterial, out catalog.oakTreeBillboardSprite);
            TryLoadPropBillboardWithFallback("tree_pine", "pine_tree", out catalog.pineTreeBillboardMaterial, out catalog.pineTreeBillboardSprite);
            TryLoadPropBillboard("stone_fence", out catalog.stoneFenceBillboardMaterial, out catalog.stoneFenceBillboardSprite);
            TryLoadPropBillboard("market_crates", out catalog.marketCratesBillboardMaterial, out catalog.marketCratesBillboardSprite);
            TryLoadPropBillboard("wildflower_bush", out catalog.wildflowerBushBillboardMaterial, out catalog.wildflowerBushBillboardSprite);
            TryLoadPropBillboardWithFallback("torch_brazier", "brazier", out catalog.brazierBillboardMaterial, out catalog.brazierBillboardSprite, subtleEmission: false);

            // 5. Transparent Overlay Material (URP Unlit)
            catalog.overlayBaseMaterial = CreateOrUpdateOverlayBaseMaterial(
                MaterialsDir + "/M1_Overlay_Base.mat");

            // 6. UI Font Asset
            catalog.uiFont = EnsureUiFontAsset();

            // 7. M1.5 Option A Painterly PBR Terrain & Scatter Materials
            PopulatePainterlyTerrainCatalog(catalog);

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[M1AssetImporter] Imported M1 assets and updated " + CatalogAssetPath);
            return catalog;
        }

        private static void ConfigureAllTextures()
        {
            for (int i = 0; i < HeroSpecs.Length; i++)
            {
                string key = HeroSpecs[i].Key;
                ConfigureBillboardSprite(HeroesDir + "/" + key + ".png");
                ConfigureNormalMap(HeroesDir + "/" + key + "_n.png", TextureWrapMode.Clamp);
                ConfigurePortraitSprite(PortraitsDir + "/" + key + "_portrait.png");
            }

            for (int i = 0; i < MonsterSpecs.Length; i++)
            {
                string key = MonsterSpecs[i].Key;
                ConfigureBillboardSprite(MonstersDir + "/" + key + ".png");
                ConfigureNormalMap(MonstersDir + "/" + key + "_n.png", TextureWrapMode.Clamp);
            }

            ConfigureBillboardSprite(PropsDir + "/torch.png");
            ConfigureNormalMap(PropsDir + "/torch_n.png", TextureWrapMode.Clamp);

            for (int i = 0; i < TileKeys.Length; i++)
            {
                string key = TileKeys[i];
                ConfigureTileAlbedo(TilesDir + "/" + key + ".png");
                ConfigureNormalMap(TilesDir + "/" + key + "_n.png", TextureWrapMode.Repeat);

                for (int v = 0; v < VariantSuffixes.Length; v++)
                {
                    string suffix = VariantSuffixes[v];
                    ConfigureTileAlbedo(TilesDir + "/" + key + suffix + ".png");
                    ConfigureNormalMap(TilesDir + "/" + key + suffix + "_n.png", TextureWrapMode.Repeat);
                }
            }

            for (int i = 0; i < OptionalPropKeys.Length; i++)
            {
                string propKey = OptionalPropKeys[i];
                ConfigureBillboardSprite(PropsDir + "/" + propKey + ".png");
                ConfigureNormalMap(PropsDir + "/" + propKey + "_n.png", TextureWrapMode.Clamp);
            }
        }

        private static void ConfigureBillboardSprite(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.GetSourceTextureWidthAndHeight(out int texW, out int texH);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ResolveCrispPpu(texW);
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            settings.spritePivot = new Vector2(0.5f, 0f);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void ConfigurePortraitSprite(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.GetSourceTextureWidthAndHeight(out int texW, out int texH);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ResolveCrispPpu(texW);
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Clamp;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void ConfigureTileAlbedo(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }

        private static void ConfigureNormalMap(string assetPath, TextureWrapMode wrapMode)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.NormalMap;
            importer.convertToNormalmap = false;
            importer.sRGBTexture = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = wrapMode;
            importer.SaveAndReimport();
        }

        private static Material CreateOrUpdateBillboardMaterial(
            string matPath,
            Texture2D albedo,
            Texture2D normalMap,
            float smoothness,
            bool subtleTorchEmission)
        {
            Material mat = LoadOrCreateMaterial(matPath, "Universal Render Pipeline/Lit", "Standard");

            mat.SetFloat("_Surface", 0f);
            mat.SetColor("_BaseColor", Color.white);
            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTextureScale("_BaseMap", Vector2.one);
            }

            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;

            mat.SetFloat("_Cull", 0f);
            mat.SetFloat("_Smoothness", Mathf.Min(smoothness, 0.04f));
            mat.SetFloat("_Metallic", 0f);

            ApplyNormalMap(mat, normalMap);

            if (subtleTorchEmission)
            {
                mat.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.15f, 1f) * 0.25f);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.DisableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateEmissiveMaterial(
            string matPath,
            Color baseColor,
            float emissionMultiplier,
            float smoothness)
        {
            return CreateOrUpdateEmissiveMaterial(
                matPath,
                baseColor,
                baseColor * emissionMultiplier,
                smoothness);
        }

        private static Material CreateOrUpdateEmissiveMaterial(
            string matPath,
            Color baseColor,
            Color emissionColor,
            float smoothness)
        {
            Material mat = LoadOrCreateMaterial(matPath, "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit");

            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)RenderQueue.Geometry;

            mat.SetColor("_BaseColor", baseColor);
            mat.SetColor("_EmissionColor", emissionColor);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateTileMaterial(
            string matPath,
            Texture2D albedo,
            Texture2D normalMap,
            Color baseColor,
            float smoothness,
            float metallic = 0f,
            float bumpScale = 1.0f)
        {
            Material mat = LoadOrCreateMaterial(matPath, "Universal Render Pipeline/Lit", "Standard");

            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)RenderQueue.Geometry;

            mat.SetColor("_BaseColor", baseColor);
            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTextureScale("_BaseMap", Vector2.one);
            }
            else
            {
                mat.SetTexture("_BaseMap", null);
            }

            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            ApplyNormalMap(mat, normalMap, bumpScale);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateShallowWaterMaterial(
            string matPath,
            Texture2D albedo,
            Texture2D normalMap)
        {
            Material mat = LoadOrCreateMaterial(matPath, "Realmz/BywaterShimmerWater", "Universal Render Pipeline/Lit");
            bool isShimmerShader = mat.shader != null && mat.shader.name == "Realmz/BywaterShimmerWater";

            mat.SetColor("_BaseColor", new Color(0.42f, 0.78f, 0.96f, 1f));
            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTextureScale("_BaseMap", Vector2.one);
            }
            else
            {
                mat.SetTexture("_BaseMap", null);
            }

            if (isShimmerShader)
            {
                mat.SetTexture("_BumpMap", normalMap);
                mat.SetTextureScale("_BumpMap", Vector2.one);
                mat.SetFloat("_BumpScale", 0.85f);
                mat.SetVector("_ScrollSpeedA", new Vector4(0.045f, 0.025f, 0f, 0f));
                mat.SetVector("_ScrollSpeedB", new Vector4(-0.032f, 0.040f, 0f, 0f));
                mat.SetColor("_ShimmerColor", new Color(0.95f, 0.99f, 1.0f, 1f));
                mat.SetFloat("_ShimmerStrength", 0.72f);
                mat.SetFloat("_WaveScale", 3.2f);
                mat.SetFloat("_Smoothness", 0.72f);
            }
            else
            {
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Smoothness", 0.72f);
                mat.SetFloat("_Metallic", 0f);
                ApplyNormalMap(mat, normalMap, 0.85f);
            }

            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)RenderQueue.Geometry;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material[] CreateOrUpdateTileVariantMaterials(
            string tileKey,
            Texture2D baseTex,
            Texture2D baseNorm)
        {
            Material[] variants = new Material[VariantSuffixes.Length];
            for (int i = 0; i < VariantSuffixes.Length; i++)
            {
                string suffix = VariantSuffixes[i];
                Texture2D varTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TilesDir + "/" + tileKey + suffix + ".png");
                Texture2D varNorm = AssetDatabase.LoadAssetAtPath<Texture2D>(TilesDir + "/" + tileKey + suffix + "_n.png");
                Texture2D chosenTex = varTex != null ? varTex : baseTex;
                Texture2D chosenNorm = varNorm != null ? varNorm : baseNorm;
                Color tint = varTex != null ? Color.white : VariantTints[i];
                if (varTex != null && tileKey == "bywater_grass" && suffix == "_b")
                {
                    // Soften clover grass luminance so grass_a/b/c blend without sharp square contrast.
                    tint = new Color(0.78f, 0.90f, 0.72f, 1f);
                }
                else if (varTex != null && tileKey == "bywater_grass" && suffix == "_c")
                {
                    tint = new Color(0.95f, 1.00f, 0.86f, 1f);
                }
                else if (varTex != null && tileKey == "cobblestone_road" && suffix == "_c")
                {
                    tint = new Color(0.86f, 0.88f, 0.90f, 1f);
                }

                variants[i] = CreateOrUpdateTileMaterial(
                    MaterialsDir + "/M1_Tile_" + tileKey + suffix + ".mat",
                    chosenTex,
                    chosenNorm,
                    tint,
                    0.05f);
            }
            return variants;
        }

        private static float ResolveCrispPpu(int texWidth)
        {
            if (texWidth >= 256)
            {
                return 256f;
            }

            return texWidth >= 128 ? 128f : 64f;
        }

        private static string FirstExistingPropPath(string primaryKey, string fallbackKey)
        {
            string primary = PropsDir + "/" + primaryKey + ".png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(primary) != null)
            {
                return primary;
            }

            return PropsDir + "/" + fallbackKey + ".png";
        }

        private static void LoadTileBaseTextures(string key, out Texture2D tex, out Texture2D norm)
        {
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TilesDir + "/" + key + ".png");
            if (tex == null)
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TilesDir + "/" + key + "_a.png");
            }

            norm = AssetDatabase.LoadAssetAtPath<Texture2D>(TilesDir + "/" + key + "_n.png");
            if (norm == null)
            {
                norm = AssetDatabase.LoadAssetAtPath<Texture2D>(TilesDir + "/" + key + "_a_n.png");
            }
        }

        private static void TryLoadPropBillboardWithFallback(
            string primaryKey,
            string fallbackKey,
            out Material mat,
            out Sprite sprite,
            bool subtleEmission = false)
        {
            TryLoadPropBillboard(primaryKey, out mat, out sprite, subtleEmission);
            if (mat == null && !string.IsNullOrEmpty(fallbackKey))
            {
                TryLoadPropBillboard(fallbackKey, out mat, out sprite, subtleEmission);
            }
        }

        private static void TryLoadPropBillboard(
            string propKey,
            out Material mat,
            out Sprite sprite,
            bool subtleEmission = false)
        {
            string albedoPath = PropsDir + "/" + propKey + ".png";
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            if (albedo == null)
            {
                mat = null;
                sprite = null;
                return;
            }

            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(PropsDir + "/" + propKey + "_n.png");
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(albedoPath);
            mat = CreateOrUpdateBillboardMaterial(
                MaterialsDir + "/M1_Prop_bb_" + propKey + ".mat",
                albedo,
                normal,
                0.18f,
                subtleEmission);
        }

        private static Material CreateOrUpdateOverlayBaseMaterial(string matPath)
        {
            Material mat = LoadOrCreateMaterial(matPath, "Universal Render Pipeline/Unlit", "Unlit/Transparent");

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static void PopulatePainterlyTerrainCatalog(BywaterAssetCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            EnsureFolder(MaterialsDir);

            Material pGrass = LoadFoundryTerrainMaterial("bywater_meadow_grass");
            Material pCobble = LoadFoundryTerrainMaterial("bywater_cobblestone_road");
            Material pDirt = LoadFoundryTerrainMaterial("bywater_dirt_trail");
            Material pRiverbank = LoadFoundryTerrainMaterial("bywater_riverbank_mud");
            Material pDungeonFloor = LoadFoundryTerrainMaterial("dungeon_flagstone_floor");
            Material pDungeonWall = LoadFoundryTerrainMaterial("dungeon_stone_wall");

            if (pGrass != null) catalog.painterlyGrassMaterial = pGrass;
            if (pCobble != null) catalog.painterlyCobbleMaterial = pCobble;
            if (pDirt != null) catalog.painterlyDirtMaterial = pDirt;
            if (pRiverbank != null) catalog.painterlyRiverbankMaterial = pRiverbank;
            if (pDungeonFloor != null) catalog.painterlyDungeonFloorMaterial = pDungeonFloor;
            if (pDungeonWall != null) catalog.painterlyDungeonWallMaterial = pDungeonWall;

            LoadTileBaseTextures("bywater_grass", out Texture2D fallbackGrassTex, out _);
            LoadTileBaseTextures("dungeon_floor", out Texture2D fallbackDungeonTex, out _);

            string[] overworldLayers =
            {
                "bywater_meadow_grass",
                "bywater_cobblestone_road",
                "bywater_dirt_trail",
                "bywater_riverbank_mud"
            };
            catalog.terrainBlendOverworldMaterial = CreateOrUpdateTerrainBlendMaterial(
                MaterialsDir + "/M1_Terrain_OverworldBlend.mat",
                overworldLayers,
                fallbackGrassTex);

            string[] dungeonLayers =
            {
                "dungeon_flagstone_floor",
                "dungeon_stone_wall",
                "bywater_dirt_trail",
                "bywater_riverbank_mud"
            };
            catalog.terrainBlendDungeonMaterial = CreateOrUpdateTerrainBlendMaterial(
                MaterialsDir + "/M1_Terrain_DungeonBlend.mat",
                dungeonLayers,
                fallbackDungeonTex);

            catalog.grassTuftMaterial = CreateOrUpdateScatterMaterial(
                MaterialsDir + "/M1_GrassTuft.mat",
                BywaterGroundScatter.CreateDefaultGrassTuftMaterial);
            catalog.wildflowerTuftMaterial = CreateOrUpdateScatterMaterial(
                MaterialsDir + "/M1_WildflowerTuft.mat",
                BywaterGroundScatter.CreateDefaultFlowerMaterial);
            catalog.pebbleScatterMaterial = CreateOrUpdateScatterMaterial(
                MaterialsDir + "/M1_PebbleScatter.mat",
                BywaterGroundScatter.CreateDefaultPebbleMaterial);
        }

        private static Material LoadFoundryTerrainMaterial(string terrainId)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(FoundryMaterialsDir + "/" + terrainId + ".mat");
        }

        private static void LoadPainterlyTerrainTextures(
            string terrainId,
            out Texture2D albedo,
            out Texture2D normal,
            out Texture2D mask)
        {
            string folder = InboxRoot + "/terrain_" + terrainId;
            albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + terrainId + "_albedo.png");
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + terrainId + "_normal.png");
            mask = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + terrainId + "_mask.png");

            Material foundryMat = LoadFoundryTerrainMaterial(terrainId);
            if (foundryMat != null)
            {
                if (albedo == null && foundryMat.HasProperty("_BaseMap"))
                {
                    albedo = foundryMat.GetTexture("_BaseMap") as Texture2D;
                }
                if (normal == null && foundryMat.HasProperty("_BumpMap"))
                {
                    normal = foundryMat.GetTexture("_BumpMap") as Texture2D;
                }
                if (mask == null && foundryMat.HasProperty("_MaskMap"))
                {
                    mask = foundryMat.GetTexture("_MaskMap") as Texture2D;
                }
            }
        }

        private static Material CreateOrUpdateTerrainBlendMaterial(
            string matPath,
            string[] layerIds,
            Texture2D fallbackBaseTex)
        {
            Material mat = LoadOrCreateMaterial(matPath, TerrainBlendShaderName, "Universal Render Pipeline/Lit");
            bool isTerrainBlend = mat.shader != null && mat.shader.name == TerrainBlendShaderName;

            if (!isTerrainBlend)
            {
                mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", fallbackBaseTex);
                }
                if (mat.HasProperty("_Smoothness"))
                {
                    mat.SetFloat("_Smoothness", 0.08f);
                }
                EditorUtility.SetDirty(mat);
                return mat;
            }

            if (mat.HasProperty("_WorldTileScale")) mat.SetFloat("_WorldTileScale", 0.5f);
            if (mat.HasProperty("_BlendContrast")) mat.SetFloat("_BlendContrast", 0.16f);
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1.0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1.0f);
            if (mat.HasProperty("_MacroScale")) mat.SetFloat("_MacroScale", 0.085f);
            if (mat.HasProperty("_MacroStrength")) mat.SetFloat("_MacroStrength", 0.15f);
            if (mat.HasProperty("_GridHintStrength")) mat.SetFloat("_GridHintStrength", 0.0f);

            LoadPainterlyTerrainTextures(layerIds[0], out Texture2D layer0Albedo, out _, out _);
            bool hasLayer0 = layer0Albedo != null;

            for (int i = 0; i < 4 && i < layerIds.Length; i++)
            {
                string albedoProp = "_Layer" + i + "Albedo";
                string normalProp = "_Layer" + i + "Normal";
                string maskProp = "_Layer" + i + "Mask";
                string colorProp = "_Layer" + i + "Color";

                if (mat.HasProperty(colorProp))
                {
                    mat.SetColor(colorProp, Color.white);
                }

                if (!hasLayer0)
                {
                    if (mat.HasProperty(albedoProp)) mat.SetTexture(albedoProp, null);
                    if (mat.HasProperty(normalProp)) mat.SetTexture(normalProp, null);
                    if (mat.HasProperty(maskProp)) mat.SetTexture(maskProp, null);
                    continue;
                }

                LoadPainterlyTerrainTextures(layerIds[i], out Texture2D albedo, out Texture2D normal, out Texture2D mask);
                if (mat.HasProperty(albedoProp)) mat.SetTexture(albedoProp, albedo);
                if (mat.HasProperty(normalProp)) mat.SetTexture(normalProp, normal);
                if (mat.HasProperty(maskProp)) mat.SetTexture(maskProp, mask);
            }

            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", hasLayer0 ? null : fallbackBaseTex);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateScatterMaterial(string matPath, Func<Material> createDefault)
        {
            Material template = createDefault != null ? createDefault() : null;
            if (template == null)
            {
                return LoadOrCreateMaterial(matPath, "Realmz/GrassTuft", "Universal Render Pipeline/Lit");
            }

            try
            {
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (existing == null)
                {
                    Material assetMat = new Material(template);
                    AssetDatabase.CreateAsset(assetMat, matPath);
                    EditorUtility.SetDirty(assetMat);
                    return assetMat;
                }

                existing.shader = template.shader;
                existing.CopyPropertiesFromMaterial(template);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        private static void ApplyNormalMap(Material mat, Texture2D normalMap, float bumpScale = 1.0f)
        {
            if (normalMap != null)
            {
                mat.SetTexture("_BumpMap", normalMap);
                mat.SetFloat("_BumpScale", bumpScale);
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
            }
        }

        private static Material LoadOrCreateMaterial(string matPath, string primaryShaderName, string fallbackShaderName)
        {
            Shader shader = Shader.Find(primaryShaderName);
            if (shader == null && !string.IsNullOrEmpty(fallbackShaderName))
            {
                shader = Shader.Find(fallbackShaderName);
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
            }

            return mat;
        }

        private static TMP_FontAsset EnsureUiFontAsset()
        {
            const string liberationPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
            TMP_FontAsset directFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(liberationPath);
            if (directFont != null)
            {
                return directFont;
            }

            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            // Ensure TMP Essential Resources are imported non-interactively if not present yet.
            Type importerType = typeof(TMP_PackageResourceImporter);
            MethodInfo importMethod = importerType.GetMethod(
                "ImportResources",
                BindingFlags.Public | BindingFlags.Static);
            if (importMethod != null)
            {
                try
                {
                    importMethod.Invoke(null, new object[] { true, false, false });
                    AssetDatabase.Refresh();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[M1AssetImporter] TMP_PackageResourceImporter.ImportResources skipped: " + ex.Message);
                }
            }

            directFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(liberationPath);
            if (directFont != null)
            {
                return directFont;
            }

            string[] fontGuids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            if (fontGuids != null && fontGuids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(fontGuids[0]));
            }

            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UiFontAssetPath);
            if (existing != null)
            {
                return existing;
            }

            Font osFont = Font.CreateDynamicFontFromOSFont("Segoe UI", 28);
            if (osFont == null)
            {
                osFont = Font.CreateDynamicFontFromOSFont("Arial", 28);
            }

            if (osFont == null)
            {
                Debug.LogWarning("[M1AssetImporter] No OS font found for RealmzUiFont.");
                return null;
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(osFont);
            if (fontAsset == null)
            {
                Debug.LogWarning("[M1AssetImporter] TMP_FontAsset.CreateFontAsset returned null.");
                return null;
            }

            fontAsset.name = "RealmzUiFont";
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            // Pre-bake printable ASCII so the saved atlas works reliably even in headless builds.
            char[] asciiChars = new char[95];
            for (int i = 0; i < asciiChars.Length; i++)
            {
                asciiChars[i] = (char)(32 + i);
            }
            fontAsset.TryAddCharacters(new string(asciiChars));

            AssetDatabase.CreateAsset(fontAsset, UiFontAssetPath);

            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    Texture2D tex = fontAsset.atlasTextures[i];
                    if (tex != null && !AssetDatabase.Contains(tex))
                    {
                        if (string.IsNullOrEmpty(tex.name))
                        {
                            tex.name = "RealmzUiFont Atlas " + i;
                        }
                        AssetDatabase.AddObjectToAsset(tex, fontAsset);
                    }
                }
            }

            if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
            {
                if (string.IsNullOrEmpty(fontAsset.material.name))
                {
                    fontAsset.material.name = "RealmzUiFont Material";
                }
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        private static Color ParseHexColor(string hex, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color parsed) ? parsed : fallback;
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
