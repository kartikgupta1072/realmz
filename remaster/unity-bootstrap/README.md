# Realmz Unity 6 Bootstrap

This folder sets up `C:\realmz\remaster\RealmzUnity` (Unity 6 URP 3D Forward Renderer) and drops in the zero-dependency `RealmzCore.dll` simulation and data library.

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

From `C:\realmz\remaster\unity-bootstrap`:

```powershell
.\bootstrap-unity.ps1
```

What `bootstrap-unity.ps1` does:
1. Creates `C:\realmz\remaster\RealmzUnity` via `unity projects create "RealmzUnity" --path "C:\realmz\remaster" --editor-version 6000.0.85f2 --template 3d --no-cloud --format json`.
2. Installs the Pipeline package (`unity pipeline install`) before first launch.
3. Opens the Unity Editor and waits until `unity status --until-ready` reports `ready`.
4. Installs `com.unity.cinemachine`, `com.unity.textmeshpro`, and `com.unity.ugui` (using `unity command package add`, or copying `PackageInstaller.cs` for headless `Client.AddAndRemove` fallback).
5. Builds `RealmzCore.csproj` (`netstandard2.1`, Release) and copies `RealmzCore.dll` to `C:\realmz\remaster\RealmzUnity\Assets\Plugins\RealmzCore\RealmzCore.dll`.
