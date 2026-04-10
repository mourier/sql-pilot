using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;

namespace SqlPilot.Package.Integration
{
    /// <summary>
    /// Wraps a SqlConnectionInfo as an IManagedConnection for use with
    /// ServiceCache.ScriptFactory.DesignTableOrView.
    /// </summary>
    internal sealed class ManagedConnectionWrapper : IManagedConnection
    {
        public SqlOlapConnectionInfoBase Connection { get; set; }
        public void Close() { }
        public void Dispose() { }
    }
}
