# SSMS Integration Notes

Hard-won knowledge about integrating with SSMS from an extension.
Read this before touching the integration layer.

## Last checked against

| SSMS | Build | When | Result |
|------|-------|------|--------|
| 18 | 18.12.1 (15.0.18424.0), the final 18 release | 2026-04 | — |
| 20 | 20.2 (20.2.30.0) | 2026-11-25 | Pane, indexing, search, ALTER, Select Data, Edit Data, Script Table work. Design Table does nothing (see *SSMS 20 loads two SMO versions*). 20.2.1 (20.2.37.0, a security-only release) not run. |
| 22 | 22.12.1 (22.12.12412.261) | 2026-11-25 | All of the above including Design Table, started as `ssms -S`. No code change was needed coming from 22.4.1; SMO is still assembly version 18.100.0.0 and `SqlWorkbench.Interfaces` still 22.200.0.0, so `lib/Ssms22` was left as it is. |

One LocalDB server with Windows authentication each time. Not rerun on these builds: several servers, Azure SQL.

Encryption and certificate trust (see *SSMS 20 loads two SMO versions*), checked on 2026-11-28 with a SQL login through `tools/TlsFront` (see *Testing encryption without a SQL Server*):

- **Inside SSMS 22.12.1**, through 22.9's redesigned Connect dialog. Encrypt Mandatory with Trust Server Certificate ticked, against a certificate Windows does not trust: indexing, ALTER and a connected query window all work (before, ALTER came back as "Could not retrieve definition"). With trust clear and `Server Certificate` pointing at the server's certificate: the same three work. Then with the server's certificate swapped for another: Refresh ends in *"Could not index 127.0.0.1,14333 (The certificate is not valid for the requested usage)"* and no login is sent.
- **Outside the IDE**, the same connection code run against each version's own SMO, `ConnectionDlg` and SqlClient: indexing, ALTER and the query-window connection connect when trust is ticked and are refused when it is not, on 18, 20 and 22; plain LocalDB is unaffected.
- Not run: SSMS 18 and 20 in the IDE with this build, and a certificate in the Windows trust store.

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

### Enumerating connected servers

Every connected server is a root node of the tree. Walk `treeView.Nodes` and ask each root for its `INodeInformation` — the node itself is the service provider:

```csharp
var treeView = oeControl as TreeView;                      // see above: cast, don't search
var nodeInfoType = ReflectionTypeCache.FindType("...ObjectExplorer.INodeInformation");
foreach (TreeNode root in treeView.Nodes)
{
    var nodeInfo = (root as IServiceProvider)?.GetService(nodeInfoType);
    var conn = nodeInfo?.GetType().GetProperty("Connection")?.GetValue(nodeInfo);
    // conn.ServerName, conn.UserName, conn.Password, conn.UseIntegratedSecurity ...
}
```

This is `ObjectExplorerBridge.GetConnectedServerNames`. Verified on SSMS 18, 20 and 22 with several servers connected, Windows and SQL authentication — for a SQL login the root's `Connection` carries the password, so indexing and query windows work without a prompt.

`oeService.GetSelectedNodes(out count, out INodeInformation[] nodes)` is kept only as a last resort: it returns just the nodes the user has selected, so on its own it sees one server however many are connected. That was the "only the last server loaded is usable" bug (issue #9) — the tree lookup silently failed (see *Things We Tried That Don't Work*), everything fell through to `GetSelectedNodes`, and each refresh replaced the index with the selected server alone.

A refresh rebuilds the whole index and starts by clearing it, so index work never overlaps: full refreshes and scope toggles all go through `EnqueueIndexWork` in the tool window control, which runs them one after another and keeps the Refresh button and the scope tree disabled until the last one is done. Refresh requests that arrive while one is still waiting its turn collapse into it; one that arrives after it has started gets one more run. `ConnectionWatcher` likewise raises one event per poll, not one per new server.

Before this, only refreshes were guarded. The watcher can start a refresh while a scope toggle is still indexing; the toggle then finished first, re-enabled the button and the tree mid-refresh, and its own indexing ran alongside the refresh's clear-and-rebuild. To reproduce an overlap on demand, make indexing slow with a schema lock from sqlcmd (`BEGIN TRAN; ALTER TABLE dbo.SomeTable ADD x int NULL; WAITFOR DELAY '00:00:30'; ROLLBACK` — SMO's metadata queries block on it), re-include that database in the Scope panel, then connect another server. Note that connecting brings Object Explorer to the front, and a tool window that is not the active tab of its group is absent from the UI Automation tree: click the `SQL Pilot` tab again before reading the pane.

