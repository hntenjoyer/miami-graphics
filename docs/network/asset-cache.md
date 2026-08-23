# AssetCache

Диск-кэш для всех ответов которые мы получаем через [WebView2BypassInterceptor](webview2-bypass.md). Это превью-PNG модов, `.glb` модели, иконки.

## Зачем

UI Miami Graphics это грид модов - десятки карточек с превью на одном экране. При первом open приложения мы качаем все эти превью с CDN. Это:

- 200+ HTTP-запросов;
- ~50-150 МБ совокупного трафика;
- 30-60 секунд при средней скорости.

Без кэша **каждый** запуск лаунчера будет такой же - поскольку WebView2 чистит свой кэш при `factoryResetAndRestart` или иногда по своей логике.

С AssetCache:

- Первый запуск: 60 сек (полный download);
- Повторные: 1-2 секунды (всё с диска).

## Что cache не делает

- **Не respect HTTP cache headers.** Никаких If-Modified-Since, никаких ETag-валидаций. Закешировали один раз, до явной инвалидации.
- **Не имеет TTL.** Файлы живут пока не превышен общий лимит (см. eviction).
- **Не валидирует SHA при hit.** Если кто-то руками поломал файл - отдадим поломанный, юзер увидит битое превью.

Это упрощения которые работают потому что:

- Превью и GLB **immutable** на CDN - мы их не перезаписываем (новый mod = новый URL);
- Лимит размера cache не даёт ему раздуться;
- Юзер может вручную почистить через Settings → Reset cache.

## Структура

```
%LocalAppData%\MiamiGraphics\cache\assets\
├── 1a2b3c4d.bin           ← body
├── 1a2b3c4d.bin.meta      ← content-type
├── ...
```

Имя файла = `KeyFromUrl(url)` - это short hash от URL'а. Длина 8 hex символов:

```cs title="MiamiGraphics.Shell/Services/AssetCache.cs"
private static string KeyFromUrl(string url)
{
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
    return BitConverter.ToString(bytes, 0, 4).Replace("-", "").ToLowerInvariant();
}
```

8 hex = 32 бита = 4 миллиарда возможностей. На 1000 файлов в cache шанс коллизии ничтожен (~0.0001%). Если случится - последний `Put` затрёт предыдущий, юзер получит wrong content. В худшем случае - кривое превью, не data loss.

`.meta` рядом с body содержит content-type:

```
1a2b3c4d.bin.meta
└── "image/png"
```

Без правильного content-type WebView2 не отрендерит файл как картинку. Это [был баг](../update/loop-incident.md) - после crash между записью body и записью meta, файл оставался без `.meta`, отдавался с `application/octet-stream`, превью не показывалось.

## Atomic put

После того баге [исправили pattern](#meta-first):

```cs title="MiamiGraphics.Shell/Services/AssetCache.cs"
public void Put(string url, byte[] body, string contentType)
{
    if (body.Length == 0) return;
    var key = KeyFromUrl(url);
    var path = Path.Combine(_root, key);
    var tmp = path + ".tmp";
    var metaTmp = path + ".meta.tmp";
    var metaPath = path + ".meta";
    try
    {
        // 1. Сначала body во временный
        File.WriteAllBytes(tmp, body);
        // 2. Meta во временный
        File.WriteAllText(metaTmp, contentType, Encoding.UTF8);
        // 3. Сначала переименовываем META (а не body)
        File.Move(metaTmp, metaPath, overwrite: true);
        // 4. Потом body
        File.Move(tmp, path, overwrite: true);
        _knownKeys[key] = 0;
    }
    catch (Exception ex) { ... }
}
```

Порядок critical:

- Сначала `.meta` файл оказывается на правильном месте;
- Потом body.

Если процесс упал между шагами - мы имеем `.meta` без body. На next start `WarmInMemoryIndex` увидит:

```cs
foreach (var file in Directory.EnumerateFiles(_root))
{
    var name = Path.GetFileName(file);
    if (name.EndsWith(".meta")) continue;
    if (name.EndsWith(".tmp"))
    {
        try { File.Delete(file); } catch { }   // residual cleanup
        continue;
    }
    // body есть, проверяем что .meta тоже есть
    if (!File.Exists(file + ".meta"))
    {
        orphans++;
        try { File.Delete(file); } catch { }   // удаляем orphan body
        continue;
    }
    _knownKeys.TryAdd(name, 0);
}
```

Body без meta - orphan, удаляем. Meta без body - оставляем (next Put перезапишет). При следующем запросе того же URL - cache miss, заново качаем и сохраняем.

## TryGet

```cs
public (byte[] Body, string ContentType)? TryGet(string url)
{
    var key = KeyFromUrl(url);
    if (!_knownKeys.ContainsKey(key)) return null;
    var path = Path.Combine(_root, key);
    if (!File.Exists(path)) { _knownKeys.TryRemove(key, out _); return null; }

    var body = File.ReadAllBytes(path);
    var contentType = File.Exists(path + ".meta")
        ? File.ReadAllText(path + ".meta", Encoding.UTF8)
        : "application/octet-stream";   // fallback - но WarmInMemoryIndex уже отсеял orphans

    File.SetLastAccessTimeUtc(path, DateTime.UtcNow);   // для LRU eviction
    return (body, contentType);
}
```

`SetLastAccessTimeUtc` - нужно для LRU eviction. NTFS по умолчанию хранит last access time для файлов, и мы используем это как «когда последний раз показывали».

## LRU Eviction

```cs
public void EvictIfOversize()
{
    var files = new DirectoryInfo(_root).EnumerateFiles()
        .Where(f => !f.Name.EndsWith(".meta", StringComparison.Ordinal)
                 && !f.Name.EndsWith(".tmp", StringComparison.Ordinal))
        .Select(f => new {
            Body = f,
            Meta = new FileInfo(f.FullName + ".meta"),
            LastAccess = f.LastAccessTimeUtc,
        })
        .OrderBy(x => x.LastAccess)
        .ToList();

    long total = files.Sum(x => x.Body.Length);
    foreach (var item in files)
    {
        if (total <= MaxCacheBytes) break;   // 500 MB
        try {
            item.Body.Delete();
            if (item.Meta.Exists) item.Meta.Delete();
            total -= item.Body.Length;
            _knownKeys.TryRemove(item.Body.Name, out _);
        } catch { /* ignore */ }
    }
}
```

Раз в час (`StartupTimer` в Shell) проходим, считаем общий размер. Если > 500 МБ - удаляем **старые** файлы пока не уложимся.

500 МБ выбрано эмпирически - это вмещает ~5000 средних превью + ~50 GLB моделей. Для big-time юзера который пересмотрел половину каталога этого хватает.

## Concurrency

`_knownKeys` это `ConcurrentDictionary<string, byte>`. Используем его как **множество** - value не важен (всегда 0), важен только ключ.

При параллельных Put/Get/Evict гонок нет - каждая операция атомарна, и все они идут через single-thread JS callback от WebView2 (UI-поток).

[Дальше: Zapret →](zapret.md)
