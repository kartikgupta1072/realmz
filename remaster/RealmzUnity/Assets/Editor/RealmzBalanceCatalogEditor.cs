using System.IO;
using Realmz.Data;
using UnityEditor;
using UnityEngine;

namespace Realmz.EditorTools
{
    /// <summary>
    /// Custom inspector and menu sync tools for RealmzBalanceCatalog, bridging the
    /// Resources ScriptableObject with StreamingAssets/bywater_m1/balance_overrides.json.
    /// </summary>
    [CustomEditor(typeof(RealmzBalanceCatalog))]
    public sealed class RealmzBalanceCatalogEditor : Editor
    {
        public const string AssetPath = "Assets/Resources/RealmzBalanceCatalog.asset";
        public const string JsonAssetPath = "Assets/StreamingAssets/bywater_m1/balance_overrides.json";

        public override void OnInspectorGUI()
        {
            RealmzBalanceCatalog catalog = (RealmzBalanceCatalog)target;

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Pull from JSON", GUILayout.Height(26f)))
            {
                PullFromJson(catalog);
            }

            if (GUILayout.Button("Apply & Export JSON", GUILayout.Height(26f)))
            {
                ExportToJson(catalog);
            }

            if (GUILayout.Button("Reset To Defaults", GUILayout.Height(26f)))
            {
                Undo.RecordObject(catalog, "Reset RealmzBalanceCatalog To Defaults");
                catalog.ResetToDefaults();
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6f);

            base.OnInspectorGUI();
        }

        public static void BatchSyncBalanceAndArtCatalogs()
        {
            M1AssetImporter.ImportAndBuildCatalog();
            CreateOrSyncAsset();
        }

        [MenuItem("Realmz/Balance/Create or Sync RealmzBalanceCatalog")]
        public static RealmzBalanceCatalog CreateOrSyncAsset()
        {
            EnsureFolder("Assets/Resources");

            RealmzBalanceCatalog catalog = AssetDatabase.LoadAssetAtPath<RealmzBalanceCatalog>(AssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<RealmzBalanceCatalog>();
                AssetDatabase.CreateAsset(catalog, AssetPath);
            }

            string fullJsonPath = GetFullJsonPath();
            if (File.Exists(fullJsonPath))
            {
                string json = File.ReadAllText(fullJsonPath);
                if (!catalog.LoadFromJson(json))
                {
                    Debug.LogWarning("[RealmzBalanceCatalogEditor] Failed to parse JSON at " + JsonAssetPath + "; keeping existing catalog values.");
                }
            }
            else
            {
                Debug.Log("[RealmzBalanceCatalogEditor] No JSON found at " + JsonAssetPath + "; initialized defaults.");
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = catalog;
            return catalog;
        }

        private static void PullFromJson(RealmzBalanceCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            string fullJsonPath = GetFullJsonPath();
            if (!File.Exists(fullJsonPath))
            {
                Debug.LogWarning("[RealmzBalanceCatalogEditor] Balance JSON not found at " + fullJsonPath);
                return;
            }

            string json = File.ReadAllText(fullJsonPath);
            Undo.RecordObject(catalog, "Pull RealmzBalanceCatalog from JSON");
            if (catalog.LoadFromJson(json))
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                Debug.Log("[RealmzBalanceCatalogEditor] Pulled balance overrides from " + JsonAssetPath);
            }
        }

        private static void ExportToJson(RealmzBalanceCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            string fullJsonPath = GetFullJsonPath();
            string directory = Path.GetDirectoryName(fullJsonPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = catalog.ToJson(indented: true);
            File.WriteAllText(fullJsonPath, json + "\n");
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[RealmzBalanceCatalogEditor] Exported balance overrides to " + JsonAssetPath);
        }

        private static string GetFullJsonPath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, JsonAssetPath));
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
