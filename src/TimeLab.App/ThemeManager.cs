using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSystemColors = System.Windows.SystemColors;

namespace TimeLab.App;

/// <summary>
/// 管理浅色、深色和系统高对比度主题，并持久化主题偏好。
/// </summary>
internal sealed class ThemeManager : IDisposable
{
    private readonly Window _window;
    private readonly SettingsStore _settingsStore;
    private bool _isDark;
    private bool _isDisposed;
    private DispatcherTimer? _fadeTimer;

    private static readonly (string key, string light, string dark)[] BackgroundBrushes =
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

    internal ThemeManager(Window window, SettingsStore settingsStore)
    {
        _window = window;
        _settingsStore = settingsStore;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    internal void ToggleTheme()
    {
        _isDark = !_isDark;
        ApplyCurrentTheme(animate: true);
    }

    internal void LoadAndApply(MainViewModel viewModel)
    {
        var settings = _settingsStore.Load();
        if (settings.IsDarkMode)
            viewModel.IsDarkMode = true;
        else
            ApplyCurrentTheme(animate: false);
    }

    internal void Save(bool isDarkMode)
    {
        _settingsStore.Save(new AppSettings
        {
            IsDarkMode = isDarkMode
        });
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _fadeTimer?.Stop();
    }

    private void ApplyCurrentTheme(bool animate)
    {
        _fadeTimer?.Stop();

        var resources = System.Windows.Application.Current.Resources;
        if (SystemParameters.HighContrast)
        {
            ApplyHighContrastResources(resources);
            return;
        }

        resources["PrimaryBrush"] = new SolidColorBrush(ParseColor("#3659D9"));
        resources["PrimaryHoverBrush"] = new SolidColorBrush(ParseColor("#2948BD"));
        resources["ButtonForegroundBrush"] = new SolidColorBrush(ParseColor("#FFFFFF"));
        resources["StopBrush"] = new SolidColorBrush(ParseColor("#B5472D"));
        resources["SuccessFillBrush"] = new SolidColorBrush(ParseColor("#126B3E"));
        resources["DangerBrush"] = new SolidColorBrush(ParseColor("#B42318"));
        resources["DangerHoverBrush"] = new SolidColorBrush(ParseColor("#8F1710"));

        const int steps = 10;
        const int stepMilliseconds = 20;
        var current = 0;
        var windowFrom = ((SolidColorBrush)_window.Background).Color;
        var windowTo = ParseColor(_isDark ? "#1E1E2E" : "#F0F2F5");

        var transitions = new (WpfColor from, WpfColor to, string key)[BackgroundBrushes.Length];
        for (var i = 0; i < BackgroundBrushes.Length; i++)
        {
            var (key, light, dark) = BackgroundBrushes[i];
            transitions[i] = (
                ((SolidColorBrush)resources[key]).Color,
                ParseColor(_isDark ? dark : light),
                key);
        }

        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            _window.Background = new SolidColorBrush(windowTo);
            foreach (var (_, to, key) in transitions)
                resources[key] = new SolidColorBrush(to);
            return;
        }

        _fadeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(stepMilliseconds)
        };
        _fadeTimer.Tick += (_, _) =>
        {
            current++;
            var progress = Math.Min((double)current / steps, 1.0);

            if (_window.Background is SolidColorBrush backgroundBrush)
                backgroundBrush.Color = Lerp(windowFrom, windowTo, progress);

            foreach (var (from, to, key) in transitions)
                resources[key] = new SolidColorBrush(Lerp(from, to, progress));

            if (current >= steps)
                _fadeTimer?.Stop();
        };
        _fadeTimer.Start();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast))
            return;

        _window.Dispatcher.InvokeAsync(() => ApplyCurrentTheme(animate: false));
    }

    private void ApplyHighContrastResources(ResourceDictionary resources)
    {
        _window.Background = WpfSystemColors.WindowBrush;
        resources["PrimaryBrush"] = WpfSystemColors.HighlightBrush;
        resources["PrimaryHoverBrush"] = WpfSystemColors.HotTrackBrush;
        resources["ButtonForegroundBrush"] = WpfSystemColors.HighlightTextBrush;
        resources["CardBrush"] = WpfSystemColors.WindowBrush;
        resources["TextBrush"] = WpfSystemColors.WindowTextBrush;
        resources["MutedBrush"] = WpfSystemColors.GrayTextBrush;
        resources["BorderBrush"] = WpfSystemColors.ActiveBorderBrush;
        resources["CardBorderBrush"] = WpfSystemColors.ActiveBorderBrush;
        resources["TimerPanelBrush"] = WpfSystemColors.ControlBrush;
        resources["AccentTextBrush"] = WpfSystemColors.HotTrackBrush;
        resources["SuccessBrush"] = WpfSystemColors.HighlightBrush;
        resources["SuccessFillBrush"] = WpfSystemColors.HighlightBrush;
        resources["StopBrush"] = WpfSystemColors.HighlightBrush;
        resources["StopTextBrush"] = WpfSystemColors.WindowTextBrush;
        resources["DangerBrush"] = WpfSystemColors.HighlightBrush;
        resources["DangerTextBrush"] = WpfSystemColors.WindowTextBrush;
        resources["DangerHoverBrush"] = WpfSystemColors.HotTrackBrush;
        resources["WarningTextBrush"] = WpfSystemColors.WindowTextBrush;
        resources["SuccessSurfaceBrush"] = WpfSystemColors.ControlBrush;
        resources["DangerSurfaceBrush"] = WpfSystemColors.ControlBrush;
        resources["WarningSurfaceBrush"] = WpfSystemColors.ControlBrush;
        resources["AccentSurfaceBrush"] = WpfSystemColors.ControlBrush;
        resources["FocusBrush"] = WpfSystemColors.HighlightBrush;
    }

    private static WpfColor Lerp(WpfColor first, WpfColor second, double progress) =>
        WpfColor.FromRgb(
            (byte)(first.R + (second.R - first.R) * progress),
            (byte)(first.G + (second.G - first.G) * progress),
            (byte)(first.B + (second.B - first.B) * progress));

    private static WpfColor ParseColor(string hex) =>
        (WpfColor)WpfColorConverter.ConvertFromString(hex)!;
}
