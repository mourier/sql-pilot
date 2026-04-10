using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using SqlPilot.Installer.Services;

namespace SqlPilot.Installer.ViewModels
{
    /// <summary>
    /// One row in the SSMS version selection list. Combines the static catalog
    /// metadata (label, paths, subfolder) with runtime state (whether SQL Pilot
    /// is currently installed and what version) and UI state (whether the user
    /// has the row checked).
    /// </summary>
    internal sealed partial class SsmsVersionRow : ObservableObject
    {
        public int Version { get; }
        public string Label { get; }
        public string IdePath { get; }
        public string Subfolder { get; }
        public string DataBase { get; }
        public Regex DataPattern { get; }

        /// <summary>The currently-installed SQL Pilot version, or null if not installed.</summary>
        [ObservableProperty]
        private string _installedVersion;

        /// <summary>True if the user has checked this row in the selection view.</summary>
        [ObservableProperty]
        private bool _isSelected;

        public bool IsAlreadyInstalled => !string.IsNullOrEmpty(InstalledVersion);

        /// <summary>
        /// Display string under the version label: "Already installed: 0.0.2" or "Not installed".
        /// Recomputed when InstalledVersion changes (the partial OnInstalledVersionChanged hook).
        /// </summary>
        public string StateText => IsAlreadyInstalled
            ? $"Already installed: {InstalledVersion}"
            : "Not installed";

        partial void OnInstalledVersionChanged(string value)
        {
            OnPropertyChanged(nameof(IsAlreadyInstalled));
            OnPropertyChanged(nameof(StateText));
        }

        internal SsmsVersionRow(SsmsCatalogEntry entry)
        {
            Version = entry.Version;
            Label = entry.Label;
            IdePath = entry.IdePath;
            Subfolder = entry.Subfolder;
            DataBase = entry.DataBase;
            DataPattern = entry.DataPattern;
            // Default selection: pre-checked, since the most common intent is "install everywhere"
            _isSelected = true;
        }
    }
}
