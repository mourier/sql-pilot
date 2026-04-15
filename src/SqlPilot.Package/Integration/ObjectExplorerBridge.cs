using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SqlPilot.Core.Database;

namespace SqlPilot.Package.Integration
{
    public sealed class ObjectExplorerBridge
    {
        private readonly IServiceProvider _serviceProvider;
        private object _objectExplorerService;
        private Type _oeServiceType;
        private readonly Dictionary<string, ConnectionDescriptor> _connectionCache =
            new Dictionary<string, ConnectionDescriptor>(StringComparer.OrdinalIgnoreCase);

        public ObjectExplorerBridge(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        private object GetObjectExplorerService()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_objectExplorerService != null)
                return _objectExplorerService;

            if (_oeServiceType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _oeServiceType = assembly.GetType(
                        "Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.IObjectExplorerService");
                    if (_oeServiceType != null) break;
                }
            }

            if (_oeServiceType != null)
                _objectExplorerService = _serviceProvider.GetService(_oeServiceType);

            return _objectExplorerService;
        }

        public object GetObjectExplorerServicePublic()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetObjectExplorerService();
        }

        private object GetObjectExplorerControl()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var oes = GetObjectExplorerService();
            if (oes == null) return null;

            var treeProp = oes.GetType().GetProperty("Tree",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            return treeProp?.GetValue(oes);
        }

        private TreeView FindTreeView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var oeControl = GetObjectExplorerControl();
            if (oeControl is Control control)
                return FindChildOfType<TreeView>(control);

            return null;
        }

        private static T FindChildOfType<T>(Control parent) where T : Control
        {
            foreach (Control child in parent.Controls)
            {
                if (child is T match) return match;
                var found = FindChildOfType<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        public IReadOnlyList<string> GetConnectedServerNames()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var results = new List<string>();

            // Try WinForms TreeView first
            var treeView = FindTreeView();
            if (treeView != null)
            {
                foreach (TreeNode node in treeView.Nodes)
                {
                    string serverName = TryGetServerFromNode(node);
                    if (string.IsNullOrEmpty(serverName))
                        serverName = ParseServerNameFromText(node.Text);
                    if (!string.IsNullOrEmpty(serverName))
                        results.Add(serverName);
                }
            }
            else
            {
                // Fallback: use GetSelectedNodes API
                TryGetFromSelectedNodes(results);
            }

            return results;
        }

        public ConnectionDescriptor GetConnectionInfo(string serverName)
        {
            _connectionCache.TryGetValue(serverName, out var conn);
            return conn;
        }

        /// <summary>
        /// Walks OE root nodes to find the URN-style server name that matches
        /// the given logical server name. SSMS strips Azure SQL domain suffixes in URNs
        /// (e.g. "foo.database.windows.net" → "foo"), and for on-prem the logical name
        /// used in the SqlPilot index (raw IP address or "HOST\INSTANCE" syntax) may
        /// not exactly match what OE stored in its URN. Always walk OE; fall back to
        /// the logical name only if no matching root node is found.
        /// </summary>
        public string ResolveUrnServerName(string logicalServerName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var oes = GetObjectExplorerService();
            if (oes == null) return logicalServerName;

            var oeControl = GetObjectExplorerControl();
            if (!(oeControl is TreeView treeView)) return logicalServerName;

            var inodeInfoType = ReflectionTypeCache.FindType("Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.INodeInformation");
            if (inodeInfoType == null) return logicalServerName;

            foreach (TreeNode root in treeView.Nodes)
            {
                if (!(root is IServiceProvider sp)) continue;

                var rootNodeInfo = sp.GetService(inodeInfoType);
                var ctx = rootNodeInfo?.GetType().GetProperty("Context")?.GetValue(rootNodeInfo)?.ToString();
                if (string.IsNullOrEmpty(ctx) || !ctx.StartsWith("Server[@Name='")) continue;

                int start = "Server[@Name='".Length;
                int end = ctx.IndexOf('\'', start);
                if (end <= start) continue;

                string rootServer = ctx.Substring(start, end - start);
                if (logicalServerName.StartsWith(rootServer, StringComparison.OrdinalIgnoreCase)
                    || rootServer.StartsWith(logicalServerName, StringComparison.OrdinalIgnoreCase))
                {
                    return rootServer;
                }
            }

            return logicalServerName;
        }

        /// <summary>
        /// Returns all URN-style server names currently shown as roots in Object Explorer,
        /// with the best prefix-match candidate listed first. Callers that need to
        /// resolve an object URN (e.g. for <c>FindNode</c>) should try each in order
        /// until one succeeds. This handles the IP-vs-hostname case where the logical
        /// connection name (a raw IP address) doesn't match the URN name OE registered
        /// (the resolved hostname).
        /// </summary>
        public System.Collections.Generic.List<string> GetCandidateUrnServerNames(string logicalServerName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var candidates = new System.Collections.Generic.List<string>();
            var oes = GetObjectExplorerService();
            if (oes == null) { candidates.Add(logicalServerName); return candidates; }

            var oeControl = GetObjectExplorerControl();
            if (!(oeControl is TreeView treeView)) { candidates.Add(logicalServerName); return candidates; }

            var inodeInfoType = ReflectionTypeCache.FindType("Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.INodeInformation");
            if (inodeInfoType == null) { candidates.Add(logicalServerName); return candidates; }

            string bestMatch = null;
            foreach (TreeNode root in treeView.Nodes)
            {
                if (!(root is IServiceProvider sp)) continue;
                var rootNodeInfo = sp.GetService(inodeInfoType);
                var ctx = rootNodeInfo?.GetType().GetProperty("Context")?.GetValue(rootNodeInfo)?.ToString();
                if (string.IsNullOrEmpty(ctx) || !ctx.StartsWith("Server[@Name='")) continue;

                int start = "Server[@Name='".Length;
                int end = ctx.IndexOf('\'', start);
                if (end <= start) continue;

                string rootServer = ctx.Substring(start, end - start);
                if (bestMatch == null &&
                    (logicalServerName.StartsWith(rootServer, StringComparison.OrdinalIgnoreCase)
                     || rootServer.StartsWith(logicalServerName, StringComparison.OrdinalIgnoreCase)))
                {
                    bestMatch = rootServer;
                }
                else
                {
                    candidates.Add(rootServer);
                }
            }

            if (bestMatch != null) candidates.Insert(0, bestMatch);
            bool hasLogical = false;
            foreach (var c in candidates)
                if (string.Equals(c, logicalServerName, StringComparison.OrdinalIgnoreCase)) { hasLogical = true; break; }
            if (!hasLogical) candidates.Add(logicalServerName);

            return candidates;
        }

        private string TryGetServerFromNode(TreeNode node)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var oes = GetObjectExplorerService();
                if (oes == null) return null;

                var getNodeInfoMethod = oes.GetType().GetMethod("GetNodeInformation",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getNodeInfoMethod == null) return null;

                var nodeInfo = getNodeInfoMethod.Invoke(oes, new object[] { node });
                if (nodeInfo == null) return null;

                return ExtractConnectionFromNodeInfo(nodeInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot: GetNodeInformation error: {ex.Message}");
                return null;
            }
        }

        private void TryGetFromSelectedNodes(List<string> results)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var oes = GetObjectExplorerService();
                if (oes == null) return;

                var method = oes.GetType().GetMethod("GetSelectedNodes",
                    BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return;

                var args = new object[] { 0, null };
                method.Invoke(oes, args);
                var nodes = args[1] as Array;
                if (nodes == null) return;

                foreach (var node in nodes)
                {
                    var name = ExtractConnectionFromNodeInfo(node);
                    if (!string.IsNullOrEmpty(name))
                        results.Add(name);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot: GetSelectedNodes error: {ex.Message}");
            }
        }

        private string ExtractConnectionFromNodeInfo(object nodeInfo)
        {
            var connProp = nodeInfo.GetType().GetProperty("Connection");
            if (connProp == null) return null;

            var conn = connProp.GetValue(nodeInfo);
            if (conn == null) return null;

            var serverName = conn.GetType().GetProperty("ServerName")?.GetValue(conn)?.ToString();
            if (string.IsNullOrEmpty(serverName)) return null;

            // Build a structured ConnectionDescriptor instead of passing raw object
            var descriptor = new ConnectionDescriptor
            {
                ServerName = serverName,
                ConnectionString = conn.GetType().GetProperty("ConnectionString")?.GetValue(conn)?.ToString(),
                UserName = conn.GetType().GetProperty("UserName")?.GetValue(conn)?.ToString(),
                Password = conn.GetType().GetProperty("Password")?.GetValue(conn)?.ToString(),
            };

            var useIntegrated = conn.GetType().GetProperty("UseIntegratedSecurity")?.GetValue(conn);
            descriptor.UseIntegratedSecurity = useIntegrated is bool b && b;
            descriptor.RawConnectionInfo = conn;

            _connectionCache[serverName] = descriptor;
            return serverName;
        }

        private static string ParseServerNameFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int parenIdx = text.IndexOf(" (", StringComparison.Ordinal);
            return parenIdx > 0 ? text.Substring(0, parenIdx).Trim() : text.Trim();
        }

        public bool NavigateToObject(string serverName, string databaseName, string schemaName, string objectName, string objectType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // TODO: implement using FindNode + SynchronizeTree
            return false;
        }
    }
}
