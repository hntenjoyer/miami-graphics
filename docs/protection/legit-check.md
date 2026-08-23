# Легит-чек: что увидит админ RP-сервера

Графический мод и чит живут в одном и том же файле - `update/update.rpf`. RP-сервера разрешают его менять, потому что иначе не поставить ни минимапу, ни таймцикл, но именно туда же кладут антиотдачу, аим-ассист и урон. Хеш файла в такой ситуации бесполезен: он отличается от ванильного у любого мода, и честного от нечестного не отделяет.

`LegitCheckService` отвечает на вопрос по значениям, а не по байтам: берёт чистый `update.rpf`, берёт установленный (или манифест мода из каталога), обходит дерево, для каждого расхождения решает - это косметика, это странно, или это правка отдачи/разброса/урона/аима. Результат - список находок с классами Neutral/Visual/Warning/Danger, пополевой дифф оружейных `.meta` и один итоговый вердикт Safe/Mixed/Danger.

Сервис только читает. Он никогда не пишет в rpf, не трогает бэкапы и не зависит от состояния инжектора.

## Два режима входа

```cs title="MiamiGraphics.Core/Services/LegitCheckService.cs"
public LegitReport CheckUpdateRpf(
    string cleanRpfPath, string targetRpfPath, string sourceLabel,
    Action<int, string>? progress, CancellationToken ct)

public LegitReport CheckFromManifest(
    DiffManifest manifest,
    Func<PatchAction, byte[]?> fetchModBytes,
    Func<string, (byte[]? bytes, string? where)> resolveVanilla,
    string sourceLabel,
    Action<int, string>? progress, CancellationToken ct)
```

`CheckUpdateRpf` - «мой установленный `update.rpf`»: полный рекурсивный обход target против clean, включая вложенные архивы. `CheckedCount` = общее число файлов в target, прогресс шлётся каждые 25 файлов.

`CheckFromManifest` - проверка мода каталога **до установки**: классификация идёт по готовому [манифесту](../parser/manifest-schema.md), `CheckedCount` = число действий в нём. Байты патча качаются не всегда:

```cs title="MiamiGraphics.Core/Services/LegitCheckService.cs"
public static bool NeedsBytesForFieldDiff(string innerPath)
{
    return ClassifyPath(innerPath) is FileClass.WeaponMeta or FileClass.ComponentsMeta
        or FileClass.AnimationsMeta or FileClass.PedAccuracy or FileClass.PedHealth
        or FileClass.PedBounds or FileClass.Explosion or FileClass.UnknownXml
        or FileClass.TuneBinary or FileClass.CamerasBinary;
}
```

Если ни одно действие манифеста не попадает в этот набор, `patch.zip` не скачивается вообще - чисто визуальный редукс получает вердикт после разбора манифеста. Когда качать всё же приходится, `AppBridge` отдаёт прогресс диапазоном 5..70 %, сам обход занимает 70..99 %.

## Открытие чистого эталона

Эталон - клин-копия из [бэкапа](../backup/snapshot-vs-clean.md), а лежит она под именем `update_<версия>.rpf`. Штатный `RageArchiveWrapper7.Open` на ней падает с «RPF7 has no directory entries», и это не баг библиотеки: индекс [NG-ключа](../rpf/encryption.md) TOC выводится из имени и длины файла.

```cs title="MiamiGraphics.Core/Services/LegitCheckService.cs"
uint? KeyIdx(string name)
{
    if (len < 0) return null;
    try { return (GTA5Hash.CalculateHash(name) + (uint)len + (101 - 40)) % 0x65; }
    catch { return null; }
}
```

`OpenArchiveResilient` пробует сначала кандидатов имени (родное имя файла, `update.rpf`), затем перебирает синтетические имена `k0`, `k1`, … пока множество опробованных индексов не покроет все `0x65` = 101 ключей; предохранитель - 5000 итераций. AES- и незашифрованные архивы открываются с первой попытки, имя для них не важно.

Отдельная деталь: `RageArchiveWrapper7.Open` при исключении закрывает переданный поток, `leaveOpen` не спасает. Поэтому функция принимает не `Stream`, а фабрику `Func<Stream>` - на каждую попытку создаётся свежий поток.

## Быстрый отказ до всякого разбора

На обходе `update.rpf` два уровня отсечки, обе до классификации:

1. Сырые байты записей совпали (`SafeRaw`) - файл не тронут, находка не создаётся.
2. Расшифрованные и распакованные байты совпали (`SafeDecode`) - файл пережат другим уровнем DEFLATE, но содержимое идентично. Тоже не находка.

