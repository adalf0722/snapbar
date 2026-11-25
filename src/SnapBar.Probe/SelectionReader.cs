using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.Probe;

internal sealed class SelectionReader
{
    private const int MaximumCharacters = 32768;
    private const int MaximumAncestorDepth = 64;
    private AutomationElement? element;
    private TextPatternRange[]? savedRanges;
    private SelectionSnapshot? saved;
    private int[]? focusedId;
    private ClipboardCaptureTarget? clipboardTarget;
    private int[]? clipboardFocusId;
    private NativeClipboardSource? clipboardNativeSource;
    private int clipboardX, clipboardY;
    private readonly ClipboardCandidateLease clipboardLease = new();
    internal SelectionProbeDiagnostics? Diagnostics { get; private set; }
    internal void BeginDiagnostics(bool enabled) => Diagnostics = enabled ? new() : null;
    private void Stage(string stage) { if (Diagnostics != null) Diagnostics = Diagnostics with { Stage = stage }; }

    public ProbeResponse Capture(ProbeRequest request)
    {
        Stage("capture-focus");
        saved = null; savedRanges = null; element = null; focusedId = null; clipboardTarget = null; clipboardFocusId = null;
        clipboardNativeSource = null;
        clipboardLease.Clear();
        var target = Native.GetIdentity((nint)request.Window);
        if (target == null || Native.GetForegroundWindow() != (nint)request.Window) return new("target-changed");
        var focused = AutomationElement.FocusedElement;
        if (focused == null) return MissingCapability(request, target);
        if (IsPassword(focused)) return new("password");
        if (Diagnostics != null) Diagnostics = Diagnostics with { FocusType = focused.Current.ControlType.Id };
        var candidates = new List<AutomationElement>();
        AddAncestors(candidates, focused, request.Window);
        if (Diagnostics != null) Diagnostics = Diagnostics with { FocusCandidates = candidates.Count };
        if (request.UsePointer)
        {
            var pointer = AutomationElement.FromPoint(new Point(request.X, request.Y));
            if (IsPassword(pointer)) return new("password");
            if (Diagnostics != null) Diagnostics = Diagnostics with { PointerType = pointer.Current.ControlType.Id };
            AddAncestors(candidates, pointer, request.Window);
        }
        bool supportsSelection = false;
        if (Diagnostics != null) Diagnostics = Diagnostics with { Candidates = candidates.Count };
        Stage("capture-candidates");
        foreach (var candidate in candidates)
        {
            if (IsPassword(candidate)) return new("password");
            if (!candidate.TryGetCurrentPattern(TextPattern.Pattern, out var raw)) continue;
            var pattern = (TextPattern)raw;
            if (pattern.SupportedTextSelection == SupportedTextSelection.None) continue;
            supportsSelection = true;
            if (Diagnostics != null) Diagnostics = Diagnostics with { SupportsSelection = true };
            var ranges = pattern.GetSelection();
            if (ranges == null || ranges.Length == 0) continue;
            if (ranges.Length > 16) return new("too-large");
            var parts = ranges.Select(r => r.GetText(MaximumCharacters + 1)).ToArray();
            var text = string.Join("\n", parts);
            if (text.Length == 0)
            {
                if (Diagnostics != null) Diagnostics = Diagnostics with { EmptySelections = Diagnostics.EmptySelections + 1,
                    EmptyType = candidate.Current.ControlType.Id };
                continue;
            }
            if (text.Length > MaximumCharacters) return new("too-large");
            if (Native.GetForegroundWindow() != (nint)request.Window || Native.GetIdentity((nint)request.Window) != target)
                return new("target-changed");

            var editable = ReadEditability(candidate, focused, ranges[0]);

            var anchorRanges = request.UsePointer ? ranges : [ranges[^1]];
            var rectangles = anchorRanges.SelectMany(range => range.GetBoundingRectangles())
                .Where(rect => !rect.IsEmpty)
                .Select(rect => new ScreenRect(rect.X, rect.Y, rect.Width, rect.Height));
            var dpi = Native.GetDpiForWindow((nint)request.Window);
            var bounds = SelectionAnchor.Resolve(rectangles, request.UsePointer, request.X, request.Y,
                (dpi == 0 ? 96 : dpi) / 96d);
            element = candidate;
            savedRanges = ranges.Select(r => r.Clone()).ToArray();
            focusedId = focused.GetRuntimeId();
            saved = new(Guid.NewGuid().ToString("N"), target, text, bounds, editable, ranges.Length, text.Length);
            Stage("capture-selected");
            return new("ok", saved);
        }
        Stage("capture-empty");
        return supportsSelection ? new("no-selection") : MissingCapability(request, target);
    }

