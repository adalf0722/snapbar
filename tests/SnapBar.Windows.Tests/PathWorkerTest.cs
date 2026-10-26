using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SnapBar.Core;
using SnapBar.Windows;

internal static class PathWorkerTest
{
    // Deterministic child used only by this test executable. It never opens Shell.
    public static int RunFake()
    {
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            var request = JsonSerializer.Deserialize<ProbeRequest>(line)!;
            if (request.Operation == "stall") Thread.Sleep(Timeout.Infinite);
            Console.WriteLine(JsonSerializer.Serialize(new ProbeResponse("ok", ElapsedMs: Environment.ProcessId)));
        }
        return 0;
    }

    public static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool condition, string name)
        { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
        var probe = Path.GetFullPath("artifacts/p0/Probe/SnapBar.Probe.exe");
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar-Path-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "中文 space, & 😀.txt");
        File.WriteAllText(file, "disposable fixture");
        try
        {
            using var worker = new ProbeClient(probe, pathWorker: true);
            Check((await worker.RequestAsync(new("prepare-path", Path: file))).Success,
                "native worker prepares a Unicode file with spaces, comma and ampersand");
            Check((await worker.RequestAsync(new("reveal-path"))).Status == "target-changed",
                "prepared item cannot reveal without an explicit foreground identity");
            Check((await worker.RequestAsync(new("reveal-path"))).Status == "path-not-prepared",
                "prepared item is consumed even when reveal is rejected");
            Check((await worker.RequestAsync(new("prepare-path", Path: directory))).Success,
                "native worker prepares a directory");
            Check((await worker.RequestAsync(new("prepare-path", Path: file + ".missing"))).Status == "path-unavailable",
                "missing item returns a neutral unavailable status");
            Check((await worker.RequestAsync(new("reveal-path"))).Status == "path-not-prepared",
                "failed preparation clears the previous prepared directory");
            Check((await worker.RequestAsync(new("prepare-path", Path: "relative.txt"))).Status == "invalid-path",
                "native worker independently rejects malformed paths");
            Check((await worker.RequestAsync(new("prepare-path", Path: Path.GetPathRoot(file)))).Success,
                "native worker prepares the drive root");
            var replacedFolder = Path.Combine(directory, "replaced-folder");
            Directory.CreateDirectory(replacedFolder);
            Check((await worker.RequestAsync(new("prepare-path", Path: replacedFolder))).Success,
                "native worker prepares a folder before replacement");
            Directory.Delete(replacedFolder);
            File.WriteAllText(replacedFolder, "disposable replacement file");
            Check((await worker.RequestAsync(new("reveal-path", Target: Native.GetIdentity(Native.GetForegroundWindow())))).Status == "path-unavailable",
                "folder replaced by a file cannot invoke its handler");

            using var fake = new ProbeClient(Environment.ProcessPath!, pathWorker: true);
            int first = (int)(await fake.RequestAsync(new("ping"))).ElapsedMs;
            using var cancel = new CancellationTokenSource(100);
            var watch = Stopwatch.StartNew();
            Check((await fake.RequestAsync(new("stall"), cancellationToken: cancel.Token)).Status == "cancelled"
                && watch.Elapsed < TimeSpan.FromSeconds(2), "cancellation interrupts a stalled worker");
            Check(await Exited(first), "cancelled worker process terminates");
            int second = (int)(await fake.RequestAsync(new("ping"))).ElapsedMs;
            Check(second != first && second > 0, "next request starts a fresh worker after cancellation");
            watch.Restart();
            Check((await fake.RequestAsync(new("stall"), TimeSpan.FromMilliseconds(150))).Status == "timeout"
                && watch.Elapsed < TimeSpan.FromSeconds(2), "request deadline interrupts a stalled worker");
            Check(await Exited(second), "timed-out worker process terminates");
            int third = (int)(await fake.RequestAsync(new("ping"))).ElapsedMs;
            using var alreadyCancelled = new CancellationTokenSource(); alreadyCancelled.Cancel();
            Check((await fake.RequestAsync(new("ping"), cancellationToken: alreadyCancelled.Token)).Status == "cancelled"
                && (int)(await fake.RequestAsync(new("ping"))).ElapsedMs == third,
                "pre-cancelled request sends nothing and retains an idle worker");
            fake.Dispose();
            Check(await Exited(third), "disposing path client releases its idle worker");
            Console.WriteLine($"{passed} passed, 0 failed"); return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL path worker: " + e.GetType().Name + " " + e.Message); return 1; }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("SnapBar-Path-Test-", StringComparison.Ordinal))
                throw new Exception("Refusing cleanup outside the dedicated temporary fixture");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static async Task<bool> Exited(int processId)
    {
        try { using var process = Process.GetProcessById(processId); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); return true; }
        catch (ArgumentException) { return true; }
    }
}
