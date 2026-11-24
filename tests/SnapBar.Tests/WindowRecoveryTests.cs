using SnapBar.Core;

internal static class WindowRecoveryTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        ScreenRect primary = new(0, 0, 1920, 1040);
        ScreenRect window = new(1700, 200, 980, 720);
        ScreenRect caption = new(1740, 208, 800, 30);
        check("window partially outside one monitor stays where placed", () =>
            WindowRecovery.Recover(window, caption, [primary], 1) == window);
        check("window spanning right monitor keeps position", () =>
            WindowRecovery.Recover(window, caption, [primary, new(1920, 0, 1280, 1024)], 1) == window);
        check("window spanning lower monitor keeps position before center crosses", () =>
        {
            var bounds = new ScreenRect(200, 850, 980, 720);
            return WindowRecovery.Recover(bounds, new(240, 858, 800, 30), [primary, new(0, 1080, 1920, 1080)], 1) == bounds;
        });
        check("negative-coordinate left monitor preserves caption", () =>
        {
            var bounds = new ScreenRect(-1600, 200, 980, 720);
            return WindowRecovery.Recover(bounds, new(-1560, 208, 800, 30), [primary, new(-1920, 0, 1920, 1040)], 1) == bounds;
        });
        check("negative-coordinate upper monitor preserves caption", () =>
        {
            var bounds = new ScreenRect(200, -800, 980, 720);
            return WindowRecovery.Recover(bounds, new(240, -792, 800, 30), [primary, new(0, -1080, 1920, 1040)], 1) == bounds;
        });
        check("disconnected monitor recovery moves only enough to grab caption", () =>
            WindowRecovery.Recover(window with { X = 2000 }, caption with { X = 2040 }, [primary], 1)
                == window with { X = 1784 });
        check("offscreen caption above monitor needs only vertical recovery", () =>
            WindowRecovery.Recover(window with { Y = -50 }, caption with { Y = -42 }, [primary], 1)
                == window with { Y = -8 });
        check("caption in monitor gap is not visible merely inside virtual desktop", () =>
        {
            var bounds = new ScreenRect(200, 1150, 980, 720);
            return WindowRecovery.Recover(bounds, new(240, 1158, 800, 30), [primary, new(0, 1400, 1920, 1080)], 1)
                == bounds with { Y = 1002 };
        });
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2 })
        {
            var scale = dpi;
            check($"caption recovery honors 96 DIP grab width at scale {scale}", () =>
            {
                var bounds = window with { X = 1850 };
                var title = caption with { X = 1890 };
                var recovered = WindowRecovery.Recover(bounds, title, [primary], scale);
                return recovered == bounds with { X = 1880 - 96 * scale }
                    && WindowRecovery.Recover(recovered, title with { X = title.X + recovered.X - bounds.X }, [primary], scale) == recovered;
            });
        }
        check("tiny work area still allows recovery without resizing window", () =>
            WindowRecovery.Recover(new(200, 200, 980, 720), new(240, 208, 800, 30), [new(0, 0, 60, 20)], 2)
                == new ScreenRect(-40, -8, 980, 720));
        check("unknown monitor geometry leaves window alone", () =>
            WindowRecovery.Recover(window, caption, [], 1) == window);
        check("invalid monitor geometry is ignored", () =>
            WindowRecovery.Recover(window, caption, [new(double.NaN, 0, 1920, 1080)], 1) == window);
        check("caption reachable on second monitor prevents recovery toward first", () =>
            WindowRecovery.Recover(window with { X = 2000 }, caption with { X = 2040 }, [primary, new(1920, 0, 1280, 1040)], 1)
                == window with { X = 2000 });
    }
}
