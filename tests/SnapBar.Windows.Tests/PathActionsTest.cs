using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class PathActionsTest
{
    private sealed class RecordingSession : IPathRevealSession
    {
        public string? PreparedPath;
        public int Reveals;
        public bool Disposed;
        public Func<CancellationToken, Task<string>> Prepare = _ => Task.FromResult("ok");
        public Task<string> PrepareAsync(string path, CancellationToken token) { PreparedPath = path; return Prepare(token); }
        public Task<string> RevealAsync(TargetIdentity target, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Reveals++; return Task.FromResult("ok"); }
        public void Dispose() => Disposed = true;
    }

    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int passed = 0, result = 1;
        void Check(bool condition, string name)
        { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
        app.Startup += async (_, _) =>
        {
            Process? child = null;
            try
            {
                using var owner = new HwndSource(new HwndSourceParameters("SnapBar path actions test") { Width = 1, Height = 1, WindowStyle = 0 });
                var start = new ProcessStartInfo(TestPaths.AppExecutable) { UseShellExecute = false };
                start.ArgumentList.Add("--fixture"); start.ArgumentList.Add("--path-fixture");
                child = Process.Start(start)!;
                Console.WriteLine("WAITING: activate the dedicated SnapBar path fixture (up to 60 seconds)");
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != child.Id)
                { if (child.HasExited || DateTime.UtcNow > deadline) throw new Exception("Fixture is not foreground"); await Task.Delay(25); }
                await Task.Delay(100);
                var sessions = new List<RecordingSession>();
                RecordingSession? next = null;
                using var controller = new SelectionController(owner, TestPaths.ProbeExecutable,
                    createPathSession: () => { var session = next ?? new(); next = null; sessions.Add(session); return session; });
                controller.ConfigureActions(ActionCatalog.SetPlacement(new(), "path", ActionPlacement.Bar));
                var statuses = new List<string>(); controller.Status += statuses.Add;
                var expected = TestPaths.FixtureReadme;
                async Task Capture()
                {
                    await controller.CaptureAsync(false, 0, 0);
                    if (controller.ActiveSelection is not { Editable: false, Source: "uia" } selection || selection.Text != expected)
                        throw new Exception("Expected readonly path fixture");
                }
                Button Button(TextAction action) => app.Windows.OfType<PopupWindow>().Single(p => p.IsVisible).FindActionButton(action);
                var sequence = Native.GetClipboardSequenceNumber();
                await Capture();
                Check(Button(TextAction.RevealPath).IsVisible && Button(TextAction.RevealPath).IsEnabled
                    && !Button(TextAction.Cut).IsEnabled && !Button(TextAction.Paste).IsEnabled,
                    "readonly path offers Reveal while writes stay disabled");
                Check(Button(TextAction.OpenLink).Visibility == Visibility.Collapsed
                    && Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == child.Id,
                    "path toolbar hides Open Link and preserves source foreground");
                var completion = new TaskCompletionSource();
                void Completed(string _) => completion.TrySetResult();
                controller.Status += Completed;
                try { Button(TextAction.RevealPath).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    await completion.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
                finally { controller.Status -= Completed; }
                Check(sessions.Count == 1 && sessions[0].PreparedPath == expected && sessions[0].Reveals == 1 && sessions[0].Disposed,
                    "actual Reveal Click event prepares and reveals the exact path once, then disposes");

                await Capture();
                next = new() { Prepare = _ => { controller.Automatic = false; return Task.FromResult("ok"); } };
                await controller.ExecuteAsync(TextAction.RevealPath);
                Check(sessions[^1].Reveals == 0 && sessions[^1].Disposed,
                    "selection invalidated during preparation never reveals a late result");

                await Capture();
                next = new() { Prepare = async token =>
                {
                    controller.Automatic = false;
                    await Task.Delay(1);
                    Check(token.IsCancellationRequested, "input invalidation cancels the path worker token");
                    return "cancelled";
                } };
                await controller.ExecuteAsync(TextAction.RevealPath);
                Check(sessions[^1].Reveals == 0 && sessions[^1].Disposed, "cancelled action releases its prepared session");

                await Capture();
                var element = AutomationElement.FromHandle(child.MainWindowHandle).FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "EditableSample"));
                var pattern = (System.Windows.Automation.TextPattern)element.GetCurrentPattern(System.Windows.Automation.TextPattern.Pattern);
                var fullRange = pattern.GetSelection()[0].Clone();
                next = new() { Prepare = _ =>
                {
                    var changed = fullRange.Clone();
                    changed.MoveEndpointByUnit(System.Windows.Automation.Text.TextPatternRangeEndpoint.End,
                        System.Windows.Automation.Text.TextUnit.Character, -1);
                    changed.Select();
                    return Task.FromResult("ok");
                } };
                await controller.ExecuteAsync(TextAction.RevealPath);
                Check(sessions[^1].Reveals == 0 && statuses.Last().Contains("已取消"),
                    "second native validation rejects a programmatic range change after prepare");
                fullRange.Select();

                await Capture();
                next = new() { Prepare = _ => Task.FromResult("path-unavailable") };
                await controller.ExecuteAsync(TextAction.RevealPath);
                Check(sessions[^1].Reveals == 0 && statuses.Last().Contains("找不到項目") && !statuses.Last().Contains(expected),
                    "unavailable path produces neutral feedback without leaking its path");

                await Capture();
                next = new() { Prepare = _ => Task.FromResult("path-timeout") };
                await controller.ExecuteAsync(TextAction.RevealPath);
                Check(sessions[^1].Reveals == 0 && statuses.Last().Contains("逾時"), "path timeout never reaches reveal");
                Check(Native.GetClipboardSequenceNumber() == sequence, "native path actions preserve desktop clipboard");

                controller.Dispose();
                using var actual = new SelectionController(owner, TestPaths.ProbeExecutable);
                var actualStatuses = new List<string>(); actual.Status += actualStatuses.Add;
                await actual.CaptureAsync(false, 0, 0);
                await actual.ExecuteAsync(TextAction.RevealPath);
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
                try
                {
                    var shellDeadline = DateTime.UtcNow.AddSeconds(8);
                    while (!PathShellSmokeTest.IsSelected(shell, Path.GetDirectoryName(expected)!, expected))
                    { if (DateTime.UtcNow > shellDeadline) throw new Exception("Controller did not reveal exact fixture path: " + string.Join(" | ", actualStatuses)); await Task.Delay(100); }
                    Check(actualStatuses.Any(status => status.Contains("已交由檔案總管"))
                        && Native.GetClipboardSequenceNumber() == sequence,
                        "real controller revalidates readonly selection and reveals exact item through isolated Shell worker");
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
                result = 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); }
            finally
            {
                if (child != null) { if (!child.HasExited) child.CloseMainWindow(); child.Dispose(); }
                Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed"); app.Shutdown();
            }
        };
        app.Run(); return result;
    }
}
