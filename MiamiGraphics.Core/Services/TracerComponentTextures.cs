using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MiamiGraphics.Core.Parser;
using RageLib.GTA5.ResourceWrappers.PC.Particles;
using RageLib.Helpers;
using RageLib.ResourceWrappers;

namespace MiamiGraphics.Core.Services
{
    public static class TracerComponentTextures
    {
        public const string ComponentKey = "tracers";

        public const string DumpDirName = "dds";
        public const string MetaFileName = "tracers_textures.json";

        private const string MainTextureName = "ptfx_bullet_tracer";
        private const string HeatTextureName = "ptfx_bullet_tracer_heat";
        private const string RgTextureName   = "ptfx_bullet_tracer_rg";
        private const string BloodTextureName = "ptfx_blood_spray";

        private static readonly string[] CanonicalTextureNames =
            { MainTextureName, HeatTextureName, RgTextureName, BloodTextureName };

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        public sealed class TextureMeta
        {
            public string Name { get; set; } = "";
            public int Width { get; set; }
            public int Height { get; set; }
            public int MipMapLevels { get; set; }
            public int Stride { get; set; }
            public uint Format { get; set; }
            public bool HasDds { get; set; }
            public long RawLen { get; set; }
        }

        public sealed class DumpedTexture
        {
            public string Name { get; init; } = "";
            public string? DdsPath { get; init; }
            public int Width { get; init; }
            public int Height { get; init; }
            public int MipMapLevels { get; init; }
            public int Stride { get; init; }
            public TextureFormat Format { get; init; }
            public byte[] RawData { get; init; } = Array.Empty<byte>();
        }

        public static void DumpForComponent(string workDir, ResolvedComponentMap componentMap, Action<string>? log = null)
        {
            log ??= s => Console.WriteLine(s);

            if (componentMap?.Components == null ||
                !componentMap.Components.TryGetValue(ComponentKey, out ComponentInfo? info) ||
                info == null || !info.IsFound ||
                info.InternalPaths == null || info.InternalPaths.Count == 0)
            {
                return;
            }

            string componentDir = Path.Combine(workDir, "components", ComponentKey);
            if (!Directory.Exists(componentDir))
            {
                log($"[TracerDump] components/tracers нет на диске - skip");
                return;
            }

            string? coreYpt = ResolveCoreYpt(componentDir, info);
            if (coreYpt == null)
            {
                log($"[TracerDump] core.ypt не найден в {componentDir} - skip");
                return;
            }

            DumpFromYpt(coreYpt, componentDir, log);
        }

