using System.Windows;

namespace SnapBar.App;

internal sealed class AiSettingsWindow : Window
{
    internal AiSettingsPanel Panel { get; }
    public AiSettingsWindow(AiPreferences preferences)
    {
        ActionUi.Setup(this, "SnapBar · AI 設定", 600, 620); MinWidth = 480; MinHeight = 470;
        var body = new System.Windows.Controls.DockPanel { Margin = new(24) };
        var cancel = ActionUi.Button("取消", Close); cancel.HorizontalAlignment = HorizontalAlignment.Right; cancel.Margin = new(0, 10, 0, 0);
        System.Windows.Controls.DockPanel.SetDock(cancel, System.Windows.Controls.Dock.Bottom); body.Children.Add(cancel);
        Panel = new(preferences); body.Children.Add(Panel); Content = body;
        Panel.RevealRequested = () => { Show(); Activate(); };
        Panel.Saved += Close; Closed += (_, _) => Panel.Detach();
        WindowWorkArea.Attach(this);
    }
}
