namespace SnapBar.Core;

/// <summary>Monotonic lifetime of a verified clipboard candidate. Focus validation belongs to Probe.</summary>
public sealed class ClipboardCandidateLease(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private string? id;
    private long started;
    private TimeSpan duration;

    public void Clear() => id = null;

    public void Start(string sessionId)
    {
        id = sessionId;
        started = clock.GetTimestamp();
        duration = TimeSpan.FromSeconds(30);
    }

    public bool IsValid(string? sessionId) => id != null && id == sessionId
        && clock.GetElapsedTime(started) <= duration;

    public bool TryExtend(string? sessionId, int seconds)
    {
        if (seconds is < 1 or > 310 || !IsValid(sessionId)) return false;
        var requested = clock.GetElapsedTime(started) + TimeSpan.FromSeconds(seconds);
        if (requested > duration) duration = requested;
        return true;
    }

    public bool TryRelease(string? sessionId)
    {
        if (id == null || id != sessionId) return false;
        // Cleanup only shortens: expired candidates stay expired.
        var requested = clock.GetElapsedTime(started) + TimeSpan.FromSeconds(30);
        if (requested < duration) duration = requested;
        return true;
    }
}
