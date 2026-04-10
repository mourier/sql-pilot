#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Dev deploy script - builds and deploys SQL Pilot to SSMS for testing.
.EXAMPLE
    .\Deploy-Dev.ps1 -Version 22
    .\Deploy-Dev.ps1 -Version 18
#>
param(
    [ValidateSet("22","18","20")]
    [string]$Version = "22"
)

$ErrorActionPreference = "Stop"
$repoRoot = "C:\code\sql-pilot"
$pkgDir = "$repoRoot\src\SqlPilot.Package"

# Stop SSMS
Stop-Process -Name Ssms -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Select build output and paths based on version
switch ($Version) {
    "22" {
        $buildDir = "$repoRoot\src\SqlPilot.Package\bin\Release\net472"
        $idePath = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE"
        $dataBase = "$env:LOCALAPPDATA\Microsoft\SSMS"
        $dataPattern = '^22\.'
        $manifestSrc = "$pkgDir\extension.vsixmanifest.v2"
        $needManifestJson = $true
    }
    default {
        $buildDir = "$repoRoot\src\SqlPilot.Package.Legacy\bin\Release\net472"
        $idePath = "C:\Program Files (x86)\Microsoft SQL Server Management Studio $Version\Common7\IDE"
        $dataBase = "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio"
        $dataPattern = "^$Version\."
        $manifestSrc = "$pkgDir\extension.vsixmanifest.v1"
        $needManifestJson = $false
    }
}

Write-Host "=== SQL Pilot Dev Deploy (SSMS $Version) ===" -ForegroundColor Cyan

$targetDir = Join-Path $idePath "Extensions\SqlPilot"
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

# Core DLLs — shared source of truth with release.yml
$dlls = Get-Content "$repoRoot\build\SqlPilotDlls.txt" | Where-Object { $_ -and -not $_.StartsWith('#') }

foreach ($dll in $dlls) {
    $src = Join-Path $buildDir $dll
    if (Test-Path $src) { Copy-Item $src $targetDir -Force; Write-Host "  $dll" -ForegroundColor Gray }
}

# Metadata
Copy-Item "$pkgDir\SqlPilot.Package.pkgdef" $targetDir -Force
Copy-Item $manifestSrc "$targetDir\extension.vsixmanifest" -Force
if ($needManifestJson) {
    Copy-Item "$pkgDir\manifest.json" $targetDir -Force
    Copy-Item "$pkgDir\catalog.json" $targetDir -Force
}
Write-Host "  pkgdef + manifests" -ForegroundColor Gray

# Invalidate caches
$dataDir = Get-ChildItem $dataBase -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match $dataPattern } | Select-Object -First 1

if ($dataDir) {
    foreach ($f in @("privateregistry.bin", "privateregistry.bin.LOG1", "privateregistry.bin.LOG2")) {
        $path = Join-Path $dataDir.FullName $f
        if (Test-Path $path) { Remove-Item $path -Force -ErrorAction SilentlyContinue }
    }
    $cache = Join-Path $dataDir.FullName "ComponentModelCache"
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue }
    "" | Set-Content (Join-Path $dataDir.FullName "extensions.configurationchanged") -Force -ErrorAction SilentlyContinue
}
"" | Set-Content (Join-Path $idePath "Extensions\extensions.configurationchanged") -Force -ErrorAction SilentlyContinue

Write-Host "`nDeployed to: $targetDir" -ForegroundColor Green
Write-Host "Press any key to launch SSMS $Version..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Start-Process (Join-Path $idePath "Ssms.exe")
