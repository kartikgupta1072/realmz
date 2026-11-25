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

### Beat 1: Town Arrival & NPC Trade (`City of Bywater`)
- Walk through Bywater (`WASD` / click-to-move) and check cobblestone road edges, building solidity, and `Borin`'s human scale next to `Sir Aldric`.
- Hover/click `Master Blacksmith Borin`: verify the `[E / Click] Trade` prompt fits cleanly and opens the Shop modal.
- **Check:** Mouse-wheel scroll zoom and keyboard pan stay locked while Shop is open; closing restores camera control cleanly.

### Beat 2: Overworld Exploration, Day/Night & 3-State Fog-of-War
- Leave the Bywater safe zone onto the wild road / meadow.
- Confirm Bywater town tiles never trigger ambushes, while wild roads/woods advance the `1,440`-minute clock (`+2` min/step) and update 3-state Fog-of-War (`Unseen` -> `Explored` -> `CurrentlyVisible`).
- **Check:** Dusk river water reads naturally (tinted glints, soft bank foam) with zero 1px skybox hairlines along cobble/grass borders.

### Beat 3: Scouting & Night Torchlight Tension
- Continue until dusk/night or rest/step into darkness; toggle Aldric's Torch and observe warm light radius vs dark outskirts.
- Trigger an encounter (or Scouting Intercept Modal): check `AMBUSHED IN THE DARK!` penalty when unlit vs scouting advantages when lit.

### Beat 4: Pre-Battle Deployment & 6-AP Tactical Combat
- In Pre-Battle Deployment, swap two heroes across Front/Mid/Back tiles and press `Start Battle [Space]`.
- Test `[T]` tactical pitch toggle (`48 <-> 64 deg`), corner-tick grid brackets, subtle move-path `1 AP` pips, and `Combat Log [L]` (confirm `[R1]` prefix clears the bottom hotbar even on `Theron`'s wide caster bar).
- Test `Shove [V]` and trigger an `oil_keg_cluster` or `LitBrazier` / oil slick hazard; check hit-stop, Cinemachine impulse, sound jitter, and ragged (non-square) oil puddle borders.

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
