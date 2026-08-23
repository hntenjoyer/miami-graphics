#nullable disable
using System.Drawing;
using System.Text.Json;
using CodeWalker.GameFiles;

namespace MiamiGraphics.Gunpack.Services;

public static class RoundtripTest
{
    public static int Run(TextureStudio studio, string extractRoot, string[] args)
    {
        bool keep = args.Contains("--keep");
        var results = new List<object>();
        int failed = 0;

        var targets = new List<(string Pack, string Gun)>();
        if (args[0] == "roundtrip" && args.Length >= 3)
        {
            targets.Add((args[1], args[2]));
        }
        else if (args[0] == "roundtrip-pack" && args.Length >= 2)
        {
            string packDir = Path.Combine(extractRoot, args[1]);
            if (!Directory.Exists(packDir)) { Console.WriteLine($"нет пака: {args[1]}"); return 2; }
            foreach (var g in Directory.GetDirectories(packDir).OrderBy(x => x))
                targets.Add((args[1], Path.GetFileName(g)));
        }
        else
        {
            Console.WriteLine("usage: gunpack roundtrip <pack> <gun> [--keep] | roundtrip-pack <pack> [--keep]");
            return 2;
        }

        foreach (var (pack, gun) in targets)
        {
            var r = TestOne(studio, pack, gun, keep);
            results.Add(r);
            if (!(bool)r.GetType().GetProperty("ok").GetValue(r)) failed++;
        }

        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\n[Roundtrip] total={results.Count} failed={failed}");
        return failed == 0 ? 0 : 1;
    }

    private static object TestOne(TextureStudio studio, string pack, string gun, bool keep)
    {
        string step = "detail";
        try
        {
            var detail = studio.GetGunDetail(pack, gun);

            string texName = detail.Shaders
                .Select(s => s.Diffuse)
                .Where(n => n != null)
                .Select(n => detail.Textures.FirstOrDefault(t =>
                    string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase)))
                .Where(t => t != null)
                .OrderByDescending(t => t.Width * t.Height)
                .Select(t => t.Name)
                .FirstOrDefault()
                ?? detail.Textures.OrderByDescending(t => t.Width * t.Height).FirstOrDefault()?.Name;

            if (texName == null)
                return new { pack, gun, ok = false, step, error = "нет текстур" };

            step = "extract";
            byte[] origPng = studio.GetTexturePng(pack, gun, texName);

            step = "paint";
            byte[] painted = PaintMagentaCross(origPng, out int w, out int h);

            step = "replace";
            var rep = studio.ReplaceTexture(pack, gun, texName, painted);
            if (!rep.Ok)
                return new { pack, gun, ok = false, step, error = rep.Error };

            step = "verify-pixels";
            byte[] back = studio.GetTexturePng(pack, gun, texName);
            if (!CenterIsMagenta(back))
                return new { pack, gun, ok = false, step, error = "центр не магента после перезаписи" };

            step = "verify-ydr-reload";
            string workGlb = studio.GetGlbPath(pack, gun);
            long glbSize = new FileInfo(workGlb).Length;
            if (glbSize < 1024)
                return new { pack, gun, ok = false, step = "verify-glb", error = $"GLB подозрительно мал: {glbSize}" };

            return new
            {
                pack, gun, ok = true,
                texture = texName, size = $"{w}x{h}",
                patched = rep.Patched, glbBytes = glbSize,
            };
        }
        catch (Exception ex)
        {
            return new { pack, gun, ok = false, step, error = $"{ex.GetType().Name}: {ex.Message}" };
        }
        finally
        {
            if (!keep)
                try { studio.Reset(pack, gun); } catch { }
        }
    }

    private static byte[] PaintMagentaCross(byte[] png, out int w, out int h)
    {
        using var srcMs = new MemoryStream(png);
        using var bmp = new Bitmap(srcMs);
        w = bmp.Width; h = bmp.Height;
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(Color.Magenta);
        int bw = Math.Max(4, w / 5), bh = Math.Max(4, h / 5);
        g.FillRectangle(brush, (w - bw) / 2, 0, bw, h);
        g.FillRectangle(brush, 0, (h - bh) / 2, w, bh);
        using var outMs = new MemoryStream();
        bmp.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
        return outMs.ToArray();
    }

    private static bool CenterIsMagenta(byte[] png)
    {
        using var ms = new MemoryStream(png);
        using var bmp = new Bitmap(ms);
        var c = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
        return c.R > 200 && c.G < 60 && c.B > 200;
    }
}
