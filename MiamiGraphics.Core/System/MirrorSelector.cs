using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;

namespace MiamiGraphics.Core.System;

public sealed record MirrorChoice(string Name, Uri BaseUri, double? SpeedMbPerSecond, bool PoorInternetLikely);

public static class MirrorSelector
{
    private static readonly Uri Cdn      = new("https://cdn.miamigraphicsstorage.uk");
    private static readonly Uri Ru       = new("https://ru.miamigraphicsstorage.uk");
    private static readonly Uri Apex     = new("https://miamigraphicsstorage.uk");
    private static readonly Uri Fallback = Apex;

    private static readonly Uri Rf = new("https://rf.miamigraphicsstorage.uk");

    private static readonly Uri[] ProbeCandidates = { Ru, Cdn, Apex, Rf };

    private static readonly Uri[] ElectionCandidates = { Ru, Cdn, Apex };

    private const string ProbePath      = "gta_versions/1.0.3751.0/guns.rpf";
    private const long   ProbeBytes     = 768 * 1024;
    private const int    ProbeTimeoutMs = 4000;

    private const double ProbeSilent = -1.0;
    private const double ProbeAlive  =  0.0;

    private const double StorageMinMbPerSecond = 0.25;

    private const int ProbeAliveMaxMs = 2000;

    private static readonly TimeSpan StoragePenalty = TimeSpan.FromMinutes(10);

