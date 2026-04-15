using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SqlPilot.Installer.Services
{
    /// <summary>
    /// Talks to the GitHub Releases API for mourier/sql-pilot. Mirrors the
    /// SqlPilot.Package.Services.UpdateChecker pattern: HttpClient with a User-Agent
    /// and regex JSON extraction. We use regex (instead of System.Text.Json) to keep
    /// the installer dependency-free and the exe small — for the handful of scalar
    /// fields we need from the GitHub releases payload, a hand-rolled extractor is
    /// simpler than pulling in another NuGet package.
    /// </summary>
    internal sealed class GitHubReleaseClient : IDisposable
    {
        // The installer pins to its own version's release rather than "latest" so
        // an old installer never accidentally pulls down a newer ZIP.
        private const string ReleasesByTagUrl = "https://api.github.com/repos/mourier/sql-pilot/releases/tags/v{0}";
        private const string LatestReleaseUrl = "https://api.github.com/repos/mourier/sql-pilot/releases/latest";

        private readonly HttpClient _http;

        public GitHubReleaseClient()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _http.DefaultRequestHeaders.Add("User-Agent", "SqlPilotInstaller");
        }

        public void Dispose() => _http.Dispose();

        /// <summary>
        /// Fetches the GitHub release matching the installer's own assembly version.
        /// Falls back to /releases/latest if the pinned tag isn't found (e.g. when
        /// running a locally-built installer that doesn't correspond to a real release).
        /// </summary>
        public async Task<ReleaseInfo> GetMatchingReleaseAsync(CancellationToken ct = default)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var pinnedUrl = string.Format(ReleasesByTagUrl, $"{version.Major}.{version.Minor}.{version.Build}");

            try
            {
                return await GetReleaseAsync(pinnedUrl, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Pinned tag not found — fall back to latest
                return await GetReleaseAsync(LatestReleaseUrl, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fetches the latest published release, regardless of the installer's own
        /// version. Used for the self-update notification: if the latest release is
        /// newer than the running installer, we show a banner pointing at it.
        /// </summary>
        public Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken ct = default)
            => GetReleaseAsync(LatestReleaseUrl, ct);

        private async Task<ReleaseInfo> GetReleaseAsync(string url, CancellationToken ct)
        {
            var json = await _http.GetStringAsync(url).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var info = new ReleaseInfo
            {
                TagName = ExtractJsonValue(json, "tag_name"),
                HtmlUrl = ExtractJsonValue(json, "html_url"),
                Body = ExtractJsonValue(json, "body"),
                Assets = ExtractAssets(json)
            };
            return info;
        }

        /// <summary>
        /// Streams an asset to the destination path, reporting download progress.
        /// </summary>
        public async Task DownloadAssetAsync(
            ReleaseAsset asset,
            string destinationPath,
            IProgress<DownloadProgress> progress,
            CancellationToken ct = default)
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var response = await _http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength ?? asset.Size;

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var file = File.Create(destinationPath))
                {
                    var buffer = new byte[81920];
                    long downloaded = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        downloaded += read;
                        progress?.Report(new DownloadProgress(downloaded, total));
                    }
                }
            }
        }

        /// <summary>
        /// Verifies a downloaded file against an expected SHA-256 hex string.
        /// If <paramref name="expectedSha256"/> is null/empty, returns true (no hash published).
        /// </summary>
        public static bool VerifySha256(string filePath, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256)) return true;

            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                return string.Equals(actual, expectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Tries to extract a "SHA-256: &lt;hex&gt;" line from the release body. Optional —
        /// if the release body doesn't include one, integrity check is skipped.
        /// </summary>
        public static string ExtractSha256FromReleaseBody(string body, string assetName)
        {
            if (string.IsNullOrEmpty(body)) return null;
            // Look for "SqlPilot-vX.Y.Z.zip: <hex>" or "<hex>  SqlPilot-vX.Y.Z.zip"
            var line = Regex.Match(body, $@"([0-9a-fA-F]{{64}})\s*[\s|]?\s*{Regex.Escape(assetName)}");
            if (line.Success) return line.Groups[1].Value;

            line = Regex.Match(body, $@"{Regex.Escape(assetName)}\s*[:|]?\s*([0-9a-fA-F]{{64}})");
            if (line.Success) return line.Groups[1].Value;

            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        // JSON extraction (regex helpers)
        // ─────────────────────────────────────────────────────────────────

        private static string ExtractJsonValue(string json, string key)
        {
            var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? UnescapeJsonString(m.Groups[1].Value) : null;
        }

        private static long? ExtractJsonLong(string json, string key)
        {
            var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*(\\d+)");
            return m.Success && long.TryParse(m.Groups[1].Value, out var v) ? v : (long?)null;
        }

        private static string UnescapeJsonString(string s)
        {
            return s
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\/", "/")
                .Replace("\\\\", "\\");
        }

        /// <summary>
        /// Walks the "assets" array in the release JSON and pulls out each entry's
        /// name, size, and browser_download_url. Regex-based — fragile by design,
        /// but the GitHub API shape is stable enough that this is acceptable for
        /// the small set of fields we need.
        /// </summary>
        private static List<ReleaseAsset> ExtractAssets(string json)
        {
            var assets = new List<ReleaseAsset>();
            // Find each {...} object inside the "assets":[...] array
            var assetsArrayMatch = Regex.Match(json, "\"assets\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!assetsArrayMatch.Success) return assets;

            var arrayBody = assetsArrayMatch.Groups[1].Value;
            // Each asset object — match braces non-greedy
            foreach (Match obj in Regex.Matches(arrayBody, "\\{(?:[^{}]|(?<o>\\{)|(?<-o>\\}))*\\}", RegexOptions.Singleline))
            {
                var name = ExtractJsonValue(obj.Value, "name");
                var url = ExtractJsonValue(obj.Value, "browser_download_url");
                var size = ExtractJsonLong(obj.Value, "size") ?? 0;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                {
                    assets.Add(new ReleaseAsset { Name = name, BrowserDownloadUrl = url, Size = size });
                }
            }
            return assets;
        }
    }

    internal sealed class ReleaseInfo
    {
        public string TagName { get; set; }
        public string HtmlUrl { get; set; }
        public string Body { get; set; }
        public List<ReleaseAsset> Assets { get; set; } = new List<ReleaseAsset>();
    }

    internal sealed class ReleaseAsset
    {
        public string Name { get; set; }
        public string BrowserDownloadUrl { get; set; }
        public long Size { get; set; }
    }

    internal readonly struct DownloadProgress
    {
        public long BytesDownloaded { get; }
        public long? TotalBytes { get; }
        public double? Percent => TotalBytes.HasValue && TotalBytes.Value > 0
            ? (double)BytesDownloaded / TotalBytes.Value
            : (double?)null;

        public DownloadProgress(long bytes, long? total)
        {
            BytesDownloaded = bytes;
            TotalBytes = total;
        }
    }
}
