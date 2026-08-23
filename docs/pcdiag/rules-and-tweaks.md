# Движок находок и твиков

Диагностика ПК состоит из трёх слоёв: сборщик снимает состояние машины (`PcSnapshot`), движок правил превращает снимок в список находок (`PcDiagRules.Evaluate`), применятель чинит то, что мы умеем чинить (`PcDiagApplier`). Между слоями нет текста - правила выдают стабильные идентификаторы и словарь параметров, формулировки живут в i18n интерфейса.

Такое разделение появилось не сразу. Первая версия возвращала готовые русские строки, и на второй же неделе выяснилось, что одну и ту же находку надо показать карточкой в UI, отдать в отчёт и сопоставить с записью в журнале отката. Строка для этого не годится: она меняется при каждой правке текста, её нельзя использовать ключом. Сейчас движок не знает ни одного человекочитаемого слова.

Второй принцип - молчать, когда не уверены. Нераспознанное имя процессора даёт `cpu-unrecognized`, а не догадку; список исключений Defender, недоступный без прав, даёт `null`, а не находку «папки нет в исключениях». Ошибка «оценили ниже, чем есть» стоит дешевле, чем обвинение исправной машины.

## Находка: идентификатор, данные, вилка

```cs title="MiamiGraphics.Core/PcDiag/PcDiagModels.cs"
public sealed record DiagFinding(
    string Id,
    DiagSeverity Severity,
    DiagCategory Category,
    IReadOnlyDictionary<string, string> Data,
    int? GainMinPercent = null,
    int? GainMaxPercent = null,
    bool AutoFixable = false
);
```

`Id` стабилен навсегда: по нему находится текст в i18n (`pcdiag.f.*`), по нему же `PcDiagApplier` ищет пару «применить/откатить», и он же ложится в `journal.json` как ключ записи. Переименование идентификатора ломает откат уже применённых твиков, поэтому идентификаторы мы не переименовываем.

`Data` - параметры для подстановки: `("rated", "3200")`, `("actual", "2133")`, `("family", "Intel 12 поколение")`. UI подставляет их в шаблон, отчёт печатает как есть.

Сортировка на выходе одна и та же всегда:

```cs title="MiamiGraphics.Core/PcDiag/PcDiagRules.cs"
return findings
    .OrderByDescending(f => f.Severity)
    .ThenBy(f => f.Category)
    .ToList();
```

Внутри одной тяжести `DiagCategory` идёт в порядке объявления: `Hardware`, `Windows`, `Apps`, `Driver`, `Game`. Железо раньше твиков - там лежат самые большие проценты, и человек должен увидеть их до того, как час крутит настройки Windows ради трёх процентов.

## Четыре класса тяжести

| Класс | Что означает | Примеры находок |
|---|---|---|
| `Info` | факт для отчёта, действий не требует | `cpu-tier-s`, `hags-state`, `wifi-only`, `vpn-active`, `autostart-crowded` |
| `Minor` | единицы процентов или качество жизни | `gamedvr-on`, `vram-low`, `sysmain-off`, `visualfx-full`, `av-third-party` |
| `Major` | двузначный потенциал прироста или источник фризов | `ram-xmp-off`, `ram-single-channel`, `power-saver`, `bcd-useplatformclock`, `prefetch-off` |
| `Critical` | играть с этим в RP почти нельзя | `ram-critical`, `vram-critical`, `game-on-hdd`, `disk-hdd-present` без SSD в системе |

Пороги, на которых класс переключается:

| Правило | Порог | Класс |
|---|---|---|
| `ram-critical` | меньше 12 ГБ | `Critical` |
| `ram-low` | 12-15 ГБ | `Major` |
| `vram-critical` | меньше 4 ГБ | `Critical` |
| `vram-low` | 4-5 ГБ | `Minor` |
| `gpu-driver-old` | драйвер старше 548 дней | `Major` |
| `gpu-driver-aging` | драйвер старше 365 дней | `Minor` |
| `display-not-max-hz` | `MaxHz >= CurrentHz + 20` | `Major` |
| `bg-browser` | суммарный WorkingSet браузеров от 1,5 ГБ | `Major` при 16 ГБ ОЗУ и меньше, иначе `Info` |
| `bg-widgets` | процесс виджетов от 0,3 ГБ | `Info` |
| `autostart-crowded` | 8 и больше строк в Run-ключах | `Info` |

