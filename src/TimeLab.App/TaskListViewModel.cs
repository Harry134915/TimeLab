using System.Collections.ObjectModel;
using System.Windows.Input;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.App;

/// <summary>
/// 管理任务列表、任务输入、关联选择和任务写入操作。
/// </summary>
public sealed class TaskListViewModel : ViewModelBase
{
    private readonly TaskService _taskService;
    private readonly WorkspaceInteractionState _interactionState;
    private readonly Func<string, bool> _confirmDelete;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private int _pendingMutationCount;
    private string _newTaskTitle = string.Empty;
    private string _newTaskDuration = string.Empty;
    private int _durationUnitIndex;
    private Guid? _selectedTaskId;

    internal TaskListViewModel(
        TaskService taskService,
        WorkspaceInteractionState interactionState,
        Func<string, bool>? confirmDelete = null)
    {
        _taskService = taskService;
        _interactionState = interactionState;
        _confirmDelete = confirmDelete ?? (_ => false);
        _interactionState.Changed += NotifyCommandStatesChanged;

        AddTaskCommand = new AsyncRelayCommand(
            _ => RunMutationAsync(AddTaskAsync),
            HandleCommandException,
            _ => CanAddTask());
        CompleteTaskCommand = new AsyncRelayCommand(
            p => RunMutationAsync(() => CompleteTaskAsync((Guid)p!)),
            HandleCommandException,
            CanCompleteTask);
        DeleteTaskCommand = new AsyncRelayCommand(
            p => RunMutationAsync(() => DeleteTaskAsync((Guid)p!)),
            HandleCommandException,
            CanDeleteTask);
        SelectTaskCommand = new RelayCommand(
            p => SelectedTaskId = (Guid)p!,
            CanSelectTask);
        ClearSelectedTaskCommand = new RelayCommand(
            _ => SelectedTaskId = null,
            _ => CanClearSelectedTask());
    }

    internal event Action<string, bool>? NotificationRequested;

