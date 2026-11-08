using System.Windows;

namespace SnapBar.App;

internal sealed class AiSettingsWindow : Window
{
    private readonly UiLanguage language = UiText.Capture();
    internal AiSettingsPanel Panel { get; }
    public AiSettingsWindow(AiPreferences preferences)
    {
        ActionUi.Setup(this, language.Get("AiSettingsWindow_000"), 600, 620); MinWidth = 480; MinHeight = 470;
        var body = new System.Windows.Controls.DockPanel { Margin = new(24) };
        var cancel = ActionUi.Button(language.Get("AiSettingsWindow_001"), Close); cancel.HorizontalAlignment = HorizontalAlignment.Right; cancel.Margin = new(0, 10, 0, 0);
        System.Windows.Controls.DockPanel.SetDock(cancel, System.Windows.Controls.Dock.Bottom); body.Children.Add(cancel);
        Panel = new(preferences, language: language); body.Children.Add(Panel); Content = body;
        Panel.RevealRequested = () => { Show(); Activate(); };
        Panel.Saved += Close; Closed += (_, _) => Panel.Detach();
        WindowWorkArea.Attach(this);
    }
}
