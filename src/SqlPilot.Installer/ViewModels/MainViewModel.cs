using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlPilot.Installer.Services;

namespace SqlPilot.Installer.ViewModels
{
    /// <summary>
    /// Drives the single-window state machine: Selection → Progress → Done (or Error).
    /// Exposes the commands the XAML binds to (Install, Uninstall, Cancel, Close, CloseSsms).
    /// </summary>
    internal sealed partial class MainViewModel : ObservableObject
    {
        public string ProductTitle => $"SQL Pilot {InstallerVersionString} — Installer";
        public string InstallerVersionString { get; }
        public string ProductTagline => "Quick search for SQL Server Management Studio";
        public string RepoUrl => "https://github.com/mourier/sql-pilot";

        public ObservableCollection<SsmsVersionRow> DetectedVersions { get; } = new();
        public ObservableCollection<InstallStep> ProgressSteps { get; } = new();

        [ObservableProperty]
        private ViewState _state = ViewState.Selection;

        [ObservableProperty]
        private string _errorMessage;

        [ObservableProperty]
        private string _doneSummary;

        [ObservableProperty]
        private bool _isAnySsmsRunning;

        [ObservableProperty]
        private double? _overallProgress;

        public bool HasNothingDetected => DetectedVersions.Count == 0;
        public bool HasAnyDetected => DetectedVersions.Count > 0;

        // Invariant: InstalledVersion is set once at detection time before each row
        // is added to DetectedVersions, never mutated afterwards. So this property
        // only needs to be notified once via DetectAll() — no per-row subscription
        // required. If a future flow re-detects after install/uninstall, this will
        // need a CollectionChanged + per-row InstalledVersion listener.
        public bool ShowUninstallButton => DetectedVersions.Any(v => v.IsAlreadyInstalled);

        public bool CanStartAction => HasAnyDetected && DetectedVersions.Any(v => v.IsSelected);

        // ─── View-state derived properties (consumed by XAML Visibility bindings) ───
        public bool IsInSelectionView => State == ViewState.Selection;
        public bool IsInProgressView => State == ViewState.Progress;
        public bool IsInDoneView => State == ViewState.Done;
        public bool IsInErrorView => State == ViewState.Error;
        public bool IsTerminalView => State == ViewState.Done || State == ViewState.Error;
        public bool OverallProgressUnknown => OverallProgress == null;

        partial void OnStateChanged(ViewState value)
        {
            OnPropertyChanged(nameof(IsInSelectionView));
            OnPropertyChanged(nameof(IsInProgressView));
            OnPropertyChanged(nameof(IsInDoneView));
            OnPropertyChanged(nameof(IsInErrorView));
            OnPropertyChanged(nameof(IsTerminalView));
        }

        partial void OnOverallProgressChanged(double? value)
        {
            OnPropertyChanged(nameof(OverallProgressUnknown));
        }

        private CancellationTokenSource _cts;

        public MainViewModel()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            InstallerVersionString = $"{v.Major}.{v.Minor}.{v.Build}";

            DetectAll();
        }

        private void DetectAll()
        {
            DetectedVersions.Clear();
            foreach (var row in SsmsDetector.DetectAll())
            {
                row.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SsmsVersionRow.IsSelected))
                        OnPropertyChanged(nameof(CanStartAction));
                };
                DetectedVersions.Add(row);
            }
            OnPropertyChanged(nameof(HasNothingDetected));
            OnPropertyChanged(nameof(ShowUninstallButton));
            OnPropertyChanged(nameof(CanStartAction));
            IsAnySsmsRunning = ProcessHelper.IsAnySsmsRunning();
        }

        // ─────────────────────────────────────────────────────────────────
        // Commands
        // ─────────────────────────────────────────────────────────────────

        [RelayCommand]
        private Task InstallAsync() => RunActionAsync(install: true);

        [RelayCommand]
        private Task UninstallAsync() => RunActionAsync(install: false);

        [RelayCommand]
        private void Cancel()
        {
            _cts?.Cancel();
            Application.Current.Shutdown();
        }

        [RelayCommand]
        private void Close() => Application.Current.Shutdown();

        [RelayCommand]
        private async Task CloseSsmsAsync()
        {
            await ProcessHelper.StopAllSsmsAsync();
            IsAnySsmsRunning = ProcessHelper.IsAnySsmsRunning();
        }

        [RelayCommand]
        private void OpenRepo()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RepoUrl) { UseShellExecute = true }); }
            catch { /* user might not have a default browser */ }
        }

        // ─────────────────────────────────────────────────────────────────
        // The actual install/uninstall pipeline
        // ─────────────────────────────────────────────────────────────────

        private async Task RunActionAsync(bool install)
        {
            var selected = DetectedVersions.Where(v => v.IsSelected).ToList();
            if (selected.Count == 0) return;

            ProgressSteps.Clear();
            if (install)
            {
                ProgressSteps.Add(new InstallStep(InstallStepKind.FetchingRelease, "Fetching release info"));
                ProgressSteps.Add(new InstallStep(InstallStepKind.Downloading, "Downloading release"));
                ProgressSteps.Add(new InstallStep(InstallStepKind.VerifyingHash, "Verifying SHA-256"));
                ProgressSteps.Add(new InstallStep(InstallStepKind.Extracting, "Extracting"));
                foreach (var row in selected)
                {
                    ProgressSteps.Add(new InstallStep(InstallStepKind.InstallingVersion, $"Installing for {row.Label}"));
                }
                ProgressSteps.Add(new InstallStep(InstallStepKind.CleaningUp, "Cleaning up"));
            }
            else
            {
                foreach (var row in selected)
                {
                    ProgressSteps.Add(new InstallStep(InstallStepKind.UninstallingVersion, $"Removing from {row.Label}"));
                }
            }

            State = ViewState.Progress;
            _cts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<InstallProgress>(OnEngineProgress);
                using (var github = new GitHubReleaseClient())
                {
                    var engine = new InstallEngine(github, progress);
                    if (install)
                        await engine.InstallAsync(selected, _cts.Token);
                    else
                        await engine.UninstallAsync(selected, _cts.Token);
                }

                DoneSummary = BuildSummary(selected, install);
                State = ViewState.Done;
            }
            catch (OperationCanceledException)
            {
                State = ViewState.Selection;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                State = ViewState.Error;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void OnEngineProgress(InstallProgress p)
        {
            // Find the matching step (last one of that kind that isn't already Done — handles
            // multiple InstallingVersion steps in a row).
            InstallStep step = null;
            for (int i = ProgressSteps.Count - 1; i >= 0; i--)
            {
                if (ProgressSteps[i].Kind == p.Step && ProgressSteps[i].State != InstallStepState.Done)
                {
                    step = ProgressSteps[i];
                    break;
                }
            }
            if (step == null) return;

            step.State = p.State;
            if (!string.IsNullOrEmpty(p.Detail)) step.Detail = p.Detail;
            if (p.Percent.HasValue) OverallProgress = p.Percent;
        }

        private static string BuildSummary(IReadOnlyList<SsmsVersionRow> processed, bool install)
        {
            var labels = string.Join(", ", processed.Select(p => p.Label));
            return install
                ? $"Installed for: {labels}\nRestart SSMS to start using SQL Pilot. Press Ctrl+D inside SSMS to open the search panel."
                : $"Removed from: {labels}\nRestart SSMS to complete removal.";
        }
    }

    internal enum ViewState
    {
        Selection,
        Progress,
        Done,
        Error
    }
}
