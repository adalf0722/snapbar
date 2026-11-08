using System.Windows;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class ActionSettingsWindow : Window
{
    private readonly UiLanguage language = UiText.Capture();
    internal ActionSettingsPanel Panel { get; }
    public ActionSettingsWindow(ActionSettings initial, System.Func<ActionSettings, bool> save)
    {
        ActionUi.Setup(this, language.Get("ActionSettingsWindow_000"), 900, 820); MinWidth = 740;
        Panel = new(initial, save, language: language) { Margin = new(24) }; Content = Panel;
        WindowWorkArea.Attach(this);
    }
}
