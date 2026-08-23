using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.System;

public sealed record BackupProgress(
    string Phase,
    int Percent,
    string? FileName,
    long? BytesProcessed,
    long? BytesTotal,
    string? ErrorCode,
    string? ErrorMessage
);

public sealed record BackupStatus(
    bool ManifestExists,
    bool CleanUpdatePresent,
    bool CleanDlcPresent,
    bool SnapshotUpdatePresent,
    bool SnapshotDlcPresent,
    DateTime? LastBackupAt,
    string? KnownExeVersion
);

public sealed record BackupResult(
    bool Success,
    bool HadDirtyUpdate,
    bool VersionUnsupported,
    string? ErrorCode,
    string? ErrorMessage,

    IReadOnlyList<FileLockDetector.LockerProcess>? Lockers = null
);

public sealed class BackupService
{
    private const string DlcRelativePath = @"update\x64\dlcpacks\patchday18ng\dlc.rpf";
    private const string UpdateRelativePath = @"update\update.rpf";
    private const long ChunkSize = 64 * 1024;

    private readonly GtaVersionsRegistry _registry;
    private readonly IBackupSource _source;

    private static string _backupRoot  => AppDataRoot.BackupRoot;
    private static string _cleanDir    => AppDataRoot.BackupDir("clean");
    private static string _snapshotDir => AppDataRoot.BackupDir("snapshot");
    private static string _manifestPath => Path.Combine(AppDataRoot.BackupRoot, "manifest.json");

    public BackupService(GtaVersionsRegistry registry, IBackupSource source)
    {
        _registry = registry;
        _source = source;

        try
        {
            Directory.CreateDirectory(_cleanDir);
            Directory.CreateDirectory(_snapshotDir);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[backup] backup layout create failed: {ex.Message}");
        }
    }

    public Task<BackupStatus> GetStatusAsync(string gtaPath, CancellationToken ct)
    {
        var manifest = ReadManifest();
        if (manifest is null)
        {
            return Task.FromResult(new BackupStatus(false, false, false, false, false, null, null));
        }

        bool cleanUpdate = manifest.Files.CleanUpdate is { } cu && File.Exists(Path.Combine(_backupRoot, cu.Path));
        bool cleanDlc    = manifest.Files.CleanDlc is { } cd && File.Exists(Path.Combine(_backupRoot, cd.Path));
        bool snapUpdate  = manifest.Files.SnapshotUpdate is { } su && File.Exists(Path.Combine(_backupRoot, su.Path));
        bool snapDlc     = manifest.Files.SnapshotDlc is { } sd && File.Exists(Path.Combine(_backupRoot, sd.Path));

        return Task.FromResult(new BackupStatus(true, cleanUpdate, cleanDlc, snapUpdate, snapDlc, manifest.LastBackupAt, manifest.ExeVersion));
    }

