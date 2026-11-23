using System;
using UnityEngine;

namespace Realmz.Foundry
{
    [Serializable]
    public sealed class FoundryBoundsData
    {
        public float x;
        public float y;
        public float z;
        public float width;
        public float height;
        public float depth;

        public Vector3 ToVector3()
        {
            float sx = x > 0.0001f ? x : width;
            float sy = y > 0.0001f ? y : height;
            float sz = z > 0.0001f ? z : depth;
            return new Vector3(sx, sy, sz);
        }
    }

    [Serializable]
    public sealed class FoundryTerrainMapsData
    {
        public string albedo = string.Empty;
        public string normal = string.Empty;
        public string height = string.Empty;
        public string roughness = string.Empty;
        public string ao = string.Empty;
        public string mask = string.Empty;
    }

    [Serializable]
    public sealed class FoundryAssetManifestData
    {
        public string schema_version = "1.0";
        public string id = string.Empty;
        public string name = string.Empty;
        public string category = "Prop";
        public string asset_kind = string.Empty;
        public int resolution;
        public string style = string.Empty;
        public float tile_scale_m = 1.0f;
        public FoundryTerrainMapsData maps = new FoundryTerrainMapsData();
        public string rig_profile = "Static";
        public bool rigged;
        public string rig_source = "none";
        public string[] bones = Array.Empty<string>();
        public string[] animations = Array.Empty<string>();
        public FoundryBoundsData bounds_m = new FoundryBoundsData();
        public string pivot = "bottom_center";
        public string forward_axis = "+Z";
        public int triangle_count;
        public float target_scale_m = 1.0f;
        public string preferred_mode = "Both";
        public int hd2d_frame_size = 128;
        public int hd2d_frame_count = 1;
        public int hd2d_ppu = 128;
        public string source_image = string.Empty;
        public float peak_vram_mib;
        public string timestamp = string.Empty;
    }

    [Serializable]
    public sealed class FoundryImportReceipt
    {
        public string schema_version = "1.0";
        public string id = string.Empty;
        public string name = string.Empty;
        public string category = string.Empty;
        public string asset_kind = string.Empty;
        public string rig_profile = string.Empty;
        public string preferred_mode = string.Empty;
        public string prefab_3d_path = string.Empty;
        public string prefab_hd2d_path = string.Empty;
        public string material_3d_path = string.Empty;
        public string material_hd2d_path = string.Empty;
        public string terrain_material_path = string.Empty;
        public string[] texture_paths = Array.Empty<string>();
        public string animator_controller_path = string.Empty;
        public string shader_name = "Realmz/Stylized3D";
        public int mesh_count;
        public int triangle_count;
        public int bone_count;
        public string[] animation_clips = Array.Empty<string>();
        public int hd2d_sprite_count;
        public string imported_timestamp = string.Empty;
        public bool success;
        public string notes = string.Empty;
    }
}
