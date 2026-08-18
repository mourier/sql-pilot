using CommunityToolkit.Mvvm.ComponentModel;

namespace SqlPilot.UI.ViewModels
{
    /// <summary>
    /// One database checkbox under a <see cref="ServerScopeNode"/>.
    /// </summary>
    public partial class DatabaseScopeNode : ObservableObject
    {
        private readonly ServerScopeNode _server;

        [ObservableProperty]
        private bool _isIncluded = true;

        internal DatabaseScopeNode(ServerScopeNode server, string name, bool isIncluded)
        {
            _server = server;
            Name = name;
            _isIncluded = isIncluded;
        }

        public string Name { get; }

        public string ServerName => _server.Name;

        /// <summary>
        /// Set while the tree is being synced from the store, so programmatic
        /// updates don't loop back into the store as if the user had clicked.
        /// </summary>
        internal bool IsSyncing { get; set; }

        internal void SetIncludedSilently(bool included)
        {
            IsSyncing = true;
            try { IsIncluded = included; }
            finally { IsSyncing = false; }
        }

        partial void OnIsIncludedChanged(bool value)
        {
            if (IsSyncing) return;
            _server.OnDatabaseToggled(this, value);
        }
    }
}
