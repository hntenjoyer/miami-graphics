using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Web.WebView2.Core;

namespace MiamiGraphics.Shell.Services;

internal sealed class WebView2BypassInterceptor
{

    private static readonly HttpClient SharedHttp = new(
        new FragmentingHttpHandler(),
        disposeHandler: false)
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private static readonly HttpClient MediaHttp = new(
        new FragmentingHttpHandler(),
        disposeHandler: false)
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,
        Task<(byte[] Body, string Headers, int Status, string Reason)>> _inFlight = new();

    private static readonly string[] FilterPatterns = new[]
    {
        "https://miamigraphicsstorage.uk/*",
        "https://cdn.miamigraphicsstorage.uk/*",
        "https://eu.miamigraphicsstorage.uk/*",
        "https://ru.miamigraphicsstorage.uk/*",
        "https://api.miamigraphicsstorage.uk/*",
        "https://*.r2.dev/*",
        "https://*.r2.cloudflarestorage.com/*",
        "https://i.ytimg.com/*",
        "https://img.youtube.com/*",
        "https://i.imgur.com/*",
        "https://*.imgur.com/*",
        "https://i.postimg.cc/*",
        "https://*.postimg.cc/*",
        "https://i.ibb.co/*",
        "https://*.ibb.co/*",
    };

