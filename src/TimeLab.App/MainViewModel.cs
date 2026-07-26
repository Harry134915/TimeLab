using System.Windows.Threading;

namespace TimeLab.App;

/// <summary>
/// 主窗口的根 ViewModel，负责主题、通知、启动加载和退出协调。
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly WorkspaceInteractionState _interactionState;
    private readonly Action<string>? _onBalloon;
    private readonly Action? _onToggleDark;
    private DispatcherTimer? _notificationTimer;
    private string _notificationMessage = string.Empty;
    private bool _isNotificationVisible;
    private bool _isErrorNotification;
    private bool _isDarkMode;

    internal MainViewModel(
        TaskListViewModel taskList,
        TimerViewModel timer,
        SessionLogViewModel sessionLog,
        WorkspaceInteractionState interactionState,
        Action<string>? onBalloon = null,
        Action? onToggleDark = null)
    {
        TaskList = taskList;
        Timer = timer;
        SessionLog = sessionLog;
        _interactionState = interactionState;
        _onBalloon = onBalloon;
        _onToggleDark = onToggleDark;

        TaskList.NotificationRequested += ShowNotification;
        Timer.NotificationRequested += ShowNotification;
        SessionLog.NotificationRequested += ShowNotification;
    }

    public TaskListViewModel TaskList { get; }
    public TimerViewModel Timer { get; }
    public SessionLogViewModel SessionLog { get; }

    public string NotificationMessage
    {
        get => _notificationMessage;
        private set => SetProperty(ref _notificationMessage, value);
    }

    public bool IsNotificationVisible
    {
        get => _isNotificationVisible;
        private set => SetProperty(ref _isNotificationVisible, value);
    }

    public bool IsErrorNotification
    {
        get => _isErrorNotification;
        private set => SetProperty(ref _isErrorNotification, value);
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (!SetProperty(ref _isDarkMode, value))
                return;
            _onToggleDark?.Invoke();
            OnPropertyChanged(nameof(DarkToggleText));
        }
    }

    public string DarkToggleText => IsDarkMode ? "深色" : "浅色";

    /// <summary>
    /// 分模块加载任务和专注记录；单个模块失败时保留其他模块的可用状态。
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            await TaskList.LoadAsync();
        }
        catch (Exception exception)
        {
            ShowNotification($"任务加载失败：{exception.Message}", true);
        }

        try
        {
            await SessionLog.LoadAsync();
        }
        catch (Exception exception)
        {
            ShowNotification($"专注记录加载失败：{exception.Message}", true);
        }

        SessionLog.RefreshTodayStats();
        Timer.InitializeState();
    }

    /// <summary>
    /// 冻结新命令，依次等待任务写入、计时处理和 Session 写入安全结束。
    /// </summary>
    public async Task PrepareForExitAsync(bool saveActiveTimer)
    {
        if (_interactionState.IsExitPreparationInProgress)
            return;

        _interactionState.SetExitPreparationInProgress(true);
        try
        {
            await TaskList.WaitForPendingOperationsAsync();
            await Timer.PrepareForExitAsync(saveActiveTimer);
            await SessionLog.WaitForPendingOperationsAsync();
        }
        catch
        {
            _interactionState.SetExitPreparationInProgress(false);
            throw;
        }
    }

    private void ShowNotification(string message, bool isError)
    {
        NotificationMessage = message;
        IsErrorNotification = isError;
        IsNotificationVisible = true;

        try
        {
            _onBalloon?.Invoke(message);
        }
        catch
        {
            // 托盘通知失败不应阻断当前操作或数据保存。
        }

        _notificationTimer?.Stop();
        _notificationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _notificationTimer.Tick += (_, _) =>
        {
            IsNotificationVisible = false;
            NotificationMessage = string.Empty;
            _notificationTimer?.Stop();
        };
        _notificationTimer.Start();
    }
}
