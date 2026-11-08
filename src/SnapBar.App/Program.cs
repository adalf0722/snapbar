using System.Windows;

namespace SnapBar.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // A side-effect-free launch check: no settings, tray, input hooks or single-instance signal.
        if (args.SequenceEqual(["--startup-check"])) return;
        if (args.SequenceEqual(["--language-resources-check"]))
        {
            try
            {
                var english = UiText.Get(System.Globalization.CultureInfo.GetCultureInfo("en"), "Action_copy");
                var chinese = UiText.Get(System.Globalization.CultureInfo.GetCultureInfo("zh-Hant"), "Action_copy");
                Environment.ExitCode = english == "Copy" && chinese == "複製"
                    && System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "zh-Hant", "SnapBar.App.resources.dll")) ? 0 : 1;
            }
            catch (Exception error) when (error is System.Resources.MissingManifestResourceException or System.IO.IOException) { Environment.ExitCode = 1; }
            return;
        }
        bool fixture = args.Contains("--fixture");
        if (fixture)
        {
            var fixtureApp = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            fixtureApp.Run(new FixtureWindow(args.Contains("--delayed-uia"), args.Contains("--deep-uia"),
                args.Contains("--web-link") ? "link" : args.Contains("--web-search") ? "search" : args.Contains("--web-lookup") ? "lookup" : null,
                args.Contains("--path-fixture") ? System.IO.Path.Combine(AppContext.BaseDirectory, "README.md") : null));
            return;
        }
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\SnapBar.ShowSettings");
        using var instance = new Mutex(true, "Local\\SnapBar.P0", out var first);
        var background = args.Contains("--background");
        if (!first) { if (!background) showSettings.Set(); return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        var wait = ThreadPool.RegisterWaitForSingleObject(showSettings, (_, _) =>
        {
            if (!app.Dispatcher.HasShutdownStarted)
                app.Dispatcher.BeginInvoke(window.ShowSettings);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        app.SessionEnding += (_, _) => window.PrepareSessionEnd();
        try
        {
            if (background) { app.MainWindow = window; window.InitializeBackground(); app.Run(); }
            else app.Run(window);
        }
        finally { wait.Unregister(null); }
    }
}
