using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SnapBar.Core;

namespace SnapBar.Probe;

internal static class Program
{
    [MTAThread]
    private static void Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        // Parent PID is an inherited local process relationship, not a remote target.
        if (args.Length >= 1 && int.TryParse(args[0], out var parentId))
        {
            _ = Task.Run(() =>
            {
                try { using var parent = Process.GetProcessById(parentId); parent.WaitForExit(); }
                catch (ArgumentException) { }
                Environment.Exit(0);
            });
        }
        var reader = new SelectionReader();
        using var pathWorker = new PathWorker();
        bool pathMode = args.Contains("--path-worker");
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            var watch = Stopwatch.StartNew();
            ProbeResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<ProbeRequest>(line.TrimStart('\uFEFF'));
                response = pathMode && request != null ? pathWorker.Handle(request) : request?.Operation switch
                {
                    "capture" => reader.Capture(request),
                    "validate" => reader.Validate(request.SessionId),
                    "clipboard-validate" => reader.ValidateClipboard(request.SessionId),
                    "clipboard-extend" => reader.ExtendClipboardLease(request.SessionId, request.LeaseSeconds),
                    "clipboard-release" => reader.ReleaseClipboardLease(request.SessionId),
                    "ping" => new("ok"),
                    _ => new("invalid-request")
                };
            }
            catch (JsonException) { response = new("invalid-request"); }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Provider exceptions often contain user text. Never serialize their messages.
                response = new("provider-error");
            }
            Console.WriteLine(JsonSerializer.Serialize(response with { ElapsedMs = watch.Elapsed.TotalMilliseconds }));
        }
    }
}
