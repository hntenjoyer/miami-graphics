using System.Diagnostics;
using System.Net;
using System.Net.Http;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services;

public static class BypassTester
{

    private const long ProbeRangeBytes = 256 * 1024;

    private const string CdnUrl =
        "https://cdn.miamigraphicsstorage.uk/releases/MiamiGraphicsRenderer_1.0.0.zip";

    public enum Strategy
    {
        Baseline = 0,
        TlsFrag3x = 1,
        TlsFrag8x = 2,
        DohCloudflare = 3,
        DohQuad9 = 4,
        DohNextDns = 5,
    }

    public sealed record Result(
        int    StrategyId,
        string StrategyLabel,
        string TargetUrl,
        bool   Success,
        long   ConnectMs,
        long   FirstByteMs,
        long   TotalMs,
        long   BytesReceived,
        double Kbps,
        int    HttpStatusCode,
        string? ErrorMessage);

    public static async Task<Result> RunAsync(Strategy strategy, CancellationToken ct = default)
    {
        var (label, url, handler) = strategy switch
        {
            Strategy.Baseline      => (Loc.T("bypass.baseline"),  CdnUrl, (HttpMessageHandler)new SocketsHttpHandler()),
            Strategy.TlsFrag3x     => ("TLS fragmentation 3×",   CdnUrl, new FragmentingHttpHandler(3, 0)),
            Strategy.TlsFrag8x     => ("TLS fragmentation 8×",   CdnUrl, new FragmentingHttpHandler(8, 0)),
            Strategy.DohCloudflare => ("DoH Cloudflare (1.1.1.1)", CdnUrl,
                new DohResolvingHttpHandler(new[] { DohResolvingHttpHandler.Providers.Cloudflare })),
            Strategy.DohQuad9      => ("DoH Quad9 (9.9.9.9)",   CdnUrl,
                new DohResolvingHttpHandler(new[] { DohResolvingHttpHandler.Providers.Quad9 })),
            Strategy.DohNextDns    => ("DoH NextDNS",            CdnUrl,
                new DohResolvingHttpHandler(new[] { DohResolvingHttpHandler.Providers.NextDns })),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
        };

        using var http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        var total = Stopwatch.StartNew();
        long connectMs = 0, firstByteMs = 0;
        long bytesReceived = 0;
        int statusCode = 0;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, ProbeRangeBytes - 1);

            req.Headers.TryAddWithoutValidation(
                "X-Test-Probe", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

            var connectSw = Stopwatch.StartNew();
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            connectMs = connectSw.ElapsedMilliseconds;

            statusCode = (int)resp.StatusCode;
            if (resp.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            {
                total.Stop();
                return new Result((int)strategy, label, url, false,
                    connectMs, connectMs, total.ElapsedMilliseconds,
                    0, 0, statusCode, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }

            firstByteMs = connectMs;

            var bodySw = Stopwatch.StartNew();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buf = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                bytesReceived += read;
                if (bytesReceived >= ProbeRangeBytes) break;
            }
            bodySw.Stop();
            total.Stop();

            double seconds = Math.Max(bodySw.ElapsedMilliseconds, 1) / 1000.0;
            double kbps = bytesReceived / 1024.0 / seconds;

            return new Result((int)strategy, label, url, true,
                connectMs, firstByteMs, total.ElapsedMilliseconds,
                bytesReceived, Math.Round(kbps, 1), statusCode, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            total.Stop();
            return new Result((int)strategy, label, url, false,
                connectMs, firstByteMs, total.ElapsedMilliseconds,
                bytesReceived, 0, statusCode, Loc.T("error.cancelledCap"));
        }
        catch (TaskCanceledException ex)
        {

            total.Stop();
            return new Result((int)strategy, label, url, false,
                connectMs, firstByteMs, total.ElapsedMilliseconds,
                bytesReceived, 0, statusCode, Loc.T("error.timeout20s", ("reason", ex.Message)));
        }
        catch (Exception ex)
        {
            total.Stop();
            return new Result((int)strategy, label, url, false,
                connectMs, firstByteMs, total.ElapsedMilliseconds,
                bytesReceived, 0, statusCode, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
