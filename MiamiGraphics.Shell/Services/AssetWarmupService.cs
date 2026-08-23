using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace MiamiGraphics.Shell.Services;

public sealed class AssetWarmupService
{
    private const int TopCoversCount = 50;

    private static readonly HttpClient SharedHttp = HttpClientFactory.CreateFragmenting(
        TimeSpan.FromSeconds(30));

    private readonly AssetCache _cache;
    private readonly SupabaseClient _supabase;

    public AssetWarmupService(AssetCache cache, SupabaseClient supabase)
    {
        _cache = cache;
        _supabase = supabase;
    }

    public async Task RunAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _cache.WarmInMemoryIndex();
            var urls = await CollectTopAssetUrls();
            Debug.WriteLine($"[warmup] collected {urls.Count} URLs from Supabase");
            if (urls.Count == 0) return;

            using var sem = new SemaphoreSlim(12);
            using var hardCap = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var tasks = urls.Select(async url =>
            {
                try
                {
                    await sem.WaitAsync(hardCap.Token);
                    try { await FetchAndCache(url, hardCap.Token); }
                    finally { sem.Release(); }
                }
                catch (OperationCanceledException) {}
            });
            await Task.WhenAll(tasks);

            _cache.EvictIfOversize();
            Debug.WriteLine($"[warmup] done in {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[warmup] failed: {ex.Message}");
        }
    }

    private async Task<List<string>> CollectTopAssetUrls()
    {
        var urls = new List<string>();

        try
        {
            var packs = await _supabase.SelectAsync<CoverRow>(
                "gunpacks",
                $"select=cover_url&order=download_count.desc.nullslast&limit={TopCoversCount}");
            foreach (var p in packs)
                if (!string.IsNullOrWhiteSpace(p.CoverUrl)) urls.Add(p.CoverUrl);
        }
        catch (Exception ex) { Debug.WriteLine($"[warmup] gunpacks fetch: {ex.Message}"); }

        try
        {
            var rxs = await _supabase.SelectAsync<ReduxImageRow>(
                "redux_items",
                $"select=preview_url&order=uploaded_at.desc.nullslast&limit={TopCoversCount}");
            foreach (var r in rxs)
                if (!string.IsNullOrWhiteSpace(r.PreviewUrl)) urls.Add(r.PreviewUrl);
        }
        catch (Exception ex) { Debug.WriteLine($"[warmup] redux_items fetch: {ex.Message}"); }

        try
        {
            var guns = await _supabase.SelectAsync<GunPreviewRow>(
                "gunpack_guns",
                "select=preview_url&preview_url=not.is.null&limit=300");
            foreach (var g in guns)
                if (!string.IsNullOrWhiteSpace(g.PreviewUrl)) urls.Add(g.PreviewUrl);
        }
        catch (Exception ex) { Debug.WriteLine($"[warmup] gunpack_guns fetch: {ex.Message}"); }

        return urls;
    }

    private async Task FetchAndCache(string url, CancellationToken ct = default)
    {

        if (_cache.TryGet(url) != null) return;

        try
        {
            using var resp = await SharedHttp.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[warmup] {(int)resp.StatusCode} {url}");
                return;
            }
            var body = await resp.Content.ReadAsByteArrayAsync(ct);
            var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            _cache.Put(url, body, contentType);
        }
        catch (OperationCanceledException) {}
        catch (Exception ex)
        {
            Debug.WriteLine($"[warmup] {url}: {ex.Message}");
        }
    }

    private sealed class CoverRow
    {
        [JsonPropertyName("cover_url")] public string? CoverUrl { get; set; }
    }
    private sealed class ReduxImageRow
    {
        [JsonPropertyName("preview_url")] public string? PreviewUrl { get; set; }
    }
    private sealed class GunPreviewRow
    {
        [JsonPropertyName("preview_url")] public string? PreviewUrl { get; set; }
    }
}
