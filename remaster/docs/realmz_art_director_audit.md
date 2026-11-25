# Realmz M1 Visual & Tactical Design Audit (Bywater Cartography Slice)

**Role:** Principal 2.5D Pixel-Art Director (*Songs of Conquest* / *Octopath Traveler* / *Battle Brothers*) + Senior Tactical RPG Game Designer
**Scope:** Strictly read-only inspection of 14 M1 PlayMode proofs/contact sheets, raw `Assets/Art/Cartography/` sprites/tiles/icons/VFX, URP shaders, C# rendering pipeline, `art-pipeline/cartography/`, and `RealmzCore` tactical systems.
**Benchmark:** *Songs of Conquest* sunlit HD-2D diorama craft (`64 px/m`, `palette_soc_sunlit_128.json`, `+Y` normal-mapped depth) paired with crisp *Battle Brothers* / *DOS2* tactical readability.

---

## Executive Summary & Grade Snapshot

Realmz M1 already has a **genuinely strong technical spine**: the `64 px/m` world scale, Sunlit-128 palette lock, `+Y` normal-mapped `CartographyBillboard.shader` / `CartographyTerrain.shader` / `CartographyWater.shader`, top-down drifting cloud lightmap (`BywaterTopDownLightmap`), and the dark-slate/brushed-bronze RPG UI overhaul (`BywaterUiHelpers`) prove the core diorama architecture works.

However, before burning GPU cycles on a large combat/biome expansion pack, **several structural visual defects and pipeline regressions are holding the game back** from looking cohesive and handcrafted:
1. **Broken/Hollowed Assets in Production:** At least **5 core buildings/props/trees** (`building_temple.png`, `building_apothecary.png`, `prop_stone_ruins.png`, `tree_pine_b.png`, `prop_market_stall_a.png`) still suffer from the old white-pocket alpha-removal bug (fixed later in `pixel_postprocess.py`, never re-run on these assets!), leaving literal transparent holes straight through walls, roofs, and trunks.
2. **Combat Biome Cover Fallbacks:** In `CartographyWorldData.ApplyCombatArenaLayout`, missing wilderness cover assets force `BoulderRock` and `RuinedWall` to reuse Bywater's suburban flower-box garden fence (`stone_fence_h.png`) and white fluted Greek dungeon columns (`dungeon_pillar.png`), while `BrushThicket` shrinks a `3.5m` `tree_oak_b` down into a `1.22m` bonsai lollipop tree (`M1_Bywater_NightCombat_Readable_Proof.png`).
3. **Zero Prop Variety & Ground Integration:** `BywaterWorldRenderer` spawns every prop at identical scale, identical orientation (and `CartographyBillboard.shader` `length()` ignores negative scale X!), hardcoded `Color.white` tint, and zero root-skirt decals/foliage collars, making props look pasted onto blotchy ground tiles.
4. **Blocky Shorelines & Over-Bright Night Combat:** Square grid-locked river corners (`BywaterTopDownLightmap` box-BFS SDF), checkerboard `surface_water` combat river tiles (`River_Proof.png`), and `b1f57ba` Combat Night's `1.40-1.50` ambient intensity wash out night mood into flat teal daylight.
5. **Tactical & UI Polish Gaps:** Clipped HUD strings (`Short Swor`, `Giant Fro`, `...for 12 t`), `2-4 HP` `Orc Warrior` vs `23 HP` `Giant Frog` encounter balance inversion, and under-leveraged surface/shove terrain interactions.

| Visual Pillar | Grade | Top Positive | Primary Bottleneck |
| :--- | :---: | :--- | :--- |
| 1. Terrain & Cliff Geometry | **C-** | Drifting cloud/canopy lightmap & `64 px/m` texel lock | Upscaled-looking blotchy grass/mud tiles, horizontal cliff banding, 90-deg river corners, combat water tile seams |
| 2. Props, Obstacles & Ground Integration | **D** | Strong hero landmarks (`bywater_inn`, `building_blacksmith`, `tree_willow_a`) | 5 hollowed alpha-bugged assets, suburban fence / dungeon column cover fallbacks, zero `FlipX`/tint/skirt decals |
| 3. Characters, Monsters & Scale Consistency | **B-** | Readable `64 px/m` combat silhouettes & contact shadows | Shopkeeper overscaling (`2.0f`), Walk/Attack frame drift (`Cleric`, `Knight`, `Rat`), chibi Priestess portrait mismatch |
| 4. Lighting, Color & Atmospheric Readability | **C+** | Warm daytime golden-hour contrast (`377165b`) & legible silhouettes | Night Combat (`b1f57ba`) flattened to overcast teal; zero embers, chimney smoke, or night fireflies; clip-art VFX |
| 5. UI Typography, Icons & Visual Polish | **B-** | Cohesive slate/bronze RPG modals, clear Paper-Doll & Victory UX | Clipped labels (`Short Swor`, stash items), thick-outlined `32px` SNES icons vs painterly portraits, cyan grid clutter |

---

## Section 1 - Brutally Honest Visual Quality Scorecard

### 1.1 Terrain & Cliff Geometry - Grade: **C-**
**What Works:**
- `CartographyTerrain.shader` (`ComputePixelLockedUV`, `_WorldTexelScale = 0.5`, `_TexelResolution = 128.0`) enforces crisp `64 px/m` texel snapping without bilinear smudge, and `BywaterElevationBuilder.BuildTerrainMesh` welds equal-elevation plateaus with a `0.06f` rim chamfer and base crease AO.
- `BywaterTopDownLightmap` drifting dual-FBM cloud cookies give open outdoor maps genuine *Songs of Conquest* sun-and-shadow rhythm.

