using System.Diagnostics;
using System.IO;
using System.Net.Http;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.System;

namespace MiamiGraphics.Shell.Services;

public sealed class SupabaseBackupSource : IBackupSource
{

    private static readonly HttpClient Http = new(new FragmentingHttpHandler()) { Timeout = TimeSpan.FromMinutes(20) };

    private static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(15);

    private readonly SupabaseClient _supabase;
    private readonly IBackupSource _fallback;

    public SupabaseBackupSource(SupabaseClient supabase, IBackupSource fallback)
    {
        _supabase = supabase;
        _fallback = fallback;
    }

    public Task<Stream> GetCleanUpdateRpfAsync(string exeVersion, IProgress<int>? progress, CancellationToken ct)
        => GetCleanUpdateRpfAsync(exeVersion, progress, null, ct);

    public async Task<Stream> GetCleanUpdateRpfAsync(string exeVersion, IProgress<int>? progress,
        Action<long, long>? bytesProgress, CancellationToken ct)
    {

        var row = await FetchLatestRowAsync(ct);
        var url = row?.CleanUpdateUrl;
        Debug.WriteLine($"[backup.r2] latest gta_versions row → url={url?[..Math.Min(60, url?.Length ?? 0)] ?? "(null)"}…, expectedSha={row?.UpdateRpfSha256?[..Math.Min(16, row.UpdateRpfSha256?.Length ?? 0)] ?? "(null)"}");

        if (string.IsNullOrWhiteSpace(url) || IsPlaceholder(url))
        {
            Debug.WriteLine($"[backup.r2] no clean_update_url in latest row - trying local source (exeVersion={exeVersion} ignored)");
            return await _fallback.GetCleanUpdateRpfAsync(exeVersion, progress, bytesProgress, ct);
        }

        return await DownloadAsync(url!, progress, bytesProgress, ct);
    }

    public async Task<(string? sha256, long size)> GetCleanUpdateRpfMetaAsync(string exeVersion, CancellationToken ct)
    {
        var row = await FetchLatestRowAsync(ct);
        return (row?.UpdateRpfSha256, row?.UpdateRpfSize ?? 0);
    }

    private async Task<UpdateRow?> FetchLatestRowAsync(CancellationToken ct)
    {
        try
        {
            var rows = await _supabase.SelectAsync<UpdateRow>(
                "gta_versions",
                "select=clean_update_url,update_rpf_sha256,update_rpf_size&clean_update_url=not.is.null&order=updated_at.desc&limit=1",
                ct);
            return rows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[backup.r2] gta_versions lookup FAIL ({ex.GetType().Name}): {ex.Message}");
            return null;
        }
    }

    public Task<Stream> GetCleanDlcRpfAsync(IProgress<int>? progress, CancellationToken ct)
        => GetCleanDlcRpfAsync(progress, null, ct);

    public async Task<Stream> GetCleanDlcRpfAsync(IProgress<int>? progress,
        Action<long, long>? bytesProgress, CancellationToken ct)
    {

        string? url = null;
        try
        {
            var rows = await _supabase.SelectAsync<DlcRow>(
                "gta_versions",
                "select=guns_rpf_url&guns_rpf_url=not.is.null&limit=1",
                ct);
            url = rows.FirstOrDefault()?.GunsRpfUrl;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[backup.r2] gta_versions guns_rpf lookup FAIL ({ex.GetType().Name}): {ex.Message} - trying fallback");
        }

        if (string.IsNullOrWhiteSpace(url) || IsPlaceholder(url))
        {
            Debug.WriteLine("[backup.r2] no guns_rpf_url - falling back to local source");
            return await _fallback.GetCleanDlcRpfAsync(progress, bytesProgress, ct);
        }

        return await DownloadAsync(url!, progress, bytesProgress, ct);
    }

