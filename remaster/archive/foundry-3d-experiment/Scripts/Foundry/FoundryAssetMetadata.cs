using System;
using UnityEngine;

namespace Realmz.Foundry
{
    /// <summary>
    /// Runtime metadata attached to generated Realmz Asset Foundry 3D and HD-2D prefabs.
    /// </summary>
    public sealed class FoundryAssetMetadata : MonoBehaviour
    {
        public string assetId = string.Empty;
        public string category = "Prop";
        public string rigProfile = "Static";
        public string preferredMode = "Both";
        public float targetScaleMeters = 1.0f;
        public int triangleCount;
        public int boneCount;
        public string[] animations = Array.Empty<string>();

        public void ApplyManifest(
            FoundryAssetManifestData manifest,
            int resolvedTriangleCount,
            int resolvedBoneCount,
            string[] resolvedAnimations)
        {
            if (manifest != null)
            {
                assetId = manifest.id ?? string.Empty;
                category = string.IsNullOrEmpty(manifest.category) ? "Prop" : manifest.category;
                rigProfile = string.IsNullOrEmpty(manifest.rig_profile) ? "Static" : manifest.rig_profile;
                preferredMode = string.IsNullOrEmpty(manifest.preferred_mode) ? "Both" : manifest.preferred_mode;
                targetScaleMeters = manifest.target_scale_m > 0f ? manifest.target_scale_m : 1.0f;
                triangleCount = resolvedTriangleCount > 0 ? resolvedTriangleCount : manifest.triangle_count;
                int manifestBones = manifest.bones != null ? manifest.bones.Length : 0;
                boneCount = resolvedBoneCount > 0 ? resolvedBoneCount : manifestBones;
                animations = resolvedAnimations != null && resolvedAnimations.Length > 0
                    ? resolvedAnimations
                    : (manifest.animations ?? Array.Empty<string>());
            }
            else
            {
                triangleCount = resolvedTriangleCount;
                boneCount = resolvedBoneCount;
                animations = resolvedAnimations ?? Array.Empty<string>();
            }
        }
    }
}
