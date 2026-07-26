using System.Windows.Input;
using TimeLab.App;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

public class TimerPersistenceRecoveryTests
{
    [Fact]
    public async Task StopAsync_WhenPersistenceFails_PreservesRunningSessionForRetry()
    {
        var repository = new FailOnceSessionRepository();
        var service = new PomodoroService(repository);
        var taskId = Guid.NewGuid();

        await service.StartAsync(taskId, targetSeconds: 60);
        service.CurrentState.StartTime = DateTime.Now.AddSeconds(-5);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync());

        Assert.Equal(TimerStatus.Running, service.CurrentState.Status);
        Assert.NotNull(service.CurrentState.StartTime);
        Assert.Equal(60, service.TargetSeconds);
        Assert.Empty(repository.Sessions);

        var session = await service.StopAsync();

        Assert.NotNull(session);
        Assert.Equal(taskId, session.TaskId);
        Assert.Single(repository.Sessions);
        Assert.Equal(TimerStatus.Stopped, service.CurrentState.Status);
        Assert.Equal(0, service.TargetSeconds);
    }

    [Fact]
    public async Task TargetReached_WhenPersistenceFails_PausesAndCompletesWhenStopRetries()
    {
        var repository = new FailOnceSessionRepository();
        var service = new PomodoroService(repository);
        var viewModel = CreateViewModel(service);

        await ExecuteAsync(viewModel.Timer.StartPresetCommand, 1);
        service.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.Timer.UpdateTimerDisplayAsync());

        Assert.Equal(TimerStatus.Paused, service.CurrentState.Status);
        Assert.False(viewModel.Timer.IsTargetReached);
        Assert.Empty(repository.Sessions);
        Assert.True(viewModel.Timer.StopTimerCommand.CanExecute(null));
        Assert.True(viewModel.Timer.StartTimerCommand.CanExecute(null));

        await ExecuteAsync(viewModel.Timer.StopTimerCommand);

        Assert.Single(repository.Sessions);
        Assert.Single(viewModel.SessionLog.Sessions);
        Assert.Equal(TimerStatus.Stopped, service.CurrentState.Status);
        Assert.True(viewModel.Timer.IsTargetReached);
        Assert.Equal(FocusMode.ShortBreak, service.CurrentMode);
    }

    [Fact]
    public async Task TargetReached_WhenPersistenceKeepsFailing_DoesNotRetryEveryTick()
    {
        var repository = new AlwaysFailSessionRepository();
        var service = new PomodoroService(repository);
        var viewModel = CreateViewModel(service);

        await ExecuteAsync(viewModel.Timer.StartPresetCommand, 1);
        service.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.Timer.UpdateTimerDisplayAsync());
        await viewModel.Timer.UpdateTimerDisplayAsync();

        Assert.Equal(1, repository.AddAttempts);
        Assert.Equal(TimerStatus.Paused, service.CurrentState.Status);
        Assert.True(viewModel.Timer.StopTimerCommand.CanExecute(null));
    }

    [Fact]
    public async Task CycleTargetReached_WhenSaveFails_StopRetrySavesOnceAndStartsBreak()
    {
        var repository = new FailOnceSessionRepository();
        var service = new PomodoroService(repository);
        var viewModel = CreateViewModel(service);
        viewModel.Timer.CycleFocusMinutes = 1;
        viewModel.Timer.CycleBreakMinutes = 1;
        viewModel.Timer.CycleTotalRoundsText = "2";

        await ExecuteAsync(viewModel.Timer.StartCycleCommand);
        service.CurrentState.StartTime = DateTime.Now.AddSeconds(-61);

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.Timer.UpdateTimerDisplayAsync());

        Assert.Equal(TimerStatus.Paused, service.CurrentState.Status);
        Assert.Equal(FocusMode.Focus, service.CurrentMode);
        Assert.True(service.IsCycleActive);
        Assert.Empty(repository.Sessions);

        await ExecuteAsync(viewModel.Timer.StopTimerCommand);

        var session = Assert.Single(repository.Sessions);
        Assert.Same(session, Assert.Single(viewModel.SessionLog.Sessions));
        Assert.Equal(FocusMode.Focus, session.Mode);
        Assert.Equal(TimerStatus.Running, service.CurrentState.Status);
        Assert.Equal(FocusMode.ShortBreak, service.CurrentMode);
        Assert.Equal(60, service.TargetSeconds);
        Assert.Equal(1, service.CurrentRound);
        Assert.True(service.IsCycleActive);
    }

    [Fact]
    public async Task StopCommand_WhileSaving_DisablesEveryConflictingTimerAction()
    {
        var repository = new BlockingSessionRepository();
        var service = new PomodoroService(repository);
        var viewModel = CreateViewModel(service);

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);
        var stopTask = ExecuteAsync(viewModel.Timer.StopTimerCommand);
        await repository.AddStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.Timer.StartTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.PauseTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.StopTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.ResetTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.StartPresetCommand.CanExecute(25));
        Assert.False(viewModel.Timer.StartCycleCommand.CanExecute(null));
        Assert.False(viewModel.Timer.ToggleTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.StopOrResetCommand.CanExecute(null));
        Assert.False(viewModel.SessionLog.DeleteSessionCommand.CanExecute(Guid.NewGuid()));
        Assert.False(viewModel.Timer.CanEditTimerSetup);

        repository.AllowAdd.TrySetResult();
        await stopTask;

        Assert.Single(repository.Sessions);
        Assert.Equal(TimerStatus.Stopped, service.CurrentState.Status);
        Assert.True(viewModel.Timer.ResetTimerCommand.CanExecute(null));
    }

    private static MainViewModel CreateViewModel(PomodoroService service)
    {
        return ViewModelTestFactory.Create(service);
    }

    private static Task ExecuteAsync(ICommand command, object? parameter = null)
    {
        return Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync(parameter);
    }

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
                throw new InvalidOperationException("无法保存专注记录");
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

    private sealed class BlockingSessionRepository : ISessionRepository
    {
        public TaskCompletionSource AddStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowAdd { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<PomodoroSession> Sessions { get; } = [];

        public Task<IReadOnlyList<PomodoroSession>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PomodoroSession>>(Sessions);

        public async Task AddAsync(PomodoroSession session)
        {
            AddStarted.TrySetResult();
            await AllowAdd.Task;
            Sessions.Add(session);
        }

        public Task DeleteAsync(Guid id)
        {
            Sessions.RemoveAll(session => session.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailSessionRepository : ISessionRepository
    {
        public int AddAttempts { get; private set; }

        public Task<IReadOnlyList<PomodoroSession>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PomodoroSession>>([]);

        public Task AddAsync(PomodoroSession session)
        {
            AddAttempts++;
            throw new InvalidOperationException("无法保存专注记录");
        }

        public Task DeleteAsync(Guid id) => Task.CompletedTask;
    }
}
