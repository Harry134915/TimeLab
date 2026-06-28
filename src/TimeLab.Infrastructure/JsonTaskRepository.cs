using System.Text.Json;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Infrastructure;

/// <summary>
/// 基于 JSON 文件的任务仓储实现，数据存储于 AppData/Local/TimeLab/tasks.json
/// </summary>
public class JsonTaskRepository : ITaskRepository
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TimeLab");

    private static readonly string FilePath = Path.Combine(DataDir, "tasks.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    /// <summary>获取所有任务</summary>
    public async Task<IReadOnlyList<TaskItem>> GetAllAsync()
    {
        var items = await LoadAsync();
        return items;
    }

    /// <summary>添加新任务，写入 JSON 文件</summary>
    public async Task AddAsync(TaskItem item)
    {
        var items = await LoadAsync();
        items.Add(item);
        await SaveAsync(items);
    }

    /// <summary>更新任务，按 ID 匹配并替换</summary>
    public async Task UpdateAsync(TaskItem item)
    {
        var items = await LoadAsync();
        var index = items.FindIndex(i => i.Id == item.Id);
        if (index >= 0)
            items[index] = item;
        await SaveAsync(items);
    }

    /// <summary>按 ID 删除任务</summary>
    public async Task DeleteAsync(Guid id)
    {
        var items = await LoadAsync();
        items.RemoveAll(i => i.Id == id);
        await SaveAsync(items);
    }

    /// <summary>从 JSON 文件加载任务列表，文件不存在时返回空列表，文件损坏时备份并返回空列表</summary>
    private static async Task<List<TaskItem>> LoadAsync()
    {
        if (!Directory.Exists(DataDir))
            Directory.CreateDirectory(DataDir);

        if (!File.Exists(FilePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<List<TaskItem>>(stream) ?? [];
        }
        catch (JsonException)
        {
            var backup = FilePath + ".bak";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(FilePath, backup);
            return [];
        }
    }

    /// <summary>将任务列表写入 JSON 文件</summary>
    private static async Task SaveAsync(List<TaskItem> items)
    {
        if (!Directory.Exists(DataDir))
            Directory.CreateDirectory(DataDir);

        var tempPath = FilePath + ".tmp";
        var backupPath = FilePath + ".bak";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, items, Options);
        }

        if (File.Exists(FilePath))
            File.Replace(tempPath, FilePath, backupPath, ignoreMetadataErrors: true);
        else
            File.Move(tempPath, FilePath);
    }
}
