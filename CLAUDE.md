# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read these first

Before touching code in `src/SqlPilot.Package*` or `src/SqlPilot.Smo*`, read both:

- **`docs/SSMS_INTEGRATION_NOTES.md`** — hard-won knowledge about SSMS internal APIs, the OE tree, ScriptFactory, the Edit-Top-N-Rows path, Azure SQL constraints, the SSMS 20 connection-string parser bug, and a "Things We Tried That Don't Work" section. Most non-obvious bugs in the integration layer have already been hit and documented here.
- **`docs/ARCHITECTURE.md`** — project structure, dual-build strategy, deployment file requirements per SSMS version, cache invalidation, and the release pipeline (version substitution gotchas).

These docs encode load-bearing knowledge that isn't visible from the code alone. If you find yourself reverse-engineering something that smells SSMS-specific, check the docs first.

## Build & test

The build chain uses **MSBuild** (not `dotnet build`) because the SSMS package projects need VSSDK targets. Tests use `dotnet test`.

```bash
# Restore (full solution)
nuget restore SqlPilot.sln

# Build everything (Modern + Legacy + installer + tests)
msbuild SqlPilot.sln /p:Configuration=Release /p:Platform="Any CPU"

# Build a single project
msbuild src/SqlPilot.Installer/SqlPilot.Installer.csproj /p:Configuration=Release

# Run all 44 core tests
dotnet test tests/SqlPilot.Core.Tests/SqlPilot.Core.Tests.csproj --configuration Release

# Run a single test
dotnet test tests/SqlPilot.Core.Tests/SqlPilot.Core.Tests.csproj --filter "FullyQualifiedName~FuzzyMatcher"
```

MSBuild on Windows lives at `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe` if it isn't on PATH.

The Core/UI/Smo projects build with `TreatWarningsAsErrors=true` (set in `Directory.Build.props`). The Package projects override it to `false` because the VSTHRD threading analyzers emit warnings the integration code intentionally accepts.

## Deploy to local SSMS for testing

```powershell
# As administrator
.\build\Deploy-Dev.ps1 -Version 22   # or 18 / 20
```

This script invokes the Modern or Legacy csproj directly, copies the DLLs + manifests + pkgdef into the SSMS Extensions folder, and invalidates SSMS caches. Re-deploys can fail if SSMS is currently running — close it first.

`build/SqlPilotDlls.txt` is the **single source of truth** for which DLLs ship in a release. It's read by `Deploy-Dev.ps1`, `Clean-And-Deploy.ps1`, AND `release.yml` (don't add a new dependency to one without updating the file).

## Project layout

Two extension builds from one source tree:

- **`SqlPilot.Package`** — SSMS 22 (VSSDK 17.x, VS Shell 17.0)
- **`SqlPilot.Package.Legacy`** — SSMS 18/20 (VSSDK 15.x, VS Shell 15.0)
- **`SqlPilot.Smo`** / **`SqlPilot.Smo.Legacy`** — same source, different SMO assembly references (SSMS 18 ships SMO 16.x, 20 ships 17.x, 22 ships 18.x)

The Legacy projects use `EnableDefaultCompileItems=false` and explicitly `<Compile Include="...\SqlPilot.Package\X.cs" Link="X.cs" />` every file from the modern project. **If you add a new file to `SqlPilot.Package` or `SqlPilot.Smo`, you must also add a `<Compile Include Link>` entry to the Legacy csproj** or the Legacy build will be missing it. CI catches this.

**Both Legacy projects must be in `SqlPilot.sln`** — `release.yml` builds via `msbuild SqlPilot.sln`, not by csproj path. A previous release shipped an empty SSMS18-20 folder because the Legacy projects were missing from the solution. Caught only by a test tag dry-run.

## The two installers

There are two installer paths and both ship in every release:

- **`src/SqlPilot.Installer/`** — WPF one-click installer (`SqlPilotInstaller.exe`). Self-fetches the release ZIP from GitHub, shows a checkbox UI for SSMS version selection, runs as admin via UAC manifest. The user-facing path; the README's primary install instructions point at it.
- **`installer/Install-SqlPilot.ps1`** + **`installer/Uninstall-SqlPilot.ps1`** — PowerShell scripts for CI / scripted deploys / IT admins. Both dot-source `installer/_SsmsHelpers.ps1` for `Get-SqlPilotSsmsCatalog` (the SSMS version table) and `Invoke-SsmsCacheInvalidation`.

The PowerShell SSMS catalog (`_SsmsHelpers.ps1::Get-SqlPilotSsmsCatalog`) and the C# catalog (`SqlPilot.Installer/Services/SsmsDetector.cs::Catalog`) are deliberate cross-language duplicates — keep them in sync. There's a comment on each pointing at the other.