**Observed Defects & Evidence:**
1. **Low-Microdetail Blotchy Ground Tiles (`M1_Bywater_LevelUp_Proof.png`, `M1_Bywater_Night_Torch_Proof.png`, `M1_Bywater_Scout_Intercept_Proof.png`):** Direct inspection of `tiles/meadow_grass.png`, `tiles/moss_patch.png`, `tiles/dirt_road.png`, `tiles/river_mud.png`, and `tiles/dark_forest_moss.png` shows chunky `2x2`-blocky noise blotches (pale grey-mint blobs on `meadow_grass`, random isolated green specks and a dark horizontal smudge on `river_mud`). Under party torchlight (`M1_Bywater_Night_Torch_Proof.png`) or in open meadows (`M1_Bywater_LevelUp_Proof.png`), those large repeating blobs read as low-res procedural noise rather than fine grass blades, clover, and pebbles.
2. **Repeating Strata & Crack Wallpaper (`M1_Bywater_Combat_Preview.png`, `M1_Bywater_LevelUp_Proof.png`):** `tiles/cliff_rock.png` contains 4 evenly spaced wavy horizontal lines across 128px, producing obvious horizontal stripe wallpaper across every cliff wall and high-ground edge. Similarly, `tiles/dungeon_cracked_slate.png` bakes 3 identical diagonal hairline cracks that repeat mechanically.
3. **Stepped Road-to-Grass Borders (`M1_Bywater_Explore_Preview.png`, `M1_Bywater_TownRoads_Clear_Proof.png`):** Cobblestone tiles sit at `CobbleElevation = 0.04f` against grass `PlazaElevation = 0.00f`. Because `BywaterElevationBuilder.CheckAndEmitCliff` emits vertical rock steps whenever `eHere > eNeighbor + 0.03f` (`0.04f > 0.03f`), cobblestone roads get hard 90-degree square tile steps along every diagonal bend instead of feathered earth/pebble shoulders.
4. **Blocky River Shorelines & Combat River Mosaic (`M1_Bywater_Night_Torch_Proof.png`, `M1_Bywater_Scout_Intercept_Proof.png`, `M1_Bywater_TacticalArena_River_Proof.png`):**
   - In Overworld, `BywaterTopDownLightmap.ConfigureFromWorldData` computes `shoreProx` (`minLandDist`) against axis-aligned square tile boxes (`Mathf.Clamp(wx, nx, nx+1)`), causing `CartographyWater.shader` shoreline foam (`waveEdge`) to trace razor-sharp 90-degree square corners and stair-steps around every bend in the river. Meanwhile `tiles/shallow_water.png` consists of flat royal blue with large hard-edged dark navy amoeba blobs and scattered white dashes.
   - In `M1_Bywater_TacticalArena_River_Proof.png`, `CartographyWorldData.ApplyCombatArenaLayout` sets `tile.IsWater = false` (`MudElevation = -0.05f`) and renders `surface_water` square decals inside bright cyan grid tiles. This turns the combat river into an artificial blue-and-brown checkerboard that visibly clashes with the real dark-blue `ShallowWaterPlane` river right outside the yellow arena border!
5. **Boxy High-Ground Cubes (`M1_Bywater_Combat_Preview.png`, `M1_Bywater_TacticalArena_Road_Proof.png`):** Tactical `ElevationTier >= 1` platforms render as rigid rectangular cobble-capped blocks (`cobble_plaza`/`dirt_road` top + `cliff_rock` sides) with zero grassy overhang lips, boulders, or moss trim.

### 1.2 Props, Obstacles & Organic Ground Integration - Grade: **D**
**What Works:**
- Hero town landmarks (`structures/bywater_inn.png`, `Buildings/building_blacksmith.png`, `Buildings/building_guildhall.png`, `Buildings/building_cottage_b.png`, `Buildings/building_cottage_c.png`), `structures/stone_bridge.png`, `trees/tree_oak_b.png`, `trees/tree_willow_a.png`, and `Props/dungeon_door_closed.png` / `dungeon_sarcophagus.png` demonstrate the target standard: crisp Sunlit-128 color ramps, readable timber/stone architecture, and strong 2.5D volume.

**Observed Defects & Evidence:**
1. **Five Severely Hollowed Assets (Legacy White-Keying Bug):** Direct inspection of `Assets/Art/Cartography/` proves that several core assets were processed BEFORE `pixel_postprocess.py`'s exterior-only BFS fix and still have giant transparent holes punched through their interiors:
   - `Buildings/building_temple.png`: Upper walls and left roof eroded down to a floating 2px black wireframe outline.
   - `Buildings/building_apothecary.png`: Entire stucco mid-section erased transparent; hanging green sign floats disconnected on the left.
   - `Props/prop_stone_ruins.png`: Stone columns/lintel eaten hollow inside, leaving only yellow top caps and thin brown edge slivers.
   - `trees/tree_pine_b.png`: Entire central trunk and sunlit inner boughs punched white-transparent.
   - `Props/prop_market_stall_a.png`: Right canopy eaten white with detached yellow floor smears outside the legs.
2. **Baked Lighting Glare & Alpha Debris (`M1_Bywater_TacticalArena_Road_Proof.png`, `M1_Bywater_TownRoads_Clear_Proof.png`):**
   - `Props/prop_barrels_crates.png` has baked-in neon yellow-orange lighting across the front/side AND a detached floating slice of barrel on its right edge.
   - `Props/prop_wooden_wagon.png` has a blown-out glowing white-blue cargo sack and neon lime-green bleed behind the left wheel.
   - `Props/prop_stone_well.png` retains floating horizontal yellow/salmon floor streaks on both sides; `Props/prop_shrine_statue.png` looks like a teal blocky cylinder with a stray 1px hook on bottom-left; `Props/prop_camp_tent.png` has green grass baked into its canvas flap.
   - `trees/tree_oak_a.png` has an unnatural solid canary-yellow highlight mass right in the center of the crown; `trees/tree_pine_a.png` traps bright pale-cyan sky pixels between its branches; `structures/stone_fence_h.png` has transparent gaps between stone courses and saturated yellow garden flowers.
3. **Immersion-Breaking Combat Cover Fallbacks (`M1_Bywater_Combat_Preview.png`, `M1_Bywater_TacticalArena_River_Proof.png`, `M1_Bywater_NightCombat_Readable_Proof.png`):** Because dedicated combat obstacles are missing, `CartographyWorldData.cs` lines 3457-3510 fall back to:
   - `BoulderRock` -> alternates `StoneFenceH` (suburban garden flower planter!) and `DungeonPillar` (clean white fluted Greek temple column!)
   - `RuinedWall` -> alternates `StoneFenceH` and `DungeonPillar`
   - `BrushThicket` -> shrinks `TreeOakB` (`3.5m` -> `1.22m x 1.08m`), producing a neat row of miniature lollipop trees (`NightCombat_Readable_Proof.png`).
4. **Zero Visual Variety & Pasted Ground Contact:** Every `TreePineA`, `DungeonPillar`, `StoneFenceH`, and `BarrelsCrates` uses identical scale, identical `FlipX = false`, and `Color.white` (`BywaterWorldRenderer.cs:1179`). Worse, `CartographyWorldData` marks obstacle and cover tiles `BlocksFoliage = true` (`line 3443`), stripping `BywaterFoliageScatter` grass cards around trees, wagons, pillars, fences, and river mud banks, leaving props sitting flat on bare textures with zero ground-skirt decals or contact shadows.

### 1.3 Characters, Monsters & Scale Consistency - Grade: **B-**
**What Works:**
- Across `M1_Bywater_Combat_Preview.png`, `Road_Proof.png`, `River_Proof.png`, and `NightCombat_Readable_Proof.png`, the `64 px/m` (`128x128` 4-frame) unit roster achieves instant tactical readability. Heroes (`Aldric`, `Liora`, `Kaelen`, `Theron`, `Seraphina`, `Vex`) and enemies (`Giant Frog`, `Orc Warrior`, `Goblin Raider`, `Goblin Archer`, `Skeleton Warrior`, `Bandit Cutthroat`) separate cleanly from terrain via crisp outlines, under-rings, and soft contact shadows (`BywaterUnitView.cs`).
- `walk_attack_monsters_sheet.png` shows awesome creature personality (`Giant Frog` tongue whip in Attack f1, `Orc Warrior` imposing armor, `Skeleton Warrior` icy cyan bones).
- Shopkeeper `128x128` portraits (`Blacksmith`, `Tanner`, `Apothecary` in `shopkeepers_sheet.png`) and all 6 Hero portraits (`PaperDoll_Camp_Proof.png`, `Victory_Loot_Proof.png`) hit high painterly pixel-art quality.

