using Forms = System.Windows.Forms;

namespace TimeLab.App;

/// <summary>
/// 封装系统托盘图标、菜单和气泡提醒。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(Action onShowRequested, Action onExitRequested)
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TimeLab",
            Icon = System.Drawing.SystemIcons.Information,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _notifyIcon.ContextMenuStrip.Items.Add("显示", null, (_, _) => onShowRequested());
        _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) => onExitRequested());
        _notifyIcon.DoubleClick += (_, _) => onShowRequested();
    }

    /// <summary>
    /// 通过系统托盘显示提醒气泡。
    /// </summary>
    public void ShowBalloon(string message)
    {
        _notifyIcon.ShowBalloonTip(3000, "TimeLab 提醒", message, Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}
