namespace TimeLab.Core;

/// <summary>
/// 番茄钟计时器状态
/// </summary>
public enum TimerStatus
{
    /// <summary>空闲，未开始计时</summary>
    Idle,
    /// <summary>计时中</summary>
    Running,
    /// <summary>已暂停</summary>
    Paused,
    /// <summary>已停止</summary>
    Stopped
}
