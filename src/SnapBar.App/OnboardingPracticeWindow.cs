using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class OnboardingPracticeWindow : Window
{
    private readonly Func<OnboardingMessage, Task> send;
    private OnboardingMessage configuration = new(1, "configure", Language: "en", Shortcut: "Ctrl + Alt + V",
        Automatic: false, HotkeyRegistered: true);
    private bool completed, updating, closing;
    private long request, pending;
    private TextBlock progress = null!;
    private ComboBox language = null!;
    private CheckBox automatic = null!;
    private StackPanel completion = null!;
    private Button skipButton = null!;
    internal bool Completed => completed;

    internal OnboardingPracticeWindow(Func<OnboardingMessage, Task> send)
    {
        this.send = send;
        ActionUi.Setup(this, "SnapBar", 640, 660);
        WindowWorkArea.Attach(this);
        Closing += (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            _ = RequestAsync(new(1, completed ? "finish" : "skip"));
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true; Close();
        };
    }

    internal void Receive(OnboardingMessage message)
    {
        if (message.Kind == "close") { CloseFromParent(); return; }
        if (message.Kind == "configure")
        {
            configuration = message;
            Render();
            if (!IsVisible) { Show(); Activate(); }
        }
        else if (message.Kind == "search-launched")
        {
            completed = true; completion.Visibility = Visibility.Visible;
            progress.Text = UiText.Get("Onboarding_Success"); progress.Foreground = ActionUi.Accent;
            AutomationProperties.SetName(progress, progress.Text); skipButton.Visibility = Visibility.Collapsed;
        }
        else if (message.Kind == "search-failed") Error("Onboarding_SearchFailed");
        else if (message.Kind == "preference-result" && message.RequestId == pending)
        {
            pending = 0;
            configuration = configuration with { Language = message.Language, Automatic = message.Automatic };
            Render();
            if (message.Error != null) Error("Onboarding_SaveFailed");
        }
    }

    private void Render()
    {
        updating = true;
        try
        {
            UiText.SetLanguage(InterfaceLanguage.Resolve(configuration.Language, UiText.SystemLanguageSource()));
            Title = UiText.Get("Onboarding_Title");
            var body = new StackPanel { Margin = new(28) };
            var top = new DockPanel { Margin = new(0, 0, 0, 22) };
            language = new ComboBox { ItemsSource = new[] { "Language · System", "English", "繁體中文" },
                SelectedIndex = configuration.Language switch { "en" => 1, "zh-Hant" => 2, _ => 0 },
                MinWidth = 145, HorizontalAlignment = HorizontalAlignment.Right };
            Identify(language, "OnboardingLanguage", UiText.Get("Onboarding_Language"));
            language.SelectionChanged += (_, _) =>
            {
                if (!updating) _ = PreferenceAsync("set-language", language.SelectedIndex switch { 1 => "en", 2 => "zh-Hant", _ => "system" });
            };
            DockPanel.SetDock(language, Dock.Right); top.Children.Add(language);
            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = ActionUi.BrandIcon(28); icon.Margin = new(0, 0, 10, 0);
            brand.Children.Add(icon); brand.Children.Add(ActionUi.Heading("SnapBar", 20)); top.Children.Add(brand); body.Children.Add(top);
            body.Children.Add(ActionUi.Heading(UiText.Get("Onboarding_Headline"), 27));
            var instruction = ActionUi.Text(UiText.Get("Onboarding_Instructions", configuration.Shortcut), 15, ActionUi.Muted);
            instruction.Margin = new(0, 14, 0, 20); body.Children.Add(instruction);
            var sample = new TextBox { Text = UiText.Get("Onboarding_Sample"), IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap, FontSize = 22, Padding = new(18), MinHeight = 110,
                BorderBrush = ActionUi.Line, Background = Brushes.White, VerticalContentAlignment = VerticalAlignment.Center };
            Identify(sample, "OnboardingSample", UiText.Get("Onboarding_SampleLabel"));
            body.Children.Add(sample);
            var browser = ActionUi.Text(UiText.Get("Onboarding_Browser"), 12, ActionUi.Muted);
            browser.Margin = new(0, 10, 0, 16); body.Children.Add(browser);
            progress = ActionUi.Text(UiText.Get(completed ? "Onboarding_Success" : "Onboarding_Hint"), 14,
                completed ? ActionUi.Accent : ActionUi.Muted);
            Identify(progress, "OnboardingProgress", progress.Text);
            AutomationProperties.SetLiveSetting(progress, AutomationLiveSetting.Polite);
            body.Children.Add(progress);
            if (configuration.Error != null) Error("Onboarding_ProgressFailed");
            if (configuration.HotkeyRegistered != true)
            {
                var change = MakeButton("Onboarding_ChangeShortcut", "OnboardingChangeShortcut", "change-shortcut");
                change.HorizontalAlignment = HorizontalAlignment.Left; change.Margin = new(0, 12, 0, 0);
                body.Children.Add(change);
                if (!completed) Error("Onboarding_HotkeyFailed");
            }
            completion = new StackPanel { Visibility = completed ? Visibility.Visible : Visibility.Collapsed, Margin = new(0, 20, 0, 0) };
            automatic = new CheckBox { Content = UiText.Get("Onboarding_Automatic"), IsChecked = configuration.Automatic,
                Style = (Style)FindResource("Switch") };
            Identify(automatic, "OnboardingAutomatic", UiText.Get("Onboarding_Automatic"));
            automatic.Click += (_, _) => { if (!updating) _ = PreferenceAsync("set-automatic", automatic: automatic.IsChecked == true); };
            var automaticRow = new DockPanel(); DockPanel.SetDock(automatic, Dock.Right);
            automaticRow.Children.Add(automatic);
            var automaticLabel = ActionUi.Text(UiText.Get("Onboarding_Automatic"), 14);
            automaticLabel.Margin = new(0, 0, 12, 0); automaticRow.Children.Add(automaticLabel);
            completion.Children.Add(automaticRow);
            var tray = ActionUi.Text(UiText.Get("Onboarding_Tray"), 13, ActionUi.Muted); tray.Margin = new(0, 14, 0, 14);
            completion.Children.Add(tray);
            var buttons = new WrapPanel();
            buttons.Children.Add(MakeButton("Onboarding_Finish", "OnboardingFinish", "finish", true));
            buttons.Children.Add(MakeButton("Onboarding_Settings", "OnboardingOpenSettings", "open-settings"));
            completion.Children.Add(buttons); body.Children.Add(completion);
            skipButton = MakeButton("Onboarding_Skip", "OnboardingSkip", "skip");
            skipButton.HorizontalAlignment = HorizontalAlignment.Left; skipButton.Margin = new(0, 20, 0, 0);
            skipButton.Visibility = completed ? Visibility.Collapsed : Visibility.Visible;
            body.Children.Add(skipButton);
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            SetPending();
        }
        finally { updating = false; }
    }

    private Button MakeButton(string key, string id, string kind, bool primary = false)
    {
        var button = ActionUi.Button(UiText.Get(key), () => _ = RequestAsync(new(1, kind)), primary: primary);
        Identify(button, id, UiText.Get(key)); return button;
    }
    private static void Identify(DependencyObject control, string id, string name)
    { AutomationProperties.SetAutomationId(control, id); AutomationProperties.SetName(control, name); }
    private void Error(string key)
    { progress.Text = UiText.Get(key); progress.Foreground = Brushes.Firebrick; AutomationProperties.SetName(progress, progress.Text); }
    private void SetPending() { language.IsEnabled = automatic.IsEnabled = pending == 0; }
    private async Task PreferenceAsync(string kind, string? value = null, bool? automatic = null)
    {
        if (pending != 0) return;
        pending = ++request; SetPending();
        await RequestAsync(new(1, kind, Language: value, RequestId: pending, Automatic: automatic));
    }
    private async Task RequestAsync(OnboardingMessage message)
    {
        try { await send(message); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        { CloseFromParent(); }
    }
    internal void CloseFromParent() { closing = true; Close(); }

    internal static int Run(string pipeName)
    {
        if (!pipeName.StartsWith("SnapBar.Onboarding.", StringComparison.Ordinal)
            || !Guid.TryParseExact(pipeName[19..], "N", out _)) return 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var lifetime = new CancellationTokenSource();
            OnboardingPracticeWindow? window = null;
            try
            {
                await pipe.ConnectAsync(5000, lifetime.Token);
                using var channel = new OnboardingChannel(pipe, fromChild: false);
                window = new(message => channel.SendAsync(message, lifetime.Token));
                app.MainWindow = window;
                window.Closed += (_, _) => lifetime.Cancel();
                var handle = new WindowInteropHelper(window).EnsureHandle();
                await channel.SendAsync(new(1, "ready", Window: (long)handle), lifetime.Token);
                while (await channel.ReadAsync(lifetime.Token) is { } message) window.Receive(message);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException
                or System.Text.Json.JsonException or InvalidOperationException) { }
            finally { window?.CloseFromParent(); app.Shutdown(); }
        };
        return app.Run();
    }
}
