using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.System;

public sealed record DataMoveProgress(
    string Phase,
    int Percent,
    string? FileName,
    long BytesProcessed,
    long BytesTotal,
    string? ErrorMessage
);

public sealed record DataMoveResult(
    bool Success,
    string EffectiveRoot,
    long MovedBytes,
    bool SourceRemoved,
    string? ErrorMessage
);

public static class DataRootMigration
{
    private static (string Source, string DestName)[] MovedTrees() => new[]
    {
        (AppDataRoot.CacheRoot,  "cache"),
        (AppDataRoot.BackupRoot, "backup"),
    };

    private const long FreeSpaceHeadroomBytes = 512L * 1024 * 1024;

    private const string MovingSuffix = ".moving";

    private const int CopyChunkSize = 4 * 1024 * 1024;

    private static int _running;

    public static long CurrentPayloadBytes()
        => MovedTrees().Sum(t => AppDataRoot.DirectorySizeBytes(t.Source));

    public static async Task<DataMoveResult> MoveAsync(
        string targetBase, IProgress<DataMoveProgress>? progress, CancellationToken ct)
    {
        var oldBase = AppDataRoot.Base;

        if (DataQuota.BusyOrPending)
        {
            progress?.Report(new DataMoveProgress("error", 0, null, 0, 0,
                Loc.T("error.moveBusyInstallOrBackup")));
            return new DataMoveResult(false, oldBase, 0, false,
                Loc.T("error.moveBusyInstallOrBackup"));
        }

        if (Interlocked.Exchange(ref _running, 1) == 1)
            return Fail(oldBase, Loc.T("error.moveAlreadyRunning"));

        var createdTemps = new List<string>();
        var createdDirs  = new List<string>();
        var pendingMoves = new List<(string Temp, string Final)>();

        try
        {
            Report(progress, "checking", 0, null, 0, 0);

            if (string.IsNullOrWhiteSpace(targetBase))
                return Fail(oldBase, Loc.T("error.moveNoFolderPicked"));

            string newBase;
            try { newBase = Path.GetFullPath(targetBase.Trim()); }
            catch (Exception ex) { return Fail(oldBase, Loc.T("error.moveBadPath", ("detail", ex.Message))); }

            var trees = MovedTrees();

            if (PathEquals(newBase, oldBase) && !AppDataRoot.BackupOnLegacyRoot)
                return Fail(oldBase, Loc.T("error.moveAlreadyThere"));

            foreach (var t in trees)
            {
                var dest = Path.Combine(newBase, t.DestName);
                if (PathEquals(dest, t.Source)) continue;
                if (IsUnder(dest, t.Source))
                    return Fail(oldBase, Loc.T("error.moveIntoItself"));
                if (IsUnder(t.Source, dest))
                    return Fail(oldBase, Loc.T("error.moveFolderOverlaps"));
            }

            try
            {
                Directory.CreateDirectory(newBase);
                var probe = Path.Combine(newBase, ".write_probe");
                await File.WriteAllTextAsync(probe, "ok", ct);
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                return Fail(oldBase, Loc.T("error.folderNotWritable", ("folder", newBase), ("detail", ex.Message)));
            }

            var items = new List<(string Src, string Rel, long Bytes)>();
            foreach (var t in trees)
            {
                var srcDir = t.Source;
                if (!Directory.Exists(srcDir)) continue;
                if (PathEquals(srcDir, Path.Combine(newBase, t.DestName))) continue;
                foreach (var f in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
                {
                    long len;
                    try { len = new FileInfo(f).Length; } catch { continue; }
                    items.Add((f, Path.Combine(t.DestName, Path.GetRelativePath(srcDir, f)), len));
                }
            }

            long totalBytes = items.Sum(i => i.Bytes);
            if (items.Count == 0)
            {
                SwitchRoot(newBase);
                Report(progress, "done", 100, null, 0, 0);
                return new DataMoveResult(true, AppDataRoot.Base, 0, true, null);
            }

            var free = AppDataRoot.FreeSpaceBytes(newBase);
            if (free > 0 && free < totalBytes + FreeSpaceHeadroomBytes)
            {
                return Fail(oldBase, Loc.T("error.moveNotEnoughFreeSpace",
                    ("free", Mb(free)), ("need", Mb(totalBytes + FreeSpaceHeadroomBytes))));
            }

            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var temps  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long done = 0;
            foreach (var it in items)
            {
                ct.ThrowIfCancellationRequested();
                var dst = Path.Combine(newBase, it.Rel);
                var dstDir = Path.GetDirectoryName(dst)!;
                if (!Directory.Exists(dstDir)) { Directory.CreateDirectory(dstDir); createdDirs.Add(dstDir); }

                var tmp = dst + MovingSuffix;
                string sha;
                try
                {
                    sha = await CopyWithHashAsync(it.Src, tmp, done, totalBytes, progress, "copying", ct);
                }
                catch (FileNotFoundException)
                {
                    done += it.Bytes;
                    continue;
                }
                createdTemps.Add(tmp);
                temps[it.Rel] = tmp;
                pendingMoves.Add((tmp, dst));
                hashes[it.Rel] = sha;
                done += it.Bytes;
                Report(progress, "copying", Pct(done, totalBytes), Path.GetFileName(it.Src), done, totalBytes);
            }

            long verified = 0;
            foreach (var it in items)
            {
                ct.ThrowIfCancellationRequested();
                if (!hashes.TryGetValue(it.Rel, out var expected)) continue;
                if (!temps.TryGetValue(it.Rel, out var tmp)) continue;
                var actual = await HashFileAsync(tmp, verified, totalBytes, progress, "verifying", ct);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    RollbackCopies(createdTemps, createdDirs);
                    return Fail(oldBase, Loc.T("error.moveChecksumMismatch",
                        ("file", Path.GetFileName(it.Src))));
                }
                verified += it.Bytes;
                Report(progress, "verifying", Pct(verified, totalBytes), Path.GetFileName(it.Src), verified, totalBytes);
            }

            if (DataQuota.BusyOrPending)
            {
                RollbackCopies(createdTemps, createdDirs);
                return Fail(oldBase, Loc.T("error.moveInterruptedByInstall"));
            }

            Report(progress, "switching", 100, null, totalBytes, totalBytes);
            foreach (var (tmp, final) in pendingMoves)
            {
                try
                {
                    File.Move(tmp, final, overwrite: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[data-move] не встал на место '{final}': {ex.Message}");
                    return Fail(oldBase, Loc.T("error.moveFilePlaceFailed",
                        ("file", Path.GetFileName(final)), ("detail", ex.Message)));
                }
            }

            try { SwitchRoot(newBase); }
            catch (Exception ex)
            {
                return Fail(oldBase, Loc.T("error.moveSaveNewPathFailed", ("detail", ex.Message)));
            }

            Report(progress, "cleanup", 100, null, totalBytes, totalBytes);
            var copiedRel = new HashSet<string>(hashes.Keys, StringComparer.OrdinalIgnoreCase);
            bool sourceRemoved = true;
            string? stuckDir = null;
            int skippedNew = 0;
            foreach (var t in trees)
            {
                var srcDir = t.Source;
                if (!Directory.Exists(srcDir)) continue;
                if (PathEquals(srcDir, Path.Combine(newBase, t.DestName))) continue;

                string[] live;
                try { live = Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories); }
                catch (Exception ex)
                {
                    sourceRemoved = false; stuckDir ??= srcDir;
                    Debug.WriteLine($"[data-move] повторный обход '{srcDir}' не удался: {ex.Message}");
                    continue;
                }

                foreach (var f in live)
                {
                    var rel = Path.Combine(t.DestName, Path.GetRelativePath(srcDir, f));
                    if (!copiedRel.Contains(rel))
                    {
                        skippedNew++;
                        Debug.WriteLine($"[data-move] '{rel}' появился после описи - НЕ удаляю");
                        continue;
                    }
                    try { File.Delete(f); }
                    catch (Exception ex)
                    {
                        sourceRemoved = false; stuckDir ??= srcDir;
                        Debug.WriteLine($"[data-move] '{f}' удалить не вышло: {ex.Message}");
                    }
                }

                TryRemoveEmptyDirs(srcDir);
                if (Directory.Exists(srcDir) && Directory.EnumerateFileSystemEntries(srcDir).Any())
                {
                    sourceRemoved = false;
                    stuckDir ??= srcDir;
                }
            }

            Report(progress, "done", 100, null, totalBytes, totalBytes);
            string? note = null;
            if (!sourceRemoved)
            {
                note = skippedNew > 0
                    ? Loc.T("misc.moveDoneNewFilesLeft", ("folder", stuckDir), ("count", skippedNew))
                    : Loc.T("misc.moveDoneOldFolderStuck", ("folder", stuckDir));
            }
            return new DataMoveResult(true, AppDataRoot.Base, totalBytes, sourceRemoved, note);
        }
        catch (OperationCanceledException)
        {
            RollbackCopies(createdTemps, createdDirs);
            return Fail(oldBase, Loc.T("error.moveCancelled"));
        }
        catch (Exception ex)
        {
            RollbackCopies(createdTemps, createdDirs);
            return Fail(oldBase, Loc.T("error.moveFailed", ("detail", ex.Message)));
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }

        DataMoveResult Fail(string root, string message)
        {
            progress?.Report(new DataMoveProgress("error", 0, null, 0, 0, message));
            Debug.WriteLine($"[data-move] FAIL: {message}");
            return new DataMoveResult(false, root, 0, false, message);
        }
    }

