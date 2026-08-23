using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services;

public sealed class IntegrityException : Exception
{
    public IntegrityException(string message) : base(message) { }
}

public static class VerifiedDownload
{
    public const string MarkerFileName = ".mg_verified_sha256";

    private const int HashChunk = 4 * 1024 * 1024;

    public static string Normalize(string? sha) => (sha ?? string.Empty).Trim().ToLowerInvariant();

    public static bool Matches(string? expected, string? actual)
    {
        var e = Normalize(expected);
        var a = Normalize(actual);
        return e.Length > 0 && e == a;
    }

    public static string ComputeSha256(string path, Action<long, long>? progress = null)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: false);

        if (progress is null)
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();

        long size = fs.Length;
        var buffer = new byte[HashChunk];
        long done = 0, lastReported = 0;
        int read;
        progress(0, size);
        while ((read = fs.Read(buffer, 0, HashChunk)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            done += read;
            if (done - lastReported >= 16L * 1024 * 1024 || done >= size)
            {
                lastReported = done;
                progress(done, size);
            }
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    public static bool IsSha256Hex(string? sha)
    {
        var s = Normalize(sha);
        if (s.Length != 64) return false;
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    public static void RequireReference(string? expectedSha, string label)
    {
        if (IsSha256Hex(expectedSha)) return;
        var msg = Loc.T("error.referenceShaMissing", ("file", label));
        Debug.WriteLine($"[integrity] REFUSE {label}: reference sha missing/short");
        DownloadLog.Write("integrity", $"отказ: у «{label}» нет эталонной SHA-256 в каталоге");
        throw new IntegrityException(msg);
    }

    public static async Task FetchAsync(
        string url,
        string destPath,
        string? expectedSha,
        string label,
        Action<long, long>? bytesProgress = null,
        Action<long, long>? hashProgress = null,
        bool referenceRequired = true,
        CancellationToken ct = default)
    {
        if (referenceRequired) RequireReference(expectedSha, label);

        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var partPath = destPath + ".part";
        TryDelete(partPath);

        try
        {
            await Bridge.AppBridge.DownloadViaMirrorAsync(url, partPath, bytesProgress, ct);
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }

        var expected = Normalize(expectedSha);
        if (expected.Length > 0)
        {
            string actual;
            try
            {
                actual = await Task.Run(() => ComputeSha256(partPath, hashProgress), ct);
            }
            catch
            {
                TryDelete(partPath);
                throw;
            }

            if (!Matches(expected, actual))
            {
                TryDelete(partPath);
                Debug.WriteLine($"[integrity] MISMATCH {label}: expected={expected} actual={actual} url={url}");
                DownloadLog.Write("integrity",
                    $"ОТКАЗ по хешу: «{label}» - ждали {expected[..16]}…, получили {actual[..16]}… (источник {url})");
                throw new IntegrityException(Loc.T("error.integrityCheckFailed", ("file", label)));
            }
            Debug.WriteLine($"[integrity] ok {label}: {expected[..16]}…");
        }
        else
        {
            Debug.WriteLine($"[integrity] WARN {label}: эталона нет, файл принят без сверки");
            DownloadLog.Write("integrity", $"внимание: «{label}» принят БЕЗ сверки - в каталоге нет эталонной SHA-256");
        }

        TryDelete(destPath);
        File.Move(partPath, destPath);
    }

    public static async Task<bool> EnsureFileAsync(
        string url,
        string destPath,
        string? expectedSha,
        string label,
        Action<long, long>? bytesProgress = null,
        Action<long, long>? hashProgress = null,
        bool referenceRequired = true,
        CancellationToken ct = default)
    {
        if (referenceRequired) RequireReference(expectedSha, label);

        if (File.Exists(destPath) && CachedFileOk(destPath, expectedSha, label))
            return true;

        await FetchAsync(url, destPath, expectedSha, label,
            bytesProgress, hashProgress, referenceRequired, ct);
        return false;
    }

    public static bool CachedFileOk(string path, string? expectedSha, string label)
    {
        if (Normalize(expectedSha).Length == 0) return false;
        try
        {
            if (!File.Exists(path)) return false;
            var actual = ComputeSha256(path);
            if (Matches(expectedSha, actual)) return true;

            Debug.WriteLine($"[integrity] cache POISONED {label} at {path} - удаляю");
            DownloadLog.Write("integrity", $"кеш «{label}» не сошёлся по хешу - удалён, качаю заново");
            TryDelete(path);
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[integrity] cache probe failed {label}: {ex.Message}");
            return false;
        }
    }

    public static void WriteDirMarker(string dir, string? verifiedSha)
    {
        try
        {
            var sha = Normalize(verifiedSha);
            if (sha.Length == 0 || !Directory.Exists(dir)) return;
            File.WriteAllText(Path.Combine(dir, MarkerFileName), sha);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[integrity] marker write failed for {dir}: {ex.Message}");
        }
    }

    public static void WriteSidecarMarker(string dir, string? verifiedSha)
    {
        try
        {
            var sha = Normalize(verifiedSha);
            if (sha.Length == 0) return;
            File.WriteAllText(dir + ".sha256", sha);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[integrity] sidecar write failed for {dir}: {ex.Message}");
        }
    }

    public static bool SidecarMarkerMatches(string dir, string? expectedSha)
    {
        try
        {
            var expected = Normalize(expectedSha);
            if (expected.Length == 0 || !Directory.Exists(dir)) return false;
            var marker = dir + ".sha256";
            if (!File.Exists(marker)) return false;
            return Matches(expected, File.ReadAllText(marker));
        }
        catch { return false; }
    }

    public static bool DirMarkerMatches(string dir, string? expectedSha)
    {
        try
        {
            var expected = Normalize(expectedSha);
            if (expected.Length == 0 || !Directory.Exists(dir)) return false;
            var marker = Path.Combine(dir, MarkerFileName);
            if (!File.Exists(marker)) return false;
            return Matches(expected, File.ReadAllText(marker));
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
