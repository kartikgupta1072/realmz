using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Realmz.World
{
    /// <summary>
    /// Controls dynamic URP lighting, ambient trilight, post-processing volume overrides,
    /// and sunlit atmospheric pollen motes between Sunlit Daylight overworld and Torchlit Dungeon.
    /// </summary>
    public sealed class BywaterBiomeAtmosphere : MonoBehaviour
    {
        [Header("Scene References")]
        public Light directionalSun;
        public Volume globalVolume;
        public Camera targetCamera;
        public ParticleSystem sunMoteParticles;

        private Texture2D _runtimeMoteTexture;
        private Material _runtimeMoteMaterial;

        public bool IsDungeonActive { get; private set; }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDestroy()
        {
            if (_runtimeMoteMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMoteMaterial);
                }
                else
                {
                    DestroyImmediate(_runtimeMoteMaterial);
                }
                _runtimeMoteMaterial = null;
            }

            if (_runtimeMoteTexture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMoteTexture);
                }
                else
                {
                    DestroyImmediate(_runtimeMoteTexture);
                }
                _runtimeMoteTexture = null;
            }
        }

        public void ApplyBiome(bool isDungeon, bool immediate = true)
        {
            ResolveReferences();
            IsDungeonActive = isDungeon;

            if (!isDungeon)
            {
                ApplySunlitDaylight();
            }
            else
            {
                ApplyTorchlitDungeon();
            }
        }

        public void UpdateFocusPosition(Vector3 worldPos)
        {
            if (!IsDungeonActive && sunMoteParticles != null)
            {
                sunMoteParticles.transform.position = new Vector3(worldPos.x, 2.6f, worldPos.z);
            }
        }

        private void ApplySunlitDaylight()
        {
            if (directionalSun != null)
            {
                directionalSun.color = new Color(1.0f, 0.941f, 0.831f, 1f);
                directionalSun.intensity = 1.85f;
                directionalSun.transform.rotation = Quaternion.Euler(44f, -36f, 0f);
                directionalSun.shadows = LightShadows.Soft;
                directionalSun.shadowStrength = 0.65f;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.56f, 0.78f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.38f, 0.35f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.18f, 1f);
            RenderSettings.ambientIntensity = 0.90f;

            if (targetCamera != null)
            {
                targetCamera.backgroundColor = new Color(0.34f, 0.50f, 0.72f, 1f);
            }

            ApplyVolumeOverrides(
                tonemappingMode: TonemappingMode.Neutral,
                postExposure: -0.18f,
                contrast: 15f,
                saturation: 14f,
                splitHighlights: new Color(1.0f, 0.95f, 0.84f, 1f),
                splitShadows: new Color(0.84f, 0.90f, 1.0f, 1f),
                splitBalance: 10f,
                bloomThreshold: 1.40f,
                bloomIntensity: 0.26f,
                bloomScatter: 0.58f,
                vignetteIntensity: 0.12f,
                vignetteSmoothness: 0.55f);

            EnsureSunMoteParticles();
            if (sunMoteParticles != null)
            {
                ParticleSystem.EmissionModule emission = sunMoteParticles.emission;
                emission.enabled = true;
                if (!sunMoteParticles.isPlaying)
                {
                    sunMoteParticles.Play();
                }
            }
        }

        private void ApplyTorchlitDungeon()
        {
            if (directionalSun != null)
            {
                directionalSun.color = new Color(0.42f, 0.52f, 0.82f, 1f);
                directionalSun.intensity = 0.28f;
                directionalSun.shadows = LightShadows.Soft;
                directionalSun.shadowStrength = 0.45f;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.20f, 0.25f, 0.42f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.14f, 0.19f, 0.32f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.12f, 0.22f, 1f);
            RenderSettings.ambientIntensity = 1.0f;

            if (targetCamera != null)
            {
                targetCamera.backgroundColor = new Color(0.045f, 0.058f, 0.110f, 1f);
            }

            ApplyVolumeOverrides(
                tonemappingMode: TonemappingMode.ACES,
                postExposure: 0.04f,
                contrast: 14f,
                saturation: 8f,
                splitHighlights: new Color(1.0f, 0.88f, 0.72f, 1f),
                splitShadows: new Color(0.68f, 0.76f, 0.96f, 1f),
                splitBalance: -15f,
                bloomThreshold: 1.35f,
                bloomIntensity: 0.32f,
                bloomScatter: 0.62f,
                vignetteIntensity: 0.26f,
                vignetteSmoothness: 0.48f);

            if (sunMoteParticles != null)
            {
                ParticleSystem.EmissionModule emission = sunMoteParticles.emission;
                emission.enabled = false;
                sunMoteParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ApplyVolumeOverrides(
            TonemappingMode tonemappingMode,
            float postExposure,
            float contrast,
            float saturation,
            Color splitHighlights,
            Color splitShadows,
            float splitBalance,
            float bloomThreshold,
            float bloomIntensity,
            float bloomScatter,
            float vignetteIntensity,
            float vignetteSmoothness)
        {
            if (globalVolume == null)
            {
                return;
            }

            VolumeProfile activeProfile = Application.isPlaying ? globalVolume.profile : globalVolume.sharedProfile;
            if (activeProfile == null)
            {
                return;
            }

            Tonemapping tonemapping = GetOrAddOverride<Tonemapping>(activeProfile);
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = tonemappingMode;

            ColorAdjustments colorAdjustments = GetOrAddOverride<ColorAdjustments>(activeProfile);
            colorAdjustments.active = true;
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = postExposure;
            colorAdjustments.contrast.overrideState = true;
            colorAdjustments.contrast.value = contrast;
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = saturation;
            colorAdjustments.colorFilter.overrideState = true;
            colorAdjustments.colorFilter.value = Color.white;

            SplitToning splitToning = GetOrAddOverride<SplitToning>(activeProfile);
            splitToning.active = true;
            splitToning.highlights.overrideState = true;
            splitToning.highlights.value = splitHighlights;
            splitToning.shadows.overrideState = true;
            splitToning.shadows.value = splitShadows;
            splitToning.balance.overrideState = true;
            splitToning.balance.value = splitBalance;

            Bloom bloom = GetOrAddOverride<Bloom>(activeProfile);
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = bloomThreshold;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = bloomIntensity;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = bloomScatter;

            Vignette vignette = GetOrAddOverride<Vignette>(activeProfile);
            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = vignetteIntensity;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = vignetteSmoothness;
        }

        private static T GetOrAddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
            }

            return component;
        }

        private void EnsureSunMoteParticles()
        {
            if (sunMoteParticles != null)
            {
                return;
            }

            Transform existing = transform.Find("SunbeamMotes");
            if (existing != null)
            {
                sunMoteParticles = existing.GetComponent<ParticleSystem>();
                if (sunMoteParticles != null)
                {
                    return;
                }
            }

            GameObject psGo = new GameObject("SunbeamMotes");
            psGo.transform.SetParent(transform, false);
            psGo.transform.position = new Vector3(0f, 2.6f, 0f);

            ParticleSystem ps = psGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 85;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 7.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.94f, 0.70f, 0.56f),
                new Color(1.0f, 0.98f, 0.88f, 0.36f));
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 14f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(20f, 4f, 18f);

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.14f, -0.04f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);

            ParticleSystem.ColorOverLifetimeModule colorLife = ps.colorOverLifetime;
            colorLife.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.95f, 0.78f), 0f),
                    new GradientColorKey(new Color(1.0f, 0.97f, 0.85f), 0.5f),
                    new GradientColorKey(new Color(1.0f, 0.93f, 0.70f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.75f, 0.22f),
                    new GradientAlphaKey(0.70f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorLife.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystemRenderer psRenderer = psGo.GetComponent<ParticleSystemRenderer>();
            psRenderer.shadowCastingMode = ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;
            psRenderer.sharedMaterial = GetOrCreateMoteMaterial();

            sunMoteParticles = ps;
        }

        private Material GetOrCreateMoteMaterial()
        {
            if (_runtimeMoteMaterial != null)
            {
                return _runtimeMoteMaterial;
            }

            if (_runtimeMoteTexture == null)
            {
                _runtimeMoteTexture = CreateSoftRadialTexture(32);
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material mat = new Material(shader);
            mat.name = "RuntimeSunbeamMote";
            mat.mainTexture = _runtimeMoteTexture;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", _runtimeMoteTexture);
            }
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", Color.white);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", Color.white);
            }
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 2f);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.One);
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            _runtimeMoteMaterial = mat;
            return _runtimeMoteMaterial;
        }

        private static Texture2D CreateSoftRadialTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float half = (size - 1) * 0.5f;
            float invHalf = 1f / Mathf.Max(1f, half);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) * invHalf;
                    float dy = (y - half) * invHalf;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = Mathf.Clamp01(1f - r);
                    float alpha = falloff * falloff * (3f - 2f * falloff);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private void ResolveReferences()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (globalVolume == null)
            {
                globalVolume = FindFirstObjectByType<Volume>();
            }

            if (directionalSun == null)
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                for (int i = 0; i < lights.Length; i++)
                {
                    Light candidate = lights[i];
                    if (candidate != null && candidate.type == LightType.Directional)
                    {
                        directionalSun = candidate;
                        break;
                    }
                }
            }
        }
    }
}
