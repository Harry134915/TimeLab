using System.Windows.Input;

namespace TimeLab.App;

/// <summary>
/// 支持等待、异常处理和执行期间防重复触发的异步命令。
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<Exception> _onException;
    private bool _isExecuting;

    public AsyncRelayCommand(
        Func<object?, Task> execute,
        Action<Exception> onException,
        Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _onException = onException;
        _canExecute = canExecute;
    }

    public bool IsExecuting => _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute(parameter);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync(parameter);
        }
        catch (Exception exception)
        {
            _onException(exception);
        }
    }

    private void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