    private ProbeResponse MissingCapability(ProbeRequest request, TargetIdentity target)
    {
        Stage("missing-capability");
        if (request.PrepareProvider)
        {
            // A read-only, bounded-scope request can cause lazy native providers to initialize.
            // Do not scan the document/desktop or infer selection from these child elements.
            if (Native.GetIdentity(Native.GetForegroundWindow()) != target) return new("target-changed");
            var root = AutomationElement.FromHandle((nint)request.Window);
            if (IsPassword(root)) return new("password");
            _ = root.FindFirst(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        }
        var focused = AutomationElement.FocusedElement;
        if (focused == null) return new("unsupported");
        if (request.UsePointer)
        {
            var contains = focused.Current.BoundingRectangle.Contains(new Point(request.X, request.Y));
            if (Diagnostics != null) Diagnostics = Diagnostics with { PointerInsideFocus = contains };
            if (!contains) { Stage("fallback-pointer-outside-focus"); return new("unsupported"); }
        }
        Stage("fallback-focus-check");
        NativeClipboardSource? nativeSource = null;
        var status = CheckClipboardFocus(focused, target);
        if (status == "clipboard-unavailable" && ClipboardSelectionAdapter.IsTarget(target))
        {
            nativeSource = NativeClipboardSourceReader.Read(target, request.X, request.Y);
            status = CheckClipboardFocus(focused, target, nativeSource);
            if (Diagnostics != null && nativeSource != null)
                Diagnostics = Diagnostics with { NativeSource = System.Text.Json.JsonSerializer.Serialize(nativeSource) };
        }
        if (status != "ok") return new(status == "clipboard-unavailable" ? "unsupported" : status);
        var id = Guid.NewGuid().ToString("N");
        var caret = nativeSource == null ? null : NativeCaretReader.TryBind(target, nativeSource, request.X, request.Y);
        var writable = IsValueWritable(focused) || caret != null;
        if (Diagnostics != null) Diagnostics = Diagnostics with { ClipboardWritable = writable };
        clipboardTarget = new(id, target, writable ? id : null, caret);
        clipboardFocusId = focused.GetRuntimeId(); clipboardLease.Start(clipboardTarget.Id);
        clipboardNativeSource = nativeSource; clipboardX = request.X; clipboardY = request.Y;
        Stage("fallback-candidate-ready");
        return new("unsupported", ClipboardTarget: clipboardTarget);
    }

    public ProbeResponse ValidateClipboard(string? sessionId)
    {
        var response = ValidateClipboardCore(sessionId);
        if (!response.Success && clipboardTarget?.WriteToken != null) clipboardLease.Clear();
        return response;
    }

    private ProbeResponse ValidateClipboardCore(string? sessionId)
    {
        if (Diagnostics != null) Diagnostics = Diagnostics with { ClipboardWritable = clipboardTarget?.WriteToken != null };
        Stage("clipboard-validate-lease");
        if (clipboardTarget == null || clipboardFocusId == null || clipboardTarget.Id != sessionId
            || !clipboardLease.IsValid(sessionId)) return new("expired");
        var focused = AutomationElement.FocusedElement;
        if (focused == null) return new("focus-changed");
        Stage("clipboard-validate-focus");
        if (Diagnostics != null) Diagnostics = Diagnostics with { FocusType = focused.Current.ControlType.Id };
        NativeClipboardSource? nativeSource = null;
        if (clipboardNativeSource != null)
        {
            nativeSource = NativeClipboardSourceReader.Read(clipboardTarget.Target, clipboardX, clipboardY);
            var nativeStatus = NativeClipboardSourceReader.Check(clipboardTarget.Target, nativeSource);
            if (nativeStatus != "ok") return new(nativeStatus);
            if (nativeSource == null || !NativeClipboardSourceReader.SameSource(clipboardNativeSource, nativeSource)) return new("focus-changed");
        }
        var status = CheckClipboardFocus(focused, clipboardTarget.Target, nativeSource);
        if (status != "ok") return new(status);
        if (!focused.GetRuntimeId().SequenceEqual(clipboardFocusId)) return new("focus-changed");
        if (clipboardTarget.WriteToken != null && (clipboardTarget.WriteCaret is { } caret
            ? !NativeCaretReader.Matches(clipboardTarget.Target, caret) : !IsValueWritable(focused)))
        {
            // A failed write binding cannot revive if the user later returns to the same field.
            clipboardLease.Clear();
            return new("focus-changed");
        }
        // A provider may become ready after capture. An explicit empty selection still denies copying.
        var candidates = new List<AutomationElement>(); AddAncestors(candidates, focused, clipboardTarget.Target.Window);
        if (Diagnostics != null) Diagnostics = Diagnostics with { Candidates = candidates.Count };
        Stage("clipboard-validate-selection");
        foreach (var candidate in candidates)
        {
            if (!candidate.TryGetCurrentPattern(TextPattern.Pattern, out var raw)) continue;
            var pattern = (TextPattern)raw;
            if (pattern.SupportedTextSelection != SupportedTextSelection.None
                && !pattern.GetSelection().Any(r => r.GetText(1).Length != 0))
            {
                Stage("clipboard-validate-empty");
                if (Diagnostics != null) Diagnostics = Diagnostics with { SupportsSelection = true, EmptySelections = 1 };
                return new("no-selection");
            }
        }
        Stage("clipboard-validate-ok");
        return new("ok", ClipboardTarget: clipboardTarget);
    }

    private static bool IsValueWritable(AutomationElement focused) =>
        focused.Current.ControlType == ControlType.Edit && focused.Current.HasKeyboardFocus
        && focused.Current.IsEnabled && !focused.Current.IsOffscreen
        && focused.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true) is false
        && focused.TryGetCurrentPattern(ValuePattern.Pattern, out _)
        // Missing provider properties must not be interpreted as the default false value.
        && focused.GetCurrentPropertyValue(ValuePattern.IsReadOnlyProperty, true) is false;

