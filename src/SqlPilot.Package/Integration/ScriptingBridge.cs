using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.VisualStudio.Shell;
using SqlPilot.Core.Database;

namespace SqlPilot.Package.Integration
{
    public sealed class ScriptingBridge
    {
        // SSMS's SQL Server type GUID, used when creating a UIConnectionInfo for query windows.
        private static readonly Guid SqlServerTypeGuid = new Guid("8c91a03d-f9b4-46c0-a305-b5dcc79ff907");

        private readonly IServiceProvider _serviceProvider;
        private readonly SqlPilotPackage _package;

        public ScriptingBridge(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _package = serviceProvider as SqlPilotPackage;
        }

        private Server GetServer(DatabaseObject obj)
        {
            var connInfo = _package?.ObjectExplorerBridge?.GetConnectionInfo(obj.ServerName);
            if (connInfo?.ConnectionString == null) return null;
            var serverConn = new ServerConnection();
            serverConn.ConnectionString = SanitizeConnectionString(connInfo.ConnectionString);
            serverConn.TrustServerCertificate = true;
            return new Server(serverConn);
        }

        /// <summary>
        /// SSMS connection strings occasionally contain keywords that the underlying
        /// <see cref="System.Data.SqlClient.SqlConnectionStringBuilder"/> doesn't
        /// recognise (e.g. SSMS 20 emits "multiple active result sets" with spaces,
        /// which SqlClient rejects with <c>ArgumentException: Keyword not supported</c>
        /// and takes the whole SMO call down with it). Parse each keyword segment
        /// individually and drop anything the builder can't accept.
        /// </summary>
        private static string SanitizeConnectionString(string connStr)
        {
            if (string.IsNullOrEmpty(connStr)) return connStr;
            var sb = new StringBuilder();
            foreach (var segment in connStr.Split(';'))
            {
                var trimmed = segment.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                try
                {
                    new System.Data.SqlClient.SqlConnectionStringBuilder().ConnectionString = trimmed;
                    if (sb.Length > 0) sb.Append(';');
                    sb.Append(trimmed);
                }
                catch { /* keyword SqlClient doesn't know — drop it */ }
            }
            return sb.ToString();
        }

        private SqlConnectionInfo GetSqlConnectionInfo(DatabaseObject obj)
        {
            var connDesc = _package?.ObjectExplorerBridge?.GetConnectionInfo(obj.ServerName);
            if (connDesc == null) return null;
            var connInfo = new SqlConnectionInfo();
            connInfo.ServerName = connDesc.ServerName;
            if (!connDesc.UseIntegratedSecurity)
            {
                connInfo.UseIntegratedSecurity = false;
                connInfo.UserName = connDesc.UserName;
                connInfo.Password = connDesc.Password;
            }
            return connInfo;
        }

        /// <summary>
        /// Azure SQL doesn't support 3-part names — returns the 2-part qualified name for Azure,
        /// or a 3-part name with database prefix for on-prem.
        /// </summary>
        private static string BuildTableReference(DatabaseObject obj)
        {
            return ConnectionDescriptor.IsAzureSqlServerName(obj.ServerName)
                ? obj.QualifiedName
                : $"[{obj.DatabaseName}].{obj.QualifiedName}";
        }

        public void ScriptSelectTopN(DatabaseObject obj, int topN = 100)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = _serviceProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;

            // Modern (SSMS 22, VS Shell 17+): arm the DocumentOpened handler before
            // creating the doc so Query.Execute fires reactively once the connection
            // is wired.
            if (_isModernSsms && dte != null) ArmAutoExecuteOnNextDocument(dte);

            CreateSqlDocument($"SELECT TOP {topN} *\r\nFROM {BuildTableReference(obj)}", obj);

            // Legacy (SSMS 18/20, VS Shell 15): DTE's Query.Execute is a silent
            // no-op on programmatically-created query windows even when
            // IsAvailable=true. Mirror Hunting Dog: send F5 to whatever has focus
            // right after CreateSqlDocument returns (which is the new window).
            if (!_isModernSsms)
            {
                try { System.Windows.Forms.SendKeys.Send("{F5}"); }
                catch (Exception ex) { Debug.WriteLine($"SqlPilot legacy SendKeys: {ex.Message}"); }
            }
        }

