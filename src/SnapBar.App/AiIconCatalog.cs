using System.Text.Json;
using System.Windows;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed record AiIconInfo(string Id, string ResourceLabel, string ResourceCategory, string Keywords, int Codepoint = 0)
{
    internal System.Globalization.CultureInfo? Culture { get; init; }
    public string Label => UiText.Current(ResourceLabel, Culture);
    public string Category => UiText.Current(ResourceCategory, Culture);
    public string SearchText => Id + " " + Label + " " + Keywords;
}

internal static class AiIconCatalog
{
    internal const string FontResource = "pack://application:,,,/SnapBar.App;component/Assets/FluentIcons/FluentSystemIcons-Regular.ttf";
    private static readonly Lazy<Catalog> Data = new(Load);
    private sealed record Catalog(Dictionary<string, AiIconInfo> All, Dictionary<string, AiIconInfo> Preferred, AiIconInfo[] Browse);
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["text_edit_style"] = UiText.Get("AiIconCatalog_000"), ["text_align_left"] = UiText.Get("AiIconCatalog_001"), ["translate"] = UiText.Get("AiIconCatalog_002"), ["text_grammar_checkmark"] = UiText.Get("AiIconCatalog_003"),
        ["text_bullet_list"] = UiText.Get("AiIconCatalog_004"), ["text_bullet_list_square_sparkle"] = UiText.Get("AiIconCatalog_005"), ["text_grammar_wand"] = UiText.Get("AiIconCatalog_006"),
        ["wand"] = UiText.Get("AiIconCatalog_007"), ["sparkle"] = UiText.Get("AiIconCatalog_008"), ["document"] = UiText.Get("AiIconCatalog_009"), ["document_text"] = UiText.Get("AiIconCatalog_010"), ["book_open"] = UiText.Get("AiIconCatalog_011"),
        ["code"] = UiText.Get("AiIconCatalog_012"), ["code_block"] = UiText.Get("AiIconCatalog_013"), ["terminal"] = UiText.Get("AiIconCatalog_014"), ["braces"] = UiText.Get("AiIconCatalog_015"), ["search"] = UiText.Get("AiIconCatalog_016"),
        ["mail"] = UiText.Get("AiIconCatalog_017"), ["chat"] = UiText.Get("AiIconCatalog_018"), ["chat_sparkle"] = UiText.Get("AiIconCatalog_019"), ["comment"] = UiText.Get("AiIconCatalog_020"), ["question_circle"] = UiText.Get("AiIconCatalog_021"),
        ["lightbulb"] = UiText.Get("AiIconCatalog_022"), ["brain_circuit"] = UiText.Get("AiIconCatalog_023"), ["checkmark_circle"] = UiText.Get("AiIconCatalog_024"), ["edit"] = UiText.Get("AiIconCatalog_025"), ["pen"] = UiText.Get("AiIconCatalog_026"),
        ["clipboard_text"] = UiText.Get("AiIconCatalog_027"), ["copy"] = UiText.Get("AiIconCatalog_028"), ["cut"] = UiText.Get("AiIconCatalog_029"), ["clipboard_paste"] = UiText.Get("AiIconCatalog_030"), ["link"] = UiText.Get("AiIconCatalog_031"),
        ["folder"] = UiText.Get("AiIconCatalog_032"), ["globe"] = UiText.Get("AiIconCatalog_033"), ["calendar"] = UiText.Get("AiIconCatalog_034"), ["task_list_square_ltr"] = UiText.Get("AiIconCatalog_035"),
        ["table"] = UiText.Get("AiIconCatalog_036"), ["data_bar_vertical"] = UiText.Get("AiIconCatalog_037"), ["arrow_sort"] = UiText.Get("AiIconCatalog_038"), ["arrow_sync"] = UiText.Get("AiIconCatalog_039"),
        ["image"] = UiText.Get("AiIconCatalog_040"), ["camera"] = UiText.Get("AiIconCatalog_041"), ["paint_brush"] = UiText.Get("AiIconCatalog_042"), ["color"] = UiText.Get("AiIconCatalog_043"), ["music_note_1"] = UiText.Get("AiIconCatalog_044"),
        ["mic"] = UiText.Get("AiIconCatalog_045"), ["video"] = UiText.Get("AiIconCatalog_046"), ["people"] = UiText.Get("AiIconCatalog_047"), ["person"] = UiText.Get("AiIconCatalog_048"), ["star"] = UiText.Get("AiIconCatalog_049"), ["heart"] = UiText.Get("AiIconCatalog_050"),
        ["settings"] = UiText.Get("AiIconCatalog_051"), ["shield_checkmark"] = UiText.Get("AiIconCatalog_052"), ["warning"] = UiText.Get("AiIconCatalog_053"), ["clock"] = UiText.Get("AiIconCatalog_054"), ["flag"] = UiText.Get("AiIconCatalog_055")
    };
    private static readonly AiIconInfo[] Legacy = ActionUi.Icons.Select((id, n) => new AiIconInfo("legacy:" + id,
        new[] { UiText.Get("AiIconCatalog_056"), UiText.Get("AiIconCatalog_057"), UiText.Get("AiIconCatalog_058"), UiText.Get("AiIconCatalog_059"), UiText.Get("AiIconCatalog_060"), UiText.Get("AiIconCatalog_061"), UiText.Get("AiIconCatalog_062"), UiText.Get("AiIconCatalog_063"), UiText.Get("AiIconCatalog_064"), UiText.Get("AiIconCatalog_065"), UiText.Get("AiIconCatalog_066"), UiText.Get("AiIconCatalog_067"), UiText.Get("AiIconCatalog_068") }[n], UiText.Get("AiIconCatalog_069"), id)).ToArray();
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["improve"] = "text_edit_style_20_regular", ["shorten"] = "text_align_left_20_regular", ["translate-en"] = "translate_20_regular",
        ["grammar"] = "text_grammar_checkmark_20_regular", ["summary"] = "text_bullet_list_20_regular"
    };
    private static readonly Dictionary<string, AiPrompt> Factory = new AiSettings().Prompts.ToDictionary(p => p.Id);
    internal static IReadOnlyList<AiIconInfo> Browse => Data.Value.Browse;
    internal static int VariantCount => Data.Value.All.Count;
    internal static string ForPrompt(AiPrompt prompt) => Factory.TryGetValue(prompt.Id, out var original) && prompt == original ? Defaults[prompt.Id] : prompt.Icon;
    internal static AiIconInfo? Stored(string id)
    {
        var legacy = Legacy.FirstOrDefault(i => i.Id == id || i.Id == "legacy:" + id);
        return legacy ?? (TryResolve(id, out var resolved) ? resolved : null);
    }
    internal static bool TryResolve(string value, out AiIconInfo info)
    {
        var id = value.Trim().ToLowerInvariant();
        if (id.EndsWith(".svg", StringComparison.Ordinal)) id = id[..^4];
        if (id.StartsWith("ic_fluent_", StringComparison.Ordinal)) id = id[10..];
        if (Data.Value.All.TryGetValue(id, out info!) || Data.Value.Preferred.TryGetValue(id, out info!)) return true;
        info = Legacy.FirstOrDefault(i => i.Id == id || i.Id == "legacy:" + id)!; return info != null;
    }
    internal static IReadOnlyList<AiIconInfo> Search(string query, string category, UiLanguage? language = null)
    {
        string Text(string key) => language?.Get(key) ?? UiText.Get(key);
        var entries = Browse.Select(icon => icon with { Culture = language?.Culture }).ToArray();
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && TryResolve(query, out var exact))
            return new[] { exact with { Culture = language?.Culture } }.Concat(entries.Where(i => i.Id != exact.Id && words.All(w => i.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase)))).ToArray();
        return entries.Where(i => words.Length > 0 ? words.All(w => i.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase))
            : category == Text("AiIconCatalog_070") || (category == Text("AiIconCatalog_071") ? Labels.Keys.Any(k => Data.Value.Preferred.GetValueOrDefault(k)?.Id == i.Id) : i.Category == category)).ToArray();
    }
    private static Catalog Load()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/SnapBar.App;component/Assets/FluentIcons/catalog.json"))!;
        using var stream = resource.Stream;
        var map = JsonSerializer.Deserialize<Dictionary<string, int>>(stream)!;
        var all = new Dictionary<string, AiIconInfo>(StringComparer.Ordinal); var preferred = new Dictionary<string, AiIconInfo>(StringComparer.Ordinal);
        foreach (var (id, point) in map)
        {
            var tail = id.LastIndexOf('_', id.Length - "_regular".Length - 1); var name = id[..tail];
            var category = Category(name); var keywords = name.Replace('_', ' ') + " " + category;
            if (name.StartsWith("text_", StringComparison.Ordinal)) keywords += UiText.Get("AiIconCatalog_072");
            if (name.Contains("checkmark", StringComparison.Ordinal)) keywords += UiText.Get("AiIconCatalog_073");
            if (name.Contains("sparkle", StringComparison.Ordinal) || name.Contains("wand", StringComparison.Ordinal)) keywords += UiText.Get("AiIconCatalog_074");
            var info = new AiIconInfo(id, Labels.GetValueOrDefault(name, name.Replace('_', ' ')), category, keywords, point); all[id] = info;
            if (!preferred.TryGetValue(name, out var old) || Rank(id) < Rank(old.Id)) preferred[name] = info;
        }
        var leading = Defaults.Values.Select((id, n) => (id, n)).ToDictionary(pair => pair.id, pair => pair.n, StringComparer.Ordinal);
        var browse = preferred.Values.OrderBy(i => leading.GetValueOrDefault(i.Id, Labels.Keys.Any(key => preferred.GetValueOrDefault(key)?.Id == i.Id) ? 5 : 6))
            .ThenBy(i => i.Id, StringComparer.Ordinal).Concat(Legacy).ToArray();
        return new(all, preferred, browse);
    }
    private static int Rank(string id) => id.EndsWith("_20_regular", StringComparison.Ordinal) ? 0 : id.EndsWith("_24_regular", StringComparison.Ordinal) ? 1 : 2;
    private static string Category(string name) => name.StartsWith("text_", StringComparison.Ordinal) || name is "edit" or "pen" or "wand" ? UiText.Get("AiIconCatalog_075")
        : name.Contains("translate", StringComparison.Ordinal) || name.StartsWith("local_language", StringComparison.Ordinal) ? UiText.Get("AiIconCatalog_076")
        : name.StartsWith("book", StringComparison.Ordinal) || name.StartsWith("reading", StringComparison.Ordinal) ? UiText.Get("AiIconCatalog_077")
        : name.StartsWith("code", StringComparison.Ordinal) || name.StartsWith("braces", StringComparison.Ordinal) || name.StartsWith("terminal", StringComparison.Ordinal) ? UiText.Get("AiIconCatalog_078")
        : name.StartsWith("mail", StringComparison.Ordinal) || name.StartsWith("chat", StringComparison.Ordinal) || name.StartsWith("comment", StringComparison.Ordinal) ? UiText.Get("AiIconCatalog_079")
        : name.StartsWith("document", StringComparison.Ordinal) || name.StartsWith("calendar", StringComparison.Ordinal) || name.StartsWith("task", StringComparison.Ordinal) || name.StartsWith("clipboard", StringComparison.Ordinal) ? UiText.Get("AiIconCatalog_080")
        : name.StartsWith("image", StringComparison.Ordinal) || name.StartsWith("camera", StringComparison.Ordinal) || name.StartsWith("paint", StringComparison.Ordinal) || name.StartsWith("video", StringComparison.Ordinal) ? UiText.Get("AiIconCatalog_081") : UiText.Get("AiIconCatalog_082");
}
