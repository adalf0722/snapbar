using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SnapBar.App;

// Presentation only: the original output remains the source for copy and apply.
internal static class ActionResultContent
{
    internal static FrameworkElement Create(string text, bool markdown, Func<string, FlowDocument> renderMarkdown)
    {
        var document = renderMarkdown(markdown ? text : "");
        document.FontSize = 15;
        document.TextAlignment = TextAlignment.Left;
        document.PagePadding = new(16);
        if (!markdown)
        {
            document.Blocks.Clear();
            var lines = text.ReplaceLineEndings("\n").Trim().Split('\n');
            var fields = lines.Select(line =>
            {
                var separator = line.IndexOfAny([':', '：']);
                return separator > 0 && separator <= 24 && separator < line.Length - 1
                    ? (Label: line[..separator].Trim(), Value: line[(separator + 1)..].Trim())
                    : default;
            }).ToArray();
            if (fields.Length is >= 2 and <= 32 && fields.All(field => !string.IsNullOrEmpty(field.Label) && !string.IsNullOrEmpty(field.Value)))
            {
                // Location is a useful summary when supplied; never infer or translate result data.
                var summary = Array.FindIndex(fields, field => field.Label is "位置" or "地點" || field.Label.Equals("Location", StringComparison.OrdinalIgnoreCase));
                if (summary < 0) summary = 0;
                var primary = new Paragraph(new Run(fields[summary].Value))
                {
                    FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12)
                };
                // Keep the summary label available to assistive technology and text selection.
                primary.Inlines.InsertBefore(primary.Inlines.FirstInline!, new Run(fields[summary].Label + "\n")
                    { FontSize = 12, FontWeight = FontWeights.Normal, Foreground = ActionUi.Muted });
                document.Blocks.Add(primary);
                for (var index = 0; index < fields.Length; index++)
                {
                    if (index == summary) continue;
                    var paragraph = new Paragraph { Margin = new(0, 0, 0, 6), LineHeight = 23 };
                    paragraph.Inlines.Add(new Run(fields[index].Label + "  ") { Foreground = ActionUi.Muted, FontSize = 13 });
                    paragraph.Inlines.Add(new Run(fields[index].Value));
                    document.Blocks.Add(paragraph);
                }
            }
            else document.Blocks.Add(new Paragraph(new Run(text)) { Margin = new(0), LineHeight = 24 });
        }
        var viewer = new FlowDocumentScrollViewer
        {
            Document = document, IsToolBarVisible = false, IsSelectionEnabled = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Top, MaxHeight = 400, Background = Brushes.White
        };
        // FlowDocument viewers otherwise request all available height, leaving short results in a large box.
        viewer.Loaded += (_, _) =>
        {
            if (FindScrollViewer(viewer) is not { } scroll) return;
            void FitContent() => viewer.MaxHeight = Math.Clamp(scroll.ExtentHeight, 64, 400);
            scroll.ScrollChanged += (_, _) => FitContent();
            FitContent();
        };
        return new Border { Background = Brushes.White, CornerRadius = new(10), BorderBrush = ActionUi.Line,
            BorderThickness = new(1), ClipToBounds = true, Child = viewer };
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, index)) is { } child) return child;
        return null;
    }
}
