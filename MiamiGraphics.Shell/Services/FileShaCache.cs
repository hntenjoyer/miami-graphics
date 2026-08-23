using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace MiamiGraphics.Shell.Services;

public static class FileShaCache
{
    private const int MaxEntries = 64;

    private static readonly object _gate = new();
    private static Dictionary<string, Entry>? _entries;

    private sealed class Entry
    {
        public long   Size       { get; set; }
        public long   MtimeTicks { get; set; }
        public string Sha        { get; set; } = "";
        public long   TouchedAt  { get; set; }
    }

    private static string CachePath => Path.Combine(
        MiamiGraphics.Core.System.AppDataRoot.Dir(), "file_sha_cache.json");

    public static string ComputeSha256Cached(string path)
        => ComputeSha256Cached(path, null);

    public static string ComputeSha256Cached(string path, Action<long, long>? bytesProgress)
    {
        var fi = new FileInfo(path);
        long size  = fi.Length;
        long mtime = fi.LastWriteTimeUtc.Ticks;
        string key = Path.GetFullPath(path).ToLowerInvariant();

        lock (_gate)
        {
            LoadIfNeeded();
            if (_entries!.TryGetValue(key, out var e) && e.Size == size && e.MtimeTicks == mtime)
            {
                e.TouchedAt = DateTime.UtcNow.Ticks;
                bytesProgress?.Invoke(size, size);
                return e.Sha;
            }
        }

        var sw = Stopwatch.StartNew();
        string sha;
        using (var sha256 = SHA256.Create())
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                   bufferSize: 1 << 20, useAsync: false))
        {
            if (bytesProgress is null)
            {
                sha = Convert.ToHexString(sha256.ComputeHash(fs)).ToLowerInvariant();
            }
            else
            {
                const int chunk = 4 * 1024 * 1024;
                var buffer = new byte[chunk];
                long done = 0;
                int read;
                long lastReported = 0;
                bytesProgress(0, size);
                while ((read = fs.Read(buffer, 0, chunk)) > 0)
                {
                    sha256.TransformBlock(buffer, 0, read, null, 0);
                    done += read;
                    if (done - lastReported >= 16L * 1024 * 1024 || done >= size)
                    {
                        lastReported = done;
                        bytesProgress(done, size);
                    }
                }
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                sha = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
            }
        }
        Debug.WriteLine($"[sha-cache] MISS {Path.GetFileName(path)}: hashed {size / (1024 * 1024)} MB in {sw.Elapsed.TotalSeconds:F1}s");

        lock (_gate)
        {
            LoadIfNeeded();
            _entries![key] = new Entry
            {
                Size = size, MtimeTicks = mtime, Sha = sha,
                TouchedAt = DateTime.UtcNow.Ticks,
            };
            TrimAndSave();
        }
        return sha;
    }

    private static void LoadIfNeeded()
    {
        if (_entries is not null) return;
        try
        {
            if (File.Exists(CachePath))
            {
                _entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(
                    File.ReadAllText(CachePath));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[sha-cache] load FAIL ({ex.Message}) - starting empty");
        }
        _entries ??= new Dictionary<string, Entry>(StringComparer.Ordinal);
    }

    private static void TrimAndSave()
    {
        try
        {
            if (_entries!.Count > MaxEntries)
            {
                var stale = _entries
                    .OrderBy(kv => kv.Value.TouchedAt)
                    .Take(_entries.Count - MaxEntries)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in stale) _entries.Remove(k);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(_entries,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[sha-cache] save FAIL ({ex.Message}) - cache stays in-memory only");
        }
    }
}
