#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Archives;

namespace MiamiGraphics.Core.Services
{

    public static class SyntheticArmorRpfBuilder
    {
        public sealed class FileEntry
        {
            public string FileName    { get; set; }
            public byte[] FileBytes   { get; set; }

            public bool   IsResource  { get; set; }
        }

        public static bool Build(
            string outPath,
            string subDirName,
            IList<FileEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(subDirName))
                throw new ArgumentException("subDirName required");
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("at least one entry required");

            var grouped = new Dictionary<string, IList<FileEntry>>(StringComparer.OrdinalIgnoreCase)
            {
                [subDirName] = entries,
            };
            return BuildMulti(outPath, grouped);
        }

        public static bool BuildMulti(
            string outPath,
            IDictionary<string, IList<FileEntry>> folders)
        {
            if (string.IsNullOrWhiteSpace(outPath))
                throw new ArgumentException("outPath required");
            if (folders == null || folders.Count == 0)
                throw new ArgumentException("at least one folder required");

            var parent = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            if (File.Exists(outPath))
                File.Delete(outPath);

            using var arc = RageArchiveWrapper7.Create(outPath);
            arc.FileName = Path.GetFileName(outPath);
            arc.archive_.Encryption = RageArchiveEncryption7.None;

            int totalFiles = 0;
            foreach (var kv in folders)
            {
                var subDirName = kv.Key;
                var entries = kv.Value;
                if (string.IsNullOrWhiteSpace(subDirName)) continue;
                if (entries == null || entries.Count == 0) continue;

                var innerDir = arc.Root.CreateDirectory();
                innerDir.Name = subDirName;

                foreach (var e in entries)
                {
                    if (e == null || e.FileBytes == null || e.FileBytes.Length == 0)
                        continue;
                    if (e.IsResource)
                    {
                        var f = innerDir.CreateResourceFile();
                        f.Name = e.FileName;
                        using var ms = new MemoryStream(e.FileBytes);
                        f.Import(ms);
                    }
                    else
                    {
                        var f = innerDir.CreateBinaryFile();
                        f.Name = e.FileName;
                        using var ms = new MemoryStream(e.FileBytes);
                        f.Import(ms);
                    }
                    totalFiles++;
                }
            }

            if (totalFiles == 0)
                throw new ArgumentException("no non-empty entries to pack");

            arc.Flush();
            return true;
        }

        public static bool IsRageResourceByExt(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".ydd" or ".ytd" or ".ydr" or ".ycd" or ".ybd"
                or ".ynd" or ".ynv" or ".ypt" or ".yvr" or ".ywr"
                or ".yld" or ".yfd" or ".ymf" or ".ymt" or ".ymap"
                or ".ybn" or ".yed" or ".ynn" or ".ypd" or ".yft"
                  => true,
                _ => false,
            };
        }
    }
}
