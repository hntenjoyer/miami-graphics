using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Services
{
    public static class CarLogosService
    {
        public const string FileName       = "miami_cars.rpf";
        public const string FolderName     = "car_logos";
        public const string EnableItem     = "update:/car_logos/miami_cars.rpf";
        public const string TargetChangeSet = "CCS_TITLE_UPDATE_STREAMING";
        private const string RootName      = "CDataFileMgr__ContentsOfDataFileXml";
        private const string PathSuffix    = "/car_logos/miami_cars.rpf";

        public static readonly IReadOnlyList<string> DefaultSignature = new[]
        {
            "majestic_veh_brand_alfaromeo.ytd", "majestic_veh_brand_audi.ytd",
            "majestic_veh_brand_bmw.ytd",       "majestic_veh_brand_bugatti.ytd",
            "majestic_veh_brand_chevrolet.ytd", "majestic_veh_brand_dodge.ytd",
            "majestic_veh_brand_ferrari.ytd",   "majestic_veh_brand_ford.ytd",
            "majestic_veh_brand_lamborghini.ytd", "majestic_veh_brand_lexus.ytd",
            "majestic_veh_brand_mercedes.ytd",  "majestic_veh_brand_porsche.ytd",
            "majestic_vehshare.ytd",
        };

        public static bool Apply(string updateRpfPath, byte[] packBytes)
        {
            if (packBytes is null || packBytes.Length == 0)
                throw new ArgumentException("miami_cars.rpf bytes empty", nameof(packBytes));
            return Mutate(updateRpfPath, enable: true, packBytes);
        }

        public static bool Remove(string updateRpfPath)
            => Mutate(updateRpfPath, enable: false, packBytes: null);

        public static bool IsApplied(string updateRpfPath)
        {
            try
            {
                using var arc = RageArchiveWrapper7.Open(updateRpfPath);
                var contentEntry = arc.Root.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
                if (contentEntry is null) return false;
                var doc = XDocument.Parse(DecodeText(ReadEntryDecoded(contentEntry)));
                bool inData = DataFilesHasEntry(doc);
                bool fileThere = FindPackDir(arc.Root, createMissing: false)?
                    .GetFiles().Any(f => f.Name.Equals(FileName, StringComparison.OrdinalIgnoreCase)) ?? false;
                return inData && fileThere;
            }
            catch { return false; }
        }

        public static IReadOnlyList<string> SignatureFromPack(byte[] packBytes)
        {
            var names = new List<string>();
            try
            {
                using var ms = new MemoryStream(packBytes);
                using var arc = RageArchiveWrapper7.Open(ms, FileName, leaveOpen: true);
                foreach (var f in arc.Root.GetFiles())
                {
                    var n = f.Name ?? "";
                    if (n.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)) names.Add(n);
                }
            }
            catch {}
            return names;
        }

        public sealed record DetectResult(bool Found, IReadOnlyList<string> Paths, IReadOnlyList<string> Hits);

        private const long MaxInspectBytes = 128L * 1024 * 1024;
        private const int MaxWalkDepth = 6;

        public static DetectResult DetectExisting(string updateRpfPath, IReadOnlyCollection<string>? signature = null)
        {
            var sig = new HashSet<string>(
                (signature is { Count: > 0 } ? signature : DefaultSignature),
                StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            var hits = new List<string>();
            try
            {
                if (!File.Exists(updateRpfPath)) return new DetectResult(false, paths, hits);
                using var arc = RageArchiveWrapper7.Open(updateRpfPath);
                WalkRpfEntries(arc.Root, "update:/", 0, (path, bin) =>
                {
                    if (IsOwnPath(path)) return;
                    if (bin.Size > MaxInspectBytes) return;
                    foreach (var n in CollectLeafNames(bin))
                    {
                        if (!sig.Contains(n)) continue;
                        paths.Add(path);
                        if (!hits.Contains(n, StringComparer.OrdinalIgnoreCase)) hits.Add(n);
                        break;
                    }
                });
            }
            catch {}
            return new DetectResult(paths.Count > 0, paths, hits);
        }

        private static bool IsOwnPath(string path)
            => path.Replace('\\', '/').EndsWith(PathSuffix, StringComparison.OrdinalIgnoreCase);

        private static void WalkRpfEntries(
            IArchiveDirectory dir, string prefix, int depth, Action<string, IArchiveBinaryFile> action)
        {
            if (depth > MaxWalkDepth) return;
            foreach (var f in dir.GetFiles())
            {
                if (f.Name != null && f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)
                    && f is IArchiveBinaryFile bin)
                {
                    try { action(prefix + f.Name, bin); } catch { }
                }
            }
            foreach (var d in dir.GetDirectories())
                WalkRpfEntries(d, prefix + d.Name + "/", depth + 1, action);
        }

        private static List<string> CollectLeafNames(IArchiveBinaryFile rpfEntry)
        {
            var names = new List<string>();
            try
            {
                using var ms = new MemoryStream();
                rpfEntry.Export(ms);
                ms.Position = 0;
                using var nested = RageArchiveWrapper7.Open(ms, rpfEntry.Name, leaveOpen: true);
                WalkNames(nested.Root, names, 0);
            }
            catch { }
            return names;
        }

        private static void WalkNames(IArchiveDirectory dir, List<string> acc, int depth)
        {
            if (depth > 3 || acc.Count > 8000) return;
            foreach (var f in dir.GetFiles()) acc.Add(f.Name);
            foreach (var d in dir.GetDirectories()) WalkNames(d, acc, depth + 1);
        }

        private static bool Mutate(string updateRpfPath, bool enable, byte[]? packBytes)
        {
            if (!File.Exists(updateRpfPath))
                throw new FileNotFoundException("update.rpf not found", updateRpfPath);

            using var arc = RageArchiveWrapper7.Open(updateRpfPath);

            var contentEntry = arc.Root.GetFiles()
                .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
            if (contentEntry is null)
                throw new InvalidOperationException("content.xml not found at update.rpf root.");

            var doc = XDocument.Parse(DecodeText(ReadEntryDecoded(contentEntry)));
            if (enable) AddEntries(doc); else RemoveEntriesBySuffix(doc, PathSuffix);
            if (contentEntry is IArchiveBinaryFile xmlBin)
            {
                var outXml = SerializeXml(doc);
                xmlBin.Import(new MemoryStream(outXml));
                xmlBin.IsCompressed = false;
                xmlBin.IsEncrypted = false;
                xmlBin.UncompressedSize = (uint)outXml.Length;
            }

            var dir = FindPackDir(arc.Root, createMissing: enable);
            if (dir is null)
            {
                if (enable) throw new InvalidOperationException("car_logos directory could not be created in update.rpf.");
            }
            else
            {
                var existing = dir.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(FileName, StringComparison.OrdinalIgnoreCase));
                if (enable)
                {
                    if (existing is IArchiveBinaryFile eb)
                    {
                        eb.Import(new MemoryStream(packBytes!));
                        eb.IsCompressed = false;
                        eb.IsEncrypted = false;
                        eb.UncompressedSize = (uint)packBytes!.Length;
                    }
                    else
                    {
                        var nf = dir.CreateBinaryFile();
                        nf.Name = FileName;
                        nf.Import(new MemoryStream(packBytes!));
                        nf.IsCompressed = false;
                        nf.IsEncrypted = false;
                        nf.UncompressedSize = (uint)packBytes!.Length;
                    }
                }
                else if (existing is not null)
                {
                    try { dir.DeleteFile(existing); }
                    catch {}
                }
            }

            arc.Flush();
            return true;
        }

        private static bool DataFilesHasEntry(XDocument doc)
        {
            var dataFiles = doc.Root?.Element("dataFiles");
            return dataFiles?.Elements("Item").Any(it =>
                (it.Element("filename")?.Value ?? "").Trim()
                    .EndsWith(PathSuffix, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        private static bool AddEntries(XDocument doc)
        {
            var root = doc.Root ?? throw new InvalidOperationException("content.xml has no root.");
            bool changed = false;

            var dataFiles = root.Element("dataFiles");
            if (dataFiles is null)
            {
                dataFiles = new XElement("dataFiles");
                root.AddFirst(dataFiles);
                changed = true;
            }
            if (!DataFilesHasEntry(doc))
            {
                dataFiles.Add(new XElement("Item",
                    new XElement("filename", EnableItem),
                    new XElement("fileType", "RPF_FILE"),
                    new XElement("locked",     new XAttribute("value", "true")),
                    new XElement("disabled",   new XAttribute("value", "true")),
                    new XElement("persistent", new XAttribute("value", "true")),
                    new XElement("overlay",    new XAttribute("value", "true"))));
                changed = true;
            }

            var enable = FindTargetFilesToEnable(root, createMissing: true);
            if (enable is not null &&
                !enable.Elements("Item").Any(i => i.Value.Trim()
                    .EndsWith(PathSuffix, StringComparison.OrdinalIgnoreCase)))
            {
                enable.Add(new XElement("Item", EnableItem));
                changed = true;
            }
            return changed;
        }

        private static bool RemoveEntriesBySuffix(XDocument doc, string suffix)
        {
            var root = doc.Root;
            if (root is null) return false;
            bool changed = false;

            foreach (var it in root.Element("dataFiles")?.Elements("Item").ToList() ?? new())
            {
                if ((it.Element("filename")?.Value ?? "").Trim()
                    .EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                { it.Remove(); changed = true; }
            }
            foreach (var fte in root.Descendants("filesToEnable").ToList())
            {
                foreach (var i in fte.Elements("Item").ToList())
                {
                    if (i.Value.Trim().EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    { i.Remove(); changed = true; }
                }
            }
            return changed;
        }

        private static XElement? FindTargetFilesToEnable(XElement root, bool createMissing)
        {
            var changeSets = root.Element("contentChangeSets");
            if (changeSets is null)
            {
                if (!createMissing) return null;
                changeSets = new XElement("contentChangeSets");
                root.Add(changeSets);
            }

            var items = changeSets.Elements("Item").ToList();

            var named = items.FirstOrDefault(i =>
                string.Equals(i.Element("changeSetName")?.Value?.Trim(), TargetChangeSet, StringComparison.OrdinalIgnoreCase));
            if (named is not null)
                return EnsureFilesToEnable(named);

            XElement? best = null; int bestCount = -1;
            foreach (var it in items)
            {
                if (it.Element("executionConditions") is not null) continue;
                var fte = it.Element("filesToEnable");
                int c = fte?.Elements("Item").Count() ?? 0;
                if (c > bestCount) { bestCount = c; best = it; }
            }
            if (best is not null)
                return EnsureFilesToEnable(best);

            if (!createMissing) return null;
            var created = new XElement("Item",
                new XElement("changeSetName", TargetChangeSet),
                new XElement("filesToEnable"));
            changeSets.Add(created);
            return created.Element("filesToEnable");
        }

        private static XElement EnsureFilesToEnable(XElement changeSetItem)
        {
            var fte = changeSetItem.Element("filesToEnable");
            if (fte is null)
            {
                fte = new XElement("filesToEnable");
                changeSetItem.Add(fte);
            }
            return fte;
        }

        private static IArchiveDirectory? FindPackDir(IArchiveDirectory root, bool createMissing)
        {
            var sub = root.GetDirectories()
                .FirstOrDefault(d => d.Name.Equals(FolderName, StringComparison.OrdinalIgnoreCase));
            if (sub is not null) return sub;
            if (!createMissing) return null;
            var nd = root.CreateDirectory();
            nd.Name = FolderName;
            return nd;
        }

        private static byte[] ReadEntryDecoded(IArchiveFile file)
        {
            if (file is IArchiveBinaryFile bin)
            {
                using var ms = new MemoryStream();
                bin.Export(ms);
                byte[] buf = ms.ToArray();

                if (bin.IsEncrypted)
                {
                    var hash = GTA5Hash.CalculateHash(bin.Name);
                    var keyIdx = (hash + (uint)bin.UncompressedSize + (101 - 40)) % 0x65;
                    var key = GTA5Constants.PC_NG_KEYS?[keyIdx];
                    if (key != null && key.Length > 0)
                        buf = GTA5Crypto.Decrypt(buf, key);
                }
                if (bin.IsCompressed)
                {
                    using var def = new global::System.IO.Compression.DeflateStream(
                        new MemoryStream(buf), global::System.IO.Compression.CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    def.CopyTo(outMs);
                    return outMs.ToArray();
                }
                return buf;
            }
            using (var ms = new MemoryStream()) { file.Export(ms); return ms.ToArray(); }
        }

        private static string DecodeText(byte[] bytes)
        {
            var txt = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true).ReadToEnd();
            if (!txt.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith("<"))
                txt = Encoding.Unicode.GetString(bytes);

            int lt = txt.IndexOf('<');
            if (lt > 0) txt = txt.Substring(lt);
            const string rootClose = "</" + RootName + ">";
            int rc = txt.IndexOf(rootClose, StringComparison.OrdinalIgnoreCase);
            if (rc >= 0)
                txt = txt.Substring(0, rc + rootClose.Length);
            else
            {
                int gt = txt.LastIndexOf('>');
                if (gt >= 0) txt = txt.Substring(0, gt + 1);
            }
            return txt;
        }

        private static byte[] SerializeXml(XDocument doc)
        {
            var sb = new StringBuilder();
            using (var w = global::System.Xml.XmlWriter.Create(sb, new global::System.Xml.XmlWriterSettings
            {
                Indent = true,
                IndentChars = "    ",
                NewLineChars = "\n",
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
            }))
            {
                doc.Save(w);
            }
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }
    }
}
