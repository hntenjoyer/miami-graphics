# История 3D рендера

Эта глава дополняет [Shaders history](../3d/shaders-history.md). Там я писал про путь к финальным шейдерам. Тут - все другие проблемы с 3D рендером которые встретили.

## Проблема: не хватало шейдеров

Самая ранняя проблема. Конвертировал первый `.ydr` (модель Carbine Rifle) → `.glb`. Загрузил в Three.js viewer. Появилась модель **без текстур**. Каркас (wireframe-mode) виден, поверхности заглушены либо чёрным, либо purple-checker (default missing texture в Three.js).

Расследование показало:

- `.ydr` ссылается на текстуры по имени (`gun_carbine_diff` для diffuse, `gun_carbine_nm` для normal map).
- Текстуры лежат в **соседнем** `.ytd` файле.
- Мой первичный конвертер брал только `.ydr` и не подгружал `.ytd`.

Поправил - стал парсить **пару** `.ydr` + `.ytd`. Текстуры появились. Но карабин выглядел **бледным** - серым с чуть металлическим оттенком вместо живой текстуры из игры.

## Проблема: BC7 текстуры

GTA использует **BC7** (BPTC) сжатие для большинства текстур. Это лучший формат с alpha + low color loss, но требует **GPU decompression** или специальной библиотеки на CPU.

`.ytd` хранит текстуру **сжатой** BC7. Чтобы получить RGB byte array - нужно decompress.

Three.js понимает BC7 через DDS текстуры если GPU поддерживает. Но `.glb` хочет PNG или JPEG, не DDS. И headless Chrome (наш [рендерер](../3d/headless-render.md)) работает на SwiftShader без BC7 поддержки.

Решение - **decompress на стороне конвертера**, embed как PNG в .glb:

```cs title="MiamiGraphics.Core/Parser/YddToGlbConverter.cs (упрощено)"
foreach (var entry in ytdFile.TextureDictionary.Textures)
{
    var bc7Data = entry.GetDecompressedData();   // RAGE-specific decode
    var pixels = Bc7Decompressor.DecompressToRgba32(bc7Data, entry.Width, entry.Height);
    var pngBytes = EncodePng(pixels, entry.Width, entry.Height);

    var imageIndex = glbBuilder.AddImage(pngBytes, mimeType: "image/png");
    var textureIndex = glbBuilder.AddTexture(imageIndex);
    glbBuilder.MapMaterial(entry.Name, textureIndex);
}
```

`Bc7Decompressor` это **отдельный** native C# implementation BC7 алгоритма (взяли open-source `dotnet-bc7` пакет). Работает на CPU, ~50 МС на текстуру 1024×1024.

Конверт одного гана с 4 текстурами (diffuse + normal + specular + glow) → 4 PNG → embed в `.glb` → ~200 МС. Для упаковки 50-гана catalog'а это **10 секунд**. Делается один раз при upload'е, кэшируется в R2.

## Проблема: cubemap reflection

Уже [упоминал в Shaders history](../3d/shaders-history.md). RAGE использует environment cubemap для отражений на металлических частях.

Конкретная боль - конвертация `.ytd` → PNG **теряла** cubemap. `.ytd` хранит cubemap как **6 face textures** в одном bundle. Мой первичный конвертер брал каждую face как **отдельную** текстуру - получалось 6 отдельных PNG, glTF не знал что их склеить как cubemap.

Решение - **выявлять** cubemap в `.ytd` (по special flag в texture metadata), упаковывать 6 граней в **horizontal strip** PNG (6× wide × 1× high), записывать в `.glb` как **ImageBitmap** с extension `KHR_environment_map`.

```cs
if (entry.IsCubemap)
{
    var faces = new[] { posX, negX, posY, negY, posZ, negZ };
    var strip = CombineHorizontal(faces);   // 6× wide
    var pngBytes = EncodePng(strip, totalWidth, height);
    glbBuilder.AddCubemap(pngBytes, KHR_environment_map: true);
}
```

В UI viewer (`GlbViewerModalCanvas.tsx`) Three.js GLTFLoader понимает `KHR_environment_map`, реconstructa cubemap при load. Отражения работают как ожидалось.

