# Donor cache

При [customize-импорте](data-model.md) нам надо качать **отдельные** компоненты из чужих редаксов. Если юзер экспериментирует - берёт минимапу из X, потом меняет на Y, потом снова на X - мы не хотим качать **тот же** файл по 4 раза.

## Где живёт

```
%LocalAppData%\MiamiGraphics\cache\donors\
├── <reduxId>_<versionId>_minimap.bin
├── <reduxId>_<versionId>_crosshair.bin
├── <reduxId>_<versionId>_tracers.bin
├── <reduxId>_<versionId>_armor.zip
└── ...
```

`<versionId>` это GUID конкретной version row, или `00000000-0000-0000-0000-000000000000` для legacy donor'ов без версий.

## Логика

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs (упрощено)"
private async Task<string> EnsureDonorCachedAsync(
    string donorReduxId,
    string componentType,
    HttpClient http,
    Action<string, int>? emitDetail,
    Guid? donorVersionId)
{
    var cacheKey = $"{donorReduxId}_{donorVersionId ?? Guid.Empty}_{componentType}";
    var cachePath = Path.Combine(_donorCacheDir, cacheKey + ".bin");

    if (File.Exists(cachePath))
    {
        emitDetail?.Invoke($"донор-кэш hit: {componentType}", 0);
        return cachePath;
    }

    // Resolve URL для конкретной версии
    var resolved = await ResolveVersionAsync(donorReduxId, donorVersionId);
    if (!resolved.Urls.Components.TryGetValue(componentType, out var url))
        throw new InvalidOperationException($"У донора нет компонента {componentType}");

    emitDetail?.Invoke($"качаем донор: {componentType}", 0);
    await DownloadFileAsync(http, url, cachePath, ct);

    return cachePath;
}
```

Cache **не проверяет SHA** при hit - мы доверяем что то что лежит на диске это то что мы скачали раньше. Если кто-то руками подменил файл - это его проблема.

## Cache invalidation

Не делаем автоматическую. Cache **накапливается** пока пользователь активно делает customize. Если ему надоел и он удалил `cache/donors/` руками - следующий customize заново качает.

Альтернативы которые отвергнуты:

- **TTL** (например удалять файлы старше 30 дней). Сложно, не нужно - диск 5 ГБ под весь backup, donor-cache редко больше 1 ГБ.
- **LRU eviction** (как [AssetCache](../network/asset-cache.md)). Бессмысленно - donor-файлы маленькие (10-50 МБ каждый), редко.
- **Версионирование с server-side ETag**. Перерendant работа - donor-файлы **immutable** в R2 (мы загружаем `<id>/<version>/<component>.bin` и не перезаписываем).

## Когда donor обновился

Edge case: админ заменил `<reduxId>/v1/minimap.gfx` на R2 (например исправил баг в моде). У юзера в cache лежит **старая** версия.

Сейчас юзер получит старую при customize. Это **ограничение** текущего дизайна - мы не отслеживаем когда donor обновился.

В будущем планируем добавить `<reduxId>/v1/minimap.gfx.sha256` файл рядом - если SHA отличается от закешированного → re-download. Сейчас не реализовано потому что:

- админы **редко** перезаливают существующие redux'ы (обычно создают новую версию V2);
- юзеры могут вручную clean cache через Settings → Reset cache.

## Cleanup при uninstall

Inno uninstaller удаляет всю `%LocalAppData%\MiamiGraphics\` целиком, включая `cache/donors/`. Никаких специальных cleanup-стадий.

[Дальше: сеть и DPI bypass →](../network/fragmenting-http-handler.md)
