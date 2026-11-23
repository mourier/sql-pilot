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
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // A root we can't read a connection from is skipped rather than listed by
            // its node text: with no connection it could only be indexed as an empty server.
            foreach (var nodeInfo in GetRootNodeInfos())
                AddServer(nodeInfo, results, seen);

            // Last resort: only sees the nodes the user has selected.
            if (results.Count == 0)
                TryGetFromSelectedNodes(results, seen);

            return results;
        }

        private void AddServer(object nodeInfo, List<string> results, HashSet<string> seen)
        {
            try
            {
                string serverName = ExtractConnectionFromNodeInfo(nodeInfo);
                if (!string.IsNullOrEmpty(serverName) && seen.Add(serverName))
                    results.Add(serverName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot: node connection error: {ex.Message}");
            }
        }

        /// <summary>
        /// The INodeInformation of every Object Explorer root node that hands one out. Not
        /// filtered by server type, and a server connected twice has two roots. Empty when
        /// the tree or the type can't be reached.
        /// </summary>
        private List<object> GetRootNodeInfos()
        {
            var infos = new List<object>();

            // Tree IS the TreeView (ObjectExplorerControl derives from it) -- it has no
            // child controls, so searching its Controls for one finds nothing.
            if (!(GetObjectExplorerControl() is TreeView treeView)) return infos;

            var nodeInfoType = ReflectionTypeCache.FindType(NodeInformationTypeName);
            if (nodeInfoType == null) return infos;

            foreach (TreeNode root in treeView.Nodes)
            {
                try
                {
                    // OE tree nodes hand out their INodeInformation as a service.
                    var nodeInfo = (root as IServiceProvider)?.GetService(nodeInfoType);
                    if (nodeInfo != null) infos.Add(nodeInfo);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SqlPilot: root node error: {ex.Message}");
                }
            }

            return infos;
        }

        /// <summary>
        /// The server name each Database Engine root carries in its URN context
        /// (<c>Server[@Name='...']</c>), in tree order.
        /// </summary>
        private List<string> GetRootUrnServerNames()
        {
            const string prefix = "Server[@Name='";
            var names = new List<string>();

            foreach (var nodeInfo in GetRootNodeInfos())
            {
                var ctx = nodeInfo.GetType().GetProperty("Context")?.GetValue(nodeInfo)?.ToString();
                if (string.IsNullOrEmpty(ctx) || !ctx.StartsWith(prefix)) continue;

                int end = ctx.IndexOf('\'', prefix.Length);
                if (end > prefix.Length)
                    names.Add(ctx.Substring(prefix.Length, end - prefix.Length));
            }

            return names;
        }

        private static bool IsPrefixMatch(string a, string b)
            => a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
               || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);

        public ConnectionDescriptor GetConnectionInfo(string serverName)
        {
            _connectionCache.TryGetValue(serverName, out var conn);
            return conn;
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

            string bestMatch = null;
            foreach (var rootServer in GetRootUrnServerNames())
            {
                if (bestMatch == null && IsPrefixMatch(logicalServerName, rootServer))
                    bestMatch = rootServer;
                else
                    candidates.Add(rootServer);
            }

            if (bestMatch != null) candidates.Insert(0, bestMatch);
            bool hasLogical = false;
            foreach (var c in candidates)
                if (string.Equals(c, logicalServerName, StringComparison.OrdinalIgnoreCase)) { hasLogical = true; break; }
            if (!hasLogical) candidates.Add(logicalServerName);

            return candidates;
        }

        private void TryGetFromSelectedNodes(List<string> results, HashSet<string> seen)
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
                    if (node != null) AddServer(node, results, seen);
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
