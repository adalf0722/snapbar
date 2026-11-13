using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapBar.App;

internal sealed record OnboardingMessage(int Version, string Kind, long Window = 0,
    string? Language = null, string? Shortcut = null, long RequestId = 0,
    bool? Automatic = null, bool? HotkeyRegistered = null, string? Error = null);

internal static class OnboardingProtocol
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static OnboardingMessage Parse(ReadOnlySpan<byte> bytes, bool fromChild)
    {
        if (bytes.Length > 4096) throw new InvalidDataException("Onboarding message too large");
        // Reject duplicate fields rather than allowing last-value-wins commands.
        using var document = JsonDocument.Parse(bytes.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.EnumerateObject().Select(p => p.Name).Distinct().Count()
                != document.RootElement.EnumerateObject().Count()) throw new InvalidDataException("Invalid message");
        var value = JsonSerializer.Deserialize<OnboardingMessage>(bytes, Options)
            ?? throw new InvalidDataException("Missing message");
        Validate(value, fromChild);
        return value;
    }

    internal static void Validate(OnboardingMessage value, bool fromChild)
    {
        bool language = value.Language is "system" or "en" or "zh-Hant";
        var empty = value with { Kind = "", Window = 0, Language = null, Shortcut = null,
            RequestId = 0, Automatic = null, HotkeyRegistered = null, Error = null };
        OnboardingMessage allowed = value.Kind switch
        {
            "ready" when fromChild && value.Window > 0 => empty with { Window = value.Window },
            "skip" or "finish" or "open-settings" or "change-shortcut" when fromChild => empty,
            "set-language" when fromChild && value.RequestId > 0 && language =>
                empty with { Language = value.Language, RequestId = value.RequestId },
            "set-automatic" when fromChild && value.RequestId > 0 && value.Automatic != null =>
                empty with { Automatic = value.Automatic, RequestId = value.RequestId },
            "configure" when !fromChild && language && value.Shortcut is { Length: > 0 and < 100 }
                && value.Automatic != null && value.HotkeyRegistered != null =>
                empty with { Language = value.Language, Shortcut = value.Shortcut,
                    Automatic = value.Automatic, HotkeyRegistered = value.HotkeyRegistered, Error = value.Error },
            "preference-result" when !fromChild && value.RequestId > 0 && language && value.Automatic != null =>
                empty with { Language = value.Language, Automatic = value.Automatic,
                    RequestId = value.RequestId, Error = value.Error },
            "search-launched" or "close" when !fromChild => empty,
            "search-failed" when !fromChild => empty with { Error = value.Error },
            _ => throw new InvalidDataException("Unknown onboarding command")
        };
        if (value.Version != 1 || value.Error is not (null or "save-failed" or "browser-unavailable" or "progress-unavailable")
            || value != (allowed with { Kind = value.Kind })) throw new InvalidDataException("Invalid onboarding fields");
    }

    internal static byte[] Serialize(OnboardingMessage message, bool fromChild)
    {
        Validate(message, fromChild);
        return JsonSerializer.SerializeToUtf8Bytes(message, Options);
    }
}

internal sealed class OnboardingChannel(Stream stream, bool fromChild) : IDisposable
{
    private readonly SemaphoreSlim writes = new(1);

    internal async Task<OnboardingMessage?> ReadAsync(CancellationToken token)
    {
        var buffer = new byte[4096]; var single = new byte[1]; int count = 0;
        while (true)
        {
            if (await stream.ReadAsync(single, token) == 0)
            {
                if (count != 0) throw new InvalidDataException("Truncated message");
                return null;
            }
            if (single[0] == (byte)'\n') return OnboardingProtocol.Parse(buffer.AsSpan(0, count), fromChild);
            if (count == buffer.Length) throw new InvalidDataException("Onboarding message too large");
            buffer[count++] = single[0];
        }
    }

    internal async Task SendAsync(OnboardingMessage message, CancellationToken token)
    {
        var bytes = OnboardingProtocol.Serialize(message, !fromChild);
        await writes.WaitAsync(token);
        try
        {
            await stream.WriteAsync(bytes, token);
            await stream.WriteAsync(new byte[] { (byte)'\n' }, token);
            await stream.FlushAsync(token);
        }
        finally { writes.Release(); }
    }

    public void Dispose() => stream.Dispose();
}
