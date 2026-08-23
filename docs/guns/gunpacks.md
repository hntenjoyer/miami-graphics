# Ганпаки: разбор донора и сборка своего rpf

Ганпак приезжает одним файлом - `dlc.rpf` чужого DLC-пака, внутри которого лежат модели и текстуры оружия. Игроку такой файл бесполезен: он хочет карабин из одного пака, снайперку из другого, а остальное оставить ванилью. Значит донора надо разложить на отдельные стволы, а потом собрать обратно свой архив ровно из выбранного.

Разбор делает `GunpackParserService` - один раз на пак, до попадания в каталог. Сборка и установка клиентские: `SelectedGunsInstaller` собирает rpf из выбранных стволов, `TargetDlcEditor` кладёт его в слот `update\x64\dlcpacks\patchday18ng\dlc.rpf` и прописывает в `content.xml`.

Про то, какие конфликты вообще возникают между паками, есть [отдельная страница](../history/guns-conflicts.md). Здесь - механика: как из плоского списка файлов получается каталог стволов и как этот каталог возвращается в игру.

## Какой rpf внутри донора считать оружейным

Внутри `dlc.rpf` может лежать сколько угодно вложенных архивов, и имя у оружейного не фиксировано - авторы называют его `weapons.rpf`, `w_pack.rpf`, как угодно. Мы обходим дерево рекурсивно, отсеиваем шесть заведомо не-оружейных имён и выбираем кандидата **по содержимому**: суммируем размеры файлов, чьё имя начинается на один из девяти оружейных маркеров (`w_ar_`, `w_sg_`, `w_sr_`, `w_mg_`, `w_pi_`, `w_me_`, `w_lr_`, `w_at_`, `w_cr_`). Побеждает наибольшая сумма.

```cs title="MiamiGraphics.Core/Services/GunpackParserService.cs"
private static readonly string[] NonWeaponRpfNames =
{
    "vehicles.rpf", "props.rpf", "scenes.rpf",
    "clip_anim@.rpf", "clip_veh@.rpf", "clip_main@.rpf"
};

foreach (var (fullPath, candidate) in filteredByName)
{
    long score = ScoreWeaponsRpf(candidate);   // сумма размеров записей w_*
    if (score <= 0)                            // внутри нет ни одного w_* - не наш
    {
        Console.WriteLine($"[GunpackParser] Пропуск {fullPath}: внутри нет файлов w_*");
        continue;
    }
    if (score > bestScore) { bestScore = score; gunsRpfFile = candidate; }
}
```

Раньше детекция была по первым 20 именам (`LooksLikeWeaponsRpf`): метод жив, но в выборе не участвует. Проблема выборки в том, что у пака с одним стволом и пака с сорока первые 20 имён выглядят одинаково, а класть в каталог надо тот архив, где оружия больше.

## Префиксы: что это за ствол

Имя модели в GTA начинается с двухбуквенного кода категории. Из него мы получаем и категорию, и человекочитаемое имя карточки.

| Префикс | Категория | 
|---|---|
| `w_ar_` | Assault Rifle |
| `w_sg_` | Shotgun |
| `w_sr_` | Sniper Rifle |
| `w_mg_` | Machine Gun |
| `w_pi_` | Pistol |
| `w_me_` | Melee |
| `w_lr_` | Launcher |

Файл, не начинающийся ни с одного из семи, попадает в `UnrecognizedFiles` и в каталог не едет. В консоль печатаются первые 10 таких имён - этого хватает, чтобы понять, пак ли кривой или мы не знаем какой-то префикс.

## Инфиксы: что это обвес, а не ствол

Обвесы (прицелы, глушители, магазины, рукоятки) - отдельные модели, и в каталоге стволов им делать нечего: игрок выбирает ствол, а обвесы к нему приезжают вместе с ним. Двух префиксов `w_at_` / `w_cr_` для отсева мало: часть обвесов авторы называют от имени ствола, и такой файл проходит проверку префикса как обычная модель. Поэтому вторая проверка - по инфиксу в любом месте имени.

```cs title="MiamiGraphics.Core/Services/GunpackParserService.cs"
private static readonly string[] AttachmentPrefixes = { "w_at_", "w_cr_" };

private static readonly string[] AttachmentInfixes =
{
    "_barrel_",
    "_scope_",  "_scope.",
    "_supp_",   "_supp.",
    "_afgrip_", "_afgrip.",
    "_sights_", "_sights.",
    "_grip_",   "_grip.",
    "_flash_",
    "_muzzle_"
};
```

