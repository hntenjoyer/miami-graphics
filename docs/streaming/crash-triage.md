# Разбор client_*.log Majestic

Жалоба «после установки вылетает игра» приходит с приложенным `client_*.log` клиента Majestic. Каждый такой разбор раньше начинался одинаково: найти в четырёх тысячах строк место смерти, посчитать предвестники, отличить одну подпись краха от другой. Подписей уже минимум три, и путать их нельзя - лечатся они разным.

Вторая половина проблемы хуже первой. Логи Majestic наших файлов по имени не называют: внутри `dlc.rpf` всё обезличено, и по строке `Last loaded asset: tat_rt_029_a_uni_hires.ytd` понять, стоял ли у человека наш ганпак и какой именно редукс, нельзя. Связь «подпись краха ↔ что мы поставили» приходилось угадывать по переписке.

Поэтому разбор встроен в лаунчер. На старте фоновая задача читает свежие логи Majestic, и если в них есть падение - кладёт подпись в **наш** журнал (`%LocalAppData%\MiamiGraphics\logs\miami-launcher.log`) вместе со списком того, что мы на тот момент установили. Игрок присылает один файл, и связь в нём уже сведена.

Код разделён на две части: чистый разбор строк (`MajesticCrashLog`, тесты на живых логах) и работа с файлами, дедупликация и журнал (`MajesticCrashWatch`).

## Три подписи

Подпись выводится из трёх признаков - `Oversized`, `StreamingFailure` и счётчика `Failed to request model`:

```cs title="MiamiGraphics.Core/Services/MajesticCrashLog.cs"
public string Signature =>
    Oversized || StreamingFailure ? "стриминг: запись не влезла (Oversized/ERR_STR_FAILURE)"
    : FailedModelRequests >= 200   ? "сборка педов на спавне (лавина Failed to request model)"
    : "неизвестная";
```

| Подпись | По чему определяется | Что за ней стоит |
|---|---|---|
| стриминг | строка `Oversized file (>100MB)` или `ERR_STR_FAILURE` | ресурсная запись не влезла в стриминг: битое содержимое под ресурсным именем либо здоровый, но слишком жирный `.ydr` |
| сборка педов | `Failed to request model` встретилась 200 раз и больше | игра не успевает собирать модели педов других игроков при массовом спавне |
| неизвестная | есть `MINIDUMP`, нет ни того, ни другого | причина вне нашей зоны, дальше руками |

Порядок веток не случаен: `Oversized`/`ERR_STR_FAILURE` перебивает счётчик педов. Лавина `Failed to request model` бывает и в тех логах, где реальная причина - отказ стриминга, и если проверять счётчик первым, часть падений уедет в чужую подпись.

### Откуда взялось число 200

Счётчик `Failed to request model` оказался единственным признаком, который отличает упавшую сессию от выжившей. В разобранных логах падений он равен 1399, 1170, 571 и 233. В единственной выжившей сессии на 44 минуты - 171. Порог поставлен на 200: между 171 и 233, ближе к нижней границе падений.

Ниже 200 подпись не выставляется намеренно. Сотня-другая сбоев синхронизации бывает у всех, и подпись, срабатывающая на здоровой сессии, хуже отсутствия подписи.

## Что вытаскиваем

Маркер смерти - `[hooks] MINIDUMP`. Всё, что нужно для диагноза, хук печатает следом:

```
[23:40:25.586][hooks] MINIDUMP: Temporary dump saved
[23:40:25.653][hooks] Last loaded asset: tat_rt_029_a_uni_hires.ytd tat_rt_028_a_uni_hires.ytd
[23:40:25.653][hooks] Texture info: givemechecker lowr_diff_005_a_whi
[23:40:25.653][hooks] Last pos Point{ x: -2471.99, y: 2945.79, z: 48.5267 }
```

| Поле | Маркер | Что даёт |
|---|---|---|
| `Time` | `^[чч:мм:сс.доли]` в строке с `MINIDUMP` | точка отсчёта, к ней привязывается остальное в логе |
| `LastAsset` | `Last loaded asset:` | последний ассет, который стриминг успел взять до смерти |
| `TextureInfo` | `Texture info:` | имена текстур; по ним опознаётся конкретный ган из пака |
| `Position` | `Last pos ` | где игрок стоял; отличает «упал при заходе» от «упал на спавне у больницы» |
| `OversizedName` | `Oversized file (>100MB)\s*(\S+)?` | имя записи, которую клиент отказался грузить |

Отдельного «окна вокруг маркера» нет: файл читается одним проходом, поля забираются первым попавшимся вхождением через `??=`.

```cs title="MiamiGraphics.Core/Services/MajesticCrashLog.cs"
lastAsset ??= After(line, "Last loaded asset:");
texInfo   ??= After(line, "Texture info:");
pos       ??= After(line, "Last pos ");
```

Так сделано потому, что эти три строки хук печатает только при смерти - до `MINIDUMP` их в логе нет, и первое вхождение и есть нужное. Буферизовать хвост вокруг маркера незачем, а разбор остаётся однопроходным и не требует держать лог в памяти.