Без второго уровня любая пересборка архива давала бы тысячи «изменённых» файлов: [Smart Rebuild](../inject/smart-rebuild.md) наследует флаги сжатия, но донор мода - нет.

## Классификация пути

`ClassifyPath` работает по имени листа и по каталогу, порядок проверок значим - первое совпадение выигрывает.

| Класс | Что попадает | Как проверяется |
|---|---|---|
| `WeaponMeta` | любой `.meta` внутри `/data/ai/`, плюс `vehicleweapon*.meta` где угодно | пополевой дифф с оригиналом |
| `ComponentsMeta` | `weaponcomponents.meta` | пополевой дифф |
| `AnimationsMeta` | `weaponanimations.meta` | пополевой дифф |
| `PedAccuracy` | `pedaccuracy.meta` | пополевой дифф |
| `PedHealth` | `pedhealth.meta`, `healthconfig.meta` | пополевой дифф |
| `PedBounds` | `pedbounds.xml` | пополевой дифф |
| `Explosion` | `explosion.meta`, `explosion.ymt` | дифф; бинарный вариант - Neutral |
| `TuneBinary` | `playertargetting.ymt`, `pedtargetevaluator.ymt` | PSO→XML, любое отклонение красное |
| `CamerasBinary` | `cameras.ymt` | PSO→XML, любое отклонение красное |
| `YellowMeta` | `pickups.meta`, `shop_weapon.meta`, `weaponarchetypes.meta`, `loadouts.meta`, `combatbehaviour.meta`, `weapontargetsequences.meta`, `scenarios.meta`, `vehiclelayouts.meta`, `taskdata.meta`, `relationships.meta` | Warning, дифф для справки |
| `Visual` | `/timecycle/`, `/weather/`, `cloudhat`, `visualsettings.dat`, `weaponfx.dat`, `minimap.ymt`, `mapzoomdata.meta` и ресурсы `.gfx .ytd .ydr .ydd .yft .ycd .awc .rel .ypt .ytyp .ymap .ynv` | Visual без разбора |
| `UnknownXml` | прочие `.meta/.xml/.ymt`, `*.ydr.xml`, `playertargetting.meta`, `pedtargetevaluator.meta`, `playerinfo.meta` | контент-скан на красные поля |
| `Neutral` | `content.xml`, `setup2.xml`, `playerswitch.meta`, прочее в `x64/data/tune/`, всё остальное | только факт изменения |

Два решения тут неочевидны и оба выстраданы.

**Оружие определяется по месту, а не по имени.** Ранняя версия искала префиксы `weapons_` / `weapon_` и проезжала мимо `marksmanrifle_mk2.meta` - файл без префикса, лежащий в том же `common/data/ai`. Сейчас правило простое: любой `.meta` в `/data/ai/` - это `CWeaponInfo`, независимо от поколения имени.

**`content.xml` и `setup2.xml` - нейтральные.** Их правит каждый мод, это регистрация файлов, а не тюнинг. Если считать их подозрительными, красным становится вообще всё.

## Красный список

74 поля и 2 флаг-токена. Совпадение по имени элемента где угодно в XML - красный дифф.

| Группа | Полей | Примеры |
|---|---|---|
| Отдача и тряска | 13 | `RecoilShakeAmplitude`, `RecoilAccuracyMax`, `RecoilRecoveryRate`, `IkRecoilDisplacement` |
| Разброс | 6 | `AccuracySpread`, `BatchSpread`, `RunAndGunAccuracyModifier` |
| Аим и хедшоты | 8 | `RecoilAccuracyToAllowHeadShotPlayer`, `MaxHeadShotDistancePlayer`, `LockOnRange`, `WeaponRange` |
| Урон и скорострельность | 18 | `Damage`, `NetworkPlayerDamageModifier`, `TimeBetweenShots`, `ClipSize`, `AnimReloadRate` |
| Вьюмодел | 5 | `FirstPersonScopeFov`, `FirstPersonScopeOffset` |
| Компоненты | 2 | `AccuracyModifier`, `DamageModifier` |
| Анимации (рапид-фаер) | 3 | `AnimFireRateModifier`, `AnimBlindFireRateModifier` |
| `pedaccuracy.meta` | 7 | `PLAYER_RECOIL_MODIFIER_MIN`, `AI_GLOBAL_MODIFIER` |
| Лок-он (текстовый `playertargetting`) | 4 | `LockOnRangeModifier`, `NoReticuleMaxLockOnRange` |
| ХП и броня | 7 | `DefaultHealth`, `DefaultArmour`, `Invincible` |
| Взрыв | 1 | `EndRadius` |

