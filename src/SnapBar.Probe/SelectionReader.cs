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

    public ProbeResponse Capture(ProbeRequest request)
    {
        saved = null; savedRanges = null; element = null; focusedId = null;
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

    private static ProbeResponse MissingCapability(ProbeRequest request, TargetIdentity target)
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
        return new("unsupported");
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
