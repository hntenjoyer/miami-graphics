using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{
    public class GunpackParserService
    {
        private static readonly string[] GunsRpfParentPath = { "x64", "levels", "gta5" };

        private static readonly Dictionary<string, string> WeaponPrefixes = new()
        {
            { "w_ar_", "Assault Rifle" },
            { "w_sg_", "Shotgun"       },
            { "w_sr_", "Sniper Rifle"  },
            { "w_mg_", "Machine Gun"   },
            { "w_pi_", "Pistol"        },
            { "w_me_", "Melee"         },
            { "w_lr_", "Launcher"      }
        };

        private static readonly string[] AttachmentPrefixes = { "w_at_", "w_cr_" };

        private static readonly string[] AttachmentInfixes =
        {
            "_barrel_",
            "_scope_",
            "_scope.",
            "_supp_",
            "_supp.",
            "_afgrip_",
            "_afgrip.",
            "_sights_",
            "_sights.",
            "_grip_",
            "_grip.",
            "_flash_",
            "_muzzle_"
        };

        private static readonly string[] NonWeaponRpfNames =
        {
            "vehicles.rpf",
            "props.rpf",
            "scenes.rpf",
            "clip_anim@.rpf",
            "clip_veh@.rpf",
            "clip_main@.rpf"
        };

        private static readonly string[] WeaponFileMarkers =
        {
            "w_ar_", "w_sg_", "w_sr_", "w_mg_", "w_pi_",
            "w_me_", "w_lr_", "w_at_", "w_cr_"
        };

        private static readonly string[] SuffixesToStrip = { "_hi", "_mag1", "_mag2", "_mag3", "_sight", "_sight_hi" };

        public class ParseRequest
        {
            public string SourceDlcRpfPath { get; set; } = "";
            public string ResultBaseDir { get; set; } = MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir;
            public string? NameOverride { get; set; }
        }

        public class ParseResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public string? GunpackDir { get; set; }
            public GunpackInfo? Info { get; set; }
        }

        public ParseResult Parse(ParseRequest req)
        {
            var result = new ParseResult();

            if (!File.Exists(req.SourceDlcRpfPath))
            {
                result.Message = $"Source dlc.rpf не найден: {req.SourceDlcRpfPath}";
                return result;
            }

            string name = req.NameOverride ?? BuildNameFromPath(req.SourceDlcRpfPath);
            string gunpackDir = Path.Combine(req.ResultBaseDir, "Gunpacks",
                $"{SanitizeName(name)}_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(gunpackDir);
            string gunsDir = Path.Combine(gunpackDir, "guns");
            Directory.CreateDirectory(gunsDir);
            string gltfDir = Path.Combine(gunpackDir, "gltf");
            Directory.CreateDirectory(gltfDir);

            Console.WriteLine($"[GunpackParser] Парсим {req.SourceDlcRpfPath}");
            Console.WriteLine($"[GunpackParser] Результат в: {gunpackDir}");

            var info = new GunpackInfo
            {
                Name = name,
                ParsedAt = DateTime.Now,
                SourceDlcRpfPath = req.SourceDlcRpfPath
            };

            try
            {
                string cachedDlcPath = Path.Combine(gunpackDir, "dlc.rpf");
                File.Copy(req.SourceDlcRpfPath, cachedDlcPath, overwrite: true);
                Console.WriteLine($"[GunpackParser] Кеш-копия dlc.rpf: {new FileInfo(cachedDlcPath).Length / 1024 / 1024} МБ");

                using var dlcArchive = RageArchiveWrapper7.Open(cachedDlcPath);

                var allRpfFiles = new List<(string FullPath, IArchiveBinaryFile File)>();
                CollectRpfsRecursive(dlcArchive.Root, "", allRpfFiles);

                if (allRpfFiles.Count == 0)
                {
                    result.Message = "Внутри dlc.rpf не найдено ни одного вложенного .rpf файла";
                    return result;
                }

                Console.WriteLine($"[GunpackParser] Найдено .rpf внутри dlc.rpf (рекурсивно): {allRpfFiles.Count}");
                foreach (var (fp, _) in allRpfFiles)
                    Console.WriteLine($"              - {fp}");

                var filteredByName = allRpfFiles
                    .Where(t => !NonWeaponRpfNames.Contains(t.File.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (filteredByName.Count == 0)
                {
                    result.Message = "Все .rpf в DLC - дефолтные Rockstar (vehicles/props/...). Нет ни одного weapons-rpf.";
                    return result;
                }

                IArchiveBinaryFile? gunsRpfFile = null;
                string? gunsRpfFullPath = null;
                long bestScore = 0;
                foreach (var (fullPath, candidate) in filteredByName)
                {
                    long score = ScoreWeaponsRpf(candidate);
                    if (score <= 0)
                    {
                        Console.WriteLine($"[GunpackParser] Пропуск {fullPath}: внутри нет файлов w_*");
                        continue;
                    }
                    Console.WriteLine($"[GunpackParser]   кандидат {fullPath}: {score / 1024} КБ weapon-данных");
                    if (score > bestScore)
                    {
                        bestScore       = score;
                        gunsRpfFile     = candidate;
                        gunsRpfFullPath = fullPath;
                    }
                }
                if (gunsRpfFile != null)
                {
                    Console.WriteLine($"[GunpackParser] ✓ Выбран weapons-rpf: {gunsRpfFullPath} ({bestScore / 1024 / 1024} МБ weapon-данных)");
                }

                if (gunsRpfFile == null)
                {
                    result.Message = "Не найден RPF с оружейными файлами (внутри нет w_ar/w_sg/w_at/...)";
                    return result;
                }

                info.WeaponsRpfName = gunsRpfFile.Name;
                Console.WriteLine($"[GunpackParser] Найден внутренний RPF: {gunsRpfFile.Name}");

                string weaponsRpfOutPath = Path.Combine(gunpackDir, gunsRpfFile.Name);
                using (var outStream = File.Create(weaponsRpfOutPath))
                {
                    gunsRpfFile.Export(outStream);
                }
                info.WeaponsRpfSize = new FileInfo(weaponsRpfOutPath).Length;
                Console.WriteLine($"[GunpackParser] Извлечён {gunsRpfFile.Name}: {info.WeaponsRpfSize / 1024 / 1024} МБ");

                var allFilesInGuns = new List<(string FullName, byte[] Bytes)>();
                string debugParseLogPath = Path.Combine(gunpackDir, "_debug_parse.txt");
                using (var debugLog = new StreamWriter(debugParseLogPath))
                {
                    debugLog.WriteLine($"=== parse snapshot ===");
                    debugLog.WriteLine($"Source dlc.rpf: {req.SourceDlcRpfPath}");
                    debugLog.WriteLine($"Inner weapons rpf: {gunsRpfFile.Name}");
                    debugLog.WriteLine($"Inner size on disk: {info.WeaponsRpfSize}");
                    debugLog.WriteLine();
                    debugLog.WriteLine("Columns: fullName | type | size | flags | magic(first16)");
                    debugLog.WriteLine();

                    using (var weaponsStream = gunsRpfFile.GetStream())
                    using (var weaponsArchive = RageArchiveWrapper7.Open(weaponsStream, gunsRpfFile.Name, true))
                    {
                        CollectAllFilesRecursive(weaponsArchive.Root, "", allFilesInGuns, debugLog);
                    }
                }
                info.TotalFiles = allFilesInGuns.Count;
                Console.WriteLine($"[GunpackParser] Всего файлов в {gunsRpfFile.Name}: {info.TotalFiles}");

                var categoryBuckets = new Dictionary<string, List<(string FullName, byte[] Bytes)>>(StringComparer.OrdinalIgnoreCase);
                var categoryPrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (fullName, bytes) in allFilesInGuns)
                {
                    string fileName = Path.GetFileName(fullName);
                    string lower = fileName.ToLowerInvariant();

                    bool isAttachmentByPrefix = AttachmentPrefixes.Any(p =>
                        lower.StartsWith(p, StringComparison.OrdinalIgnoreCase));

                    bool isAttachmentByInfix = AttachmentInfixes.Any(inf =>
                        lower.Contains(inf, StringComparison.OrdinalIgnoreCase));

                    if (isAttachmentByPrefix || isAttachmentByInfix)
                    {
                        info.AttachmentFilesSkipped++;
                        continue;
                    }

                    string? gunPrefix = WeaponPrefixes.Keys.FirstOrDefault(p => lower.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                    if (gunPrefix == null)
                    {
                        info.UnrecognizedFiles.Add(fileName);
                        continue;
                    }

                    string nameNoPrefix = fileName.Substring(gunPrefix.Length);
                    string nameNoExt = StripAllGameExtensions(nameNoPrefix);
                    string baseName = StripGroupingSuffixes(nameNoExt);

                    string categoryKey = gunPrefix + baseName;

                    if (!categoryBuckets.ContainsKey(categoryKey))
                    {
                        categoryBuckets[categoryKey] = new List<(string, byte[])>();
                        categoryPrefixes[categoryKey] = gunPrefix;
                    }
                    categoryBuckets[categoryKey].Add((fileName, bytes));
                }

                foreach (var kvp in categoryBuckets.OrderBy(kv => kv.Key))
                {
                    string categoryKey = kvp.Key;
                    string prefix = categoryPrefixes[categoryKey];
                    string baseName = categoryKey.Substring(prefix.Length);
                    var files = kvp.Value;

                    string baseFileName = $"{prefix}{baseName}.ydr";
                    string hiFileName = $"{prefix}{baseName}_hi.ydr";
                    bool hasMainModel = files.Any(f =>
                        f.FullName.Equals(baseFileName, StringComparison.OrdinalIgnoreCase) ||
                        f.FullName.Equals(hiFileName, StringComparison.OrdinalIgnoreCase));

                    if (!hasMainModel)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[GunpackParser] SKIP категория '{baseName}' - нет base/_hi модели, только {files.Count} файлов ({string.Join(", ", files.Select(f => f.FullName))})");
                        Console.ResetColor();
                        info.AttachmentFilesSkipped += files.Count;
                        continue;
                    }

                    string zipName = $"{baseName}.zip";
                    string zipPath = Path.Combine(gunsDir, zipName);

                    using (var zipStream = File.Create(zipPath))
                    using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
                    {
                        foreach (var (fileName, bytes) in files)
                        {
                            var entry = zip.CreateEntry(fileName, CompressionLevel.Optimal);
                            using var entryStream = entry.Open();
                            entryStream.Write(bytes, 0, bytes.Length);
                        }
                    }

                    var category = new GunCategory
                    {
                        BaseName = baseName,
                        WeaponPrefix = prefix,
                        DisplayName = BuildDisplayName(baseName, prefix),
                        Files = files.Select(f => f.FullName).OrderBy(x => x).ToList(),
                        ZipFileName = zipName
                    };
                    info.Categories.Add(category);
                }

                Console.WriteLine();
                Console.WriteLine($"[GunpackParser] Конвертация в GLTF ({info.Categories.Count} категорий)...");

                int gltfCounter = 0;
                int gltfOkCount = 0;
                int gltfSkipCount = 0;

                foreach (var category in info.Categories)
                {
                    gltfCounter++;

                    string? sourceYdrFileName = PickSourceYdrForGltf(category);
                    if (sourceYdrFileName == null)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[{gltfCounter}/{info.Categories.Count}] {category.BaseName}: SKIP - нет base или _hi .ydr");
                        Console.ResetColor();
                        gltfSkipCount++;
                        continue;
                    }

                    try
                    {
                        Console.WriteLine($"[{gltfCounter}/{info.Categories.Count}] {category.BaseName} → gltf (из {sourceYdrFileName})...");

                        string workDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics.YdrToGltf", category.BaseName);
                        if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
                        Directory.CreateDirectory(workDir);

                        var sourceEntry = allFilesInGuns.FirstOrDefault(x =>
                            Path.GetFileName(x.FullName).Equals(sourceYdrFileName, StringComparison.OrdinalIgnoreCase));
                        if (sourceEntry.Bytes == null || sourceEntry.Bytes.Length == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"       WARN: источник {sourceYdrFileName} не найден в allFilesInGuns. SKIP.");
                            Console.ResetColor();
                            gltfSkipCount++;
                            continue;
                        }

                        string ydrTempPath = Path.Combine(workDir, sourceYdrFileName);
                        File.WriteAllBytes(ydrTempPath, sourceEntry.Bytes);

                        string gltfOutPath = Path.Combine(workDir, $"{category.BaseName}.gltf");

                        bool ok = YdrToGltfConverter.ConvertAsync(ydrTempPath, gltfOutPath)
                                    .GetAwaiter().GetResult();

                        if (!ok || !File.Exists(gltfOutPath))
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"       WARN: конвертация вернула false / файл не создан. SKIP.");
                            Console.ResetColor();
                            gltfSkipCount++;
                            try { Directory.Delete(workDir, recursive: true); } catch { }
                            continue;
                        }

                        string gltfZipName = $"{category.BaseName}.zip";
                        string gltfZipPath = Path.Combine(gltfDir, gltfZipName);
                        if (File.Exists(gltfZipPath)) File.Delete(gltfZipPath);

                        using (var zipFs = File.Create(gltfZipPath))
                        using (var zip = new ZipArchive(zipFs, ZipArchiveMode.Create))
                        {

                            foreach (var filePath in Directory.EnumerateFiles(workDir, "*", SearchOption.AllDirectories))
                            {
                                string fname = Path.GetFileName(filePath);
                                if (fname.Equals(sourceYdrFileName, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                string rel = Path.GetRelativePath(workDir, filePath).Replace('\\', '/');
                                AddFileToZip(zip, filePath, rel);
                            }
                        }

                        category.GltfZipFileName = gltfZipName;
                        gltfOkCount++;

                        try { Directory.Delete(workDir, recursive: true); } catch { }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"       WARN: {ex.Message}");
                        Console.ResetColor();
                        gltfSkipCount++;
                    }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[GunpackParser] GLTF: {gltfOkCount} ok, {gltfSkipCount} skip");
                Console.ResetColor();

                string infoPath = Path.Combine(gunpackDir, "gunpack_info.json");
                string json = JsonSerializer.Serialize(info, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(infoPath, json);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[GunpackParser] ✓ Готово.");
                Console.WriteLine($"              Категорий ганов: {info.Categories.Count}");
                Console.WriteLine($"              Обвесов пропущено: {info.AttachmentFilesSkipped}");
                Console.WriteLine($"              Нераспознано: {info.UnrecognizedFiles.Count}");
                if (info.UnrecognizedFiles.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("              Нераспознанные файлы:");
                    foreach (var f in info.UnrecognizedFiles.Take(10))
                        Console.WriteLine($"                - {f}");
                    if (info.UnrecognizedFiles.Count > 10)
                        Console.WriteLine($"                ... ещё {info.UnrecognizedFiles.Count - 10}");
                }
                Console.ResetColor();

                result.Success = true;
                result.GunpackDir = gunpackDir;
                result.Info = info;
                result.Message = $"Ганпак распарсен: {info.Categories.Count} категорий";
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[GunpackParser] ОШИБКА: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                result.Message = ex.Message;
            }

            return result;
        }

        public static List<GunpackInfo> ListAvailableGunpacks(string? resultBaseDir = null)
        {
            resultBaseDir ??= MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir;
            var result = new List<GunpackInfo>();
            string gunpacksRoot = Path.Combine(resultBaseDir, "Gunpacks");
            if (!Directory.Exists(gunpacksRoot)) return result;

            foreach (var dir in Directory.GetDirectories(gunpacksRoot))
            {
                string infoPath = Path.Combine(dir, "gunpack_info.json");
                if (!File.Exists(infoPath)) continue;

                try
                {
                    var info = JsonSerializer.Deserialize<GunpackInfo>(File.ReadAllText(infoPath));
                    if (info != null)
                    {
                        result.Add(info);
                    }
                }
                catch { }
            }

            return result.OrderByDescending(i => i.ParsedAt).ToList();
        }

        private static string BuildNameFromPath(string path)
        {
            string parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
            if (!string.IsNullOrWhiteSpace(parent) && !parent.Equals("dlcpacks", StringComparison.OrdinalIgnoreCase))
                return parent;
            return Path.GetFileNameWithoutExtension(path);
        }

        private static string SanitizeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
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

        private static bool LooksLikeWeaponsRpf(IArchiveBinaryFile rpfFile)
        {
            try
            {
                using var stream = rpfFile.GetStream();
                using var nested = RageArchiveWrapper7.Open(stream, rpfFile.Name, true);

                var sample = new List<string>();
                CollectFirstFileNames(nested.Root, sample, 20);

                foreach (var name in sample)
                {
                    foreach (var marker in WeaponFileMarkers)
                    {
                        if (name.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunpackParser] Не смогли прочитать {rpfFile.Name} для детекции: {ex.Message}");
                return false;
            }
        }

        private static long ScoreWeaponsRpf(IArchiveBinaryFile rpfFile)
        {
            try
            {
                using var stream = rpfFile.GetStream();
                using var nested = RageArchiveWrapper7.Open(stream, rpfFile.Name, true);
                long total = 0;
                AccumulateWeaponSizes(nested.Root, ref total);
                return total;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunpackParser] Не смогли оценить {rpfFile.Name} для скоринга: {ex.Message}");
                return 0;
            }
        }

        private static void AccumulateWeaponSizes(IArchiveDirectory dir, ref long total)
        {
            foreach (var f in dir.GetFiles())
            {
                bool isWeapon = false;
                foreach (var marker in WeaponFileMarkers)
                {
                    if (f.Name.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        isWeapon = true;
                        break;
                    }
                }
                if (!isWeapon) continue;

                long size;
                try { size = f.Size; }
                catch { size = 0; }
                if (size > 0) total += size;
            }
            foreach (var sub in dir.GetDirectories())
            {
                AccumulateWeaponSizes(sub, ref total);
            }
        }

        private static void CollectFirstFileNames(IArchiveDirectory dir, List<string> acc, int limit)
        {
            if (acc.Count >= limit) return;
            foreach (var f in dir.GetFiles())
            {
                acc.Add(f.Name);
                if (acc.Count >= limit) return;
            }
            foreach (var sub in dir.GetDirectories())
            {
                CollectFirstFileNames(sub, acc, limit);
                if (acc.Count >= limit) return;
            }
        }

        private static void CollectRpfsRecursive(
            IArchiveDirectory dir,
            string currentPath,
            List<(string FullPath, IArchiveBinaryFile File)> acc)
        {
            foreach (var f in dir.GetFiles())
            {
                if (!f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
                if (f is not IArchiveBinaryFile bin) continue;
                string full = string.IsNullOrEmpty(currentPath) ? f.Name : currentPath + "/" + f.Name;
                acc.Add((full, bin));
            }
            foreach (var sub in dir.GetDirectories())
            {
                string subPath = string.IsNullOrEmpty(currentPath) ? sub.Name : currentPath + "/" + sub.Name;
                CollectRpfsRecursive(sub, subPath, acc);
            }
        }

        private static void CollectAllFilesRecursive(IArchiveDirectory dir, string currentPath, List<(string, byte[])> acc, StreamWriter? debugLog = null)
        {
            foreach (var f in dir.GetFiles())
            {
                string fullName = string.IsNullOrEmpty(currentPath) ? f.Name : currentPath + "/" + f.Name;
                try
                {
                    using var ms = new MemoryStream();
                    f.Export(ms);
                    byte[] bytes = ms.ToArray();
                    acc.Add((fullName, bytes));

                    if (debugLog != null)
                    {
                        string typeName = f.GetType().Name;
                        string iface = (f is IArchiveBinaryFile) ? "Binary" : (f is IArchiveResourceFile ? "Resource" : "Unknown");
                        string flags = "-";
                        if (f is IArchiveBinaryFile bin)
                            flags = $"compressed={bin.IsCompressed} encrypted={bin.IsEncrypted} uncompressedSize={bin.UncompressedSize}";
                        int take = Math.Min(16, bytes.Length);
                        string hex = string.Join(" ", Enumerable.Range(0, take).Select(i => bytes[i].ToString("x2")));
                        debugLog.WriteLine($"{fullName,-50} | {iface,-8} ({typeName,-26}) | {bytes.Length,10} | {flags,-70} | {hex}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunpackParser] WARN: не смогли прочитать {fullName}: {ex.Message}");
                    debugLog?.WriteLine($"{fullName} | ERROR: {ex.Message}");
                }
            }
            foreach (var sub in dir.GetDirectories())
            {
                string subPath = string.IsNullOrEmpty(currentPath) ? sub.Name : currentPath + "/" + sub.Name;
                CollectAllFilesRecursive(sub, subPath, acc, debugLog);
            }
        }

        private static string? PickSourceYdrForGltf(GunCategory category)
        {
            string prefix = category.WeaponPrefix;
            string baseName = category.BaseName;

            string[] candidates = {
                $"{prefix}{baseName}.ydr",
                $"{prefix}{baseName}_hi.ydr"
            };

            foreach (var candidate in candidates)
            {
                if (category.Files.Any(f =>
                    Path.GetFileName(f).Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void AddFileToZip(ZipArchive zip, string sourcePath, string entryName)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var fs = File.OpenRead(sourcePath);
            fs.CopyTo(entryStream);
        }

        private static string StripAllGameExtensions(string name)
        {
            string[] knownExts = { ".ydr", ".ytd", ".yft", ".ydd" };
            bool changed;
            do
            {
                changed = false;
                foreach (var ext in knownExts)
                {
                    if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(0, name.Length - ext.Length);
                        changed = true;
                    }
                }
            } while (changed);
            return name;
        }

        private static string StripGroupingSuffixes(string name)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var suf in SuffixesToStrip)
                {
                    if (name.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(0, name.Length - suf.Length);
                        changed = true;
                    }
                }
            } while (changed);
            return name;
        }

        private static string BuildDisplayName(string baseName, string prefix)
        {
            string s = baseName;
            s = Regex.Replace(s, @"mk(\d)", m => "MK" + m.Groups[1].Value, RegexOptions.IgnoreCase);
            if (s.Length > 0) s = char.ToUpper(s[0]) + s.Substring(1);

            string prettyPrefix = WeaponPrefixes.TryGetValue(prefix, out var v) ? v : prefix;
            return $"{prettyPrefix}: {s}";
        }
    }
}
