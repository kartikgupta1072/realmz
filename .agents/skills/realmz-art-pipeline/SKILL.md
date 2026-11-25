---
name: realmz-art-pipeline
description: >-
  Generate, post-process, QA, and import 64 px/m Cartography or realmz-foundry
  assets safely via .scratch/ staging. Use when creating or repairing sprites,
  normal maps, tiles, props, or portraits.
---

# Realmz 64 px/m Art & Asset Foundry Pipeline

All Realmz art must maintain strict `64 px/m` texel density, `Sunlit-128` color harmony, and `+Y` OpenGL tangent-space normals.

## Workflow

1. **Clear GPU VRAM & Stage in `.scratch/` Only:**
   - Before running ComfyUI, Blender, or Hunyuan3D on the 8 GB RTX 5060 Ti, evict any resident Ollama models:
     ```powershell
     powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 local-ai evict
     ```
   - Put all raw renders, contact sheets, and QA manifests in `C:\Users\karti\realmz\.scratch\<pack_name>\`.
   - Never write temporary packs or outputs into `C:\Users\karti\LifeTwin`.

2. **Enforce Cartography Technical Specs:**
   - Palette: `C:\Users\karti\realmz\remaster\art-pipeline\palette_soc_sunlit_128.json`.
   - Scale: `64 PPU` (`128x128` terrain tiles, `512x128` for `4f` strips = `4 x 128x128`, exact pixel multiples for props/buildings).
   - Normal maps: Paired `*_n.png` in OpenGL `+Y` orientation.
   - QA checks: Verify zero interior alpha holes (especially dark roofs/foliage), zero magenta halo bleed, and clean contact sheets before promoting. Run local sprite screening with:
     ```powershell
     powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 local-ai vision-qa C:\Users\karti\realmz\.scratch\<pack_name> --mode sprite --json
     ```

3. **Run Asset Foundry Tests (if modifying foundry code):**
   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 test-python
   ```
   If `remaster/asset-foundry/bin/realmz-foundry.py` changed, mirror it to `C:\Users\karti\.local\bin\realmz-foundry.py` and verify parity with `dev.ps1 check`.

4. **Promote & Verify Unity Import Settings:**
   - Copy verified `*.png` + `*_n.png` pairs to `C:\Users\karti\realmz\remaster\RealmzUnity\Assets\Art\Cartography\<Subfolder>\`.
   - Confirm `CartographyAssetImporter` applies `Point` filter, `Uncompressed`, `MipMap OFF`, `64 PPU`, `NormalMap + sRGB OFF` on `*_n.png`, `Center (0.5, 0.5)` pivot on `decal_*.png` and `dungeon_floor_grate_rusted.png`, and `Multiple` slicing on `*_4f.png`.

5. **Run Editor & Visual Gates:**
   - Run `CartographyEditorTests`:
     ```powershell
     powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 unity-tests -Mode EditMode -Filter "CartographyEditorTests"
     ```
   - Complete `realmz-visual-qa` on freshly captured proofs.
