using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiamiGraphics.Core.I18n;
using RageLib.Resources.GTA5;
using RageLib.Resources.GTA5.PC.Particles;
using RageLib.Resources.GTA5.PC.Textures;

namespace MiamiGraphics.Core.Services
{
    public static class CoreYptTexturePatcher
    {
        private const long GraphicsBase = 0x60000000;

        private const uint FmtDxt1 = 0x31545844;
        private const uint FmtDxt3 = 0x33545844;
        private const uint FmtDxt5 = 0x35545844;
        private const uint FmtA8R8G8B8 = 21;
        private const uint FmtA8B8G8R8 = 32;

        public readonly record struct TextureSource(byte[] Pixels, int Width, int Height, uint Format);

        public static Dictionary<string, byte[]> ExtractPixelData(string yptPath, IEnumerable<string> names)
        {
            var want = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(yptPath)) return result;

            var res = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            res.Load(yptPath);
            foreach (var t in EnumerateTextures(res))
            {
                var name = t.Name?.Value ?? "";
                if (!want.Contains(name)) continue;
                var data = t.Data?.FullData;
                if (data == null || data.Length == 0) continue;
                result[name] = (byte[])data.Clone();
            }
            return result;
        }

        public static int ReplacePixelDataInPlace(string yptPath, IReadOnlyDictionary<string, byte[]> pixelsByName)
            => ReplacePixelDataInPlace(yptPath, pixelsByName, out _, out _);

        public static int ReplacePixelDataInPlace(string yptPath, IReadOnlyDictionary<string, byte[]> pixelsByName,
            out int matchedNames, out int lengthSkipped,
            IReadOnlyDictionary<string, TextureSource>? adaptSources = null)
        {
            matchedNames = 0;
            lengthSkipped = 0;
            if (!File.Exists(yptPath)) return 0;
            if (pixelsByName == null || pixelsByName.Count == 0) return 0;

            var res = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            res.Load(yptPath);
            byte[] gfx = res.GraphicsData;
            if (gfx == null || gfx.Length == 0)
            {
                Console.WriteLine($"[CoreYptTex] {Path.GetFileName(yptPath)}: пустой графический сегмент - skip");
                return 0;
            }

            var written = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            int replaced = 0, skippedLen = 0, skippedMap = 0;
            foreach (var t in EnumerateTextures(res))
            {
                var name = t.Name?.Value ?? "";
                if (!pixelsByName.TryGetValue(name, out var src) || src == null) continue;
                matchedNames++;

                var cur = t.Data?.FullData;
                if (cur == null) continue;
                if (cur.Length != src.Length)
                {
                    byte[]? adapted = null;
                    if (adaptSources != null && adaptSources.TryGetValue(name, out var srcTex))
                        adapted = TryAdaptToTexture(srcTex, t, cur.Length);
                    if (adapted is null)
                    {
                        Console.WriteLine($"[CoreYptTex] {name}: длина не совпала ({src.Length} против {cur.Length}) - пропускаю");
                        skippedLen++;
                        continue;
                    }
                    Console.WriteLine($"[CoreYptTex] {name}: пережата под живую текстуру " +
                        $"{t.Width}x{t.Height} fmt=0x{t.Format:X} ({src.Length} → {adapted.Length} б)");
                    src = adapted;
                }

                long off = t.Data.Position - GraphicsBase;
                if (off < 0 || off + cur.Length > gfx.Length) { skippedMap++; continue; }

                if (!SpansEqual(gfx, (int)off, cur)) { skippedMap++; continue; }

                Buffer.BlockCopy(src, 0, gfx, (int)off, src.Length);
                written[name] = src;
                replaced++;
            }

            if (skippedMap > 0)
                throw new InvalidOperationException(
                    Loc.T("error.coreYptOffsetSelfCheck", ("count", skippedMap), ("path", yptPath)));

            lengthSkipped = skippedLen;
            if (replaced == 0)
            {
                Console.WriteLine($"[CoreYptTex] {Path.GetFileName(yptPath)}: заменять нечего (имён совпало {matchedNames}, по длине отсеяно {skippedLen})");
                return 0;
            }

            SaveRaw(res, gfx, yptPath);

            var check = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            check.Load(yptPath);
            int reTex = EnumerateTextures(check).Count();
            if (reTex == 0)
                throw new InvalidOperationException(Loc.T("error.coreYptTexturesUnreadable", ("path", yptPath)));
            foreach (var t in EnumerateTextures(check))
            {
                var name = t.Name?.Value ?? "";
                if (!written.TryGetValue(name, out var expect) || expect == null) continue;
                var got = t.Data?.FullData;
                if (got == null || got.Length != expect.Length || !SpansEqual(got, 0, expect))
                    throw new InvalidOperationException(Loc.T("error.coreYptTextureMismatch", ("name", name), ("path", yptPath)));
            }

            Console.WriteLine($"[CoreYptTex] {Path.GetFileName(yptPath)}: заменено {replaced}, текстур после перечтения {reTex}");
            return replaced;
        }

