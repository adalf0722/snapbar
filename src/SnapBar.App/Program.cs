using System.Windows;

namespace SnapBar.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool fixture = args.Contains("--fixture");
        using var instance = new Mutex(true, "Local\\SnapBar.P0", out var first);
        if (!fixture && !first)
        { MessageBox.Show("SnapBar P0 已在執行中，請切回原型控制視窗。", "SnapBar"); return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(fixture ? new FixtureWindow(args.Contains("--delayed-uia"), args.Contains("--deep-uia")) : new MainWindow());
    }
}
