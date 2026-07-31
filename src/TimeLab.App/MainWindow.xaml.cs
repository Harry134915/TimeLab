using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace TimeLab.App;

/// <summary>
/// 主窗口装配层，负责 View 事件、快捷键和具体控件的状态播报。
/// </summary>
public partial class MainWindow : Window, IMainWindowHost
{
    private readonly MainViewModel _viewModel;
    private readonly ThemeManager _themeManager;
    private readonly WindowLifecycleCoordinator _lifecycle;
    private readonly LiveRegionAnnouncer _liveRegionAnnouncer;

    public MainWindow()
    {
        InitializeComponent();

        var dialogs = new WindowDialogService(this);
        _themeManager = new ThemeManager(this, new SettingsStore());
        _lifecycle = new WindowLifecycleCoordinator(this, dialogs);
        _liveRegionAnnouncer = new LiveRegionAnnouncer(Dispatcher);

        _viewModel = AppComposition.CreateMainViewModel(
            _lifecycle.ShowBalloon,
            _themeManager.ToggleTheme,
            dialogs.ConfirmDelete);
        _lifecycle.Attach(_viewModel);
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.TaskList.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Timer.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdateWorkspaceScrolling();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
        _themeManager.LoadAndApply(_viewModel);
        UpdateWorkspaceScrolling();
    }

    private void UpdateWorkspaceScrolling()
    {
        WorkspaceScrollViewer.HorizontalScrollBarVisibility =
            ActualWidth < 900
                ? System.Windows.Controls.ScrollBarVisibility.Auto
                : System.Windows.Controls.ScrollBarVisibility.Disabled;
        WorkspaceScrollViewer.VerticalScrollBarVisibility =
            ActualHeight < 620
                ? System.Windows.Controls.ScrollBarVisibility.Auto
                : System.Windows.Controls.ScrollBarVisibility.Disabled;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_lifecycle.HandleClosing())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.TaskList.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Timer.PropertyChanged -= OnViewModelPropertyChanged;
        _themeManager.Dispose();
        _lifecycle.Dispose();
        base.OnClosed(e);
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
            or System.Windows.Controls.PasswordBox
            or System.Windows.Controls.ComboBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                _viewModel.Timer.ToggleTimerCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.Timer.StopOrResetCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == _viewModel
            && e.PropertyName == nameof(MainViewModel.IsDarkMode))
        {
            _themeManager.Save(_viewModel.IsDarkMode);
        }

        if (sender == _viewModel.Timer
            && e.PropertyName == nameof(TimerViewModel.StatusText))
        {
            _liveRegionAnnouncer.Announce(TimerStatusText);
        }
        else if (sender == _viewModel
                 && e.PropertyName == nameof(MainViewModel.IsNotificationVisible)
                 && _viewModel.IsNotificationVisible)
        {
            _liveRegionAnnouncer.Announce(NotificationText);
        }
        else if (sender == _viewModel.TaskList
                 && e.PropertyName == nameof(TaskListViewModel.NewTaskDurationError)
                 && !string.IsNullOrEmpty(_viewModel.TaskList.NewTaskDurationError))
        {
            _liveRegionAnnouncer.Announce(TaskDurationErrorText);
        }
        else if (sender == _viewModel.Timer
                 && e.PropertyName == nameof(TimerViewModel.CycleValidationMessage)
                 && !string.IsNullOrEmpty(_viewModel.Timer.CycleValidationMessage))
        {
            _liveRegionAnnouncer.Announce(CycleValidationText);
        }
    }

    void IMainWindowHost.ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    void IMainWindowHost.Hide() => Hide();

    void IMainWindowHost.Close() => Close();
}
