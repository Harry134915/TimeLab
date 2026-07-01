using TimeLab.Application;
using TimeLab.Infrastructure;

namespace TimeLab.App;

/// <summary>
/// 集中创建 App 层所需的仓储、服务和 ViewModel。
/// </summary>
public static class AppComposition
{
    /// <summary>
    /// 创建主窗口使用的 ViewModel，并注入 UI 回调。
    /// </summary>
    public static MainViewModel CreateMainViewModel(Action<string>? onBalloon, Action? onToggleDark)
    {
        var taskRepository = new JsonTaskRepository();
        var sessionRepository = new JsonSessionRepository();
        var taskService = new TaskService(taskRepository);
        var pomodoroService = new PomodoroService(sessionRepository);

        return new MainViewModel(taskService, pomodoroService, onBalloon, onToggleDark);
    }
}
