using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class AppExclusionTest
{
    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int passed = 0, result = 1;
        void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name); passed++;
        }
        app.Startup += async (_, _) =>
        {
            Process? child = null;
            try
            {
                using var owner = new HwndSource(new HwndSourceParameters("SnapBar exclusion test")
                    { Width = 1, Height = 1, WindowStyle = 0 });
                var start = new ProcessStartInfo(TestPaths.AppExecutable) { UseShellExecute = false };
                start.ArgumentList.Add("--fixture");
                child = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not start");
                var deadline = DateTime.UtcNow.AddSeconds(60);
                Console.WriteLine("WAITING: activate the dedicated SnapBar Test Fixture (up to 60 seconds)");
                while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != child.Id)
                {
                    if (child.HasExited || DateTime.UtcNow > deadline) throw new InvalidOperationException("Fixture must be foreground");
                    await Task.Delay(25);
                }
                await Task.Delay(100);
                var target = Native.GetIdentity(Native.GetForegroundWindow()) ?? throw new InvalidOperationException("Missing fixture identity");
                var path = Native.GetExecutablePath(target) ?? throw new InvalidOperationException("Missing executable path");
                var entry = AppExclusionPolicy.Create(path, "Dedicated fixture");
                Require(Native.GetExecutablePath(target with { ProcessStarted = target.ProcessStarted + 1 }) == null,
                    "reused process identity cannot supply a trusted executable path");
                using var controller = new SelectionController(owner, TestPaths.ProbeExecutable);
                controller.Automatic = true;
                controller.SetExcludedApplications([entry]);
                var messages = new List<string>();
                controller.Status += messages.Add;
                var sequence = Native.GetClipboardSequenceNumber();
                var helpersBefore = Process.GetProcessesByName("SnapBar.Probe").Select(p => { using (p) return p.Id; }).ToArray();
                Native.GetWindowRect((nint)target.Window, out var rect);
                int x = rect.Left + 120, y = rect.Top + 180;
                await controller.CaptureAsync(true, x, y);
                var helpersAfter = Process.GetProcessesByName("SnapBar.Probe").Select(p => { using (p) return p.Id; }).ToArray();
                Require(controller.ActiveSelection == null && messages.Count == 0
                    && helpersBefore.Order().SequenceEqual(helpersAfter.Order()) && Native.GetClipboardSequenceNumber() == sequence,
                    "excluded automatic gesture does not launch helper, expose text or change clipboard");
                await controller.CaptureAsync(false, x, y);
                Require(controller.ActiveSelection is { Text: "SnapBar", Source: "uia", Editable: true },
                    "manual capture still reads an excluded app through native validation");
                controller.SetExcludedApplications([]);
                Require(controller.ActiveSelection == null, "changing exclusion list invalidates an existing snapshot");
                await controller.CaptureAsync(true, x, y);
                Require(controller.ActiveSelection is { Text: "SnapBar", Source: "uia" },
                    "removing exclusion restores automatic capture");
                controller.Automatic = false;
                controller.SetExcludedApplications([entry]);
                await controller.CaptureAsync(true, x, y);
                await controller.CaptureAsync(false, x, y);
                Require(controller.ActiveSelection is { Text: "SnapBar", Source: "uia" },
                    "manual capture works while automatic detection is paused and app is excluded");
                messages.Clear();
                var pending = controller.CaptureAsync(false, x, y);
                controller.SetExcludedApplications([]);
                await pending;
                Require(controller.ActiveSelection == null && messages.Count == 0 && Native.GetClipboardSequenceNumber() == sequence,
                    "exclusion change cancels a pending capture without publishing stale results");
                result = 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); }
            finally
            {
                if (child != null)
                {
                    if (!child.HasExited) child.CloseMainWindow();
                    child.Dispose();
                }
                Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed");
                app.Shutdown();
            }
        };
        app.Run();
        return result;
    }
}
