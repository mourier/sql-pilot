#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"

# Stop SSMS if running
$ssmsProcs = Get-Process -Name Ssms -ErrorAction SilentlyContinue
if ($ssmsProcs) {
    Write-Host "Stopping SSMS..." -ForegroundColor Yellow
    $ssmsProcs | Stop-Process -Force
    Start-Sleep -Seconds 3
    Write-Host "  SSMS stopped" -ForegroundColor Gray
}

Write-Host "=== Cleaning spikes ===" -ForegroundColor Yellow

# Remove all spike folders
$spikePaths = @(
    "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\SqlPilotSpike",
    "C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE\Extensions\SqlPilotSpike",
    "C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE\Extensions\SqlPilotSpike"
)
foreach ($p in $spikePaths) {
    if (Test-Path $p) {
        Remove-Item $p -Recurse -Force
        Write-Host "  Removed spike: $p" -ForegroundColor Gray
    }
}

# Also remove any old SqlPilot folder to start fresh
$oldPilot = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\SqlPilot"
if (Test-Path $oldPilot) {
    Remove-Item $oldPilot -Recurse -Force
    Write-Host "  Removed old SqlPilot" -ForegroundColor Gray
}

Write-Host "`n=== Deploying SQL Pilot to SSMS 22 ===" -ForegroundColor Cyan

$repoRoot = "C:\code\sql-pilot"
$buildDir = "$repoRoot\src\SqlPilot.Package\bin\Release\net472"
$pkgDir = "$repoRoot\src\SqlPilot.Package"
$idePath = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE"
$targetDir = Join-Path $idePath "Extensions\SqlPilot"

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

# Copy DLLs — shared source of truth with release.yml
$dlls = Get-Content "$repoRoot\build\SqlPilotDlls.txt" | Where-Object { $_ -and -not $_.StartsWith('#') }
foreach ($dll in $dlls) {
    $src = Join-Path $buildDir $dll
    if (Test-Path $src) { Copy-Item $src $targetDir -Force; Write-Host "  $dll" -ForegroundColor Gray }
}

# Metadata files
Copy-Item "$pkgDir\SqlPilot.Package.pkgdef" $targetDir -Force
Copy-Item "$pkgDir\extension.vsixmanifest.v2" "$targetDir\extension.vsixmanifest" -Force
Copy-Item "$pkgDir\manifest.json" $targetDir -Force
Copy-Item "$pkgDir\catalog.json" $targetDir -Force
Write-Host "  pkgdef + manifests" -ForegroundColor Gray

# Invalidate caches
Write-Host "`nClearing caches..." -ForegroundColor Yellow
$dataDir = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\SSMS" -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^22\.' } | Select-Object -First 1

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

Write-Host "`n=== Done! ===" -ForegroundColor Green
Get-ChildItem $targetDir | Format-Table Name, Length -AutoSize
Write-Host "Press any key to launch SSMS 22..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Start-Process (Join-Path $idePath "Ssms.exe")
