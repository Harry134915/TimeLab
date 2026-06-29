using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Infrastructure;

/// <summary>
/// 基于 JSON 文件的任务仓储实现，数据存储于 AppData/Local/TimeLab/tasks.json
/// </summary>
public class JsonTaskRepository : ITaskRepository
{
    private readonly JsonFileStore<TaskItem> _store;

    public JsonTaskRepository(string? dataDir = null)
    {
        _store = new JsonFileStore<TaskItem>("tasks.json", dataDir);
    }

    /// <summary>获取所有任务</summary>
    public async Task<IReadOnlyList<TaskItem>> GetAllAsync()
    {
        var items = await _store.LoadAsync();
        return items;
    }

    /// <summary>添加新任务，写入 JSON 文件</summary>
    public async Task AddAsync(TaskItem item)
    {
        var items = await _store.LoadAsync();
        items.Add(item);
        await _store.SaveAsync(items);
    }

    /// <summary>更新任务，按 ID 匹配并替换</summary>
    public async Task UpdateAsync(TaskItem item)
    {
        var items = await _store.LoadAsync();
        var index = items.FindIndex(i => i.Id == item.Id);
        if (index >= 0)
            items[index] = item;
        await _store.SaveAsync(items);
    }

    /// <summary>按 ID 删除任务</summary>
    public async Task DeleteAsync(Guid id)
    {
        var items = await _store.LoadAsync();
        items.RemoveAll(i => i.Id == id);
        await _store.SaveAsync(items);
    }
}