    private static readonly HashSet<string> RewritableHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "miamigraphicsstorage.uk",
        "cdn.miamigraphicsstorage.uk",
        "ru.miamigraphicsstorage.uk",
        "rf.miamigraphicsstorage.uk",
        "pub-f3641b214c164277964c1e92c826b19b.r2.dev",
    };

    public static Func<TimeSpan, HttpClient>? ProbeHttpClientFactory;

    public static Func<bool>? RuStorageProvider;

    public static Action<string>? LogSink;

    private static void Log(string message)
    {
        try { LogSink?.Invoke(message); } catch { }
    }

    private static volatile Uri _selected = Cdn;
    private static Task<Uri>? _probe;
    private static Uri? _manual;
    private static volatile bool _zapretForUs;
    private static readonly object _gate = new();

    private static volatile Dictionary<string, double>? _probeMbps;

    private static long _storageDownUntilTicks;

    public static MirrorChoice? Current => new("selected", _selected, null, false);

    public static string RuStorageHost => Rf.Host;

    public static string RegionStorageHost
        => (!_zapretForUs && (RuStorageProvider?.Invoke() ?? false)) ? Rf.Host : Cdn.Host;

    public static bool RegionStorageUsable
    {
        get { var h = RegionStorageHost; return IsStorageUsable(h); }
    }

    public static bool IsStorageUsable(string host)
    {
        if (InStoragePenalty(host)) return false;
        var manual = _manual;
        if (manual != null) return string.Equals(host, manual.Host, StringComparison.OrdinalIgnoreCase);
        var m = _probeMbps;
        if (m is null) return true;
        if (!m.TryGetValue(host, out var mbps)) return true;
        if (mbps <= ProbeSilent) return false;
        if (mbps == ProbeAlive) return true;
        return mbps >= StorageMinMbPerSecond;
    }

    public static void ReportStorageFailure(string? host)
    {
        if (!string.Equals(host, Rf.Host, StringComparison.OrdinalIgnoreCase)) return;
        Interlocked.Exchange(ref _storageDownUntilTicks, (DateTime.UtcNow + StoragePenalty).Ticks);
        Debug.WriteLine($"[mirror] {Rf.Host} отвалился на загрузке - {StoragePenalty.TotalMinutes:F0} мин не ставим первым");
        Log($"хранилище {Rf.Host} отвалилось на боевой загрузке - {StoragePenalty.TotalMinutes:F0} мин не ставим первым (отстойник)");
    }

    private static bool InStoragePenalty(string host)
        => string.Equals(host, Rf.Host, StringComparison.OrdinalIgnoreCase)
           && DateTime.UtcNow.Ticks < Interlocked.Read(ref _storageDownUntilTicks);

    public static void SetManualOverride(string? choice)
    {
        _manual = (choice?.Trim().ToLowerInvariant()) switch
        {
            "ru"                                => Ru,
            "cdn"                               => Cdn,
            "apex" or "direct" or "r2"          => Apex,
            "rf"                                => Rf,
            _                                   => null,
        };
        if (_manual != null)
        {
            _selected = _manual;
            lock (_gate) { _probe = Task.FromResult(_manual); }
            Debug.WriteLine($"[mirror] manual override -> {_manual.Host}");
        }
        else
        {
            lock (_gate) { _probe = null; }
            Debug.WriteLine("[mirror] manual override cleared - auto-probe re-enabled");
        }
    }

    public static void SetZapretForUs(bool on)
    {
        if (_zapretForUs == on) return;
        _zapretForUs = on;
        if (_manual == null) lock (_gate) { _probe = null; }
        Debug.WriteLine($"[mirror] zapret-for-us = {on} -> {(on ? "prefer CF, offload RU" : "auto")}");
    }

    public static void Warmup() => _ = EnsureSelectedAsync();

    public static Task<Uri> EnsureSelectedAsync(CancellationToken ct = default)
    {
        if (_probe != null) return _probe;
        lock (_gate) { _probe ??= ProbeAsync(); }
        return _probe;
    }

    public static async Task<MirrorChoice> SelectAsync(CancellationToken ct = default)
    {
        Uri uri;
        try { uri = await EnsureSelectedAsync(ct).WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { uri = _selected; }
        return new MirrorChoice(uri.Host, uri, null, false);
    }

    public static async Task<string> RewriteUrlAsync(string url, CancellationToken ct = default)
    {
        if (!ShouldRewrite(url)) return url;
        Uri mirror;
        try { mirror = await EnsureSelectedAsync(ct).WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { mirror = _selected; }
        return RewriteUrl(url, mirror);
    }

    private static async Task<Uri> ProbeAsync()
    {
        var tasks = new List<Task<(Uri uri, double mbps)>>();
        foreach (var c in ProbeCandidates) tasks.Add(ProbeOneAsync(c));
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMilliseconds(ProbeTimeoutMs * 3)).ConfigureAwait(false); }
        catch (TimeoutException) { Debug.WriteLine("[mirror.probe] страховочный потолок пробы - беру, что успело"); }
        var results = new (Uri uri, double mbps)[tasks.Count];
        for (int i = 0; i < tasks.Count; i++)
            results[i] = tasks[i].IsCompletedSuccessfully ? tasks[i].Result : (ProbeCandidates[i], ProbeSilent);

        var measured = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results) measured[r.uri.Host] = r.mbps;
        _probeMbps = measured;

        var ok = new List<(Uri uri, double mbps)>();
        foreach (var r in results)
            if (r.mbps > 0 && Array.IndexOf(ElectionCandidates, r.uri) >= 0) ok.Add(r);

        Uri chosen = Cdn;
        if (ok.Count > 0)
        {
            ok.Sort((a, b) => b.mbps.CompareTo(a.mbps));
            chosen = ok[0].uri;

            if (_zapretForUs)
            {
                var cf = ok.FirstOrDefault(r => r.uri == Cdn || r.uri == Apex);
                if (cf.uri != null) chosen = cf.uri;
            }
        }
        _selected = chosen;
        var storageHost = RegionStorageHost;
        measured.TryGetValue(storageHost, out var storageMbps);
        Debug.WriteLine($"[mirror.probe] selected {chosen.Host} (of {ok.Count}/{ElectionCandidates.Length} responsive, zapret={_zapretForUs}); "
                      + $"хранилище региона {storageHost}: {storageMbps:F1} МБ/с -> {(IsStorageUsable(storageHost) ? "первым" : "в обход, идём по узлам")}");
        Log($"проба зеркал: {string.Join(", ", results.Select(r => $"{r.uri.Host} - {DescribeProbeMbps(r.mbps)}"))}; "
          + $"выбрано {chosen.Host}; хранилище региона {storageHost}: "
          + (IsStorageUsable(storageHost) ? "пригодно, ставим первым" : "молчит или ползёт - в обход, идём по узлам"));
        return chosen;
    }

    private static string DescribeProbeMbps(double mbps)
        => mbps <= ProbeSilent ? "молчит (таймаут/TLS/обрыв)"
         : mbps == ProbeAlive  ? "жив, пробного файла нет"
         : $"{mbps:F1} МБ/с";

    private static async Task<(Uri uri, double mbps)> ProbeOneAsync(Uri c)
    {
        try
        {
            var timeout = TimeSpan.FromMilliseconds(ProbeTimeoutMs);
            using var http = ProbeHttpClientFactory?.Invoke(timeout)
                             ?? new HttpClient { Timeout = timeout };
            using var bodyCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(ProbeTimeoutMs * 2));
            var url = $"{c.Scheme}://{c.Host}/{ProbePath}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, ProbeBytes - 1);
            var sw = Stopwatch.StartNew();
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, bodyCts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var slow = sw.ElapsedMilliseconds > ProbeAliveMaxMs;
                Debug.WriteLine($"[mirror.probe] {c.Host} -> HTTP {(int)resp.StatusCode} за {sw.ElapsedMilliseconds}мс"
                              + (slow ? " - слишком долго, считаем непригодным" : ""));
                return (c, slow ? ProbeSilent : ProbeAlive);
            }
            long got = 0;
            try
            {
                await using var body = await resp.Content.ReadAsStreamAsync(bodyCts.Token).ConfigureAwait(false);
                var buf = new byte[64 * 1024];
                int n;
                while ((n = await body.ReadAsync(buf.AsMemory(), bodyCts.Token).ConfigureAwait(false)) > 0)
                    got += n;
            }
            catch (OperationCanceledException)
            {
            }
            sw.Stop();
            if (got <= 0)
            {
                Debug.WriteLine($"[mirror.probe] {c.Host}: 2xx, но тело молчит {sw.ElapsedMilliseconds}мс - непригоден");
                return (c, ProbeSilent);
            }
            double mbps = got / 1024.0 / 1024.0 / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            Debug.WriteLine($"[mirror.probe] {c.Host}: {got}B in {sw.ElapsedMilliseconds}ms = {mbps:F1} MB/s");
            return (c, mbps);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[mirror.probe] {c.Host} FAIL: {ex.Message}");
            return (c, ProbeSilent);
        }
    }

    public static bool ShouldRewrite(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return RewritableHosts.Contains(uri.Host);
    }

    public static string RewriteUrl(string url, Uri mirrorBase)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        if (!RewritableHosts.Contains(uri.Host)) return url;

        var builder = new UriBuilder(uri)
        {
            Scheme = mirrorBase.Scheme,
            Host = mirrorBase.Host,
            Port = mirrorBase.IsDefaultPort ? -1 : mirrorBase.Port,
        };
        return builder.Uri.ToString();
    }

    public static Uri FallbackBase => Fallback;
}
