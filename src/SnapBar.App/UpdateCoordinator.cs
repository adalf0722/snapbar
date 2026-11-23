using System.IO;
using System.Net.Http;
using System.Text.Json;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

// All entry points and callbacks run on the owning UI dispatcher.
internal sealed class UpdateCoordinator : IDisposable
{
    private readonly ProductVersion? current;
    private readonly UpdateStateStore store;
    private readonly Func<CancellationToken, Task<IReadOnlyList<UpdateRelease>>> read;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<bool> busy;
    private readonly Action<UpdateRelease> notify;
    private readonly DateTimeOffset startAfter;
    private CancellationTokenSource? request;
    private Task? active;
    private long generation;
    private bool disposed;
    private bool manualInterest;
    internal UpdateState State { get; private set; }
    internal bool Supported => current?.IsPublic == true;
    internal bool StorageAvailable { get; private set; }
    internal bool Checking { get; private set; }
    internal string Status { get; private set; } = "Updates_NotChecked";
    internal event Action? Changed;
    internal bool CanCheck => Supported && !Checking && (NextManualUtc == null || now() >= NextManualUtc);
    internal DateTimeOffset? NextManualUtc
    {
        get
        {
            var next = State.LastAttemptUtc is { } attempt
                ? attempt > DateTimeOffset.MaxValue.AddSeconds(-60) ? DateTimeOffset.MaxValue : attempt.AddSeconds(60)
                : (DateTimeOffset?)null;
            return State.RetryAtUtc > next || next == null ? State.RetryAtUtc ?? next : next;
        }
    }

    internal UpdateCoordinator(string version, UpdateStateStore store,
        Func<CancellationToken, Task<IReadOnlyList<UpdateRelease>>> read, Func<bool> busy,
        Action<UpdateRelease> notify, Func<DateTimeOffset>? now = null)
    {
        try { current = ProductVersion.Parse(version); } catch (FormatException) { }
        this.store = store; this.read = read; this.busy = busy; this.notify = notify;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        startAfter = this.now().AddSeconds(30);
        State = store.Load(); StorageAvailable = store.Writable;
        if (!Supported) { State = State with { Latest = null, Notification = null }; Status = "Updates_Unsupported"; }
        else if (!StorageAvailable) Status = "Updates_StorageUnavailable";
        else
        {
            var cached = new[] { State.Latest, State.Notification }.OfType<UpdateRelease>().DistinctBy(item => item.Version);
            var eligible = UpdatePolicy.Select(current!, cached, State.NotifiedVersions);
            State = State with { Latest = eligible.Latest, Notification = eligible.Notification };
            if (State.Latest != null) Status = "Updates_Available";
        }
    }

    internal void SetEnabled(bool enabled)
    {
        if (disposed || !Supported || !StorageAvailable) return;
        generation++; request?.Cancel();
        Status = State.Latest != null ? "Updates_Available" : State.LastSuccessUtc != null ? "Updates_Current" : "Updates_NotChecked";
        Persist(State with { Enabled = enabled });
        Changed?.Invoke();
    }

    internal async Task TickAsync()
    {
        if (disposed) return;
        if (!Supported || !StorageAvailable || !State.Enabled || now() < startAfter) { Changed?.Invoke(); return; }
        if (!Checking && (State.LastAttemptUtc == null || now() - State.LastAttemptUtc >= TimeSpan.FromHours(24))
            && (State.RetryAtUtc == null || now() >= State.RetryAtUtc)) await CheckAsync(false);
        TryNotify();
        if (!disposed) Changed?.Invoke(); // Refresh cooldown text even without a new result.
    }

    internal Task CheckAsync(bool manual = true)
    {
        if (disposed || !Supported) return Task.CompletedTask;
        if (Checking) { manualInterest |= manual; return active ?? Task.CompletedTask; }
        if (manual && NextManualUtc is { } next && now() < next) return Task.CompletedTask;
        active = RunAsync(manual);
        return active;
    }

    private async Task RunAsync(bool manual)
    {
        manualInterest = manual;
        var ticket = ++generation;
        using var cancellation = new CancellationTokenSource(); request = cancellation;
        Checking = true; Status = "Updates_Checking";
        Persist(State with { LastAttemptUtc = now() });
        Changed?.Invoke();
        try
        {
            var releases = await read(cancellation.Token);
            if (disposed || ticket != generation || cancellation.IsCancellationRequested) return;
            var selected = UpdatePolicy.Select(current!, releases, State.NotifiedVersions);
            var notified = State.NotifiedVersions;
            if (manualInterest && selected.Notification != null)
                notified = notified.Append(selected.Notification.Version).Distinct(StringComparer.Ordinal)
                    .OrderByDescending(ProductVersion.Parse).Take(64).ToArray();
            Persist(State with { Latest = selected.Latest, Notification = selected.Notification,
                NotifiedVersions = notified, LastSuccessUtc = now(), CandidatesFetchedAtUtc = now(), RetryAtUtc = null });
            Status = selected.Latest == null ? "Updates_Current" : "Updates_Available";
        }
        catch (OperationCanceledException)
        { if (!disposed && ticket == generation) Status = "Updates_Failed"; }
        catch (UpdateRateLimitException error)
        {
            if (!disposed && ticket == generation)
            { Persist(State with { RetryAtUtc = error.RetryAt }); Status = "Updates_Failed"; }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException
            or FormatException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        { if (!disposed && ticket == generation) Status = "Updates_Failed"; }
        finally
        {
            if (ReferenceEquals(request, cancellation)) request = null;
            Checking = false;
            if (!disposed) Changed?.Invoke();
        }
    }

    private void TryNotify()
    {
        if (disposed || Checking || !StorageAvailable || !State.Enabled || State.Notification is not { } candidate
            || State.NotifiedVersions.Contains(candidate.Version, StringComparer.Ordinal) || busy()) return;
        var notified = State.NotifiedVersions.Append(candidate.Version).Distinct(StringComparer.Ordinal)
            .OrderByDescending(ProductVersion.Parse).Take(64).ToArray();
        if (!Persist(State with { NotifiedVersions = notified })) return;
        notify(candidate);
    }

    private bool Persist(UpdateState state)
    {
        State = state;
        if (StorageAvailable && store.Save(state)) return true;
        StorageAvailable = false;
        State = State with { Enabled = false };
        return false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; generation++; request?.Cancel(); Changed = null;
    }
}
