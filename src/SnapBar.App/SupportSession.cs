using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace SnapBar.App;

internal sealed class SupportSession(string directory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static readonly SupportSession Local = new(Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "SnapBar", "diagnostics"));
    private readonly Session current = new(Guid.NewGuid(), BuildVersion, DateTimeOffset.UtcNow, false);
    private Session? previous;
    private Crash? previousCrash;
    private int crashRecorded;

    internal static string BuildVersion => typeof(SupportSession).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    internal bool PreviousInterrupted => previous is { Completed: false };

    internal void Start()
    {
        previous = Read<Session>("session.json");
        var crash = Read<Crash>("crash.json");
        if (PreviousInterrupted && crash?.SessionId == previous?.Id) previousCrash = crash;
        Write("session.json", current);
    }

    internal void Complete()
    {
        if (Volatile.Read(ref crashRecorded) == 0) Write("session.json", current with { Completed = true });
    }

    internal void RecordCrash(Exception error, SelectionDiagnostics diagnostics)
    {
        if (Interlocked.Exchange(ref crashRecorded, 1) != 0) return;
        var frames = new StackTrace(error, false).GetFrames().Take(32).Select(frame =>
        {
            var method = frame.GetMethod();
            if (method == null || method.Module.Assembly.IsDynamic || method is System.Reflection.Emit.DynamicMethod)
                return new CrashFrame(null, null, frame.GetILOffset());
            return new CrashFrame(method.Module.ModuleVersionId, method.MetadataToken, frame.GetILOffset());
        }).ToArray();
        Write("crash.json", new Crash(current.Id, DateTimeOffset.UtcNow,
            error.GetType().FullName ?? "Exception", error.HResult, frames, diagnostics.TrySnapshot()));
    }

    internal string CreateReport(SelectionDiagnostics diagnostics) => JsonSerializer.Serialize(new
    {
        schema = 1,
        build = current.Build,
        windows = Environment.OSVersion.Version.ToString(),
        timestampFrequency = Stopwatch.Frequency,
        previousSession = PreviousInterrupted ? previous : null,
        previousCrash,
        reproduction = Read<Reproduction>("reproduction.json"),
        events = diagnostics.Snapshot()
    }, Json);

    internal void SaveReproduction(SelectionDiagnosticEvent[] events) =>
        Write("reproduction.json", new Reproduction(current.Id, current.Build, DateTimeOffset.UtcNow, events));

    private T? Read<T>(string name) where T : class
    {
        try
        {
            using var file = File.OpenRead(Path.Combine(directory, name));
            return file.Length <= 512 * 1024 ? JsonSerializer.Deserialize<T>(file, Json) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    private void Write<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { }
    }

    private sealed record Session(Guid Id, string Build, DateTimeOffset Started, bool Completed);
    private sealed record Crash(Guid SessionId, DateTimeOffset Time, string Type, int HResult,
        CrashFrame[] Frames, SelectionDiagnosticEvent[] Events);
    private sealed record CrashFrame(Guid? Module, int? MethodToken, int IlOffset);
    private sealed record Reproduction(Guid SessionId, string Build, DateTimeOffset Time, SelectionDiagnosticEvent[] Events);
}
