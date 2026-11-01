using System.Text.Json;
using System.Windows;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed record AiIconInfo(string Id, string Label, string Category, string Keywords, int Codepoint = 0)
{
    public string SearchText => Id + " " + Label + " " + Keywords;
}

internal static class AiIconCatalog
{
    internal const string FontResource = "pack://application:,,,/SnapBar.App;component/Assets/FluentIcons/FluentSystemIcons-Regular.ttf";
    private static readonly Lazy<Catalog> Data = new(Load);
    private sealed record Catalog(Dictionary<string, AiIconInfo> All, Dictionary<string, AiIconInfo> Preferred, AiIconInfo[] Browse);
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["text_edit_style"] = "改善寫作", ["text_align_left"] = "縮短文字", ["translate"] = "翻譯", ["text_grammar_checkmark"] = "文法拼字檢查",
        ["text_bullet_list"] = "重點清單／摘要", ["text_bullet_list_square_sparkle"] = "智慧摘要", ["text_grammar_wand"] = "潤飾文字",
        ["wand"] = "魔法棒", ["sparkle"] = "星光", ["document"] = "文件", ["document_text"] = "文字文件", ["book_open"] = "閱讀",
        ["code"] = "程式碼", ["code_block"] = "程式碼區塊", ["terminal"] = "終端機", ["braces"] = "大括號", ["search"] = "搜尋",
        ["mail"] = "郵件", ["chat"] = "對話", ["chat_sparkle"] = "智慧對話", ["comment"] = "註解", ["question_circle"] = "提問",
        ["lightbulb"] = "靈感", ["brain_circuit"] = "思考分析", ["checkmark_circle"] = "檢查完成", ["edit"] = "編輯", ["pen"] = "書寫",
        ["clipboard_text"] = "剪貼簿文字", ["copy"] = "複製", ["cut"] = "剪下", ["clipboard_paste"] = "貼上", ["link"] = "連結",
        ["folder"] = "資料夾", ["globe"] = "網頁／語言", ["calendar"] = "行事曆", ["task_list_square_ltr"] = "待辦清單",
        ["table"] = "表格", ["data_bar_vertical"] = "長條圖", ["arrow_sort"] = "排序", ["arrow_sync"] = "同步轉換",
        ["image"] = "圖片", ["camera"] = "相機", ["paint_brush"] = "畫筆", ["color"] = "色彩", ["music_note_1"] = "音樂",
        ["mic"] = "語音", ["video"] = "影片", ["people"] = "人員", ["person"] = "個人", ["star"] = "星號", ["heart"] = "愛心",
        ["settings"] = "設定", ["shield_checkmark"] = "安全檢查", ["warning"] = "提醒", ["clock"] = "時間", ["flag"] = "旗標"
    };
    private static readonly AiIconInfo[] Legacy = ActionUi.Icons.Select((id, n) => new AiIconInfo("legacy:" + id,
        new[] { "複製", "剪下", "貼上", "搜尋", "連結", "資料夾", "網頁", "翻譯", "程式碼", "終端機", "星芒", "文件", "清理文字" }[n], "原有圖示", id)).ToArray();
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
    internal static IReadOnlyList<AiIconInfo> Search(string query, string category)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && TryResolve(query, out var exact))
            return new[] { exact }.Concat(Browse.Where(i => i.Id != exact.Id && words.All(w => i.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase)))).ToArray();
        return Browse.Where(i => words.Length > 0 ? words.All(w => i.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase))
            : category == "全部圖示" || (category == "常用" ? Labels.Keys.Any(k => Data.Value.Preferred.GetValueOrDefault(k)?.Id == i.Id) : i.Category == category)).ToArray();
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
            if (name.StartsWith("text_", StringComparison.Ordinal)) keywords += " 文字 寫作 格式";
            if (name.Contains("checkmark", StringComparison.Ordinal)) keywords += " 勾選 檢查 校正";
            if (name.Contains("sparkle", StringComparison.Ordinal) || name.Contains("wand", StringComparison.Ordinal)) keywords += " 智慧 AI 魔法 改善 潤飾";
            var info = new AiIconInfo(id, Labels.GetValueOrDefault(name, name.Replace('_', ' ')), category, keywords, point); all[id] = info;
            if (!preferred.TryGetValue(name, out var old) || Rank(id) < Rank(old.Id)) preferred[name] = info;
        }
        var leading = Defaults.Values.Select((id, n) => (id, n)).ToDictionary(pair => pair.id, pair => pair.n, StringComparer.Ordinal);
        var browse = preferred.Values.OrderBy(i => leading.GetValueOrDefault(i.Id, Labels.Values.Contains(i.Label) ? 5 : 6))
            .ThenBy(i => i.Id, StringComparer.Ordinal).Concat(Legacy).ToArray();
        return new(all, preferred, browse);
    }
    private static int Rank(string id) => id.EndsWith("_20_regular", StringComparison.Ordinal) ? 0 : id.EndsWith("_24_regular", StringComparison.Ordinal) ? 1 : 2;
    private static string Category(string name) => name.StartsWith("text_", StringComparison.Ordinal) || name is "edit" or "pen" or "wand" ? "寫作"
        : name.Contains("translate", StringComparison.Ordinal) || name.StartsWith("local_language", StringComparison.Ordinal) ? "翻譯"
        : name.StartsWith("book", StringComparison.Ordinal) || name.StartsWith("reading", StringComparison.Ordinal) ? "閱讀"
        : name.StartsWith("code", StringComparison.Ordinal) || name.StartsWith("braces", StringComparison.Ordinal) || name.StartsWith("terminal", StringComparison.Ordinal) ? "程式"
        : name.StartsWith("mail", StringComparison.Ordinal) || name.StartsWith("chat", StringComparison.Ordinal) || name.StartsWith("comment", StringComparison.Ordinal) ? "溝通"
        : name.StartsWith("document", StringComparison.Ordinal) || name.StartsWith("calendar", StringComparison.Ordinal) || name.StartsWith("task", StringComparison.Ordinal) || name.StartsWith("clipboard", StringComparison.Ordinal) ? "工作"
        : name.StartsWith("image", StringComparison.Ordinal) || name.StartsWith("camera", StringComparison.Ordinal) || name.StartsWith("paint", StringComparison.Ordinal) || name.StartsWith("video", StringComparison.Ordinal) ? "影像" : "其他";
}
