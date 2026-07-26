using System.Windows.Input;
using TimeLab.App;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

public class ViewModelCoordinationTests
{
    [Fact]
    public async Task LoadAsync_WhenTaskLoadFails_StillLoadsSessionsAndReportsError()
    {
        var sessionRepository = new InMemorySessionRepository();
        sessionRepository.Sessions.Add(new PomodoroSession
        {
            Id = Guid.NewGuid(),
            StartTime = DateTime.Now.AddMinutes(-25),
            EndTime = DateTime.Now,
            Duration = TimeSpan.FromMinutes(25),
            Mode = FocusMode.Focus
        });
        var viewModel = ViewModelTestFactory.Create(
            new PomodoroService(sessionRepository),
            new TaskService(new FailingLoadTaskRepository()));

        await viewModel.LoadAsync();

        Assert.Single(viewModel.SessionLog.Sessions);
        Assert.True(viewModel.IsErrorNotification);
        Assert.Contains("任务加载失败", viewModel.NotificationMessage);
    }

    [Fact]
    public async Task ActiveTimer_DisablesConflictingTaskCommandsThroughSharedState()
    {
        var viewModel = ViewModelTestFactory.Create(
            new PomodoroService(new InMemorySessionRepository()),
            onConfirmDelete: _ => true);
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "共享状态测试",
            PlannedSeconds = 60
        };
        viewModel.TaskList.Tasks.Add(task);
        viewModel.TaskList.SelectedTaskId = task.Id;

        await ExecuteAsync(viewModel.Timer.StartTimerCommand);

        Assert.False(viewModel.TaskList.CompleteTaskCommand.CanExecute(task.Id));
        Assert.False(viewModel.TaskList.DeleteTaskCommand.CanExecute(task.Id));
        Assert.False(viewModel.TaskList.SelectTaskCommand.CanExecute(task.Id));
        Assert.False(viewModel.TaskList.ClearSelectedTaskCommand.CanExecute(null));
    }

    [Fact]
    public async Task PrepareForExitAsync_WhenSaveFails_RestoresWorkspaceCommands()
    {
        var viewModel = ViewModelTestFactory.Create(
            new PomodoroService(new AlwaysFailSessionRepository()));
        viewModel.TaskList.NewTaskTitle = "退出恢复测试";
        await ExecuteAsync(viewModel.Timer.StartTimerCommand);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.PrepareForExitAsync(saveActiveTimer: true));

        Assert.True(viewModel.TaskList.AddTaskCommand.CanExecute(null));
        Assert.True(viewModel.Timer.StopTimerCommand.CanExecute(null));
        Assert.True(viewModel.SessionLog.DeleteSessionCommand.CanExecute(Guid.NewGuid()));
    }

    private static Task ExecuteAsync(ICommand command, object? parameter = null) =>
        Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync(parameter);

    private sealed class FailingLoadTaskRepository : ITaskRepository
    {
        public Task<IReadOnlyList<TaskItem>> GetAllAsync() =>
            throw new InvalidOperationException("模拟任务读取失败");

        public Task AddAsync(TaskItem item) => Task.CompletedTask;

        public Task UpdateAsync(TaskItem item) => Task.CompletedTask;

        public Task DeleteAsync(Guid id) => Task.CompletedTask;
    }

    private sealed class AlwaysFailSessionRepository : ISessionRepository
    {
        public Task<IReadOnlyList<PomodoroSession>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PomodoroSession>>([]);

        public Task AddAsync(PomodoroSession session) =>
            throw new InvalidOperationException("模拟记录保存失败");

        public Task DeleteAsync(Guid id) => Task.CompletedTask;
    }
}
