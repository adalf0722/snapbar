using System.Diagnostics;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace SnapBar.Windows.Ocr;

public static class OcrWorker
{
    internal const int MaximumInputBytes = 80_000_000;
    internal const int MaximumOutputBytes = 1_048_576;

    public static async Task<OcrResult> RecognizeAsync(string executable, byte[] png, bool preferEnglish,
        CancellationToken token, TimeSpan? timeout = null)
    {
        token.ThrowIfCancellationRequested();
        if (png.Length == 0 || png.Length > MaximumInputBytes) return new("protocol");
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        using var process = new Process { StartInfo = new(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        } };
        process.StartInfo.ArgumentList.Add("--ocr-worker");
        if (preferEnglish) process.StartInfo.ArgumentList.Add("--english");
        Task? transfer = null, stderr = null, exit = null;
        Task<byte[]>? response = null;
        try
        {
            // Configure the job before starting the child. The child waits for input, and exits on
            // EOF, closing the small start/assignment window if the parent exits there.
            using var job = WorkerJob.Create();
            if (!process.Start()) return new("worker");
            try
            {
                job.Assign(process);
                linked.Token.ThrowIfCancellationRequested();
                stderr = DrainAsync(process.StandardError.BaseStream, linked.Token);
                response = ReadBoundedAsync(process.StandardOutput.BaseStream, MaximumOutputBytes, linked.Token);
                transfer = SendAsync(process.StandardInput.BaseStream, png, linked.Token);
                exit = process.WaitForExitAsync(linked.Token);
                var pending = new List<Task> { transfer, response, exit };
                while (pending.Count != 0)
                {
                    var finished = await Task.WhenAny(pending);
                    await finished; // A pipe failure must terminate the child immediately.
                    pending.Remove(finished);
                }
                if (process.ExitCode != 0) return new("worker");
                var result = JsonSerializer.Deserialize<OcrResult>(response.Result);
                return result != null && (result.Status == "ok" && !string.IsNullOrWhiteSpace(result.Text)
                    || result.Status is "empty" or "models" or "runtime" or "recognition" && result.Text == null)
                    ? result : new("protocol");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                linked.Cancel();
                foreach (var task in new Task?[] { transfer, response, stderr, exit })
                    if (task != null) try { await task; } catch (Exception) { /* Observe closed/canceled pipes. */ }
            }
        }
        catch (OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return new("timeout");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        { token.ThrowIfCancellationRequested(); return deadline.IsCancellationRequested ? new("timeout") : new("worker"); }
    }

    private static async Task SendAsync(Stream stream, byte[] png, CancellationToken token)
    {
        await stream.WriteAsync(png, token);
        await stream.FlushAsync(token);
        stream.Close();
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > maximum) throw new IOException("OCR protocol limit exceeded.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static async Task DrainAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[4096];
        while (await stream.ReadAsync(buffer, token) != 0) { }
    }

    public static async Task<int> RunAsync(bool preferEnglish)
    {
        using var output = Console.OpenStandardOutput();
        // Tesseract/Leptonica can emit native diagnostics; never put those on protocol pipes.
        using var nul = File.OpenWrite("NUL");
        SetStdHandle(-11, nul.SafeFileHandle.DangerousGetHandle());
        SetStdHandle(-12, nul.SafeFileHandle.DangerousGetHandle());
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        try
        {
            var png = await ReadBoundedAsync(Console.OpenStandardInput(), MaximumInputBytes, CancellationToken.None);
            if (!HasValidDimensions(png)) return 2;
            var result = await ScreenOcr.RecognizeAsync(png, CancellationToken.None, preferEnglish);
            var json = JsonSerializer.SerializeToUtf8Bytes(result);
            if (json.Length > MaximumOutputBytes) return 2;
            await output.WriteAsync(json);
            await output.FlushAsync();
            return 0;
        }
        catch (Exception) { return 2; }
    }

    internal static bool HasValidDimensions(ReadOnlySpan<byte> png)
    {
        if (png.Length < 33 || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || BinaryPrimitives.ReadUInt32BigEndian(png[8..]) != 13 || !png[12..16].SequenceEqual("IHDR"u8)) return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        return width >= 4 && height >= 4 && (ulong)width * height <= 16_000_000;
    }

    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, nint handle);

    private sealed class WorkerJob : IDisposable
    {
        private readonly SafeFileHandle handle;
        private WorkerJob(SafeFileHandle handle) { this.handle = handle; }
        public static WorkerJob Create()
        {
            var handle = CreateJobObjectW(0, null);
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception();
            var info = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
            if (!SetInformationJobObject(handle, 9, ref info, Marshal.SizeOf<ExtendedLimits>()))
            { handle.Dispose(); throw new System.ComponentModel.Win32Exception(); }
            return new(handle);
        }
        public void Assign(Process process)
        {
            if (!AssignProcessToJobObject(handle, process.Handle)) throw new System.ComponentModel.Win32Exception();
        }
        public void Dispose() => handle.Dispose();
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
        { public long ProcessTime, JobTime; public uint Flags; public nuint MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcesses; public nuint Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
        { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(nint attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits info, int length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
    }
}
