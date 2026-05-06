using TimeLab.Core;

namespace TimeLab.Application;

/// <summary>
/// 任务应用服务，处理任务的创建、完成、删除等业务逻辑
/// </summary>
public class TaskService
{
    private readonly ITaskRepository _repository;

    public TaskService(ITaskRepository repository)
    {
        _repository = repository;
    }

    /// <summary>创建新任务</summary>
    public async Task<TaskItem> CreateAsync(string title, int plannedSeconds = 0)
    {
        var item = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAt = DateTime.Now,
            PlannedSeconds = plannedSeconds
        };

        await _repository.AddAsync(item);
        return item;
    }

    /// <summary>获取所有任务</summary>
    public Task<IReadOnlyList<TaskItem>> GetAllAsync()
    {
        return _repository.GetAllAsync();
    }

    /// <summary>将指定任务标记为完成</summary>
    public async Task CompleteAsync(Guid id)
    {
        var items = await _repository.GetAllAsync();
        var item = items.FirstOrDefault(i => i.Id == id);

        if (item is null)
            return;

        item.IsCompleted = true;
        item.CompletedAt = DateTime.Now;

        await _repository.UpdateAsync(item);
    }

    /// <summary>按 ID 删除任务</summary>
    public Task DeleteAsync(Guid id)
    {
        return _repository.DeleteAsync(id);
    }
}
