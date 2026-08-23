# Что мы вытаскиваем из мода

Когда админ заливает новый `redux`, мы получаем зашифрованный `update.rpf` от автора мода. Из него надо:

1. Понять что внутри менялось против чистой версии.
2. Выделить **минимум файлов** которые модифицированы - не весь архив, только дельту.
3. Разнести модифицированные файлы по **компонентам** - минимапа отдельно, прицел отдельно, трейсера отдельно. Это нужно для функции [«кастомизация»](../customize/data-model.md) - юзер может взять минимапу из одного redux'а, прицел из другого.

## Какие компоненты есть

Из `MiamiGraphics.Core/Parser/ComponentScanner.cs`:

```cs
map.Components["minimap"]   = ResolveComponent(..., targetFile: "minimap.gfx", ...);
map.Components["crosshair"] = ResolveComponent(..., targetFile: "hud_reticle.gfx", ...);
map.Components["tracers"]   = ResolveComponent(..., targetFile: "core.ypt", ...);
map.Components["bloodfx"]   = ResolveLooseFileComponent(..., targetFile: "bloodfx.dat", ...);
// timecycle - отдельная функция FindTimecyclePaths, многофайловый компонент
// armor - отдельный детектор ArmorComponentDetector
// arena - отдельный детектор ArenaComponentDetector
```

| Компонент | Целевой файл | Где лежит внутри update.rpf | Flags |
|---|---|---|---|
| `minimap` | `minimap.gfx` | `x64/patch/data/cdimages/scaleform_minimap.rpf:/` | importable, replaceable |
| `crosshair` | `hud_reticle.gfx` | `x64/patch/data/cdimages/scaleform_generic.rpf:/` | importable, replaceable |
| `tracers` | `core.ypt` | `x64/patch/data/effects/ptfx.rpf:/` | importable, replaceable |
| `bloodfx` | `bloodfx.dat` (loose, не в rpf) | `x64/patch/common/data/effects/bloodfx.dat` | importable, replaceable |
| `timecycle` | многофайловый | `x64/patch/common/data/timecycle/*.xml` + `visualsettings.dat` + `*timecycle_mods_*.xml` | importable, replaceable |
| `armor` | `.ydd` + `.ytd` модели брони | `x64/dlcpacks/.../dlc.rpf:/x64/data/cdimages/streamedpeds_players.rpf:/...` | replaceable, transferable |
| `arena` | модели и текстуры арены | `x64/dlcpacks/mp201803_g9ec/dlc.rpf:/...` | replaceable |

Flags определяют что юзер может делать с компонентом в UI:

- `importable` - можно взять «из другого редукса» через ImportReduxPicker;
- `replaceable` - можно поставить из library (наш каталог отдельных компонент-файлов, не привязанных к конкретному redux'у);
- `transferable` - broник может быть установлен «standalone», без всего остального redux'а.

## Алгоритм определения компонента

`ResolveComponent` берёт цепочку fallback'ов:

```cs title="MiamiGraphics.Core/Parser/ComponentScanner.cs (упрощённо)"
ComponentInfo ResolveComponent(
    IArchiveDirectory moddedRoot,
    ContentXmlInfo contentInfo,
    string targetFile,
    string[] flags,
    string[] fallbackRpfs,
    string relatedFilePattern = null)
{
    // 1. Сначала смотрим в content.xml - если он указывает где искать
    foreach (var customRpf in contentInfo.CustomRpfs)
    {
        var subArchive = OpenSubArchive(moddedRoot, customRpf);
        if (subArchive.Contains(targetFile))
            return new ComponentInfo { IsFound = true, SourceRpf = customRpf, ... };
    }

    // 2. Если не нашли - проверяем стандартные пути куда Rockstar
    //    кладёт этот компонент в чистой GTA
    foreach (var path in fallbackRpfs)
    {
        var subArchive = OpenSubArchive(moddedRoot, path);
        if (subArchive.Contains(targetFile))
            return new ComponentInfo { IsFound = true, ... };
    }

    // 3. Не нашли вообще - компонент в этом моде отсутствует
    return new ComponentInfo { IsFound = false };
}
```

### Зачем `content.xml`

DLC внутри GTA описывают сами себя через `content.xml` (XML внутри DLC `.rpf`). Там перечислены custom-rpf этого DLC и в каком порядке грузить. Мод может объявить **свой** `scaleform_minimap.rpf` где-нибудь в `x64/dlcpacks/mod_minimap_pack/dlc.rpf:/scaleform_minimap.rpf` - игра загрузит его поверх стандартного.

`ContentXmlAnalyzer` это парсит и говорит «вот эти rpf - кастомные от мода, проверяй их первыми».

Без этого мы бы пропустили моды которые **добавляют** новые `.rpf` поверх дефолтных вместо того чтобы менять существующие. Это популярный паттерн для оружейных модов (они кидают новый DLC, не трогая `update.rpf` базы).

## Особый случай: tracers и `core.ypt`

Трейсера - это компонент `tracers`, и его цель - файл `core.ypt`. Это **partial файл** (`.ypt` = particle effects, RSC7 ресурс). Внутри `core.ypt` лежит сотни типов партиклов: blood, sparks, smoke, dust, **muzzle flashes, weapon tracers**, искры от пуль по бетону.

Когда мод хочет поменять только трейсера - он берёт **весь** `core.ypt` от чистой GTA, отредактирует партиклы трейсеров, и подкладывает целиком. Потому что менять отдельные партиклы внутри `.ypt` без специальной тулзы нельзя - формат бинарный, нет публичных editor'ов которые умеют сохранять обратно валидный RSC7.

В CodeWalker есть редактор `.ypt`, но он **не сохраняет** изменения - read-only. Это была [причина первой неудачи через CodeWalker](../history/codewalker.md). Перешли на цельную замену файла - менее точно (мы заменяем все 200 партиклов в `core.ypt` ради 4 трейсеров), но единственный рабочий путь.

Это значит что если у юзера установлен redux X с правленым `core.ypt`, а потом он импортирует **только трейсера** из redux Y, новые трейсера получит **со всем остальным `core.ypt`** Y. Если автор Y что-то менял в, скажем, пулевых искрах - юзер их тоже получит. Это документировано в UI tooltip'е: «при импорте трейсеров может поменяться поведение других партиклов».

## ContentXmlAnalyzer на простом примере

```xml title="dlc.rpf:/content.xml (упрощено)"
<CDataFileMgr__ContentsOfDataFileXml>
  <files>
    <Item>
      <filename>dlc_redux:/scaleform_minimap.rpf</filename>
      <fileType>RPF_FILE</fileType>
    </Item>
    <Item>
      <filename>dlc_redux:/scaleform_generic.rpf</filename>
      <fileType>RPF_FILE</fileType>
    </Item>
  </files>
  <dataFiles>
    <Item>
      <filename>platform:/data/effects/bloodfx.dat</filename>
      <fileType>BLOOD_FX_FILE</fileType>
      <overlay value="true"/>  <!-- замещает базовый -->
    </Item>
  </dataFiles>
</CDataFileMgr__ContentsOfDataFileXml>
```

После парсинга получаем:

```cs
ContentXmlInfo {
    CustomRpfs = ["dlc_redux:/scaleform_minimap.rpf", "dlc_redux:/scaleform_generic.rpf"],
    AllFilesToEnable = ["platform:/data/effects/bloodfx.dat"],
}
```

И уже на этой инфе `ComponentScanner` знает что искать `minimap.gfx` нужно в кастомном `scaleform_minimap.rpf` мода, не в стандартном.

[Дальше: схема нашего manifest.json →](manifest-schema.md)
