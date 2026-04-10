using System;
using System.Collections.Generic;
using System.Linq;
using SqlPilot.Core.Database;
using SqlPilot.Core.Persistence;

namespace SqlPilot.Core.Recents
{
    public sealed class RecentObjectsStore : IRecentObjectsStore
    {
        private readonly string _filePath;
        private readonly int _capacity;
        private readonly LinkedList<DatabaseObject> _recents = new LinkedList<DatabaseObject>();
        private readonly Dictionary<DatabaseObject, LinkedListNode<DatabaseObject>> _lookup = new Dictionary<DatabaseObject, LinkedListNode<DatabaseObject>>();
        private readonly object _lock = new object();

        public RecentObjectsStore(string filePath, int capacity = 100)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _capacity = capacity;
        }

        public bool IsRecent(DatabaseObject obj)
        {
            lock (_lock) { return _lookup.ContainsKey(obj); }
        }

        public void RecordAccess(DatabaseObject obj)
        {
            lock (_lock)
            {
                if (_lookup.TryGetValue(obj, out var existing))
                {
                    _recents.Remove(existing);
                    _lookup.Remove(obj);
                }

                var node = _recents.AddFirst(obj);
                _lookup[obj] = node;

                while (_recents.Count > _capacity)
                {
                    var last = _recents.Last;
                    _lookup.Remove(last.Value);
                    _recents.RemoveLast();
                }
            }
        }

        public IReadOnlyList<DatabaseObject> GetRecent(int maxItems = 20)
        {
            lock (_lock) { return _recents.Take(maxItems).ToList(); }
        }

        public void Save()
        {
            lock (_lock) { LineStore.SaveObjects(_filePath, _recents); }
        }

        public void Load()
        {
            lock (_lock)
            {
                _recents.Clear();
                _lookup.Clear();
                foreach (var item in LineStore.LoadObjects(_filePath))
                {
                    var node = _recents.AddLast(item);
                    _lookup[item] = node;
                }
            }
        }
    }
}
