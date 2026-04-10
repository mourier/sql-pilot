#Requires -RunAsAdministrator
param(
    [ValidateSet("18","20","22","all")]
    [string]$Version = "all"
)

$ErrorActionPreference = "Stop"
$sourceDir = "C:\code\sql-pilot\spike"

$legacyDir = "C:\code\sql-pilot\spike-legacy"

function Deploy-Spike {
    param($Name, $IdePath, $DataDir, $UseV1Manifest, $NeedManifestJson, $UseLegacyBuild)

    Write-Host "`n=== Deploying to $Name ===" -ForegroundColor Cyan

    if (-not (Test-Path $IdePath)) {
        Write-Host "  SKIP: $Name not installed at $IdePath" -ForegroundColor Yellow
        return
    }

    $targetDir = Join-Path $IdePath "Extensions\SqlPilotSpike"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

    # Use legacy build (VS Shell 15.x) for SSMS 18/20, modern build (17.x) for SSMS 22
    $buildDir = if ($UseLegacyBuild) { "$legacyDir\bin\Release\net472" } else { "$sourceDir\bin\Release\net472" }
    Copy-Item "$buildDir\SqlPilot.Spike.dll" $targetDir -Force
    Copy-Item "$sourceDir\bin\Release\net472\SqlPilot.Spike.pkgdef" $targetDir -Force

    # Copy the correct manifest format
    if ($UseV1Manifest) {
        Copy-Item "$sourceDir\extension.vsixmanifest.v1" "$targetDir\extension.vsixmanifest" -Force
    } else {
        Copy-Item "$sourceDir\extension.vsixmanifest.installed" "$targetDir\extension.vsixmanifest" -Force
    }

    # SSMS 22 needs manifest.json and catalog.json
    if ($NeedManifestJson) {
        Copy-Item "$sourceDir\manifest.json" $targetDir -Force
        Copy-Item "$sourceDir\catalog.json" $targetDir -Force
    }

    Write-Host "  Deployed to: $targetDir" -ForegroundColor Green
    Get-ChildItem $targetDir | ForEach-Object { Write-Host "    $($_.Name)" -ForegroundColor Gray }

    # Invalidate caches
    if ($DataDir -and (Test-Path $DataDir)) {
        foreach ($f in @("privateregistry.bin", "privateregistry.bin.LOG1", "privateregistry.bin.LOG2")) {
            $path = Join-Path $DataDir $f
            if (Test-Path $path) { Remove-Item $path -Force -ErrorAction SilentlyContinue }
        }
        $cache = Join-Path $DataDir "ComponentModelCache"
        if (Test-Path $cache) { Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue }
        "" | Set-Content (Join-Path $DataDir "extensions.configurationchanged") -Force -ErrorAction SilentlyContinue
        Write-Host "  Caches cleared" -ForegroundColor Gray
    }

    "" | Set-Content (Join-Path $IdePath "Extensions\extensions.configurationchanged") -Force -ErrorAction SilentlyContinue
}

Write-Host "=== SQL Pilot Spike Multi-Version Deployer ===" -ForegroundColor Cyan

$ssmsLocalData = "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio"
$ssmsLocalData22 = "$env:LOCALAPPDATA\Microsoft\SSMS"

if ($Version -eq "18" -or $Version -eq "all") {
    $dataDir18 = Get-ChildItem $ssmsLocalData -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^18\.' } | Select-Object -First 1
    Deploy-Spike -Name "SSMS 18" `
        -IdePath "C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE" `
        -DataDir ($dataDir18.FullName) `
        -UseV1Manifest $true `
        -NeedManifestJson $false `
        -UseLegacyBuild $true
}

if ($Version -eq "20" -or $Version -eq "all") {
    $dataDir20 = Get-ChildItem $ssmsLocalData -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^20\.' } | Select-Object -First 1
    Deploy-Spike -Name "SSMS 20" `
        -IdePath "C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE" `
        -DataDir ($dataDir20.FullName) `
        -UseV1Manifest $true `
        -NeedManifestJson $false `
        -UseLegacyBuild $true
}

if ($Version -eq "22" -or $Version -eq "all") {
    $dataDir22 = Get-ChildItem $ssmsLocalData22 -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^22\.' } | Select-Object -First 1
    Deploy-Spike -Name "SSMS 22" `
        -IdePath "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE" `
        -DataDir ($dataDir22.FullName) `
        -UseV1Manifest $false `
        -NeedManifestJson $true `
        -UseLegacyBuild $false
}

Write-Host "`n=== Done! Restart SSMS to test. ===" -ForegroundColor Green
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
