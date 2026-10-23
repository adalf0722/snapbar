using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Windows;

internal static class UiaReadinessTest
{
    public static int Run(bool deepTree = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        app.Startup += async (_, _) =>
        {
            Process? child = null;
            try
            {
                using var owner = new HwndSource(new HwndSourceParameters("SnapBar UIA readiness test") { Width = 1, Height = 1 });
                var start = new ProcessStartInfo(Path.GetFullPath("artifacts/p0/SnapBar.App.exe")) { UseShellExecute = false };
                start.ArgumentList.Add("--fixture"); start.ArgumentList.Add(deepTree ? "--deep-uia" : "--delayed-uia");
                child = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not start");
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != child.Id)
                {
                    if (child.HasExited || DateTime.UtcNow > deadline) throw new InvalidOperationException("Dedicated fixture is not foreground");
                    await Task.Delay(25);
                }
                // Focus/selection are set by the fixture's Loaded handler. Do not inspect its
                // automation tree before capture: that would warm the provider under test.
                await Task.Delay(100);
                using var controller = new SelectionController(owner, Path.GetFullPath("artifacts/p0/Probe/SnapBar.Probe.exe"));
                var messages = new List<string>();
                controller.Status += status => { messages.Add(status); Console.WriteLine("STATUS " + status); };
                var sequence = Native.GetClipboardSequenceNumber();
                await controller.CaptureAsync(false, 0, 0);
                if (controller.ActiveSelection is not { Source: "uia", Text: "SnapBar", Editable: true })
                    throw new InvalidOperationException("Delayed provider did not yield the expected native selection");
                if (!deepTree && !messages.Any(message => message.Contains("正在等候")))
                    throw new InvalidOperationException("Provider was already warm; cold path was not exercised");
                if (deepTree && messages.Any(message => message.Contains("正在等候")))
                    throw new InvalidOperationException("Deep selection should be found in first query");
                if (Native.GetClipboardSequenceNumber() != sequence) throw new InvalidOperationException("Native readiness changed clipboard");
                Console.WriteLine(deepTree ? "PASS deeply nested native selection belongs to target and succeeds in first query"
                    : "PASS delayed native provider succeeds within one capture without clipboard changes");
                messages.Clear();
                await controller.CaptureAsync(false, 0, 0);
                if (controller.ActiveSelection is not { Source: "uia", Text: "SnapBar" }
                    || messages.Any(message => message.Contains("正在等候")))
                    throw new InvalidOperationException("Warm native provider incurred unnecessary retry");
                Console.WriteLine("PASS warm native provider succeeds without readiness waits");
                result = 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); }
            finally
            {
                // Only our child fixture is closed; no existing user App is terminated.
                if (child != null)
                {
                    if (!child.HasExited) child.CloseMainWindow();
                    child.Dispose();
                }
                app.Shutdown();
            }
        };
        app.Run();
        return result;
    }
}
