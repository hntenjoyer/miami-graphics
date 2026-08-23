using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{
    public static class OverlayModDetector
    {
        public enum Kind { Zalazy, GreenZone, Backpack }

        private static readonly string[] OwnPathSuffixes =
        {
            "/zalazu.rpf", "/mirz.rpf", "/slivkimods.rpf", "/patch/anim/zalaz.rpf",
            "/green_zone/zz_rpf.rpf", "/green_zone/gz.rpf",
            "/rukzak/miami_rukzak_v2.rpf", "/rukzak/miami_rukzak.rpf",
        };

        private static readonly string[] ZalazyDetectTokens =
        {
            "firstpov", "thirdpov", "peoplehere", "teamneed", "rightclick.yd",
            "climbs.ymap", "climbs.ytyp", "zapret.yd",
            "gucci_ghetto", "gucci_mapping", "guccimcl", "gucciv3_", "yoopiyo_",
            "mirz_ghetto", "mirz_leak",
        };
        private static readonly string[] ZalazyStripTokens =
        {
            "firstpov", "thirdpov", "peoplehere", "teamneed", "rightclick",
            "climbs.ymap", "climbs.ytyp", "zapret", "gucci", "yoopiyo", "mirz",
        };

        private static readonly string[] GreenZoneDetectTokens =
        {
            "greenzone.yt", "gz_azs", "gz_unik", "gz_frmland", "gz_marketpolya", "gz_marketsh",
            "marketshors", "arenagz", "bankgz", "zheltigz", "lostgz", "azsghetto", "azsems",
            "gunshopz", "inkaskluch", "kluchelektrik",
            "zz_barbershop", "zz_bahama", "zz_auktsion", "zz_bolnitsa", "zz_ferma", "zz_avtoshkola",
            "processfeliks", "mrzfl",
        };
        private static readonly HashSet<string> GreenZoneStripExact = new(StringComparer.OrdinalIgnoreCase)
        {
            "gz_azs.ydr", "gz_azs.ymap", "gz_frmland2.ydr", "gz_frmland2.ymap",
            "gz_marketpolya.ydr", "gz_marketpolya.ymap", "gz_unik.ydr", "gz_unik.ymap",
            "marketsh.ydr", "marketshors.ymap", "mrkt.ytyp",
            "arenagz.ydr", "autosalongh.ydr", "azsems.ydr", "azsghetto.ydr", "azsshop.ydr",
            "bankgz.ydr", "bankinkas.ydr", "ems.ydr", "ghinkas.ydr", "greenzone.ytyp",
            "gunshopz.ydr", "inkaskluch.ydr", "keyems.ydr", "kluchelektrik.ydr", "lostgz.ydr",
            "map1.ymap", "subz.ydr", "taxiz.ydr", "zheltigz.ydr",
        };

        private const int BackpackMinModels = 3;

        public static bool IsBackpackModelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!name.EndsWith("_u.ydd", StringComparison.OrdinalIgnoreCase)) return false;
            if (!name.StartsWith("hand_", StringComparison.OrdinalIgnoreCase)) return false;
            var mid = name.Substring(5, name.Length - 5 - 6);
            return mid.Length > 0 && mid.All(char.IsDigit);
        }

        public static int CountBackpackModels(IReadOnlyCollection<string> names)
            => names.Count(n => IsBackpackModelName(LeafName(n)));

        private static string LeafName(string path)
        {
            int i = path.LastIndexOfAny(new[] { '/', '\\' });
            return i >= 0 ? path.Substring(i + 1) : path;
        }

        private static string[] DetectTokens(Kind k) => k switch
        {
            Kind.Zalazy    => ZalazyDetectTokens,
            Kind.GreenZone => GreenZoneDetectTokens,
            _              => Array.Empty<string>(),
        };

        private static bool ClassifiesAs(Kind kind, IReadOnlyCollection<string> names)
            => kind == Kind.Backpack
                ? CountBackpackModels(names) >= BackpackMinModels
                : MatchesAny(names, DetectTokens(kind));

        private static bool ShouldStrip(Kind kind, string name)
        {
            if (kind == Kind.Backpack)
                return IsBackpackModelName(name);
            if (kind == Kind.Zalazy)
                return ZalazyStripTokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
            return GreenZoneStripExact.Contains(name)
                || name.StartsWith("gz_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("mirz_", StringComparison.OrdinalIgnoreCase)
                || name.Contains("greenzone", StringComparison.OrdinalIgnoreCase)
                || name.Contains("processfeliks", StringComparison.OrdinalIgnoreCase)
                || name.Contains("mrzfl", StringComparison.OrdinalIgnoreCase);
        }

        private const long MaxInspectBytes = 64L * 1024 * 1024;
        private const int MaxWalkDepth = 6;

        public sealed record DetectResult(
            bool ForeignZalazy,
            bool ForeignGreenZone,
            IReadOnlyList<string> ZalazyPaths,
            IReadOnlyList<string> GreenZonePaths,
            bool ForeignBackpack,
            IReadOnlyList<string> BackpackPaths);

        public static DetectResult Detect(string updateRpfPath)
        {
            var zal = new List<string>();
            var gz = new List<string>();
            var bp = new List<string>();
            try
            {
                if (!File.Exists(updateRpfPath)) return new DetectResult(false, false, zal, gz, false, bp);
                using var arc = RageArchiveWrapper7.Open(updateRpfPath);
                WalkRpfEntries(arc.Root, "update:/", 0, (path, bin) =>
                {
                    if (IsOwnPath(path)) return;
                    if (bin.Size > MaxInspectBytes) return;
                    var names = CollectLeafNames(bin);
                    if (names.Count == 0) return;
                    if (MatchesAny(names, ZalazyDetectTokens)) zal.Add(path);
                    if (MatchesAny(names, GreenZoneDetectTokens)) gz.Add(path);
                    if (CountBackpackModels(names) >= BackpackMinModels) bp.Add(path);
                });
            }
            catch {}
            return new DetectResult(zal.Count > 0, gz.Count > 0, zal, gz, bp.Count > 0, bp);
        }

        public sealed record RemoveResult(
            int FilesStripped, int RpfsEdited, int RpfsDropped,
            IReadOnlyList<string> EditedRpfNames);

        public static RemoveResult RemoveForeign(string updateRpfPath, Kind kind)
        {
            if (!File.Exists(updateRpfPath))
                throw new FileNotFoundException("update.rpf not found", updateRpfPath);

            using var arc = RageArchiveWrapper7.Open(updateRpfPath);

            var targets = new List<IArchiveBinaryFile>();
            WalkRpfEntries(arc.Root, "update:/", 0, (path, bin) =>
            {
                if (IsOwnPath(path)) return;
                if (bin.Size > MaxInspectBytes) return;
                targets.Add(bin);
            });

            int stripped = 0, edited = 0;
            var editedNames = new List<string>();

            foreach (var bin in targets)
            {
                byte[] original;
                using (var ex = new MemoryStream()) { bin.Export(ex); original = ex.ToArray(); }

                int removedHere = 0; byte[]? rebuilt = null;
                try
                {
                    using var ms = new MemoryStream();
                    ms.Write(original, 0, original.Length);
                    ms.Position = 0;
                    using (var nested = RageArchiveWrapper7.Open(ms, bin.Name, leaveOpen: true))
                    {
                        var names = new List<string>();
                        WalkNames(nested.Root, names, 0);
                        if (!ClassifiesAs(kind, names)) continue;

                        removedHere = DeleteSignatureFiles(nested.Root, kind, 0);
                        if (removedHere == 0) continue;
                        nested.Flush();
                    }
                    rebuilt = ms.ToArray();
                }
                catch { continue;  }

                if (rebuilt is null) continue;
                bin.Import(new MemoryStream(rebuilt));
                bin.IsCompressed = false;
                bin.IsEncrypted = false;
                bin.UncompressedSize = (uint)rebuilt.Length;
                stripped += removedHere;
                edited++;
                if (!string.IsNullOrEmpty(bin.Name)) editedNames.Add(bin.Name);
            }

            if (stripped == 0) return new RemoveResult(0, 0, 0, Array.Empty<string>());

            arc.Flush();
            return new RemoveResult(stripped, edited, 0, editedNames);
        }

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

        private static bool IsOwnPath(string path)
        {
            var v = Normalize(path);
            return OwnPathSuffixes.Any(s => v.EndsWith(s, StringComparison.OrdinalIgnoreCase));
        }

        private static string Normalize(string path)
        {
            var p = path.Replace("%PLATFORM%", "x64", StringComparison.OrdinalIgnoreCase);
            int colon = p.IndexOf(':');
            if (colon >= 0) p = p.Substring(colon + 1);
            p = p.Replace('\\', '/');
            if (!p.StartsWith('/')) p = "/" + p;
            return p.ToLowerInvariant();
        }

        private static bool MatchesAny(IReadOnlyCollection<string> names, string[] tokens)
            => names.Any(n => tokens.Any(t => n.Contains(t, StringComparison.OrdinalIgnoreCase)));

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
            catch {}
            return names;
        }

        private static void WalkNames(IArchiveDirectory dir, List<string> acc, int depth)
        {
            if (depth > 4 || acc.Count > 8000) return;
            foreach (var f in dir.GetFiles())
            {
                acc.Add(f.Name);
                if (f.Name != null && f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)
                    && f is IArchiveBinaryFile bin)
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        bin.Export(ms);
                        ms.Position = 0;
                        using var nested = RageArchiveWrapper7.Open(ms, f.Name, leaveOpen: true);
                        WalkNames(nested.Root, acc, depth + 1);
                    }
                    catch {}
                }
            }
            foreach (var d in dir.GetDirectories())
                WalkNames(d, acc, depth);
        }

        private static int DeleteSignatureFiles(IArchiveDirectory dir, Kind kind, int depth)
        {
            if (depth > 4) return 0;
            int n = 0;
            foreach (var f in dir.GetFiles().ToList())
            {
                var name = f.Name ?? "";
                if (name.Length > 0 && ShouldStrip(kind, name))
                {
                    try { dir.DeleteFile(f); n++; } catch { }
                }
            }
            foreach (var d in dir.GetDirectories())
                n += DeleteSignatureFiles(d, kind, depth + 1);
            return n;
        }
    }
}