**Observed Defects & Evidence:**
1. **Scale Inconsistency (`M1_Bywater_Explore_Preview.png`, `shopkeepers_sheet.png`):** `BywaterWorldRenderer.RebuildCartographyNpcs` spawns shopkeepers at `spriteSize: 2.0f` (`line 1228`), yet `npc_blacksmith` fills nearly the full vertical height of its `128x128` canvas. Next to `Aldric` in `Explore_Preview.png`, Blacksmith Borin looks like a `2.4m` half-giant almost as tall as the cottage wall eaves behind him. Similarly, `Giant Frog` at `2.0m` reads almost 2x wider than `Orc Warrior` (`M1_Bywater_TacticalArena_River_Proof.png`).
2. **Walk/Attack Frame Drift (`walk_attack_heroes_sheet.png`, `walk_attack_monsters_sheet.png`):**
   - `Aldric (Knight)`: Broadsword shifts from cool steel (Idle / Walk f1 / Attack f0) to bright orange-bronze in Walk f0/f2; Attack f1-f3 lose blade motion arc and settle into a stiff static pose.
   - `Liora (Cleric)`: Severe identity drift across sheets - slender white/blue priestess with tall staff (Idle f0) morphs into yellow shoulder pads holding a sickle (Walk f2) and sprouts erratic brown jagged sticks from her back (Attack f2).
   - `Theron (Mage)`: Staff orb flips upside-down to the bottom end of the staff in Attack f0.
   - `Seraphina (Paladin)`: Holds crossed dual weapons in Idle f0 vs single sword in Walk, and loses leg armor/skirt into exposed pink legs in Attack f2.
   - `Vex (Rogue)`: Loses steel shin-greaves in Walk f0-f3, and dark purple-charcoal values sink into dark grass/night terrain (`NightCombat_Readable_Proof.png`).
   - `Giant Rat`: Mid-torso breaks completely apart between front and hind legs in Attack f2.
   - `Orc Warrior`: Head merges into dark shoulder silhouette in Walk f0; broad axe head collapses into a thin horizontal line in Attack f1.
3. **Shopkeeper Portrait Mismatch & Static Idles (`shopkeepers_sheet.png`):** `portrait_npc_priestess.png` uses clean anime/chibi cel-shading with giant stylized eyes, breaking the rugged *Battle Brothers* / *Songs of Conquest* style of `Blacksmith`, `Tanner`, and `Apothecary`. All 4 shopkeeper `IDLE f0..f3` sheets exhibit barely 1px of motion across frames.

### 1.4 Lighting, Color & Atmospheric Readability - Grade: **C+**
**What Works:**
- Commit `377165b` nailed sunny daytime clarity (`BuildSunlitDaylightState`: sun `1.82f`, cool indigo ambient shadows, `exposure -0.32f`, `contrast 20`, `SunbeamMotes` pollen particles).
- `M1_Bywater_Night_Torch_Proof.png` delivers strong warm party-torch falloff (`BywaterWorldRenderer` `_partyLantern`) against deep night blues (`BuildNightState`).

**Observed Defects & Evidence:**
1. **Night Combat Over-Correction (`M1_Bywater_NightCombat_Readable_Proof.png`, commit `b1f57ba`):** While `b1f57ba` fixed unplayable darkness, `BuildCombatNightState` (`BywaterBiomeAtmosphere.cs:331-363`) sets `sunIntensity = 0.88-0.98`, `ambientIntensity = 1.40-1.50`, `shadowStrength = 0.30`, `contrast = 6`, and `postExposure = +0.22..+0.28`. The result reads like flat, desaturated overcast teal daylight rather than atmospheric torchlit night, stripping contrast from torch/brazier pools.
2. **Flat Night Exploration Rim Separation & Dead Air (`M1_Bywater_Night_Torch_Proof.png`):** Beyond the immediate ground circle, pine trees (`tree_pine_a`) sink into muddy dark green with zero cool moonlight rim separation, and `BywaterBiomeAtmosphere` hard-disables `sunMoteParticles` at Night (`playSunMotes = false`), missing effortless night mood from drifting fireflies.
3. **Lifeless Static Fire & Clip-Art Combat VFX:** `TorchBrazier` (`M1_Bywater_Combat_Preview.png`, `Road_Proof.png`) displays a solid yellow cartoon flame shape with zero rising sparks or smoke; town chimneys (`Blacksmith`, `Inn`, `Cottages`) emit no smoke; `Vfx/vfx_slash_arc.png`, `vfx_fireball_impact.png` (frame 4 uniform 1px-outlined grey spheres), and `vfx_arrow_bolt.png` (frame 2-4 grey dots + squiggle ring) look like thin-outlined 16-bit clip-art rather than *Octopath*-grade glowing additive combat VFX.

### 1.5 UI Typography, Icons & Visual Polish - Grade: **B-**
**What Works:**
- `BywaterUiHelpers.cs` establishes a crisp, readable dark-fantasy visual identity (`#0C111E` slate, `#C09142` brushed bronze borders, parchment typography with drop shadows).
- The initiative queue (`Combat_Preview.png`), 9-slot Paper-Doll (`PaperDoll_Camp_Proof.png`), and 980x690 Victory & Loot screen with green upgrade arrows (`Victory_Loot_Proof.png`) are genuinely informative and fast to parse.

**Observed Defects & Evidence:**
1. **Recurring Text Truncation / Clipping Across Screens:**
   - Combat bottom bar (`NightCombat_Readable_Proof.png`, `Victory_Loot_Proof.png`): `Short Swor` (clipped `d`).
   - Combat tooltip (`Combat_Preview.png`): `[Theron] Approach & Melee: Giant Fro` (clipped `g`) and overlaps `Goblin Archer`.
   - Combat Log (`Combat_Preview.png`): `[R1] Giant Frog attacks Vex (1 swing(s)) for 12 t` (clipped mid-word).
   - Shared Stash rows (`PaperDoll_Camp_Proof.png`): `Heal Medium Wounds x1 (3 chg) [140g] - Heal` and `Heal Small Wounds x4 (3 chg) [75g] - Heal 1d` clip before the `Use` button.
   - World-space NPC prompt (`Explore_Preview.png`): `[E / Click] Trade: Master Blacksmith / Borin` wraps awkwardly with an orphaned word (`280x44` box in `BywaterWorldRenderer.cs:1291`).
