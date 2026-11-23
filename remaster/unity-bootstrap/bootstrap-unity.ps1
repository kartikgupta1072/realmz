[CmdletBinding()]
param(
    [string]$RemasterRoot = "C:\realmz\remaster",
    [string]$EditorVersion = "6000.0.85f2",
    [string]$DotnetExe = "C:\Users\karti\AppData\Local\Microsoft\dotnet\dotnet.exe"
)

$ErrorActionPreference = "Stop"

$ProjectName = "RealmzUnity"
$ProjectPath = Join-Path $RemasterRoot $ProjectName
$CoreCsproj = Join-Path $RemasterRoot "RealmzCore\RealmzCore.csproj"
$CoreDllSource = Join-Path $RemasterRoot "RealmzCore\bin\Release\netstandard2.1\RealmzCore.dll"
$PluginDestDir = Join-Path $ProjectPath "Assets\Plugins\RealmzCore"
$InstallerSource = Join-Path $PSScriptRoot "PackageInstaller.cs"
$InstallerDestDir = Join-Path $ProjectPath "Assets\Editor\ProjectBootstrap"

function Require-Command($Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

Require-Command "unity"

if (-not (Test-Path $DotnetExe)) {
    $fallbackDotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if ($null -eq $fallbackDotnet) {
        throw "dotnet executable not found at '$DotnetExe' or on PATH."
    }
    $DotnetExe = $fallbackDotnet.Source
}

Write-Host "[1/6] Checking Unity authentication and license..."
unity auth status --format json | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Unity CLI is not signed in. Run 'unity auth login' first."
}
unity license status --format json | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "No active Unity license found. Run 'unity license activate --personal --accept-eula' first."
}

if (-not (Test-Path $ProjectPath)) {
    Write-Host "[2/6] Creating Unity project '$ProjectName' (3D URP Forward Renderer, editor $EditorVersion)..."
    unity projects create $ProjectName `
        --path $RemasterRoot `
        --editor-version $EditorVersion `
        --template 3d `
        --no-cloud `
        --format json
    if ($LASTEXITCODE -ne 0) {
        throw "unity projects create failed with exit code $LASTEXITCODE."
    }
} else {
    Write-Host "[2/6] Project already exists at '$ProjectPath', skipping create."
}

Write-Host "[3/6] Installing Unity Pipeline package..."
unity pipeline install --project-path $ProjectPath
if ($LASTEXITCODE -ne 0) {
    throw "unity pipeline install failed with exit code $LASTEXITCODE."
}

Write-Host "[4/6] Building RealmzCore (Release, netstandard2.1) and copying DLL..."
& $DotnetExe build $CoreCsproj --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $CoreDllSource)) {
    throw "Built RealmzCore.dll not found at '$CoreDllSource'."
}

New-Item -ItemType Directory -Path $PluginDestDir -Force | Out-Null
Copy-Item -Path $CoreDllSource -Destination (Join-Path $PluginDestDir "RealmzCore.dll") -Force

New-Item -ItemType Directory -Path $InstallerDestDir -Force | Out-Null
Copy-Item -Path $InstallerSource -Destination (Join-Path $InstallerDestDir "PackageInstaller.cs") -Force

Write-Host "[5/6] Opening Unity Editor and waiting for ready state..."
unity open $ProjectPath
unity status --until-ready --project-path $ProjectPath --format json
if ($LASTEXITCODE -ne 0) {
    throw "Editor did not become ready (exit code $LASTEXITCODE)."
}

Write-Host "[6/6] Installing UPM packages (com.unity.cinemachine, com.unity.textmeshpro, com.unity.ugui)..."
$packages = @("com.unity.cinemachine", "com.unity.textmeshpro", "com.unity.ugui")
$schemaCheck = unity command --query package --format json --project-path $ProjectPath 2>$null
if ($LASTEXITCODE -eq 0 -and $schemaCheck) {
    foreach ($pkg in $packages) {
        unity command package add --project-path $ProjectPath --id $pkg --caller plugin --skill unity-package-management
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "unity command package add failed for $pkg; run ProjectBootstrap.PackageInstaller.Install in batchmode without -quit if needed."
        }
    }
    unity command package list --project-path $ProjectPath --caller plugin --skill unity-package-management
} else {
    Write-Warning "Pipeline 'package' commands not exposed. Close the Editor and run the headless fallback:"
    Write-Warning "  <Unity.exe> -batchmode -projectPath `"$ProjectPath`" -executeMethod ProjectBootstrap.PackageInstaller.Install -logFile - (NO -quit)"
}

Write-Host "Bootstrap complete! RealmzCore.dll staged at '$PluginDestDir\RealmzCore.dll'."