Флаги считаются отдельно: в `WeaponFlags` красным становится **добавление** токена `CanLockonOnFoot` или `CanLockonInVehicle` - авто-аим на оружие, которое его в ваниле не имеет. Удаление токенов красным не считается.

Категория для подписи файла берётся из имени поля, первое совпадение выигрывает:

```cs title="MiamiGraphics.Core/Services/LegitCheckService.cs"
if (f.Contains("recoil") || f.Contains("shake")) return Loc.T("legit.catRecoil");
if (f.Contains("spread") || f.Contains("accuracy")) return Loc.T("legit.catSpread");
if (f.Contains("headshot") || f.Contains("lockon") || f == "weaponrange") return Loc.T("legit.catAim");
if (f.Contains("damage") || f == "timebetweenshots" || f == "clipsize"
    || f.Contains("firerate") || f.Contains("reload")) return Loc.T("legit.catDamage");
if (f.Contains("firstperson") || f.Contains("fov")) return Loc.T("legit.catViewmodel");
if (f.Contains("health") || f.Contains("armour") || f == "invincible") return Loc.T("legit.catHpArmor");
return Loc.T("legit.catShooting");
```

Порядок объясняет пару неинтуитивных попаданий: `RecoilAccuracyMax` уходит в «отдачу», а не в «разброс», а `HeadShotDamageModifierPlayer` - в «аим», а не в «урон».

## Классы находок

| Класс | Когда присваивается |
|---|---|
| `Neutral` | файл вне красных категорий; или боевая мета, где все значения совпали с оригиналом; или дифф дал ноль расхождений (`FormatOnly`); или добавленное оружие без ванильного аналога; или вложенный архив, байты которого не получены |
| `Visual` | класс `Visual` по пути; или вложенный `.rpf`, который открылся и не содержит оружейных правок |
| `Warning` | `YellowMeta`; `UnknownXml`, внутри которого нашлись красные поля; изменённая боевая мета, оригинал которой достать не удалось; бинарная мета без XML-разбора; файл, для которого не получены байты мода (плюс попадание в `Unverified`) |
| `Danger` | боевая мета отличается от оригинала хотя бы по одному красному полю; `TuneBinary`/`CamerasBinary` с любым отклонением значения; удалённый боевой файл; вложенный архив, внутри которого нашлась красная мета |

Значения enum упорядочены `Neutral < Visual < Warning < Danger` - на этом держатся и сортировка находок, и подъём класса `if (finding.Severity < LegitSeverity.Warning) finding.Severity = LegitSeverity.Warning`.

Ключевое правило: **красным становится не факт правки оружейного файла, а расхождение по красному полю**. Мод, который переписал `weapons_pistol.meta` ради звука выстрела и анимации, получает Neutral с подписью «оружие изменено» и заметкой, что значения стрельбы совпали. Ранняя версия красила такие файлы в Danger просто за имя - на дистрибутиве реальных редуксов это давало красный вердикт почти всем, и вердикт переставали читать.

## Где ищется оригинал

Вторая половина того же правила: чтобы сравнивать по значениям, нужен ванильный экземпляр файла. У DLC-оружия (`*_mk2` и прочее) его в `update.rpf` нет вообще. `ResolveVanillaBytes` перебирает шесть мест по порядку и возвращает не только байты, но и человекочитаемое «где нашли» - оно печатается в заметке находки.

| # | Где ищем | Зачем |
|---|---|---|
| 1 | чистый `update.rpf`, точный путь | обычный случай |
| 2 | чистый `update.rpf`, `common/data/ai/<leaf>` и `common/data/<leaf>` | мод положил файл в свой `dlc_patch`, оригинал - в базе |
| 3 | `dlc_patch/<dlc>/…` → `update/x64/dlcpacks/<dlc>/dlc.rpf` | оригинал mk2-ганов живёт только в dlcpack |
| 4 | путь уже вида `dlcpacks/<dlc>/dlc.rpf/…` → тот же dlcpack | мод правит пак напрямую |
| 5 | `common.rpf` - точный путь, `data/ai/<leaf>`, `data/<leaf>` | `pedaccuracy`, `pedhealth`, базовые оружейные меты |
| 6 | фаззи-поиск по базе имени гана в `common/data/ai` чистого `update.rpf` | мод положил мету под другим поколением имени |

