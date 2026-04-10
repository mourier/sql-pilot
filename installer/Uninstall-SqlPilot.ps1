#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls SQL Pilot extension from all SSMS versions (18, 20, 22).

.PARAMETER Silent
    Run without prompts.

.PARAMETER Versions
    Comma-separated list of SSMS version numbers to remove from (18, 20, 22).
    If omitted, removes from every detected version where SQL Pilot is installed.

.EXAMPLE
    .\Uninstall-SqlPilot.ps1
    .\Uninstall-SqlPilot.ps1 -Silent
    .\Uninstall-SqlPilot.ps1 -Versions 22
#>
[CmdletBinding()]
param(
    [switch]$Silent,
    [string]$Versions
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=== SQL Pilot Uninstaller ===" -ForegroundColor Cyan
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "_SsmsHelpers.ps1")

$ssmsVersions = Get-SqlPilotSsmsCatalog

$installed = $ssmsVersions | Where-Object {
    Test-Path (Join-Path $_.IdePath "Extensions\SqlPilot")
}

if ($installed.Count -eq 0) {
    Write-Host "SQL Pilot is not installed in any supported SSMS version." -ForegroundColor Yellow
    exit 0
}

# Apply -Versions filter if supplied
if ($Versions) {
    $requested = $Versions -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $unknown = $requested | Where-Object {
        $ver = $_
        -not ($installed | Where-Object { $_.Label -match "\b$ver\b" })
    }
    $filtered = $installed | Where-Object {
        $row = $_
        $requested | Where-Object { $row.Label -match "\b$_\b" }
    }
    if ($filtered.Count -eq 0) {
        Write-Host "ERROR: -Versions '$Versions' did not match any installed SSMS." -ForegroundColor Red
        Write-Host "SQL Pilot is currently installed in: $(($installed | ForEach-Object { $_.Label -replace 'SSMS ', '' }) -join ', ')" -ForegroundColor Yellow
        exit 1
    }
    if ($unknown) {
        Write-Host "WARNING: skipping unknown/uninstalled versions: $($unknown -join ', ')" -ForegroundColor Yellow
    }
    $installed = $filtered
}

Write-Host "SQL Pilot is currently installed in:" -ForegroundColor White
$installed | ForEach-Object {
    Write-Host "  $($_.Label)  ($(Join-Path $_.IdePath 'Extensions\SqlPilot'))" -ForegroundColor Gray
}
Write-Host ""

if (-not $Silent) {
    $confirm = Read-Host "Remove from all of the above? (Y/n)"
    if ($confirm -eq 'n') {
        Write-Host "Uninstall cancelled." -ForegroundColor Yellow
        exit 0
    }
}

$failed = @()
foreach ($ssms in $installed) {
    Write-Host ""
    Write-Host "--- Removing from $($ssms.Label) ---" -ForegroundColor Cyan

    # Per-version try/catch: a locked DLL (e.g. SSMS 20 still running) must not
    # abort removal from the other versions.
    try {
        $extensionDir = Join-Path $ssms.IdePath "Extensions\SqlPilot"
        Remove-Item $extensionDir -Recurse -Force
        Write-Host "  Removed: $extensionDir" -ForegroundColor Gray

        Invoke-SsmsCacheInvalidation -IdePath $ssms.IdePath -DataBase $ssms.DataBase -DataPattern $ssms.DataPattern
    } catch {
        Write-Host "  FAILED: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  (Is $($ssms.Label) still running?)" -ForegroundColor Yellow
        $failed += $ssms.Label
    }
}

Write-Host ""
if ($failed.Count -eq 0) {
    Write-Host "SQL Pilot uninstalled successfully!" -ForegroundColor Green
} else {
    Write-Host "SQL Pilot partially uninstalled. Failed: $($failed -join ', ')" -ForegroundColor Yellow
    Write-Host "Close those SSMS instances and re-run the uninstaller." -ForegroundColor Yellow
}
Write-Host "Please restart SSMS for the changes to take effect." -ForegroundColor Yellow
Write-Host ""

# Clean up shared user-data (favorites, recents, settings)
$appDataDir = Join-Path $env:APPDATA "SqlPilot"
if (Test-Path $appDataDir) {
    if (-not $Silent) {
        $cleanData = Read-Host "Remove SQL Pilot settings, favorites, and recents? (y/N)"
        if ($cleanData -eq 'y') {
            Remove-Item $appDataDir -Recurse -Force
            Write-Host "Settings and data removed." -ForegroundColor Gray
        }
    }
}
