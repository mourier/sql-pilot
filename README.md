<p align="center">
  <img src=".github/logo.png" alt="SQL Pilot" width="280" height="280" />
</p>

<h1 align="center">SQL Pilot</h1>

<p align="center">
  <strong>Quick search for SQL Server Management Studio.</strong><br/>
  Find any table, view, stored procedure, or function in any database.<br/>
  Type three letters. Hit Enter. Done.
</p>

<p align="center">
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/License-Apache%202.0-blue.svg" alt="License: Apache 2.0" />
  </a>
  <a href="https://github.com/mourier/sql-pilot/actions/workflows/ci.yml">
    <img src="https://github.com/mourier/sql-pilot/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI" />
  </a>
  <img src="https://img.shields.io/badge/SSMS-18%20%7C%2020%20%7C%2022-1f4e79" alt="SSMS 18, 20, 22" />
  <img src="https://img.shields.io/badge/Language-C%23-239120" alt="Language: C#" />
  <img src="https://img.shields.io/badge/Platform-Windows%20x64%20%7C%20Arm64-0078D4" alt="Windows x64 and Arm64" />
</p>

<p align="center">
  <a href="#features">Features</a> •
  <a href="#keyboard-shortcuts">Shortcuts</a> •
  <a href="#install">Install</a> •
  <a href="#building-from-source">Build</a> •
  <a href="docs/ARCHITECTURE.md">Docs</a>
</p>

<!--
Drop a screenshot in .github/screenshot.png and uncomment this block when ready:

<p align="center">
  <img src=".github/screenshot.png" alt="SQL Pilot in action" />
</p>
-->

---

## The Problem

