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
                var parts = line.Split(Sep);
                if (parts.Length < 5) continue;

                if (int.TryParse(parts[4], out var typeInt))
                {
                    results.Add(new DatabaseObject
                    {
                        ServerName = Unesc(parts[0]),
                        DatabaseName = Unesc(parts[1]),
                        SchemaName = Unesc(parts[2]),
                        ObjectName = Unesc(parts[3]),
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
                var idx = line.IndexOf(Sep);
                if (idx > 0)
                    result[Unesc(line.Substring(0, idx))] = Unesc(line.Substring(idx + 1));
            }

            return result;
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