Пять из тринадцати инфиксов задублированы в двух написаниях - с подчёркиванием и с точкой. Причина в позиции: `w_ar_carbinerifle_scope_01.ydr` ловится по `_scope_`, а `w_ar_carbinerifle_scope.ydr` - только по `_scope.`, потому что дальше идёт расширение, а не следующий сегмент имени. Без второго варианта каждый такой обвес уезжал бы в каталог отдельной карточкой.

Всё отсеянное считается в `AttachmentFilesSkipped` - число видно в итоговой сводке парсера.

## Как файлы собираются в один ствол

Один ствол - это 2-8 файлов: базовая модель, вторая модель, текстуры, магазины. Ключ группировки - префикс плюс базовое имя, из которого сняты расширения и суффиксы.

```cs title="MiamiGraphics.Core/Services/GunpackParserService.cs"
private static readonly string[] SuffixesToStrip =
    { "_hi", "_mag1", "_mag2", "_mag3", "_sight", "_sight_hi" };

string nameNoPrefix = fileName.Substring(gunPrefix.Length);
string nameNoExt    = StripAllGameExtensions(nameNoPrefix);   // .ydr .ytd .yft .ydd
string baseName     = StripGroupingSuffixes(nameNoExt);       // цикл, пока что-то снимается

string categoryKey  = gunPrefix + baseName;
```

Обе чистки идут циклом `do/while`, а не одним проходом: имена вида `..._sight_hi.ydr` требуют двух снятий суффикса, а двойные расширения - двух снятий расширения. Один проход оставлял бы хвост, и файлы одного ствола расходились по двум категориям.

Категория без модели выбрасывается целиком:

```cs title="MiamiGraphics.Core/Services/GunpackParserService.cs"
bool hasMainModel = files.Any(f =>
    f.FullName.Equals($"{prefix}{baseName}.ydr",     StringComparison.OrdinalIgnoreCase) ||
    f.FullName.Equals($"{prefix}{baseName}_hi.ydr",  StringComparison.OrdinalIgnoreCase));

if (!hasMainModel)
{
    // только текстуры/магазины без модели - это остатки обвеса, а не ствол
    info.AttachmentFilesSkipped += files.Count;
    continue;
}
```

На выходе получается папка `Gunpacks\<имя>_<yyyyMMdd_HHmmss>` с:

| Что | Содержимое |
|---|---|
| `guns/<base>.zip` | все файлы одного ствола как есть |
| `gltf/<base>.zip` | glTF-превью, сконвертированное из `.ydr` (base, иначе `_hi`) |
| `gunpack_info.json` | категории, префиксы, списки файлов, счётчики |
| `<weapons>.rpf` | извлечённый внутренний архив донора |
| `dlc.rpf` | кеш-копия исходного донора |
| `_debug_parse.txt` | по строке на запись: тип, размер, флаги, первые 16 байт |

## Слот patchday18ng и две записи в content.xml

Свои rpf мы кладём не в `update.rpf`, а в отдельный DLC-слот: `update\x64\dlcpacks\patchday18ng\dlc.rpf`, внутрь по пути `x64/levels/gta5/`. Файла в архиве мало - без регистрации в `content.xml` игра его не смонтирует. Регистраций ровно две, и они разные.

```cs title="MiamiGraphics.Shell/Services/TargetDlcEditor.cs"
private static readonly string[] WeaponsRpfParentParts = { "x64", "levels", "gta5" };
private const string ContentXmlDevicePrefix         = "dlc_PATCHDAY18NG";
private const string ContentXmlChangeSet            = "CCS_PATCHDAY18_NG_STREAMING";
private const string ContentXmlFilesToEnableDevice  = "dlc_PATCHDAY18ng";

string filenameValue      = $"{ContentXmlDevicePrefix}:/%PLATFORM%/levels/gta5/{rpfName}";
string filesToEnableValue = $"{ContentXmlFilesToEnableDevice}:/%PLATFORM%/levels/gta5/{rpfName}";

dataFilesRoot.Add(new XElement("Item",
    new XElement("filename", filenameValue),
    new XElement("fileType", "RPF_FILE"),
    new XElement("locked",     new XAttribute("value", "true")),
    new XElement("disabled",   new XAttribute("value", "true")),
    new XElement("persistent", new XAttribute("value", "true")),
    new XElement("overlay",    new XAttribute("value", "true"))));
// ...
filesToEnable.Add(new XElement("Item", filesToEnableValue));
```

