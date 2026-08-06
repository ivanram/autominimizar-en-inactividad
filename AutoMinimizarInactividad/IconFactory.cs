using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace AutoMinimizarInactividad;

/// <summary>
/// Builds the tray/exe icons from the two embedded WhatsApp-with-sunglasses
/// PNGs (Assets/whatsapp-active.png, Assets/whatsapp-paused.png) instead of
/// drawing them procedurally — resized on demand with high-quality
/// interpolation since the source assets are already background-transparent
/// and near-square.
/// </summary>
internal static class IconFactory
{
    private const string ActiveResourceName = "whatsapp-active.png";
    private const string PausedResourceName = "whatsapp-paused.png";

    public static Icon BuildTrayIcon(int size, bool paused) =>
        BuildIcon(paused ? PausedResourceName : ActiveResourceName, size);

    /// <summary>The active-state glyph, used as the settings window's WhatsApp row icon.</summary>
    public static Icon BuildPlainBubbleIcon(int size) => BuildIcon(ActiveResourceName, size);

    /// <summary>Writes a multi-resolution .ico (used as the exe/taskbar icon) from the active-state asset.</summary>
    public static void SaveMultiResolutionIco(string path, bool paused = false)
    {
        using var source = LoadEmbeddedBitmap(paused ? PausedResourceName : ActiveResourceName);

        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (var size in sizes)
        {
            using var bmp = Resize(source, size);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }

        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);

        bw.Write((short)0); // reserved
        bw.Write((short)1); // type: icon
        bw.Write((short)sizes.Length);

        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            var size = sizes[i];
            var data = pngs[i];
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)0); // color count
            bw.Write((byte)0); // reserved
            bw.Write((short)1); // planes
            bw.Write((short)32); // bits per pixel
            bw.Write(data.Length);
            bw.Write(offset);
            offset += data.Length;
        }

        foreach (var data in pngs) bw.Write(data);
    }

    private static Icon BuildIcon(string resourceFileName, int size)
    {
        using var source = LoadEmbeddedBitmap(resourceFileName);
        using var bmp = Resize(source, size);
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private static Bitmap Resize(Bitmap source, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(source, 0, 0, size, size);
        return bmp;
    }

    private static Bitmap LoadEmbeddedBitmap(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().First(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return new Bitmap(stream);
    }
}
