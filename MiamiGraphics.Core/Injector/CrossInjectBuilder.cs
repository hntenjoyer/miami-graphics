using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Parser;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Injector
{
    public class CrossInjectBuilder
    {

        public static readonly string ResultBaseDir = MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir;

        private readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();

        public IReadOnlyList<ReduxPatchInfo> GetAvailableReduxes()
        {
            if (!Directory.Exists(ResultBaseDir))
                return Array.Empty<ReduxPatchInfo>();

            var result = new List<ReduxPatchInfo>();

            foreach (string directory in Directory.GetDirectories(ResultBaseDir))
            {
                string folderName = Path.GetFileName(directory);
                if (folderName.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                    continue;

                string manifestPath = Path.Combine(directory, "manifest.json");
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    var manifest = JsonSerializer.Deserialize<DiffManifest>(File.ReadAllText(manifestPath), _jsonOptions);
                    if (manifest == null)
                        continue;

                    string componentMapPath = Path.Combine(directory, "component_map.json");
                    ResolvedComponentMap? componentMap = File.Exists(componentMapPath)
                        ? JsonSerializer.Deserialize<ResolvedComponentMap>(File.ReadAllText(componentMapPath), _jsonOptions)
                        : null;

                    result.Add(new ReduxPatchInfo
                    {
                        FolderPath = directory,
                        FolderName = folderName,
                        ReduxName = string.IsNullOrWhiteSpace(manifest.ReduxName) ? folderName : manifest.ReduxName,
                        ParsedAt = manifest.ParsedAt,
                        ManifestPath = manifestPath,
                        ComponentMapPath = componentMapPath,
                        ComponentMap = componentMap
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CrossInject] Пропуск {folderName}: {ex.Message}");
                }
            }

            return result
                .OrderByDescending(x => x.ParsedAt)
                .ThenBy(x => x.FolderName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<ReduxPatchInfo> GetDonorCandidates(string baseReduxPath, string componentName)
        {
            return GetAvailableReduxes()
                .Where(x => !x.FolderPath.Equals(baseReduxPath, StringComparison.OrdinalIgnoreCase))
                .Where(x => HasComponent(x, componentName))
                .ToList();
        }

        public string CreateWorkingPatchCopy(string baseReduxPath)
        {
            return BuildCrossInjectedPatch(baseReduxPath, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        public string BuildCrossInjectedPatch(string baseReduxPath, IReadOnlyDictionary<string, string> donorSelections)
        {
            string baseManifestPath = Path.Combine(baseReduxPath, "manifest.json");
            string basePatchDir = Path.Combine(baseReduxPath, "patch_files");

            if (!File.Exists(baseManifestPath))
                throw new FileNotFoundException(Loc.T("error.baseReduxManifestMissing"), baseManifestPath);

            if (!Directory.Exists(basePatchDir))
                throw new DirectoryNotFoundException(Loc.T("error.baseReduxPatchFilesMissing", ("dir", basePatchDir)));

            var manifest = JsonSerializer.Deserialize<DiffManifest>(File.ReadAllText(baseManifestPath), _jsonOptions)
                ?? throw new InvalidOperationException(Loc.T("error.baseReduxManifestUnreadable"));

            ReduxPatchInfo baseRedux = GetAvailableReduxes()
                .FirstOrDefault(x => x.FolderPath.Equals(baseReduxPath, StringComparison.OrdinalIgnoreCase))
                ?? CreatePatchInfoFromDirectory(baseReduxPath, manifest);

            string mergedDir = Path.Combine(
                Path.GetTempPath(),
                "MiamiGraphics.CrossInject",
                $"{Path.GetFileName(baseReduxPath)}_{DateTime.Now:yyyyMMdd_HHmmss}");

            string mergedPatchDir = Path.Combine(mergedDir, "patch_files");
            Directory.CreateDirectory(mergedDir);
            CopyDirectory(basePatchDir, mergedPatchDir);

            foreach (var selection in donorSelections)
            {
                MergeComponent(manifest, mergedPatchDir, selection.Key, selection.Value, baseRedux);
            }

            manifest.TotalPatchSize = manifest.Actions
                .Where(a => a.Type != ActionType.Delete)
                .Sum(a => a.Size);

            File.WriteAllText(Path.Combine(mergedDir, "manifest.json"), JsonSerializer.Serialize(manifest, _jsonOptions));

            if (File.Exists(baseRedux.ComponentMapPath))
                File.Copy(baseRedux.ComponentMapPath, Path.Combine(mergedDir, "component_map.json"), true);

            string contentInfoPath = Path.Combine(baseReduxPath, "content_info.json");
            if (File.Exists(contentInfoPath))
                File.Copy(contentInfoPath, Path.Combine(mergedDir, "content_info.json"), true);

            File.Copy(baseManifestPath, Path.Combine(mergedDir, "manifest.base.json"), true);
            return mergedDir;
        }

        private void MergeComponent(
            DiffManifest manifest,
            string mergedPatchDir,
            string componentName,
            string donorReduxPath,
            ReduxPatchInfo baseRedux)
        {

            ReduxPatchInfo donor =
                GetAvailableReduxes()
                    .FirstOrDefault(x => x.FolderPath.Equals(donorReduxPath, StringComparison.OrdinalIgnoreCase))
                ?? CreatePatchInfoFromDonorPath(donorReduxPath);

            if (donor.ComponentMap?.Components == null ||
                !donor.ComponentMap.Components.TryGetValue(componentName, out ComponentInfo? donorComponent) ||
                donorComponent == null ||
                !donorComponent.IsFound)
            {
                throw new InvalidOperationException(Loc.T("error.donorComponentUnavailable",
                    ("donor", donor.FolderName), ("component", componentName)));
            }

            string componentRoot = Path.Combine(donorReduxPath, "components", componentName);
            if (!Directory.Exists(componentRoot))
                throw new DirectoryNotFoundException(Loc.T("error.donorComponentDirMissing", ("dir", componentRoot)));

            ILookup<string, string> donorFilesByName = Directory.GetFiles(componentRoot, "*", SearchOption.AllDirectories)
                .ToLookup(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

            List<string> targetPaths = GetTargetPaths(baseRedux, componentName, donorComponent);

            bool isTimecycle = componentName.Equals("timecycle", StringComparison.OrdinalIgnoreCase);
            Dictionary<string, string>? donorModifierBlocks = isTimecycle
                ? TimecycleModsMerger.CollectDonorModifierBlocks(componentRoot)
                : null;

            foreach (string targetPath in targetPaths)
            {
                string fileName = Path.GetFileName(targetPath.Replace('/', '\\'));

                if (isTimecycle && TimecycleModsMerger.IsModsFile(fileName))
                {
                    MergeTimecycleModsFile(manifest, mergedPatchDir, targetPath, fileName, donorModifierBlocks!);
                    continue;
                }

                string? donorFilePath = ResolveDonorFilePath(componentRoot, donorFilesByName, donorComponent, targetPath, fileName);

                if (donorFilePath is null)
                {
                    Console.WriteLine($"[CrossInject] SKIP '{fileName}' - донор не предоставляет этот файл (target='{targetPath}'). Оставляю файл базового редукса.");
                    continue;
                }

                string patchRelativePath = NormalizePath(targetPath);
                PatchAction? existingAction = manifest.Actions.FirstOrDefault(a => NormalizePath(a.TargetPath) == patchRelativePath);

                if (TryResolvePhysicalParentRpf(mergedPatchDir, patchRelativePath, out string parentRpfPhysicalPath, out string internalPathInRpf))
                {
                    ReplaceFileInsideArchive(parentRpfPhysicalPath, internalPathInRpf, File.ReadAllBytes(donorFilePath));

                    if (existingAction != null)
                        manifest.Actions.Remove(existingAction);

                    continue;
                }

                string mergedFilePath = Path.Combine(mergedPatchDir, patchRelativePath.Replace("/", "\\"));
                string? mergedFileDir = Path.GetDirectoryName(mergedFilePath);

                if (!string.IsNullOrWhiteSpace(mergedFileDir))
                    Directory.CreateDirectory(mergedFileDir);

                File.Copy(donorFilePath, mergedFilePath, true);

                byte[] bytes = File.ReadAllBytes(mergedFilePath);

                if (existingAction != null)
                    manifest.Actions.Remove(existingAction);

                manifest.Actions.Add(new PatchAction
                {
                    Type = existingAction?.Type == ActionType.Import ? ActionType.Import : ActionType.Replace,
                    TargetPath = patchRelativePath,
                    SourcePath = $"patch_files/{patchRelativePath}",
                    Size = bytes.LongLength,
                    Sha256 = ComputeSha256(bytes),
                    IsWholeReplaceNestedRpf = false
                });
            }
        }

        private void MergeTimecycleModsFile(
            DiffManifest manifest,
            string mergedPatchDir,
            string targetPath,
            string fileName,
            IReadOnlyDictionary<string, string> donorModifierBlocks)
        {
            string patchRelativePath = NormalizePath(targetPath);
            string mergedFilePath = Path.Combine(mergedPatchDir, patchRelativePath.Replace("/", "\\"));

            if (!File.Exists(mergedFilePath))
            {
                Console.WriteLine($"[CrossInject][timecycle] SKIP '{fileName}' - базовый редукс не заменяет этот файл, ванильная таблица сохранена.");
                return;
            }

            string baseXml = TimecycleModsMerger.ReadAllTextPreserving(mergedFilePath, out bool hadBom);
            string? merged = TimecycleModsMerger.MergeModsXml(baseXml, donorModifierBlocks, out int replacedCount, out string? error);

            if (merged is null)
            {
                Console.WriteLine(error is null
                    ? $"[CrossInject][timecycle] '{fileName}' - у донора нет одноимённых модификаторов, файл базы оставлен как есть."
                    : $"[CrossInject][timecycle] '{fileName}' - merge отменён ({error}), файл базы оставлен как есть.");
                return;
            }

            TimecycleModsMerger.WriteAllTextPreserving(mergedFilePath, merged, hadBom);
            Console.WriteLine($"[CrossInject][timecycle] '{fileName}' - перенесены значения {replacedCount} одноимённых модификаторов, таблица имён базы сохранена.");

            byte[] bytes = File.ReadAllBytes(mergedFilePath);
            PatchAction? existingAction = manifest.Actions.FirstOrDefault(a => NormalizePath(a.TargetPath) == patchRelativePath);
            if (existingAction != null)
                manifest.Actions.Remove(existingAction);

            manifest.Actions.Add(new PatchAction
            {
                Type = existingAction?.Type == ActionType.Import ? ActionType.Import : ActionType.Replace,
                TargetPath = patchRelativePath,
                SourcePath = $"patch_files/{patchRelativePath}",
                Size = bytes.LongLength,
                Sha256 = ComputeSha256(bytes),
                IsWholeReplaceNestedRpf = false
            });
        }

        private static List<string> GetTargetPaths(ReduxPatchInfo baseRedux, string componentName, ComponentInfo donorComponent)
        {
            if (baseRedux.ComponentMap?.Components != null &&
                baseRedux.ComponentMap.Components.TryGetValue(componentName, out ComponentInfo? baseComponent) &&
                baseComponent != null &&
                baseComponent.IsFound &&
                baseComponent.InternalPaths.Count > 0)
            {
                return baseComponent.InternalPaths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return donorComponent.InternalPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? ResolveDonorFilePath(
            string componentRoot,
            ILookup<string, string> donorFilesByName,
            ComponentInfo donorComponent,
            string targetPath,
            string fileName)
        {

            string? directMatch = donorFilesByName[fileName].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(directMatch))
            {
                Console.WriteLine($"[CrossInject][resolve] '{fileName}' → exact match: {directMatch}");
                return directMatch;
            }

            var allDonorFiles = donorFilesByName.SelectMany(g => g).ToList();
            string targetDir = GetNormalizedDir(targetPath);
            var suffixMatch = allDonorFiles.FirstOrDefault(p =>
            {
                var n = Path.GetFileName(p);
                if (n.Length <= fileName.Length) return false;
                if (!n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) return false;
                char boundary = n[n.Length - fileName.Length - 1];
                if (char.IsLetterOrDigit(boundary)) return false;
                return DirsSuffixCompatible(GetDonorRelativeDir(componentRoot, p), targetDir);
            });
            if (!string.IsNullOrWhiteSpace(suffixMatch))
            {
                Console.WriteLine($"[CrossInject][resolve] '{fileName}' → suffix match: {suffixMatch}");
                return suffixMatch;
            }

            string? fallbackInternalPath = donorComponent.InternalPaths
                .FirstOrDefault(path => Path.GetFileName(path.Replace('/', '\\'))
                    .Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(fallbackInternalPath))
            {
                string donorFilePath = Path.Combine(componentRoot, fallbackInternalPath.Replace("/", "\\"));
                if (File.Exists(donorFilePath))
                {
                    Console.WriteLine($"[CrossInject][resolve] '{fileName}' → InternalPath fallback: {donorFilePath}");
                    return donorFilePath;
                }
            }

            var haveFiles = string.Join(", ", allDonorFiles
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Take(20));
            var haveInternalPaths = string.Join(", ",
                (donorComponent.InternalPaths ?? new List<string>()).Take(10));

            Console.WriteLine(
                $"[CrossInject] WARN: файл '{fileName}' для подмены пути '{targetPath}' " +
                $"не найден у донора (componentRoot={componentRoot}). " +
                $"Файлы у донора: [{haveFiles}]. " +
                $"InternalPaths у донора: [{haveInternalPaths}]. " +
                $"Пропускаю - файл базового редукса остаётся.");
            return null;
        }

        private static bool HasComponent(ReduxPatchInfo redux, string componentName)
        {
            if (redux.ComponentMap?.Components == null)
                return false;

            return redux.ComponentMap.Components.TryGetValue(componentName, out ComponentInfo? info) &&
                   info != null &&
                   info.IsFound &&
                   info.InternalPaths.Count > 0;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace("\\", "/").TrimStart('/').ToLowerInvariant();
        }

        private static string GetNormalizedDir(string path)
        {
            string normalized = NormalizePath(path);
            int idx = normalized.LastIndexOf('/');
            return idx <= 0 ? "" : normalized.Substring(0, idx);
        }

        private static string GetDonorRelativeDir(string componentRoot, string donorFilePath)
        {
            string dir = Path.GetDirectoryName(donorFilePath) ?? "";
            string root = componentRoot.TrimEnd('\\', '/');
            if (dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                dir = dir.Substring(root.Length);
            return NormalizePath(dir);
        }

        private static bool DirsSuffixCompatible(string donorDir, string targetDir)
        {
            if (donorDir.Length == 0) return true;
            if (targetDir.Length == 0) return false;
            if (string.Equals(donorDir, targetDir, StringComparison.OrdinalIgnoreCase)) return true;
            return targetDir.EndsWith("/" + donorDir, StringComparison.OrdinalIgnoreCase) ||
                   donorDir.EndsWith("/" + targetDir, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        private static bool TryResolvePhysicalParentRpf(
            string mergedPatchDir,
            string patchRelativePath,
            out string parentRpfPhysicalPath,
            out string internalPathInRpf)
        {
            int rpfIndex = patchRelativePath.IndexOf(".rpf/", StringComparison.OrdinalIgnoreCase);
            if (rpfIndex < 0)
            {
                parentRpfPhysicalPath = "";
                internalPathInRpf = "";
                return false;
            }

            string parentRpfRelativePath = patchRelativePath.Substring(0, rpfIndex + 4);
            internalPathInRpf = patchRelativePath.Substring(rpfIndex + 5);
            parentRpfPhysicalPath = Path.Combine(mergedPatchDir, parentRpfRelativePath.Replace("/", "\\"));

            return !string.IsNullOrWhiteSpace(internalPathInRpf) && File.Exists(parentRpfPhysicalPath);
        }

        private static void ReplaceFileInsideArchive(string archivePath, string internalPath, byte[] bytes)
        {
            using (var archive = RageArchiveWrapper7.Open(archivePath))
            {
                if (!TryReplaceRecursive(archive.Root, NormalizePath(internalPath).Split('/'), 0, bytes))
                    throw new FileNotFoundException($"Could not find internal file {internalPath} inside {archivePath}.", archivePath);

                archive.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                archive.Flush();
            }

            ArchiveFixer.FixOrThrow(archivePath, Path.GetFileName(archivePath));
        }

        private static bool TryReplaceRecursive(IArchiveDirectory currentDirectory, string[] parts, int index, byte[] bytes)
        {
            if (index >= parts.Length)
                return false;

            string currentPart = parts[index];

            if (index == parts.Length - 1)
            {
                IArchiveFile? file = currentDirectory.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(currentPart, StringComparison.OrdinalIgnoreCase));

                if (file == null)
                    return false;

                if (file is IArchiveBinaryFile binaryFile)
                {
                    binaryFile.Import(new MemoryStream(bytes));

                    binaryFile.IsCompressed = false;
                    binaryFile.IsEncrypted = false;
                    binaryFile.UncompressedSize = (uint)bytes.Length;
                    return true;
                }

                if (file is IArchiveResourceFile resourceFile)
                {
                    resourceFile.Import(new MemoryStream(bytes));
                    return true;
                }

                return false;
            }

            IArchiveDirectory? subDirectory = currentDirectory.GetDirectories()
                .FirstOrDefault(d => d.Name.Equals(currentPart, StringComparison.OrdinalIgnoreCase));

            if (subDirectory != null)
                return TryReplaceRecursive(subDirectory, parts, index + 1, bytes);

            IArchiveBinaryFile? nestedRpf = currentDirectory.GetFiles()
                .FirstOrDefault(f => f.Name.Equals(currentPart, StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;

            if (nestedRpf != null && currentPart.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                string nestedTempDir = Path.Combine(Path.GetTempPath(), "mg_ci_nested_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(nestedTempDir);
                string nestedTempPath = Path.Combine(nestedTempDir, nestedRpf.Name);
                try
                {
                    using (var srcStream = nestedRpf.GetStream())
                    using (var outFs = new FileStream(nestedTempPath, FileMode.Create, FileAccess.ReadWrite))
                        srcStream.CopyTo(outFs);

                    bool replaced;
                    using (var nestedArchive = RageArchiveWrapper7.Open(nestedTempPath))
                    {
                        replaced = TryReplaceRecursive(nestedArchive.Root, parts, index + 1, bytes);
                        if (!replaced) return false;
                        nestedArchive.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                        nestedArchive.Flush();
                    }

                    ArchiveFixer.FixOrThrow(nestedTempPath, nestedRpf.Name);
                    byte[] fixedBytes = File.ReadAllBytes(nestedTempPath);
                    nestedRpf.Import(new MemoryStream(fixedBytes));
                    nestedRpf.IsCompressed = false;
                    nestedRpf.IsEncrypted = false;
                    nestedRpf.UncompressedSize = (uint)fixedBytes.Length;
                    return true;
                }
                finally { try { Directory.Delete(nestedTempDir, recursive: true); } catch { } }
            }

            return false;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, directory);
                Directory.CreateDirectory(Path.Combine(destinationDir, relative));
            }

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                string target = Path.Combine(destinationDir, relative);
                string? targetDir = Path.GetDirectoryName(target);

                if (!string.IsNullOrWhiteSpace(targetDir))
                    Directory.CreateDirectory(targetDir);

                File.Copy(file, target, true);
            }
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private ReduxPatchInfo CreatePatchInfoFromDirectory(string patchDirectory, DiffManifest manifest)
        {
            string componentMapPath = Path.Combine(patchDirectory, "component_map.json");
            ResolvedComponentMap? componentMap = File.Exists(componentMapPath)
                ? JsonSerializer.Deserialize<ResolvedComponentMap>(File.ReadAllText(componentMapPath), _jsonOptions)
                : null;

            return new ReduxPatchInfo
            {
                FolderPath = patchDirectory,
                FolderName = Path.GetFileName(patchDirectory),
                ReduxName = string.IsNullOrWhiteSpace(manifest.ReduxName) ? Path.GetFileName(patchDirectory) : manifest.ReduxName,
                ParsedAt = manifest.ParsedAt,
                ManifestPath = Path.Combine(patchDirectory, "manifest.json"),
                ComponentMapPath = componentMapPath,
                ComponentMap = componentMap
            };
        }

        private ReduxPatchInfo CreatePatchInfoFromDonorPath(string donorReduxPath)
        {
            if (!Directory.Exists(donorReduxPath))
                throw new DirectoryNotFoundException(Loc.T("error.donorReduxNotFound", ("path", donorReduxPath)));

            string manifestPath = Path.Combine(donorReduxPath, "manifest.json");
            DiffManifest manifest;
            if (File.Exists(manifestPath))
            {
                manifest = JsonSerializer.Deserialize<DiffManifest>(
                    File.ReadAllText(manifestPath), _jsonOptions) ?? new DiffManifest();
            }
            else
            {
                manifest = new DiffManifest { ReduxName = Path.GetFileName(donorReduxPath) };
            }
            return CreatePatchInfoFromDirectory(donorReduxPath, manifest);
        }
    }

    public class ReduxPatchInfo
    {
        public string FolderPath { get; set; } = "";
        public string FolderName { get; set; } = "";
        public string ReduxName { get; set; } = "";
        public DateTime ParsedAt { get; set; }
        public string ManifestPath { get; set; } = "";
        public string ComponentMapPath { get; set; } = "";
        public ResolvedComponentMap? ComponentMap { get; set; }
    }
}
