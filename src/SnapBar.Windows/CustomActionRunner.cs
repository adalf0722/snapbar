using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SnapBar.Core;

namespace SnapBar.Windows;

public sealed record ScriptExecutionResult(string Status, string Output = "", string Error = "", int? ExitCode = null,
    bool OutputTruncated = false, bool ErrorTruncated = false, bool CaptureIncomplete = false,
    string? ProductErrorCode = null, string? ProductErrorArgument = null)
{
    public bool Success => Status == "ok";
}

public static class CustomActionRunner
{
    private const int OutputLimit = 256 * 1024;

    public static async Task<ScriptExecutionResult> RunAsync(CustomActionDefinition action, string text, CancellationToken token)
    {
        if (CustomActionPolicy.Validate(action) != null || action.Kind == CustomActionKind.Url || text == null || text.Length > 32768 || text.Contains('\0'))
            return new("invalid-action");
        if (token.IsCancellationRequested) return new("cancelled");
        string? directory = null;
        string? script = null;
        string? bootstrapFile = null;
        string? gate = null;
        Process? process = null;
        SafeFileHandle? job = null;
        try
        {
            var interpreter = ResolveInterpreter(action);
            if (interpreter == null) return new("runtime-unavailable",
                ProductErrorCode: action.InterpreterPath != null ? "Runtime_PathInvalid"
                    : action.Kind == CustomActionKind.Python ? "Runtime_PythonMissing" : "Runtime_DefaultMissing",
                ProductErrorArgument: action.InterpreterPath ?? action.Kind.ToString());
            directory = Path.Combine(Path.GetTempPath(), "SnapBar.Actions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            script = Path.Combine(directory, action.Kind switch
            {
                CustomActionKind.PowerShell => "action.ps1", CustomActionKind.Python => "action.py", _ => "action.js"
            });
            await File.WriteAllTextAsync(script, action.Content, new UTF8Encoding(false), token).ConfigureAwait(false);
            var start = new ProcessStartInfo(interpreter)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.Environment["POPCLIP_TEXT"] = action.UseSelection ? text : "";
            start.Environment["SNAPBAR_SCRIPT_PATH"] = script;
            gate = Path.Combine(directory, "ready");
            start.Environment["SNAPBAR_READY_PATH"] = gate;
            if (action.Kind == CustomActionKind.PowerShell)
            {
                const string bootstrap = "while(![IO.File]::Exists($env:SNAPBAR_READY_PATH)){[Threading.Thread]::Sleep(10)};$ProgressPreference='SilentlyContinue';$u=New-Object System.Text.UTF8Encoding($false);[Console]::OutputEncoding=$u;$OutputEncoding=$u;& ([ScriptBlock]::Create([IO.File]::ReadAllText($env:SNAPBAR_SCRIPT_PATH,$u)))";
                foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-OutputFormat", "Text", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap)) })
                    start.ArgumentList.Add(argument);
            }
            else if (action.Kind == CustomActionKind.Python)
            {
                bootstrapFile = Path.Combine(directory, "bootstrap.py");
                const string bootstrap = "import os, sys, time, runpy\nif sys.version_info.major != 3:\n    sys.exit('SnapBar requires Python 3.')\nsys.stdout.reconfigure(newline='')\nsys.stderr.reconfigure(newline='')\nwhile not os.path.exists(os.environ['SNAPBAR_READY_PATH']):\n    time.sleep(0.01)\nrunpy.run_path(os.environ['SNAPBAR_SCRIPT_PATH'], run_name='__main__')\n";
                await File.WriteAllTextAsync(bootstrapFile, bootstrap, Encoding.ASCII, token).ConfigureAwait(false);
                start.Environment["PYTHONIOENCODING"] = "utf-8";
                foreach (var argument in new[] { "-X", "utf8", "-u", bootstrapFile })
                    start.ArgumentList.Add(argument);
            }
            else
            {
                bootstrapFile = Path.Combine(directory, "bootstrap.js");
                await File.WriteAllTextAsync(bootstrapFile, "const fs=require('fs');const wait=new Int32Array(new SharedArrayBuffer(4));while(!fs.existsSync(process.env.SNAPBAR_READY_PATH)){Atomics.wait(wait,0,0,10);}require(process.env.SNAPBAR_SCRIPT_PATH);", Encoding.ASCII, token).ConfigureAwait(false);
                start.ArgumentList.Add(bootstrapFile);
            }
            job = CreateRequestJob();
            token.ThrowIfCancellationRequested();
            process = new Process { StartInfo = start };
            if (!process.Start()) return new("runtime-unavailable");
            if (!AssignProcessToJobObject(job, process.Handle)) throw new Win32Exception();
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(gate, "ready", Encoding.ASCII, token).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(action.TimeoutSeconds));
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
            var overflow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var outputCapture = new BoundedCapture();
            var errorCapture = new BoundedCapture();
            var stdout = outputCapture.ReadAsync(process.StandardOutput, overflow);
            var stderr = errorCapture.ReadAsync(process.StandardError, overflow);
            var exited = process.WaitForExitAsync();
            var stopped = Task.Delay(Timeout.Infinite, stop.Token);
            var completed = Task.WhenAll(exited, stdout, stderr);
            await Task.WhenAny(exited, overflow.Task, stopped).ConfigureAwait(false);
            string? status = overflow.Task.IsCompleted ? "script-output-too-large" : token.IsCancellationRequested ? "cancelled" : timeout.IsCancellationRequested ? "script-timeout" : null;
            job.Dispose(); // Kill descendants even when the interpreter already exited.
            if (status != null) Kill(process);
            try { await completed.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { Kill(process); status ??= "script-timeout"; }
            if (overflow.Task.IsCompleted) status = "script-output-too-large";
            var output = outputCapture.Snapshot();
            var error = errorCapture.Snapshot();
            var incomplete = !output.Complete || !error.Complete;
            int? exitCode = process.HasExited ? process.ExitCode : null;
            return new(status ?? (exitCode == 0 && !incomplete ? "ok" : "script-failed"), output.Text, error.Text, exitCode,
                output.Truncated, error.Truncated, incomplete);
        }
        catch (OperationCanceledException) { return new("cancelled"); }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        { return new(process == null || !HasStarted(process) ? "runtime-unavailable" : "script-failed", Error: error.Message); }
        finally
        {
            job?.Dispose();
            if (process != null) { Kill(process); process.Dispose(); }
            try
            {
                if (script != null) File.Delete(script);
                if (bootstrapFile != null) File.Delete(bootstrapFile);
                if (gate != null) File.Delete(gate);
                if (directory != null) Directory.Delete(directory, false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static SafeFileHandle CreateRequestJob()
    {
        var handle = CreateJobObjectW(0, null);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(); }
        var limits = new JobExtendedLimits { Basic = new JobBasicLimits { LimitFlags = 0x2000 } };
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<JobExtendedLimits>()))
        { handle.Dispose(); throw new Win32Exception(); }
        return handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobIoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimits
    {
        public JobBasicLimits Basic;
        public JobIoCounters Io;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref JobExtendedLimits information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);

    private static bool HasStarted(Process process)
    { try { _ = process.Id; return true; } catch (InvalidOperationException) { return false; } }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException) { }
    }

