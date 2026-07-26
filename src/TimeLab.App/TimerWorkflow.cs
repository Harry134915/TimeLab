using System.Media;
using System.Windows.Threading;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.App;

/// <summary>
/// 执行计时操作、目标转换和退出处理，不承担 WPF 命令与绑定声明。
/// </summary>
internal sealed class TimerWorkflow
{
    private readonly PomodoroService _pomodoroService;
    private readonly TaskListViewModel _taskList;
    private readonly SessionLogViewModel _sessionLog;
    private readonly WorkspaceInteractionState _interactionState;
    private readonly Action _onTimerReached;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly TimerPresentationState _presentation = new();
    private readonly TimerTargetCoordinator _targetCoordinator;

    private bool _advanceModeOnTarget;
    private TimerStatus? _publishedTimerStatus;
    private int _publishedTargetSeconds = -1;
    private bool? _publishedCycleActive;
    private bool _alarmPlayed;

    internal TimerWorkflow(
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
        _targetCoordinator = new TimerTargetCoordinator(pomodoroService, taskList);
        _tick.Tick += async (_, _) => await HandleTimerTickAsync();
        PublishState(force: true);
    }

    internal event Action? StateChanged;
    internal event Action<string, bool>? NotificationRequested;

    internal int[] PresetMinutes => _pomodoroService.CurrentMode switch
    {
        FocusMode.ShortBreak => [3, 5, 10],
        FocusMode.LongBreak => [10, 15, 30],
        _ => [15, 25, 30, 45, 60]
    };

    internal string ModeName => _pomodoroService.CurrentMode switch
    {
        FocusMode.ShortBreak => "短休",
        FocusMode.LongBreak => "长休",
        _ => "专注"
    };

    internal int ModeDefaultMinutes => _pomodoroService.ModeDefaultSeconds / 60;
    internal int CompletedFocusCount => _pomodoroService.CompletedFocusCount;
    internal bool IsCycleActive => _pomodoroService.IsCycleActive;

    internal string CycleProgress => IsCycleActive
        ? $"第 {_pomodoroService.CurrentRound}/{_pomodoroService.CycleTotalRounds} 轮 · {ModeName}"
        : string.Empty;

    internal bool IsTargetReached => _presentation.IsTargetReached;
    internal string ElapsedDisplay => _presentation.ElapsedDisplay;
    internal string StatusText => _presentation.StatusText;

    internal string StartButtonText => _pomodoroService.CurrentState.Status switch
    {
        TimerStatus.Paused => "继续",
        TimerStatus.Running => "进行中",
        _ => "开始"
    };

    internal bool IsTimerActive =>
        _pomodoroService.CurrentState.Status is TimerStatus.Running or TimerStatus.Paused;

    internal bool CanEditTimerSetup =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && !IsTimerActive;

    internal string TimerDisplayLabel => _presentation.GetDisplayLabel(_pomodoroService);

    internal string TimerTargetSummary =>
        _presentation.GetTargetSummary(_pomodoroService, ModeName);

    internal bool CanStartNewTimer() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTaskMutationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && (_pomodoroService.CurrentState.Status is TimerStatus.Idle or TimerStatus.Stopped);

    internal bool CanStartOrResumeTimer() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTaskMutationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && (_pomodoroService.CurrentState.Status
            is TimerStatus.Idle or TimerStatus.Stopped or TimerStatus.Paused);

    internal bool CanResetTimer() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && (_pomodoroService.CurrentState.Status == TimerStatus.Stopped || IsTargetReached);

    internal bool IsTimerRunning() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && _pomodoroService.CurrentState.Status == TimerStatus.Running;

    internal void InitializeState() => PublishState(force: true);

    internal Task RunOperationAsync(Func<Task> operation) =>
        RunOperationCoreAsync(operation, waitForGate: false);

    internal Task PrepareForExitAsync(bool saveActiveTimer) =>
        RunOperationCoreAsync(
            saveActiveTimer ? SaveForExitAsync : AbandonForExitAsync,
            waitForGate: true);

    internal async Task UpdateDisplayAsync()
    {
        var state = _pomodoroService.CurrentState;
        PublishState();
        var elapsed = GetCurrentElapsed(state);
        _presentation.RefreshElapsed(state, elapsed, _pomodoroService.TargetSeconds);
        StateChanged?.Invoke();
        await HandleTargetAsync(state, elapsed);
    }

    internal async Task StartTimerAsync()
    {
        var status = _pomodoroService.CurrentState.Status;
        if (status != TimerStatus.Paused)
            _targetCoordinator.ClearPendingRetry();
        _presentation.ClearLastSession();

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

        StartTicking();
        await UpdateDisplayAsync();
    }

    internal async Task StartPresetAsync(int minutes)
    {
        _targetCoordinator.ClearPendingRetry();
        _presentation.ClearLastSession();
        StopCycleIfActive();

        _advanceModeOnTarget = true;
        var seconds = ResolvePresetTargetSeconds(checked(minutes * 60));
        await _pomodoroService.StartAsync(GetTaskIdForCurrentMode(), seconds);
        StartTicking();
        await UpdateDisplayAsync();
    }

    internal async Task StartCycleAsync(int focusMinutes, int breakMinutes, int rounds)
    {
        _targetCoordinator.ClearPendingRetry();
        _presentation.ClearLastSession();
        _pomodoroService.StopCycle();
        await _pomodoroService.StartCycleAsync(
            checked(focusMinutes * 60),
            checked(breakMinutes * 60),
            rounds,
            _taskList.SelectedTaskId);
        _advanceModeOnTarget = false;
        StartTicking();
        PublishState(force: true);
        await UpdateDisplayAsync();
    }

