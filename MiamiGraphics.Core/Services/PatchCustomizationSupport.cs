using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MiamiGraphics.Core.Parser;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Services
{
    public sealed class PatchWorkspaceFile
    {
        public string TargetPath { get; init; } = "";
        public string PhysicalPath { get; init; } = "";
        public ActionType ActionType { get; set; }
        public string? ParentRpfPhysicalPath { get; set; }
        public string? InternalPathInRpf { get; set; }
    }

    public static class PatchCustomizationSupport
    {

        public static List<PatchWorkspaceFile> FindExistingFiles(string patchRootDirectory, DiffManifest manifest, string fileName)
        {
            var files = new Dictionary<string, PatchWorkspaceFile>(StringComparer.OrdinalIgnoreCase);
            string patchFilesDirectory = Path.Combine(patchRootDirectory, "patch_files");

            foreach (PatchAction action in manifest.Actions.Where(a =>
                         a.Type != ActionType.Delete &&
                         !string.IsNullOrWhiteSpace(a.TargetPath) &&
                         a.TargetPath.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)))
            {
                string targetPath = NormalizePath(action.TargetPath);
                string? physicalPath = ResolvePhysicalPath(patchRootDirectory, action, targetPath);

                if (!string.IsNullOrWhiteSpace(physicalPath) && File.Exists(physicalPath))
                {
                    files[targetPath] = new PatchWorkspaceFile
                    {
                        TargetPath = targetPath,
                        PhysicalPath = physicalPath,
                        ActionType = action.Type
                    };
                }
            }

            if (Directory.Exists(patchFilesDirectory))
            {
                foreach (string physicalPath in Directory.GetFiles(patchFilesDirectory, fileName, SearchOption.AllDirectories))
                {
                    string targetPath = NormalizePath(Path.GetRelativePath(patchFilesDirectory, physicalPath));

                    if (!files.ContainsKey(targetPath))
                    {
                        files[targetPath] = new PatchWorkspaceFile
                        {
                            TargetPath = targetPath,
                            PhysicalPath = physicalPath,
                            ActionType = ActionType.Replace
                        };
                    }
                }
            }

            foreach (PatchWorkspaceFile workspaceFile in FindNestedFilesFromComponentMap(patchRootDirectory, manifest, fileName))
            {
                files[workspaceFile.TargetPath] = workspaceFile;
            }

            return files.Values
                .OrderBy(x => x.TargetPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> ResolveOriginalTargetPaths(
            string gtaRootPath,
            string targetFileName,
            IReadOnlyList<string>? defaultTargetPaths,
            params string[] rpfNameHints)
        {
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string path)
            {
                string normalized = NormalizePath(path);
                if (seen.Add(normalized))
                    ordered.Add(normalized);
            }

            try
            {
                string updateRpfPath = ResolveUpdateRpfPath(gtaRootPath);
                using IArchive archive = RageArchiveWrapper7.Open(updateRpfPath);

                ContentXmlInfo contentInfo;
                try { contentInfo = new ContentXmlAnalyzer().AnalyzeFromArchive(archive.Root); }
                catch { contentInfo = new ContentXmlInfo(); }

                foreach (string declaredRpf in contentInfo.CustomRpfs.Concat(contentInfo.DefaultRpfs))
                {
                    var hits = new List<string>();
                    CollectPathsInDeclaredRpf(archive.Root, declaredRpf, targetFileName, hits);
                    foreach (string hit in hits)
                        Add(hit);
                }

                if (rpfNameHints is { Length: > 0 })
                {
                    var hits = new List<string>();
                    CollectPathsRecursive(archive.Root, "", targetFileName, rpfNameHints, hits);
                    foreach (string hit in hits)
                        Add(hit);
                }

                {
                    var deep = new List<string>();
                    CollectPathsRecursive(archive.Root, "", targetFileName, Array.Empty<string>(), deep);
                    foreach (string hit in deep)
                        Add(hit);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PatchCustomizationSupport.ResolveOriginalTargetPaths] scan failed: {ex.Message}");
            }

            if (defaultTargetPaths != null)
            {
                foreach (string path in defaultTargetPaths)
                    Add(path);
            }

            return ordered;
        }

        public static List<PatchWorkspaceFile> EnsureOriginalsImported(
            string patchRootDirectory,
            DiffManifest manifest,
            string gtaRootPath,
            string targetFileName,
            IReadOnlyList<string>? defaultTargetPaths,
            params string[] rpfNameHints)
        {
            string updateRpfPath = ResolveUpdateRpfPath(gtaRootPath);
            List<string> candidatePaths = ResolveOriginalTargetPaths(
                gtaRootPath, targetFileName, defaultTargetPaths, rpfNameHints);

            if (candidatePaths.Count == 0)
                throw new ArgumentException(
                    $"No candidate paths resolved for '{targetFileName}' - pass default target paths or rpf name hints.",
                    nameof(targetFileName));

            Dictionary<string, byte[]> extracted = ExtractManyFromArchive(updateRpfPath, candidatePaths);

            var imported = new List<PatchWorkspaceFile>();
            foreach (string candidatePath in candidatePaths)
            {
                if (!extracted.TryGetValue(candidatePath, out byte[]? bytes) || bytes == null)
                    continue;

                try
                {
                    string physicalPath = Path.Combine(
                        patchRootDirectory, "patch_files", candidatePath.Replace("/", "\\"));
                    string? physicalDirectory = Path.GetDirectoryName(physicalPath);
                    if (!string.IsNullOrWhiteSpace(physicalDirectory))
                        Directory.CreateDirectory(physicalDirectory);

                    File.WriteAllBytes(physicalPath, bytes);

                    var workspaceFile = new PatchWorkspaceFile
                    {
                        TargetPath = candidatePath,
                        PhysicalPath = physicalPath,
                        ActionType = ActionType.Import
                    };

                    UpsertPatchAction(manifest, patchRootDirectory, workspaceFile);
                    imported.Add(workspaceFile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PatchCustomizationSupport] import '{candidatePath}' failed: {ex.Message}");
                }
            }

            if (imported.Count == 0)
                throw new FileNotFoundException(
                    $"Failed to extract '{targetFileName}' from {updateRpfPath}. Checked paths: {string.Join(", ", candidatePaths)}");

            Console.WriteLine(
                $"[PatchCustomizationSupport] '{targetFileName}': imported {imported.Count} original(s): {string.Join(", ", imported.Select(f => f.TargetPath))}");
            return imported;
        }

        private static void CollectPathsInDeclaredRpf(
            IArchiveDirectory updateRoot, string declaredRpfPath, string targetFileName, List<string> results)
        {
            if (!declaredRpfPath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                return;

            string[] parts = NormalizePath(declaredRpfPath).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            IArchiveDirectory? current = updateRoot;
            foreach (string part in parts.Take(parts.Length - 1))
            {
                current = current.GetDirectories()
                    .FirstOrDefault(d => d.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (current == null)
                    return;
            }

            if (current.GetFiles().FirstOrDefault(f => f.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase))
                is not IArchiveBinaryFile rpfFile)
                return;

            try
            {
                using var stream = rpfFile.GetStream();
                using IArchive nested = RageArchiveWrapper7.Open(stream, rpfFile.Name, true);
                CollectPathsRecursive(nested.Root, NormalizePath(declaredRpfPath), targetFileName, Array.Empty<string>(), results);
            }
            catch
            {
            }
        }

        public static byte[]? GetCleanOriginalBytes(string gtaRootPath, IReadOnlyList<string> candidateTargetPaths)
        {
            if (candidateTargetPaths == null || candidateTargetPaths.Count == 0)
                return null;

            string updateRpfPath = ResolveUpdateRpfPath(gtaRootPath);

            foreach (string candidateTargetPath in candidateTargetPaths)
            {
                if (candidateTargetPath.EndsWith("core.ypt", StringComparison.OrdinalIgnoreCase) &&
                    (candidateTargetPath.Contains("ptfx_hi", StringComparison.OrdinalIgnoreCase) ||
                     candidateTargetPath.Contains("ptfx_lo", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                byte[]? bytes = TryExtractFile(updateRpfPath, candidateTargetPath);
                if (bytes != null)
                    return bytes;
            }

            return null;
        }

        public static byte[]? GetCleanBytesForExactPath(string gtaRootPath, string internalPath)
        {
            if (string.IsNullOrWhiteSpace(internalPath))
                return null;
            try
            {
                string updateRpfPath = ResolveUpdateRpfPath(gtaRootPath);
                return TryExtractFile(updateRpfPath, internalPath);
            }
            catch
            {
                return null;
            }
        }

        public static byte[]? GetBytesFromArchiveExactPath(string rpfPath, string internalPath)
        {
            if (string.IsNullOrWhiteSpace(rpfPath) || string.IsNullOrWhiteSpace(internalPath))
                return null;
            try { return TryExtractFile(rpfPath, internalPath); }
            catch { return null; }
        }

        public static Dictionary<string, byte[]> ExtractManyFromArchive(string rpfPath, IEnumerable<string> internalPaths)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var wanted = internalPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (wanted.Count == 0) return result;
            try
            {
                using IArchive archive = RageArchiveWrapper7.OpenRead(rpfPath);
                foreach (var ip in wanted)
                {
                    try
                    {
                        var parts = NormalizePath(ip).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        if (TryExtractRecursive(archive.Root, parts, 0, out byte[]? bytes) && bytes != null)
                            result[ip] = bytes;
                    }
                    catch {}
                }
            }
            catch {}
            return result;
        }

        public static void UpsertPatchAction(DiffManifest manifest, string patchRootDirectory, PatchWorkspaceFile workspaceFile)
        {
            if (!string.IsNullOrWhiteSpace(workspaceFile.ParentRpfPhysicalPath))
            {
                ReplaceFileInsideArchive(
                    workspaceFile.ParentRpfPhysicalPath,
                    workspaceFile.InternalPathInRpf ?? throw new InvalidOperationException("InternalPathInRpf is required."),
                    File.ReadAllBytes(workspaceFile.PhysicalPath));
                return;
            }

            string normalizedTargetPath = NormalizePath(workspaceFile.TargetPath);
            string normalizedPhysicalPath = Path.GetFullPath(workspaceFile.PhysicalPath);

            PatchAction? existingAction = manifest.Actions
                .FirstOrDefault(a => NormalizePath(a.TargetPath) == normalizedTargetPath);

            if (existingAction != null)
                manifest.Actions.Remove(existingAction);

            ActionType actionType = existingAction != null && existingAction.Type != ActionType.Delete
                ? existingAction.Type
                : workspaceFile.ActionType;

            byte[] bytes = File.ReadAllBytes(normalizedPhysicalPath);
            string sourcePath = NormalizePath(Path.GetRelativePath(patchRootDirectory, normalizedPhysicalPath));

            manifest.Actions.Add(new PatchAction
            {
                Type = actionType,
                TargetPath = normalizedTargetPath,
                SourcePath = sourcePath,
                Size = bytes.LongLength,
                Sha256 = ComputeSha256(bytes),
                IsWholeReplaceNestedRpf = normalizedTargetPath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)
            });
        }

        public static void RecalculateTotalPatchSize(DiffManifest manifest)
        {
            manifest.TotalPatchSize = manifest.Actions
                .Where(a => a.Type != ActionType.Delete)
                .Sum(a => a.Size);
        }

        private static IEnumerable<PatchWorkspaceFile> FindNestedFilesFromComponentMap(string patchRootDirectory, DiffManifest manifest, string fileName)
        {
            string componentMapPath = Path.Combine(patchRootDirectory, "component_map.json");
            if (!File.Exists(componentMapPath))
                yield break;

            ResolvedComponentMap? componentMap;

            try
            {
                componentMap = JsonSerializer.Deserialize<ResolvedComponentMap>(
                    File.ReadAllText(componentMapPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                yield break;
            }

            if (componentMap?.Components == null)
                yield break;

            foreach (string targetPath in componentMap.Components.Values
                         .Where(component => component != null && component.IsFound)
                         .SelectMany(component => component.InternalPaths ?? Enumerable.Empty<string>())
                         .Where(path => path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!TrySplitPhysicalRpfPath(targetPath, out string parentRpfRelativePath, out string internalPathInRpf))
                    continue;

                string parentRpfPhysicalPath = Path.Combine(
                    patchRootDirectory,
                    "patch_files",
                    NormalizePath(parentRpfRelativePath).Replace("/", "\\"));

                if (!File.Exists(parentRpfPhysicalPath))
                    continue;

                byte[]? bytes = TryExtractFile(parentRpfPhysicalPath, internalPathInRpf);
                if (bytes == null)
                    continue;

                string tempPath = CreateTempWorkspaceFile(fileName, bytes);
                yield return new PatchWorkspaceFile
                {
                    TargetPath = NormalizePath(targetPath),
                    PhysicalPath = tempPath,
                    ActionType = ResolveParentArchiveActionType(manifest, parentRpfRelativePath),
                    ParentRpfPhysicalPath = parentRpfPhysicalPath,
                    InternalPathInRpf = NormalizePath(internalPathInRpf)
                };
            }
        }

        private static string ResolveUpdateRpfPath(string gtaRootPath)
        {

            if (string.IsNullOrWhiteSpace(gtaRootPath))
                throw new FileNotFoundException(
                    "GTA root path is empty. Set it via HardwareLocator or Admin → Settings → Paths → GTA path override.");

            string primaryPath = Path.Combine(gtaRootPath, @"update\update.rpf");
            if (File.Exists(primaryPath))
                return primaryPath;

            throw new FileNotFoundException(
                $"update.rpf was not found at '{primaryPath}'. Check Admin → Settings → Paths → GTA path override.");
        }

        private static byte[]? TryExtractFile(string archivePath, string targetPath)
        {
            using IArchive archive = RageArchiveWrapper7.OpenRead(archivePath);
            string[] parts = NormalizePath(targetPath)
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (TryExtractRecursive(archive.Root, parts, 0, out byte[]? bytes))
                return bytes;

            return null;
        }

        private static bool TryExtractRecursive(IArchiveDirectory currentDirectory, string[] parts, int index, out byte[]? bytes)
        {
            bytes = null;

            if (index >= parts.Length)
                return false;

            string currentPart = parts[index];

            if (index == parts.Length - 1)
            {
                IArchiveFile? file = currentDirectory.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(currentPart, StringComparison.OrdinalIgnoreCase));

                if (file == null)
                    return false;

                bytes = GetRealFileBytes(file);
                return true;
            }

            IArchiveDirectory? subDirectory = currentDirectory.GetDirectories()
                .FirstOrDefault(d => d.Name.Equals(currentPart, StringComparison.OrdinalIgnoreCase));

            if (subDirectory != null)
                return TryExtractRecursive(subDirectory, parts, index + 1, out bytes);

            IArchiveBinaryFile? nestedRpf = currentDirectory.GetFiles()
                .FirstOrDefault(f => f.Name.Equals(currentPart, StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;

            if (nestedRpf != null && currentPart.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = nestedRpf.GetStream();
                using IArchive nestedArchive = RageArchiveWrapper7.Open(stream, nestedRpf.Name, true);
                return TryExtractRecursive(nestedArchive.Root, parts, index + 1, out bytes);
            }

            return false;
        }

        public static string? FindInternalPathByName(string gtaRootPath, string fileName, params string[] rpfNameHints)
            => FindInternalPathsByName(gtaRootPath, fileName, rpfNameHints).FirstOrDefault();

        public static List<string> FindInternalPathsByName(string gtaRootPath, string fileName, params string[] rpfNameHints)
            => FindInternalPathsByName(gtaRootPath, fileName, rpfNameHints, out _);

        public static List<string> FindInternalPathsByName(
            string gtaRootPath, string fileName, string[]? rpfNameHints, out Exception? openError)
        {
            openError = null;
            var results = new List<string>();
            try
            {
                string updateRpfPath = ResolveUpdateRpfPath(gtaRootPath);
                var hints = (rpfNameHints is { Length: > 0 }) ? rpfNameHints : new[] { "minimap", "scaleform" };
                using IArchive archive = RageArchiveWrapper7.OpenRead(updateRpfPath);
                CollectPathsRecursive(archive.Root, "", fileName, hints, results);
            }
            catch (Exception ex) { openError = ex;  }
            return results;
        }

        public static List<string> FindInternalPathsByNameDeep(string gtaRootPath, string fileName)
            => FindInternalPathsByNameDeep(gtaRootPath, fileName, out _);

        public static List<string> FindInternalPathsDeepWhere(
            string gtaRootPath, Func<string, bool> leafMatch, int maxHits = int.MaxValue)
        {
            var results = new List<string>();
            try
            {
                string updateRpfPath = ResolveUpdateRpfPath(gtaRootPath);
                using IArchive archive = RageArchiveWrapper7.OpenRead(updateRpfPath);
                CollectDeepWhere(archive.Root, "", leafMatch, results, maxHits, 0);
            }
            catch {}
            return results;
        }

        private static void CollectDeepWhere(
            IArchiveDirectory dir, string prefix, Func<string, bool> leafMatch,
            List<string> results, int maxHits, int depth)
        {
            if (results.Count >= maxHits || depth > 8) return;

            foreach (var f in dir.GetFiles())
            {
                if (results.Count >= maxHits) return;
                if (leafMatch(f.Name)) { results.Add(prefix + f.Name); continue; }

                if (!f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
                if (f is not IArchiveBinaryFile bin) continue;
                try
                {
                    using var stream = bin.GetStream();
                    using IArchive nested = RageArchiveWrapper7.Open(stream, f.Name, true);
                    CollectDeepWhere(nested.Root, prefix + f.Name + "/", leafMatch, results, maxHits, depth + 1);
                }
                catch {}
            }

            foreach (var d in dir.GetDirectories())
            {
                if (results.Count >= maxHits) return;
                CollectDeepWhere(d, prefix + d.Name + "/", leafMatch, results, maxHits, depth + 1);
            }
        }

        public static List<string> FindInternalPathsByNameDeep(
            string gtaRootPath, string fileName, out Exception? openError)
        {
            openError = null;
            var results = new List<string>();
            try
            {
                string updateRpfPath = ResolveUpdateRpfPath(gtaRootPath);
                using IArchive archive = RageArchiveWrapper7.OpenRead(updateRpfPath);
                CollectPathsRecursive(archive.Root, "", fileName, Array.Empty<string>(), results);
            }
            catch (Exception ex) { openError = ex;  }
            return results;
        }

        public static List<string> EnumerateInternalPaths(string gtaRootPath, Func<string, bool> leafMatch)
        {
            var results = new List<string>();
            try
            {
                string updateRpfPath = ResolveUpdateRpfPath(gtaRootPath);
                using IArchive archive = RageArchiveWrapper7.OpenRead(updateRpfPath);
                CollectLeafPathsRecursive(archive.Root, "", leafMatch, results);
            }
            catch {}
            return results;
        }

        private static void CollectLeafPathsRecursive(IArchiveDirectory dir, string prefix, Func<string, bool> leafMatch, List<string> results)
        {
            foreach (var f in dir.GetFiles())
            {
                if (leafMatch(f.Name))
                    results.Add(prefix.Length == 0 ? f.Name : prefix + "/" + f.Name);
            }
            foreach (var sub in dir.GetDirectories())
                CollectLeafPathsRecursive(sub, prefix.Length == 0 ? sub.Name : prefix + "/" + sub.Name, leafMatch, results);
        }

        public static List<string> EnumerateFilesByContent(
            string gtaRootPath, Func<string, bool> leafMatch, Func<string, bool> contentMatch)
        {
            var results = new List<string>();
            try
            {
                string updateRpfPath = ResolveUpdateRpfPath(gtaRootPath);
                using IArchive archive = RageArchiveWrapper7.OpenRead(updateRpfPath);
                CollectContentPathsRecursive(archive.Root, "", leafMatch, contentMatch, results);
            }
            catch {}
            return results;
        }

        private static void CollectContentPathsRecursive(
            IArchiveDirectory dir, string prefix,
            Func<string, bool> leafMatch, Func<string, bool> contentMatch, List<string> results)
        {
            foreach (var f in dir.GetFiles())
            {
                if (!leafMatch(f.Name)) continue;
                byte[]? bytes;
                try { bytes = GetRealFileBytes(f); }
                catch { continue; }
                if (bytes == null) continue;
                string text;
                try { text = global::System.Text.Encoding.UTF8.GetString(bytes); }
                catch { continue; }
                if (contentMatch(text))
                    results.Add(prefix.Length == 0 ? f.Name : prefix + "/" + f.Name);
            }
            foreach (var sub in dir.GetDirectories())
                CollectContentPathsRecursive(sub, prefix.Length == 0 ? sub.Name : prefix + "/" + sub.Name, leafMatch, contentMatch, results);
        }

        private static void CollectPathsRecursive(IArchiveDirectory dir, string prefix, string fileName, string[] rpfHints, List<string> results)
        {
            foreach (var f in dir.GetFiles())
            {
                if (f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    results.Add(prefix.Length == 0 ? f.Name : prefix + "/" + f.Name);
            }
            foreach (var sub in dir.GetDirectories())
                CollectPathsRecursive(sub, prefix.Length == 0 ? sub.Name : prefix + "/" + sub.Name, fileName, rpfHints, results);
            foreach (var f in dir.GetFiles())
            {
                if (f is not IArchiveBinaryFile bin) continue;
                if (!f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
                if (rpfHints.Length > 0 && !rpfHints.Any(h => f.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                try
                {
                    using var stream = bin.GetStream();
                    using IArchive nested = RageArchiveWrapper7.Open(stream, bin.Name, true);
                    var childPrefix = prefix.Length == 0 ? f.Name : prefix + "/" + f.Name;
                    CollectPathsRecursive(nested.Root, childPrefix, fileName, rpfHints, results);
                }
                catch {}
            }
        }

        public static bool TryReplaceNestedComponentFile(
            string patchRootDirectory, string componentName, string fileName, byte[] donorBytes)
            => TryReplaceNestedComponentFiles(
                   patchRootDirectory, componentName,
                   p => p.EndsWith(fileName, StringComparison.OrdinalIgnoreCase),
                   donorBytes) > 0;

        public static int TryReplaceNestedComponentFiles(
            string patchRootDirectory, string componentName,
            Func<string, bool> pathMatch, byte[] donorBytes)
        {
            string componentMapPath = Path.Combine(patchRootDirectory, "component_map.json");
            if (!File.Exists(componentMapPath))
                return 0;

            ResolvedComponentMap? componentMap;
            try
            {
                componentMap = JsonSerializer.Deserialize<ResolvedComponentMap>(
                    File.ReadAllText(componentMapPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return 0;
            }
            if (componentMap?.Components == null) return 0;
            if (!componentMap.Components.TryGetValue(componentName, out var info) || info == null || !info.IsFound)
                return 0;
            if (info.InternalPaths == null) return 0;

            int replaced = 0;
            foreach (var targetPath in info.InternalPaths)
            {
                if (!pathMatch(targetPath))
                    continue;
                if (!TrySplitPhysicalRpfPath(targetPath, out var parentRpfRelativePath, out var internalPathInRpf))
                    continue;

                string parentRpfPhysicalPath = Path.Combine(
                    patchRootDirectory,
                    "patch_files",
                    NormalizePath(parentRpfRelativePath).Replace("/", "\\"));
                if (!File.Exists(parentRpfPhysicalPath))
                    continue;

                try
                {
                    ReplaceFileInsideArchive(parentRpfPhysicalPath, internalPathInRpf, donorBytes);
                    replaced++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PatchCustomizationSupport] component '{componentName}' replace '{targetPath}' failed: {ex.Message}");
                }
            }
            return replaced;
        }

        public static byte[]? TryExtractLiveFileBytes(string archivePath, string internalPath)
        {
            try { return TryExtractFile(archivePath, internalPath); }
            catch { return null; }
        }

        public static bool ReplaceFilesInLiveArchive(
            string archivePath,
            IReadOnlyList<KeyValuePair<string, byte[]>> replacements,
            out int appliedCount,
            out List<string> skippedPaths,
            bool addMissingPlainPaths = false)
        {
            appliedCount = 0;
            skippedPaths = new List<string>();
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (IArchive archive = OpenLiveArchiveWithRetry(archivePath))
            {
                foreach (var kv in replacements)
                {
                    var parts = NormalizePath(kv.Key).Split('/');
                    if (TryReplaceRecursive(archive.Root, parts, 0, kv.Value))
                    {
                        appliedCount++;
                        foreach (var part in parts.SkipLast(1))
                            if (part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                                touched.Add(part);
                        continue;
                    }

                    var isPlainPath = !parts.SkipLast(1)
                        .Any(p => p.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase));
                    if (addMissingPlainPaths)
                    {
                        try
                        {
                            if (isPlainPath)
                            {
                                AddFileToOpenArchive(archive, kv.Key, kv.Value);
                            }
                            else if (!TryReplaceRecursive(archive.Root, parts, 0, kv.Value, allowCreate: true))
                            {
                                skippedPaths.Add(kv.Key);
                                continue;
                            }
                            else
                            {
                                foreach (var part in parts.SkipLast(1))
                                    if (part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                                        touched.Add(part);
                            }
                            appliedCount++;
                            continue;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[LiveBatch] add '{kv.Key}' failed: {ex.Message}");
                        }
                    }
                    skippedPaths.Add(kv.Key);
                }

                if (appliedCount == 0) return false;
                archive.Flush();
            }
            return FixLiveArchiveChecksums(archivePath, touched);
        }

        internal static RageArchiveWrapper7 OpenLiveArchiveWithRetry(
            string archivePath, int attempts = 20, int delayMs = 300)
        {
            for (int i = 1; ; i++)
            {
                try { return RageArchiveWrapper7.Open(archivePath); }
                catch (IOException) when (i < attempts)
                {
                    global::System.Threading.Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException) when (i < attempts)
                {
                    global::System.Threading.Thread.Sleep(delayMs);
                }
            }
        }

        private static void AddFileToPlainPath(string archivePath, string internalPath, byte[] bytes)
        {
            using IArchive archive = OpenLiveArchiveWithRetry(archivePath);
            AddFileToOpenArchive(archive, internalPath, bytes);
            archive.Flush();
        }

        private static void AddFileToOpenArchive(IArchive archive, string internalPath, byte[] bytes)
        {
            var parts = NormalizePath(internalPath).Split('/');
            var dir = archive.Root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var next = dir.GetDirectories()
                    .FirstOrDefault(d => d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (next is null)
                {
                    next = dir.CreateDirectory();
                    next.Name = parts[i];
                }
                dir = next;
            }

            var leaf = parts[^1];
            bool isRsc7 = bytes.Length > 3 && bytes[0] == 0x52 && bytes[1] == 0x53 && bytes[2] == 0x43;
            if (isRsc7)
            {
                var rf = dir.CreateResourceFile();
                rf.Name = leaf;
                rf.Import(new MemoryStream(bytes));
            }
            else
            {
                var bf = dir.CreateBinaryFile();
                bf.Name = leaf;
                bf.Import(new MemoryStream(bytes));
                bf.IsCompressed = false;
                bf.IsEncrypted = false;
                bf.UncompressedSize = (uint)bytes.Length;
            }
        }

        public static bool ReplaceFileInLiveArchive(string archivePath, string internalPath, byte[] bytes)
        {
            ReplaceFileInsideArchive(archivePath, internalPath, bytes);

            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in NormalizePath(internalPath).Split('/'))
                if (part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                    touched.Add(part);
            return FixLiveArchiveChecksums(archivePath, touched);
        }

        public static bool FixLiveArchiveChecksums(
            string archivePath, IReadOnlyCollection<string> touchedNestedRpfNames)
        {
            try
            {
                var exePath = Injector.ArchiveFixer.ResolveExePath();
                if (!File.Exists(exePath))
                {
                    Console.WriteLine("[LiveFix] ArchiveFix.exe не найден - пропускаю checksum fix-up (архив остаётся как после RageLib).");
                    return false;
                }

                var lutsPath = Path.Combine(Path.GetDirectoryName(exePath)!, "gtav_ng_encrypt_luts.dat");
                if (!File.Exists(lutsPath) || new FileInfo(lutsPath).Length < 10_000_000)
                {
                    Console.WriteLine($"[LiveFix] gtav_ng_encrypt_luts.dat отсутствует/усечён ({(File.Exists(lutsPath) ? new FileInfo(lutsPath).Length : 0):N0} b) - пропускаю fix-up ДО force-OPEN.");
                    return false;
                }

                bool nestedOk = true;
                string tempDir = Path.Combine(Path.GetTempPath(), "mg_livefix_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                bool rootOk;
                try
                {
                    using (var archive = RageArchiveWrapper7.Open(archivePath))
                    {
                        if (touchedNestedRpfNames.Count > 0)
                            nestedOk = FixNestedRpfsRecursive(archive.Root, touchedNestedRpfNames, tempDir);

                        archive.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                        archive.Flush();
                    }

                    rootOk = Injector.ArchiveFixer.Fix(archivePath);

                    if (rootOk && IsOpenRootRpf(archivePath))
                    {
                        rootOk = false;
                        Console.WriteLine($"[LiveFix] root {Path.GetFileName(archivePath)}: ArchiveFix отчитался успехом, но корень ОСТАЛСЯ OPEN - считаем провалом.");
                    }
                    Console.WriteLine($"[LiveFix] root {Path.GetFileName(archivePath)}: ArchiveFix {(rootOk ? "OK (проверено по заголовку)" : "FAILED")}");
                }
                finally
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
                return rootOk && nestedOk;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LiveFix] checksum fix-up failed ({ex.GetType().Name}): {ex.Message} - архив остаётся как после RageLib flush.");
                return false;
            }
        }

        private static bool FixNestedRpfsRecursive(
            IArchiveDirectory dir, IReadOnlyCollection<string> targetNames, string tempDir)
        {
            bool allOk = true;
            foreach (var file in dir.GetFiles())
            {
                if (!targetNames.Contains(file.Name, StringComparer.OrdinalIgnoreCase)) continue;
                if (file is not IArchiveBinaryFile bin) continue;

                try
                {
                    string tempPath = Path.Combine(tempDir, file.Name);
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                        bin.Export(fs);

                    using (var nestedFs = new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite))
                    using (var nested = RageArchiveWrapper7.Open(nestedFs, file.Name, true))
                    {
                        nested.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                        nested.Flush();
                    }

                    if (!Injector.ArchiveFixer.Fix(tempPath))
                    {
                        Console.WriteLine($"[LiveFix] nested {file.Name}: ArchiveFix FAILED - оставляю как есть");
                        allOk = false;
                        continue;
                    }

                    byte[] fixedBytes = File.ReadAllBytes(tempPath);
                    using (var ms = new MemoryStream(fixedBytes))
                        bin.Import(ms);
                    bin.IsEncrypted = false;
                    bin.IsCompressed = false;
                    bin.UncompressedSize = fixedBytes.LongLength;
                    Console.WriteLine($"[LiveFix] nested {file.Name}: fixed + reimported ({fixedBytes.Length:N0} b)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LiveFix] nested {file.Name}: fix-up failed ({ex.GetType().Name}): {ex.Message}");
                    allOk = false;
                }
            }

            foreach (var sub in dir.GetDirectories())
                if (!FixNestedRpfsRecursive(sub, targetNames, tempDir))
                    allOk = false;
            return allOk;
        }

        public static bool NormalizeArchiveEncryptionToNG(string archivePath)
        {
            if (!IsOpenRootRpf(archivePath)) return true;

            var openNested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var archive = RageArchiveWrapper7.Open(archivePath);
                CollectOpenNestedRpfs(archive.Root, openNested);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NormalizeNG] OPEN-root nested scan failed ({ex.Message}) - fixing root only");
            }
            Console.WriteLine($"[NormalizeNG] OPEN root; nested OPEN=[{string.Join(",", openNested)}] - normalizing to NG");
            return FixLiveArchiveChecksums(archivePath, openNested);
        }

        private static bool IsOpenRootRpf(string archivePath)
        {
            try
            {
                using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var hdr = new byte[16];
                if (fs.Read(hdr, 0, 16) < 16) return false;
                return BitConverter.ToUInt32(hdr, 0) == 0x52504637
                    && BitConverter.ToUInt32(hdr, 12) == 0x4E45504F;
            }
            catch { return false; }
        }

        private static void CollectOpenNestedRpfs(IArchiveDirectory dir, HashSet<string> open)
        {
            foreach (var file in dir.GetFiles())
            {
                if (file is not IArchiveBinaryFile bin) continue;
                if (!file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var nested = RageArchiveWrapper7.Open(bin.GetStream(), file.Name);
                    if (nested.archive_.Encryption == RageLib.GTA5.Archives.RageArchiveEncryption7.None)
                        open.Add(file.Name);
                }
                catch (Exception ex) { Console.WriteLine($"[NormalizeNG] probe nested {file.Name}: {ex.Message}"); }
            }
            foreach (var sub in dir.GetDirectories())
                CollectOpenNestedRpfs(sub, open);
        }

        public static string? TryFindFirstRpfPath(string rootDir, string preferredFileName = "")
        {
            if (!Directory.Exists(rootDir)) return null;
            try
            {
                string? best = null;
                foreach (var rpf in Directory.EnumerateFiles(rootDir, "*.rpf", SearchOption.AllDirectories))
                {
                    if (!string.IsNullOrEmpty(preferredFileName) &&
                        string.Equals(Path.GetFileName(rpf), preferredFileName, StringComparison.OrdinalIgnoreCase))
                        return rpf;
                    best ??= rpf;
                }
                return best;
            }
            catch { return null; }
        }

        public static byte[]? TryFindFileBytesInDir(string rootDir, string fileNameLower)
        {
            if (!Directory.Exists(rootDir)) return null;

            try
            {
                foreach (var f in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFileName(f), fileNameLower, StringComparison.OrdinalIgnoreCase))
                        return File.ReadAllBytes(f);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PatchCustomizationSupport.TryFindFileBytesInDir] file walk failed: {ex.Message}");
            }

            try
            {
                foreach (var rpf in Directory.EnumerateFiles(rootDir, "*.rpf", SearchOption.AllDirectories))
                {
                    byte[]? found = TryExtractFromRpfRecursive(rpf, fileNameLower);
                    if (found is not null) return found;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PatchCustomizationSupport.TryFindFileBytesInDir] rpf walk failed: {ex.Message}");
            }

            return null;
        }

        private static byte[]? TryExtractFromRpfRecursive(string archivePath, string targetFileLower)
        {
            try
            {
                using IArchive archive = RageArchiveWrapper7.OpenRead(archivePath);
                return TryExtractFromDirRecursive(archive.Root, targetFileLower);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PatchCustomizationSupport.TryExtractFromRpfRecursive] {archivePath}: {ex.Message}");
                return null;
            }
        }

        private static byte[]? TryExtractFromDirRecursive(IArchiveDirectory dir, string targetFileLower)
        {
            IList<IArchiveFile> files;
            try { files = dir.GetFiles(); }
            catch { return null; }

            foreach (var file in files)
            {
                if (string.Equals(file.Name, targetFileLower, StringComparison.OrdinalIgnoreCase))
                {
                    try { return GetRealFileBytes(file); }
                    catch { }
                }
            }

            foreach (var file in files)
            {
                if (!file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
                if (file is not IArchiveBinaryFile bin) continue;
                IArchive? nested = null;
                try
                {
                    using var stream = bin.GetStream();
                    nested = RageArchiveWrapper7.Open(stream, bin.Name, true);
                    var r = TryExtractFromDirRecursive(nested.Root, targetFileLower);
                    if (r is not null) return r;
                }
                catch { }
                finally { nested?.Dispose(); }
            }

            try
            {
                foreach (var sub in dir.GetDirectories())
                {
                    var r = TryExtractFromDirRecursive(sub, targetFileLower);
                    if (r is not null) return r;
                }
            }
            catch { }

            return null;
        }

        public static byte[]? TryExtractFirstMatchingFileBytes(
            string archivePath, string canonicalFileName)
        {
            using IArchive archive = RageArchiveWrapper7.OpenRead(archivePath);
            return FindFirstMatchingRecursive(archive.Root, canonicalFileName);
        }

        private static byte[]? FindFirstMatchingRecursive(
            IArchiveDirectory currentDirectory, string canonicalFileName)
        {
            IList<IArchiveFile> files;
            try { files = currentDirectory.GetFiles(); }
            catch { return null; }

            foreach (IArchiveFile file in files)
            {
                if (file.Name.Equals(canonicalFileName, StringComparison.OrdinalIgnoreCase))
                {
                    try { return GetRealFileBytes(file); }
                    catch {}
                }
            }

            foreach (IArchiveFile file in files)
            {
                if (file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) &&
                    file is IArchiveBinaryFile nestedBin)
                {
                    IArchive? nested = null;
                    try
                    {
                        var stream = nestedBin.GetStream();
                        nested = RageArchiveWrapper7.Open(stream, nestedBin.Name, true);
                        var found = FindFirstMatchingRecursive(nested.Root, canonicalFileName);
                        if (found != null) return found;
                    }
                    catch {}
                    finally { nested?.Dispose(); }
                }
            }

            foreach (IArchiveDirectory dir in currentDirectory.GetDirectories())
            {
                var found = FindFirstMatchingRecursive(dir, canonicalFileName);
                if (found != null) return found;
            }

            return null;
        }

        public static int ReplaceAllMatchingFilesInLiveArchive(
            string archivePath, string canonicalFileName, byte[] bytes,
            ISet<string>? deferFixAccumulator = null)
            => ReplaceAllMatchingFilesInLiveArchive(archivePath, canonicalFileName, bytes, out _, deferFixAccumulator);

        public static int ReplaceAllMatchingFilesInLiveArchive(
            string archivePath, string canonicalFileName, byte[] bytes,
            out bool checksumOk,
            ISet<string>? deferFixAccumulator = null)
        {
            checksumOk = true;
            var touchedNested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int replaced;
            using (IArchive archive = OpenLiveArchiveWithRetry(archivePath))
            {
                replaced = ReplaceAllMatchingRecursive(archive.Root, canonicalFileName, bytes,
                    parentArchives: new List<IArchive>(), touchedNestedRpfNames: touchedNested);
                if (replaced > 0)
                    archive.Flush();
            }
            if (deferFixAccumulator is not null)
            {
                foreach (var n in touchedNested) deferFixAccumulator.Add(n);
                return replaced;
            }
            if (replaced > 0)
                checksumOk = FixLiveArchiveChecksums(archivePath, touchedNested);
            return replaced;
        }

        private static int ReplaceAllMatchingRecursive(
            IArchiveDirectory currentDirectory,
            string canonicalFileName,
            byte[] bytes,
            List<IArchive> parentArchives,
            ISet<string>? touchedNestedRpfNames = null)
        {
            int count = 0;
            IList<IArchiveFile> files;
            try { files = currentDirectory.GetFiles(); }
            catch { return 0; }

            foreach (IArchiveFile file in files)
            {
                bool nameMatches = file.Name.Equals(canonicalFileName, StringComparison.OrdinalIgnoreCase);
                bool variantMatches =
                    file.Name.Length > canonicalFileName.Length &&
                    file.Name.EndsWith(canonicalFileName, StringComparison.OrdinalIgnoreCase) &&
                    !char.IsLetterOrDigit(file.Name[file.Name.Length - canonicalFileName.Length - 1]);

                if (nameMatches || variantMatches)
                {
                    if (file is IArchiveBinaryFile bin)
                    {
                        bin.Import(new MemoryStream(bytes));

                        bin.IsCompressed = false;
                        bin.IsEncrypted = false;
                        bin.UncompressedSize = (uint)bytes.Length;
                        count++;
                    }
                    else if (file is IArchiveResourceFile res)
                    {
                        res.Import(new MemoryStream(bytes));
                        count++;
                    }
                }

                if (file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) &&
                    file is IArchiveBinaryFile nestedBin)
                {
                    IArchive? nested = null;
                    try
                    {
                        var stream = nestedBin.GetStream();
                        nested = RageArchiveWrapper7.Open(stream, nestedBin.Name, true);
                        var nestedParents = new List<IArchive>(parentArchives) { nested };
                        int nestedCount = ReplaceAllMatchingRecursive(nested.Root, canonicalFileName, bytes, nestedParents, touchedNestedRpfNames);
                        if (nestedCount > 0)
                        {
                            nested.Flush();
                            count += nestedCount;
                            touchedNestedRpfNames?.Add(nestedBin.Name);
                        }
                    }
                    catch
                    {

                    }
                    finally
                    {
                        nested?.Dispose();
                    }
                }
            }

            foreach (IArchiveDirectory dir in currentDirectory.GetDirectories())
            {
                count += ReplaceAllMatchingRecursive(dir, canonicalFileName, bytes, parentArchives, touchedNestedRpfNames);
            }

            return count;
        }

        public static int ReplaceFilesByContentInLiveArchive(
            string archivePath,
            Func<string, bool> leafMatch,
            Func<byte[], bool> contentMatch,
            byte[] bytes,
            out bool checksumOk,
            out List<string> replacedPaths)
        {
            checksumOk = true;
            replacedPaths = new List<string>();
            var touchedNested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int replaced;
            using (IArchive archive = OpenLiveArchiveWithRetry(archivePath))
            {
                replaced = ReplaceByContentRecursive(archive.Root, "", leafMatch, contentMatch, bytes,
                    touchedNestedRpfNames: touchedNested, replacedPaths: replacedPaths);
                if (replaced > 0)
                    archive.Flush();
            }
            if (replaced > 0)
                checksumOk = FixLiveArchiveChecksums(archivePath, touchedNested);
            return replaced;
        }

        public static List<string> FindLiveFilesByContent(
            string gtaRootPath, Func<string, bool> leafMatch, Func<byte[], bool> contentMatch)
        {
            try { return FindFilesByContentInLiveArchive(ResolveUpdateRpfPath(gtaRootPath), leafMatch, contentMatch); }
            catch { return new List<string>(); }
        }

        public static List<string> FindFilesByContentInLiveArchive(
            string archivePath, Func<string, bool> leafMatch, Func<byte[], bool> contentMatch)
        {
            var found = new List<string>();
            try
            {
                using IArchive archive = RageArchiveWrapper7.OpenRead(archivePath);
                ReplaceByContentRecursive(archive.Root, "", leafMatch, contentMatch,
                    replacement: null, touchedNestedRpfNames: null, replacedPaths: found);
            }
            catch {}
            return found;
        }

        private static int ReplaceByContentRecursive(
            IArchiveDirectory currentDirectory,
            string prefix,
            Func<string, bool> leafMatch,
            Func<byte[], bool> contentMatch,
            byte[]? replacement,
            ISet<string>? touchedNestedRpfNames,
            List<string> replacedPaths)
        {
            int count = 0;
            IList<IArchiveFile> files;
            try { files = currentDirectory.GetFiles(); }
            catch { return 0; }

            foreach (IArchiveFile file in files)
            {
                if (!leafMatch(file.Name)) continue;

                byte[]? current;
                try { current = GetRealFileBytes(file); }
                catch { continue; }
                if (current is null) continue;

                bool match;
                try { match = contentMatch(current); }
                catch { continue; }
                if (!match) continue;

                string full = prefix.Length == 0 ? file.Name : prefix + "/" + file.Name;
                if (replacement is null) { replacedPaths.Add(full); count++; continue; }

                try
                {
                    if (file is IArchiveBinaryFile bin)
                    {
                        bin.Import(new MemoryStream(replacement));
                        bin.IsCompressed = false;
                        bin.IsEncrypted = false;
                        bin.UncompressedSize = (uint)replacement.Length;
                    }
                    else if (file is IArchiveResourceFile res)
                    {
                        res.Import(new MemoryStream(replacement));
                    }
                    else continue;
                }
                catch { continue; }

                replacedPaths.Add(full);
                count++;
            }

            foreach (IArchiveFile file in files)
            {
                if (!file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
                if (file is not IArchiveBinaryFile nestedBin) continue;
                IArchive? nested = null;
                try
                {
                    var stream = nestedBin.GetStream();
                    nested = RageArchiveWrapper7.Open(stream, nestedBin.Name, true);
                    var childPrefix = prefix.Length == 0 ? file.Name : prefix + "/" + file.Name;
                    int nestedCount = ReplaceByContentRecursive(nested.Root, childPrefix, leafMatch,
                        contentMatch, replacement, touchedNestedRpfNames, replacedPaths);
                    if (nestedCount > 0)
                    {
                        if (replacement is not null)
                        {
                            nested.Flush();
                            touchedNestedRpfNames?.Add(nestedBin.Name);
                        }
                        count += nestedCount;
                    }
                }
                catch {}
                finally { nested?.Dispose(); }
            }

            foreach (IArchiveDirectory dir in currentDirectory.GetDirectories())
            {
                count += ReplaceByContentRecursive(dir,
                    prefix.Length == 0 ? dir.Name : prefix + "/" + dir.Name,
                    leafMatch, contentMatch, replacement, touchedNestedRpfNames, replacedPaths);
            }

            return count;
        }

        private static IArchiveDirectory? NavigateDir(IArchiveDirectory root, params string[] segments)
        {
            var cur = root;
            foreach (var seg in segments)
            {
                IArchiveDirectory? next = null;
                try
                {
                    foreach (var d in cur.GetDirectories())
                        if (d.Name.Equals(seg, StringComparison.OrdinalIgnoreCase)) { next = d; break; }
                }
                catch { return null; }
                if (next is null) return null;
                cur = next;
            }
            return cur;
        }

        public static bool AudioSubtreeContainsFile(string archivePath, string fileName)
        {
            try
            {
                using IArchive archive = RageArchiveWrapper7.OpenRead(archivePath);
                var audio = NavigateDir(archive.Root, "x64", "audio");
                return audio is not null && AudioContainsRecursive(audio, fileName);
            }
            catch { return false; }
        }

        private static bool AudioContainsRecursive(IArchiveDirectory dir, string fileName)
        {
            IList<IArchiveFile> files;
            try { files = dir.GetFiles(); } catch { return false; }
            foreach (var f in files)
                if (f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var f in files)
            {
                if (f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && f is IArchiveBinaryFile nb)
                {
                    IArchive? nested = null;
                    try { nested = RageArchiveWrapper7.Open(nb.GetStream(), nb.Name, true);
                          if (AudioContainsRecursive(nested.Root, fileName)) return true; }
                    catch { }
                    finally { nested?.Dispose(); }
                }
            }
            foreach (var d in dir.GetDirectories())
                if (AudioContainsRecursive(d, fileName)) return true;
            return false;
        }

        public static int ReplaceAudioAwcInLiveArchive(
            string archivePath, string fileName, byte[] bytes,
            out byte[]? originalBytes, out bool checksumOk)
        {
            originalBytes = null;
            checksumOk = true;
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byte[]? captured = null;
            int replaced;
            using (IArchive archive = OpenLiveArchiveWithRetry(archivePath))
            {
                var audio = NavigateDir(archive.Root, "x64", "audio");
                if (audio is null) { return 0; }
                replaced = ReplaceAudioRecursive(audio, fileName, bytes, touched, ref captured);
                if (replaced > 0) archive.Flush();
            }
            originalBytes = captured;
            if (replaced > 0)
                checksumOk = FixLiveArchiveChecksums(archivePath, touched);
            return replaced;
        }

        private static int ReplaceAudioRecursive(
            IArchiveDirectory dir, string fileName, byte[] bytes,
            ISet<string> touchedNested, ref byte[]? captured)
        {
            int count = 0;
            IList<IArchiveFile> files;
            try { files = dir.GetFiles(); } catch { return 0; }
            foreach (IArchiveFile file in files)
            {
                if (!file.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase)) continue;
                if (captured is null) { try { captured = GetRealFileBytes(file); } catch { } }
                if (file is IArchiveBinaryFile bin)
                {
                    bin.Import(new MemoryStream(bytes));
                    bin.IsCompressed = false;
                    bin.IsEncrypted = false;
                    bin.UncompressedSize = (uint)bytes.Length;
                    count++;
                }
                else if (file is IArchiveResourceFile res)
                {
                    res.Import(new MemoryStream(bytes));
                    count++;
                }
            }
            foreach (IArchiveFile file in files)
            {
                if (file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && file is IArchiveBinaryFile nb)
                {
                    IArchive? nested = null;
                    try
                    {
                        nested = RageArchiveWrapper7.Open(nb.GetStream(), nb.Name, true);
                        int n = ReplaceAudioRecursive(nested.Root, fileName, bytes, touchedNested, ref captured);
                        if (n > 0) { nested.Flush(); count += n; touchedNested.Add(nb.Name); }
                    }
                    catch { }
                    finally { nested?.Dispose(); }
                }
            }
            foreach (IArchiveDirectory d in dir.GetDirectories())
                count += ReplaceAudioRecursive(d, fileName, bytes, touchedNested, ref captured);
            return count;
        }

        private static void ReplaceFileInsideArchive(string archivePath, string internalPath, byte[] bytes)
        {
            using IArchive archive = OpenLiveArchiveWithRetry(archivePath);

            if (!TryReplaceRecursive(archive.Root, NormalizePath(internalPath).Split('/'), 0, bytes))
                throw new FileNotFoundException($"Could not find internal file {internalPath} inside {archivePath}.");

            archive.Flush();
        }

        private static bool TryReplaceRecursive(IArchiveDirectory currentDirectory, string[] parts, int index, byte[] bytes, bool allowCreate = false)
        {
            if (index >= parts.Length)
                return false;

            string currentPart = parts[index];

            if (index == parts.Length - 1)
            {
                IArchiveFile? file = currentDirectory.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(currentPart, StringComparison.OrdinalIgnoreCase));

                if (file == null)
                {
                    if (!allowCreate) return false;
                    bool isRsc7 = bytes.Length > 3 && bytes[0] == 0x52 && bytes[1] == 0x53 && bytes[2] == 0x43;
                    if (isRsc7)
                    {
                        var rf = currentDirectory.CreateResourceFile();
                        rf.Name = currentPart;
                        rf.Import(new MemoryStream(bytes));
                    }
                    else
                    {
                        var bf = currentDirectory.CreateBinaryFile();
                        bf.Name = currentPart;
                        bf.Import(new MemoryStream(bytes));
                        bf.IsCompressed = false;
                        bf.IsEncrypted = false;
                        bf.UncompressedSize = (uint)bytes.Length;
                    }
                    return true;
                }

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
                return TryReplaceRecursive(subDirectory, parts, index + 1, bytes, allowCreate);

            IArchiveBinaryFile? nestedRpf = currentDirectory.GetFiles()
                .FirstOrDefault(f => f.Name.Equals(currentPart, StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;

            if (nestedRpf != null && currentPart.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = nestedRpf.GetStream();
                using IArchive nestedArchive = RageArchiveWrapper7.Open(stream, nestedRpf.Name, true);
                bool replaced = TryReplaceRecursive(nestedArchive.Root, parts, index + 1, bytes, allowCreate);
                if (replaced)
                    nestedArchive.Flush();
                return replaced;
            }

            return false;
        }

        private static byte[] GetRealFileBytes(IArchiveFile file)
        {
            if (file is IArchiveBinaryFile binaryFile)
            {
                using var memoryStream = new MemoryStream();
                binaryFile.Export(memoryStream);
                byte[] buffer = memoryStream.ToArray();

                if (binaryFile.IsEncrypted)
                {
                    uint hash = GTA5Hash.CalculateHash(binaryFile.Name);
                    uint keyIndex = (hash + (uint)binaryFile.UncompressedSize + (101 - 40)) % 0x65;
                    buffer = GTA5Crypto.Decrypt(buffer, GTA5Constants.PC_NG_KEYS[keyIndex]);
                }

                if (binaryFile.IsCompressed)
                {
                    using var deflate = new global::System.IO.Compression.DeflateStream(
                        new MemoryStream(buffer),
                        global::System.IO.Compression.CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    deflate.CopyTo(output);
                    return output.ToArray();
                }

                return buffer;
            }

            using (var memoryStream = new MemoryStream())
            {
                file.Export(memoryStream);
                return memoryStream.ToArray();
            }
        }

        private static string? ResolvePhysicalPath(string patchRootDirectory, PatchAction action, string fallbackTargetPath)
        {
            if (MiamiGraphics.Core.System.SafePath.TryResolveInside(
                    patchRootDirectory, action.SourcePath, out var physicalPath, out _)
                && File.Exists(physicalPath))
                return physicalPath;

            if (MiamiGraphics.Core.System.SafePath.TryResolveInside(
                    Path.Combine(patchRootDirectory, "patch_files"), fallbackTargetPath, out var fallbackPath, out _)
                && File.Exists(fallbackPath))
                return fallbackPath;

            return null;
        }

        private static bool TrySplitPhysicalRpfPath(string targetPath, out string parentRpfRelativePath, out string internalPathInRpf)
        {
            string normalizedPath = NormalizePath(targetPath);
            int rpfIndex = normalizedPath.IndexOf(".rpf/", StringComparison.OrdinalIgnoreCase);

            if (rpfIndex < 0)
            {
                parentRpfRelativePath = "";
                internalPathInRpf = "";
                return false;
            }

            parentRpfRelativePath = normalizedPath.Substring(0, rpfIndex + 4);
            internalPathInRpf = normalizedPath.Substring(rpfIndex + 5);
            return !string.IsNullOrWhiteSpace(internalPathInRpf);
        }

        private static string CreateTempWorkspaceFile(string fileName, byte[] bytes)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "MiamiGraphics.PatchWorkspace");
            Directory.CreateDirectory(tempDirectory);

            string safeName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            string hash = ComputeSha256(bytes).Substring(0, 8);
            string tempPath = Path.Combine(tempDirectory, $"{safeName}_{hash}_{Guid.NewGuid():N}{extension}");

            File.WriteAllBytes(tempPath, bytes);
            return tempPath;
        }

        private static ActionType ResolveParentArchiveActionType(DiffManifest manifest, string parentRpfRelativePath)
        {
            PatchAction? parentAction = manifest.Actions.FirstOrDefault(a =>
                NormalizePath(a.TargetPath) == NormalizePath(parentRpfRelativePath));

            if (parentAction != null && parentAction.Type != ActionType.Delete)
                return parentAction.Type;

            return ActionType.Replace;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace("\\", "/").TrimStart('/');
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }
    }
}
