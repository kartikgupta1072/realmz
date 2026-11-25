---
name: realmz-m1-ship-gate
description: >-
  Run the end-to-end Milestone 1 (City of Bywater vertical slice) completion and
  7-gate release verification workflow. Use when preparing an M1 sign-off,
  validating a major gameplay/visual change, or checking release readiness.
---

# Realmz M1 Ship Gate

Execute the 7-gate verification sequence for **Milestone 1 (*City of Bywater*)** using `remaster/dev.ps1` and direct visual inspection. Review [the M1 completion checklist](./references/m1-checklist.md) for feature and gameplay sign-off criteria.

## Gate Sequence

1. **Workspace & Invariant Audit (`check`):**
   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 check
   ```
   Verifies `Remaster` branch safety, `RealmzCore` purity, 1-`MonoBehaviour`-per-file, `RealmzCore.dll` parity, zero em/en-dashes, and `realmz-foundry` parity.

2. **Core Simulation Build, Tests & DLL Sync (`sync-core`):**
   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 sync-core
   ```
   Builds `RealmzCore.csproj` in Release, passes all `RealmzCore.Tests`, and syncs `RealmzCore.dll` into `RealmzUnity/Assets/Plugins/RealmzCore/`.

3. **Python Exporter & Asset Foundry Suites (`test-python`):**
   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 test-python
   ```
   Passes `realmz-exporter` (`17/17`) and `asset-foundry` unit tests.

4. **Unity Strict Recompile (`unity-recompile`):**
   - If Unity Editor is open:
     ```powershell
     powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 unity-recompile
     ```
   - Require `0` errors and `0` warnings. (If Unity Editor is closed, Gate 5 batchmode tests perform compilation.)

5. **Unity EditMode + PlayMode Suites & Proof Capture (`unity-tests`):**
   Make sure no GUI Unity Editor holds the project lock before running batchmode:
   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 unity-tests -Mode All -CaptureProofs
   ```
   Confirm `0` failed, `0` skipped, and `PlayMode_Stage2_PerformanceBudget_120FramesUnder3Ms` passes (`< 3.0 ms` added delta, `>= 60 FPS`).

6. **Visual QA Gate (`realmz-visual-qa`):**
   Inspect the freshly captured PNGs in `C:\Users\karti\realmz\.scratch\proofs\` and `remaster/RealmzUnity/M1_Bywater_*_Preview.png` directly with `read_file` against the 5-pillar Art Director rubric.

7. **Standalone Windows Build & 8s Smoke Test (`build` + `smoke`):**
   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 build
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 smoke
   ```
   Confirm `remaster/builds/Realmz_M1/Realmz.exe` builds with `warnings=0`, stays alive past 8 seconds (`alive_at_8s=True`), and writes `0` errors/exceptions to `Player.log`.
