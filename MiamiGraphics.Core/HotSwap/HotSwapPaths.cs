using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.HotSwap
{
    public static class HotSwapPaths
    {
        public static readonly string[] RelPaths =
        {
            @"update\update.rpf",
            @"update\x64\dlcpacks\patchday18ng\dlc.rpf",
        };

        public static string ImageRoot(string gtaRoot) => HotSwapStore.Resolve(gtaRoot);

        public static string ImageRoot(string gtaRoot, HotSwapMethod method, string? storeRoot) =>
            HotSwapStore.RootFor(gtaRoot, method, storeRoot);

        public static string GamePath(string gtaRoot, string rel) => Path.Combine(gtaRoot, rel);
        public static string ModdedPath(string gtaRoot, string rel) => Path.Combine(ImageRoot(gtaRoot), "modded", rel);
        public static string CleanPath(string gtaRoot, string rel) => Path.Combine(ImageRoot(gtaRoot), "clean", rel);

        public static string JournalPath(string gtaRoot) => Path.Combine(ImageRoot(gtaRoot), "journal.json");
        public static string AgentStatePath(string gtaRoot) => Path.Combine(ImageRoot(gtaRoot), "agent.json");
        public static string SetPath(string gtaRoot) => Path.Combine(ImageRoot(gtaRoot), "swapset.json");

        public static bool VolumeSupported(string gtaRoot, out string? reason)
        {
            var mode = HotSwapModeStore.Read();
            return VolumeSupported(gtaRoot, HotSwapPlan.Normalize(mode.Method), mode.StoreRoot, out reason);
        }

        public static bool VolumeSupported(string gtaRoot, HotSwapMethod method, string? storeRoot, out string? reason)
        {
            reason = null;
            try
            {
                var plan = HotSwapPlan.For(method);
                var gameVolume = Path.GetPathRoot(Path.GetFullPath(gtaRoot))!;

                if (plan.Store == HotSwapStoreKind.CustomFolder && !string.IsNullOrWhiteSpace(storeRoot))
                {
                    var full = Path.GetFullPath(storeRoot!);
                    if (IsInside(full, Path.GetFullPath(gtaRoot)))
                    {
                        reason = Loc.T("error.hotSwapStoreInsideGame");
                        return false;
                    }
                }

                var imageRoot = HotSwapStore.RootFor(gtaRoot, method, storeRoot);
                var storeVolume = Path.GetPathRoot(Path.GetFullPath(imageRoot))!;
                bool sameVolume = string.Equals(storeVolume.TrimEnd('\\'), gameVolume.TrimEnd('\\'),
                                                StringComparison.OrdinalIgnoreCase);

                if (plan.RequireSameVolume && !sameVolume)
                {
                    reason = Loc.T("error.hotSwapNeedsSameDrive");
                    return false;
                }

                var storeDrive = new DriveInfo(storeVolume);
                if (plan.RequireSameVolume &&
                    !string.Equals(storeDrive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    reason = Loc.T("error.hotSwapNeedsNtfs", ("fs", storeDrive.DriveFormat));
                    return false;
                }

                long setSize = 0, biggest = 0;
                foreach (var rel in ExistingRelPaths(gtaRoot))
                {
                    var len = new FileInfo(GamePath(gtaRoot, rel)).Length;
                    setSize += len;
                    if (len > biggest) biggest = len;
                }

                long storeNeed = plan.Primitive == HotSwapPrimitive.SafeCopy ? setSize * 2 : setSize;
                long gameNeed = plan.Primitive == HotSwapPrimitive.SafeCopy ? biggest : 0;
                if (sameVolume) { storeNeed += gameNeed; gameNeed = 0; }

                if (storeDrive.AvailableFreeSpace < storeNeed + 512L * 1024 * 1024)
                {
                    reason = Loc.T("error.hotSwapNoRoomForImage",
                        ("drive", storeVolume), ("gb", (storeNeed / 1073741824.0).ToString("N1")));
                    return false;
                }
                if (gameNeed > 0)
                {
                    var gameDrive = new DriveInfo(gameVolume);
                    if (gameDrive.AvailableFreeSpace < gameNeed + 256L * 1024 * 1024)
                    {
                        reason = Loc.T("error.hotSwapNoRoomOnGameDrive",
                            ("drive", gameVolume), ("gb", (gameNeed / 1073741824.0).ToString("N1")));
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        private static bool IsInside(string candidate, string parent)
        {
            var c = candidate.TrimEnd('\\', '/') + "\\";
            var p = parent.TrimEnd('\\', '/') + "\\";
            return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        public static List<string> ExistingRelPaths(string gtaRoot) =>
            RelPaths.Where(r => File.Exists(GamePath(gtaRoot, r)))
                    .Concat(SfxRelPaths(gtaRoot))
                    .ToList();

        private const string SfxDirRel = @"x64\audio\sfx";

        public static List<string> SfxRelPaths(string gtaRoot)
        {
            var res = new List<string>();
            try
            {
                var dir = Path.Combine(gtaRoot, SfxDirRel);
                if (!Directory.Exists(dir)) return res;
                foreach (var f in Directory.EnumerateFiles(dir, "*.rpf"))
                {
                    if (!File.Exists(f + ".bak")) continue;
                    res.Add(Path.Combine(SfxDirRel, Path.GetFileName(f)));
                }
            }
            catch {}
            return res;
        }

        public static string SfxCleanSource(string gtaRoot, string rel) =>
            GamePath(gtaRoot, rel) + ".bak";
    }
}
