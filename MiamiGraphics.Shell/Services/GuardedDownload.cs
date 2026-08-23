using System.Diagnostics;
using System.IO;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services;

public static class GuardedDownload
{
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(15);

    private const long SpeedWindowMs  = 25_000;
    private const long MinWindowBytes = 50L * 1024 * SpeedWindowMs / 1000;

    public static async Task CopyAsync(
        Stream src,
        Stream dst,
        Action<long>? onBytes,
        CancellationToken ct,
        bool throttleGuard = true,
        long expectedTotal = -1)
    {
        var buf = new byte[1 << 18];
        long done = 0;
        var sw = Stopwatch.StartNew();
        long windowStartMs = 0, windowStartBytes = 0;
        bool firstWindow = true;

        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stallCts.CancelAfter(StallTimeout);

        while (true)
        {
            int n;
            try
            {
                n = await src.ReadAsync(buf.AsMemory(), stallCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException(
                    Loc.T("net.streamStalled", ("sec", StallTimeout.TotalSeconds.ToString("F0")), ("got", done.ToString("N0"))));
            }
            if (n <= 0) break;

            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            done += n;
            stallCts.CancelAfter(StallTimeout);
            onBytes?.Invoke(done);

            if (throttleGuard && (expectedTotal <= 0 || done < expectedTotal)
                && sw.ElapsedMilliseconds - windowStartMs >= SpeedWindowMs)
            {
                long windowBytes = done - windowStartBytes;
                if (!firstWindow && windowBytes < MinWindowBytes)
                    throw new IOException(
                        Loc.T("net.channelThrottled", ("kb", windowBytes / 1024), ("sec", SpeedWindowMs / 1000), ("got", done.ToString("N0"))));
                firstWindow = false;
                windowStartMs = sw.ElapsedMilliseconds;
                windowStartBytes = done;
            }
        }
    }
}
