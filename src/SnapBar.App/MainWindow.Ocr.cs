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
        var window = new Window();
        ActionUi.Setup(window, "框選 OCR · SnapBar", 560, 410);
        window.MinHeight = 300;
        ocrResultWindow = window;
        var lifetime = new CancellationTokenSource();
        window.Closed += (_, _) =>
        {
            ocrResultWindow = null;
            lifetime.Cancel();
            lifetime.Dispose();
        };
        var body = new DockPanel { Margin = new(18) };
        window.Content = body;
        var status = ActionUi.Text(result.Status switch
        {
            "ok" => "可校正辨識文字，或反白其中一部分再執行動作。",
            "empty" => "沒有辨識到文字。請重新框選清楚的文字。",
            "runtime" => "OCR 引擎無法載入。請確認 Microsoft Visual C++ x64 執行階段已安裝。",
            "models" => "找不到 OCR 語言模型。請重新解壓完整套件。",
            "region" => "框選區域太大或超出螢幕。請在同一螢幕內重新框選。",
            _ => "無法辨識此區域，請重新框選。"
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
            buttons.Children.Add(ActionUi.Button("複製", () =>
            {
                var selected = SelectedText();
                if (string.IsNullOrWhiteSpace(selected)) return;
                try { Clipboard.SetText(selected); status.Text = "已複製。"; }
                catch (System.Runtime.InteropServices.ExternalException) { status.Text = "剪貼簿忙碌，請稍後再試。"; }
            }, "copy"));
            buttons.Children.Add(ActionUi.Button("搜尋", () =>
            {
                var destination = WebActionPolicy.Search(SelectedText(), preferences.SearchUrlTemplate);
                if (!destination.Success) { status.Text = "無法搜尋這段文字，請縮短內容後再試。"; return; }
                try { BrowserLauncher.Open(destination.Uri!); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or ArgumentException)
                { status.Text = "無法開啟預設瀏覽器。"; }
            }, "search"));
            Button translateButton = null!;
            translateButton = ActionUi.Button("翻譯", async () =>
            {
                var settings = translation.Load();
                if (settings == null) { ShowTranslationSettings(); return; }
                var selected = SelectedText();
                if (string.IsNullOrWhiteSpace(selected)) return;
                var token = lifetime.Token;
                translateButton.IsEnabled = false;
                status.Text = "正在使用 Azure Translator 翻譯…";
                try
                {
                    var translated = await translation.TranslateAsync(settings, selected, token);
                    if (token.IsCancellationRequested) return;
                    status.Text = translated.Text == null ? "翻譯未完成，請檢查文字長度、網路及翻譯設定。" : "譯文已顯示。";
                    if (translated.Text is { } output)
                    {
                        var resultWindow = new ActionResultWindow("Azure Translator · 譯文", output, false, false, compact: true)
                        { Owner = window };
                        resultWindow.Show();
                    }
                }
                catch (OperationCanceledException) { }
                finally { if (!token.IsCancellationRequested) translateButton.IsEnabled = true; }
            }, "translate");
            buttons.Children.Add(translateButton);
        }
        buttons.Children.Add(ActionUi.Button("關閉", window.Close));
        window.Show();
    }
}
