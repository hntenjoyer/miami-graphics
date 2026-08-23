using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Services
{
    public class GunpackBuilderService
    {
        private const string TargetDlcRelativePath = @"update\x64\dlcpacks\patchday18ng\dlc.rpf";

        private static readonly string CleanDlcTemplatePath =
            Path.Combine(MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir, "backup", "dlc.rpf");

        private static readonly string[] WeaponsRpfParentParts = { "x64", "levels", "gta5" };

        private const string TargetStreamingChangeSetName = "CCS_PATCHDAY18_NG_STREAMING";

        private const string ContentXmlDevicePrefix = "dlc_PATCHDAY18NG";

        private const string HntgraphRpfName = "hntgraph.rpf";

        public class InstallFullPackRequest
        {
            public string GunpackDir { get; set; } = "";
            public string GtaPath { get; set; } = "";
        }

        public class InstallFullPackResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public string? TargetDlcRpfPath { get; set; }
            public long WeaponsRpfSize { get; set; }
        }

        public InstallFullPackResult InstallFullPack(InstallFullPackRequest req)
        {
            var result = new InstallFullPackResult();

            if (!Directory.Exists(req.GunpackDir))
            {
                result.Message = $"GunpackDir не найден: {req.GunpackDir}";
                return result;
            }

            string infoPath = Path.Combine(req.GunpackDir, "gunpack_info.json");
            if (!File.Exists(infoPath))
            {
                result.Message = $"gunpack_info.json не найден в {req.GunpackDir}";
                return result;
            }

            GunpackInfo? info;
            try
            {
                info = JsonSerializer.Deserialize<GunpackInfo>(File.ReadAllText(infoPath));
            }
            catch (Exception ex)
            {
                result.Message = $"Ошибка чтения gunpack_info.json: {ex.Message}";
                return result;
            }
            if (info == null)
            {
                result.Message = "gunpack_info.json пустой/повреждён";
                return result;
            }

            if (string.IsNullOrWhiteSpace(req.GtaPath) || !Directory.Exists(req.GtaPath))
            {
                result.Message = $"GtaPath некорректен: {req.GtaPath}";
                return result;
            }

            string weaponsRpfPath = Path.Combine(req.GunpackDir, info.WeaponsRpfName);
            if (!File.Exists(weaponsRpfPath))
            {
                result.Message = $"{info.WeaponsRpfName} не найден в {req.GunpackDir}. Ганпак повреждён - перепарсить.";
                return result;
            }

            byte[] weaponsBytes;
            try
            {
                weaponsBytes = File.ReadAllBytes(weaponsRpfPath);
            }
            catch (Exception ex)
            {
                result.Message = $"Не удалось прочитать {info.WeaponsRpfName}: {ex.Message}";
                return result;
            }

            if (weaponsBytes.Length == 0)
            {
                result.Message = $"{info.WeaponsRpfName} пустой (0 байт)";
                return result;
            }

            Console.WriteLine($"[Gunpack] Загружен {info.WeaponsRpfName}: {weaponsBytes.LongLength / 1024 / 1024} МБ");

            string targetDlcRpf = Path.Combine(req.GtaPath, TargetDlcRelativePath);

            if (!RestoreCleanTargetDlc(targetDlcRpf, out string restoreErr))
            {
                result.Message = restoreErr;
                return result;
            }

            result.TargetDlcRpfPath = targetDlcRpf;
            result.WeaponsRpfSize = weaponsBytes.LongLength;

            if (!TryInjectRpfIntoTarget(targetDlcRpf, info.WeaponsRpfName, weaponsBytes))
            {
                result.Message = "Ошибка при инжекте weapons.rpf в target dlc.rpf";
                return result;
            }

            if (!TryRegisterRpfInContentXml(targetDlcRpf, info.WeaponsRpfName))
            {
                result.Message = "weapons.rpf вброшен в архив, но content.xml обновить не удалось. Ганпак НЕ подхватится игрой.";
                return result;
            }

            FixTargetDlcChecksums(targetDlcRpf);

            result.Success = true;
            result.Message = $"Ганпак {info.Name} установлен в patchday18ng";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Gunpack] ✓ {result.Message}");
            Console.ResetColor();
            return result;
        }

        public static bool RestoreCleanTargetDlc(string targetDlcRpf, out string errorMessage)
        {
            errorMessage = "";

            if (!File.Exists(CleanDlcTemplatePath))
            {
                errorMessage = $"Эталон чистого dlc.rpf не найден: {CleanDlcTemplatePath}\n" +
                               "Положите туда dlc.rpf (распространяется с программой).";
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Gunpack] {errorMessage}");
                Console.ResetColor();
                return false;
            }

            string? targetDir = Path.GetDirectoryName(targetDlcRpf);
            if (string.IsNullOrEmpty(targetDir))
            {
                errorMessage = $"Некорректный target path: {targetDlcRpf}";
                return false;
            }

            if (!Directory.Exists(targetDir))
            {
                errorMessage = $"Папка target не существует: {targetDir}\n" +
                               "Ожидалось что patchday18ng установлен в GTA.";
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Gunpack] {errorMessage}");
                Console.ResetColor();
                return false;
            }

            try
            {
                File.Copy(CleanDlcTemplatePath, targetDlcRpf, overwrite: true);
                long size = new FileInfo(targetDlcRpf).Length;
                Console.WriteLine($"[Gunpack] Чистый dlc.rpf восстановлен в target ({size / 1024} КБ).");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Не удалось скопировать чистый dlc.rpf: {ex.Message}";
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Gunpack] {errorMessage}");
                Console.ResetColor();
                return false;
            }
        }

        public bool RollbackToClean(string gtaPath, out string errorMessage)
        {
            errorMessage = "";
            if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
            {
                errorMessage = $"GtaPath некорректен: {gtaPath}";
                return false;
            }

            string targetDlcRpf = Path.Combine(gtaPath, TargetDlcRelativePath);
            return RestoreCleanTargetDlc(targetDlcRpf, out errorMessage);
        }

        public bool RebuildTargetDlc(string gtaPath, InstalledModsJournal journal, InstalledModsService journalService)
        {
            if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
            {
                Console.WriteLine($"[GunpackRebuild] gtaPath некорректен: {gtaPath}");
                return false;
            }

            string targetDlcRpf = Path.Combine(gtaPath, TargetDlcRelativePath);

            if (!RestoreCleanTargetDlc(targetDlcRpf, out string restoreErr))
            {
                Console.WriteLine($"[GunpackRebuild] {restoreErr}");
                return false;
            }

            bool mutated = false;

            if (journal.ActiveGunpack != null)
            {
                string gunpackDir = journal.ActiveGunpack.GunpackDir;
                string infoPath = Path.Combine(gunpackDir, "gunpack_info.json");
                if (!File.Exists(infoPath))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[GunpackRebuild] WARN: активный ганпак потерян ({gunpackDir}), пропускаем.");
                    Console.ResetColor();
                }
                else
                {
                    try
                    {
                        var info = JsonSerializer.Deserialize<GunpackInfo>(File.ReadAllText(infoPath));
                        if (info != null)
                        {
                            string weaponsRpfPath = Path.Combine(gunpackDir, info.WeaponsRpfName);
                            if (File.Exists(weaponsRpfPath))
                            {
                                byte[] weaponsBytes = File.ReadAllBytes(weaponsRpfPath);
                                if (TryInjectRpfIntoTarget(targetDlcRpf, info.WeaponsRpfName, weaponsBytes))
                                {
                                    TryRegisterRpfInContentXml(targetDlcRpf, info.WeaponsRpfName);
                                    mutated = true;
                                    Console.WriteLine($"[GunpackRebuild] + ганпак {journal.ActiveGunpack.Name} ({info.WeaponsRpfName})");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[GunpackRebuild] WARN на ганпаке: {ex.Message}");
                        Console.ResetColor();
                    }
                }
            }

            if (journal.Hntgraph.SelectedGuns.Count > 0)
            {
                string workDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics.Hntgraph");
                Directory.CreateDirectory(workDir);
                string hntgraphOut = Path.Combine(workDir, "hntgraph.rpf");

                var hntSvc = new HntgraphBuilderService();
                var buildRes = hntSvc.Build(new HntgraphBuilderService.BuildRequest
                {
                    SelectedGuns = journal.Hntgraph.SelectedGuns,
                    OutputHntgraphPath = hntgraphOut
                });

                if (buildRes.Success)
                {
                    byte[] hntBytes = File.ReadAllBytes(hntgraphOut);
                    if (TryInjectRpfIntoTarget(targetDlcRpf, HntgraphRpfName, hntBytes))
                    {
                        TryRegisterRpfInContentXml(targetDlcRpf, HntgraphRpfName);
                        mutated = true;
                        Console.WriteLine($"[GunpackRebuild] + hntgraph ({buildRes.GunsApplied} ганов)");

                        journal.Hntgraph.LastBuiltAt = DateTime.Now;
                        journal.Hntgraph.LastBuiltSize = buildRes.FinalSize;
                        journal.Hntgraph.LastBuiltSha256 = buildRes.Sha256;
                        journalService.Save(journal);
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[GunpackRebuild] WARN hntgraph: {buildRes.Message}");
                    Console.ResetColor();
                }
            }
            else
            {
                if (journal.Hntgraph.LastBuiltSha256 != null)
                {
                    journal.Hntgraph.LastBuiltAt = null;
                    journal.Hntgraph.LastBuiltSize = 0;
                    journal.Hntgraph.LastBuiltSha256 = null;
                    journalService.Save(journal);
                }
            }

            if (mutated)
                FixTargetDlcChecksums(targetDlcRpf);

            return true;
        }

        private static void FixTargetDlcChecksums(string targetDlcRpf)
        {
            try
            {
                using (var archive = RageArchiveWrapper7.Open(targetDlcRpf))
                {
                    archive.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                    archive.Flush();
                }
                bool ok = MiamiGraphics.Core.Injector.ArchiveFixer.Fix(targetDlcRpf);
                Console.WriteLine($"[Gunpack] ArchiveFix dlc.rpf: {(ok ? "OK" : "FAILED (архив остаётся OPEN)")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gunpack] ArchiveFix dlc.rpf пропущен: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static IArchiveDirectory? NavigateTo(IArchiveDirectory root, string[] parts)
        {
            IArchiveDirectory? cur = root;
            foreach (var p in parts)
            {
                cur = cur?.GetDirectories().FirstOrDefault(d => d.Name.Equals(p, StringComparison.OrdinalIgnoreCase));
                if (cur == null) return null;
            }
            return cur;
        }

        private static bool TryRegisterRpfInContentXml(string targetDlcRpfPath, string rpfName)
        {
            try
            {
                using var archive = RageArchiveWrapper7.Open(targetDlcRpfPath);

                var contentFile = archive.Root.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase))
                    as IArchiveBinaryFile;

                if (contentFile == null)
                {
                    Console.WriteLine("[Gunpack] content.xml не найден в корне target dlc.rpf");
                    return false;
                }

                bool wasCompressed = contentFile.IsCompressed;
                bool wasEncrypted = contentFile.IsEncrypted;

                byte[] xmlBytes = ReadRealBytes(contentFile);
                if (xmlBytes == null || xmlBytes.Length == 0)
                {
                    Console.WriteLine("[Gunpack] content.xml пустой после извлечения");
                    return false;
                }

                string xmlText = DecodeXmlText(xmlBytes);
                if (string.IsNullOrWhiteSpace(xmlText))
                {
                    Console.WriteLine("[Gunpack] content.xml не удалось декодировать в текст");
                    return false;
                }

                XDocument doc;
                try
                {
                    doc = XDocument.Parse(xmlText);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Gunpack] content.xml битый: {ex.Message}");
                    return false;
                }

                string filenameValue = $"{ContentXmlDevicePrefix}:/%PLATFORM%/levels/gta5/{rpfName}";

                bool modified = false;

                var dataFilesRoot = doc.Descendants("dataFiles").FirstOrDefault();
                if (dataFilesRoot == null)
                {
                    Console.WriteLine("[Gunpack] В content.xml не найден <dataFiles>");
                    return false;
                }

                bool alreadyInDataFiles = dataFilesRoot.Elements("Item").Any(item =>
                {
                    var fn = item.Element("filename")?.Value;
                    return fn != null && fn.Equals(filenameValue, StringComparison.OrdinalIgnoreCase);
                });

                if (!alreadyInDataFiles)
                {
                    var newItem = new XElement("Item",
                        new XElement("filename", filenameValue),
                        new XElement("fileType", "RPF_FILE"),
                        new XElement("locked", new XAttribute("value", "true")),
                        new XElement("disabled", new XAttribute("value", "true")),
                        new XElement("persistent", new XAttribute("value", "true")),
                        new XElement("overlay", new XAttribute("value", "true"))
                    );
                    dataFilesRoot.Add(newItem);
                    modified = true;
                    Console.WriteLine($"[Gunpack] content.xml: добавлена запись в <dataFiles>");
                }
                else
                {
                    Console.WriteLine($"[Gunpack] content.xml: запись в <dataFiles> уже есть (дубль не создаём)");
                }

                var changeSet = doc.Descendants("Item")
                    .FirstOrDefault(item =>
                    {
                        var name = item.Element("changeSetName")?.Value;
                        return name != null && name.Equals(TargetStreamingChangeSetName, StringComparison.OrdinalIgnoreCase);
                    });

                if (changeSet == null)
                {
                    Console.WriteLine($"[Gunpack] В content.xml не найден changeSet {TargetStreamingChangeSetName}");
                    return false;
                }

                var filesToEnable = changeSet.Element("filesToEnable");
                if (filesToEnable == null)
                {
                    filesToEnable = new XElement("filesToEnable");
                    changeSet.Add(filesToEnable);
                }

                string filesToEnableValue = $"dlc_PATCHDAY18ng:/%PLATFORM%/levels/gta5/{rpfName}";

                bool alreadyInFilesToEnable = filesToEnable.Elements("Item")
                    .Any(it => string.Equals(it.Value, filesToEnableValue, StringComparison.OrdinalIgnoreCase));

                if (!alreadyInFilesToEnable)
                {
                    filesToEnable.Add(new XElement("Item", filesToEnableValue));
                    modified = true;
                    Console.WriteLine($"[Gunpack] content.xml: добавлена запись в <filesToEnable> ({TargetStreamingChangeSetName})");
                }
                else
                {
                    Console.WriteLine($"[Gunpack] content.xml: запись в <filesToEnable> уже есть (дубль не создаём)");
                }

                if (!modified)
                {
                    Console.WriteLine("[Gunpack] content.xml: обе записи уже были, ничего не меняем.");
                    return true;
                }

                string newXml;
                using (var sw = new StringWriter())
                {
                    doc.Save(sw, SaveOptions.None);
                    newXml = sw.ToString();
                }

                byte[] newRawBytes = Encoding.UTF8.GetBytes(newXml);

                contentFile.IsEncrypted = false;
                byte[] toImport;
                if (wasCompressed)
                {
                    using var msDef = new MemoryStream();
                    using (var def = new DeflateStream(msDef, CompressionMode.Compress, true))
                    {
                        def.Write(newRawBytes, 0, newRawBytes.Length);
                    }
                    toImport = msDef.ToArray();
                    contentFile.IsCompressed = true;
                    contentFile.UncompressedSize = newRawBytes.LongLength;
                }
                else
                {
                    toImport = newRawBytes;
                    contentFile.IsCompressed = false;
                    contentFile.UncompressedSize = newRawBytes.LongLength;
                }

                using (var ms = new MemoryStream(toImport))
                {
                    contentFile.Import(ms);
                }

                archive.Flush();
                Console.WriteLine($"[Gunpack] content.xml обновлён (raw={newRawBytes.Length}, stored={toImport.Length}, compressed={wasCompressed}, wasEncrypted={wasEncrypted}→false).");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gunpack] Ошибка обновления content.xml: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        private static byte[] ReadRealBytes(IArchiveBinaryFile binFile)
        {
            using var ms = new MemoryStream();
            binFile.Export(ms);
            byte[] buf = ms.ToArray();

            if (binFile.IsEncrypted)
            {
                var hash = GTA5Hash.CalculateHash(binFile.Name);
                var keyIdx = (hash + (uint)binFile.UncompressedSize + (101 - 40)) % 0x65;
                var key = GTA5Constants.PC_NG_KEYS[keyIdx];

                if (key != null && key.Length > 0)
                {
                    buf = GTA5Crypto.Decrypt(buf, key);
                }
                else
                {
                    Console.WriteLine($"[Gunpack] WARN: Нет NG ключа для {binFile.Name} (idx={keyIdx})");
                }
            }

            if (binFile.IsCompressed)
            {
                using var def = new DeflateStream(new MemoryStream(buf), CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                def.CopyTo(outMs);
                return outMs.ToArray();
            }

            return buf;
        }

        private static string DecodeXmlText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";

            using (var ms = new MemoryStream(bytes))
            using (var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                string text = reader.ReadToEnd();
                if (LooksLikeXml(text)) return text;
            }

            string utf8 = Encoding.UTF8.GetString(bytes);
            if (LooksLikeXml(utf8)) return utf8;

            string utf16 = Encoding.Unicode.GetString(bytes);
            if (LooksLikeXml(utf16)) return utf16;

            string utf16be = Encoding.BigEndianUnicode.GetString(bytes);
            if (LooksLikeXml(utf16be)) return utf16be;

            return "";
        }

        private static bool LooksLikeXml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.TrimStart('﻿', '\0', ' ', '\t', '\r', '\n');
            return trimmed.StartsWith("<") &&
                   (trimmed.Contains("<content") ||
                    trimmed.Contains("<filesToEnable") ||
                    trimmed.Contains("<?xml"));
        }

        private static bool TryInjectRpfIntoTarget(string targetDlcRpfPath, string rpfName, byte[] newBytes)
        {
            try
            {
                using var archive = RageArchiveWrapper7.Open(targetDlcRpfPath);

                var dir = NavigateTo(archive.Root, WeaponsRpfParentParts);
                if (dir == null)
                {
                    Console.WriteLine($"[Gunpack] Не найдена цепочка {string.Join("/", WeaponsRpfParentParts)} в target dlc.rpf");
                    return false;
                }

                var existingFile = dir.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(rpfName, StringComparison.OrdinalIgnoreCase));

                if (existingFile is IArchiveBinaryFile existingBin)
                {
                    using var ms = new MemoryStream(newBytes);
                    existingBin.Import(ms);
                    existingBin.IsCompressed = false;
                    existingBin.IsEncrypted = false;
                    existingBin.UncompressedSize = (uint)newBytes.Length;
                    Console.WriteLine($"[Gunpack] {rpfName} заменён (Import).");
                }
                else
                {
                    var newFile = dir.CreateBinaryFile();
                    newFile.Name = rpfName;
                    newFile.IsEncrypted = false;
                    newFile.IsCompressed = false;
                    newFile.UncompressedSize = newBytes.LongLength;
                    using var ms = new MemoryStream(newBytes);
                    newFile.Import(ms);
                    Console.WriteLine($"[Gunpack] {rpfName} создан (Create+Import).");
                }

                archive.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gunpack] Ошибка при инжекте в target: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }
    }
}