    private static readonly HashSet<string> RestrictedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Keep-Alive", "Proxy-Connection",
        "Transfer-Encoding", "Upgrade", "Trailer", "TE", "Expect",
    };

    private static readonly HashSet<string> RestrictedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding", "Connection", "Keep-Alive", "Trailer",
        "Upgrade", "Proxy-Connection",
    };

    private readonly CoreWebView2 _webView;
    private readonly AssetCache? _cache;
    private bool _registered;

    public WebView2BypassInterceptor(CoreWebView2 webView, AssetCache? cache = null)
    {
        _webView = webView;
        _cache = cache;
    }

    public void Register()
    {
        if (_registered) return;
        _registered = true;

        foreach (var pattern in FilterPatterns)
        {
            _webView.AddWebResourceRequestedFilter(
                pattern, CoreWebView2WebResourceContext.All);
        }
        _webView.WebResourceRequested += OnWebResourceRequested;
        Debug.WriteLine($"[bypass] registered WebView2 interceptor on {FilterPatterns.Length} patterns");
    }

    private async void OnWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs args)
    {

        var deferral = args.GetDeferral();
        var sw = Stopwatch.StartNew();
        var url = args.Request.Uri;

        try
        {
            if (url.Contains(".huntergraphics.local", StringComparison.OrdinalIgnoreCase))
                return;

            if (IsStreamingMedia(url))
            {
                Debug.WriteLine($"[bypass] MEDIA passthrough {url}");
                return;
            }

            var method = args.Request.Method;
            var hasRange = args.Request.Headers.Any(h => string.Equals(h.Key, "Range", StringComparison.OrdinalIgnoreCase));
            var isCacheable = _cache is not null
                && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                && !hasRange;
            if (!isCacheable)
            {
                AssetCache.DiagPublic($"[bypass] NOT-CACHEABLE method={method} hasRange={hasRange} cacheNull={_cache is null} url={url}");
            }
            if (isCacheable)
            {
                var hit = _cache!.TryGet(url);
                if (hit is { } cached)
                {
                    var cachedHeaders = "Content-Type: " + cached.ContentType +
                                        "\r\nCache-Control: public, max-age=86400" +
                                        "\r\nAccess-Control-Allow-Origin: *";
                    args.Response = _webView.Environment.CreateWebResourceResponse(
                        new MemoryStream(cached.Body, writable: false),
                        200, "OK", cachedHeaders);
                    Debug.WriteLine($"[bypass] CACHE HIT {url} ({cached.Body.Length} B, {sw.ElapsedMilliseconds} ms)");
                    return;
                }
            }

            var task = _inFlight.GetOrAdd(url, _ => FetchOnceAsync(args.Request, url, isCacheable));
            (byte[] body, string headers, int status, string reason) result;
            try { result = await task; }
            finally { _inFlight.TryRemove(new System.Collections.Generic.KeyValuePair<string, Task<(byte[], string, int, string)>>(url, task)); }

            if (url.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                AssetCache.DiagPublic($"[bypass] glb-RESP status={result.status} bytes={result.body.Length} ms={sw.ElapsedMilliseconds} url={url}");

            args.Response = _webView.Environment.CreateWebResourceResponse(
                new MemoryStream(result.body, writable: false),
                result.status,
                result.reason,
                result.headers);

            Debug.WriteLine($"[bypass] {result.status} {url} ({result.body.Length} B, {sw.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex)
        {

            Debug.WriteLine($"[bypass] FAIL {url}: {ex.GetType().Name}: {ex.Message}");
            if (url.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                AssetCache.DiagPublic($"[bypass] glb-EXC {ex.GetType().Name} '{ex.Message}' ms={sw.ElapsedMilliseconds} url={url}");
            var msg = $"DPI-bypass proxy error: {ex.Message}";
            var errStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(msg));
            args.Response = _webView.Environment.CreateWebResourceResponse(
                errStream, 502, "Bad Gateway",
                "Content-Type: text/plain; charset=utf-8" +
                "\r\nAccess-Control-Allow-Origin: *");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static readonly string[] StreamingMediaExtensions =
        { ".mp4", ".webm", ".m4v", ".mov", ".m4a", ".mp3", ".ogg" };

    private static bool IsStreamingMedia(string url)
    {
        var path = url;
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        return StreamingMediaExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    private async Task StreamMediaAsync(
        CoreWebView2WebResourceRequestedEventArgs args, string url, Stopwatch sw)
    {
        var req = BuildOutgoingRequest(args.Request);
        HttpResponseMessage? resp = null;
        try { resp = await MediaHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead); }
        catch (HttpRequestException) { resp = null; }

        if (resp is null || (int)resp.StatusCode >= 500 || (int)resp.StatusCode == 404)
        {
            var altUrl = AlternateHostUrl(req.RequestUri?.ToString() ?? url);
            if (altUrl is not null)
            {
                try { resp?.Dispose(); } catch { }
                var altReq = BuildOutgoingRequest(args.Request);
                altReq.RequestUri = new Uri(altUrl);
                try { resp = await MediaHttp.SendAsync(altReq, HttpCompletionOption.ResponseHeadersRead); }
                catch { resp = null; }
            }
        }

        if (resp is null)
            throw new HttpRequestException("Both RU mirror and CF origin unreachable (media)");

        var netStream = await resp.Content.ReadAsStreamAsync();
        args.Response = _webView.Environment.CreateWebResourceResponse(
            new ResponseOwningStream(netStream, resp),
            (int)resp.StatusCode,
            resp.ReasonPhrase ?? StatusReason(resp.StatusCode),
            SerializeResponseHeaders(resp));

        Debug.WriteLine($"[bypass] STREAM {(int)resp.StatusCode} {url} " +
                        $"(range={args.Request.Headers.Any(h => string.Equals(h.Key, "Range", StringComparison.OrdinalIgnoreCase))}, " +
                        $"{sw.ElapsedMilliseconds} ms до заголовков)");
    }

    private sealed class ResponseOwningStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _resp;

        public ResponseOwningStream(Stream inner, HttpResponseMessage resp)
        { _inner = inner; _resp = resp; }

        public override bool CanRead  => _inner.CanRead;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken ct)
            => _inner.ReadAsync(buffer, offset, count, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _inner.Dispose(); } catch { }
                try { _resp.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }

    private async Task<(byte[] Body, string Headers, int Status, string Reason)>
        FetchOnceAsync(CoreWebView2WebResourceRequest reqIn, string url, bool isCacheable)
    {
        using var req = BuildOutgoingRequest(reqIn);

        HttpResponseMessage? resp = null;
        try { resp = await SharedHttp.SendAsync(req, HttpCompletionOption.ResponseContentRead); }
        catch (HttpRequestException) { resp = null; }

        if (resp is null || ((int)resp.StatusCode >= 500) || (int)resp.StatusCode == 404)
        {
            var primaryUrl = req.RequestUri?.ToString() ?? reqIn.Uri;
            var altUrl = AlternateHostUrl(primaryUrl);
            if (altUrl is not null && !string.Equals(primaryUrl, altUrl, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[bypass] primary failed ({resp?.StatusCode.ToString() ?? "transport"}), retrying via alt host: {altUrl}");
                try { resp?.Dispose(); } catch { }
                using var altReq = BuildOutgoingRequest(reqIn);
                altReq.RequestUri = new Uri(altUrl);
                try { resp = await SharedHttp.SendAsync(altReq, HttpCompletionOption.ResponseContentRead); }
                catch { resp = null; }
            }
        }

        if (resp is null)
            throw new HttpRequestException("Both RU mirror and CF origin unreachable");

        var bodyBytes = await resp.Content.ReadAsByteArrayAsync();
        var headers = SerializeResponseHeaders(resp);
        var status = (int)resp.StatusCode;
        var reason = resp.ReasonPhrase ?? StatusReason(resp.StatusCode);

        if (isCacheable
            && _cache is not null
            && status >= 200 && status < 300
            && bodyBytes.Length > 0)
        {
            var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            _cache.Put(url, bodyBytes, contentType);
        }
        try { resp.Dispose(); } catch { }

        return (bodyBytes, headers, status, reason);
    }

    private static HttpRequestMessage BuildOutgoingRequest(CoreWebView2WebResourceRequest src)
    {
        var effectiveUri = RewriteAssetUrlForRegion(src.Uri);
        var req = new HttpRequestMessage(new HttpMethod(src.Method), effectiveUri);

        if (src.Content is { } body && body.Length > 0)
        {

            var ms = new MemoryStream();
            body.CopyTo(ms);
            ms.Position = 0;
            req.Content = new StreamContent(ms);
        }

        foreach (var pair in src.Headers)
        {
            var key = pair.Key;
            var val = pair.Value;
            if (RestrictedRequestHeaders.Contains(key)) continue;

            if (key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                req.Content ??= new ByteArrayContent(Array.Empty<byte>());
                req.Content.Headers.TryAddWithoutValidation(key, val);
            }
            else
            {
                req.Headers.TryAddWithoutValidation(key, val);
            }
        }

        return req;
    }

    private static string RewriteAssetUrlForRegion(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;

        if (ServerRegionStore.EffectiveRegion() != ServerRegion.Ru) return url;

        var pathLower = uri.AbsolutePath.ToLowerInvariant();
        if (pathLower.EndsWith(".glb", StringComparison.Ordinal)
         || pathLower.EndsWith(".rpf", StringComparison.Ordinal))
            return url;

        if (!string.Equals(uri.Host, "miamigraphicsstorage.uk", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, "cdn.miamigraphicsstorage.uk", StringComparison.OrdinalIgnoreCase))
            return url;

        var rewritten = new UriBuilder(uri)
        {
            Scheme = "https",
            Host = "ru.miamigraphicsstorage.uk",
            Port = -1,
        };
        return rewritten.Uri.ToString();
    }

    private static string? AlternateHostUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        if (string.Equals(uri.Host, "ru.miamigraphicsstorage.uk", StringComparison.OrdinalIgnoreCase))
            return WithHost(uri, "miamigraphicsstorage.uk");

        if ((string.Equals(uri.Host, "miamigraphicsstorage.uk", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Host, "cdn.miamigraphicsstorage.uk", StringComparison.OrdinalIgnoreCase))
            && ServerRegionStore.Load() == ServerRegion.Ru)
            return WithHost(uri, "ru.miamigraphicsstorage.uk");

        return null;
    }

    private static string WithHost(Uri uri, string host) =>
        new UriBuilder(uri) { Scheme = "https", Host = host, Port = -1 }.Uri.ToString();

    private static string SerializeResponseHeaders(HttpResponseMessage resp)
    {

        var lines = new List<string>();

        void AppendHeaders(HttpHeaders headers)
        {
            foreach (var h in headers)
            {
                if (RestrictedResponseHeaders.Contains(h.Key)) continue;
                foreach (var v in h.Value) lines.Add($"{h.Key}: {v}");
            }
        }
        AppendHeaders(resp.Headers);
        AppendHeaders(resp.Content.Headers);
        lines.Add("Access-Control-Allow-Origin: *");
        return string.Join("\r\n", lines);
    }

    private static string StatusReason(HttpStatusCode code) => code switch
    {
        HttpStatusCode.OK            => "OK",
        HttpStatusCode.PartialContent=> "Partial Content",
        HttpStatusCode.NotModified   => "Not Modified",
        HttpStatusCode.NotFound      => "Not Found",
        HttpStatusCode.Forbidden     => "Forbidden",
        HttpStatusCode.InternalServerError => "Internal Server Error",
        _ => code.ToString(),
    };
}
