using System.Windows;

namespace TimeLab.App;

internal enum ActiveTimerExitChoice
{
    Save,
    Discard,
    Cancel
}

internal interface IWindowDialogService
{
    bool ConfirmDelete(string message);

    ActiveTimerExitChoice ConfirmActiveTimerExit();

    void ShowSaveFailure(string message);
}

/// <summary>
/// 使用 WPF MessageBox 提供主窗口所需的确认和错误提示。
/// </summary>
internal sealed class WindowDialogService : IWindowDialogService
{
    private readonly Window _owner;

    internal WindowDialogService(Window owner)
    {
        _owner = owner;
    }

    public bool ConfirmDelete(string message) =>
        System.Windows.MessageBox.Show(
            _owner,
            message,
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public ActiveTimerExitChoice ConfirmActiveTimerExit()
    {
        var result = System.Windows.MessageBox.Show(
            _owner,
            "当前计时仍在进行。\n\n是：保存本次记录并退出\n否：不保存本次记录并退出\n取消：继续计时，不退出",
            "退出 TimeLab",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        return result switch
        {
            MessageBoxResult.Yes => ActiveTimerExitChoice.Save,
            MessageBoxResult.No => ActiveTimerExitChoice.Discard,
            _ => ActiveTimerExitChoice.Cancel
        };
    }

    public void ShowSaveFailure(string message)
    {
        System.Windows.MessageBox.Show(
            _owner,
            $"本次记录保存失败，应用不会退出。\n计时状态已保留，可以稍后重试。\n\n{message}",
            "保存失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
