using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.Cartography
{
    /// <summary>
    /// Maintains the top-down lightmap RenderTexture (_TopDownLightmapTex + _TopDownLightmapBounds)
    /// and static baked world map (_WorldStaticMap: R=1-canopy/cliff AO, G=shoreline SDF proximity, B=lantern glow)
    /// aligned to canonical coordinates (X in [0, Width], Z in [-Height, 0]).
    /// </summary>
    [ExecuteAlways]
    public sealed class BywaterTopDownLightmap : MonoBehaviour
    {
        public const int LightmapResolution = 256;
        public const string GeneratorShaderName = "Hidden/Realmz/CartographyTopDownLightmapGen";

        private static readonly int TopDownLightmapTexId = Shader.PropertyToID("_TopDownLightmapTex");
        private static readonly int TopDownLightmapBoundsId = Shader.PropertyToID("_TopDownLightmapBounds");
        private static readonly int WorldStaticMapId = Shader.PropertyToID("_WorldStaticMap");

        private static readonly int SimTimeId = Shader.PropertyToID("_SimTime");
        private static readonly int WorldSizeId = Shader.PropertyToID("_WorldSize");

        [Header("Appearance")]
        public Color cloudShadowColor = new Color(0.56f, 0.62f, 0.74f, 1f);
        public Color canopyShadowColor = new Color(0.48f, 0.54f, 0.66f, 1f);
        [Range(0f, 1f)] public float cloudStrength = 0.82f;

        private RenderTexture _lightmapRT;
        private Texture2D _worldStaticMap;
        private Texture2D _fallbackTexture;
        private Material _generatorMaterial;
        private bool _configured;
        private int _worldWidth = CartographyWorldData.GridWidth;
        private int _worldHeight = CartographyWorldData.GridHeight;

        public RenderTexture LightmapRT => _lightmapRT;
        public Texture2D WorldStaticMap => _worldStaticMap;
        public Vector4 WorldBounds => new Vector4(0f, -_worldHeight, 1f / _worldWidth, 1f / _worldHeight);

        private void OnEnable()
        {
            if (!_configured)
            {
                ConfigureFromWorldData(CartographyWorldData.BuildBywaterProofLayout());
            }
            UpdateLightmap(Application.isPlaying ? Time.time : 0f);
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        public void ConfigureFromWorldData(CartographyWorldData data)
        {
            if (data == null)
            {
                data = CartographyWorldData.BuildBywaterProofLayout();
            }

            _worldWidth = Mathf.Max(1, data.Width);
            _worldHeight = Mathf.Max(1, data.Height);
            int staticRes = Mathf.Clamp(Mathf.Max(_worldWidth, _worldHeight) * 8, 128, 512);

            if (_worldStaticMap == null || _worldStaticMap.width != staticRes || _worldStaticMap.height != staticRes)
            {
                if (_worldStaticMap != null)
                {
                    DestroySafe(_worldStaticMap);
                }

                _worldStaticMap = new Texture2D(staticRes, staticRes, TextureFormat.RGBA32, false, true)
                {
                    name = "Bywater_WorldStaticMap",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            // 1. Multi-source BFS from land (!IsWater) tiles for shoreline distance field
            int[,] waterBfsDist = ComputeShoreBfsDistances(data, _worldWidth, _worldHeight);

            // 2. Spatial bucket props & lanterns so 90x90 baking stays < 15ms with zero caps
            const float bucketSize = 4f;
            int bucketsX = Mathf.Max(1, Mathf.CeilToInt(_worldWidth / bucketSize));
            int bucketsY = Mathf.Max(1, Mathf.CeilToInt(_worldHeight / bucketSize));
            int bucketCount = bucketsX * bucketsY;

            var props = data.Props;
            List<int>[] canopyBuckets = new List<int>[bucketCount];
            List<int> bridgeProps = new List<int>(4);
            for (int i = 0; i < props.Count; i++)
            {
                CartographyPropPlacement prop = props[i];
                if (prop.Kind == CartographyPropKind.StoneBridge)
                {
                    bridgeProps.Add(i);
                }

                if (!prop.CastsCanopyShadow)
                {
                    continue;
                }

                float radius = Mathf.Max(0.5f, prop.CanopyRadius);
                float pyCoord = -prop.WorldPosition.z;
                InsertIntoBuckets(canopyBuckets, bucketsX, bucketsY, bucketSize, prop.WorldPosition.x, pyCoord, radius, i);
            }

            var lanterns = data.Lanterns;
            List<int>[] lanternBuckets = new List<int>[bucketCount];
            for (int j = 0; j < lanterns.Count; j++)
            {
                CartographyLanternSource l = lanterns[j];
                float radius = Mathf.Max(0.5f, l.Radius);
                float lyCoord = -l.WorldXZ.y;
                InsertIntoBuckets(lanternBuckets, bucketsX, bucketsY, bucketSize, l.WorldXZ.x, lyCoord, radius, j);
            }

            Color[] pixels = new Color[staticRes * staticRes];

            for (int py = 0; py < staticRes; py++)
            {
                float v = (py + 0.5f) / staticRes;
                // Canonical shader UV: lmUV.y = (ws.z - (-Height)) / Height -> ws.z = -(1 - v) * Height
                float worldY = (1f - v) * _worldHeight;
                float wz = -worldY;
                int ty = Mathf.Clamp(Mathf.FloorToInt(worldY), 0, _worldHeight - 1);
                int by = Mathf.Clamp(Mathf.FloorToInt(worldY / bucketSize), 0, bucketsY - 1);

                for (int px = 0; px < staticRes; px++)
                {
                    float u = (px + 0.5f) / staticRes;
                    float wx = u * _worldWidth;
                    int tx = Mathf.Clamp(Mathf.FloorToInt(wx), 0, _worldWidth - 1);
                    int bx = Mathf.Clamp(Mathf.FloorToInt(wx / bucketSize), 0, bucketsX - 1);
                    int bIdx = by * bucketsX + bx;

                    CartographyTile curTile = data.GetTile(tx, ty);

                    // 1. Canopy circles (uncapped, bucket-accelerated)
                    float canopyDarkness = 0f;
                    List<int> propIndices = canopyBuckets[bIdx];
                    if (propIndices != null)
                    {
                        for (int k = 0; k < propIndices.Count; k++)
                        {
                            CartographyPropPlacement prop = props[propIndices[k]];
                            float radius = Mathf.Max(0.5f, prop.CanopyRadius);
                            float dx = wx - prop.WorldPosition.x;
                            float dz = wz - prop.WorldPosition.z;
                            float distSq = dx * dx + dz * dz;
                            if (distSq >= radius * radius)
                            {
                                continue;
                            }

                            float dist = Mathf.Sqrt(distSq);
                            float t = Mathf.Clamp01((dist - radius * 0.25f) / (radius * 0.75f));
                            float smooth = t * t * (3f - 2f * t);
                            float blotch = (1f - smooth) * Mathf.Clamp01(prop.CanopyStrength);
                            if (blotch > canopyDarkness)
                            {
                                canopyDarkness = blotch;
                            }
                        }
                    }

                    // 2. Dynamic cliff overhang AO (lower ny = further North +Z) and shoreline distance field
                    float minLandDist = curTile.IsWater ? 10f : 0f;
                    float cliffAO = 0f;
                    bool checkLandDist = curTile.IsWater && waterBfsDist[tx, ty] <= 2;

                    for (int nyOff = -1; nyOff <= 1; nyOff++)
                    {
                        int ny = ty + nyOff;
                        if (ny < 0 || ny >= _worldHeight) continue;

                        for (int nxOff = -1; nxOff <= 1; nxOff++)
                        {
                            int nx = tx + nxOff;
                            if (nx < 0 || nx >= _worldWidth) continue;

                            CartographyTile nb = data.GetTile(nx, ny);
                            float cx = Mathf.Clamp(wx, nx, nx + 1f);
                            float cy = Mathf.Clamp(worldY, ny, ny + 1f);
                            float dx = wx - cx;
                            float dy = worldY - cy;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);

                            if (checkLandDist && !nb.IsWater && d < minLandDist)
                            {
                                minLandDist = d;
                            }

                            if (nb.Elevation > curTile.Elevation + 0.25f && d < 0.95f)
                            {
                                // In canonical coords, lower y is further North (+Z), casting stronger south-facing cliff AO
                                float bias = ny < ty ? 0.36f : 0.24f;
                                float cAO = (1f - d / 0.95f) * bias;
                                if (cAO > cliffAO) cliffAO = cAO;
                            }
                        }
                    }

                    canopyDarkness = Mathf.Clamp01(Mathf.Max(canopyDarkness, cliffAO));
                    if (curTile.IsWater && curTile.CobbleWeight > 0.4f)
                    {
                        canopyDarkness = Mathf.Max(canopyDarkness, 0.42f);
                    }
                    else if (curTile.IsWater && bridgeProps.Count > 0)
                    {
                        for (int bi = 0; bi < bridgeProps.Count; bi++)
                        {
                            CartographyPropPlacement bridge = props[bridgeProps[bi]];
                            float halfSpanZ = bridge.SizeMeters.y * 0.5f + 0.25f;
                            if (Mathf.Abs(wz - bridge.WorldPosition.z) <= halfSpanZ)
                            {
                                float halfW = bridge.SizeMeters.x * 0.5f;
                                float dx = Mathf.Abs(wx - bridge.WorldPosition.x);
                                if (dx < halfW + 0.25f)
                                {
                                    float spill = 1f - Mathf.Clamp01((dx - (halfW - 0.20f)) / 0.45f);
                                    canopyDarkness = Mathf.Max(canopyDarkness, 0.24f * spill);
                                }
                            }
                        }
                    }

                    float shoreProx = curTile.IsWater ? Mathf.Clamp01(1f - minLandDist / 0.55f) : 1f;

                    // 3. Static lantern pools (uncapped, bucket-accelerated)
                    float lanternGlow = 0f;
                    List<int> lanternIndices = lanternBuckets[bIdx];
                    if (lanternIndices != null)
                    {
                        for (int j = 0; j < lanternIndices.Count; j++)
                        {
                            CartographyLanternSource l = lanterns[lanternIndices[j]];
                            float radius = Mathf.Max(0.5f, l.Radius);
                            float dx = wx - l.WorldXZ.x;
                            float dz = wz - l.WorldXZ.y;
                            float distSq = dx * dx + dz * dz;
                            if (distSq >= radius * radius)
                            {
                                continue;
                            }

                            float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(distSq) / radius);
                            lanternGlow += falloff * falloff * Mathf.Clamp01(l.Intensity);
                        }
                    }

                    pixels[py * staticRes + px] = new Color(
                        1f - canopyDarkness,
                        shoreProx,
                        Mathf.Clamp01(lanternGlow),
                        1f);
                }
            }

            _worldStaticMap.SetPixels(pixels);
            _worldStaticMap.Apply(false, false);
            _configured = true;
        }

        public void UpdateLightmap(float simTimeSeconds)
        {
            if (!_configured || _worldStaticMap == null)
            {
                ConfigureFromWorldData(CartographyWorldData.BuildBywaterProofLayout());
            }

            Vector4 bounds = WorldBounds;
            Shader.SetGlobalVector(TopDownLightmapBoundsId, bounds);
            Shader.SetGlobalTexture(WorldStaticMapId, _worldStaticMap);

            bool hasGpu = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (hasGpu && EnsureGeneratorMaterial())
            {
                EnsureRenderTexture();
                _generatorMaterial.SetFloat(SimTimeId, simTimeSeconds);
                _generatorMaterial.SetVector(WorldSizeId, new Vector4(_worldWidth, _worldHeight, 0f, 0f));
                _generatorMaterial.SetColor("_CloudShadowColor", cloudShadowColor);
                _generatorMaterial.SetColor("_CanopyShadowColor", canopyShadowColor);
                _generatorMaterial.SetFloat("_CloudStrength", cloudStrength);
                _generatorMaterial.SetTexture(WorldStaticMapId, _worldStaticMap);

                RenderTexture prevActive = RenderTexture.active;
                Graphics.Blit(null, _lightmapRT, _generatorMaterial, 0);
                RenderTexture.active = prevActive;
                Shader.SetGlobalTexture(TopDownLightmapTexId, _lightmapRT);
            }
            else
            {
                EnsureCpuFallbackTexture();
                Shader.SetGlobalTexture(TopDownLightmapTexId, _fallbackTexture);
            }
        }

        private static int[,] ComputeShoreBfsDistances(CartographyWorldData data, int width, int height)
        {
            int[,] dist = new int[width, height];
            Queue<int> queue = new Queue<int>(width * height / 4);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!data.GetTile(x, y).IsWater)
                    {
                        dist[x, y] = 0;
                        queue.Enqueue(y * width + x);
                    }
                    else
                    {
                        dist[x, y] = int.MaxValue;
                    }
                }
            }

            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            while (queue.Count > 0)
            {
                int packed = queue.Dequeue();
                int cx = packed % width;
                int cy = packed / width;
                int nextDist = dist[cx, cy] + 1;

                for (int i = 0; i < 4; i++)
                {
                    int nx = cx + dx[i];
                    int ny = cy + dy[i];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    if (nextDist < dist[nx, ny])
                    {
                        dist[nx, ny] = nextDist;
                        queue.Enqueue(ny * width + nx);
                    }
                }
            }

            return dist;
        }

        private static void InsertIntoBuckets(
            List<int>[] buckets,
            int bucketsX,
            int bucketsY,
            float bucketSize,
            float wx,
            float wy,
            float radius,
            int itemIndex)
        {
            int minBx = Mathf.Clamp(Mathf.FloorToInt((wx - radius) / bucketSize), 0, bucketsX - 1);
            int maxBx = Mathf.Clamp(Mathf.FloorToInt((wx + radius) / bucketSize), 0, bucketsX - 1);
            int minBy = Mathf.Clamp(Mathf.FloorToInt((wy - radius) / bucketSize), 0, bucketsY - 1);
            int maxBy = Mathf.Clamp(Mathf.FloorToInt((wy + radius) / bucketSize), 0, bucketsY - 1);

            for (int by = minBy; by <= maxBy; by++)
            {
                for (int bx = minBx; bx <= maxBx; bx++)
                {
                    int idx = by * bucketsX + bx;
                    if (buckets[idx] == null)
                    {
                        buckets[idx] = new List<int>(8);
                    }
                    buckets[idx].Add(itemIndex);
                }
            }
        }

        private void EnsureRenderTexture()
        {
            int desiredRes = Mathf.Max(_worldWidth, _worldHeight) > 32 ? 512 : LightmapResolution;
            if (_lightmapRT != null && _lightmapRT.IsCreated() && _lightmapRT.width == desiredRes)
            {
                return;
            }

            if (_lightmapRT != null)
            {
                _lightmapRT.Release();
                DestroySafe(_lightmapRT);
                _lightmapRT = null;
            }

            RenderTextureFormat fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : RenderTextureFormat.ARGB32;

            _lightmapRT = new RenderTexture(desiredRes, desiredRes, 0, fmt, RenderTextureReadWrite.Linear)
            {
                name = "Bywater_TopDownLightmap_RT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            _lightmapRT.Create();
        }

        private bool EnsureGeneratorMaterial()
        {
            if (_generatorMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find(GeneratorShaderName);
            if (shader == null || !shader.isSupported)
            {
                return false;
            }

            _generatorMaterial = new Material(shader)
            {
                name = "Runtime_TopDownLightmapGen_Mat",
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
        }

        private void EnsureCpuFallbackTexture()
        {
            if (_fallbackTexture != null)
            {
                return;
            }

            _fallbackTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false, true)
            {
                name = "Bywater_TopDownLightmap_Fallback",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    pixels[y * 64 + x] = new Color(1f, 1f, 1f, 0f);
                }
            }
            _fallbackTexture.SetPixels(pixels);
            _fallbackTexture.Apply(false, false);
        }

        private void ReleaseResources()
        {
            if (_lightmapRT != null)
            {
                _lightmapRT.Release();
                DestroySafe(_lightmapRT);
                _lightmapRT = null;
            }

            if (_worldStaticMap != null)
            {
                DestroySafe(_worldStaticMap);
                _worldStaticMap = null;
            }

            if (_generatorMaterial != null)
            {
                DestroySafe(_generatorMaterial);
                _generatorMaterial = null;
            }

            if (_fallbackTexture != null)
            {
                DestroySafe(_fallbackTexture);
                _fallbackTexture = null;
            }

            _configured = false;
        }

        private static void DestroySafe(UnityEngine.Object obj)
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
