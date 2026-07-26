using System.Windows.Input;
using TimeLab.App;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

public class MainViewModelTimerTests
{
    [Fact]
    public async Task PresetTargetReached_SavesCompletedModeBeforeAdvancing()
    {
        var repository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);

        await ExecuteAsync(viewModel.Timer.StartPresetCommand, 1);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);

        await viewModel.Timer.UpdateTimerDisplayAsync();

        var session = Assert.Single(repository.Sessions);
        Assert.Equal(FocusMode.Focus, session.Mode);
        Assert.Equal(FocusMode.ShortBreak, pomodoroService.CurrentMode);
        Assert.Equal(TimerStatus.Stopped, pomodoroService.CurrentState.Status);
        Assert.Same(session, Assert.Single(viewModel.SessionLog.Sessions));
    }

    [Fact]
    public async Task BreakPreset_AfterFocusCompletion_PreservesAndSavesBreakMode()
    {
        var repository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);

        await ExecuteAsync(viewModel.Timer.StartPresetCommand, 1);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);
        await viewModel.Timer.UpdateTimerDisplayAsync();

        Assert.Equal(FocusMode.ShortBreak, pomodoroService.CurrentMode);

        await ExecuteAsync(viewModel.Timer.StartPresetCommand, 1);
        Assert.Equal(FocusMode.ShortBreak, pomodoroService.CurrentMode);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);
        await viewModel.Timer.UpdateTimerDisplayAsync();

        Assert.Equal(2, repository.Sessions.Count);
        Assert.Equal(FocusMode.Focus, repository.Sessions[0].Mode);
        Assert.Equal(FocusMode.ShortBreak, repository.Sessions[1].Mode);
        Assert.Equal(FocusMode.Focus, pomodoroService.CurrentMode);
        Assert.StartsWith("今日 1 次", viewModel.SessionLog.TodayStats);
    }

    [Fact]
    public async Task Reset_AfterFocusCompletion_ReturnsToFocusReadyState()
    {
        var repository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);

        await ExecuteAsync(viewModel.Timer.StartPresetCommand, 1);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);
        await viewModel.Timer.UpdateTimerDisplayAsync();
        await ExecuteAsync(viewModel.Timer.ResetTimerCommand);

        Assert.Equal(FocusMode.Focus, pomodoroService.CurrentMode);
        Assert.Equal(0, pomodoroService.CompletedFocusCount);
        Assert.Equal(TimerStatus.Idle, pomodoroService.CurrentState.Status);
        Assert.Contains(25, viewModel.Timer.PresetMinutes);
    }

    [Fact]
    public async Task MainStart_InSuggestedBreak_UsesDefaultBreakAndDoesNotLinkTask()
    {
        var repository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "关联专注任务",
            PlannedSeconds = 30
        };
        viewModel.TaskList.Tasks.Add(task);
        viewModel.TaskList.SelectedTaskId = task.Id;

        await ExecuteAsync(viewModel.Timer.StartPresetCommand, 1);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-31);
        await viewModel.Timer.UpdateTimerDisplayAsync();

        Assert.Equal(FocusMode.ShortBreak, pomodoroService.CurrentMode);

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);

        Assert.Equal(FocusMode.ShortBreak, pomodoroService.CurrentMode);
        Assert.Equal(5 * 60, pomodoroService.TargetSeconds);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-(5 * 60 + 1));
        await viewModel.Timer.UpdateTimerDisplayAsync();

        Assert.Equal(2, repository.Sessions.Count);
        Assert.Equal(task.Id, repository.Sessions[0].TaskId);
        Assert.Null(repository.Sessions[1].TaskId);
        Assert.Equal(FocusMode.ShortBreak, repository.Sessions[1].Mode);
        Assert.Equal(FocusMode.Focus, pomodoroService.CurrentMode);
    }

    [Fact]
    public async Task CountdownDisplay_CeilsFractionalRemainingSecond()
    {
        var pomodoroService = new PomodoroService(new InMemorySessionRepository());
        var viewModel = CreateViewModel(pomodoroService);
        await pomodoroService.StartAsync(targetSeconds: 60);
        pomodoroService.CurrentState.Status = TimerStatus.Paused;
        pomodoroService.CurrentState.StartTime = null;
        pomodoroService.CurrentState.ElapsedTime = TimeSpan.FromMilliseconds(200);

        await viewModel.Timer.UpdateTimerDisplayAsync();

        Assert.Equal("00:01:00", viewModel.Timer.ElapsedDisplay);
    }

    [Fact]
    public async Task TargetReached_ExternalNotificationFailures_DoNotBlockSessionCompletion()
    {
        var repository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = ViewModelTestFactory.Create(
            pomodoroService,
            new TaskService(new InMemoryTaskRepository()),
            onTimerReached: () => throw new InvalidOperationException("声音不可用"),
            onBalloon: _ => throw new InvalidOperationException("托盘不可用"));

        await ExecuteAsync(viewModel.Timer.StartPresetCommand, 1);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);

        await viewModel.Timer.UpdateTimerDisplayAsync();

        Assert.Single(repository.Sessions);
        Assert.Equal(TimerStatus.Stopped, pomodoroService.CurrentState.Status);
        Assert.True(viewModel.Timer.IsTargetReached);
    }

    [Fact]
    public async Task TaskTargetReached_UsesPlannedSecondsAndDoesNotAdvanceMode()
    {
        var repository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "编写测试",
            PlannedSeconds = 30
        };
        viewModel.TaskList.Tasks.Add(task);
        viewModel.TaskList.SelectedTaskId = task.Id;

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);

        Assert.Equal(task.PlannedSeconds, pomodoroService.TargetSeconds);

        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-31);
        await viewModel.Timer.UpdateTimerDisplayAsync();

        var session = Assert.Single(repository.Sessions);
        Assert.Equal(task.Id, session.TaskId);
        Assert.Equal(FocusMode.Focus, session.Mode);
        Assert.Equal(FocusMode.Focus, pomodoroService.CurrentMode);
        Assert.Equal(TimerStatus.Stopped, pomodoroService.CurrentState.Status);
        Assert.Contains(task.Title, viewModel.NotificationMessage);
    }

    [Fact]
    public async Task CycleFocusTargetReached_SavesFocusAndStartsBreak()
    {
        var repository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);
        viewModel.Timer.CycleFocusMinutes = 1;
        viewModel.Timer.CycleBreakMinutes = 1;
        viewModel.Timer.CycleTotalRoundsText = "2";

        await ExecuteAsync(viewModel.Timer.StartCycleCommand);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);

        await viewModel.Timer.UpdateTimerDisplayAsync();

        var session = Assert.Single(repository.Sessions);
        Assert.Equal(FocusMode.Focus, session.Mode);
        Assert.Equal(FocusMode.ShortBreak, pomodoroService.CurrentMode);
        Assert.Equal(TimerStatus.Running, pomodoroService.CurrentState.Status);
        Assert.Equal(60, pomodoroService.TargetSeconds);
        Assert.Equal(1, pomodoroService.CurrentRound);
        Assert.True(pomodoroService.IsCycleActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareForExitAsync_RunningOrPausedTimer_SavesAndStops(bool pauseFirst)
    {
        var repository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        if (pauseFirst)
            await ExecuteAsync(viewModel.Timer.PauseTimerCommand);

        await viewModel.PrepareForExitAsync(saveActiveTimer: true);

        var session = Assert.Single(repository.Sessions);
        Assert.Same(session, Assert.Single(viewModel.SessionLog.Sessions));
        Assert.Equal(TimerStatus.Stopped, pomodoroService.CurrentState.Status);
        Assert.False(viewModel.Timer.IsTimerActive);
    }

    [Fact]
    public async Task PrepareForExitAsync_SaveFails_KeepsRetryableTimerState()
    {
        var repository = new FailOnceSessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);

        await ExecuteAsync(viewModel.Timer.StartPresetCommand, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.PrepareForExitAsync(saveActiveTimer: true));

        Assert.Equal(TimerStatus.Paused, pomodoroService.CurrentState.Status);
        Assert.Null(pomodoroService.CurrentState.StartTime);
        Assert.True(pomodoroService.CurrentState.ElapsedTime > TimeSpan.Zero);
        Assert.Equal(60, pomodoroService.TargetSeconds);
        Assert.True(viewModel.Timer.IsTimerActive);
        Assert.True(viewModel.Timer.StopTimerCommand.CanExecute(null));
        Assert.Empty(viewModel.SessionLog.Sessions);

        await viewModel.PrepareForExitAsync(saveActiveTimer: true);

        Assert.Equal(TimerStatus.Stopped, pomodoroService.CurrentState.Status);
        Assert.False(viewModel.Timer.IsTimerActive);
        Assert.Single(repository.Sessions);
        Assert.Single(viewModel.SessionLog.Sessions);
    }

    [Fact]
    public async Task PrepareForExitAsync_WaitsForInFlightStopWithoutDuplicateSession()
    {
        var repository = new DelayedSessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        var stopTask = ExecuteAsync(viewModel.Timer.StopTimerCommand);
        await repository.AddStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var exitSaveTask = viewModel.PrepareForExitAsync(saveActiveTimer: true);
        Assert.False(exitSaveTask.IsCompleted);

        repository.AllowAddToComplete.TrySetResult();
        await Task.WhenAll(stopTask, exitSaveTask);

        Assert.Equal(TimerStatus.Stopped, pomodoroService.CurrentState.Status);
        Assert.Single(repository.Sessions);
        Assert.Single(viewModel.SessionLog.Sessions);
    }

    [Fact]
    public async Task PrepareForExitAsync_DiscardActiveTimer_StopsWithoutSaving()
    {
        var repository = new InMemorySessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        await viewModel.PrepareForExitAsync(saveActiveTimer: false);

        Assert.Equal(TimerStatus.Idle, pomodoroService.CurrentState.Status);
        Assert.False(viewModel.Timer.IsTimerActive);
        Assert.Empty(repository.Sessions);
        Assert.Empty(viewModel.SessionLog.Sessions);
    }

    [Fact]
    public async Task PrepareForExitAsync_ConcurrentCallers_WaitForSameSave()
    {
        var repository = new DelayedSessionRepository();
        var pomodoroService = new PomodoroService(repository);
        var viewModel = CreateViewModel(pomodoroService);

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        var firstExit = viewModel.PrepareForExitAsync(saveActiveTimer: true);
        await repository.AddStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondExit = viewModel.PrepareForExitAsync(saveActiveTimer: true);

        Assert.False(firstExit.IsCompleted);
        Assert.False(secondExit.IsCompleted);

        repository.AllowAddToComplete.TrySetResult();
        await Task.WhenAll(firstExit, secondExit);

        Assert.Single(repository.Sessions);
        Assert.Single(viewModel.SessionLog.Sessions);
        Assert.Equal(TimerStatus.Stopped, pomodoroService.CurrentState.Status);
    }

    private static MainViewModel CreateViewModel(PomodoroService pomodoroService) =>
        ViewModelTestFactory.Create(pomodoroService);

    private static Task ExecuteAsync(ICommand command, object? parameter = null) =>
        Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync(parameter);

    private sealed class FailOnceSessionRepository : ISessionRepository
    {
        private bool _shouldFail = true;

        public List<PomodoroSession> Sessions { get; } = [];

        public Task<IReadOnlyList<PomodoroSession>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PomodoroSession>>(Sessions);

        public Task AddAsync(PomodoroSession session)
        {
            if (_shouldFail)
            {
                _shouldFail = false;
                throw new InvalidOperationException("模拟保存失败");
            }

            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id)
        {
            Sessions.RemoveAll(session => session.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class DelayedSessionRepository : ISessionRepository
    {
        public List<PomodoroSession> Sessions { get; } = [];
        public TaskCompletionSource AddStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowAddToComplete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<PomodoroSession>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PomodoroSession>>(Sessions);

        public async Task AddAsync(PomodoroSession session)
        {
            AddStarted.TrySetResult();
            await AllowAddToComplete.Task;
            Sessions.Add(session);
        }

        public Task DeleteAsync(Guid id)
        {
            Sessions.RemoveAll(session => session.Id == id);
            return Task.CompletedTask;
        }
    }
}
