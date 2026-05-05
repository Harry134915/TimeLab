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

    /// <summary>重置计时器为空闲状态，不生成记录</summary>
    public Task ResetAsync()
    {
        _state.Status = TimerStatus.Idle;
        _state.StartTime = null;
        _state.ElapsedTime = TimeSpan.Zero;
        _currentTaskId = null;
        return Task.CompletedTask;
    }

    /// <summary>开始计时，可关联一个任务</summary>
    public Task StartAsync(Guid? taskId = null)
    {
        _currentTaskId = taskId;
        _state.Status = TimerStatus.Running;
        _state.StartTime = DateTime.Now;
        _state.ElapsedTime = TimeSpan.Zero;
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
            Note = note
        };

        await _repository.AddAsync(session);
        return session;
    }
}
