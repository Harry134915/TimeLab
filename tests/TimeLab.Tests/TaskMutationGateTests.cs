using System.Windows.Input;
using TimeLab.App;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

public class TaskMutationGateTests
{
    [Fact]
    public async Task CompleteTask_WhileSaving_DisablesConflictingTaskAndTimerActions()
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "需要串行保存的任务",
            CreatedAt = DateTime.Now,
            PlannedSeconds = 60
        };
        var repository = new BlockingTaskRepository(task);
        var viewModel = new MainViewModel(
            new TaskService(repository),
            new PomodoroService(new InMemorySessionRepository()),
            onConfirmDelete: _ => true,
            onTimerReached: () => { });
        await viewModel.LoadAsync();
        viewModel.SelectedTaskId = task.Id;
        viewModel.NewTaskTitle = "另一个任务";
        Assert.True(viewModel.AddTaskCommand.CanExecute(null));

        var completionTask = ExecuteAsync(viewModel.CompleteTaskCommand, task.Id);
        await repository.UpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.AddTaskCommand.CanExecute(null));
        Assert.False(viewModel.CompleteTaskCommand.CanExecute(task.Id));
        Assert.False(viewModel.DeleteTaskCommand.CanExecute(task.Id));
        Assert.False(viewModel.SelectTaskCommand.CanExecute(task.Id));
        Assert.False(viewModel.ClearSelectedTaskCommand.CanExecute(null));
        Assert.False(viewModel.StartTimerCommand.CanExecute(null));
        Assert.False(viewModel.StartPresetCommand.CanExecute(25));
        Assert.False(viewModel.StartCycleCommand.CanExecute(null));
        Assert.False(viewModel.ToggleTimerCommand.CanExecute(null));

        await ExecuteAsync(viewModel.DeleteTaskCommand, task.Id);
        Assert.Equal(0, repository.DeleteCalls);

        repository.AllowUpdate.TrySetResult();
        await completionTask;

        Assert.True(Assert.Single(viewModel.Tasks).IsCompleted);
        Assert.Null(viewModel.SelectedTaskId);
        Assert.True(viewModel.DeleteTaskCommand.CanExecute(task.Id));
    }

    [Fact]
    public async Task PrepareForExitAsync_WaitsForInFlightTaskMutation()
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "退出前等待保存",
            CreatedAt = DateTime.Now,
            PlannedSeconds = 60
        };
        var repository = new BlockingTaskRepository(task);
        var viewModel = new MainViewModel(
            new TaskService(repository),
            new PomodoroService(new InMemorySessionRepository()),
            onTimerReached: () => { });
        await viewModel.LoadAsync();

        var completionTask = ExecuteAsync(viewModel.CompleteTaskCommand, task.Id);
        await repository.UpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exitTask = viewModel.PrepareForExitAsync(saveActiveTimer: false);
        Assert.False(exitTask.IsCompleted);
        Assert.False(viewModel.AddTaskCommand.CanExecute(null));
        Assert.False(viewModel.StartTimerCommand.CanExecute(null));

        repository.AllowUpdate.TrySetResult();
        await Task.WhenAll(completionTask, exitTask);

        Assert.True(Assert.Single(viewModel.Tasks).IsCompleted);
    }

    [Fact]
    public async Task PrepareForExitAsync_WaitsForInFlightSessionDelete()
    {
        var session = new PomodoroSession
        {
            Id = Guid.NewGuid(),
            StartTime = DateTime.Now.AddMinutes(-1),
            Duration = TimeSpan.FromMinutes(1),
            Mode = FocusMode.Focus
        };
        var repository = new BlockingSessionRepository(session);
        var viewModel = new MainViewModel(
            new TaskService(new InMemoryTaskRepository()),
            new PomodoroService(repository),
            onConfirmDelete: _ => true,
            onTimerReached: () => { });
        await viewModel.LoadAsync();

        var deleteTask = ExecuteAsync(viewModel.DeleteSessionCommand, session.Id);
        await repository.DeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exitTask = viewModel.PrepareForExitAsync(saveActiveTimer: false);
        Assert.False(exitTask.IsCompleted);
        Assert.False(viewModel.DeleteSessionCommand.CanExecute(session.Id));
        Assert.False(viewModel.CanEditTimerSetup);

        repository.AllowDelete.TrySetResult();
        await Task.WhenAll(deleteTask, exitTask);

        Assert.Empty(viewModel.Sessions);
        Assert.Empty(repository.Sessions);
    }

    private static Task ExecuteAsync(ICommand command, object? parameter = null) =>
        Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync(parameter);

    private sealed class BlockingTaskRepository(TaskItem task) : ITaskRepository
    {
        private readonly List<TaskItem> _items = [task];

        public TaskCompletionSource UpdateStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowUpdate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DeleteCalls { get; private set; }

        public Task<IReadOnlyList<TaskItem>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<TaskItem>>(_items);

        public Task AddAsync(TaskItem item)
        {
            _items.Add(item);
            return Task.CompletedTask;
        }

        public async Task UpdateAsync(TaskItem item)
        {
            UpdateStarted.TrySetResult();
            await AllowUpdate.Task;
            var index = _items.FindIndex(current => current.Id == item.Id);
            if (index >= 0)
                _items[index] = item;
        }

        public Task DeleteAsync(Guid id)
        {
            DeleteCalls++;
            _items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSessionRepository(PomodoroSession session) : ISessionRepository
    {
        public List<PomodoroSession> Sessions { get; } = [session];
        public TaskCompletionSource DeleteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowDelete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<PomodoroSession>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PomodoroSession>>(Sessions);

        public Task AddAsync(PomodoroSession item)
        {
            Sessions.Add(item);
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(Guid id)
        {
            DeleteStarted.TrySetResult();
            await AllowDelete.Task;
            Sessions.RemoveAll(item => item.Id == id);
        }
    }
}
