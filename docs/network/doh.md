# DoH (DNS over HTTPS)

DNS - это **первый** шаг любого HTTP-запроса. Перед TLS-handshake'ом клиент должен резолвить имя `cdn.miamigraphicsstorage.uk` в IP. Обычно через системный DNS (роутер, провайдер).

В РФ часть провайдеров **подменяют DNS-ответы** для blocked доменов - вместо настоящего IP возвращают свой sinkhole. На это [FragmentingHttpHandler не помогает](fragmenting-http-handler.md) - мы не дойдём до TLS, потому что коннектимся не в тот endpoint.

DoH (DNS over HTTPS) - это резолвинг **через HTTPS** к доверенному резолверу. Мы обходим системный DNS целиком.

## Когда нужен

В наших тестах FragmentingHttpHandler даёт **92% success rate** в РФ. Остаются 8% случаев которые в основном - DNS-подмены провайдером. Для них DoH часто помогает.

Юзер сам выбирает в Settings → Серверы и сеть → Стратегия резолвинга:

| Стратегия | Резолвер |
|---|---|
| System | дефолтный, без обхода |
| Cloudflare DNS | `1.1.1.1` через `cloudflare-dns.com` |
| Quad9 | `9.9.9.9` через `dns.quad9.net` |
| NextDNS | `anycast.dns.nextdns.io` |

Дефолтное значение **system**, потому что DoH добавляет 50-100 мс latency на каждый новый домен (первый запрос). Для большинства юзеров не нужно.

## Реализация

`DohResolvingHttpHandler` - это `DelegatingHandler` который **перехватывает** SocketsHttpHandler.ConnectCallback. До коннекта мы делаем DoH-запрос:

```cs title="MiamiGraphics.Shell/Services/DohResolvingHttpHandler.cs (упрощено)"
public sealed class DohResolvingHttpHandler : DelegatingHandler
{
    private readonly DohResolver _resolver;
    private readonly ConcurrentDictionary<string, IPAddress[]> _cache = new();

    private async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (_cache.TryGetValue(host, out var cached)) return cached;

        // DoH запрос - DNS message в base64url через GET к /dns-query
        var dnsQuery = BuildDnsQuery(host, type: 1);  // A record
        var url = $"{_resolver.BaseUrl}/dns-query?dns={Base64UrlEncode(dnsQuery)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new("application/dns-message"));

        using var resp = await _internalHttp.SendAsync(req, ct);
        var dnsResponse = await resp.Content.ReadAsByteArrayAsync(ct);

        var ips = ParseDnsResponse(dnsResponse);
        _cache.TryAdd(host, ips);
        return ips;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        // Resolve хост вручную
        var ips = await ResolveAsync(req.RequestUri!.Host, ct);
        // ... TCP connect to ips[0], TLS handshake (с TLS frag) ...
        // Дальше обычный HTTP request
    }
}
```

В реальности это **сложнее** - нужно handling fallback'а если первый IP не отвечает, IPv4/IPv6 dual-stack, TTL respecting. Реальная имплементация ~200 строк.

## DNS Cache

DoH-запросы дорогие (50-200 мс). Кешируем результат in-memory на время жизни процесса. TTL **не уважаем** - закешировал один раз, до перезапуска лаунчера.

Это работает потому что:

- Наши домены **редко** меняют IP (CF anycast, IP стабильные годами);
- При перезапуске лаунчера cache сбросится.

Для нормальных DNS-клиентов respect TTL - best practice, но для нашего use-case overkill.

## DoH через TLS-frag handler

Вот тут интересная композиция: DoH делает HTTPS-запрос к `cloudflare-dns.com` (или другому резолверу). Этот HTTPS запрос **тоже** может быть зарезан DPI по SNI. Поэтому **внутренний** HttpClient в DohResolvingHttpHandler - это **тот же** FragmentingHttpHandler:

```
Юзерский запрос на cdn.miamigraphicsstorage.uk
    ↓ DohResolvingHttpHandler.SendAsync
    ↓ нужно резолвить → DoH-запрос на cloudflare-dns.com
        ↓ внутренний FragmentingHttpHandler
        ↓ TLS handshake к 1.1.1.1 с TLS frag
        ↓ получаем DNS response
    ↓ имеем IP для cdn.miamigraphicsstorage.uk
    ↓ TCP connect + TLS handshake к этому IP (с FragmentingHttpHandler)
    ↓ получаем response
```

Двойной TLS-frag на каждый новый домен. Дорого, но **работает**.

## Region selector

В Settings есть **другая** стратегия - Server Region (RU / EU). Это не DoH, а **другой Supabase endpoint**.

- **EU** - прямой к Supabase Mumbai через Cloudflare. Работает в Европе, СНГ кроме РФ.
- **RU** - через **прокси** на TimeWeb VPS в РФ. Прокси сидит на российском IP, ходит к Supabase через свой канал, отдаёт юзеру через российский трафик.

```cs title="MiamiGraphics.Shell/Services/MirrorSelector.cs (упрощено)"
public static async Task<string> RewriteUrlAsync(string url)
{
    var region = ServerRegionStore.Load();
    if (region == ServerRegion.Ru)
    {
        // меняем cdn.miamigraphicsstorage.uk → cdn-ru.proxy.example
        return url.Replace("cdn.miamigraphicsstorage.uk", "cdn-ru.proxy.example");
    }
    return url;   // EU - прямо
}
```

Это **полностью** обходит CF блок (трафик идёт на российский IP), но добавляет 100-200 мс latency на каждый запрос (прокси сидит в Москве, юзер в Питере → дополнительный hop).

Юзер выбирает регион при первом запуске в `RegionPicker`. Можно поменять позже в Settings.

[Дальше: AssetCache →](asset-cache.md)
