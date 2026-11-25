# RealmzCore - Pure Deterministic Simulation Rules

Scope: `C:\Users\karti\realmz\remaster\RealmzCore\`

## 1. Zero Engine Dependencies (`netstandard2.1`)
- Keep `RealmzCore.csproj` strictly on `netstandard2.1` (`LangVersion 9.0`, `Nullable enable`, `Deterministic true`).
- NEVER add `using UnityEngine`, `using UnityEditor`, or any `<PackageReference` NuGet dependency.
- All Unity-side scripts consume the compiled Release assembly at `remaster/RealmzUnity/Assets/Plugins/RealmzCore/RealmzCore.dll`.

## 2. Locked Gameplay Contracts
- **Fixed 6-AP Combat Contract:** `BaseApPerTurn = 6` and `MaxCarryoverAp = 2` (`CombatActionEconomy` in `Rules/CombatRules.cs`). Zero Agility or Fatigue AP modifiers; only Equipment Sets (`Scout's Leather`, `Iron Vanguard`) and gear may modify AP or Free Move.
- **Deterministic Simulation:** Use `RealmzCore.Rules.Dice` / explicit seeds for encounter rolls, combat math, and arena generation.
- **Zero GC Churn in Hot Paths:** Avoid LINQ allocations and per-step heap churn inside exploration step rolls, `FogOfWarMap` LOS evaluation, and combat turn resolution.

## 3. Mandatory Post-Edit Sync
After any edit in `RealmzCore/`, rebuild Release, run `RealmzCore.Tests`, and sync the plugin DLL in one command:
```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 sync-core
```
