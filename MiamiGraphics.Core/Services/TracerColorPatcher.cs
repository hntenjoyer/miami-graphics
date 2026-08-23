using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MiamiGraphics.Core.I18n;
using RageLib.Resources.Common;
using RageLib.Resources.GTA5;
using RageLib.Resources.GTA5.PC.Particles;

namespace MiamiGraphics.Core.Services
{
    public static class TracerColorPatcher
    {
        private const long SystemBase = 0x50000000;

        private const int OffR = 0x10;
        private const int OffG = 0x14;
        private const int OffB = 0x18;

        public static int PatchTracerColor(string yptPath, byte red, byte green, byte blue,
            IReadOnlyCollection<string>? onlyRules = null)
        {
            HashSet<string>? allow = onlyRules is { Count: > 0 }
                ? new HashSet<string>(onlyRules, StringComparer.OrdinalIgnoreCase)
                : null;
            float r = red / 255f, g = green / 255f, b = blue / 255f;

            var res = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            res.Load(yptPath);
            byte[] sys = res.SystemData;
            var particles = res.ResourceData;

            var rules = particles?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items;
            if (rules == null || rules.Count == 0)
            {
                Console.WriteLine($"[TracerColor] {Path.GetFileName(yptPath)}: нет ParticleRules - skip");
                return 0;
            }

            var done = new HashSet<long>();
            int patched = 0, badmap = 0, rulesHit = 0;

            foreach (var rule in rules)
            {
                string name = rule?.Name?.Value ?? "";
                if (allow != null)
                {
                    if (!allow.Contains(name)) continue;
                }
                else
                {
                    if (name.IndexOf("tracer", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                }

                bool any = false;
                foreach (var bl in new[] { rule.Unknown_128h, rule.Unknown_138h, rule.Unknown_148h, rule.Unknown_158h, rule.Unknown_168h })
                {
                    var behs = bl?.Entries?.data_items;
                    if (behs == null) continue;
                    foreach (var beh in behs)
                    {
                        if (!(beh is BehaviourColour bc)) continue;
                        foreach (var kfp in new[] { bc.KeyframeProp0, bc.KeyframeProp1 })
                        {
                            var vals = kfp?.Unknown_70h?.Entries;
                            if (vals == null) continue;
                            foreach (var v in vals)
                            {
                                long off = v.Position - SystemBase;
                                if (off < 0 || off + 0x20 > sys.Length) { badmap++; continue; }
                                if (!done.Add(v.Position)) { any = true; continue; }

                                float bufR = BitConverter.ToSingle(sys, (int)(off + OffR));
                                if (Math.Abs(bufR - v.Unknown_10h) > 1e-6f) { badmap++; continue; }

                                BitConverter.GetBytes(r).CopyTo(sys, (int)(off + OffR));
                                BitConverter.GetBytes(g).CopyTo(sys, (int)(off + OffG));
                                BitConverter.GetBytes(b).CopyTo(sys, (int)(off + OffB));
                                patched++;
                                any = true;
                            }
                        }
                    }
                }
                if (any) rulesHit++;
            }

            if (badmap > 0)
                throw new InvalidOperationException(
                    $"TracerColor: {badmap} keyframe(s) с несовпавшим self-check - маппинг offset неверен, файл не сохраняем ({yptPath}).");

            if (patched == 0)
            {
                Console.WriteLine($"[TracerColor] {Path.GetFileName(yptPath)}: правил трейсера с цветом не найдено" +
                                  (allow == null ? "" : $" (искали: {string.Join(", ", allow)})") + " - skip");
                return 0;
            }

            SaveRaw(res, sys, yptPath);

            var check = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            check.Load(yptPath);
            int reRules = check.ResourceData?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items?.Count ?? 0;
            if (reRules == 0)
                throw new InvalidOperationException(Loc.T("error.tracerColorReloadEmpty", ("path", yptPath)));

            Console.WriteLine($"[TracerColor] {Path.GetFileName(yptPath)}: RGB({red},{green},{blue}) " +
                              $"rules={rulesHit} keyframes={patched} reload-rules={reRules}");
            return patched;
        }

        private const float SaneValueLimit = 1.0e4f;

        public static int TransferDonorTracerRules(string targetYptPath, string donorYptPath)
        {
            var donorRes = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            donorRes.Load(donorYptPath);
            var tgtRes = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            tgtRes.Load(targetYptPath);

            byte[] dSys = donorRes.SystemData;
            byte[] tSys = tgtRes.SystemData;

            var donorSlots = CollectTracerKfpSlots(donorRes.ResourceData);
            var targetSlots = CollectTracerKfpSlots(tgtRes.ResourceData);

            const int valOff = 0x10, valLen = 0x10;
            int copied = 0, skippedNoMatch = 0, skippedCount = 0, skippedBounds = 0, skippedGuard = 0;
            foreach (var kv in targetSlots)
            {
                if (!donorSlots.TryGetValue(kv.Key, out var dPos)) { skippedNoMatch++; continue; }
                var tPos = kv.Value;
                if (dPos.Count != tPos.Count) { skippedCount++; continue; }

                for (int i = 0; i < tPos.Count; i++)
                {
                    int dv = (int)(dPos[i] - SystemBase) + valOff;
                    int tv = (int)(tPos[i] - SystemBase) + valOff;
                    if (dv < valOff || dv + valLen > dSys.Length || tv < valOff || tv + valLen > tSys.Length)
                    { skippedBounds++; continue; }

                    bool sane = true;
                    for (int b = 0; b < valLen; b += 4)
                    {
                        float f = BitConverter.ToSingle(dSys, dv + b);
                        if (!float.IsFinite(f) || Math.Abs(f) > SaneValueLimit) { sane = false; break; }
                    }
                    if (!sane) { skippedGuard++; continue; }

                    Array.Copy(dSys, dv, tSys, tv, valLen);
                    copied++;
                }
            }

            if (copied == 0)
            {
                Console.WriteLine($"[TracerColor] перенос донора: 0 значений (структуры не совпали/отсеяны: " +
                                  $"no-match={skippedNoMatch}, count-diff={skippedCount}, guard={skippedGuard}) - цель без изменений");
                return 0;
            }

            SaveRaw(tgtRes, tSys, targetYptPath);

            var chk = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            chk.Load(targetYptPath);
            if ((chk.ResourceData?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items?.Count ?? 0) == 0)
                throw new InvalidOperationException(Loc.T("error.transferDonorReloadEmpty", ("path", targetYptPath)));

            Console.WriteLine($"[TracerColor] перенос донора → {Path.GetFileName(targetYptPath)}: " +
                              $"values={copied} (skip: no-match={skippedNoMatch}, count-diff={skippedCount}, guard={skippedGuard}, bounds={skippedBounds})");
            return copied;
        }

        private static Dictionary<string, List<long>> CollectTracerKfpSlots(ParticleEffectsList p)
        {
            var map = new Dictionary<string, List<long>>();

            var prules = p?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items;
            if (prules != null)
            {
                foreach (var rule in prules)
                {
                    string name = rule?.Name?.Value ?? "";
                    if (!IsTracerRuleName(name)) continue;

                    int behIdx = 0;
                    foreach (var bl in new[] { rule.Unknown_128h, rule.Unknown_138h, rule.Unknown_148h, rule.Unknown_158h, rule.Unknown_168h })
                    {
                        var behs = bl?.Entries?.data_items;
                        if (behs == null) continue;
                        foreach (var beh in behs)
                        {
                            var kfps = GetKeyframeProps(beh);
                            for (int ki = 0; ki < kfps.Count; ki++)
                                AddSlot(map, $"P|{name}|{behIdx}|{beh.Type}|{ki}", kfps[ki]);
                            behIdx++;
                        }
                    }
                }
            }

            return map;
        }

        private static void AddSlot(Dictionary<string, List<long>> map, string key, KeyframeProp kfp)
        {
            var vals = kfp?.Unknown_70h?.Entries;
            if (vals == null) return;
            var list = new List<long>();
            foreach (var v in vals) list.Add(v.Position);
            if (list.Count > 0) map[key] = list;
        }

        private static List<KeyframeProp> GetKeyframeProps(object o)
        {
            var f = o.GetType().GetField("KeyframeProps", BindingFlags.Public | BindingFlags.Instance);
            var v = f?.GetValue(o);
            if (v is ResourcePointerArray64<KeyframeProp> arr)
                return arr.data_items ?? new List<KeyframeProp>();
            if (v is ResourcePointerList64<KeyframeProp> lst)
                return lst.Entries?.data_items ?? new List<KeyframeProp>();
            return new List<KeyframeProp>();
        }

        private static bool IsTracerRuleName(string name)
            => name.IndexOf("tracer", StringComparison.OrdinalIgnoreCase) >= 0
               && name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) < 0;

        public static (byte r, byte g, byte b)? TryReadDominantTracerColor(string yptPath)
        {
            var res = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            res.Load(yptPath);
            var rules = res.ResourceData?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items;
            if (rules == null) return null;

            foreach (var rule in rules)
            {
                string name = rule?.Name?.Value ?? "";
                if (name.IndexOf("tracer", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                foreach (var bl in new[] { rule.Unknown_128h, rule.Unknown_138h, rule.Unknown_148h, rule.Unknown_158h, rule.Unknown_168h })
                {
                    var behs = bl?.Entries?.data_items;
                    if (behs == null) continue;
                    foreach (var beh in behs)
                    {
                        if (!(beh is BehaviourColour bc)) continue;
                        var vals = bc.KeyframeProp1?.Unknown_70h?.Entries;
                        if (vals == null) continue;

                        float bestSum = -1f, br = 0, bg = 0, bb = 0;
                        foreach (var v in vals)
                        {
                            float sum = v.Unknown_10h + v.Unknown_14h + v.Unknown_18h;
                            if (sum > bestSum) { bestSum = sum; br = v.Unknown_10h; bg = v.Unknown_14h; bb = v.Unknown_18h; }
                        }
                        if (bestSum >= 0f)
                            return (FloatToByte(br), FloatToByte(bg), FloatToByte(bb));
                    }
                }
            }
            return null;
        }

        private static byte FloatToByte(float f)
        {
            int v = (int)Math.Round(f * 255f);
            return (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
        }

        public static int PatchTracerGradient(string yptPath, IReadOnlyCollection<string> rules,
            IReadOnlyList<(byte r, byte g, byte b)> stops)
        {
            if (rules == null || rules.Count == 0 || stops == null || stops.Count == 0) return 0;
            var allow = new HashSet<string>(rules, StringComparer.OrdinalIgnoreCase);

            var res = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            res.Load(yptPath);
            byte[] sys = res.SystemData;

            var prules = res.ResourceData?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items;
            if (prules == null || prules.Count == 0)
            {
                Console.WriteLine($"[TracerGradient] {Path.GetFileName(yptPath)}: нет ParticleRules - skip");
                return 0;
            }

            var done = new HashSet<long>();
            int patched = 0, badmap = 0, rulesHit = 0;

            foreach (var rule in prules)
            {
                string name = rule?.Name?.Value ?? "";
                if (!allow.Contains(name)) continue;

                bool any = false;
                foreach (var bl in new[] { rule.Unknown_128h, rule.Unknown_138h, rule.Unknown_148h, rule.Unknown_158h, rule.Unknown_168h })
                {
                    var behs = bl?.Entries?.data_items;
                    if (behs == null) continue;
                    foreach (var beh in behs)
                    {
                        if (!(beh is BehaviourColour bc)) continue;
                        foreach (var kfp in new[] { bc.KeyframeProp0, bc.KeyframeProp1 })
                        {
                            var vals = kfp?.Unknown_70h?.Entries;
                            if (vals == null || vals.Count == 0) continue;

                            var ordered = vals.OrderBy(v => v.Unknown_0h).ToList();
                            for (int i = 0; i < ordered.Count; i++)
                            {
                                var v = ordered[i];
                                long off = v.Position - SystemBase;
                                if (off < 0 || off + 0x20 > sys.Length) { badmap++; continue; }
                                if (!done.Add(v.Position)) { any = true; continue; }

                                float bufR = BitConverter.ToSingle(sys, (int)(off + OffR));
                                if (Math.Abs(bufR - v.Unknown_10h) > 1e-6f) { badmap++; continue; }

                                int stopIdx = ordered.Count == 1 ? 0
                                    : (int)Math.Round((double)i / (ordered.Count - 1) * (stops.Count - 1));
                                var s = stops[Math.Clamp(stopIdx, 0, stops.Count - 1)];
                                BitConverter.GetBytes(s.r / 255f).CopyTo(sys, (int)(off + OffR));
                                BitConverter.GetBytes(s.g / 255f).CopyTo(sys, (int)(off + OffG));
                                BitConverter.GetBytes(s.b / 255f).CopyTo(sys, (int)(off + OffB));
                                patched++;
                                any = true;
                            }
                        }
                    }
                }
                if (any) rulesHit++;
            }

            if (badmap > 0)
                throw new InvalidOperationException(
                    $"TracerGradient: {badmap} keyframe(s) с несовпавшим self-check - маппинг offset неверен, файл не сохраняем ({yptPath}).");

            if (patched == 0)
            {
                Console.WriteLine($"[TracerGradient] {Path.GetFileName(yptPath)}: правил не найдено " +
                                  $"(искали: {string.Join(", ", allow)}) - skip");
                return 0;
            }

            SaveRaw(res, sys, yptPath);

            var check = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            check.Load(yptPath);
            int reRules = check.ResourceData?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items?.Count ?? 0;
            if (reRules == 0)
                throw new InvalidOperationException(Loc.T("error.tracerColorReloadEmpty", ("path", yptPath)));

            Console.WriteLine($"[TracerGradient] {Path.GetFileName(yptPath)}: стопов={stops.Count} " +
                              $"rules={rulesHit} keyframes={patched} reload-rules={reRules}");
            return patched;
        }

        public static int PatchTracerSize(string yptPath, IReadOnlyCollection<string> rules,
            float? thickness, float? length)
        {
            if (thickness == null && length == null) return 0;
            if (rules == null || rules.Count == 0) return 0;
            var allow = new HashSet<string>(rules, StringComparer.OrdinalIgnoreCase);

            var res = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            res.Load(yptPath);
            byte[] sys = res.SystemData;

            var prules = res.ResourceData?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items;
            if (prules == null || prules.Count == 0)
            {
                Console.WriteLine($"[TracerSize] {Path.GetFileName(yptPath)}: нет ParticleRules - skip");
                return 0;
            }

            var done = new HashSet<long>();
            int patched = 0, badmap = 0, rulesHit = 0;

            foreach (var rule in prules)
            {
                string name = rule?.Name?.Value ?? "";
                if (!allow.Contains(name)) continue;

                bool any = false;
                foreach (var bl in new[] { rule.Unknown_128h, rule.Unknown_138h, rule.Unknown_148h, rule.Unknown_158h, rule.Unknown_168h })
                {
                    var behs = bl?.Entries?.data_items;
                    if (behs == null) continue;
                    foreach (var beh in behs)
                    {
                        if (!(beh is BehaviourSize bs)) continue;
                        foreach (var kfp in new[] { bs.KeyframeProp0, bs.KeyframeProp1 })
                        {
                            var vals = kfp?.Unknown_70h?.Entries;
                            if (vals == null) continue;
                            foreach (var v in vals)
                            {
                                long off = v.Position - SystemBase;
                                if (off < 0 || off + 0x20 > sys.Length) { badmap++; continue; }
                                if (!done.Add(v.Position)) { any = true; continue; }

                                float bufR = BitConverter.ToSingle(sys, (int)(off + OffR));
                                if (Math.Abs(bufR - v.Unknown_10h) > 1e-6f) { badmap++; continue; }

                                if (thickness != null) BitConverter.GetBytes(thickness.Value).CopyTo(sys, (int)(off + OffR));
                                if (length != null) BitConverter.GetBytes(length.Value).CopyTo(sys, (int)(off + OffG));
                                patched++;
                                any = true;
                            }
                        }
                    }
                }
                if (any) rulesHit++;
            }

            if (badmap > 0)
                throw new InvalidOperationException(
                    $"TracerSize: {badmap} keyframe(s) с несовпавшим self-check - маппинг offset неверен, файл не сохраняем ({yptPath}).");

            if (patched == 0)
            {
                Console.WriteLine($"[TracerSize] {Path.GetFileName(yptPath)}: правил формы не найдено " +
                                  $"(искали: {string.Join(", ", allow)}) - skip");
                return 0;
            }

            SaveRaw(res, sys, yptPath);

            var check = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            check.Load(yptPath);
            int reRules = check.ResourceData?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items?.Count ?? 0;
            if (reRules == 0)
                throw new InvalidOperationException(Loc.T("error.tracerColorReloadEmpty", ("path", yptPath)));

            Console.WriteLine($"[TracerSize] {Path.GetFileName(yptPath)}: " +
                              $"толщина={(thickness?.ToString() ?? "-")} длина={(length?.ToString() ?? "-")} " +
                              $"rules={rulesHit} keyframes={patched} reload-rules={reRules}");
            return patched;
        }

        public static (float thickness, float length)? TryReadSize(string yptPath, IReadOnlyCollection<string> rules)
        {
            var allow = new HashSet<string>(rules ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var res = new ResourceFile_GTA5_pc<ParticleEffectsList>();
            res.Load(yptPath);
            var prules = res.ResourceData?.ParticleRuleDictionary?.ParticleRules?.Entries?.data_items;
            if (prules == null) return null;
            foreach (var rule in prules)
            {
                if (!allow.Contains(rule?.Name?.Value ?? "")) continue;
                foreach (var bl in new[] { rule.Unknown_128h, rule.Unknown_138h, rule.Unknown_148h, rule.Unknown_158h, rule.Unknown_168h })
                {
                    var behs = bl?.Entries?.data_items;
                    if (behs == null) continue;
                    foreach (var beh in behs)
                    {
                        if (!(beh is BehaviourSize bs)) continue;
                        var vals = bs.KeyframeProp0?.Unknown_70h?.Entries;
                        if (vals == null || vals.Count == 0) continue;
                        return (vals[0].Unknown_10h, vals[0].Unknown_14h);
                    }
                }
            }
            return null;
        }

        private static void SaveRaw(ResourceFile_GTA5_pc<ParticleEffectsList> src, byte[] patchedSystem, string outPath)
        {
            var raw = new ResourceFile_GTA5_pc
            {
                Version = src.Version,
                SystemData = patchedSystem,
                GraphicsData = src.GraphicsData,
                SystemPagesDiv16 = src.SystemPagesDiv16, SystemPagesDiv8 = src.SystemPagesDiv8,
                SystemPagesDiv4 = src.SystemPagesDiv4, SystemPagesDiv2 = src.SystemPagesDiv2,
                SystemPagesMul1 = src.SystemPagesMul1, SystemPagesMul2 = src.SystemPagesMul2,
                SystemPagesMul4 = src.SystemPagesMul4, SystemPagesMul8 = src.SystemPagesMul8,
                SystemPagesMul16 = src.SystemPagesMul16, SystemPagesSizeShift = src.SystemPagesSizeShift,
                GraphicsPagesDiv16 = src.GraphicsPagesDiv16, GraphicsPagesDiv8 = src.GraphicsPagesDiv8,
                GraphicsPagesDiv4 = src.GraphicsPagesDiv4, GraphicsPagesDiv2 = src.GraphicsPagesDiv2,
                GraphicsPagesMul1 = src.GraphicsPagesMul1, GraphicsPagesMul2 = src.GraphicsPagesMul2,
                GraphicsPagesMul4 = src.GraphicsPagesMul4, GraphicsPagesMul8 = src.GraphicsPagesMul8,
                GraphicsPagesMul16 = src.GraphicsPagesMul16, GraphicsPagesSizeShift = src.GraphicsPagesSizeShift,
            };
            raw.Save(outPath);
        }
    }
}