2. **Icon Style Split:** `Icons/` (`icon_shove.png`, `icon_guard.png`, `icon_potion_heal_small.png`, `icon_broadsword.png`, `icon_torch.png`) are `32x32` sprites with heavy pure-black outlines and slight upscaling softness, clashing noticeably with the rich painterly `128x128` character portraits inside `PaperDoll` and `Victory` cards.
3. **Visual Clutter & Unused Padding:**
   - Bright cyan square tile outlines across the entire combat arena plus solid opaque cream/beige squares (`Combat_Preview.png`, `River_Proof.png`) fight with unit silhouettes and cover terrain details.
   - Non-casters (`Aldric`, `Kaelen`, `Vex`) display empty `SP -` rows in the bottom party strip; `PaperDoll` equipment slots, `LevelUp` stat rows (`LevelUp_Proof.png`), and `Scouted Enemies` cards (`Scout_Intercept_Proof.png`) carry large blank vertical padding; the `1/2` and `12t` header camp icons (`PaperDoll_Camp_Proof.png`) are slightly too small without hover.

---

## Section 2 - Unity Procedural Variety Engine Spec (Zero-GPU Visual Multipliers)

All 5 multipliers below run cleanly inside existing URP shaders (`CartographyBillboard.shader`, `CartographyTerrain.shader`, `CartographyWater.shader`) and C# managers (`BywaterWorldRenderer`, `BywaterFoliageScatter`, `BywaterElevationBuilder`, `BywaterTopDownLightmap`, `BywaterBiomeAtmosphere`) with **zero new GPU generations required**.

### 2.1 Normal-Safe `FlipX` (Fixing the Current Shader + Normal Inversion Bug)
**Observed Bug in `CartographyBillboard.shader` & `BywaterUnitView.cs`:**
- `CartographyBillboard.shader` line 220 computes `float scaleX = length(float3(UNITY_MATRIX_M[0][0], UNITY_MATRIX_M[1][0], UNITY_MATRIX_M[2][0]));`. Because `length()` discards negative signs, setting `localScale.x = -1` (used in `BywaterUnitView.SetFacingRight` line 188!) **does not invert `_BillboardMode > 0.5` billboards cleanly**, and flipping `uv.x` without flipping the normal map inverts `normalTS.x`, causing directional sunlight and torches to light the wrong side of mirrored trees, rocks, and characters.

**Exact Implementation Spec:**
1. **Shader Property (`CartographyBillboard.shader`):**
   - Add `_FlipX ("Flip Horizontal (0/1)", Float) = 0.0` to `Properties`, non-instanced `CBUFFER`, and `UNITY_INSTANCING_BUFFER_START(Props)` (`UNITY_DEFINE_INSTANCED_PROP(float, _FlipX)`).
   - In `ComputeBillboardAtlasUV(float2 localUV, float3 rootWS)` (line 144): flip `localUV.x` **before** sub-rect interpolation so 4-frame strips and 2-bank foliage atlases mirror inside the active cell without bleeding into neighbor frames:
     ```hlsl
     float flipX = CARTO_INST_PROP(_FlipX);
     float2 uvInCell = float2(flipX > 0.5 ? (1.0 - localUV.x) : localUV.x, localUV.y);
     return lerp(uvMin, uvMax, saturate(uvInCell));
     ```
   - In `BillboardFrag` (line 393): invert tangent-space X normal when flipped:
     ```hlsl
     half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
     if (CARTO_INST_PROP(_FlipX) > 0.5)
     {
         normalTS.x = -normalTS.x;
     }
     ```
2. **Placement Rules (`BywaterWorldRenderer`, `BywaterFoliageScatter`, `BywaterUnitView`):**
   - `BywaterWorldRenderer.RebuildCartographyProps`: Compute deterministic `bool flipX = (CartographyWorldData.DeterministicHash(gx, gy, (int)prop.Kind * 31) & 1) == 1;` for natural/rubble/cover props (`Tree*`, `Boulder*`, `Log`, `Bramble`, `Rubble`, `BarrelsCrates`, `RuinedWall`, `Crates`, `Decal*`). **Never** flip `StoneBridge`, asymmetric town buildings with doors/signs, or directional `Signpost`. Store `entry.FlipX` on `PropBillboardEntry` and pass `mpb.SetFloat(FlipXId, entry.FlipX ? 1f : 0f)`.
   - `BywaterFoliageScatter`: Include a `float[] batchFlipX` array in `BuildBatchesFromLists` (`50%` random flip per grass/fern card), doubling effective foliage atlas silhouette variety at zero VRAM cost.
   - `BywaterUnitView.SetFacingRight`: Keep `localScale.x = 1f` and drive `_FlipX` (`_facingRight ? 0f : 1f`) via `_spritePropBlock` so West-facing and East-facing combatants share accurate directional sunlight/torch rim shading.

### 2.2 Subtle Value / Tint / Scale Variance (`MaterialPropertyBlock`, Sunlit-128 Safe)
**Observed Gap:** `BywaterWorldRenderer.cs` lines 1177-1181 clear `_propFadeBlock` and hardcode `Color.white`, while `UpdateTreeOcclusionFade` (`line 679`) overwrites `_TintColor` with `(1, 1, 1, alpha)`.

**Exact Implementation Spec:**
- Add `public Color BaseTint;` and `public Vector2 BaseScale;` to `PropBillboardEntry`, seeded once in `RebuildCartographyProps` from `DeterministicHash(tileX, tileY, (int)prop.Kind)` and preserved across both `UpdateTreeOcclusionFade` (`entry.BaseTint * alpha`) and `ApplyAnimated4FramePropertyBlock` (`entry.BaseTint * alpha`).
- Keep linear RGB multipliers tight around neutral `[0.85..1.10]` so colors stay within Sunlit-128 warmth and never drift into neon clipping or muddy grey:

| Prop Class | Scale X Range | Scale Y Range | Value Range | RGB Channel Bias (Linear Multiplier) |
| :--- | :---: | :---: | :---: | :--- |
| **Trees** (`Oak`, `Pine`, `Willow`, `DeadTree`) | `0.88 - 1.14` | `0.86 - 1.18` | `0.86 - 1.07` | Sunlit Meadow/Oak: `(0.96-1.06 R, 0.96-1.05 G, 0.90-1.00 B)`; Forest/Pine: `(0.88-0.98 R, 0.95-1.04 G, 0.96-1.06 B)` |
| **Rocks & Boulders** | `0.84 - 1.15` | `0.85 - 1.12` | `0.86 - 1.06` | Warm Sandstone vs Cool Slate: `(0.94-1.05 R, 0.96-1.02 G, 0.94-1.08 B)` |
| **Ruins, Pillars & Walls** | `0.94 - 1.06` | `0.92 - 1.08` | `0.88 - 1.04` | Damp/Mossy bias near trees/water: `(0.92-1.02 R, 0.96-1.05 G, 0.94-1.03 B)` |
| **Crates, Barrels & Barricades** | `0.92 - 1.08` | `0.92 - 1.07` | `0.88 - 1.05` | Weathered Timber: `(0.95-1.06 R, 0.92-1.02 G, 0.86-0.96 B)` |
| **Foliage Cards** (`BywaterFoliageScatter`) | `0.82 - 1.24` | `0.78 - 1.32` | `0.82 - 1.12` | Existing `ComputeCardTint` + boost root-collar clusters around props by `+0.08` height |

