# Realmz HD-2D Remaster - Standalone Antigravity Workspace

You are working inside Kartik Gupta's dedicated gamedev workspace for **Realmz HD-2D Remaster** (`C:\Users\karti\realmz`, `https://github.com/kartikgupta1072/realmz`), completely independent of `LifeTwin`.

---

## 1. Locked `#direct` Game Design & Architectural Invariants

1. **Creative North Star:** **Tactical Survival RPG** - classic *Realmz* Option B exploration encounters where geography, Day/Night torchlight cycles, biome/water ecology, party scouting, finite bandages/torches, and weather/resource tension make every journey meaningful.
2. **Fixed 6-AP Combat Contract:**
   - Base turn AP is strictly fixed at `BaseApPerTurn = 6` with zero Agility or Fatigue AP modifiers (`Agility` governs Initiative, Dodge AC, Ranged Hit, Scouting/Evade).
   - Carryover is capped at `MaxCarryoverAp = 2` (max `8` without gear) via `CombatActionEconomy` (`remaster/RealmzCore/Rules/CombatRules.cs`).
   - Only Equipment Sets and gear modify AP or Free Move (`Scout's Leather` `+1 FreeMove` / `+1 Max AP`, `Iron Vanguard` `+2 AC` / `1 AP Guard` / `+1 Max AP`).
3. **Victory Screen Contract:** Option 2 Pure Selectable Loot (`4`-across compact cards, default selected, inline stats, green up-arrow `Hero` class-valid tags, gold up-arrow & heart-cross symbols beside `Lv`, Gold Coins icon, zero `MVP`, manual equip in `[P]`).
4. **Core Architecture (`RealmzCore`):** `C:\Users\karti\realmz\remaster\RealmzCore\RealmzCore.csproj` (`netstandard2.1`) MUST maintain strictly ZERO `UnityEngine` or external NuGet dependencies. All Unity gameplay glue communicates with the compiled Release DLL at `remaster/RealmzUnity/Assets/Plugins/RealmzCore/RealmzCore.dll`.
5. **Standalone Player Safety:** Keep strictly one `MonoBehaviour` per `.cs` file named identically to its class so Windows Standalone builds (`Realmz.exe`) never hit `level0` deserialization corruption.
6. **Git Branch Safety:** All commits and pushes target `Remaster` / `origin/Remaster` only. Never modify or push to `main` (untouched at `21a951c`).

---

## 2. Workspace Layout, Scratch Policy & Hygiene

- **Canonical Design Docs & Audits:** Store persistent design docs, audits, and state snapshots in `remaster/docs/` (see [PROJECT-STATE.md](file:///C:/Users/karti/realmz/remaster/docs/PROJECT-STATE.md)).
- **Temporary Workspace Scratch (`.scratch/`):** Temporary art packs, proof screenshots (`.scratch/proofs/`), test XMLs/logs/scripts (`.scratch/runs/`), and agent task prompts (`.scratch/prompts/`) belong strictly inside gitignored `C:\Users\karti\realmz\.scratch\`. Never write Realmz files into `C:\Users\karti\LifeTwin`.
- **Formatting & Subprocess Hygiene:** Enforce strictly zero em-dashes (`\u2014`) and zero en-dashes (`\u2013`) in every file, code comment, log, and message (use ASCII hyphens `-` or ` - ` only). Use `CREATE_NO_WINDOW = 0x08000000` on Windows Python `subprocess` calls.

---

## 3. Local Toolchain & Verification Commands (`Home-PC`)

- **.NET SDK 8.0.428** (`C:\Users\karti\AppData\Local\Microsoft\dotnet\dotnet.exe`):
  - Core VM tests: `C:\Users\karti\AppData\Local\Microsoft\dotnet\dotnet.exe test C:\Users\karti\realmz\remaster\RealmzCore.Tests\RealmzCore.Tests.csproj -c Release`
- **Unity `6000.0.85f2`** (`C:\Unity\Editors\6000.0.85f2\Editor\Unity.exe`, Unity Personal license active) & **Unity CLI `1.0.1`** (`C:\Users\karti\AppData\Local\Unity\bin\unity.exe`):
  - Unity project: `C:\Users\karti\realmz\remaster\RealmzUnity` (primary scene `Assets/Scenes/M1_Bywater_Slice.unity`)
  - Check running editor: `C:\Users\karti\AppData\Local\Unity\bin\unity.exe status --project-path C:\Users\karti\realmz\remaster\RealmzUnity --json`
  - Strict recompile: `C:\Users\karti\AppData\Local\Unity\bin\unity.exe recompile --strict --project-path C:\Users\karti\realmz\remaster\RealmzUnity`
  - Proof screenshot override env var: `REALMZ_PROOF_DIR` (defaults to `C:\Users\karti\realmz\.scratch\proofs`)
- **Realmz Asset Foundry & Cartography:**
  - CLI: `realmz-foundry` (`C:\Users\karti\realmz\remaster\asset-foundry\bin\realmz-foundry.py` mirrored with SHA-256 parity to `C:\Users\karti\.local\bin\realmz-foundry.py`)
  - Tests: `C:\Users\karti\realmz\tools\asset-foundry\.venv\Scripts\python.exe -m unittest discover -s C:\Users\karti\realmz\remaster\asset-foundry\tests -v`
  - `64 px/m` Cartography pipeline: `C:\Users\karti\realmz\remaster\art-pipeline\cartography`
- **Scenario Exporter & Classic Resource Tooling:**
  - Exporter: `C:\Users\karti\realmz\remaster\realmz-exporter\export_realmz.py` (`Python 3`, `17/17` `unittest`)
  - `resource_dasm` (`v2026-09-03`): `C:\Users\karti\.local\tools\resource_dasm\bin\release-windows`
- **Standalone Build Output:** `C:\Users\karti\realmz\remaster\builds\Realmz_M1\Realmz.exe`

---

## 4. Workspace Automation & Quality Gates

- **Unified Dev CLI (`remaster/dev.ps1`):** Run `powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 <command>` for `status`, `check` (architectural/hygiene lints), `sync-core` (Release build + `RealmzCore.Tests` + DLL copy/hash check), `test-python`, `test-fast`, `unity-recompile`, `unity-tests`, `proof`, `build`, `smoke`, and `test-all`.
- **Automated Lifecycle Hooks (`.agents/hooks.json`):** `SessionStart` injects live branch, `RealmzCore.dll` sync status, and Unity Editor state; `PreToolUse` guards block `main` branch mutations, writes to `LifeTwin`, em/en-dashes, `UnityEngine` refs inside `RealmzCore`, and multi-`MonoBehaviour` files in `RealmzUnity/Assets/Scripts/`.
- **Project Skills (`.agents/skills/`):**
  - `realmz-m1-ship-gate`: End-to-end M1 (*City of Bywater*) completion and 7-gate release verification.
  - `realmz-core-sync`: Pure `RealmzCore` build, unit test, and Unity plugin DLL sync workflow.
  - `realmz-visual-qa`: In-engine proof capture (`REALMZ_CAPTURE_PROOFS=1`) and 5-pillar Art Director visual inspection.
  - `realmz-art-pipeline`: `64 px/m` Cartography and `realmz-foundry` staging, normal-map, and import verification workflow.
