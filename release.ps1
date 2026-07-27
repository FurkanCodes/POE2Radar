# Build a distributable POE2Radar release folder + zip.
# Dev builds use `dotnet build` (bin/Debug or bin/Release). This script is for shipping only.
#
# Usage:
#   ./release.ps1                    # version from POE2Radar.Overlay.csproj
#   ./release.ps1 -Version 0.15.2
#   ./release.ps1 -Version 0.15.2 -SkipZip
param(
    [string]$Version = "",
    [switch]$SkipZip,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

function Test-LaunchedFromExplorer {
    try {
        $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
        $parentName = (Get-CimInstance Win32_Process -Filter "ProcessId=$parentId").Name
        return $parentName -in @('explorer.exe', 'OpenWith.exe')
    }
    catch {
        return $false
    }
}

function Wait-IfNeeded {
    param([int]$ExitCode = 0)
    if ($NoPause) { return }
    if ($env:CI -eq 'true' -or $env:GITHUB_ACTIONS -eq 'true') { return }
    if (-not (Test-LaunchedFromExplorer)) { return }
    Write-Host ""
    if ($ExitCode -ne 0) {
        Write-Host "Release FAILED (exit $ExitCode)." -ForegroundColor Red
    }
    else {
        Write-Host "Release finished." -ForegroundColor Green
    }
    Read-Host "Press Enter to close"
}

trap {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Wait-IfNeeded 1
    exit 1
}
$root = $PSScriptRoot
$project = Join-Path $root "src/POE2Radar.Overlay/POE2Radar.Overlay.csproj"
$overlaySrc = Join-Path $root "src/POE2Radar.Overlay/Overlay"

function Get-ProjectVersion {
    param([string]$ProjectPath)
    $value = dotnet msbuild $ProjectPath -getProperty:Version
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Could not read Version from $ProjectPath" }
    return $value.Trim()
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion $project
}
$Version = $Version.TrimStart('v', 'V')

$releaseRoot = Join-Path $root "release"
$bundleName = "POE2Radar-v$Version-win-x64"
$outDir = Join-Path $releaseRoot $bundleName
$zipPath = Join-Path $releaseRoot "$bundleName.zip"
$runtimeBackup = Join-Path $releaseRoot ".$bundleName-runtime-backup"

Write-Host "POE2Radar release build v$Version"
Write-Host "  output: $outDir"

# Preserve only local imgui layout across rebuilds. Shipped config always comes from Config/Defaults.
if (Test-Path (Join-Path $runtimeBackup "imgui.ini")) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    Copy-Item (Join-Path $runtimeBackup "imgui.ini") (Join-Path $outDir "imgui.ini") -Force
}
if (Test-Path $runtimeBackup) {
    Remove-Item $runtimeBackup -Recurse -Force
}
if (Test-Path (Join-Path $outDir "imgui.ini")) {
    New-Item -ItemType Directory -Path $runtimeBackup -Force | Out-Null
    Copy-Item (Join-Path $outDir "imgui.ini") (Join-Path $runtimeBackup "imgui.ini") -Force
}