## Проблема: alpha-cutout «толстые» сетки

Некоторые модели оружия имеют **сетки** - например prison-style решётка на pistol grip или fence-pattern на attachments. Это рендерится через **alpha-cutout** shader (`gta_cutout`) - пиксель либо полностью видим либо полностью прозрачен по alpha-channel.

В моём первом mapping'е `gta_cutout` → `MeshLambertMaterial { transparent: true }`. Получалось - пиксели **полу-прозрачные** где должны быть либо да либо нет. Сетка выглядела «толстой», размытой по краям.

Поправил - `MeshLambertMaterial { transparent: false, alphaTest: 0.5 }`. `alphaTest` это **threshold** для cutout - пиксели с alpha > 0.5 рендерятся непрозрачно, остальные **отбрасываются** (depth buffer не пишется, sorting не нужен). Сетки стали тонкими, корректными.

## Проблема: glass на снайперках

Optic glass (стекло прицела на снайперке) - самая сложная часть. Это **полупрозрачный** material с **отражением** environment + **подсветкой** crosshair'а изнутри.

В RAGE это `gta_glass` или `gta_glass_emissive` shader. Параметры:

- `BaseColor` - tint стекла (обычно белый, иногда жёлтый/зелёный для NV-prism);
- `Alpha` - opacity (обычно 0.3-0.5);
- `Reflection` - strength reflection-cubemap (0.8-1.0);
- `Emissive` - для подсветки crosshair (0.2-0.4 если есть);
- `Specular` - bright highlight на углах (0.9-1.0).

В Three.js это `MeshPhysicalMaterial` с `transmission` (для refraction) или `MeshStandardMaterial { transparent: true, envMap, opacity }`.

Сначала пробовал `MeshPhysicalMaterial { transmission: 0.9, thickness: 0.1, ior: 1.5 }`. Получалось ОК но **очень** медленно (transmission даёт extra rendering pass).

Перешёл на `MeshStandardMaterial { transparent: true, opacity: 0.4, envMap: cubemap }`. Без transmission, но с environment-map для отражений. Выглядит «достаточно хорошо» - стекло прозрачное, отражение видно, на изгибах яркий highlight.

Это не **физически** точно (не учитывает refraction внутри объёма), но визуально похоже на in-game.

## Проблема: разный цветовой gamut между разными `.glb`

Под конец заметил - карабин из Pack A выглядит ярче чем из Pack B при тех же настройках сцены. Хотя в игре они одинаково яркие.

Расследование - некоторые `.ytd` хранят текстуры в **linear color space**, другие в **sRGB**. Это зависит от того как автор создал текстуру (Photoshop default vs Substance Painter default).

В RAGE-движке это не проблема - движок умеет работать с обоими и сам делает correct gamma-conversion. В Three.js - **строгая** разница, и если мы embed в `.glb` без указания colorSpace, Three.js предполагает sRGB.

Поправил - при decompress BC7 явно **проверяем** flag «isSRGB» из `.ytd` metadata. Если sRGB - embed как есть. Если linear - конвертируем в sRGB перед embed:

```cs
if (!entry.IsSRGB)
{
    pixels = LinearToSRGB(pixels);
}
```

`LinearToSRGB` - gamma correction 2.2. После этого все ганы выглядят одинаково «правильно» в viewer'е.

## Что я узнал

3D рендер чужих ассетов это **бесконечная** яма edge case'ов. Каждый формат текстур, каждый shader тип, каждый namespace конвенции - потенциал baг.

Самый эффективный путь - это **итеративные** тесты:

1. Залить ган в каталог.
2. Открыть UI viewer.
3. Сравнить с in-game скриншотом (юзеры дают screenshots в чате).
4. Заметить разницу → исправить конкретный case в конвертере.
5. Re-залить ган (форсированно triggers re-convert), повторить.

Полная переписка каждый раз когда новый shader тип попадается. **Через 6 месяцев** добавили все базовые cases для оружия. Когда юзеры начали заливать **броники** - снова те же case'ы (но для skinned mesh, PBR-based, разные shader types). Ещё месяц работы.

[Дальше: гарды и проверки →](../protection/guards.md)
