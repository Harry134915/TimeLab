using TimeLab.Application;
using TimeLab.Infrastructure;

namespace TimeLab.App;

public static class AppComposition
{
    public static MainViewModel CreateMainViewModel(Action<string>? onBalloon, Action? onToggleDark)
    {
        var taskRepository = new JsonTaskRepository();
        var sessionRepository = new JsonSessionRepository();
        var taskService = new TaskService(taskRepository);
        var pomodoroService = new PomodoroService(sessionRepository);

        return new MainViewModel(taskService, pomodoroService, onBalloon, onToggleDark);
    }
}
