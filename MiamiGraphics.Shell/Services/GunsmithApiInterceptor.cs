using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MiamiGraphics.Core.I18n;
using Microsoft.Web.WebView2.Core;

namespace MiamiGraphics.Shell.Services;

public sealed class GunsmithApiInterceptor
{
    private const string Host = "gunsmith.huntergraphics.local";
    private readonly CoreWebView2 _webView;
    private readonly GunsmithService _service;
    private readonly string _staticRoot;
    private bool _registered;

    public GunsmithApiInterceptor(CoreWebView2 webView, GunsmithService service, string staticRoot)
    {
        _webView = webView;
        _service = service;
        _staticRoot = Path.GetFullPath(staticRoot);
    }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics", "gunsmith-api.log");

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
        }
        catch {}
    }

    public void Register()
    {
        if (_registered) return;
        _registered = true;
        _webView.AddWebResourceRequestedFilter($"https://{Host}/*", CoreWebView2WebResourceContext.All);
        _webView.WebResourceRequested += OnRequested;
        Debug.WriteLine("[gunsmith] interceptor registered");
        Log($"=== interceptor registered (whole host https://{Host}/*, root={_staticRoot}) ===");
    }

    private async void OnRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        var url = args.Request.Uri;
        try
        {
            Uri uri;
            try { uri = new Uri(url); }
            catch { return; }
            if (!string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)) return;

            var path = uri.AbsolutePath;
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                var method = args.Request.Method ?? "GET";
                var query = ParseQuery(uri.Query);
                var body = await ReadBodyAsync(args.Request);

                if (IsMutatingGunsmithApi(path) && HotSwapBlocksGunsmith(out var gateMsg))
                {
                    var payload = System.Text.Encoding.UTF8.GetBytes(
                        System.Text.Json.JsonSerializer.Serialize(new { ok = false, error = gateMsg }));
                    args.Response = _webView.Environment.CreateWebResourceResponse(
                        new MemoryStream(payload, writable: false), 409, "Conflict",
                        "Content-Type: application/json\r\nCache-Control: no-store");
                    Log($"{method} {path} -> 409 (заблокировано режимом Rockstar)");
                    return;
                }

                var res = await _service.HandleAsync(method, path, query, body, CancellationToken.None);
                Log($"{method} {path} -> {res.Status} ({res.Body.Length}b)"
                    + (res.Status >= 400 ? " :: " + System.Text.Encoding.UTF8.GetString(res.Body) : ""));
                args.Response = _webView.Environment.CreateWebResourceResponse(
                    new MemoryStream(res.Body, writable: false), res.Status, StatusReason(res.Status),
                    "Content-Type: " + res.ContentType + "\r\nCache-Control: no-store");
            }
            else
            {
                ServeStatic(args, path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunsmith] FAIL {url}: {ex.GetType().Name}: {ex.Message}");
            Log($"EXCEPTION {url}: {ex.GetType().Name}: {ex.Message}");
            try
            {
                var msg = System.Text.Encoding.UTF8.GetBytes(
                    "{\"ok\":false,\"error\":" + System.Text.Json.JsonSerializer.Serialize(ex.Message) + "}");
                args.Response = _webView.Environment.CreateWebResourceResponse(
                    new MemoryStream(msg, writable: false), 500, "Internal Server Error",
                    "Content-Type: application/json; charset=utf-8\r\nCache-Control: no-store");
            }
            catch {}
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ServeStatic(CoreWebView2WebResourceRequestedEventArgs args, string path)
    {
        var rel = Uri.UnescapeDataString(path.TrimStart('/'));
        if (string.IsNullOrEmpty(rel)) rel = "index.html";
        var full = Path.GetFullPath(Path.Combine(_staticRoot, rel.Replace('/', Path.DirectorySeparatorChar)));

        if (!full.StartsWith(_staticRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            Log($"STATIC 404 {path}");
            var nf = System.Text.Encoding.UTF8.GetBytes("not found");
            args.Response = _webView.Environment.CreateWebResourceResponse(
                new MemoryStream(nf, writable: false), 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        var bytes = File.ReadAllBytes(full);
        var ct = MimeOf(full);
        args.Response = _webView.Environment.CreateWebResourceResponse(
            new MemoryStream(bytes, writable: false), 200, "OK",
            "Content-Type: " + ct + "\r\nCache-Control: no-store");
    }

    private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".glb" => "model/gltf-binary",
        ".wasm" => "application/wasm",
        _ => "application/octet-stream",
    };

    private static async Task<byte[]> ReadBodyAsync(CoreWebView2WebResourceRequest req)
    {
        if (req.Content is not { } content) return Array.Empty<byte>();
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static bool IsMutatingGunsmithApi(string path) =>
        path.StartsWith("/api/apply", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/anim-install", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/anim-remove", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/install", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/remove", StringComparison.OrdinalIgnoreCase);

    private static bool HotSwapBlocksGunsmith(out string msg)
    {
        msg = string.Empty;
        try
        {
            var mode = MiamiGraphics.Core.HotSwap.HotSwapModeStore.Read();
            if (!mode.Enabled || string.IsNullOrWhiteSpace(mode.GtaRoot)) return false;
            if (MiamiGraphics.Core.HotSwap.GameFileSwapper.ReadSet(mode.GtaRoot!).Count == 0) return false;
            msg = Loc.T("error.rockstarModeBlocksChange");
            return true;
        }
        catch { return false; }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return dict;
        var q = query.StartsWith("?") ? query[1..] : query;
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) { dict[Uri.UnescapeDataString(part)] = ""; continue; }
            var k = Uri.UnescapeDataString(part[..eq]);
            var v = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            dict[k] = v;
        }
        return dict;
    }

    private static string StatusReason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        499 => "Client Closed Request",
        500 => "Internal Server Error",
        _   => "OK",
    };
}
