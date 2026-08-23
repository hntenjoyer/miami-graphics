using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Parser
{
    public class ComponentExportService
    {
        public void ExportResolvedComponents(string workDir, string patchDir, IArchiveDirectory moddedRoot, ResolvedComponentMap componentMap)
        {
            string componentsRoot = Path.Combine(workDir, "components");
            Directory.CreateDirectory(componentsRoot);

            foreach (var kv in componentMap.Components)
            {
                string componentName = kv.Key;
                ComponentInfo component = kv.Value;

                if (component == null || !component.IsFound || component.InternalPaths == null || component.InternalPaths.Count == 0)
                    continue;

                string componentDir = Path.Combine(componentsRoot, componentName);
                Directory.CreateDirectory(componentDir);

                foreach (string internalPath in component.InternalPaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!TryCopyFromPatchFiles(patchDir, componentDir, internalPath))
                    {
                        TryExtractFromArchive(moddedRoot, componentDir, internalPath);
                    }
                }

                WriteInfoJson(componentDir, component);
            }
        }

        private bool TryCopyFromPatchFiles(string patchDir, string componentDir, string internalPath)
        {
            string source = Path.Combine(patchDir, internalPath.Replace("/", "\\"));
            if (!File.Exists(source))
                return false;

            string dest = Path.Combine(componentDir, internalPath.Replace("/", "\\"));
            string destDir = Path.GetDirectoryName(dest);

            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(source, dest, true);
            return true;
        }

        private bool TryExtractFromArchive(IArchiveDirectory root, string componentDir, string internalPath)
        {
            try
            {
                string[] parts = internalPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return false;

                if (TryExtractRecursive(root, parts, 0, out byte[] bytes))
                {
                    string dest = Path.Combine(componentDir, internalPath.Replace("/", "\\"));
                    string destDir = Path.GetDirectoryName(dest);

                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    File.WriteAllBytes(dest, bytes);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ComponentExport] Ошибка извлечения {internalPath}: {ex.Message}");
            }

            return false;
        }

        private bool TryExtractRecursive(IArchiveDirectory dir, string[] parts, int index, out byte[] bytes)
        {
            bytes = null;

            if (index >= parts.Length)
                return false;

            string current = parts[index];

            if (index == parts.Length - 1)
            {
                var file = dir.GetFiles().FirstOrDefault(f => f.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
                if (file == null)
                    return false;

                bytes = GetRealFileBytes(file);
                return true;
            }

            var subDir = dir.GetDirectories().FirstOrDefault(d => d.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (subDir != null)
                return TryExtractRecursive(subDir, parts, index + 1, out bytes);

            var nestedRpf = dir.GetFiles().FirstOrDefault(f => f.Name.Equals(current, StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;
            if (nestedRpf != null && current.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                using (var stream = nestedRpf.GetStream())
                using (var nestedArc = RageArchiveWrapper7.Open(stream, nestedRpf.Name, true))
                {
                    return TryExtractRecursive(nestedArc.Root, parts, index + 1, out bytes);
                }
            }

            return false;
        }

        private byte[] GetRealFileBytes(IArchiveFile file)
        {
            if (file is IArchiveBinaryFile binFile)
            {
                using (var ms = new MemoryStream())
                {
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
                            Console.WriteLine($"[ComponentExport] WARN: Нет NG ключа для {binFile.Name} (idx={keyIdx})");
                        }
                    }

                    if (binFile.IsCompressed)
                    {
                        using (var def = new global::System.IO.Compression.DeflateStream(
                            new MemoryStream(buf),
                            global::System.IO.Compression.CompressionMode.Decompress))
                        using (var outMs = new MemoryStream())
                        {
                            def.CopyTo(outMs);
                            return outMs.ToArray();
                        }
                    }

                    return buf;
                }
            }

            using (var ms = new MemoryStream())
            {
                file.Export(ms);
                return ms.ToArray();
            }
        }

        private void WriteInfoJson(string componentDir, ComponentInfo component)
        {
            string path = Path.Combine(componentDir, "component_info.json");

            string json = global::System.Text.Json.JsonSerializer.Serialize(
                component,
                new global::System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(path, json);
        }
    }
}