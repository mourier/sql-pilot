using System;
using System.Collections.Concurrent;

namespace SqlPilot.Package.Integration
{
    /// <summary>
    /// Shared cache for reflection-based type lookups against SSMS internal assemblies.
    /// Both <see cref="ScriptingBridge"/> and <see cref="ObjectExplorerBridge"/> reach
    /// into SSMS internals by assembly-walking for types like
    /// <c>Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.INodeInformation</c>;
    /// the walk is expensive because SSMS loads ~150 assemblies, and the result never
    /// changes at runtime. Null results are cached intentionally — a type that isn't
    /// present in the current SSMS version won't appear later.
    /// </summary>
    internal static class ReflectionTypeCache
    {
        private static readonly ConcurrentDictionary<string, Type> Cache =
            new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);

        public static Type FindType(string typeName)
        {
            return Cache.GetOrAdd(typeName, name =>
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType(name);
                    if (type != null) return type;
                }
                return null;
            });
        }
    }
}
