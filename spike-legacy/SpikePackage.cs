using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SqlPilot.Spike
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("SQL Pilot Spike", "SSMS 22 extension loading test", "0.0.1")]
    [Guid("11111111-2222-3333-4444-555566667777")]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(SpikeToolWindow), Style = VsDockStyle.Tabbed,
        Window = "d114938f-591c-46cf-a785-500a82d97410")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class SpikePackage : AsyncPackage
    {
        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            // Log to Activity Log
            var log = await GetServiceAsync(typeof(SVsActivityLog)) as IVsActivityLog;
            log?.LogEntry(
                (uint)__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION,
                "SqlPilot.Spike",
                "SQL Pilot Spike package loaded successfully in SSMS 22!");

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Try to get Object Explorer service
            Type oeServiceType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                oeServiceType = assembly.GetType(
                    "Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.IObjectExplorerService");
                if (oeServiceType != null) break;
            }

            string oeStatus;
            if (oeServiceType != null)
            {
                var oeService = GetService(oeServiceType);
                oeStatus = oeService != null
                    ? $"IObjectExplorerService: FOUND (type: {oeService.GetType().FullName})"
                    : "IObjectExplorerService type found but service instance is null";

                // Try reflection to get Tree
                if (oeService != null)
                {
                    var treeProp = oeService.GetType().GetProperty("Tree",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                    oeStatus += treeProp != null
                        ? "\nTree property: FOUND"
                        : "\nTree property: NOT FOUND";
                }
            }
            else
            {
                oeStatus = "IObjectExplorerService type NOT FOUND in loaded assemblies";
            }

            // Try to get DTE
            var dte = GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            string dteStatus = dte != null
                ? $"DTE2: FOUND (Version: {dte.Version})"
                : "DTE2: NOT FOUND";

            // Check SMO availability
            string smoStatus;
            try
            {
                var smoAssembly = Assembly.Load("Microsoft.SqlServer.Smo");
                smoStatus = $"SMO: LOADED (Version: {smoAssembly.GetName().Version})";
            }
            catch (Exception ex)
            {
                smoStatus = $"SMO: FAILED ({ex.GetType().Name}: {ex.Message})";
            }

            // Log results
            string report = $@"
=== SQL Pilot Spike Report ===
Package: Loaded OK
{dteStatus}
{oeStatus}
{smoStatus}
SSMS PID: {Process.GetCurrentProcess().Id}
CLR: {Environment.Version}
64-bit: {Environment.Is64BitProcess}
===============================";

            log?.LogEntry(
                (uint)__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION,
                "SqlPilot.Spike",
                report);

            // Also write to a file for easy reading
            string reportPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "SqlPilot_Spike_Report.txt");
            System.IO.File.WriteAllText(reportPath, report);

            // Show the tool window with results
            try
            {
                var window = FindToolWindow(typeof(SpikeToolWindow), 0, true);
                if (window?.Frame is IVsWindowFrame frame)
                {
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
                    if (window is SpikeToolWindow spikeWindow)
                    {
                        spikeWindow.SetReport(report);
                    }
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(reportPath, $"\nToolWindow error: {ex}");
            }
        }
    }

    [Guid("11111111-2222-3333-4444-555566668888")]
    public class SpikeToolWindow : ToolWindowPane
    {
        private TextBox _textBox;

        public SpikeToolWindow() : base(null)
        {
            Caption = "SQL Pilot Spike";

            _textBox = new TextBox
            {
                IsReadOnly = true,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                Text = "Loading spike results..."
            };

            Content = _textBox;
        }

        public void SetReport(string report)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _textBox.Text = report;
        }
    }
}