### 2.3 Organic Cluster Spawning + Ground Scatter Skirts
To eliminate the "cardboard cutout pasted onto flat floor" look, spawn a **2-tier Ground Collar** automatically whenever primary props or buildings are placed (`CartographyWorldData` + `BywaterWorldRenderer`):
1. **Horizontal Skirt Decal (`_groundQuadMesh` at `elev + 0.015f`, `IsFloorDecal = true`, shadows off, random `FlipX` + `0/90/180/270` Y-rotation):**
   - Around `Tree*` / `GnarledDeadTree` / `FallenHollowLog`: spawn 1 `decal_fallen_leaves` or `decal_moss_skirt` (`1.2m - 1.6m` footprint).
   - Around `Boulder*` / `RuinedWall` / `BrokenArchway` / `Rubble*`: spawn 1 `decal_pebbles` or `decal_cracked_stone` (`1.1m - 1.4m`).
   - Under `Tree*`, `WoodenWagon`, `CartWreck`, and `Buildings/*`: add a soft **Prop Contact Shadow Quad** (`ContactShadowColor` at `elev + 0.010f`, scaled to `0.85 * SizeMeters.x`), mirroring `BywaterUnitView._contactShadowRenderer` so upright billboards remain grounded even at Dusk/Night.
2. **Perimeter Foliage Collar Ring (`BywaterFoliageScatter`):**
   - Currently `CartographyWorldData` (`line 3443`) sets `tile.BlocksFoliage = true` on cover/obstacle tiles, deleting all grass blades around every prop.
   - Change `BywaterFoliageScatter` to recognize a new `HasRootCollar` flag (or emit a `RootCollar` pass for non-road `ObstacleKind != None` tiles): scatter `4-8` `0.50m` grass/fern/pebble-tuft cards in a perimeter ring (`radius 0.36m - 0.50m` around tile center) so tall grass and ferns visibly wrap around tree trunks, boulders, fences, and ruined masonry.

### 2.4 Shoreline, Road Shoulder & Cliff Edge Polish
1. **Smooth Overworld River Shorelines (`BywaterTopDownLightmap.cs` + `CartographyWater.shader`):**
   - In `BywaterTopDownLightmap.cs` (`lines 198-206`), replace box-clamped tile distance with an elliptical/rounded corner SDF (plus a 3x3 Gaussian blur pass on `_worldStaticMap.g`), eliminating sharp 90-degree square corners in `shoreProx`.
   - In `CartographyWater.shader` (`lines 176-180`), perturb `distToBank` with quantized `64 px/m` multi-frequency wave noise (`Hash21(floor(snappedWS * 16.0)) * 0.07 + sin(snappedWS.x * 3.5 + snappedWS.y * 2.7) * 0.04`) so shoreline foam scallops naturally around banks, and place `river_reeds_4f` + `decal_shore_foam` skirts along `RiverbankReed` bank tiles.
2. **Fix Combat River Checkerboard (`CartographyWorldData.ApplyCombatArenaLayout` + `BywaterGridOverlays`):**
   - Instead of raising combat river cells to `-0.05f` and pasting 1x1 `surface_water.png` decals inside cyan grid lines (`M1_Bywater_TacticalArena_River_Proof.png`), depress `RiverbankCrossing` stream cells to `RiverElevation = -0.28f` with `WaterSurfaceHeight = -0.18f` (allowing the continuous animated `CartographyWater.shader` plane to flow seamlessly through the combat arena!) while rendering subtle corner-tick grid brackets at `y = -0.16f` and `decal_shore_foam` / `river_reeds_4f` along mud banks (`IsMudBank`), plus `wooden_plank_bridge` props over `BridgePlank` crossings!
   - Preserve `SurfaceType.Water` gameplay rules in `CombatGrid`; only change the visual presentation so combat rivers match overworld rivers seamlessly.
3. **Feather Road Shoulders & Break Cliff Horizontal Banding (`CartographyWorldData.cs`, `BywaterElevationBuilder.cs`, `CartographyTerrain.shader`):**
   - Lower `CobbleElevation` from `0.04f` to `0.025f` (`<= 0.03f` threshold in `CheckAndEmitCliff` line 387) so cobblestone roads stop generating rigid 90-degree vertical curb cliffs against grass. Let `CartographyTerrain.shader` alpha noise-mask blending (`_NoiseMaskStrength`) + `decal_pebbles` edge scatter feather the transition.
   - In `CartographyTerrain.shader` (`ComputePixelLockedUV` lines 171-178), add a world-coordinate vertical UV jitter on steep cliff faces (`absN.y < 0.55`): `rawUV.y += CartoHash12(floor(positionWS.xz)) * 0.37;` plus higher base crease AO (`lerp(0.45h, 1.0h, creaseAO)`), instantly breaking horizontal strata tiling across cliffs and combat high-ground blocks.

### 2.5 Ambient Living Micro-Motion & Night Combat Rebalance
1. **Chimney Smoke (`Blacksmith`, `BywaterInn`, `CottageB`, `CottageC`):** Pooled lightweight URP particle emitters (`4-6` particles/sec, `0.15m -> 0.45m` pixel-quad grey puffs `#DCD7CE` drifting `+Y` and `+X` with `_WindDir`).
2. **Forge, Brazier & Campfire Embers (`TorchBrazier`, `Campfire4f`, `DungeonWallSconce4f`):** Tiny `1-2px` additive sparks (`#FFC952` -> `#E35259`, `8-12` particles/source) rising and flickering above static 4-frame fire sprites so fire feels alive.
3. **Dusk/Night Fireflies & Dungeon Spores (`BywaterBiomeAtmosphere.cs`):** Reuse `EnsureSunMoteParticles` instead of stopping it at Night/Dungeon: swap to warm pulsing chartreuse-gold **Fireflies** (`#FFE07D` / `#B4EC86`, gentle sine curl around `RiverbankReed`/`ForestShade`) at Dusk/Night, and drifting teal **Cave Spores** (`#85E6DA`) in Dungeons.
4. **Night Combat Chiaroscuro Rebalance (`BywaterBiomeAtmosphere.cs:331-363` + `CartographyBillboard.shader`):**
   - Dial `BuildCombatNightState` back from flat daylight-teal (`ambientIntensity 1.40-1.50 -> 0.96-1.06`, `sunIntensity 0.88-0.98 -> 0.68-0.78`, `contrast 6 -> 15`, `postExposure +0.28 -> +0.08`, `shadowStrength 0.30 -> 0.48`) so torches and braziers cast authentic warm pools again.
   - To keep units and obstacles 100% readable without washing out the terrain, add a subtle **Moonlight Rim Separation** term (`_NightRimStrength`, `pow(1.0 - saturate(dot(normalWS, viewDirWS)), 2.2)`) on `CartographyBillboard.shader` driven by cool silver-blue moonlight (`#A8BBE0`), keeping characters/cover silhouettes crisp against dark night ground.

---

## Section 3 - Prioritized `64 px/m` Asset Expansion Manifest

### Part A: Existing Asset Repairs & Cleanups (Run First Before New Pack!)

