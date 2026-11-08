using SnapBar.Core;

namespace SnapBar.App;

internal static class ValidationMessages
{
    internal static string Describe(string? code, UiLanguage? language = null, params object?[] arguments)
    {
        if (code == null) return "";
        var culture = language?.Culture ?? UiText.Culture;
        var key = "Validation_" + code;
        if (UiText.Resources.GetString(key, culture) == null) key = "Validation_Unknown";
        return UiText.Get(culture, key, arguments);
    }
    internal static string Describe(Exception error, UiLanguage? language = null) => ProductError.FromException(error) is { } validation
        ? Describe(validation.Code, language, validation.Arguments.ToArray()) : Describe("Unknown", language);
}
