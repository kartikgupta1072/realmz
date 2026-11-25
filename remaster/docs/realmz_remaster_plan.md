# Realmz Remaster Strategy & Technical Blueprint

**Target Repo Audited:** [`kartikgupta1072/realmz`](https://github.com/kartikgupta1072/realmz) (fork of [`Realmz-Castle/realmz`](https://github.com/Realmz-Castle/realmz))
**Audit Date:** 2026-11-23 | **Mode:** Strictly Read-Only Audit

---

## Executive Snapshot: What Is Inside `kartikgupta1072/realmz`

- **Branch & Upstream Status:** Both `main` and `Remaster` in `kartikgupta1072/realmz` currently point to commit `21a951c` (`2026-07-20`, *"Handle missing resources consistently (#285)"*). Your fork is **0 commits ahead and 12 commits behind** upstream `Realmz-Castle/realmz` `main` (`4089d55`, `2026-08-07`), which added high-DPI rendering (`#316`), post-fight treasure screen fixes (`#263`), 3D dungeon view presentation fixes (`#303`), centered map/dungeon viewport + map notes (`#300`/`#301`/`#302`), and left/right modifier keys (`#318`). No `.github/workflows/` CI files exist in either repo. No local checkout of `Realmz` exists on `C:`.
- **Language & Build Architecture:** CMake `>= 3.27` (`CMakeLists.txt`), **C90 (`src/realmz_orig/`, ~1.46 MB)** + **C++23 (`src/` & `src/windows/`, ~289 KB)** + **Objective-C++ (`src/macos/`, ~13 KB)**, linking **SDL3**, **SDL3_image**, **SDL3_ttf**, **zlib**, and [`fuzziqersoftware/phosg`](https://github.com/fuzziqersoftware/phosg) + [`fuzziqersoftware/resource_dasm`](https://github.com/fuzziqersoftware/resource_dasm) (`resource_file`). Compiles as a macOS fat binary (`x86_64;arm64`, min macOS 13.3) or cross-compiles to Windows (`llvm-mingw` + NSIS/ZIP).
- **What Works Today vs What Does Not:**
  - **Compiles & runs:** Classic Mac QuickDraw, WindowManager (`dBoxProc` chrome), Dialogs/Controls, TextEdit, EventManager, and `ResourceManager` (`PICT`, `cicn`, `ppat`, `crsr`, `STR#`, `TEXT`/`styl`) are shimmed in C++ over software-composited RGBA8888 surfaces presented via SDL3 (`SDL_LOGICAL_PRESENTATION_LETTERBOX`, `SDL_SCALEMODE_PIXELART`, 1.8 gamma LUT). `snd ` sound effects decode and resample to 48 kHz `SDL_AUDIO_S16LE`.
  - **Broken / stubbed:** Background music is a no-op stub (`src/realmz_orig/music.c`: `void music(int16_t playlist) {}`), with original PlayerPRO (`MADDriver`/`MADMusic`) code commented out in `src/realmz_orig/main.c`. Upstream `README.md` still warns this is a beta release with stability and save-corruption risks, and `CONTRIBUTING.md` explicitly rejects UI overhauls, new APs/encounter types, rebalance changes, or scroll-wheel UX.
- **Bundled Content & Ecosystem Discovery:**
  - **13 full scenarios** (`Assault on Giant Mountain`, `Castle in the Clouds`, `City of Bywater`, `Destroy the Necronomicon`, `Grilochs Revenge`, `Half Truth`, `Mithril Vault`, `Prelude to Pestilence`, `Trouble in the Sword Lands`, `Twin Sands of Time`, `War in the Sword Lands`, `White Dragon`, `Wrath of the Mind Lords`) plus `Tutorial`, `New Scenario`, `Test`, and `base/Optional/` (`Divine Development X-tras.rsrc`, `Race N' Caste Pack.rsrc`).
  - **Core media forks:** `The Family Jewels.rsrc` (7.47 MB: `973` `cicn`, `129` `PICT`, `142` `snd `, `70` `STR#`, `36` `crsr`, `7` `ppat`), `Portraits.rsrc` (`120` `cicn`), `Tacticals.rsrc` (`240` `cicn`), `Realmz Manual.rsrc` (`82` `PICT`, `31` `TEXT`/`styl`), `Realmz Music/` (`10` Amiga 4-ch `M.K.` ProTracker MODs + `1` PlayerPRO `MADG` track).
  - **Two critical sibling repos under `Realmz-Castle` / `fuzziqersoftware`:**
    1. `fuzziqersoftware/resource_dasm` already includes **`src/realmz_dasm.cc`**, `RealmzGlobalData`, `RealmzScenarioData`, and `RealmzSaveData`, which already disassemble scenarios into `script.txt`, `land_*.json` / `dungeon_*.json` (`--maps-as-json`), PNGs (`PICT`/`cicn`), WAVs (`snd `), and TXT/RTF (`TEXT`/`styl`).
    2. `Realmz-Castle/Realmz-Remake` is an active GDScript/Godot 4 community remake (last pushed `2026-07-25`, also `CC BY-NC-SA 4.0`) using `Tiled` maps and `godot-openmpt` for tracker music.

---

## A. Codebase & License Reality

### 1. Why You Should NOT Build a Remaster Inside `src/realmz_orig/`

`src/realmz_orig/` is a faithful 1994 Classic Mac C90 codebase (plus 1999 Myriad `#ifdef PC` Windows shims) whose game rules are deeply entangled with QuickDraw screen coordinates and blocking OS modal loops:
- **Global State Monolith (`src/realmz_orig/variables.h`, 340 lines):** Party (`c[6]`), 90x90 land/fog grids (`field[90][90]`, `tempfield[90][90]`, `site[90][90]`, `mapstats[402]`, `solids[1024]`, `door[100]`), combat queues (`incombat`, `enemy[12]`, `monpos[100][2]`, `pos[6][2]`, `q[maxloop]`, `track[maxloop]`), spells (`spelldata[5][7][15]`), and offscreen `GWorldPtr`s live in global arrays mutated everywhere.
- **Blocking Event Loops & Hardcoded Rects:** `MainLoop()` (`main.c`), `combat()` (`combat.c`), `threed()` (`threed.c`), `castspell()` (`castspell.c`), `resolvespell()` (`resolvespell.c`), and `thiefenc()` (`encounters.c`) enter synchronous `for (;;)` / `while` polling loops (`GetMouse()`, `StillDown()`, `GetNextEvent()`, `ModalDialog()`, `MenuSelect()`, `goto` state jumps) against hardcoded 640x480 offsets (`GlobalLeft + 469`, `flamerect {122, 474, 154, 506}`, `WALLDEST[14]`).
- **Implication:** Adding widescreen responsive UI, initiative bars, AoE hover previews, smooth animations, or mouse-wheel inventory scrolling inside `src/realmz_orig/` fights every line of the legacy architecture.

### 2. Salvage vs Clean Rewrite Matrix

| Layer | Verdict | Specific Files / Formats | Rationale |
| :--- | :--- | :--- | :--- |
| **Binary Data & Scenario Importer** | **SALVAGE / WRAP** | `structs.h`, `convert.c`/`h`, `setupnewgame.c`, `files.c`, `loadland-loadpixmap.c`, plus `resource_dasm` (`realmz_dasm.cc`) | Zero compression or encryption (except ignored registration `codeseg1..4`). Every `Data *` file is flat 68k big-endian C structs + MacRoman Pascal strings (`Str255`, `Str63`). `realmz_dasm` already parses maps to JSON and `.rsrc` to PNG/WAV/TXT. |
| **Ruleset Math & Tables** | **PORT CLEANLY (Golden Reference)** | `attack.c`, `castspell.c`, `resolvespell.c`, `resist.c`, `age.c`, `levelup.c`, `race.c`, `class.c`, `encounters.c` | Port formulas into a pure, unit-tested headless rules module (`RealmzCore`), using `src/realmz_orig/` (including upstream bugfixes `#276`, `#288`, `#295`, `#297`) as the ground-truth spec. |
| **AP / Encounter Script VM** | **REWRITE CLEANLY** | `encounters.c`, `newland.c`, `Data DD`/`DDD`/`ED`/`ED2`/`ED3`/`EDCD`, `Global` | Implement an explicit, non-blocking opcode/state-machine VM for APs, simple/complex encounters, rogue traps (`Data TD2`), and timed events (`Data TD3`) instead of `goto` + `ModalDialog` loops. |
| **Audio (SFX + Tracker Music)** | **SALVAGE + OPTIONAL UPGRADE** | `The Family Jewels.rsrc` (`142` `snd `), `Realmz Music/*`, `Custom * Music` | Export `snd ` via `ResourceDASM::decode_snd_data` to WAV/OGG. Play 4/6/8-ch MODs (`M.K.`, `6CHN`, `8CHN`) via `libopenmpt`; convert the 2 `MADG` tracks (`Outdoor Music`, `Trouble/Custom 2`) via PlayerPRO `MADLibrary`. |
| **Rendering, Windowing, Input, Saves, Shareware Gate** | **REWRITE 100%** | `WindowManager.cpp`, `QuickDraw.cpp`, `EventManager.h`, `threed.c`, `loadsavedgame.c`, `orderform.c`/`doreg*` | Replace software-composited Mac windows, 3D wall blits, `0x3979`/`0x398B` fragile binary saves, and `serial` shareware locks (`doreg*`, level-4 cap, scenario `>10` cap) with modern scene graph, GPU shaders, JSON saves (plus optional legacy save importer), and zero shareware gates. |

### 3. Critical Binary Parser Gotchas Verified in `src/realmz_orig/`

Any external importer or engine loader must handle these exact verified quirks:
1. **68k Big-Endian + Exact Struct Alignment:** `structs.h` uses 4-byte natural alignment for `int32_t` (replacing 32-bit `long`), while `Types.h` applies `#pragma pack(push, 2)` on `Point`/`Rect`. Verified on-disk record sizes: `race` = 408 B (`Data Race` is 28,800 B on disk, but `main.c:1181` stops at `30 * 408 = 12,240 B`), `caste` = 576 B, `itemattr` = 100 B (`Data ID` = `0..799`, scenario `Data NI` = `800..999`), `spell` = 30 B (`420` base in `Data S` + `105` in scenario `Data Spell`), `monster` = 210 B (`Data MD`, `MD-1`, `MD1` for 3 monster sets), `maps` = 340 B (`Data MD2`), `battle` = 346 B (`Data BD`), `shop` = 3,002 B (`Data SD`), `encount` + strings = 426 B (`Data ED`), `encount2` + strings = 520 B (`Data ED2`).
2. **`Data ED` File Sizes Across Newer Scenarios:** Older scenarios (`City of Bywater` `8,520 B = 20 * 426`, `Assault`, `Castle`, `Grilochs`, `White Dragon`) are exact multiples of 426 B; newer scenarios (`Half Truth`, `War in the Sword Lands`, `Wrath of the Mind Lords`, `Trouble`) have trailing/differently-sized bytes. Replicate `setupnewgame.c:162`'s loop (`while fread(&enc, sizeof(enc), 1)` + `4 * 80` B) and verify against `realmz_dasm`'s `RealmzScenarioData`.
3. **Scenario Overrides & Optional Files:** Scenarios >= 20 (e.g., `Half Truth`) ship a custom `Data Caste` overriding base `Data Caste`; `Tutorial` ships a custom `Data Race`; `Data Custom 1..3 BD` pair with `PICT` `306..308` in `Scenario.rsrc`; `City of Bywater` omits `Layout`, `Data RI`, `Data Spell`, and `Custom * Music` which newer scenarios include.

### 4. IP & License Boundaries (`CC BY-NC-SA 4.0`)

Everything in `kartikgupta1072/realmz` (`src/realmz_orig/`, SDL3 port, `base/Realmz/`, `The Family Jewels.rsrc`, `Portraits.rsrc`, `Tacticals.rsrc`, all 13 scenarios, `Realmz Manual.rsrc`) is distributed under **Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)**, copyright 1994 Tim Phillips:
- **Public Open-Source Non-Commercial Remaster (Valid Today):** You can freely build, fork, modernize, overhaul UI/engine, and release publicly for **free / non-commercial** use as long as you keep the project under **CC BY-NC-SA 4.0**, retain attribution to Tim Phillips + modification notices (`LICENSE` Sec. 3a/3b), and apply no DRM. Note `CONTRIBUTING.md` confirms `Realmz-Castle/realmz` rejects non-preservation changes upstream, explicitly welcoming custom forks for remakes/enhancements.
- **Commercial Release / App Store / Paid Steam Release (Blocked Without Clean-Room Separation or Separate License):**
  - `NC` strictly forbids commercial advantage or monetary compensation on any Adapted Material derived from the repo's C code, Tim Phillips's scenarios, text, or `.rsrc` assets.
  - Line-by-line C-to-C# port of `src/realmz_orig/` also counts as Adapted Material under `SA`.
  - **Escape Hatch if Commercial Potential Matters Later:** Write an **MIT/proprietary clean-room tactical RPG engine + original IP/art/campaign**, and ship an optional **free BYO-Data Importer plugin** that reads a user's own legitimately obtained Realmz `Data Files/` + `Scenarios/` at runtime without bundling any Fantasoft/CC BY-NC-SA files.

---

## B. Engine & Architecture Recommendation

### 1. Architecture Pattern First: Decoupled 3-Layer Stack

Regardless of engine choice, preserve Realmz's greatest superpower - **playability of all 13 classic scenarios (starting with *City of Bywater*) plus fan-made scenarios** - via a strict 3-layer separation:

1. **`realmz-exporter` (Offline CLI / Build Tool):** Wraps `fuzziqersoftware/resource_dasm` (`realmz_dasm`) + a small Python validator to convert any Realmz folder (`Data Files/` + `Scenarios/<Name>/` + `Character Files/`) into clean normalized JSON (`scenario.json`, `maps/land_*.json`, `maps/dungeon_*.json`, `encounters.json`, `items.json`, `spells.json`, `monsters.json`) plus extracted PNG/WAV/OGG reference assets.
2. **`RealmzCore` (Headless Simulation & Rules Engine):** Pure engine-agnostic simulation (C# or TypeScript) owning party stats, inventory/encumbrance, 90x90 map/LOS/fog state, AP/EC/DD/ED bytecode VM, combat queue, `spellarea[18][7][7]` targeting, saving throws, and JSON save/load. Headlessly testable against `src/realmz_orig/` math.
3. **Presentation Layer (Engine):** Tilemap/lighting, animations, spell VFX, audio mixer, and modern responsive RPG HUD subscribing to `RealmzCore` state events.

### 2. Target Stack Comparison

| Criteria | **Option 1: Unity 6 (C# + 2D URP / HD-2D)** | **Option 2: Godot 4 (C# / GDScript)** | **Option 3: Web / TypeScript (PixiJS 8 + React HUD)** |
| :--- | :--- | :--- | :--- |
| **Visual Ceiling (Lighting, HD-2D, Spell VFX)** | **Best.** 2D URP normal-mapped sprites, Shader Graph, 2D dynamic torchlights, optional 2.5D tilted HD-2D camera (`Cinemachine`), VFX Graph. | **Very Good.** `TileMapLayer`, `PointLight2D`, canvas shaders, solid 2D/2.5D; slightly more DIY for HD-2D post-FX. | **Good 2D / Fast UI.** Crisp WebGL sprites & shaders via PixiJS 8, but 2.5D tilt requires Three.js mixing. |
| **RPG Menu & Inspector UX Speed** | **Good** (`UI Toolkit` UXML/USS + Grid/ListViews), moderate boilerplate for dense tooltips. | **Good** (`Control` nodes, Themes), fast layout once learned. | **Unmatched.** Realmz is 60% menus/sheets/shops/logs; React + Tailwind builds searchable inventory, hover stat breakdowns, and combat logs 3x faster. |
| **License Fit w/ `CC BY-NC-SA 4.0`** | **Friction if commercial later; fine for free/hobby.** Unity Personal runtime terms & proprietary editor vs pure FOSS expectations of retro community. | **Best.** 100% MIT engine, zero runtime fees/EULAs; matches `Realmz-Castle/Realmz-Remake` (`Godot 4 + godot-openmpt + Tiled`). | **Excellent.** Open MIT stack, instant URL sharing (no `.exe`/`.dmg` gatekeeping), easy PWA/iPad play. |
| **Scenario Importer & `RealmzCore` Fit** | **Excellent.** Pure C# `RealmzCore` + `System.Text.Json`; `dotnet` CLI already present on Home-PC (Unity Hub Editor not yet installed). | **Excellent.** Same pure C# `RealmzCore` works in Godot 4 `.NET` build, OR interop with `Realmz-Remake` GDScript lessons. | **Very Good.** TS `RealmzCore` + WASM `libopenmpt`; note runtime local-folder BYO-import uses browser File System Access API (or Tauri desktop wrap). |
| **Kartik Tooling Leverage** | **Strong.** Official Unity skills already loaded in Antigravity (`ui-uitk`, `2d-pixel-perfect`, `create-tile-palette-or-rule-tiles`, `urp-postprocessing`, `unity-cli`). | Clean from-scratch setup; `godot` binary not yet installed. | `node.exe` already installed on Home-PC; fastest zero-install UI iteration. |

### 3. Recommendation

- **Primary Recommendation (Best Overall Remaster): Unity 6 (2D URP) + Pure C# `RealmzCore`** if Kartik wants the richest visual/lighting presentation (normal-mapped fantasy tiles, dramatic dungeon torchlight, optional HD-2D depth-of-field, crisp `UI Toolkit` HUD, Steam/PC/macOS/Steam Deck polish) and wants to leverage the Unity skills/tooling already wired into his workspace.
- **Top Open-Source / Community Alternative: Godot 4 (.NET C#) + `RealmzCore`** if Kartik wants a lightweight, 100% MIT open-source toolchain that plugs directly into the `Realmz-Castle` community ecosystem (`godot-openmpt`, `Tiled` pipelines, Linux/macOS/Windows/Web). Because `RealmzCore` is pure C#, **switching between Unity 6 and Godot 4 `.NET` shares 100% of the simulation and importer code**.
- **Dark-Horse Speed Option: TypeScript + PixiJS 8 + React** if Kartik wants to ship a playable *City of Bywater* vertical slice in the fewest evenings with the cleanest possible RPG menus/tooltips and instant browser/iPad play.
- **Avoid:** Extending `src/realmz_orig/`'s C90 SDL3 window shim for a visual remaster (save upstream contributions to `Realmz-Castle/realmz` strictly for preservation/bugfixes).

---

## C. Asset Rebuild Pipeline (Cohesive Art Direction, Zero "AI Slop")

### 1. Original Asset Inventory to Replace or Remaster

| Asset Source | Count & Type | Role in Game | Strategy |
| :--- | :--- | :--- | :--- |
| `Portraits.rsrc` | `120` `cicn` | Party & NPC character portraits | **AI-Remaster via ComfyUI** (high-impact hero art, 1:1 style-locked) |
| `Tacticals.rsrc` | `240` `cicn` | Tactical combat tokens, missiles, AoE/spell icons | **AI-Remaster (monsters/tokens)** + **Shader/VFX (spells)** |
| `The Family Jewels.rsrc` | `973` `cicn`, `129` `PICT`, `142` `snd `, `36` `crsr`, `7` `ppat` | Terrain tiles, items, monsters, story/location art, SFX, cursors | **Hybrid:** Cohesive indie tileset + UI pack for generic terrain/items; **ComfyUI Img2Img** for key `PICT` scenes & unique monsters |
| `Scenario.rsrc` (per scenario) | e.g., Bywater `1` `PICT`/`3` `cicn`; Wrath `21` `PICT`/`498` `cicn`/`50` `snd ` | Scenario-specific banners, bosses, custom monsters, `Custom 1..3 BD` tiles (`PICT` `306..308`) | Automated batch pipeline via `realmz-exporter` + style-locked ComfyUI + fallback clean upscale so fan scenarios still render cleanly |

### 2. Grounded Hardware & Local Stack on Home-PC

Verified already installed on Home-PC:
- **GPU:** NVIDIA RTX 5060 Ti (`8,151 MiB` VRAM, CUDA 13.x).
- **ComfyUI (`C:\ComfyUI` + `Comfy-Desktop`):**
  - `Illustrious-XL-v2.0.safetensors` (`6.94 GB`) - ideal backbone for cohesive inked/painted dark-fantasy RPG portraits, monsters, and illustration `PICT`s.
  - `z-image-turbo_fp8_scaled_e4m3fn_KJ.safetensors` (`6.16 GB`) - ultra-fast (`~11 - 14s`) iteration for concept exploration and scenario splash scenes.
  - `comfyui_controlnet_aux` + `ComfyUI-GGUF` installed.
- **Local Vision QA (`Ollama`):** `qwen2.5vl:3b` (with automatic `keep_alive=0` VRAM eviction verified alongside ComfyUI).

### 3. Anti-Slop Cohesion Pipeline (5 Mandatory Rules)

Raw text-to-image fails in RPGs because every sprite gets different lighting angles, clashing line weights, messy backgrounds, and mixed pixel sizes ("mixels"). Lock cohesion with this exact pipeline:

1. **Choose ONE Visual Grammar & Style Bible First (before batching):**
   - *Option A (Recommended for `Illustrious-XL-v2.0`):* **Inked Dark-Fantasy Tactical RPG (Battle Brothers / Darkest Dungeon / Banner Saga token clarity)** - `128x128` crisp painted/inked tokens on clean transparent backgrounds + `256x256` portraits + cohesive dark-fantasy 2D tileset (purchased/curated indie base pack + 2D URP normal-map lighting). Plays to `Illustrious-XL`'s strength; avoids fragile pixel-grid distortion.
   - *Option B:* **Crisp Retro-Modern Pixel / HD-2D (Octopath / Sea of Stars)** - `32x32` or `48x48` locked tiles + `64x64` battle sprites with strict nearest-neighbor quantization and 2.5D tilted lighting. Requires a dedicated pixel-art LoRA + aggressive grid snapping and cleanup.
2. **Silhouette-Locked Img2Img + ControlNet (Never Prompt Blindly):**
   - Extract every original 32x32 `cicn` and `PICT` via `realmz_dasm`.
   - Upscale original 32x32 sprites 16x (`512x512` nearest-neighbor) and feed into ComfyUI using **ControlNet (Lineart/Depth/Canny) + IP-Adapter Style Reference** anchored to **one approved 4-sprite "Style Bible" sheet** + a single locked **Style LoRA** at fixed weight (`0.75 - 0.85`), denoise `0.45 - 0.62`, fixed lighting prompt (`top-left warm key light, neutral dark studio backdrop, consistent 2px dark ink outline, full-body centeredRPG token`).
   - Result: A Goblin, Beholder, Mind Lord, or Mithril Helm keeps its unmistakable 1994 Realmz silhouette and facing direction while gaining modern detail.
3. **Do NOT AI-Generate 973 Individual Terrain Tiles:**
   - Realmz's `Data * BD` (`struct mapstats { short build[3][3]; }`) is literally an early autotile/terrain-transition system.
   - Build or license **10 - 15 cohesive base terrain autotiles** (grass, dirt/road, stone cobble, water, forest, mountain, swamp, desert, snow, dungeon floor/wall, lava) using a curated indie RPG pack or hand-vetted seamless `CircularVAEDecode` base textures + **map original Realmz tile IDs to modern Wang/9-slice autotiles**! This eliminates grid seams completely and cuts tile art work by 85%.
4. **Deterministic Python/CLI Post-Processing Pass (Zero Unchecked Raw PNGs):**
   - **Background removal + edge cleanup:** Automated alpha matting (`rembg`/threshold) + 1px halo erosion + uniform dark ink rim pass + fixed bottom-center foot anchor.
   - **Color Harmony Lock:** Quantize or color-grade generated sprites toward a shared **64-color master RPG palette** (and if Option B Pixel Art, downscale with pixel-grid majority voting / `Aseprite` CLI so zero half-pixels exist).
   - **Vision QA Gate (`qwen2.5vl:3b`):** Batch-score outputs locally for cropped limbs, lingering background blobs, wrong perspective, or text hallucinations; auto-flag outliers for seed retry before packing atlases.
5. **Typography, UI & Audio Cohesion:**
   - **UI & Fonts:** Replace Mac `dBoxProc` 1px borders and `Black Chancery` body text with crisp 9-slice dark-slate/parchment panels, high-legibility `Inter` (already in `vendored/Inter/InterVariable.ttf`) for stats/logs, and an editorial fantasy serif for headers.
   - **Spell VFX:** Drive Unity VFX Graph / URP shaders (or Godot `GPUParticles2D`) directly from `struct spell`'s `damagetype`, `size`, and `spellarea[18][7][7]` rather than generating static sprite sheets.
   - **Audio:** Dual-mode toggle in Settings: **Classic** (`libopenmpt` playing original `.mod` + converted `MADG` + crisp normalized `snd ` WAVs) vs **Remastered** (re-orchestrated SoundFont/ambient stems + layered combat hit/cast foley).

---

## D. Gameplay Modernization ("Keep the 1994 Soul, Cut the Friction")

Realmz's 1994 charm comes from its deep D&D-inspired party building, lethal tactical battles, secret-packed maps, and richly written encounters. Every modernization below removes 1994 UI friction **without diluting the ruleset**:

| System in `src/realmz_orig/` | 1994 Pain Point | Modernization Upgrade |
| :--- | :--- | :--- |
| **Tactical Combat** (`combat.c`, `getbattleorder.c`, `showrange.c`, `Data AD`) | Blocking mouse polling, zero turn-order visibility, hidden `spellarea[18][7][7]` footprints, modal `flashmessage` pauses | **Top Initiative Order Ribbon** (`track[]` live turn queue); **Hover Previews** for movement cost, missile/LOS blockers (`cansee.c`), and exact `spellarea` AoE tiles highlighting friend vs foe; **Floating Damage/Status Popups** + filterable Combat Log drawer; 1x/2x/4x speed slider. |
| **3D Dungeon vs Grid** (`threed.c`, `Data RD`/`RDD`) | Claustrophobic static wall-blit rect viewport with hidden turn direction and `delay(10)` step pauses | **Unified Crisp Top-Down / 2.5D Dungeon Grid** with dynamic torchlight/darkness (`PARTY_COND_TORCH_LIT`, `isdark`, `uselos`), line-of-sight fog-of-war, and **optional "Classic First-Person Minimap/Homage Window"** toggle for nostalgia. |
| **Stats, AC & 40 Conditions** (`structs.h`, `showstats.c`, `resist.c`, `age.c`) | 40 `CharCondition` flags, 8 `save[8]`, 6 monster immunities, `vssmall`/`vsundead`/`vsevil` weapon bonuses, and `agechange[5][15]` aging tiers hidden behind cryptic abbreviations | **Interactive Tooltips Everywhere:** Hover any enemy or attack button to see hit %, AC/dodge math, elemental save/immunity badges, and weapon bonus triggers; clear icon trays for all 40 conditions (`COND_*`); item compare cards. |
| **Inventory, Shops & Camp** (`items.c`, `pool.c`, `saveshop.c`, `temple.c`) | No mouse wheel, tedious per-character item transfers, manual gold pooling, repetitive identify/sell/bandage clicks | **Drag-and-Drop Paper-Doll Equipment**, shared Party Stash + 1-click Encumbrance Balance, **"Identify All / Sell Junk" Shop Filters**, quick-camp presets (`nspells[7]` loadouts + auto-bandage/torch renew), and scrollable mouse-wheel lists. |
| **Exploration & Pathing** (`moveparty.c`, `Data DD`, `Data TD2`) | Tile-by-tile click/numpad slog across 90x90 maps; punishing blind trial-and-error on traps/locks | **Smart Click-to-Move A* Pathing** (auto-stops immediately on sight of monsters, closed doors, traps, or `AP` triggers), mini-map with searchable player pins (`Data MD2` notes upgraded), and keyboard WASD/gamepad navigation. |
| **Campaign Ruleset Options** (`pref.c`, `age.c`, `misc.c`) | Brutal age-death surprises, single-slot risk before lethal `AP`s | **Campaign Difficulty Presets:** *Classic 1994 Strict* (authentic aging, permastats, manual saves) vs *Modern Balanced* (softened aging penalties, pre-encounter auto-save slot, optional rest stamina recovery). |

---

## E. Phased Vertical-Slice Roadmap & Open Decisions

### Milestone 0 - Foundation, Importer & Style Lock (Weeks 1 - 2)
- **M0.1 Sync & Reference Tooling:** Sync/inspect upstream `Realmz-Castle/realmz` (`4089d55`, 12 commits ahead) and build `fuzziqersoftware/resource_dasm` (`realmz_dasm`) locally.
- **M0.2 `realmz-exporter` Verification:** Export `City of Bywater` + base `Data ID`, `Data S`, `Data Race`, `Data Caste`, `Data AD` into normalized JSON + reference PNG/WAVs; write schema assertions verifying `Data ED`, `Data DD`, `Data LD`, `Data MD`, `Data BD`, and `struct` strides match `src/realmz_orig/` 100%.
- **M0.3 Style Bible Smoke Test on RTX 5060 Ti:** Run ComfyUI (`Illustrious-XL-v2.0`) on 3 portraits (`Portraits.rsrc`), 3 monsters (`Tacticals.rsrc`), and 1 terrain/dungeon autotile set; visually verify cohesion before writing full game UI.
- **Exit Gate:** `City of Bywater` JSON loads cleanly with zero schema errors, and 1 approved Style Bible image grid is locked.

### Milestone 1 - Playable Vertical Slice: *City of Bywater* Intro Slice (Weeks 3 - 6)
Build the complete game loop on a **tiny curated slice** before touching bulk asset generation:
- **Scope:**
  - Load the 6 pre-made party characters (`base/Realmz/Character Files/*`) + basic Party Sheet inspect.
  - **1 Region:** `City of Bywater` Level 0 (town + nearby wilderness) with A* click-move, `Data DD` AP triggers, **1 Shop** (`Data SD`), and **2 Encounters** (1 `Data ED` simple, 1 `Data ED2` complex).
  - **1 Tactical Battle (`Data BD`):** Grid movement, melee + missile, **5 representative spells** covering single-target, line/cone/7x7 `spellarea` AoE, buff/debuff (`CharCondition`), initiative bar, LOS/AoE preview overlays, floating damage, and `booty` treasure screen + JSON save/load.
- **Exit Gate:** End-to-end 10-minute playable vertical slice feels crisp, legible, and fun with placeholder/slice remastered assets. **Do NOT batch-generate 120 portraits / 240 tacticals until Milestone 1 playtests cleanly.**

### Milestone 2 - Full *City of Bywater* & Asset Batch Pipeline (Weeks 7 - 12)
- Scale the ComfyUI + Python palette/alpha/QA pipeline to produce all portraits, *City of Bywater* monsters/bosses, UI chrome, and scenario splash art.
- Implement remaining core systems: all 40 `CharCondition`s, full `Data S` spellbook, `Data TD2` thief lockpicking/traps, `temple.c` healing/resurrection, dungeon lighting (`Data DL`/`DDD`), and Classic/Remastered audio toggle (`libopenmpt` + converted `MADG`).

### Milestone 3 - Full 13-Scenario Parity & Community Polish (Weeks 13+)
- Validate `realmz-exporter` + `RealmzCore` VM against edge-case scenarios (`Half Truth` custom `Data Caste`, `War in the Sword Lands` populated `Data Spell` & `Custom 1..3 BD`, `Wrath of the Mind Lords` scale, `Layout` overworld grids, third-party scenarios).
- Add controller/Steam Deck bindings, scenario launcher UI, and clean packaging (keeping `RealmzCore` + BYO-scenario importer strictly separated if commercial/MIT separation is desired).

---

### 4 Key Creative Decisions for Kartik to Pick First

1. **Art Direction:** **Inked Dark-Fantasy Illustrated** (`128x128` tokens + `256x256` portraits, ideal for `Illustrious-XL-v2.0`) vs **Crisp Pixel Art / HD-2D** (`32x32`/`48x48` pixel-locked + 2.5D lighting) vs **Clean Retro HD Upscale**.
2. **Engine Stack:** **Unity 6 (C# + 2D URP / HD-2D)** (best lighting/VFX + existing Unity Antigravity skills) vs **Godot 4 (.NET C#)** (100% MIT, synergy with `Realmz-Castle/Realmz-Remake`) vs **TypeScript + PixiJS 8 + React** (fastest RPG menu/tooltip iteration + instant web/iPad play).
3. **Fidelity vs Reimagining:** **1994 Ruleset Faithful + Modern UX** (all 13 scenarios + fan scenarios playable verbatim) vs **Ruleset Overhaul** (rebalanced classes, modern action-point combat inspired by Realmz).
4. **Release & IP Posture:** **Free Open-Source Fan Remaster (`CC BY-NC-SA 4.0`, bundled scenarios)** vs **Clean-Room MIT/Commercial Engine + BYO-Data Importer** (no bundled Fantasoft assets) vs **Private Personal Build**.
