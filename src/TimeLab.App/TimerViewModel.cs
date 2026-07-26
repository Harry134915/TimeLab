using System.Media;
using System.Windows.Input;
using System.Windows.Threading;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.App;

/// <summary>
/// 管理计时状态、循环模式、计时命令和专注记录保存恢复。
/// </summary>
public sealed class TimerViewModel : ViewModelBase
{
    private readonly PomodoroService _pomodoroService;
    private readonly TaskListViewModel _taskList;
    private readonly SessionLogViewModel _sessionLog;
    private readonly WorkspaceInteractionState _interactionState;
    private readonly Action _onTimerReached;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private bool _advanceModeOnTarget;
    private TimerStatus? _publishedTimerStatus;
    private int _publishedTargetSeconds = -1;
    private bool? _publishedCycleActive;
    private TimeSpan? _lastSessionDuration;
    private int _lastSessionTargetSeconds;
    private TargetSaveRetry? _pendingTargetSaveRetry;
    private string _cycleFocusMinutesText = "25";
    private string _cycleBreakMinutesText = "5";
    private string _cycleTotalRoundsText = "4";
    private bool _isTargetReached;
    private bool _alarmPlayed;
    private string _elapsedDisplay = "00:00:00";
    private string _statusText = "就绪";

    private sealed record TargetSaveRetry(
        bool IsCycle,
        string NotificationMessage,
        int TargetSeconds);

    internal TimerViewModel(
        PomodoroService pomodoroService,
        TaskListViewModel taskList,
        SessionLogViewModel sessionLog,
        WorkspaceInteractionState interactionState,
        Action? onTimerReached = null)
    {
        _pomodoroService = pomodoroService;
        _taskList = taskList;
        _sessionLog = sessionLog;
        _interactionState = interactionState;
        _onTimerReached = onTimerReached ?? SystemSounds.Beep.Play;

        _tick.Tick += async (_, _) => await HandleTimerTickAsync();
        _interactionState.Changed += NotifyCommandStatesChanged;

        StartTimerCommand = new AsyncRelayCommand(
            _ => RunTimerOperationAsync(StartTimerAsync),
            HandleCommandException,
            _ => CanStartOrResumeTimer());
        PauseTimerCommand = new AsyncRelayCommand(
            _ => RunTimerOperationAsync(PauseTimerAsync),
            HandleCommandException,
            _ => IsTimerRunning());
        StopTimerCommand = new AsyncRelayCommand(
            _ => RunTimerOperationAsync(StopTimerAsync),
            HandleCommandException,
            _ => !_interactionState.IsExitPreparationInProgress
                 && !_interactionState.IsTimerOperationInProgress
                 && IsTimerActive);
        ResetTimerCommand = new AsyncRelayCommand(
            _ => RunTimerOperationAsync(ResetTimerAsync),
            HandleCommandException,
            _ => CanResetTimer());
        ToggleTimerCommand = new AsyncRelayCommand(
            _ => RunTimerOperationAsync(ToggleTimerAsync),
            HandleCommandException,
            _ => !_interactionState.IsExitPreparationInProgress
                 && !_interactionState.IsTimerOperationInProgress
                 && (IsTimerActive || !_interactionState.IsTaskMutationInProgress));
        StopOrResetCommand = new AsyncRelayCommand(
            _ => RunTimerOperationAsync(StopOrResetAsync),
            HandleCommandException,
            _ => !_interactionState.IsExitPreparationInProgress
                 && !_interactionState.IsTimerOperationInProgress);
        StartPresetCommand = new AsyncRelayCommand(
            p => RunTimerOperationAsync(() => StartPresetAsync((int)p!)),
            HandleCommandException,
            p => CanStartNewTimer() && p is int minutes && minutes > 0);
        StartCycleCommand = new AsyncRelayCommand(
            _ => RunTimerOperationAsync(StartCycleAsync),
            HandleCommandException,
            _ => CanStartNewTimer() && IsCycleConfigurationValid());

        PublishTimerState(force: true);
    }

    internal event Action<string, bool>? NotificationRequested;

    public int[] PresetMinutes => _pomodoroService.CurrentMode switch
    {
        FocusMode.ShortBreak => [3, 5, 10],
        FocusMode.LongBreak => [10, 15, 30],
        _ => [15, 25, 30, 45, 60]
    };

    public string ModeName => _pomodoroService.CurrentMode switch
    {
        FocusMode.ShortBreak => "短休",
        FocusMode.LongBreak => "长休",
        _ => "专注"
    };

