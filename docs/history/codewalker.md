# CodeWalker - почему не получилось

Первая попытка читать `update.rpf` была через [CodeWalker](https://github.com/dexyfex/CodeWalker). Это open-source инструмент от автора `dexyfex`, де-факто стандарт для GTA V mod community. Полноценный read-only browser RPF архивов, конвертер форматов, viewer 3D-моделей.

CodeWalker умеет **очень многое**:

- Открывать любой RPF8 архив, читать TOC, расшифровывать содержимое;
- Конвертировать `.ydr/.ydd/.yft` в `.obj`/`.fbx` (для blender'а);
- Открывать `.ypt` файлы и просматривать партикл-эффекты;
- Распаковывать вложенные RPF;
- Преобразовывать `.gxt2` (тексты) ↔ XML.

Логично было попробовать использовать его.

## План

Запустить CodeWalker CLI в дочернем процессе на нужный RPF. Получить распакованную папку. Diff'нуть против чистой версии. Создать `patch.zip`.

```csharp
// псевдокод первой попытки
var args = $"-extract \"{moddedRpfPath}\" -output \"{tempDir}\"";
var p = Process.Start("CodeWalker.exe", args);
p.WaitForExit();

// тут должна быть папка с распакованным содержимым
var files = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories);
```

Простая идея. Сработала бы - обошлись бы без своего парсера.

## Что пошло не так

### Проблема 1: nested .rpf

CodeWalker CLI **не** проходит рекурсивно во вложенные `.rpf`. При extract он выдаёт `update.rpf` как **папку**, внутри которой `.rpf`-файлы как **файлы** (без расширения распакованные). То есть `scaleform_minimap.rpf` остаётся бинарём.

Чтобы получить `minimap.gfx`, нужно:

1. Прогнать CodeWalker на `update.rpf` → получили дерево с `scaleform_minimap.rpf` как файлом.
2. Прогнать CodeWalker на `scaleform_minimap.rpf` → получили дерево с `minimap.gfx`.

Два process spawn'а на каждый вложенный RPF. У `update.rpf` 15+ вложенных RPF'ов на типичный мод. **30+ spawn'ов**. Каждый spawn = 1.5 секунды на startup CodeWalker (он подгружает все ключи + RAGE manifest). Итог - **45 секунд** просто на extract.

И это **до** того как мы что-то проанализировали.

### Проблема 2: невозможность работать с `core.ypt` для трейсеров

`core.ypt` это **партикл** ресурс - RSC7 файл с 200+ embedded particle effects. У CodeWalker есть **viewer** для `.ypt`. Можно посмотреть как выглядит партикл.

Но **save обратно** в `.ypt` после правок CodeWalker не умеет. `dexyfex` явно об этом писал в README - read-only writer не stable, save **не реализован**.

Для трейсеров это критично - нам нужно **выгрузить** модифицированный `core.ypt` из донора и положить в результирующий `update.rpf`. CodeWalker может выгрузить, но если мы захотим что-то изменить (а кастомизация это позволяет: цвет трейсеров) - мы можем только заменить **весь** файл целиком (replacement-mode, не edit-mode).

Это **не** тупик сам по себе - replacement-mode мы используем по сей день. Но это значит CodeWalker не даёт **больше** чем простой byte-copy `core.ypt`. Зачем тогда CodeWalker?

### Проблема 3: нормализация имён в lowercase

CodeWalker при extract **приводит** имена файлов в lowercase. `Sprite_Particle.ydr` становится `sprite_particle.ydr` на выходе.

При обратной упаковке (через CodeWalker rebuild) это **могло бы** сработать - Rockstar в RPF тоже хранит lowercase для большинства случаев. Но мы хотели **не** rebuild через CodeWalker, а собрать свой результирующий RPF через RageLib. И там оригинальный case **важен** - RAGE при загрузке делает case-sensitive lookup для некоторых типов ассетов.

Когда мы попробовали:

1. CodeWalker extract → файлы в lowercase.
2. RageLib pack → новый RPF с lowercase именами.
3. GTA загружает → часть ассетов **не находит** (например `MaleHero_001.ydd` теперь `malehero_001.ydd`, и какая-то xml ссылается на старое CamelCase имя).
4. GTA крашится при загрузке uniformly уровня.

Это можно было обойти - патчить XML вместе с переименованием. Но это работа над работой над работой.

### Проблема 4: CodeWalker не считает internal RPF checksums

После rebuild через RageLib + ArchiveFix (как мы делаем сейчас) - checksum'ы пересчитываются и GTA принимает файл. CodeWalker сам **не пересчитывает** их при rebuild - потому что он rebuild'ит в основном для **просмотра**, не для финального деплоя в игру.

Опять не тупик, но опять CodeWalker не делает ту работу за нас.

## Итог

CodeWalker оказался **viewer**'ом, не **builder**'ом. Он отлично читает RPF, показывает 3D, конвертирует в blender-friendly формат - для модмейкеров это идеальный инструмент.

Но как **компонент в нашей build pipeline** для серверной обработки модов - слишком много швов: nested rpf требует множественных runs, нормализация имён ломает RAGE-pathing, save back в RAGE-формат не везде стабилен.

После 3-4 дней попыток с CodeWalker - **перешли на прямую работу с RageLib**. RageLib это API для **формата**, без UI и без extract-в-disk. Мы сами решаем что extract'ить, как именно сравнивать, что писать обратно. Это даёт нам **контроль** который мы не получили бы через CLI.

CodeWalker мы оставили как **ProjectReference** в нашем `MiamiGraphics.Core.csproj` - но не для RPF работы. Используем только его `CodeWalker.Core.GameFiles.YddFile` и `YtdFile` парсеры для конверта `.ydd` → `.glb` (там CodeWalker отличный - полная поддержка формата). RPF читает наш собственный код через RageLib API.

[Дальше: RageLib v1 - почему первая попытка не взлетела →](ragelib-v1.md)