Фаззи-поиск нужен, потому что одно и то же оружие в разных поколениях зовётся по-разному. `NormalizeGunBase` срезает префиксы `vehicleweapons_`, `vehicleweapon_`, `weapons_`, `weapon_`, `vehicle_` и расширение, после чего `weapons_marksmanrifle_mk2.meta` и `marksmanrifle_mk2.meta` сходятся в один ключ `marksmanrifle_mk2`.

Если оригинал не нашёлся: **добавленный** файл - Neutral с подписью «новое оружие · кастомный контент» (чит-вектор - это подмена существующего оружия, а не появление новой пушки), **изменённый** - Warning с честной заметкой «не удалось получить оригинал».

## Пополевой дифф `.meta`

`DiffMetaXml` сравнивает не строки, а значения по адресу «владелец → поле». Форматирование, отступы, порядок атрибутов и добавленные пустые строки на результат не влияют.

```cs title="MiamiGraphics.Core/Services/LegitCheckService.cs"
public sealed class LegitFieldDiff
{
    public string Owner { get; set; } = "";
    public string Field { get; set; } = "";
    public string CleanValue { get; set; } = "";
    public string ModValue { get; set; } = "";
    public double? DeltaPercent { get; set; }
    public bool IsRed { get; set; }
}
```

Как матчатся элементы:

- дети группируются по имени элемента; внутри группы пара ищется по **идентичности** - дочерний `<Name>` без вложенных элементов, иначе атрибут `key` или `name`;
- безымянные элементы матчатся по порядковому номеру **внутри группы**, а не по номеру строки в файле;
- `Owner` наследуется вниз по дереву от ближайшего `<Name>`, поэтому в отчёте строки сгруппированы по `WEAPON_CARBINERIFLE`, `COMPONENT_AT_AR_AFGRIP` и подобным;
- элемент, которого нет в чистом - строка `<Item> (добавлен)`; красная, если внутри него есть хоть одно поле из красного списка. Симметрично для удалённых.

Как сравниваются значения:

- атрибуты сверяются по объединению имён, кроме служебного `type`; для `value` в поле пишется имя элемента, для остальных - `Элемент.Атрибут`;
- числа считаются равными при `|a-b| <= 1e-6 * max(1, max(|a|,|b|))` - иначе `0.34` и `0.340000` дают ложный дифф;
- текст, содержащий пробел и не парсящийся как число, разбирается как список токенов (флаги). Сравниваются множества: изменение порядка токенов диффом не считается, показываются только добавленные и убранные;
- дельта в процентах: `Math.Round((mod - clean) / |clean| * 100.0, 1)`. Если `|clean| <= 1e-9`, процент не считается - в UI колонка пустая.

Как это выглядит в отчёте:

| Владелец | Поле | Чисто | Мод | Δ |
|---|---|---|---|---|
| `WEAPON_CARBINERIFLE` | `RecoilShakeAmplitude` | `0.250000` | `0.050000` | −80,0 % |
| `WEAPON_CARBINERIFLE` | `AccuracySpread` | `1.500000` | `0.100000` | −93,3 % |
| `WEAPON_CARBINERIFLE` | `Damage` | `30` | `45` | +50,0 % |
| `COMPONENT_AT_AR_AFGRIP` | `AccuracyModifier` | `1.000000` | `1.750000` | +75,0 % |
| `WEAPON_CARBINERIFLE` | `WeaponFlags` | убрано: — | добавлено: `CanLockonOnFoot` | — |

Красные строки подсвечиваются в UI и только они участвуют в вердикте; нередкий случай - десяток чёрных строк (звук, анимация, модель) и ни одной красной, файл при этом остаётся Neutral.

## Бинарные `.ymt`: наводка и камера

`playertargetting.ymt`, `pedtargetevaluator.ymt` и `cameras.ymt` в ваниле - бинарный PSO с магией `PSIN`. Сравнивать их побайтно бессмысленно: получится «байты не совпали» без единого указания, что именно поменяли.

```cs title="MiamiGraphics.Core/Services/LegitCheckService.cs"
private static byte[]? TryDecodePsoToXml(byte[] bytes)
{
    if (bytes.Length < 4 || bytes[0] != (byte)'P' || bytes[1] != (byte)'S'
        || bytes[2] != (byte)'I' || bytes[3] != (byte)'N')
        return null;
    ...
    var exp = new RageLib.GTA5.PSOWrappers.PsoXmlExporter
    {
        HashMapping = PsoHashMap(),
        TolerateUnknownHashes = true,
    };
}
```