    public int ModeDefaultMinutes => _pomodoroService.ModeDefaultSeconds / 60;

    public int CompletedFocusCount => _pomodoroService.CompletedFocusCount;

    public int CycleFocusMinutes
    {
        get => ParsePositiveInt(CycleFocusMinutesText);
        set => CycleFocusMinutesText = value.ToString();
    }

    public string CycleFocusMinutesText
    {
        get => _cycleFocusMinutesText;
        set
        {
            if (!SetProperty(ref _cycleFocusMinutesText, value))
                return;
            OnPropertyChanged(nameof(CycleFocusMinutes));
            OnPropertyChanged(nameof(CycleValidationMessage));
            NotifyCommandStatesChanged();
        }
    }

    public int CycleBreakMinutes
    {
        get => ParsePositiveInt(CycleBreakMinutesText);
        set => CycleBreakMinutesText = value.ToString();
    }

    public string CycleBreakMinutesText
    {
        get => _cycleBreakMinutesText;
        set
        {
            if (!SetProperty(ref _cycleBreakMinutesText, value))
                return;
            OnPropertyChanged(nameof(CycleBreakMinutes));
            OnPropertyChanged(nameof(CycleValidationMessage));
            NotifyCommandStatesChanged();
        }
    }

    public string CycleTotalRoundsText
    {
        get => _cycleTotalRoundsText;
        set
        {
            if (!SetProperty(ref _cycleTotalRoundsText, value))
                return;
            OnPropertyChanged(nameof(CycleValidationMessage));
            NotifyCommandStatesChanged();
        }
    }

    public string CycleValidationMessage => IsCycleConfigurationValid()
        ? string.Empty
        : "专注、休息和轮数都必须是大于 0 的整数";

    public bool IsCycleActive => _pomodoroService.IsCycleActive;

    public string CycleProgress => IsCycleActive
        ? $"第 {_pomodoroService.CurrentRound}/{_pomodoroService.CycleTotalRounds} 轮 · {ModeName}"
        : string.Empty;

    public bool IsTargetReached
    {
        get => _isTargetReached;
        private set => SetProperty(ref _isTargetReached, value);
    }

    public string ElapsedDisplay
    {
        get => _elapsedDisplay;
        private set => SetProperty(ref _elapsedDisplay, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (!SetProperty(ref _statusText, value))
                return;
            OnPropertyChanged(nameof(StartButtonText));
        }
    }

    public string StartButtonText => _pomodoroService.CurrentState.Status switch
    {
        TimerStatus.Paused => "继续",
        TimerStatus.Running => "进行中",
        _ => "开始"
    };

    public bool IsTimerActive =>
        _pomodoroService.CurrentState.Status is TimerStatus.Running or TimerStatus.Paused;

    public bool CanEditTimerSetup =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && !IsTimerActive;

    public string TimerDisplayLabel =>
        _lastSessionDuration.HasValue
        && _pomodoroService.CurrentState.Status == TimerStatus.Stopped
            ? IsTargetReached ? "已完成" : "本次专注"
            : _pomodoroService.TargetSeconds > 0 ? "剩余时间" : "已用时间";

    public string TimerTargetSummary
    {
        get
        {
            if (_lastSessionDuration.HasValue
                && _pomodoroService.CurrentState.Status == TimerStatus.Stopped)
            {
                return _lastSessionTargetSeconds > 0
                    ? $"目标 {FormatSeconds(_lastSessionTargetSeconds)}"
                    : "已保存到专注记录";
            }

            return _pomodoroService.TargetSeconds > 0
                ? $"目标 {FormatSeconds(_pomodoroService.TargetSeconds)}"
                : $"{ModeName} · 不限时";
        }
    }

    public AsyncRelayCommand StartTimerCommand { get; }
    public AsyncRelayCommand PauseTimerCommand { get; }
    public AsyncRelayCommand StopTimerCommand { get; }
    public AsyncRelayCommand ResetTimerCommand { get; }
    public AsyncRelayCommand ToggleTimerCommand { get; }
    public AsyncRelayCommand StopOrResetCommand { get; }
    public AsyncRelayCommand StartPresetCommand { get; }
    public AsyncRelayCommand StartCycleCommand { get; }

    internal void InitializeState() => PublishTimerState(force: true);

    internal Task SaveActiveSessionForExitAsync() =>
        PrepareForExitAsync(saveActiveTimer: true);

    internal Task PrepareForExitAsync(bool saveActiveTimer)
    {
        return RunTimerOperationAndWaitAsync(async () =>
        {
            if (saveActiveTimer && IsTimerActive)
                await StopTimerAsync(completePendingTargetTransition: false);
        });
    }

