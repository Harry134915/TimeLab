namespace TimeLab.Core;

/// <summary>
/// 计时器当前状态
/// </summary>
public class TimerState
{
    /// <summary>计时器状态</summary>
    public TimerStatus Status { get; set; }
    /// <summary>当前计时段的开始时间</summary>
    public DateTime? StartTime { get; set; }
    /// <summary>已累计的时长（不含当前进行中的时段）</summary>
    public TimeSpan ElapsedTime { get; set; }
}
