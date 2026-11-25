# Realmz Remaster: Engine + MCP Winner & Songs-of-Conquest ComfyUI Pixel Pipeline

**Task:** `T-289` (`W-212`) | **Date:** `2026-11-23` | **Mode:** Strictly Read-Only Research & Architecture Blueprint
**Base Context:** Extends [`scratch/realmz_remaster_plan.md`](file:///c:/Users/karti/LifeTwin/scratch/realmz_remaster_plan.md)

---

## Locked-In Creative & Scope Decisions (`#direct #time:2026-11-23 07:28 EST`)

1. **Art Direction:** Pixel art inspired by **Songs of Conquest** - crisp pixel-grid sprites and tilesets placed inside dynamic 3D lighting, soft shadows, warm torchlight, and subtle bloom.
2. **Asset Pipeline Hardware:** Home-PC **RTX 5060 Ti (`8,151 MiB` VRAM)** + **32 GB RAM** + `1,512.08 GB` free on `C:`, using `C:\ComfyUI` (already containing `Illustrious-XL-v2.0` and `Z-Image-Turbo`).
3. **Scope & Gameplay:** Start with a **small starter scenario vertical slice** paired with **modern tactical balance and QoL upgrades** while keeping 1994 Realmz's RPG soul intact.
4. **Release Posture:** **Personal passion build** - directly parse and remaster `kartikgupta1072/realmz`'s bundled 13 scenarios, `.rsrc` forks, `cicn` icons, portraits, tacticals, and `MOD`/`MADG` music via `realmz_dasm`.

---

## 1. Local Home-PC Tooling, Plugins & Repo Audit (Read-Only)

### 1.1 Verified Local Environment (`Home-PC`)

| Component | Observed State | Notes |
| :--- | :--- | :--- |
| **Storage (`C:`)** | **`1,512.08 GB` free** (`312.52 GB` used) | Ample room for Unity 6 LTS (~40-50 GB unpack headroom) + ComfyUI LoRAs/ControlNets. |
| **Unity Hub / Editor / CLI** | **Not installed** | `C:\Program Files\Unity*`, `%LOCALAPPDATA%\Unity*`, `where.exe unity*`, and `winget` returned none. |
| **Godot 4** | **Not installed** | `where.exe godot*`, `C:\Program Files\Godot*`, and `winget` returned none. |
| **.NET** | **Runtime only (`9.0.7`) - NO SDK** | `C:\Program Files\dotnet\dotnet.exe` (`Microsoft.NETCore.App 9.0.7`, `WindowsDesktop.App 9.0.7`); `dotnet --list-sdks` returned empty. |
| **Native C++ Build Tools** | **Not installed** | `cmake`, `msbuild`, and Visual Studio are absent (`python 3.12.10`/`3.13`/`3.11`, `node v22.23.2`, `git 2.54.0`, `gh 2.104.0` are installed). |
| **Antigravity Plugins** | **Official `unity` plugin (`v0.1.9-beta`) ALREADY installed** | `C:\Users\karti\.gemini\config\plugins\unity\` (`Unity-Technologies/unity-agent-plugin`) contains **31 skills** (`unity-cli`, `new-unity-project`, `unity-package-management`, `urp-postprocessing`, `2d-pixel-perfect`, `craft-game-floors`, `shader-graph-create-custom-node`, `create-tile-palette-or-rule-tiles`, `ui`, `ui-uitk`, `ui-ugui`, `optimize-audio`, etc.). Marketplace search confirms **0** Godot or ComfyUI plugins. |
| **Antigravity MCP Config** | `whatsapp`, `chrome-devtools`, `dropbox` only | `C:\Users\karti\.gemini\config\mcp_config.json` has no Unity or Godot MCP server configured yet. |
| **`C:\ComfyUI`** | **Idle** (`:8188` down), `Python 3.12.10` `.venv` | `run_krosmoz.bat` launches `--reserve-vram 0.6`. Existing models: `Illustrious-XL-v2.0.safetensors` (`6,616.6 MB`), `z-image-turbo_fp8_scaled_e4m3fn_KJ.safetensors` (`5,872.8 MB`), `qwen_3_4b_fp4_mixed.safetensors` (`3,318.2 MB`), `umt5_xxl_fp8_e4m3fn_scaled.safetensors` (`6,423.9 MB`), `Wan2.2-TI2V-5B-Q5_K_M.gguf` (`3,634.1 MB`), `ae.safetensors`, `sdxl.vae.safetensors`, `wan2.2_vae.safetensors`. Empty: `loras/`, `controlnet/`, `clip/`. Nodes: `ComfyUI-F5-TTS`, `ComfyUI-GGUF`, `comfyui-manager`, `comfyui_controlnet_aux`. |

### 1.2 GitHub Repo Audit (`kartikgupta1072/realmz`, `resource_dasm`, `Realmz-Remake`)

- **`kartikgupta1072/realmz`** (`main`, C90 + C++23 SDL3 port `v8.1.0`, `944` tree entries, `base/` = `658` files / `50.11 MB`):
  - Houses original `src/realmz_orig/` (166 C files), `base/Realmz/Data Files/` (`The Family Jewels.rsrc` `7,297.1 KB`, `Tacticals.rsrc` `363.6 KB`, `Portraits.rsrc` `350.6 KB`, `Data ID`, `Data S`, `Data Race`, `Data Caste`, 7 base `* BD` tileset files), `base/Realmz/Character Files/` (6 Level-1 pre-made heroes: `Kevlar`, `Lothlorian`, `Silver Leaf`, `Traskelion`, `Trevor`, `Vormale`), `base/Realmz/Realmz Music/` (11 tracks), and `base/Realmz/Scenarios/` (17 folders).
  - **Audio format split verified via magic bytes:** `Battle Music`, `Temple Music`, `Shop Music`, and scenario `Custom 1 Music` files are ProTracker/FastTracker MODs (`M.K.`, `6CHN`, `8CHN`), whereas `Outdoor Music` is PlayerPRO `MADG` (`@0='MADG'`).
- **Where `realmz_dasm` lives:** Inside **`fuzziqersoftware/resource_dasm`** (`src/realmz_dasm.cc`, `RealmzGlobalData.{cc,hh}`, `RealmzScenarioData.{cc,hh}`, `RealmzSaveData.{cc,hh}`), NOT inside `kartikgupta1072/realmz`. It disassembles scenarios to `script.txt`, `land_*.json`/`dungeon_*.json`, and `media/` (`PICT_*.png`, `icon_*.png`, `snd_*.wav`, `text_*.rtf/txt`).
- **`Realmz-Castle/Realmz-Remake`** (`dev`, Godot `4.7` GDScript, last commit `2026-07-25`, `v0.1.107`): Useful reference for `City of Bywater` `map_0` Tiled port (`95 LAP + 168 XAP` in `map_scripts.gd`, 186 KB) and `godot-openmpt` tracker notes, **but incomplete**: `map_1..8` and `mapd_0..1` are empty 98-byte stubs, the other 12 scenarios are absent, PR #86 (`realmz_dump_importer`) is unmerged, and open bugs remain on untyped `Dictionary` items (`#65`), spell migration (`#64/#67/#77`), and combat state paths (`#80/#84/#85`).

### 1.3 All 13 Scenarios + Sandbox Folders in `kartikgupta1072/realmz`

*(Map counts derived from `Data LD / 16200` and `Data DL / 16200`; `rec_lvl` = scenario header `recommended_starting_levels`, where `6` = 6 Level-1 characters.)*

| Scenario / Folder | Files | Size (KB) | Maps (Land / Dung) | `Data DD` (B) | `Data EDCD` (B) | `rec_lvl` | Start `(L,X,Y)` | Special / Custom Files |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **Tutorial** *(Non-campaign)* | 36 | 517.9 | 2L / 1D | 8,000 | 1,020 | 6 | - | Custom `Data Race`, ultra-minimal testbed |
| **Prelude to Pestilence** | 32 | **894.6** | **5L / 1D** | **20,000** | **6,160** | 30 | `(1,9,62)` | Smallest full scenario across maps & scripts |
| **City of Bywater** | 32 | **988.1** | 9L / 2D | 36,000 | 52,820 | **6** | `(0,2,1)` | Level-1 starter; `Data Custom 1 BD`; `Scenario.rsrc` 113.7 KB |
| **Assault on Giant Mountain** | 33 | 1,145.7 | 7L / 2D | 28,000 | 23,920 | 36 | `(0,48,15)` | Medium scripts (`Data SD2.rsrc` present) |
| **Twin Sands of Time** | 36 | 1,238.6 | 8L / 2D | 32,000 | 49,030 | 18 | `(1,52,34)` | Low-mid party; `author=(none)` |
| **Castle in the Clouds** | 32 | 1,390.8 | 8L / 5D | 32,000 | 259,710 | 42 | `(0,61,66)` | Heavy complex dialogs (`EDCD` 260 KB) |
| **White Dragon** | 32 | 1,437.0 | 12L / 4D | 48,000 | 10,170 | 125 | `(0,43,74)` | High-level outdoor/dungeon crawl |
| **Destroy the Necronomicon** | 32 | 1,441.6 | 9L / 2D | 36,000 | 5,820 | 72 | `(0,35,68)` | Tiny `EDCD` (5.8 KB) but high party level |
| **Grilochs Revenge** | 31 | 1,669.1 | 10L / 5D | 40,000 | 12,830 | 90 | `(0,0,73)` | High-level 5-dungeon campaign |
| **Mithril Vault** | 44 | 2,509.2 | 13L / 2D | 52,000 | 16,720 | 170 | `(0,37,5)` | 5 lore `.rsrc` files + `Data Spell` |
| **Half Truth** *(+ `HT 1.0.6`)* | 37 | 4,088.6 | 12L / 1D | 48,000 | 14,980 | 54 | `(0,42,71)` | Custom `Data Caste`, 3 custom tilesets, `Data Spell` |
| **Trouble in the Sword Lands** | 38 | 4,245.9 | 20L / 4D | 80,000 | 160,200 | 30 | `(0,1,1)` | Pierre Vachon; 2 custom music + 2 custom tilesets |
| **War in the Sword Lands** | 38 | 6,995.2 | 21L / 3D | 84,000 | 644,730 | 72 | `(0,1,72)` | Huge (`EDCD` 645 KB); 3 custom music/tilesets |
| **Wrath of the Mind Lords** | 40 | 7,525.1 | 20L / 4D | 80,000 | 37,970 | 108 | `(0,3,84)` | Largest overall; 3 custom music/tilesets |
| *Other non-campaign* | - | - | - | - | - | - | - | `Test` (33 files, 1,066.9 KB), `New Scenario` (33 files, 433.9 KB) |

---

## 2. Decisive Recommendation #1: Game Engine + Agent Tooling / MCP

### WINNER: **Unity 6 (`6000.0.x` LTS, URP 3D Forward Renderer `--template 3d`) + Pure C# `RealmzCore` + Official `unity-cli` (`com.unity.pipeline`)**

### 2.1 Engine & Tooling Comparison Matrix

| Dimension | **Unity 6 (URP 3D HD-2D)** [WINNER] | **Godot 4.x (.NET C#)** | **Web / TS (Three.js + C# WASM)** |
| :--- | :--- | :--- | :--- |
| **Songs-of-Conquest HD-2D Look** | **Best (Native Blueprint).** Exact engine used by Lavapotion (*Songs of Conquest*). Native 3D terrain heights + Shader Graph vertex-billboarded sprites + URP Lit Normal Maps + dynamic torch Point Lights + soft Shadow Maps + URP Volume (ACES, selective emissive Bloom, Vignette) + Cinemachine 3. | **Strong.** `Sprite3D` (Y-Billboard, `StandardMaterial3D` Normal + Alpha Scissor + Double-Sided Shadows) + `WorldEnvironment` ACES/Glow. Slightly more DIY for camera pixel stability and shader tooling. | **High Friction.** `THREE.SpriteMaterial` lacks normal maps/shadows, requiring custom Plane meshes + `MeshStandardMaterial` shadow bias tuning and custom post-FX. |
| **Agent Automation, CLI & MCP** | **Unmatched.** Antigravity's official `unity` plugin (`0.1.9-beta`, 31 skills) is **already installed**. `unity-cli` + `com.unity.pipeline` gives live Editor `script eval`, `gameobject`/`scene`/`asset`/`package` commands, `unity recompile --strict`, `capture screenshot`, `unity test` (EditMode/PlayMode NUnit/JUnit), `unity build`, and built-in `unity mcp` stdio server (`--project-path`). | **Good w/ Addon, Weak Default.** `Coding-Solo/godot-mcp` & `bradypp/godot-mcp` lack live viewport/C# reflection and edit `.tscn` as text; **requires `IvanMurzak/Godot-MCP`** addon + manual `dotnet build` before Godot refreshes C# classes. Zero Antigravity Marketplace plugin/skills. | **Easy CLI, Weak Visual QA.** Good browser/Playwright debugging (`chrome-devtools`), no native 3D scene inspector or Unity-grade build/test tooling. |
| **Pure C# `RealmzCore` Isolation** | **Native In-Process.** `RealmzCore` compiles as pure `.NET Standard 2.1` (`netstandard2.1`) with zero `UnityEngine` refs; runs natively in Unity AND independently via `dotnet test` in `<1s`. *(Note: keep 1 `MonoBehaviour` per `.cs` file matching class name for Unity builds.)* | **Native .NET 8/9.** References `RealmzCore.csproj` directly from Godot `.csproj`. *(Portable: keeping `RealmzCore` clean lets us share 100% of logic if ever targeting Godot later.)* | **WASM Marshaling Tax.** Requires Blazor/NativeAOT WASM + `Float32Array`/shared-memory batching across JS/WASM boundary. |
| **Footprint & Licensing** | Unity Hub (~100-300 MB) + Unity 6 LTS (~10-25 GB w/ modules; needs 40-50 GB unpack headroom; Home-PC has `1,512 GB` free). **Unity Personal is free** (<$200k/yr; **splash screen optional/removable in Unity 6**). | Tiny (~150-200 MB zip, ~400-500 MB extracted; ~130-175 MB exports), 100% MIT, zero account/license daemon. | Minimal runtime, MIT, zero install weight. |

### 2.2 Critical Unity 6 HD-2D Architecture Rules (Avoiding Pitfalls)

1. **CRITICAL: Use URP 3D Forward Renderer (`com.unity.template.urp-blank` / `--template 3d`), NOT `universal-2d`!**
   - *Songs of Conquest* is a **3D world** (tilted perspective camera, 3D elevation/cliffs, 3D shadow maps, dynamic 3D point lights) populated with 2D pixel sprites. URP's `2D Renderer` (`Light2D` / `Shadow Caster 2D`) is strictly flat orthographic and breaks 3D perspective shadows and terrain height.
2. **Lavapotion-Style Shader Billboarding & Shadow Casting:**
   - Rotate sprite bases (`Mesh` quad / `SpriteRenderer`) via a custom **URP Shader Graph** (`shader-graph-create-custom-node`) so vertices billboard toward the tilted camera around their **bottom-center foot pivot** without tilting backwards into the floor.
   - Use **Alpha Clipping (`0.5`)** + **Normal/Bump Map (`Point` filter, Compression `None`, MipMaps OFF)** + a non-tilted `ShadowCaster` pass (or `Shadows Only` 3D proxy capsule) so sprites cast grounded, non-stretched shadows under torch and sun lights.
3. **Pixel Stability & Lighting Stack:**
   - Keep sprite texel density locked (e.g., `32 PPU` for tiles/icons/units), disable MSAA/anti-aliasing on crisp pixel targets, and configure URP Global `Volume` (`urp-postprocessing` + `craft-game-floors`): **ACES Tonemapping**, subtle **Bloom** (`threshold ~1.1`, `intensity ~0.30` with HDR on, driven by emissive pixels on torches/spells), warm directional sun (`#FFF2DC`) + cool-indigo ambient shadow gradient (`#181E38`) + dynamic flickering warm torch `PointLight`s (`#FF9E44`).
4. **CLI-First + Optional MCP Wiring:**
   - Per `unity-cli/SKILL.md`, Antigravity runs **`unity command ...` directly in shell** (exposes more tools than MCP, including `editor set_auto_tick`, `script eval`, `package add`, `recompile`, `test`, `build`).
   - Built-in `unity mcp` (`unity mcp configure antigravity --project-path <path>`) is also available in one command if MCP tool registration is desired, and `CoplayDev/unity-mcp` is optional fallback only for non-shell clients.
   - **Version Guardrail:** Install latest **`6000.0.x` LTS** (avoid `6000.5.0-6000.5.2`, which fail with `PIPELINE_EDITOR_UNSUPPORTED`).
5. **Tracker Music Handling (`MOD` + `MADG`):**
   - During Milestone 0 `realmz-exporter`, convert `MOD` (`M.K.`, `6CHN`, `8CHN`) via `openmpt123` / `libopenmpt` and `Outdoor Music` (`MADG`) via PlayerPRO `MADLibrary` (or record/convert offline) into clean looping `.ogg`/`.wav` stems routed through Unity `AudioMixer` (`MusicVol` / `SFXVol`) using `optimize-audio` presets.

### 2.3 Exact Milestone 0 Setup Steps (Await Kartik "Go" - Not Executed Yet)

```powershell
# 1. Install .NET 8 SDK (for standalone `dotnet test` on RealmzCore) + CMake/Ninja (or prebuilt resource_dasm)
winget install Microsoft.DotNet.SDK.8

# 2. Install standalone Unity CLI (`unity`), authenticate & activate free Unity Personal (<$200k, no splash in Unity 6)
irm https://unity.com/install.ps1 | iex
# (Open fresh shell)
unity auth login
unity license activate --personal --accept-eula

# 3. Check latest 6000.0.x LTS, install Editor + Windows module, and scaffold URP 3D project
unity releases --lts --limit 5 --format json
unity projects create "RealmzHD2D" --path "C:\Projects" --editor-version 6000.0.47f1 --template 3d --install-editor --module windows-mono --accept-eula --no-cloud --format json
unity pipeline install --project-path "C:\Projects\RealmzHD2D"
unity open "C:\Projects\RealmzHD2D"
unity status --until-ready --project-path "C:\Projects\RealmzHD2D" --format json

# 4. Optional: Register built-in Unity MCP server in Antigravity (alongside primary `unity` CLI)
unity mcp configure antigravity --project-path "C:\Projects\RealmzHD2D" --dry-run
```

---

## 3. Decisive Recommendation #2: ComfyUI *Songs of Conquest* Pixel-Art + Normal-Map Pipeline (`8 GB` VRAM)

### WINNER: **SDXL-Family Stack (`Illustrious-XL-v2.0` [installed] + Illustrious Pixel LoRA for characters/monsters/portraits + optional SDXL Base 1.0/`nerijs/pixel-art-xl` for terrain/props) + `xinsir/controlnet-union-sdxl-1.0` Silhouette Lock + Deterministic Grid-Snap & Locked 64-Color Palette Chain + `azagaya/laigter` (1x Sprites) / `Marigold V2` & `TextureNormalFromHeight` (1x Tiles) Normal Maps**

### 3.1 Model Stack Comparison on Home-PC (`RTX 5060 Ti 8 GB` + `32 GB RAM`)

| Model Stack | 8 GB VRAM Behavior | Speed (1024x1024) | Pixel-Art & ControlNet Fit | Verdict |
| :--- | :--- | :--- | :--- | :--- |
| **`Illustrious-XL-v2.0` (Installed)** + Illustrious/SDXL Pixel LoRA | Native FP16 (~5-6 GB); ControlNet Union triggers clean 32 GB RAM async offload. | **Fast** (~4-7s; ~8-11s w/ ControlNet) | **Best for heroes, monsters, dark-fantasy portraits.** Dedicated LoRAs (*Creativehotia Illustrious Pixel Art XL*, Chenkin *Pixel Art XL v2.0*) + Illustrious Canny/Depth ControlNets. | **PRIMARY WORKHORSE** (already on disk). |
| **SDXL Base 1.0 + `nerijs/pixel-art-xl`** | Native FP16 (~5-6 GB), fits in 8 GB cleanly. | **Fast** (~4-7s; <3s w/ LCM-LoRA) | **Highest general pixel-grid regularity** (`weight 1.0-1.2`, 8x downscale) + `xinsir/controlnet-union-sdxl-1.0`. | **BEST FOR TILES / PROPS** (optional 6.5 GB add-on in M0). |
| **`Z-Image-Turbo` (Installed)** + `tarn59/pixel_art_style_lora_z_image_turbo` | FP8 `5.87 GB` + `qwen_3_4b` FP4 (`3.32 GB`) uses RAM offload on 8 GB (`~11-15s`). | **Moderate-Fast** | Emerging LoRA + `Z-Image-Turbo-Fun-Controlnet-Union`; far fewer dark-fantasy sprite LoRAs than SDXL. | **SECONDARY** (quick concept/splash ideation only). |
| **Flux.1-dev / Flux.1-schnell** | 12B DiT needs Q4 GGUF + heavy `T5xxl` RAM swap. | **Slow** (~25-60s+ Dev; ~10-15s Schnell) | Q4 quant creates soft/mushy pixels; Schnell fights style LoRAs. | **SKIP** (too slow/soft for sprite batching). |

### 3.2 Mandatory 7-Stage Anti-Mixel & Normal-Map Pipeline

Raw diffusion outputs fail in games because of "mixels" (inconsistent pixel grid sizes), semi-transparent edge halos, drifting color palettes, and blobby AI normal maps. Enforce this exact graph:

1. **Stage 1 - Realmz `32x32` Silhouette Lock (`xinsir/controlnet-union-sdxl-1.0`):**
   - Extract `32x32` `cicn` and `Tacticals.rsrc` via `realmz_dasm`; **Nearest-Neighbor** upscale to `512x512` (16x) or `1024x1024` (32x).
   - Condition `KSampler` (`denoise 0.45-0.65`) with **Canny** (locks exact weapon/limb silhouette) + **Tile/Depth** (retains internal armor/body mass) against a **flat solid chroma backdrop** (`#FF00FF` or `#00FF00`) and fixed lighting prompt (`3/4 isometric tactical RPG sprite, crisp dark outlines, warm upper-left torchlight, cool deep indigo shadow side, full body centered`).
2. **Stage 2 - Moody Contrast Grade (`kijai/ComfyUI-KJNodes`):**
   - Run `ColorMatchV2` (`mkl`/`reinhard`) against one curated *Songs of Conquest* reference frame (warm torch amber vs deep indigo shadows) *before* quantizing so lighting temperature stays uniform across every batch.
3. **Stage 3 - True Pixel-Grid Snap & Integer Downscale (Zero Mixels):**
   - Optional thin-line safeguard: `PixelOEPixelize+` (`cubiq/ComfyUI_essentials`) expands thin weapons/outlines before shrinking so swords/spears do not vanish.
   - Grid detector + integer snap: **`unclepomedev/ComfyUI-ProperPixelArt`** (Hough/grid detection) OR **`dimtoneff/ComfyUI-PixelArt-Detector`** (`PixelArtDetectorToImage`, Astropulse algorithm) to snap cleanly down to **exact 1x target dimensions**: `32x32` (tiles/icons), `64x64` (standard heroes/monsters), `96x96` (large monsters/trees/props), `128x128` (portraits).
4. **Stage 4 - Locked 64-Color Dark-Fantasy Palette:**
   - Pass through **`sousakujikken/ComfyUI-PixydustQuantizer`** (accepts an external shared palette tensor across all batches) or `PixelArtDetectorConverter` (`ComfyUI-PixelArt-Detector`) locked to one **Realmz HD-2D 64-Color Master Palette** (warm amber torch `#F2A65A` / `#FFE09C`, deep shadow indigo `#14192E` / `#262B4D`, weathered granite, mossy forest, aged brass, crimson). Use clean flat shading or mild Bayer dithering (avoid noisy Floyd-Steinberg on moving `64x64` sprites).
5. **Stage 5 - Halo-Free 1-Bit Alpha Cleanup:**
   - Use **`Limbicnation/ComfyUI-TransparencyBackgroundRemover`** (built specifically for game sprites) or exact Chroma Key color-to-alpha. Avoid raw `RMBG`/`BiRefNet` soft alpha on pixel art (creates gray outline halos unless hard-thresholded + 1px eroded).
6. **Stage 6 - Local Vision QA Gate (`qwen2.5vl:3b`):**
   - Run Home-PC's `qwen2.5vl:3b` (`keep_alive=0` VRAM flush) to reject cropped weapons, stray background islands, or mixed perspectives before Unity import.
7. **Stage 7 - Crisp 2D Normal Maps (Reacts Cleanly to 3D Torches & Sun):**
   - *Best for Units & Portraits (Zero Pillow Blobs):* Batch final 1x transparent sprites through **`azagaya/laigter`** (open-source pixel-art normal generator; outputs OpenGL `+Y up` normal + specular/AO maps with edge-aware bevels that keep crisp outlines intact instead of turning ink lines into sunken trenches).
   - *In-ComfyUI Route (Best for Terrain Tiles, Walls & Volumetric Props):* Generate normals with **`visualbruno/ComfyUI-Marigold-v2`** / **`kijai/ComfyUI-Marigold`** (or zero-VRAM **`ComfyUI-Texture-Simple`** `TextureNormalFromHeight` Sobel height-to-normal), **Nearest-Neighbor downscale to 1x**, mask with 1-bit sprite alpha, and run **Normal Map Normalize** ($Z = \sqrt{1 - X^2 - Y^2}$) so lighting vectors stay valid in Unity URP.

### 3.3 Milestone 1 Concrete Asset Manifest (Tight & Ship-First)

| Category | Exact Milestone 1 Scope | Target 1x Size | Maps Generated |
| :--- | :--- | :---: | :--- |
| **Core Terrain & Dungeon Autotiles** | Grass, dirt road, town cobble, shallow water, stone dungeon floor, 3D extruded dungeon walls, wood cabin/tavern walls, billboarded pine/oak trees, animated wall/standing torches (~28 base tiles + Wang transitions) | `32x32` tiles / `64x96` trees & props | Albedo + Normal + Torch Emissive Mask |
| **Hero Tactical Sprites** | 6 starter party heroes matching `base/Realmz/Character Files/` (`Kevlar`, `Lothlorian`, `Silver Leaf`, `Traskelion`, `Trevor`, `Vormale` archetypes; idle, melee swing/cast, hit-flash frames) | `64x64` | Albedo + Laigter Normal |
| **Starter Monsters** | 8 early *City of Bywater* foes (Giant Frog, Wolf, Brigand/Bandit, Goblin/Orc Scout, Skeleton, Giant Spider, Ogre/Troll, Cultist/Acolyte) | `64x64` (`96x96` Ogre) | Albedo + Laigter Normal |
| **Portraits** | 6 hero portraits + 4 key NPCs (Mayor, Shopkeeper, Temple Priest, Tavern/Quest Giver) | `128x128` | Albedo (optional subtle normal for UI light) |
| **UI & Icons + Spell VFX** | ~20 `32x32` item/weapon/potion/condition/spell icons + 9-slice dark-slate/parchment UI chrome; **5 combat spells rendered via URP shader/VFX Graph + dynamic torch/magic point light flashes** | `32x32` icons | Crisp Albedo + Procedural URP VFX |

---

## 4. Small Starter Scenario Pick + Modern Tactical Balance & QoL Spec

### 4.1 Recommended Starter Scenario Scope

- **Milestone 1 Playable Vertical Slice: `City of Bywater` - Opening Quest Slice (`Land 0 / map_0` + First Dungeon Entrance)**
  - **Why `Bywater` `Land 0` wins for M1:** It is the **only Level-1 campaign** (`rec_lvl = 6`, aligning directly with the 6 pre-made Level-1 heroes in `base/Realmz/Character Files/`), its opening `Land 0` (`map_0`) forms a complete, self-contained town-to-woods RPG loop (Shop, Temple, Mayor/Tavern quests, early forest encounters), and `map_0` (`95 LAP + 168 XAP`) is already cross-verifiable against `Realmz-Castle/Realmz-Remake`.
  - **Why NOT the full `Bywater` campaign in M1:** Full `Bywater` has `9` land + `2` dungeon maps and heavy `Data EDCD` (`52,820 B`). Slicing to `Land 0` delivers a playable 15-minute vertical slice in Weeks 3-6 without waiting on 11 maps.
  - **Dual Companion Targets:**
    - **`Tutorial`** (`517.9 KB`, `2L + 1D`, `Data EDCD` `1,020 B`, `rec_lvl = 6`): Use as the automated headless `realmz_dasm` + `RealmzCore` VM CI regression fixture in Milestone 0.
    - **`Prelude to Pestilence`** (`894.6 KB`, `5L + 1D`, `Data DD` `20,000 B`, `Data EDCD` `6,160 B`, `rec_lvl = 30`): Smallest of the 13 full scenarios by map/script footprint (~9x fewer complex dialogs than Bywater); target as the **fastest full 100%-cleared scenario in Milestone 2** right after finishing `Bywater`.

### 4.2 Modern Tactical Balance & QoL Upgrades ("1994 Tactical Depth, Zero Clunky Friction")

| System (`src/realmz_orig/`) | 1994 Friction / Balance Flaw | Modern Tactical Balance & QoL Upgrade |
| :--- | :--- | :--- |
| **Action Economy & Turn Loop** (`combat.c`, `combatinfo-combatchoice.c`, `track[]`) | Opaque polling loops (`GetMouse`/`StillDown`), hidden turn queue, accidental single-tile misclicks wasting turns. | **1 Move + 1 Standard Action + 1 Swift/Free Action per turn** (`Move` up to `movement` budget with **free Undo Move** before committing any attack/spell/LOS reveal; `Standard` = Melee / Missile / Spell / Item / Guard-Overwatch; `Swift` = Belt Potion / Swap Weapon / Torch). Always-visible **Top Initiative Ribbon** showing next 8 turns (`track[]`) + hover-highlighting active units on the 3D grid. |
| **Transparent Math & Tooltips** (`attack.c`, `resist.c`, `structs.h`) | Hidden THAC0/AC hit math, buried `save[8]` & 6 immunities, unreadable 40 `CharCondition` flags, cryptic `vssmall`/`vsundead`/`vsevil` weapon perks. | **Hover Combat Preview Card:** Shows exact **Hit %**, min-max **Damage**, crit %, active slaying bonuses, defender AC breakdown, elemental resist/immunity badges, and active `COND_*` icon tray with exact stat effects & turns left. |
| **Tactical Positioning, AoE & LOS** (`cansee.c`, `castspell.c`, `spellarea[18][7][7]`) | Blind AoE targeting causing accidental friendly fire; hidden missile blockers; flat melee trading without positioning reward. | **Live Grid Overlays:** Cyan reachable movement tiles; dotted LOS ray showing **Blocked / Partial Cover** penalties before firing; exact **`spellarea` AoE footprint coloring enemies red vs allies amber/green**; **Attack-of-Opportunity (AoO) threat rings** around engaged melee units; **+15% Hit / +2 Flanking Bonus** indicator when engaging from opposite arcs. |
| **Fairer RNG & Anti-Wipeout Guardrails** (`age.c`, `resist.c`, `encounters.c`) | Unfair 10-round stun/paralyze party wipes, sudden permanent `agechange[5][15]` death surprises, harsh single-save RNG spikes. | **Campaign Mode Toggle (`Modern Balanced` default vs `Classic 1994 Strict`):** Escalating end-of-turn recovery saves (`+10%` per round) + boss diminishing returns on hard CCs (`Paralyze`/`Sleep`/`Stone`/`Charm`); convert fatal aging spikes into curable **Exhaustion/Curse** stacks removable at Temples; **Grazing Hits** (`50%` damage on near-miss heavy spells/blows); automatic **Pre-Encounter Checkpoint Save** before every `Data DD`/`BD` trigger. |
| **Pacing, Exploration & Camp/Shop QoL** (`moveparty.c`, `threed.c`, `items.c`, `saveshop.c`, `temple.c`) | Slow `delay(10)` step pauses, claustrophobic 1994 wall-blits, tedious per-hero item/gold clicks, zero scroll wheel, slow trash fights. | **`1x / 2x / 4x / Instant`** combat animation speed + **Smart Auto-Resolve** prompt on trivial `>=3:1` power fights. **A* Click-to-Move** across `90x90` maps (auto-halts on enemy sighting, door, trap, or AP) + WASD/controller support. **Unified 2.5D Dynamic Torchlight** (`PARTY_COND_TORCH_LIT`) replacing 1994 wall-blits (with optional nostalgia 1st-person minimap). **Shared Party Stash + 1-Click Auto-Balance Encumbrance**, **Quick-Camp Presets** (`nspells[7]` loadouts + auto-bandage/torch), **"Identify All / Sell Common Junk"** shop filters, and clean **JSON saves**. |

---

## 5. Phased Execution Roadmap & 1-Line "Go" Trigger

- **Milestone 0 - Toolchain, `realmz_dasm` Extractor & 4-Asset Style Lock (Weeks 1 - 2):**
  - Install `.NET 8 SDK`, `unity` CLI + Unity `6000.0.x` LTS (`--template 3d`) + `com.unity.pipeline`, and build/install `resource_dasm` (`realmz_dasm`) + ComfyUI pixel/palette custom nodes + `azagaya/laigter`.
  - Build `RealmzCore` (`netstandard2.1` + NUnit tests) and `realmz-exporter`, verifying 100% schema/opcode extraction on `Tutorial` and `City of Bywater`.
  - Run the RTX 5060 Ti Style Bible smoke test on **1 hero, 2 monsters, 1 portrait, and 8 core tiles** + Laigter normal maps inside a lit Unity URP 3D test scene (torches + shadows + ACES/Bloom).
- **Milestone 1 - Playable Vertical Slice: *City of Bywater* `Land 0` (`map_0` + 1 Dungeon Entrance, Weeks 3 - 6):**
  - Deliver Milestone 1's ~40-asset manifest, full Move+Standard+Swift action loop, Initiative Ribbon, Hit%/Resist tooltips, AoE/LOS/Flanking/AoO overlays, A* click-move, 1 Shop, 1 Temple, and 2 Encounters with JSON save/load.
- **Milestone 2 - Complete *City of Bywater* + *Prelude to Pestilence* Fast Full Clear (Weeks 7 - 12):**
  - Scale ComfyUI + `qwen2.5vl:3b` batch pipeline for remaining Bywater & Prelude monsters/portraits, all 40 `CharCondition`s, full `Data S` spellbook, `Data TD2` thief traps, and converted `MOD`/`MADG` music toggle.
- **Milestone 3 - Full 13-Scenario Parity & Polish (Weeks 13+):**
  - Validate `Half Truth` (`Data Caste`), `Sword Lands` (`Data Spell` / `Custom 1..3 BD`), `Layout` overworld stitching, and full gamepad/Steam Deck bindings.

**1-Line "Go" Command for Milestone 0 (Await Kartik Approval):**
> Say **"Go on Milestone 0"** to authorize installing `.NET 8 SDK` + `unity` CLI + Unity `6000.0.x` LTS (`--template 3d` + `com.unity.pipeline`), adding the ComfyUI pixel/palette nodes + `azagaya/laigter`, scaffolding `RealmzCore` + `RealmzHD2D`, and running the 4-asset *Songs of Conquest* normal-lit smoke test on RTX 5060 Ti.
