namespace SnapBar.Core;

public static class InterfaceLanguage
{
    public const string System = "system", TraditionalChinese = "zh-Hant", English = "en";

    public static string Normalize(string? preference) => preference is TraditionalChinese or English ? preference : System;

    public static string Resolve(string? preference, string? systemUiLanguage)
    {
        preference = Normalize(preference);
        if (preference != System) return preference;
        var culture = (systemUiLanguage ?? "").ToLowerInvariant();
        return culture is "zh-tw" or "zh-hk" or "zh-mo" or "zh-hant" || culture.StartsWith("zh-hant-", StringComparison.Ordinal)
            ? TraditionalChinese : English;
    }
}
