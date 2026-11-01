using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SnapBar.App;

internal static class AiMarkdownView
{
    private static readonly Regex Inline = new(@"(`[^`]+`|\*\*[^*]+\*\*|\*[^*]+\*)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    public static FrameworkElement Create(string text)
    {
        // Large output remains fully available without creating thousands of document blocks.
        if (text.Length > 64000 || text.Count(c => c == '\n') > 1000) return Plain(text);
        var document = new FlowDocument { FontFamily = ActionUi.Font, FontSize = 15, LineHeight = 24, PagePadding = new(20), Foreground = ActionUi.Ink, Background = Brushes.Transparent };
        var code = new List<string>(); bool inCode = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode) { AddCode(document, string.Join("\n", code)); code.Clear(); }
                inCode = !inCode; continue;
            }
            if (inCode) { code.Add(raw); continue; }
            var line = raw; var paragraph = new Paragraph { Margin = new(0, 3, 0, 8) };
            if (line.StartsWith('#'))
            {
                var count = line.TakeWhile(c => c == '#').Count();
                if (count <= 6 && line.Length > count && line[count] == ' ')
                { paragraph.FontSize = Math.Max(15, 28 - count * 2); paragraph.FontWeight = FontWeights.SemiBold; line = line[(count + 1)..]; }
            }
            if (line.StartsWith("> ", StringComparison.Ordinal)) { line = line[2..]; paragraph.BorderBrush = ActionUi.Line; paragraph.BorderThickness = new(3, 0, 0, 0); paragraph.Padding = new(10, 0, 0, 0); }
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)) line = "•  " + line[2..];
            var position = 0;
            foreach (Match match in Inline.Matches(line))
            {
                if (match.Index > position) paragraph.Inlines.Add(new Run(line[position..match.Index]));
                var value = match.Value;
                if (value[0] == '`') paragraph.Inlines.Add(new Run(value[1..^1]) { FontFamily = new("Consolas"), Background = ActionUi.Canvas });
                else if (value.StartsWith("**", StringComparison.Ordinal)) paragraph.Inlines.Add(new Bold(new Run(value[2..^2])));
                else paragraph.Inlines.Add(new Italic(new Run(value[1..^1])));
                position = match.Index + match.Length;
            }
            if (position < line.Length) paragraph.Inlines.Add(new Run(line[position..]));
            document.Blocks.Add(paragraph);
        }
        if (code.Count > 0) AddCode(document, string.Join("\n", code));
        return new FlowDocumentScrollViewer { Document = document, Background = Brushes.Transparent, IsToolBarVisible = false, IsSelectionEnabled = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    public static TextBox Plain(string text) => new() { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
        Padding = new(20), BorderThickness = new(0), Background = Brushes.Transparent, FontSize = 15 };
    private static void AddCode(FlowDocument document, string text)
    {
        var panel = new DockPanel { Background = ActionUi.Canvas, Margin = new(0, 6, 0, 12) };
        var copy = ActionUi.Button("複製程式碼", () => { try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { } });
        copy.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(copy, Dock.Top); panel.Children.Add(copy);
        var code = Plain(text); code.FontFamily = new("Consolas"); code.TextWrapping = TextWrapping.NoWrap; code.Background = ActionUi.Canvas;
        code.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; code.MaxHeight = 320; panel.Children.Add(code);
        document.Blocks.Add(new BlockUIContainer(panel));
    }
}
