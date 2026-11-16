namespace SnapBar.Core;

// Adapters provide verified sources and an executor. They do not own routing or retries.
public sealed record ClipboardFallback(TargetIdentity Target, Func<Task<ProbeResponse>> Capture,
    bool AutomaticBackoff = true);
public sealed record SelectionCaptureResult(NativeCaptureResult Native, ProbeResponse Response,
    bool ClipboardAttempted = false, bool BackedOff = false);

public sealed class SelectionCapturePipeline(NativeSelectionCapture? nativeReader = null,
    AutomaticClipboardBackoff? automaticBackoff = null)
{
    private readonly NativeSelectionCapture native = nativeReader ?? new();
    private readonly AutomaticClipboardBackoff backoff = automaticBackoff ?? new();

    public async Task<SelectionCaptureResult> CaptureAsync(TargetIdentity target, bool automatic,
        Func<TimeSpan, Task<ProbeResponse>> readNative, Func<bool> isCurrent,
        Func<ProbeResponse, ClipboardFallback?> resolveFallback, bool retryUnsupported,
        CancellationToken cancellationToken, Action? waitingForProvider = null)
    {
        bool Current() => !cancellationToken.IsCancellationRequested && isCurrent();
        ClipboardFallback? Resolve(ProbeResponse response)
        {
            if (!Current() || response.Status != "unsupported") return null;
            var plan = resolveFallback(response);
            return plan?.Target == target ? plan : null;
        }
        ProbeResponse Validate(ProbeResponse response) => !Current()
            || response.Success && response.Selection?.Target != target ? new("target-changed") : response;

        var captured = await native.CaptureAsync(readNative, Current, retryUnsupported, cancellationToken,
            waitingForProvider, response => Resolve(response) != null);
        var result = Validate(captured.Response);
        var fallback = Resolve(result);
        if (fallback == null) return new(captured, result);
        if (automatic && fallback.AutomaticBackoff && !backoff.CanAttempt(target))
            return new(captured, result, BackedOff: true);
        if (!Current()) return new(captured, new("target-changed"));
        result = Validate(await fallback.Capture());
        if (fallback.AutomaticBackoff) backoff.Observe(target, result.Status, automatic);
        return new(captured, result, ClipboardAttempted: true);
    }
}
