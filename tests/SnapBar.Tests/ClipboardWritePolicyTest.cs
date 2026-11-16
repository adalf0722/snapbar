using SnapBar.Core;

internal static class ClipboardWritePolicyTest
{
    internal static void Run(Action<string, Func<bool>> check, TargetIdentity target, SelectionSnapshot selection)
    {
        var caret = new CaretIdentity(42, new(100, 200, 2, 19));
        bool Bind(bool app = true, bool accepted = true, long focus = 42, long point = 42,
            CaretIdentity? identity = null, int? role = 7, int? state = 0, double x = 101, double y = 209) =>
            ClipboardWritePolicy.CanBindCaret(app, accepted, focus, point, identity ?? caret, role, state, x, y, 12);
        check("clipboard write requires an editable snapshot and its own candidate token", () =>
            !SessionPolicy.CanWrite(selection with { Source = "clipboard" }, target)
            && !SessionPolicy.CanWrite(selection with { Source = "clipboard", WriteToken = "other" }, target)
            && SessionPolicy.CanWrite(selection with { Source = "clipboard", WriteToken = selection.Id }, target)
            && !SessionPolicy.CanWrite(selection with { Source = "clipboard", WriteToken = selection.Id, Editable = null }, target));
        check("clipboard write token cannot authorize a foreign window or multiple ranges", () =>
            !SessionPolicy.CanWrite(selection with { Source = "clipboard", WriteToken = selection.Id }, target with { Window = 99 })
            && !SessionPolicy.CanWrite(selection with { Source = "clipboard", WriteToken = selection.Id, RangeCount = 2 }, target));
        check("verified native caret near the selection endpoint qualifies", () => Bind());
        check("caret alone never authorizes unknown or unverified native sources", () => !Bind(app: false) && !Bind(accepted: false));
        check("write caret and pointer must belong to the same focused native control", () =>
            !Bind(focus: 0) && !Bind(point: 99) && !Bind(identity: caret with { Window = 99 }));
        foreach (var state in new[] { 0x20000000, 0x40, 0x8000, 0x10000, 0x1 })
            check($"protected readonly hidden or unavailable caret state {state} rejects writes", () => !Bind(state: state));
        check("missing or non-caret metadata cannot authorize writes", () => !Bind(role: null) && !Bind(role: 10) && !Bind(state: null));
        check("caret in another row or input location cannot authorize the selection", () => !Bind(x: 500) && !Bind(y: 20));
        check("double-click word centers have bounded horizontal allowance without relaxing the caret row", () =>
            ClipboardWritePolicy.CanBindCaret(true, true, 42, 42, caret, 7, 0, 78, 209, 8, 32)
            && !ClipboardWritePolicy.CanBindCaret(true, true, 42, 42, caret, 7, 0, 78, 170, 8, 32)
            && !ClipboardWritePolicy.CanBindCaret(true, true, 42, 42, caret, 7, 0, 20, 209, 8, 32));
        check("empty oversized and non-finite caret geometry fails closed", () =>
            !Bind(identity: caret with { ClientBounds = new(100, 200, 0, 19) })
            && !Bind(identity: caret with { ClientBounds = new(100, 200, 2, 1000) })
            && !Bind(identity: caret with { ClientBounds = new(double.NaN, 200, 2, 19) }));
    }
}