## Release pipeline

Tag push (`v*`) triggers `.github/workflows/release.yml`. The workflow:

1. Validates the tag matches `^\d+\.\d+\.\d+(-[\w.-]+)?$`
2. **Patches the version into 7 files** via targeted regex (the same `1.0.0` string appears in `source.extension.vsixmanifest`, `extension.vsixmanifest.v1`, `extension.vsixmanifest.v2`, `manifest.json`, `catalog.json` ×2, `SqlPilot.Package.pkgdef`, and `SqlPilotPackage.cs`'s `[InstalledProductRegistration]` attribute). The v1 and v2 manifests each contain TWO `1.0.0` strings — one is the schema version, one is the extension version — only the extension version should be patched. **Don't touch the patch step's regexes without understanding the schema-version collision.** See `docs/ARCHITECTURE.md` § Release Pipeline.
3. Builds with `/p:Version=$numericVersion` (numeric only — pre-release suffixes get stripped because `AssemblyVersion` is strict numeric)
4. Packages two ZIPs: `SqlPilot-vX.Y.Z.zip` (the payload, with `SSMS22/` and `SSMS18-20/` subfolders) and `SqlPilotInstaller-vX.Y.Z.zip` (the WPF installer)
5. Attaches both to the GitHub Release. Tags with `-` in them auto-prerelease.

The installer pins to its own assembly version: it asks GitHub for `releases/tags/v{Major.Minor.Build}`, not `/releases/latest`. This keeps a `v1.0.0` installer from accidentally pulling a `v2.0.0` payload.

## Persistence: don't use System.Text.Json

`System.Text.Json` depends on a newer `System.Memory` than SSMS 18 ships, and the conflict surfaces as `FileNotFoundException` at runtime. The repo deliberately avoids it everywhere: `LineStore.cs` is a hand-rolled key/value file format, `UpdateChecker.cs` and `GitHubReleaseClient.cs` use regex JSON extraction. If you need to parse JSON inside an assembly that loads into SSMS, use `Newtonsoft.Json` (SSMS already ships it) or stick with regex for trivial extraction.

The standalone installer (`SqlPilot.Installer`) inherits the same regex pattern for consistency, even though it doesn't load into SSMS — the actual reason there is keeping the exe small and dependency-free.

## Cross-version reflection

Many integration points (`IObjectExplorerService.Tree`, `OpenTableHelperClass.EditTopNRows`, `ScriptFactory.DesignTableOrView`, etc.) are SSMS-internal types accessed via `AppDomain.CurrentDomain.GetAssemblies()` lookup. Type lookups are cached in `SqlPilot.Package/Integration/ReflectionTypeCache.cs` — both `ObjectExplorerBridge` and `ScriptingBridge` route through it. Don't roll your own assembly walks; use `ReflectionTypeCache.FindType("Microsoft.SqlServer.Management...")`.

When using SMO enums or properties that may differ across SMO 16/17/18, prefer `enum.ToString().Contains("...")` or reflection over hard typed-enum comparisons — the same enum value can have different members across SMO versions and the Legacy build will fail to load if you bind to one that doesn't exist in 16.x. (Counter-example: `UserDefinedFunctionType.Scalar` exists in all three and is safe.)

## Keep README and docs in sync

After any non-trivial change, **check whether `README.md` and `docs/` need updating**. The docs aren't auto-generated from code, so they drift unless someone notices. Specifically:

- **`README.md`** — features list, install instructions, keyboard shortcuts, tech stack, architecture tree. If you add or remove a feature, change the install flow, change a keyboard binding, or restructure projects, mirror the change here.
- **`docs/ARCHITECTURE.md`** — project structure, build strategy, deployment file requirements, release pipeline. If you add a new project, change `release.yml`, change the SSMS version requirements, or change cache-invalidation logic, mirror the change here.
- **`docs/SSMS_INTEGRATION_NOTES.md`** — anything you learn the hard way about SSMS internals belongs here. New gotchas, new fixes, new "things that don't work". This doc is the long-term value of every painful debugging session.
- **`docs/INSTALL.md`** — keep in sync with `README.md`'s install section.

When in doubt, ask: "if a future contributor reads only the docs, will they know what I just changed?" If the answer is no, update the docs.

## Persistent user preferences

This repo is being prepared for public release. **Never add Co-Authored-By, "Generated with Claude Code", or any AI-attribution trailers/comments** to commits, files, PRs, or anything else produced for this project. Match the existing terse-but-informative commit message style — the user values that.
