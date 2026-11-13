using System.IO;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed record OnboardingSearchResult(string SelectionId, TargetIdentity Target, bool Launched);

internal sealed class OnboardingCoordinator(MainWindow host, OnboardingStore store)
{
    private OnboardingPracticeSession? session;
    private bool starting, stopping, completed;
    private readonly HashSet<string> successfulSelections = [];
    internal bool Active => session != null || starting;
    internal TargetIdentity? Target => session?.Target;
    internal bool Completed => completed;
    internal string? ProgressError { get; private set; }

    private void Save(OnboardingStatus status)
    {
        try { store.Save(new(1, status)); ProgressError = null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ProgressError = "progress-unavailable"; }
    }

    internal async Task OpenAsync()
    {
        if (starting || stopping) return;
        if (session is { Connected: true }) { session.Activate(); return; }
        starting = true;
        var current = new OnboardingPracticeSession();
        session = current; completed = false; successfulSelections.Clear();
        try
        {
            await current.StartAsync();
            if (stopping || session != current) return;
            Save(OnboardingStatus.Presented);
            current.Message += message => ReceiveAsync(current, message);
            current.Disconnected += () => _ = EndAsync(current, showSettings: true, failed: true);
            await current.SendAsync(host.OnboardingConfiguration(ProgressError));
            host.OnboardingShown();
            current.Listen();
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        { await EndAsync(current, showSettings: true, failed: true); }
        finally { starting = false; }
    }

    private async Task ReceiveAsync(OnboardingPracticeSession source, OnboardingMessage message)
    {
        if (session != source || stopping) return;
        switch (message.Kind)
        {
            case "skip":
            case "finish":
                if (!completed) Save(OnboardingStatus.Skipped);
                await EndAsync(source, showSettings: false);
                break;
            case "open-settings": host.OnboardingShowSettings(false); break;
            case "change-shortcut": host.OnboardingShowSettings(true); break;
            case "set-language":
            case "set-automatic":
                var saved = host.ApplyOnboardingPreference(message);
                var configuration = host.OnboardingConfiguration();
                await source.SendAsync(new(1, "preference-result", Language: configuration.Language,
                    Automatic: configuration.Automatic, RequestId: message.RequestId, Error: saved ? null : "save-failed"));
                break;
        }
    }

    internal async Task ConfigureAsync()
    {
        var current = session;
        if (current?.Connected != true || stopping) return;
        try { await current.SendAsync(host.OnboardingConfiguration(ProgressError)); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        { await EndAsync(current, true, true); }
    }

    internal async Task SearchFinishedAsync(OnboardingSearchResult result)
    {
        var current = session;
        if (current?.Connected != true || stopping || result.Target != current.Target) return;
        if (result.Launched)
        {
            if (!successfulSelections.Add(result.SelectionId)) return;
            completed = true; Save(OnboardingStatus.Completed);
        }
        try
        {
            await current.SendAsync(new(1, result.Launched ? "search-launched" : "search-failed",
                Error: result.Launched ? null : "browser-unavailable"));
            if (ProgressError != null) await ConfigureAsync();
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        { await EndAsync(current, true, true); }
    }

    private async Task EndAsync(OnboardingPracticeSession source, bool showSettings, bool failed = false)
    {
        if (session != source) return;
        session = null;
        await source.StopAsync();
        if (!stopping) host.OnboardingEnded(showSettings, failed, ProgressError != null);
    }

    internal async Task StopAsync()
    {
        stopping = true;
        if (session is { } current) await EndAsync(current, false);
    }
}