Лавина `Failed to request model` наоборот считается по всему файлу, без окна. Она нарастает за секунды до смерти, и точная граница окна на итог не влияет - на порядок величин между 171 и 1399 сдвиг окна не действует.

### Время читается не везде

Регулярка времени требует долей секунды:

```cs title="MiamiGraphics.Core/Services/MajesticCrashLog.cs"
private static readonly Regex TimeRx = new(@"^\[(\d\d:\d\d:\d\d\.\d+)\]", RegexOptions.Compiled);
```

У части строк клиента штамп короткий - `[17:19:21]`, без долей. Если `MINIDUMP` пришёл именно с таким штампом, `Time` остаётся `null`, и в журнале печатается `краш в ?`. Остальные поля при этом читаются как обычно, подпись не страдает. Правкой регулярки под оба формата не занимались: время здесь - справочная величина, диагноз строится не на нём.

## Когда считаем, что падение было

```cs title="MiamiGraphics.Core/Services/MajesticCrashLog.cs"
if (!minidump && !oversized && !strFailure) return null;
```

`null` означает «в этом логе разбирать нечего» - такой лог в журнал не попадает вообще. Спокойная сессия с парой `Failed to request model` и `Shutting down` в конце ничего не пишет.

Условие намеренно не сводится к одному `MINIDUMP`. `Oversized file (>100MB)` без минидампа - тоже находка: игра выжила, но ассет не загрузился. В следующий раз в том же месте может не повезти, и знать об этом лучше заранее, чем после жалобы.

## Фоновый сканер

Сканер стоит в старте лаунчера, между восстановлением брошенных `.bak` и подключением логгера квоты:

```cs title="MiamiGraphics.Shell/App.xaml.cs"
try { MiamiGraphics.Shell.Services.MajesticCrashWatch.ScanInBackground(); }
catch (Exception cwEx) { System.Diagnostics.Debug.WriteLine($"[startup] crash watch failed: {cwEx.Message}"); }
```

`ScanInBackground` уходит в `Task.Run` и всё внутри ловит: чтение пары мегабайт текста не должно задерживать окно, а сбой диагностики не должен ронять старт. Отказ пишется в журнал одной строкой `WARN [crash-watch]`.

Логи ищутся в `%AppData%\majestic-launcher\Multiplayer\logs`. Папки нет - выходим молча: клиент Majestic у человека не установлен, и это не ошибка.

### Пороги

| Параметр | Значение | Почему так |
|---|---|---|
| `MaxAge` | 7 дней по `LastWriteTime` | разбирать позапрошлую неделю смысла нет: с тех пор и наша установка, и клиент успели измениться |
| `MaxLogs` | 5 файлов за заход | берём последние сессии по `LastWriteTime`, а не весь архив логов |
| `MaxLogBytes` | 32 МБ | потолок чтения одного лога: у зависшей сессии файл бывает огромным |
| размер `majestic_crashes_seen.json` | 50 записей | файл состояния не должен расти без конца |

```cs title="MiamiGraphics.Shell/Services/MajesticCrashWatch.cs"
foreach (var f in fresh.OrderByDescending(x => x.LastWriteTime).Take(MaxLogs))
{
    seen[f.Name] = f.Length;
    if (f.Length > MaxLogBytes) continue;

    MajesticCrashLog.Crash? crash;
    try { crash = MajesticCrashLog.Parse(ReadLines(f.FullName)); }
    catch (Exception ex) { SessionLog.Warn("crash-watch", $"{f.Name}: {ex.Message}"); continue; }
    if (crash == null) continue;

    SessionLog.Warn("crash-watch", $"{f.Name}: {crash.Describe()}");
    SessionLog.Warn("crash-watch", $"{f.Name}: у нас стояло - {installed}");
}
```

Отметка в `seen` ставится **до** проверки размера. Слишком большой лог помечается как обработанный и пропускается; заново он попадёт в разбор только если игра допишет его до другого размера - а с ним и до другой длины.

### Чтение

```cs title="MiamiGraphics.Shell/Services/MajesticCrashWatch.cs"
private static IEnumerable<string> ReadLines(string path)
{
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    while (sr.ReadLine() is { } line) yield return line;
}
```

`FileShare.ReadWrite` обязателен: игрок мог запустить лаунчер, не выходя из игры, и клиент пишет в этот файл прямо сейчас. Без общего доступа сканер получал бы `IOException` ровно на том логе, который интереснее всех - на текущем. Построчная выдача связана с `MaxLogBytes` только косвенно: разбор и так не держит файл в памяти, потолок стоит против времени чтения, а не против памяти.

## Дедупликация

Ключ - имя файла плюс его длина:

```cs title="MiamiGraphics.Shell/Services/MajesticCrashWatch.cs"
foreach (var f in new DirectoryInfo(dir).EnumerateFiles("client_*.log"))
{
    if (DateTime.Now - f.LastWriteTime > MaxAge) continue;
    if (seen.TryGetValue(f.Name, out var len) && len == f.Length) continue;
    fresh.Add(f);
}
```

