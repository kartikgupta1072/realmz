using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Realmz.Cartography;
using UnityEditor;
using UnityEngine;

namespace Realmz.EditorTests
{
    public sealed class CartographyEditorTests
    {
        [Test]
        public void CartographyShaders_CompileWithoutErrorsAndExposeRequiredProperties()
        {
            Shader terrainShader = Shader.Find("Realmz/CartographyTerrain");
            Assert.That(terrainShader, Is.Not.Null, "Realmz/CartographyTerrain shader not found.");
            Assert.That(ShaderUtil.ShaderHasError(terrainShader), Is.False, "Realmz/CartographyTerrain has shader compile errors.");

            AssertShaderHasProperty(terrainShader, "_TerrainArray");
            AssertShaderHasProperty(terrainShader, "_NormalArray");
            AssertShaderHasProperty(terrainShader, "_WorldTexelScale");
            AssertShaderHasProperty(terrainShader, "_NoiseBlendSharpness");
            AssertShaderHasProperty(terrainShader, "_CreaseAOStrength");
            AssertShaderHasProperty(terrainShader, "_LanternPoolColor");

            Shader billboardShader = Shader.Find("Realmz/CartographyBillboard");
            Assert.That(billboardShader, Is.Not.Null, "Realmz/CartographyBillboard shader not found.");
            Assert.That(ShaderUtil.ShaderHasError(billboardShader), Is.False, "Realmz/CartographyBillboard has shader compile errors.");

            AssertShaderHasProperty(billboardShader, "_BaseMap");
            AssertShaderHasProperty(billboardShader, "_IsTwoBankAtlas");
            AssertShaderHasProperty(billboardShader, "_Cols");
            AssertShaderHasProperty(billboardShader, "_Rows");
            AssertShaderHasProperty(billboardShader, "_VariantIndex");
            AssertShaderHasProperty(billboardShader, "_WindPhase");
            AssertShaderHasProperty(billboardShader, "_TintColor");
            AssertShaderHasProperty(billboardShader, "_FrameCount");
            AssertShaderHasProperty(billboardShader, "_AnimFps");
            AssertShaderHasProperty(billboardShader, "_CameraPitchTiltDeg");
            AssertShaderHasProperty(billboardShader, "_NormalUpBias");
            AssertShaderHasProperty(billboardShader, "_RootTint");
            AssertShaderHasProperty(billboardShader, "_TrampleRadius");
            AssertShaderHasProperty(billboardShader, "_TrampleStrength");

            Shader lmGenShader = Shader.Find(BywaterTopDownLightmap.GeneratorShaderName);
            Assert.That(lmGenShader, Is.Not.Null, "TopDownLightmapGen shader not found.");
            Assert.That(ShaderUtil.ShaderHasError(lmGenShader), Is.False, "TopDownLightmapGen has shader compile errors.");
        }

