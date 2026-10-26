using System.Windows;

namespace SnapBar.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool fixture = args.Contains("--fixture");
        if (fixture)
        {
            var fixtureApp = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            fixtureApp.Run(new FixtureWindow(args.Contains("--delayed-uia"), args.Contains("--deep-uia"),
                args.Contains("--web-link") ? "link" : args.Contains("--web-search") ? "search" : null,
                args.Contains("--path-fixture") ? System.IO.Path.Combine(AppContext.BaseDirectory, "README.md") : null));
            return;
        }
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\SnapBar.ShowSettings");
        using var instance = new Mutex(true, "Local\\SnapBar.P0", out var first);
        if (!first) { showSettings.Set(); return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        var wait = ThreadPool.RegisterWaitForSingleObject(showSettings, (_, _) =>
        {
            if (!app.Dispatcher.HasShutdownStarted)
                app.Dispatcher.BeginInvoke(window.ShowSettings);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        app.SessionEnding += (_, _) => window.PrepareSessionEnd();
        try { app.Run(window); }
        finally { wait.Unregister(null); }
    }
}