Пара «имя + размер» выбрана вместо хеша содержимого и вместо одного только имени. Хеш означал бы полное чтение файла ради решения, читать ли файл, - на 32 МБ это вся работа впустую. Одного имени мало: лог активной сессии дописывается, и логи, разобранные при прошлом запуске лаунчера, к следующему запуску успевают обрасти хвостом с падением. Размер меняется вместе с содержимым, и такой лог разбирается заново.

Состояние лежит в `%LocalAppData%\MiamiGraphics\config\majestic_crashes_seen.json` и при записи обрезается до 50 последних:

```cs title="MiamiGraphics.Shell/Services/MajesticCrashWatch.cs"
var trimmed = seen.OrderByDescending(kv => kv.Key, StringComparer.Ordinal).Take(50)
                  .ToDictionary(kv => kv.Key, kv => kv.Value);
File.WriteAllText(StateFile, JsonSerializer.Serialize(trimmed));
```

Сортировка по имени, а не по времени, работает потому, что имя лога начинается с даты (`client_2026-08-21_23-34-39.log`), и порядковое сравнение строк совпадает с хронологическим. Отдельного поля с датой хранить не нужно.

## Что пишется рядом с подписью

Вторая строка журнала - выжимка из `install_state.json`: наш редукс, броня, звуковой пак и включённые кастомизации.

```cs title="MiamiGraphics.Shell/Services/MajesticCrashWatch.cs"
Add("редукс", Str(r, "ReduxId"));
Add("броня", Str(r, "CurrentArmorName") ?? Str(r, "CurrentArmorId"));
Add("звуки", Str(r, "CurrentSoundPackName"));

if (r.TryGetProperty("CustomizationDraft", out var d) && d.ValueKind == JsonValueKind.Object)
{
    foreach (var key in new[] { "Bloodfx", "Crosshair", "Timecycle", "Armor", "Arena" })
        if (d.TryGetProperty(key, out var sub) && sub.ValueKind == JsonValueKind.Object)
            Add(key.ToLowerInvariant(), Str(sub, "Kind"));

    if (d.TryGetProperty("Minimap", out var mm) && Bool(mm, "Enabled")) parts.Add("миникарта");
    // ... Tracers.SourceKind и семь флагов вида ZalazyEnabled / SmokeEnabled
}
```

Состояние читается как дерево `JsonDocument`, а не как типизированная модель. Формат `install_state.json` меняется чаще, чем этот диагностический код, и падать из-за нового или переименованного поля он не должен: значение `default` и пустые строки просто не попадают в выжимку, отсутствующий узел пропускается.

Три отдельных исхода вместо исключения:

| Ситуация | Что в журнале |
|---|---|
| файла нет | `состояние установки не найдено` |
| файл есть, ничего нашего не стоит | `ничего из наших модов` |
| файл не разобрался | `состояние прочитать не вышло: <сообщение>` |

Каждый из трёх - осмысленный ответ при разборе жалобы. «Ничего из наших модов» рядом с подписью краха означает, что искать надо не у нас, и это тоже результат.

Пара строк в журнале выглядит так:

```
2026-08-21 23:51:04.118 +03:00  WARN  [crash-watch]  client_2026-08-21_23-34-39.log: краш в 23:40:25.586 | сборка педов на спавне (лавина Failed to request model) | последний ассет: tat_rt_029_a_uni_hires.ytd | текстуры: givemechecker lowr_diff_005_a_whi | позиция: Point{ x: -2471.99, y: 2945.79, z: 48.5267 } | Failed to request model: 303
2026-08-21 23:51:04.119 +03:00  WARN  [crash-watch]  client_2026-08-21_23-34-39.log: у нас стояло - редукс=..., броня=..., трейсеры=..., миникарта
```

Обе строки идут уровнем `WARN` и с тегом `crash-watch` - по тегу они выбираются из журнала одним grep. Сам `miami-launcher.log` ротируется по 3 МБ с тремя копиями, так что разбор недельной давности в файле может уже не лежать; свежая пара строк появляется при следующем старте лаунчера, если лог падения ещё не старше 7 дней.

## Границы

- Сам минидамп мы не читаем и не отправляем: работаем только с текстовым логом клиента.
- Строку `CObjectSyncData pool (...) is full - limit is 80 elements`, которая соседствует с лавиной сбоев синхронизации, разбор не учитывает. В подпись она не входит: одного счётчика достаточно, а формат этой строки менялся вместе с клиентом.
- Ничего наружу не уходит. Разбор пишется в локальный журнал, и решает игрок - прислать его или нет.
- Тесты (`MajesticCrashLogTests`) прогоняются на строках, взятых дословно из логов игроков. На синтетике проверять этот разбор бессмысленно: формат чужой и меняется вместе с клиентом Majestic.

[Дальше: AppData layout →](../paths/appdata-layout.md)