`<dataFiles>` объявляет файл, `<filesToEnable>` внутри changeSet `CCS_PATCHDAY18_NG_STREAMING` его включает. Имя устройства в двух записях пишется в разном регистре - так в ванильном `content.xml`, и мы повторяем как есть; сравнение при поиске дублей идёт `OrdinalIgnoreCase`, так что повторный вызов ничего не задваивает. Обе вставки идемпотентны, метод возвращает `true` только если что-то реально изменилось.

## Пересборка из шаблона вместо правки на месте

`RebuildFromTemplate` никогда не редактирует текущий `dlc.rpf` игрока. Он копирует чистый шаблон (`<Result>\backup\dlc.rpf`, скачивается один раз и проверяется по SHA-256), вкладывает детей, регистрирует их и атомарно подменяет цель.

```cs title="MiamiGraphics.Shell/Services/TargetDlcEditor.cs"
var tempPath = targetDlcPath + ".building";
File.Copy(templatePath, tempPath, overwrite: true);
var canonicalName = Path.GetFileName(targetDlcPath);

foreach (var child in payload)
{
    InjectRpfIntoTarget(tempPath, child.RpfName, child.Bytes!, canonicalName);
    EnsureContentXmlEntry(tempPath, child.RpfName, canonicalName);
}

// снимок прежнего рабочего DLC: rename, не копия - файл сотни МБ
rollbackPath = targetDlcPath + ".rollback";
File.Move(targetDlcPath, rollbackPath);
File.Move(tempPath, targetDlcPath, overwrite: true);

try   { FixTargetDlcArchive(targetDlcPath, canonicalName); }
catch { File.Move(rollbackPath, targetDlcPath, overwrite: true); throw; }
```

Стартовать всегда от шаблона обязательно: слот собирается с нуля, поэтому остатки прошлой установки физически не могут просочиться. Обратная сторона - любой чужой дочерний rpf в этом слоте тоже исчезнет, если его не передали в список детей. Именно так один раз пропадали пропатченные билборды: сигнатура принимала ровно двух детей, третий было некуда положить.

`ArchiveFix` (OPEN → NG) гоняется **после** подмены, на файле с финальным именем `dlc.rpf` - ключ NG выводится из имени, и починка `.building` дала бы архив, зашифрованный не тем ключом. Отсюда и `.rollback`: до этого фикса падение `ArchiveFix` оставляло игрока без рабочего DLC и без пути назад. Подробности пересчёта - [ArchiveFix.exe](../rpf/archivefix.md).

## Почему поштучный rpf ставится ПОСЛЕ основного

Движок применяет `filesToEnable` по порядку списка, последний побеждает. Порядок элементов в `children` - это и есть порядок регистрации.

```cs title="MiamiGraphics.Shell/Services/TargetDlcEditor.cs"
public const string MIAMI_WEAPON_RPF_NAME        = "miami_weapon.rpf";
public const string MIAMI_GUNS_SELECTED_RPF_NAME = "miami_guns_selected.rpf";

public static void RebuildFromTemplate(
    string templatePath, string targetDlcPath,
    byte[]? hunterWeaponBytes, byte[]? hunterGunsSelectedBytes)
    => RebuildFromTemplate(templatePath, targetDlcPath, new[]
    {
        new DlcChild(MIAMI_WEAPON_RPF_NAME,        hunterWeaponBytes),   // база пака
        new DlcChild(MIAMI_GUNS_SELECTED_RPF_NAME, hunterGunsSelectedBytes), // поверх неё
    });
```

Третий дочерний rpf - `miami_billboards.rpf` - с оружием не пересекается, поэтому его позиция в списке значения не имеет. Пустые и `null`-элементы пропускаются: установка без ганпака кладёт только выборочные стволы, снятие ганпака при живом выборе - наоборот.

## Имя файла = ключ шифрования

Неочевидная деталь, давшая регрессию в 1.1.6. Ключ NG выводится из пары (имя файла, длина). `HunterGunsSelectedRpfBuilder` чинит собранный архив `ArchiveFixer.FixOrThrow(tempPath, MIAMI_GUNS_SELECTED_RPF_NAME)`, то есть шифрует его под именем `miami_guns_selected.rpf`. Если те же байты вложить в DLC под именем `miami_weapon.rpf`, игра выведет другой ключ, не расшифрует вложенный архив и молча оставит ванильные стволы - без ошибки, просто «поставилось, а в игре старые пушки».

Та же логика в обратную сторону: пока временный файл называется `dlc.rpf.building`, RageLib открывает его через `OpenWithLogicalName(path, "dlc.rpf")` - иначе TOC расшифруется в мусор.

