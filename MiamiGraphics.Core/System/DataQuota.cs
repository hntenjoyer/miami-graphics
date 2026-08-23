using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.System;

public sealed record QuotaReservation(
    bool Ok,
    long NeedBytes,
    long UsedBytes,
    long LimitBytes,
    long ProtectedBytes,
    long ShortfallBytes,
    string? Message);

public static class SweepReason
{
    public const string UnderLimit = "under_limit";
    public const string Freed = "freed";
    public const string NoVictims = "no_victims";
    public const string DeleteFailed = "delete_failed";
    public const string ProtectedOverLimit = "protected_over_limit";
    public const string Busy = "busy";
    public const string Concurrent = "concurrent";
    public const string Error = "error";
}

public sealed record QuotaHolder(string Name, long Bytes);

public sealed record QuotaSweepResult(
    long LimitBytes,
    long BeforeBytes,
    long AfterBytes,
    long FreedBytes,
    int DeletedEntries,
    long ProtectedBytes,
    bool StillOverLimit,
    string Reason = SweepReason.Freed
);

public static class DataQuota
{
    private static readonly string[] Tier1CacheDirs =
    {
        "assets", "packzips", "redux_patches", "customize_donor_cache", "armor_library", "bigmap",
        "ffdec_minimap_as",
    };

    private static readonly string[] Tier0WorkPrefixes =
    {
        "Redux_analysis_", "Redux_redux_", "Redux_update_", "_unlocked", "_debug",
        "catalog_inject_", "customize_inject_",
    };

    private static readonly string[] ProtectedWorkDirs =
    {
        "backup", "Gunpacks", "GunpacksAdmin",
    };

    private static readonly string[] Tier2CacheDirs = { "redux" };

    private static readonly string[] ProtectedCacheDirs =
    {
        "notracer", "rukzak", "gunskins", "gunsmith",
        "pergun_tracer", "pergun_core",
    };

    private static readonly string[] ProtectedBackupDirs =
    {
        "trees", "roads", "bigmap", "rukzak", "graphicsmods",
    };

    private sealed record Victim(string Path, bool IsDirectory, long Bytes, DateTime LastUse);

    private static readonly TimeSpan RecentGrace = TimeSpan.FromMinutes(5);

    public static Action<string>? Logger;

    private static void Say(string message)
    {
        Debug.WriteLine($"[quota] {message}");
        try { Logger?.Invoke(message); } catch {}
    }

    private static string Human(long bytes)
    {
        if (bytes < 0) bytes = 0;
        double gb = bytes / 1024.0 / 1024 / 1024;
        return gb >= 1.0
            ? $"{gb:0.0} ГБ"
            : $"{bytes / (1024 * 1024)} МБ";
    }

    private static string HumanUi(long bytes)
    {
        if (bytes < 0) bytes = 0;
        double gb = bytes / 1024.0 / 1024 / 1024;
        return gb >= 1.0
            ? Loc.T("misc.sizeGb", ("value", gb.ToString("0.0")))
            : Loc.T("misc.sizeMb", ("value", bytes / (1024 * 1024)));
    }

    private static readonly object _sweepLock = new();
    private static bool _sweepRunning;

    public static bool SweepInProgress { get { lock (_sweepLock) return _sweepRunning; } }

    private static int _busy;

    public static bool Busy => global::System.Threading.Volatile.Read(ref _busy) > 0;

    public static IDisposable Hold(string reason) => new BusyHold(reason, sweepAfter: false);

    private static int _pending;

    public static bool Pending => global::System.Threading.Volatile.Read(ref _pending) > 0;

    public static bool BusyOrPending => Busy || Pending;

    public static IDisposable HoldPending(string reason) => new PendingHold(reason);

    private sealed class PendingHold : IDisposable
    {
        private readonly string _reason;
        private int _released;
        public PendingHold(string reason)
        {
            _reason = reason;
            global::System.Threading.Interlocked.Increment(ref _pending);
            Debug.WriteLine($"[quota] в очереди: {reason}");
        }
        public void Dispose()
        {
            if (global::System.Threading.Interlocked.Exchange(ref _released, 1) != 0) return;
            global::System.Threading.Interlocked.Decrement(ref _pending);
            Debug.WriteLine($"[quota] очередь разошлась ({_reason})");
        }
    }

