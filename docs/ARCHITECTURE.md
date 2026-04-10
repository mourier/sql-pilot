# SQL Pilot Architecture

> **Also read [SSMS_INTEGRATION_NOTES.md](SSMS_INTEGRATION_NOTES.md)** — contains hard-won knowledge about SSMS internal APIs, gotchas, and things that don't work. Read before touching the Package/Integration layer.

## Overview

SQL Pilot is a Quick Search extension for SQL Server Management Studio (SSMS). It provides fuzzy search across database objects (tables, views, stored procedures, functions) with keyboard-driven navigation, favorites, and context actions.

## Supported SSMS Versions

| Version | Architecture | VS Shell | VSSDK Build | Extension Path |
|---------|-------------|----------|-------------|----------------|
| SSMS 18 | 32-bit (x86) | 15.0.0.0 | 15.x | `C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE\Extensions\` |
| SSMS 20 | 32-bit (x86) | 15.0.0.0 | 15.x | `C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE\Extensions\` |
| SSMS 22 | 64-bit (x64/Arm64) | 17.0.0.0 | 17.x | `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\` |

### Key Differences

- **SSMS 18/20** use the VS 2017 Isolated Shell and require VSSDK 15.x packages. They are 32-bit.
- **SSMS 22** uses the VS 2026 shell (version 18.x internally) and requires VSSDK 17.x packages. It is 64-bit and Arm64 native.
- **Same source code** works across all versions — only the VSSDK references differ.
- The DLL must be built as **AnyCPU** to work on both 32-bit and 64-bit SSMS.

### SMO Versions by SSMS

| SSMS | SMO Version |
|------|-------------|
| 18   | 16.100.0.0  |
| 20   | 17.100.0.0  |
| 22   | 18.100.0.0  |

SMO is loaded from SSMS at runtime (not bundled). Set `<Private>false</Private>` on SMO references.

## Project Structure

```
SqlPilot/
├── src/
│   ├── SqlPilot.Core/           # Search engine, models, favorites, recents, settings
│   │                            # Target: netstandard2.0 + net472 (no SSMS deps)
│   ├── SqlPilot.Smo/            # SMO database object provider (SSMS 22 SMO 18.x)
│   │                            # Target: net472
│   ├── SqlPilot.Smo.Legacy/     # Same source, references SSMS 18 SMO 16.x
│   │                            # Target: net472
│   ├── SqlPilot.UI/             # WPF controls, view models, themes
│   │                            # Target: net472 + WPF
│   ├── SqlPilot.UI.Demo/        # Standalone WPF app for UI development
│   │                            # Target: net472 + WPF
│   ├── SqlPilot.Package/        # SSMS 22 extension (VSSDK 17.x)
│   │                            # Target: net472 + VSSDK
│   ├── SqlPilot.Package.Legacy/ # SSMS 18/20 extension (VSSDK 15.x)
│   │                            # Target: net472 + VSSDK 15.x
│   └── SqlPilot.Installer/      # Standalone WPF one-click installer (downloads from GitHub Releases)
│                                # Target: net472 + WPF
├── tests/
│   └── SqlPilot.Core.Tests/     # xUnit tests for core logic
├── spike/                       # Phase 0 spike (SSMS 22, VSSDK 17.x)
├── spike-legacy/                # Phase 0 spike (SSMS 18/20, VSSDK 15.x)
├── lib/Ssms18/                  # Compile-time refs against SSMS 18 SMO/SqlWorkbench DLLs
├── lib/Ssms22/                  # Compile-time refs against SSMS 22 SMO/SqlWorkbench DLLs
├── build/                       # Deploy-Dev.ps1, Clean-And-Deploy.ps1, SqlPilotDlls.txt
├── installer/                   # PowerShell install/uninstall + _SsmsHelpers.ps1 (catalog + cache helpers)
└── .github/workflows/           # CI/CD pipelines
```

## Build Strategy

Two extension builds from the same source code, plus a standalone installer:

1. **Modern build** (SSMS 22): VSSDK 17.x, targets VS Shell 17.0.0.0
2. **Legacy build** (SSMS 18/20): VSSDK 15.x, targets VS Shell 15.0.0.0
3. **WPF installer** (`SqlPilot.Installer`): standalone net472 exe, no VSSDK, no extension reference. Builds independently.

Core projects shared by both extension builds: `SqlPilot.Core`, `SqlPilot.UI`. The SMO and Package projects each have a Modern and Legacy variant — the Legacy `.csproj` uses `EnableDefaultCompileItems=false` and `<Compile Include Link>` to pull in every file from the Modern project, so source stays in one place. **If you add a new file to `SqlPilot.Package` or `SqlPilot.Smo`, you must also add a `<Compile Include Link>` entry to the corresponding Legacy `.csproj`** or the Legacy build will be missing it. CI builds catch this immediately.

### Legacy projects must be in SqlPilot.sln

`release.yml` builds via `msbuild SqlPilot.sln`, which only compiles projects listed in the solution. `SqlPilot.Package.Legacy` and `SqlPilot.Smo.Legacy` must be explicitly added — they were initially missing and the first test release shipped an empty `SSMS18-20/` subfolder as a result. `build/Deploy-Dev.ps1` sidesteps this by invoking individual `.csproj` files by path, so local dev wouldn't notice the omission.

## SSMS Integration Points

All verified working via Phase 0 spike on SSMS 18, 20, and 22:

### AsyncPackage (Entry Point)

```csharp
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideToolWindow(typeof(SqlPilotToolWindow))]
```

The package loads automatically when SSMS starts via `ProvideAutoLoad`.

### IObjectExplorerService

Accessed via service provider. The type is discovered at runtime via `AppDomain.CurrentDomain.GetAssemblies()` since it's in `SqlWorkbench.Interfaces.dll` which is loaded by SSMS.

```csharp
Type oeServiceType = null;
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
{
    oeServiceType = assembly.GetType(
        "Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.IObjectExplorerService");
    if (oeServiceType != null) break;
}
var oeService = serviceProvider.GetService(oeServiceType);
```

### Tree Property (Object Explorer TreeView)

The `Tree` property on `IObjectExplorerService` is **internal** and must be accessed via reflection:

```csharp
var treeProp = oeService.GetType().GetProperty("Tree",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
var treeView = treeProp?.GetValue(oeService) as TreeView;
```

Confirmed working on all three SSMS versions.

### DTE2 (IDE Automation)

Available via `GetService(typeof(EnvDTE.DTE))`. Used for opening new query windows and inserting script text.

### SMO (SQL Server Management Objects)

Loaded from SSMS at runtime. Use `SetDefaultInitFields` to only load Name + Schema for performance.

## Extension Deployment

### SSMS 22 — Required Files

Extensions must be deployed to the **global** `Program Files` path. The per-user `%LocalAppData%` path is NOT scanned for pkgdef files.

Each extension folder needs **5 files**:

| File | Purpose |
|------|---------|
| `SqlPilot.dll` | The extension assembly |
| `SqlPilot.pkgdef` | VS package registration (auto-generated by VSSDK build) |
| `extension.vsixmanifest` | VSIX v2 manifest with **resolved** asset paths (not MSBuild tokens) |
| `manifest.json` | File inventory listing all files in the extension |
| `catalog.json` | VSIX catalog metadata |

**Critical**: The `extension.vsixmanifest` must have resolved Asset paths. During development, the `.csproj` generates paths like `|ProjectName;PkgdefProjectOutputGroup|` — these are MSBuild substitution tokens that only resolve during VSIX packaging. For manual deployment, replace with the actual filename: `Path="SqlPilot.pkgdef"`.

### SSMS 18/20 — Required Files

Simpler structure, only **3 files** needed:

| File | Purpose |
|------|---------|
| `SqlPilot.dll` | The extension assembly |
| `SqlPilot.pkgdef` | VS package registration |
| `extension.vsixmanifest` | VSIX **v1** manifest (2010 schema with `IsolatedShell` target) |

### VSIX Manifest Formats

**SSMS 22 (v2 schema)**:
```xml
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Id="SqlPilot.xxx" Version="1.0.0" Language="en-US" Publisher="..." />
    <DisplayName>SQL Pilot</DisplayName>
  </Metadata>
  <Installation>
    <InstallationTarget Version="[21.0,23.0)" Id="Microsoft.VisualStudio.Ssms">
      <ProductArchitecture>amd64</ProductArchitecture>
    </InstallationTarget>
  </Installation>
  <Assets>
    <Asset Type="Microsoft.VisualStudio.VsPackage" Path="SqlPilot.pkgdef" />
  </Assets>
</PackageManifest>
```

**SSMS 18/20 (v1 schema)**:
```xml
<Vsix Version="1.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2010">
  <Identifier Id="SqlPilot.xxx">
    <Name>SQL Pilot</Name>
    <Version>1.0.0</Version>
    <SupportedProducts>
      <IsolatedShell Version="1.0">ssms</IsolatedShell>
    </SupportedProducts>
  </Identifier>
  <Content>
    <VsPackage>SqlPilot.pkgdef</VsPackage>
  </Content>
</Vsix>
```

### Cache Invalidation

After deploying or updating extension files, SSMS caches must be invalidated or the extension won't be discovered:

1. **Delete private registry hive** (forces full pkgdef rescan):
   - `%LocalAppData%\Microsoft\SSMS\22.0_xxx\privateregistry.bin` (+ .LOG1, .LOG2) for SSMS 22
   - `%LocalAppData%\Microsoft\SQL Server Management Studio\18.0_IsoShell\privateregistry.bin` for SSMS 18
   - `%LocalAppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\privateregistry.bin` for SSMS 20

2. **Delete ComponentModelCache** (forces MEF recomposition):
   - Same parent directory as above, `ComponentModelCache\` subfolder

3. **Touch `extensions.configurationchanged`** in the Extensions directory:
   - Signals SSMS to rescan extension folders on next launch

All three steps are needed for reliable deployment. Just touching the marker file alone is NOT sufficient if the registry hive has a cached "timestamps are current" state.

### PkgDef Search Paths

SSMS only scans specific directories for `.pkgdef` files. Discovered from Activity Log:

**SSMS 22**:
```
C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions
C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\CommonExtensions
C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\Application
```

**SSMS 18/20**:
```
C:\Program Files (x86)\Microsoft SQL Server Management Studio {18|20}\Common7\IDE\Extensions
C:\Program Files (x86)\Microsoft SQL Server Management Studio {18|20}\Common7\IDE\CommonExtensions
C:\Program Files (x86)\Microsoft SQL Server Management Studio {18|20}\Common7\IDE\Extensions\Application
```

**Note**: The per-user `%LocalAppData%` Extensions path is NOT in the default search path. Extensions installed via `VSIXInstaller.exe` may register additional paths, but for manual deployment, use the global `Program Files` path.

## Release Pipeline

A tag push matching `v*` triggers `.github/workflows/release.yml`, which builds Modern + Legacy + the WPF installer in one `msbuild SqlPilot.sln` step and publishes **two release assets**:

- **`SqlPilot-vX.Y.Z.zip`** — the payload. Contains `SSMS22/` and `SSMS18-20/` subfolders with the extension DLLs + manifests + pkgdef, plus `Install-SqlPilot.ps1`, `Uninstall-SqlPilot.ps1`, `_SsmsHelpers.ps1`, LICENSE, NOTICE, README at the ZIP root. This is the file the WPF installer downloads at runtime, and what power users extract for scripted installs.
- **`SqlPilotInstaller-vX.Y.Z.zip`** — the friendly user-facing installer. Contains `SqlPilotInstaller.exe` + its dependency DLLs. Users download this, extract, double-click the exe, approve UAC, and the installer fetches the matching payload ZIP from GitHub itself.

The installer pins to its own assembly version: it asks GitHub for `releases/tags/v{Major.Minor.Build}`, not `/releases/latest`. This keeps a `v1.0.0` installer from accidentally pulling a `v2.0.0` payload.

### Version substitution

The version string `1.0.0` is hardcoded in **seven** files because we don't build a proper VSIX (no VSSDK packaging step — `dotnet build` doesn't produce one). The release workflow regex-patches all seven before calling `msbuild`:

| File | What to patch | Schema-version gotcha |
|------|--------------|------------------------|
| `source.extension.vsixmanifest` | `<Identity Version="...">` attribute | Document also has `PackageManifest Version="2.0.0"` schema — don't patch that |
| `extension.vsixmanifest.v2` | `<Identity Version="...">` attribute | Same — `PackageManifest Version="2.0.0"` is the schema |
| `extension.vsixmanifest.v1` | `<Version>...</Version>` element | **NOT** the `<Vsix Version="1.0.0">` schema attribute, which happens to also be `1.0.0` |
| `manifest.json` | `"version": "..."` | (no collision) |
| `catalog.json` | `version=...` AND `"version":"..."` | Two occurrences, both are the extension version |
| `SqlPilot.Package.pkgdef` | `"ProductDetails"="..."` | (no collision) |
| `SqlPilotPackage.cs` | `[InstalledProductRegistration(..., "...")]` attribute | (no collision) |

The v1 and v2 manifests each contain *two* `1.0.0` strings — the schema version and the extension version. Only the extension version should be patched. `release.yml` uses targeted regexes anchored on surrounding syntax (`<Identity ... Version="X">`, `<Version>X</Version>`) to avoid touching the schema versions. The distinction is invisible while the extension is at `1.0.0` but becomes load-bearing the moment you tag anything else.

### Numeric version vs display version

Tags can have semver pre-release suffixes (`v0.0.1-test`, `v1.2.0-beta`). But:

- **MSBuild `/p:Version`** defaults `AssemblyVersion` to `$(VersionPrefix).0`, which must be strictly numeric — a suffix breaks the build
- **VSIX manifest Identity Version** is parsed as a strict `System.Version`
- **pkgdef `ProductDetails`** is free-form text but should match the assembly version for consistency

So the Compute-version step emits two outputs:

```
version         = 1.2.0-beta    # drives the GitHub release title + ZIP filename
numericVersion  = 1.2.0         # used for /p:Version and for all patched files
```

The tag itself (via `github.ref_name`) drives the release name. The internal version is always strict numeric. `AssemblyVersion` ends up as `1.2.0.0`, which is what `UpdateChecker` reads via `Assembly.GetExecutingAssembly().GetName().Version` when comparing against GitHub's `tag_name`.

### DLL whitelist

`build/SqlPilotDlls.txt` is the single source of truth for which DLLs get copied into the release ZIP. The same file is `Get-Content`'d by `release.yml`, `build/Deploy-Dev.ps1`, and `build/Clean-And-Deploy.ps1`. Missing files are silently skipped via `Test-Path`, so the same list works for both the Modern and Legacy bin directories (the Legacy build produces fewer DLLs because it doesn't reference `System.Text.Json` et al).

### Pre-release tags and UpdateChecker

Tags matching `v*-*` are auto-marked `prerelease: true` on the GitHub release via `prerelease: ${{ contains(github.ref, '-') }}`. `UpdateChecker` queries `/releases/latest` which excludes prereleases, so test tags like `v0.0.1-test` won't notify real users. Separately, `System.Version.TryParse` returns false for strings with pre-release suffixes, so even if a prerelease leaked through the filter, the version comparison would silently skip it. This is intentional — it keeps pre-releases out of the auto-update flow.

### Installer dot-sources a shared helper

`installer/Install-SqlPilot.ps1` and `installer/Uninstall-SqlPilot.ps1` both dot-source `installer/_SsmsHelpers.ps1`, which exports two things:
- `Get-SqlPilotSsmsCatalog` — the SSMS version table (Label / IdePath / Subfolder / DataBase / DataPattern). Single source of truth for the PowerShell side.
- `Invoke-SsmsCacheInvalidation` — the cache-invalidation routine.

`release.yml` copies all three files to the package root so they ship together and the dot-source path resolves. If you add a new installer script that needs the same helpers, add it to the release.yml copy list as well.

The C# installer (`SqlPilot.Installer/Services/SsmsDetector.cs`) maintains its own mirror of the catalog as a static array — the same three rows in C# syntax. PowerShell and C# can't share a single data file without adding a JSON parser to either side, so the cross-language duplication is accepted with comments on both sides flagging the sync requirement.

### The WPF installer (SqlPilot.Installer)

`src/SqlPilot.Installer/` is a standalone WPF .exe (`SqlPilotInstaller.exe`). It does NOT load into SSMS — it's a friendly download-and-install UI that runs as its own process under UAC.

Architecture:
- `Services/SsmsDetector.cs` — finds installed SSMS versions, parses pkgdef ProductDetails to detect already-installed SQL Pilot versions
- `Services/GitHubReleaseClient.cs` — talks to the GitHub Releases API, streams the payload ZIP with progress, optional SHA-256 verification (regex JSON extraction — no `System.Text.Json`)
- `Services/InstallEngine.cs` — orchestrates download → verify → extract → copy → invalidate caches. Cache invalidation is a 1:1 port of `Invoke-SsmsCacheInvalidation` from `_SsmsHelpers.ps1`. The local-CPU steps run inside `Task.Run` so they don't block the WPF dispatcher.
- `Services/ProcessHelper.cs` — detects/closes running SSMS instances. Manually disposes the `Process` handles it enumerates (the `using` pattern).
- `ViewModels/MainViewModel.cs` — three-state state machine (Selection / Progress / Done|Error)
- `MainWindow.xaml` — single window with three swappable views, branded with `.github/logo.png`
- `Properties/app.manifest` — `requestedExecutionLevel="requireAdministrator"` so UAC fires up front, plus `PerMonitorV2` DPI awareness

The exe is small (~160 KB) because the source logo PNG is downscaled to 256×256 before being embedded as both a WPF Resource and the `.exe` app icon. **If you re-add a larger source PNG to `.github/logo.png`, the exe size will balloon** — both the Resource and the ICO embed the raw bytes verbatim.

## Spike Results

Phase 0 spike validated on 2026-04-10:

| | SSMS 18 | SSMS 20 | SSMS 22 |
|---|---------|---------|---------|
| Package loads | Yes | Yes | Yes |
| DTE2 | 2019.0150 | 20.2 | 18.0 |
| IObjectExplorerService | Found | Found | Found |
| Tree property (reflection) | Found | Found | Found |
| SMO loads | 16.100.0.0 | 17.100.0.0 | 18.100.0.0 |
| Architecture | 32-bit | 32-bit | 64-bit |
| CLR | 4.0.30319 | 4.0.30319 | 4.0.30319 |
