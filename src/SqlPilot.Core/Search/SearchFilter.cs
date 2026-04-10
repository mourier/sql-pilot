using SqlPilot.Core.Database;

namespace SqlPilot.Core.Search
{
    public sealed class SearchFilter
    {
        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public DatabaseObjectType[] ObjectTypes { get; set; }
        public int MaxResults { get; set; } = 50;
    }
}
