using SnapBar.Core;

int passed = 0, failed = 0;
void Check(string name, Func<bool> test)
{
    try { if (!test()) throw new Exception("Assertion failed"); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception e) { Console.WriteLine($"FAIL {name}: {e.Message}"); failed++; }
}
var target = new TargetIdentity(42, 123, 999);
var selection = new SelectionSnapshot("s", target, "original", null, true, 1, 8);
Check("valid editable selection allows write", () => SessionPolicy.CanWrite(selection, target));
Check("unknown editability denies write", () => !SessionPolicy.CanWrite(selection with { Editable = null }, target));
Check("read-only denies write", () => !SessionPolicy.CanWrite(selection with { Editable = false }, target));
Check("disjoint ranges deny write", () => !SessionPolicy.CanWrite(selection with { RangeCount = 2 }, target));
Check("empty selection denies write", () => !SessionPolicy.CanWrite(selection with { Text = "" }, target));
Check("different foreground denies write", () => !SessionPolicy.CanWrite(selection, target with { Window = 43 }));
Check("reused PID denies write", () => !SessionPolicy.CanWrite(selection, target with { ProcessStarted = 1000 }));
Check("different PID denies write", () => !SessionPolicy.CanWrite(selection, target with { ProcessId = 124 }));
Check("no session denies write", () => !SessionPolicy.CanWrite(null, target));
Check("unfocused editable provider cannot receive input", () => SessionPolicy.ResolveEditability(false, true, false, false) == null);
Check("unsupported read-only attribute remains unknown", () => SessionPolicy.ResolveEditability(null, true, true, false) == null);
Check("ValuePattern read-only overrides TextPattern", () => SessionPolicy.ResolveEditability(false, true, true, true) == false);
Check("disabled control cannot receive input", () => SessionPolicy.ResolveEditability(false, false, true, false) == false);
Check("focused enabled editable provider can receive input", () => SessionPolicy.ResolveEditability(false, true, true, false) == true);
Check("invalidated requests cannot show popup", () =>
{
    var gate = new GenerationGate(); var old = gate.Next(); gate.Next(); return !gate.IsCurrent(old);
});
Check("latest request remains valid", () => { var gate = new GenerationGate(); return gate.IsCurrent(gate.Next()); });
Check("bottom-right popup stays within work area", () =>
{
    var p = PopupPlacement.Place(new(1900, 1060, 10, 10), new(0, 0, 1920, 1080), 300, 54);
    return p.X >= 0 && p.Right <= 1920 && p.Bottom <= 1080 && p.Y >= 0;
});
Check("negative-coordinate monitor is preserved", () =>
{
    var p = PopupPlacement.Place(new(-1800, 100, 20, 20), new(-1920, 0, 1920, 1080), 300, 54);
    return p.X < 0 && p.X >= -1920 && p.Right <= 0;
});
Check("oversized popup is clamped", () =>
{
    var p = PopupPlacement.Place(new(10, 10, 1, 1), new(0, 0, 100, 50), 300, 80);
    return p.Width == 100 && p.Height == 50 && p.X == 0 && p.Y == 0;
});
Check("popup prefers below selection", () => PopupPlacement.Place(new(100, 100, 10, 20), new(0, 0, 1000, 800), 200, 50).Y == 128);
Check("popup flips above bottom selection", () => PopupPlacement.Place(new(100, 770, 10, 20), new(0, 0, 1000, 800), 200, 50).Y == 712);
Check("Sublime binding installs without plugin", () => SublimeKeymap.IsReady(SublimeKeymap.Install("[]")));
Check("Sublime binding install is idempotent", () =>
{
    var text = SublimeKeymap.Install("[]"); return SublimeKeymap.Install(text) == text;
});
Check("Sublime existing comments and trailing comma survive", () =>
{
    const string text = "// 開頭註解\n[\n// Keep this comment\n{\"keys\":[\"ctrl+k\"],\"command\":\"noop\"},\n] // end";
    var updated = SublimeKeymap.Install(text);
    return updated.Contains("// Keep this comment") && updated.StartsWith("// 開頭註解")
        && updated.EndsWith("] // end") && SublimeKeymap.IsReady(updated);
});
Check("Sublime shortcut conflicts are rejected", () =>
{
    try { SublimeKeymap.Install("[{\"keys\":[\"ctrl+alt+shift+f12\"],\"command\":\"cut\"}]"); return false; }
    catch (InvalidOperationException) { return true; }
});
Check("weakened empty-selection guard is rejected", () =>
    !SublimeKeymap.IsReady(SublimeKeymap.Install("[]").Replace("\"selection_empty\", \"operand\": false", "\"selection_empty\", \"operand\": true")));
