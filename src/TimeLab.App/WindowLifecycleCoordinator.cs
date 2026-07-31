namespace TimeLab.App;

internal interface IMainWindowHost
{
    void ShowAndActivate();

    void Hide();

    void Close();
}

internal readonly struct TrayIconHandle : IDisposable
{
    private readonly Action<string> _showBalloon;
    private readonly Action _dispose;

    internal TrayIconHandle(Action<string> showBalloon, Action dispose)
    {
        _showBalloon = showBalloon;
        _dispose = dispose;
    }

    internal void ShowBalloon(string message) => _showBalloon(message);

    public void Dispose() => _dispose();
}

/// <summary>
/// 协调主窗口、系统托盘和退出确认流程。
/// </summary>
internal sealed class WindowLifecycleCoordinator : IDisposable
{
    private readonly IMainWindowHost _host;
    private readonly IWindowDialogService _dialogs;
    private readonly TrayIconHandle _trayIcon;
    private MainViewModel? _viewModel;
    private bool _isExitInProgress;
    private bool _actuallyQuit;
    private bool _isDisposed;

    internal WindowLifecycleCoordinator(
        IMainWindowHost host,
        IWindowDialogService dialogs)
        : this(host, dialogs, CreateTrayIcon)
    {
    }

    internal WindowLifecycleCoordinator(
        IMainWindowHost host,
        IWindowDialogService dialogs,
        Func<Action, Action, TrayIconHandle> trayIconFactory)
    {
        _host = host;
        _dialogs = dialogs;
        _trayIcon = trayIconFactory(ShowFromTray, OnExitRequested);
    }

    internal void Attach(MainViewModel viewModel)
    {
        if (_viewModel is not null)
            throw new InvalidOperationException("窗口生命周期协调器只能绑定一次 ViewModel。");

        _viewModel = viewModel;
    }

    internal void ShowBalloon(string message) => _trayIcon.ShowBalloon(message);

    internal void ShowFromTray() => _host.ShowAndActivate();

    internal bool HandleClosing()
    {
        if (_actuallyQuit)
            return false;

        _host.Hide();
        return true;
    }

    internal async Task ExitFromTrayAsync()
    {
        if (_isExitInProgress || _viewModel is null)
            return;

        _isExitInProgress = true;
        try
        {
            var saveActiveTimer = false;
            if (_viewModel.Timer.IsTimerActive)
            {
                _host.ShowAndActivate();
                var choice = _dialogs.ConfirmActiveTimerExit();
                if (choice == ActiveTimerExitChoice.Cancel)
                    return;

                saveActiveTimer = choice == ActiveTimerExitChoice.Save;
            }

            try
            {
                await _viewModel.PrepareForExitAsync(saveActiveTimer);
            }
            catch (Exception exception)
            {
                _dialogs.ShowSaveFailure(exception.Message);
                return;
            }

            _actuallyQuit = true;
            _host.Close();
        }
        finally
        {
            if (!_actuallyQuit)
                _isExitInProgress = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _trayIcon.Dispose();
    }

    private async void OnExitRequested()
    {
        await ExitFromTrayAsync();
    }

    private static TrayIconHandle CreateTrayIcon(Action onShowRequested, Action onExitRequested)
    {
        var service = new TrayIconService(onShowRequested, onExitRequested);
        return new TrayIconHandle(service.ShowBalloon, service.Dispose);
    }
}
