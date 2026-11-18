using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapBar.Core;

internal sealed class ToolbarAppearanceDelayJsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number) throw new JsonException("Expected an integer delay");
        if (reader.TryGetInt32(out var milliseconds)) return ToolbarAppearanceDelay.Normalize(milliseconds);

        // The settings file is size-limited. Inspect the token without converting an
        // integer beyond Int32 to a floating-point value or rejecting other settings.
        using var number = JsonDocument.ParseValue(ref reader);
        var digits = number.RootElement.GetRawText().AsSpan();
        var negative = digits[0] == '-';
        if (negative) digits = digits[1..];
        foreach (var digit in digits)
            if (digit is < '0' or > '9') throw new JsonException("Expected an integer delay");
        return negative ? 0 : ToolbarAppearanceDelay.MaximumMilliseconds;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(ToolbarAppearanceDelay.Normalize(value));
}