        // VSSDK Shell assembly major version: v15.x = SSMS 18/20, v17.x+ = SSMS 22+.
        // DTE.Version is unreliable here — SSMS 18 reports "2019.0150".
        private static readonly bool _isModernSsms = ComputeIsModernSsms();

        private static bool ComputeIsModernSsms()
        {
            try
            {
                var shellVer = typeof(Microsoft.VisualStudio.Shell.AsyncPackage).Assembly.GetName().Version;
                return shellVer != null && shellVer.Major >= 17;
            }
            catch { return false; }
        }

        /// <summary>
        /// Subscribes a one-shot handler to <c>DocumentEvents.DocumentOpened</c> that
        /// fires <c>Query.Execute</c> when the next document opens. Used to auto-run
        /// the SELECT TOP N query created by <see cref="ScriptSelectTopN"/>.
        /// Reacting to the actual open event is more robust than guessing a fixed
        /// delay — SSMS wires the connection on a background thread after the editor
        /// surfaces. A safety auto-unsubscribe fires after a short window in case no
        /// document opens (e.g. ScriptFactory failed silently).
        /// </summary>
        private void ArmAutoExecuteOnNextDocument(EnvDTE80.DTE2 dte)
        {
            try
            {
                var docEvents = dte.Events.DocumentEvents;
                EnvDTE._dispDocumentEvents_DocumentOpenedEventHandler handler = null;
                handler = (EnvDTE.Document doc) =>
                {
                    try { docEvents.DocumentOpened -= handler; } catch { }
                    // Activate + Execute must happen OUTSIDE this event handler —
                    // SSMS 18 throws "This method or property cannot be called within
                    // an event handler" for Activate(). Dispatch both together.
                    DispatchActivateAndExecute(dte, doc);
                };
                docEvents.DocumentOpened += handler;

                // Safety: if no document opens within 2s, drop the subscription so
                // a later unrelated DocumentOpened doesn't accidentally fire Execute.
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                dispatcher?.BeginInvoke(new Action(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(2000);
                    try { docEvents.DocumentOpened -= handler; } catch { }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex) { Debug.WriteLine($"SqlPilot ArmAutoExecute: {ex.Message}"); }
        }

        private void DispatchActivateAndExecute(EnvDTE80.DTE2 dte, EnvDTE.Document doc)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            _ = dispatcher.BeginInvoke(new Action(async () =>
            {
                try { doc?.Activate(); } catch { }

                // Poll Query.Execute.IsAvailable — SSMS reports the document as
                // opened before its connection is actually wired, so an immediate
                // ExecuteCommand is a silent no-op. Wait up to 3s for availability.
                EnvDTE.Command cmd = null;
                try { cmd = dte.Commands.Item("Query.Execute", 0); } catch { }

                for (int i = 0; i < 15; i++)
                {
                    bool available = false;
                    try { available = cmd?.IsAvailable == true; } catch { }
                    if (available) break;
                    await System.Threading.Tasks.Task.Delay(200);
                }

                try { dte.ExecuteCommand("Query.Execute"); }
                catch (Exception ex) { Debug.WriteLine($"SqlPilot Query.Execute: {ex.Message}"); }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Opens SSMS's "Edit Top N Rows" editable grid via OpenTableHelperClass.EditTopNRows.
        /// This is the same entry point SSMS uses for its own right-click menu — the
        /// ScriptFactory.DesignTableOrView(DocumentType.OpenTable) path is broken
        /// (GetDsRef throws ArgumentException) on all SSMS versions.
        /// </summary>
        public void EditTableData(DatabaseObject obj, int topN = 200)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var oeBridge = _package?.ObjectExplorerBridge;
                var oeService = oeBridge?.GetObjectExplorerServicePublic();
                if (oeService == null) goto fallback;

                var findNodeMethod = oeService.GetType().GetMethod("FindNode", BindingFlags.Public | BindingFlags.Instance);
                if (findNodeMethod == null) goto fallback;

                // Try each candidate server name from OE until one resolves.
                // Handles IP-vs-hostname mismatches (connecting via a raw IP
                // while OE stored the resolved hostname) by enumerating every
                // OE root URN.
                object nodeInfo = null;
                foreach (var serverName in oeBridge.GetCandidateUrnServerNames(obj.ServerName))
                {
                    string candidateUrn = $"Server[@Name='{serverName}']/Database[@Name='{obj.DatabaseName}']/Table[@Name='{obj.ObjectName}' and @Schema='{obj.SchemaName}']";
                    nodeInfo = findNodeMethod.Invoke(oeService, new object[] { candidateUrn });
                    if (nodeInfo != null) break;
                }
                if (nodeInfo == null) goto fallback;

                // Set DatabaseName on the node's Connection so EditTopNRows reuses
                // the existing OE connection instead of prompting for a new one.
                var nodeConn = nodeInfo.GetType().GetProperty("Connection")?.GetValue(nodeInfo);
                nodeConn?.GetType().GetProperty("DatabaseName")?.SetValue(nodeConn, obj.DatabaseName);

                var helperType = FindType("Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.OpenTableHelperClass");
                var editMethod = helperType?.GetMethod("EditTopNRows", BindingFlags.Static | BindingFlags.Public);
                if (editMethod == null) goto fallback;

                editMethod.Invoke(null, new object[] { nodeInfo, topN });
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot EditTableData: {ex.InnerException?.Message ?? ex.Message}");
            }

            fallback:
            CreateSqlDocument($"SELECT TOP {topN} *\r\nFROM {BuildTableReference(obj)}", obj);
        }


        public void DesignTable(DatabaseObject obj)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var server = GetServer(obj);
                var table = server?.Databases[obj.DatabaseName]?.Tables[obj.ObjectName, obj.SchemaName];
                if (table == null) return;

                var scriptFactory = GetServiceCacheScriptFactory();
                if (scriptFactory == null) return;

                var connDesc = _package?.ObjectExplorerBridge?.GetConnectionInfo(obj.ServerName);
                var rawConn = connDesc?.RawConnectionInfo as SqlOlapConnectionInfoBase;
                if (rawConn == null) return;

                var mc = new ManagedConnectionWrapper { Connection = rawConn };

                var docType = FindEnumValue("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.DocumentType", 6);
                var docOpts = FindEnumValue("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.DocumentOptions", 2);
                if (docType == null || docOpts == null) return;

                var designMethod = scriptFactory.GetType().GetMethod("DesignTableOrView", BindingFlags.Public | BindingFlags.Instance);
                designMethod?.Invoke(scriptFactory, new object[] { docType, docOpts, table.Urn.ToString(), mc });
            }
            catch (Exception ex) { Debug.WriteLine($"SqlPilot DesignTable: {ex.InnerException?.Message ?? ex.Message}"); }
        }

        public void ScriptAlter(DatabaseObject obj)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var script = GetModuleScript(obj, alterHeader: true);
            CreateSqlDocument(script ?? $"-- Could not retrieve definition for {obj.QualifiedName}", obj);
        }

