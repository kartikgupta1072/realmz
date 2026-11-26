---
name: realmz-delegation
description: >-
  Coordinate sub-workers (invoke_subagent / run_workflow) and local GPU models
  (remaster/dev.ps1 local-ai) for Realmz HD-2D game development. Use whenever a
  task spans 2+ independent repo tracks, needs bulk code/audit research,
  processes large Unity logs or batch screenshots, or runs ComfyUI/Blender on
  the 8 GB RTX 5060 Ti.
---

# Realmz Game-Dev Delegation & Local GPU Routing

Keep the lead conversation focused on architecture, gameplay invariants, diff review, and final visual/playability sign-off while delegating bounded execution and offloading bulk triage to local GPU models. See [lane-ownership.md](./references/lane-ownership.md) for exact path boundaries and prompt templates.

## Mandatory Pre-Execution Routing Gate

Check these thresholds BEFORE starting sequential work on the lead model:
- **`2+` disjoint lanes (`RealmzCore`, `RealmzUnity`, `Exporter/Art`, `Research`):** MUST launch parallel `invoke_subagent` workers (`Model="flash"` for bounded edits/tests; avoid `flash_lite` where local 14B can handle read/triage) instead of editing everything sequentially in one turn.
- **`4+` independent units/files:** MUST fan out via `run_workflow`.
- **Large log `> 50` lines / long file summary / flavor text draft:** MUST run `dev.ps1 local-ai` (`summarize` and `draft-content` default to `--tier heavy` `qwen2.5:14b-instruct`; `log-triage` defaults to `--tier fast` `qwen2.5:7b-instruct`) via standalone zero-daemon `llama.cpp` before burning cloud tokens.
- **`>= 3` screenshots or sprites to screen:** When vision backend (`llama-mtmd-cli` + `mmproj` or Ollama fallback) is ready, run `dev.ps1 local-ai vision-qa <path> --mode proof|sprite|sheet --json` first before direct `read_file` sign-off on flagged/final PNGs.
- **GPU handoff:** `llama.cpp` releases 100% of VRAM on exit; run `dev.ps1 local-ai evict` if Ollama was used before ComfyUI, Blender, Hunyuan3D, or Unity PlayMode proof captures.

## 1. Sub-Worker Delegation Rules

### When to Delegate vs. Execute Directly
- **Execute directly:** Tiny single-file edits (`<= 10` lines), quick 1-2 tool lookups, or sequential edits tightly coupled to Unity scene state.
- **Delegate (`invoke_subagent`):**
  - 2+ independent tracks with disjoint files (e.g., `RealmzCore` rules + `realmz-exporter` data + `RealmzUnity` HUD/shaders).
  - Read-only deep audits (`TypeName="research"`) across multiple systems or classic `src/realmz_orig/` C90 reference checks.
  - Self-contained feature/test/build lanes (`TypeName="self"` or `"research"`).
- **Fan-out (`run_workflow`):** 4+ parallel independent units (e.g., auditing all 13 classic scenarios or batch-generating/QA-ing M2 asset packs).

### Model Tier Selection
- **`flash`:** Bounded unit tests, single-module edits, exporter updates, and test/lint runners.
- **`inherit` / `pro` (Lead):** Cross-layer architecture (`RealmzCore` <-> `RealmzUnity` <-> `StreamingAssets`), HLSL shader/normal-map math, game-feel/UX tuning, resolving failed retries, and final Art Director sign-off.
- **Local `llama.cpp` (`dev.ps1 local-ai`):** C90/doc summaries and M2 content drafts (`qwen2.5:14b-instruct` `--tier heavy`), Unity log triage (`qwen2.5:7b-instruct`).

### Hard Concurrency Locks
- **Unity Single-Lock Rule:** Only **one** worker may run `Unity.exe` batchmode tests/builds or control the open Editor at a time (`UnityLockfile`). Other workers may edit pure C# (`RealmzCore`), Python (`realmz-exporter`, `art-pipeline`), or docs concurrently, and **one** Unity-owner worker runs `dev.ps1 sync-core` -> `unity-tests` -> `build` -> `smoke` last.
- **DLL Sync Order:** Always run `dev.ps1 sync-core` before Unity tests or builds whenever `remaster/RealmzCore/` changed.

## 2. Local GPU & Offline Model Offload (`remaster/dev.ps1 local-ai`)

Primary backend is standalone Vulkan `llama.cpp` (`C:\Users\karti\.local\tools\llama.cpp`, zero background daemon, instant VRAM release on exit, optional Ollama fallback at `127.0.0.1:11434`). Use global `local-ai web-research "<query>"` for Unity/URP/Cinemachine documentation lookups.

| Task | Local Command | Model | When to Escalate to Lead Cloud Model |
| :--- | :--- | :--- | :--- |
| **GPU / Backend Check** | `dev.ps1 local-ai status` | - | Check `llama.cpp` + GGUFs + free VRAM |
| **Batch Screenshot / Sprite QA** | `dev.ps1 local-ai vision-qa <path> --mode proof\|sprite\|sheet --json` | `qwen2.5vl:3b` (`mmproj` or Ollama) | Always inspect final proof PNGs directly with `read_file` for Grade-A sign-off |
| **Large Unity / Build Log Triage** | `dev.ps1 local-ai log-triage <log_path> --json` | `qwen2.5:7b-instruct` (or `--tier heavy` `14b`) | Any non-trivial C#/shader/Unity failure needing root-cause repair |
| **Classic C90 / Doc Summary** | `dev.ps1 local-ai summarize <file> --focus "..."` | `qwen2.5:14b-instruct` (`--tier heavy` default) | Exact line-level code edits or binary struct parsing |
| **Flavor Text / Notes Draft** | `dev.ps1 local-ai draft-content --kind item\|monster\|npc\|encounter\|changelog --spec "..."` | `qwen2.5:14b-instruct` (`--tier heavy` default) | Final narrative pass |
| **VRAM Cleanup** | `dev.ps1 local-ai evict` | - | Releases any residual Ollama VRAM before ComfyUI/Blender/Unity |

### 8 GB RTX 5060 Ti VRAM Handoff Rule
- Single `log-triage`, `summarize`, `draft-content`, and single-image `vision-qa` calls send `keep_alive: 0` automatically.
- Multi-image `vision-qa` directory batches evict Ollama models in `finally:` on completion.
- **Always run `dev.ps1 local-ai evict` before launching ComfyUI, Blender, Hunyuan3D, or Unity PlayMode proof captures** so the 8 GB GPU has full headroom.
- Never offload complex C#/HLSL debugging, architectural refactors, or final Art Director visual sign-offs to local 3B-14B models.