    public ProbeResponse ExtendClipboardLease(string? sessionId, int seconds)
    {
        if (seconds is < 1 or > 310) return new("invalid-request");
        var validation = ValidateClipboard(sessionId);
        if (!validation.Success) return validation;
        return new(clipboardLease.TryExtend(sessionId, seconds) ? "ok" : "expired");
    }

    public ProbeResponse ReleaseClipboardLease(string? sessionId)
    {
        if (clipboardTarget == null || clipboardTarget.Id != sessionId) return new("expired");
        // Cleanup may only shorten a lease; it cannot revive an expired or replaced candidate.
        return new(clipboardLease.TryRelease(sessionId) ? "ok" : "expired");
    }

    private string CheckClipboardFocus(AutomationElement focused, TargetIdentity target, NativeClipboardSource? nativeSource = null)
    {
        if (Native.GetIdentity(Native.GetForegroundWindow()) != target) return "target-changed";
        var branch = new List<AutomationElement>(); AddAncestors(branch, focused, target.Window);
        var nativeStatus = nativeSource == null ? "clipboard-unavailable" : NativeClipboardSourceReader.Check(target, nativeSource);
        if (nativeStatus == "password") return "password";
        if (branch.Count == 0 || !focused.Current.IsEnabled || focused.Current.IsOffscreen
            || !focused.Current.HasKeyboardFocus && nativeStatus != "ok")
        { Stage("fallback-focus-state-rejected"); return "clipboard-unavailable"; }
        foreach (var candidate in branch)
        {
            var password = candidate.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
            if (password is true) return "password";
            // A missing property on a proxy root requires independently verified native
            // text metadata or the scoped opaque adapter. Unknown child fields fail closed.
            if (password is not bool && !(nativeStatus == "ok"
                && candidate.Current.NativeWindowHandle == target.Window
                && candidate.Current.ControlType == ControlType.Window))
            { Stage("fallback-password-state-unavailable"); return "clipboard-unavailable"; }
        }
        var type = focused.Current.ControlType;
        if (type == ControlType.Window && nativeStatus == "ok") return "ok";
        return type == ControlType.Edit || type == ControlType.Document || type == ControlType.Text || type == ControlType.Pane
            ? "ok" : "clipboard-unavailable";
    }

