using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SnapBar.Core;

namespace SnapBar.App;

internal static class ActionUi
{
    public static readonly Brush Ink = Brush("#20242C"), Muted = Brush("#626B78"), Canvas = Brush("#F6F7F9"), Line = Brush("#DDE2E9"), Accent = Brush("#3867D6"), Pale = Brush("#EDF2FF");
    public static readonly Brush DarkSurface = Brush("#252932"), DarkLine = Brush("#444C5C"), DarkHover = Brush("#363E4D"), DarkMuted = Brush("#B2BBCB");
    public static readonly Brush ButtonLine = Brush("#9AA7B8");
    public static readonly FontFamily Font = new("Microsoft JhengHei UI, Segoe UI");
    public static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    public static readonly string[] Icons = ["copy", "cut", "paste", "search", "link", "folder", "globe", "translate", "code", "terminal", "spark", "document", "clean"];
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["copy"] = "M8,8 L20,8 20,21 8,21 Z M16,8 L16,3 3,3 3,16 8,16",
        ["cut"] = "M8,8 L21,21 M8,16 L21,3 M9,6 A3,3 0 1 1 3,6 A3,3 0 1 1 9,6 M9,18 A3,3 0 1 1 3,18 A3,3 0 1 1 9,18",
        ["paste"] = "M9,4 L4,4 4,21 20,21 20,4 15,4 M9,2 L15,2 15,7 9,7 Z",
        ["search"] = "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L22,22",
        ["link"] = "M9,15 L15,9 M8,17 L6,19 A4,4 0 0 1 1,14 L6,9 A4,4 0 0 1 11,9 M16,7 L18,5 A4,4 0 0 1 23,10 L18,15 A4,4 0 0 1 13,15",
        ["folder"] = "M3,7 L3,3 10,3 12,6 21,6 21,21 3,21 Z",
        ["globe"] = "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M3,12 L21,12 M12,3 C18,8 18,16 12,21 C6,16 6,8 12,3",
        ["translate"] = "M3,5 L15,5 M9,2 L9,5 M6,5 C6,10 9,13 14,15 M13,5 C12,10 8,14 3,16 M14,21 L18,11 22,21 M16,17 L20,17",
        ["code"] = "M7,6 L2,12 7,18 M17,6 L22,12 17,18 M14,3 L10,21",
        ["terminal"] = "M2,4 L22,4 22,20 2,20 Z M6,8 L10,12 6,16 M13,16 L18,16",
        ["spark"] = "M12,3 L14.5,9.5 21,12 14.5,14.5 12,21 9.5,14.5 3,12 9.5,9.5 Z",
        ["document"] = "M14,2 L5,2 5,22 19,22 19,7 Z M14,2 L14,7 19,7 M8,12 L16,12 M8,16 L14,16",
        ["clean"] = "M3,7 L21,7 M3,12 L16,12 M3,17 L11,17 M16,17 L19,20 23,14",
        ["more"] = "M4,12 L6,12 M11,12 L13,12 M18,12 L20,12",
        ["grip"] = "M8,5 L8.1,5 M16,5 L16.1,5 M8,12 L8.1,12 M16,12 L16.1,12 M8,19 L8.1,19 M16,19 L16.1,19",
        ["plus"] = "M12,4 L12,20 M4,12 L20,12", ["up"] = "M5,15 L12,8 19,15", ["down"] = "M5,9 L12,16 19,9",
        ["pin"] = "M16,3 L21,8 17,10 16,15 13,16 8,11 9,8 14,7 Z M10,14 L4,20",
        ["settings"] = "M3,7 L9,7 M15,7 L21,7 M3,17 L13,17 M19,17 L21,17 M15,7 A3,3 0 1 1 9,7 A3,3 0 1 1 15,7 M19,17 A3,3 0 1 1 13,17 A3,3 0 1 1 19,17"
    };
    public static FrameworkElement Icon(string name, Brush? color = null, double size = 18) => new Viewbox
    {
        Width = size, Height = size, Child = new Path { Data = Geometry.Parse(Paths.GetValueOrDefault(name, Paths["spark"])),
            Stroke = color ?? Ink, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 24, Height = 24 }
    };
    public static FrameworkElement ItemIcon(ActionItem item, Brush color, double size = 18) =>
        item.Custom == null ? Icon(item.Icon, color, size)
            : new AiIconPresenter { IconId = item.Icon, Foreground = color, Width = size, Height = size };
    public static TextBlock Text(string value, double size = 14, Brush? color = null) => new()
    { Text = value, FontSize = Math.Max(12, size), FontFamily = Font, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    public static Button Button(string label, Action click, string? icon = null, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) { var image = Icon(icon, primary ? Brushes.White : Ink, 16); image.Margin = new(0, 0, label.Length == 0 ? 0 : 7, 0); content.Children.Add(image); }
        if (label.Length > 0) content.Children.Add(Text(label, 13, primary ? Brushes.White : Ink));
        var button = new Button { Content = content, Padding = new(12, 8, 12, 8), Margin = new(0, 0, 7, 0),
            Background = primary ? Accent : Brushes.White, Foreground = primary ? Brushes.White : Ink,
            BorderBrush = primary ? Accent : ButtonLine, BorderThickness = new(1), Cursor = System.Windows.Input.Cursors.Hand,
            MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary)
        {
            button.Resources["ControlHover"] = Brush("#2E58BE");
            button.Resources["ControlPressed"] = Brush("#24489F");
        }
        AutomationProperties.SetName(button, label.Length == 0 ? icon ?? "動作" : label);
        button.Click += (_, _) => click(); return button;
    }
    public static TextBlock Heading(string value, double size = 16)
    { var text = Text(value, size); text.FontWeight = FontWeights.SemiBold; return text; }

    public static void TintButton(Button button, bool dark)
    {
        var foreground = dark ? Brushes.White : Ink;
        button.Foreground = foreground;
        if (button.Content is StackPanel content)
            foreach (var child in content.Children)
            {
                if (child is TextBlock text) text.Foreground = foreground;
                if (child is Viewbox { Child: Path path }) path.Stroke = foreground;
            }
    }

    public static void LoadStyles(FrameworkElement element) => element.Resources.MergedDictionaries.Add(new ResourceDictionary
    { Source = new Uri("/SnapBar.App;component/Themes/Controls.xaml", UriKind.Relative) });
    public static Border Card(UIElement content, Thickness? padding = null) => new()
    { Background = Brushes.White, BorderBrush = Line, BorderThickness = new(1), CornerRadius = new(12), Padding = padding ?? new(20), Child = content };
    public static TextBox Input(string value = "", int max = 32768) => new()
    { Text = value, MaxLength = max, Padding = new(10, 8, 10, 8), BorderBrush = Line, BorderThickness = new(1), FontSize = 14, Background = Brushes.White };
    public static void Setup(Window window, string title, double width, double height)
    {
        window.Title = title; window.Width = width; window.Height = height; window.MinWidth = Math.Min(width, 570); window.MinHeight = 420;
        LoadStyles(window);
        window.Background = Canvas; window.Foreground = Ink; window.FontFamily = Font; window.FontSize = 14;
        window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
