using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MiamiGraphics.Core.Injector
{
    public static class TimecycleModsMerger
    {
        private static readonly Regex ModifierBlock = new(
            "<modifier\\s+name=\"(?<name>[^\"]+)\"[^>]*?(?:/>|>[\\s\\S]*?</modifier>)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsModsFile(string fileName) =>
            Regex.IsMatch(fileName, @"^timecycle_mods_.*\.xml$", RegexOptions.IgnoreCase);

        public static Dictionary<string, string> CollectDonorModifierBlocks(string donorComponentRoot)
        {
            var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(donorComponentRoot)) return blocks;

            var modsFiles = Directory.GetFiles(donorComponentRoot, "*.xml", SearchOption.AllDirectories)
                .Where(p => IsModsFile(Path.GetFileName(p)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

            foreach (var file in modsFiles)
            {
                string text;
                try { text = ReadAllTextPreserving(file, out _); }
                catch { continue; }

                foreach (Match m in ModifierBlock.Matches(text))
                {
                    var name = m.Groups["name"].Value;
                    if (!blocks.ContainsKey(name))
                        blocks[name] = m.Value;
                }
            }
            return blocks;
        }

        public static string? MergeModsXml(
            string baseXml,
            IReadOnlyDictionary<string, string> donorBlocksByName,
            out int replacedCount,
            out string? error)
        {
            replacedCount = 0;
            error = null;

            var baseNames = ModifierBlock.Matches(baseXml).Select(m => m.Groups["name"].Value).ToList();
            if (baseNames.Count == 0)
            {
                error = "в базовом файле не найдено ни одного <modifier> - формат неожиданный";
                return null;
            }

            int replaced = 0;
            string merged = ModifierBlock.Replace(baseXml, m =>
            {
                var name = m.Groups["name"].Value;
                if (donorBlocksByName.TryGetValue(name, out var donorBlock))
                {
                    replaced++;
                    return donorBlock;
                }
                return m.Value;
            });
            replacedCount = replaced;

            if (replaced == 0) return null;

            var mergedNames = ModifierBlock.Matches(merged).Select(m => m.Groups["name"].Value).ToList();
            if (!mergedNames.SequenceEqual(baseNames, StringComparer.Ordinal))
            {
                error = $"пост-проверка провалена: список модификаторов изменился ({baseNames.Count} → {mergedNames.Count})";
                return null;
            }

            return merged;
        }

        public static string ReadAllTextPreserving(string path, out bool hadBom)
        {
            var bytes = File.ReadAllBytes(path);
            hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return new UTF8Encoding(false).GetString(hadBom ? bytes.AsSpan(3).ToArray() : bytes);
        }

        public static void WriteAllTextPreserving(string path, string text, bool hadBom)
        {
            File.WriteAllText(path, text, new UTF8Encoding(hadBom));
        }
    }
}
