using System.Drawing;
using System.IO;
using System.Windows;
using IdleSentry;

internal static class TrayArtworkChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (bool monitoring in new[] { true, false })
        {
            using var source = Application.GetResourceStream(new Uri(
                "pack://application:,,,/IdleSentry;component/IdleSentry.png"))!.Stream;
            using var ico = new MemoryStream(TrayArtwork.Encode(source, monitoring));
            using var reader = new BinaryReader(ico);
            check(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 7,
                $"{monitoring}: tray artwork is a seven-resolution ICO");
            foreach (int size in new[] { 16, 20, 24, 32, 40, 48, 64 })
            {
                check(reader.ReadByte() == size && reader.ReadByte() == size, $"{monitoring}: native {size}px tray frame");
                reader.ReadBytes(6);
                int length = reader.ReadInt32(), offset = reader.ReadInt32();
                long nextEntry = ico.Position;
                ico.Position = offset;
                using var frame = new MemoryStream(reader.ReadBytes(length));
                using var bitmap = new Bitmap(frame);
                bool transparent = false, colored = false, opaque = false;
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    transparent |= pixel.A == 0;
                    opaque |= pixel.A > 200;
                    colored |= pixel.A > 200 && Math.Max(pixel.R, Math.Max(pixel.G, pixel.B))
                        - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) > 2;
                }
                check(transparent && opaque && colored == monitoring,
                    $"{monitoring}: {size}px preserves transparent logo silhouette and expected color state");
                ico.Position = nextEntry;
            }
            using var live = TrayArtwork.Create(monitoring);
            check(live.Width >= 16 && live.Handle != IntPtr.Zero, $"{monitoring}: taskbar-sized icon loads successfully");
        }
    }
}
