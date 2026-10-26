using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using SnapBar.Core;
using SnapBar.Windows;

// Explicit desktop tests. Producers are this fixture and our hidden child process;
// no real editor, user document, Sublime setting, or keyboard shortcut is involved.
internal static class ClipboardRaceTest
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Locked(nint owner, Action work)
    {
        Require(OpenClipboard(owner), "Desktop clipboard is busy; stop copying and retry");
        try { work(); } finally { CloseClipboard(); }
    }

    private static void SetBytes(uint format, byte[] bytes)
    {
        var memory = GlobalAlloc(2, (nuint)bytes.Length);
        Require(memory != 0, "Allocation failed");
        try
        {
            var pointer = GlobalLock(memory);
            Require(pointer != 0, "Clipboard memory could not be locked");
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            Require(SetClipboardData(format, memory) != 0, "Clipboard write failed");
            memory = 0;
        }
        finally { if (memory != 0) GlobalFree(memory); }
    }

    private static void Write(nint owner, string text, uint richFormat = 0)
    {
        Locked(owner, () =>
        {
            Require(EmptyClipboard(), "Clipboard could not be emptied");
            SetBytes(13, Encoding.Unicode.GetBytes(text + "\0"));
            if (richFormat != 0) SetBytes(richFormat, [1, 7, 13, 29]);
        });
    }

    private static void ExpectClipboard(nint owner, string expected, uint richFormat = 0)
    {
        Locked(owner, () =>
        {
            var memory = GetClipboardData(13);
            Require(memory != 0, "Expected Unicode clipboard data");
            var pointer = GlobalLock(memory);
            Require(pointer != 0, "Clipboard text could not be locked");
            try { Require(Marshal.PtrToStringUni(pointer) == expected, "Unexpected clipboard text"); }
            finally { GlobalUnlock(memory); }
            if (richFormat == 0) return;
            memory = GetClipboardData(richFormat);
            Require(memory != 0 && GlobalSize(memory) == 4, "Original rich format was lost");
            pointer = GlobalLock(memory);
            Require(pointer != 0, "Rich format could not be locked");
            try
            {
                var bytes = new byte[4]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
                Require(bytes.SequenceEqual(new byte[] { 1, 7, 13, 29 }), "Original rich bytes changed");
            }
            finally { GlobalUnlock(memory); }
        });
    }

    // Only a child launched by Run uses this mode. Keep its owner HWND alive until EOF.
    public static int RunWriter()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var owner = new HwndSource(new HwndSourceParameters("SnapBar race writer") { Width = 1, Height = 1, WindowStyle = 0 });
        int result = 0;
        Console.WriteLine("READY");
        _ = Task.Run(async () =>
        {
            try
            {
                while (await Console.In.ReadLineAsync() is { } command)
                {
                    if (command != "write") { result = 2; break; }
                    await app.Dispatcher.InvokeAsync(() => Write(owner.Handle, "external writer"));
                    Console.WriteLine("WRITTEN");
                }
            }
            catch (Exception) { result = 1; }
            finally { await app.Dispatcher.InvokeAsync(() => app.Shutdown()); }
        });
        app.Run();
        return result;
    }

    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int passed = 0, failed = 0;
        app.Startup += async (_, _) =>
        {
            var fixture = new Window { Title = "SnapBar clipboard race fixture", Width = 420, Height = 120,
                Content = "Disposable integration fixture. Do not copy while tests run." };
            using var original = new ClipboardBackup();
            Process? writer = null;
            bool saved = false;
            var pending = new List<Task>();
            try
            {
                fixture.Show(); fixture.Activate();
                var owner = new WindowInteropHelper(fixture).Handle;
                var target = Native.GetIdentity(owner) ?? throw new InvalidOperationException("Missing fixture identity");
                // WPF's initial activation can be delivered after Show returns.
                Console.WriteLine("WAITING: activate the dedicated clipboard race fixture (up to 60 seconds)");
                var activationDeadline = DateTime.UtcNow.AddSeconds(60);
                while (Native.GetIdentity(Native.GetForegroundWindow()) != target && DateTime.UtcNow < activationDeadline)
                { fixture.Activate(); await Task.Delay(25); }
                Require(Native.GetIdentity(Native.GetForegroundWindow()) == target, "Test fixture must be foreground");
                Require(!Native.AnyModifierDown(), "Release modifier keys before running");
                Locked(owner, () => { var status = original.Capture(); Require(status == "ok", status); saved = true; });
                var richFormat = RegisterClipboardFormat("SnapBar.Test.Race.Rich");
                Require(richFormat != 0, "Could not register test format");
                var reader = new ClipboardSelectionReader(owner);
                ClipboardReadPlan Plan(ClipboardReadAttempt first, params ClipboardReadAttempt[] rest) =>
                    new("race-fixture", identity => identity == target, () => true, [first, .. rest], "no-response");
                bool Publish(string text) { Write(owner, text); return true; }
                void Later(int milliseconds, Action work)
                {
                    async Task Complete() { await Task.Delay(milliseconds); work(); }
                    pending.Add(Complete());
                }
                async Task Check(string name, Func<Task> test)
                {
                    try
                    {
                        Require(Native.GetIdentity(Native.GetForegroundWindow()) == target, "Fixture lost foreground");
                        Write(owner, "original paste", richFormat);
                        await test();
                        Require(!reader.IsBusy, "Reader remained busy");
                        Console.WriteLine("PASS " + name); passed++;
                    }
                    catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
                    finally
                    {
                        try { await Task.WhenAll(pending); }
                        catch (Exception e) { Console.WriteLine("FAIL pending producer: " + e.Message); failed++; }
                        pending.Clear();
                    }
                }

                await Check("capture restores Unicode and registered bytes, returning restored sequence", async () =>
                {
                    var result = await reader.CaptureAsync(target, () => true, Plan(new(() => Publish("selection 😀"), true)));
                    Require(result.Selection is { Source: "race-fixture", Text: "selection 😀", Editable: true }, "Missing expected selection");
                    Require(result.ClipboardSequence == Native.GetClipboardSequenceNumber(), "Returned sequence is not restored clipboard");
                    ExpectClipboard(owner, "original paste", richFormat);
                });
                await Check("delayed producer succeeds without duplicate copy", async () =>
                {
                    int copies = 0;
                    var result = await reader.CaptureAsync(target, () => true, Plan(new(() =>
                        { copies++; Later(150, () => Publish("delayed")); return true; }, true)));
                    Require(result.Selection?.Text == "delayed" && copies == 1, "Delayed read did not complete once");
                    ExpectClipboard(owner, "original paste", richFormat);
                });
                await Check("overlapping capture is rejected while original completes", async () =>
                {
                    var first = reader.CaptureAsync(target, () => true, Plan(new(() =>
                        { Later(100, () => Publish("first")); return true; }, true)));
                    var second = await reader.CaptureAsync(target, () => true,
                        Plan(new(() => throw new InvalidOperationException("Duplicate injection"), true)));
                    Require(second.Status == "busy", "Concurrent read was not rejected");
                    Require((await first).Selection?.Text == "first", "Original read failed");
                    ExpectClipboard(owner, "original paste", richFormat);
                });
                await Check("no-response editable attempt falls back with unknown write capability", async () =>
                {
                    int copies = 0;
                    var result = await reader.CaptureAsync(target, () => true, Plan(
                        new(() => { copies++; return true; }, true), new ClipboardReadAttempt(() => { copies++; return Publish("read only"); }, null)));
                    Require(copies == 2 && result.Selection is { Text: "read only", Editable: null }, "Fallback granted write capability");
                    ExpectClipboard(owner, "original paste", richFormat);
                });
                await Check("no copy response leaves clipboard and sequence untouched", async () =>
                {
                    var sequence = Native.GetClipboardSequenceNumber();
                    var result = await reader.CaptureAsync(target, () => true, Plan(new(() => true, null)));
                    Require(result.Status == "no-response" && sequence == Native.GetClipboardSequenceNumber(), "No-response changed clipboard");
                    ExpectClipboard(owner, "original paste", richFormat);
                });
                await Check("input invalidation rejects late data without overwriting uncertain clipboard", async () =>
                {
                    bool current = true;
                    var result = await reader.CaptureAsync(target, () => current, Plan(new(() =>
                        { current = false; Later(100, () => Publish("late selection")); return true; }, true)));
                    Require(result.Status == "clipboard-input-changed" && result.Selection == null, "Invalidated selection was accepted");
                    await Task.WhenAll(pending);
                    ExpectClipboard(owner, "late selection");
                });
                await Check("input invalidation without a copy response cancels instead of timing out", async () =>
                {
                    bool current = true;
                    var result = await reader.CaptureAsync(target, () => current, Plan(new(() =>
                        { current = false; return true; }, true)));
                    Require(result.Status == "clipboard-input-changed" && result.Selection == null, "Cancellation was reported as no response");
                    ExpectClipboard(owner, "original paste", richFormat);
                });
                await Check("second write during stability window is preserved", async () =>
                {
                    var result = await reader.CaptureAsync(target, () => true, Plan(new(() =>
                        { Publish("first version"); Later(40, () => Publish("new version")); return true; }, true)));
                    Require(result.Status == "clipboard-changed" && result.Selection == null, "Unstable clipboard was accepted");
                    ExpectClipboard(owner, "new version");
                });
                await Check("empty copied text restores original clipboard and yields no selection", async () =>
                {
                    var result = await reader.CaptureAsync(target, () => true, Plan(new(() => Publish(""), true)));
                    Require(result.Status == "no-selection" && result.Selection == null, "Empty selection was accepted");
                    ExpectClipboard(owner, "original paste", richFormat);
                });
                await Check("oversized copied text restores original clipboard and is rejected", async () =>
                {
                    var result = await reader.CaptureAsync(target, () => true, Plan(new(() => Publish(new string('x', 32769)), true)));
                    Require(result.Status == "too-large" && result.Selection == null, "Oversized selection was accepted");
                    ExpectClipboard(owner, "original paste", richFormat);
                });
                await Check("failed copy submission releases reader and preserves backup", async () =>
                {
                    var sequence = Native.GetClipboardSequenceNumber();
                    var result = await reader.CaptureAsync(target, () => true, Plan(new(() => false, true)));
                    Require(result.Status == "input-blocked" && sequence == Native.GetClipboardSequenceNumber(), "Failed send changed clipboard");
                    ExpectClipboard(owner, "original paste", richFormat);
                });

                var start = new ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
                // Support both the apphost and an explicit `dotnet <assembly.dll>` invocation.
                if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(typeof(ClipboardRaceTest).Assembly.Location);
                start.ArgumentList.Add("--clipboard-race-writer");
                writer = Process.Start(start) ?? throw new InvalidOperationException("Writer did not start");
                Require(await writer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) == "READY", "Writer did not become ready");
                await Check("different real process owns clipboard: external data is preserved", async () =>
                {
                    var plan = Plan(new(() =>
                    {
                        async Task WriteExternally()
                        {
                            await writer.StandardInput.WriteLineAsync("write");
                            await writer.StandardInput.FlushAsync();
                            Require(await writer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) == "WRITTEN", "Writer failed");
                        }
                        pending.Add(WriteExternally());
                        return true;
                    }, true));
                    var result = await reader.CaptureAsync(target, () => true, plan);
                    Require(result.Status == "clipboard-other-owner" && result.Selection == null, "External producer was accepted");
                    ExpectClipboard(owner, "external writer");
                });
                await Check("source closes during pending copy: result is rejected", async () =>
                {
                    var result = await reader.CaptureAsync(target, () => true, Plan(new(() =>
                        { Publish("closing source"); fixture.Close(); return true; }, true)));
                    Require(result.Status == "target-changed" && result.Selection == null, "Closed target result: " + result.Status);
                    ExpectClipboard(0, "closing source");
                });
            }
            catch (Exception e) { Console.WriteLine("FAIL fixture: " + e.Message); failed++; }
            finally
            {
                try { await Task.WhenAll(pending); }
                catch (Exception e) { Console.WriteLine("FAIL producer cleanup: " + e.Message); failed++; }
                if (writer != null)
                {
                    try
                    {
                        writer.StandardInput.Close();
                        await writer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        Require(writer.ExitCode == 0, "Writer exited with an error");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("FAIL writer cleanup: " + e.Message); failed++;
                        if (!writer.HasExited) writer.Kill(entireProcessTree: true);
                    }
                    finally { writer.Dispose(); }
                }
                if (saved)
                {
                    try
                    {
                        // The original fixture may already be closed; use a fresh hidden owner.
                        using var cleanup = new HwndSource(new HwndSourceParameters("SnapBar race cleanup") { Width = 1, Height = 1, WindowStyle = 0 });
                        Locked(cleanup.Handle, () => Require(original.Restore(), "Original clipboard restoration failed"));
                        Console.WriteLine("Original desktop clipboard restored");
                    }
                    catch (Exception e) { Console.WriteLine("FAIL clipboard cleanup: " + e.Message); failed++; }
                }
                fixture.Close();
                Console.WriteLine($"{passed} passed, {failed} failed");
                app.Shutdown();
            }
        };
        app.Run();
        return failed == 0 ? 0 : 1;
    }
}
