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
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);

    private static string ReadClipboardText()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (!OpenClipboard(0)) { Thread.Sleep(10); continue; }
            try
            {
                var memory = GetClipboardData(13);
                if (memory == 0) throw new InvalidOperationException("Fixture expected Unicode clipboard data");
                var pointer = GlobalLock(memory);
                if (pointer == 0) throw new InvalidOperationException("Fixture could not lock Unicode clipboard data");
                try { return Marshal.PtrToStringUni(pointer) ?? ""; }
                finally { GlobalUnlock(memory); }
            }
            finally { CloseClipboard(); }
        }
        throw new InvalidOperationException("Fixture clipboard read remained busy");
    }

    private sealed class OpaqueTextBox : TextBox
    {
        public bool ExposeValue { get; set; }
        public string? Marker { get; set; }
        public int Copies { get; private set; }
        protected override AutomationPeer OnCreateAutomationPeer() => new OpaquePeer(this);
        protected override async void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Copies++;
                if (Marker is { } marker)
                {
                    e.Handled = true;
                    var owner = new WindowInteropHelper(Window.GetWindow(this)).Handle;
                    var opened = false;
                    for (var attempt = 0; attempt < 20 && !opened; attempt++)
                    {
                        opened = OpenClipboard(owner);
                        if (!opened) await Task.Delay(10);
                    }
                    if (!opened) throw new InvalidOperationException("fixture clipboard remained busy after bounded retry");
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
                        var metadata = marker.EndsWith("selection") ? "{\"isFromEmptySelection\":false}" : "{\"isFromEmptySelection\":true}";
                        if (marker.StartsWith("chromium"))
                        {
                            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.Unicode, true);
                            writer.Write(0); writer.Write(1);
                            foreach (var value in new[] { "vscode-editor-data", metadata })
                            { writer.Write(value.Length); writer.Write(Encoding.Unicode.GetBytes(value)); while (stream.Length % 4 != 0) writer.Write((byte)0); }
                            stream.Position = 0; writer.Write((int)stream.Length - 4);
                            Set(RegisterClipboardFormat("Chromium Web Custom MIME Data Format"), stream.ToArray());
                        }
                        else Set(RegisterClipboardFormat(marker.StartsWith("vscode") ? "vscode-editor-data" : marker), Encoding.UTF8.GetBytes(metadata + "\0"));
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
                patternInterface == PatternInterface.Text || patternInterface == PatternInterface.Value && !owner.ExposeValue
                    ? null : base.GetPattern(patternInterface);
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
                        case "select": edit.ExposeValue = false; edit.IsReadOnly = false; edit.Marker = null; edit.Text = Sample; edit.Focus(); edit.SelectAll(); break;
                        case "writable": edit.ExposeValue = true; edit.IsReadOnly = false; edit.Marker = null; edit.Text = Sample; edit.Focus(); edit.SelectAll(); break;
                        case "readonly": edit.ExposeValue = true; edit.IsReadOnly = true; edit.Marker = null; edit.Text = Sample; edit.Focus(); edit.SelectAll(); break;
                        case "readonly-state": edit.IsReadOnly = true; break;
                        case "empty": edit.Marker = null; edit.Focus(); edit.Select(0, 0); break;
                        case "other": other.Focus(); other.SelectAll(); break;
                        case "changed": edit.Focus(); edit.Select(0, 7); break;
                        case "password": password.Focus(); break;
                        case "button": button.Focus(); break;
                        case "MSDEVLineSelect": case "VisualStudioEditorOperationsLineCutCopyClipboardTag": case "vscode-empty": case "vscode-selection": case "chromium-empty": case "chromium-selection":
                            edit.Marker = command; edit.Focus(); edit.Select(0, 0); break;
                        case "quit": window.Close(); break;
                    }
                    Console.WriteLine(command switch {
                        "is-sample" => edit.Text == Sample ? 1 : 0,
                        "is-empty" => edit.Text.Length == 0 ? 1 : 0,
                        "is-replacement" => edit.Text == "fixture result" ? 1 : 0,
                        "other-intact" => other.Text == "Other focus" ? 1 : 0,
                        _ => edit.Copies });
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
                    if (response == null) throw new InvalidOperationException("Compatibility fixture exited before replying to the test command");
                    await Task.Delay(50); return int.Parse(response!);
                }
                async Task<bool> AwaitFixture(string command)
                {
                    // SendInput queues events; wait for the independent fixture to consume
                    // the single action, without retrying the action or changing expected data.
                    var deadline = Stopwatch.StartNew();
                    while (deadline.Elapsed < TimeSpan.FromSeconds(3))
                    {
                        if (await Command(command) == 1) return true;
                        await Task.Delay(25);
                    }
                    return false;
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
                Check(controller.ActiveSelection == null && await Command("stats") == 0 && ReadClipboardText() == "original clipboard", "disabled fallback never sends Ctrl+C");
                controller.ConfigureAutoCopy(new(true, AutoCopyMode.OnSelection, false));
                var autoBounds = AutomationElement.FocusedElement.Current.BoundingRectangle;
                await controller.CaptureAsync(true, (int)autoBounds.Left + 20, (int)autoBounds.Top + 20, autoCopyRequest: true);
                Check(ReadClipboardText() == "original clipboard" && await Command("stats") == 0,
                    "auto-copy never enables disabled clipboard compatibility");
                controller.ClipboardCompatibilityEnabled = true;
                await controller.CaptureAsync(true, (int)autoBounds.Left + 20, (int)autoBounds.Top + 20, autoCopyRequest: true);
                Check(ReadClipboardText() == Sample && controller.ActiveSelection?.Source == ClipboardSelectionAdapter.Source,
                    "automatic copy revalidates fallback selection across backup restore sequences");
                controller.ConfigureAutoCopy(new()); Clipboard.SetText("original clipboard");
                controller.ClipboardCompatibilityEnabled = true; await Capture();
                Check(controller.ActiveSelection is { Source: ClipboardSelectionAdapter.Source, Editable: null, Text: Sample }
                    && ReadClipboardText() == "original clipboard", "real Ctrl+C reads Unicode from unsupported provider and restores original clipboard");
                await controller.ExecuteAsync(TextAction.Copy);
                Check(ReadClipboardText() == Sample, "Copy revalidates generic selection and copies exact text");
                Clipboard.SetText("original clipboard"); await Capture(); await controller.ExecuteAsync(TextAction.Search);
                Check(launched.Count == 1 && Uri.UnescapeDataString(launched[0].Query).Contains(Sample) && ReadClipboardText() == "original clipboard", "Search revalidates generic selection without retaining temporary clipboard text");
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
                Check(launched.Count == 1 && ReadClipboardText() == "original clipboard", "changed selection text cancels action and restores clipboard");
                await Command("empty"); await Capture();
                Check(controller.ActiveSelection == null && ReadClipboardText() == "original clipboard", "empty selection with no copy response never reuses stale clipboard contents");
                foreach (var marker in new[] { "MSDEVLineSelect", "VisualStudioEditorOperationsLineCutCopyClipboardTag", "vscode-empty", "chromium-empty" })
                {
                    await Command(marker); await Capture();
                    var restored = ReadClipboardText();
                    Check(controller.ActiveSelection == null && restored == "original clipboard",
                        "empty-line copy marker is rejected after clipboard restoration: " + marker
                        + $" (selection absent={controller.ActiveSelection == null}, clipboard restored={restored == "original clipboard"}, clipboard length={restored.Length})");
                }
                await Command("vscode-selection"); await Capture();
                Check(controller.ActiveSelection?.Text == Sample && ReadClipboardText() == "original clipboard", "explicit nonempty editor metadata remains supported");
                await Command("chromium-selection"); await Capture();
                var chromiumClipboard = ReadClipboardText();
                Check(controller.ActiveSelection?.Text == Sample && chromiumClipboard == "original clipboard",
                    "Chromium UTF-16 custom metadata supports nonempty editor selection"
                    + $" (selection matches={controller.ActiveSelection?.Text == Sample}, clipboard restored={chromiumClipboard == "original clipboard"}, clipboard length={chromiumClipboard.Length}, foreground fixture={Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == fixture.Id})");
                before = await Command("password"); await Capture();
                Check(controller.ActiveSelection == null && before == await Command("stats") && ReadClipboardText() == "original clipboard", "password focus never triggers clipboard fallback");
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
                    Check(resultWindow != null && Children<FlowDocumentScrollViewer>(resultWindow).Any(viewer => new System.Windows.Documents.TextRange(viewer.Document.ContentStart, viewer.Document.ContentEnd).Text.TrimEnd() == "long script completed")
                        && ReadClipboardText() == "original clipboard", "35-second script retains result after original candidate deadline and preserves clipboard");
                    resultWindow!.Close();
                }
                runner = async (_, _, _) => { await Command("other"); return new("ok", "retained readonly result"); };
                await Capture(); await controller.ExecuteCustomAsync(longScript.Id);
                resultWindow = app.Windows.OfType<ActionResultWindow>().SingleOrDefault(w => w.IsVisible);
                Check(resultWindow != null && Children<FlowDocumentScrollViewer>(resultWindow).Any(viewer => new System.Windows.Documents.TextRange(viewer.Document.ContentStart, viewer.Document.ContentEnd).Text.TrimEnd() == "retained readonly result")
                    && !Children<Button>(resultWindow).Single(b => AutomationProperties.GetName(b) == "套用至原文").IsEnabled
                    && ReadClipboardText() == "original clipboard", "extended lease retains readonly output after focus change without source write permission");
                resultWindow!.Close();
                await Command("writable"); Clipboard.SetText("fixture result"); await Capture();
                Check(controller.ActiveSelection is { Source: "clipboard", Editable: true } writable
                    && writable.WriteToken == writable.Id && SessionPolicy.CanWrite(writable, writable.Target),
                    "unsupported text provider with writable ValuePattern receives a bound write token");
                await controller.ExecuteAsync(TextAction.Paste); await Task.Delay(100);
                Check(await AwaitFixture("is-replacement") && ReadClipboardText() == "fixture result",
                    "verified clipboard Paste replaces exact selection and preserves the original paste data");
                await Command("writable"); await Capture(); await controller.ExecuteAsync(TextAction.Cut); await Task.Delay(100);
                Check(await AwaitFixture("is-empty") && ReadClipboardText() == Sample,
                    "verified clipboard Cut removes selection and leaves the cut text on the clipboard");
                await Command("readonly"); await Capture();
                Check(controller.ActiveSelection is { Editable: null, WriteToken: null },
                    "read-only ValuePattern cannot mint a clipboard write token");
                await Command("writable"); await Capture(); await Command("other");
                await controller.ExecuteAsync(TextAction.Paste);
                Check(await Command("is-sample") == 1 && await Command("other-intact") == 1,
                    "same-window focus change rejects a previously writable clipboard selection");
                runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("ok", "fixture result"));
                await Command("writable"); controller.ConfigureActions(ActionCatalog.Normalize(new() { CustomActions = [replace] }));
                await Capture(); await controller.ExecuteCustomAsync(replace.Id); await Task.Delay(100);
                Check(await AwaitFixture("is-replacement"),
                    "custom replacement revalidates and writes a verified clipboard selection");
                runner = async (_, _, _) => { await Command("other"); return new("ok", "fixture result"); };
                await Command("writable"); await Capture(); await controller.ExecuteCustomAsync(replace.Id);
                Check(await Command("is-sample") == 1 && await Command("other-intact") == 1,
                    "late replacement result cannot write after a programmatic focus switch");
                runner = async (_, _, _) => { await Command("changed"); return new("ok", "fixture result"); };
                await Command("writable"); await Capture(); await controller.ExecuteCustomAsync(replace.Id);
                Check(await Command("is-sample") == 1,
                    "late replacement result cannot overwrite changed selection in the same editor");
                await Command("writable");
                using (var writeProbe = new ProbeClient(TestPaths.ProbeExecutable))
                {
                    var candidate = (await writeProbe.RequestAsync(new("capture", fixture.MainWindowHandle.ToInt64()))).ClipboardTarget;
                    Check(candidate?.WriteToken == candidate?.Id && candidate != null,
                        "Probe issues the write capability only on its original candidate");
                    await Command("readonly-state");
                    var revoked = await writeProbe.RequestAsync(new("clipboard-validate", SessionId: candidate!.Id));
                    await Command("writable");
                    var revived = await writeProbe.RequestAsync(new("clipboard-validate", SessionId: candidate.Id));
                    Check(revoked.Status == "focus-changed" && revived.Status == "expired",
                        "revoked write capability cannot revive when the same editor becomes writable again");
                }
                Clipboard.SetText("original clipboard");
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
