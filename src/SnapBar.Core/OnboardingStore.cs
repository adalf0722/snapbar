using System.Text;
using System.Text.Json;

namespace SnapBar.Core;

public sealed class OnboardingStore(string path)
{
    public string FilePath { get; } = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public OnboardingLoadResult Load()
    {
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 4096) return new(OnboardingLoadKind.Invalid);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
            var state = JsonSerializer.Deserialize<OnboardingState>(reader.ReadToEnd(), Options);
            if (state == null || !Enum.IsDefined(state.Status)) return new(OnboardingLoadKind.Invalid);
            return state.Version == 1 ? new(OnboardingLoadKind.Valid, state) : new(OnboardingLoadKind.Unsupported);
        }
        catch (FileNotFoundException) { return new(OnboardingLoadKind.Missing); }
        catch (DirectoryNotFoundException) { return new(OnboardingLoadKind.Missing); }
        catch (Exception e) when (e is JsonException or DecoderFallbackException)
        { return new(OnboardingLoadKind.Invalid); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return new(OnboardingLoadKind.Unavailable); }
    }

    public void Save(OnboardingState state)
    {
        if (state.Version != 1 || !Enum.IsDefined(state.Status)) throw new ArgumentException("Invalid onboarding state");
        var previous = Load();
        if (previous.Kind is not (OnboardingLoadKind.Missing or OnboardingLoadKind.Valid))
            throw new InvalidOperationException("Existing onboarding state must be preserved");
        if (previous.State?.Status == OnboardingStatus.Completed) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = Path.Combine(Path.GetDirectoryName(FilePath)!, ".onboarding-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(state, Options));
                stream.Flush(true);
            }
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
