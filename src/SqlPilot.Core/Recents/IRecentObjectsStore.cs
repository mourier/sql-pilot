using System.Collections.Generic;
using SqlPilot.Core.Database;

namespace SqlPilot.Core.Recents
{
    public interface IRecentObjectsStore
    {
        bool IsRecent(DatabaseObject obj);
        void RecordAccess(DatabaseObject obj);
        IReadOnlyList<DatabaseObject> GetRecent(int maxItems = 20);
        void Save();
        void Load();
    }
}
