[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = "help",
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$Mode = "All",
    [switch]$CaptureProofs,
    [string]$Filter = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$RemasterRoot = $PSScriptRoot
$CoreCsproj = Join-Path $RemasterRoot "RealmzCore\RealmzCore.csproj"
$CoreTestsCsproj = Join-Path $RemasterRoot "RealmzCore.Tests\RealmzCore.Tests.csproj"
$CoreReleaseDll = Join-Path $RemasterRoot "RealmzCore\bin\Release\netstandard2.1\RealmzCore.dll"
$UnityProject = Join-Path $RemasterRoot "RealmzUnity"
$UnityPluginDll = Join-Path $UnityProject "Assets\Plugins\RealmzCore\RealmzCore.dll"
$ScriptsDir = Join-Path $UnityProject "Assets\Scripts"
$ScratchDir = Join-Path $RepoRoot ".scratch"
$RunsDir = Join-Path $ScratchDir "runs"
$ProofsDir = Join-Path $ScratchDir "proofs"
$BuildExe = Join-Path $RemasterRoot "builds\Realmz_M1\Realmz.exe"

$DotnetExe = "C:\Users\karti\AppData\Local\Microsoft\dotnet\dotnet.exe"
$UnityExe = "C:\Unity\Editors\6000.0.85f2\Editor\Unity.exe"
$UnityCliExe = "C:\Users\karti\AppData\Local\Unity\bin\unity.exe"
$FoundryVenvPy = Join-Path $RepoRoot "tools\asset-foundry\.venv\Scripts\python.exe"
$FoundryRepoBin = Join-Path $RemasterRoot "asset-foundry\bin\realmz-foundry.py"
$FoundryLocalBin = "C:\Users\karti\.local\bin\realmz-foundry.py"

function Write-Section([string]$Title) {
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

function Ensure-Dir([string]$DirPath) {
    if (-not (Test-Path $DirPath)) {
        New-Item -ItemType Directory -Path $DirPath -Force | Out-Null
    }
}

function Get-FileSha256([string]$FilePath) {
    if (-not (Test-Path $FilePath)) {
        return $null
    }
    return (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash
}

function Test-UnityEditorRunning {
    $procs = Get-Process -Name "Unity" -ErrorAction SilentlyContinue
    if ($null -eq $procs) {
        return $false
    }
    return (@($procs).Count -gt 0)
}

function Get-CoreDllSyncState {
    if (-not (Test-Path $UnityPluginDll)) {
        return "PLUGIN_MISSING"
    }
    if (-not (Test-Path $CoreReleaseDll)) {
        return "RELEASE_NOT_BUILT"
    }
    $h1 = Get-FileSha256 $CoreReleaseDll
    $h2 = Get-FileSha256 $UnityPluginDll
    if ($h1 -ne $h2) {
        return "DLL_MISMATCH"
    }
    $pluginTime = (Get-Item $UnityPluginDll).LastWriteTimeUtc
    $coreFiles = Get-ChildItem -Path (Join-Path $RemasterRoot "RealmzCore") -Recurse -File |
        Where-Object {
            ($_.Extension -eq ".cs" -or $_.Extension -eq ".csproj") -and
            $_.FullName -notmatch "\\(bin|obj)\\"
        }
    foreach ($f in $coreFiles) {
        if ($f.LastWriteTimeUtc -gt $pluginTime.AddSeconds(1)) {
            return "STALE_SOURCE ($($f.Name))"
        }
    }
    return "IN_SYNC"
}

function Strip-CSharpCommentsAndStrings([string]$Text) {
    $pattern = '//[^\r\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*'''
    return [regex]::Replace($Text, $pattern, {
        param($m)
        $nlCount = ([regex]::Matches($m.Value, "`n")).Count
        return ("`n" * $nlCount)
    })
}

function Get-TopLevelMonoBehaviours([string]$Text) {
    $cleaned = Strip-CSharpCommentsAndStrings $Text
    $hasBlockNs = [regex]::IsMatch($cleaned, '\bnamespace\s+[A-Za-z0-9_.]+\s*\{')
    $topDepth = 0
    if ($hasBlockNs) {
        $topDepth = 1
    }
    $classRegex = [regex]'\b(?:public|internal|private|protected)?\s*(?:sealed|abstract|partial)?\s*class\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]+>)?\s*:[^{]*\bMonoBehaviour\b'
    $found = New-Object System.Collections.Generic.List[string]
    $depth = 0
    $i = 0
    $len = $cleaned.Length
    while ($i -lt $len) {
        $ch = $cleaned[$i]
        if ($ch -eq '{') {
            $depth++
            $i++
            continue
        }
        if ($ch -eq '}') {
            if ($depth -gt 0) { $depth-- }
            $i++
            continue
        }
        if ($depth -le $topDepth -and $ch -match '[A-Za-z]') {
            $m = $classRegex.Match($cleaned, $i)
            if ($m.Success -and $m.Index -eq $i) {
                $found.Add($m.Groups[1].Value)
                $i = $m.Index + $m.Length
                continue
            }
        }
        $i++
    }
    return $found
}

function Invoke-Status {
    Write-Section "Realmz Workspace Status"
    Push-Location $RepoRoot
    try {
        $branch = (& git rev-parse --abbrev-ref HEAD).Trim()
        $head = (& git rev-parse --short HEAD).Trim()
        $dirtyLines = @(& git status --porcelain | Where-Object { $_.Trim() -ne "" })
    }
    finally {
        Pop-Location
    }

    $dllState = Get-CoreDllSyncState
    $unityRunning = Test-UnityEditorRunning
    $unityStatusText = "IDLE (no Unity.exe running)"
    if ($unityRunning) {
        $unityStatusText = "RUNNING"
        if (Test-Path $UnityCliExe) {
            try {
                $cliJson = & $UnityCliExe status --project-path $UnityProject --json 2>$null
                if ($cliJson) {
                    $unityStatusText = "RUNNING (Pipeline CLI responsive)"
                }
            }
            catch {
            }
        }
    }

    $buildInfo = "MISSING"
    if (Test-Path $BuildExe) {
        $item = Get-Item $BuildExe
        $buildDir = Split-Path -Parent $BuildExe
        $dirFiles = @(Get-ChildItem -Path $buildDir -Recurse -File)
        $sumBytes = ($dirFiles | Measure-Object -Property Length -Sum).Sum
        $mb = [math]::Round($sumBytes / 1MB, 2)
        $buildInfo = "$mb MB ($($dirFiles.Count) files, modified $($item.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))"
    }

    $proofCount = 0
    $latestProof = "none"
    if (Test-Path $ProofsDir) {
        $proofs = @(Get-ChildItem -Path $ProofsDir -Filter "*.png" -File | Sort-Object LastWriteTime -Descending)
        $proofCount = $proofs.Count
        if ($proofCount -gt 0) {
            $latestProof = "$($proofs[0].Name) ($($proofs[0].LastWriteTime.ToString('yyyy-MM-dd HH:mm')))"
        }
    }

    Write-Host "Repo Root     : $RepoRoot"
    Write-Host "Git Branch    : $branch @ $head ($($dirtyLines.Count) dirty)"
    Write-Host "RealmzCore DLL: $dllState"
    Write-Host "Unity Editor  : $unityStatusText"
    Write-Host "Standalone Exe: $buildInfo"
    Write-Host "Proof PNGs    : $proofCount in .scratch/proofs (latest: $latestProof)"
}

function Invoke-Check {
    Write-Section "Workspace Invariant Check"
    $failures = New-Object System.Collections.Generic.List[string]

    # 1. Git branch safety
    Push-Location $RepoRoot
    try {
        $branch = (& git rev-parse --abbrev-ref HEAD).Trim()
        if ($branch -eq "main") {
            $failures.Add("Git branch is 'main'. All work must happen on 'Remaster'.")
        }
        else {
            Write-Host "[OK] Git branch: $branch" -ForegroundColor Green
        }
    }
    finally {
        Pop-Location
    }

    # 2. Pure RealmzCore check
    $csprojText = Get-Content -Path $CoreCsproj -Raw
    if ($csprojText -match "<PackageReference") {
        $failures.Add("RealmzCore.csproj must not declare any <PackageReference> dependencies.")
    }
    $coreCsFiles = @(Get-ChildItem -Path (Join-Path $RemasterRoot "RealmzCore") -Filter "*.cs" -Recurse |
        Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" })
    $coreViolations = @()
    foreach ($f in $coreCsFiles) {
        $txt = Get-Content -Path $f.FullName -Raw
        if ($txt -match "\bUnityEngine\b|\bUnityEditor\b") {
            $coreViolations += $f.FullName
        }
    }
    if ($coreViolations.Count -gt 0) {
        $failures.Add("UnityEngine/UnityEditor reference found in RealmzCore: $($coreViolations -join ', ')")
    }
    else {
        Write-Host "[OK] RealmzCore purity: 0 UnityEngine/UnityEditor/NuGet dependencies ($($coreCsFiles.Count) files)" -ForegroundColor Green
    }

    # 3. RealmzCore.dll parity
    $dllState = Get-CoreDllSyncState
    if ($dllState -ne "IN_SYNC") {
        $failures.Add("RealmzCore.dll is not in sync ($dllState). Run: remaster/dev.ps1 sync-core")
    }
    else {
        Write-Host "[OK] RealmzCore.dll parity: IN_SYNC" -ForegroundColor Green
    }

    # 4. 1-MonoBehaviour-per-file check
    $unityScripts = @(Get-ChildItem -Path $ScriptsDir -Filter "*.cs" -Recurse)
    $monoViolations = @()
    foreach ($f in $unityScripts) {
        $txt = Get-Content -Path $f.FullName -Raw
        $classes = @(Get-TopLevelMonoBehaviours $txt)
        if ($classes.Count -gt 1) {
            $monoViolations += "$($f.Name) has $($classes.Count) top-level MonoBehaviours ($($classes -join ', '))"
        }
        elseif ($classes.Count -eq 1) {
            $prefix = $f.Name.Split('.')[0]
            if ($classes[0] -ne $prefix) {
                $monoViolations += "$($f.Name) declares '$($classes[0])' (prefix mismatch '$prefix')"
            }
        }
    }
    if ($monoViolations.Count -gt 0) {
        $failures.Add("MonoBehaviour safety violation: $($monoViolations -join '; ')")
    }
    else {
        Write-Host "[OK] Standalone safety: 1 top-level MonoBehaviour per file ($($unityScripts.Count) scripts scanned)" -ForegroundColor Green
    }

    # 5. Zero em/en-dashes check
    $emChar = [char]0x2014
    $enChar = [char]0x2013
    $scanExts = @(".cs", ".csproj", ".md", ".py", ".ps1", ".shader", ".hlsl", ".uss", ".uxml", ".json", ".txt", ".asmdef")
    $targets = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    if (Test-Path (Join-Path $RepoRoot "GEMINI.md")) {
        $targets.Add((Get-Item (Join-Path $RepoRoot "GEMINI.md")))
    }
    foreach ($rootDir in @((Join-Path $RepoRoot ".agents"), $RemasterRoot)) {
        if (Test-Path $rootDir) {
            $items = Get-ChildItem -Path $rootDir -Recurse -File | Where-Object {
                $scanExts -contains $_.Extension.ToLowerInvariant() -and
                $_.FullName -notmatch "\\(Library|Temp|Logs|obj|bin|builds|extracted|__pycache__|\.venv|\.scratch|node_modules|TextMesh Pro)\\"
            }
            foreach ($it in $items) {
                $targets.Add($it)
            }
        }
    }
    $dashHits = @()
    foreach ($tf in $targets) {
        $raw = Get-Content -Path $tf.FullName -Raw
        if ($null -ne $raw -and ($raw.IndexOf($emChar) -ge 0 -or $raw.IndexOf($enChar) -ge 0)) {
            $dashHits += $tf.FullName.Substring($RepoRoot.Length + 1)
        }
    }
    if ($dashHits.Count -gt 0) {
        $failures.Add("Em/en-dash characters found in: $($dashHits -join ', ')")
    }
    else {
        Write-Host "[OK] Dash hygiene: 0 em/en-dashes across $($targets.Count) files" -ForegroundColor Green
    }

    # 6. realmz-foundry.py SHA-256 mirror parity
    if ((Test-Path $FoundryRepoBin) -and (Test-Path $FoundryLocalBin)) {
        $fh1 = Get-FileSha256 $FoundryRepoBin
        $fh2 = Get-FileSha256 $FoundryLocalBin
        if ($fh1 -ne $fh2) {
            $failures.Add("realmz-foundry.py mismatch between repo and $FoundryLocalBin")
        }
        else {
            Write-Host "[OK] realmz-foundry.py mirror parity: SHA-256 match" -ForegroundColor Green
        }
    }
    else {
        Write-Host "[WARN] realmz-foundry mirror check skipped (local mirror not found at $FoundryLocalBin)" -ForegroundColor Yellow
    }

    if ($failures.Count -gt 0) {
        Write-Host ""
        foreach ($err in $failures) {
            Write-Host "[FAIL] $err" -ForegroundColor Red
        }
        exit 1
    }
}

function Invoke-SyncCore {
    Write-Section "Build RealmzCore (Release) + Run Tests + Sync DLL"
    & $DotnetExe build $CoreCsproj -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $DotnetExe test $CoreTestsCsproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) {
        # Fallback if test project needs rebuild
        & $DotnetExe test $CoreTestsCsproj -c Release
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    Ensure-Dir (Split-Path -Parent $UnityPluginDll)
    Copy-Item -Path $CoreReleaseDll -Destination $UnityPluginDll -Force
    $h1 = Get-FileSha256 $CoreReleaseDll
    $h2 = Get-FileSha256 $UnityPluginDll
    if ($h1 -ne $h2) {
        Write-Host "[FAIL] RealmzCore.dll SHA-256 mismatch after copy." -ForegroundColor Red
        exit 1
    }
    Write-Host "[OK] Synced RealmzCore.dll (SHA-256: $h2)" -ForegroundColor Green
}

function Invoke-TestPython {
    Write-Section "Python Tests (realmz-exporter + asset-foundry)"
    $exporterDir = Join-Path $RemasterRoot "realmz-exporter"
    & python -m unittest discover -s $exporterDir -p "test_*.py" -v
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $foundryTestsDir = Join-Path $RemasterRoot "asset-foundry\tests"
    if (Test-Path $FoundryVenvPy) {
        & $FoundryVenvPy -m unittest discover -s $foundryTestsDir -v
    }
    else {
        & python -m unittest discover -s $foundryTestsDir -v
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Invoke-UnityRecompile {
    Write-Section "Unity Strict Recompile"
    if (-not (Test-UnityEditorRunning)) {
        Write-Host "[INFO] No running Unity Editor detected. Open RealmzUnity with Pipeline installed, or run 'dev.ps1 unity-tests' in batchmode." -ForegroundColor Yellow
        exit 1
    }
    & $UnityCliExe recompile --strict --project-path $UnityProject
    exit $LASTEXITCODE
}

function Invoke-SingleUnityTestSuite([string]$Platform) {
    Ensure-Dir $RunsDir
    $xmlPath = Join-Path $RunsDir "${Platform}_Results.xml"
    $logPath = Join-Path $RunsDir "${Platform}_Unity.log"
    if (Test-Path $xmlPath) { Remove-Item $xmlPath -Force }

    $prevCapture = $env:REALMZ_CAPTURE_PROOFS
    $prevProofDir = $env:REALMZ_PROOF_DIR
    try {
        if ($CaptureProofs -and $Platform -eq "PlayMode") {
            Ensure-Dir $ProofsDir
            $env:REALMZ_CAPTURE_PROOFS = "1"
            $env:REALMZ_PROOF_DIR = $ProofsDir
            Write-Host "Capturing PlayMode proof screenshots to: $ProofsDir" -ForegroundColor Yellow
        }
        else {
            $env:REALMZ_CAPTURE_PROOFS = $null
        }

        $unityArgs = @(
            "-batchmode",
            "-projectPath", $UnityProject,
            "-runTests",
            "-testPlatform", $Platform,
            "-testResults", $xmlPath,
            "-logFile", $logPath
        )
        if (-not [string]::IsNullOrWhiteSpace($Filter)) {
            $unityArgs += @("-testFilter", $Filter)
        }

        Write-Host "Running Unity $Platform tests..."
        & $UnityExe @unityArgs
        $exitCode = $LASTEXITCODE

        if (-not (Test-Path $xmlPath)) {
            Write-Host "[FAIL] Test results XML not generated at $xmlPath. Check log: $logPath" -ForegroundColor Red
            exit 1
        }

        [xml]$doc = Get-Content -Path $xmlPath
        $run = $doc.'test-run'
        Write-Host "$Platform Summary: total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped) duration=$($run.duration)s"
        if ([int]$run.failed -gt 0) {
            $failedCases = $doc.SelectNodes("//test-case[@result='Failed']")
            foreach ($fc in $failedCases) {
                Write-Host "  [FAILED] $($fc.fullname)" -ForegroundColor Red
            }
            exit 1
        }
        if ($exitCode -ne 0) {
            Write-Host "[FAIL] Unity exited with code $exitCode. Check log: $logPath" -ForegroundColor Red
            exit $exitCode
        }
    }
    finally {
        $env:REALMZ_CAPTURE_PROOFS = $prevCapture
        $env:REALMZ_PROOF_DIR = $prevProofDir
    }
}

function Invoke-UnityTests {
    Write-Section "Unity Test Runner ($Mode)"
    if (Test-UnityEditorRunning) {
        Write-Host "[BLOCKED] Unity Editor is currently open and holds the project lock." -ForegroundColor Red
        Write-Host "Close Unity Editor before running batchmode tests, or run tests inside the open Editor." -ForegroundColor Yellow
        exit 1
    }
    if ($Mode -eq "EditMode" -or $Mode -eq "All") {
        Invoke-SingleUnityTestSuite "EditMode"
    }
    if ($Mode -eq "PlayMode" -or $Mode -eq "All") {
        Invoke-SingleUnityTestSuite "PlayMode"
    }
}

function Invoke-Build {
    Invoke-SyncCore
    Write-Section "Build Windows Standalone (Realmz.exe)"
    if (Test-UnityEditorRunning) {
        Write-Host "[BLOCKED] Unity Editor is currently open and holds the project lock. Close it or use Realmz -> M1 -> Build Windows Standalone (x64)." -ForegroundColor Red
        exit 1
    }
    Ensure-Dir $RunsDir
    $logPath = Join-Path $RunsDir "standalone_build.log"
    & $UnityExe -batchmode -quit -projectPath $UnityProject -executeMethod "Realmz.EditorTools.RealmzM1BuildMenu.BuildWindowsStandalone" -logFile $logPath
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] Unity standalone build failed (exit=$LASTEXITCODE). Check $logPath" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    if (-not (Test-Path $BuildExe)) {
        Write-Host "[FAIL] Expected build output missing: $BuildExe" -ForegroundColor Red
        exit 1
    }
    $mb = [math]::Round((Get-Item $BuildExe).Length / 1MB, 2)
    Write-Host "[OK] Built $BuildExe ($mb MB)" -ForegroundColor Green
}

function Invoke-Smoke {
    Write-Section "Standalone 8s Smoke Test"
    if (-not (Test-Path $BuildExe)) {
        Write-Host "[FAIL] Standalone exe not found: $BuildExe. Run 'dev.ps1 build' first." -ForegroundColor Red
        exit 1
    }
    $proc = Start-Process -FilePath $BuildExe -PassThru
    Start-Sleep -Seconds 8
    $alive = -not $proc.HasExited
    if ($alive) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
    Write-Host "alive_at_8s=$alive"
    if (-not $alive) {
        Write-Host "[FAIL] Realmz.exe exited before 8 seconds (exitCode=$($proc.ExitCode))." -ForegroundColor Red
        exit 1
    }

    $localLow = Join-Path $env:USERPROFILE "AppData\LocalLow"
    $playerLogs = @(Get-ChildItem -Path $localLow -Filter "Player.log" -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "Realmz" } |
        Sort-Object LastWriteTime -Descending)
    if ($playerLogs.Count -gt 0) {
        $latestLog = $playerLogs[0].FullName
        Write-Host "Inspecting Player.log: $latestLog"
        $errLines = @(Select-String -Path $latestLog -Pattern "Exception|NullReferenceException|Assertion failed|Shader error" -ErrorAction SilentlyContinue)
        if ($errLines.Count -gt 0) {
            foreach ($el in $errLines) {
                Write-Host "  [LOG ERROR] $($el.Line)" -ForegroundColor Red
            }
            exit 1
        }
        Write-Host "[OK] Player.log clean (0 errors/exceptions)" -ForegroundColor Green
    }
    else {
        Write-Host "[WARN] No Realmz Player.log found under $localLow" -ForegroundColor Yellow
    }
}

function Invoke-Help {
    Write-Host "Realmz HD-2D Remaster Dev CLI (remaster/dev.ps1)" -ForegroundColor Cyan
    Write-Host "Usage: powershell -ExecutionPolicy Bypass -File remaster/dev.ps1 <command> [options]"
    Write-Host ""
    Write-Host "Commands:"
    Write-Host "  status           Show git, RealmzCore.dll parity, Unity Editor, build, and proof status"
    Write-Host "  check            Run static invariant checks (branch, Core purity, MonoBehaviour, dashes, mirror)"
    Write-Host "  sync-core        Build RealmzCore Release, run RealmzCore.Tests, and sync RealmzCore.dll"
    Write-Host "  test-python      Run realmz-exporter and asset-foundry Python unit tests"
    Write-Host "  test-fast        Run sync-core + check + test-python"
    Write-Host "  unity-recompile  Run unity.exe recompile --strict on open Unity Editor"
    Write-Host "  unity-tests      Run Unity batchmode tests (-Mode EditMode|PlayMode|All [-CaptureProofs] [-Filter <name>])"
    Write-Host "  proof            Run PlayMode tests with REALMZ_CAPTURE_PROOFS=1 to .scratch/proofs/"
    Write-Host "  build            Sync RealmzCore and build Windows Standalone Realmz.exe"
    Write-Host "  smoke            Launch Realmz.exe for 8s and verify 0 Player.log errors"
    Write-Host "  test-all         Run test-fast + unity-tests -Mode All"
}

switch ($Command.ToLowerInvariant()) {
    "status"          { Invoke-Status }
    "check"           { Invoke-Check }
    "sync-core"       { Invoke-SyncCore }
    "test-python"     { Invoke-TestPython }
    "test-fast"       { Invoke-SyncCore; Invoke-Check; Invoke-TestPython }
    "unity-recompile" { Invoke-UnityRecompile }
    "unity-tests"     { Invoke-UnityTests }
    "proof"           { $Mode = "PlayMode"; $CaptureProofs = $true; Invoke-UnityTests }
    "build"           { Invoke-Build }
    "smoke"           { Invoke-Smoke }
    "test-all"        { Invoke-SyncCore; Invoke-Check; Invoke-TestPython; $Mode = "All"; Invoke-UnityTests }
    default           { Invoke-Help }
}
