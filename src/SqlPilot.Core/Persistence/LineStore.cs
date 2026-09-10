using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SqlPilot.Core.Database;

namespace SqlPilot.Core.Persistence
{
    /// <summary>
    /// Simple line-based persistence for DatabaseObject lists.
    /// Format: ServerName|DatabaseName|SchemaName|ObjectName|ObjectType
    /// No external dependencies (avoids System.Text.Json version conflicts on SSMS 18).
    /// </summary>
    public static class LineStore
    {
        private const char Sep = '|';

        public static void SaveObjects(string path, IEnumerable<DatabaseObject> objects)
        {
            EnsureDirectory(path);
            var lines = objects.Select(o => $"{Esc(o.ServerName)}{Sep}{Esc(o.DatabaseName)}{Sep}{Esc(o.SchemaName)}{Sep}{Esc(o.ObjectName)}{Sep}{(int)o.ObjectType}");
            File.WriteAllLines(path, lines);
        }

        public static List<DatabaseObject> LoadObjects(string path)
        {
            var results = new List<DatabaseObject>();
            if (!File.Exists(path)) return results;

            foreach (var line in File.ReadAllLines(path))
            {
                // Split, not line.Split(Sep): SaveObjects escapes separators inside a
                // name, so a raw split would cut an object called "a|b" in half.
                var parts = Split(line);
                if (parts.Length < 5) continue;

                if (int.TryParse(parts[4], out var typeInt))
                {
                    results.Add(new DatabaseObject
                    {
                        ServerName = parts[0],
                        DatabaseName = parts[1],
                        SchemaName = parts[2],
                        ObjectName = parts[3],
                        ObjectType = (DatabaseObjectType)typeInt
                    });
                }
            }

            return results;
        }

        public static void SaveSettings(string path, Dictionary<string, string> settings)
        {
            EnsureDirectory(path);
            var lines = settings.Select(kvp => $"{Esc(kvp.Key)}{Sep}{Esc(kvp.Value)}");
            File.WriteAllLines(path, lines);
        }

        public static Dictionary<string, string> LoadSettings(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;

            foreach (var line in File.ReadAllLines(path))
            {
                var idx = IndexOfUnescaped(line);
                if (idx > 0)
                    result[Unesc(line.Substring(0, idx))] = Unesc(line.Substring(idx + 1));
            }

            return result;
        }

        /// <summary>
        /// Join values into a single separated string, escaping any separator they
        /// contain. Callers that compose composite keys (see SearchScopeStore) share
        /// this rather than rolling their own copy of the escape scheme.
        /// </summary>
        public static string Join(params string[] parts) => string.Join(Sep.ToString(), parts.Select(Esc));

        /// <summary>Inverse of <see cref="Join"/>: split on unescaped separators and unescape each part.</summary>
        public static string[] Split(string value)
        {
            var parts = new List<string>();
            int start = 0;

            while (true)
            {
                int idx = IndexOfUnescaped(value, start);
                if (idx < 0) break;

                parts.Add(Unesc(value.Substring(start, idx - start)));
                start = idx + 1;
            }

            parts.Add(Unesc(value.Substring(start)));
            return parts.ToArray();
        }

        /// <summary>
        /// Position of the first separator at or after <paramref name="startIndex"/> that
        /// isn't part of an escape sequence. Keys may legitimately contain an escaped
        /// separator (scope.txt composes "D|server|database" keys), so a plain IndexOf
        /// would split mid-key.
        /// </summary>
        private static int IndexOfUnescaped(string line, int startIndex = 0)
        {
            for (int i = startIndex; i < line.Length; i++)
            {
                if (line[i] == '\\') i++;          // skip the escaped character
                else if (line[i] == Sep) return i;
            }
            return -1;
        }

        private static string Esc(string s) => s?.Replace("\\", "\\\\").Replace("|", "\\|") ?? "";
        private static string Unesc(string s) => s.Replace("\\|", "|").Replace("\\\\", "\\");

        private static void EnsureDirectory(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
