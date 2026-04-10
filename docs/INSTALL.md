# Installing SQL Pilot

## Requirements

- SQL Server Management Studio 18, 20, or 22
- Windows 10/11 (x64 or Arm64 for SSMS 22)
- Administrator privileges (the extension installs to Program Files)

## Recommended: one-click installer

The friendliest path. Single download, double-click, done.

1. Download `SqlPilotInstaller-vX.Y.Z.zip` from [GitHub Releases](https://github.com/mourier/sql-pilot/releases)
2. Extract anywhere (e.g. your Downloads folder)
3. Double-click `SqlPilotInstaller.exe`
4. Approve the UAC prompt — the installer needs admin to write into the Program Files extension folders
5. The installer detects every SSMS version on your machine. Pick which to install into via the checkboxes, then click **Install**
6. The installer downloads the matching release payload from GitHub, verifies it, and copies the extension into each selected SSMS folder
7. Restart SSMS — press `Ctrl+D` to open SQL Pilot

The same exe handles uninstall: re-run it on a machine where SQL Pilot is already installed and an **Uninstall** button appears alongside Install.

> **First run will show "Windows protected your PC"** because the installer isn't code-signed (no EV certificate yet). Click **More info** → **Run anyway**. The installer source is at [`src/SqlPilot.Installer/`](../src/SqlPilot.Installer) — verify before running if you want.

## PowerShell scripts (for CI / scripted deploys)

If you're scripting deployments, running on a server without an interactive desktop, or just prefer the command line:

1. Download `SqlPilot-vX.Y.Z.zip` from [GitHub Releases](https://github.com/mourier/sql-pilot/releases)
2. Extract the ZIP. It contains `SSMS22/` and `SSMS18-20/` subfolders, the install scripts, and `_SsmsHelpers.ps1`.
3. Open an **administrator** PowerShell in the extracted directory
4. Run:
   ```powershell
   powershell -ExecutionPolicy Bypass -File Install-SqlPilot.ps1
   ```

The script auto-detects every SSMS version on your machine and installs into each one. Caches are invalidated automatically.

### Selective install via `-Versions`

Install into only specific SSMS versions:

```powershell
.\Install-SqlPilot.ps1 -Versions 22         # SSMS 22 only
.\Install-SqlPilot.ps1 -Versions 22,20      # SSMS 22 and 20
```

### Silent install

For unattended deployment:

```powershell
.\Install-SqlPilot.ps1 -Silent
.\Install-SqlPilot.ps1 -Silent -Versions 22
```

### Uninstall

```powershell
.\Uninstall-SqlPilot.ps1
.\Uninstall-SqlPilot.ps1 -Versions 22       # remove from SSMS 22 only
```

Per-version errors don't abort the others — if SSMS 20 is locked because it's still running, the uninstaller still cleans up 18 and 22 and tells you which versions to retry.

## Manual install (last resort)

If both the WPF installer and the PowerShell scripts are unavailable for some reason, you can copy the files by hand. The release ZIP contains the exact file set under `SSMS22/` and `SSMS18-20/`.

### SSMS 22

1. Close SSMS 22
2. Copy every file from the ZIP's `SSMS22/` folder to:
   `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\SqlPilot\`
3. Delete the SSMS 22 cache files in `%LocalAppData%\Microsoft\SSMS\22.0_*\`:
   - `privateregistry.bin` (and `.LOG1`, `.LOG2`)
   - The entire `ComponentModelCache\` folder
4. Touch `extensions.configurationchanged` in `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\` (create it if it doesn't exist)
5. Launch SSMS 22

### SSMS 18 / SSMS 20

1. Close SSMS
2. Copy every file from the ZIP's `SSMS18-20/` folder to:
   `C:\Program Files (x86)\Microsoft SQL Server Management Studio {18|20}\Common7\IDE\Extensions\SqlPilot\`
3. Delete the cache files in `%LocalAppData%\Microsoft\SQL Server Management Studio\{18|20}.0_IsoShell\`:
   - `privateregistry.bin` (and `.LOG1`, `.LOG2`)
   - The entire `ComponentModelCache\` folder
4. Touch `extensions.configurationchanged` in `C:\Program Files (x86)\Microsoft SQL Server Management Studio {18|20}\Common7\IDE\Extensions\`
5. Launch SSMS

## Troubleshooting

### Extension doesn't appear after install

- Make sure SSMS was **fully closed** before installing — the installer/script can't replace files that are loaded by a running SSMS
- Verify the cache files were deleted (especially `privateregistry.bin`)
- Launch SSMS with `-log` and search the Activity Log for "SqlPilot" or "error":
  ```
  Ssms.exe -log
  ```
  The log lives at `%AppData%\Microsoft\SSMS\<ver>\ActivityLog.xml`

### "Package did not load correctly" error

- Usually a VS Shell version mismatch — the SSMS 22 build was deployed into SSMS 18/20 or vice versa
- Use the WPF installer or the PowerShell script: both pick the right subfolder per detected version automatically

### Permission denied during install

- The PowerShell script requires **administrator** privileges since it writes to Program Files
- Right-click PowerShell → "Run as Administrator", then run the script
- The WPF installer auto-elevates via UAC and doesn't have this problem
