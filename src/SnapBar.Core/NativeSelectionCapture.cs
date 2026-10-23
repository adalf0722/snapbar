namespace SnapBar.Core;

public sealed record NativeCaptureResult(ProbeResponse Response, int Attempts, double ProbeElapsedMs);

// Only missing capability may be transient. Never retry an empty/sensitive/failed selection.
public sealed class NativeSelectionCapture(TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? wait = null)
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)];
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<NativeCaptureResult> CaptureAsync(Func<TimeSpan, Task<ProbeResponse>> read,
        Func<bool> isCurrent, bool retryUnsupported, CancellationToken cancellationToken,
        Action? waitingForProvider = null)
    {
        var started = clock.GetTimestamp();
        int attempts = 0;
        double elapsed = 0;
        ProbeResponse response = new("unsupported");
        bool Current() => !cancellationToken.IsCancellationRequested && isCurrent();
        NativeCaptureResult Invalid() => new(new("target-changed"), attempts, elapsed);
        while (true)
        {
            if (!Current()) return Invalid();
            var remaining = Budget - clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return new(response, attempts, elapsed);
            response = await read(remaining);
            attempts++;
            elapsed += response.ElapsedMs;
            if (!Current()) return Invalid();
            if (clock.GetElapsedTime(started) >= Budget)
                return new(new("timeout"), attempts, elapsed);
            if (!retryUnsupported || response.Status != "unsupported" || attempts > RetryDelays.Length)
                return new(response, attempts, elapsed);
            var pause = RetryDelays[attempts - 1];
            if (Budget - clock.GetElapsedTime(started) <= pause) return new(response, attempts, elapsed);
            if (attempts == 1) waitingForProvider?.Invoke();
            try { await (wait?.Invoke(pause, cancellationToken) ?? Task.Delay(pause, cancellationToken)); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Invalid(); }
        }
    }
}
