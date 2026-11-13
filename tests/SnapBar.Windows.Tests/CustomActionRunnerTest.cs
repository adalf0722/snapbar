using SnapBar.Core;
using System.IO;
using System.Diagnostics;
using SnapBar.Windows;

internal static class CustomActionRunnerTest
{
    public static async Task<int> RunAsync(bool requireRuntimes = false)
    {
        int passed = 0, failed = 0;
        async Task Check(string name, Func<Task> test)
        {
            try { await test(); Console.WriteLine("PASS " + name); passed++; }
            catch (Exception error) { Console.WriteLine("FAIL " + name + ": " + error.Message); failed++; }
        }
        static CustomActionDefinition Action(string script, int timeout = 10) => new("custom-" + Guid.NewGuid().ToString("N"), "Test", CustomActionKind.PowerShell, "terminal", script, TimeoutSeconds: timeout);
        static void Require(bool condition, ScriptExecutionResult? result = null)
        { if (!condition) throw new Exception(result == null ? "Unexpected execution result" : ScriptExecutionAssert.Describe(result)); }
        await Check("execution assertion reports status exit and stream lengths", () =>
        {
            try { ScriptExecutionAssert.Result(new("script-failed", "fixture", "fixture error", 7)); }
            catch (InvalidOperationException error)
            {
                Require(error.Message.Contains("status=script-failed, exit=7, stdout length=7, stderr length=13"));
                return Task.CompletedTask;
            }
            throw new Exception("Expected assertion failure");
        });
        await Check("text assertion identifies CRLF expansion and EOF without dumping streams", () =>
        {
            foreach (var (expected, actual, expectedCharacter, actualCharacter) in new[]
            {
                ("a\r\nb", "a\r\r\nb", "<LF>", "<CR>"), ("a\t", "a", "<TAB>", "<EOF>")
            })
            {
                try { ScriptExecutionAssert.Result(new("ok", actual, ExitCode: 0), output: expected); }
                catch (InvalidOperationException error)
                {
                    Require(error.Message.Contains("first difference at UTF-16 index=")
                        && error.Message.Contains("expected " + expectedCharacter + ", actual " + actualCharacter)
                        && error.Message.Contains("expected length="));
                    continue;
                }
                throw new Exception("Expected text assertion failure");
            }
            return Task.CompletedTask;
        });
        var root = Path.Combine(Path.GetTempPath(), "SnapBar.Actions");
        var before = Directory.Exists(root) ? Directory.GetDirectories(root).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        await Check("PowerShell Unicode and malicious selection remain data", async () =>
        {
            const string text = "中文 😀 '; $(throw 'injected'); `\" & | \r\n";
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Write($env:POPCLIP_TEXT)"), text, default);
            ScriptExecutionAssert.Result(result, output: text);
        });
        await Check("explicit no-selection script receives an empty environment value", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Write('[' + $env:POPCLIP_TEXT + ']')") with { UseSelection = false }, "must not reach script 中文", default);
            ScriptExecutionAssert.Result(result, output: "[]");
        });
        await Check("nonzero exit preserves UTF8 stderr", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Error.Write('錯誤 😀'); exit 7"), "", default);
            ScriptExecutionAssert.Result(result, "script-failed", 7, error: "錯誤 😀");
        });
        await Check("failure retains both output streams", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Write('before'); [Console]::Error.Write('failure'); exit 7"), "", default);
            ScriptExecutionAssert.Result(result, "script-failed", 7, "before", "failure");
        });
        await Check("noninteractive input retains original PowerShell error", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("Read-Host 'input'"), "", default);
            Require(result.Error.Contains("NonInteractive", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("非互動"), result);
        });
        await Check("timeout retains flushed output and diagnostics", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action("[Console]::Write('before timeout'); [Console]::Out.Flush(); [Console]::Error.Write('diagnostic'); [Console]::Error.Flush(); Start-Sleep 30", 2), "", default);
            ScriptExecutionAssert.Result(result, "script-timeout", null, "before timeout", "diagnostic");
        });
        await Check("timeout terminates script", async () => ScriptExecutionAssert.Result(await CustomActionRunner.RunAsync(Action("Start-Sleep 30", 1), "", default), "script-timeout", null));
        await Check("pre-cancellation", async () => ScriptExecutionAssert.Result(await CustomActionRunner.RunAsync(Action("throw 'must not run'"), "", new CancellationToken(true)), "cancelled", null));
        await Check("mid-execution cancellation", async () =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
            ScriptExecutionAssert.Result(await CustomActionRunner.RunAsync(Action("Start-Sleep 30"), "", cancellation.Token), "cancelled", null);
        });
        await Check("stdout overflow bounded", async () => ScriptExecutionAssert.Result(await CustomActionRunner.RunAsync(Action("[Console]::Write(('x' * 300000)); Start-Sleep 30"), "", default), "script-output-too-large", null));
        await Check("stderr overflow bounded", async () => ScriptExecutionAssert.Result(await CustomActionRunner.RunAsync(Action("[Console]::Error.Write(('x' * 300000)); Start-Sleep 30"), "", default), "script-output-too-large", null));
        foreach (var errorStream in new[] { false, true })
        await Check((errorStream ? "stderr" : "stdout") + " overflow retains bounded original text", async () =>
        {
            var result = await CustomActionRunner.RunAsync(Action($"[Console]::{(errorStream ? "Error" : "Out")}.Write(('x' * 300000)); Start-Sleep 30"), "", default);
            ScriptExecutionAssert.Result(result, "script-output-too-large", null,
                output: errorStream ? null : new string('x', 256 * 1024), error: errorStream ? new string('x', 256 * 1024) : null);
            Require(errorStream ? result.ErrorTruncated && !result.OutputTruncated : result.OutputTruncated && !result.ErrorTruncated, result);
        });
        await Check("simultaneous environments are isolated", async () =>
        {
            var action = Action("Start-Sleep -Milliseconds 100; [Console]::Write($env:POPCLIP_TEXT)");
            var results = await Task.WhenAll(CustomActionRunner.RunAsync(action, "one", default), CustomActionRunner.RunAsync(action, "two", default));
            ScriptExecutionAssert.Result(results[0], output: "one");
            ScriptExecutionAssert.Result(results[1], output: "two");
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
                ScriptExecutionAssert.Result(result, mode == "exit" ? "ok" : mode == "timeout" ? "script-timeout" : "cancelled", null);
                try { using var child = Process.GetProcessById(pid); Require(child.WaitForExit(3000)); }
                catch (ArgumentException) { /* Already reaped. */ }
            }
            finally { File.Delete(marker); }
        });
        await Check("invalid actions and interpreter paths rejected", async () =>
        {
            ScriptExecutionAssert.Result(await CustomActionRunner.RunAsync(Action("1") with { TimeoutSeconds = 0 }, "", default), "invalid-action", null);
            ScriptExecutionAssert.Result(await CustomActionRunner.RunAsync(Action("1") with { InterpreterPath = "powershell.exe -NoProfile" }, "", default), "runtime-unavailable", null);
            ScriptExecutionAssert.Result(await CustomActionRunner.RunAsync(Action("1") with { InterpreterPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe") }, "", default), "runtime-unavailable", null);
            ScriptExecutionAssert.Result(await CustomActionRunner.RunAsync(Action("1"), new string('x', 32769), default), "invalid-action", null);
        });
        var node = await CustomActionRunner.RunAsync(Action("process.stdout.write(process.env.POPCLIP_TEXT)") with { Kind = CustomActionKind.JavaScript }, "中文 😀", default);
        if (node.Status == "runtime-unavailable" && !requireRuntimes) Console.WriteLine("SKIP Node Unicode roundtrip: node.exe not found on PATH");
        else await Check("Node Unicode roundtrip (required in strict mode)", () => { ScriptExecutionAssert.Result(node, output: "中文 😀"); return Task.CompletedTask; });
        var pythonAction = Action("import os, sys; sys.stdout.write(os.environ['POPCLIP_TEXT'])") with { Kind = CustomActionKind.Python };
        var pythonResult = await CustomActionRunner.RunAsync(pythonAction, "中文 😀 '; $(throw 'injected'); `\" & | \r\n", default);
        if (pythonResult.Status == "runtime-unavailable" && !requireRuntimes) Console.WriteLine("SKIP Python runtime checks: python.exe not found on PATH");
        else if (pythonResult.Status == "runtime-unavailable")
            await Check("Python runtime required in strict mode", () => { ScriptExecutionAssert.Result(pythonResult); return Task.CompletedTask; });
        else
        {
            await Check("Python Unicode selection remains data", () => { ScriptExecutionAssert.Result(pythonResult, output: "中文 😀 '; $(throw 'injected'); `\" & | \r\n"); return Task.CompletedTask; });
            await Check("Python no-selection input and __main__ execution", async () =>
            {
                var result = await CustomActionRunner.RunAsync(pythonAction with { Content = "import os; print(__name__ + '[' + os.environ.get('POPCLIP_TEXT', '') + ']', end='')", UseSelection = false }, "private fixture", default);
                ScriptExecutionAssert.Result(result, output: "__main__[]");
            });
            await Check("Python nonzero exit retains Unicode streams", async () =>
            {
                var result = await CustomActionRunner.RunAsync(pythonAction with { Content = "import sys; print('結果 😀', end=''); print('錯誤 中文', end='', file=sys.stderr); sys.exit(7)" }, "", default);
                ScriptExecutionAssert.Result(result, "script-failed", 7, "結果 😀", "錯誤 中文");
            });
            await Check("Python missing module retains traceback", async () =>
            {
                var result = await CustomActionRunner.RunAsync(pythonAction with { Content = "import snapbar_missing_fixture_module" }, "", default);
                ScriptExecutionAssert.Result(result, "script-failed", 1);
                Require(result.Error.Contains("ModuleNotFoundError") && result.Error.Contains("snapbar_missing_fixture_module"), result);
            });
            await Check("Python timeout retains unbuffered output", async () =>
            {
                var result = await CustomActionRunner.RunAsync(pythonAction with { Content = "import time; print('before timeout', end=''); time.sleep(30)", TimeoutSeconds = 1 }, "", default);
                ScriptExecutionAssert.Result(result, "script-timeout", null, "before timeout");
            });
            await Check("Python cancellation", async () =>
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
                var result = await CustomActionRunner.RunAsync(pythonAction with { Content = "import time; time.sleep(30)" }, "", cancellation.Token);
                ScriptExecutionAssert.Result(result, "cancelled", null);
            });
            foreach (var stream in new[] { "stdout", "stderr" })
            await Check("Python " + stream + " overflow bounded", async () =>
            {
                var result = await CustomActionRunner.RunAsync(pythonAction with { Content = $"import sys, time; sys.{stream}.write('x' * 300000); time.sleep(30)" }, "", default);
                ScriptExecutionAssert.Result(result, "script-output-too-large", null,
                    output: stream == "stdout" ? new string('x', 256 * 1024) : null, error: stream == "stderr" ? new string('x', 256 * 1024) : null);
                Require(stream == "stdout" ? result.OutputTruncated : result.ErrorTruncated, result);
            });
            await Check("Python explicit virtual environment path and site packages", async () =>
            {
                var venv = Path.Combine(Path.GetTempPath(), "SnapBar.Python-" + Guid.NewGuid().ToString("N"), "環境 with spaces");
                try
                {
                    var create = await CustomActionRunner.RunAsync(pythonAction with { Content = "import os, venv; venv.create(os.environ['POPCLIP_TEXT'], with_pip=False)" }, venv, default);
                    ScriptExecutionAssert.Result(create);
                    var package = Path.Combine(venv, "Lib", "site-packages", "snapbar_fixture.py");
                    await File.WriteAllTextAsync(package, "value = '中文 😀'", new System.Text.UTF8Encoding(false));
                    var result = await CustomActionRunner.RunAsync(pythonAction with { InterpreterPath = Path.Combine(venv, "Scripts", "python.exe"), Content = "import sys, snapbar_fixture; print(snapbar_fixture.value, end=''); assert sys.prefix != sys.base_prefix" }, "", default);
                    ScriptExecutionAssert.Result(result, output: "中文 😀");
                }
                finally { if (Directory.Exists(Path.GetDirectoryName(venv))) Directory.Delete(Path.GetDirectoryName(venv)!, true); }
            });
            foreach (var mode in new[] { "exit", "timeout", "cancel" })
            await Check("Python job terminates descendant on " + mode, async () =>
            {
                var marker = Path.Combine(Path.GetTempPath(), "SnapBar.PythonChild-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using var cancellation = new CancellationTokenSource();
                    var content = "import os, subprocess, sys, time\np = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])\nwith open(os.environ['POPCLIP_TEXT'], 'w') as f:\n    f.write(str(p.pid))\n" + (mode == "exit" ? "sys.exit(0)" : "time.sleep(60)");
                    var running = CustomActionRunner.RunAsync(pythonAction with { Content = content, TimeoutSeconds = mode == "timeout" ? 2 : 10 }, marker, cancellation.Token);
                    var deadline = DateTime.UtcNow.AddSeconds(8);
                    while (!File.Exists(marker) && !running.IsCompleted && DateTime.UtcNow < deadline) await Task.Delay(20);
                    Require(File.Exists(marker));
                    var pid = int.Parse(await File.ReadAllTextAsync(marker));
                    if (mode == "cancel") cancellation.Cancel();
                    var result = await running;
                    ScriptExecutionAssert.Result(result, mode == "exit" ? "ok" : mode == "timeout" ? "script-timeout" : "cancelled", null);
                    try { using var child = Process.GetProcessById(pid); Require(child.WaitForExit(3000)); }
                    catch (ArgumentException) { }
                }
                finally { File.Delete(marker); }
            });
        }
        await Check("Python missing runtime and invalid path explain setup", async () =>
        {
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", "");
                var result = await CustomActionRunner.RunAsync(pythonAction, "", default);
                ScriptExecutionAssert.Result(result, "runtime-unavailable", null);
                Require(result.ProductErrorCode == "Runtime_PythonMissing", result);
                result = await CustomActionRunner.RunAsync(pythonAction with { InterpreterPath = "python.exe" }, "", default);
                ScriptExecutionAssert.Result(result, "runtime-unavailable", null);
                Require(result.ProductErrorCode == "Runtime_PathInvalid", result);
            }
            finally { Environment.SetEnvironmentVariable("PATH", previousPath); }
        });
        await Check("request temporary directories cleaned", () =>
        {
            Require(!Directory.Exists(root) || Directory.GetDirectories(root).All(before.Contains));
            return Task.CompletedTask;
        });
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
