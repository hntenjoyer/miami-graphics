using System;
using System.Collections.Generic;
using System.Linq;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Parser
{

    public static class ArenaComponentDetector
    {

        private static readonly string[] ArenaMarkers =
        {
            "sp1_10_brand_01_wip_dontdelq.ydr",
            "sp1_10_details5.ydr",
            "v_62_bannersx.ydr",
            "v_62_blinds.ydr",
            "sp1_10_ground_2.ydr",
        };

        public static ComponentInfo Detect(IArchiveDirectory moddedRoot, ContentXmlInfo contentInfo)
        {
            var info = new ComponentInfo
            {
                Flags = new List<string> { "transferable" }
            };

            if (contentInfo == null || contentInfo.CustomRpfs == null || contentInfo.CustomRpfs.Count == 0)
                return info;

            var matchedPaths = new List<string>();
            var matchedMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string customRpf in contentInfo.CustomRpfs)
            {

                string rpfInternalPath = StripUpdatePrefix(customRpf);

                if (!TryOpenNestedArchive(moddedRoot, rpfInternalPath, out IArchive? nestedArc))
                {
                    Console.WriteLine($"[ArenaDetector] Пропуск кандидата {customRpf}: не удалось открыть внутри update.rpf.");
                    continue;
                }

                using (nestedArc)
                {
                    var foundMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    CollectMarkersRecursive(nestedArc!.Root, foundMarkers);

                    if (foundMarkers.Count > 0)
                    {
                        matchedPaths.Add(rpfInternalPath);
                        foreach (string m in foundMarkers)
                            matchedMarkers.Add(m);
                        Console.WriteLine($"[ArenaDetector] {rpfInternalPath} - арена (маркеры: {string.Join(", ", foundMarkers)}).");
                    }
                }
            }

            if (matchedPaths.Count == 0)
                return info;

            info.IsFound = true;
            info.InternalPaths = matchedPaths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            info.SourceRpf = info.InternalPaths[0];
            foreach (string m in matchedMarkers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                info.Flags.Add($"marker:{m}");

            return info;
        }

        private static string StripUpdatePrefix(string path)
        {
            string p = (path ?? "").Replace('\\', '/').Trim();
            if (p.StartsWith("update:/", StringComparison.OrdinalIgnoreCase))
                p = p.Substring("update:/".Length);
            return p.TrimStart('/');
        }

        private static bool TryOpenNestedArchive(IArchiveDirectory root, string rpfInternalPath, out IArchive? archive)
        {
            archive = null;
            if (string.IsNullOrWhiteSpace(rpfInternalPath))
                return false;

            var parts = rpfInternalPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            IArchiveDirectory? current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                current = current?.GetDirectories().FirstOrDefault(d =>
                    d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (current == null) return false;
            }

            var rpfFile = current?.GetFiles().FirstOrDefault(f =>
                f.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;
            if (rpfFile == null) return false;

            try
            {
                var stream = rpfFile.GetStream();
                archive = RageArchiveWrapper7.Open(stream, rpfFile.Name, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void CollectMarkersRecursive(IArchiveDirectory dir, HashSet<string> found)
        {
            foreach (var file in dir.GetFiles())
            {
                foreach (string marker in ArenaMarkers)
                {
                    if (file.Name.Equals(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(marker);
                        break;
                    }
                }

                if (file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && file is IArchiveBinaryFile binFile)
                {
                    try
                    {
                        using var stream = binFile.GetStream();
                        using var nestedArc = RageArchiveWrapper7.Open(stream, binFile.Name, true);
                        CollectMarkersRecursive(nestedArc.Root, found);
                    }
                    catch {  }
                }
            }

            foreach (var subDir in dir.GetDirectories())
                CollectMarkersRecursive(subDir, found);
        }
    }
}
