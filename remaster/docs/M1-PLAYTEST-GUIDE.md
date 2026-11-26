# Realmz HD-2D Remaster - Milestone 1 (City of Bywater) 10-Minute Playtest Guide

Use this guide to run the human playtest exit gate for Milestone 1 (`M1_Bywater_Slice.unity`). Launching with `REALMZ_RNG_SEED` makes encounters, rolls, and combat deterministic so any bug or feel note can be replayed verbatim.

---

## 1. Launch Standalone Build (Reproducible Seed)

```powershell
$env:REALMZ_RNG_SEED = "20261125"
Start-Process "C:\Users\karti\realmz\remaster\builds\Realmz_M1\Realmz.exe"
```

If you want to capture your own session notes or log issues, note the **tile `(x, y)`**, **in-game time**, and **combat round/turn** alongside `REALMZ_RNG_SEED=20261125`.

---

## 2. 10-Minute Vertical-Slice Checklist (7 Beats)

### Beat 1: Town Arrival & Indoor Shops (`City of Bywater`)
- Walk through Bywater (`WASD` / click-to-move) and test crisp step responsiveness + `Mystic highlands` town music crossfading cleanly to fixed-pitch `after-the-rain` music outside town gates.
- Click a Shop/Temple doorway (e.g., Armory/Blacksmith, Tanner, Apothecary, Shrine) or press `[E]` nearby: verify the Shop/Temple modal opens with merchant portrait/dialogue cleanly inside.
- **Check:** Mouse-wheel scroll zoom and keyboard pan stay locked while Shop is open; closing restores camera control cleanly.

### Beat 2: Overworld Exploration, Day/Night & 3-State Fog-of-War
- Leave the Bywater safe zone onto the wild road / Haunted Quarters / meadow.
- Confirm Bywater town tiles never trigger ambushes, while wild roads/woods advance the `1,440`-minute clock (`+2` min/step) and update 3-state Fog-of-War (`Unseen` -> `Explored` -> `CurrentlyVisible`), keeping distant chimney smoke hidden inside pitch-black `Unseen` tiles.
- **Check:** Dusk river water reads naturally (tinted glints, soft bank foam) with zero 1px skybox hairlines along cobble/grass borders.

### Beat 3: Spoiler-Free Intercept & BG3 `d20` Rolls
- Continue until an encounter triggers (or Dusk/Night ambush): verify the intercept modal shows terrain flavor without revealing clock/torch spoilers, displays `Evade [DEX DC X]` / `Quick Resolve [Tier | DC X]`, and animates a BG3-style `d20` check modal!

### Beat 4: Pre-Battle Deployment & Option A D&D 5e Tactical Combat
- In Pre-Battle Deployment, verify only the selected hero shows the active ring (not unselected heroes), click hero sprites (head/body) or tiles in the curated 8-slot zone to swap/reposition, and press `Start Battle [Space]`.
- In **Haunted Quarters** combat, verify ruins stone features (`RuinsOutskirts`) instead of road supply wagons.
- Verify **Option A** resources (`MP` movement + `ACT` Main Action + `BON` Bonus Action + `RCT` Reaction + **`Dash [D]`** + Iron Vanguard Bonus Action `Guard`), zero distracting red enemy intent arcs, and **Green / Yellow / Red + `X` dotted LOS sightlines** when aiming Ranged attacks or Spells!

### Beat 5: Option 2 Victory Screen & `[P]` Paper-Doll
- Finish the encounter and verify the Victory modal:
  - `4`-across compact pre-checked loot cards (`Bow` stats not clipped with `...`).
  - Green up-arrow class-valid tags, gold up-arrow & heart-cross symbols beside `Lv`, Gold Coins icon, zero `MVP`.
  - `Take Selected` deposits checked items to party stash (manual equip in `[P]`, never auto-equipped).
- Open `[P]` Party/Inventory: verify 9-slot Paper-Doll, class proficiencies, and modal scroll-zoom lock.

### Beat 6: Dungeon Portal `(89, 48) <-> (33, 72)`
- Travel east to the portal at `(89, 48)` and enter `dungeon_0` (`(33, 72)`).
- **Check:** Ambience/music crossfade (`RealmzMasterMixer`), torchlight chiaroscuro inside the dungeon, and clean return transition back to `land_0`.

### Beat 7: Save / Load v4 (`F5` / `F9`)
- Verify saving/loading is blocked during active combat.
- Outside combat, press `F5` to save, take a few steps or toggle torch, and press `F9` to load.
- **Check:** Position, party formation, 3-state Fog-of-War, weather, `worldTimeMinutes`, HP, active conditions, and item `RemainingCharges` all restore accurately.

---

## 3. Quick Playtest Triage Scorecard

| Category | Rating (1-5) | Notes (Seed, Tile, Turn) | Action (`Fix for M1` / `Defer to M2`) |
| :--- | :---: | :--- | :--- |
| **Core Feel & Fun** | | | |
| **Tactical Readability & 6-AP Clarity** | | | |
| **Survival Tension (Torch / Fog / Day-Night)** | | | |
| **UI, Menus & Combat Log** | | | |
| **Audio, VFX & Camera Juice** | | | |
| **Bugs / Friction Spotted** | | | |
