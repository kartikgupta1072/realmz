using Realmz.Audio;
using Realmz.Cartography;
using Realmz.Data;
using Realmz.Game;
using Realmz.World;
using RealmzCore.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Realmz.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/M1_Bywater_Slice.unity with URP 3D HD-2D lighting, Neutral + Bloom,
    /// single AudioListener on Main Camera, InputSystem UI EventSystem, and wired BywaterGameController
    /// + BywaterAudioDirector + BywaterAssetCatalog.
    /// Registers M1_Bywater_Slice.unity at index 0 in EditorBuildSettings while leaving BywaterDiorama_Proof.unity off the build list.
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

            // Load the committed mixer first. Re-running BuildMasterMixer through Unity's internal
            // AudioMixerController API appends hidden Attenuation effect sub-assets on every call,
            // so only build it when the asset is missing (use Realmz/Audio/Build Master AudioMixer to rebuild).
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(RealmzAudioMixerBuilder.MixerAssetPath)
                ?? RealmzAudioMixerBuilder.BuildMasterMixer();
            if (mixer == null)
            {
                Debug.LogWarning("[M1BywaterSceneBuilder] Failed to load or build RealmzMasterMixer.mixer.");
            }

            RealmzBalanceCatalog balanceCatalog = RealmzBalanceCatalogEditor.CreateOrSyncAsset()
                ?? AssetDatabase.LoadAssetAtPath<RealmzBalanceCatalog>(RealmzBalanceCatalogEditor.AssetPath)
                ?? Resources.Load<RealmzBalanceCatalog>("RealmzBalanceCatalog");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Crisp sunlit trilight ambient with rich forest-indigo shadows for City of Bywater
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.24f, 0.36f, 0.72f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.21f, 0.32f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.10f, 0.16f, 1f);
            RenderSettings.ambientIntensity = 0.72f;
            RenderSettings.fog = false;

            // 2. Perspective HD-2D Camera Rig with URP Post-Processing & HDR
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.26f, 0.42f, 0.66f, 1f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 140f;
            cam.allowHDR = true;
            cam.allowMSAA = false;

            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.volumeLayerMask = ~0;
            camData.antialiasing = AntialiasingMode.None;

            camGo.AddComponent<AudioListener>();

            BywaterCameraRig cameraRig = camGo.AddComponent<BywaterCameraRig>();
            cameraRig.SetWorldBounds(90, 90);
            cameraRig.SetFollowTarget(CartographyWorldData.GridToWorld(new GridCoord(2, 1), 0f), immediate: true);

            // 3. Warm Directional Sun with grounded 3D soft shadows
            GameObject sunGo = new GameObject("Directional Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.920f, 0.780f, 1f);
            sun.intensity = 1.82f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.78f;
            sunGo.transform.rotation = Quaternion.Euler(44f, -36f, 0f);

            // 4. Global URP Volume: Neutral Tonemapping + ColorAdjustments + SplitToning + Bloom + Vignette
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
            tonemapping.mode.value = TonemappingMode.Neutral;

            if (!profile.TryGet<ColorAdjustments>(out var colorAdjustments))
            {
                colorAdjustments = profile.Add<ColorAdjustments>(true);
            }
            if (!AssetDatabase.Contains(colorAdjustments))
            {
                AssetDatabase.AddObjectToAsset(colorAdjustments, profile);
            }
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = -0.32f;
            colorAdjustments.contrast.overrideState = true;
            colorAdjustments.contrast.value = 20f;
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = 11f;

            if (!profile.TryGet<SplitToning>(out var splitToning))
            {
                splitToning = profile.Add<SplitToning>(true);
            }
            if (!AssetDatabase.Contains(splitToning))
            {
                AssetDatabase.AddObjectToAsset(splitToning, profile);
            }
            splitToning.highlights.overrideState = true;
            splitToning.highlights.value = new Color(1.0f, 0.94f, 0.80f, 1f);
            splitToning.shadows.overrideState = true;
            splitToning.shadows.value = new Color(0.52f, 0.64f, 0.92f, 1f);
            splitToning.balance.overrideState = true;
            splitToning.balance.value = -8f;

            if (!profile.TryGet<Bloom>(out var bloom))
            {
                bloom = profile.Add<Bloom>(true);
            }
            if (!AssetDatabase.Contains(bloom))
            {
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.45f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.18f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.50f;

            if (!profile.TryGet<Vignette>(out var vignette))
            {
                vignette = profile.Add<Vignette>(true);
            }
            if (!AssetDatabase.Contains(vignette))
            {
                AssetDatabase.AddObjectToAsset(vignette, profile);
            }
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.15f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.52f;

            EditorUtility.SetDirty(profile);

            GameObject volGo = new GameObject("Global Volume (Neutral + Bloom)");
            Volume vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = profile;

            // 5. Biome Atmosphere, World, Overlays, HUD, InputSystem EventSystem, and Game Controller
            GameObject atmoGo = new GameObject("BywaterAtmosphere");
            BywaterBiomeAtmosphere atmosphere = atmoGo.AddComponent<BywaterBiomeAtmosphere>();
            atmosphere.directionalSun = sun;
            atmosphere.globalVolume = vol;
            atmosphere.targetCamera = cam;
            EditorUtility.SetDirty(atmosphere);

            GameObject worldGo = new GameObject("BywaterWorld");
            BywaterWorldRenderer worldRenderer = worldGo.AddComponent<BywaterWorldRenderer>();

            GameObject overlaysGo = new GameObject("BywaterOverlays");
            BywaterGridOverlays gridOverlays = overlaysGo.AddComponent<BywaterGridOverlays>();
            if (overlaysGo.GetComponent<BywaterCombatVfxDirector>() == null)
            {
                overlaysGo.AddComponent<BywaterCombatVfxDirector>();
            }
            EditorUtility.SetDirty(overlaysGo);

            GameObject hudGo = new GameObject("BywaterHUD");
            BywaterHudController hudController = hudGo.AddComponent<BywaterHudController>();

            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            GameObject controllerGo = new GameObject("BywaterGameController");
            GameObject audioGo = new GameObject("BywaterAudioDirector");
            audioGo.transform.SetParent(controllerGo.transform, false);
            BywaterAudioDirector audioDirector = audioGo.AddComponent<BywaterAudioDirector>();
            if (mixer != null)
            {
                audioDirector.BindMixer(mixer);
            }

            BywaterGameController controller = controllerGo.AddComponent<BywaterGameController>();
            controller.assetCatalog = catalog;
            controller.worldRenderer = worldRenderer;
            controller.cameraRig = cameraRig;
            controller.gridOverlays = gridOverlays;
            controller.hudController = hudController;
            controller.biomeAtmosphere = atmosphere;
            controller.audioDirector = audioDirector;
            controller.masterMixer = mixer;
            controller.balanceCatalog = balanceCatalog;
            EditorUtility.SetDirty(audioDirector);
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
                    // Leave SampleScene.unity and BywaterDiorama_Proof.unity off the build list while keeping files intact.
                    if (!existing[i].path.EndsWith("SampleScene.unity", System.StringComparison.OrdinalIgnoreCase) &&
                        !existing[i].path.EndsWith("BywaterDiorama_Proof.unity", System.StringComparison.OrdinalIgnoreCase))
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
