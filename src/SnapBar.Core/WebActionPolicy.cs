namespace SnapBar.Core;

public sealed record WebDestination(string Status, Uri? Uri = null)
{
    public bool Success => Status == "ok" && Uri != null;
}

public static class WebActionPolicy
{
    public const string DefaultSearchTemplate = "https://www.google.com/search?q={query}";
    public const int MaximumTemplateLength = 2048, MaximumDestinationLength = 8192;
    private const string Placeholder = "{query}";

    public static WebDestination OpenLink(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new("invalid-link");
        var value = text.Trim();
        if (value.Length > MaximumDestinationLength) return new("web-address-too-long");
        if (value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || value.IndexOfAny(['\\', '{', '}', '"', '<', '>', '|', '^', '`']) >= 0) return new("invalid-link");
        for (var i = 0; i < value.Length; i++)
            if (value[i] == '%' && (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])))
                return new("invalid-link");
        try { _ = new System.Text.UTF8Encoding(false, true).GetByteCount(value); }
        catch (System.Text.EncoderFallbackException) { return new("invalid-link"); }
        if (!(value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))) return new("invalid-link");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length != 0) return new("invalid-link");
        // IsWellFormedOriginalString rejects valid escaped emoji on this SDK. Explicit
        // character/escape checks above retain full Unicode without repairing malformed input.
        if (uri.AbsoluteUri.Length > MaximumDestinationLength) return new("web-address-too-long");
        return new("ok", uri);
    }

    public static bool IsValidSearchTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > MaximumTemplateLength) return false;
        // Do not repair malformed templates: only query values may contain the placeholder.
        if (template != template.Trim() || template.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return false;
        var index = template.IndexOf(Placeholder, StringComparison.Ordinal);
        var queryStart = template.IndexOf('?');
        var fragmentStart = template.IndexOf('#');
        if (index < 0 || queryStart < 0 || index <= queryStart
            || template.LastIndexOf(Placeholder, StringComparison.Ordinal) != index
            || fragmentStart >= 0 && fragmentStart < index) return false;
        // A fixed key and '=' keep encoded text out of the authority and parameter names.
        var parameterStart = template.LastIndexOf('&', index);
        parameterStart = Math.Max(parameterStart, queryStart);
        var equals = template.IndexOf('=', parameterStart + 1);
        if (equals <= parameterStart + 1 || equals >= index) return false;
        return OpenLink(template.Replace(Placeholder, "snapbar", StringComparison.Ordinal)).Success;
    }

    public static WebDestination Search(string? text, string? template)
    {
        if (!IsValidSearchTemplate(template)) return new("invalid-search-template");
        if (string.IsNullOrWhiteSpace(text)) return new("no-selection");
        if (text.Length > 32768) return new("web-address-too-long");
        var destination = template!.Replace(Placeholder, Uri.EscapeDataString(text.Trim()), StringComparison.Ordinal);
        if (destination.Length > MaximumDestinationLength) return new("web-address-too-long");
        return OpenLink(destination);
    }
}
