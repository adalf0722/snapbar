using System.Text;
using System.Text.Json;

namespace SnapBar.Core;

public sealed record InstalledDictionary(string Id, string Name, int EntryCount, bool Enabled, string Snapshot);
public sealed record DictionaryMatch(string DictionaryName, string Headword, string Text);
public sealed record DictionaryLookup(IReadOnlyList<DictionaryMatch> Matches, IReadOnlyList<string> UnavailableSources);

public sealed class DuplicateDictionaryException(InstalledDictionary existing) : InvalidOperationException("已匯入同名詞典。")
{
    public InstalledDictionary Existing { get; } = existing;
}

public sealed class OfflineDictionaryLibrary(string legacyPath)
{
    private readonly object gate = new();
    private readonly string directory = legacyPath + ".library";
    private string Manifest => Path.Combine(directory, "library.json");
    private List<InstalledDictionary>? installed;
    private readonly Dictionary<string, OfflineDictionary> cache = new(StringComparer.Ordinal);

    public bool IsInstalled
    {
        get
        {
            try { return Dictionaries.Count > 0; }
            catch (Exception e) when (IsReadError(e)) { return File.Exists(Manifest) || File.Exists(legacyPath); }
        }
    }

    public IReadOnlyList<InstalledDictionary> Dictionaries
    {
        get { lock (gate) { EnsureLoaded(); return installed!.ToArray(); } }
    }

    public InstalledDictionary Import(string metadataPath, string? replaceId = null)
    {
        lock (gate)
        {
            EnsureLoaded();
            Directory.CreateDirectory(directory);
            var snapshot = Guid.NewGuid().ToString("N");
            var path = SnapshotPath(snapshot);
            var dictionary = new OfflineDictionary(path);
            var committed = false;
            try
            {
                dictionary.Import(metadataPath);
                var info = dictionary.Info;
                var existing = installed!.FirstOrDefault(item => string.Equals(item.Name.Trim(), info.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing != null && replaceId != existing.Id) throw new DuplicateDictionaryException(existing);
                if (replaceId != null && existing?.Id != replaceId)
                    throw new InvalidOperationException("詞典清單已變更，請重新匯入。");
                var entry = new InstalledDictionary(existing?.Id ?? Guid.NewGuid().ToString("N"), info.Name, info.EntryCount,
                    existing?.Enabled ?? true, snapshot);
                var next = installed!.ToList();
                if (existing == null) next.Add(entry);
                else next[next.IndexOf(existing)] = entry;
                Save(next);
                committed = true;
                cache[snapshot] = dictionary;
                if (existing != null) Forget(existing.Snapshot);
                return entry;
            }
            finally { if (!committed) TryDelete(path); }
        }
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (gate)
        {
            EnsureLoaded();
            var next = installed!.ToList();
            var index = next.FindIndex(item => item.Id == id);
            if (index < 0) throw new InvalidOperationException("詞典已移除。");
            next[index] = next[index] with { Enabled = enabled };
            Save(next);
            if (!enabled) cache.Remove(next[index].Snapshot);
        }
    }

    public void Remove(string id)
    {
        lock (gate)
        {
            EnsureLoaded();
            var entry = installed!.FirstOrDefault(item => item.Id == id);
            if (entry == null) return;
            Save(installed!.Where(item => item.Id != id).ToList());
            Forget(entry.Snapshot);
        }
    }

    public DictionaryLookup Lookup(string selection)
    {
        if (!OfflineDictionary.CanLookup(selection)) return new([], []);
        lock (gate)
        {
            EnsureLoaded();
            var sources = installed!.Where(item => item.Enabled).ToArray();
            var unavailable = new HashSet<string>(StringComparer.Ordinal);
            // Let the single-source normalizer handle punctuation and spacing first.
            var matches = Find(selection, exactOnly: true);
            if (matches.Count == 0) matches = Find(selection, exactOnly: false);
            return new(matches, unavailable.ToArray());

            List<DictionaryMatch> Find(string term, bool exactOnly)
            {
                var found = new List<DictionaryMatch>();
                foreach (var source in sources)
                {
                    if (unavailable.Contains(source.Name)) continue;
                    try
                    {
                        if (!cache.TryGetValue(source.Snapshot, out var dictionary))
                            cache[source.Snapshot] = dictionary = new(SnapshotPath(source.Snapshot));
                        if (!dictionary.IsInstalled) { unavailable.Add(source.Name); continue; }
                        if (dictionary.Lookup(term, exactOnly) is { } match)
                            found.Add(new(source.Name, match.Headword, match.Text));
                    }
                    catch (Exception e) when (IsReadError(e)) { unavailable.Add(source.Name); }
                }
                return found;
            }
        }
    }

    private void EnsureLoaded()
    {
        if (installed != null) return;
        if (File.Exists(Manifest))
        {
            using var stream = File.OpenRead(Manifest);
            if (stream.Length > 1024 * 1024) throw InvalidManifest();
            List<InstalledDictionary> entries;
            try { entries = JsonSerializer.Deserialize<List<InstalledDictionary>>(stream) ?? throw InvalidManifest(); }
            catch (JsonException) { throw InvalidManifest(); }
            if (entries.Count > 256 || entries.Any(item => item == null || !IsId(item.Id) || !IsId(item.Snapshot)
                || string.IsNullOrWhiteSpace(item.Name) || item.EntryCount <= 0)
                || entries.Select(item => item.Id).Distinct().Count() != entries.Count
                || entries.Select(item => item.Snapshot).Distinct().Count() != entries.Count)
                throw InvalidManifest();
            installed = entries;
            return;
        }
        if (!File.Exists(legacyPath)) { installed = []; return; }
        // Keep the old snapshot as a recovery copy. An empty manifest prevents re-import after removal.
        var legacy = new OfflineDictionary(legacyPath);
        var info = legacy.Info;
        Directory.CreateDirectory(directory);
        var snapshot = Guid.NewGuid().ToString("N");
        var path = SnapshotPath(snapshot);
        try
        {
            File.Copy(legacyPath, path);
            Save([new(Guid.NewGuid().ToString("N"), info.Name, info.EntryCount, true, snapshot)]);
        }
        catch { TryDelete(path); throw; }
    }

    private void Save(List<InstalledDictionary> entries)
    {
        if (entries.Count > 256) throw new InvalidDataException("最多可匯入 256 本詞典。");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entries);
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("詞典清單超過大小上限。");
        Directory.CreateDirectory(directory);
        var temporary = Manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, Manifest, overwrite: true);
            installed = entries;
        }
        finally { TryDelete(temporary); }
    }

    private string SnapshotPath(string snapshot) => Path.Combine(directory, snapshot + ".bin");
    private static bool IsId(string value) => Guid.TryParseExact(value, "N", out _);
    private static InvalidDataException InvalidManifest() => new("詞典清單損毀，請還原備份的 library.json。");
    private static bool IsReadError(Exception e) => e is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException;
    private void Forget(string snapshot) { cache.Remove(snapshot); TryDelete(SnapshotPath(snapshot)); }
    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
