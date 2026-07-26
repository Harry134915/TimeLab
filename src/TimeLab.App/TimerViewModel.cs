using System.Windows.Input;
using TimeLab.Application;

namespace TimeLab.App;

/// <summary>
/// 暴露计时区域的 WPF 绑定和命令，具体工作流由内部协调器执行。
/// </summary>
public sealed class TimerViewModel : ViewModelBase
{
    private readonly TimerWorkflow _workflow;
    private readonly WorkspaceInteractionState _interactionState;
    private string _cycleFocusMinutesText = "25";
    private string _cycleBreakMinutesText = "5";
    private string _cycleTotalRoundsText = "4";
    private string _publishedStatusText;

    internal TimerViewModel(
        PomodoroService pomodoroService,
        TaskListViewModel taskList,
        SessionLogViewModel sessionLog,
        WorkspaceInteractionState interactionState,
        Action? onTimerReached = null)
    {
        _interactionState = interactionState;
        _workflow = new TimerWorkflow(
            pomodoroService,
            taskList,
            sessionLog,
            interactionState,
            onTimerReached);
        _publishedStatusText = _workflow.StatusText;
        _workflow.StateChanged += HandleWorkflowStateChanged;
        _workflow.NotificationRequested += (message, isError) =>
            NotificationRequested?.Invoke(message, isError);
        _interactionState.Changed += NotifyCommandStatesChanged;

        StartTimerCommand = CreateCommand(
            _workflow.StartTimerAsync,
            _ => _workflow.CanStartOrResumeTimer());
        PauseTimerCommand = CreateCommand(
            _workflow.PauseTimerAsync,
            _ => _workflow.IsTimerRunning());
        StopTimerCommand = CreateCommand(
            _workflow.StopTimerAsync,
            _ => !_interactionState.IsExitPreparationInProgress
                 && !_interactionState.IsTimerOperationInProgress
                 && IsTimerActive);
        ResetTimerCommand = CreateCommand(
            _workflow.ResetTimerAsync,
            _ => _workflow.CanResetTimer());
        ToggleTimerCommand = CreateCommand(
            _workflow.ToggleTimerAsync,
            _ => !_interactionState.IsExitPreparationInProgress
                 && !_interactionState.IsTimerOperationInProgress
                 && (IsTimerActive || !_interactionState.IsTaskMutationInProgress));
        StopOrResetCommand = CreateCommand(
            _workflow.StopOrResetAsync,
            _ => !_interactionState.IsExitPreparationInProgress
                 && !_interactionState.IsTimerOperationInProgress);
        StartPresetCommand = new AsyncRelayCommand(
            parameter => _workflow.RunOperationAsync(
                () => _workflow.StartPresetAsync((int)parameter!)),
            HandleCommandException,
            parameter => _workflow.CanStartNewTimer()
                         && parameter is int minutes
                         && minutes > 0
                         && minutes <= int.MaxValue / 60);
        StartCycleCommand = new AsyncRelayCommand(
            _ => _workflow.RunOperationAsync(
                () => _workflow.StartCycleAsync(
                    CycleFocusMinutes,
                    CycleBreakMinutes,
                    ParsePositiveInt(CycleTotalRoundsText))),
            HandleCommandException,
            _ => _workflow.CanStartNewTimer() && IsCycleConfigurationValid());
    }

    internal event Action<string, bool>? NotificationRequested;

    public int[] PresetMinutes => _workflow.PresetMinutes;
    public string ModeName => _workflow.ModeName;
    public int ModeDefaultMinutes => _workflow.ModeDefaultMinutes;
    public int CompletedFocusCount => _workflow.CompletedFocusCount;
    public bool IsCycleActive => _workflow.IsCycleActive;
    public string CycleProgress => _workflow.CycleProgress;
    public bool IsTargetReached => _workflow.IsTargetReached;
    public string ElapsedDisplay => _workflow.ElapsedDisplay;
    public string StatusText => _workflow.StatusText;
    public string StartButtonText => _workflow.StartButtonText;
    public bool IsTimerActive => _workflow.IsTimerActive;
    public bool CanEditTimerSetup => _workflow.CanEditTimerSetup;
    public string TimerDisplayLabel => _workflow.TimerDisplayLabel;
    public string TimerTargetSummary => _workflow.TimerTargetSummary;

