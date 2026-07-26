using System.Runtime.InteropServices;

namespace TimeLab.App;

internal static class Program
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [STAThread]
    internal static void Main()
    {
        SetProcessDpiAwarenessContext(PerMonitorAwareV2);

        var application = new App();
        application.InitializeComponent();
        application.Run();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
