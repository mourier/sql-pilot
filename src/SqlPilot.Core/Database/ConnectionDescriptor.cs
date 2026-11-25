using System;

namespace SqlPilot.Core.Database
{
    public sealed class ConnectionDescriptor
    {
        public string ServerName { get; set; }
        public string ConnectionString { get; set; }
        public bool UseIntegratedSecurity { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }

        /// <summary>
        /// The raw SqlConnectionInfoWithConnection from Object Explorer.
        /// Needed by SSMS internal APIs that require a live connection.
        /// </summary>
        public object RawConnectionInfo { get; set; }

        public bool IsAzureSql => IsAzureSqlServerName(ServerName);

        public static bool IsAzureSqlServerName(string serverName)
        {
            return !string.IsNullOrEmpty(serverName)
                && serverName.IndexOf(".database.windows.net", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// True for a LocalDB instance name or its local pipe name, with or without "np:".
        /// LocalDB reached through an alias is not recognised, and neither is a pipe
        /// on another host, whatever it is called.
        /// </summary>
        public static bool IsLocalDbServerName(string serverName)
        {
            return !string.IsNullOrEmpty(serverName)
                && (serverName.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)
                    || serverName.IndexOf(@"\\.\pipe\LOCALDB#", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
