# Realmz HD-2D: Songs-of-Conquest ComfyUI Pixel-Art + Normal-Map Pipeline

Reusable `8 GB` VRAM (RTX 5060 Ti) asset pipeline for converting Realmz `32x32` `cicn`/`Tacticals.rsrc` sprites (or text prompts) into crisp `1x` palette-locked dark-fantasy sprites (`transparent PNG`) and matching `1x` OpenGL tangent-space normal maps (`*_n.png`) ready for Unity 6 URP 3D dynamic torchlight.

---

## 1. Installed Models & Tools (0 GB Duplicated via NTFS Hardlinks)

| Component | Canonical Path (`C:\ComfyUI`) | Hardlinked Comfy-Desktop Shared Path | Purpose |
| :--- | :--- | :--- | :--- |
| **Base Checkpoint** | `C:\ComfyUI\models\checkpoints\Illustrious-XL-v2.0.safetensors` | `...\ComfyUI-Shared\models\checkpoints\Illustrious-XL-v2.0.safetensors` | Primary dark-fantasy SDXL-lineage workhorse (FP16) |
| **Illustrious Pixel LoRA** | `C:\ComfyUI\models\loras\pixel-art-style-illustrious-by-skormino.safetensors` | `...\ComfyUI-Shared\models\loras\pixel-art-style-illustrious-by-skormino.safetensors` | Native Illustrious-XL pixel-art LoRA (`pixpix, pixel_art`) |
| **SDXL Pixel LoRA** | `C:\ComfyUI\models\loras\pixel-art-xl-nerijs.safetensors` | `...\ComfyUI-Shared\models\loras\pixel-art-xl-nerijs.safetensors` | General SDXL pixel-art LoRA for tiles & props |
| **ControlNet Union SDXL** | `C:\ComfyUI\models\controlnet\xinsir-controlnet-union-sdxl-1.0-promax.safetensors` | `...\ComfyUI-Shared\models\controlnet\xinsir-controlnet-union-sdxl-1.0-promax.safetensors` | Silhouette lock (Canny / Depth / Tile) onto Realmz `32x32` sprites |
| **Portable Laigter** | `C:\ComfyUI\tools\laigter\laigter-windows\laigter.exe` (`v1.14.0`) | - | Optional headless pixel-art normal generator (`--normal-backend laigter`) |

- **`.venv` Safety:** Zero PyTorch/CUDA/pip modifications made to `C:\ComfyUI\.venv` (`torch 2.12.1+cu130` Blackwell `sm_120` preserved untouched). All post-processing runs deterministically via existing `PIL`, `numpy`, `scipy`, and `cv2`.

---

## 2. Pipeline Files

- [`comfy_workflow_sprite_api.json`](comfy_workflow_sprite_api.json) - Text-to-sprite ComfyUI API graph (`Illustrious-XL-v2.0` + `pixel-art-style-illustrious-by-skormino`, `1024x1024`, solid `#FF00FF` chroma backdrop).
- [`comfy_workflow_silhouette_cn_api.json`](comfy_workflow_silhouette_cn_api.json) - Realmz `32x32` silhouette-lock ComfyUI API graph using `xinsir-controlnet-union-sdxl-1.0-promax.safetensors` (`SetUnionControlNetType=canny` + `ControlNetApplyAdvanced` + img2img `denoise=0.62`).
- [`palette_songs_of_conquest_64.json`](palette_songs_of_conquest_64.json) - Locked 64-color dark-fantasy master palette (deep indigo shadow ramp, weathered steel, warm amber torchlight/brass, crimson banner, leather, mossy forest, bone/skin, arcane teal, royal plum, deep ink & highlight anchors).
- [`postprocess_sprite.py`](postprocess_sprite.py) - Deterministic 4-stage post-processor:
  1. Halo-free 1-bit alpha background removal (border CIELAB flood fill + interior chroma cutout + rim despill).
  2. Tight bottom-center foot-pivot framing + outline-preserving crisp `1x` pixel-grid snap (`32x32`, `64x64`, `96x96`, or `128x128`; zero mixels).
  3. Subtle warm-torch / cool-indigo contrast grade + locked 64-color CIELAB quantization (optional Bayer 2x2 dither).
  4. Crisp `1x` OpenGL `+Y up` tangent-space normal map (`*_n.png`, `#8080FF` neutral base, outline-protected relief + smooth alpha-distance bevels, unit-normalized vectors, transparent alpha masked to `(128, 128, 255, 0)`).
- [`run_pipeline.py`](run_pipeline.py) - ComfyUI `:8188` API driver with peak VRAM monitor, post-processing, and invariant validation JSON report.

---

## 3. Usage

### Start ComfyUI (8 GB VRAM Safe Flags)
```powershell
C:\ComfyUI\.venv\Scripts\python.exe C:\ComfyUI\main.py --listen 127.0.0.1 --port 8188 --disable-auto-launch --reserve-vram 0.6
```

### Generate 1x Sprite + Normal Map End-to-End
```powershell
C:\ComfyUI\.venv\Scripts\python.exe run_pipeline.py --name knight_hero --size 64 --seed 1994064 --out-dir samples
```

### Post-Process Any Raw Render Offline (Zero VRAM)
```powershell
C:\ComfyUI\.venv\Scripts\python.exe postprocess_sprite.py samples\knight_hero_raw_1024.png -o samples\knight_hero_64x64.png --normal samples\knight_hero_64x64_n.png --preview samples\knight_hero_qa_8x.png --size 64 --normal-backend auto
```

---

## 4. Unity 6 URP 3D Import Settings

- **Albedo (`*_64x64.png`):** Texture Type `Sprite (2D and UI)`, Pixels Per Unit `32` (or `64` for hero units), Filter Mode `Point (no filter)`, Compression `None`, Generate Mip Maps `OFF`, Pivot `Bottom` (`0.5, 0.0`).
- **Normal Map (`*_64x64_n.png`):** Texture Type `Normal map`, Create from Grayscale `OFF`, Filter Mode `Point (no filter)`, Compression `None`, Generate Mip Maps `OFF`, sRGB `OFF`.

---

## 5. Verified Live 8 GB VRAM Smoke-Test Benchmark (`2026-11-23`)

- **GPU:** NVIDIA GeForce RTX 5060 Ti (`8,151 MiB` VRAM, `--reserve-vram 0.6`, ComfyUI `0.37.0`)
- **Peak VRAM:** `7,363 MiB` (`< 8,151 MiB`, zero OOM; returned to `636 MiB` idle after stopping `:8188`)
- **Timing:** `17.49s` ComfyUI cold-load + 28-step `1024x1024` generation + `2.11s` deterministic post-process = `19.60s` total
- **Sample Outputs (`samples/`):**
  - `knight_hero_64x64.png` - `64x64` transparent `1x` knight (`1-bit` alpha `[0, 255]`, `31/64` master palette colors, `0` off-palette, zero mixels)
  - `knight_hero_64x64_n.png` - `64x64` OpenGL `+Y up` normal map (`#8080FF` base, mean RGB `[128.0, 127.2, 250.3]`, unit magnitude `1.0002`, `min_z 0.733`)
  - `knight_hero_qa_8x.png` - `8x` nearest-neighbor QA strip (albedo + normal + simulated warm torch / cool indigo relighting)
  - `knight_hero_report.json` - Machine-readable telemetry & validation stats

