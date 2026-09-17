# SSMS Integration Notes

Hard-won knowledge about integrating with SSMS from an extension.
Read this before touching the integration layer.

## Object Explorer Tree

### The Tree property returns a TreeView (not a container)

`IObjectExplorerService.Tree` returns `ObjectExplorerControl` which **inherits from** `System.Windows.Forms.TreeView`:

```
ObjectExplorerControl
  → LazyTreeView
  → ThemedTreeView
  → System.Windows.Forms.TreeView
```

**Not** a container with a child TreeView. Cast directly:

```csharp
var treeView = oeControl as System.Windows.Forms.TreeView;
// Not: FindChildOfType<TreeView>(oeControl)
```

`treeView.Controls.Count == 0` — the tree IS the control, not a parent of it.

### Nodes are `ExplorerHierarchyNode`

When you get `treeView.SelectedNode`, it's an `ExplorerHierarchyNode` (extends `TreeNode`). These nodes have SSMS-specific methods for context menu actions but they are version-dependent — always reflect to check what's available.

### FindNode returns a detached NodeContext, not a TreeNode

`IObjectExplorerService.FindNode(urn)` returns `INodeInformation` (concrete: `NodeContext`). This is a **detached** node info object — its `TreeNode` property is **null** because it isn't actually attached to the tree.

To get the real tree node, you must:
1. Call `FindNode` to get the `INodeInformation`
2. Call `SynchronizeTree(nodeInfo)` which navigates + selects it in the tree
3. Then read `treeView.SelectedNode` — now it's a real `ExplorerHierarchyNode` with TreeNode attached

### SynchronizeTree can trigger connection dialogs

`SynchronizeTree` walks down the tree expanding each level. If any level isn't yet cached, it triggers a new connection to load it. This can pop up the "Connect to Server" dialog. To avoid: ensure the user has already expanded the relevant database folder in OE before calling it.

### GetConnectedServerNames via GetSelectedNodes

The most reliable way to get connected servers is `oeService.GetSelectedNodes(out count, out INodeInformation[] nodes)` — but this only returns nodes the user has currently selected. For a full enumeration without user interaction, walk `treeView.Nodes` directly.

The connection extracted from a node's `Connection` property is a `SqlConnectionInfoWithConnection` — it has a live, already-authenticated connection. Never try to construct a new connection from parts; reuse this one.

## Scripting Bridge (actions on objects)

### Modifying procs/views/functions

