using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapBar.App;

internal static class GrabCursors
{
    // Original vector silhouettes, rendered at multiple sizes for per-monitor DPI.
    internal static Cursor Open { get; } = Create(
        "M 10,27 C 8,24 5,20 4,18 C 3,16 5,14 7,16 L 10,19 L 8,8 C 8,5 11,5 12,8 L 13,15 L 13,4 C 13,1 17,1 17,4 L 17,14 L 18,5 C 18,2 22,3 22,6 L 21,15 L 23,9 C 24,6 27,7 26,10 L 25,20 C 25,23 23,25 22,28 L 22,30 L 11,30 Z");
    internal static Cursor Closed { get; } = Create(
        "M 9,26 C 7,23 5,20 5,17 C 5,14 8,14 10,17 L 10,12 C 10,9 13,9 14,11 C 14,8 18,8 19,11 C 20,8 23,9 23,12 C 25,10 28,12 27,15 L 26,21 C 26,24 23,26 22,28 L 22,30 L 11,30 Z M 14,12 L 14,17 M 19,12 L 19,17 M 23,13 L 23,18");

    private static Cursor Create(string path)
    {
        var geometry = Geometry.Parse(path);
        int[] sizes = [32, 48, 64];
        var frames = sizes.Select(size => Frame(geometry, size)).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)sizes[i]); writer.Write((byte)sizes[i]); writer.Write((ushort)0);
            writer.Write((ushort)(16 * sizes[i] / 32)); writer.Write((ushort)(16 * sizes[i] / 32));
            writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        stream.Position = 0;
        return new Cursor(stream, true);
    }

    private static byte[] Frame(Geometry geometry, int size)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new ScaleTransform(size / 32d, size / 32d));
            drawing.DrawGeometry(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(32, 32, 32)), 1.25)
                { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
            drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = size * 4;
        var pixels = new byte[stride * size]; converted.CopyPixels(pixels, stride, 0);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40); writer.Write(size); writer.Write(size * 2);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(pixels.Length);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (var y = size - 1; y >= 0; y--) writer.Write(pixels, y * stride, stride);
        var maskStride = (size + 31) / 32 * 4;
        for (var y = size - 1; y >= 0; y--)
        {
            var mask = new byte[maskStride];
            for (var x = 0; x < size; x++)
                if (pixels[y * stride + x * 4 + 3] == 0) mask[x / 8] |= (byte)(0x80 >> (x % 8));
            writer.Write(mask);
        }
        return stream.ToArray();
    }
}