Класс не всегда константа. `power-balanced` на ноутбуке `Major`, на десктопе `Minor` - схема «Сбалансированная» душит частоты по-разному в зависимости от того, есть ли батарея. `disk-hdd-present` становится `Critical` только когда SSD в системе нет вовсе: иначе это `Minor`, потому что игру можно перенести.

## Вилки выигрыша

`GainMinPercent`/`GainMaxPercent` заполнены не у всех правил, и это осознанно. `null` означает одно из двух: эффект не измеряется в кадрах (пинг, время загрузки, фризы) либо честной вилки у нас нет и её даст только замер.

| Правило | Вилка | На чём основана |
|---|---|---|
| `ram-single-channel` | 10-30% | половина полосы памяти на CPU-bound сцене |
| `ram-xmp-off` | 10-25% | разница между JEDEC и паспортной частотой планки |
| `power-saver` | 5-30% | схема удерживает частоты процессора внизу |
| `power-balanced` | 2-15% на ноутбуке, 2-5% на десктопе | зависит от `HasBattery` |
| `vbs-running` | 3-8% | цена аппаратной виртуализации в играх |
| `gamedvr-on` | 1-5% | фоновая запись Game Bar |

Всё остальное вилки не получает. `cpu-tier-d` процентов не обещает вообще: там ответ не в процентах, а «апгрейд процессора даст больше, чем все твики вместе».

`vbs-running` - единственная находка с вилкой и `AutoFixable: false`. Это защита, а не мусор: мы показываем цену и оставляем решение человеку. Автоматически выключать VBS не будем никогда.

## Тир процессора: S..D

GTA V в RP упирается в одно ядро и в L3-кэш, поэтому тир определяется поколением, сегментом и наличием 3D V-Cache, а не числом ядер. Матрица сознательно грубая: ошибка на полтира допустима, ошибка «S вместо D» - нет.

```cs title="MiamiGraphics.Core/PcDiag/PcDiagCpuRating.cs"
CpuTier tier = series switch
{
    >= 9 => CpuTier.A,                            // Zen 5
    7 or 8 => series == 7 ? CpuTier.A : CpuTier.B, // Zen 4 / APU 8000
    5 or 6 => CpuTier.B,                          // Zen 3 (6000 - ноутбучный Zen3+)
    3 or 4 => CpuTier.C,                          // Zen 2
    _ => CpuTier.D                                // Zen/Zen+
};
if (laptop) tier = Down(tier, sfx == "U" ? 2 : 1);
```

| Семейство | Признак | Тир |
|---|---|---|
| Ryzen X3D | серия 7000 и новее | S |
| Ryzen X3D | серия 5000 (5800X3D) | A |
| Ryzen без X3D | 9000 / 7000 | A |
| Ryzen без X3D | 8000 (APU) / 6000 / 5000 | B |
| Ryzen без X3D | 4000 / 3000 | C |
| Ryzen без X3D | 2000 и старше | D |
| Core Ultra | десктоп, модель 200+ | A |
| Core Ultra | мобильный или серия 100 | B |
| Core i7/i9 | 12 поколение и новее | A |
| Core i3/i5 | 12 поколение и новее | B |
| Core i7/i9 | 10-11 поколение | B |
| Core i3/i5 | 10-11 поколение | C |
| Core i7/i9 | 8-9 поколение | C |
| Core i3/i5 | 8-9 поколение | D |
| Core любой | 7 поколение и старше | D |
| FX, Phenom, Athlon, Pentium, Celeron, Core 2, A4-A12 | по имени семейства | D |
| Xeon, Snapdragon, прочее | имя не разобрано | `Unknown` |

Мобильные суффиксы сдвигают тир вниз: `H`, `HS`, `HX`, `C` - на одну ступень, `U` - на две. Ниже D не опускаемся.

Поколение Intel вытаскивается из длины номера модели: пять цифр - первые две (12700 → 12), четыре цифры с ведущей единицей - тоже первые две (1165G7 → 11, 1355U → 13), остальные четырёхзначные - первая цифра (9700 → 9), трёхзначные - первое поколение. Четырёхзначных настольных моделей на «1» не существует, поэтому правило однозначно.