        private static byte[]? TryAdaptToTexture(TextureSource src, TextureDX11 t, int targetLen)
        {
            try
            {
                var rgba = DecodeBaseRgba(src);
                if (rgba is null) return null;

                using var ms = new MemoryStream(targetLen);
                int w = t.Width, h = t.Height;
                if (w <= 0 || h <= 0) return null;
                while (ms.Length < targetLen)
                {
                    var level = ResampleRgba(rgba, src.Width, src.Height, w, h);
                    var enc = EncodeLevel(level, w, h, t.Format);
                    if (enc is null) return null;
                    if (ms.Length + enc.Length > targetLen) break;
                    ms.Write(enc, 0, enc.Length);
                    if (w == 1 && h == 1) break;
                    w = Math.Max(1, w / 2);
                    h = Math.Max(1, h / 2);
                }
                long gap = targetLen - ms.Length;
                if (gap > 0 && gap <= 16) { ms.Write(new byte[gap], 0, (int)gap); }
                return ms.Length == targetLen ? ms.ToArray() : null;
            }
            catch { return null; }
        }

        private static byte[]? DecodeBaseRgba(TextureSource src)
        {
            int w = src.Width, h = src.Height;
            if (w <= 0 || h <= 0 || src.Pixels is null) return null;
            int bw = (w + 3) / 4, bh = (h + 3) / 4;
            switch (src.Format)
            {
                case FmtDxt1:
                {
                    int len = bw * bh * 8;
                    if (src.Pixels.Length < len) return null;
                    return RageLib.Compression.TextureCompressionHelper.DecompressBC1(Slice(src.Pixels, len), w, h);
                }
                case FmtDxt3:
                {
                    int len = bw * bh * 16;
                    if (src.Pixels.Length < len) return null;
                    return RageLib.Compression.TextureCompressionHelper.DecompressBC2(Slice(src.Pixels, len), w, h);
                }
                case FmtDxt5:
                {
                    int len = bw * bh * 16;
                    if (src.Pixels.Length < len) return null;
                    return RageLib.Compression.TextureCompressionHelper.DecompressBC3(Slice(src.Pixels, len), w, h);
                }
                case FmtA8R8G8B8:
                {
                    int len = w * h * 4;
                    if (src.Pixels.Length < len) return null;
                    return SwapRB(Slice(src.Pixels, len));
                }
                case FmtA8B8G8R8:
                {
                    int len = w * h * 4;
                    if (src.Pixels.Length < len) return null;
                    return Slice(src.Pixels, len);
                }
                default: return null;
            }
        }

        private static byte[]? EncodeLevel(byte[] rgba, int w, int h, uint format)
        {
            int bw = (w + 3) / 4, bh = (h + 3) / 4;
            switch (format)
            {
                case FmtDxt1: return FitLen(RageLib.Compression.TextureCompressionHelper.CompressBC1(rgba, w, h), bw * bh * 8);
                case FmtDxt3: return FitLen(RageLib.Compression.TextureCompressionHelper.CompressBC2(rgba, w, h), bw * bh * 16);
                case FmtDxt5: return FitLen(RageLib.Compression.TextureCompressionHelper.CompressBC3(rgba, w, h), bw * bh * 16);
                case FmtA8R8G8B8: return SwapRB(rgba);
                case FmtA8B8G8R8: return (byte[])rgba.Clone();
                default: return null;
            }
        }

        private static byte[]? FitLen(byte[]? data, int want)
        {
            if (data is null || data.Length < want) return null;
            return data.Length == want ? data : Slice(data, want);
        }

        private static byte[] Slice(byte[] src, int len)
        {
            var r = new byte[len];
            Buffer.BlockCopy(src, 0, r, 0, len);
            return r;
        }

        private static byte[] SwapRB(byte[] rgba)
        {
            var r = new byte[rgba.Length];
            for (int i = 0; i + 3 < rgba.Length; i += 4)
            {
                r[i] = rgba[i + 2];
                r[i + 1] = rgba[i + 1];
                r[i + 2] = rgba[i];
                r[i + 3] = rgba[i + 3];
            }
            return r;
        }

        private static byte[] ResampleRgba(byte[] src, int sw, int sh, int dw, int dh)
        {
            if (sw == dw && sh == dh) return (byte[])src.Clone();
            var dst = new byte[dw * dh * 4];
            for (int dy = 0; dy < dh; dy++)
            {
                int sy0 = dy * sh / dh, sy1 = Math.Max(sy0 + 1, (dy + 1) * sh / dh);
                for (int dx = 0; dx < dw; dx++)
                {
                    int sx0 = dx * sw / dw, sx1 = Math.Max(sx0 + 1, (dx + 1) * sw / dw);
                    long r = 0, g = 0, b = 0, a = 0; int n = 0;
                    for (int sy = sy0; sy < sy1; sy++)
                        for (int sx = sx0; sx < sx1; sx++)
                        {
                            int o = (sy * sw + sx) * 4;
                            r += src[o]; g += src[o + 1]; b += src[o + 2]; a += src[o + 3];
                            n++;
                        }
                    int d = (dy * dw + dx) * 4;
                    dst[d] = (byte)(r / n); dst[d + 1] = (byte)(g / n);
                    dst[d + 2] = (byte)(b / n); dst[d + 3] = (byte)(a / n);
                }
            }
            return dst;
        }

