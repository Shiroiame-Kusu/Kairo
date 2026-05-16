using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace Kairo.ViewModels
{
    /// <summary>
    /// Simple ICommand implementations for synchronous and asynchronous actions.
    /// </summary>
    internal static class UiThreadHelper
    {
        public static void RaiseOnUi(EventHandler? handler, object sender)
        {
            if (handler == null) return;
            if (Dispatcher.UIThread.CheckAccess())
                handler(sender, EventArgs.Empty);
            else
                Dispatcher.UIThread.Post(() => handler(sender, EventArgs.Empty));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => UiThreadHelper.RaiseOnUi(CanExecuteChanged, this);
    }

    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        private readonly Func<bool>? _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            _isRunning = true;
            RaiseCanExecuteChanged();
            try
            {
                await _executeAsync();
            }
            finally
            {
                _isRunning = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() => UiThreadHelper.RaiseOnUi(CanExecuteChanged, this);
    }
}
