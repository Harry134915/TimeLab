namespace TimeLab.Core;

/// <summary>
/// 一次专注记录
/// </summary>
public class PomodoroSession
{
    /// <summary>唯一标识</summary>
    public Guid Id { get; set; }
    /// <summary>关联的任务 ID，未关联时为 null</summary>
    public Guid? TaskId { get; set; }
    /// <summary>开始时间</summary>
    public DateTime StartTime { get; set; }
    /// <summary>结束时间</summary>
    public DateTime EndTime { get; set; }
    /// <summary>持续时长</summary>
    public TimeSpan Duration { get; set; }
    /// <summary>备注，可选</summary>
    public string? Note { get; set; }
}
