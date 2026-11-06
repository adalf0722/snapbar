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
        ActionSettings PinFiveMore(ActionSettings value) => ActionCatalog.GetMore(value, selection)
            .Where(item => item.Id != action.Id).Take(5)
            .Aggregate(value, (layout, item) => ActionCatalog.SetPlacement(layout, item.Id, ActionPlacement.Bar));
        check("action defaults pin four builtins and keep custom actions in More", () =>
            ActionCatalog.GetBar(settings, selection).Select(a => a.Id).SequenceEqual(new[] { "copy", "cut", "paste", "search" })
            && ActionCatalog.GetMore(settings, selection).Any(a => a.Id == action.Id));
        check("More placement is respected for valid links without automatic promotion", () =>
            !ActionCatalog.GetBar(settings, selection with { Text = "https://example.com/" }).Any(a => a.Id == "link")
            && ActionCatalog.GetMore(settings, selection with { Text = "https://example.com/" }).Any(a => a.Id == "link"));
        check("configured path remains discoverable in More even for nonpath text", () =>
            ActionCatalog.GetMore(new(), selection).Any(a => a.Id == "path")
            && !ActionCatalog.IsApplicable(ActionCatalog.GetMore(new(), selection).Single(a => a.Id == "path"), selection));
        check("valid paths stay in More and remain executable there", () =>
        {
            var pathSelection = selection with { Text = @"D:\Documents\example.txt" };
            return !ActionCatalog.GetBar(new(), pathSelection).Any(a => a.Id == "path")
                && ActionCatalog.IsApplicable(ActionCatalog.GetMore(new(), pathSelection).Single(a => a.Id == "path"), pathSelection);
        });
        check("unavailable More actions can be searched while hidden actions stay excluded", () =>
        {
            var hidden = ActionCatalog.SetPlacement(new(), "path", ActionPlacement.Hidden);
            return ActionCatalog.GetMore(new(), selection, "PATH").Single().Id == "path"
                && !ActionCatalog.GetMore(hidden, selection).Any(a => a.Id == "path");
        });
        check("inapplicable pinned actions do not silently move into More", () =>
            !ActionCatalog.GetMore(new(), selection with { Editable = false }).Any(a => a.Id is "cut" or "paste"));
        check("hidden contextual action cannot be promoted", () =>
            !ActionCatalog.GetBar(ActionCatalog.SetPlacement(settings, "link", ActionPlacement.Hidden), selection with { Text = "https://example.com/" }).Any(a => a.Id == "link"));
        check("read only selection excludes built in and custom write actions", () =>
        {
            var readOnly = selection with { Editable = null };
            return !ActionCatalog.GetBar(settings, readOnly).Any(a => a.Id is "cut" or "paste")
                && !ActionCatalog.IsApplicable(new(action.Id, action.Name, action.Icon, Custom: action with { Kind = CustomActionKind.PowerShell, Output = ActionOutput.ReplaceSelection }), readOnly);
        });
        check("URL action remains readable after switching from replace-output script", () =>
            ActionCatalog.IsApplicable(new(action.Id, action.Name, action.Icon, Custom: action with { Output = ActionOutput.ReplaceSelection }), selection with { Editable = false }));
        check("pin cap accepts nine in both display modes and rejects a tenth", () =>
        {
            var nine = PinFiveMore(settings);
            var pinned = ActionCatalog.GetPinned(nine);
            return pinned.Count == 9 && pinned.SequenceEqual(ActionCatalog.GetPinned(nine with { ShowLabels = false }))
                && Reject(() => ActionCatalog.SetPlacement(nine, action.Id, ActionPlacement.Bar));
        });
        check("pinning sixth action appends after existing fifth instead of using its old More slot", () =>
        {
            var five = ActionCatalog.SetPlacement(settings, action.Id, ActionPlacement.Bar);
            var six = ActionCatalog.SetPlacement(five, "link", ActionPlacement.Bar);
            return ActionCatalog.GetPinned(six).Select(a => a.Id).SequenceEqual(new[] { "copy", "cut", "paste", "search", action.Id, "link" });
        });
        check("configuration preview retains all six conditional actions in the configured order", () =>
        {
            var six = ActionCatalog.SetPlacement(ActionCatalog.SetPlacement(new(), "link", ActionPlacement.Bar), "path", ActionPlacement.Bar);
            six = ActionCatalog.Move(six, "path", -5);
            return ActionCatalog.GetPinned(six).Select(a => a.Id).SequenceEqual(new[] { "path", "copy", "cut", "paste", "search", "link" })
                && ActionCatalog.GetBar(six, selection).Select(a => a.Id).SequenceEqual(new[] { "copy", "cut", "paste", "search" });
        });
        check("sixth custom action can move to either end and re-pin appends it", () =>
        {
            var second = action with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "第六個" };
            var six = ActionCatalog.SetPlacement(ActionCatalog.SetPlacement(new() { CustomActions = [action, second] }, action.Id, ActionPlacement.Bar), second.Id, ActionPlacement.Bar);
            var first = ActionCatalog.Move(six, second.Id, -5);
            var last = ActionCatalog.Move(first, second.Id, 5);
            var rePinned = ActionCatalog.SetPlacement(ActionCatalog.SetPlacement(first, second.Id, ActionPlacement.More), second.Id, ActionPlacement.Bar);
            return ActionCatalog.GetPinned(first)[0].Id == second.Id && ActionCatalog.GetPinned(last)[^1].Id == second.Id
                && ActionCatalog.GetPinned(rePinned).Select(a => a.Id).SequenceEqual(ActionCatalog.GetPinned(six).Select(a => a.Id));
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
        check("restoring a deleted action preserves subsequent edits additions order and recents", () =>
        {
            var original = ActionCatalog.Remember(ActionCatalog.SetPlacement(settings, action.Id, ActionPlacement.Bar), action.Id);
            var remaining = ActionCatalog.DeleteCustom(original, action.Id, out var deleted);
            var added = action with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "新增動作" };
            var edited = ActionCatalog.Remember(ActionCatalog.Move(remaining, "copy", 3), "search") with
                { CustomActions = [added], Theme = "Light", ShowLabels = false };
            edited = ActionCatalog.Normalize(edited);
            var restored = ActionCatalog.RestoreCustom(edited, deleted);
            return restored.Theme == "Light" && !restored.ShowLabels && restored.CustomActions.Contains(added)
                && restored.CustomActions.Contains(action) && restored.RecentIds.SequenceEqual(edited.RecentIds)
                && restored.Layout.Where(e => e.Id != action.Id).SequenceEqual(edited.Layout)
                && ActionCatalog.GetPinned(restored)[4].Id == action.Id;
        });
        check("restoring a deleted pinned action uses More when later edits fill the bar", () =>
        {
            var original = ActionCatalog.SetPlacement(settings, action.Id, ActionPlacement.Bar);
            var remaining = ActionCatalog.DeleteCustom(original, action.Id, out var deleted);
            remaining = PinFiveMore(remaining);
            var restored = ActionCatalog.RestoreCustom(remaining, deleted);
            return ActionCatalog.GetPinned(restored).Count == 9
                && ActionCatalog.GetPinned(restored).SequenceEqual(ActionCatalog.GetPinned(remaining))
                && restored.Layout.Single(e => e.Id == action.Id).Placement == ActionPlacement.More;
        });
        check("restore retains hidden placement and does not resurrect removed recents", () =>
        {
            var original = ActionCatalog.SetPlacement(settings, action.Id, ActionPlacement.Hidden);
            var remaining = ActionCatalog.DeleteCustom(original, action.Id, out var deleted);
            var restored = ActionCatalog.RestoreCustom(remaining, deleted);
            return restored.Layout.SequenceEqual(original.Layout) && restored.RecentIds.Length == 0;
        });
        check("restore refuses duplicate identities and full custom capacity without changing current actions", () =>
        {
            var remaining = ActionCatalog.DeleteCustom(settings, action.Id, out var deleted);
            var full = ActionCatalog.Normalize(remaining with { CustomActions = Enumerable.Range(0, 32)
                .Select(_ => action with { Id = "custom-" + Guid.NewGuid().ToString("N") }).ToArray() });
            return Reject(() => ActionCatalog.RestoreCustom(settings, deleted))
                && Reject(() => ActionCatalog.RestoreCustom(full, deleted)) && full.CustomActions.Length == 34;
        });
        check("deleting builtins is refused and restoring only the latest deletion leaves older deletions removed", () =>
        {
            var other = action with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "另一動作" };
            var remaining = ActionCatalog.DeleteCustom(settings with { CustomActions = [action, other] }, action.Id, out _);
            remaining = ActionCatalog.DeleteCustom(remaining, other.Id, out var latest);
            var restored = ActionCatalog.RestoreCustom(remaining, latest);
            return Reject(() => ActionCatalog.DeleteCustom(settings, "copy", out _))
                && restored.CustomActions.Single(a => a.Id == other.Id) == other
                && !restored.CustomActions.Any(a => a.Id == action.Id);
        });
        check("More search matches Chinese names and URL aliases", () =>
            ActionCatalog.GetMore(settings, selection, "翻譯").Any(a => a.Id == action.Id)
            && ActionCatalog.GetMore(settings, selection, "URL").Any(a => a.Id == action.Id));
        check("bundled Gmail action resolves encoded selection from More", () =>
        {
            var item = ActionCatalog.GetMore(new(), selection, "Gmail").Single();
            var destination = CustomActionPolicy.ResolveUrl("中文 #&/", item.Custom!.Content);
            return item.Name == "搜尋 Gmail" && item.Custom.Kind == CustomActionKind.Url
                && ActionCatalog.IsApplicable(item, selection)
                && destination.Uri?.AbsoluteUri == "https://mail.google.com/mail/u/0/#search/%E4%B8%AD%E6%96%87%20%23%26%2F";
        });
        check("bundled public IP action uses no selection input and displays its result", () =>
        {
            var item = ActionCatalog.GetMore(new(), selection, "取得公網 IP").Single();
            return item.Custom is { Kind: CustomActionKind.PowerShell, UseSelection: false, Output: ActionOutput.Window }
                && CustomActionPolicy.Validate(item.Custom) == null
                && ActionCatalog.IsApplicable(item, selection);
        });
        foreach (var iconId in new[] { "globe", "terminal", "legacy:clean", "translate_20_regular", "unknown-fixture" })
        {
            var expected = iconId;
            check("custom icon round trip " + expected, () =>
            {
                var changed = action with { Icon = expected };
                return ActionCatalog.Normalize(settings with { CustomActions = [changed] }).CustomActions[0].Icon == expected
                    && CustomActionPolicy.Import(CustomActionPolicy.Export(changed)).Icon == expected;
            });
        }
        foreach (var iconId in new[] { "", "bad icon", "../icon", "圖示", new string('a', 129), null!, "control\nicon" })
        {
            var invalid = iconId;
            check("invalid custom icon falls back " + invalid?.Length, () =>
            {
                var changed = action with { Icon = invalid! };
                return ActionCatalog.Normalize(settings with { CustomActions = [changed] }).CustomActions[0].Icon == "spark"
                    && CustomActionPolicy.Import(CustomActionPolicy.Export(changed)).Icon == "spark";
            });
        }
        check("custom icon boundary accepts 128 ASCII characters", () =>
        {
            var changed = action with { Icon = new string('a', 128) };
            return ActionCatalog.Normalize(settings with { CustomActions = [changed] }).CustomActions[0].Icon.Length == 128
                && CustomActionPolicy.Import(CustomActionPolicy.Export(changed)).Icon.Length == 128;
        });
        foreach (var externalIcon in new string?[] { "future_icon_20_regular", "legacy:globe", "bad icon", "../icon", "\u5716\u793a", new string('a', 129), null, "control\nicon" })
        {
            check("external JSON import normalizes icon " + externalIcon?.Length, () =>
            {
                // Serialize the external envelope directly: Export must not sanitize this fixture first.
                var json = System.Text.Json.JsonSerializer.Serialize(new { Version = 2, Action = action with { Icon = externalIcon! } },
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                var imported = CustomActionPolicy.Import(json);
                return imported.Icon == (externalIcon is "future_icon_20_regular" or "legacy:globe" ? externalIcon : "spark")
                    && imported.Name == action.Name && imported.Content == action.Content;
            });
        }
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
        check("import rejects duplicate case-insensitive envelope properties", () => Reject(() => CustomActionPolicy.Import(CustomActionPolicy.Export(action).Replace("\"version\": 2", "\"version\": 2, \"Version\": 2"))));
        check("import rejects duplicate action properties", () => Reject(() => CustomActionPolicy.Import(CustomActionPolicy.Export(action).Replace("\"name\":", "\"name\": \"other\", \"name\":"))));
        check("import rejects future version", () => Reject(() => CustomActionPolicy.Import(CustomActionPolicy.Export(action).Replace("\"version\": 2", "\"version\": 3"))));
        check("legacy action without input flag still uses selection", () =>
            CustomActionPolicy.Import(CustomActionPolicy.Export(action).Replace("\"version\": 2", "\"version\": 1").Replace(",\n    \"useSelection\": true", "")).UseSelection);
        var independent = action with { Kind = CustomActionKind.PowerShell, Content = "Write-Output 'fixture'", UseSelection = false };
        check("new action envelope preserves explicit no-selection input", () => !CustomActionPolicy.Import(CustomActionPolicy.Export(independent)).UseSelection);
        check("legacy envelope cannot downgrade no-selection semantics", () => Reject(() => CustomActionPolicy.Import(CustomActionPolicy.Export(independent).Replace("\"version\": 2", "\"version\": 1"))));
        check("URL actions cannot disable selection", () => CustomActionPolicy.Validate(action with { UseSelection = false }) != null);
        check("import rejects oversized UTF8 input", () => Reject(() => CustomActionPolicy.Import(new string('中', 90000))));
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Actions.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            foreach (var version in new[] { 1, 2, 3, 4, 5, 6, 7 })
                check("action settings migration from v" + version, () =>
                {
                    File.WriteAllText(path, "{\"schemaVersion\":" + version + "}", new UTF8Encoding(false));
                    var before = File.ReadAllBytes(path);
                    var loaded = new SettingsStore(path).Load();
                    return loaded.Warning == null && loaded.Settings.SchemaVersion == 7 && loaded.Settings.Actions.Layout.Length == 10
                        && loaded.Settings.Actions.Layout.Single(e => e.Id == "ai").Placement == ActionPlacement.More
                        && loaded.Settings.Actions.Layout.Single(e => e.Id == "translate").Placement == ActionPlacement.More
                        && ActionCatalog.GetMore(loaded.Settings.Actions, selection, "Gmail").Count == 1
                        && ActionCatalog.GetMore(loaded.Settings.Actions, selection, "取得公網 IP").Count == 1
                        && before.SequenceEqual(File.ReadAllBytes(path));
                });
            check("legacy stored action without input flag defaults to true without rewriting", () =>
            {
                var actionJson = System.Text.Json.JsonSerializer.Serialize(action, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                File.WriteAllText(path, "{\"schemaVersion\":4,\"actions\":{\"customActions\":[" + actionJson.Replace(",\"useSelection\":true", "") + "]}}", new UTF8Encoding(false));
                var before = File.ReadAllBytes(path); var loaded = new SettingsStore(path).Load();
                return loaded.Warning == null && loaded.Settings.SchemaVersion == 7 && loaded.Settings.Actions.CustomActions.Single(a => a.Id == action.Id).UseSelection && before.SequenceEqual(File.ReadAllBytes(path));
            });
            check("current schema preserves explicit disabled selection on disk", () =>
            {
                var store = new SettingsStore(path); var loaded = store.Load();
                store.Save(loaded.Settings with { Actions = new() { CustomActions = [independent] } });
                var roundtrip = new SettingsStore(path).Load();
                return roundtrip.Warning == null && !roundtrip.Settings.Actions.CustomActions.Single(a => a.Id == independent.Id).UseSelection;
            });
            check("bundled Gmail edits, removal, and undo survive settings normalization", () =>
            {
                var store = new SettingsStore(path);
                var loaded = store.Load();
                var actions = loaded.Settings.Actions;
                var gmail = ActionCatalog.GetMore(actions, selection, "Gmail").Single().Custom!;
                var edited = gmail with { Content = gmail.Content.Replace("/u/0/", "/u/1/") };
                actions = actions with { CustomActions = actions.CustomActions.Select(a => a.Id == gmail.Id ? edited : a).ToArray() };
                store.Save(loaded.Settings with { Actions = actions });
                var editedSettings = store.Load().Settings;
                if (editedSettings.Actions.CustomActions.Single(a => a.Id == gmail.Id) != edited) return false;
                var removed = ActionCatalog.DeleteCustom(editedSettings.Actions, gmail.Id, out var undo);
                store.Save(editedSettings with { Actions = removed });
                var reloaded = store.Load().Settings.Actions;
                return !ActionCatalog.GetItems(reloaded).Any(a => a.Id == gmail.Id)
                    && ActionCatalog.GetMore(ActionCatalog.RestoreCustom(reloaded, undo), selection, "Gmail").Single().Custom == edited;
            });
            check("action settings roundtrip preserves nine pins, theme, labels and recents", () =>
            {
                var store = new SettingsStore(path); var loaded = store.Load();
                var actions = ActionCatalog.Remember(PinFiveMore(settings), action.Id) with { Theme = "Light", ShowLabels = false };
                store.Save(loaded.Settings with { Actions = actions });
                var roundtrip = new SettingsStore(path).Load();
                return roundtrip.Warning == null && roundtrip.Settings.Actions.CustomActions.SequenceEqual(actions.CustomActions)
                    && ActionCatalog.GetPinned(roundtrip.Settings.Actions).Count == 9
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
