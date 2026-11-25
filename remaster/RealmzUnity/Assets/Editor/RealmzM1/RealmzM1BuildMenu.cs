using System;
using System.IO;
using Realmz.Game;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Realmz.EditorTools
{
    /// <summary>
    /// Build and live verification helper menu items for City of Bywater Milestone 1 Stage 1B.
    /// </summary>
    public static class RealmzM1BuildMenu
    {
        public const string DefaultWindowsBuildExe = @"C:\Users\karti\realmz\remaster\builds\Realmz_M1\Realmz.exe";

        [MenuItem("Realmz/M1/Build Windows Standalone (x64)")]
        public static string BuildWindowsStandalone()
        {
            // Ship the committed scene that the PlayMode suite tests. Regenerating it here would
            // reshuffle fileIDs and could ship a scene that differs from the tested one.
            // After changing M1BywaterSceneBuilder, run Realmz/M1/Build City of Bywater Scene and commit the scene.
            if (!File.Exists(Path.GetFullPath(M1BywaterSceneBuilder.ScenePath)))
            {
                M1BywaterSceneBuilder.BuildBywaterScene();
            }

            string outExe = Path.GetFullPath(DefaultWindowsBuildExe);
            string outDir = Path.GetDirectoryName(outExe);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { M1BywaterSceneBuilder.ScenePath },
                locationPathName = outExe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception(
                    $"Windows Standalone build failed: {summary.result} (errors={summary.totalErrors}, warnings={summary.totalWarnings})");
            }

            Debug.Log(
                $"[RealmzM1BuildMenu] Windows build succeeded: {outExe} " +
                $"({summary.totalSize / (1024f * 1024f):F2} MB, warnings={summary.totalWarnings})");
            return outExe;
        }

        public static string StageExploreShowcase()
        {
            BywaterGameController ctrl = UnityEngine.Object.FindFirstObjectByType<BywaterGameController>();
            if (ctrl == null)
            {
                throw new Exception("BywaterGameController not found in active PlayMode scene.");
            }

            ctrl.WarpToOverworldTownShowcase();
            return $"Explore showcase ready at {ctrl.ExploreSession.Position} on {ctrl.ExploreSession.CurrentMap.MapId}";
        }

        public static string StageCombatShowcase()
        {
            BywaterGameController ctrl = UnityEngine.Object.FindFirstObjectByType<BywaterGameController>();
            if (ctrl == null)
            {
                throw new Exception("BywaterGameController not found in active PlayMode scene.");
            }

            ctrl.StageCombatOverlayShowcaseForCapture();
            string actorName = ctrl.CombatSession?.CurrentActor?.Name ?? "Unknown";
            int turn = ctrl.CombatSession?.TurnNumber ?? 0;
            int round = ctrl.CombatSession?.RoundNumber ?? 0;
            int logCount = ctrl.CombatSession?.Log.Count ?? 0;
            int count = ctrl.CombatSession?.Combatants.Count ?? 0;
            return $"Combat showcase ready: actor={actorName}, turn={turn}, round={round}, logs={logCount}, combatants={count}";
        }

        public static string StageDungeonShowcase()
        {
            BywaterGameController ctrl = UnityEngine.Object.FindFirstObjectByType<BywaterGameController>();
            if (ctrl == null)
            {
                throw new Exception("BywaterGameController not found in active PlayMode scene.");
            }

            ctrl.WarpToDungeonShowcase();
            return $"Dungeon showcase ready at {ctrl.ExploreSession.Position} on {ctrl.ExploreSession.CurrentMap.MapId}";
        }

        public static string StageShopShowcase(string shopId = "1")
        {
            BywaterGameController ctrl = UnityEngine.Object.FindFirstObjectByType<BywaterGameController>();
            if (ctrl == null)
            {
                throw new Exception("BywaterGameController not found in active PlayMode scene.");
            }

            ctrl.StageShopShowcase(shopId);
            return $"Shop showcase ready for shop #{shopId} at {ctrl.ExploreSession.Position}";
        }

        public static string StagePauseShowcase()
        {
            BywaterGameController ctrl = UnityEngine.Object.FindFirstObjectByType<BywaterGameController>();
            if (ctrl == null)
            {
                throw new Exception("BywaterGameController not found in active PlayMode scene.");
            }

            ctrl.StagePauseMenuShowcase();
            return "Pause menu showcase ready.";
        }
    }
}
