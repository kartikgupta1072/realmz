using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
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
        private const string ResourcesDir = "Assets/Resources";
        private const string AudioResourcesRoot = ResourcesDir + "/RealmzAudio";
        private const string CatalogAssetPath = ResourcesDir + "/BywaterAssetCatalog.asset";
        private const string UiFontAssetPath = M1ArtRoot + "/RealmzUiFont.asset";
        private const float UnifiedCharacterPpu = 64f;

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
            public string LegacyM1Key;
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

        private static readonly HeroSpec[] ExtraHeroSpecs =
        {
            new HeroSpec { Key = "archer", Id = "hero_archer", Archetype = "Archer", IconId = 0, PortraitId = 0 },
            new HeroSpec { Key = "assassin", Id = "hero_assassin", Archetype = "Assassin", IconId = 0, PortraitId = 0 },
            new HeroSpec { Key = "barbarian", Id = "hero_barbarian", Archetype = "Barbarian", IconId = 0, PortraitId = 0 },
            new HeroSpec { Key = "druid", Id = "hero_druid", Archetype = "Druid", IconId = 0, PortraitId = 0 },
            new HeroSpec { Key = "enchanter", Id = "hero_enchanter", Archetype = "Enchanter", IconId = 0, PortraitId = 0 },
            new HeroSpec { Key = "monk", Id = "hero_monk", Archetype = "Monk", IconId = 0, PortraitId = 0 }
        };

        private static readonly MonsterSpec[] MonsterSpecs =
        {
            new MonsterSpec { Key = "monster_giant_frog", LegacyM1Key = "giant_frog", DisplayName = "Giant Frog" },
            new MonsterSpec { Key = "monster_goblin_raider", LegacyM1Key = "goblin_raider", DisplayName = "Goblin Raider" },
            new MonsterSpec { Key = "monster_goblin_archer", LegacyM1Key = "goblin_archer", DisplayName = "Goblin Archer" },
            new MonsterSpec { Key = "monster_orc_warrior", LegacyM1Key = "orc_warrior", DisplayName = "Orc Warrior" },
            new MonsterSpec { Key = "monster_giant_rat", LegacyM1Key = "giant_sewer_rat", DisplayName = "Giant Rat" },
            new MonsterSpec { Key = "monster_skeleton_warrior", LegacyM1Key = "skeleton_warrior", DisplayName = "Skeleton Warrior" },
            new MonsterSpec { Key = "monster_bandit_cutthroat", LegacyM1Key = "bandit_cutthroat", DisplayName = "Bandit Cutthroat" }
        };

        private static readonly string[] NpcSheetKeys =
        {
            "npc_blacksmith",
            "npc_tanner",
            "npc_apothecary",
            "npc_priestess"
        };

        private static readonly string[] LegacyM1MonsterTextureKeys =
        {
            "giant_frog",
            "goblin_raider",
            "kobold_spearman",
            "giant_sewer_rat",
            "skeleton_warrior",
            "bandit_cutthroat"
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
            ConfigureAudioClips();
            AssetDatabase.Refresh();

            BywaterAssetCatalog catalog = AssetDatabase.LoadAssetAtPath<BywaterAssetCatalog>(CatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BywaterAssetCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
            }

            // 1. Heroes (unified 64 px/m 512x128 sheet + normals + 256x256 portraits)
            Realmz.EditorTools.Cartography.CartographyAssetImporter.ImportAll();

            catalog.heroes = new List<BywaterAssetCatalog.HeroArtEntry>(HeroSpecs.Length);
            catalog.cartographyUnitSheets = new List<BywaterAssetCatalog.CartographyUnitSheetEntry>(
                HeroSpecs.Length + MonsterSpecs.Length);
            for (int i = 0; i < HeroSpecs.Length; i++)
            {
                HeroSpec spec = HeroSpecs[i];
                string sheetAlbedoPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.UnitsDir}/{spec.Id}_sheet.png";
                string unifiedPortraitPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.PortraitsDir}/portrait_{spec.Key}.png";
                string legacyPortraitPath = PortraitsDir + "/" + spec.Key + "_portrait.png";
                string legacyAlbedoPath = HeroesDir + "/" + spec.Key + ".png";
                string legacyNormalPath = HeroesDir + "/" + spec.Key + "_n.png";
                string legacyMatPath = MaterialsDir + "/M1_Hero_" + spec.Key + ".mat";

                Texture2D legacyAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(legacyAlbedoPath);
                Texture2D legacyNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(legacyNormalPath);
                CreateOrUpdateBillboardMaterial(legacyMatPath, legacyAlbedo, legacyNormal, 0.18f, subtleTorchEmission: false);

                Material unitSheetMat = Realmz.EditorTools.Cartography.CartographyAssetImporter.CreateOrUpdateUnitMaterial(spec.Id);
                Material walkMat = CreateOrUpdateVariantUnitMaterial(spec.Id, "walk", 8.5f);
                Material attackMat = CreateOrUpdateVariantUnitMaterial(spec.Id, "attack", 10.0f);
                Sprite frameZeroSprite = Realmz.EditorTools.Cartography.CartographyAssetImporter.LoadFirstSpriteFrame(sheetAlbedoPath)
                    ?? AssetDatabase.LoadAssetAtPath<Sprite>(legacyAlbedoPath);
                Sprite portrait = AssetDatabase.LoadAssetAtPath<Sprite>(unifiedPortraitPath)
                    ?? AssetDatabase.LoadAssetAtPath<Sprite>(legacyPortraitPath);

                catalog.heroes.Add(new BywaterAssetCatalog.HeroArtEntry
                {
                    id = spec.Id,
                    archetype = spec.Archetype,
                    iconId = spec.IconId,
                    portraitId = spec.PortraitId,
                    billboardMaterial = unitSheetMat,
                    unitSheetMaterial = unitSheetMat,
                    walkMaterial = walkMat,
                    attackMaterial = attackMat,
                    sprite = frameZeroSprite,
                    portrait = portrait
                });
                catalog.cartographyUnitSheets.Add(new BywaterAssetCatalog.CartographyUnitSheetEntry
                {
                    unitKey = spec.Id,
                    unitSheetMaterial = unitSheetMat,
                    walkMaterial = walkMat,
                    attackMaterial = attackMat
                });
            }

            // 1b. Pack 1 extra heroes (6 additional hero classes) & all 12 bronze portraits
            catalog.extraHeroes = new List<BywaterAssetCatalog.HeroArtEntry>(ExtraHeroSpecs.Length);
            for (int i = 0; i < ExtraHeroSpecs.Length; i++)
            {
                HeroSpec spec = ExtraHeroSpecs[i];
                string sheetAlbedoPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.UnitsDir}/{spec.Id}_sheet.png";
                string unifiedPortraitPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.PortraitsDir}/portrait_{spec.Key}.png";
                Material unitSheetMat = Realmz.EditorTools.Cartography.CartographyAssetImporter.CreateOrUpdateUnitMaterial(spec.Id);
                Material walkMat = CreateOrUpdateVariantUnitMaterial(spec.Id, "walk", 8.5f);
                Material attackMat = CreateOrUpdateVariantUnitMaterial(spec.Id, "attack", 10.0f);
                Sprite frameZeroSprite = Realmz.EditorTools.Cartography.CartographyAssetImporter.LoadFirstSpriteFrame(sheetAlbedoPath);
                Sprite portrait = AssetDatabase.LoadAssetAtPath<Sprite>(unifiedPortraitPath);

                catalog.extraHeroes.Add(new BywaterAssetCatalog.HeroArtEntry
                {
                    id = spec.Id,
                    archetype = spec.Archetype,
                    iconId = spec.IconId,
                    portraitId = spec.PortraitId,
                    billboardMaterial = unitSheetMat,
                    unitSheetMaterial = unitSheetMat,
                    walkMaterial = walkMat,
                    attackMaterial = attackMat,
                    sprite = frameZeroSprite,
                    portrait = portrait
                });
            }

            catalog.unifiedPortraits = CollectUnifiedPortraits();

            // 2. Monsters (keep exact 7 baseline entries in catalog.monsters + 43 expandedMonsters + 133 archetypeMappings)
            catalog.monsters = new List<BywaterAssetCatalog.MonsterArtEntry>(MonsterSpecs.Length);
            for (int i = 0; i < MonsterSpecs.Length; i++)
            {
                MonsterSpec spec = MonsterSpecs[i];
                string sheetAlbedoPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.UnitsDir}/{spec.Key}_sheet.png";
                Material unitSheetMat = Realmz.EditorTools.Cartography.CartographyAssetImporter.CreateOrUpdateUnitMaterial(spec.Key);
                Material walkMat = CreateOrUpdateVariantUnitMaterial(spec.Key, "walk", 8.5f);
                Material attackMat = CreateOrUpdateVariantUnitMaterial(spec.Key, "attack", 10.0f);
                Sprite frameZeroSprite = Realmz.EditorTools.Cartography.CartographyAssetImporter.LoadFirstSpriteFrame(sheetAlbedoPath);
                if (frameZeroSprite == null && !string.IsNullOrEmpty(spec.LegacyM1Key))
                {
                    frameZeroSprite = AssetDatabase.LoadAssetAtPath<Sprite>(MonstersDir + "/" + spec.LegacyM1Key + ".png");
                }

                catalog.monsters.Add(new BywaterAssetCatalog.MonsterArtEntry
                {
                    key = spec.Key,
                    displayName = spec.DisplayName,
                    billboardMaterial = unitSheetMat,
                    unitSheetMaterial = unitSheetMat,
                    walkMaterial = walkMat,
                    attackMaterial = attackMat,
                    sprite = frameZeroSprite,
                    portraitPixelRect = ComputeFrameZeroPortraitRect(sheetAlbedoPath),
                    archetypeKey = spec.Key
                });
                catalog.cartographyUnitSheets.Add(new BywaterAssetCatalog.CartographyUnitSheetEntry
                {
                    unitKey = spec.Key,
                    unitSheetMaterial = unitSheetMat,
                    walkMaterial = walkMat,
                    attackMaterial = attackMat
                });
            }

            // 2a. Town NPC sheets (populated when PNGs exist on disk, keeping cartographyUnitSheets at exact count 13)
            catalog.cartographyNpcSheets = new List<BywaterAssetCatalog.CartographyUnitSheetEntry>(NpcSheetKeys.Length);
            for (int i = 0; i < NpcSheetKeys.Length; i++)
            {
                string npcKey = NpcSheetKeys[i];
                string npcSheetPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.UnitsDir}/{npcKey}_sheet.png";
                Texture2D npcAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(npcSheetPath);
                Material npcWalkMat = CreateOrUpdateVariantUnitMaterial(npcKey, "walk", 8.5f);
                Material npcAttackMat = CreateOrUpdateVariantUnitMaterial(npcKey, "attack", 10.0f);
                if (npcAlbedo == null && npcWalkMat == null && npcAttackMat == null)
                {
                    continue;
                }

                Material npcIdleMat = npcAlbedo != null
                    ? Realmz.EditorTools.Cartography.CartographyAssetImporter.CreateOrUpdateUnitMaterial(npcKey)
                    : null;
                catalog.cartographyNpcSheets.Add(new BywaterAssetCatalog.CartographyUnitSheetEntry
                {
                    unitKey = npcKey,
                    unitSheetMaterial = npcIdleMat,
                    walkMaterial = npcWalkMat,
                    attackMaterial = npcAttackMat
                });
            }

            catalog.expandedMonsters = CollectExpandedMonsters();
            catalog.archetypeMappings = LoadArchetypeMappings();

            for (int i = 0; i < LegacyM1MonsterTextureKeys.Length; i++)
            {
                string legacyKey = LegacyM1MonsterTextureKeys[i];
                Texture2D legacyAlb = AssetDatabase.LoadAssetAtPath<Texture2D>(MonstersDir + "/" + legacyKey + ".png");
                Texture2D legacyNorm = AssetDatabase.LoadAssetAtPath<Texture2D>(MonstersDir + "/" + legacyKey + "_n.png");
                CreateOrUpdateBillboardMaterial(MaterialsDir + "/M1_Monster_" + legacyKey + ".mat", legacyAlb, legacyNorm, 0.18f, subtleTorchEmission: false);
            }

            // 2b. All 21 32x32 combat hotbar & utility icons + full item/spell/badge icon library
            string[] iconKeys = Realmz.EditorTools.Cartography.CartographyAssetImporter.HotbarIconKeys;
            catalog.hotbarIcons = new List<BywaterAssetCatalog.HotbarIconEntry>(iconKeys.Length);
            for (int i = 0; i < iconKeys.Length; i++)
            {
                string iconKey = iconKeys[i];
                string iconPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.IconsDir}/icon_{iconKey}.png";
                Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                catalog.hotbarIcons.Add(new BywaterAssetCatalog.HotbarIconEntry
                {
                    key = iconKey,
                    sprite = iconSprite
                });
            }

            catalog.itemAndSpellIcons = CollectAllCartographyIcons();
            LoadItemAndSpellIconMappings(out catalog.itemIdIconMappings, out catalog.spellIdIconMappings);

            // 2c. 4-frame Combat Surfaces & Combat VFX strips
            catalog.combatSurfaces = CollectStripAssets(
                Realmz.EditorTools.Cartography.CartographyAssetImporter.SurfacesDir,
                isVfx: false);
            catalog.combatVfx = CollectStripAssets(
                Realmz.EditorTools.Cartography.CartographyAssetImporter.VfxDir,
                isVfx: true);

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

            // 5b. Wave B Cartography Terrain, Water, Foliage, and Prop Materials (already imported in Step 1)
            catalog.cartographyTerrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Realmz.EditorTools.Cartography.CartographyAssetImporter.TerrainMaterialPath);
            catalog.cartographyWaterMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Realmz.EditorTools.Cartography.CartographyAssetImporter.WaterMaterialPath);
            catalog.cartographyFoliageMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Realmz.EditorTools.Cartography.CartographyAssetImporter.FoliageMaterialPath);
            catalog.cartographyDungeonTerrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Realmz.EditorTools.Cartography.CartographyAssetImporter.DungeonTerrainMaterialPath);
            catalog.cartographyDungeonWaterMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Realmz.EditorTools.Cartography.CartographyAssetImporter.DungeonWaterMaterialPath);
            catalog.cartographyDungeonFoliageMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Realmz.EditorTools.Cartography.CartographyAssetImporter.DungeonFoliageMaterialPath);
            catalog.cartographyMarshFoliageMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Realmz.EditorTools.Cartography.CartographyAssetImporter.MarshFoliageMaterialPath);
            catalog.shallowWaterMaterial = catalog.cartographyWaterMaterial;

            catalog.cartographyPropMaterials = new List<BywaterAssetCatalog.CartographyPropMaterialEntry>();
            foreach (Realmz.Cartography.CartographyPropKind kind in Enum.GetValues(typeof(Realmz.Cartography.CartographyPropKind)))
            {
                if (kind == Realmz.Cartography.CartographyPropKind.None)
                {
                    continue;
                }

                Material propMat = Realmz.EditorTools.Cartography.CartographyAssetImporter.LoadOrCreatePropMaterial(kind, out Texture2D propTex);
                Vector2 propSizeMeters = Realmz.EditorTools.Cartography.CartographyAssetImporter.GetPropSizeMeters(kind, propTex);
                catalog.cartographyPropMaterials.Add(new BywaterAssetCatalog.CartographyPropMaterialEntry
                {
                    kind = kind,
                    material = propMat,
                    sizeMeters = propSizeMeters
                });
            }

            // 6. UI Font Asset
            catalog.uiFont = EnsureUiFontAsset();

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
                ConfigureBillboardSprite(HeroesDir + "/" + key + ".png", UnifiedCharacterPpu);
                ConfigureNormalMap(HeroesDir + "/" + key + "_n.png", TextureWrapMode.Clamp);
                ConfigurePortraitSprite(PortraitsDir + "/" + key + "_portrait.png", UnifiedCharacterPpu);
            }

            for (int i = 0; i < LegacyM1MonsterTextureKeys.Length; i++)
            {
                string key = LegacyM1MonsterTextureKeys[i];
                ConfigureBillboardSprite(MonstersDir + "/" + key + ".png", UnifiedCharacterPpu);
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

        private static void ConfigureBillboardSprite(string assetPath, float? ppuOverride = null)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.GetSourceTextureWidthAndHeight(out int texW, out int texH);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppuOverride ?? ResolveCrispPpu(texW);
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

        private static void ConfigurePortraitSprite(string assetPath, float? ppuOverride = null)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.GetSourceTextureWidthAndHeight(out int texW, out int texH);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppuOverride ?? ResolveCrispPpu(texW);
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
            Material mat = LoadOrCreateMaterial(matPath, "Realmz/CartographyWater", "Universal Render Pipeline/Lit");
            bool isCartographyWater = mat.shader != null && mat.shader.name == "Realmz/CartographyWater";

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", new Color(0.42f, 0.78f, 0.96f, 1f));
            }
            if (mat.HasProperty("_ShallowColor"))
            {
                mat.SetColor("_ShallowColor", new Color(0.18f, 0.45f, 0.62f, 0.88f));
            }
            if (mat.HasProperty("_DeepColor"))
            {
                mat.SetColor("_DeepColor", new Color(0.08f, 0.24f, 0.40f, 0.96f));
            }
            if (!isCartographyWater)
            {
                if (albedo != null)
                {
                    mat.SetTexture("_BaseMap", albedo);
                    mat.SetTextureScale("_BaseMap", Vector2.one);
                }
                else
                {
                    mat.SetTexture("_BaseMap", null);
                }
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

        [MenuItem("Realmz/M1/Import Classic Audio")]
        public static void ConfigureAudioClips()
        {
            if (!AssetDatabase.IsValidFolder(AudioResourcesRoot))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioResourcesRoot });
            int updated = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                AudioImporter importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
                if (importer == null)
                {
                    continue;
                }

                bool isMusic = assetPath.IndexOf("/music/", StringComparison.OrdinalIgnoreCase) >= 0;
                bool desiredMono = !isMusic;
                bool desiredLoadBg = isMusic;
                AudioClipLoadType desiredLoadType = isMusic ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                AudioCompressionFormat desiredCompression = isMusic ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;

                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                bool needsSave = importer.forceToMono != desiredMono
                    || importer.loadInBackground != desiredLoadBg
                    || settings.loadType != desiredLoadType
                    || settings.compressionFormat != desiredCompression
                    || Math.Abs(settings.quality - 0.7f) > 0.02f;

                if (!needsSave)
                {
                    continue;
                }

                importer.forceToMono = desiredMono;
                importer.loadInBackground = desiredLoadBg;
                settings.loadType = desiredLoadType;
                settings.compressionFormat = desiredCompression;
                settings.quality = 0.7f;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                updated++;
            }

            if (updated > 0)
            {
                Debug.Log("[M1AssetImporter] Configured " + updated + " RealmzAudio AudioClip(s).");
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

        [Serializable]
        private sealed class ArchetypeMappingFileDto
        {
            public ArchetypeMappingRecordDto[] records;
        }

        [Serializable]
        private sealed class ArchetypeMappingRecordDto
        {
            public int monster_index;
            public string name;
            public int icon_id;
            public int hd;
            public int size;
            public string archetype;
            public string source;
        }

        [Serializable]
        private sealed class IdIconMapFileDto
        {
            public ItemIconRecordDto[] items;
            public SpellIconRecordDto[] spells;
        }

        [Serializable]
        private sealed class ItemIconRecordDto
        {
            public int id;
            public string name;
            public string category;
            public string slot;
            public string icon_path;
        }

        [Serializable]
        private sealed class SpellIconRecordDto
        {
            public int id;
            public string key;
            public string name;
            public string icon_path;
        }

        private static List<BywaterAssetCatalog.HotbarIconEntry> CollectUnifiedPortraits()
        {
            List<BywaterAssetCatalog.HotbarIconEntry> list = new List<BywaterAssetCatalog.HotbarIconEntry>(12);
            string dir = Path.GetFullPath(Realmz.EditorTools.Cartography.CartographyAssetImporter.PortraitsDir);
            if (!Directory.Exists(dir))
            {
                return list;
            }

            string[] files = Directory.GetFiles(dir, "portrait_*.png", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (fileName.EndsWith("_n.png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string assetPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.PortraitsDir}/{fileName}";
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                string key = Path.GetFileNameWithoutExtension(fileName);
                if (key.StartsWith("portrait_", StringComparison.OrdinalIgnoreCase))
                {
                    key = key.Substring("portrait_".Length);
                }

                list.Add(new BywaterAssetCatalog.HotbarIconEntry
                {
                    key = key.ToLowerInvariant(),
                    sprite = sprite
                });
            }

            return list;
        }

        private static Material CreateOrUpdateVariantUnitMaterial(string unitId, string stateSuffix, float animFps)
        {
            if (string.IsNullOrWhiteSpace(unitId) || string.IsNullOrWhiteSpace(stateSuffix))
            {
                return null;
            }

            string normalized = unitId.Trim().ToLowerInvariant();
            string unitsDir = Realmz.EditorTools.Cartography.CartographyAssetImporter.UnitsDir;
            string albedoPath = $"{unitsDir}/{normalized}_{stateSuffix}_sheet.png";
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            if (albedo == null)
            {
                return null;
            }

            string normalPath = $"{unitsDir}/{normalized}_{stateSuffix}_sheet_n.png";
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

            string matsDir = Realmz.EditorTools.Cartography.CartographyAssetImporter.MaterialsDir;
            EnsureFolder(matsDir);
            string matPath = $"{matsDir}/Cartography_Unit_{normalized}_{stateSuffix}.mat";
            Material mat = LoadOrCreateMaterial(matPath, "Realmz/CartographyBillboard", "Universal Render Pipeline/Lit");
            mat.enableInstancing = false;
            mat.mainTexture = albedo;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normal);
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 0.70f);
            if (mat.HasProperty("_IsTwoBankAtlas")) mat.SetFloat("_IsTwoBankAtlas", 0f);
            if (mat.HasProperty("_Cols")) mat.SetFloat("_Cols", 4f);
            if (mat.HasProperty("_Rows")) mat.SetFloat("_Rows", 1f);
            if (mat.HasProperty("_VariantIndex")) mat.SetFloat("_VariantIndex", 0f);
            if (mat.HasProperty("_FrameCount")) mat.SetFloat("_FrameCount", 4f);
            if (mat.HasProperty("_AnimFps")) mat.SetFloat("_AnimFps", animFps);
            if (mat.HasProperty("_BillboardMode")) mat.SetFloat("_BillboardMode", 0f);
            if (mat.HasProperty("_CameraPitchTiltDeg")) mat.SetFloat("_CameraPitchTiltDeg", 0f);
            if (mat.HasProperty("_WindTipSway")) mat.SetFloat("_WindTipSway", 0f);
            if (mat.HasProperty("_TrampleStrength")) mat.SetFloat("_TrampleStrength", 0f);
            if (mat.HasProperty("_WindGustInfluence")) mat.SetFloat("_WindGustInfluence", 0.08f);
            if (mat.HasProperty("_NormalUpBias")) mat.SetFloat("_NormalUpBias", 0.55f);
            if (mat.HasProperty("_RootTint")) mat.SetColor("_RootTint", Color.white);
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.45f);
            if (mat.HasProperty("_ReceiveRootShadowOnly")) mat.SetFloat("_ReceiveRootShadowOnly", 0f);
            if (mat.HasProperty("_HorizonHazeStrength")) mat.SetFloat("_HorizonHazeStrength", 0.08f);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static List<BywaterAssetCatalog.MonsterArtEntry> CollectExpandedMonsters()
        {
            List<BywaterAssetCatalog.MonsterArtEntry> list = new List<BywaterAssetCatalog.MonsterArtEntry>(48);
            string dir = Path.GetFullPath(Realmz.EditorTools.Cartography.CartographyAssetImporter.UnitsDir);
            if (!Directory.Exists(dir))
            {
                return list;
            }

            string[] files = Directory.GetFiles(dir, "monster_*_sheet.png", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (fileName.EndsWith("_n.png", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith("_walk_sheet.png", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith("_attack_sheet.png", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith("_hit_sheet.png", StringComparison.OrdinalIgnoreCase) ||
                    !fileName.EndsWith("_sheet.png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string unitKey = fileName.Substring(0, fileName.Length - "_sheet.png".Length).ToLowerInvariant();
                string sheetAssetPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.UnitsDir}/{fileName}";
                Material unitSheetMat = Realmz.EditorTools.Cartography.CartographyAssetImporter.CreateOrUpdateUnitMaterial(unitKey);
                Material walkMat = CreateOrUpdateVariantUnitMaterial(unitKey, "walk", 8.5f);
                Material attackMat = CreateOrUpdateVariantUnitMaterial(unitKey, "attack", 10.0f);
                Sprite frameZeroSprite = Realmz.EditorTools.Cartography.CartographyAssetImporter.LoadFirstSpriteFrame(sheetAssetPath);

                string shortName = unitKey.StartsWith("monster_", StringComparison.Ordinal)
                    ? unitKey.Substring("monster_".Length)
                    : unitKey;

                list.Add(new BywaterAssetCatalog.MonsterArtEntry
                {
                    key = unitKey,
                    displayName = ToDisplayName(shortName),
                    billboardMaterial = unitSheetMat,
                    unitSheetMaterial = unitSheetMat,
                    walkMaterial = walkMat,
                    attackMaterial = attackMat,
                    sprite = frameZeroSprite,
                    portraitPixelRect = ComputeFrameZeroPortraitRect(sheetAssetPath),
                    archetypeKey = unitKey
                });
            }

            return list;
        }

        private static Rect ComputeFrameZeroPortraitRect(string sheetAssetPath)
        {
            Rect fallback = new Rect(28f, 44f, 72f, 72f);
            string fullPath = Path.GetFullPath(sheetAssetPath);
            if (!File.Exists(fullPath))
            {
                return fallback;
            }

            Texture2D scratch = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (!ImageConversion.LoadImage(scratch, bytes, markNonReadable: false))
                {
                    return fallback;
                }

                int texW = scratch.width;
                int texH = scratch.height;
                int frameW = Mathf.Min(128, texW);
                int frameH = Mathf.Min(128, texH);
                if (frameW < 16 || frameH < 16)
                {
                    return fallback;
                }

                Color32[] pixels = scratch.GetPixels32();
                int minX = frameW;
                int maxX = -1;
                int minY = frameH;
                int maxY = -1;

                for (int y = 0; y < frameH; y++)
                {
                    int rowOffset = y * texW;
                    for (int x = 0; x < frameW; x++)
                    {
                        if (pixels[rowOffset + x].a > 16)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }

                if (maxX < minX || maxY < minY)
                {
                    return fallback;
                }

                int silW = maxX - minX + 1;
                int silH = maxY - minY + 1;
                int cropSide = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(silW * 0.78f, silH * 0.56f)), 32, 96);
                cropSide = Mathf.Min(cropSide, Mathf.Min(frameW, frameH));

                float centerX = (minX + maxX) * 0.5f;
                int topPad = Mathf.Min(frameH, maxY + 4);
                int cropX = Mathf.Clamp(Mathf.RoundToInt(centerX - cropSide * 0.5f), 0, frameW - cropSide);
                int cropY = Mathf.Clamp(topPad - cropSide, 0, frameH - cropSide);
                return new Rect(cropX, cropY, cropSide, cropSide);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scratch);
            }
        }

        private static List<BywaterAssetCatalog.MonsterArchetypeMappingEntry> LoadArchetypeMappings()
        {
            List<BywaterAssetCatalog.MonsterArchetypeMappingEntry> list = new List<BywaterAssetCatalog.MonsterArchetypeMappingEntry>(140);
            string jsonPath = Path.Combine(Application.streamingAssetsPath, "bywater_m1", "archetype_mapping.json");
            if (!File.Exists(jsonPath))
            {
                return list;
            }

            string json = File.ReadAllText(jsonPath);
            ArchetypeMappingFileDto dto = JsonUtility.FromJson<ArchetypeMappingFileDto>(json);
            if (dto == null || dto.records == null)
            {
                return list;
            }

            for (int i = 0; i < dto.records.Length; i++)
            {
                ArchetypeMappingRecordDto rec = dto.records[i];
                if (rec == null) continue;

                string arch = (rec.archetype ?? string.Empty).Trim();
                if (arch.StartsWith("w234:", StringComparison.OrdinalIgnoreCase))
                {
                    arch = arch.Substring(5).Trim();
                }

                list.Add(new BywaterAssetCatalog.MonsterArchetypeMappingEntry
                {
                    monsterIndex = rec.monster_index,
                    id = SlugifyKey(rec.name),
                    name = rec.name ?? string.Empty,
                    iconId = rec.icon_id,
                    hd = rec.hd,
                    size = rec.size,
                    archetype = arch,
                    source = rec.source ?? string.Empty
                });
            }

            return list;
        }

        private static List<BywaterAssetCatalog.HotbarIconEntry> CollectAllCartographyIcons()
        {
            List<BywaterAssetCatalog.HotbarIconEntry> list = new List<BywaterAssetCatalog.HotbarIconEntry>(110);
            string dir = Path.GetFullPath(Realmz.EditorTools.Cartography.CartographyAssetImporter.IconsDir);
            if (!Directory.Exists(dir))
            {
                return list;
            }

            string[] files = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (fileName.EndsWith("_n.png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string assetPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.IconsDir}/{fileName}";
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                string key = Path.GetFileNameWithoutExtension(fileName);
                if (key.StartsWith("icon_", StringComparison.OrdinalIgnoreCase))
                {
                    key = key.Substring("icon_".Length);
                }

                list.Add(new BywaterAssetCatalog.HotbarIconEntry
                {
                    key = key.ToLowerInvariant(),
                    sprite = sprite
                });
            }

            return list;
        }

        private static void LoadItemAndSpellIconMappings(
            out List<BywaterAssetCatalog.IdIconMappingEntry> itemMappings,
            out List<BywaterAssetCatalog.IdIconMappingEntry> spellMappings)
        {
            itemMappings = new List<BywaterAssetCatalog.IdIconMappingEntry>(64);
            spellMappings = new List<BywaterAssetCatalog.IdIconMappingEntry>(16);

            string jsonPath = Path.Combine(Application.streamingAssetsPath, "bywater_m1", "items_by_id_icon_map.json");
            if (!File.Exists(jsonPath))
            {
                return;
            }

            string json = File.ReadAllText(jsonPath);
            IdIconMapFileDto dto = JsonUtility.FromJson<IdIconMapFileDto>(json);
            if (dto == null)
            {
                return;
            }

            if (dto.items != null)
            {
                for (int i = 0; i < dto.items.Length; i++)
                {
                    ItemIconRecordDto rec = dto.items[i];
                    if (rec == null) continue;
                    ResolveIconPath(rec.icon_path, out string iconKey, out Sprite sprite);
                    itemMappings.Add(new BywaterAssetCatalog.IdIconMappingEntry
                    {
                        id = rec.id,
                        key = SlugifyKey(rec.name),
                        name = rec.name ?? string.Empty,
                        category = rec.category ?? string.Empty,
                        iconKey = iconKey,
                        sprite = sprite
                    });
                }
            }

            if (dto.spells != null)
            {
                for (int i = 0; i < dto.spells.Length; i++)
                {
                    SpellIconRecordDto rec = dto.spells[i];
                    if (rec == null) continue;
                    ResolveIconPath(rec.icon_path, out string iconKey, out Sprite sprite);
                    spellMappings.Add(new BywaterAssetCatalog.IdIconMappingEntry
                    {
                        id = rec.id,
                        key = rec.key ?? SlugifyKey(rec.name),
                        name = rec.name ?? string.Empty,
                        category = "Spell",
                        iconKey = iconKey,
                        sprite = sprite
                    });
                }
            }
        }

        private static void ResolveIconPath(string relativeIconPath, out string iconKey, out Sprite sprite)
        {
            iconKey = string.Empty;
            sprite = null;
            if (string.IsNullOrWhiteSpace(relativeIconPath))
            {
                return;
            }

            string fileName = Path.GetFileName(relativeIconPath.Trim());
            if (string.IsNullOrEmpty(fileName))
            {
                return;
            }

            string assetPath = $"{Realmz.EditorTools.Cartography.CartographyAssetImporter.IconsDir}/{fileName}";
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            iconKey = Path.GetFileNameWithoutExtension(fileName);
            if (iconKey.StartsWith("icon_", StringComparison.OrdinalIgnoreCase))
            {
                iconKey = iconKey.Substring("icon_".Length);
            }
            iconKey = iconKey.ToLowerInvariant();
        }

        private static List<BywaterAssetCatalog.StripAssetEntry> CollectStripAssets(string assetDir, bool isVfx)
        {
            List<BywaterAssetCatalog.StripAssetEntry> list = new List<BywaterAssetCatalog.StripAssetEntry>(8);
            string fullDir = Path.GetFullPath(assetDir);
            if (!Directory.Exists(fullDir))
            {
                return list;
            }

            string[] files = Directory.GetFiles(fullDir, "*.png", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (fileName.EndsWith("_n.png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string key = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
                string albedoPath = $"{assetDir}/{fileName}";
                string normalPath = $"{assetDir}/{Path.GetFileNameWithoutExtension(fileName)}_n.png";

                Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                Material mat = Realmz.EditorTools.Cartography.CartographyAssetImporter.CreateOrUpdateStripMaterial(key, albedo, normal, isVfx);
                Sprite[] frames = Realmz.EditorTools.Cartography.CartographyAssetImporter.LoadAllSpriteFrames(albedoPath);

                list.Add(new BywaterAssetCatalog.StripAssetEntry
                {
                    key = key,
                    albedo = albedo,
                    normal = normal,
                    material = mat,
                    frames = frames
                });
            }

            return list;
        }

        private static string SlugifyKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(raw.Length);
            bool lastUnderscore = false;
            string lower = raw.Trim().ToLowerInvariant();
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    lastUnderscore = false;
                }
                else if (c == '\'' || c == '+')
                {
                    continue;
                }
                else if (!lastUnderscore && sb.Length > 0)
                {
                    sb.Append('_');
                    lastUnderscore = true;
                }
            }

            if (sb.Length > 0 && sb[sb.Length - 1] == '_')
            {
                sb.Length--;
            }
            return sb.ToString();
        }

        private static string ToDisplayName(string snakeKey)
        {
            if (string.IsNullOrWhiteSpace(snakeKey))
            {
                return string.Empty;
            }

            string[] parts = snakeKey.Split('_');
            StringBuilder sb = new StringBuilder(snakeKey.Length + 4);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p)) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1)
                {
                    sb.Append(p.Substring(1));
                }
            }
            return sb.ToString();
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
