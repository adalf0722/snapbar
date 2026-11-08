using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapBar.App;

internal sealed class ActionDiagnosticView : StackPanel
{
    private readonly UiLanguage language = UiText.Capture();
    private readonly TextBlock summary = ActionUi.Text("", 13), feedback = ActionUi.Text("", 11, ActionUi.Muted);
    private readonly TextBox details = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 220,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new("Consolas, Microsoft JhengHei UI"), FontSize = 12, Padding = new(10),
        Background = Brushes.White, BorderBrush = ActionUi.Line, BorderThickness = new(1)
    };
    private readonly Expander expander;
    private string copyText = "";

    public ActionDiagnosticView()
    {
        summary.Margin = new(0, 0, 0, 14); Children.Add(summary);
        expander = new Expander { Header = language.Get("ActionDiagnosticView_000"), Content = details, Margin = new(0, 0, 0, 14) };
        Children.Add(expander);
        Children.Add(ActionUi.Button(language.Get("ActionDiagnosticView_001"), CopyDetails, "copy"));
        feedback.Margin = new(0, 6, 0, 0); Children.Add(feedback);
        AutomationProperties.SetAutomationId(this, "ActionDiagnosticView");
        AutomationProperties.SetAutomationId(summary, "ActionDiagnosticSummary");
        AutomationProperties.SetAutomationId(details, "ActionDiagnosticDetails");
    }

    public void Show(ScriptDiagnostic diagnostic)
    {
        summary.Text = diagnostic.Summary; details.Text = diagnostic.Details;
        copyText = diagnostic.Summary + Environment.NewLine + Environment.NewLine + diagnostic.Details;
        expander.IsExpanded = false; feedback.Text = ""; Visibility = Visibility.Visible;
    }

    public void Clear()
    {
        Visibility = Visibility.Collapsed; expander.IsExpanded = false;
        summary.Text = details.Text = copyText = feedback.Text = "";
    }

    private void CopyDetails()
    {
        try { Clipboard.SetText(copyText, TextDataFormat.UnicodeText); feedback.Text = language.Get("ActionDiagnosticView_002"); }
        catch (System.Runtime.InteropServices.ExternalException) { feedback.Text = language.Get("ActionDiagnosticView_003"); }
    }
}
