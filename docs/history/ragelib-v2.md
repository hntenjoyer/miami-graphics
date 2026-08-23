# RageLib v2 - оптимизация с 4 минут до 6 секунд

После [фикса критичных багов в форке](ragelib-v1.md) Smart Rebuild **работал**, но был ужасно медленный - **4 минуты 12 секунд** на типичный `update.rpf` (2 ГБ) на NVMe SSD.

На HDD - 12+ минут. Юзеры жаловались что «лаунчер виснет» - UI показывал «инжектирую» 4 минуты подряд без видимого прогресса.

Это была главная техническая проблема первого квартала. Расскажу что нашли и поправили.

## Profile сначала

Замерил где время уходит. Профилировал через `Stopwatch` вокруг каждого блока в `RebuildDirectory`:

```
[Profile] Read clean root TOC:           120 ms
[Profile] Process 50000 files:       240000 ms ←← основная боль
[Profile] Write dest TOC:                 80 ms
[Profile] Flush dest archive:          12000 ms
[Profile] ArchiveFix.exe call:          3500 ms
[Profile] Total:                      255700 ms (~4 минуты)
```

`Process 50000 files` - это циклы по всем 50000 файлам в архиве с копированием в новый. 240 секунд = 4.8 мс на файл в среднем. На NVMe это **очень много**.

Дальше профайлинг внутри `CopyFile`:

```
[Profile per file]
  - Open source IArchiveFile.GetStream():    0.5 ms
  - Read source stream (~50-200 KB):         1.8 ms
  - Write to MemoryStream:                   0.2 ms
  - DEFLATE compress (расход CPU):           1.4 ms
  - Write MemoryStream → dest CreateBinaryFile.Import():   0.9 ms
```

Половина времени **CPU** на DEFLATE. Половина - **read-write через MemoryStream**.

## Оптимизация 1: убрать MemoryStream

Первый таргет - лишний `MemoryStream` посередине:

```cs
// Было: stream через memory
using var ms = new MemoryStream();
sourceStream.CopyTo(ms);
ms.Position = 0;
destFile.Import(ms);

// Стало: прямой stream
destFile.Import(sourceStream);
```

`Import` в RageLib умеет читать любой Stream. Лишний MemoryStream был **на всякий случай** - типа «может source stream однопроходный». Оказалось RageLib `Open` возвращает seekable streams (через `FileStream` с offset).

После этой правки - `Process 50000 files: 180000 ms`. **60 секунд экономии**.

## Оптимизация 2: убрать double compression

Файлы внутри RPF хранятся **сжатые** DEFLATE-ом (если `IsCompressed=true`). Когда мы Read source - RageLib **decompress'ит** автоматически (мы получаем raw bytes). Когда Write dest - снова compress'ит.

Это **двойная** работа для **неизменных** файлов. Мы прочитали несжатые байты только чтобы их снова сжать.

Решение - flag `IsCompressed` копировать как есть, и **переливать сжатые** байты напрямую через **raw stream** API:

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:250"
else if (file is IArchiveBinaryFile bFile)
{
    var newF = destDir.CreateBinaryFile();
    newF.Name = file.Name;
    newF.Import(bFile.GetStream());        // raw stream, без decompression
    newF.IsCompressed = bFile.IsCompressed;
    newF.IsEncrypted = bFile.IsEncrypted;
    newF.UncompressedSize = bFile.UncompressedSize;
}
```

`bFile.GetStream()` теперь возвращает stream **без** decompression - сырые байты с диска. `Import` записывает их **как есть**. `IsCompressed` flag в TOC выставляется как у источника. RPF8 валиден, GTA принимает.

После - `Process 50000 files: 18000 ms`. **162 секунды экономии**. Это была главная победа.

`GetStream()` в нашем форке имеет два режима - `GetStream(decompress: true)` (для **изменённых** файлов где нужно прочитать содержимое) и `GetStream(decompress: false)` (для копирования без декода).

## Оптимизация 3: ленивая обработка nested RPF

В первой версии **каждый** вложенный `.rpf` пересобирался - даже если у мода в нём ничего не правилось. Из-за «на всякий случай». Это значит open + walk-через 200+ entries + write для **каждого** из 15 вложенных = тонна I/O.

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:223"
bool isRpf = file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
bool needsRebuild = false;
if (isRpf)
{
    string rpfPrefix = path + "/";
    needsRebuild = actionMap.Keys.Any(k => k.StartsWith(rpfPrefix, StringComparison.OrdinalIgnoreCase));
}

if (isRpf && needsRebuild && file is IArchiveBinaryFile bin)
{
    // только пересобираем если у мода есть actions внутри
    var inArc = RageArchiveWrapper7.Open(bin.GetStream(), file.Name);
    var outArc = RageArchiveWrapper7.Create(newF.GetStream(), file.Name);
    RebuildDirectory(inArc.Root, outArc.Root, path + "/", actionMap, patchDirectory);
    // ...
}
else if (file is IArchiveBinaryFile bFile)
{
    // обычная копия как чёрный ящик
    newF.Import(bFile.GetStream());
}
```

