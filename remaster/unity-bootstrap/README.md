# Realmz Unity 6 Bootstrap

This folder sets up `C:\Users\karti\realmz\remaster\RealmzUnity` (Unity 6 URP 3D Forward Renderer) and drops in the zero-dependency `RealmzCore.dll` simulation and data library.

## Prerequisites (One-Time Setup)

1. Install the Unity CLI if `unity --version` is not yet on your PATH:
   ```powershell
   irm https://unity.com/install.ps1 | iex
   ```
   Open a new PowerShell window after installation so `unity` is on PATH.

2. Sign in to your Unity account:
   ```powershell
   unity auth login
   ```

3. Activate your Unity Personal license:
   ```powershell
   unity license activate --personal --accept-eula
   ```

4. Ensure Unity Editor `6000.0.85f2` is installed (or install it with `unity install 6000.0.85f2 --yes --accept-eula`).

## 1-Click Bootstrap

From `C:\Users\karti\realmz\remaster\unity-bootstrap`:

```powershell
.\bootstrap-unity.ps1
```

What `bootstrap-unity.ps1` does:
1. Creates `C:\Users\karti\realmz\remaster\RealmzUnity` via `unity projects create "RealmzUnity" --path "C:\Users\karti\realmz\remaster" --editor-version 6000.0.85f2 --template 3d --no-cloud --format json`.
2. Installs the Pipeline package (`unity pipeline install`) before first launch.
3. Opens the Unity Editor and waits until `unity status --until-ready` reports `ready`.
4. Installs `com.unity.cinemachine`, `com.unity.textmeshpro`, and `com.unity.ugui` (using `unity command package add`, or copying `PackageInstaller.cs` for headless `Client.AddAndRemove` fallback).
5. Builds `RealmzCore.csproj` (`netstandard2.1`, Release) and copies `RealmzCore.dll` to `C:\Users\karti\realmz\remaster\RealmzUnity\Assets\Plugins\RealmzCore\RealmzCore.dll`.
6. Stages the verified `knight_hero_64x64.png` + `knight_hero_64x64_n.png` sprites and extracted City of Bywater assets (`terrain_bywater_tile_grass.png`, `monster_giant_frog_392.png`, `hero_kevlar_tactical_9018.png`) plus `PixelBillboard.cs` and `M0SmokeSceneBuilder.cs`.

## Build M0 HD-2D Smoke Scene

Once the Editor is ready, run `Realmz > M0 > Build HD-2D Smoke Scene` (or invoke `ProjectBootstrap.M0SmokeSceneBuilder.BuildSmokeScene()` via `unity command script eval`) to configure crisp Point/uncompressed Sprite and Normal-Map imports, generate `Assets/Scenes/M0_HD2D_SmokeScene.unity` (URP 3D camera, Bywater grass floor, foot-anchored normal-mapped Knight Hero + Giant Frog billboards, warm torch Point Light, directional shadow, Global Volume ACES + Bloom), and capture `M0_SmokeScene_Preview.png` via `Realmz > M0 > Capture Smoke Scene Preview`.
