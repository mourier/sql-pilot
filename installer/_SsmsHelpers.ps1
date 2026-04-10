# Helpers dot-sourced by Install-SqlPilot.ps1 and Uninstall-SqlPilot.ps1.
# Both scripts ship in the same release ZIP so dot-sourcing works without
# extra install steps.
#
# Source of truth for the SSMS version table — both Install and Uninstall
# call Get-SqlPilotSsmsCatalog so the rows live in one place. The C# installer
# (src/SqlPilot.Installer/Services/SsmsDetector.cs) maintains its own mirror
# of this table; if you change anything here, mirror it there.

function Get-SqlPilotSsmsCatalog {
    @(
        [pscustomobject]@{
            Label       = "SSMS 22"
            IdePath     = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE"
            Subfolder   = "SSMS22"
            DataBase    = Join-Path $env:LOCALAPPDATA "Microsoft\SSMS"
            DataPattern = '^22\.'
        },
        [pscustomobject]@{
            Label       = "SSMS 20"
            IdePath     = "C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE"
            Subfolder   = "SSMS18-20"
            DataBase    = Join-Path $env:LOCALAPPDATA "Microsoft\SQL Server Management Studio"
            DataPattern = '^20\.'
        },
        [pscustomobject]@{
            Label       = "SSMS 18"
            IdePath     = "C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE"
            Subfolder   = "SSMS18-20"
            DataBase    = Join-Path $env:LOCALAPPDATA "Microsoft\SQL Server Management Studio"
            DataPattern = '^18\.'
        }
    )
}

function Invoke-SsmsCacheInvalidation {
    <#
    .SYNOPSIS
        Forces SSMS to rescan pkgdef/extension metadata on next launch.
    .DESCRIPTION
        Deletes privateregistry.bin (and its WAL-style LOGs) and the
        ComponentModelCache directory, then touches
        extensions.configurationchanged markers in both the user data
        directory and the global Extensions folder.
    .PARAMETER IdePath
        Absolute path to SSMS's Common7\IDE directory.
    .PARAMETER DataBase
        Absolute path to the SSMS user-data base directory
        (e.g. %LOCALAPPDATA%\Microsoft\SSMS for SSMS 22,
         %LOCALAPPDATA%\Microsoft\SQL Server Management Studio for 18/20).
    .PARAMETER DataPattern
        Regex matching the version-specific subdirectory name inside DataBase
        (e.g. '^22\.').
    #>
    param(
        [Parameter(Mandatory)] [string]$IdePath,
        [Parameter(Mandatory)] [string]$DataBase,
        [Parameter(Mandatory)] [string]$DataPattern
    )

    $dataDir = Get-ChildItem $DataBase -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $DataPattern } |
        Select-Object -First 1

    if ($dataDir) {
        foreach ($f in @("privateregistry.bin", "privateregistry.bin.LOG1", "privateregistry.bin.LOG2")) {
            $path = Join-Path $dataDir.FullName $f
            if (Test-Path $path) {
                Remove-Item $path -Force -ErrorAction SilentlyContinue
            }
        }
        $cacheDir = Join-Path $dataDir.FullName "ComponentModelCache"
        if (Test-Path $cacheDir) {
            Remove-Item $cacheDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        "" | Set-Content (Join-Path $dataDir.FullName "extensions.configurationchanged") -Force -ErrorAction SilentlyContinue
    }

    "" | Set-Content (Join-Path $IdePath "Extensions\extensions.configurationchanged") -Force
}
