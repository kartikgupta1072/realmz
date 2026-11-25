using System;
using System.Collections.Generic;
using System.IO;
using Realmz.Cartography;
using UnityEditor;
using UnityEngine;

namespace Realmz.EditorTools.Cartography
{
    /// <summary>
    /// Configures all textures under Assets/Art/Cartography/ (Point, Uncompressed, MipMap OFF, 64 PPU,
    /// NormalMap+sRGB=false for *_n.png, Repeat for tiles/) and packs the 6-layer 128x128 Texture2DArray
    /// assets for CartographyTerrain.shader, cleanly generating deterministic procedural fallbacks when
    /// source PNGs are not yet present on disk.
    /// </summary>
    public static class CartographyAssetImporter
    {
        public const string CartographyArtRoot = "Assets/Art/Cartography";
        public const string TilesDir = CartographyArtRoot + "/tiles";
        public const string FoliageDir = CartographyArtRoot + "/foliage";
        public const string AtlasesDir = CartographyArtRoot + "/Atlases";
        public const string UnitsDir = CartographyArtRoot + "/units";
        public const string PortraitsDir = CartographyArtRoot + "/Portraits";
        public const string BuildingsDir = CartographyArtRoot + "/Buildings";
        public const string PropsDir = CartographyArtRoot + "/Props";
        public const string SurfacesDir = CartographyArtRoot + "/Surfaces";
        public const string VfxDir = CartographyArtRoot + "/Vfx";
        public const string IconsDir = CartographyArtRoot + "/Icons";
        public const string MaterialsDir = CartographyArtRoot + "/Materials";
        public const string AlbedoArrayAssetPath = TilesDir + "/CartographyTerrainAlbedoArray.asset";
        public const string NormalArrayAssetPath = TilesDir + "/CartographyTerrainNormalArray.asset";
        public const string DungeonAlbedoArrayAssetPath = TilesDir + "/CartographyDungeonTerrainAlbedoArray.asset";
        public const string DungeonNormalArrayAssetPath = TilesDir + "/CartographyDungeonTerrainNormalArray.asset";
        public const string TerrainMaterialPath = MaterialsDir + "/Cartography_Terrain.mat";
        public const string DungeonTerrainMaterialPath = MaterialsDir + "/Cartography_DungeonTerrain.mat";
        public const string FoliageMaterialPath = MaterialsDir + "/Cartography_FoliageAtlas.mat";
        public const string DungeonFoliageMaterialPath = MaterialsDir + "/Cartography_FoliageAtlas_DungeonCave.mat";
        public const string MarshFoliageMaterialPath = MaterialsDir + "/Cartography_FoliageAtlas_MarshAutumn.mat";
        public const string WaterMaterialPath = MaterialsDir + "/Cartography_ShallowWater.mat";
        public const string DungeonWaterMaterialPath = MaterialsDir + "/Cartography_DungeonWater.mat";

        public const int TileLayerSize = 128;
        public const int UnitFrameSize = 128;
        public const int UnitFrameCount = 4;

        public static readonly string[] UnifiedUnitIds =
        {
            "hero_knight",
            "hero_cleric",
            "hero_ranger",
            "hero_mage",
            "hero_paladin",
            "hero_rogue",
            "monster_giant_frog",
            "monster_goblin_raider",
            "monster_goblin_archer",
            "monster_orc_warrior",
            "monster_giant_rat",
            "monster_skeleton_warrior",
            "monster_bandit_cutthroat"
        };

        public static readonly string[] HotbarIconKeys =
        {
            "sword",
            "bow",
            "dagger",
            "staff",
            "fireball",
            "flame_hands",
            "magic_darts",
            "heal",
            "buff",
            "daze",
            "shove",
            "potion",
            "guard",
            "undo",
            "end_turn",
            "auto_combat",
            "speed",
            "audio",
            "grid",
            "settings",
            "flee"
        };

        private static readonly string[] TerrainLayerNames =
        {
            "meadow_grass",
            "moss_patch",
            "dirt_road",
            "cobble_plaza",
            "river_mud",
            "cliff_rock",
            "forest_root_dirt",
            "dark_forest_moss",
            "fallen_leaves_loam",
            "highland_scree",
            "marsh_bog_turf",
            "swamp_mire_mud",
            "stagnant_reeds_water",
            "alpine_frost_grass",
            "packed_snow_trail",
            "hearth_ash_stone",
            "inn_wood_planks",
            "temple_marble_tile",
            "shallow_water"
        };

        private static readonly string[] DungeonTerrainLayerNames =
        {
            "dungeon_cavern_stone",
            "dungeon_cracked_slate",
            "dungeon_moss_cobble",
            "dungeon_chasm_void",
            "granite_cliff_rock",
            "subterranean_water"
        };

