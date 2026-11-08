using System.Buffers.Binary;
using System.Text;
using SnapBar.Core;

internal static class OfflineDictionaryLibraryTests
{
    internal static string WriteDictionary(string folder, string filename, string name, params (string Word, string Meaning)[] words)
    {
        var path = Path.Combine(folder, filename + ".ifo");
        using var index = new MemoryStream();
        using var data = new MemoryStream();
        foreach (var (word, meaning) in words)
        {
            var definition = Encoding.UTF8.GetBytes(meaning);
            index.Write(Encoding.UTF8.GetBytes(word));
            index.WriteByte(0);
            var range = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(range, (uint)data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(range.AsSpan(4), (uint)definition.Length);
            index.Write(range);
            data.Write(definition);
        }
        File.WriteAllText(path, $"StarDict's dict ifo file\nversion=2.4.2\nbookname={name}\nwordcount={words.Length}\nidxfilesize={index.Length}\nsametypesequence=m\n", new UTF8Encoding(false));
        File.WriteAllBytes(Path.ChangeExtension(path, ".idx"), index.ToArray());
        File.WriteAllBytes(Path.ChangeExtension(path, ".dict"), data.ToArray());
        return path;
    }

    public static void Run(Action<string, Func<bool>> check)
    {
        var folder = Directory.CreateTempSubdirectory("snapbar-library-");
        try
        {
            var saved = Path.Combine(folder.FullName, "saved.bin");
            var library = new OfflineDictionaryLibrary(saved);
            var first = WriteDictionary(folder.FullName, "first", "通用詞典", ("child", "孩子"), ("run", "跑"), ("RUN", "重複"));
            var second = WriteDictionary(folder.FullName, "second", "專業詞典", ("child", "子項目"), ("running", "執行中"));
            InstalledDictionary one = null!, two = null!;
            check("dictionary library retains multiple names and counts only usable unique entries", () =>
            {
                one = library.Import(first);
                two = library.Import(second);
                var reloaded = new OfflineDictionaryLibrary(saved).Dictionaries;
                return reloaded.Count == 2 && reloaded[0].Name == "通用詞典" && reloaded[0].EntryCount == 2
                    && reloaded[1].Name == "專業詞典" && reloaded[1].EntryCount == 2;
            });
            check("dictionary library returns all matching sources in import order", () =>
                library.Lookup("CHILD").Matches.Select(item => (item.DictionaryName, item.Text)).SequenceEqual(
                    [("通用詞典", "孩子"), ("專業詞典", "子項目")]) && library.Lookup("absent").Matches.Count == 0);
            check("dictionary library prefers exact matches across sources over earlier inflections", () =>
                library.Lookup("running").Matches is [{ DictionaryName: "專業詞典", Headword: "running", Text: "執行中" }]);
            check("dictionary library persists disabled sources and uses inflections when exact sources are disabled", () =>
            {
                library.SetEnabled(two.Id, false);
                var reload = new OfflineDictionaryLibrary(saved);
                return reload.Dictionaries.Count == 2 && !reload.Dictionaries[1].Enabled
                    && reload.Lookup("running").Matches is [{ DictionaryName: "通用詞典", Headword: "run" }];
            });
            check("dictionary library rejects same-name imports until replacement is explicit", () =>
            {
                var before = Directory.GetFiles(saved + ".library").Length;
                try { library.Import(second); return false; }
                catch (DuplicateDictionaryException e)
                {
                    return e.Existing.Id == two.Id && library.Dictionaries.Count == 2
                        && Directory.GetFiles(saved + ".library").Length == before;
                }
            });
            check("dictionary library replacement preserves identity order and disabled preference", () =>
            {
                WriteDictionary(folder.FullName, "second", "專業詞典", ("child", "新版子項目"));
                var updated = library.Import(second, two.Id);
                if (updated.Id != two.Id || updated.Enabled || updated.EntryCount != 1 || library.Dictionaries[1] != updated) return false;
                library.SetEnabled(two.Id, true);
                return new OfflineDictionaryLibrary(saved).Lookup("child").Matches[1].Text == "新版子項目";
            });
            check("dictionary library rejects damaged replacements without losing working sources", () =>
            {
                File.WriteAllBytes(Path.ChangeExtension(second, ".idx"), [255]);
                try { library.Import(second, two.Id); return false; }
                catch (InvalidDataException)
                {
                    return new OfflineDictionaryLibrary(saved).Lookup("child").Matches.Count == 2
                        && library.Lookup("child").Matches[1].Text == "新版子項目";
                }
            });
            check("dictionary library isolates an unreadable source and still returns healthy matches", () =>
            {
                File.WriteAllBytes(Path.Combine(saved + ".library", library.Dictionaries[1].Snapshot + ".bin"), [0]);
                var result = new OfflineDictionaryLibrary(saved).Lookup("child");
                return result.Matches is [{ DictionaryName: "通用詞典" }]
                    && result.UnavailableSources.SequenceEqual(["專業詞典"]);
            });
            check("dictionary library reports missing snapshots instead of silently treating them as unmatched", () =>
            {
                File.Delete(Path.Combine(saved + ".library", library.Dictionaries[1].Snapshot + ".bin"));
                var result = new OfflineDictionaryLibrary(saved).Lookup("child");
                return result.Matches is [{ DictionaryName: "通用詞典" }]
                    && result.UnavailableSources.SequenceEqual(["專業詞典"]);
            });
            check("dictionary library removal persists without affecting other sources", () =>
            {
                library.Remove(two.Id);
                return new OfflineDictionaryLibrary(saved).Dictionaries is [{ Id: var id }] && id == one.Id
                    && library.Lookup("child").Matches.Count == 1;
            });
            check("dictionary library supports all sources disabled and re-enabling after reload", () =>
            {
                library.SetEnabled(one.Id, false);
                var reload = new OfflineDictionaryLibrary(saved);
                if (!reload.IsInstalled || reload.Lookup("child").Matches.Count != 0) return false;
                reload.SetEnabled(one.Id, true);
                return reload.Lookup("child").Matches.Count == 1;
            });
            check("dictionary library migrates the legacy snapshot and never resurrects a removed dictionary", () =>
            {
                var legacyPath = Path.Combine(folder.FullName, "legacy.bin");
                new OfflineDictionary(legacyPath).Import(first);
                var migrated = new OfflineDictionaryLibrary(legacyPath);
                var entry = migrated.Dictionaries.Single();
                if (entry.Name != "通用詞典" || entry.EntryCount != 2 || migrated.Lookup("child").Matches.Count != 1) return false;
                migrated.Remove(entry.Id);
                return File.Exists(legacyPath) && !new OfflineDictionaryLibrary(legacyPath).IsInstalled;
            });
            check("dictionary library failed manifest saves preserve the previous snapshot and enabled state", () =>
            {
                var manifest = Path.Combine(saved + ".library", "library.json");
                var reload = new OfflineDictionaryLibrary(saved);
                _ = reload.Dictionaries;
                using var locked = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
                try { reload.SetEnabled(one.Id, false); return false; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { return reload.Dictionaries[0].Enabled && reload.Lookup("child").Matches.Count == 1; }
            });
            check("dictionary library rejects manifest path traversal before opening source files", () =>
            {
                var invalid = Path.Combine(folder.FullName, "invalid.bin");
                Directory.CreateDirectory(invalid + ".library");
                File.WriteAllText(Path.Combine(invalid + ".library", "library.json"),
                    "[{\"Id\":\"" + Guid.NewGuid().ToString("N") + "\",\"Name\":\"Invalid\",\"EntryCount\":1,\"Enabled\":true,\"Snapshot\":\"../saved\"}]", new UTF8Encoding(false));
                try { _ = new OfflineDictionaryLibrary(invalid).Dictionaries; return false; }
                catch (InvalidDataException) { return true; }
            });
        }
        finally { folder.Delete(recursive: true); }
    }
}
