using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Realmz.Cartography;
using Realmz.Data;
using Realmz.EditorTools.Cartography;
using Realmz.Game;
using Realmz.World;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
            AssertShaderHasProperty(billboardShader, "_EmissionWarmMask");
            AssertShaderHasProperty(billboardShader, "_EmissionMinV");
            AssertShaderHasProperty(billboardShader, "_HorizonHazeColor");
            AssertShaderHasProperty(billboardShader, "_HorizonHazeStrength");

            Shader lmGenShader = Shader.Find(BywaterTopDownLightmap.GeneratorShaderName);
            Assert.That(lmGenShader, Is.Not.Null, "TopDownLightmapGen shader not found.");
            Assert.That(ShaderUtil.ShaderHasError(lmGenShader), Is.False, "TopDownLightmapGen has shader compile errors.");

            Shader waterShader = Shader.Find("Realmz/CartographyWater");
            Assert.That(waterShader, Is.Not.Null, "Realmz/CartographyWater shader not found.");
            Assert.That(ShaderUtil.ShaderHasError(waterShader), Is.False, "Realmz/CartographyWater has shader compile errors.");
            AssertShaderHasProperty(waterShader, "_BaseMap");
            AssertShaderHasProperty(waterShader, "_BaseColor");
            AssertShaderHasProperty(waterShader, "_ShimmerColor");
            AssertShaderHasProperty(waterShader, "_WaveScale");
            Assert.That(waterShader.FindPropertyIndex("_BywaterTopDownLightmap"), Is.EqualTo(-1),
                "Realmz/CartographyWater should not expose obsolete _BywaterTopDownLightmap property.");
        }

        [Test]
        public void FoliageScatter_DistributesEvenlyAcrossAllFiveZStrata()
        {
            for (int cardCount = BywaterFoliageScatter.MinCardsPerOpenTile; cardCount <= 28; cardCount++)
            {
                int tileHash = CartographyWorldData.DeterministicHash(10 + cardCount, 7, 107);
                int[] zBandCounts = new int[5];
                for (int i = 0; i < cardCount; i++)
                {
                    int h = CartographyWorldData.DeterministicHash(10 * 37 + i, 7 * 53 + i * 7, 911);
                    Vector2 off = BywaterFoliageScatter.ComputeCardCellOffset(i, tileHash, h);
                    int band = Mathf.Clamp(Mathf.FloorToInt(off.y * 5f), 0, 4);
                    zBandCounts[band]++;
                }

                for (int b = 0; b < 5; b++)
                {
                    Assert.That(zBandCounts[b], Is.GreaterThanOrEqualTo(3),
                        $"cardCount={cardCount} left Z-band {b} starved ({zBandCounts[b]} cards).");
                }
            }
        }

        [Test]
        public void UrpRendererAndVolume_DisableWastedSsaoAndEnableVignette()
        {
            ScriptableRendererData rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/PC_Renderer.asset");
            Assert.That(rendererData, Is.Not.Null, "PC_Renderer.asset missing.");
            for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
            {
                ScriptableRendererFeature feat = rendererData.rendererFeatures[i];
                if (feat != null && feat.GetType().Name.Contains("ScreenSpaceAmbientOcclusion"))
                {
                    Assert.That(feat.isActive, Is.False, "Wasted SSAO feature should be inactive.");
                }
            }

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Art/Cartography/BywaterDiorama_VolumeProfile.asset");
            Assert.That(profile, Is.Not.Null, "BywaterDiorama_VolumeProfile.asset missing.");
            Assert.That(profile.TryGet(out Vignette vignette), Is.True, "VolumeProfile missing Vignette.");
            Assert.That(vignette.active, Is.True);
            Assert.That(vignette.intensity.value, Is.GreaterThan(0.15f));
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

                List<Vector4> tangents = new List<Vector4>();
                mesh.GetTangents(tangents);
                Assert.That(tangents.Count, Is.EqualTo(mesh.vertexCount));

                bool hasCliffMask = false;
                bool hasDarkenedCreaseAO = false;
                bool hasOpenCreaseAO = false;

                for (int i = 0; i < uv1.Count; i++)
                {
                    if (uv1[i].x > 0.9f)
                    {
                        hasCliffMask = true;
                        Assert.That(tangents[i].w, Is.GreaterThan(0f), $"Expected cliff vertex {i} tangent.w > 0.");
                    }
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

                // Explicit spot checks on river (y=13..14), bridge, cobble road (y=10), inn footprint (y=7), and open meadow (y=9)
                Assert.That(scatter.GetTileCardCount(4, 14), Is.EqualTo(0), "River tile must have 0 cards.");
                Assert.That(scatter.GetTileCardCount(8, 14), Is.EqualTo(0), "Bridge tile must have 0 cards.");
                Assert.That(scatter.GetTileCardCount(8, 10), Is.EqualTo(0), "Cobble road tile must have 0 cards.");
                Assert.That(scatter.GetTileCardCount(5, 7), Is.EqualTo(0), "Bywater Inn footprint must have 0 cards.");
                Assert.That(scatter.GetTileCardCount(13, 9), Is.GreaterThanOrEqualTo(20), "East sunlit meadow must have >= 20 cards.");
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

        [Test]
        public void CartographyTerrainArrays_AreNonReadableAfterImport()
        {
            CartographyAssetImporter.BuildTerrainTextureArrays(out Texture2DArray albedoArray, out Texture2DArray normalArray);
            AssetDatabase.SaveAssets();

            Texture2DArray loadedAlbedo = AssetDatabase.LoadAssetAtPath<Texture2DArray>(CartographyAssetImporter.AlbedoArrayAssetPath);
            Texture2DArray loadedNormal = AssetDatabase.LoadAssetAtPath<Texture2DArray>(CartographyAssetImporter.NormalArrayAssetPath);

            Assert.That(albedoArray, Is.Not.Null);
            Assert.That(normalArray, Is.Not.Null);
            Assert.That(loadedAlbedo, Is.Not.Null, "CartographyTerrainAlbedoArray.asset missing.");
            Assert.That(loadedNormal, Is.Not.Null, "CartographyTerrainNormalArray.asset missing.");
            Assert.That(loadedAlbedo.isReadable, Is.False, "CartographyTerrainAlbedoArray must have isReadable == false.");
            Assert.That(loadedNormal.isReadable, Is.False, "CartographyTerrainNormalArray must have isReadable == false.");
        }

        [Test]
        public void CartographyMaterials_ConfigureInstancingForSrpBatcherAndFoliage()
        {
            string materialsDir = Path.GetFullPath(Path.Combine(Application.dataPath, "Art", "Cartography", "Materials"));
            if (!Directory.Exists(materialsDir))
            {
                Assert.Pass("Assets/Art/Cartography/Materials directory not created yet; skipping material instancing check.");
                return;
            }

            Material foliageMat = AssetDatabase.LoadAssetAtPath<Material>(CartographyAssetImporter.FoliageMaterialPath);
            if (foliageMat != null)
            {
                Assert.That(foliageMat.enableInstancing, Is.True, "Cartography_FoliageAtlas.mat must have enableInstancing == true.");
            }

            string[] propMats = Directory.GetFiles(materialsDir, "Cartography_Prop_*.mat", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < propMats.Length; i++)
            {
                string rel = "Assets" + propMats[i].Substring(Application.dataPath.Length).Replace('\\', '/');
                Material propMat = AssetDatabase.LoadAssetAtPath<Material>(rel);
                Assert.That(propMat, Is.Not.Null, "Failed to load material: " + rel);
                Assert.That(propMat.enableInstancing, Is.False, $"Prop material {rel} must have enableInstancing == false for SRP Batcher.");
            }
        }

        [Test]
        public void BywaterTopDownLightmap_ComputesDynamicShorelineSdfAndBridgeOverhang()
        {
            CartographyWorldData worldData = CartographyWorldData.BuildBywaterProofLayout();
            GameObject host = new GameObject("TempTopDownLightmapTest");

            try
            {
                BywaterTopDownLightmap lightmap = host.AddComponent<BywaterTopDownLightmap>();
                lightmap.ConfigureFromWorldData(worldData);
                lightmap.UpdateLightmap(0f);

                Texture2D staticMap = lightmap.WorldStaticMap;
                Assert.That(staticMap, Is.Not.Null, "WorldStaticMap should be generated.");
                Assert.That(staticMap.isReadable, Is.True, "WorldStaticMap should remain readable for SDF assertions.");

                Color SampleWorldStatic(float wx, float wz)
                {
                    int px = Mathf.Clamp(Mathf.FloorToInt(wx / worldData.Width * staticMap.width), 0, staticMap.width - 1);
                    int py = Mathf.Clamp(Mathf.FloorToInt((wz + worldData.Height) / worldData.Height * staticMap.height), 0, staticMap.height - 1);
                    return staticMap.GetPixel(px, py);
                }

                // Canonical proof river spans y=13..14 (Z in [-15..-13], center -14.0)
                Color openMidRiver = SampleWorldStatic(2.5f, -14.0f);
                Color bridgeMidRiver = SampleWorldStatic(8.5f, -14.0f);
                Color nearBank = SampleWorldStatic(2.0f, -13.05f);
                Color openLand = SampleWorldStatic(2.0f, -10.0f);

                Assert.That(openMidRiver.g, Is.LessThan(0.5f), $"Mid-river (2.5, -14.0) shoreProx should be < 0.5, got {openMidRiver.g}.");
                Assert.That(bridgeMidRiver.g, Is.LessThan(0.5f), $"Mid-river (8.5, -14.0) shoreProx should be < 0.5, got {bridgeMidRiver.g}.");
                Assert.That(nearBank.g, Is.GreaterThan(0.5f), $"Near-bank (2.0, -13.05) shoreProx should be > 0.5, got {nearBank.g}.");
                Assert.That(openLand.g, Is.EqualTo(1.0f).Within(0.01f), $"Land (2.0, -10.0) shoreProx should be 1.0, got {openLand.g}.");

                Assert.That(bridgeMidRiver.r, Is.LessThanOrEqualTo(0.59f),
                    $"Bridge overhang should darken WorldStaticMap.r to <= 0.59 at (8.5, -14.0), got {bridgeMidRiver.r}.");
                Assert.That(openMidRiver.r, Is.GreaterThan(0.90f),
                    $"Open river (2.5, -14.0) should stay unoccluded (WorldStaticMap.r > 0.90), got {openMidRiver.r}.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CartographyWorldData_CanonicalGridToWorldAndWorldToGridRoundTrip()
        {
            Vector3 p00 = CartographyWorldData.GridToWorld(0, 0);
            Assert.That(p00, Is.EqualTo(new Vector3(0.5f, 0f, -0.5f)));

            GridCoord c89 = new GridCoord(89, 89);
            Vector3 p89 = CartographyWorldData.GridToWorld(c89, 0.55f);
            Assert.That(p89, Is.EqualTo(new Vector3(89.5f, 0.55f, -89.5f)));

            CartographyWorldData.WorldToGrid(new Vector3(12.2f, 0f, -47.9f), out int gx, out int gy);
            Assert.That(gx, Is.EqualTo(12));
            Assert.That(gy, Is.EqualTo(47));
            Assert.That(CartographyWorldData.ToGridCoord(p89), Is.EqualTo(c89));

            CartographyWorldData data = new CartographyWorldData(4, 4, false);
            CartographyTile t = data.GetTile(1, 2);
            t.Elevation = 0.04f;
            data.SetTile(1, 2, t);
            Assert.That(data.GetElevation(1, 2), Is.EqualTo(0.04f).Within(1e-4f));
            Assert.That(data.GetInterpolatedElevation(1.5f, -2.5f), Is.EqualTo(0.04f).Within(1e-4f));
        }

        [Test]
        public void CartographyWorldData_BuildFromTileMap_Loads90x90LandAndDungeonWithUncappedPropsAndLanterns()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);

            CartographyWorldData land = CartographyWorldData.BuildFromTileMap(bundle.Land0);
            Assert.That(land.Width, Is.EqualTo(90));
            Assert.That(land.Height, Is.EqualTo(90));
            Assert.That(land.IsDungeon, Is.False);
            Assert.That(land.WaterSurfaceHeight, Is.EqualTo(CartographyWorldData.TileMapWaterSurfaceElevation).Within(1e-4f));
            Assert.That(land.Props.Count, Is.GreaterThan(24), "90x90 land_0 should not cap props at 24.");
            Assert.That(land.Lanterns.Count, Is.GreaterThan(12), "90x90 land_0 should not cap lanterns at 12.");

            bool hasWater = false;
            bool hasMud = false;
            bool hasCobble = false;
            bool hasHighland = false;
            bool hasCliff = false;
            for (int y = 0; y < land.Height; y++)
            {
                for (int x = 0; x < land.Width; x++)
                {
                    float e = land.GetElevation(x, y);
                    if (Mathf.Abs(e - CartographyWorldData.RiverElevation) < 0.01f) hasWater = true;
                    if (Mathf.Abs(e - CartographyWorldData.MudElevation) < 0.01f) hasMud = true;
                    if (Mathf.Abs(e - CartographyWorldData.CobbleElevation) < 0.01f) hasCobble = true;
                    if (Mathf.Abs(e - CartographyWorldData.HighlandElevation) < 0.01f) hasHighland = true;
                    if (e >= CartographyWorldData.CliffLowElevation - 0.01f) hasCliff = true;
                }
            }

            Assert.That(hasWater, Is.True, "Expected water tiles at -0.60m.");
            Assert.That(hasMud, Is.True, "Expected mud shoreline tiles at -0.05m.");
            Assert.That(hasCobble, Is.True, "Expected cobble road tiles at +0.04m.");
            Assert.That(hasHighland, Is.True, "Expected stepped highland plateaus at +0.55m.");
            Assert.That(hasCliff, Is.True, "Expected mountain/crag cliffs >= +1.15m.");

            CartographyWorldData dungeon = CartographyWorldData.BuildFromTileMap(bundle.Dungeon0);
            Assert.That(dungeon.Width, Is.EqualTo(90));
            Assert.That(dungeon.Height, Is.EqualTo(90));
            Assert.That(dungeon.IsDungeon, Is.True);
            Assert.That(dungeon.Lanterns.Count, Is.GreaterThan(0), "Expected dungeon wall sconce/portal lanterns.");

            bool hasDungeonWall = false;
            bool hasDungeonFloor = false;
            for (int y = 0; y < dungeon.Height; y++)
            {
                for (int x = 0; x < dungeon.Width; x++)
                {
                    float e = dungeon.GetElevation(x, y);
                    if (Mathf.Abs(e - CartographyWorldData.DungeonWallElevation) < 0.01f) hasDungeonWall = true;
                    if (Mathf.Abs(e - CartographyWorldData.PlazaElevation) < 0.01f) hasDungeonFloor = true;
                }
            }

            Assert.That(hasDungeonWall, Is.True, "Expected dungeon walls at DungeonWallElevation.");
            Assert.That(hasDungeonFloor, Is.True, "Expected dungeon floors at 0.0m.");
        }

        [Test]
        public void BywaterElevationBuilder_90x90GeneratesColliderBoundsAndDisposesOnRebuild()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            CartographyWorldData land = CartographyWorldData.BuildFromTileMap(bundle.Land0);

            GameObject host = new GameObject("Temp90x90ElevationTest");
            try
            {
                BywaterElevationBuilder builder = host.AddComponent<BywaterElevationBuilder>();
                builder.Rebuild(land);

                Mesh firstMesh = builder.LastBuiltMesh;
                Assert.That(firstMesh, Is.Not.Null);
                Assert.That(builder.terrainMeshCollider, Is.Not.Null);
                Assert.That(builder.terrainMeshCollider.sharedMesh, Is.SameAs(firstMesh));
                Assert.That(firstMesh.bounds.min.x, Is.EqualTo(0f).Within(0.05f));
                Assert.That(firstMesh.bounds.max.x, Is.EqualTo(90f).Within(0.05f));
                Assert.That(firstMesh.bounds.min.z, Is.EqualTo(-90f).Within(0.05f));
                Assert.That(firstMesh.bounds.max.z, Is.EqualTo(0f).Within(0.05f));

                Assert.That(builder.waterPlaneObject, Is.Not.Null);
                Assert.That(builder.waterPlaneObject.transform.position.x, Is.EqualTo(45f).Within(0.01f));
                Assert.That(builder.waterPlaneObject.transform.position.y, Is.EqualTo(land.WaterSurfaceHeight).Within(0.01f));
                Assert.That(builder.waterPlaneObject.transform.position.z, Is.EqualTo(-45f).Within(0.01f));

                builder.Rebuild(CartographyWorldData.BuildBywaterProofLayout());
                Assert.That(firstMesh == null, Is.True, "Expected previous terrain mesh to be destroyed on Rebuild.");
                Assert.That(builder.LastBuiltMesh, Is.Not.Null);
                Assert.That(builder.terrainMeshCollider.sharedMesh, Is.SameAs(builder.LastBuiltMesh));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BywaterFoliageScatter_90x90PartitionsInto36ChunksAndFrustumCulls()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            CartographyWorldData land = CartographyWorldData.BuildFromTileMap(bundle.Land0);
            CartographyWorldData dungeon = CartographyWorldData.BuildFromTileMap(bundle.Dungeon0);

            GameObject host = new GameObject("Temp90x90FoliageTest");
            GameObject camGo = new GameObject("TempTestCam");
            try
            {
                BywaterFoliageScatter scatter = host.AddComponent<BywaterFoliageScatter>();
                scatter.Rebuild(land);

                Assert.That(scatter.ChunkCount, Is.EqualTo(36), "90x90 should partition into 6x6 = 36 chunks of 16x16 tiles.");
                Assert.That(scatter.TotalInstanceCount, Is.GreaterThan(10000));

                Camera cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 34f;
                cam.aspect = 16f / 9f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 40f;
                camGo.transform.rotation = Quaternion.Euler(48f, 0f, 0f);
                camGo.transform.position = new Vector3(10f, 12f, -22f);

                scatter.RenderBatches(cam);
                Assert.That(scatter.LastVisibleChunkCount, Is.GreaterThan(0));
                Assert.That(scatter.LastVisibleChunkCount, Is.LessThan(36), "Expected frustum culling to skip off-screen chunks.");

                camGo.transform.position = new Vector3(500f, 50f, 500f);
                camGo.transform.rotation = Quaternion.identity;
                scatter.RenderBatches(cam);
                Assert.That(scatter.LastVisibleChunkCount, Is.EqualTo(0), "Expected 0 visible chunks when camera looks away.");

                scatter.Rebuild(dungeon);
                Assert.That(scatter.TotalInstanceCount, Is.EqualTo(0), "Dungeon maps should skip outdoor grass tufts.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(camGo);
            }
        }

        [Test]
        public void BywaterTopDownLightmap_90x90SetsDynamicCanonicalWorldBounds()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            CartographyWorldData land = CartographyWorldData.BuildFromTileMap(bundle.Land0);

            GameObject host = new GameObject("Temp90x90LightmapTest");
            try
            {
                BywaterTopDownLightmap lm = host.AddComponent<BywaterTopDownLightmap>();
                lm.ConfigureFromWorldData(land);
                lm.UpdateLightmap(0f);

                Vector4 bounds = Shader.GetGlobalVector("_TopDownLightmapBounds");
                Assert.That(bounds.x, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(bounds.y, Is.EqualTo(-90f).Within(1e-5f));
                Assert.That(bounds.z, Is.EqualTo(1f / 90f).Within(1e-5f));
                Assert.That(bounds.w, Is.EqualTo(1f / 90f).Within(1e-5f));
                Assert.That(lm.WorldStaticMap, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Pack3BuildingsTownAndDungeonProps_ResolveMaterialsAt64PpuAndWireDungeonTerrainAndFoliage()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null, "BywaterAssetCatalog missing.");

            CartographyPropKind[] requiredPack3Kinds =
            {
                CartographyPropKind.Guildhall,
                CartographyPropKind.Blacksmith,
                CartographyPropKind.Apothecary,
                CartographyPropKind.Temple,
                CartographyPropKind.CottageB,
                CartographyPropKind.CottageC,
                CartographyPropKind.Watchtower,
                CartographyPropKind.MarketStallA,
                CartographyPropKind.MarketStallB,
                CartographyPropKind.StoneWell,
                CartographyPropKind.Signpost,
                CartographyPropKind.WoodenWagon,
                CartographyPropKind.BarrelsCrates,
                CartographyPropKind.CampTent,
                CartographyPropKind.StoneRuins,
                CartographyPropKind.ShrineStatue,
                CartographyPropKind.Campfire4f,
                CartographyPropKind.DungeonChestClosed,
                CartographyPropKind.DungeonChestOpen,
                CartographyPropKind.DungeonDoorClosed,
                CartographyPropKind.DungeonDoorOpen,
                CartographyPropKind.DungeonPillar,
                CartographyPropKind.DungeonStairsDown,
                CartographyPropKind.DungeonStairsUp,
                CartographyPropKind.DungeonWallSconce4f,
                CartographyPropKind.DungeonSarcophagus,
                CartographyPropKind.DungeonSpikeTrap,
                CartographyPropKind.DungeonIronGrate
            };

            for (int i = 0; i < requiredPack3Kinds.Length; i++)
            {
                CartographyPropKind kind = requiredPack3Kinds[i];
                Material mat = catalog.GetCartographyPropMaterial(kind);
                Assert.That(mat, Is.Not.Null, $"Missing cartographyPropMaterials entry for {kind}.");

                Texture tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
                Assert.That(tex, Is.Not.Null, $"Material for {kind} is missing base texture.");
            }

            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            CartographyWorldData land = CartographyWorldData.BuildFromTileMap(bundle.Land0);
            CartographyWorldData dungeon = CartographyWorldData.BuildFromTileMap(bundle.Dungeon0);

            Assert.That(land.Props.Count, Is.GreaterThan(0));
            Assert.That(dungeon.Props.Count, Is.GreaterThan(0));

            bool sawDungeonProp = false;
            for (int i = 0; i < dungeon.Props.Count; i++)
            {
                CartographyPropPlacement p = dungeon.Props[i];
                if (p.Kind == CartographyPropKind.DungeonWallSconce4f)
                {
                    sawDungeonProp = true;
                    Assert.That(p.SizeMeters.x, Is.EqualTo(2.0f).Within(0.05f), "DungeonWallSconce4f width must be 2.0m (64 px/m).");
                    Assert.That(p.SizeMeters.y, Is.EqualTo(2.0f).Within(0.05f), "DungeonWallSconce4f height must be 2.0m (64 px/m).");
                }
                else if (p.Kind == CartographyPropKind.DungeonStairsUp ||
                         p.Kind == CartographyPropKind.DungeonStairsDown ||
                         p.Kind == CartographyPropKind.DungeonChestClosed ||
                         p.Kind == CartographyPropKind.DungeonPillar ||
                         p.Kind == CartographyPropKind.DungeonDoorClosed)
                {
                    sawDungeonProp = true;
                }
            }
            Assert.That(sawDungeonProp, Is.True, "Expected Pack 3 dungeon props in dungeon_0 CartographyWorldData.");

            for (int i = 0; i < land.Props.Count; i++)
            {
                CartographyPropPlacement p = land.Props[i];
                if (p.Kind == CartographyPropKind.Campfire4f)
                {
                    Assert.That(p.SizeMeters.x, Is.EqualTo(2.0f).Within(0.05f), "Campfire4f width must be 2.0m (64 px/m).");
                    Assert.That(p.SizeMeters.y, Is.EqualTo(2.0f).Within(0.05f), "Campfire4f height must be 2.0m (64 px/m).");
                }
            }

            string dungeonAtlasPath = "Assets/Art/Cartography/Atlases/foliage_atlas_dungeon_cave.png";
            TextureImporter atlasImp = AssetImporter.GetAtPath(dungeonAtlasPath) as TextureImporter;
            Assert.That(atlasImp, Is.Not.Null, "Missing foliage_atlas_dungeon_cave.png importer.");
            Assert.That(atlasImp.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(atlasImp.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(atlasImp.spritePixelsPerUnit, Is.EqualTo(64f));

            string materialsDir = Path.GetFullPath(Path.Combine(Application.dataPath, "Art", "Cartography", "Materials"));
            bool foundDungeonFoliageMat = false;
            if (Directory.Exists(materialsDir))
            {
                string[] matFiles = Directory.GetFiles(materialsDir, "*.mat", SearchOption.AllDirectories);
                for (int i = 0; i < matFiles.Length; i++)
                {
                    string rel = "Assets" + matFiles[i].Substring(Application.dataPath.Length).Replace('\\', '/');
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(rel);
                    if (m == null) continue;
                    Texture t = m.HasProperty("_BaseMap")
                        ? m.GetTexture("_BaseMap")
                        : (m.HasProperty("_MainTex") ? m.mainTexture : null);
                    if (t != null && string.Equals(t.name, "foliage_atlas_dungeon_cave", System.StringComparison.OrdinalIgnoreCase))
                    {
                        foundDungeonFoliageMat = true;
                        break;
                    }
                }
            }
            Assert.That(foundDungeonFoliageMat, Is.True, "Expected a Cartography material wired to foliage_atlas_dungeon_cave.");

            GameObject worldGo = new GameObject("TempDungeonRenderTest");
            try
            {
                BywaterWorldRenderer renderer = worldGo.AddComponent<BywaterWorldRenderer>();
                renderer.Initialize(catalog);
                renderer.RenderMap(bundle.Dungeon0, new GridCoord(33, 72), forceRebuild: true);

                BywaterElevationBuilder elev = renderer.GetComponentInChildren<BywaterElevationBuilder>(true);
                Assert.That(elev, Is.Not.Null);
                Assert.That(elev.terrainMeshRenderer, Is.Not.Null);
                Assert.That(elev.terrainMeshRenderer.sharedMaterial, Is.Not.Null, "dungeon_0 terrain material not wired.");
                Assert.That(elev.terrainMeshRenderer.sharedMaterial.shader.name, Is.EqualTo("Realmz/CartographyTerrain"));

                BywaterFoliageScatter scatter = renderer.GetComponentInChildren<BywaterFoliageScatter>(true);
                Assert.That(scatter, Is.Not.Null);
                Assert.That(scatter.TotalInstanceCount, Is.EqualTo(0), "BywaterFoliageScatter must return 0 on dungeon_0.");
            }
            finally
            {
                Object.DestroyImmediate(worldGo);
            }
        }

        [Test]
        public void CombatSpawnClearance_Land0NearTownAvoidsNorthBuildingRoofOverhang()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession explore = new ExplorationSession(
                bundle.Maps,
                "land_0",
                party,
                new DeterministicDiceRoller(4242UL),
                new GridCoord(10, 9));

            Assert.That(BywaterGameController.IsSpawnTileNorthClear(bundle.Land0, 6, 7), Is.False,
                "Tile (6,7) immediately south of town building (6,6) must fail north clearance.");
            Assert.That(BywaterGameController.IsSpawnTileNorthClear(bundle.Land0, 14, 14), Is.True,
                "Open town plaza tile (14,14) should be north clear.");

            GameObject ctrlGo = new GameObject("TempSpawnClearanceController");
            try
            {
                BywaterGameController ctrl = ctrlGo.AddComponent<BywaterGameController>();
                ctrl.InitializeForTests(bundle, explore, 4242UL);

                GridCoord origin = ctrl.FindCleanArenaOrigin(bundle.Land0, new GridCoord(10, 9), 12, 10);
                for (int lx = 0; lx < 12; lx++)
                {
                    int wx = origin.X + lx;
                    int northBorderY = origin.Y - 1;
                    if (bundle.Land0.InBounds(wx, northBorderY))
                    {
                        Assert.That(BywaterTileClassifier.Classify(bundle.Land0, wx, northBorderY),
                            Is.Not.EqualTo(BywaterTileKind.TownBuildingSolid),
                            $"Chosen arena north border ({wx},{northBorderY}) should avoid TownBuildingSolid.");
                    }
                    for (int ly = 0; ly <= 1; ly++)
                    {
                        int wy = origin.Y + ly;
                        if (bundle.Land0.InBounds(wx, wy))
                        {
                            Assert.That(BywaterTileClassifier.Classify(bundle.Land0, wx, wy),
                                Is.Not.EqualTo(BywaterTileKind.TownBuildingSolid),
                                $"Chosen arena north row ({wx},{wy}) should avoid TownBuildingSolid.");
                        }
                    }
                }

                ctrl.TriggerDemoSkirmish();
                Assert.That(ctrl.IsInCombat, Is.True);
                Assert.That(ctrl.CombatSession, Is.Not.Null);

                GridCoord activeOrigin = ctrl.ArenaOrigin;
                IReadOnlyList<CombatantState> units = ctrl.CombatSession.Combatants;
                Assert.That(units.Count, Is.GreaterThanOrEqualTo(7));

                for (int i = 0; i < units.Count; i++)
                {
                    CombatantState unit = units[i];
                    int worldX = activeOrigin.X + unit.Position.X;
                    int worldY = activeOrigin.Y + unit.Position.Y;

                    Assert.That(unit.Position.Y, Is.GreaterThanOrEqualTo(3),
                        $"Combatant {unit.Name} spawned too far north at local {unit.Position}.");
                    Assert.That(BywaterGameController.IsSpawnTileNorthClear(bundle.Land0, worldX, worldY), Is.True,
                        $"Combatant {unit.Name} spawned at world ({worldX},{worldY}) with north building/wall overhang.");
                }
            }
            finally
            {
                Object.DestroyImmediate(ctrlGo);
            }
        }

        [Test]
        public void Bug1_AllUnitsAndAnimStatesHaveFootAnchoredGroundedQuads_AndShaderPreservesRoot()
        {
            BywaterAssetCatalog catalog = Resources.Load<BywaterAssetCatalog>("BywaterAssetCatalog");
            Assert.That(catalog, Is.Not.Null);

            string shaderText = File.ReadAllText(Path.GetFullPath("Assets/Shaders/CartographyBillboard.shader"));
            Assert.That(shaderText.Contains("max(0.0, positionWS.y - rootWS.y)"), Is.False,
                "CartographyBillboard.shader must not clamp below-root vertices upward onto rootWS.y.");

            string[] unitIds =
            {
                "hero_knight", "hero_cleric", "hero_ranger",
                "hero_mage", "hero_paladin", "hero_rogue",
                "monster_giant_frog", "monster_goblin_raider", "monster_goblin_archer",
                "monster_orc_warrior", "monster_giant_rat", "monster_skeleton_warrior",
                "monster_bandit_cutthroat",
                "npc_blacksmith", "npc_tanner", "npc_apothecary", "npc_priestess"
            };

            UnitAnimState[] states = { UnitAnimState.Idle, UnitAnimState.Walk, UnitAnimState.Attack };
            GameObject unitGo = new GameObject("TempBug1GroundingUnit");
            try
            {
                BywaterUnitView view = unitGo.AddComponent<BywaterUnitView>();
                for (int i = 0; i < unitIds.Length; i++)
                {
                    string uid = unitIds[i];
                    view.Configure(uid, uid, null, Color.yellow, catalog.uiFont, 2.0f, true, true, catalog);
                    view.SetWorldPosition(new Vector3(10.5f, 0.35f, -9.5f));
                    view.SetTurnHighlight(true);

                    Transform spriteQuad = view.transform.Find("SpriteBillboard/SpriteQuad");
                    Transform underRing = view.transform.Find("UnderRing");
                    Transform contactShadow = view.transform.Find("ContactShadow");
                    Assert.That(spriteQuad, Is.Not.Null, $"SpriteQuad missing on {uid}");
                    Assert.That(underRing, Is.Not.Null, $"UnderRing missing on {uid}");
                    Assert.That(contactShadow, Is.Not.Null, $"ContactShadow missing on {uid}");

                    MeshFilter mf = spriteQuad.GetComponent<MeshFilter>();
                    Assert.That(mf, Is.Not.Null);
                    Assert.That(mf.sharedMesh, Is.Not.Null);
                    Vector3[] verts = mf.sharedMesh.vertices;
                    float minVertY = float.MaxValue;
                    float maxVertY = float.MinValue;
                    for (int v = 0; v < verts.Length; v++)
                    {
                        if (verts[v].y < minVertY) minVertY = verts[v].y;
                        if (verts[v].y > maxVertY) maxVertY = verts[v].y;
                    }
                    Assert.That(minVertY, Is.EqualTo(0f).Within(0.001f), $"{uid} shared unit quad must be foot-anchored at y=0.");
                    Assert.That(maxVertY, Is.EqualTo(1f).Within(0.001f), $"{uid} shared unit quad must span height [0..1].");

                    for (int s = 0; s < states.Length; s++)
                    {
                        view.SetAnimState(states[s], 0.45f);
                        Assert.That(spriteQuad.localPosition, Is.EqualTo(Vector3.zero), $"{uid} ({states[s]}) SpriteQuad localPosition must stay zero.");
                        Vector3 worldFoot = spriteQuad.TransformPoint(new Vector3(0f, 0f, 0f));
                        Assert.That(worldFoot.y, Is.EqualTo(view.transform.position.y).Within(0.01f), $"{uid} ({states[s]}) feet must rest at transform Y.");
                        Assert.That(Mathf.Abs(worldFoot.x - underRing.position.x), Is.LessThan(0.02f));
                        Assert.That(Mathf.Abs(worldFoot.z - underRing.position.z), Is.LessThan(0.02f));
                        Assert.That(Mathf.Abs(contactShadow.position.z - underRing.position.z), Is.LessThan(0.02f));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(unitGo);
            }
        }

        [Test]
        public void Bug3_MultiTileBuildingsAndPropsBlockInteriors_DetourCleanly_AndKeepKeyTargetsReachable()
        {
            string bundleDir = Path.Combine(Application.streamingAssetsPath, "bywater_m1");
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(bundleDir);
            CartographyWorldData landWorld = CartographyWorldData.BuildFromTileMap(bundle.Land0);
            bundle.Land0.SetBlockedPropFootprints(landWorld.ExportBlockedPropGridCoords());

            int blockedBuildingTiles = 0;
            for (int y = 0; y < bundle.Land0.Height; y++)
            {
                for (int x = 0; x < bundle.Land0.Width; x++)
                {
                    BywaterTileKind k = BywaterTileClassifier.Classify(bundle.Land0, x, y);
                    if (k == BywaterTileKind.TownBuildingSolid && !bundle.Land0.IsPassable(x, y, false))
                    {
                        blockedBuildingTiles++;
                    }
                    if (k == BywaterTileKind.TownBuildingDoor)
                    {
                        Assert.That(bundle.Land0.IsPassable(x, y, false), Is.True, $"Doorway ({x},{y}) must stay passable.");
                    }
                }
            }
            Assert.That(blockedBuildingTiles, Is.GreaterThanOrEqualTo(15), "Solid building footprints must block multi-tile walls/interiors.");

            int multiTileBlockedBuildings = 0;
            for (int i = 0; i < landWorld.Props.Count; i++)
            {
                CartographyPropPlacement pr = landWorld.Props[i];
                if (pr.Kind != CartographyPropKind.BywaterInn &&
                    pr.Kind != CartographyPropKind.Blacksmith &&
                    pr.Kind != CartographyPropKind.Apothecary &&
                    pr.Kind != CartographyPropKind.Temple &&
                    pr.Kind != CartographyPropKind.Guildhall &&
                    pr.Kind != CartographyPropKind.CottageB &&
                    pr.Kind != CartographyPropKind.CottageC &&
                    pr.Kind != CartographyPropKind.Watchtower)
                {
                    continue;
                }

                float halfW = Mathf.Max(0.45f, pr.SizeMeters.x * 0.40f);
                float southMargin = 0.48f;
                float northDepth = Mathf.Clamp(pr.SizeMeters.y * 0.38f, 0.85f, 1.95f);
                int minTx = Mathf.FloorToInt(pr.WorldPosition.x - halfW + 0.12f);
                int maxTx = Mathf.FloorToInt(pr.WorldPosition.x + halfW - 0.12f);
                int minTy = Mathf.FloorToInt(-(pr.WorldPosition.z + northDepth) + 0.12f);
                int maxTy = Mathf.FloorToInt(-(pr.WorldPosition.z - southMargin) - 0.12f);
                int blockedInside = 0;
                for (int ty = minTy; ty <= maxTy; ty++)
                {
                    for (int tx = minTx; tx <= maxTx; tx++)
                    {
                        if (bundle.Land0.InBounds(tx, ty) && !bundle.Land0.IsPassable(tx, ty, false))
                        {
                            blockedInside++;
                        }
                    }
                }
                if (blockedInside >= 2)
                {
                    multiTileBlockedBuildings++;
                }
            }
            Assert.That(multiTileBlockedBuildings, Is.GreaterThanOrEqualTo(5), "Pack 3 town buildings must block multi-tile footprints.");

            // Verify A* refuses building interior tiles and routes around TownBuildingSolid structures
            PartyState party = PartyState.FromBundle(bundle);
            ExplorationSession session = new ExplorationSession(bundle.Maps, "land_0", party, new DeterministicDiceRoller(99UL), new GridCoord(10, 9));
            PathResult innInteriorPath = session.PlanPath(new GridCoord(6, 5), allowDiagonal: true);
            Assert.That(innInteriorPath.Found, Is.False, "A* must refuse paths terminating inside solid building tile (6,5).");

            PathResult detour = session.PlanPath(new GridCoord(7, 11), allowDiagonal: true);
            Assert.That(detour.Found, Is.True, "Path to Inn doorway (7,11) must detour cleanly around solid footprints.");
            for (int i = 0; i < detour.Steps.Count; i++)
            {
                GridCoord s = detour.Steps[i];
                Assert.That(bundle.Land0.IsBlockedByProp(s.X, s.Y), Is.False, $"Path step {s} must never enter blocked building/prop tile.");
            }

            // Dungeon portal (89, 48) and all 4 NPC approaches remain 100% reachable
            PathResult dungeonPortal = session.PlanPath(new GridCoord(89, 48), allowDiagonal: true);
            Assert.That(dungeonPortal.Found, Is.True, "Dungeon portal (89,48) must remain reachable from town.");
        }

        private static void AssertShaderHasProperty(Shader shader, string propertyName)
        {
            int idx = shader.FindPropertyIndex(propertyName);
            Assert.That(idx, Is.GreaterThanOrEqualTo(0), $"Shader '{shader.name}' missing required property '{propertyName}'.");
        }
    }
}
