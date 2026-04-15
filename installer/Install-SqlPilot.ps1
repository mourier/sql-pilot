#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs SQL Pilot extension for SSMS 18, 20, and 22.

.DESCRIPTION
    Detects every installed version of SSMS (18, 20, 22) and copies the
    appropriate SQL Pilot binaries into each one's global Extensions folder.
    Requires administrator privileges.

.PARAMETER Silent
    Run without prompts (for automated deployment).

.PARAMETER Versions
    Comma-separated list of SSMS version numbers to install into (18, 20, 22).
    If omitted, installs into every detected version.

.EXAMPLE
    .\Install-SqlPilot.ps1
    .\Install-SqlPilot.ps1 -Silent
    .\Install-SqlPilot.ps1 -Versions 22
    .\Install-SqlPilot.ps1 -Silent -Versions 22,20
#>
[CmdletBinding()]
param(
    [switch]$Silent,
    [string]$Versions
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=== SQL Pilot Installer ===" -ForegroundColor Cyan
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "_SsmsHelpers.ps1")

$ssmsVersions = Get-SqlPilotSsmsCatalog

$found = $ssmsVersions | Where-Object { Test-Path $_.IdePath }

if ($found.Count -eq 0) {
    Write-Host "ERROR: No supported SSMS installation found." -ForegroundColor Red
    Write-Host "Expected one of:" -ForegroundColor Yellow
    $ssmsVersions | ForEach-Object { Write-Host "  $($_.IdePath)" -ForegroundColor Gray }
    exit 1
}

# Apply -Versions filter if supplied
if ($Versions) {
    $requested = $Versions -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $unknown = $requested | Where-Object { $_ -notmatch '^\d+$' -or -not ($found | Where-Object { $_.Label -match "$_(\b|$)" }) }
    $filtered = $found | Where-Object {
        $row = $_
        $requested | Where-Object { $row.Label -match "\b$_\b" }
    }
    if ($filtered.Count -eq 0) {
        Write-Host "ERROR: -Versions '$Versions' did not match any detected SSMS." -ForegroundColor Red
        Write-Host "Detected: $(($found | ForEach-Object { $_.Label -replace 'SSMS ', '' }) -join ', ')" -ForegroundColor Yellow
        exit 1
    }
    if ($unknown) {
        Write-Host "WARNING: skipping unknown/undetected versions: $($unknown -join ', ')" -ForegroundColor Yellow
    }
    $found = $filtered
}

Write-Host "Detected SSMS installations:" -ForegroundColor Green
$found | ForEach-Object { Write-Host "  $($_.Label)  ($($_.IdePath))" -ForegroundColor Gray }
Write-Host ""

if (-not $Silent) {
    $confirm = Read-Host "Install SQL Pilot into all of the above? (Y/n)"
    if ($confirm -eq 'n') {
        Write-Host "Installation cancelled." -ForegroundColor Yellow
        exit 0
    }
}

foreach ($ssms in $found) {
    Write-Host ""
    Write-Host "--- Installing for $($ssms.Label) ---" -ForegroundColor Cyan

    $payloadDir = Join-Path $scriptDir $ssms.Subfolder
    if (-not (Test-Path $payloadDir)) {
        Write-Host "  SKIPPED: $($ssms.Subfolder) payload missing from package." -ForegroundColor Yellow
        continue
    }

    $extensionDir = Join-Path $ssms.IdePath "Extensions\SqlPilot"
    New-Item -ItemType Directory -Path $extensionDir -Force | Out-Null

    Get-ChildItem $payloadDir -File | ForEach-Object {
        Copy-Item $_.FullName $extensionDir -Force
        Write-Host "  Copied: $($_.Name)" -ForegroundColor Gray
    }

    # Strip the Mark-of-the-Web NTFS stream from every copied file. If the user
    # downloaded the release ZIP via a browser and extracted it with Explorer,
    # the MotW propagates to the DLLs and SSMS then refuses to load them with
    # "An attempt was made to load an assembly from a network location...".
    Get-ChildItem $extensionDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

    Invoke-SsmsCacheInvalidation -IdePath $ssms.IdePath -DataBase $ssms.DataBase -DataPattern $ssms.DataPattern
    Write-Host "  Invalidated SSMS caches" -ForegroundColor Gray
}

Write-Host ""
Write-Host "SQL Pilot installed successfully!" -ForegroundColor Green
Write-Host "Please restart SSMS for the changes to take effect." -ForegroundColor Yellow
Write-Host ""
