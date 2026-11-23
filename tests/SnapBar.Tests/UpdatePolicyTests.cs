using System.Text;
using System.Text.Json;
using SnapBar.Core;

internal static class UpdatePolicyTests
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static UpdateMetadata Metadata(string version, bool desktop = true) => new(1, version, new string('a', 40), desktop,
        new(["更新選取工具列"], ["請先結束舊版。"]), new(["Improved selection toolbar"], ["Exit the previous version first."]));
    private static string Body(UpdateMetadata metadata) => Encode(JsonSerializer.Serialize(metadata, CamelCase));
    private static string Encode(string json) => "<!-- snapbar-update-v1: " + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) + " -->";
    private static bool ThrowsFormat(Action action)
    {
        try { action(); return false; }
        catch (FormatException) { return true; }
    }

    public static void Run(Action<string, Func<bool>> check)
    {
        check("updates compare rc identifiers numerically and transition through stable", () =>
        {
            var versions = new[] { "0.1.0-rc.9", "0.1.0-rc.10", "0.1.0", "0.1.1-rc.1" }.Select(ProductVersion.Parse).ToArray();
            return versions.Zip(versions.Skip(1)).All(pair => pair.First.CompareTo(pair.Second) < 0);
        });
        check("updates order arbitrary length numbers and SemVer identifier types", () =>
            ProductVersion.Parse("999999999999999999999999999999.0.0").CompareTo(ProductVersion.Parse("1000000000000000000000000000000.0.0")) < 0
            && ProductVersion.Parse("1.0.0-99999999999999999999999999").CompareTo(ProductVersion.Parse("1.0.0-alpha")) < 0
            && ProductVersion.Parse("1.0.0-rc").CompareTo(ProductVersion.Parse("1.0.0-rc.1")) < 0
            && ProductVersion.Parse("1.0.0-B").CompareTo(ProductVersion.Parse("1.0.0-a")) < 0
            && ThrowsFormat(() => ProductVersion.Parse("1.0.0-rc.01")));
        check("updates stable filters previews while preview accepts stable and newer previews", () =>
        {
            UpdateRelease[] releases = [new("1.0.1-rc.1", null), new("1.0.0", null)];
            return UpdatePolicy.Select(ProductVersion.Parse("0.9.0"), releases).Latest?.Version == "1.0.0"
                && UpdatePolicy.Select(ProductVersion.Parse("1.0.0-rc.10"), releases).Latest?.Version == "1.0.1-rc.1"
                && UpdatePolicy.Select(ProductVersion.Parse("1.0.0-dev"), releases).Latest == null
                && UpdatePolicy.Select(ProductVersion.Parse("1.0.0-validation"), releases).Notification == null;
        });
        check("updates download and unnotified desktop targets are independent", () =>
        {
            UpdateRelease[] releases = [new("1.4.0", Metadata("1.4.0", false)), new("1.3.0", null),
                new("1.2.0", Metadata("1.2.0")), new("1.1.0", Metadata("1.1.0")), new("0.8.0", Metadata("0.8.0"))];
            var result = UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), releases, ["1.2.0"]);
            return result.Latest?.Version == "1.4.0" && result.Notification == null
                && UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), releases).Notification?.Version == "1.2.0"
                && UpdatePolicy.Select(ProductVersion.Parse("1.4.0"), releases).Latest == null;
        });
        check("updates notified floor follows channel eligibility and ignores invalid history", () =>
        {
            UpdateRelease[] releases = [new("1.2.0", Metadata("1.2.0"))];
            string[] history = ["bad", "9.0.0-dev", "8.0.0-validation", "2.0.0-rc.1", "1.1.0"];
            return UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), releases, history).Notification?.Version == "1.2.0"
                && UpdatePolicy.Select(ProductVersion.Parse("1.0.0-rc.1"), releases, history).Notification == null
                && UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), releases, ["1.2.0"]).Notification == null
                && UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), releases, ["1.3.0"]).Latest?.Version == "1.2.0";
        });
        check("updates retained highest64 history never renotifies evicted older versions", () => WithStore((directory, store) =>
        {
            var history = Enumerable.Range(1, 65).Select(i => $"1.0.{i}").ToArray();
            if (!store.Save(new() { NotifiedVersions = history })) return false;
            var loaded = new UpdateStateStore(Path.Combine(directory, "updates.json")).Load();
            var older = new UpdateRelease("1.0.1", Metadata("1.0.1"));
            var result = UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), [older], loaded.NotifiedVersions);
            var newer = new UpdateRelease("1.0.66", Metadata("1.0.66"));
            return loaded.NotifiedVersions.Length == 64 && !loaded.NotifiedVersions.Contains(older.Version)
                && result.Notification == null && result.Latest?.Version == older.Version
                && UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), [older, newer], loaded.NotifiedVersions).Notification?.Version == newer.Version;
        }));
        check("updates reject duplicate source versions even outside the active channel", () =>
            ThrowsFormat(() => UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), [new("2.0.0-rc.1", null), new("2.0.0-rc.1", Metadata("2.0.0-rc.1"))]))
            && ThrowsFormat(() => UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), [new("garbage", null)])));
        check("updates decode strict bilingual metadata", () =>
        {
            var metadata = UpdatePolicy.ReadMetadata(Body(Metadata("2.0.0")), "2.0.0");
            return metadata?.Zh.Summaries.Single() == "更新選取工具列" && metadata.En.Notices.Length == 1;
        });
        check("updates reject missing duplicated mismatched and unknown metadata", () =>
        {
            var body = Body(Metadata("2.0.0"));
            return UpdatePolicy.ReadMetadata(null, "2.0.0") == null
                && UpdatePolicy.ReadMetadata(body + body, "2.0.0") == null
                && UpdatePolicy.ReadMetadata(body, "2.0.1") == null
                && UpdatePolicy.ReadMetadata(Body(Metadata("2.0.0") with { SchemaVersion = 2 }), "2.0.0") == null
                && UpdatePolicy.ReadMetadata(Body(Metadata("2.0.0") with { SourceCommit = "bad" }), "2.0.0") == null
                && UpdatePolicy.ReadMetadata(Encode("{\"schemaVersion\":1,\"schemaVersion\":1}"), "2.0.0") == null
                && UpdatePolicy.ReadMetadata("<!-- snapbar-update-v1: !!! -->", "2.0.0") == null;
        });
        check("updates reject invalid UTF8 controls translations and contract sizes", () =>
        {
            var metadata = Metadata("2.0.0");
            var invalid = "<!-- snapbar-update-v1: " + Convert.ToBase64String([0xff, 0xfe]) + " -->";
            return UpdatePolicy.ReadMetadata(invalid, "2.0.0") == null
                && UpdatePolicy.ReadMetadata(Body(metadata with { En = new([], []) }), "2.0.0") == null
                && UpdatePolicy.ReadMetadata(Body(metadata with { Zh = new([new string('中', 141)], []) }), "2.0.0") == null
                && UpdatePolicy.ReadMetadata(Body(metadata with { En = new(["hidden\u0001control"], []) }), "2.0.0") == null
                && UpdatePolicy.ReadMetadata(Body(metadata with { En = new(["Summary"], [new string('n', 2001)]) }), "2.0.0") == null
                && UpdatePolicy.ReadMetadata(Encode(new string(' ', UpdatePolicy.MaximumMetadataBytes + 1)), "2.0.0") == null;
        });
        check("updates invalid metadata remains download-only and never reaches display", () =>
        {
            var result = UpdatePolicy.Select(ProductVersion.Parse("1.0.0"), [new("2.0.0", Metadata("2.0.0") with { SourceCommit = "bad" })]);
            return result.Latest is { Version: "2.0.0", Metadata: null } && result.Notification == null;
        });
        check("update state roundtrip preserves preferences targets UTC and highest 64 notifications", () => WithStore((directory, store) =>
        {
            var now = DateTimeOffset.UtcNow;
            var state = new UpdateState { Enabled = false, LastAttemptUtc = now, LastSuccessUtc = now, RetryAtUtc = now.AddHours(1),
                CandidatesFetchedAtUtc = now, Latest = new("3.0.0", Metadata("3.0.0", false)), Notification = new("2.0.0", Metadata("2.0.0")),
                NotifiedVersions = Enumerable.Range(1, 70).Select(i => $"1.0.0-rc.{i}").ToArray() };
            var settings = Path.Combine(directory, "settings.json");
            File.WriteAllText(settings, "existing preferences", new UTF8Encoding(false));
            if (!store.Save(state)) return false;
            var loaded = new UpdateStateStore(Path.Combine(directory, "updates.json")).Load();
            return !loaded.Enabled && loaded.LastAttemptUtc == now && loaded.LastSuccessUtc == now && loaded.RetryAtUtc == state.RetryAtUtc
                && loaded.CandidatesFetchedAtUtc == now && loaded.Latest?.Version == "3.0.0" && loaded.Notification?.Version == "2.0.0"
                && loaded.NotifiedVersions.Length == 64 && loaded.NotifiedVersions[0] == "1.0.0-rc.70"
                && loaded.NotifiedVersions[^1] == "1.0.0-rc.7" && File.ReadAllText(settings) == "existing preferences";
        }));
        foreach (var contents in new[] { "broken", "{\"schemaVersion\":2,\"enabled\":true}", "{\"schemaVersion\":1,\"enabled\":true,\"enabled\":false}", new string('x', 65537) })
            check("update state protects corrupt newer duplicate or oversized existing files " + contents.Length, () => WithStore((directory, store) =>
            {
                var path = Path.Combine(directory, "updates.json");
                File.WriteAllText(path, contents, new UTF8Encoding(false));
                return !store.Load().Enabled && !store.Writable && !store.Save(new()) && File.ReadAllText(path) == contents;
            }));
        check("update state rejects invalid saves without changing valid bytes", () => WithStore((directory, store) =>
        {
            if (!store.Load().Enabled || !store.Save(new())) return false;
            var path = Path.Combine(directory, "updates.json");
            var before = File.ReadAllBytes(path);
            return !store.Save(new() { SchemaVersion = 2 }) && !store.Save(new() { NotifiedVersions = ["invalid"] })
                && !store.Save(new() { Notification = new("2.0.0", null) })
                && !store.Save(new() { LastAttemptUtc = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.FromHours(8)) })
                && File.ReadAllBytes(path).SequenceEqual(before) && store.Writable;
        }));
        check("update state protects a newer destination written after initial load", () => WithStore((directory, store) =>
        {
            store.Load();
            var path = Path.Combine(directory, "updates.json");
            const string newer = "{\"schemaVersion\":2,\"enabled\":true}";
            File.WriteAllText(path, newer, new UTF8Encoding(false));
            return !store.Save(new()) && !store.Writable && File.ReadAllText(path) == newer;
        }));
        check("update state size validation preserves the previous file", () => WithStore((directory, store) =>
        {
            if (!store.Save(new())) return false;
            var path = Path.Combine(directory, "updates.json");
            var before = File.ReadAllBytes(path);
            var oversized = new UpdateState { NotifiedVersions = Enumerable.Range(1, 64).Select(i => new string('9', 1100) + i + ".0.0").ToArray() };
            return !store.Save(oversized) && store.Writable && File.ReadAllBytes(path).SequenceEqual(before)
                && Directory.GetFiles(directory, "*.tmp").Length == 0;
        }));
        check("update state invalid UTF8 remains protected", () => WithStore((directory, store) =>
        {
            var path = Path.Combine(directory, "updates.json");
            byte[] invalid = [0xff, 0xfe, 0x7b, 0x7d];
            File.WriteAllBytes(path, invalid);
            return !store.Load().Enabled && !store.Writable && !store.Save(new()) && File.ReadAllBytes(path).SequenceEqual(invalid);
        }));
        check("update state read-only original disables background checks and is preserved", () => WithStore((directory, store) =>
        {
            if (!store.Save(new())) return false;
            var path = Path.Combine(directory, "updates.json");
            var before = File.ReadAllBytes(path);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                return !store.Load().Enabled && !store.Writable && !store.Save(new()) && File.ReadAllBytes(path).SequenceEqual(before);
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        }));
        check("update state rejects cached metadata whose version disagrees with its release", () => WithStore((directory, store) =>
        {
            var path = Path.Combine(directory, "updates.json");
            var invalid = JsonSerializer.Serialize(new UpdateState { Latest = new("2.0.0", Metadata("3.0.0")) }, CamelCase);
            File.WriteAllText(path, invalid, new UTF8Encoding(false));
            return !store.Load().Enabled && !store.Writable && File.ReadAllText(path) == invalid;
        }));
        check("update state write failures preserve destination and disable persistence", () => WithStore((directory, store) =>
        {
            var path = Path.Combine(directory, "updates.json");
            Directory.CreateDirectory(path);
            return !store.Save(new()) && !store.Writable && Directory.Exists(path);
        }));
    }

    private static bool WithStore(Func<string, UpdateStateStore, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { return check(directory, new(Path.Combine(directory, "updates.json"))); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
