using TimeLab.Infrastructure;

namespace TimeLab.Tests;

public class JsonTaskRepositoryTests : IDisposable
{
    private readonly string _dataDir;

    public JsonTaskRepositoryTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "TimeLab.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyListWhenFileDoesNotExist()
    {
        var repository = new JsonTaskRepository(_dataDir);

        var items = await repository.GetAllAsync();

        Assert.Empty(items);
    }

    [Fact]
    public async Task AddAsync_SavesTaskThatCanBeReadBack()
    {
        var repository = new JsonTaskRepository(_dataDir);
        var item = await new TimeLab.Application.TaskService(repository)
            .CreateAsync("Persist me", plannedSeconds: 300);

        var reloadedRepository = new JsonTaskRepository(_dataDir);
        var items = await reloadedRepository.GetAllAsync();

        var saved = Assert.Single(items);
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal("Persist me", saved.Title);
        Assert.Equal(300, saved.PlannedSeconds);
    }

    [Fact]
    public async Task AddAsync_FromDifferentRepositoryInstances_PreservesEveryTask()
    {
        const int taskCount = 24;
        var repositories = Enumerable.Range(0, taskCount)
            .Select(_ => new JsonTaskRepository(_dataDir))
            .ToArray();
        var tasks = Enumerable.Range(0, taskCount)
            .Select(index => new TimeLab.Core.TaskItem
            {
                Id = Guid.NewGuid(),
                Title = $"Concurrent task {index}",
                CreatedAt = DateTime.UtcNow,
                PlannedSeconds = 300
            })
            .ToArray();

        await Task.WhenAll(tasks.Select((item, index) => repositories[index].AddAsync(item)));

        var saved = await new JsonTaskRepository(_dataDir).GetAllAsync();
        Assert.Equal(taskCount, saved.Count);
        Assert.Equal(
            tasks.Select(item => item.Id).OrderBy(id => id),
            saved.Select(item => item.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task GetAllAsync_BacksUpCorruptedJsonAndReturnsEmptyList()
    {
        Directory.CreateDirectory(_dataDir);
        var filePath = Path.Combine(_dataDir, "tasks.json");
        await File.WriteAllTextAsync(filePath, "{ invalid json");

        var repository = new JsonTaskRepository(_dataDir);
        var items = await repository.GetAllAsync();

        Assert.Empty(items);
        Assert.False(File.Exists(filePath));
        Assert.True(File.Exists(filePath + ".bak"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
            Directory.Delete(_dataDir, recursive: true);
    }
}
