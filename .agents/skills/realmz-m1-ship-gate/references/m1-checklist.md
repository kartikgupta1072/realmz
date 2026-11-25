# Milestone 1 (City of Bywater) Completion Checklist

Use this checklist to sign off Milestone 1 gameplay, visuals, and technical stability.

## 1. Exploration, Survival & World Loop
- [ ] `90x90` `land_0` Bywater overworld and `dungeon_0` interior connected via portal `(89,48)<->(33,72)`.
- [ ] Town safety invariant: Bywater town tiles have zero random combat ambushes (`100%` safe).
- [ ] Continuous `1,440`-minute Day/Night arc (`1.0` in-game min/sec outdoors, `+2` min/step, `+480` min Full Rest) with smooth solar/lunar lighting and Aldric torchlight radius.
- [ ] 3-state `FogOfWarMap` (`Unseen` / `Explored` / `CurrentlyVisible`) updates `_FogOfWarTex` cleanly across terrain, water, and billboards.
- [ ] `EncounterDirector` step rolls, `10`-step post-combat grace, `AMBUSHED IN THE DARK!` unlit penalty, Scouting Intercept Modal, and `WeatherState` behave deterministically.
- [ ] `BywaterSaveService` v4 saves and reloads formation, fog-of-war, weather, and `worldTimeMinutes` with backward compatibility.

## 2. Tactical Combat & 6-AP Contract
- [ ] `BaseApPerTurn = 6` strictly fixed (zero Agility/Fatigue AP modifiers); `MaxCarryoverAp = 2`.
- [ ] Only Equipment Sets (`Scout's Leather`, `Iron Vanguard`) and gear modify AP or Free Move.
- [ ] Front/Mid/Back Pre-Battle Deployment works cleanly (`Start Battle [Space]`).
- [ ] 5 deterministic `CombatArenaArchetype` templates (`DeepWoods`, `RoadAmbush`, `RiverbankCrossing`, `RuinsOutskirts`, `DungeonChamber`) spawn biome-appropriate props and real `-0.28f` `IsWater` river tiles.
- [ ] `CombatDestructibleProp` (`oil_keg_cluster`, crates, braziers) and surface combos (`Water` chain lightning, `Oil` + fire, `Shove [V]`) trigger deterministic damage and VFX/hit-stop.

## 3. RPG Systems, HUD & Option 2 Victory Screen
- [ ] `[P]` Party/Inventory 9-slot Paper-Doll, class proficiencies, and manual equip work with scroll-zoom lock active.
- [ ] NPC interaction prompts (`Borin`, Tanner, Apothecary, Priestess) navigate adjacent cleanly and open Shop/Temple modals without clipping.
- [ ] Victory Screen matches Option 2 contract: `4`-across compact cards, default selected, auto-sized inline stats (zero ellipsis), green up-arrow `Hero` class-valid tags, gold up-arrow & heart-cross symbols beside `Lv`, Gold Coins icon, zero `MVP`, animated XP fill, manual equip in `[P]`.
- [ ] Role tags on Initiative Ribbon accurately reflect hero/monster roles (`Vex` / `Goblin` not mislabeled `Tank`).

## 4. Audio, Camera & Polish
- [ ] `Unity.Cinemachine` `3.1.7`: `[T]` `48<->64 deg` pitch toggle, directional impact impulses, hit-stop.
- [ ] `RealmzMasterMixer`: `1.5s` music crossfades, spatial river/forge/torch beds, pitch-jittered combat SFX.
- [ ] `RealmzBalanceCatalog` SO + `balance_overrides.json` loaded cleanly.

## 5. Build, Performance & Stability
- [ ] `RealmzCore` (`netstandard2.1`) has zero `UnityEngine`/`UnityEditor`/NuGet refs and `RealmzCore.dll` matches Release SHA-256.
- [ ] Strictly 1 `MonoBehaviour` per `.cs` file in `Assets/Scripts/`.
- [ ] EditMode (`CartographyEditorTests` + `RealmzM1EditorTests`) and PlayMode (`RealmzM1PlayModeTests`) pass `100%` with `< 3.0 ms` added frame delta (`>= 60 FPS`).
- [ ] Standalone `Realmz.exe` builds with `0` warnings and survives 8s smoke run with `0` `Player.log` errors.
