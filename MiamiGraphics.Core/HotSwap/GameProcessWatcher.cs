using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MiamiGraphics.Core.HotSwap
{
    public static class GameProcessWatcher
    {
        public static readonly string[] ArmProcesses =
        {
            "PlayGTAV",
            "GTA5", "GTA5_Enhanced", "GTA5_Enhanced_BE",
            "EACLauncher",
            "RageMP", "ragemp_v",
            "altv", "altv-client",
        };

        public static readonly string[] RpfHolderProcesses =
        {
            "GTA5", "GTA5_Enhanced", "GTA5_Enhanced_BE",
            "EACLauncher",
            "RageMP", "ragemp_v",
            "altv", "altv-client",
        };

        public static int? FindRpfHolderProcess() => FindGameProcess(RpfHolderProcesses, null);

        public static readonly string[] ReplaceXArmProcesses =
        {
            "GTA5", "GTA5_Enhanced", "GTA5_Enhanced_BE",
            "EACLauncher", "EasyAntiCheat_Launcher",
            "RageMP", "ragemp_v",
            "altv", "altv-client",
            "Launcher",
        };

        private static readonly string[] AmbiguousNames =
        {
            "Launcher", "EasyAntiCheat_Launcher",
        };

        public static readonly string[] ReturnBlockers =
        {
            "GTA5", "GTA5_Enhanced", "GTA5_Enhanced_BE",
            "SocialClubHelper", "Launcher", "RockstarService", "PlayGTAV", "LauncherPatcher",
        };

        private static readonly string[] LauncherNames = { "Launcher", "RockstarService", "PlayGTAV" };

        public sealed class ProcSnapshot : IDisposable
        {
            public IReadOnlyList<(int Pid, string Name)> All { get; }
            private ProcSnapshot(IReadOnlyList<(int, string)> all) => All = all;

            public static ProcSnapshot Take()
            {
                var list = new List<(int, string)>(512);
                try
                {
                    foreach (var p in Process.GetProcesses())
                    {
                        try { list.Add((p.Id, p.ProcessName)); } catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
                return new ProcSnapshot(list);
            }

            public void Dispose() {}
        }

        public static bool RockstarLauncherRunning()
        {
            using var snap = ProcSnapshot.Take();
            return RockstarLauncherRunning(snap);
        }

        public static bool RockstarLauncherRunning(ProcSnapshot snap)
        {
            foreach (var (_, name) in snap.All)
                foreach (var n in LauncherNames)
                    if (string.Equals(name, n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public readonly record struct GameProcessInfo(int Pid, string Name);

        public static int? FindGameProcess() => FindGameProcess(ArmProcesses, null);

        public static int? FindGameProcess(IReadOnlyList<string> names, string? gtaRoot) =>
            FindGameProcessInfo(names, gtaRoot)?.Pid;

        public static GameProcessInfo? FindGameProcessInfo(IReadOnlyList<string> names, string? gtaRoot)
        {
            using var snap = ProcSnapshot.Take();
            return FindGameProcessInfo(names, gtaRoot, snap);
        }

        public static GameProcessInfo? FindGameProcessInfo(
            IReadOnlyList<string> names, string? gtaRoot, ProcSnapshot snap)
        {
            foreach (var (pid, name) in snap.All)
            {
                try
                {
                    if (!names.Any(n => string.Equals(name, n, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (IsAmbiguous(name))
                    {
                        using var p = Process.GetProcessById(pid);
                        if (!LooksLikeGtaPath(p, gtaRoot)) continue;
                    }
                    return new GameProcessInfo(pid, name);
                }
                catch {}
            }
            return null;
        }

        private static bool IsAmbiguous(string name) =>
            AmbiguousNames.Any(n => string.Equals(name, n, StringComparison.OrdinalIgnoreCase));

        private static bool LooksLikeGtaPath(Process p, string? gtaRoot)
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { }
            if (string.IsNullOrEmpty(path)) return false;

            if (!string.IsNullOrWhiteSpace(gtaRoot) &&
                path!.StartsWith(gtaRoot!.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var marker in new[] { "Rockstar", "Grand Theft Auto", "GTAV", "RAGEMP", "ragemp", "altv" })
                if (path!.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static int KillReturnBlockers(string? gtaRoot)
        {
            int killed = 0;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == Environment.ProcessId) continue;
                    if (!ReturnBlockers.Any(n => string.Equals(p.ProcessName, n, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (IsAmbiguous(p.ProcessName) && !LooksLikeGtaPath(p, gtaRoot)) continue;
                    var name = p.ProcessName;
                    var pid = p.Id;
                    p.Kill();
                    killed++;
                    HotSwapLog.Write("watcher", $"погашен блокер возврата: {name} (pid {pid})");
                }
                catch {}
                finally { p.Dispose(); }
            }
            return killed;
        }
    }
}
