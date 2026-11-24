using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SnapBar.App;

internal sealed class SettingsSearchBox : Grid
{
    internal SettingsSearchBox(TextBox input, string placeholderText, string clearLabel, string clearId)
    {
        Height = 36;
        input.Margin = new(0);
        input.Padding = new(36, 0, 40, 0);
        input.VerticalContentAlignment = VerticalAlignment.Center;
        Children.Add(input);

        var icon = ActionUi.Icon("search", ActionUi.Muted, 16);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new(12, 0, 0, 0);
        icon.IsHitTestVisible = false;
        Children.Add(icon);

        var placeholder = ActionUi.Text(placeholderText, 14, ActionUi.Muted);
        placeholder.Margin = new(37, 0, 40, 0);
        placeholder.TextWrapping = TextWrapping.NoWrap;
        placeholder.TextTrimming = TextTrimming.CharacterEllipsis;
        placeholder.IsHitTestVisible = false;
        AutomationProperties.SetAutomationId(placeholder, AutomationProperties.GetAutomationId(input) + "Placeholder");
        Children.Add(placeholder);

        var clear = ActionUi.Button("", () => { input.Clear(); input.Focus(); });
        clear.Content = ActionUi.Text("×", 18, ActionUi.Muted);
        clear.Width = clear.Height = clear.MinHeight = 28;
        clear.Padding = new(0);
        clear.Margin = new(0, 0, 4, 0);
        clear.HorizontalAlignment = HorizontalAlignment.Right;
        clear.VerticalAlignment = VerticalAlignment.Center;
        SettingsFeedback.Quiet(clear);
        SettingsFeedback.Hint(clear, clearLabel);
        AutomationProperties.SetName(clear, clearLabel);
        AutomationProperties.SetAutomationId(clear, clearId);
        Children.Add(clear);

        void Refresh()
        {
            var empty = input.Text.Length == 0;
            placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }
        input.TextChanged += (_, _) => Refresh();
        Refresh();
    }
}
