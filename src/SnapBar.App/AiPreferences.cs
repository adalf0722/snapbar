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
}
