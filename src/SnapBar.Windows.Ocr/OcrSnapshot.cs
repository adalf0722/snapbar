using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnapBar.Core;

namespace SnapBar.Windows.Ocr;

public sealed class OcrSnapshot : IDisposable
{
    private Bitmap? bitmap;
    public ScreenRect Bounds { get; }

    private OcrSnapshot(ScreenRect bounds, Bitmap bitmap) { Bounds = bounds; this.bitmap = bitmap; }

    public static OcrSnapshot Capture(ScreenRect bounds)
    {
        var rectangle = Validate(bounds, 67_108_864, 1);
        var image = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(image);
            graphics.CopyFromScreen(rectangle.Location, Point.Empty, rectangle.Size);
            return new(bounds, image);
        }
        catch { image.Dispose(); throw; }
    }

    internal static OcrSnapshot FromBitmap(ScreenRect bounds, Bitmap source)
    {
        var rectangle = Validate(bounds, 67_108_864, 1);
        if (rectangle.Size != source.Size) throw new ArgumentException("Snapshot dimensions differ.");
        return new(bounds, source.Clone(new Rectangle(Point.Empty, source.Size), PixelFormat.Format32bppArgb));
    }

    public byte[] CopyBgraPixels()
    {
        var image = bitmap ?? throw new ObjectDisposedException(nameof(OcrSnapshot));
        var bytes = new byte[checked(image.Width * image.Height * 4)];
        var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < image.Height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * image.Width * 4, image.Width * 4);
        }
        finally { image.UnlockBits(data); }
        return bytes;
    }

    public byte[] CropPng(ScreenRect region)
    {
        var image = bitmap ?? throw new ObjectDisposedException(nameof(OcrSnapshot));
        var rectangle = Validate(region, 16_000_000, 4);
        if (region.X < Bounds.X || region.Y < Bounds.Y || region.Right > Bounds.Right || region.Bottom > Bounds.Bottom)
            throw new ArgumentOutOfRangeException(nameof(region));
        rectangle.Offset(-(int)Bounds.X, -(int)Bounds.Y);
        using var crop = image.Clone(rectangle, PixelFormat.Format32bppArgb);
        using var output = new MemoryStream();
        crop.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    internal static Rectangle Validate(ScreenRect bounds, long maximumPixels, int minimum)
    {
        double[] values = [bounds.X, bounds.Y, bounds.Width, bounds.Height, bounds.Right, bounds.Bottom];
        if (values.Any(value => !double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            || bounds.Width < minimum || bounds.Height < minimum || bounds.Width * bounds.Height > maximumPixels)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        return new((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
    }

    public void Dispose() { bitmap?.Dispose(); bitmap = null; }
}