    public int CycleFocusMinutes
    {
        get => ParsePositiveInt(CycleFocusMinutesText);
        set => CycleFocusMinutesText = value.ToString();
    }

    public string CycleFocusMinutesText
    {
        get => _cycleFocusMinutesText;
        set => SetCycleText(
            ref _cycleFocusMinutesText,
            value,
            nameof(CycleFocusMinutesText),
            nameof(CycleFocusMinutes));
    }

    public int CycleBreakMinutes
    {
        get => ParsePositiveInt(CycleBreakMinutesText);
        set => CycleBreakMinutesText = value.ToString();
    }

    public string CycleBreakMinutesText
    {
        get => _cycleBreakMinutesText;
        set => SetCycleText(
            ref _cycleBreakMinutesText,
            value,
            nameof(CycleBreakMinutesText),
            nameof(CycleBreakMinutes));
    }

    public string CycleTotalRoundsText
    {
        get => _cycleTotalRoundsText;
        set => SetCycleText(
            ref _cycleTotalRoundsText,
            value,
            nameof(CycleTotalRoundsText));
    }

    public string CycleValidationMessage => IsCycleConfigurationValid()
        ? string.Empty
        : "专注、休息和轮数都必须是大于 0 的整数";

    public AsyncRelayCommand StartTimerCommand { get; }
    public AsyncRelayCommand PauseTimerCommand { get; }
    public AsyncRelayCommand StopTimerCommand { get; }
    public AsyncRelayCommand ResetTimerCommand { get; }
    public AsyncRelayCommand ToggleTimerCommand { get; }
    public AsyncRelayCommand StopOrResetCommand { get; }
    public AsyncRelayCommand StartPresetCommand { get; }
    public AsyncRelayCommand StartCycleCommand { get; }

    internal void InitializeState() => _workflow.InitializeState();

    internal Task SaveActiveSessionForExitAsync() =>
        _workflow.PrepareForExitAsync(saveActiveTimer: true);

    internal Task PrepareForExitAsync(bool saveActiveTimer) =>
        _workflow.PrepareForExitAsync(saveActiveTimer);

    internal Task UpdateTimerDisplayAsync() => _workflow.UpdateDisplayAsync();

    private AsyncRelayCommand CreateCommand(
        Func<Task> operation,
        Func<object?, bool> canExecute) =>
        new(
            _ => _workflow.RunOperationAsync(operation),
            HandleCommandException,
            canExecute);

    private void SetCycleText(
        ref string field,
        string value,
        string propertyName,
        string? numericPropertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
            return;
        if (numericPropertyName is not null)
            OnPropertyChanged(numericPropertyName);
        OnPropertyChanged(nameof(CycleValidationMessage));
        NotifyCommandStatesChanged();
    }

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

    private static int ParsePositiveInt(string value) =>
        int.TryParse(value, out var result) && result > 0 ? result : 0;

    private void HandleWorkflowStateChanged()
    {
        OnPropertyChanged(nameof(PresetMinutes));
        OnPropertyChanged(nameof(ModeName));
        OnPropertyChanged(nameof(ModeDefaultMinutes));
        OnPropertyChanged(nameof(CompletedFocusCount));
        OnPropertyChanged(nameof(IsCycleActive));
        OnPropertyChanged(nameof(CycleProgress));
        OnPropertyChanged(nameof(IsTargetReached));
        OnPropertyChanged(nameof(ElapsedDisplay));
        if (_publishedStatusText != StatusText)
        {
            _publishedStatusText = StatusText;
            OnPropertyChanged(nameof(StatusText));
        }
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(IsTimerActive));
        OnPropertyChanged(nameof(CanEditTimerSetup));
        OnPropertyChanged(nameof(TimerDisplayLabel));
        OnPropertyChanged(nameof(TimerTargetSummary));
        NotifyCommandStatesChanged();
    }

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
