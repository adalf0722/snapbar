using System.Text;
using System.Text.Json;
using SnapBar.Core;

internal static class AiProviderCatalogTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        var expected = new[] { "openai", "gemini", "claude", "deepseek", "groq", "openrouter", "xai", "minimax", "compatible" };
        check("AI catalog creates nine stable profiles", () => AiProviderCatalog.CreateProfiles().Select(p => p.Id).SequenceEqual(expected));
        check("AI catalog keeps model choice explicit", () => AiProviderCatalog.CreateProfiles().All(p => p.Model.Length == 0));
        check("AI protocol preserves serialized identities", () => (int)AiProtocol.Responses == 0 && (int)AiProtocol.ChatCompletions == 1
            && (int)AiProtocol.Messages == 2);
        var endpoints = new[] { "https://api.openai.com/v1/responses", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
            "https://api.anthropic.com/v1/messages", "https://api.deepseek.com/chat/completions", "https://api.groq.com/openai/v1/chat/completions",
            "https://openrouter.ai/api/v1/chat/completions", "https://api.x.ai/v1/chat/completions", "https://api.minimax.io/v1/chat/completions",
            "http://localhost:11434/v1/chat/completions" };
        check("AI catalog uses approved endpoints and protocols", () => AiProviderCatalog.CreateProfiles().Select(p => p.Endpoint).SequenceEqual(endpoints)
            && AiProviderCatalog.CreateProfiles().All(p => p.Protocol == (p.Id == "openai" ? AiProtocol.Responses : p.Id == "claude" ? AiProtocol.Messages : AiProtocol.ChatCompletions)));
        var hosts = new[] { "openai.com", "google.com", "claude.com", "deepseek.com", "groq.com", "openrouter.ai", "x.ai", "minimax.io" };
        bool Official(string? url, string host) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && (uri.Host == host || uri.Host.EndsWith("." + host, StringComparison.Ordinal)) && uri.UserInfo.Length == 0;
        check("AI catalog provides verified model metadata and official HTTPS links", () => AiProviderCatalog.Definitions.Count == 9 && AiProviderCatalog.Definitions.Take(8).Select((d, i) =>
            d.Models.Length > 0 && d.Models.All(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name))
            && Official(d.KeyUrl, hosts[i]) && Official(d.ModelsUrl, hosts[i]) && d.VerifiedOn == "2026-11-03").All(x => x));
        check("AI custom catalog has no assumed models or links", () => AiProviderCatalog.Find("compatible") is { Models.Length: 0, KeyUrl: null, ModelsUrl: null }
            && AiProviderCatalog.Find("unknown") == null && AiProviderCatalog.Find("OpenAI") == null);
        check("AI catalog isolates returned defaults and model arrays", () =>
        {
            var profiles = AiProviderCatalog.CreateProfiles(); profiles[0] = profiles[0] with { Model = "changed" };
            var definitions = AiProviderCatalog.Definitions; definitions[0].Models[0] = new("changed", "Changed");
            var found = AiProviderCatalog.Find("openai")!; found.Models[0] = new("changed", "Changed");
            return AiProviderCatalog.CreateProfiles()[0].Model == "" && AiProviderCatalog.Find("openai")!.Models[0].Id != "changed"
                && !ReferenceEquals(found.Profile, AiProviderCatalog.Find("openai")!.Profile)
                && !ReferenceEquals(definitions[0].Profile, AiProviderCatalog.Definitions[0].Profile);
        });
        var legacy = new AiSettings { Profiles = [new("compatible", "My server", AiProtocol.ChatCompletions, "https://example.invalid/chat", "private-model")],
            SelectedProfileId = "compatible", PinnedIds = ["summary"], RecentIds = ["grammar"] };
        check("AI catalog preserves custom configuration and selection", () =>
        {
            var merged = AiProviderCatalog.MergeMissing(legacy);
            return merged.Profiles[0] == legacy.Profiles[0] && merged.SelectedProfileId == "compatible" && merged.Profiles.Length == 9
                && merged.Prompts.SequenceEqual(legacy.Prompts) && merged.PinnedIds.SequenceEqual(legacy.PinnedIds)
                && merged.RecentIds.SequenceEqual(legacy.RecentIds) && AiProviderCatalog.MergeMissing(merged).Profiles.SequenceEqual(merged.Profiles);
        });
        check("AI catalog preserves same-id overrides for every provider", () =>
        {
            var profiles = AiProviderCatalog.CreateProfiles().Select(p => p with { Name = "Override", Model = "private", Protocol = AiProtocol.ChatCompletions,
                Endpoint = "https://example.invalid/custom" }).ToArray();
            return AiProviderCatalog.MergeMissing(new() { Profiles = profiles }).Profiles.SequenceEqual(profiles);
        });
        bool Reject(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
        AiSettings Sized(int count) => legacy with { Profiles = Enumerable.Range(0, count).Select(i => legacy.Profiles[0] with { Id = "custom" + i }).ToArray(), SelectedProfileId = "custom0" };
        check("AI normalization accepts one and 32 profiles without merging", () => AiPolicy.Normalize(Sized(1)).Profiles.Length == 1 && AiPolicy.Normalize(Sized(32)).Profiles.Length == 32);
        check("AI normalization rejects 33 profiles", () => Reject(() => AiPolicy.Normalize(Sized(33))));
        check("AI merging accepts exactly 32 and rejects overflow without dropping profiles", () => AiProviderCatalog.MergeMissing(Sized(23)).Profiles.Length == 32
            && Reject(() => AiProviderCatalog.MergeMissing(Sized(24))) && Reject(() => AiProviderCatalog.MergeMissing(Sized(32))));
        StoreChecks(check, legacy, Sized(24));
    }

    private static void StoreChecks(Action<string, Func<bool>> check, AiSettings legacy, AiSettings overflowing)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Ai.Catalog." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ai.json");
        try
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            check("AI load merges legacy only in memory and explicit save persists it", () =>
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(legacy, options); File.WriteAllBytes(path, bytes);
                var store = new AiSettingsStore(path); var loaded = store.Load();
                if (loaded.Warning != null || loaded.Settings.Profiles.Length != 9 || !bytes.SequenceEqual(File.ReadAllBytes(path))
                    || loaded.Settings.Profiles[0] != legacy.Profiles[0] || loaded.Settings.SelectedProfileId != legacy.SelectedProfileId) return false;
                store.Save(loaded.Settings);
                return new AiSettingsStore(path).Load().Settings.Profiles.Length == 9 && !bytes.SequenceEqual(File.ReadAllBytes(path));
            });
            check("AI explicit save does not implicitly merge profiles", () =>
            {
                var store = new AiSettingsStore(path); store.Load(); store.Save(legacy);
                var saved = JsonSerializer.Deserialize<AiSettings>(File.ReadAllBytes(path), options)!;
                return saved.Profiles.SequenceEqual(legacy.Profiles) && saved.Prompts.SequenceEqual(legacy.Prompts);
            });
            foreach (var (json, warning) in new[] { ("{broken", "settings-invalid"), ("{\"schemaVersion\":2}", "settings-version"),
                (JsonSerializer.Serialize(overflowing, options), "settings-invalid") })
                check("AI load preserves protected file bytes " + warning + " " + json.Length, () =>
                {
                    var bytes = new UTF8Encoding(false, true).GetBytes(json); File.WriteAllBytes(path, bytes);
                    var store = new AiSettingsStore(path); var loaded = store.Load();
                    try { store.Save(loaded.Settings); return false; }
                    catch (InvalidOperationException) { return loaded.Warning == warning && bytes.SequenceEqual(File.ReadAllBytes(path)); }
                });
        }
        finally { Directory.Delete(directory, true); }
    }
}
