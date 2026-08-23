using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using MiamiGraphics.Core.I18n;
using Microsoft.Win32.SafeHandles;

namespace MiamiGraphics.Shell.Services;

public static class ParallelDownloader
{
    private const long DefaultThresholdBytes = 8L * 1024 * 1024;

    private const int DefaultParallelism = 8;

    private const int BufferSize = 1 << 19;

    private const int ReadStallTimeoutSec = 30;

    private const int HeaderStallTimeoutSec = 30;

    private const int SpeedWindowSec = 30;

    private const long DeadWindowBytes = 60L * 1024 * SpeedWindowSec;

    private const long SlowWindowBytes = 5L * 1024 * 1024;

    private static readonly TimeSpan MinCandidateBudget = TimeSpan.FromMinutes(8);

    private static readonly TimeSpan MaxCandidateBudget = TimeSpan.FromMinutes(120);

    private const long BudgetFloorBytesPerSec = 64L * 1024;

    private static TimeSpan BudgetFor(long total)
    {
        if (total <= 0) return MinCandidateBudget;
        var sec = Math.Clamp(total / (double)BudgetFloorBytesPerSec,
                             MinCandidateBudget.TotalSeconds, MaxCandidateBudget.TotalSeconds);
        return TimeSpan.FromSeconds(sec);
    }

    public static Action<string, double>? OnSlowDownload;

    private sealed class TooManyStreamsException : IOException
    {
        public TooManyStreamsException(int status) : base(Loc.T("net.tooManyStreams", ("status", status))) { }
    }

    private sealed class RangeIgnoredException : Exception
    {
        public RangeIgnoredException() : base(Loc.T("net.rangeIgnored")) { }
    }

    public sealed class FileNotOnMirrorException : HttpRequestException
    {
        public FileNotOnMirrorException(string host)
            : base(Loc.T("net.fileNotOnMirror", ("host", host)), null, HttpStatusCode.NotFound) { }
    }

