# Структура patch.zip

`patch.zip` это обычный ZIP без особых ухищрений - DEFLATE compression, никаких заголовков, никакой подписи. Внутри лежат сырые файлы которые мы вытащили из мода во время diff.

## Layout

```
patch.zip
├── manifest.json                  (~5-50 КБ)
├── patch_files/
│   ├── minimap.gfx                (если Replace минимапы)
│   ├── hud_reticle.gfx
│   ├── core.ypt                   (трейсера)
│   ├── bloodfx.dat
│   ├── timecycle/                 (timecycle - много файлов)
│   │   ├── timecycle_main.xml
│   │   ├── clouds.xml
│   │   ├── visualsettings.dat
│   │   └── ...
│   ├── armor/                     (если есть armor компонент)
│   │   ├── armor.glb              (3D модель для UI)
│   │   ├── armor.ydd              (модель для GTA)
│   │   └── armor.ytd              (текстуры)
│   ├── arena/                     (если арена)
│   └── ...
```

`SourcePath` в каждом `PatchAction` указывает на относительный путь внутри `patch.zip`. Например:

```json
{
  "Type": "Replace",
  "TargetPath": "update/update.rpf:/x64/patch/data/cdimages/scaleform_minimap.rpf:/minimap.gfx",
  "SourcePath": "patch_files/minimap.gfx",
  ...
}
```

Инжектор:

1. Скачал `patch.zip` в `<tmp>/redux_install_xxx/`.
2. Распаковал в `<tmp>/redux_install_xxx/patch_files/...`.
3. Прочитал `manifest.json`.
4. Для каждого action открыл `<tmp>/redux_install_xxx/<action.SourcePath>` и положил в результирующий `update.rpf`.

## Что не лежит в patch.zip

- Чистая копия `update.rpf` (она у юзера локально или на R2 как reference).
- Файлы которые не модифицируются (большая часть `update.rpf` - нетронутая ваниль).
- Preview-картинки, gallery images, video - это отдельные R2 объекты не имеющие отношения к patch.

## Размер

Типичный redux:

- `manifest.json` - 5-20 КБ;
- `minimap.gfx` - 200 КБ;
- `hud_reticle.gfx` - 150 КБ;
- `core.ypt` - 40 МБ (самый толстый, как [объяснено](../parser/what-we-extract.md));
- `bloodfx.dat` - 8 КБ;
- timecycle - 1-3 МБ суммарно;
- armor - 5-30 МБ;
- Custom текстуры (если есть) - 50-300 МБ.

Итого 50-400 МБ для среднего redux. Это **в 5-10 раз меньше** чем полный mod RPF (1-3 ГБ).

## Compression

ZIP сжимает большинство файлов DEFLATE'ом, и **хорошо** жмёт XML, `.dat` и uncompressed binary. Плохо жмёт `.gfx` и `.ypt` потому что они уже сжаты внутренне (Adobe Flash compress в `.gfx`, RSC7 + LZ внутри `.ypt`). Поэтому при упаковке:

- XML и .dat сжимаются на 70-90%;
- `.gfx`, `.ypt`, текстуры - на 5-15%;
- Armor `.glb` (если есть) - уже сжат внутренне gltf'ом, не выигрывает от ZIP.

В сумме `patch.zip` обычно 80-90% от raw размера всех файлов.

## Альтернативы которые отвергли

### 7z LZMA2

Жмёт лучше на 10-20%. Но требует на стороне юзера 7z библиотеку или CLI инструмент. И **медленнее** распаковка - 5-10 секунд против 1 для ZIP на тех же данных. Размер моды критичен меньше, чем UX install'а.

### Кастомный binary формат

Был соблазн «давайте свой формат с минимальной overhead'ой». Отверг по двум причинам:

1. ZIP уже стандарт. Любой может открыть в WinRAR, проверить что внутри. Это дешёвая «прозрачность».
2. Кастомный формат потребует свой парсер на C# + поддержку в admin tools. Лишний код, лишние баги.

### .NUPKG (как Velopack)

Velopack использует NuGet `.nupkg` (это тоже ZIP но с XML manifest рядом + Microsoft signing). Пробовали [короткое время](../update/loop-incident.md) - на VirusTotal стало 8/60 (false positives на self-extractor), вернулись на Inno + наш ZIP.

[Дальше: pipeline инжекта →](../inject/pipeline.md)
