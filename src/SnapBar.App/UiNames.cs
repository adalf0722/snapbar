using System.Globalization;
using SnapBar.Core;

namespace SnapBar.App;

internal static class UiNames
{
    private static readonly Dictionary<string, AiPrompt> OriginalPrompts = new AiSettings().Prompts.ToDictionary(p => p.Id);
    internal static string Action(ActionItem item, UiLanguage? language = null) => item.Custom is { } custom
        ? Action(custom, language) : Get("Action_" + item.Id, language);
    internal static string Action(CustomActionDefinition action, UiLanguage? language = null) => (action.Id, action.Name) switch
    {
        ("custom-62bf87b962054dc7bacd6588e37c2843", "搜尋 Gmail") => Get("Action_Gmail", language),
        ("custom-7a935c9cbd654f8aace39e311174cc72", "取得公網 IP") => Get("Action_PublicIp", language),
        _ => UserText(action.Name)
    };
    internal static string Prompt(AiPrompt prompt, UiLanguage? language = null) => OriginalPrompts.TryGetValue(prompt.Id, out var original)
        && prompt.Name == original.Name ? Get("Prompt_" + prompt.Id, language) : UserText(prompt.Name);
    internal static string Category(AiPrompt prompt, UiLanguage? language = null)
    {
        if (!OriginalPrompts.TryGetValue(prompt.Id, out var original) || prompt.Category != original.Category) return UserText(prompt.Category);
        var key = original.Id switch { "summary" => "Category_Reading", "translate-en" => "Category_Translation", _ => "Category_Writing" };
        return Get(key, language);
    }
    internal static string Provider(AiProviderProfile profile, UiLanguage? language = null) => profile.Id == "compatible" && profile.Name == "自訂相容服務"
        ? Get("Provider_Compatible", language) : UserText(profile.Name);
    internal static string KeepOriginalName(string input, string initialDisplay, string? original) => UserText(original != null && input == initialDisplay ? original : input);
    private static string UserText(string value) => new(value.AsSpan());
    internal static IReadOnlyList<ActionItem> More(ActionSettings settings, SelectionSnapshot selection, string query)
    {
        var rawMatches = ActionCatalog.GetMore(settings, selection, query).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return ActionCatalog.GetMore(settings, selection).Where(item => rawMatches.Contains(item.Id)
            || Action(item).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    internal static IReadOnlyList<AiPrompt> SearchPrompts(AiSettings settings, string query, string? category, bool recentFirst = false, UiLanguage? language = null)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var source = recentFirst ? AiPolicy.Search(settings, "") : AiPromptCatalog.Search(settings, "", category);
        return source.Where(prompt => (words.Length > 0 || !recentFirst || string.IsNullOrEmpty(category) || prompt.Category == category)
            && words.All(word => (Prompt(prompt, language) + "\n" + prompt.Name + "\n" + prompt.Instruction + "\n" + prompt.Keywords)
                .Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    private static string Get(string key, UiLanguage? language) => language?.Get(key) ?? UiText.Get(key);
}
