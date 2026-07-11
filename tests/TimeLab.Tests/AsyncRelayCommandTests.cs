using System.Windows.Input;
using TimeLab.App;

namespace TimeLab.Tests;

public class AsyncRelayCommandTests
{
    [Fact]
    public async Task ExecuteAsync_PreventsConcurrentExecution()
    {
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executionCount = 0;
        var command = new AsyncRelayCommand(
            async _ =>
            {
                executionCount++;
                started.SetResult(true);
                await release.Task;
            },
            _ => { });

        var firstExecution = command.ExecuteAsync(null);
        await started.Task;

        await command.ExecuteAsync(null);

        Assert.True(command.IsExecuting);
        Assert.False(command.CanExecute(null));
        Assert.Equal(1, executionCount);

        release.SetResult(true);
        await firstExecution;

        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesExceptionToAwaitingCaller()
    {
        var command = new AsyncRelayCommand(
            _ => Task.FromException(new InvalidOperationException("boom")),
            _ => { });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => command.ExecuteAsync(null));

        Assert.Equal("boom", exception.Message);
        Assert.False(command.IsExecuting);
    }

    [Fact]
    public async Task Execute_RoutesExceptionToHandler()
    {
        var handledException = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ICommand command = new AsyncRelayCommand(
            _ => Task.FromException(new InvalidOperationException("boom")),
            exception => handledException.SetResult(exception));

        command.Execute(null);
        var exception = await handledException.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowingCanExecuteSubscriber_DoesNotLockCommand()
    {
        var executionCount = 0;
        var command = new AsyncRelayCommand(
            _ =>
            {
                executionCount++;
                return Task.CompletedTask;
            },
            _ => { });
        command.CanExecuteChanged += (_, _) => throw new InvalidOperationException("subscriber failed");

        await command.ExecuteAsync(null);

        Assert.Equal(1, executionCount);
        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute(null));
    }
}