| Asset Path | Observed Issue | Exact Pipeline Fix | Priority |
| :--- | :--- | :--- | :---: |
| `Buildings/building_temple.png` | Interior walls/roof hollowed transparent | Re-run with exterior-only BFS `pixel_postprocess.py` (preserve interior whites) + refresh `_n.png` | **P0** |
| `Buildings/building_apothecary.png` | Stucco center erased + floating sign | Same exterior-only BFS re-roll + reconnect sign bracket | **P0** |
| `Props/prop_stone_ruins.png` | Pillars/arch hollowed out, yellow top glare | Regenerate on white studio backdrop with exterior-only BFS + neutral lighting | **P0** |
| `trees/tree_pine_b.png` | Trunk & center needles hollowed transparent | Re-run exterior-only BFS on raw Comfy output or re-roll with dark trunk guide | **P0** |
| `Props/prop_market_stall_a.png` | Right canopy eaten white + stray floor lines | Re-run exterior-only BFS + `keep_major_components` floor strip | **P0** |
| `Props/prop_barrels_crates.png` | Baked neon-yellow glow + detached barrel slice | Re-roll neutral-lit barrels/crates + crop detached alpha island | **P0** |
| `Props/prop_wooden_wagon.png` | Blown-out white-blue sack + green wheel spill | Re-roll with muted parchment/linen cargo + chroma despill | **P1** |
| `Props/prop_stone_well.png` | Floating yellow/pink ground puddle streaks | Strip disconnected bottom floor smudges (`keep_major_components`) | **P1** |
| `trees/tree_oak_a.png` & `tree_pine_a.png` | Oak flat yellow center blob; Pine cyan sky dots | Re-roll `oak_a` emerald-sage; flood-purge interior cyan pixels on `pine_a` | **P1** |
| `structures/stone_fence_h.png` | See-through brick gaps + bright garden flowers | Solidify mortar backing; swap flowers for subtle moss tufts | **P1** |
| `tiles/meadow_grass.png`, `cliff_rock.png`, `dungeon_cracked_slate.png`, `shallow_water.png` | Blotchy upscaled noise (`grass`), 4 horizontal bands (`cliff`), 3 diagonal cracks (`slate`), amoeba blobs (`water`) | Re-author 128x128 native `64 px/m` blade/stone/ripple micro-detail tiles with zero repeating seams | **P0** |
| `units/hero_cleric_*`, `hero_knight_walk`, `hero_mage_attack`, `hero_paladin_attack`, `hero_rogue_*`, `monster_giant_rat_attack`, `monster_orc_warrior_*` | Frame drift (Liora back sticks, Knight orange sword, Mage inverted orb, Paladin pink legs, Rat split torso, Rogue low contrast) | Targeted img2img/ControlNet pose-locked re-rolls using Idle f0 as IP-Adapter identity lock (`ip_weight 0.65`) | **P1** |
| `Portraits/portrait_npc_priestess.png` & `units/npc_*` | Anime chibi portrait clashes with Blacksmith/Tanner; NPCs fill ~95% frame height (look giant in town) | Re-roll Priestess portrait in gritty painterly style; scale NPC sprites to `~75%` frame height (~1.65m visual) | **P1** |
| `Vfx/vfx_slash_arc.png`, `vfx_fireball_impact.png`, `vfx_arrow_bolt.png` | Thin black-outlined clip-art look (frame 4 grey dots) | Re-author soft-edged luminous pixel VFX (no outer black ink outline) | **P2** |
| `Icons/icon_shove.png`, `icon_guard.png`, `icon_potion*`, `icon_spell_*` | Thick black-outlined `32x32` SNES style clashes with painterly UI | Re-render/upscale core combat action + potion icons at crisp `64x64` Sunlit-128 | **P2** |

---

### Part B: `30` Concrete New `64 px/m` Assets (`art-pipeline/cartography/` Build Sheet)

All sprite dimensions strictly match `64 px/m` (`1.0m = 64px`), BottomCenter pivot (`0.5, 0`) unless noted as `FloorDecal` (`0.5, 0.5`), quantized to `palette_soc_sunlit_128.json`, with `+Y` Laigter normal maps (`*_n.png`).

#### Priority P0 - Combat Cover Variety, Bridges & Ground-Skirt Decals (14 Assets)

| # | Asset ID | Size (px) | Frames | Target Biome(s) | Sunlit-128 & Art Direction Notes |
| :---: | :--- | :---: | :---: | :--- | :--- |
| 1 | `boulder_moss_a` | `96x96` | `1f` | `RoadAmbush`, `RiverbankCrossing`, `DeepWoods` | Weathered faceted limestone boulder (`warm_cobble_gray` + `slate_indigo_shadow`), olive moss cap (`sage_olive_moss`), zero baked cast shadow |
| 2 | `boulder_moss_b` | `112x96` | `1f` | `RoadAmbush`, `RiverbankCrossing`, `DeepWoods` | Wide low twin-rock cluster (`1.75m x 1.5m`) for natural half-cover, fractured bevel normals |
| 3 | `boulder_moss_c` | `80x80` | `1f` | `RoadAmbush`, `RiverbankCrossing`, `RuinsOutskirts` | Tall craggy spire boulder (`1.25m`), darker slate base, lichen speckles |
| 4 | `fallen_hollow_log` | `160x80` | `1f` | `DeepWoods`, `RiverbankCrossing` | Mossy fallen oak trunk (`2.5m x 1.25m`, `timber_leather_earth` + `emerald_turf` moss strip + hollow dark knot end) |
| 5 | `gnarled_dead_tree` | `192x224` | `1f` | `DeepWoods`, `RuinsOutskirts` | Twisted leafless lightning-struck oak (`3.0m x 3.5m`), crooked silhouette breaks pine/oak monotony and preserves combat sightlines |
| 6 | `thorn_bramble_thicket` | `96x80` | `1f` | `DeepWoods`, `RoadAmbush` | Dense thorny briar mound (`1.5m x 1.25m`, dark emerald/crimson berries); replaces shrunken `TreeOakB` for `BrushThicket` |
| 7 | `ruined_wall_corner` | `128x128` | `1f` | `RuinsOutskirts`, `RoadAmbush` | Crumbled L-section medieval ashlar wall (`2.0m x 2.0m`), broken top stones, ivy creepers; replaces `StoneFenceH` in combat |
| 8 | `broken_archway` | `192x160` | `1f` | `RuinsOutskirts` | Collapsed stone archway (`3.0m x 2.5m`), weathered sandstone & slate mortar, open beneath arch via chroma-key pass |
| 9 | `rubble_pile_a` | `96x80` | `1f` | `RuinsOutskirts`, `DungeonChamber` | Chunky fallen masonry heap (`1.5m x 1.25m`), angular broken blocks & dust gravel |
| 10 | `wooden_plank_bridge` | `192x128` | `1f` | `RiverbankCrossing` | Bound timber ford planks with low rope/stake rails (`3.0m x 2.0m`), weathered `timber_leather_earth`, fits `BridgePlank` tiles |
| 11 | `river_reeds_4f` | `256x64` | `4f` | `RiverbankCrossing`, `Town` | 4-frame `64x64` animated tall cattail/reed clump (`emerald_turf` + `sunlit_gold_amber` cattails), wind-swaying shoreline silhouette |
| 12 | `decal_moss_skirt` | `96x96` | `1f` | All outdoor biomes | `FloorDecal` (`0.5, 0.5` pivot): soft irregular moss/clover ring (`sage_olive_moss`), zero dark border, anchors trees/ruins to terrain |
| 13 | `decal_pebbles` | `96x96` | `1f` | `RoadAmbush`, `RuinsOutskirts`, `Town` | `FloorDecal`: scattered gravel, cracked road pebbles & earth grit (`warm_cobble_gray` / `sunlit_sandstone`), feathers road edges |
| 14 | `decal_fallen_leaves` | `96x96` | `1f` | `DeepWoods`, `Town` | `FloorDecal`: crisp autumn/oak leaf litter & twigs (`terracotta_russet` / `sunlit_gold_amber` / `sage_olive_moss`) beneath tree canopies |

