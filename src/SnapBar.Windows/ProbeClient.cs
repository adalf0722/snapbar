using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using SnapBar.Core;

namespace SnapBar.Windows;

public sealed class ProbeClient : IDisposable
{
    private readonly SemaphoreSlim singleRequest = new(1);
    private readonly string executable;
    private readonly bool pathWorker;
    private Process? process;
    private bool disposed;
    public ProbeClient(string executable, bool pathWorker = false)
    { this.executable = executable; this.pathWorker = pathWorker; }

    public async Task<ProbeResponse> RequestAsync(ProbeRequest request, TimeSpan? remainingBudget = null,
        CancellationToken cancellationToken = default)
    {
        if (!await singleRequest.WaitAsync(0)) return new("busy");
        try
        {
            if (disposed) return new("closed");
            if (cancellationToken.IsCancellationRequested) return new("cancelled");
            EnsureStarted();
            var limit = TimeSpan.FromMilliseconds(1500);
            if (remainingBudget is { } remaining && remaining < limit) limit = remaining;
            if (limit <= TimeSpan.Zero) return new("timeout");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(limit);
            var current = process!;
            await current.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
            await current.StandardInput.FlushAsync(timeout.Token);
            var response = await current.StandardOutput.ReadLineAsync(timeout.Token);
            if (response == null) { Stop(); return new("helper-exited"); }
            return JsonSerializer.Deserialize<ProbeResponse>(response) ?? new("invalid-response");
        }
        catch (OperationCanceledException) { Stop(); return new(cancellationToken.IsCancellationRequested ? "cancelled" : "timeout"); }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Stop(); return new("helper-unavailable"); }
        finally { singleRequest.Release(); }
    }

    private void EnsureStarted()
    {
        if (process is { HasExited: false }) return;
        Stop();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        if (pathWorker) start.ArgumentList.Add("--path-worker");
        process = Process.Start(start) ?? throw new InvalidOperationException();
    }

    private void Stop()
    {
        var old = process; process = null;
        if (old == null) return;
        // A path worker can hand work to Explorer. Never terminate Explorer as
        // part of cancelling the worker after the Shell has accepted a request.
        try { if (!old.HasExited) old.Kill(entireProcessTree: !pathWorker); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { old.Dispose(); }
    }

    public void Dispose() { disposed = true; Stop(); }
}
