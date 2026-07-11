using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

public class TaskServiceTests
{
    [Fact]
    public async Task CreateAsync_AddsTaskWithTitleAndPlannedSeconds()
    {
        var repository = new InMemoryTaskRepository();
        var service = new TaskService(repository);

        var item = await service.CreateAsync("Write docs", plannedSeconds: 1500);

        Assert.Single(repository.Items);
        Assert.Equal(item.Id, repository.Items[0].Id);
        Assert.Equal("Write docs", item.Title);
        Assert.Equal(1500, item.PlannedSeconds);
        Assert.False(item.IsCompleted);
        Assert.NotEqual(Guid.Empty, item.Id);
    }

    [Fact]
    public async Task CompleteAsync_MarksExistingTaskCompleted()
    {
        var repository = new InMemoryTaskRepository();
        var service = new TaskService(repository);
        var item = await service.CreateAsync("Focus");

        var completedItem = await service.CompleteAsync(item.Id);

        Assert.True(item.IsCompleted);
        Assert.NotNull(item.CompletedAt);
        Assert.Same(item, completedItem);
    }

    [Fact]
    public async Task CompleteAsync_WhenPersistenceFails_RestoresOriginalTaskState()
    {
        var item = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "保存失败后保持未完成",
            CreatedAt = DateTime.Now
        };
        var service = new TaskService(new FailingUpdateTaskRepository(item));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(item.Id));

        Assert.False(item.IsCompleted);
        Assert.Null(item.CompletedAt);
    }

    [Fact]
    public async Task DeleteAsync_DeletesTaskById()
    {
        var repository = new InMemoryTaskRepository();
        var service = new TaskService(repository);
        var item = await service.CreateAsync("Remove me");

        await service.DeleteAsync(item.Id);

        Assert.Empty(repository.Items);
        Assert.Contains(item.Id, repository.DeletedIds);
    }

    private sealed class FailingUpdateTaskRepository(TaskItem item) : ITaskRepository
    {
        public Task<IReadOnlyList<TaskItem>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<TaskItem>>([item]);

        public Task AddAsync(TaskItem newItem) => Task.CompletedTask;

        public Task UpdateAsync(TaskItem updatedItem) =>
            Task.FromException(new InvalidOperationException("无法保存"));

        public Task DeleteAsync(Guid id) => Task.CompletedTask;
    }
}
