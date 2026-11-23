using System.IO;
using Realmz.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ProjectBootstrap
{
    /// <summary>
    /// Configures crisp Point-filtered pixel/normal texture imports and builds the
    /// M0 Songs-of-Conquest style HD-2D Smoke Scene (URP 3D + ACES + Bloom + Torch PointLight).
    /// </summary>
    public static class M0SmokeSceneBuilder
    {
        private const string KnightAlbedoPath = "Assets/Art/Sprites/knight_hero_64x64.png";
        private const string KnightNormalPath = "Assets/Art/Sprites/knight_hero_64x64_n.png";
        private const string FrogAlbedoPath = "Assets/Art/Sprites/monster_giant_frog_392.png";
        private const string KevlarAlbedoPath = "Assets/Art/Sprites/hero_kevlar_tactical_9018.png";
        private const string GrassTilePath = "Assets/Art/Tiles/terrain_bywater_tile_grass.png";

        private const string MaterialsDir = "Assets/Art/Materials";
        private const string SettingsDir = "Assets/Settings";
        private const string ScenesDir = "Assets/Scenes";
        private const string SmokeScenePath = "Assets/Scenes/M0_HD2D_SmokeScene.unity";
        private const string VolumeProfilePath = "Assets/Settings/M0_Smoke_VolumeProfile.asset";

        [MenuItem("Realmz/M0/Build HD-2D Smoke Scene")]
        public static void BuildSmokeScene()
        {
            EnsureFolder(MaterialsDir);
            EnsureFolder(SettingsDir);
            EnsureFolder(ScenesDir);

            ConfigurePixelTextures();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Ambient cool-indigo shadow fill (Songs of Conquest dark-fantasy base)
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.094f, 0.118f, 0.220f, 1f); // #181E38
            RenderSettings.fog = false;

            // 2. Tilted 3D Perspective Camera with URP Post-Processing enabled
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.067f, 0.125f, 1f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            cam.allowHDR = true;
            cam.allowMSAA = false;
            camGo.transform.position = new Vector3(0f, 3.6f, -5.0f);
            camGo.transform.LookAt(new Vector3(0f, 0.75f, 0f), Vector3.up);

            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.volumeLayerMask = ~0;
            camData.antialiasing = AntialiasingMode.None;

            // 3. Warm Directional Sun with grounded 3D shadows
            GameObject sunGo = new GameObject("Directional Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.949f, 0.863f, 1f); // #FFF2DC
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sunGo.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            // 4. 3D Grass Floor Grid (City of Bywater extracted grass tile)
            Texture2D grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTilePath);
            Material floorMat = CreateOrUpdateUrpLitMaterial(
                MaterialsDir + "/M0_BywaterGrassFloor.mat",
                grassTex,
                null,
                alphaClip: false,
                smoothness: 0.08f,
                tiling: new Vector2(6f, 6f),
                doubleSided: false
            );

            GameObject floorGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floorGo.name = "Bywater_GrassFloor_6x6";
            floorGo.transform.position = Vector3.zero;
            floorGo.transform.localScale = new Vector3(0.6f, 1f, 0.6f); // 6m x 6m
            MeshRenderer floorRenderer = floorGo.GetComponent<MeshRenderer>();
            floorRenderer.sharedMaterial = floorMat;
            floorRenderer.shadowCastingMode = ShadowCastingMode.Off;
            floorRenderer.receiveShadows = true;

            // 5. Foot-Anchored Billboard Knight Hero (64x64 Albedo + OpenGL Normal Map)
            Texture2D knightAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(KnightAlbedoPath);
            Texture2D knightNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(KnightNormalPath);
            Material knightMat = CreateOrUpdateUrpLitMaterial(
                MaterialsDir + "/M0_KnightHero_HD2D.mat",
                knightAlbedo,
                knightNormal,
                alphaClip: true,
                smoothness: 0.20f,
                tiling: Vector2.one,
                doubleSided: true
            );
            CreateBillboardUnit("KnightHero_HD2D", knightMat, new Vector3(-0.35f, 0f, 0f), 1.5f, 1.5f);

            // 6. Foot-Anchored Bywater Giant Frog Monster (extracted cicn 392)
            Texture2D frogAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(FrogAlbedoPath);
            if (frogAlbedo != null)
            {
                Material frogMat = CreateOrUpdateUrpLitMaterial(
                    MaterialsDir + "/M0_GiantFrog_Bywater.mat",
                    frogAlbedo,
                    null,
                    alphaClip: true,
                    smoothness: 0.12f,
                    tiling: Vector2.one,
                    doubleSided: true
                );
                CreateBillboardUnit("Bywater_GiantFrog", frogMat, new Vector3(1.45f, 0f, 0.45f), 1.5f, 0.75f);
            }

            // 7. Warm Torch Point Light + Brazier Post (lights knight normal map dynamically)
            GameObject torchRoot = new GameObject("Torch_Brazier");
            torchRoot.transform.position = new Vector3(-1.35f, 0f, -0.55f);

            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "TorchPost";
            post.transform.SetParent(torchRoot.transform, false);
            post.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            post.transform.localScale = new Vector3(0.12f, 0.55f, 0.12f);
            Material postMat = CreateOrUpdateUrpLitMaterial(
                MaterialsDir + "/M0_TorchPostIron.mat",
                null,
                null,
                alphaClip: false,
                smoothness: 0.35f,
                tiling: Vector2.one,
                doubleSided: false
            );
            postMat.SetColor("_BaseColor", new Color(0.22f, 0.17f, 0.14f, 1f));
            EditorUtility.SetDirty(postMat);
            post.GetComponent<MeshRenderer>().sharedMaterial = postMat;

            GameObject ember = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ember.name = "TorchFlameEmber";
            ember.transform.SetParent(torchRoot.transform, false);
            ember.transform.localPosition = new Vector3(0f, 1.18f, 0f);
            ember.transform.localScale = new Vector3(0.18f, 0.22f, 0.18f);
            Material emberMat = CreateEmissiveMaterial(
                MaterialsDir + "/M0_TorchEmber.mat",
                new Color(1.0f, 0.42f, 0.08f, 1f),
                4.2f
            );
            ember.GetComponent<MeshRenderer>().sharedMaterial = emberMat;
            ember.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            GameObject torchLightGo = new GameObject("TorchPointLight");
            torchLightGo.transform.SetParent(torchRoot.transform, false);
            torchLightGo.transform.localPosition = new Vector3(0f, 1.30f, 0f);
            Light torchLight = torchLightGo.AddComponent<Light>();
            torchLight.type = LightType.Point;
            torchLight.color = new Color(1.0f, 0.620f, 0.267f, 1f); // #FF9E44
            torchLight.intensity = 6.5f;
            torchLight.range = 7.5f;
            torchLight.shadows = LightShadows.Soft;
            torchLightGo.AddComponent<TorchFlicker>();

            // 8. Global URP Volume: ACES Tonemapping + Selective HDR Bloom
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            if (!profile.TryGet<Tonemapping>(out var tonemapping))
            {
                tonemapping = profile.Add<Tonemapping>(true);
            }
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            if (!profile.TryGet<Bloom>(out var bloom))
            {
                bloom = profile.Add<Bloom>(true);
            }
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.05f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.42f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.68f;
            EditorUtility.SetDirty(profile);

            GameObject volGo = new GameObject("Global Volume (ACES + Bloom)");
            Volume vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = profile;

            // Align billboards immediately for edit-mode preview and screenshots
            AlignBillboardsToCamera(cam);

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, SmokeScenePath);
            Debug.Log("[M0SmokeSceneBuilder] Saved HD-2D Smoke Scene to " + SmokeScenePath);
        }

        [MenuItem("Realmz/M0/Capture Smoke Scene Preview")]
        public static void CapturePreviewScreenshot()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[M0SmokeSceneBuilder] Main Camera not found.");
                return;
            }

            AlignBillboardsToCamera(cam);

            const int width = 1280;
            const int height = 720;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "M0_SmokeScene_Preview.png"));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[M0SmokeSceneBuilder] Captured preview screenshot: " + outPath);
        }

        private static void AlignBillboardsToCamera(Camera cam)
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return;
            }
            Quaternion rot = Quaternion.LookRotation(forward.normalized, Vector3.up);
            PixelBillboard[] billboards = Object.FindObjectsByType<PixelBillboard>(FindObjectsSortMode.None);
            foreach (PixelBillboard bb in billboards)
            {
                bb.transform.rotation = rot;
            }
        }

        public static void ConfigurePixelTextures()
        {
            ConfigureSpriteTexture(KnightAlbedoPath, 64f);
            ConfigureSpriteTexture(FrogAlbedoPath, 32f);
            ConfigureSpriteTexture(KevlarAlbedoPath, 32f);
            ConfigureNormalTexture(KnightNormalPath);
            ConfigureTileTexture(GrassTilePath);
            AssetDatabase.Refresh();
        }

        private static void ConfigureSpriteTexture(string assetPath, float pixelsPerUnit)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
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

        private static void ConfigureNormalTexture(string assetPath)
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
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        private static void ConfigureTileTexture(string assetPath)
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

        private static GameObject CreateBillboardUnit(string name, Material mat, Vector3 footPos, float width, float height)
        {
            GameObject root = new GameObject(name);
            root.transform.position = footPos;
            root.AddComponent<PixelBillboard>();

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SpriteQuad";
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            quad.transform.localScale = new Vector3(width, height, 1f);

            Collider col = quad.GetComponent<Collider>();
            if (col != null)
            {
                Object.DestroyImmediate(col);
            }

            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            return root;
        }

        private static Material CreateOrUpdateUrpLitMaterial(
            string matPath,
            Texture2D albedo,
            Texture2D normalMap,
            bool alphaClip,
            float smoothness,
            Vector2 tiling,
            bool doubleSided)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = shader;
            }

            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTextureScale("_BaseMap", tiling);
            }

            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);

            if (normalMap != null)
            {
                mat.SetTexture("_BumpMap", normalMap);
                mat.SetFloat("_BumpScale", 1.0f);
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                mat.DisableKeyword("_NORMALMAP");
            }

            if (alphaClip)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                mat.SetFloat("_AlphaClip", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)RenderQueue.Geometry;
            }

            if (doubleSided)
            {
                mat.SetFloat("_Cull", 0f);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateEmissiveMaterial(string matPath, Color color, float intensity)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetColor("_BaseColor", color);
            mat.SetColor("_EmissionColor", color * intensity);
            mat.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(mat);
            return mat;
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
