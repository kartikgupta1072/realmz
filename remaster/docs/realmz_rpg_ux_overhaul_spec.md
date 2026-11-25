# Realmz Remaster: RPG UX Overhaul Specification (Items #7-#10)

**Target Codebase:** `C:\realmz\remaster` (`RealmzCore` + `RealmzUnity`, City of Bywater M1)
**Scope:** Camp/Rest Economy & Compact UI (#7), Item Slot Forensics & Paper-Doll Inventory Overhaul (#8), Character Sheet & Interactive Level-Up System (#9), and Rich Tactical Victory & Loot Summary Screen (#10).
**Design Benchmarks:** *Baldur's Gate 3* (paper-doll slot clarity, hover diff tooltips, party-wide portrait switching, level-up preview), *Pillars of Eternity II* (stacked stash, clean stat breakdowns), *Battle Brothers* (compact loot icon grid, immediate post-battle stat/injury readability), *Songs of Conquest* (pixel-art portrait victory showcase, animated XP bars), *Solasta* (tabletop-faithful level-up summary & deterministic HP option).
**Palette & Visual Continuity:** Preserves `BywaterUiHelpers` dark-slate + brushed-bronze aesthetic (`PanelDarkBg #080C16`, `PanelBg #0C111E`, `BrassAccent #C09142`, `BrassHighlight #F5CC63`, `ParchmentText #F6EEDD`, `SuccessGreen #47D973`, `DangerRed #EB5247`, `InfoCyan #59D1F5`) on the `1920x1080` Canvas, using existing `32x32` sprites in `Assets/Art/Cartography/Icons/` (101 authored icons) and `128x128` hero portraits in `Assets/Art/Cartography/Portraits/`.

---

## 1. Item #7: Camp, Quick Bandage & Full Rest Rules + Compact UI

### 1.1 Forensics: Current Implementation in Code
Audited across `RealmzCore/Exploration/PartyAndInventory.cs` (L589-641), `RealmzUnity/Assets/Scripts/Game/BywaterGameController.cs` (L1794-1845, L2162-2172), and `RealmzUnity/Assets/Scripts/Game/BywaterHudController.cs` (L3520-3538):

| Action | Current Cost | Current Effect | Current Problems |
| :--- | :--- | :--- | :--- |
| **Quick Bandage** (`CampMode.QuickBandage`) | **100% Free, Unlimited** (0 gold, 0 turns, 0 supplies, 0 ambush risk) | Heals living allies (`Hp > 0`) by `Max(2, MaxHp / 3)` (33% HP). If `TorchTurnsRemaining <= 0`, sets `TorchTurnsRemaining = 30` **for free** without consuming a Torch (`itemId = 805`). Does not restore SP, revive fallen allies, or clear conditions. | Completely overshadowed by free unlimited `Full Rest`; also breaks torch economy by conjuring infinite 30-turn torches out of thin air. |
| **Full Rest** (`CampMode.FullRest`) | **100% Free, Unlimited** (0 gold, 0 turns, 0 torches, 0 rations, 0 ambush risk) | Revives fallen allies (`Hp <= 0` -> `MaxHp`), restores **100% HP and 100% SP**, clears **all `ExhaustionStacks`** and **all `Conditions`** (`m.Conditions.Clear()`, leaving only `CurseStacks`). | Makes `Quick Bandage`, Healing Potions (`611`/`612`), Refresh Potions (`723`), and Bywater Temple Heal (`25g`) completely obsolete outside combat. |
| **Camp UI** (`BywaterHudController.cs:3520-3538`) | N/A | Two wide text-only buttons (`QuickBandageBtn` 160px, `FullRestBtn` 130px) occupying the top bar of `StashCampCol` with zero icons, zero cost indicators, zero remaining-use counters, and zero tooltips. | Wastes prime right-panel real estate, provides zero mechanical transparency (player cannot tell why two buttons exist or what they cost), and crowds out stash controls. |

### 1.2 Recommended RPG Rules (Classic *Realmz* Spirit, Modern Clarity)
Introduce a clear tactical distinction between **Field First Aid (Quick Bandage)** and **Campfire Rest (Full Rest)**, wired to existing Bywater resources (`Torch` #805 at 3g with 6 charges, `TurnCount`, and `EncounterZones`):

1. **Quick Bandage (Field First Aid):**
   - **Charges:** `PartyState.QuickBandageCharges` (integer, `MaxQuickBandageCharges = 2`, starts at `2`). Refills to `2/2` on `Full Rest`, Temple Heal, or entering a Safe Town zone.
   - **Cost:** Consumes `1` Quick Bandage charge and advances `+1` exploration turn (`ExplorationSession.TurnCount += 1`). Zero gold, zero torches, zero ambush risk.
   - **Effect:** Heals all living allies (`Hp > 0`) by `Max(3, (int)Math.Ceiling(m.MaxHp * 0.35))` (35% Max HP, rounded up). Does NOT revive fallen (`0 HP`) allies, does NOT restore SP, does NOT clear Exhaustion/Poison/Curse, and does **NOT** conjure a free torch.
   - **When Charges = 0:** Button remains visible but dimmed with tooltip: `"No field bandages left (0/2). Full Rest or visit Bywater Town to resupply."`
2. **Full Rest (Campfire Rest):**
   - **Safe Town Zone (Bywater town tiles / Hearth / Camp POI):**
     - **Cost:** Free (safe hearth rest) and advances `+8` exploration turns. **0% ambush chance.**
   - **Wilderness / Dungeon (`!isSafeTownZone`):**
     - **Cost:** Consumes **1 Torch charge** (deducted from stash `Torch` #805 `RemainingCharges`, removing the torch when charges hit 0; if no stash torch exists, consumes `15` active `TorchTurnsRemaining` if `>= 15`). Advances `+8` exploration turns.
     - **If no Torch charge and `< 15` active Torch turns:** Button is disabled with red badge `"Need Torch"` and tooltip `"Buy Torches (3g, 6 uses) at Bywater Armory to make camp in the wilds."`
     - **Ambush Risk:** `0%` in town or cleared zones; `15%` ambush roll (`Roller.NextInt(1, 101) <= 15`) if resting inside an **uncleared** `EncounterZoneData` (ambush triggers that zone's battle before rest completes, classic *Realmz* / *BG3* tension).
   - **Effect (on undisturbed completion):**
     - Restores all party members to **100% HP** and **100% SP** (revives fallen allies from 0 HP).
     - Clears all `ExhaustionStacks` and temporary combat buffs/debuffs (keeps `CurseStacks`, `CharConditionId.Poisoned`, and `CharConditionId.Diseased` so `Heal Poison` potions and the Bywater Temple Shrine `20g/25g` remain meaningful!).
     - Refills `QuickBandageCharges` back to `2/2`.
     - Sets `TorchTurnsRemaining = Math.Max(TorchTurnsRemaining, 20)` if a Torch charge was burned for the campfire (representing the relit torch taken from camp).

### 1.3 Compact UI Layout (Freeing 80%+ of Right Panel)
Remove `CampBar` from the right column (`StashCampCol`) entirely. Move both rest actions into the **Party Drawer Top Header Bar** (`44px` tall, right next to `Gold` and `Torch` chips) as compact **`44x44` / slim icon+badge pills** (`~110px` wide each, or `48x48` square icon buttons with corner count badges):

```text
+---------------------------------------------------------------------------------------------------+
| Vanguard Party & Gear        [Bandage 2/2] [Camp Rest (12)]   Gold: 400g | Torch: 30t    [Close]  |
+---------------------------------------------------------------------------------------------------+
```

- **Quick Bandage Pill (`48x38` compact button):**
  - Icon: `icon_heal.png` (`24x24` left) + dynamic badge `"2/2"` (green when `> 0`, muted red when `0/2`).
  - Hover Tooltip (reusing `BywaterHudController`'s floating cursor tooltip):
    - Title: `Quick Bandage (2/2 left)`
    - Body: `Heals living allies +35% HP. Cost: 1 charge, +1 Turn. Safe (0% ambush). Refills on Full Rest.`
- **Full Rest Pill (`48x38` compact button):**
  - Icon: `icon_torch.png` (`24x24` left) + dynamic badge: `"Safe"` (green in Bywater Town) or `"T:12"` (amber total torch charges available in stash/active; red `"0"` if unable to rest).
  - Hover Tooltip:
    - Title: `Full Rest (Campfire)`
    - Body (Town): `Restores 100% HP & SP, clears Exhaustion, refills Bandages (2/2). Cost: Free in Bywater (+8 Turns, 0% ambush).`
    - Body (Wilds/Dungeon): `Restores 100% HP & SP, clears Exhaustion, refills Bandages (2/2). Cost: 1 Torch charge (+8 Turns). Ambush Risk: 0% (cleared) / 15% (hostile zone).`
- **Space Reclaimed:** Removing the camp bar from `StashCampCol` dedicates **100% of the right column** (`~680x690px`) to the Shared Stash filter tabs, sort bar, `Equip Best` actions, and dense stacked item grid.

---

## 2. Item #8: Slot Bug Forensics + Paper-Doll Equipment & Stash Overhaul

### 2.1 Exact Bug Forensics: Why Boots & Gloves Equip into `Helm`
1. **Enum Bottleneck (`RealmzCore/Exploration/PartyAndInventory.cs:8-15`):** `EquipmentSlot` only defines **5 slots**: `Weapon (0)`, `MissileWeapon (1)`, `Armor (2)`, `Shield (3)`, `Helm (4)`.
2. **M1 Extractor Collapse (`spells_items_m1.json` & `items_by_id_icon_map.json`):** Classic *Realmz* (`src/realmz_orig/wear.c`) supported 16+ distinct equipment types (`armor[0..19]`, including separate slots for Head, Hands, Feet, Cloak, Quiver, Ring 1, Ring 2, Amulet). During M1 JSON extraction, all non-shield items in IDs `400-499` were collapsed to `category: "Helm", slot: "Helm"`, `Padded Cloak (250)` was collapsed to `slot: "Armor"`, and permanent `+2 AC` passive magic items (`622`, `675`, `charges: -1`) were dumped into `slot: "Consumable"`.
3. **Parser Routing (`PartyMemberState.TryParseSlot`, L351-392):** Because `TryParseSlot` only matches those 5 strings, `Leather Gloves (452)`, `Leather Boots (454)`, and `Steel Shod Boots (455)` equip directly into `EquipmentSlot.Helm` (replacing the hero's helmet!), `Padded Cloak (250)` replaces chest armor (`Chain Armor`/`Leather Armor`), and `Speckled Luck Stone +2 (622)` & `Quiver of Protection +2 (675)` fail `TryParseSlot` AND lack `HealDice`, making them **100% un-equippable and un-usable dead items**.
4. **Bonus Bug Found - Charged Consumable Unstacking (`PartyAndInventory.cs:474-487`):** `PartyState.AddItemToStash()` has `if (charges <= 0)` guarding its stacking loop! Because potions (`602, 611, 612, 723` have `charges: 2-3`) and torches (`805` has `charges: 6`) have `charges > 0`, identical full-charge items **never stack**. Starting stash `[611, 611, 611, 611, 612, 602, 805, 805, 803, 1]` renders as **4 separate rows** of `Heal Small Wounds x1` and **2 separate rows** of `Torch x1`.

### 2.2 Complete Audit of All 58 Items in `spells_items_m1.json`

#### A. Confirmed Misclassified / Broken Items (12 Items Requiring Fixes)
| ID | Item Name | Current `category` / `slot` | Stats | Bug Diagnosis | Fix (`category` / `slot` / behavior) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **452** | **Leather Gloves** | `Helm` / `Helm` | `+2 AC, Wt 12, 5g` | Equips on Head; overwrites Helm | `Gloves` / **`Hands`** |
| **454** | **Leather Boots** | `Helm` / `Helm` | `+1 AC, Wt 40, 25g` | Equips on Head; overwrites Helm | `Boots` / **`Feet`** |
| **455** | **Steel Shod Boots** | `Helm` / `Helm` | `+2 AC, Wt 65, 35g` | Equips on Head; overwrites Helm | `Boots` / **`Feet`** |
| **250** | **Padded Cloak** | `Armor` / `Armor` | `+2 AC, Wt 55, 25g` | Overwrites Chest Armor (+9 to +21 AC) | `Cloak` / **`Accessory2`** (Cloak/Amulet) |
| **622** | **Speckled Luck Stone +2** | `Magic` / `Consumable` | `+2 AC, Wt 1, 750g, chg=-1` | Permanent passive charm; cannot equip or use! | `Accessory` / **`Accessory1`** (Ring/Charm) |
| **675** | **Quiver of Protection +2** | `Magic` / `Consumable` | `+2 AC, Wt 70, 750g, chg=-1` | Permanent wearable item ("wear... for magical benefits"); cannot equip or use! | `Accessory` / **`Accessory2`** (or flexible `Accessory`) |
| **603** | **Heat Resistance** | `Magic` / `Consumable` | `chg=3, 300g, heal=""` | Category is `Magic` not `Potion`, no `HealDice` -> `[Use]` hidden & no handler | `Potion` / `Consumable` (Use grants Fire/Heat Resist buff) |
| **723** | **Refresh Potion** | `Potion` / `Consumable` | `chg=2, 500g, heal=1d6+2` | Description says clears fatigue, but Use only heals HP | Keep `Potion` / `Consumable`; Use clears `ExhaustionStacks` + restores `1d6+2` SP & HP |
| **747** | **Salt** | `Magic` / `Consumable` | `chg=20, 100g` | Tagged `Magic/Consumable` with no Use effect | `Supply` / `Utility` (or tactical combat throwable; remove misleading `Magic` tag) |
| **748** | **Pepper** | `Magic` / `Consumable` | `chg=20, 100g` | Tagged `Magic/Consumable` with no Use effect | `Supply` / `Utility` (or tactical combat throwable; remove misleading `Magic` tag) |
| **102** | **Quarter Staff** (sparring) | `Weapon` / `Weapon` | `1d5, Wt 60, 2H, junk` | Exact duplicate name with ID `98` (`Quarter Staff`, Wt 35); confuses stacked lists | Rename to **`Sparring Quarter Staff`** (`icon_quarter_staff_sparring.png` already exists!) |
| **2** | **Dagger of Shielding +2** | `Weapon` / `Weapon` | `1d4, +2 Hit, AC 0, 10000g` | Named "of Shielding +2" yet `armor_class_bonus: 0` (data polish) | Set `armor_class_bonus: 2` so it lives up to its name & 10,000g value |

*(Optional data polish: ID `8` `Dagger of Spells +1` at 3,200g can grant `+2` Spell Power / `+4 Max SP` once accessory/weapon passive hooks exist.)*

#### B. Verified Correct Items (Remaining 46 of 58 Items)
- **Melee Weapons (`Weapon` / `Weapon` - 23 items):** `1` Dagger, `8` Dagger of Spells +1, `10` Longsword, `11` Longsword +1, `20` Broadsword, `31` Scimitar, `37` Mace, `44` Nunchuka (2H), `59` Morning Star, `65` Flail, `75` Short Sword, `81` War Axe, `92` Battleaxe (2H), `98` Quarter Staff (2H), `99` Quarter Staff +1 (2H), `120` Spear (2H), `125` Club, `135` Staff (2H), `136` Stone Hammer, `137` Stone Axe, `138` War Hammer, `140` Pike Axe (2H), `151` Battle Hammer.
- **Ranged Weapons (`Weapon` / `MissileWeapon` - 7 items):** `87` Throwing Stars +1 (chg 24), `104` Bow (2H), `108` Bow +1 (2H), `109` Darts (chg 24), `113` Dart of Poison (chg 1), `144` Throwing Daggers (chg 12), `147` Throwing Hammer.
- **Body Armor (`Armor` / `Armor` - 6 items):** `200` Robe (+3 AC), `209` Leather Armor (+9 AC), `210` Leather Armor +3 (+12 AC), `211` Leather Armor +6 (+15 AC), `215` Chain Armor (+15 AC), `219` Banded Armor (+21 AC).
- **Headgear (`Helm` / `Helm` - 2 items):** `400` Helm (+3 AC), `450` Leather Cap (+2 AC).
- **Shield (`Shield` / `Shield` - 1 item):** `418` Shield (+6 AC).
- **Potions (`Potion` / `Consumable` - 3 items):** `602` Heal Poison, `611` Heal Small Wounds, `612` Heal Medium Wounds.
- **Supplies (`Supply` / `Utility` - 4 items):** `803` Quiver of Arrows, `805` Torch, `806` Parchment, `878` Rope.
*(Total: 12 audited fixes + 46 verified = **58 / 58 items accounted for**.)*

### 2.3 Canonical 9-Slot `EquipmentSlot` Architecture & Save Compatibility
Extend `EquipmentSlot` in `RealmzCore/Exploration/PartyAndInventory.cs` while keeping existing enum integers `0..4` unchanged:

```csharp
public enum EquipmentSlot
{
    Weapon = 0,        // UI: "Main Hand"
    MissileWeapon = 1, // UI: "Ranged"
    Armor = 2,         // UI: "Body"
    Shield = 3,        // UI: "Off-Hand" (blocked/auto-unequipped when 2H Weapon equipped)
    Helm = 4,          // UI: "Head"
    Hands = 5,         // UI: "Hands" (Gloves / Gauntlets)
    Feet = 6,          // UI: "Feet" (Boots / Greaves)
    Accessory1 = 7,    // UI: "Charm / Ring" (Luck Stones, Rings)
    Accessory2 = 8     // UI: "Cloak / Amulet" (Cloaks, Amulets, Protection Quivers)
}
```

- **`TryParseSlot` Aliases:**
  - `hands` / `gloves` / `gauntlets` -> `EquipmentSlot.Hands`
  - `feet` / `boots` / `greaves` -> `EquipmentSlot.Feet`
  - `accessory1` / `ring` / `charm` / `trinket` -> `EquipmentSlot.Accessory1`
  - `accessory2` / `cloak` / `amulet` / `neck` / `back` / `quiver` -> `EquipmentSlot.Accessory2`
  - Generic `accessory`: smart-resolves in `EquipFromStash` to `Accessory1` if empty, else `Accessory2` if empty, else swaps `Accessory1` (unless explicit `targetSlot` is passed).
- **Save-Game Backward Compatibility (`BywaterSaveService.cs` v2 -> v3):**
  - `RestoreMemberEquipment` already re-equips each saved item by looking up `itemDef.Slot` from `spells_items_m1.json` and calling `EquipFromStash`.
  - Therefore, any existing v1/v2 save where a hero wore `Leather Boots (454)` or `Leather Gloves (452)` in `Helm` automatically migrates into `EquipmentSlot.Feet` / `EquipmentSlot.Hands` on load with zero data loss!
  - Bump `CurrentSaveVersion = 3` and keep v1/v2 loaders intact.
- **Shop Category Filter Fix (`TownShopService` & `BywaterHudController`):**
  - Keep Town Shop category tabs clean by mapping `Gloves`, `Boots`, `Helm`, `Shield`, `Armor` under a unified `"Armor"` filter group (or `"Armor & Gear"`) while `Accessory` / `Cloak` show under `"Magic"` or `"Gear"` so Tanner Mira's shop (`Bywater Tannery`) continues displaying Leather Cap, Leather Gloves, Leather Boots, Steel Shod Boots, and Padded Cloak seamlessly.

### 2.4 Visual Paper-Doll + Compact Stacked Shared Stash Layout
Redesign the `1360x840` Party Drawer with **two clean tabs on the Left Column** (`[Equipment]` | `[Character Sheet]`, default `[Equipment]` when opening Inventory/Party, with instant toggle or side-by-side summary strip) and the **Shared Stash Grid on the Right Column**:

```text
+---------------------------------------------------------------------------------------------------+
| Vanguard Party & Gear        [Bandage 2/2] [Full Rest: Safe]    Gold: 400g | Torch: 30t   [Close] |
+---------------------------------------------------------------------------------------------------+
| [Aldric Lv1] [Liora Lv1 *] [Kaelen Lv1] [Theron Lv1] [Seraphina Lv1] [Vex Lv1]   (* = LEVEL UP!)  |
+-----------------------------------------------+---------------------------------------------------+
| LEFT COLUMN (560px): [Gear] [Sheet] [Lvl Up!] | RIGHT COLUMN (740px): SHARED STASH                |
|                                               | [All] [Gear] [Potions] [Supplies]  Sort:[Upgrade] |
|   [Portrait 72px] Aldric - Lv1 Human Fighter  | [Equip Best: Hero] [Equip Best: Party]  Wt:520/550|
|   HP 17/17 | AC 29 | Hit +18 | Dmg 1d8+2      | +-----------------------------------------------+ |
|                                               | | [icon] Leather Boots x1   [UPGRADE]   [Equip] | |
|           +------+   +------+                 | |   Feet | +1 AC, Wt 40  (+1 AC vs Empty)       | |
|           | Head |   |Cloak/|                 | | [icon] Leather Gloves x1  [UPGRADE]   [Equip] | |
|           | 56px |   |Amulet|                 | |   Hands | +2 AC, Wt 12 (+2 AC vs Empty)       | |
|           +------+   +------+                 | | [icon] Heal Small Wounds x4 (3/3)     [Use..] | |
|   +------+   .-----------.   +------+         | |   Potion | Heal 1d8+2                 [Use..] | |
|   | Main |   |   Hero    |   | Off- |         | | [icon] Longsword x2                   [Equip] | |
|   | Hand |   | Silhouette|   | Hand |         | |   Main Hand | Dmg 1d10 (+1 avg vs Broadsword) | |
|   +------+   |  + Body   |   +------+         | | [icon] Torch x2 (6/6)                 [Light] | |
|   +------+   |   Slot    |   +------+         | |   Supply | +30t Light / Camp Fuel             | |
|   |Ranged|   '-----------'   |Charm/|         | +-----------------------------------------------+ |
|   +------+                   | Ring |         |  (Hover ANY item -> Side-by-side Equipped vs      |
|           +------+   +------+                 |   Candidate diff tooltip with green/red deltas!)  |
|           |Hands |   | Feet |                 |                                                   |
|           +------+   +------+                 |                                                   |
|                                               |                                                   |
|  Tip: Click slot to filter Stash. Right-click |                                                   |
|  or Shift-click filled slot to Unequip.       |                                                   |
+-----------------------------------------------+---------------------------------------------------+
```

#### Paper-Doll Slot Rules (Left Column):
- **9 Anatomical Slots (`64x64` bronze-framed squares):**
  - Top row: `Head` (`Helm`), `Cloak / Amulet` (`Accessory2`).
  - Middle row: `Main Hand` (`Weapon`) on left, `Body` (`Armor`) centered over a subtle dark-bronze hero silhouette backing, `Off-Hand` (`Shield`, shows dimmed lock badge `"2H"` when a two-handed weapon is in `Main Hand`) on right.
  - Lower row: `Ranged` (`MissileWeapon`) on left, `Hands` (`Hands`) & `Feet` (`Feet`) centered below torso, `Charm / Ring` (`Accessory1`) on right.
- **Zero Redundant `Unequip` Text Buttons:** Remove all 5 `Unequip` buttons completely!
  - **Empty slot:** Renders a subtle muted silhouette icon + tiny 10px slot label (`Head`, `Hands`, `Feet`, etc.). Clicking an empty slot filters the Shared Stash to items matching that slot!
  - **Filled slot:** Renders crisp `36x36` item sprite (from `_catalog.GetItemIcon`) + rarity border (Common Slate, Uncommon Green `+1`, Rare Cyan `+2/+3`, Epic Gold `+6`/1000g+) + tiny bonus pill (`+9`, `1d8`, `+2`).
    - **Left-click filled slot:** Filters stash to matching slot (for quick comparison/swap).
    - **Right-click (or double-click) filled slot:** Immediately unequips item to Shared Stash with UI click audio & toast.
    - **Hover filled slot:** Shows rich floating item tooltip (Name, Slot, Stats, Weight, Value, Flavor Description, and `Right-click to Unequip`).

#### Shared Stash Rules (Right Column):
- **Auto-Stacking Fix (`PartyState.AddItemToStash`):** Stack items whenever `ItemId`, `IsIdentified`, AND `RemainingCharges` match! `Heal Small Wounds x4` displays as a single stacked row/tile with `x4` badge and `(3/3 chg)` tag instead of 4 duplicate rows.
- **Filter Tabs:** `All` | `Gear` (equippable slots) | `Potions` (consumables) | `Supplies` (torches, arrows, rope, salt/pepper) + active Paper-Doll slot filter chip (e.g. `[Filter: Feet x]` when user clicks the `Feet` slot).
- **Sort Dropdown / Cycle Pill:** `Upgrade` (strict upgrades for selected hero first) | `Slot` (grouped anatomically) | `Value` (highest gold value first) | `Name`.
- **Comparative Diff Tooltip (BG3 Style):** Hovering any equippable stash item shows the candidate tooltip AND an inline **"vs Equipped ([Current Item])"** diff block with color-coded deltas:
  - Green: `+2 AC`, `+1.0 Avg Dmg`, `+5% Hit`, `-20 Wt`
  - Red: `-3 AC`, `+40 Wt`, `Unequips Shield (2H)`
  - Neutral: `Empty Slot -> Direct Upgrade`
- **Green `[UPGRADE]` Badge:** Reuses & upgrades `BywaterHudController.ScoreItemForSlot` for the **currently selected hero** (accounting for all 9 slots, weapon avg dice + `WeaponBonus * 2.5`, `ArmorClassBonus`, and 2H shield trade-off so a 2H weapon does not blindly flag as an upgrade if it drops a tank's Shield AC unless net score improves).
- **1-Click Actions:**
  - `[Equip]` button on equippable rows (or double-click row) equips directly to the selected hero.
  - **`[Equip Best: Hero]`** and **`[Equip Best: Party]`** buttons in the Stash toolbar: runs a deterministic greedy upgrade pass across all 9 slots (filling empty slots first, then strictly higher-scoring gear without stripping an equipped 1H+Shield tank unless strictly better, and respecting caster vs melee weapon archetypes: Mage/Cleric prefer Staff/Dagger/Mace, Ranger prefers Bow+ShortSword, Knight/Paladin prefer Heavy Armor+Shield+Sword/Axe). Displays a crisp toast summarizing count of items equipped.
  - **Direct Potion / Consumable Target Picker (`[Use]`):** Clicking `[Use]` on a Healing Potion, Heal Poison, Heat Resistance, or Refresh Potion opens a compact inline popover (`Who drinks?`) showing all 6 heroes with their current `HP/MaxHP` bars & conditions (pre-highlighting the most injured / poisoned / exhausted hero at the top!) so players never have to switch hero tabs just to heal an ally. Clicking `[Light]` on `Torch` lights it immediately (`+30 turns`).

---

## 3. Item #9: Character Sheet, Attributes & Level-Up Progression System

### 3.1 Forensics: Current Implementation & Gaps
Audited across `PartyAndInventory.cs` (L44-191), `CombatModels.cs` (L551-613), `BywaterSaveService.cs` (L34-50, L249-320), and `party_m1.json`:

1. **XP Curve (`PartyMemberState.GetXpRequiredForLevel`, L144-152):**
   - Formula: `level <= 1 ? 0 : (level - 1) * level * 100` -> Lv 1: `0`, Lv 2: `200`, Lv 3: `600`, Lv 4: `1,200`, Lv 5: `2,000`, Lv 6: `3,000`.
   - Gap: **Zero XP progress bar anywhere in the UI** (`RefreshPartyDrawer` and `RefreshExploreHud` never even display `hero.Xp` or `hero.XpForNextLevel`!).
2. **Level-Up Flow (`PartyMemberState.AddXp`, L154-191):**
   - Currently **silently auto-applies** level-ups during combat victory (`HpGain`, `SpGain`, `BaseToHit += 2`, `BaseMissile += 2`).
   - Gap: Zero player interaction, zero attribute point spending, zero new spell unlocks, zero portrait level-up indicator.
   - Persistence Gap: `SavedMemberState` does NOT save the 6 attributes (`Brawn..Luck`), `PendingLevelUps`, or `KnownSpellIds` (and `Brawn..Luck` on `PartyMemberState` are read-only `{ get; }` properties!).
3. **Attribute Audit - What the 6 Stats Actually Affect Today:**

| Attribute | Current Effect in `RealmzCore` | Currently Ignored By | Recommended Realmz Remaster Wiring |
| :--- | :--- | :--- | :--- |
| **Brawn** | Melee `DamageBonus = Max(0, (Brawn - 12) / 3)` (`CombatModels.cs:572`); Shove contested roll. | Carry weight / Encumbrance. | Keep Melee Damage (`+1` per 3 pts > 12) + Shove + **Carry Capacity** (`120 + Brawn * 25` Wt; exceeding capacity applies `-2 Move` Encumbered warning). |
| **Knowledge** | Caster SP gain on level-up via `Max(Knowledge, Judgment)`. | Spell damage, Spell DC, Arcane potency. | Sorcerer/Enchanter SP gain + **Arcane Spell Power** (`+1` dmg per 4 pts > 10) + **Spell Hit/DC Bonus** (`+(Knowledge - 10)`%). |
| **Judgment** | Caster SP gain on level-up via `Max(Knowledge, Judgment)`. | Healing potency, Magic Resistance, Will saves. | Priest/Crusader SP gain + **Healing Bonus** (`+1` HP per 4 pts > 10) + **Magic Resistance** (`+Max(0, (Judgment - 10) * 2)`%). |
| **Agility** | Initiative turn order (`Dexterity = Agility`). | AC/Dodge, Ranged To-Hit. | Initiative order + **Dodge AC Bonus** (`+Max(0, (Agility - 12) / 3)` AC) + **Ranged To-Hit Bonus** (`+Max(0, (Agility - 12) * 2)`%). |
| **Vitality** | Level-up HP gain (`Max(2, 4 + Max(0, (Vitality - 10) / 2))`). | Condition/Poison saves, Recovery. | Level-up HP gain + **Status Save Bonus** (`+Max(0, (Vitality - 10) * 2)`% recovery/resist vs Poison, Disease, Slow, Helpless). |
| **Luck** | **100% UNUSED DEAD STAT** (zero code references!). | Everything! | **Critical Hit Chance** (`5% + Max(0, (Luck - 10) / 2)`% crit, e.g. Luck 14 = 7%, Luck 20 = 10%) + **All-Saves Bonus** (`+Max(0, Luck - 10)`%) + **Gold Find Bonus** (`+1%` per point of party avg Luck above 10). |

4. **Data Anomalies Discovered in `party_m1.json` (Recommended Cleanup):**
   - `hero_cleric` (**Liora**): `"attacks_per_round": 49.0` (classic binary export artifact; clamped to 5 in `CombatModels.cs:571` -> should be `1.0`).
   - `hero_ranger` (**Kaelen**): `brawn: 20, knowledge: 20, judgment: 20, agility: 6, vitality: 6, luck: 6` (inverted physical/mental attributes for a Half-Elf Archer -> recommend `brawn: 14, knowledge: 11, judgment: 12, agility: 18, vitality: 13, luck: 14`).
   - `hero_rogue` (**Vex**): all 6 attributes are `20` (`20/20/20/20/20/20` -> recommend balanced Furfoot Rogue spread `brawn: 12, knowledge: 12, judgment: 11, agility: 19, vitality: 13, luck: 17`).

### 3.2 Two-Column Character Sheet Tab (Inside Party Drawer)
Switching the Left Column tab to **`[Character Sheet]`** (or clicking a hero's `[Inspect]` button) presents a structured, tooltip-rich RPG sheet:

- **Top Identity Banner:** `96x96` Bronze Portrait, Name, `Level N [Race] [Archetype] ([Caste])`, **XP Progress Bar** (`XP: 140 / 200 (70%) - 60 XP to Lv 2`) in brushed gold, plus a glowing **`[LEVEL UP!]`** button right beside the XP bar whenever `hero.HasPendingLevelUp`.
- **Sub-Column A - Core Attributes (6 Interactive Rows):**
  - Each row shows `[Stat Name]` | **`[Value]`** | `[Live Modifier Summary]`, e.g.:
    - `Brawn      18   (+2 Melee Dmg, 570 Max Wt)`
    - `Knowledge   8   (+0 Spell Pwr, +0% DC)`
    - `Judgment    9   (+0 Heal, +0% Magic Res)`
    - `Agility    14   (Init 14, +0 Dodge AC, +4% Ranged)`
    - `Vitality   16   (+7 HP/Lv, +12% Status Resist)`
    - `Luck       11   (5% Crit, +1% All Saves)`
  - Hovering any attribute row shows the exact formula breakdown tooltip.
- **Sub-Column B - Combat Derived Stats & Breakdown:**
  - `HP: 17 / 17`, `SP: 0 / 0`
  - `Armor Class (AC): 29` (hover breakdown: `Base 11 + Leather Armor 9 + Shield 6 + Helm 3 + Agility 0`)
  - `Melee To-Hit: +18` (`Base 18 + Weapon 0`) | `Damage: 1d8 + 2` (`Broadsword 1d8 + Brawn 2`)
  - `Ranged To-Hit: +11` | `Range: -`
  - `Movement: 14` | `Crit Chance: 5%` | `Magic Resist: 0%`
  - `Carried Weight: 520 / 570 Wt` (green normal / amber heavy / red encumbered `-2 Move` bar)
- **Bottom Section - Conditions & Known Spells:**
  - Condition pills with existing `icon_badge_*.png` sprites (or `"Healthy"` green pill).
  - Scrollable compact Known Spells chips showing real spell icons (`icon_spell_*.png`), `SP Cost`, `Range`, and hover tooltip with `EffectDice` & `Description`.

### 3.3 HUD Portrait `LEVEL UP!` Indicator & Interactive Level-Up Modal
1. **Explore HUD Party Strip (`HeroCardRefs` in `BywaterHudController.cs:3282-3353`):**
   - Add a `3px` slim golden XP progress bar along the bottom edge of each hero card.
   - Add a pulsing golden **`LVL UP!`** pill anchored to the top-right corner of the hero's portrait frame whenever `hero.PendingLevelUps > 0`. Clicking the card (or badge) opens the Party Drawer directly into the Level-Up Modal for that hero.
2. **Deferred Level-Up Architecture (`PartyMemberState`):**
   - Add mutable setters for `Brawn`, `Knowledge`, `Judgment`, `Agility`, `Vitality`, `Luck`.
   - Add `PendingLevelUps` property (`int`, persisted in Save v3) and `HasPendingLevelUp => PendingLevelUps > 0`.
   - Update `AwardXp(int amount)` (used by live gameplay `GrantVictoryRewardsToParty`) to add `Xp` and increment `PendingLevelUps` for every threshold crossed (`Xp >= GetXpRequiredForLevel(Level + PendingLevelUps + 1)`), WITHOUT silently auto-spending the player's choices!
   - Keep `AddXp(int amount)` (or add `ApplyPendingLevelUp(AttributeAllocation? alloc = null, int? learnedSpellId = null)`) cleanly structured: if `AddXp` is called with `autoApply: true` (default for legacy unit tests) it applies deterministic gains immediately; live combat calls `AwardXp` so the player enjoys the interactive Level-Up Modal! *(See Section 5 for exact test compatibility plan.)*
3. **Interactive Level-Up Modal (`760x640` centered bronze-framed overlay):**
   - **Header:** Hero `96x96` portrait, `"LEVEL UP! Aldric advances to Level 2"`, `Fighter (Human)` subtitle, and pending counter (`1 of 2 ready heroes` with quick Prev/Next arrows if multiple heroes leveled!).
   - **Section 1 - Guaranteed Class & Vital Gains (Preview Table):**
     - Uses **deterministic high-average HP/SP gains** (eliminating frustrating 1-HP RNG rolls while honoring `Vitality` / `Knowledge` / `Judgment`):
       - `Max HP`: `17 -> 24 (+7)` (formula: `CasteBaseHp + Max(0, (Vitality - 10) / 2)`, where Fighter/Paladin = `5`, Ranger/Cleric = `4`, Rogue = `4`, Mage = `3`). Dynamically recalculates live if player puts points into `Vitality`!
       - `Max SP`: `21 -> 28 (+7)` (for casters; updates live if player invests in `Knowledge` or `Judgment`!).
       - `Melee To-Hit`: `+18 -> +20 (+2)` | `Ranged To-Hit`: `+15 -> +17 (+2)` (if Archer/Ranger/Rogue).
   - **Section 2 - Allocate `+2` Attribute Points:**
     - 6 rows (`Brawn`, `Knowledge`, `Judgment`, `Agility`, `Vitality`, `Luck`), each with current value, `[-]` button, `+added` green delta, `[+]` button, and live derived impact preview (e.g. `Crit 5% -> 6%`, `Dmg +2 -> +3`).
     - Includes a `[Recommended]` 1-click button that pre-fills class-appropriate stats (e.g. Knight: `+1 Brawn, +1 Vitality`; Mage: `+1 Knowledge, +1 Agility`; Cleric: `+1 Judgment, +1 Vitality`) so casual players can level up in 2 clicks while tacticians can min-max!
   - **Section 3 - Newly Unlocked Spells (Caste Progression Table):**
     - Automatically unlocks or offers a choice of 1 new spell from `spells_items_m1.json` not yet in `hero.KnownSpellIds`:
       - **Sorcerer / Mage (`caste_id 6`):** Lv 2 unlocks `1306 Fireball` or `3202 Daze` (whichever not known; if all known, grants `+4 Bonus SP` Arcane Mastery).
       - **Priest / Cleric (`caste_id 7`):** Lv 2 unlocks `2101 Brimstones` (missing from Liora's starting list!), Lv 3 unlocks `2207 Heal Medium Wounds` / `2310 Sandman` (if not known, else `+4 Bonus SP` Divine Grace).
       - **Crusader / Paladin (`caste_id 3`):** Lv 2 unlocks `2101 Brimstones` or `1102 Enchanted Blade`, Lv 3 unlocks `2207 Heal Medium Wounds` (Seraphina starts with only `2105` & `2106`!).
       - **Martial Classes (Fighter, Archer, Rogue):** Shows passive milestone unlock (e.g., Lv 2: `Combat Conditioning: +1 Move` or `+3% Crit`, Lv 3: `Weapon Specialization: +2 Dmg`).
   - **Footer:** `[Cancel / Later]` (preserves `PendingLevelUps` so player can level up anytime) and `[Confirm Level Up]` (enabled once all 2 points are spent; commits stats, heals the gained HP/SP delta, saves cleanly in v3 schema, plays level-up chime, and advances to next ready hero if any).

---

## 4. Item #10: Rich Tactical Victory & Loot Summary Screen

### 4.1 Forensics: Current Victory Screen Gaps
Inspected directly from Kartik's screenshot (`media_1795558694984_14856af3.jpg`), `TacticalCombatSession.cs` (L1923-1986, L2699-2756), `BywaterGameController.cs` (L4124-4174), and `BywaterHudController.cs` (L2806-2886, L5205-5268):

1. **Text-Only Wall in a `600x430` Box:** No hero portraits, no XP bars, no item icons, no stat previews.
2. **Unstacked & Truncated Loot:** `BywaterGameController.cs:4146-4158` converts raw `CollectedLootItemIds` into duplicate strings (`Longsword`, `Helm`, `Shield`, `Speckled Luck Stone +2`, `Longsword`, `Helm`, ...), and `BywaterHudController.cs:2856-2861` hard-truncates after 6 lines with `"- ...and 2 more"`!
3. **Zero Per-Hero XP Visibility:** Displays only `"XP Earned: +100"` without showing how it split (`+17 XP` per living hero), how close anyone is to leveling up, or who was MVP / fell and recovered.
4. **Fallen Allies Get `0 XP` Silently:** `GrantVictoryRewardsToParty` (`TacticalCombatSession.cs:1951-1961`) excludes fallen heroes from XP completely without informing the player (recommend giving fallen allies **50% XP share** or full share with `"Revived @ 1 HP"` tag so a hero who tanked a boss hit isn't permanently behind the party!).
5. **Frequent `Gold Looted: +0g`:** 6 of 12 monsters in `monsters_bywater_m1.json` (including `Skeletal Warrior`, `Giant Frog`, `Giant Spider`, `Zombie`) have `gold_min: 0, gold_max: 0`. Giving humanoid/armed undead modest coin drops (e.g. `Skeletal Warrior`: `4-12g`, `Zombie`: `2-6g`) plus styling `0g` cleanly avoids jarring `Gold Looted: +0g` readouts.

### 4.2 New `CombatVictorySummary` Payload in `RealmzCore`
Extend `TacticalCombatSession` to track per-hero combat telemetry (`Kills`, `DamageDealt`, `HealingDone`) during battle and expose a rich `CombatVictorySummary` object upon `Outcome == CombatOutcome.Victory`:

```csharp
public sealed class HeroVictorySummary
{
    public string HeroId { get; }
    public string Name { get; }
    public string Archetype { get; }
    public int PortraitId { get; }
    public int Level { get; }
    public int XpBefore { get; }
    public int XpGained { get; }
    public int XpAfter { get; }
    public int CurrentLevelXpFloor { get; }
    public int NextLevelXpThreshold { get; }
    public bool ReadyToLevelUp { get; }
    public int PendingLevelUps { get; }
    public bool WasFallenRevived { get; }
    public int Kills { get; }
    public int DamageDealt { get; }
    public int HealingDone { get; }
    public bool IsMvp { get; }
}

public sealed class LootStackSummary
{
    public int ItemId { get; }
    public int Quantity { get; }
    public bool IsUpgradeForParty { get; }
    public string BestHeroId { get; }
    public string BestHeroName { get; }
    public string UpgradeDeltaLabel { get; } // e.g. "+2 AC (Aldric)"
}

public sealed class CombatVictorySummary
{
    public string EncounterTitle { get; }
    public int RoundsSurvived { get; }
    public int EnemiesDefeated { get; }
    public int TotalXpEarned { get; }
    public int TotalGoldLoot { get; }
    public IReadOnlyList<HeroVictorySummary> Heroes { get; }
    public IReadOnlyList<LootStackSummary> StackedLoot { get; }
    public bool AnyHeroReadyToLevelUp { get; }
    public bool AnyLootUpgradeAvailable { get; }
}
```

### 4.3 Visual Victory Screen Layout (`980x690` Centered Modal)

```text
+===================================================================================================+
|                                     *  V I C T O R Y !  *                                         |
|         Bywater Skirmish - 4 Enemies Defeated in 3 Rounds     [Coin] +24g   [Star] +100 XP        |
+===================================================================================================+
| PARTY PROGRESS                                                                                    |
| +-----------------------------+ +-----------------------------+ +-----------------------------+   |
| | [64px] Aldric (Lv 1)  [MVP] | | [64px] Liora (Lv 1) [LVL UP]| | [64px] Kaelen (Lv 1)        |   |
| |  +17 XP  | 2 Kills | 28 Dmg | |  +17 XP  | 18 Heal          | |  +17 XP  | 1 Kill  | 19 Dmg |   |
| | [======++-------] 117/200   | | [===============] 205/200!  | | [=====++--------] 97/200    |   |
| +-----------------------------+ +-----------------------------+ +-----------------------------+   |
| +-----------------------------+ +-----------------------------+ +-----------------------------+   |
| | [64px] Theron (Lv 1)        | | [64px] Seraphina (Lv 1)     | | [64px] Vex (Lv 1) [Revived] |   |
| |  +17 XP  | 1 Kill  | 24 Dmg | |  +17 XP  | 14 Dmg           | |  +17 XP  | Revived @ 1 HP   |   |
| | [=====++--------] 97/200    | | [=====++--------] 97/200    | | [====++---------] 82/200    |   |
| +-----------------------------+ +-----------------------------+ +-----------------------------+   |
|                                                                                                   |
| LOOT RECOVERED (4 Unique Items, 8 Total - Hover to Compare)                                       |
| +-----------------------------------------------------------------------------------------------+ |
| | +--------+  +--------+  +--------+  +--------+                                                | |
| | | [UPG]  |  |        |  |        |  | [UPG]  |   (Scrollable GridLayoutGroup of 72x72 tiles;  | |
| | | [32px] |  | [32px] |  | [32px] |  | [32px] |    ZERO "...and 2 more" text truncation!       | |
| | |  x2    |  |  x2    |  |  x2    |  |  x2    |    Rarity border + stacked qty + green UPG     | |
| | +--------+  +--------+  +--------+  +--------+    chevron + hover Equipped-vs-Candidate diff) | |
| | Longsword    Helm        Shield     Luck Stn+2                                                | |
| +-----------------------------------------------------------------------------------------------+ |
|                                                                                                   |
|  [  Level Up Now (1)  ]      [  Take & Equip Upgrades (2)  ]      [  Take All & Continue  ]       |
+===================================================================================================+
```

#### Key UX Details:
- **6-Hero Party Grid (2 rows x 3 columns):**
  - Displays all 6 bronze-framed hero portraits (`64x64`, sourced from `128x128` `portrait_*.png`).
  - **Dual-Segment XP Bar:** Base bronze fill (`XpBefore`), animated `0.6s` lerp bright-gold gain segment (`+XpGained`), numeric text (`117 / 200 XP`), and glowing **`LEVEL UP READY!`** golden badge when `ReadyToLevelUp == true`.
  - **Combat Contribution Micro-Tags:** Highest damage+kills hero gets a small brass **`MVP`** tag; fallen heroes revived at 1 HP show a subtle cyan **`Revived @ 1 HP`** tag instead of a generic footer disclaimer.
- **Visual Stacked Loot Tray (`GridLayoutGroup`, 8 columns x scrollable rows):**
  - Uses real `32x32` sprites via `_catalog.GetItemIcon(itemId, name)` centered in `72x72` cards with `12px` short name below, stack badge (`x2`, `x4`) in bottom-right, and rarity-colored border (Common `#5A6578`, Uncommon/Magic `#47D973`, Rare `#59D1F5`, Epic `#F5CC63`).
  - Green **`[UPG]`** badge in top-left corner whenever `IsUpgradeForParty == true`.
  - Hovering any loot tile displays the floating tooltip: Item Name, Slot, Stats, Value, Description, and `"Best Upgrade: +2 AC for Aldric (Empty Charm slot)"`.
  - **Zero Truncation:** Stacked tiles fit up to 16 unique item types visibly without scrolling, and scroll cleanly beyond that.
- **Smart Footer Actions:**
  - **`[Take All & Continue]`** (always shown): Closes modal, returns to exploration (rewards already committed idempotently to `PartyState`).
  - **`[Take & Equip Upgrades (N)]`** (shown when `AnyLootUpgradeAvailable`): Runs party-wide strict upgrade auto-equip on the newly looted items, shows toast (`"Equipped 2 upgrades: Speckled Luck Stone +2 (Aldric), Longsword (Seraphina)"`), and returns to exploration.
  - **`[Level Up Now (N)]`** (glowing gold, shown when `AnyHeroReadyToLevelUp`): Exits combat to exploration and opens the interactive **Level-Up Modal** directly on the first ready hero!
- **Defeat Modal Preserved:** When `outcome == CombatOutcome.Defeat`, the existing compact defeat banner + `[Retry Skirmish]` / `[Revive at Shrine]` buttons remain intact.

---

## 5. Exact Implementation Blueprint & File Plan (Ready for Build Worker `W-246`)

Execute in strict phase order so `RealmzCore` compiles and passes `dotnet test` before touching `RealmzUnity`.

### Phase 1: `RealmzCore` Data, Slots, Economy, Progression & Victory Telemetry
1. **`C:\realmz\remaster\RealmzUnity\Assets\StreamingAssets\bywater_m1\spells_items_m1.json` & `items_by_id_icon_map.json`:**
   - Update item slots/categories for the 12 audited items:
     - `452` Leather Gloves -> `category: "Gloves", slot: "Hands"`
     - `454` Leather Boots, `455` Steel Shod Boots -> `category: "Boots", slot: "Feet"`
     - `250` Padded Cloak -> `category: "Cloak", slot: "Accessory2"`
     - `622` Speckled Luck Stone +2 -> `category: "Accessory", slot: "Accessory1"`
     - `675` Quiver of Protection +2 -> `category: "Accessory", slot: "Accessory2"`
     - `603` Heat Resistance -> `category: "Potion", slot: "Consumable"`
     - `747` Salt, `748` Pepper -> `category: "Supply", slot: "Utility"`
     - `102` Quarter Staff -> `name: "Sparring Quarter Staff"`
     - `2` Dagger of Shielding +2 -> `armor_class_bonus: 2`
   - Mirror the updated `category`/`slot`/`name` fields into `items_by_id_icon_map.json`.
2. **`C:\realmz\remaster\RealmzUnity\Assets\StreamingAssets\bywater_m1\party_m1.json` & `monsters_bywater_m1.json`:**
   - Fix `party_m1.json` anomalies: Liora `attacks_per_round: 1.0` (was `49.0`); normalize Kaelen & Vex attribute spreads.
   - Optional polish in `monsters_bywater_m1.json`: add modest `gold_min: 3, gold_max: 10` on `skeletal_warrior` so humanoid skeleton packs do not show `+0g`.
3. **`C:\realmz\remaster\RealmzCore\Exploration\PartyAndInventory.cs`:**
   - Expand `EquipmentSlot` enum to 9 slots (`0..8`: adding `Hands = 5`, `Feet = 6`, `Accessory1 = 7`, `Accessory2 = 8`).
   - Update `PartyMemberState.TryParseSlot()` to parse `Hands/Gloves`, `Feet/Boots`, `Accessory1/Ring/Charm`, `Accessory2/Cloak/Amulet/Quiver`, and add `EquipFromStash(heroIndex, stashIndex, catalog, EquipmentSlot? preferredSlot = null)` with smart fallback between `Accessory1` and `Accessory2`.
   - Fix `PartyState.AddItemToStash()` stacking condition: stack whenever `existing.ItemId == itemId && existing.IsIdentified == isIdentified && existing.RemainingCharges == charges` (fixes `Heal Small Wounds` and `Torch` unstacking!).
   - Make `Brawn`, `Knowledge`, `Judgment`, `Agility`, `Vitality`, `Luck` settable (`{ get; set; }`) and add `PendingLevelUps`, `HasPendingLevelUp`, `AwardXp(int amount)` (deferred level-up), `ApplyPendingLevelUp(AttributeAllocation alloc, int? learnedSpellId = null)`, `GetLevelUpPreview(AttributeAllocation? alloc = null)`, and `GetCasteUnlockableSpells(IReadOnlyDictionary<int, SpellDefinition> spells)`.
   - Wire all 6 attributes into derived stats (`GetEffectiveAc`, `GetEffectiveMeleeToHit`, `GetEffectiveRangedToHit`, `GetEffectiveCritChancePct`, `GetEffectiveSpellPowerBonus`, `GetEffectiveHealBonus`, `GetEffectiveMagicResistancePct`, `GetEffectiveStatusSaveBonusPct`, `GetCarryCapacity`, `GetEquippedWeight`, `IsEncumbered`, `GetEffectiveMovementMax`).
   - Add `PartyState.QuickBandageCharges` (`MaxQuickBandageCharges = 2`), `GetTotalTorchCharges(catalog)`, and `Rest(CampMode mode, bool isSafeZone, IReadOnlyDictionary<int, ItemDefinition>? catalog, out bool triggeredAmbush, ...)` implementing Section 1.2 rules; keep legacy `Rest(CampMode mode)` overload or update `ExplorationAndEconomyTests.cs` cleanly.
   - Add deterministic `EquipBestForHero(heroIndex, catalog)` and `EquipBestForParty(catalog)` + `ScoreItemForHero(hero, item, slot, catalog)` helpers directly in `RealmzCore` (reusable by Paper-Doll, Stash, Shop, and Victory screen!).
4. **`C:\realmz\remaster\RealmzCore\Combat\CombatModels.cs`, `CombatRules.cs`, & `TacticalCombatSession.cs`:**
   - Pass hero `CritChancePct`, `SpellPowerBonus`, `HealBonus`, `MagicResistance`, and `StatusSaveBonus` from `PartyMemberState` into `CombatantState.FromHero` and apply them in `CombatRules.CalculateHitPreview` / `ResolveMeleeAttack` and `TacticalCombatSession.CastSpell`.
   - Track per-combatant `Kills`, `DamageDealt`, `HealingDone` during combat.
   - Add `CombatVictorySummary`, `HeroVictorySummary`, `LootStackSummary` DTOs and build `TacticalCombatSession.VictorySummary` inside `CheckOutcome()` / `GrantVictoryRewardsToParty()`.
   - Award fallen allies `50%` (or `100%` with `WasFallenRevived = true`) XP share and call `member.AwardXp(share)` (or `AddXp` when `DeferLevelUp == false` so existing `WaveAPassBAndP0Tests.cs` stays 100% green!).
5. **`C:\realmz\remaster\RealmzCore\Exploration\ExplorationSession.cs`:**
   - Add `TryCampRest(CampMode mode, IReadOnlyDictionary<int, ItemDefinition> catalog)` that checks safe town zone vs uncleared encounter zone ambush roll (`15%`), advances `TurnCount` (`+1` bandage / `+8` full rest), and delegates to `Party.Rest(...)`.
   - Refill `Party.QuickBandageCharges = PartyState.MaxQuickBandageCharges` on `TriggerTempleHeal`.
6. **`C:\realmz\remaster\RealmzCore.Tests\`:**
   - Add unit tests covering: 9-slot equip/unequip (Boots -> Feet, Gloves -> Hands, Cloak -> Accessory2, Luck Stone -> Accessory1), charged item stacking, `EquipBestForHero`/`Party`, Quick Bandage charge limits & Full Rest torch/safe-zone/ambush rules, all 6 attribute formulas (including Luck crit & Brawn encumbrance), deferred `AwardXp` + `ApplyPendingLevelUp` + spell unlock, and `CombatVictorySummary` stacked loot & MVP calculation.
   - Verify `dotnet test C:\realmz\remaster\RealmzCore.Tests\RealmzCore.Tests.csproj` passes 100%.

### Phase 2: `RealmzUnity` Save v3, Paper-Doll, Character Sheet, Level-Up Modal & Victory Screen
7. **`C:\realmz\remaster\RealmzUnity\Assets\Scripts\Data\BywaterSaveService.cs`:**
   - Bump `CurrentSaveVersion = 3`.
   - Persist `brawn`, `knowledge`, `judgment`, `agility`, `vitality`, `luck`, `pendingLevelUps`, `knownSpellIds` on `SavedMemberState`, and `quickBandageCharges` on `BywaterSaveData`.
   - Maintain full v1/v2 backward-compatible load (auto-migrating old `Helm`-slotted Boots/Gloves via `itemDef.Slot` lookup).
8. **`C:\realmz\remaster\RealmzUnity\Assets\Scripts\Game\BywaterHudController.cs` & `BywaterUiHelpers.cs`:**
   - **Header & Camp Compact Pills:** Move `QuickBandageBtn` and `FullRestBtn` into the Party Drawer header as slim icon+badge pills with live hover tooltips (`icon_heal.png` & `icon_torch.png`).
   - **Left Column Tabbed Paper-Doll & Character Sheet:**
     - `[Equipment]` tab: 9-slot anatomical Paper-Doll grid with empty silhouettes, rarity borders, click-to-filter, right-click-to-unequip, hover tooltips, and zero `Unequip` text buttons.
     - `[Character Sheet]` tab: XP progress bar + `[LEVEL UP!]` CTA, 6 attribute rows with live modifier readouts & formula tooltips, derived combat stats & weight bar, condition badges, and spell icon chips.
   - **Right Column Shared Stash Overhaul:**
     - Category tabs (`All | Gear | Potions | Supplies`), active slot filter chip, Sort toggle (`Upgrade | Slot | Value`), `[Equip Best: Hero]`, `[Equip Best: Party]`.
     - Pooled stacked rows/cards showing `32x32` sprite, `xN` badge, `(chg)` tag, green `[UPGRADE]` pill, hover **Equipped vs Candidate** delta tooltip, 1-click `[Equip]`, and `[Use]` -> inline **`Who drinks?` 6-hero target popover**.
   - **Explore HUD Hero Cards:** Add slim 3px gold XP bar + pulsing golden **`LVL UP!`** portrait badge when `PendingLevelUps > 0`.
   - **Interactive Level-Up Modal:** New `BuildLevelUpModal` + `OpenLevelUpModal(PartyState, BywaterM1Bundle, int heroIdx)` with `+2` attribute steppers, live HP/SP/stat delta previews, `[Recommended]` quick-fill, spell unlock selector, and `[Confirm Level Up]`.
   - **Rich Victory Modal:** Upgrade `BuildOutcomeModal` (`980x690` for Victory, `600x430` for Defeat) and add `ShowVictoryOutcome(CombatVictorySummary summary, PartyState party, BywaterM1Bundle bundle)` with 6-hero portrait + animated XP progress grid, `MVP` / `LEVEL UP READY!` / `Revived @ 1 HP` tags, scrollable stacked `32x32` icon loot tray (zero `...and N more` truncation), hover upgrade diffs, `[Equip Upgrades & Continue]`, and `[Level Up Now (N)]`. Keep legacy `ShowOutcomeModal(...)` overload delegating cleanly so existing editor tests remain green.
9. **`C:\realmz\remaster\RealmzUnity\Assets\Scripts\Game\BywaterGameController.cs`:**
   - Wire new HUD events: `OnEquipBestHeroRequested`, `OnEquipBestPartyRequested`, `OnUseStashItemOnHeroRequested(int targetHeroIdx, int stashIdx)`, `OnOpenLevelUpRequested(int heroIdx)`, `OnConfirmLevelUpRequested(int heroIdx, AttributeAllocation alloc, int? spellId)`, `OnVictoryEquipUpgradesRequested`, `OnVictoryLevelUpRequested`.
   - Update `HandleUseStashItem` to support `603 Heat Resistance` (applies fire/heat resist buff) and `723 Refresh Potion` (clears `ExhaustionStacks` + restores SP/HP).
   - Update `HandleCampRest` to call `ExploreSession.TryCampRest(...)` and launch ambush combat cleanly if a dungeon rest triggers an ambush.
   - Update `HandleCombatOutcome` and `QuickResolve` to pass `CombatVictorySummary` to `hudController.ShowVictoryOutcome(...)`.
10. **Unity EditMode & PlayMode Tests (`RealmzM1EditorTests.cs` & `RealmzM1PlayModeTests.cs`):**
    - Add EditMode tests for: 9-slot Paper-Doll binding (Boots in `Feet`, Gloves in `Hands`, Luck Stone in `Accessory1`, Cloak in `Accessory2`), zero `Unequip` buttons on empty slots, stacked stash rows (`Heal Small Wounds x4`), Save v3 roundtrip of attributes/spells/pending levels + v2 save migration of `Leather Boots`, Level-Up modal confirmation updating stats & clearing badge, and Victory modal displaying all 6 hero portraits + stacked loot icons without `...and N more`.
    - Run Unity EditMode + PlayMode suites and verify zero console errors and zero broken regressions.
