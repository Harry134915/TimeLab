using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Media;
using System.Windows.Input;
using System.Windows.Threading;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.App;

/// <summary>
/// 主窗口的 ViewModel，管理 Todo 列表、番茄钟和专注记录的 UI 交互
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly TaskService _taskService;
    private readonly PomodoroService _pomodoroService;
    private readonly Action<string>? _onBalloon;
    private readonly Action? _onToggleDark;
    private readonly Func<string, bool> _confirmDelete;
    private readonly Action _onTimerReached;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly SemaphoreSlim _timerOperationGate = new(1, 1);
    private readonly SemaphoreSlim _taskMutationGate = new(1, 1);
    private readonly SemaphoreSlim _sessionMutationGate = new(1, 1);

    private DispatcherTimer? _notificationTimer;
    private bool _advanceModeOnTarget;
    private TimerStatus? _publishedTimerStatus;
    private int _publishedTargetSeconds = -1;
    private bool? _publishedCycleActive;
    private bool _isTimerOperationInProgress;
    private int _pendingTaskMutationCount;
    private int _pendingSessionMutationCount;
    private bool _isExitPreparationInProgress;
    private TimeSpan? _lastSessionDuration;
    private int _lastSessionTargetSeconds;
    private TargetSaveRetry? _pendingTargetSaveRetry;

    private sealed record TargetSaveRetry(bool IsCycle, string NotificationMessage, int TargetSeconds);

    public MainViewModel(TaskService taskService, PomodoroService pomodoroService,
                         Action<string>? onBalloon = null, Action? onToggleDark = null,
                         Func<string, bool>? onConfirmDelete = null,
                         Action? onTimerReached = null)
    {
        _taskService = taskService;
        _pomodoroService = pomodoroService;
        _onBalloon = onBalloon;
        _onToggleDark = onToggleDark;
        _confirmDelete = onConfirmDelete ?? (_ => false);
        _onTimerReached = onTimerReached ?? SystemSounds.Beep.Play;

        // 每秒刷新计时显示
        _tick.Tick += async (_, _) => await HandleTimerTickAsync();

        AddTaskCommand = new AsyncRelayCommand(_ => RunTaskMutationAsync(AddTaskAsync), HandleCommandException, _ => CanAddTask());
        CompleteTaskCommand = new AsyncRelayCommand(
            p => RunTaskMutationAsync(() => CompleteTaskAsync((Guid)p!)), HandleCommandException, CanCompleteTask);
        DeleteTaskCommand = new AsyncRelayCommand(
            p => RunTaskMutationAsync(() => DeleteTaskAsync((Guid)p!)), HandleCommandException, CanDeleteTask);
        DeleteSessionCommand = new AsyncRelayCommand(
            p => RunSessionMutationAsync(() => DeleteSessionAsync((Guid)p!)), HandleCommandException,
            _ => !_isExitPreparationInProgress && !_isTimerOperationInProgress && !IsSessionMutationInProgress);
        SelectTaskCommand = new RelayCommand(p => SelectTask((Guid)p!), CanSelectTask);
        ClearSelectedTaskCommand = new RelayCommand(_ => ClearSelectedTask(), _ => CanClearSelectedTask());
        StartTimerCommand = new AsyncRelayCommand(_ => RunTimerOperationAsync(StartTimerAsync), HandleCommandException, _ => CanStartOrResumeTimer());
        PauseTimerCommand = new AsyncRelayCommand(_ => RunTimerOperationAsync(PauseTimerAsync), HandleCommandException, _ => IsTimerRunning());
        StopTimerCommand = new AsyncRelayCommand(_ => RunTimerOperationAsync(StopTimerAsync), HandleCommandException,
            _ => !_isExitPreparationInProgress && !_isTimerOperationInProgress && IsTimerActive);
        ResetTimerCommand = new AsyncRelayCommand(_ => RunTimerOperationAsync(ResetTimerAsync), HandleCommandException, _ => CanResetTimer());
        ToggleTimerCommand = new AsyncRelayCommand(_ => RunTimerOperationAsync(ToggleTimerAsync), HandleCommandException,
            _ => !_isExitPreparationInProgress && !_isTimerOperationInProgress && (IsTimerActive || !IsTaskMutationInProgress));
        StopOrResetCommand = new AsyncRelayCommand(_ => RunTimerOperationAsync(StopOrResetAsync), HandleCommandException,
            _ => !_isExitPreparationInProgress && !_isTimerOperationInProgress);
        StartPresetCommand = new AsyncRelayCommand(p => RunTimerOperationAsync(() => StartPresetAsync((int)p!)), HandleCommandException,
            p => CanStartNewTimer() && p is int minutes && minutes > 0);
        StartCycleCommand = new AsyncRelayCommand(_ => RunTimerOperationAsync(StartCycleAsync), HandleCommandException,
            _ => CanStartNewTimer() && IsCycleConfigurationValid());
    }

    /// <summary>任务列表</summary>
    public ObservableCollection<TaskItem> Tasks { get; } = [];

    /// <summary>专注记录列表</summary>
    public ObservableCollection<PomodoroSession> Sessions { get; } = [];

    /// <summary>新任务输入框文本</summary>
    private string _newTaskTitle = string.Empty;
    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set
        {
            if (_newTaskTitle == value) return;
            _newTaskTitle = value;
            OnPropertyChanged(nameof(NewTaskTitle));
            NotifyCommandStatesChanged();
        }
    }

    /// <summary>新任务时长数值</summary>
    private string _newTaskDuration = string.Empty;
    public string NewTaskDuration
    {
        get => _newTaskDuration;
        set
        {
            if (_newTaskDuration == value) return;
            _newTaskDuration = value;
            OnPropertyChanged(nameof(NewTaskDuration));
            OnPropertyChanged(nameof(NewTaskDurationError));
            NotifyCommandStatesChanged();
        }
    }

    /// <summary>时长单位索引：0=秒, 1=分钟, 2=时</summary>
    private int _durationUnitIndex;
    public int DurationUnitIndex
    {
        get => _durationUnitIndex;
        set
        {
            if (_durationUnitIndex == value) return;
            _durationUnitIndex = value;
            OnPropertyChanged(nameof(DurationUnitIndex));
            OnPropertyChanged(nameof(NewTaskDurationError));
            NotifyCommandStatesChanged();
        }
    }

    public string NewTaskDurationError => TryGetDurationSeconds(out _)
        ? string.Empty
        : "请输入大于 0 的整数";

    /// <summary>时长单位列表</summary>
    public List<string> DurationUnits { get; } = ["秒", "分钟", "时"];

    /// <summary>预设时长按钮（分钟），随模式动态切换</summary>
    public int[] PresetMinutes => _pomodoroService.CurrentMode switch
    {
        FocusMode.ShortBreak => [3, 5, 10],
        FocusMode.LongBreak => [10, 15, 30],
        _ => [15, 25, 30, 45, 60]
    };

    /// <summary>当前模式名称</summary>
    public string ModeName => _pomodoroService.CurrentMode switch
    {
        FocusMode.ShortBreak => "短休",
        FocusMode.LongBreak => "长休",
        _ => "专注"
    };

    /// <summary>模式建议时长（分钟）</summary>
    public int ModeDefaultMinutes => _pomodoroService.ModeDefaultSeconds / 60;

    /// <summary>已完成专注轮数</summary>
    public int CompletedFocusCount => _pomodoroService.CompletedFocusCount;

    // ---- 循环模式配置 ----

    /// <summary>循环：专注分钟</summary>
    public int CycleFocusMinutes
    {
        get => ParsePositiveInt(CycleFocusMinutesText);
        set => CycleFocusMinutesText = value.ToString();
    }

    private string _cycleFocusMinutesText = "25";
    public string CycleFocusMinutesText
    {
        get => _cycleFocusMinutesText;
        set
        {
            if (_cycleFocusMinutesText == value) return;
            _cycleFocusMinutesText = value;
            OnPropertyChanged(nameof(CycleFocusMinutesText));
            OnPropertyChanged(nameof(CycleFocusMinutes));
            OnPropertyChanged(nameof(CycleValidationMessage));
            NotifyCommandStatesChanged();
        }
    }

    /// <summary>循环：休息分钟</summary>
    public int CycleBreakMinutes
    {
        get => ParsePositiveInt(CycleBreakMinutesText);
        set => CycleBreakMinutesText = value.ToString();
    }

    private string _cycleBreakMinutesText = "5";
    public string CycleBreakMinutesText
    {
        get => _cycleBreakMinutesText;
        set
        {
            if (_cycleBreakMinutesText == value) return;
            _cycleBreakMinutesText = value;
            OnPropertyChanged(nameof(CycleBreakMinutesText));
            OnPropertyChanged(nameof(CycleBreakMinutes));
            OnPropertyChanged(nameof(CycleValidationMessage));
            NotifyCommandStatesChanged();
        }
    }
    /// <summary>循环：轮数输入文本</summary>
    private string _cycleTotalRoundsText = "4";
    public string CycleTotalRoundsText
    {
        get => _cycleTotalRoundsText;
        set
        {
            if (_cycleTotalRoundsText == value) return;
            _cycleTotalRoundsText = value;
            OnPropertyChanged(nameof(CycleTotalRoundsText));
            OnPropertyChanged(nameof(CycleValidationMessage));
            NotifyCommandStatesChanged();
        }
    }

    public string CycleValidationMessage => IsCycleConfigurationValid()
        ? string.Empty
        : "专注、休息和轮数都必须是大于 0 的整数";

    /// <summary>循环是否激活</summary>
    public bool IsCycleActive => _pomodoroService.IsCycleActive;
    /// <summary>循环进度文字</summary>
    public string CycleProgress => IsCycleActive
        ? $"第 {_pomodoroService.CurrentRound}/{_pomodoroService.CycleTotalRounds} 轮 · {ModeName}"
        : string.Empty;

    /// <summary>今日统计文字</summary>
    private string _todayStats = string.Empty;
    public string TodayStats
    {
        get => _todayStats;
        set { _todayStats = value; OnPropertyChanged(nameof(TodayStats)); }
    }

    /// <summary>是否刚刚到达了计时目标。</summary>
    private bool _isTargetReached;
    public bool IsTargetReached
    {
        get => _isTargetReached;
        set
        {
            if (_isTargetReached == value) return;
            _isTargetReached = value;
            OnPropertyChanged(nameof(IsTargetReached));
        }
    }

    private bool _alarmPlayed;

    /// <summary>通知消息内容</summary>
    private string _notificationMessage = string.Empty;
    public string NotificationMessage
    {
        get => _notificationMessage;
        set { _notificationMessage = value; OnPropertyChanged(nameof(NotificationMessage)); }
    }

    /// <summary>通知条是否可见</summary>
    private bool _isNotificationVisible;
    public bool IsNotificationVisible
    {
        get => _isNotificationVisible;
        set { _isNotificationVisible = value; OnPropertyChanged(nameof(IsNotificationVisible)); }
    }

    private bool _isErrorNotification;
    public bool IsErrorNotification
    {
        get => _isErrorNotification;
        private set
        {
            if (_isErrorNotification == value) return;
            _isErrorNotification = value;
            OnPropertyChanged(nameof(IsErrorNotification));
        }
    }

    /// <summary>计时器时间显示 HH:MM:SS</summary>
    private string _elapsedDisplay = "00:00:00";
    public string ElapsedDisplay
    {
        get => _elapsedDisplay;
        set { _elapsedDisplay = value; OnPropertyChanged(nameof(ElapsedDisplay)); }
    }

    /// <summary>当前选中的关联任务 ID</summary>
    private Guid? _selectedTaskId;
    public Guid? SelectedTaskId
    {
        get => _selectedTaskId;
        set
        {
            if (_selectedTaskId == value) return;
            _selectedTaskId = value;
            OnPropertyChanged(nameof(SelectedTaskId));
            OnPropertyChanged(nameof(SelectedTaskTitle));
            NotifyCommandStatesChanged();
        }
    }

    /// <summary>选中任务的标题，未选中时显示"无"</summary>
    public string SelectedTaskTitle
        => SelectedTaskId.HasValue
            ? Tasks.FirstOrDefault(t => t.Id == SelectedTaskId.Value)?.Title ?? "无"
            : "无";

    /// <summary>计时器状态中文描述</summary>
    private string _statusText = "就绪";
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StartButtonText));
        }
    }

    /// <summary>开始按钮文字随计时状态变化。</summary>
    public string StartButtonText => _pomodoroService.CurrentState.Status switch
    {
        TimerStatus.Paused => "继续",
        TimerStatus.Running => "进行中",
        _ => "开始"
    };

    public bool IsTimerActive => _pomodoroService.CurrentState.Status is TimerStatus.Running or TimerStatus.Paused;
    public bool CanEditTimerSetup => !_isExitPreparationInProgress && !_isTimerOperationInProgress && !IsTimerActive;
    private bool IsTaskMutationInProgress => Volatile.Read(ref _pendingTaskMutationCount) > 0;
    private bool IsSessionMutationInProgress => Volatile.Read(ref _pendingSessionMutationCount) > 0;
    public string TimerDisplayLabel => _lastSessionDuration.HasValue
        && _pomodoroService.CurrentState.Status == TimerStatus.Stopped
            ? IsTargetReached ? "已完成" : "本次专注"
            : _pomodoroService.TargetSeconds > 0 ? "剩余时间" : "已用时间";
    public string TimerTargetSummary
    {
        get
        {
            if (_lastSessionDuration.HasValue && _pomodoroService.CurrentState.Status == TimerStatus.Stopped)
                return _lastSessionTargetSeconds > 0
                    ? $"目标 {FormatSeconds(_lastSessionTargetSeconds)}"
                    : "已保存到专注记录";

            return _pomodoroService.TargetSeconds > 0
                ? $"目标 {FormatSeconds(_pomodoroService.TargetSeconds)}"
                : $"{ModeName} · 不限时";
        }
    }

    public AsyncRelayCommand AddTaskCommand { get; }
    public AsyncRelayCommand CompleteTaskCommand { get; }
    public AsyncRelayCommand DeleteTaskCommand { get; }
    public AsyncRelayCommand DeleteSessionCommand { get; }
    public RelayCommand SelectTaskCommand { get; }
    public RelayCommand ClearSelectedTaskCommand { get; }
    public AsyncRelayCommand StartTimerCommand { get; }
    public AsyncRelayCommand PauseTimerCommand { get; }
    public AsyncRelayCommand StopTimerCommand { get; }
    public AsyncRelayCommand ResetTimerCommand { get; }
    public AsyncRelayCommand ToggleTimerCommand { get; }
    public AsyncRelayCommand StopOrResetCommand { get; }
    public AsyncRelayCommand StartPresetCommand { get; }
    public AsyncRelayCommand StartCycleCommand { get; }
    /// <summary>是否深色模式</summary>
    private bool _isDarkMode;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode == value) return;
            _isDarkMode = value;
            _onToggleDark?.Invoke();
            OnPropertyChanged(nameof(IsDarkMode));
            OnPropertyChanged(nameof(DarkToggleText));
        }
    }

    /// <summary>深色切换按钮文字</summary>
    public string DarkToggleText => _isDarkMode ? "深色" : "浅色";

    /// <summary>启动时加载已保存的任务和专注记录</summary>
    public async Task LoadAsync()
    {
        try
        {
            var tasks = await _taskService.GetAllAsync();
            foreach (var t in tasks)
                Tasks.Add(t);
        }
        catch (Exception exception)
        {
            ShowNotification($"任务加载失败：{exception.Message}", isError: true);
        }

        try
        {
            var sessions = await _pomodoroService.GetSessionsAsync();
            foreach (var s in sessions)
                Sessions.Add(s);
        }
        catch (Exception exception)
        {
            ShowNotification($"专注记录加载失败：{exception.Message}", isError: true);
        }

        RefreshTodayStats();
        PublishTimerState(force: true);
    }

    /// <summary>创建新任务并添加到列表</summary>
    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle) || !TryGetDurationSeconds(out var seconds))
            return;

        var item = await _taskService.CreateAsync(NewTaskTitle.Trim(), seconds);
        Tasks.Add(item);
        NewTaskTitle = string.Empty;
        NewTaskDuration = string.Empty;
    }

    /// <summary>
    /// 显示应用内通知，并同步触发系统托盘提醒。
    /// </summary>
    private void ShowNotification(string message, bool isError = false)
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

        //防止连续通知
        _notificationTimer?.Stop();

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

        _notificationTimer.Tick += (_, _) =>
        {
            IsNotificationVisible = false;
            NotificationMessage = string.Empty;
            _notificationTimer?.Stop();
        };

        _notificationTimer?.Start();
    }

    /// <summary>获取当前关联任务的计划时长；未关联或未设时长时返回 0。</summary>
    private int GetSelectedTaskTargetSeconds()
    {
        return SelectedTaskId.HasValue
            ? Tasks.FirstOrDefault(t => t.Id == SelectedTaskId.Value)?.PlannedSeconds ?? 0
            : 0;
    }

    /// <summary>预设和任务时长同时有效时取较小值。</summary>
    private int ResolvePresetTargetSeconds(int presetSeconds)
    {
        if (_pomodoroService.CurrentMode != FocusMode.Focus)
            return presetSeconds;

        var taskSeconds = GetSelectedTaskTargetSeconds();
        return taskSeconds > 0 ? Math.Min(presetSeconds, taskSeconds) : presetSeconds;
    }

    private Guid? GetTaskIdForCurrentMode() =>
        _pomodoroService.CurrentMode == FocusMode.Focus ? SelectedTaskId : null;

    /// <summary>
    /// 根据当前计时模式和关联任务生成到时提醒文案。
    /// </summary>
    private string TargetDescription()
    {
        // 循环模式的提示
        if (_pomodoroService.IsCycleActive)
        {
            var r = _pomodoroService.CurrentRound;
            var t = _pomodoroService.CycleTotalRounds;

            if (_pomodoroService.CurrentMode == FocusMode.Focus)
            {
                return r >= t
                    ? $"第 {r}/{t} 轮专注完成！休息后结束"
                    : $"第 {r}/{t} 轮专注完成！休息一下";
            }

            return r >= t
                ? $"全部完成！共 {t} 轮"
                : _pomodoroService.CurrentMode == FocusMode.ShortBreak
                    ? $"第 {r}/{t} 轮短休结束，开始下一轮专注"
                    : $"第 {r}/{t} 轮长休结束，下一轮专注";
        }

        // 普通预设模式
        if (_advanceModeOnTarget)
        {
            return _pomodoroService.CurrentMode switch
            {
                FocusMode.ShortBreak => "短休结束，开始专注",
                FocusMode.LongBreak => "长休结束，下一轮专注",
                _ => "专注完成！休息一下"
            };
        }

        // 任务计划
        if (SelectedTaskId.HasValue)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == SelectedTaskId.Value);
            if (task?.PlannedSeconds > 0)
                return $"“{task.Title}”本次专注完成";
        }
        return "本次专注完成";
    }

    /// <summary>
    /// 将任务输入区的时长文本按当前单位转换为秒；空值表示不限时。
    /// </summary>
    private bool TryGetDurationSeconds(out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(NewTaskDuration))
            return true;

        if (!int.TryParse(NewTaskDuration, out var value) || value <= 0)
            return false;

        try
        {
            seconds = checked(DurationUnitIndex switch
            {
                2 => value * 3600,
                1 => value * 60,
                _ => value
            });
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static int ParsePositiveInt(string value) =>
        int.TryParse(value, out var result) && result > 0 ? result : 0;

    private bool IsCycleConfigurationValid()
    {
        var focusMinutes = ParsePositiveInt(CycleFocusMinutesText);
        var breakMinutes = ParsePositiveInt(CycleBreakMinutesText);
        var rounds = ParsePositiveInt(CycleTotalRoundsText);
        return focusMinutes > 0
            && breakMinutes > 0
            && rounds > 0
            && focusMinutes <= int.MaxValue / 60
            && breakMinutes <= int.MaxValue / 60;
    }

    private bool CanAddTask() =>
        !_isExitPreparationInProgress
        && !IsTaskMutationInProgress
        && !string.IsNullOrWhiteSpace(NewTaskTitle)
        && TryGetDurationSeconds(out _);

    private bool CanCompleteTask(object? parameter) =>
        !_isExitPreparationInProgress
        && !IsTaskMutationInProgress
        && !_isTimerOperationInProgress
        && !IsTimerActive
        && parameter is Guid id
        && Tasks.FirstOrDefault(task => task.Id == id) is { IsCompleted: false };

    private bool CanDeleteTask(object? parameter) =>
        !_isExitPreparationInProgress
        && !IsTaskMutationInProgress
        && !_isTimerOperationInProgress
        && !IsTimerActive
        && parameter is Guid id
        && Tasks.Any(task => task.Id == id);

    private bool CanSelectTask(object? parameter) =>
        !_isExitPreparationInProgress
        && !IsTaskMutationInProgress
        && !_isTimerOperationInProgress
        && !IsTimerActive
        && parameter is Guid id
        && Tasks.FirstOrDefault(task => task.Id == id) is { IsCompleted: false };

    private bool CanClearSelectedTask() =>
        !_isExitPreparationInProgress
        && !IsTaskMutationInProgress
        && !_isTimerOperationInProgress
        && !IsTimerActive
        && SelectedTaskId.HasValue;

    private bool CanStartNewTimer() =>
        !_isExitPreparationInProgress
        && !IsTaskMutationInProgress
        && !_isTimerOperationInProgress
        && (_pomodoroService.CurrentState.Status is TimerStatus.Idle or TimerStatus.Stopped);

    private bool CanStartOrResumeTimer() =>
        !_isExitPreparationInProgress
        && !IsTaskMutationInProgress
        && !_isTimerOperationInProgress
        && (_pomodoroService.CurrentState.Status is TimerStatus.Idle or TimerStatus.Stopped or TimerStatus.Paused);

    private bool CanResetTimer() =>
        !_isExitPreparationInProgress
        && !_isTimerOperationInProgress
        && (_pomodoroService.CurrentState.Status == TimerStatus.Stopped || IsTargetReached);

    private bool IsTimerRunning() =>
        !_isExitPreparationInProgress
        && !_isTimerOperationInProgress
        && _pomodoroService.CurrentState.Status == TimerStatus.Running;

    private static string FormatSeconds(int seconds)
    {
        if (seconds % 3600 == 0)
            return $"{seconds / 3600} 小时";
        if (seconds % 60 == 0)
            return $"{seconds / 60} 分钟";
        return $"{seconds} 秒";
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

    private void ClearLastSessionDisplay()
    {
        _lastSessionDuration = null;
        _lastSessionTargetSeconds = 0;
    }

    private void RememberSessionDisplay(PomodoroSession session, int targetSeconds)
    {
        _lastSessionDuration = session.Duration;
        _lastSessionTargetSeconds = targetSeconds;
        ElapsedDisplay = FormatElapsed(session.Duration);
    }

    private void NotifyCommandStatesChanged()
    {
        AddTaskCommand.RaiseCanExecuteChanged();
        CompleteTaskCommand.RaiseCanExecuteChanged();
        DeleteTaskCommand.RaiseCanExecuteChanged();
        DeleteSessionCommand.RaiseCanExecuteChanged();
        StartTimerCommand.RaiseCanExecuteChanged();
        PauseTimerCommand.RaiseCanExecuteChanged();
        StopTimerCommand.RaiseCanExecuteChanged();
        ResetTimerCommand.RaiseCanExecuteChanged();
        StartPresetCommand.RaiseCanExecuteChanged();
        StartCycleCommand.RaiseCanExecuteChanged();
        ToggleTimerCommand.RaiseCanExecuteChanged();
        StopOrResetCommand.RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RunTimerOperationAsync(Func<Task> operation)
    {
        if (!await _timerOperationGate.WaitAsync(0))
            return;

        await ExecuteTimerOperationAsync(operation);
    }

    private async Task RunTaskMutationAsync(Func<Task> operation)
    {
        if (Interlocked.Increment(ref _pendingTaskMutationCount) == 1)
            NotifyCommandStatesChanged();

        await _taskMutationGate.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _taskMutationGate.Release();
            if (Interlocked.Decrement(ref _pendingTaskMutationCount) == 0)
                NotifyCommandStatesChanged();
        }
    }

    private async Task RunSessionMutationAsync(Func<Task> operation)
    {
        if (Interlocked.Increment(ref _pendingSessionMutationCount) == 1)
            NotifyCommandStatesChanged();

        await _sessionMutationGate.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _sessionMutationGate.Release();
            if (Interlocked.Decrement(ref _pendingSessionMutationCount) == 0)
                NotifyCommandStatesChanged();
        }
    }

    private async Task RunTimerOperationAndWaitAsync(Func<Task> operation)
    {
        await _timerOperationGate.WaitAsync();
        await ExecuteTimerOperationAsync(operation);
    }

    private async Task ExecuteTimerOperationAsync(Func<Task> operation)
    {
        _isTimerOperationInProgress = true;
        NotifyCommandStatesChanged();
        try
        {
            await operation();
        }
        finally
        {
            _isTimerOperationInProgress = false;
            PublishTimerState(force: true);
            NotifyCommandStatesChanged();
            _timerOperationGate.Release();
        }
    }

    /// <summary>
    /// 在应用退出前保存仍在运行或暂停的计时，并等待其他持久化操作安全结束。
    /// </summary>
    public Task SaveActiveSessionForExitAsync() => PrepareForExitAsync(saveActiveTimer: true);

    /// <summary>
    /// 阻止新的写入操作，等待已开始的任务、计时和专注记录操作完成；按需保存活动计时。
    /// 保存异常会向调用方传播，界面恢复可操作状态以便用户重试。
    /// </summary>
    public async Task PrepareForExitAsync(bool saveActiveTimer)
    {
        if (_isExitPreparationInProgress)
            return;

        _isExitPreparationInProgress = true;
        OnPropertyChanged(nameof(CanEditTimerSetup));
        NotifyCommandStatesChanged();

        try
        {
            await WaitForMutationDrainAsync(_taskMutationGate, () => IsTaskMutationInProgress);
            await RunTimerOperationAndWaitAsync(async () =>
            {
                if (saveActiveTimer && IsTimerActive)
                    await StopTimerAsync(completePendingTargetTransition: false);
            });
            await WaitForMutationDrainAsync(_sessionMutationGate, () => IsSessionMutationInProgress);
        }
        catch
        {
            _isExitPreparationInProgress = false;
            OnPropertyChanged(nameof(CanEditTimerSetup));
            NotifyCommandStatesChanged();
            throw;
        }
    }

    private static async Task WaitForMutationDrainAsync(SemaphoreSlim gate, Func<bool> hasPendingOperations)
    {
        while (hasPendingOperations())
        {
            await gate.WaitAsync();
            gate.Release();
            await Task.Yield();
        }
    }

    /// <summary>将指定任务标记为完成</summary>
    private async Task CompleteTaskAsync(Guid id)
    {
        TaskItem? completedItem;
        try
        {
            completedItem = await _taskService.CompleteAsync(id);
        }
        catch
        {
            var unchangedItem = Tasks.FirstOrDefault(t => t.Id == id);
            if (unchangedItem is not null)
            {
                var unchangedIndex = Tasks.IndexOf(unchangedItem);
                Tasks[unchangedIndex] = unchangedItem;
            }
            throw;
        }

        var currentItem = Tasks.FirstOrDefault(t => t.Id == id);
        if (completedItem is null || currentItem is null)
            return;

        var index = Tasks.IndexOf(currentItem);
        Tasks[index] = completedItem;
        if (SelectedTaskId == id)
            SelectedTaskId = null;
        else
            OnPropertyChanged(nameof(SelectedTaskTitle));

        RefreshTodayStats();
        NotifyCommandStatesChanged();
    }

    /// <summary>删除指定任务</summary>
    private async Task DeleteTaskAsync(Guid id)
    {
        var item = Tasks.FirstOrDefault(t => t.Id == id);
        if (item is null || !_confirmDelete($"确定删除任务“{item.Title}”吗？"))
            return;

        await _taskService.DeleteAsync(id);
        Tasks.Remove(item);
        if (SelectedTaskId == id)
            SelectedTaskId = null;
        RefreshTodayStats();
    }

    /// <summary>删除指定专注记录</summary>
    private async Task DeleteSessionAsync(Guid id)
    {
        if (!_confirmDelete("确定删除这条专注记录吗？"))
            return;

        await _pomodoroService.DeleteSessionAsync(id);
        var session = Sessions.FirstOrDefault(s => s.Id == id);
        if (session is not null)
            Sessions.Remove(session);
        RefreshTodayStats();
    }

    /// <summary>选择关联任务</summary>
    private void SelectTask(Guid id)
    {
        SelectedTaskId = id;
    }

    /// <summary>
    /// 清除当前计时器关联的任务。
    /// </summary>
    private void ClearSelectedTask()
    {
        SelectedTaskId = null;
    }

    /// <summary>开始计时（手动），暂停状态下恢复计时，停止后重新开始</summary>
    private async Task StartTimerAsync()
    {
        var status = _pomodoroService.CurrentState.Status;
        if (status != TimerStatus.Paused)
            _pendingTargetSaveRetry = null;
        ClearLastSessionDisplay();
        if (status == TimerStatus.Paused)
        {
            await _pomodoroService.ResumeAsync();
        }
        else
        {
            var isBreak = _pomodoroService.CurrentMode != FocusMode.Focus;
            _advanceModeOnTarget = isBreak;
            var targetSeconds = isBreak
                ? _pomodoroService.ModeDefaultSeconds
                : GetSelectedTaskTargetSeconds();
            await _pomodoroService.StartAsync(GetTaskIdForCurrentMode(), targetSeconds);
        }
        _alarmPlayed = false;
        IsTargetReached = false;
        _tick.Stop();
        _tick.Start();
        await UpdateTimerDisplayAsync();
    }

    /// <summary>按预设时长（分钟）开始计时</summary>
    private async Task StartPresetAsync(int minutes)
    {
        _pendingTargetSaveRetry = null;
        ClearLastSessionDisplay();
        if (_pomodoroService.IsCycleActive)
        {
            _pomodoroService.StopCycle();
            NotifyCycleChanged();
        }
        _advanceModeOnTarget = true;
        var seconds = ResolvePresetTargetSeconds(minutes * 60);
        await _pomodoroService.StartAsync(GetTaskIdForCurrentMode(), seconds);
        _alarmPlayed = false;
        IsTargetReached = false;
        _tick.Stop();
        _tick.Start();
        await UpdateTimerDisplayAsync();
    }

    /// <summary>开始循环模式</summary>
    private async Task StartCycleAsync()
    {
        if (!IsCycleConfigurationValid())
            return;

        _pendingTargetSaveRetry = null;
        ClearLastSessionDisplay();
        _pomodoroService.StopCycle();
        var rounds = ParsePositiveInt(CycleTotalRoundsText);
        await _pomodoroService.StartCycleAsync(
            CycleFocusMinutes * 60,
            CycleBreakMinutes * 60,
            rounds,
            SelectedTaskId);
        _advanceModeOnTarget = false;
        NotifyCycleChanged();
        _alarmPlayed = false;
        IsTargetReached = false;
        _tick.Stop();
        _tick.Start();
        await UpdateTimerDisplayAsync();
    }

    /// <summary>暂停计时</summary>
    private async Task PauseTimerAsync()
    {
        await _pomodoroService.PauseAsync();
        _tick.Stop();
        await UpdateTimerDisplayAsync();
    }

    /// <summary>停止计时并生成专注记录</summary>
    private Task StopTimerAsync() => StopTimerAsync(completePendingTargetTransition: true);

    private async Task StopTimerAsync(bool completePendingTargetTransition)
    {
        _tick.Stop();
        IsTargetReached = false;
        var targetSeconds = _pomodoroService.TargetSeconds;
        PomodoroSession? session;
        try
        {
            session = await _pomodoroService.StopAsync();
        }
        catch (Exception exception)
        {
            await PauseAfterSaveFailureAsync();
            throw CreateSaveFailureException(exception);
        }

        var pendingTargetSaveRetry = _pendingTargetSaveRetry;
        if (session is not null && pendingTargetSaveRetry is not null && completePendingTargetTransition)
        {
            _pendingTargetSaveRetry = null;
            if (pendingTargetSaveRetry.IsCycle)
            {
                await CompleteCycleTargetTransitionAsync(
                    session,
                    pendingTargetSaveRetry.NotificationMessage,
                    pendingTargetSaveRetry.TargetSeconds);
            }
            else
            {
                CompleteSingleTargetTransition(
                    session,
                    pendingTargetSaveRetry.NotificationMessage,
                    pendingTargetSaveRetry.TargetSeconds);
            }
            return;
        }

        _pendingTargetSaveRetry = null;
        if (session is not null)
        {
            Sessions.Add(session);
            RememberSessionDisplay(session, targetSeconds);
        }
        if (_pomodoroService.IsCycleActive)
        {
            _pomodoroService.StopCycle();
            NotifyCycleChanged();
        }
        _advanceModeOnTarget = false;
        await UpdateTimerDisplayAsync();
    }

    /// <summary>清除计时器状态，不生成记录</summary>
    private async Task ResetTimerAsync()
    {
        _tick.Stop();
        _alarmPlayed = false;
        IsTargetReached = false;
        _pendingTargetSaveRetry = null;
        ClearLastSessionDisplay();
        if (_pomodoroService.IsCycleActive)
        {
            _pomodoroService.StopCycle();
            NotifyCycleChanged();
        }
        _advanceModeOnTarget = false;
        await _pomodoroService.FullResetAsync();
        NotifyModeChanged();
        await UpdateTimerDisplayAsync();
    }

    /// <summary>Space 快捷键：空闲/暂停→开始，运行→暂停</summary>
    private async Task ToggleTimerAsync()
    {
        switch (_pomodoroService.CurrentState.Status)
        {
            case TimerStatus.Running:
                await PauseTimerAsync();
                break;
            case TimerStatus.Idle:
            case TimerStatus.Paused:
            case TimerStatus.Stopped:
                await StartTimerAsync();
                break;
        }
    }

    /// <summary>Esc 快捷键：运行/暂停→停止，其他→清除</summary>
    private async Task StopOrResetAsync()
    {
        switch (_pomodoroService.CurrentState.Status)
        {
            case TimerStatus.Running:
            case TimerStatus.Paused:
                await StopTimerAsync();
                break;
            default:
                await ResetTimerAsync();
                break;
        }
    }

    /// <summary>
    /// 基于当前内存中的任务和 Session 刷新今日统计文本。
    /// </summary>
    private void RefreshTodayStats()
    {
        var today = DateTime.Today;
        var sessions = Sessions
            .Where(s => s.StartTime.Date == today && s.Mode == FocusMode.Focus)
            .ToList();
        var count = sessions.Count;
        var totalMinutes = (int)sessions.Sum(s => s.Duration.TotalMinutes);
        var tasksDone = Tasks.Count(t => t.CompletedAt?.Date == today);
        TodayStats = $"今日 {count} 次 · {totalMinutes} 分钟 · {tasksDone} 个任务";
    }

    /// <summary>刷新计时器显示：状态文字、实时耗时、超时检测</summary>
    internal async Task UpdateTimerDisplayAsync()
    {
        var state = _pomodoroService.CurrentState;
        PublishTimerState();
        RefreshTodayStats();

        var elapsed = GetCurrentElapsed(state);
        RefreshElapsedDisplay(state, elapsed);

        await HandleTimerTargetAsync(state, elapsed);
    }

    private async Task HandleTimerTickAsync()
    {
        try
        {
            await UpdateTimerDisplayAsync();
        }
        catch (Exception exception)
        {
            HandleCommandException(exception);
        }
    }

    /// <summary>
    /// 将领域层计时状态映射为界面显示文本。
    /// </summary>
    private void PublishTimerState(bool force = false)
    {
        var status = _pomodoroService.CurrentState.Status;
        var targetSeconds = _pomodoroService.TargetSeconds;
        var isCycleActive = _pomodoroService.IsCycleActive;
        if (!force
            && _publishedTimerStatus == status
            && _publishedTargetSeconds == targetSeconds
            && _publishedCycleActive == isCycleActive)
        {
            return;
        }

        _publishedTimerStatus = status;
        _publishedTargetSeconds = targetSeconds;
        _publishedCycleActive = isCycleActive;

        StatusText = IsTargetReached && status == TimerStatus.Stopped
            ? "已完成"
            : status switch
            {
                TimerStatus.Idle => "就绪",
                TimerStatus.Running => "运行中",
                TimerStatus.Paused => "已暂停",
                TimerStatus.Stopped => "已停止",
                _ => string.Empty
            };

        OnPropertyChanged(nameof(IsTimerActive));
        OnPropertyChanged(nameof(CanEditTimerSetup));
        OnPropertyChanged(nameof(TimerDisplayLabel));
        OnPropertyChanged(nameof(TimerTargetSummary));
        NotifyCommandStatesChanged();
    }

    /// <summary>
    /// 计算实时已用时长，运行中时补上当前计时片段。
    /// </summary>
    private static TimeSpan GetCurrentElapsed(TimerState state)
    {
        var elapsed = state.ElapsedTime;
        if (state.Status == TimerStatus.Running && state.StartTime.HasValue)
            elapsed += DateTime.Now - state.StartTime.Value;

        return elapsed;
    }

    /// <summary>
    /// 根据是否存在目标时长，刷新正计时或倒计时显示。
    /// </summary>
    private void RefreshElapsedDisplay(TimerState state, TimeSpan elapsed)
    {
        if (state.Status == TimerStatus.Stopped && _lastSessionDuration.HasValue)
        {
            ElapsedDisplay = FormatElapsed(_lastSessionDuration.Value);
            return;
        }

        if (_pomodoroService.TargetSeconds > 0)
        {
            var remainingSeconds = Math.Ceiling(
                Math.Max(0, _pomodoroService.TargetSeconds - elapsed.TotalSeconds));
            var remaining = TimeSpan.FromSeconds(remainingSeconds);
            ElapsedDisplay = FormatElapsed(remaining);
        }
        else
        {
            ElapsedDisplay = FormatElapsed(elapsed);
        }
    }

    /// <summary>
    /// 检查目标时长是否到达，并分派普通预设或循环模式的后续处理。
    /// </summary>
    private async Task HandleTimerTargetAsync(TimerState state, TimeSpan elapsed)
    {
        if (state.Status != TimerStatus.Running || _alarmPlayed || _isTimerOperationInProgress)
            return;

        var targetSeconds = _pomodoroService.TargetSeconds;
        if (targetSeconds <= 0 || elapsed.TotalSeconds < targetSeconds)
            return;

        var notificationMessage = $"时间到！{TargetDescription()}";
        await RunTimerOperationAsync(async () =>
        {
            if (_pomodoroService.IsCycleActive)
                await HandleCycleTargetReachedAsync(notificationMessage, targetSeconds);
            else
                await HandleSingleTargetReachedAsync(notificationMessage, targetSeconds);
        });
    }

    /// <summary>
    /// 统一处理到时后的超时状态、界面通知和提示音。
    /// </summary>
    private void NotifyTimerReachedTarget(string message)
    {
        _alarmPlayed = true;
        ShowNotification(message);
        try
        {
            _onTimerReached();
        }
        catch
        {
            // 提示音失败不应阻断到时后的 Session 保存和状态转换。
        }
    }

    /// <summary>
    /// 普通预设或任务目标到时后，先按当前模式保存 Session，再按需切换模式。
    /// </summary>
    private async Task HandleSingleTargetReachedAsync(string notificationMessage, int targetSeconds)
    {
        _tick.Stop();
        PomodoroSession? session;
        try
        {
            session = await _pomodoroService.StopAsync();
        }
        catch (Exception exception)
        {
            _pendingTargetSaveRetry = new TargetSaveRetry(false, notificationMessage, targetSeconds);
            await PauseAfterSaveFailureAsync();
            throw CreateSaveFailureException(exception);
        }
        _pendingTargetSaveRetry = null;
        CompleteSingleTargetTransition(session, notificationMessage, targetSeconds);
    }

    private void CompleteSingleTargetTransition(
        PomodoroSession? session,
        string notificationMessage,
        int targetSeconds)
    {
        if (session is not null)
        {
            Sessions.Add(session);
            RememberSessionDisplay(session, targetSeconds);
        }

        IsTargetReached = true;

        if (_advanceModeOnTarget)
        {
            _pomodoroService.AdvanceMode();
            NotifyModeChanged();
        }

        _advanceModeOnTarget = false;
        PublishTimerState(force: true);
        RefreshTodayStats();
        NotifyTimerReachedTarget(notificationMessage);
    }

    /// <summary>
    /// 循环模式到时后记录当前阶段，并在还有下一阶段时自动启动。
    /// </summary>
    private async Task HandleCycleTargetReachedAsync(string notificationMessage, int targetSeconds)
    {
        _tick.Stop();
        PomodoroSession? session;
        try
        {
            session = await _pomodoroService.StopAsync();
        }
        catch (Exception exception)
        {
            _pendingTargetSaveRetry = new TargetSaveRetry(true, notificationMessage, targetSeconds);
            await PauseAfterSaveFailureAsync();
            throw CreateSaveFailureException(exception);
        }
        _pendingTargetSaveRetry = null;
        await CompleteCycleTargetTransitionAsync(session, notificationMessage, targetSeconds);
    }

    private async Task CompleteCycleTargetTransitionAsync(
        PomodoroSession? session,
        string notificationMessage,
        int targetSeconds)
    {
        if (session is not null)
            Sessions.Add(session);

        var nextSeconds = _pomodoroService.AdvanceCycle();

        if (nextSeconds > 0)
        {
            ClearLastSessionDisplay();
            IsTargetReached = false;
            await _pomodoroService.StartAsync(GetTaskIdForCurrentMode(), nextSeconds);
            _alarmPlayed = false;
            _tick.Start();
            NotifyCycleChanged();
            RefreshTodayStats();
            NotifyTimerReachedTarget(notificationMessage);
            _alarmPlayed = false;
            return;
        }

        if (session is not null)
            RememberSessionDisplay(session, targetSeconds);
        IsTargetReached = true;
        NotifyCycleChanged();
        RefreshTodayStats();
        NotifyTimerReachedTarget(notificationMessage);
        await UpdateTimerDisplayAsync();
    }

    /// <summary>
    /// 通知与循环模式相关的绑定属性发生变化。
    /// </summary>
    private void NotifyCycleChanged()
    {
        NotifyModeChanged();
        OnPropertyChanged(nameof(IsCycleActive));
        OnPropertyChanged(nameof(CycleProgress));
        PublishTimerState(force: true);
    }

    /// <summary>
    /// 通知与专注/休息模式相关的绑定属性发生变化。
    /// </summary>
    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(ModeName));
        OnPropertyChanged(nameof(ModeDefaultMinutes));
        OnPropertyChanged(nameof(CompletedFocusCount));
        OnPropertyChanged(nameof(PresetMinutes));
    }

    private async Task PauseAfterSaveFailureAsync()
    {
        _tick.Stop();
        _alarmPlayed = false;
        IsTargetReached = false;
        if (_pomodoroService.CurrentState.Status == TimerStatus.Running)
            await _pomodoroService.PauseAsync();
    }

    private static InvalidOperationException CreateSaveFailureException(Exception exception) =>
        new("专注记录保存失败，计时已暂停；请再次点击“停止”重试。", exception);

    private void HandleCommandException(Exception exception)
    {
        ShowNotification($"操作失败：{exception.Message}", isError: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
