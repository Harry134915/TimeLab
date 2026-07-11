using System.Windows.Input;
using TimeLab.App;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

public class MainViewModelInteractionTests
{
    [Fact]
    public async Task StartTimer_RaisesStatusChangeOnlyOnceForRunningState()
    {
        var viewModel = CreateViewModel();
        var statusChanges = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.StatusText))
                statusChanges++;
        };

        await ExecuteAsync(viewModel.StartTimerCommand);

        Assert.Equal("运行中", viewModel.StatusText);
        Assert.Equal(1, statusChanges);
    }

    [Fact]
    public async Task TimerCommands_ReflectReadyRunningAndPausedStates()
    {
        var viewModel = CreateViewModel();
        var taskId = Guid.NewGuid();
        viewModel.Tasks.Add(new TaskItem
        {
            Id = taskId,
            Title = "可关联任务"
        });

        Assert.True(viewModel.StartTimerCommand.CanExecute(null));
        Assert.False(viewModel.PauseTimerCommand.CanExecute(null));
        Assert.False(viewModel.StopTimerCommand.CanExecute(null));
        Assert.True(viewModel.SelectTaskCommand.CanExecute(taskId));

        await ExecuteAsync(viewModel.StartTimerCommand);

        Assert.False(viewModel.StartTimerCommand.CanExecute(null));
        Assert.False(viewModel.StartCycleCommand.CanExecute(null));
        Assert.False(viewModel.SelectTaskCommand.CanExecute(taskId));
        Assert.False(viewModel.ResetTimerCommand.CanExecute(null));
        Assert.True(viewModel.PauseTimerCommand.CanExecute(null));
        Assert.True(viewModel.StopTimerCommand.CanExecute(null));

        await ExecuteAsync(viewModel.PauseTimerCommand);

        Assert.Equal("继续", viewModel.StartButtonText);
        Assert.True(viewModel.StartTimerCommand.CanExecute(null));
        Assert.False(viewModel.PauseTimerCommand.CanExecute(null));
        Assert.True(viewModel.StopTimerCommand.CanExecute(null));
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
        viewModel.NewTaskTitle = "编写回归测试";

        viewModel.NewTaskDuration = invalidDuration;

        Assert.False(viewModel.AddTaskCommand.CanExecute(null));
        Assert.False(string.IsNullOrWhiteSpace(viewModel.NewTaskDurationError));
    }

    [Fact]
    public void AddTaskCommand_PositiveIntegerDuration_IsEnabledWithoutError()
    {
        var viewModel = CreateViewModel();
        viewModel.NewTaskTitle = "编写回归测试";

        viewModel.NewTaskDuration = "25";

        Assert.True(viewModel.AddTaskCommand.CanExecute(null));
        Assert.True(string.IsNullOrEmpty(viewModel.NewTaskDurationError));
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
        await viewModel.UpdateTimerDisplayAsync();

        Assert.Equal("今日 1 次 · 25 分钟 · 0 个任务", viewModel.TodayStats);
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
        viewModel.Tasks.Add(task);
        viewModel.SelectedTaskId = task.Id;

        await ExecuteAsync(viewModel.StartTimerCommand);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-2);

        await viewModel.UpdateTimerDisplayAsync();

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
        viewModel.NewTaskTitle = "完成 UI 审查";
        viewModel.NewTaskDuration = "2";
        viewModel.DurationUnitIndex = 1;

        await ExecuteAsync(viewModel.AddTaskCommand);

        var task = Assert.Single(viewModel.Tasks);
        viewModel.SelectTaskCommand.Execute(task.Id);
        await ExecuteAsync(viewModel.StartTimerCommand);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-65);
        await ExecuteAsync(viewModel.StopTimerCommand);

        var session = Assert.Single(sessionRepository.Sessions);
        Assert.Equal(task.Id, session.TaskId);
        Assert.Equal(FocusMode.Focus, session.Mode);
        Assert.Single(viewModel.Sessions);
        Assert.Contains("今日 1 次 · 1 分钟", viewModel.TodayStats);
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
        viewModel.Tasks.Add(task);

        await ExecuteAsync(viewModel.DeleteTaskCommand, task.Id);

        Assert.Contains(task, viewModel.Tasks);
    }

    private static MainViewModel CreateViewModel(
        PomodoroService? pomodoroService = null,
        InMemorySessionRepository? sessionRepository = null,
        InMemoryTaskRepository? taskRepository = null,
        Func<string, bool>? onConfirmDelete = null)
    {
        pomodoroService ??= new PomodoroService(sessionRepository ?? new InMemorySessionRepository());
        var taskService = new TaskService(taskRepository ?? new InMemoryTaskRepository());
        return new MainViewModel(
            taskService,
            pomodoroService,
            onConfirmDelete: onConfirmDelete,
            onTimerReached: () => { });
    }

    private static PomodoroSession CreateSession(FocusMode mode, int minutes, DateTime startTime)
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

    private static Task ExecuteAsync(ICommand command, object? parameter = null)
    {
        return Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync(parameter);
    }
}
