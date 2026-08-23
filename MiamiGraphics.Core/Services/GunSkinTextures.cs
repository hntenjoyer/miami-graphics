#nullable disable
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using ImageMagick;

namespace MiamiGraphics.Core.Services;

public static class GunSkinTextures
{
    public sealed record TexInfo(string Name, int Width, int Height, string Role, byte[] Png);

    public static List<TexInfo> List(string gunDir)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outp = new List<TexInfo>();
        foreach (var file in EnumerateResourceFiles(gunDir))
        {
            var td = LoadDict(file);
            var items = td?.Textures?.data_items;
            if (items == null) continue;
            foreach (var t in items)
            {
                if (t?.Name == null || !seen.Add(t.Name)) continue;
                byte[] png;
                try { png = ToPng(t); }
                catch { continue; }
                outp.Add(new TexInfo(t.Name, t.Width, t.Height, RoleOf(t.Name), png));
            }
        }
        return outp;
    }

    public static List<string> Replace(string gunDir, string name, byte[] pngBytes)
    {
        var (bgra, w, h) = PngToBgra(pngBytes);
        var pending = new List<(string Path, byte[] Bytes)>();
        foreach (var file in EnumerateResourceFiles(gunDir))
        {
            if (file.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
            {
                var ydr = new YdrFile();
                ydr.Load(File.ReadAllBytes(file));
                if (Mutate(ydr.Drawable?.ShaderGroup?.TextureDictionary, name, bgra, w, h))
                    pending.Add((file, ydr.Save()));
            }
            else
            {
                var ytd = new YtdFile();
                ytd.Load(File.ReadAllBytes(file));
                if (Mutate(ytd.TextureDict, name, bgra, w, h))
                    pending.Add((file, ytd.Save()));
            }
        }
        var patched = new List<string>();
        foreach (var (path, bytes) in pending)
        {
            File.WriteAllBytes(path, bytes);
            patched.Add(Path.GetFileName(path));
        }
        return patched;
    }

    private static IEnumerable<string> EnumerateResourceFiles(string dir)
        => Directory.GetFiles(dir)
            .Where(f => f.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f);

    private static TextureDictionary LoadDict(string file)
    {
        try
        {
            if (file.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
            {
                var ydr = new YdrFile();
                ydr.Load(File.ReadAllBytes(file));
                return ydr.Drawable?.ShaderGroup?.TextureDictionary;
            }
            var ytd = new YtdFile();
            ytd.Load(File.ReadAllBytes(file));
            return ytd.TextureDict;
        }
        catch { return null; }
    }

    private static bool Mutate(TextureDictionary td, string name, byte[] bgra, int w, int h)
    {
        var tex = td?.Textures?.data_items?
            .FirstOrDefault(t => string.Equals(t?.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tex == null) return false;
        GameTextureWriter.Apply(tex, bgra, w, h);
        return true;
    }

    private static string RoleOf(string name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("_n") || n.Contains("normal") || n.Contains("bump")) return "normal";
        if (n.Contains("spec") || n.Contains("_s")) return "spec";
        return "diffuse";
    }

    private static byte[] ToPng(Texture tex)
    {
        byte[] bgra = null;
        try { bgra = DDSIO.GetPixels(tex, 0); } catch { }
        int w = tex.Width, h = tex.Height;
        if (bgra != null && w > 0 && h > 0 && bgra.Length >= w * h * 4)
            return BgraToPng(bgra, w, h);

        byte[] dds = DDSIO.GetDDSFile(tex);
        if (dds == null || dds.Length == 0)
            throw new InvalidOperationException($"decode failed '{tex.Name}' ({tex.Format})");
        using var image = new MagickImage(dds);
        image.Format = MagickFormat.Png32;
        using var ms = new MemoryStream();
        image.Write(ms, MagickFormat.Png);
        return ms.ToArray();
    }

    private static byte[] BgraToPng(byte[] bgra, int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, w, h);
        var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int srcRow = w * 4;
            for (int y = 0; y < h; y++)
                Marshal.Copy(bgra, y * srcRow, bd.Scan0 + y * bd.Stride, srcRow);
        }
        finally { bmp.UnlockBits(bd); }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static (byte[] Bgra, int Width, int Height) PngToBgra(byte[] png)
    {
        using var srcMs = new MemoryStream(png);
        using var src = new Bitmap(srcMs);
        using var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = global::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(src, 0, 0, src.Width, src.Height);
        }
        int w = bmp.Width, h = bmp.Height;
        var rect = new Rectangle(0, 0, w, h);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[w * h * 4];
            int dstRow = w * 4;
            for (int y = 0; y < h; y++)
                Marshal.Copy(bd.Scan0 + y * bd.Stride, bgra, y * dstRow, dstRow);
            return (bgra, w, h);
        }
        finally { bmp.UnlockBits(bd); }
    }
}
