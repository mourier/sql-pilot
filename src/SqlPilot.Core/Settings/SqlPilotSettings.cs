using System;

namespace SqlPilot.Core.Settings
{
    public sealed class SqlPilotSettings
    {
        public int MaxSearchResults { get; set; } = 50;
        public int SearchDebounceMs { get; set; } = 150;
        public int SelectTopNCount { get; set; } = 100;
        public bool CheckForUpdates { get; set; } = true;
        public string SkippedVersion { get; set; }

        /// <summary>
        /// UTC timestamp of the last successful GitHub update check, or null if never checked.
        /// Used to throttle checks to at most once per day.
        /// </summary>
        public DateTime? LastUpdateCheckUtc { get; set; }
    }
}