В находки попадают не все тиры: S даёт `cpu-tier-s` (`Info`), C - `cpu-tier-c` (`Minor`), D - `cpu-tier-d` (`Major`). A и B находки не порождают - сказать про них нечего. Отдельно от тира ставится `cpu-hybrid` для Intel 12 поколения и новее и Core Ultra: это не совет «отключи E-ядра», а пометка, что машина - кандидат на эксперимент с CPU set и замером.

## Тир памяти

Порядок важности здесь другой, чем принято в народных гайдах: сначала каналы, потом объём, и только потом профиль.

```cs title="MiamiGraphics.Core/PcDiag/PcDiagPartRatings.cs"
if (gb < 12)
    return new PartRating(PartTier.D, $"{gb} ГБ - главный лимитер, апгрейд даст больше любых твиков");

if (channels < 2)
    return new PartRating(PartTier.C,
        "одна планка: одноканал стоит кадров больше, чем любая частота - вторая планка того же объёма всё чинит");

if (!profileOn)
    return new PartRating(gb >= 24 ? PartTier.A : PartTier.B, note + ", профиль XMP/EXPO не включён");

return new PartRating(gb >= 24 ? PartTier.S : PartTier.A, note + ", профиль включён");
```

| Условие | Тир |
|---|---|
| меньше 12 ГБ | D |
| один канал занят | C |
| двухканал, 16-23 ГБ, профиль выключен | B |
| двухканал, 24 ГБ и больше, профиль выключен | A |
| двухканал, 16-23 ГБ, профиль включён | A |
| двухканал, 24 ГБ и больше, профиль включён | S |

Порог «профиль включён» - `ConfiguredMt >= RatedMt - 132`. Сто тридцать два мегатранзакта отсекают округления SPD: планка 3200 регулярно рапортует 3199 или 3193 даже при поднятом XMP, и без допуска правило `ram-xmp-off` срабатывало бы на исправных машинах.

Каналы считаются по букве слота (`A2` → A), при её отсутствии - по строке банка (`P0 CHANNEL B` → B), а если и там формата нет - по числу планок. Правило `ram-single-channel` осторожнее рейтинга: оно требует либо ровно одну планку, либо чтобы у всех планок совпала связка «контроллер+канал», разобранная регуляркой `(?:(Controller\d+)[-_ ]*)?(Channel\s*[A-Z0-9])`. Формат не распознан - молчим.

Отдельная ветка `ram-jedec-speed` (`Minor`) ловит случай, когда паспорт и факт совпадают, но частота стоит на стартовом JEDEC своего поколения: DDR4 (SMBIOS-тип 26) до 2666 МТ/с включительно, DDR5 (тип 34) ниже 4800. Уверенности «профиль выключен» тут нет - WMI видит XMP-паспорт не всегда, он лежит в SPD, - поэтому это подсказка, а не диагноз.

## Тир диска

Шкала про загрузку игры, а не про место в бенчмарках. GTA V читает сотни тысяч мелких ресурсов и распаковывает их процессором: разница между дешёвым и дорогим NVMe в заходе почти не видна, разница между SSD и HDD - видна очень.

| Носитель игры | Тир | Подпись |
|---|---|---|
| NVMe | S | быстрее для GTA уже некуда |
| SATA SSD | A | разница с NVMe в загрузке почти не видна |
| SSD, шина не определена | A | игра на SSD |
| внешний диск по USB | B | упирается в USB, а не в сам SSD |
| HDD | D | самая дорогая потеря времени при заходе |

Тир считается для диска, на котором лежит игра, а не для самого быстрого в системе: держать Windows на NVMe, а GTA на HDD - обычное дело, и показать надо именно это. Шину раздела WMI не отдаёт, поэтому её берём с диска только тогда, когда носитель игры совпал ровно с одним диском либо все диски того же носителя сидят на одной шине. Иначе шина `Unknown`, и подпись честно говорит «игра на SSD» без уточнения.

Правило `disk-hdd-present` выключается целиком, если носитель игры распознан: там уже отработало точное `game-on-hdd`, и общая находка «в системе есть HDD» только дублировала бы его.

## Применение: точка восстановления и журнал

Три правила, на которых держится применятель:

1. Перед первым изменением - точка восстановления Windows.
2. Каждое применение пишет в журнал **прежнее** состояние, а не наше представление о дефолте.
3. Применяется только то, что помечено `AutoFixable`. Ничего в сторону отключения следов здесь нет.