        [MenuItem("Realmz/Cartography/Import Assets & Build Texture Arrays")]
        public static void ImportAll()
        {
            EnsureFolder(CartographyArtRoot);
            EnsureFolder(TilesDir);
            EnsureFolder(FoliageDir);
            EnsureFolder(UnitsDir);
            EnsureFolder(IconsDir);
            EnsureFolder(MaterialsDir);

            AssetDatabase.Refresh();
            ConfigureAllPngsOnDisk();
            AssetDatabase.Refresh();

            BuildTerrainTextureArrays(out Texture2DArray albedoArray, out Texture2DArray normalArray);
            CreateOrUpdateTerrainMaterial(albedoArray, normalArray);
            BuildDungeonTerrainTextureArrays(out Texture2DArray dungeonAlbedoArray, out Texture2DArray dungeonNormalArray);
            CreateOrUpdateDungeonTerrainMaterial(dungeonAlbedoArray, dungeonNormalArray);
            CreateOrUpdateFoliageMaterial();
            CreateOrUpdateDungeonFoliageMaterial();
            CreateOrUpdateMarshFoliageMaterial();
            CreateOrUpdateWaterMaterial();
            CreateOrUpdateDungeonWaterMaterial();
            CreateOrUpdateAllUnitMaterials();
            foreach (CartographyPropKind kind in Enum.GetValues(typeof(CartographyPropKind)))
            {
                if (kind != CartographyPropKind.None)
                {
                    LoadOrCreatePropMaterial(kind, out _);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CartographyAssetImporter] Imported Cartography assets and rebuilt Texture2DArrays.");
        }

        public static void ConfigureAllPngsOnDisk()
        {
            string fullRoot = Path.GetFullPath(CartographyArtRoot);
            if (!Directory.Exists(fullRoot))
            {
                return;
            }

            string[] pngs = Directory.GetFiles(fullRoot, "*.png", SearchOption.AllDirectories);
            for (int i = 0; i < pngs.Length; i++)
            {
                string relPath = ToAssetRelativePath(pngs[i]);
                ConfigurePngImporter(relPath);
            }
        }

        public static void ConfigurePngImporter(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            bool isNormal = assetPath.EndsWith("_n.png", StringComparison.OrdinalIgnoreCase);
            bool isTile = assetPath.IndexOf("/tiles/", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isFoliageAtlas = assetPath.IndexOf("/foliage/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  assetPath.IndexOf("/Atlases/", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isIcon = !isNormal && assetPath.IndexOf("/Icons/", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isPortrait = !isNormal && assetPath.IndexOf("/Portraits/", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isUnitSheet = !isNormal &&
                               assetPath.IndexOf("/units/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                               assetPath.EndsWith("_sheet.png", StringComparison.OrdinalIgnoreCase);
            bool isProp4f = !isNormal &&
                            assetPath.IndexOf("/Props/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            assetPath.EndsWith("_4f.png", StringComparison.OrdinalIgnoreCase);
            bool isSurface = !isNormal && assetPath.IndexOf("/Surfaces/", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isVfx = !isNormal && assetPath.IndexOf("/Vfx/", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isPropsPath = assetPath.IndexOf("/Props/", StringComparison.OrdinalIgnoreCase) >= 0;
            string fileName = Path.GetFileName(assetPath);
            bool isFlatFloorProp = !isNormal &&
                                   (assetPath.EndsWith("/dungeon_iron_grate.png", StringComparison.OrdinalIgnoreCase) ||
                                    assetPath.EndsWith("/dungeon_floor_grate_rusted.png", StringComparison.OrdinalIgnoreCase) ||
                                    assetPath.EndsWith("/dungeon_spike_trap.png", StringComparison.OrdinalIgnoreCase) ||
                                    (isPropsPath && fileName.StartsWith("decal_", StringComparison.OrdinalIgnoreCase)));

            bool dirty = false;

            if (isNormal)
            {
                if (importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    dirty = true;
                }
                if (importer.convertToNormalmap)
                {
                    importer.convertToNormalmap = false;
                    dirty = true;
                }
                if (importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    dirty = true;
                }
                TextureWrapMode desiredWrap = isTile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                if (importer.wrapMode != desiredWrap)
                {
                    importer.wrapMode = desiredWrap;
                    dirty = true;
                }
            }
            else if (isTile || isFoliageAtlas)
            {
                if (importer.textureType != TextureImporterType.Default)
                {
                    importer.textureType = TextureImporterType.Default;
                    dirty = true;
                }
                if (!importer.sRGBTexture)
                {
                    importer.sRGBTexture = true;
                    dirty = true;
                }
                if (isTile && importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = false;
                    dirty = true;
                }
                if (isFoliageAtlas && !importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    dirty = true;
                }
                TextureWrapMode desiredWrap = isTile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                if (importer.wrapMode != desiredWrap)
                {
                    importer.wrapMode = desiredWrap;
                    dirty = true;
                }
            }
            else if (isIcon || isPortrait)
            {
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    dirty = true;
                }
                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    dirty = true;
                }
                if (!importer.sRGBTexture)
                {
                    importer.sRGBTexture = true;
                    dirty = true;
                }
                if (!importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    dirty = true;
                }
                if (importer.wrapMode != TextureWrapMode.Clamp)
                {
                    importer.wrapMode = TextureWrapMode.Clamp;
                    dirty = true;
                }

                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                if (settings.spriteAlignment != (int)SpriteAlignment.Center ||
                    (settings.spritePivot - new Vector2(0.5f, 0.5f)).sqrMagnitude > 0.0001f)
                {
                    settings.spriteAlignment = (int)SpriteAlignment.Center;
                    settings.spritePivot = new Vector2(0.5f, 0.5f);
                    importer.SetTextureSettings(settings);
                    dirty = true;
                }
            }
            else
            {
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    dirty = true;
                }

                importer.GetSourceTextureWidthAndHeight(out int texW, out int texH);
                bool sliceAs4Frames = ((isUnitSheet || isSurface || isVfx) &&
                                       texW >= UnitFrameSize * UnitFrameCount &&
                                       texH == UnitFrameSize) ||
                                      (isProp4f &&
                                       texH > 0 &&
                                       texW >= texH * 3 &&
                                       texW % UnitFrameCount == 0);
                SpriteImportMode desiredMode = sliceAs4Frames ? SpriteImportMode.Multiple : SpriteImportMode.Single;
                if (importer.spriteImportMode != desiredMode)
                {
                    importer.spriteImportMode = desiredMode;
                    dirty = true;
                }
                if (!importer.sRGBTexture)
                {
                    importer.sRGBTexture = true;
                    dirty = true;
                }
                if (!importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    dirty = true;
                }
                if (importer.wrapMode != TextureWrapMode.Clamp)
                {
                    importer.wrapMode = TextureWrapMode.Clamp;
                    dirty = true;
                }

                bool useCenterPivot = isSurface || isVfx || isFlatFloorProp;
                SpriteAlignment desiredAlign = useCenterPivot ? SpriteAlignment.Center : SpriteAlignment.BottomCenter;
                Vector2 desiredPivot = useCenterPivot ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f);

                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                if (settings.spriteAlignment != (int)desiredAlign ||
                    (settings.spritePivot - desiredPivot).sqrMagnitude > 0.0001f)
                {
                    settings.spriteAlignment = (int)desiredAlign;
                    settings.spritePivot = desiredPivot;
                    importer.SetTextureSettings(settings);
                    dirty = true;
                }

                if (sliceAs4Frames && EnsureFourFrameSpriteSheet(importer, assetPath, texW, texH, desiredAlign, desiredPivot))
                {
                    dirty = true;
                }
            }

            // Apply crisp pixel settings AFTER textureType assignment so switching to NormalMap/Sprite never resets mipmaps or filterMode
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                dirty = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }
            if (Mathf.Abs(importer.spritePixelsPerUnit - 64f) > 0.01f)
            {
                importer.spritePixelsPerUnit = 64f;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        private static bool EnsureFourFrameSpriteSheet(
            TextureImporter importer,
            string assetPath,
            int texW,
            int texH,
            SpriteAlignment alignment,
            Vector2 pivot)
        {
            int frameW = texW / UnitFrameCount;
            int frameH = texH;
            string baseName = Path.GetFileNameWithoutExtension(assetPath);

            SerializedObject so = new SerializedObject(importer);
            SerializedProperty spritesProp = so.FindProperty("m_SpriteSheet.m_Sprites");
            if (spritesProp == null || !spritesProp.isArray)
            {
                return false;
            }

            bool needsUpdate = spritesProp.arraySize != UnitFrameCount;
            if (!needsUpdate)
            {
                for (int i = 0; i < UnitFrameCount; i++)
                {
                    SerializedProperty elem = spritesProp.GetArrayElementAtIndex(i);
                    string expectedName = $"{baseName}_{i}";
                    Rect curRect = elem.FindPropertyRelative("m_Rect").rectValue;
                    Vector2 curPivot = elem.FindPropertyRelative("m_Pivot").vector2Value;
                    int curAlign = elem.FindPropertyRelative("m_Alignment").intValue;
                    string curName = elem.FindPropertyRelative("m_Name").stringValue;
                    if (!string.Equals(curName, expectedName, StringComparison.Ordinal) ||
                        Mathf.Abs(curRect.x - i * frameW) > 0.1f ||
                        Mathf.Abs(curRect.y) > 0.1f ||
                        Mathf.Abs(curRect.width - frameW) > 0.1f ||
                        Mathf.Abs(curRect.height - frameH) > 0.1f ||
                        curAlign != (int)alignment ||
                        (curPivot - pivot).sqrMagnitude > 0.0001f)
                    {
                        needsUpdate = true;
                        break;
                    }
                }
            }

            if (!needsUpdate)
            {
                return false;
            }

            spritesProp.arraySize = UnitFrameCount;
            for (int i = 0; i < UnitFrameCount; i++)
            {
                SerializedProperty elem = spritesProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("m_Name").stringValue = $"{baseName}_{i}";
                elem.FindPropertyRelative("m_Rect").rectValue = new Rect(i * frameW, 0f, frameW, frameH);
                elem.FindPropertyRelative("m_Alignment").intValue = (int)alignment;
                elem.FindPropertyRelative("m_Pivot").vector2Value = pivot;
                SerializedProperty borderProp = elem.FindPropertyRelative("m_Border");
                if (borderProp != null)
                {
                    borderProp.vector4Value = Vector4.zero;
                }
                SerializedProperty spriteIdProp = elem.FindPropertyRelative("m_SpriteID");
                if (spriteIdProp != null && string.IsNullOrEmpty(spriteIdProp.stringValue))
                {
                    spriteIdProp.stringValue = ComputeDeterministicSpriteGuidHex(assetPath, i);
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static string ComputeDeterministicSpriteGuidHex(string assetPath, int frameIndex)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] input = System.Text.Encoding.UTF8.GetBytes($"{assetPath}#{frameIndex}");
                byte[] hash = md5.ComputeHash(input);
                System.Text.StringBuilder sb = new System.Text.StringBuilder(32);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public static void CreateOrUpdateAllUnitMaterials()
        {
            HashSet<string> processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < UnifiedUnitIds.Length; i++)
            {
                if (processed.Add(UnifiedUnitIds[i]))
                {
                    CreateOrUpdateUnitMaterial(UnifiedUnitIds[i]);
                }
            }

            string fullUnitsDir = Path.GetFullPath(UnitsDir);
            if (Directory.Exists(fullUnitsDir))
            {
                string[] sheets = Directory.GetFiles(fullUnitsDir, "*_sheet.png", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < sheets.Length; i++)
                {
                    string fileName = Path.GetFileName(sheets[i]);
                    if (fileName.EndsWith("_sheet.png", StringComparison.OrdinalIgnoreCase))
                    {
                        string unitId = fileName.Substring(0, fileName.Length - "_sheet.png".Length);
                        if (processed.Add(unitId))
                        {
                            CreateOrUpdateUnitMaterial(unitId);
                        }
                    }
                }
            }
        }

        public static Material CreateOrUpdateUnitMaterial(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return null;
            }

            string normalized = unitId.Trim().ToLowerInvariant();
            string albedoPath = $"{UnitsDir}/{normalized}_sheet.png";
            string normalPath = $"{UnitsDir}/{normalized}_sheet_n.png";
            string matPath = $"{MaterialsDir}/Cartography_Unit_{normalized}.mat";

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            Shader shader = Shader.Find("Realmz/CartographyBillboard") ?? Shader.Find("Universal Render Pipeline/Lit");

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

            mat.enableInstancing = false;
            if (albedo != null)
            {
                mat.mainTexture = albedo;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
            }
            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
            }
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 0.70f);
            if (mat.HasProperty("_IsTwoBankAtlas")) mat.SetFloat("_IsTwoBankAtlas", 0f);
            if (mat.HasProperty("_Cols")) mat.SetFloat("_Cols", UnitFrameCount);
            if (mat.HasProperty("_Rows")) mat.SetFloat("_Rows", 1f);
            if (mat.HasProperty("_VariantIndex")) mat.SetFloat("_VariantIndex", 0f);
            if (mat.HasProperty("_FrameCount")) mat.SetFloat("_FrameCount", UnitFrameCount);
            if (mat.HasProperty("_AnimFps")) mat.SetFloat("_AnimFps", 5.5f);
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

        public static Sprite LoadFirstSpriteFrame(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            Sprite first = null;
            float bestX = float.MaxValue;
            if (subAssets != null)
            {
                for (int i = 0; i < subAssets.Length; i++)
                {
                    if (subAssets[i] is Sprite s && s.rect.x < bestX)
                    {
                        bestX = s.rect.x;
                        first = s;
                    }
                }
            }

            return first != null ? first : AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        public static Sprite[] LoadAllSpriteFrames(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return Array.Empty<Sprite>();
            }

            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            List<Sprite> frames = new List<Sprite>(4);
            if (subAssets != null)
            {
                for (int i = 0; i < subAssets.Length; i++)
                {
                    if (subAssets[i] is Sprite s)
                    {
                        frames.Add(s);
                    }
                }
            }

            if (frames.Count == 0)
            {
                Sprite single = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (single != null)
                {
                    frames.Add(single);
                }
            }

            frames.Sort((a, b) => a.rect.x.CompareTo(b.rect.x));
            return frames.ToArray();
        }

        public static void BuildTerrainTextureArrays(out Texture2DArray albedoArray, out Texture2DArray normalArray)
        {
            PackTerrainTextureArrays(
                TerrainLayerNames,
                AlbedoArrayAssetPath,
                NormalArrayAssetPath,
                "CartographyTerrainAlbedoArray",
                "CartographyTerrainNormalArray",
                out albedoArray,
                out normalArray);
        }

        public static void BuildDungeonTerrainTextureArrays(out Texture2DArray albedoArray, out Texture2DArray normalArray)
        {
            PackTerrainTextureArrays(
                DungeonTerrainLayerNames,
                DungeonAlbedoArrayAssetPath,
                DungeonNormalArrayAssetPath,
                "CartographyDungeonTerrainAlbedoArray",
                "CartographyDungeonTerrainNormalArray",
                out albedoArray,
                out normalArray);
        }

        private static void PackTerrainTextureArrays(
            string[] layerNames,
            string albedoAssetPath,
            string normalAssetPath,
            string albedoName,
            string normalName,
            out Texture2DArray albedoArray,
            out Texture2DArray normalArray)
        {
            int sliceCount = layerNames.Length;

            Texture2DArray existingAlbedo = AssetDatabase.LoadAssetAtPath<Texture2DArray>(albedoAssetPath);
            bool createAlbedo = existingAlbedo == null || existingAlbedo.width != TileLayerSize ||
                                existingAlbedo.height != TileLayerSize || existingAlbedo.depth != sliceCount;
            bool copyIntoExistingAlbedo = !createAlbedo && !existingAlbedo.isReadable;
            if (createAlbedo || copyIntoExistingAlbedo)
            {
                albedoArray = new Texture2DArray(
                    TileLayerSize,
                    TileLayerSize,
                    sliceCount,
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: false)
                {
                    name = albedoName,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };
            }
            else
            {
                albedoArray = existingAlbedo;
            }

            Texture2DArray existingNormal = AssetDatabase.LoadAssetAtPath<Texture2DArray>(normalAssetPath);
            bool createNormal = existingNormal == null || existingNormal.width != TileLayerSize ||
                                existingNormal.height != TileLayerSize || existingNormal.depth != sliceCount;
            bool copyIntoExistingNormal = !createNormal && !existingNormal.isReadable;
            if (createNormal || copyIntoExistingNormal)
            {
                normalArray = new Texture2DArray(
                    TileLayerSize,
                    TileLayerSize,
                    sliceCount,
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: true)
                {
                    name = normalName,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };
            }
            else
            {
                normalArray = existingNormal;
            }

            for (int slice = 0; slice < sliceCount; slice++)
            {
                string layerName = layerNames[slice];
                string albPath = TilesDir + "/" + layerName + ".png";
                string nrmPath = TilesDir + "/" + layerName + "_n.png";

                Color32[] albPixels = TryLoadPngPixelsResampled(albPath, TileLayerSize, TileLayerSize, linear: false)
                    ?? GenerateProceduralTerrainSlice(slice % 6, TileLayerSize);
                Color32[] nrmPixels = TryLoadPngPixelsResampled(nrmPath, TileLayerSize, TileLayerSize, linear: true)
                    ?? GenerateProceduralNormalSlice(slice % 6, TileLayerSize);

                albedoArray.SetPixels32(albPixels, slice);
                normalArray.SetPixels32(nrmPixels, slice);
            }

            albedoArray.filterMode = FilterMode.Point;
            albedoArray.wrapMode = TextureWrapMode.Repeat;
            albedoArray.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            normalArray.filterMode = FilterMode.Point;
            normalArray.wrapMode = TextureWrapMode.Repeat;
            normalArray.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            if (createAlbedo)
            {
                AssetDatabase.CreateAsset(albedoArray, albedoAssetPath);
            }
            else if (copyIntoExistingAlbedo)
            {
                EditorUtility.CopySerialized(albedoArray, existingAlbedo);
                UnityEngine.Object.DestroyImmediate(albedoArray);
                albedoArray = existingAlbedo;
                EditorUtility.SetDirty(albedoArray);
            }
            else
            {
                EditorUtility.SetDirty(albedoArray);
            }

            if (createNormal)
            {
                AssetDatabase.CreateAsset(normalArray, normalAssetPath);
            }
            else if (copyIntoExistingNormal)
            {
                EditorUtility.CopySerialized(normalArray, existingNormal);
                UnityEngine.Object.DestroyImmediate(normalArray);
                normalArray = existingNormal;
                EditorUtility.SetDirty(normalArray);
            }
            else
            {
                EditorUtility.SetDirty(normalArray);
            }
        }

        public static Material CreateOrUpdateTerrainMaterial(Texture2DArray albedoArray, Texture2DArray normalArray)
        {
            return CreateOrUpdateTerrainMaterialAtPath(
                TerrainMaterialPath,
                albedoArray,
                normalArray,
                groundValueScale: 0.76f,
                roadValueBoost: 0.86f,
                horizonHazeColor: new Color(0.91f, 0.83f, 0.70f, 1f),
                horizonHazeStrength: 0.09f);
        }

        public static Material CreateOrUpdateDungeonTerrainMaterial(Texture2DArray albedoArray, Texture2DArray normalArray)
        {
            return CreateOrUpdateTerrainMaterialAtPath(
                DungeonTerrainMaterialPath,
                albedoArray,
                normalArray,
                groundValueScale: 0.42f,
                roadValueBoost: 0.46f,
                horizonHazeColor: new Color(0.05f, 0.07f, 0.12f, 1f),
                horizonHazeStrength: 0.22f);
        }

        private static Material CreateOrUpdateTerrainMaterialAtPath(
            string matPath,
            Texture2DArray albedoArray,
            Texture2DArray normalArray,
            float groundValueScale,
            float roadValueBoost,
            Color horizonHazeColor,
            float horizonHazeStrength)
        {
            Shader shader = Shader.Find("Realmz/CartographyTerrain") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            if (mat.HasProperty("_TerrainArray")) mat.SetTexture("_TerrainArray", albedoArray);
            if (mat.HasProperty("_NormalArray")) mat.SetTexture("_NormalArray", normalArray);
            if (mat.HasProperty("_WorldTexelScale")) mat.SetFloat("_WorldTexelScale", 0.5f);
            if (mat.HasProperty("_TexelResolution")) mat.SetFloat("_TexelResolution", TileLayerSize);
            if (mat.HasProperty("_NoiseBlendSharpness")) mat.SetFloat("_NoiseBlendSharpness", 18f);
            if (mat.HasProperty("_NoiseMaskStrength")) mat.SetFloat("_NoiseMaskStrength", 0.55f);
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1.0f);
            if (mat.HasProperty("_CreaseAOStrength")) mat.SetFloat("_CreaseAOStrength", 1.0f);
            if (mat.HasProperty("_MacroScale")) mat.SetFloat("_MacroScale", 0.085f);
            if (mat.HasProperty("_MacroStrength")) mat.SetFloat("_MacroStrength", 0.10f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.06f);
            if (mat.HasProperty("_GroundValueScale")) mat.SetFloat("_GroundValueScale", groundValueScale);
            if (mat.HasProperty("_RoadValueBoost")) mat.SetFloat("_RoadValueBoost", roadValueBoost);
            if (mat.HasProperty("_HorizonHazeColor")) mat.SetColor("_HorizonHazeColor", horizonHazeColor);
            if (mat.HasProperty("_HorizonHazeStrength")) mat.SetFloat("_HorizonHazeStrength", horizonHazeStrength);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Material CreateOrUpdateFoliageMaterial()
        {
            return CreateOrUpdateFoliageMaterialAtPath(
                FoliageMaterialPath,
                new[]
                {
                    FoliageDir + "/bywater_foliage_atlas.png",
                    FoliageDir + "/foliage_atlas.png",
                    FoliageDir + "/cartography_foliage_atlas.png",
                    CartographyArtRoot + "/bywater_foliage_atlas.png",
                    CartographyArtRoot + "/foliage_atlas.png"
                },
                new[]
                {
                    FoliageDir + "/bywater_foliage_atlas_n.png",
                    FoliageDir + "/foliage_atlas_n.png",
                    CartographyArtRoot + "/bywater_foliage_atlas_n.png"
                },
                new Color(0.44f, 0.52f, 0.42f, 1f),
                fallbackIfMissing: true);
        }

        public static Material CreateOrUpdateDungeonFoliageMaterial()
        {
            return CreateOrUpdateFoliageMaterialAtPath(
                DungeonFoliageMaterialPath,
                new[] { AtlasesDir + "/foliage_atlas_dungeon_cave.png" },
                new[] { AtlasesDir + "/foliage_atlas_dungeon_cave_n.png" },
                new Color(0.28f, 0.34f, 0.38f, 1f),
                fallbackIfMissing: false);
        }

        public static Material CreateOrUpdateMarshFoliageMaterial()
        {
            return CreateOrUpdateFoliageMaterialAtPath(
                MarshFoliageMaterialPath,
                new[] { AtlasesDir + "/foliage_atlas_marsh_autumn.png" },
                new[] { AtlasesDir + "/foliage_atlas_marsh_autumn_n.png" },
                new Color(0.42f, 0.38f, 0.28f, 1f),
                fallbackIfMissing: false);
        }

        private static Material CreateOrUpdateFoliageMaterialAtPath(
            string matPath,
            string[] albedoCandidates,
            string[] normalCandidates,
            Color rootTint,
            bool fallbackIfMissing)
        {
            Shader shader = Shader.Find("Realmz/CartographyBillboard") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            Texture2D atlas = FindFirstTextureInCartography(albedoCandidates);
            if (atlas == null && fallbackIfMissing)
            {
                atlas = EnsureFallbackFoliageAtlasAsset();
            }

            Texture2D atlasNormal = FindFirstTextureInCartography(normalCandidates);

            mat.enableInstancing = true;
            if (atlas != null)
            {
                mat.mainTexture = atlas;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", atlas);
            }
            if (mat.HasProperty("_BumpMap") && atlasNormal != null) mat.SetTexture("_BumpMap", atlasNormal);
            if (mat.HasProperty("_IsTwoBankAtlas")) mat.SetFloat("_IsTwoBankAtlas", 1f);
            if (mat.HasProperty("_BillboardMode")) mat.SetFloat("_BillboardMode", 1f);
            if (mat.HasProperty("_CameraPitchTiltDeg")) mat.SetFloat("_CameraPitchTiltDeg", 22f);
            if (mat.HasProperty("_NormalUpBias")) mat.SetFloat("_NormalUpBias", 0.72f);
            if (mat.HasProperty("_FrameCount")) mat.SetFloat("_FrameCount", 8f);
            if (mat.HasProperty("_AnimFps")) mat.SetFloat("_AnimFps", 7f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.42f);
            if (mat.HasProperty("_RootTint")) mat.SetColor("_RootTint", rootTint);
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Material CreateOrUpdateWaterMaterial()
        {
            return CreateOrUpdateWaterMaterialAtPath(
                WaterMaterialPath,
                new[]
                {
                    TilesDir + "/shallow_water.png",
                    "Assets/Art/M1/Tiles/shallow_water.png"
                },
                new[]
                {
                    TilesDir + "/shallow_water_n.png",
                    "Assets/Art/M1/Tiles/shallow_water_n.png"
                },
                new Color(0.16f, 0.38f, 0.46f, 1f),
                new Color(0.94f, 0.99f, 1.0f, 1f),
                0.34f);
        }

        public static Material CreateOrUpdateDungeonWaterMaterial()
        {
            return CreateOrUpdateWaterMaterialAtPath(
                DungeonWaterMaterialPath,
                new[] { TilesDir + "/subterranean_water.png" },
                new[] { TilesDir + "/subterranean_water_n.png" },
                new Color(0.08f, 0.16f, 0.24f, 1f),
                new Color(0.54f, 0.78f, 0.92f, 1f),
                0.24f);
        }

        private static Material CreateOrUpdateWaterMaterialAtPath(
            string matPath,
            string[] albedoCandidates,
            string[] normalCandidates,
            Color baseColor,
            Color shimmerColor,
            float shimmerStrength)
        {
            Shader shader = Shader.Find("Realmz/CartographyWater")
                ?? Shader.Find("Universal Render Pipeline/Lit");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            Texture2D waterAlbedo = FindFirstTextureInCartography(albedoCandidates);
            Texture2D waterNormal = FindFirstTextureInCartography(normalCandidates);

            if (mat.HasProperty("_BaseMap") && waterAlbedo != null) mat.SetTexture("_BaseMap", waterAlbedo);
            if (mat.HasProperty("_BumpMap") && waterNormal != null) mat.SetTexture("_BumpMap", waterNormal);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_ShimmerColor")) mat.SetColor("_ShimmerColor", shimmerColor);
            if (mat.HasProperty("_ShimmerStrength")) mat.SetFloat("_ShimmerStrength", shimmerStrength);
            if (mat.HasProperty("_WaveScale")) mat.SetFloat("_WaveScale", 2.0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Material CreateOrUpdateStripMaterial(string key, Texture2D albedo, Texture2D normal, bool isVfx)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            string matPath = $"{MaterialsDir}/Cartography_{(isVfx ? "Vfx" : "Surface")}_{key}.mat";
            Shader shader = Shader.Find("Realmz/CartographyBillboard") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.enableInstancing = false;
            if (albedo != null)
            {
                mat.mainTexture = albedo;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
            }
            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
            }
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 0.55f);
            if (mat.HasProperty("_IsTwoBankAtlas")) mat.SetFloat("_IsTwoBankAtlas", 0f);
            if (mat.HasProperty("_Cols")) mat.SetFloat("_Cols", UnitFrameCount);
            if (mat.HasProperty("_Rows")) mat.SetFloat("_Rows", 1f);
            if (mat.HasProperty("_VariantIndex")) mat.SetFloat("_VariantIndex", 0f);
            if (mat.HasProperty("_FrameCount")) mat.SetFloat("_FrameCount", UnitFrameCount);
            if (mat.HasProperty("_AnimFps")) mat.SetFloat("_AnimFps", isVfx ? 10.0f : 6.0f);
            if (mat.HasProperty("_BillboardMode")) mat.SetFloat("_BillboardMode", 0f);
            if (mat.HasProperty("_CameraPitchTiltDeg")) mat.SetFloat("_CameraPitchTiltDeg", 0f);
            if (mat.HasProperty("_WindTipSway")) mat.SetFloat("_WindTipSway", 0f);
            if (mat.HasProperty("_TrampleStrength")) mat.SetFloat("_TrampleStrength", 0f);
            if (mat.HasProperty("_NormalUpBias")) mat.SetFloat("_NormalUpBias", 0.85f);
            if (mat.HasProperty("_RootTint")) mat.SetColor("_RootTint", Color.white);
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.35f);
            if (mat.HasProperty("_ReceiveRootShadowOnly")) mat.SetFloat("_ReceiveRootShadowOnly", 0f);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", isVfx ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.black);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Vector2 GetPropSizeMeters(CartographyPropKind kind, Texture2D tex)
        {
            if (kind == CartographyPropKind.Campfire4f || kind == CartographyPropKind.DungeonWallSconce4f)
            {
                return new Vector2(2.0f, 2.0f);
            }

            if (tex != null && tex.width > 0 && tex.height > 0)
            {
                bool is4Frame = kind == CartographyPropKind.Knight ||
                                kind == CartographyPropKind.GiantFrog ||
                                tex.width >= tex.height * 3;
                float effectiveW = is4Frame ? (tex.width / (float)UnitFrameCount) : tex.width;
                return new Vector2(effectiveW / 64f, tex.height / 64f);
            }

            return CartographyWorldData.GetDefaultPropSizeMeters(kind);
        }

        public static Material LoadOrCreatePropMaterial(CartographyPropKind kind, out Texture2D tex)
        {
            string key = GetPropKey(kind);
            tex = FindPropTexture(kind, out Texture2D normalTex);
            if (tex == null)
            {
                tex = EnsureFallbackPropTextureAsset(kind);
            }

            string matPath = MaterialsDir + "/Cartography_Prop_" + key + ".mat";
            Shader shader = Shader.Find("Realmz/CartographyBillboard") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            bool is4FrameSheet = CartographyWorldData.IsAnimated4FrameKind(kind) ||
                                 (tex != null && tex.height > 0 && tex.width >= tex.height * 3);
            bool isBridge = kind == CartographyPropKind.StoneBridge ||
                            kind == CartographyPropKind.WoodenPlankBridge;
            bool isFlatFloor = CartographyWorldData.IsFloorDecalKind(kind);
            bool isStoneFenceV = kind == CartographyPropKind.StoneFenceV;
            bool isTree = kind == CartographyPropKind.TreeOakA || kind == CartographyPropKind.TreeOakB ||
                          kind == CartographyPropKind.TreePineA || kind == CartographyPropKind.TreePineB ||
                          kind == CartographyPropKind.TreeWillowA ||
                          kind == CartographyPropKind.GnarledDeadTree;
            bool isBuilding = kind == CartographyPropKind.BywaterInn ||
                              kind == CartographyPropKind.Blacksmith ||
                              kind == CartographyPropKind.Apothecary ||
                              kind == CartographyPropKind.Temple ||
                              kind == CartographyPropKind.Guildhall ||
                              kind == CartographyPropKind.CottageB ||
                              kind == CartographyPropKind.CottageC ||
                              kind == CartographyPropKind.Watchtower;
            bool isFire = kind == CartographyPropKind.TorchBrazier ||
                          kind == CartographyPropKind.Campfire4f ||
                          kind == CartographyPropKind.DungeonWallSconce4f;

            mat.enableInstancing = false;
            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            }
            if (mat.HasProperty("_BumpMap") && normalTex != null) mat.SetTexture("_BumpMap", normalTex);
            if (mat.HasProperty("_IsTwoBankAtlas")) mat.SetFloat("_IsTwoBankAtlas", 0f);
            if (mat.HasProperty("_Cols")) mat.SetFloat("_Cols", is4FrameSheet ? 4f : 1f);
            if (mat.HasProperty("_Rows")) mat.SetFloat("_Rows", 1f);
            if (mat.HasProperty("_VariantIndex")) mat.SetFloat("_VariantIndex", 0f);
            if (mat.HasProperty("_FrameCount")) mat.SetFloat("_FrameCount", is4FrameSheet ? 4f : 1f);
            if (mat.HasProperty("_AnimFps")) mat.SetFloat("_AnimFps", is4FrameSheet ? 5.5f : 0f);
            if (mat.HasProperty("_BillboardMode")) mat.SetFloat("_BillboardMode", 0f);
            if (mat.HasProperty("_CameraPitchTiltDeg"))
            {
                float pitchTiltDeg = isStoneFenceV ? 30f : ((isBridge || isFlatFloor) ? 4f : (isTree ? 14f : 12f));
                mat.SetFloat("_CameraPitchTiltDeg", pitchTiltDeg);
            }
            if (mat.HasProperty("_WindTipSway")) mat.SetFloat("_WindTipSway", isTree ? 0.028f : 0f);
            if (mat.HasProperty("_TrampleStrength")) mat.SetFloat("_TrampleStrength", 0f);
            if (mat.HasProperty("_NormalUpBias")) mat.SetFloat("_NormalUpBias", (isBridge || isFlatFloor) ? 0.85f : 0.65f);
            if (mat.HasProperty("_RootTint")) mat.SetColor("_RootTint", new Color(0.68f, 0.70f, 0.68f, 1f));
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
            if (mat.HasProperty("_ReceiveRootShadowOnly")) mat.SetFloat("_ReceiveRootShadowOnly", 1f);

            Color emissionColor = Color.black;
            float emissionWarmMask = 1f;
            float emissionMinV = 0f;
            if (isFire)
            {
                emissionColor = new Color(2.4f, 1.15f, 0.30f, 1f);
                emissionWarmMask = 1f;
                emissionMinV = kind == CartographyPropKind.TorchBrazier ? 0.44f : 0.30f;
            }
            else if (kind == CartographyPropKind.TownLanternPost4f)
            {
                emissionColor = new Color(1.0f, 0.72f, 0.30f, 1f);
                emissionWarmMask = 1f;
                emissionMinV = 0.50f;
            }
            else if (kind == CartographyPropKind.RuneObelisk4f)
            {
                emissionColor = new Color(0.08f, 0.20f, 0.35f, 1f);
                emissionWarmMask = 0f;
                emissionMinV = 0f;
            }
            else if (isBuilding)
            {
                emissionColor = new Color(0.85f, 0.56f, 0.18f, 1f);
                emissionWarmMask = 1f;
                emissionMinV = 0.16f;
            }
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
            if (mat.HasProperty("_EmissionWarmMask")) mat.SetFloat("_EmissionWarmMask", emissionWarmMask);
            if (mat.HasProperty("_EmissionMinV")) mat.SetFloat("_EmissionMinV", emissionMinV);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D FindPropTexture(CartographyPropKind kind, out Texture2D normalTex)
        {
            normalTex = null;
            string[] candidates = GetPropTextureCandidates(kind);
            for (int i = 0; i < candidates.Length; i++)
            {
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(candidates[i]);
                if (t != null)
                {
                    string nPath = candidates[i].Replace(".png", "_n.png");
                    normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
                    return t;
                }
            }
            return null;
        }

        private static string[] GetPropTextureCandidates(CartographyPropKind kind)
        {
            switch (kind)
            {
                case CartographyPropKind.BywaterInn:
                    return new[]
                    {
                        CartographyArtRoot + "/structures/bywater_inn.png",
                        BuildingsDir + "/bywater_inn.png",
                        PropsDir + "/bywater_inn.png",
                        "Assets/Art/M1/Props/tavern.png",
                        "Assets/Art/M1/Props/cottage_medieval.png"
                    };
                case CartographyPropKind.StoneBridge:
                    return new[]
                    {
                        CartographyArtRoot + "/structures/stone_bridge.png",
                        PropsDir + "/stone_bridge.png",
                        BuildingsDir + "/stone_bridge.png"
                    };
                case CartographyPropKind.StoneFenceH:
                    return new[]
                    {
                        CartographyArtRoot + "/structures/stone_fence_h.png",
                        PropsDir + "/stone_fence_h.png",
                        PropsDir + "/stone_fence.png",
                        "Assets/Art/M1/Props/stone_fence.png"
                    };
                case CartographyPropKind.StoneFenceV:
                    return new[]
                    {
                        CartographyArtRoot + "/structures/stone_fence_v.png",
                        PropsDir + "/stone_fence_v.png",
                        PropsDir + "/stone_fence.png",
                        "Assets/Art/M1/Props/stone_fence.png"
                    };
                case CartographyPropKind.Knight:
                    return new[]
                    {
                        UnitsDir + "/hero_knight_sheet.png",
                        UnitsDir + "/knight.png",
                        PropsDir + "/knight.png",
                        "Assets/Art/M1/Heroes/knight.png"
                    };
                case CartographyPropKind.GiantFrog:
                    return new[]
                    {
                        UnitsDir + "/monster_giant_frog_sheet.png",
                        UnitsDir + "/giant_frog.png",
                        PropsDir + "/giant_frog.png",
                        "Assets/Art/M1/Monsters/giant_frog.png"
                    };
                case CartographyPropKind.TreeOakA:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_oak_a.png",
                        PropsDir + "/tree_oak_a.png",
                        "Assets/Art/M1/Props/tree_oak.png"
                    };
                case CartographyPropKind.TreeOakB:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_oak_b.png",
                        PropsDir + "/tree_oak_b.png",
                        "Assets/Art/M1/Props/tree_oak.png"
                    };
                case CartographyPropKind.TreePineA:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_pine_a.png",
                        PropsDir + "/tree_pine_a.png",
                        "Assets/Art/M1/Props/tree_pine.png"
                    };
                case CartographyPropKind.TreePineB:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_pine_b.png",
                        PropsDir + "/tree_pine_b.png",
                        "Assets/Art/M1/Props/tree_pine.png"
                    };
                case CartographyPropKind.TreeWillowA:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_willow_a.png",
                        PropsDir + "/tree_willow_a.png",
                        "Assets/Art/M1/Props/tree_oak.png"
                    };
                case CartographyPropKind.TorchBrazier:
                    return new[]
                    {
                        PropsDir + "/torch_brazier.png",
                        "Assets/Art/M1/Props/torch_brazier.png",
                        "Assets/Art/M1/Props/torch.png"
                    };
                case CartographyPropKind.MarketCrates:
                    return new[]
                    {
                        PropsDir + "/market_crates.png",
                        PropsDir + "/prop_barrels_crates.png",
                        "Assets/Art/M1/Props/market_crates.png"
                    };
                case CartographyPropKind.Blacksmith:
                    return new[] { BuildingsDir + "/building_blacksmith.png" };
                case CartographyPropKind.Apothecary:
                    return new[] { BuildingsDir + "/building_apothecary.png" };
                case CartographyPropKind.Temple:
                    return new[] { BuildingsDir + "/building_temple.png" };
                case CartographyPropKind.Guildhall:
                    return new[] { BuildingsDir + "/building_guildhall.png" };
                case CartographyPropKind.CottageB:
                    return new[] { BuildingsDir + "/building_cottage_b.png" };
                case CartographyPropKind.CottageC:
                    return new[] { BuildingsDir + "/building_cottage_c.png" };
                case CartographyPropKind.Watchtower:
                    return new[] { BuildingsDir + "/building_watchtower.png" };
                case CartographyPropKind.MarketStallA:
                    return new[] { PropsDir + "/prop_market_stall_a.png" };
                case CartographyPropKind.MarketStallB:
                    return new[] { PropsDir + "/prop_market_stall_b.png" };
                case CartographyPropKind.StoneWell:
                    return new[] { PropsDir + "/prop_stone_well.png" };
                case CartographyPropKind.Signpost:
                    return new[] { PropsDir + "/prop_signpost.png" };
                case CartographyPropKind.WoodenWagon:
                    return new[] { PropsDir + "/prop_wooden_wagon.png" };
                case CartographyPropKind.BarrelsCrates:
                    return new[] { PropsDir + "/prop_barrels_crates.png" };
                case CartographyPropKind.CampTent:
                    return new[] { PropsDir + "/prop_camp_tent.png" };
                case CartographyPropKind.StoneRuins:
                    return new[] { PropsDir + "/prop_stone_ruins.png" };
                case CartographyPropKind.ShrineStatue:
                    return new[] { PropsDir + "/prop_shrine_statue.png" };
                case CartographyPropKind.Campfire4f:
                    return new[] { PropsDir + "/prop_campfire_4f.png" };
                case CartographyPropKind.DungeonChestClosed:
                    return new[] { PropsDir + "/dungeon_chest_closed.png" };
                case CartographyPropKind.DungeonChestOpen:
                    return new[] { PropsDir + "/dungeon_chest_open.png" };
                case CartographyPropKind.DungeonDoorClosed:
                    return new[] { PropsDir + "/dungeon_door_closed.png" };
                case CartographyPropKind.DungeonDoorOpen:
                    return new[] { PropsDir + "/dungeon_door_open.png" };
                case CartographyPropKind.DungeonPillar:
                    return new[] { PropsDir + "/dungeon_pillar.png" };
                case CartographyPropKind.DungeonStairsDown:
                    return new[] { PropsDir + "/dungeon_stairs_down.png" };
                case CartographyPropKind.DungeonStairsUp:
                    return new[] { PropsDir + "/dungeon_stairs_up.png" };
                case CartographyPropKind.DungeonWallSconce4f:
                    return new[] { PropsDir + "/dungeon_wall_sconce_4f.png" };
                case CartographyPropKind.DungeonSarcophagus:
                    return new[] { PropsDir + "/dungeon_sarcophagus.png" };
                case CartographyPropKind.DungeonSpikeTrap:
                    return new[] { PropsDir + "/dungeon_spike_trap.png" };
                case CartographyPropKind.DungeonIronGrate:
                    return new[] { PropsDir + "/dungeon_iron_grate.png" };
                case CartographyPropKind.GnarledDeadTree:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/gnarled_dead_tree.png",
                        PropsDir + "/gnarled_dead_tree.png"
                    };
                case CartographyPropKind.BoulderMossA:
                    return new[] { PropsDir + "/boulder_moss_a.png" };
                case CartographyPropKind.BoulderMossB:
                    return new[] { PropsDir + "/boulder_moss_b.png" };
                case CartographyPropKind.BoulderMossC:
                    return new[] { PropsDir + "/boulder_moss_c.png" };
                case CartographyPropKind.FallenHollowLog:
                    return new[] { PropsDir + "/fallen_hollow_log.png" };
                case CartographyPropKind.ThornBrambleThicket:
                    return new[] { PropsDir + "/thorn_bramble_thicket.png" };
                case CartographyPropKind.RuinedWallCorner:
                    return new[] { PropsDir + "/ruined_wall_corner.png" };
                case CartographyPropKind.BrokenArchway:
                    return new[] { PropsDir + "/broken_archway.png" };
                case CartographyPropKind.RubblePileA:
                    return new[] { PropsDir + "/rubble_pile_a.png" };
                case CartographyPropKind.RubblePileB:
                    return new[] { PropsDir + "/rubble_pile_b.png" };
                case CartographyPropKind.FallenStatue:
                    return new[] { PropsDir + "/fallen_statue.png" };
                case CartographyPropKind.RuneObelisk4f:
                    return new[] { PropsDir + "/rune_obelisk_4f.png" };
                case CartographyPropKind.BonePile:
                    return new[] { PropsDir + "/bone_pile.png" };
                case CartographyPropKind.IronCage:
                    return new[] { PropsDir + "/iron_cage.png" };
                case CartographyPropKind.DungeonFloorGrateRusted:
                    return new[] { PropsDir + "/dungeon_floor_grate_rusted.png" };
                case CartographyPropKind.PalisadeStakes:
                    return new[] { PropsDir + "/palisade_stakes.png" };
                case CartographyPropKind.CartWreck:
                    return new[] { PropsDir + "/cart_wreck.png" };
                case CartographyPropKind.HayBale:
                    return new[] { PropsDir + "/hay_bale.png" };
                case CartographyPropKind.OilKegCluster:
                    return new[] { PropsDir + "/oil_keg_cluster.png" };
                case CartographyPropKind.TownNoticeBoard:
                    return new[] { PropsDir + "/town_notice_board.png" };
                case CartographyPropKind.TownLanternPost4f:
                    return new[] { PropsDir + "/town_lantern_post_4f.png" };
                case CartographyPropKind.WoodenPlankBridge:
                    return new[] { PropsDir + "/wooden_plank_bridge.png" };
                case CartographyPropKind.RiverReeds4f:
                    return new[] { PropsDir + "/river_reeds_4f.png" };
                case CartographyPropKind.DriftwoodSnag:
                    return new[] { PropsDir + "/driftwood_snag.png" };
                case CartographyPropKind.HalfSunkRowboat:
                    return new[] { PropsDir + "/half_sunk_rowboat.png" };
                case CartographyPropKind.DecalMossSkirt:
                    return new[] { PropsDir + "/decal_moss_skirt.png" };
                case CartographyPropKind.DecalPebbles:
                    return new[] { PropsDir + "/decal_pebbles.png" };
                case CartographyPropKind.DecalFallenLeaves:
                    return new[] { PropsDir + "/decal_fallen_leaves.png" };
                case CartographyPropKind.DecalCrackedStone:
                    return new[] { PropsDir + "/decal_cracked_stone.png" };
                case CartographyPropKind.DecalShoreFoam:
                    return new[] { PropsDir + "/decal_shore_foam.png" };
                default:
                    return Array.Empty<string>();
            }
        }

        private static string GetPropKey(CartographyPropKind kind)
        {
            return kind.ToString().ToLowerInvariant();
        }

        private static Texture2D FindFirstTextureInCartography(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
                if (t != null)
                {
                    return t;
                }
            }
            return null;
        }

        private static Color32[] TryLoadPngPixelsResampled(string assetPath, int targetW, int targetH, bool linear)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            Texture2D temp = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            if (!ImageConversion.LoadImage(temp, bytes, markNonReadable: false))
            {
                UnityEngine.Object.DestroyImmediate(temp);
                return null;
            }

            Color32[] src = temp.GetPixels32();
            int srcW = temp.width;
            int srcH = temp.height;
            UnityEngine.Object.DestroyImmediate(temp);

            if (srcW == targetW && srcH == targetH)
            {
                return src;
            }

            Color32[] dst = new Color32[targetW * targetH];
            for (int y = 0; y < targetH; y++)
            {
                int sy = Mathf.Clamp((y * srcH) / targetH, 0, srcH - 1);
                for (int x = 0; x < targetW; x++)
                {
                    int sx = Mathf.Clamp((x * srcW) / targetW, 0, srcW - 1);
                    dst[y * targetW + x] = src[sy * srcW + sx];
                }
            }
            return dst;
        }

        private static Color32[] GenerateProceduralTerrainSlice(int slice, int size)
        {
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int h = CartographyWorldData.DeterministicHash(x, y, slice * 71 + 17);
                    float n = (h & 0xFF) / 255f;
                    float n2 = ((h >> 8) & 0xFF) / 255f;
                    // Coarse 4x4 block noise for organic pixel-art clusters
                    int bx = x >> 2;
                    int by = y >> 2;
                    float cluster = (CartographyWorldData.DeterministicHash(bx, by, slice * 37 + 5) & 0xFF) / 255f;
                    float maskF = Mathf.Clamp01(cluster * 0.55f + n * 0.30f + n2 * 0.15f);
                    byte maskA = (byte)Mathf.RoundToInt(maskF * 255f);

                    Color col;
                    switch (slice)
                    {
                        case 0: // meadow_grass
                            col = Color.Lerp(new Color(0.24f, 0.44f, 0.16f), new Color(0.36f, 0.58f, 0.22f), maskF);
                            break;
                        case 1: // moss_patch
                            col = Color.Lerp(new Color(0.18f, 0.36f, 0.17f), new Color(0.30f, 0.50f, 0.24f), maskF);
                            break;
                        case 2: // dirt_road
                            col = Color.Lerp(new Color(0.42f, 0.31f, 0.20f), new Color(0.56f, 0.43f, 0.28f), maskF);
                            break;
                        case 3: // cobble_plaza (crisp staggered stone grid)
                            int row = y / 16;
                            int cx = (x + (row % 2) * 8) % 16;
                            int cy = y % 16;
                            bool mortar = (cx == 0 || cy == 0);
                            float stoneHash = (CartographyWorldData.DeterministicHash((x + (row % 2) * 8) / 16, row, 99) & 0xFF) / 255f;
                            col = mortar
                                ? new Color(0.26f, 0.25f, 0.24f)
                                : Color.Lerp(new Color(0.46f, 0.47f, 0.48f), new Color(0.62f, 0.61f, 0.57f), stoneHash * 0.7f + n * 0.3f);
                            maskA = mortar ? (byte)64 : (byte)Mathf.RoundToInt(Mathf.Lerp(150f, 255f, stoneHash));
                            break;
                        case 4: // river_mud
                            col = Color.Lerp(new Color(0.24f, 0.22f, 0.17f), new Color(0.35f, 0.30f, 0.22f), maskF);
                            break;
                        default: // cliff_rock
                            int cr = y / 12;
                            bool crack = (y % 12 == 0) || ((x + cr * 5) % 24 == 0);
                            col = crack
                                ? new Color(0.20f, 0.21f, 0.24f)
                                : Color.Lerp(new Color(0.34f, 0.35f, 0.38f), new Color(0.50f, 0.49f, 0.47f), cluster * 0.65f + n * 0.35f);
                            break;
                    }

                    Color32 c32 = col;
                    c32.a = maskA;
                    pixels[y * size + x] = c32;
                }
            }

            return pixels;
        }

        private static Color32[] GenerateProceduralNormalSlice(int slice, int size)
        {
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int h = CartographyWorldData.DeterministicHash(x, y, slice * 97 + 11);
                    float dx = (((h & 0xFF) / 255f) - 0.5f) * 0.25f;
                    float dy = ((((h >> 8) & 0xFF) / 255f) - 0.5f) * 0.25f;
                    if (slice == 3 && ((x % 16 == 0) || (y % 16 == 0)))
                    {
                        dx *= 1.8f;
                        dy *= 1.8f;
                    }
                    Vector3 n = new Vector3(dx, dy, 1f).normalized;
                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            }
            return pixels;
        }

        private static Texture2D EnsureFallbackFoliageAtlasAsset()
        {
            const string path = FoliageDir + "/CartographyFallbackFoliageAtlas.asset";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            const int w = 512;
            const int h = 256;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = "CartographyFallbackFoliageAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[w * h];
            for (int variant = 0; variant < 16; variant++)
            {
                int bank = variant / 8;
                int rowFromTop = variant - bank * 8;
                bool isFlower = variant >= 4 && variant <= 7;
                bool isReed = variant >= 8 && variant <= 11;
                bool isFern = variant >= 12;

                Color baseCol = isFern
                    ? new Color(0.18f, 0.42f, 0.26f)
                    : (isReed ? new Color(0.24f, 0.48f, 0.22f) : new Color(0.28f, 0.56f, 0.20f));
                Color tipCol = isFern
                    ? new Color(0.32f, 0.62f, 0.42f)
                    : (isReed ? new Color(0.56f, 0.66f, 0.30f) : new Color(0.54f, 0.78f, 0.30f));
                Color flowerCol = (variant % 3 == 0)
                    ? new Color(0.98f, 0.86f, 0.28f)
                    : ((variant % 3 == 1) ? new Color(0.96f, 0.94f, 0.90f) : new Color(0.88f, 0.44f, 0.62f));

                for (int frame = 0; frame < 8; frame++)
                {
                    int ox = (bank * 8 + frame) * 32;
                    int oy = (7 - rowFromTop) * 32; // bottom-up Texture2D Y
                    float swayShift = Mathf.Sin((frame / 8f) * Mathf.PI * 2f) * 3.0f;

                    for (int py = 0; py < 32; py++)
                    {
                        float t = py / 31f;
                        for (int px = 0; px < 32; px++)
                        {
                            // 5 pixel grass blades rising from bottom center
                            bool hit = false;
                            bool isBlossom = false;
                            for (int b = 0; b < 5; b++)
                            {
                                float rootX = 6f + b * 5f + (variant % 3 - 1);
                                float lean = (b - 2f) * 1.8f * t + swayShift * (t * t);
                                float bladeH = (isReed ? 28f : (isFern ? 22f : 24f)) - Mathf.Abs(b - 2f) * 2.5f;
                                if (py <= bladeH)
                                {
                                    float halfW = (isFern ? 2.4f : 1.5f) * (1f - t * 0.65f);
                                    if (Mathf.Abs(px - (rootX + lean)) <= halfW)
                                    {
                                        hit = true;
                                        if (isFlower && (b == 1 || b == 3) && py >= bladeH - 4f)
                                        {
                                            isBlossom = true;
                                        }
                                    }
                                }
                            }

                            if (hit)
                            {
                                Color c = isBlossom ? flowerCol : Color.Lerp(baseCol, tipCol, t);
                                Color32 c32 = c;
                                c32.a = 255;
                                pixels[(oy + py) * w + (ox + px)] = c32;
                            }
                            else
                            {
                                pixels[(oy + py) * w + (ox + px)] = new Color32(0, 0, 0, 0);
                            }
                        }
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        private static Texture2D EnsureFallbackPropTextureAsset(CartographyPropKind kind)
        {
            string key = GetPropKey(kind);
            string path = CartographyArtRoot + "/Materials/Fallback_" + key + ".asset";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = "Fallback_" + key,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color32 px = new Color32(0, 0, 0, 0);
                    if (kind == CartographyPropKind.StoneBridge)
                    {
                        bool arch = y >= 10 && y <= 50 && x >= 4 && x <= 59;
                        bool rail = y >= 42 && y <= 50 && x >= 4 && x <= 59;
                        if (arch)
                        {
                            px = rail ? new Color32(158, 152, 144, 255) : new Color32(124, 120, 114, 255);
                        }
                    }
                    else if (kind == CartographyPropKind.BywaterInn)
                    {
                        bool wall = x >= 6 && x <= 57 && y >= 2 && y <= 36;
                        bool roof = y > 36 && y <= 58 && x >= (2 + (y - 36) / 2) && x <= (61 - (y - 36) / 2);
                        bool window = (y >= 16 && y <= 24) && ((x >= 14 && x <= 20) || (x >= 43 && x <= 49));
                        bool door = y >= 2 && y <= 18 && x >= 27 && x <= 36;
                        if (roof) px = new Color32(158, 66, 46, 255);
                        else if (door) px = new Color32(92, 56, 30, 255);
                        else if (window) px = new Color32(252, 204, 96, 255);
                        else if (wall) px = new Color32(214, 196, 162, 255);
                    }
                    else
                    {
                        // Willow / generic foliage silhouette fallback
                        float dx = (x - 32f) / 24f;
                        float dy = (y - 38f) / 22f;
                        bool trunk = y <= 26 && Mathf.Abs(x - 32) <= 4;
                        if (dx * dx + dy * dy <= 1f)
                        {
                            px = new Color32(72, 134, 68, 255);
                        }
                        else if (trunk)
                        {
                            px = new Color32(96, 62, 36, 255);
                        }
                    }

                    pixels[y * size + x] = px;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        private static string ToAssetRelativePath(string fullPath)
        {
            string dataRoot = Path.GetFullPath(Application.dataPath);
            string normalized = Path.GetFullPath(fullPath);
            return "Assets" + normalized.Substring(dataRoot.Length).Replace('\\', '/');
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
