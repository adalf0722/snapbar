namespace SnapBar.Core;

// Only automatic clipboard attempts are throttled. Native reads and hotkeys remain available.
public sealed class AutomaticClipboardBackoff(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(10);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<TargetIdentity, (int Failures, long Started)> entries = [];

    public bool CanAttempt(TargetIdentity target)
    {
        if (!entries.TryGetValue(target, out var entry) || entry.Failures < 2) return true;
        if (clock.GetElapsedTime(entry.Started) < Pause) return false;
        entries.Remove(target); return true;
    }

    public void Observe(TargetIdentity target, string status, bool automatic)
    {
        if (status == "ok") { entries.Remove(target); return; }
        if (!automatic || status != "no-selection") return;
        if (!entries.ContainsKey(target) && entries.Count >= 64) entries.Remove(entries.Keys.First());
        entries.TryGetValue(target, out var entry);
        entries[target] = (Math.Min(2, entry.Failures + 1), clock.GetTimestamp());
    }
}
