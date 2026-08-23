#nullable enable
using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace MiamiGraphics.Core.System;

public static class MemoryRelease
{
    private static readonly Stopwatch _sinceLast = Stopwatch.StartNew();
    private static bool _everRan;

    private static readonly TimeSpan MinGap = TimeSpan.FromSeconds(30);

    public static Action<string>? Logger { get; set; }

    public static void Trim(string reason, bool force = false)
    {
        if (!force && _everRan && _sinceLast.Elapsed < MinGap) return;
        _everRan = true;
        _sinceLast.Restart();

        try
        {
            long before = GC.GetTotalMemory(false);
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            long after = GC.GetTotalMemory(false);

            TrimWorkingSet();

            Logger?.Invoke($"память: {reason} - куча {Mb(before)} -> {Mb(after)} МБ");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[memory] trim failed: {ex.Message}");
        }
    }

    private static long Mb(long bytes) => bytes / (1024 * 1024);

    private static void TrimWorkingSet()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
        }
        catch {}
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, int min, int max);
}