    private sealed class BoundedCapture
    {
        private readonly StringBuilder text = new();
        private readonly object sync = new();
        private bool truncated, complete;

        public (string Text, bool Truncated, bool Complete) Snapshot()
        { lock (sync) return (text.ToString(), truncated, complete); }

        public async Task ReadAsync(StreamReader reader, TaskCompletionSource overflow)
        {
            var buffer = new char[4096];
            try
            {
                while (true)
                {
                    var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                    lock (sync)
                    {
                        if (count == 0) { complete = true; return; }
                        var remaining = OutputLimit - text.Length;
                        text.Append(buffer, 0, Math.Min(count, remaining));
                        if (count > remaining)
                        { truncated = true; complete = true; overflow.TrySetResult(); return; }
                    }
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            { /* Retain the bounded snapshot and mark capture incomplete. */ }
        }
    }

    private static string? ResolveInterpreter(CustomActionDefinition action)
    {
        if (action.InterpreterPath != null)
            return Path.IsPathFullyQualified(action.InterpreterPath) && string.Equals(Path.GetExtension(action.InterpreterPath), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(action.InterpreterPath) ? action.InterpreterPath : null;
        if (action.Kind == CustomActionKind.PowerShell)
        {
            var path = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(path) ? path : null;
        }
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var candidate = Path.Combine(entry.Trim().Trim('"'), action.Kind == CustomActionKind.Python ? "python.exe" : "node.exe");
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
