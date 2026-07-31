using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.App;

/// <summary>
/// 管理专注记录、记录删除和今日统计。
/// </summary>
public sealed class SessionLogViewModel : ViewModelBase
{
    private readonly PomodoroService _pomodoroService;
    private readonly TaskListViewModel _taskList;
    private readonly WorkspaceInteractionState _interactionState;
    private readonly Func<string, bool> _confirmDelete;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private int _pendingMutationCount;
    private string _todayStats = string.Empty;
    private int _todayFocusCount;
    private int _todayFocusMinutes;
    private int _todayCompletedTaskCount;

    internal SessionLogViewModel(
        PomodoroService pomodoroService,
        TaskListViewModel taskList,
        WorkspaceInteractionState interactionState,
        Func<string, bool>? confirmDelete = null)
    {
        _pomodoroService = pomodoroService;
        _taskList = taskList;
        _interactionState = interactionState;
        _confirmDelete = confirmDelete ?? (_ => false);

        DeleteSessionCommand = new AsyncRelayCommand(
            p => RunMutationAsync(() => DeleteSessionAsync((Guid)p!)),
            HandleCommandException,
            _ => CanDeleteSession());
        ClearSessionsCommand = new AsyncRelayCommand(
            _ => RunMutationAsync(ClearSessionsAsync),
            HandleCommandException,
            _ => CanDeleteSession() && Sessions.Count > 0);

        Sessions.CollectionChanged += HandleCollectionChanged;
        _taskList.Tasks.CollectionChanged += HandleCollectionChanged;
        _interactionState.Changed += NotifyCommandStatesChanged;
        RefreshTodayStats();
    }

    internal event Action<string, bool>? NotificationRequested;

    public ObservableCollection<PomodoroSession> Sessions { get; } = [];

    public string TodayStats
    {
        get => _todayStats;
        private set => SetProperty(ref _todayStats, value);
    }

    public int TodayFocusCount
    {
        get => _todayFocusCount;
        private set => SetProperty(ref _todayFocusCount, value);
    }

    public int TodayFocusMinutes
    {
        get => _todayFocusMinutes;
        private set => SetProperty(ref _todayFocusMinutes, value);
    }

    public int TodayCompletedTaskCount
    {
        get => _todayCompletedTaskCount;
        private set => SetProperty(ref _todayCompletedTaskCount, value);
    }

    public AsyncRelayCommand DeleteSessionCommand { get; }
    public AsyncRelayCommand ClearSessionsCommand { get; }

    internal async Task LoadAsync()
    {
        var sessions = await _pomodoroService.GetSessionsAsync();
        foreach (var session in sessions)
            Sessions.Add(session);
        RefreshTodayStats();
    }

    internal void AddSession(PomodoroSession session) => Sessions.Add(session);

    internal async Task WaitForPendingOperationsAsync()
    {
        while (_interactionState.IsSessionMutationInProgress)
        {
            await _mutationGate.WaitAsync();
            _mutationGate.Release();
            await Task.Yield();
        }
    }

    internal void RefreshTodayStats()
    {
        var today = DateTime.Today;
        var sessions = Sessions
            .Where(session => session.StartTime.Date == today && session.Mode == FocusMode.Focus)
            .ToList();
        var count = sessions.Count;
        var totalMinutes = (int)sessions.Sum(session => session.Duration.TotalMinutes);
        var tasksDone = _taskList.Tasks.Count(task => task.CompletedAt?.Date == today);
        TodayFocusCount = count;
        TodayFocusMinutes = totalMinutes;
        TodayCompletedTaskCount = tasksDone;
        TodayStats = $"今日 {count} 次 · {totalMinutes} 分钟 · {tasksDone} 个任务";
    }

    private async Task DeleteSessionAsync(Guid id)
    {
        if (!_confirmDelete("确定删除这条专注记录吗？"))
            return;

        await _pomodoroService.DeleteSessionAsync(id);
        var session = Sessions.FirstOrDefault(item => item.Id == id);
        if (session is not null)
            Sessions.Remove(session);
    }

    private async Task ClearSessionsAsync()
    {
        var sessions = Sessions.ToArray();
        if (sessions.Length == 0
            || !_confirmDelete($"确定清空全部 {sessions.Length} 条专注记录吗？此操作无法撤销。"))
            return;

        foreach (var session in sessions)
        {
            await _pomodoroService.DeleteSessionAsync(session.Id);
            Sessions.Remove(session);
        }

        NotificationRequested?.Invoke("已清空全部专注记录", false);
    }

    private bool CanDeleteSession() =>
        !_interactionState.IsExitPreparationInProgress
        && !_interactionState.IsTimerOperationInProgress
        && !_interactionState.IsSessionMutationInProgress;

    private async Task RunMutationAsync(Func<Task> operation)
    {
        if (Interlocked.Increment(ref _pendingMutationCount) == 1)
            _interactionState.SetSessionMutationInProgress(true);

        await _mutationGate.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _mutationGate.Release();
            if (Interlocked.Decrement(ref _pendingMutationCount) == 0)
                _interactionState.SetSessionMutationInProgress(false);
        }
    }

    private void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshTodayStats();
        ClearSessionsCommand.RaiseCanExecuteChanged();
    }

    private void NotifyCommandStatesChanged()
    {
        DeleteSessionCommand.RaiseCanExecuteChanged();
        ClearSessionsCommand.RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void HandleCommandException(Exception exception) =>
        NotificationRequested?.Invoke($"操作失败：{exception.Message}", true);
}
