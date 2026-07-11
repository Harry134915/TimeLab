using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.Infrastructure;

/// <summary>
/// 基于 JSON 文件的专注记录仓储实现，数据存储于 AppData/Local/TimeLab/sessions.json
/// </summary>
public class JsonSessionRepository : ISessionRepository
{
    private readonly JsonFileStore<PomodoroSession> _store;

    public JsonSessionRepository(string? dataDir = null)
    {
        _store = new JsonFileStore<PomodoroSession>("sessions.json", dataDir);
    }

    /// <summary>获取所有专注记录</summary>
    public async Task<IReadOnlyList<PomodoroSession>> GetAllAsync()
    {
        return await _store.ExecuteExclusiveAsync(async () =>
        {
            var sessions = await _store.LoadAsync();
            return (IReadOnlyList<PomodoroSession>)sessions;
        });
    }

    /// <summary>添加新的专注记录，写入 JSON 文件</summary>
    public async Task AddAsync(PomodoroSession session)
    {
        await _store.ExecuteExclusiveAsync(async () =>
        {
            var sessions = await _store.LoadAsync();
            sessions.Add(session);
            await _store.SaveAsync(sessions);
        });
    }

    /// <summary>按 ID 删除专注记录</summary>
    public async Task DeleteAsync(Guid id)
    {
        await _store.ExecuteExclusiveAsync(async () =>
        {
            var sessions = await _store.LoadAsync();
            sessions.RemoveAll(s => s.Id == id);
            await _store.SaveAsync(sessions);
        });
    }
}
