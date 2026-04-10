using SqlPilot.Core.Database;

namespace SqlPilot.Core.Search
{
    public sealed class SearchResult
    {
        public DatabaseObject Object { get; set; }
        public int Score { get; set; }
        public int[] MatchedIndices { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsRecent { get; set; }
    }
}
