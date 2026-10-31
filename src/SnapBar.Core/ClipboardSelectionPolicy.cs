using System.Text.Json;
using System.Text;
using System.Buffers.Binary;

namespace SnapBar.Core;

public static class ClipboardSelectionPolicy
{
    // Chromium custom_data_helper.cc uses a Pickle header, item count and aligned UTF-16 pairs.
    public static bool IsChromiumSelectionData(ReadOnlySpan<byte> bytes, bool requireEditorMetadata = false)
    {
        if (bytes.Length < 8 || bytes.Length > 1048576) return false;
        var payload = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (payload > bytes.Length - 4 || payload < 4) return false;
        bytes = bytes[..((int)payload + 4)];
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        if (count is < 0 or > 128) return false;
        var offset = 8; bool found = false;
        for (var i = 0; i < count; i++)
        {
            var type = ReadString(bytes, ref offset); var value = ReadString(bytes, ref offset);
            if (type == null || value == null) return false;
            if (type != "vscode-editor-data") continue;
            if (found || !IsSelectionMetadata(value)) return false;
            found = true;
        }
        return offset == bytes.Length && (!requireEditorMetadata || found);
    }

    private static string? ReadString(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (offset > bytes.Length - 4) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]); offset += 4;
        if (length < 0 || length > (bytes.Length - offset) / 2) return null;
        var size = length * 2; var aligned = (size + 3) & ~3;
        if (aligned > bytes.Length - offset) return null;
        try
        {
            var value = new UnicodeEncoding(false, false, true).GetString(bytes.Slice(offset, size));
            offset += aligned; return value;
        }
        catch (DecoderFallbackException) { return null; }
    }

    public static bool IsSelectionMetadata(string? metadata)
    {
        if (metadata == null) return true;
        if (metadata.Length > 65536) return false;
        try
        {
            using var json = JsonDocument.Parse(metadata);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var flags = root.EnumerateObject().Where(p => p.NameEquals("isFromEmptySelection")).ToArray();
            return flags.Length == 1 && flags[0].Value.ValueKind == JsonValueKind.False;
        }
        catch (JsonException) { return false; }
    }
}