    private static async Task<HttpResponseMessage> SendWithHeaderTimeoutAsync(
        HttpClient http, HttpRequestMessage req, CancellationToken ct)
    {
        using var hdrCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        hdrCts.CancelAfter(TimeSpan.FromSeconds(HeaderStallTimeoutSec));
        try
        {
            return await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, hdrCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (hdrCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new IOException($"no response headers for {HeaderStallTimeoutSec}s from {req.RequestUri?.Host}");
        }
    }

    public static async Task DownloadAsync(
        string url,
        string destPath,
        Action<long, long>? bytesProgress = null,
        int parallelism = DefaultParallelism,
        long thresholdBytes = DefaultThresholdBytes,
        bool resumable = true,
        CancellationToken ct = default)
    {
        long total;
        bool acceptsRange;
        try
        {
            using var headHttp = HttpClientFactory.CreateFragmenting(TimeSpan.FromSeconds(30));
            using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResp = await headHttp.SendAsync(headReq, ct).ConfigureAwait(false);

            if (headResp.StatusCode == HttpStatusCode.NotFound)
            {
                DownloadLog.Write("chunk", $"{SafeHost(url)}: файла нет (404 на HEAD)");
                throw new FileNotOnMirrorException(SafeHost(url));
            }

            headResp.EnsureSuccessStatusCode();

            total = headResp.Content.Headers.ContentLength ?? -1;
            acceptsRange = headResp.Headers.AcceptRanges.Any(a =>
                string.Equals(a, "bytes", StringComparison.OrdinalIgnoreCase));
        }
        catch (FileNotOnMirrorException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[parallel-dl] HEAD probe failed for {url}: {ex.Message} - fallback single-stream");
            DownloadResumeMap.DiscardFor(destPath);
            await SingleStreamDownloadAsync(url, destPath, bytesProgress, ct).ConfigureAwait(false);
            return;
        }

        if (total < thresholdBytes || !acceptsRange)
        {
            Debug.WriteLine($"[parallel-dl] {url}: total={total} acceptsRange={acceptsRange} - single-stream");
            DownloadResumeMap.DiscardFor(destPath);
            await SingleStreamDownloadAsync(url, destPath, bytesProgress, ct).ConfigureAwait(false);
            return;
        }

        if (!resumable) DownloadResumeMap.DiscardFor(destPath);

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        var streams = Math.Max(1, parallelism);
        while (true)
        {
            try
            {
                await RunParallelAsync(url, destPath, total, streams, bytesProgress, resumable, ct).ConfigureAwait(false);
                return;
            }
            catch (TooManyStreamsException ex) when (streams > 1)
            {
                streams = streams >= 4 ? 2 : 1;
                Debug.WriteLine($"[parallel-dl] {url}: {ex.Message} - перезапуск на {streams} потоках");
                DownloadLog.Write("chunk", $"{SafeHost(url)}: {ex.Message} - перезапуск на {streams} потоках");
            }
            catch (RangeIgnoredException)
            {
                Debug.WriteLine($"[parallel-dl] {url}: Range не honored - single-stream");
                DownloadLog.Write("chunk", $"{SafeHost(url)}: сервер вернул 200 вместо 206 (Range не поддержан) - качаю одним потоком");
                DownloadResumeMap.DiscardFor(destPath);
                await SingleStreamDownloadAsync(url, destPath, bytesProgress, ct).ConfigureAwait(false);
                return;
            }
        }
    }

    private static async Task RunParallelAsync(
        string url, string destPath, long total, int parallelism,
        Action<long, long>? bytesProgress, bool resumable, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        using var groupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var budget = BudgetFor(total);
        groupCts.CancelAfter(budget);

        var resume = DownloadResumeMap.OpenOrCreate(destPath, total, resumable, out var resumed);

        long received = resume.DoneBytes();
        var slowSignalled = 0;
        var deadChannel = 0;

        if (received > 0) bytesProgress?.Invoke(received, total);

        using (var handle = File.OpenHandle(destPath,
                   resumed ? FileMode.Open : FileMode.Create, FileAccess.Write, FileShare.None,
                   FileOptions.Asynchronous, preallocationSize: resumed ? 0 : total))
        {
            RandomAccess.SetLength(handle, total);

            var blockCount = resume.BlockCount;
            var blocksPerChunk = Math.Max(1, (int)Math.Ceiling(blockCount / (double)parallelism));
            var chunkCount = Math.Max(1, (int)Math.Ceiling(blockCount / (double)blocksPerChunk));
            var tasks = new Task[chunkCount];

            var watchdog = Task.Run(async () =>
            {
                var host = SafeHost(url);
                long windowStart = 0;
                var grace = true;
                while (!groupCts.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(SpeedWindowSec), groupCts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }

                    var now = Interlocked.Read(ref received);
                    var delta = now - windowStart;
                    windowStart = now;
                    if (now >= total) return;

                    var kbps = delta / 1024d / SpeedWindowSec;
                    if (delta < SlowWindowBytes && Interlocked.Exchange(ref slowSignalled, 1) == 0)
                    {
                        Debug.WriteLine($"[parallel-dl] {host}: медленно, {kbps:F0} КБ/с - зову диагностику");
                        DownloadLog.Write("chunk", $"{host}: медленно - {kbps:F0} КБ/с за окно {SpeedWindowSec} с, зову диагностику");
                        try { OnSlowDownload?.Invoke(host, kbps); } catch { }
                    }

                    if (grace) { grace = false; continue; }
                    if (delta < DeadWindowBytes)
                    {
                        Debug.WriteLine($"[parallel-dl] {host}: канал мёртв, {delta / 1024} КБ за {SpeedWindowSec}с - рвём кандидата");
                        DownloadLog.Write("chunk", $"{host}: канал мёртв - {delta / 1024} КБ за {SpeedWindowSec} с, рву кандидата");
                        Interlocked.Exchange(ref deadChannel, 1);
                        groupCts.Cancel();
                        return;
                    }
                }
            }, CancellationToken.None);

            void Progress(long delta)
            {
                var newTotal = Interlocked.Add(ref received, delta);
                bytesProgress?.Invoke(newTotal, total);
            }

            for (var i = 0; i < chunkCount; i++)
            {
                var firstBlock = i * blocksPerChunk;
                var lastBlock = Math.Min(blockCount - 1, firstBlock + blocksPerChunk - 1);
                tasks[i] = DownloadBlockRangeAsync(handle, url, resume, total,
                    firstBlock, lastBlock, Progress, groupCts, ct);
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                foreach (var t in tasks)
                {
                    var verdict = t.Exception?.Flatten().InnerExceptions.FirstOrDefault(e =>
                        e is FileNotOnMirrorException or TooManyStreamsException or RangeIgnoredException);
                    if (verdict is not null) throw verdict;
                }

                if (ex is OperationCanceledException && groupCts.IsCancellationRequested)
                {
                    var dead = Volatile.Read(ref deadChannel) == 1;
                    var reason = dead
                        ? Loc.T("net.channelThrottledWindow", ("kb", DeadWindowBytes / 1024), ("sec", SpeedWindowSec), ("got", Interlocked.Read(ref received).ToString("N0")))
                        : Loc.T("net.serverBudgetExceeded", ("minutes", budget.TotalMinutes.ToString("F0")), ("got", Interlocked.Read(ref received).ToString("N0")), ("total", total.ToString("N0")));
                    if (!dead) DownloadLog.Write("chunk", $"{SafeHost(url)}: {reason}");
                    throw new IOException(reason);
                }
                throw;
            }
            finally
            {
                groupCts.Cancel();
                try { await watchdog.ConfigureAwait(false); } catch { }
                resume.Flush();
            }
        }

        resume.Discard();

        sw.Stop();
        var mbps = total / 1024d / 1024d / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        Debug.WriteLine($"[parallel-dl] {url}: {total / 1024 / 1024} MB in {sw.Elapsed.TotalSeconds:F1}s = {mbps:F1} MB/s ({parallelism} streams)");

        bytesProgress?.Invoke(total, total);
    }

