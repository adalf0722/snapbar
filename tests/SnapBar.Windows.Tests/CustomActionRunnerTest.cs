using SnapBar.Core;
using System.IO;
using System.Diagnostics;
using SnapBar.Windows;

internal static class CustomActionRunnerTest
{
    public static async Task<int> RunAsync()
    {
        int passed = 0, failed = 0;
        async Task Check(string name, Func<Task> test)
        {
            try { await test(); Console.WriteLine("PASS " + name); passed++; }
            catch (Exception error) { Console.WriteLine("FAIL " + name + ": " + error.Message); failed++; }
        }
        static CustomActionDefinition Action(string script, int timeout = 10) => new("custom-" + Guid.NewGuid().ToString("N"), "Test", CustomActionKind.PowerShell, "terminal", script, TimeoutSeconds: timeout);
        static void Require(bool condition) { if (!condition) throw new Exception("Unexpected execution result"); }
        var root = Path.Combine(Path.GetTempPath(), "SnapBar.Actions");
        var before = Directory.Exists(root) ? Directory.GetDirectories(root).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        await Check("PowerShell Unicode and malicious selection remain data", async () =>
        {
            const string text = "中文 😀 '; $(throw 'injected'); `\" & | \r\n";
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Write($env:POPCLIP_TEXT)"), text, default);
            Require(result.Success && result.Output == text);
        });
        await Check("explicit no-selection script receives an empty environment value", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Write('[' + $env:POPCLIP_TEXT + ']')") with { UseSelection = false }, "must not reach script 中文", default);
            Require(result.Success && result.Output == "[]");
        });
        await Check("nonzero exit preserves UTF8 stderr", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Error.Write('錯誤 😀'); exit 7"), "", default);
            Require(result.Status == "script-failed" && result.ExitCode == 7 && result.Error == "錯誤 😀");
        });
        await Check("failure retains both output streams", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Write('before'); [Console]::Error.Write('failure'); exit 7"), "", default);
            Require(result.Status == "script-failed" && result.ExitCode == 7 && result.Output == "before" && result.Error == "failure");
        });
        await Check("noninteractive input retains original PowerShell error", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("Read-Host 'input'"), "", default);
            Require(result.Error.Contains("NonInteractive", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("非互動"));
        });
        await Check("timeout retains flushed output and diagnostics", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Write('before timeout'); [Console]::Out.Flush(); [Console]::Error.Write('diagnostic'); [Console]::Error.Flush(); Start-Sleep 30", 2), "", default);
            Require(result.Status == "script-timeout" && result.Output == "before timeout" && result.Error == "diagnostic");
        });
        await Check("timeout terminates script", async () => Require((await CustomActionRunner.RunAsync(Action("Start-Sleep 30", 1), "", default)).Status == "script-timeout"));
        await Check("pre-cancellation", async () => Require((await CustomActionRunner.RunAsync(Action("throw 'must not run'"), "", new CancellationToken(true))).Status == "cancelled"));
        await Check("mid-execution cancellation", async () =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
            Require((await CustomActionRunner.RunAsync(Action("Start-Sleep 30"), "", cancellation.Token)).Status == "cancelled");
        });
        await Check("stdout overflow bounded", async () => Require((await CustomActionRunner.RunAsync(Action("[Console]::Write(('x' * 300000)); Start-Sleep 30"), "", default)).Status == "script-output-too-large"));
        await Check("stderr overflow bounded", async () => Require((await CustomActionRunner.RunAsync(Action("[Console]::Error.Write(('x' * 300000)); Start-Sleep 30"), "", default)).Status == "script-output-too-large"));
        foreach (var errorStream in new[] { false, true })
        await Check((errorStream ? "stderr" : "stdout") + " overflow retains bounded original text", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action($"[Console]::{(errorStream ? "Error" : "Out")}.Write(('x' * 300000)); Start-Sleep 30"), "", default);
            Require(result.Status == "script-output-too-large");
            Require((errorStream ? result.Error : result.Output) == new string('x', 256 * 1024));
            Require(errorStream ? result.ErrorTruncated && !result.OutputTruncated : result.OutputTruncated && !result.ErrorTruncated);
        });
        await Check("simultaneous environments are isolated", async () =>
        {
            var action = Action("Start-Sleep -Milliseconds 100; [Console]::Write($env:POPCLIP_TEXT)");
            var results = await Task.WhenAll(CustomActionRunner.RunAsync(action, "one", default), CustomActionRunner.RunAsync(action, "two", default));
            Require(results[0].Success && results[0].Output == "one" && results[1].Success && results[1].Output == "two");
        });
        foreach (var mode in new[] { "exit", "timeout", "cancel" })
        await Check("job terminates descendant on " + mode, async () =>
        {
            var marker = Path.Combine(Path.GetTempPath(), "SnapBar.Child-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Unredirected child inherits the interpreter's output handles. Its PID is
                // recorded independently so a stranded pipe cannot hide a surviving child.
                var script = "$s=New-Object Diagnostics.ProcessStartInfo;$s.FileName=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe';$s.UseShellExecute=$false;$s.CreateNoWindow=$true;$s.Arguments='-NoProfile -NonInteractive -Command Start-Sleep 60';$p=[Diagnostics.Process]::Start($s);[IO.File]::WriteAllText($env:POPCLIP_TEXT,[string]$p.Id);" + (mode == "exit" ? "exit 0" : "Start-Sleep 60");
                using var cancellation = new CancellationTokenSource();
                var running = CustomActionRunner.RunAsync(Action(script, mode == "timeout" ? 2 : 10), marker, cancellation.Token);
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (!File.Exists(marker) && !running.IsCompleted && DateTime.UtcNow < deadline) await Task.Delay(20);
                Require(File.Exists(marker));
                var pid = int.Parse(await File.ReadAllTextAsync(marker));
                if (mode == "cancel") cancellation.Cancel();
                var result = await running;
                Require(result.Status == (mode == "exit" ? "ok" : mode == "timeout" ? "script-timeout" : "cancelled"));
                try { using var child = Process.GetProcessById(pid); Require(child.WaitForExit(3000)); }
                catch (ArgumentException) { /* Already reaped. */ }
            }
            finally { File.Delete(marker); }
        });
        await Check("invalid actions and interpreter paths rejected", async () =>
        {
            Require((await CustomActionRunner.RunAsync(Action("1") with { TimeoutSeconds = 0 }, "", default)).Status == "invalid-action");
            Require((await CustomActionRunner.RunAsync(Action("1") with { InterpreterPath = "powershell.exe -NoProfile" }, "", default)).Status == "runtime-unavailable");
            Require((await CustomActionRunner.RunAsync(Action("1") with { InterpreterPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe") }, "", default)).Status == "runtime-unavailable");
            Require((await CustomActionRunner.RunAsync(Action("1"), new string('x', 32769), default)).Status == "invalid-action");
        });
        var node = await CustomActionRunner.RunAsync(Action("process.stdout.write(process.env.POPCLIP_TEXT)") with { Kind = CustomActionKind.JavaScript }, "中文 😀", default);
        if (node.Status == "runtime-unavailable") Console.WriteLine("SKIP Node Unicode roundtrip: node.exe not found on PATH");
        else await Check("Node Unicode roundtrip", () => { Require(node.Success && node.Output == "中文 😀"); return Task.CompletedTask; });
        await Check("request temporary directories cleaned", () =>
        {
            Require(!Directory.Exists(root) || Directory.GetDirectories(root).All(before.Contains));
            return Task.CompletedTask;
        });
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
