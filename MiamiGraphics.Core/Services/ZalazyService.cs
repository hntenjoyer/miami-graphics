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
    public static class ZalazyService
    {
        public sealed class Variant
        {
            public string Server     { get; }
            public string FileName   { get; }
            public string EnableItem { get; }
            public string PathSuffix { get; }

            public Variant(string server, string fileName)
            {
                Server     = server;
                FileName   = fileName;
                EnableItem = "update:/" + fileName;
                PathSuffix = "/" + fileName;
            }
        }

        public static readonly Variant Gta5Rp   = new("gta5rp",   "zalazu.rpf");
        public static readonly Variant Majestic  = new("majestic", "mirz.rpf");
        public static readonly Variant[] All      = { Gta5Rp, Majestic };

        private const string LegacyPathSuffix = "/patch/anim/zalaz.rpf";
        private const string LegacyFileName   = "zalaz.rpf";
        private static readonly string[] LegacyRootFiles = { "slivkimods.rpf" };

        public const string TargetChangeSet = "CCS_TITLE_UPDATE_STREAMING";
        private const string RootName        = "CDataFileMgr__ContentsOfDataFileXml";

        public static Variant ByServer(string? server)
            => string.Equals(server, "majestic", StringComparison.OrdinalIgnoreCase) ? Majestic : Gta5Rp;

        public static bool Apply(string updateRpfPath, Variant variant, byte[] rpfBytes)
        {
            if (variant is null) throw new ArgumentNullException(nameof(variant));
            if (rpfBytes is null || rpfBytes.Length == 0)
                throw new ArgumentException("zalazy rpf bytes empty", nameof(rpfBytes));
            return Mutate(updateRpfPath, keep: variant, rpfBytes);
        }

        public static bool Remove(string updateRpfPath)
            => Mutate(updateRpfPath, keep: null, rpfBytes: null);

        public static string? AppliedServer(string updateRpfPath)
        {
            try
            {
                using var arc = RageArchiveWrapper7.Open(updateRpfPath);
                var contentEntry = arc.Root.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
                if (contentEntry is null) return null;
                var doc = XDocument.Parse(DecodeText(ReadEntryDecoded(contentEntry)));
                foreach (var v in All)
                {
                    bool inData = DataFilesHas(doc, v.PathSuffix);
                    bool fileThere = arc.Root.GetFiles()
                        .Any(f => f.Name.Equals(v.FileName, StringComparison.OrdinalIgnoreCase));
                    if (inData && fileThere) return v.Server;
                }
                return null;
            }
            catch { return null; }
        }

        public static bool IsApplied(string updateRpfPath) => AppliedServer(updateRpfPath) is not null;

        private static bool Mutate(string updateRpfPath, Variant? keep, byte[]? rpfBytes)
        {
            if (!File.Exists(updateRpfPath))
                throw new FileNotFoundException("update.rpf not found", updateRpfPath);

            using var arc = RageArchiveWrapper7.Open(updateRpfPath);

            var contentEntry = arc.Root.GetFiles()
                .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
            if (contentEntry is null)
                throw new InvalidOperationException("content.xml not found at update.rpf root.");

            var doc = XDocument.Parse(DecodeText(ReadEntryDecoded(contentEntry)));

            var stripSuffixes = All.Select(v => v.PathSuffix)
                .Concat(LegacyRootFiles.Select(f => "/" + f))
                .Append(LegacyPathSuffix)
                .ToList();
            RemoveEntries(doc, stripSuffixes);
            if (keep is not null) AddEntry(doc, keep);

            if (contentEntry is IArchiveBinaryFile xmlBin)
            {
                var outXml = SerializeXml(doc);
                xmlBin.Import(new MemoryStream(outXml));
                xmlBin.IsCompressed = false;
                xmlBin.IsEncrypted = false;
                xmlBin.UncompressedSize = (uint)outXml.Length;
            }

            foreach (var v in All)
            {
                if (keep is not null && v.Server == keep.Server) continue;
                DeleteRootFile(arc.Root, v.FileName);
            }
            DeleteLegacyFile(arc.Root);
            foreach (var name in LegacyRootFiles)
                DeleteRootFile(arc.Root, name);

            if (keep is not null)
            {
                var existing = arc.Root.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(keep.FileName, StringComparison.OrdinalIgnoreCase));
                if (existing is IArchiveBinaryFile eb)
                {
                    eb.Import(new MemoryStream(rpfBytes!));
                    eb.IsCompressed = false;
                    eb.IsEncrypted = false;
                    eb.UncompressedSize = (uint)rpfBytes!.Length;
                }
                else
                {
                    var nf = arc.Root.CreateBinaryFile();
                    nf.Name = keep.FileName;
                    nf.Import(new MemoryStream(rpfBytes!));
                    nf.IsCompressed = false;
                    nf.IsEncrypted = false;
                    nf.UncompressedSize = (uint)rpfBytes!.Length;
                }
            }

            arc.Flush();
            return true;
        }

        private static void DeleteRootFile(IArchiveDirectory root, string fileName)
        {
            var existing = root.GetFiles()
                .FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (existing is null) return;
            try { root.DeleteFile(existing); } catch {}
        }

        private static void DeleteLegacyFile(IArchiveDirectory root)
        {
            var animDir = Descend(Descend(Descend(root, "x64"), "patch"), "anim");
            if (animDir is null) return;
            var existing = animDir.GetFiles()
                .FirstOrDefault(f => f.Name.Equals(LegacyFileName, StringComparison.OrdinalIgnoreCase));
            if (existing is null) return;
            try { animDir.DeleteFile(existing); } catch {}
        }

        private static bool DataFilesHas(XDocument doc, string suffix)
        {
            var dataFiles = doc.Root?.Element("dataFiles");
            return dataFiles?.Elements("Item").Any(it =>
                (it.Element("filename")?.Value ?? "").Trim()
                    .EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        private static void AddEntry(XDocument doc, Variant variant)
        {
            var root = doc.Root ?? throw new InvalidOperationException("content.xml has no root.");

            var dataFiles = root.Element("dataFiles");
            if (dataFiles is null)
            {
                dataFiles = new XElement("dataFiles");
                root.AddFirst(dataFiles);
            }
            if (!DataFilesHas(doc, variant.PathSuffix))
            {
                dataFiles.Add(new XElement("Item",
                    new XElement("filename", variant.EnableItem),
                    new XElement("fileType", "RPF_FILE"),
                    new XElement("locked",     new XAttribute("value", "true")),
                    new XElement("disabled",   new XAttribute("value", "true")),
                    new XElement("persistent", new XAttribute("value", "true")),
                    new XElement("overlay",    new XAttribute("value", "true"))));
            }

            var enable = FindTargetFilesToEnable(root, createMissing: true);
            if (enable is not null &&
                !enable.Elements("Item").Any(i => i.Value.Trim()
                    .EndsWith(variant.PathSuffix, StringComparison.OrdinalIgnoreCase)))
            {
                enable.Add(new XElement("Item", variant.EnableItem));
            }
        }

        private static void RemoveEntries(XDocument doc, IReadOnlyList<string> suffixes)
        {
            var root = doc.Root;
            if (root is null) return;

            bool Matches(string value) =>
                suffixes.Any(s => value.EndsWith(s, StringComparison.OrdinalIgnoreCase));

            foreach (var it in root.Element("dataFiles")?.Elements("Item").ToList() ?? new())
            {
                if (Matches((it.Element("filename")?.Value ?? "").Trim()))
                    it.Remove();
            }
            foreach (var fte in root.Descendants("filesToEnable").ToList())
            {
                foreach (var i in fte.Elements("Item").ToList())
                {
                    if (Matches(i.Value.Trim()))
                        i.Remove();
                }
            }
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

        private static IArchiveDirectory? Descend(IArchiveDirectory? dir, string name)
        {
            if (dir is null) return null;
            return dir.GetDirectories()
                .FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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
