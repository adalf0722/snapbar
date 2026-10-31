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
    private long clipboardExpires;

    public ProbeResponse Capture(ProbeRequest request)
    {
        saved = null; savedRanges = null; element = null; focusedId = null; clipboardTarget = null; clipboardFocusId = null;
        var target = Native.GetIdentity((nint)request.Window);
        if (target == null || Native.GetForegroundWindow() != (nint)request.Window) return new("target-changed");
        var focused = AutomationElement.FocusedElement;
        if (focused == null) return MissingCapability(request, target);
        if (IsPassword(focused)) return new("password");
        var candidates = new List<AutomationElement>();
        AddAncestors(candidates, focused, request.Window);
        if (request.UsePointer)
        {
            var pointer = AutomationElement.FromPoint(new Point(request.X, request.Y));
            if (IsPassword(pointer)) return new("password");
            AddAncestors(candidates, pointer, request.Window);
        }
        bool supportsSelection = false;
        foreach (var candidate in candidates)
        {
            if (IsPassword(candidate)) return new("password");
            if (!candidate.TryGetCurrentPattern(TextPattern.Pattern, out var raw)) continue;
            var pattern = (TextPattern)raw;
            if (pattern.SupportedTextSelection == SupportedTextSelection.None) continue;
            supportsSelection = true;
            var ranges = pattern.GetSelection();
            if (ranges == null || ranges.Length == 0) continue;
            if (ranges.Length > 16) return new("too-large");
            var parts = ranges.Select(r => r.GetText(MaximumCharacters + 1)).ToArray();
            var text = string.Join("\n", parts);
            if (text.Length == 0) continue;
            if (text.Length > MaximumCharacters) return new("too-large");
            if (Native.GetForegroundWindow() != (nint)request.Window || Native.GetIdentity((nint)request.Window) != target)
                return new("target-changed");

            var editable = ReadEditability(candidate, focused, ranges[0]);

            ScreenRect? bounds = null;
            var rectangles = ranges[^1].GetBoundingRectangles();
            if (rectangles.Length > 0)
            {
                var last = rectangles[^1];
                if (!last.IsEmpty && double.IsFinite(last.X) && double.IsFinite(last.Y) && last.Width > 0 && last.Height > 0)
                    bounds = new(last.X, last.Y, last.Width, last.Height);
            }
            element = candidate;
            savedRanges = ranges.Select(r => r.Clone()).ToArray();
            focusedId = focused.GetRuntimeId();
            saved = new(Guid.NewGuid().ToString("N"), target, text, bounds, editable, ranges.Length, text.Length);
            return new("ok", saved);
        }
        return supportsSelection ? new("no-selection") : MissingCapability(request, target);
    }

    private ProbeResponse MissingCapability(ProbeRequest request, TargetIdentity target)
    {
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
        if (request.UsePointer && !focused.Current.BoundingRectangle.Contains(new Point(request.X, request.Y)))
            return new("unsupported");
        var status = CheckClipboardFocus(focused, target);
        if (status != "ok") return new(status == "clipboard-unavailable" ? "unsupported" : status);
        clipboardTarget = new(Guid.NewGuid().ToString("N"), target);
        clipboardFocusId = focused.GetRuntimeId(); clipboardExpires = Environment.TickCount64 + 30000;
        return new("unsupported", ClipboardTarget: clipboardTarget);
    }

    public ProbeResponse ValidateClipboard(string? sessionId)
    {
        if (clipboardTarget == null || clipboardFocusId == null || clipboardTarget.Id != sessionId
            || Environment.TickCount64 > clipboardExpires) return new("expired");
        var focused = AutomationElement.FocusedElement;
        if (focused == null) return new("focus-changed");
        var status = CheckClipboardFocus(focused, clipboardTarget.Target);
        if (status != "ok") return new(status);
        if (!focused.GetRuntimeId().SequenceEqual(clipboardFocusId)) return new("focus-changed");
        // A provider may become ready after capture. An explicit empty selection still denies copying.
        var candidates = new List<AutomationElement>(); AddAncestors(candidates, focused, clipboardTarget.Target.Window);
        foreach (var candidate in candidates)
        {
            if (!candidate.TryGetCurrentPattern(TextPattern.Pattern, out var raw)) continue;
            var pattern = (TextPattern)raw;
            if (pattern.SupportedTextSelection != SupportedTextSelection.None
                && !pattern.GetSelection().Any(r => r.GetText(1).Length != 0)) return new("no-selection");
        }
        return new("ok");
    }

    public ProbeResponse ExtendClipboardLease(string? sessionId, int seconds)
    {
        if (seconds is < 1 or > 310) return new("invalid-request");
        var validation = ValidateClipboard(sessionId);
        if (!validation.Success) return validation;
        clipboardExpires = Math.Max(clipboardExpires, Environment.TickCount64 + seconds * 1000L);
        return new("ok");
    }

    public ProbeResponse ReleaseClipboardLease(string? sessionId)
    {
        if (clipboardTarget == null || clipboardTarget.Id != sessionId) return new("expired");
        // Cleanup may only shorten a lease; it cannot revive an expired or replaced candidate.
        clipboardExpires = Math.Min(clipboardExpires, Environment.TickCount64 + 30000);
        return new("ok");
    }

    private static string CheckClipboardFocus(AutomationElement focused, TargetIdentity target)
    {
        if (Native.GetIdentity(Native.GetForegroundWindow()) != target) return "target-changed";
        var branch = new List<AutomationElement>(); AddAncestors(branch, focused, target.Window);
        if (branch.Count == 0 || !focused.Current.IsEnabled || focused.Current.IsOffscreen || !focused.Current.HasKeyboardFocus)
            return "clipboard-unavailable";
        foreach (var candidate in branch)
        {
            var password = candidate.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
            if (password is true) return "password";
            if (password is not bool) return "clipboard-unavailable";
        }
        var type = focused.Current.ControlType;
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
