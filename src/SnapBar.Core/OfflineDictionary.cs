using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SnapBar.Core;

public sealed class OfflineDictionary(string filePath)
{
    private const int MetadataLimit = 64 * 1024;
    private const int IndexLimit = 16 * 1024 * 1024;
    private const int DataLimit = 64 * 1024 * 1024;
    private const int EntryLimit = 512 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex Term = new(@"^[A-Za-z]+(?:[.'’ -][A-Za-z]+)*\z", RegexOptions.NonBacktracking);
    private static readonly Dictionary<string, string> Irregular = new(StringComparer.OrdinalIgnoreCase)
    {
        ["went"] = "go", ["gone"] = "go", ["children"] = "child", ["mice"] = "mouse",
        ["men"] = "man", ["women"] = "woman", ["feet"] = "foot", ["teeth"] = "tooth",
        ["ran"] = "run", ["better"] = "good", ["best"] = "good"
    };
    private readonly object gate = new();
    private Lexicon? loaded;

    public bool IsInstalled => File.Exists(filePath);

    public (string Name, int EntryCount) Info
    {
        get
        {
            lock (gate)
            {
                loaded ??= Load();
                return (loaded.Name, loaded.Entries.Count);
            }
        }
    }

    public string Import(string metadataPath)
    {
        if (!Path.GetExtension(metadataPath).Equals(".ifo", StringComparison.OrdinalIgnoreCase))
            throw ProductError.InvalidData("dictionary-ifo-required");
        var indexPath = Path.ChangeExtension(metadataPath, ".idx");
        var dataPath = Path.ChangeExtension(metadataPath, ".dict");
        var metadata = ReadFile(metadataPath, MetadataLimit);
        var index = ReadFile(indexPath, IndexLimit);
        byte[] data;
        if (File.Exists(dataPath)) data = ReadFile(dataPath, DataLimit);
        else
        {
            var compressed = ReadFile(dataPath + ".dz", DataLimit);
            using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
            data = ReadBounded(gzip, DataLimit);
        }
        var candidate = Parse(metadata, index, data);
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            var temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                {
                    using var writer = new BinaryWriter(stream);
                    writer.Write("SnapBar dictionary 1"u8);
                    foreach (var part in new[] { metadata, index, data })
                    {
                        writer.Write(part.Length);
                        writer.Write(part);
                    }
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, filePath, overwrite: true);
                loaded = candidate;
            }
            finally { File.Delete(temporary); }
        }
        return candidate.Name;
    }

    public void Remove()
    {
        lock (gate)
        {
            File.Delete(filePath);
            loaded = null;
        }
    }

    public (string Headword, string Text)? Lookup(string selection, bool exactOnly = false)
    {
        var query = Normalize(selection);
        if (query == null) return null;
        Lexicon? lexicon;
        lock (gate)
        {
            if (loaded == null && IsInstalled) loaded = Load();
            lexicon = loaded;
        }
        if (lexicon == null) return null;
        foreach (var term in exactOnly ? [query] : Candidates(query))
        {
            if (lexicon.Entries.TryGetValue(term, out var entry))
                return (entry.Headword, ReadEntry(lexicon.Data.AsSpan(entry.Offset, entry.Length), lexicon.Sequence, render: true)!);
        }
        return null;
    }

    public static bool CanLookup(string selection) => Normalize(selection) != null;

    private static string? Normalize(string selection)
    {
        if (selection.Length > 160) return null;
        var trimmed = selection.Trim().Trim('"', '\'', '“', '”', '‘', '’', '(', ')', '[', ']', ',', '.', '!', '?', ';', ':').Trim();
        var term = string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Term.IsMatch(term) ? term : null;
    }

    private static IEnumerable<string> Candidates(string query)
    {
        yield return query;
        if (query.Contains(' ')) yield break;
        if (Irregular.TryGetValue(query, out var lemma)) yield return lemma;
        var word = query.ToLowerInvariant();
        if (word.Length < 4) yield break;
        if (word.EndsWith("ies") || word.EndsWith("ied")) yield return word[..^3] + "y";
        if (word.EndsWith("ing") || word.EndsWith("ed"))
        {
            var stem = word.EndsWith("ing") ? word[..^3] : word[..^2];
            if (stem.Length > 2 && stem[^1] == stem[^2]) yield return stem[..^1];
            yield return stem;
            yield return stem + "e";
        }
        if (word.EndsWith('s')) yield return word[..^1];
        if (word.EndsWith("es")) yield return word[..^2];
    }

    private Lexicon Load()
    {
        using var reader = new BinaryReader(File.OpenRead(filePath));
        if (!reader.ReadBytes(20).AsSpan().SequenceEqual("SnapBar dictionary 1"u8))
            throw ProductError.InvalidData("dictionary-stored-format-invalid");
        return Parse(ReadPart(MetadataLimit), ReadPart(IndexLimit), ReadPart(DataLimit));

        byte[] ReadPart(int limit)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > limit || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw ProductError.InvalidData("dictionary-stored-data-invalid");
            return reader.ReadBytes(length);
        }
    }

    private static byte[] ReadFile(string path, int limit)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > limit) throw ProductError.InvalidData("dictionary-file-too-large");
        return ReadBounded(stream, limit);
    }

    private static byte[] ReadBounded(Stream stream, int limit)
    {
        using var data = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            if (data.Length + read > limit) throw ProductError.InvalidData("dictionary-expanded-too-large");
            data.Write(buffer, 0, read);
        }
        return data.ToArray();
    }

    private static Lexicon Parse(byte[] metadata, byte[] index, byte[] data)
    {
        using var lines = new StringReader(Utf8.GetString(metadata).TrimStart('\uFEFF'));
        if (lines.ReadLine() != "StarDict's dict ifo file")
            throw ProductError.InvalidData("dictionary-unrecognized");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        while (lines.ReadLine() is { } line)
        {
            var equals = line.IndexOf('=');
            if (equals > 0) fields[line[..equals]] = line[(equals + 1)..];
        }
        var name = fields.GetValueOrDefault("bookname");
        var sequence = fields.GetValueOrDefault("sametypesequence", "");
        if (fields.GetValueOrDefault("version") != "2.4.2" || fields.GetValueOrDefault("idxoffsetbits", "32") != "32"
            || sequence.Any(type => type is not ('m' or 't')))
            throw ProductError.InvalidData("dictionary-format-unsupported");
        if (string.IsNullOrWhiteSpace(name) || !int.TryParse(fields.GetValueOrDefault("idxfilesize"), out var size) || size != index.Length
            || !int.TryParse(fields.GetValueOrDefault("wordcount"), out var count) || count <= 0 || count > IndexLimit / 16)
            throw InvalidIndex();

        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var remaining = index.AsSpan();
        long indexedData = 0;
        for (var row = 0; row < count; row++)
        {
            var terminator = remaining[..Math.Min(256, remaining.Length)].IndexOf((byte)0);
            if (terminator < 1 || remaining.Length - terminator < 9) throw InvalidIndex();
            var headword = Utf8.GetString(remaining[..terminator]);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(remaining[(terminator + 1)..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(remaining[(terminator + 5)..]);
            if (length == 0 || length > EntryLimit || (ulong)offset + length > (ulong)data.Length) throw InvalidIndex();
            indexedData += length;
            if (indexedData > 4L * DataLimit) throw InvalidIndex();
            if (ReadEntry(data.AsSpan((int)offset, (int)length), sequence, render: false) != null)
                entries.TryAdd(headword, new(headword, (int)offset, (int)length));
            remaining = remaining[(terminator + 9)..];
        }
        if (!remaining.IsEmpty) throw InvalidIndex();
        if (entries.Count == 0) throw ProductError.InvalidData("dictionary-empty");
        return new(name, entries, data, sequence);
    }

    private static string? ReadEntry(ReadOnlySpan<byte> bytes, string sequence, bool render)
    {
        var output = render ? new StringBuilder() : null;
        var meaning = false;
        var field = 0;
        while (!bytes.IsEmpty || field < sequence.Length)
        {
            if (sequence.Length != 0 && field >= sequence.Length) throw InvalidIndex();
            var type = sequence.Length == 0 ? (char)bytes[0] : sequence[field];
            if (sequence.Length == 0) bytes = bytes[1..];
            if (type is not ('m' or 't')) throw ProductError.InvalidData("dictionary-definition-unsupported");
            field++;
            var lastFixedField = field == sequence.Length;
            var end = lastFixedField ? bytes.Length : bytes.IndexOf((byte)0);
            if (end < 0 || (lastFixedField && bytes.Contains((byte)0))) throw InvalidIndex();
            var value = bytes[..end];
            _ = Utf8.GetCharCount(value);
            meaning |= type == 'm' && !value.IsEmpty;
            if (output != null && !value.IsEmpty) output.AppendLine(Utf8.GetString(value));
            bytes = lastFixedField ? [] : bytes[(end + 1)..];
        }
        return meaning ? output?.ToString().Trim() ?? "" : null;
    }

    private static InvalidDataException InvalidIndex() => ProductError.InvalidData("dictionary-index-invalid");
    private sealed record Entry(string Headword, int Offset, int Length);
    private sealed record Lexicon(string Name, Dictionary<string, Entry> Entries, byte[] Data, string Sequence);
}
