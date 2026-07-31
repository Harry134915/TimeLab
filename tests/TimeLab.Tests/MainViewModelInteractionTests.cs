using System.Windows.Input;
using TimeLab.App;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

public class MainViewModelInteractionTests
{
    [Fact]
    public void SessionLogNavigation_ShowsRecordsAndReturnsToWorkspace()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.IsSessionLogViewVisible);

        viewModel.ShowSessionLogCommand.Execute(null);

        Assert.True(viewModel.IsSessionLogViewVisible);

        viewModel.BackToWorkspaceCommand.Execute(null);

        Assert.False(viewModel.IsSessionLogViewVisible);
    }

    [Fact]
    public async Task StartTimer_RaisesStatusChangeOnlyOnceForRunningState()
    {
        var viewModel = CreateViewModel();
        var statusChanges = 0;
        viewModel.Timer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TimerViewModel.StatusText))
                statusChanges++;
        };

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);

        Assert.Equal("运行中", viewModel.Timer.StatusText);
        Assert.Equal(1, statusChanges);
    }

    [Fact]
    public async Task TimerCommands_ReflectReadyRunningAndPausedStates()
    {
        var viewModel = CreateViewModel();
        var taskId = Guid.NewGuid();
        viewModel.TaskList.Tasks.Add(new TaskItem
        {
            Id = taskId,
            Title = "可关联任务"
        });

        Assert.True(viewModel.Timer.StartTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.PauseTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.StopTimerCommand.CanExecute(null));
        Assert.True(viewModel.TaskList.SelectTaskCommand.CanExecute(taskId));

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);

        Assert.False(viewModel.Timer.StartTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.StartCycleCommand.CanExecute(null));
        Assert.False(viewModel.TaskList.SelectTaskCommand.CanExecute(taskId));
        Assert.False(viewModel.Timer.ResetTimerCommand.CanExecute(null));
        Assert.True(viewModel.Timer.PauseTimerCommand.CanExecute(null));
        Assert.True(viewModel.Timer.StopTimerCommand.CanExecute(null));

        await ExecuteAsync(viewModel.Timer.PauseTimerCommand);

        Assert.Equal("继续", viewModel.Timer.StartButtonText);
        Assert.True(viewModel.Timer.StartTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.PauseTimerCommand.CanExecute(null));
        Assert.True(viewModel.Timer.StopTimerCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("不是数字")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public void AddTaskCommand_NonEmptyNonPositiveIntegerDuration_IsDisabledWithError(
        string invalidDuration)
    {
        var viewModel = CreateViewModel();
        viewModel.TaskList.NewTaskTitle = "编写回归测试";

        viewModel.TaskList.NewTaskDuration = invalidDuration;

        Assert.False(viewModel.TaskList.AddTaskCommand.CanExecute(null));
        Assert.False(string.IsNullOrWhiteSpace(viewModel.TaskList.NewTaskDurationError));
    }

    [Fact]
    public void AddTaskCommand_PositiveIntegerDuration_IsEnabledWithoutError()
    {
        var viewModel = CreateViewModel();
        viewModel.TaskList.NewTaskTitle = "编写回归测试";

        viewModel.TaskList.NewTaskDuration = "25";

        Assert.True(viewModel.TaskList.AddTaskCommand.CanExecute(null));
        Assert.True(string.IsNullOrEmpty(viewModel.TaskList.NewTaskDurationError));
    }

    [Fact]
    public async Task TodayStats_CountsOnlyFocusSessions()
    {
        var sessionRepository = new InMemorySessionRepository();
        sessionRepository.Sessions.Add(CreateSession(FocusMode.Focus, 25, DateTime.Today.AddHours(9)));
        sessionRepository.Sessions.Add(CreateSession(FocusMode.ShortBreak, 5, DateTime.Today.AddHours(10)));
        sessionRepository.Sessions.Add(CreateSession(FocusMode.LongBreak, 15, DateTime.Today.AddHours(11)));
        sessionRepository.Sessions.Add(CreateSession(FocusMode.Focus, 45, DateTime.Today.AddDays(-1)));
        var viewModel = CreateViewModel(sessionRepository: sessionRepository);

        await viewModel.LoadAsync();
        await viewModel.Timer.UpdateTimerDisplayAsync();

        Assert.Equal("今日 1 次 · 25 分钟 · 0 个任务", viewModel.SessionLog.TodayStats);
        Assert.Equal(1, viewModel.SessionLog.TodayFocusCount);
        Assert.Equal(25, viewModel.SessionLog.TodayFocusMinutes);
        Assert.Equal(0, viewModel.SessionLog.TodayCompletedTaskCount);
    }

    [Fact]
    public async Task ClearSessions_WhenConfirmed_RemovesAllRecords()
    {
        var sessionRepository = new InMemorySessionRepository();
        sessionRepository.Sessions.Add(CreateSession(FocusMode.Focus, 25, DateTime.Today.AddHours(9)));
        sessionRepository.Sessions.Add(CreateSession(FocusMode.ShortBreak, 5, DateTime.Today.AddHours(10)));
        var viewModel = CreateViewModel(
            sessionRepository: sessionRepository,
            onConfirmDelete: _ => true);
        await viewModel.LoadAsync();

        await ExecuteAsync(viewModel.SessionLog.ClearSessionsCommand);

        Assert.Empty(viewModel.SessionLog.Sessions);
        Assert.Empty(sessionRepository.Sessions);
        Assert.False(viewModel.SessionLog.ClearSessionsCommand.CanExecute(null));
        Assert.Equal("已清空全部专注记录", viewModel.NotificationMessage);
    }

    [Fact]
    public async Task ClearSessions_WhenConfirmationIsDeclined_KeepsRecords()
    {
        var sessionRepository = new InMemorySessionRepository();
        sessionRepository.Sessions.Add(CreateSession(FocusMode.Focus, 25, DateTime.Today.AddHours(9)));
        var viewModel = CreateViewModel(
            sessionRepository: sessionRepository,
            onConfirmDelete: _ => false);
        await viewModel.LoadAsync();

        await ExecuteAsync(viewModel.SessionLog.ClearSessionsCommand);

        Assert.Single(viewModel.SessionLog.Sessions);
        Assert.Single(sessionRepository.Sessions);
    }

    [Fact]
    public async Task TaskTargetReached_ReportsFocusCompletionWithoutClaimingTaskCompletion()
    {
        var sessionRepository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(sessionRepository);
        var viewModel = CreateViewModel(pomodoroService: pomodoroService);
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "整理审查结果",
            PlannedSeconds = 1
        };
        viewModel.TaskList.Tasks.Add(task);
        viewModel.TaskList.SelectedTaskId = task.Id;

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-2);

        await viewModel.Timer.UpdateTimerDisplayAsync();

        Assert.Contains("本次专注完成", viewModel.NotificationMessage);
        Assert.DoesNotContain("任务已完成", viewModel.NotificationMessage);
    }

    [Fact]
    public async Task CoreFocusFlow_CreatesTaskAndLinkedFocusSession()
    {
        var taskRepository = new InMemoryTaskRepository();
        var sessionRepository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(sessionRepository);
        var viewModel = CreateViewModel(
            pomodoroService,
            sessionRepository,
            taskRepository);
        viewModel.TaskList.NewTaskTitle = "完成 UI 审查";
        viewModel.TaskList.NewTaskDuration = "2";
        viewModel.TaskList.DurationUnitIndex = 1;

        await ExecuteAsync(viewModel.TaskList.AddTaskCommand);

        var task = Assert.Single(viewModel.TaskList.Tasks);
        viewModel.TaskList.SelectTaskCommand.Execute(task.Id);
        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-65);
        await ExecuteAsync(viewModel.Timer.StopTimerCommand);

        var session = Assert.Single(sessionRepository.Sessions);
        Assert.Equal(task.Id, session.TaskId);
        Assert.Equal(FocusMode.Focus, session.Mode);
        Assert.Single(viewModel.SessionLog.Sessions);
        Assert.Contains("今日 1 次 · 1 分钟", viewModel.SessionLog.TodayStats);
    }

    [Fact]
    public async Task DeleteTask_WhenConfirmationIsDeclined_KeepsTask()
    {
        var viewModel = CreateViewModel(onConfirmDelete: _ => false);
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "不要误删"
        };
        viewModel.TaskList.Tasks.Add(task);

        await ExecuteAsync(viewModel.TaskList.DeleteTaskCommand, task.Id);

        Assert.Contains(task, viewModel.TaskList.Tasks);
    }

    private static MainViewModel CreateViewModel(
        PomodoroService? pomodoroService = null,
        InMemorySessionRepository? sessionRepository = null,
        InMemoryTaskRepository? taskRepository = null,
        Func<string, bool>? onConfirmDelete = null)
    {
        pomodoroService ??= new PomodoroService(
            sessionRepository ?? new InMemorySessionRepository());
        var taskService = new TaskService(taskRepository ?? new InMemoryTaskRepository());
        return ViewModelTestFactory.Create(
            pomodoroService,
            taskService,
            onConfirmDelete);
    }

    private static PomodoroSession CreateSession(
        FocusMode mode,
        int minutes,
        DateTime startTime)
    {
        return new PomodoroSession
        {
            Id = Guid.NewGuid(),
            StartTime = startTime,
            EndTime = startTime.AddMinutes(minutes),
            Duration = TimeSpan.FromMinutes(minutes),
            Mode = mode
        };
    }

    private static Task ExecuteAsync(ICommand command, object? parameter = null) =>
        Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync(parameter);
}
