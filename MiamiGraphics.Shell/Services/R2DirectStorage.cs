using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace MiamiGraphics.Shell.Services;

public interface IRemoteStorage
{
    Task<string> UploadAsync(string localPath, string remoteKey, IProgress<int>? progress, CancellationToken ct);

    Task<string> UploadHashedAsync(string localPath, string remoteKey, IProgress<int>? progress, CancellationToken ct);
    Task DeleteAsync(string remoteKey, CancellationToken ct);

    Task<int> DeletePrefixAsync(string prefix, CancellationToken ct);

    Task<int> DeletePrefixExceptAsync(string prefix, IReadOnlyList<string> preservePrefixes, CancellationToken ct);

    Task<List<string>> ListKeysAsync(string prefix, CancellationToken ct);

    Task<bool> EnsureCorsAsync(CancellationToken ct);
}

public sealed class R2DirectStorage : IRemoteStorage
{
    private readonly IAdminConfigService _config;

    public R2DirectStorage(IAdminConfigService config) => _config = config;

    private async Task<AmazonS3Client> BuildClientAsync()
    {
        var cfg = await _config.GetAsync();
        if (string.IsNullOrWhiteSpace(cfg.R2AccessKey) || string.IsNullOrWhiteSpace(cfg.R2SecretKey))
            throw new InvalidOperationException("R2 credentials are not configured. Open Admin → Settings.");

        var creds = new BasicAWSCredentials(cfg.R2AccessKey, cfg.R2SecretKey);

        var s3Cfg = new AmazonS3Config
        {
            ServiceURL = cfg.R2Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
        };
        return new AmazonS3Client(creds, s3Cfg);
    }

    public Task<string> UploadAsync(string localPath, string remoteKey, IProgress<int>? progress, CancellationToken ct)
        => UploadInternalAsync(localPath, remoteKey, progress, ct, immutable: false);

    public async Task<string> UploadHashedAsync(string localPath, string remoteKey, IProgress<int>? progress, CancellationToken ct)
    {
        var hashedKey = InjectContentHash(localPath, remoteKey);
        return await UploadInternalAsync(localPath, hashedKey, progress, ct, immutable: true);
    }

    private static string InjectContentHash(string localPath, string remoteKey)
    {
        using var sha = SHA1.Create();
        using var fs = File.OpenRead(localPath);
        var bytes = sha.ComputeHash(fs);
        var hash = Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 12);

        var lastSlash = remoteKey.LastIndexOf('/');
        var dir = lastSlash >= 0 ? remoteKey.Substring(0, lastSlash + 1) : string.Empty;
        var file = lastSlash >= 0 ? remoteKey.Substring(lastSlash + 1) : remoteKey;

