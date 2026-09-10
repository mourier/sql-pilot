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
            // The panel goes Collapsed -> Visible in the same handler, so containers
            // don't exist yet; force the layout the focus call depends on. An empty
            // tree has nothing to lay out, and falls through to focusing itself.
            if (ScopeTree.Items.Count > 0)
                ScopeTree.UpdateLayout();

            if (ScopeTree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem first)
                first.Focus();
            else
                ScopeTree.Focus();
        }
    }
}
