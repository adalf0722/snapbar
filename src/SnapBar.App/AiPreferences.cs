using System.IO;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class AiPreferences
{
    private readonly AiSettingsStore store;
    public AiSettings Settings { get; private set; }
    public AiSecretStore Secrets { get; }
    public string? Warning { get; }
    public event Action? Changed;
    public event Action? UndoChanged;
    public event Action<SettingsCategory>? SettingsRequested;
    internal System.Windows.Window? SettingsHost { get; set; }
    public DeletedAiPrompt? LastDeletion { get; private set; }
    private readonly Dictionary<string, AiPromptEditorWindow> editors = [];
    private readonly List<ISettingsDraft> drafts = [];
    internal IReadOnlyList<ISettingsDraft> Drafts => drafts.ToArray();

    public AiPreferences(string directory)
    {
        store = new(Path.Combine(directory, "ai.json"));
        var loaded = store.Load(); Settings = loaded.Settings; Warning = loaded.Warning;
        Secrets = new(Path.Combine(directory, "secrets"));
    }

    public bool TrySave(AiSettings value, out string? error)
    {
        try { value = AiPolicy.Normalize(value); store.Save(value); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "AI 設定無法保存，請檢查設定格式、檔案權限或大小。原有設定已保留。"; return false; }
        Settings = value; error = null; Changed?.Invoke(); return true;
    }

    public AiProviderProfile SelectedProfile => Settings.Profiles.First(p => p.Id == Settings.SelectedProfileId);

    internal void RegisterDraft(ISettingsDraft draft) { if (!drafts.Contains(draft)) drafts.Add(draft); }
    internal void UnregisterDraft(ISettingsDraft draft) => drafts.Remove(draft);
    internal void NotifyChanged() => Changed?.Invoke();
    internal bool RequestSettings(SettingsCategory category)
    {
        if (SettingsRequested == null) return false;
        SettingsRequested.Invoke(category); return true;
    }

    public bool TrySavePrompt(AiPrompt prompt, bool isNew, out string? error)
    {
        if (!isNew && !Settings.Prompts.Any(p => p.Id == prompt.Id))
        { error = "此命令已被刪除；草稿仍保留。"; return false; }
        if (isNew && Settings.Prompts.Any(p => p.Id == prompt.Id))
        { error = "此命令 ID 已存在。"; return false; }
        var prompts = isNew ? Settings.Prompts.Append(prompt).ToArray()
            : Settings.Prompts.Select(p => p.Id == prompt.Id ? prompt : p).ToArray();
        return TrySave(Settings with { Prompts = prompts }, out error);
    }

    public bool TogglePin(string id, out string? error)
    {
        var pins = Settings.PinnedIds.Contains(id) ? Settings.PinnedIds.Where(p => p != id).ToArray()
            : Settings.PinnedIds.Append(id).ToArray();
        return TrySave(Settings with { PinnedIds = pins }, out error);
    }

    public bool DeletePrompt(string id, out string? error)
    {
        try
        {
            var updated = AiPromptCatalog.Delete(Settings, id, out var deletion);
            if (!TrySave(updated, out error)) return false;
            LastDeletion = deletion; UndoChanged?.Invoke(); return true;
        }
        catch (ArgumentException) { error = "無法刪除：找不到此命令。"; return false; }
    }

    public bool TryRestoreDeleted(out string? error)
    {
        if (LastDeletion == null) { error = "沒有可復原的命令。"; return false; }
        try
        {
            var updated = AiPromptCatalog.Restore(Settings, LastDeletion);
            if (!TrySave(updated, out error)) return false;
            LastDeletion = null; UndoChanged?.Invoke(); return true;
        }
        catch (ArgumentException) { error = "無法復原：命令 ID 已存在或已達 128 筆上限。"; return false; }
    }

    internal AiPromptEditorWindow OpenEditor(System.Windows.Window? owner, AiPrompt? prompt)
    {
        var identity = prompt?.Id ?? "__new";
        if (editors.TryGetValue(identity, out var active)) { active.Activate(); return active; }
        var editor = new AiPromptEditorWindow(prompt, changed => TrySavePrompt(changed, prompt == null, out var error) ? null : error);
        if (owner != null) editor.Owner = owner;
        editors.Add(identity, editor); RegisterDraft(editor);
        editor.Closed += (_, _) => { editors.Remove(identity); UnregisterDraft(editor); };
        editor.Show(); return editor;
    }
}
