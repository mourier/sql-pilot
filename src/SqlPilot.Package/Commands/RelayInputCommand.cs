using System;
using System.Windows.Input;

namespace SqlPilot.Package.Commands
{
    internal class RelayInputCommand : ICommand
    {
        private readonly Action _execute;

        public RelayInputCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute();
    }
}
