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
        pageHeading.Text = UiText.Get("MainWindow_Dictionary_000");
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
        var back = ActionUi.Button(UiText.Get("MainWindow_Dictionary_001"), () =>
        {
            ShowActionList();
            actionSettingsPanel.FocusDictionaryEditor();
        });
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new(0, 0, 0, 16);
        AutomationProperties.SetAutomationId(back, "BackFromDictionary");
        header.Children.Add(back);
        var description = ActionUi.Text(UiText.Get("MainWindow_Dictionary_002"), 13, ActionUi.Muted);
        description.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(description);
        dictionaryImport = ActionUi.Button(UiText.Get("MainWindow_Dictionary_003"), () => { });
        dictionaryImport.HorizontalAlignment = HorizontalAlignment.Left;
        dictionaryImport.Margin = new(0, 12, 0, 8);
        AutomationProperties.SetAutomationId(dictionaryImport, "ImportLookupDictionary");
        dictionaryImport.Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = UiText.Get("MainWindow_Dictionary_004"), CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            var path = picker.FileName;
            await UpdateDictionaryAsync(async () =>
            {
                InstalledDictionary entry;
                try { entry = await Task.Run(() => offlineDictionary.Import(path)); }
                catch (DuplicateDictionaryException duplicate)
                {
                    if (MessageBox.Show(this, UiText.Get("MainWindow_Dictionary_005",(duplicate.Existing.Name),(duplicate.Existing.EntryCount).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)),
                        UiText.Get("MainWindow_Dictionary_006"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                        return UiText.Get("MainWindow_Dictionary_007");
                    entry = await Task.Run(() => offlineDictionary.Import(path, duplicate.Existing.Id));
                }
                return UiText.Get("MainWindow_Dictionary_008",(entry.Name),(entry.EntryCount).ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
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
            dictionaryStatus.Text = entries.Count == 0 ? UiText.Get("MainWindow_Dictionary_009") : UiText.Get("MainWindow_Dictionary_010",(entries.Count),(entries.Count(item => item.Enabled)));
            foreach (var entry in entries)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new());
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                AutomationProperties.SetAutomationId(row, "DictionaryRow_" + entry.Id);
                var enabled = new CheckBox { IsChecked = entry.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) };
                AutomationProperties.SetName(enabled, UiText.Get("Dictionary_EnableName", entry.Name));
                AutomationProperties.SetAutomationId(enabled, "EnableDictionary_" + entry.Id);
                enabled.Click += async (_, _) =>
                {
                    var value = enabled.IsChecked == true;
                    await UpdateDictionaryAsync(() => Task.Run(() =>
                    {
                        offlineDictionary.SetEnabled(entry.Id, value);
                        return value ? UiText.Get("MainWindow_Dictionary_012") : UiText.Get("MainWindow_Dictionary_013");
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
                details.Children.Add(ActionUi.Text(UiText.Get("MainWindow_Dictionary_014",(entry.EntryCount).ToString("N0", System.Globalization.CultureInfo.InvariantCulture),((entry.Enabled ? UiText.Get("MainWindow_Dictionary_024") : UiText.Get("MainWindow_Dictionary_025")))), 12, ActionUi.Muted));
                Grid.SetColumn(details, 1);
                row.Children.Add(details);
                var remove = ActionUi.Button(UiText.Get("MainWindow_Dictionary_015"), () => { });
                AutomationProperties.SetName(remove, UiText.Get("Dictionary_RemoveName", entry.Name));
                AutomationProperties.SetAutomationId(remove, "RemoveDictionary_" + entry.Id);
                remove.Click += async (_, _) =>
                {
                    if (MessageBox.Show(this, UiText.Get("MainWindow_Dictionary_017",(entry.Name)), UiText.Get("MainWindow_Dictionary_018"),
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    await UpdateDictionaryAsync(() => Task.Run(() => { offlineDictionary.Remove(entry.Id); return UiText.Get("MainWindow_Dictionary_019"); }));
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
        { dictionaryStatus.Text = UiText.Get("MainWindow_Dictionary_020"); }
    }

    private async Task UpdateDictionaryAsync(Func<Task<string>> operation)
    {
        if (dictionaryBusy) return;
        dictionaryBusy = true;
        dictionaryImport.IsEnabled = dictionaryList.IsEnabled = false;
        dictionaryStatus.Text = UiText.Get("MainWindow_Dictionary_021");
        string message;
        try { message = await operation(); }
        catch (InvalidDataException e) { message = ValidationMessages.Describe(e); }
        catch (DecoderFallbackException) { message = UiText.Get("MainWindow_Dictionary_022"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { message = UiText.Get("MainWindow_Dictionary_023"); }
        catch (InvalidOperationException e) { message = ValidationMessages.Describe(e); }
        finally
        {
            dictionaryBusy = false;
            dictionaryImport.IsEnabled = dictionaryList.IsEnabled = true;
            RefreshDictionaryList();
        }
        dictionaryStatus.Text = UiText.Join(dictionaryStatus.Text, " ", message);
    }
}
