#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DbgWriter = System.Diagnostics.Debug;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{

    public static class ArmorRpfMultiFolder
    {
        private static void Log(string s) => DbgWriter.WriteLine("[ArmorRpfMultiFolder] " + s);

        private static readonly string[] CanonicalPedNamespaces =
        {
            "mp_f_freemode_01_mp_f_january2016",
            "mp_m_freemode_01_mp_m_january2016",
            "mp_f_freemode_01_female_heist",
            "mp_m_freemode_01_male_heist",
        };

        public static bool ExpandToCanonicalPedNamespaces(string armorRpfPath)
        {
            if (string.IsNullOrWhiteSpace(armorRpfPath))
            {
                Log("path is empty - skip");
                return false;
            }
            if (!File.Exists(armorRpfPath))
            {
                Log($"file missing: {armorRpfPath} - skip");
                return false;
            }

            var folderGroups = new Dictionary<string, List<(string FileName, byte[] Bytes, bool IsResource)>>(
                StringComparer.OrdinalIgnoreCase);

            try
            {
                using var stream = File.Open(armorRpfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var arc = RageArchiveWrapper7.Open(stream, Path.GetFileName(armorRpfPath), true);
                CollectFiles(arc.Root, "", folderGroups);
            }
            catch (Exception ex)
            {
                Log($"open failed: {ex.GetType().Name}: {ex.Message} - skip");
                return false;
            }

            if (folderGroups.Count == 0)
            {
                Log("no files found in armor RPF - skip");
                return false;
            }

            var armorBearingFolders = folderGroups
                .Where(kv => kv.Value.Any(f => YddNameRegex.IsMatch(f.FileName)))
                .Select(kv => kv.Key)
                .ToList();
            if (armorBearingFolders.Count == 0)
            {
                Log("no folder contains task_*_u.ydd - not an armor pack, skip");
                return false;
            }

            var missingCanonical = CanonicalPedNamespaces
                .Where(ns => !folderGroups.ContainsKey(ns))
                .ToList();
            if (missingCanonical.Count == 0)
            {
                Log("all four canonical ped namespaces already present - no-op");
                return false;
            }

            foreach (var missing in missingCanonical)
            {
                var donor = PickDonor(missing, armorBearingFolders, folderGroups);
                if (donor == null)
                {
                    Log($"no donor available for {missing} - skip");
                    continue;
                }

                folderGroups[missing] = folderGroups[donor]
                    .Select(f => (f.FileName, f.Bytes, f.IsResource))
                    .ToList();
                Log($"duplicated {donor} -> {missing} ({folderGroups[missing].Count} files)");
            }

            var entriesByFolder = new Dictionary<string, IList<SyntheticArmorRpfBuilder.FileEntry>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var kv in folderGroups)
            {
                if (kv.Value.Count == 0) continue;
                entriesByFolder[kv.Key] = kv.Value.Select(f => new SyntheticArmorRpfBuilder.FileEntry
                {
                    FileName   = f.FileName,
                    FileBytes  = f.Bytes,
                    IsResource = f.IsResource,
                }).ToList();
            }

            var tempOut = armorRpfPath + ".multifold.tmp";
            try
            {
                if (!SyntheticArmorRpfBuilder.BuildMulti(tempOut, entriesByFolder))
                {
                    Log("BuildMulti returned false - skip");
                    return false;
                }
                File.Copy(tempOut, armorRpfPath, overwrite: true);
                File.Delete(tempOut);
            }
            catch (Exception ex)
            {
                Log($"repack/replace failed: {ex.GetType().Name}: {ex.Message}");
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                return false;
            }

            Log($"OK: armor.rpf now covers {entriesByFolder.Count} ped namespace(s) " +
                $"(filled: {string.Join(", ", missingCanonical)})");
            return true;
        }

        private static readonly Regex YddNameRegex = new Regex(
            @"^task_\d+_u\.ydd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string PickDonor(string missing, List<string> armorBearing,
            Dictionary<string, List<(string, byte[], bool)>> _ )
        {
            if (armorBearing.Count == 0) return null;
            char missingGender = GuessGender(missing);

            if (missingGender != '?')
            {
                var matched = armorBearing.FirstOrDefault(d => GuessGender(d) == missingGender);
                if (matched != null) return matched;
            }

            return armorBearing[0];
        }

        private static char GuessGender(string ns)
        {
            if (string.IsNullOrEmpty(ns)) return '?';
            var lower = ns.ToLowerInvariant();
            if (lower.StartsWith("mp_f_") || lower.Contains("female")) return 'f';
            if (lower.StartsWith("mp_m_") || lower.Contains("male"))   return 'm';
            return '?';
        }

        private static void CollectFiles(
            IArchiveDirectory dir,
            string parentChain,
            Dictionary<string, List<(string, byte[], bool)>> groups)
        {

            string leafFolder = string.IsNullOrEmpty(parentChain)
                ? ""
                : LastSegment(parentChain);

            foreach (var f in dir.GetFiles())
            {
                if (string.IsNullOrEmpty(f.Name)) continue;
                bool isResource = LooksLikeResource(f.Name);
                byte[] bytes = TryExportBytes(f, parentChain + "/" + f.Name);
                if (bytes == null || bytes.Length == 0) continue;
                if (string.IsNullOrEmpty(leafFolder)) continue;

                if (!groups.TryGetValue(leafFolder, out var lst))
                {
                    lst = new List<(string, byte[], bool)>();
                    groups[leafFolder] = lst;
                }
                lst.Add((f.Name, bytes, isResource));
            }

            foreach (var sub in dir.GetDirectories())
            {
                string subPath = string.IsNullOrEmpty(parentChain) ? sub.Name : parentChain + "/" + sub.Name;
                CollectFiles(sub, subPath, groups);
            }
        }

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[parts.Length - 1] : "";
        }

        private static bool LooksLikeResource(string fileName)
            => SyntheticArmorRpfBuilder.IsRageResourceByExt(fileName);

        private static byte[] TryExportBytes(IArchiveFile f, string pathForLog)
        {
            try
            {
                using var ms = new MemoryStream();
                f.Export(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                Log($"export fail {pathForLog}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}
