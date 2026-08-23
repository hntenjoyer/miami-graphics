# Манифест нашего patch.zip

После того как [парсер](what-we-extract.md) прошёл через мод и diff-engine посчитал что менялось, у нас на руках:

- список `PatchAction` - что и куда положить;
- набор бинарных файлов которые этот список ссылается.

Это упаковывается в нашу собственную пару `patch.zip` + `manifest.json` и заливается в R2. Затем юзер скачивает оба, и наш [инжектор](../inject/pipeline.md) применяет манифест на чистую `update.rpf`.

## Формат `manifest.json`

```cs title="MiamiGraphics.Core/Parser/ParserModels.cs"
public class DiffManifest
{
    public string ReduxName { get; set; }
    public DateTime ParsedAt { get; set; }
    public long TotalPatchSize { get; set; }
    public List<PatchAction> Actions { get; set; }
}

public enum ActionType { Import, Replace, Delete }

public class PatchAction
{
    public ActionType Type { get; set; }
    public string TargetPath { get; set; }   // путь внутри update.rpf куда класть
    public string SourcePath { get; set; }   // путь в patch.zip откуда брать
    public long Size { get; set; }           // размер исходного файла
    public string Sha256 { get; set; }       // SHA-256 для проверки целостности
    public bool IsWholeReplaceNestedRpf { get; set; }  // редкий флаг
}
```

JSON выглядит так:

```json title="manifest.json (фрагмент)"
{
  "ReduxName": "Allegri V3",
  "ParsedAt": "2026-05-17T12:34:56Z",
  "TotalPatchSize": 287654321,
  "Actions": [
    {
      "Type": "Replace",
      "TargetPath": "update/update.rpf:/x64/patch/data/cdimages/scaleform_minimap.rpf:/minimap.gfx",
      "SourcePath": "patch_files/minimap.gfx",
      "Size": 218456,
      "Sha256": "a7c2..."
    },
    {
      "Type": "Replace",
      "TargetPath": "update/update.rpf:/x64/patch/data/effects/ptfx.rpf:/core.ypt",
      "SourcePath": "patch_files/core.ypt",
      "Size": 41356288,
      "Sha256": "b2f8..."
    },
    {
      "Type": "Import",
      "TargetPath": "update/update.rpf:/x64/patch/common/data/clouds.xml",
      "SourcePath": "patch_files/clouds.xml",
      "Size": 8492,
      "Sha256": "ff03..."
    },
    {
      "Type": "Delete",
      "TargetPath": "update/update.rpf:/x64/patch/data/cdimages/somefile_we_dont_want.xml",
      "SourcePath": "",
      "Size": 0,
      "Sha256": ""
    }
  ]
}
```

## TargetPath - наш path формат

Это **путь через вложенные архивы** с разделителем `:` между уровнями.

```
update/update.rpf:/x64/patch/data/cdimages/scaleform_minimap.rpf:/minimap.gfx
```

Читается как:

1. Открой `update/update.rpf` (относительно `<GTA install>/`).
2. Внутри него найди `x64/patch/data/cdimages/scaleform_minimap.rpf`.
3. Внутри этого вложенного rpf найди `minimap.gfx`.

Это **отличается** от форматов CodeWalker (где `/` тоже разделяет вложенные архивы) и Native UI Tools (обратные слэши). Мы выбрали `:` потому что:

- Прямые `/` не дают визуально различить «папка vs вложенный архив»;
- `:` валидный символ в JSON (не надо escape'ить);
- При парсинге легко split'ить.

Когда [инжектор](../inject/pipeline.md) получает manifest, он [группирует actions по root RPF](../inject/pipeline.md#group-by-root) и потом для каждого root делает Smart Rebuild с правильным action map.

## Три типа Action

### Import

> «Этого файла нет в чистой GTA, но он есть у мода. Добавь.»

Применяется для new-only файлов. Типичный пример - кастомный `clouds.xml` который мод хочет добавить. Игра подхватит его если он указан в DLC content.xml как `<filename>` с `BLOOD_FX_FILE` (или соответствующим типом).

### Replace

> «Файл есть и в чистой GTA, и у мода, но они разные. Замени на модную версию.»

Это 90% всех actions. Большинство модов **меняют** существующие файлы (текстуры, конфиги, модели) а не добавляют новые.

### Delete

> «Этого файла НЕТ у мода, но он есть в чистой GTA. Удали.»

Редко используется. Применяется когда мод хочет **отключить** какой-то ванильный эффект - например удалить `tracer_white.ypt` файл который добавляет белые трейсера к стандартным пулям, чтобы остались только цветные от мода.

Diff-engine генерирует Delete actions только для **строго определённых** путей которые мы знаем что мод может убирать. Полностью отзеркаленный diff (всё что есть в чистом и нет в моде) генерировал бы тысячи Delete на каждый redux, потому что моды не таскают с собой весь чистый contents.

## SHA-256 проверка

Каждый action имеет `Sha256` исходного файла. После download `patch.zip` инжектор перед применением:

1. Извлекает из zip каждый `SourcePath`.
2. Считает SHA-256 содержимого.
3. Сравнивает с manifest.
4. Если не совпало - ошибка `"Patch.zip corrupted at <path>"`, install прерывается.

Это защита от:

- Битый download (CDN отдал кривой файл);
- MITM на промежуточном прокси (для РФ важно - российские провайдеры порой подменяют контент);
- Поврёжденный архив на R2 (admin при upload не докачал, partial file).

## Размер typical patch

| Redux | Размер мода (`.rpf`) | Размер `patch.zip` |
|---|---|---|
| Минималистичный (только минимапа + крос) | ~80 МБ | 2-5 МБ |
| Средний (минимапа + крос + трейсера + bloodfx) | ~300 МБ | 50-80 МБ |
| Тяжёлый (всё выше + timecycle + кастомные текстуры) | 1-1.5 ГБ | 150-400 МБ |
| Total redux replacement | 2+ ГБ | 1.5+ ГБ (близко к full) |

Главный «толстый» файл - `core.ypt` для трейсеров - он сам по себе 40 МБ. Если автор мода трогает только трейсера, патч уже не меньше 40 МБ. Если трогает timecycle XMLs - это уже мелочь, килобайты.

[Дальше: алгоритм diff →](../diff/algorithm.md)