    public ProbeResponse Validate(string? sessionId)
    {
        if (saved == null || element == null || savedRanges == null || saved.Id != sessionId) return new("expired");
        if (Native.GetForegroundWindow() != (nint)saved.Target.Window || Native.GetIdentity((nint)saved.Target.Window) != saved.Target)
            return new("target-changed");
        var focused = AutomationElement.FocusedElement;
        if (focused == null || IsPassword(focused) || !focused.GetRuntimeId().SequenceEqual(focusedId ?? [])) return new("focus-changed");
        if (IsPassword(element) || !element.TryGetCurrentPattern(TextPattern.Pattern, out var raw)) return new("unsupported");
        var ranges = ((TextPattern)raw).GetSelection();
        if (ranges == null || ranges.Length != savedRanges.Length) return new("selection-changed");
        for (var i = 0; i < ranges.Length; i++)
        {
            if (ranges[i].CompareEndpoints(TextPatternRangeEndpoint.Start, savedRanges[i], TextPatternRangeEndpoint.Start) != 0
                || ranges[i].CompareEndpoints(TextPatternRangeEndpoint.End, savedRanges[i], TextPatternRangeEndpoint.End) != 0)
                return new("selection-changed");
        }
        if (string.Join("\n", ranges.Select(r => r.GetText(MaximumCharacters + 1))) != saved.Text) return new("selection-changed");
        var editable = ReadEditability(element, focused, ranges[0]);
        return new("ok", saved with { Editable = editable });
    }

    private static bool? ReadEditability(AutomationElement candidate, AutomationElement focused, TextPatternRange range)
    {
        var attribute = range.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        bool? readOnly = attribute is bool ro ? ro : null;
        var valueReadOnly = candidate.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && ((ValuePattern)value).Current.IsReadOnly;
        // Pointer-derived controls can retain a selection while a different editor owns focus.
        return SessionPolicy.ResolveEditability(readOnly, candidate.Current.IsEnabled,
            candidate.GetRuntimeId().SequenceEqual(focused.GetRuntimeId()), valueReadOnly);
    }

    private static bool IsPassword(AutomationElement item) => item.Current.IsPassword;

    private static void AddAncestors(List<AutomationElement> list, AutomationElement start, long target)
    {
        var branch = new List<AutomationElement>();
        AutomationElement? node = start;
        bool belongs = false;
        // Chromium/Electron content can be nested beyond sixteen control ancestors.
        // Keep a finite limit, but still require ancestry to establish target ownership.
        for (var depth = 0; depth < MaximumAncestorDepth && node != null; depth++)
        {
            var window = node.Current.NativeWindowHandle;
            if (window != 0 && Native.GetAncestor((nint)window, 2) == (nint)target) belongs = true;
            if (window != 0 && (long)window == target) { belongs = true; branch.Add(node); break; }
            if (node.Current.ControlType == ControlType.Window && window != 0 && !belongs) break;
            branch.Add(node);
            node = TreeWalker.ControlViewWalker.GetParent(node);
        }
        if (belongs) list.AddRange(branch);
    }
}
