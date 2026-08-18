using System;
using System.Windows.Input;

namespace SqlPilot.UI.Commands
{
    /// <summary>
    /// Minimal always-executable ICommand for KeyBindings.
    ///
    /// Lives in SqlPilot.UI so both the SSMS tool window and the demo can use it —
    /// a copy in SqlPilot.Package would also need a &lt;Compile Include Link&gt; entry in
    /// the Legacy csproj to reach the SSMS 18/20 build.
    /// </summary>
    public sealed class RelayInputCommand : ICommand
    {
        private readonly Action _execute;

        public RelayInputCommand(Action execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _execute();
    }
}
