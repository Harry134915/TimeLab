using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

public class PomodoroServiceTests
{
    [Fact]
    public async Task StartAsync_SetsTimerRunning()
    {
        var service = new PomodoroService(new InMemorySessionRepository());

        await service.StartAsync(targetSeconds: 60);

        Assert.Equal(TimerStatus.Running, service.CurrentState.Status);
        Assert.NotNull(service.CurrentState.StartTime);
        Assert.Equal(60, service.TargetSeconds);
    }

    [Fact]
    public async Task PauseAsync_AccumulatesElapsedTime()
    {
        var service = new PomodoroService(new InMemorySessionRepository());

        await service.StartAsync();
        await Task.Delay(20);
        await service.PauseAsync();

        Assert.Equal(TimerStatus.Paused, service.CurrentState.Status);
        Assert.True(service.CurrentState.ElapsedTime > TimeSpan.Zero);
        Assert.Null(service.CurrentState.StartTime);
    }

    [Fact]
    public async Task ResumeAsync_DoesNotReplaceSessionStartTime()
    {
        var repository = new InMemorySessionRepository();
        var service = new PomodoroService(repository);

        await service.StartAsync();
        await Task.Delay(20);
        await service.PauseAsync();
        await Task.Delay(20);
        var beforeResume = DateTime.Now;
        await service.ResumeAsync();
        await Task.Delay(20);

        var session = await service.StopAsync();

        Assert.NotNull(session);
        Assert.True(session.StartTime < beforeResume);
        Assert.True(session.Duration > TimeSpan.Zero);
        Assert.Same(session, repository.Sessions.Single());
    }

    [Fact]
    public async Task StopAsync_AddsSessionToRepository()
    {
        var repository = new InMemorySessionRepository();
        var service = new PomodoroService(repository);
        var taskId = Guid.NewGuid();

        await service.StartAsync(taskId);
        await Task.Delay(20);

        var session = await service.StopAsync();

        Assert.NotNull(session);
        Assert.Single(repository.Sessions);
        Assert.Equal(taskId, session.TaskId);
        Assert.Equal(TimerStatus.Stopped, service.CurrentState.Status);
        Assert.True(session.EndTime >= session.StartTime);
    }
}
