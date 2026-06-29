using TimeLab.Application;

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

        await service.CompleteAsync(item.Id);

        Assert.True(item.IsCompleted);
        Assert.NotNull(item.CompletedAt);
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
}
