using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SnapBar.Core;

internal static class OfflineDictionaryTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        var folder = Directory.CreateTempSubdirectory("snapbar-dictionary-");
        try
        {
            var path = Path.Combine(folder.FullName, "test.ifo");
            var saved = Path.Combine(folder.FullName, "installed.bin");
            var dictionary = new OfflineDictionary(saved);
            var index = new MemoryStream();
            var entries = new MemoryStream();
            foreach (var (word, meaning) in new[] { ("child", "孩子"), ("go", "走"), ("run", "跑"), ("take off", "起飛") })
            {
                var definition = Encoding.UTF8.GetBytes(meaning);
                index.Write(Encoding.UTF8.GetBytes(word));
                index.WriteByte(0);
                var span = new byte[8];
                BinaryPrimitives.WriteUInt32BigEndian(span, (uint)entries.Length);
                BinaryPrimitives.WriteUInt32BigEndian(span.AsSpan(4), (uint)definition.Length);
                index.Write(span);
                entries.Write(definition);
            }
            File.WriteAllText(path, $"StarDict's dict ifo file\nversion=2.4.2\nbookname=Test\nwordcount=4\nidxfilesize={index.Length}\nsametypesequence=m\n");
            var indexPath = Path.ChangeExtension(path, ".idx");
            File.WriteAllBytes(indexPath, index.ToArray());
            var compressedPath = Path.ChangeExtension(path, ".dict.dz");
            using (var compressed = new GZipStream(File.Create(compressedPath), CompressionMode.Compress))
                compressed.Write(entries.ToArray());

            check("dictionary imports compressed data and matches normalized exact terms and phrases", () =>
                dictionary.Import(path) == "Test" && dictionary.Lookup(" “TAKE  OFF!” ") == ("take off", "起飛")
                && dictionary.Lookup("RUN") == ("run", "跑") && dictionary.Lookup("screenshot") == null);
            check("dictionary resolves familiar inflections and keeps unmatched selections offline", () =>
                dictionary.Lookup("running") == ("run", "跑") && dictionary.Lookup("children") == ("child", "孩子")
                && dictionary.Lookup("went") == ("go", "走") && !OfflineDictionary.CanLookup("https://example.com"));
            check("dictionary validates offsets before replacing the installed snapshot", () =>
            {
                var bad = index.ToArray();
                bad[6] = 255;
                File.WriteAllBytes(indexPath, bad);
                return Rejected() && dictionary.Lookup("child") == ("child", "孩子")
                    && new OfflineDictionary(saved).Lookup("child") == ("child", "孩子");
            });
            check("dictionary rejects oversized decompression and keeps the previous snapshot", () =>
            {
                File.WriteAllBytes(indexPath, index.ToArray());
                using (var compressed = new GZipStream(File.Create(compressedPath), CompressionLevel.Fastest))
                {
                    var megabyte = new byte[1024 * 1024];
                    for (var i = 0; i < 65; i++) compressed.Write(megabyte);
                }
                return Rejected() && new OfflineDictionary(saved).Lookup("child") == ("child", "孩子");
            });
            check("dictionary removal clears the cached result and saved snapshot", () =>
            {
                dictionary.Remove();
                return !dictionary.IsInstalled && dictionary.Lookup("child") == null;
            });

            bool Rejected()
            {
                try { dictionary.Import(path); return false; }
                catch (InvalidDataException) { return true; }
            }
        }
        finally { folder.Delete(recursive: true); }
    }
}
