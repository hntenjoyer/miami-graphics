# Atomic writes

Все критичные writes в backup pipeline сделаны через **temp + rename** с явным `Flush(true)` для fsync на диск.

## Pattern

```cs title="MiamiGraphics.Core/System/BackupService.cs"
private static async Task CopyFileAsync(string srcPath, string dstPath, ..., CancellationToken ct)
{
    Directory.CreateDirectory(Path.GetDirectoryName(dstPath)!);
    var tempPath = dstPath + ".restoring";
    try
    {
        await using var src = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
        await using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true))
        {
            // ... копируем буферами ...
            await dst.FlushAsync(ct);
            dst.Flush(flushToDisk: true);   // fsync на устройство
        }
        File.Move(tempPath, dstPath, overwrite: true);   // atomic rename
    }
    catch
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        throw;
    }
}
```

Три гарантии:

1. **Никогда нет partially-written файла под именем `dstPath`**. Либо весь файл успешно скопирован и переименован, либо файла нет (или старая версия - если перезаписываем).
2. **`Flush(flushToDisk: true)`** заставляет ОС записать буфер на диск. Без него `await dst.FlushAsync(ct)` только сбрасывает buffer .NET в OS write cache. Если питание оборвётся между FlushAsync и реальной записью на диск - пустой файл.
3. **`File.Move` на NTFS** - atomic для same-volume rename. Это гарантия ОС: либо ты видишь старое имя, либо новое, никогда «полу-старое полу-новое».

## Почему `Flush(true)` критичен

`FlushAsync(ct)` в .NET делает следующее:

1. Сбрасывает managed buffer `FileStream` в OS write cache.
2. Возвращает control приложению.

OS дальше **отложенно** пишет на диск (Lazy Write механизм Windows). На NVMe это обычно сотни миллисекунд, на HDD - несколько секунд. Если в этот момент питание отрубилось, юзер открыл диспетчер задач и убил процесс, или OS словила BSOD - файл **остаётся** в OS cache, на физическом диске **пусто или часть**.

`Flush(flushToDisk: true)` вызывает `FlushFileBuffers` Win32 API - синхронный fsync. Возвращается только когда драйвер диска ответил «записано на физический носитель». Это **дорого** (50-200 мс), но даёт гарантию.

Делаем только для **критичных** writes:

- backup `clean/update.rpf`;
- backup `snapshot/update.rpf`;
- `manifest.json`;
- `install_state.json` (post-install marker).

Не делаем для **рабочих** временных файлов:

- `patch_files/` распакованный zip (всегда переподтянем);
- `<tmp>/redux_install_xxx/` (cleanup в любом случае);
- Кеш downloaded patch.zip (если поломалась - заново скачаем).

## Почему наивный File.WriteAllText недостаточен

```cs
// Плохо:
File.WriteAllText(_manifestPath, json);
```

Под капотом `WriteAllText` это:

1. Открыть `_manifestPath` с FileMode.Create (truncate to 0).
2. Записать json.
3. Закрыть.

Если процесс убит между 1 и 2 - файл существует, но он **пустой**. ReadManifest потом увидит 0-byte файл, не сможет распарсить, выкинет exception, лаунчер подумает что backup отсутствует, попытается сделать заново → но снимок оригинала юзера **уже потерян** (если он был там).

Pattern с temp+rename защищает от этого:

```cs
// Хорошо:
private void WriteManifest(Manifest m)
{
    var json = JsonSerializer.Serialize(m, ManifestJson);
    var tmp = _manifestPath + ".tmp";
    File.WriteAllText(tmp, json);
    File.Move(tmp, _manifestPath, overwrite: true);
}
```

Если процесс убит во время `WriteAllText(tmp, json)` - `tmp` остаётся в полупустом виде, но `_manifestPath` цел. При следующем запуске мы прочитаем старый manifest успешно, а битый `.tmp` файл - мусор который никто не читает (можно вычистить на старте, но мы не делаем - он сам перезапишется на следующем backup).

## Real bug: до атомарных writes

В одной из ранних версий manifest.json иногда оказывался **пустым** у юзеров после краша лаунчера или принудительного выключения PC. Они потом не могли установить мод - backup считался отсутствующим, install pipeline отказывался работать без backup'а.

Воспроизводилось только когда юзер ловил backup в момент его записи. На быстрых SSD это окно <100 мс - ловилось редко. Но за полгода набралось ~15 жалоб.

Audit-проход добавил temp+rename + Flush(true) на все critical writes. После релиза баг исчез.

## Что НЕ atomic в pipeline

| Операция | Atomic? | Почему так |
|---|---|---|
| Backup manifest.json | ✅ temp + rename | Master state |
| Backup clean.rpf | ✅ temp + rename + Flush(true) | Critical resource |
| Backup snapshot.rpf | ✅ через CopyFileAsync с temp | Backup юзерского оригинала |
| install_state.json | ✅ temp + rename | Post-install marker |
| Smart Rebuild → update.rpf | ✅ через .hnt_temp + 2× File.Move | Самый критичный - `.bak` слой |
| patch.zip распаковка | ❌ обычная распаковка в temp dir | На крэше - re-download заново |
| Renderer cache (R2 → диск) | ❌ direct write | Не критично, recover'ится |
| AssetCache (картинки UI) | ✅ .meta-first, потом тело | Иначе [orphan keys](../network/asset-cache.md) |

## CopyFileAsync - сравнение с обычным File.Copy

```cs title="MiamiGraphics.Core/System/BackupService.cs"
private static async Task CopyFileAsync(string srcPath, string dstPath, ...)
{
    var tempPath = dstPath + ".restoring";
    // ... читаем + пишем через буфер 64 КБ с прогрессом ...
    await dst.FlushAsync(ct);
    dst.Flush(flushToDisk: true);
    File.Move(tempPath, dstPath, overwrite: true);
}
```

Плюсы против `File.Copy`:

- Прогресс-callback в UI (`CopyFileAsync` принимает `Action<int>? progressPercent`);
- Atomic rename (`File.Copy` пишет прямо в dst, любой crash оставляет partial).

Минусы:

- На 30% медленнее на больших файлах (мы copy через managed buffers, `File.Copy` это native CopyFileEx).

На 2 ГБ `update.rpf` это +2-3 секунды. Считаем приемлемой ценой за прогресс и атомарность.

[Дальше: 3D и GLB viewer →](../3d/glb-viewer.md)
