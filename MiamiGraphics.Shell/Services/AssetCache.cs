using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MiamiGraphics.Shell.Services;

public sealed class AssetCache
{
    private const long MaxCacheBytes = 2L * 1024 * 1024 * 1024;

    private static readonly string DiagLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics", "cache.log");
    private static readonly object _diagLock = new();

    private const long DiagMaxBytes = 4L * 1024 * 1024;
    private static long _diagBytes = -1;

    private static void Diag(string s)
    {
        try
        {
            var line = DateTime.Now.ToString("HH:mm:ss.fff ") + s + "\r\n";
            lock (_diagLock)
            {
                if (_diagBytes < 0)
                    _diagBytes = File.Exists(DiagLog) ? new FileInfo(DiagLog).Length : 0;

                if (_diagBytes + line.Length > DiagMaxBytes)
                {
                    try { File.Move(DiagLog, DiagLog + ".prev", overwrite: true); } catch { }
                    _diagBytes = 0;
                }

                File.AppendAllText(DiagLog, line);
                _diagBytes += line.Length;
            }
        }
        catch { }
    }
    public static void DiagPublic(string s) => Diag(s);
    private const string CacheDirName = "cache";
    private const string AssetsSubdir = "assets";

    private static string _root => ModCacheSettings.Dir(AssetsSubdir);

    private readonly ConcurrentDictionary<string, byte> _knownKeys = new();
    private readonly ConcurrentDictionary<string, byte> _known404 = new();

    public AssetCache()
    {
        Directory.CreateDirectory(_root);
    }

    public void WarmInMemoryIndex()
    {
        if (!Directory.Exists(_root)) return;
        var count = 0;
        var orphans = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".meta", StringComparison.Ordinal)) continue;
                if (name.EndsWith(".tmp", StringComparison.Ordinal))
                {

                    try { File.Delete(file); } catch { }
                    continue;
                }
                if (name.EndsWith(".404", StringComparison.Ordinal))
                {
                    var key404 = name[..^4];
                    _known404.TryAdd(key404, 0);
                    continue;
                }

                if (!File.Exists(file + ".meta"))
                {
                    orphans++;
                    try { File.Delete(file); } catch { }
                    continue;
                }
                _knownKeys.TryAdd(name, 0);
                count++;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[asset-cache] index warmup failed: {ex.Message}");
        }
        Debug.WriteLine($"[asset-cache] in-memory index: {count} files (cleaned {orphans} orphans)");
        Diag($"WarmInMemoryIndex DONE count={count} orphans={orphans}");
    }

    public (byte[] Body, string ContentType)? TryGet(string url)
    {
        var key = KeyFromUrl(url);
        if (!_knownKeys.ContainsKey(key))
        {
            Diag($"TryGet MISS-NOKEY  key={key} index={_knownKeys.Count} url={url}");
            return null;
        }
        var path = Path.Combine(_root, key);
        try
        {
            if (!File.Exists(path))
            {
                _knownKeys.TryRemove(key, out _);
                Diag($"TryGet MISS-NOFILE key={key} url={url}");
                return null;
            }
            var body = File.ReadAllBytes(path);
            var ct = TryReadMeta(path) ?? "application/octet-stream";

            try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch {  }
            Diag($"TryGet HIT        key={key} bytes={body.Length} ct={ct} url={url}");
            return (body, ct);
        }
        catch (Exception ex)
        {
            Diag($"TryGet ERR        key={key} {ex.Message} url={url}");
            Debug.WriteLine($"[asset-cache] read {key}: {ex.Message}");
            return null;
        }
    }

    public bool Contains(string url)
    {
        var key = KeyFromUrl(url);
        if (_knownKeys.ContainsKey(key)) return true;
        if (_known404.ContainsKey(key)) return true;
        return false;
    }

    public bool IsHit(string url)
    {
        var key = KeyFromUrl(url);
        return _knownKeys.ContainsKey(key);
    }

    public void PutNotFoundMarker(string url)
    {
        var key = KeyFromUrl(url);
        var path = Path.Combine(_root, key + ".404");
        try
        {
            File.WriteAllBytes(path, Array.Empty<byte>());
            _known404[key] = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[asset-cache] write 404 marker {key}: {ex.Message}");
        }
    }

    public (int IndexSize, bool Has) DiagContains(string url)
    {
        var key = KeyFromUrl(url);
        var has = _knownKeys.ContainsKey(key);
        return (_knownKeys.Count, has);
    }

    public void Put(string url, byte[] body, string contentType)
    {
        if (body.Length == 0) return;
        var key = KeyFromUrl(url);
        var path = Path.Combine(_root, key);
        var tmp = path + ".tmp";
        var metaTmp = path + ".meta.tmp";
        var metaPath = path + ".meta";
        try
        {

            File.WriteAllBytes(tmp, body);
            File.WriteAllText(metaTmp, contentType, Encoding.UTF8);

            File.Move(metaTmp, metaPath, overwrite: true);
            File.Move(tmp, path, overwrite: true);
            _knownKeys[key] = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[asset-cache] write {key}: {ex.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch {  }
            try { if (File.Exists(metaTmp)) File.Delete(metaTmp); } catch {  }
        }
    }

    public void EvictIfOversize()
    {
        try
        {
            var files = new DirectoryInfo(_root).EnumerateFiles()
                .Where(f => !f.Name.EndsWith(".meta", StringComparison.Ordinal)
                         && !f.Name.EndsWith(".tmp", StringComparison.Ordinal))
                .Select(f => new { File = f, Size = f.Length, Atime = f.LastAccessTimeUtc })
                .ToList();
            long total = 0;
            foreach (var f in files) total += f.Size;
            if (total <= MaxCacheBytes) return;

            var sorted = files.OrderBy(f => f.Atime).ToList();
            foreach (var f in sorted)
            {
                if (total <= MaxCacheBytes) break;
                try
                {
                    f.File.Delete();
                    var meta = new FileInfo(f.File.FullName + ".meta");
                    if (meta.Exists) meta.Delete();
                    _knownKeys.TryRemove(f.File.Name, out _);
                    total -= f.Size;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[asset-cache] evict {f.File.Name}: {ex.Message}");
                }
            }
            Debug.WriteLine($"[asset-cache] evicted to {total / (1024 * 1024)} MB");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[asset-cache] eviction failed: {ex.Message}");
        }
    }

    public void Invalidate(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        var key = KeyFromUrl(url);
        var path = Path.Combine(_root, key);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var meta = path + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
            _knownKeys.TryRemove(key, out _);
            Debug.WriteLine($"[asset-cache] invalidate {key} ({url})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[asset-cache] invalidate {key} failed: {ex.Message}");
        }
    }

    private static string KeyFromUrl(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        var hex = Convert.ToHexString(hash);
        return hex[..32].ToLowerInvariant();
    }

    private static string? TryReadMeta(string assetPath)
    {
        var metaPath = assetPath + ".meta";
        try { return File.Exists(metaPath) ? File.ReadAllText(metaPath, Encoding.UTF8).Trim() : null; }
        catch { return null; }
    }
}