    public static IDisposable HoldAndSweepAfter(string reason) => new BusyHold(reason, sweepAfter: true);

    private sealed class BusyHold : IDisposable
    {
        private readonly string _reason;
        private readonly bool _sweepAfter;
        private int _released;
        public BusyHold(string reason, bool sweepAfter)
        {
            _reason = reason;
            _sweepAfter = sweepAfter;
            global::System.Threading.Interlocked.Increment(ref _busy);
            Debug.WriteLine($"[quota] уборка приостановлена: {reason}");
        }
        public void Dispose()
        {
            if (global::System.Threading.Interlocked.Exchange(ref _released, 1) != 0) return;
            global::System.Threading.Interlocked.Decrement(ref _busy);
            Debug.WriteLine($"[quota] уборка снова разрешена ({_reason} закончилась)");
            if (_sweepAfter) SweepInBackground($"после операции «{_reason}»");
        }
    }

    public static QuotaSweepResult Sweep(long? limitOverride = null, bool ignoreProtectedFloor = false,
        TimeSpan? waitForConcurrent = null)
    {
        var limit = limitOverride ?? AppDataRoot.LimitBytes;

        if (Busy)
        {
            Say("идёт установка или бэкап - уборку пропускаем");
            var busyNow = AppDataRoot.TotalSizeBytes();
            return new QuotaSweepResult(limit, busyNow, busyNow, 0, 0, ComputeProtectedBytes(),
                busyNow > limit, SweepReason.Busy);
        }

        bool concurrent;
        lock (_sweepLock)
        {
            if (_sweepRunning && waitForConcurrent is TimeSpan wait && wait > TimeSpan.Zero)
            {
                var deadline = DateTime.UtcNow + wait;
                while (_sweepRunning)
                {
                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) break;
                    global::System.Threading.Monitor.Wait(_sweepLock, left);
                }
            }
            concurrent = _sweepRunning;
            if (!concurrent) _sweepRunning = true;
        }

        if (concurrent)
        {
            var now = AppDataRoot.TotalSizeBytes();
            Say("уборка уже идёт в другом потоке - второй проход не запускаем");
            return new QuotaSweepResult(limit, now, now, 0, 0, ComputeProtectedBytes(),
                now > limit, SweepReason.Concurrent);
        }

