using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Realmz.Foundry;
using Realmz.Rendering;
using Realmz.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Realmz.EditorTools
{
    /// <summary>
    /// Processes dropped Realmz Asset Foundry bundles under Assets/Inbox/<id>/ into
    /// URP Stylized3D / HD-2D / PBR Terrain materials, AnimatorControllers, grounded prefabs, and import receipts.
    /// </summary>
    public sealed class RealmzAssetInboxPostprocessor : AssetPostprocessor
    {
        public const string InboxRoot = "Assets/Inbox";
        public const string MaterialsFoundryRoot = "Assets/Materials/Foundry";
        public const string PrefabsFoundryRoot = "Assets/Prefabs/Foundry";
        public const string Stylized3DShaderName = "Realmz/Stylized3D";
        public const string TerrainBlendShaderName = "Realmz/TerrainBlend";
        public const string GrassTuftShaderName = "Realmz/GrassTuft";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static bool _isProcessing;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (_isProcessing || importedAssets == null || importedAssets.Length == 0)
            {
                return;
            }

            HashSet<string> candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < importedAssets.Length; i++)
            {
                string path = importedAssets[i].Replace('\\', '/');
                if (!path.StartsWith(InboxRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string remainder = path.Substring(InboxRoot.Length + 1);
                int slashIdx = remainder.IndexOf('/');
                if (slashIdx <= 0)
                {
                    continue;
                }

                string assetId = remainder.Substring(0, slashIdx);
                if (assetId.StartsWith("__", StringComparison.Ordinal))
                {
                    continue;
                }

                string fileName = remainder.Substring(slashIdx + 1);
                if (fileName.EndsWith(".import_receipt.json", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, "import_receipt.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (fileName.EndsWith(".asset.json", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, "terrain.asset.json", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("_sheet.png", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("_sheet_n.png", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("_albedo.png", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("_normal.png", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("_height.png", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("_mask.png", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("_roughness.png", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("_ao.png", StringComparison.OrdinalIgnoreCase))
                {
                    candidateIds.Add(assetId);
                }
            }

            if (candidateIds.Count == 0)
            {
                return;
            }

            foreach (string assetId in candidateIds)
            {
                if (!string.IsNullOrEmpty(ResolveManifestAssetPath(assetId)))
                {
                    ProcessAsset(assetId);
                }
            }
        }

        [MenuItem("Realmz/Foundry/Process Asset Inbox")]
        public static int ProcessInboxAll()
        {
            string absInbox = ToAbsolutePath(InboxRoot);
            if (!Directory.Exists(absInbox))
            {
                Debug.Log("[RealmzAssetInbox] Assets/Inbox directory does not exist yet.");
                return 0;
            }

            int processed = 0;
            string[] dirs = Directory.GetDirectories(absInbox);
            for (int i = 0; i < dirs.Length; i++)
            {
                string assetId = Path.GetFileName(dirs[i]);
                if (string.IsNullOrEmpty(assetId) || assetId.StartsWith("__", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(ResolveManifestAssetPath(assetId)))
                {
                    continue;
                }

                FoundryImportReceipt receipt = ProcessAsset(assetId);
                if (receipt != null && receipt.success)
                {
                    processed++;
                }
            }

            Debug.Log("[RealmzAssetInbox] Processed " + processed + " Foundry asset(s).");
            return processed;
        }

        public static FoundryImportReceipt ProcessAsset(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId))
            {
                throw new ArgumentException("Asset ID cannot be empty.", nameof(assetId));
            }

            if (_isProcessing)
            {
                return null;
            }

            _isProcessing = true;
            try
            {
                string assetFolder = InboxRoot + "/" + assetId;
                string manifestAssetPath = ResolveManifestAssetPath(assetId);
                if (string.IsNullOrEmpty(manifestAssetPath))
                {
                    string expectedPath = assetFolder + "/" + assetId + ".asset.json";
                    Debug.LogError("[RealmzAssetInbox] Missing manifest: " + expectedPath);
                    return new FoundryImportReceipt
                    {
                        id = assetId,
                        success = false,
                        notes = "Missing manifest: " + expectedPath
                    };
                }

                string manifestAbsPath = ToAbsolutePath(manifestAssetPath);
                string jsonText = File.ReadAllText(manifestAbsPath, Encoding.UTF8);
                FoundryAssetManifestData manifest = JsonUtility.FromJson<FoundryAssetManifestData>(jsonText);
                if (manifest == null)
                {
                    throw new InvalidOperationException("Failed to deserialize manifest: " + manifestAssetPath);
                }

                if (string.IsNullOrEmpty(manifest.id))
                {
                    manifest.id = assetId;
                }

                bool isTerrain = string.Equals(manifest.asset_kind, "terrain", StringComparison.OrdinalIgnoreCase)
                    || manifestAssetPath.EndsWith("/terrain.asset.json", StringComparison.OrdinalIgnoreCase);

                EnsureFolder("Assets/Materials");
                EnsureFolder(MaterialsFoundryRoot);
                EnsureFolder("Assets/Prefabs");
                EnsureFolder(PrefabsFoundryRoot);

                FoundryImportReceipt receipt = new FoundryImportReceipt
                {
                    schema_version = "1.0",
                    id = manifest.id,
                    name = string.IsNullOrEmpty(manifest.name) ? manifest.id : manifest.name,
                    category = string.IsNullOrEmpty(manifest.category) ? (isTerrain ? "Terrain" : "Prop") : manifest.category,
                    asset_kind = isTerrain ? "terrain" : (string.IsNullOrEmpty(manifest.asset_kind) ? "unit" : manifest.asset_kind),
                    rig_profile = string.IsNullOrEmpty(manifest.rig_profile) ? "Static" : manifest.rig_profile,
                    preferred_mode = string.IsNullOrEmpty(manifest.preferred_mode) ? "Both" : manifest.preferred_mode,
                    shader_name = Stylized3DShaderName,
                    imported_timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    success = true
                };

                if (isTerrain)
                {
                    ProcessTerrainAsset(assetId, assetFolder, manifest, receipt);
                }
                else
                {
                    // 1. Configure HD-2D textures if present
                    string sheetPath = assetFolder + "/" + assetId + "_sheet.png";
                    string sheetNormalPath = assetFolder + "/" + assetId + "_sheet_n.png";
                    int cols = 1;
                    int rows = 1;
                    int spriteCount = 0;

                    if (File.Exists(ToAbsolutePath(sheetPath)))
                    {
                        spriteCount = ConfigureFoundrySpriteSheet(sheetPath, manifest, out cols, out rows);
                        receipt.hd2d_sprite_count = spriteCount;
                    }

                    if (File.Exists(ToAbsolutePath(sheetNormalPath)))
                    {
                        ConfigureFoundryNormalMap(sheetNormalPath);
                    }

                    // 2. Process 3D GLB if present
                    string glbPath = assetFolder + "/" + assetId + ".glb";
                    if (File.Exists(ToAbsolutePath(glbPath)))
                    {
                        ProcessGlbAsset(assetId, glbPath, manifest, receipt);
                    }

                    // 3. Process HD-2D billboard prefab if sheet exists
                    if (File.Exists(ToAbsolutePath(sheetPath)))
                    {
                        ProcessHd2dAsset(assetId, sheetPath, sheetNormalPath, manifest, cols, rows, receipt);
                    }
                }

                AssetDatabase.SaveAssets();

                string receiptAssetPath = isTerrain
                    ? (assetFolder + "/import_receipt.json")
                    : (assetFolder + "/" + assetId + ".import_receipt.json");
                string receiptJson = JsonUtility.ToJson(receipt, true);
                File.WriteAllText(ToAbsolutePath(receiptAssetPath), receiptJson, Utf8NoBom);
                AssetDatabase.ImportAsset(receiptAssetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();

                return receipt;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RealmzAssetInbox] Error processing '" + assetId + "': " + ex);
                return new FoundryImportReceipt
                {
                    id = assetId,
                    success = false,
                    imported_timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    notes = ex.Message
                };
            }
            finally
            {
                _isProcessing = false;
            }
        }

        [MenuItem("Realmz/Foundry/Process Inbox and Capture URP Preview")]
        public static void ProcessInboxAndCaptureDefaultPreview()
        {
            ProcessInboxAll();
            string absInbox = ToAbsolutePath(InboxRoot);
            if (!Directory.Exists(absInbox))
            {
                return;
            }

            string[] dirs = Directory.GetDirectories(absInbox);
            var ids = new List<string>();
            foreach (string d in dirs)
            {
                string id = Path.GetFileName(d);
                if (string.IsNullOrEmpty(id)
                    || id.StartsWith(".", StringComparison.Ordinal)
                    || id.StartsWith("__", StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsTerrainBundleFolder(id))
                {
                    continue;
                }

                ids.Add(id);
            }

            ids.Sort(StringComparer.Ordinal);
            if (ids.Count > 0)
            {
                string outPng = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Foundry_Preview_URP.png"));
                CaptureFoundryPreview(ids.ToArray(), outPng);
            }
        }

        public static string CaptureFoundryPreview(string[] assetIds, string outputPngPath)
        {
            if (string.IsNullOrWhiteSpace(outputPngPath))
            {
                throw new ArgumentException("Output PNG path cannot be empty.", nameof(outputPngPath));
            }

            if (assetIds == null || assetIds.Length == 0)
            {
                throw new ArgumentException("At least one asset ID is required for preview capture.", nameof(assetIds));
            }

            const int width = 1600;
            const int height = 900;

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            List<GameObject> spawned = new List<GameObject>();
            List<UnityEngine.Object> tempPreviewObjects = new List<UnityEngine.Object>();
            RenderTexture rt = null;
            Texture2D readTex = null;

            try
            {
                // Spawn prefabs side-by-side in pairs (3D and HD-2D) along X
                float cursorX = 0f;
                const float pairSpacing = 1.6f;
                const float groupSpacing = 2.6f;
                Bounds overallBounds = new Bounds(Vector3.zero, Vector3.one);
                bool hasBounds = false;
                List<Vector3> unitClearanceCenters = new List<Vector3>();
                List<float> unitClearanceRadii = new List<float>();

                for (int i = 0; i < assetIds.Length; i++)
                {
                    string id = assetIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    string prefab3dPath = PrefabsFoundryRoot + "/" + id + "_3D.prefab";
                    string prefabHd2dPath = PrefabsFoundryRoot + "/" + id + "_HD2D.prefab";

                    GameObject prefab3d = AssetDatabase.LoadAssetAtPath<GameObject>(prefab3dPath);
                    GameObject prefabHd2d = AssetDatabase.LoadAssetAtPath<GameObject>(prefabHd2dPath);

                    if (prefab3d != null)
                    {
                        GameObject inst3d = (GameObject)PrefabUtility.InstantiatePrefab(prefab3d, previewScene);
                        inst3d.transform.position = new Vector3(cursorX, 0f, 0f);
                        spawned.Add(inst3d);
                        AccumulateBounds(inst3d, ref overallBounds, ref hasBounds);
                        RecordClearance(inst3d, unitClearanceCenters, unitClearanceRadii);
                        cursorX += pairSpacing;
                    }

                    if (prefabHd2d != null)
                    {
                        GameObject inst2d = (GameObject)PrefabUtility.InstantiatePrefab(prefabHd2d, previewScene);
                        inst2d.transform.position = new Vector3(cursorX, 0f, 0f);
                        spawned.Add(inst2d);
                        AccumulateBounds(inst2d, ref overallBounds, ref hasBounds);
                        RecordClearance(inst2d, unitClearanceCenters, unitClearanceRadii);
                        cursorX += pairSpacing;
                    }

                    cursorX += groupSpacing * 0.4f;
                }

                // Re-center spawned items around X = 0
                if (hasBounds)
                {
                    float offsetX = -overallBounds.center.x;
                    for (int i = 0; i < spawned.Count; i++)
                    {
                        Vector3 p = spawned[i].transform.position;
                        p.x += offsetX;
                        spawned[i].transform.position = p;
                    }

                    for (int i = 0; i < unitClearanceCenters.Count; i++)
                    {
                        Vector3 c = unitClearanceCenters[i];
                        c.x += offsetX;
                        unitClearanceCenters[i] = c;
                    }

                    overallBounds.center += new Vector3(offsetX, 0f, 0f);
                }

                // Sunlit Bywater ground plane (upgrades to Realmz/TerrainBlend when Inbox terrain textures exist)
                GameObject floorGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floorGo.name = "PreviewBywaterGround";
                SceneManager.MoveGameObjectToScene(floorGo, previewScene);
                float span = hasBounds ? Mathf.Max(12f, overallBounds.size.x * 2.5f) : 12f;
                float planeScale = span * 0.12f;
                float planeWorldExtent = 10f * planeScale;
                floorGo.transform.position = new Vector3(0f, 0f, 0f);
                floorGo.transform.localScale = new Vector3(planeScale, 1f, planeScale);
                MeshRenderer floorRenderer = floorGo.GetComponent<MeshRenderer>();

                Material terrainBlendMat = BuildPreviewTerrainBlendMaterial(planeWorldExtent, tempPreviewObjects);
                if (terrainBlendMat != null)
                {
                    floorRenderer.sharedMaterial = terrainBlendMat;
                }
                else
                {
                    Material grassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/M1/Materials/M1_Tile_bywater_grass.mat");
                    if (grassMat != null)
                    {
                        floorRenderer.sharedMaterial = grassMat;
                    }
                    else
                    {
                        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                        Material fallbackFloor = new Material(lit);
                        fallbackFloor.color = new Color(0.28f, 0.48f, 0.24f, 1f);
                        tempPreviewObjects.Add(fallbackFloor);
                        floorRenderer.sharedMaterial = fallbackFloor;
                    }
                }
                spawned.Add(floorGo);

                // Attach living 3D grass tufts, wildflowers, and pebbles around the models
                Material previewGrassMat = BywaterGroundScatter.CreateDefaultGrassTuftMaterial();
                Material previewFlowerMat = BywaterGroundScatter.CreateDefaultFlowerMaterial();
                Material previewPebbleMat = BywaterGroundScatter.CreateDefaultPebbleMaterial();
                tempPreviewObjects.Add(previewGrassMat);
                tempPreviewObjects.Add(previewFlowerMat);
                tempPreviewObjects.Add(previewPebbleMat);

                float scatterHalfSpan = Mathf.Min(planeWorldExtent * 0.44f, 10.5f);
                GameObject scatterRoot = BywaterGroundScatter.CreatePreviewScatterRoot(
                    null,
                    new Vector2(-scatterHalfSpan, -scatterHalfSpan),
                    new Vector2(scatterHalfSpan, scatterHalfSpan),
                    previewGrassMat,
                    previewFlowerMat,
                    previewPebbleMat,
                    pos =>
                    {
                        float trailCenterZ = Mathf.Sin(pos.x * 0.28f) * 0.85f + 0.15f;
                        if (Mathf.Abs(pos.z - trailCenterZ) < 1.30f)
                        {
                            return true;
                        }

                        for (int c = 0; c < unitClearanceCenters.Count; c++)
                        {
                            float dx = pos.x - unitClearanceCenters[c].x;
                            float dz = pos.z - unitClearanceCenters[c].z;
                            float r = unitClearanceRadii[c];
                            if ((dx * dx + dz * dz) < (r * r))
                            {
                                return true;
                            }
                        }
                        return false;
                    });

                if (scatterRoot != null)
                {
                    SceneManager.MoveGameObjectToScene(scatterRoot, previewScene);
                    MeshFilter[] scatterFilters = scatterRoot.GetComponentsInChildren<MeshFilter>(true);
                    for (int f = 0; f < scatterFilters.Length; f++)
                    {
                        if (scatterFilters[f].sharedMesh != null)
                        {
                            tempPreviewObjects.Add(scatterFilters[f].sharedMesh);
                        }
                    }
                    spawned.Add(scatterRoot);
                }

                // Warm M1.5 Bywater Directional Sun (calibrated for LDR preview without HDR tonemapping blowout)
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.42f, 0.56f, 0.78f, 1f);
                RenderSettings.ambientEquatorColor = new Color(0.36f, 0.38f, 0.35f, 1f);
                RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.18f, 1f);
                RenderSettings.ambientIntensity = 0.72f;
                RenderSettings.reflectionIntensity = 0.10f;

                GameObject sunGo = new GameObject("PreviewDirectionalSun");
                SceneManager.MoveGameObjectToScene(sunGo, previewScene);
                Light sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1.0f, 0.941f, 0.831f, 1f);
                sun.intensity = 1.05f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.65f;
                sunGo.transform.rotation = Quaternion.Euler(46f, 155f, 0f);
                spawned.Add(sunGo);

                // Studio Camera framed on combined bounds (+Z front view)
                GameObject camGo = new GameObject("PreviewCamera");
                SceneManager.MoveGameObjectToScene(camGo, previewScene);
                Camera cam = camGo.AddComponent<Camera>();
                cam.scene = previewScene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.34f, 0.50f, 0.72f, 1f);
                cam.fieldOfView = 34f;
                cam.nearClipPlane = 0.2f;
                cam.farClipPlane = 120f;
                cam.allowHDR = false;
                cam.allowMSAA = false;

                Vector3 targetCenter = hasBounds ? overallBounds.center : new Vector3(0f, 0.8f, 0f);
                float radius = hasBounds ? Mathf.Max(1.6f, overallBounds.extents.magnitude) : 2.2f;
                float distance = (radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.12f;
                Vector3 camOffset = Quaternion.Euler(-24f, 18f, 0f) * new Vector3(0f, 0f, distance);
                camGo.transform.position = targetCenter + camOffset;
                camGo.transform.LookAt(targetCenter, Vector3.up);
                spawned.Add(camGo);

                // Align any PixelBillboard components to face the preview camera explicitly
                Vector3 camForwardXZ = camGo.transform.forward;
                camForwardXZ.y = 0f;
                if (camForwardXZ.sqrMagnitude > 0.0001f)
                {
                    Quaternion bbRot = Quaternion.LookRotation(camForwardXZ.normalized, Vector3.up);
                    for (int i = 0; i < spawned.Count; i++)
                    {
                        PixelBillboard[] billboards = spawned[i].GetComponentsInChildren<PixelBillboard>(true);
                        for (int b = 0; b < billboards.Length; b++)
                        {
                            billboards[b].transform.rotation = bbRot;
                        }
                    }
                }

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                cam.targetTexture = rt;
                bool prevAsyncCompile = ShaderUtil.allowAsyncCompilation;
                ShaderUtil.allowAsyncCompilation = false;
                try
                {
                    cam.Render();
                }
                finally
                {
                    ShaderUtil.allowAsyncCompilation = prevAsyncCompile;
                }

                RenderTexture prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                readTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readTex.Apply(false, false);
                RenderTexture.active = prevActive;
                cam.targetTexture = null;

                string absOut = Path.IsPathRooted(outputPngPath) ? outputPngPath : ToAbsolutePath(outputPngPath);
                string outDir = Path.GetDirectoryName(absOut);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                byte[] pngBytes = readTex.EncodeToPNG();
                File.WriteAllBytes(absOut, pngBytes);
                Debug.Log("[RealmzAssetInbox] Captured preview screenshot: " + absOut);
                return absOut;
            }
            finally
            {
                if (readTex != null)
                {
                    UnityEngine.Object.DestroyImmediate(readTex);
                }

                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }

                for (int i = 0; i < spawned.Count; i++)
                {
                    if (spawned[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(spawned[i]);
                    }
                }

                for (int i = 0; i < tempPreviewObjects.Count; i++)
                {
                    if (tempPreviewObjects[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(tempPreviewObjects[i]);
                    }
                }

                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static int ConfigureFoundrySpriteSheet(
            string sheetPath,
            FoundryAssetManifestData manifest,
            out int cols,
            out int rows)
        {
            cols = 1;
            rows = 1;

            AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (importer == null)
            {
                return 0;
            }

            importer.GetSourceTextureWidthAndHeight(out int texW, out int texH);
            int ppu = manifest.hd2d_ppu > 0 ? manifest.hd2d_ppu : 128;
            int requestedFrames = Mathf.Max(1, manifest.hd2d_frame_count);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = requestedFrames <= 1 ? SpriteImportMode.Single : SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            settings.spritePivot = new Vector2(0.5f, 0f);
            importer.SetTextureSettings(settings);

            int actualCount = 1;
            if (requestedFrames > 1 && texW > 0 && texH > 0)
            {
                int cellSize = manifest.hd2d_frame_size > 0 ? manifest.hd2d_frame_size : Mathf.Min(texW, texH);
                cols = Mathf.Max(1, texW / cellSize);
                rows = Mathf.Max(1, texH / cellSize);
                int maxCells = cols * rows;
                actualCount = Mathf.Min(requestedFrames, maxCells);

                List<SpriteMetaData> metas = new List<SpriteMetaData>(actualCount);
                for (int idx = 0; idx < actualCount; idx++)
                {
                    int col = idx % cols;
                    int rowFromTop = idx / cols;
                    int rowFromBottom = (rows - 1) - rowFromTop;
                    metas.Add(new SpriteMetaData
                    {
                        name = manifest.id + "_frame_" + idx,
                        rect = new Rect(col * cellSize, rowFromBottom * cellSize, cellSize, cellSize),
                        alignment = (int)SpriteAlignment.BottomCenter,
                        pivot = new Vector2(0.5f, 0f)
                    });
                }

                #pragma warning disable 0618
                importer.spritesheet = metas.ToArray();
                #pragma warning restore 0618
            }

            importer.SaveAndReimport();
            return actualCount;
        }

        private static void ConfigureFoundryNormalMap(string normalPath)
        {
            AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.NormalMap;
            importer.convertToNormalmap = false;
            importer.sRGBTexture = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        private static void ProcessGlbAsset(
            string assetId,
            string glbPath,
            FoundryAssetManifestData manifest,
            FoundryImportReceipt receipt)
        {
            ConfigureGltfImporterProperties(glbPath);
            AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceSynchronousImport);

            GameObject glbMain = AssetDatabase.LoadMainAssetAtPath(glbPath) as GameObject;
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(glbPath);
            if (glbMain == null)
            {
                receipt.notes = "GLB imported without a GameObject root (verify com.unity.cloud.gltfast is resolved).";
                return;
            }

            List<AnimationClip> clips = new List<AnimationClip>();
            List<string> clipNames = new List<string>();
            int totalMeshCount = 0;
            int totalTriangles = 0;
            Material firstSourceMat = null;

            for (int i = 0; i < subAssets.Length; i++)
            {
                UnityEngine.Object obj = subAssets[i];
                if (obj is AnimationClip clip)
                {
                    if (!clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                    {
                        clips.Add(clip);
                        clipNames.Add(clip.name);
                    }
                }
                else if (obj is Mesh mesh)
                {
                    totalMeshCount++;
                    totalTriangles += mesh.triangles != null ? (mesh.triangles.Length / 3) : 0;
                }
                else if (obj is Material mat && firstSourceMat == null)
                {
                    firstSourceMat = mat;
                }
            }

            // Build or update Stylized3D material
            string mat3dPath = MaterialsFoundryRoot + "/" + assetId + "_Stylized3D.mat";
            Material stylizedMat = CreateOrUpdateStylized3DMaterial(mat3dPath, firstSourceMat);
            receipt.material_3d_path = mat3dPath;
            receipt.shader_name = stylizedMat.shader != null ? stylizedMat.shader.name : Stylized3DShaderName;

            // Build AnimatorController if GLB carries animation clips
            RuntimeAnimatorController controller = null;
            if (clips.Count > 0)
            {
                string controllerPath = PrefabsFoundryRoot + "/" + assetId + "_Controller.controller";
                controller = BuildAnimatorController(controllerPath, clips);
                receipt.animator_controller_path = controllerPath;
            }

            // Assemble clean grounded prefab <id>_3D
            GameObject rootGo = new GameObject(assetId + "_3D");
            GameObject visualGo = UnityEngine.Object.Instantiate(glbMain, rootGo.transform, false);
            visualGo.name = "Visual";
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localRotation = Quaternion.identity;

            HashSet<Transform> uniqueBones = new HashSet<Transform>();
            Renderer[] renderers = visualGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                int slotCount = Math.Max(1, r.sharedMaterials != null ? r.sharedMaterials.Length : 1);
                Material[] mats = new Material[slotCount];
                for (int m = 0; m < slotCount; m++)
                {
                    mats[m] = stylizedMat;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;

                if (r is SkinnedMeshRenderer smr && smr.bones != null)
                {
                    for (int b = 0; b < smr.bones.Length; b++)
                    {
                        if (smr.bones[b] != null)
                        {
                            uniqueBones.Add(smr.bones[b]);
                        }
                    }
                }
            }

            // Ground visual child strictly at Y = 0
            Bounds measuredBounds = MeasureCombinedRendererBounds(renderers);
            if (renderers.Length > 0)
            {
                visualGo.transform.localPosition = new Vector3(0f, -measuredBounds.min.y, 0f);
                measuredBounds = MeasureCombinedRendererBounds(renderers);
            }

            Vector3 colliderSize = manifest.bounds_m != null ? manifest.bounds_m.ToVector3() : Vector3.zero;
            if (colliderSize.x <= 0.001f || colliderSize.y <= 0.001f || colliderSize.z <= 0.001f)
            {
                colliderSize = renderers.Length > 0 ? measuredBounds.size : Vector3.one * Mathf.Max(0.5f, manifest.target_scale_m);
            }

            BoxCollider box = rootGo.AddComponent<BoxCollider>();
            box.size = new Vector3(
                Mathf.Max(0.2f, colliderSize.x),
                Mathf.Max(0.2f, colliderSize.y),
                Mathf.Max(0.2f, colliderSize.z));
            box.center = new Vector3(0f, box.size.y * 0.5f, 0f);

            int resolvedBoneCount = uniqueBones.Count > 0
                ? uniqueBones.Count
                : (manifest.bones != null ? manifest.bones.Length : 0);
            int resolvedTris = totalTriangles > 0 ? totalTriangles : manifest.triangle_count;

            Animator anim = rootGo.GetComponentInChildren<Animator>(true);
            if (controller != null)
            {
                if (anim == null)
                {
                    anim = visualGo.AddComponent<Animator>();
                }
                anim.runtimeAnimatorController = controller;
            }

            FoundryAssetMetadata metadata = rootGo.AddComponent<FoundryAssetMetadata>();
            metadata.ApplyManifest(manifest, resolvedTris, resolvedBoneCount, clipNames.ToArray());

            BywaterFoundryUnit unit = rootGo.AddComponent<BywaterFoundryUnit>();
            unit.rigProfile = metadata.rigProfile;
            unit.category = metadata.category;
            unit.visualRoot = visualGo.transform;
            unit.animator = anim;

            string prefab3dPath = PrefabsFoundryRoot + "/" + assetId + "_3D.prefab";
            PrefabUtility.SaveAsPrefabAsset(rootGo, prefab3dPath);
            UnityEngine.Object.DestroyImmediate(rootGo);

            receipt.prefab_3d_path = prefab3dPath;
            receipt.mesh_count = totalMeshCount;
            receipt.triangle_count = resolvedTris;
            receipt.bone_count = resolvedBoneCount;
            receipt.animation_clips = clipNames.ToArray();
        }

        private static void ProcessHd2dAsset(
            string assetId,
            string sheetPath,
            string sheetNormalPath,
            FoundryAssetManifestData manifest,
            int cols,
            int rows,
            FoundryImportReceipt receipt)
        {
            Texture2D sheetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
            Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetNormalPath);
            Sprite firstSprite = LoadFirstSprite(sheetPath);

            string matHd2dPath = MaterialsFoundryRoot + "/" + assetId + "_HD2D.mat";
            bool useSubRectMesh = cols > 1 || rows > 1;
            Material hd2dMat = CreateOrUpdateHd2dBillboardMaterial(
                matHd2dPath,
                sheetTex,
                normalTex,
                useSubRectMesh ? 1 : cols,
                useSubRectMesh ? 1 : rows);
            receipt.material_hd2d_path = matHd2dPath;

            float quadHeight = manifest.target_scale_m > 0.01f ? manifest.target_scale_m : 1.35f;
            float aspect = 1.0f;
            if (firstSprite != null && firstSprite.rect.height > 0f)
            {
                aspect = firstSprite.rect.width / firstSprite.rect.height;
            }
            else if (sheetTex != null && sheetTex.height > 0)
            {
                float cellW = sheetTex.width / (float)Mathf.Max(1, cols);
                float cellH = sheetTex.height / (float)Mathf.Max(1, rows);
                aspect = cellW / Mathf.Max(1f, cellH);
            }

            float quadWidth = Mathf.Max(0.25f, quadHeight * aspect);

            GameObject rootGo = new GameObject(assetId + "_HD2D");

            GameObject billboardGo = new GameObject("SpriteBillboard");
            billboardGo.transform.SetParent(rootGo.transform, false);
            billboardGo.transform.localPosition = Vector3.zero;
            billboardGo.AddComponent<PixelBillboard>();

            GameObject quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "SpriteQuad";
            quadGo.transform.SetParent(billboardGo.transform, false);
            quadGo.transform.localPosition = new Vector3(0f, quadHeight * 0.5f, 0f);
            quadGo.transform.localScale = new Vector3(quadWidth, quadHeight, 1f);

            if (useSubRectMesh)
            {
                string meshAssetPath = PrefabsFoundryRoot + "/" + assetId + "_Frame0Quad.asset";
                Mesh quadMesh = CreateOrUpdateSubRectQuadMesh(meshAssetPath, cols, rows);
                MeshFilter mf = quadGo.GetComponent<MeshFilter>();
                if (mf != null && quadMesh != null)
                {
                    mf.sharedMesh = quadMesh;
                }
            }

            Collider quadCol = quadGo.GetComponent<Collider>();
            if (quadCol != null)
            {
                UnityEngine.Object.DestroyImmediate(quadCol);
            }

            MeshRenderer mr = quadGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = hd2dMat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;

            BoxCollider box = rootGo.AddComponent<BoxCollider>();
            box.size = new Vector3(Mathf.Max(0.3f, quadWidth * 0.7f), quadHeight, Mathf.Max(0.35f, quadWidth * 0.55f));
            box.center = new Vector3(0f, quadHeight * 0.5f, 0f);

            FoundryAssetMetadata metadata = rootGo.AddComponent<FoundryAssetMetadata>();
            metadata.ApplyManifest(
                manifest,
                receipt.triangle_count > 0 ? receipt.triangle_count : manifest.triangle_count,
                receipt.bone_count > 0 ? receipt.bone_count : (manifest.bones != null ? manifest.bones.Length : 0),
                receipt.animation_clips);

            BywaterFoundryUnit unit = rootGo.AddComponent<BywaterFoundryUnit>();
            unit.rigProfile = metadata.rigProfile;
            unit.category = metadata.category;
            unit.visualRoot = billboardGo.transform;

            string prefabHd2dPath = PrefabsFoundryRoot + "/" + assetId + "_HD2D.prefab";
            PrefabUtility.SaveAsPrefabAsset(rootGo, prefabHd2dPath);
            UnityEngine.Object.DestroyImmediate(rootGo);

            receipt.prefab_hd2d_path = prefabHd2dPath;
        }

        private static void ConfigureGltfImporterProperties(string glbPath)
        {
            AssetImporter importer = AssetImporter.GetAtPath(glbPath);
            if (importer == null)
            {
                return;
            }

            // Use SerializedObject so we stay decoupled from internal glTFast.Editor assembly types
            SerializedObject so = new SerializedObject(importer);
            bool modified = false;

            SerializedProperty animMethod = so.FindProperty("importerSettings.animationMethod");
            if (animMethod != null && animMethod.propertyType == SerializedPropertyType.Enum && animMethod.intValue != 2)
            {
                // Enum 2 corresponds to Mecanim in glTFast AnimationMethod (None=0, Legacy=1, Mecanim=2)
                animMethod.intValue = 2;
                modified = true;
            }

            SerializedProperty nodeNameMethod = so.FindProperty("importerSettings.nodeNameMethod");
            if (nodeNameMethod != null && nodeNameMethod.propertyType == SerializedPropertyType.Enum && nodeNameMethod.intValue == 0)
            {
                nodeNameMethod.intValue = 1;
                modified = true;
            }

            if (modified)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
            }
        }

        private static Material CreateOrUpdateStylized3DMaterial(string matPath, Material sourceMat)
        {
            Shader shader = Shader.Find(Stylized3DShaderName);
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
            }

            Texture baseTex = ExtractFirstTexture(sourceMat, true, "_BaseMap", "baseColorTexture", "_MainTex");
            Texture normalTex = ExtractFirstTexture(sourceMat, false, "_BumpMap", "normalTexture", "_NormalMap");

            Color baseColor = Color.white;
            if (sourceMat != null)
            {
                if (sourceMat.HasProperty("_BaseColor"))
                {
                    baseColor = sourceMat.GetColor("_BaseColor");
                }
                else if (sourceMat.HasProperty("baseColorFactor"))
                {
                    baseColor = sourceMat.GetColor("baseColorFactor");
                }
                else if (sourceMat.HasProperty("_Color"))
                {
                    baseColor = sourceMat.GetColor("_Color");
                }
            }

            mat.SetColor("_BaseColor", baseColor);
            if (baseTex != null)
            {
                mat.SetTexture("_BaseMap", baseTex);
            }

            if (normalTex != null)
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.SetFloat("_BumpScale", 1.0f);
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                mat.SetTexture("_BumpMap", null);
                mat.SetFloat("_BumpScale", 0f);
                mat.DisableKeyword("_NORMALMAP");
            }

            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", 0.15f);
            }
            if (mat.HasProperty("_RampSteps"))
            {
                mat.SetFloat("_RampSteps", 4.0f);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateHd2dBillboardMaterial(
            string matPath,
            Texture2D albedo,
            Texture2D normalMap,
            int cols,
            int rows)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.SetFloat("_Surface", 0f);
            mat.SetColor("_BaseColor", Color.white);
            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                Vector2 scale = new Vector2(1f / Mathf.Max(1, cols), 1f / Mathf.Max(1, rows));
                Vector2 offset = new Vector2(0f, 1f - scale.y);
                mat.SetTextureScale("_BaseMap", scale);
                mat.SetTextureOffset("_BaseMap", offset);
            }

            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;

            mat.SetFloat("_Cull", 0f);
            mat.SetFloat("_WorkflowMode", 1f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

            if (normalMap != null)
            {
                mat.SetTexture("_BumpMap", normalMap);
                Vector2 nScale = new Vector2(1f / Mathf.Max(1, cols), 1f / Mathf.Max(1, rows));
                Vector2 nOffset = new Vector2(0f, 1f - nScale.y);
                mat.SetTextureScale("_BumpMap", nScale);
                mat.SetTextureOffset("_BumpMap", nOffset);
                mat.SetFloat("_BumpScale", 1.0f);
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
            }

            mat.SetColor("_EmissionColor", Color.black);
            mat.DisableKeyword("_EMISSION");

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static RuntimeAnimatorController BuildAnimatorController(
            string controllerPath,
            List<AnimationClip> clips)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                AssetDatabase.DeleteAsset(controllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            AnimationClip idleClip = FindClipByKeywords(clips, "idle", "breath", "stand") ?? clips[0];
            AnimationClip moveClip = FindClipByKeywords(clips, "move", "walk", "run", "swim");
            AnimationClip attackClip = FindClipByKeywords(clips, "attack", "bite", "hit", "strike");

            AnimatorState idleState = sm.AddState(BywaterFoundryUnit.StateIdle);
            idleState.motion = idleClip;
            sm.defaultState = idleState;

            if (moveClip != null)
            {
                AnimatorState moveState = sm.AddState(BywaterFoundryUnit.StateMove);
                moveState.motion = moveClip;

                AnimatorStateTransition toMove = idleState.AddTransition(moveState);
                toMove.hasExitTime = false;
                toMove.duration = 0.12f;
                toMove.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

                AnimatorStateTransition toIdle = moveState.AddTransition(idleState);
                toIdle.hasExitTime = false;
                toIdle.duration = 0.12f;
                toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");
            }

            if (attackClip != null)
            {
                AnimatorState attackState = sm.AddState(BywaterFoundryUnit.StateAttack);
                attackState.motion = attackClip;

                AnimatorStateTransition anyToAttack = sm.AddAnyStateTransition(attackState);
                anyToAttack.hasExitTime = false;
                anyToAttack.duration = 0.08f;
                anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");

                AnimatorStateTransition attackToIdle = attackState.AddTransition(idleState);
                attackToIdle.hasExitTime = true;
                attackToIdle.exitTime = 0.95f;
                attackToIdle.duration = 0.10f;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Mesh CreateOrUpdateSubRectQuadMesh(string meshPath, int cols, int rows)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = Path.GetFileNameWithoutExtension(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            float uMax = 1f / Mathf.Max(1, cols);
            float vMin = 1f - (1f / Mathf.Max(1, rows));

            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.uv = new[]
            {
                new Vector2(0f, vMin),
                new Vector2(uMax, vMin),
                new Vector2(0f, 1f),
                new Vector2(uMax, 1f)
            };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static AnimationClip FindClipByKeywords(List<AnimationClip> clips, params string[] keywords)
        {
            for (int k = 0; k < keywords.Length; k++)
            {
                string kw = keywords[k];
                for (int i = 0; i < clips.Count; i++)
                {
                    if (clips[i].name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return clips[i];
                    }
                }
            }

            return null;
        }

        private static Texture ExtractFirstTexture(Material mat, bool allowMainFallback, params string[] propNames)
        {
            if (mat == null)
            {
                return null;
            }

            for (int i = 0; i < propNames.Length; i++)
            {
                if (mat.HasProperty(propNames[i]))
                {
                    Texture t = mat.GetTexture(propNames[i]);
                    if (t != null)
                    {
                        return t;
                    }
                }
            }

            return allowMainFallback ? mat.mainTexture : null;
        }

        private static Sprite LoadFirstSprite(string sheetPath)
        {
            Sprite single = AssetDatabase.LoadAssetAtPath<Sprite>(sheetPath);
            if (single != null)
            {
                return single;
            }

            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(sheetPath);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is Sprite s)
                {
                    return s;
                }
            }

            return null;
        }

        private static string ResolveManifestAssetPath(string assetId)
        {
            string assetFolder = InboxRoot + "/" + assetId;
            string namedManifest = assetFolder + "/" + assetId + ".asset.json";
            if (File.Exists(ToAbsolutePath(namedManifest)))
            {
                return namedManifest;
            }

            string terrainManifest = assetFolder + "/terrain.asset.json";
            if (File.Exists(ToAbsolutePath(terrainManifest)))
            {
                return terrainManifest;
            }

            return null;
        }

        private static bool IsTerrainBundleFolder(string assetId)
        {
            string manifestPath = ResolveManifestAssetPath(assetId);
            if (string.IsNullOrEmpty(manifestPath))
            {
                return false;
            }

            if (manifestPath.EndsWith("/terrain.asset.json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                string jsonText = File.ReadAllText(ToAbsolutePath(manifestPath), Encoding.UTF8);
                FoundryAssetManifestData manifest = JsonUtility.FromJson<FoundryAssetManifestData>(jsonText);
                return manifest != null && string.Equals(manifest.asset_kind, "terrain", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ProcessTerrainAsset(
            string assetId,
            string assetFolder,
            FoundryAssetManifestData manifest,
            FoundryImportReceipt receipt)
        {
            List<string> importedTexPaths = new List<string>(6);

            string albedoPath = ResolveTerrainMapAssetPath(assetFolder, manifest, manifest.maps != null ? manifest.maps.albedo : null, "_albedo.png");
            string normalPath = ResolveTerrainMapAssetPath(assetFolder, manifest, manifest.maps != null ? manifest.maps.normal : null, "_normal.png");
            string heightPath = ResolveTerrainMapAssetPath(assetFolder, manifest, manifest.maps != null ? manifest.maps.height : null, "_height.png");
            string roughnessPath = ResolveTerrainMapAssetPath(assetFolder, manifest, manifest.maps != null ? manifest.maps.roughness : null, "_roughness.png");
            string aoPath = ResolveTerrainMapAssetPath(assetFolder, manifest, manifest.maps != null ? manifest.maps.ao : null, "_ao.png");
            string maskPath = ResolveTerrainMapAssetPath(assetFolder, manifest, manifest.maps != null ? manifest.maps.mask : null, "_mask.png");

            Texture2D albedoTex = ConfigureTerrainTexture(albedoPath, isSrgb: true, isNormal: false, importedTexPaths);
            Texture2D normalTex = ConfigureTerrainTexture(normalPath, isSrgb: false, isNormal: true, importedTexPaths);
            Texture2D heightTex = ConfigureTerrainTexture(heightPath, isSrgb: false, isNormal: false, importedTexPaths);
            ConfigureTerrainTexture(roughnessPath, isSrgb: false, isNormal: false, importedTexPaths);
            Texture2D aoTex = ConfigureTerrainTexture(aoPath, isSrgb: false, isNormal: false, importedTexPaths);
            Texture2D maskTex = ConfigureTerrainTexture(maskPath, isSrgb: false, isNormal: false, importedTexPaths);

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            string matPath = MaterialsFoundryRoot + "/" + manifest.id + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(litShader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (litShader != null && mat.shader != litShader)
            {
                mat.shader = litShader;
            }

            float tiling = manifest.tile_scale_m > 0.001f ? (1f / manifest.tile_scale_m) : 1f;
            Vector2 tilingVec = new Vector2(tiling, tiling);

            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_WorkflowMode", 1f);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.85f);

            if (albedoTex != null)
            {
                mat.SetTexture("_BaseMap", albedoTex);
                mat.SetTextureScale("_BaseMap", tilingVec);
            }

            if (normalTex != null)
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.SetTextureScale("_BumpMap", tilingVec);
                mat.SetFloat("_BumpScale", 1.0f);
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
            }

            if (maskTex != null)
            {
                mat.SetTexture("_MetallicGlossMap", maskTex);
                mat.SetTextureScale("_MetallicGlossMap", tilingVec);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                mat.SetTexture("_MetallicGlossMap", null);
                mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            }

            if (aoTex != null)
            {
                mat.SetTexture("_OcclusionMap", aoTex);
                mat.SetTextureScale("_OcclusionMap", tilingVec);
                mat.SetFloat("_OcclusionStrength", 1.0f);
            }
            else
            {
                mat.SetTexture("_OcclusionMap", null);
            }

            if (heightTex != null)
            {
                mat.SetTexture("_ParallaxMap", heightTex);
                mat.SetTextureScale("_ParallaxMap", tilingVec);
                mat.SetFloat("_Parallax", 0.02f);
                mat.EnableKeyword("_PARALLAXMAP");
            }
            else
            {
                mat.SetTexture("_ParallaxMap", null);
                mat.DisableKeyword("_PARALLAXMAP");
            }

            EditorUtility.SetDirty(mat);

            receipt.asset_kind = "terrain";
            receipt.terrain_material_path = matPath;
            receipt.shader_name = litShader != null ? litShader.name : "Universal Render Pipeline/Lit";
            receipt.texture_paths = importedTexPaths.ToArray();
        }

        private static string ResolveTerrainMapAssetPath(
            string assetFolder,
            FoundryAssetManifestData manifest,
            string explicitFileName,
            string suffix)
        {
            if (!string.IsNullOrEmpty(explicitFileName))
            {
                string explicitPath = assetFolder + "/" + explicitFileName;
                if (File.Exists(ToAbsolutePath(explicitPath)))
                {
                    return explicitPath;
                }
            }

            if (!string.IsNullOrEmpty(manifest.id))
            {
                string byId = assetFolder + "/" + manifest.id + suffix;
                if (File.Exists(ToAbsolutePath(byId)))
                {
                    return byId;
                }
            }

            string absFolder = ToAbsolutePath(assetFolder);
            if (Directory.Exists(absFolder))
            {
                string[] matches = Directory.GetFiles(absFolder, "*" + suffix);
                if (matches.Length > 0)
                {
                    return assetFolder + "/" + Path.GetFileName(matches[0]);
                }
            }

            return null;
        }

        private static Texture2D ConfigureTerrainTexture(
            string assetPath,
            bool isSrgb,
            bool isNormal,
            List<string> importedTexPaths)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(ToAbsolutePath(assetPath)))
            {
                return null;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (isNormal)
                {
                    importer.convertToNormalmap = false;
                }
                importer.sRGBTexture = isSrgb;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            importedTexPaths.Add(assetPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static void RecordClearance(
            GameObject go,
            List<Vector3> centers,
            List<float> radii)
        {
            if (go == null)
            {
                return;
            }

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                centers.Add(go.transform.position);
                radii.Add(0.95f);
                return;
            }

            Bounds b = MeasureCombinedRendererBounds(renderers);
            float xzRadius = Mathf.Max(0.85f, Mathf.Max(b.extents.x, b.extents.z) + 0.35f);
            centers.Add(new Vector3(b.center.x, 0f, b.center.z));
            radii.Add(xzRadius);
        }

        private static Material BuildPreviewTerrainBlendMaterial(
            float planeWorldExtent,
            List<UnityEngine.Object> tempObjects)
        {
            Shader terrainShader = Shader.Find(TerrainBlendShaderName);
            if (terrainShader == null)
            {
                return null;
            }

            string grassFolder = FindTerrainInboxFolder("bywater_meadow_grass");
            if (string.IsNullOrEmpty(grassFolder))
            {
                return null;
            }

            string dirtFolder = FindTerrainInboxFolder("bywater_dirt_trail");
            string cobbleFolder = FindTerrainInboxFolder("bywater_cobblestone_road");

            if (!TryLoadTerrainLayerTextures(grassFolder, out Texture2D grassAlb, out Texture2D grassNrm, out Texture2D grassMask))
            {
                return null;
            }

            TryLoadTerrainLayerTextures(dirtFolder, out Texture2D dirtAlb, out Texture2D dirtNrm, out Texture2D dirtMask);
            TryLoadTerrainLayerTextures(cobbleFolder, out Texture2D cobbleAlb, out Texture2D cobbleNrm, out Texture2D cobbleMask);

            // Synthetic bilinear splat map: meadow grass base + soft curving dirt trail + subtle cobblestone accents
            const int splatRes = 128;
            Texture2D splatTex = new Texture2D(splatRes, splatRes, TextureFormat.RGBA32, false, true)
            {
                name = "PreviewTerrainSplatMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[splatRes * splatRes];
            for (int y = 0; y < splatRes; y++)
            {
                float v = y / (float)(splatRes - 1);
                float worldZ = (v - 0.5f) * planeWorldExtent;
                for (int x = 0; x < splatRes; x++)
                {
                    float u = x / (float)(splatRes - 1);
                    float worldX = (u - 0.5f) * planeWorldExtent;

                    float trailCenterZ = Mathf.Sin(worldX * 0.28f) * 0.85f + 0.15f;
                    float distTrail = Mathf.Abs(worldZ - trailCenterZ);
                    float dirtWeight = Mathf.Clamp01(1.0f - (distTrail - 0.65f) / 1.45f) * 0.85f;
                    float cobbleWeight = Mathf.Clamp01(1.0f - Mathf.Abs(worldZ + 2.6f) / 1.15f)
                        * Mathf.Clamp01(1.0f - Mathf.Abs(worldX) / 5.5f) * 0.72f;
                    float grassWeight = Mathf.Max(0.12f, 1.0f - dirtWeight - cobbleWeight);

                    float sum = grassWeight + dirtWeight + cobbleWeight;
                    pixels[y * splatRes + x] = new Color(
                        grassWeight / sum,
                        dirtWeight / sum,
                        cobbleWeight / sum,
                        0f);
                }
            }
            splatTex.SetPixels(pixels);
            splatTex.Apply(false, false);
            tempObjects.Add(splatTex);

            Material mat = new Material(terrainShader)
            {
                name = "PreviewBywaterTerrainBlend"
            };
            tempObjects.Add(mat);

            float halfExtent = planeWorldExtent * 0.5f;
            float invExtent = planeWorldExtent > 0.001f ? (1f / planeWorldExtent) : 0.05f;
            mat.SetTexture("_SplatMap", splatTex);
            mat.SetVector("_WorldBounds", new Vector4(-halfExtent, -halfExtent, invExtent, invExtent));
            mat.SetFloat("_WorldTileScale", 0.5f);
            mat.SetFloat("_BlendContrast", 0.16f);
            mat.SetFloat("_BumpScale", 1.0f);
            mat.SetFloat("_Smoothness", 0.85f);
            mat.SetFloat("_MacroScale", 0.085f);
            mat.SetFloat("_MacroStrength", 0.15f);
            mat.SetFloat("_GridHintStrength", 0.0f);

            BindTerrainBlendLayer(mat, 0, grassAlb, grassNrm, grassMask);
            BindTerrainBlendLayer(mat, 1, dirtAlb ?? grassAlb, dirtNrm ?? grassNrm, dirtMask ?? grassMask);
            BindTerrainBlendLayer(mat, 2, cobbleAlb ?? grassAlb, cobbleNrm ?? grassNrm, cobbleMask ?? grassMask);
            BindTerrainBlendLayer(mat, 3, grassAlb, grassNrm, grassMask);

            return mat;
        }

        private static void BindTerrainBlendLayer(
            Material mat,
            int layerIdx,
            Texture2D albedo,
            Texture2D normal,
            Texture2D mask)
        {
            string prefix = "_Layer" + layerIdx;
            if (albedo != null)
            {
                mat.SetTexture(prefix + "Albedo", albedo);
            }
            if (normal != null)
            {
                mat.SetTexture(prefix + "Normal", normal);
            }
            if (mask != null)
            {
                mat.SetTexture(prefix + "Mask", mask);
            }
            mat.SetColor(prefix + "Color", Color.white);
        }

        private static string FindTerrainInboxFolder(string key)
        {
            string absInbox = ToAbsolutePath(InboxRoot);
            if (!Directory.Exists(absInbox))
            {
                return null;
            }

            string[] dirs = Directory.GetDirectories(absInbox);
            for (int i = 0; i < dirs.Length; i++)
            {
                string folderName = Path.GetFileName(dirs[i]);
                if (string.IsNullOrEmpty(folderName) || !folderName.StartsWith("terrain_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (folderName.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return InboxRoot + "/" + folderName;
                }
            }

            return null;
        }

        private static bool TryLoadTerrainLayerTextures(
            string folderAssetPath,
            out Texture2D albedo,
            out Texture2D normal,
            out Texture2D mask)
        {
            albedo = null;
            normal = null;
            mask = null;
            if (string.IsNullOrEmpty(folderAssetPath))
            {
                return false;
            }

            string absFolder = ToAbsolutePath(folderAssetPath);
            if (!Directory.Exists(absFolder))
            {
                return false;
            }

            string[] albedos = Directory.GetFiles(absFolder, "*_albedo.png");
            string[] normals = Directory.GetFiles(absFolder, "*_normal.png");
            string[] masks = Directory.GetFiles(absFolder, "*_mask.png");

            if (albedos.Length > 0)
            {
                albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(folderAssetPath + "/" + Path.GetFileName(albedos[0]));
            }
            if (normals.Length > 0)
            {
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(folderAssetPath + "/" + Path.GetFileName(normals[0]));
            }
            if (masks.Length > 0)
            {
                mask = AssetDatabase.LoadAssetAtPath<Texture2D>(folderAssetPath + "/" + Path.GetFileName(masks[0]));
            }

            return albedo != null;
        }

        private static Bounds MeasureCombinedRendererBounds(Renderer[] renderers)
        {
            Bounds combined = new Bounds(Vector3.zero, Vector3.zero);
            bool initialized = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                if (!initialized)
                {
                    combined = renderers[i].bounds;
                    initialized = true;
                }
                else
                {
                    combined.Encapsulate(renderers[i].bounds);
                }
            }

            return combined;
        }

        private static void AccumulateBounds(GameObject go, ref Bounds overall, ref bool hasBounds)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!hasBounds)
                {
                    overall = renderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    overall.Encapsulate(renderers[i].bounds);
                }
            }
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

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalized = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(projectRoot, normalized);
        }
    }
}
