# RealmzCore - Pure Deterministic Simulation Rules

Scope: `C:\Users\karti\realmz\remaster\RealmzCore\`

## 1. Zero Engine Dependencies (`netstandard2.1`)
- Keep `RealmzCore.csproj` strictly on `netstandard2.1` (`LangVersion 9.0`, `Nullable enable`, `Deterministic true`).
- NEVER add `using UnityEngine`, `using UnityEditor`, or any `<PackageReference` NuGet dependency.
- All Unity-side scripts consume the compiled Release assembly at `remaster/RealmzUnity/Assets/Plugins/RealmzCore/RealmzCore.dll`.

## 2. Locked Gameplay Contracts
- **D&D 5e Option A Combat Contract:** Separate `MP` movement + `1 Main Action` + `1 Bonus Action` + `1 Reaction` + `Dash` (`CombatActionEconomy.ResetDndTurn` in `Rules/CombatRules.cs`), with D&D `Advantage`/`Disadvantage` (`RollAdvantageState`) and Equipment Set perks (`Scout's Leather` `+2 MP`, `Iron Vanguard` Bonus Action `Guard/Overwatch`).
- **Deterministic Simulation:** Use `RealmzCore.Rules.Dice` / explicit seeds for encounter rolls, combat math, and arena generation.
- **Zero GC Churn in Hot Paths:** Avoid LINQ allocations and per-step heap churn inside exploration step rolls, `FogOfWarMap` LOS evaluation, and combat turn resolution.

## 3. Mandatory Post-Edit Sync
After any edit in `RealmzCore/`, rebuild Release, run `RealmzCore.Tests`, and sync the plugin DLL in one command:
```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 sync-core
```
