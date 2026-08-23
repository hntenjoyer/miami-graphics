using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.Core.Services
{
    public sealed record MinimapLayoutElement(
        string Element,
        decimal PosX,
        decimal PosY,
        decimal? SizeX,
        decimal? SizeY);

    public class MinimapLayoutService
    {
        public const string FrontendXmlTargetPath = "common/data/ui/frontend.xml";

        private static readonly string[] DefaultTargetPaths = { FrontendXmlTargetPath };

        public static readonly IReadOnlyList<MinimapLayoutElement> VanillaElements = new[]
        {
            new MinimapLayoutElement("minimap",      -0.0045m, 0.002m, 0.150m, 0.188888m),
            new MinimapLayoutElement("minimap_mask",  0.020m,  0.032m, 0.111m, 0.159m),
            new MinimapLayoutElement("minimap_blur", -0.030m,  0.022m, 0.266m, 0.237m),
        };

        public static readonly IReadOnlyList<MinimapLayoutElement> VanillaBigmapElements = new[]
        {
            new MinimapLayoutElement("bigmap",      -0.003975m, 0.022m, 0.364m, 0.460416666m),
            new MinimapLayoutElement("bigmap_mask",  0.145m,    0.015m, 0.176m, 0.395m),
            new MinimapLayoutElement("bigmap_blur", -0.019m,    0.022m, 0.262m, 0.464m),
        };

        private const decimal MaxSafezone = 0.05m;

        private const decimal MinimapVisibleCenterY = 0.5220588m;

        public static List<MinimapLayoutElement> BuildCustomElements(decimal posX, decimal posY)
        {
            var v = VanillaElements[0];
            decimal dx = posX - v.PosX, dy = posY - v.PosY;
            return AppendBigmap(VanillaElements
                .Select(e => e with { PosX = e.PosX + dx, PosY = e.PosY + dy })
                .ToList());
        }

        public static List<MinimapLayoutElement> AppendBigmap(IReadOnlyList<MinimapLayoutElement> elements)
        {
            var list = elements?.ToList() ?? new List<MinimapLayoutElement>();
            if (list.Any(e => e.Element.StartsWith("bigmap", StringComparison.OrdinalIgnoreCase)))
                return list;

            list.AddRange(VanillaBigmapElements.Select(e => e with { SizeX = null, SizeY = null }));
            return list;
        }

        public bool Apply(
            string patchRootDirectory,
            DiffManifest manifest,
            string gtaRootPath,
            IReadOnlyList<MinimapLayoutElement> elements,
            byte[]? fallbackFrontendXml = null)
        {
            if (elements == null || elements.Count == 0)
                return false;

            var frontendFiles = PatchCustomizationSupport
                .FindExistingFiles(patchRootDirectory, manifest, "frontend.xml")
                .Where(f => f.TargetPath.EndsWith(FrontendXmlTargetPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (frontendFiles.Count == 0)
            {
                Console.WriteLine("[MinimapLayout] frontend.xml не найден в патче. Извлекаем оригинал из update.rpf...");
                try
                {
                    frontendFiles.AddRange(PatchCustomizationSupport
                        .EnsureOriginalsImported(
                            patchRootDirectory,
                            manifest,
                            gtaRootPath,
                            "frontend.xml",
                            DefaultTargetPaths)
                        .Where(f => f.TargetPath.EndsWith(FrontendXmlTargetPath, StringComparison.OrdinalIgnoreCase)));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MinimapLayout] Оригинал frontend.xml не найден в update.rpf ({ex.Message}).");
                }
            }

            if (frontendFiles.Count == 0)
            {
                if (fallbackFrontendXml == null || fallbackFrontendXml.Length == 0)
                {
                    Console.WriteLine("[MinimapLayout] frontend.xml отсутствует и эталон не передан - позиция не применена.");
                    return false;
                }
                Console.WriteLine("[MinimapLayout] Вписываю эталонный frontend.xml в патч (R2/бандл).");
                var wf = ImportFallbackFrontend(patchRootDirectory, manifest, fallbackFrontendXml);
                if (wf != null) frontendFiles.Add(wf);
            }

            bool anyPatched = false;
            foreach (PatchWorkspaceFile file in frontendFiles)
            {
                if (PatchFrontendXml(file.PhysicalPath, elements))
                {
                    PatchCustomizationSupport.UpsertPatchAction(manifest, patchRootDirectory, file);
                    anyPatched = true;
                    Console.WriteLine($"[MinimapLayout] Применена раскладка: {file.TargetPath}");
                }
            }

            if (anyPatched)
                PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);

            return anyPatched;
        }

        public static byte[]? PatchFrontendXmlBytes(byte[] frontendXml, IReadOnlyList<MinimapLayoutElement> elements)
        {
            if (frontendXml is null || frontendXml.Length == 0 || elements is null || elements.Count == 0)
                return null;
            var tmp = Path.Combine(Path.GetTempPath(), $"mg_frontend_{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllBytes(tmp, frontendXml);
                return PatchFrontendXml(tmp, elements) ? File.ReadAllBytes(tmp) : null;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static List<MinimapLayoutElement> WithTransparency(
            IReadOnlyList<MinimapLayoutElement> elements, bool transparent)
        {
            var list = elements?.ToList() ?? new List<MinimapLayoutElement>();
            if (!transparent) return list;

            var blur = list.FirstOrDefault(e =>
                string.Equals(e.Element, "minimap_blur", StringComparison.OrdinalIgnoreCase));
            var basis = blur ?? list.FirstOrDefault(e =>
                string.Equals(e.Element, "minimap", StringComparison.OrdinalIgnoreCase));
            decimal px = basis?.PosX ?? 0m, py = basis?.PosY ?? 0m;

            list.RemoveAll(e => string.Equals(e.Element, "minimap_blur", StringComparison.OrdinalIgnoreCase));
            list.Add(new MinimapLayoutElement("minimap_blur", px, py, 0m, 0m));
            return list;
        }

        private static PatchWorkspaceFile? ImportFallbackFrontend(
            string patchRootDirectory, DiffManifest manifest, byte[] frontendXml)
        {
            try
            {
                string physicalPath = Path.Combine(
                    patchRootDirectory, "patch_files", FrontendXmlTargetPath.Replace("/", "\\"));
                string? dir = Path.GetDirectoryName(physicalPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(physicalPath, frontendXml);

                var wf = new PatchWorkspaceFile
                {
                    TargetPath   = FrontendXmlTargetPath,
                    PhysicalPath = physicalPath,
                    ActionType   = ActionType.Import,
                };
                PatchCustomizationSupport.UpsertPatchAction(manifest, patchRootDirectory, wf);
                return wf;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MinimapLayout] Не удалось вписать эталонный frontend.xml: {ex.Message}");
                return null;
            }
        }

        public static MinimapLayoutElement? ReadElementCoords(byte[] frontendXml, string element = "minimap")
        {
            if (frontendXml is null || frontendXml.Length == 0) return null;
            string text;
            try { text = Encoding.UTF8.GetString(frontendXml); }
            catch { return null; }

            var tagRegex = new Regex(
                "<data\\s+name=\"" + Regex.Escape(element) + "\"[^>]*>",
                RegexOptions.IgnoreCase);
            var m = tagRegex.Match(text);
            if (!m.Success) return null;

            var tag = m.Value;
            if (TryReadAttr(tag, "posX", out var px) &&
                TryReadAttr(tag, "posY", out var py) &&
                TryReadAttr(tag, "sizeX", out var sx) &&
                TryReadAttr(tag, "sizeY", out var sy))
                return new MinimapLayoutElement(element, px, py, sx, sy);
            return null;
        }

        private static bool TryReadAttr(string tag, string attribute, out decimal value)
        {
            value = 0m;
            var m = Regex.Match(tag, attribute + "\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success && decimal.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool PatchFrontendXml(string physicalPath, IReadOnlyList<MinimapLayoutElement> elements)
        {
            string text;
            try
            {
                text = Encoding.UTF8.GetString(File.ReadAllBytes(physicalPath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MinimapLayout] Не удалось прочитать {physicalPath}: {ex.Message}");
                return false;
            }

            bool changed = false;
            foreach (MinimapLayoutElement element in elements)
            {
                var tagRegex = new Regex(
                    "<data\\s+name=\"" + Regex.Escape(element.Element) + "\"[^>]*>",
                    RegexOptions.IgnoreCase);

                bool replacedHere = false;
                string updated = tagRegex.Replace(text, match =>
                {
                    string tag = match.Value;
                    tag = SetAttribute(tag, "posX",  element.PosX);
                    tag = SetAttribute(tag, "posY",  element.PosY);
                    if (element.SizeX is { } sx) tag = SetAttribute(tag, "sizeX", sx);
                    if (element.SizeY is { } sy) tag = SetAttribute(tag, "sizeY", sy);
                    replacedHere = true;
                    return tag;
                });

                if (replacedHere)
                {
                    text = updated;
                    changed = true;
                }
                else
                {
                    Console.WriteLine($"[MinimapLayout] Элемент '{element.Element}' не найден в {Path.GetFileName(physicalPath)} - пропущен.");
                }
            }

            if (!changed)
                return false;

            try
            {
                File.WriteAllBytes(physicalPath, Encoding.UTF8.GetBytes(text));
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MinimapLayout] Не удалось записать {physicalPath}: {ex.Message}");
                return false;
            }
        }

        private static string SetAttribute(string tag, string attribute, decimal value)
        {
            string formatted = value.ToString(CultureInfo.InvariantCulture);
            return Regex.Replace(
                tag,
                attribute + "\\s*=\\s*\"[^\"]*\"",
                attribute + "=\"" + formatted + "\"",
                RegexOptions.IgnoreCase);
        }
    }
}
