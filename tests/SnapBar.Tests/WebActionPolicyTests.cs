using SnapBar.Core;

internal static class WebActionPolicyTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        check("Google search encodes Unicode, reserved characters, newline and emoji as data", () =>
        {
            var result = WebActionPolicy.Search(" 漢字 &+#?/\n😀 ", WebActionPolicy.DefaultSearchTemplate);
            if (result.Uri?.AbsoluteUri != "https://www.google.com/search?q=%E6%BC%A2%E5%AD%97%20%26%2B%23%3F%2F%0A%F0%9F%98%80")
                throw new Exception("Disposable encoding fixture: " + result.Status + " " + result.Uri?.AbsoluteUri);
            return true;
        });
        check("search query cannot add parameters or a fragment", () =>
            WebActionPolicy.Search("a&admin=yes#fragment", "https://example.com/?q={query}").Uri?.AbsoluteUri
                == "https://example.com/?q=a%26admin%3Dyes%23fragment");
        check("custom search parameters and fixed fragment survive substitution", () =>
            WebActionPolicy.Search("hello world", "https://example.com/search?lang=zh&term={query}#results").Uri?.AbsoluteUri
                == "https://example.com/search?lang=zh&term=hello%20world#results");
        check("blank selection has no search destination", () =>
            WebActionPolicy.Search(" \r\n\t", WebActionPolicy.DefaultSearchTemplate).Status == "no-selection");
        check("oversized search destination is rejected before launching", () =>
            WebActionPolicy.Search(new string('x', 8192), WebActionPolicy.DefaultSearchTemplate).Status == "web-address-too-long");
        foreach (var template in new[]
        {
            "https://example.com/search", "https://example.com/?q={query}&other={query}",
            "https://{query}.example.com/?q=fixed", "https://example.com/{query}?q=fixed",
            "https://example.com/?{query}=value", "https://example.com/?={query}",
            "https://example.com/?q=fixed#{query}", "file:///tmp?q={query}",
            "javascript:alert('{query}')", "https://user:pass@example.com/?q={query}",
            " https://example.com/?q={query}", "https://example.com/?q={query} ",
            "https://example.com/?q=%7Bquery%7D", "https://example.com/?q={query}&bad={unknown}"
        })
            check("invalid search template is rejected: " + template, () =>
                !WebActionPolicy.IsValidSearchTemplate(template)
                && WebActionPolicy.Search("fixture", template).Status == "invalid-search-template");
        check("oversized search template is rejected", () =>
            !WebActionPolicy.IsValidSearchTemplate("https://example.com/?padding=" + new string('a', 2048) + "&q={query}"));
        check("complete HTTPS URL preserves query separators and fragment", () =>
            WebActionPolicy.OpenLink(" https://example.com/path?x=1&y=two#part ").Uri?.AbsoluteUri
                == "https://example.com/path?x=1&y=two#part");
        check("HTTP intranet address is accepted", () => WebActionPolicy.OpenLink("http://localhost:8080/path").Success);
        check("international domain and Unicode path are accepted", () => WebActionPolicy.OpenLink("https://例え.テスト/你好").Success);
        check("percent-encoded emoji in a URL is accepted", () => WebActionPolicy.OpenLink("https://example.com/%F0%9F%98%80").Success);
        check("raw emoji in a URL is accepted", () => WebActionPolicy.OpenLink("https://example.com/😀").Success);
        check("URL percent-encoded space is retained", () =>
            WebActionPolicy.OpenLink("https://example.com/a%20b").Uri?.AbsoluteUri == "https://example.com/a%20b");
        check("HTTP scheme comparison is case insensitive", () =>
            WebActionPolicy.OpenLink("HTTPS://EXAMPLE.COM/Path").Uri?.AbsoluteUri == "https://example.com/Path");
        foreach (var text in new[]
        {
            "", "www.example.com", "//example.com", "https:/example.com", "https://",
            "a paragraph https://example.com", "https://example.com/a b", "https://example.com/a\nb",
            "https://example.com/a\0b", @"https://example.com\path", "https://user@example.com/",
            "https://example.com/%GG", "file:///C:/Windows/notepad.exe", "mailto:user@example.com",
            "javascript:alert(1)", "data:text/html,test", "ms-settings:defaultapps"
        })
            check("non-link or unsupported URL is rejected: " + text.Replace('\0', '_').Replace('\n', '_'), () =>
                !WebActionPolicy.OpenLink(text).Success);
        check("oversized direct URL is rejected", () =>
            WebActionPolicy.OpenLink("https://example.com/" + new string('x', 8192)).Status == "web-address-too-long");
    }
}
