# Определение компонентов

Когда [парсер](what-we-extract.md) прошёл diff донора против чистой GTA, он получает список **сырых** action'ов: «вот этот файл изменён, вот этот добавлен». Список - это десятки или сотни путей внутри `update.rpf`. Чтобы UI мог показать «у этого мода есть минимапа, прицел и трейсера», нужно разобрать сырые actions по **компонентам**.

Этим занимается `ComponentScanner` параллельно с `RpfDiffEngine`. Он не использует action list - идёт прямо по дереву donor RPF и проверяет наличие конкретных «маркерных» файлов.

## Какие маркеры используем

Каждый компонент имеет один-два **обязательных** файла без которых он не работает:

| Компонент | Маркер (минимум один из) | Дополнительные файлы |
|---|---|---|
| minimap | `minimap.gfx` | - |
| crosshair | `hud_reticle.gfx` | `reticle*.png` текстуры (если custom) |
| tracers | `core.ypt` | - |
| bloodfx | `bloodfx.dat` (loose, не в rpf) | - |
| timecycle | `visualsettings.dat` ИЛИ `clouds.xml` ИЛИ `cloudkeyframes.xml` ИЛИ `timecycle_mods_*.xml` | вся папка `common/data/timecycle/` |
| armor | `*.ydd` модели persona/freemode + `*.ytd` текстуры | manifest entries |
| arena | streaming files в arena DLC | - |

Если хоть один маркер найден - компонент `IsFound = true`. Если их **нет** - компонент пропускается, UI не покажет его в списке кастомизации.

## Алгоритм для loose-файлов

`bloodfx.dat` не лежит во вложенном `.rpf` - это loose-файл в `common/data/effects/`. Для него отдельный helper:

```cs title="ComponentScanner.ResolveLooseFileComponent (упрощено)"
ComponentInfo ResolveLooseFileComponent(
    IArchiveDirectory root,
    string targetFile,
    string defaultPath)
{
    // ищем файл по точному пути
    if (root.TryFindByPath(defaultPath, out var file))
        return new ComponentInfo {
            IsFound = true,
            InternalPaths = [defaultPath],
            Flags = ["importable", "replaceable"]
        };
    return new ComponentInfo { IsFound = false };
}
```

## Алгоритм для timecycle

Timecycle - самый сложный компонент, потому что **много файлов** разных типов:

```cs title="ComponentScanner.FindTimecyclePaths"
var tcPaths = FindFilesByGroupRulesRecursive(moddedRoot, new Func<string, bool>[]
{
    p => p.Contains("common/data/timecycle/", StringComparison.OrdinalIgnoreCase),
    p => p.EndsWith("common/data/visualsettings.dat", StringComparison.OrdinalIgnoreCase),
    p => p.EndsWith("common/data/cloudkeyframes.xml", StringComparison.OrdinalIgnoreCase),
    p => p.EndsWith("common/data/clouds.xml", StringComparison.OrdinalIgnoreCase),
    p => Regex.IsMatch(p, @"timecycle_mods_.*\.xml$", RegexOptions.IgnoreCase),
    p => p.Contains("weather/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase),
    p => p.Contains("clouds/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
});
```

Это **OR-список правил** - любой файл удовлетворяющий хоть одному правилу попадает в `InternalPaths`. Дальше при импорте timecycle из другого redux'а мы **копируем все эти файлы** разом (timecycle нельзя «частично» взять - атомарная замена визуального стиля игры).

## Algorithm Internal Paths - зачем нужны

Поле `InternalPaths` в `ComponentInfo` хранит **точный список** файлов которые принадлежат компоненту. UI его использует для счётчика «вот столько файлов будет импортировано». Бэкенд использует при apply кастомизации - копирует **именно** эти файлы из донора, ничего лишнего.

Например для minimap:

```json
"minimap": {
  "IsFound": true,
  "SourceRpf": "x64/patch/data/cdimages/scaleform_minimap.rpf",
  "InternalPaths": [
    "x64/patch/data/cdimages/scaleform_minimap.rpf:/minimap.gfx"
  ],
  "Flags": ["importable", "replaceable"]
}
```

Для timecycle (обычно):

```json
"timecycle": {
  "IsFound": true,
  "SourceRpf": "common.rpf / direct",
  "InternalPaths": [
    "x64/patch/common/data/timecycle/timecycle_main.xml",
    "x64/patch/common/data/timecycle/clouds.xml",
    "x64/patch/common/data/timecycle/cloudkeyframes.xml",
    "x64/patch/common/data/visualsettings.dat",
    "x64/patch/common/data/timecycle_mods_1.xml",
    "x64/patch/common/data/timecycle_mods_2.xml"
  ],
  "Flags": ["importable", "replaceable"]
}
```

`SourceRpf` - диагностическое поле, показывает «откуда» собран компонент. Не используется в логике, только в Debug UI.

## Что попадает в Supabase

Результат `ResolvedComponentMap` сохраняется в БД при заливке redux'а:

```sql
-- public.redux_versions
-- (один row на каждую версию мода, V1/V2/V3 → 3 row)
components jsonb NOT NULL,           -- весь ResolvedComponentMap
componentUrls jsonb NOT NULL,        -- map componentName → R2 URL
```

Это значит **каждая версия** мода имеет свой набор компонентов. У версии V1 могут быть `minimap + crosshair`, а у V2 добавлен `tracers`. UI это учитывает в [multi-version customize picker](../customize/data-model.md).

`componentUrls` - отдельный map для случая когда нужен **только один компонент**. Если юзер импортирует минимапу из чужого мода, мы не хотим качать полный `patch.zip` донора (может быть 300 МБ). Вместо этого админ при загрузке мода **разделяет** компоненты на отдельные R2 объекты:

```
redux/<id>/v1/patch.zip           # всё вместе, на 300 МБ
redux/<id>/v1/minimap.gfx         # 218 КБ - для customize
redux/<id>/v1/crosshair.gfx       # 145 КБ - для customize
redux/<id>/v1/tracers/core.ypt    # 40 МБ - для customize
redux/<id>/v1/bloodfx.dat         # 8 КБ - для customize
```

И в `componentUrls`:

```json
{
  "minimap":   "https://cdn.miamigraphicsstorage.uk/redux/abc-123/v1/minimap.gfx",
  "crosshair": "https://cdn.miamigraphicsstorage.uk/redux/abc-123/v1/crosshair.gfx",
  "tracers":   "https://cdn.miamigraphicsstorage.uk/redux/abc-123/v1/tracers/core.ypt"
}
```

Так когда юзер хочет «трейсера из X V2», мы качаем только 40 МБ `core.ypt`, не весь patch.zip.

[Дальше: алгоритм diff →](../diff/algorithm.md)
