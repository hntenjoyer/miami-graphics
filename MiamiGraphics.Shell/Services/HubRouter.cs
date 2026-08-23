using System.Net.Http;
using System.Text.Json;

namespace MiamiGraphics.Shell.Services;

public sealed class HubLease : IAsyncDisposable
{
    public static readonly HubLease NoOp = new(null, null);
    private readonly string? _hub;
    private readonly string? _cid;
    private readonly CancellationTokenSource? _cts;

    private HubLease(string? hub, string? cid)
    {
        _hub = hub; _cid = cid;
        if (hub != null && cid != null) { _cts = new CancellationTokenSource(); _ = HeartbeatLoop(_cts.Token); }
    }

    internal static HubLease Active(string hub, string cid) => new(hub, cid);

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct).ConfigureAwait(false);
                try
                {
                    using var h = HttpClientFactory.CreateFragmenting(TimeSpan.FromSeconds(5));
                    await h.GetAsync($"{_hub}/route/heartbeat?cid={_cid}", ct).ConfigureAwait(false);
                }
                catch {}
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_hub != null && _cid != null)
        {
            try
            {
                using var h = HttpClientFactory.CreateFragmenting(TimeSpan.FromSeconds(5));
                await h.GetAsync($"{_hub}/route/release?cid={_cid}").ConfigureAwait(false);
            }
            catch {}
        }
    }
}

public static class HubRouter
{
    private static readonly string[] Fallback =
    {
        "https://ru.miamigraphicsstorage.uk",
        "https://hnt.miamigraphicsstorage.uk",
    };
    private static volatile string[] _hubs = Fallback;
    private static long _cfgAtTicks;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> CurrentHubs => _hubs;

    public static Task RefreshHubsAsync(CancellationToken ct) => RefreshConfigAsync(ct);

    private sealed record RouteResp(string? Status, string? Node, string? Url, int Position, int Eta, int RetryAfter);
    private sealed record CfgResp(string[]? Hubs, bool RuEnabled);

    private static async Task RefreshConfigAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow.Ticks - Interlocked.Read(ref _cfgAtTicks) < TimeSpan.FromMinutes(5).Ticks) return;
        foreach (var hub in _hubs)
        {
            try
            {
                using var h = HttpClientFactory.CreateFragmenting(TimeSpan.FromSeconds(5));
                var s = await h.GetStringAsync($"{hub}/route/config", ct).ConfigureAwait(false);
                var c = JsonSerializer.Deserialize<CfgResp>(s, JsonOpts);
                if (c?.Hubs is { Length: > 0 }) _hubs = c.Hubs;
                Interlocked.Exchange(ref _cfgAtTicks, DateTime.UtcNow.Ticks);
                return;
            }
            catch {}
        }
    }

    public static async Task<(string? Url, HubLease Lease)> ResolveAsync(
        string key, Action<int, int>? onQueue, CancellationToken ct)
    {
        var k = key.TrimStart('/');
        var cid = Guid.NewGuid().ToString("N");
        try { await RefreshConfigAsync(ct).ConfigureAwait(false); } catch { }

        var loggedPos = int.MinValue;

        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var anyHub = false;
            var queued = false;
            var retry = 3;
            foreach (var hub in _hubs)
            {
                try
                {
                    using var h = HttpClientFactory.CreateFragmenting(TimeSpan.FromSeconds(8));
                    var s = await h.GetStringAsync(
                        $"{hub}/route?key={Uri.EscapeDataString(k)}&cid={cid}&format=json", ct).ConfigureAwait(false);
                    var r = JsonSerializer.Deserialize<RouteResp>(s, JsonOpts);
                    anyHub = true;
                    if (r?.Status == "grant" && !string.IsNullOrEmpty(r.Url))
                    {
                        DownloadLog.Write("hub",
                            $"грант: узел {(string.IsNullOrEmpty(r.Node) ? HostOf(r.Url!) : r.Node)} ({HostOf(r.Url!)}) отдаёт {k}");
                        return (r.Url, HubLease.Active(hub, cid));
                    }
                    if (r?.Status == "queue")
                    {
                        if (r.Position != loggedPos)
                        {
                            DownloadLog.Write("hub", $"очередь: позиция {r.Position}, ~{r.Eta} с (ключ {k})");
                            loggedPos = r.Position;
                        }
                        onQueue?.Invoke(r.Position, r.Eta);
                        retry = Math.Clamp(r.RetryAfter, 1, 10);
                        queued = true;
                        break;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch {}
            }
            if (!anyHub)
            {
                DownloadLog.Write("hub", $"хабы не ответили (ключ {k}) - иду по прямой цепочке узлов");
                return (null, HubLease.NoOp);
            }
            if (queued)
                await Task.Delay(TimeSpan.FromSeconds(retry), ct).ConfigureAwait(false);
            else
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
        DownloadLog.Write("hub", $"не дождались гранта за 5 мин (ключ {k}) - иду по прямой цепочке узлов");
        return (null, HubLease.NoOp);
    }

    private static string HostOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
}
