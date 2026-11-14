using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SnapBar.App;

internal sealed partial class OnboardingPracticeWindow
{
    private TextBlock progress = null!;
    private FrameworkElement progressSurface = null!;
    private ComboBox language = null!;
    private CheckBox automatic = null!;
    private TextBox practiceText = null!;
    private TextBlock? selectionCue;

    private void Render()
    {
        updating = true;
        try
        {
            UiText.SetLanguage(SnapBar.Core.InterfaceLanguage.Resolve(configuration.Language, UiText.SystemLanguageSource()));
            Title = UiText.Get("Onboarding_Title");
            selectionCue = null;
            var shell = new Grid { Margin = new(32, 24, 32, 22) };
            shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
            shell.Children.Add(BuildHeader());

            var body = new StackPanel { Margin = new(0, 10, 0, 18) };
            body.Children.Add(ActionUi.Heading(UiText.Get(completed ? "Onboarding_ReadyHeadline" : "Onboarding_Headline"), 28));
            if (!completed)
            {
                var introduction = ActionUi.Text(UiText.Get("Onboarding_Instructions"), 14, ActionUi.Muted);
                introduction.Margin = new(0, 10, 0, 20); body.Children.Add(introduction);
            }
            var practice = BuildPractice(); practice.Margin = new(0, completed ? 18 : 0, 0, 0);
            body.Children.Add(practice);
            if (!completed)
            {
                body.Children.Add(BuildShortcut());
                var browser = ActionUi.Text(UiText.Get("Onboarding_Browser"), 12, ActionUi.Muted);
                browser.Margin = new(2, 12, 2, 0); body.Children.Add(browser);
            }
            body.Children.Add(BuildProgress());
            if (configuration.HotkeyRegistered != true)
            {
                var change = MakeButton("Onboarding_ChangeShortcut", "OnboardingChangeShortcut", "change-shortcut");
                change.HorizontalAlignment = HorizontalAlignment.Left; change.Margin = new(0, 10, 0, 0);
                body.Children.Add(change);
                if (!completed) Error("Onboarding_HotkeyFailed");
            }
            body.Children.Add(BuildPreferences());
            if (configuration.Error != null) Error("Onboarding_ProgressFailed");

            var scroll = new ScrollViewer { Content = body, VerticalContentAlignment = VerticalAlignment.Center,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Identify(scroll, "OnboardingContent", UiText.Get("Onboarding_Title"));
            Grid.SetRow(scroll, 1); shell.Children.Add(scroll);
            var footer = BuildFooter(); Grid.SetRow(footer, 2); shell.Children.Add(footer);
            Content = shell;
            SetPending();
        }
        finally { updating = false; }
    }

    private FrameworkElement BuildHeader()
    {
        var header = new DockPanel { Margin = new(0, 0, 0, 16) };
        language = new ComboBox { ItemsSource = new[] { "Language · System", "English", "繁體中文" },
            SelectedIndex = configuration.Language switch { "en" => 1, "zh-Hant" => 2, _ => 0 },
            MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Right };
        KeyboardNavigation.SetTabIndex(language, 3);
        Identify(language, "OnboardingLanguage", UiText.Get("Onboarding_Language"));
        language.SelectionChanged += (_, _) =>
        {
            if (!updating) _ = PreferenceAsync("set-language", language.SelectedIndex switch { 1 => "en", 2 => "zh-Hant", _ => "system" });
        };
        DockPanel.SetDock(language, Dock.Right); header.Children.Add(language);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = ActionUi.BrandIcon(24); icon.Margin = new(0, 0, 9, 0);
        brand.Children.Add(icon); brand.Children.Add(ActionUi.Heading("SnapBar", 17)); header.Children.Add(brand);
        return header;
    }

    private Border BuildPractice()
    {
        var sample = new TextBox { Text = UiText.Get("Onboarding_Sample"), IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap, FontSize = completed ? 22 : 25,
            Padding = new(22, completed ? 16 : 24, 22, completed ? 16 : 24), MinHeight = completed ? 80 : 116,
            BorderThickness = new(0), Background = Brushes.Transparent, Foreground = ActionUi.Ink,
            VerticalContentAlignment = VerticalAlignment.Center, Cursor = Cursors.IBeam,
            IsInactiveSelectionHighlightEnabled = true };
        practiceText = sample;
        KeyboardNavigation.SetTabIndex(sample, 0);
        Identify(sample, "OnboardingSample", UiText.Get("Onboarding_SampleLabel"));
        AutomationProperties.SetHelpText(sample, UiText.Get("Onboarding_Instructions"));
        var surface = new Border { Background = Brushes.White, BorderBrush = ActionUi.Line, BorderThickness = new(1),
            CornerRadius = new(14), Child = sample, Cursor = Cursors.IBeam,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.045, Color = Colors.Black } };
        Identify(surface, "OnboardingSelectionSurface", UiText.Get("Onboarding_SampleLabel"));
        void RefreshSurface()
        {
            var active = sample.IsKeyboardFocusWithin || sample.SelectionLength > 0;
            surface.BorderBrush = active ? ActionUi.Accent : surface.IsMouseOver ? ActionUi.Brush("#AEC2EB") : ActionUi.Line;
            surface.Background = surface.IsMouseOver && !active ? ActionUi.Brush("#F3F6FD") : Brushes.White;
        }
        surface.MouseEnter += (_, _) => RefreshSurface();
        surface.MouseLeave += (_, _) => RefreshSurface();
        sample.IsKeyboardFocusWithinChanged += (_, _) => RefreshSurface();
        sample.SelectionChanged += (_, _) =>
        {
            RefreshSurface();
            if (updating || completed || sample != practiceText || selectionCue == null) return;
            var selected = sample.SelectionLength > 0;
            selectionCue.Text = UiText.Get(selected ? "Onboarding_SelectedBeforeKeys" : "Onboarding_BeforeKeys");
            selectionCue.Foreground = selected ? ActionUi.Accent : ActionUi.Muted;
            AutomationProperties.SetName(selectionCue, selectionCue.Text);
        };
        return surface;
    }