Each server is indexed inside its own `try`/`catch`. Indexing opens a fresh SMO connection per server, and that can fail for one server while Object Explorer still lists it (login disabled, instance stopped). Because the index was just cleared, an exception escaping the loop would leave every later server unsearchable; instead the failed server is named after the totals in `IndexStatus` (and in its tooltip, since the status line is a single unwrapped row). To reproduce: connect a SQL-login server and another server after it, `ALTER LOGIN ... DISABLE` and `KILL` its sessions from a different connection, press Refresh.

The connection extracted from a node's `Connection` property is a `SqlConnectionInfoWithConnection` — it has a live, already-authenticated connection. Never try to construct a new connection from parts to hand to SSMS's own APIs; reuse this one. (SQL Pilot's own SMO connections are built separately; see *SSMS 20 loads two SMO versions*.)

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

Everything else the Connect dialog offers travels in `AdvancedOptions`, a `NameValueCollection` keyed by the constants of `ConnectionDlg`'s `SqlServerType`: `DATABASE`, `ENCRYPT_CONNECTION` (`True`/`False` on 18; also `Mandatory`/`Optional`/`Strict` on 20 and 22), `TRUST_SERVER_CERTIFICATE`, `HOST_NAME_IN_CERTIFICATE` (20 and 22), `CONNECTION_TIMEOUT`, `EXEC_TIMEOUT`, `PACKET_SIZE`, `APPLICATIONINTENT` and more. **A key SSMS does not know is ignored without a word**: `AdvancedOptions["TrustServerCertificate"]` did nothing for as long as SQL Pilot set it. `OtherParams` is the dialog's Additional Connection Parameters, appended to the connection string as it stands, where a repeated keyword overrides the earlier one.

With nothing set, SSMS 20 and 22 encrypt and do not trust the certificate; SSMS 18 does not encrypt. To see what a `UIConnectionInfo` turns into without opening a window, pass it to `UIConnectionInfoUtil.GetCoreConnectionInfo` (public, in `ConnectionDlg.dll`) and read `ConnectionString` off the result; it runs outside the IDE.

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

**It is global to the desktop, not to one SSMS.** `RegisterHotKey` claims Ctrl+D system-wide for the first SSMS that loads SQL Pilot. A second SSMS started while the first is open (another version, or the same one) fails to register, silently, and Ctrl+D pressed in it opens the pane in the *first* instance. When testing versions side by side this looks exactly like "the package did not load in the new one": close every other SSMS before drawing that conclusion.

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

The valid ADO.NET keyword is `MultipleActiveResultSets` (no spaces) but SSMS 20 writes it with spaces. SSMS 18 writes it without spaces. SSMS 22 writes it with spaces too, but its SMO accepts it, so the problem only surfaces on 20 (see *SSMS 20 loads two SMO versions* below).

**Fix** in `SmoDatabaseObjectProvider.CreateServerConnection`: on SSMS 20, construct the `ServerConnection` from individual properties instead of the string, carrying over the encryption and trust the string asked for:

```csharp
var serverConn = new ServerConnection();
serverConn.ServerInstance = connection.ServerName;
serverConn.EncryptConnection = connection.RequestsEncryption
    && !ConnectionDescriptor.IsLocalDbServerName(connection.ServerName);
serverConn.TrustServerCertificate = connection.TrustsServerCertificate;
if (!connection.UseIntegratedSecurity)
{
    serverConn.LoginSecure = false;
    serverConn.Login = connection.UserName ?? "";
    serverConn.Password = connection.Password ?? "";
}
```

Do NOT try to sanitize the connection string, with regex or by asking a `SqlConnectionStringBuilder` which keywords it knows. The second was tried and caused two bugs (next section).

### SSMS 20 loads two SMO versions

The Legacy build references SMO 16.100. SSMS 20's own SMO is 17.100, but it also ships 16.100 for SSIS (`Common7\IDE\CommonExtensions\Microsoft\SSIS\160\Binn\150References\SMO`), and SQL Pilot binds to that copy. Both are loaded in the one process:

| | Object Explorer | SQL Pilot |
|---|---|---|
| SMO | 17.100 | 16.100 |
| Driver | `Microsoft.Data.SqlClient` | `System.Data.SqlClient` |

On SSMS 18 and 22 the SMO that SQL Pilot is built against is the one SSMS itself uses, so the string is read by the driver family that wrote it, and SQL Pilot hands it over untouched.

This was the "`-- Could not retrieve definition for ...` instead of the ALTER script" bug (issue #15). `ScriptingBridge.GetServer` ran the string through `SanitizeConnectionString`, which dropped every keyword `System.Data.SqlClient` rejects, `Trust Server Certificate` among them, but kept `Encrypt=True`. The connection then failed, against LocalDB with *"The instance of SQL Server you attempted to connect to does not support encryption"*, and `GetModuleScript` swallowed the exception. Translating the trust keyword is not enough: SMO 16 cannot open an encrypted connection to LocalDB at all.

The same filter did the same damage on SSMS 22, where it was never needed: with "Trust server certificate" ticked and a certificate Windows does not trust (any server still on its self-generated one), indexing worked and every ALTER or CREATE came back as "Could not retrieve definition", the driver saying *"The certificate chain was issued by an authority that is not trusted"*. LocalDB hides it, because `Microsoft.Data.SqlClient` ignores `Encrypt` there. The filter is gone.

`SmoDatabaseObjectProvider.CreateServerConnection` now builds the one connection that both indexing and scripting use. It takes the connection string as it is when Object Explorer's connection is an instance of our own `SqlConnectionInfo` (SSMS 18, 22), and the individual properties otherwise (SSMS 20). Either way the rule is the same: **SQL Pilot's connection asks for the encryption and the certificate trust that the Object Explorer connection asked for.** Until this, indexing on every version and scripting on SSMS 20 set `TrustServerCertificate` regardless, so a login, password included, went to whatever answered, even with the tick box clear.

Consequences:

- On SSMS 18 and 22 the match with the Connect dialog is exact, since it is the same string for the same driver. That includes the keywords indexing never used to see (`Authentication`, `Host Name In Certificate`, `Application Intent`, an initial database). Not run against Entra authentication.
- On SSMS 20 it is a translation of two keywords, read by `ConnectionDescriptor.RequestsEncryption` and `TrustsServerCertificate`. Encryption is requested unless the string turns it off (a missing `Encrypt` counts as on), except on LocalDB, recognised by `(localdb)` or its pipe name but not through an alias. `Strict` becomes ordinary encryption that never trusts, because `System.Data.SqlClient` has no TDS 8; a server that forces strict encryption cannot be reached. Trust is `Trust Server Certificate` (SSMS 18 writes `TrustServerCertificate`; both are read).
- Nothing else crosses over on SSMS 20: no Entra authentication, no initial database, and no `Host Name In Certificate` or `Server Certificate`. A connection that needed one of those to validate is refused by SQL Pilot's driver, which checks the certificate against the server name as typed; the status bar then reads "Could not index ..." with the driver's message. Not tested.
- The two drivers differ when the client does not ask for encryption and the server forces it: `Microsoft.Data.SqlClient` (SSMS 20's and 22's own) still validates the certificate unless told to trust it, `System.Data.SqlClient` (SSMS 18, and SQL Pilot on SSMS 20) does not.
- **Query windows follow the same rule by a third route.** They connect through SSMS's own driver on every version, so `CreateUIConnectionInfo` passes on the four things that decide how the server is authenticated, under SSMS's own names: `ENCRYPT_CONNECTION` (the `Encrypt` value as written), `TRUST_SERVER_CERTIFICATE`, `HOST_NAME_IN_CERTIFICATE`, and `Server Certificate` through `OtherParams`. Until this it added `TrustServerCertificate=True` through `OtherParams` whatever the dialog said, and on SSMS 18 the window was not encrypted even when Object Explorer's connection was. Nothing else from the dialog reaches the window yet (timeouts, application intent, Entra authentication, other Additional Connection Parameters).
- Do not ask Object Explorer for its own `UIConnectionInfo`. `ConnectionCache.GetUIConnectionInfo(SqlOlapConnectionInfoBase)` (non-public, `ObjectExplorer.dll`, all three versions) looks like the way, but it opens a connection to the server as a side effect.
- So on SSMS 20 a server reached with `Host Name In Certificate` or `Server Certificate` and trust clear gets working query windows but no index and no scripts. The way out is for SQL Pilot to use SSMS 20's own SMO 17 there, which means a third build of `SqlPilot.Smo` and moving the SMO calls in `ScriptingBridge` behind it; not done.
- Any cast of an Object Explorer object to an SMO type (`RawConnectionInfo as SqlOlapConnectionInfoBase`) is null on SSMS 20. `DesignTable` relies on one, so **Design Table does nothing on SSMS 20** (checked on 20.2: no designer opens, no error). It works on SSMS 22. Not fixed: `ManagedConnectionWrapper` implements `IManagedConnection` against SMO 16's `SqlOlapConnectionInfoBase`, and SSMS 20's `ScriptFactory` wants the SMO 17 one.

To see a swallowed exception in a Release build, trace the CLR with ETW: `logman start x -p "{e13c0d23-ccbc-4e12-931b-d9cc2eee27e4}" 0x8000 2 -o x.etl -ets` (needs Performance Log Users), then read event 80 with `Get-WinEvent -Path x.etl -Oldest`. Keyword `0x8` level 4 lists assembly loads with their paths.

## Azure SQL constraints

Azure SQL Database (`*.database.windows.net`) differs from on-prem SQL Server in three load-bearing ways. Detect via `ConnectionDescriptor.IsAzureSqlServerName(serverName)`.

### SSMS strips the domain suffix from URNs

SSMS's internal Object Explorer URNs use the logical short name, not the full FQDN. `foo.database.windows.net` becomes `Server[@Name='foo']`. If you build a URN from `ConnectionDescriptor.ServerName` (which has the full FQDN), `IObjectExplorerService.FindNode` returns null and table-based actions (Edit Top N Rows, etc.) silently fall back.

**Fix**: walk the OE tree root nodes and extract each root's `Context` property (which carries the URN as SSMS stored it). Match by prefix against the logical server name, and use the extracted root server name when building URNs. See `ObjectExplorerBridge.GetCandidateUrnServerNames`: it always walks the roots and returns the best prefix match first, then the other roots, then the logical name, and the caller tries each in turn. There is no on-prem shortcut, because an on-prem logical name (a raw IP address, `HOST\INSTANCE`) can differ from the URN name too.

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
- **`-S <server>` asks first.** SSMS 22 shows a *"Connect to the following server?"* Yes/No
  prompt inside the main window before connecting; nothing is connected until it is answered.
  SSMS 18/20 (`-S <server> -E`) connect straight away. The prompt is a WinForms dialog whose
  `Yes`/`No` buttons reach UI Automation as `Pane` elements with no `InvokePattern`; post
  `BM_CLICK` (`0xF5`) to the button's `NativeWindowHandle` instead. Still true on 22.12.1.

### Driving a multi-server test through UI Automation

To get a second server into Object Explorer: `File > Connect Object Explorer...`. On SSMS 22
that opens a WPF `Connect` window (`Server Name` / `Authentication` / `User Name` / `Password`
elements, button id `OkButton`); on 18/20 it is the WinForms `Connect to Server` dialog, whose
`Authentication:` combo is a drop-down list (focus it and type the first letter) and whose
user field is labelled `Login:` once SQL authentication is selected. Two LocalDB instances
(`sqllocaldb create <name>`) make a cheap pair of servers, and LocalDB accepts SQL logins.
The pane itself exposes `IndexStatus`, `SearchTextBox`, `ResultsListBox`, `ScopeButton` and
`RefreshButton` as automation ids.

SSMS 22.9 and later: the `Connect` window is a child of the main window, and its inputs are
found by name: `Server Name` (combo box), `Authentication`, `User Name`, `Password`, `Encrypt`
(`Mandatory` / `Optional` / `Strict ...`), the `Trust Server Certificate` check box and
`Host Name In Certificate`. `AdvancedPropertiesButton` opens `Advanced Properties`, which
has `Server Certificate`, `Packet Size` and the rest. The second tab holds the whole thing as
`connectionStringTextBox`; Object Explorer's connections are `Pooling=False;Packet Size=4096`.
`ValuePattern.SetValue` works on all of them, the password box included. Leave the
connection-string tab for reading: a connection made by pasting a string there and pressing
`Apply` logged in once and never appeared in Object Explorer, with no error.

The text of a query window is `TextPattern` on the element of class `WpfTextView`, and its
tab's name says whether it is connected (`SQLQuery1.sql - server.database (login (spid))`).
Closing SSMS with an unsaved query raises a `Save` / `Don't Save` / `Cancel` window; a pending
connection error raises *"an operation is blocking user input ... shut down anyway?"* first.

### Testing encryption without a SQL Server

LocalDB cannot encrypt, and it hides encryption bugs: `Microsoft.Data.SqlClient` quietly
ignores `Encrypt` for it, `System.Data.SqlClient` fails outright. `tools/TlsFront` puts a
loopback TCP endpoint in front of a LocalDB instance that negotiates TDS 7.x encryption with
a throwaway self-signed certificate, then relays to the instance's pipe. To a client it is an
encrypted server whose certificate Windows does not trust, and it answers a client that does
not ask for encryption the way a server with Force Encryption does. No install, no elevation.

```powershell
sqllocaldb start SqlPilotB; sqllocaldb info SqlPilotB     # note the instance pipe name
dotnet run --project tools\TlsFront -c Release -- 14333 'LOCALDB#5F8A78B0\tsql\query' front.cer
```

Each connection is logged with what the client asked for, and a `LOGIN7` line naming the
program once the client has accepted the certificate and sent its login. A handshake with no
`LOGIN7` after it is a client that looked at the certificate and hung up, which is the
evidence that trust was checked. `TLSFRONT_TRACE=1` adds every packet.

- **Connect as `127.0.0.1,14333`, not `localhost,14333`.** SqlClient and ODBC replace
  `localhost` with the machine name and go to its network address, where the front does not
  listen (connection refused, or a timeout).
- **SQL logins only.** Windows authentication does not survive being relayed (*"Cannot
  generate SSPI context"*). LocalDB accepts SQL logins.
- **LocalDB stops when idle and comes back under a new pipe name.** Everything then fails with
  *"An existing connection was forcibly closed by the remote host"*; restart the front with
  the new name.
- **A certificate that validates**, two ways. The front writes its certificate (no key) to
  the file named last on its command line. Either point the client at it with
  `Server Certificate=<file>` under Additional Connection Parameters, which
  `Microsoft.Data.SqlClient` matches exactly without consulting Windows (SSMS 20 and 22's own
  connections; not `System.Data.SqlClient`), or import it into Trusted Root Certification
  Authorities for the test and remove it afterwards. It is named `127.0.0.1`, is not a CA,
  and is new on every start.
- TDS 8 (`Encrypt=Strict`) is not handled.
- **If you write another relay: one TLS record per TDS packet**, as SQL Server sends them.
  The client's native SNI sizes its buffer by the packet size, and a record holding more
  kills the client process outright: fast-fail `0xc0000409` in
  `Microsoft.Data.SqlClient.SNI.x64.dll`, no managed exception. It took SSMS 22 down twice
  before the front learned this. SSMS asks for a packet size of 4096, which two packets in
  one record exceed; SqlClient's default of 8000 happened not to show it.

To run SQL Pilot's connection code against one SSMS version's assemblies without the IDE,
load `SqlPilot.Smo.dll` in a console app with an `AssemblyResolve` handler that looks in that
SSMS's `Common7\IDE` (plus `PublicAssemblies`, where SSMS 22 keeps `System.Text.Json`, and
`Extensions\Application` on SSMS 20), make a
`SqlConnectionInfo` of SSMS's own SMO to stand in for Object Explorer's, and call
`CreateServerConnection`. For SSMS 20 load both SMO copies, as it does. The query-window
connection can be had the same way: `CreateUIConnectionInfo`, then
`UIConnectionInfoUtil.GetCoreConnectionInfo`, then connect with SSMS's own `ServerConnection`.

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

## Following the SSMS theme

`VSColorTheme.GetThemedColor(EnvironmentColors.…)` and `VSColorTheme.ThemeChanged` work in all three versions, including the VS 2017 shell in SSMS 18/20 — the `EnvironmentColors`, `CommonControlsColors` and `InfoBarColors` keys resolve there to the Blue theme's values. `SsmsThemeAdapter` is the only consumer; see `docs/ARCHITECTURE.md` § Theming.

- **A tool window gets nothing for free.** WPF content hosted in a `ToolWindowPane` is not given themed brushes or implicit styles; unresolved `DynamicResource` keys just leave the property unset (a transparent background, black text), which looks fine in a light theme and unreadable in a dark one.
- **Stock WPF templates hard-code light colours**: button hover/pressed fills, and the icon gutter in the `ContextMenu` template. Setting `Background`/`Foreground` is not enough for those two; they need their own templates. `ListBoxItem` selection is translucent and survives a dark background as it is.
- **`TreeView` and `TreeViewItem` set their own `Foreground`**, so text inside a tree does not inherit the host's colour.
- **SSMS 22 themes** are under `Tools > Theme` and switch live: Light, Dark and a set of tinted ones (Cool Breeze, Moonlight Glow, …). There is no "Blue" any more. SSMS 18/20 offer Blue and Light only.
- **Scroll bars**: `VsResourceKeys.ScrollBarStyleKey` resolves through `Application.Current.TryFindResource` in the VS 2017 shell (SSMS 18/20) as well as SSMS 22. Put on the hosted control as the implicit `ScrollBar` style it gives the results list and the Scope tree the shell's own scroll bar, and it follows a live theme switch without being reassigned. Checked on SSMS 22 (Dark, Light, Cool Breeze) and SSMS 20; not run on 18, which shares 20's shell.
- **Testing a scrolling list**: on a large monitor 30 results fit in a docked pane without scrolling. Shrink the SSMS window, or open the Scope panel with a few dozen databases connected, before concluding anything about scroll bars.
- **Testing the update bar**: a dev build is version 1.0.0, older than any release, so it shows the bar whenever the update check actually runs. The check is throttled to once per 24 hours; blank the `LastUpdateCheckUtc|` line in `%AppData%\SqlPilot\settings.json` (keep the file BOM-less) and restart SSMS.

## Debug Logging

`Debug.WriteLine` from an extension goes to... nowhere useful by default. For real diagnostics:

- Write to a file on Desktop (temporary — **never** ship this)
- Use `IVsActivityLog` (from `SVsActivityLog` service) — appears in SSMS's `ActivityLog.xml` at `%AppData%\Microsoft\SSMS\<ver>\` when launched with `-log`

For user-visible messages, use the SQL Pilot status bar (`IndexStatus.Text`).

## Things We Tried That Don't Work

- **`Process.Modules` as a "did the extension load?" check** — managed assemblies loaded by a package don't reliably appear there; it reported "not loaded" for an extension that was demonstrably running. Drive the real UI instead: send Ctrl+D and look for the `SQL Pilot` pane through UI Automation — after first confirming the window you attached to is the IDE and not a dialog (see *SSMS 22's command line*).
- **`FindChildOfType<TreeView>(oeControl)` to locate the Object Explorer tree** — `Tree` *is* the `TreeView` and has no child controls, so the search returns null without any error. Server enumeration then quietly fell back to `GetSelectedNodes`, which looks fine with one server and breaks with two. Cast `Tree` directly.
- **`IObjectExplorerService.GetNodeInformation(treeNode)` via reflection** to get a root's connection — it sat behind the lookup above and never ran. Use `(root as IServiceProvider).GetService(INodeInformation)`.
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
