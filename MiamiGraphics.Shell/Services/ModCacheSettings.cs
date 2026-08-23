using MiamiGraphics.Core.System;

namespace MiamiGraphics.Shell.Services;

public static class ModCacheSettings
{
    public static bool ReuseEnabled => AppDataRoot.ReuseCache;

    public static string? RootOverride => AppDataRoot.Override;

    public static string CacheRoot => AppDataRoot.CacheRoot;

    public static string DefaultCacheRoot => AppDataRoot.DefaultCacheRoot;

    public static string Dir(params string[] sub) => AppDataRoot.Dir(sub);

    public static string BackupDir(params string[] sub) => AppDataRoot.BackupDir(sub);

    public static void Set(bool enabled, string? rootOverride)
        => AppDataRoot.Set(
            reuseCache: enabled,
            rootOverride: rootOverride,
            clearRootOverride: string.IsNullOrWhiteSpace(rootOverride));

    public static long ComputeSizeBytes() => AppDataRoot.CacheSizeBytes();
}
