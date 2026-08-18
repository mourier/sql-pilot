using System.Windows.Controls;

namespace SqlPilot.UI.Controls
{
    /// <summary>
    /// The scope checkbox tree — servers with their databases. Shared by the SSMS
    /// tool window and the demo app; its DataContext is a
    /// <see cref="ViewModels.ScopeViewModel"/>.
    /// </summary>
    public partial class ScopeControl : UserControl
    {
        public ScopeControl()
        {
            InitializeComponent();
        }

        /// <summary>Disabled while an index pass is running so toggles can't overlap it.</summary>
        public void SetTreeEnabled(bool enabled) => ScopeTree.IsEnabled = enabled;
    }
}
