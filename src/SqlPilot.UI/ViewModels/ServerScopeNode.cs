using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SqlPilot.UI.ViewModels
{
    /// <summary>
    /// A connected server in the scope tree. <see cref="IsChecked"/> is tri-state:
    /// true when everything under it is in scope, false when the server itself is
    /// excluded (or every database is), null when only some databases are.
    /// </summary>
    public partial class ServerScopeNode : ObservableObject
    {
        private readonly ScopeViewModel _owner;
        private bool _isServerIncluded = true;

        internal ServerScopeNode(ScopeViewModel owner, string name, bool isServerIncluded)
        {
            _owner = owner;
            Name = name;
            _isServerIncluded = isServerIncluded;
        }

        public string Name { get; }

        public ObservableCollection<DatabaseScopeNode> Databases { get; } = new ObservableCollection<DatabaseScopeNode>();

        public bool? IsChecked
        {
            get
            {
                if (!_isServerIncluded) return false;
                if (Databases.Count == 0) return true;
                if (Databases.All(d => d.IsIncluded)) return true;
                if (Databases.All(d => !d.IsIncluded)) return false;
                return null;
            }
            set
            {
                // The checkbox is bound with IsThreeState="False", so a click only
                // ever produces true or false; null arrives when the binding pushes
                // the indeterminate state back and is ignored.
                if (value == null) return;
                if (value == IsChecked) return;
                _owner.OnServerToggled(this, value.Value);
            }
        }

        internal bool IsServerIncluded
        {
            get => _isServerIncluded;
            set
            {
                _isServerIncluded = value;
                RaiseIsCheckedChanged();
            }
        }

        /// <summary>
        /// Inserts at the sorted position. The collection is only ever added to here
        /// and removed from below — both order-preserving — so it stays sorted without
        /// a re-sort pass over a live-bound collection after every refresh.
        /// </summary>
        internal void AddDatabase(string name, bool isIncluded)
        {
            int i = 0;
            while (i < Databases.Count
                   && StringComparer.OrdinalIgnoreCase.Compare(Databases[i].Name, name) < 0)
                i++;

            Databases.Insert(i, new DatabaseScopeNode(this, name, isIncluded));
        }

        internal void RemoveDatabasesExcept(IEnumerable<string> keep)
        {
            var live = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
            foreach (var gone in Databases.Where(d => !live.Contains(d.Name)).ToList())
                Databases.Remove(gone);
        }

        internal void RaiseIsCheckedChanged() => OnPropertyChanged(nameof(IsChecked));

        internal void OnDatabaseToggled(DatabaseScopeNode database, bool included)
            => _owner.OnDatabaseToggled(this, database, included);
    }
}