[SQL Hunting Dog](https://github.com/bugzinga/sql-hunting-dog) was the gold-standard quick-search add-in for SSMS for years — and it has been abandoned since 2018. It only runs on SSMS 2008–2014 (32-bit, .NET 3.5), and Microsoft never shipped a first-party equivalent. SSMS's native "Find Objects" dialog is modal, slow, and forces you to click through Object Explorer trees one node at a time.

SQL Pilot is a modern, from-scratch replacement. Same fast workflow, all the same context actions, runs on SSMS 18, 20, and 22 — including 64-bit SSMS 22 on both x64 and Arm64. The default keybindings, the type-aware action set, and the muscle memory all match hunting-dog so you can switch without retraining.

---

## Features

- **Fuzzy search** across every database on every connected server. Indexes tables, views, stored procedures, scalar and table-valued functions, and synonyms.
- **Keyboard-driven** — type to filter, arrows to navigate, Enter to act, Right for the secondary action, Space for the context menu. No mouse needed.
- **Type-aware context actions** — Select Top N, Edit Top N (the real editable grid, not a SELECT script), Design Table, Script Create, Modify, Execute. Tables get *Edit Data*. Procedures and functions get *Execute*. Views get *Modify View*.
- **Favorites** — pin objects you use constantly, persisted across SSMS restarts.
- **Recent objects** — automatically tracks your most recently accessed objects per session.
- **Azure SQL Database** — full support, including the URN domain stripping, `USE [db]` rejection, and 3-part-name restrictions that SSMS doesn't tell you about.
- **Friendly one-click installer** — branded WPF installer that detects every SSMS version on your machine, lets you pick which to install into via checkboxes, downloads the matching release ZIP from GitHub itself, and verifies SHA-256 before extracting. Same exe handles uninstall. PowerShell scripts also ship for CI / scripted deploys.
- **Auto-updater** — checks GitHub once a day in the background, never auto-installs, remembers skipped versions, and stays out of your way if you're offline.
- **Theme aware** — follows SSMS dark/light theme.

---

## Keyboard Shortcuts

| Key | Action |
|---|---|
| `Ctrl+D` | Open SQL Pilot from anywhere in SSMS |
| Type letters | Fuzzy-match across all indexed objects |
| `↓` / `↑` | Navigate results |
| `Enter` | Default action — *Select Data* for tables/views, *Modify* for procs/functions |
| `→` Right | Secondary action — *Edit Data* for tables, *Execute* for procs/functions |
| `Space` | Open the context menu |
| `Tab` / `Esc` | Back to the search box |
| Single letter (in open menu) | Activate the menu item with that underlined access key |

---

## Install

### Recommended — one-click installer

1. Download `SqlPilotInstaller-vX.Y.Z.zip` from [Releases](https://github.com/mourier/sql-pilot/releases)
2. Extract anywhere and run **`SqlPilotInstaller.exe`**
3. Approve the UAC prompt
4. Pick which SSMS versions to install into (the installer detects what you have), click **Install**
5. Restart SSMS · Press `Ctrl+D` to open SQL Pilot

The installer downloads the matching release payload from GitHub itself, verifies it, and copies the extension into every selected SSMS version. Same exe handles uninstall — re-run it on a machine where SQL Pilot is already installed and the **Uninstall** button appears.

> **First run will show "Windows protected your PC"** because we don't pay for an EV code-signing certificate yet. Click **More info** → **Run anyway**. The installer source is in [`src/SqlPilot.Installer/`](src/SqlPilot.Installer) — verify before running if you want.

### Power user — scripted install

For CI pipelines, silent installs, or admins who script everything:

1. Download `SqlPilot-vX.Y.Z.zip` from [Releases](https://github.com/mourier/sql-pilot/releases)
2. Extract it somewhere
3. Run as administrator:
   ```powershell
   powershell -ExecutionPolicy Bypass -File Install-SqlPilot.ps1
   ```
   The script detects every SSMS version on your machine (18, 20, and/or 22) and installs into each one. Caches are invalidated automatically.
4. Restart SSMS
5. Press `Ctrl+D` to open SQL Pilot

### Uninstall (scripted)

```powershell
powershell -ExecutionPolicy Bypass -File Uninstall-SqlPilot.ps1
```

Removes SQL Pilot from every SSMS version it's installed in. Per-version errors don't abort the others — if SSMS 20 is locked because it's still running, the uninstaller still cleans up 18 and 22 and tells you what to retry.

---

## Building from Source

### Prerequisites

- Visual Studio 2022 with the **".NET desktop development"** and **"Visual Studio extension development"** workloads
- **.NET Framework 4.7.2** targeting pack
- SSMS 18, 20, or 22 installed locally (the projects reference SMO DLLs from the SSMS install — see `lib/Ssms18/` and `lib/Ssms22/`)

### Build

```bash
nuget restore SqlPilot.sln
msbuild SqlPilot.sln /p:Configuration=Release /p:Platform="Any CPU"
```

### Run Tests

```bash
dotnet test tests/SqlPilot.Core.Tests/SqlPilot.Core.Tests.csproj --configuration Release
```

### Deploy to your local SSMS for testing

```powershell
# As administrator
.\build\Deploy-Dev.ps1 -Version 22   # or 18 / 20
```

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Language | **C#** (langversion=latest, .NET Framework 4.7.2) |
| UI | **WPF** + [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) |
| SSMS 22 (Modern) | **VSSDK 17.x** (`Microsoft.VisualStudio.SDK`) |
| SSMS 18/20 (Legacy) | **VSSDK 15.x** + IsolatedShell manifest |
| SQL metadata | **SMO** (`Microsoft.SqlServer.Smo`) — loaded from SSMS at runtime |
| Persistence | Line-based key/value store (no `System.Text.Json` — version conflict on SSMS 18) |
| Tests | **xUnit** + **FluentAssertions** + **NSubstitute** |
| CI / Release | **GitHub Actions** on `windows-latest` |

Single AnyCPU build. Works on Windows x64 and Arm64. No native dependencies.

---

## Architecture

```
src/
├── SqlPilot.Core/             # Search engine, models, favorites, recents (no SSMS deps)
├── SqlPilot.Smo/              # SMO database object provider (SSMS 22 SMO 18.x)
├── SqlPilot.Smo.Legacy/       # Same source, references SSMS 18 SMO 16.x
├── SqlPilot.UI/               # WPF controls, view models, themes
├── SqlPilot.UI.Demo/          # Standalone WPF app for UI work without SSMS
├── SqlPilot.Package/          # SSMS 22 extension (VSSDK 17.x)
├── SqlPilot.Package.Legacy/   # SSMS 18/20 extension (VSSDK 15.x)
└── SqlPilot.Installer/        # Standalone WPF installer .exe (downloads from GitHub Releases)
```

Two extension builds from one source tree, sharing every file except the VSSDK references and the manifest schema. The installer is a separate WPF exe that lives outside the extension load chain. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full breakdown.

---

## Documentation

| Doc | What it covers |
|-----|----------------|
| [Architecture](docs/ARCHITECTURE.md) | Project structure, build strategy, deployment files, release pipeline, version substitution |
| [SSMS Integration Notes](docs/SSMS_INTEGRATION_NOTES.md) | Hard-won knowledge: OE tree internals, `ScriptFactory` APIs, Azure SQL quirks, the SSMS 20 connection-string parser bug, things that don't work |
| [Install](docs/INSTALL.md) | Detailed installation walkthrough |

---

## Contributing

Contributions welcome. The code is intentionally small and easy to navigate — read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/SSMS_INTEGRATION_NOTES.md](docs/SSMS_INTEGRATION_NOTES.md) before touching the SSMS integration layer (there are several non-obvious gotchas documented there).

1. Fork the repo
2. Create a feature branch
3. Make your changes — keep PRs focused
4. Run `dotnet test tests/SqlPilot.Core.Tests/SqlPilot.Core.Tests.csproj`
5. Open a pull request

---

## Credits

SQL Pilot was inspired by [SQL Hunting Dog](https://github.com/bugzinga/sql-hunting-dog), the pioneering quick-search add-in for SSMS by **Alexander Maslyukov** and **Maxim Novikov**, with contributions from **pstraszak**, **FastNinja**, and **pklejnowski**.

SQL Pilot is an independent from-scratch implementation — no code is copied — but the workflow, default keybindings, and type-aware action set are deliberate matches so hunting-dog users can switch without retraining.

---

## License

Licensed under the [Apache License, Version 2.0](LICENSE). See [NOTICE](NOTICE) for additional attribution.
