[CmdletBinding()]
param(
    [string]$RemasterRoot = "C:\Users\karti\realmz\remaster",
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
$SmokeBuilderSource = Join-Path $PSScriptRoot "M0SmokeSceneBuilder.cs"
$BillboardSource = Join-Path $PSScriptRoot "PixelBillboard.cs"
$InstallerDestDir = Join-Path $ProjectPath "Assets\Editor\ProjectBootstrap"
$RenderScriptsDestDir = Join-Path $ProjectPath "Assets\Scripts\Rendering"
$SpritesDestDir = Join-Path $ProjectPath "Assets\Art\Sprites"
$TilesDestDir = Join-Path $ProjectPath "Assets\Art\Tiles"
$ArtPipelineSamplesDir = Join-Path $RemasterRoot "art-pipeline\samples"
$ExtractedDir = Join-Path $RemasterRoot "extracted"

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
if (Test-Path $SmokeBuilderSource) {
    Copy-Item -Path $SmokeBuilderSource -Destination (Join-Path $InstallerDestDir "M0SmokeSceneBuilder.cs") -Force
}

New-Item -ItemType Directory -Path $RenderScriptsDestDir -Force | Out-Null
if (Test-Path $BillboardSource) {
    Copy-Item -Path $BillboardSource -Destination (Join-Path $RenderScriptsDestDir "PixelBillboard.cs") -Force
}

New-Item -ItemType Directory -Path $SpritesDestDir -Force | Out-Null
New-Item -ItemType Directory -Path $TilesDestDir -Force | Out-Null
foreach ($sprite in @("knight_hero_64x64.png", "knight_hero_64x64_n.png")) {
    $srcPath = Join-Path $ArtPipelineSamplesDir $sprite
    if (Test-Path $srcPath) {
        Copy-Item -Path $srcPath -Destination (Join-Path $SpritesDestDir $sprite) -Force
    }
}
foreach ($sprite in @("monster_giant_frog_392.png", "hero_kevlar_tactical_9018.png")) {
    $srcPath = Join-Path $ExtractedDir $sprite
    if (Test-Path $srcPath) {
        Copy-Item -Path $srcPath -Destination (Join-Path $SpritesDestDir $sprite) -Force
    }
}
$grassSrc = Join-Path $ExtractedDir "terrain_bywater_tile_grass.png"
if (Test-Path $grassSrc) {
    Copy-Item -Path $grassSrc -Destination (Join-Path $TilesDestDir "terrain_bywater_tile_grass.png") -Force
}

Write-Host "[5/6] Opening Unity Editor and waiting for ready state..."
unity open $ProjectPath
unity status --until-ready --project-path $ProjectPath --format json
if ($LASTEXITCODE -ne 0) {
    throw "Editor did not become ready (exit code $LASTEXITCODE)."
}

Write-Host "[6/6] Ensuring UPM packages (com.unity.cinemachine, com.unity.ugui)..."
$packages = @("com.unity.cinemachine", "com.unity.ugui")
$schemaCheck = unity command --query package --format json --project-path $ProjectPath 2>$null
if ($LASTEXITCODE -eq 0 -and $schemaCheck) {
    foreach ($pkg in $packages) {
        unity command package add --project-path $ProjectPath --name $pkg --confirm --wait --caller plugin --skill unity-package-management
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "unity command package add returned non-zero for $pkg; verify in Packages/manifest.json."
        }
    }
    unity command package list --project-path $ProjectPath --caller plugin --skill unity-package-management
} else {
    Write-Warning "Pipeline 'package' commands not exposed. Close the Editor and run the headless fallback:"
    Write-Warning "  <Unity.exe> -batchmode -projectPath `"$ProjectPath`" -executeMethod ProjectBootstrap.PackageInstaller.Install -logFile - (NO -quit)"
}

Write-Host "Bootstrap complete! RealmzCore.dll staged at '$PluginDestDir\RealmzCore.dll'."