        /// <summary>
        /// Fetches the body of a procedure/view/function with either CREATE or ALTER header.
        /// Returns null if the object doesn't exist or isn't a scriptable module type.
        /// </summary>
        private string GetModuleScript(DatabaseObject obj, bool alterHeader)
        {
            try
            {
                var server = GetServer(obj);
                var db = server?.Databases[obj.DatabaseName];
                if (db == null) return null;

                switch (obj.ObjectType)
                {
                    case DatabaseObjectType.StoredProcedure:
                        var sp = db.StoredProcedures[obj.ObjectName, obj.SchemaName];
                        if (sp == null) return null;
                        sp.Refresh();
                        return sp.ScriptHeader(alterHeader) + sp.TextBody;

                    case DatabaseObjectType.View:
                        var vw = db.Views[obj.ObjectName, obj.SchemaName];
                        if (vw == null) return null;
                        vw.Refresh();
                        return vw.ScriptHeader(alterHeader) + vw.TextBody;

                    case DatabaseObjectType.ScalarFunction:
                    case DatabaseObjectType.TableValuedFunction:
                        var fn = db.UserDefinedFunctions[obj.ObjectName, obj.SchemaName];
                        if (fn == null) return null;
                        fn.Refresh();
                        return fn.ScriptHeader(alterHeader) + fn.TextBody;

                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot GetModuleScript {obj.QualifiedName}: {ex.Message}");
                return null;
            }
        }

        public void ScriptExecute(DatabaseObject obj)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var script = BuildExecuteScript(obj) ?? $"EXEC {obj.QualifiedName}";
            CreateSqlDocument(script, obj);
        }