    private FrameworkElement BuildShortcut()
    {
        var shortcut = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(2, 18, 0, 0) };
        Identify(shortcut, "OnboardingShortcutKeys", configuration.Shortcut!);
        selectionCue = ActionUi.Text(UiText.Get("Onboarding_BeforeKeys"), 13, ActionUi.Muted);
        selectionCue.Width = 78; selectionCue.Margin = new(0, 0, 8, 0);
        Identify(selectionCue, "OnboardingSelectionCue", selectionCue.Text);
        AutomationProperties.SetLiveSetting(selectionCue, AutomationLiveSetting.Polite);
        shortcut.Children.Add(selectionCue);
        var keys = configuration.Shortcut!.Split(" + ", StringSplitOptions.None);
        for (var index = 0; index < keys.Length; index++)
        {
            if (index > 0)
            {
                var plus = ActionUi.Text("+", 12, ActionUi.Muted); plus.Margin = new(5, 0, 5, 0);
                shortcut.Children.Add(plus);
            }
            shortcut.Children.Add(new Border { Background = ActionUi.Canvas, BorderBrush = ActionUi.Line,
                BorderThickness = new(1, 1, 1, 2), CornerRadius = new(5), Padding = new(9, 5, 9, 5),
                Margin = new(0, 2, 0, 2), Child = ActionUi.Text(keys[index], 13) });
        }
        var after = ActionUi.Text(UiText.Get("Onboarding_AfterKeys"), 13);
        after.Margin = new(12, 0, 0, 0); shortcut.Children.Add(after);
        return shortcut;
    }

    private FrameworkElement BuildProgress()
    {
        var row = new DockPanel();
        if (completed)
        {
            var symbol = new Grid { Width = 24, Height = 24, Margin = new(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            symbol.Children.Add(new Ellipse { Fill = ActionUi.Accent });
            symbol.Children.Add(new Path { Data = Geometry.Parse("M6,12 L10,16 18,8"), Stroke = Brushes.White,
                StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            DockPanel.SetDock(symbol, Dock.Left); row.Children.Add(symbol);
        }
        progress = ActionUi.Text(UiText.Get(completed ? "Onboarding_Success" : "Onboarding_Hint"), 14, ActionUi.Ink);
        Identify(progress, "OnboardingProgress", progress.Text);
        AutomationProperties.SetLiveSetting(progress, AutomationLiveSetting.Polite);
        row.Children.Add(progress);
        progressSurface = new Border { Child = row, Margin = new(0, 16, 0, 0),
            Visibility = completed ? Visibility.Visible : Visibility.Collapsed };
        return progressSurface;
    }

    private FrameworkElement BuildPreferences()
    {
        var preferences = new StackPanel { Margin = new(0, 18, 0, 0), Visibility = completed ? Visibility.Visible : Visibility.Collapsed };
        automatic = new CheckBox { IsChecked = configuration.Automatic, Style = (Style)FindResource("Switch") };
        KeyboardNavigation.SetTabIndex(automatic, 1);
        Identify(automatic, "OnboardingAutomatic", UiText.Get("Onboarding_Automatic"));
        automatic.Click += (_, _) => { if (!updating) _ = PreferenceAsync("set-automatic", automatic: automatic.IsChecked == true); };
        var row = new DockPanel(); DockPanel.SetDock(automatic, Dock.Right); row.Children.Add(automatic);
        var label = ActionUi.Text(UiText.Get("Onboarding_Automatic"), 14); label.Margin = new(0, 0, 16, 0);
        row.Children.Add(label); preferences.Children.Add(row);
        var tray = ActionUi.Text(UiText.Get("Onboarding_Tray"), 12, ActionUi.Muted);
        tray.Margin = new(0, 12, 0, 0); preferences.Children.Add(tray);
        return preferences;
    }

    private FrameworkElement BuildFooter()
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var skip = MakeButton("Onboarding_Skip", "OnboardingSkip", "skip");
        MakeQuiet(skip); skip.HorizontalAlignment = HorizontalAlignment.Left;
        KeyboardNavigation.SetTabIndex(skip, 4);
        skip.ToolTip = UiText.Get("Onboarding_SkipHint"); skip.Visibility = completed ? Visibility.Collapsed : Visibility.Visible;
        row.Children.Add(skip);
        var actions = new WrapPanel { Visibility = completed ? Visibility.Visible : Visibility.Collapsed };
        var settings = MakeButton("Onboarding_Settings", "OnboardingOpenSettings", "open-settings");
        KeyboardNavigation.SetTabIndex(settings, 4);
        MakeQuiet(settings); actions.Children.Add(settings);
        var finish = MakeButton("Onboarding_Finish", "OnboardingFinish", "finish", true);
        KeyboardNavigation.SetTabIndex(finish, 2);
        finish.Margin = new(6, 0, 0, 0); finish.IsDefault = completed; actions.Children.Add(finish);
        Grid.SetColumn(actions, 1); row.Children.Add(actions);
        return new Border { BorderBrush = ActionUi.Line, BorderThickness = new(0, 1, 0, 0), Padding = new(0, 16, 0, 0), Child = row };
    }

    private static void MakeQuiet(Button button)
    {
        button.Background = Brushes.Transparent; button.BorderThickness = new(0); button.BorderBrush = Brushes.Transparent;
        button.Foreground = ActionUi.Muted;
        foreach (var text in ((StackPanel)button.Content).Children.OfType<TextBlock>()) text.Foreground = ActionUi.Muted;
    }
    private Button MakeButton(string key, string id, string kind, bool primary = false)
    {
        var button = ActionUi.Button(UiText.Get(key), () => _ = RequestAsync(new(1, kind)), primary: primary);
        Identify(button, id, UiText.Get(key)); return button;
    }
    private static void Identify(DependencyObject control, string id, string name)
    { AutomationProperties.SetAutomationId(control, id); AutomationProperties.SetName(control, name); }
    private void Error(string key)
    {
        progressSurface.Visibility = Visibility.Visible;
        progress.Text = UiText.Get(key); progress.Foreground = Brushes.Firebrick;
        AutomationProperties.SetName(progress, progress.Text);
    }
    private void SetPending() { language.IsEnabled = automatic.IsEnabled = pending == 0; }
}