Check("Sublime multiple-selection guard is required", () =>
    !SublimeKeymap.IsReady(SublimeKeymap.Install("[]").Replace("\"num_selections\", \"operand\": 1", "\"num_selections\", \"operand\": 2")));
Check("Sublime changed binding order is rejected", () =>
{
    var nodes = System.Text.Json.Nodes.JsonNode.Parse(SublimeKeymap.Install("[]"), documentOptions:
        new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.AsArray();
    var reversed = new System.Text.Json.Nodes.JsonArray(nodes[1]!.DeepClone(), nodes[0]!.DeepClone());
    return !SublimeKeymap.IsReady(reversed.ToJsonString());
});
Check("clipboard fallback with unknown editability denies write", () =>
    !SessionPolicy.CanWrite(selection with { Source = "sublime-clipboard", Editable = null }, target));
Check("ownerless Sublime copy is accepted with stable data and focus", () =>
    ClipboardCapturePolicy.Evaluate(true, true, false, 0, 123, true, true) == "ok");
Check("unchanged clipboard is never treated as selection", () =>
    ClipboardCapturePolicy.Evaluate(false, true, false, 0, 123, true, true) == "pending");
Check("another known producer is preserved", () =>
    ClipboardCapturePolicy.Evaluate(true, true, true, 999, 123, true, true) == "clipboard-other-owner");
Check("concurrent clipboard sequence change is preserved", () =>
    ClipboardCapturePolicy.Evaluate(true, false, false, 0, 123, true, true) == "clipboard-changed");
Check("ownerless copy cannot bypass focus validation", () =>
    ClipboardCapturePolicy.Evaluate(true, true, false, 0, 123, false, true) == "target-changed");
Check("ownerless copy cannot bypass input validation", () =>
    ClipboardCapturePolicy.Evaluate(true, true, false, 0, 123, true, false) == "clipboard-input-changed");
Check("target-owned copy is accepted", () =>
    ClipboardCapturePolicy.Evaluate(true, true, true, 123, 123, true, true) == "ok");
foreach (var status in new[] { "ok", "no-selection", "password", "timeout", "target-changed", "provider-error", "busy" })
    Check($"{status} never triggers clipboard fallback", () =>
        !SelectionRoutingPolicy.UseClipboardFallback(new(status), true, true));
Check("unsupported verified enabled target can use fallback", () =>
    SelectionRoutingPolicy.UseClipboardFallback(new("unsupported"), true, true));
Check("unverified or disabled target cannot use fallback", () =>
    !SelectionRoutingPolicy.UseClipboardFallback(new("unsupported"), false, true)
    && !SelectionRoutingPolicy.UseClipboardFallback(new("unsupported"), true, false));
Check("paste requires original or restored clipboard sequence", () =>
    ClipboardActionPolicy.IsUnchanged(12, 12) && !ClipboardActionPolicy.IsUnchanged(12, 13));
Check("verified editable fallback can write", () =>
    SessionPolicy.CanWrite(selection with { Source = "sublime-clipboard" }, target));
Check("Sublime v1 upgrade preserves original text exactly", () =>
{
    const string legacy = """
    // Original user settings
    [
    { "keys": ["ctrl+alt+shift+f12"], "command": "noop" },
    { "keys": ["ctrl+alt+shift+f12"], "command": "copy", "context": [
        { "key": "selection_empty", "operand": false, "match_all": true },
        { "key": "num_selections", "operand": 1 },
        { "key": "panel_has_focus", "operand": false },
        { "key": "overlay_visible", "operand": false }
    ] },
    // User trailing comment
    { "keys": ["ctrl+k"], "command": "noop" },
    ]
    """;
    var upgraded = SublimeKeymap.Install(legacy);
    var insertStart = legacy.IndexOf('[') + 1;
    return SublimeKeymap.IsReady(upgraded) && SublimeKeymap.Install(upgraded) == upgraded
        && upgraded.Remove(insertStart, upgraded.Length - legacy.Length) == legacy;
});
foreach (var chord in new[] { SublimeKeymap.EditableChord, SublimeKeymap.CutChord, SublimeKeymap.PasteChord })
{
    Check($"reserved {chord} conflicts are rejected", () =>
    {
        try { SublimeKeymap.Install($$"""[{"keys":["{{chord}}"],"command":"noop"}]"""); return false; }
        catch (InvalidOperationException) { return true; }
    });
}
Check("weakening any write guard disables compatibility", () =>
    !SublimeKeymap.IsReady(SublimeKeymap.Install("[]").Replace("\"read_only\", \"operator\": \"not_equal\"", "\"read_only\", \"operator\": \"equal\"")));
Check("panel guards cannot be weakened", () =>
    !SublimeKeymap.IsReady(SublimeKeymap.Install("[]").Replace("\"panel_has_focus\", \"operand\": false", "\"panel_has_focus\", \"operand\": true")));
Check("duplicate guarded binding disables compatibility", () =>
{
    var text = SublimeKeymap.Install("[]");
    return !SublimeKeymap.IsReady(text.Insert(text.IndexOf('[') + 1,
        "{\"keys\":[\"ctrl+alt+shift+f10\"],\"command\":\"cut\"},"));
});
foreach (var alias in new[] { "shift+alt+control+f10", "SHIFT + primary + ALT + f9" })
    Check($"equivalent shortcut {alias} conflicts are rejected", () =>
    {
        try { SublimeKeymap.Install($$"""[{"keys":["{{alias}}"],"command":"noop"}]"""); return false; }
        catch (InvalidOperationException) { return true; }
    });
Check("native success has no retry delay", () =>
{
    var clock = new ManualTimeProvider();
    var capture = new NativeSelectionCapture(clock, (_, _) => throw new Exception("Unexpected wait"));
    var result = capture.CaptureAsync(_ => Task.FromResult(new ProbeResponse("ok", selection, 5)),
        () => true, true, default).GetAwaiter().GetResult();
    return result.Response.Selection == selection && result.Attempts == 1 && result.ProbeElapsedMs == 5;
});
Check("lazy native provider succeeds within same capture", () =>
{
    var clock = new ManualTimeProvider(); int reads = 0, notices = 0;
    var result = new NativeSelectionCapture(clock, clock.Wait).CaptureAsync(
        _ => Task.FromResult(++reads < 3 ? new ProbeResponse("unsupported", ElapsedMs: 4) : new("ok", selection, 4)),
        () => true, true, default, () => notices++).GetAwaiter().GetResult();
    return result.Response.Selection == selection && reads == 3 && notices == 1 && result.ProbeElapsedMs == 12;
});
foreach (var terminal in new[] { "no-selection", "password", "timeout", "target-changed", "provider-error", "busy", "helper-exited" })
    Check($"native {terminal} stops without retry", () =>
    {
        var clock = new ManualTimeProvider();
        var capture = new NativeSelectionCapture(clock, (_, _) => throw new Exception("Unexpected retry"));
        var result = capture.CaptureAsync(_ => Task.FromResult(new ProbeResponse(terminal)),
            () => true, true, default).GetAwaiter().GetResult();
        return result.Response.Status == terminal && result.Attempts == 1;
    });
Check("unsupported native retries are bounded", () =>
{
    var clock = new ManualTimeProvider();
    var result = new NativeSelectionCapture(clock, clock.Wait).CaptureAsync(
        _ => Task.FromResult(new ProbeResponse("unsupported")), () => true, true, default).GetAwaiter().GetResult();
    return result.Response.Status == "unsupported" && result.Attempts == 4
        && clock.GetElapsedTime(0) == TimeSpan.FromMilliseconds(870);
});
Check("Sublime fallback does not incur native readiness waits", () =>
{
    var capture = new NativeSelectionCapture(wait: (_, _) => throw new Exception("Unexpected wait"));
    var result = capture.CaptureAsync(_ => Task.FromResult(new ProbeResponse("unsupported")),
        () => true, false, default).GetAwaiter().GetResult();
    return result.Attempts == 1 && SelectionRoutingPolicy.UseClipboardFallback(result.Response, true, true);
});
Check("invalid target is never queried", () =>
{
    var result = new NativeSelectionCapture().CaptureAsync(_ => throw new Exception("Unexpected read"),
        () => false, true, default).GetAwaiter().GetResult();
    return result.Response.Status == "target-changed" && result.Attempts == 0;
});
Check("cancelled capture is never queried", () =>
{
    var result = new NativeSelectionCapture().CaptureAsync(_ => throw new Exception("Unexpected read"),
        () => true, true, new CancellationToken(true)).GetAwaiter().GetResult();
    return result.Response.Status == "target-changed" && result.Attempts == 0;
});
Check("late native success is discarded after input invalidation", () =>
{
    bool current = true;
    var result = new NativeSelectionCapture().CaptureAsync(_ =>
        { current = false; return Task.FromResult(new ProbeResponse("ok", selection)); },
        () => current, true, default).GetAwaiter().GetResult();
    return result.Response.Status == "target-changed" && result.Response.Selection == null;
});
Check("foreground change during readiness wait stops next query", () =>
{
    bool current = true; int reads = 0;
    var capture = new NativeSelectionCapture(wait: (_, _) => { current = false; return Task.CompletedTask; });
    var result = capture.CaptureAsync(_ => { reads++; return Task.FromResult(new ProbeResponse("unsupported")); },
        () => current, true, default).GetAwaiter().GetResult();
    return result.Response.Status == "target-changed" && reads == 1;
});
Check("cancellation during readiness wait stops next query", () =>
{
    using var cancellation = new CancellationTokenSource();
    var capture = new NativeSelectionCapture(wait: (_, token) =>
        { cancellation.Cancel(); return Task.FromCanceled(token); });
    var result = capture.CaptureAsync(_ => Task.FromResult(new ProbeResponse("unsupported")),
        () => true, true, cancellation.Token).GetAwaiter().GetResult();
    return result.Response.Status == "target-changed" && result.Attempts == 1;
});
Check("slow unsupported query does not start retry past total budget", () =>
{
    var clock = new ManualTimeProvider();
    var result = new NativeSelectionCapture(clock, clock.Wait).CaptureAsync(_ =>
        { clock.Advance(TimeSpan.FromMilliseconds(1950)); return Task.FromResult(new ProbeResponse("unsupported")); },
        () => true, true, default).GetAwaiter().GetResult();
    return result.Attempts == 1 && result.Response.Status == "unsupported";
});
Check("next helper query receives remaining total budget", () =>
{
    var clock = new ManualTimeProvider(); int reads = 0; TimeSpan nextBudget = default;
    var result = new NativeSelectionCapture(clock, clock.Wait).CaptureAsync(remaining =>
    {
        if (++reads == 1) { clock.Advance(TimeSpan.FromMilliseconds(400)); return Task.FromResult(new ProbeResponse("unsupported")); }
        nextBudget = remaining; return Task.FromResult(new ProbeResponse("ok", selection));
    }, () => true, true, default).GetAwaiter().GetResult();
    return result.Response.Success && nextBudget == TimeSpan.FromMilliseconds(1480);
});
Check("native success past total deadline is rejected", () =>
{
    var clock = new ManualTimeProvider();
    var result = new NativeSelectionCapture(clock, clock.Wait).CaptureAsync(_ =>
        { clock.Advance(TimeSpan.FromMilliseconds(2100)); return Task.FromResult(new ProbeResponse("ok", selection)); },
        () => true, true, default).GetAwaiter().GetResult();
    return result.Response.Status == "timeout" && result.Response.Selection == null;
});
SettingsStoreTests.Run(Check);
AppExclusionPolicyTests.Run(Check);
WebActionPolicyTests.Run(Check);
PathActionPolicyTests.Run(Check);
ActionCatalogTests.Run(Check);
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
