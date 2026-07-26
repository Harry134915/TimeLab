using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.App;

/// <summary>
/// 协调目标到达后的 Session 保存、模式推进和保存失败重试。
/// </summary>
internal sealed class TimerTargetCoordinator
{
    private readonly PomodoroService _pomodoroService;
    private readonly TaskListViewModel _taskList;
    private PendingTargetSave? _pendingSave;

    internal TimerTargetCoordinator(
        PomodoroService pomodoroService,
        TaskListViewModel taskList)
    {
        _pomodoroService = pomodoroService;
        _taskList = taskList;
    }

    internal void ClearPendingRetry() => _pendingSave = null;

    internal async Task<TimerTargetTransition> CompleteTargetAsync(
        bool isCycle,
        bool advanceMode,
        string notificationMessage,
        int targetSeconds)
    {
        PomodoroSession? session;
        try
        {
            session = await _pomodoroService.StopAsync();
        }
        catch (Exception exception)
        {
            _pendingSave = new PendingTargetSave(
                isCycle,
                advanceMode,
                notificationMessage,
                targetSeconds);
            await PauseAfterSaveFailureAsync();
            throw CreateSaveFailureException(exception);
        }

        _pendingSave = null;
        return await CompleteTransitionAsync(
            session,
            new PendingTargetSave(
                isCycle,
                advanceMode,
                notificationMessage,
                targetSeconds));
    }

    internal async Task<TimerTargetTransition?> CompletePendingRetryAsync(
        PomodoroSession? session)
    {
        if (session is null || _pendingSave is null)
            return null;

        var pendingSave = _pendingSave;
        _pendingSave = null;
        return await CompleteTransitionAsync(session, pendingSave);
    }

    internal async Task PauseAfterSaveFailureAsync()
    {
        if (_pomodoroService.CurrentState.Status == TimerStatus.Running)
            await _pomodoroService.PauseAsync();
    }

    internal static InvalidOperationException CreateSaveFailureException(Exception exception) =>
        new("专注记录保存失败，计时已暂停；请再次点击“停止”重试。", exception);

    private async Task<TimerTargetTransition> CompleteTransitionAsync(
        PomodoroSession? session,
        PendingTargetSave pendingSave)
    {
        var nextStageStarted = false;
        if (pendingSave.IsCycle)
        {
            var nextSeconds = _pomodoroService.AdvanceCycle();
            if (nextSeconds > 0)
            {
                await _pomodoroService.StartAsync(GetTaskIdForCurrentMode(), nextSeconds);
                nextStageStarted = true;
            }
        }
        else if (pendingSave.AdvanceMode)
        {
            _pomodoroService.AdvanceMode();
        }

        return new TimerTargetTransition(
            session,
            pendingSave.IsCycle,
            nextStageStarted,
            pendingSave.NotificationMessage,
            pendingSave.TargetSeconds);
    }

    private Guid? GetTaskIdForCurrentMode() =>
        _pomodoroService.CurrentMode == FocusMode.Focus
            ? _taskList.SelectedTaskId
            : null;

    private sealed record PendingTargetSave(
        bool IsCycle,
        bool AdvanceMode,
        string NotificationMessage,
        int TargetSeconds);
}

internal sealed record TimerTargetTransition(
    PomodoroSession? Session,
    bool IsCycle,
    bool NextStageStarted,
    string NotificationMessage,
    int TargetSeconds);
