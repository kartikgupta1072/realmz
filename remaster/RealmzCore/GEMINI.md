# RealmzCore - Pure Deterministic Simulation Rules

Scope: `C:\Users\karti\realmz\remaster\RealmzCore\`

## 1. Zero Engine Dependencies (`netstandard2.1`)
- Keep `RealmzCore.csproj` strictly on `netstandard2.1` (`LangVersion 9.0`, `Nullable enable`, `Deterministic true`).
- NEVER add `using UnityEngine`, `using UnityEditor`, or any `<PackageReference` NuGet dependency.
- All Unity-side scripts consume the compiled Release assembly at `remaster/RealmzUnity/Assets/Plugins/RealmzCore/RealmzCore.dll`.

## 2. Locked Gameplay Contracts
- **Split 4 MP + 6 AP Tactical Combat Contract:** Separate **`4 MP` base movement** + **`6 AP` base actions** (`2-4 AP` skills/spells) + **`1 Reaction`** + `Dash` (`2 AP` -> `+4 MP`) (`CombatActionEconomy.ResetSplitMpApTurn` in `Rules/CombatRules.cs`), with D&D `Advantage`/`Disadvantage` (`RollAdvantageState`), Equipment Set bonuses (`Scout's Leather` `+2 MP` / `+1 Max AP`, `Iron Vanguard` `Guard 1 AP` & `+1 Max AP`), and `120`-minute time-based Torches.
- **Deterministic Simulation:** Use `RealmzCore.Rules.Dice` / explicit seeds for encounter rolls, combat math, and arena generation.
- **Zero GC Churn in Hot Paths:** Avoid LINQ allocations and per-step heap churn inside exploration step rolls, `FogOfWarMap` LOS evaluation, and combat turn resolution.

## 3. Mandatory Post-Edit Sync
After any edit in `RealmzCore/`, rebuild Release, run `RealmzCore.Tests`, and sync the plugin DLL in one command:
```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 sync-core
```
