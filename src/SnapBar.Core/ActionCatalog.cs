namespace SnapBar.Core;

public static class ActionCatalog
{
    private static readonly ActionItem[] BuiltIns = [
        new("copy", "複製", "copy", TextAction.Copy), new("cut", "剪下", "cut", TextAction.Cut),
        new("paste", "貼上", "paste", TextAction.Paste), new("search", "搜尋", "search", TextAction.Search),
        new("link", "開啟連結", "link", TextAction.OpenLink), new("path", "檔案位置", "folder", TextAction.RevealPath)];

    public static ActionSettings Normalize(ActionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.CustomActions == null || settings.Layout == null || settings.RecentIds == null
            || settings.CustomActions.Length > 32 || settings.Theme is not ("Dark" or "Light")) throw new ArgumentException("動作設定無效。");
        var customs = settings.CustomActions.Select(action =>
        {
            if (CustomActionPolicy.Validate(action!) is { } error) throw new ArgumentException(error);
            return action with { Icon = CustomActionPolicy.Icons.Contains(action.Icon ?? "") ? action.Icon! : "spark" };
        }).ToArray();
        if (customs.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != customs.Length) throw new ArgumentException("動作識別碼重複。");
        var ids = BuiltIns.Select(a => a.Id).Concat(customs.Select(a => a.Id)).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var layout = new List<ActionLayoutEntry>();
        foreach (var entry in settings.Layout)
        {
            if (entry == null || entry.Id == null || !ids.Contains(entry.Id) || !seen.Add(entry.Id) || !Enum.IsDefined(entry.Placement)) throw new ArgumentException("動作排列無效。");
            layout.Add(entry);
        }
        foreach (var id in BuiltIns.Select(a => a.Id).Concat(customs.Select(a => a.Id)))
            if (seen.Add(id)) layout.Add(new(id, id is "copy" or "cut" or "paste" or "search" ? ActionPlacement.Bar : ActionPlacement.More));
        if (layout.Count(e => e.Placement == ActionPlacement.Bar) > 6) throw new ArgumentException("工具列最多可放六個動作。");
        if (settings.RecentIds.Any(id => id == null)) throw new ArgumentException("最近動作無效。");
        var visible = layout.Where(e => e.Placement != ActionPlacement.Hidden).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        return settings with { CustomActions = customs, Layout = layout.ToArray(), RecentIds = settings.RecentIds.Where(visible.Contains).Distinct(StringComparer.Ordinal).Take(5).ToArray() };
    }

    private static ActionItem[] Items(ActionSettings settings) => BuiltIns.Concat(settings.CustomActions.Select(a => new ActionItem(a.Id, a.Name, a.Icon, Custom: a))).ToArray();
    public static IReadOnlyList<ActionItem> GetItems(ActionSettings settings) => Items(Normalize(settings));

    public static IReadOnlyList<ActionItem> GetPinned(ActionSettings settings)
    {
        settings = Normalize(settings);
        var items = Items(settings).ToDictionary(a => a.Id);
        return settings.Layout.Where(e => e.Placement == ActionPlacement.Bar).Select(e => items[e.Id]).ToArray();
    }

    public static bool IsApplicable(ActionItem item, SelectionSnapshot selection) => item.BuiltIn switch
    {
        TextAction.Cut or TextAction.Paste => SessionPolicy.CanWrite(selection, selection.Target),
        TextAction.OpenLink => WebActionPolicy.OpenLink(selection.Text).Success,
        TextAction.RevealPath => PathActionPolicy.Resolve(selection.Text).Success,
        _ => !string.IsNullOrWhiteSpace(selection.Text) && (item.Custom is not { Kind: not CustomActionKind.Url, Output: ActionOutput.ReplaceSelection }
            || SessionPolicy.CanWrite(selection, selection.Target))
    };

    public static IReadOnlyList<ActionItem> GetBar(ActionSettings settings, SelectionSnapshot selection)
    {
        settings = Normalize(settings);
        var items = Items(settings).ToDictionary(a => a.Id);
        var result = settings.Layout.Where(e => e.Placement == ActionPlacement.Bar).Select(e => items[e.Id]).Where(a => IsApplicable(a, selection)).ToList();
        foreach (var entry in settings.Layout.Where(e => e.Placement == ActionPlacement.More && e.Id is "link" or "path"))
            if (result.Count < 6 && IsApplicable(items[entry.Id], selection)) result.Add(items[entry.Id]);
        return result;
    }

    public static IReadOnlyList<ActionItem> GetMore(ActionSettings settings, SelectionSnapshot selection, string query = "")
    {
        settings = Normalize(settings);
        var bar = GetBar(settings, selection).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var visible = settings.Layout.Where(e => e.Placement != ActionPlacement.Hidden).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        query = (query ?? "").Trim();
        return Items(settings).Where(a => visible.Contains(a.Id) && !bar.Contains(a.Id) && IsApplicable(a, selection))
            .Where(a => (a.Name + " " + a.Id + " " + Aliases(a)).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => { int index = Array.IndexOf(settings.RecentIds, a.Id); return index < 0 ? int.MaxValue : index; })
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Aliases(ActionItem item) => item.Custom?.Kind switch
    {
        CustomActionKind.Url => "url web 網址 網頁", CustomActionKind.PowerShell => "powershell ps 腳本 指令",
        CustomActionKind.JavaScript => "javascript js 腳本", _ => item.BuiltIn?.ToString() ?? ""
    };

    public static ActionSettings SetPlacement(ActionSettings settings, string id, ActionPlacement placement)
    {
        settings = Normalize(settings);
        if (!Enum.IsDefined(placement) || !settings.Layout.Any(e => e.Id == id)) throw new ArgumentException("動作排列無效。");
        var current = settings.Layout.Single(e => e.Id == id);
        if (current.Placement == placement) return settings;
        var layout = settings.Layout.Where(e => e.Id != id).ToList();
        var changed = current with { Placement = placement };
        if (placement == ActionPlacement.Bar)
            layout.Insert(layout.FindLastIndex(e => e.Placement == ActionPlacement.Bar) + 1, changed);
        else layout.Add(changed);
        return Normalize(settings with { Layout = layout.ToArray() });
    }

    public static ActionSettings Move(ActionSettings settings, string id, int delta)
    {
        settings = Normalize(settings);
        var layout = settings.Layout.ToArray();
        var indices = Enumerable.Range(0, layout.Length).Where(i => layout[i].Placement == ActionPlacement.Bar).ToArray();
        int current = Array.FindIndex(indices, i => layout[i].Id == id);
        if (current < 0) return settings;
        int destination = (int)Math.Clamp((long)current + delta, 0, indices.Length - 1);
        var pinned = indices.Select(i => layout[i]).ToList();
        var moving = pinned[current]; pinned.RemoveAt(current); pinned.Insert(destination, moving);
        for (int i = 0; i < indices.Length; i++) layout[indices[i]] = pinned[i];
        return settings with { Layout = layout };
    }

    public static ActionSettings Remember(ActionSettings settings, string id)
    {
        settings = Normalize(settings);
        return Normalize(settings with { RecentIds = new[] { id }.Concat(settings.RecentIds).ToArray() });
    }
}
