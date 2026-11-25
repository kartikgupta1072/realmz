# Realmz Asset Foundry V1 (Local 8 GB Pipeline + Unity Bridge)

Realmz Asset Foundry V1 is a 100% local, zero-cloud-token headless CLI pipeline on Home-PC (`RTX 5060 Ti 8,151 MiB`) that turns any reference image (Pass A sprites, concept art, or uploaded photos such as a fish) into both:
1. A canonical, textured, normalized (`Y = 0` bottom-center, `+Z` forward, real-world Unity meters), low-poly decimated, auto-rigged `.glb` with embedded `Idle` / `Move` / `Attack` NLA skeletal animations.
2. A deterministic HD-2D `128x128` (or `256x256` for buildings) sprite sheet + matching OpenGL `+Y` tangent-space normal map snapped to the *Songs of Conquest* 128-color sunlit palette (`palette_soc_sunlit_128.json`).

## Architecture & Windows / 8 GB GPU Design
- **Isolated Environment (`C:\Users\karti\realmz\tools\asset-foundry\.venv`)**: Python 3.12 + `torch==2.10.0+cu130` (`sm_120` Blackwell native) + Kijai `custom_rasterizer-0.1.0+torch2100.cuda130-cp312-cp312-win_amd64.whl`. `C:\ComfyUI\.venv` (`torch 2.12.1+cu130`) is never modified.
- **Sequential GPU Execution**:
  - Stage 1 (CPU): `preprocess.py` isolates foreground via `rembg` (CPU ONNX) or existing sprite alpha.
  - Stage 2a (Subprocess GPU): `Hunyuan3D-2mini-Turbo` (`0.6B` DiT + `HierarchicalVolumeDecoding` ShapeVAE fp16, 5 turbo steps, `output_type="trimesh"`) generates shape mesh and cleans floaters/decimates, then exits so OS CUDA frees 100% VRAM.
  - Stage 2b (Subprocess GPU): `Hunyuan3D-Delight-v2-0` (skipped automatically for pixel sprites via `skip_delight_for_pixel`) -> explicit unload -> `Hunyuan3D-Paint-v2-0-Turbo` (CPU-first load + `enable_model_cpu_offload()`, `enable_attention_slicing()`, `enable_vae_slicing()`, `1024x1024` UV bake via `custom_rasterizer`) paints multi-view textures, then exits cleanly (`< 1 GB` idle VRAM). Required local patches in `C:\Users\karti\realmz\tools\repos\Hunyuan3D-2`: `volume_decoders.py:81` `padded[tuple(slice_dims)]`, `multiview_utils.py:34` `trust_remote_code=True`, and `hunyuanpaint/unet/modules.py` `.safetensors`-first loading with `strict=False` + missing-key guard.
- **Auto-Rigging (`UniRig` Windows Audit + Blender 4.5.9 Hybrid)**:
  - **Honest Windows Compatibility Note**: On Windows Python 3.12 + `torch 2.10+cu130` (`RTX 5060 Ti sm_120`) without local MSVC `cl.exe` or CUDA Toolkit, `UniRig` Skinning (`unirig_skin` / `PTv3Object`) is blocked by compiled extensions (`spconv`, `torch_scatter`, `flash_attn`).
  - `UniRig` Skeleton AR (`unirig_ar_350m` + `michelangelo_encoder`) runs using pure-PyTorch `torch_cluster.fps` and PyTorch native `sdpa` attention shims, paired cleanly with headless **Blender 4.5.9 Portable** (`ARMATURE_AUTO` bone heat + inverse-distance Gaussian fallback skinning + template armatures for `Humanoid`, `Quadruped`, `Aquatic`, and `Static` bypass) and looping `Idle` / `Move` / `Attack` clip baking.
- **HD-2D Baking & QA**: `blender_rig_bake.py --mode render_views` renders 26-deg orthographic albedo, exact camera/view-space OpenGL `+Y` normal passes, and a 2x2 turntable QA contact sheet (`<id>_qa_preview.png`). `hd2d_baker.py` quantizes and snaps to crisp 1-bit alpha sheets.
- **Unity Bridge (`C:\Users\karti\realmz\remaster\RealmzUnity`)**:
  - `com.unity.cloud.gltfast` `6.2.0` imports `.glb` meshes, materials, skeletons, and animations natively.
  - `RealmzAssetInboxPostprocessor.cs` watches `Assets/Inbox/<id>/`, builds `Assets/Prefabs/Foundry/<id>_3D.prefab` (using `Assets/Shaders/RealmzStylized3D.shader`, `AnimatorController`, `FoundryAssetMetadata`, and `BywaterFoundryUnit`) and `<id>_HD2D.prefab` (`PixelBillboard` + normal map), and writes `<id>.import_receipt.json`.

## CLI Usage (`realmz-foundry`)
```powershell
realmz-foundry status --json
realmz-foundry convert C:\Users\karti\realmz\remaster\RealmzUnity\Assets\Art\M1\Heroes\knight.png --name knight --category Hero --rig Humanoid --scale 1.8 --mode Both --ship --json
realmz-foundry batch batch_manifest.json --ship --json
realmz-foundry ship knight --notify --json
```
