using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SnapBar.Windows;

internal static class NativeClipboardSourceTest
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint extended,
        string className, string text, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);

    private sealed class ControlHost(string className, bool password = false) : HwndHost
    {
        internal nint Control { get; private set; }
        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            Control = CreateWindowEx(0, className, "Disposable native fixture", 0x50010000u | (password ? 0x20u : 0),
                0, 0, 400, 40, parent.Handle, 0, 0, 0);
            if (Control == 0) throw new InvalidOperationException("Native fixture could not create a control");
            return new(this, Control);
        }
        protected override void DestroyWindowCore(HandleRef window) { DestroyWindow(window.Handle); Control = 0; }
    }

    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var result = 1; var passed = 0;
        app.Startup += async (_, _) =>
        {
            var panel = new StackPanel { Margin = new(20) };
            using var edit = new ControlHost("EDIT") { Height = 40, Margin = new(0, 4, 0, 4) };
            using var other = new ControlHost("EDIT") { Height = 40, Margin = new(0, 4, 0, 4) };
            using var password = new ControlHost("EDIT", true) { Height = 40, Margin = new(0, 4, 0, 4) };
            using var button = new ControlHost("BUTTON") { Height = 40, Margin = new(0, 4, 0, 4) };
            panel.Children.Add(edit); panel.Children.Add(other); panel.Children.Add(password); panel.Children.Add(button);
            var window = new Window { Title = "SnapBar native clipboard source fixture", Width = 540, Height = 300, Content = panel };
            void Check(bool condition, string name)
            { if (!condition) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS " + name); }
            try
            {
                window.Show(); window.Activate();
                var target = Native.GetIdentity(new WindowInteropHelper(window).Handle)!;
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (Native.GetIdentity(Native.GetForegroundWindow()) != target && DateTime.UtcNow < deadline)
                { window.Activate(); await Task.Delay(25); }
                Check(Native.GetIdentity(Native.GetForegroundWindow()) == target, "native source fixture owns foreground");
                async Task<NativeClipboardSource> Read(ControlHost focus, ControlHost point)
                {
                    SetFocus(focus.Control); await Task.Delay(30);
                    if (!Native.GetWindowRect(point.Control, out var bounds)) throw new InvalidOperationException("Missing native control bounds");
                    // Hit the first line of text rather than blank space below a single-line EDIT.
                    return NativeClipboardSourceReader.Read(target, bounds.Left + 12, bounds.Top + 8)
                        ?? throw new InvalidOperationException("Native source metadata was unavailable");
                }
                var normal = await Read(edit, edit);
                Check(normal.FocusWindow == (long)edit.Control && normal.PointWindow == (long)edit.Control,
                    "native focus and pointer bind the actual child control");
                Check(normal.PointRole == 42 && normal.FocusRole == 42,
                    $"real MSAA exposes text roles (point={normal.PointRole}, focus={normal.FocusRole}, error={normal.AccessibilityError}, pointChild={normal.PointChildId}, focusChild={normal.FocusChildId})");
                Check(NativeClipboardSourceReader.Check(target, normal) == "ok",
                    "unknown application qualifies through native text capabilities without an app profile");
                var caret = NativeCaretReader.ReadIdentity(target);
                Check(caret != null && caret.Window == (long)edit.Control,
                    "real native caret is bound to the focused edit control");
                var protectedSource = await Read(password, password);
                Check(protectedSource.PasswordStyle && NativeClipboardSourceReader.Check(target, protectedSource) == "password",
                    "real password edit is rejected by native protection");
                Check(NativeClipboardSourceReader.Check(target, await Read(button, button)) == "clipboard-unavailable",
                    "real native button cannot authorize fallback");
                Check(NativeClipboardSourceReader.Check(target, await Read(edit, other)) == "clipboard-unavailable",
                    "pointer over another editor cannot copy from the old focused editor");
                Check(!NativeClipboardSourceReader.SameSource(normal, await Read(other, other)),
                    "native source identity changes when another editor gains focus");
                Check(NativeCaretReader.ReadIdentity(target) != caret,
                    "changing editor invalidates the old native write caret identity");
                Check(NativeClipboardSourceReader.Read(target, -100000, -100000) == null,
                    "pointer outside the owned window cannot create a source");
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error); }
            finally { window.Close(); Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed"); app.Shutdown(); }
        };
        app.Run(); return result;
    }
}
