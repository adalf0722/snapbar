using SnapBar.Core;

namespace SnapBar.App;

internal sealed class AiServiceTestController(IAiTextClient client) : IDisposable
{
    internal const string Source = "SnapBar connection test";
    internal const string Instruction = "Reply with OK.";
    private CancellationTokenSource? request;
    private int revision;
    private bool disposed;
    internal bool Busy => request != null;
    internal string Status { get; private set; } = "untested";
    internal event Action? Changed;

    internal void Reset()
    {
        revision++;
        var previous = request;
        request = null;
        Status = "untested";
        previous?.Cancel();
        if (!disposed) Changed?.Invoke();
    }

    internal void Cancel()
    {
        Reset();
    }

    internal async Task StartAsync(AiProviderProfile profile, string key)
    {
        if (disposed || Busy) return;
        var currentRevision = ++revision;
        using var cancellation = new CancellationTokenSource();
        request = cancellation;
        Status = "testing";
        Changed?.Invoke();
        try
        {
            var result = await client.ExecuteAsync(profile, key, Source, Instruction,
                cancellation.Token).WaitAsync(cancellation.Token);
            if (!disposed && currentRevision == revision)
                Status = result.Success ? "success" : result.Status;
        }
        catch (OperationCanceledException)
        {
            if (!disposed && currentRevision == revision) Status = "untested";
        }
        catch (Exception error) when (error is InvalidOperationException
            or System.IO.IOException or System.Net.Http.HttpRequestException
            or ObjectDisposedException)
        {
            if (!disposed && currentRevision == revision) Status = "network";
        }
        finally
        {
            if (!disposed && currentRevision == revision)
            {
                request = null;
                Changed?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        disposed = true;
        Reset();
        Changed = null;
    }
}
