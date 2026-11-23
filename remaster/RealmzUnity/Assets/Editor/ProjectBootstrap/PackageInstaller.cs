using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace ProjectBootstrap
{
    /// <summary>
    /// Fallback UPM package installer using Client.AddAndRemove.
    /// Launch Editor in -batchmode WITHOUT -quit:
    /// -executeMethod ProjectBootstrap.PackageInstaller.Install
    /// </summary>
    public static class PackageInstaller
    {
        private static readonly string[] PackagesToAdd =
        {
            "com.unity.cinemachine",
            "com.unity.textmeshpro",
            "com.unity.ugui"
        };

        private static readonly string[] PackagesToRemove = { };

        private const double TimeoutSeconds = 600;

        private static AddAndRemoveRequest _request;
        private static double _deadline;

        public static void Install()
        {
            if (PackagesToAdd.Length == 0 && PackagesToRemove.Length == 0)
            {
                Debug.Log("[PackageInstaller] Nothing to do.");
                EditorApplication.Exit(0);
                return;
            }

            Debug.Log("[PackageInstaller] Adding: " + string.Join(", ", PackagesToAdd));
            _request = Client.AddAndRemove(packagesToAdd: PackagesToAdd, packagesToRemove: PackagesToRemove);
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (_request == null)
            {
                return;
            }

            if (!_request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    EditorApplication.update -= Poll;
                    Debug.LogError("[PackageInstaller] Timed out waiting for UPM.");
                    EditorApplication.Exit(2);
                }
                return;
            }

            EditorApplication.update -= Poll;

            if (_request.Status == StatusCode.Success)
            {
                var names = _request.Result.Select(p => $"{p.name}@{p.version}");
                Debug.Log("[PackageInstaller] Resolved: " + string.Join(", ", names));
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("[PackageInstaller] Failed: " + (_request.Error != null ? _request.Error.message : "Unknown error"));
                EditorApplication.Exit(1);
            }
        }
    }
}