if (Test-Path $outDir) {
    Remove-Item $outDir -Recurse -Force
}
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "Publishing self-contained overlay..."
dotnet publish $project `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -p:DebugType=none -p:DebugSymbols=false `
    -p:Deterministic=true -p:ContinuousIntegrationBuild=true `
    -o $outDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Remove-Item (Join-Path $outDir "*.pdb") -Force -ErrorAction SilentlyContinue

$releaseExeName = "AppHost.exe"

function Find-BinaryPatternCount {
    param(
        [byte[]]$Bytes,
        [byte[]]$Needle
    )
    if ($Needle.Length -eq 0) { return 0 }
    $count = 0
    for ($i = 0; $i -le $Bytes.Length - $Needle.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $Needle.Length; $j++) {
            if ($Bytes[$i + $j] -ne $Needle[$j]) { $match = $false; break }
        }
        if ($match) {
            $count++
            $i += $Needle.Length - 1
        }
    }
    return $count
}

function Replace-BinaryPattern {
    param(
        [byte[]]$Bytes,
        [byte[]]$Needle,
        [byte[]]$Replacement
    )
    if ($Needle.Length -ne $Replacement.Length) {
        throw "Scrub replacement length mismatch ($($Needle.Length) vs $($Replacement.Length))"
    }
    if ($Needle.Length -eq 0) { return 0 }
    $count = 0
    for ($i = 0; $i -le $Bytes.Length - $Needle.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $Needle.Length; $j++) {
            if ($Bytes[$i + $j] -ne $Needle[$j]) { $match = $false; break }
        }
        if (-not $match) { continue }
        for ($j = 0; $j -lt $Replacement.Length; $j++) {
            $Bytes[$i + $j] = $Replacement[$j]
        }
        $count++
        $i += $Needle.Length - 1
    }
    return $count
}

function Scrub-ReleaseBinary {
    param([string]$ExePath)
    if (-not (Test-Path $ExePath)) { throw "Scrub target missing: $ExePath" }

    $bytes = [System.IO.File]::ReadAllBytes($ExePath)
    $utf8 = [System.Text.Encoding]::UTF8
    $utf16 = [System.Text.Encoding]::Unicode

    # Same-length fillers only (PE integrity).
    $pairs = @(
        @{ Needle = "POE2Radar"; Replacement = "HostApp00" },
        @{ Needle = "poe2radar"; Replacement = "hostapp00" }
    )

    $total = 0
    foreach ($pair in $pairs) {
        $n = $pair.Needle
        $r = $pair.Replacement
        if ($n.Length -ne $r.Length) {
            throw "Scrub pair length mismatch for '$n' / '$r'"
        }
        $total += Replace-BinaryPattern -Bytes $bytes -Needle ($utf8.GetBytes($n)) -Replacement ($utf8.GetBytes($r))
        $total += Replace-BinaryPattern -Bytes $bytes -Needle ($utf16.GetBytes($n)) -Replacement ($utf16.GetBytes($r))
    }

    [System.IO.File]::WriteAllBytes($ExePath, $bytes)
    Write-Host "  string scrub: $total replacements in $(Split-Path $ExePath -Leaf)"

    $verify = [System.IO.File]::ReadAllBytes($ExePath)
    $remaining = (Find-BinaryPatternCount -Bytes $verify -Needle ($utf8.GetBytes("POE2Radar")))
    $remaining += (Find-BinaryPatternCount -Bytes $verify -Needle ($utf16.GetBytes("POE2Radar")))
    if ($remaining -gt 0) {
        throw "String scrub verification failed: POE2Radar still present ($remaining hits)"
    }
}

function Stage-OverlayAssets {
    param([string]$TargetDir)
    $overlayOut = Join-Path $TargetDir "Overlay"
    $texturesOut = Join-Path $overlayOut "Textures"
    New-Item -ItemType Directory -Path $texturesOut -Force | Out-Null

    Copy-Item (Join-Path $overlaySrc "icons.png") (Join-Path $overlayOut "icons.png") -Force
    Copy-Item (Join-Path $overlaySrc "Textures\*.png") $texturesOut -Force
}

function Stage-DefaultConfig {
    param([string]$TargetDir)
    $defaultsDir = Join-Path $root "src/POE2Radar.Overlay/Config/Defaults"
    if (-not (Test-Path $defaultsDir)) {
        throw "Missing shipped defaults folder: $defaultsDir"
    }

    $configOut = Join-Path $TargetDir "config"
    New-Item -ItemType Directory -Path $configOut -Force | Out-Null

    $requiredDefaults = @(
        "radar_settings.json",
        "display_rules.json",
        "watched_entities.json",
        "hidden_entities.json",
        "zone_entity_overrides.json"
    )
    foreach ($name in $requiredDefaults) {
        $from = Join-Path $defaultsDir $name
        if (-not (Test-Path $from)) {
            throw "Missing shipped default config: $from"
        }
        Copy-Item $from (Join-Path $configOut $name) -Force
    }
}

function Test-ReleaseLayout {
    param([string]$TargetDir)
    $required = @(
        (Join-Path $TargetDir $releaseExeName),
        (Join-Path $TargetDir "Overlay\icons.png"),
        (Join-Path $TargetDir "Overlay\Textures\full_bar.png"),
        (Join-Path $TargetDir "Overlay\Textures\hollow_bar.png"),
        (Join-Path $TargetDir "icons\Circle.svg"),
        (Join-Path $TargetDir "config\radar_settings.json"),
        (Join-Path $TargetDir "config\display_rules.json"),
        (Join-Path $TargetDir "config\watched_entities.json"),
        (Join-Path $TargetDir "README.md"),
        (Join-Path $TargetDir "LICENSE"),
        (Join-Path $TargetDir "VERSION.txt")
    )
    foreach ($path in $required) {
        if (-not (Test-Path $path)) {
            throw "Release verification failed - missing: $path"
        }
    }
}

function Wait-ForReleaseAsset {
    param(
        [string]$Path,
        [int]$TimeoutMs = 3000
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    while (-not (Test-Path $Path)) {
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 50
    }
    return $true
}

Write-Host "Staging overlay textures and sprite atlas..."
Stage-OverlayAssets $outDir

Write-Host "Materializing built-in SVG icon library..."
$exe = Join-Path $outDir $releaseExeName
& $exe --export-release-assets $outDir
if ($LASTEXITCODE -ne 0) { throw "Icon export failed with exit code $LASTEXITCODE" }
$circleIcon = Join-Path $outDir "icons\Circle.svg"
if (-not (Wait-ForReleaseAsset $circleIcon)) {
    throw "Icon export finished but did not create: $circleIcon"
}

Write-Host "Scrubbing release binary strings..."
Scrub-ReleaseBinary $exe

Write-Host "Bundling shipped default config..."
Stage-DefaultConfig $outDir

Copy-Item (Join-Path $root "README.md"), (Join-Path $root "LICENSE") $outDir -Force
@"
POE2Radar $Version
Windows x64 self-contained build

Layout:
  $releaseExeName           - run as Administrator with PoE2 already open
  Overlay/icons.png       - entity sprite atlas
  Overlay/Textures/       - HP/ES bar textures
  icons/                  - editable SVG shape library
  config/                 - shipped defaults (settings, display rules, watched/hidden entities)
  cache/                  - created at runtime (terrain bitmap cache)
  logs/                   - crash log

Built: $([DateTime]::UtcNow.ToString("yyyy-MM-dd HH:mm:ss")) UTC
"@ | Set-Content -Path (Join-Path $outDir "VERSION.txt") -Encoding UTF8

Test-ReleaseLayout $outDir

if (-not $SkipZip) {
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath -Force
    Write-Host "Zip: $zipPath"
}

# Keep local imgui layout across rebuilds; never overlay personal config onto shipped defaults.
if (Test-Path (Join-Path $runtimeBackup "imgui.ini")) {
    Copy-Item (Join-Path $runtimeBackup "imgui.ini") (Join-Path $outDir "imgui.ini") -Force
}
if (Test-Path $runtimeBackup) {
    Remove-Item $runtimeBackup -Recurse -Force
}

Write-Host "Release ready: $outDir"
Wait-IfNeeded 0
