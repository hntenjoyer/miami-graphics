# RageLib v1 - почему первая попытка не взлетела

После [CodeWalker'а](codewalker.md) пробовали RageLib **напрямую** - без модификаций. Это вот этот upstream - [Neodymium146/gta-toolkit](https://github.com/Neodymium146/gta-toolkit).

Логично - это **C# API** для RPF формата, ровно что нам нужно. Открыть архив, пройтись по дереву, что-то изменить, записать обратно. Никаких CLI spawn'ов.

## План

```csharp
using var clean = RageArchiveWrapper7.Open(cleanPath);
using var modded = RageArchiveWrapper7.Open(moddedPath);

// Найти изменённые файлы
var actions = new List<PatchAction>();
foreach (var file in modded.Root.GetFiles())
{
    var cleanFile = clean.Root.GetFiles().FirstOrDefault(f => f.Name == file.Name);
    if (cleanFile == null) actions.Add(new(Import, file));
    else if (!SamesContent(cleanFile, file)) actions.Add(new(Replace, file));
}

// Применить actions к clean → новый файл
using var dest = RageArchiveWrapper7.Create(outPath);
RebuildDirectory(clean.Root, dest.Root, actions);
dest.Flush();
```

Простая логика. Должно было работать.

## Проблема 1: ключи NG не подгружаются

Самый первый прогон - RageLib открывает чистый `update.rpf` и **молча** возвращает пустое дерево. Никаких exception'ов. `clean.Root.GetFiles()` возвращает 0.

Думали мод сломан. Поставили на разный мод - то же самое. На любой `update.rpf`.

Расследование показало: RageLib искал ключи (`gtav_aes_key.dat`, `gtav_ng_key.dat`) в фиксированном пути относительно `AppDomain.CurrentDomain.BaseDirectory`. Когда мы запускали из `bin/Debug/net8.0/` - путь не совпадал, ключи не найдены, `GTA5Constants.PC_NG_KEYS` = `null` array.

Дальше RageLib **не падает** при отсутствии ключей. Просто **тихо** возвращает что прочитал, а прочитать ничего не может (TOC зашифрован, без ключей не парсится).

Лечится явной инициализацией:

```csharp
GTA5Constants.LoadFromFolder(@"D:\path\to\keys\");
```

После этого 101 ключ загрузился, `clean.Root` стал давать реальные 50000+ файлов. **OK, идём дальше**.

## Проблема 2: RageLib не умеет удалять файлы

После того как заработало чтение - пробую написать модифицированный `update.rpf`.

Тестовый кейс: убрать один файл (`Delete` action).

```csharp
using var dest = RageArchiveWrapper7.Create(outPath);
foreach (var file in clean.Root.GetFiles())
{
    if (action.Type == ActionType.Delete && file.Name == action.TargetName)
        continue;   // пропускаем - не копируем
    // ...
}
```

Получаем выходной `update.rpf`. По размеру - на 10 КБ меньше (как и ожидалось - мы удалили один файл). GTA запускается → крашится на загрузке `update.rpf`. «Files corrupted».

Откатился - попробовал не Delete а **Replace**. Заменяю один файл другим того же размера. Тот же crash.

Оказалось RageLib API для **создания нового** архива (`Create + Add files`) генерирует файл с **другой** структурой TOC чем оригинальный. Какие-то поля reserved/padding отличаются. GTA эту структуру не любит - она проверяет некий магический паттерн в шапке.

С `Open(existing) + Modify + Flush` - RageLib **не реализовал** mutate-in-place операции. `Open` это read-only handle, `Flush` ничего не делает на нём.

Пришли в тупик. RageLib **только читает**. Запись - только полный create-from-scratch, и оно не работает с GTA.

## Проблема 3: encryption flag inheritance для nested RPF

Параллельно ещё одна боль. При попытке создать `dest` через `Create()` - он создаётся с **дефолтным** encryption (NONE). А оригинальный `update.rpf` был NG.

GTA при загрузке смотрит encryption flag в шапке. Если файл объявил «NG» но реально содержимое шифровано как-то иначе - `files corrupted`.

Решается вручную:

```csharp
dest.archive_.Encryption = clean.archive_.Encryption;
```

`archive_` это internal поле, не публичное. Использовать его - хак, но работало.

Для вложенных RPF (которые мы тоже пересобираем в Smart Rebuild) - это **критично**. Иначе каждый вложенный `.rpf` после rebuild имеет другой encryption чем оригинал → GTA отбрасывает.

## Проблема 4: огромные временные файлы на диске

Даже если бы первые 3 проблемы решились - четвёртая всё равно убивала performance.

В RageLib upstream `RageArchiveWrapper7.Open` загружает **весь** архив в `MemoryStream`. Это 2 ГБ RAM на каждый `update.rpf`. Для нашего pipeline'а где мы open clean + open modded + create dest - это **6 ГБ** RAM minimum.

На 8 ГБ ноутбуках сразу OOM. На 16 ГБ работает но swap из-за других процессов (браузер, VS, лаунчер сам по себе).

Конечно можно было держать архивы по очереди - open clean → close → open modded - но это значит **двойное** чтение с диска. На HDD это 60 секунд только на open phase.

## Решение - форк

Пробовали fix'ить эти 4 проблемы патчами к upstream'у. Каждая правка трогала 50-100 строк в RageLib. После 3-й итерации pull request'а в upstream решил - **слишком много** правок, проще fork.

`RageLib` и `RageLib.GTA5` теперь живут как ProjectReference в нашем repo. В них:

- **Lazy stream-based чтение** - не грузим архив в RAM целиком. `IArchiveFile.GetStream()` возвращает `FileStream` напрямую с offset'ом и size'ом из TOC.
- **Stream-based Smart Rebuild** - мы [пересоздаём весь архив](../inject/smart-rebuild.md) каждый install, никаких mutate-in-place. Это даёт нам **корректный** новый файл.
- **Encryption inheritance явно** - `outArc.archive_.Encryption = inArc.archive_.Encryption` в каждом nested rpf.
- **Ключи загружаются автоматически** - поиск в `AppContext.BaseDirectory`, walk-up, fallback в `%LocalAppData%`.

Это решило все 4 проблемы. Полная rewrite за неделю работы.

Но **performance** всё ещё был ужасным - 4 минуты на rebuild 2 ГБ. Это уже [следующая глава](ragelib-v2.md).

[Дальше: RageLib v2 - оптимизация с 4 минут до 6 секунд →](ragelib-v2.md)