        private string BuildExecuteScript(DatabaseObject obj)
        {
            try
            {
                var db = GetServer(obj)?.Databases[obj.DatabaseName];
                if (db == null) return null;

                if (obj.ObjectType == DatabaseObjectType.StoredProcedure)
                {
                    var sp = db.StoredProcedures[obj.ObjectName, obj.SchemaName];
                    if (sp == null) return null;
                    sp.Refresh();
                    sp.Parameters.Refresh(true);

                    var sb = new StringBuilder();
                    sb.AppendLine($"EXECUTE {obj.QualifiedName}");
                    for (int i = 0; i < sp.Parameters.Count; i++)
                    {
                        var p = sp.Parameters[i];
                        string comma = i > 0 ? "," : " ";
                        string def = !string.IsNullOrEmpty(p.DefaultValue) ? $" -- default: {p.DefaultValue}" : "";
                        sb.AppendLine($"\t{comma}{p.Name} = NULL{def} -- {p.DataType.Name}");
                    }
                    return sb.ToString();
                }

                var fn = db.UserDefinedFunctions[obj.ObjectName, obj.SchemaName];
                if (fn == null) return null;
                fn.Refresh();
                fn.Parameters.Refresh(true);

                string prefix = fn.FunctionType == UserDefinedFunctionType.Scalar ? "SELECT" : "SELECT * FROM";
                var pars = new StringBuilder();
                for (int i = 0; i < fn.Parameters.Count; i++)
                {
                    if (i > 0) pars.Append(", ");
                    pars.Append("NULL");
                }
                return $"{prefix} {obj.QualifiedName}({pars})";
            }
            catch
            {
                return null;
            }
        }

        public void ScriptCreate(DatabaseObject obj)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (obj.ObjectType == DatabaseObjectType.Table)
            {
                var tableScript = GetTableScript(obj);
                if (tableScript != null)
                {
                    CreateSqlDocument(tableScript, obj);
                    return;
                }
            }
            else
            {
                var script = GetModuleScript(obj, alterHeader: false);
                if (script != null)
                {
                    CreateSqlDocument(script, obj);
                    return;
                }
            }