Don't use `OBJECT_DEFINITION(OBJECT_ID(...))` — it's unreliable (returns NULL for encrypted objects, doesn't include the header). Use SMO directly:

```csharp
var sp = db.StoredProcedures[name, schema];
sp.Refresh();
string script = sp.ScriptHeader(true) + sp.TextBody;  // true = ALTER, false = CREATE
```

Same pattern for `View` and `UserDefinedFunction`. This matches hunting-dog's approach exactly.

### Scripting Tables

For CREATE TABLE scripts, use `Scripter` with these options:

```csharp
var scripter = new Scripter(server);
scripter.Options.ScriptDrops = false;
scripter.Options.WithDependencies = false;
scripter.Options.Indexes = true;
scripter.Options.DriAllConstraints = true;
scripter.Options.DriDefaults = true;
scripter.Options.NoCollation = true;
foreach (string s in scripter.Script(new Urn[] { table.Urn })) { ... }
```

### Creating a new connected query window

Use `ServiceCache.ScriptFactory.CreateNewBlankScript` — it opens a query window already connected to the right server:

```csharp
var scriptFactory = GetServiceCacheScriptFactory();  // via reflection
// ScriptType.Sql = 0
var scriptType = FindEnumValue("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptType", 0);
var uiConn = CreateUIConnectionInfo(connInfo);  // UIConnectionInfo struct via reflection
var method = scriptFactory.GetType().GetMethod("CreateNewBlankScript", new[] { scriptType.GetType(), uiConn.GetType(), typeof(System.Data.IDbConnection) });
method.Invoke(scriptFactory, new object[] { scriptType, uiConn, null });
```

Then insert text via DTE:

```csharp
var dte = GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
if (dte?.ActiveDocument?.Object("TextDocument") is EnvDTE.TextDocument doc)
    doc.EndPoint.CreateEditPoint().Insert(scriptText);
```

The `UIConnectionInfo` needs these properties set: `ServerName`, `UserName`, `Password`, `PersistPassword=true`, `ApplicationName`, `ServerType` (GUID `8c91a03d-f9b4-46c0-a305-b5dcc79ff907` for SQL Server), `AuthenticationType` (0=Windows, 1=SQL).

### Design Table (works!)

```csharp
var mc = new ManagedConnectionWrapper { Connection = rawConn };  // rawConn is SqlOlapConnectionInfoBase from OE
var docType = FindEnumValue("...Editors.DocumentType", 6);  // Table = 6
var docOpts = FindEnumValue("...Editors.DocumentOptions", 2);  // ManageConnection = 2
var method = scriptFactory.GetType().GetMethod("DesignTableOrView", BindingFlags.Public | BindingFlags.Instance);
method.Invoke(scriptFactory, new object[] { docType, docOpts, table.Urn.ToString(), mc });
```

### Edit Top N Rows — Use OpenTableHelperClass, NOT DesignTableOrView

**The right API:** `Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.OpenTableHelperClass.EditTopNRows(NodeContext, int)` (static). Lives in `ObjectExplorer.dll`.

```csharp
var oeService = GetObjectExplorerService();
var findNodeMethod = oeService.GetType().GetMethod("FindNode", BindingFlags.Public | BindingFlags.Instance);
var nodeInfo = findNodeMethod.Invoke(oeService, new object[] { tableUrn });

// CRITICAL: Set DatabaseName on the node's Connection so EditTopNRows
// reuses the existing OE connection instead of prompting for a new one.
var nodeConn = nodeInfo.GetType().GetProperty("Connection")?.GetValue(nodeInfo);
nodeConn?.GetType().GetProperty("DatabaseName")?.SetValue(nodeConn, databaseName);

var helperType = FindType("Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.OpenTableHelperClass");
var editMethod = helperType.GetMethod("EditTopNRows", BindingFlags.Static | BindingFlags.Public);
editMethod.Invoke(null, new object[] { nodeInfo, 200 });
```

**This works on SSMS 18, 20, and 22.** Hunting-dog used this exact approach and we confirmed the class still exists in all three versions.

### What does NOT work — DesignTableOrView(DocumentType.OpenTable)

`ScriptFactory.DesignTableOrView(DocumentType.OpenTable, ...)` fails with:

```
System.ArgumentException: Value does not fall within the expected range.
  at VsDataDesignerNode.GetDsRef(Urn urn)
  at OpenTableNode.CreateDesigner(...)
```

on **all three versions** (SSMS 18, 20, 22). This confirms the `DocumentType.OpenTable` code path is not the right entry point for programmatic access — use `OpenTableHelperClass` instead. `DocumentType.Table` (Design Table) still works fine through `DesignTableOrView`.

**Key lesson:** SSMS has multiple internal entry points for the same UI feature. The `Scripter`/`ScriptFactory.DesignTableOrView` API is for designer-style entry (table designer, view designer). The `OpenTableHelperClass` is for grid-style entry (Edit Top N Rows, Select Top N Rows). Don't conflate them.

## IManagedConnection

Implement it yourself — it's in `SqlWorkbench.Interfaces.dll` (which we already reference):

```csharp
internal sealed class ManagedConnectionWrapper : IManagedConnection
{
    public SqlOlapConnectionInfoBase Connection { get; set; }
    public void Close() { }
    public void Dispose() { }
}
```

`SqlConnectionInfoWithConnection` from OE inherits from `SqlOlapConnectionInfoBase`, so assign it directly. Do **not** construct a new `SqlConnectionInfo` from scratch — you'll lose the live connection and trigger a new login prompt.

## Global Hotkey (Ctrl+D)

`InputBinding` on the main WPF window does **not** intercept keys when the SQL editor has focus (the editor is a WinForms-hosted control with its own message loop). Use Win32 `RegisterHotKey` instead:

```csharp
[DllImport("user32.dll")]
static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

var mainWindow = Application.Current.MainWindow;
var hwnd = new WindowInteropHelper(mainWindow).Handle;
var hwndSource = HwndSource.FromHwnd(hwnd);
hwndSource.AddHook(WndProc);
RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL, VK_D);
// MOD_CONTROL = 0x0002, VK_D = 0x44, WM_HOTKEY = 0x0312
```

Works globally across all SSMS panels including the SQL editor.

**Note**: `Ctrl+D` is hunting-dog's default. It's a reasonable choice but conflicts with any other extension using the same global hotkey. Consider making it configurable in a future version.

## DTE Commands

DTE `ExecuteCommand` command names in SSMS are **not** the same as in Visual Studio. Most VS-style commands (`Query.EditTopNRows`, `Tools.Options`) don't exist. The commands you can use are mostly the ones listed in SSMS's Tools → Options → Environment → Keyboard.

To enumerate available commands:

```csharp
foreach (EnvDTE.Command cmd in dte.Commands)
    Console.WriteLine($"{cmd.Name} (guid:{cmd.Guid})");
```

This gives ~5000 commands in SSMS 22 — search for keywords.

## Persistence: Don't use System.Text.Json

`System.Text.Json` depends on `System.Memory` — and **SSMS 18** ships a version of `System.Memory` (4.0.1.1) that's incompatible with modern `System.Text.Json` packages. This causes `FileNotFoundException` on load.

Use a simpler format. We use line-based pipe-delimited files (`LineStore.cs`) which has zero dependencies. If you need JSON, bundle `Newtonsoft.Json` — SSMS already ships it so you can reference it without conflict.

## Reflection type lookups: use the shared cache

Several integration points (`IObjectExplorerService`, `INodeInformation`, `OpenTableHelperClass`, `ScriptFactory`, etc.) live in SSMS-internal assemblies that we discover at runtime via `AppDomain.CurrentDomain.GetAssemblies()`. SSMS loads ~150 assemblies, so a naive walk per call is expensive.

Use `SqlPilot.Package.Integration.ReflectionTypeCache.FindType("Fully.Qualified.Type.Name")` — it's a `ConcurrentDictionary<string, Type>` that walks loaded assemblies once per unique type name and caches the result (including null results, since a missing type in this SSMS version won't suddenly appear later). Both `ScriptingBridge` and `ObjectExplorerBridge` route through it.

