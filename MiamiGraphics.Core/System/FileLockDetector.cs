using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MiamiGraphics.Core.System;

public static class FileLockDetector
{
    public sealed record LockerProcess(int Pid, string ProcessName, string? FriendlyName);

    private const int RmRebootReasonNone = 0;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;
    private const int ERROR_MORE_DATA = 234;
    private const int ERROR_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public global::System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle,
        uint nFiles, string[] rgsFilenames,
        uint nApplications, [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
        ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    public static IReadOnlyList<LockerProcess> WhoIsLocking(string filePath)
    {
        var ownPid = Environment.ProcessId;
        var key = Guid.NewGuid().ToString();
        if (RmStartSession(out var session, 0, key) != ERROR_SUCCESS)
            return Array.Empty<LockerProcess>();

        try
        {
            var resources = new[] { filePath };
            if (RmRegisterResources(session, (uint)resources.Length, resources, 0, null, 0, null) != ERROR_SUCCESS)
                return Array.Empty<LockerProcess>();

            uint pnProcInfo = 0;
            uint rebootReasons = RmRebootReasonNone;
            var rc = RmGetList(session, out var pnProcInfoNeeded, ref pnProcInfo, null, ref rebootReasons);

            if (rc == ERROR_SUCCESS) return Array.Empty<LockerProcess>();
            if (rc != ERROR_MORE_DATA || pnProcInfoNeeded == 0)
                return Array.Empty<LockerProcess>();

            var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
            pnProcInfo = pnProcInfoNeeded;
            if (RmGetList(session, out _, ref pnProcInfo, processInfo, ref rebootReasons) != ERROR_SUCCESS)
                return Array.Empty<LockerProcess>();

            var result = new List<LockerProcess>((int)pnProcInfo);
            for (int i = 0; i < pnProcInfo; i++)
            {
                var pid = processInfo[i].Process.dwProcessId;
                if (pid == ownPid)
                {

                    Debug.WriteLine($"[file-lock] dropping own pid={pid} from lockers");
                    continue;
                }
                var friendly = processInfo[i].strAppName;
                string procName;
                try
                {
                    using var p = Process.GetProcessById(pid);
                    procName = p.ProcessName + ".exe";
                }
                catch
                {

                    procName = string.IsNullOrEmpty(friendly)
                        ? $"pid:{pid}"
                        : global::System.IO.Path.GetFileName(friendly);
                }
                result.Add(new LockerProcess(pid, procName, string.IsNullOrEmpty(friendly) ? null : friendly));
            }
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[file-lock] WhoIsLocking({filePath}) FAIL: {ex.Message}");
            return Array.Empty<LockerProcess>();
        }
        finally
        {
            try { RmEndSession(session); } catch {  }
        }
    }

    public static int KillProcesses(IEnumerable<int> pids)
    {
        var ownPid = Environment.ProcessId;
        int killed = 0;
        foreach (var pid in pids)
        {
            if (pid == ownPid)
            {
                Debug.WriteLine($"[file-lock] refusing to kill self pid={pid}");
                continue;
            }
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) continue;
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
                killed++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[file-lock] kill pid={pid} FAIL: {ex.Message}");
            }
        }
        return killed;
    }
}
