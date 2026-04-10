using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SqlPilot.Package
{
    [Guid("c7d8e9f0-1a2b-3c4d-5e6f-7a8b9c0d1e2f")]
    public class SqlPilotToolWindow : ToolWindowPane
    {
        public SqlPilotToolWindow() : base(null)
        {
            Caption = "SQL Pilot";
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (Package is SqlPilotPackage package)
            {
                Content = new SqlPilotToolWindowControl(package);
            }
        }
    }
}
