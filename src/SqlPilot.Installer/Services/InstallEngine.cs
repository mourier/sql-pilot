using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SqlPilot.Installer.ViewModels;

namespace SqlPilot.Installer.Services
{
    /// <summary>
    /// The actual install/uninstall worker. Reports progress via IProgress so the
    /// UI thread can update the progress view.
    ///
    /// File copy + cache invalidation logic mirrors installer/_SsmsCacheHelpers.ps1
    /// (Invoke-SsmsCacheInvalidation) and the foreach loop in Install-SqlPilot.ps1
    /// — keep them in sync.
    /// </summary>
    internal sealed class InstallEngine
    {
        private readonly GitHubReleaseClient _github;
        private readonly IProgress<InstallProgress> _progress;

        public InstallEngine(GitHubReleaseClient github, IProgress<InstallProgress> progress)
        {
            _github = github;
            _progress = progress;
        }

        /// <summary>
        /// Full install pipeline: fetch release info → download asset → verify SHA-256
        /// → extract to temp → copy files into each selected SSMS → invalidate caches
        /// → clean up temp.
        /// </summary>
        public async Task InstallAsync(IReadOnlyList<SsmsVersionRow> selectedVersions, CancellationToken ct)
        {
            if (selectedVersions == null || selectedVersions.Count == 0)
                throw new InvalidOperationException("At least one SSMS version must be selected.");

            Report(InstallStepKind.FetchingRelease);
            var release = await _github.GetMatchingReleaseAsync(ct).ConfigureAwait(false);

            // The hyphen in "SqlPilot-" is load-bearing: it excludes the
            // SqlPilotInstaller-*.zip asset that lives in the same release.
            var asset = release.Assets.FirstOrDefault(a => a.Name.StartsWith("SqlPilot-", StringComparison.OrdinalIgnoreCase)
                                                            && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null)
                throw new InvalidOperationException($"Release {release.TagName} has no SqlPilot ZIP asset attached.");
            CompleteStep(InstallStepKind.FetchingRelease, $"Found {release.TagName}");

            var tempRoot = Path.Combine(Path.GetTempPath(), "SqlPilotInstaller_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempRoot);
            var zipPath = Path.Combine(tempRoot, asset.Name);

            try
            {
                Report(InstallStepKind.Downloading, $"{asset.Name} ({FormatBytes(asset.Size)})");
                var downloadProgress = new Progress<DownloadProgress>(p =>
                {
                    var pct = p.Percent.HasValue ? $" {(int)(p.Percent.Value * 100)}%" : "";
                    _progress?.Report(new InstallProgress(InstallStepKind.Downloading,
                        InstallStepState.InProgress,
                        $"{asset.Name} — {FormatBytes(p.BytesDownloaded)} of {FormatBytes(p.TotalBytes ?? asset.Size)}{pct}",
                        p.Percent));
                });
                await _github.DownloadAssetAsync(asset, zipPath, downloadProgress, ct).ConfigureAwait(false);
                CompleteStep(InstallStepKind.Downloading, FormatBytes(new FileInfo(zipPath).Length));

                // The local-CPU work — verify, extract, copy, invalidate caches —
                // runs on a thread-pool thread so the WPF dispatcher stays responsive
                // while the (potentially slow) ZipFile.ExtractToDirectory runs.
                await Task.Run(() => RunLocalSteps(release, asset, zipPath, tempRoot, selectedVersions, ct), ct).ConfigureAwait(false);
            }
            finally
            {
                TryDelete(tempRoot);
            }

            CompleteStep(InstallStepKind.CleaningUp);
        }

        private void RunLocalSteps(
            ReleaseInfo release,
            ReleaseAsset asset,
            string zipPath,
            string tempRoot,
            IReadOnlyList<SsmsVersionRow> selectedVersions,
            CancellationToken ct)
        {
            var expectedHash = GitHubReleaseClient.ExtractSha256FromReleaseBody(release.Body, asset.Name);
            if (!string.IsNullOrEmpty(expectedHash))
            {
                Report(InstallStepKind.VerifyingHash);
                if (!GitHubReleaseClient.VerifySha256(zipPath, expectedHash))
                    throw new InvalidOperationException("SHA-256 mismatch — the downloaded file does not match the expected hash. Aborting.");
                CompleteStep(InstallStepKind.VerifyingHash, "matched release SHA-256");
            }
            else
            {
                CompleteStep(InstallStepKind.VerifyingHash, "no hash published — skipped");
            }

            ct.ThrowIfCancellationRequested();
            Report(InstallStepKind.Extracting);
            var extractDir = Path.Combine(tempRoot, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            CompleteStep(InstallStepKind.Extracting);

            // Release ZIPs contain a single top-level "SqlPilot-vX.Y.Z" folder.
            var payloadRoot = FindPayloadRoot(extractDir);

            foreach (var row in selectedVersions)
            {
                ct.ThrowIfCancellationRequested();
                InstallOneVersion(payloadRoot, row);
            }

            Report(InstallStepKind.CleaningUp);
        }

        /// <summary>
        /// Removes the SQL Pilot extension folder from each selected SSMS version
        /// and invalidates that version's caches. Per-version try/catch so a locked
        /// DLL on one version doesn't abort the others.
        /// </summary>
        public Task UninstallAsync(IReadOnlyList<SsmsVersionRow> selectedVersions, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                foreach (var row in selectedVersions)
                {
                    ct.ThrowIfCancellationRequested();
                    UninstallOneVersion(row);
                }
            }, ct);
        }

