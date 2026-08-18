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

        /// <summary>
        /// Put keyboard focus on the first server so arrow keys and Space work straight
        /// away — the panel is useless to a keyboard user if opening it leaves focus behind.
        /// </summary>
        public void FocusTree()
        {
            // Containers don't exist until the panel has been laid out at least once.
            ScopeTree.UpdateLayout();

            if (ScopeTree.Items.Count > 0
                && ScopeTree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem first)
            {
                first.Focus();
            }
            else
            {
                ScopeTree.Focus();
            }
        }
    }
}