        var ext = Path.GetExtension(file);
        var name = string.IsNullOrEmpty(ext) ? file : file.Substring(0, file.Length - ext.Length);
        return $"{dir}{name}-{hash}{ext}";
    }

    private async Task<string> UploadInternalAsync(
        string localPath, string remoteKey, IProgress<int>? progress, CancellationToken ct, bool immutable)
    {
        var cfg = await _config.GetAsync();
        const int MaxAttempts = 3;
        Exception? lastEx = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var client = await BuildClientAsync();
                Debug.WriteLine($"[r2.upload] start: key='{remoteKey}', file='{localPath}', immutable={immutable}, attempt={attempt}/{MaxAttempts}");

                await using var fs = File.OpenRead(localPath);
                var req = new PutObjectRequest
                {
                    BucketName = cfg.R2Bucket,
                    Key = remoteKey,
                    InputStream = fs,
                    AutoCloseStream = false,
                    DisableDefaultChecksumValidation = true,
                    DisablePayloadSigning = true,
                    UseChunkEncoding = false,
                    ContentType = ContentTypeFromKey(remoteKey),
                };
                req.Headers.CacheControl = immutable
                    ? "public, max-age=31536000, immutable"
                    : "public, max-age=300";
                req.StreamTransferProgress += (_, e) =>
                {
                    if (e.TotalBytes > 0) progress?.Report((int)((e.TransferredBytes * 100L) / e.TotalBytes));
                };
                var resp = await client.PutObjectAsync(req, ct);
                Debug.WriteLine($"[r2.upload] ok: status={resp.HttpStatusCode}, etag={resp.ETag}, attempt={attempt}");
                return $"{cfg.R2PublicUrl.TrimEnd('/')}/{remoteKey}";
            }
            catch (AmazonS3Exception aex) when (IsTransientS3Error(aex) && attempt < MaxAttempts)
            {
                lastEx = aex;
                var backoffMs = 500 * attempt;
                Debug.WriteLine($"[r2.upload] transient S3 {aex.StatusCode}/{aex.ErrorCode} on attempt {attempt}, retrying in {backoffMs}ms: {aex.Message}");
                await Task.Delay(backoffMs, ct);
            }
            catch (HttpRequestException hex) when (attempt < MaxAttempts)
            {
                lastEx = hex;
                var backoffMs = 500 * attempt;
                Debug.WriteLine($"[r2.upload] transient HTTP error on attempt {attempt}, retrying in {backoffMs}ms: {hex.Message}");
                await Task.Delay(backoffMs, ct);
            }
            catch (TaskCanceledException tex) when (!ct.IsCancellationRequested && attempt < MaxAttempts)
            {
                lastEx = tex;
                var backoffMs = 500 * attempt;
                Debug.WriteLine($"[r2.upload] timeout on attempt {attempt}, retrying in {backoffMs}ms");
                await Task.Delay(backoffMs, ct);
            }
            catch (AmazonS3Exception aex)
            {
                Debug.WriteLine($"[r2.upload] S3-FAIL final ({aex.StatusCode}/{aex.ErrorCode}): {aex.Message}");
                throw new InvalidOperationException(
                    $"R2 upload failed [{aex.StatusCode} {aex.ErrorCode}]: {aex.Message}", aex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[r2.upload] FAIL ({ex.GetType().Name}): {ex.Message}");
                throw new InvalidOperationException($"R2 upload failed: {ex.Message}", ex);
            }
        }

        throw new InvalidOperationException(
            $"R2 upload failed after {MaxAttempts} attempts: {lastEx?.Message}", lastEx);
    }

    private static bool IsTransientS3Error(AmazonS3Exception aex)
    {
        var status = (int)aex.StatusCode;
        if (status == 0) return true;
        if (status >= 500 && status < 600) return true;
        if (status == 408 || status == 429) return true;
        if (aex.ErrorCode is { } code && (
            code.Contains("SlowDown", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("RequestTimeout", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("InternalError", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }

    private static string ContentTypeFromKey(string key)
    {

        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".webp" => "image/webp",
            ".png"  => "image/png",
            ".jpg"  => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif"  => "image/gif",
            ".svg"  => "image/svg+xml",
            ".glb"  => "model/gltf-binary",
            ".gltf" => "model/gltf+json",
            ".json" => "application/json",
            ".xml"  => "application/xml",
            ".zip"  => "application/zip",
            ".rar"  => "application/x-rar-compressed",
            ".rpf"  => "application/octet-stream",
            ".mp4"  => "video/mp4",
            ".webm" => "video/webm",
            _       => "application/octet-stream",
        };
    }

    public async Task DeleteAsync(string remoteKey, CancellationToken ct)
    {
        var cfg = await _config.GetAsync();
        if (string.IsNullOrWhiteSpace(cfg.R2AccessKey)) return;

        using var client = await BuildClientAsync();
        Debug.WriteLine($"[r2.delete] key='{remoteKey}'");
        var resp = await client.DeleteObjectAsync(cfg.R2Bucket, remoteKey, ct);
        Debug.WriteLine($"[r2.delete] ok: status={resp.HttpStatusCode}");
    }

    public async Task<List<string>> ListKeysAsync(string prefix, CancellationToken ct)
    {
        var cfg = await _config.GetAsync();
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(cfg.R2AccessKey)) return result;

        using var client = await BuildClientAsync();
        string? continuationToken = null;
        do
        {
            var listResp = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = cfg.R2Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
                MaxKeys = 1000,
            }, ct);
            if (listResp.S3Objects is { Count: > 0 })
                foreach (var obj in listResp.S3Objects) result.Add(obj.Key);
            continuationToken = listResp.IsTruncated == true ? listResp.NextContinuationToken : null;
        }
        while (continuationToken is not null);
        return result;
    }

    public async Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        var cfg = await _config.GetAsync();
        if (string.IsNullOrWhiteSpace(cfg.R2AccessKey)) return 0;

        using var client = await BuildClientAsync();
        Debug.WriteLine($"[r2.deletePrefix] prefix='{prefix}'");

        int totalDeleted = 0;
        string? continuationToken = null;

        do
        {
            var listResp = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = cfg.R2Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
                MaxKeys = 1000,
            }, ct);

            if (listResp.S3Objects is { Count: > 0 })
            {
                var batch = listResp.S3Objects
                    .Select(o => new Amazon.S3.Model.KeyVersion { Key = o.Key })
                    .ToList();
                try
                {
                    var delReq = new DeleteObjectsRequest
                    {
                        BucketName = cfg.R2Bucket,
                        Objects    = batch,
                        Quiet      = true,
                    };
                    var delResp = await client.DeleteObjectsAsync(delReq, ct);
                    totalDeleted += batch.Count;
                    Debug.WriteLine($"[r2.deletePrefix] batched {batch.Count} delete(s) in 1 call, status={delResp.HttpStatusCode}");
                }
                catch (DeleteObjectsException dex)
                {
                    Debug.WriteLine($"[r2.deletePrefix] batch had partial failures, falling back to per-object");
                    int ok = 0;
                    foreach (var obj in batch)
                    {
                        if (ct.IsCancellationRequested) break;
                        try { await client.DeleteObjectAsync(cfg.R2Bucket, obj.Key, ct); ok++; }
                        catch (Exception ex) { Debug.WriteLine($"[r2.deletePrefix] FAIL on '{obj.Key}': {ex.Message}"); }
                    }
                    totalDeleted += ok;
                    _ = dex;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[r2.deletePrefix] batch FAIL: {ex.Message} - falling back to per-object");
                    int ok = 0;
                    foreach (var obj in batch)
                    {
                        if (ct.IsCancellationRequested) break;
                        try { await client.DeleteObjectAsync(cfg.R2Bucket, obj.Key, ct); ok++; }
                        catch (Exception inner) { Debug.WriteLine($"[r2.deletePrefix] FAIL on '{obj.Key}': {inner.Message}"); }
                    }
                    totalDeleted += ok;
                }
            }

            continuationToken = listResp.IsTruncated == true ? listResp.NextContinuationToken : null;
        }
        while (continuationToken is not null);

        Debug.WriteLine($"[r2.deletePrefix] removed {totalDeleted} object(s) under '{prefix}'");
        return totalDeleted;
    }

    public async Task<int> DeletePrefixExceptAsync(string prefix, IReadOnlyList<string> preservePrefixes, CancellationToken ct)
    {
        var cfg = await _config.GetAsync();
        if (string.IsNullOrWhiteSpace(cfg.R2AccessKey)) return 0;

        var preserve = preservePrefixes ?? Array.Empty<string>();
        using var client = await BuildClientAsync();
        Debug.WriteLine($"[r2.deletePrefixExcept] prefix='{prefix}' preserving={preserve.Count}");

        int totalDeleted = 0, totalPreserved = 0;
        string? continuationToken = null;

        do
        {
            var listResp = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = cfg.R2Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
                MaxKeys = 1000,
            }, ct);

            if (listResp.S3Objects is { Count: > 0 })
            {
                var toDelete = new List<Amazon.S3.Model.KeyVersion>();
                foreach (var o in listResp.S3Objects)
                {
                    bool keep = false;
                    foreach (var pp in preserve)
                    {
                        if (!string.IsNullOrEmpty(pp) && o.Key.StartsWith(pp, StringComparison.Ordinal))
                        {
                            keep = true;
                            break;
                        }
                    }
                    if (keep) { totalPreserved++; continue; }
                    toDelete.Add(new Amazon.S3.Model.KeyVersion { Key = o.Key });
                }

                if (toDelete.Count > 0)
                {
                    try
                    {
                        var delReq = new DeleteObjectsRequest
                        {
                            BucketName = cfg.R2Bucket,
                            Objects = toDelete,
                            Quiet = true,
                        };
                        var delResp = await client.DeleteObjectsAsync(delReq, ct);
                        totalDeleted += toDelete.Count;
                        Debug.WriteLine($"[r2.deletePrefixExcept] batched {toDelete.Count} delete(s), status={delResp.HttpStatusCode}");
                    }
                    catch (DeleteObjectsException dex)
                    {
                        Debug.WriteLine($"[r2.deletePrefixExcept] batch had partial failures, falling back to per-object");
                        int ok = 0;
                        foreach (var obj in toDelete)
                        {
                            if (ct.IsCancellationRequested) break;
                            try { await client.DeleteObjectAsync(cfg.R2Bucket, obj.Key, ct); ok++; }
                            catch (Exception ex) { Debug.WriteLine($"[r2.deletePrefixExcept] FAIL on '{obj.Key}': {ex.Message}"); }
                        }
                        totalDeleted += ok;
                        _ = dex;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[r2.deletePrefixExcept] batch FAIL: {ex.Message} - falling back to per-object");
                        int ok = 0;
                        foreach (var obj in toDelete)
                        {
                            if (ct.IsCancellationRequested) break;
                            try { await client.DeleteObjectAsync(cfg.R2Bucket, obj.Key, ct); ok++; }
                            catch (Exception inner) { Debug.WriteLine($"[r2.deletePrefixExcept] FAIL on '{obj.Key}': {inner.Message}"); }
                        }
                        totalDeleted += ok;
                    }
                }
            }

            continuationToken = listResp.IsTruncated == true ? listResp.NextContinuationToken : null;
        }
        while (continuationToken is not null);

        Debug.WriteLine($"[r2.deletePrefixExcept] under '{prefix}': deleted={totalDeleted}, preserved={totalPreserved}");
        return totalDeleted;
    }

    public async Task<bool> EnsureCorsAsync(CancellationToken ct)
    {
        var cfg = await _config.GetAsync();
        if (string.IsNullOrWhiteSpace(cfg.R2AccessKey) || string.IsNullOrWhiteSpace(cfg.R2Bucket))
        {
            Debug.WriteLine("[r2.cors] skipped - R2 credentials/bucket not configured");
            return false;
        }

        using var client = await BuildClientAsync();
        var req = new PutCORSConfigurationRequest
        {
            BucketName = cfg.R2Bucket,
            Configuration = new CORSConfiguration
            {
                Rules = new List<CORSRule>
                {
                    new CORSRule
                    {
                        Id             = "hntgraph-public-read",
                        AllowedOrigins = new List<string> { "*" },
                        AllowedMethods = new List<string> { "GET", "HEAD" },
                        AllowedHeaders = new List<string> { "*" },
                        ExposeHeaders  = new List<string> { "Content-Length", "Content-Range", "ETag" },
                        MaxAgeSeconds  = 3600,
                    },
                },
            },
        };
        try
        {
            var resp = await client.PutCORSConfigurationAsync(req, ct);
            Debug.WriteLine($"[r2.cors] applied - status={resp.HttpStatusCode}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[r2.cors] FAIL: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}
