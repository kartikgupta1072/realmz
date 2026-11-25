# Art Director 5-Pillar Visual QA Rubric

Reference baseline: `remaster/docs/realmz_stage2_grade_a_audit.md` and `remaster/docs/realmz_unity_gameplay_master_plan.md`.

## The 5 Visual Pillars

1. **Terrain, Cliffs & Water:**
   - Crisp `64 px/m` texel lock on meadow grass, dirt road, river mud, and cliff rock (`128x128` seamless).
   - Zero horizontal 1px skybox hairlines between elevation tiers (e.g. cobble/plaza/grass borders).
   - Vertical cliff UV jitter breaks horizontal banding; base crease AO grounds cliff edges.
   - Real `-0.28f` `IsWater` combat/overworld rivers with smooth shoreline foam and scene-light-attenuated water glints (no pitch-black Dusk water with unattenuated white speckles).

2. **Props, Obstacles & Ground Integration:**
   - Zero hollowed interior alpha holes on buildings, pine/oak trees, ruins, or barrels.
   - Biome-authentic arena cover (`BoulderMoss`, `FallenHollowLog`, `ThornBrambleThicket`, `BrokenArchway`, `PalisadeStakes`, `OilKegCluster`) - no suburban `StoneFenceH` planters in wild ambushes.
   - `0.5x` ground-skirt decals (`decal_moss_skirt`, `decal_pebbles`, `decal_fallen_leaves`, `decal_cracked_stone`, `decal_shore_foam`), contact shadows, and `RootCollar` grass rings tie props to terrain.
   - Normal-safe `_FlipX` keeps sunlit/moonlit facing consistent across flipped props and units.

3. **Characters, Monsters & Scale Consistency:**
   - Shopkeepers and NPCs match hero human scale (`1.20f`) beside Sir Aldric.
   - Enemies match tactical weight (`Orc Warrior` `18-24 HP` visually bulkier than `Goblin` / `Giant Frog`).
   - Crisp `Point` sampling with no soft bilinear blur or mixels.

4. **Lighting, Color & Atmospheric Readability:**
   - Smooth `1,440`-minute solar/lunar arc (`Day` / `Dusk` / `Night`).
   - Night Combat maintains warm brazier/torch chiaroscuro (`ambient 0.98/1.08`) plus `_GlobalNightRimStrength` moonlight silhouette separation on unlit flanks.
   - 3-state Fog-of-War cleanly distinguishes `Unseen`, `Explored` memory, and `CurrentlyVisible`.

5. **UI Typography, Icons & Readability:**
   - Subtle `20%` alpha corner-tick `L`-brackets on combat grid; clean hover path `1 AP` pips without opaque cream squares hiding terrain.
   - Zero text truncation/ellipsis on Victory Loot cards (`Bow`, `Short Swd`, etc.), NPC prompts, and combat tooltips.
   - Accurate role tags on Initiative Ribbon (`Vex` and `Goblin` as `Skirm`, never `Tank`).
   - Option 2 Victory Screen: `4`-across pre-checked cards, green up-arrow class tags, gold up-arrow & heart-cross beside `Lv`, Gold Coins icon, zero `MVP`, single `Take Selected` CTA.

## Defect Severity Definitions
- **P0 (Blocker):** Skybox mesh cracks, hollowed sprite alpha, broken water/lighting readability, unreadable/broken modal layout.
- **P1 (Must Fix for M1):** Text clipping (`...`), misclassified role tags, opaque path quads obscuring ground, missing/broken normal shading.
- **P2 (Polish):** Minor panel padding, non-blocking prop occlusion edge cases, secondary surface decal border softness.
