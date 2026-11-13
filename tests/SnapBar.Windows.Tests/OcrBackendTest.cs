using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using SnapBar.Core;
using SnapBar.Windows.Ocr;

internal static class OcrBackendTest
{
    internal static int FakeWorker(bool english)
    {
        var mode = Environment.GetEnvironmentVariable("SNAPBAR_OCR_TEST_MODE");
        File.WriteAllText(Environment.GetEnvironmentVariable("SNAPBAR_OCR_TEST_PID")!, Environment.ProcessId.ToString());
        if (mode == "read-stall") Console.OpenStandardInput().CopyTo(Stream.Null);
        if (mode is "stall" or "read-stall") { Thread.Sleep(Timeout.Infinite); return 0; }
        Console.OpenStandardInput().CopyTo(Stream.Null);
        if (mode == "null") { Console.Write("null"); return 0; }
        if (mode == "bad-status") { Console.Write("{\"Status\":\"unexpected\"}"); return 0; }
        if (mode == "large-output") { Console.Write(new string('x', OcrWorker.MaximumOutputBytes + 1)); return 0; }
        Console.Write("{\"Status\":\"ok\",\"Text\":\"" + (english ? "english" : "traditional") + "\"}");
        return 0;
    }

    internal static int Supervisor()
    {
        OcrWorker.RecognizeAsync(Environment.ProcessPath!, new byte[8192], false, CancellationToken.None,
            TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
        return 0;
    }

    internal static async Task<int> MeasureEngineAsync()
    {
        using var bitmap = new Bitmap(640, 140);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 32))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("SnapBar OCR test 123", font, Brushes.Black, 20, 30);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var png = stream.ToArray();
        for (int run = 0; run < 6; run++)
        {
            var clock = Stopwatch.StartNew();
            var result = await ScreenOcr.RecognizeAsync(png, default, true);
            Console.WriteLine($"ENGINE run={run} pixels=89600 elapsed_ms={clock.Elapsed.TotalMilliseconds:F1} peak_bytes={Process.GetCurrentProcess().PeakWorkingSet64} status={result.Status}");
            if (result.Status != "ok") return 1;
        }
        var worker = await OcrWorker.RecognizeAsync(Path.Combine(AppContext.BaseDirectory, "SnapBar.App.exe"), png, true, default);
        Console.WriteLine("APP_WORKER status=" + worker.Status);
        return worker.Status == "ok" ? 0 : 1;
    }

    internal static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
        void Reject(Action action, string name)
        {
            try { action(); } catch (ArgumentOutOfRangeException) { Check(true, name); return; }
            throw new Exception(name);
        }
        using var bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.Red);
        bitmap.SetPixel(4, 5, Color.Blue);
        using var snapshot = OcrSnapshot.FromBitmap(new(-20, -30, 16, 16), bitmap);
        bitmap.SetPixel(4, 5, Color.Green);
        using (var stream = new MemoryStream(snapshot.CropPng(new(-16, -25, 4, 4))))
        using (var crop = new Bitmap(stream)) Check(crop.GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb(), "negative-coordinate crop retains frozen original pixels");
        Check(snapshot.CopyBgraPixels()[(5 * 16 + 4) * 4] == 255, "display BGRA matches crop pixels");
        Reject(() => snapshot.CropPng(new(-20, -30, 3, 4)), "small crop rejected");
        Reject(() => snapshot.CropPng(new(-21, -30, 4, 4)), "outside crop rejected");
        Reject(() => snapshot.CropPng(new(-20, -30, 5000, 5000)), "oversized crop rejected before allocation");
        Reject(() => snapshot.CropPng(new(double.NaN, -30, 4, 4)), "nonfinite crop rejected");
        Reject(() => snapshot.CropPng(new(-19.5, -30, 4, 4)), "fractional crop rejected");
        snapshot.Dispose();
        try { snapshot.CopyBgraPixels(); throw new Exception("disposed snapshot usable"); }
        catch (ObjectDisposedException) { Check(true, "disposed snapshot rejected"); }
        try { await OcrWorker.ReadBoundedAsync(new MemoryStream(new byte[33]), 32, default); throw new Exception("unbounded protocol"); }
        catch (IOException) { Check(true, "protocol rejects oversized stream"); }
        Check((await OcrWorker.RecognizeAsync("missing", new byte[OcrWorker.MaximumInputBytes + 1], false, default)).Status == "protocol", "oversized input rejected before launch");
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            var png = stream.ToArray();
            Check(OcrWorker.HasValidDimensions(png), "valid PNG dimensions accepted");
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), uint.MaxValue);
            Check(!OcrWorker.HasValidDimensions(png), "oversized decoded PNG rejected before native allocation");
        }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await OcrWorker.RecognizeAsync("missing", [1], false, canceled.Token); throw new Exception("pre-cancel launched"); }
            catch (OperationCanceledException) { Check(true, "pre-canceled call rejects before launch"); }
        }

        var pidPath = Path.Combine(Path.GetTempPath(), "SnapBar.Ocr.Worker." + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SNAPBAR_OCR_TEST_PID", pidPath);
        async Task<int> Pid()
        {
            var until = Stopwatch.StartNew();
            while (until.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (File.Exists(pidPath) && int.TryParse(await File.ReadAllTextAsync(pidPath), out var pid)) return pid;
                await Task.Delay(20);
            }
            throw new Exception("worker did not start");
        }
        bool Exited(int pid) { try { using var p = Process.GetProcessById(pid); return p.HasExited; } catch (ArgumentException) { return true; } }
        try
        {
            foreach (var mode in new[] { "stall", "read-stall" })
            {
                Environment.SetEnvironmentVariable("SNAPBAR_OCR_TEST_MODE", mode);
                using var cancel = new CancellationTokenSource();
                var task = OcrWorker.RecognizeAsync(Environment.ProcessPath!, new byte[8_000_000], false, cancel.Token);
                var pid = await Pid();
                cancel.Cancel();
                try { await task; throw new Exception("cancel returned success"); }
                catch (OperationCanceledException) { Check(Exited(pid), mode + " cancellation exits owned worker before returning"); }
                File.Delete(pidPath);
            }
            Environment.SetEnvironmentVariable("SNAPBAR_OCR_TEST_MODE", "read-stall");
            var timeout = OcrWorker.RecognizeAsync(Environment.ProcessPath!, new byte[8192], false, default, TimeSpan.FromSeconds(1));
            var timeoutPid = await Pid();
            Check((await timeout).Status == "timeout" && Exited(timeoutPid), "timeout exits stalled native equivalent");
            File.Delete(pidPath);
            using (var supervisor = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--ocr-test-supervisor" } })!)
            {
                var pid = await Pid();
                supervisor.Kill();
                await supervisor.WaitForExitAsync();
                for (int retry = 0; retry < 100 && !Exited(pid); retry++) await Task.Delay(20);
                Check(Exited(pid), "parent exit closes job and terminates owned worker");
            }
            File.Delete(pidPath);
            Environment.SetEnvironmentVariable("SNAPBAR_OCR_TEST_MODE", "ok");
            Check((await OcrWorker.RecognizeAsync(Environment.ProcessPath!, new byte[8192], true, default)).Text == "english", "next worker completes and receives language preference");
            foreach (var mode in new[] { "null", "bad-status", "large-output" })
            {
                Environment.SetEnvironmentVariable("SNAPBAR_OCR_TEST_MODE", mode);
                Check((await OcrWorker.RecognizeAsync(Environment.ProcessPath!, new byte[8192], false, default)).Status is "protocol" or "worker", mode + " output rejected");
            }
        }
        finally
        {
            File.Delete(pidPath);
            Environment.SetEnvironmentVariable("SNAPBAR_OCR_TEST_PID", null);
            Environment.SetEnvironmentVariable("SNAPBAR_OCR_TEST_MODE", null);
        }
        Console.WriteLine($"{passed} passed");
        return 0;
    }
}
