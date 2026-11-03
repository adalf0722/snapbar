using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapBar.Core;

[JsonConverter(typeof(HotkeySettingsConverter))]
public sealed record HotkeySettings(int Modifiers = 3, int Key = 0x56)
{
    public static HotkeySettings Default { get; } = new();
    public bool IsValid => (Modifiers & ~7) == 0 && (Modifiers & 3) != 0
        && (Key is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 or >= 0x70 and <= 0x7A)
        && !(Modifiers == 2 && Key is 0x43 or 0x56 or 0x58 or 0x41 or 0x5A or 0x59 or 0x53 or 0x46 or 0x50 or 0x4E or 0x4F or 0x57 or 0x54)
        && !(Modifiers == 1 && Key == 0x73);

    [JsonIgnore]
    public string Display => string.Join(" + ", Parts);
    [JsonIgnore]
    public IEnumerable<string> Parts
    {
        get
        {
            if ((Modifiers & 2) != 0) yield return "Ctrl";
            if ((Modifiers & 1) != 0) yield return "Alt";
            if ((Modifiers & 4) != 0) yield return "Shift";
            yield return Key is >= 0x70 and <= 0x7A ? "F" + (Key - 0x6F) : ((char)Key).ToString();
        }
    }
}

// A damaged shortcut is recoverable independently from other application preferences.
public sealed class HotkeySettingsConverter : JsonConverter<HotkeySettings>
{
    public override bool HandleNull => true;
    public override HotkeySettings Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("modifiers", out var modifiers) && modifiers.ValueKind == JsonValueKind.Number && modifiers.TryGetInt32(out var modifierValue)
            && value.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.Number && key.TryGetInt32(out var keyValue)) return new(modifierValue, keyValue);
        return new(0, 0);
    }
    public override void Write(Utf8JsonWriter writer, HotkeySettings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteNumber("modifiers", value.Modifiers); writer.WriteNumber("key", value.Key); writer.WriteEndObject();
    }
}