```cs title="MiamiGraphics.Shell/Services/TargetDlcEditor.cs"
private static RageArchiveWrapper7 OpenWithLogicalName(string path, string? logicalName)
{
    if (string.IsNullOrEmpty(logicalName)) return RageArchiveWrapper7.Open(path);
    var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
    try { return RageArchiveWrapper7.Open(fs, logicalName, leaveOpen: false); }
    catch { fs.Dispose(); throw; }
}
```

Детали схемы ключей - в [шифровании AES+NG](../rpf/encryption.md).

## Когда порядка регистрации мало: слияние в один rpf

Порядок решает, если два архива спорят за один и тот же файл. Но выбранный ствол и ствол из пака могут отличаться набором файлов: у пака к карабину идёт четыре записи, у выбранного - три. Тогда четвёртая запись пака остаётся в слоте и подмешивается к чужой модели.

Поэтому при активном ганпаке выборочные стволы не едут отдельным архивом. Они вплавляются в `miami_weapon.rpf`: файлы пака для этого `internal_name` вырезаются по списку из каталога, файлы выбранного добавляются на их место, `selectedBytes` обнуляется.

```cs title="MiamiGraphics.Shell/Services/SelectedGunsInstaller.cs"
if (s.ExtractedFromActivePackId == activePackId
    && activeByInternal.TryGetValue(s.InternalName, out var activeGun)
    && activeGun.Files is { Count: > 0 })
{
    foreach (var f in activeGun.Files) removeNames.Add(f);   // всё, что пак давал под этим именем
}
foreach (var (name, bytes) in selectedFiles) addByName[name] = bytes;
// ...
weaponBytes = RpfFileMutator.Apply(weaponBytes, removeNames, addByName, overlayStubs);
```

Тем же проходом вырезаются «ванильные слоты» - стволы пака, которые автор сборки оставил стоком (`VanillaSlotInternalNames`). Явно выбранная пушка важнее пометки «ваниль»: если имя есть в обоих списках, вырезания не будет.

`RpfFileMutator.Apply` делает всё за один цикл open → modify → flush. Прошлая версия имела отдельные `RemoveFiles`/`AddFiles`, которые вызывались по разу на ствол: каждый вызов писал промежуточный rpf во временный файл и открывал его заново на следующей итерации, и повторное открытие периодически падало с `Error in RPF7 file entry`. Один проход убрал сам этот путь.

Третий, необязательный слой - пустые заглушки обвесов из `templates/overlaymags`. Они докидываются последними и только если такого имени в итоговом архиве ещё нет, то есть авторский файл и файл выбранной пушки всегда побеждают заглушку.

Каждая запись проходит `RpfEntryGate.TryPrepare` перед укладкой в архив. Ворота отсеивают запись, чьё содержимое не соответствует имени, и пытаются ужать ресурс, который не влезает в стриминг: клиентский потолок - 100 МБ на ресурс, наш порог отказа - 90 МБ, порог предупреждения - 64 МБ. Ужатые байты кешируются по SHA-256 исходника (не больше 4 записей) - в паках `w_x.ydr` и `w_x_hi.ydr` обычно побайтно одинаковы, а пережатие стоит секунды.

## Проверка конфликтов до установки

