// Temporary diagnostic — dump available SSMS APIs to Desktop
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

public static class ApiDumper
{
    public static void Dump()
    {
        var sb = new StringBuilder();

        // Find ServiceCache
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var sc = asm.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
            if (sc != null)
            {
                sb.AppendLine($"=== ServiceCache found in {asm.GetName().Name} ===");
                foreach (var p in sc.GetProperties(BindingFlags.Public | BindingFlags.Static))
                    sb.AppendLine($"  Property: {p.Name} ({p.PropertyType.Name})");
                foreach (var m in sc.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    sb.AppendLine($"  Method: {m.Name}");

                // Dump ScriptFactory methods
                var sfProp = sc.GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static);
                if (sfProp != null)
                {
                    var sf = sfProp.GetValue(null);
                    if (sf != null)
                    {
                        sb.AppendLine($"\n=== ScriptFactory type: {sf.GetType().FullName} ===");
                        foreach (var m in sf.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            var pars = string.Join(", ", m.GetParameters().Select(p2 => $"{p2.ParameterType.Name} {p2.Name}"));
                            sb.AppendLine($"  {m.Name}({pars})");
                        }
                    }
                }
            }

            // Find ObjectExplorerManager
            var oem = asm.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.ObjectExplorerManager");
            if (oem != null)
            {
                sb.AppendLine($"\n=== ObjectExplorerManager found in {asm.GetName().Name} ===");
                foreach (var m in oem.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var pars = string.Join(", ", m.GetParameters().Select(p2 => $"{p2.ParameterType.Name} {p2.Name}"));
                    sb.AppendLine($"  {m.Name}({pars})");
                }
            }
        }

        File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SqlPilot_APIs.txt"), sb.ToString());
    }
}
