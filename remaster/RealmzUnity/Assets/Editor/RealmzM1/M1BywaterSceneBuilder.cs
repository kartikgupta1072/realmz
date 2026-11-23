using Realmz.Data;
using Realmz.Game;
using Realmz.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Realmz.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/M1_Bywater_Slice.unity with URP 3D HD-2D lighting, ACES + Bloom,
    /// InputSystem UI EventSystem, and wired BywaterGameController + BywaterAssetCatalog.
    /// Registers M1_Bywater_Slice.unity at index 0 in EditorBuildSettings without altering M0_HD2D_SmokeScene.unity.
    /// </summary>
    public static class M1BywaterSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/M1_Bywater_Slice.unity";
        private const string VolumeProfilePath = "Assets/Settings/M1_Bywater_VolumeProfile.asset";

        [MenuItem("Realmz/M1/Build City of Bywater Scene")]
        public static void BuildBywaterScene()
        {
            EnsureFolder("Assets/Settings");
            EnsureFolder("Assets/Scenes");

            BywaterAssetCatalog catalog = M1AssetImporter.ImportAndBuildCatalog();
            if (catalog == null)
            {
                Debug.LogError("[M1BywaterSceneBuilder] Failed to import BywaterAssetCatalog.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Songs-of-Conquest cool-indigo shadow fill
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.094f, 0.125f, 0.243f, 1f);
            RenderSettings.fog = false;

            // 2. Perspective HD-2D Camera Rig with URP Post-Processing & HDR
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.043f, 0.055f, 0.106f, 1f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 140f;
            cam.allowHDR = true;
            cam.allowMSAA = false;

            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.volumeLayerMask = ~0;
            camData.antialiasing = AntialiasingMode.None;

            BywaterCameraRig cameraRig = camGo.AddComponent<BywaterCameraRig>();
            cameraRig.SetFollowTarget(BywaterCameraRig.GridToWorld(new RealmzCore.Data.GridCoord(2, 1)), immediate: true);

            // 3. Warm Directional Sun with grounded 3D soft shadows
            GameObject sunGo = new GameObject("Directional Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.941f, 0.839f, 1f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            sunGo.transform.rotation = Quaternion.Euler(46f, -34f, 0f);

            // 4. Global URP Volume: ACES Tonemapping + Bloom + subtle Vignette
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            if (profile.components != null)
            {
                profile.components.RemoveAll(c => c == null);
            }

            if (!profile.TryGet<Tonemapping>(out var tonemapping))
            {
                tonemapping = profile.Add<Tonemapping>(true);
            }
            if (!AssetDatabase.Contains(tonemapping))
            {
                AssetDatabase.AddObjectToAsset(tonemapping, profile);
            }
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            if (!profile.TryGet<Bloom>(out var bloom))
            {
                bloom = profile.Add<Bloom>(true);
            }
            if (!AssetDatabase.Contains(bloom))
            {
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.02f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.45f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.68f;

            if (!profile.TryGet<Vignette>(out var vignette))
            {
                vignette = profile.Add<Vignette>(true);
            }
            if (!AssetDatabase.Contains(vignette))
            {
                AssetDatabase.AddObjectToAsset(vignette, profile);
            }
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.22f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.45f;

            EditorUtility.SetDirty(profile);

            GameObject volGo = new GameObject("Global Volume (ACES + Bloom)");
            Volume vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = profile;

            // 5. World, Overlays, HUD, InputSystem EventSystem, and Game Controller
            GameObject worldGo = new GameObject("BywaterWorld");
            BywaterWorldRenderer worldRenderer = worldGo.AddComponent<BywaterWorldRenderer>();

            GameObject overlaysGo = new GameObject("BywaterOverlays");
            BywaterGridOverlays gridOverlays = overlaysGo.AddComponent<BywaterGridOverlays>();

            GameObject hudGo = new GameObject("BywaterHUD");
            BywaterHudController hudController = hudGo.AddComponent<BywaterHudController>();

            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            GameObject controllerGo = new GameObject("BywaterGameController");
            BywaterGameController controller = controllerGo.AddComponent<BywaterGameController>();
            controller.assetCatalog = catalog;
            controller.worldRenderer = worldRenderer;
            controller.cameraRig = cameraRig;
            controller.gridOverlays = gridOverlays;
            controller.hudController = hudController;
            EditorUtility.SetDirty(controller);

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);

            RegisterSceneInBuildSettings(ScenePath);
            Debug.Log("[M1BywaterSceneBuilder] Saved playable scene: " + ScenePath);
        }

        public static void RegisterSceneInBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            System.Collections.Generic.List<EditorBuildSettingsScene> updated =
                new System.Collections.Generic.List<EditorBuildSettingsScene>();

            updated.Add(new EditorBuildSettingsScene(scenePath, true));
            for (int i = 0; i < existing.Length; i++)
            {
                if (!string.Equals(existing[i].path, scenePath, System.StringComparison.OrdinalIgnoreCase))
                {
                    // Keep SampleScene out if replaced, or keep disabled if non-M1; preserve M0 untouched in Assets/Scenes
                    if (!existing[i].path.EndsWith("SampleScene.unity", System.StringComparison.OrdinalIgnoreCase))
                    {
                        updated.Add(existing[i]);
                    }
                }
            }

            EditorBuildSettings.scenes = updated.ToArray();
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