Перед установкой пака UI спрашивает `gunpackCheckInstallConflicts`. Конфликт - это выбранный ствол (или свой раскрашенный скин), чей `internal_name` есть и в устанавливаемом паке.

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs"
foreach (var sel in selectedRows)
{
    // ствол выбран из этого же пака - спорить не с чем
    if (string.Equals(sel.GunpackId, gunpackId, StringComparison.OrdinalIgnoreCase)) continue;
    if (!newPackByInternal.TryGetValue(sel.InternalName, out var packGun)) continue;

    output.Add(new GunpackInstallConflictDto(
        InternalName:         sel.InternalName,
        DisplayName:          display,
        GunpackPreviewUrl:    packGun.PreviewUrl,   // что даст новый пак
        GunpackGlbUrl:        packGun.GlbUrl,
        GunpackPackName:      newPack.Name ?? gunpackId,
        SelectedPreviewUrl:   srcGun?.PreviewUrl,   // что стоит сейчас
        SelectedGlbUrl:       srcGun?.GlbUrl,
        SelectedFromPackId:   sel.GunpackId,
        SelectedFromPackName: sel.GunpackName));
}
```

Свои скины идут вторым проходом и только по тем именам, которые не дали конфликт на первом - иначе игрок увидел бы два шага модалки про один ствол. У такого конфликта `SelectedFromPackId = "_custom"`: превью в каталоге у нарисованного скина нет, и метка в интерфейсе должна быть не про пак.

Пустой список - ставим молча. Непустой - модалка с двумя превью и 3D-просмотром на каждую пару, ответ приезжает картой `internalName → "pack" | "selected"`. Ответ `pack` разбирается **до** установки: выбранный ствол снимается через `RemoveGunAsync`, свой скин удаляется вместе с кешем, иначе пересборка наложила бы его обратно поверх свежего пака. Ответ `selected` не требует ничего: слияние выше и так поставит выбранный вариант выше пакового.

Если каталог не ответил, метод возвращает пустой список, а не ошибку - модалку не показываем, реальная установка всё равно упрётся в ту же недоступность и скажет об этом сама.

## Состояние установки

Два JSON-файла в `%LocalAppData%\MiamiGraphics\admin`:

| Файл | Владелец | Что внутри |
|---|---|---|
| `installed_gunpack.json` | `GunpackInstaller` | id и имя активного пака, SHA-256 его `weapon.rpf`, время установки |
| `selected_guns.json` | `SelectedGunsInstaller` | список выбранных стволов с их файлами и источником, размер/SHA последней сборки |

Пишутся оба через `.tmp` + `File.Move(overwrite: true)` под своим семафором, установка целиком - под [UpdateRpfMutex](../inject/mutex.md).

`Reconcile` сверяет состояние с диском на старте лаунчера, и здесь два правила выведены из инцидентов.

**Проверка присутствия трёхзначная.** `TryRpfExistsInsideTarget` возвращает `null`, если DLC не удалось открыть - файл занят запущенной игрой. Раньше исключение схлопывалось в `false`, и рестарт лаунчера при запущенной GTA стирал `selected_guns.json` на каждом буте, хотя стволы стояли и работали.

**Расхождение SHA дрейфом не считается.** Вложенный rpf перепаковывается любым следующим проходом - `ArchiveFix` ребёнка, пересборка DLC другим компонентом, слияние выборочных стволов. Его SHA закономерно уходит от записанного при установке. Пока архив с нужным именем лежит внутри DLC, пак считается установленным; несовпадение только пишется в журнал.

```cs title="MiamiGraphics.Shell/Services/GunpackInstaller.cs"
if (stateClaimsInstalled && !dlcExists)
{ drift = true; driftReason = "state claims installed but target DLC missing on disk"; }
else if (stateClaimsInstalled && !weaponInside)
{ drift = true; driftReason = "state claims installed but miami_weapon.rpf missing inside DLC"; }
```

При обнаруженном дрейфе состояние обнуляется, а DLC пересобирается из чистого шаблона; если шаблон не закеширован - DLC просто удаляется.

Отдельно живёт `VerifyInstalledAsync` - явная сверка SHA встроенного `miami_weapon.rpf` с записанным в состоянии. Она нужна для диагностики стороннего вмешательства и возвращает `GunpackVerifyReport` с обоими хешами; на автоматические решения она не влияет.

## Удаление

Удаления «вырезать наши файлы из текущего DLC» нет ни в одном сценарии. Снятие - это та же пересборка из шаблона, только с другим набором детей:

| Что снимаем | Что остаётся в слоте |
|---|---|
| ганпак, выбранных стволов нет | чистый шаблон без вложенных rpf |
| ганпак, выбранные стволы есть | шаблон + `miami_guns_selected.rpf` |
| последний выбранный ствол при отсутствии ганпака | чистый шаблон |
| всё, шаблон не закеширован | `dlc.rpf` удаляется с диска |

Состояние обнуляется **до** пересборки, потому что пересборка читает именно его: `UninstallAsync` пишет пустой `InstalledGunpackState` и зовёт `_selectedGuns.RebuildAsync`, который уже строит DLC по актуальной картине. Если пересборка упала, состояние восстанавливается из снимка (`TryRestoreStateAsync`) - иначе игрок получил бы «ничего не установлено» при живом паке в игре.

Прогресс вложенной пересборки укладывается в свободную полосу вызывающего, а не показывается своей шкалой от нуля:

| Фаза установки пака | Полоса |
|---|---|
| `downloading_template` | 8..28 |
| `downloading_pack` | 28..60 |
| `installing` | 62 |
| `registering` | 70 |
| вложенная пересборка | 72..97 |
| `done` | 100 |

Полоса для снятия - 32..97. Терминальные события вложенного вызова глотаются: `done` и `error` эмитит вызывающий, у него на руках финальный текст.

[Дальше: конфликты ганов →](../history/guns-conflicts.md)
