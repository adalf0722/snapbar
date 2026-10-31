using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapBar.App;

internal sealed class ActionTestComparisonView : StackPanel
{
    private readonly TextBlock summary = ActionUi.Text("", 12, ActionUi.Muted);
    private readonly TextBox before = CreateTextBox("TestInputWhitespace"), after = CreateTextBox("TestOutputWhitespace");
    private readonly Expander whitespace;

    public ActionTestComparisonView()
    {
        AutomationProperties.SetAutomationId(this, "ActionTestComparison");
        AutomationProperties.SetAutomationId(summary, "TestComparisonSummary");
        var body = new StackPanel();
        summary.Margin = new(0, 4, 0, 4); body.Children.Add(summary);
        var legend = ActionUi.Text("· 空格   ⇥ Tab   ␍ CR   ↵ 換行", 12, ActionUi.Muted);
        legend.Margin = new(0, 4, 0, 10); body.Children.Add(legend);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new() { Width = new(12) }); columns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var left = new StackPanel(); left.Children.Add(ActionUi.Text("輸入", 12, ActionUi.Muted)); before.Margin = new(0, 5, 0, 0); left.Children.Add(before);
        var right = new StackPanel(); right.Children.Add(ActionUi.Text("輸出", 12, ActionUi.Muted)); after.Margin = new(0, 5, 0, 0); right.Children.Add(after);
        columns.Children.Add(left); Grid.SetColumn(right, 2); columns.Children.Add(right); body.Children.Add(columns);
        whitespace = new Expander { Header = "輸入／輸出比較", Content = body };
        AutomationProperties.SetAutomationId(whitespace, "TestWhitespaceComparison"); Children.Add(whitespace);
        Visibility = Visibility.Collapsed;
    }

    public void Show(string input, string output)
    {
        var inputCount = new StringInfo(input).LengthInTextElements;
        var outputCount = new StringInfo(output).LengthInTextElements;
        var state = output.Length == 0 ? "沒有文字輸出" : string.Equals(input, output, StringComparison.Ordinal) ? "內容相同" : "內容已變更";
        summary.Text = $"輸入 {inputCount} 字元 → 輸出 {outputCount} 字元 · {state}";
        before.Text = VisibleWhitespace(input); after.Text = VisibleWhitespace(output);
        whitespace.IsExpanded = false; Visibility = Visibility.Visible;
    }

    public void Clear()
    {
        Visibility = Visibility.Collapsed; whitespace.IsExpanded = false;
        summary.Text = before.Text = after.Text = "";
    }

    internal static string VisibleWhitespace(string value)
    {
        var visible = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case ' ': visible.Append('·'); break;
                case '\t': visible.Append('⇥'); break;
                case '\r':
                    visible.Append('␍');
                    if (i + 1 < value.Length && value[i + 1] == '\n') { visible.Append('↵'); i++; }
                    visible.Append('\n'); break;
                case '\n': visible.Append("↵\n"); break;
                default: visible.Append(value[i]); break;
            }
        }
        return visible.ToString();
    }

    private static TextBox CreateTextBox(string id)
    {
        var text = ActionUi.Input(); text.IsReadOnly = true; text.AcceptsReturn = true; text.TextWrapping = TextWrapping.NoWrap;
        text.Height = 64; text.FontSize = 12; text.FontFamily = new("Consolas, Microsoft JhengHei UI");
        text.VerticalContentAlignment = VerticalAlignment.Top; text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        text.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        text.Background = Brushes.White; AutomationProperties.SetAutomationId(text, id);
        AutomationProperties.SetName(text, id == "TestInputWhitespace" ? "輸入的空白與換行" : "輸出的空白與換行");
        return text;
    }
}
