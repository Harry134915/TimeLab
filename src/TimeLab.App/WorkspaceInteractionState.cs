namespace TimeLab.App;

/// <summary>
/// 在各功能 ViewModel 之间共享会影响命令可用性的运行状态。
/// </summary>
internal sealed class WorkspaceInteractionState
{
    private bool _isTaskMutationInProgress;
    private bool _isSessionMutationInProgress;
    private bool _isTimerOperationInProgress;
    private bool _isTimerActive;
    private bool _isExitPreparationInProgress;

    internal event Action? Changed;

    internal bool IsTaskMutationInProgress =>
        Volatile.Read(ref _isTaskMutationInProgress);

    internal bool IsSessionMutationInProgress =>
        Volatile.Read(ref _isSessionMutationInProgress);

    internal bool IsTimerOperationInProgress =>
        Volatile.Read(ref _isTimerOperationInProgress);

    internal bool IsTimerActive =>
        Volatile.Read(ref _isTimerActive);

    internal bool IsExitPreparationInProgress =>
        Volatile.Read(ref _isExitPreparationInProgress);

    internal void SetTaskMutationInProgress(bool value) =>
        SetState(ref _isTaskMutationInProgress, value);

    internal void SetSessionMutationInProgress(bool value) =>
        SetState(ref _isSessionMutationInProgress, value);

    internal void SetTimerOperationInProgress(bool value) =>
        SetState(ref _isTimerOperationInProgress, value);

    internal void SetTimerActive(bool value) =>
        SetState(ref _isTimerActive, value);

    internal void SetExitPreparationInProgress(bool value) =>
        SetState(ref _isExitPreparationInProgress, value);

    private void SetState(ref bool field, bool value)
    {
        if (field == value)
            return;

        Volatile.Write(ref field, value);
        Changed?.Invoke();
    }
}
