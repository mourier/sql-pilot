using System;
using System.Data.Common;

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
        /// Needed by SSMS internal APIs that require a live connection. Its type also tells
        /// whether SSMS shares our SMO: only then is it an instance of our SqlConnectionInfo,
        /// and only then can <see cref="ConnectionString"/> be handed to our SqlClient as it is.
        /// </summary>
        public object RawConnectionInfo { get; set; }

        public bool IsAzureSql => IsAzureSqlServerName(ServerName);

        // What the Connect dialog asked for, read off ConnectionString by keyword so that it
        // holds whichever SqlClient wrote the string. SSMS 18 writes keywords without spaces,
        // SSMS 20 and 22 with them.

        /// <summary>
        /// The Encrypt keyword as written ("True", "False", "Strict"), or null if absent.
        /// </summary>
        public string EncryptSetting => ReadKeyword("Encrypt");

        /// <summary>
        /// Whether to ask for encryption when translating the string for another driver.
        /// Anything but an explicit "off" counts, a missing keyword included: that is the
        /// default of Microsoft.Data.SqlClient, whose strings are the ones that need translating.
        /// </summary>
        public bool RequestsEncryption
            => !IsAnyOf(EncryptSetting, "False", "No", "Optional");

        /// <summary>
        /// Whether the Object Explorer connection was told to trust the server certificate
        /// unchecked. Never under strict encryption, which validates regardless.
        /// </summary>
        public bool TrustsServerCertificate
            => !IsAnyOf(EncryptSetting, "Strict")
                && IsAnyOf(ReadKeyword("TrustServerCertificate", "Trust Server Certificate"), "True", "Yes");

        /// <summary>
        /// The name the server certificate is to be checked against when it is not the
        /// server name, or null. SSMS 20 and 22 only.
        /// </summary>
        public string HostNameInCertificate
            => ReadKeyword("HostNameInCertificate", "Host Name In Certificate");

        /// <summary>
        /// The path of a certificate file the server's certificate must match exactly, or
        /// null. Microsoft.Data.SqlClient only, so SSMS 20 and 22.
        /// </summary>
        public string ServerCertificate
            => ReadKeyword("ServerCertificate", "Server Certificate");

        // The value under the first spelling present. Null when there is none or the string
        // does not parse, which the properties above read as "encrypt, and do not trust".
        private string ReadKeyword(params string[] spellings)
        {
            if (string.IsNullOrEmpty(ConnectionString)) return null;
            try
            {
                var builder = new DbConnectionStringBuilder { ConnectionString = ConnectionString };
                foreach (var spelling in spellings)
                {
                    if (!builder.TryGetValue(spelling, out var value)) continue;
                    var text = value?.ToString().Trim();
                    return string.IsNullOrEmpty(text) ? null : text;
                }
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool IsAnyOf(string value, params string[] candidates)
        {
            foreach (var candidate in candidates)
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

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
