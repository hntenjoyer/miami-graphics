using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services
{
    public static class FileLockFinder
    {
        public sealed record Locker(int Pid, string ProcessName, string Friendly);

        public sealed record LockScan(IReadOnlyList<Locker> Lockers, bool RebootRequired);

        private static readonly (string exe, string noteKey)[] KnownNotes =
        {
            ("MsMpEng",            "locker.windowsDefender"),
            ("SearchProtocolHost", "locker.windowsIndexer"),
            ("SearchIndexer",      "locker.windowsIndexer"),
            ("RockstarService",    "locker.rockstarService"),
            ("SocialClubHelper",   "locker.rockstarSocialClub"),
            ("Launcher",           "locker.rockstarLauncher"),
            ("GTA5",               "locker.gtaV"),
            ("GTA5_Enhanced",      "locker.gtaVEnhanced"),
            ("PlayGTAV",           "locker.gtaV"),
            ("OneDrive",           "locker.oneDriveSync"),
            ("Dropbox",            "locker.dropboxSync"),
            ("steam",              "locker.steam"),
            ("EpicGamesLauncher",  "locker.epicGamesLauncher"),
            ("Miami Graphics",     "locker.anotherMiamiGraphics"),
            ("explorer",           "locker.explorerPreview"),
        };

        public static IReadOnlyList<Locker> FindLockers(string filePath) => Scan(filePath).Lockers;

        public static LockScan Scan(string filePath)
        {
            var result = new List<Locker>();
            bool rebootRequired = false;
            if (string.IsNullOrWhiteSpace(filePath)) return new LockScan(result, false);

            int ownPid = Environment.ProcessId;

            uint handle;
            var key = Guid.NewGuid().ToString("N");
            int res = RmStartSession(out handle, 0, key);
            if (res != 0) return new LockScan(result, false);

            try
            {
                string[] resources = { filePath };
                res = RmRegisterResources(handle, (uint)resources.Length, resources,
                    0, IntPtr.Zero, 0, IntPtr.Zero);
                if (res != 0) return new LockScan(result, false);

                uint pnProcInfoNeeded = 0, pnProcInfo = 0, lpdwRebootReasons = 0;
                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, out lpdwRebootReasons);
                rebootRequired = lpdwRebootReasons != 0;

                if (res == ERROR_MORE_DATA && pnProcInfoNeeded > 0)
                {
                    var info = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;
                    res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, info, out lpdwRebootReasons);
                    rebootRequired = lpdwRebootReasons != 0;
                    if (res == 0)
                    {
                        for (int i = 0; i < pnProcInfo; i++)
                        {
                            int pid = (int)info[i].Process.dwProcessId;
                            if (pid == ownPid) continue;
                            string name = SafeProcessName(pid) ?? info[i].strAppName ?? $"PID {pid}";
                            result.Add(new Locker(pid, name, Annotate(name)));
                        }
                    }
                }
            }
            catch {}
            finally { RmEndSession(handle); }

            var lockers = result
                .GroupBy(l => l.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            return new LockScan(lockers, rebootRequired);
        }

        public static string Describe(string filePath)
        {
            var lockers = FindLockers(filePath);
            if (lockers.Count == 0) return string.Empty;
            var parts = lockers.Select(l =>
                string.IsNullOrEmpty(l.Friendly) ? $"{l.ProcessName} (PID {l.Pid})"
                                                  : $"{l.ProcessName} - {l.Friendly}");
            return Loc.T("errors.lock.heldBy", ("who", string.Join(", ", parts)));
        }

        private static string Annotate(string processName)
        {
            foreach (var (exe, noteKey) in KnownNotes)
                if (processName.StartsWith(exe, StringComparison.OrdinalIgnoreCase)) return Loc.T(noteKey);
            return string.Empty;
        }

        private static string? SafeProcessName(int pid)
        {
            try { return Process.GetProcessById(pid).ProcessName; }
            catch { return null; }
        }

        private const int ERROR_MORE_DATA = 234;
        private const int RM_SESSION_KEY_LEN = 32;

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public uint dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]  public string strServiceShortName;
            public uint ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle,
            uint nFiles, string[] rgsFilenames,
            uint nApplications, IntPtr rgApplications,
            uint nServices, IntPtr rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle,
            out uint pnProcInfoNeeded, ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, out uint lpdwRebootReasons);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);
    }
}
