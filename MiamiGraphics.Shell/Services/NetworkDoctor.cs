using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using MiamiGraphics.Core.I18n;
using MirrorSelector = MiamiGraphics.Core.System.MirrorSelector;

namespace MiamiGraphics.Shell.Services;

public static class NetworkDoctor
{
    private const string HotProbePath = "gta_versions/1.0.3751.0/guns.rpf";

    private const long HotProbeBytes = 2L * 1024 * 1024;
    private const int  RequestTimeoutSec = 25;
    private const int  ConcurrencyProbe = 8;

    private const double StorageSlowKbPerSec = 256;

    public sealed record NodeResult(
        string Id,
        string Label,
        string Host,
        string Role,
        bool   Ok,
        int    HttpStatus,
        string? Ip,
        long   DnsMs,
        long   ConnectMs,
        long   TtfbMs,
        long   TotalMs,
        bool   RangeOk,
        long   Bytes,
        double KbPerSec,
        int    StreamsAccepted,
        int    StreamsRefused,
        long   ColdHeadTtfbMs,
        long   ColdMidTtfbMs,
        bool   ColdOk,
        string? Error);

    public sealed record HubResult(
        bool    Ok,
        string? NodeGiven,
        string? UrlGiven,
        long    Ms,
        string? Status,
        string? Error);

    public sealed record Report(
        string                     StartedAtUtc,
        long                       TotalMs,
        List<NodeResult>           Nodes,
        HubResult?                 Hub,
        Dictionary<string, string> Env,
        List<string>               Problems,
        string                     Verdict,
        string?                    BestHost,
        string?                    ColdProbeUrl);

    private sealed record Node(string Id, string LabelKey, string Host, string Role);

    private static readonly Node[] Nodes =
    {
        new("rf",   "net.nodeRfStorage",   MirrorSelector.RuStorageHost,  "ru"),
        new("cdn",  "net.nodeCdn",         "cdn.miamigraphicsstorage.uk", "cf"),
        new("apex", "net.nodeApex",        "miamigraphicsstorage.uk",     "cf"),
        new("r2",   "net.nodeR2Direct",    "pub-f3641b214c164277964c1e92c826b19b.r2.dev", "r2"),
        new("ru1",  "net.nodeRu1",  "ru.miamigraphicsstorage.uk",  "ru"),
        new("spb1", "net.nodeSpb1", "spb1.miamigraphicsstorage.uk","ru"),
        new("spb2", "net.nodeSpb2", "spb2.miamigraphicsstorage.uk","ru"),
        new("msk",  "net.nodeMsk",  "msk.miamigraphicsstorage.uk", "ru"),
        new("hnt",  "net.nodeHnt",  "hnt.miamigraphicsstorage.uk", "eu"),
        new("eu",   "net.nodeEu",   "eu.miamigraphicsstorage.uk",  "eu"),
    };

    private static string RegionStorageHostSafe()
    {
        try { return MirrorSelector.RegionStorageHost; } catch { return string.Empty; }
    }

    private static string LabelFor(Node n)
    {
        var storage = RegionStorageHostSafe();
        var label = Loc.T(n.LabelKey);
        return storage.Length > 0 && string.Equals(n.Host, storage, StringComparison.OrdinalIgnoreCase)
            ? Loc.T("net.labelPrimarySource", ("label", label))
            : label;
    }

    public static async Task<Report> RunAsync(string? coldProbeUrl = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow.ToString("O");

        var coldPath = ExtractPath(coldProbeUrl);

        var nodeTasks = Nodes.Select(n => ProbeNodeAsync(n, coldPath, ct)).ToArray();
        var hubTask = ProbeHubAsync(coldPath, ct);

        var nodes = (await Task.WhenAll(nodeTasks).ConfigureAwait(false)).ToList();
        var hub = await hubTask.ConfigureAwait(false);

        var env = CollectEnv();
        var (problems, verdict, best) = Judge(nodes, hub, coldPath != null);

        sw.Stop();
        return new Report(startedAt, sw.ElapsedMilliseconds, nodes, hub, env, problems, verdict, best, coldProbeUrl);
    }

