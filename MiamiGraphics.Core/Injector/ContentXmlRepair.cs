#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MiamiGraphics.Core.Services;

namespace MiamiGraphics.Core.Injector
{
    public sealed record ContentXmlRepairResult(
        bool Changed,
        IReadOnlyList<string> Removed,
        IReadOnlyList<string> Left,
        string Error);

    public static class ContentXmlRepair
    {
        private const string ContentXmlTarget = "content.xml";

        public static ContentXmlRepairResult Run(string updateRpfPath)
        {
            try
            {
                var check = UpdateRpfDeclarationCheck.Run(updateRpfPath);
                if (check.Error.Length > 0)
                    return new ContentXmlRepairResult(false, Array.Empty<string>(), Array.Empty<string>(), check.Error);
                if (check.Missing.Count == 0)
                    return new ContentXmlRepairResult(false, Array.Empty<string>(), Array.Empty<string>(), "");

                var xml = UpdateRpfDeclarationCheck.ReadContentXml(updateRpfPath);
                if (xml is null)
                    return new ContentXmlRepairResult(false, Array.Empty<string>(), check.Missing,
                                                      "content.xml не прочитан");

                var patched = RemoveDeclarations(xml, check.Missing, out var removed);
                if (removed.Count == 0)
                    return new ContentXmlRepairResult(false, Array.Empty<string>(), check.Missing,
                                                      "строки объявлений не найдены - файл размечен иначе");

                PatchCustomizationSupport.ReplaceFilesInLiveArchive(
                    updateRpfPath,
                    new[] { new KeyValuePair<string, byte[]>(ContentXmlTarget, new UTF8Encoding(false).GetBytes(patched)) },
                    out int applied, out _, addMissingPlainPaths: false);

                if (applied == 0)
                    return new ContentXmlRepairResult(false, Array.Empty<string>(), check.Missing,
                                                      "content.xml не переписался");

                var after = UpdateRpfDeclarationCheck.Run(updateRpfPath);
                var left = after.Missing.ToList();
                return new ContentXmlRepairResult(true, removed, left, "");
            }
            catch (Exception ex)
            {
                return new ContentXmlRepairResult(false, Array.Empty<string>(), Array.Empty<string>(),
                                                  $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static string RemoveDeclarations(string xml, IReadOnlyList<string> paths, out List<string> removed)
        {
            removed = new List<string>();
            var text = xml;

            foreach (var path in paths)
            {
                var needle = "update:/" + path;
                bool touched = false;

                while (true)
                {
                    int at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) break;

                    int open = text.LastIndexOf("<Item>", at, StringComparison.OrdinalIgnoreCase);
                    int close = text.IndexOf("</Item>", at, StringComparison.OrdinalIgnoreCase);
                    if (open < 0 || close < 0) break;

                    var between = text.Substring(open + 6, at - open - 6);
                    if (between.Contains('<')) break;

                    text = CutTag(text, open, close + "</Item>".Length);
                    touched = true;
                }

                while (true)
                {
                    int at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) break;

                    int open = text.LastIndexOf("<Item>", at, StringComparison.OrdinalIgnoreCase);
                    int close = text.IndexOf("</Item>", at, StringComparison.OrdinalIgnoreCase);
                    if (open < 0 || close < 0) break;

                    var body = text.Substring(open + 6, close - open - 6);
                    if (body.Contains("<Item>", StringComparison.OrdinalIgnoreCase)) break;

                    text = CutTag(text, open, close + "</Item>".Length);
                    touched = true;
                }

                if (touched) removed.Add(path);
            }

            return text;
        }

        private static string CutTag(string text, int from, int to)
        {
            var cut = text.Remove(from, to - from);

            if (cut.Length == 0) return cut;
            int lineStart = cut.LastIndexOf((char)10, Math.Max(0, Math.Min(from - 1, cut.Length - 1)));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            int lineEnd = cut.IndexOf((char)10, lineStart);
            if (lineEnd < 0) lineEnd = cut.Length;

            var line = cut.Substring(lineStart, lineEnd - lineStart);
            if (line.Trim().Length == 0)
            {
                int removeTo = lineEnd < cut.Length ? lineEnd + 1 : cut.Length;
                cut = cut.Remove(lineStart, removeTo - lineStart);
            }

            return cut;
        }
    }
}