```cs title="MiamiGraphics.Core/PcDiag/PcDiagApplier.cs"
var journal = Load();

string restoreNote = "";
if (!journal.RestorePointCreated)
{
    var rp = TryCreateRestorePoint();
    restoreNote = rp.Ok ? " Точка восстановления создана." : $" Точка восстановления: {rp.Message}";
    journal = journal with { RestorePointCreated = true };
}

Dictionary<string, string?> prev;
try { prev = t.Capture(); }
catch (Exception ex) { return new TweakResult(false, $"Не удалось снять прежнее состояние: {ex.Message}", false); }
```

Точка создаётся через WMI-класс `SystemRestore` в области `\\.\root\default`, тип `12` (MODIFY_SETTINGS), событие `100` (BEGIN_SYSTEM_CHANGE). Windows троттлит точки - одна в сутки без правки реестра, - поэтому отказ мы не считаем фатальным: пишем причину в сообщение и продолжаем. Каждый твик и без точки обратим через журнал.

Журнал - `%LocalAppData%\MiamiGraphics\pcdiag\journal.json`, флаг `RestorePointCreated` плюс список записей:

```cs title="MiamiGraphics.Core/PcDiag/PcDiagApplier.cs"
public sealed record JournalEntry(
    string Id,
    DateTime AppliedAtUtc,
    Dictionary<string, string?> Previous,
    bool Reverted,
    DateTime? RevertedAtUtc);
```

Откат берёт **последнюю незаоткаченную** запись по идентификатору; предыдущие остаются в файле как история. Битый журнал не блокирует работу - `Load()` глотает исключение и возвращает пустой.

Ключевая деталь `Previous`: значение `null` там означает «ключа реестра не было», и откат его удаляет, а не пишет ноль:

```cs title="MiamiGraphics.Core/PcDiag/PcDiagApplier.cs"
private static void WriteRegDword(RegistryKey root, string path, string name, int? value)
{
    using var k = root.CreateSubKey(path)!;
    if (value is null) { try { k.DeleteValue(name); } catch { } }
    else k.SetValue(name, value.Value, RegistryValueKind.DWord);
}
```

Без этого откат оставлял бы после себя ключи, которых на машине изначально не было, и следующая диагностика видела бы их как чужие твики.

Внешние утилиты (`powercfg`, `sc.exe`, `bcdedit`) запускаются через `RunTool` с таймаутом 15 000 мс, вывод читается в OEM-кодировке текущей культуры - иначе русские сообщения `powercfg` приходят мусором. Код `1056` от `sc start` («служба уже запущена») считается успехом.

Три твика меняют состояние без реестра и требуют перезагрузки: `pagefile-off` (WMI `AutomaticManagedPagefile`), `bcd-useplatformclock` (`bcdedit /deletevalue useplatformclock`), `hags-on` и `widgets-off` (значения в `HKLM`, подхватываются драйвером и оболочкой только после рестарта).

## Проактивные твики

Находки описывают поломки. Каталог `PcDiagTweakCatalog` описывает то, что можно применить на любой машине независимо от находок. У каждого пункта честная пометка веса.

| Пометка | Что значит |
|---|---|
| `works` | подтверждённый эффект, единицы процентов и выше |
| `micro` | меньше процента, но безопасно и стало стандартом де-факто |
| `experiment` | эффект спорный, проверяется замером |
| `device` | не FPS, а отклик устройств и прицела |
| `maintenance` | обслуживание без отката |

| Идентификатор | Вес | В «применить всё безопасное» | Что делает |
|---|---|---|---|
| `nvidia-profile` | works | нет | профиль драйвера для `gta5.exe` |
| `commandline-clean` | works | да, если есть что чистить | убирает плацебо-флаги |
| `mmcss-games` | micro | да | `GPU Priority` 8, `Priority` 6, категория `High`, `SFIO Priority` `High` |
| `system-responsiveness` | micro | да | резерв CPU под фон 20% → 10% |
| `gamebar-nexus-off` | micro | да | кнопка Xbox не открывает оверлей |
| `power-throttling-gta` | micro | да | `powercfg /powerthrottling disable` для `GTA5.exe` |
| `widgets-off` | micro | да | `AllowNewsAndInterests = 0`, нужен рестарт |
| `background-apps-off` | micro | да | `GlobalUserDisabled = 1` |
| `stickykeys-off` | device | да | флаги `506` / `58` / `122` - системные окна не выскочат в бою |
| `mouse-accel-off` | device | нет | `MouseSpeed` и оба порога в `0` |
| `w32-priority-separation` | experiment | нет | `Win32PrioritySeparation = 0x26`, откат в `2` |
| `network-throttling-off` | experiment | нет | `NetworkThrottlingIndex = 0xFFFFFFFF` |
| `hags-on` | experiment | нет | `HwSchMode = 2`, только сборка 19041 и выше |
| `fso-off-gta` | experiment | нет | флаг `DISABLEDXMAXIMIZEDWINDOWEDMODE` в `AppCompatFlags\Layers` |
| `shader-cache-clean` | maintenance | нет | чистит кэши NVIDIA/AMD/DirectX |
| `temp-clean` | maintenance | нет | удаляет временные файлы старше 7 дней |

