using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Realmz.Cartography;
using Realmz.Game;
using Realmz.World;
using RealmzCore.Combat;
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

                // 5. Verify Town Plaza Combat hides all 4 shopkeeper NPCs cleanly and Combat stage showcases intent + corpse + Melee cancel
                ctrl.ExploreSession.MoveAlongPath(new GridCoord(10, 9), autoHaltOnEvents: false);
                ctrl.TriggerDemoSkirmish();
                yield return null;
                Assert.That(ctrl.worldRenderer.AreTownNpcsHiddenForCombat, Is.True, "Town NPCs must hide in combat.");
                Assert.That(ctrl.worldRenderer.VisibleTownNpcCount, Is.EqualTo(0), "Zero town NPCs allowed inside combat.");

                ctrl.StageCombatOverlayShowcaseForCapture();
                yield return null;
                BywaterUnitView[] combatViews = Object.FindObjectsByType<BywaterUnitView>(FindObjectsSortMode.None);
                Assert.That(combatViews.Length, Is.GreaterThanOrEqualTo(6));
                for (int i = 0; i < combatViews.Length; i++)
                {
                    if (!combatViews[i].gameObject.activeInHierarchy) continue;
                    Transform cSq = combatViews[i].transform.Find("SpriteBillboard/SpriteQuad");
                    Assert.That(cSq, Is.Not.Null);
                    Assert.That(cSq.TransformPoint(Vector3.zero).y, Is.EqualTo(combatViews[i].transform.position.y).Within(0.025f));
                }

                // Hover Giant Frog cleanly so directional enemy intent arc + active hero & intent tooltip appear in preview
                CombatantState giantFrog = null;
                CombatantState goblin = null;
                for (int i = 0; i < ctrl.CombatSession.Combatants.Count; i++)
                {
                    CombatantState unit = ctrl.CombatSession.Combatants[i];
                    if (!unit.IsAlive || unit.Side != CombatSide.Monster) continue;
                    if (unit.Id.IndexOf("frog", StringComparison.OrdinalIgnoreCase) >= 0) giantFrog = unit;
                    else if (goblin == null) goblin = unit;
                }

                if (giantFrog != null)
                {
                    typeof(BywaterGameController).GetMethod("UpdateCombatHoverVisuals", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(ctrl, new object[] { giantFrog.Position });
                    if (Camera.main != null)
                    {
                        GridCoord frogWorldGrid = new GridCoord(ctrl.ArenaOrigin.X + giantFrog.Position.X, ctrl.ArenaOrigin.Y + giantFrog.Position.Y);
                        Vector3 frogWorld = CartographyWorldData.GridToWorld(frogWorldGrid, ctrl.worldRenderer.GetTileElevation(frogWorldGrid.X, frogWorldGrid.Y));
                        ctrl.hudController.UpdateCursorTooltipPosition(Camera.main.WorldToScreenPoint(frogWorld));
                    }
                    yield return null;
                }

                if (captureProofs)
                {
                    CaptureCameraToPng(Camera.main, ctrl.hudController,
                        Path.GetFullPath("M1_Bywater_Combat_Preview.png"), 1600, 900);
                }

                // 6. Bug #4 + Bug #5 Proof: Topple Giant Frog into settled flat corpse, place living Goblin on overlapping spot, activate Melee mode & test cancel/smart-move
                if (giantFrog != null)
                {
                    giantFrog.Hp = 0;
                    typeof(BywaterGameController).GetMethod("SyncAllCombatUnitViews", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(ctrl, null);
                    yield return new WaitForSeconds(0.45f);
                    if (goblin != null)
                    {
                        GridCoord overlapTile = new GridCoord(
                            Mathf.Clamp(giantFrog.Position.X, 0, ctrl.CombatSession.Grid.Width - 1),
                            Mathf.Clamp(giantFrog.Position.Y, 0, ctrl.CombatSession.Grid.Height - 1));
                        goblin.Position = overlapTile;
                        var viewsDict = typeof(BywaterGameController).GetField("_combatUnitViews", BindingFlags.Instance | BindingFlags.NonPublic)
                            ?.GetValue(ctrl) as Dictionary<string, BywaterUnitView>;
                        if (viewsDict != null && viewsDict.TryGetValue(goblin.Id, out BywaterUnitView goblinView) && goblinView != null)
                        {
                            GridCoord worldTile = new GridCoord(ctrl.ArenaOrigin.X + overlapTile.X, ctrl.ArenaOrigin.Y + overlapTile.Y);
                            Vector3 worldPos = CartographyWorldData.GridToWorld(worldTile, ctrl.worldRenderer.GetTileElevation(worldTile.X, worldTile.Y));
                            goblinView.SetWorldPosition(worldPos);
                        }
                        typeof(BywaterGameController).GetMethod("SyncAllCombatUnitViews", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(ctrl, null);
                        yield return null;
                    }
                }

                ctrl.SelectCombatModeForUi(CombatActionMode.Melee);
                yield return null;
                Assert.That(ctrl.ActiveCombatMode, Is.EqualTo(CombatActionMode.Melee));
                Assert.That(ctrl.hudController.IsTargetingBannerVisible, Is.True);

                CombatantState currentHero = ctrl.CombatSession.CurrentActor;
                GridCoord smartMoveTile = currentHero.Position;
                IReadOnlyList<ReachableTileInfo> heroReach = ctrl.CombatSession.GetReachableTiles(currentHero.Id);
                for (int i = 0; i < heroReach.Count; i++)
                {
                    GridCoord candidate = heroReach[i].Coord;
                    bool occupied = false;
                    for (int c = 0; c < ctrl.CombatSession.Combatants.Count; c++)
                    {
                        CombatantState unit = ctrl.CombatSession.Combatants[c];
                        if (unit.IsAlive && unit.Position == candidate)
                        {
                            occupied = true;
                            break;
                        }
                    }
                    if (candidate != currentHero.Position && !occupied)
                    {
                        smartMoveTile = candidate;
                        break;
                    }
                }

                if (smartMoveTile != currentHero.Position)
                {
                    typeof(BywaterGameController).GetMethod("UpdateCombatHoverVisuals", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(ctrl, new object[] { smartMoveTile });
                    if (Camera.main != null)
                    {
                        GridCoord moveWorldGrid = new GridCoord(ctrl.ArenaOrigin.X + smartMoveTile.X, ctrl.ArenaOrigin.Y + smartMoveTile.Y);
                        Vector3 moveWorld = CartographyWorldData.GridToWorld(moveWorldGrid, ctrl.worldRenderer.GetTileElevation(moveWorldGrid.X, moveWorldGrid.Y));
                        ctrl.hudController.UpdateCursorTooltipPosition(Camera.main.WorldToScreenPoint(moveWorld));
                    }
                    yield return null;
                }

                if (captureProofs)
                {
                    CaptureCameraToPng(Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_Corpse_And_MeleeCancel_Proof.png", 1600, 900);
                }

                if (smartMoveTile != currentHero.Position)
                {
                    ctrl.ClickCombatCoordForTest(smartMoveTile);
                    yield return null;
                    Assert.That(currentHero.Position, Is.EqualTo(smartMoveTile));
                    Assert.That(ctrl.ActiveCombatMode, Is.EqualTo(CombatActionMode.Smart));
                }

                // Exit combat -> confirm all 4 shopkeeper NPCs cleanly restored
                typeof(BywaterGameController).GetMethod("ExitCombatToExploration", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { false });
                yield return null;
                Assert.That(ctrl.worldRenderer.AreTownNpcsHiddenForCombat, Is.False);
                Assert.That(ctrl.worldRenderer.VisibleTownNpcCount, Is.EqualTo(4));

                // 7. UX Overhaul #7-#10 Live Verification + Proof Captures
                // Stage 9-slot gear, Camp bandages (1/2 after click), stacked potions/torches -> Paper-Doll & Camp proof
                PartyState party = ctrl.ExploreSession.Party;
                party.AddItemToStash(452, 1);
                party.AddItemToStash(455, 1);
                party.AddItemToStash(250, 1);
                party.AddItemToStash(622, 1);
                party.AddItemToStash(675, 1);
                party.AddItemToStash(603, 1, charges: 3);
                party.AddItemToStash(723, 1, charges: 2);
                party.EquipBestForHero(0, ctrl.Bundle.Items);
                party.Members[0].Hp = Math.Max(1, party.Members[0].MaxHp - 10);

                ctrl.hudController.TogglePartyPanel(true);
                ctrl.hudController.RefreshPartyDrawer(party, ctrl.Bundle, 0);
                yield return null;
                UnityEngine.UI.Button qbBtn = GameObject.Find("QuickBandageBtn")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(qbBtn, Is.Not.Null);
                qbBtn.onClick.Invoke();
                yield return null;
                Assert.That(party.QuickBandageCharges, Is.EqualTo(1));

                if (captureProofs)
                {
                    CaptureCameraToPng(Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_PaperDoll_Camp_Proof.png", 1600, 900);
                }

                // Level-Up badge & interactive modal -> LevelUp proof
                party.Members[0].AwardXp(250);
                ctrl.hudController.RefreshExploreHud(ctrl.ExploreSession, ctrl.Bundle, null, null, 0, string.Empty, false);
                ctrl.hudController.OpenLevelUpModal(0);
                yield return null;
                Assert.That(ctrl.hudController.IsLevelUpModalOpen, Is.True);

                if (captureProofs)
                {
                    CaptureCameraToPng(Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_LevelUp_Proof.png", 1600, 900);
                }

                UnityEngine.UI.Button lvlConfirm = GameObject.Find("ConfirmLevelUpBtn")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(lvlConfirm, Is.Not.Null);
                lvlConfirm.onClick.Invoke();
                yield return null;
                Assert.That(party.Members[0].Level, Is.EqualTo(2));
                Assert.That(ctrl.hudController.IsLevelUpModalOpen, Is.False);

                // Rich 980x690 Victory Screen -> Victory Loot proof
                party.Members[1].AwardXp(250);
                List<HeroVictorySummary> hv = new List<HeroVictorySummary>();
                for (int i = 0; i < party.Members.Count; i++)
                {
                    PartyMemberState pm = party.Members[i];
                    hv.Add(new HeroVictorySummary(
                        pm.Id, pm.Name, pm.Archetype, pm.IconId, pm.PortraitId,
                        pm.Level, pm.Level, Math.Max(0, pm.Xp - 90), pm.Xp, 90,
                        PartyMemberState.GetXpRequiredForLevel(pm.Level), pm.XpForNextLevel,
                        pm.HasPendingLevelUp, pm.PendingLevelUps, i == 5, i == 0 ? 2 : 1, 24 - i * 2, i == 1 ? 12 : 0, i == 0));
                }
                List<LootStackSummary> loot = new List<LootStackSummary>
                {
                    new LootStackSummary(452, "Leather Gloves", "Gloves", "Hands", 2, 0, 5, false),
                    new LootStackSummary(622, "Speckled Luck Stone +2", "Accessory", "Accessory1", 1, 0, 750, false),
                    new LootStackSummary(250, "Padded Cloak", "Cloak", "Accessory2", 1, 0, 25, false),
                    new LootStackSummary(455, "Steel Shod Boots", "Boots", "Feet", 1, 0, 35, false),
                    new LootStackSummary(603, "Heat Resistance", "Potion", "Consumable", 1, 3, 300, false),
                    new LootStackSummary(611, "Heal Small Wounds", "Potion", "Consumable", 3, 3, 50, false),
                    new LootStackSummary(723, "Refresh Potion", "Potion", "Consumable", 1, 2, 500, false),
                };
                CombatVictorySummary summary = new CombatVictorySummary(540, 42, 8, 50, party.Gold, 4, hv, loot);
                ctrl.hudController.ShowVictoryOutcome(summary, party, ctrl.Bundle);
                yield return null;
                Assert.That(ctrl.hudController.IsOutcomeOpen, Is.True);

                if (captureProofs)
                {
                    CaptureCameraToPng(Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_Victory_Loot_Proof.png", 1600, 900);
                }

                ctrl.hudController.CloseAllModals();
                yield return null;

                Assert.That(errors, Is.Empty, "PlayMode runtime errors/warnings detected:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }
        }

        [UnityTest]
        public IEnumerator PlayMode_EncounterDirector_NightTorch_ScoutingModal_AndScrollLock()
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
                Assert.That(ctrl, Is.Not.Null);
                bool captureProofs = string.Equals(Environment.GetEnvironmentVariable("REALMZ_CAPTURE_PROOFS"), "1", StringComparison.Ordinal);

                // 1. Night exploration + lit warm Aldric torchlight on East Riverbank
                ctrl.StageNightTorchExploreShowcaseForCapture(new GridCoord(53, 62), worldClockTurns: 58, torchTurnsRemaining: 36);
                yield return null;
                yield return null;

                Assert.That(ctrl.ExploreSession.CurrentWorldClock.Phase, Is.EqualTo(WorldTimePhase.Night));
                Assert.That(ctrl.worldRenderer.IsPartyLanternEnabled, Is.True, "Warm Aldric torchlight must enable at Night.");
                Assert.That(ctrl.worldRenderer.PartyLanternIntensity, Is.GreaterThan(8.0f));
                Assert.That(ctrl.biomeAtmosphere.CurrentTimePhase, Is.EqualTo(WorldTimePhase.Night));

                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_Night_Torch_Proof.png", 1600, 900);
                }

                // 2. Encounter Intercept Modal on East Riverbank (Giant Frog + Goblin ecology pack) + Bug #7 Scroll Lock
                ctrl.StageScoutInterceptShowcaseForCapture(new GridCoord(53, 62), worldClockTurns: 24, torchTurnsRemaining: 24);
                yield return null;
                yield return null;

                Assert.That(ctrl.hudController.IsEncounterInterceptOpen, Is.True);
                Assert.That(ctrl.hudController.IsModalOpen, Is.True);
                Assert.That(ctrl.ExploreSession.GetCurrentGeography().LocationLabel, Is.EqualTo("Bywater East Riverbank"));
                Assert.That(ctrl.cameraRig.ApplyScrollZoomDelta(240f), Is.EqualTo(0f), "Bug #7 zoom lock must block while Intercept open.");
                Assert.That(ctrl.cameraRig.ApplyKeyboardPanInput(Vector2.right, false, 0.2f), Is.EqualTo(Vector3.zero));

                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_Scout_Intercept_Proof.png", 1600, 900);
                }

                // Engage -> launches TacticalCombat with riverbank water tiles
                UnityEngine.UI.Button engageBtn = GameObject.Find("InterceptEngageBtn")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(engageBtn, Is.Not.Null);
                engageBtn.onClick.Invoke();
                yield return null;
                yield return null;

                Assert.That(ctrl.IsInCombat, Is.True);
                Assert.That(ctrl.hudController.AreExploreButtonsHiddenInCombat, Is.True);
                Assert.That(ctrl.CombatSession.GetActiveSurfaceCells().Count, Is.GreaterThan(0),
                    "Riverbank combat arena should seed water/mud surface tiles.");

                Assert.That(errors, Is.Empty, "PlayMode EncounterDirector errors:\n" + string.Join("\n", errors));
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
                hud?.ClampTooltipToActiveCanvas();
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
