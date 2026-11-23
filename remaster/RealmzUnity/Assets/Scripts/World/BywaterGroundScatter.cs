using System;
using System.Collections.Generic;
using Realmz.Data;
using RealmzCore.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.World
{
    /// <summary>
    /// Deterministic GPU-instanced ground scatter (grass tufts, wildflowers, pebbles)
    /// for Bywater outdoor grass tiles and Foundry URP preview captures.
    /// </summary>
    public sealed class BywaterGroundScatter : MonoBehaviour
    {
        public const int MaxBatchSize = 1023;
        public const string GrassTuftShaderName = "Realmz/GrassTuft";

        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");

        public Material grassTuftMaterial;
        public Material wildflowerTuftMaterial;
        public Material pebbleMaterial;

        public int ActiveTuftCount { get; private set; }
        public int ActiveFlowerCount { get; private set; }
        public int ActivePebbleCount { get; private set; }

        private sealed class ScatterBatch
        {
            public Matrix4x4[] Matrices;
            public Vector4[] Colors;
            public int Count;
        }

        private readonly List<ScatterBatch> _grassBatches = new List<ScatterBatch>(4);
        private readonly List<ScatterBatch> _flowerBatches = new List<ScatterBatch>(2);
        private readonly List<ScatterBatch> _pebbleBatches = new List<ScatterBatch>(2);
        private readonly HashSet<long> _occupiedTiles = new HashSet<long>();

        private readonly List<Matrix4x4> _scratchGrassMatrices = new List<Matrix4x4>(2048);
        private readonly List<Vector4> _scratchGrassColors = new List<Vector4>(2048);
        private readonly List<Matrix4x4> _scratchFlowerMatrices = new List<Matrix4x4>(256);
        private readonly List<Vector4> _scratchFlowerColors = new List<Vector4>(256);
        private readonly List<Matrix4x4> _scratchPebbleMatrices = new List<Matrix4x4>(256);
        private readonly List<Vector4> _scratchPebbleColors = new List<Vector4>(256);

        private Mesh _grassMesh;
        private Mesh _flowerMesh;
        private Mesh _pebbleMesh;
        private MaterialPropertyBlock _propBlock;

        private bool _ownsGrassMat;
        private bool _ownsFlowerMat;
        private bool _ownsPebbleMat;

        private void Awake()
        {
            EnsureMeshesAndBlock();
        }

        private void OnDestroy()
        {
            Clear();

            if (_grassMesh != null)
            {
                DestroyImmediateIfEditor(_grassMesh);
                _grassMesh = null;
            }

            if (_flowerMesh != null)
            {
                DestroyImmediateIfEditor(_flowerMesh);
                _flowerMesh = null;
            }

            if (_pebbleMesh != null)
            {
                DestroyImmediateIfEditor(_pebbleMesh);
                _pebbleMesh = null;
            }

            if (_ownsGrassMat && grassTuftMaterial != null)
            {
                DestroyImmediateIfEditor(grassTuftMaterial);
                grassTuftMaterial = null;
            }

            if (_ownsFlowerMat && wildflowerTuftMaterial != null)
            {
                DestroyImmediateIfEditor(wildflowerTuftMaterial);
                wildflowerTuftMaterial = null;
            }

            if (_ownsPebbleMat && pebbleMaterial != null)
            {
                DestroyImmediateIfEditor(pebbleMaterial);
                pebbleMaterial = null;
            }
        }

        public void Initialize(BywaterAssetCatalog catalog)
        {
            EnsureMeshesAndBlock();
            EnsureDefaultMaterials(catalog);
        }

        public void SetMaterials(Material grass, Material flower = null, Material pebble = null)
        {
            if (grass != null)
            {
                if (_ownsGrassMat && grassTuftMaterial != null && grassTuftMaterial != grass)
                {
                    DestroyImmediateIfEditor(grassTuftMaterial);
                }

                grassTuftMaterial = grass;
                grassTuftMaterial.enableInstancing = true;
                _ownsGrassMat = false;
            }

            if (flower != null)
            {
                if (_ownsFlowerMat && wildflowerTuftMaterial != null && wildflowerTuftMaterial != flower)
                {
                    DestroyImmediateIfEditor(wildflowerTuftMaterial);
                }

                wildflowerTuftMaterial = flower;
                wildflowerTuftMaterial.enableInstancing = true;
                _ownsFlowerMat = false;
            }

            if (pebble != null)
            {
                if (_ownsPebbleMat && pebbleMaterial != null && pebbleMaterial != pebble)
                {
                    DestroyImmediateIfEditor(pebbleMaterial);
                }

                pebbleMaterial = pebble;
                pebbleMaterial.enableInstancing = true;
                _ownsPebbleMat = false;
            }

            EnsureDefaultMaterials(null);
        }

        public static Material CreateDefaultGrassTuftMaterial()
        {
            Shader shader = Shader.Find(GrassTuftShaderName) ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(shader)
            {
                name = "BywaterGrassTuft_Mat",
                enableInstancing = true
            };
            if (mat.HasProperty(BaseColorPropId))
            {
                mat.SetColor(BaseColorPropId, new Color(0.36f, 0.56f, 0.24f, 1f));
            }
            if (mat.HasProperty("_TipColor"))
            {
                mat.SetColor("_TipColor", new Color(0.70f, 0.84f, 0.42f, 1f));
            }
            if (mat.HasProperty("_WindSpeed"))
            {
                mat.SetFloat("_WindSpeed", 2.2f);
            }
            if (mat.HasProperty("_WindStrength"))
            {
                mat.SetFloat("_WindStrength", 0.08f);
            }
            return mat;
        }

        public static Material CreateDefaultFlowerMaterial()
        {
            Shader shader = Shader.Find(GrassTuftShaderName) ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(shader)
            {
                name = "BywaterWildflowerTuft_Mat",
                enableInstancing = true
            };
            if (mat.HasProperty(BaseColorPropId))
            {
                mat.SetColor(BaseColorPropId, new Color(0.92f, 0.80f, 0.34f, 1f));
            }
            if (mat.HasProperty("_TipColor"))
            {
                mat.SetColor("_TipColor", new Color(1.15f, 1.10f, 1.02f, 1f));
            }
            if (mat.HasProperty("_WindSpeed"))
            {
                mat.SetFloat("_WindSpeed", 2.4f);
            }
            if (mat.HasProperty("_WindStrength"))
            {
                mat.SetFloat("_WindStrength", 0.065f);
            }
            return mat;
        }

        public static Material CreateDefaultPebbleMaterial()
        {
            Shader shader = Shader.Find(GrassTuftShaderName) ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(shader)
            {
                name = "BywaterPebble_Mat",
                enableInstancing = true
            };
            if (mat.HasProperty(BaseColorPropId))
            {
                mat.SetColor(BaseColorPropId, new Color(0.54f, 0.52f, 0.48f, 1f));
            }
            if (mat.HasProperty("_TipColor"))
            {
                mat.SetColor("_TipColor", new Color(1.08f, 1.06f, 1.02f, 1f));
            }
            if (mat.HasProperty("_WindStrength"))
            {
                mat.SetFloat("_WindStrength", 0f);
            }
            if (mat.HasProperty("_Translucency"))
            {
                mat.SetFloat("_Translucency", 0f);
            }
            return mat;
        }

        public void RebuildForMap(
            TileMapData map,
            GridCoord focusCenter,
            int windowRadius = 16,
            bool suppressCombatCenter = false,
            GridCoord combatOrigin = default,
            int combatW = 0,
            int combatH = 0)
        {
            Clear();

            if (map == null || map.IsDungeon)
            {
                return;
            }

            EnsureMeshesAndBlock();
            EnsureDefaultMaterials(null);

            int radius = Mathf.Max(1, windowRadius);
            int minX = Mathf.Max(0, focusCenter.X - radius);
            int maxX = Mathf.Min(map.Width - 1, focusCenter.X + radius);
            int minY = Mathf.Max(0, focusCenter.Y - radius);
            int maxY = Mathf.Min(map.Height - 1, focusCenter.Y + radius);

            _scratchGrassMatrices.Clear();
            _scratchGrassColors.Clear();
            _scratchFlowerMatrices.Clear();
            _scratchFlowerColors.Clear();
            _scratchPebbleMatrices.Clear();
            _scratchPebbleColors.Clear();

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (suppressCombatCenter && combatW > 0 && combatH > 0
                        && x >= combatOrigin.X && x < combatOrigin.X + combatW
                        && y >= combatOrigin.Y && y < combatOrigin.Y + combatH)
                    {
                        continue;
                    }

                    BywaterTileKind kind = BywaterTileClassifier.Classify(map, x, y);
                    if (kind != BywaterTileKind.Grass)
                    {
                        continue;
                    }

                    if (map.GetPortalAt(x, y) != null)
                    {
                        continue;
                    }

                    if (BywaterTileClassifier.ClassifyOutdoorProp(map, x, y, out _) != BywaterOutdoorPropKind.None)
                    {
                        continue;
                    }

                    int tileHash = BywaterTileClassifier.DeterministicTileHash(x, y);
                    // Leave ~14% of grass tiles clear for natural breathing room
                    if ((tileHash % 7) == 0)
                    {
                        continue;
                    }

                    bool placedAny = false;
                    int tuftCount = 2 + (tileHash % 3); // 2 to 4 tufts
                    for (int i = 0; i < tuftCount; i++)
                    {
                        int h = MixHash(tileHash, i + 1);
                        float ox = ((h & 0xFF) / 255f - 0.5f) * 0.78f;
                        float oz = (((h >> 8) & 0xFF) / 255f - 0.5f) * 0.78f;
                        float yaw = (((h >> 16) & 0xFF) / 255f) * 360f;
                        float heightScale = 0.14f + (((h >> 24) & 0x3F) / 63f) * 0.11f; // 0.14m - 0.25m
                        float widthScale = 0.17f + (((h >> 20) & 0x0F) / 15f) * 0.08f;

                        Vector3 pos = new Vector3(x + ox, 0f, -y + oz);
                        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                        Vector3 scale = new Vector3(widthScale, heightScale, widthScale);

                        _scratchGrassMatrices.Add(Matrix4x4.TRS(pos, rot, scale));
                        _scratchGrassColors.Add(SampleMacroGrassColor(pos.x, pos.z, h));
                        placedAny = true;
                    }

                    // Occasional wildflower cluster (~14% of eligible grass tiles)
                    if (((tileHash >> 3) % 7) == 0)
                    {
                        int fh = MixHash(tileHash, 97);
                        float fx = ((fh & 0xFF) / 255f - 0.5f) * 0.66f;
                        float fz = (((fh >> 8) & 0xFF) / 255f - 0.5f) * 0.66f;
                        float fyaw = (((fh >> 16) & 0xFF) / 255f) * 360f;
                        float fScale = 0.16f + (((fh >> 24) & 0x1F) / 31f) * 0.07f;

                        Vector3 fPos = new Vector3(x + fx, 0f, -y + fz);
                        _scratchFlowerMatrices.Add(Matrix4x4.TRS(fPos, Quaternion.Euler(0f, fyaw, 0f), Vector3.one * fScale));
                        _scratchFlowerColors.Add(PickWildflowerColor(fh));
                        placedAny = true;
                    }

                    // Occasional small pebble cluster (~10% of eligible grass tiles)
                    if (((tileHash >> 7) % 10) == 0)
                    {
                        int ph = MixHash(tileHash, 193);
                        float px = ((ph & 0xFF) / 255f - 0.5f) * 0.68f;
                        float pz = (((ph >> 8) & 0xFF) / 255f - 0.5f) * 0.68f;
                        float pyaw = (((ph >> 16) & 0xFF) / 255f) * 360f;
                        float pWidth = 0.08f + (((ph >> 20) & 0x0F) / 15f) * 0.06f;
                        float pHeight = 0.05f + (((ph >> 24) & 0x0F) / 15f) * 0.04f;

                        Vector3 pPos = new Vector3(x + px, 0f, -y + pz);
                        _scratchPebbleMatrices.Add(Matrix4x4.TRS(pPos, Quaternion.Euler(0f, pyaw, 0f), new Vector3(pWidth, pHeight, pWidth)));
                        _scratchPebbleColors.Add(PickPebbleColor(ph));
                        placedAny = true;
                    }

                    if (placedAny)
                    {
                        _occupiedTiles.Add(PackTileKey(x, y));
                    }
                }
            }

            BuildBatches(_scratchGrassMatrices, _scratchGrassColors, _grassBatches);
            BuildBatches(_scratchFlowerMatrices, _scratchFlowerColors, _flowerBatches);
            BuildBatches(_scratchPebbleMatrices, _scratchPebbleColors, _pebbleBatches);

            ActiveTuftCount = _scratchGrassMatrices.Count;
            ActiveFlowerCount = _scratchFlowerMatrices.Count;
            ActivePebbleCount = _scratchPebbleMatrices.Count;
        }

        public void Clear()
        {
            _grassBatches.Clear();
            _flowerBatches.Clear();
            _pebbleBatches.Clear();
            _occupiedTiles.Clear();
            ActiveTuftCount = 0;
            ActiveFlowerCount = 0;
            ActivePebbleCount = 0;
        }

        public bool HasScatterAtTile(int x, int y)
        {
            return _occupiedTiles.Contains(PackTileKey(x, y));
        }

        private void LateUpdate()
        {
            if (!SystemInfo.supportsInstancing)
            {
                return;
            }

            EnsureMeshesAndBlock();
            DrawBatchList(_grassMesh, grassTuftMaterial, _grassBatches);
            DrawBatchList(_flowerMesh, wildflowerTuftMaterial, _flowerBatches);
            DrawBatchList(_pebbleMesh, pebbleMaterial, _pebbleBatches);
        }

        private void DrawBatchList(Mesh mesh, Material mat, List<ScatterBatch> batches)
        {
            if (mesh == null || mat == null || batches == null || batches.Count == 0)
            {
                return;
            }

            if (!mat.enableInstancing)
            {
                mat.enableInstancing = true;
            }

            for (int i = 0; i < batches.Count; i++)
            {
                ScatterBatch batch = batches[i];
                if (batch.Count <= 0)
                {
                    continue;
                }

                _propBlock.Clear();
                _propBlock.SetVectorArray(BaseColorPropId, batch.Colors);
                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    mat,
                    batch.Matrices,
                    batch.Count,
                    _propBlock,
                    ShadowCastingMode.Off,
                    true,
                    gameObject.layer);
            }
        }

        public static Mesh BuildGrassTuftMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "Realmz_GrassTuftMesh"
            };

            const int bladeCount = 5;
            List<Vector3> verts = new List<Vector3>(bladeCount * 5);
            List<Vector3> norms = new List<Vector3>(bladeCount * 5);
            List<Vector2> uvs = new List<Vector2>(bladeCount * 5);
            List<int> tris = new List<int>(bladeCount * 9);

            for (int b = 0; b < bladeCount; b++)
            {
                float angle = (b / (float)bladeCount) * Mathf.PI * 2f + 0.35f;
                float radialOffset = (b % 2 == 0) ? 0.08f : 0.14f;
                float bladeHeight = 0.82f + (b % 3) * 0.11f;
                float baseHalfW = 0.085f;
                float midHalfW = 0.055f;

                Vector3 dirXZ = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 tangentXZ = new Vector3(-dirXZ.z, 0f, dirXZ.x);
                Vector3 rootCenter = dirXZ * radialOffset;
                Vector3 midCenter = dirXZ * (radialOffset + 0.12f) + Vector3.up * (bladeHeight * 0.52f);
                Vector3 tipPos = dirXZ * (radialOffset + 0.28f) + Vector3.up * bladeHeight;

                Vector3 v0 = rootCenter - tangentXZ * baseHalfW;
                Vector3 v1 = rootCenter + tangentXZ * baseHalfW;
                Vector3 v2 = midCenter - tangentXZ * midHalfW;
                Vector3 v3 = midCenter + tangentXZ * midHalfW;
                Vector3 v4 = tipPos;

                int baseIdx = verts.Count;
                verts.Add(v0);
                verts.Add(v1);
                verts.Add(v2);
                verts.Add(v3);
                verts.Add(v4);

                Vector3 bladeNorm = Vector3.Slerp(Vector3.up, dirXZ, 0.28f).normalized;
                for (int k = 0; k < 5; k++)
                {
                    norms.Add(bladeNorm);
                }

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0.15f, 0.52f));
                uvs.Add(new Vector2(0.85f, 0.52f));
                uvs.Add(new Vector2(0.5f, 1f));

                tris.Add(baseIdx + 0);
                tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 1);

                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 3);

                tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 4);
                tris.Add(baseIdx + 3);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh BuildFlowerTuftMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "Realmz_FlowerTuftMesh"
            };

            List<Vector3> verts = new List<Vector3>(32);
            List<Vector3> norms = new List<Vector3>(32);
            List<Vector2> uvs = new List<Vector2>(32);
            List<int> tris = new List<int>(48);

            const int stemCount = 3;
            for (int s = 0; s < stemCount; s++)
            {
                float angle = (s / (float)stemCount) * Mathf.PI * 2f + 0.5f;
                Vector3 dirXZ = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 tangentXZ = new Vector3(-dirXZ.z, 0f, dirXZ.x);

                float stemHeight = 0.78f + s * 0.10f;
                Vector3 root = dirXZ * 0.07f;
                Vector3 top = dirXZ * 0.22f + Vector3.up * stemHeight;
                float stemHalfW = 0.045f;

                int baseIdx = verts.Count;
                verts.Add(root - tangentXZ * stemHalfW);
                verts.Add(root + tangentXZ * stemHalfW);
                verts.Add(top - tangentXZ * (stemHalfW * 0.65f));
                verts.Add(top + tangentXZ * (stemHalfW * 0.65f));

                Vector3 stemNorm = Vector3.Slerp(Vector3.up, dirXZ, 0.25f).normalized;
                for (int k = 0; k < 4; k++)
                {
                    norms.Add(stemNorm);
                }

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 0.75f));
                uvs.Add(new Vector2(1f, 0.75f));

                tris.Add(baseIdx + 0);
                tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 3);

                // Blossom cap (tilted diamond at the top of the stem)
                float blossomR = 0.16f;
                int capIdx = verts.Count;
                Vector3 tiltDir = Vector3.Slerp(Vector3.up, dirXZ, 0.2f).normalized;
                Vector3 capA = tangentXZ * blossomR;
                Vector3 capB = Vector3.Cross(tiltDir, tangentXZ).normalized * blossomR;

                verts.Add(top - capA - capB);
                verts.Add(top + capA - capB);
                verts.Add(top + capA + capB);
                verts.Add(top - capA + capB);

                for (int k = 0; k < 4; k++)
                {
                    norms.Add(tiltDir);
                }

                uvs.Add(new Vector2(0f, 0.95f));
                uvs.Add(new Vector2(1f, 0.95f));
                uvs.Add(new Vector2(1f, 1.0f));
                uvs.Add(new Vector2(0f, 1.0f));

                tris.Add(capIdx + 0);
                tris.Add(capIdx + 2);
                tris.Add(capIdx + 1);
                tris.Add(capIdx + 0);
                tris.Add(capIdx + 3);
                tris.Add(capIdx + 2);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh BuildPebbleMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "Realmz_PebbleMesh"
            };

            List<Vector3> verts = new List<Vector3>(24);
            List<Vector3> norms = new List<Vector3>(24);
            List<Vector2> uvs = new List<Vector2>(24);
            List<int> tris = new List<int>(48);

            // Two compact low-poly faceted stones per cluster
            AppendFacetedStone(verts, norms, uvs, tris, new Vector3(-0.18f, 0f, -0.08f), 0.42f, 0.34f);
            AppendFacetedStone(verts, norms, uvs, tris, new Vector3(0.22f, 0f, 0.14f), 0.28f, 0.24f);

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject CreatePreviewScatterRoot(
            Transform parent,
            Vector2 minXZ,
            Vector2 maxXZ,
            Material grassMat,
            Material flowerMat,
            Material pebbleMat,
            Func<Vector3, bool> exclusionPredicate = null)
        {
            GameObject root = new GameObject("PreviewGroundScatter");
            if (parent != null)
            {
                root.transform.SetParent(parent, false);
            }

            Mesh baseGrassMesh = BuildGrassTuftMesh();
            Mesh baseFlowerMesh = BuildFlowerTuftMesh();
            Mesh basePebbleMesh = BuildPebbleMesh();

            List<CombineInstance> grassCombines = new List<CombineInstance>(256);
            List<CombineInstance> flowerCombines = new List<CombineInstance>(48);
            List<CombineInstance> pebbleCombines = new List<CombineInstance>(48);

            float step = 0.56f;
            int ix = 0;
            for (float x = minXZ.x + 0.35f; x <= maxXZ.x - 0.35f; x += step, ix++)
            {
                int iz = 0;
                for (float z = minXZ.y + 0.35f; z <= maxXZ.y - 0.35f; z += step, iz++)
                {
                    int hash = BywaterTileClassifier.DeterministicTileHash(ix + 17, iz + 41);
                    if ((hash % 5) == 0)
                    {
                        continue;
                    }

                    float jx = ((hash & 0xFF) / 255f - 0.5f) * 0.38f;
                    float jz = (((hash >> 8) & 0xFF) / 255f - 0.5f) * 0.38f;
                    Vector3 pos = new Vector3(x + jx, 0f, z + jz);

                    if (exclusionPredicate != null && exclusionPredicate(pos))
                    {
                        continue;
                    }

                    float yaw = (((hash >> 16) & 0xFF) / 255f) * 360f;
                    float hScale = 0.15f + (((hash >> 24) & 0x1F) / 31f) * 0.11f;
                    float wScale = 0.18f + (((hash >> 20) & 0x0F) / 15f) * 0.08f;

                    grassCombines.Add(new CombineInstance
                    {
                        mesh = baseGrassMesh,
                        transform = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(wScale, hScale, wScale))
                    });

                    if ((hash % 11) == 0)
                    {
                        int fh = MixHash(hash, 73);
                        Vector3 fPos = pos + new Vector3(0.14f, 0f, -0.10f);
                        if (exclusionPredicate == null || !exclusionPredicate(fPos))
                        {
                            float fScale = 0.17f + (((fh >> 20) & 0x0F) / 15f) * 0.06f;
                            flowerCombines.Add(new CombineInstance
                            {
                                mesh = baseFlowerMesh,
                                transform = Matrix4x4.TRS(fPos, Quaternion.Euler(0f, yaw + 35f, 0f), Vector3.one * fScale)
                            });
                        }
                    }

                    if ((hash % 15) == 0)
                    {
                        int ph = MixHash(hash, 139);
                        Vector3 pPos = pos + new Vector3(-0.14f, 0f, 0.12f);
                        if (exclusionPredicate == null || !exclusionPredicate(pPos))
                        {
                            float pw = 0.09f + (((ph >> 16) & 0x0F) / 15f) * 0.05f;
                            float phScale = 0.05f + (((ph >> 20) & 0x0F) / 15f) * 0.03f;
                            pebbleCombines.Add(new CombineInstance
                            {
                                mesh = basePebbleMesh,
                                transform = Matrix4x4.TRS(pPos, Quaternion.Euler(0f, yaw + 72f, 0f), new Vector3(pw, phScale, pw))
                            });
                        }
                    }
                }
            }

            CreateCombinedChild(root.transform, "PreviewGrassScatter", grassCombines, grassMat ?? CreateDefaultGrassTuftMaterial());
            CreateCombinedChild(root.transform, "PreviewFlowerScatter", flowerCombines, flowerMat ?? CreateDefaultFlowerMaterial());
            CreateCombinedChild(root.transform, "PreviewPebbleScatter", pebbleCombines, pebbleMat ?? CreateDefaultPebbleMaterial());

            DestroyImmediateIfEditor(baseGrassMesh);
            DestroyImmediateIfEditor(baseFlowerMesh);
            DestroyImmediateIfEditor(basePebbleMesh);

            return root;
        }

        private static void CreateCombinedChild(
            Transform parent,
            string childName,
            List<CombineInstance> combines,
            Material material)
        {
            if (combines == null || combines.Count == 0 || material == null)
            {
                return;
            }

            Mesh combined = new Mesh
            {
                name = childName + "_Mesh"
            };
            combined.CombineMeshes(combines.ToArray(), true, true);
            combined.RecalculateBounds();

            GameObject go = new GameObject(childName);
            go.transform.SetParent(parent, false);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = combined;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        private static void AppendFacetedStone(
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector2> uvs,
            List<int> tris,
            Vector3 center,
            float radius,
            float height)
        {
            int baseIdx = verts.Count;
            const int sides = 5;

            for (int i = 0; i < sides; i++)
            {
                float a = (i / (float)sides) * Mathf.PI * 2f;
                float r = radius * (i % 2 == 0 ? 1.0f : 0.85f);
                Vector3 p = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                verts.Add(p);
                norms.Add(new Vector3(Mathf.Cos(a), 0.5f, Mathf.Sin(a)).normalized);
                uvs.Add(new Vector2(i / (float)sides, 0f));
            }

            Vector3 apex = center + new Vector3(0f, height, 0f);
            int apexIdx = verts.Count;
            verts.Add(apex);
            norms.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 1f));

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                tris.Add(baseIdx + i);
                tris.Add(apexIdx);
                tris.Add(baseIdx + next);
            }
        }

        private void EnsureMeshesAndBlock()
        {
            if (_grassMesh == null)
            {
                _grassMesh = BuildGrassTuftMesh();
            }

            if (_flowerMesh == null)
            {
                _flowerMesh = BuildFlowerTuftMesh();
            }

            if (_pebbleMesh == null)
            {
                _pebbleMesh = BuildPebbleMesh();
            }

            if (_propBlock == null)
            {
                _propBlock = new MaterialPropertyBlock();
            }
        }

        private void EnsureDefaultMaterials(BywaterAssetCatalog catalog)
        {
            if (grassTuftMaterial == null)
            {
                grassTuftMaterial = CreateDefaultGrassTuftMaterial();
                _ownsGrassMat = true;
            }
            else if (!grassTuftMaterial.enableInstancing)
            {
                grassTuftMaterial.enableInstancing = true;
            }

            if (wildflowerTuftMaterial == null)
            {
                wildflowerTuftMaterial = CreateDefaultFlowerMaterial();
                _ownsFlowerMat = true;
            }
            else if (!wildflowerTuftMaterial.enableInstancing)
            {
                wildflowerTuftMaterial.enableInstancing = true;
            }

            if (pebbleMaterial == null)
            {
                pebbleMaterial = CreateDefaultPebbleMaterial();
                _ownsPebbleMat = true;
            }
            else if (!pebbleMaterial.enableInstancing)
            {
                pebbleMaterial.enableInstancing = true;
            }
        }

        private static void BuildBatches(List<Matrix4x4> matrices, List<Vector4> colors, List<ScatterBatch> targetBatches)
        {
            targetBatches.Clear();
            int total = matrices.Count;
            for (int offset = 0; offset < total; offset += MaxBatchSize)
            {
                int count = Mathf.Min(MaxBatchSize, total - offset);
                ScatterBatch batch = new ScatterBatch
                {
                    Matrices = new Matrix4x4[count],
                    Colors = new Vector4[count],
                    Count = count
                };

                for (int i = 0; i < count; i++)
                {
                    batch.Matrices[i] = matrices[offset + i];
                    batch.Colors[i] = colors[offset + i];
                }

                targetBatches.Add(batch);
            }
        }

        private static Vector4 SampleMacroGrassColor(float worldX, float worldZ, int hash)
        {
            // Match Realmz/TerrainBlend world-space macro modulation (_MacroScale = 0.085, _MacroStrength = 0.15)
            float n1 = Mathf.PerlinNoise(worldX * 0.085f + 13.7f, worldZ * 0.085f + 29.3f);
            float n2 = Mathf.PerlinNoise(worldX * 0.181f + 7.4f, worldZ * 0.181f + 3.1f);
            float macro = ((n1 * 0.67f + n2 * 0.33f) * 2f - 1f) * 0.15f;
            float micro = ((hash & 0x1F) / 31f - 0.5f) * 0.08f;
            float mul = Mathf.Clamp(1.0f + macro + micro, 0.78f, 1.22f);
            return new Vector4(0.36f * mul, 0.56f * mul, 0.24f * mul, 1f);
        }

        private static Vector4 PickWildflowerColor(int hash)
        {
            switch (Mathf.Abs(hash) % 4)
            {
                case 0:
                    return new Vector4(0.95f, 0.82f, 0.28f, 1f); // Buttercup gold
                case 1:
                    return new Vector4(0.96f, 0.94f, 0.85f, 1f); // Cream daisy
                case 2:
                    return new Vector4(0.76f, 0.64f, 0.92f, 1f); // Meadow lavender
                default:
                    return new Vector4(0.92f, 0.56f, 0.50f, 1f); // Soft coral blossom
            }
        }

        private static Vector4 PickPebbleColor(int hash)
        {
            float shade = 0.44f + ((hash & 0x1F) / 31f) * 0.20f;
            return new Vector4(shade * 1.04f, shade, shade * 0.94f, 1f);
        }

        private static int MixHash(int h, int salt)
        {
            return unchecked((int)(((uint)(h ^ (salt * 73856093)) * 2654435761u) & 0x7FFFFFFFu));
        }

        private static long PackTileKey(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        private static void DestroyImmediateIfEditor(UnityEngine.Object obj)
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
