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

        private const string NodeInformationTypeName =
            "Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.INodeInformation";

        /// <summary>
        /// Every server connected in Object Explorer, whichever node is selected.
        /// Also refreshes the cached connection for each one.
        /// </summary>
        public IReadOnlyList<string> GetConnectedServerNames()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var results = new List<string>();

            // Tree IS the TreeView (ObjectExplorerControl derives from it) -- it has no
            // child controls, so searching its Controls for one finds nothing.
            if (GetObjectExplorerControl() is TreeView treeView)
            {
                var nodeInfoType = ReflectionTypeCache.FindType(NodeInformationTypeName);

                foreach (TreeNode root in treeView.Nodes)
                {
                    // A root we can't read a connection from is skipped rather than
                    // listed by its node text: with no connection it could only be
                    // indexed as an empty server.
                    string serverName = TryGetServerFromRootNode(root, nodeInfoType);
                    if (!string.IsNullOrEmpty(serverName) && !ContainsServer(results, serverName))
                        results.Add(serverName);
                }
            }

            // Last resort: only sees the nodes the user has selected.
            if (results.Count == 0)
                TryGetFromSelectedNodes(results);

            return results;
        }

        private static bool ContainsServer(List<string> servers, string serverName)
        {
            foreach (var s in servers)
                if (string.Equals(s, serverName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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

            var inodeInfoType = ReflectionTypeCache.FindType(NodeInformationTypeName);
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

            var inodeInfoType = ReflectionTypeCache.FindType(NodeInformationTypeName);
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

        private string TryGetServerFromRootNode(TreeNode root, Type nodeInfoType)
        {
            try
            {
                // OE tree nodes hand out their INodeInformation as a service.
                if (nodeInfoType == null || !(root is IServiceProvider sp)) return null;

                var nodeInfo = sp.GetService(nodeInfoType);
                return nodeInfo == null ? null : ExtractConnectionFromNodeInfo(nodeInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot: root node connection error: {ex.Message}");
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

        public bool NavigateToObject(string serverName, string databaseName, string schemaName, string objectName, string objectType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // TODO: implement using FindNode + SynchronizeTree
            return false;
        }
    }
}
