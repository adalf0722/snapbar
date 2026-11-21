using System.Diagnostics;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal readonly record struct SelectionDiagnosticEvent(long Timestamp, long Capture, string Stage, string Status,
    double ElapsedMs, string? Source, SelectionProbeDiagnostics? Probe);

internal sealed class SelectionDiagnostics
{
    internal static readonly SelectionDiagnostics Shared = new();
    private readonly SelectionDiagnosticEvent[] events = new SelectionDiagnosticEvent[128];
    private readonly object gate = new();
    private long written;
    private int recording;

    internal bool Enabled => Volatile.Read(ref recording) != 0;

    internal void Record(long capture, string stage, ProbeResponse response, double? elapsedMs = null)
    {
        var probe = response.Diagnostics is { NativeSource: not null } detail
            ? detail with { NativeSource = null } : response.Diagnostics;
        var source = response.Selection?.Source switch
        {
            "uia" => "uia",
            ClipboardSelectionAdapter.Source => "clipboard",
            "sublime-clipboard" => "sublime-clipboard",
            _ => null
        };
        Record(new(Stopwatch.GetTimestamp(), capture, stage, response.Status, elapsedMs ?? response.ElapsedMs,
            source, probe));
    }

    internal void RecordStatus(long capture, string stage, string status, double elapsedMs = 0) =>
        Record(new(Stopwatch.GetTimestamp(), capture, stage, status, elapsedMs, null, null));

    private void Record(SelectionDiagnosticEvent item)
    {
        if (!Monitor.TryEnter(gate)) return;
        try { events[written++ % events.Length] = item; }
        finally { Monitor.Exit(gate); }
    }

    internal SelectionDiagnosticEvent[] Snapshot()
    {
        lock (gate) return CopyAfter(0);
    }

    internal SelectionDiagnosticEvent[] TrySnapshot()
    {
        if (!Monitor.TryEnter(gate)) return [];
        try { return CopyAfter(0); }
        finally { Monitor.Exit(gate); }
    }

    internal async Task RecordReproductionAsync(SupportSession session, TimeSpan duration)
    {
        if (Interlocked.CompareExchange(ref recording, 1, 0) != 0) return;
        try
        {
            long start;
            lock (gate) start = written;
            var clock = Stopwatch.StartNew();
            var saved = start;
            while (duration - clock.Elapsed is { Ticks: > 0 } remaining)
            {
                await Task.Delay(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                SelectionDiagnosticEvent[] snapshot;
                lock (gate)
                {
                    if (written == saved) continue;
                    snapshot = CopyAfter(start);
                    saved = written;
                }
                session.SaveReproduction(snapshot);
            }
        }
        finally { Volatile.Write(ref recording, 0); }
    }

    private SelectionDiagnosticEvent[] CopyAfter(long start)
    {
        var first = Math.Max(start, written - events.Length);
        var copy = new SelectionDiagnosticEvent[written - first];
        for (var index = 0; index < copy.Length; index++)
            copy[index] = events[(first + index) % events.Length];
        return copy;
    }
}
