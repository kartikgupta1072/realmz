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

        public List<HeroArtEntry> heroes = new List<HeroArtEntry>();
        public List<MonsterArtEntry> monsters = new List<MonsterArtEntry>();

        [Serializable]
        public sealed class HeroArtEntry
        {
            public string id;
            public string archetype;
            public int iconId;
            public int portraitId;
            public Material billboardMaterial;
            public Sprite sprite;
            public Sprite portrait;
        }

        [Serializable]
        public sealed class MonsterArtEntry
        {
            public string key;
            public string displayName;
            public Material billboardMaterial;
            public Sprite sprite;
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

        private static string ResolveMonsterArtKey(string monsterId, int iconId)
        {
            if (!string.IsNullOrWhiteSpace(monsterId))
            {
                switch (monsterId.Trim().ToLowerInvariant())
                {
                    case "giant_frog":
                        return "giant_frog";
                    case "goblin":
                    case "goblin_champion":
                    case "goblin_raider":
                        return "goblin_raider";
                    case "goblin_archer":
                    case "goblin_shaman":
                    case "orc_archer":
                    case "kobold_spearman":
                        return "kobold_spearman";
                    case "giant_rat":
                    case "monstrous_rat":
                    case "giant_spider":
                    case "giant_sewer_rat":
                        return "giant_sewer_rat";
                    case "skeletal_warrior":
                    case "zombie":
                    case "skeleton_warrior":
                        return "skeleton_warrior";
                    case "orc_warrior":
                    case "bandit_cutthroat":
                        return "bandit_cutthroat";
                }
            }

            switch (iconId)
            {
                case 392:
                    return "giant_frog";
                case 384:
                case 390:
                    return "goblin_raider";
                case 388:
                case 508:
                case 513:
                    return "kobold_spearman";
                case 449:
                case 511:
                case 468:
                    return "giant_sewer_rat";
                case 505:
                case 470:
                    return "skeleton_warrior";
                case 515:
                    return "bandit_cutthroat";
                default:
                    return string.Empty;
            }
        }
    }
}
