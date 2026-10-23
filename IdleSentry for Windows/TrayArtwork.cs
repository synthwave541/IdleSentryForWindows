using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace IdleSentry;

internal static class TrayArtwork
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64 };

    internal static Icon Create(bool monitoring)
    {
        // The taskbar's DPI may differ from the monitor containing the main window.
        var taskbar = FindWindow("Shell_TrayWnd", null);
        uint dpi = taskbar == IntPtr.Zero ? GetDpiForSystem() : GetDpiForWindow(taskbar);
        int pixels = GetSystemMetricsForDpi(49 /* SM_CXSMICON */, dpi == 0 ? 96u : dpi);
        using var artwork = Application.GetResourceStream(new Uri(
            "pack://application:,,,/IdleSentry;component/IdleSentry.png"))!.Stream;
        using var bytes = new MemoryStream(Encode(artwork, monitoring));
        using var icon = new Icon(bytes, pixels, pixels);
        return (Icon)icon.Clone();
    }

    internal static byte[] Encode(Stream source, bool monitoring)
    {
        using var original = Image.FromStream(source);
        var frames = Sizes.Select(size => Render(original, size, monitoring)).ToArray();
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)Sizes.Length);
        int offset = 6 + 16 * Sizes.Length;
        for (int i = 0; i < Sizes.Length; i++)
        {
            writer.Write((byte)Sizes[i]); writer.Write((byte)Sizes[i]);
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(frames[i].Length); writer.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        return output.ToArray();
    }

    private static byte[] Render(Image original, int size, bool monitoring)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var attributes = new ImageAttributes())
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            // Desaturate the actual artwork, retaining its transparency and geometry.
            if (!monitoring) attributes.SetColorMatrix(new ColorMatrix(new[]
            {
                new[] { .299f, .299f, .299f, 0f, 0f },
                new[] { .587f, .587f, .587f, 0f, 0f },
                new[] { .114f, .114f, .114f, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f }
            }));
            graphics.DrawImage(original, new Rectangle(0, 0, size, size),
                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
        }
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
