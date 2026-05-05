using TimeLab.Core;

namespace TimeLab.Application;

/// <summary>
/// 专注记录仓储接口，定义会话数据的持久化操作
/// </summary>
public interface ISessionRepository
{
    /// <summary>获取所有专注记录</summary>
    Task<IReadOnlyList<PomodoroSession>> GetAllAsync();
    /// <summary>添加新的专注记录</summary>
    Task AddAsync(PomodoroSession session);
    /// <summary>按 ID 删除专注记录</summary>
    Task DeleteAsync(Guid id);
}
