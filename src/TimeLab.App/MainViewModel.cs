using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Media;
using System.Windows.Input;
using System.Windows.Threading;
using TimeLab.Application;
using TimeLab.Core;

namespace TimeLab.App;

/// <summary>
/// 主窗口的 ViewModel，管理 Todo 列表、番茄钟和专注记录的 UI 交互
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly TaskService _taskService;
    private readonly PomodoroService _pomodoroService;
    private readonly Action<string>? _onBalloon;
    private readonly Action? _onToggleDark;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private DispatcherTimer? _notificationTimer;

    public MainViewModel(TaskService taskService, PomodoroService pomodoroService,
                         Action<string>? onBalloon = null, Action? onToggleDark = null)
    {
        _taskService = taskService;
        _pomodoroService = pomodoroService;
        _onBalloon = onBalloon;
        _onToggleDark = onToggleDark;

        // 每秒刷新计时显示
        _tick.Tick += (_, _) => UpdateTimerDisplay();

        AddTaskCommand = new RelayCommand(async _ => await AddTaskAsync());
        CompleteTaskCommand = new RelayCommand(async p => await CompleteTaskAsync((Guid)p!));
        DeleteTaskCommand = new RelayCommand(async p => await DeleteTaskAsync((Guid)p!));
        DeleteSessionCommand = new RelayCommand(async p => await DeleteSessionAsync((Guid)p!));
        SelectTaskCommand = new RelayCommand(p => SelectTask((Guid)p!));
        ClearSelectedTaskCommand = new RelayCommand(_ => ClearSelectedTask());
        StartTimerCommand = new RelayCommand(async _ => await StartTimerAsync());
        PauseTimerCommand = new RelayCommand(async _ => await PauseTimerAsync());
        StopTimerCommand = new RelayCommand(async _ => await StopTimerAsync());
        ResetTimerCommand = new RelayCommand(async _ => await ResetTimerAsync());
        ToggleTimerCommand = new RelayCommand(async _ => await ToggleTimerAsync());
        StopOrResetCommand = new RelayCommand(async _ => await StopOrResetAsync());
        StartPresetCommand = new RelayCommand(p => StartPreset((int)p!));
        StartCycleCommand = new RelayCommand(async _ => await StartCycleAsync());
    }

    /// <summary>任务列表</summary>
    public ObservableCollection<TaskItem> Tasks { get; } = [];

    /// <summary>专注记录列表</summary>
    public ObservableCollection<PomodoroSession> Sessions { get; } = [];

    /// <summary>新任务输入框文本</summary>
    private string _newTaskTitle = string.Empty;
    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set { _newTaskTitle = value; OnPropertyChanged(nameof(NewTaskTitle)); }
    }

    /// <summary>新任务时长数值</summary>
    private string _newTaskDuration = string.Empty;
    public string NewTaskDuration
    {
        get => _newTaskDuration;
        set { _newTaskDuration = value; OnPropertyChanged(nameof(NewTaskDuration)); }
    }

    /// <summary>时长单位索引：0=秒, 1=分钟, 2=时</summary>
    public int DurationUnitIndex { get; set; }

    /// <summary>时长单位列表</summary>
    public List<string> DurationUnits { get; } = ["秒", "分钟", "时"];

    /// <summary>预设时长按钮（分钟），随模式动态切换</summary>
    public int[] PresetMinutes => _pomodoroService.CurrentMode switch
    {
        FocusMode.ShortBreak => [3, 5, 10],
        FocusMode.LongBreak => [10, 15, 30],
        _ => [15, 25, 30, 45, 60]
    };

    /// <summary>当前模式名称</summary>
    public string ModeName => _pomodoroService.CurrentMode switch
    {
        FocusMode.ShortBreak => "短休",
        FocusMode.LongBreak => "长休",
        _ => "专注"
    };

    /// <summary>模式建议时长（分钟）</summary>
    public int ModeDefaultMinutes => _pomodoroService.ModeDefaultSeconds / 60;

    /// <summary>已完成专注轮数</summary>
    public int CompletedFocusCount => _pomodoroService.CompletedFocusCount;

    // ---- 循环模式配置 ----

    /// <summary>循环：专注分钟</summary>
    public int CycleFocusMinutes { get; set; } = 25;
    /// <summary>循环：休息分钟</summary>
    public int CycleBreakMinutes { get; set; } = 5;
    /// <summary>循环：轮数输入文本</summary>
    private string _cycleTotalRoundsText = "4";
    public string CycleTotalRoundsText
    {
        get => _cycleTotalRoundsText;
        set { _cycleTotalRoundsText = value; OnPropertyChanged(nameof(CycleTotalRoundsText)); }
    }

    /// <summary>循环是否激活</summary>
    public bool IsCycleActive => _pomodoroService.IsCycleActive;
    /// <summary>循环进度文字</summary>
    public string CycleProgress => IsCycleActive
        ? $"第 {_pomodoroService.CurrentRound}/{_pomodoroService.CycleTotalRounds} 轮 · {ModeName}"
        : string.Empty;

    /// <summary>今日统计文字</summary>
    private string _todayStats = string.Empty;
    public string TodayStats
    {
        get => _todayStats;
        set { _todayStats = value; OnPropertyChanged(nameof(TodayStats)); }
    }

    /// <summary>是否已超时</summary>
    private bool _isOvertime;
    public bool IsOvertime
    {
        get => _isOvertime;
        set { _isOvertime = value; OnPropertyChanged(nameof(IsOvertime)); }
    }

    private bool _alarmPlayed;

    /// <summary>通知消息内容</summary>
    private string _notificationMessage = string.Empty;
    public string NotificationMessage
    {
        get => _notificationMessage;
        set { _notificationMessage = value; OnPropertyChanged(nameof(NotificationMessage)); }
    }

    /// <summary>通知条是否可见</summary>
    private bool _isNotificationVisible;
    public bool IsNotificationVisible
    {
        get => _isNotificationVisible;
        set { _isNotificationVisible = value; OnPropertyChanged(nameof(IsNotificationVisible)); }
    }

    /// <summary>计时器时间显示 HH:MM:SS</summary>
    private string _elapsedDisplay = "00:00";
    public string ElapsedDisplay
    {
        get => _elapsedDisplay;
        set { _elapsedDisplay = value; OnPropertyChanged(nameof(ElapsedDisplay)); }
    }

    /// <summary>当前选中的关联任务 ID</summary>
    private Guid? _selectedTaskId;
    public Guid? SelectedTaskId
    {
        get => _selectedTaskId;
        set { _selectedTaskId = value; OnPropertyChanged(nameof(SelectedTaskId)); OnPropertyChanged(nameof(SelectedTaskTitle)); }
    }

    /// <summary>选中任务的标题，未选中时显示"无"</summary>
    public string SelectedTaskTitle
        => SelectedTaskId.HasValue
            ? Tasks.FirstOrDefault(t => t.Id == SelectedTaskId.Value)?.Title ?? "无"
            : "无";

    /// <summary>计时器状态中文描述</summary>
    private string _statusText = "就绪";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StartButtonText)); }
    }

    /// <summary>开始按钮文字：暂停时"继续"，其他状态为"开始"</summary>
    public string StartButtonText => _pomodoroService.CurrentState.Status is TimerStatus.Paused
        ? "继续"
        : "开始";

    public ICommand AddTaskCommand { get; }
    public ICommand CompleteTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand DeleteSessionCommand { get; }
    public ICommand SelectTaskCommand { get; }
    public ICommand ClearSelectedTaskCommand { get; }
    public ICommand StartTimerCommand { get; }
    public ICommand PauseTimerCommand { get; }
    public ICommand StopTimerCommand { get; }
    public ICommand ResetTimerCommand { get; }
    public ICommand ToggleTimerCommand { get; }
    public ICommand StopOrResetCommand { get; }
    public ICommand StartPresetCommand { get; }
    public ICommand StartCycleCommand { get; }
    /// <summary>是否深色模式</summary>
    private bool _isDarkMode;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode == value) return;
            _isDarkMode = value;
            _onToggleDark?.Invoke();
            OnPropertyChanged(nameof(IsDarkMode));
            OnPropertyChanged(nameof(DarkToggleText));
        }
    }

    /// <summary>深色切换按钮文字</summary>
    public string DarkToggleText => _isDarkMode ? "深色" : "浅色";

    /// <summary>启动时加载已保存的任务和专注记录</summary>
    public async Task LoadAsync()
    {
        var tasks = await _taskService.GetAllAsync();
        foreach (var t in tasks)
            Tasks.Add(t);

        var sessions = await _pomodoroService.GetSessionsAsync();
        foreach (var s in sessions)
            Sessions.Add(s);
    }

    /// <summary>创建新任务并添加到列表</summary>
    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle))
            return;

        var seconds = ParseDuration();
        var item = await _taskService.CreateAsync(NewTaskTitle, seconds);
        Tasks.Add(item);
        NewTaskTitle = string.Empty;
        NewTaskDuration = string.Empty;
    }

    /// <summary>
    /// 显示应用内通知，并同步触发系统托盘提醒。
    /// </summary>
    private void ShowNotification(string message)
    {
        NotificationMessage = message;
        IsNotificationVisible = true;

        _onBalloon?.Invoke(message);

        //防止连续通知
        _notificationTimer?.Stop();

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

        _notificationTimer.Tick += (_, _) =>
        {
            IsNotificationVisible = false;
            NotificationMessage = string.Empty;
            _notificationTimer?.Stop();
        };

        _notificationTimer?.Start();
    }

    /// <summary>取预设时长和关联任务计划时长中有效的（取最小值，都无效返回0）</summary>
    private int ResolveTargetSeconds()
    {
        var fromPreset = _pomodoroService.TargetSeconds;
        if (fromPreset <= 0) return 0;

        var fromTask = SelectedTaskId.HasValue
            ? Tasks.FirstOrDefault(t => t.Id == SelectedTaskId.Value)?.PlannedSeconds ?? 0
            : 0;

        return fromTask > 0 ? Math.Min(fromPreset, fromTask) : fromPreset;
    }

    /// <summary>
    /// 根据当前计时模式和关联任务生成到时提醒文案。
    /// </summary>
    private string TargetDescription()
    {
        // 循环模式的提示
        if (_pomodoroService.IsCycleActive)
        {
            var r = _pomodoroService.CurrentRound;
            var t = _pomodoroService.CycleTotalRounds;

            if (_pomodoroService.CurrentMode == FocusMode.Focus)
            {
                return r >= t
                    ? $"第 {r}/{t} 轮专注完成！休息后结束"
                    : $"第 {r}/{t} 轮专注完成！休息一下";
            }

            return r >= t
                ? $"全部完成！共 {t} 轮"
                : _pomodoroService.CurrentMode == FocusMode.ShortBreak
                    ? $"第 {r}/{t} 轮短休结束，开始下一轮专注"
                    : $"第 {r}/{t} 轮长休结束，下一轮专注";
        }

        // 普通预设模式
        if (_pomodoroService.TargetSeconds > 0)
        {
            return _pomodoroService.CurrentMode switch
            {
                FocusMode.ShortBreak => "短休结束，开始专注",
                FocusMode.LongBreak => "长休结束，下一轮专注",
                _ => "专注完成！休息一下"
            };
        }

        // 任务计划
        if (SelectedTaskId.HasValue)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == SelectedTaskId.Value);
            if (task?.PlannedSeconds > 0)
                return $"\"{task.Title}\" 任务已完成！";
        }
        return "";
    }

    /// <summary>
    /// 将任务输入区的时长文本按当前单位转换为秒；无效输入视为不设限。
    /// </summary>
    private int ParseDuration()
    {
        if (string.IsNullOrWhiteSpace(NewTaskDuration) || !int.TryParse(NewTaskDuration, out var value))
            return 0;

        return DurationUnitIndex switch
        {
            2 => value * 3600,
            1 => value * 60,
            _ => value
        };
    }

    /// <summary>将指定任务标记为完成</summary>
    private async Task CompleteTaskAsync(Guid id)
    {
        await _taskService.CompleteAsync(id);
        var item = Tasks.FirstOrDefault(t => t.Id == id);
        if (item is not null)
        {
            item.IsCompleted = true;
            item.CompletedAt = DateTime.Now;
        }
    }

    /// <summary>删除指定任务</summary>
    private async Task DeleteTaskAsync(Guid id)
    {
        await _taskService.DeleteAsync(id);
        var item = Tasks.FirstOrDefault(t => t.Id == id);
        if (item is not null)
            Tasks.Remove(item);
        RefreshTodayStats();
    }

    /// <summary>删除指定专注记录</summary>
    private async Task DeleteSessionAsync(Guid id)
    {
        await _pomodoroService.DeleteSessionAsync(id);
        var session = Sessions.FirstOrDefault(s => s.Id == id);
        if (session is not null)
            Sessions.Remove(session);
        RefreshTodayStats();
    }

    /// <summary>选择关联任务</summary>
    private void SelectTask(Guid id)
    {
        SelectedTaskId = id;
    }

    /// <summary>
    /// 清除当前计时器关联的任务。
    /// </summary>
    private void ClearSelectedTask()
    {
        SelectedTaskId = null;
    }

    /// <summary>开始计时（手动），暂停状态下恢复计时，停止后重新开始</summary>
    private Task StartTimerAsync()
    {
        var status = _pomodoroService.CurrentState.Status;
        if (status == TimerStatus.Paused)
            _pomodoroService.ResumeAsync();
        else
            _pomodoroService.StartAsync(SelectedTaskId);
        _alarmPlayed = false;
        IsOvertime = false;
        _tick.Stop();
        _tick.Start();
        UpdateTimerDisplay();
        return Task.CompletedTask;
    }

    /// <summary>按预设时长（分钟）开始计时</summary>
    private Task StartPreset(int minutes)
    {
        _pomodoroService.StopCycle();
        NotifyCycleChanged();
        var seconds = minutes * 60;
        _pomodoroService.StartAsync(SelectedTaskId, seconds);
        _alarmPlayed = false;
        IsOvertime = false;
        _tick.Stop();
        _tick.Start();
        UpdateTimerDisplay();
        return Task.CompletedTask;
    }

    /// <summary>开始循环模式</summary>
    private async Task StartCycleAsync()
    {
        _pomodoroService.StopCycle();
        int.TryParse(CycleTotalRoundsText, out var rounds);
        if (rounds < 1) rounds = 1;
        await _pomodoroService.StartCycleAsync(
            CycleFocusMinutes * 60,
            CycleBreakMinutes * 60,
            rounds,
            SelectedTaskId);
        NotifyCycleChanged();
        _alarmPlayed = false;
        IsOvertime = false;
        _tick.Stop();
        _tick.Start();
        UpdateTimerDisplay();
    }

    /// <summary>暂停计时</summary>
    private Task PauseTimerAsync()
    {
        _pomodoroService.PauseAsync();
        _tick.Stop();
        UpdateTimerDisplay();
        return Task.CompletedTask;
    }

    /// <summary>停止计时并生成专注记录</summary>
    private async Task StopTimerAsync()
    {
        _tick.Stop();
        var session = await _pomodoroService.StopAsync();
        if (session is not null)
            Sessions.Add(session);
        if (_pomodoroService.IsCycleActive)
        {
            _pomodoroService.StopCycle();
            NotifyCycleChanged();
        }
        UpdateTimerDisplay();
    }

    /// <summary>清除计时器状态，不生成记录</summary>
    private async Task ResetTimerAsync()
    {
        _tick.Stop();
        _alarmPlayed = false;
        IsOvertime = false;
        _pomodoroService.StopCycle();
        NotifyCycleChanged();
        await _pomodoroService.ResetAsync();
        UpdateTimerDisplay();
    }

    /// <summary>Space 快捷键：空闲/暂停→开始，运行→暂停</summary>
    private async Task ToggleTimerAsync()
    {
        switch (_pomodoroService.CurrentState.Status)
        {
            case TimerStatus.Running:
                await PauseTimerAsync();
                break;
            case TimerStatus.Idle:
            case TimerStatus.Paused:
            case TimerStatus.Stopped:
                await StartTimerAsync();
                break;
        }
    }

    /// <summary>Esc 快捷键：运行/暂停→停止，其他→清除</summary>
    private async Task StopOrResetAsync()
    {
        switch (_pomodoroService.CurrentState.Status)
        {
            case TimerStatus.Running:
            case TimerStatus.Paused:
                await StopTimerAsync();
                break;
            default:
                await ResetTimerAsync();
                break;
        }
    }

    /// <summary>
    /// 基于当前内存中的任务和 Session 刷新今日统计文本。
    /// </summary>
    private void RefreshTodayStats()
    {
        var today = DateTime.Today;
        var sessions = Sessions.Where(s => s.StartTime.Date == today).ToList();
        var count = sessions.Count;
        var totalMinutes = (int)sessions.Sum(s => s.Duration.TotalMinutes);
        var tasksDone = Tasks.Count(t => t.CompletedAt?.Date == today);
        TodayStats = $"今日 {count} 次 · {totalMinutes} 分钟 · {tasksDone} 个任务";
    }

    /// <summary>刷新计时器显示：状态文字、实时耗时、超时检测</summary>
    private async void UpdateTimerDisplay()
    {
        var state = _pomodoroService.CurrentState;
        RefreshStatusText(state);
        RefreshTodayStats();

        var elapsed = GetCurrentElapsed(state);
        RefreshElapsedDisplay(elapsed);

        await HandleTimerTargetAsync(state, elapsed);
    }

    /// <summary>
    /// 将领域层计时状态映射为界面显示文本。
    /// </summary>
    private void RefreshStatusText(TimerState state)
    {
        StatusText = state.Status switch
        {
            TimerStatus.Idle => "就绪",
            TimerStatus.Running => "运行中",
            TimerStatus.Paused => "已暂停",
            TimerStatus.Stopped => "已停止",
            _ => ""
        };
    }

    /// <summary>
    /// 计算实时已用时长，运行中时补上当前计时片段。
    /// </summary>
    private static TimeSpan GetCurrentElapsed(TimerState state)
    {
        var elapsed = state.ElapsedTime;
        if (state.Status == TimerStatus.Running && state.StartTime.HasValue)
            elapsed += DateTime.Now - state.StartTime.Value;

        return elapsed;
    }

    /// <summary>
    /// 根据是否存在目标时长，刷新正计时或倒计时显示。
    /// </summary>
    private void RefreshElapsedDisplay(TimeSpan elapsed)
    {
        if (_pomodoroService.TargetSeconds > 0)
        {
            var remaining = TimeSpan.FromSeconds(Math.Max(0, _pomodoroService.TargetSeconds - elapsed.TotalSeconds));
            ElapsedDisplay = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }
        else
        {
            ElapsedDisplay = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
    }

    /// <summary>
    /// 检查目标时长是否到达，并分派普通预设或循环模式的后续处理。
    /// </summary>
    private async Task HandleTimerTargetAsync(TimerState state, TimeSpan elapsed)
    {
        if (state.Status != TimerStatus.Running || _alarmPlayed)
            return;

        var targetSeconds = ResolveTargetSeconds();
        if (targetSeconds <= 0 || elapsed.TotalSeconds < targetSeconds)
            return;

        NotifyTimerReachedTarget();

        if (_pomodoroService.IsCycleActive)
        {
            await HandleCycleTargetReachedAsync();
            return;
        }

        if (_pomodoroService.TargetSeconds > 0)
        {
            _pomodoroService.AdvanceMode();
            NotifyModeChanged();
        }
    }

    /// <summary>
    /// 统一处理到时后的超时状态、界面通知和提示音。
    /// </summary>
    private void NotifyTimerReachedTarget()
    {
        IsOvertime = true;
        _alarmPlayed = true;
        ShowNotification($"时间到！{TargetDescription()}");
        SystemSounds.Beep.Play();
    }

    /// <summary>
    /// 循环模式到时后记录当前阶段，并在还有下一阶段时自动启动。
    /// </summary>
    private async Task HandleCycleTargetReachedAsync()
    {
        var session = await _pomodoroService.StopAsync();
        if (session is not null)
            Sessions.Add(session);

        var nextSeconds = _pomodoroService.AdvanceCycle();
        NotifyCycleChanged();

        if (nextSeconds > 0)
        {
            await _pomodoroService.StartAsync(SelectedTaskId, nextSeconds);
            _alarmPlayed = false;
            IsOvertime = false;
            _tick.Stop();
            _tick.Start();
            return;
        }

        _tick.Stop();
        UpdateTimerDisplay();
    }

    /// <summary>
    /// 通知与循环模式相关的绑定属性发生变化。
    /// </summary>
    private void NotifyCycleChanged()
    {
        NotifyModeChanged();
        OnPropertyChanged(nameof(IsCycleActive));
        OnPropertyChanged(nameof(CycleProgress));
    }

    /// <summary>
    /// 通知与专注/休息模式相关的绑定属性发生变化。
    /// </summary>
    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(ModeName));
        OnPropertyChanged(nameof(ModeDefaultMinutes));
        OnPropertyChanged(nameof(CompletedFocusCount));
        OnPropertyChanged(nameof(PresetMinutes));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
