---
name: realmz-visual-qa
description: >-
  Capture in-engine Realmz screenshots in .scratch/proofs/ and inspect them
  directly with read_file against the 5-pillar Grade-A Art Director rubric. Use
  after shader, lighting, terrain, prop, HUD, or art-import changes.
---

# Realmz Visual QA & Art Director Loop

Never sign off visual, shader, UI, or cartography work from test counts alone. Capture live renders and inspect every PNG directly with `read_file` against [the 5-pillar Art Director rubric](./references/art-director-rubric.md).

## Capture Paths

### Option A: Full PlayMode Proof Suite (`M1_Bywater_Slice.unity`)
Requires no GUI Unity Editor holding the project lock:
```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 proof
```
This sets `REALMZ_CAPTURE_PROOFS=1` and `REALMZ_PROOF_DIR=C:\Users\karti\realmz\.scratch\proofs`, runs `RealmzM1PlayModeTests`, and outputs proof PNGs to `.scratch/proofs/` plus `remaster/RealmzUnity/M1_Bywater_*_Preview.png`.

### Option B: Targeted PlayMode Filter (Faster Iteration)
```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 unity-tests -Mode PlayMode -CaptureProofs -Filter "PlayMode_Stage2"
```

### Option C: Cartography 18x18 Diorama Proof Scene
For isolated shader/foliage/water iteration via `CartographyCaptureTools.BatchCaptureProofFromCli`:
```powershell
& "C:\Unity\Editors\6000.0.85f2\Editor\Unity.exe" -batchmode -quit -projectPath C:\Users\karti\realmz\remaster\RealmzUnity -executeMethod Realmz.EditorTools.Cartography.CartographyCaptureTools.BatchCaptureProofFromCli -cartoOut C:\Users\karti\realmz\.scratch\proofs\carto_diorama_proof.png -logFile C:\Users\karti\realmz\.scratch\runs\carto_capture.log
```

## Inspection Protocol

1. List captured PNGs in `C:\Users\karti\realmz\.scratch\proofs\` and open every relevant screenshot using `read_file`.
2. Score all 5 pillars in [art-director-rubric.md](./references/art-director-rubric.md) (`Terrain & Cliffs`, `Props & Ground Integration`, `Characters & Scale`, `Lighting & Atmosphere`, `UI Typography & Readability`).
3. Classify any visual issue as **P0** (broken geometry, skybox cracks, unreadable water/fog, alpha holes), **P1** (text clipping, wrong role tags, noisy grid stamps, glare/tint mismatch), or **P2** (minor margin/occlusion polish).
4. Do not claim Grade-A visual sign-off until zero P0 and zero P1 defects remain in freshly captured proofs.
