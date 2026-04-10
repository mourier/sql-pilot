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
    }
}
