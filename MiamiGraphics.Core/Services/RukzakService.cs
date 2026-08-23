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
    public static class RukzakService
    {
        public const string FileName        = "miami_rukzak_v2.rpf";
        public const string FolderName      = "rukzak";
        public const string EnableItem      = "update:/rukzak/miami_rukzak_v2.rpf";
        public const string TargetChangeSet = "CCS_TITLE_UPDATE_STREAMING";
        private const string RootName       = "CDataFileMgr__ContentsOfDataFileXml";
        private const string PathSuffix     = "/rukzak/miami_rukzak_v2.rpf";

        private const string LegacyFileName   = "miami_rukzak.rpf";
        private const string LegacyPathSuffix = "/rukzak/miami_rukzak.rpf";

        public static bool Apply(string updateRpfPath, byte[] rpfBytes)
        {
            if (rpfBytes is null || rpfBytes.Length == 0)
                throw new ArgumentException("miami_rukzak.rpf bytes empty", nameof(rpfBytes));
            return Mutate(updateRpfPath, enable: true, rpfBytes);
        }

        public static bool Remove(string updateRpfPath)
            => Mutate(updateRpfPath, enable: false, rpfBytes: null);

        public static bool IsApplied(string updateRpfPath)
        {
            try
            {
                using var arc = RageArchiveWrapper7.Open(updateRpfPath);
                var contentEntry = arc.Root.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
                if (contentEntry is null) return false;
                var doc = XDocument.Parse(DecodeText(ReadEntryDecoded(contentEntry)));
                var dir = FindDir(arc.Root, createMissing: false);
                bool Has(string suffix, string name) =>
                    DataFilesHas(doc, suffix) &&
                    (dir?.GetFiles().Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? false);
                return Has(PathSuffix, FileName) || Has(LegacyPathSuffix, LegacyFileName);
            }
            catch { return false; }
        }

        private static bool Mutate(string updateRpfPath, bool enable, byte[]? rpfBytes)
        {
            if (!File.Exists(updateRpfPath))
                throw new FileNotFoundException("update.rpf not found", updateRpfPath);

            using var arc = RageArchiveWrapper7.Open(updateRpfPath);

            var contentEntry = arc.Root.GetFiles()
                .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
            if (contentEntry is null)
                throw new InvalidOperationException("content.xml not found at update.rpf root.");

            var doc = XDocument.Parse(DecodeText(ReadEntryDecoded(contentEntry)));
            RemoveEntries(doc, LegacyPathSuffix);
            if (enable) AddEntries(doc); else RemoveEntries(doc, PathSuffix);
            if (contentEntry is IArchiveBinaryFile xmlBin)
            {
                var outXml = SerializeXml(doc);
                xmlBin.Import(new MemoryStream(outXml));
                xmlBin.IsCompressed = false;
                xmlBin.IsEncrypted = false;
                xmlBin.UncompressedSize = (uint)outXml.Length;
            }

            var dir = FindDir(arc.Root, createMissing: enable);
            if (dir is null)
            {
                if (enable) throw new InvalidOperationException("rukzak directory could not be created in update.rpf.");
            }
            else
            {
                var legacy = dir.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(LegacyFileName, StringComparison.OrdinalIgnoreCase));
                if (legacy is not null)
                {
                    try { dir.DeleteFile(legacy); }
                    catch {}
                }

                var existing = dir.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(FileName, StringComparison.OrdinalIgnoreCase));
                if (enable)
                {
                    if (existing is IArchiveBinaryFile eb)
                    {
                        eb.Import(new MemoryStream(rpfBytes!));
                        eb.IsCompressed = false;
                        eb.IsEncrypted = false;
                        eb.UncompressedSize = (uint)rpfBytes!.Length;
                    }
                    else
                    {
                        var nf = dir.CreateBinaryFile();
                        nf.Name = FileName;
                        nf.Import(new MemoryStream(rpfBytes!));
                        nf.IsCompressed = false;
                        nf.IsEncrypted = false;
                        nf.UncompressedSize = (uint)rpfBytes!.Length;
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

        public enum RukzakSource
        {
            None,
            Ours,
            OursOutdated,
        }

        public sealed class RukzakStatus
        {
            public RukzakSource Source { get; init; }
            public int NulledModels { get; init; }
        }

        private const long MaxScanBytes = 32L * 1024 * 1024;

        public static RukzakStatus Detect(string updateRpfPath)
        {
            try
            {
                using var arc = RageArchiveWrapper7.Open(updateRpfPath);
                var contentEntry = arc.Root.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
                if (contentEntry is null) return new RukzakStatus { Source = RukzakSource.None };
                var doc = XDocument.Parse(DecodeText(ReadEntryDecoded(contentEntry)));

                var dir = FindDir(arc.Root, createMissing: false);

                bool HasFile(string name) => dir?.GetFiles()
                    .Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? false;

                if (DataFilesHas(doc, PathSuffix) && HasFile(FileName))
                    return new RukzakStatus { Source = RukzakSource.Ours, NulledModels = CountNulled(dir!, FileName) };

                if (DataFilesHas(doc, LegacyPathSuffix) && HasFile(LegacyFileName))
                    return new RukzakStatus { Source = RukzakSource.OursOutdated, NulledModels = CountNulled(dir!, LegacyFileName) };

                return new RukzakStatus { Source = RukzakSource.None };
            }
            catch { return new RukzakStatus { Source = RukzakSource.None }; }
        }

        private static int CountNulled(IArchiveDirectory dir, string fileName)
        {
            try
            {
                var f = dir.GetFiles().FirstOrDefault(x => x.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                return f is null ? 0 : CountHandModels(f);
            }
            catch { return 0; }
        }

        private static int CountHandModels(IArchiveFile file)
        {
            try
            {
                if (file is not IArchiveBinaryFile bin) return 0;
                if (bin.UncompressedSize > MaxScanBytes) return 0;

                using var ms = new MemoryStream();
                bin.Export(ms);
                if (ms.Length == 0 || ms.Length > MaxScanBytes) return 0;
                ms.Position = 0;

                using var nested = RageArchiveWrapper7.Open(ms, file.Name, leaveOpen: true);
                int n = 0;
                CountHandModelsIn(nested.Root, ref n, 0);
                return n;
            }
            catch { return 0; }
        }

        private static void CountHandModelsIn(IArchiveDirectory dir, ref int n, int depth)
        {
            if (depth > 3) return;
            foreach (var f in dir.GetFiles())
                if (OverlayModDetector.IsBackpackModelName(f.Name)) n++;
            foreach (var d in dir.GetDirectories())
                CountHandModelsIn(d, ref n, depth + 1);
        }

        private static bool DataFilesHas(XDocument doc, string suffix)
        {
            var dataFiles = doc.Root?.Element("dataFiles");
            return dataFiles?.Elements("Item").Any(it =>
                (it.Element("filename")?.Value ?? "").Trim()
                    .EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        private static bool DataFilesHasEntry(XDocument doc) => DataFilesHas(doc, PathSuffix);

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

        private static bool RemoveEntries(XDocument doc, string suffix)
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

        private static IArchiveDirectory? FindDir(IArchiveDirectory root, bool createMissing)
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
