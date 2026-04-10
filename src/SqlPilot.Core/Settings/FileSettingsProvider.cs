using System;
using System.Collections.Generic;
using System.Globalization;
using SqlPilot.Core.Persistence;

namespace SqlPilot.Core.Settings
{
    public sealed class FileSettingsProvider : ISettingsProvider
    {
        private readonly string _filePath;

        public FileSettingsProvider(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public SqlPilotSettings GetSettings()
        {
            var dict = LineStore.LoadSettings(_filePath);
            var s = new SqlPilotSettings();

            if (dict.TryGetValue("MaxSearchResults", out var v) && int.TryParse(v, out var i))
                s.MaxSearchResults = i;
            if (dict.TryGetValue("SearchDebounceMs", out v) && int.TryParse(v, out i))
                s.SearchDebounceMs = i;
            if (dict.TryGetValue("SelectTopNCount", out v) && int.TryParse(v, out i))
                s.SelectTopNCount = i;
            if (dict.TryGetValue("CheckForUpdates", out v) && bool.TryParse(v, out var b))
                s.CheckForUpdates = b;
            if (dict.TryGetValue("SkippedVersion", out v))
                s.SkippedVersion = v;
            if (dict.TryGetValue("LastUpdateCheckUtc", out v) && !string.IsNullOrEmpty(v)
                && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                s.LastUpdateCheckUtc = dt;

            return s;
        }

        public void SaveSettings(SqlPilotSettings s)
        {
            var dict = new Dictionary<string, string>
            {
                ["MaxSearchResults"] = s.MaxSearchResults.ToString(CultureInfo.InvariantCulture),
                ["SearchDebounceMs"] = s.SearchDebounceMs.ToString(CultureInfo.InvariantCulture),
                ["SelectTopNCount"] = s.SelectTopNCount.ToString(CultureInfo.InvariantCulture),
                ["CheckForUpdates"] = s.CheckForUpdates.ToString(),
                ["SkippedVersion"] = s.SkippedVersion ?? "",
                ["LastUpdateCheckUtc"] = s.LastUpdateCheckUtc?.ToString("o", CultureInfo.InvariantCulture) ?? ""
            };
            LineStore.SaveSettings(_filePath, dict);
        }
    }
}
