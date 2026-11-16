using System.IO;
using System.Text;
using System.Text.Json;
using SnapBar.Core;

namespace SnapBar.App;

// Explicit local troubleshooting mode. Deliberately never serializes ProbeResponse or Selection.
internal sealed class SelectionDiagnostics
{
    internal bool Enabled { get; } = Environment.GetEnvironmentVariable("SNAPBAR_SELECTION_DIAGNOSTICS") == "1";
    private string? path;
    private bool failed;

    internal void Record(long capture, string stage, ProbeResponse response)
    {
        if (!Enabled || failed) return;
        try
        {
            if (path == null)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapBar", "diagnostics");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, $"selection-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Environment.ProcessId}.jsonl");
            }
            var line = JsonSerializer.Serialize(new
            {
                time = DateTime.UtcNow, capture, stage, status = response.Status,
                elapsedMs = response.ElapsedMs, probe = response.Diagnostics
            }) + Environment.NewLine;
            var encoding = new UTF8Encoding(false);
            if ((File.Exists(path) ? new FileInfo(path).Length : 0) + encoding.GetByteCount(line) > 1048576)
            { failed = true; return; }
            File.AppendAllText(path, line, encoding);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { failed = true; }
    }
}