        public static Dictionary<string, TextureSource> ExtractSources(string yptPath, IEnumerable<string> names)
        {
            var want = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, TextureSource>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(yptPath)) return result;

            var res = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            res.Load(yptPath);
            foreach (var t in EnumerateTextures(res))
            {
                var name = t.Name?.Value ?? "";
                if (!want.Contains(name)) continue;
                var data = t.Data?.FullData;
                if (data == null || data.Length == 0) continue;
                result[name] = new TextureSource((byte[])data.Clone(), t.Width, t.Height, t.Format);
            }
            return result;
        }

        public static List<(string Name, int Width, int Height, uint Format, int Length)> DescribeTextures(
            string yptPath, IEnumerable<string> names)
        {
            var want = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var result = new List<(string, int, int, uint, int)>();
            var res = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            res.Load(yptPath);
            foreach (var t in EnumerateTextures(res))
            {
                var name = t.Name?.Value ?? "";
                if (!want.Contains(name)) continue;
                result.Add((name, t.Width, t.Height, t.Format, t.Data?.FullData?.Length ?? 0));
            }
            return result;
        }

        public static TextureSource ParseDds(byte[] dds)
        {
            var pixels = DdsPixelData(dds);
            int height = BitConverter.ToInt32(dds, 12);
            int width = BitConverter.ToInt32(dds, 16);
            uint pfFlags = BitConverter.ToUInt32(dds, 80);
            uint format = (pfFlags & 0x4) != 0
                ? BitConverter.ToUInt32(dds, 84)
                : FmtA8R8G8B8;
            return new TextureSource(pixels, width, height, format);
        }

        public static byte[] DdsPixelData(byte[] dds)
        {
            if (dds == null || dds.Length < 128) throw new ArgumentException(Loc.T("error.ddsTooShort"), nameof(dds));
            if (!(dds[0] == 'D' && dds[1] == 'D' && dds[2] == 'S' && dds[3] == ' '))
                throw new ArgumentException(Loc.T("error.ddsNoSignature"), nameof(dds));

            int header = 128;
            if (dds[84] == 'D' && dds[85] == 'X' && dds[86] == '1' && dds[87] == '0') header += 20;
            if (dds.Length <= header) throw new ArgumentException(Loc.T("error.ddsNoDataAfterHeader"), nameof(dds));

            var body = new byte[dds.Length - header];
            Buffer.BlockCopy(dds, header, body, 0, body.Length);
            return body;
        }

        private static IEnumerable<TextureDX11> EnumerateTextures(ResourceFile_GTA5_pc<ParticleEffectsList> res)
        {
            var items = res?.ResourceData?.TextureDictionary?.Textures?.Entries?.data_items;
            if (items == null) yield break;
            foreach (var t in items) if (t != null) yield return t;
        }

        private static bool SpansEqual(byte[] buf, int offset, byte[] expect)
        {
            if (offset < 0 || offset + expect.Length > buf.Length) return false;
            for (int i = 0; i < expect.Length; i++)
                if (buf[offset + i] != expect[i]) return false;
            return true;
        }

        private static void SaveRaw(ResourceFile_GTA5_pc<ParticleEffectsList> src, byte[] patchedGraphics, string outPath)
        {
            var raw = new ResourceFile_GTA5_pc
            {
                Version = src.Version,
                SystemData = src.SystemData,
                GraphicsData = patchedGraphics,
                SystemPagesDiv16 = src.SystemPagesDiv16, SystemPagesDiv8 = src.SystemPagesDiv8,
                SystemPagesDiv4 = src.SystemPagesDiv4, SystemPagesDiv2 = src.SystemPagesDiv2,
                SystemPagesMul1 = src.SystemPagesMul1, SystemPagesMul2 = src.SystemPagesMul2,
                SystemPagesMul4 = src.SystemPagesMul4, SystemPagesMul8 = src.SystemPagesMul8,
                SystemPagesMul16 = src.SystemPagesMul16, SystemPagesSizeShift = src.SystemPagesSizeShift,
                GraphicsPagesDiv16 = src.GraphicsPagesDiv16, GraphicsPagesDiv8 = src.GraphicsPagesDiv8,
                GraphicsPagesDiv4 = src.GraphicsPagesDiv4, GraphicsPagesDiv2 = src.GraphicsPagesDiv2,
                GraphicsPagesMul1 = src.GraphicsPagesMul1, GraphicsPagesMul2 = src.GraphicsPagesMul2,
                GraphicsPagesMul4 = src.GraphicsPagesMul4, GraphicsPagesMul8 = src.GraphicsPagesMul8,
                GraphicsPagesMul16 = src.GraphicsPagesMul16, GraphicsPagesSizeShift = src.GraphicsPagesSizeShift,
            };
            raw.Save(outPath);
        }
    }
}
