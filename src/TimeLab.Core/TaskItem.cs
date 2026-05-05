namespace TimeLab.Core;

/// <summary>
/// 任务实体
/// </summary>
public class TaskItem
{
    /// <summary>唯一标识</summary>
    public Guid Id { get; set; }
    /// <summary>任务标题</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>是否已完成</summary>
    public bool IsCompleted { get; set; }
    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>完成时间，未完成时为 null</summary>
    public DateTime? CompletedAt { get; set; }
}
