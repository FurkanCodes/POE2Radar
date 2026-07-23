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

if (Test-Path (Join-Path $runtimeBackup "config")) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    Copy-Item (Join-Path $runtimeBackup "config") (Join-Path $outDir "config") -Recurse -Force
}
if (Test-Path (Join-Path $runtimeBackup "imgui.ini")) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    Copy-Item (Join-Path $runtimeBackup "imgui.ini") (Join-Path $outDir "imgui.ini") -Force
}
if (Test-Path $runtimeBackup) {
    Remove-Item $runtimeBackup -Recurse -Force
}
if (Test-Path (Join-Path $outDir "config")) {
    New-Item -ItemType Directory -Path $runtimeBackup -Force | Out-Null
    Copy-Item (Join-Path $outDir "config") (Join-Path $runtimeBackup "config") -Recurse -Force
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

function Stage-OverlayAssets {
    param([string]$TargetDir)
    $overlayOut = Join-Path $TargetDir "Overlay"
    $texturesOut = Join-Path $overlayOut "Textures"
    New-Item -ItemType Directory -Path $texturesOut -Force | Out-Null

    Copy-Item (Join-Path $overlaySrc "icons.png") (Join-Path $overlayOut "icons.png") -Force
    Copy-Item (Join-Path $overlaySrc "Textures\*.png") $texturesOut -Force
}

function Test-ReleaseLayout {
    param([string]$TargetDir)
    $required = @(
        (Join-Path $TargetDir "POE2Radar.Overlay.exe"),
        (Join-Path $TargetDir "Overlay\icons.png"),
        (Join-Path $TargetDir "Overlay\Textures\full_bar.png"),
        (Join-Path $TargetDir "Overlay\Textures\hollow_bar.png"),
        (Join-Path $TargetDir "icons\Circle.svg"),
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
$exe = Join-Path $outDir "POE2Radar.Overlay.exe"
& $exe --export-release-assets $outDir
if ($LASTEXITCODE -ne 0) { throw "Icon export failed with exit code $LASTEXITCODE" }
$circleIcon = Join-Path $outDir "icons\Circle.svg"
if (-not (Wait-ForReleaseAsset $circleIcon)) {
    throw "Icon export finished but did not create: $circleIcon"
}

Copy-Item (Join-Path $root "README.md"), (Join-Path $root "LICENSE") $outDir -Force
@"
POE2Radar $Version
Windows x64 self-contained build

Layout:
  POE2Radar.Overlay.exe   - run as Administrator with PoE2 already open
  Overlay/icons.png       - entity sprite atlas
  Overlay/Textures/     - HP/ES bar textures
  icons/                  - editable SVG shape library
  config/                 - created on first run (settings, rules, watched entities)
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

if (Test-Path (Join-Path $runtimeBackup "config")) {
    Copy-Item (Join-Path $runtimeBackup "config") (Join-Path $outDir "config") -Recurse -Force
}
if (Test-Path (Join-Path $runtimeBackup "imgui.ini")) {
    Copy-Item (Join-Path $runtimeBackup "imgui.ini") (Join-Path $outDir "imgui.ini") -Force
}
if (Test-Path $runtimeBackup) {
    Remove-Item $runtimeBackup -Recurse -Force
}

Write-Host "Release ready: $outDir"
Wait-IfNeeded 0
