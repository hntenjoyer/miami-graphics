using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.Core.Injector
{
    public class RpfInjectEngine
    {
        private readonly string _gtaRootPath;

        public string? LastError { get; private set; }

        public RpfInjectEngine(string gtaRootPath)
        {
            _gtaRootPath = gtaRootPath;
        }

        public bool InjectPatch(string patchDirectory)
        {
            LastError = null;
            string manifestPath = Path.Combine(patchDirectory, "manifest.json");

            if (!File.Exists(manifestPath))
            {
                SetError(Loc.T("error.manifestNotFound", ("path", manifestPath)));
                return false;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            var manifest = JsonSerializer.Deserialize<DiffManifest>(File.ReadAllText(manifestPath), options);

            if (manifest == null || manifest.Actions == null || manifest.Actions.Count == 0)
            {
                Console.WriteLine("[Injector] Манифест пуст. Нет действий для инжекта.");
                return true;
            }

            var actionsByRoot = GroupActionsByRootRpf(manifest.Actions);
            bool overallSuccess = true;

            foreach (var kvp in actionsByRoot)
            {
                string rootRpfName = kvp.Key;
                string absoluteRootPath = Path.Combine(_gtaRootPath, rootRpfName);

                if (!File.Exists(absoluteRootPath))
                {
                    SetError(Loc.T("error.rootArchiveNotFound", ("path", absoluteRootPath)));
                    overallSuccess = false;
                    continue;
                }

                Console.WriteLine($"\n[Injector] >>> Запуск Smart Rebuild для: {rootRpfName}");

                bool success = InjectSmartRebuild(absoluteRootPath, kvp.Value, patchDirectory);

                if (success)
                {
                    if (!FixArchive(absoluteRootPath))
                    {
                        SetError(Loc.T("error.archiveFixFailedRoot", ("file", rootRpfName)));
                        overallSuccess = false;
                    }
                    else if (!VerifyAndRepairPlainWrites(absoluteRootPath, kvp.Value, patchDirectory))
                    {
                        overallSuccess = false;
                    }

                    if (UpdateRpfLimits.IsUpdateRpf(absoluteRootPath))
                    {
                        var note = UpdateRpfLimits.Describe(absoluteRootPath);
                        if (note.Length > 0) Console.WriteLine($"[Injector] {note}");

                        var decl = UpdateRpfDeclarationCheck.Run(absoluteRootPath);
                        if (decl.Error.Length > 0)
                            Console.WriteLine($"[Injector] сверка объявлений не отработала: {decl.Error}");
                        else if (!decl.Ok)
                        {
                            Console.WriteLine($"[Injector] в content.xml объявлено, но в архиве нет " +
                                              $"({decl.Missing.Count} из {decl.Declared.Count}): {string.Join(", ", decl.Missing.Take(8))}");

                            var repair = ContentXmlRepair.Run(absoluteRootPath);
                            if (repair.Changed)
                                Console.WriteLine($"[Injector] content.xml починен, снято объявлений: " +
                                                  $"{repair.Removed.Count} ({string.Join(", ", repair.Removed.Take(8))})" +
                                                  (repair.Left.Count > 0 ? $"; осталось незакрытых: {repair.Left.Count}" : ""));
                            else if (repair.Error.Length > 0)
                                Console.WriteLine($"[Injector] починить content.xml не вышло: {repair.Error}");
                        }
                    }
                }
                else
                {
                    overallSuccess = false;
                }
            }

            return overallSuccess;
        }

        public bool InjectSmartRebuild(string sourceArchivePath, List<PatchAction> actions, string patchDirectory)
        {
            RemoveReadOnly(sourceArchivePath);

            string tempPath = sourceArchivePath + ".hnt_temp";
            string backupPath = sourceArchivePath + ".bak";

            var actionMap = BuildActionMap(actions);

            try
            {
                using (var cleanArchive = RageArchiveWrapper7.Open(sourceArchivePath))
                {
                    var destArchive = RageArchiveWrapper7.Create(tempPath);

                    Console.WriteLine($"[Injector] Пересборка файлового дерева...");
                    RebuildDirectory(cleanArchive.Root, destArchive.Root, "", actionMap, patchDirectory);

                    destArchive.FileName = Path.GetFileName(tempPath);
                    destArchive.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;

                    Console.WriteLine($"[Injector] Сохранение нового архива...");
                    destArchive.Flush();
                    destArchive.Dispose();
                }

                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(sourceArchivePath, backupPath);
                File.Move(tempPath, sourceArchivePath);
                File.Delete(backupPath);

                return true;
            }
            catch (Exception ex)
            {
                SetError(Loc.T("error.rebuildFailed",
                    ("archive", sourceArchivePath), ("detail", $"{ex.GetType().Name}: {ex.Message}")));

                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    if (File.Exists(backupPath) && !File.Exists(sourceArchivePath))
                        File.Move(backupPath, sourceArchivePath);
                }
                catch (Exception cleanupEx)
                {
                    SetError(LastError + Loc.T("error.tempRollbackFailedSuffix",
                        ("detail", $"{cleanupEx.GetType().Name}: {cleanupEx.Message}")));
                }

                return false;
            }
        }

        private void RebuildDirectory(IArchiveDirectory sourceDir, IArchiveDirectory destDir, string currentPath, Dictionary<string, PatchAction> actionMap, string patchDirectory)
        {
            List<string> dirsToProcess = new List<string>();
            if (sourceDir != null) dirsToProcess.AddRange(sourceDir.GetDirectories().Select(d => d.Name));

            var importDirs = actionMap.Where(x => x.Value.Type == ActionType.Import && x.Key.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase) && x.Key.Length > currentPath.Length)
                .Select(x => GetNextFolder(x.Key, currentPath)).Where(d => !string.IsNullOrEmpty(d));

            var nestedRpfsToCreate = new List<string>();

            foreach (var d in importDirs)
            {
                if (d.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                {
                    bool existsInSource = sourceDir?.GetFiles()
                        .Any(f => f.Name.Equals(d, StringComparison.OrdinalIgnoreCase)) ?? false;
                    bool hasExactAction = actionMap.ContainsKey(currentPath + d.ToLower());
                    if (!existsInSource && !hasExactAction &&
                        !nestedRpfsToCreate.Contains(d, StringComparer.OrdinalIgnoreCase))
                        nestedRpfsToCreate.Add(d);
                    continue;
                }

                if (!dirsToProcess.Contains(d, StringComparer.OrdinalIgnoreCase))
                    dirsToProcess.Add(d);
            }

            foreach (var dirName in dirsToProcess.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var cleanSub = sourceDir?.GetDirectories().FirstOrDefault(d => d.Name.Equals(dirName, StringComparison.OrdinalIgnoreCase));
                var destSub = destDir.CreateDirectory();
                destSub.Name = dirName;
                RebuildDirectory(cleanSub, destSub, currentPath + dirName.ToLower() + "/", actionMap, patchDirectory);
            }

            List<string> processedFiles = new List<string>();
            if (sourceDir != null)
            {
                foreach (var file in sourceDir.GetFiles())
                {
                    string path = currentPath + file.Name.ToLower();
                    processedFiles.Add(file.Name.ToLower());

                    if (actionMap.TryGetValue(path, out var action))
                    {
                        if (action.Type == ActionType.Delete)
                        {
                            string deletedRpfPrefix = path + "/";
                            bool hasNestedEdits = file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) &&
                                actionMap.Any(x => x.Key.StartsWith(deletedRpfPrefix, StringComparison.OrdinalIgnoreCase) &&
                                                   (x.Value.Type == ActionType.Replace || x.Value.Type == ActionType.Import));
                            if (hasNestedEdits)
                            {
                                Console.WriteLine($"[Injector] Delete '{path}' отменён: в манифесте есть вложенные правки внутри - ванильный rpf оставлен базой, правки применяются поверх.");
                                CopyFile(file, destDir, path, actionMap, patchDirectory);
                                continue;
                            }

                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.WriteLine($"[Injector] Удален: {path}");
                            Console.ResetColor();
                            continue;
                        }
                        if (action.Type == ActionType.Replace || action.Type == ActionType.Import)
                        {
                            if (!TryAddModdedRpfMergingNestedActions(destDir, file.Name, action, path, actionMap, patchDirectory))
                                AddModdedFile(destDir, file.Name, action, patchDirectory, file as IArchiveBinaryFile);
                            continue;
                        }
                    }

                    CopyFile(file, destDir, path, actionMap, patchDirectory);
                }
            }

            foreach (var rpfName in nestedRpfsToCreate)
                CreateNestedRpfFromImports(destDir, rpfName, currentPath + rpfName.ToLower() + "/",
                                           actionMap, patchDirectory);

            var exactImports = actionMap.Where(x => x.Value.Type == ActionType.Import && GetParentPath(x.Key).Equals(currentPath, StringComparison.OrdinalIgnoreCase));
            foreach (var kvp in exactImports)
            {
                string fileName = GetFileName(kvp.Key);
                if (!processedFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!TryAddModdedRpfMergingNestedActions(destDir, fileName, kvp.Value, kvp.Key, actionMap, patchDirectory))
                        AddModdedFile(destDir, fileName, kvp.Value, patchDirectory, null);
                }
            }
        }

        private void CreateNestedRpfFromImports(IArchiveDirectory destDir, string name, string nestedPrefix,
            Dictionary<string, PatchAction> actionMap, string patchDirectory)
        {
            int inner = actionMap.Count(x => x.Key.StartsWith(nestedPrefix, StringComparison.OrdinalIgnoreCase)
                                             && x.Value.Type == ActionType.Import);
            if (inner == 0) return;

            string nestedTempDir = Path.Combine(Path.GetTempPath(), "mg_newrpf_" + Guid.NewGuid().ToString("N"));
            string nestedTempPath = Path.Combine(nestedTempDir, name);
            try
            {
                Directory.CreateDirectory(nestedTempDir);
                using (var outFs = new FileStream(nestedTempPath, FileMode.Create, FileAccess.ReadWrite))
                {
                    var outArc = RageArchiveWrapper7.Create(outFs, name);
                    RebuildDirectory(null, outArc.Root, nestedPrefix, actionMap, patchDirectory);
                    outArc.FileName = name;
                    outArc.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                    outArc.Flush();
                    outArc.Dispose();
                }

                if (!FixArchive(nestedTempPath))
                    throw new InvalidOperationException(
                        Loc.T("error.archiveFixFailedNested", ("file", name)));

                var newF = destDir.CreateBinaryFile();
                newF.Name = name;
                newF.IsCompressed = false;
                newF.IsEncrypted = false;
                newF.UncompressedSize = (uint)new FileInfo(nestedTempPath).Length;
                using (var readFs = new FileStream(nestedTempPath, FileMode.Open, FileAccess.Read))
                    newF.Import(readFs);

                Console.WriteLine($"  -> Создан вложенный RPF: {name} (файлов внутри: {inner})");
            }
            finally
            {
                try { Directory.Delete(nestedTempDir, true); } catch { }
            }
        }

        private bool TryAddModdedRpfMergingNestedActions(IArchiveDirectory destDir, string name, PatchAction action, string path, Dictionary<string, PatchAction> actionMap, string patchDirectory)
        {
            if (!name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) return false;
            string nestedPrefix = path + "/";
            if (!actionMap.Keys.Any(k => k.StartsWith(nestedPrefix, StringComparison.OrdinalIgnoreCase))) return false;

            if (!MiamiGraphics.Core.System.SafePath.TryResolveInside(
                    patchDirectory, action.SourcePath, out var physicalSourcePath, out _))
                return false;
            if (!File.Exists(physicalSourcePath)) return false;

            string nestedTempDir = Path.Combine(Path.GetTempPath(), "mg_nested_" + Guid.NewGuid().ToString("N"));
            string nestedTempPath = Path.Combine(nestedTempDir, name);
            try
            {
                Directory.CreateDirectory(nestedTempDir);
                using (var baseFs = new FileStream(physicalSourcePath, FileMode.Open, FileAccess.Read))
                using (var inArc = RageArchiveWrapper7.Open(baseFs, name))
                using (var outFs = new FileStream(nestedTempPath, FileMode.Create, FileAccess.ReadWrite))
                {
                    var outArc = RageArchiveWrapper7.Create(outFs, name);
                    RebuildDirectory(inArc.Root, outArc.Root, nestedPrefix, actionMap, patchDirectory);
                    outArc.FileName = name;
                    outArc.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                    outArc.Flush();
                    outArc.Dispose();
                }

                if (!FixArchive(nestedTempPath))
                    throw new InvalidOperationException(
                        Loc.T("error.archiveFixFailedNestedMerge", ("file", name)));

                var newF = destDir.CreateBinaryFile();
                newF.Name = name;
                newF.IsCompressed = false;
                newF.IsEncrypted = false;
                newF.UncompressedSize = (uint)new FileInfo(nestedTempPath).Length;
                using (var readFs = new FileStream(nestedTempPath, FileMode.Open, FileAccess.Read))
                    newF.Import(readFs);

                Console.WriteLine($"  -> Заменен (RPF целиком + вложенные правки поверх): {name}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Injector] WARN: merge вложенных правок в '{path}' не удался ({ex.GetType().Name}: {ex.Message}) - целиковая замена без вложенных правок.");
                return false;
            }
            finally
            {
                try { if (Directory.Exists(nestedTempDir)) Directory.Delete(nestedTempDir, true); } catch { }
            }
        }

        private void CopyFile(IArchiveFile file, IArchiveDirectory destDir, string path, Dictionary<string, PatchAction> actionMap, string patchDirectory)
        {
            bool isRpf = file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
            bool needsRebuild = false;

            if (isRpf)
            {
                string rpfPrefix = path + "/";
                needsRebuild = actionMap.Keys.Any(k => k.StartsWith(rpfPrefix, StringComparison.OrdinalIgnoreCase));
            }

            if (isRpf && needsRebuild && file is IArchiveBinaryFile bin)
            {
                string nestedTempDir = Path.Combine(Path.GetTempPath(), "mg_nested_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(nestedTempDir);
                string nestedTempPath = Path.Combine(nestedTempDir, file.Name);
                try
                {
                    using (var inArc = RageArchiveWrapper7.Open(bin.GetStream(), file.Name))
                    using (var outFs = new FileStream(nestedTempPath, FileMode.Create, FileAccess.ReadWrite))
                    {
                        var outArc = RageArchiveWrapper7.Create(outFs, file.Name);
                        RebuildDirectory(inArc.Root, outArc.Root, path + "/", actionMap, patchDirectory);
                        outArc.FileName = file.Name;
                        outArc.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                        outArc.Flush();
                        outArc.Dispose();
                    }

                    if (!FixArchive(nestedTempPath))
                        throw new InvalidOperationException(
                            Loc.T("error.archiveFixFailedNested", ("file", file.Name)));

                    var newF = destDir.CreateBinaryFile();
                    newF.Name = file.Name;
                    newF.IsCompressed = false;
                    newF.IsEncrypted = false;
                    newF.UncompressedSize = (uint)new FileInfo(nestedTempPath).Length;
                    using (var readFs = new FileStream(nestedTempPath, FileMode.Open, FileAccess.Read))
                        newF.Import(readFs);
                }
                finally
                {
                    try { Directory.Delete(nestedTempDir, true); } catch { }
                }
            }
            else if (file is IArchiveBinaryFile bFile)
            {
                var newF = destDir.CreateBinaryFile();
                newF.Name = file.Name;
                newF.Import(bFile.GetStream());
                newF.IsCompressed = bFile.IsCompressed;
                newF.IsEncrypted = bFile.IsEncrypted;
                newF.UncompressedSize = bFile.UncompressedSize;
            }
            else if (file is IArchiveResourceFile rFile)
            {
                var newF = destDir.CreateResourceFile();
                newF.Name = file.Name;
                using (var ms = new MemoryStream()) { rFile.Export(ms); ms.Position = 0; newF.Import(ms); }
            }
        }

        private bool VerifyAndRepairPlainWrites(string archivePath, List<PatchAction> actions, string patchDirectory)
        {
            var suspects = new List<(string Target, string Source)>();
            foreach (var a in actions)
            {
                if (a.Type != ActionType.Replace && a.Type != ActionType.Import) continue;
                if (a.IsWholeReplaceNestedRpf) continue;

                var target = (a.TargetPath ?? "").Replace('\\', '/').TrimStart('/');
                if (target.Length == 0) continue;
                var segments = target.Split('/');
                if (segments[^1].EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;

                if (!MiamiGraphics.Core.System.SafePath.TryResolveInside(
                        patchDirectory, a.SourcePath, out var src, out _)) continue;
                if (!File.Exists(src)) continue;

                var head = new byte[4];
                using (var fs = File.OpenRead(src))
                    if (fs.Read(head, 0, 4) < 4) continue;
                bool isRsc7 = head[0] == 0x52 && head[1] == 0x53 && head[2] == 0x43;
                if (isRsc7) continue;

                suspects.Add((target, src));
            }
            if (suspects.Count == 0) return true;

            var missing = new List<KeyValuePair<string, byte[]>>();
            try
            {
                using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var arc = RageArchiveWrapper7.Open(fs, Path.GetFileName(archivePath), leaveOpen: true);
                foreach (var (target, src) in suspects)
                {
                    var want = File.ReadAllBytes(src);
                    var got = ReadPlainFileBytes(arc.Root, target.Split('/'));
                    if (got != null && got.Length == want.Length && got.AsSpan().SequenceEqual(want))
                        continue;
                    if (missing.Count < 3)
                        Console.WriteLine($"[Injector.verify] {target}: в архиве {(got == null ? "НЕТ" : got.Length.ToString())}, в патче {want.Length}");
                    missing.Add(new KeyValuePair<string, byte[]>(target, want));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Injector] сверка груза не удалась: {ex.GetType().Name}: {ex.Message}");
                return true;
            }

            if (missing.Count == 0) return true;

            Console.WriteLine($"[Injector] пересборка не донесла {missing.Count} из {suspects.Count} файлов - дописываю");
            bool ok = MiamiGraphics.Core.Services.PatchCustomizationSupport.ReplaceFilesInLiveArchive(
                archivePath, missing, out int applied, out var skipped, addMissingPlainPaths: true);
            if (!ok || applied != missing.Count)
            {
                SetError(Loc.T("error.rebuildFailed",
                    ("archive", archivePath),
                    ("detail", $"груз патча дописан не полностью: {applied} из {missing.Count}" +
                               (skipped.Count > 0 ? $", пропущено: {string.Join(", ", skipped)}" : ""))));
                return false;
            }
            Console.WriteLine($"[Injector] дописано {applied} файлов");
            return true;
        }

        private static byte[]? ReadPlainFileBytes(IArchiveDirectory root, string[] parts, int index = 0)
        {
            var dir = root;
            for (int i = index; i < parts.Length - 1; i++)
            {
                var next = dir.GetDirectories()
                    .FirstOrDefault(d => d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (next is not null) { dir = next; continue; }

                var nested = dir.GetFiles()
                    .FirstOrDefault(x => x.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase))
                    as IArchiveBinaryFile;
                if (nested is null || !parts[i].EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                    return null;
                try
                {
                    using var stream = nested.GetStream();
                    using var inner = RageArchiveWrapper7.Open(stream, nested.Name, true);
                    return ReadPlainFileBytes(inner.Root, parts, i + 1);
                }
                catch { return null; }
            }
            var f = dir.GetFiles()
                .FirstOrDefault(x => x.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase));
            if (f is null) return null;
            return MiamiGraphics.Core.Parser.RpfRealBytes.Get(f);
        }

        private void AddModdedFile(IArchiveDirectory dir, string name, PatchAction action, string patchDirectory, IArchiveBinaryFile oldBinTemplate)
        {
            if (!MiamiGraphics.Core.System.SafePath.TryResolveInside(
                    patchDirectory, action.SourcePath, out var physicalSourcePath, out var whySrc))
            {
                SetError(Loc.T("error.manifestFileOutsidePatchWhy",
                    ("path", action.SourcePath), ("why", whySrc)));
                throw new InvalidOperationException(
                    Loc.T("error.manifestFileOutsidePatch", ("path", action.SourcePath)));
            }

            if (!File.Exists(physicalSourcePath))
            {
                SetError(Loc.T("error.manifestFileMissingOnDiskPath", ("path", physicalSourcePath)));
                throw new FileNotFoundException(Loc.T("error.manifestFileMissingOnDisk"), physicalSourcePath);
            }

            byte[] rawData = File.ReadAllBytes(physicalSourcePath);

            if (name.Equals("miami_graphics_armor.rpf", StringComparison.OrdinalIgnoreCase) && rawData.Length >= 16)
            {
                var check = RpfEncryptionCheck.Inspect(physicalSourcePath);
                bool survivesRename = RpfEncryptionCheck.WillSurviveRename(
                    physicalSourcePath, "miami_graphics_armor.rpf");
                if (check.IsRpf7 && (!check.GameCanRead || !survivesRename))
                {
                    SetError(Loc.T("error.armorRpfNotOpen",
                        ("marker", check.Detail.Length > 0 ? check.Detail : check.Declared.ToString())));
                    throw new InvalidOperationException(LastError);
                }
            }

            bool isRsc7Resource =
                rawData.Length >= 4 &&
                rawData[0] == 0x52 &&
                rawData[1] == 0x53 &&
                rawData[2] == 0x43;

            if (isRsc7Resource)
            {
                var newF = dir.CreateResourceFile();
                newF.Name = name;
                newF.Import(new MemoryStream(rawData));
                Console.WriteLine($"  -> {(action.Type == ActionType.Import ? "Импортирован" : "Заменен")} (Resource): {name}");
            }
            else
            {
                var newF = dir.CreateBinaryFile();
                newF.Name = name;

                bool shouldCompress = !name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
                if (oldBinTemplate != null && !oldBinTemplate.IsCompressed) shouldCompress = false;

                if (shouldCompress)
                {
                    using (var ms = new MemoryStream())
                    {
                        using (var def = new DeflateStream(ms, CompressionMode.Compress, true))
                            def.Write(rawData, 0, rawData.Length);
                        newF.UncompressedSize = (uint)rawData.Length;
                        rawData = ms.ToArray();
                        newF.IsCompressed = true;
                    }
                }
                else
                {
                    newF.IsCompressed = false;
                    newF.UncompressedSize = (uint)rawData.Length;
                }

                newF.IsEncrypted = false;
                newF.Import(new MemoryStream(rawData));
                Console.WriteLine($"  -> {(action.Type == ActionType.Import ? "Импортирован" : "Заменен")} (Binary): {name}");
            }
        }

        private Dictionary<string, PatchAction> BuildActionMap(List<PatchAction> rootActions)
        {
            var map = new Dictionary<string, PatchAction>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in rootActions)
            {
                string path = action.TargetPath.Replace('\\', '/');
                int colonIndex = path.IndexOf(':');
                if (colonIndex >= 0) path = path.Substring(colonIndex + 1);
                path = path.TrimStart('/');
                map[path] = action;
            }
            return map;
        }

        private Dictionary<string, List<PatchAction>> GroupActionsByRootRpf(List<PatchAction> actions)
        {
            var result = new Dictionary<string, List<PatchAction>>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in actions)
            {
                string rootRpf = ResolveRootRpf(action.TargetPath);

                if (rootRpf == "SKIP") continue;

                if (!result.ContainsKey(rootRpf)) result[rootRpf] = new List<PatchAction>();
                result[rootRpf].Add(action);
            }
            return result;
        }

        private string ResolveRootRpf(string virtualPath)
        {
            if (virtualPath.Equals("setup2.xml", StringComparison.OrdinalIgnoreCase) ||
                virtualPath.Equals("assembly.xml", StringComparison.OrdinalIgnoreCase))
                return "SKIP";

            if (virtualPath.StartsWith("common:/", StringComparison.OrdinalIgnoreCase)) return "common.rpf";
            if (virtualPath.StartsWith("update:/", StringComparison.OrdinalIgnoreCase)) return @"update\update.rpf";

            int colonIndex = virtualPath.IndexOf(':');
            if (colonIndex > 0)
            {
                string prefix = virtualPath.Substring(0, colonIndex);
                if (prefix.Equals("update", StringComparison.OrdinalIgnoreCase)) return @"update\update.rpf";
                return $"{prefix}.rpf";
            }

            return @"update\update.rpf";
        }

        private string GetParentPath(string fullPath)
        {
            int lastSlash = fullPath.LastIndexOf('/');
            return lastSlash == -1 ? "" : fullPath.Substring(0, lastSlash + 1);
        }

        private string GetFileName(string fullPath)
        {
            int lastSlash = fullPath.LastIndexOf('/');
            return lastSlash == -1 ? fullPath : fullPath.Substring(lastSlash + 1);
        }

        private string GetNextFolder(string fullPath, string currentPath)
        {
            if (!fullPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase)) return "";
            string remainder = fullPath.Substring(currentPath.Length);
            int slashIdx = remainder.IndexOf('/');
            return slashIdx == -1 ? "" : remainder.Substring(0, slashIdx);
        }

        private void RemoveReadOnly(string path)
        {
            if (File.Exists(path))
            {
                var attr = File.GetAttributes(path);
                if (attr.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
            }
        }

        private bool FixArchive(string rpfPath) => ArchiveFixer.Fix(rpfPath);

        private void SetError(string message)
        {
            LastError = message;
            Debug.WriteLine("[Injector] " + message);
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Injector] " + message);
                Console.ResetColor();
            }
            catch { }
        }
    }
}
