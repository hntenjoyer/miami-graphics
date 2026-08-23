#nullable disable
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using ImageMagick;

using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.Gunsmith;

public static class TextureCodec
{
    public static byte[] ToPng(Texture tex)
    {
        byte[] bgra = null;
        try { bgra = DDSIO.GetPixels(tex, 0); }
        catch {}

        int w = tex.Width, h = tex.Height;
        if (bgra != null && w > 0 && h > 0 && bgra.Length >= w * h * 4)
            return BgraToPng(bgra, w, h);

        byte[] dds = DDSIO.GetDDSFile(tex);
        if (dds == null || dds.Length == 0)
            throw new InvalidOperationException(Loc.T("error.textureDecodeFailed",
                ("name", tex.Name), ("format", tex.Format)));
        using var image = new MagickImage(dds);
        image.Format = MagickFormat.Png32;
        using var ms = new MemoryStream();
        image.Write(ms, MagickFormat.Png);
        return ms.ToArray();
    }

    public static byte[] BgraToPng(byte[] bgra, int w, int h)
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

    public static byte[] ResizePng(byte[] png, int max)
    {
        using var srcMs = new MemoryStream(png);
        using var src = new Bitmap(srcMs);
        if (src.Width <= max && src.Height <= max) return png;
        double k = Math.Min((double)max / src.Width, (double)max / src.Height);
        int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = global::System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static (byte[] Bgra, int Width, int Height) PngToBgra(byte[] png)
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
