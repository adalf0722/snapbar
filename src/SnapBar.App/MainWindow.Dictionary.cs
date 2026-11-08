using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.Core;
using Microsoft.Win32;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    private readonly OfflineDictionaryLibrary offlineDictionary;
    private FrameworkElement dictionaryEditor = null!;
    private Button dictionaryImport = null!;
    private StackPanel dictionaryList = null!;
    private TextBlock dictionaryStatus = null!;
    private bool dictionaryBusy;
    internal OfflineDictionaryLibrary DictionaryLibrary => offlineDictionary;

    internal void ShowDictionarySettings()
    {
        if (exitRequested) return;
        ShowSettings();
        NavigateTo(SettingsCategory.Actions);
        actionSettingsPanel.Visibility = searchEditor.Visibility = Visibility.Collapsed;
        dictionaryEditor.Visibility = Visibility.Visible;
        pageHeading.Text = "管理離線詞典";
        if (!dictionaryBusy) RefreshDictionaryList();
        dictionaryImport.Focus();
    }

    private FrameworkElement BuildDictionaryEditor()
    {
        var page = new DockPanel();
        AutomationProperties.SetAutomationId(page, "DictionaryEditor");
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        page.Children.Add(header);
        var back = ActionUi.Button("← 返回動作列", () =>
        {
            ShowActionList();
            actionSettingsPanel.FocusDictionaryEditor();
        });
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new(0, 0, 0, 16);
        AutomationProperties.SetAutomationId(back, "BackFromDictionary");
        header.Children.Add(back);
        var description = ActionUi.Text("匯入多本詞典，查詞時會一起搜尋已啟用的來源。請將 .ifo、.idx 與 .dict 或 .dict.dz 放在同一資料夾。", 13, ActionUi.Muted);
        description.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(description);
        dictionaryImport = ActionUi.Button("匯入詞典", () => { });
        dictionaryImport.HorizontalAlignment = HorizontalAlignment.Left;
        dictionaryImport.Margin = new(0, 12, 0, 8);
        AutomationProperties.SetAutomationId(dictionaryImport, "ImportLookupDictionary");
        dictionaryImport.Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "StarDict 詞典 (*.ifo)|*.ifo", CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            var path = picker.FileName;
            await UpdateDictionaryAsync(async () =>
            {
                InstalledDictionary entry;
                try { entry = await Task.Run(() => offlineDictionary.Import(path)); }
                catch (DuplicateDictionaryException duplicate)
                {
                    if (MessageBox.Show(this, $"「{duplicate.Existing.Name}」已匯入（{duplicate.Existing.EntryCount:N0} 詞條）。要以這份檔案更新嗎？\n啟用狀態會保留。",
                        "更新詞典", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                        return "已保留原有詞典。";
                    entry = await Task.Run(() => offlineDictionary.Import(path, duplicate.Existing.Id));
                }
                return $"已匯入「{entry.Name}」，{entry.EntryCount:N0} 詞條。";
            });
        };
        header.Children.Add(dictionaryImport);
        dictionaryStatus = ActionUi.Text("", 13, ActionUi.Muted);
        dictionaryStatus.TextWrapping = TextWrapping.Wrap;
        dictionaryStatus.Margin = new(0, 0, 0, 12);
        AutomationProperties.SetAutomationId(dictionaryStatus, "DictionaryLibraryStatus");
        header.Children.Add(dictionaryStatus);
        dictionaryList = new StackPanel();
        AutomationProperties.SetAutomationId(dictionaryList, "InstalledDictionaries");
        page.Children.Add(new ScrollViewer { Content = dictionaryList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        RefreshDictionaryList();
        return page;
    }

    private void RefreshDictionaryList()
    {
        dictionaryList.Children.Clear();
        try
        {
            var entries = offlineDictionary.Dictionaries;
            dictionaryStatus.Text = entries.Count == 0 ? "尚未匯入詞典。" : $"已匯入 {entries.Count} 本詞典，已啟用 {entries.Count(item => item.Enabled)} 本。";
            foreach (var entry in entries)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new());
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                AutomationProperties.SetAutomationId(row, "DictionaryRow_" + entry.Id);
                var enabled = new CheckBox { IsChecked = entry.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) };
                AutomationProperties.SetName(enabled, "啟用「" + entry.Name + "」");
                AutomationProperties.SetAutomationId(enabled, "EnableDictionary_" + entry.Id);
                enabled.Click += async (_, _) =>
                {
                    var value = enabled.IsChecked == true;
                    await UpdateDictionaryAsync(() => Task.Run(() =>
                    {
                        offlineDictionary.SetEnabled(entry.Id, value);
                        return value ? "已啟用詞典。" : "已停用詞典，檔案仍會保留。";
                    }));
                    dictionaryList.Children.OfType<Border>().Select(card => card.Child).OfType<Grid>()
                        .SelectMany(grid => grid.Children.OfType<CheckBox>())
                        .FirstOrDefault(box => AutomationProperties.GetAutomationId(box) == "EnableDictionary_" + entry.Id)?.Focus();
                };
                row.Children.Add(enabled);
                var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var name = ActionUi.Text(entry.Name, 14);
                name.TextWrapping = TextWrapping.Wrap;
                details.Children.Add(name);
                details.Children.Add(ActionUi.Text($"{entry.EntryCount:N0} 詞條 · {(entry.Enabled ? "已啟用" : "已停用")}", 12, ActionUi.Muted));
                Grid.SetColumn(details, 1);
                row.Children.Add(details);
                var remove = ActionUi.Button("移除", () => { });
                AutomationProperties.SetName(remove, "移除「" + entry.Name + "」");
                AutomationProperties.SetAutomationId(remove, "RemoveDictionary_" + entry.Id);
                remove.Click += async (_, _) =>
                {
                    if (MessageBox.Show(this, $"要移除「{entry.Name}」嗎？原始詞典檔案會保留。", "移除詞典",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    await UpdateDictionaryAsync(() => Task.Run(() => { offlineDictionary.Remove(entry.Id); return "已移除詞典。"; }));
                };
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                var card = ActionUi.Card(row, new Thickness(14));
                card.Margin = new(0, 0, 0, 8);
                dictionaryList.Children.Add(card);
            }
            actionSettingsPanel.ShowDictionaryStatus(entries.Count > 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        { dictionaryStatus.Text = "無法讀取詞典清單，請檢查檔案或還原備份。"; }
    }

    private async Task UpdateDictionaryAsync(Func<Task<string>> operation)
    {
        if (dictionaryBusy) return;
        dictionaryBusy = true;
        dictionaryImport.IsEnabled = dictionaryList.IsEnabled = false;
        dictionaryStatus.Text = "正在處理詞典…";
        string message;
        try { message = await operation(); }
        catch (InvalidDataException e) { message = e.Message; }
        catch (DecoderFallbackException) { message = "詞典文字必須使用 UTF-8。"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { message = "無法讀取或儲存詞典，請檢查配套檔案與存取權限。"; }
        catch (InvalidOperationException e) { message = e.Message; }
        finally
        {
            dictionaryBusy = false;
            dictionaryImport.IsEnabled = dictionaryList.IsEnabled = true;
            RefreshDictionaryList();
        }
        dictionaryStatus.Text += " " + message;
    }
}
