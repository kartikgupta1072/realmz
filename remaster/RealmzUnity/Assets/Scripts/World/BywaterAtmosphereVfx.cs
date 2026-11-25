using System.Collections.Generic;
using Realmz.Cartography;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.World
{
    /// <summary>
    /// Pre-allocated particle VFX pools for living-world atmosphere:
    /// chimney smoke plumes, brazier/campfire/sconce embers, and wind-tilted pixel rain.
    /// Zero per-frame Instantiate/Destroy or GC allocations.
    /// </summary>
    public sealed class BywaterAtmosphereVfx : MonoBehaviour
    {
        private const int SmokePoolSize = 4;
        private const int EmberPoolSize = 8;
        private const int RainMaxParticles = 220;

        private struct AnchorEntry
        {
            public Vector3 Position;
            public float HeightOffset;
        }

        private readonly ParticleSystem[] _smokeEmitters = new ParticleSystem[SmokePoolSize];
        private readonly ParticleSystem[] _emberEmitters = new ParticleSystem[EmberPoolSize];
        private ParticleSystem _rainEmitter;

        private readonly List<AnchorEntry> _lightAnchors = new List<AnchorEntry>(64);
        private readonly List<Vector3> _selectedEmberPositions = new List<Vector3>(EmberPoolSize);
        private readonly List<float> _selectedEmberDistSq = new List<float>(EmberPoolSize);

        private Texture2D _smokePixelTexture;
        private Material _smokeMaterial;
        private Texture2D _emberPixelTexture;
        private Material _emberMaterial;
        private Texture2D _rainStreakTexture;
        private Material _rainMaterial;

        private bool _poolsInitialized;
        private bool _isDungeon;
        private bool _isRaining;
        private float _windScale = 1f;
        private bool _hasRuntimeCampfire;
        private Vector3 _runtimeCampfirePos;
        private Vector3 _focusWorldPos;

        public int ActiveSmokeEmitterCount { get; private set; }
        public int ActiveEmberEmitterCount { get; private set; }
        public bool IsRainActive { get; private set; }
        public float WindScale => _windScale;
        public int MaxRainParticles => RainMaxParticles;

        private void Awake()
        {
            EnsurePoolsAllocated();
        }

        private void OnDestroy()
        {
            DestroyResource(_smokeMaterial);
            _smokeMaterial = null;
            DestroyResource(_smokePixelTexture);
            _smokePixelTexture = null;

            DestroyResource(_emberMaterial);
            _emberMaterial = null;
            DestroyResource(_emberPixelTexture);
            _emberPixelTexture = null;

            DestroyResource(_rainMaterial);
            _rainMaterial = null;
            DestroyResource(_rainStreakTexture);
            _rainStreakTexture = null;
        }

        public void ConfigureFromWorldData(CartographyWorldData data, bool isDungeon)
        {
            EnsurePoolsAllocated();
            _isDungeon = isDungeon;

            _lightAnchors.Clear();
            int smokeIdx = 0;

            if (data != null && data.Props != null)
            {
                IReadOnlyList<CartographyPropPlacement> props = data.Props;
                for (int i = 0; i < props.Count; i++)
                {
                    CartographyPropPlacement prop = props[i];
                    if (!isDungeon && smokeIdx < SmokePoolSize && IsChimneyBuilding(prop.Kind))
                    {
                        Vector3 chimneyPos = ResolveChimneyWorldPosition(prop);
                        ConfigureSmokeEmitter(_smokeEmitters[smokeIdx], chimneyPos, enabled: true);
                        smokeIdx++;
                    }

                    if (TryGetEmberHeightOffset(prop.Kind, out float emberOffset))
                    {
                        _lightAnchors.Add(new AnchorEntry
                        {
                            Position = prop.WorldPosition,
                            HeightOffset = emberOffset
                        });
                    }
                }
            }

            ActiveSmokeEmitterCount = smokeIdx;
            for (int i = smokeIdx; i < SmokePoolSize; i++)
            {
                ConfigureSmokeEmitter(_smokeEmitters[i], Vector3.zero, enabled: false);
            }

            RefreshEmberSelection();
            ApplyWeatherState();
        }

        public void UpdateFocusPosition(Vector3 worldPos)
        {
            EnsurePoolsAllocated();
            _focusWorldPos = worldPos;

            if (_rainEmitter != null)
            {
                _rainEmitter.transform.position = new Vector3(worldPos.x, worldPos.y + 7.5f, worldPos.z);
            }

            RefreshEmberSelection();
        }

        public void SetWeather(bool isRaining, float windScale)
        {
            EnsurePoolsAllocated();
            _isRaining = isRaining;
            _windScale = Mathf.Clamp(windScale, 0f, 3.0f);
            ApplyWeatherState();
        }

        public void SetCampfirePosition(bool active, Vector3 worldPos)
        {
            EnsurePoolsAllocated();
            _hasRuntimeCampfire = active;
            _runtimeCampfirePos = worldPos;
            RefreshEmberSelection();
        }

        private void EnsurePoolsAllocated()
        {
            if (_poolsInitialized)
            {
                return;
            }

            EnsureMaterials();

            for (int i = 0; i < SmokePoolSize; i++)
            {
                _smokeEmitters[i] = CreateSmokeEmitter(i);
            }

            for (int i = 0; i < EmberPoolSize; i++)
            {
                _emberEmitters[i] = CreateEmberEmitter(i);
            }

            _rainEmitter = CreateRainEmitter();
            _poolsInitialized = true;
        }

        private void RefreshEmberSelection()
        {
            _selectedEmberPositions.Clear();
            _selectedEmberDistSq.Clear();

            if (_hasRuntimeCampfire)
            {
                _selectedEmberPositions.Add(_runtimeCampfirePos + new Vector3(0f, 0.35f, 0f));
                _selectedEmberDistSq.Add(-1f);
            }

            const float maxRangeSq = 32f * 32f;
            for (int i = 0; i < _lightAnchors.Count; i++)
            {
                AnchorEntry anchor = _lightAnchors[i];
                Vector3 candidate = new Vector3(
                    anchor.Position.x,
                    anchor.Position.y + anchor.HeightOffset,
                    anchor.Position.z);

                float dx = candidate.x - _focusWorldPos.x;
                float dz = candidate.z - _focusWorldPos.z;
                float distSq = dx * dx + dz * dz;
                if (distSq > maxRangeSq && _selectedEmberPositions.Count >= EmberPoolSize)
                {
                    continue;
                }

                if (_selectedEmberPositions.Count < EmberPoolSize)
                {
                    _selectedEmberPositions.Add(candidate);
                    _selectedEmberDistSq.Add(distSq);
                }
                else
                {
                    int worstIdx = -1;
                    float worstDist = distSq;
                    for (int k = 0; k < _selectedEmberDistSq.Count; k++)
                    {
                        if (_selectedEmberDistSq[k] > worstDist)
                        {
                            worstDist = _selectedEmberDistSq[k];
                            worstIdx = k;
                        }
                    }

                    if (worstIdx >= 0)
                    {
                        _selectedEmberPositions[worstIdx] = candidate;
                        _selectedEmberDistSq[worstIdx] = distSq;
                    }
                }
            }

            int activeCount = _selectedEmberPositions.Count;
            ActiveEmberEmitterCount = activeCount;

            for (int i = 0; i < activeCount; i++)
            {
                ParticleSystem ps = _emberEmitters[i];
                if (ps == null)
                {
                    continue;
                }

                ps.transform.position = _selectedEmberPositions[i];
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.enabled = true;
                if (!ps.isPlaying)
                {
                    ps.Play();
                }
            }

            for (int i = activeCount; i < EmberPoolSize; i++)
            {
                ParticleSystem ps = _emberEmitters[i];
                if (ps == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = ps.emission;
                emission.enabled = false;
                if (ps.isPlaying || ps.particleCount > 0)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void ApplyWeatherState()
        {
            for (int i = 0; i < SmokePoolSize; i++)
            {
                ParticleSystem smoke = _smokeEmitters[i];
                if (smoke == null)
                {
                    continue;
                }

                ParticleSystem.VelocityOverLifetimeModule vel = smoke.velocityOverLifetime;
                vel.x = new ParticleSystem.MinMaxCurve(0.86f * 0.42f * _windScale, 0.86f * 0.62f * _windScale);
                vel.z = new ParticleSystem.MinMaxCurve(0.51f * 0.42f * _windScale, 0.51f * 0.62f * _windScale);
            }

            bool rainShouldPlay = _isRaining && !_isDungeon;
            IsRainActive = rainShouldPlay;

            if (_rainEmitter == null)
            {
                return;
            }

            ParticleSystem.VelocityOverLifetimeModule rainVel = _rainEmitter.velocityOverLifetime;
            rainVel.x = 1.6f * _windScale;
            rainVel.y = -12.5f;
            rainVel.z = 0.9f * _windScale;

            ParticleSystem.EmissionModule emission = _rainEmitter.emission;
            if (rainShouldPlay)
            {
                emission.enabled = true;
                emission.rateOverTime = 200f;
                if (!_rainEmitter.isPlaying)
                {
                    _rainEmitter.Play();
                }
            }
            else
            {
                emission.enabled = false;
                _rainEmitter.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ConfigureSmokeEmitter(ParticleSystem ps, Vector3 worldPos, bool enabled)
        {
            if (ps == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = ps.emission;
            if (enabled)
            {
                ps.transform.position = worldPos;
                emission.enabled = true;
                if (!ps.isPlaying)
                {
                    ps.Play();
                }
            }
            else
            {
                emission.enabled = false;
                if (ps.isPlaying || ps.particleCount > 0)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private ParticleSystem CreateSmokeEmitter(int index)
        {
            GameObject go = new GameObject("ChimneySmoke_" + index);
            go.transform.SetParent(transform, false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 32;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 2.8f;
            main.startSpeed = 0.05f;
            main.startSize = 0.45f;
            main.startColor = new Color(0.863f, 0.843f, 0.808f, 0.68f);
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            emission.rateOverTime = 5f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.14f;

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.86f * 0.42f * _windScale, 0.86f * 0.62f * _windScale);
            vel.y = new ParticleSystem.MinMaxCurve(0.65f, 0.92f);
            vel.z = new ParticleSystem.MinMaxCurve(0.51f * 0.42f * _windScale, 0.51f * 0.62f * _windScale);

            ParticleSystem.SizeOverLifetimeModule sizeLife = ps.sizeOverLifetime;
            sizeLife.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.333f),
                new Keyframe(1f, 1.0f));
            sizeLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystem.ColorOverLifetimeModule colorLife = ps.colorOverLifetime;
            colorLife.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.863f, 0.843f, 0.808f), 0f),
                    new GradientColorKey(new Color(0.863f, 0.843f, 0.808f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.75f, 0.18f),
                    new GradientAlphaKey(0.45f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorLife.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = _smokeMaterial;

            return ps;
        }

        private ParticleSystem CreateEmberEmitter(int index)
        {
            GameObject go = new GameObject("LightEmbers_" + index);
            go.transform.SetParent(transform, false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 36;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.788f, 0.322f, 0.95f),
                new Color(0.890f, 0.322f, 0.349f, 0.90f));
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            emission.rateOverTime = 10f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.22f;

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.14f, 0.14f);
            vel.y = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.14f, 0.14f);

            ParticleSystem.ColorOverLifetimeModule colorLife = ps.colorOverLifetime;
            colorLife.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.788f, 0.322f), 0f),
                    new GradientColorKey(new Color(0.96f, 0.56f, 0.28f), 0.55f),
                    new GradientColorKey(new Color(0.890f, 0.322f, 0.349f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.85f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorLife.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = _emberMaterial;

            return ps;
        }

        private ParticleSystem CreateRainEmitter()
        {
            GameObject go = new GameObject("PixelRain");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 7.5f, 0f);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = RainMaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.78f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.28f);
            main.startColor = new Color(0.76f, 0.88f, 1.0f, 0.62f);
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            emission.rateOverTime = 200f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(22f, 1.2f, 20f);

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = 1.6f * _windScale;
            vel.y = -12.5f;
            vel.z = 0.9f * _windScale;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.045f;
            renderer.lengthScale = 1.6f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = _rainMaterial;

            return ps;
        }

        private void EnsureMaterials()
        {
            if (_smokePixelTexture == null)
            {
                _smokePixelTexture = CreatePixelQuadTexture(8, softEdge: true);
            }

            if (_smokeMaterial == null)
            {
                _smokeMaterial = CreateParticleMaterial("RuntimeSmokeVfx_Mat", _smokePixelTexture, additive: false);
            }

            if (_emberPixelTexture == null)
            {
                _emberPixelTexture = CreatePixelQuadTexture(4, softEdge: false);
            }

            if (_emberMaterial == null)
            {
                _emberMaterial = CreateParticleMaterial("RuntimeEmberVfx_Mat", _emberPixelTexture, additive: true);
            }

            if (_rainStreakTexture == null)
            {
                _rainStreakTexture = CreateRainStreakTexture();
            }

            if (_rainMaterial == null)
            {
                _rainMaterial = CreateParticleMaterial("RuntimeRainVfx_Mat", _rainStreakTexture, additive: false);
            }
        }

        private static Material CreateParticleMaterial(string matName, Texture2D tex, bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material mat = new Material(shader)
            {
                name = matName,
                hideFlags = HideFlags.HideAndDontSave
            };

            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", tex);
                }
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
                mat.SetFloat("_Blend", additive ? 2f : 0f);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            return mat;
        }

        private static Texture2D CreatePixelQuadTexture(int size, bool softEdge)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            float invHalf = 1f / Mathf.Max(1f, half);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (!softEdge)
                    {
                        bool border = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                        byte a = border ? (byte)170 : (byte)255;
                        pixels[y * size + x] = new Color32(255, 255, 255, a);
                    }
                    else
                    {
                        float dx = (x - half) * invHalf;
                        float dy = (y - half) * invHalf;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float alpha = r <= 0.65f ? 1f : (r <= 1.05f ? 0.6f : 0f);
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D CreateRainStreakTexture()
        {
            const int w = 4;
            const int h = 16;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float ny = (y + 0.5f) / h;
                byte a = (byte)Mathf.RoundToInt(Mathf.Lerp(90f, 235f, ny));
                for (int x = 0; x < w; x++)
                {
                    bool core = x == 1 || x == 2;
                    pixels[y * w + x] = core ? new Color32(255, 255, 255, a) : new Color32(255, 255, 255, 0);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static bool IsChimneyBuilding(CartographyPropKind kind)
        {
            return kind == CartographyPropKind.BywaterInn ||
                   kind == CartographyPropKind.Blacksmith ||
                   kind == CartographyPropKind.CottageB ||
                   kind == CartographyPropKind.CottageC;
        }

        private static Vector3 ResolveChimneyWorldPosition(CartographyPropPlacement prop)
        {
            float sx = Mathf.Max(1f, prop.SizeMeters.x);
            float sy = Mathf.Max(1f, prop.SizeMeters.y);
            return new Vector3(
                prop.WorldPosition.x + sx * 0.22f,
                prop.WorldPosition.y + sy * 0.82f,
                prop.WorldPosition.z);
        }

        private static bool TryGetEmberHeightOffset(CartographyPropKind kind, out float offset)
        {
            switch (kind)
            {
                case CartographyPropKind.Campfire4f:
                    offset = 0.35f;
                    return true;
                case CartographyPropKind.TorchBrazier:
                    offset = 0.85f;
                    return true;
                case CartographyPropKind.DungeonWallSconce4f:
                    offset = 1.05f;
                    return true;
                case CartographyPropKind.TownLanternPost4f:
                    offset = 1.35f;
                    return true;
                default:
                    offset = 0f;
                    return false;
            }
        }

        private static void DestroyResource(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