```csharp
var oeServiceType = ReflectionTypeCache.FindType(
    "Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.IObjectExplorerService");
```

Don't roll your own assembly walks in new code.

## Smo Version Matters

SMO DLLs differ significantly between SSMS versions:

| SSMS | SMO Version |
|------|-------------|
| 18   | 16.100.0.0  |
| 20   | 17.100.0.0  |
| 22   | 18.100.0.0  |

The **enum names and values** can differ between versions (e.g., `UserDefinedFunctionType` resides in different assemblies, `DataType.SqlDataType` has version-specific values). If the legacy build compiles against 18.x SMO, it will **fail to load** on SSMS 18's 16.x SMO.

**Fix**: Separate project `SqlPilot.Smo.Legacy` that references `lib/Ssms18/` DLLs for the legacy build. Source is shared via `<Compile Include="..\SqlPilot.Smo\*.cs" />`.

Safer: use reflection for anything version-specific like `fn.FunctionType.ToString().Contains("Table")` instead of `fn.FunctionType == UserDefinedFunctionType.Table`.

## SSMS 20's ConnectionString is incompatible with SMO's parser

`ObjectExplorerBridge.GetConnectionInfo()` returns a live connection's `ConnectionString` property. On **SSMS 20** the string contains keywords with spaces that SMO's older `ServerConnection.ConnectionString` parser rejects at runtime:

```
Keyword not supported: 'multiple active result sets'.
```

The valid ADO.NET keyword is `MultipleActiveResultSets` (no spaces) but SSMS 20 writes it with spaces. SSMS 18 and 22 don't include this keyword in their connection strings at all, so the problem only surfaces on 20.

