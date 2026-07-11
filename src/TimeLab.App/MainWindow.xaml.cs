using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace TimeLab.App;

/// <summary>
/// 主窗口，负责窗口生命周期、快捷键和主题切换等 UI 外壳行为。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsStore _settingsStore = new();
    private readonly TrayIconService _trayIconService;
    private bool _isDark;
    private bool _isExitInProgress;
    private DispatcherTimer? _fadeTimer;

    private static readonly (string key, string light, string dark)[] BgBrushes =
    [
        ("CardBrush",           "#FFFFFF", "#2D2D3F"),
        ("TextBrush",           "#2D3436", "#F2F3F5"),
        ("MutedBrush",          "#5F6B76", "#BBC1CB"),
        ("BorderBrush",         "#84909C", "#777B91"),
        ("CardBorderBrush",     "#D7DCE2", "#505267"),
        ("TimerPanelBrush",     "#F5F7FA", "#383A4C"),
        ("AccentTextBrush",     "#2F50C8", "#AFC0FF"),
        ("SuccessBrush",        "#126B3E", "#70D6A2"),
        ("StopTextBrush",       "#9E3B28", "#FF9F88"),
        ("DangerTextBrush",     "#A61B13", "#FF8A80"),
        ("WarningTextBrush",    "#8A5A00", "#FFD27A"),
        ("SuccessSurfaceBrush", "#EDF9F2", "#223B31"),
        ("DangerSurfaceBrush",  "#FFF1F0", "#442C32"),
        ("WarningSurfaceBrush", "#FFF6DF", "#4A3A22"),
        ("AccentSurfaceBrush",  "#EEF1FE", "#353B59"),
        ("FocusBrush",          "#1D4ED8", "#F8FAFC"),
    ];

    /// <summary>
    /// 初始化主窗口、ViewModel、系统托盘服务和窗口生命周期事件。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = AppComposition.CreateMainViewModel(ShowBalloon, ToggleDarkMode, ConfirmDelete);
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _trayIconService = new TrayIconService(ShowFromTray, ExitFromTray);

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadAsync();
            LoadAndApplySettings();
        };
        Closed += (_, _) => _trayIconService.Dispose();
    }

    private bool _actuallyQuit;

    /// <summary>
    /// 响应托盘“显示”动作，恢复并激活主窗口。
    /// </summary>
    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>
    /// 响应托盘“退出”动作，绕过隐藏到托盘逻辑并真正关闭应用。
    /// </summary>
    private async void ExitFromTray()
    {
        if (_isExitInProgress)
            return;

        _isExitInProgress = true;
        try
        {
            var saveActiveTimer = false;
            if (_viewModel.IsTimerActive)
            {
                // 托盘菜单也可在窗口隐藏时触发；先显示窗口，确保确认框不会出现在后台。
                ShowFromTray();
                var result = System.Windows.MessageBox.Show(
                    this,
                    "当前计时仍在进行。\n\n是：保存本次记录并退出\n否：不保存本次记录并退出\n取消：继续计时，不退出",
                    "退出 TimeLab",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.Cancel);

                if (result == MessageBoxResult.Cancel)
                    return;

                saveActiveTimer = result == MessageBoxResult.Yes;
            }

            try
            {
                await _viewModel.PrepareForExitAsync(saveActiveTimer);
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"本次记录保存失败，应用不会退出。\n计时状态已保留，可以稍后重试。\n\n{exception.Message}",
                    "保存失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _actuallyQuit = true;
            Close();
        }
        finally
        {
            if (!_actuallyQuit)
                _isExitInProgress = false;
        }
    }

    /// <summary>
    /// 默认关闭时隐藏到系统托盘，只有通过托盘“退出”才真正关闭应用。
    /// </summary>
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

    /// <summary>
    /// 处理全局快捷键，输入框获得焦点时不拦截用户输入。
    /// </summary>
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        // 文本编辑和下拉选择优先保留标准键盘语义；其余未处理按键继续作为窗口快捷键。
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
            or System.Windows.Controls.PasswordBox
            or System.Windows.Controls.ComboBox)
        {
            return;
        }

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

    /// <summary>
    /// 通过系统托盘展示计时到点提醒。
    /// </summary>
    private void ShowBalloon(string message)
    {
        _trayIconService.ShowBalloon(message);
    }

    private bool ConfirmDelete(string message) =>
        System.Windows.MessageBox.Show(
            this,
            message,
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>
    /// 在浅色和深色资源之间做短动画过渡。
    /// </summary>
    private void ToggleDarkMode()
    {
        _isDark = !_isDark;
        _fadeTimer?.Stop();

        var resources = System.Windows.Application.Current.Resources;
        var steps = 10;
        var stepMs = 20;
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

        if (!SystemParameters.ClientAreaAnimation)
        {
            Background = new SolidColorBrush(winTo);
            foreach (var (_, to, key) in transitions)
                resources[key] = new SolidColorBrush(to);
            return;
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

    /// <summary>
    /// 监听需要持久化的 ViewModel 状态变化。
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDarkMode))
            SaveSettings();

        if (e.PropertyName == nameof(MainViewModel.StatusText))
            RaiseLiveRegionChanged(TimerStatusText);
        else if (e.PropertyName == nameof(MainViewModel.IsNotificationVisible) && _viewModel.IsNotificationVisible)
            RaiseLiveRegionChanged(NotificationText);
        else if (e.PropertyName == nameof(MainViewModel.NewTaskDurationError)
                 && !string.IsNullOrEmpty(_viewModel.NewTaskDurationError))
            RaiseLiveRegionChanged(TaskDurationErrorText);
        else if (e.PropertyName == nameof(MainViewModel.CycleValidationMessage)
                 && !string.IsNullOrEmpty(_viewModel.CycleValidationMessage))
            RaiseLiveRegionChanged(CycleValidationText);
    }

    private void RaiseLiveRegionChanged(FrameworkElement element)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var peer = UIElementAutomationPeer.FromElement(element)
                ?? UIElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 启动时读取设置，并把深色模式偏好应用到当前窗口。
    /// </summary>
    private void LoadAndApplySettings()
    {
        var settings = _settingsStore.Load();
        if (settings.IsDarkMode)
            _viewModel.IsDarkMode = true;
    }

    /// <summary>
    /// 保存当前可持久化的窗口设置。
    /// </summary>
    private void SaveSettings()
    {
        _settingsStore.Save(new AppSettings
        {
            IsDarkMode = _viewModel.IsDarkMode
        });
    }
}
