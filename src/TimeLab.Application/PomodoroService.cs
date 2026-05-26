using TimeLab.Core;

namespace TimeLab.Application;

/// <summary>
/// 番茄钟应用服务，管理计时器状态与专注记录的生成
/// </summary>
public class PomodoroService
{
    private readonly ISessionRepository _repository;
    private readonly TimerState _state = new();
    private Guid? _currentTaskId;

    public PomodoroService(ISessionRepository repository)
    {
        _repository = repository;
    }

    /// <summary>当前计时器状态</summary>
    public TimerState CurrentState => _state;

    /// <summary>预设目标时长（秒），0 表示正计时模式</summary>
    public int TargetSeconds { get; private set; }

    /// <summary>当前模式</summary>
    public FocusMode CurrentMode { get; private set; } = FocusMode.Focus;

    /// <summary>已完成专注次数（用于判断长休）</summary>
    public int CompletedFocusCount { get; private set; }

    /// <summary>切换到下一模式（专注→短休、短休→专注、第4次→长休）</summary>
    public void AdvanceMode()
    {
        if (CurrentMode == FocusMode.Focus)
        {
            CompletedFocusCount++;
            CurrentMode = CompletedFocusCount % 4 == 0
                ? FocusMode.LongBreak
                : FocusMode.ShortBreak;
        }
        else
        {
            CurrentMode = FocusMode.Focus;
        }
    }

    /// <summary>当前模式建议的预设秒数</summary>
    public int ModeDefaultSeconds => CurrentMode switch
    {
        FocusMode.ShortBreak => 5 * 60,
        FocusMode.LongBreak => 15 * 60,
        _ => 25 * 60
    };

    // ---- 循环模式 ----

    /// <summary>是否处于循环模式</summary>
    public bool IsCycleActive { get; private set; }

    /// <summary>循环：专注秒数</summary>
    public int CycleFocusSeconds { get; private set; }
    /// <summary>循环：休息秒数</summary>
    public int CycleBreakSeconds { get; private set; }
    /// <summary>循环：总轮数</summary>
    public int CycleTotalRounds { get; private set; }
    /// <summary>循环：当前第几轮（1-based）</summary>
    public int CurrentRound { get; private set; }

    /// <summary>开始循环模式</summary>
    public Task StartCycleAsync(int focusSeconds, int breakSeconds, int rounds, Guid? taskId = null)
    {
        IsCycleActive = true;
        CycleFocusSeconds = focusSeconds;
        CycleBreakSeconds = breakSeconds;
        CycleTotalRounds = rounds;
        CurrentRound = 1;
        CurrentMode = FocusMode.Focus;
        CompletedFocusCount = 0;

        return StartAsync(taskId, focusSeconds);
    }

    /// <summary>循环模式下切换到下一阶段，返回新阶段目标秒数；循环结束返回 0</summary>
    public int AdvanceCycle()
    {
        if (!IsCycleActive)
            return 0;

        if (CurrentMode == FocusMode.Focus)
        {
            CompletedFocusCount++;
            CurrentMode = CompletedFocusCount % 4 == 0
                ? FocusMode.LongBreak
                : FocusMode.ShortBreak;
            return CycleBreakSeconds;
        }

        // 休息结束
        if (CurrentRound >= CycleTotalRounds)
        {
            IsCycleActive = false;
            CurrentMode = FocusMode.Focus;
            return 0;
        }

        CurrentRound++;
        CurrentMode = FocusMode.Focus;
        return CycleFocusSeconds;
    }

    /// <summary>停止循环</summary>
    public void StopCycle()
    {
        IsCycleActive = false;
        CurrentMode = FocusMode.Focus;
        CompletedFocusCount = 0;
    }

    /// <summary>获取所有历史专注记录</summary>
    public Task<IReadOnlyList<PomodoroSession>> GetSessionsAsync()
    {
        return _repository.GetAllAsync();
    }

    /// <summary>删除一条专注记录</summary>
    public Task DeleteSessionAsync(Guid id)
    {
        return _repository.DeleteAsync(id);
    }

    /// <summary>重置所有状态（模式不重置）</summary>
    public Task ResetAsync()
    {
        _state.Status = TimerStatus.Idle;
        _state.StartTime = null;
        _state.ElapsedTime = TimeSpan.Zero;
        _currentTaskId = null;
        TargetSeconds = 0;
        return Task.CompletedTask;
    }

    /// <summary>重置所有状态包括模式</summary>
    public Task FullResetAsync()
    {
        ResetAsync();
        CurrentMode = FocusMode.Focus;
        CompletedFocusCount = 0;
        return Task.CompletedTask;
    }

    /// <summary>开始计时，可关联任务和预设时长</summary>
    public Task StartAsync(Guid? taskId = null, int targetSeconds = 0)
    {
        _currentTaskId = taskId;
        TargetSeconds = targetSeconds;
        _state.Status = TimerStatus.Running;
        _state.StartTime = DateTime.Now;
        _state.ElapsedTime = TimeSpan.Zero;
        return Task.CompletedTask;
    }

    /// <summary>从暂停/停止状态恢复计时，保留已累计时长</summary>
    public Task ResumeAsync()
    {
        _state.Status = TimerStatus.Running;
        _state.StartTime = DateTime.Now;
        return Task.CompletedTask;
    }

    /// <summary>暂停计时，累加已计时长</summary>
    public Task PauseAsync()
    {
        if (_state.Status != TimerStatus.Running)
            return Task.CompletedTask;

        _state.Status = TimerStatus.Paused;
        _state.ElapsedTime += DateTime.Now - _state.StartTime!.Value;
        return Task.CompletedTask;
    }

    /// <summary>停止计时并生成一条专注记录，返回 null 表示当前无有效计时</summary>
    public async Task<PomodoroSession?> StopAsync(string? note = null)
    {
        if (_state.Status != TimerStatus.Running && _state.Status != TimerStatus.Paused)
            return null;

        var endTime = DateTime.Now;

        if (_state.Status == TimerStatus.Running)
            _state.ElapsedTime += endTime - _state.StartTime!.Value;

        _state.Status = TimerStatus.Stopped;

        var session = new PomodoroSession
        {
            Id = Guid.NewGuid(),
            TaskId = _currentTaskId,
            StartTime = _state.StartTime!.Value,
            EndTime = endTime,
            Duration = _state.ElapsedTime,
            Note = note,
            Mode = CurrentMode
        };

        _state.ElapsedTime = TimeSpan.Zero;

        await _repository.AddAsync(session);
        return session;
    }
}