#### Priority P1 - Biome Signatures, Road Barricades & Interactive Props (11 Assets)

| # | Asset ID | Size (px) | Frames | Target Biome(s) | Sunlit-128 & Art Direction Notes |
| :---: | :--- | :---: | :---: | :--- | :--- |
| 15 | `palisade_stakes` | `128x96` | `1f` | `RoadAmbush` | Bandit sharpened timber roadblock (`2.0m x 1.5m`), lashed crossbeam & rope bindings (`timber_leather_earth`) |
| 16 | `cart_wreck` | `192x128` | `1f` | `RoadAmbush` | Splintered overturned merchant cart with broken spoke wheel (`3.0m x 2.0m`), muted burlap cargo (zero neon highlights) |
| 17 | `hay_bale` | `96x80` | `1f` | `RoadAmbush`, `Town` | Twine-bound golden-straw hay bale (`1.5m x 1.25m`, `sunlit_gold_amber` / `sunlit_sandstone`), flammable cover potential |
| 18 | `oil_keg_cluster` | `96x80` | `1f` | `RoadAmbush`, `RuinsOutskirts`, `DungeonChamber` | Pitch-stained iron-banded oil casks with red wax hazard seal (`crimson_scarlet` band), pairs with `SurfaceType.Oil` combos |
| 19 | `driftwood_snag` | `128x80` | `1f` | `RiverbankCrossing` | Bleached river-washed log snag (`2.0m x 1.25m`), pale `warm_cobble_gray` wood + wet dark mud base |
| 20 | `half_sunk_rowboat` | `160x96` | `1f` | `RiverbankCrossing` | Tilted weathered wooden skiff half-beached in river mud (`2.5m x 1.5m`), strong tactical bank cover |
| 21 | `fallen_statue` | `160x96` | `1f` | `RuinsOutskirts`, `DungeonChamber` | Toppled cracked knight/guardian stone statue torso (`2.5m x 1.5m`), moss-veined sandstone/slate |
| 22 | `rubble_pile_b` | `112x80` | `1f` | `RuinsOutskirts`, `DungeonChamber` | Secondary broken column-drum & slate-slab pile (`1.75m x 1.25m`), avoids mirror-identical rubble repeats |
| 23 | `rune_obelisk_4f` | `384x160` | `4f` | `RuinsOutskirts`, `DungeonChamber` | 4x `96x160` carved monolith pulsing with `arcane_teal_cyan` runes (`_EmissionWarmMask` / cyan glow mask at night) |
| 24 | `decal_cracked_stone` | `96x96` | `1f` | `RuinsOutskirts`, `DungeonChamber`, `Town` | `FloorDecal`: radiating flagstone fractures + dark mortar cracks to break flat tile grids |
| 25 | `decal_shore_foam` | `128x64` | `1f` | `RiverbankCrossing`, `Town` | `FloorDecal`: soft organic scalloped wet-mud & foam transition strip (`cerulean_water` + `river_mud`) to soften straight bank edges |

#### Priority P2 - Dungeon Chamber Atmosphere & Town Life (5 Assets)

| # | Asset ID | Size (px) | Frames | Target Biome(s) | Sunlit-128 & Art Direction Notes |
| :---: | :--- | :---: | :---: | :--- | :--- |
| 26 | `bone_pile` | `80x64` | `1f` | `DungeonChamber`, `RuinsOutskirts` | Scattered ribcages, skulls, rusted shield rim (`warm_skin_parchment` + `slate_indigo_shadow`), low non-blocking dressing |
| 27 | `iron_cage` | `96x128` | `1f` | `DungeonChamber`, `RuinsOutskirts` | Rusted wrought-iron gibbet/prison cage (`1.5m x 2.0m`, `gleaming_steel` darks + `terracotta_russet` rust) |
| 28 | `dungeon_floor_grate_rusted` | `96x96` | `1f` | `DungeonChamber` | `FloorDecal`: high-contrast iron drainage grate over dark void (`#11131F`), crisper depth than current flat grate |
| 29 | `town_notice_board` | `128x144` | `1f` | `Town` | Timber quest/bounty board (`2.0m x 2.25m`) with pinned parchment scrolls & wax seals near Bywater square |
| 30 | `town_lantern_post_4f` | `256x160` | `4f` | `Town`, `RoadAmbush` | 4x `64x160` wrought-iron streetlamp swaying gently with warm amber flame (`_EmissionColor`), replaces generic braziers in town |

---

## Section 4 - Tactical RPG Game Designer Review (Beyond Visuals)

Below are the **7 highest-leverage gameplay & UX improvements**, tagged explicitly by how they pair with `W-253`'s **Deployment + 6 AP + Equipment Sets** pass vs follow-on work.

### 1. Scouting-Driven Tactical Deployment Advantage *(Directly Complements W-253 Deployment)*
- **Current State (`M1_Bywater_Scout_Intercept_Proof.png`, `EncounterDirector.cs`):** Succeeding on scouting (`Kaelen`/`Vex`) only yields `PartyAmbush` Round-1 initiative bonus while dropping heroes onto fixed `preferredHeroSlots` (`BywaterGameController.cs`).
- **Design Upgrade:** Connect `EncounterScoutingReport.CanAmbush` directly to `W-253`'s Deployment Grid:
  - Normal/Neutral encounter: `3x6` rear deployment rectangle.
  - **Party Ambush (`CanAmbush`)**: Expand deployment to include elevated roadside bluffs (`ElevationTier = 1`) and flank cover behind `boulder_moss` / `fallen_hollow_log`, AND display enemy Round-1 `PredictNextIntent` threat lines **during Deployment** so players can counter-position tank `Aldric`/`Seraphina` in front of squishies (`Theron`/`Liora`) before Round 1 begins.
  - **Enemy Surprised Party (`IsAmbushedInDark`)**: Tight clustered deployment box inside enemy archer LoS, making torches genuinely tactical.