        private void InstallOneVersion(string payloadRoot, SsmsVersionRow row)
        {
            Report(InstallStepKind.InstallingVersion, $"for {row.Label}");

            var payloadDir = Path.Combine(payloadRoot, row.Subfolder);
            if (!Directory.Exists(payloadDir))
            {
                FailStep(InstallStepKind.InstallingVersion, $"{row.Label}: payload subfolder '{row.Subfolder}' missing from ZIP");
                return;
            }

            var targetDir = Path.Combine(row.IdePath, "Extensions", "SqlPilot");
            Directory.CreateDirectory(targetDir);

            foreach (var src in Directory.GetFiles(payloadDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(src));
                File.Copy(src, dest, overwrite: true);
                RemoveMarkOfTheWeb(dest);
            }

            InvalidateCaches(row);
            CompleteStep(InstallStepKind.InstallingVersion, $"{row.Label} ✓");
        }

        private void UninstallOneVersion(SsmsVersionRow row)
        {
            Report(InstallStepKind.UninstallingVersion, $"from {row.Label}");

            try
            {
                var targetDir = Path.Combine(row.IdePath, "Extensions", "SqlPilot");
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, recursive: true);
                }
                InvalidateCaches(row);
                CompleteStep(InstallStepKind.UninstallingVersion, $"{row.Label} ✓");
            }
            catch (Exception ex)
            {
                FailStep(InstallStepKind.UninstallingVersion, $"{row.Label} failed: {ex.Message}");
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string name);

        /// <summary>
        /// Strips the Windows Mark-of-the-Web (the <c>Zone.Identifier</c> NTFS
        /// alternate data stream). Without this, .NET Framework refuses to load
        /// any DLL that carries it, with the error "An attempt was made to load
        /// an assembly from a network location..." — which happens when a user
        /// downloads the release ZIP via a browser and extracts it with Windows
        /// Explorer. Failure is silent because the stream may not be present.
        /// </summary>
        private static void RemoveMarkOfTheWeb(string filePath)
        {
            try { DeleteFile(filePath + ":Zone.Identifier"); } catch { }
        }

        /// <summary>
        /// 1:1 port of installer/_SsmsCacheHelpers.ps1::Invoke-SsmsCacheInvalidation.
        /// Deletes privateregistry.bin (+ LOG1/LOG2) and ComponentModelCache, then
        /// touches extensions.configurationchanged in both the user-data dir and
        /// the global Extensions folder.
        /// </summary>
        private static void InvalidateCaches(SsmsVersionRow row)
        {
            DirectoryInfo dataDir = null;
            try
            {
                if (Directory.Exists(row.DataBase))
                {
                    dataDir = new DirectoryInfo(row.DataBase)
                        .GetDirectories()
                        .FirstOrDefault(d => row.DataPattern.IsMatch(d.Name));
                }
            }
            catch { /* directory may not be accessible */ }

            if (dataDir != null)
            {
                foreach (var name in new[] { "privateregistry.bin", "privateregistry.bin.LOG1", "privateregistry.bin.LOG2" })
                {
                    var p = Path.Combine(dataDir.FullName, name);
                    TryDeleteFile(p);
                }

                var componentCache = Path.Combine(dataDir.FullName, "ComponentModelCache");
                if (Directory.Exists(componentCache))
                {
                    try { Directory.Delete(componentCache, recursive: true); } catch { }
                }

                TryWriteEmpty(Path.Combine(dataDir.FullName, "extensions.configurationchanged"));
            }

            // Touch the global extensions marker
            TryWriteEmpty(Path.Combine(row.IdePath, "Extensions", "extensions.configurationchanged"));
        }

        private static string FindPayloadRoot(string extractDir)
        {
            // Release ZIPs contain a single top-level "SqlPilot-vX.Y.Z" folder.
            var topLevel = Directory.GetDirectories(extractDir);
            if (topLevel.Length == 1) return topLevel[0];

            // Older / loose layouts: payload may already be at the root
            if (Directory.Exists(Path.Combine(extractDir, "SSMS22")) ||
                Directory.Exists(Path.Combine(extractDir, "SSMS18-20")))
                return extractDir;

            throw new InvalidOperationException($"Could not find SqlPilot payload root inside {extractDir}");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best effort */ }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryWriteEmpty(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Empty);
            }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
            return $"{bytes / 1024.0 / 1024.0:0.##} MB";
        }

        // ─────────────────────────────────────────────────────────────────
        // Progress reporting
        // ─────────────────────────────────────────────────────────────────

        private void Report(InstallStepKind kind, string detail = null)
            => _progress?.Report(new InstallProgress(kind, InstallStepState.InProgress, detail, null));

        private void CompleteStep(InstallStepKind kind, string detail = null)
            => _progress?.Report(new InstallProgress(kind, InstallStepState.Done, detail, null));

        private void FailStep(InstallStepKind kind, string detail)
            => _progress?.Report(new InstallProgress(kind, InstallStepState.Failed, detail, null));
    }

    internal enum InstallStepKind
    {
        FetchingRelease,
        Downloading,
        VerifyingHash,
        Extracting,
        InstallingVersion,
        UninstallingVersion,
        CleaningUp
    }

    internal enum InstallStepState
    {
        Pending,
        InProgress,
        Done,
        Failed
    }

    internal readonly struct InstallProgress
    {
        public InstallStepKind Step { get; }
        public InstallStepState State { get; }
        public string Detail { get; }
        public double? Percent { get; }

        public InstallProgress(InstallStepKind step, InstallStepState state, string detail, double? percent)
        {
            Step = step;
            State = state;
            Detail = detail;
            Percent = percent;
        }
    }
}
