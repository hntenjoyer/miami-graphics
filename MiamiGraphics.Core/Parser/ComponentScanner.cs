using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Parser
{
    public class ComponentScanner
    {
        public ResolvedComponentMap BuildResolvedComponentMap(IArchiveDirectory moddedRoot, ContentXmlInfo contentInfo)
        {
            var map = new ResolvedComponentMap();

            map.Components["minimap"] = ResolveComponent(
                moddedRoot,
                contentInfo,
                targetFile: "minimap.gfx",
                flags: new[] { "importable", "replaceable" },
                fallbackRpfs: new[]
                {
                    "x64/patch/data/cdimages/scaleform_minimap.rpf",
                    "x64/data/cdimages/scaleform_minimap.rpf"
                });

            map.Components["crosshair"] = ResolveComponent(
                moddedRoot,
                contentInfo,
                targetFile: "hud_reticle.gfx",
                flags: new[] { "importable", "replaceable" },
                fallbackRpfs: new[]
                {
                    "x64/patch/data/cdimages/scaleform_generic.rpf",
                    "x64/data/cdimages/scaleform_generic.rpf"
                },
                relatedFilePattern: "reticle");

            map.Components["tracers"] = ResolveTracersComponent(moddedRoot, contentInfo);

            map.Components["bloodfx"] = ResolveLooseFileComponent(
                moddedRoot,
                contentInfo,
                targetFile: "bloodfx.dat",
                defaultPath: "common/data/effects/bloodfx.dat",
                flags: new[] { "importable", "replaceable" });

            var tcInfo = FindTimecyclePaths(moddedRoot);
            if (tcInfo != null)
                map.Components["timecycle"] = tcInfo;

            return map;
        }

        private ComponentInfo FindTimecyclePaths(IArchiveDirectory moddedRoot)
        {
            var tcPaths = FindFilesByGroupRulesRecursive(moddedRoot, new Func<string, bool>[]
            {
                p => p.Contains("common/data/timecycle/", StringComparison.OrdinalIgnoreCase),
                p => p.EndsWith("common/data/visualsettings.dat", StringComparison.OrdinalIgnoreCase),
                p => p.EndsWith("common/data/cloudkeyframes.xml", StringComparison.OrdinalIgnoreCase),
                p => p.EndsWith("common/data/clouds.xml", StringComparison.OrdinalIgnoreCase),
                p => Regex.IsMatch(p, @"timecycle_mods_.*\.xml$", RegexOptions.IgnoreCase),
                p => p.Contains("weather/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase),
                p => p.Contains("clouds/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            });

            tcPaths = tcPaths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

            if (tcPaths.Any())
            {
                return new ComponentInfo
                {
                    IsFound = true,
                    SourceRpf = "common.rpf / direct",
                    InternalPaths = tcPaths,
                    Flags = new List<string> { "importable", "replaceable" }
                };
            }

            return null;
        }

        private ComponentInfo ResolveComponent(
            IArchiveDirectory root,
            ContentXmlInfo contentInfo,
            string targetFile,
            string[] flags,
            string[] fallbackRpfs,
            string[] additionalRelatedFiles = null,
            string relatedFilePattern = null)
        {
            var info = new ComponentInfo
            {
                Flags = flags.ToList()
            };

            string foundCanonicalPath = null;

            foreach (var customRpf in contentInfo.CustomRpfs)
            {
                if (CheckIfFileExistsInRpfRecursive(root, customRpf, targetFile, out string exactInternalPath))
                {
                    foundCanonicalPath = $"{customRpf}/{exactInternalPath}";
                    info.SourceRpf = customRpf;
                    break;
                }
            }

            if (foundCanonicalPath == null)
            {
                foreach (var fallback in fallbackRpfs)
                {
                    if (CheckIfFileExistsInRpfRecursive(root, fallback, targetFile, out string exactInternalPath))
                    {
                        foundCanonicalPath = $"{fallback}/{exactInternalPath}";
                        info.SourceRpf = fallback;
                        break;
                    }
                }
            }

            if (foundCanonicalPath != null)
            {
                info.IsFound = true;
                info.InternalPaths.Add(foundCanonicalPath);

                if (additionalRelatedFiles != null || relatedFilePattern != null)
                {
                    var related = GetRelatedFilesInRpfRecursive(root, info.SourceRpf, additionalRelatedFiles, relatedFilePattern);
                    info.InternalPaths.AddRange(related);
                }

                info.InternalPaths = info.InternalPaths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }

            return info;
        }

        private ComponentInfo ResolveTracersComponent(IArchiveDirectory root, ContentXmlInfo contentInfo)
        {
            var info = new ComponentInfo
            {
                Flags = new List<string> { "importable", "replaceable" }
            };

            string[] ptfxRpfNames = { "ptfx.rpf", "ptfx_hi.rpf", "ptfx_lo.rpf" };

            var candidates = new List<string>();

            foreach (var customRpf in contentInfo.CustomRpfs)
            {
                string fileName = customRpf.Split('/').Last();
                if (ptfxRpfNames.Any(n => fileName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(customRpf);
            }

            foreach (var name in ptfxRpfNames)
            {
                candidates.Add($"x64/patch/data/effects/{name}");
                candidates.Add($"x64/data/effects/{name}");
            }

            string firstSourceRpf = null;
            foreach (var rpf in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (CheckIfFileExistsInRpfRecursive(root, rpf, "core.ypt", out string exactInternalPath))
                {
                    info.InternalPaths.Add($"{rpf}/{exactInternalPath}");
                    firstSourceRpf ??= rpf;
                }
            }

            if (info.InternalPaths.Count > 0)
            {
                info.IsFound = true;
                info.SourceRpf = firstSourceRpf;
                info.InternalPaths = info.InternalPaths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }

            return info;
        }

        private ComponentInfo ResolveLooseFileComponent(
            IArchiveDirectory root,
            ContentXmlInfo contentInfo,
            string targetFile,
            string defaultPath,
            string[] flags)
        {
            var info = new ComponentInfo
            {
                Flags = flags.ToList()
            };

            string foundPath = contentInfo.AllFilesToEnable
                .FirstOrDefault(path => path.EndsWith(targetFile, StringComparison.OrdinalIgnoreCase));

            foundPath = NormalizeLooseFilePath(foundPath);

            if (foundPath == null && FileExistsByPathRecursive(root, defaultPath))
            {
                foundPath = defaultPath;
            }
            else if (foundPath != null && !FileExistsByPathRecursive(root, foundPath))
            {
                foundPath = null;
            }

            if (foundPath != null)
            {
                info.IsFound = true;
                info.SourceRpf = foundPath.StartsWith("update.rpf/", StringComparison.OrdinalIgnoreCase)
                    ? "update.rpf"
                    : "update.rpf / filesToEnable";
                info.InternalPaths.Add(foundPath);
            }

            return info;
        }

        private string NormalizeLooseFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = path.Replace("\\", "/").Trim();

            if (normalized.StartsWith("update:/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("update:/".Length);

            if (normalized.StartsWith("update.rpf/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("update.rpf/".Length);

            return normalized.TrimStart('/');
        }

        private bool CheckIfFileExistsInRpfRecursive(IArchiveDirectory root, string rpfPath, string targetFile, out string exactInternalPath)
        {
            exactInternalPath = null;

            var rpfArc = OpenNestedArchiveByPath(root, rpfPath);
            if (rpfArc == null)
                return false;

            using (rpfArc)
            {
                exactInternalPath = FindFileInDirectoryRecursive(rpfArc.Root, targetFile, "");
                return exactInternalPath != null;
            }
        }

        private string FindFileInDirectoryRecursive(IArchiveDirectory dir, string targetFileName, string currentPath)
        {

            var files = dir.GetFiles();

            var exact = files.FirstOrDefault(f => f.Name.Equals(targetFileName, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return currentPath + exact.Name;

            var suffix = files.FirstOrDefault(f =>
            {
                if (f.Name.Length <= targetFileName.Length) return false;
                if (!f.Name.EndsWith(targetFileName, StringComparison.OrdinalIgnoreCase)) return false;
                char boundary = f.Name[f.Name.Length - targetFileName.Length - 1];
                return !char.IsLetterOrDigit(boundary);
            });
            if (suffix != null)
                return currentPath + suffix.Name;

            foreach (var nested in files)
            {
                if (!nested.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
                if (nested is not IArchiveBinaryFile binFile) continue;

                try
                {
                    using var stream = binFile.GetStream();
                    using var nestedArc = RageArchiveWrapper7.Open(stream, binFile.Name, true);
                    var found = FindFileInDirectoryRecursive(nestedArc.Root, targetFileName, currentPath + nested.Name + "/");
                    if (found != null)
                        return found;
                }
                catch
                {

                }
            }

            foreach (var subDir in dir.GetDirectories())
            {
                var found = FindFileInDirectoryRecursive(subDir, targetFileName, currentPath + subDir.Name + "/");
                if (found != null)
                    return found;
            }

            return null;
        }

        private List<string> GetRelatedFilesInRpfRecursive(IArchiveDirectory root, string rpfPath, string[] exactNames, string pattern)
        {
            var list = new List<string>();

            var rpfArc = OpenNestedArchiveByPath(root, rpfPath);
            if (rpfArc == null)
                return list;

            using (rpfArc)
            {
                SearchRelatedFilesRecursive(rpfArc.Root, exactNames, pattern, "", rpfPath, list);
            }

            return list
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        private void SearchRelatedFilesRecursive(IArchiveDirectory dir, string[] exactNames, string pattern, string internalPath, string basePath, List<string> results)
        {
            foreach (var file in dir.GetFiles())
            {
                bool matchExact = exactNames != null && exactNames.Any(n => n.Equals(file.Name, StringComparison.OrdinalIgnoreCase));
                bool matchPattern = pattern != null && file.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

                if (matchExact || matchPattern)
                    results.Add($"{basePath}/{internalPath}{file.Name}");

                if (file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && file is IArchiveBinaryFile binFile)
                {
                    try
                    {
                        using var stream = binFile.GetStream();
                        using var nestedArc = RageArchiveWrapper7.Open(stream, binFile.Name, true);
                        SearchRelatedFilesRecursive(nestedArc.Root, exactNames, pattern, internalPath + file.Name + "/", basePath, results);
                    }
                    catch
                    {
                    }
                }
            }

            foreach (var subDir in dir.GetDirectories())
            {
                SearchRelatedFilesRecursive(subDir, exactNames, pattern, internalPath + subDir.Name + "/", basePath, results);
            }
        }

        private List<string> FindFilesByGroupRulesRecursive(IArchiveDirectory dir, Func<string, bool>[] rules, string currentPath = "")
        {
            var list = new List<string>();

            foreach (var file in dir.GetFiles())
            {
                string fullPath = currentPath + file.Name;

                if (rules.Any(rule => rule(fullPath)))
                    list.Add(fullPath);

                if (file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && file is IArchiveBinaryFile binFile)
                {
                    try
                    {
                        using var stream = binFile.GetStream();
                        using var nestedArc = RageArchiveWrapper7.Open(stream, file.Name, true);
                        list.AddRange(FindFilesByGroupRulesRecursive(nestedArc.Root, rules, fullPath + "/"));
                    }
                    catch
                    {
                    }
                }
            }

            foreach (var subDir in dir.GetDirectories())
            {
                list.AddRange(FindFilesByGroupRulesRecursive(subDir, rules, currentPath + subDir.Name + "/"));
            }

            return list
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool FileExistsByPathRecursive(IArchiveDirectory root, string fullPath)
        {
            string[] parts = fullPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            return FileExistsByPathRecursive(root, parts, 0);
        }

        private bool FileExistsByPathRecursive(IArchiveDirectory currentDir, string[] parts, int index)
        {
            if (index >= parts.Length)
                return false;

            string current = parts[index];

            if (index == parts.Length - 1)
            {
                return currentDir.GetFiles().Any(f => f.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
            }

            var subDir = currentDir.GetDirectories().FirstOrDefault(d => d.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (subDir != null)
                return FileExistsByPathRecursive(subDir, parts, index + 1);

            var nestedRpf = currentDir.GetFiles().FirstOrDefault(f => f.Name.Equals(current, StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;
            if (nestedRpf != null && current.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var stream = nestedRpf.GetStream();
                    using var nestedArc = RageArchiveWrapper7.Open(stream, nestedRpf.Name, true);
                    return FileExistsByPathRecursive(nestedArc.Root, parts, index + 1);
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private IArchive OpenNestedArchiveByPath(IArchiveDirectory root, string rpfPath)
        {
            var parts = rpfPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            IArchiveDirectory current = root;

            foreach (var part in parts.Take(parts.Length - 1))
            {
                current = current?.GetDirectories().FirstOrDefault(d => d.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (current == null)
                    return null;
            }

            var rpfFile = current?.GetFiles().FirstOrDefault(f => f.Name.Equals(parts.Last(), StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;
            if (rpfFile == null)
                return null;

            try
            {
                var stream = rpfFile.GetStream();
                return RageArchiveWrapper7.Open(stream, rpfFile.Name, true);
            }
            catch
            {
                return null;
            }
        }
    }
}