    private static string? ExtractPath(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath.TrimStart('/') : null;

    private static async Task<NodeResult> ProbeNodeAsync(Node n, string? coldPath, CancellationToken ct)
    {
        var label = LabelFor(n);

        long dnsMs = 0;
        string? ip = null;
        try
        {
            var t = Stopwatch.StartNew();
            var addrs = await Dns.GetHostAddressesAsync(n.Host, ct).ConfigureAwait(false);
            t.Stop();
            dnsMs = t.ElapsedMilliseconds;
            ip = addrs.FirstOrDefault()?.ToString();
        }
        catch (Exception ex)
        {
            return new NodeResult(n.Id, label, n.Host, n.Role, false, 0, null,
                0, 0, 0, 0, false, 0, 0, 0, 0, 0, 0, false, Loc.T("net.dnsUnresolved", ("reason", Short(ex))));
        }

        long connectMs = 0;
        try
        {
            var t = Stopwatch.StartNew();
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(n.Host, 443, ct).ConfigureAwait(false);
            t.Stop();
            connectMs = t.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            return new NodeResult(n.Id, label, n.Host, n.Role, false, 0, ip,
                dnsMs, 0, 0, 0, false, 0, 0, 0, 0, 0, 0, false, Loc.T("net.port443Closed", ("reason", Short(ex))));
        }

        var hot = await TimedRangeAsync($"https://{n.Host}/{HotProbePath}", 0, HotProbeBytes - 1, ct)
            .ConfigureAwait(false);

        if (!hot.Ok)
        {
            return new NodeResult(n.Id, label, n.Host, n.Role, false, hot.Status, ip,
                dnsMs, connectMs, hot.TtfbMs, hot.TotalMs, false, hot.Bytes, 0, 0, 0, 0, 0, false, hot.Error);
        }

        var kbps = hot.Bytes / 1024d / Math.Max(hot.TotalMs / 1000d, 0.001);

        var (accepted, refused) = await ProbeConcurrencyAsync(n.Host, ct).ConfigureAwait(false);

        long coldHead = -1, coldMid = -1;
        var coldOk = false;
        if (coldPath != null)
        {
            var head = await TimedRangeAsync($"https://{n.Host}/{coldPath}", 0, 262143, ct).ConfigureAwait(false);
            coldHead = head.Ok ? head.TtfbMs : -1;
            if (head.Ok && head.TotalBytesKnown > 4L * 1024 * 1024)
            {
                var mid = head.TotalBytesKnown / 2;
                var m = await TimedRangeAsync($"https://{n.Host}/{coldPath}", mid, mid + 262143, ct).ConfigureAwait(false);
                coldMid = m.Ok ? m.TtfbMs : -1;
                coldOk = head.Ok && m.Ok;
            }
            else coldOk = head.Ok;
        }

        return new NodeResult(n.Id, label, n.Host, n.Role, true, hot.Status, ip,
            dnsMs, connectMs, hot.TtfbMs, hot.TotalMs, hot.RangeOk, hot.Bytes, kbps,
            accepted, refused, coldHead, coldMid, coldOk, null);
    }

    private static async Task<(int Accepted, int Refused)> ProbeConcurrencyAsync(string host, CancellationToken ct)
    {
        var clients = new List<HttpClient>();
        var responses = new List<HttpResponseMessage>();
        try
        {
            var tasks = Enumerable.Range(0, ConcurrencyProbe).Select(async _ =>
            {
                HttpClient http;
                try
                {
                    http = HttpClientFactory.CreateFragmenting(TimeSpan.FromSeconds(RequestTimeoutSec));
                    lock (clients) clients.Add(http);
                }
                catch { return 0; }

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/{HotProbePath}");
                    req.Headers.Range = new RangeHeaderValue(0, 4L * 1024 * 1024 - 1);
                    var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                    lock (responses) responses.Add(resp);
                    return (int)resp.StatusCode;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { return 0; }
            }).ToArray();

            var codes = await Task.WhenAll(tasks).ConfigureAwait(false);
            return (codes.Count(c => c is 200 or 206), codes.Count(c => c is 503 or 429));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return (0, 0); }
        finally
        {
            foreach (var r in responses) { try { r.Dispose(); } catch { } }
            foreach (var c in clients) { try { c.Dispose(); } catch { } }
        }
    }

    private sealed record RangeProbe(
        bool Ok, int Status, long TtfbMs, long TotalMs, long Bytes,
        bool RangeOk, long TotalBytesKnown, string? Error);

    private static async Task<RangeProbe> TimedRangeAsync(string url, long from, long to, CancellationToken ct)
    {
        try
        {
            using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromSeconds(RequestTimeoutSec));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(from, to);

            var sw = Stopwatch.StartNew();
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var ttfb = sw.ElapsedMilliseconds;

            var status = (int)resp.StatusCode;
            if (status is not (200 or 206))
                return new RangeProbe(false, status, ttfb, sw.ElapsedMilliseconds, 0, false, -1,
                    status is 503 or 429 ? Loc.T("net.nodeRefusedTooManyConnections") : $"HTTP {status}");

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            sw.Stop();

            var totalKnown = resp.Content.Headers.ContentRange?.Length ?? resp.Content.Headers.ContentLength ?? -1;
            return new RangeProbe(true, status, ttfb, Math.Max(sw.ElapsedMilliseconds, 1), bytes.LongLength,
                status == 206, totalKnown, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RangeProbe(false, 0, 0, 0, 0, false, -1, Short(ex));
        }
    }

    private static async Task<HubResult> ProbeHubAsync(string? coldPath, CancellationToken ct)
    {
        var key = coldPath ?? HotProbePath;

        try { await HubRouter.RefreshHubsAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch {}

        var hubs = HubRouter.CurrentHubs;
        for (var i = 0; i < hubs.Count; i++)
        {
            var hub = hubs[i];
            try
            {
                var sw = Stopwatch.StartNew();
                using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromSeconds(10));
                var body = await http.GetStringAsync(
                    $"{hub}/route?key={Uri.EscapeDataString(key)}&cid=doctor&format=json", ct).ConfigureAwait(false);
                sw.Stop();

                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
                var node = root.TryGetProperty("node", out var nd) ? nd.GetString() : null;
                var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
                return new HubResult(true, node, url, sw.ElapsedMilliseconds, status, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                if (i == hubs.Count - 1)
                    return new HubResult(false, null, null, 0, null, Short(ex));
            }
        }
        return new HubResult(false, null, null, 0, null, Loc.T("net.noHubResponded"));
    }

    private static Dictionary<string, string> CollectEnv()
    {
        var env = new Dictionary<string, string>();
        try { env[Loc.T("net.envDbRegion")] = ServerRegionStore.EffectiveRegion().ToString(); } catch { }
        try { env[Loc.T("net.envDownloadSource")] = DownloadSourceStore.ToStr(DownloadSourceStore.Effective()); } catch { }
        try
        {
            var z = ZapretIntegration.DetectZapretRootFromRegistry();
            env["Zapret"] = !ZapretIntegration.IsInstalledAt(z)
                ? Loc.T("net.zapretNotFound")
                : (ZapretIntegration.IsConfiguredForUs(z) ? Loc.T("net.zapretOurDomainSet") : Loc.T("net.zapretOurDomainMissing"))
                  + (ZapretIntegration.IsWinwsRunning() ? Loc.T("net.zapretRunning") : Loc.T("net.zapretNotRunning"));
        }
        catch { env["Zapret"] = Loc.T("net.zapretCheckFailed"); }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!);
            env[Loc.T("net.envFreeDiskSpace")] = Loc.T("misc.sizeGb", ("value", drive.AvailableFreeSpace / 1024 / 1024 / 1024));
        }
        catch { }

        try
        {
            var proxy = WebRequest.DefaultWebProxy?.GetProxy(new Uri("https://cdn.miamigraphicsstorage.uk"));
            if (proxy != null && !proxy.Host.Contains("miamigraphicsstorage", StringComparison.OrdinalIgnoreCase))
                env[Loc.T("net.envSystemProxy")] = proxy.Host;
        }
        catch { }

        return env;
    }

