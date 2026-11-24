using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Realmz.Cartography;
using Realmz.Game;
using Realmz.World;
using RealmzCore.Data;
using RealmzCore.Exploration;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Realmz.PlayModeTests
{
    public sealed class RealmzM1PlayModeTests
    {
        [UnityTest]
        public IEnumerator PlayMode_BywaterSlice_GroundedUnits_BuildingDetour_NpcTrade_AndModalScrollLock_ZeroErrors()
        {
            var errors = new List<string>();
            void LogHandler(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning)
                {
                    if (!condition.Contains("Curl error") && !condition.Contains("LicenseClient"))
                    {
                        errors.Add($"[{type}] {condition}");
                    }
                }
            }

            Application.logMessageReceived += LogHandler;
            try
            {
                AsyncOperation loadOp = SceneManager.LoadSceneAsync("M1_Bywater_Slice", LoadSceneMode.Single);
                while (loadOp != null && !loadOp.isDone)
                {
                    yield return null;
                }
                yield return null;
                yield return null;

                BywaterGameController ctrl = Object.FindFirstObjectByType<BywaterGameController>();
                Assert.That(ctrl, Is.Not.Null, "BywaterGameController missing in PlayMode scene.");
                Assert.That(ctrl.ExploreSession, Is.Not.Null);
                Assert.That(ctrl.hudController, Is.Not.Null);
                Assert.That(ctrl.cameraRig, Is.Not.Null);
                Assert.That(ctrl.worldRenderer, Is.Not.Null);

                bool captureProofs = string.Equals(Environment.GetEnvironmentVariable("REALMZ_CAPTURE_PROOFS"), "1", StringComparison.Ordinal);

                // 1. Verify Aldric grounded right inside UnderRing at explore spawn (10, 9)
                ctrl.WarpToOverworldTownShowcase();
                yield return null;
                yield return null;

                GameObject heroGo = GameObject.Find("PartyHeroView_0");
                Assert.That(heroGo, Is.Not.Null);
                Transform sq = heroGo.transform.Find("SpriteBillboard/SpriteQuad");
                Transform ring = heroGo.transform.Find("UnderRing");
                Assert.That(sq, Is.Not.Null);
                Assert.That(ring, Is.Not.Null);
                Assert.That(sq.TransformPoint(Vector3.zero).y, Is.EqualTo(heroGo.transform.position.y).Within(0.02f));
                Assert.That(Vector2.Distance(
                    new Vector2(sq.position.x, sq.position.z),
                    new Vector2(ring.position.x, ring.position.z)), Is.LessThan(0.05f));

                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        Path.GetFullPath("M1_Bywater_Explore_Preview.png"), 1600, 900);
                }

                // 2. Verify Party/Stash modal locks scroll zoom (0.0f delta) across live PlayMode frames
                float distBefore = ctrl.cameraRig.TargetDistance;
                ctrl.hudController.TogglePartyPanel(true);
                yield return null;
                Assert.That(ctrl.hudController.IsPartyOpen, Is.True);
                Assert.That(ctrl.cameraRig.ApplyScrollZoomDelta(240f), Is.EqualTo(0f));
                Assert.That(ctrl.cameraRig.ApplyScrollZoomDelta(-240f), Is.EqualTo(0f));
                Assert.That(ctrl.cameraRig.ApplyKeyboardPanInput(Vector2.up, false, 0.25f), Is.EqualTo(Vector3.zero));
                yield return null;
                Assert.That(ctrl.cameraRig.TargetDistance, Is.EqualTo(distBefore));
                ctrl.hudController.TogglePartyPanel(false);
                yield return null;

                // 3. Verify clicking Borin navigates from (11,9) to adjacent tile cleanly (never onto Borin's tile) and shows compact prompt / opens Shop
                ctrl.ExploreSession.MoveAlongPath(new GridCoord(11, 9), autoHaltOnEvents: false);
                typeof(BywaterGameController).GetMethod("SyncExploreHeroViewsToTrail", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { true });
                yield return null;

                CartographyNpcPlacement borin = ctrl.worldRenderer.ActiveNpcs[0];
                GridCoord borinCoord = new GridCoord(borin.TileX, borin.TileY);
                typeof(BywaterGameController).GetMethod("UpdateExplorationInput", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { borinCoord, true });
                ctrl.ForceCompleteWalkForTest();
                yield return null;
                Assert.That(ctrl.ExploreSession.Position, Is.Not.EqualTo(borinCoord));
                Assert.That(ctrl.ExploreSession.Position.ChebyshevDistance(borinCoord), Is.LessThanOrEqualTo(1));
                Assert.That(ctrl.hudController.IsShopOpen, Is.True);
                ctrl.hudController.CloseShopModal();
                yield return null;
                yield return null;

                if (captureProofs)
                {
                    typeof(BywaterGameController).GetMethod("UpdateExplorationHoverVisuals", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(ctrl, new object[] { borinCoord });
                    yield return null;
                    CaptureCameraToPng(Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_Feet_And_Npc_Proof.png", 1600, 900);
                }

                // 4. Verify live pathing detours around Pack 3 building footprints without stepping on blocked interior tiles
                ctrl.ExploreSession.MoveAlongPath(new GridCoord(10, 10), autoHaltOnEvents: false);
                typeof(BywaterGameController).GetMethod("SyncExploreHeroViewsToTrail", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { true });
                ctrl.cameraRig.SetFollowTarget(CartographyWorldData.GridToWorld(8, 11, 0f), immediate: true, recenterManualPan: true);
                yield return null;

                GridCoord detourTarget = new GridCoord(7, 11);
                PathResult detourPath = ctrl.ExploreSession.PlanPath(detourTarget, allowDiagonal: true);
                Assert.That(detourPath.Found, Is.True, "Detour around Pack 3 building to Inn/Armory doorway (7,11) must succeed.");
                for (int i = 0; i < detourPath.Steps.Count; i++)
                {
                    GridCoord step = detourPath.Steps[i];
                    Assert.That(ctrl.ExploreSession.CurrentMap.IsBlockedByProp(step.X, step.Y), Is.False,
                        $"Detour step {step} must never enter blocked building/NPC footprint.");
                }

                if (captureProofs)
                {
                    typeof(BywaterGameController).GetMethod("UpdateExplorationHoverVisuals", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(ctrl, new object[] { detourTarget });
                    yield return null;
                    CaptureCameraToPng(Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_Building_Avoid_Proof.png", 1600, 900);
                }

                typeof(BywaterGameController).GetMethod("UpdateExplorationInput", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { detourTarget, true });
                ctrl.ForceCompleteWalkForTest();
                yield return null;
                Assert.That(ctrl.ExploreSession.CurrentMap.IsBlockedByProp(ctrl.ExploreSession.Position.X, ctrl.ExploreSession.Position.Y), Is.False);

                // 5. Verify live Combat stage keeps units grounded cleanly as well
                ctrl.StageCombatOverlayShowcaseForCapture();
                yield return null;
                BywaterUnitView[] combatViews = Object.FindObjectsByType<BywaterUnitView>(FindObjectsSortMode.None);
                Assert.That(combatViews.Length, Is.GreaterThanOrEqualTo(6));
                for (int i = 0; i < combatViews.Length; i++)
                {
                    if (!combatViews[i].gameObject.activeInHierarchy) continue;
                    Transform cSq = combatViews[i].transform.Find("SpriteBillboard/SpriteQuad");
                    Assert.That(cSq, Is.Not.Null);
                    Assert.That(cSq.TransformPoint(Vector3.zero).y, Is.EqualTo(combatViews[i].transform.position.y).Within(0.02f));
                }

                if (captureProofs)
                {
                    CaptureCameraToPng(Camera.main, ctrl.hudController,
                        Path.GetFullPath("M1_Bywater_Combat_Preview.png"), 1600, 900);
                }

                Assert.That(errors, Is.Empty, "PlayMode runtime errors/warnings detected:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }
        }

        private static void CaptureCameraToPng(Camera cam, BywaterHudController hud, string outPngPath, int width, int height)
        {
            if (cam == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(outPngPath));

            Canvas hudCanvas = hud != null ? hud.GetComponentInChildren<Canvas>(true) : null;
            RenderMode prevMode = hudCanvas != null ? hudCanvas.renderMode : RenderMode.ScreenSpaceOverlay;
            Camera prevWorldCam = hudCanvas != null ? hudCanvas.worldCamera : null;
            float prevPlaneDist = hudCanvas != null ? hudCanvas.planeDistance : 100f;

            if (hudCanvas != null)
            {
                hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                hudCanvas.worldCamera = cam;
                hudCanvas.planeDistance = 0.45f;
            }

            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture prevAct = RenderTexture.active;
            RenderTexture prevTgt = cam.targetTexture;
            try
            {
                Canvas.ForceUpdateCanvases();
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false, false);
                File.WriteAllBytes(outPngPath, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = prevTgt;
                RenderTexture.active = prevAct;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                if (hudCanvas != null)
                {
                    hudCanvas.renderMode = prevMode;
                    hudCanvas.worldCamera = prevWorldCam;
                    hudCanvas.planeDistance = prevPlaneDist;
                }
            }
        }
    }
}
