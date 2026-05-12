using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
    private DispatcherTimer? _fadeTimer;

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

        var viewModel = new MainViewModel(taskService, pomodoroService, ShowBalloon, onToggleDark: ToggleDarkMode);
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
        _fadeTimer?.Stop();

        var resources = System.Windows.Application.Current.Resources;
        var steps = 16;
        var stepMs = 25; // 16 × 25 = 400ms
        var current = 0;

        // 起点
        var winFrom = ((SolidColorBrush)Background).Color;
        var winTo = ParseColor(_isDark ? "#1E1E2E" : "#F0F2F5");

        var transitions = new (WpfColor from, WpfColor to, string key)[BgBrushes.Length];
        for (int i = 0; i < BgBrushes.Length; i++)
        {
            var (key, light, dark) = BgBrushes[i];
            transitions[i] = (
                ((SolidColorBrush)resources[key]).Color,
                ParseColor(_isDark ? dark : light),
                key);
        }

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepMs) };
        _fadeTimer.Tick += (_, _) =>
        {
            current++;
            var t = Math.Min((double)current / steps, 1.0);

            Background = new SolidColorBrush(Lerp(winFrom, winTo, t));

            foreach (var (from, to, key) in transitions)
            {
                resources[key] = new SolidColorBrush(Lerp(from, to, t));
            }

            if (current >= steps)
                _fadeTimer?.Stop();
        };
        _fadeTimer.Start();
    }

    private static WpfColor Lerp(WpfColor a, WpfColor b, double t) =>
        WpfColor.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

    private static WpfColor ParseColor(string hex) =>
        (WpfColor)WpfColorConverter.ConvertFromString(hex)!;
}
