using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapBar.App;

internal static class SettingsFeedback
{
    internal static void Quiet(Button button)
    {
        button.BorderBrush = Brushes.Transparent;
        button.Background = Brushes.Transparent;
    }

    internal static Button IconButton(string icon, string label, Action click)
    {
        var button = ActionUi.Button("", click, icon);
        button.Width = button.Height = 36; button.Padding = new(8); button.Margin = new(0, 0, 4, 0);
        Quiet(button); Hint(button, label); AutomationProperties.SetName(button, label);
        return button;
    }

    internal static void Hint(FrameworkElement element, string text)
    {
        element.ToolTip = text;
        ToolTipService.SetInitialShowDelay(element, 450);
        AutomationProperties.SetHelpText(element, text);
    }

    internal static void Live(TextBlock text) => AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);

    // A preview and its row share emphasis without suggesting the row executes an action.
    internal static void Link(FrameworkElement row, Border card, FrameworkElement preview, Border previewSurface, bool dark, bool reverse = true)
    {
        void Refresh()
        {
            var active = row.IsMouseOver || row.IsKeyboardFocusWithin || (reverse && preview.IsMouseOver);
            card.BorderBrush = active ? ActionUi.Accent : ActionUi.Line;
            previewSurface.Background = active ? dark ? ActionUi.DarkHover : ActionUi.Pale : Brushes.Transparent;
        }
        row.MouseEnter += (_, _) => Refresh(); row.MouseLeave += (_, _) => Refresh();
        row.IsKeyboardFocusWithinChanged += (_, _) => Refresh();
        if (reverse) { preview.MouseEnter += (_, _) => Refresh(); preview.MouseLeave += (_, _) => Refresh(); }
    }
}
