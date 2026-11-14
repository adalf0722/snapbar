using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed partial class OnboardingPracticeWindow : Window
{
    private readonly Func<OnboardingMessage, Task> send;
    private OnboardingMessage configuration = new(1, "configure", Language: "en", Shortcut: "Ctrl + Alt + V",
        Automatic: false, HotkeyRegistered: true);
    private bool completed, updating, closing;
    private long request, pending;
    internal bool Completed => completed;

    internal OnboardingPracticeWindow(Func<OnboardingMessage, Task> send)
    {
        this.send = send;
        ActionUi.Setup(this, "SnapBar", 660, 590);
        WindowWorkArea.Attach(this);
        Closing += (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            _ = RequestAsync(new(1, completed ? "finish" : "skip"));
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true; Close();
        };
    }

    internal void Receive(OnboardingMessage message)
    {
        if (message.Kind == "close") { CloseFromParent(); return; }
        if (message.Kind == "configure")
        {
            configuration = message;
            Render();
            if (!IsVisible) { Show(); Activate(); }
        }
        else if (message.Kind == "search-launched")
        {
            completed = true;
            Render();
        }
        else if (message.Kind == "search-failed") Error("Onboarding_SearchFailed");
        else if (message.Kind == "preference-result" && message.RequestId == pending)
        {
            pending = 0;
            configuration = configuration with { Language = message.Language, Automatic = message.Automatic };
            Render();
            if (message.Error != null) Error("Onboarding_SaveFailed");
        }
    }

    private async Task PreferenceAsync(string kind, string? value = null, bool? automatic = null)
    {
        if (pending != 0) return;
        pending = ++request; SetPending();
        await RequestAsync(new(1, kind, Language: value, RequestId: pending, Automatic: automatic));
    }
    private async Task RequestAsync(OnboardingMessage message)
    {
        try { await send(message); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        { CloseFromParent(); }
    }
    internal void CloseFromParent() { closing = true; Close(); }

    internal static int Run(string pipeName)
    {
        if (!pipeName.StartsWith("SnapBar.Onboarding.", StringComparison.Ordinal)
            || !Guid.TryParseExact(pipeName[19..], "N", out _)) return 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var lifetime = new CancellationTokenSource();
            OnboardingPracticeWindow? window = null;
            try
            {
                await pipe.ConnectAsync(5000, lifetime.Token);
                using var channel = new OnboardingChannel(pipe, fromChild: false);
                window = new(message => channel.SendAsync(message, lifetime.Token));
                app.MainWindow = window;
                window.Closed += (_, _) => lifetime.Cancel();
                var handle = new WindowInteropHelper(window).EnsureHandle();
                await channel.SendAsync(new(1, "ready", Window: (long)handle), lifetime.Token);
                while (await channel.ReadAsync(lifetime.Token) is { } message) window.Receive(message);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException
                or System.Text.Json.JsonException or InvalidOperationException) { }
            finally { window?.CloseFromParent(); app.Shutdown(); }
        };
        return app.Run();
    }
}
