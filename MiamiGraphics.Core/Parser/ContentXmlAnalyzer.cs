using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using RageLib.Archives;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Parser
{
    public class ContentXmlAnalyzer
    {
        private static readonly HashSet<string> DefaultFilesToEnable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "x64/patch/anim/expressions.rpf",
            "x64/patch/anim/networkdefs.rpf",
            "x64/data/cdimages/scaleform_generic.rpf",
            "x64/data/cdimages/scaleform_generic_2.rpf",
            "x64/patch/data/cdimages/scaleform_minigames.rpf",
            "x64/patch/data/cdimages/scaleform_minimap.rpf",
            "x64/patch/data/cdimages/scaleform_web.rpf",
            "x64/patch/data/effects/ptfx.rpf"
        };

        public ContentXmlInfo AnalyzeFromArchive(IArchiveDirectory moddedRoot)
        {
            var info = new ContentXmlInfo();

            var contentXmlFile = moddedRoot
                .GetFiles()
                .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));

            if (contentXmlFile == null)
            {
                Console.WriteLine("[ContentXmlAnalyzer] content.xml не найден в корне modded archive");
                return info;
            }

            try
            {
                byte[] xmlBytes = GetRealFileBytes(contentXmlFile);

                if (xmlBytes == null || xmlBytes.Length == 0)
                {
                    Console.WriteLine("[ContentXmlAnalyzer] content.xml пустой после извлечения");
                    info.ParseError = "content.xml присутствует, но пуст после извлечения";
                    return info;
                }

                string xmlText = DecodeXmlText(xmlBytes);

                if (string.IsNullOrWhiteSpace(xmlText))
                {
                    Console.WriteLine("[ContentXmlAnalyzer] content.xml не удалось декодировать в текст");
                    info.ParseError = "content.xml не удалось декодировать (неизвестная кодировка/битые байты)";
                    DumpBrokenXml(xmlBytes);
                    return info;
                }

                var doc = XDocument.Parse(xmlText);
                var items = doc
                    .Descendants("filesToEnable")
                    .Elements("Item")
                    .Select(x => x.Value)
                    .ToList();

                Console.WriteLine($"[ContentXmlAnalyzer] Найдено записей filesToEnable: {items.Count}");

                foreach (var item in items)
                {
                    string normalizedItem = NormalizeFilesToEnablePath(item);

                    info.AllFilesToEnable.Add(normalizedItem);

                    if (DefaultFilesToEnable.Contains(normalizedItem))
                        info.DefaultRpfs.Add(normalizedItem);
                    else
                        info.CustomRpfs.Add(normalizedItem);
                }

                info.AllFilesToEnable = info.AllFilesToEnable
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                info.DefaultRpfs = info.DefaultRpfs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                info.CustomRpfs = info.CustomRpfs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Console.WriteLine($"[ContentXmlAnalyzer] Default RPFs: {info.DefaultRpfs.Count}, Custom RPFs: {info.CustomRpfs.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ContentXmlAnalyzer] Ошибка чтения content.xml: " + ex.Message);
                info.ParseError = "content.xml повреждён: " + ex.Message;

                try
                {
                    using var ms = new MemoryStream();
                    contentXmlFile.Export(ms);
                    DumpBrokenXml(ms.ToArray());
                }
                catch
                {
                }
            }

            return info;
        }

        private byte[] GetRealFileBytes(IArchiveFile file)
        {
            if (file is IArchiveBinaryFile binFile)
            {
                using var ms = new MemoryStream();
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
                        Console.WriteLine($"[ContentXmlAnalyzer] WARN: Нет NG ключа для {binFile.Name} (idx={keyIdx})");
                    }
                }

                if (binFile.IsCompressed)
                {
                    using var def = new global::System.IO.Compression.DeflateStream(
                        new MemoryStream(buf),
                        global::System.IO.Compression.CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    def.CopyTo(outMs);
                    return outMs.ToArray();
                }

                return buf;
            }

            using (var ms = new MemoryStream())
            {
                file.Export(ms);
                return ms.ToArray();
            }
        }

        private string DecodeXmlText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            using (var ms = new MemoryStream(bytes))
            using (var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                string text = reader.ReadToEnd();
                if (LooksLikeXml(text))
                    return text;
            }

            string utf8 = Encoding.UTF8.GetString(bytes);
            if (LooksLikeXml(utf8))
                return utf8;

            string utf16 = Encoding.Unicode.GetString(bytes);
            if (LooksLikeXml(utf16))
                return utf16;

            string utf16be = Encoding.BigEndianUnicode.GetString(bytes);
            if (LooksLikeXml(utf16be))
                return utf16be;

            return null;
        }

        private bool LooksLikeXml(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string trimmed = text.TrimStart('\uFEFF', '\0', ' ', '\t', '\r', '\n');

            return trimmed.StartsWith("<") &&
                   (trimmed.Contains("<content") ||
                    trimmed.Contains("<filesToEnable") ||
                    trimmed.Contains("<?xml"));
        }

        private string NormalizeFilesToEnablePath(string xmlItemPath)
        {
            string path = xmlItemPath.Trim();

            path = path.Replace("update:/%PLATFORM%/", "x64/", StringComparison.OrdinalIgnoreCase);
            path = path.Replace("\\", "/");

            if (path.StartsWith("update:/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring("update:/".Length);

            while (path.Contains("//"))
                path = path.Replace("//", "/");

            return path.Trim('/');
        }

        private void DumpBrokenXml(byte[] bytes)
        {
            try
            {
                string dumpDir = Path.Combine(MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir, "_debug");
                Directory.CreateDirectory(dumpDir);

                string rawPath = Path.Combine(dumpDir, "content_raw.bin");
                File.WriteAllBytes(rawPath, bytes);

                Console.WriteLine($"[ContentXmlAnalyzer] RAW dump сохранён: {rawPath}");
            }
            catch
            {
            }
        }
    }
}