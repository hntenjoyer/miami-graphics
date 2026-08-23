using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MiamiGraphics.Core.HotSwap
{
    public static class RockstarRepairWatch
    {
        public static readonly string[] LauncherProcesses =
        {
            "Launcher",
            "RockstarService",
            "SocialClubHelper",
            "LauncherPatcher",
            "RockstarErrorHandler",
            "PlayGTAV",
        };

        private static readonly string[] PartialSuffixes =
        {
            ".part", ".partial", ".download", ".rgl_tmp",
        };

        private const string OwnTempSuffix = ".mgswap.tmp";

        private const int PartialFreshMinutes = 10;

        public readonly record struct Snapshot(IReadOnlyList<string> Processes, IReadOnlyList<string> Partials)
        {
            public bool RepairInProgress => Partials.Count > 0;

            public bool LauncherAlive => Processes.Any(p =>
                p.StartsWith("Launcher ", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("LauncherPatcher ", StringComparison.OrdinalIgnoreCase));

            public string Describe() =>
                $"процессы Rockstar: [{(Processes.Count == 0 ? "нет" : string.Join(", ", Processes))}], " +
                $"следы докачки: [{(Partials.Count == 0 ? "нет" : string.Join(", ", Partials))}]";
        }

        public static Snapshot Probe(string gtaRoot)
        {
            var procs = new List<string>();
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (LauncherProcesses.Any(n => string.Equals(p.ProcessName, n, StringComparison.OrdinalIgnoreCase)))
                            procs.Add($"{p.ProcessName} (pid {p.Id})");
                    }
                    catch {}
                    finally { p.Dispose(); }
                }
            }
            catch { }

            var partials = new List<string>();
            try
            {
                var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rel in HotSwapPaths.RelPaths)
                {
                    var dir = Path.GetDirectoryName(HotSwapPaths.GamePath(gtaRoot, rel));
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) dirs.Add(dir!);
                }
                var now = DateTime.UtcNow;
                foreach (var dir in dirs)
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (f.EndsWith(OwnTempSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!PartialSuffixes.Any(s => f.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                        try
                        {
                            var fi = new FileInfo(f);
                            if (!fi.Exists) continue;
                            if ((now - fi.LastWriteTimeUtc).TotalMinutes > PartialFreshMinutes) continue;
                            partials.Add(Path.GetFileName(f));
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return new Snapshot(procs, partials);
        }

        public sealed class RepairMark
        {
            public string? Rel { get; set; }
            public string? WasStamp { get; set; }
            public string? NowStamp { get; set; }
            public string? Who { get; set; }
            public string? AtUtc { get; set; }
        }

        private static string MarkPath(string gtaRoot) =>
            Path.Combine(HotSwapPaths.ImageRoot(gtaRoot), "repair.json");

        public static void Mark(string gtaRoot, string rel, string wasStamp, string nowStamp, string who)
        {
            try
            {
                if (Read(gtaRoot) is not null) return;
                var p = MarkPath(gtaRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, JsonSerializer.Serialize(new RepairMark
                {
                    Rel = rel,
                    WasStamp = wasStamp,
                    NowStamp = nowStamp,
                    Who = who,
                    AtUtc = DateTime.UtcNow.ToString("O"),
                }, new JsonSerializerOptions { WriteIndented = true }));
                HotSwapLog.Write("rockstar",
                    $"обнаружен ремонт Rockstar: {rel} переписан под нами (был {wasStamp}, стал {nowStamp}); {who}. " +
                    "Образ собран под старую версию игры - подмена запрещена до пересборки.");
            }
            catch (Exception ex) { HotSwapLog.Write("rockstar", "не записался маркер ремонта", ex); }
        }

        public static RepairMark? Read(string gtaRoot)
        {
            try
            {
                var p = MarkPath(gtaRoot);
                if (!File.Exists(p)) return null;
                return JsonSerializer.Deserialize<RepairMark>(File.ReadAllText(p));
            }
            catch { return null; }
        }

        public static void Clear(string gtaRoot)
        {
            try { File.Delete(MarkPath(gtaRoot)); } catch { }
        }
    }
}
