using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.Core.Services
{
    public sealed class NoTracerInstallService
    {
        public const string CatNormal = "normal";
        public const string CatVehicle = "vehicle";
        public const string CatMk2Ammo = "mk2ammo";
        public static readonly string[] AllCategories = { CatNormal, CatVehicle, CatMk2Ammo };

        private static readonly HashSet<string> SniperKeepNames = new(StringComparer.Ordinal)
        {
            "WEAPON_HEAVYSNIPER", "WEAPON_HEAVYSNIPER_MK2",
            "WEAPON_MARKSMANRIFLE", "WEAPON_MARKSMANRIFLE_MK2",
            "WEAPON_PRECISIONRIFLE",
        };

        private static readonly Regex TracerFxRegex =
            new(@"<TracerFx>\s*([^<\s][^<]*?)\s*</TracerFx>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WeaponNameRegex =
            new(@"<Name>(WEAPON_[A-Z0-9_]+|VEHICLE_WEAPON_[A-Z0-9_]+)</Name>", RegexOptions.Compiled);
        private static readonly Regex ContentFilenameRegex =
            new(@"<filename>([^<]+)</filename>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public sealed class Result
        {
            public bool Success { get; init; }
            public string ErrorMessage { get; init; } = "";
            public int Patched { get; init; }
        }

        private sealed class DlcTarget
        {
            public string DlcRpfPath = "";
            public string UpdateTargetPath = "";
            public byte[] OriginalBytes = Array.Empty<byte>();
        }

        private static string VariantKey(string internalPath)
        {
            var chars = internalPath.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars);
        }

        public static List<string> EnumerateWeaponMetaPaths(string gtaRoot)
        {
            return PatchCustomizationSupport.EnumerateFilesByContent(
                gtaRoot,
                leaf => leaf.EndsWith(".meta", StringComparison.OrdinalIgnoreCase),
                HasNonEmptyTracerFx);
        }

        private static bool HasNonEmptyTracerFx(string text)
        {
            if (text.IndexOf("<TracerFx>", StringComparison.OrdinalIgnoreCase) < 0) return false;
            return TracerFxRegex.IsMatch(text);
        }

        private static List<DlcTarget> EnumerateDlcTargets(string gtaRoot, string cacheDir)
        {
            var targets = new List<DlcTarget>();
            try
            {
                var updatePaths = new HashSet<string>(
                    PatchCustomizationSupport.EnumerateInternalPaths(gtaRoot, _ => true),
                    StringComparer.OrdinalIgnoreCase);

                var contentXmls = PatchCustomizationSupport
                    .EnumerateInternalPaths(gtaRoot, leaf => leaf.Equals("content.xml", StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.StartsWith("dlc_patch/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var cxPath in contentXmls)
                {
                    var seg = cxPath.Split('/');
                    if (seg.Length < 3) continue;
                    string dlcName = seg[1];
                    string? dlcRpf = ResolveDlcPackRpf(gtaRoot, dlcName);
                    if (dlcRpf == null) continue;

                    var cxBytes = PatchCustomizationSupport.GetCleanBytesForExactPath(gtaRoot, cxPath);
                    if (cxBytes == null) continue;
                    string cx = Utf8NoBom.GetString(cxBytes);

                    var wanted = new List<(string rel, string updateTarget)>();
                    foreach (Match m in ContentFilenameRegex.Matches(cx))
                    {
                        string fn = m.Groups[1].Value.Trim();
                        if (!fn.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                        int colon = fn.IndexOf(":/", StringComparison.Ordinal);
                        string rel = (colon >= 0 ? fn.Substring(colon + 2) : fn).TrimStart('/');
                        if (rel.Length == 0) continue;
                        string updateTarget = $"dlc_patch/{dlcName}/{rel}";
                        bool inUpdate = updatePaths.Contains(updateTarget);
                        bool ours = File.Exists(Path.Combine(cacheDir, VariantKey(updateTarget) + ".added"));
                        if (inUpdate && !ours) continue;
                        wanted.Add((rel, updateTarget));
                    }
                    if (wanted.Count == 0) continue;

                    var extracted = PatchCustomizationSupport.ExtractManyFromArchive(dlcRpf, wanted.Select(w => w.rel));
                    foreach (var (rel, updateTarget) in wanted)
                    {
                        if (!extracted.TryGetValue(rel, out var bytes)) continue;
                        string text;
                        try { text = Utf8NoBom.GetString(bytes); } catch { continue; }
                        if (!HasNonEmptyTracerFx(text)) continue;
                        targets.Add(new DlcTarget { DlcRpfPath = dlcRpf, UpdateTargetPath = updateTarget, OriginalBytes = bytes });
                    }
                }
            }
            catch {}
            return targets;
        }

        public static List<string> ListDlcTracerTargets(string gtaRoot, string cacheDir)
            => EnumerateDlcTargets(gtaRoot, cacheDir).Select(t => $"{t.UpdateTargetPath}  <=  {Path.GetFileName(Path.GetDirectoryName(t.DlcRpfPath))}/dlc.rpf").ToList();

        private static string? ResolveDlcPackRpf(string gtaRoot, string dlcName)
        {
            string[] roots =
            {
                Path.Combine(gtaRoot, "update", "x64", "dlcpacks"),
                Path.Combine(gtaRoot, "x64", "dlcpacks"),
            };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string direct = Path.Combine(root, dlcName, "dlc.rpf");
                if (File.Exists(direct)) return direct;
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root))
                    {
                        if (!string.Equals(Path.GetFileName(dir), dlcName, StringComparison.OrdinalIgnoreCase)) continue;
                        string cand = Path.Combine(dir, "dlc.rpf");
                        if (File.Exists(cand)) return cand;
                    }
                }
                catch { }
            }
            return null;
        }

        private static string CategoryForOwner(string? owner)
        {
            if (owner != null && owner.StartsWith("VEHICLE_WEAPON_", StringComparison.Ordinal)) return CatVehicle;
            if (owner != null && owner.StartsWith("WEAPON_", StringComparison.Ordinal)) return CatNormal;
            return CatMk2Ammo;
        }

        private static string BlankTracerFxByCategory(string text, ISet<string> categories, bool keepSnipers, out int replaced)
        {
            var names = new List<(int pos, string val)>();
            foreach (Match m in WeaponNameRegex.Matches(text)) names.Add((m.Index, m.Groups[1].Value));

            int rep = 0;
            string result = TracerFxRegex.Replace(text, m =>
            {
                string? owner = null; int best = -1;
                foreach (var n in names) if (n.pos < m.Index && n.pos > best) { best = n.pos; owner = n.val; }
                if (keepSnipers && owner != null && SniperKeepNames.Contains(owner)) return m.Value;
                if (categories.Contains(CategoryForOwner(owner))) { rep++; return "<TracerFx />"; }
                return m.Value;
            });
            replaced = rep;
            return result;
        }

        public Result Apply(string gtaRoot, string cacheDir, IReadOnlyCollection<string> categories,
            bool keepSnipers, Func<string, byte[]?> liveBytesForPath)
        {
            if (string.IsNullOrWhiteSpace(gtaRoot))
                return new Result { Success = false, ErrorMessage = Loc.T("error.gtaNotFoundShort") };
            var cats = new HashSet<string>(categories ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (cats.Count == 0)
                return new Result { Success = false, ErrorMessage = Loc.T("error.noCategorySelected") };

            var updateTargets = EnumerateWeaponMetaPaths(gtaRoot);
            var dlcTargets = EnumerateDlcTargets(gtaRoot, cacheDir);
            if (updateTargets.Count == 0 && dlcTargets.Count == 0)
                return new Result { Success = false, ErrorMessage = Loc.T("error.tracerFilesNotFound") };

            string workDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics",
                "notracer_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string patchFilesDir = Path.Combine(workDir, "patch_files");
            Directory.CreateDirectory(patchFilesDir);
            Directory.CreateDirectory(cacheDir);

            var manifest = new DiffManifest { ReduxName = "notracer", ParsedAt = DateTime.Now, Actions = new List<PatchAction>() };
            int patched = 0;
            try
            {
                foreach (var internalPath in updateTargets)
                {
                    var live = liveBytesForPath(internalPath);
                    if (live is null) continue;

                    string cacheFile = Path.Combine(cacheDir, VariantKey(internalPath) + ".bin");
                    byte[] baseline = live;
                    if (File.Exists(cacheFile))
                    {
                        try { baseline = File.ReadAllBytes(cacheFile); } catch { baseline = live; }
                    }

                    string baseText;
                    try { baseText = Utf8NoBom.GetString(baseline); } catch { continue; }
                    byte[] desired = Utf8NoBom.GetBytes(BlankTracerFxByCategory(baseText, cats, keepSnipers, out _));
                    if (ByteEquals(desired, live)) continue;

                    if (!File.Exists(cacheFile))
                    {
                        try { File.WriteAllBytes(cacheFile, baseline); }
                        catch (Exception ex) { Console.WriteLine($"[NoTracer] cache {internalPath} failed: {ex.Message}"); }
                    }
                    StageReplace(manifest, workDir, patchFilesDir, internalPath, desired, ActionType.Replace);
                    patched++;
                }

                foreach (var dt in dlcTargets)
                {
                    string baseText;
                    try { baseText = Utf8NoBom.GetString(dt.OriginalBytes); } catch { continue; }
                    byte[] desired = Utf8NoBom.GetBytes(BlankTracerFxByCategory(baseText, cats, keepSnipers, out int rep));
                    string marker = Path.Combine(cacheDir, VariantKey(dt.UpdateTargetPath) + ".added");

                    if (rep > 0 && !ByteEquals(desired, dt.OriginalBytes))
                    {
                        var cur = liveBytesForPath(dt.UpdateTargetPath);
                        if (cur != null && ByteEquals(cur, desired)) { WriteMarker(marker, dt.UpdateTargetPath); continue; }

                        StageReplace(manifest, workDir, patchFilesDir, dt.UpdateTargetPath, desired, ActionType.Import);
                        WriteMarker(marker, dt.UpdateTargetPath);
                        patched++;
                    }
                    else if (File.Exists(marker))
                    {
                        manifest.Actions.Add(new PatchAction { Type = ActionType.Delete, TargetPath = dt.UpdateTargetPath });
                        try { File.Delete(marker); } catch { }
                        patched++;
                    }
                }

                if (patched == 0)
                    return new Result { Success = true, Patched = 0 };

                return Inject(gtaRoot, workDir, manifest, patched);
            }
            catch (Exception ex) { return new Result { Success = false, ErrorMessage = ex.Message }; }
            finally { try { Directory.Delete(workDir, recursive: true); } catch { } }
        }

        public Result Restore(string gtaRoot, string cacheDir,
            Func<string, byte[]?> liveBytesForPath,
            Func<string, byte[]?> stockBytesForPath)
        {
            if (string.IsNullOrWhiteSpace(gtaRoot))
                return new Result { Success = false, ErrorMessage = Loc.T("error.gtaNotFoundShort") };

            string workDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics",
                "notracer_restore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string patchFilesDir = Path.Combine(workDir, "patch_files");
            Directory.CreateDirectory(patchFilesDir);

            var manifest = new DiffManifest { ReduxName = "notracer_restore", ParsedAt = DateTime.Now, Actions = new List<PatchAction>() };
            int patched = 0;
            try
            {
                var targets = EnumerateWeaponMetaPaths(gtaRoot);
                var pathsByKey = targets.ToDictionary(VariantKey, p => p, StringComparer.OrdinalIgnoreCase);

                var cachedBins = Directory.Exists(cacheDir) ? Directory.GetFiles(cacheDir, "*.bin") : Array.Empty<string>();
                var addedMarkers = Directory.Exists(cacheDir) ? Directory.GetFiles(cacheDir, "*.added") : Array.Empty<string>();

                var toRestore = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var cf in cachedBins)
                {
                    string key = Path.GetFileNameWithoutExtension(cf);
                    if (!pathsByKey.TryGetValue(key, out var internalPath)) continue;
                    try { toRestore[internalPath] = File.ReadAllBytes(cf); } catch { }
                }
                foreach (var internalPath in targets)
                {
                    if (toRestore.ContainsKey(internalPath)) continue;
                    var live = liveBytesForPath(internalPath);
                    var stock = stockBytesForPath(internalPath);
                    if (live is null || stock is null) continue;
                    if (ByteEquals(stock, live)) continue;
                    toRestore[internalPath] = stock;
                }
                foreach (var kv in toRestore)
                {
                    StageReplace(manifest, workDir, patchFilesDir, kv.Key, kv.Value, ActionType.Replace);
                    patched++;
                }

                foreach (var mk in addedMarkers)
                {
                    string target;
                    try { target = File.ReadAllText(mk).Trim(); } catch { continue; }
                    if (string.IsNullOrWhiteSpace(target)) continue;
                    manifest.Actions.Add(new PatchAction { Type = ActionType.Delete, TargetPath = target });
                    patched++;
                }

                if (patched == 0)
                {
                    try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true); } catch { }
                    return new Result { Success = true, Patched = 0 };
                }

                var res = Inject(gtaRoot, workDir, manifest, patched);
                if (res.Success)
                {
                    try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true); } catch { }
                }
                return res;
            }
            catch (Exception ex) { return new Result { Success = false, ErrorMessage = ex.Message }; }
            finally { try { Directory.Delete(workDir, recursive: true); } catch { } }
        }

        private static void StageReplace(DiffManifest manifest, string workDir, string patchFilesDir,
            string internalPath, byte[] bytes, ActionType action)
        {
            string staged = Path.Combine(patchFilesDir, internalPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.WriteAllBytes(staged, bytes);
            PatchCustomizationSupport.UpsertPatchAction(manifest, workDir, new PatchWorkspaceFile
            {
                TargetPath = internalPath,
                PhysicalPath = staged,
                ActionType = action,
            });
        }

        private static void WriteMarker(string markerPath, string targetPath)
        {
            try { File.WriteAllText(markerPath, targetPath); } catch { }
        }

        private static bool ByteEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static Result Inject(string gtaRoot, string workDir, DiffManifest manifest, int patched)
        {
            PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);
            File.WriteAllText(Path.Combine(workDir, "manifest.json"),
                global::System.Text.Json.JsonSerializer.Serialize(manifest,
                    new global::System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            var engine = new RpfInjectEngine(gtaRoot);
            if (!engine.InjectPatch(workDir))
                return new Result { Success = false, ErrorMessage = string.IsNullOrWhiteSpace(engine.LastError) ? Loc.T("error.injectPatchFalseShort") : engine.LastError! };

            return new Result { Success = true, Patched = patched };
        }
    }
}
