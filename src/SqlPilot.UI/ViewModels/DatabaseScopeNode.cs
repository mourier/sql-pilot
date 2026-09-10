using CommunityToolkit.Mvvm.ComponentModel;

namespace SqlPilot.UI.ViewModels
{
    /// <summary>
    /// One database checkbox under a <see cref="ServerScopeNode"/>.
    /// </summary>
    public class DatabaseScopeNode : ObservableObject
    {
        private readonly ServerScopeNode _server;
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
        /// Written by the checkbox binding, so a change here means the user clicked.
        /// Hand-rolled rather than [ObservableProperty] so that
        /// <see cref="SetIncludedSilently"/> can bypass that meaning.
        /// </summary>
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (_isIncluded == value) return;
                _isIncluded = value;
                OnPropertyChanged();
                _server.OnDatabaseToggled(this, value);
            }
        }

        /// <summary>
        /// Push a state that came from the store, without looping back into it as if
        /// the user had clicked the checkbox.
        /// </summary>
        internal void SetIncludedSilently(bool included)
        {
            if (_isIncluded == included) return;
            _isIncluded = included;
            OnPropertyChanged(nameof(IsIncluded));
        }
    }
}
