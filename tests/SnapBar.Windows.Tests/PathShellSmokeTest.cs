using System.IO;
using System.Runtime.InteropServices;
using SnapBar.Windows;

internal static class PathShellSmokeTest
{
    // Opt-in: opens Explorer windows under a new ignored disposable directory.
    public static int Run()
    {
        int passed = 0;
        var root = Path.GetFullPath(Path.Combine("artifacts", "path-shell-smoke-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            var file = Path.Combine(root, "中文 空白, & 😀.txt");
            File.WriteAllText(file, "Disposable SnapBar Shell selection fixture.");
            var folder = Path.Combine(root, "資料夾 space, &"); Directory.CreateDirectory(folder);
            var sentinel = Path.Combine(root, "script-was-executed.txt");
            var script = Path.Combine(root, "請勿執行, fixture.cmd");
            File.WriteAllText(script, "@echo off\r\necho executed>\"" + sentinel + "\"\r\n");
            foreach (var path in new[] { file, folder, script })
            {
                var target = Native.GetIdentity(Native.GetForegroundWindow()) ?? throw new Exception("No foreground window");
                using (var session = new PathRevealSession(TestPaths.ProbeExecutable))
                {
                    var prepared = session.PrepareAsync(path, default).GetAwaiter().GetResult();
                    if (prepared != "ok") throw new Exception("Native preparation failed: " + prepared);
                    var result = session.RevealAsync(target, default).GetAwaiter().GetResult();
                    if (result != "ok") throw new Exception("Native reveal failed: " + result);
                }
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (!IsSelected(shell, path == folder ? folder : root, path == folder ? null : path))
                { if (DateTime.UtcNow > deadline) throw new Exception("Explorer did not open the exact folder or select the exact file"); Thread.Sleep(100); }
                Console.WriteLine("PASS actual Explorer " + (path == folder ? "opens the selected directory itself"
                    : path == file ? "selects disposable Unicode file" : "selects script as an item")); passed++;
            }
            Thread.Sleep(250);
            if (File.Exists(sentinel)) throw new Exception("Selection unexpectedly executed the script");
            Console.WriteLine("PASS selected script is not executed"); passed++;
            foreach (var folderPath in new[] { Path.GetPathRoot(root)!, Path.GetDirectoryName(Environment.CurrentDirectory)! })
            {
                var target = Native.GetIdentity(Native.GetForegroundWindow()) ?? throw new Exception("No foreground window");
                using var session = new PathRevealSession(TestPaths.ProbeExecutable);
                if (session.PrepareAsync(folderPath, default).GetAwaiter().GetResult() != "ok"
                    || session.RevealAsync(target, default).GetAwaiter().GetResult() != "ok")
                    throw new Exception("Folder reveal failed");
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (!IsSelected(shell, folderPath, null))
                { if (DateTime.UtcNow > deadline) throw new Exception("Explorer did not open root or repository parent"); Thread.Sleep(100); }
                Console.WriteLine("PASS actual Explorer opens " + (folderPath == Path.GetPathRoot(root) ? "drive root" : "repository parent folder") + " directly"); passed++;
            }
            Console.WriteLine($"{passed} passed, 0 failed; disposable Explorer fixture retained under artifacts"); return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL native Shell smoke: " + e.GetType().Name + " " + e.Message); return 1; }
        finally { if (shell != null) Marshal.FinalReleaseComObject(shell); }
    }

    internal static bool IsSelected(dynamic shell, string root, string? path)
    {
        dynamic? windows = null;
        try
        {
            windows = shell.Windows();
            for (int index = 0; index < windows.Count; index++)
            {
                dynamic? window = null, document = null, folder = null, self = null, selected = null, item = null;
                try
                {
                    window = windows.Item(index);
                    document = window.Document; folder = document.Folder; self = folder.Self;
                    if (!string.Equals((string)self.Path, root, StringComparison.OrdinalIgnoreCase)) continue;
                    if (path == null) return true;
                    selected = document.SelectedItems();
                    if (selected.Count != 1) continue;
                    item = selected.Item(0);
                    if (string.Equals((string)item.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (COMException) { }
                finally
                {
                    foreach (object? value in new object?[] { item, selected, self, folder, document, window })
                        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
                }
            }
            return false;
        }
        finally { if (windows != null) Marshal.FinalReleaseComObject(windows); }
    }
}
