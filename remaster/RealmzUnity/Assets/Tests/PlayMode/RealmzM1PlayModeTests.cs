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
using RealmzCore.Rules;
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

                    // Additional clear cobblestone town avenues proof centered on NW avenue / Market plaza
                    ctrl.ExploreSession.MoveAlongPath(new GridCoord(11, 7), autoHaltOnEvents: false);
                    typeof(BywaterGameController).GetMethod("SyncExploreHeroViewsToTrail", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(ctrl, new object[] { true });
                    ctrl.cameraRig?.FocusWorld(
                        CartographyWorldData.GridToWorld(new GridCoord(12, 8), ctrl.worldRenderer.GetTileElevation(12, 8)),
                        immediate: true,
                        recenterManualPan: true);
                    yield return null;
                    yield return null;
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_TownRoads_Clear_Proof.png", 1600, 900);

                    ctrl.WarpToOverworldTownShowcase();
                    yield return null;
                    yield return null;
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

                // Rich 980x690 Option 2 Victory Screen contract check
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

                if (captureProofs)
                {
                    ctrl.StageCombatArenaArchetypeShowcaseForCapture(CombatArenaArchetype.RiverbankCrossing, new GridCoord(53, 62));
                    yield return null;
                    yield return null;
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_TacticalArena_River_Proof.png", 1600, 900);

                    ctrl.StageCombatArenaArchetypeShowcaseForCapture(CombatArenaArchetype.RoadAmbush, new GridCoord(18, 54));
                    yield return null;
                    yield return null;
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_TacticalArena_Road_Proof.png", 1600, 900);
                }

                Assert.That(errors, Is.Empty, "PlayMode EncounterDirector errors:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }
        }

        [UnityTest]
        public IEnumerator PlayMode_W249_NightCombatReadability_ForegroundClear_And_Option2VictoryLoot()
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

                // 1. Night Exploration with 0 torch -> engage Combat -> verify In the Dark & Combat Foreground Clear Buffer
                PartyState party = ctrl.ExploreSession.Party;
                party.AddItemToStash(805, 1, isIdentified: true, charges: 6);
                ctrl.StageScoutInterceptShowcaseForCapture(new GridCoord(18, 54), worldClockTurns: 58, torchTurnsRemaining: 0);
                yield return null;
                yield return null;

                UnityEngine.UI.Button engageBtn = GameObject.Find("InterceptEngageBtn")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(engageBtn, Is.Not.Null);
                engageBtn.onClick.Invoke();
                yield return null;
                yield return null;

                Assert.That(ctrl.IsInCombat, Is.True);
                Assert.That(ctrl.biomeAtmosphere.IsCombatActive, Is.True);
                Assert.That(ctrl.biomeAtmosphere.CurrentTimePhase, Is.EqualTo(WorldTimePhase.Night));
                Assert.That(RenderSettings.ambientIntensity, Is.InRange(0.95f, 1.12f));
                Assert.That(Shader.GetGlobalFloat("_GlobalNightRimStrength"), Is.GreaterThanOrEqualTo(0.50f));
                Assert.That(ctrl.worldRenderer.IsCombatForegroundClearActive, Is.True);
                Assert.That(ctrl.CombatSession.IsInTheDark, Is.True);

                GameObject darkBadge = GameObject.Find("CombatDarknessBadge");
                Assert.That(darkBadge, Is.Not.Null);
                Assert.That(darkBadge.activeInHierarchy, Is.True);

                // Wait for a conscious Hero turn with HUD action bar enabled (waiting out any EnemySurprisedParty monster turn animations)
                float waitElapsed = 0f;
                UnityEngine.UI.Button torchBtn = null;
                while (ctrl.CombatSession != null &&
                       ctrl.CombatSession.Outcome == CombatOutcome.InProgress &&
                       waitElapsed < 12f)
                {
                    torchBtn = GameObject.Find("CombatTorchBtn")?.GetComponent<UnityEngine.UI.Button>();
                    if (ctrl.CombatSession.CurrentActor != null &&
                        ctrl.CombatSession.CurrentActor.Side == CombatSide.Hero &&
                        ctrl.CombatSession.CurrentActor.Economy.HasSwiftAction &&
                        torchBtn != null &&
                        torchBtn.interactable)
                    {
                        break;
                    }
                    waitElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                Assert.That(ctrl.CombatSession.Outcome, Is.EqualTo(CombatOutcome.InProgress));
                Assert.That(ctrl.CombatSession.CurrentActor?.Side, Is.EqualTo(CombatSide.Hero));
                Assert.That(ctrl.CombatSession.CurrentActor.Economy.HasSwiftAction, Is.True,
                    $"Actor {ctrl.CombatSession.CurrentActor?.Name} missing Swift action for torch.");

                // 2. Light Torch inside Combat -> lifts In the Dark debuff and casts warm party-side Torchlight pool
                Assert.That(torchBtn, Is.Not.Null, "CombatTorchBtn should be active in dark combat.");
                Assert.That(torchBtn.interactable, Is.True,
                    $"CombatTorchBtn not interactable on turn of {ctrl.CombatSession.CurrentActor?.Name}.");
                torchBtn.onClick.Invoke();
                yield return null;
                yield return null;

                Assert.That(ctrl.CombatSession.IsInTheDark, Is.False, "Lighting a Torch in Combat must lift In the Dark.");
                Assert.That(ctrl.worldRenderer.IsPartyLanternEnabled, Is.True);
                Assert.That(ctrl.worldRenderer.PartyLanternIntensity, Is.GreaterThan(10f));
                Assert.That(darkBadge.activeInHierarchy, Is.False);

                // Ensure combat cover & units are clean for Night Combat readable proof capture
                ctrl.hudController?.HideToast();
                yield return null;
                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_NightCombat_Readable_Proof.png", 1600, 900);
                }

                // 3. Stage clean Option 2 Victory & Loot modal proof: fallen hero revived badge, Aldric level-up arrow badge, zero MVP, Loot title, gold coin icon, selectable cards
                int preVictoryStashTotal = 0;
                for (int i = 0; i < party.Stash.Count; i++)
                {
                    preVictoryStashTotal += party.Stash[i].Quantity;
                }

                party.Members[0].AwardXp(250);
                var lootField = typeof(TacticalCombatSession).GetField("_collectedLootItemIds", BindingFlags.Instance | BindingFlags.NonPublic);
                var lootList = lootField?.GetValue(ctrl.CombatSession) as List<int>;
                Assert.That(lootList, Is.Not.Null);
                lootList.Clear();
                lootList.AddRange(new[] { 10, 418, 622, 611, 250, 108, 450, 452, 454, 455, 209, 20 });

                CombatantState currentHero = ctrl.CombatSession.CurrentActor;
                Assert.That(currentHero, Is.Not.Null);
                Assert.That(currentHero.IsConscious, Is.True);
                CombatantState finalMob = null;
                bool markedRevivedHero = false;
                for (int i = 0; i < ctrl.CombatSession.Combatants.Count; i++)
                {
                    CombatantState u = ctrl.CombatSession.Combatants[i];
                    if (u.Side == CombatSide.Monster && u.IsAlive)
                    {
                        if (finalMob == null)
                        {
                            finalMob = u;
                        }
                        else
                        {
                            u.Hp = 0;
                        }
                    }
                    else if (u.Side == CombatSide.Hero && !markedRevivedHero &&
                             !string.Equals(u.Id, currentHero.Id, StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(u.Id, party.Members[0].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        u.Hp = 0; // Mark a non-Aldric ally fallen -> cleanly separate Gold LvUp on Aldric from Revive badge on ally
                        markedRevivedHero = true;
                    }
                }

                Assert.That(finalMob, Is.Not.Null);
                finalMob.Hp = 1;
                finalMob.Ac = 0;
                // Place actor adjacent to finalMob in bounds (avoiding same-tile distance 0)
                int adjX = finalMob.Position.X > 0 ? finalMob.Position.X - 1 : finalMob.Position.X + 1;
                currentHero.Position = new GridCoord(adjX, finalMob.Position.Y);
                currentHero.ToHit = 95;
                currentHero.WeaponDice = "2d6+6";
                CombatActionResult killRes = ctrl.CombatSession.ExecuteMeleeAttack(finalMob.Id);
                Assert.That(killRes.Success, Is.True, "Killing blow failed: " + killRes.Message);
                Assert.That(ctrl.CombatSession.Outcome, Is.EqualTo(CombatOutcome.Victory));

                typeof(BywaterGameController).GetMethod("HandleCombatOutcome", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(ctrl, null);
                yield return null;
                yield return null;

                Assert.That(ctrl.hudController.IsOutcomeOpen, Is.True);
                Assert.That(GameObject.Find("TakeAndEquipBtn"), Is.Null);
                Assert.That(GameObject.Find("VictoryLootHeader")?.GetComponent<TMPro.TextMeshProUGUI>()?.text, Is.EqualTo("Loot"));
                Assert.That(GameObject.Find("VictoryHero_0/Right/Top/LvlUpArrowBadge")?.activeInHierarchy, Is.True);
                bool hasRevivedIcon = false;
                for (int h = 0; h < party.Members.Count; h++)
                {
                    GameObject revGo = GameObject.Find($"VictoryHero_{h}/Right/Top/ReviveBadgeIcon");
                    if (revGo != null && revGo.activeInHierarchy)
                    {
                        hasRevivedIcon = true;
                        break;
                    }
                }
                Assert.That(hasRevivedIcon, Is.True, "At least one fallen hero must display ReviveBadgeIcon.");

                int duringVictoryStashTotal = 0;
                for (int i = 0; i < party.Stash.Count; i++) duringVictoryStashTotal += party.Stash[i].Quantity;
                Assert.That(duringVictoryStashTotal, Is.EqualTo(preVictoryStashTotal), "Victory loot must wait for Take Selected (X).");

                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_Victory_Loot_Proof.png", 1600, 900);
                }

                // Deselect card #1 and verify Take Selected (X) updates and deposits ONLY checked stacks
                CombatVictorySummary vSummary = ctrl.CombatSession.LastVictorySummary;
                Assert.That(vSummary, Is.Not.Null);
                int totalLootQty = 0;
                for (int i = 0; i < vSummary.LootStacks.Count; i++)
                {
                    totalLootQty += vSummary.LootStacks[i].Quantity;
                }
                int skippedQty = vSummary.LootStacks[1].Quantity;
                int expectedTakenQty = totalLootQty - skippedQty;

                UnityEngine.UI.Button tile1Btn = GameObject.Find("LootTile_1")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(tile1Btn, Is.Not.Null);
                tile1Btn.onClick.Invoke();
                yield return null;

                UnityEngine.UI.Button continueBtn = GameObject.Find("ContinueBtn")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(continueBtn, Is.Not.Null);
                Assert.That(continueBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>()?.text, Is.EqualTo($"Take Selected ({expectedTakenQty})"));
                continueBtn.onClick.Invoke();
                yield return null;
                yield return null;

                Assert.That(ctrl.IsInCombat, Is.False);
                Assert.That(ctrl.hudController.IsOutcomeOpen, Is.False);
                Assert.That(ctrl.worldRenderer.IsCombatForegroundClearActive, Is.False);

                int postVictoryStashTotal = 0;
                for (int i = 0; i < party.Stash.Count; i++) postVictoryStashTotal += party.Stash[i].Quantity;
                Assert.That(postVictoryStashTotal, Is.EqualTo(preVictoryStashTotal + expectedTakenQty));

                Assert.That(errors, Is.Empty, "PlayMode W-249 errors:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }
        }

        [UnityTest]
        public IEnumerator PlayMode_W253_PreBattleDeployment_And_UnifiedSixApCombat_CapturesProofs()
        {
            List<string> errors = new List<string>();
            void LogHandler(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    errors.Add($"{type}: {condition}");
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

                // Give Vex 2pc Scout's Leather (Leather Cap 450) so FreeMovePill is active cleanly during combat!
                PartyState party = ctrl.ExploreSession.Party;
                party.AddItemToStash(450, 1);
                int vexIdx = -1;
                for (int i = 0; i < party.Members.Count; i++)
                {
                    if (party.Members[i].Name == "Vex") { vexIdx = i; break; }
                }
                if (vexIdx >= 0)
                {
                    party.EquipFromStash(vexIdx, party.Stash.Count - 1, ctrl.Bundle.Items);
                }

                // 1. Trigger combat with Pre-Battle Deployment active
                ctrl.TriggerDemoSkirmish(enterDeployment: true);
                yield return null;
                yield return null;

                Assert.That(ctrl.IsInCombat, Is.True);
                Assert.That(ctrl.CombatSession.Phase, Is.EqualTo(CombatPhase.Deployment));
                Assert.That(ctrl.CombatSession.DeploymentZoneTiles.Count, Is.GreaterThanOrEqualTo(6));
                GameObject deployBanner = GameObject.Find("DeploymentBanner");
                Assert.That(deployBanner != null && deployBanner.activeInHierarchy, Is.True, "DeploymentBanner must be active in Deployment phase.");

                CombatantState heroA = null;
                CombatantState heroB = null;
                for (int i = 0; i < ctrl.CombatSession.Combatants.Count; i++)
                {
                    CombatantState c = ctrl.CombatSession.Combatants[i];
                    if (c.Side == CombatSide.Hero && c.IsAlive)
                    {
                        if (heroA == null) heroA = c;
                        else if (heroB == null) { heroB = c; break; }
                    }
                }
                Assert.That(heroA != null && heroB != null, Is.True);
                GridCoord origA = heroA.Position;
                GridCoord origB = heroB.Position;

                ctrl.ClickCombatCoordForTest(origA);
                yield return null;
                ctrl.ClickCombatCoordForTest(origB);
                yield return null;
                Assert.That(heroA.Position, Is.EqualTo(origB));
                Assert.That(heroB.Position, Is.EqualTo(origA));

                // Re-select Hero A so highlighted cyan/gold tile shows clearly in Deployment proof
                ctrl.ClickCombatCoordForTest(heroA.Position);
                ctrl.hudController?.HideToast();
                yield return null;

                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_Deployment_Proof.png", 1600, 900);
                }

                // 2. Click Start Battle -> enter Unified 6-AP Combat
                UnityEngine.UI.Button startBattleBtn = GameObject.Find("StartBattleBtn")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(startBattleBtn, Is.Not.Null);
                startBattleBtn.onClick.Invoke();
                yield return null;

                int waitFrames = 0;
                while (ctrl.CombatSession != null &&
                       ctrl.CombatSession.Outcome == CombatOutcome.InProgress &&
                       (ctrl.CombatSession.CurrentActor == null ||
                        ctrl.CombatSession.CurrentActor.Side != CombatSide.Hero ||
                        !ctrl.CombatSession.CurrentActor.IsConscious) &&
                       waitFrames < 240)
                {
                    waitFrames++;
                    yield return new WaitForSeconds(0.05f);
                }

                Assert.That(ctrl.CombatSession.Phase, Is.EqualTo(CombatPhase.Combat));
                Assert.That(ctrl.CombatSession.CurrentActor?.Side, Is.EqualTo(CombatSide.Hero));
                CombatantState currentHero = ctrl.CombatSession.CurrentActor;

                // Hover nearest monster so Cost: X AP tooltip & AP Diamonds are displayed cleanly
                CombatantState enemy = null;
                for (int i = 0; i < ctrl.CombatSession.Combatants.Count; i++)
                {
                    CombatantState c = ctrl.CombatSession.Combatants[i];
                    if (c.Side == CombatSide.Monster && c.IsAlive) { enemy = c; break; }
                }
                Assert.That(enemy, Is.Not.Null);
                typeof(BywaterGameController).GetMethod("UpdateCombatHoverVisuals", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { enemy.Position });
                if (Camera.main != null)
                {
                    GridCoord wGrid = new GridCoord(ctrl.ArenaOrigin.X + enemy.Position.X, ctrl.ArenaOrigin.Y + enemy.Position.Y);
                    Vector3 wPos = CartographyWorldData.GridToWorld(wGrid, ctrl.worldRenderer.GetTileElevation(wGrid.X, wGrid.Y));
                    ctrl.hudController.UpdateCursorTooltipPosition(Camera.main.WorldToScreenPoint(wPos));
                }
                ctrl.hudController?.HideToast();
                yield return null;

                GameObject apRow = GameObject.Find("ApDiamondsRow");
                Assert.That(apRow != null && apRow.activeInHierarchy, Is.True);
                TMPro.TextMeshProUGUI apLbl = GameObject.Find("ApValueLabel")?.GetComponent<TMPro.TextMeshProUGUI>();
                Assert.That(apLbl != null && apLbl.text.Contains("AP"), Is.True);

                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\M1_Bywater_ActionPoints_Combat_Proof.png", 1600, 900);
                }

                // Execute Guard / Overwatch and verify exact AP deduction
                int apBeforeGuard = currentHero.Economy.CurrentAp;
                int expectedGuardCost = currentHero.Economy.EffectiveGuardApCost;
                GameObject.Find("GuardBtn")?.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
                yield return null;
                Assert.That(currentHero.IsGuarding, Is.True);
                Assert.That(currentHero.Economy.CurrentAp, Is.EqualTo(apBeforeGuard - expectedGuardCost));

                Assert.That(errors, Is.Empty, "PlayMode W-253 errors:\n" + string.Join("\n", errors));
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
            int prevOrder = hudCanvas != null ? hudCanvas.sortingOrder : 0;

            if (hudCanvas != null)
            {
                hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                hudCanvas.worldCamera = cam;
                hudCanvas.planeDistance = 0.35f;
                hudCanvas.sortingOrder = 200;
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
                    hudCanvas.sortingOrder = prevOrder;
                }
            }
        }

        [UnityTest]
        public IEnumerator PlayMode_Stage2_CinemachineTacticalPitch_FocusBeat_AndHudLabelBounds()
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

                ctrl.StageCombatArenaArchetypeShowcaseForCapture(CombatArenaArchetype.DeepWoods, new GridCoord(58, 18));
                yield return null;
                yield return null;

                Assert.That(ctrl.IsInCombat, Is.True);
                Assert.That(ctrl.cameraRig, Is.Not.Null);
                Assert.That(ctrl.cameraRig.transform.Find("CinemachineVirtualCamera"), Is.Not.Null, "CinemachineVirtualCamera must be active on BywaterCameraRig.");
                Assert.That(ctrl.cameraRig.IsTacticalPitchActive, Is.False);

                float pitchBefore = ctrl.cameraRig.CurrentPitchDegrees;
                UnityEngine.UI.Button pitchBtn = GameObject.Find("CombatPitchBtn")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(pitchBtn, Is.Not.Null, "CombatPitchBtn missing in combat top bar.");
                pitchBtn.onClick.Invoke();

                float elapsed = 0f;
                while (elapsed < 0.45f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                Assert.That(ctrl.cameraRig.IsTacticalPitchActive, Is.True);
                Assert.That(ctrl.cameraRig.CurrentPitchDegrees, Is.GreaterThan(pitchBefore + 5f),
                    "Tactical pitch toggle [T] must smoothly raise camera pitch.");

                ctrl.cameraRig.PlayFocusBeat(ctrl.cameraRig.transform.position, -1.5f, 0.25f);
                yield return null;

                // Verify active HUD buttons in TopBar and TacticalActionBar render without TMP truncation
                GameObject actionBar = GameObject.Find("TacticalActionBar");
                Assert.That(actionBar != null && actionBar.activeInHierarchy, Is.True);
                UnityEngine.UI.Button[] barButtons = actionBar.GetComponentsInChildren<UnityEngine.UI.Button>(false);
                for (int i = 0; i < barButtons.Length; i++)
                {
                    TMPro.TextMeshProUGUI[] btnLabels = barButtons[i].GetComponentsInChildren<TMPro.TextMeshProUGUI>(false);
                    for (int j = 0; j < btnLabels.Length; j++)
                    {
                        TMPro.TextMeshProUGUI lbl = btnLabels[j];
                        if (string.IsNullOrWhiteSpace(lbl.text)) continue;
                        lbl.ForceMeshUpdate();
                        Assert.That(lbl.isTextTruncated, Is.False,
                            $"Combat HUD label '{lbl.gameObject.name}' ('{lbl.text}') was truncated.");
                    }
                }

                TMPro.TextMeshProUGUI pitchBtnLbl = pitchBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                Assert.That(pitchBtnLbl, Is.Not.Null);
                pitchBtnLbl.ForceMeshUpdate();
                Assert.That(pitchBtnLbl.isTextTruncated, Is.False, "CombatPitchBtn label must not truncate.");

                ctrl.hudController?.HideToast();
                yield return null;

                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\stage2_proof_03_biome_forest_combat.png", 1600, 900);
                }

                pitchBtn.onClick.Invoke();
                yield return null;
                Assert.That(ctrl.cameraRig.IsTacticalPitchActive, Is.False);

                Assert.That(errors, Is.Empty, "PlayMode Stage2 TacticalPitch errors:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }
        }

        [UnityTest]
        public IEnumerator PlayMode_Stage2_OilKegDetonation_AndShoveBrazierHazard_VerifyContracts()
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

                ctrl.TriggerDemoSkirmish(enterDeployment: false);
                yield return null;
                yield return null;

                float waitElapsed = 0f;
                while (ctrl.CombatSession != null &&
                       ctrl.CombatSession.Outcome == CombatOutcome.InProgress &&
                       (ctrl.CombatSession.CurrentActor == null || ctrl.CombatSession.CurrentActor.Side != CombatSide.Hero) &&
                       waitElapsed < 10f)
                {
                    waitElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                Assert.That(ctrl.CombatSession, Is.Not.Null);
                Assert.That(ctrl.CombatSession.CurrentActor?.Side, Is.EqualTo(CombatSide.Hero));

                CombatantState hero = ctrl.CombatSession.CurrentActor;
                CombatantState enemy = null;
                for (int i = 0; i < ctrl.CombatSession.Combatants.Count; i++)
                {
                    CombatantState c = ctrl.CombatSession.Combatants[i];
                    if (c.Side == CombatSide.Monster && c.IsAlive)
                    {
                        enemy = c;
                        break;
                    }
                }
                Assert.That(enemy, Is.Not.Null);

                // 1. Find 3 open contiguous horizontal tiles inside the combat grid for Hero -> Enemy -> LitBrazier
                static CombatantState FindLivingAt(TacticalCombatSession session, GridCoord coord)
                {
                    for (int i = 0; i < session.Combatants.Count; i++)
                    {
                        CombatantState c = session.Combatants[i];
                        if (c.IsAlive && c.Position == coord)
                        {
                            return c;
                        }
                    }
                    return null;
                }

                GridCoord heroPos = new GridCoord(-1, -1);
                GridCoord enemyPos = new GridCoord(-1, -1);
                GridCoord brazierPos = new GridCoord(-1, -1);
                CombatGrid grid = ctrl.CombatSession.Grid;
                for (int y = 2; y < grid.Height - 2 && heroPos.X < 0; y++)
                {
                    for (int x = 2; x < grid.Width - 4; x++)
                    {
                        GridCoord c0 = new GridCoord(x, y);
                        GridCoord c1 = new GridCoord(x + 1, y);
                        GridCoord c2 = new GridCoord(x + 2, y);
                        if (!grid.IsWalkable(c0.X, c0.Y) || !grid.IsWalkable(c1.X, c1.Y) || !grid.IsWalkable(c2.X, c2.Y))
                        {
                            continue;
                        }
                        if (FindLivingAt(ctrl.CombatSession, c0) != null ||
                            FindLivingAt(ctrl.CombatSession, c1) != null ||
                            FindLivingAt(ctrl.CombatSession, c2) != null)
                        {
                            continue;
                        }
                        if (ctrl.CombatSession.GetDestructiblePropAt(c0) != null ||
                            ctrl.CombatSession.GetDestructiblePropAt(c1) != null ||
                            ctrl.CombatSession.GetDestructiblePropAt(c2) != null)
                        {
                            continue;
                        }

                        heroPos = c0;
                        enemyPos = c1;
                        brazierPos = c2;
                        break;
                    }
                }
                Assert.That(heroPos.X, Is.GreaterThanOrEqualTo(0), "Could not locate 3 open tiles for Shove -> Brazier test.");

                hero.Position = heroPos;
                enemy.Position = enemyPos;
                enemy.Hp = 40;
                enemy.MaxHp = 40;
                hero.Brawn = 30;
                hero.Economy.ResetUnifiedTurn(CombatActionEconomy.DefaultBaseApPerTurn, applyCarryover: false);

                CombatDestructibleProp brazier = ctrl.CombatSession.AddDestructibleProp(
                    "stage2_test_brazier", brazierPos, CombatArenaObstacleKind.LitBrazier);
                Assert.That(brazier, Is.Not.Null);

                ctrl.SelectCombatModeForUi(CombatActionMode.Shove);
                typeof(BywaterGameController).GetMethod("UpdateCombatHoverVisuals", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { enemy.Position });
                yield return null;

                TacticalShovePreview shovePreview = ctrl.CombatSession.PreviewShove(hero.Id, enemy.Id);
                Assert.That(shovePreview.CanExecute, Is.True);
                Assert.That(shovePreview.IgnitesOnBrazier, Is.True, "Shove preview must detect LitBrazier hazard.");

                GameObject shoveBadge = GameObject.Find("ShoveHazardBadge");
                Assert.That(shoveBadge != null && shoveBadge.activeInHierarchy, Is.True,
                    "ShoveHazardBadge must appear when hovering Shove -> Brazier.");
                TMPro.TextMeshProUGUI badgeLbl = shoveBadge.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                Assert.That(badgeLbl != null && badgeLbl.text.Contains("BRAZIER"), Is.True,
                    "ShoveHazardBadge must indicate Brazier ignition hazard.");

                bool shovedIntoBrazier = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (!hero.Economy.HasApFor(CombatActionEconomy.DefaultShoveApCost))
                    {
                        hero.Economy.ResetUnifiedTurn(CombatActionEconomy.DefaultBaseApPerTurn, applyCarryover: false);
                    }
                    CombatActionResult shoveRes = ctrl.CombatSession.TryShove(enemy.Id);
                    Assert.That(shoveRes.Success, Is.True);
                    if (enemy.Conditions.Has(RealmzCore.Rules.CharConditionId.Burning) || enemy.Hp < 40)
                    {
                        shovedIntoBrazier = true;
                        break;
                    }
                }
                Assert.That(shovedIntoBrazier, Is.True, "Shove -> Brazier must ignite or inflict Heat damage on target.");

                // 2. Destructible OilKegCluster detonation removes cover obstacle, fires PropDetonated, and leaves scorch
                CombatDestructibleProp keg = null;
                for (int i = 0; i < ctrl.CombatSession.DestructibleProps.Count; i++)
                {
                    CombatDestructibleProp p = ctrl.CombatSession.DestructibleProps[i];
                    if (p != null && !p.IsDestroyed && p.ObstacleKind == CombatArenaObstacleKind.OilKegCluster)
                    {
                        keg = p;
                        break;
                    }
                }
                if (keg == null)
                {
                    GridCoord kegCoord = new GridCoord(Mathf.Clamp(heroPos.X, 1, grid.Width - 2), Mathf.Clamp(heroPos.Y + 2, 1, grid.Height - 2));
                    keg = ctrl.CombatSession.AddDestructibleProp("stage2_test_oilkeg", kegCoord, CombatArenaObstacleKind.OilKegCluster);
                    ctrl.worldRenderer.ApplyCombatArenaVisuals(ctrl.ExploreSession.CurrentMap, ctrl.ArenaOrigin.X, ctrl.ArenaOrigin.Y, grid);
                }
                Assert.That(keg, Is.Not.Null);

                CombatExplosionEvent capturedBlast = null;
                ctrl.CombatSession.PropDetonated += ev => capturedBlast = ev;

                CombatActionResult dmgRes = ctrl.CombatSession.DamageDestructibleProp(keg.PropId, keg.MaxHp, "Heat", hero.Id);
                Assert.That(dmgRes.Success, Is.True);
                yield return null;
                yield return null;

                Assert.That(keg.IsDestroyed, Is.True, "OilKegCluster must be marked destroyed after detonation.");
                Assert.That(capturedBlast, Is.Not.Null, "PropDetonated event must fire on OilKegCluster detonation.");
                GridCoord worldKegCoord = new GridCoord(ctrl.ArenaOrigin.X + keg.Coord.X, ctrl.ArenaOrigin.Y + keg.Coord.Y);
                Assert.That(ctrl.worldRenderer.CartographyData.IsBlockedByProp(worldKegCoord.X, worldKegCoord.Y), Is.False,
                    "MarkArenaObstacleDestroyed must clear blocked prop tile on CartographyData.");
                Assert.That(grid.GetCell(keg.Coord.X, keg.Coord.Y).Surface, Is.EqualTo(SurfaceType.Fire),
                    "OilKegCluster detonation must leave Fire surface.");

                // 3. Stage Night Combat with Tactical Pitch + VFX proof capture
                if (captureProofs)
                {
                    ctrl.StageScoutInterceptShowcaseForCapture(new GridCoord(18, 54), worldClockTurns: 58, torchTurnsRemaining: 24);
                    yield return null;
                    GameObject.Find("InterceptEngageBtn")?.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
                    yield return null;
                    yield return null;
                    GameObject.Find("StartBattleBtn")?.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
                    yield return null;
                    yield return null;

                    GameObject.Find("CombatPitchBtn")?.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
                    float pWait = 0f;
                    while (pWait < 0.45f)
                    {
                        pWait += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    BywaterCombatVfxDirector vfx = ctrl.gridOverlays != null ? ctrl.gridOverlays.GetComponent<BywaterCombatVfxDirector>() : null;
                    if (vfx != null && ctrl.CombatSession.CurrentActor != null)
                    {
                        GridCoord aWorld = new GridCoord(ctrl.ArenaOrigin.X + ctrl.CombatSession.CurrentActor.Position.X,
                            ctrl.ArenaOrigin.Y + ctrl.CombatSession.CurrentActor.Position.Y);
                        Vector3 aPos = CartographyWorldData.GridToWorld(aWorld, ctrl.worldRenderer.GetTileElevation(aWorld.X, aWorld.Y));
                        vfx.PlayKegExplosion(aPos + Vector3.right * 2.2f, 1.9f);
                        yield return null;
                        yield return null;
                    }

                    ctrl.hudController?.HideToast();
                    yield return null;
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\stage2_proof_04_night_combat_vfx_pitch.png", 1600, 900);
                }

                Assert.That(errors, Is.Empty, "PlayMode Stage2 OilKeg/Shove errors:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }
        }

        [UnityTest]
        public IEnumerator PlayMode_Stage2_FogOfWar3State_ContinuousSunRotation_AndClockHud_VerifyCleanly()
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

                ctrl.WarpToOverworldTownShowcase();
                yield return null;
                yield return null;

                Assert.That(Shader.GetGlobalFloat("_FogOfWarEnabled"), Is.GreaterThan(0.5f),
                    "Live Exploration must enable _FogOfWarEnabled shader state.");

                TMPro.TextMeshProUGUI clockTxt = GameObject.Find("ClockText")?.GetComponent<TMPro.TextMeshProUGUI>();
                Assert.That(clockTxt, Is.Not.Null, "ClockText missing in Explore HUD.");
                Assert.That(clockTxt.text, Does.StartWith("Day 1 - "));
                clockTxt.ForceMeshUpdate();
                Assert.That(clockTxt.isTextTruncated, Is.False, "Explore ClockText must not truncate.");

                ctrl.hudController?.HideToast();
                yield return null;
                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\stage2_proof_01_town_day.png", 1600, 900);
                }

                MethodInfo applyAtmoMethod = typeof(BywaterGameController).GetMethod(
                    "ApplyAtmosphereAndTorchLighting", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(applyAtmoMethod, Is.Not.Null);

                // Advance continuous WorldClock across Dawn -> Noon -> Dusk and verify directional sun arc rotation
                double startMin = ctrl.ExploreSession.CurrentWorldClock.TimeOfDayMinutes;
                double toNextDawn = (WorldClock.MinutesPerDay - startMin) + 360.0; // 06:00 AM Dawn
                ctrl.ExploreSession.AdvanceWorldClockMinutes(toNextDawn);
                Vector3 focusPos = CartographyWorldData.GridToWorld(
                    ctrl.ExploreSession.Position,
                    ctrl.worldRenderer.GetTileElevation(ctrl.ExploreSession.Position.X, ctrl.ExploreSession.Position.Y));
                applyAtmoMethod.Invoke(ctrl, new object[] { focusPos, true, true });
                ctrl.hudController.RefreshExploreHud(ctrl.ExploreSession, ctrl.Bundle, null, null, 0, string.Empty, false);
                yield return null;

                Quaternion qDawn = ctrl.biomeAtmosphere.CurrentSunRotation;
                Assert.That(clockTxt.text, Is.EqualTo(ctrl.ExploreSession.CurrentWorldClock.DisplayLabel));
                clockTxt.ForceMeshUpdate();
                Assert.That(clockTxt.isTextTruncated, Is.False);

                ctrl.ExploreSession.AdvanceWorldClockMinutes(390.0); // 12:30 PM Noon
                applyAtmoMethod.Invoke(ctrl, new object[] { focusPos, true, true });
                ctrl.hudController.RefreshExploreHud(ctrl.ExploreSession, ctrl.Bundle, null, null, 0, string.Empty, false);
                yield return null;
                Quaternion qNoon = ctrl.biomeAtmosphere.CurrentSunRotation;

                ctrl.ExploreSession.AdvanceWorldClockMinutes(345.0); // 06:15 PM Dusk
                applyAtmoMethod.Invoke(ctrl, new object[] { focusPos, true, true });
                ctrl.hudController.RefreshExploreHud(ctrl.ExploreSession, ctrl.Bundle, null, null, 0, string.Empty, false);
                yield return null;
                Quaternion qDusk = ctrl.biomeAtmosphere.CurrentSunRotation;

                Assert.That(Quaternion.Angle(qDawn, qNoon), Is.GreaterThan(15f), "Sun rotation must advance smoothly from Dawn to Noon.");
                Assert.That(Quaternion.Angle(qDawn, qDusk), Is.GreaterThan(40f), "Sun rotation must sweep smoothly from Dawn to Dusk.");
                if (ctrl.biomeAtmosphere.directionalSun != null)
                {
                    Assert.That(Quaternion.Angle(ctrl.biomeAtmosphere.directionalSun.transform.rotation, qDusk), Is.LessThan(1.0f));
                }

                // Stage Riverbank Dusk (06:15 PM) + Fog-of-War proof capture
                ctrl.ExploreSession.MoveAlongPath(new GridCoord(53, 62), autoHaltOnEvents: false);
                double curMinOfDay = ctrl.ExploreSession.CurrentWorldClock.TimeOfDayMinutes;
                double deltaToDusk = 1095.0 - curMinOfDay; // 18:15 (06:15 PM) Dusk
                if (deltaToDusk < 0.0) deltaToDusk += WorldClock.MinutesPerDay;
                if (deltaToDusk > 0.01) ctrl.ExploreSession.AdvanceWorldClockMinutes(deltaToDusk);
                typeof(BywaterGameController).GetMethod("SyncExploreHeroViewsToTrail", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(ctrl, new object[] { true });
                typeof(BywaterGameController).GetMethod("SyncFogOfWarAndWeatherVisuals", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(ctrl, null);
                Vector3 riverPos = CartographyWorldData.GridToWorld(new GridCoord(53, 62), ctrl.worldRenderer.GetTileElevation(53, 62));
                applyAtmoMethod.Invoke(ctrl, new object[] { riverPos, true, true });
                ctrl.cameraRig?.FocusWorld(riverPos, immediate: true, recenterManualPan: true);
                ctrl.hudController.RefreshExploreHud(ctrl.ExploreSession, ctrl.Bundle, null, null, 0, string.Empty, false);
                ctrl.hudController?.HideToast();
                yield return null;
                yield return null;

                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\stage2_proof_02_riverbank_dusk_fog.png", 1600, 900);
                }

                Assert.That(errors, Is.Empty, "PlayMode Stage2 Fog/Clock/Sun errors:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }
        }

        [UnityTest]
        public IEnumerator PlayMode_Stage2_AnimatedVictoryXp_Option2SelectableLoot_AndMixerBound()
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

                Assert.That(ctrl.audioDirector, Is.Not.Null);
                Assert.That(ctrl.audioDirector.MasterMixer, Is.Not.Null,
                    "RealmzMasterMixer must be bound on BywaterAudioDirector in M1_Bywater_Slice.");

                PartyState party = ctrl.ExploreSession.Party;
                int preStashTotal = 0;
                for (int i = 0; i < party.Stash.Count; i++) preStashTotal += party.Stash[i].Quantity;

                ctrl.TriggerDemoSkirmish(enterDeployment: false);
                yield return null;
                yield return null;

                float waitElapsed = 0f;
                while (ctrl.CombatSession != null &&
                       ctrl.CombatSession.Outcome == CombatOutcome.InProgress &&
                       (ctrl.CombatSession.CurrentActor == null || ctrl.CombatSession.CurrentActor.Side != CombatSide.Hero) &&
                       waitElapsed < 10f)
                {
                    waitElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                var lootField = typeof(TacticalCombatSession).GetField("_collectedLootItemIds", BindingFlags.Instance | BindingFlags.NonPublic);
                var lootList = lootField?.GetValue(ctrl.CombatSession) as List<int>;
                Assert.That(lootList, Is.Not.Null);
                lootList.Clear();
                lootList.AddRange(new[] { 10, 418, 622, 611 });

                CombatantState currentHero = ctrl.CombatSession.CurrentActor;
                Assert.That(currentHero, Is.Not.Null);
                CombatantState finalMob = null;
                for (int i = 0; i < ctrl.CombatSession.Combatants.Count; i++)
                {
                    CombatantState u = ctrl.CombatSession.Combatants[i];
                    if (u.Side == CombatSide.Monster && u.IsAlive)
                    {
                        if (finalMob == null) finalMob = u;
                        else u.Hp = 0;
                    }
                }
                Assert.That(finalMob, Is.Not.Null);
                finalMob.Hp = 1;
                finalMob.Ac = 0;
                int adjX = finalMob.Position.X > 0 ? finalMob.Position.X - 1 : finalMob.Position.X + 1;
                currentHero.Position = new GridCoord(adjX, finalMob.Position.Y);
                currentHero.ToHit = 95;
                currentHero.WeaponDice = "2d6+6";
                CombatActionResult killRes = ctrl.CombatSession.ExecuteMeleeAttack(finalMob.Id);
                Assert.That(killRes.Success, Is.True);
                Assert.That(ctrl.CombatSession.Outcome, Is.EqualTo(CombatOutcome.Victory));

                typeof(BywaterGameController).GetMethod("HandleCombatOutcome", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(ctrl, null);
                yield return null;
                yield return null;

                Assert.That(ctrl.hudController.IsOutcomeOpen, Is.True);
                Assert.That(GameObject.Find("TakeAndEquipBtn"), Is.Null, "Option 2 must omit TakeAndEquipBtn.");
                Assert.That(GameObject.Find("VictoryHero_0/Right/XpTrack"), Is.Not.Null, "VictoryHero_0 XpTrack missing.");
                Assert.That(GameObject.Find("VictoryHero_0/Right/XpTrack/XpGainFill"), Is.Not.Null, "VictoryHero_0 XpGainFill missing.");
                Assert.That(GameObject.Find("LootTile_0"), Is.Not.Null);

                int duringStashTotal = 0;
                for (int i = 0; i < party.Stash.Count; i++) duringStashTotal += party.Stash[i].Quantity;
                Assert.That(duringStashTotal, Is.EqualTo(preStashTotal), "Option 2 loot must remain deferred until clicked.");

                ctrl.hudController?.HideToast();
                yield return null;
                if (captureProofs)
                {
                    CaptureCameraToPng(ctrl.GetComponentInChildren<Camera>() ?? Camera.main, ctrl.hudController,
                        @"c:\Users\karti\LifeTwin\scratch\stage2_proof_05_victory_loot_hud.png", 1600, 900);
                }

                // Deselect first loot card and confirm Take Selected updates count & deposits only remaining items
                UnityEngine.UI.Button continueBtn = GameObject.Find("ContinueBtn")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(continueBtn, Is.Not.Null);
                string beforeLabel = continueBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>()?.text ?? string.Empty;
                int beforeCount = int.Parse(System.Text.RegularExpressions.Regex.Match(beforeLabel, @"\d+").Value);
                Assert.That(beforeCount, Is.GreaterThanOrEqualTo(4));

                UnityEngine.UI.Button tile0Btn = GameObject.Find("LootTile_0")?.GetComponent<UnityEngine.UI.Button>();
                Assert.That(tile0Btn, Is.Not.Null);
                tile0Btn.onClick.Invoke();
                yield return null;

                string afterLabel = continueBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>()?.text ?? string.Empty;
                int afterCount = int.Parse(System.Text.RegularExpressions.Regex.Match(afterLabel, @"\d+").Value);
                Assert.That(afterLabel, Is.EqualTo($"Take Selected ({afterCount})"));
                Assert.That(afterCount, Is.LessThan(beforeCount));
                Assert.That(afterCount, Is.GreaterThan(0));
                continueBtn.onClick.Invoke();
                yield return null;
                yield return null;

                int postStashTotal = 0;
                for (int i = 0; i < party.Stash.Count; i++) postStashTotal += party.Stash[i].Quantity;
                Assert.That(postStashTotal, Is.EqualTo(preStashTotal + afterCount));

                Assert.That(errors, Is.Empty, "PlayMode Stage2 Victory/Mixer errors:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }
        }

        [UnityTest]
        public IEnumerator PlayMode_Stage2_PerformanceBudget_120FramesUnder3Ms()
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
                ctrl.WarpToOverworldTownShowcase();

                // Exclude first 8 warmup frames before measuring 120 live PlayMode frames
                for (int w = 0; w < 8; w++)
                {
                    yield return null;
                }

                const int sampleFrames = 120;
                double totalDeltaSeconds = 0.0;
                for (int f = 0; f < sampleFrames; f++)
                {
                    yield return null;
                    totalDeltaSeconds += Time.unscaledDeltaTime;
                }

                double avgMs = (totalDeltaSeconds / sampleFrames) * 1000.0;
                double avgFps = 1000.0 / Math.Max(0.0001, avgMs);
                Debug.Log($"[Stage2Perf] Measured {sampleFrames} live frames: avg={avgMs:F3} ms/frame ({avgFps:F1} FPS)");

                Assert.That(avgMs, Is.LessThan(3.0),
                    $"Stage 2 PlayMode performance budget exceeded: {avgMs:F3} ms/frame ({avgFps:F1} FPS) >= 3.0 ms.");
                Assert.That(errors, Is.Empty, "PlayMode Stage2 Performance errors:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }
        }
    }
}
