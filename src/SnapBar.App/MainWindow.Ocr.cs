using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapBar.Core;
using SnapBar.Windows;
using SnapBar.Windows.Ocr;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    private sealed class OcrSession : IDisposable
    {
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly List<OcrSnapshot> Snapshots = [];
        internal bool Recognizing, Disposed;
        internal void ReleaseImages() { foreach (var image in Snapshots) image.Dispose(); Snapshots.Clear(); }
        public void Dispose() { if (Disposed) return; Disposed = true; ReleaseImages(); Cancellation.Dispose(); }
    }
    private readonly List<OcrOverlay> ocrOverlays = [];
    private OcrSession? ocrSession;
    private Window? ocrResultWindow;
    internal Func<byte[], bool, CancellationToken, Task<OcrResult>> OcrRecognize { get; set; } =
        (png, english, token) => OcrWorker.RecognizeAsync(Path.Combine(AppContext.BaseDirectory, "SnapBar.App.exe"), png, english, token);
    internal bool OcrCaptureReady => ocrOverlays.Count > 0 && ocrOverlays.All(window => window.IsVisible);

    private void SetOcrLanguagePriority(bool preferEnglish)
    {
        if (exitRequested || preferences.OcrPreferEnglish == preferEnglish) return;
        preferences = preferences with { OcrPreferEnglish = preferEnglish }; SavePreferences();
    }
    private void StartOcr() => _ = StartOcrAsync();
    private async Task StartOcrAsync()
    {
        if (exitRequested || ocrSession != null) return;
        ocrResultWindow?.Close();
        var session = new OcrSession(); ocrSession = session;
        try
        {
            controller?.CancelSelection();
            foreach (Window window in Application.Current.Windows)
                if (window is ActionResultWindow or AiPanelWindow or PopupWindow) window.Hide();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(60, session.Cancellation.Token);
            var displays = new List<(OcrSnapshot Snapshot, BitmapSource Image)>();
            foreach (var monitor in ScreenOcr.Monitors())
            {
                session.Cancellation.Token.ThrowIfCancellationRequested();
                var snapshot = OcrSnapshot.Capture(monitor); session.Snapshots.Add(snapshot);
                var pixels = snapshot.CopyBgraPixels();
                try
                {
                    var image = BitmapSource.Create((int)monitor.Width, (int)monitor.Height, 96, 96, PixelFormats.Bgra32,
                        null, pixels, checked((int)monitor.Width * 4));
                    image.Freeze(); displays.Add((snapshot, image));
                }
                finally { Array.Clear(pixels); }
            }
            if (!Current(session)) return;
            Native.GetCursorPos(out var cursor);
            OcrOverlay? active = null;
            foreach (var display in displays)
            {
                var monitor = display.Snapshot.Bounds;
                var overlay = new OcrOverlay(monitor, display.Image,
                    region => _ = CompleteOcrAsync(session, display.Snapshot, region),
                    entered => { foreach (var item in ocrOverlays) item.ShowHelp(item == entered); });
                ocrOverlays.Add(overlay);
                overlay.Closed += (_, _) => { if (ocrOverlays.Contains(overlay)) CancelOcr(session); };
                if (!overlay.ShowOnMonitor()) throw new InvalidOperationException();
                if (cursor.X >= monitor.X && cursor.X < monitor.Right && cursor.Y >= monitor.Y && cursor.Y < monitor.Bottom) active = overlay;
            }
            active ??= ocrOverlays.FirstOrDefault();
            foreach (var overlay in ocrOverlays) overlay.ShowHelp(overlay == active);
            active?.ActivateCapture();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not StackOverflowException)
        {
            if (Current(session))
            {
                CloseOcrOverlays(); session.ReleaseImages();
                try { ShowSessionCard(session, null).SetResult(new("capture")); }
                finally { FinishOcr(session); }
            }
        }
        finally { if (!session.Disposed && session.Cancellation.IsCancellationRequested) FinishOcr(session); }
    }
    private bool Current(OcrSession session) => !exitRequested && ReferenceEquals(ocrSession, session) && !session.Disposed && !session.Cancellation.IsCancellationRequested;
    private void CloseOcrOverlays()
    {
        var overlays = ocrOverlays.ToArray(); ocrOverlays.Clear();
        foreach (var overlay in overlays) overlay.Close();
    }
    private void CancelOcr(OcrSession session)
    {
        if (!ReferenceEquals(ocrSession, session) || session.Disposed) return;
        session.Cancellation.Cancel(); CloseOcrOverlays(); session.ReleaseImages();
        // Retain the busy lease until the owned worker has exited.
        if (!session.Recognizing) FinishOcr(session);
    }
    private void FinishOcr(OcrSession session)
    {
        if (ReferenceEquals(ocrSession, session)) ocrSession = null;
        session.Dispose();
    }
    private void StopOcr()
    {
        if (ocrSession is { } session) CancelOcr(session);
        ocrResultWindow?.Close();
    }
    private OcrResultWindow ShowSessionCard(OcrSession session, ScreenRect? region)
    {
        var window = new OcrResultWindow(preferences.SearchUrlTemplate, region, OcrActions());
        ocrResultWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(ocrResultWindow, window)) ocrResultWindow = null;
            CancelOcr(session);
        };
        window.Reselect += () => { window.Close(); StartOcr(); };
        window.Show(); return window;
    }
    private async Task CompleteOcrAsync(OcrSession session, OcrSnapshot snapshot, ScreenRect? region)
    {
        if (!Current(session) || ocrOverlays.Count == 0) return;
        if (region == null) { CancelOcr(session); return; }
        session.Recognizing = true;
        byte[]? png = null;
        CloseOcrOverlays();
        OcrResultWindow? window = null;
        try
        {
            window = ShowSessionCard(session, region);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            session.Cancellation.Token.ThrowIfCancellationRequested();
            png = snapshot.CropPng(region); session.ReleaseImages();
            var result = await OcrRecognize(png, preferences.OcrPreferEnglish, session.Cancellation.Token);
            if (Current(session) && ReferenceEquals(ocrResultWindow, window)) window.SetResult(result);
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException) { if (Current(session)) window?.SetResult(new("region")); }
        catch (Exception error) when (error is not StackOverflowException) { if (Current(session)) window?.SetResult(new("recognition")); }
        finally { if (png != null) Array.Clear(png); FinishOcr(session); }
    }
    private void ShowOcrResult(OcrResult result)
    {
        var window = new OcrResultWindow(preferences.SearchUrlTemplate, null, OcrActions());
        ocrResultWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(ocrResultWindow, window)) ocrResultWindow = null; };
        window.Reselect += () => { window.Close(); StartOcr(); };
        window.SetResult(result); window.Show();
    }
    private OcrActionDependencies OcrActions() => new(() => preferences.Actions, () => preferences.SearchUrlTemplate,
        value => { var saved = SaveActionSettings(value); actionSettingsPanel.ShowSettings(value); return saved; }, ShowActionSettings, aiPreferences,
        offlineDictionary, ShowDictionarySettings);
}
