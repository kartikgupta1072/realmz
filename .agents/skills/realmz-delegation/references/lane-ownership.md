# Realmz Lane Ownership & Delegation Templates

## 1. Disjoint Game-Dev Lanes

| Lane | Owned Paths | Typical Subagent | Lock / Sync Notes |
| :--- | :--- | :--- | :--- |
| **Core Simulation** | `remaster/RealmzCore/`, `remaster/RealmzCore.Tests/` | `self` (`flash` / `inherit`) | Zero `UnityEngine`/NuGet; must run `dev.ps1 sync-core` after edits |
| **Unity Presentation & Tests** | `remaster/RealmzUnity/Assets/Scripts/`, `Shaders/`, `Editor/`, `Tests/` | `self` (`inherit` for HLSL/UX, `flash` for tests) | **Exclusive `Unity.exe` lock owner**; wait for Core/Exporter/Art lanes before `unity-tests` |
| **Scenario Exporter & Data** | `remaster/realmz-exporter/`, `remaster/extracted/`, `Assets/StreamingAssets/bywater_m1/` | `self` (`flash`) | Keep `extracted/bywater_m1` and `StreamingAssets/bywater_m1` byte-identical |
| **Art Pipeline & Foundry** | `remaster/art-pipeline/`, `remaster/asset-foundry/`, `.scratch/<pack>/`, `Assets/Art/Cartography/` | `self` (`flash`) + `local-ai` | Run `dev.ps1 local-ai evict` before ComfyUI/Blender; `64 px/m`, `Sunlit-128`, `+Y` normals |
| **Read-Only Audits & C90 Reference** | `src/realmz_orig/`, `remaster/docs/` | `research` (`flash` / `flash_lite` / `inherit`) | Strictly read-only; cite exact `path:line` and observed symbols |

## 2. Subagent Brief Template

When launching parallel `invoke_subagent` workers, specify:
1. **Owned Paths (Exclusive):** Exact files/directories this worker may edit (or `READ-ONLY`).
2. **Do Not Touch:** Explicitly exclude `Unity.exe` batchmode if another lane owns the Unity lock.
3. **Invariants:** Remind worker of Fixed 6-AP contract, zero `UnityEngine` in `RealmzCore`, 1 `MonoBehaviour` per file, and zero em/en-dashes.
4. **Verification Command & Return Format:** Exact test command to run and concise summary (`files changed`, `test counts`, `blockers`).
