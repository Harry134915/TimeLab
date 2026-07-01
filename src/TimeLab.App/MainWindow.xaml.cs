using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using Forms = System.Windows.Forms;

namespace TimeLab.App;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly MainViewModel _viewModel;
    private readonly SettingsStore _settingsStore = new();
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

        var viewModel = AppComposition.CreateMainViewModel(ShowBalloon, ToggleDarkMode);
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TimeLab",
            Icon = System.Drawing.SystemIcons.Information,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _notifyIcon.ContextMenuStrip.Items.Add("显示", null, (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
        _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) =>
        {
            _actuallyQuit = true;
            Close();
        });
        _notifyIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadAsync();
            LoadAndApplySettings();
        };
        Closed += (_, _) => _notifyIcon.Dispose();
    }

    private bool _actuallyQuit;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_actuallyQuit)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;

        // 焦点在输入框内时不拦截快捷键
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        switch (e.Key)
        {
            case Key.Space:
                _viewModel.ToggleTimerCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.StopOrResetCommand.Execute(null);
                e.Handled = true;
                break;
        }
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

            if (Background is SolidColorBrush bgBrush)
            {
                bgBrush.Color = Lerp(winFrom, winTo, t);
            }

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDarkMode))
            SaveSettings();
    }

    private void LoadAndApplySettings()
    {
        var settings = _settingsStore.Load();
        if (settings.IsDarkMode)
            _viewModel.IsDarkMode = true;
    }

    private void SaveSettings()
    {
        _settingsStore.Save(new AppSettings
        {
            IsDarkMode = _viewModel.IsDarkMode
        });
    }
}
