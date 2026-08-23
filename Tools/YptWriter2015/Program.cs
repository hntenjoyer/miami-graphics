using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RageLib.GTA5.ResourceWrappers.PC.Particles;
using RageLib.ResourceWrappers;

namespace YptWriter2015
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--check")
            {
                try
                {
                    var probe = new ParticlesFileWrapper_GTA5_pc();
                    probe.Load(args[1]);
                    int pn = probe.Particles != null && probe.Particles.TextureDictionary != null && probe.Particles.TextureDictionary.Textures != null
                        ? probe.Particles.TextureDictionary.Textures.Count : 0;
                    if (pn <= 0) { Console.Error.WriteLine("--check: empty TextureDictionary"); return 3; }
                    Console.WriteLine("--check OK: 2015 reader accepts " + args[1] + " (textures=" + pn + ")");
                    return 0;
                }
                catch (Exception cex)
                {
                    Console.Error.WriteLine("--check REJECTED by 2015 reader: " + cex.GetType().Name + ": " + cex.Message);
                    return 3;
                }
            }

            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: YptWriter2015 <in.ypt> <out.ypt> <specDir>");
                Console.Error.WriteLine("       YptWriter2015 --check <file.ypt>");
                return 2;
            }

            string inYpt = args[0];
            string outYpt = args[1];
            string specDir = args[2];

            try
            {
                var ypt = new ParticlesFileWrapper_GTA5_pc();
                ypt.Load(inYpt);

                var dict = ypt.Particles != null && ypt.Particles.TextureDictionary != null
                    ? ypt.Particles.TextureDictionary.Textures
                    : null;
                if (dict == null)
                {
                    Console.Error.WriteLine("no TextureDictionary in " + inYpt);
                    return 4;
                }

                var byName = new Dictionary<string, ITexture>(StringComparer.OrdinalIgnoreCase);
                foreach (ITexture t in dict)
                    byName[t.Name] = t;

                int applied = 0;
                foreach (string metaPath in Directory.GetFiles(specDir, "*.meta"))
                {
                    string name = Path.GetFileNameWithoutExtension(metaPath);
                    string binPath = Path.Combine(specDir, name + ".bin");
                    if (!File.Exists(binPath))
                    {
                        Console.Error.WriteLine("WARN missing bin for: " + name);
                        continue;
                    }

                    string[] m = File.ReadAllLines(metaPath);
                    if (m.Length < 5)
                    {
                        Console.Error.WriteLine("WARN bad meta for: " + name);
                        continue;
                    }
                    int w = int.Parse(m[0], CultureInfo.InvariantCulture);
                    int h = int.Parse(m[1], CultureInfo.InvariantCulture);
                    int mips = int.Parse(m[2], CultureInfo.InvariantCulture);
                    int stride = int.Parse(m[3], CultureInfo.InvariantCulture);
                    uint fmt = uint.Parse(m[4], CultureInfo.InvariantCulture);

                    ITexture tex;
                    if (!byName.TryGetValue(name, out tex))
                    {
                        Console.Error.WriteLine("WARN target lacks texture: " + name);
                        continue;
                    }

                    byte[] data = File.ReadAllBytes(binPath);
                    tex.Reset(w, h, mips, stride, (TextureFormat)fmt);
                    tex.SetTextureData(data);
                    applied++;
                    Console.WriteLine("applied " + name + " " + w + "x" + h + " mips=" + mips + " " + data.Length + "b");
                }

                if (applied == 0)
                {
                    Console.Error.WriteLine("nothing applied from " + specDir);
                    return 5;
                }

                ypt.Save(outYpt);

                var check = new ParticlesFileWrapper_GTA5_pc();
                check.Load(outYpt);
                int n = check.Particles != null
                        && check.Particles.TextureDictionary != null
                        && check.Particles.TextureDictionary.Textures != null
                    ? check.Particles.TextureDictionary.Textures.Count
                    : 0;
                if (n <= 0)
                {
                    Console.Error.WriteLine("self-reload produced empty TextureDictionary");
                    return 3;
                }

                Console.WriteLine("OK saved " + outYpt + " applied=" + applied + " reload-textures=" + n);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL " + ex);
                return 1;
            }
        }
    }
}
