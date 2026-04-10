$targetDir = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\SqlPilotSpike"
$sourceDir = "C:\code\sql-pilot\spike"
$ssmsDataDir = "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_31545408"

Write-Host "=== SQL Pilot Spike Deployer ===" -ForegroundColor Cyan

# 1. Deploy extension files
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Copy-Item "$sourceDir\bin\Release\net472\SqlPilot.Spike.dll" $targetDir -Force
Copy-Item "$sourceDir\bin\Release\net472\SqlPilot.Spike.pkgdef" $targetDir -Force
Copy-Item "$sourceDir\extension.vsixmanifest.installed" "$targetDir\extension.vsixmanifest" -Force
Copy-Item "$sourceDir\manifest.json" $targetDir -Force
Copy-Item "$sourceDir\catalog.json" $targetDir -Force

Write-Host "`nDeployed to: $targetDir" -ForegroundColor Green
Get-ChildItem $targetDir | Format-Table Name, Length

# 2. Invalidate caches
Write-Host "Invalidating caches..." -ForegroundColor Yellow

# Delete private registry hive to force full pkgdef rescan
$regFiles = @("privateregistry.bin", "privateregistry.bin.LOG1", "privateregistry.bin.LOG2")
foreach ($f in $regFiles) {
    $path = Join-Path $ssmsDataDir $f
    if (Test-Path $path) {
        Remove-Item $path -Force -ErrorAction SilentlyContinue
        Write-Host "  Deleted: $f" -ForegroundColor Gray
    }
}

# Clear ComponentModelCache
$cacheDir = Join-Path $ssmsDataDir "ComponentModelCache"
if (Test-Path $cacheDir) {
    Remove-Item $cacheDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  Cleared: ComponentModelCache" -ForegroundColor Gray
}

# Touch extensions.configurationchanged in both locations
"" | Set-Content (Join-Path $ssmsDataDir "extensions.configurationchanged") -Force
"" | Set-Content (Join-Path $ssmsDataDir "Extensions\extensions.configurationchanged") -Force

$globalExtDir = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions"
"" | Set-Content (Join-Path $globalExtDir "extensions.configurationchanged") -Force
Write-Host "  Touched: extensions.configurationchanged" -ForegroundColor Gray

Write-Host "`nDone! Restart SSMS to test." -ForegroundColor Green
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
