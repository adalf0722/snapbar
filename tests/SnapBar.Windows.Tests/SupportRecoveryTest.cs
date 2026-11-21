using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class SupportRecoveryTest
{
    private const string PrivateText = "support-test-private-text";
    private const string ProbeFixtureVariable = "SNAPBAR_SUPPORT_TEST_PROBE";
    private static string Executable => Path.Combine(AppContext.BaseDirectory, "SnapBar.Windows.Tests.exe");

    internal static async Task<int> RunAsync()
    {
        var directory = Directory.CreateTempSubdirectory("snapbar-support-test-");
        try
        {
            var session = new SupportSession(directory.FullName);
            var diagnostics = new SelectionDiagnostics();
            session.Start();
            for (var index = 0; index < 200; index++)
                diagnostics.Record(index, "capture-result", new("ok",
                    new("fixture", new(1, 2, 3), PrivateText, null, false, 1, PrivateText.Length),
                    Diagnostics: new(NativeSource: PrivateText)));
            using var report = JsonDocument.Parse(session.CreateReport(diagnostics));
            var events = report.RootElement.GetProperty("events");
            Check(events.GetArrayLength() == 128 && events[0].GetProperty("capture").GetInt64() == 72 &&
                events[127].GetProperty("capture").GetInt64() == 199, "diagnostics retain the latest 128 events in order");
            Check(!report.RootElement.GetRawText().Contains(PrivateText), "selection and native source stay out of reports");
            Check(!File.Exists(Path.Combine(directory.FullName, "reproduction.json")),
                "ordinary selections do not write a trace file");
            var response = new ProbeResponse("ok");
            var elapsed = Stopwatch.StartNew();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 100_000; index++)
                diagnostics.Record(index, "capture-result", response);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            elapsed.Stop();
            Check(allocated == 0, "the default event recorder performs no per-event managed allocation");
            Console.WriteLine($"Recorder sample: {elapsed.Elapsed.TotalMilliseconds:F2} ms for 100,000 events");

            var recording = diagnostics.RecordReproductionAsync(session, TimeSpan.FromMilliseconds(100));
            Check(diagnostics.Enabled, "reproduction opts in to Probe metadata");
            diagnostics.RecordStatus(200, "popup-present", "ok");
            await recording;
            using var trace = JsonDocument.Parse(session.CreateReport(diagnostics));
            events = trace.RootElement.GetProperty("reproduction").GetProperty("events");
            Check(!diagnostics.Enabled && events.GetArrayLength() == 1 && events[0].GetProperty("capture").GetInt64() == 200,
                "reproduction ends and keeps only the opted-in events");
            session.Complete();
            var clean = new SupportSession(directory.FullName);
            clean.Start();
            Check(!clean.PreviousInterrupted, "normal shutdown does not report an interruption");
            clean.Complete();

            var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--support-crash-fixture");
            start.ArgumentList.Add(directory.FullName);
            using var crashed = Process.Start(start) ?? throw new InvalidOperationException("Could not launch crash fixture.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await crashed.WaitForExitAsync(timeout.Token);
            var recovered = new SupportSession(directory.FullName);
            recovered.Start();
            using var crash = JsonDocument.Parse(recovered.CreateReport(diagnostics));
            var previous = crash.RootElement.GetProperty("previousCrash");
            Check(crashed.ExitCode == 42 && recovered.PreviousInterrupted &&
                previous.GetProperty("frames").GetArrayLength() > 0, "next launch can export a fatal summary from a separate process");
            Check(!crash.RootElement.GetRawText().Contains(PrivateText), "exception messages stay out of reports");
            var interrupted = new SupportSession(directory.FullName);
            interrupted.Start();
            using var unknown = JsonDocument.Parse(interrupted.CreateReport(diagnostics));
            Check(interrupted.PreviousInterrupted && unknown.RootElement.GetProperty("previousCrash").ValueKind == JsonValueKind.Null,
                "an unclean exit without a matching fatal summary has an unknown cause");

            var previousFixture = Environment.GetEnvironmentVariable(ProbeFixtureVariable);
            Environment.SetEnvironmentVariable(ProbeFixtureVariable, "1");
            try
            {
                using var probe = new ProbeClient(Executable);
                Check((await probe.RequestAsync(new("exit"))).Status == "helper-exited", "Probe exit is classified");
                Check((await probe.RequestAsync(new("ping"))).Success, "Probe requests recover after helper exit");
                Check((await probe.RequestAsync(new("hang"), TimeSpan.FromMilliseconds(250))).Status == "timeout",
                    "a stuck Probe request times out");
                Check((await probe.RequestAsync(new("ping"))).Success, "Probe requests recover after timeout");
            }
            finally { Environment.SetEnvironmentVariable(ProbeFixtureVariable, previousFixture); }
            return 0;
        }
        finally { directory.Delete(recursive: true); }
    }

    internal static int RunCrashFixture(string directory)
    {
        var session = new SupportSession(directory);
        session.Start();
        try { throw new InvalidOperationException(PrivateText); }
        catch (InvalidOperationException error) { session.RecordCrash(error, new()); }
        return 42;
    }

    internal static int RunProbeFixture()
    {
        while (Console.ReadLine() is { } line)
        {
            var request = JsonSerializer.Deserialize<ProbeRequest>(line);
            if (request?.Operation == "exit") return 42;
            if (request?.Operation == "hang") Thread.Sleep(Timeout.Infinite);
            Console.WriteLine(JsonSerializer.Serialize(new ProbeResponse("ok")));
        }
        return 0;
    }

    private static void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS {name}");
    }
}
