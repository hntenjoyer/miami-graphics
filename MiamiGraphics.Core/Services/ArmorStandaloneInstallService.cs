#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Parser;
using MiamiGraphics.Core.System;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Services
{

    public sealed class ArmorStandaloneInstallService
    {
        private readonly BackupService _backupService;

        public ArmorStandaloneInstallService(BackupService backupService)
        {
            _backupService = backupService;
        }

        public const string CanonicalArmorRpfPath = "miamigraphics/miami_graphics_armor.rpf";

        public sealed class InstallResult
        {
            public bool Success { get; init; }
            public string ErrorMessage { get; init; } = "";
            public string WorkDir { get; init; } = "";
        }

        public async Task<InstallResult> InstallAsync(
            string gtaRoot,
            string donorPatchRoot,
            Action<string, int> progress = null,
            CancellationToken ct = default,
            bool restoreFromClean = true)
        {
            string updateRpf = Path.Combine(gtaRoot, "update", "update.rpf");
            if (!File.Exists(updateRpf))
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.updateRpfNotFound"), WorkDir = "" };

            if (restoreFromClean)
            {
                progress?.Invoke("preparing", 5);
                try
                {
                    bool restored = await _backupService.RestoreFromCleanAsync(gtaRoot, ct);
                    if (!restored)
                    {
                        return new InstallResult
                        {
                            Success = false,
                            ErrorMessage = Loc.T("error.cleanUpdateRpfMissingLocally"),
                            WorkDir = "",
                        };
                    }
                }
                catch (Exception ex)
                {
                    return new InstallResult { Success = false, ErrorMessage = Loc.T("error.cleanUpdateRpfRestoreFailed", ("reason", ex.Message)), WorkDir = "" };
                }
            }
            else
            {
                progress?.Invoke("preparing", 5);
            }

            string workDir = Path.Combine(
                Path.GetTempPath(),
                "MiamiGraphics",
                "armor_only_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(workDir);
            string patchFilesDir = Path.Combine(workDir, "patch_files");
            Directory.CreateDirectory(patchFilesDir);

            progress?.Invoke("preparing", 15);
            string contentXmlPath = Path.Combine(patchFilesDir, "content.xml");
            try
            {
                ExtractContentXml(updateRpf, contentXmlPath);
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.contentXmlExtractFailed", ("reason", ex.Message)), WorkDir = workDir };
            }
            if (!File.Exists(contentXmlPath))
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.contentXmlNotAtRoot"), WorkDir = workDir };

            var existingArmorPaths = DiscoverExistingArmorEntries(contentXmlPath);
            var seededInternalPaths = new List<string> { CanonicalArmorRpfPath };
            foreach (var p in existingArmorPaths)
            {
                if (!seededInternalPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    seededInternalPaths.Add(p);
            }
            Console.WriteLine($"[ArmorStandalone] sweep: discovered {existingArmorPaths.Count} existing armor entries → seed {seededInternalPaths.Count} for removal");

            var manifest = new DiffManifest
            {
                ReduxName = "armor_standalone",
                ParsedAt = DateTime.Now,
                Actions = new List<PatchAction>(),
            };
            var seededRecipientMap = new ResolvedComponentMap();
            seededRecipientMap.Components["armor"] = new ComponentInfo
            {
                IsFound = true,
                SourceRpf = CanonicalArmorRpfPath,
                InternalPaths = seededInternalPaths,
                Flags = new List<string> { "transferable", "clearable" },
            };
            File.WriteAllText(
                Path.Combine(workDir, "component_map.json"),
                JsonSerializer.Serialize(seededRecipientMap,
                    new JsonSerializerOptions { WriteIndented = true }));

            try
            {
                CanonicalizeDonor(donorPatchRoot);
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.donorCanonicalPathFailed", ("reason", ex.Message)), WorkDir = workDir };
            }

            progress?.Invoke("applying", 30);
            var transferSvc = new ArmorTransferService();
            var transfer = transferSvc.Transfer(new ArmorTransferRequest
            {
                RecipientPatchRoot = workDir,
                RecipientManifest = manifest,
                DonorPatchRoot = donorPatchRoot,
            });
            if (!transfer.Success)
                return new InstallResult { Success = false, ErrorMessage = "ArmorTransfer: " + transfer.Message, WorkDir = workDir };

            try
            {
                int added = ExpandArmorDrawables(patchFilesDir);
                if (added > 0)
                    Console.WriteLine($"[ArmorStandalone] model-fix: duplicated drawable(s) to {added} extra index(es)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArmorStandalone] model-fix skipped: {ex.GetType().Name}: {ex.Message}");
            }

            progress?.Invoke("applying", 50);
            try
            {
                byte[] contentXmlBytes = File.ReadAllBytes(contentXmlPath);
                manifest.Actions.Add(new PatchAction
                {
                    Type = ActionType.Replace,
                    TargetPath = "content.xml",
                    SourcePath = "patch_files/content.xml",
                    Size = contentXmlBytes.LongLength,
                    Sha256 = ComputeSha256(contentXmlBytes),
                    IsWholeReplaceNestedRpf = false,
                });
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.manifestAddContentXmlFailed", ("reason", ex.Message)), WorkDir = workDir };
            }

            try
            {
                File.WriteAllText(
                    Path.Combine(workDir, "manifest.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.manifestJsonWriteFailed", ("reason", ex.Message)), WorkDir = workDir };
            }

            progress?.Invoke("injecting", 65);
            try
            {
                var engine = new RpfInjectEngine(gtaRoot);
                bool injectOk = await Task.Run(() => engine.InjectPatch(workDir), ct);
                if (!injectOk)
                    return new InstallResult { Success = false, ErrorMessage = string.IsNullOrWhiteSpace(engine.LastError) ? Loc.T("error.injectPatchReturnedFalse") : engine.LastError, WorkDir = workDir };
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.injectFailed", ("reason", ex.GetType().Name + ": " + ex.Message)), WorkDir = workDir };
            }

            progress?.Invoke("done", 100);
            return new InstallResult { Success = true, ErrorMessage = "", WorkDir = workDir };
        }

        public async Task<InstallResult> InstallClearAsync(
            string gtaRoot,
            Action<string, int> progress = null,
            CancellationToken ct = default)
        {
            string updateRpf = Path.Combine(gtaRoot, "update", "update.rpf");
            if (!File.Exists(updateRpf))
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.updateRpfNotFound"), WorkDir = "" };

            progress?.Invoke("preparing", 10);
            string workDir = Path.Combine(
                Path.GetTempPath(),
                "MiamiGraphics",
                "armor_clear_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(workDir);
            string patchFilesDir = Path.Combine(workDir, "patch_files");
            Directory.CreateDirectory(patchFilesDir);

            progress?.Invoke("preparing", 20);
            string contentXmlPath = Path.Combine(patchFilesDir, "content.xml");
            try
            {
                ExtractContentXml(updateRpf, contentXmlPath);
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.contentXmlExtractFailed", ("reason", ex.Message)), WorkDir = workDir };
            }
            if (!File.Exists(contentXmlPath))
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.contentXmlNotAtRoot"), WorkDir = workDir };

            progress?.Invoke("preparing", 30);
            string synthDonor = Path.Combine(workDir, "synth_donor");
            string synthArmorDir = Path.Combine(synthDonor, "components", "armor");
            Directory.CreateDirectory(synthArmorDir);
            string synthArmorPath = Path.Combine(
                synthArmorDir,
                CanonicalArmorRpfPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(synthArmorPath)!);
            try
            {
                CreateEmptyRpf7(synthArmorPath);
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.emptyArmorRpfFailed", ("reason", ex.Message)), WorkDir = workDir };
            }

            var donorMap = new ResolvedComponentMap();
            donorMap.Components["armor"] = new ComponentInfo
            {
                IsFound = true,
                SourceRpf = CanonicalArmorRpfPath,
                InternalPaths = new List<string> { CanonicalArmorRpfPath },
                Flags = new List<string> { "transferable", "clearable" },
            };
            File.WriteAllText(
                Path.Combine(synthDonor, "component_map.json"),
                JsonSerializer.Serialize(donorMap, new JsonSerializerOptions { WriteIndented = true }));

            var manifest = new DiffManifest
            {
                ReduxName = "armor_clear_overlay",
                ParsedAt = DateTime.Now,
                Actions = new List<PatchAction>(),
            };

            var existingArmorPaths = DiscoverExistingArmorEntries(contentXmlPath);
            var seededInternalPaths = new List<string> { CanonicalArmorRpfPath };
            foreach (var p in existingArmorPaths)
            {
                if (!seededInternalPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    seededInternalPaths.Add(p);
            }
            Console.WriteLine($"[ArmorClear] sweep: discovered {existingArmorPaths.Count} existing armor entries → seed {seededInternalPaths.Count} for removal");

            var seededRecipientMap = new ResolvedComponentMap();
            seededRecipientMap.Components["armor"] = new ComponentInfo
            {
                IsFound = true,
                SourceRpf = CanonicalArmorRpfPath,
                InternalPaths = seededInternalPaths,
                Flags = new List<string> { "transferable", "clearable" },
            };
            File.WriteAllText(
                Path.Combine(workDir, "component_map.json"),
                JsonSerializer.Serialize(seededRecipientMap,
                    new JsonSerializerOptions { WriteIndented = true }));

            progress?.Invoke("applying", 50);
            var transferSvc = new ArmorTransferService();
            var transfer = transferSvc.Transfer(new ArmorTransferRequest
            {
                RecipientPatchRoot = workDir,
                RecipientManifest = manifest,
                DonorPatchRoot = synthDonor,
            });
            if (!transfer.Success)
                return new InstallResult { Success = false, ErrorMessage = "ArmorTransfer (clear): " + transfer.Message, WorkDir = workDir };

            progress?.Invoke("applying", 60);
            try
            {
                byte[] contentXmlBytes = File.ReadAllBytes(contentXmlPath);
                manifest.Actions.Add(new PatchAction
                {
                    Type = ActionType.Replace,
                    TargetPath = "content.xml",
                    SourcePath = "patch_files/content.xml",
                    Size = contentXmlBytes.LongLength,
                    Sha256 = ComputeSha256(contentXmlBytes),
                    IsWholeReplaceNestedRpf = false,
                });
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.manifestAddContentXmlFailed", ("reason", ex.Message)), WorkDir = workDir };
            }

            try
            {
                File.WriteAllText(
                    Path.Combine(workDir, "manifest.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.manifestJsonWriteFailed", ("reason", ex.Message)), WorkDir = workDir };
            }

            progress?.Invoke("injecting", 75);
            try
            {
                var engine = new RpfInjectEngine(gtaRoot);
                bool injectOk = await Task.Run(() => engine.InjectPatch(workDir), ct);
                if (!injectOk)
                    return new InstallResult { Success = false, ErrorMessage = string.IsNullOrWhiteSpace(engine.LastError) ? Loc.T("error.injectPatchClearReturnedFalse") : engine.LastError, WorkDir = workDir };
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = Loc.T("error.injectClearFailed", ("reason", ex.GetType().Name + ": " + ex.Message)), WorkDir = workDir };
            }

            progress?.Invoke("done", 100);
            return new InstallResult { Success = true, ErrorMessage = "", WorkDir = workDir };
        }

        private static void ExtractContentXml(string updateRpfPath, string outputPath)
        {
            using var stream = File.Open(updateRpfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var arc = RageArchiveWrapper7.Open(stream, Path.GetFileName(updateRpfPath), true);
            var contentXmlEntry = arc.Root.GetFiles()
                .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
            if (contentXmlEntry == null) return;

            byte[] plain;
            if (contentXmlEntry is IArchiveBinaryFile binFile)
            {
                using var ms = new MemoryStream();
                binFile.Export(ms);
                byte[] buf = ms.ToArray();

                if (binFile.IsEncrypted)
                {
                    var hash = GTA5Hash.CalculateHash(binFile.Name);
                    var keyIdx = (hash + (uint)binFile.UncompressedSize + (101 - 40)) % 0x65;
                    var key = GTA5Constants.PC_NG_KEYS?[keyIdx];
                    if (key != null && key.Length > 0)
                        buf = GTA5Crypto.Decrypt(buf, key);
                }

                if (binFile.IsCompressed)
                {
                    using var def = new global::System.IO.Compression.DeflateStream(
                        new MemoryStream(buf),
                        global::System.IO.Compression.CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    def.CopyTo(outMs);
                    plain = outMs.ToArray();
                }
                else
                {
                    plain = buf;
                }
            }
            else
            {
                using var ms = new MemoryStream();
                contentXmlEntry.Export(ms);
                plain = ms.ToArray();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, plain);
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static List<string> DiscoverExistingArmorEntries(string contentXmlPath)
        {
            var result = new List<string>();
            if (!File.Exists(contentXmlPath))
                return result;
            try
            {
                var doc = ArmorTransferService.LoadXmlTolerant(contentXmlPath);
                var dataFiles = doc.Root?.Element("dataFiles");
                if (dataFiles == null)
                    return result;
                foreach (var item in dataFiles.Elements("Item"))
                {
                    var filename = item.Element("filename")?.Value?.Trim() ?? "";
                    if (string.IsNullOrEmpty(filename)) continue;
                    if (!filename.EndsWith("armor.rpf", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string internalPath = filename;
                    if (internalPath.StartsWith("update:/", StringComparison.OrdinalIgnoreCase))
                        internalPath = internalPath.Substring("update:/".Length);
                    if (!result.Contains(internalPath, StringComparer.OrdinalIgnoreCase))
                        result.Add(internalPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArmorStandalone] DiscoverExistingArmorEntries failed: {ex.Message}");
            }
            return result;
        }

        private static void CreateEmptyRpf7(string targetPath)
        {
            if (File.Exists(targetPath)) File.Delete(targetPath);
            using var arc = RageArchiveWrapper7.Create(targetPath);
            arc.FileName = Path.GetFileName(targetPath);
            arc.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
            arc.Flush();
        }

        private static readonly Regex DrawableRx = new(@"^([a-z]+)_(\d{3})_([a-z])\.ydd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TextureRx  = new(@"^([a-z]+)_diff_(\d{3})_", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static int ExpandArmorDrawables(string patchFilesDir)
        {
            string rpfPath = Directory.Exists(patchFilesDir)
                ? Directory.EnumerateFiles(patchFilesDir, "*armor.rpf", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (string.IsNullOrEmpty(rpfPath) || !File.Exists(rpfPath))
                return 0;

            string tmp = rpfPath + ".expand_tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            int added = 0;
            using (var src = RageArchiveWrapper7.Open(rpfPath))
            using (var dst = RageArchiveWrapper7.Create(tmp))
            {
                ExpandDir(src.Root, dst.Root, ref added);
                dst.FileName = Path.GetFileName(rpfPath);
                dst.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                dst.Flush();
            }
            if (added > 0)
            {
                File.Delete(rpfPath);
                File.Move(tmp, rpfPath);
            }
            else
            {
                try { File.Delete(tmp); } catch { }
            }
            return added;
        }

        private static void ExpandDir(IArchiveDirectory src, IArchiveDirectory dst, ref int added)
        {
            var drawableSrc = new Dictionary<string, (byte[] bytes, string suffix)>(StringComparer.OrdinalIgnoreCase);
            var drawableIdx = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var textureIdx  = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in src.GetFiles())
            {
                if (f is IArchiveResourceFile rf)
                {
                    using var ms = new MemoryStream();
                    rf.Export(ms);
                    byte[] bytes = ms.ToArray();
                    var nf = dst.CreateResourceFile();
                    nf.Name = f.Name;
                    nf.Import(new MemoryStream(bytes));

                    var md = DrawableRx.Match(f.Name);
                    if (md.Success)
                    {
                        string comp = md.Groups[1].Value.ToLowerInvariant();
                        if (!drawableIdx.TryGetValue(comp, out var set)) { set = new HashSet<string>(); drawableIdx[comp] = set; }
                        set.Add(md.Groups[2].Value);
                        if (!drawableSrc.ContainsKey(comp)) drawableSrc[comp] = (bytes, md.Groups[3].Value);
                    }
                    var mt = TextureRx.Match(f.Name);
                    if (mt.Success)
                    {
                        string comp = mt.Groups[1].Value.ToLowerInvariant();
                        if (!textureIdx.TryGetValue(comp, out var set)) { set = new HashSet<string>(); textureIdx[comp] = set; }
                        set.Add(mt.Groups[2].Value);
                    }
                }
                else if (f is IArchiveBinaryFile bf)
                {
                    var nf = dst.CreateBinaryFile();
                    nf.Name = f.Name;
                    using var ms = new MemoryStream();
                    bf.Export(ms);
                    ms.Position = 0;
                    nf.Import(ms);
                    nf.IsCompressed = bf.IsCompressed;
                    nf.IsEncrypted = bf.IsEncrypted;
                    nf.UncompressedSize = bf.UncompressedSize;
                }
            }

            foreach (var kv in drawableSrc)
            {
                string comp = kv.Key;
                if (!textureIdx.TryGetValue(comp, out var texIdxs)) continue;
                drawableIdx.TryGetValue(comp, out var have);
                foreach (var idx in texIdxs)
                {
                    if (have != null && have.Contains(idx)) continue;
                    var nf = dst.CreateResourceFile();
                    nf.Name = $"{comp}_{idx}_{kv.Value.suffix}.ydd";
                    nf.Import(new MemoryStream(kv.Value.bytes));
                    added++;
                }
            }

            foreach (var sd in src.GetDirectories())
            {
                var nd = dst.CreateDirectory();
                nd.Name = sd.Name;
                ExpandDir(sd, nd, ref added);
            }
        }

        private static void CanonicalizeDonor(string donorPatchRoot)
        {
            string componentMapPath = Path.Combine(donorPatchRoot, "component_map.json");
            if (!File.Exists(componentMapPath))
                throw new FileNotFoundException(Loc.T("error.donorComponentMapMissing"), componentMapPath);

            var donorMap = JsonSerializer.Deserialize<ResolvedComponentMap>(File.ReadAllText(componentMapPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (donorMap?.Components is null
                || !donorMap.Components.TryGetValue("armor", out var armor)
                || armor is null
                || !armor.IsFound
                || armor.InternalPaths.Count == 0)
                throw new InvalidOperationException(Loc.T("error.donorNoArmorComponent"));

            string componentDir = Path.Combine(donorPatchRoot, "components", "armor");
            string patchFilesDir = Path.Combine(donorPatchRoot, "patch_files");
            string donorRoot = Directory.Exists(componentDir) ? componentDir : patchFilesDir;

            string canonicalRel = CanonicalArmorRpfPath;
            string canonicalAbs = Path.Combine(donorRoot, canonicalRel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(canonicalAbs)!);

            bool moved = false;
            foreach (var oldRel in armor.InternalPaths)
            {
                string oldAbs = Path.Combine(donorRoot, oldRel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(oldAbs))
                {
                    Console.WriteLine($"[CanonicalizeDonor] WARN: исходный файл не найден: {oldAbs} - пропуск.");
                    continue;
                }
                if (string.Equals(Path.GetFullPath(oldAbs), Path.GetFullPath(canonicalAbs), StringComparison.OrdinalIgnoreCase))
                {
                    moved = true;
                    continue;
                }
                if (!moved)
                {
                    var before = Injector.RpfEncryptionCheck.Inspect(oldAbs);
                    if (!Injector.RpfEncryptionCheck.WillSurviveRename(oldAbs, "miami_graphics_armor.rpf"))
                    {
                        if (Injector.RpfEncryptionCheck.TryConvertToOpen(oldAbs, out var convErr))
                            Console.WriteLine($"[CanonicalizeDonor] {oldRel}: {before.Declared} → OPEN (иначе имя ломает ключ)");
                        else
                            throw new InvalidOperationException(
                                Loc.T("error.donorArmorNotConvertible", ("file", oldRel), ("reason", convErr)));
                    }

                    File.Move(oldAbs, canonicalAbs, overwrite: true);
                    moved = true;
                    Console.WriteLine($"[CanonicalizeDonor] {oldRel} → {canonicalRel}");

                    var after = Injector.RpfEncryptionCheck.Inspect(canonicalAbs);
                    if (!after.GameCanRead)
                        throw new InvalidOperationException(
                            Loc.T("error.donorArmorUnreadable", ("file", canonicalRel), ("reason", after.Detail)));
                }
                else
                {

                    try { File.Delete(oldAbs); }
                    catch (Exception ex) { Console.WriteLine($"[CanonicalizeDonor] не удалось удалить лишний файл {oldAbs}: {ex.Message}"); }
                    Console.WriteLine($"[CanonicalizeDonor] лишний armor-файл отброшен: {oldRel}");
                }
            }

            if (!moved && File.Exists(canonicalAbs))
            {
                Console.WriteLine($"[CanonicalizeDonor] броня уже под каноническим именем ({canonicalRel}) - чиню опись после прерванного запуска.");
                moved = true;
            }

            if (!moved)
            {
                string otherRoot = string.Equals(Path.GetFullPath(donorRoot), Path.GetFullPath(componentDir), StringComparison.OrdinalIgnoreCase)
                    ? patchFilesDir
                    : componentDir;
                foreach (var rel in armor.InternalPaths.Concat(new[] { canonicalRel }))
                {
                    string alt = Path.Combine(otherRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(alt)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(canonicalAbs)!);
                    File.Move(alt, canonicalAbs, overwrite: true);
                    Console.WriteLine($"[CanonicalizeDonor] найдено во втором корне: {alt} → {canonicalRel}");
                    moved = true;
                    break;
                }
            }

            if (!moved)
            {
                var looked = string.Join(" | ", armor.InternalPaths.Concat(new[] { canonicalRel })
                    .Select(p => Path.Combine(donorRoot, p.Replace('/', Path.DirectorySeparatorChar))));
                throw new FileNotFoundException(
                    Loc.T("error.donorArmorRpfMissing") + " " + Loc.T("error.donorArmorRpfLookedIn", ("paths", looked)),
                    componentMapPath);
            }

            armor.SourceRpf = canonicalRel;
            armor.InternalPaths = new List<string> { canonicalRel };
            donorMap.Components["armor"] = armor;
            File.WriteAllText(componentMapPath,
                JsonSerializer.Serialize(donorMap, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
