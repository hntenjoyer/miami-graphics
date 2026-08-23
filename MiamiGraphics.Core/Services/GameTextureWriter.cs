#nullable enable
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using ImageMagick;

namespace MiamiGraphics.Core.Services;

public static class GameTextureWriter
{
    public const int MaxSide = 2048;

    public readonly record struct Encoded(
        int Width, int Height, byte Levels, TextureFormat Format, ushort Stride, byte[] Data);

    public static void Apply(Texture tex, byte[] bgra, int w, int h)
    {
        var enc = Encode(bgra, w, h, tex.Width, tex.Height, PolicyFor(tex));
        tex.Width = (ushort)enc.Width;
        tex.Height = (ushort)enc.Height;
        tex.Depth = 1;
        tex.Levels = enc.Levels;
        tex.Format = enc.Format;
        tex.Stride = enc.Stride;
        tex.Data = new TextureData { FullData = enc.Data };
    }

    public enum Policy
    {
        Auto,
        ForceBc3,
        KeepRaw,
    }

    private const int RawBelowPixels = 256 * 256;

    public static Policy PolicyFor(Texture tex)
    {
        long px = (long)tex.Width * tex.Height;
        return tex.Format switch
        {
            TextureFormat.D3DFMT_DXT1 => Policy.Auto,
            TextureFormat.D3DFMT_A8R8G8B8 when px <= RawBelowPixels => Policy.KeepRaw,
            TextureFormat.D3DFMT_A8R8G8B8 => Policy.Auto,
            _ => Policy.ForceBc3,
        };
    }

    public static Encoded Encode(byte[] bgra, int w, int h, int capW = 0, int capH = 0,
        Policy policy = Policy.Auto)
    {
        if (bgra == null || w <= 0 || h <= 0)
            throw new ArgumentException("пустые пиксели");

        int limW = capW > 0 ? Math.Min(capW, MaxSide) : MaxSide;
        int limH = capH > 0 ? Math.Min(capH, MaxSide) : MaxSide;

        if (w > limW || h > limH)
        {
            int nw = Math.Min(w, limW), nh = Math.Min(h, limH);
            bgra = Resize(bgra, w, h, nw, nh);
            w = nw; h = nh;
        }

        bool blockable = w % 4 == 0 && h % 4 == 0 && w >= 4 && h >= 4;
        if (blockable && policy != Policy.KeepRaw)
        {
            var bc = TryEncodeBc(bgra, w, h, policy == Policy.ForceBc3);
            if (bc.HasValue) return bc.Value;
        }

        return Raw(bgra, w, h);
    }

    public static Encoded Raw(byte[] bgra, int w, int h) =>
        new(w, h, 1, TextureFormat.D3DFMT_A8R8G8B8, (ushort)(w * 4), bgra);

    private static Encoded? TryEncodeBc(byte[] bgra, int w, int h, bool forceAlpha = false)
    {
        try
        {
            bool alpha = forceAlpha || HasAlpha(bgra);
            byte[] png = BgraToPng(bgra, w, h);

            using var img = new MagickImage(png);
            if (!alpha) img.Alpha(AlphaOption.Off);
            img.Settings.SetDefine(MagickFormat.Dds, "compression", alpha ? "dxt5" : "dxt1");
            img.Settings.SetDefine(MagickFormat.Dds, "mipmaps", MipCount(w, h).ToString());
            img.Settings.SetDefine(MagickFormat.Dds, "fast-mipmaps", "true");

            using var ms = new MemoryStream();
            img.Write(ms, MagickFormat.Dds);
            var dds = ms.ToArray();
            if (dds.Length == 0) return null;

            var tex = DDSIO.GetTexture(dds);
            if (tex?.Data?.FullData == null || tex.Data.FullData.Length == 0) return null;
            if (tex.Width != w || tex.Height != h) return null;
            if (tex.Format != TextureFormat.D3DFMT_DXT1 && tex.Format != TextureFormat.D3DFMT_DXT5)
                return null;

            return new Encoded(tex.Width, tex.Height, tex.Levels == 0 ? (byte)1 : tex.Levels,
                tex.Format, tex.Stride, tex.Data.FullData);
        }
        catch
        {
            return null;
        }
    }

    internal static int MipCount(int w, int h)
    {
        int n = 1, s = Math.Max(w, h);
        while (s > 1 && n < 14) { s >>= 1; n++; }
        return n;
    }

    internal static bool HasAlpha(byte[] bgra)
    {
        for (int i = 3; i < bgra.Length; i += 4)
            if (bgra[i] != 255) return true;
        return false;
    }

    internal static byte[] Resize(byte[] bgra, int w, int h, int nw, int nh)
    {
        using var src = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var srect = new Rectangle(0, 0, w, h);
        var sbd = src.LockBits(srect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(bgra, y * w * 4, sbd.Scan0 + y * sbd.Stride, w * 4);
        }
        finally { src.UnlockBits(sbd); }

        using var dst = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.CompositingMode = global::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.InterpolationMode = global::System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = global::System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, nw, nh);
        }

        var outBgra = new byte[nw * nh * 4];
        var drect = new Rectangle(0, 0, nw, nh);
        var dbd = dst.LockBits(drect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < nh; y++)
                Marshal.Copy(dbd.Scan0 + y * dbd.Stride, outBgra, y * nw * 4, nw * 4);
        }
        finally { dst.UnlockBits(dbd); }
        return outBgra;
    }

    internal static byte[] BgraToPng(byte[] bgra, int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, w, h);
        var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(bgra, y * w * 4, bd.Scan0 + y * bd.Stride, w * 4);
        }
        finally { bmp.UnlockBits(bd); }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