    private static (List<string> Problems, string Verdict, string? Best) Judge(
        List<NodeResult> nodes, HubResult? hub, bool coldTested)
    {
        var problems = new List<string>();
        var alive = nodes.Where(n => n.Ok).ToList();
        var cf = nodes.Where(n => n.Role == "cf" || n.Role == "r2").ToList();
        var ru = nodes.Where(n => n.Role == "ru").ToList();

        var storageHost = RegionStorageHostSafe();
        var storage = storageHost.Length == 0 ? null : nodes.FirstOrDefault(
            n => string.Equals(n.Host, storageHost, StringComparison.OrdinalIgnoreCase));

        foreach (var n in nodes.Where(n => !n.Ok).OrderByDescending(n => ReferenceEquals(n, storage)))
            problems.Add(Loc.T("net.problemNoResponse", ("node", n.Label), ("error", n.Error)));

        if (storage is { Ok: true, KbPerSec: < StorageSlowKbPerSec })
            problems.Insert(0, Loc.T("net.problemStorageSlow", ("node", storage.Label), ("speed", FmtSpeed(storage.KbPerSec))));

        if (alive.Count == 0)
            return (problems, Loc.T("net.verdictNoServersRespond"), null);

        var cfAlive = cf.Count(n => n.Ok);
        var ruAlive = ru.Count(n => n.Ok);

        if (cfAlive == 0 && ruAlive > 0)
            problems.Add(Loc.T("net.problemCloudflareBlocked"));

        foreach (var n in alive.Where(n => n.StreamsRefused > 0))
            problems.Add(Loc.T("net.problemStreamsRefused", ("node", n.Label), ("accepted", n.StreamsAccepted), ("probed", ConcurrencyProbe), ("usable", Math.Max(1, n.StreamsAccepted))));

        foreach (var n in alive.Where(n => !n.RangeOk))
            problems.Add(Loc.T("net.problemNoRange", ("node", n.Label)));

        if (coldTested)
        {
            foreach (var n in alive.Where(n => n.ColdHeadTtfbMs >= 0 && n.ColdMidTtfbMs > 0
                                               && n.ColdMidTtfbMs > n.ColdHeadTtfbMs * 5 + 2000))
                problems.Add(Loc.T("net.problemColdMidSlow", ("node", n.Label), ("midMs", n.ColdMidTtfbMs), ("headMs", n.ColdHeadTtfbMs)));

            foreach (var n in alive.Where(n => !n.ColdOk))
                problems.Add(Loc.T("net.problemModMissingOnNode", ("node", n.Label)));
        }
        else
        {
            problems.Add(Loc.T("net.problemNoModProbeUrl"));
        }

        var slow = alive.Where(n => n.KbPerSec < 200).ToList();
        if (slow.Count == alive.Count)
            problems.Add(Loc.T("net.problemAllNodesSlow"));

        if (hub is { Ok: false })
            problems.Add(Loc.T("net.problemHubDown", ("error", hub.Error)));

        var best = alive
            .Where(n => n.RangeOk)
            .OrderByDescending(n => n.KbPerSec)
            .FirstOrDefault();

        string verdict;
        if (problems.Count == 0)
            verdict = Loc.T("net.verdictAllGood", ("node", best?.Label ?? "-"), ("speed", FmtSpeed(best?.KbPerSec ?? 0)));
        else if (cfAlive == 0 && ruAlive > 0)
            verdict = Loc.T("net.verdictCloudflareThrottled");
        else if (best != null)
            verdict = Loc.T("net.verdictUsableWithProblems", ("node", best.Label), ("speed", FmtSpeed(best.KbPerSec)));
        else
            verdict = Loc.T("net.verdictNoNodeServesProperly");

        if (storage != null)
            verdict += !storage.Ok
                ? " " + Loc.T("net.verdictPrimaryDown", ("host", storage.Host))
                : storage.KbPerSec < StorageSlowKbPerSec
                    ? " " + Loc.T("net.verdictPrimaryCrawling", ("host", storage.Host), ("speed", FmtSpeed(storage.KbPerSec)))
                    : " " + Loc.T("net.verdictPrimaryOk", ("host", storage.Host), ("speed", FmtSpeed(storage.KbPerSec)));

        return (problems, verdict, best?.Host);
    }

    private static string FmtSpeed(double kbps)
        => kbps >= 1024
            ? Loc.T("net.speedMbPerSec", ("value", (kbps / 1024).ToString("F1", global::System.Globalization.CultureInfo.InvariantCulture)))
            : Loc.T("net.speedKbPerSec", ("value", kbps.ToString("F0", global::System.Globalization.CultureInfo.InvariantCulture)));

    private static string Short(Exception ex)
    {
        var m = ex.Message;
        if (ex.InnerException != null && m.Length < 40) m += " / " + ex.InnerException.Message;
        return m.Length > 160 ? m[..160] : m;
    }
}
