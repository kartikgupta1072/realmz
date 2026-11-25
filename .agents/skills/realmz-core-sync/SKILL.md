---
name: realmz-core-sync
description: >-
  Build RealmzCore in Release, run RealmzCore.Tests, verify zero UnityEngine
  dependencies, and sync RealmzCore.dll into RealmzUnity. Use after any edit to
  remaster/RealmzCore/ or before running Unity tests or builds.
---

# RealmzCore Build, Test & DLL Sync

`RealmzCore` (`netstandard2.1`) is the deterministic simulation core shared with `RealmzUnity` via a compiled Release DLL. Whenever C# code inside `remaster/RealmzCore/` changes, Unity will not see those changes until `RealmzCore.dll` is rebuilt and copied into `Assets/Plugins/RealmzCore/`.

## Procedure

1. **Run `sync-core`:**
   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 sync-core
   ```
   This performs:
   - `dotnet build C:\Users\karti\realmz\remaster\RealmzCore\RealmzCore.csproj -c Release`
   - `dotnet test C:\Users\karti\realmz\remaster\RealmzCore.Tests\RealmzCore.Tests.csproj -c Release`
   - Copies `remaster/RealmzCore/bin/Release/netstandard2.1/RealmzCore.dll` to `remaster/RealmzUnity/Assets/Plugins/RealmzCore/RealmzCore.dll`
   - Verifies SHA-256 parity between both DLLs.

2. **Run Invariant Check (`check`):**
   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 check
   ```
   Confirms zero `UnityEngine`/`UnityEditor` references or NuGet `<PackageReference` entries crept into `RealmzCore`.

3. **Notify Unity Editor (if open):**
   If Unity Editor is running, recompile strictly so the refreshed plugin DLL reloads cleanly:
   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Users\karti\realmz\remaster\dev.ps1 unity-recompile
   ```
