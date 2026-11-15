using System.IO;
using System.Windows.Interop;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    internal Func<Action, Action, Action, TrayIcon> CreateTrayIcon { get; set; } = (show, toggle, exit) => new(show, toggle, exit);
    internal GeneralSettingsPage GeneralPage => generalPage;
    internal HotkeySettings CurrentHotkey => preferences.Hotkey;

    private HotkeySettings? ReadShortcut(TextAction? action) => action switch
    {
        TextAction.Search => preferences.SearchHotkey,
        TextAction.Copy => preferences.CopyHotkey,
        TextAction.Ocr => preferences.OcrHotkey,
        TextAction.Lookup => preferences.LookupHotkey,
        TextAction.Ai => preferences.AiHotkey,
        TextAction.Custom => preferences.CustomHotkey,
        _ => preferences.Hotkey
    };

    private ShortcutSaveError? SaveShortcut(TextAction? action, HotkeySettings? value, string? targetId)
    {
        if (controller == null || exitRequested) return new(UiText.Get("MainWindow_General_000"));
        if (value != null && new (TextAction? Action, HotkeySettings? Key)[] {
            (null, preferences.Hotkey), (TextAction.Search, preferences.SearchHotkey), (TextAction.Ocr, preferences.OcrHotkey),
            (TextAction.Lookup, preferences.LookupHotkey), (TextAction.Ai, preferences.AiHotkey), (TextAction.Custom, preferences.CustomHotkey)
        }.Any(binding => binding.Action != action && binding.Key == value))
            return new(UiText.Get("MainWindow_General_001"), RetryKeys: true);
        if (value != null && (action == TextAction.Ai && !aiPreferences.Settings.Prompts.Any(p => p.Id == targetId)
            || action == TextAction.Custom && !AvailableShortcutActions().Any(a => a.Id == targetId)))
            return new(UiText.Get("GeneralSettings_ShortcutTargetMissing"));
        if (action == null) return SaveHotkey(value!);
        var updated = action switch
        {
            TextAction.Search => preferences with { SearchHotkey = value },
            TextAction.Copy => preferences with { CopyHotkey = value },
            TextAction.Ocr => preferences with { OcrHotkey = value },
            TextAction.Lookup => preferences with { LookupHotkey = value },
            TextAction.Ai => preferences with { AiHotkey = value, AiHotkeyPromptId = value == null ? null : targetId },
            TextAction.Custom => preferences with { CustomHotkey = value, CustomHotkeyActionId = value == null ? null : targetId },
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        try
        {
            if (!controller.TryChangeActionHotkey(action.Value, value, () => settingsStore.Save(updated)))
                return new(UiText.Get("MainWindow_General_002"), RetryKeys: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(UiText.Get("MainWindow_General_003")); }
        preferences = updated;
        controller.AiShortcutPromptId = preferences.AiHotkeyPromptId;
        controller.CustomShortcutActionId = preferences.CustomHotkeyActionId;
        return null;
    }

    private IReadOnlyList<CustomActionDefinition> AvailableShortcutActions() => ActionCatalog.Normalize(preferences.Actions).CustomActions
        .Where(a => !preferences.Actions.Layout.Any(e => e.Id == a.Id && e.Placement == ActionPlacement.Hidden)).ToArray();

    private ShortcutSaveError? SaveHotkey(HotkeySettings value)
    {
        if (controller == null || exitRequested) return new(UiText.Get("MainWindow_General_004"));
        if (!value.IsValid) return new(UiText.Get("MainWindow_General_005"), RetryKeys: true);
        var updated = preferences with { Hotkey = value };
        try
        {
            if (!controller.TryChangeHotkey(value, () => settingsStore.Save(updated)))
                return new(controller.HotkeyRegistered ? UiText.Get("MainWindow_General_006")
                    : UiText.Get("MainWindow_General_007"), RetryKeys: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(UiText.Get("MainWindow_General_008")); }
        preferences = updated; UpdateAutomaticControls();
        _ = onboarding.ConfigureAsync();
        if (hotkeyNotice) { notice.Visibility = System.Windows.Visibility.Collapsed; hotkeyNotice = false; }
        AddStatus(UiText.Join(UiText.Get("MainWindow_General_009"), value.Display));
        return null;
    }

    internal void InitializeBackground()
    {
        backgroundInitialization = true;
        try
        {
            new WindowInteropHelper(this).EnsureHandle();
            if (tray == null) ShowSettings();
        }
        finally { Dispatcher.BeginInvoke(() => backgroundInitialization = false); }
    }
}