### 2. Asymmetric `6 AP` Action & Combo Economy *(Directly Complements W-253 6 AP)*
- **Current State (`CombatModels.cs`, `M1_Bywater_Combat_Preview.png`):** Rigid `MoveRemaining` + `1 Standard` + `1 Swift` forces identical turn cadence across classes (`Theron` shows `9/9 Act | Swf`, `Vex` shows `12/12 Act | Swf`).
- **Design Upgrade:** With `W-253`'s unified `6 AP` pool, make action pricing create real tactical trade-offs:
  - `1 AP`: `Step (1 tile)`, `Shove` (low-cost positional enabler!), `Item/Torch Light`.
  - `2 AP`: `Off-Hand / Dagger Strike`, `Potion`, `Darts / Cantrip`.
  - `3 AP`: `Primary Weapon Strike` (Sword/Bow), `Guard / Overwatch` (converts each remaining unspent `AP` into `+2 AC` or an extra Overwatch intercept).
  - `4 AP`: Heavy/AoE spells (`Fireball`, `Heal Medium Wounds`).
  - **Class Identity Payoff:** `Vex (Rogue)` can `Step (1 AP) + Flanking Strike (2 AP) + Shove into Oil (1 AP) + Dagger (2 AP)`, whereas `Aldric (Knight)` trades mobility for `3 AP` Shield Strike + `3 AP` Guard (`+6 AC`).

### 3. Build-Defining Equipment Set Mechanics *(Directly Complements W-253 Equipment Sets)*
- **Current State (`M1_Bywater_PaperDoll_Camp_Proof.png`):** Gear is pure flat math (`+9 AC`, `1d8 Dmg`), so `Equip Best (Party)` removes interesting choice.
- **Design Upgrade:** Ensure `W-253`'s Equipment Sets grant **behavioral combat triggers** at `2-piece` and `3-piece` thresholds rather than boring `+2 AC`:
  - *Bywater Vanguard (Helm + Shield + Armor):* Ending turn with `>= 2 AP` unused triggers free `Guard (+4 AC)` and grants adjacent allies `+2 AC`.
  - *Thornwood Stalker (Bow + Cloak + Boots):* Firing from `PartialCover` or `ElevationTier >= 1` refunds `1 AP` once per turn.
  - *Cryptbreaker (Hammer/Mace + Gauntlets):* `Shove` knocks targets `2` tiles and applies `Dazed` (`-2 AP` on target's next turn) on wall/obstacle impact.
  - Surface set counts (`[2/3]`) directly on Paper-Doll slot headers and Victory upgrade arrows (`Victory_Loot_Proof.png`).

### 4. Rebalance Early Monster HP & Add Telegraphed Role Mechanics *(Next Sprint - High Priority)*
- **Observed Bug in Screenshots (`M1_Bywater_Combat_Preview.png`, `River_Proof.png`, `Road_Proof.png`):** Top turn order shows **`Orc Warrior HP 2/2` to `4/4`** and **`Goblin HP 4/4` to `7/7`**, while **`Giant Frog` has `23 HP`**! An armored `Orc Warrior` dying in 1 dagger poke breaks visual expectations and trivializes ambushes.
- **Design Upgrade:**
  - Tune Bywater Tier-1 monster HP/role identity to match sprite silhouette: `Goblin Raider` (`8-10 HP`, nimble flanker), `Goblin Archer` (`6-8 HP`, prioritizes high-ground bluff), `Orc Warrior` (`18-24 HP`, heavy armor + telegraphed **2-tile Cleave** floor warning), `Giant Frog` (`16-18 HP`, **Tongue Pull** pulling backline casters off ridges into mud!).
  - Display crisp role badges (Tank / Skirmisher / Ranged / Controller) on top turn-order portraits.

### 5. Destructible & Chain-Reactive Terrain Props (`Oil Keg` + `Shove` Hazard Previews) *(Next Sprint - Pairs with Manifest P1)*
- **Current State:** `EncounterDirector` spawns `LitBrazier` + `Oil` pools (`BuildRoadAmbushTemplate`) and `Shove` supports ledge/wall crush, yet players have no visual clue why `Shove [V]` is exciting.
- **Design Upgrade:**
  - Make `oil_keg_cluster` (`P1 #18`) a `1 HP` tactical object: shooting it with `Flame` / `Fireball` or using `Shove` to slam it into a `LitBrazier` detonates a `3x3` `SurfaceType.Fire` blast (`2d6` AoE).
  - When hovering `Shove [V]`, draw a bold **Knockback Trajectory Arrow + Impact Badge** on the destination tile (`WALL CRUSH +1d6`, `WATER +2 Move Cost / Shock`, `FIRE +Burn`, `LEDGE FALL +1d6`) so tactical combos become self-teaching.

### 6. Survival Loop Cohesion: Rations & Torch Scarcity *(Next Sprint)*
- **Current State:** `Icons/icon_food_ration.png` exists in assets with zero gameplay hook, while `Torches` cost a trivial `1g` in stash (`PaperDoll_Camp_Proof.png`), removing any real night/dungeon tension.
- **Design Upgrade:**
  - Give `Food Rations` a real role in `CampMode.FullRest`: resting outdoors/in dungeon consumes `1 Ration + 1 Torch` (`Free` in Bywater Inn/Town); resting without Rations cures HP/SP but inflicts `Hungry (-1 Starting AP)` until fed.
  - Raise Torch price from `1g` -> `12-15g` so lighting a torch (`+30t`) or deciding to risk `DARK DANGER!` (`+25% Night Reward Bonus`) is a genuine economic/risk decision.

### 7. Combat HUD Readability, Grid Cleanup & Micro-Juice *(Immediate Polish alongside W-253)*
- **Exact UI/UX Fixes from Screenshots:**
  - **Zero Clipped Text:** Fix `Short Swor` (`NightCombat_Readable_Proof.png`), `[Theron] Approach & Melee: Giant Fro` (`Combat_Preview.png`), `...for 12 t` in `Combat Log [L]`, and `Shared Stash` potion descriptions (`PaperDoll_Camp_Proof.png`) via `enableAutoSizing` + cleaner concise formatting (`Heal Med (3)` instead of verbose truncated sentences) + `Wrap` on Combat Log lines.
  - **Replace Cyan Box Grid with Corner Ticks + AP Path Pips:** Replace loud full-square cyan lines (`Combat_Preview.png`, `River_Proof.png`) with subtle `20%` alpha corner-tick `L`-brackets (`#38BDF8` reachable / `#FACC15` attack-move / `#EF4444` AoO danger) and show crisp step-by-step `1 AP` / `2 AP` cost pips on the hovered movement path.
  - **Clean Non-Caster Portrait Strip:** Replace `SP -` on `Aldric`, `Kaelen`, and `Vex` (`Explore_Preview.png`) with either an `AP` / `Guard` badge or omit the empty track so caster `SP` bars stand out cleanly.
  - **Impact Juice:** Add `40ms` hit-stop + `0.07m` damped camera punch + pixel dust ring on `Shove` impacts, crits, and `Fireball` detonations (`BywaterCameraRig` + `BywaterUnitView`).
