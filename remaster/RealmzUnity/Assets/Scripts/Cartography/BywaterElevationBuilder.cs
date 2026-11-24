using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.Cartography
{
    /// <summary>
    /// Generates the stepped heightfield terrain mesh at canonical coordinates
    /// (x + 0.5, elev, -(y + 0.5)), welding contiguous equal-elevation plateaus,
    /// emitting vertical cliff faces (cliff_rock via TEXCOORD1.x), baking crease AO
    /// (TEXCOORD1.y), attaching a MeshCollider, and building ShallowWaterPlane.
    /// </summary>
    public sealed class BywaterElevationBuilder : MonoBehaviour
    {
        public const int SubdivisionsPerTile = 2;

        [Header("Materials")]
        public Material terrainMaterial;
        public Material waterMaterial;

        [Header("Generated References")]
        public MeshFilter terrainMeshFilter;
        public MeshRenderer terrainMeshRenderer;
        public MeshCollider terrainMeshCollider;
        public GameObject waterPlaneObject;

        public int LastCliffTriangleCount { get; private set; }
        public Mesh LastBuiltMesh { get; private set; }

        private Mesh _ownedTerrainMesh;
        private Mesh _ownedWaterMesh;
        private CartographyWorldData _lastWorldData;

        private void OnDestroy()
        {
            if (_ownedTerrainMesh != null)
            {
                if (terrainMeshFilter != null && terrainMeshFilter.sharedMesh == _ownedTerrainMesh)
                {
                    terrainMeshFilter.sharedMesh = null;
                }
                if (terrainMeshCollider != null && terrainMeshCollider.sharedMesh == _ownedTerrainMesh)
                {
                    terrainMeshCollider.sharedMesh = null;
                }
                DestroySafe(_ownedTerrainMesh);
                _ownedTerrainMesh = null;
            }

            if (_ownedWaterMesh != null)
            {
                if (waterPlaneObject != null)
                {
                    MeshFilter wf = waterPlaneObject.GetComponent<MeshFilter>();
                    if (wf != null && wf.sharedMesh == _ownedWaterMesh)
                    {
                        wf.sharedMesh = null;
                    }
                }
                DestroySafe(_ownedWaterMesh);
                _ownedWaterMesh = null;
            }
        }

        public void Rebuild(CartographyWorldData data)
        {
            if (data == null)
            {
                data = CartographyWorldData.BuildBywaterProofLayout();
            }

            _lastWorldData = data;

            if (terrainMeshFilter == null)
            {
                terrainMeshFilter = GetComponent<MeshFilter>();
                if (terrainMeshFilter == null) terrainMeshFilter = gameObject.AddComponent<MeshFilter>();
            }
            if (terrainMeshRenderer == null)
            {
                terrainMeshRenderer = GetComponent<MeshRenderer>();
                if (terrainMeshRenderer == null) terrainMeshRenderer = gameObject.AddComponent<MeshRenderer>();
            }
            if (terrainMeshCollider == null)
            {
                terrainMeshCollider = GetComponent<MeshCollider>();
                if (terrainMeshCollider == null) terrainMeshCollider = gameObject.AddComponent<MeshCollider>();
            }

            if (_ownedTerrainMesh != null)
            {
                if (terrainMeshFilter.sharedMesh == _ownedTerrainMesh) terrainMeshFilter.sharedMesh = null;
                if (terrainMeshCollider.sharedMesh == _ownedTerrainMesh) terrainMeshCollider.sharedMesh = null;
                DestroySafe(_ownedTerrainMesh);
                _ownedTerrainMesh = null;
            }

            Mesh builtMesh = BuildTerrainMesh(data, out int cliffTris);
            _ownedTerrainMesh = builtMesh;
            LastCliffTriangleCount = cliffTris;
            LastBuiltMesh = builtMesh;

            terrainMeshFilter.sharedMesh = builtMesh;
            terrainMeshCollider.sharedMesh = builtMesh;
            terrainMeshRenderer.shadowCastingMode = ShadowCastingMode.On;
            terrainMeshRenderer.receiveShadows = true;

            if (terrainMaterial != null)
            {
                terrainMeshRenderer.sharedMaterial = terrainMaterial;
            }

            EnsureWaterPlane(data);
        }

        public void EnsureWaterPlane()
        {
            EnsureWaterPlane(_lastWorldData);
        }

        public void EnsureWaterPlane(CartographyWorldData data)
        {
            if (data == null)
            {
                data = CartographyWorldData.BuildBywaterProofLayout();
            }

            Transform existing = transform.Find("ShallowWaterPlane");
            if (existing != null)
            {
                waterPlaneObject = existing.gameObject;
            }
            else if (waterPlaneObject == null)
            {
                waterPlaneObject = new GameObject("ShallowWaterPlane");
                waterPlaneObject.transform.SetParent(transform, false);
            }

            waterPlaneObject.transform.localPosition = new Vector3(
                data.Width * 0.5f,
                data.WaterSurfaceHeight,
                -data.Height * 0.5f);
            waterPlaneObject.transform.localRotation = Quaternion.identity;
            waterPlaneObject.transform.localScale = Vector3.one;

            MeshFilter wf = waterPlaneObject.GetComponent<MeshFilter>();
            if (wf == null) wf = waterPlaneObject.AddComponent<MeshFilter>();
            MeshRenderer wr = waterPlaneObject.GetComponent<MeshRenderer>();
            if (wr == null) wr = waterPlaneObject.AddComponent<MeshRenderer>();

            if (_ownedWaterMesh != null)
            {
                if (wf.sharedMesh == _ownedWaterMesh) wf.sharedMesh = null;
                DestroySafe(_ownedWaterMesh);
                _ownedWaterMesh = null;
            }

            _ownedWaterMesh = BuildWaterPlaneMesh(data.Width, data.Height);
            wf.sharedMesh = _ownedWaterMesh;

            wr.shadowCastingMode = ShadowCastingMode.Off;
            wr.receiveShadows = true;
            if (waterMaterial != null)
            {
                wr.sharedMaterial = waterMaterial;
            }
        }

        private static void DestroySafe(Object obj)
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

        public static Mesh BuildTerrainMesh(CartographyWorldData data, out int cliffTriangleCount)
        {
            if (data == null)
            {
                data = CartographyWorldData.BuildBywaterProofLayout();
            }

            int w = data.Width;
            int h = data.Height;
            const int sub = SubdivisionsPerTile;
            const float step = 1.0f / sub;
            const float chamferDrop = 0.06f;

            int estimatedQuads = w * h * sub * sub;
            List<Vector3> verts = new List<Vector3>(estimatedQuads + w * h * 4);
            List<Vector3> norms = new List<Vector3>(estimatedQuads + w * h * 4);
            List<Vector4> tangs = new List<Vector4>(estimatedQuads + w * h * 4);
            List<Color> colors = new List<Color>(estimatedQuads + w * h * 4);
            List<Vector2> uv0 = new List<Vector2>(estimatedQuads + w * h * 4);
            List<Vector2> uv1 = new List<Vector2>(estimatedQuads + w * h * 4);
            List<int> tris = new List<int>(estimatedQuads * 6 + w * h * 12);

            // Cache welded plateau vertices by (subgridX, subgridZ, quantizedElevation)
            Dictionary<long, int> plateauVertCache = new Dictionary<long, int>(estimatedQuads);

            // 1. Horizontal plateau surface quads (welded across contiguous equal-elevation tiles)
            for (int ty = 0; ty < h; ty++)
            {
                for (int tx = 0; tx < w; tx++)
                {
                    CartographyTile tile = data.GetTile(tx, ty);
                    float yElev = tile.Elevation;
                    int elevKey = Mathf.RoundToInt(yElev * 1000f);

                    for (int sy = 0; sy < sub; sy++)
                    {
                        for (int sx = 0; sx < sub; sx++)
                        {
                            int gx0 = tx * sub + sx;
                            int gx1 = gx0 + 1;
                            int gzNorth = ty * sub + sy;
                            int gzSouth = gzNorth + 1;

                            int i00 = GetOrAddPlateauVertex(data, w, h, gx0, gzSouth, step, yElev, elevKey, chamferDrop,
                                plateauVertCache, verts, norms, tangs, colors, uv0, uv1);
                            int i10 = GetOrAddPlateauVertex(data, w, h, gx1, gzSouth, step, yElev, elevKey, chamferDrop,
                                plateauVertCache, verts, norms, tangs, colors, uv0, uv1);
                            int i11 = GetOrAddPlateauVertex(data, w, h, gx1, gzNorth, step, yElev, elevKey, chamferDrop,
                                plateauVertCache, verts, norms, tangs, colors, uv0, uv1);
                            int i01 = GetOrAddPlateauVertex(data, w, h, gx0, gzNorth, step, yElev, elevKey, chamferDrop,
                                plateauVertCache, verts, norms, tangs, colors, uv0, uv1);

                            // Winding for +Y up normal in Unity LH coords: (0, 2, 1) and (0, 3, 2)
                            tris.Add(i00);
                            tris.Add(i11);
                            tris.Add(i10);
                            tris.Add(i00);
                            tris.Add(i01);
                            tris.Add(i11);
                        }
                    }
                }
            }

            // 2. Stepped vertical cliff walls (and outer diorama skirt rim)
            int cliffTriStart = tris.Count;
            float topRimOffset = chamferDrop * 0.5f;

            for (int ty = 0; ty < h; ty++)
            {
                float zNorth = -ty;
                float zSouth = -(ty + 1);

                for (int tx = 0; tx < w; tx++)
                {
                    float eHere = data.GetTile(tx, ty).Elevation;
                    float topY = eHere - topRimOffset;

                    // South face (neighbor at ty + 1, Z = -(ty + 1), outward Vector3.back)
                    CheckAndEmitCliff(data, tx, ty + 1, eHere, topY, topRimOffset,
                        new Vector3(tx + 1, topY, zSouth), new Vector3(tx, topY, zSouth),
                        Vector3.back, verts, norms, tangs, colors, uv0, uv1, tris);

                    // North face (neighbor at ty - 1, Z = -ty, outward Vector3.forward)
                    CheckAndEmitCliff(data, tx, ty - 1, eHere, topY, topRimOffset,
                        new Vector3(tx, topY, zNorth), new Vector3(tx + 1, topY, zNorth),
                        Vector3.forward, verts, norms, tangs, colors, uv0, uv1, tris);

                    // West face (neighbor at tx - 1, X = tx, outward Vector3.left)
                    CheckAndEmitCliff(data, tx - 1, ty, eHere, topY, topRimOffset,
                        new Vector3(tx, topY, zSouth), new Vector3(tx, topY, zNorth),
                        Vector3.left, verts, norms, tangs, colors, uv0, uv1, tris);

                    // East face (neighbor at tx + 1, X = tx + 1, outward Vector3.right)
                    CheckAndEmitCliff(data, tx + 1, ty, eHere, topY, topRimOffset,
                        new Vector3(tx + 1, topY, zNorth), new Vector3(tx + 1, topY, zSouth),
                        Vector3.right, verts, norms, tangs, colors, uv0, uv1, tris);
                }
            }

            cliffTriangleCount = (tris.Count - cliffTriStart) / 3;

            Mesh mesh = new Mesh
            {
                name = $"Cartography_Terrain_{w}x{h}",
                indexFormat = verts.Count > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTangents(tangs);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int GetOrAddPlateauVertex(
            CartographyWorldData data,
            int w,
            int h,
            int gx,
            int gz,
            float step,
            float yElev,
            int elevKey,
            float chamferDrop,
            Dictionary<long, int> cache,
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector4> tangs,
            List<Color> colors,
            List<Vector2> uv0,
            List<Vector2> uv1)
        {
            long key = ((long)(uint)(elevKey + 32768) << 32) | ((long)(uint)gz << 16) | (uint)gx;
            if (cache.TryGetValue(key, out int existingIdx))
            {
                return existingIdx;
            }

            float wx = gx * step;
            float worldY = gz * step;
            float wz = -worldY;

            SampleCornerWeights(data, wx, worldY, yElev, out Color splat, out float ao);
            float rimDrop = ComputeCornerRimDrop(data, wx, worldY, yElev, chamferDrop);

            int idx = verts.Count;
            verts.Add(new Vector3(wx, yElev - rimDrop, wz));
            norms.Add(Vector3.up);
            tangs.Add(new Vector4(1f, 0f, 0f, -1f));
            colors.Add(splat);
            uv0.Add(new Vector2(wx / w, 1f - worldY / h));
            uv1.Add(new Vector2(0f, ao));

            cache[key] = idx;
            return idx;
        }

        private static void CheckAndEmitCliff(
            CartographyWorldData data,
            int nx,
            int ny,
            float eHere,
            float topY,
            float topRimOffset,
            Vector3 topLeft,
            Vector3 topRight,
            Vector3 outwardDir,
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector4> tangs,
            List<Color> colors,
            List<Vector2> uv0,
            List<Vector2> uv1,
            List<int> tris)
        {
            float eNeighbor;
            float botY;
            if (data.InBounds(nx, ny))
            {
                eNeighbor = data.GetTile(nx, ny).Elevation;
                // Tuck cliff base down by rim offset so it seats cleanly against chamfered lower rims
                botY = eNeighbor - topRimOffset;
            }
            else
            {
                eNeighbor = -0.75f;
                botY = eNeighbor;
            }

            if (eHere <= eNeighbor + 0.03f)
            {
                return;
            }

            Vector3 botRight = new Vector3(topRight.x, botY, topRight.z);
            Vector3 botLeft = new Vector3(topLeft.x, botY, topLeft.z);

            Vector3 cliffNormal = (outwardDir + new Vector3(0f, 0.14f, 0f)).normalized;
            Vector3 tangDir = (topRight - topLeft).normalized;
            Vector4 tangent = new Vector4(tangDir.x, tangDir.y, tangDir.z, 1f);

            int baseIdx = verts.Count;
            verts.Add(botLeft);
            verts.Add(botRight);
            verts.Add(topRight);
            verts.Add(topLeft);

            for (int k = 0; k < 4; k++)
            {
                norms.Add(cliffNormal);
                tangs.Add(tangent);
                colors.Add(new Color(0f, 0f, 0f, 0f));
            }

            uv0.Add(new Vector2(0f, 0f));
            uv0.Add(new Vector2(1f, 0f));
            uv0.Add(new Vector2(1f, 1f));
            uv0.Add(new Vector2(0f, 1f));

            // TEXCOORD1.x = 1.0 (cliff_rock slice 5), TEXCOORD1.y = crease AO darkened at cliff base
            uv1.Add(new Vector2(1f, 0.42f));
            uv1.Add(new Vector2(1f, 0.42f));
            uv1.Add(new Vector2(1f, 0.94f));
            uv1.Add(new Vector2(1f, 0.94f));

            // Outward front-facing winding in Unity left-handed coordinates
            tris.Add(baseIdx + 0);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 0);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 3);
        }

        private static void SampleCornerWeights(
            CartographyWorldData data,
            float wx,
            float worldY,
            float tierElevation,
            out Color splatColor,
            out float creaseAO)
        {
            int ix = Mathf.FloorToInt(wx - 0.001f);
            int iy = Mathf.FloorToInt(worldY - 0.001f);

            float sumR = 0f;
            float sumG = 0f;
            float sumB = 0f;
            float sumA = 0f;
            float sumAO = 0f;
            int count = 0;
            bool touchesHigherTier = false;

            for (int dy = 0; dy <= 1; dy++)
            {
                int ty = iy + dy;
                for (int dx = 0; dx <= 1; dx++)
                {
                    int tx = ix + dx;
                    if (!data.InBounds(tx, ty))
                    {
                        continue;
                    }

                    CartographyTile t = data.GetTile(tx, ty);
                    if (Mathf.Abs(t.Elevation - tierElevation) < 0.03f)
                    {
                        sumR += t.MossWeight;
                        sumG += t.DirtWeight;
                        sumB += t.CobbleWeight;
                        sumA += t.MudWeight;
                        sumAO += t.CreaseAO;
                        count++;
                    }
                    else if (t.Elevation > tierElevation + 0.03f)
                    {
                        touchesHigherTier = true;
                    }
                }
            }

            if (count == 0)
            {
                CartographyTile fallback = data.GetTile(Mathf.FloorToInt(wx), Mathf.FloorToInt(worldY));
                splatColor = new Color(fallback.MossWeight, fallback.DirtWeight, fallback.CobbleWeight, fallback.MudWeight);
                creaseAO = fallback.CreaseAO;
                return;
            }

            float inv = 1.0f / count;
            splatColor = new Color(sumR * inv, sumG * inv, sumB * inv, sumA * inv);
            creaseAO = sumAO * inv;

            if (touchesHigherTier)
            {
                creaseAO = Mathf.Min(creaseAO, 0.58f);
            }
        }

        private static float ComputeCornerRimDrop(
            CartographyWorldData data,
            float wx,
            float worldY,
            float tierElevation,
            float drop)
        {
            int minTx = Mathf.FloorToInt(wx - 0.001f);
            int maxTx = Mathf.FloorToInt(wx + 0.001f);
            int minTy = Mathf.FloorToInt(worldY - 0.001f);
            int maxTy = Mathf.FloorToInt(worldY + 0.001f);

            for (int ty = minTy; ty <= maxTy; ty++)
            {
                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    if (!data.InBounds(tx, ty) || data.GetTile(tx, ty).Elevation < tierElevation - 0.03f)
                    {
                        return drop * 0.5f;
                    }
                }
            }

            return 0f;
        }

        private static Mesh BuildWaterPlaneMesh(int width, int height)
        {
            int segsX = Mathf.Max(1, Mathf.CeilToInt(width / 8f));
            int segsZ = Mathf.Max(1, Mathf.CeilToInt(height / 8f));

            int vertsX = segsX + 1;
            int vertsZ = segsZ + 1;

            List<Vector3> verts = new List<Vector3>(vertsX * vertsZ);
            List<Vector3> norms = new List<Vector3>(vertsX * vertsZ);
            List<Vector4> tangs = new List<Vector4>(vertsX * vertsZ);
            List<Vector2> uvs = new List<Vector2>(vertsX * vertsZ);
            List<int> tris = new List<int>(segsX * segsZ * 6);

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            for (int iz = 0; iz <= segsZ; iz++)
            {
                float tz = (float)iz / segsZ;
                float z = Mathf.Lerp(-halfH, halfH, tz);
                float v = tz * (height * 0.5f);

                for (int ix = 0; ix <= segsX; ix++)
                {
                    float tx = (float)ix / segsX;
                    float x = Mathf.Lerp(-halfW, halfW, tx);
                    float u = tx * (width * 0.5f);

                    verts.Add(new Vector3(x, 0f, z));
                    norms.Add(Vector3.up);
                    tangs.Add(new Vector4(1f, 0f, 0f, -1f));
                    uvs.Add(new Vector2(u, v));
                }
            }

            for (int iz = 0; iz < segsZ; iz++)
            {
                int row0 = iz * vertsX;
                int row1 = (iz + 1) * vertsX;
                for (int ix = 0; ix < segsX; ix++)
                {
                    int v00 = row0 + ix;
                    int v10 = v00 + 1;
                    int v01 = row1 + ix;
                    int v11 = v01 + 1;

                    tris.Add(v00);
                    tris.Add(v11);
                    tris.Add(v10);
                    tris.Add(v00);
                    tris.Add(v01);
                    tris.Add(v11);
                }
            }

            Mesh mesh = new Mesh
            {
                name = $"Cartography_ShallowWaterPlane_{width}x{height}"
            };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTangents(tangs);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
