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

        await ExecuteAsync(viewModel.StartPresetCommand, 1);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);

        await viewModel.UpdateTimerDisplayAsync();

        var session = Assert.Single(repository.Sessions);
        Assert.Equal(FocusMode.Focus, session.Mode);
        Assert.Equal(FocusMode.ShortBreak, pomodoroService.CurrentMode);
        Assert.Equal(TimerStatus.Stopped, pomodoroService.CurrentState.Status);
        Assert.Same(session, Assert.Single(viewModel.Sessions));
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
        viewModel.Tasks.Add(task);
        viewModel.SelectedTaskId = task.Id;

        await ExecuteAsync(viewModel.StartTimerCommand);

        Assert.Equal(task.PlannedSeconds, pomodoroService.TargetSeconds);

        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-31);
        await viewModel.UpdateTimerDisplayAsync();

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
        viewModel.CycleFocusMinutes = 1;
        viewModel.CycleBreakMinutes = 1;
        viewModel.CycleTotalRoundsText = "2";

        await ExecuteAsync(viewModel.StartCycleCommand);
        pomodoroService.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);

        await viewModel.UpdateTimerDisplayAsync();

        var session = Assert.Single(repository.Sessions);
        Assert.Equal(FocusMode.Focus, session.Mode);
        Assert.Equal(FocusMode.ShortBreak, pomodoroService.CurrentMode);
        Assert.Equal(TimerStatus.Running, pomodoroService.CurrentState.Status);
        Assert.Equal(60, pomodoroService.TargetSeconds);
        Assert.Equal(1, pomodoroService.CurrentRound);
        Assert.True(pomodoroService.IsCycleActive);
    }

    private static MainViewModel CreateViewModel(PomodoroService pomodoroService)
    {
        var taskService = new TaskService(new InMemoryTaskRepository());
        return new MainViewModel(
            taskService,
            pomodoroService,
            onTimerReached: () => { });
    }

    private static Task ExecuteAsync(ICommand command, object? parameter = null)
    {
        return Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync(parameter);
    }
}
