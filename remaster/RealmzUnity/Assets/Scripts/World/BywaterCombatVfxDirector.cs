using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.World
{
    /// <summary>
    /// Pooled, zero-per-cast-allocation combat VFX director for arrows, fireballs,
    /// chain lightning, frost shatters, and keg explosions. Safe in EditMode and -batchmode.
    /// </summary>
    public sealed class BywaterCombatVfxDirector : MonoBehaviour
    {
        private const int MaxLinePool = 12;
        private const int MaxQuadPool = 64;
        private const int MaxLightPool = 4;
        private const int MaxActiveEffects = 12;
        private const int MaxHopsPerEffect = 16;
        private const int MaxQuadsPerEffect = 16;
        private const int LightningSubdivisionsPerHop = 5;
        private const int ArrowTrailSamples = 6;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private static readonly Color ArrowTint = new Color(0.98f, 0.90f, 0.62f, 1.0f);
        private static readonly Color DustTint = new Color(0.76f, 0.72f, 0.64f, 0.65f);
        private static readonly Color FireCoreTint = new Color(1.00f, 0.72f, 0.22f, 0.96f);
        private static readonly Color FireFlameTint = new Color(1.00f, 0.42f, 0.12f, 0.92f);
        private static readonly Color FireSmokeTint = new Color(0.24f, 0.22f, 0.24f, 0.72f);
        private static readonly Color LightningTint = new Color(0.490f, 0.976f, 1.000f, 1.0f); // #7DF9FF
        private static readonly Color FrostShardTint = new Color(0.62f, 0.94f, 1.00f, 0.96f);
        private static readonly Color FrostMistTint = new Color(0.84f, 0.97f, 1.00f, 0.56f);
        private static readonly Color KegGoldTint = new Color(1.00f, 0.82f, 0.26f, 0.98f);
        private static readonly Color WarmFlashColor = new Color(1.00f, 0.58f, 0.20f, 1.0f);
        private static readonly Color MeleeSparkGoldTint = new Color(1.00f, 0.84f, 0.30f, 1.0f);
        private static readonly Color MeleeSparkWhiteTint = new Color(1.00f, 0.98f, 0.88f, 1.0f);
        private static readonly Color HealEmeraldTint = new Color(0.28f, 0.96f, 0.56f, 0.95f);
        private static readonly Color HealGoldTint = new Color(1.00f, 0.88f, 0.38f, 0.95f);
        private static readonly Color HealRingTint = new Color(0.36f, 0.98f, 0.62f, 0.85f);

        private enum VfxKind
        {
            None = 0,
            ArrowArc = 1,
            DustPuff = 2,
            FireballFlight = 3,
            FireballBlast = 4,
            ChainLightning = 5,
            FrostShatter = 6,
            KegExplosion = 7,
            MeleeHitSpark = 8,
            HealBurst = 9
        }

        private sealed class PooledLineSlot
        {
            public LineRenderer Line;
            public bool InUse;
        }

        private sealed class PooledQuadSlot
        {
            public GameObject Root;
            public MeshRenderer Renderer;
            public bool InUse;
        }

        private sealed class PooledLightSlot
        {
            public Light PointLight;
            public bool InUse;
        }

        private sealed class ActiveVfxEffect
        {
            public bool Active;
            public VfxKind Kind;
            public float Elapsed;
            public float Duration;
            public float LightDuration;
            public Vector3 StartPos;
            public Vector3 TargetPos;
            public float ArcHeight;
            public float RadiusMeters;
            public bool SpawnedImpact;
            public int HopCount;
            public readonly Vector3[] Hops = new Vector3[MaxHopsPerEffect];
            public readonly Vector3[] LinePointsScratch = new Vector3[(MaxHopsPerEffect - 1) * LightningSubdivisionsPerHop + 1];
            public int LineIndex = -1;
            public int LightIndex = -1;
            public int QuadCount;
            public readonly int[] QuadIndices = new int[MaxQuadsPerEffect];
            public readonly Vector3[] QuadStartPos = new Vector3[MaxQuadsPerEffect];
            public readonly Vector3[] QuadVelocities = new Vector3[MaxQuadsPerEffect];
            public readonly float[] QuadBaseScales = new float[MaxQuadsPerEffect];
            public readonly float[] QuadSpinAngles = new float[MaxQuadsPerEffect];
            public readonly bool[] QuadIsSmoke = new bool[MaxQuadsPerEffect];
            public readonly bool[] QuadIsAlt = new bool[MaxQuadsPerEffect];
            public bool IsCrit;
            public int Seed;
        }

        private Transform _rootContainer;
        private bool _initialized;
        private int _nextSeed = 101;
        private int _effectCursor;

        private readonly List<PooledLineSlot> _linePool = new List<PooledLineSlot>(MaxLinePool);
        private readonly List<PooledQuadSlot> _quadPool = new List<PooledQuadSlot>(MaxQuadPool);
        private readonly List<PooledLightSlot> _lightPool = new List<PooledLightSlot>(MaxLightPool);
        private readonly ActiveVfxEffect[] _activeEffects = new ActiveVfxEffect[MaxActiveEffects];

        private MaterialPropertyBlock _propBlock;
        private Material _additiveGlowMat;
        private Material _additiveStreakMat;
        private Material _additiveRingMat;
        private Material _additiveShardMat;
        private Material _additiveLightningMat;
        private Material _alphaSmokeMat;

        private Texture2D _glowTex;
        private Texture2D _streakTex;
        private Texture2D _ringTex;
        private Texture2D _shardTex;
        private Texture2D _lightningTex;
        private Texture2D _smokeTex;

        public bool IsAnyVfxActive
        {
            get
            {
                for (int i = 0; i < _activeEffects.Length; i++)
                {
                    if (_activeEffects[i] != null && _activeEffects[i].Active)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            if (!Application.isPlaying || !_initialized)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                return;
            }

            StepActiveEffects(dt);
        }

        private void OnDestroy()
        {
            ClearAll();

            DestroySafe(_additiveGlowMat);
            _additiveGlowMat = null;
            DestroySafe(_additiveStreakMat);
            _additiveStreakMat = null;
            DestroySafe(_additiveRingMat);
            _additiveRingMat = null;
            DestroySafe(_additiveShardMat);
            _additiveShardMat = null;
            DestroySafe(_additiveLightningMat);
            _additiveLightningMat = null;
            DestroySafe(_alphaSmokeMat);
            _alphaSmokeMat = null;

            DestroySafe(_glowTex);
            _glowTex = null;
            DestroySafe(_streakTex);
            _streakTex = null;
            DestroySafe(_ringTex);
            _ringTex = null;
            DestroySafe(_shardTex);
            _shardTex = null;
            DestroySafe(_lightningTex);
            _lightningTex = null;
            DestroySafe(_smokeTex);
            _smokeTex = null;
        }

        public void PlayArrowArc(Vector3 startWorldPos, Vector3 targetWorldPos, float duration = 0f)
        {
            EnsureInitialized();

            Vector3 start = startWorldPos + new Vector3(0f, 0.55f, 0f);
            Vector3 target = targetWorldPos + new Vector3(0f, 0.55f, 0f);
            float dist = Vector3.Distance(start, target);
            float resolvedDuration = duration > 0f
                ? duration
                : Mathf.Clamp(0.18f + dist * 0.015f, 0.18f, 0.28f);
            float arcHeight = Mathf.Max(0.14f, dist * 0.16f);

            ActiveVfxEffect fx = AcquireEffectSlot();
            fx.Active = true;
            fx.Kind = VfxKind.ArrowArc;
            fx.Elapsed = 0f;
            fx.Duration = resolvedDuration;
            fx.StartPos = start;
            fx.TargetPos = target;
            fx.ArcHeight = arcHeight;
            fx.SpawnedImpact = false;
            fx.LineIndex = AcquireLine(_additiveStreakMat, 0.075f, 0.015f, ArrowTint, ArrowTint);
            fx.QuadCount = 1;
            fx.QuadIndices[0] = AcquireQuad(_additiveStreakMat, ArrowTint, new Vector3(0.46f, 0.18f, 1f));

            UpdateArrowArcVisual(fx);
        }

        public void PlayFireball(Vector3 startWorldPos, Vector3 targetWorldPos, float duration = 0.32f, float blastRadiusMeters = 1.6f)
        {
            EnsureInitialized();

            Vector3 start = startWorldPos + new Vector3(0f, 0.65f, 0f);
            Vector3 target = targetWorldPos + new Vector3(0f, 0.50f, 0f);
            float dist = Vector3.Distance(start, target);
            float resolvedDuration = Mathf.Max(0.08f, duration);

            ActiveVfxEffect fx = AcquireEffectSlot();
            fx.Active = true;
            fx.Kind = VfxKind.FireballFlight;
            fx.Elapsed = 0f;
            fx.Duration = resolvedDuration;
            fx.StartPos = start;
            fx.TargetPos = target;
            fx.ArcHeight = Mathf.Max(0.22f, dist * 0.14f);
            fx.RadiusMeters = Mathf.Max(0.8f, blastRadiusMeters);
            fx.SpawnedImpact = false;
            fx.LineIndex = AcquireLine(_additiveGlowMat, 0.16f, 0.04f, FireCoreTint, FireFlameTint);
            fx.QuadCount = 2;
            fx.QuadIndices[0] = AcquireQuad(_additiveGlowMat, FireCoreTint, new Vector3(0.58f, 0.58f, 1f));
            fx.QuadIndices[1] = AcquireQuad(_additiveGlowMat, FireFlameTint, new Vector3(0.82f, 0.82f, 1f));

            UpdateFireballFlightVisual(fx);
        }

        public void PlayChainLightning(IReadOnlyList<Vector3> worldHops)
        {
            EnsureInitialized();
            if (worldHops == null || worldHops.Count < 2)
            {
                return;
            }

            int hopCount = Mathf.Min(worldHops.Count, MaxHopsPerEffect);
            ActiveVfxEffect fx = AcquireEffectSlot();
            fx.Active = true;
            fx.Kind = VfxKind.ChainLightning;
            fx.Elapsed = 0f;
            fx.HopCount = hopCount;
            fx.Duration = (hopCount - 1) * 0.06f + 0.18f;
            fx.Seed = ++_nextSeed;

            for (int i = 0; i < hopCount; i++)
            {
                fx.Hops[i] = worldHops[i] + new Vector3(0f, 0.65f, 0f);
            }

            fx.LineIndex = AcquireLine(_additiveLightningMat, 0.14f, 0.09f, Color.white, LightningTint);
            int sparkCount = Mathf.Min(hopCount, MaxQuadsPerEffect);
            fx.QuadCount = sparkCount;
            for (int i = 0; i < sparkCount; i++)
            {
                fx.QuadIndices[i] = AcquireQuad(_additiveGlowMat, LightningTint, new Vector3(0.55f, 0.55f, 1f));
                fx.QuadStartPos[i] = fx.Hops[i];
            }

            UpdateChainLightningVisual(fx);
        }

        public void PlayFrostShatter(Vector3 worldPos)
        {
            EnsureInitialized();

            Vector3 center = worldPos + new Vector3(0f, 0.55f, 0f);
            ActiveVfxEffect fx = AcquireEffectSlot();
            fx.Active = true;
            fx.Kind = VfxKind.FrostShatter;
            fx.Elapsed = 0f;
            fx.Duration = 0.38f;
            fx.StartPos = center;
            fx.TargetPos = center;
            fx.Seed = ++_nextSeed;

            const int shardCount = 10;
            const int mistCount = 2;
            fx.QuadCount = shardCount + mistCount;

            for (int i = 0; i < shardCount; i++)
            {
                float angle = (i / (float)shardCount) * Mathf.PI * 2f + ((fx.Seed % 7) * 0.15f);
                float speed = 2.1f + ((i * 37) % 10) * 0.16f;
                float up = 1.6f + ((i * 19) % 7) * 0.25f;
                fx.QuadIndices[i] = AcquireQuad(_additiveShardMat, FrostShardTint, new Vector3(0.26f, 0.26f, 1f));
                fx.QuadStartPos[i] = center;
                fx.QuadVelocities[i] = new Vector3(Mathf.Cos(angle) * speed, up, Mathf.Sin(angle) * speed);
                fx.QuadBaseScales[i] = 0.22f + ((i % 3) * 0.06f);
                fx.QuadSpinAngles[i] = (i % 2 == 0 ? 1f : -1f) * (240f + i * 25f);
                fx.QuadIsSmoke[i] = false;
            }

            for (int m = 0; m < mistCount; m++)
            {
                int idx = shardCount + m;
                fx.QuadIndices[idx] = AcquireQuad(_alphaSmokeMat, FrostMistTint, new Vector3(0.95f, 0.95f, 1f));
                fx.QuadStartPos[idx] = center + new Vector3((m == 0 ? -0.18f : 0.18f), -0.15f, 0f);
                fx.QuadVelocities[idx] = new Vector3(m == 0 ? -0.35f : 0.35f, 0.45f, 0f);
                fx.QuadBaseScales[idx] = 0.95f + m * 0.25f;
                fx.QuadSpinAngles[idx] = 0f;
                fx.QuadIsSmoke[idx] = true;
            }

            UpdateFrostShatterVisual(fx);
        }

        public void PlayKegExplosion(Vector3 worldPos, float radiusMeters = 1.8f)
        {
            EnsureInitialized();
            SpawnExplosionInternal(
                VfxKind.KegExplosion,
                worldPos + new Vector3(0f, 0.35f, 0f),
                Mathf.Max(1.0f, radiusMeters),
                KegGoldTint,
                FireFlameTint);
        }

        public void PlayMeleeHitSpark(Vector3 targetWorldPos, bool isCrit = false)
        {
            EnsureInitialized();

            Vector3 center = targetWorldPos + new Vector3(0f, 0.55f, 0f);
            ActiveVfxEffect fx = AcquireEffectSlot();
            fx.Active = true;
            fx.Kind = VfxKind.MeleeHitSpark;
            fx.Elapsed = 0f;
            fx.Duration = isCrit ? 0.24f : 0.20f;
            fx.LightDuration = 0.18f;
            fx.StartPos = center;
            fx.TargetPos = center;
            fx.IsCrit = isCrit;
            fx.Seed = ++_nextSeed;
            fx.LightIndex = isCrit ? AcquireLight(center + new Vector3(0f, 0.35f, 0f), 4.5f) : -1;

            int sparkCount = isCrit ? 8 : 5;
            fx.QuadCount = sparkCount;

            for (int i = 0; i < sparkCount; i++)
            {
                float angle = (i / (float)sparkCount) * Mathf.PI * 2f + ((fx.Seed % 7) * 0.19f);
                float elev = ((i % 2 == 0) ? 0.45f : 0.15f) + ((i % 3) * 0.18f);
                float speed = (isCrit ? 3.2f : 2.4f) + ((i * 19) % 5) * 0.25f;
                Vector3 dir = new Vector3(Mathf.Cos(angle), elev, Mathf.Sin(angle)).normalized;
                bool useGlow = (i % 3) == 1;
                bool isGold = (i % 2) == 0;
                Material mat = useGlow ? _additiveGlowMat : _additiveStreakMat;
                Color col = isGold ? MeleeSparkGoldTint : MeleeSparkWhiteTint;

                fx.QuadIndices[i] = AcquireQuad(mat, col, new Vector3(0.32f, 0.14f, 1f));
                fx.QuadStartPos[i] = center + dir * 0.06f;
                fx.QuadVelocities[i] = dir * speed;
                fx.QuadBaseScales[i] = (isCrit ? 0.36f : 0.28f) + ((i % 2) * 0.06f);
                fx.QuadIsAlt[i] = useGlow;
                fx.QuadIsSmoke[i] = isGold;
            }

            UpdateMeleeHitSparkVisual(fx);
        }

        public void PlayHealBurst(Vector3 targetWorldPos)
        {
            EnsureInitialized();

            ActiveVfxEffect fx = AcquireEffectSlot();
            fx.Active = true;
            fx.Kind = VfxKind.HealBurst;
            fx.Elapsed = 0f;
            fx.Duration = 0.42f;
            fx.StartPos = targetWorldPos;
            fx.TargetPos = targetWorldPos;
            fx.Seed = ++_nextSeed;

            const int moteCount = 6;
            fx.QuadCount = 1 + moteCount;

            fx.QuadIndices[0] = AcquireQuad(_additiveRingMat, HealRingTint, new Vector3(0.40f, 0.40f, 1f));
            fx.QuadStartPos[0] = new Vector3(targetWorldPos.x, targetWorldPos.y + 0.08f, targetWorldPos.z);
            fx.QuadBaseScales[0] = 1.45f;
            fx.QuadIsAlt[0] = true;
            fx.QuadIsSmoke[0] = true;

            for (int i = 1; i <= moteCount; i++)
            {
                float angle = ((i - 1) / (float)moteCount) * Mathf.PI * 2f + ((fx.Seed % 5) * 0.22f);
                float radial = 0.24f + ((i % 2) * 0.14f);
                bool isEmerald = (i % 2) == 1;
                Color c = isEmerald ? HealEmeraldTint : HealGoldTint;

                fx.QuadIndices[i] = AcquireQuad(_additiveGlowMat, c, new Vector3(0.24f, 0.24f, 1f));
                fx.QuadStartPos[i] = targetWorldPos + new Vector3(
                    Mathf.Cos(angle) * radial,
                    0.12f + (i % 3) * 0.08f,
                    Mathf.Sin(angle) * radial);
                fx.QuadVelocities[i] = new Vector3(
                    Mathf.Cos(angle) * 0.22f,
                    1.45f + (i % 3) * 0.25f,
                    Mathf.Sin(angle) * 0.22f);
                fx.QuadBaseScales[i] = 0.22f + (i % 2) * 0.07f;
                fx.QuadIsAlt[i] = false;
                fx.QuadIsSmoke[i] = isEmerald;
            }

            UpdateHealBurstVisual(fx);
        }

        public void ClearAll()
        {
            for (int i = 0; i < _activeEffects.Length; i++)
            {
                if (_activeEffects[i] != null)
                {
                    ReleaseEffectResources(_activeEffects[i]);
                    _activeEffects[i].Active = false;
                }
            }

            for (int i = 0; i < _linePool.Count; i++)
            {
                if (_linePool[i] != null)
                {
                    _linePool[i].InUse = false;
                    if (_linePool[i].Line != null)
                    {
                        _linePool[i].Line.positionCount = 0;
                        _linePool[i].Line.gameObject.SetActive(false);
                    }
                }
            }

            for (int i = 0; i < _quadPool.Count; i++)
            {
                if (_quadPool[i] != null)
                {
                    _quadPool[i].InUse = false;
                    if (_quadPool[i].Root != null)
                    {
                        _quadPool[i].Root.SetActive(false);
                    }
                }
            }

            for (int i = 0; i < _lightPool.Count; i++)
            {
                if (_lightPool[i] != null)
                {
                    _lightPool[i].InUse = false;
                    if (_lightPool[i].PointLight != null)
                    {
                        _lightPool[i].PointLight.intensity = 0f;
                        _lightPool[i].PointLight.gameObject.SetActive(false);
                    }
                }
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _propBlock ??= new MaterialPropertyBlock();

            if (_rootContainer == null)
            {
                GameObject root = new GameObject("CombatVfxDirectorPool");
                root.transform.SetParent(transform, false);
                _rootContainer = root.transform;
            }

            for (int i = 0; i < _activeEffects.Length; i++)
            {
                _activeEffects[i] = new ActiveVfxEffect();
            }

            BuildTexturesAndMaterials();
            PrewarmPools();
        }

        private void PrewarmPools()
        {
            for (int i = _linePool.Count; i < MaxLinePool; i++)
            {
                GameObject go = new GameObject("VfxLine_" + i);
                go.transform.SetParent(_rootContainer, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.alignment = LineAlignment.View;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 2;
                lr.positionCount = 0;
                go.SetActive(false);
                _linePool.Add(new PooledLineSlot { Line = lr, InUse = false });
            }

            for (int i = _quadPool.Count; i < MaxQuadPool; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "VfxQuad_" + i;
                go.transform.SetParent(_rootContainer, false);

                Collider col = go.GetComponent<Collider>();
                if (col != null)
                {
                    DestroySafe(col);
                }

                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
                _quadPool.Add(new PooledQuadSlot { Root = go, Renderer = mr, InUse = false });
            }

            for (int i = _lightPool.Count; i < MaxLightPool; i++)
            {
                GameObject go = new GameObject("VfxFlashLight_" + i);
                go.transform.SetParent(_rootContainer, false);
                Light pt = go.AddComponent<Light>();
                pt.type = LightType.Point;
                pt.shadows = LightShadows.None;
                pt.range = 6.0f;
                pt.color = WarmFlashColor;
                pt.intensity = 0f;
                go.SetActive(false);
                _lightPool.Add(new PooledLightSlot { PointLight = pt, InUse = false });
            }
        }

        private void BuildTexturesAndMaterials()
        {
            _glowTex = BuildGlowTexture();
            _streakTex = BuildStreakTexture();
            _ringTex = BuildRingTexture();
            _shardTex = BuildShardTexture();
            _lightningTex = BuildLightningTexture();
            _smokeTex = BuildSmokeTexture();

            _additiveGlowMat = CreateVfxMaterial("VfxAddGlow", _glowTex, additive: true);
            _additiveStreakMat = CreateVfxMaterial("VfxAddStreak", _streakTex, additive: true);
            _additiveRingMat = CreateVfxMaterial("VfxAddRing", _ringTex, additive: true);
            _additiveShardMat = CreateVfxMaterial("VfxAddShard", _shardTex, additive: true);
            _additiveLightningMat = CreateVfxMaterial("VfxAddLightning", _lightningTex, additive: true);
            _alphaSmokeMat = CreateVfxMaterial("VfxAlphaSmoke", _smokeTex, additive: false);
        }

        private ActiveVfxEffect AcquireEffectSlot()
        {
            for (int i = 0; i < _activeEffects.Length; i++)
            {
                if (!_activeEffects[i].Active)
                {
                    ResetEffect(_activeEffects[i]);
                    return _activeEffects[i];
                }
            }

            ActiveVfxEffect recycled = _activeEffects[_effectCursor % _activeEffects.Length];
            _effectCursor = (_effectCursor + 1) % _activeEffects.Length;
            ReleaseEffectResources(recycled);
            ResetEffect(recycled);
            return recycled;
        }

        private static void ResetEffect(ActiveVfxEffect fx)
        {
            fx.Active = false;
            fx.Kind = VfxKind.None;
            fx.Elapsed = 0f;
            fx.Duration = 0.25f;
            fx.LightDuration = 0.25f;
            fx.StartPos = Vector3.zero;
            fx.TargetPos = Vector3.zero;
            fx.ArcHeight = 0f;
            fx.RadiusMeters = 1f;
            fx.SpawnedImpact = false;
            fx.HopCount = 0;
            fx.LineIndex = -1;
            fx.LightIndex = -1;
            fx.QuadCount = 0;
            fx.IsCrit = false;
            for (int i = 0; i < MaxQuadsPerEffect; i++)
            {
                fx.QuadIndices[i] = -1;
                fx.QuadIsSmoke[i] = false;
                fx.QuadIsAlt[i] = false;
            }
        }

        private void ReleaseEffectResources(ActiveVfxEffect fx)
        {
            if (fx.LineIndex >= 0 && fx.LineIndex < _linePool.Count)
            {
                PooledLineSlot slot = _linePool[fx.LineIndex];
                slot.InUse = false;
                if (slot.Line != null)
                {
                    slot.Line.positionCount = 0;
                    slot.Line.gameObject.SetActive(false);
                }
            }
            fx.LineIndex = -1;

            if (fx.LightIndex >= 0 && fx.LightIndex < _lightPool.Count)
            {
                PooledLightSlot lSlot = _lightPool[fx.LightIndex];
                lSlot.InUse = false;
                if (lSlot.PointLight != null)
                {
                    lSlot.PointLight.intensity = 0f;
                    lSlot.PointLight.gameObject.SetActive(false);
                }
            }
            fx.LightIndex = -1;

            for (int i = 0; i < fx.QuadCount && i < MaxQuadsPerEffect; i++)
            {
                int qIdx = fx.QuadIndices[i];
                if (qIdx >= 0 && qIdx < _quadPool.Count)
                {
                    PooledQuadSlot qSlot = _quadPool[qIdx];
                    qSlot.InUse = false;
                    if (qSlot.Root != null)
                    {
                        qSlot.Root.SetActive(false);
                    }
                }
                fx.QuadIndices[i] = -1;
            }
            fx.QuadCount = 0;
        }

        private int AcquireLine(Material mat, float startWidth, float endWidth, Color startCol, Color endCol)
        {
            for (int i = 0; i < _linePool.Count; i++)
            {
                if (!_linePool[i].InUse)
                {
                    PooledLineSlot slot = _linePool[i];
                    slot.InUse = true;
                    slot.Line.sharedMaterial = mat;
                    slot.Line.startWidth = startWidth;
                    slot.Line.endWidth = endWidth;
                    slot.Line.startColor = startCol;
                    slot.Line.endColor = endCol;
                    slot.Line.textureMode = LineTextureMode.Stretch;
                    slot.Line.gameObject.SetActive(true);
                    return i;
                }
            }
            return -1;
        }

        private int AcquireQuad(Material mat, Color tint, Vector3 scale)
        {
            for (int i = 0; i < _quadPool.Count; i++)
            {
                if (!_quadPool[i].InUse)
                {
                    PooledQuadSlot slot = _quadPool[i];
                    slot.InUse = true;
                    if (mat != null)
                    {
                        slot.Renderer.sharedMaterial = mat;
                    }
                    slot.Root.transform.localScale = scale;
                    SetQuadTint(slot.Renderer, tint);
                    slot.Root.SetActive(true);
                    return i;
                }
            }
            return -1;
        }

        private int AcquireLight(Vector3 pos, float range)
        {
            for (int i = 0; i < _lightPool.Count; i++)
            {
                if (!_lightPool[i].InUse)
                {
                    PooledLightSlot slot = _lightPool[i];
                    slot.InUse = true;
                    slot.PointLight.transform.position = pos;
                    slot.PointLight.range = range;
                    slot.PointLight.color = WarmFlashColor;
                    slot.PointLight.intensity = 8.0f;
                    slot.PointLight.gameObject.SetActive(true);
                    return i;
                }
            }
            return -1;
        }

        private void StepActiveEffects(float dt)
        {
            for (int i = 0; i < _activeEffects.Length; i++)
            {
                ActiveVfxEffect fx = _activeEffects[i];
                if (fx == null || !fx.Active)
                {
                    continue;
                }

                fx.Elapsed += dt;
                switch (fx.Kind)
                {
                    case VfxKind.ArrowArc:
                        if (fx.Elapsed >= fx.Duration)
                        {
                            Vector3 landingPos = fx.TargetPos;
                            ReleaseEffectResources(fx);
                            fx.Active = false;
                            SpawnDustPuff(landingPos);
                        }
                        else
                        {
                            UpdateArrowArcVisual(fx);
                        }
                        break;

                    case VfxKind.DustPuff:
                        if (fx.Elapsed >= fx.Duration)
                        {
                            ReleaseEffectResources(fx);
                            fx.Active = false;
                        }
                        else
                        {
                            UpdateDustPuffVisual(fx);
                        }
                        break;

                    case VfxKind.FireballFlight:
                        if (fx.Elapsed >= fx.Duration)
                        {
                            Vector3 blastPos = fx.TargetPos;
                            float radius = fx.RadiusMeters;
                            ReleaseEffectResources(fx);
                            fx.Active = false;
                            SpawnExplosionInternal(VfxKind.FireballBlast, blastPos, radius, FireCoreTint, FireFlameTint);
                        }
                        else
                        {
                            UpdateFireballFlightVisual(fx);
                        }
                        break;

                    case VfxKind.FireballBlast:
                    case VfxKind.KegExplosion:
                        if (fx.Elapsed >= fx.Duration)
                        {
                            ReleaseEffectResources(fx);
                            fx.Active = false;
                        }
                        else
                        {
                            UpdateExplosionVisual(fx);
                        }
                        break;

                    case VfxKind.ChainLightning:
                        if (fx.Elapsed >= fx.Duration)
                        {
                            ReleaseEffectResources(fx);
                            fx.Active = false;
                        }
                        else
                        {
                            UpdateChainLightningVisual(fx);
                        }
                        break;

                    case VfxKind.FrostShatter:
                        if (fx.Elapsed >= fx.Duration)
                        {
                            ReleaseEffectResources(fx);
                            fx.Active = false;
                        }
                        else
                        {
                            UpdateFrostShatterVisual(fx);
                        }
                        break;

                    case VfxKind.MeleeHitSpark:
                        if (fx.Elapsed >= fx.Duration)
                        {
                            ReleaseEffectResources(fx);
                            fx.Active = false;
                        }
                        else
                        {
                            UpdateMeleeHitSparkVisual(fx);
                        }
                        break;

                    case VfxKind.HealBurst:
                        if (fx.Elapsed >= fx.Duration)
                        {
                            ReleaseEffectResources(fx);
                            fx.Active = false;
                        }
                        else
                        {
                            UpdateHealBurstVisual(fx);
                        }
                        break;
                }
            }
        }

        private void UpdateArrowArcVisual(ActiveVfxEffect fx)
        {
            float t = Mathf.Clamp01(fx.Elapsed / Mathf.Max(0.01f, fx.Duration));
            Vector3 headPos = EvalParabola(fx.StartPos, fx.TargetPos, fx.ArcHeight, t);
            Vector3 nextPos = EvalParabola(fx.StartPos, fx.TargetPos, fx.ArcHeight, Mathf.Min(1f, t + 0.04f));
            Vector3 prevPos = EvalParabola(fx.StartPos, fx.TargetPos, fx.ArcHeight, Mathf.Max(0f, t - 0.04f));
            Vector3 tangent = (nextPos - prevPos);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = fx.TargetPos - fx.StartPos;
            }
            tangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;

            if (fx.QuadCount > 0 && fx.QuadIndices[0] >= 0)
            {
                PooledQuadSlot head = _quadPool[fx.QuadIndices[0]];
                head.Root.transform.position = headPos;
                OrientStreakQuad(head.Root.transform, headPos, tangent);
                SetQuadTint(head.Renderer, ArrowTint);
            }

            if (fx.LineIndex >= 0)
            {
                PooledLineSlot line = _linePool[fx.LineIndex];
                float trailSpan = 0.28f;
                float trailStartT = Mathf.Max(0f, t - trailSpan);
                for (int s = 0; s < ArrowTrailSamples; s++)
                {
                    float u = s / (float)(ArrowTrailSamples - 1);
                    float sampleT = Mathf.Lerp(trailStartT, t, u);
                    fx.LinePointsScratch[s] = EvalParabola(fx.StartPos, fx.TargetPos, fx.ArcHeight, sampleT);
                }
                line.Line.positionCount = ArrowTrailSamples;
                line.Line.SetPositions(fx.LinePointsScratch);
            }
        }

        private void SpawnDustPuff(Vector3 worldPos)
        {
            ActiveVfxEffect fx = AcquireEffectSlot();
            fx.Active = true;
            fx.Kind = VfxKind.DustPuff;
            fx.Elapsed = 0f;
            fx.Duration = 0.22f;
            fx.StartPos = worldPos;
            fx.QuadCount = 3;
            for (int i = 0; i < 3; i++)
            {
                float angle = i * (Mathf.PI * 2f / 3f) + 0.4f;
                fx.QuadIndices[i] = AcquireQuad(_alphaSmokeMat, DustTint, new Vector3(0.32f, 0.32f, 1f));
                fx.QuadStartPos[i] = worldPos + new Vector3(Mathf.Cos(angle) * 0.08f, -0.25f, Mathf.Sin(angle) * 0.08f);
                fx.QuadVelocities[i] = new Vector3(Mathf.Cos(angle) * 0.75f, 0.55f, Mathf.Sin(angle) * 0.75f);
                fx.QuadBaseScales[i] = 0.32f;
            }
            UpdateDustPuffVisual(fx);
        }

        private void UpdateDustPuffVisual(ActiveVfxEffect fx)
        {
            float t = Mathf.Clamp01(fx.Elapsed / Mathf.Max(0.01f, fx.Duration));
            Quaternion camRot = GetBillboardRotation();
            Color col = DustTint;
            col.a *= (1f - t);

            for (int i = 0; i < fx.QuadCount; i++)
            {
                int qIdx = fx.QuadIndices[i];
                if (qIdx < 0) continue;
                PooledQuadSlot slot = _quadPool[qIdx];
                slot.Root.transform.position = fx.QuadStartPos[i] + fx.QuadVelocities[i] * fx.Elapsed;
                slot.Root.transform.rotation = camRot;
                float sc = fx.QuadBaseScales[i] * Mathf.Lerp(0.8f, 1.85f, t);
                slot.Root.transform.localScale = new Vector3(sc, sc, 1f);
                SetQuadTint(slot.Renderer, col);
            }
        }

        private void UpdateFireballFlightVisual(ActiveVfxEffect fx)
        {
            float t = Mathf.Clamp01(fx.Elapsed / Mathf.Max(0.01f, fx.Duration));
            Vector3 pos = EvalParabola(fx.StartPos, fx.TargetPos, fx.ArcHeight, t);
            Quaternion camRot = GetBillboardRotation();

            if (fx.QuadCount >= 2)
            {
                if (fx.QuadIndices[0] >= 0)
                {
                    PooledQuadSlot core = _quadPool[fx.QuadIndices[0]];
                    core.Root.transform.position = pos;
                    core.Root.transform.rotation = camRot;
                    float pulse = 0.52f + 0.12f * Mathf.Sin(fx.Elapsed * 28f);
                    core.Root.transform.localScale = new Vector3(pulse, pulse, 1f);
                }
                if (fx.QuadIndices[1] >= 0)
                {
                    PooledQuadSlot halo = _quadPool[fx.QuadIndices[1]];
                    halo.Root.transform.position = pos;
                    halo.Root.transform.rotation = camRot;
                    float hScale = 0.85f + 0.16f * Mathf.Cos(fx.Elapsed * 24f);
                    halo.Root.transform.localScale = new Vector3(hScale, hScale, 1f);
                }
            }

            if (fx.LineIndex >= 0)
            {
                PooledLineSlot line = _linePool[fx.LineIndex];
                float trailStartT = Mathf.Max(0f, t - 0.35f);
                for (int s = 0; s < ArrowTrailSamples; s++)
                {
                    float u = s / (float)(ArrowTrailSamples - 1);
                    fx.LinePointsScratch[s] = EvalParabola(fx.StartPos, fx.TargetPos, fx.ArcHeight, Mathf.Lerp(trailStartT, t, u));
                }
                line.Line.positionCount = ArrowTrailSamples;
                line.Line.SetPositions(fx.LinePointsScratch);
            }
        }

        private void SpawnExplosionInternal(
            VfxKind kind,
            Vector3 center,
            float radiusMeters,
            Color coreColor,
            Color emberColor)
        {
            ActiveVfxEffect fx = AcquireEffectSlot();
            fx.Active = true;
            fx.Kind = kind;
            fx.Elapsed = 0f;
            fx.Duration = kind == VfxKind.KegExplosion ? 0.44f : 0.38f;
            fx.LightDuration = 0.25f;
            fx.StartPos = center;
            fx.TargetPos = center;
            fx.RadiusMeters = radiusMeters;
            fx.Seed = ++_nextSeed;
            fx.LightIndex = AcquireLight(center + new Vector3(0f, 0.8f, 0f), Mathf.Max(5.5f, radiusMeters * 3.5f));

            // Slot 0: Ground-aligned expanding shock/flame ring
            // Slot 1: Central flash disc
            // Slots 2..9: Embers & debris shards
            // Slots 10..13: Smoke puffs
            const int totalQuads = 14;
            fx.QuadCount = totalQuads;
            fx.QuadIndices[0] = AcquireQuad(_additiveRingMat, coreColor, new Vector3(0.5f, 0.5f, 1f));
            fx.QuadStartPos[0] = new Vector3(center.x, center.y - 0.15f, center.z);
            fx.QuadBaseScales[0] = radiusMeters * 2.0f;

            fx.QuadIndices[1] = AcquireQuad(_additiveGlowMat, coreColor, new Vector3(1.1f, 1.1f, 1f));
            fx.QuadStartPos[1] = center + new Vector3(0f, 0.25f, 0f);
            fx.QuadBaseScales[1] = radiusMeters * 1.35f;

            for (int e = 2; e < 10; e++)
            {
                float angle = ((e - 2) / 8f) * Mathf.PI * 2f + ((fx.Seed % 5) * 0.18f);
                float speed = radiusMeters * (1.25f + ((e * 23) % 7) * 0.22f);
                float up = 1.6f + ((e * 17) % 6) * 0.32f;
                Material mat = (kind == VfxKind.KegExplosion && (e % 2 == 0)) ? _additiveShardMat : _additiveGlowMat;
                Color c = (e % 2 == 0) ? coreColor : emberColor;
                fx.QuadIndices[e] = AcquireQuad(mat, c, new Vector3(0.30f, 0.30f, 1f));
                fx.QuadStartPos[e] = center;
                fx.QuadVelocities[e] = new Vector3(Mathf.Cos(angle) * speed, up, Mathf.Sin(angle) * speed);
                fx.QuadBaseScales[e] = 0.26f + (e % 3) * 0.08f;
                fx.QuadSpinAngles[e] = (e % 2 == 0 ? 1f : -1f) * 260f;
                fx.QuadIsSmoke[e] = false;
            }

            for (int s = 10; s < totalQuads; s++)
            {
                float angle = ((s - 10) / 4f) * Mathf.PI * 2f + 0.35f;
                float speed = radiusMeters * 0.65f;
                fx.QuadIndices[s] = AcquireQuad(_alphaSmokeMat, FireSmokeTint, new Vector3(0.85f, 0.85f, 1f));
                fx.QuadStartPos[s] = center + new Vector3(Mathf.Cos(angle) * 0.25f, 0.15f, Mathf.Sin(angle) * 0.25f);
                fx.QuadVelocities[s] = new Vector3(Mathf.Cos(angle) * speed, 1.15f + (s - 10) * 0.18f, Mathf.Sin(angle) * speed);
                fx.QuadBaseScales[s] = 0.90f + (s % 2) * 0.25f;
                fx.QuadIsSmoke[s] = true;
            }

            UpdateExplosionVisual(fx);
        }

        private void UpdateExplosionVisual(ActiveVfxEffect fx)
        {
            float t = Mathf.Clamp01(fx.Elapsed / Mathf.Max(0.01f, fx.Duration));
            float easeOut = 1f - (1f - t) * (1f - t);
            Quaternion camRot = GetBillboardRotation();

            if (fx.LightIndex >= 0)
            {
                PooledLightSlot lSlot = _lightPool[fx.LightIndex];
                if (fx.Elapsed >= fx.LightDuration)
                {
                    lSlot.PointLight.intensity = 0f;
                    lSlot.PointLight.gameObject.SetActive(false);
                }
                else
                {
                    float lt = Mathf.Clamp01(fx.Elapsed / fx.LightDuration);
                    lSlot.PointLight.intensity = Mathf.Lerp(8.0f, 0f, lt);
                }
            }

            if (fx.QuadCount > 0 && fx.QuadIndices[0] >= 0)
            {
                PooledQuadSlot ring = _quadPool[fx.QuadIndices[0]];
                ring.Root.transform.position = fx.QuadStartPos[0];
                ring.Root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                float rScale = Mathf.Lerp(0.35f, fx.QuadBaseScales[0], easeOut);
                ring.Root.transform.localScale = new Vector3(rScale, rScale, 1f);
                Color ringCol = fx.Kind == VfxKind.KegExplosion ? KegGoldTint : FireFlameTint;
                ringCol.a *= (1f - t);
                SetQuadTint(ring.Renderer, ringCol);
            }

            if (fx.QuadCount > 1 && fx.QuadIndices[1] >= 0)
            {
                PooledQuadSlot core = _quadPool[fx.QuadIndices[1]];
                core.Root.transform.position = fx.QuadStartPos[1];
                core.Root.transform.rotation = camRot;
                float cScale = Mathf.Lerp(0.55f, fx.QuadBaseScales[1], easeOut);
                core.Root.transform.localScale = new Vector3(cScale, cScale, 1f);
                Color cCol = FireCoreTint;
                cCol.a *= Mathf.Clamp01(1f - t * 1.25f);
                SetQuadTint(core.Renderer, cCol);
            }

            for (int i = 2; i < fx.QuadCount; i++)
            {
                int qIdx = fx.QuadIndices[i];
                if (qIdx < 0) continue;
                PooledQuadSlot slot = _quadPool[qIdx];
                Vector3 pos = fx.QuadStartPos[i] + fx.QuadVelocities[i] * fx.Elapsed;
                if (!fx.QuadIsSmoke[i])
                {
                    pos.y -= 4.8f * fx.Elapsed * fx.Elapsed;
                }
                slot.Root.transform.position = pos;
                slot.Root.transform.rotation = fx.QuadIsSmoke[i]
                    ? camRot
                    : camRot * Quaternion.Euler(0f, 0f, fx.QuadSpinAngles[i] * fx.Elapsed);
                float sc = fx.QuadIsSmoke[i]
                    ? fx.QuadBaseScales[i] * Mathf.Lerp(0.8f, 1.9f, t)
                    : fx.QuadBaseScales[i] * (1f - t * 0.55f);
                slot.Root.transform.localScale = new Vector3(sc, sc, 1f);
                Color col = fx.QuadIsSmoke[i] ? FireSmokeTint : (fx.Kind == VfxKind.KegExplosion ? KegGoldTint : FireFlameTint);
                col.a *= (1f - t);
                SetQuadTint(slot.Renderer, col);
            }
        }

        private void UpdateChainLightningVisual(ActiveVfxEffect fx)
        {
            if (fx.HopCount < 2 || fx.LineIndex < 0)
            {
                return;
            }

            const float hopDuration = 0.06f;
            float totalHopTime = (fx.HopCount - 1) * hopDuration;
            float reachedHopsFloat = totalHopTime > 0.001f
                ? Mathf.Min(fx.HopCount - 1, fx.Elapsed / hopDuration)
                : fx.HopCount - 1;
            int activeSegments = Mathf.Clamp(Mathf.CeilToInt(reachedHopsFloat + 0.01f), 1, fx.HopCount - 1);

            float fadeAlpha = 1f;
            if (fx.Elapsed > totalHopTime)
            {
                fadeAlpha = Mathf.Clamp01(1f - (fx.Elapsed - totalHopTime) / 0.18f);
            }

            int pointCursor = 0;
            int jitterFrame = Mathf.FloorToInt(fx.Elapsed * 32f);
            for (int seg = 0; seg < activeSegments; seg++)
            {
                Vector3 a = fx.Hops[seg];
                Vector3 b = fx.Hops[seg + 1];
                float segEndT = 1f;
                if (seg == activeSegments - 1 && fx.Elapsed < totalHopTime)
                {
                    segEndT = Mathf.Clamp01((fx.Elapsed - seg * hopDuration) / hopDuration);
                }

                Vector3 segDir = b - a;
                Vector3 side = Vector3.Cross(segDir.sqrMagnitude > 0.0001f ? segDir.normalized : Vector3.forward, Vector3.up);
                if (side.sqrMagnitude < 0.0001f)
                {
                    side = Vector3.right;
                }
                side.Normalize();

                int startSub = seg == 0 ? 0 : 1;
                for (int s = startSub; s <= LightningSubdivisionsPerHop; s++)
                {
                    float frac = (s / (float)LightningSubdivisionsPerHop) * segEndT;
                    Vector3 pt = Vector3.Lerp(a, b, frac);
                    if (s > 0 && s < LightningSubdivisionsPerHop)
                    {
                        int h1 = HashInt(fx.Seed + seg * 31 + s * 7 + jitterFrame * 13);
                        int h2 = HashInt(fx.Seed + seg * 17 + s * 29 + jitterFrame * 19);
                        float jSide = (((h1 & 0xFF) / 255f) - 0.5f) * 0.42f;
                        float jUp = (((h2 & 0xFF) / 255f) - 0.5f) * 0.24f;
                        pt += side * jSide + Vector3.up * jUp;
                    }
                    fx.LinePointsScratch[pointCursor++] = pt;
                }
            }

            PooledLineSlot line = _linePool[fx.LineIndex];
            Color startCol = new Color(1f, 1f, 1f, fadeAlpha);
            Color endCol = new Color(LightningTint.r, LightningTint.g, LightningTint.b, fadeAlpha);
            line.Line.startColor = startCol;
            line.Line.endColor = endCol;
            line.Line.positionCount = pointCursor;
            line.Line.SetPositions(fx.LinePointsScratch);

            Quaternion camRot = GetBillboardRotation();
            Color sparkCol = new Color(LightningTint.r, LightningTint.g, LightningTint.b, fadeAlpha);
            for (int i = 0; i < fx.QuadCount; i++)
            {
                int qIdx = fx.QuadIndices[i];
                if (qIdx < 0) continue;
                PooledQuadSlot spark = _quadPool[qIdx];
                bool reached = i <= activeSegments;
                spark.Root.SetActive(reached);
                if (reached)
                {
                    spark.Root.transform.position = fx.QuadStartPos[i];
                    spark.Root.transform.rotation = camRot;
                    float sc = 0.45f + 0.18f * Mathf.Sin(fx.Elapsed * 42f + i);
                    spark.Root.transform.localScale = new Vector3(sc, sc, 1f);
                    SetQuadTint(spark.Renderer, sparkCol);
                }
            }
        }

        private void UpdateFrostShatterVisual(ActiveVfxEffect fx)
        {
            float t = Mathf.Clamp01(fx.Elapsed / Mathf.Max(0.01f, fx.Duration));
            Quaternion camRot = GetBillboardRotation();

            for (int i = 0; i < fx.QuadCount; i++)
            {
                int qIdx = fx.QuadIndices[i];
                if (qIdx < 0) continue;
                PooledQuadSlot slot = _quadPool[qIdx];
                Vector3 pos = fx.QuadStartPos[i] + fx.QuadVelocities[i] * fx.Elapsed;
                if (!fx.QuadIsSmoke[i])
                {
                    pos.y -= 5.2f * fx.Elapsed * fx.Elapsed;
                    slot.Root.transform.rotation = camRot * Quaternion.Euler(0f, 0f, fx.QuadSpinAngles[i] * fx.Elapsed);
                    float sc = fx.QuadBaseScales[i] * (1f - t * 0.45f);
                    slot.Root.transform.localScale = new Vector3(sc, sc, 1f);
                    Color c = FrostShardTint;
                    c.a *= (1f - t);
                    SetQuadTint(slot.Renderer, c);
                }
                else
                {
                    slot.Root.transform.rotation = camRot;
                    float sc = fx.QuadBaseScales[i] * Mathf.Lerp(0.85f, 1.75f, t);
                    slot.Root.transform.localScale = new Vector3(sc, sc, 1f);
                    Color c = FrostMistTint;
                    c.a *= (1f - t);
                    SetQuadTint(slot.Renderer, c);
                }
                slot.Root.transform.position = pos;
            }
        }

        private void UpdateMeleeHitSparkVisual(ActiveVfxEffect fx)
        {
            float t = Mathf.Clamp01(fx.Elapsed / Mathf.Max(0.01f, fx.Duration));
            Quaternion camRot = GetBillboardRotation();

            if (fx.LightIndex >= 0)
            {
                PooledLightSlot lSlot = _lightPool[fx.LightIndex];
                if (fx.Elapsed >= fx.LightDuration)
                {
                    lSlot.PointLight.intensity = 0f;
                    lSlot.PointLight.gameObject.SetActive(false);
                }
                else
                {
                    float lt = Mathf.Clamp01(fx.Elapsed / Mathf.Max(0.01f, fx.LightDuration));
                    lSlot.PointLight.intensity = Mathf.Lerp(7.5f, 0f, lt);
                }
            }

            for (int i = 0; i < fx.QuadCount; i++)
            {
                int qIdx = fx.QuadIndices[i];
                if (qIdx < 0) continue;
                PooledQuadSlot slot = _quadPool[qIdx];
                Vector3 pos = fx.QuadStartPos[i] + fx.QuadVelocities[i] * fx.Elapsed;
                pos.y -= 2.4f * fx.Elapsed * fx.Elapsed;

                if (fx.QuadIsAlt[i])
                {
                    slot.Root.transform.position = pos;
                    slot.Root.transform.rotation = camRot;
                    float sc = fx.QuadBaseScales[i] * Mathf.Lerp(1.1f, 0.35f, t);
                    slot.Root.transform.localScale = new Vector3(sc, sc, 1f);
                }
                else
                {
                    Vector3 vel = fx.QuadVelocities[i];
                    vel.y -= 4.8f * fx.Elapsed;
                    Vector3 tangent = vel.sqrMagnitude > 0.0001f ? vel.normalized : Vector3.right;
                    OrientStreakQuad(slot.Root.transform, pos, tangent);
                    float len = fx.QuadBaseScales[i] * Mathf.Lerp(1.25f, 0.5f, t);
                    slot.Root.transform.localScale = new Vector3(len, len * 0.42f, 1f);
                }

                Color col = fx.QuadIsSmoke[i] ? MeleeSparkGoldTint : MeleeSparkWhiteTint;
                col.a *= (1f - t);
                SetQuadTint(slot.Renderer, col);
            }
        }

        private void UpdateHealBurstVisual(ActiveVfxEffect fx)
        {
            float t = Mathf.Clamp01(fx.Elapsed / Mathf.Max(0.01f, fx.Duration));
            float easeOut = 1f - (1f - t) * (1f - t);
            Quaternion camRot = GetBillboardRotation();

            if (fx.QuadCount > 0 && fx.QuadIndices[0] >= 0)
            {
                PooledQuadSlot ring = _quadPool[fx.QuadIndices[0]];
                ring.Root.transform.position = fx.QuadStartPos[0];
                ring.Root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                float rScale = Mathf.Lerp(0.32f, fx.QuadBaseScales[0], easeOut);
                ring.Root.transform.localScale = new Vector3(rScale, rScale, 1f);
                Color rCol = HealRingTint;
                rCol.a *= (1f - t);
                SetQuadTint(ring.Renderer, rCol);
            }

            float moteFade = t < 0.25f
                ? Mathf.Lerp(0.4f, 1f, t / 0.25f)
                : Mathf.Clamp01(1f - (t - 0.25f) / 0.75f);

            for (int i = 1; i < fx.QuadCount; i++)
            {
                int qIdx = fx.QuadIndices[i];
                if (qIdx < 0) continue;
                PooledQuadSlot slot = _quadPool[qIdx];
                Vector3 pos = fx.QuadStartPos[i] + fx.QuadVelocities[i] * fx.Elapsed;
                slot.Root.transform.position = pos;
                slot.Root.transform.rotation = camRot;
                float pulse = 1f + 0.22f * Mathf.Sin(fx.Elapsed * 24f + i);
                float sc = fx.QuadBaseScales[i] * pulse * (1f - t * 0.35f);
                slot.Root.transform.localScale = new Vector3(sc, sc, 1f);
                Color col = fx.QuadIsSmoke[i] ? HealEmeraldTint : HealGoldTint;
                col.a *= moteFade;
                SetQuadTint(slot.Renderer, col);
            }
        }

        private void SetQuadTint(MeshRenderer renderer, Color color)
        {
            if (renderer == null) return;
            _propBlock ??= new MaterialPropertyBlock();
            _propBlock.Clear();
            _propBlock.SetColor(BaseColorId, color);
            _propBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_propBlock);
        }

        private static Vector3 EvalParabola(Vector3 start, Vector3 end, float arcHeight, float t)
        {
            Vector3 pos = Vector3.Lerp(start, end, t);
            pos.y += 4f * arcHeight * t * (1f - t);
            return pos;
        }

        private static Quaternion GetBillboardRotation()
        {
            Camera cam = Camera.main;
            return cam != null ? cam.transform.rotation : Quaternion.Euler(45f, 0f, 0f);
        }

        private static void OrientStreakQuad(Transform tf, Vector3 pos, Vector3 tangent)
        {
            Camera cam = Camera.main;
            Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.Euler(45f, 0f, 0f);
            if (cam != null)
            {
                float dx = Vector3.Dot(tangent, cam.transform.right);
                float dy = Vector3.Dot(tangent, cam.transform.up);
                float rollDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                tf.rotation = camRot * Quaternion.Euler(0f, 0f, rollDeg);
            }
            else
            {
                Vector3 flat = new Vector3(tangent.x, 0f, tangent.z);
                float yaw = flat.sqrMagnitude > 0.0001f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : 0f;
                tf.rotation = Quaternion.Euler(90f, yaw - 90f, 0f);
            }
            tf.position = pos;
        }

        private static Material CreateVfxMaterial(string matName, Texture2D tex, bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Particles/Standard Unlit")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                return null;
            }

            Material mat = new Material(shader)
            {
                name = matName,
                hideFlags = HideFlags.HideAndDontSave
            };

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", additive ? 2f : 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent + 12;

            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty(BaseMapId))
                {
                    mat.SetTexture(BaseMapId, tex);
                }
            }
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, Color.white);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, Color.white);
            return mat;
        }

        private static Texture2D BuildGlowTexture()
        {
            const int size = 32;
            Texture2D tex = CreateBlankTexture("VfxGlowTex", size, size, FilterMode.Bilinear, TextureWrapMode.Clamp);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = ((y + 0.5f) / size - 0.5f) * 2f;
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size - 0.5f) * 2f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), 1.8f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildStreakTexture()
        {
            const int w = 32;
            const int h = 12;
            Texture2D tex = CreateBlankTexture("VfxStreakTex", w, h, FilterMode.Point, TextureWrapMode.Clamp);
            Color32[] pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float ny = Mathf.Abs(((y + 0.5f) / h - 0.5f) * 2f);
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w; // 0 = tail, 1 = tip
                    bool inHead = nx >= 0.72f && ny <= (1f - nx) / 0.28f * 0.90f;
                    bool inShaft = nx < 0.72f && ny <= 0.26f;
                    float a = inHead ? 1.0f : (inShaft ? Mathf.Lerp(0.15f, 0.95f, nx / 0.72f) : 0f);
                    pixels[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildRingTexture()
        {
            const int size = 64;
            Texture2D tex = CreateBlankTexture("VfxRingTex", size, size, FilterMode.Bilinear, TextureWrapMode.Clamp);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = ((y + 0.5f) / size - 0.5f) * 2f;
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size - 0.5f) * 2f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float band = 1f - Mathf.Clamp01(Mathf.Abs(r - 0.78f) / 0.18f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, band * band);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildShardTexture()
        {
            const int size = 16;
            Texture2D tex = CreateBlankTexture("VfxShardTex", size, size, FilterMode.Point, TextureWrapMode.Clamp);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = Mathf.Abs(((y + 0.5f) / size - 0.5f) * 2f);
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs(((x + 0.5f) / size - 0.5f) * 2f);
                    float d = nx * 1.35f + ny * 0.85f;
                    float a = d <= 0.88f ? (d <= 0.45f ? 1.0f : 0.78f) : 0f;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildLightningTexture()
        {
            const int w = 32;
            const int h = 8;
            Texture2D tex = CreateBlankTexture("VfxLightningTex", w, h, FilterMode.Bilinear, TextureWrapMode.Repeat);
            Color32[] pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float ny = Mathf.Abs(((y + 0.5f) / h - 0.5f) * 2f);
                float a = Mathf.Pow(Mathf.Clamp01(1f - ny), 1.4f);
                Color32 c = new Color(1f, 1f, 1f, a);
                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildSmokeTexture()
        {
            const int size = 32;
            Texture2D tex = CreateBlankTexture("VfxSmokeTex", size, size, FilterMode.Bilinear, TextureWrapMode.Clamp);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = ((y + 0.5f) / size - 0.5f) * 2f;
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size - 0.5f) * 2f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.Clamp01(1f - d) * 0.82f;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D CreateBlankTexture(string texName, int w, int h, FilterMode filter, TextureWrapMode wrap)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = texName,
                filterMode = filter,
                wrapMode = wrap,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static int HashInt(int v)
        {
            unchecked
            {
                v = (v ^ 61) ^ (v >> 16);
                v += v << 3;
                v ^= v >> 4;
                v *= 0x27d4eb2d;
                v ^= v >> 15;
                return v & 0x7FFFFFFF;
            }
        }

        private static void DestroySafe(UnityEngine.Object obj)
        {
            if (obj == null) return;
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
