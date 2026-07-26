using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.App;

/// <summary>
/// 保存计时区域的纯展示状态，并统一生成时间和目标文案。
/// </summary>
internal sealed class TimerPresentationState
{
    private TimeSpan? _lastSessionDuration;
    private int _lastSessionTargetSeconds;

    internal bool IsTargetReached { get; set; }

    internal string ElapsedDisplay { get; private set; } = "00:00:00";

    internal string StatusText { get; private set; } = "就绪";

    internal void ClearLastSession()
    {
        _lastSessionDuration = null;
        _lastSessionTargetSeconds = 0;
    }

    internal void RememberSession(PomodoroSession session, int targetSeconds)
    {
        _lastSessionDuration = session.Duration;
        _lastSessionTargetSeconds = targetSeconds;
        ElapsedDisplay = FormatElapsed(session.Duration);
    }

    internal void RefreshStatus(TimerStatus status)
    {
        StatusText = IsTargetReached && status == TimerStatus.Stopped
            ? "已完成"
            : status switch
            {
                TimerStatus.Idle => "就绪",
                TimerStatus.Running => "运行中",
                TimerStatus.Paused => "已暂停",
                TimerStatus.Stopped => "已停止",
                _ => string.Empty
            };
    }

    internal void RefreshElapsed(
        TimerState state,
        TimeSpan elapsed,
        int targetSeconds)
    {
        if (state.Status == TimerStatus.Stopped && _lastSessionDuration.HasValue)
        {
            ElapsedDisplay = FormatElapsed(_lastSessionDuration.Value);
            return;
        }

        if (targetSeconds > 0)
        {
            var remainingSeconds = Math.Ceiling(
                Math.Max(0, targetSeconds - elapsed.TotalSeconds));
            ElapsedDisplay = FormatElapsed(TimeSpan.FromSeconds(remainingSeconds));
            return;
        }

        ElapsedDisplay = FormatElapsed(elapsed);
    }

    internal string GetDisplayLabel(PomodoroService service)
    {
        if (_lastSessionDuration.HasValue
            && service.CurrentState.Status == TimerStatus.Stopped)
        {
            return IsTargetReached ? "已完成" : "本次专注";
        }

        return service.TargetSeconds > 0 ? "剩余时间" : "已用时间";
    }

    internal string GetTargetSummary(PomodoroService service, string modeName)
    {
        if (_lastSessionDuration.HasValue
            && service.CurrentState.Status == TimerStatus.Stopped)
        {
            return _lastSessionTargetSeconds > 0
                ? $"目标 {FormatSeconds(_lastSessionTargetSeconds)}"
                : "已保存到专注记录";
        }

        return service.TargetSeconds > 0
            ? $"目标 {FormatSeconds(service.TargetSeconds)}"
            : $"{modeName} · 不限时";
    }

    private static string FormatSeconds(int seconds)
    {
        if (seconds % 3600 == 0)
            return $"{seconds / 3600} 小时";
        if (seconds % 60 == 0)
            return $"{seconds / 60} 分钟";
        return $"{seconds} 秒";
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
}
