using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.System
{
    public enum HealthStatus { Ok, Fail, Skip }

    public sealed class HealthCheckItem
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public HealthStatus Status { get; set; }
        public string Detail { get; set; } = "";
    }

    public sealed class HealthCheckReport
    {
        public bool OverallOk { get; set; }
        public string UpdateRpfPath { get; set; } = "";
        public long ElapsedMilliseconds { get; set; }
        public List<HealthCheckItem> Items { get; set; } = new();
    }

    public static class UpdateRpfHealthCheck
    {
        private static readonly string[] CriticalNestedRpfs =
        {
            "x64/patch/data/effects/ptfx.rpf",
            "x64/patch/data/cdimages/scaleform_minimap.rpf",
            "x64/data/cdimages/scaleform_generic.rpf",
        };

        public static HealthCheckReport Run(string updateRpfPath)
        {
            var report = new HealthCheckReport { UpdateRpfPath = updateRpfPath };
            var sw = Stopwatch.StartNew();

            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine($"[HealthCheck] Проверка: {updateRpfPath}");
            Console.WriteLine("================================================================");

            if (!File.Exists(updateRpfPath))
            {
                report.Items.Add(new HealthCheckItem
                {
                    Name = "update.rpf existence",
                    Category = "Level1",
                    Status = HealthStatus.Fail,
                    Detail = "файл не найден"
                });
                report.OverallOk = false;
                sw.Stop();
                report.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                PrintReport(report);
                return report;
            }

            RunLevel1(updateRpfPath, report);

            bool level1Ok = report.Items
                .Where(i => i.Category == "Level1")
                .All(i => i.Status != HealthStatus.Fail);

            if (level1Ok)
            {
                RunLevel2(updateRpfPath, report);
            }
            else
            {
                Console.WriteLine("[HealthCheck] Уровень 2 пропущен (уровень 1 не прошёл).");
            }

            sw.Stop();
            report.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            report.OverallOk = report.Items.All(i => i.Status != HealthStatus.Fail);

            PrintReport(report);
            return report;
        }

        private static void RunLevel1(string updateRpfPath, HealthCheckReport report)
        {
            IArchive? archive = null;
            try
            {
                archive = RageArchiveWrapper7.Open(updateRpfPath);
                if (archive?.Root == null)
                {
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = "root archive",
                        Category = "Level1",
                        Status = HealthStatus.Fail,
                        Detail = "Root == null после Open"
                    });
                    return;
                }

                int files = archive.Root.GetFiles().Count();
                int dirs = archive.Root.GetDirectories().Count();
                report.Items.Add(new HealthCheckItem
                {
                    Name = "root archive",
                    Category = "Level1",
                    Status = HealthStatus.Ok,
                    Detail = $"files={files}, dirs={dirs}"
                });

                var contentXml = archive.Root.GetFiles().FirstOrDefault(f =>
                    f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));

                if (contentXml == null)
                {
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = "content.xml",
                        Category = "Level1",
                        Status = HealthStatus.Fail,
                        Detail = "не найден в корне update.rpf"
                    });
                    return;
                }

                if (contentXml is not IArchiveBinaryFile binFile)
                {
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = "content.xml",
                        Category = "Level1",
                        Status = HealthStatus.Fail,
                        Detail = "не является IArchiveBinaryFile"
                    });
                    return;
                }

                byte[] xmlBytes;
                try
                {
                    xmlBytes = GetDecodedFileBytes(binFile);
                }
                catch (Exception ex)
                {
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = "content.xml",
                        Category = "Level1",
                        Status = HealthStatus.Fail,
                        Detail = $"Export/Decode упал: {ex.GetType().Name}: {ex.Message}"
                    });
                    return;
                }

                if (xmlBytes.Length == 0)
                {
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = "content.xml",
                        Category = "Level1",
                        Status = HealthStatus.Fail,
                        Detail = "0 байт"
                    });
                    return;
                }

                string xmlText = DecodeXmlText(xmlBytes);
                if (string.IsNullOrWhiteSpace(xmlText))
                {
                    string hexDump = FormatHexDump(xmlBytes, 32);
                    string dumpPath = SaveRawDump(xmlBytes);
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = "content.xml",
                        Category = "Level1",
                        Status = HealthStatus.Fail,
                        Detail = $"не удалось декодировать ({xmlBytes.Length} байт). Первые 32: {hexDump}. Raw dump: {dumpPath}"
                    });
                    return;
                }

                try
                {
                    var doc = XDocument.Parse(xmlText);
                    int itemCount = doc.Descendants("Item").Count();
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = "content.xml",
                        Category = "Level1",
                        Status = HealthStatus.Ok,
                        Detail = $"{xmlBytes.Length} байт, {itemCount} <Item>"
                    });
                }
                catch (Exception ex)
                {
                    string hexDump = FormatHexDump(xmlBytes, 32);
                    string dumpPath = SaveRawDump(xmlBytes);
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = "content.xml",
                        Category = "Level1",
                        Status = HealthStatus.Fail,
                        Detail = $"XDocument.Parse упал: {ex.GetType().Name}: {ex.Message}. Первые 32: {hexDump}. Raw dump: {dumpPath}"
                    });
                }
            }
            catch (Exception ex)
            {
                report.Items.Add(new HealthCheckItem
                {
                    Name = "root archive",
                    Category = "Level1",
                    Status = HealthStatus.Fail,
                    Detail = $"{ex.GetType().Name}: {ex.Message}"
                });
            }
            finally
            {
                try { archive?.Dispose(); } catch {  }
            }
        }

        private static void RunLevel2(string updateRpfPath, HealthCheckReport report)
        {
            IArchive? archive = null;
            try
            {
                archive = RageArchiveWrapper7.Open(updateRpfPath);
                if (archive?.Root == null)
                    return;

                foreach (string nestedPath in CriticalNestedRpfs)
                {
                    CheckNestedRpf(archive.Root, nestedPath, report);
                }
            }
            catch (Exception ex)
            {
                report.Items.Add(new HealthCheckItem
                {
                    Name = "Level2 root reopen",
                    Category = "Level2",
                    Status = HealthStatus.Fail,
                    Detail = $"{ex.GetType().Name}: {ex.Message}"
                });
            }
            finally
            {
                try { archive?.Dispose(); } catch { }
            }
        }

        private static void CheckNestedRpf(IArchiveDirectory root, string internalPath, HealthCheckReport report)
        {
            string[] parts = internalPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            IArchiveDirectory? current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                current = current?.GetDirectories().FirstOrDefault(d =>
                    d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (current == null)
                {
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = internalPath,
                        Category = "Level2",
                        Status = HealthStatus.Skip,
                        Detail = $"каталог не найден: {parts[i]}"
                    });
                    return;
                }
            }

            var rpfFile = current?.GetFiles().FirstOrDefault(f =>
                f.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;

            if (rpfFile == null)
            {
                report.Items.Add(new HealthCheckItem
                {
                    Name = internalPath,
                    Category = "Level2",
                    Status = HealthStatus.Skip,
                    Detail = "not present in update.rpf"
                });
                return;
            }

            try
            {
                using var stream = rpfFile.GetStream();
                using var nestedArc = RageArchiveWrapper7.Open(stream, rpfFile.Name, true);
                if (nestedArc?.Root == null)
                {
                    report.Items.Add(new HealthCheckItem
                    {
                        Name = internalPath,
                        Category = "Level2",
                        Status = HealthStatus.Fail,
                        Detail = "Open вернул null Root"
                    });
                    return;
                }

                int files = nestedArc.Root.GetFiles().Count();
                int dirs = nestedArc.Root.GetDirectories().Count();
                report.Items.Add(new HealthCheckItem
                {
                    Name = internalPath,
                    Category = "Level2",
                    Status = HealthStatus.Ok,
                    Detail = $"files={files}, dirs={dirs}"
                });
            }
            catch (Exception ex)
            {
                report.Items.Add(new HealthCheckItem
                {
                    Name = internalPath,
                    Category = "Level2",
                    Status = HealthStatus.Fail,
                    Detail = $"{ex.GetType().Name}: {ex.Message}"
                });
            }
        }

        private static void PrintReport(HealthCheckReport report)
        {
            Console.WriteLine();
            Console.WriteLine("----------------------------------------------------------------");
            foreach (var item in report.Items)
            {
                ConsoleColor color = item.Status switch
                {
                    HealthStatus.Ok => ConsoleColor.Green,
                    HealthStatus.Fail => ConsoleColor.Red,
                    HealthStatus.Skip => ConsoleColor.DarkGray,
                    _ => ConsoleColor.Gray
                };
                Console.ForegroundColor = color;
                string tag = item.Status.ToString().ToUpperInvariant();
                Console.WriteLine($"  [{tag,-4}] [{item.Category}] {item.Name}: {item.Detail}");
                Console.ResetColor();
            }
            Console.WriteLine("----------------------------------------------------------------");

            Console.ForegroundColor = report.OverallOk ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"[HealthCheck] ИТОГ: {(report.OverallOk ? "OK" : "FAIL")}  |  Время: {report.ElapsedMilliseconds} мс");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static string DecodeXmlText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";

            try
            {
                using var ms = new MemoryStream(bytes);
                using var reader = new StreamReader(ms, global::System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string text = reader.ReadToEnd();
                if (LooksLikeXml(text)) return text;
            }
            catch { }

            try
            {
                string utf8 = global::System.Text.Encoding.UTF8.GetString(bytes);
                if (LooksLikeXml(utf8)) return utf8;
            }
            catch { }

            try
            {
                string utf16 = global::System.Text.Encoding.Unicode.GetString(bytes);
                if (LooksLikeXml(utf16)) return utf16;
            }
            catch { }

            return "";
        }

        private static bool LooksLikeXml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.TrimStart('﻿', '\0', ' ', '\t', '\r', '\n');
            return trimmed.StartsWith("<");
        }

        private static string FormatHexDump(byte[] bytes, int count)
        {
            int take = Math.Min(count, bytes.Length);
            var hexParts = new string[take];
            var asciiParts = new char[take];
            for (int i = 0; i < take; i++)
            {
                hexParts[i] = bytes[i].ToString("X2");
                asciiParts[i] = (bytes[i] >= 32 && bytes[i] < 127) ? (char)bytes[i] : '.';
            }
            return $"hex=[{string.Join(" ", hexParts)}] ascii=\"{new string(asciiParts)}\"";
        }

        private static string SaveRawDump(byte[] bytes)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dumpDir = Path.Combine(localAppData, "MiamiGraphics", "_DebugLogs");
                Directory.CreateDirectory(dumpDir);
                string dumpPath = Path.Combine(dumpDir, "content_xml_dump.bin");
                File.WriteAllBytes(dumpPath, bytes);
                return dumpPath;
            }
            catch (Exception ex)
            {
                return $"<save failed: {ex.Message}>";
            }
        }

        private static byte[] GetDecodedFileBytes(IArchiveBinaryFile binFile)
        {
            byte[] buf;
            using (var ms = new MemoryStream())
            {
                binFile.Export(ms);
                buf = ms.ToArray();
            }

            if (binFile.IsEncrypted)
            {
                var hash = GTA5Hash.CalculateHash(binFile.Name);
                var keyIdx = (hash + (uint)binFile.UncompressedSize + (101 - 40)) % 0x65;
                var key = GTA5Constants.PC_NG_KEYS[keyIdx];
                if (key != null && key.Length > 0)
                {
                    buf = GTA5Crypto.Decrypt(buf, key);
                }
            }

            if (binFile.IsCompressed)
            {
                using var def = new DeflateStream(
                    new MemoryStream(buf),
                    CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                def.CopyTo(outMs);
                return outMs.ToArray();
            }

            return buf;
        }
    }
}
