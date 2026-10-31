using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class ClipboardCompatibilityTest
{
    private const string Sample = "Unicode 相容測試 😀";
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);

    private sealed class OpaqueTextBox : TextBox
    {
        public string? Marker { get; set; }
        public int Copies { get; private set; }
        protected override AutomationPeer OnCreateAutomationPeer() => new OpaquePeer(this);
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Copies++;
                if (Marker != null)
                {
                    if (!OpenClipboard(new WindowInteropHelper(Window.GetWindow(this)).Handle)) throw new InvalidOperationException("fixture clipboard busy");
                    try
                    {
                        EmptyClipboard();
                        void Set(uint format, byte[] bytes)
                        {
                            var memory = GlobalAlloc(2, (nuint)bytes.Length); var pointer = GlobalLock(memory);
                            Marshal.Copy(bytes, 0, pointer, bytes.Length); GlobalUnlock(memory);
                            if (SetClipboardData(format, memory) == 0) throw new InvalidOperationException("fixture write failed");
                        }
                        Set(13, Encoding.Unicode.GetBytes(Sample + "\0"));
                        var metadata = Marker.EndsWith("selection") ? "{\"isFromEmptySelection\":false}" : "{\"isFromEmptySelection\":true}";
                        if (Marker.StartsWith("chromium"))
                        {
                            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.Unicode, true);
                            writer.Write(0); writer.Write(1);
                            foreach (var value in new[] { "vscode-editor-data", metadata })
                            { writer.Write(value.Length); writer.Write(Encoding.Unicode.GetBytes(value)); while (stream.Length % 4 != 0) writer.Write((byte)0); }
                            stream.Position = 0; writer.Write((int)stream.Length - 4);
                            Set(RegisterClipboardFormat("Chromium Web Custom MIME Data Format"), stream.ToArray());
                        }
                        else Set(RegisterClipboardFormat(Marker.StartsWith("vscode") ? "vscode-editor-data" : Marker), Encoding.UTF8.GetBytes(metadata + "\0"));
                    }
                    finally { CloseClipboard(); }
                    e.Handled = true; return;
                }
            }
            base.OnPreviewKeyDown(e);
        }
        private sealed class OpaquePeer(OpaqueTextBox owner) : TextBoxAutomationPeer(owner)
        {
            public override object? GetPattern(PatternInterface patternInterface) =>
                patternInterface is PatternInterface.Text or PatternInterface.Value ? null : base.GetPattern(patternInterface);
        }
    }

    public static int RunFixture()
    {
        var app = new Application(); var edit = new OpaqueTextBox { Text = Sample, Height = 80, FontSize = 20 };
        var other = new OpaqueTextBox { Text = "Other focus", Height = 60 };
        var password = new PasswordBox { Password = "disposable password", Height = 40 };
        var button = new Button { Content = "Non-text focus", Height = 40 };
        var panel = new StackPanel { Margin = new(30) }; panel.Children.Add(edit); panel.Children.Add(other); panel.Children.Add(password); panel.Children.Add(button);
        var window = new Window { Title = "SnapBar Clipboard Compatibility Fixture", Width = 650, Height = 350, Content = panel };
        window.Loaded += (_, _) => { edit.Focus(); edit.SelectAll(); Console.WriteLine("READY"); };
        _ = Task.Run(async () =>
        {
            while (await Console.In.ReadLineAsync() is { } command)
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    switch (command)
                    {
                        case "select": edit.Marker = null; edit.Text = Sample; edit.Focus(); edit.SelectAll(); break;
                        case "empty": edit.Marker = null; edit.Focus(); edit.Select(0, 0); break;
                        case "other": other.Focus(); other.SelectAll(); break;
                        case "changed": edit.Focus(); edit.Select(0, 7); break;
                        case "password": password.Focus(); break;
                        case "button": button.Focus(); break;
                        case "MSDEVLineSelect": case "VisualStudioEditorOperationsLineCutCopyClipboardTag": case "vscode-empty": case "vscode-selection": case "chromium-empty": case "chromium-selection":
                            edit.Marker = command; edit.Focus(); edit.Select(0, 0); break;
                        case "quit": window.Close(); break;
                    }
                    Console.WriteLine(edit.Copies);
                });
                if (command == "quit") break;
            }
        });
        app.Run(window); return 0;
    }

    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
            foreach (var descendant in Children<T>(child)) yield return descendant;
    }

    public static int Run(bool includeLong = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var passed = 0; var result = 1;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS " + name); }
        app.Startup += async (_, _) =>
        {
            Process? fixture = null; using var backup = new ClipboardBackup(); bool captured = false;
            using var owner = new HwndSource(new HwndSourceParameters("SnapBar compatibility test owner") { Width = 1, Height = 1, WindowStyle = 0 });
            try
            {
                if (!OpenClipboard(owner.Handle)) throw new InvalidOperationException("clipboard busy");
                try { captured = backup.Capture() == "ok"; } finally { CloseClipboard(); }
                if (!captured) throw new InvalidOperationException("cannot preserve original clipboard");
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
                if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(Program).Assembly.Location);
                start.ArgumentList.Add("--clipboard-compatibility-fixture"); fixture = Process.Start(start)!;
                Check(await fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "READY", "independent unsupported-provider fixture starts");
                async Task<int> Command(string command)
                {
                    await fixture.StandardInput.WriteLineAsync(command); await fixture.StandardInput.FlushAsync();
                    var response = await fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    await Task.Delay(50); return int.Parse(response!);
                }
                fixture.Refresh(); Native.SetForegroundWindow(fixture.MainWindowHandle);
                Console.WriteLine("WAITING: activate SnapBar Clipboard Compatibility Fixture");
                var foregroundDeadline = DateTime.UtcNow.AddSeconds(45);
                while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != fixture.Id)
                { if (DateTime.UtcNow >= foregroundDeadline) throw new InvalidOperationException("fixture must be foreground"); await Task.Delay(50); }
                await Command("select");
                var launched = new List<Uri>(); var calls = 0; string? scriptInput = null;
                Func<CustomActionDefinition, string, CancellationToken, Task<ScriptExecutionResult>> runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("ok", "fixture result"));
                using var controller = new SelectionController(owner, TestPaths.ProbeExecutable, launched.Add,
                    runCustom: (action, text, token) => { calls++; scriptInput = text; return runner(action, text, token); });
                controller.Status += message => Console.WriteLine("STATUS " + message);
                // Record only event kinds, never keys or clipboard data, to diagnose desktop interference.
                using var inputOwner = new HwndSource(new HwndSourceParameters("SnapBar test input observer") { Width = 1, Height = 1, WindowStyle = 0 });
                using var inputObserver = new InputMonitor(inputOwner);
                inputObserver.Input += input => Console.WriteLine("INPUT " + input.Kind);
                async Task Capture()
                {
                    if (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != fixture.Id)
                        throw new InvalidOperationException("Fixture lost foreground; pause other desktop input and rerun. No capture was requested from another app.");
                    await controller.CaptureAsync(false, 0, 0);
                }
                Clipboard.SetText("original clipboard");
                await Capture();
                Check(controller.ActiveSelection == null && await Command("stats") == 0 && Clipboard.GetText() == "original clipboard", "disabled fallback never sends Ctrl+C");
                controller.ClipboardCompatibilityEnabled = true; await Capture();
                Check(controller.ActiveSelection is { Source: ClipboardSelectionAdapter.Source, Editable: null, Text: Sample }
                    && Clipboard.GetText() == "original clipboard", "real Ctrl+C reads Unicode from unsupported provider and restores original clipboard");
                await controller.ExecuteAsync(TextAction.Copy);
                Check(Clipboard.GetText() == Sample, "Copy revalidates generic selection and copies exact text");
                Clipboard.SetText("original clipboard"); await Capture(); await controller.ExecuteAsync(TextAction.Search);
                Check(launched.Count == 1 && Uri.UnescapeDataString(launched[0].Query).Contains(Sample) && Clipboard.GetText() == "original clipboard", "Search revalidates generic selection without retaining temporary clipboard text");
                var script = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "fixture", CustomActionKind.PowerShell, "terminal", "fixture", ActionOutput.Window);
                var replace = script with { Id = "custom-" + Guid.NewGuid().ToString("N"), Output = ActionOutput.ReplaceSelection };
                controller.ConfigureActions(ActionCatalog.Normalize(new() { CustomActions = [script, replace] }));
                await Capture(); await controller.ExecuteCustomAsync(script.Id);
                var resultWindow = app.Windows.OfType<ActionResultWindow>().Single(w => w.IsVisible);
                Check(calls == 1 && scriptInput == Sample && !Children<Button>(resultWindow).Single(b => AutomationProperties.GetName(b) == "套用至原文").IsEnabled,
                    "generic selection supplies script input but result cannot write back to unverified source");
                resultWindow.Close(); await Capture(); await controller.ExecuteCustomAsync(replace.Id);
                Check(calls == 1, "replacement is rejected before script execution for generic source");
                await Capture(); var before = await Command("stats"); await controller.ExecuteAsync(TextAction.Cut);
                Check(before == await Command("stats"), "Cut is rejected before recopy or injection for generic source");
                await Capture(); before = await Command("other"); await controller.ExecuteAsync(TextAction.Search);
                Check(launched.Count == 1 && before == await Command("stats"), "programmatic focus change inside same window prevents recopy and action");
                await Command("select"); await Capture(); await Command("changed"); await controller.ExecuteAsync(TextAction.Search);
                Check(launched.Count == 1 && Clipboard.GetText() == "original clipboard", "changed selection text cancels action and restores clipboard");
                await Command("empty"); await Capture();
                Check(controller.ActiveSelection == null && Clipboard.GetText() == "original clipboard", "empty selection with no copy response never reuses stale clipboard contents");
                foreach (var marker in new[] { "MSDEVLineSelect", "VisualStudioEditorOperationsLineCutCopyClipboardTag", "vscode-empty", "chromium-empty" })
                {
                    await Command(marker); await Capture();
                    Check(controller.ActiveSelection == null && Clipboard.GetText() == "original clipboard", "empty-line copy marker is rejected after clipboard restoration: " + marker);
                }
                await Command("vscode-selection"); await Capture();
                Check(controller.ActiveSelection?.Text == Sample && Clipboard.GetText() == "original clipboard", "explicit nonempty editor metadata remains supported");
                await Command("chromium-selection"); await Capture();
                Check(controller.ActiveSelection?.Text == Sample && Clipboard.GetText() == "original clipboard", "Chromium UTF-16 custom metadata supports nonempty editor selection");
                before = await Command("password"); await Capture();
                Check(controller.ActiveSelection == null && before == await Command("stats") && Clipboard.GetText() == "original clipboard", "password focus never triggers clipboard fallback");
                before = await Command("button"); await Capture();
                Check(controller.ActiveSelection == null && before == await Command("stats"), "unsupported non-text focus does not authorize copying");
                await Command("select");
                var longScript = script with { TimeoutSeconds = 60 };
                controller.ConfigureActions(ActionCatalog.Normalize(new() { CustomActions = [longScript] }));
                if (includeLong)
                {
                    runner = async (_, _, token) =>
                    {
                        var watch = Stopwatch.StartNew();
                        Console.WriteLine("RUNNING: 35-second script exceeds the normal clipboard candidate lifetime; keep the fixture foreground");
                        try { await Task.Delay(TimeSpan.FromSeconds(35), token); }
                        catch (OperationCanceledException)
                        {
                            Console.WriteLine($"FAIL long script cancelled after {watch.Elapsed.TotalSeconds:F1}s; fixture foreground={Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == fixture.Id}; selected session present={controller.ActiveSelection != null}");
                            throw;
                        }
                        return new("ok", "long script completed");
                    };
                    await Capture(); await controller.ExecuteCustomAsync(longScript.Id);
                    resultWindow = app.Windows.OfType<ActionResultWindow>().SingleOrDefault(w => w.IsVisible);
                    Check(resultWindow != null && Children<TextBox>(resultWindow).Any(t => t.Text == "long script completed")
                        && Clipboard.GetText() == "original clipboard", "35-second script retains result after original candidate deadline and preserves clipboard");
                    resultWindow!.Close();
                }
                runner = async (_, _, _) => { await Command("other"); return new("ok", "must not display"); };
                await Capture(); await controller.ExecuteCustomAsync(longScript.Id);
                Check(!app.Windows.OfType<ActionResultWindow>().Any(w => w.IsVisible), "extended lease still rejects focus changes during script execution");
                await Command("select"); await Capture(); controller.ClipboardCompatibilityEnabled = false;
                before = await Command("stats"); await controller.ExecuteAsync(TextAction.Search);
                Check(controller.ActiveSelection == null && launched.Count == 1 && before == await Command("stats"), "disabling compatibility immediately invalidates prior generic session");
                await Command("quit"); await fixture.WaitForExitAsync(); result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error); }
            finally
            {
                if (fixture is { HasExited: false }) { fixture.Kill(); fixture.WaitForExit(); } fixture?.Dispose();
                if (captured && OpenClipboard(owner.Handle)) { try { backup.Restore(); } finally { CloseClipboard(); } }
                foreach (var window in app.Windows.Cast<Window>().ToArray()) window.Close();
                Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed"); app.Shutdown();
            }
        };
        app.Run(); return result;
    }
}
