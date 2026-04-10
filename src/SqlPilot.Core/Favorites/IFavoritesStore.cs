using System.Collections.Generic;
using SqlPilot.Core.Database;

namespace SqlPilot.Core.Favorites
{
    public interface IFavoritesStore
    {
        bool IsFavorite(DatabaseObject obj);
        void AddFavorite(DatabaseObject obj);
        void RemoveFavorite(DatabaseObject obj);
        IReadOnlyList<DatabaseObject> GetAll();
        void Save();
        void Load();
    }
}
