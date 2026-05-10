using System.Windows;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using TimeLab.Application;
using TimeLab.Infrastructure;
using Forms = System.Windows.Forms;

namespace TimeLab.App;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isDark;

    private static readonly (string key, string light, string dark)[] BgBrushes =
    [
        ("CardBrush",       "#FFFFFF", "#2D2D3F"),
        ("TextBrush",       "#2D3436", "#D4D4DC"),
        ("MutedBrush",      "#B2BEC3", "#7A7A8A"),
        ("BorderBrush",     "#E8ECF0", "#404058"),
        ("TimerPanelBrush", "#F7F8FA", "#3D3D50"),
    ];

    public MainWindow()
    {
        InitializeComponent();

        var taskRepo = new JsonTaskRepository();
        var sessionRepo = new JsonSessionRepository();
        var taskService = new TaskService(taskRepo);
        var pomodoroService = new PomodoroService(sessionRepo);

        var viewModel = new MainViewModel(taskService, pomodoroService, ShowBalloon, ToggleDarkMode);
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

    private void ToggleDarkMode()
    {
        _isDark = !_isDark;
        Background = _isDark
            ? new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#1E1E2E")!)
            : new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#F0F2F5")!);
        ApplyBackgroundBrushes();
    }

    private void ApplyBackgroundBrushes()
    {
        var resources = System.Windows.Application.Current.Resources;
        foreach (var (key, light, dark) in BgBrushes)
        {
            resources[key] = new SolidColorBrush(
                (WpfColor)WpfColorConverter.ConvertFromString(_isDark ? dark : light)!);
        }
    }
}