    private static string SafeHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;

    private static async Task DownloadBlockRangeAsync(
        SafeFileHandle handle,
        string url,
        DownloadResumeMap resume,
        long total,
        int firstBlock,
        int lastBlock,
        Action<long> deltaProgress,
        CancellationTokenSource groupCts,
        CancellationToken userCt)
    {
        var b = firstBlock;
        while (b <= lastBlock)
        {
            if (resume.IsDone(b)) { b++; continue; }

            var runStart = b;
            while (b <= lastBlock && !resume.IsDone(b)) b++;
            var runEnd = b - 1;

            var startByte = (long)runStart * DownloadResumeMap.BlockBytes;
            var endByte = Math.Min(total - 1, (long)(runEnd + 1) * DownloadResumeMap.BlockBytes - 1);

            await DownloadChunkAsync(handle, url, startByte, endByte, deltaProgress,
                offset => MarkBlocksBelow(resume, runStart, runEnd, total, offset),
                groupCts, userCt).ConfigureAwait(false);

            MarkBlocksBelow(resume, runStart, runEnd, total, endByte + 1);
        }
    }

    private static void MarkBlocksBelow(DownloadResumeMap resume, int runStart, int runEnd, long total, long offset)
    {
        for (var i = runStart; i <= runEnd; i++)
        {
            var blockEnd = Math.Min(total, (long)(i + 1) * DownloadResumeMap.BlockBytes);
            if (offset < blockEnd) break;
            resume.MarkDone(i);
        }
    }

    private static async Task DownloadChunkAsync(
        SafeFileHandle handle,
        string url,
        long start,
        long end,
        Action<long> deltaProgress,
        Action<long> offsetProgress,
        CancellationTokenSource groupCts,
        CancellationToken userCt)
    {
        const int MaxAttempts = 3;
        Exception? last = null;

        var currentOffset = start;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (currentOffset > end) return;
            try
            {
                await DownloadRangeAttemptAsync(handle, url, currentOffset, end, deltaProgress,
                    groupCts.Token, off => { currentOffset = off; offsetProgress(off); }).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (userCt.IsCancellationRequested)
            {
                throw;
            }
            catch (TooManyStreamsException)
            {
                groupCts.Cancel();
                throw;
            }
            catch (RangeIgnoredException)
            {
                groupCts.Cancel();
                throw;
            }
            catch (FileNotOnMirrorException)
            {
                groupCts.Cancel();
                throw;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                last = ex;
                Debug.WriteLine($"[parallel-dl] chunk [{start}-{end}] attempt {attempt + 1}/{MaxAttempts} failed at offset={currentOffset}: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"[parallel-dl] chunk [{start}-{end}] фатально: {ex.Message}");
                groupCts.Cancel();
                throw;
            }

            if (attempt < MaxAttempts - 1)
            {
                var backoffMs = (int)Math.Pow(3, attempt) * 1000;
                Debug.WriteLine($"[parallel-dl] chunk [{start}-{end}] backoff {backoffMs}ms before retry");
                await Task.Delay(backoffMs, groupCts.Token).ConfigureAwait(false);
            }
        }

        groupCts.Cancel();
        throw last ?? new IOException($"chunk [{start}-{end}] failed after {MaxAttempts} attempts");
    }

    private static async Task DownloadRangeAttemptAsync(
        SafeFileHandle handle,
        string url,
        long start,
        long end,
        Action<long> deltaProgress,
        CancellationToken ct,
        Action<long> resumeOffsetCallback)
    {
        using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromMinutes(30));
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(start, end);

        using var resp = await SendWithHeaderTimeoutAsync(http, req, ct).ConfigureAwait(false);

        if (resp.StatusCode != HttpStatusCode.PartialContent)
        {
            var code = (int)resp.StatusCode;

            if (code is 503 or 429)
                throw new TooManyStreamsException(code);

            if (code == 200)
                throw new RangeIgnoredException();

            if (code == 404)
            {
                DownloadLog.Write("chunk", $"{SafeHost(url)}: файл пропал между HEAD и GET (404) - на хосте идёт заливка?");
                throw new FileNotOnMirrorException(SafeHost(url));
            }

            throw new InvalidOperationException(
                $"Expected 206 Partial Content for Range request {start}-{end}, got {code}");
        }

        var cr = resp.Content.Headers.ContentRange;
        if (cr?.From is { } from && from != start)
            throw new InvalidOperationException(
                Loc.T("net.wrongRangeReturned", ("got", from), ("want", start)));

        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buf = new byte[BufferSize];
        var offset = start;
        while (true)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(ReadStallTimeoutSec));
            int n;
            try
            {
                n = await src.ReadAsync(buf.AsMemory(), readCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (readCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new IOException($"read stalled for {ReadStallTimeoutSec}s at offset {offset}");
            }
            if (n <= 0) break;
            await RandomAccess.WriteAsync(handle, buf.AsMemory(0, n), offset, ct).ConfigureAwait(false);
            offset += n;
            resumeOffsetCallback(offset);
            deltaProgress(n);
        }

        if (offset <= end)
            throw new IOException(Loc.T("net.streamTruncated", ("at", offset), ("total", end + 1)));
    }

