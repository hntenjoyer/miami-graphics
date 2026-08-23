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
    public sealed class ArmorTransferRequest
    {
        public string RecipientPatchRoot { get; init; } = "";
        public DiffManifest RecipientManifest { get; init; } = default!;
        public string DonorPatchRoot { get; init; } = "";
    }

    public sealed class ArmorTransferResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public IReadOnlyList<string> TransferredFiles { get; init; } = Array.Empty<string>();
    }

    public sealed class ArmorTransferService
    {
        private static readonly JsonSerializerOptions JsonOpts = CreateJsonOptions();

        public ArmorTransferResult Transfer(ArmorTransferRequest request)
        {

            ResolvedComponentMap? recipientMap = LoadComponentMap(request.RecipientPatchRoot);
            ResolvedComponentMap? donorMap = LoadComponentMap(request.DonorPatchRoot);

            if (donorMap == null ||
                !donorMap.Components.TryGetValue("armor", out ComponentInfo? bArmor) ||
                bArmor == null || !bArmor.IsFound || bArmor.InternalPaths.Count == 0)
                return Fail(Loc.T("error.donorNoArmorRegistered"));

            ComponentInfo? aArmor = null;
            recipientMap?.Components.TryGetValue("armor", out aArmor);
            bool recipientHasArmor = aArmor != null && aArmor.IsFound && aArmor.InternalPaths.Count > 0;

            string aPatchFiles = Path.Combine(request.RecipientPatchRoot, "patch_files");

            string bComponentDir = Path.Combine(request.DonorPatchRoot, "components", "armor");
            string bPatchFiles   = Path.Combine(request.DonorPatchRoot, "patch_files");
            string bRoot         = Directory.Exists(bComponentDir) ? bComponentDir : bPatchFiles;

            Console.WriteLine($"[ArmorTransfer] A has armor: {recipientHasArmor}");
            if (recipientHasArmor)
                Console.WriteLine($"[ArmorTransfer] A: {string.Join(", ", aArmor!.InternalPaths)}");
            Console.WriteLine($"[ArmorTransfer] B: {string.Join(", ", bArmor.InternalPaths)}");
            Console.WriteLine($"[ArmorTransfer] B-root: {bRoot}");

            foreach (string bPath in bArmor.InternalPaths)
            {
                string abs = Path.Combine(bRoot, bPath.Replace('/', '\\'));
                if (!File.Exists(abs))
                    return Fail(Loc.T("error.donorArmorFileMissing", ("path", abs)));
            }

            string basePrefix = recipientHasArmor
                ? GetDirectory(aArmor!.InternalPaths[0])
                : "x64/levels/gta5/_citye";

            var newPaths = new List<(string DonorInternalPath, string NewInternalPath, string NewFileName)>();
            foreach (string bPath in bArmor.InternalPaths)
            {
                string fileName = Path.GetFileName(bPath);
                string newInternal = string.IsNullOrEmpty(basePrefix) ? fileName : $"{basePrefix}/{fileName}";
                newPaths.Add((bPath, newInternal, fileName));
            }

            Console.WriteLine($"[ArmorTransfer] basePrefix = '{basePrefix}'");
            foreach (var np in newPaths)
                Console.WriteLine($"[ArmorTransfer]   B:{np.DonorInternalPath} → A:{np.NewInternalPath}");

            var oldSet = recipientHasArmor
                ? new HashSet<string>(aArmor!.InternalPaths.Select(NormalizePath), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            if (recipientHasArmor)
            {
                foreach (string aPath in aArmor!.InternalPaths)
                {
                    string oldAbs = Path.Combine(aPatchFiles, aPath.Replace('/', '\\'));
                    if (File.Exists(oldAbs))
                    {
                        File.Delete(oldAbs);
                        Console.WriteLine($"[ArmorTransfer] Удалён старый файл: {aPath}");
                    }

                    int removed = manifest.Actions.RemoveAll(a =>
                        NormalizePath(a.TargetPath) == NormalizePath(aPath));
                    if (removed > 0)
                        Console.WriteLine($"[ArmorTransfer] Из manifest удалено действий: {removed} (для {aPath})");
                }
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
                Console.WriteLine($"[ArmorTransfer] Скопирован: {np.DonorInternalPath} → {np.NewInternalPath} ({bytes.Length} байт)");
            }

            UpdateContentXml(
                aPatchFiles,
                recipientHasArmor ? aArmor!.InternalPaths : new List<string>(),
                newPaths,
                manifest);

            if (recipientMap is null)
            {

                recipientMap = new ResolvedComponentMap();
            }
            recipientMap.Components["armor"] = new ComponentInfo
            {
                IsFound = true,
                SourceRpf = newPaths[0].NewInternalPath,
                InternalPaths = newPaths.Select(np => np.NewInternalPath).ToList(),
                Flags = new List<string> { "transferable", "clearable" }
            };
            SaveComponentMap(request.RecipientPatchRoot, recipientMap);
            Console.WriteLine($"[ArmorTransfer] component_map.json обновлён: armor.InternalPaths = [{string.Join(", ", recipientMap.Components["armor"].InternalPaths)}]");

            PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);

            return new ArmorTransferResult
            {
                Success = true,
                Message = recipientHasArmor
                    ? Loc.T("misc.armorRpfReplaced", ("count", transferred.Count), ("before", aArmor!.InternalPaths.Count))
                    : Loc.T("misc.armorRpfAdded", ("count", transferred.Count)),
                TransferredFiles = transferred
            };
        }

        public static XDocument LoadXmlTolerant(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            global::System.Text.Encoding enc;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) enc = global::System.Text.Encoding.Unicode;
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) enc = global::System.Text.Encoding.BigEndianUnicode;
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) enc = new global::System.Text.UTF8Encoding(false);
            else
            {
                int nulls = 0, sample = global::System.Math.Min(bytes.Length, 512);
                for (int i = 0; i < sample; i++) if (bytes[i] == 0) nulls++;
                enc = nulls > sample / 4 ? global::System.Text.Encoding.Unicode : new global::System.Text.UTF8Encoding(false);
            }
            string text = enc.GetString(bytes);
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            return XDocument.Parse(text);
        }

        private static void UpdateContentXml(
            string aPatchFiles,
            List<string> oldArmorInternalPaths,
            List<(string DonorInternalPath, string NewInternalPath, string NewFileName)> newPaths,
            DiffManifest manifest)
        {
            string contentXmlPath = Path.Combine(aPatchFiles, "content.xml");
            if (!File.Exists(contentXmlPath))
            {
                Console.WriteLine($"[ArmorTransfer] WARN: content.xml не найден в {aPatchFiles} - пропуск правки XML.");
                return;
            }

            XDocument doc = LoadXmlTolerant(contentXmlPath);

            var oldFilenames = new HashSet<string>(
                oldArmorInternalPaths.Select(p => "update:/" + NormalizePath(p)),
                StringComparer.OrdinalIgnoreCase);

            var dataFilesRoot = doc.Root?.Element("dataFiles");
            XElement? templateDataFileItem = null;
            int removedFromDataFiles = 0;
            if (dataFilesRoot != null)
            {
                if (oldFilenames.Count > 0)
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
                }

                if (templateDataFileItem == null)
                {
                    var anyRpfItem = dataFilesRoot.Elements("Item")
                        .FirstOrDefault(item =>
                        {
                            var ft = item.Element("fileType")?.Value?.Trim() ?? "";
                            return ft.Equals("RPF_FILE", StringComparison.OrdinalIgnoreCase);
                        });
                    if (anyRpfItem != null)
                        templateDataFileItem = new XElement(anyRpfItem);
                }

                Console.WriteLine($"[ArmorTransfer] content.xml <dataFiles>: удалено {removedFromDataFiles} старых armor-Item");
            }

            int removedFromEnable = 0;
            if (oldFilenames.Count > 0)
            {
                foreach (var fte in doc.Descendants("filesToEnable"))
                {
                    var toRemove = fte.Elements("Item")
                        .Where(item => oldFilenames.Contains(item.Value?.Trim() ?? ""))
                        .ToList();
                    foreach (var item in toRemove) { item.Remove(); removedFromEnable++; }
                }
            }
            Console.WriteLine($"[ArmorTransfer] content.xml <filesToEnable>: удалено {removedFromEnable} старых armor-Item");

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
                Console.WriteLine("[ArmorTransfer] content.xml <dataFiles>: шаблон не найден, используем дефолтный RPF_FILE-шаблон.");
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
            Console.WriteLine($"[ArmorTransfer] content.xml <dataFiles>: добавлено {newPaths.Count} новых Item");

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
                    fte.Add(new XElement("Item", "update:/" + NormalizePath(np.NewInternalPath)));
                Console.WriteLine($"[ArmorTransfer] content.xml STREAMING/filesToEnable: добавлено {newPaths.Count} новых Item");
            }
            else
            {
                Console.WriteLine("[ArmorTransfer] WARN: не найден CCS_TITLE_UPDATE_STREAMING - новые файлы не добавлены в filesToEnable.");
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
            Console.WriteLine($"[ArmorTransfer] content.xml сохранён ({xmlBytes.Length} байт, sha256={xmlSha.Substring(0, 12)}…)");
        }

        private static ArmorTransferResult Fail(string message) => new()
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
                Console.WriteLine($"[ArmorTransfer] Ошибка чтения component_map.json ({reduxRoot}): {ex.Message}");
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
