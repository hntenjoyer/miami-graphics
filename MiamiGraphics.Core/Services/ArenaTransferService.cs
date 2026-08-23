using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.Core.Services
{
    public sealed class ArenaTransferRequest
    {
        public string RecipientPatchRoot { get; init; } = "";
        public DiffManifest RecipientManifest { get; init; } = default!;
        public string DonorPatchRoot { get; init; } = "";
    }

    public sealed class ArenaTransferResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public IReadOnlyList<string> TransferredFiles { get; init; } = Array.Empty<string>();
    }

    public sealed class ArenaTransferService
    {
        private static readonly JsonSerializerOptions JsonOpts = CreateJsonOptions();

        private static readonly string AuditLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiamiGraphics",
            "arena-transfer.log");

        public ArenaTransferResult Transfer(ArenaTransferRequest request)
        {

            void Audit(string line)
            {
                Console.WriteLine($"[ArenaTransfer] {line}");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(AuditLogPath)!);
                    File.AppendAllText(AuditLogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {line}{Environment.NewLine}");
                }
                catch {  }
            }
            Audit($"=== TRANSFER START: recipient={request.RecipientPatchRoot} donor={request.DonorPatchRoot} ===");

            var preSnapshot = SnapshotManifest(request.RecipientManifest);
            Audit($"pre-snapshot: {preSnapshot.Count} manifest entries");

            List<string> originalRecipientArenaPaths = new();

            ResolvedComponentMap? recipientMap = LoadComponentMap(request.RecipientPatchRoot);
            ResolvedComponentMap? donorMap = LoadComponentMap(request.DonorPatchRoot);

            if (recipientMap == null ||
                !recipientMap.Components.TryGetValue("arena", out ComponentInfo? aArena) ||
                aArena == null || !aArena.IsFound || aArena.InternalPaths.Count == 0)
                return Fail(Loc.T("error.recipientNoArenaRegistered"));

            if (donorMap == null ||
                !donorMap.Components.TryGetValue("arena", out ComponentInfo? bArena) ||
                bArena == null || !bArena.IsFound || bArena.InternalPaths.Count == 0)
                return Fail(Loc.T("error.donorNoArenaRegistered"));

            originalRecipientArenaPaths = aArena.InternalPaths.ToList();
            Audit($"recipient arena paths: {string.Join(", ", originalRecipientArenaPaths)}");
            Audit($"donor arena paths:     {string.Join(", ", bArena.InternalPaths)}");

            string aPatchFiles = Path.Combine(request.RecipientPatchRoot, "patch_files");

            string bComponentDir = Path.Combine(request.DonorPatchRoot, "components", "arena");
            string bPatchFiles   = Path.Combine(request.DonorPatchRoot, "patch_files");
            string bRoot         = Directory.Exists(bComponentDir) ? bComponentDir : bPatchFiles;

            Console.WriteLine($"[ArenaTransfer] A: {string.Join(", ", aArena.InternalPaths)}");
            Console.WriteLine($"[ArenaTransfer] B: {string.Join(", ", bArena.InternalPaths)}");
            Console.WriteLine($"[ArenaTransfer] B-root: {bRoot}");

            foreach (string bPath in bArena.InternalPaths)
            {
                string abs = Path.Combine(bRoot, bPath.Replace('/', '\\'));
                if (!File.Exists(abs))
                    return Fail(Loc.T("error.donorArenaFileMissing", ("path", abs)));
            }

            string basePrefixA = GetDirectory(aArena.InternalPaths[0]);
            var newPaths = new List<(string DonorInternalPath, string NewInternalPath, string NewFileName)>();
            foreach (string bPath in bArena.InternalPaths)
            {
                string fileName = Path.GetFileName(bPath);
                string newInternal = string.IsNullOrEmpty(basePrefixA) ? fileName : $"{basePrefixA}/{fileName}";
                newPaths.Add((bPath, newInternal, fileName));
            }

            Console.WriteLine($"[ArenaTransfer] basePrefixA = '{basePrefixA}'");
            Console.WriteLine($"[ArenaTransfer] Файлов A: {aArena.InternalPaths.Count}, Файлов B: {bArena.InternalPaths.Count}");
            foreach (var np in newPaths)
                Console.WriteLine($"[ArenaTransfer]   B:{np.DonorInternalPath} → A:{np.NewInternalPath}");

            var oldSet = new HashSet<string>(
                aArena.InternalPaths.Select(NormalizePath),
                StringComparer.OrdinalIgnoreCase);

            foreach (var np in newPaths)
            {
                string normNew = NormalizePath(np.NewInternalPath);
                if (oldSet.Contains(normNew))
                    continue;
                string newAbs = Path.Combine(aPatchFiles, np.NewInternalPath.Replace('/', '\\'));
                if (File.Exists(newAbs))
                    return Fail(Loc.T("error.transferCollision", ("path", np.NewInternalPath)));
            }

            var manifest = request.RecipientManifest;
            foreach (string aPath in aArena.InternalPaths)
            {
                string oldAbs = Path.Combine(aPatchFiles, aPath.Replace('/', '\\'));
                if (File.Exists(oldAbs))
                {
                    File.Delete(oldAbs);
                    Console.WriteLine($"[ArenaTransfer] Удалён старый файл: {aPath}");
                }

                int removed = manifest.Actions.RemoveAll(a =>
                    NormalizePath(a.TargetPath) == NormalizePath(aPath));
                if (removed > 0)
                    Console.WriteLine($"[ArenaTransfer] Из manifest удалено действий: {removed} (для {aPath})");
            }

            var transferred = new List<string>();
            foreach (var np in newPaths)
            {
                string srcAbs = Path.Combine(bRoot, np.DonorInternalPath.Replace('/', '\\'));
                string dstAbs = Path.Combine(aPatchFiles, np.NewInternalPath.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(dstAbs)!);
                File.Copy(srcAbs, dstAbs, true);

                byte[] bytes = File.ReadAllBytes(dstAbs);
                manifest.Actions.Add(new PatchAction
                {
                    Type = ActionType.Import,
                    TargetPath = np.NewInternalPath,
                    SourcePath = $"patch_files/{np.NewInternalPath}",
                    Size = bytes.LongLength,
                    Sha256 = ComputeSha256(bytes),
                    IsWholeReplaceNestedRpf = np.NewInternalPath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)
                });

                transferred.Add(np.NewInternalPath);
                Console.WriteLine($"[ArenaTransfer] Скопирован: {np.DonorInternalPath} → {np.NewInternalPath} ({bytes.Length} байт)");
            }

            UpdateContentXml(aPatchFiles, aArena.InternalPaths, newPaths, manifest);

            if (recipientMap.Components.TryGetValue("arena", out var recipientArena) && recipientArena != null)
            {
                recipientArena.InternalPaths = newPaths.Select(np => np.NewInternalPath).ToList();
                recipientArena.SourceRpf = recipientArena.InternalPaths.FirstOrDefault() ?? recipientArena.SourceRpf;
                SaveComponentMap(request.RecipientPatchRoot, recipientMap);
                Console.WriteLine($"[ArenaTransfer] component_map.json обновлён: arena.InternalPaths = [{string.Join(", ", recipientArena.InternalPaths)}]");
            }

            PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);

            var expectedRemovals = new HashSet<string>(
                originalRecipientArenaPaths.Select(NormalizePath),
                StringComparer.OrdinalIgnoreCase);
            var expectedAdditions = new HashSet<string>(
                newPaths.Select(np => NormalizePath(np.NewInternalPath)),
                StringComparer.OrdinalIgnoreCase);
            var sanityIssues = AuditManifestDiff(
                preSnapshot,
                manifest,
                expectedRemovals,
                expectedAdditions);

            string sanityNote = "";
            if (sanityIssues.Count > 0)
            {
                Audit($"SANITY ISSUES ({sanityIssues.Count}):");
                foreach (var issue in sanityIssues)
                    Audit($"  ⚠ {issue}");
                sanityNote = "\nВНИМАНИЕ - пост-чек арены поймал подозрительные изменения "
                           + $"({sanityIssues.Count}):\n  • " + string.Join("\n  • ", sanityIssues.Take(5));
                if (sanityIssues.Count > 5)
                    sanityNote += $"\n  … и ещё {sanityIssues.Count - 5}";
                sanityNote += $"\nПолный отчёт: {AuditLogPath}";
            }
            else
            {
                Audit($"sanity: чисто ({preSnapshot.Count} → {manifest.Actions.Count} entries, тронуты только arena + content.xml)");
            }
            Audit($"=== TRANSFER END: success, transferred={transferred.Count}, sanity={(sanityIssues.Count == 0 ? "clean" : sanityIssues.Count + " issues")} ===");

            return new ArenaTransferResult
            {
                Success = true,
                Message = $"Перенесено {transferred.Count} arena-RPF из донора (было {aArena.InternalPaths.Count}, стало {transferred.Count})." + sanityNote,
                TransferredFiles = transferred
            };
        }

        private sealed record ActionSnapshot(string TargetPath, ActionType Type, long Size, string? Sha256);

        private static Dictionary<string, ActionSnapshot> SnapshotManifest(DiffManifest manifest)
        {
            var map = new Dictionary<string, ActionSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in manifest.Actions)
            {
                map[NormalizePath(a.TargetPath)] = new ActionSnapshot(
                    a.TargetPath ?? "", a.Type, a.Size, a.Sha256);
            }
            return map;
        }

        private static List<string> AuditManifestDiff(
            Dictionary<string, ActionSnapshot> before,
            DiffManifest afterManifest,
            HashSet<string> expectedRemovals,
            HashSet<string> expectedAdditions)
        {
            var issues = new List<string>();
            var after = SnapshotManifest(afterManifest);

            var criticalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "minimap.rpf", "scaleform_minimap.rpf",
                "weapons.rpf", "miami_weapon.rpf", "miami_guns_selected.rpf",
                "armor.rpf",
                "common.rpf", "common.rpf/data",
            };
            static bool IsCritical(string path, HashSet<string> critical)
            {
                if (string.IsNullOrEmpty(path)) return false;
                var leaf = Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar));
                return critical.Contains(leaf)
                    || critical.Any(c => path.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (var (key, snap) in before)
            {
                if (after.ContainsKey(key)) continue;
                if (expectedRemovals.Contains(key)) continue;
                string tag = IsCritical(snap.TargetPath, criticalNames) ? " [CRITICAL]" : "";
                issues.Add($"DROPPED{tag}: {snap.TargetPath} (Type={snap.Type}, Size={snap.Size})");
            }

            foreach (var (key, snap) in after)
            {
                if (before.ContainsKey(key)) continue;
                if (expectedAdditions.Contains(key)) continue;
                if (NormalizePath(snap.TargetPath) == "content.xml") continue;
                issues.Add($"ADDED unexpectedly: {snap.TargetPath} (Type={snap.Type}, Size={snap.Size})");
            }

            foreach (var (key, beforeSnap) in before)
            {
                if (!after.TryGetValue(key, out var afterSnap)) continue;
                if (NormalizePath(beforeSnap.TargetPath) == "content.xml") continue;
                if (expectedRemovals.Contains(key) || expectedAdditions.Contains(key)) continue;

                bool sizeChanged = beforeSnap.Size != afterSnap.Size;
                bool shaChanged  = !string.Equals(beforeSnap.Sha256 ?? "", afterSnap.Sha256 ?? "", StringComparison.OrdinalIgnoreCase);
                if (!sizeChanged && !shaChanged) continue;

                string tag = IsCritical(beforeSnap.TargetPath, criticalNames) ? " [CRITICAL]" : "";
                if (afterSnap.Size == 0 && beforeSnap.Size > 0)
                {
                    issues.Add($"ZEROED{tag}: {beforeSnap.TargetPath} (was {beforeSnap.Size}b → 0b)");
                }
                else
                {
                    issues.Add($"MUTATED{tag}: {beforeSnap.TargetPath} "
                             + $"(Size {beforeSnap.Size}→{afterSnap.Size}, "
                             + $"Sha {Short(beforeSnap.Sha256)}→{Short(afterSnap.Sha256)})");
                }
            }

            return issues;
        }

        private static string Short(string? sha) =>
            string.IsNullOrEmpty(sha) ? "-" : sha.Substring(0, Math.Min(8, sha.Length));

        private static void UpdateContentXml(
            string aPatchFiles,
            List<string> oldArenaInternalPaths,
            List<(string DonorInternalPath, string NewInternalPath, string NewFileName)> newPaths,
            DiffManifest manifest)
        {
            string contentXmlPath = Path.Combine(aPatchFiles, "content.xml");
            if (!File.Exists(contentXmlPath))
            {
                Console.WriteLine($"[ArenaTransfer] WARN: content.xml не найден в {aPatchFiles} - пропуск правки XML.");
                return;
            }

            XDocument doc = ArmorTransferService.LoadXmlTolerant(contentXmlPath);

            var oldFilenames = new HashSet<string>(
                oldArenaInternalPaths.Select(p => "update:/" + NormalizePath(p)),
                StringComparer.OrdinalIgnoreCase);

            var dataFilesRoot = doc.Root?.Element("dataFiles");
            XElement? templateDataFileItem = null;
            int removedFromDataFiles = 0;
            if (dataFilesRoot != null)
            {
                var toRemove = dataFilesRoot.Elements("Item")
                    .Where(item =>
                    {
                        var fn = item.Element("filename")?.Value?.Trim() ?? "";
                        return oldFilenames.Contains(fn);
                    })
                    .ToList();
                if (toRemove.Count > 0)
                    templateDataFileItem = new XElement(toRemove[0]);
                foreach (var item in toRemove) { item.Remove(); removedFromDataFiles++; }
                Console.WriteLine($"[ArenaTransfer] content.xml <dataFiles>: удалено {removedFromDataFiles} старых arena-Item");
            }

            int removedFromEnable = 0;
            foreach (var fte in doc.Descendants("filesToEnable"))
            {
                var toRemove = fte.Elements("Item")
                    .Where(item => oldFilenames.Contains(item.Value?.Trim() ?? ""))
                    .ToList();
                foreach (var item in toRemove) { item.Remove(); removedFromEnable++; }
            }
            Console.WriteLine($"[ArenaTransfer] content.xml <filesToEnable>: удалено {removedFromEnable} старых arena-Item");

            if (templateDataFileItem == null)
            {
                templateDataFileItem = new XElement("Item",
                    new XElement("filename", "PLACEHOLDER"),
                    new XElement("fileType", "RPF_FILE"),
                    new XElement("locked",     new XAttribute("value", "true")),
                    new XElement("disabled",   new XAttribute("value", "true")),
                    new XElement("persistent", new XAttribute("value", "true")),
                    new XElement("overlay",    new XAttribute("value", "true"))
                );
                Console.WriteLine("[ArenaTransfer] content.xml <dataFiles>: шаблон не найден, используем дефолтный RPF_FILE-шаблон.");
            }

            if (dataFilesRoot == null)
            {
                dataFilesRoot = new XElement("dataFiles");
                doc.Root!.Add(dataFilesRoot);
            }
            foreach (var np in newPaths)
            {
                var newItem = new XElement(templateDataFileItem);
                string newFilename = "update:/" + NormalizePath(np.NewInternalPath);
                var fn = newItem.Element("filename");
                if (fn != null) fn.Value = newFilename;
                else newItem.AddFirst(new XElement("filename", newFilename));
                dataFilesRoot.Add(newItem);
            }
            Console.WriteLine($"[ArenaTransfer] content.xml <dataFiles>: добавлено {newPaths.Count} новых Item");

            var streamingChangeSet = doc.Descendants("contentChangeSets")
                .Elements("Item")
                .FirstOrDefault(cs => (cs.Element("changeSetName")?.Value?.Trim() ?? "")
                    .Equals("CCS_TITLE_UPDATE_STREAMING", StringComparison.OrdinalIgnoreCase));

            if (streamingChangeSet != null)
            {
                var fte = streamingChangeSet.Element("filesToEnable");
                if (fte == null)
                {
                    fte = new XElement("filesToEnable");
                    streamingChangeSet.Add(fte);
                }
                foreach (var np in newPaths)
                {
                    fte.Add(new XElement("Item", "update:/" + NormalizePath(np.NewInternalPath)));
                }
                Console.WriteLine($"[ArenaTransfer] content.xml STREAMING/filesToEnable: добавлено {newPaths.Count} новых Item");
            }
            else
            {
                Console.WriteLine("[ArenaTransfer] WARN: не найден CCS_TITLE_UPDATE_STREAMING - новые файлы не добавлены в filesToEnable.");
            }

            var xmlSettings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "    ",
                OmitXmlDeclaration = false
            };
            using (var writer = XmlWriter.Create(contentXmlPath, xmlSettings))
                doc.Save(writer);
            byte[] xmlBytes = File.ReadAllBytes(contentXmlPath);
            string xmlSha = ComputeSha256(xmlBytes);
            var xmlAction = manifest.Actions.FirstOrDefault(a => NormalizePath(a.TargetPath) == "content.xml");
            if (xmlAction != null)
            {
                xmlAction.Size = xmlBytes.LongLength;
                xmlAction.Sha256 = xmlSha;
            }
            else
            {
                manifest.Actions.Add(new PatchAction
                {
                    Type = ActionType.Replace,
                    TargetPath = "content.xml",
                    SourcePath = "patch_files/content.xml",
                    Size = xmlBytes.LongLength,
                    Sha256 = xmlSha,
                    IsWholeReplaceNestedRpf = false
                });
            }
            Console.WriteLine($"[ArenaTransfer] content.xml сохранён ({xmlBytes.Length} байт, sha256={xmlSha.Substring(0, 12)}…)");
        }

        private static ArenaTransferResult Fail(string message) => new()
        {
            Success = false,
            Message = message
        };

        private static string NormalizePath(string p) =>
            (p ?? "").Replace('\\', '/').TrimStart('/').ToLowerInvariant();

        private static string GetDirectory(string internalPath)
        {
            string norm = NormalizePath(internalPath);
            int last = norm.LastIndexOf('/');
            return last < 0 ? "" : norm.Substring(0, last);
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        private static ResolvedComponentMap? LoadComponentMap(string reduxRoot)
        {
            string path = Path.Combine(reduxRoot, "component_map.json");
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<ResolvedComponentMap>(File.ReadAllText(path), JsonOpts);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArenaTransfer] Ошибка чтения component_map.json ({reduxRoot}): {ex.Message}");
                return null;
            }
        }

        private static void SaveComponentMap(string reduxRoot, ResolvedComponentMap map)
        {
            string path = Path.Combine(reduxRoot, "component_map.json");
            File.WriteAllText(path, JsonSerializer.Serialize(map, JsonOpts));
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var o = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }
    }
}
