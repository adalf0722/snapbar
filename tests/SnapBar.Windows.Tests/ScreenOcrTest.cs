using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;
using SnapBar.Windows.Ocr;

internal static class ScreenOcrTest
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int passed = 0, exit = 1;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
            passed++;
        }

        app.Startup += async (_, _) =>
        {
            var path = Path.Combine(Path.GetTempPath(), "SnapBar.Ocr.Tests." + Guid.NewGuid().ToString("N") + ".json");
            var store = new SettingsStore(path);
            var hotkey = new HotkeySettings(3, 0x55);
            store.Load();
            store.Save(new() { OcrHotkey = hotkey });
            var content = new StackPanel { Background = Brushes.White };
            content.Children.Add(new TextBlock { Text = "SnapBar OCR test 123", FontFamily = new("Segoe UI"),
                FontSize = 45, Margin = new(35, 25, 0, 8), Foreground = Brushes.Black });
            content.Children.Add(new TextBlock { Text = "繁體中文辨識測試", FontFamily = new("Microsoft JhengHei"),
                FontSize = 45, Margin = new(35, 4, 0, 0), Foreground = Brushes.Black });
            var fixture = new Window { Title = "SnapBar OCR desktop fixture", Content = content, Width = 700, Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen };
            MainWindow? snapBar = null;
            try
            {
                snapBar = new MainWindow(store, () => { }, () => false);
                snapBar.InitializeBackground();
                var hwnd = new WindowInteropHelper(snapBar).Handle;
                fixture.Show();
                fixture.Activate();
                var fixtureHandle = new WindowInteropHelper(fixture).Handle;
                Console.WriteLine("WAITING: activate the dedicated SnapBar OCR desktop fixture (up to 60 seconds)");
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (Native.GetForegroundWindow() != fixtureHandle && DateTime.UtcNow < deadline) await Task.Delay(25);
                Check(Native.GetForegroundWindow() == fixtureHandle, "desktop OCR fixture owns foreground");

                void Trigger() => SendMessage(hwnd, Native.WM_HOTKEY, 9, (hotkey.Key << 16) | hotkey.Modifiers);
                async Task Ready()
                {
                    var until = DateTime.UtcNow.AddSeconds(5);
                    while (!snapBar.OcrCaptureReady && DateTime.UtcNow < until) await Task.Delay(15);
                }
                Trigger();
                await Ready();
                Check(app.Windows.OfType<OcrOverlay>().Count(window => window.IsVisible) == ScreenOcr.Monitors().Count,
                    "OCR hotkey opens a capture overlay on each monitor");
                await Task.Delay(100);
                keybd_event(0x1B, 0, 0, 0);
                keybd_event(0x1B, 0, 2, 0);
                await Task.Delay(100);
                Check(!app.Windows.OfType<OcrOverlay>().Any(window => window.IsVisible)
                    && !app.Windows.OfType<Window>().Any(window => window.Title == "框選 OCR · SnapBar"),
                    "Escape cancels capture without opening results");

                var recognize = snapBar.OcrRecognize;
                snapBar.OcrRecognize = async (png, english, token) =>
                {
                    await Task.Delay(450, token);
                    return await recognize(png, english, token);
                };
                Trigger();
                await Ready();
                var from = content.PointToScreen(new(12, 12));
                var to = content.PointToScreen(new(content.ActualWidth - 12, 190));
                Check(SetCursorPos((int)from.X, (int)from.Y), "drag starts over the disposable image");
                await Task.Delay(50);
                mouse_event(2, 0, 0, 0, 0);
                try
                {
                    await Task.Delay(50);
                    Check(SetCursorPos((int)to.X, (int)to.Y), "drag ends over the disposable image");
                    await Task.Delay(80);
                }
                finally { mouse_event(4, 0, 0, 0, 0); }

                Window? result = null;
                var sawProcessing = false;
                OcrResultWindow? processingCard = null;
                var release = DateTime.UtcNow;
                deadline = DateTime.UtcNow.AddSeconds(8);
                while (result == null && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);
                    var processing = app.Windows.OfType<OcrResultWindow>().SingleOrDefault(window => window.IsProcessing);
                    if (!sawProcessing && processing != null && DateTime.UtcNow - release >= TimeSpan.FromMilliseconds(330))
                    {
                        UiAppearanceTest.Snapshot(processing, "ocr-processing-card.png");
                        processingCard = processing;
                        sawProcessing = true;
                    }
                    result = app.Windows.OfType<OcrResultWindow>().SingleOrDefault(window => !window.IsProcessing);
                }
                Check(result != null && !app.Windows.OfType<OcrOverlay>().Any(window => window.IsVisible),
                    "drag closes capture overlays and displays OCR results");
                Check(sawProcessing && ReferenceEquals(processingCard, result),
                    "recognition exposes processing feedback before transitioning in place");
                var body = (DockPanel)result!.Content;
                var editor = body.Children.OfType<TextBox>().SingleOrDefault();
                if (editor == null) throw new InvalidOperationException(body.Children.OfType<TextBlock>().Single().Text);
                var normalized = string.Concat(editor.Text.Where(c => !char.IsWhiteSpace(c)));
                Check(normalized == "SnapBarOCRtest123繁體中文辨識測試", "actual desktop crop recognizes English and Traditional Chinese");
                Check(!editor.IsReadOnly, "recognized text can be corrected without modifying the source");
                var buttons = body.Children.OfType<WrapPanel>().Single().Children.OfType<Button>()
                    .Where(button => button.IsVisible)
                    .Select(System.Windows.Automation.AutomationProperties.GetName).ToArray();
                Check(buttons.SequenceEqual(new[] { "複製文字", "重新框選", "搜尋" }),
                    "OCR result offers text actions without a write-back action");
                UiAppearanceTest.Snapshot(result, "ocr-result-card.png");
                result.Close();
                exit = 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); }
            finally
            {
                if (snapBar != null) { snapBar.PrepareSessionEnd(); snapBar.Close(); }
                fixture.Close();
                File.Delete(path);
                Console.WriteLine($"{passed} passed, {(exit == 0 ? 0 : 1)} failed");
                app.Shutdown();
            }
        };
        app.Run();
        return exit;
    }
}