Ни один `experiment` и ни один `maintenance` в пакетное применение не входит. `widgets-off` показывается только на сборках 22000 и выше, `hags-on` - только на 19041 и выше, иначе состояние `NotApplicable`.

Каталог не хранит собственного состояния - он каждый раз читает систему. `Done` означает «система уже в целевом состоянии», неважно, привели её туда мы или человек руками. `shader-cache-clean` считается `Ready` только при кэше больше 64 МБ, `temp-clean` - при 256 МБ мусора старше недели: чистить меньшее незачем.

Отдельная деталь в `fso-off-gta`: значение `Layers` может уже нести чужие флаги (`RUNASADMIN` и подобные), поэтому свой мы **дописываем**, а не затираем строку. Прежняя строка целиком уходит в журнал.

`visualfx-full` при применении ставит `VisualFXSetting = 2`, но следом отдельно возвращает `FontSmoothing = "2"`. Режим «производительность» гасит сглаживание шрифтов, и без этой поправки после твика текст в Windows становится рваным - жалоба, которую мы получили раньше, чем успели описать сам твик.

## Профиль NVIDIA: прямые вызовы nvapi64.dll

Профиль драйвера для `gta5.exe` ставится через NVAPI DRS напрямую из процесса лаунчера. Ни `nvidiaProfileInspector.exe`, ни любых других сторонних бинарников мы не тянем: лишний исполняемый файл в поставке - это и вопрос от антивируса, и вещь, которую надо обновлять.

`nvapi64.dll` не экспортирует функции по именам. Есть одна точка входа `nvapi_QueryInterface(uint id)`, которая по числовому идентификатору отдаёт указатель, а дальше он маршалится в делегат:

```cs title="MiamiGraphics.Core/PcDiag/PcDiagNvidia.cs"
[DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
private static extern IntPtr QueryInterface(uint id);

private static T Fn<T>(uint id) where T : Delegate
{
    var p = QueryInterface(id);
    if (p == IntPtr.Zero) throw new InvalidOperationException($"NVAPI: функция 0x{id:X8} недоступна");
    return Marshal.GetDelegateForFunctionPointer<T>(p);
}
```

Наличие NVIDIA определяется тем же вызовом: `QueryInterface(0x0150E828)` (это `NvAPI_Initialize`) возвращает ноль на машинах без их драйвера, и твик уходит в `NotApplicable`.

Ставим три настройки, идентификаторы взяты из `NvApiDriverSettings.h`:

| Настройка | ID | Значение | Зачем |
|---|---|---|---|
| `PREFERRED_PSTATE` | `0x1057EB71` | `1` (PREFER_MAX) | карта не сбрасывает частоты в CPU-bound сценах - главная беда RP: GPU недогружен и засыпает |
| `PRERENDERLIMIT` | `0x007BA09E` | `1` | короткая очередь кадров, ниже задержка ввода |
| `SHADERDISKCACHE_MAX_SIZE` | `0x00AC8497` | `10485760` КБ (10 ГБ) | кэш шейдеров не вытесняется, меньше перекомпиляций - меньше статтера |

«Threaded optimization» из панели NVIDIA сознательно не трогаем. Это настройка OpenGL (`OGL_THREAD_CONTROL`), к DX11-игре она отношения не имеет - классический карго-культ гайдов, который перекочевал в половину чужих оптимизаторов.

Защита от порчи профилей встроена в сам протокол NVAPI: у каждой структуры первое поле `Version`, и в него пишется размер, посчитанный на нашей стороне:

```cs title="MiamiGraphics.Core/PcDiag/PcDiagNvidia.cs"
public static NvdrsSetting Create() => new()
{
    Version = (uint)Marshal.SizeOf<NvdrsSetting>() | (1u << 16),
    SettingName = "",
};
```