    internal async Task UpdateTimerDisplayAsync()
    {
        var state = _pomodoroService.CurrentState;
        PublishTimerState();

        var elapsed = GetCurrentElapsed(state);
        RefreshElapsedDisplay(state, elapsed);

        await HandleTimerTargetAsync(state, elapsed);
    }

    private int ResolvePresetTargetSeconds(int presetSeconds)
    {
        if (_pomodoroService.CurrentMode != FocusMode.Focus)
            return presetSeconds;

        var taskSeconds = _taskList.GetSelectedTaskTargetSeconds();
        return taskSeconds > 0 ? Math.Min(presetSeconds, taskSeconds) : presetSeconds;
    }

    private Guid? GetTaskIdForCurrentMode() =>
        _pomodoroService.CurrentMode == FocusMode.Focus
            ? _taskList.SelectedTaskId
            : null;

    private string TargetDescription()
    {
        if (_pomodoroService.IsCycleActive)
        {
            var round = _pomodoroService.CurrentRound;
            var total = _pomodoroService.CycleTotalRounds;

            if (_pomodoroService.CurrentMode == FocusMode.Focus)
            {
                return round >= total
                    ? $"第 {round}/{total} 轮专注完成！休息后结束"
                    : $"第 {round}/{total} 轮专注完成！休息一下";
            }

            return round >= total
                ? $"全部完成！共 {total} 轮"
                : _pomodoroService.CurrentMode == FocusMode.ShortBreak
                    ? $"第 {round}/{total} 轮短休结束，开始下一轮专注"
                    : $"第 {round}/{total} 轮长休结束，下一轮专注";
        }

        if (_advanceModeOnTarget)
        {
            return _pomodoroService.CurrentMode switch
            {
                FocusMode.ShortBreak => "短休结束，开始专注",
                FocusMode.LongBreak => "长休结束，下一轮专注",
                _ => "专注完成！休息一下"
            };
        }

        var task = _taskList.GetSelectedTask();
        if (task?.PlannedSeconds > 0)
            return $"“{task.Title}”本次专注完成";

        return "本次专注完成";
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

    private bool CanStartNewTimer() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTaskMutationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && (_pomodoroService.CurrentState.Status is TimerStatus.Idle or TimerStatus.Stopped);

    private bool CanStartOrResumeTimer() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTaskMutationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && (_pomodoroService.CurrentState.Status
            is TimerStatus.Idle or TimerStatus.Stopped or TimerStatus.Paused);

    private bool CanResetTimer() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && (_pomodoroService.CurrentState.Status == TimerStatus.Stopped || IsTargetReached);

    private bool IsTimerRunning() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTimerOperationInProgress
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

    private async Task RunTimerOperationAsync(Func<Task> operation)
    {
        if (!await _operationGate.WaitAsync(0))
            return;

        await ExecuteTimerOperationAsync(operation);
    }

    private async Task RunTimerOperationAndWaitAsync(Func<Task> operation)
    {
        await _operationGate.WaitAsync();
        await ExecuteTimerOperationAsync(operation);
    }

    private async Task ExecuteTimerOperationAsync(Func<Task> operation)
    {
        _interactionState.SetTimerOperationInProgress(true);
        try
        {
            await operation();
        }
        finally
        {
            _interactionState.SetTimerOperationInProgress(false);
            PublishTimerState(force: true);
            NotifyCommandStatesChanged();
            _operationGate.Release();
        }
    }

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
                : _taskList.GetSelectedTaskTargetSeconds();
            await _pomodoroService.StartAsync(GetTaskIdForCurrentMode(), targetSeconds);
        }

