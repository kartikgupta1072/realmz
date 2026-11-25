using RealmzCore.Exploration;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Realmz.World
{
    /// <summary>
    /// Controls dynamic URP lighting, ambient trilight, post-processing volume overrides,
    /// smooth Day/Night survival cycle transitions (Dawn -> Day -> Dusk -> Night),
    /// and sunlit atmospheric pollen motes between Overworld phases and Torchlit Dungeon.
    /// </summary>
    public sealed class BywaterBiomeAtmosphere : MonoBehaviour
    {
        private struct AtmosphereState
        {
            public Color sunColor;
            public float sunIntensity;
            public Quaternion sunRotation;
            public float shadowStrength;
            public Color ambientSky;
            public Color ambientEquator;
            public Color ambientGround;
            public float ambientIntensity;
            public bool fogEnabled;
            public Color fogColor;
            public float fogStart;
            public float fogEnd;
            public Color cameraBg;
            public TonemappingMode tonemapping;
            public float postExposure;
            public float contrast;
            public float saturation;
            public Color splitHighlights;
            public Color splitShadows;
            public float splitBalance;
            public float bloomThreshold;
            public float bloomIntensity;
            public float bloomScatter;
            public float vignetteIntensity;
            public float vignetteSmoothness;
            public bool playSunMotes;
        }

        [Header("Scene References")]
        public Light directionalSun;
        public Volume globalVolume;
        public Camera targetCamera;
        public ParticleSystem sunMoteParticles;

        private const float TransitionDurationSeconds = 1.05f;

        private Texture2D _runtimeMoteTexture;
        private Material _runtimeMoteMaterial;
        private AtmosphereState _currentState;
        private AtmosphereState _targetState;
        private AtmosphereState _startState;
        private bool _hasInitializedState;
        private bool _isTransitioning;
        private float _transitionElapsed;

        public bool IsDungeonActive { get; private set; }
        public WorldTimePhase CurrentTimePhase { get; private set; } = WorldTimePhase.Day;
        public bool HasLitTorch { get; private set; }
        public bool IsCombatActive { get; private set; }

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (!_isTransitioning || !Application.isPlaying)
            {
                return;
            }

            _transitionElapsed += Time.unscaledDeltaTime;
            float rawT = Mathf.Clamp01(_transitionElapsed / TransitionDurationSeconds);
            float t = rawT * rawT * (3f - 2f * rawT);

            AtmosphereState blended = LerpState(_startState, _targetState, t);
            _currentState = blended;
            ApplyAtmosphereStateImmediate(blended);

            if (rawT >= 1f)
            {
                _currentState = _targetState;
                _isTransitioning = false;
            }
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
            ApplyBiome(isDungeon, WorldTimePhase.Day, 0.5f, isDungeon, immediate, isCombat: false);
        }

        public void ApplyBiome(
            bool isDungeon,
            WorldTimePhase timePhase,
            float phaseProgress01 = 0.5f,
            bool hasLitTorch = false,
            bool immediate = true,
            bool isCombat = false)
        {
            ResolveReferences();
            IsDungeonActive = isDungeon;
            CurrentTimePhase = timePhase;
            HasLitTorch = hasLitTorch;
            IsCombatActive = isCombat;

            AtmosphereState desired = BuildTargetState(isDungeon, timePhase, phaseProgress01, hasLitTorch, isCombat);

            if (immediate || !Application.isPlaying || !_hasInitializedState)
            {
                _currentState = desired;
                _targetState = desired;
                _startState = desired;
                _hasInitializedState = true;
                _isTransitioning = false;
                _transitionElapsed = 0f;
                ApplyAtmosphereStateImmediate(desired);
            }
            else
            {
                _startState = _currentState;
                _targetState = desired;
                _transitionElapsed = 0f;
                _isTransitioning = true;
            }
        }

        public void UpdateFocusPosition(Vector3 worldPos)
        {
            if (!IsDungeonActive && CurrentTimePhase != WorldTimePhase.Night && sunMoteParticles != null)
            {
                sunMoteParticles.transform.position = new Vector3(worldPos.x, 2.6f, worldPos.z);
            }
        }

        private static AtmosphereState BuildTargetState(
            bool isDungeon,
            WorldTimePhase timePhase,
            float phaseProgress01,
            bool hasLitTorch,
            bool isCombat = false)
        {
            if (isDungeon)
            {
                return BuildDungeonState(hasLitTorch);
            }

            switch (timePhase)
            {
                case WorldTimePhase.Dawn:
                    return BuildDawnState(phaseProgress01);
                case WorldTimePhase.Dusk:
                    return BuildDuskState(phaseProgress01);
                case WorldTimePhase.Night:
                    return isCombat ? BuildCombatNightState(hasLitTorch) : BuildNightState(hasLitTorch);
                default:
                    return BuildSunlitDaylightState();
            }
        }

        private static AtmosphereState BuildDawnState(float phaseProgress01)
        {
            float sunPitch = Mathf.Lerp(24f, 34f, Mathf.Clamp01(phaseProgress01));
            return new AtmosphereState
            {
                sunColor = new Color(1.00f, 0.78f, 0.52f, 1f),
                sunIntensity = 1.48f,
                sunRotation = Quaternion.Euler(sunPitch, -56f, 0f),
                shadowStrength = 0.74f,
                ambientSky = new Color(0.28f, 0.36f, 0.64f, 1f),
                ambientEquator = new Color(0.24f, 0.22f, 0.28f, 1f),
                ambientGround = new Color(0.10f, 0.10f, 0.15f, 1f),
                ambientIntensity = 0.72f,
                fogEnabled = true,
                fogColor = new Color(0.46f, 0.44f, 0.52f, 1f),
                fogStart = 62f,
                fogEnd = 138f,
                cameraBg = new Color(0.28f, 0.36f, 0.54f, 1f),
                tonemapping = TonemappingMode.Neutral,
                postExposure = -0.28f,
                contrast = 18f,
                saturation = 12f,
                splitHighlights = new Color(1.0f, 0.88f, 0.68f, 1f),
                splitShadows = new Color(0.56f, 0.64f, 0.90f, 1f),
                splitBalance = -6f,
                bloomThreshold = 1.42f,
                bloomIntensity = 0.22f,
                bloomScatter = 0.52f,
                vignetteIntensity = 0.18f,
                vignetteSmoothness = 0.52f,
                playSunMotes = true
            };
        }

        private static AtmosphereState BuildSunlitDaylightState()
        {
            // Deep cool forest-indigo shadows + far-pushed fog + dialed-back exposure eliminate milky daytime washout.
            return new AtmosphereState
            {
                sunColor = new Color(1.00f, 0.920f, 0.780f, 1f),
                sunIntensity = 1.82f,
                sunRotation = Quaternion.Euler(44f, -36f, 0f),
                shadowStrength = 0.78f,
                ambientSky = new Color(0.24f, 0.36f, 0.72f, 1f),
                ambientEquator = new Color(0.16f, 0.21f, 0.32f, 1f),
                ambientGround = new Color(0.08f, 0.10f, 0.16f, 1f),
                ambientIntensity = 0.72f,
                fogEnabled = true,
                fogColor = new Color(0.36f, 0.50f, 0.72f, 1f),
                fogStart = 76f,
                fogEnd = 160f,
                cameraBg = new Color(0.26f, 0.42f, 0.66f, 1f),
                tonemapping = TonemappingMode.Neutral,
                postExposure = -0.32f,
                contrast = 20f,
                saturation = 11f,
                splitHighlights = new Color(1.0f, 0.94f, 0.80f, 1f),
                splitShadows = new Color(0.52f, 0.64f, 0.92f, 1f),
                splitBalance = -8f,
                bloomThreshold = 1.45f,
                bloomIntensity = 0.18f,
                bloomScatter = 0.50f,
                vignetteIntensity = 0.15f,
                vignetteSmoothness = 0.52f,
                playSunMotes = true
            };
        }

        private static AtmosphereState BuildDuskState(float phaseProgress01)
        {
            float sunPitch = Mathf.Lerp(30f, 20f, Mathf.Clamp01(phaseProgress01));
            return new AtmosphereState
            {
                sunColor = new Color(1.00f, 0.60f, 0.34f, 1f),
                sunIntensity = 1.26f,
                sunRotation = Quaternion.Euler(sunPitch, 48f, 0f),
                shadowStrength = 0.72f,
                ambientSky = new Color(0.22f, 0.22f, 0.46f, 1f),
                ambientEquator = new Color(0.22f, 0.17f, 0.24f, 1f),
                ambientGround = new Color(0.09f, 0.08f, 0.14f, 1f),
                ambientIntensity = 0.76f,
                fogEnabled = true,
                fogColor = new Color(0.30f, 0.24f, 0.34f, 1f),
                fogStart = 54f,
                fogEnd = 124f,
                cameraBg = new Color(0.18f, 0.16f, 0.28f, 1f),
                tonemapping = TonemappingMode.ACES,
                postExposure = -0.22f,
                contrast = 18f,
                saturation = 10f,
                splitHighlights = new Color(1.0f, 0.74f, 0.52f, 1f),
                splitShadows = new Color(0.48f, 0.54f, 0.86f, 1f),
                splitBalance = -12f,
                bloomThreshold = 1.32f,
                bloomIntensity = 0.26f,
                bloomScatter = 0.55f,
                vignetteIntensity = 0.22f,
                vignetteSmoothness = 0.50f,
                playSunMotes = true
            };
        }

        private static AtmosphereState BuildNightState(bool hasLitTorch)
        {
            return new AtmosphereState
            {
                sunColor = new Color(0.56f, 0.70f, 1.00f, 1f),
                sunIntensity = hasLitTorch ? 0.56f : 0.36f,
                sunRotation = Quaternion.Euler(46f, -24f, 0f),
                shadowStrength = 0.42f,
                ambientSky = new Color(0.15f, 0.24f, 0.50f, 1f),
                ambientEquator = new Color(0.12f, 0.19f, 0.38f, 1f),
                ambientGround = new Color(0.08f, 0.12f, 0.24f, 1f),
                ambientIntensity = hasLitTorch ? 1.15f : 0.88f,
                fogEnabled = true,
                fogColor = new Color(0.055f, 0.085f, 0.180f, 1f),
                fogStart = hasLitTorch ? 38f : 28f,
                fogEnd = hasLitTorch ? 92f : 68f,
                cameraBg = new Color(0.035f, 0.055f, 0.125f, 1f),
                tonemapping = TonemappingMode.ACES,
                postExposure = hasLitTorch ? 0.08f : -0.14f,
                contrast = 16f,
                saturation = 8f,
                splitHighlights = new Color(0.86f, 0.92f, 1.0f, 1f),
                splitShadows = new Color(0.46f, 0.56f, 0.92f, 1f),
                splitBalance = -16f,
                bloomThreshold = 1.24f,
                bloomIntensity = 0.34f,
                bloomScatter = 0.58f,
                vignetteIntensity = hasLitTorch ? 0.22f : 0.34f,
                vignetteSmoothness = 0.50f,
                playSunMotes = false
            };
        }

        private static AtmosphereState BuildCombatNightState(bool hasLitTorch)
        {
            // Cool silver-blue nocturnal combat floor: preserves night mood while keeping heroes, enemies, grid brackets, and cover crisp.
            return new AtmosphereState
            {
                sunColor = new Color(0.68f, 0.80f, 1.00f, 1f),
                sunIntensity = hasLitTorch ? 0.98f : 0.88f,
                sunRotation = Quaternion.Euler(50f, -26f, 0f),
                shadowStrength = 0.30f,
                ambientSky = new Color(0.30f, 0.42f, 0.70f, 1f),
                ambientEquator = new Color(0.24f, 0.34f, 0.58f, 1f),
                ambientGround = new Color(0.16f, 0.22f, 0.40f, 1f),
                ambientIntensity = hasLitTorch ? 1.50f : 1.40f,
                fogEnabled = true,
                fogColor = new Color(0.085f, 0.125f, 0.250f, 1f),
                fogStart = 60f,
                fogEnd = 138f,
                cameraBg = new Color(0.065f, 0.100f, 0.215f, 1f),
                tonemapping = TonemappingMode.Neutral,
                postExposure = hasLitTorch ? 0.28f : 0.22f,
                contrast = 6f,
                saturation = 10f,
                splitHighlights = new Color(0.88f, 0.94f, 1.0f, 1f),
                splitShadows = new Color(0.48f, 0.60f, 0.94f, 1f),
                splitBalance = -10f,
                bloomThreshold = 1.24f,
                bloomIntensity = 0.28f,
                bloomScatter = 0.56f,
                vignetteIntensity = hasLitTorch ? 0.12f : 0.16f,
                vignetteSmoothness = 0.52f,
                playSunMotes = false
            };
        }

        private static AtmosphereState BuildDungeonState(bool hasLitTorch)
        {
            return new AtmosphereState
            {
                sunColor = new Color(0.62f, 0.72f, 0.98f, 1f),
                sunIntensity = hasLitTorch ? 0.44f : 0.30f,
                sunRotation = Quaternion.Euler(44f, -36f, 0f),
                shadowStrength = 0.35f,
                ambientSky = new Color(0.18f, 0.28f, 0.50f, 1f),
                ambientEquator = new Color(0.14f, 0.22f, 0.42f, 1f),
                ambientGround = new Color(0.10f, 0.15f, 0.30f, 1f),
                ambientIntensity = hasLitTorch ? 1.25f : 0.90f,
                fogEnabled = true,
                fogColor = new Color(0.055f, 0.072f, 0.130f, 1f),
                fogStart = hasLitTorch ? 26f : 20f,
                fogEnd = hasLitTorch ? 62f : 48f,
                cameraBg = new Color(0.040f, 0.052f, 0.100f, 1f),
                tonemapping = TonemappingMode.ACES,
                postExposure = hasLitTorch ? 0.02f : -0.18f,
                contrast = 16f,
                saturation = 8f,
                splitHighlights = new Color(1.0f, 0.88f, 0.72f, 1f),
                splitShadows = new Color(0.58f, 0.68f, 0.94f, 1f),
                splitBalance = -18f,
                bloomThreshold = 1.32f,
                bloomIntensity = 0.30f,
                bloomScatter = 0.58f,
                vignetteIntensity = hasLitTorch ? 0.26f : 0.36f,
                vignetteSmoothness = 0.48f,
                playSunMotes = false
            };
        }

        private static AtmosphereState LerpState(in AtmosphereState a, in AtmosphereState b, float t)
        {
            return new AtmosphereState
            {
                sunColor = Color.Lerp(a.sunColor, b.sunColor, t),
                sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
                sunRotation = Quaternion.Slerp(a.sunRotation, b.sunRotation, t),
                shadowStrength = Mathf.Lerp(a.shadowStrength, b.shadowStrength, t),
                ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t),
                ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t),
                ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t),
                ambientIntensity = Mathf.Lerp(a.ambientIntensity, b.ambientIntensity, t),
                fogEnabled = b.fogEnabled,
                fogColor = Color.Lerp(a.fogColor, b.fogColor, t),
                fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t),
                fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t),
                cameraBg = Color.Lerp(a.cameraBg, b.cameraBg, t),
                tonemapping = t >= 0.5f ? b.tonemapping : a.tonemapping,
                postExposure = Mathf.Lerp(a.postExposure, b.postExposure, t),
                contrast = Mathf.Lerp(a.contrast, b.contrast, t),
                saturation = Mathf.Lerp(a.saturation, b.saturation, t),
                splitHighlights = Color.Lerp(a.splitHighlights, b.splitHighlights, t),
                splitShadows = Color.Lerp(a.splitShadows, b.splitShadows, t),
                splitBalance = Mathf.Lerp(a.splitBalance, b.splitBalance, t),
                bloomThreshold = Mathf.Lerp(a.bloomThreshold, b.bloomThreshold, t),
                bloomIntensity = Mathf.Lerp(a.bloomIntensity, b.bloomIntensity, t),
                bloomScatter = Mathf.Lerp(a.bloomScatter, b.bloomScatter, t),
                vignetteIntensity = Mathf.Lerp(a.vignetteIntensity, b.vignetteIntensity, t),
                vignetteSmoothness = Mathf.Lerp(a.vignetteSmoothness, b.vignetteSmoothness, t),
                playSunMotes = b.playSunMotes
            };
        }

        private void ApplyAtmosphereStateImmediate(in AtmosphereState state)
        {
            if (directionalSun != null)
            {
                directionalSun.color = state.sunColor;
                directionalSun.intensity = state.sunIntensity;
                directionalSun.transform.rotation = state.sunRotation;
                directionalSun.shadows = LightShadows.Soft;
                directionalSun.shadowStrength = state.shadowStrength;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = state.ambientSky;
            RenderSettings.ambientEquatorColor = state.ambientEquator;
            RenderSettings.ambientGroundColor = state.ambientGround;
            RenderSettings.ambientIntensity = state.ambientIntensity;
            RenderSettings.fog = state.fogEnabled;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = state.fogColor;
            RenderSettings.fogStartDistance = state.fogStart;
            RenderSettings.fogEndDistance = state.fogEnd;

            if (targetCamera != null)
            {
                targetCamera.backgroundColor = state.cameraBg;
            }

            ApplyVolumeOverrides(
                tonemappingMode: state.tonemapping,
                postExposure: state.postExposure,
                contrast: state.contrast,
                saturation: state.saturation,
                splitHighlights: state.splitHighlights,
                splitShadows: state.splitShadows,
                splitBalance: state.splitBalance,
                bloomThreshold: state.bloomThreshold,
                bloomIntensity: state.bloomIntensity,
                bloomScatter: state.bloomScatter,
                vignetteIntensity: state.vignetteIntensity,
                vignetteSmoothness: state.vignetteSmoothness);

            if (state.playSunMotes)
            {
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
            else if (sunMoteParticles != null)
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
