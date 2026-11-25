using SnapBar.Core;

internal static class SelectionAnchorTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        ScreenRect[] columns = [new(200, 100, 200, 20), new(500, 100, 300, 20), new(0, 900, 1000, 20)];
        check("cross-column toolbar follows release instead of the final page rectangle", () =>
            SelectionAnchor.Resolve(columns, true, 790, 110) == new ScreenRect(790, 100, 1, 20));
        check("reverse selection follows release at the first column", () =>
            SelectionAnchor.Resolve(columns, true, 210, 110) == new ScreenRect(210, 100, 1, 20));
        check("keyboard selection keeps its final text rectangle even with pointer elsewhere", () =>
            SelectionAnchor.Resolve(columns, false, 210, 110) == columns[^1]);
        check("distant or container-height geometry falls back to release", () =>
            SelectionAnchor.Resolve([columns[^1], new(100, 100, 1000, 800)], true, 790, 110)
                == new ScreenRect(790, 110, 1, 1));
        check("missing geometry falls back to pointer for clipboard and keyboard captures", () =>
            SelectionAnchor.Resolve([], true, -400, 110) == new ScreenRect(-400, 110, 1, 1)
            && SelectionAnchor.Resolve([], false, -400, 110) == new ScreenRect(-400, 110, 1, 1));
        check("invalid geometry never reaches native popup placement", () =>
            SelectionAnchor.Resolve([new(double.NaN, 0, 10, 10), new(0, 0, double.PositiveInfinity, 10),
                new(0, 0, 10, -1), new(double.MaxValue, 0, double.MaxValue, 10)], false, 50, 60)
                == new ScreenRect(50, 60, 1, 1));
        check("pointer proximity and line height scale with source DPI", () =>
            SelectionAnchor.Resolve([new(-800, 200, 200, 70)], true, -590, 240, 2)
                == new ScreenRect(-590, 200, 1, 70)
            && SelectionAnchor.Resolve([new(-800, 200, 200, 70)], true, -590, 240)
                == new ScreenRect(-590, 240, 1, 1));
        check("wide selected line anchors horizontally at release", () =>
            SelectionAnchor.Resolve([new(-1800, 100, 1500, 20)], true, -350, 110)
                == new ScreenRect(-350, 100, 1, 20));
        check("pointer fallback still flips and clamps at bottom-right edge", () =>
        {
            var anchor = SelectionAnchor.Resolve([new(0, 5000, 1000, 20)], true, 1910, 1070);
            var placed = PopupPlacement.Place(anchor, new(0, 0, 1920, 1080), 300, 54);
            return placed.X == 1620 && placed.Y == 1008 && placed.Bottom < anchor.Y;
        });
    }
}
