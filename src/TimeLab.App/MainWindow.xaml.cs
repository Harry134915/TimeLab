using System.Windows;
using TimeLab.Application;
using TimeLab.Infrastructure;
using Forms = System.Windows.Forms;

namespace TimeLab.App;

/// <summary>
/// 主窗口，组装依赖、设置 DataContext、管理系统托盘
/// </summary>
public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public MainWindow()
    {
        InitializeComponent();

        var taskRepo = new JsonTaskRepository();
        var sessionRepo = new JsonSessionRepository();
        var taskService = new TaskService(taskRepo);
        var pomodoroService = new PomodoroService(sessionRepo);

        var viewModel = new MainViewModel(taskService, pomodoroService, ShowBalloon);
        DataContext = viewModel;

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TimeLab",
            Icon = System.Drawing.SystemIcons.Information,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        Loaded += async (_, _) => await viewModel.LoadAsync();
        Closed += (_, _) => _notifyIcon.Dispose();
    }

    private void ShowBalloon(string message)
    {
        _notifyIcon.ShowBalloonTip(3000, "TimeLab 提醒", message, Forms.ToolTipIcon.Info);
    }
}
