using System.Windows;
using System.Windows.Controls;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    private Window? translationSettingsWindow;

    private void ShowTranslationSettings()
    {
        if (exitRequested) return;
        if (translationSettingsWindow is { } existing) { existing.Activate(); return; }
        var saved = translation.Load();
        var window = new Window { ShowInTaskbar = !IsVisible };
        ActionUi.Setup(window, "翻譯服務設定 · SnapBar", 470, 390);
        window.MinHeight = 390;
        window.SizeToContent = SizeToContent.Height;
        if (IsVisible) { window.Owner = this; window.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        var panel = new StackPanel { Margin = new(22) };
        panel.Children.Add(ActionUi.Heading("Azure Translator", 19));
        var description = ActionUi.Text("執行翻譯時會將所選文字送至 Azure Translator。用量計入你自己的 Azure 資源。", 13, ActionUi.Muted);
        description.TextWrapping = TextWrapping.Wrap;
        description.Margin = new(0, 6, 0, 12);
        panel.Children.Add(description);

        var key = new PasswordBox { MaxLength = 256 };
        var region = new TextBox { Text = saved?.Region ?? "", MaxLength = 64 };
        var languages = new (string Code, string Label)[] { ("zh-Hant", "繁體中文"), ("en", "英文"),
            ("ja", "日文"), ("ko", "韓文"), ("zh-Hans", "簡體中文") };
        var target = new ComboBox { ItemsSource = languages.Select(language => language.Label),
            SelectedIndex = Math.Max(0, Array.FindIndex(languages, language => language.Code == saved?.Target)) };
        void AddField(string title, Control input)
        {
            var label = new Label { Content = title, Target = input, Padding = new(0, 5, 0, 4) };
            System.Windows.Automation.AutomationProperties.SetLabeledBy(input, label);
            panel.Children.Add(label);
            panel.Children.Add(input);
        }
        AddField(saved == null ? "資源金鑰" : "資源金鑰（留空沿用已儲存的金鑰）", key);
        AddField("資源區域（全域資源可留空）", region);
        AddField("目標語言", target);

        var status = ActionUi.Text("", 13);
        status.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(status);
        var buttons = new WrapPanel { Margin = new(0, 12, 0, 0) };
        void Save(TranslationSettings? value)
        {
            try { translation.Save(value); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            { status.Text = "無法儲存翻譯設定。請檢查金鑰格式、區域與設定檔權限。"; return; }
            window.Close();
        }
        buttons.Children.Add(ActionUi.Button("儲存", () =>
        {
            var value = key.Password.Trim();
            Save(new(value.Length == 0 ? saved?.Key ?? "" : value, region.Text.Trim(), languages[target.SelectedIndex].Code));
        }, primary: true));
        buttons.Children.Add(ActionUi.Button("取消", window.Close));
        if (saved != null) buttons.Children.Add(ActionUi.Button("清除翻譯設定", () => Save(null)));
        panel.Children.Add(buttons);
        window.Content = panel;
        translationSettingsWindow = window;
        window.Closed += (_, _) => translationSettingsWindow = null;
        window.Show();
    }
}
