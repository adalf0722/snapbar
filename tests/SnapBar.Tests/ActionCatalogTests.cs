using System.Text;
using SnapBar.Core;

internal static class ActionCatalogTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        var action = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "翻譯", CustomActionKind.Url, "translate", "https://example.com/?q=***");
        var target = new TargetIdentity(1, 2, 3);
        var selection = new SelectionSnapshot("s", target, "hello", null, true, 1, 5);
        var settings = new ActionSettings { CustomActions = [action] };
        bool Reject(Action test) { try { test(); return false; } catch (ArgumentException) { return true; } }
        check("action defaults pin four builtins and keep custom actions in More", () =>
            ActionCatalog.GetBar(settings, selection).Select(a => a.Id).SequenceEqual(new[] { "copy", "cut", "paste", "search" })
            && ActionCatalog.GetMore(settings, selection).Single().Id == action.Id);
        check("link contextual promotion excludes duplicated More item", () =>
            ActionCatalog.GetBar(settings, selection with { Text = "https://example.com/" }).Any(a => a.Id == "link")
            && !ActionCatalog.GetMore(settings, selection with { Text = "https://example.com/" }).Any(a => a.Id == "link"));
        check("hidden contextual action cannot be promoted", () =>
            !ActionCatalog.GetBar(ActionCatalog.SetPlacement(settings, "link", ActionPlacement.Hidden), selection with { Text = "https://example.com/" }).Any(a => a.Id == "link"));
        check("read only selection excludes built in and custom write actions", () =>
        {
            var readOnly = selection with { Editable = null };
            return !ActionCatalog.GetBar(settings, readOnly).Any(a => a.Id is "cut" or "paste")
                && !ActionCatalog.IsApplicable(new(action.Id, action.Name, action.Icon, Custom: action with { Output = ActionOutput.ReplaceSelection }), readOnly);
        });
        check("pin cap rejects seventh pinned item", () =>
        {
            var six = ActionCatalog.SetPlacement(ActionCatalog.SetPlacement(settings, "link", ActionPlacement.Bar), "path", ActionPlacement.Bar);
            return Reject(() => ActionCatalog.SetPlacement(six, action.Id, ActionPlacement.Bar));
        });
        check("move reorders pinned actions while preserving More slots", () =>
        {
            var normalized = ActionCatalog.Normalize(settings);
            var moved = ActionCatalog.Move(normalized, "copy", 3);
            return moved.Layout.Take(4).Select(e => e.Id).SequenceEqual(new[] { "cut", "paste", "search", "copy" })
                && moved.Layout.Skip(4).SequenceEqual(normalized.Layout.Skip(4));
        });
        check("recent ordering wins over name and hidden recents are removed", () =>
        {
            var other = action with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "AAA" };
            var recent = ActionCatalog.Remember(new() { CustomActions = [action, other] }, action.Id);
            return ActionCatalog.GetMore(recent, selection)[0].Id == action.Id
                && ActionCatalog.SetPlacement(recent, action.Id, ActionPlacement.Hidden).RecentIds.Length == 0;
        });
        check("recents keep five distinct known visible actions", () =>
            ActionCatalog.Normalize(settings with { RecentIds = ["missing", "copy", "copy", "cut", "paste", "search", "link", "path"] }).RecentIds.SequenceEqual(new[] { "copy", "cut", "paste", "search", "link" }));
        check("More search matches Chinese names and URL aliases", () =>
            ActionCatalog.GetMore(settings, selection, "翻譯").Count == 1 && ActionCatalog.GetMore(settings, selection, "URL").Count == 1);
        check("unknown action icons normalize to spark", () => ActionCatalog.Normalize(settings with { CustomActions = [action with { Icon = "unknown" }] }).CustomActions[0].Icon == "spark");
        check("timeout upper boundary accepts 300 seconds", () => CustomActionPolicy.Validate(action with { TimeoutSeconds = 300 }) == null);
        int invalidCase = 0;
        foreach (var invalid in new ActionSettings[] {
            settings with { CustomActions = null! }, settings with { Layout = null! }, settings with { RecentIds = null! },
            settings with { Theme = "dark" }, settings with { CustomActions = [null!] },
            settings with { CustomActions = [action, action] }, settings with { CustomActions = [action with { Id = "copy" }] },
            settings with { Layout = [new("missing", ActionPlacement.More)] },
            settings with { Layout = [new("copy", ActionPlacement.More), new("copy", ActionPlacement.Hidden)] },
            settings with { Layout = [new("copy", (ActionPlacement)99)] }, settings with { RecentIds = [null!] },
            settings with { CustomActions = [action with { Kind = (CustomActionKind)99 }] },
            settings with { CustomActions = [action with { Output = (ActionOutput)99 }] },
            settings with { CustomActions = [action with { TimeoutSeconds = 0 }] },
            settings with { CustomActions = [action with { TimeoutSeconds = 301 }] },
            settings with { CustomActions = [action with { Name = "bad\nname" }] },
            settings with { CustomActions = [action with { Content = new string('x', 32769) }] },
            settings with { CustomActions = Enumerable.Range(0, 33).Select(_ => action with { Id = "custom-" + Guid.NewGuid().ToString("N") }).ToArray() } })
            check("invalid action settings rejected " + ++invalidCase, () => Reject(() => ActionCatalog.Normalize(invalid)));
        foreach (var template in new[] { "https://***.example.com/", "https://example.com:***/", "https://user:pass@example.com/***", "file:///tmp/***", "https://example.com/", "https://example.com/***/***", "https://example.com\\@evil.test/***", "https://example.com/ ***", "https://example.com/%xy/***" })
            check("adversarial custom URL rejected " + template, () => CustomActionPolicy.Validate(action with { Content = template }) != null && !CustomActionPolicy.ResolveUrl("hello", template).Success);
        foreach (var template in new[] { "https://example.com/***", "https://example.com?***", "https://example.com#***" })
            check("custom URL encodes selection outside authority " + template, () =>
            {
                var destination = CustomActionPolicy.ResolveUrl("//evil.test/@a?b#c&d=中文", template);
                return destination.Success && destination.Uri!.Host == "example.com" && destination.Uri.AbsoluteUri.Contains("%2F%2Fevil.test", StringComparison.OrdinalIgnoreCase);
            });
        check("custom URL rejects invalid UTF16 input", () => !CustomActionPolicy.ResolveUrl("\ud800", action.Content).Success);
        check("import roundtrip creates fresh IDs without changing executable content", () =>
        {
            var script = action with { Kind = CustomActionKind.PowerShell, Content = "Write-Output $text", InterpreterPath = @"C:\Tools\pwsh.exe" };
            var imported = CustomActionPolicy.Import(CustomActionPolicy.Export(script));
            return imported.Id != script.Id && imported with { Id = script.Id } == script;
        });
        check("import rejects duplicate case-insensitive envelope properties", () => Reject(() => CustomActionPolicy.Import(CustomActionPolicy.Export(action).Replace("\"version\": 1", "\"version\": 1, \"Version\": 1"))));
        check("import rejects duplicate action properties", () => Reject(() => CustomActionPolicy.Import(CustomActionPolicy.Export(action).Replace("\"name\":", "\"name\": \"other\", \"name\":"))));
        check("import rejects future version", () => Reject(() => CustomActionPolicy.Import(CustomActionPolicy.Export(action).Replace("\"version\": 1", "\"version\": 2"))));
        check("import rejects oversized UTF8 input", () => Reject(() => CustomActionPolicy.Import(new string('中', 90000))));
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Actions.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            foreach (var version in new[] { 1, 2, 3, 4 })
                check("action settings migration from v" + version, () =>
                {
                    File.WriteAllText(path, "{\"schemaVersion\":" + version + "}", new UTF8Encoding(false));
                    var before = File.ReadAllBytes(path);
                    var loaded = new SettingsStore(path).Load();
                    return loaded.Warning == null && loaded.Settings.SchemaVersion == 4 && loaded.Settings.Actions.Layout.Length == 6 && before.SequenceEqual(File.ReadAllBytes(path));
                });
            check("action settings roundtrip preserves layout theme labels and recents", () =>
            {
                var store = new SettingsStore(path); var loaded = store.Load();
                var actions = ActionCatalog.Remember(ActionCatalog.SetPlacement(settings, "cut", ActionPlacement.Hidden), action.Id) with { Theme = "Light", ShowLabels = false };
                store.Save(loaded.Settings with { Actions = actions });
                var roundtrip = new SettingsStore(path).Load();
                return roundtrip.Warning == null && roundtrip.Settings.Actions.CustomActions.SequenceEqual(actions.CustomActions)
                    && roundtrip.Settings.Actions.Layout.SequenceEqual(actions.Layout) && roundtrip.Settings.Actions.RecentIds.SequenceEqual(actions.RecentIds)
                    && !roundtrip.Settings.Actions.ShowLabels && roundtrip.Settings.Actions.Theme == "Light";
            });
            foreach (var invalid in new[] { "null", "{\"customActions\":null}", "{\"layout\":[{\"id\":\"missing\",\"placement\":0}]}" })
                check("invalid stored actions preserve source " + invalid, () =>
                {
                    File.WriteAllText(path, "{\"schemaVersion\":4,\"actions\":" + invalid + "}", new UTF8Encoding(false));
                    var before = File.ReadAllBytes(path); var store = new SettingsStore(path);
                    if (store.Load().Warning != "settings-invalid") return false;
                    try { store.Save(new()); return false; }
                    catch (InvalidOperationException) { return before.SequenceEqual(File.ReadAllBytes(path)); }
                });
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
