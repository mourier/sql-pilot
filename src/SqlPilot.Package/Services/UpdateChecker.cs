using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SqlPilot.Package.Services
{
    public static class UpdateChecker
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/mourier/sql-pilot/releases/latest";
        private static readonly HttpClient HttpClient = new HttpClient();

        static UpdateChecker()
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "SqlPilot-UpdateChecker");
            HttpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public static async Task<UpdateInfo> CheckForUpdateAsync(string skippedVersion = null)
        {
            try
            {
                var response = await HttpClient.GetStringAsync(GitHubApiUrl);

                // Simple regex extraction instead of System.Text.Json
                string tagName = ExtractJsonValue(response, "tag_name");
                string htmlUrl = ExtractJsonValue(response, "html_url");

                string latestVersion = tagName?.TrimStart('v') ?? "0.0.0";

                var current = Assembly.GetExecutingAssembly().GetName().Version;
                var latest = Version.TryParse(latestVersion, out var v) ? v : new Version(0, 0, 0);

                if (latest > current && latestVersion != skippedVersion)
                {
                    return new UpdateInfo
                    {
                        IsUpdateAvailable = true,
                        LatestVersion = latestVersion,
                        ReleaseUrl = htmlUrl
                    };
                }

                return new UpdateInfo { IsUpdateAvailable = false };
            }
            catch (Exception ex)
            {
                // Offline, rate-limited, or transient failure — silent by design.
                Debug.WriteLine($"SqlPilot UpdateChecker: {ex.Message}");
                return new UpdateInfo { IsUpdateAvailable = false };
            }
        }

        // Regex instead of System.Text.Json: we only need two scalar values from the
        // GitHub releases payload, and System.Text.Json pulls in a newer System.Memory
        // than SSMS 18 ships — same constraint that keeps LineStore string-based
        // (see SqlPilot.Core/Persistence/LineStore.cs).
        private static string ExtractJsonValue(string json, string key)
        {
            var match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    public class UpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseUrl { get; set; }
    }
}
