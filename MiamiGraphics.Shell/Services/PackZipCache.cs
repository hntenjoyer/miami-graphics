using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.System;

namespace MiamiGraphics.Shell.Services;

public sealed class PackZipCache
{
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    private static string CacheDir => ModCacheSettings.Dir("packzips");

    public static string? CachedPathOrNull(string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return null;
        var p = Path.Combine(CacheDir, expectedSha256.ToLowerInvariant() + ".zip");
        return File.Exists(p) ? p : null;
    }

    public async Task<string> EnsurePackZipAsync(
        string url,
        string expectedSha256,
        long? expectedSize,
        IProgress<(long received, long total)>? bytesProgress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))    throw new ArgumentException("pack.zip url is empty", nameof(url));
        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new ArgumentException("expectedSha256 is empty - refusing to cache without an integrity key", nameof(expectedSha256));

        var sha = expectedSha256.ToLowerInvariant();
        var dst = Path.Combine(CacheDir, sha + ".zip");

        await _ioLock.WaitAsync(ct);
        try
        {

            if (ModCacheSettings.ReuseEnabled && File.Exists(dst))
            {
                var sizeOk = !expectedSize.HasValue || new FileInfo(dst).Length == expectedSize.Value;
                if (sizeOk)
                {
                    var actual = await Task.Run(() => ComputeFileSha256(dst), ct);
                    if (string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[packzip-cache] hit: {sha[..8]} ({new FileInfo(dst).Length / 1024} KB)");
                        MiamiGraphics.Core.System.DataQuota.Touch(dst);
                        return dst;
                    }
                    Debug.WriteLine($"[packzip-cache] sha mismatch on cached entry - refetching");
                }
            }

            var reserve = await Task.Run(
                () => DataQuota.TryReserve(expectedSize ?? 0, Loc.T("misc.gunpackDownload")), ct);
            if (!reserve.Ok) throw new InvalidOperationException(reserve.Message!);

            var part = dst + ".part";
            try { if (File.Exists(part)) File.Delete(part); } catch { }

            long lastEmit = 0;
            await Bridge.AppBridge.DownloadViaMirrorAsync(url, part,
                (done, total) =>
                {
                    if (done - lastEmit < (1 << 19) && done != total) return;
                    lastEmit = done;
                    bytesProgress?.Report((done, total > 0 ? total : done));
                }, ct);

            var dlSha = await Task.Run(() => ComputeFileSha256(part), ct);
            if (!string.Equals(dlSha, sha, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(part); } catch { }
                throw new InvalidOperationException(
                    Loc.T("error.packZipShaMismatch", ("want", sha[..16]), ("got", dlSha[..16]), ("url", url)));
            }
            if (File.Exists(dst)) { try { File.Delete(dst); } catch { } }
            File.Move(part, dst);
            Debug.WriteLine($"[packzip-cache] downloaded: {sha[..8]} ({new FileInfo(dst).Length / 1024} KB)");
            return dst;
        }
        finally { _ioLock.Release(); }
    }

    public static Dictionary<string, byte[]> ExtractFiles(
        string packZipPath,
        IEnumerable<string> wantedFileNames)
    {
        var wanted = new HashSet<string>(wantedFileNames, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return result;
        if (!File.Exists(packZipPath)) return result;

        using var fs = File.OpenRead(packZipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!wanted.Contains(entry.Name)) continue;
            if (!MiamiGraphics.Core.System.SafePath.IsSafeRelative(entry.Name)) continue;
            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            result[entry.Name] = ms.ToArray();
        }
        return result;
    }

    private static string ComputeFileSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: false);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}