Если layout наших структур разойдётся с драйвером, NVAPI вернёт ошибку версии, а не запишет мусор в чужой профиль.

Схема отката двойная, потому что стартовых ситуаций две:

- `gta5.exe` уже привязан к какому-то профилю (обычно предустановленному «Grand Theft Auto V») - правим его, а прежние значения трёх настроек кладём в журнал под ключами `v0`, `v1`, `v2` вместе с флагом `created = 0`. Пустая строка означает «настройки в профиле не было», и откат удаляет её через `NvAPI_DRS_DeleteProfileSetting`, а не пишет туда выдуманный дефолт.
- Привязки нет - создаём собственный профиль «Miami Graphics - GTA V», `created = 1`, и откат просто удаляет его целиком.

Твик единственный в каталоге, где `Capture` пустой: NVAPI сам разбирается со сценарием внутри `Apply` и возвращает данные для отката, которые применятель дописывает в запись журнала.

## Чего мы не выключаем: Prefetch и журнал событий

Народные оптимизаторы почти поголовно гасят Prefetch/SysMain и службу `EventLog`. Мы не только этого не делаем - у нас есть правила, которые ищут такое как **чужой след** и предлагают включить обратно.

```cs title="MiamiGraphics.Core/PcDiag/PcDiagRules.cs"
private static void EvalCompetitiveTraces(PcSnapshot s, List<DiagFinding> f)
{
    if (s.Prefetch is { EnablePrefetcher: 0 })
        f.Add(new DiagFinding("prefetch-off", DiagSeverity.Major, DiagCategory.Windows, D(), AutoFixable: true));

    var eventLog = s.Services.FirstOrDefault(x => x.Name.Equals("EventLog", StringComparison.OrdinalIgnoreCase));
    if (eventLog is { Exists: true } &&
        (!eventLog.Running || eventLog.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)))
        f.Add(new DiagFinding("eventlog-off", DiagSeverity.Major, DiagCategory.Windows, D(), AutoFixable: true));
}
```

Причина не в производительности. На SSD выигрыш от отключённого Prefetch нулевой, от выключенного журнала событий - тоже. Причина в том, что папка `Prefetch` и журналы Windows - это список того, что на машине запускалось, и админ-проверки RP-серверов читают их именно так. Пустой Prefetch и остановленный `EventLog` на проверке ПК выглядят как зачистка следов после читов и стоят бана. Плюс без журналов невозможно разбирать вылеты игры.

Поэтому оба твика в `PcDiagApplier` заведены **только на включение обратно**: `EnablePrefetcher` возвращается в штатное `3`, служба через `sc config EventLog start= auto` плюс `sc start`. Выключателя в эту сторону в коде нет и не будет. Тот же принцип у `SysMain` и `WSearch` - `RestoreService` умеет только `start= auto`, обратное направление доступно исключительно как откат по журналу, то есть возврат ровно того состояния, которое было до нашего вмешательства.

## Плацебо-флаги в commandline.txt

```cs title="MiamiGraphics.Core/PcDiag/PcDiagTweakCatalog.cs"
internal static readonly string[] PlaceboFlags = { "-high", "-norestrictions", "-nomemrestrict", "-veryhigh" };
```

Этих параметров у GTA V не существует. Они кочуют по гайдам с 2015 года, парсер игры их игнорирует, а файл они замусоривают - и заодно создают у человека уверенность, что «оптимизация уже сделана». Чистка вырезает строки, содержащие любой из четырёх флагов, целиком:

```cs title="MiamiGraphics.Core/PcDiag/PcDiagApplier.cs"
var lines = content.Split('\n')
    .Where(line => !PcDiagTweakCatalog.PlaceboFlags.Any(f =>
        line.Contains(f, StringComparison.OrdinalIgnoreCase)))
    .ToArray();
File.WriteAllText(p, string.Join("\n", lines));
```

Прежнее содержимое файла целиком лежит в журнале под ключом `content`, откат пишет его назад байт в байт. Состояния каталога: файла нет - `Done` (мусора тоже нет), файл есть и флагов в нём нет - `Done`, флаги найдены - `Ready`, а их список уходит в `Data["flags"]`, чтобы UI показал, что именно будет удалено. Путь до игры неизвестен - `NotApplicable`.
