using System.Windows.Input;
using TimeLab.App;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

public class WindowLifecycleCoordinatorTests
{
    [Fact]
    public async Task WindowLifecycle_HidesRestoresAndClosesOnlyAfterExitPreparation()
    {
        var viewModel = CreateViewModel(new InMemorySessionRepository());
        var host = new FakeWindowHost();
        var dialogs = new FakeWindowDialogService();
        var trayDisposed = false;
        using var coordinator = new WindowLifecycleCoordinator(
            host,
            dialogs,
            (_, _) => new TrayIconHandle(_ => { }, () => trayDisposed = true));
        coordinator.Attach(viewModel);

        Assert.True(coordinator.HandleClosing());
        Assert.Equal(1, host.HideCount);

        coordinator.ShowFromTray();
        Assert.Equal(1, host.ShowAndActivateCount);

        await coordinator.ExitFromTrayAsync();

        Assert.Equal(1, host.CloseCount);
        Assert.False(coordinator.HandleClosing());
        Assert.Equal(0, dialogs.ExitConfirmationCount);

        coordinator.Dispose();
        Assert.True(trayDisposed);
    }

    [Fact]
    public async Task ExitFromTray_ActiveTimerSaveChoice_SavesSessionAndCloses()
    {
        var repository = new InMemorySessionRepository();
        var viewModel = CreateViewModel(repository);
        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        var dialogs = new FakeWindowDialogService
        {
            ExitChoice = ActiveTimerExitChoice.Save
        };
        var host = new FakeWindowHost();
        using var coordinator = CreateCoordinator(viewModel, host, dialogs);

        await coordinator.ExitFromTrayAsync();

        Assert.Single(repository.Sessions);
        Assert.False(viewModel.Timer.IsTimerActive);
        Assert.Equal(1, host.ShowAndActivateCount);
        Assert.Equal(1, host.CloseCount);
        Assert.Equal(1, dialogs.ExitConfirmationCount);
    }

    [Fact]
    public async Task ExitFromTray_ActiveTimerDiscardChoice_DoesNotSaveSession()
    {
        var repository = new InMemorySessionRepository();
        var viewModel = CreateViewModel(repository);
        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        var dialogs = new FakeWindowDialogService
        {
            ExitChoice = ActiveTimerExitChoice.Discard
        };
        var host = new FakeWindowHost();
        using var coordinator = CreateCoordinator(viewModel, host, dialogs);

        await coordinator.ExitFromTrayAsync();

        Assert.Empty(repository.Sessions);
        Assert.False(viewModel.Timer.IsTimerActive);
        Assert.Equal(1, host.CloseCount);
    }

    [Fact]
    public async Task ExitFromTray_ActiveTimerCancelChoice_KeepsTimerAndWindowOpen()
    {
        var viewModel = CreateViewModel(new InMemorySessionRepository());
        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        var dialogs = new FakeWindowDialogService
        {
            ExitChoice = ActiveTimerExitChoice.Cancel
        };
        var host = new FakeWindowHost();
        using var coordinator = CreateCoordinator(viewModel, host, dialogs);

        await coordinator.ExitFromTrayAsync();

        Assert.True(viewModel.Timer.IsTimerActive);
        Assert.Equal(0, host.CloseCount);
        Assert.Equal(1, dialogs.ExitConfirmationCount);
    }

    [Fact]
    public async Task ExitFromTray_WhenSaveFails_ShowsErrorAndRestoresCommands()
    {
        var viewModel = CreateViewModel(new AlwaysFailSessionRepository());
        viewModel.TaskList.NewTaskTitle = "退出失败恢复";
        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        var dialogs = new FakeWindowDialogService
        {
            ExitChoice = ActiveTimerExitChoice.Save
        };
        var host = new FakeWindowHost();
        using var coordinator = CreateCoordinator(viewModel, host, dialogs);

        await coordinator.ExitFromTrayAsync();

        Assert.Equal(0, host.CloseCount);
        Assert.True(viewModel.Timer.IsTimerActive);
        Assert.True(viewModel.TaskList.AddTaskCommand.CanExecute(null));
        Assert.Contains("专注记录保存失败", dialogs.SaveFailureMessage);
    }

    [Fact]
    public async Task ExitFromTray_WhileSaveIsPending_IgnoresDuplicateRequest()
    {
        var repository = new DelayedSessionRepository();
        var viewModel = CreateViewModel(repository);
        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        var dialogs = new FakeWindowDialogService
        {
            ExitChoice = ActiveTimerExitChoice.Save
        };
        var host = new FakeWindowHost();
        using var coordinator = CreateCoordinator(viewModel, host, dialogs);

        var firstExit = coordinator.ExitFromTrayAsync();
        await repository.AddStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.ExitFromTrayAsync();

        Assert.Equal(1, dialogs.ExitConfirmationCount);
        Assert.Equal(0, host.CloseCount);

        repository.AllowAddToComplete.TrySetResult();
        await firstExit;

        Assert.Equal(1, host.CloseCount);
        Assert.Single(repository.Sessions);
    }

    private static WindowLifecycleCoordinator CreateCoordinator(
        MainViewModel viewModel,
        FakeWindowHost host,
        FakeWindowDialogService dialogs)
    {
        var coordinator = new WindowLifecycleCoordinator(
            host,
            dialogs,
            (_, _) => new TrayIconHandle(_ => { }, () => { }));
        coordinator.Attach(viewModel);
        return coordinator;
    }

    private static MainViewModel CreateViewModel(ISessionRepository sessionRepository) =>
        ViewModelTestFactory.Create(new PomodoroService(sessionRepository));

    private static Task ExecuteAsync(ICommand command, object? parameter = null) =>
        Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync(parameter);

    private sealed class FakeWindowHost : IMainWindowHost
    {
        internal int ShowAndActivateCount { get; private set; }
        internal int HideCount { get; private set; }
        internal int CloseCount { get; private set; }

        public void ShowAndActivate() => ShowAndActivateCount++;

        public void Hide() => HideCount++;

        public void Close() => CloseCount++;
    }

    private sealed class FakeWindowDialogService : IWindowDialogService
    {
        internal ActiveTimerExitChoice ExitChoice { get; init; } = ActiveTimerExitChoice.Cancel;
        internal int ExitConfirmationCount { get; private set; }
        internal string SaveFailureMessage { get; private set; } = string.Empty;

        public bool ConfirmDelete(string message) => true;

        public ActiveTimerExitChoice ConfirmActiveTimerExit()
        {
            ExitConfirmationCount++;
            return ExitChoice;
        }

        public void ShowSaveFailure(string message) => SaveFailureMessage = message;
    }

    private sealed class AlwaysFailSessionRepository : ISessionRepository
    {
        public Task<IReadOnlyList<PomodoroSession>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PomodoroSession>>([]);

        public Task AddAsync(PomodoroSession session) =>
            throw new InvalidOperationException("模拟记录保存失败");

        public Task DeleteAsync(Guid id) => Task.CompletedTask;
    }

    private sealed class DelayedSessionRepository : ISessionRepository
    {
        internal List<PomodoroSession> Sessions { get; } = [];
        internal TaskCompletionSource AddStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AllowAddToComplete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<PomodoroSession>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PomodoroSession>>(Sessions);

        public async Task AddAsync(PomodoroSession session)
        {
            AddStarted.TrySetResult();
            await AllowAddToComplete.Task;
            Sessions.Add(session);
        }

        public Task DeleteAsync(Guid id) => Task.CompletedTask;
    }
}
