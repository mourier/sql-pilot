## `lib/` — SSMS reference assemblies

This folder contains compiled reference assemblies from Microsoft SQL Server Management Studio. They are committed here so the `SqlPilot.Package*` and `SqlPilot.Smo*` projects can resolve references at compile time without requiring every contributor to install a specific SSMS version just to build.

### Not redistributed

These DLLs are **not shipped** in the SQL Pilot release. Every `<Reference>` that points into `lib/` sets `<Private>false</Private>`, so the SSMS-owned binaries are used purely for compilation and are excluded from the build output. The authoritative list of files that actually ship in the release is [`build/SqlPilotDlls.txt`](../build/SqlPilotDlls.txt) — verify there.

### Contents

| File | Source | Purpose |
|---|---|---|
| `Ssms18/SqlWorkbench.Interfaces.dll` | SSMS 18 install dir | Object Explorer + scripting service interfaces (Legacy build) |
| `Ssms18/Microsoft.SqlServer.Smo.dll` et al | SSMS 18 install dir | SMO 16.x reference for the Legacy build |
| `Ssms22/SqlWorkbench.Interfaces.dll` | SSMS 22 install dir | Object Explorer + scripting service interfaces (Modern build) |
| `Ssms22/SqlWorkbench.Interfaces.v15.dll` | SSMS 22 install dir | VSSDK 15-era interface surface still used by SSMS 22 |
| `Ssms22/Microsoft.SqlServer.Smo.dll` et al | SSMS 22 install dir | SMO 18.x reference for the Modern build |

### License

These assemblies are Microsoft's and are governed by the [SQL Server Management Studio License Terms](https://learn.microsoft.com/en-us/legal/sql/ssms/sql-server-management-studio-license-terms). SQL Pilot's own Apache 2.0 license does not apply to them and grants no rights in them — they are here solely as build-time references. If you obtain a copy of this repository, you still need a licensed SSMS installation on any machine where those DLLs are used at runtime.

This is the same pattern used by [SQL Hunting Dog](https://github.com/bugzinga/sql-hunting-dog) (since 2014), [SSMS-Schema-Folders](https://github.com/nicholas-ross/SSMS-Schema-Folders), [Sql4Cds](https://github.com/MarkMpn/Sql4Cds), and most other open-source SSMS extensions — no official NuGet package or open-source distribution of `SqlWorkbench.Interfaces.dll` exists.
