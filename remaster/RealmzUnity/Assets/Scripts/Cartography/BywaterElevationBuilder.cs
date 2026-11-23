using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.Cartography
{
    /// <summary>
    /// Generates the 3-tier heightfield terrain mesh (-0.6m river, 0.0m plaza, +0.75m ridge),
    /// chamfered vertical cliff faces (cliff_rock via TEXCOORD1.x), baked elevation-crease and
    /// structure footprint AO (TEXCOORD1.y), and the animated shallow_water river plane.
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

        public void Rebuild(CartographyWorldData data)
        {
            if (data == null)
            {
                data = CartographyWorldData.BuildBywaterProofLayout();
            }

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

            Mesh builtMesh = BuildTerrainMesh(data, out int cliffTris);
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

            EnsureWaterPlane();
        }

        public void EnsureWaterPlane()
        {
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

            MeshFilter wf = waterPlaneObject.GetComponent<MeshFilter>();
            if (wf == null) wf = waterPlaneObject.AddComponent<MeshFilter>();
            MeshRenderer wr = waterPlaneObject.GetComponent<MeshRenderer>();
            if (wr == null) wr = waterPlaneObject.AddComponent<MeshRenderer>();

            if (wf.sharedMesh == null)
            {
                wf.sharedMesh = BuildWaterPlaneMesh(
                    0f,
                    CartographyWorldData.GridWidth,
                    2.80f,
                    5.20f,
                    CartographyWorldData.WaterSurfaceElevation);
            }

            wr.shadowCastingMode = ShadowCastingMode.Off;
            wr.receiveShadows = true;
            if (waterMaterial != null)
            {
                wr.sharedMaterial = waterMaterial;
            }
        }

        public static Mesh BuildTerrainMesh(CartographyWorldData data, out int cliffTriangleCount)
        {
            if (data == null)
            {
                data = CartographyWorldData.BuildBywaterProofLayout();
            }

            const int w = CartographyWorldData.GridWidth;
            const int h = CartographyWorldData.GridHeight;
            const int sub = SubdivisionsPerTile;
            const float step = 1.0f / sub;
            const float chamferInset = 0.08f;
            const float chamferDrop = 0.06f;

            List<Vector3> verts = new List<Vector3>(w * h * sub * sub * 4);
            List<Vector3> norms = new List<Vector3>(w * h * sub * sub * 4);
            List<Vector4> tangs = new List<Vector4>(w * h * sub * sub * 4);
            List<Color> colors = new List<Color>(w * h * sub * sub * 4);
            List<Vector2> uv0 = new List<Vector2>(w * h * sub * sub * 4);
            List<Vector2> uv1 = new List<Vector2>(w * h * sub * sub * 4);
            List<int> tris = new List<int>(w * h * sub * sub * 6);

            // 1. Horizontal plateau surface quads (subdivided per tile for smooth splat gradients)
            for (int ty = 0; ty < h; ty++)
            {
                for (int tx = 0; tx < w; tx++)
                {
                    CartographyTile tile = data.GetTile(tx, ty);
                    float yElev = tile.Elevation;

                    for (int sy = 0; sy < sub; sy++)
                    {
                        for (int sx = 0; sx < sub; sx++)
                        {
                            float x0 = tx + sx * step;
                            float z0 = ty + sy * step;
                            float x1 = x0 + step;
                            float z1 = z0 + step;

                            SampleCornerWeights(data, x0, z0, yElev, out Color c00, out float ao00);
                            SampleCornerWeights(data, x1, z0, yElev, out Color c10, out float ao10);
                            SampleCornerWeights(data, x1, z1, yElev, out Color c11, out float ao11);
                            SampleCornerWeights(data, x0, z1, yElev, out Color c01, out float ao01);

                            // Slight chamfer lip drop right at higher cliff rims so sunlight highlights edges
                            float y00 = yElev - ComputeEdgeRimDrop(data, tx, ty, x0, z0, yElev, chamferInset, chamferDrop);
                            float y10 = yElev - ComputeEdgeRimDrop(data, tx, ty, x1, z0, yElev, chamferInset, chamferDrop);
                            float y11 = yElev - ComputeEdgeRimDrop(data, tx, ty, x1, z1, yElev, chamferInset, chamferDrop);
                            float y01 = yElev - ComputeEdgeRimDrop(data, tx, ty, x0, z1, yElev, chamferInset, chamferDrop);

                            int baseIdx = verts.Count;
                            verts.Add(new Vector3(x0, y00, z0));
                            verts.Add(new Vector3(x1, y10, z0));
                            verts.Add(new Vector3(x1, y11, z1));
                            verts.Add(new Vector3(x0, y01, z1));

                            for (int k = 0; k < 4; k++)
                            {
                                norms.Add(Vector3.up);
                                tangs.Add(new Vector4(1f, 0f, 0f, -1f));
                            }

                            colors.Add(c00);
                            colors.Add(c10);
                            colors.Add(c11);
                            colors.Add(c01);

                            uv0.Add(new Vector2(x0 / w, z0 / h));
                            uv0.Add(new Vector2(x1 / w, z0 / h));
                            uv0.Add(new Vector2(x1 / w, z1 / h));
                            uv0.Add(new Vector2(x0 / w, z1 / h));

                            uv1.Add(new Vector2(0f, ao00));
                            uv1.Add(new Vector2(0f, ao10));
                            uv1.Add(new Vector2(0f, ao11));
                            uv1.Add(new Vector2(0f, ao01));

                            // Winding for +Y up normal in Unity LH coords: (0, 2, 1) and (0, 3, 2)
                            tris.Add(baseIdx + 0);
                            tris.Add(baseIdx + 2);
                            tris.Add(baseIdx + 1);
                            tris.Add(baseIdx + 0);
                            tris.Add(baseIdx + 3);
                            tris.Add(baseIdx + 2);
                        }
                    }
                }
            }

            // 2. Stepped vertical cliff walls (and outer diorama skirt rim)
            int cliffTriStart = tris.Count;
            for (int ty = 0; ty < h; ty++)
            {
                for (int tx = 0; tx < w; tx++)
                {
                    float eHere = data.GetTile(tx, ty).Elevation;

                    // Check 4 cardinal neighbors: South (-Z), North (+Z), West (-X), East (+X)
                    CheckAndEmitCliff(data, tx, ty, tx, ty - 1, eHere,
                        new Vector3(tx, eHere, ty), new Vector3(tx + 1, eHere, ty),
                        Vector3.back, verts, norms, tangs, colors, uv0, uv1, tris);

                    CheckAndEmitCliff(data, tx, ty, tx, ty + 1, eHere,
                        new Vector3(tx + 1, eHere, ty + 1), new Vector3(tx, eHere, ty + 1),
                        Vector3.forward, verts, norms, tangs, colors, uv0, uv1, tris);

                    CheckAndEmitCliff(data, tx, ty, tx - 1, ty, eHere,
                        new Vector3(tx, eHere, ty + 1), new Vector3(tx, eHere, ty),
                        Vector3.left, verts, norms, tangs, colors, uv0, uv1, tris);

                    CheckAndEmitCliff(data, tx, ty, tx + 1, ty, eHere,
                        new Vector3(tx + 1, eHere, ty), new Vector3(tx + 1, eHere, ty + 1),
                        Vector3.right, verts, norms, tangs, colors, uv0, uv1, tris);
                }
            }

            cliffTriangleCount = (tris.Count - cliffTriStart) / 3;

            Mesh mesh = new Mesh
            {
                name = "Bywater_Cartography_Terrain",
                indexFormat = IndexFormat.UInt32
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

        private static void CheckAndEmitCliff(
            CartographyWorldData data,
            int tx,
            int ty,
            int nx,
            int ny,
            float eHere,
            Vector3 topRight,
            Vector3 topLeft,
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
            if (data.IsInBounds(nx, ny))
            {
                eNeighbor = data.GetTile(nx, ny).Elevation;
            }
            else
            {
                // Diorama cutaway border drop
                eNeighbor = -0.75f;
            }

            if (eHere <= eNeighbor + 0.05f)
            {
                return;
            }

            Vector3 botRight = new Vector3(topRight.x, eNeighbor, topRight.z);
            Vector3 botLeft = new Vector3(topLeft.x, eNeighbor, topLeft.z);

            // Slight chamfer outward normal for crisp directional rim catch
            Vector3 cliffNormal = (outwardDir + new Vector3(0f, 0.14f, 0f)).normalized;
            Vector3 tangDir = (topRight - topLeft).normalized;
            Vector4 tangent = new Vector4(tangDir.x, tangDir.y, tangDir.z, -1f);

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
            float wz,
            float tierElevation,
            out Color splatColor,
            out float creaseAO)
        {
            // Average the up to 4 adjacent tiles sharing vertex (wx, wz) ONLY on the same elevation tier
            // so river mud / ridge moss don't bleed weirdly across cliff dropoffs.
            int ix = Mathf.FloorToInt(wx - 0.001f);
            int iz = Mathf.FloorToInt(wz - 0.001f);

            float sumR = 0f;
            float sumG = 0f;
            float sumB = 0f;
            float sumA = 0f;
            float sumAO = 0f;
            int count = 0;
            bool touchesDifferentTier = false;

            for (int dz = 0; dz <= 1; dz++)
            {
                for (int dx = 0; dx <= 1; dx++)
                {
                    int tx = Mathf.Clamp(ix + dx, 0, CartographyWorldData.GridWidth - 1);
                    int tz = Mathf.Clamp(iz + dz, 0, CartographyWorldData.GridHeight - 1);
                    CartographyTile t = data.GetTile(tx, tz);
                    if (Mathf.Abs(t.Elevation - tierElevation) < 0.05f)
                    {
                        sumR += t.MossWeight;
                        sumG += t.DirtWeight;
                        sumB += t.CobbleWeight;
                        sumA += t.MudWeight;
                        sumAO += t.CreaseAO;
                        count++;
                    }
                    else
                    {
                        touchesDifferentTier = true;
                    }
                }
            }

            if (count == 0)
            {
                splatColor = new Color(0f, 0f, 0f, 0f);
                creaseAO = 1.0f;
                return;
            }

            float inv = 1.0f / count;
            splatColor = new Color(sumR * inv, sumG * inv, sumB * inv, sumA * inv);
            creaseAO = sumAO * inv;

            // Darken lower plateau crease right next to a rising cliff base
            if (touchesDifferentTier && tierElevation < CartographyWorldData.RidgeElevation)
            {
                creaseAO = Mathf.Min(creaseAO, 0.58f);
            }
        }

        private static float ComputeEdgeRimDrop(
            CartographyWorldData data,
            int tx,
            int ty,
            float wx,
            float wz,
            float tierElevation,
            float inset,
            float drop)
        {
            // Subtle chamfer drop if vertex lies right on a higher edge facing a lower neighbor
            bool atWestEdge = Mathf.Abs(wx - tx) < 0.01f && (!data.IsInBounds(tx - 1, ty) || data.GetTile(tx - 1, ty).Elevation < tierElevation - 0.05f);
            bool atEastEdge = Mathf.Abs(wx - (tx + 1)) < 0.01f && (!data.IsInBounds(tx + 1, ty) || data.GetTile(tx + 1, ty).Elevation < tierElevation - 0.05f);
            bool atSouthEdge = Mathf.Abs(wz - ty) < 0.01f && (!data.IsInBounds(tx, ty - 1) || data.GetTile(tx, ty - 1).Elevation < tierElevation - 0.05f);
            bool atNorthEdge = Mathf.Abs(wz - (ty + 1)) < 0.01f && (!data.IsInBounds(tx, ty + 1) || data.GetTile(tx, ty + 1).Elevation < tierElevation - 0.05f);

            if (atWestEdge || atEastEdge || atSouthEdge || atNorthEdge)
            {
                return drop * 0.5f;
            }
            return 0f;
        }

        private static Mesh BuildWaterPlaneMesh(float minX, float maxX, float minZ, float maxZ, float y)
        {
            Mesh mesh = new Mesh
            {
                name = "Bywater_ShallowWaterPlane"
            };

            mesh.SetVertices(new[]
            {
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ)
            });

            mesh.SetNormals(new[]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            });

            mesh.SetTangents(new[]
            {
                new Vector4(1f, 0f, 0f, -1f),
                new Vector4(1f, 0f, 0f, -1f),
                new Vector4(1f, 0f, 0f, -1f),
                new Vector4(1f, 0f, 0f, -1f)
            });

            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(9f, 0f),
                new Vector2(9f, 1.5f),
                new Vector2(0f, 1.5f)
            });

            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
