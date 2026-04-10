using System;
using System.Collections.Generic;

namespace SqlPilot.Core.Search
{
    public static class FuzzyMatcher
    {
        public static int Score(string query, string candidate)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(candidate))
                return 0;

            // Pre-compute lowered query once (avoids per-char ToLowerInvariant in sub-methods)
            string queryLower = query.ToLowerInvariant();
            string candidateLower = candidate.ToLowerInvariant();

            // Quick reject: first char not present
            if (queryLower.Length > 2 && candidateLower.IndexOf(queryLower[0]) < 0)
                return 0;

            // Exact match
            if (queryLower.Length == candidateLower.Length && queryLower == candidateLower)
                return 1000;

            // Exact prefix
            if (candidateLower.StartsWith(queryLower))
                return 800 + Math.Max(0, 100 - (candidate.Length - query.Length));

            // Substring contains
            int idx = candidateLower.IndexOf(queryLower);
            if (idx >= 0)
                return 400 + Math.Max(0, 100 - idx);

            // CamelCase hump match (uses original case for hump detection)
            int camelScore = ScoreCamelCase(queryLower, candidate);
            if (camelScore > 0)
                return 600 + camelScore;

            // Subsequence match
            int subseqScore = ScoreSubsequence(queryLower, candidateLower);
            if (subseqScore > 0)
                return 200 + subseqScore;

            // Levenshtein fuzzy match for short queries
            if (query.Length <= 8)
            {
                int distance = LevenshteinDistance(queryLower, candidateLower);
                int maxDistance = query.Length / 3 + 1;
                if (distance <= maxDistance)
                    return Math.Max(1, 100 - distance * 30);
            }

            return 0;
        }

        public static int[] GetMatchedIndices(string query, string candidate)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(candidate))
                return Array.Empty<int>();

            string queryLower = query.ToLowerInvariant();
            string candidateLower = candidate.ToLowerInvariant();

            // Prefix
            if (candidateLower.StartsWith(queryLower))
            {
                var indices = new int[query.Length];
                for (int i = 0; i < query.Length; i++)
                    indices[i] = i;
                return indices;
            }

            // Substring
            int idx = candidateLower.IndexOf(queryLower);
            if (idx >= 0)
            {
                var indices = new int[query.Length];
                for (int i = 0; i < query.Length; i++)
                    indices[i] = idx + i;
                return indices;
            }

            // Subsequence
            var matched = new List<int>();
            int qi = 0;
            for (int ci = 0; ci < candidateLower.Length && qi < queryLower.Length; ci++)
            {
                if (queryLower[qi] == candidateLower[ci])
                {
                    matched.Add(ci);
                    qi++;
                }
            }

            return qi == query.Length ? matched.ToArray() : Array.Empty<int>();
        }

        private static int ScoreCamelCase(string queryLower, string candidate)
        {
            // Extract hump positions from candidate using original casing
            var humps = new List<int> { 0 };
            for (int i = 1; i < candidate.Length; i++)
            {
                if (char.IsUpper(candidate[i]) && !char.IsUpper(candidate[i - 1]))
                    humps.Add(i);
                else if (candidate[i] == '_' && i + 1 < candidate.Length)
                    humps.Add(i + 1);
            }

            int qi = 0;
            int matchedHumps = 0;
            foreach (int humpStart in humps)
            {
                if (qi >= queryLower.Length) break;

                int ci = humpStart;
                while (qi < queryLower.Length && ci < candidate.Length
                    && queryLower[qi] == char.ToLowerInvariant(candidate[ci]))
                {
                    qi++;
                    ci++;
                }

                if (ci > humpStart) matchedHumps++;
            }

            if (qi == queryLower.Length)
                return matchedHumps * 20 + queryLower.Length * 5;

            return 0;
        }

        private static int ScoreSubsequence(string queryLower, string candidateLower)
        {
            int qi = 0;
            int gaps = 0;
            bool lastMatched = false;

            for (int ci = 0; ci < candidateLower.Length && qi < queryLower.Length; ci++)
            {
                if (queryLower[qi] == candidateLower[ci])
                {
                    if (!lastMatched && qi > 0) gaps++;
                    lastMatched = true;
                    qi++;
                }
                else
                {
                    lastMatched = false;
                }
            }

            if (qi == queryLower.Length)
                return Math.Max(1, 100 - gaps * 15 - (candidateLower.Length - queryLower.Length) * 2);

            return 0;
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;

            if (n == 0) return m;
            if (m == 0) return n;

            var prev = new int[m + 1];
            var curr = new int[m + 1];

            for (int j = 0; j <= m; j++)
                prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }

                var temp = prev;
                prev = curr;
                curr = temp;
            }

            return prev[m];
        }
    }
}
