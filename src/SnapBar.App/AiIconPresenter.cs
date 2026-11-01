using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapBar.App;

internal sealed class AiIconPresenter : Viewbox
{
    internal static readonly FontFamily FluentFont = new(new Uri("pack://application:,,,/SnapBar.App;component/Assets/FluentIcons/"), "./#FluentSystemIcons-Regular");
    public static readonly DependencyProperty IconIdProperty = DependencyProperty.Register(nameof(IconId), typeof(string), typeof(AiIconPresenter),
        new PropertyMetadata("spark", (d, _) => ((AiIconPresenter)d).Render()));
    public string IconId { get => (string)GetValue(IconIdProperty); set => SetValue(IconIdProperty, value); }
    public AiIconPresenter() { Width = Height = 20; IsHitTestVisible = false; Render(); }
    private void Render()
    {
        var info = AiIconCatalog.Stored(IconId ?? "spark");
        Child = info is { Codepoint: > 0 } ? new TextBlock { Text = char.ConvertFromUtf32(info.Codepoint), FontFamily = FluentFont,
            FontSize = 20, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Width = 20, Height = 20, Foreground = ActionUi.Muted, TextAlignment = TextAlignment.Center }
            : ActionUi.Icon(info?.Id.Replace("legacy:", "", StringComparison.Ordinal) ?? "spark", ActionUi.Muted, 20);
    }
}
