using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SqlPilot.Package.Commands
{
    internal sealed class OpenSearchCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("a1b2c3d4-5e6f-7a8b-9c0d-e1f2a3b4c5d6");

        private readonly SqlPilotPackage _package;
        private static HwndSource _hwndSource;
        private const int HOTKEY_ID = 0x5150; // "SP"
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int VK_D = 0x44;
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private OpenSearchCommand(SqlPilotPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            if (commandService != null)
            {
                var menuCommandId = new CommandID(CommandSet, CommandId);
                var menuItem = new MenuCommand(Execute, menuCommandId);
                commandService.AddCommand(menuItem);
            }
        }

        public static OpenSearchCommand Instance { get; private set; }

        public static async Task InitializeAsync(SqlPilotPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService))
                as OleMenuCommandService;
            Instance = new OpenSearchCommand(package, commandService);

            // Register Ctrl+D global hotkey via Win32
            RegisterGlobalHotkey();
        }

        private static void RegisterGlobalHotkey()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow == null) return;

                var helper = new WindowInteropHelper(mainWindow);
                var hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero) return;

                _hwndSource = HwndSource.FromHwnd(hwnd);
                _hwndSource?.AddHook(WndProc);

                RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL, VK_D);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot: Failed to register hotkey: {ex.Message}");
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Instance?.ShowWindow();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void Execute(object sender, EventArgs e) => ShowWindow();

        public void ShowWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var window = _package.FindToolWindow(typeof(SqlPilotToolWindow), 0, true);
            if (window?.Frame == null)
                return;

            var windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());

            if (window.Content is SqlPilotToolWindowControl control)
            {
                control.FocusSearch();
            }
        }

        public static void Cleanup()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    var helper = new WindowInteropHelper(mainWindow);
                    UnregisterHotKey(helper.Handle, HOTKEY_ID);
                }
                _hwndSource?.RemoveHook(WndProc);
            }
            catch { }
        }
    }
}
