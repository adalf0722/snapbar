using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using SnapBar.Core;

namespace SnapBar.Windows;

public sealed class UpdateRateLimitException(DateTimeOffset retryAt) : HttpRequestException("Update source is rate limited.")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}

public sealed class GitHubUpdateSource : IDisposable
{
    public const string Repository = "adalf0722/snapbar";
    private const string Endpoint = "https://api.github.com/repos/" + Repository + "/releases";
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Func<DateTimeOffset> now;

    public GitHubUpdateSource(HttpClient? client = null, Func<DateTimeOffset>? now = null)
    {
        ownsClient = client == null;
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public static Uri ReleaseUri(string version)
    {
        if (!ProductVersion.Parse(version).IsPublic) throw new FormatException("Public version required.");
        return new Uri("https://github.com/" + Repository + "/releases/tag/v" + Uri.EscapeDataString(version));
    }

    public async Task<IReadOnlyList<UpdateRelease>> ReadAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var releases = new List<UpdateRelease>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Uri? next = new(Endpoint + "?per_page=100&page=1");
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; next != null; page++)
        {
            if (page == 10 || !visited.Add(next.AbsoluteUri)) throw new InvalidDataException("Incomplete release listing.");
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.UserAgent.ParseAdd("SnapBar-UpdateCheck/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new UpdateRateLimitException(RetryAt(response));
            response.EnsureSuccessStatusCode();
            // Redirects must never turn a fixed-source request into a different source.
            if (response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path) != Endpoint)
                throw new InvalidDataException("Unexpected update response origin.");
            if (response.Content.Headers.ContentLength > 8 * 1024 * 1024) throw new InvalidDataException("Release response too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                if (body.Length + read > 8 * 1024 * 1024) throw new InvalidDataException("Release response too large.");
                body.Write(buffer, 0, read);
            }
            using var json = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 100)
                throw new InvalidDataException("Invalid release listing.");
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid release.");
                if (!item.TryGetProperty("draft", out var draft) || draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("Missing release state.");
                if (draft.GetBoolean()) continue;
                var tag = item.GetProperty("tag_name").GetString();
                ProductVersion version;
                try { version = ProductVersion.Parse(tag?.StartsWith('v') == true ? tag[1..] : ""); }
                catch (FormatException) { continue; }
                if (!version.IsPublic) continue;
                if (!seen.Add(version.Version) || item.GetProperty("prerelease").GetBoolean() != (version.Channel == "Preview"))
                    throw new InvalidDataException("Contradictory release identity.");
                var name = "SnapBar-v" + version.Version + "-win-x64.zip";
                var assets = item.GetProperty("assets");
                if (assets.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Missing assets.");
                bool Complete(string assetName) => assets.EnumerateArray().Count(asset =>
                    asset.GetProperty("name").GetString() == assetName && asset.GetProperty("state").GetString() == "uploaded"
                    && asset.GetProperty("size").GetInt64() > 0) == 1;
                if (!Complete(name) || !Complete(name + ".sha256")) continue;
                var text = item.TryGetProperty("body", out var notes) && notes.ValueKind == JsonValueKind.String ? notes.GetString() : null;
                releases.Add(new(version.Version, UpdatePolicy.ReadMetadata(text ?? "", version.Version)));
            }
            next = NextPage(response);
        }
        token.ThrowIfCancellationRequested();
        return releases;
    }

    private DateTimeOffset RetryAt(HttpResponseMessage response)
    {
        var minimum = now().AddHours(1);
        var retry = response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is { } delta ? now() + delta : minimum);
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
            && long.TryParse(values.FirstOrDefault(), out var seconds))
        {
            try { if (DateTimeOffset.FromUnixTimeSeconds(seconds) > retry) retry = DateTimeOffset.FromUnixTimeSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { }
        }
        return (retry > minimum ? retry : minimum).ToUniversalTime();
    }

    private static Uri? NextPage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var links)) return null;
        Uri? next = null;
        foreach (var link in links.SelectMany(value => value.Split(',')))
        {
            var match = Regex.Match(link.Trim(), "^<([^>]+)>;\\s*rel=\"([^\"]+)\"$");
            if (!match.Success) throw new InvalidDataException("Invalid pagination.");
            if (match.Groups[2].Value != "next") continue;
            if (next != null || !Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out next)
                || next.GetLeftPart(UriPartial.Path) != Endpoint || next.UserInfo.Length != 0 || next.Fragment.Length != 0
                || !Regex.IsMatch(next.Query, @"\A\?(?:per_page=100&page=[1-9][0-9]*|page=[1-9][0-9]*&per_page=100)\z"))
                throw new InvalidDataException("Unsafe pagination.");
        }
        return next;
    }

    public void Dispose() { if (ownsClient) client.Dispose(); }
}
