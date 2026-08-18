using CommunityToolkit.Mvvm.ComponentModel;

namespace SqlPilot.UI.ViewModels
{
    /// <summary>
    /// One database checkbox under a <see cref="ServerScopeNode"/>.
    /// </summary>
    public partial class DatabaseScopeNode : ObservableObject
    {
        private readonly ServerScopeNode _server;
        private bool _isSyncing;

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
        /// Push a state that came from the store, without looping back into it as if
        /// the user had clicked the checkbox.
        /// </summary>
        internal void SetIncludedSilently(bool included)
        {
            _isSyncing = true;
            try { IsIncluded = included; }
            finally { _isSyncing = false; }
        }

        partial void OnIsIncludedChanged(bool value)
        {
            if (_isSyncing) return;
            _server.OnDatabaseToggled(this, value);
        }
    }
}
