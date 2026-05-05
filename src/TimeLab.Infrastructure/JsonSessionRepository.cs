using System.Text.Json;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Infrastructure;

/// <summary>
/// 基于 JSON 文件的专注记录仓储实现，数据存储于 AppData/Local/TimeLab/sessions.json
/// </summary>
public class JsonSessionRepository : ISessionRepository
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TimeLab");

    private static readonly string FilePath = Path.Combine(DataDir, "sessions.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    /// <summary>获取所有专注记录</summary>
    public async Task<IReadOnlyList<PomodoroSession>> GetAllAsync()
    {
        var sessions = await LoadAsync();
        return sessions;
    }

    /// <summary>添加新的专注记录，写入 JSON 文件</summary>
    public async Task AddAsync(PomodoroSession session)
    {
        var sessions = await LoadAsync();
        sessions.Add(session);
        await SaveAsync(sessions);
    }

    /// <summary>按 ID 删除专注记录</summary>
    public async Task DeleteAsync(Guid id)
    {
        var sessions = await LoadAsync();
        sessions.RemoveAll(s => s.Id == id);
        await SaveAsync(sessions);
    }

    /// <summary>从 JSON 文件加载专注记录列表，文件不存在时返回空列表</summary>
    private static async Task<List<PomodoroSession>> LoadAsync()
    {
        if (!Directory.Exists(DataDir))
            Directory.CreateDirectory(DataDir);

        if (!File.Exists(FilePath))
            return [];

        await using var stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync<List<PomodoroSession>>(stream) ?? [];
    }

    /// <summary>将专注记录列表写入 JSON 文件</summary>
    private static async Task SaveAsync(List<PomodoroSession> sessions)
    {
        if (!Directory.Exists(DataDir))
            Directory.CreateDirectory(DataDir);

        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, sessions, Options);
    }
}