Карта `хеш → имя` собирается лениво из embedded-ресурсов `Resources/pso/*.txt`: каждая строка прогоняется через `Jenkins.Hash`. Если ресурсов нет, карта пустая - поля показываются как `hash_XXXX`, дифф от этого не ломается. `NormalizeTuneToXml` сначала пробует разобрать байты как обычный XML (моды часто подкладывают текстовую версию вместо бинарной) и только потом декодит PSO.

Для этих трёх файлов планка другая, чем для оружейных мет: **любое** отклонение значения помечается красным, потому что весь файл целиком про наведение. Если декодировать удалось и расхождений ноль - Neutral с подписью «совпадает с оригиналом». Если декодировать не удалось: добавленный файл - Warning, изменённый - Danger.

## Вложенные архивы и удаления

Мод может принести целый `.rpf` - свой dlcpack или подменённый `scaleform_*.rpf`. Такой архив вскрывается из памяти и обходится, но проверяются в нём только боевые классы (`WeaponMeta`, `ComponentsMeta`, `AnimationsMeta`, `PedAccuracy`, `PedHealth`, `PedBounds`):

- архив не открылся - Neutral, заметка «не удалось открыть для проверки»;
- открылся, боевых файлов нет - Visual, «проверен · чисто»;
- нашлись боевые файлы - их находки добавляются в отчёт по отдельности, а сам архив получает Danger, если внутри есть красная находка, иначе Warning.

Удаления фильтруются на входе: удаление визуального файла (таймцикл, погода, текстура) в отчёт не попадает вообще - это шум, моды регулярно выкидывают ванильные ресурсы. Удаление боевого файла - Danger: убрав оригинальную мету, можно отключить ванильные значения.

## Вердикт

```cs title="MiamiGraphics.Core/Services/LegitCheckService.cs"
report.Findings = report.Findings
    .GroupBy(f => f.Change + "|" + f.Path.ToLowerInvariant())
    .Select(g => g.First())
    .OrderByDescending(f => f.Severity)
    .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
    .ToList();
```

Сначала дубликаты путей схлопываются (обход вложенных архивов может дать повтор), список сортируется по классу вниз, считаются `DangerCount`, `WarningCount`, `ChangedCount`, `AddedCount`, `DeletedCount`. Дальше правило в три ветки, без весов и порогов:

| Вердикт | Условие |
|---|---|
| `Danger` | есть хотя бы одна находка класса Danger |
| `Mixed` | Danger нет, есть хотя бы одна Warning |
| `Safe` | ни одной Danger и ни одной Warning |

Порога вида «три красных поля - это ещё нормально» здесь нет намеренно. Одно поле `RecoilShakeAmplitude`, уведённое в ноль, - это уже готовая антиотдача.

К вердикту собираются причины (`VerdictReasons`), они и попадают в текст отчёта:

- **Danger** - список затронутых категорий, список владельцев вида `WEAPON_*` (до 8 имён, дальше многоточие), самое сильное отклонение (диф с наибольшим `|Δ|` в формате `владелец → поле clean → mod (±%)`), плюс до 4 опасных файлов, у которых полевого диффа нет вовсе.
- **Mixed** - сколько оружейных файлов изменено при совпавших значениях, сколько «необычных» файлов (до 5 имён), сколько файлов не удалось проверить по значениям (`Unverified`).
- **Safe** - сколько чисто визуальных изменений, сколько оружейных файлов изменено при полном совпадении значений, сколько файлов отличаются только форматированием; если находок нет вовсе - «отличий от чистой игры не найдено».

Отдельная ветка в `AppBridge`: если мод в каталоге несёт плашку «Проверенный», вердикт `Mixed` поднимается до `Safe` с явным указанием причины. `Danger` не маскируется никогда - если у проверенного мода нашлись красные значения, это важнее плашки.

## Чего проверка не даёт

- Сравнение идёт с **нашей** клин-копией. Если её нет, `CheckUpdateRpf` не запускается вовсе, а `CheckFromManifest` деградирует до классификации по путям, и всё, что требовало байтов, уходит в `Unverified` и Warning.
- Бинарные форматы без XML-представления по значениям не сверяются. `explosion.ymt` в таком виде даёт Neutral, остальные боевые классы - Warning с заметкой «сравнение по значениям недоступно».
- Контент-скан нового файла и перечисление значений недекодируемого тюнинга обрезаются на 60 строках - новая оружейная мета несёт все поля по схеме, и полный список нечитаем.
- Проверяется только содержимое архивов. Внешние `.asi`, `.dll` и скрипты в области `LegitCheckService` не находятся.

[Гарды перед инжектом →](guards.md) · [Что будет если не пройти →](failure-modes.md)
