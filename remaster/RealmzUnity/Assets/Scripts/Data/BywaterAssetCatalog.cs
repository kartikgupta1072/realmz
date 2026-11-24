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
        public Material cartographyDungeonTerrainMaterial;
        public Material cartographyDungeonWaterMaterial;
        public Material cartographyDungeonFoliageMaterial;
        public Material cartographyMarshFoliageMaterial;
        public List<CartographyPropMaterialEntry> cartographyPropMaterials = new List<CartographyPropMaterialEntry>();

        [Header("W-234 Cartography Unit Sheets & Hotbar Icons")]
        public List<HotbarIconEntry> hotbarIcons = new List<HotbarIconEntry>();
        public List<CartographyUnitSheetEntry> cartographyUnitSheets = new List<CartographyUnitSheetEntry>();

        public List<HeroArtEntry> heroes = new List<HeroArtEntry>();
        public List<MonsterArtEntry> monsters = new List<MonsterArtEntry>();

        [Header("Expanded Pack 1-4 Catalog")]
        public List<HeroArtEntry> extraHeroes = new List<HeroArtEntry>();
        public List<HotbarIconEntry> unifiedPortraits = new List<HotbarIconEntry>();
        public List<MonsterArtEntry> expandedMonsters = new List<MonsterArtEntry>();
        public List<MonsterArchetypeMappingEntry> archetypeMappings = new List<MonsterArchetypeMappingEntry>();
        public List<HotbarIconEntry> itemAndSpellIcons = new List<HotbarIconEntry>();
        public List<IdIconMappingEntry> itemIdIconMappings = new List<IdIconMappingEntry>();
        public List<IdIconMappingEntry> spellIdIconMappings = new List<IdIconMappingEntry>();
        public List<StripAssetEntry> combatSurfaces = new List<StripAssetEntry>();
        public List<StripAssetEntry> combatVfx = new List<StripAssetEntry>();

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
            public Vector2 sizeMeters;
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
            public Rect portraitPixelRect;
            public string archetypeKey;

            [NonSerialized]
            private Sprite _cachedPortraitCrop;

            public Sprite GetPortraitCropSprite()
            {
                if (_cachedPortraitCrop != null)
                {
                    return _cachedPortraitCrop;
                }

                if (sprite == null || sprite.texture == null)
                {
                    return null;
                }

                Rect baseRect = sprite.rect;
                if (baseRect.width < 16f || baseRect.height < 16f)
                {
                    return sprite;
                }

                Rect crop = portraitPixelRect;
                if (crop.width < 16f || crop.height < 16f)
                {
                    float cw = Mathf.Max(16f, Mathf.Round(baseRect.width * 0.56f));
                    float ch = Mathf.Max(16f, Mathf.Round(baseRect.height * 0.56f));
                    float cx = baseRect.x + Mathf.Round((baseRect.width - cw) * 0.5f);
                    float cy = baseRect.y + Mathf.Round(baseRect.height * 0.36f);
                    crop = new Rect(cx, cy, cw, ch);
                }

                float xMin = Mathf.Clamp(crop.x, baseRect.xMin, baseRect.xMax - 16f);
                float yMin = Mathf.Clamp(crop.y, baseRect.yMin, baseRect.yMax - 16f);
                float xMax = Mathf.Clamp(crop.xMax, xMin + 16f, baseRect.xMax);
                float yMax = Mathf.Clamp(crop.yMax, yMin + 16f, baseRect.yMax);
                Rect clamped = Rect.MinMaxRect(xMin, yMin, xMax, yMax);

                _cachedPortraitCrop = Sprite.Create(
                    sprite.texture,
                    clamped,
                    new Vector2(0.5f, 0.5f),
                    64f,
                    0,
                    SpriteMeshType.FullRect);
                _cachedPortraitCrop.name = (string.IsNullOrEmpty(key) ? "monster" : key) + "_portrait_crop";
                return _cachedPortraitCrop;
            }
        }

        [Serializable]
        public sealed class MonsterArchetypeMappingEntry
        {
            public int monsterIndex;
            public string id;
            public string name;
            public int iconId;
            public int hd;
            public int size;
            public string archetype;
            public string source;
        }

        [Serializable]
        public sealed class IdIconMappingEntry
        {
            public int id;
            public string key;
            public string name;
            public string category;
            public string iconKey;
            public Sprite sprite;
        }

        [Serializable]
        public sealed class StripAssetEntry
        {
            public string key;
            public Texture2D albedo;
            public Texture2D normal;
            public Material material;
            public Sprite[] frames;
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
            string monsterAlias = string.Empty;
            if (string.IsNullOrEmpty(heroAlias))
            {
                TryResolveMonsterArchetype(normalized, 0, out monsterAlias);
            }
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

            Material heroSheet = FindHeroSheetMaterial(heroes, normalized, canonicalKey)
                ?? FindHeroSheetMaterial(extraHeroes, normalized, canonicalKey);
            if (heroSheet != null)
            {
                return heroSheet;
            }

            Material monsterSheet = FindMonsterSheetMaterial(expandedMonsters, normalized, canonicalKey)
                ?? FindMonsterSheetMaterial(monsters, normalized, canonicalKey);
            if (monsterSheet != null)
            {
                return monsterSheet;
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

        public Vector2 GetCartographyPropSizeMeters(Realmz.Cartography.CartographyPropKind kind)
        {
            if (cartographyPropMaterials != null && kind != Realmz.Cartography.CartographyPropKind.None)
            {
                for (int i = 0; i < cartographyPropMaterials.Count; i++)
                {
                    CartographyPropMaterialEntry entry = cartographyPropMaterials[i];
                    if (entry != null && entry.kind == kind && entry.sizeMeters.x > 0.01f && entry.sizeMeters.y > 0.01f)
                    {
                        return entry.sizeMeters;
                    }
                }
            }

            return Realmz.Cartography.CartographyWorldData.GetDefaultPropSizeMeters(kind);
        }

        public Sprite GetUnifiedPortrait(string keyOrArchetype)
        {
            if (string.IsNullOrWhiteSpace(keyOrArchetype))
            {
                return null;
            }

            string normalized = keyOrArchetype.Trim().ToLowerInvariant();
            string heroSheetKey = ResolveHeroSheetKey(normalized);
            if (!string.IsNullOrEmpty(heroSheetKey))
            {
                normalized = heroSheetKey;
            }

            if (normalized.StartsWith("portrait_", StringComparison.Ordinal))
            {
                normalized = normalized.Substring("portrait_".Length);
            }
            if (normalized.StartsWith("hero_", StringComparison.Ordinal))
            {
                normalized = normalized.Substring("hero_".Length);
            }

            if (unifiedPortraits != null)
            {
                for (int i = 0; i < unifiedPortraits.Count; i++)
                {
                    HotbarIconEntry entry = unifiedPortraits[i];
                    if (entry != null && entry.sprite != null &&
                        (string.Equals(entry.key, normalized, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.key, "portrait_" + normalized, StringComparison.OrdinalIgnoreCase)))
                    {
                        return entry.sprite;
                    }
                }
            }

            HeroArtEntry heroEntry = FindHeroInList(heroes, keyOrArchetype, 0, keyOrArchetype)
                ?? FindHeroInList(extraHeroes, keyOrArchetype, 0, keyOrArchetype);
            if (heroEntry != null && heroEntry.portrait != null)
            {
                return heroEntry.portrait;
            }

#if UNITY_EDITOR
            string portraitPath = $"Assets/Art/Cartography/Portraits/portrait_{normalized}.png";
            Sprite editorPortrait = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(portraitPath);
            if (editorPortrait != null)
            {
                return editorPortrait;
            }
#endif
            return null;
        }

        public Sprite GetMonsterPortrait(string monsterIdOrName, int iconId)
        {
            MonsterArtEntry entry = GetMonsterArt(monsterIdOrName, iconId);
            return entry != null ? entry.GetPortraitCropSprite() : null;
        }

        public bool TryResolveMonsterArchetype(string monsterIdOrName, int iconId, out string archetypeKey)
        {
            archetypeKey = string.Empty;
            string cleaned = StripArchetypePrefix(StripCombatInstancePrefix(monsterIdOrName));

            if (!string.IsNullOrEmpty(cleaned))
            {
                if (expandedMonsters != null)
                {
                    for (int i = 0; i < expandedMonsters.Count; i++)
                    {
                        MonsterArtEntry entry = expandedMonsters[i];
                        if (entry != null &&
                            (string.Equals(entry.key, cleaned, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(entry.key, "monster_" + cleaned, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(entry.archetypeKey, cleaned, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(entry.displayName, monsterIdOrName.Trim(), StringComparison.OrdinalIgnoreCase)))
                        {
                            archetypeKey = entry.key;
                            return true;
                        }
                    }
                }

                if (monsters != null)
                {
                    for (int i = 0; i < monsters.Count; i++)
                    {
                        MonsterArtEntry entry = monsters[i];
                        if (entry != null &&
                            (string.Equals(entry.key, cleaned, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(entry.key, "monster_" + cleaned, StringComparison.OrdinalIgnoreCase)))
                        {
                            archetypeKey = entry.key;
                            return true;
                        }
                    }
                }
            }

            if (archetypeMappings != null)
            {
                string rawTrimmed = string.IsNullOrWhiteSpace(monsterIdOrName) ? string.Empty : monsterIdOrName.Trim();
                string slug = SlugifyKey(cleaned);
                for (int i = 0; i < archetypeMappings.Count; i++)
                {
                    MonsterArchetypeMappingEntry map = archetypeMappings[i];
                    if (map == null || string.IsNullOrEmpty(map.archetype))
                    {
                        continue;
                    }

                    bool matchName = !string.IsNullOrEmpty(rawTrimmed) &&
                        (string.Equals(map.name, rawTrimmed, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(map.id, cleaned, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(map.id, slug, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(SlugifyKey(map.name), slug, StringComparison.OrdinalIgnoreCase));
                    if (matchName)
                    {
                        archetypeKey = StripArchetypePrefix(map.archetype);
                        return !string.IsNullOrEmpty(archetypeKey);
                    }
                }

                if (iconId > 0)
                {
                    for (int i = 0; i < archetypeMappings.Count; i++)
                    {
                        MonsterArchetypeMappingEntry map = archetypeMappings[i];
                        if (map != null && map.iconId == iconId && !string.IsNullOrEmpty(map.archetype))
                        {
                            archetypeKey = StripArchetypePrefix(map.archetype);
                            return !string.IsNullOrEmpty(archetypeKey);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(cleaned) && cleaned.Length >= 3)
            {
                string slug = SlugifyKey(cleaned);
                if (expandedMonsters != null && !string.IsNullOrEmpty(slug))
                {
                    for (int i = 0; i < expandedMonsters.Count; i++)
                    {
                        MonsterArtEntry entry = expandedMonsters[i];
                        if (entry == null || string.IsNullOrEmpty(entry.key))
                        {
                            continue;
                        }

                        if (entry.key.IndexOf("_" + slug, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            entry.key.IndexOf(slug, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (!string.IsNullOrEmpty(entry.displayName) &&
                             entry.displayName.IndexOf(cleaned, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            archetypeKey = entry.key;
                            return true;
                        }
                    }
                }

                if (archetypeMappings != null && !string.IsNullOrEmpty(slug))
                {
                    for (int i = 0; i < archetypeMappings.Count; i++)
                    {
                        MonsterArchetypeMappingEntry map = archetypeMappings[i];
                        if (map == null || string.IsNullOrEmpty(map.archetype))
                        {
                            continue;
                        }

                        if ((!string.IsNullOrEmpty(map.name) &&
                             map.name.IndexOf(cleaned, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            map.archetype.IndexOf(slug, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            archetypeKey = StripArchetypePrefix(map.archetype);
                            return !string.IsNullOrEmpty(archetypeKey);
                        }
                    }
                }
            }

            string legacyKey = ResolveMonsterArtKey(monsterIdOrName, iconId);
            if (!string.IsNullOrEmpty(legacyKey))
            {
                archetypeKey = legacyKey;
                return true;
            }

            return false;
        }

        public Sprite GetItemIcon(int itemId, string itemName = null)
        {
            if (itemIdIconMappings != null)
            {
                if (itemId > 0)
                {
                    for (int i = 0; i < itemIdIconMappings.Count; i++)
                    {
                        IdIconMappingEntry entry = itemIdIconMappings[i];
                        if (entry != null && entry.id == itemId && entry.sprite != null)
                        {
                            return entry.sprite;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    string trimmed = itemName.Trim();
                    string slug = SlugifyKey(trimmed);
                    for (int i = 0; i < itemIdIconMappings.Count; i++)
                    {
                        IdIconMappingEntry entry = itemIdIconMappings[i];
                        if (entry != null && entry.sprite != null &&
                            (string.Equals(entry.name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(entry.key, slug, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(SlugifyKey(entry.name), slug, StringComparison.OrdinalIgnoreCase)))
                        {
                            return entry.sprite;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(itemName))
            {
                Sprite byKey = FindIconInLists(itemName);
                if (byKey != null)
                {
                    return byKey;
                }
            }

            return null;
        }

        public Sprite GetSpellIcon(int spellId, string spellKeyOrName = null)
        {
            if (spellIdIconMappings != null)
            {
                if (spellId > 0)
                {
                    for (int i = 0; i < spellIdIconMappings.Count; i++)
                    {
                        IdIconMappingEntry entry = spellIdIconMappings[i];
                        if (entry != null && entry.id == spellId && entry.sprite != null)
                        {
                            return entry.sprite;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(spellKeyOrName))
                {
                    string trimmed = spellKeyOrName.Trim();
                    string slug = SlugifyKey(trimmed);
                    for (int i = 0; i < spellIdIconMappings.Count; i++)
                    {
                        IdIconMappingEntry entry = spellIdIconMappings[i];
                        if (entry != null && entry.sprite != null &&
                            (string.Equals(entry.key, trimmed, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(entry.name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(SlugifyKey(entry.name), slug, StringComparison.OrdinalIgnoreCase)))
                        {
                            return entry.sprite;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(spellKeyOrName))
            {
                Sprite byKey = FindIconInLists("spell_" + SlugifyKey(spellKeyOrName))
                    ?? FindIconInLists(spellKeyOrName);
                if (byKey != null)
                {
                    return byKey;
                }
            }

            return null;
        }

        public Sprite GetConditionBadgeIcon(string conditionKey)
        {
            if (string.IsNullOrWhiteSpace(conditionKey))
            {
                return null;
            }

            string normalized = conditionKey.Trim().ToLowerInvariant();
            if (normalized.StartsWith("icon_", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(5);
            }
            if (normalized.StartsWith("badge_", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(6);
            }

            switch (normalized)
            {
                case "poison":
                case "poisoned":
                    normalized = "badge_poison";
                    break;
                case "burn":
                case "burning":
                case "fire":
                    normalized = "badge_burn";
                    break;
                case "stun":
                case "stunned":
                case "daze":
                case "dazed":
                case "hold":
                case "sleep":
                    normalized = "badge_stun";
                    break;
                case "bless":
                case "blessed":
                case "buff":
                case "magic_aura":
                    normalized = "badge_bless";
                    break;
                default:
                    normalized = "badge_" + normalized;
                    break;
            }

            return FindIconInLists(normalized);
        }

        public StripAssetEntry GetCombatSurfaceEntry(string surfaceTypeKey)
        {
            if (string.IsNullOrWhiteSpace(surfaceTypeKey) || combatSurfaces == null)
            {
                return null;
            }

            string normalized = SlugifyKey(surfaceTypeKey);
            if (normalized.StartsWith("surface_", StringComparison.Ordinal))
            {
                normalized = normalized.Substring("surface_".Length);
            }
            if (normalized == "steam")
            {
                normalized = "steam_cloud";
            }
            else if (normalized == "electrified" || normalized == "electric" || normalized == "lightning_water")
            {
                normalized = "electrified_water";
            }

            string normalizedCompact = normalized.Replace("_", string.Empty);

            for (int i = 0; i < combatSurfaces.Count; i++)
            {
                StripAssetEntry entry = combatSurfaces[i];
                if (entry == null) continue;
                string entryNorm = SlugifyKey(entry.key);
                if (entryNorm.StartsWith("surface_", StringComparison.Ordinal))
                {
                    entryNorm = entryNorm.Substring("surface_".Length);
                }
                string entryCompact = entryNorm.Replace("_", string.Empty);
                if (string.Equals(entryNorm, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entryCompact, normalizedCompact, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.key, surfaceTypeKey.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        public StripAssetEntry GetCombatVfxEntry(string vfxKey)
        {
            if (string.IsNullOrWhiteSpace(vfxKey) || combatVfx == null)
            {
                return null;
            }

            string normalized = SlugifyKey(vfxKey);
            if (normalized.StartsWith("vfx_", StringComparison.Ordinal))
            {
                normalized = normalized.Substring("vfx_".Length);
            }

            for (int i = 0; i < combatVfx.Count; i++)
            {
                StripAssetEntry entry = combatVfx[i];
                if (entry == null) continue;
                string entryNorm = SlugifyKey(entry.key);
                if (entryNorm.StartsWith("vfx_", StringComparison.Ordinal))
                {
                    entryNorm = entryNorm.Substring("vfx_".Length);
                }
                if (string.Equals(entryNorm, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.key, vfxKey.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private Sprite FindIconInLists(string rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                return null;
            }

            string trimmed = rawKey.Trim().ToLowerInvariant();
            string noPrefix = trimmed.StartsWith("icon_", StringComparison.Ordinal) ? trimmed.Substring(5) : trimmed;
            string slug = SlugifyKey(noPrefix);

            if (itemAndSpellIcons != null)
            {
                for (int i = 0; i < itemAndSpellIcons.Count; i++)
                {
                    HotbarIconEntry entry = itemAndSpellIcons[i];
                    if (entry != null && entry.sprite != null &&
                        (string.Equals(entry.key, noPrefix, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.key, "icon_" + noPrefix, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.key, slug, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.key, "icon_" + slug, StringComparison.OrdinalIgnoreCase)))
                    {
                        return entry.sprite;
                    }
                }
            }

            Sprite hotbar = GetHotbarIcon(noPrefix);
            if (hotbar != null)
            {
                return hotbar;
            }

#if UNITY_EDITOR
            string iconPath = $"Assets/Art/Cartography/Icons/icon_{slug}.png";
            Sprite editorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (editorSprite != null)
            {
                return editorSprite;
            }
#endif
            return null;
        }

        private static Material FindHeroSheetMaterial(List<HeroArtEntry> list, string normalized, string canonicalKey)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                HeroArtEntry entry = list[i];
                if (entry != null &&
                    entry.unitSheetMaterial != null &&
                    (string.Equals(entry.id, normalized, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(entry.id, canonicalKey, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(entry.archetype, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    return entry.unitSheetMaterial;
                }
            }
            return null;
        }

        private static Material FindMonsterSheetMaterial(List<MonsterArtEntry> list, string normalized, string canonicalKey)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                MonsterArtEntry entry = list[i];
                if (entry != null &&
                    entry.unitSheetMaterial != null &&
                    (string.Equals(entry.key, normalized, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(entry.key, canonicalKey, StringComparison.OrdinalIgnoreCase)))
                {
                    return entry.unitSheetMaterial;
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
        /// Resolves hero visual entry by id, then portraitId, then archetype across heroes and extraHeroes,
        /// falling back to heroes[0].
        /// </summary>
        public HeroArtEntry GetHeroArt(string heroId, int portraitId, string archetype)
        {
            HeroArtEntry found = FindHeroInList(heroes, heroId, portraitId, archetype)
                ?? FindHeroInList(extraHeroes, heroId, portraitId, archetype);
            if (found != null)
            {
                return found;
            }

            if (heroes != null && heroes.Count > 0)
            {
                return heroes[0];
            }

            return (extraHeroes != null && extraHeroes.Count > 0) ? extraHeroes[0] : null;
        }

        private static HeroArtEntry FindHeroInList(List<HeroArtEntry> list, string heroId, int portraitId, string archetype)
        {
            if (list == null || list.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(heroId))
            {
                string trimmedId = heroId.Trim();
                for (int i = 0; i < list.Count; i++)
                {
                    HeroArtEntry entry = list[i];
                    if (entry != null &&
                        (string.Equals(entry.id, trimmedId, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.id, "hero_" + trimmedId, StringComparison.OrdinalIgnoreCase)))
                    {
                        return entry;
                    }
                }
            }

            if (portraitId > 0)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    HeroArtEntry entry = list[i];
                    if (entry != null && entry.portraitId == portraitId)
                    {
                        return entry;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(archetype))
            {
                string trimmedArch = archetype.Trim();
                for (int i = 0; i < list.Count; i++)
                {
                    HeroArtEntry entry = list[i];
                    if (entry != null && string.Equals(entry.archetype, trimmedArch, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves Bywater monster IDs, names, and icon IDs across M1 baseline monsters,
        /// all 43 expanded monster sheets, and all 133 Bywater archetype mappings.
        /// </summary>
        public MonsterArtEntry GetMonsterArt(string monsterId, int iconId)
        {
            string targetKey = string.Empty;
            string explicitUpgrade = ResolveMonsterArtKey(monsterId, iconId);
            if (!string.IsNullOrEmpty(explicitUpgrade))
            {
                targetKey = explicitUpgrade;
            }
            else if (TryResolveMonsterArchetype(monsterId, iconId, out string archKey))
            {
                targetKey = archKey;
            }

            if (!string.IsNullOrEmpty(targetKey))
            {
                if (monsters != null)
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

                if (expandedMonsters != null)
                {
                    for (int i = 0; i < expandedMonsters.Count; i++)
                    {
                        MonsterArtEntry entry = expandedMonsters[i];
                        if (entry != null && string.Equals(entry.key, targetKey, StringComparison.OrdinalIgnoreCase))
                        {
                            return entry;
                        }
                    }
                }
            }

            if (monsters != null && monsters.Count > 0)
            {
                return monsters[0];
            }

            return (expandedMonsters != null && expandedMonsters.Count > 0) ? expandedMonsters[0] : null;
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
                case "hero_archer":
                case "archer":
                    return "hero_archer";
                case "hero_assassin":
                case "assassin":
                    return "hero_assassin";
                case "hero_barbarian":
                case "barbarian":
                    return "hero_barbarian";
                case "hero_druid":
                case "druid":
                    return "hero_druid";
                case "hero_enchanter":
                case "enchanter":
                    return "hero_enchanter";
                case "hero_monk":
                case "monk":
                    return "hero_monk";
                default:
                    return string.Empty;
            }
        }

        private static string StripArchetypePrefix(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string trimmed = raw.Trim();
            if (trimmed.StartsWith("w234:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(5).Trim();
            }

            int colon = trimmed.IndexOf(':');
            if (colon > 0 && colon + 1 < trimmed.Length)
            {
                return trimmed.Substring(colon + 1).Trim();
            }

            return trimmed;
        }

        private static string SlugifyKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(raw.Length);
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
                string key = StripArchetypePrefix(StripCombatInstancePrefix(monsterId));
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
                    case "monster_goblin_archer":
                    case "kobold_spearman":
                        return "monster_goblin_archer";
                    case "goblin_shaman":
                    case "goblin_chief":
                    case "monster_goblin_shaman":
                        return "monster_goblin_shaman";
                    case "orc_archer":
                    case "orc_shaman":
                    case "monster_orc_shaman":
                        return "monster_orc_shaman";
                    case "orc_warrior":
                    case "orc_champion":
                    case "monster_orc_warrior":
                        return "monster_orc_warrior";
                    case "giant_rat":
                    case "monstrous_rat":
                    case "giant_sewer_rat":
                    case "monster_giant_rat":
                        return "monster_giant_rat";
                    case "giant_spider":
                    case "rock_spider":
                    case "dune_spider":
                    case "silt_spider":
                    case "vesp_spider":
                    case "guardian_spider":
                    case "vixies_spider":
                    case "monster_giant_spider":
                        return "monster_giant_spider";
                    case "skeletal_warrior":
                    case "skeleton_warrior":
                    case "monster_skeleton_warrior":
                        return "monster_skeleton_warrior";
                    case "zombie":
                    case "giant_zombie":
                    case "monster_zombie":
                        return "monster_zombie";
                    case "bandit_cutthroat":
                    case "mercenary_villain":
                    case "thief":
                    case "monster_bandit_cutthroat":
                        return "monster_bandit_cutthroat";
                    case "skeleton_archer":
                    case "monster_skeleton_archer":
                        return "monster_skeleton_archer";
                }
            }

            switch (iconId)
            {
                case 392:
                    return "monster_giant_frog";
                case 384:
                case 387:
                case 390:
                    return "monster_goblin_raider";
                case 388:
                    return "monster_goblin_archer";
                case 508:
                case 389:
                    return "monster_goblin_shaman";
                case 513:
                case 385:
                    return "monster_orc_shaman";
                case 515:
                case 512:
                    return "monster_orc_warrior";
                case 449:
                case 511:
                    return "monster_giant_rat";
                case 468:
                case 402:
                case 403:
                case 413:
                case 414:
                case 424:
                    return "monster_giant_spider";
                case 505:
                    return "monster_skeleton_warrior";
                case 470:
                case 436:
                    return "monster_zombie";
                case 495:
                case 496:
                    return "monster_bandit_cutthroat";
                default:
                    return string.Empty;
            }
        }
    }
}