        public static void DumpFromYpt(string coreYptPath, string componentDir, Action<string>? log = null)
        {
            log ??= s => Console.WriteLine(s);

            var ypt = new ParticlesFileWrapper_GTA5_pc();
            ypt.Load(coreYptPath);

            var textures = ypt.Particles?.TextureDictionary?.Textures;
            if (textures == null || textures.Count == 0)
            {
                log($"[TracerDump] WARNING: пустой TextureDictionary в {coreYptPath} - дамп пропущен");
                return;
            }

            var list = new List<ITexture>();
            foreach (var t in textures) list.Add(t);

            string dumpDir = Path.Combine(componentDir, DumpDirName);
            Directory.CreateDirectory(dumpDir);

            var metas = new List<TextureMeta>();
            foreach (string name in CanonicalTextureNames)
            {
                var tex = list.FirstOrDefault(t => t.Name != null &&
                                                   t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (tex == null)
                {
                    log($"[TracerDump] info: '{name}' нет в core.ypt - skip");
                    continue;
                }

                byte[] raw;
                try { raw = tex.GetTextureData(); }
                catch (Exception ex)
                {
                    log($"[TracerDump] WARNING: '{name}' GetTextureData упал ({ex.GetType().Name}) - skip");
                    continue;
                }

                File.WriteAllBytes(Path.Combine(dumpDir, name + ".raw"), raw);

                bool hasDds = false;
                if (IsTextureDataSane(tex))
                {
                    try
                    {
                        DDSIO.SaveTextureData(tex, Path.Combine(dumpDir, name + ".dds"));
                        hasDds = true;
                    }
                    catch (Exception ex)
                    {
                        log($"[TracerDump] WARNING: '{name}' SaveTextureData упал ({ex.GetType().Name}) - только raw");
                    }
                }
                else
                {
                    log($"[TracerDump] WARNING: '{name}' мип-чейн битый ({raw.LongLength} б) - только raw");
                }

                metas.Add(new TextureMeta
                {
                    Name = name,
                    Width = tex.Width,
                    Height = tex.Height,
                    MipMapLevels = tex.MipMapLevels,
                    Stride = tex.Stride,
                    Format = (uint)tex.Format,
                    HasDds = hasDds,
                    RawLen = raw.LongLength,
                });
                log($"[TracerDump] '{name}' → raw{(hasDds ? "+dds" : "")} (W={tex.Width} H={tex.Height} {tex.Format} mips={tex.MipMapLevels})");
            }

            File.WriteAllText(Path.Combine(componentDir, MetaFileName),
                JsonSerializer.Serialize(metas, JsonOpts));

            if (!metas.Any(m => m.Name.Equals(MainTextureName, StringComparison.OrdinalIgnoreCase)))
                log($"[TracerDump] WARNING: '{MainTextureName}' не извлечена - кросс-инжект с этого редукса будет недоступен");
            else
                log($"[TracerDump] OK: извлечено текстур {metas.Count}, meta → {MetaFileName}");
        }

        public static List<DumpedTexture>? TryReadDumped(string componentDir, Action<string>? log = null)
        {
            log ??= s => Console.WriteLine(s);

            string metaPath = Path.Combine(componentDir, MetaFileName);
            string dumpDir = Path.Combine(componentDir, DumpDirName);
            if (!File.Exists(metaPath) || !Directory.Exists(dumpDir))
                return null;

            List<TextureMeta>? metas;
            try
            {
                metas = JsonSerializer.Deserialize<List<TextureMeta>>(File.ReadAllText(metaPath), JsonOpts);
            }
            catch (Exception ex)
            {
                log($"[TracerDump] WARNING: {MetaFileName} не читается ({ex.GetType().Name}) - fallback на live-извлечение");
                return null;
            }
            if (metas == null || metas.Count == 0)
                return null;

            var result = new List<DumpedTexture>();
            foreach (var m in metas)
            {
                string rawPath = Path.Combine(dumpDir, m.Name + ".raw");
                if (!File.Exists(rawPath))
                {
                    log($"[TracerDump] WARNING: нет {m.Name}.raw в дампе - skip");
                    continue;
                }
                byte[] raw = File.ReadAllBytes(rawPath);

                string? ddsPath = null;
                if (m.HasDds)
                {
                    string p = Path.Combine(dumpDir, m.Name + ".dds");
                    if (File.Exists(p)) ddsPath = p;
                }

                result.Add(new DumpedTexture
                {
                    Name = m.Name,
                    DdsPath = ddsPath,
                    Width = m.Width,
                    Height = m.Height,
                    MipMapLevels = m.MipMapLevels,
                    Stride = m.Stride,
                    Format = (TextureFormat)m.Format,
                    RawData = raw,
                });
            }

            return result.Count > 0 ? result : null;
        }

        private static string? ResolveCoreYpt(string componentDir, ComponentInfo info)
        {
            foreach (string internalPath in info.InternalPaths)
            {
                if (!internalPath.EndsWith(".ypt", StringComparison.OrdinalIgnoreCase)) continue;
                string dest = Path.Combine(componentDir, internalPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(dest)) return dest;
            }
            return Directory.EnumerateFiles(componentDir, "*.ypt", SearchOption.AllDirectories).FirstOrDefault();
        }

        private static bool IsTextureDataSane(ITexture tex)
        {
            long expected = 0;
            int w = tex.Width, h = tex.Height;
            for (int m = 0; m < tex.MipMapLevels; m++)
            {
                long lvl = tex.Format switch
                {
                    TextureFormat.D3DFMT_DXT1 => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8L,
                    TextureFormat.D3DFMT_DXT3 or TextureFormat.D3DFMT_DXT5 or
                    TextureFormat.D3DFMT_ATI2 or TextureFormat.D3DFMT_BC7
                        => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16L,
                    TextureFormat.D3DFMT_ATI1 => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8L,
                    TextureFormat.D3DFMT_A8R8G8B8 => w * (long)h * 4,
                    TextureFormat.D3DFMT_L8 or TextureFormat.D3DFMT_A8 => w * (long)h,
                    _ => -1,
                };
                if (lvl < 0) return true;
                expected += lvl;
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
            try { return tex.GetTextureData().LongLength == expected; }
            catch { return false; }
        }
    }
}