        _alarmPlayed = false;
        IsTargetReached = false;
        _tick.Stop();
        _tick.Start();
        await UpdateTimerDisplayAsync();
    }

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
            _taskList.SelectedTaskId);
        _advanceModeOnTarget = false;
        NotifyCycleChanged();
        _alarmPlayed = false;
        IsTargetReached = false;
        _tick.Stop();
        _tick.Start();
        await UpdateTimerDisplayAsync();
    }

    private async Task PauseTimerAsync()
    {
        await _pomodoroService.PauseAsync();
        _tick.Stop();
        await UpdateTimerDisplayAsync();
    }

    private Task StopTimerAsync() =>
        StopTimerAsync(completePendingTargetTransition: true);

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
        if (session is not null
            && pendingTargetSaveRetry is not null
            && completePendingTargetTransition)
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
            _sessionLog.AddSession(session);
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

        _interactionState.SetTimerActive(IsTimerActive);
        OnPropertyChanged(nameof(IsTimerActive));
        OnPropertyChanged(nameof(CanEditTimerSetup));
        OnPropertyChanged(nameof(TimerDisplayLabel));
        OnPropertyChanged(nameof(TimerTargetSummary));
        NotifyCommandStatesChanged();
    }

    private static TimeSpan GetCurrentElapsed(TimerState state)
    {
        var elapsed = state.ElapsedTime;
        if (state.Status == TimerStatus.Running && state.StartTime.HasValue)
            elapsed += DateTime.Now - state.StartTime.Value;

        return elapsed;
    }

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
            ElapsedDisplay = FormatElapsed(TimeSpan.FromSeconds(remainingSeconds));
        }
        else
        {
            ElapsedDisplay = FormatElapsed(elapsed);
        }
    }

    private async Task HandleTimerTargetAsync(TimerState state, TimeSpan elapsed)
    {
        if (state.Status != TimerStatus.Running
            || _alarmPlayed
            || _interactionState.IsTimerOperationInProgress)
        {
            return;
        }

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

    private void NotifyTimerReachedTarget(string message)
    {
        _alarmPlayed = true;
        NotificationRequested?.Invoke(message, false);
        try
        {
            _onTimerReached();
        }
        catch
        {
            // 提示音失败不应阻断 Session 保存和状态转换。
        }
    }

    private async Task HandleSingleTargetReachedAsync(
        string notificationMessage,
        int targetSeconds)
    {
        _tick.Stop();
        PomodoroSession? session;
        try
        {
            session = await _pomodoroService.StopAsync();
        }
        catch (Exception exception)
        {
            _pendingTargetSaveRetry = new TargetSaveRetry(
                false,
                notificationMessage,
                targetSeconds);
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
            _sessionLog.AddSession(session);
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
        NotifyTimerReachedTarget(notificationMessage);
    }

    private async Task HandleCycleTargetReachedAsync(
        string notificationMessage,
        int targetSeconds)
    {
        _tick.Stop();
        PomodoroSession? session;
        try
        {
            session = await _pomodoroService.StopAsync();
        }
        catch (Exception exception)
        {
            _pendingTargetSaveRetry = new TargetSaveRetry(
                true,
                notificationMessage,
                targetSeconds);
            await PauseAfterSaveFailureAsync();
            throw CreateSaveFailureException(exception);
        }

        _pendingTargetSaveRetry = null;
        await CompleteCycleTargetTransitionAsync(
            session,
            notificationMessage,
            targetSeconds);
    }

    private async Task CompleteCycleTargetTransitionAsync(
        PomodoroSession? session,
        string notificationMessage,
        int targetSeconds)
    {
        if (session is not null)
            _sessionLog.AddSession(session);

        var nextSeconds = _pomodoroService.AdvanceCycle();
        if (nextSeconds > 0)
        {
            ClearLastSessionDisplay();
            IsTargetReached = false;
            await _pomodoroService.StartAsync(GetTaskIdForCurrentMode(), nextSeconds);
            _alarmPlayed = false;
            _tick.Start();
            NotifyCycleChanged();
            NotifyTimerReachedTarget(notificationMessage);
            _alarmPlayed = false;
            return;
        }

        if (session is not null)
            RememberSessionDisplay(session, targetSeconds);
        IsTargetReached = true;
        NotifyCycleChanged();
        NotifyTimerReachedTarget(notificationMessage);
        await UpdateTimerDisplayAsync();
    }

    private void NotifyCycleChanged()
    {
        NotifyModeChanged();
        OnPropertyChanged(nameof(IsCycleActive));
        OnPropertyChanged(nameof(CycleProgress));
        PublishTimerState(force: true);
    }

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

    private void NotifyCommandStatesChanged()
    {
        StartTimerCommand.RaiseCanExecuteChanged();
        PauseTimerCommand.RaiseCanExecuteChanged();
        StopTimerCommand.RaiseCanExecuteChanged();
        ResetTimerCommand.RaiseCanExecuteChanged();
        StartPresetCommand.RaiseCanExecuteChanged();
        StartCycleCommand.RaiseCanExecuteChanged();
        ToggleTimerCommand.RaiseCanExecuteChanged();
        StopOrResetCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditTimerSetup));
        CommandManager.InvalidateRequerySuggested();
    }

    private void HandleCommandException(Exception exception) =>
        NotificationRequested?.Invoke($"操作失败：{exception.Message}", true);
}