`actionMap.Keys.Any(k => k.StartsWith(rpfPrefix))` - `O(N)` scan по action keys. Action keys у типичного мода - 5-30 штук. Быстро.

Если внутри `scaleform_generic.rpf` мод **не** правил `hud_reticle.gfx` - мы **не** открываем `scaleform_generic.rpf`. Просто **переливаем** как обычный binary. Из 15 вложенных пересобираем 1-2.

После - `Process 50000 files: 4500 ms`. **13 секунд** экономии. Меньше потому что 1-2 вложенных всё-таки тяжёлые (`ptfx.rpf` с 200+ файлами).

## Оптимизация 4: write buffer size

`Flush dest archive: 12000 ms` - 12 секунд на финальный sequential write 2 ГБ файла. Это медленно даже для NVMe (4-7 ГБ/с).

Профайл показал - RageLib пишет через `FileStream` с дефолтным буфером 4096 байт. Это значит **миллионы** syscalls.

Поправили в форке RageLib:

```csharp
// В RageArchiveWrapper7
var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
    bufferSize: 1 * 1024 * 1024,   // 1 MB buffer вместо 4 KB
    options: FileOptions.SequentialScan);
```

1 МБ буфер + `FileOptions.SequentialScan` (подсказка ОС что мы пишем последовательно - оптимизирует prefetch).

После - `Flush dest archive: 1100 ms`. **11 секунд** экономии.

## Финальный профайл

```
[Profile] Read clean root TOC:           120 ms
[Profile] Process 50000 files:          4500 ms
[Profile] Write dest TOC:                 80 ms
[Profile] Flush dest archive:           1100 ms
[Profile] ArchiveFix.exe call:          3500 ms
[Profile] Total:                        9300 ms (~9 секунд)
```

С учётом OS prefetch и warm-up - реально на повторный install ~**6 секунд**.

| Метрика | До | После |
|---|---|---|
| Smart Rebuild 2 ГБ | **4:12** | **6 сек** |
| ArchiveFix | 5 сек | 3 сек |
| **Total install (без download)** | **4:17** | **9 сек** |

**До 40 раз быстрее**. Это окупило неделю работы над форком RageLib.

## Что мы не оптимизировали

- **Parallel rebuild подаpхивов** - теоретически 15 вложенных можно делать параллельно. На практике bottleneck это **disk write** не CPU, параллелизация дала бы 5-10% не больше.
- **Memory pooling** - пробовали `ArrayPool<byte>` для буферов копирования. Эффекта на NVMe нет, на HDD 5% улучшение. Не стоит сложности кода.
- **Skip ArchiveFix для unmodified rpf** - теоретически можно не запускать ArchiveFix если внутри `update.rpf` не было правок. Но это **редкий** случай (зачем тогда install pipeline?), и риск что мы что-то пропустим и GTA не примет - слишком высокий. Запускаем всегда.

## Что узнали по пути

- **MemoryStream не free.** Лишний middle-buffer на 50000 операций стоит 60 секунд.
- **Decompress + recompress** одних и тех же данных - самый дорогой no-op. Всегда проверять можно ли пройти данные **сырыми**.
- **Default буферы Stream.Write слишком маленькие** для больших файлов. Дефолт 4 КБ оптимизирован под 1990-е disk speeds.
- **OS prefetch hints** (`FileOptions.SequentialScan`) реальны и работают - 20-30% улучшение на больших sequential writes.

## Что сейчас наш RPF Smart Rebuild делает

Для типичного мода:

1. Open clean (lazy, no full load).
2. Iterate 50000 files в `update.rpf`:
   - 49998 файлов - **copy raw stream без decompression**;
   - 2 файла - **read + replace** (наши Replace actions);
3. Внутри встречаем 15 вложенных `.rpf`:
   - 13 копируем как обычный binary (нет actions внутри);
   - 2 рекурсивно пересобираем (там есть actions).
4. Write TOC и flush.
5. Atomic rename `update.rpf.hnt_temp` → `update.rpf`.
6. ArchiveFix пересчитывает hashes.

Это самое экономное что возможно с RPF форматом. Можно было бы делать **in-place** редактирование TOC + appending новых файлов в конец - но это требовало бы значительной переписки RageLib и могло бы дать **несовместимый** результат с разными версиями GTA. Решили что 6 секунд это «достаточно быстро».

[Дальше: трейсера-сага →](tracers.md)
