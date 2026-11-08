using System.Windows;
using System.Windows.Controls;
using SnapBar.Core;
using SnapBar.Windows;
using SnapBar.Windows.Ocr;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    private readonly List<OcrOverlay> ocrOverlays = [];
    private CancellationTokenSource? ocrWork;
    private Window? ocrResultWindow;

    private void SetOcrLanguagePriority(bool preferEnglish)
    {
        if (exitRequested || preferences.OcrPreferEnglish == preferEnglish) return;
        preferences = preferences with { OcrPreferEnglish = preferEnglish };
        SavePreferences();
    }

    private void StartOcr()
    {
        if (exitRequested || ocrOverlays.Count > 0 || ocrWork != null) return;
        controller?.CancelSelection();
        ocrResultWindow?.Close();
        var monitors = ScreenOcr.Monitors();
        Native.GetCursorPos(out var cursor);
        OcrOverlay? active = null;
        foreach (var monitor in monitors)
        {
            var overlay = new OcrOverlay(monitor, region => _ = CompleteOcrAsync(region));
            ocrOverlays.Add(overlay);
            overlay.Closed += (_, _) =>
            {
                if (ocrOverlays.Remove(overlay)) CloseOcrOverlays();
            };
            if (!overlay.ShowOnMonitor()) { CloseOcrOverlays(); return; }
            if (cursor.X >= monitor.X && cursor.X < monitor.Right && cursor.Y >= monitor.Y && cursor.Y < monitor.Bottom)
                active = overlay;
        }
        (active ?? ocrOverlays.FirstOrDefault())?.ActivateCapture();
    }

    private void CloseOcrOverlays()
    {
        var overlays = ocrOverlays.ToArray();
        ocrOverlays.Clear();
        foreach (var overlay in overlays) overlay.Close();
    }

    private void StopOcr()
    {
        CloseOcrOverlays();
        ocrWork?.Cancel();
        ocrResultWindow?.Close();
    }

    private async Task CompleteOcrAsync(ScreenRect? region)
    {
        if (ocrOverlays.Count == 0) return;
        CloseOcrOverlays();
        if (region == null || exitRequested) return;
        using var cancellation = new CancellationTokenSource();
        ocrWork = cancellation;
        try
        {
            // Let the desktop repaint after the overlays have closed before reading pixels.
            await Task.Delay(100, cancellation.Token);
            var result = await ScreenOcr.CaptureAsync(region, cancellation.Token, preferences.OcrPreferEnglish);
            if (exitRequested || cancellation.IsCancellationRequested) return;
            ShowOcrResult(result);
        }
        catch (OperationCanceledException) { }
        finally { ocrWork = null; }
    }

    private void ShowOcrResult(OcrResult result)
    {
        var language = UiText.Capture();
        var window = new Window();
        ActionUi.Setup(window, language.Get("MainWindow_Ocr_000"), 560, 410);
        window.MinHeight = 300;
        ocrResultWindow = window;
        window.Closed += (_, _) =>
        {
            ocrResultWindow = null;
        };
        var body = new DockPanel { Margin = new(18) };
        window.Content = body;
        var status = ActionUi.Text(result.Status switch
        {
            "ok" => language.Get("MainWindow_Ocr_001"),
            "empty" => language.Get("MainWindow_Ocr_002"),
            "runtime" => language.Get("MainWindow_Ocr_003"),
            "models" => language.Get("MainWindow_Ocr_004"),
            "region" => language.Get("MainWindow_Ocr_005"),
            _ => language.Get("MainWindow_Ocr_006")
        }, 13, ActionUi.Muted);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new(0, 0, 0, 10);
        DockPanel.SetDock(status, Dock.Top);
        body.Children.Add(status);
        var buttons = new WrapPanel { Margin = new(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);
        if (result.Text is { } text)
        {
            var editor = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(8) };
            body.Children.Add(editor);
            string SelectedText() => editor.SelectionLength > 0 ? editor.SelectedText : editor.Text;
            buttons.Children.Add(ActionUi.Button(language.Get("MainWindow_Ocr_007"), () =>
            {
                var selected = SelectedText();
                if (string.IsNullOrWhiteSpace(selected)) return;
                try { Clipboard.SetText(selected); status.Text = language.Get("MainWindow_Ocr_008"); }
                catch (System.Runtime.InteropServices.ExternalException) { status.Text = language.Get("MainWindow_Ocr_009"); }
            }, "copy"));
            buttons.Children.Add(ActionUi.Button(language.Get("MainWindow_Ocr_010"), () =>
            {
                var destination = WebActionPolicy.Search(SelectedText(), preferences.SearchUrlTemplate);
                if (!destination.Success) { status.Text = language.Get("MainWindow_Ocr_011"); return; }
                try { BrowserLauncher.Open(destination.Uri!); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or ArgumentException)
                { status.Text = language.Get("MainWindow_Ocr_012"); }
            }, "search"));
        }
        buttons.Children.Add(ActionUi.Button(language.Get("MainWindow_Ocr_013"), window.Close));
        window.Show();
    }
}
