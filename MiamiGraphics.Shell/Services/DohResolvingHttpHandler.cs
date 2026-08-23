using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;

namespace MiamiGraphics.Shell.Services;

public sealed class DohResolvingHttpHandler : DelegatingHandler
{

    private static readonly HttpClient DohClient = new(
        new FragmentingHttpHandler(),
        disposeHandler: false)
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    public static class Providers
    {
        public const string Cloudflare = "https://cloudflare-dns.com/dns-query?name={0}&type=A";
        public const string Quad9      = "https://dns.quad9.net:5053/dns-query?name={0}&type=A";
        public const string NextDns    = "https://anycast.dns.nextdns.io/dns-query?name={0}&type=A";
        public const string Google     = "https://dns.google/resolve?name={0}&type=A";
        public const string AdGuard    = "https://dns.adguard-dns.com/resolve?name={0}&type=A";
    }

    public DohResolvingHttpHandler()
        : this(new[] { Providers.Cloudflare, Providers.Quad9, Providers.NextDns }) { }

    public DohResolvingHttpHandler(string[] dohEndpointTemplates)
        : base(BuildInner(dohEndpointTemplates
            ?? throw new ArgumentNullException(nameof(dohEndpointTemplates))))
    { }

    private static SocketsHttpHandler BuildInner(string[] endpoints)
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
            EnableMultipleHttp2Connections = false,

            ConnectCallback = async (ctx, ct) =>
            {
                var host = ctx.DnsEndPoint.Host;
                var port = ctx.DnsEndPoint.Port;

                IPAddress ip;
                try
                {
                    ip = await ResolveViaDoH(host, endpoints, ct).ConfigureAwait(false);
                    Debug.WriteLine($"[doh] {host} → {ip} (DoH-resolved)");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[doh] FAIL resolve {host}: {ex.Message}");
                    throw;
                }

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                try
                {
                    await socket.ConnectAsync(ip, port, ct).ConfigureAwait(false);

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }

    private static async Task<IPAddress> ResolveViaDoH(string host, string[] endpoints, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var template in endpoints)
        {
            var url = string.Format(template, Uri.EscapeDataString(host));
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));

                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                using var resp = await DohClient.SendAsync(req, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Answer", out var answers))
                    throw new InvalidOperationException("no Answer section");

                foreach (var ans in answers.EnumerateArray())
                {

                    if (!ans.TryGetProperty("type", out var typeEl)) continue;
                    if (typeEl.GetInt32() != 1) continue;
                    var data = ans.GetProperty("data").GetString();
                    if (IPAddress.TryParse(data, out var addr) && addr.AddressFamily == AddressFamily.InterNetwork)
                        return addr;
                }
                throw new InvalidOperationException("no A record in Answer section");
            }
            catch (Exception ex)
            {
                last = ex;
                Debug.WriteLine($"[doh] endpoint {template} failed: {ex.Message}");
                continue;
            }
        }
        throw new InvalidOperationException($"DoH resolve failed for {host}: {last?.Message}");
    }
}
