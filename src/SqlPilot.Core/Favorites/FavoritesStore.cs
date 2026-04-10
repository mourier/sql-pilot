using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SqlPilot.Core.Database;
using SqlPilot.Core.Persistence;

namespace SqlPilot.Core.Favorites
{
    public sealed class FavoritesStore : IFavoritesStore
    {
        private readonly string _filePath;
        private readonly ConcurrentDictionary<DatabaseObject, byte> _favorites = new ConcurrentDictionary<DatabaseObject, byte>();

        public FavoritesStore(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public bool IsFavorite(DatabaseObject obj) => _favorites.ContainsKey(obj);
        public void AddFavorite(DatabaseObject obj) => _favorites.TryAdd(obj, 0);
        public void RemoveFavorite(DatabaseObject obj) => _favorites.TryRemove(obj, out _);
        public IReadOnlyList<DatabaseObject> GetAll() => _favorites.Keys.ToList();

        public void Save() => LineStore.SaveObjects(_filePath, _favorites.Keys);

        public void Load()
        {
            _favorites.Clear();
            foreach (var item in LineStore.LoadObjects(_filePath))
                _favorites.TryAdd(item, 0);
        }
    }
}
