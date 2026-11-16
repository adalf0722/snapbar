using SnapBar.Core;

internal static class SelectionCapturePipelineTest
{
    internal static void Run(Action<string, Func<bool>> check, TargetIdentity target, SelectionSnapshot selection)
    {
        SelectionCapturePipeline Pipeline(AutomaticClipboardBackoff? backoff = null) =>
            new(new NativeSelectionCapture(wait: (_, _) => throw new Exception("Unexpected readiness delay")), backoff);
        var unsupported = new ProbeResponse("unsupported", ClipboardTarget: new("candidate", target));
        var copied = new ProbeResponse("ok", selection with { Source = "clipboard", Editable = null });
        SelectionCaptureResult Capture(SelectionCapturePipeline pipeline, ProbeResponse native,
            Func<ProbeResponse, ClipboardFallback?> resolver, bool automatic = true, Func<bool>? current = null) =>
            pipeline.CaptureAsync(target, automatic, _ => Task.FromResult(native), current ?? (() => true),
                resolver, false, default).GetAwaiter().GetResult();

        check("unified capture returns UIA success without consulting any clipboard adapter", () =>
        {
            var native = new ProbeResponse("ok", selection);
            var result = Capture(Pipeline(), native, _ => throw new Exception("Clipboard adapter was consulted"));
            return result.Response == native && !result.ClipboardAttempted && result.Native.Attempts == 1;
        });
        foreach (var status in new[] { "no-selection", "password", "timeout", "focus-changed", "target-changed", "provider-error", "busy" })
            check($"unified capture never copies after native {status}", () =>
            {
                var result = Capture(Pipeline(), new(status), _ => throw new Exception("Terminal state reached clipboard adapter"));
                return result.Response.Status == status && !result.ClipboardAttempted;
            });
        check("unified capture executes one verified fallback and skips readiness delays", () =>
        {
            int copies = 0;
            var result = Pipeline().CaptureAsync(target, true, _ => Task.FromResult(unsupported), () => true,
                _ => new(target, () => { copies++; return Task.FromResult(copied); }), true, default).GetAwaiter().GetResult();
            return copies == 1 && result.Native.Attempts == 1 && result.Response == copied && result.ClipboardAttempted;
        });
        check("unified capture rejects a clipboard plan for another target", () =>
        {
            var result = Capture(Pipeline(), unsupported, _ => new(target with { Window = 99 },
                () => throw new Exception("Foreign plan copied")));
            return result.Response.Status == "unsupported" && !result.ClipboardAttempted;
        });
        check("unified capture respects disabled or unavailable compatibility", () =>
        {
            var result = Capture(Pipeline(), unsupported, _ => null);
            return result.Response.Status == "unsupported" && !result.ClipboardAttempted;
        });
        check("unified capture discards late native success before consulting clipboard", () =>
        {
            bool current = true;
            var result = Pipeline().CaptureAsync(target, true,
                _ => { current = false; return Task.FromResult(new ProbeResponse("ok", selection)); }, () => current,
                _ => throw new Exception("Stale capture reached adapter"), false, default).GetAwaiter().GetResult();
            return result.Response.Status == "target-changed" && !result.ClipboardAttempted;
        });
        check("unified capture rejects native text belonging to another window", () =>
            Capture(Pipeline(), new("ok", selection with { Target = target with { Window = 99 } }),
                _ => throw new Exception("Invalid native result reached clipboard")).Response.Status == "target-changed");
        check("unified capture rejects canceled or foreign clipboard results", () =>
        {
            bool current = true;
            var stale = Capture(Pipeline(), unsupported, _ => new(target,
                () => { current = false; return Task.FromResult(copied); }), current: () => current);
            var foreign = Capture(Pipeline(), unsupported, _ => new(target,
                () => Task.FromResult(copied with { Selection = copied.Selection! with { Target = target with { Window = 99 } } })));
            return stale.Response.Status == "target-changed" && foreign.Response.Status == "target-changed";
        });
        check("unified backoff suppresses automatic copying while native and manual reads remain available", () =>
        {
            var backoff = new AutomaticClipboardBackoff(); int copies = 0;
            backoff.Observe(target, "no-selection", true); backoff.Observe(target, "no-selection", true);
            var pipeline = Pipeline(backoff);
            ClipboardFallback Plan(ProbeResponse _) => new(target, () => { copies++; return Task.FromResult(copied); });
            var throttled = Capture(pipeline, unsupported, Plan);
            var native = Capture(pipeline, new("ok", selection), _ => throw new Exception("Native success consulted adapter"));
            var manual = Capture(pipeline, unsupported, Plan, automatic: false);
            return throttled.BackedOff && !throttled.ClipboardAttempted && native.Response.Success
                && manual.Response.Success && copies == 1 && backoff.CanAttempt(target);
        });
        check("unknown application with focused MSAA text can use native clipboard validation", () =>
            NativeClipboardPolicy.Check(true, true, false, 42, 4, 42, 4) == "ok"
            && NativeClipboardPolicy.Check(true, true, false, 15, 4, 15, 4) == "ok");
        check("unknown opaque source and missing focus states never authorize clipboard reads", () =>
            NativeClipboardPolicy.Check(true, true, false, null, null, null, null) == "clipboard-unavailable"
            && NativeClipboardPolicy.Check(true, true, false, 42, null, 42, 4) == "clipboard-unavailable"
            && NativeClipboardPolicy.Check(true, true, false, 42, 0, 42, 4) == "clipboard-unavailable"
            && NativeClipboardPolicy.Check(true, false, false, 42, 4, 42, 4) == "clipboard-unavailable"
            && NativeClipboardPolicy.Check(false, true, false, 42, 4, 42, 4) == "clipboard-unavailable");
        check("unknown native source rejects password hidden and non-text metadata", () =>
            NativeClipboardPolicy.Check(true, true, true, 42, 4, 42, 4) == "password"
            && NativeClipboardPolicy.Check(true, true, false, 42, 0x20000004, 42, 4) == "password"
            && NativeClipboardPolicy.Check(true, true, false, 42, 4, 42, 0x8004) == "clipboard-unavailable"
            && NativeClipboardPolicy.Check(true, true, false, 43, 4, 43, 4) == "clipboard-unavailable");
    }
}
