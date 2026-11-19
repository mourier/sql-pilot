using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SqlPilot.Package.Integration;

namespace SqlPilot.Package
{
    [Guid("c7d8e9f0-1a2b-3c4d-5e6f-7a8b9c0d1e2f")]
    public class SqlPilotToolWindow : ToolWindowPane
    {
        private SsmsThemeAdapter _themeAdapter;

        public SqlPilotToolWindow() : base(null)
        {
            Caption = "SQL Pilot";
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (Package is SqlPilotPackage package)
            {
                var control = new SqlPilotToolWindowControl(package);

                // Before the control is shown, so it never paints unthemed.
                _themeAdapter = new SsmsThemeAdapter(control);
                Content = control;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _themeAdapter?.Dispose();
                _themeAdapter = null;
            }
            base.Dispose(disposing);
        }
    }
}
