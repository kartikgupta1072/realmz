using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.Cartography
{
    /// <summary>
    /// Scatters 20-36 bottom-anchored, UV2.y-weighted 0.50m x 0.50m pixel foliage cards per
    /// open meadow/bank/forest tile across the 18x18 Bywater diorama via GPU instancing.
    /// </summary>
    [ExecuteAlways]
    public sealed class BywaterFoliageScatter : MonoBehaviour
    {
        public const int MaxBatchSize = 1023;
        public const int MinCardsPerOpenTile = 20;
        public const int MaxCardsPerOpenTile = 36;
        public const float CardWidthMeters = 0.50f;
        public const float CardHeightMeters = 0.50f;

        private static readonly int VariantIndexId = Shader.PropertyToID("_VariantIndex");
        private static readonly int WindPhaseId = Shader.PropertyToID("_WindPhase");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int FrameCountId = Shader.PropertyToID("_FrameCount");
        private static readonly int AnimFpsId = Shader.PropertyToID("_AnimFps");

        [Header("Material")]
        public Material foliageMaterial;

        [Header("Scatter Stats")]
        [SerializeField] private int totalCardCount;
        [SerializeField] private int populatedTileCount;

        public int TotalCardCount => totalCardCount;
        public int PopulatedTileCount => populatedTileCount;

        private sealed class FoliageBatch
        {
            public Matrix4x4[] Matrices;
            public float[] Variants;
            public float[] WindPhases;
            public Vector4[] Tints;
            public float[] FrameCounts;
            public float[] AnimFps;
            public int Count;
        }

        private readonly List<FoliageBatch> _batches = new List<FoliageBatch>(8);
        private readonly Dictionary<long, int> _tileCounts = new Dictionary<long, int>(256);

        private Mesh _cardMesh;
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            EnsureCardMesh();
        }

        private void OnEnable()
        {
            EnsureCardMesh();
            if (_batches.Count == 0)
            {
                RebuildScatter(CartographyWorldData.BuildBywaterProofLayout());
            }
        }

        private void OnDestroy()
        {
            if (_cardMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_cardMesh);
                }
                else
                {
                    DestroyImmediate(_cardMesh);
                }
                _cardMesh = null;
            }
        }

        private void LateUpdate()
        {
            RenderBatches(null);
        }

        public void RenderBatches(Camera targetCamera)
        {
            if (foliageMaterial == null)
            {
                return;
            }

            if (_batches.Count == 0)
            {
                RebuildScatter(CartographyWorldData.BuildBywaterProofLayout());
            }

            if (_batches.Count == 0 || !SystemInfo.supportsInstancing)
            {
                return;
            }

            EnsureCardMesh();
            if (!foliageMaterial.enableInstancing)
            {
                foliageMaterial.enableInstancing = true;
            }

            for (int i = 0; i < _batches.Count; i++)
            {
                FoliageBatch batch = _batches[i];
                if (batch.Count <= 0)
                {
                    continue;
                }

                _propBlock.Clear();
                _propBlock.SetFloatArray(VariantIndexId, batch.Variants);
                _propBlock.SetFloatArray(WindPhaseId, batch.WindPhases);
                _propBlock.SetVectorArray(TintColorId, batch.Tints);
                _propBlock.SetFloatArray(FrameCountId, batch.FrameCounts);
                _propBlock.SetFloatArray(AnimFpsId, batch.AnimFps);

                Graphics.DrawMeshInstanced(
                    _cardMesh,
                    0,
                    foliageMaterial,
                    batch.Matrices,
                    batch.Count,
                    _propBlock,
                    ShadowCastingMode.Off,
                    true,
                    gameObject.layer,
                    targetCamera);
            }
        }

        public void RebuildScatter(CartographyWorldData data)
        {
            if (data == null)
            {
                data = CartographyWorldData.BuildBywaterProofLayout();
            }

            EnsureCardMesh();
            _batches.Clear();
            _tileCounts.Clear();

            List<Matrix4x4> matrices = new List<Matrix4x4>(4096);
            List<float> variants = new List<float>(4096);
            List<float> windPhases = new List<float>(4096);
            List<Vector4> tints = new List<Vector4>(4096);
            List<float> frameCounts = new List<float>(4096);
            List<float> animFps = new List<float>(4096);

            int openTiles = 0;

            for (int y = 0; y < CartographyWorldData.GridHeight; y++)
            {
                for (int x = 0; x < CartographyWorldData.GridWidth; x++)
                {
                    CartographyTile tile = data.GetTile(x, y);
                    if (tile.BlocksFoliage || tile.IsWater || tile.BiomeZone == CartographyBiomeZone.Blocked)
                    {
                        continue;
                    }

                    int tileHash = CartographyWorldData.DeterministicHash(x, y, 107);
                    int countRange = MaxCardsPerOpenTile - MinCardsPerOpenTile + 1;
                    int cardCount = MinCardsPerOpenTile + (tileHash % countRange);

                    // Slight density boost in sunlit wildflower meadows while keeping >= 20 everywhere
                    if (tile.BiomeZone == CartographyBiomeZone.SunlitWildflower)
                    {
                        cardCount = Mathf.Clamp(cardCount + 3, MinCardsPerOpenTile, MaxCardsPerOpenTile);
                    }

                    openTiles++;
                    _tileCounts[PackKey(x, y)] = cardCount;

                    for (int i = 0; i < cardCount; i++)
                    {
                        int h = CartographyWorldData.DeterministicHash(x * 37 + i, y * 53 + i * 7, 911);

                        // Stratified jitter inside [0.06..0.94] so cards cover tile organically without grid lines
                        int gridSide = 5;
                        int cellX = i % gridSide;
                        int cellY = (i / gridSide) % gridSide;
                        float jx = (cellX + 0.15f + ((h & 0xFF) / 255f) * 0.70f) / gridSide;
                        float jz = (cellY + 0.15f + (((h >> 8) & 0xFF) / 255f) * 0.70f) / gridSide;

                        float wx = x + Mathf.Clamp(jx, 0.05f, 0.95f);
                        float wz = y + Mathf.Clamp(jz, 0.05f, 0.95f);

                        // Scale variance (0.85x to 1.25x around 0.50m base card)
                        float scaleW = 0.88f + (((h >> 16) & 0x1F) / 31f) * 0.34f;
                        float scaleH = 0.84f + (((h >> 21) & 0x1F) / 31f) * 0.42f;

                        int variant = PickVariantForBiome(tile.BiomeZone, h);
                        float windPhase = ((h >> 12) & 0xFF) / 255f;
                        float fps = 5.5f + (((h >> 26) & 0x0F) / 15f) * 3.0f;
                        Vector4 tint = ComputeCardTint(tile.BiomeZone, wx, wz, h);

                        matrices.Add(Matrix4x4.TRS(
                            new Vector3(wx, tile.Elevation, wz),
                            Quaternion.identity,
                            new Vector3(scaleW, scaleH, 1.0f)));
                        variants.Add(variant);
                        windPhases.Add(windPhase);
                        tints.Add(tint);
                        frameCounts.Add(8f);
                        animFps.Add(fps);
                    }
                }
            }

            // Slice into 1023-instance GPU batches
            int total = matrices.Count;
            for (int offset = 0; offset < total; offset += MaxBatchSize)
            {
                int count = Mathf.Min(MaxBatchSize, total - offset);
                FoliageBatch batch = new FoliageBatch
                {
                    Matrices = new Matrix4x4[count],
                    Variants = new float[count],
                    WindPhases = new float[count],
                    Tints = new Vector4[count],
                    FrameCounts = new float[count],
                    AnimFps = new float[count],
                    Count = count
                };

                for (int i = 0; i < count; i++)
                {
                    int src = offset + i;
                    batch.Matrices[i] = matrices[src];
                    batch.Variants[i] = variants[src];
                    batch.WindPhases[i] = windPhases[src];
                    batch.Tints[i] = tints[src];
                    batch.FrameCounts[i] = frameCounts[src];
                    batch.AnimFps[i] = animFps[src];
                }

                _batches.Add(batch);
            }

            totalCardCount = total;
            populatedTileCount = openTiles;
        }

        public int GetTileCardCount(int x, int y)
        {
            return _tileCounts.TryGetValue(PackKey(x, y), out int count) ? count : 0;
        }

        /// <summary>
        /// Computes exact [0..1] UV rectangle for a cell in the 2-bank 512x256 (16x8 of 32x32) foliage atlas.
        /// </summary>
        public static Rect ComputeTwoBankAtlasCellUvRect(int variantIndex, int frameIndex)
        {
            int variant = Mathf.Clamp(variantIndex, 0, 15);
            int frame = Mathf.Clamp(frameIndex, 0, 7);

            int bank = variant / 8;
            int rowFromTop = variant - bank * 8;

            float uMin = (bank * 8f + frame) / 16f;
            float vMin = 1f - (rowFromTop + 1f) / 8f;
            return new Rect(uMin, vMin, 1f / 16f, 1f / 8f);
        }

        /// <summary>
        /// Builds a bottom-anchored 0.50m x 0.50m quad where UV2.y = 0 at root and 1 at top.
        /// </summary>
        public static Mesh BuildBottomAnchoredFoliageCardMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "Cartography_FoliageCard_050m"
            };

            float halfW = CardWidthMeters * 0.5f;
            float h = CardHeightMeters;

            mesh.SetVertices(new[]
            {
                new Vector3(-halfW, 0f, 0f),
                new Vector3( halfW, 0f, 0f),
                new Vector3( halfW, h,  0f),
                new Vector3(-halfW, h,  0f)
            });

            mesh.SetNormals(new[]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            });

            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });

            // UV2.y = root-anchored height factor (0 at root, 1 at tip)
            mesh.SetUVs(1, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });

            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void EnsureCardMesh()
        {
            if (_cardMesh == null)
            {
                _cardMesh = BuildBottomAnchoredFoliageCardMesh();
            }

            if (_propBlock == null)
            {
                _propBlock = new MaterialPropertyBlock();
            }
        }

        private static int PickVariantForBiome(CartographyBiomeZone zone, int hash)
        {
            int roll = hash % 100;
            int sub = (hash >> 8) & 0x7FFF;
            switch (zone)
            {
                case CartographyBiomeZone.RiverbankReed:
                    // Cattail reeds (11), tall grass (5), radiating ferns (9), and lush meadow grass (0..3)
                    if (roll < 42) return 11;
                    if (roll < 64) return 5;
                    if (roll < 78) return 9;
                    return sub % 4;

                case CartographyBiomeZone.SunlitWildflower:
                    // Wildflowers (6,7,8), golden sunlit grass (14), clover (12), and meadow grass (0..5)
                    if (roll < 28) return 6 + (sub % 3);
                    if (roll < 46) return 14;
                    if (roll < 56) return 12;
                    return sub % 6;

                case CartographyBiomeZone.ForestShade:
                    // Shaded forest grass (15), radiating fern (9), moss pebble tuft (13), shrub (10), grass (0..1)
                    if (roll < 36) return 15;
                    if (roll < 64) return 9;
                    if (roll < 80) return 13;
                    if (roll < 88) return 10;
                    return sub % 2;

                default:
                    // Meadow grass (0..5), clover (12), sunlit grass (14), occasional wildflower (6..8)
                    if (roll < 10) return 6 + (sub % 3);
                    if (roll < 22) return 12;
                    if (roll < 34) return 14;
                    return sub % 6;
            }
        }

        private static Vector4 ComputeCardTint(CartographyBiomeZone zone, float wx, float wz, int hash)
        {
            float macro = Mathf.Sin(wx * 0.42f + 0.7f) * Mathf.Cos(wz * 0.39f - 0.5f) * 0.08f;
            float micro = (((hash >> 4) & 0x1F) / 31f - 0.5f) * 0.10f;
            float lum = 1.0f + macro + micro;

            switch (zone)
            {
                case CartographyBiomeZone.ForestShade:
                    return new Vector4(0.84f * lum, 0.94f * lum, 0.96f * lum, 1f);
                case CartographyBiomeZone.RiverbankReed:
                    return new Vector4(0.92f * lum, 1.00f * lum, 0.90f * lum, 1f);
                case CartographyBiomeZone.SunlitWildflower:
                    return new Vector4(1.03f * lum, 1.02f * lum, 0.94f * lum, 1f);
                default:
                    return new Vector4(0.98f * lum, 1.01f * lum, 0.94f * lum, 1f);
            }
        }

        private static long PackKey(int x, int y)
        {
            return ((long)x << 16) | (uint)y;
        }
    }
}