        try
        {
            var before = AppDataRoot.TotalSizeBytes();
            var protectedBytes = ComputeProtectedBytes();

            if (before <= limit)
                return new QuotaSweepResult(limit, before, before, 0, 0, protectedBytes, false,
                    SweepReason.UnderLimit);

            if (protectedBytes >= limit && !ignoreProtectedFloor)
            {
                Say($"лимит {Human(limit)} ниже неприкосновенного {Human(protectedBytes)} - " +
                    "уборку пропускаем, чистить нечего");
                return new QuotaSweepResult(limit, before, before, 0, 0, protectedBytes, true,
                    SweepReason.ProtectedOverLimit);
            }

            long freed = 0;
            int deleted = 0;
            int failed = 0;
            long current = before;
            bool interrupted = false;

            foreach (var tier in EnumerateTiers())
            {
                if (current <= limit || interrupted) break;
                foreach (var v in tier)
                {
                    if (current <= limit) break;
                    if (Busy)
                    {
                        Say("началась установка или бэкап - уборку прерываю на полпути");
                        interrupted = true;
                        break;
                    }
                    if (!TryDelete(v)) { failed++; continue; }
                    freed += v.Bytes;
                    current -= v.Bytes;
                    deleted++;
                }
            }

            var after = AppDataRoot.TotalSizeBytes();
            var still = after > limit;

            var reason =
                deleted > 0 ? SweepReason.Freed :
                interrupted ? SweepReason.Busy :
                failed  > 0 ? SweepReason.DeleteFailed :
                              SweepReason.NoVictims;

            if (still)
            {
                Say($"после уборки всё ещё {Human(after)} при цели {Human(limit)}; " +
                    $"защищено {Human(protectedBytes)}" +
                    (interrupted ? " (проход прерван начавшейся установкой)" : "") +
                    (failed > 0 ? $"; не удалось удалить единиц: {failed}" : ""));
            }
            Say($"уборка [{reason}]: {Human(before)} -> {Human(after)}, " +
                $"удалено {deleted} единиц на {Human(freed)}");

            return new QuotaSweepResult(limit, before, after, freed, deleted, protectedBytes, still, reason);
        }
        catch (Exception ex)
        {
            Say($"уборка упала целиком: {ex.Message}");
            var now = AppDataRoot.TotalSizeBytes();
            return new QuotaSweepResult(limit, now, now, 0, 0, 0, now > limit, SweepReason.Error);
        }
        finally
        {
            lock (_sweepLock)
            {
                _sweepRunning = false;
                global::System.Threading.Monitor.PulseAll(_sweepLock);
            }
        }
    }

    public static QuotaReservation TryReserve(long needBytes, string what)
    {
        var limit = AppDataRoot.LimitBytes;
        try
        {
            if (needBytes <= 0)
                return new QuotaReservation(true, 0, 0, limit, 0, 0, null);

            var used = AppDataRoot.TotalSizeBytes();
            if (used + needBytes <= limit)
                return new QuotaReservation(true, needBytes, used, limit, 0, 0, null);

            var protectedBytes = ComputeProtectedBytes();
            var reclaimable    = ReclaimableBytes();
            var floor          = used - reclaimable;

            if (floor + needBytes > limit)
            {
                var ripeLater = ReclaimableBytes(TimeSpan.Zero);
                if (used - ripeLater + needBytes <= limit)
                {
                    Say($"«{what}»: место есть, но удаляемые записи ещё моложе " +
                        $"{RecentGrace.TotalMinutes:0} мин - пускаем, подметём после установки " +
                        $"(нужно {Human(needBytes)}, занято {Human(used)}/{Human(limit)}, " +
                        $"дозреет {Human(ripeLater)})");
                    return new QuotaReservation(true, needBytes, used, limit, protectedBytes, 0, null);
                }

                var shortfall = used - ripeLater + needBytes - limit;
                var msg = Loc.T("error.quotaNotEnoughRoom",
                    ("what", what), ("need", HumanUi(shortfall)), ("used", HumanUi(used)),
                    ("limit", HumanUi(limit)), ("locked", HumanUi(protectedBytes)));
                Say($"ОТКАЗ «{what}»: нужно {Human(needBytes)}, занято {Human(used)}/{Human(limit)}, " +
                    $"удаляемого {Human(reclaimable)}, защищено {Human(protectedBytes)}, не хватает {Human(shortfall)}");
                return new QuotaReservation(false, needBytes, used, limit, protectedBytes, shortfall, msg);
            }

            if (Busy)
            {
                Say($"«{what}»: нужно {Human(needBytes)}, занято {Human(used)}/{Human(limit)}; " +
                    "прибраться сейчас нельзя (идёт другая операция) - пускаем, уборка догонит после");
                return new QuotaReservation(true, needBytes, used, limit, protectedBytes, 0, null);
            }

            var target = Math.Max(0, limit - needBytes);
            var swept  = Sweep(limitOverride: target, ignoreProtectedFloor: true);
            var after  = swept.AfterBytes;
            Say($"«{what}»: под {Human(needBytes)} освободили {Human(swept.FreedBytes)}, " +
                $"стало {Human(after)} из {Human(limit)}");

            if (after + needBytes <= limit)
                return new QuotaReservation(true, needBytes, after, limit, protectedBytes, 0, null);

            if (Busy || SweepInProgress)
            {
                Say($"«{what}»: уборку перебила соседняя операция - пускаем, догоним после");
                return new QuotaReservation(true, needBytes, after, limit, protectedBytes, 0, null);
            }

            var left = after + needBytes - limit;
            return new QuotaReservation(false, needBytes, after, limit, protectedBytes, left,
                Loc.T("error.quotaSweepBlocked", ("what", what), ("need", HumanUi(left))));
        }
        catch (Exception ex)
        {
            Say($"проверка места под «{what}» сорвалась ({ex.Message}) - пропускаем загрузку");
            return new QuotaReservation(true, needBytes, 0, limit, 0, 0, null);
        }
    }

    public static long EnsureRoom(long needBytes, string what)
    {
        try
        {
            if (Busy) return 0;
            var limit = AppDataRoot.LimitBytes;
            var used  = AppDataRoot.TotalSizeBytes();
            if (used + Math.Max(0, needBytes) <= limit) return 0;
            var target = Math.Max(0, limit - Math.Max(0, needBytes));
            var r = Sweep(limitOverride: target, ignoreProtectedFloor: true);
            Say($"под «{what}» ({Human(needBytes)}) освободили {Human(r.FreedBytes)}");
            return r.FreedBytes;
        }
        catch (Exception ex)
        {
            Say($"подготовка места под «{what}» сорвалась: {ex.Message}");
            return 0;
        }
    }

    public static long ReclaimableBytes(TimeSpan? graceOverride = null)
    {
        long total = 0;
        try
        {
            foreach (var tier in EnumerateTiers(graceOverride))
                foreach (var v in tier) total += v.Bytes;
        }
        catch (Exception ex) { Say($"подсчёт удаляемого сорвался: {ex.Message}"); }
        return total;
    }

    public static void SweepInBackground(string reason)
    {
        global::System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var r = Sweep();
                if (r.FreedBytes > 0 || r.StillOverLimit)
                    Say($"фоновая уборка ({reason}) [{r.Reason}]: освобождено {Human(r.FreedBytes)}, " +
                        $"занято {Human(r.AfterBytes)} из {Human(r.LimitBytes)}");
            }
            catch (Exception ex) { Say($"фоновая уборка ({reason}) упала: {ex.Message}"); }
        });
    }

    public static void Touch(string path)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (Directory.Exists(path)) Directory.SetLastAccessTimeUtc(path, now);
            else if (File.Exists(path)) File.SetLastAccessTimeUtc(path, now);
        }
        catch {}
    }

    private static IEnumerable<IReadOnlyList<Victim>> EnumerateTiers(TimeSpan? grace = null)
    {
        var g = grace ?? RecentGrace;
        yield return CollectWorkDirVictims(g);
        yield return CollectCacheVictims(Tier1CacheDirs, g);
        yield return CollectCacheVictims(Tier2CacheDirs, g);
        yield return CollectDeadBackupVictims(g);
    }

    private static IReadOnlyList<Victim> CollectWorkDirVictims(TimeSpan grace)
    {
        var list = new List<Victim>();
        var work = AppDataRoot.WorkRoot;
        if (!Directory.Exists(work)) return list;
        try
        {
            foreach (var entry in Directory.EnumerateDirectories(work))
            {
                var name = Path.GetFileName(entry);
                if (ProtectedWorkDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (!Tier0WorkPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                var bytes = AppDataRoot.DirectorySizeBytes(entry);
                if (bytes <= 0) continue;
                var lastUse = LastUseUtc(entry, true);
                if (TooFresh(lastUse, grace)) continue;
                list.Add(new Victim(entry, true, bytes, lastUse));
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[quota] enumerate workdir '{work}': {ex.Message}"); }
        return list.OrderBy(v => v.LastUse).ToList();
    }

    private static IReadOnlyList<Victim> CollectCacheVictims(string[] subdirs, TimeSpan grace)
    {
        var list = new List<Victim>();
        var cacheRoot = AppDataRoot.CacheRoot;
        foreach (var sub in subdirs)
        {
            var dir = Path.Combine(cacheRoot, sub);
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    if (entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                    if (Path.GetFileName(entry).StartsWith(".", StringComparison.Ordinal)) continue;

                    var isDir = Directory.Exists(entry);
                    var bytes = isDir ? AppDataRoot.DirectorySizeBytes(entry) : SafeLength(entry);
                    if (bytes <= 0 && !isDir) continue;
                    var lastUse = LastUseUtc(entry, isDir);
                    if (TooFresh(lastUse, grace)) continue;
                    list.Add(new Victim(entry, isDir, bytes, lastUse));
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[quota] enumerate '{dir}': {ex.Message}"); }
        }
        return list.OrderBy(v => v.LastUse).ToList();
    }

    private static IReadOnlyList<Victim> CollectDeadBackupVictims(TimeSpan grace)
    {
        var list = new List<Victim>();
        var backupRoot = AppDataRoot.BackupRoot;
        var alive = ReadManifestPaths(backupRoot);

        DateTime cutoff;
        try
        {
            var manifestPath = Path.Combine(backupRoot, "manifest.json");
            if (!File.Exists(manifestPath)) return list;
            cutoff = File.GetLastWriteTimeUtc(manifestPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[quota] manifest mtime failed ({ex.Message}) - тир 3 пропускаем");
            return list;
        }

        if (alive.Count == 0)
        {
            Say("манифест бэкапов не дал ни одного пути (битый?) - тир 3 пропускаем, " +
                "чтобы не снести снимок игры");
            return list;
        }

        foreach (var sub in new[] { "clean", "snapshot" })
        {
            var dir = Path.Combine(backupRoot, sub);
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (alive.Contains(Path.GetFullPath(f))) continue;
                    if (f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                    DateTime born;
                    try { born = File.GetLastWriteTimeUtc(f); } catch { continue; }
                    if (born > cutoff) continue;
                    var bytes = SafeLength(f);
                    if (bytes <= 0) continue;
                    var lastUse = LastUseUtc(f, false);
                    if (TooFresh(lastUse, grace)) continue;
                    list.Add(new Victim(f, false, bytes, lastUse));
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[quota] enumerate '{dir}': {ex.Message}"); }
        }
        return list.OrderBy(v => v.LastUse).ToList();
    }

    public static long ProtectedBytes() => ComputeProtectedBytes();

    private static long ComputeProtectedBytes()
    {
        long total = 0;
        try
        {
            var backupRoot = AppDataRoot.BackupRoot;
            foreach (var p in ReadManifestPaths(backupRoot)) total += SafeLength(p);
            foreach (var sub in ProtectedBackupDirs)
                total += AppDataRoot.DirectorySizeBytes(Path.Combine(backupRoot, sub));

            var cacheRoot = AppDataRoot.CacheRoot;
            foreach (var sub in ProtectedCacheDirs)
                total += AppDataRoot.DirectorySizeBytes(Path.Combine(cacheRoot, sub));

            var workRoot = AppDataRoot.WorkRoot;
            foreach (var sub in ProtectedWorkDirs)
                total += AppDataRoot.DirectorySizeBytes(Path.Combine(workRoot, sub));
        }
        catch (Exception ex) { Debug.WriteLine($"[quota] protected size failed: {ex.Message}"); }
        return total;
    }

    public static IReadOnlyList<QuotaHolder> ProtectedHolders()
    {
        var list = new List<QuotaHolder>();
        try
        {
            var backupRoot = AppDataRoot.BackupRoot;
            var cacheRoot  = AppDataRoot.CacheRoot;
            var workRoot   = AppDataRoot.WorkRoot;

            long backups = 0;
            foreach (var p in ReadManifestPaths(backupRoot)) backups += SafeLength(p);

            long tweaks = 0;
            foreach (var sub in ProtectedBackupDirs)
                tweaks += AppDataRoot.DirectorySizeBytes(Path.Combine(backupRoot, sub));
            tweaks += AppDataRoot.DirectorySizeBytes(Path.Combine(cacheRoot, "notracer"));
            tweaks += AppDataRoot.DirectorySizeBytes(Path.Combine(cacheRoot, "rukzak"));

            long gunsmith = AppDataRoot.DirectorySizeBytes(Path.Combine(cacheRoot, "gunsmith"))
                          + AppDataRoot.DirectorySizeBytes(Path.Combine(cacheRoot, "gunskins"));

            long gunpacks = 0;
            foreach (var sub in ProtectedWorkDirs)
                gunpacks += AppDataRoot.DirectorySizeBytes(Path.Combine(workRoot, sub));

            if (backups  > 0) list.Add(new QuotaHolder(Loc.T("misc.quotaHolderBackups"), backups));
            if (gunpacks > 0) list.Add(new QuotaHolder(Loc.T("misc.quotaHolderGunpacks"), gunpacks));
            if (gunsmith > 0) list.Add(new QuotaHolder(Loc.T("misc.quotaHolderGunsmith"), gunsmith));
            if (tweaks   > 0) list.Add(new QuotaHolder(Loc.T("misc.quotaHolderTweakOriginals"), tweaks));
        }
        catch (Exception ex) { Say($"разбор защищённого сорвался: {ex.Message}"); }
        return list.OrderByDescending(h => h.Bytes).ToList();
    }

    private static long ManagedAreaBytes()
    {
        long total = 0;
        try
        {
            var cacheRoot = AppDataRoot.CacheRoot;
            foreach (var sub in Tier1CacheDirs) total += AppDataRoot.DirectorySizeBytes(Path.Combine(cacheRoot, sub));
            foreach (var sub in Tier2CacheDirs) total += AppDataRoot.DirectorySizeBytes(Path.Combine(cacheRoot, sub));

            var work = AppDataRoot.WorkRoot;
            if (Directory.Exists(work))
            {
                foreach (var entry in Directory.EnumerateDirectories(work))
                {
                    var name = Path.GetFileName(entry);
                    if (ProtectedWorkDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (!Tier0WorkPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                    total += AppDataRoot.DirectorySizeBytes(entry);
                }
            }

            var backupRoot = AppDataRoot.BackupRoot;
            var alive = ReadManifestPaths(backupRoot);
            foreach (var sub in new[] { "clean", "snapshot" })
            {
                var dir = Path.Combine(backupRoot, sub);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (alive.Contains(Path.GetFullPath(f))) continue;
                    total += SafeLength(f);
                }
            }
        }
        catch (Exception ex) { Say($"подсчёт площади уборки сорвался: {ex.Message}"); }
        return total;
    }

    public static long OtherBytes(long? totalBytes = null)
    {
        try
        {
            var total = totalBytes ?? AppDataRoot.TotalSizeBytes();
            var other = total - ComputeProtectedBytes() - ManagedAreaBytes();
            return other > 0 ? other : 0;
        }
        catch (Exception ex) { Say($"подсчёт прочего сорвался: {ex.Message}"); return 0; }
    }

    private static HashSet<string> ReadManifestPaths(string backupRoot)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var manifestPath = Path.Combine(backupRoot, "manifest.json");
            if (!File.Exists(manifestPath)) return set;
            using var fs = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(fs);
            if (!doc.RootElement.TryGetProperty("files", out var files)) return set;
            foreach (var key in new[] { "cleanUpdate", "cleanDlc", "snapshotUpdate", "snapshotDlc" })
            {
                if (files.TryGetProperty(key, out var e)
                    && e.ValueKind == JsonValueKind.Object
                    && e.TryGetProperty("path", out var p)
                    && p.ValueKind == JsonValueKind.String
                    && p.GetString() is { Length: > 0 } rel)
                {
                    try { set.Add(Path.GetFullPath(Path.Combine(backupRoot, rel))); } catch { }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[quota] manifest read failed: {ex.Message}"); }
        return set;
    }

    private static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; } catch { return 0; }
    }

    private static bool TooFresh(DateTime lastUseUtc, TimeSpan grace)
    {
        if (grace <= TimeSpan.Zero) return false;
        try { return DateTime.UtcNow - lastUseUtc < grace; }
        catch { return false; }
    }

    private static DateTime LastUseUtc(string path, bool isDir)
    {
        try
        {
            var a = isDir ? Directory.GetLastAccessTimeUtc(path) : File.GetLastAccessTimeUtc(path);
            var w = isDir ? Directory.GetLastWriteTimeUtc(path)  : File.GetLastWriteTimeUtc(path);
            return a > w ? a : w;
        }
        catch { return DateTime.MinValue; }
    }

    private static bool TryDelete(Victim v)
    {
        try
        {
            if (v.IsDirectory)
            {
                var marker = Path.Combine(v.Path, "manifest.json");
                if (File.Exists(marker)) { try { File.Delete(marker); } catch { } }
                Directory.Delete(v.Path, recursive: true);
            }
            else
            {
                File.Delete(v.Path);
                var meta = v.Path + ".meta";
                if (File.Exists(meta)) { try { File.Delete(meta); } catch { } }
            }
            Debug.WriteLine($"[quota] удалено {v.Path} ({v.Bytes / (1024 * 1024)} МБ, посл. доступ {v.LastUse:yyyy-MM-dd})");
            return true;
        }
        catch (Exception ex)
        {
            Say($"удалить {Path.GetFileName(v.Path)} ({Human(v.Bytes)}) не вышло: {ex.Message}");
            return false;
        }
    }
}