    public ObservableCollection<TaskItem> Tasks { get; } = [];

    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set
        {
            if (!SetProperty(ref _newTaskTitle, value))
                return;
            NotifyCommandStatesChanged();
        }
    }

    public string NewTaskDuration
    {
        get => _newTaskDuration;
        set
        {
            if (!SetProperty(ref _newTaskDuration, value))
                return;
            OnPropertyChanged(nameof(NewTaskDurationError));
            NotifyCommandStatesChanged();
        }
    }

    public int DurationUnitIndex
    {
        get => _durationUnitIndex;
        set
        {
            if (!SetProperty(ref _durationUnitIndex, value))
                return;
            OnPropertyChanged(nameof(NewTaskDurationError));
            NotifyCommandStatesChanged();
        }
    }

    public string NewTaskDurationError => TryGetDurationSeconds(out _)
        ? string.Empty
        : "请输入大于 0 的整数";

    public List<string> DurationUnits { get; } = ["秒", "分钟", "时"];

    public Guid? SelectedTaskId
    {
        get => _selectedTaskId;
        set
        {
            if (!SetProperty(ref _selectedTaskId, value))
                return;
            OnPropertyChanged(nameof(SelectedTaskTitle));
            NotifyCommandStatesChanged();
        }
    }

    public string SelectedTaskTitle =>
        SelectedTaskId.HasValue
            ? Tasks.FirstOrDefault(task => task.Id == SelectedTaskId.Value)?.Title ?? "无"
            : "无";

    public AsyncRelayCommand AddTaskCommand { get; }
    public AsyncRelayCommand CompleteTaskCommand { get; }
    public AsyncRelayCommand DeleteTaskCommand { get; }
    public RelayCommand SelectTaskCommand { get; }
    public RelayCommand ClearSelectedTaskCommand { get; }

    internal async Task LoadAsync()
    {
        var tasks = await _taskService.GetAllAsync();
        foreach (var task in tasks)
            Tasks.Add(task);
        OnPropertyChanged(nameof(SelectedTaskTitle));
    }

    internal int GetSelectedTaskTargetSeconds() =>
        SelectedTaskId.HasValue
            ? Tasks.FirstOrDefault(task => task.Id == SelectedTaskId.Value)?.PlannedSeconds ?? 0
            : 0;

    internal TaskItem? GetSelectedTask() =>
        SelectedTaskId.HasValue
            ? Tasks.FirstOrDefault(task => task.Id == SelectedTaskId.Value)
            : null;

    internal async Task WaitForPendingOperationsAsync()
    {
        while (_interactionState.IsTaskMutationInProgress)
        {
            await _mutationGate.WaitAsync();
            _mutationGate.Release();
            await Task.Yield();
        }
    }

    internal void RefreshCommandStates() => NotifyCommandStatesChanged();

    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle) || !TryGetDurationSeconds(out var seconds))
            return;

        var item = await _taskService.CreateAsync(NewTaskTitle.Trim(), seconds);
        Tasks.Add(item);
        NewTaskTitle = string.Empty;
        NewTaskDuration = string.Empty;
    }

    private async Task CompleteTaskAsync(Guid id)
    {
        TaskItem? completedItem;
        try
        {
            completedItem = await _taskService.CompleteAsync(id);
        }
        catch
        {
            var unchangedItem = Tasks.FirstOrDefault(task => task.Id == id);
            if (unchangedItem is not null)
            {
                var unchangedIndex = Tasks.IndexOf(unchangedItem);
                Tasks[unchangedIndex] = unchangedItem;
            }
            throw;
        }

        var currentItem = Tasks.FirstOrDefault(task => task.Id == id);
        if (completedItem is null || currentItem is null)
            return;

        Tasks[Tasks.IndexOf(currentItem)] = completedItem;
        if (SelectedTaskId == id)
            SelectedTaskId = null;
        else
            OnPropertyChanged(nameof(SelectedTaskTitle));

        NotifyCommandStatesChanged();
    }

    private async Task DeleteTaskAsync(Guid id)
    {
        var item = Tasks.FirstOrDefault(task => task.Id == id);
        if (item is null || !_confirmDelete($"确定删除任务“{item.Title}”吗？"))
            return;

        await _taskService.DeleteAsync(id);
        Tasks.Remove(item);
        if (SelectedTaskId == id)
            SelectedTaskId = null;
    }

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

    private bool CanAddTask() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTaskMutationInProgress
        && !string.IsNullOrWhiteSpace(NewTaskTitle)
        && TryGetDurationSeconds(out _);

    private bool CanCompleteTask(object? parameter) =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTaskMutationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && !_interactionState.IsTimerActive
        && parameter is Guid id
        && Tasks.FirstOrDefault(task => task.Id == id) is { IsCompleted: false };

    private bool CanDeleteTask(object? parameter) =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTaskMutationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && !_interactionState.IsTimerActive
        && parameter is Guid id
        && Tasks.Any(task => task.Id == id);

    private bool CanSelectTask(object? parameter) =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTaskMutationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && !_interactionState.IsTimerActive
        && parameter is Guid id
        && Tasks.FirstOrDefault(task => task.Id == id) is { IsCompleted: false };

    private bool CanClearSelectedTask() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTaskMutationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && !_interactionState.IsTimerActive
        && SelectedTaskId.HasValue;

    private async Task RunMutationAsync(Func<Task> operation)
    {
        if (Interlocked.Increment(ref _pendingMutationCount) == 1)
            _interactionState.SetTaskMutationInProgress(true);

        await _mutationGate.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _mutationGate.Release();
            if (Interlocked.Decrement(ref _pendingMutationCount) == 0)
                _interactionState.SetTaskMutationInProgress(false);
        }
    }

    private void NotifyCommandStatesChanged()
    {
        AddTaskCommand.RaiseCanExecuteChanged();
        CompleteTaskCommand.RaiseCanExecuteChanged();
        DeleteTaskCommand.RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void HandleCommandException(Exception exception) =>
        NotificationRequested?.Invoke($"操作失败：{exception.Message}", true);
}
