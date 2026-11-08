using System.Collections;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Automation;
using System.Windows.Media;

namespace SnapBar.App;

internal sealed class UiLanguage(CultureInfo culture)
{
    internal CultureInfo Culture { get; } = culture;
    internal string Get(string key, params object?[] arguments) => UiText.Get(Culture, key, arguments);
    internal T Run<T>(Func<T> action) => UiText.WithCulture(Culture, action);
}

internal static class UiText
{
    internal static readonly ResourceManager Resources = new("SnapBar.App.Resources.Strings", typeof(UiText).Assembly);
    private sealed record Recipe(string Key, object?[] Arguments);
    // These are identities of strings created by resource calls, never a dictionary of user text.
    private static readonly ConditionalWeakTable<string, Recipe> Metadata = new();
    private static readonly AsyncLocal<CultureInfo?> Ambient = new();
    internal static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("zh-Hant");
    internal static event Action? Changed;
    internal static Func<string> SystemLanguageSource { get; set; } = SnapBar.Windows.SystemUiLanguage.Read;
    internal static bool IsRefreshing { get; private set; }
    internal static UiLanguage Capture() => new(Culture);
    internal static string Get(string key, params object?[] arguments) => Get(Ambient.Value ?? Culture, key, arguments);
    internal static T WithCulture<T>(CultureInfo culture, Func<T> action)
    {
        var previous = Ambient.Value; Ambient.Value = culture;
        try { return action(); }
        finally { Ambient.Value = previous; }
    }
    internal static string Get(CultureInfo culture, string key, params object?[] arguments)
    {
        var template = Resources.GetString(key, culture) ?? throw new MissingManifestResourceException(key);
        var values = arguments.Select(argument => argument is string text ? Current(text, culture) : argument).ToArray();
        var value = values.Length == 0 ? template : string.Format(culture, template, values);
        // ResourceManager may intern/reuse strings; keep a separate identity for every recipe.
        var tagged = new string(value.AsSpan());
        if (tagged.Length > 0) Metadata.Add(tagged, new(key, arguments));
        return tagged;
    }
    internal static void SetLanguage(string resolved)
    {
        var next = CultureInfo.GetCultureInfo(resolved);
        if (Culture.Name == next.Name) return;
        Culture = next;
        Changed?.Invoke();
    }
    internal static string Current(string value, CultureInfo? culture = null) => Metadata.TryGetValue(value, out var recipe)
        ? recipe.Key.Length == 0 ? Join(recipe.Arguments.Select(argument => argument is string text ? Current(text, culture) : argument).ToArray())
            : Get(culture ?? Culture, recipe.Key, recipe.Arguments) : value;
    internal static string Join(params object?[] values)
    {
        var result = new string(string.Concat(values).AsSpan());
        if (result.Length > 0) Metadata.Add(result, new("", values));
        return result;
    }

    internal static void Refresh(DependencyObject root, CultureInfo? culture = null)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        void Visit(DependencyObject element)
        {
            if (!visited.Add(element)) return;
            var localValues = element.GetLocalValueEnumerator();
            var properties = new List<DependencyProperty>();
            while (localValues.MoveNext()) properties.Add(localValues.Current.Property);
            foreach (var property in properties)
            {
                if (property.ReadOnly || property == Selector.SelectedItemProperty || property == Selector.SelectedValueProperty
                    || (element is TextBox && property == TextBox.TextProperty)
                    || (element is ComboBox && property == ComboBox.TextProperty)) continue;
                if (element.GetValue(property) is string value && Metadata.TryGetValue(value, out _))
                    element.SetCurrentValue(property, Current(value, culture));
            }
            // Only replace tagged product items; editable fields and selected/user text are never visited as text.
            if (element is ItemsControl items && items.ItemsSource is string[] values)
            {
                var selection = (items as Selector)?.SelectedIndex ?? -1;
                var translated = values.Select(value => Current(value, culture)).ToArray();
                if (translated.Where((value, i) => !ReferenceEquals(value, values[i])).Any())
                {
                    items.ItemsSource = translated;
                    if (items is Selector selector) selector.SelectedIndex = selection;
                }
            }
            else if (element is ItemsControl { ItemsSource: null } directItems)
            {
                var selectedIndex = (directItems as Selector)?.SelectedIndex ?? -1;
                for (int i = 0; i < directItems.Items.Count; i++)
                    if (directItems.Items[i] is string text && Metadata.TryGetValue(text, out _)) directItems.Items[i] = Current(text, culture);
                if (directItems is Selector selector) selector.SelectedIndex = selectedIndex;
            }
            foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().ToArray()) Visit(child);
            if (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Visit(VisualTreeHelper.GetChild(element, i));
            if (element is FrameworkElement { ContextMenu: { } menu }) Visit(menu);
        }
        var previous = IsRefreshing;
        IsRefreshing = true;
        try { Visit(root); }
        finally { IsRefreshing = previous; }
    }
}
