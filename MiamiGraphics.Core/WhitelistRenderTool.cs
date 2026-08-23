using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Services;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core
{
    public static class WhitelistRenderTool
    {
        private static readonly string[] Targets =
        {
            "w_sg_assaultshotgun", "w_sg_heavyshotgun", "w_pi_revolver",
            "w_sb_smgmk2", "w_sb_minismg", "w_sb_microsmg",
            "w_ar_specialcarbinemk2", "w_ar_specialcarbine", "w_ar_carbineriflemk2",
            "w_ar_carbinerifle", "w_ar_heavyrifleh", "w_sr_heavysniper",
            "w_sr_heavysnipermk2", "w_mg_combatmgmk2", "w_sr_marksmanriflemk2",
            "w_sr_precisionrifle",
        };

        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "w_sr_precisionrifle_reh", "w_sr_precisionrifle" },
        };

        private const string WSUF = "/x64/models/cdimages/weapons.rpf/";
        private static readonly (string Target, string Rel)[] ExplicitPaths =
        {
            ("w_sg_assaultshotgun",    "update/x64/dlcpacks/patchday8ng/dlc.rpf" + WSUF + "w_sg_assaultshotgun.ydr"),
            ("w_sg_heavyshotgun",      "update/x64/dlcpacks/patchday8ng/dlc.rpf" + WSUF + "w_sg_heavyshotgun.ydr"),
            ("w_pi_revolver",          "update/x64/dlcpacks/mpapartment/dlc.rpf" + WSUF + "w_pi_revolver.ydr"),
            ("w_sb_smgmk2",            "update/x64/dlcpacks/mpgunrunning/dlc.rpf" + WSUF + "w_sb_smgmk2.ydr"),
            ("w_sb_minismg",           "update/x64/dlcpacks/mpbiker/dlc.rpf" + WSUF + "w_sb_minismg.ydr"),
            ("w_sb_microsmg",          "update/x64/dlcpacks/patchday8ng/dlc.rpf" + WSUF + "w_sb_microsmg.ydr"),
            ("w_ar_specialcarbinemk2", "update/update.rpf/dlc_patch/mpchristmas2017" + WSUF + "w_ar_specialcarbinemk2.ydr"),
            ("w_ar_specialcarbine",    "update/x64/dlcpacks/patchday8ng/dlc.rpf" + WSUF + "w_ar_specialcarbine.ydr"),
            ("w_ar_carbineriflemk2",   "update/update.rpf/dlc_patch/mpgunrunning" + WSUF + "w_ar_carbineriflemk2.ydr"),
            ("w_ar_carbinerifle",      "update/x64/dlcpacks/patchday8ng/dlc.rpf" + WSUF + "w_ar_carbinerifle.ydr"),
            ("w_ar_heavyrifleh",       "update/update.rpf/dlc_patch/mpsecurity" + WSUF + "w_ar_heavyrifleh.ydr"),
            ("w_sr_heavysniper",       "update/x64/dlcpacks/patchday8ng/dlc.rpf" + WSUF + "w_sr_heavysniper.ydr"),
            ("w_sr_heavysnipermk2",    "update/update.rpf/dlc_patch/mpgunrunning" + WSUF + "w_sr_heavysnipermk2.ydr"),
            ("w_mg_combatmgmk2",       "update/update.rpf/dlc_patch/mpgunrunning" + WSUF + "w_mg_combatmgmk2.ydr"),
            ("w_sr_marksmanriflemk2",  "update/update.rpf/dlc_patch/mpchristmas2017" + WSUF + "w_sr_marksmanriflemk2.ydr"),
            ("w_sr_precisionrifle",    "update/update.rpf/dlc_patch/mpsum2" + WSUF + "w_sr_precisionrifle_reh.ydr"),
        };

        private static int _ydrSeen, _rpfOpened, _rpfFail;
        private static readonly List<string> _ydrSample = new();

        private static readonly Dictionary<string, Dictionary<string, byte[]>> _ytdPool =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Dictionary<string, string>> _ytdSrc =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool YtdMatches(string ytdBase, string target)
            => ytdBase.Equals(target, StringComparison.OrdinalIgnoreCase)
            || ytdBase.StartsWith(target + "+", StringComparison.OrdinalIgnoreCase)
            || ytdBase.StartsWith(target + "_", StringComparison.OrdinalIgnoreCase);

        public static async Task RunAsync(string gtaPath, string outDir)
        {
            Directory.CreateDirectory(outDir);
            bool debug = false;
            var found = new Dictionary<string, Wf>(StringComparer.OrdinalIgnoreCase);

            var sources = new List<string>();
            if (outDir.Equals("DEBUG", StringComparison.OrdinalIgnoreCase))
            {
                debug = true; outDir = Path.Combine(Path.GetTempPath(), "wl_out");
                Directory.CreateDirectory(outDir);
                sources.Add(Path.Combine(gtaPath, "update", "update.rpf"));
            }
            else
            {
                foreach (var f in Directory.EnumerateFiles(gtaPath, "*.rpf")) sources.Add(f);
                var upd = Path.Combine(gtaPath, "update", "update.rpf");
                if (File.Exists(upd)) sources.Add(upd);
                var dlcRoot = Path.Combine(gtaPath, "update", "x64", "dlcpacks");
                if (Directory.Exists(dlcRoot))
                    foreach (var d in Directory.EnumerateDirectories(dlcRoot))
                    {
                        var dlc = Path.Combine(d, "dlc.rpf");
                        if (File.Exists(dlc)) sources.Add(dlc);
                    }
            }

            Console.WriteLine($"[scan] {sources.Count} source RPFs, looking for {Targets.Length} weapons...");
            foreach (var src in sources)
            {
                try
                {
                    var rel = src.StartsWith(gtaPath, StringComparison.OrdinalIgnoreCase)
                        ? src.Substring(gtaPath.Length).TrimStart('\\', '/')
                        : Path.GetFileName(src);
                    using var arc = RageArchiveWrapper7.OpenRead(src);
                    ScanDir(arc.Root, found, rel.Replace('\\', '/'));
                }
                catch (Exception ex) { Console.WriteLine($"[scan] skip {Path.GetFileName(src)}: {ex.Message}"); }
                Console.WriteLine($"[scan] {Path.GetFileName(Path.GetDirectoryName(src) ?? src)}/{Path.GetFileName(src)} -> {found.Count}/{Targets.Length}");
            }

            Console.WriteLine($"\n[scan] found {found.Count}/{Targets.Length} models");
            Console.WriteLine($"[diag] .ydr seen: {_ydrSeen} | nested rpf opened: {_rpfOpened} | rpf open-fail: {_rpfFail}");
            Console.WriteLine("[diag] sample .ydr names: " + string.Join(", ", _ydrSample.Take(25)));
            Console.WriteLine("\n========== PER-WEAPON SOURCE PATHS ==========");
            foreach (var t in Targets)
            {
                Console.WriteLine("\n### " + t);
                if (found.TryGetValue(t, out var wf))
                    Console.WriteLine("   .ydr   <- " + wf.YdrSrc);
                else
                    Console.WriteLine("   .ydr   <- (NOT FOUND)");
                if (_ytdSrc.TryGetValue(t, out var srcMap))
                {
                    foreach (var name in srcMap.Keys.OrderBy(k => k))
                        Console.WriteLine("   " + name + "  <- " + srcMap[name]);
                }
                else Console.WriteLine("   (no .ytd collected)");
            }
            Console.WriteLine("\n=============================================\n");
            if (debug) return;
            foreach (var t in Targets) if (!found.ContainsKey(t)) Console.WriteLine("   MISSING: " + t);

            var tmp = Path.Combine(Path.GetTempPath(), "wl_render");
            Directory.CreateDirectory(tmp);
            int ok = 0;
            foreach (var t in Targets)
            {
                if (!found.TryGetValue(t, out var v)) continue;
                try
                {
                    var work = Path.Combine(tmp, t);
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                    Directory.CreateDirectory(work);

                    var ydr = Path.Combine(work, t + ".ydr");
                    File.WriteAllBytes(ydr, v.Ydr);

                    var ytdPaths = new List<string>();
                    if (_ytdPool.TryGetValue(t, out var pool))
                    {
                        foreach (var name in pool.Keys.OrderBy(k => k.Contains("+hi", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                        {
                            var yp = Path.Combine(work, name);
                            File.WriteAllBytes(yp, pool[name]);
                            ytdPaths.Add(yp);
                        }
                    }

                    var glb = Path.Combine(work, t + ".glb");
                    bool conv = ytdPaths.Count > 0
                        ? await YdrToGltfConverter.ConvertAsync(ydr, glb, ytdPaths)
                        : await YdrToGltfConverter.ConvertAsync(ydr, glb);
                    if (!conv || !File.Exists(glb))
                    {
                        Console.WriteLine($"   CONV FAIL: {t}");
                        continue;
                    }
                    var png = Path.Combine(outDir, t + ".png");
                    var rend = await GlbToPngRenderer.RenderAsync(glb, png);
                    Console.WriteLine((rend ? "   OK   " : "   REND FAIL ") + t + $" (ytd={ytdPaths.Count})");
                    if (rend) ok++;
                }
                catch (Exception ex) { Console.WriteLine($"   ERR {t}: {ex.Message}"); }
            }
            Console.WriteLine($"\n[done] {ok}/{Targets.Length} rendered -> {outDir}");
        }

        public static async Task RunExplicitAsync(string gtaPath, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var tmp = Path.Combine(Path.GetTempPath(), "wl_render");
            Directory.CreateDirectory(tmp);
            int ok = 0;
            foreach (var (target, rel) in ExplicitPaths)
            {
                try
                {
                    var (ydrBytes, ytds) = ExtractExplicit(gtaPath, rel);
                    Console.WriteLine($"[{target}] ydr={ydrBytes.Length}B  ytd={ytds.Count}" +
                        (ytds.Any(y => y.Name.Contains("+hi", StringComparison.OrdinalIgnoreCase)) ? " (+hi)" : ""));

                    var work = Path.Combine(tmp, target);
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                    Directory.CreateDirectory(work);

                    var ydr = Path.Combine(work, target + ".ydr");
                    File.WriteAllBytes(ydr, ydrBytes);

                    var ytdPaths = new List<string>();
                    foreach (var (name, bytes) in ytds.OrderBy(y => y.Name.Contains("+hi", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                    {
                        var yp = Path.Combine(work, name);
                        File.WriteAllBytes(yp, bytes);
                        ytdPaths.Add(yp);
                    }

                    var glb = Path.Combine(work, target + ".glb");
                    bool conv = ytdPaths.Count > 0
                        ? await YdrToGltfConverter.ConvertAsync(ydr, glb, ytdPaths)
                        : await YdrToGltfConverter.ConvertAsync(ydr, glb);
                    if (!conv || !File.Exists(glb)) { Console.WriteLine("   CONV FAIL: " + target); continue; }

                    var png = Path.Combine(outDir, target + ".png");
                    var rend = await GlbToPngRenderer.RenderAsync(glb, png);
                    Console.WriteLine((rend ? "   OK   " : "   REND FAIL ") + target);
                    if (rend) ok++;
                }
                catch (Exception ex) { Console.WriteLine($"   ERR {target}: {ex.Message}"); }
            }
            Console.WriteLine($"\n[done] {ok}/{ExplicitPaths.Length} rendered -> {outDir}");
        }

        public static bool IsKnownVanilla(string internalName) =>
            ExplicitPaths.Any(p => p.Target.Equals(internalName, StringComparison.OrdinalIgnoreCase));

        public static (string YdrName, byte[] Ydr, List<(string Name, byte[] Bytes)> Ytds)
            ExtractVanilla(string gtaPath, string internalName)
        {
            var entry = ExplicitPaths.FirstOrDefault(p =>
                p.Target.Equals(internalName, StringComparison.OrdinalIgnoreCase));
            if (entry.Rel == null)
                throw new FileNotFoundException(Loc.T("error.notInStandardGunList", ("name", internalName)));

            EnsureKeysLoaded();

            var ydrName = entry.Rel.Replace('\\', '/').Split('/')[^1];

            (byte[] Ydr, List<(string Name, byte[] Bytes)> Ytds)? spare = null;
            var tried = new List<string>();
            var errors = new List<string>();
            string? lockedFile = null;

            foreach (var rel in Candidates(gtaPath, entry.Rel, ydrName))
            {
                if (tried.Contains(rel, StringComparer.OrdinalIgnoreCase)) continue;
                tried.Add(rel);
                try
                {
                    var got = ExtractExplicit(gtaPath, rel);
                    if (got.Ydr == null || got.Ydr.Length == 0) continue;
                    if (got.Ytds.Count > 0) return (ydrName, got.Ydr, got.Ytds);
                    spare ??= got;
                }
                catch (Exception ex)
                {
                    if (tried.Count <= 2)
                    {
                        var msg = ex.Message.Replace('\n', ' ');
                        errors.Add(msg.Length > 140 ? msg[..140] + "…" : msg);
                    }
                    if (lockedFile == null && IsFileLocked(ex))
                        lockedFile = rel.Replace('\\', '/').Split('/')[0];
                }
            }

            if (spare != null) return (ydrName, spare.Value.Ydr, spare.Value.Ytds);

            if (lockedFile != null)
                throw new IOException(Loc.T("error.gameFileLocked", ("file", lockedFile)));

            throw new FileNotFoundException(
                Loc.T("error.modelNotFoundAnywhere", ("model", ydrName), ("places", tried.Count)) +
                (errors.Count > 0 ? " [" + string.Join("; ", errors) + "]" : ""));
        }

        private static bool IsFileLocked(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return true;
            if (ex is FileNotFoundException or DirectoryNotFoundException) return false;
            if (ex is not IOException io) return false;
            int code = io.HResult & 0xFFFF;
            return code is 32 or 33 || io.HResult == unchecked((int)0x80070020)
                                    || io.HResult == unchecked((int)0x80070021);
        }

        private static void EnsureKeysLoaded()
        {
            var keys = RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS;
            if (keys is { Length: > 0 } && keys[0] is { Length: > 0 }) return;
            throw new InvalidOperationException(Loc.T("error.gtaKeysNotLoaded"));
        }

        private static IEnumerable<string> Candidates(string gtaPath, string explicitRel, string ydrName)
        {
            yield return explicitRel;

            const string PatchRoot = "update/update.rpf/dlc_patch/";
            const string PacksRoot = "update/x64/dlcpacks/";
            var twin = PackNameAfter(explicitRel, PatchRoot);
            if (twin != null)
                yield return PacksRoot + twin + "/dlc.rpf" + WSUF + ydrName;
            else
            {
                twin = PackNameAfter(explicitRel, PacksRoot);
                if (twin != null)
                    yield return PatchRoot + twin + WSUF + ydrName;
            }

            foreach (var dir in WeaponDirs(gtaPath))
                yield return dir + "/" + ydrName;
        }

        private static string? PackNameAfter(string rel, string prefix)
        {
            if (!rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var rest = rel.Substring(prefix.Length);
            var slash = rest.IndexOf('/');
            return slash <= 0 ? null : rest.Substring(0, slash);
        }

        private static IEnumerable<string> WeaponDirs(string gtaPath)
        {
            const string W = "/x64/models/cdimages/weapons.rpf";

            foreach (var pack in SubDirNames(Path.Combine(gtaPath, "update", "update.rpf"), "dlc_patch"))
                yield return "update/update.rpf/dlc_patch/" + pack + W;

            var dlcRoot = Path.Combine(gtaPath, "update", "x64", "dlcpacks");
            if (Directory.Exists(dlcRoot))
                foreach (var d in Directory.EnumerateDirectories(dlcRoot).OrderBy(x => x))
                    if (File.Exists(Path.Combine(d, "dlc.rpf")))
                        yield return "update/x64/dlcpacks/" + Path.GetFileName(d) + "/dlc.rpf" + W;

            string[] roots;
            try { roots = Directory.GetFiles(gtaPath, "x64*.rpf"); } catch { yield break; }
            Array.Sort(roots, StringComparer.OrdinalIgnoreCase);

            foreach (var f in roots)
                yield return Path.GetFileName(f) + "/models/cdimages/weapons.rpf";

            foreach (var f in roots)
                foreach (var pack in SubDirNames(f, "dlcpacks"))
                    yield return Path.GetFileName(f) + "/dlcpacks/" + pack + "/dlc.rpf" + W;
        }

        private static List<string> SubDirNames(string archivePath, string parentDir)
        {
            var res = new List<string>();
            if (!File.Exists(archivePath)) return res;
            try
            {
                using var arc = RageArchiveWrapper7.OpenRead(archivePath);
                var parent = arc.Root.GetDirectories()
                    .FirstOrDefault(d => d.Name.Equals(parentDir, StringComparison.OrdinalIgnoreCase));
                if (parent == null) return res;
                foreach (var d in parent.GetDirectories()) res.Add(d.Name);
            }
            catch { }
            return res;
        }

        private static (byte[] Ydr, List<(string Name, byte[] Bytes)> Ytds) ExtractExplicit(string gtaPath, string rel)
        {
            var comps = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            int i0 = -1;
            for (int i = 0; i < comps.Length; i++)
                if (comps[i].EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) { i0 = i; break; }
            if (i0 < 0) throw new Exception("no .rpf in path: " + rel);

            var diskFile = Path.Combine(gtaPath, Path.Combine(comps.Take(i0 + 1).ToArray()));
            if (!File.Exists(diskFile))
                throw new FileNotFoundException(
                    Loc.T("error.gameFileMissing", ("file", string.Join("/", comps.Take(i0 + 1)))),
                    diskFile);

            var disposables = new List<IDisposable>();
            try
            {
                var arc = RageArchiveWrapper7.OpenRead(diskFile);
                disposables.Add(arc);
                IArchiveDirectory cur = arc.Root;

                for (int j = i0 + 1; j < comps.Length - 1; j++)
                {
                    var comp = comps[j];
                    if (comp.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                    {
                        var rpfFile = cur.GetFiles()
                            .FirstOrDefault(f => f.Name.Equals(comp, StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile
                            ?? throw new FileNotFoundException(Loc.T("error.gamePartMissingInside", ("part", comp)));
                        var s = rpfFile.GetStream();
                        disposables.Add(s);
                        var nested = RageArchiveWrapper7.Open(s, rpfFile.Name, true);
                        disposables.Add(nested);
                        cur = nested.Root;
                    }
                    else
                    {
                        cur = cur.GetDirectories()
                            .FirstOrDefault(d => d.Name.Equals(comp, StringComparison.OrdinalIgnoreCase))
                            ?? throw new FileNotFoundException(Loc.T("error.gameFolderMissingInside", ("folder", comp)));
                    }
                }

                var ydrName = comps[^1];
                var files = cur.GetFiles();
                var ydrFile = files.FirstOrDefault(f => f.Name.Equals(ydrName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException(Loc.T("error.gameModelMissing", ("model", ydrName)));
                var ydrBytes = ReadAll(ydrFile);

                var weaponBase = ydrName.Substring(0, ydrName.Length - ".ydr".Length);
                var ytds = files
                    .Where(f => f.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
                    .Where(f => YtdMatches(f.Name.Substring(0, f.Name.Length - ".ytd".Length), weaponBase))
                    .Select(f => (f.Name, ReadAll(f)))
                    .ToList();
                return (ydrBytes, ytds);
            }
            finally
            {
                for (int k = disposables.Count - 1; k >= 0; k--)
                    try { disposables[k].Dispose(); } catch { }
            }
        }

        private static void ScanDir(IArchiveDirectory dir, Dictionary<string, Wf> found, string path)
        {
            IList<IArchiveFile> files;
            try { files = dir.GetFiles(); } catch { return; }

            foreach (var f in files)
            {
                var n = f.Name;
                if (n.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
                {
                    _ydrSeen++;
                    if (_ydrSample.Count < 25 && !_ydrSample.Contains(n)) _ydrSample.Add(n);
                    bool hi = n.EndsWith("_hi.ydr", StringComparison.OrdinalIgnoreCase);
                    var baseName = hi
                        ? n.Substring(0, n.Length - "_hi.ydr".Length)
                        : n.Substring(0, n.Length - ".ydr".Length);
                    var match = Targets.FirstOrDefault(t => t.Equals(baseName, StringComparison.OrdinalIgnoreCase));
                    if (match == null && Aliases.TryGetValue(baseName, out var aliasTarget)) match = aliasTarget;
                    if (match != null && (!found.TryGetValue(match, out var ex) || (!hi && ex.Hi)))
                    {
                        try { found[match] = new Wf { Ydr = ReadAll(f), Hi = hi, YdrSrc = path + "/" + n }; }
                        catch (Exception e) { Console.WriteLine($"   read fail {n}: {e.Message}"); }
                    }
                }

                if (n.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
                {
                    var ytdBase = n.Substring(0, n.Length - ".ytd".Length);
                    foreach (var t in Targets)
                    {
                        if (!YtdMatches(ytdBase, t)) continue;
                        if (!_ytdPool.TryGetValue(t, out var pool))
                            _ytdPool[t] = pool = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                        if (!_ytdSrc.TryGetValue(t, out var srcMap))
                            _ytdSrc[t] = srcMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (!pool.ContainsKey(n))
                        {
                            try { pool[n] = ReadAll(f); srcMap[n] = path + "/" + n; } catch { }
                        }
                        else
                        {
                            srcMap[n] += "  |dup: " + path + "/" + n;
                        }
                    }
                }

                if (n.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && f is IArchiveBinaryFile rpfBin)
                {
                    try
                    {
                        using var s = rpfBin.GetStream();
                        using var nested = RageArchiveWrapper7.Open(s, rpfBin.Name, true);
                        _rpfOpened++;
                        ScanDir(nested.Root, found, path + "/" + n);
                    }
                    catch { _rpfFail++; }
                }
            }

            foreach (var sub in dir.GetDirectories()) ScanDir(sub, found, path + "/" + sub.Name);
        }

        private sealed class Wf
        {
            public byte[] Ydr = Array.Empty<byte>();
            public bool Hi;
            public string YdrSrc = "";
        }

        private static byte[] ReadAll(IArchiveFile f)
        {
            using var ms = new MemoryStream();
            f.Export(ms);
            return ms.ToArray();
        }
    }
}
