using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace SqlPilot.Installer.Services
{
    /// <summary>
    /// Detects and (optionally) closes running SSMS instances. Used by the
    /// installer's warning banner: a locked DLL on a running SSMS is the most
    /// common installer failure mode, so we detect it up front and let the
    /// user close it with one click.
    /// </summary>
    internal static class ProcessHelper
    {
        public static Process[] GetRunningSsms()
        {
            try
            {
                return Process.GetProcessesByName("Ssms");
            }
            catch
            {
                return Array.Empty<Process>();
            }
        }

        public static bool IsAnySsmsRunning()
        {
            var procs = GetRunningSsms();
            try { return procs.Length > 0; }
            finally { foreach (var p in procs) p.Dispose(); }
        }

        /// <summary>
        /// Asks every running SSMS to close gracefully (CloseMainWindow), waits a
        /// few seconds, then force-kills any that didn't comply. Safe to call
        /// when no SSMS is running.
        /// </summary>
        public static async Task StopAllSsmsAsync()
        {
            var initial = GetRunningSsms();
            if (initial.Length == 0) return;

            try
            {
                foreach (var p in initial)
                {
                    try { p.CloseMainWindow(); }
                    catch { /* process may have exited between enumerate and close */ }
                }
            }
            finally
            {
                foreach (var p in initial) p.Dispose();
            }

            // Give SSMS up to 5 seconds to honor the close request. Each
            // GetRunningSsms() call returns fresh Process handles that we own
            // and must dispose.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var current = GetRunningSsms();
                try
                {
                    if (!current.Any(p => !p.HasExited)) break;
                }
                finally
                {
                    foreach (var p in current) p.Dispose();
                }
                await Task.Delay(250);
            }

            var stragglers = GetRunningSsms();
            try
            {
                foreach (var p in stragglers)
                {
                    try { if (!p.HasExited) p.Kill(); }
                    catch { /* already gone */ }
                }
            }
            finally
            {
                foreach (var p in stragglers) p.Dispose();
            }
        }
    }
}