        [Test]
        public void BywaterElevationBuilder_BuildsThreeTierMeshWithCliffsAndCreaseAO()
        {
            CartographyWorldData worldData = CartographyWorldData.BuildBywaterProofLayout();
            Mesh mesh = BywaterElevationBuilder.BuildTerrainMesh(worldData, out int cliffTriangleCount);

            try
            {
                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                Assert.That(mesh.triangles.Length, Is.GreaterThan(0));
                Assert.That(cliffTriangleCount, Is.GreaterThan(0), "Expected stepped vertical cliff triangles.");

                Vector3[] verts = mesh.vertices;
                float minY = float.MaxValue;
                float maxY = float.MinValue;
                for (int i = 0; i < verts.Length; i++)
                {
                    if (verts[i].y < minY) minY = verts[i].y;
                    if (verts[i].y > maxY) maxY = verts[i].y;
                }

                Assert.That(minY, Is.LessThanOrEqualTo(-0.55f), $"Expected river channel minY <= -0.55, got {minY}.");
                Assert.That(maxY, Is.GreaterThanOrEqualTo(0.70f), $"Expected northern ridge maxY >= 0.70, got {maxY}.");

                List<Vector2> uv1 = new List<Vector2>();
                mesh.GetUVs(1, uv1);
                Assert.That(uv1.Count, Is.EqualTo(mesh.vertexCount));

                bool hasCliffMask = false;
                bool hasDarkenedCreaseAO = false;
                bool hasOpenCreaseAO = false;

                for (int i = 0; i < uv1.Count; i++)
                {
                    if (uv1[i].x > 0.9f) hasCliffMask = true;
                    if (uv1[i].y < 0.75f) hasDarkenedCreaseAO = true;
                    if (uv1[i].y > 0.95f) hasOpenCreaseAO = true;
                }

                Assert.That(hasCliffMask, Is.True, "Expected cliff_rock mask in TEXCOORD1.x.");
                Assert.That(hasDarkenedCreaseAO, Is.True, "Expected darkened crease/footprint AO in TEXCOORD1.y.");
                Assert.That(hasOpenCreaseAO, Is.True, "Expected unoccluded plateau AO in TEXCOORD1.y.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FoliageAtlasUvMath_All16VariantsBy8FramesAreDisjointInUnitSquare()
        {
            const float expectedW = 1f / 16f;
            const float expectedH = 1f / 8f;
            const float eps = 1e-5f;

            List<Rect> allCells = new List<Rect>(16 * 8);

            for (int variant = 0; variant < 16; variant++)
            {
                for (int frame = 0; frame < 8; frame++)
                {
                    Rect cell = BywaterFoliageScatter.ComputeTwoBankAtlasCellUvRect(variant, frame);

                    Assert.That(cell.xMin, Is.GreaterThanOrEqualTo(-eps), $"Variant {variant} frame {frame} xMin < 0");
                    Assert.That(cell.yMin, Is.GreaterThanOrEqualTo(-eps), $"Variant {variant} frame {frame} yMin < 0");
                    Assert.That(cell.xMax, Is.LessThanOrEqualTo(1f + eps), $"Variant {variant} frame {frame} xMax > 1");
                    Assert.That(cell.yMax, Is.LessThanOrEqualTo(1f + eps), $"Variant {variant} frame {frame} yMax > 1");
                    Assert.That(cell.width, Is.EqualTo(expectedW).Within(eps));
                    Assert.That(cell.height, Is.EqualTo(expectedH).Within(eps));

                    for (int prev = 0; prev < allCells.Count; prev++)
                    {
                        Rect other = allCells[prev];
                        float overlapX = Mathf.Min(cell.xMax, other.xMax) - Mathf.Max(cell.xMin, other.xMin);
                        float overlapY = Mathf.Min(cell.yMax, other.yMax) - Mathf.Max(cell.yMin, other.yMin);
                        bool overlaps = overlapX > eps && overlapY > eps;
                        Assert.That(overlaps, Is.False, $"Cell ({variant},{frame}) overlaps previous cell {prev}.");
                    }

                    allCells.Add(cell);
                }
            }

            Assert.That(allCells.Count, Is.EqualTo(128));
        }

        [Test]
        public void BywaterFoliageScatter_ScattersDenseMeadowCardsAndRespectsExclusions()
        {
            CartographyWorldData worldData = CartographyWorldData.BuildBywaterProofLayout();
            GameObject host = new GameObject("TempFoliageScatterTest");

            try
            {
                BywaterFoliageScatter scatter = host.AddComponent<BywaterFoliageScatter>();
                scatter.RebuildScatter(worldData);

                Assert.That(scatter.TotalCardCount, Is.GreaterThanOrEqualTo(3500),
                    $"Expected >= 3500 grass cards across 18x18 diorama, got {scatter.TotalCardCount}.");

                for (int y = 0; y < CartographyWorldData.GridHeight; y++)
                {
                    for (int x = 0; x < CartographyWorldData.GridWidth; x++)
                    {
                        CartographyTile tile = worldData.GetTile(x, y);
                        int count = scatter.GetTileCardCount(x, y);
                        if (tile.BlocksFoliage || tile.IsWater)
                        {
                            Assert.That(count, Is.EqualTo(0), $"Blocked tile ({x},{y}) should have 0 foliage cards.");
                        }
                        else
                        {
                            Assert.That(count, Is.InRange(
                                BywaterFoliageScatter.MinCardsPerOpenTile,
                                BywaterFoliageScatter.MaxCardsPerOpenTile),
                                $"Open tile ({x},{y}) card count {count} out of [20, 36] range.");
                        }
                    }
                }

                // Explicit spot checks on river, bridge, cobble road, inn footprint, and open meadow
                Assert.That(scatter.GetTileCardCount(4, 3), Is.EqualTo(0), "River tile must have 0 cards.");
                Assert.That(scatter.GetTileCardCount(8, 3), Is.EqualTo(0), "Bridge tile must have 0 cards.");
                Assert.That(scatter.GetTileCardCount(8, 7), Is.EqualTo(0), "Cobble road tile must have 0 cards.");
                Assert.That(scatter.GetTileCardCount(5, 10), Is.EqualTo(0), "Bywater Inn footprint must have 0 cards.");
                Assert.That(scatter.GetTileCardCount(13, 8), Is.GreaterThanOrEqualTo(20), "East sunlit meadow must have >= 20 cards.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CartographyPngAssets_WhenPresent_UsePointUncompressedNoMipMapsAnd64Ppu()
        {
            string cartoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "Art", "Cartography"));
            if (!Directory.Exists(cartoRoot))
            {
                Assert.Pass("Assets/Art/Cartography directory not created yet; skipping PNG import verification.");
                return;
            }

            string[] pngs = Directory.GetFiles(cartoRoot, "*.png", SearchOption.AllDirectories);
            if (pngs.Length == 0)
            {
                Assert.Pass("No Cartography PNGs present on disk yet; skipping PNG import verification.");
                return;
            }

            for (int i = 0; i < pngs.Length; i++)
            {
                string rel = "Assets" + pngs[i].Substring(Application.dataPath.Length).Replace('\\', '/');
                TextureImporter importer = AssetImporter.GetAtPath(rel) as TextureImporter;
                Assert.That(importer, Is.Not.Null, "Missing TextureImporter for " + rel);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), "Expected Point filter on " + rel);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), "Expected Uncompressed on " + rel);
                Assert.That(importer.mipmapEnabled, Is.False, "Expected mipmaps off on " + rel);
                Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(64f), "Expected 64 PPU on " + rel);

                if (rel.EndsWith("_n.png", System.StringComparison.OrdinalIgnoreCase))
                {
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap), "Expected NormalMap on " + rel);
                    Assert.That(importer.sRGBTexture, Is.False, "Expected sRGB=false on " + rel);
                }
                else if (rel.IndexOf("/tiles/", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat), "Expected Repeat wrap on tile " + rel);
                }
            }
        }

        private static void AssertShaderHasProperty(Shader shader, string propertyName)
        {
            int idx = shader.FindPropertyIndex(propertyName);
            Assert.That(idx, Is.GreaterThanOrEqualTo(0), $"Shader '{shader.name}' missing required property '{propertyName}'.");
        }
    }
}
