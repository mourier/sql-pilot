using CommunityToolkit.Mvvm.ComponentModel;
using SqlPilot.Installer.Services;

namespace SqlPilot.Installer.ViewModels
{
    /// <summary>
    /// One step in the progress view list. Bound by an ItemsControl in the
    /// MainWindow Progress view. Updated as the InstallEngine reports progress.
    /// </summary>
    internal sealed partial class InstallStep : ObservableObject
    {
        public InstallStepKind Kind { get; }
        public string DefaultLabel { get; }

        [ObservableProperty]
        private InstallStepState _state = InstallStepState.Pending;

        [ObservableProperty]
        private string _detail;

        public InstallStep(InstallStepKind kind, string defaultLabel)
        {
            Kind = kind;
            DefaultLabel = defaultLabel;
        }

        /// <summary>Glyph shown to the left of the label: ─ pending, ⊙ in-progress, ✓ done, ✗ failed.</summary>
        public string Glyph => State switch
        {
            InstallStepState.Pending => "─",
            InstallStepState.InProgress => "⊙",
            InstallStepState.Done => "✓",
            InstallStepState.Failed => "✗",
            _ => "─"
        };

        partial void OnStateChanged(InstallStepState value) => OnPropertyChanged(nameof(Glyph));
    }
}
