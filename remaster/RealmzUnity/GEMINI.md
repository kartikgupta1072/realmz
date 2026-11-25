# RealmzUnity - Presentation, Scene & Standalone Safety Rules

Scope: `C:\Users\karti\realmz\remaster\RealmzUnity\`

## 1. Standalone Player Safety
- Keep strictly ONE top-level `MonoBehaviour` per `.cs` file under `Assets/Scripts/`, named identically to its class (`ClassName.cs`, or `ClassName.Feature.cs` for partial classes) so Windows Standalone builds (`Realmz.exe`) never hit `level0` deserialization corruption.
- Never place Unity-specific logic inside `RealmzCore`; communicate strictly via `Assets/Plugins/RealmzCore/RealmzCore.dll`.

## 2. Performance Guardrails (`1920x1080` PC URP)
- Maintain `< 3.0 ms` added Stage 2 frame delta and `>= 60 FPS` (`<= 16.6 ms` total frame time).
- Zero per-frame GC allocations and zero `Instantiate`/`Destroy` inside combat or exploration update loops - use pre-allocated pools for projectiles, atmosphere VFX, and SFX voices (`RealmzMasterMixer`).
- Use `Graphics.DrawMeshInstanced` (`1023`/batch) + `16x16` chunk culling for foliage and dirty-flag texture uploads for `_FogOfWarTex`.

## 3. Cartography, Shaders & Art Import
- Lock all world sprites/tiles to `64 px/m` (`64 PPU`), `Point` filter, `Uncompressed`, `MipMap OFF`.
- Normal maps (`*_n.png`) use `+Y` OpenGL orientation (`NormalMap` texture type, `sRGB OFF`).
- `decal_*.png` and `dungeon_floor_grate_rusted.png` use `Center (0.5, 0.5)` pivot; `*_4f.png` strips use `Multiple` sprite slicing.
- Preserve `entry.BaseTint * alpha` across occlusion fades and animated `MaterialPropertyBlock` updates; use shader `_FlipX` (inverting `normalTS.x` in fragment) rather than negative `localScale.x`.

## 4. M1 HUD & Option 2 Victory Loot Contract
- Pure Selectable Loot: `4`-across compact cards, default selected, auto-sized inline stats (zero ellipsis clipping), green up-arrow `Hero` class-valid tags, gold up-arrow & heart-cross symbols beside `Lv`, Gold Coins icon, zero `MVP`, manual equip in `[P]`.
- Keep modal scroll/zoom lock active when `[P]`, Shop, Pause, or Victory modals are open.

## 5. Verification
- Do not run Unity batchmode while the Unity Editor GUI holds the project lock; use `unity.exe recompile --strict` when the Editor is open, or `remaster/dev.ps1 unity-tests` / `remaster/dev.ps1 proof` when idle.
- Save visual proof PNGs to `C:\Users\karti\realmz\.scratch\proofs\` (`REALMZ_PROOF_DIR`).
