using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SnapBar.Core;
using Tesseract;

namespace SnapBar.Windows.Ocr;

public sealed record OcrResult(string Status, string? Text = null);

public static class ScreenOcr
{
    private const int MaximumPixels = 16_000_000;
    private static readonly string DataPath = Path.Combine(AppContext.BaseDirectory, "OcrData");

    public static IReadOnlyList<ScreenRect> Monitors() => Screen.AllScreens.Select(screen =>
        new ScreenRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height)).ToArray();

    public static async Task<OcrResult> CaptureAsync(ScreenRect region, CancellationToken token, bool preferEnglish = false)
    {
        Rectangle pixels;
        try { pixels = OcrSnapshot.Validate(region, MaximumPixels, 4); }
        catch (ArgumentOutOfRangeException) { return new("region"); }
        if (!Monitors().Any(screen => pixels.Left >= screen.X && pixels.Top >= screen.Y
            && pixels.Right <= screen.Right && pixels.Bottom <= screen.Bottom)
            || pixels.Width < 4 || pixels.Height < 4 || (long)pixels.Width * pixels.Height > MaximumPixels)
            return new("region");
        try
        {
            token.ThrowIfCancellationRequested();
            using var snapshot = OcrSnapshot.Capture(region);
            return await OcrWorker.RecognizeAsync(Path.Combine(AppContext.BaseDirectory, "SnapBar.App.exe"),
                snapshot.CropPng(region), preferEnglish, token);
        }
        catch (ExternalException) { return new("capture"); }
    }

    internal static Task<OcrResult> RecognizeAsync(byte[] image, CancellationToken token, bool preferEnglish = false) => Task.Run(() =>
    {
        if (!File.Exists(Path.Combine(DataPath, "eng.traineddata"))
            || !File.Exists(Path.Combine(DataPath, "chi_tra.traineddata"))) return new OcrResult("models");
        try
        {
            token.ThrowIfCancellationRequested();
            var languages = preferEnglish ? "eng+chi_tra" : "chi_tra+eng";
            using var engine = new TesseractEngine(DataPath, languages, EngineMode.LstmOnly);
            using var pixels = Pix.LoadFromMemory(image);
            using var page = engine.Process(pixels, PageSegMode.Auto);
            var text = page.GetText().Trim();
            token.ThrowIfCancellationRequested();
            return text.Length == 0 ? new("empty") : new("ok", text);
        }
        catch (Exception e) when (e is DllNotFoundException or TypeInitializationException or BadImageFormatException)
        { return new("runtime"); }
        catch (TesseractException) { return new("recognition"); }
    }, token);
}
