using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Tests;

/// <summary>
/// TaskService 测试使用的内存仓储替身，避免测试读写真实 JSON 文件。
/// </summary>
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

/// <summary>
/// PomodoroService 测试使用的内存仓储替身，用于捕获生成的 Session。
/// </summary>
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
