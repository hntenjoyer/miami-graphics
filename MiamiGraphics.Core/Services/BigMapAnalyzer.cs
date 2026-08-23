using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{
    public static class BigMapAnalyzer
    {
        public const string MapRpfTarget       = "x64/levels/gta5/minimap.rpf";
        public const string SpexRpfTarget      = "x64/levels/gta5/spexvector.rpf";
        public const string GenericRpfTarget   = "x64/data/cdimages/scaleform_generic.rpf";
        public const string MinimapRpfDir      = "x64/patch/data/cdimages/scaleform_minimap.rpf";
        public const string YmtTarget          = "x64/data/tune/minimap.ymt";
        public const string ZoomTarget         = "common/data/ui/mapzoomdata.meta";
        public const string ImagesMetaTarget   = "common/data/levels/gta5/images.meta";
        public const string LegacyImagesMetaTarget = "common/data/images.meta";

        public static readonly string[] CanonTiles =
        {
            "minimap_0_0.ytd", "minimap_0_1.ytd", "minimap_1_0.ytd", "minimap_1_1.ytd",
            "minimap_2_0.ytd", "minimap_2_1.ytd", "minimap_lod_128.ytd",
            "minimap_sea_0_0.ytd", "minimap_sea_0_1.ytd", "minimap_sea_1_0.ytd",
            "minimap_sea_1_1.ytd", "minimap_sea_2_0.ytd", "minimap_sea_2_1.ytd",
        };

        public sealed record PlanEntry(
            string SourceRel,
            string TargetPath,
            long SizeBytes,
            string Sha256);

        public sealed class Analysis
        {
            public string PackFormat { get; set; } = "A";
            public List<PlanEntry> Entries { get; } = new();
            public List<string> Warnings { get; } = new();
            public List<string> PhotoPaths { get; } = new();
            public long TotalBytes => Entries.Sum(e => e.SizeBytes);
            public bool IsInstallable { get; set; }
        }

        private static readonly string[] PhotoExts = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
        private static readonly string[] IgnoreExts = { ".txt", ".url", ".lnk", ".db", ".ini", ".md" };

        public static Analysis Analyze(string extractedRoot, string? outPackageDir = null)
        {
            if (!Directory.Exists(extractedRoot))
                throw new DirectoryNotFoundException(extractedRoot);

            var a = new Analysis();

            var allFiles = Directory.GetFiles(extractedRoot, "*", SearchOption.AllDirectories);
            var unpackedMapDir = Directory.GetDirectories(extractedRoot, "minimap.rpf", SearchOption.AllDirectories)
                .FirstOrDefault(d => Directory.GetFiles(d, "*.ydd", SearchOption.AllDirectories).Length > 0);

            byte[]? Read(string p) { try { return File.ReadAllBytes(p); } catch { return null; } }
            static bool IsRpf7(byte[] b) => b.Length >= 4 && b[0] == '7' && b[1] == 'F' && b[2] == 'P' && b[3] == 'R';

            string? mapRpfFile = null, spexFile = null, wholeGenericFile = null, ymtFile = null,
                    zoomFile = null, imagesMetaFile = null;
            var tileFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var gfxFiles  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in allFiles)
            {
                var name = Path.GetFileName(f);
                var ext = Path.GetExtension(f).ToLowerInvariant();

                if (PhotoExts.Contains(ext)) { a.PhotoPaths.Add(f); continue; }
                if (IgnoreExts.Contains(ext)) continue;

                if (name.Equals("minimap.rpf", StringComparison.OrdinalIgnoreCase))
                {
                    var b = Read(f);
                    if (b != null && IsRpf7(b)) mapRpfFile = f;
                    else a.Warnings.Add($"minimap.rpf найден, но это не RPF7 - пропущен ({f})");
                }
                else if (name.Equals("spexvector.rpf", StringComparison.OrdinalIgnoreCase))
                    spexFile = f;
                else if (name.Equals("scaleform_generic.rpf", StringComparison.OrdinalIgnoreCase))
                {
                    var b = Read(f);
                    if (b != null && IsRpf7(b)) wholeGenericFile = f;
                    else a.Warnings.Add($"scaleform_generic.rpf найден, но это не RPF7 - пропущен ({f})");
                }
                else if (name.Equals("minimap.ymt", StringComparison.OrdinalIgnoreCase))
                    ymtFile = f;
                else if (name.Equals("mapzoomdata.meta", StringComparison.OrdinalIgnoreCase))
                    zoomFile = f;
                else if (name.Equals("images.meta", StringComparison.OrdinalIgnoreCase))
                    imagesMetaFile = f;
                else if (ext == ".ytd" && name.StartsWith("minimap_", StringComparison.OrdinalIgnoreCase))
                    tileFiles[name] = f;
                else if (ext == ".gfx")
                    gfxFiles[name] = f;
            }

            var plan = new List<(string src, string target, byte[]? repackedBytes)>();

            if (mapRpfFile != null)
                plan.Add((mapRpfFile, MapRpfTarget, null));
            else if (unpackedMapDir != null)
            {
                var repacked = RepackFolderToOpenRpf(unpackedMapDir);
                plan.Add((unpackedMapDir, MapRpfTarget, repacked));
                a.PackFormat = "B";
                a.Warnings.Add($"minimap.rpf был распакован папкой - репакнут в RPF7 ({repacked.Length:N0} байт)");
            }

            if (spexFile != null)
            {
                plan.Add((spexFile, SpexRpfTarget, null));
                a.PackFormat = "E";
                if (imagesMetaFile == null)
                    a.Warnings.Add("spexvector.rpf без images.meta - карта НЕ зарегистрируется в игре");
            }
            if (imagesMetaFile != null)
            {
                if (spexFile == null)
                    a.Warnings.Add("images.meta без spexvector.rpf - вероятно, обрезанный mirz-пак");
                plan.Add((imagesMetaFile, ImagesMetaTarget, null));
            }

            if (wholeGenericFile != null)
            {
                plan.Add((wholeGenericFile, GenericRpfTarget, null));
                if (a.PackFormat == "A") a.PackFormat = "C";
                if (tileFiles.Count > 0)
                    a.Warnings.Add("И целый scaleform_generic.rpf, И loose-тайлы: тайлы проигнорированы (целый rpf уже их содержит)");
            }
            else
            {
                foreach (var (name, path) in tileFiles.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    plan.Add((path, $"{GenericRpfTarget}/{name.ToLowerInvariant()}", null));
                var missing = CanonTiles.Where(t => !tileFiles.ContainsKey(t)).ToList();
                if (tileFiles.Count > 0 && missing.Count > 0)
                    a.Warnings.Add($"Тайлов угловой миникарты {tileFiles.Count}/13, нет: {string.Join(", ", missing)}");
            }

            foreach (var (name, path) in gfxFiles.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                plan.Add((path, $"{MinimapRpfDir}/{name.ToLowerInvariant()}", null));

            if (ymtFile != null) plan.Add((ymtFile, YmtTarget, null));
            if (zoomFile != null) plan.Add((zoomFile, ZoomTarget, null));

            bool hasMap = mapRpfFile != null || unpackedMapDir != null || spexFile != null || wholeGenericFile != null;
            a.IsInstallable = hasMap;
            if (!hasMap)
                a.Warnings.Add("Не найдена сама карта (ни minimap.rpf, ни spexvector.rpf, ни scaleform_generic.rpf) - ставить нечего");
            if (a.PackFormat == "A" && gfxFiles.Count == 0 && tileFiles.Count > 0)
                a.PackFormat = "D";

            foreach (var (src, target, repacked) in plan)
            {
                byte[] bytes = repacked ?? File.ReadAllBytes(src);
                string sourceRel = "files/" + target;
                a.Entries.Add(new PlanEntry(sourceRel, target, bytes.LongLength, Sha256Hex(bytes)));

                if (outPackageDir != null)
                {
                    var dst = Path.Combine(outPackageDir, sourceRel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.WriteAllBytes(dst, bytes);
                }
            }

            if (outPackageDir != null)
            {
                var manifest = new
                {
                    schema = 1,
                    packFormat = a.PackFormat,
                    entries = a.Entries.Select(e => new
                    {
                        sourceRel = e.SourceRel,
                        targetPath = e.TargetPath,
                        sizeBytes = e.SizeBytes,
                        sha256 = e.Sha256,
                    }),
                };
                File.WriteAllText(
                    Path.Combine(outPackageDir, "manifest.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
            }

            return a;
        }

        public static List<PlanEntry> ReadManifest(string packageDir)
        {
            var manifestPath = Path.Combine(packageDir, "manifest.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var list = new List<PlanEntry>();
            foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                list.Add(new PlanEntry(
                    e.GetProperty("sourceRel").GetString()!,
                    e.GetProperty("targetPath").GetString()!,
                    e.GetProperty("sizeBytes").GetInt64(),
                    e.GetProperty("sha256").GetString() ?? ""));
            }
            return list;
        }

        private static byte[] RepackFolderToOpenRpf(string srcFolder)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "mg_bigmap_repack_" + Guid.NewGuid().ToString("N") + ".rpf");
            try
            {
                var arc = RageArchiveWrapper7.Create(tmp);
                void Pack(string fsDir, IArchiveDirectory arcDir)
                {
                    foreach (var file in Directory.GetFiles(fsDir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        byte[] raw = File.ReadAllBytes(file);
                        bool isRsc7 = raw.Length >= 4 && raw[0] == 0x52 && raw[1] == 0x53 && raw[2] == 0x43;
                        if (isRsc7)
                        {
                            var rf = arcDir.CreateResourceFile();
                            rf.Name = Path.GetFileName(file).ToLowerInvariant();
                            rf.Import(new MemoryStream(raw));
                        }
                        else
                        {
                            var bf = arcDir.CreateBinaryFile();
                            bf.Name = Path.GetFileName(file).ToLowerInvariant();
                            bf.IsCompressed = true;
                            bf.UncompressedSize = (uint)raw.Length;
                            bf.Import(new MemoryStream(raw));
                        }
                    }
                    foreach (var sub in Directory.GetDirectories(fsDir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        var d = arcDir.CreateDirectory();
                        d.Name = Path.GetFileName(sub).ToLowerInvariant();
                        Pack(sub, d);
                    }
                }
                Pack(srcFolder, arc.Root);
                arc.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                arc.Flush();
                arc.Dispose();
                return File.ReadAllBytes(tmp);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data));
        }
    }
}
