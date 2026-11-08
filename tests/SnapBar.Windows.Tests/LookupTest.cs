using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class LookupTest
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();

    internal static string WriteDictionary(string folder, string filename, string name, string text)
    {
        var ifo = Path.Combine(folder, filename + ".ifo");
        var definition = System.Text.Encoding.UTF8.GetBytes(text);
        var index = new byte[FixtureWindow.LookupFixtureText.Length + 9];
        System.Text.Encoding.UTF8.GetBytes(FixtureWindow.LookupFixtureText).CopyTo(index, 0);
        BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(index.Length - 4), (uint)definition.Length);
        File.WriteAllText(ifo, $"StarDict's dict ifo file\nversion=2.4.2\nbookname={name}\nwordcount=1\nidxfilesize={index.Length}\nsametypesequence=m\n", new System.Text.UTF8Encoding(false));
        File.WriteAllBytes(Path.ChangeExtension(ifo, ".idx"), index);
        File.WriteAllBytes(Path.ChangeExtension(ifo, ".dict"), definition);
        return ifo;
    }

    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var passed = 0;
        var result = 1;

        void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
            passed++;
        }

        app.Startup += async (_, _) =>
        {
            Process? child = null;
            try
            {
                using var owner = new HwndSource(new HwndSourceParameters("SnapBar lookup test")
                    { Width = 1, Height = 1, WindowStyle = 0 });
                var start = new ProcessStartInfo(TestPaths.AppExecutable) { UseShellExecute = false };
                start.ArgumentList.Add("--fixture");
                start.ArgumentList.Add("--web-lookup");
                start.Environment["SNAPBAR_LOOKUP_TEST_RUNNER_PID"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                child = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not start");
                Console.WriteLine("WAITING: activate the dedicated lookup fixture (up to 60 seconds)");
                var activationDeadline = DateTime.UtcNow.AddSeconds(60);
                while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != child.Id)
                {
                    if (child.HasExited || DateTime.UtcNow > activationDeadline)
                        throw new InvalidOperationException("Lookup fixture must be foreground");
                    await Task.Delay(25);
                }
                await Task.Delay(100);

                void WithClipboard(Action operation)
                {
                    for (var attempt = 0; attempt < 20; attempt++)
                    {
                        if (!OpenClipboard(owner.Handle)) { Thread.Sleep(15); continue; }
                        try { operation(); } finally { CloseClipboard(); }
                        return;
                    }
                    throw new InvalidOperationException("Clipboard busy");
                }

                var folder = Directory.CreateTempSubdirectory("snapbar-lookup-");
                try
                {
                    var dictionary = new OfflineDictionaryLibrary(Path.Combine(folder.FullName, "stored.bin"));
                    using var lookup = new SelectionController(owner, TestPaths.ProbeExecutable) { Dictionary = dictionary };
                    var settingsRequested = false;
                    lookup.OpenDictionarySettings = () => settingsRequested = true;
                    lookup.ConfigureActions(ActionCatalog.SetPlacement(new(), "lookup", ActionPlacement.Bar));

                    async Task<ActionResultWindow> RunLookup()
                    {
                        await lookup.CaptureAsync(false, 0, 0);
                        var popup = app.Windows.OfType<PopupWindow>().Single(window => window.IsVisible);
                        var button = popup.FindActionButton(TextAction.Lookup);
                        if (!button.IsVisible || !button.IsEnabled) throw new InvalidOperationException("Lookup button is unavailable");
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var deadline = DateTime.UtcNow.AddSeconds(4);
                        while (DateTime.UtcNow < deadline)
                        {
                            if (app.Windows.OfType<ActionResultWindow>().FirstOrDefault(window => window.IsVisible) is { } resultWindow)
                                return resultWindow;
                            await Task.Delay(20);
                        }
                        throw new InvalidOperationException("Lookup result did not open");
                    }

                    static T[] Elements<T>(DependencyObject root) where T : DependencyObject =>
                        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(Elements<T>)
                            .Concat(root is T element ? [element] : []).ToArray();

                    var missing = await RunLookup();
                    TextBlock LookupContent(ActionResultWindow window) =>
                        Elements<TextBlock>(window).Single(text => text.Name == "LookupContent");

                    var missingText = LookupContent(missing);
                    Require(Elements<Button>(missing).Length == 3 && AutomationProperties.GetName(Elements<Button>(missing)[0]) == "匯入詞典"
                        && missingText.Text.Contains("請先匯入")
                        && missingText.ActualHeight >= missingText.DesiredSize.Height
                        && Elements<ScrollViewer>(missing).Length == 0,
                        "lookup without a dictionary shows a settings entry");
                    Elements<Button>(missing)[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(settingsRequested && !missing.IsVisible, "lookup settings entry opens management and closes the message");
                    const string rawDefinition = "take off\n起飛";
                    var ifo = WriteDictionary(folder.FullName, "test", "Fixture", rawDefinition);
                    dictionary.Import(ifo);

                    var found = await RunLookup();
                    Require(LookupContent(found).Inlines.OfType<System.Windows.Documents.Run>().Single().Text == "起飛",
                        "readonly lookup shows the definition without a duplicate headword");
                    Require(found.ActualHeight < 250 && !found.ShowInTaskbar && found.WindowStyle == WindowStyle.None,
                        $"readonly lookup shows a compact result (height {found.ActualHeight:F0} DIP)");
                    Require(Native.GetForegroundWindow() == child!.MainWindowHandle,
                        "lookup preserves source foreground");
                    UiAppearanceTest.Snapshot(found, "lookup-result.png");
                    using var lookupBackup = new ClipboardBackup();
                    WithClipboard(() => { if (lookupBackup.Capture() != "ok") throw new InvalidOperationException("Clipboard backup failed"); });
                    try
                    {
                        Elements<Button>(found)[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Require(Clipboard.GetText() == "【Fixture】\n" + rawDefinition, "lookup copies the definition with its dictionary source");
                    }
                    finally { WithClipboard(() => { if (!lookupBackup.Restore()) throw new InvalidOperationException("Clipboard restore failed"); }); }
                    found.Close();
                    var second = dictionary.Import(WriteDictionary(folder.FullName, "second", "航空專業詞典", "take off\n離地起飛；開始飛行"));
                    found = await RunLookup();
                    Require(Elements<TextBlock>(found).Where(text => AutomationProperties.GetAutomationId(text) == "LookupDictionarySource")
                        .Select(text => text.Text).SequenceEqual(["Fixture", "航空專業詞典"])
                        && Elements<TextBlock>(found).Count(text => text.Name == "LookupContent") == 2
                        && Elements<ScrollViewer>(found).Length == 1,
                        "lookup groups enabled dictionary results by source in one scroll region");
                    UiAppearanceTest.Snapshot(found, "lookup-multiple-dictionaries.png");
                    using var multiBackup = new ClipboardBackup();
                    WithClipboard(() => { if (multiBackup.Capture() != "ok") throw new InvalidOperationException("Clipboard backup failed"); });
                    try
                    {
                        Elements<Button>(found)[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Require(Clipboard.GetText() == "【Fixture】\n" + rawDefinition + "\n\n【航空專業詞典】\ntake off\n離地起飛；開始飛行",
                            "lookup copies every matched definition with its source heading");
                    }
                    finally { WithClipboard(() => { if (!multiBackup.Restore()) throw new InvalidOperationException("Clipboard restore failed"); }); }
                    found.Close();
                    dictionary.SetEnabled(second.Id, false);
                    found = await RunLookup();
                    Require(Elements<TextBlock>(found).Count(text => AutomationProperties.GetAutomationId(text) == "LookupDictionarySource") == 1,
                        "lookup excludes a disabled dictionary");
                    found.Close();
                    var first = dictionary.Dictionaries[0];
                    dictionary.SetEnabled(first.Id, false);
                    var disabled = await RunLookup();
                    Require(LookupContent(disabled).Text.Contains("尚未啟用")
                        && Elements<Button>(disabled).Any(button => AutomationProperties.GetName(button) == "管理詞典"),
                        "lookup offers management when all installed dictionaries are disabled");
                    disabled.Close();
                    dictionary.SetEnabled(first.Id, true);
                    dictionary.SetEnabled(second.Id, true);
                    found = await RunLookup();
                    var other = new Window { Title = "SnapBar lookup focus fixture", Width = 240, Height = 120 };
                    try
                    {
                        var pin = Elements<Button>(found).Single(button => AutomationProperties.GetAutomationId(button) == "PinLookupResult");
                        pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        other.ShowActivated = false;
                        other.Show();
                        var otherHandle = new WindowInteropHelper(other).Handle;
                        void SwitchToOther()
                        {
                            Native.SetForegroundWindow(otherHandle);
                            if (Native.GetForegroundWindow() != otherHandle)
                                throw new InvalidOperationException("Could not move foreground to the lookup focus fixture");
                        }
                        SwitchToOther();
                        await Task.Delay(250);
                        Require(found.IsVisible && found.IsLookupPinned && AutomationProperties.GetName(pin) == "取消釘住",
                            "pinned lookup survives a foreground change");
                        UiAppearanceTest.Snapshot(found, "lookup-pinned.png");
                        pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Require(!found.IsLookupPinned, "lookup can be unpinned");
                        Require(Native.SetForegroundWindow(child.MainWindowHandle), "restore source for unpinned lookup");
                        found = await RunLookup();
                        SwitchToOther();
                        var deadline = DateTime.UtcNow.AddSeconds(2);
                        while (found.IsVisible && DateTime.UtcNow < deadline) await Task.Delay(25);
                        Require(!found.IsVisible, "lookup result closes when the source loses foreground");
                        Require(Native.SetForegroundWindow(child.MainWindowHandle), "restore source for lookup cancellation check");
                        await lookup.CaptureAsync(false, 0, 0);
                        var pending = lookup.ExecuteAsync(TextAction.Lookup);
                        SwitchToOther();
                        await pending;
                        Require(!app.Windows.OfType<ActionResultWindow>().Any(window => window.IsVisible),
                            "focus switch during lookup suppresses the stale result");
                    }
                    finally { other.Close(); }
                }
                finally { folder.Delete(recursive: true); }

                result = 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); }
            finally
            {
                if (child != null) { if (!child.HasExited) child.CloseMainWindow(); child.Dispose(); }
                Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed");
                app.Shutdown();
            }
        };
        app.Run();
        return result;
    }
}
