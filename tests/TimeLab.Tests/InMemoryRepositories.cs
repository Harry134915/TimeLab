using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

internal sealed class InMemoryTaskRepository : ITaskRepository
{
    public List<TaskItem> Items { get; } = [];
    public List<Guid> DeletedIds { get; } = [];

    public Task<IReadOnlyList<TaskItem>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyList<TaskItem>>(Items);
    }

    public Task AddAsync(TaskItem item)
    {
        Items.Add(item);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TaskItem item)
    {
        var index = Items.FindIndex(i => i.Id == item.Id);
        if (index >= 0)
            Items[index] = item;

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        DeletedIds.Add(id);
        Items.RemoveAll(i => i.Id == id);
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySessionRepository : ISessionRepository
{
    public List<PomodoroSession> Sessions { get; } = [];

    public Task<IReadOnlyList<PomodoroSession>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyList<PomodoroSession>>(Sessions);
    }

    public Task AddAsync(PomodoroSession session)
    {
        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        Sessions.RemoveAll(s => s.Id == id);
        return Task.CompletedTask;
    }
}