    private static async Task SingleStreamDownloadAsync(
        string url, string destPath, Action<long, long>? bytesProgress, CancellationToken ct)
    {
        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var innerCt = budgetCts.Token;

        using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromMinutes(30));
        using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await SendWithHeaderTimeoutAsync(http, getReq, innerCt).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            DownloadLog.Write("chunk", $"{SafeHost(url)}: файла нет (404 на single-stream GET)");
            throw new FileNotOnMirrorException(SafeHost(url));
        }
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        var budget = BudgetFor(total);
        budgetCts.CancelAfter(budget);
        await using var src = await resp.Content.ReadAsStreamAsync(innerCt).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write,
            FileShare.None, BufferSize, useAsync: true);

        var buf = new byte[BufferSize];
        long received = 0;
        long lastReport = 0;
        var sw = Stopwatch.StartNew();
        long windowStartMs = 0, windowStartBytes = 0;
        var grace = true;
        var slowSignalled = false;

        while (true)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(innerCt);
            readCts.CancelAfter(TimeSpan.FromSeconds(ReadStallTimeoutSec));
            int n;
            try
            {
                n = await src.ReadAsync(buf.AsMemory(), readCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (readCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                if (budgetCts.IsCancellationRequested)
                {
                    var reason = Loc.T("net.serverBudgetExceeded",
                        ("minutes", budget.TotalMinutes.ToString("F0")),
                        ("got", received.ToString("N0")),
                        ("total", (total > 0 ? total : received).ToString("N0")));
                    DownloadLog.Write("chunk", $"{SafeHost(url)}: {reason}");
                    throw new IOException(reason);
                }
                throw new IOException($"single-stream read stalled for {ReadStallTimeoutSec}s at {received} bytes");
            }
            if (n <= 0) break;
            await dst.WriteAsync(buf.AsMemory(0, n), innerCt).ConfigureAwait(false);
            received += n;
            if (received - lastReport >= (1 << 19))
            {
                bytesProgress?.Invoke(received, total);
                lastReport = received;
            }

            if (sw.ElapsedMilliseconds - windowStartMs >= SpeedWindowSec * 1000)
            {
                var delta = received - windowStartBytes;
                windowStartMs = sw.ElapsedMilliseconds;
                windowStartBytes = received;

                if (delta < SlowWindowBytes && !slowSignalled)
                {
                    slowSignalled = true;
                    DownloadLog.Write("chunk",
                        $"{SafeHost(url)}: медленно - {delta / 1024d / SpeedWindowSec:F0} КБ/с за окно {SpeedWindowSec} с, зову диагностику");
                    try { OnSlowDownload?.Invoke(SafeHost(url), delta / 1024d / SpeedWindowSec); } catch { }
                }
                if (!grace && delta < DeadWindowBytes)
                {
                    DownloadLog.Write("chunk",
                        $"{SafeHost(url)}: канал мёртв - {delta / 1024} КБ за {SpeedWindowSec} с, рву кандидата");
                    throw new IOException(
                        Loc.T("net.channelThrottled", ("kb", delta / 1024), ("sec", SpeedWindowSec), ("got", received.ToString("N0"))));
                }
                grace = false;
            }
        }
        bytesProgress?.Invoke(received, total > 0 ? total : received);
    }
}
