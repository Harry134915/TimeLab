using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

namespace TimeLab.App;

/// <summary>
/// 在 UI 更新完成后发布 Windows UI Automation Live Region 事件。
/// </summary>
internal sealed class LiveRegionAnnouncer
{
    private readonly Dispatcher _dispatcher;

    internal LiveRegionAnnouncer(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    internal void Announce(FrameworkElement element)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var peer = UIElementAutomationPeer.FromElement(element)
                ?? UIElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }, DispatcherPriority.Background);
    }
}
