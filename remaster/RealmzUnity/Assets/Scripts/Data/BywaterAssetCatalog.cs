using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Realmz.Data
{
    /// <summary>
    /// Central ScriptableObject holding imported M1 materials, sprites, portraits, and UI font assets
    /// for City of Bywater rendering and UI binding.
    /// </summary>
    [CreateAssetMenu(fileName = "BywaterAssetCatalog", menuName = "Realmz/Bywater Asset Catalog")]
    public sealed class BywaterAssetCatalog : ScriptableObject
    {
        public Material grassMaterial;
        public Material cobblestoneMaterial;
        public Material dirtPathMaterial;
        public Material dungeonFloorMaterial;
        public Material dungeonWallMaterial;
        public Material shallowWaterMaterial;
        public Material doorArchMaterial;
        public Material buildingWallMaterial;
        public Material treeCoverMaterial;
        public Material torchPropMaterial;
        public Material torchEmberMaterial;
        public Material portalBeaconMaterial;
        public Material overlayBaseMaterial;
        public Sprite torchSprite;
        public TMP_FontAsset uiFont;

        [Header("M1.5 Tile Variant Materials")]
        public Material[] grassVariantMaterials = new Material[3];
        public Material[] cobblestoneVariantMaterials = new Material[3];
        public Material[] dirtPathVariantMaterials = new Material[3];
        public Material[] dungeonFloorVariantMaterials = new Material[3];
        public Material[] dungeonWallVariantMaterials = new Material[3];

        [Header("M1.5 2.5D Medieval Prop Materials")]
        public Material cottageWallMaterial;
        public Material cottageRoofTerracottaMaterial;
        public Material cottageRoofSlateMaterial;
        public Material cottageRoofThatchMaterial;
        public Material timberTrimMaterial;
        public Material warmWindowGlowMaterial;
        public Material oakFoliageMaterial;
        public Material pineFoliageMaterial;
        public Material treeTrunkMaterial;
        public Material boulderMaterial;
        public Material stoneFenceMaterial;
        public Material crateWoodMaterial;
        public Material ironTrimMaterial;
        public Material wildflowerMaterial;

        [Header("M1.5 Incoming Hi-Bit Prop Billboards (W-222)")]
        public Material cottageBillboardMaterial;
        public Sprite cottageBillboardSprite;
        public Material tavernBillboardMaterial;
        public Sprite tavernBillboardSprite;
        public Material oakTreeBillboardMaterial;
        public Sprite oakTreeBillboardSprite;
        public Material pineTreeBillboardMaterial;
        public Sprite pineTreeBillboardSprite;
        public Material stoneFenceBillboardMaterial;
        public Sprite stoneFenceBillboardSprite;
        public Material marketCratesBillboardMaterial;
        public Sprite marketCratesBillboardSprite;
        public Material wildflowerBushBillboardMaterial;
        public Sprite wildflowerBushBillboardSprite;
        public Material brazierBillboardMaterial;
        public Sprite brazierBillboardSprite;

        [Header("M1.5 Option A Painterly PBR Terrain")]
        public Material terrainBlendOverworldMaterial;
        public Material terrainBlendDungeonMaterial;
        public Material painterlyGrassMaterial;
        public Material painterlyCobbleMaterial;
        public Material painterlyDirtMaterial;
        public Material painterlyRiverbankMaterial;
        public Material painterlyDungeonFloorMaterial;
        public Material painterlyDungeonWallMaterial;
        public Material grassTuftMaterial;
        public Material wildflowerTuftMaterial;
        public Material pebbleScatterMaterial;

        [Header("Wave B Cartography")]
        public Material cartographyTerrainMaterial;
        public Material cartographyWaterMaterial;
        public Material cartographyFoliageMaterial;
        public List<CartographyPropMaterialEntry> cartographyPropMaterials = new List<CartographyPropMaterialEntry>();

        [Header("W-234 Cartography Unit Sheets & Hotbar Icons")]
        public List<HotbarIconEntry> hotbarIcons = new List<HotbarIconEntry>();
        public List<CartographyUnitSheetEntry> cartographyUnitSheets = new List<CartographyUnitSheetEntry>();

        public List<HeroArtEntry> heroes = new List<HeroArtEntry>();
        public List<MonsterArtEntry> monsters = new List<MonsterArtEntry>();

        [NonSerialized]
        private Dictionary<string, Material> _runtimeUnitSheetMaterials;

        [Serializable]
        public sealed class HotbarIconEntry
        {
            public string key;
            public Sprite sprite;
        }

        [Serializable]
        public sealed class CartographyUnitSheetEntry
        {
            public string unitKey;
            public Material unitSheetMaterial;
        }

        [Serializable]
        public sealed class CartographyPropMaterialEntry
        {
            public Realmz.Cartography.CartographyPropKind kind;
            public Material material;
        }

        [Serializable]
        public sealed class HeroArtEntry
        {
            public string id;
            public string archetype;
            public int iconId;
            public int portraitId;
            public Material billboardMaterial;
            public Material unitSheetMaterial;
            public Sprite sprite;
            public Sprite portrait;
        }

        [Serializable]
        public sealed class MonsterArtEntry
        {
            public string key;
            public string displayName;
            public Material billboardMaterial;
            public Material unitSheetMaterial;
            public Sprite sprite;
        }

        /// <summary>
        /// Resolves an optional W-234 hotbar icon sprite by key, returning null if not registered.
        /// </summary>
        public Sprite GetHotbarIcon(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            string normalized = NormalizeIconLookupKey(key);
            if (hotbarIcons != null)
            {
                for (int i = 0; i < hotbarIcons.Count; i++)
                {
                    HotbarIconEntry entry = hotbarIcons[i];
                    if (entry != null &&
                        entry.sprite != null &&
                        (string.Equals(entry.key, normalized, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.key, "icon_" + normalized, StringComparison.OrdinalIgnoreCase)))
                    {
                        return entry.sprite;
                    }
                }
            }

#if UNITY_EDITOR
            string iconPath = $"Assets/Art/Cartography/Icons/icon_{normalized}.png";
            Sprite editorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (editorSprite != null)
            {
                return editorSprite;
            }
#endif

            return null;
        }

        private static string NormalizeIconLookupKey(string key)
        {
            string normalized = key.Trim().ToLowerInvariant();
            if (normalized.StartsWith("icon_", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(5);
            }

            switch (normalized)
            {
                case "undo_move":
                    return "undo";
                case "auto":
                    return "auto_combat";
                case "mute":
                    return "audio";
                case "menu":
                    return "settings";
                case "shield":
                    return "guard";
                case "fire":
                    return "fireball";
                case "bolt":
                    return "magic_darts";
                case "mace":
                    return "sword";
                case "spell":
                    return "staff";
                default:
                    return normalized;
            }
        }

        /// <summary>
        /// Resolves an optional W-234 Cartography unit sheet material for the given unit ID,
        /// falling back cleanly to the provided material when no sheet override is present.
        /// </summary>
        public Material ResolveCartographyUnitMaterial(string unitId, Material fallback)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return fallback;
            }

            string normalized = unitId.Trim();
            string heroAlias = ResolveHeroSheetKey(normalized);
            string monsterAlias = ResolveMonsterArtKey(normalized, 0);
            string canonicalKey = !string.IsNullOrEmpty(heroAlias)
                ? heroAlias
                : (!string.IsNullOrEmpty(monsterAlias) ? monsterAlias : normalized.ToLowerInvariant());

            if (cartographyUnitSheets != null)
            {
                for (int i = 0; i < cartographyUnitSheets.Count; i++)
                {
                    CartographyUnitSheetEntry entry = cartographyUnitSheets[i];
                    if (entry == null || entry.unitSheetMaterial == null)
                    {
                        continue;
                    }

                    if (string.Equals(entry.unitKey, normalized, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(entry.unitKey, canonicalKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.unitSheetMaterial;
                    }
                }
            }

            if (heroes != null)
            {
                for (int i = 0; i < heroes.Count; i++)
                {
                    HeroArtEntry entry = heroes[i];
                    if (entry != null &&
                        entry.unitSheetMaterial != null &&
                        (string.Equals(entry.id, normalized, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.id, canonicalKey, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.archetype, normalized, StringComparison.OrdinalIgnoreCase)))
                    {
                        return entry.unitSheetMaterial;
                    }
                }
            }

            if (monsters != null)
            {
                for (int i = 0; i < monsters.Count; i++)
                {
                    MonsterArtEntry entry = monsters[i];
                    if (entry != null &&
                        entry.unitSheetMaterial != null &&
                        (string.Equals(entry.key, normalized, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.key, canonicalKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        return entry.unitSheetMaterial;
                    }
                }
            }

#if UNITY_EDITOR
            string lookupKey = canonicalKey;
            string matPath = $"Assets/Art/Cartography/Materials/Cartography_Unit_{lookupKey}.mat";
            Material editorMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (editorMat != null)
            {
                return editorMat;
            }

            string sheetPath = $"Assets/Art/Cartography/units/{lookupKey}_sheet.png";
            Texture2D sheetTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
            if (sheetTex != null)
            {
                _runtimeUnitSheetMaterials ??= new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
                if (_runtimeUnitSheetMaterials.TryGetValue(lookupKey, out Material cached) && cached != null)
                {
                    return cached;
                }

                Shader cartoShader = Shader.Find("Realmz/CartographyBillboard");
                Material mat = fallback != null
                    ? new Material(fallback)
                    : (cartoShader != null ? new Material(cartoShader) : null);
                if (mat != null)
                {
                    mat.name = $"CartographyUnit_{lookupKey}_Runtime";
                    mat.hideFlags = HideFlags.HideAndDontSave;
                    mat.mainTexture = sheetTex;
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", sheetTex);
                    if (mat.HasProperty("_IsTwoBankAtlas")) mat.SetFloat("_IsTwoBankAtlas", 0f);
                    if (mat.HasProperty("_Cols")) mat.SetFloat("_Cols", 4f);
                    if (mat.HasProperty("_Rows")) mat.SetFloat("_Rows", 1f);
                    if (mat.HasProperty("_FrameCount")) mat.SetFloat("_FrameCount", 4f);
                    if (mat.HasProperty("_AnimFps")) mat.SetFloat("_AnimFps", 5.5f);
                    if (mat.HasProperty("_BillboardMode")) mat.SetFloat("_BillboardMode", 0f);
                    if (mat.HasProperty("_CameraPitchTiltDeg")) mat.SetFloat("_CameraPitchTiltDeg", 0f);
                    if (mat.HasProperty("_RootTint")) mat.SetColor("_RootTint", Color.white);
                    _runtimeUnitSheetMaterials[lookupKey] = mat;
                    return mat;
                }
            }
#endif

            return fallback;
        }

        /// <summary>
        /// Resolves a Wave B Cartography prop material by kind, returning null if not registered.
        /// </summary>
        public Material GetCartographyPropMaterial(Realmz.Cartography.CartographyPropKind kind)
        {
            if (cartographyPropMaterials == null || kind == Realmz.Cartography.CartographyPropKind.None)
            {
                return null;
            }

            for (int i = 0; i < cartographyPropMaterials.Count; i++)
            {
                CartographyPropMaterialEntry entry = cartographyPropMaterials[i];
                if (entry != null && entry.kind == kind && entry.material != null)
                {
                    return entry.material;
                }
            }

            return null;
        }

        /// <summary>
        /// Safely resolves a tile variant material modulo variants.Length, returning the first non-null
        /// slot or fallback if the array is null/empty or all slots are null.
        /// </summary>
        public Material GetTileVariantMaterial(Material fallback, Material[] variants, int variantIndex)
        {
            if (variants == null || variants.Length == 0)
            {
                return fallback;
            }

            int len = variants.Length;
            int idx = ((variantIndex % len) + len) % len;
            Material chosen = variants[idx];
            if (chosen != null)
            {
                return chosen;
            }

            for (int i = 0; i < len; i++)
            {
                if (variants[i] != null)
                {
                    return variants[i];
                }
            }

            return fallback;
        }

        /// <summary>
        /// Resolves hero visual entry by id, then portraitId, then archetype, falling back to heroes[0].
        /// </summary>
        public HeroArtEntry GetHeroArt(string heroId, int portraitId, string archetype)
        {
            if (heroes == null || heroes.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(heroId))
            {
                for (int i = 0; i < heroes.Count; i++)
                {
                    HeroArtEntry entry = heroes[i];
                    if (entry != null && string.Equals(entry.id, heroId, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            if (portraitId > 0)
            {
                for (int i = 0; i < heroes.Count; i++)
                {
                    HeroArtEntry entry = heroes[i];
                    if (entry != null && entry.portraitId == portraitId)
                    {
                        return entry;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(archetype))
            {
                for (int i = 0; i < heroes.Count; i++)
                {
                    HeroArtEntry entry = heroes[i];
                    if (entry != null && string.Equals(entry.archetype, archetype, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            return heroes[0];
        }

        /// <summary>
        /// Maps Bywater monster IDs and icon IDs cleanly to the six M1 monster art keys,
        /// falling back to monsters[0].
        /// </summary>
        public MonsterArtEntry GetMonsterArt(string monsterId, int iconId)
        {
            if (monsters == null || monsters.Count == 0)
            {
                return null;
            }

            string targetKey = ResolveMonsterArtKey(monsterId, iconId);
            if (!string.IsNullOrEmpty(targetKey))
            {
                for (int i = 0; i < monsters.Count; i++)
                {
                    MonsterArtEntry entry = monsters[i];
                    if (entry != null && string.Equals(entry.key, targetKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            return monsters[0];
        }

        private static string ResolveHeroSheetKey(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return string.Empty;
            }

            switch (unitId.Trim().ToLowerInvariant())
            {
                case "hero_knight":
                case "knight":
                case "aldric":
                    return "hero_knight";
                case "hero_cleric":
                case "cleric":
                case "liora":
                    return "hero_cleric";
                case "hero_ranger":
                case "ranger":
                case "kaelen":
                    return "hero_ranger";
                case "hero_mage":
                case "mage":
                case "theron":
                    return "hero_mage";
                case "hero_paladin":
                case "paladin":
                case "seraphina":
                    return "hero_paladin";
                case "hero_rogue":
                case "rogue":
                case "vex":
                    return "hero_rogue";
                default:
                    return string.Empty;
            }
        }

        private static string StripCombatInstancePrefix(string rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId))
            {
                return string.Empty;
            }

            string lower = rawId.Trim().ToLowerInvariant();
            if (lower.StartsWith("mon_", StringComparison.Ordinal) ||
                lower.StartsWith("qr_", StringComparison.Ordinal))
            {
                int firstUnderscore = lower.IndexOf('_');
                if (firstUnderscore >= 0 && firstUnderscore + 1 < lower.Length)
                {
                    int secondUnderscore = lower.IndexOf('_', firstUnderscore + 1);
                    if (secondUnderscore > firstUnderscore + 1 && secondUnderscore + 1 < lower.Length)
                    {
                        bool allDigits = true;
                        for (int i = firstUnderscore + 1; i < secondUnderscore; i++)
                        {
                            if (!char.IsDigit(lower[i]))
                            {
                                allDigits = false;
                                break;
                            }
                        }

                        if (allDigits)
                        {
                            return lower.Substring(secondUnderscore + 1);
                        }
                    }
                }
            }

            return lower;
        }

        private static string ResolveMonsterArtKey(string monsterId, int iconId)
        {
            if (!string.IsNullOrWhiteSpace(monsterId))
            {
                string key = StripCombatInstancePrefix(monsterId);
                switch (key)
                {
                    case "giant_frog":
                    case "monster_giant_frog":
                        return "monster_giant_frog";
                    case "goblin":
                    case "goblin_champion":
                    case "goblin_raider":
                    case "monster_goblin_raider":
                        return "monster_goblin_raider";
                    case "goblin_archer":
                    case "goblin_shaman":
                    case "orc_archer":
                    case "monster_goblin_archer":
                    case "kobold_spearman":
                        return "monster_goblin_archer";
                    case "orc_warrior":
                    case "monster_orc_warrior":
                        return "monster_orc_warrior";
                    case "giant_rat":
                    case "monstrous_rat":
                    case "giant_spider":
                    case "giant_sewer_rat":
                    case "monster_giant_rat":
                        return "monster_giant_rat";
                    case "skeletal_warrior":
                    case "zombie":
                    case "skeleton_warrior":
                    case "monster_skeleton_warrior":
                        return "monster_skeleton_warrior";
                    case "bandit_cutthroat":
                    case "monster_bandit_cutthroat":
                        return "monster_bandit_cutthroat";
                }
            }

            switch (iconId)
            {
                case 392:
                    return "monster_giant_frog";
                case 384:
                case 390:
                    return "monster_goblin_raider";
                case 388:
                case 508:
                case 513:
                    return "monster_goblin_archer";
                case 515:
                    return "monster_orc_warrior";
                case 449:
                case 511:
                case 468:
                    return "monster_giant_rat";
                case 505:
                case 470:
                    return "monster_skeleton_warrior";
                default:
                    return string.Empty;
            }
        }
    }
}
