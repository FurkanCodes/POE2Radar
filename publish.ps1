# Back-compat wrapper — use release.ps1 for distributable builds.
param([string]$Version = "dev")
& "$PSScriptRoot/release.ps1" -Version $Version
