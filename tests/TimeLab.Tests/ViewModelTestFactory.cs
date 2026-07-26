using TimeLab.App;
using TimeLab.Application;

namespace TimeLab.Tests;

internal static class ViewModelTestFactory
{
    internal static MainViewModel Create(
        PomodoroService pomodoroService,
        TaskService? taskService = null,
        Func<string, bool>? onConfirmDelete = null,
        Action? onTimerReached = null,
        Action<string>? onBalloon = null)
    {
        taskService ??= new TaskService(new InMemoryTaskRepository());
        var interactionState = new WorkspaceInteractionState();
        var taskList = new TaskListViewModel(
            taskService,
            interactionState,
            onConfirmDelete);
        var sessionLog = new SessionLogViewModel(
            pomodoroService,
            taskList,
            interactionState,
            onConfirmDelete);
        var timer = new TimerViewModel(
            pomodoroService,
            taskList,
            sessionLog,
            interactionState,
            onTimerReached ?? (() => { }));

        return new MainViewModel(
            taskList,
            timer,
            sessionLog,
            interactionState,
            onBalloon);
    }
}
