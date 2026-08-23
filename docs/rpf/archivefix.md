# ArchiveFix.exe - пересчёт хешей после rebuild

После того как [Smart Rebuild](../inject/smart-rebuild.md) собрал новый `update.rpf`, файл валиден с точки зрения структуры RPF - TOC корректен, контент на месте, encryption правильный. Но игра его всё равно отбросит.

Причина - внутри RPF8 шапки есть **поле checksum**, которое игра пересчитывает при загрузке и сравнивает с записанным. Если не совпало - `files corrupted` без подробностей. RageLib это поле **не** пересчитывает (это была одна из его дыр в первой версии нашего инжектора, [см. историю](../history/ragelib-v2.md)).

Пересчётом занимается **отдельный нативный exe** - `ArchiveFix.exe`. Это open-source инструмент (его автор - `dexyfex`, тот же что писал CodeWalker), мы используем его через `Process.Start` сразу после rebuild.

## Где лежит

```
additionals/
└── ArchiveFix.exe         ~400 KB native binary
```

Поставляется вместе с лаунчером в `additionals/`. Эту папку Inno installer кладёт рядом с основным exe.

`RpfInjectEngine` ищет его в нескольких кандидатах:

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:28"
private static string ResolveArchiveFixPath()
{
    string[] candidates = {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "additionals", "ArchiveFix.exe"),
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "ArchiveFix.exe"),
    };
    foreach (var c in candidates)
        if (File.Exists(c)) return c;

    // walk up до 8 уровней
    var dir = AppDomain.CurrentDomain.BaseDirectory;
    for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
    {
        var p = Path.Combine(dir, "additionals", "ArchiveFix.exe");
        if (File.Exists(p)) return p;
        dir = Path.GetDirectoryName(dir);
    }

    // fallback на LocalAppData (наш кэш)
    var localAppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics", "additionals", "ArchiveFix.exe");
    if (File.Exists(localAppData)) return localAppData;

    return candidates[0];
}
```

Walk-up нужен потому что в dev-сборке `bin/Debug/...` лежит глубоко, а `additionals/` рядом с корнем репо. В release-сборке (single-file exe) `BaseDirectory` указывает на extract'нутый temp, там `additionals/` правильно рядом.

## Как вызываем

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:441"
private bool FixArchive(string rpfPath)
{
    if (!File.Exists(_archiveFixPath))
    {
        Console.WriteLine($"[ArchiveFix] WARN: не найден - пропускаю fix-up для {Path.GetFileName(rpfPath)}");
        return false;
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = _archiveFixPath,
        Arguments = $"\"{rpfPath}\"",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        WorkingDirectory = Path.GetDirectoryName(_archiveFixPath)
    };

    using var process = Process.Start(startInfo);
    // ...
    bool exited = process.WaitForExit(15000);
    if (!exited) { process.Kill(); return false; }
    if (process.ExitCode != 0) return false;
    return true;
}
```

15 секунд timeout - на NVMe `ArchiveFix` обрабатывает 2 ГБ за ~3-4 секунды, на медленных SSD до 10. Если за 15 не уложился - что-то зависло, убиваем.

`RedirectStandardInput = true` - ArchiveFix в интерактивном режиме ждёт Enter при ошибках. Мы перенаправляем stdin чтобы он не висел в фоне.

## Что он делает

ArchiveFix:

1. Открывает RPF архив.
2. Пересчитывает hash для каждой TOC entry (это хеш name + size + offset + flags).
3. Пересчитывает общий checksum шапки.
4. Если внутри есть вложенные `.rpf` - рекурсивно делает то же самое для них.
5. Пишет обратно в файл.

Для ресурсных файлов (`.ydr`/`.ydd`/`.yft`/`.ypt`/`.ytd`) он **не трогает** их содержимое - внутри RSC7 свои хеши и version числа, которые корректны если файл валиден сам по себе. Мы их не модифицируем, только заменяем целиком или импортируем чужие - поэтому внутренние RSC7 хеши автоматически верны.

## Что будет если пропустить

Когда мы только поставили на single-file сборку, на этапе extraction additionals/ оказывался не в `BaseDirectory`, а где-то в `temp\.net\<guid>\` ниже. Walk-up до 8 уровней не находил его. `FixArchive` возвращал `false` тихо (был `catch{}`), Smart Rebuild возвращал успех, юзер запускал GTA - `files corrupted`. Несколько релизов с этим багом, юзеры жаловались что моды не работают, парсер давал зелёный «установлено».

Сейчас:

- `FixArchive` возвращает `bool` и **логирует** каждый шаг (`[ArchiveFix] WARN: не найден`, `[ArchiveFix] ERROR: exit code N`);
- `InjectPatch` сохраняет это в результат;
- В debug-логах юзера сразу видно что fix-up не отработал.

См. [защита pipeline'а](../protection/guards.md) - это один из обязательных шагов.

[Дальше: парсер донора →](../parser/what-we-extract.md)
