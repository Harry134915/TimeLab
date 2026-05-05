using System.Windows;
using TimeLab.Application;
using TimeLab.Infrastructure;

namespace TimeLab.App;

/// <summary>
/// 主窗口，组装依赖并设置 DataContext
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var taskRepo = new JsonTaskRepository();
        var sessionRepo = new JsonSessionRepository();
        var taskService = new TaskService(taskRepo);
        var pomodoroService = new PomodoroService(sessionRepo);

        var viewModel = new MainViewModel(taskService, pomodoroService);
        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
