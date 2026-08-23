using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.Core.Services
{
    public static class PerGunTracerCodec
    {
        public static string Encode(IEnumerable<(string weapon, string channel, float sp, float mp)> items)
            => string.Join(";", items
                .Where(i => !string.IsNullOrWhiteSpace(i.weapon))
                .Select(i => $"{i.weapon}={i.channel}:{i.sp.ToString(CultureInfo.InvariantCulture)}:{i.mp.ToString(CultureInfo.InvariantCulture)}"));

        public static List<(string weapon, string channel, float sp, float mp)> Decode(string? packed)
        {
            var result = new List<(string, string, float, float)>();
            if (string.IsNullOrWhiteSpace(packed)) return result;

            foreach (var chunk in packed.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = chunk.IndexOf('=');
                if (eq <= 0) continue;
                string weapon = chunk.Substring(0, eq).Trim();
                if (weapon.Length == 0) continue;

                var parts = chunk.Substring(eq + 1).Split(':');
                string channel = parts.Length > 0 ? parts[0].Trim() : "";
                float sp = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                    ? a : PerGunTracerService.VanillaChanceSp;
                float mp = parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
                    ? b : PerGunTracerService.VanillaChanceMp;
                result.Add((weapon, channel, sp, mp));
            }
            return result;
        }
    }

    public sealed class PerGunTracerService
    {
        public const float VanillaChanceSp = 0.15f;
        public const float VanillaChanceMp = 0.75f;

        private static readonly Regex TracerFxRegex =
            new(@"<TracerFx\s*/>|<TracerFx>\s*([^<]*?)\s*</TracerFx>",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ChanceSpRegex =
            new(@"<TracerFxChanceSP\s+value\s*=\s*""([^""]*)""\s*/>",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ChanceMpRegex =
            new(@"<TracerFxChanceMP\s+value\s*=\s*""([^""]*)""\s*/>",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WeaponNameRegex =
            new(@"<Name>(WEAPON_[A-Z0-9_]+|VEHICLE_WEAPON_[A-Z0-9_]+)</Name>", RegexOptions.Compiled);
        private static readonly Regex ContentFilenameRegex =
            new(@"<filename>([^<]+)</filename>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public sealed class GunSetting
        {
            public string? EffectRule { get; init; }
            public float? ChanceSp { get; init; }
            public float? ChanceMp { get; init; }

            public bool IsEmpty => string.IsNullOrWhiteSpace(EffectRule) && ChanceSp == null && ChanceMp == null;
        }

        public sealed class GunState
        {
            public string WeaponName { get; init; } = "";
            public string EffectRule { get; init; } = "";
            public float? ChanceSp { get; init; }
            public float? ChanceMp { get; init; }
            public string SourcePath { get; init; } = "";
            public bool FromDlcPack { get; init; }
            public bool Assignable { get; init; }
        }

        public sealed class Result
        {
            public bool Success { get; init; }
            public string ErrorMessage { get; init; } = "";
            public int Patched { get; init; }
            public IReadOnlyList<string> NotFound { get; init; } = Array.Empty<string>();
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
            => PatchCustomizationSupport.EnumerateFilesByContent(
                gtaRoot,
                leaf => leaf.EndsWith(".meta", StringComparison.OrdinalIgnoreCase),
                HasTracerFxTag);

        private static bool HasTracerFxTag(string text)
            => text.IndexOf("<TracerFx", StringComparison.OrdinalIgnoreCase) >= 0 && TracerFxRegex.IsMatch(text);

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
                        if (!HasTracerFxTag(text)) continue;
                        targets.Add(new DlcTarget { DlcRpfPath = dlcRpf, UpdateTargetPath = updateTarget, OriginalBytes = bytes });
                    }
                }
            }
            catch { }
            return targets;
        }

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

        public static List<GunState> Scan(string gtaRoot, string cacheDir, Func<string, byte[]?> liveBytesForPath)
        {
            var byWeapon = new Dictionary<string, GunState>(StringComparer.Ordinal);

            void Absorb(string text, string sourcePath, bool fromDlc)
            {
                foreach (var (owner, fx, sp, mp) in ParseOwners(text))
                {
                    if (byWeapon.TryGetValue(owner, out var prev) && !prev.FromDlcPack && fromDlc) continue;
                    byWeapon[owner] = new GunState
                    {
                        WeaponName = owner,
                        EffectRule = fx ?? "",
                        ChanceSp = sp,
                        ChanceMp = mp,
                        SourcePath = sourcePath,
                        FromDlcPack = fromDlc,
                        Assignable = fx != null,
                    };
                }
            }

            foreach (var internalPath in EnumerateWeaponMetaPaths(gtaRoot))
            {
                var live = liveBytesForPath(internalPath);
                if (live is null) continue;
                string text;
                try { text = Utf8NoBom.GetString(live); } catch { continue; }
                Absorb(text, internalPath, fromDlc: false);
            }

            foreach (var dt in EnumerateDlcTargets(gtaRoot, cacheDir))
            {
                string text;
                try { text = Utf8NoBom.GetString(dt.OriginalBytes); } catch { continue; }
                Absorb(text, dt.UpdateTargetPath, fromDlc: true);
            }

            return byWeapon.Values.OrderBy(g => g.WeaponName, StringComparer.Ordinal).ToList();
        }

        private static IEnumerable<(string owner, string? fx, float? sp, float? mp)> ParseOwners(string text)
        {
            var names = new List<(int pos, string val)>();
            foreach (Match m in WeaponNameRegex.Matches(text)) names.Add((m.Index, m.Groups[1].Value));
            if (names.Count == 0) yield break;

            var fxOf = new Dictionary<string, string?>(StringComparer.Ordinal);
            var spOf = new Dictionary<string, float?>(StringComparer.Ordinal);
            var mpOf = new Dictionary<string, float?>(StringComparer.Ordinal);
            var order = new List<string>();

            void Note(string owner)
            {
                if (!fxOf.ContainsKey(owner)) { fxOf[owner] = null; spOf[owner] = null; mpOf[owner] = null; order.Add(owner); }
            }

            foreach (Match m in TracerFxRegex.Matches(text))
            {
                string? owner = OwnerAt(names, m.Index);
                if (owner == null) continue;
                Note(owner);
                fxOf[owner] = m.Groups[1].Success ? m.Groups[1].Value : "";
            }
            foreach (Match m in ChanceSpRegex.Matches(text))
            {
                string? owner = OwnerAt(names, m.Index);
                if (owner == null || !fxOf.ContainsKey(owner)) continue;
                spOf[owner] = ParseFloat(m.Groups[1].Value);
            }
            foreach (Match m in ChanceMpRegex.Matches(text))
            {
                string? owner = OwnerAt(names, m.Index);
                if (owner == null || !fxOf.ContainsKey(owner)) continue;
                mpOf[owner] = ParseFloat(m.Groups[1].Value);
            }

            foreach (var owner in order)
                yield return (owner, fxOf[owner], spOf[owner], mpOf[owner]);
        }

        private static string? OwnerAt(List<(int pos, string val)> names, int at)
        {
            string? owner = null; int best = -1;
            foreach (var n in names) if (n.pos < at && n.pos > best) { best = n.pos; owner = n.val; }
            return owner;
        }

        private static float? ParseFloat(string s)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        private static string Fmt(float v) => v.ToString("0.000000", CultureInfo.InvariantCulture);

        internal static string ApplySettings(
            string text, IReadOnlyDictionary<string, GunSetting> assignments, out int replaced, ISet<string>? touched = null)
        {
            var names = new List<(int pos, string val)>();
            foreach (Match m in WeaponNameRegex.Matches(text)) names.Add((m.Index, m.Groups[1].Value));

            int rep = 0;
            if (names.Count == 0) { replaced = 0; return text; }

            string result = TracerFxRegex.Replace(text, m =>
            {
                string? owner = OwnerAt(names, m.Index);
                if (owner == null || !assignments.TryGetValue(owner, out var s)) return m.Value;
                if (string.IsNullOrWhiteSpace(s.EffectRule)) return m.Value;
                touched?.Add(owner);
                rep++;
                return $"<TracerFx>{s.EffectRule}</TracerFx>";
            });

            names.Clear();
            foreach (Match m in WeaponNameRegex.Matches(result)) names.Add((m.Index, m.Groups[1].Value));

            result = ChanceSpRegex.Replace(result, m =>
            {
                string? owner = OwnerAt(names, m.Index);
                if (owner == null || !assignments.TryGetValue(owner, out var s) || s.ChanceSp == null) return m.Value;
                touched?.Add(owner);
                rep++;
                return $"<TracerFxChanceSP value=\"{Fmt(s.ChanceSp.Value)}\" />";
            });

            names.Clear();
            foreach (Match m in WeaponNameRegex.Matches(result)) names.Add((m.Index, m.Groups[1].Value));

            result = ChanceMpRegex.Replace(result, m =>
            {
                string? owner = OwnerAt(names, m.Index);
                if (owner == null || !assignments.TryGetValue(owner, out var s) || s.ChanceMp == null) return m.Value;
                touched?.Add(owner);
                rep++;
                return $"<TracerFxChanceMP value=\"{Fmt(s.ChanceMp.Value)}\" />";
            });

            replaced = rep;
            return result;
        }

        public Result Apply(string gtaRoot, string cacheDir,
            IReadOnlyDictionary<string, GunSetting> assignments,
            Func<string, byte[]?> liveBytesForPath)
        {
            string workDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics",
                "pergun_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string patchFilesDir = Path.Combine(workDir, "patch_files");
            Directory.CreateDirectory(patchFilesDir);

            var manifest = new DiffManifest { ReduxName = "pergun_tracer", ParsedAt = DateTime.Now, Actions = new List<PatchAction>() };
            try
            {
                var res = StageApply(gtaRoot, cacheDir, assignments, liveBytesForPath, manifest, workDir, patchFilesDir);
                if (!res.Success || res.Patched == 0) return res;
                var inj = Inject(gtaRoot, workDir, manifest, res.Patched);
                return inj.Success ? new Result { Success = true, Patched = res.Patched, NotFound = res.NotFound } : inj;
            }
            catch (Exception ex) { return new Result { Success = false, ErrorMessage = ex.Message }; }
            finally { try { Directory.Delete(workDir, recursive: true); } catch { } }
        }

        public Result StageApply(string gtaRoot, string cacheDir,
            IReadOnlyDictionary<string, GunSetting> assignments,
            Func<string, byte[]?> liveBytesForPath,
            DiffManifest manifest, string workDir, string patchFilesDir)
        {
            if (string.IsNullOrWhiteSpace(gtaRoot))
                return new Result { Success = false, ErrorMessage = Loc.T("error.gtaNotFoundShort") };

            var live = (assignments ?? new Dictionary<string, GunSetting>())
                .Where(kv => kv.Value != null && !kv.Value.IsEmpty)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

            var updateTargets = EnumerateWeaponMetaPaths(gtaRoot);
            var dlcTargets = EnumerateDlcTargets(gtaRoot, cacheDir);
            if (updateTargets.Count == 0 && dlcTargets.Count == 0)
                return new Result { Success = false, ErrorMessage = Loc.T("error.tracerFilesNotFound") };

            Directory.CreateDirectory(patchFilesDir);
            Directory.CreateDirectory(cacheDir);

            var touched = new HashSet<string>(StringComparer.Ordinal);
            int patched = 0;
            {
                foreach (var internalPath in updateTargets)
                {
                    var cur = liveBytesForPath(internalPath);
                    if (cur is null) continue;

                    string cacheFile = Path.Combine(cacheDir, VariantKey(internalPath) + ".bin");
                    byte[] baseline = cur;
                    if (File.Exists(cacheFile))
                    {
                        try { baseline = File.ReadAllBytes(cacheFile); } catch { baseline = cur; }
                    }

                    string baseText;
                    try { baseText = Utf8NoBom.GetString(baseline); } catch { continue; }
                    byte[] desired = Utf8NoBom.GetBytes(ApplySettings(baseText, live, out _, touched));
                    if (ByteEquals(desired, cur)) continue;

                    if (!File.Exists(cacheFile))
                    {
                        try { File.WriteAllBytes(cacheFile, baseline); }
                        catch (Exception ex) { Console.WriteLine($"[PerGunTracer] кеш {internalPath} не записан: {ex.Message}"); }
                    }
                    StageReplace(manifest, workDir, patchFilesDir, internalPath, desired, ActionType.Replace);
                    patched++;
                }

                foreach (var dt in dlcTargets)
                {
                    string baseText;
                    try { baseText = Utf8NoBom.GetString(dt.OriginalBytes); } catch { continue; }
                    byte[] desired = Utf8NoBom.GetBytes(ApplySettings(baseText, live, out int rep, touched));
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

                var notFound = live.Keys.Where(k => !touched.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
                return new Result { Success = true, Patched = patched, NotFound = notFound };
            }
        }

        public Result Restore(string gtaRoot, string cacheDir,
            Func<string, byte[]?> liveBytesForPath,
            Func<string, byte[]?> stockBytesForPath)
        {
            string workDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics",
                "pergun_restore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string patchFilesDir = Path.Combine(workDir, "patch_files");
            Directory.CreateDirectory(patchFilesDir);

            var manifest = new DiffManifest { ReduxName = "pergun_tracer_restore", ParsedAt = DateTime.Now, Actions = new List<PatchAction>() };
            try
            {
                var res = StageRestore(gtaRoot, cacheDir, liveBytesForPath, stockBytesForPath, manifest, workDir, patchFilesDir);
                if (!res.Success) return res;
                if (res.Patched == 0)
                {
                    try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true); } catch { }
                    return res;
                }
                var inj = Inject(gtaRoot, workDir, manifest, res.Patched);
                if (inj.Success)
                {
                    try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true); } catch { }
                }
                return inj;
            }
            catch (Exception ex) { return new Result { Success = false, ErrorMessage = ex.Message }; }
            finally { try { Directory.Delete(workDir, recursive: true); } catch { } }
        }

        public Result StageRestore(string gtaRoot, string cacheDir,
            Func<string, byte[]?> liveBytesForPath,
            Func<string, byte[]?> stockBytesForPath,
            DiffManifest manifest, string workDir, string patchFilesDir)
        {
            if (string.IsNullOrWhiteSpace(gtaRoot))
                return new Result { Success = false, ErrorMessage = Loc.T("error.gtaNotFoundShort") };

            Directory.CreateDirectory(patchFilesDir);
            int patched = 0;
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
                    var cur = liveBytesForPath(internalPath);
                    var stock = stockBytesForPath(internalPath);
                    if (cur is null || stock is null) continue;
                    if (ByteEquals(stock, cur)) continue;
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

                return new Result { Success = true, Patched = patched };
            }
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
                return new Result
                {
                    Success = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(engine.LastError)
                        ? Loc.T("error.injectPatchFalseShort")
                        : engine.LastError!,
                };

            return new Result { Success = true, Patched = patched };
        }
    }
}