    private static void SwitchRoot(string newBase)
    {
        if (PathEquals(newBase, AppDataRoot.DefaultBase))
            AppDataRoot.Set(clearRootOverride: true);
        else
            AppDataRoot.Set(rootOverride: newBase);
    }

    private static void TryRemoveEmptyDirs(string root)
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(x => x.Length))
            {
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
            }
            if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        catch (Exception ex) { Debug.WriteLine($"[data-move] чистка пустых папок '{root}': {ex.Message}"); }
    }

    private static void RollbackCopies(List<string> files, List<string> dirs)
    {
        foreach (var f in files) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
        foreach (var d in dirs.Distinct().OrderByDescending(x => x.Length))
        {
            try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); }
            catch { }
        }
    }

    private static async Task<string> CopyWithHashAsync(string src, string tmp,
        long baseDone, long totalBytes, IProgress<DataMoveProgress>? progress, string phase, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        try
        {
            await using (var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, CopyChunkSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var d = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, CopyChunkSize,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buf = new byte[CopyChunkSize];
                int read;
                long fileDone = 0;
                var lastPct = -1;
                while ((read = await s.ReadAsync(buf.AsMemory(0, CopyChunkSize), ct)) > 0)
                {
                    sha.TransformBlock(buf, 0, read, null, 0);
                    await d.WriteAsync(buf.AsMemory(0, read), ct);
                    fileDone += read;
                    var pct = Pct(baseDone + fileDone, totalBytes);
                    if (pct != lastPct)
                    {
                        Report(progress, phase, pct, Path.GetFileName(src), baseDone + fileDone, totalBytes);
                        lastPct = pct;
                    }
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                await d.FlushAsync(ct);
                d.Flush(flushToDisk: true);
            }

            try
            {
                File.SetLastWriteTimeUtc(tmp, File.GetLastWriteTimeUtc(src));
                File.SetLastAccessTimeUtc(tmp, File.GetLastAccessTimeUtc(src));
            }
            catch {}

            return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    private static async Task<string> HashFileAsync(string path,
        long baseDone, long totalBytes, IProgress<DataMoveProgress>? progress, string phase, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buf = new byte[CopyChunkSize];
        int read;
        long fileDone = 0;
        var lastPct = -1;
        while ((read = await s.ReadAsync(buf.AsMemory(0, CopyChunkSize), ct)) > 0)
        {
            sha.TransformBlock(buf, 0, read, null, 0);
            fileDone += read;
            var pct = Pct(baseDone + fileDone, totalBytes);
            if (pct != lastPct)
            {
                Report(progress, phase, pct, Path.GetFileName(path), baseDone + fileDone, totalBytes);
                lastPct = pct;
            }
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static void Report(IProgress<DataMoveProgress>? p, string phase, int percent,
        string? file, long done, long total)
        => p?.Report(new DataMoveProgress(phase, percent, file, done, total, null));

    private static int Pct(long done, long total) => total <= 0 ? 100 : (int)(done * 100L / total);

    private static long Mb(long bytes) => bytes / (1024 * 1024);

    private static bool PathEquals(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsUnder(string candidate, string parent)
    {
        try
        {
            var c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
            var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            return c.Equals(p, StringComparison.OrdinalIgnoreCase)
                || c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
