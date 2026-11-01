using System.Text;
using System.Text.Json;
using SnapBar.Core;

internal static class AiRuntimeTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        var defaults = new AiSettings();
        bool Reject(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
        check("AI defaults leave models unset and pin stable prompt ids", () => defaults.Profiles.All(p => p.Model == "")
            && defaults.Profiles.Length == 2 && defaults.Prompts.Length == 5
            && defaults.PinnedIds.SequenceEqual(new[] { "improve", "shorten", "translate-en" }));
        check("AI search finds English aliases globally despite browsing category", () => AiPolicy.Search(defaults, "TRANSLATE english", "寫作").Single().Id == "translate-en");
        check("AI search requires every query word", () => AiPolicy.Search(defaults, "grammar english").Count == 0);
        check("AI whitespace search restricts browsing to category", () => AiPolicy.Search(defaults, " \t\n", "閱讀").Single().Id == "summary");
        check("AI ordering deduplicates pins then recents then remaining", () => AiPolicy.Search(defaults with
            { PinnedIds = ["shorten", "missing", "shorten"], RecentIds = ["summary", "shorten", "grammar", "summary"] }, "")
            .Select(p => p.Id).SequenceEqual(new[] { "shorten", "summary", "grammar", "improve", "translate-en" }));
        check("AI recents normalize to ten known distinct ids", () =>
        {
            var prompts = Enumerable.Range(0, 12).Select(i => new AiPrompt("p" + i, "name", "instruction", "")).ToArray();
            return AiPolicy.Normalize(defaults with { Prompts = prompts, RecentIds = ["missing", .. prompts.Select(p => p.Id), "p0"] }).RecentIds.Length == 10;
        });
        check("AI all prompts may be deleted and stale pins removed", () => AiPolicy.Search(defaults with { Prompts = [] }, "").Count == 0
            && AiPolicy.Normalize(defaults with { Prompts = [] }).PinnedIds.Length == 0);
        var profile = defaults.Profiles[0];
        foreach (var endpoint in new[] { "http://example.com/v1", "/relative", "file:///C:/test", "https://user:pass@example.com/v1", "https://example.com/v1?", "https://example.com/v1#", "https://example.com/v1?q=x", "https://example.com/v1#frag" })
            check("AI rejects unsafe endpoint " + endpoint, () => AiPolicy.ValidateProfile(profile with { Endpoint = endpoint }, false) != null);
        foreach (var endpoint in new[] { "https://api.openai.com/v1/responses", "http://localhost:11434/v1/chat/completions", "http://127.0.0.1:1234/full", "http://[::1]:1234/full" })
            check("AI accepts full endpoint " + endpoint, () => AiPolicy.ValidateProfile(profile with { Endpoint = endpoint }, false) == null);
        check("AI model is required only at execution", () => AiPolicy.ValidateProfile(profile) != null && AiPolicy.ValidateProfile(profile, false) == null);
        var invalidProfiles = new[] { profile with { Id = null! }, profile with { Id = "" }, profile with { Name = null! }, profile with { Name = new('n', 65) },
            profile with { Protocol = (AiProtocol)99 }, profile with { Endpoint = null! }, profile with { Endpoint = new('x', 2049) }, profile with { Model = null! }, profile with { Model = new('m', 129) } };
        int invalidCase = 0;
        foreach (var invalid in invalidProfiles)
            check("AI invalid profile rejected " + ++invalidCase, () => Reject(() => AiPolicy.Normalize(defaults with { Profiles = [invalid] })));
        var prompt = defaults.Prompts[0];
        foreach (var invalid in new[] { prompt with { Id = null! }, prompt with { Name = " " }, prompt with { Name = new('x', 65) }, prompt with { Instruction = null! },
            prompt with { Instruction = "" }, prompt with { Instruction = new('x', 8193) }, prompt with { Category = null! }, prompt with { Category = new('x', 33) },
            prompt with { Keywords = null! }, prompt with { Keywords = new('x', 257) }, prompt with { Icon = null! } })
            check("AI invalid prompt rejected " + ++invalidCase, () => Reject(() => AiPolicy.Normalize(defaults with { Prompts = [invalid] })));
        foreach (var invalid in new AiSettings[] { null!, defaults with { SchemaVersion = 2 }, defaults with { Profiles = null! }, defaults with { Profiles = [] },
            defaults with { Profiles = [profile, profile] }, defaults with { Profiles = [null!] }, defaults with { SelectedProfileId = null! }, defaults with { SelectedProfileId = "missing" },
            defaults with { Profiles = Enumerable.Range(0, 9).Select(i => profile with { Id = "id" + i }).ToArray() }, defaults with { Prompts = null! },
            defaults with { Prompts = [null!] }, defaults with { Prompts = [prompt, prompt] },
            defaults with { Prompts = Enumerable.Range(0, 129).Select(i => prompt with { Id = "id" + i }).ToArray() } })
            check("AI invalid settings rejected " + ++invalidCase, () => Reject(() => AiPolicy.Normalize(invalid)));

        StoreChecks(check);
        SessionChecks(check);
        ManagementChecks(check);
    }

    private static void ManagementChecks(Action<string, Func<bool>> check)
    {
        var initial = new AiSettings { RecentIds = ["summary", "grammar"] };
        check("AI management keeps stored order despite pins and recents", () =>
            AiPromptCatalog.Search(initial, "").Select(p => p.Id).SequenceEqual(initial.Prompts.Select(p => p.Id)));
        check("AI management combines category and every query word", () =>
            AiPromptCatalog.Search(initial, "TRANSLATE english", "寫作").Count == 0
            && AiPromptCatalog.Search(initial, "TRANSLATE english", "翻譯").Single().Id == "translate-en");
        check("AI management distinguishes uncategorized from all categories", () =>
        {
            var settings = initial with { Prompts = [.. initial.Prompts, new("other", "自訂", "custom instruction", "")] };
            return AiPromptCatalog.Search(settings, "", "").Single().Id == "other"
                && AiPromptCatalog.Search(settings, "").Count == 6;
        });
        check("AI management filtering preserves original order", () =>
            AiPromptCatalog.Search(initial, "", "寫作").Select(p => p.Id).SequenceEqual(new[] { "grammar", "improve", "shorten" }));
        check("AI deletion removes only target and its stale references", () =>
        {
            var result = AiPromptCatalog.Delete(initial with { RecentIds = ["improve", "summary"] }, "improve", out var deleted);
            return deleted.Prompt == initial.Prompts[1] && deleted.Index == 1 && deleted.PinnedIndex == 0
                && result.Prompts.Length == 4 && !result.PinnedIds.Contains("improve")
                && result.RecentIds.SequenceEqual(new[] { "summary" });
        });
        check("AI restore merges latest prompts provider and pin changes", () =>
        {
            var removed = AiPromptCatalog.Delete(initial, "improve", out var deleted);
            var edited = removed.Prompts.Select(p => p.Id == "shorten" ? p with { Name = "Updated" } : p).ToArray();
            var current = removed with { Prompts = [.. edited, new("new", "New", "instruction", "")],
                SelectedProfileId = "compatible", PinnedIds = ["summary", .. removed.PinnedIds], RecentIds = ["new", "summary"] };
            var restored = AiPromptCatalog.Restore(current, deleted);
            return restored.Prompts[1] == deleted.Prompt && restored.Prompts.Single(p => p.Id == "shorten").Name == "Updated"
                && restored.Prompts.Last().Id == "new" && restored.SelectedProfileId == "compatible"
                && restored.PinnedIds.SequenceEqual(new[] { "improve", "summary", "shorten", "translate-en" })
                && restored.RecentIds.SequenceEqual(current.RecentIds);
        });
        check("AI restore clamps indexes after other prompts disappear", () =>
        {
            var removed = AiPromptCatalog.Delete(initial, "translate-en", out var deleted);
            var restored = AiPromptCatalog.Restore(removed with { Prompts = [], PinnedIds = [] }, deleted);
            return restored.Prompts.Single() == deleted.Prompt && restored.PinnedIds.Single() == deleted.Prompt.Id;
        });
        check("AI restore preserves unpinned state and does not resurrect recents", () =>
        {
            var removed = AiPromptCatalog.Delete(initial, "summary", out var deleted);
            var restored = AiPromptCatalog.Restore(removed, deleted);
            return deleted.PinnedIndex == -1 && !restored.PinnedIds.Contains("summary") && !restored.RecentIds.Contains("summary");
        });
        bool Reject(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
        check("AI deletion rejects unknown identity without mutation", () =>
            Reject(() => AiPromptCatalog.Delete(initial, "missing", out _)) && initial.Prompts.Length == 5);
        check("AI restore rejects duplicate identity", () =>
        {
            AiPromptCatalog.Delete(initial, "improve", out var deleted);
            return Reject(() => AiPromptCatalog.Restore(initial, deleted));
        });
        check("AI restore rejects full latest collection without dropping commands", () =>
        {
            var removed = AiPromptCatalog.Delete(initial, "improve", out var deleted);
            var full = removed with { Prompts = Enumerable.Range(0, 128).Select(i => new AiPrompt("p" + i, "Name", "instruction", "")).ToArray() };
            return Reject(() => AiPromptCatalog.Restore(full, deleted)) && full.Prompts.Length == 128;
        });
    }

    private static void StoreChecks(Action<string, Func<bool>> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Ai.Core." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ai.json");
        try
        {
            check("AI settings cannot save until Load establishes writability", () =>
            {
                try { new AiSettingsStore(path).Save(new()); return false; } catch (InvalidOperationException) { return !File.Exists(path); }
            });
            check("AI settings atomic roundtrip preserves Unicode without BOM or temp files", () =>
            {
                var store = new AiSettingsStore(path); store.Load(); store.Save(new());
                var bytes = File.ReadAllBytes(path);
                return new AiSettingsStore(path).Load().Settings.Prompts[0].Name == "修正文法拼字"
                    && !bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) && Directory.GetFiles(directory).Length == 1;
            });
            foreach (var bytes in new byte[][] { Encoding.UTF8.GetBytes("{"), Encoding.UTF8.GetBytes("null"), Encoding.UTF8.GetBytes("{\"schemaVersion\":2}"),
                Encoding.UTF8.GetBytes("{\"profiles\":null}"), [0xff], [0xef, 0xbb, 0xbf, 0x7b, 0x7d], new byte[512 * 1024 + 1] })
                check("AI invalid newer oversized or nonUTF8 settings remain unchanged " + bytes.Length + " " + bytes[0], () =>
                {
                    File.WriteAllBytes(path, bytes);
                    var store = new AiSettingsStore(path);
                    if (store.Load().Warning == null) return false;
                    try { store.Save(new()); return false; } catch (InvalidOperationException) { return File.ReadAllBytes(path).SequenceEqual(bytes); }
                });
            check("AI failed validation leaves good settings unchanged", () =>
            {
                File.Delete(path); var store = new AiSettingsStore(path); store.Load(); store.Save(new()); var bytes = File.ReadAllBytes(path);
                try { store.Save(new() { Profiles = [] }); return false; } catch (ArgumentException) { return bytes.SequenceEqual(File.ReadAllBytes(path)); }
            });
            check("AI serialized size cap preserves good original", () =>
            {
                var store = new AiSettingsStore(path); store.Load(); var bytes = File.ReadAllBytes(path);
                var large = new AiSettings { Prompts = Enumerable.Range(0, 128).Select(i => new AiPrompt("id" + i, "name", new string('x', 8192), "")).ToArray() };
                try { store.Save(large); return false; } catch (InvalidOperationException) { return bytes.SequenceEqual(File.ReadAllBytes(path)); }
            });
            check("AI deleting every prompt survives settings restart", () =>
            {
                var store = new AiSettingsStore(path); store.Load(); store.Save(new() { Prompts = [] });
                var loaded = new AiSettingsStore(path).Load(); return loaded.Warning == null && loaded.Settings.Prompts.Length == 0 && loaded.Settings.PinnedIds.Length == 0;
            });
            check("AI failed atomic replacement preserves destination and cleans temporary", () =>
            {
                var store = new AiSettingsStore(path); store.Load(); var bytes = File.ReadAllBytes(path);
                using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try { store.Save(new() { Prompts = [] }); return false; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { return bytes.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(directory).Length == 1; }
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void SessionChecks(Action<string, Func<bool>> check)
    {
        check("AI original starts visible without result and processes original", () =>
        {
            var s = new AiSession("中文原文"); var ticket = s.Begin("translate");
            return s.ViewingOriginal && s.Current == null && s.BasisLabel == "原文" && ticket.SourceText == "中文原文";
        });
        check("AI continuation keeps English result as source", () =>
        {
            var s = new AiSession("中文原文") { Draft = "translate" }; s.Complete(s.Begin(s.Draft), "English result"); s.Draft = "shorten";
            return s.Begin(s.Draft).SourceText == "English result" && s.Draft == "shorten" && s.BasisLabel == "第 1 版";
        });
        check("AI pending draft edits preserve previous processing basis", () =>
        {
            var s = new AiSession("original") { Draft = "first" }; var t = s.Begin(s.Draft); s.Draft = "next"; s.Complete(t, "result");
            return s.Draft == "next" && s.BasisId == null && s.Begin(s.Draft).SourceText == "original";
        });
        check("AI same-value draft assignment does not create an edit revision", () =>
        {
            var s = new AiSession("source") { Draft = "rewrite" }; var t = s.Begin(s.Draft); s.Draft = "rewrite"; s.Complete(t, "result");
            return s.Draft == "" && s.BasisId == 1;
        });
        check("AI explicit original view while pending preserves view and signals result", () =>
        {
            var s = new AiSession("original"); var t = s.Begin("rewrite"); s.ShowOriginal(); s.Complete(t, "result");
            var valid = s.ViewingOriginal && s.HasNewResult; s.ShowResult(); return valid && !s.ViewingOriginal && !s.HasNewResult;
        });
        check("AI history navigation while busy is ignored", () =>
        {
            var s = History(3); var t = s.Begin("next"); s.MoveHistory(-1); return s.Index == 2 && s.BasisId == 3 && s.Busy;
        });
        check("AI explicit views do not change processing basis", () =>
        {
            var s = History(2); s.ShowOriginal(); var basis = s.BasisId; s.ShowResult(); return basis == 2 && s.BasisId == 2;
        });
        check("AI original processing branches at selected history position", () =>
        {
            var s = History(3); s.MoveHistory(-1); s.FromOriginal(); s.Complete(s.Begin("rewrite"), "branch");
            return s.Versions.Select(v => v.Id).SequenceEqual(new long[] { 1, 2, 4 }) && s.Current!.BasisLabel == "原文";
        });
        check("AI draft keeps basis while navigation and branches from its source", () =>
        {
            var s = History(3); s.Draft = "rewrite"; s.MoveHistory(-2); var t = s.Begin(s.Draft); s.Complete(t, "branch");
            return t.BasisId == 3 && t.SourceText == "result3" && s.Versions.Count == 4;
        });
        check("AI empty draft navigation sets basis and history branches monotonically", () =>
        {
            var s = History(3); var unchanged = s.Versions[0]; s.MoveHistory(-2); s.Complete(s.Begin("branch"), "new");
            return s.Versions.Select(v => v.Id).SequenceEqual(new long[] { 1, 4 }) && ReferenceEquals(s.Versions[0], unchanged);
        });
        check("AI retention keeps twenty versions and stable identifiers", () =>
        {
            var s = History(25); return s.Versions.Count == 20 && s.Versions[0].Id == 6 && s.Current!.Id == 25;
        });
        check("AI retention preserves explicitly chosen pending draft basis", () =>
        {
            var s = History(20); s.MoveHistory(-19); s.Draft = "draft"; s.MoveHistory(19);
            s.FromOriginal(); var t = s.Begin("generate"); s.ContinueCurrent(); s.MoveHistory(-19);
            s.Draft = "edited"; s.Complete(t, "result21");
            return s.Versions.Count == 20 && s.Versions[0].Id == 2 && s.Draft == "edited" && s.Begin("next").SourceText == "result20";
        });
        check("AI preserved draft source survives branch removal", () =>
        {
            var s = History(3); s.MoveHistory(-2); s.Draft = "pending"; s.MoveHistory(2);
            var t = s.Begin("branch"); s.ContinueCurrent(); s.Draft = "edited";
            s.Complete(t, "branch4"); return s.BasisId == 3 && !s.Versions.Any(v => v.Id == 3) && s.Begin("next").SourceText == "result3";
        });
        check("AI canceled completion cannot append even after another request", () =>
        {
            var s = History(1); var old = s.Begin("old"); s.Cancel(); var current = s.Begin("new");
            return !s.Complete(old, "late") && s.Busy && s.Complete(current, "fresh") && s.Versions.Count == 2;
        });
        check("AI completion without active request is rejected", () => !new AiSession("source").Complete(null!, "late"));
        check("AI forged ticket cannot finish active request", () =>
        {
            var s = new AiSession("original"); var t = s.Begin("rewrite"); return !s.Complete(t with { }, "forged") && s.Busy && s.Complete(t, "real");
        });
        foreach (var output in new[] { "", " \n", new string('x', 262145) })
            check("AI invalid output preserves prior result " + output.Length, () =>
            {
                var s = History(1); var old = s.Current; return !s.Complete(s.Begin("next"), output) && !s.Busy && s.Current == old;
            });
        check("AI input and instruction boundaries are enforced", () =>
        {
            bool Reject(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
            var s = new AiSession(new string('x', 262144));
            return Reject(() => new AiSession(new string('x', 262145))) && Reject(() => s.Begin(new string('x', 8193)))
                && Reject(() => s.Begin(" ")) && s.Begin(new string('x', 8192)).SourceText.Length == 262144;
        });
    }
    private static AiSession History(int count)
    {
        var s = new AiSession("original"); for (var i = 1; i <= count; i++) s.Complete(s.Begin("rewrite"), "result" + i); return s;
    }
}
