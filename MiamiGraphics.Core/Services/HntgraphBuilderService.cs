using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{
    public class HntgraphBuilderService
    {
        private const string TemplateRpfRelative = @"templates\miami_empty.rpf";
        private const string TemplateOverlaymagsRelative = @"templates\overlaymags";

        public class BuildRequest
        {
            public List<SelectedGunRecord> SelectedGuns { get; set; } = new();
            public string OutputHntgraphPath { get; set; } = "";
        }

        public class BuildResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public int GunsApplied { get; set; }
            public int GunsSkipped { get; set; }
            public long FinalSize { get; set; }
            public string? Sha256 { get; set; }
        }

        public BuildResult Build(BuildRequest req)
        {
            var result = new BuildResult();

            if (string.IsNullOrWhiteSpace(req.OutputHntgraphPath))
            {
                result.Message = "OutputHntgraphPath пустой";
                return result;
            }

            string? templateRpf = ResolveTemplateRpfPath();
            if (templateRpf == null)
            {
                result.Message = "Шаблон miami_empty.rpf не найден. Ожидался в templates\\ рядом с приложением.";
                return result;
            }

            string? overlaymagsDir = ResolveOverlaymagsPath();
            if (overlaymagsDir == null)
            {
                result.Message = "Папка overlaymags не найдена (ни в templates\\, ни на рабочем столе).";
                return result;
            }

            try
            {
                string? outDir = Path.GetDirectoryName(req.OutputHntgraphPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);

                string tmpPath = req.OutputHntgraphPath + ".tmp";
                File.Copy(templateRpf, tmpPath, overwrite: true);

                string debugBuildLogPath = Path.Combine(outDir ?? ".", "_debug_build.txt");
                using (var debugLog = new StreamWriter(debugBuildLogPath))
                {
                    debugLog.WriteLine("=== build snapshot ===");
                    debugLog.WriteLine($"Output: {req.OutputHntgraphPath}");
                    debugLog.WriteLine($"Template: {templateRpf}");
                    debugLog.WriteLine($"Overlaymags source: {overlaymagsDir}");
                    debugLog.WriteLine();
                    debugLog.WriteLine("Columns: source | fileName | createdAs | size | magic(first16)");
                    debugLog.WriteLine();

                    using (var archive = RageArchiveWrapper7.Open(tmpPath))
                    {
                        var gunsDir = archive.Root;

                        foreach (var gun in req.SelectedGuns)
                        {
                            string zipPath = Path.Combine(gun.GunpackDir, "guns", $"{gun.BaseName}.zip");
                            if (!File.Exists(zipPath))
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[Hntgraph] SKIP: zip не найден для гана {gun.DisplayName} ({zipPath})");
                                Console.ResetColor();
                                result.GunsSkipped++;
                                continue;
                            }

                            try
                            {
                                using var fs = File.OpenRead(zipPath);
                                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                                foreach (var entry in zip.Entries)
                                {
                                    using var es = entry.Open();
                                    using var ms = new MemoryStream();
                                    es.CopyTo(ms);
                                    byte[] data = ms.ToArray();

                                    AddOrReplaceFile(gunsDir, entry.Name, data);
                                    LogAddedFile(debugLog, $"gun:{gun.DisplayName}", entry.Name, data, IsRsc7Resource(data));
                                }
                                result.GunsApplied++;
                                Console.WriteLine($"[Hntgraph] + {gun.DisplayName} ({zip.Entries.Count} файлов)");
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[Hntgraph] SKIP {gun.DisplayName}: {ex.Message}");
                                Console.ResetColor();
                                result.GunsSkipped++;
                            }
                        }

                        int overlayAdded = 0;
                        foreach (var file in Directory.EnumerateFiles(overlaymagsDir, "*", SearchOption.AllDirectories))
                        {
                            string fileName = Path.GetFileName(file);
                            byte[] data = File.ReadAllBytes(file);
                            AddOrReplaceFile(gunsDir, fileName, data);
                            LogAddedFile(debugLog, "overlaymag", fileName, data, IsRsc7Resource(data));
                            overlayAdded++;
                        }
                        Console.WriteLine($"[Hntgraph] + {overlayAdded} файлов overlaymags");

                        archive.Flush();
                    }
                }

                string verifyLogPath = Path.Combine(outDir ?? ".", "_debug_hntgraph_after_build.txt");
                VerifyHntgraphAfterBuild(tmpPath, verifyLogPath);

                if (File.Exists(req.OutputHntgraphPath))
                    File.Delete(req.OutputHntgraphPath);
                File.Move(tmpPath, req.OutputHntgraphPath);

                result.FinalSize = new FileInfo(req.OutputHntgraphPath).Length;
                result.Sha256 = ComputeSha256(req.OutputHntgraphPath);
                result.Success = true;
                result.Message = $"hntgraph собран: {result.GunsApplied} ганов, {result.FinalSize / 1024} КБ";

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Hntgraph] ✓ {result.Message}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                result.Message = $"Ошибка сборки hntgraph: {ex.Message}";
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Hntgraph] {result.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }

            return result;
        }

        public static bool IsCoherentWithJournal(string hntgraphPath, HntgraphCustomization journal)
        {
            if (!File.Exists(hntgraphPath)) return journal.SelectedGuns.Count == 0;
            if (journal.LastBuiltSize == 0 || string.IsNullOrEmpty(journal.LastBuiltSha256))
                return false;
            long actualSize = new FileInfo(hntgraphPath).Length;
            if (actualSize != journal.LastBuiltSize) return false;
            string actualSha = ComputeSha256(hntgraphPath);
            return string.Equals(actualSha, journal.LastBuiltSha256, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ResolveTemplateRpfPath()
        {

            return WalkUpForFile(TemplateRpfRelative)
                ?? WalkUpForFile(@"MiamiGraphics.Core\templates\miami_empty.rpf");
        }

        private static string? ResolveOverlaymagsPath()
        {
            return WalkUpForDirectory(TemplateOverlaymagsRelative)
                ?? WalkUpForDirectory(@"MiamiGraphics.Core\templates\overlaymags");
        }

        private static string? WalkUpForFile(string relativePath)
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var p = Path.Combine(dir, relativePath);
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static string? WalkUpForDirectory(string relativePath)
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var p = Path.Combine(dir, relativePath);
                if (Directory.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static void LogAddedFile(StreamWriter log, string source, string fileName, byte[] data, bool isResource)
        {
            int take = Math.Min(16, data.Length);
            string hex = string.Join(" ", Enumerable.Range(0, take).Select(i => data[i].ToString("x2")));
            string createdAs = isResource ? "CreateResourceFile" : "CreateBinaryFile";
            log.WriteLine($"{source,-30} | {fileName,-45} | {createdAs,-20} | {data.Length,10} | {hex}");
        }

        private static void VerifyHntgraphAfterBuild(string hntgraphPath, string logPath)
        {
            try
            {
                using var archive = RageArchiveWrapper7.Open(hntgraphPath);
                using var writer = new StreamWriter(logPath);
                writer.WriteLine("=== hntgraph.rpf verification ===");
                writer.WriteLine($"Path: {hntgraphPath}");
                writer.WriteLine($"Size: {new FileInfo(hntgraphPath).Length} bytes");
                writer.WriteLine();
                writer.WriteLine("Columns: fileName | iface | type | flags | magic(first16)");
                writer.WriteLine();

                foreach (var f in archive.Root.GetFiles())
                {
                    string typeName = f.GetType().Name;
                    string iface = (f is IArchiveBinaryFile) ? "Binary" : (f is IArchiveResourceFile ? "Resource" : "Unknown");
                    string flags = "-";
                    if (f is IArchiveBinaryFile bin2)
                        flags = $"compressed={bin2.IsCompressed} encrypted={bin2.IsEncrypted} uncompressedSize={bin2.UncompressedSize}";

                    string hex = "?";
                    try
                    {
                        using var ms = new MemoryStream();
                        f.Export(ms);
                        byte[] b = ms.ToArray();
                        int take = Math.Min(16, b.Length);
                        hex = string.Join(" ", Enumerable.Range(0, take).Select(i => b[i].ToString("x2")));
                    }
                    catch (Exception ex) { hex = $"read-err={ex.Message}"; }

                    writer.WriteLine($"{f.Name,-45} | {iface,-8} | {typeName,-26} | {flags,-70} | {hex}");
                }

                var subs = archive.Root.GetDirectories().ToList();
                if (subs.Count > 0)
                {
                    writer.WriteLine();
                    writer.WriteLine($"!! warning: root has {subs.Count} subdirectories (expected 0 for weapons-like rpf)");
                    foreach (var s in subs) writer.WriteLine($"  - {s.Name}");
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(logPath, $"ERROR during verify: {ex.Message}\n{ex.StackTrace}"); } catch { }
            }
        }

        private static void AddOrReplaceFile(IArchiveDirectory dir, string fileName, byte[] data)
        {
            bool isResource = IsRsc7Resource(data);

            var existing = dir.GetFiles()
                .FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                dir.DeleteFile(existing);
            }

            if (isResource)
            {
                var resFile = dir.CreateResourceFile();
                resFile.Name = fileName;
                using var ms = new MemoryStream(data);
                resFile.Import(ms);
            }
            else
            {
                var binFile = dir.CreateBinaryFile();
                binFile.Name = fileName;
                binFile.IsEncrypted = false;
                binFile.IsCompressed = false;
                binFile.UncompressedSize = data.LongLength;
                using var ms = new MemoryStream(data);
                binFile.Import(ms);
            }
        }

        private static bool IsRsc7Resource(byte[] data)
        {
            if (data.Length < 4) return false;
            return data[0] == 0x52 &&
                   data[1] == 0x53 &&
                   data[2] == 0x43;
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
