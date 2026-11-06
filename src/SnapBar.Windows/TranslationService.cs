using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SnapBar.Windows;

public sealed record TranslationSettings(string Key, string Region, string Target)
{
    public bool IsValid => Key is { Length: > 0 and <= 256 } && Key.All(char.IsAsciiLetterOrDigit)
        && Region is { Length: <= 64 } && Region.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
        && Target is "zh-Hant" or "en" or "ja" or "ko" or "zh-Hans";

    public override string ToString() => $"Azure Translator ({Target})";
}

public sealed record TranslationResult(string Status, string? Text = null);

public sealed class TranslationService
{
    private const string Endpoint = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = 1024 * 1024
    };
    private readonly HttpClient client;
    private readonly string path;

    public TranslationService(string directory, HttpClient? client = null)
    {
        path = Path.Combine(Path.GetFullPath(directory), "translator.key");
        this.client = client ?? SharedClient;
    }

    public TranslationSettings? Load()
    {
        var json = AiSecretStore.ReadFile(path);
        if (json == null) return null;
        try
        {
            var value = JsonSerializer.Deserialize<TranslationSettings>(json);
            return value is { IsValid: true } ? value : null;
        }
        catch (JsonException) { return null; }
    }

    public void Save(TranslationSettings? value)
    {
        if (value is { IsValid: false }) throw new ArgumentException("Invalid translation settings.");
        AiSecretStore.WriteFile(path, value == null ? null : JsonSerializer.Serialize(value));
    }

    public async Task<TranslationResult> TranslateAsync(TranslationSettings settings, string text, CancellationToken token)
    {
        if (!settings.IsValid) return new("configuration");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 50_000) return new("length");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + "&to=" + settings.Target);
        request.Headers.Add("Ocp-Apim-Subscription-Key", settings.Key);
        if (settings.Region.Length > 0) request.Headers.Add("Ocp-Apim-Subscription-Region", settings.Region);
        request.Content = JsonContent.Create(new[] { new { Text = text } });
        try
        {
            using var response = await client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                return new(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "credentials",
                    HttpStatusCode.TooManyRequests => "quota",
                    HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "timeout",
                    _ => "unavailable"
                });
            var items = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
            if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() == 1
                && items[0].ValueKind == JsonValueKind.Object && items[0].TryGetProperty("translations", out var translations)
                && translations.ValueKind == JsonValueKind.Array && translations.GetArrayLength() == 1
                && translations[0].ValueKind == JsonValueKind.Object
                && translations[0].TryGetProperty("text", out var result) && result.ValueKind == JsonValueKind.String
                && result.GetString() is { } output && !string.IsNullOrWhiteSpace(output))
                return new("ok", output);
            return new("invalid-response");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new("timeout"); }
        catch (JsonException) { return new("invalid-response"); }
        catch (Exception e) when (e is HttpRequestException or IOException) { return new("unavailable"); }
    }
}
