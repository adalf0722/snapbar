using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class GeneralSettingsPage : ContentControl
{
    internal CheckBox AutomaticToggle { get; }
    private readonly ScrollViewer scroll;
    private readonly CheckBox startupToggle;
    private readonly TextBlock startupMessage;
    private readonly Button startupSettings, retryStartup;
    private readonly StartupRegistration startup;
    private bool updatingStartup;
    private ComboBox? languageSelector;
    private TextBlock? languageStatus;
    private bool updatingLanguage;

    internal void ConfigureLanguage(string preference, Func<string, bool> saveLanguage)
    {
        var row = new StackPanel();
        var heading = new DockPanel();
        languageSelector = new ComboBox { ItemsSource = new[] { UiText.Get("Language_OptionSystem"),
                UiText.Get("Language_OptionTraditionalChinese"), UiText.Get("Language_OptionEnglish") },
            SelectedIndex = LanguageIndex(preference), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 200, Margin = new(16, 0, 0, 0) };
        Identify(languageSelector, "InterfaceLanguageSelector", UiText.Get("Language_Label"));
        languageStatus = Description(""); Identify(languageStatus, "InterfaceLanguageStatus", UiText.Get("Language_Status"));
        languageStatus.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(languageSelector, Dock.Right); heading.Children.Add(languageSelector);
        heading.Children.Add(ActionUi.Heading(UiText.Get("Language_Label"), 16)); row.Children.Add(heading); row.Children.Add(languageStatus);
        var card = ActionUi.Card(row); card.Margin = new(0, 0, 0, 14);
        ((StackPanel)scroll.Content).Children.Insert(0, card);
        languageSelector.SelectionChanged += (_, _) =>
        {
            if (updatingLanguage || UiText.IsRefreshing) return;
            var proposed = languageSelector.SelectedIndex switch { 1 => "zh-Hant", 2 => "en", _ => "system" };
            if (saveLanguage(proposed)) { preference = proposed; languageStatus.Text = ""; languageStatus.Visibility = Visibility.Collapsed; return; }
            updatingLanguage = true;
            try { languageSelector.SelectedIndex = LanguageIndex(preference); languageStatus.Text = UiText.Get("Language_SaveFailed"); languageStatus.Visibility = Visibility.Visible; }
            finally { updatingLanguage = false; }
        };
    }

    private static int LanguageIndex(string preference) => preference switch { "zh-Hant" => 1, "en" => 2, _ => 0 };

    internal GeneralSettingsPage(bool automatic, Action toggle, StartupRegistration startup,
        bool ocrPreferEnglish, Action<bool> setOcrLanguagePriority)
    {
        this.startup = startup;
        ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var panel = new StackPanel();
        var launch = new DockPanel();
        startupToggle = new CheckBox { Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
        Identify(startupToggle, "StartupToggle", UiText.Get("GeneralSettingsPage_000"));
        startupToggle.Checked += (_, _) => ChangeStartup(true); startupToggle.Unchecked += (_, _) => ChangeStartup(false);
        DockPanel.SetDock(startupToggle, Dock.Right); launch.Children.Add(startupToggle);
        var launchTitle = new StackPanel(); launchTitle.Children.Add(ActionUi.Heading(UiText.Get("GeneralSettingsPage_001"), 16));
        startupMessage = Description(UiText.Get("GeneralSettingsPage_002"));
        Identify(startupMessage, "StartupStatus", UiText.Get("GeneralSettingsPage_003")); launchTitle.Children.Add(startupMessage);
        startupSettings = ActionUi.Button(UiText.Get("GeneralSettingsPage_004"), OpenStartupSettings);
        Identify(startupSettings, "StartupWindowsSettings", UiText.Get("GeneralSettingsPage_005"));
        startupSettings.HorizontalAlignment = HorizontalAlignment.Left; launchTitle.Children.Add(startupSettings);
        retryStartup = ActionUi.Button(UiText.Get("GeneralSettingsPage_006"), RefreshStartup); Identify(retryStartup, "StartupRetry", UiText.Get("GeneralSettingsPage_007"));
        retryStartup.HorizontalAlignment = HorizontalAlignment.Left; launchTitle.Children.Add(retryStartup);
        launch.Children.Add(launchTitle); AddCard(panel, launch);

        var detection = new DockPanel();
        AutomaticToggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = automatic,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = UiText.Get("GeneralSettingsPage_008") };
        Identify(AutomaticToggle, "AutomaticDetectionToggle", UiText.Get("GeneralSettingsPage_009"));
        AutomaticToggle.Click += (_, _) => toggle(); DockPanel.SetDock(AutomaticToggle, Dock.Right); detection.Children.Add(AutomaticToggle);
        var title = new StackPanel(); title.Children.Add(ActionUi.Heading(UiText.Get("GeneralSettingsPage_010"), 16));
        title.Children.Add(Description(UiText.Get("GeneralSettingsPage_011"))); detection.Children.Add(title); AddCard(panel, detection);

        var ocr = new DockPanel();
        var ocrLanguage = new ComboBox { ItemsSource = new[] { UiText.Get("GeneralSettingsPage_026"), UiText.Get("GeneralSettingsPage_027") },
            SelectedIndex = ocrPreferEnglish ? 1 : 0, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("GeneralSettingsPage_028") };
        Identify(ocrLanguage, "OcrLanguagePriority", UiText.Get("GeneralSettingsPage_029"));
        ocrLanguage.SelectionChanged += (_, _) => { if (!UiText.IsRefreshing) setOcrLanguagePriority(ocrLanguage.SelectedIndex == 1); };
        DockPanel.SetDock(ocrLanguage, Dock.Right);
        ocr.Children.Add(ocrLanguage);
        var ocrTitle = ActionUi.Heading(UiText.Get("GeneralSettingsPage_030"), 16);
        ocrTitle.Margin = new(0, 0, 12, 0);
        ocrTitle.VerticalAlignment = VerticalAlignment.Center;
        ocr.Children.Add(ocrTitle);
        AddCard(panel, ocr);
        scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Identify(scroll, "GeneralSettingsContent", UiText.Get("GeneralSettingsPage_031")); Content = scroll;
        RefreshStartup();
    }

    private static void Identify(DependencyObject control, string id, string name)
    { AutomationProperties.SetAutomationId(control, id); AutomationProperties.SetName(control, name); }
    private static readonly Brush DescriptionInk = ActionUi.Brush("#4F5968");
    private static TextBlock Description(string text) => ActionUi.Text(text, 14, DescriptionInk);
    private static void AddCard(Panel panel, UIElement content)
    { var card = ActionUi.Card(content); card.Margin = new(0, 0, 0, 14); panel.Children.Add(card); }

    internal void RefreshStartup()
    {
        var status = startup.Read(); updatingStartup = true;
        try { startupToggle.IsChecked = status.State == StartupState.On; startupToggle.IsEnabled = status.State != StartupState.Unavailable; }
        finally { updatingStartup = false; }
        startupSettings.Visibility = status.State == StartupState.DisabledByWindows ? Visibility.Visible : Visibility.Collapsed;
        retryStartup.Visibility = status.State == StartupState.Unavailable ? Visibility.Visible : Visibility.Collapsed;
        startupMessage.Text = status.State switch
        {
            StartupState.On => UiText.Get("GeneralSettingsPage_032"),
            StartupState.DisabledByWindows => UiText.Get("GeneralSettingsPage_033"),
            StartupState.Unavailable => UiText.Get("GeneralSettingsPage_034"),
            _ => status.DifferentLocation ? UiText.Get("GeneralSettingsPage_035") : UiText.Get("GeneralSettingsPage_036")
        };
    }

    private void ChangeStartup(bool enabled)
    {
        if (updatingStartup) return;
        try
        {
            var status = startup.SetEnabled(enabled); RefreshStartup();
            if (enabled && status.State is not (StartupState.On or StartupState.DisabledByWindows))
                startupMessage.Text = UiText.Get("GeneralSettingsPage_037");
        }
        catch (StartupLaunchException)
        { RefreshStartup(); startupMessage.Text = UiText.Get("GeneralSettingsPage_038"); }
        catch (Exception e) when (StartupRegistration.IsAccessError(e))
        { RefreshStartup(); startupMessage.Text = UiText.Get("GeneralSettingsPage_039"); }
    }

    private void OpenStartupSettings()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { startupMessage.Text = UiText.Get("GeneralSettingsPage_040"); }
    }

    internal void SetCompact(bool compact)
    {
        foreach (var card in ((StackPanel)scroll.Content).Children.OfType<Border>())
        {
            card.Padding = compact ? new(12, 8, 12, 8) : new(20);
            card.Margin = compact ? new(0, 0, 0, 8) : new(0, 0, 0, 14);
        }
    }
}
