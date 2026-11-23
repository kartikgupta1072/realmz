using System;
using System.IO;
using Realmz.Cartography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Realmz.EditorTools.Cartography
{
    /// <summary>
    /// Programmatic EditMode/batchmode camera capture utilities for verifying the
    /// Bywater 18x18 proof scene and staggered grass/cloud wave sequences.
    /// </summary>
    public static class CartographyCaptureTools
    {
        public const int DefaultWidth = 1600;
        public const int DefaultHeight = 900;

        [MenuItem("Realmz/Cartography/Capture Proof Scene Preview")]
        public static void CaptureDefaultPreviewMenu()
        {
            string outPath = Path.GetFullPath("Bywater_SoC_VerticalSlice_Preview.png");
            CaptureProofScene(outPath);
        }

        public static string CaptureProofScene(string outPngPath)
        {
            if (string.IsNullOrWhiteSpace(outPngPath))
            {
                outPngPath = Path.GetFullPath("Bywater_SoC_VerticalSlice_Preview.png");
            }

            EnsureProofSceneLoaded();

            Camera cam = ResolveProofCamera();
            if (cam == null)
            {
                throw new InvalidOperationException("No Main Camera found in BywaterDiorama_Proof.unity.");
            }

            BywaterDioramaController controller = UnityEngine.Object.FindFirstObjectByType<BywaterDioramaController>();
            if (controller != null)
            {
                controller.SyncGlobals(0f, 0f);
            }

            BywaterFoliageScatter scatter = UnityEngine.Object.FindFirstObjectByType<BywaterFoliageScatter>();
            if (scatter != null)
            {
                scatter.RenderBatches(cam);
            }

            RenderCameraToPng(cam, outPngPath, DefaultWidth, DefaultHeight);
            Debug.Log("[CartographyCaptureTools] Captured proof scene to: " + outPngPath);
            return outPngPath;
        }

        public static void CaptureGrassWaveFrames(string outDir, int count)
        {
            if (string.IsNullOrWhiteSpace(outDir))
            {
                outDir = Path.GetFullPath("Temp/CartographyWaveCapture");
            }
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            EnsureProofSceneLoaded();

            Camera cam = ResolveProofCamera();
            if (cam == null)
            {
                throw new InvalidOperationException("No Main Camera found in BywaterDiorama_Proof.unity.");
            }

            BywaterDioramaController controller = UnityEngine.Object.FindFirstObjectByType<BywaterDioramaController>();
            BywaterFoliageScatter scatter = UnityEngine.Object.FindFirstObjectByType<BywaterFoliageScatter>();

            int safeCount = Mathf.Max(1, count);
            for (int i = 0; i < safeCount; i++)
            {
                float simTime = i * 0.25f;
                if (controller != null)
                {
                    if (controller.heroTransform != null)
                    {
                        float angle = simTime * 1.2f;
                        controller.heroTransform.position = controller.patrolCenter + new Vector3(
                            Mathf.Sin(angle) * 1.3f,
                            0f,
                            Mathf.Cos(angle) * 0.6f);
                    }
                    controller.SyncGlobals(simTime, simTime);
                }

                if (scatter != null)
                {
                    scatter.RenderBatches(cam);
                }

                string framePath = Path.Combine(outDir, $"wave_frame_{i:D2}.png");
                RenderCameraToPng(cam, framePath, DefaultWidth, DefaultHeight);
            }

            Debug.Log($"[CartographyCaptureTools] Captured {safeCount} wave frames into: {outDir}");
        }

        public static void BatchCaptureProofFromCli()
        {
            string outPath = GetCliArg("-cartoOut", Path.GetFullPath("Bywater_SoC_VerticalSlice_Preview.png"));
            CaptureProofScene(outPath);

            string waveDir = GetCliArg("-cartoWaveDir", string.Empty);
            if (!string.IsNullOrEmpty(waveDir))
            {
                CaptureGrassWaveFrames(waveDir, 6);
            }
        }

        private static void EnsureProofSceneLoaded()
        {
            if (!File.Exists(Path.GetFullPath(BywaterDioramaSceneBuilder.ScenePath)))
            {
                BywaterDioramaSceneBuilder.BuildProofScene();
                return;
            }

            UnityEngine.SceneManagement.Scene active = EditorSceneManager.GetActiveScene();
            if (!string.Equals(active.path, BywaterDioramaSceneBuilder.ScenePath, StringComparison.OrdinalIgnoreCase))
            {
                EditorSceneManager.OpenScene(BywaterDioramaSceneBuilder.ScenePath, OpenSceneMode.Single);
            }
        }

        private static Camera ResolveProofCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            }
            return cam;
        }

        private static void RenderCameraToPng(Camera cam, string outPngPath, int width, int height)
        {
            string dir = Path.GetDirectoryName(outPngPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevTarget = cam.targetTexture;

            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 1;
            rt.Create();

            Texture2D readTex = new Texture2D(width, height, TextureFormat.RGB24, false);

            try
            {
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                readTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readTex.Apply(false, false);

                byte[] png = readTex.EncodeToPNG();
                File.WriteAllBytes(outPngPath, png);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(readTex);
            }
        }

        private static string GetCliArg(string flag, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return fallback;
        }
    }
}
