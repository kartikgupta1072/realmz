# Realmz Remastered - Stage 2 Visual & Tactical Gameplay Re-Audit (`C:\realmz`)

**Auditor:** W-263 (Wave C: Independent Principal 2.5D Pixel-Art Director & Tactical RPG Game Designer)  
**Scope:** Strictly read-only verification of `C:\realmz` at `Remaster` HEAD `40f7afe78f29b570bd3fce4ae2e5152d7f41f35b`, direct visual inspection of all 8 Stage 2 screenshots + 5 repaired source PNGs, EditMode/PlayMode XMLs, `Player.log`, Windows Standalone build footprint, code purity, and all 6 Unity-native gameplay pillars.  
**Baseline:** [realmz_art_director_audit.md](file:///c:/Users/karti/LifeTwin/scratch/realmz_art_director_audit.md) & [realmz_unity_gameplay_master_plan.md](file:///c:/Users/karti/LifeTwin/scratch/realmz_unity_gameplay_master_plan.md)

---

## Executive Verdict: **Grade A- (Pass with 5 Targeted Visual Polish Fixes for Clean Grade A)**

Stage 2 delivers a **transformative leap** over M1. Every structural disaster flagged in the prior art-director audit has been resolved: all 5 hollowed alpha-bugged buildings/props/trees are solid, the `W-255` 45+45 biome cover/decal pack replaces suburban fences and bonsai oak trees across arenas, shopkeepers stand at true `1.20f` human scale, normal-safe `_FlipX` keeps lighting consistent, Night Combat restores warm torch chiaroscuro with moonlight rim separation, real `IsWater` rivers eliminate the combat river checkerboard, `Orc Warrior` (`18-24 HP`) out-tanks `Giant Frog` (`15-19 HP`), and all 6 Unity-native gameplay pillars (Cinemachine 3, 3-state Fog-of-War + Save v4 + 1440m solar/lunar arc, pooled Combat/Atmosphere VFX, `RealmzMasterMixer` + spatial beds, Destructible Props, Victory XP tween + Option 2 Loot + `RealmzBalanceCatalog`) are cleanly wired and passing all tests at `0.559 ms/frame` (`1788.3 FPS`).

Two **P0** rendering defects and three **P1** HUD/overlay polish items observed directly in the Stage 2 proof PNGs keep Terrain & Cliffs (`B+`) and UI & Readability (`B+`) just short of an unqualified **Grade A**:
1. **P0 - Open `0.025m` Cobble-to-Plaza Mesh Slits Showing Skybox Horizon:** `CobbleElevation = 0.025f` against `PlazaElevation = 0.00f` falls inside `BywaterElevationBuilder.CheckAndEmitCliff`'s `<= 0.03f` deadzone (`BywaterElevationBuilder.cs:387`), opening 1px horizontal pale-blue skybox cracks across `stage2_proof_01`, `stage2_proof_04`, `stage2_proof_05`, and `M1_Bywater_Deployment_Proof`.
2. **P0 - Dusk River Unattenuated Shimmer & Stark White Stair-Step Foam:** `CartographyWater.shader` (`L178, 185, 190-191`) adds full-bright white sparkles and shoreline foam without `mainLight.color` / `SampleSH` attenuation, turning Dusk river water (`stage2_proof_02_riverbank_dusk_fog.png`) nearly pitch-black with bright white salt-and-pepper speckles and jagged icy-white bank blocks.
3. **P1 - Victory `Bow` Card Clip (`1d6 Dmg | Rng 8 | Wt...`):** `BywaterHudController.UxOverhaul.cs:2453-2454` uses fixed `15f` font + `Ellipsis` on `LootTile` stats (`stage2_proof_05_victory_loot_hud.png`).
4. **P1 - Role Tag Misclassification (`Tank` on Rogue `Vex`, `Goblin`, `Giant Frog`):** `BywaterHudController.cs:2894` tags any unit with `Ac >= 14` as `"Tank"` across all combat proofs.
5. **P1 - Opaque Cream Step Squares on Hovered Move Path:** `BywaterGridOverlays.cs:53, 1039` draws `0.42m` solid pale-cream quads (`alpha 0.76`) under `1 AP` pills (`stage2_proof_03_biome_forest_combat.png`).

### Before vs. After Visual Pillar Scorecard

| Visual Pillar | Prior Grade (M1) | Stage 2 Grade | Key Stage 2 Gains | Remaining Blocker to Clean A |
| :--- | :---: | :---: | :--- | :--- |
| **1. Terrain & Cliff Geometry** | **C-** | **B+** | Native `64 px/m` `128x128` ground tiles, cliff vertical UV jitter + `0.45h` crease AO, real `-0.28f` `IsWater` combat rivers, feathered cobble edges | **P0:** `0.025m` `CobbleElevation` skybox seam (`BywaterElevationBuilder.cs:387`); **P0:** Dusk river unattenuated glint/foam (`CartographyWater.shader:178,190-191`) |
| **2. Props, Obstacles & Ground Integration** | **D** | **A-** | All 5 hollowed PNGs & glare props repaired; 30 new biome props (`boulder_moss`, `broken_archway`, `fallen_hollow_log`, `oil_keg_cluster`, `thorn_bramble_thicket`), normal-safe `_FlipX`, tint/scale jitter, `0.5x` ground decals, `RootCollar` grass rings | **P2:** `RoadAmbush` odd `StoneFence` tiles still place suburban `StoneFenceH`; `Goblin` behind `fallen_hollow_log` not occlusion-faded (`stage2_proof_03`) |
| **3. Characters, Monsters & Scale Consistency** | **B-** | **A-** | Shopkeeper scale `2.0f -> 1.20f` matches Aldric (`stage2_proof_01`); painterly `portrait_npc_priestess.png`; `_FlipX` `MaterialPropertyBlock` facing; `Orc Warrior` (`18-24 HP`) vs `Giant Frog` (`15-19 HP`) | **P2:** Unit `walk_attack_*` sprite sheets untouched in Stage 2 (M2 polish item for `Liora` Walk/Attack staff drift) |
| **4. Lighting, Color & Atmospheric Readability** | **C+** | **A-** | Night Combat chiaroscuro (`ambient 0.98/1.08`, `contrast 15`, `ACES`) + `_GlobalNightRimStrength = 0.55f`; continuous 1,440-min Solar/Lunar arc; 3-state Fog-of-War; pooled smoke, embers, fireflies, rain, combat VFX | **P1:** Ties into Dusk water tint/glint attenuation; 3 PlayMode tests log URP 2048 shadow-atlas downscale warning across 6 punctual shadow maps |
| **5. UI Typography, Icons & Visual Polish** | **B-** | **B+** | Fixed `Short Swd`, `Giant Fro`, `...for 12 t`, NPC prompt wrap (`330x46`), empty `SP -` rows; `20%` alpha corner-tick `L`-brackets; Option 2 Loot + `0.65s` XP tween | **P1:** `Bow` loot card `Wt...` clip (`stage2_proof_05`), `Vex`/`Goblin`/`Frog` `Tank` mislabel, opaque cream hover-path squares (`stage2_proof_03`) |

---

## 1. Git, Purity, Build & Test Verification (Read-Only)

| Check | Expected | Observed Result | Status |
| :--- | :--- | :--- | :---: |
| **Active Branch & `HEAD`** | `Remaster` at `40f7afe7...` | `Remaster` at `40f7afe78f29b570bd3fce4ae2e5152d7f41f35b` | **PASS** |
| **`main` Branch Protection** | Untouched at `21a951ce3356a01306d0f12e5c0d0c76670387e8` | `21a951ce3356a01306d0f12e5c0d0c76670387e8` | **PASS** |
| **Working Tree Cleanliness** | Clean | `git status --short` empty (0 dirty files) | **PASS** |
| **Stage 2 Diff Scope** | `14f7d04..40f7afe` | `421 files changed, 29394 insertions(+), 1073 deletions(-)` | **PASS** |
| **`RealmzCore` Purity** | Zero `UnityEngine`/`UnityEditor` refs | 0 occurrences across all 21 `.cs` files + `RealmzCore.csproj` (`netstandard2.1`) | **PASS** |
| **1-MonoBehaviour-Per-File** | 1 top-level `MonoBehaviour` per `.cs` | 0 violations across all 19 `MonoBehaviour` files in `Assets/Scripts/` (`BywaterHudController` uses 3 partial files + nested private relays `HoverRelay` / `PointerActionRelay`) | **PASS** |
| **Standalone Build Footprint** | `Realmz.exe` (`>= 191 MB`) | Found at `C:\realmz\remaster\builds\Realmz_M1\Realmz.exe`: **189 files, `206,494,543` bytes (`196.93 MB`)**. `Realmz_Data/Managed/RealmzCore.dll` (`362,496` bytes), `Assembly-CSharp.dll`, `level0`, `sharedassets0.assets` all built `11/24/2026 11:14:37-38 PM` (note: `Realmz.exe` stub kept `9:46 PM` timestamp from incremental build; `C:\realmz\builds\Realmz_M1\` path does not exist, builds live under `C:\realmz\remaster\builds\Realmz_M1\`) | **PASS** |
| **EditMode Suite** | Pass all tests | `61 / 61` passed (`0` failed, `0` skipped, `18.114s`): `CartographyEditorTests` `24/24`, `RealmzM1EditorTests` `37/37` | **PASS** |
| **PlayMode Suite** | Pass all tests + `< 3.0 ms` / `>= 60 FPS` | `9 / 9` passed (`0` failed, `0` skipped, `27.730s`); `PlayMode_Stage2_PerformanceBudget_120FramesUnder3Ms` measured **`avg=0.559 ms/frame (1788.3 FPS)`** on RTX 5060 Ti | **PASS** |
| **Player Logs** | `0` errors / exceptions | `Player.log` (`11/24/2026 11:15:04 PM`, 27 lines) & `standalone_smoke_player.log` (28 lines): **0 errors, 0 exceptions, 0 shader warnings**. (`Player-prev.log` & 3 PlayMode cases logged non-fatal URP `2048x2048` punctual shadow atlas downscale notice) | **PASS** |

---

## 2. Direct Visual Inspection of Stage 2 Proof Screenshots

### 2.1 [stage2_proof_01_town_day.png](file:///c:/Users/karti/LifeTwin/scratch/stage2_proof_01_town_day.png) & [M1_Bywater_Explore_Preview.png](file:///C:/realmz/remaster/RealmzUnity/M1_Bywater_Explore_Preview.png)
*(Captured `11/24/2026 11:13:18-31 PM`, `1600x900`, `City of Bywater (10, 9)`, `Day 1 - 08:22 AM (Morning)`, `SAFE`, `Clear | Vis 13`)*
- **Verified Improvements:**
  - `building_temple.png` (top-left) and the repaired timber/thatch buildings + `town_notice_board` (top-right) render completely solid with zero interior alpha holes; direct check of [building_temple.png](file:///C:/realmz/remaster/RealmzUnity/Assets/Art/Cartography/Buildings/building_temple.png), [building_apothecary.png](file:///C:/realmz/remaster/RealmzUnity/Assets/Art/Cartography/Buildings/building_apothecary.png), [tree_pine_b.png](file:///C:/realmz/remaster/RealmzUnity/Assets/Art/Cartography/trees/tree_pine_b.png), [prop_barrels_crates.png](file:///C:/realmz/remaster/RealmzUnity/Assets/Art/Cartography/Props/prop_barrels_crates.png), and painterly [portrait_npc_priestess.png](file:///C:/realmz/remaster/RealmzUnity/Assets/Art/Cartography/Portraits/portrait_npc_priestess.png) confirms clean Part 1 repairs.
  - Master Blacksmith Borin stands at `1.20f` scale (`BywaterWorldRenderer.cs:1451`), reading naturally at human height beside Sir Aldric.
  - World prompt `[E / Click] Trade: Master Blacksmith Borin` fits cleanly on a single line (`330x46` container at `BywaterWorldRenderer.cs:1514`); bottom party portraits omit empty `SP -` tracks on non-casters (`Aldric`, `Kaelen`, `Vex`).
  - Cobblestone roads feather cleanly against grass (`CobbleElevation = 0.025f`) with crisp `128x128` stone/grass micro-detail.
- **Remaining Visual Defects:**
  - **P0 Cobble-to-Plaza Mesh Seam:** Horizontal 1px pale-blue/grey lines (`y=317-318`, sampled RGB `(175, 182, 186)` to `(189, 185, 186)`) appear below Borin, right of Aldric, above the left cottage roof, and near the lower cobble path wherever `CobbleElevation (0.025f)` meets `PlazaElevation (0.000f)` without a cliff skirt (`BywaterElevationBuilder.cs:387`).

### 2.2 [stage2_proof_02_riverbank_dusk_fog.png](file:///c:/Users/karti/LifeTwin/scratch/stage2_proof_02_riverbank_dusk_fog.png)
*(Captured `11/24/2026 11:13:31 PM`, `1600x900`, `City of Bywater (53, 62)`, `Day 3 - 06:15 PM (Dusk)`, `HIGH`, `Clear | Vis 8`)*
- **Verified Improvements:**
  - 3-State Fog-of-War works clearly: `Unseen` tiles shroud the outer perimeter in dark indigo-black, `Explored` memory tiles preserve muted slate silhouettes of the southwest pine grove and eastern bank, and `CurrentlyVisible` (`Vis 8`) illuminates Aldric's clearing.
- **Remaining Visual Defects:**
  - **P0 Pitch-Black Dusk Water + White Speckles + Jagged White Foam:** Because `CartographyWater.shader:178, 190-191` adds `_ShimmerColor.rgb * glint * tdLm.rgb` and lerps shoreline foam to `_ShimmerColor.rgb * 0.88h * tdLm.rgb` without multiplying by `mainLight.color` or `SampleSH`, the Dusk river renders almost black (`#04070D`) speckled with bright 1px white glints (`RGB ~141, 143, 161` in fog, brighter in view) and framed by jagged, high-contrast icy-white shoreline stair-steps (`step(..., waveEdge)` at `L185`). Adding a subtle warm/indigo sky Fresnel tint and modulating glint/foam by scene light will immediately restore Dusk water cohesion.

### 2.3 [stage2_proof_03_biome_forest_combat.png](file:///c:/Users/karti/LifeTwin/scratch/stage2_proof_03_biome_forest_combat.png) & [M1_Bywater_Combat_Preview.png](file:///C:/realmz/remaster/RealmzUnity/M1_Bywater_Combat_Preview.png)
*(Captured `11/24/2026 11:13:20-30 PM`, `1600x900`)*
- **Verified Improvements:**
  - **Biome Authenticity:** `DeepWoods` (`stage2_proof_03`) showcases `fallen_hollow_log` with `decal_fallen_leaves`, `gnarled_dead_tree`, `thorn_bramble_thicket` (replacing miniature oak trees), and `boulder_moss`; `RuinsOutskirts` (`M1_Bywater_Combat_Preview`) showcases `broken_archway`, `ruined_wall_corner`, `rubble_pile_a/b`, and `oil_keg_cluster`. Cliff faces have zero horizontal strata wallpaper.
  - **Grid & Balance:** Corner-tick `L`-brackets (`20%` alpha) replace noisy solid cyan tile borders; `Orc Warrior` (`20/20`, `23/23`), `Goblin` (`10/10`, `8/8`), `Goblin Archer` (`7/7`, `6/6`), and `Giant Frog` (`16/16`) match visual weight; `[Theron] Melee vs Giant Frog` hover tooltip renders cleanly with zero clipping (`M1_Bywater_Combat_Preview`).
- **Remaining Visual Defects:**
  - **P1 Opaque Cream Path Squares (`stage2_proof_03`):** `HoverPathSafeColor` (`0.42m` quads at `alpha 0.76`) renders 4 opaque cream/tan squares beneath the `1 AP` path pills (`BywaterGridOverlays.cs:53, 1039`).
  - **P1 Role Tag Misclassification:** Initiative ribbon tags `Vex` (`HP 9/10` / `10/10`) and `Goblin` as `Tank` (`BywaterHudController.cs:2894`).
  - **P2 Minor Occlusion & Margin:** In `stage2_proof_03` bottom-right, a goblin/archer immediately north of `fallen_hollow_log` is half-occluded behind the log (`IsOccludingPropKind` excludes `FallenHollowLog` at `BywaterWorldRenderer.cs:2892-2931`), and `Combat Log [L]` line 3 (`1 total damage.` / `0 total damage.`) sits flush against the panel's left border (`BywaterHudController.cs:4873-4914`).

### 2.4 [stage2_proof_04_night_combat_vfx_pitch.png](file:///c:/Users/karti/LifeTwin/scratch/stage2_proof_04_night_combat_vfx_pitch.png) & [M1_Bywater_Deployment_Proof.png](file:///C:/realmz/remaster/RealmzUnity/M1_Bywater_Deployment_Proof.png)
*(Captured `11/24/2026 11:13:34-43 PM`, `1600x900`)*
- **Verified Improvements:**
  - **Night Chiaroscuro & Pitch (`stage2_proof_04`):** `64 deg` `[T]` tactical pitch gives crisp overhead readability; deep nocturnal grass/cliffs (`ambientIntensity = 1.08`, `contrast = 15`, `ACES`) contrast richly against the warm central brazier/lantern pool and `PlayKegExplosion` impact glow while `_GlobalNightRimStrength` (`0.55f`) outlines `Orc Warrior` (`24/24`, `18/18`), `Goblin`, and `BoulderMoss` on the unlit flank.
  - **Road Ambush Dressing & HUD:** `palisade_stakes`, `cart_wreck`, `oil_keg_cluster`, `hay_bale`, and `BarrelsCrates` read clearly; `Short Swd` fits cleanly without truncation; `Deployment_Proof` shows crisp cyan deployment tiles, yellow swap highlight, and `Start Battle [Space]`.
- **Remaining Visual Defects:**
  - **P0 Horizontal Skybox Hairline Across Arena (`y=530` in `stage2_proof_04`, `y=585` in `Deployment_Proof`):** Same `0.025m` cobble-to-grass vertical gap (`CheckAndEmitCliff` deadzone) slices visually across the entire south road edge (`RGB (176, 191, 206)` at Night, `(126..188)` at Day).
  - **P2 Surface Oil Decal Border & Town Fence on Road:** In `stage2_proof_04`, the `surface_oil` cell still presents a crisp rectangular sticker edge; in `Deployment_Proof`, `StoneFence` on an odd tile still spawns `stone_fence_h` flower planters directly behind `OilKegCluster` (`CartographyWorldData.cs:3743-3753`).

### 2.5 [stage2_proof_05_victory_loot_hud.png](file:///c:/Users/karti/LifeTwin/scratch/stage2_proof_05_victory_loot_hud.png)
*(Captured `11/24/2026 11:13:27 PM`, `1600x900`)*
- **Verified Improvements:**
  - Clean Option 2 Pure Selectable Loot modal: `VICTORY!`, `Total Party XP: +185 | Rounds: 1`, gold coins icon + `+6g | Total: 406g`, 6 hero cards with smooth XP fill (`+31 XP (31/200)` / `+30 XP (30/200)`), `Loot` header, `4`-across pre-checked loot cards, green upgrade arrows (`Kaelen`, `Aldric`), zero `MVP`, single `Take Selected (13)` CTA (no `Take & Equip`).
- **Remaining Visual Defects:**
  - **P1 `Bow` Card Stat Ellipsis:** Bottom-left `Bow` card displays `1d6 Dmg | Rng 8 | Wt...` because `statsLbl` in `BywaterHudController.UxOverhaul.cs:2453-2454` lacks `enableAutoSizing = true`.
  - **P0/P2 Background Details:** Same `0.025m` cobble hairline visible behind right modal edge; sharp cyan skybox corner exposed top-left off the arena cliff edge.

---

## 3. Exact P0 / P1 / P2 Visual Polish Targets (To Reach Clean Grade A)

| Priority | Observed Defect | Root Cause (Exact File & Line) | Concrete Fix |
| :---: | :--- | :--- | :--- |
| **P0** | Horizontal 1px bright skybox hairline across cobble/grass borders (`stage2_proof_01`, `04`, `05`, `Deployment_Proof`) | `CartographyWorldData.cs:171` (`CobbleElevation = 0.025f` vs `PlazaElevation = 0.00f`) + `BywaterElevationBuilder.cs:219, 387` (`CheckAndEmitCliff` skips when `eHere <= eNeighbor + 0.03f`) | Set `CobbleElevation = PlazaElevation` (`0.00f`) so roads splat-blend flat, **and** replace `eHere <= eNeighbor + 0.03f` in `BywaterElevationBuilder.cs:387` with `Mathf.RoundToInt(eHere * 1000f) <= Mathf.RoundToInt(eNeighbor * 1000f)` so mismatched vertex tiers can never leave an open crack |
| **P0** | Pitch-black Dusk water with unattenuated white speckles & stark icy-white stair-step bank foam (`stage2_proof_02`) | `CartographyWater.shader:178, 185, 190-191` (`glint` & `foam` multiply only by `tdLm.rgb`, bypassing `mainLight.color` + `SampleSH`, hard `step(...)` at `L185`, zero sky Fresnel reflection) | Attenuate `glint` and `foam` by `(mainLight.color * 0.65h + ambient * 1.15h)`, soften `L185` with `smoothstep(waveEdge + 0.04, waveEdge - 0.04, distToBank + edgeNoise * 0.06)`, and add a `0.22h` Fresnel dusk/sky reflection blend |
| **P1** | Clipped `1d6 Dmg | Rng 8 | Wt...` on `Bow` Victory Loot card (`stage2_proof_05`) | `BywaterHudController.UxOverhaul.cs:2453-2454` fixes `statsLbl` at `15f` with `TextOverflowModes.Ellipsis` | Enable `statsLbl.enableAutoSizing = true; statsLbl.fontSizeMin = 11.5f; statsLbl.fontSizeMax = 15f;` |
| **P1** | `Vex`, `Goblin`, `Giant Frog` mislabeled `Tank` on Initiative Bar (`stage2_proof_03`, `04`, `05`, `Deployment`) | `BywaterHudController.cs:2894` (`if (unit.Ac >= 14 \|\| unit.MaxHp >= 28) return "Tank";`) | Require `unit.MaxHp >= 17 && !unit.Id.Contains("frog")` or read monster/hero role metadata so `Vex` & `Goblin` display `"Skirm"` |
| **P1** | Opaque solid cream `0.42m` step squares behind `1 AP` move pips (`stage2_proof_03`) | `BywaterGridOverlays.cs:53, 1039` (`HoverPathSafeColor` `alpha 0.76f`, `0.42f` solid quad) | Reduce step footprint to `0.18f` diamond/dot at `alpha 0.42f` so grass/cobble remains visible beneath `1 AP` pills |
| **P2** | `StoneFenceH` flower planter in wild `RoadAmbush`; `Goblin` behind `fallen_hollow_log`; `surface_oil` hard square border; `CombatTickerPanel` tight margin | `CartographyWorldData.cs:3743-3753`; `BywaterWorldRenderer.cs:2892-2931`; `BywaterGridOverlays.cs:1851-1859`; `BywaterHudController.cs:4873` | In `RoadAmbush`, map `StoneFence` strictly to `PalisadeStakes` / `RubblePileA`; add `FallenHollowLog` to combat occlusion fade; feather `BuildSurfacePixelTexture` outer 4px alpha; expand `tickerPanel` height `136f -> 150f` |

---

## 4. Verification of All 6 Unity-Native Gameplay Pillars & Locked Invariants

1. **Pillar 1 - Cinemachine 3 (`BywaterCameraRig.cs`, `BywaterGameController.cs`):**
   - `EnsureCinemachineSetup` (`BywaterCameraRig.cs:1129-1201`) mounts `CinemachineBrain` (`IgnoreTimeScale = true`), `CinemachineCamera`, `CinemachineImpulseSource`, and `CinemachineImpulseListener`.
   - `[T]` pitch toggle (`BywaterGameController.cs:1177, 8070-8079` -> `BywaterCameraRig.cs:311-345`) smoothly interpolates `48.0 deg <-> 64.0 deg` over `0.32s` via `SmoothStep`.
   - Directional impulse (`PlayImpactImpulse` `L656-680`) and `0.040s` / `0.055s` hit-stop (`TriggerHitStop` `BywaterGameController.cs:5539-5562`, `BywaterCameraRig.cs:702-725`) verified on melee, shove, crit, and keg blasts. **PASS.**

2. **Pillar 2 - 3-State Fog-of-War, Save v4 & 1,440-Min Solar/Lunar Arc:**
   - Pure `RealmzCore/Exploration/FogOfWarMap.cs:6-246` (`Unseen = 0, Explored = 1, CurrentlyVisible = 2`, raycast LOS + base64 bitset export) feeds `_FogOfWarTex` (`BywaterTopDownLightmap.cs:337-419`) across Terrain, Water, and Billboard shaders.
   - `BywaterSaveService.cs:78-120` (`CurrentSaveVersion = 4`) persists `exploredFogByMap`, `worldTimeMinutes`, and `weatherState` with v1-v3 backward compatibility.
   - `BywaterBiomeAtmosphere.cs:203-299` computes smooth continuous `1,440`-minute sun/moon orbital rotation and 8-keyframe sky/fog/post-process interpolation (`EvaluateSkyKeyframe`). **PASS.**

3. **Pillar 3 - Combat & Atmosphere VFX:**
   - `BywaterCombatVfxDirector.cs:12-476` pre-allocates `MaxLinePool = 12`, `MaxQuadPool = 64`, `MaxLightPool = 4`, `MaxActiveEffects = 12` for arcing arrows, Fireball + `PointLight`, `#7DF9FF` multi-hop chain lightning, frost shatters, and keg explosions.
   - `BywaterAtmosphereVfx.cs:13-305` pools `4` chimney smoke emitters, `8` ember emitters, and `220` wind-tilted rain particles; `BywaterBiomeAtmosphere.cs:843-909` transitions motes across `SunMotes`, `Fireflies`, and `CaveSpores`. **PASS.**

4. **Pillar 4 - Audio (`RealmzMasterMixer` & Spatial Beds):**
   - `Assets/Audio/RealmzMasterMixer.mixer` (`RealmzAudioMixerBuilder.cs:14-154`) defines `Master` -> `Music`, `SFX`, `Ambience`, `UI` groups, exposed dB/lowpass params, and `Overworld` (`0 dB`, `22000 Hz`) vs `Dungeon` (`-4 dB`, `1200 Hz`) snapshots.
   - `BywaterAudioDirector.cs:223-267` binds all mixer groups cleanly; `BywaterAmbientAudio.cs:12-120` runs 3 pooled spatial/environmental beds (`RiverWater` `0.85f` spatial, `BlacksmithAnvil` `0.85f` spatial, `WeatherFire`). **PASS.**

5. **Pillar 5 - Destructible Props (`CombatModels.cs`, `TacticalCombatSession.cs`):**
   - `OilKegCluster` (`maxHp = 1`, `isExplosiveOil = true`, `isFlammable = true` at `TacticalCombatSession.cs:3558-3562`): detonates `3x3` `2d6` Heat + `SurfaceType.Fire` (2 turns), chains recursively to adjacent kegs and `Oil` pools, fires `PropDetonated` -> `BywaterGameController.cs:8108-8126`.
   - `LitBrazier` (`maxHp = 9999`, `isLightSource = true`, `L3567-3570`): deals `1d6` Heat + `Burning` on Shove (`L1315-1336`); `Cold` extinguishes into `3x3` `SteamCloud` (`ExtinguishLitBrazier` `L3625-3655`).
   - `BarrelsCrates` (`maxHp = 4`, `ProvidesCover = true`, `MoveCost = 2` at `L3571-3621`): breaks cleanly on damage (`L1851-1859`). **PASS.**

6. **Pillar 6 - Progression, Option 2 Loot, `RealmzBalanceCatalog`, `6 AP` & Monster HP:**
   - Victory XP bar tweens over `0.65s` + `0.24s` `LvlUpIconGo` pop (`BywaterHudController.UxOverhaul.cs:2066-2143`); Option 2 Loot verified (`TakeAndEquipBtn == null`, `Loot` header, `4`-across selectable cards, `Take Selected (13)`, zero `MVP`).
   - Fixed base `6 AP`: `CombatActionEconomy.DefaultBaseApPerTurn = 6` (`CombatRules.cs:8`); zero stale `5 AP` overrides.
   - Monster HP parity verified across `BalanceOverrides.cs:180-224`, `RealmzBalanceCatalog.cs:788-835`, `RealmzBalanceCatalog.asset`, `balance_overrides.json`, `monsters_bywater_m1.json`, and all live screenshots: `Orc Warrior` **`18-24 HP` (`default 21`)** vs `Giant Frog` **`15-19 HP` (`default 17`)**, `Goblin` `8-11 HP`, `Goblin Archer` `6-9 HP`. *(Minor non-breaking metadata note: `BalanceOverrides.cs:196` lists `giant_frog` `hitDice = 3` while `RealmzBalanceCatalog.cs:807` has `hitDice = 0`; HP min/max/default `15/19/17` match cleanly.)* **PASS.**