**Fix** in `SmoDatabaseObjectProvider.CreateServer`: prefer the individual properties (`ServerInstance`, `Login`, `Password`) when constructing `ServerConnection`, and fall back to the raw connection string only if those aren't available:

```csharp
var serverConn = new ServerConnection();
serverConn.ServerInstance = _connection.ServerName;
serverConn.TrustServerCertificate = true;
if (!_connection.UseIntegratedSecurity)
{
    serverConn.LoginSecure = false;
    serverConn.Login = _connection.UserName ?? "";
    serverConn.Password = _connection.Password ?? "";
}
```

Do NOT try to sanitize the connection string with regex — new keywords may appear in future SSMS versions.

## Azure SQL constraints

Azure SQL Database (`*.database.windows.net`) differs from on-prem SQL Server in three load-bearing ways. Detect via `ConnectionDescriptor.IsAzureSqlServerName(serverName)`.

### SSMS strips the domain suffix from URNs

SSMS's internal Object Explorer URNs use the logical short name, not the full FQDN. `foo.database.windows.net` becomes `Server[@Name='foo']`. If you build a URN from `ConnectionDescriptor.ServerName` (which has the full FQDN), `IObjectExplorerService.FindNode` returns null and table-based actions (Edit Top N Rows, etc.) silently fall back.

**Fix**: walk the OE tree root nodes and extract each root's `Context` property (which carries the URN as SSMS stored it). Match by prefix against the logical server name, and use the extracted root server name when building URNs. See `ObjectExplorerBridge.ResolveUrnServerName`. For on-prem servers the URN equals the logical name, so the walk is skipped as a fast path.

### `USE [database]` is not supported

Azure SQL rejects `USE` — each query window must bind directly to a target database at connection time. When opening a query via `ScriptFactory.CreateNewBlankScript`, set `AdvancedOptions["DATABASE"]` on the `UIConnectionInfo` *before* calling it, and do **not** prepend a `USE [db] GO` header to the script:

```csharp
if (isAzure)
{
    var advProp = uiConn.GetType().GetProperty("AdvancedOptions");
    var adv = advProp?.GetValue(uiConn);  // IDictionary<string,string> via reflection
    var indexer = adv.GetType().GetProperty("Item", new[] { typeof(string) });
    indexer?.SetValue(adv, obj.DatabaseName, new object[] { "DATABASE" });
}
```

The query window will open already bound to the right database.

### 3-part names (`[db].[schema].[obj]`) are not supported

Azure SQL queries can only reference objects in the current database via 2-part names. Build table references conditionally:

```csharp
static string BuildTableReference(DatabaseObject obj) =>
    ConnectionDescriptor.IsAzureSqlServerName(obj.ServerName)
        ? obj.QualifiedName                              // [schema].[name]
        : $"[{obj.DatabaseName}].{obj.QualifiedName}";   // [db].[schema].[name]
```

Combined with the `USE` constraint, this means generated scripts look different on Azure SQL vs on-prem: no `USE [db] GO` header, 2-part names only.

## Build & Packaging

### Manual pkgdef + catalog.json + manifest.json

`dotnet build` does **not** run the VSSDK build targets that generate `.pkgdef` and VSIX package layout. For now we hand-maintain these in `src/SqlPilot.Package/`:

- `SqlPilot.Package.pkgdef` — VS package registration (GUID, assembly, auto-load)
- `extension.vsixmanifest.v1` — for SSMS 18/20 (IsolatedShell target)
- `extension.vsixmanifest.v2` — for SSMS 22 (Microsoft.VisualStudio.Ssms target)
- `manifest.json` — SSMS 22 extension inventory (required to be discovered)
- `catalog.json` — SSMS 22 VSIX catalog metadata

If you add a new package GUID, tool window GUID, or command, update the pkgdef.

### Cache invalidation is critical

After deploying/updating files, SSMS **will not pick up changes** unless you invalidate its caches. Required steps:

