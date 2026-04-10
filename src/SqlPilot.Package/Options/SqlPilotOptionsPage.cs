using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace SqlPilot.Package.Options
{
    public class SqlPilotOptionsPage : DialogPage
    {
        [Category("Search")]
        [DisplayName("Max Search Results")]
        [Description("Maximum number of results to display (default: 50)")]
        public int MaxSearchResults { get; set; } = 50;

        [Category("Search")]
        [DisplayName("Search Debounce (ms)")]
        [Description("Delay before executing search after typing stops (default: 150)")]
        public int SearchDebounceMs { get; set; } = 150;

        [Category("Actions")]
        [DisplayName("SELECT TOP N Count")]
        [Description("Number of rows for SELECT TOP N action (default: 100)")]
        public int SelectTopNCount { get; set; } = 100;

        [Category("Updates")]
        [DisplayName("Check for Updates")]
        [Description("Automatically check for new versions on startup")]
        public bool CheckForUpdates { get; set; } = true;
    }
}
