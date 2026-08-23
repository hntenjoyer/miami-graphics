#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace MiamiGraphics.Core.Services;

public static class GunResourceFitter
{
    private const int MinSide = 128;

    private const int MaxSteps = 12;

    public sealed record Report(
        bool Changed, int Recompressed, int Downscaled,
        long Before, long After, bool Fits)
    {
        public string Describe() =>
            $"{Mb(Before)} -> {Mb(After)} МБ (пережато {Recompressed}, уменьшено {Downscaled})";
        private static string Mb(long b) => (b / 1024.0 / 1024.0).ToString("0.0");
    }

    public static bool CanFit(string? name) =>
        name != null &&
        (name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith(".yft", StringComparison.OrdinalIgnoreCase));

    public static byte[] Fit(string name, byte[] bytes, out Report report)
    {
        long before = RpfResourceBudget.MemorySize(bytes);
        report = new Report(false, 0, 0, before, before, before <= RpfResourceBudget.RefuseAt);
        if (report.Fits || !CanFit(name)) return bytes;

        try
        {
            var doc = ResourceDoc.Load(name, bytes);
            if (doc == null) return bytes;

            var textures = doc.Textures();
            if (textures.Count == 0) return bytes;

            int recompressed = 0;
            foreach (var tex in textures)
            {
                if (tex.Format != TextureFormat.D3DFMT_A8R8G8B8) continue;
                if (!Recode(tex, tex.Width, tex.Height)) continue;
                recompressed++;
            }

            var current = recompressed > 0 ? doc.Save() : bytes;
            long after = RpfResourceBudget.MemorySize(current);

            long texSum = TexBytes(textures);
            long nonTex = Math.Max(0, after - texSum);
            var stuck = new HashSet<Texture>();
            int downscaled = 0, steps = 0;

            while (after > RpfResourceBudget.RefuseAt && steps < MaxSteps)
            {
                var victim = textures
                    .Where(t => !stuck.Contains(t) && t.Width > MinSide && t.Height > MinSide)
                    .OrderByDescending(t => (long)(t.Data?.FullData?.Length ?? 0))
                    .FirstOrDefault();
                if (victim == null) break;

                steps++;
                if (!Recode(victim, Math.Max(MinSide, victim.Width / 2),
                                    Math.Max(MinSide, victim.Height / 2)))
                {
                    stuck.Add(victim);
                    continue;
                }
                downscaled++;

                texSum = TexBytes(textures);
                if (nonTex + texSum > RpfResourceBudget.RefuseAt) continue;

                current = doc.Save();
                after = RpfResourceBudget.MemorySize(current);
                nonTex = Math.Max(0, after - texSum);
            }

            report = new Report(
                Changed:      recompressed > 0 || downscaled > 0,
                Recompressed: recompressed,
                Downscaled:   downscaled,
                Before:       before,
                After:        after,
                Fits:         after <= RpfResourceBudget.RefuseAt);
            return report.Changed ? current : bytes;
        }
        catch
        {
            return bytes;
        }
    }

    private static long TexBytes(IEnumerable<Texture> textures) =>
        textures.Sum(t => (long)(t.Data?.FullData?.Length ?? 0));

    private static bool Recode(Texture tex, int targetW, int targetH)
    {
        try
        {
            var bgra = Pixels(tex);
            if (bgra == null || bgra.Length < (long)tex.Width * tex.Height * 4) return false;
            var policy = GameTextureWriter.PolicyFor(tex);
            if (policy == GameTextureWriter.Policy.KeepRaw)
                policy = GameTextureWriter.Policy.Auto;
            var enc = GameTextureWriter.Encode(bgra, tex.Width, tex.Height, targetW, targetH, policy);
            tex.Width = (ushort)enc.Width;
            tex.Height = (ushort)enc.Height;
            tex.Depth = 1;
            tex.Levels = enc.Levels;
            tex.Format = enc.Format;
            tex.Stride = enc.Stride;
            tex.Data = new TextureData { FullData = enc.Data };
            return true;
        }
        catch { return false; }
    }

    private static byte[]? Pixels(Texture tex)
    {
        try
        {
            var direct = DDSIO.GetPixels(tex, 0);
            if (direct != null && direct.Length >= (long)tex.Width * tex.Height * 4) return direct;
        }
        catch {}

        try
        {
            var dds = DDSIO.GetDDSFile(tex);
            if (dds == null || dds.Length == 0) return null;
            using var img = new ImageMagick.MagickImage(dds);
            img.Format = ImageMagick.MagickFormat.Bgra;
            img.Alpha(ImageMagick.AlphaOption.Set);
            using var ms = new global::System.IO.MemoryStream();
            img.Write(ms, ImageMagick.MagickFormat.Bgra);
            var px = ms.ToArray();
            return px.Length >= (long)tex.Width * tex.Height * 4 ? px : null;
        }
        catch { return null; }
    }

    private sealed class ResourceDoc
    {
        private readonly YdrFile? _ydr;
        private readonly YtdFile? _ytd;
        private readonly YftFile? _yft;

        private ResourceDoc(YdrFile? ydr, YtdFile? ytd, YftFile? yft)
        { _ydr = ydr; _ytd = ytd; _yft = yft; }

        public static ResourceDoc? Load(string name, byte[] bytes)
        {
            if (name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
            {
                var f = new YdrFile(); f.Load(bytes); return new ResourceDoc(f, null, null);
            }
            if (name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
            {
                var f = new YtdFile(); f.Load(bytes); return new ResourceDoc(null, f, null);
            }
            if (name.EndsWith(".yft", StringComparison.OrdinalIgnoreCase))
            {
                var f = new YftFile(); f.Load(bytes); return new ResourceDoc(null, null, f);
            }
            return null;
        }

        public List<Texture> Textures()
        {
            var dicts = new List<TextureDictionary?>
            {
                _ydr?.Drawable?.ShaderGroup?.TextureDictionary,
                _ytd?.TextureDict,
                _yft?.Fragment?.Drawable?.ShaderGroup?.TextureDictionary,
            };
            return dicts
                .Where(d => d?.Textures?.data_items != null)
                .SelectMany(d => d!.Textures!.data_items!)
                .Where(t => t != null)
                .ToList()!;
        }

        public byte[] Save()
        {
            if (_ydr != null) return _ydr.Save();
            if (_ytd != null) return _ytd.Save();
            return _yft!.Save();
        }
    }
}
