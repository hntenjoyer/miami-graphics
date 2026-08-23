using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.Core.Services
{
    public sealed class TracerStudioService
    {

        public sealed class ChannelTweak
        {
            public string Channel = "";
            public (byte r, byte g, byte b)[]? Gradient;
            public float Thickness = 1f;
            public float Length = 1f;
            public (byte r, byte g, byte b)? Smoke;

            public bool IsEmpty => Gradient == null && Smoke == null
                && Math.Abs(Thickness - 1f) < 0.001f && Math.Abs(Length - 1f) < 0.001f;
        }

        public sealed class Settings
        {
            public Dictionary<string, PerGunTracerService.GunSetting> Guns = new(StringComparer.Ordinal);
            public List<ChannelTweak> Channels = new();

            public bool IsEmpty => Guns.Count == 0 && Channels.All(c => c.IsEmpty);
        }

        public const string CodecTag = "MGTS1";

        public static string Encode(Settings s)
        {
            var parts = new List<string> { CodecTag };
            if (s.Guns.Count > 0)
                parts.Add("W:" + PerGunTracerCodec.Encode(
                    s.Guns.Select(kv => (kv.Key, kv.Value.EffectRule ?? "", kv.Value.ChanceSp ?? 1f, kv.Value.ChanceMp ?? 1f))));
            foreach (var c in s.Channels.Where(c => !c.IsEmpty))
            {
                var seg = new List<string>();
                if (c.Gradient is { Length: 3 })
                    seg.Add("g:" + string.Join(",", c.Gradient.Select(Hex)));
                if (Math.Abs(c.Thickness - 1f) >= 0.001f) seg.Add("t:" + c.Thickness.ToString(CultureInfo.InvariantCulture));
                if (Math.Abs(c.Length - 1f) >= 0.001f) seg.Add("l:" + c.Length.ToString(CultureInfo.InvariantCulture));
                if (c.Smoke != null) seg.Add("s:" + Hex(c.Smoke.Value));
                parts.Add($"C:{c.Channel}={string.Join(";", seg)}");
            }
            return string.Join("|", parts);
        }

        public static Settings Decode(string? packed)
        {
            var s = new Settings();
            if (string.IsNullOrWhiteSpace(packed)) return s;
            var parts = packed.Split('|');
            if (parts.Length == 0 || parts[0] != CodecTag) return s;

            foreach (var part in parts.Skip(1))
            {
                if (part.StartsWith("W:", StringComparison.Ordinal))
                {
                    foreach (var (weapon, channel, sp, mp) in PerGunTracerCodec.Decode(part.Substring(2)))
                        s.Guns[weapon] = new PerGunTracerService.GunSetting { EffectRule = channel, ChanceSp = sp, ChanceMp = mp };
                }
                else if (part.StartsWith("C:", StringComparison.Ordinal))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 2) continue;
                    var tweak = new ChannelTweak { Channel = part.Substring(2, eq - 2).Trim() };
                    if (tweak.Channel.Length == 0) continue;
                    foreach (var seg in part.Substring(eq + 1).Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (seg.StartsWith("g:", StringComparison.Ordinal))
                        {
                            var cols = seg.Substring(2).Split(',').Select(ParseHex).ToArray();
                            if (cols.Length == 3 && cols.All(c => c != null))
                                tweak.Gradient = cols.Select(c => c!.Value).ToArray();
                        }
                        else if (seg.StartsWith("t:", StringComparison.Ordinal) &&
                                 float.TryParse(seg.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var tv))
                            tweak.Thickness = Sane(tv);
                        else if (seg.StartsWith("l:", StringComparison.Ordinal) &&
                                 float.TryParse(seg.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var lv))
                            tweak.Length = Sane(lv);
                        else if (seg.StartsWith("s:", StringComparison.Ordinal))
                        {
                            var c = ParseHex(seg.Substring(2));
                            if (c != null) tweak.Smoke = c;
                        }
                    }
                    s.Channels.Add(tweak);
                }
            }
            return s;
        }

        private static float Sane(float v) => float.IsFinite(v) ? Math.Clamp(v, 0.05f, 50f) : 1f;

        private static string Hex((byte r, byte g, byte b) c) => $"{c.r:X2}{c.g:X2}{c.b:X2}";

        private static (byte, byte, byte)? ParseHex(string s)
        {
            s = s.Trim().TrimStart('#');
            if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
                return null;
            return ((byte)(n >> 16), (byte)((n >> 8) & 0xFF), (byte)(n & 0xFF));
        }

        public sealed class Result
        {
            public bool Success { get; init; }
            public string ErrorMessage { get; init; } = "";
            public int PatchedMetas { get; init; }
            public int PatchedCores { get; init; }
            public IReadOnlyList<string> NotFound { get; init; } = Array.Empty<string>();
            public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
        }

        public Result Apply(string gtaRoot, string metaCacheDir, string coreCacheDir,
            Settings settings, Func<string, byte[]?> liveBytesForPath,
            Action<int, string>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(gtaRoot))
                return new Result { Success = false, ErrorMessage = Loc.T("error.gtaNotFoundShort") };
            settings ??= new Settings();

            string workDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics",
                "tracer_studio_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string patchFilesDir = Path.Combine(workDir, "patch_files");
            Directory.CreateDirectory(patchFilesDir);
            Directory.CreateDirectory(coreCacheDir);

            var manifest = new DiffManifest { ReduxName = "tracer_studio", ParsedAt = DateTime.Now, Actions = new List<PatchAction>() };
            var pergun = new PerGunTracerService();
            try
            {
                progress?.Invoke(20, Loc.T("progress.tracerStudio.metas"));
                PerGunTracerService.Result metaRes;
                if (settings.Guns.Count > 0)
                    metaRes = pergun.StageApply(gtaRoot, metaCacheDir, settings.Guns, liveBytesForPath, manifest, workDir, patchFilesDir);
                else
                    metaRes = pergun.StageRestore(gtaRoot, metaCacheDir, liveBytesForPath,
                        p => liveBytesForPath(p), manifest, workDir, patchFilesDir);
                if (!metaRes.Success) return new Result { Success = false, ErrorMessage = metaRes.ErrorMessage };

                progress?.Invoke(40, Loc.T("progress.tracerStudio.core"));
                int cores = 0;
                var warnings = new List<string>();
                var corePaths = CoreLivePaths(gtaRoot, liveBytesForPath, out var loaded);
                foreach (var p in corePaths)
                {
                    string key = VariantKey(p);
                    string bin = Path.Combine(coreCacheDir, key + ".bin");
                    byte[] baseline;
                    if (File.Exists(bin)) baseline = File.ReadAllBytes(bin);
                    else
                    {
                        baseline = loaded[p];
                        File.WriteAllBytes(bin, baseline);
                        File.WriteAllText(Path.Combine(coreCacheDir, key + ".path"), p);
                    }

                    string tmp = Path.Combine(workDir, key + ".ypt");
                    File.WriteAllBytes(tmp, baseline);

                    try
                    {
                        var usedTex = settings.Channels.Where(c => !c.IsEmpty)
                            .Select(c => TracerChannels.ByEffectRule(c.Channel))
                            .Where(c => c != null)
                            .SelectMany(c => c!.Textures)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (usedTex.Count > 0)
                        {
                            var px = CoreYptTexturePatcher.ExtractPixelData(tmp, usedTex);
                            foreach (var kvp in px.Where(x => x.Value.Length < 1024))
                                warnings.Add(Loc.T("warn.tracerStudio.stubbedTexture", ("texture", kvp.Key)));
                        }
                    }
                    catch { }

                    bool changed = false;
                    foreach (var tw in settings.Channels.Where(c => !c.IsEmpty))
                    {
                        var ch = TracerChannels.ByEffectRule(tw.Channel);
                        if (ch == null) continue;
                        if (tw.Gradient is { Length: 3 })
                            changed |= TracerColorPatcher.PatchTracerGradient(tmp, ch.BodyRules, tw.Gradient) > 0;
                        float? th = Math.Abs(tw.Thickness - 1f) >= 0.001f ? tw.Thickness : null;
                        float? ln = Math.Abs(tw.Length - 1f) >= 0.001f ? tw.Length : null;
                        if (th != null || ln != null)
                        {
                            float baseTh = ch.BaseThickness, baseLn = ch.BaseLength;
                            if (baseTh <= 0 || baseLn <= 0)
                            {
                                var basev = TracerColorPatcher.TryReadSize(tmp, ch.BodyRules);
                                if (basev != null) { baseTh = basev.Value.thickness; baseLn = basev.Value.length; }
                            }
                            if (baseTh > 0 && baseLn > 0)
                                changed |= TracerColorPatcher.PatchTracerSize(tmp, ch.BodyRules,
                                    th != null ? Sane(th.Value) * baseTh : null,
                                    ln != null ? Sane(ln.Value) * baseLn : null) > 0;
                        }
                        if (tw.Smoke != null && ch.SmokeRules.Count > 0)
                            changed |= TracerColorPatcher.PatchTracerColor(tmp,
                                tw.Smoke.Value.r, tw.Smoke.Value.g, tw.Smoke.Value.b, ch.SmokeRules) > 0;
                    }

                    var patchedBytes = File.ReadAllBytes(tmp);
                    var cur = loaded[p];
                    if (!changed && cur.AsSpan().SequenceEqual(baseline)) continue;
                    if (cur.AsSpan().SequenceEqual(patchedBytes)) continue;
                    StageReplace(manifest, workDir, patchFilesDir, p, patchedBytes);
                    cores++;
                }

                int total = metaRes.Patched + cores;
                if (total == 0)
                {
                    CleanupCachesIfDisabled(settings, metaCacheDir, coreCacheDir);
                    return new Result { Success = true, PatchedMetas = 0, PatchedCores = 0, NotFound = metaRes.NotFound, Warnings = warnings };
                }

                progress?.Invoke(55, Loc.T("progress.tracerStudio.inject"));
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
                            ? Loc.T("error.injectPatchFalseShort") : engine.LastError!,
                    };

                CleanupCachesIfDisabled(settings, metaCacheDir, coreCacheDir);
                return new Result { Success = true, PatchedMetas = metaRes.Patched, PatchedCores = cores, NotFound = metaRes.NotFound, Warnings = warnings };
            }
            catch (Exception ex) { return new Result { Success = false, ErrorMessage = ex.Message }; }
            finally { try { Directory.Delete(workDir, recursive: true); } catch { } }
        }

        private static void CleanupCachesIfDisabled(Settings s, string metaCacheDir, string coreCacheDir)
        {
            if (!s.IsEmpty) return;
            try { if (Directory.Exists(metaCacheDir)) Directory.Delete(metaCacheDir, recursive: true); } catch { }
            try { if (Directory.Exists(coreCacheDir)) Directory.Delete(coreCacheDir, recursive: true); } catch { }
        }

        public static void InvalidateCoreBaseline(string coreCacheDir)
        {
            try { if (Directory.Exists(coreCacheDir)) Directory.Delete(coreCacheDir, recursive: true); } catch { }
        }

        private static List<string> CoreLivePaths(string gtaRoot, Func<string, byte[]?> live, out Dictionary<string, byte[]> loaded)
        {
            loaded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in SmokeInstallService.CoreYptTargets)
            {
                var b = live(p);
                if (b != null) loaded[p] = b;
            }
            if (loaded.Count > 0) return loaded.Keys.ToList();

            foreach (var p in PatchCustomizationSupport.FindInternalPathsDeepWhere(
                         gtaRoot, n => n.Equals("core.ypt", StringComparison.OrdinalIgnoreCase), maxHits: 24))
            {
                var b = live(p);
                if (b != null) loaded[p] = b;
            }
            return loaded.Keys.ToList();
        }

        private static void StageReplace(DiffManifest manifest, string workDir, string patchFilesDir,
            string internalPath, byte[] bytes)
        {
            string staged = Path.Combine(patchFilesDir, internalPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.WriteAllBytes(staged, bytes);
            PatchCustomizationSupport.UpsertPatchAction(manifest, workDir, new PatchWorkspaceFile
            {
                TargetPath = internalPath,
                PhysicalPath = staged,
                ActionType = ActionType.Replace,
            });
        }

        private static string VariantKey(string internalPath)
        {
            var chars = internalPath.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars);
        }
    }
}
