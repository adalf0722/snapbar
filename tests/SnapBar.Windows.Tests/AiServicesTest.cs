using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;

internal static class AiServicesTest
{
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(item, i))) yield return child;
        if (item is Expander expander && !expander.IsExpanded && expander.Content is DependencyObject content)
            foreach (var child in Children<T>(content)) yield return child;
    }
    private static T Find<T>(Window window, string id) where T : DependencyObject => Children<T>(window).Single(x => AutomationProperties.GetAutomationId(x) == id);
    public static async Task RunAsync(string directory, Action<bool, string> check, Action<Window, string> snapshot)
    {
        var preferences = new AiPreferences(Path.Combine(directory, "services"));
        var original = preferences.Settings;
        var legacy = original.Profiles[0] with { Model = "legacy-private-model" };
        check(preferences.TrySave(original with { Profiles = original.Profiles.Select(p => p.Id == legacy.Id ? legacy : p).ToArray() }, out _), "service fixture saves isolated legacy model");
        var links = new List<Uri>();
        var panel = new AiSettingsPanel(preferences, links.Add);
        var window = new Window { Title = "AI services fixture", Width = 720, Height = 740, Content = panel };
        try
        {
            window.Show(); await Task.Delay(80); window.UpdateLayout();
            var advanced = Find<Expander>(window, "AiAdvancedSettings");
            var model = Find<ComboBox>(window, "AiModel");
            var endpoint = Find<TextBox>(window, "AiEndpoint");
            check(!advanced.IsExpanded, "named provider starts with advanced settings collapsed");
            check(model.IsEditable && model.Text == "legacy-private-model", "model editor preserves a value absent from recommendations");
            var editor = (TextBox)model.Template.FindName("PART_EditableTextBox", model);
            check(editor.IsVisible && editor.MaxLength == 128 && editor.Text == "legacy-private-model" && editor.Focus(),
                "editable model template visibly renders legacy text accepts focus and limits input to 128 characters");
            editor.Text = "keyboard-model";
            check(model.Text == "keyboard-model", "editing the actual model text box updates the draft");
            model.Text = "typed-model"; advanced.IsExpanded = true; advanced.IsExpanded = false;
            check(model.Text == "typed-model" && endpoint.Text == legacy.Endpoint, "collapsing advanced settings preserves field values");
            snapshot(window, "services-normal.png");
            var providers = Find<ComboBox>(window, "AiProvider");
            check(providers.Items.Count == 9 && links.Count == 0, "nine services load without opening a browser");
            var key = Find<PasswordBox>(window, "AiApiKey");
            void Click(string id) => Find<Button>(window, id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            for (var i = 0; i < original.Profiles.Length; i++)
            {
                providers.SelectedIndex = i; window.UpdateLayout();
                var profile = original.Profiles[i]; var definition = AiProviderCatalog.Find(profile.Id)!;
                check(advanced.IsExpanded == (profile.Id == "compatible"), profile.Id + " uses the expected advanced default");
                check(model.Text == (i == 0 ? "typed-model" : profile.Model), profile.Id + " does not auto-select a model");
                if (definition.Models.Length > 0)
                {
                    model.SelectedValue = definition.Models[0].Id;
                    check(model.Text == definition.Models[0].Id, profile.Id + " suggestion selects its exact model ID");
                }
                model.Text = "manual-" + i;
                endpoint.Text = "https://fixture.invalid/provider/" + i;
                key.Password = "disposable-key-" + i;
                check(Find<TextBlock>(window, "AiCustomEndpointNotice").Visibility == (profile.Id == "compatible" ? Visibility.Collapsed : Visibility.Visible), profile.Id + " marks a custom endpoint");
                if (definition.KeyUrl != null)
                {
                    var before = links.Count; Click("AiKeyLink"); Click("AiModelsLink");
                    check(links.Count == before + 2 && links[^2].AbsoluteUri == definition.KeyUrl && links[^1].AbsoluteUri == definition.ModelsUrl
                        && links.TakeLast(2).All(u => u.Scheme == "https"), profile.Id + " opens only its catalog HTTPS links");
                }
                else check(Find<Button>(window, "AiKeyLink").Visibility == Visibility.Collapsed && Find<Button>(window, "AiModelsLink").Visibility == Visibility.Collapsed,
                    "custom service hides absent official links");
            }
            var calls = links.Count;
            for (var i = 0; i < original.Profiles.Length; i++)
            {
                providers.SelectedIndex = i;
                check(model.Text == "manual-" + i && endpoint.Text == "https://fixture.invalid/provider/" + i && key.Password == "disposable-key-" + i,
                    original.Profiles[i].Id + " preserves independent model endpoint and key drafts");
            }
            check(links.Count == calls, "switching services never opens official links");
            providers.SelectedIndex = 0; endpoint.Text = legacy.Endpoint.ToUpperInvariant();
            check(Find<TextBlock>(window, "AiCustomEndpointNotice").Visibility == Visibility.Visible, "endpoint path case remains significant");
            endpoint.Text = "https://API.OPENAI.COM:443/v1/responses";
            check(Find<TextBlock>(window, "AiCustomEndpointNotice").Visibility == Visibility.Collapsed, "endpoint comparison normalizes host and default port");
            advanced.IsExpanded = true; window.Width = 540; window.Height = 420; window.UpdateLayout();
            var scroll = Children<ScrollViewer>(window).First(s => s.Content is StackPanel);
            scroll.ScrollToBottom(); window.UpdateLayout();
            check(Find<Button>(window, "SaveAiSettings").IsVisible && scroll.ScrollableHeight > 0, "minimum service window scrolls fields and keeps save footer visible");
            snapshot(window, "services-small-advanced.png");
        }
        finally { panel.Detach(); window.Close(); }
        var failurePanel = new AiSettingsPanel(preferences, _ => throw new InvalidOperationException("private diagnostic"));
        var failureWindow = new Window { Width = 600, Height = 650, Content = failurePanel };
        try
        {
            failureWindow.Show(); await Task.Delay(40);
            Find<Button>(failureWindow, "AiKeyLink").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(Find<TextBlock>(failureWindow, "AiSettingsError").Text == "無法開啟官方頁面，請稍後再試。", "link failure uses fixed safe Chinese feedback");
        }
        finally { failurePanel.Detach(); failureWindow.Close(); }
    }
}
