using TimeLab.Core;

namespace TimeLab.Application;

/// <summary>
/// 任务仓储接口，定义任务数据的持久化操作
/// </summary>
public interface ITaskRepository
{
    /// <summary>获取所有任务</summary>
    Task<IReadOnlyList<TaskItem>> GetAllAsync();
    /// <summary>添加新任务</summary>
    Task AddAsync(TaskItem item);
    /// <summary>更新已有任务</summary>
    Task UpdateAsync(TaskItem item);
    /// <summary>按 ID 删除任务</summary>
    Task DeleteAsync(Guid id);
}
