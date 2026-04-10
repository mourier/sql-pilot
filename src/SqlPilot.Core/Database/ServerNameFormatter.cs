using System;

namespace SqlPilot.Core.Database
{
    /// <summary>
    /// Helpers for displaying server names in compact UI surfaces.
    /// Server names from SSMS can be long (e.g. "tcp:foo.database.windows.net,1433"),
    /// so we strip protocol prefixes, drop ports, and shorten dotted hostnames where safe.
    /// </summary>
    public static class ServerNameFormatter
    {
        /// <summary>
        /// Returns a compact display form of the given server name.
        /// IP addresses are preserved; instance names (MACHINE\INSTANCE) are preserved.
        /// </summary>
        public static string Shorten(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
                return serverName ?? "";

            string s = serverName.Trim();

            s = StripPrefix(s, "tcp:");
            s = StripPrefix(s, "np:");
            s = StripPrefix(s, "lpc:");
            s = StripPrefix(s, "admin:");

            int comma = s.IndexOf(',');
            if (comma >= 0)
                s = s.Substring(0, comma);

            // MACHINE\INSTANCE: keep the instance segment — it identifies the
            // server. Only the dotted FQDN form gets trimmed below.
            int dot = s.IndexOf('.');
            if (dot > 0 && !IsIPv4(s))
            {
                string firstSegment = s.Substring(0, dot);
                if (firstSegment.Length >= 2)
                    s = firstSegment;
            }

            return s;
        }

        private static string StripPrefix(string value, string prefix)
        {
            if (value.Length >= prefix.Length &&
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value.Substring(prefix.Length);
            }
            return value;
        }

        private static bool IsIPv4(string value)
        {
            // Quick check: 4 dot-separated numeric segments. Avoids pulling in IPAddress.TryParse.
            int segments = 0;
            int segLen = 0;
            foreach (char c in value)
            {
                if (c == '.')
                {
                    if (segLen == 0) return false;
                    segments++;
                    segLen = 0;
                }
                else if (c >= '0' && c <= '9')
                {
                    segLen++;
                    if (segLen > 3) return false;
                }
                else
                {
                    return false;
                }
            }
            if (segLen > 0) segments++;
            return segments == 4;
        }
    }
}
