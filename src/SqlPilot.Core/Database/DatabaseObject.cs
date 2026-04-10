using System;

namespace SqlPilot.Core.Database
{
    public sealed class DatabaseObject : IEquatable<DatabaseObject>
    {
        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public string SchemaName { get; set; }
        public string ObjectName { get; set; }
        public DatabaseObjectType ObjectType { get; set; }

        public string QualifiedName => $"[{SchemaName}].[{ObjectName}]";

        public string FullPath => $"{ServerName}/{DatabaseName}/{QualifiedName}";

        public bool Equals(DatabaseObject other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(ServerName, other.ServerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(DatabaseName, other.DatabaseName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(SchemaName, other.SchemaName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ObjectName, other.ObjectName, StringComparison.OrdinalIgnoreCase)
                && ObjectType == other.ObjectType;
        }

        public override bool Equals(object obj) => Equals(obj as DatabaseObject);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(ServerName ?? "");
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(DatabaseName ?? "");
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(SchemaName ?? "");
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(ObjectName ?? "");
                hash = hash * 31 + (int)ObjectType;
                return hash;
            }
        }

        public override string ToString() => $"{DatabaseName}.{QualifiedName} ({ObjectType})";
    }
}
