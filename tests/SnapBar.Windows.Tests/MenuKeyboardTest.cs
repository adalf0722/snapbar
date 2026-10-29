using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class MenuKeyboardTest
{
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint window);
    static IEnumerable<T> Children<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var passed = 0; var result = 1;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS " + name); }
        app.Startup += async (_, _) =>
        {
            var sourceText = "Disposable source selection";
            var editor = new TextBox { Text = sourceText, AcceptsReturn = true };
            var source = new Window { Title = "SnapBar menu keyboard fixture", Content = editor, Width = 500, Height = 220 };
            var popup = new PopupWindow();
            using var backup = new ClipboardBackup(); var captured = false;
            nint handle = 0;
            void Locked(Action action)
            {
                var opened = false;
                for (var i = 0; i < 20 && !opened; i++) { opened = OpenClipboard(handle); if (!opened) Thread.Sleep(15); }
                if (!opened) throw new InvalidOperationException("Clipboard busy");
                try { action(); } finally { CloseClipboard(); }
            }
            async Task Key(byte key, bool down) { keybd_event(key, 0, down ? 0u : 2u, 0); await Task.Delay(40); }
            async Task Tap(byte key) { await Key(key, true); await Key(key, false); }
            try
            {
                source.Show(); handle = new WindowInteropHelper(source).Handle;
                Console.WriteLine("WAITING: activate the dedicated SnapBar menu keyboard fixture (up to 60 seconds)");
                source.Activate(); SetForegroundWindow(handle); editor.Focus(); editor.SelectAll();
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (Native.GetForegroundWindow() != handle && DateTime.UtcNow < deadline) await Task.Delay(50);
                Check(Native.GetForegroundWindow() == handle, "dedicated editable source owns foreground");
                Locked(() => { var status = backup.Capture(); if (status != "ok") throw new InvalidOperationException("Clipboard preservation failed: " + status); captured = true; });
                const string query = "\u4e2d\u6587 English";
                Clipboard.SetText(query);
                var actions = Enumerable.Range(0, 20).Select(i => new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), i == 19 ? query : "Action " + i, CustomActionKind.Url, "globe", "https://example.com/?q=***")).ToArray();
                var settings = ActionCatalog.Normalize(new() { CustomActions = actions, Layout = actions.Select(a => new ActionLayoutEntry(a.Id, ActionPlacement.More)).ToArray() });
                var snapshot = new SelectionSnapshot("keyboard-test", Native.GetIdentity(handle)!, sourceText, new(40, 260, 100, 20), true, 1, sourceText.Length);
                popup.Present(snapshot, 40, 260, actionSettings: settings);
                Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var keyDowns = new List<int>(); var keyUps = new List<int>(); var invalidations = 0;
                HwndSource.FromHwnd(handle).AddHook((nint h, int msg, nint w, nint l, ref bool handled) => { if (msg == 0x100) keyDowns.Add((int)w); if (msg == 0x101) keyUps.Add((int)w); return 0; });
                using var monitor = new InputMonitor(HwndSource.FromHwnd(handle));
                monitor.KeyFilter = key => popup.HandleMenuKey(key);
                monitor.Input += e => { if (e.Kind == "key") { invalidations++; popup.Dismiss(); } };
                await Key(0x11, true); await Tap(0x56); await Key(0x11, false);
                Check(popup.MenuOpen && Children<Button>(popup).Count(b => AutomationProperties.GetAutomationId(b).StartsWith("MoreAction_")) == 1, "native Ctrl+V filters Unicode clipboard query");
                Check(editor.Text == sourceText && editor.SelectionLength == sourceText.Length && invalidations == 0, "menu paste preserves source selection without invalidation");
                Check(keyDowns.Count(k => k == 0x11) == 1 && keyUps.Count(k => k == 0x11) == 1 && !keyDowns.Contains(0x56) && !keyUps.Contains(0x56), "source receives balanced Control and no swallowed-key release");
                await Tap(0x1B); Check(popup.MenuOpen, "Escape clears search first");
                var buttons = Children<Button>(popup).Where(b => b.IsEnabled && AutomationProperties.GetAutomationId(b).StartsWith("MoreAction_")).ToArray();
                for (var i = 0; i < buttons.Length; i++) await Tap(0x28);
                var selected = Children<Button>(popup).Last(b => b.IsEnabled && AutomationProperties.GetAutomationId(b).StartsWith("MoreAction_"));
                var scroll = Children<ScrollViewer>(popup).Single();
                var bounds = selected.TransformToAncestor(scroll).TransformBounds(new Rect(selected.RenderSize));
                Check(scroll.VerticalOffset > 0 && bounds.Top >= -1 && bounds.Bottom <= scroll.ActualHeight + 1, "Down navigation scrolls final action fully into view");
                string? dispatched = null; popup.CustomActionRequested += id => dispatched = id;
                await Tap(0x0D); Check(dispatched == actions[^1].Id, "Enter dispatches visible final selection");
                await Tap(0x1B); Check(!popup.MenuOpen, "Escape closes empty search");
                Check(!keyDowns.Contains(0x28) && !keyUps.Contains(0x28), "menu navigation consumes balanced key transitions");
                // A shortcut outside the menu must reach the editable source intact.
                editor.Select(0, 0); editor.Focus(); await Key(0x11, true); await Tap(0x41); await Key(0x11, false);
                Check(editor.SelectionLength == sourceText.Length && editor.Text == sourceText && keyUps.Contains(0x41)
                    && keyDowns.Count(k => k == 0x11) == 2 && keyUps.Count(k => k == 0x11) == 2,
                    "unrelated Control shortcut survives menu closure with balanced modifiers");
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error.Message); }
            finally
            {
                keybd_event(0x11, 0, 2, 0);
                try { if (captured) Locked(() => { if (!backup.Restore()) throw new InvalidOperationException("Clipboard restore failed"); }); }
                catch (Exception error) { result = 1; Console.WriteLine("FAIL " + error.Message); }
                popup.Close(); source.Close(); Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed; clipboard restored"); app.Shutdown();
            }
        };
        app.Run(); return result;
    }
}