    internal async Task PauseTimerAsync()
    {
        await _pomodoroService.PauseAsync();
        _tick.Stop();
        await UpdateDisplayAsync();
    }

    internal Task StopTimerAsync() =>
        StopTimerAsync(completePendingTargetTransition: true);

    internal async Task ResetTimerAsync()
    {
        _tick.Stop();
        _alarmPlayed = false;
        _presentation.IsTargetReached = false;
        _targetCoordinator.ClearPendingRetry();
        _presentation.ClearLastSession();
        StopCycleIfActive();
        _advanceModeOnTarget = false;
        await _pomodoroService.FullResetAsync();
        PublishState(force: true);
        await UpdateDisplayAsync();
    }

    internal async Task ToggleTimerAsync()
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

    internal async Task StopOrResetAsync()
    {
        if (IsTimerActive)
            await StopTimerAsync();
        else
            await ResetTimerAsync();
    }

    private async Task RunOperationCoreAsync(
        Func<Task> operation,
        bool waitForGate)
    {
        var entered = waitForGate
            ? await WaitForOperationGateAsync()
            : await _operationGate.WaitAsync(0);
        if (!entered)
            return;

        _interactionState.SetTimerOperationInProgress(true);
        try
        {
            await operation();
        }
        finally
        {
            _interactionState.SetTimerOperationInProgress(false);
            PublishState(force: true);
            _operationGate.Release();
        }
    }

    private async Task<bool> WaitForOperationGateAsync()
    {
        await _operationGate.WaitAsync();
        return true;
    }

    private async Task SaveForExitAsync()
    {
        if (IsTimerActive)
            await StopTimerAsync(completePendingTargetTransition: false);
    }

    private async Task AbandonForExitAsync()
    {
        _tick.Stop();
        _alarmPlayed = false;
        _presentation.IsTargetReached = false;
        _targetCoordinator.ClearPendingRetry();
        _presentation.ClearLastSession();
        StopCycleIfActive();
        _advanceModeOnTarget = false;
        await _pomodoroService.FullResetAsync();
        PublishState(force: true);
    }

    private async Task StopTimerAsync(bool completePendingTargetTransition)
    {
        _tick.Stop();
        _presentation.IsTargetReached = false;
        var targetSeconds = _pomodoroService.TargetSeconds;
        PomodoroSession? session;
        try
        {
            session = await _pomodoroService.StopAsync();
        }
        catch (Exception exception)
        {
            _alarmPlayed = false;
            await _targetCoordinator.PauseAfterSaveFailureAsync();
            throw TimerTargetCoordinator.CreateSaveFailureException(exception);
        }

        if (completePendingTargetTransition)
        {
            var transition = await _targetCoordinator.CompletePendingRetryAsync(session);
            if (transition is not null)
            {
                await ApplyTargetTransitionAsync(transition);
                return;
            }
        }
        else
        {
            _targetCoordinator.ClearPendingRetry();
        }

        if (session is not null)
        {
            _sessionLog.AddSession(session);
            _presentation.RememberSession(session, targetSeconds);
        }

        StopCycleIfActive();
        _advanceModeOnTarget = false;
        await UpdateDisplayAsync();
    }

    private async Task HandleTimerTickAsync()
    {
        try
        {
            await UpdateDisplayAsync();
        }
        catch (Exception exception)
        {
            NotificationRequested?.Invoke($"操作失败：{exception.Message}", true);
        }
    }

    private async Task HandleTargetAsync(TimerState state, TimeSpan elapsed)
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
        await RunOperationAsync(async () =>
        {
            _tick.Stop();
            TimerTargetTransition transition;
            try
            {
                transition = await _targetCoordinator.CompleteTargetAsync(
                    _pomodoroService.IsCycleActive,
                    _advanceModeOnTarget,
                    notificationMessage,
                    targetSeconds);
            }
            catch
            {
                _alarmPlayed = false;
                _presentation.IsTargetReached = false;
                throw;
            }

            await ApplyTargetTransitionAsync(transition);
        });
    }

    private async Task ApplyTargetTransitionAsync(TimerTargetTransition transition)
    {
        if (transition.Session is not null)
            _sessionLog.AddSession(transition.Session);

        if (transition.IsCycle && transition.NextStageStarted)
        {
            _presentation.ClearLastSession();
            _presentation.IsTargetReached = false;
            _alarmPlayed = false;
            _tick.Start();
            PublishState(force: true);
            NotifyTimerReached(transition.NotificationMessage);
            _alarmPlayed = false;
            return;
        }

        if (transition.Session is not null)
            _presentation.RememberSession(transition.Session, transition.TargetSeconds);

        _presentation.IsTargetReached = true;
        _advanceModeOnTarget = false;
        PublishState(force: true);
        NotifyTimerReached(transition.NotificationMessage);

        if (transition.IsCycle)
            await UpdateDisplayAsync();
    }

    private void StartTicking()
    {
        _alarmPlayed = false;
        _presentation.IsTargetReached = false;
        _tick.Stop();
        _tick.Start();
    }

    private void StopCycleIfActive()
    {
        if (_pomodoroService.IsCycleActive)
            _pomodoroService.StopCycle();
    }

    private void NotifyTimerReached(string message)
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

    private void PublishState(bool force = false)
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
        _presentation.RefreshStatus(status);
        _interactionState.SetTimerActive(IsTimerActive);
        StateChanged?.Invoke();
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
        return task?.PlannedSeconds > 0
            ? $"“{task.Title}”本次专注完成"
            : "本次专注完成";
    }

    private static TimeSpan GetCurrentElapsed(TimerState state)
    {
        var elapsed = state.ElapsedTime;
        if (state.Status == TimerStatus.Running && state.StartTime.HasValue)
            elapsed += DateTime.Now - state.StartTime.Value;
        return elapsed;
    }
}