1. Delete `privateregistry.bin` (+ `.LOG1`, `.LOG2`) in `%LocalAppData%\Microsoft\SSMS\<version>\`
2. Delete `ComponentModelCache\` folder in the same location
3. Touch `extensions.configurationchanged` file

Just one of these is not enough. All three are needed. See `build/Deploy-Dev.ps1`.

### PkgDefSearchPath is global-only

SSMS only scans `C:\Program Files\...\Common7\IDE\Extensions\` for `.pkgdef` files. Per-user `%LocalAppData%\Microsoft\SSMS\<ver>\Extensions\` is **not** scanned (despite what some docs suggest). Deploy to Program Files — requires admin.

### SSMS 22 install path has an extra "Release" folder

- SSMS 18/20: `C:\Program Files (x86)\Microsoft SQL Server Management Studio <ver>\Common7\IDE\`
- SSMS 22: `C:\Program Files\Microsoft SQL Server Management Studio 22\**Release**\Common7\IDE\`

The `Release` folder is unique to SSMS 22.

## Running SSMS from code

**Don't** use bash `start` to launch `Ssms.exe` — git bash expands `/log` to `C:/Program Files/Git/log`. Use PowerShell `Start-Process` instead:

```powershell
Start-Process 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe' -ArgumentList '-log'
```

Note: SSMS 22 uses `-log` (dash), not `/log` (slash). SSMS 18 accepts both.

### SSMS 22's command line is not SSMS 18/20's

Two differences that each cost a long debugging session:

- **There is no `-E`.** SSMS 18/20 take `-E` for Windows auth; SSMS 22 dropped it (auth is
  `-A <method>`, and Windows auth is the default). An unknown switch does **not** start the
  IDE — it shows a usage-error dialog whose title is *"Microsoft SQL Server Management Studio"*,
  the same as the real main window. Automation that finds a window by title and process will
  happily attach to that 625×420 error box, and every "the extension didn't load" conclusion
  drawn from it is meaningless. Check the window is IDE-sized, or that Object Explorer exists,
  before trusting anything else.
- **`-log` takes a filename** (`-log C:\path\ActivityLog.xml`). Bare `-log` consumes the next
  argument as the filename, so `-log -S server` silently eats `-S`.

### Autoload: NoSolution is not enough on SSMS 22

The package autoloads on `UIContextGuids80.NoSolution`. That fires at startup in the VS 2017
shell (SSMS 18/20) and on a **plain** SSMS 22 start, but **not** when SSMS 22 is launched as
`ssms -S <server>` — the auto-connect path never raises it. Because `SqlPilotCommandSet.vsct`
is compiled by neither csproj, the Ctrl+D hotkey registered during package init is the only way
into the extension, so on that path it simply never appears, with no error anywhere.

`SSMS.Application.pkgdef` declares the shell's own context, `UICONTEXT_SSMS =
{B7B07F42-6013-4C67-A504-C771CBC7625A}`, and autoloading on that as well covers the `-S` path.
Both triggers are in `SqlPilot.Package.pkgdef`; a package loads once however many fire.

The pkgdef is **hand-maintained and checked in** — `Deploy-Dev.ps1` and `release.yml` copy
`src/SqlPilot.Package/SqlPilot.Package.pkgdef`, and the VSSDK pkgdef-generation target does
not run in this SDK-style project (same reason as the `.vsix` container target). Changing a
`[ProvideAutoLoad]` attribute does nothing on its own; edit the pkgdef too.

**How to test an autoload context without rebuilding or elevation:** VSIXInstaller will install
a per-user `.vsix` whose only payload is a pkgdef adding one more `AutoLoadPackages` entry for
the already-registered package GUID. If the package then loads, that context fires; a control
probe with a made-up GUID must *not* make it load. This is how the two contexts above were
established.

```powershell
# minimal probe pkgdef -- the package itself is already registered from Program Files
[$RootKey$\AutoLoadPackages\{B7B07F42-6013-4C67-A504-C771CBC7625A}]
"{8f4a3b2e-1c5d-4e6f-9a0b-7d8c2e3f4a5b}"=dword:00000002
# zip it with extension.vsixmanifest + [Content_Types].xml, then:
& "$ide\VSIXInstaller.exe" /quiet probe.vsix
& "$ide\VSIXInstaller.exe" /quiet /uninstall:<identity>
```

Plain file copies into `%LocalAppData%\Microsoft\SSMS\<ver>\Extensions\` are **not** picked up
(not even by `/updateconfiguration`); only VSIXInstaller registers per-user extensions.

## Debug Logging

`Debug.WriteLine` from an extension goes to... nowhere useful by default. For real diagnostics:

- Write to a file on Desktop (temporary — **never** ship this)
- Use `IVsActivityLog` (from `SVsActivityLog` service) — appears in SSMS's `ActivityLog.xml` at `%AppData%\Microsoft\SSMS\<ver>\` when launched with `-log`

For user-visible messages, use the SQL Pilot status bar (`IndexStatus.Text`).

## Things We Tried That Don't Work

- **`Process.Modules` as a "did the extension load?" check** — managed assemblies loaded by a package don't reliably appear there; it reported "not loaded" for an extension that was demonstrably running. Drive the real UI instead: send Ctrl+D and look for the `SQL Pilot` pane through UI Automation — after first confirming the window you attached to is the IDE and not a dialog (see *SSMS 22's command line*).
- **DTE commands** for Edit Top N Rows: `Query.EditTopNRows`, `ObjectExplorer.EditTopNRows`, etc. — none exist in SSMS
- **`DesignTableOrView(DocumentType.OpenTable)`** — fails at `GetDsRef` on all three SSMS versions. Use `OpenTableHelperClass.EditTopNRows` instead
- **ScheduleSqlScriptAsOneStep** — opens the SQL Agent Job Schedule dialog, not a query
- **WPF InputBinding on main window** for Ctrl+D — doesn't intercept when SQL editor has focus
- **System.Text.Json** for persistence — breaks on SSMS 18 due to System.Memory version
- **`SynchronizeTree` without pre-expanded tree** — triggers connection dialogs
- **Building against SSMS 22's SMO and expecting it to work on SSMS 18** — version mismatch at runtime
- **Passing a `nodeInfo` from `FindNode` to `OpenTableHelperClass.EditTopNRows` without setting `Connection.DatabaseName` first** — triggers a new connection dialog because the connection defaults to master/empty
- **`<CreateVsixContainer>true</CreateVsixContainer>` on `SqlPilot.Package.csproj`** to get a `.vsix` out of the build — produces **no** `.vsix`. The VSSDK `CreateVsixContainer` target never imports because `source.extension.vsixmanifest` is only auto-globbed as a plain `None` item in this SDK-style project; the property is read but no target runs (verified via `-v:diagnostic`). Instead, the release builds the `.vsix` by zipping a copy of the assembled `SSMS22/` release folder plus a generated `[Content_Types].xml` (an OPC ZIP), which guarantees payload parity with the installer via the shared `build/SqlPilotDlls.txt`. See `release.yml` and `docs/ARCHITECTURE.md` § Modern `.vsix` artifact.
- **Shipping `catalog.json` / `manifest.json` *inside* the `.vsix`** — the modern SSMS 22 VSIXInstaller (the VS-Installer ServiceHub `VSIXInstaller.exe`) reads the metadata fine but **fails the actual install** with `InvalidExtensionManifestException: the provided vsixmanifest, version Invalid is not vsixV1, vsixV2, or vsixV3`. Their presence makes it take a v3-catalog install path that rejects the manifest. A clean OPC vsix containing only `extension.vsixmanifest` + `[Content_Types].xml` + payload (DLLs + pkgdef) installs with exit code 0 (to `%LOCALAPPDATA%\Microsoft\SSMS\<ver>\Extensions\<random>\`). So `release.yml` deletes `catalog.json`/`manifest.json` from the `.vsix` staging copy only — the loose-file payload ZIP still ships them for the file-copy installer. (`catalog.json`/`manifest.json` are the VS2017+ loose-file deployment descriptors, not VSIX-container files.)
