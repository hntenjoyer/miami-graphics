#nullable enable
using System;
using CodeWalker.GameFiles;

namespace MiamiGraphics.Core.Services;

public static class RpfResourceBudget
{
    public const long GameHardLimit = 100L * 1024 * 1024;

    public const long RefuseAt = 90L * 1024 * 1024;

    public const long WarnAt = 64L * 1024 * 1024;

    public static bool TryReadFlags(byte[]? d, out uint sys, out uint gfx)
    {
        sys = gfx = 0;
        if (!RpfEntrySanity.IsRsc7(d) || d!.Length < 16) return false;
        sys = BitConverter.ToUInt32(d, 8);
        gfx = BitConverter.ToUInt32(d, 12);
        return true;
    }

    public static long MemorySize(byte[]? d)
    {
        if (!TryReadFlags(d, out var sys, out var gfx)) return 0;
        return (long)(uint)RpfResourceFileEntry.GetSizeFromFlags(sys)
             + (long)(uint)RpfResourceFileEntry.GetSizeFromFlags(gfx);
    }

    public static (long System, long Graphics) MemorySizeParts(byte[]? d)
    {
        if (!TryReadFlags(d, out var sys, out var gfx)) return (0, 0);
        return ((long)(uint)RpfResourceFileEntry.GetSizeFromFlags(sys),
                (long)(uint)RpfResourceFileEntry.GetSizeFromFlags(gfx));
    }

    public static string? RejectReasonBySize(string? name, byte[]? bytes)
    {
        var total = MemorySize(bytes);
        if (total <= RefuseAt) return null;
        var (sys, gfx) = MemorySizeParts(bytes);
        return $"ресурс на {Mb(total)} МБ в памяти игры (система {Mb(sys)} + графика {Mb(gfx)}), "
             + $"потолок стриминга {Mb(GameHardLimit)} МБ - игра ответит "
             + "«Oversized file (>100MB)» и упадёт";
    }

    public static bool IsOverBudget(byte[]? bytes) => MemorySize(bytes) > RefuseAt;

    private static long Mb(long bytes) => bytes / (1024 * 1024);
}
