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
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public MainViewModel(TaskService taskService, PomodoroService pomodoroService)
    {
        _taskService = taskService;
        _pomodoroService = pomodoroService;

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

    private int _notificationDismissSeconds;

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
        set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
    }

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

    private void ShowNotification(string message)
    {
        NotificationMessage = message;
        IsNotificationVisible = true;
        _notificationDismissSeconds = 0;
    }

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
    }

    /// <summary>删除指定专注记录</summary>
    private async Task DeleteSessionAsync(Guid id)
    {
        await _pomodoroService.DeleteSessionAsync(id);
        var session = Sessions.FirstOrDefault(s => s.Id == id);
        if (session is not null)
            Sessions.Remove(session);
    }

    /// <summary>选择关联任务</summary>
    private void SelectTask(Guid id)
    {
        SelectedTaskId = id;
    }

    /// <summary>清除关联任务</summary>
    private void ClearSelectedTask()
    {
        SelectedTaskId = null;
    }

    /// <summary>开始计时</summary>
    private Task StartTimerAsync()
    {
        _pomodoroService.StartAsync(SelectedTaskId);
        _alarmPlayed = false;
        IsOvertime = false;
        _tick.Stop();
        _tick.Start();
        UpdateTimerDisplay();
        return Task.CompletedTask;
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
        UpdateTimerDisplay();
    }

    /// <summary>清除计时器状态，不生成记录</summary>
    private async Task ResetTimerAsync()
    {
        _tick.Stop();
        _alarmPlayed = false;
        IsOvertime = false;
        await _pomodoroService.ResetAsync();
        UpdateTimerDisplay();
    }

    /// <summary>刷新计时器显示：状态文字、实时耗时、超时检测</summary>
    private void UpdateTimerDisplay()
    {
        var state = _pomodoroService.CurrentState;
        StatusText = state.Status switch
        {
            TimerStatus.Idle => "就绪",
            TimerStatus.Running => "运行中",
            TimerStatus.Paused => "已暂停",
            TimerStatus.Stopped => "已停止",
            _ => ""
        };

        var elapsed = state.ElapsedTime;
        if (state.Status == TimerStatus.Running && state.StartTime.HasValue)
            elapsed += DateTime.Now - state.StartTime.Value;

        ElapsedDisplay = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

        // 超时检测
        if (state.Status == TimerStatus.Running && SelectedTaskId.HasValue && !_alarmPlayed)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == SelectedTaskId.Value);
            if (task is not null && task.PlannedSeconds > 0
                && elapsed.TotalSeconds >= task.PlannedSeconds)
            {
                IsOvertime = true;
                _alarmPlayed = true;
                ShowNotification($"时间到！“{task.Title}” 已完成 {task.PlannedSeconds / 60}:{task.PlannedSeconds % 60:D2}");
                SystemSounds.Beep.Play();
            }
        }

        // 通知条自动消失（约 4 秒，250ms × 16）
        if (IsNotificationVisible)
        {
            _notificationDismissSeconds++;
            if (_notificationDismissSeconds >= 16)
            {
                IsNotificationVisible = false;
                NotificationMessage = string.Empty;
                _notificationDismissSeconds = 0;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
