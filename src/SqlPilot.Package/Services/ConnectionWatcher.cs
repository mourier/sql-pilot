using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.Shell;

namespace SqlPilot.Package.Services
{
    /// <summary>
    /// Polls Object Explorer every few seconds for new server connections.
    /// Fires ServersConnected once per poll in which new servers turned up.
    /// </summary>
    public sealed class ConnectionWatcher : IDisposable
    {
        private readonly SqlPilotPackage _package;
        private readonly Timer _timer;
        private readonly HashSet<string> _knownServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public event Action ServersConnected;

        public ConnectionWatcher(SqlPilotPackage package, int intervalMs = 3000)
        {
            _package = package;
            _timer = new Timer(OnTick, null, intervalMs, intervalMs);
        }

        private void OnTick(object state)
        {
            if (_disposed) return;

            try
            {
                // Must access OE on the UI thread
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    var servers = _package.ObjectExplorerBridge.GetConnectedServerNames();

                    bool anyNew = false;
                    foreach (var server in servers)
                    {
                        if (_knownServers.Add(server))
                        {
                            Debug.WriteLine($"SqlPilot: New server detected: {server}");
                            anyNew = true;
                        }
                    }

                    // One event however many servers appeared: a refresh re-indexes
                    // every connected server anyway.
                    if (anyNew)
                        ServersConnected?.Invoke();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SqlPilot: ConnectionWatcher error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer?.Dispose();
        }
    }
}
