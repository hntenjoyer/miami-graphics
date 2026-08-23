#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Injector
{
    public sealed record DeclarationCheck(
        bool Ok,
        IReadOnlyList<string> Declared,
        IReadOnlyList<string> Missing,
        string Error);

    public static class UpdateRpfDeclarationCheck
    {
        private const string ContentXmlEntry = "content.xml";

        public static DeclarationCheck Run(string updateRpfPath)
        {
            try
            {
                using var arc = RageArchiveWrapper7.Open(updateRpfPath);

                var xmlEntry = arc.Root.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(ContentXmlEntry, StringComparison.OrdinalIgnoreCase));
                if (xmlEntry is not IArchiveBinaryFile bin)
                    return new DeclarationCheck(false, Array.Empty<string>(), Array.Empty<string>(),
                                                "в корне update.rpf нет content.xml");

                string xml = Encoding.UTF8.GetString(ReadEntryBytes(bin));

                var declared = Regex.Matches(xml, @"update:/([^<""\s]+)", RegexOptions.IgnoreCase)
                    .Select(m => m.Groups[1].Value.Trim().TrimEnd('/')
                                  .Replace("%PLATFORM%", "x64", StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var missing = declared.Where(p => !EntryExists(arc.Root, p)).ToList();
                return new DeclarationCheck(missing.Count == 0, declared, missing, "");
            }
            catch (Exception ex)
            {
                return new DeclarationCheck(false, Array.Empty<string>(), Array.Empty<string>(),
                                            $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        public static string? ReadContentXml(string updateRpfPath)
        {
            try
            {
                using var arc = RageArchiveWrapper7.Open(updateRpfPath);
                var entry = arc.Root.GetFiles()
                    .FirstOrDefault(f => f.Name.Equals(ContentXmlEntry, StringComparison.OrdinalIgnoreCase));
                return entry is IArchiveBinaryFile bin ? Encoding.UTF8.GetString(ReadEntryBytes(bin)) : null;
            }
            catch { return null; }
        }

        private static byte[] ReadEntryBytes(IArchiveBinaryFile bin)
        {
            using var ms = new MemoryStream();
            bin.Export(ms);
            var buf = ms.ToArray();

            if (bin.IsEncrypted)
            {
                var hash = RageLib.GTA5.Cryptography.GTA5Hash.CalculateHash(bin.Name);
                var keyIdx = (hash + (uint)bin.UncompressedSize + (101 - 40)) % 0x65;
                var key = RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS?[keyIdx];
                if (key != null && key.Length > 0)
                    buf = RageLib.GTA5.Cryptography.GTA5Crypto.Decrypt(buf, key);
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

        private static bool EntryExists(IArchiveDirectory root, string relPath)
        {
            var parts = relPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var dir = root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                var next = dir.GetDirectories()
                    .FirstOrDefault(d => (d.Name ?? "").Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (next is null) return false;
                dir = next;
            }

            var leaf = parts[^1];
            if (dir.GetFiles().Any(f => (f.Name ?? "").Equals(leaf, StringComparison.OrdinalIgnoreCase))) return true;
            return dir.GetDirectories().Any(d => (d.Name ?? "").Equals(leaf, StringComparison.OrdinalIgnoreCase));
        }
    }
}
