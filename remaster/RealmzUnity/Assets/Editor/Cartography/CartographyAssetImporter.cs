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
        public const string MaterialsDir = CartographyArtRoot + "/Materials";
        public const string AlbedoArrayAssetPath = TilesDir + "/CartographyTerrainAlbedoArray.asset";
        public const string NormalArrayAssetPath = TilesDir + "/CartographyTerrainNormalArray.asset";
        public const string TerrainMaterialPath = MaterialsDir + "/Cartography_Terrain.mat";
        public const string FoliageMaterialPath = MaterialsDir + "/Cartography_FoliageAtlas.mat";
        public const string WaterMaterialPath = MaterialsDir + "/Cartography_ShallowWater.mat";

        public const int TileLayerSize = 128;

        private static readonly string[] TerrainLayerNames =
        {
            "meadow_grass",
            "moss_patch",
            "dirt_road",
            "cobble_plaza",
            "river_mud",
            "cliff_rock"
        };

        [MenuItem("Realmz/Cartography/Import Assets & Build Texture Arrays")]
        public static void ImportAll()
        {
            EnsureFolder(CartographyArtRoot);
            EnsureFolder(TilesDir);
            EnsureFolder(FoliageDir);
            EnsureFolder(MaterialsDir);

            AssetDatabase.Refresh();
            ConfigureAllPngsOnDisk();
            AssetDatabase.Refresh();

            BuildTerrainTextureArrays(out Texture2DArray albedoArray, out Texture2DArray normalArray);
            CreateOrUpdateTerrainMaterial(albedoArray, normalArray);
            CreateOrUpdateFoliageMaterial();
            CreateOrUpdateWaterMaterial();
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
            bool isFoliageAtlas = assetPath.IndexOf("/foliage/", StringComparison.OrdinalIgnoreCase) >= 0;

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
            else
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
                if (settings.spriteAlignment != (int)SpriteAlignment.BottomCenter ||
                    (settings.spritePivot - new Vector2(0.5f, 0f)).sqrMagnitude > 0.0001f)
                {
                    settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                    settings.spritePivot = new Vector2(0.5f, 0f);
                    importer.SetTextureSettings(settings);
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

        public static void BuildTerrainTextureArrays(out Texture2DArray albedoArray, out Texture2DArray normalArray)
        {
            int sliceCount = TerrainLayerNames.Length;

            Texture2DArray existingAlbedo = AssetDatabase.LoadAssetAtPath<Texture2DArray>(AlbedoArrayAssetPath);
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
                    name = "CartographyTerrainAlbedoArray",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };
            }
            else
            {
                albedoArray = existingAlbedo;
            }

            Texture2DArray existingNormal = AssetDatabase.LoadAssetAtPath<Texture2DArray>(NormalArrayAssetPath);
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
                    name = "CartographyTerrainNormalArray",
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
                string layerName = TerrainLayerNames[slice];
                string albPath = TilesDir + "/" + layerName + ".png";
                string nrmPath = TilesDir + "/" + layerName + "_n.png";

                Color32[] albPixels = TryLoadPngPixelsResampled(albPath, TileLayerSize, TileLayerSize, linear: false)
                    ?? GenerateProceduralTerrainSlice(slice, TileLayerSize);
                Color32[] nrmPixels = TryLoadPngPixelsResampled(nrmPath, TileLayerSize, TileLayerSize, linear: true)
                    ?? GenerateProceduralNormalSlice(slice, TileLayerSize);

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
                AssetDatabase.CreateAsset(albedoArray, AlbedoArrayAssetPath);
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
                AssetDatabase.CreateAsset(normalArray, NormalArrayAssetPath);
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
            Shader shader = Shader.Find("Realmz/CartographyTerrain") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, TerrainMaterialPath);
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
            if (mat.HasProperty("_GroundValueScale")) mat.SetFloat("_GroundValueScale", 0.76f);
            if (mat.HasProperty("_RoadValueBoost")) mat.SetFloat("_RoadValueBoost", 0.86f);
            if (mat.HasProperty("_HorizonHazeColor")) mat.SetColor("_HorizonHazeColor", new Color(0.91f, 0.83f, 0.70f, 1f));
            if (mat.HasProperty("_HorizonHazeStrength")) mat.SetFloat("_HorizonHazeStrength", 0.09f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Material CreateOrUpdateFoliageMaterial()
        {
            Shader shader = Shader.Find("Realmz/CartographyBillboard") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(FoliageMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, FoliageMaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            Texture2D atlas = FindFirstTextureInCartography(new[]
            {
                FoliageDir + "/bywater_foliage_atlas.png",
                FoliageDir + "/foliage_atlas.png",
                FoliageDir + "/cartography_foliage_atlas.png",
                CartographyArtRoot + "/bywater_foliage_atlas.png",
                CartographyArtRoot + "/foliage_atlas.png"
            });

            if (atlas == null)
            {
                atlas = EnsureFallbackFoliageAtlasAsset();
            }

            Texture2D atlasNormal = FindFirstTextureInCartography(new[]
            {
                FoliageDir + "/bywater_foliage_atlas_n.png",
                FoliageDir + "/foliage_atlas_n.png",
                CartographyArtRoot + "/bywater_foliage_atlas_n.png"
            });

            mat.enableInstancing = true;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", atlas);
            if (mat.HasProperty("_BumpMap") && atlasNormal != null) mat.SetTexture("_BumpMap", atlasNormal);
            if (mat.HasProperty("_IsTwoBankAtlas")) mat.SetFloat("_IsTwoBankAtlas", 1f);
            if (mat.HasProperty("_BillboardMode")) mat.SetFloat("_BillboardMode", 1f);
            if (mat.HasProperty("_CameraPitchTiltDeg")) mat.SetFloat("_CameraPitchTiltDeg", 22f);
            if (mat.HasProperty("_NormalUpBias")) mat.SetFloat("_NormalUpBias", 0.72f);
            if (mat.HasProperty("_FrameCount")) mat.SetFloat("_FrameCount", 8f);
            if (mat.HasProperty("_AnimFps")) mat.SetFloat("_AnimFps", 7f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.42f);
            if (mat.HasProperty("_RootTint")) mat.SetColor("_RootTint", new Color(0.44f, 0.52f, 0.42f, 1f));
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Material CreateOrUpdateWaterMaterial()
        {
            Shader shader = Shader.Find("Realmz/CartographyWater")
                ?? Shader.Find("Universal Render Pipeline/Lit");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, WaterMaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            Texture2D waterAlbedo = FindFirstTextureInCartography(new[]
            {
                TilesDir + "/shallow_water.png",
                "Assets/Art/M1/Tiles/shallow_water.png"
            });
            Texture2D waterNormal = FindFirstTextureInCartography(new[]
            {
                TilesDir + "/shallow_water_n.png",
                "Assets/Art/M1/Tiles/shallow_water_n.png"
            });

            if (mat.HasProperty("_BaseMap") && waterAlbedo != null) mat.SetTexture("_BaseMap", waterAlbedo);
            if (mat.HasProperty("_BumpMap") && waterNormal != null) mat.SetTexture("_BumpMap", waterNormal);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.16f, 0.38f, 0.46f, 1f));
            if (mat.HasProperty("_ShimmerColor")) mat.SetColor("_ShimmerColor", new Color(0.94f, 0.99f, 1.0f, 1f));
            if (mat.HasProperty("_ShimmerStrength")) mat.SetFloat("_ShimmerStrength", 0.34f);
            if (mat.HasProperty("_WaveScale")) mat.SetFloat("_WaveScale", 2.0f);

            EditorUtility.SetDirty(mat);
            return mat;
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

            bool is4FrameSheet = tex != null && tex.height > 0 && tex.width >= tex.height * 3;
            bool isBridge = kind == CartographyPropKind.StoneBridge;
            bool isStoneFenceV = kind == CartographyPropKind.StoneFenceV;
            bool isTree = kind == CartographyPropKind.TreeOakA || kind == CartographyPropKind.TreeOakB ||
                          kind == CartographyPropKind.TreePineA || kind == CartographyPropKind.TreePineB ||
                          kind == CartographyPropKind.TreeWillowA;

            mat.enableInstancing = false;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
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
                float pitchTiltDeg = isStoneFenceV ? 30f : (isBridge ? 4f : (isTree ? 14f : 12f));
                mat.SetFloat("_CameraPitchTiltDeg", pitchTiltDeg);
            }
            if (mat.HasProperty("_WindTipSway")) mat.SetFloat("_WindTipSway", isTree ? 0.028f : 0f);
            if (mat.HasProperty("_TrampleStrength")) mat.SetFloat("_TrampleStrength", 0f);
            if (mat.HasProperty("_NormalUpBias")) mat.SetFloat("_NormalUpBias", isBridge ? 0.85f : 0.65f);
            if (mat.HasProperty("_RootTint")) mat.SetColor("_RootTint", new Color(0.68f, 0.70f, 0.68f, 1f));
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.45f);
            if (mat.HasProperty("_ReceiveRootShadowOnly")) mat.SetFloat("_ReceiveRootShadowOnly", 1f);

            Color emissionColor = Color.black;
            float emissionWarmMask = 1f;
            float emissionMinV = 0f;
            if (kind == CartographyPropKind.TorchBrazier)
            {
                emissionColor = new Color(2.4f, 1.15f, 0.30f, 1f);
                emissionWarmMask = 1f;
                emissionMinV = 0.44f;
            }
            else if (kind == CartographyPropKind.BywaterInn)
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
                        CartographyArtRoot + "/buildings/bywater_inn.png",
                        CartographyArtRoot + "/props/bywater_inn.png",
                        "Assets/Art/M1/Props/tavern.png",
                        "Assets/Art/M1/Props/cottage_medieval.png"
                    };
                case CartographyPropKind.StoneBridge:
                    return new[]
                    {
                        CartographyArtRoot + "/structures/stone_bridge.png",
                        CartographyArtRoot + "/props/stone_bridge.png",
                        CartographyArtRoot + "/buildings/stone_bridge.png"
                    };
                case CartographyPropKind.StoneFenceH:
                    return new[]
                    {
                        CartographyArtRoot + "/structures/stone_fence_h.png",
                        CartographyArtRoot + "/props/stone_fence_h.png",
                        CartographyArtRoot + "/props/stone_fence.png",
                        "Assets/Art/M1/Props/stone_fence.png"
                    };
                case CartographyPropKind.StoneFenceV:
                    return new[]
                    {
                        CartographyArtRoot + "/structures/stone_fence_v.png",
                        CartographyArtRoot + "/props/stone_fence_v.png",
                        CartographyArtRoot + "/props/stone_fence.png",
                        "Assets/Art/M1/Props/stone_fence.png"
                    };
                case CartographyPropKind.Knight:
                    return new[]
                    {
                        CartographyArtRoot + "/units/knight.png",
                        CartographyArtRoot + "/props/knight.png",
                        "Assets/Art/M1/Heroes/knight.png"
                    };
                case CartographyPropKind.GiantFrog:
                    return new[]
                    {
                        CartographyArtRoot + "/units/giant_frog.png",
                        CartographyArtRoot + "/props/giant_frog.png",
                        "Assets/Art/M1/Monsters/giant_frog.png"
                    };
                case CartographyPropKind.TreeOakA:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_oak_a.png",
                        CartographyArtRoot + "/props/tree_oak_a.png",
                        "Assets/Art/M1/Props/tree_oak.png"
                    };
                case CartographyPropKind.TreeOakB:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_oak_b.png",
                        CartographyArtRoot + "/props/tree_oak_b.png",
                        "Assets/Art/M1/Props/tree_oak.png"
                    };
                case CartographyPropKind.TreePineA:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_pine_a.png",
                        CartographyArtRoot + "/props/tree_pine_a.png",
                        "Assets/Art/M1/Props/tree_pine.png"
                    };
                case CartographyPropKind.TreePineB:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_pine_b.png",
                        CartographyArtRoot + "/props/tree_pine_b.png",
                        "Assets/Art/M1/Props/tree_pine.png"
                    };
                case CartographyPropKind.TreeWillowA:
                    return new[]
                    {
                        CartographyArtRoot + "/trees/tree_willow_a.png",
                        CartographyArtRoot + "/props/tree_willow_a.png",
                        "Assets/Art/M1/Props/tree_oak.png"
                    };
                case CartographyPropKind.TorchBrazier:
                    return new[]
                    {
                        CartographyArtRoot + "/props/torch_brazier.png",
                        "Assets/Art/M1/Props/torch_brazier.png",
                        "Assets/Art/M1/Props/torch.png"
                    };
                case CartographyPropKind.MarketCrates:
                    return new[]
                    {
                        CartographyArtRoot + "/props/market_crates.png",
                        "Assets/Art/M1/Props/market_crates.png"
                    };
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