    private async Task<Stream> DownloadAsync(string url, IProgress<int>? progress,
        Action<long, long>? onBytes, CancellationToken ct)
    {
        progress?.Report(0);

        try { await MirrorSelector.EnsureSelectedAsync(ct).WaitAsync(TimeSpan.FromSeconds(6), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch {}

        var candidates = MiamiGraphics.Shell.Bridge.AppBridge.BuildDownloadCandidates(url);

        const string RuVpsHost = "ru.miamigraphicsstorage.uk";
        var ruIdx = candidates.FindIndex(c =>
            Uri.TryCreate(c, UriKind.Absolute, out var u)
            && string.Equals(u.Host, RuVpsHost, StringComparison.OrdinalIgnoreCase));
        if (ruIdx > 1)
        {
            var ruUrl = candidates[ruIdx];
            candidates.RemoveAt(ruIdx);
            candidates.Insert(1, ruUrl);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics");

        EnforceCleanDownloadLimit(tempDir);

        var tempPath = Path.Combine(tempDir,
            $"clean_dl_{Guid.NewGuid():N}.rpf.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        Exception? last = null;
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                try
                {
                    if (i > 0 || attempt > 1)
                        Debug.WriteLine($"[backup.r2] host fallback {i}/{candidates.Count - 1} (attempt {attempt}): {candidate}");

                    using var stallCts = new CancellationTokenSource();
                    stallCts.CancelAfter(NoProgressTimeout);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, stallCts.Token);
                    long lastBytes = -1;
                    long fwdMax = 0;
                    long fwdAtMs = 0;

                    await ParallelDownloader.DownloadAsync(candidate, tempPath,
                        bytesProgress: (received, total) =>
                        {
                            if (received > lastBytes)
                            {
                                lastBytes = received;
                                stallCts.CancelAfter(NoProgressTimeout);
                            }
                            if (total > 0)
                                progress?.Report((int)((received * 100L) / total));

                            if (onBytes is null) return;
                            var prevMax = Interlocked.Read(ref fwdMax);
                            if (received <= prevMax) return;
                            if (Interlocked.CompareExchange(ref fwdMax, received, prevMax) != prevMax) return;
                            var nowMs = Environment.TickCount64;
                            if (received < total && nowMs - Interlocked.Read(ref fwdAtMs) < 200) return;
                            Interlocked.Exchange(ref fwdAtMs, nowMs);
                            onBytes(received, total);
                        },
                        ct: linked.Token);
                    return new AutoDeleteFileStream(tempPath);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch {  }
                    throw;
                }
                catch (OperationCanceledException oce)
                {
                    last = new IOException(
                        Loc.T("net.hostStalled", ("sec", NoProgressTimeout.TotalSeconds.ToString("F0")), ("host", candidate)), oce);
                    Debug.WriteLine($"[backup.r2] {candidate} stalled (no progress {NoProgressTimeout.TotalSeconds}s, attempt {attempt}) -> next mirror");
                }
                catch (Exception ex)
                {
                    last = ex;
                    Debug.WriteLine($"[backup.r2] candidate failed ({candidate}, attempt {attempt}): {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (attempt < maxAttempts && MiamiGraphics.Shell.Bridge.AppBridge.IsTransientNetworkError(last))
            {
                try { await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                continue;
            }
            break;
        }

        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch {  }
        throw last ?? new IOException(Loc.T("error.allMirrorsDownForBackup", ("url", url)));
    }

    private const int MaxCleanDownloads = 3;

    private static void EnforceCleanDownloadLimit(string dir)
    {
        string[] existing;
        try
        {
            existing = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "clean_dl_*")
                : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[backup.r2] clean-download limit check skipped: {ex.Message}");
            return;
        }

        if (existing.Length >= MaxCleanDownloads)
            throw new IOException(
                Loc.T("error.cleanDownloadsLimitExceeded", ("count", existing.Length), ("max", MaxCleanDownloads), ("dir", dir)));
    }

    private static bool IsPlaceholder(string url)

        => url.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
           || !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private sealed class UpdateRow
    {
        public string? CleanUpdateUrl  { get; set; }
        public string? UpdateRpfSha256 { get; set; }
        public long    UpdateRpfSize   { get; set; }
    }

    private sealed class DlcRow
    {
        public string? GunsRpfUrl { get; set; }
    }

    private sealed class AutoDeleteFileStream : FileStream
    {
        private readonly string _path;
        public AutoDeleteFileStream(string path)
            : base(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, useAsync: true)
        {
            _path = path;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                TryDelete();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
            TryDelete();
        }

        private void TryDelete()
        {
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (Exception ex) { Debug.WriteLine($"[backup.r2] temp cleanup failed for {_path}: {ex.Message}"); }
        }
    }

    private sealed class ResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;
        private readonly long? _length;

        public ResponseStream(Stream inner, HttpResponseMessage response, long? length)
        {
            _inner = inner;
            _response = response;
            _length = length;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length ?? throw new NotSupportedException();
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            _response.Dispose();
            await base.DisposeAsync();
        }
    }
}
