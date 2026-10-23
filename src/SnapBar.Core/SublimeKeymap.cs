using System.Text;
using System.Text.Json;

namespace SnapBar.Core;

public static class SublimeKeymap
{
    public const string Chord = "ctrl+alt+shift+f12";
    public const string EditableChord = "ctrl+alt+shift+f11";
    public const string CutChord = "ctrl+alt+shift+f10";
    public const string PasteChord = "ctrl+alt+shift+f9";
    private const string Entries = """
    { "keys": ["ctrl+alt+shift+f12"], "command": "noop" },
    { "keys": ["ctrl+alt+shift+f12"], "command": "copy", "context": [
        { "key": "selection_empty", "operand": false, "match_all": true },
        { "key": "num_selections", "operand": 1 },
        { "key": "panel_has_focus", "operand": false },
        { "key": "overlay_visible", "operand": false }
    ] }
    """;
    private static string GuardedEntries(string chord, string command) => $$"""
    { "keys": ["{{chord}}"], "command": "noop" },
    { "keys": ["{{chord}}"], "command": "{{command}}", "context": [
        { "key": "selection_empty", "operand": false, "match_all": true },
        { "key": "num_selections", "operand": 1 },
        { "key": "panel_has_focus", "operand": false },
        { "key": "overlay_visible", "operand": false },
        { "key": "read_only", "operator": "not_equal", "operand": true }
    ] }
    """;
    private static string AdditionalEntries => string.Join(",\n",
        GuardedEntries(EditableChord, "copy"), GuardedEntries(CutChord, "cut"), GuardedEntries(PasteChord, "paste"));
    private static readonly string[] Chords = [Chord, EditableChord, CutChord, PasteChord];
    private static JsonDocument Parse(string text) => JsonDocument.Parse(text,
        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    public static bool HasChord(string text)
    {
        using var doc = Parse(text);
        return doc.RootElement.EnumerateArray().Any(entry => Chords.Any(chord => HasKey(entry, chord)));
    }

    private static string NormalizeChord(string chord) => string.Join('+', chord.Split('+', StringSplitOptions.TrimEntries)
        .Select(part => part.ToLowerInvariant() switch { "control" or "primary" => "ctrl", var other => other })
        .Order(StringComparer.Ordinal));

    private static bool HasKey(JsonElement entry, string chord) => entry.TryGetProperty("keys", out var keys)
        && keys.EnumerateArray().Any(k => k.GetString() is { } key && NormalizeChord(key) == NormalizeChord(chord));

    private static bool Matches(JsonElement root, string chord, string entries)
    {
        using var expected = Parse("[" + entries + "]");
        var actual = root.EnumerateArray().Where(entry => HasKey(entry, chord)).ToArray();
        return actual.Length == 2 && actual.Zip(expected.RootElement.EnumerateArray())
            .All(pair => JsonElement.DeepEquals(pair.First, pair.Second));
    }

    public static bool IsReady(string text)
    {
        using var doc = Parse(text);
        return Matches(doc.RootElement, Chord, Entries)
            && Matches(doc.RootElement, EditableChord, GuardedEntries(EditableChord, "copy"))
            && Matches(doc.RootElement, CutChord, GuardedEntries(CutChord, "cut"))
            && Matches(doc.RootElement, PasteChord, GuardedEntries(PasteChord, "paste"));
    }

    // Insert after the opening bracket so all existing comments and formatting survive.
    public static string Install(string text)
    {
        if (IsReady(text)) return text;
        using var doc = Parse(text);
        var legacy = Matches(doc.RootElement, Chord, Entries)
            && !doc.RootElement.EnumerateArray().Any(entry => Chords.Skip(1).Any(chord => HasKey(entry, chord)));
        if (!legacy && HasChord(text)) throw new InvalidOperationException("Sublime shortcut conflict");
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        reader.Read();
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected key binding array");
        var offset = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, (int)reader.BytesConsumed));
        return text.Insert(offset, "\n// SnapBar guarded clipboard actions (no plugin). Remove these bindings to uninstall.\n"
            + AdditionalEntries + (legacy ? "" : ",\n" + Entries)
            + (doc.RootElement.GetArrayLength() > 0 ? ",\n" : "\n"));
    }
}
