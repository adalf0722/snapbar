using System.IO;
using SnapBar.Core;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    private OnboardingCoordinator onboarding = null!;
    private bool firstRunEligible, offeringOnboarding;
    private bool backgroundInitialization;
    internal OnboardingCoordinator Onboarding => onboarding;

    private void InitializeOnboarding(bool enabled, bool settingsExisted, string? warning)
    {
        var store = new OnboardingStore(Path.Combine(Path.GetDirectoryName(settingsStore.FilePath)!, "onboarding.json"));
        onboarding = new(this, store);
        firstRunEligible = enabled && OnboardingPolicy.ShouldOffer(false, settingsExisted,
            warning is null or "hotkey-invalid", store.Load());
        Loaded += (_, _) => { if (firstRunEligible && IsVisible && !backgroundInitialization) _ = OfferOnboardingAsync(); };
    }

    private async Task OfferOnboardingAsync()
    {
        if (!firstRunEligible || offeringOnboarding || exitRequested) return;
        offeringOnboarding = true;
        try { await onboarding.OpenAsync(); }
        finally { offeringOnboarding = false; }
    }

    internal void OpenOnboarding() { if (!exitRequested) _ = onboarding.OpenAsync(); }

    internal OnboardingMessage OnboardingConfiguration(string? error = null) => new(1, "configure",
        Language: preferences.InterfaceLanguage, Shortcut: preferences.Hotkey.Display,
        Automatic: preferences.AutomaticDetectionEnabled, HotkeyRegistered: controller?.HotkeyRegistered == true, Error: error);

    internal void OnboardingShown() { firstRunEligible = false; Hide(); }

    internal void OnboardingShowSettings(bool shortcut)
    {
        firstRunEligible = false;
        ShowSettings();
        if (shortcut) { NavigateTo(SettingsCategory.Shortcuts); shortcutsPage.Edit(null); }
    }

    internal void OnboardingEnded(bool showSettings, bool failed, bool progressFailed = false)
    {
        if (exitRequested) return;
        // Do not re-enter the automatic offer when presenting recovery UI.
        var eligible = firstRunEligible; firstRunEligible = false;
        if (showSettings || tray == null || progressFailed) ShowSettings();
        if (failed) { ShowNotice(UiText.Get("Onboarding_StartFailed")); firstRunEligible = eligible; }
        else if (progressFailed) ShowNotice(UiText.Get("Onboarding_ProgressFailed"));
        else if (!showSettings && tray != null) Hide();
    }

    internal bool ApplyOnboardingPreference(OnboardingMessage message)
    {
        if (exitRequested) return false;
        if (message.Kind == "set-language") return ChangeInterfaceLanguage(message.Language!);
        if (message.Kind != "set-automatic" || controller == null) return false;
        var updated = preferences with { AutomaticDetectionEnabled = message.Automatic == true };
        try { settingsStore.Save(updated); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
        preferences = updated; controller.Automatic = updated.AutomaticDetectionEnabled;
        UpdateAutomaticControls(); return true;
    }
}
