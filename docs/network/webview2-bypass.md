# WebView2 bypass перехватчик

WebView2 (Chromium внутри Miami Graphics WPF) делает HTTP запросы через **WinHTTP** - это API Windows, не через .NET `HttpClient`. Это значит наш [FragmentingHttpHandler](fragmenting-http-handler.md) с TLS-фрагментацией к WebView2 не применяется.

Для РФ юзеров без этого превью-картинки в UI просто не грузились (Cloudflare блок по SNI на уровне сети).

## Решение

WebView2 имеет API `CoreWebView2.WebResourceRequested` - event который срабатывает на **каждый** outbound HTTP/HTTPS запрос. Можно перехватить, обработать сами, вернуть response.

```cs title="MiamiGraphics.Shell/Services/WebView2BypassInterceptor.cs (упрощённо)"
public sealed class WebView2BypassInterceptor
{
    private readonly CoreWebView2 _webView;
    private readonly AssetCache _cache;
    private readonly HttpClient _http;   // с FragmentingHttpHandler

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

    public void Register()
    {
        foreach (var pattern in FilterPatterns)
            _webView.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All);
        _webView.WebResourceRequested += OnWebResourceRequested;
    }

    private async void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var url = e.Request.Uri;

            // 1. Cache hit?
            var cached = _cache.TryGet(url);
            if (cached is not null)
            {
                e.Response = _webView.Environment.CreateWebResourceResponse(
                    new MemoryStream(cached.Value.Body),
                    200, "OK",
                    $"Content-Type: {cached.Value.ContentType}\r\nAccess-Control-Allow-Origin: *");
                return;
            }

            // 2. Качаем через наш HttpClient с TLS fragmentation
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

            // 3. Сохраняем в кеш
            _cache.Put(url, bytes, contentType);

            // 4. Отдаём WebView2 как WebResourceResponse
            e.Response = _webView.Environment.CreateWebResourceResponse(
                new MemoryStream(bytes),
                (int)resp.StatusCode, resp.ReasonPhrase ?? "OK",
                $"Content-Type: {contentType}\r\nAccess-Control-Allow-Origin: *");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[wv2-bypass] failed {e.Request.Uri}: {ex.Message}");
            // Не устанавливаем e.Response → WebView2 сделает обычный запрос (упадёт, но это его проблема)
        }
        finally
        {
            deferral.Complete();
        }
    }
}
```

## MemoryStream-buffered, не Response stream

`CreateWebResourceResponse` принимает `Stream`. Можно было бы передать `await resp.Content.ReadAsStreamAsync()` напрямую - это **стримит** в WebView2 без full buffer. Гораздо эффективнее по памяти для крупных файлов.

Но был [баг](../history/codewalker.md): WebView2 на больших ответах (>10 МБ) иногда отрезает половину. Поток рассинхронизируется - WebView2 ждёт «больше байтов», наш stream уже закрылся.

Решение - буферить полностью в memory через `MemoryStream`. Чуть больше памяти на пик (50-100 МБ для большой текстуры), но **надёжно**. Все равно картинки и `.glb` редко выше 5 МБ.

## Регистрация filter'а

`AddWebResourceRequestedFilter` принимает URL pattern. Без фильтра мы получим event на **каждый** запрос из WebView2 (включая локальные `app://` и `chrome://`). Это медленно.

Регистрируем три группы доменов:

1. **Свои R2 customs** (`*.miamigraphicsstorage.uk`) и raw R2 endpoints (`*.r2.dev`, `*.r2.cloudflarestorage.com`). Через них идут все наши preview, gallery, glb и mirror'нутые картинки.
2. **YouTube thumbnail CDN** (`i.ytimg.com`, `img.youtube.com`). Когда админ пастит YouTube-ссылку в `video_url`, форма автоматически ставит preview на `i.ytimg.com/vi/{id}/maxresdefault.jpg`. Без перехвата эти превью валятся в РФ из-за замедления YouTube.
3. **Популярные image hosting** (`*.imgur.com`, `*.postimg.cc`, `*.ibb.co`). Историческое: до фичи auto-mirror админы могли вставлять URL'ы с этих хостингов. У старых редуксов в БД такие URL до сих пор могут остаться.

`CoreWebView2WebResourceContext.All` означает перехватываем все типы (image, fetch, xhr, document). Без `All` некоторые типы пропускаются и идут через WinHTTP.

Все запросы по pattern'ам выше идут через `SendAsync(HttpClient с FragmentingHttpHandler)`, то есть c TLS-фрагментацией. В РФ работает.

## Внешние URL: mirror в R2

Whitelist выше покрывает популярные хостинги, но не все. Если админ вставит ссылку с какого-нибудь редкого CDN, перехвата не будет. Решение: при сохранении редукса автоматически зеркалить любые внешние URL'ы в наш R2. См. admin/redux: автозеркалирование картинок.

После зеркалирования URL в БД указывает на `*.miamigraphicsstorage.uk/...`, который попадает в filter из группы (1). На клиенте у юзера в РФ картинка идёт через тот же interceptor + fragmenting handler, как и patch.zip.

## CORS

В response headers мы добавляем `Access-Control-Allow-Origin: *`. WebView2 enforces CORS, и если React попробует `fetch()` на CDN-домен без правильных CORS-headers - отказ.

Изначально на R2 headers не было (по умолчанию). Добавляли в R2 bucket-CORS config, но это применяется **only** для прямых запросов из браузера, а наш interceptor стоит **между** WebView2 и R2, и мы сами формируем headers. Можем добавить любые что нужны.

## Кэширование через AssetCache

Все ответы сохраняются в [AssetCache](asset-cache.md). При повторном запросе той же картинки (юзер прокрутил грид модов 3 раза) - мгновенный hit с диска, без сети.

Это критично для UX:

- Первый load каталога: 30-60 сек (качаем 200+ превью с CDN).
- Второй load (юзер закрыл/открыл лаунчер): 1-2 сек (всё с диска).

## Failure mode

Если interceptor упал по любой причине (HttpClient бросил, AssetCache.Put failed):

```cs
catch (Exception ex)
{
    Debug.WriteLine($"[wv2-bypass] failed {e.Request.Uri}: {ex.Message}");
    // Не устанавливаем e.Response → WebView2 сделает обычный запрос
}
finally { deferral.Complete(); }
```

Не устанавливая `e.Response`, WebView2 **возвращается** к нативному WinHTTP. Это значит для РФ юзера запрос упадёт (CF блокировка), но **остальные** запросы продолжат работать.

`deferral.Complete()` в `finally` - критически важно. Без него WebView2 будет **висеть** на этом запросе (ждёт нашего ответа), и UI зависнет. `using var deferral = e.GetDeferral();` тоже бы работал, но finally более explicit.

[Дальше: DoH →](doh.md)
