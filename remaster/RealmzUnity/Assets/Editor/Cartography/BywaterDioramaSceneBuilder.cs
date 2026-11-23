using System.Collections.Generic;
using Realmz.Cartography;
using Realmz.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Realmz.EditorTools.Cartography
{
    /// <summary>
    /// Builds Assets/Scenes/BywaterDiorama_Proof.unity: an 18x18 Songs-of-Conquest-style
    /// vertical slice proof scene with 3-tier terrain, GPU-instanced pixel grass scatter,
    /// top-down cloud/canopy/lantern lightmap, sunbeam motes, and URP HDR post-processing.
    /// Leaves EditorBuildSettings untouched (M1_Bywater_Slice.unity stays at index 0).
    /// </summary>
    public static class BywaterDioramaSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/BywaterDiorama_Proof.unity";
        public const string VolumeProfilePath = "Assets/Art/Cartography/BywaterDiorama_VolumeProfile.asset";

        [MenuItem("Realmz/Cartography/Build Bywater 18x18 Proof Scene")]
        public static void BuildProofScene()
        {
            CartographyAssetImporter.ImportAll();
            EnsureFolder("Assets/Scenes");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Golden-hour trilight ambient lighting
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.56f, 0.78f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.38f, 0.35f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.18f, 1f);
            RenderSettings.ambientIntensity = 0.72f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.54f, 0.68f, 0.84f, 1f);
            RenderSettings.fogStartDistance = 38f;
            RenderSettings.fogEndDistance = 95f;

            // 2. URP HDR Camera (FOV 34, pitch 48 deg) framing the 18x18 diorama
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.46f, 0.68f, 1f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.allowHDR = true;
            cam.allowMSAA = false;

            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.volumeLayerMask = ~0;
            camData.antialiasing = AntialiasingMode.None;

            Vector3 dioramaFocus = new Vector3(9.0f, 0.15f, 8.2f);
            Quaternion camRot = Quaternion.Euler(48f, 0f, 0f);
            camGo.transform.rotation = camRot;
            camGo.transform.position = dioramaFocus - (camRot * Vector3.forward) * 19.2f;

            // 3. Warm Directional Sun with crisp soft shadows
            GameObject sunGo = new GameObject("Directional Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.941f, 0.831f, 1f);
            sun.intensity = 2.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.84f;
            sunGo.transform.rotation = Quaternion.Euler(46f, -34f, 0f);

            // 4. Cloned VolumeProfile (Bloom + Neutral Tonemapping + SplitToning + subtle DepthOfField)
            VolumeProfile profile = BuildOrUpdateVolumeProfile();
            GameObject volGo = new GameObject("Global Volume (Cartography)");
            Volume vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = profile;

            // 5. Build 18x18 world layout, terrain, water, foliage scatter, and top-down lightmap
            CartographyWorldData worldData = CartographyWorldData.BuildBywaterProofLayout();

            GameObject terrainGo = new GameObject("BywaterCartographyTerrain");
            BywaterElevationBuilder elevationBuilder = terrainGo.AddComponent<BywaterElevationBuilder>();
            elevationBuilder.terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(CartographyAssetImporter.TerrainMaterialPath);
            elevationBuilder.waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(CartographyAssetImporter.WaterMaterialPath);
            elevationBuilder.Rebuild(worldData);

            GameObject foliageGo = new GameObject("BywaterFoliageScatter");
            BywaterFoliageScatter foliageScatter = foliageGo.AddComponent<BywaterFoliageScatter>();
            foliageScatter.foliageMaterial = AssetDatabase.LoadAssetAtPath<Material>(CartographyAssetImporter.FoliageMaterialPath);
            foliageScatter.RebuildScatter(worldData);

            GameObject lightmapGo = new GameObject("BywaterTopDownLightmap");
            BywaterTopDownLightmap topDownLightmap = lightmapGo.AddComponent<BywaterTopDownLightmap>();
            topDownLightmap.ConfigureFromWorldData(worldData);
            topDownLightmap.UpdateLightmap(0f);

            // 6. Spawn props, buildings, trees, units, lantern point lights, and sunbeam motes
            Transform heroTransform = SpawnPropsAndUnits(worldData, cam);
            SpawnAtmosphereLightsAndMotes();

            // 7. Wire runtime controller
            GameObject controllerGo = new GameObject("BywaterDioramaController");
            BywaterDioramaController controller = controllerGo.AddComponent<BywaterDioramaController>();
            controller.topDownLightmap = topDownLightmap;
            controller.foliageScatter = foliageScatter;
            controller.heroTransform = heroTransform;
            controller.targetCamera = cam;
            controller.SyncGlobals(0f);

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[BywaterDioramaSceneBuilder] Saved proof scene: " + ScenePath);
        }

        private static Transform SpawnPropsAndUnits(CartographyWorldData worldData, Camera cam)
        {
            GameObject root = new GameObject("BywaterProps");
            Mesh unitQuad = BuildPropBillboardQuadMesh();
            Transform heroTransform = null;

            IReadOnlyList<CartographyPropPlacement> props = worldData.Props;
            for (int i = 0; i < props.Count; i++)
            {
                CartographyPropPlacement placement = props[i];
                GameObject propGo = new GameObject($"{placement.Kind}_{i}");
                propGo.transform.SetParent(root.transform, false);
                propGo.transform.position = placement.WorldPosition;

                if (placement.Kind == CartographyPropKind.StoneBridge)
                {
                    // Curved 64 px/m pixel-locked 3D cobblestone arch bridge spanning river z=2.6..5.4
                    BuildArchedStoneBridge(propGo, placement.SizeMeters.x, placement.SizeMeters.y);
                }
                else
                {
                    Material mat = CartographyAssetImporter.LoadOrCreatePropMaterial(placement.Kind, out _);
                    propGo.transform.localScale = new Vector3(placement.SizeMeters.x, placement.SizeMeters.y, 1f);

                    MeshFilter mf = propGo.AddComponent<MeshFilter>();
                    mf.sharedMesh = unitQuad;

                    MeshRenderer mr = propGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.receiveShadows = true;

                    PixelBillboard bb = propGo.AddComponent<PixelBillboard>();
                    bb.targetCamera = cam;
                    Vector3 fwd = cam.transform.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude > 0.0001f)
                    {
                        propGo.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                    }
                }

                if (placement.Kind == CartographyPropKind.Knight)
                {
                    heroTransform = propGo.transform;
                }
            }

            return heroTransform;
        }

        private static void SpawnAtmosphereLightsAndMotes()
        {
            GameObject atmoRoot = new GameObject("BywaterDioramaAtmosphere");

            // Warm Inn Door Lantern
            CreateWarmLanternLight(atmoRoot.transform, "InnDoorLantern", new Vector3(5.2f, 0.95f, 8.9f), 4.0f, 1.40f);
            // Warm Bridge Brazier Light
            CreateWarmLanternLight(atmoRoot.transform, "BridgeBrazierLight", new Vector3(9.5f, 0.85f, 5.4f), 3.4f, 1.15f);

            // Sunbeam Dust Motes floating subtly above the meadow
            GameObject psGo = new GameObject("SunbeamMotes");
            psGo.transform.SetParent(atmoRoot.transform, false);
            psGo.transform.position = new Vector3(9.0f, 2.5f, 8.5f);

            ParticleSystem ps = psGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.maxParticles = 45;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 7.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.065f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.95f, 0.74f, 0.36f),
                new Color(1.0f, 0.98f, 0.88f, 0.22f));
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 8f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 3.5f, 16f);

            ParticleSystemRenderer psr = psGo.GetComponent<ParticleSystemRenderer>();
            psr.shadowCastingMode = ShadowCastingMode.Off;
            psr.receiveShadows = false;
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (particleShader != null)
            {
                string moteMatPath = CartographyAssetImporter.MaterialsDir + "/Cartography_SunbeamMote.mat";
                Material moteMat = AssetDatabase.LoadAssetAtPath<Material>(moteMatPath);
                if (moteMat == null)
                {
                    moteMat = new Material(particleShader) { name = "Cartography_SunbeamMote" };
                    AssetDatabase.CreateAsset(moteMat, moteMatPath);
                }
                else if (moteMat.shader != particleShader)
                {
                    moteMat.shader = particleShader;
                }
                if (moteMat.HasProperty("_BaseColor")) moteMat.SetColor("_BaseColor", new Color(1f, 0.96f, 0.78f, 0.36f));
                EditorUtility.SetDirty(moteMat);
                psr.sharedMaterial = moteMat;
            }
        }

        private static void CreateWarmLanternLight(Transform parent, string name, Vector3 pos, float range, float intensity)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1.0f, 0.68f, 0.30f, 1f);
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }

        private static VolumeProfile BuildOrUpdateVolumeProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            if (profile.components != null)
            {
                profile.components.RemoveAll(c => c == null);
            }

            Tonemapping tonemapping = GetOrAddVolumeComponent<Tonemapping>(profile);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            ColorAdjustments colorAdj = GetOrAddVolumeComponent<ColorAdjustments>(profile);
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = -0.14f;
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 14f;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 14f;

            SplitToning split = GetOrAddVolumeComponent<SplitToning>(profile);
            split.highlights.overrideState = true;
            split.highlights.value = new Color(1.0f, 0.95f, 0.84f, 1f);
            split.shadows.overrideState = true;
            split.shadows.value = new Color(0.84f, 0.90f, 1.0f, 1f);
            split.balance.overrideState = true;
            split.balance.value = 10f;

            Bloom bloom = GetOrAddVolumeComponent<Bloom>(profile);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.60f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.14f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.45f;

            // Keep crisp razor-sharp pixel art across entire diorama (disable soft DoF blur)
            DepthOfField dof = GetOrAddVolumeComponent<DepthOfField>(profile);
            dof.active = false;
            dof.mode.overrideState = true;
            dof.mode.value = DepthOfFieldMode.Off;

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T GetOrAddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T comp))
            {
                comp = profile.Add<T>(true);
            }
            if (!AssetDatabase.Contains(comp))
            {
                AssetDatabase.AddObjectToAsset(comp, profile);
            }
            return comp;
        }

        private static Mesh BuildPropBillboardQuadMesh()
        {
            const string meshPath = "Assets/Art/Cartography/Materials/CartographyPropQuad.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null)
            {
                return existing;
            }

            Mesh mesh = new Mesh
            {
                name = "Cartography_PropBillboardQuad"
            };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3( 0.5f, 0f, 0f),
                new Vector3( 0.5f, 1f, 0f),
                new Vector3(-0.5f, 1f, 0f)
            });
            mesh.SetNormals(new[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });
            mesh.SetUVs(1, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();

            AssetDatabase.CreateAsset(mesh, meshPath);
            return mesh;
        }

        private static void BuildArchedStoneBridge(GameObject root, float widthMeters, float lengthMeters)
        {
            const string deckMeshPath = "Assets/Art/Cartography/Materials/Cartography_BridgeDeck.asset";
            const string curbMeshPath = "Assets/Art/Cartography/Materials/Cartography_BridgeCurbs.asset";

            Material deckMat = LoadOrCreateBridgeLitMaterial("Cartography_Bridge_DeckMat", CartographyAssetImporter.TilesDir + "/cobble_plaza.png", CartographyAssetImporter.TilesDir + "/cobble_plaza_n.png");
            Material curbMat = LoadOrCreateBridgeLitMaterial("Cartography_Bridge_CurbMat", CartographyAssetImporter.TilesDir + "/cliff_rock.png", CartographyAssetImporter.TilesDir + "/cliff_rock_n.png");

            Mesh deckMesh = BuildBridgeDeckMesh(widthMeters * 0.86f, lengthMeters);
            deckMesh = SaveOrUpdateMeshAsset(deckMesh, deckMeshPath);

            GameObject deckGo = new GameObject("BridgeCobbleDeck");
            deckGo.transform.SetParent(root.transform, false);
            deckGo.AddComponent<MeshFilter>().sharedMesh = deckMesh;
            MeshRenderer deckMr = deckGo.AddComponent<MeshRenderer>();
            deckMr.sharedMaterial = deckMat;
            deckMr.shadowCastingMode = ShadowCastingMode.On;
            deckMr.receiveShadows = true;

            Mesh curbMesh = BuildBridgeParapetAndArchMesh(widthMeters, lengthMeters);
            curbMesh = SaveOrUpdateMeshAsset(curbMesh, curbMeshPath);

            GameObject curbGo = new GameObject("BridgeStoneParapets");
            curbGo.transform.SetParent(root.transform, false);
            curbGo.AddComponent<MeshFilter>().sharedMesh = curbMesh;
            MeshRenderer curbMr = curbGo.AddComponent<MeshRenderer>();
            curbMr.sharedMaterial = curbMat;
            curbMr.shadowCastingMode = ShadowCastingMode.On;
            curbMr.receiveShadows = true;
        }

        private static float EvalBridgeArchY(float zNorm)
        {
            float clamped = Mathf.Clamp(zNorm, -1f, 1f);
            float arch = Mathf.Cos(clamped * Mathf.PI * 0.5f);
            return 0.04f + 0.36f * arch;
        }

        private static float EvalBridgeUndersideY(float zNorm)
        {
            float az = Mathf.Abs(zNorm);
            if (az < 0.62f)
            {
                float t = az / 0.62f;
                return -0.52f + 0.66f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
            }
            return -0.52f;
        }

        private static Mesh BuildBridgeDeckMesh(float roadWidth, float lengthMeters)
        {
            const int segs = 12;
            float halfW = roadWidth * 0.5f;
            float halfL = lengthMeters * 0.5f;

            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs * 2f - 1f;
                float t1 = (float)(i + 1) / segs * 2f - 1f;
                float z0 = t0 * halfL;
                float z1 = t1 * halfL;
                float y0 = EvalBridgeArchY(t0);
                float y1 = EvalBridgeArchY(t1);
                float v0 = (z0 + halfL) * 0.5f;
                float v1 = (z1 + halfL) * 0.5f;
                float u0 = 0f;
                float u1 = roadWidth * 0.5f;

                AddQuad(
                    verts, uvs, tris,
                    new Vector3(-halfW, y0, z0),
                    new Vector3(-halfW, y1, z1),
                    new Vector3( halfW, y1, z1),
                    new Vector3( halfW, y0, z0),
                    new Vector2(u0, v0),
                    new Vector2(u0, v1),
                    new Vector2(u1, v1),
                    new Vector2(u1, v0));
            }

            Mesh m = new Mesh { name = "Cartography_BridgeDeck" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildBridgeParapetAndArchMesh(float totalWidth, float lengthMeters)
        {
            const int segs = 12;
            float halfTotal = totalWidth * 0.5f;
            float halfL = lengthMeters * 0.5f;
            float curbW = 0.24f;
            float curbRise = 0.28f;

            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            // Left and right raised stone parapets + arched retaining skirts down to -0.52m
            for (int side = -1; side <= 1; side += 2)
            {
                float xOuter = side < 0 ? -halfTotal : halfTotal - curbW;
                float xInner = xOuter + curbW;
                float xMin = Mathf.Min(xOuter, xInner);
                float xMax = Mathf.Max(xOuter, xInner);

                for (int i = 0; i < segs; i++)
                {
                    float t0 = (float)i / segs * 2f - 1f;
                    float t1 = (float)(i + 1) / segs * 2f - 1f;
                    float z0 = t0 * halfL;
                    float z1 = t1 * halfL;
                    float yTop0 = EvalBridgeArchY(t0) + curbRise;
                    float yTop1 = EvalBridgeArchY(t1) + curbRise;
                    float yBot0 = EvalBridgeUndersideY(t0);
                    float yBot1 = EvalBridgeUndersideY(t1);
                    float v0 = (z0 + halfL) * 0.5f;
                    float v1 = (z1 + halfL) * 0.5f;

                    // Top parapet face
                    AddQuad(verts, uvs, tris,
                        new Vector3(xMin, yTop0, z0), new Vector3(xMin, yTop1, z1),
                        new Vector3(xMax, yTop1, z1), new Vector3(xMax, yTop0, z0),
                        new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(0.18f, v1), new Vector2(0.18f, v0));

                    // Outer / -X facing vertical stone arch wall face
                    AddQuad(verts, uvs, tris,
                        new Vector3(xMin, yBot1, z1), new Vector3(xMin, yTop1, z1),
                        new Vector3(xMin, yTop0, z0), new Vector3(xMin, yBot0, z0),
                        new Vector2(v1, 0f), new Vector2(v1, 0.5f), new Vector2(v0, 0.5f), new Vector2(v0, 0f));

                    // Inner vertical face facing roadway
                    AddQuad(verts, uvs, tris,
                        new Vector3(xMax, yBot0, z0), new Vector3(xMax, yTop0, z0),
                        new Vector3(xMax, yTop1, z1), new Vector3(xMax, yBot1, z1),
                        new Vector2(v0, 0f), new Vector2(v0, 0.5f), new Vector2(v1, 0.5f), new Vector2(v1, 0f));
                }

                // South and North end caps
                float yEndTop = EvalBridgeArchY(-1f) + curbRise;
                AddQuad(verts, uvs, tris,
                    new Vector3(xMin, -0.52f, -halfL), new Vector3(xMin, yEndTop, -halfL),
                    new Vector3(xMax, yEndTop, -halfL), new Vector3(xMax, -0.52f, -halfL),
                    new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0.2f, 0.5f), new Vector2(0.2f, 0f));
                AddQuad(verts, uvs, tris,
                    new Vector3(xMax, -0.52f, halfL), new Vector3(xMax, yEndTop, halfL),
                    new Vector3(xMin, yEndTop, halfL), new Vector3(xMin, -0.52f, halfL),
                    new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0.2f, 0.5f), new Vector2(0.2f, 0f));
            }

            Mesh m = new Mesh { name = "Cartography_BridgeCurbs" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static void AddQuad(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<int> tris,
            Vector3 p0,
            Vector3 p1,
            Vector3 p2,
            Vector3 p3,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2,
            Vector2 uv3)
        {
            int idx = verts.Count;
            verts.Add(p0);
            verts.Add(p1);
            verts.Add(p2);
            verts.Add(p3);
            uvs.Add(uv0);
            uvs.Add(uv1);
            uvs.Add(uv2);
            uvs.Add(uv3);
            tris.Add(idx);
            tris.Add(idx + 1);
            tris.Add(idx + 2);
            tris.Add(idx);
            tris.Add(idx + 2);
            tris.Add(idx + 3);
        }

        private static Mesh SaveOrUpdateMeshAsset(Mesh source, string assetPath)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(source, assetPath);
                return source;
            }
            existing.Clear();
            existing.SetVertices(source.vertices);
            existing.SetUVs(0, source.uv);
            existing.SetTriangles(source.triangles, 0);
            existing.SetNormals(source.normals);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(source);
            return existing;
        }

        private static Material LoadOrCreateBridgeLitMaterial(string name, string albedoPath, string normalPath)
        {
            string matPath = CartographyAssetImporter.MaterialsDir + "/" + name + ".mat";
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(litShader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != litShader)
            {
                mat.shader = litShader;
            }

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath)
                ?? AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath.Replace("Cartography/Tiles", "Cartography/tiles"));
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath)
                ?? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath.Replace("Cartography/Tiles", "Cartography/tiles"));

            if (albedo != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