    public async Task<BackupResult> RunFullBackupAsync(string gtaPath, IProgress<BackupProgress> progress, CancellationToken ct)
    {
        try
        {
            var probe = Path.Combine(gtaPath ?? string.Empty, UpdateRelativePath);
            long need = File.Exists(probe) ? new FileInfo(probe).Length * 2 : 0;
            await Task.Run(() => DataQuota.EnsureRoom(need, "бэкап игры"));
        }
        catch (Exception ex) { Debug.WriteLine($"[backup] подготовка места сорвалась: {ex.Message}"); }

        using var quotaHold = DataQuota.HoldAndSweepAfter("бэкап");

        try
        {

            Report(progress, "detecting", 0);
            if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
                return Fail("GTA_PATH_INVALID", "GTA path does not exist");
            var exePath = Path.Combine(gtaPath, "GTA5.exe");
            if (!File.Exists(exePath))
                return Fail("EXE_NOT_FOUND", "GTA5.exe not found");
            var exeVersion = FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "unknown";
            Report(progress, "detecting", 100);

            var workingUpdate = Path.Combine(gtaPath, UpdateRelativePath);
            var workingDlc = Path.Combine(gtaPath, DlcRelativePath);
            if (!File.Exists(workingUpdate))
                return Fail("UNKNOWN", $"working update.rpf missing: {workingUpdate}");

            Report(progress, "hashing_user_update", 0, "update.rpf");
            string userHash;
            long userSize;
            try
            {
                (userHash, userSize) = await ComputeSha256Async(workingUpdate, p =>
                    Report(progress, "hashing_user_update", p, "update.rpf"), ct);
            }
            catch (IOException ioe)
            {
                var lockers = FileLockDetector.WhoIsLocking(workingUpdate);
                var who = lockers.Count == 0
                    ? Loc.T("misc.lockerGuessGame")
                    : string.Join(", ", lockers.Select(l => l.ProcessName));
                return FailLocked(
                    Loc.T("error.updateRpfReadFailed", ("detail", ioe.Message), ("who", who)),
                    lockers);
            }

            Report(progress, "comparing", 0);
            var reference = _registry.GetReferenceForVersion(exeVersion);
            bool versionUnsupported = reference is null;
            bool isDirty;
            if (reference is null)
            {

                isDirty = true;
            }
            else
            {
                isDirty = !(userSize == reference.UpdateRpfSize &&
                            string.Equals(userHash, reference.UpdateRpfSha256, StringComparison.OrdinalIgnoreCase));
            }
            Report(progress, "comparing", 100);

            string? snapshotUpdatePath = null;
            var manifestSnapshot = ReadManifest()?.Files.SnapshotUpdate;

            if (isDirty && manifestSnapshot is null)
            {
                var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                snapshotUpdatePath = Path.Combine("snapshot", $"update_{exeVersion}_{ts}.rpf");
                try
                {
                    await RetryOnSharingViolationAsync(
                        () => CopyFileAsync(workingUpdate, Path.Combine(_backupRoot, snapshotUpdatePath), p =>
                            Report(progress, "snapshot_user_update", p, "update.rpf"), ct),
                        ct);
                }
                catch (IOException ioe) when (IsDiskFull(ioe))
                {
                    return Fail("DISK_FULL", ioe.Message);
                }
                catch (IOException ioe)
                {

                    var lockers = FileLockDetector.WhoIsLocking(workingUpdate);
                    var who = lockers.Count == 0
                        ? Loc.T("misc.lockerGuessGame")
                        : string.Join(", ", lockers.Select(l => l.ProcessName));
                    return FailLocked(
                        Loc.T("error.updateRpfCopyToSnapshotFailed", ("detail", ioe.Message), ("who", who)),
                        lockers);
                }
            }
            else if (manifestSnapshot is not null)
            {
                snapshotUpdatePath = manifestSnapshot.Path;
            }

            var cleanUpdateRel = Path.Combine("clean", $"update_{exeVersion}.rpf");
            var cleanUpdateAbs = Path.Combine(_backupRoot, cleanUpdateRel);
            bool cleanUpdateAvailable = true;

            if (!isDirty)
            {

                if (!File.Exists(cleanUpdateAbs))
                {
                    Report(progress, "downloading_clean_update", 0, "update.rpf");
                    try
                    {
                        await RetryOnSharingViolationAsync(
                            () => CopyFileAsync(workingUpdate, cleanUpdateAbs, p =>
                                Report(progress, "downloading_clean_update", p, "update.rpf"), ct),
                            ct);
                    }
                    catch (IOException ioe) when (IsDiskFull(ioe))
                    {
                        return Fail("DISK_FULL", ioe.Message);
                    }
                    catch (IOException ioe)
                    {
                        var lockers = FileLockDetector.WhoIsLocking(workingUpdate);
                        var who = lockers.Count == 0
                            ? Loc.T("misc.lockerGuessGame")
                            : string.Join(", ", lockers.Select(l => l.ProcessName));
                        try { File.Delete(cleanUpdateAbs); } catch {  }
                        return FailLocked(
                            Loc.T("error.updateRpfCopyToCacheFailed", ("detail", ioe.Message), ("who", who)),
                            lockers);
                    }
                }

            }
            else
            {

                if (!File.Exists(cleanUpdateAbs))
                {
                    Report(progress, "downloading_clean_update", 0, "update.rpf");
                    Stream? src = null;
                    var sawBytes = false;
                    try
                    {
                        src = await _source.GetCleanUpdateRpfAsync(exeVersion,
                            new Progress<int>(p =>
                            {
                                if (!sawBytes) Report(progress, "downloading_clean_update", p, "update.rpf");
                            }),
                            (received, total) =>
                            {
                                sawBytes = true;
                                Report(progress, "downloading_clean_update",
                                    total > 0 ? (int)(received * 100L / total) : 0, "update.rpf",
                                    received, total > 0 ? total : (long?)null);
                            },
                            ct);
                    }
                    catch (FileNotFoundException)
                    {

                        cleanUpdateAvailable = false;
                        Report(progress, "downloading_clean_update", 100, "update.rpf");
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException)
                    {
                        return Fail("NETWORK_ERROR", ex.Message);
                    }

                    if (src is not null)
                    {
                        using (src)
                        {

                            try
                            {
                                await RetryOnSharingViolationAsync(
                                    () => StreamToFileAsync(src, cleanUpdateAbs, p =>
                                        Report(progress, "writing_working_update", p, "update.rpf"), ct),
                                    ct);
                            }
                            catch (IOException ioe) when (IsDiskFull(ioe))
                            {
                                return Fail("DISK_FULL", ioe.Message);
                            }
                            catch (IOException ioe)
                            {
                                var lockers = FileLockDetector.WhoIsLocking(cleanUpdateAbs);
                                var who = lockers.Count == 0
                                    ? Loc.T("misc.lockerGuessAv")
                                    : string.Join(", ", lockers.Select(l => l.ProcessName));

                                try { File.Delete(cleanUpdateAbs); } catch {  }
                                return FailLocked(
                                    Loc.T("error.cleanUpdateRpfSaveFailed", ("detail", ioe.Message), ("who", who)),
                                    lockers);
                            }
                        }
                    }
                }

                if (cleanUpdateAvailable && File.Exists(cleanUpdateAbs))
                {
                    string downloadedSha = "";
                    long   downloadedSize = 0;
                    try
                    {
                        await RetryOnSharingViolationAsync(async () =>
                        {
                            (downloadedSha, downloadedSize) = await ComputeSha256Async(cleanUpdateAbs, null, ct);
                        }, ct);
                    }
                    catch (IOException ioe)
                    {
                        var lockers = FileLockDetector.WhoIsLocking(cleanUpdateAbs);
                        var who = lockers.Count == 0
                            ? Loc.T("misc.lockerGuessAv")
                            : string.Join(", ", lockers.Select(l => l.ProcessName));

                        return FailLocked(
                            Loc.T("error.cleanUpdateRpfVerifyFailed", ("detail", ioe.Message), ("who", who)),
                            lockers);
                    }

                    string? sourceSha = null;
                    try
                    {
                        var (sha, _) = await _source.GetCleanUpdateRpfMetaAsync(exeVersion, ct);
                        sourceSha = sha;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[backup] source meta lookup failed ({ex.GetType().Name}): {ex.Message} - falling back to embedded registry");
                    }

                    var expectedSha = sourceSha ?? reference?.UpdateRpfSha256;
                    if (expectedSha is not null
                        && !string.Equals(downloadedSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[backup] clean update.rpf SHA mismatch (expected={expectedSha[..8]} actual={downloadedSha[..8]}, src={(sourceSha is not null ? "supabase" : "embedded")}) - discarding ~{downloadedSize / (1024 * 1024)} MB of wrong-version cache");
                        try { File.Delete(cleanUpdateAbs); } catch {  }
                        cleanUpdateAvailable = false;
                    }
                }

            }

            string? cleanUpdateSha = null;
            long cleanUpdateSize = 0;
            if (cleanUpdateAvailable && File.Exists(cleanUpdateAbs))
            {
                try
                {
                    string sha = "";
                    long sz = 0;
                    await RetryOnSharingViolationAsync(async () =>
                    {
                        (sha, sz) = await ComputeSha256Async(cleanUpdateAbs, null, ct);
                    }, ct);
                    cleanUpdateSha = sha;
                    cleanUpdateSize = sz;
                }
                catch (IOException ioe)
                {
                    Debug.WriteLine($"[backup] manifest-entry SHA failed ({ioe.Message}) - leaving cleanUpdate=null in manifest");
                }
            }

            string? snapshotDlcPath = ReadManifest()?.Files.SnapshotDlc?.Path;
            if (snapshotDlcPath is null && File.Exists(workingDlc))
            {
                var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                snapshotDlcPath = Path.Combine("snapshot", $"dlc_patchday18ng_{ts}.rpf");
                try
                {
                    await RetryOnSharingViolationAsync(
                        () => CopyFileAsync(workingDlc, Path.Combine(_backupRoot, snapshotDlcPath), p =>
                            Report(progress, "snapshot_dlc", p, "dlc.rpf"), ct),
                        ct);
                }
                catch (IOException ioe) when (IsDiskFull(ioe))
                {
                    return Fail("DISK_FULL", ioe.Message);
                }
                catch (IOException ioe)
                {

                    Debug.WriteLine($"[backup] snapshot dlc.rpf failed ({ioe.Message}) - continuing without DLC snapshot");
                    snapshotDlcPath = null;
                }
            }

            var cleanDlcRel = Path.Combine("clean", "dlc_patchday18ng.rpf");
            var cleanDlcAbs = Path.Combine(_backupRoot, cleanDlcRel);
            long cleanDlcSize = 0;
            try
            {
                Report(progress, "downloading_clean_dlc", 0, "dlc.rpf");
                Stream src;
                var sawDlcBytes = false;
                try
                {
                    src = await _source.GetCleanDlcRpfAsync(
                        new Progress<int>(p =>
                        {
                            if (!sawDlcBytes) Report(progress, "downloading_clean_dlc", p, "dlc.rpf");
                        }),
                        (received, total) =>
                        {
                            sawDlcBytes = true;
                            Report(progress, "downloading_clean_dlc",
                                total > 0 ? (int)(received * 100L / total) : 0, "dlc.rpf",
                                received, total > 0 ? total : (long?)null);
                        },
                        ct);
                }
                catch (FileNotFoundException)
                {

                    src = Stream.Null;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    return Fail("NETWORK_ERROR", ex.Message);
                }

                if (src != Stream.Null)
                {
                    using (src)
                    {
                        try
                        {
                            await RetryOnSharingViolationAsync(
                                () => StreamToFileAsync(src, cleanDlcAbs, p =>
                                    Report(progress, "writing_working_dlc", p, "dlc.rpf"), ct),
                                ct);
                        }
                        catch (IOException ioe) when (IsDiskFull(ioe))
                        {
                            return Fail("DISK_FULL", ioe.Message);
                        }
                        catch (IOException ioe)
                        {

                            try { File.Delete(cleanDlcAbs); } catch {  }
                            var lockers = FileLockDetector.WhoIsLocking(cleanDlcAbs);
                            var who = lockers.Count == 0
                                ? Loc.T("misc.lockerGuessAv")
                                : string.Join(", ", lockers.Select(l => l.ProcessName));
                            return FailLocked(
                                Loc.T("error.cleanDlcRpfSaveFailed", ("detail", ioe.Message), ("who", who)),
                                lockers);
                        }
                    }
                    cleanDlcSize = new FileInfo(cleanDlcAbs).Length;

                }
            }
            catch (Exception ex)
            {
                return Fail("UNKNOWN", $"DLC stage failed: {ex.Message}");
            }

            Report(progress, "writing_manifest", 0);

            var prevFiles = ReadManifest()?.Files;
            var cleanUpdateEntry = KeepAliveEntry(cleanUpdateRel,
                cleanUpdateSha is null ? null : new FileEntry { Path = cleanUpdateRel, Size = cleanUpdateSize, Sha256 = cleanUpdateSha, CreatedAt = DateTime.UtcNow },
                prevFiles?.CleanUpdate);
            var cleanDlcEntry = KeepAliveEntry(cleanDlcRel,
                cleanDlcSize == 0 ? null : new FileEntry { Path = cleanDlcRel, Size = cleanDlcSize, CreatedAt = DateTime.UtcNow },
                prevFiles?.CleanDlc);

            var manifest = new Manifest
            {
                Version = 1,
                LastBackupAt = DateTime.UtcNow,
                ExeVersion = exeVersion,
                Files = new ManifestFiles
                {
                    CleanUpdate    = cleanUpdateEntry,
                    SnapshotUpdate = snapshotUpdatePath is null ? null : new FileEntry { Path = snapshotUpdatePath, Size = userSize, CreatedAt = DateTime.UtcNow },
                    CleanDlc       = cleanDlcEntry,
                    SnapshotDlc    = snapshotDlcPath is null ? null : new FileEntry { Path = snapshotDlcPath, CreatedAt = DateTime.UtcNow },
                },
            };
            WriteManifest(manifest);
            Report(progress, "writing_manifest", 100);

            PruneBackupDirs(manifest);

            Report(progress, "done", 100);
            return new BackupResult(true, isDirty, versionUnsupported, null, null);
        }
        catch (OperationCanceledException)
        {
            return Fail("CANCELLED", Loc.T("error.backupCancelled"));
        }
        catch (UnauthorizedAccessException uae)
        {
            return Fail("GTA_PATH_INVALID", uae.Message);
        }
        catch (IOException ioe) when (IsDiskFull(ioe))
        {
            return Fail("DISK_FULL", ioe.Message);
        }
        catch (Exception ex)
        {
            return Fail("UNKNOWN", ex.Message);
        }

        BackupResult Fail(string code, string message)
        {
            progress.Report(new BackupProgress("error", 0, null, null, null, code, message));
            return new BackupResult(false, false, false, code, message);
        }

        BackupResult FailLocked(string message, IReadOnlyList<FileLockDetector.LockerProcess> lockers)
        {
            progress.Report(new BackupProgress("error", 0, null, null, null, "FILE_LOCKED", message));
            return new BackupResult(false, false, false, "FILE_LOCKED", message, lockers);
        }
    }

    public string? GetCleanUpdateRpfPath()
    {
        var manifest = ReadManifest();
        if (manifest?.Files.CleanUpdate is not { } cu) return null;
        var cleanAbs = Path.Combine(_backupRoot, cu.Path);
        return File.Exists(cleanAbs) ? cleanAbs : null;
    }

    public Task<bool> RestoreFromCleanAsync(string gtaPath, CancellationToken ct)
        => RestoreFromCleanAsync(gtaPath, ct, null);

    public async Task<bool> RestoreFromCleanAsync(string gtaPath, CancellationToken ct, Action<int>? progressPercent)
    {
        var manifest = ReadManifest();
        if (manifest?.Files.CleanUpdate is not { } cu) return false;
        var cleanAbs = Path.Combine(_backupRoot, cu.Path);
        if (!File.Exists(cleanAbs)) return false;
        var workingUpdate = Path.Combine(gtaPath, UpdateRelativePath);
        await CopyFileAsync(cleanAbs, workingUpdate, progressPercent, ct);
        return true;
    }

    public static Task CopyLargeFileAsync(string srcPath, string dstPath, Action<int>? progressPercent, CancellationToken ct)
        => CopyFileAsync(srcPath, dstPath, progressPercent, ct);

    public async Task<bool> RestoreCleanDlcAsync(string gtaPath, CancellationToken ct)
    {
        var manifest = ReadManifest();
        if (manifest?.Files.CleanDlc is not { } cd) return false;
        var cleanAbs = Path.Combine(_backupRoot, cd.Path);
        if (!File.Exists(cleanAbs)) return false;
        var workingDlc = Path.Combine(gtaPath, DlcRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(workingDlc)!);
        await CopyFileAsync(cleanAbs, workingDlc, null, ct);
        return true;
    }

    public async Task<bool> RestoreFromSnapshotAsync(string gtaPath, CancellationToken ct)
    {
        var manifest = ReadManifest();
        if (manifest?.Files.SnapshotUpdate is not { } su) return false;
        var snapAbs = Path.Combine(_backupRoot, su.Path);
        if (!File.Exists(snapAbs)) return false;
        var workingUpdate = Path.Combine(gtaPath, UpdateRelativePath);
        await CopyFileAsync(snapAbs, workingUpdate, null, ct);
        return true;
    }

    private static void Report(IProgress<BackupProgress> p, string phase, int percent, string? fileName = null,
        long? processed = null, long? total = null)
        => p.Report(new BackupProgress(phase, percent, fileName, processed, total, null, null));

    private static async Task<(string sha256, long size)> ComputeSha256Async(string path, Action<int>? progressPercent, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, (int)ChunkSize, useAsync: true);
        var size = fs.Length;
        var buffer = new byte[ChunkSize];
        long total = 0;
        int read;
        var lastReport = -1;
        while ((read = await fs.ReadAsync(buffer.AsMemory(0, (int)ChunkSize), ct)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            total += read;
            var percent = size == 0 ? 100 : (int)((total * 100L) / size);
            if (percent != lastReport && percent % 4 == 0)
            {
                progressPercent?.Invoke(percent);
                lastReport = percent;
            }
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        progressPercent?.Invoke(100);
        return (hash, size);
    }

    private const int CopyChunkSize = 4 * 1024 * 1024;

    private static async Task CopyFileAsync(string srcPath, string dstPath, Action<int>? progressPercent, CancellationToken ct)
    {

        Directory.CreateDirectory(Path.GetDirectoryName(dstPath)!);
        var tempPath = dstPath + ".restoring";
        try
        {
            await using var src = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyChunkSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var size = src.Length;
                var buffer = new byte[CopyChunkSize];
                long total = 0;
                int read;
                var lastReport = -1;
                while ((read = await src.ReadAsync(buffer.AsMemory(0, CopyChunkSize), ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    total += read;
                    var percent = size == 0 ? 100 : (int)((total * 100L) / size);
                    if (percent != lastReport && percent % 4 == 0)
                    {
                        progressPercent?.Invoke(percent);
                        lastReport = percent;
                    }
                }
                await dst.FlushAsync(ct);
                dst.Flush(flushToDisk: true);
            }
            File.Move(tempPath, dstPath, overwrite: true);
            progressPercent?.Invoke(100);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch {  }
            throw;
        }
    }

    private static async Task StreamToFileAsync(Stream src, string dstPath, Action<int>? progressPercent, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dstPath)!);
        var tempPath = dstPath + ".partial";
        var buffer = new byte[ChunkSize];
        long total = 0;
        long? expected = TryGetStreamLength(src);
        try
        {
            await using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, (int)ChunkSize, useAsync: true))
            {
                int read;
                var lastReport = -1;
                while ((read = await src.ReadAsync(buffer.AsMemory(0, (int)ChunkSize), ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    total += read;
                    if (expected is long e && e > 0)
                    {
                        var percent = (int)((total * 100L) / e);
                        if (percent != lastReport && percent % 2 == 0)
                        {
                            progressPercent?.Invoke(percent);
                            lastReport = percent;
                        }
                    }
                }

                await dst.FlushAsync(ct);

                dst.Flush(flushToDisk: true);
            }
            progressPercent?.Invoke(100);
            File.Move(tempPath, dstPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch {  }
            throw;
        }
    }

    private static long? TryGetStreamLength(Stream stream)
    {
        try
        {
            var len = stream.Length;
            return len > 0 ? len : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDiskFull(IOException ex)
    {
        const int ERROR_DISK_FULL = 0x70;
        const int ERROR_HANDLE_DISK_FULL = 0x27;
        var hr = ex.HResult & 0xFFFF;
        return hr == ERROR_DISK_FULL || hr == ERROR_HANDLE_DISK_FULL;
    }

    private static bool IsSharingViolation(IOException ex)
    {
        const int ERROR_SHARING_VIOLATION = 0x20;
        const int ERROR_LOCK_VIOLATION    = 0x21;
        var hr = ex.HResult & 0xFFFF;
        return hr == ERROR_SHARING_VIOLATION || hr == ERROR_LOCK_VIOLATION;
    }

    private static async global::System.Threading.Tasks.Task RetryOnSharingViolationAsync(
        Func<global::System.Threading.Tasks.Task> op,
        global::System.Threading.CancellationToken ct,
        int maxAttempts = 4)
    {
        IOException? last = null;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try { await op(); return; }
            catch (IOException ioe) when (IsSharingViolation(ioe) && !IsDiskFull(ioe))
            {
                last = ioe;
                if (attempt == maxAttempts - 1) break;

                var delayMs = 300 * (1 << attempt);
                Debug.WriteLine($"[backup] share-violation on '{ioe.Message}', retry {attempt + 1}/{maxAttempts - 1} in {delayMs} ms");
                await global::System.Threading.Tasks.Task.Delay(delayMs, ct);
            }
        }

        throw last!;
    }

    private const int MaxBackupFilesPerDir = 3;

    private void PruneBackupDirs(Manifest current)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in new[] { current.Files.CleanUpdate, current.Files.CleanDlc,
                                  current.Files.SnapshotUpdate, current.Files.SnapshotDlc })
            if (e is { Path: { } p })
                keep.Add(Path.GetFullPath(Path.Combine(_backupRoot, p)));

        foreach (var dir in new[] { _cleanDir, _snapshotDir })
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                var files = Directory.GetFiles(dir)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .ToList();

                var kept = 0;
                foreach (var fi in files)
                {
                    var full = Path.GetFullPath(fi.FullName);
                    if (keep.Contains(full) || kept < MaxBackupFilesPerDir)
                    {
                        kept++;
                        continue;
                    }
                    try
                    {
                        var mb = fi.Length / (1024 * 1024);
                        fi.Delete();
                        Debug.WriteLine($"[backup.prune] deleted stale {fi.FullName} ({mb} MB)");
                    }
                    catch (Exception ex) { Debug.WriteLine($"[backup.prune] delete {fi.FullName}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[backup.prune] {dir}: {ex.Message}"); }
        }
    }

    private FileEntry? KeepAliveEntry(string relPath, FileEntry? fresh, FileEntry? previous)
    {
        if (fresh is not null) return fresh;
        try
        {
            var abs = Path.Combine(_backupRoot, relPath);
            var fi = new FileInfo(abs);
            if (!fi.Exists || fi.Length == 0) return null;

            if (previous is not null
                && string.Equals(previous.Path, relPath, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[backup] '{relPath}' на диске есть, но обновить запись не вышло - " +
                    "оставляю прошлую ссылку (иначе уборка снесла бы файл как мёртвый)");
                return previous;
            }

            Debug.WriteLine($"[backup] '{relPath}' на диске есть без прошлой записи - пишу ссылку без SHA");
            return new FileEntry { Path = relPath, Size = fi.Length, CreatedAt = fi.LastWriteTimeUtc };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[backup] keep-alive '{relPath}' failed: {ex.Message}");
            return previous;
        }
    }

    private Manifest? ReadManifest()
    {
        if (!File.Exists(_manifestPath)) return null;
        try
        {
            var raw = File.ReadAllText(_manifestPath);
            return JsonSerializer.Deserialize<Manifest>(raw, ManifestJson);
        }
        catch
        {
            return null;
        }
    }

    private void WriteManifest(Manifest m)
    {

        var json = JsonSerializer.Serialize(m, ManifestJson);
        var tmp = _manifestPath + ".tmp";
        File.WriteAllText(tmp, json);

        File.Move(tmp, _manifestPath, overwrite: true);
    }

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class Manifest
    {
        public int Version { get; set; }
        public DateTime LastBackupAt { get; set; }
        public string ExeVersion { get; set; } = string.Empty;
        public ManifestFiles Files { get; set; } = new();
    }

    private sealed class ManifestFiles
    {
        public FileEntry? CleanUpdate { get; set; }
        public FileEntry? SnapshotUpdate { get; set; }
        public FileEntry? CleanDlc { get; set; }
        public FileEntry? SnapshotDlc { get; set; }
    }

    private sealed class FileEntry
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string? Sha256 { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
