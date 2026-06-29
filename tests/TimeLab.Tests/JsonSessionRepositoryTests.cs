using TimeLab.Core;
using TimeLab.Infrastructure;

namespace TimeLab.Tests;

public class JsonSessionRepositoryTests : IDisposable
{
    private readonly string _dataDir;

    public JsonSessionRepositoryTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "TimeLab.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyListWhenFileDoesNotExist()
    {
        var repository = new JsonSessionRepository(_dataDir);

        var sessions = await repository.GetAllAsync();

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task AddAsync_SavesSessionThatCanBeReadBack()
    {
        var repository = new JsonSessionRepository(_dataDir);
        var session = CreateSession();

        await repository.AddAsync(session);

        var reloadedRepository = new JsonSessionRepository(_dataDir);
        var sessions = await reloadedRepository.GetAllAsync();

        var saved = Assert.Single(sessions);
        Assert.Equal(session.Id, saved.Id);
        Assert.Equal(session.TaskId, saved.TaskId);
        Assert.Equal(session.Duration, saved.Duration);
        Assert.Equal(session.Mode, saved.Mode);
        Assert.Equal(session.Note, saved.Note);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSessionFromStorage()
    {
        var repository = new JsonSessionRepository(_dataDir);
        var session = CreateSession();

        await repository.AddAsync(session);
        await repository.DeleteAsync(session.Id);

        var reloadedRepository = new JsonSessionRepository(_dataDir);
        var sessions = await reloadedRepository.GetAllAsync();

        Assert.Empty(sessions);
    }

    private static PomodoroSession CreateSession()
    {
        var startTime = new DateTime(2026, 6, 29, 9, 0, 0);

        return new PomodoroSession
        {
            Id = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            StartTime = startTime,
            EndTime = startTime.AddMinutes(25),
            Duration = TimeSpan.FromMinutes(25),
            Note = "Session note",
            Mode = FocusMode.Focus
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
            Directory.Delete(_dataDir, recursive: true);
    }
}
