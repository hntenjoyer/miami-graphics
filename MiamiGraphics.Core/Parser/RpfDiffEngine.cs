using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Parser
{
    public class RpfDiffEngine
    {
        public void CompareDirectories(IArchiveDirectory cleanDir, IArchiveDirectory moddedDir, string currentPath, string patchDir, List<PatchAction> actions)
        {
            var cleanFiles = cleanDir?.GetFiles().ToDictionary(f => f.Name.ToLower()) ?? new Dictionary<string, IArchiveFile>();
            var moddedFiles = moddedDir?.GetFiles().ToDictionary(f => f.Name.ToLower()) ?? new Dictionary<string, IArchiveFile>();

            foreach (var moddedKvp in moddedFiles)
            {
                string fileName = moddedKvp.Key;
                IArchiveFile moddedFile = moddedKvp.Value;
                string fullPath = currentPath + fileName;
                bool isNestedRpf = fileName.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);

                if (!cleanFiles.ContainsKey(fileName))
                {

                    byte[] moddedBytes = GetRealFileBytes(moddedFile);
                    string hash = CalculateSha256(moddedBytes);
                    ExportRealBytesToDisk(moddedBytes, fullPath, patchDir);

                    actions.Add(new PatchAction
                    {
                        Type = ActionType.Import,
                        TargetPath = fullPath,
                        SourcePath = $"patch_files/{fullPath}",
                        Size = moddedBytes.Length,
                        Sha256 = hash,
                        IsWholeReplaceNestedRpf = isNestedRpf
                    });
                }
                else
                {

                    IArchiveFile cleanFile = cleanFiles[fileName];

                    if (IsFileModified(cleanFile, moddedFile, out byte[] moddedBytes))
                    {
                        if (isNestedRpf)
                        {
                            bool success = false;
                            try
                            {

                                using (var cleanStream = ((IArchiveBinaryFile)cleanFile).GetStream())
                                using (var moddedStream = ((IArchiveBinaryFile)moddedFile).GetStream())
                                using (var cleanArc = RageArchiveWrapper7.Open(cleanStream, fileName, true))
                                using (var moddedArc = RageArchiveWrapper7.Open(moddedStream, fileName, true))
                                {
                                    CompareDirectories(cleanArc.Root, moddedArc.Root, fullPath + "/", patchDir, actions);
                                    success = true;
                                }
                            }
                            catch (Exception)
                            {

                                success = false;
                            }

                            if (!success)
                            {
                                string hash = CalculateSha256(moddedBytes);
                                ExportRealBytesToDisk(moddedBytes, fullPath, patchDir);

                                actions.Add(new PatchAction
                                {
                                    Type = ActionType.Replace,
                                    TargetPath = fullPath,
                                    SourcePath = $"patch_files/{fullPath}",
                                    Size = moddedBytes.Length,
                                    Sha256 = hash,
                                    IsWholeReplaceNestedRpf = true
                                });
                            }
                        }
                        else
                        {

                            string hash = CalculateSha256(moddedBytes);
                            ExportRealBytesToDisk(moddedBytes, fullPath, patchDir);

                            actions.Add(new PatchAction
                            {
                                Type = ActionType.Replace,
                                TargetPath = fullPath,
                                SourcePath = $"patch_files/{fullPath}",
                                Size = moddedBytes.Length,
                                Sha256 = hash,
                                IsWholeReplaceNestedRpf = false
                            });
                        }
                    }
                }
            }

            foreach (var cleanKvp in cleanFiles)
            {
                if (!moddedFiles.ContainsKey(cleanKvp.Key))
                {
                    actions.Add(new PatchAction
                    {
                        Type = ActionType.Delete,
                        TargetPath = currentPath + cleanKvp.Key,
                        Size = 0,
                        Sha256 = ""
                    });
                }
            }

            var cleanSubDirs = cleanDir?.GetDirectories().ToDictionary(d => d.Name.ToLower()) ?? new Dictionary<string, IArchiveDirectory>();
            var moddedSubDirs = moddedDir?.GetDirectories().ToDictionary(d => d.Name.ToLower()) ?? new Dictionary<string, IArchiveDirectory>();

            foreach (var moddedSubDir in moddedSubDirs)
            {
                cleanSubDirs.TryGetValue(moddedSubDir.Key, out IArchiveDirectory matchedCleanDir);
                CompareDirectories(matchedCleanDir, moddedSubDir.Value, currentPath + moddedSubDir.Key + "/", patchDir, actions);
            }
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
                        var key = GTA5Constants.PC_NG_KEYS != null && GTA5Constants.PC_NG_KEYS.Length > keyIdx
                            ? GTA5Constants.PC_NG_KEYS[keyIdx]
                            : null;
                        if (key != null && key.Length > 0)
                        {
                            buf = GTA5Crypto.Decrypt(buf, key);
                        }
                        else
                        {

                            Console.WriteLine($"[RpfDiffEngine] WARN: no NG key for {binFile.Name} (idx={keyIdx})");
                        }
                    }

                    if (binFile.IsCompressed)
                    {
                        using (var def = new DeflateStream(new MemoryStream(buf), CompressionMode.Decompress))
                        using (var outMs = new MemoryStream())
                        {
                            def.CopyTo(outMs);
                            return outMs.ToArray();
                        }
                    }
                    return buf;
                }
            }
            else
            {

                using (var ms = new MemoryStream())
                {
                    file.Export(ms);
                    byte[] data = ms.ToArray();

                    NormalizeRsc7Magic(data);

                    return data;
                }
            }
        }

        private static void NormalizeRsc7Magic(byte[] data)
        {
            if (data == null || data.Length < 4)
                return;

            if (data[0] == 0x52 && data[1] == 0x53 && data[2] == 0x43 && data[3] == 0x07)
            {
                data[3] = 0x37;
            }
        }

        private bool IsFileModified(IArchiveFile clean, IArchiveFile modded, out byte[] moddedBytes)
        {
            moddedBytes = GetRealFileBytes(modded);
            byte[] cleanBytes = GetRealFileBytes(clean);

            if (cleanBytes.Length != moddedBytes.Length) return true;

            string cleanHash = CalculateSha256(cleanBytes);
            string modHash = CalculateSha256(moddedBytes);

            return cleanHash != modHash;
        }

        private void ExportRealBytesToDisk(byte[] data, string targetPath, string patchDir)
        {
            string destPath = Path.Combine(patchDir, targetPath.Replace("/", "\\"));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
            File.WriteAllBytes(destPath, data);
        }

        private string CalculateSha256(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}