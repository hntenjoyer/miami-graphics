using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.System;

public static class AppDataRoot
{
    public const long DefaultLimitBytes = 12L * 1024 * 1024 * 1024;

    public const long MinLimitBytes = 4L * 1024 * 1024 * 1024;

    public const long MaxLimitBytes = 64L * 1024 * 1024 * 1024;

    private sealed record Persisted(bool ReuseCache, string? RootOverride, long LimitBytes)
    {
        private string? _backupRoot;
        public string ResolvedBackupRoot => _backupRoot ??= ResolveBackupRoot(RootOverride);
    }

    private static readonly object _lock = new();
    private static Persisted? _cached;

    private static string LocalAppData
        => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string SettingsPath
        => Path.Combine(LocalAppData, "MiamiGraphics", "cache_settings.json");

    public static string DefaultBase => Path.Combine(LocalAppData, "MiamiGraphics");

    private static Persisted Load()
    {
        lock (_lock)
        {
            if (_cached is not null) return _cached;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(SettingsPath));
                    var root = doc.RootElement;

                    bool reuse = !root.TryGetProperty("enabled", out var e) || e.ValueKind != JsonValueKind.False;

                    string? over = root.TryGetProperty("rootOverride", out var r) && r.ValueKind == JsonValueKind.String
                        ? r.GetString() : null;
                    if (string.IsNullOrWhiteSpace(over)) over = null;

                    long limit = root.TryGetProperty("limitBytes", out var l) && l.ValueKind == JsonValueKind.Number
                        ? l.GetInt64() : DefaultLimitBytes;
                    limit = ClampLimit(limit);

                    _cached = new Persisted(reuse, over, limit);
                    return _cached;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[data-root] read failed, using defaults: {ex.Message}");
            }
            _cached = new Persisted(true, null, DefaultLimitBytes);
            return _cached;
        }
    }

    private static long ClampLimit(long v)
        => v < MinLimitBytes ? MinLimitBytes : (v > MaxLimitBytes ? MaxLimitBytes : v);

    public static bool ReuseCache => Load().ReuseCache;

    public static string? Override => Load().RootOverride;

    public static string Base => Load().RootOverride ?? DefaultBase;

    public static long LimitBytes => Load().LimitBytes;

    public static string CacheRoot        => Path.Combine(Base, "cache");
    public static string DefaultCacheRoot => Path.Combine(DefaultBase, "cache");
    public static string DefaultBackupRoot => Path.Combine(DefaultBase, "backup");

    public static string WorkRoot => Path.Combine(DefaultBase, "workdir");

    public static string BackupRoot => Load().ResolvedBackupRoot;

    public static bool BackupOnLegacyRoot
        => !string.Equals(BackupRoot, Path.Combine(Base, "backup"), StringComparison.OrdinalIgnoreCase);

    private static readonly string[] ManifestlessBackupDirs =
    {
        "trees", "roads", "bigmap", "rukzak", "graphicsmods",
    };

    private static bool LooksLikeBackupRoot(string path)
    {
        try
        {
            if (File.Exists(Path.Combine(path, "manifest.json"))) return true;
            foreach (var sub in ManifestlessBackupDirs)
            {
                var d = Path.Combine(path, sub);
                if (Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any()) return true;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[data-root] backup probe '{path}': {ex.Message}"); }
        return false;
    }

    private static string ResolveBackupRoot(string? rootOverride)
    {
        var preferred = Path.Combine(rootOverride ?? DefaultBase, "backup");
        if (rootOverride is null) return preferred;
        try
        {
            if (LooksLikeBackupRoot(preferred)) return preferred;
            var legacy = Path.Combine(DefaultBase, "backup");
            if (LooksLikeBackupRoot(legacy))
            {
                Debug.WriteLine($"[data-root] бэкапы ещё на старом корне ({legacy}) - работаем оттуда до переноса");
                return legacy;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[data-root] backup root probe failed: {ex.Message}"); }
        return preferred;
    }

    public static string Dir(params string[] sub) => Resolve(CacheRoot, DefaultCacheRoot, sub);

    public static string BackupDir(params string[] sub)
        => Resolve(BackupRoot, DefaultBackupRoot, sub);

    private static string Resolve(string root, string fallbackRoot, string[] sub)
    {
        foreach (var s in sub)
            if (!SafePath.IsSafeRelative(s) || s.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new InvalidOperationException(Loc.T("error.invalidCacheSubfolderName", ("name", s)));

        var path = root;
        foreach (var s in sub) path = Path.Combine(path, s);
        try { Directory.CreateDirectory(path); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[data-root] create '{path}' failed ({ex.Message}) - falling back to default root");
            path = fallbackRoot;
            foreach (var s in sub) path = Path.Combine(path, s);
            Directory.CreateDirectory(path);
        }
        return path;
    }

    public static void Set(bool? reuseCache = null, string? rootOverride = null,
        bool clearRootOverride = false, long? limitBytes = null)
    {
        rootOverride = string.IsNullOrWhiteSpace(rootOverride) ? null : rootOverride.Trim();
        if (rootOverride is not null)
        {
            try
            {
                var probeDir = Path.Combine(rootOverride, "cache");
                Directory.CreateDirectory(probeDir);
                var probe = Path.Combine(probeDir, ".write_probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    Loc.T("error.folderNotWritable", ("folder", rootOverride), ("detail", ex.Message)));
            }
        }

        bool rootMoved;
        lock (_lock)
        {
            var prev = Load();
            var nextRoot = clearRootOverride ? null : (rootOverride ?? prev.RootOverride);
            var next = new Persisted(
                reuseCache ?? prev.ReuseCache,
                nextRoot,
                limitBytes is long lb ? ClampLimit(lb) : prev.LimitBytes);

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(new
            {
                enabled = next.ReuseCache,
                rootOverride = next.RootOverride,
                limitBytes = next.LimitBytes,
            }, new JsonSerializerOptions { WriteIndented = true });
            var tmpSettings = SettingsPath + ".tmp";
            File.WriteAllText(tmpSettings, json);
            File.Move(tmpSettings, SettingsPath, overwrite: true);

            rootMoved = !string.Equals(prev.RootOverride ?? "", next.RootOverride ?? "",
                StringComparison.OrdinalIgnoreCase);
            _cached = next;
        }

        Debug.WriteLine($"[data-root] saved: reuse={Load().ReuseCache} root={Load().RootOverride ?? "<default>"} " +
            $"limit={Load().LimitBytes}{(rootMoved ? " (корень сменился)" : "")}");
    }

    public static void Invalidate()
    {
        lock (_lock) { _cached = null; }
    }

    public static long DirectorySizeBytes(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch {}
            }
            return total;
        }
        catch { return 0; }
    }

    public static long CacheSizeBytes()  => DirectorySizeBytes(CacheRoot);
    public static long BackupSizeBytes() => DirectorySizeBytes(BackupRoot);

    public static long WorkSizeBytes()   => DirectorySizeBytes(WorkRoot);

    public static long TotalSizeBytes()  => CacheSizeBytes() + BackupSizeBytes() + WorkSizeBytes();

    public static long FreeSpaceBytes(string anyPathOnDisk)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(anyPathOnDisk));
            if (string.IsNullOrEmpty(root)) return 0;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return 0; }
    }
}
