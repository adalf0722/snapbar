using System.Windows;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class ActionSettingsWindow : Window
{
    internal ActionSettingsPanel Panel { get; }
    public ActionSettingsWindow(ActionSettings initial, System.Func<ActionSettings, bool> save)
    {
        ActionUi.Setup(this, "我的動作 · SnapBar", 900, 820); MinWidth = 740;
        Panel = new(initial, save) { Margin = new(24) }; Content = Panel;
        WindowWorkArea.Attach(this);
    }
}
