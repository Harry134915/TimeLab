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
        var viewModel = ViewModelTestFactory.Create(
            new PomodoroService(new InMemorySessionRepository()),
            new TaskService(repository),
            onConfirmDelete: _ => true);
        await viewModel.LoadAsync();
        viewModel.TaskList.SelectedTaskId = task.Id;
        viewModel.TaskList.NewTaskTitle = "另一个任务";
        Assert.True(viewModel.TaskList.AddTaskCommand.CanExecute(null));

        var completionTask = ExecuteAsync(viewModel.TaskList.CompleteTaskCommand, task.Id);
        await repository.UpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.TaskList.AddTaskCommand.CanExecute(null));
        Assert.False(viewModel.TaskList.CompleteTaskCommand.CanExecute(task.Id));
        Assert.False(viewModel.TaskList.DeleteTaskCommand.CanExecute(task.Id));
        Assert.False(viewModel.TaskList.SelectTaskCommand.CanExecute(task.Id));
        Assert.False(viewModel.TaskList.ClearSelectedTaskCommand.CanExecute(null));
        Assert.False(viewModel.Timer.StartTimerCommand.CanExecute(null));
        Assert.False(viewModel.Timer.StartPresetCommand.CanExecute(25));
        Assert.False(viewModel.Timer.StartCycleCommand.CanExecute(null));
        Assert.False(viewModel.Timer.ToggleTimerCommand.CanExecute(null));

        await ExecuteAsync(viewModel.TaskList.DeleteTaskCommand, task.Id);
        Assert.Equal(0, repository.DeleteCalls);

        repository.AllowUpdate.TrySetResult();
        await completionTask;

        Assert.True(Assert.Single(viewModel.TaskList.Tasks).IsCompleted);
        Assert.Null(viewModel.TaskList.SelectedTaskId);
        Assert.True(viewModel.TaskList.DeleteTaskCommand.CanExecute(task.Id));
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
        var viewModel = ViewModelTestFactory.Create(
            new PomodoroService(new InMemorySessionRepository()),
            new TaskService(repository));
        await viewModel.LoadAsync();

        var completionTask = ExecuteAsync(viewModel.TaskList.CompleteTaskCommand, task.Id);
        await repository.UpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exitTask = viewModel.PrepareForExitAsync(saveActiveTimer: false);
        Assert.False(exitTask.IsCompleted);
        Assert.False(viewModel.TaskList.AddTaskCommand.CanExecute(null));
        Assert.False(viewModel.Timer.StartTimerCommand.CanExecute(null));

        repository.AllowUpdate.TrySetResult();
        await Task.WhenAll(completionTask, exitTask);

        Assert.True(Assert.Single(viewModel.TaskList.Tasks).IsCompleted);
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
        var viewModel = ViewModelTestFactory.Create(
            new PomodoroService(repository),
            new TaskService(new InMemoryTaskRepository()),
            onConfirmDelete: _ => true);
        await viewModel.LoadAsync();

        var deleteTask = ExecuteAsync(viewModel.SessionLog.DeleteSessionCommand, session.Id);
        await repository.DeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exitTask = viewModel.PrepareForExitAsync(saveActiveTimer: false);
        Assert.False(exitTask.IsCompleted);
        Assert.False(viewModel.SessionLog.DeleteSessionCommand.CanExecute(session.Id));
        Assert.False(viewModel.Timer.CanEditTimerSetup);

        repository.AllowDelete.TrySetResult();
        await Task.WhenAll(deleteTask, exitTask);

        Assert.Empty(viewModel.SessionLog.Sessions);
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
