using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.Cartography
{
    /// <summary>
    /// Scatters 20-36 bottom-anchored, UV2.y-weighted 0.50m x 0.50m pixel foliage cards per
    /// open outdoor tile at canonical coordinates (x + 0.5, elev, -(y + 0.5)), partitioned
    /// into 16x16 spatial chunks with prebuilt MaterialPropertyBlocks and frustum culling.
    /// </summary>
    [ExecuteAlways]
    public sealed class BywaterFoliageScatter : MonoBehaviour
    {
        public const int MaxBatchSize = 1023;
        public const int ChunkSizeTiles = 16;
        public const int MinCardsPerOpenTile = 20;
        public const int MaxCardsPerOpenTile = 36;
        public const float CardWidthMeters = 0.50f;
        public const float CardHeightMeters = 0.50f;

        private static readonly int VariantIndexId = Shader.PropertyToID("_VariantIndex");
        private static readonly int WindPhaseId = Shader.PropertyToID("_WindPhase");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int FrameCountId = Shader.PropertyToID("_FrameCount");
        private static readonly int AnimFpsId = Shader.PropertyToID("_AnimFps");
        private static readonly int FlipXId = Shader.PropertyToID("_FlipX");

        [Header("Material & Camera")]
        public Material foliageMaterial;
        public Material marshFoliageMaterial;
        public Camera targetCamera;

        [Header("Scatter Stats")]
        [SerializeField] private int totalCardCount;
        [SerializeField] private int populatedTileCount;
        [SerializeField] private int rootCollarCardCount;

        public int TotalCardCount => totalCardCount;
        public int TotalInstanceCount => totalCardCount;
        public int PopulatedTileCount => populatedTileCount;
        public int RootCollarCardCount => rootCollarCardCount;
        public int ChunkCount => _chunks.Count;
        public int LastVisibleChunkCount { get; private set; }

        private sealed class FoliageBatch
        {
            public Matrix4x4[] Matrices;
            public MaterialPropertyBlock PropertyBlock;
            public int Count;
        }

        private sealed class FoliageChunk
        {
            public Bounds Bounds;
            public readonly List<FoliageBatch> Batches = new List<FoliageBatch>(4);
            public readonly List<FoliageBatch> MarshBatches = new List<FoliageBatch>(4);
        }

        private struct RootCollarCard
        {
            public Matrix4x4 Matrix;
            public float Variant;
            public float WindPhase;
            public Vector4 Tint;
            public float FrameCount;
            public float AnimFps;
            public float FlipX;
        }

        private readonly List<FoliageChunk> _chunks = new List<FoliageChunk>(36);
        private readonly Dictionary<long, int> _tileCounts = new Dictionary<long, int>(1024);
        private readonly Plane[] _frustumPlanes = new Plane[6];

        private Mesh _cardMesh;
        private bool _hasBuilt;

        private void Awake()
        {
            EnsureCardMesh();
        }

        private void OnEnable()
        {
            EnsureCardMesh();
            if (!_hasBuilt)
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
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            RenderBatchesInternal(cam, null);
        }

        public void RenderBatches(Camera cam)
        {
            Camera cullCam = cam != null ? cam : (targetCamera != null ? targetCamera : Camera.main);
            RenderBatchesInternal(cullCam, cam);
        }

        private void RenderBatchesInternal(Camera cullCamera, Camera drawCamera)
        {
            if (!_hasBuilt)
            {
                RebuildScatter(CartographyWorldData.BuildBywaterProofLayout());
            }

            bool hasFrustum = cullCamera != null;
            if (hasFrustum)
            {
                GeometryUtility.CalculateFrustumPlanes(cullCamera, _frustumPlanes);
            }

            bool canDrawPrimary = foliageMaterial != null && SystemInfo.supportsInstancing;
            bool canDrawMarsh = marshFoliageMaterial != null && SystemInfo.supportsInstancing;
            if (canDrawPrimary || canDrawMarsh)
            {
                EnsureCardMesh();
                if (canDrawPrimary && !foliageMaterial.enableInstancing)
                {
                    foliageMaterial.enableInstancing = true;
                }
                if (canDrawMarsh && !marshFoliageMaterial.enableInstancing)
                {
                    marshFoliageMaterial.enableInstancing = true;
                }
            }

            int visibleChunks = 0;
            for (int c = 0; c < _chunks.Count; c++)
            {
                FoliageChunk chunk = _chunks[c];
                if (chunk.Batches.Count == 0 && chunk.MarshBatches.Count == 0)
                {
                    continue;
                }

                if (hasFrustum && !GeometryUtility.TestPlanesAABB(_frustumPlanes, chunk.Bounds))
                {
                    continue;
                }

                visibleChunks++;
                if (canDrawPrimary)
                {
                    DrawBatchList(chunk.Batches, foliageMaterial, drawCamera);
                }
                if (canDrawMarsh)
                {
                    DrawBatchList(chunk.MarshBatches, marshFoliageMaterial, drawCamera);
                }
            }

            LastVisibleChunkCount = visibleChunks;
        }

        private void DrawBatchList(List<FoliageBatch> batches, Material mat, Camera drawCamera)
        {
            for (int b = 0; b < batches.Count; b++)
            {
                FoliageBatch batch = batches[b];
                if (batch.Count <= 0)
                {
                    continue;
                }

                Graphics.DrawMeshInstanced(
                    _cardMesh,
                    0,
                    mat,
                    batch.Matrices,
                    batch.Count,
                    batch.PropertyBlock,
                    ShadowCastingMode.Off,
                    true,
                    gameObject.layer,
                    drawCamera);
            }
        }

        public void Rebuild(CartographyWorldData data)
        {
            RebuildScatter(data);
        }

        public void RebuildScatter(CartographyWorldData data)
        {
            if (data == null)
            {
                data = CartographyWorldData.BuildBywaterProofLayout();
            }

            EnsureCardMesh();
            _chunks.Clear();
            _tileCounts.Clear();
            LastVisibleChunkCount = 0;

            int width = data.Width;
            int height = data.Height;
            int chunksX = Mathf.CeilToInt(width / (float)ChunkSizeTiles);
            int chunksY = Mathf.CeilToInt(height / (float)ChunkSizeTiles);

            if (data.IsDungeon)
            {
                for (int cy = 0; cy < chunksY; cy++)
                {
                    int minY = cy * ChunkSizeTiles;
                    int maxY = Mathf.Min(height, minY + ChunkSizeTiles);
                    for (int cx = 0; cx < chunksX; cx++)
                    {
                        int minX = cx * ChunkSizeTiles;
                        int maxX = Mathf.Min(width, minX + ChunkSizeTiles);
                        Vector3 center = new Vector3((minX + maxX) * 0.5f, 0f, -(minY + maxY) * 0.5f);
                        Vector3 size = new Vector3(maxX - minX, 1f, maxY - minY);
                        _chunks.Add(new FoliageChunk { Bounds = new Bounds(center, size) });
                    }
                }

                totalCardCount = 0;
                populatedTileCount = 0;
                rootCollarCardCount = 0;
                _hasBuilt = true;
                return;
            }

            List<RootCollarCard>[] collarBuckets = new List<RootCollarCard>[chunksX * chunksY];
            int collarCardsPlaced = 0;
            IReadOnlyList<CartographyPropPlacement> props = data.Props;
            for (int p = 0; p < props.Count; p++)
            {
                CartographyPropPlacement prop = props[p];
                if (!prop.HasRootCollar)
                {
                    continue;
                }

                CartographyWorldData.WorldToGrid(prop.WorldPosition, out int ax, out int ay);
                if (!data.InBounds(ax, ay))
                {
                    continue;
                }

                CartographyTile anchorTile = data.GetTile(ax, ay);
                if (anchorTile.IsWater || anchorTile.CobbleWeight > 0.45f || anchorTile.DirtWeight > 0.65f)
                {
                    continue;
                }

                int propHash = CartographyWorldData.DeterministicHash(ax * 31 + p, ay * 47 + (int)prop.Kind, 613);
                int collarCount = 4 + (propHash % 5);
                for (int i = 0; i < collarCount; i++)
                {
                    int h = CartographyWorldData.DeterministicHash(propHash + i * 19, p * 53 + i * 7, 859);
                    float angleJitter = (((h & 0xFF) / 255f) - 0.5f) * (Mathf.PI * 2f / collarCount) * 0.55f;
                    float angle = (i / (float)collarCount) * Mathf.PI * 2f + angleJitter;
                    float radiusFrac = ((h >> 8) & 0xFF) / 255f;
                    float radius = 0.36f + radiusFrac * 0.14f;

                    float wx = prop.WorldPosition.x + Mathf.Cos(angle) * radius;
                    float wz = prop.WorldPosition.z + Mathf.Sin(angle) * radius;
                    CartographyWorldData.WorldToGrid(new Vector3(wx, 0f, wz), out int tx, out int ty);
                    if (!data.InBounds(tx, ty))
                    {
                        continue;
                    }

                    CartographyTile tile = data.GetTile(tx, ty);
                    if (tile.IsWater ||
                        tile.CobbleWeight > 0.45f ||
                        tile.DirtWeight > 0.65f ||
                        (tile.BiomeZone == CartographyBiomeZone.Blocked && !data.IsBlockedByProp(tx, ty)))
                    {
                        continue;
                    }

                    int chunkX = tx / ChunkSizeTiles;
                    int chunkY = ty / ChunkSizeTiles;
                    int chunkIdx = chunkY * chunksX + chunkX;
                    List<RootCollarCard> bucket = collarBuckets[chunkIdx];
                    if (bucket == null)
                    {
                        bucket = new List<RootCollarCard>(16);
                        collarBuckets[chunkIdx] = bucket;
                    }

                    float scaleW = 0.72f + (((h >> 16) & 0x1F) / 31f) * 0.28f;
                    float scaleH = 0.64f + (((h >> 21) & 0x1F) / 31f) * 0.28f;
                    CartographyBiomeZone collarZone = tile.BiomeZone == CartographyBiomeZone.Blocked
                        ? CartographyBiomeZone.ForestShade
                        : tile.BiomeZone;
                    int variant = PickVariantForBiome(collarZone, h);
                    float windPhase = ((h >> 12) & 0xFF) / 255f;
                    float fps = 5.0f + (((h >> 26) & 0x0F) / 15f) * 2.5f;
                    Vector4 tint = ComputeCardTint(collarZone, wx, wz, h);
                    float flipX = ((h >> 3) & 1) == 1 ? 1f : 0f;

                    bucket.Add(new RootCollarCard
                    {
                        Matrix = Matrix4x4.TRS(
                            new Vector3(wx, tile.Elevation, wz),
                            Quaternion.identity,
                            new Vector3(scaleW, scaleH, 1.0f)),
                        Variant = variant,
                        WindPhase = windPhase,
                        Tint = tint,
                        FrameCount = 8f,
                        AnimFps = fps,
                        FlipX = flipX
                    });
                    collarCardsPlaced++;
                }
            }

            List<Matrix4x4> matrices = new List<Matrix4x4>(4096);
            List<float> variants = new List<float>(4096);
            List<float> windPhases = new List<float>(4096);
            List<Vector4> tints = new List<Vector4>(4096);
            List<float> frameCounts = new List<float>(4096);
            List<float> animFps = new List<float>(4096);
            List<float> flipXs = new List<float>(4096);

            List<Matrix4x4> marshMatrices = new List<Matrix4x4>(1024);
            List<float> marshVariants = new List<float>(1024);
            List<float> marshWindPhases = new List<float>(1024);
            List<Vector4> marshTints = new List<Vector4>(1024);
            List<float> marshFrameCounts = new List<float>(1024);
            List<float> marshAnimFps = new List<float>(1024);
            List<float> marshFlipXs = new List<float>(1024);

            System.Span<int> strataPass0 = stackalloc int[25];
            System.Span<int> strataPass1 = stackalloc int[25];

            int total = 0;
            int openTiles = 0;
            bool splitMarsh = marshFoliageMaterial != null;

            for (int cy = 0; cy < chunksY; cy++)
            {
                int yStart = cy * ChunkSizeTiles;
                int yEnd = Mathf.Min(height, yStart + ChunkSizeTiles);

                for (int cx = 0; cx < chunksX; cx++)
                {
                    int xStart = cx * ChunkSizeTiles;
                    int xEnd = Mathf.Min(width, xStart + ChunkSizeTiles);

                    matrices.Clear();
                    variants.Clear();
                    windPhases.Clear();
                    tints.Clear();
                    frameCounts.Clear();
                    animFps.Clear();
                    flipXs.Clear();

                    marshMatrices.Clear();
                    marshVariants.Clear();
                    marshWindPhases.Clear();
                    marshTints.Clear();
                    marshFrameCounts.Clear();
                    marshAnimFps.Clear();
                    marshFlipXs.Clear();

                    float minElev = float.MaxValue;
                    float maxElev = float.MinValue;

                    for (int y = yStart; y < yEnd; y++)
                    {
                        for (int x = xStart; x < xEnd; x++)
                        {
                            CartographyTile tile = data.GetTile(x, y);
                            if (tile.BlocksFoliage || tile.IsWater || tile.BiomeZone == CartographyBiomeZone.Blocked)
                            {
                                continue;
                            }

                            int tileHash = CartographyWorldData.DeterministicHash(x, y, 107);
                            int countRange = MaxCardsPerOpenTile - MinCardsPerOpenTile + 1;
                            int cardCount = MinCardsPerOpenTile + (tileHash % countRange);

                            if (tile.BiomeZone == CartographyBiomeZone.SunlitWildflower)
                            {
                                cardCount = Mathf.Clamp(cardCount + 3, MinCardsPerOpenTile, MaxCardsPerOpenTile);
                            }

                            openTiles++;
                            _tileCounts[PackKey(x, y)] = cardCount;

                            if (tile.Elevation < minElev) minElev = tile.Elevation;
                            if (tile.Elevation > maxElev) maxElev = tile.Elevation;

                            FillShuffledStrata25(strataPass0, tileHash);
                            if (cardCount > 25)
                            {
                                FillShuffledStrata25(strataPass1, tileHash ^ 0x517cc1b7);
                            }

                            bool openWest = data.InBounds(x - 1, y) && !data.GetTile(x - 1, y).BlocksFoliage && !data.GetTile(x - 1, y).IsWater;
                            bool openEast = data.InBounds(x + 1, y) && !data.GetTile(x + 1, y).BlocksFoliage && !data.GetTile(x + 1, y).IsWater;
                            bool openNorth = data.InBounds(x, y - 1) && !data.GetTile(x, y - 1).BlocksFoliage && !data.GetTile(x, y - 1).IsWater;
                            bool openSouth = data.InBounds(x, y + 1) && !data.GetTile(x, y + 1).BlocksFoliage && !data.GetTile(x, y + 1).IsWater;

                            bool useMarsh = splitMarsh && (tile.BiomeZone == CartographyBiomeZone.RiverbankReed || tile.MudWeight >= 0.35f);
                            List<Matrix4x4> dstMatrices = useMarsh ? marshMatrices : matrices;
                            List<float> dstVariants = useMarsh ? marshVariants : variants;
                            List<float> dstWind = useMarsh ? marshWindPhases : windPhases;
                            List<Vector4> dstTints = useMarsh ? marshTints : tints;
                            List<float> dstFrames = useMarsh ? marshFrameCounts : frameCounts;
                            List<float> dstFps = useMarsh ? marshAnimFps : animFps;
                            List<float> dstFlips = useMarsh ? marshFlipXs : flipXs;

                            for (int i = 0; i < cardCount; i++)
                            {
                                int h = CartographyWorldData.DeterministicHash(x * 37 + i, y * 53 + i * 7, 911);
                                int strataIndex = (i < 25) ? strataPass0[i] : strataPass1[i - 25];
                                Vector2 localXY = ComputeCellOffsetFromStrata(strataIndex, h);
                                if (openWest && openEast)
                                {
                                    localXY.x = Mathf.Lerp(-0.04f, 1.04f, localXY.x);
                                }
                                if (openNorth && openSouth)
                                {
                                    localXY.y = Mathf.Lerp(-0.04f, 1.04f, localXY.y);
                                }

                                float wx = x + localXY.x;
                                float wz = -(y + localXY.y);

                                float scaleW = 0.88f + (((h >> 16) & 0x1F) / 31f) * 0.34f;
                                float scaleH = 0.84f + (((h >> 21) & 0x1F) / 31f) * 0.42f;

                                int variant = PickVariantForBiome(tile.BiomeZone, h);
                                float windPhase = ((h >> 12) & 0xFF) / 255f;
                                float fps = 5.5f + (((h >> 26) & 0x0F) / 15f) * 3.0f;
                                Vector4 tint = ComputeCardTint(tile.BiomeZone, wx, wz, h);
                                float flipX = ((h >> 3) & 1) == 1 ? 1f : 0f;

                                dstMatrices.Add(Matrix4x4.TRS(
                                    new Vector3(wx, tile.Elevation, wz),
                                    Quaternion.identity,
                                    new Vector3(scaleW, scaleH, 1.0f)));
                                dstVariants.Add(variant);
                                dstWind.Add(windPhase);
                                dstTints.Add(tint);
                                dstFrames.Add(8f);
                                dstFps.Add(fps);
                                dstFlips.Add(flipX);
                            }
                        }
                    }

                    List<RootCollarCard> collarBucket = collarBuckets[cy * chunksX + cx];
                    if (collarBucket != null)
                    {
                        for (int ci = 0; ci < collarBucket.Count; ci++)
                        {
                            RootCollarCard card = collarBucket[ci];
                            float cardElev = card.Matrix.m13;
                            if (cardElev < minElev) minElev = cardElev;
                            if (cardElev > maxElev) maxElev = cardElev;

                            matrices.Add(card.Matrix);
                            variants.Add(card.Variant);
                            windPhases.Add(card.WindPhase);
                            tints.Add(card.Tint);
                            frameCounts.Add(card.FrameCount);
                            animFps.Add(card.AnimFps);
                            flipXs.Add(card.FlipX);
                        }
                    }

                    FoliageChunk chunk = new FoliageChunk();
                    int chunkTotal = matrices.Count + marshMatrices.Count;
                    if (chunkTotal > 0)
                    {
                        Vector3 boundsMin = new Vector3(xStart - 0.35f, minElev - 0.1f, -yEnd - 0.35f);
                        Vector3 boundsMax = new Vector3(xEnd + 0.35f, maxElev + CardHeightMeters * 1.35f, -yStart + 0.35f);
                        chunk.Bounds = new Bounds((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin);

                        total += chunkTotal;
                        BuildBatchesFromLists(chunk.Batches, matrices, variants, windPhases, tints, frameCounts, animFps, flipXs);
                        BuildBatchesFromLists(chunk.MarshBatches, marshMatrices, marshVariants, marshWindPhases, marshTints, marshFrameCounts, marshAnimFps, marshFlipXs);
                    }
                    else
                    {
                        Vector3 center = new Vector3((xStart + xEnd) * 0.5f, 0f, -(yStart + yEnd) * 0.5f);
                        Vector3 size = new Vector3(xEnd - xStart, 1f, yEnd - yStart);
                        chunk.Bounds = new Bounds(center, size);
                    }

                    _chunks.Add(chunk);
                }
            }

            totalCardCount = total;
            populatedTileCount = openTiles;
            rootCollarCardCount = collarCardsPlaced;
            _hasBuilt = true;
        }

        /// <summary>
        /// Scatters sparse glowing cave mushrooms, cave ferns, bone piles, and stone chips for dungeon
        /// maps using foliage_atlas_dungeon_cave without altering the primary Rebuild(dungeon) test path.
        /// </summary>
        public void RebuildDungeonScatter(CartographyWorldData data, Material dungeonFoliageMaterial)
        {
            if (data == null || !data.IsDungeon)
            {
                RebuildScatter(data);
                return;
            }

            if (dungeonFoliageMaterial != null)
            {
                foliageMaterial = dungeonFoliageMaterial;
            }
            marshFoliageMaterial = null;

            EnsureCardMesh();
            _chunks.Clear();
            _tileCounts.Clear();
            LastVisibleChunkCount = 0;

            int width = data.Width;
            int height = data.Height;
            int chunksX = Mathf.CeilToInt(width / (float)ChunkSizeTiles);
            int chunksY = Mathf.CeilToInt(height / (float)ChunkSizeTiles);

            List<Matrix4x4> matrices = new List<Matrix4x4>(512);
            List<float> variants = new List<float>(512);
            List<float> windPhases = new List<float>(512);
            List<Vector4> tints = new List<Vector4>(512);
            List<float> frameCounts = new List<float>(512);
            List<float> animFps = new List<float>(512);
            List<float> flipXs = new List<float>(512);
            System.Span<int> strata = stackalloc int[25];

            int total = 0;
            int populatedTiles = 0;

            for (int cy = 0; cy < chunksY; cy++)
            {
                int yStart = cy * ChunkSizeTiles;
                int yEnd = Mathf.Min(height, yStart + ChunkSizeTiles);

                for (int cx = 0; cx < chunksX; cx++)
                {
                    int xStart = cx * ChunkSizeTiles;
                    int xEnd = Mathf.Min(width, xStart + ChunkSizeTiles);

                    matrices.Clear();
                    variants.Clear();
                    windPhases.Clear();
                    tints.Clear();
                    frameCounts.Clear();
                    animFps.Clear();
                    flipXs.Clear();

                    float minElev = float.MaxValue;
                    float maxElev = float.MinValue;

                    for (int y = yStart; y < yEnd; y++)
                    {
                        for (int x = xStart; x < xEnd; x++)
                        {
                            CartographyTile tile = data.GetTile(x, y);
                            if (tile.IsWater || tile.Elevation >= CartographyWorldData.DungeonWallElevation - 0.05f)
                            {
                                continue;
                            }

                            int tileHash = CartographyWorldData.DeterministicHash(x, y, 401);
                            bool nearWallOrCrease = tile.CreaseAO < 0.85f;
                            int cardCount;
                            if (nearWallOrCrease)
                            {
                                if ((tileHash % 3) >= 2)
                                {
                                    continue;
                                }
                                cardCount = 2 + (tileHash % 3); // 2..4 wall-edge cave mushrooms/ferns
                            }
                            else
                            {
                                if ((tileHash % 13) >= 2)
                                {
                                    continue;
                                }
                                cardCount = 1 + (tileHash % 2); // 1..2 floor bone piles/stone chips
                            }

                            populatedTiles++;
                            _tileCounts[PackKey(x, y)] = cardCount;
                            if (tile.Elevation < minElev) minElev = tile.Elevation;
                            if (tile.Elevation > maxElev) maxElev = tile.Elevation;

                            FillShuffledStrata25(strata, tileHash);
                            for (int i = 0; i < cardCount; i++)
                            {
                                int h = CartographyWorldData.DeterministicHash(x * 41 + i, y * 59 + i * 11, 773);
                                Vector2 localXY = ComputeCellOffsetFromStrata(strata[i], h);
                                float wx = x + Mathf.Lerp(0.08f, 0.92f, localXY.x);
                                float wz = -(y + Mathf.Lerp(0.08f, 0.92f, localXY.y));

                                float scaleW = 0.78f + (((h >> 16) & 0x1F) / 31f) * 0.30f;
                                float scaleH = 0.78f + (((h >> 21) & 0x1F) / 31f) * 0.34f;

                                int variant = nearWallOrCrease
                                    ? (h % 10)
                                    : (10 + (h % 6));
                                float windPhase = ((h >> 12) & 0xFF) / 255f;
                                float fps = 2.6f + (((h >> 26) & 0x0F) / 15f) * 1.8f;
                                float lum = 0.88f + (((h >> 4) & 0x1F) / 31f) * 0.22f;
                                Vector4 tint = nearWallOrCrease
                                    ? new Vector4(0.76f * lum, 0.90f * lum, 0.98f * lum, 1f)
                                    : new Vector4(0.84f * lum, 0.86f * lum, 0.90f * lum, 1f);
                                float flipX = ((h >> 3) & 1) == 1 ? 1f : 0f;

                                matrices.Add(Matrix4x4.TRS(
                                    new Vector3(wx, tile.Elevation, wz),
                                    Quaternion.identity,
                                    new Vector3(scaleW, scaleH, 1.0f)));
                                variants.Add(variant);
                                windPhases.Add(windPhase);
                                tints.Add(tint);
                                frameCounts.Add(8f);
                                animFps.Add(fps);
                                flipXs.Add(flipX);
                            }
                        }
                    }

                    FoliageChunk chunk = new FoliageChunk();
                    if (matrices.Count > 0)
                    {
                        Vector3 boundsMin = new Vector3(xStart - 0.35f, minElev - 0.1f, -yEnd - 0.35f);
                        Vector3 boundsMax = new Vector3(xEnd + 0.35f, maxElev + CardHeightMeters * 1.35f, -yStart + 0.35f);
                        chunk.Bounds = new Bounds((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin);

                        total += matrices.Count;
                        BuildBatchesFromLists(chunk.Batches, matrices, variants, windPhases, tints, frameCounts, animFps, flipXs);
                    }
                    else
                    {
                        Vector3 center = new Vector3((xStart + xEnd) * 0.5f, 0f, -(yStart + yEnd) * 0.5f);
                        Vector3 size = new Vector3(xEnd - xStart, 1f, yEnd - yStart);
                        chunk.Bounds = new Bounds(center, size);
                    }

                    _chunks.Add(chunk);
                }
            }

            totalCardCount = total;
            populatedTileCount = populatedTiles;
            rootCollarCardCount = 0;
            _hasBuilt = true;
        }

        private static void BuildBatchesFromLists(
            List<FoliageBatch> targetBatches,
            List<Matrix4x4> matrices,
            List<float> variants,
            List<float> windPhases,
            List<Vector4> tints,
            List<float> frameCounts,
            List<float> animFps,
            List<float> flipXs)
        {
            int totalCount = matrices.Count;
            if (totalCount == 0)
            {
                return;
            }

            for (int offset = 0; offset < totalCount; offset += MaxBatchSize)
            {
                int count = Mathf.Min(MaxBatchSize, totalCount - offset);
                Matrix4x4[] batchMatrices = new Matrix4x4[count];
                float[] batchVariants = new float[count];
                float[] batchWindPhases = new float[count];
                Vector4[] batchTints = new Vector4[count];
                float[] batchFrameCounts = new float[count];
                float[] batchAnimFps = new float[count];
                float[] batchFlipXs = new float[count];

                for (int i = 0; i < count; i++)
                {
                    int src = offset + i;
                    batchMatrices[i] = matrices[src];
                    batchVariants[i] = variants[src];
                    batchWindPhases[i] = windPhases[src];
                    batchTints[i] = tints[src];
                    batchFrameCounts[i] = frameCounts[src];
                    batchAnimFps[i] = animFps[src];
                    batchFlipXs[i] = flipXs[src];
                }

                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                mpb.SetFloatArray(VariantIndexId, batchVariants);
                mpb.SetFloatArray(WindPhaseId, batchWindPhases);
                mpb.SetVectorArray(TintColorId, batchTints);
                mpb.SetFloatArray(FrameCountId, batchFrameCounts);
                mpb.SetFloatArray(AnimFpsId, batchAnimFps);
                mpb.SetFloatArray(FlipXId, batchFlipXs);

                targetBatches.Add(new FoliageBatch
                {
                    Matrices = batchMatrices,
                    PropertyBlock = mpb,
                    Count = count
                });
            }
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
        }

        private static int PickVariantForBiome(CartographyBiomeZone zone, int hash)
        {
            int roll = hash % 100;
            int sub = (hash >> 8) & 0x7FFF;
            switch (zone)
            {
                case CartographyBiomeZone.RiverbankReed:
                    if (roll < 42) return 11;
                    if (roll < 64) return 5;
                    if (roll < 78) return 9;
                    return sub % 4;

                case CartographyBiomeZone.SunlitWildflower:
                    if (roll < 28) return 6 + (sub % 3);
                    if (roll < 46) return 14;
                    if (roll < 56) return 12;
                    return sub % 6;

                case CartographyBiomeZone.ForestShade:
                    if (roll < 36) return 15;
                    if (roll < 64) return 9;
                    if (roll < 80) return 13;
                    if (roll < 88) return 10;
                    return sub % 2;

                default:
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
                    return new Vector4(0.66f * lum, 0.78f * lum, 0.80f * lum, 1f);
                case CartographyBiomeZone.RiverbankReed:
                    return new Vector4(0.74f * lum, 0.82f * lum, 0.72f * lum, 1f);
                case CartographyBiomeZone.SunlitWildflower:
                    return new Vector4(0.84f * lum, 0.85f * lum, 0.76f * lum, 1f);
                default:
                    return new Vector4(0.76f * lum, 0.82f * lum, 0.72f * lum, 1f);
            }
        }

        /// <summary>
        /// Deterministic Fisher-Yates shuffle of strata indices 0..24 across 5 Z-band rounds
        /// so every tile gets a non-linear permutation without starving any horizontal Z band.
        /// </summary>
        public static void FillShuffledStrata25(System.Span<int> dst, int tileHash)
        {
            if (dst.Length < 25)
            {
                throw new System.ArgumentException("dst span must have length >= 25.", nameof(dst));
            }

            uint state = (uint)tileHash;
            if (state == 0u)
            {
                state = 0x9E3779B9u;
            }

            System.Span<int> rowCols = stackalloc int[25];
            for (int r = 0; r < 5; r++)
            {
                int rowBase = r * 5;
                for (int c = 0; c < 5; c++)
                {
                    rowCols[rowBase + c] = rowBase + c;
                }

                for (int c = 4; c > 0; c--)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    int j = (int)(state % (uint)(c + 1));
                    int tmp = rowCols[rowBase + c];
                    rowCols[rowBase + c] = rowCols[rowBase + j];
                    rowCols[rowBase + j] = tmp;
                }
            }

            System.Span<int> rowOrder = stackalloc int[5];
            for (int k = 0; k < 5; k++)
            {
                for (int r = 0; r < 5; r++)
                {
                    rowOrder[r] = r;
                }

                for (int r = 4; r > 0; r--)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    int j = (int)(state % (uint)(r + 1));
                    int tmp = rowOrder[r];
                    rowOrder[r] = rowOrder[j];
                    rowOrder[j] = tmp;
                }

                for (int m = 0; m < 5; m++)
                {
                    int r = rowOrder[m];
                    dst[k * 5 + m] = rowCols[r * 5 + k];
                }
            }
        }

        /// <summary>
        /// Computes deterministically Fisher-Yates shuffled 5x5 stratified XZ placement in [0.05..0.95] per tile
        /// so card counts in 20..36 never leave empty horizontal bands or stack at row borders.
        /// </summary>
        public static Vector2 ComputeCardCellOffset(int cardIndex, int tileHash, int cardHash)
        {
            System.Span<int> strata = stackalloc int[25];
            int seed = (cardIndex < 25) ? tileHash : (tileHash ^ 0x517cc1b7);
            FillShuffledStrata25(strata, seed);
            int slot = cardIndex % 25;
            return ComputeCellOffsetFromStrata(strata[slot], cardHash);
        }

        private static Vector2 ComputeCellOffsetFromStrata(int strata, int cardHash)
        {
            const int gridSide = 5;
            int cellX = strata % gridSide;
            int cellY = strata / gridSide;
            float jx = (cellX + 0.02f + ((cardHash & 0xFF) / 255f) * 0.96f) / gridSide;
            float jz = (cellY + 0.02f + (((cardHash >> 8) & 0xFF) / 255f) * 0.96f) / gridSide;
            return new Vector2(Mathf.Clamp(jx, 0.005f, 0.995f), Mathf.Clamp(jz, 0.005f, 0.995f));
        }

        private static long PackKey(int x, int y)
        {
            return ((long)x << 16) | (uint)y;
        }
    }
}