            CreateSqlDocument($"-- Could not retrieve definition for {obj.QualifiedName}", obj);
        }

        private string GetTableScript(DatabaseObject obj)
        {
            try
            {
                var server = GetServer(obj);
                var table = server?.Databases[obj.DatabaseName]?.Tables[obj.ObjectName, obj.SchemaName];
                if (table == null) return null;

                var scripter = new Scripter(server);
                scripter.Options.ScriptDrops = false;
                scripter.Options.WithDependencies = false;
                scripter.Options.Indexes = true;
                scripter.Options.DriAllConstraints = true;
                scripter.Options.DriDefaults = true;
                scripter.Options.NoCollation = true;

                var sb = new StringBuilder();
                foreach (string s in scripter.Script(new Urn[] { table.Urn }))
                {
                    sb.AppendLine(s);
                    sb.AppendLine("GO");
                }
                return sb.ToString().Trim();
            }
            catch
            {
                return null;
            }
        }

        private void CreateSqlDocument(string sqlText, DatabaseObject obj)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            bool isAzure = ConnectionDescriptor.IsAzureSqlServerName(obj.ServerName);
            // Azure SQL doesn't support USE [db] — must connect directly to the target db
            string fullScript = isAzure
                ? sqlText
                : $"USE [{obj.DatabaseName}]\r\nGO\r\n\r\n{sqlText}";

            try
            {
                var scriptFactory = GetServiceCacheScriptFactory();
                if (scriptFactory != null)
                {
                    var connInfo = GetSqlConnectionInfo(obj);
                    var uiConn = CreateUIConnectionInfo(connInfo);
                    if (uiConn != null)
                    {
                        // For Azure SQL, set the target database on the connection
                        // via AdvancedOptions["DATABASE"] so the query window opens in
                        // the right database (Azure SQL can't USE to switch).
                        if (isAzure)
                        {
                            try
                            {
                                var advProp = uiConn.GetType().GetProperty("AdvancedOptions");
                                var adv = advProp?.GetValue(uiConn);
                                if (adv != null)
                                {
                                    // AdvancedOptions is an IDictionary<string, string>
                                    var indexer = adv.GetType().GetProperty("Item", new[] { typeof(string) });
                                    indexer?.SetValue(adv, obj.DatabaseName, new object[] { "DATABASE" });
                                }
                            }
                            catch (Exception ex) { Debug.WriteLine($"SqlPilot set AdvancedOptions[DATABASE]: {ex.Message}"); }
                        }

                        var scriptType = FindEnumValue("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptType", 0);
                        var createMethod = scriptFactory.GetType().GetMethod("CreateNewBlankScript",
                            new[] { scriptType.GetType(), uiConn.GetType(), typeof(System.Data.IDbConnection) });
                        if (createMethod != null)
                        {
                            createMethod.Invoke(scriptFactory, new object[] { scriptType, uiConn, null });
                            InsertTextInActiveDocument(fullScript);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"SqlPilot CreateSqlDocument (ScriptFactory path): {ex.InnerException?.Message ?? ex.Message}"); }

            var dte = _serviceProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            if (dte == null) return;
            dte.ItemOperations.NewFile("General\\Sql File", "", EnvDTE.Constants.vsViewKindTextView);
            InsertTextInActiveDocument(fullScript);
        }

        private void InsertTextInActiveDocument(string text)
        {
            var dte = _serviceProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            if (dte?.ActiveDocument?.Object("TextDocument") is EnvDTE.TextDocument doc)
                doc.EndPoint.CreateEditPoint().Insert(text);
        }

        private static object GetServiceCacheScriptFactory()
        {
            try
            {
                var scType = FindType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
                return scType?.GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot GetServiceCacheScriptFactory: {ex.Message}");
                return null;
            }
        }

        private static object CreateUIConnectionInfo(SqlConnectionInfo connInfo)
        {
            if (connInfo == null) return null;
            try
            {
                var uiType = FindType("Microsoft.SqlServer.Management.Smo.RegSvrEnum.UIConnectionInfo");
                if (uiType == null) return null;
                var uiConn = Activator.CreateInstance(uiType);
                uiType.GetProperty("ServerName")?.SetValue(uiConn, connInfo.ServerName);
                uiType.GetProperty("UserName")?.SetValue(uiConn, connInfo.UserName);
                uiType.GetProperty("Password")?.SetValue(uiConn, connInfo.Password);
                uiType.GetProperty("PersistPassword")?.SetValue(uiConn, true);
                uiType.GetProperty("ApplicationName")?.SetValue(uiConn, "SQL Pilot");
                uiType.GetProperty("ServerType")?.SetValue(uiConn, SqlServerTypeGuid);
                uiType.GetProperty("AuthenticationType")?.SetValue(uiConn, connInfo.UseIntegratedSecurity ? 0 : 1);

                // SQL Server 2022+ ships with Encrypt=Mandatory by default. Without
                // TrustServerCertificate=True, on-prem servers with self-signed certs
                // fail with "certificate chain issued by untrusted authority" in SSMS 22.
                // AdvancedOptions is a NameValueCollection whose entries are merged into
                // the resulting connection string; OtherParams is appended raw. Set both
                // so whichever path SSMS uses picks it up.
                try
                {
                    var advProp = uiType.GetProperty("AdvancedOptions");
                    var adv = advProp?.GetValue(uiConn);
                    if (adv != null)
                    {
                        var indexer = adv.GetType().GetProperty("Item", new[] { typeof(string) });
                        indexer?.SetValue(adv, "True", new object[] { "TrustServerCertificate" });
                    }
                    uiType.GetProperty("OtherParams")?.SetValue(uiConn, "TrustServerCertificate=True");
                }
                catch (Exception ex) { Debug.WriteLine($"SqlPilot set TrustServerCertificate: {ex.Message}"); }

                return uiConn;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot CreateUIConnectionInfo: {ex.Message}");
                return null;
            }
        }

        private static object FindEnumValue(string enumTypeName, int value)
        {
            var enumType = FindType(enumTypeName);
            return enumType != null ? Enum.ToObject(enumType, value) : null;
        }

        private static Type FindType(string typeName) => ReflectionTypeCache.FindType(typeName);
    }
}
