# Трейсера-сага

Трейсера - это эффект пуль которые летят. В GTA это **партикл-эффект** из файла `core.ypt`. Очень популярный визуальный мод - авторы делают цветные трейсера (синие, красные, зелёные), вариации толщины, glow, и так далее.

С этим компонентом у нас было больше проблем чем со всеми остальными вместе. Расскажу.

## Что такое `core.ypt`

`core.ypt` это **RSC7 ресурс-файл** (`.ypt` = particle library). Внутри 200+ partikel effect definitions:

- **muzzle flashes** - вспышки на стволе при выстреле;
- **bullet tracers** - собственно трейсера (то что светящаяся линия за пулей);
- **bullet impact** - искры/пыль от пули по бетону;
- **blood splash** - кровавые брызги;
- **explosion smoke** - дым после взрыва;
- **fire effects** - горение машин и persons;
- **lots of weather effects** - дождь, снег, туман.

Это **один** файл с 200+ embedded effects. Изменить отдельный - нельзя без специальной тулзы.

## Попытка 1: открыть .ypt в C#

Думал - это же просто RSC7 ресурс, мы его умеем читать через RageLib. Распарсим внутреннюю структуру, найдём partikel «bullet_tracer», поменяем color, запишем обратно.

Внутри `.ypt`:

```
RSC7 header
├── ParticleEffectLibrary
│   ├── ParticleEffect[0] - "muz_flash_pistol"
│   │   ├── EmittersList
│   │   ├── KeyframesList
│   │   └── TextureRefs[]
│   ├── ParticleEffect[1] - "bullet_tracer_normal"
│   │   ├── EmittersList
│   │   │   ├── Emitter[0]
│   │   │   │   ├── ColorOverLife: Vector4[] (R,G,B,A keyframes)
│   │   │   │   ├── SpeedOverLife: float[]
│   │   │   │   ├── TextureRef
│   │   │   │   └── ...
│   │   │   └── ...
│   │   └── ...
│   ├── ParticleEffect[2] - "bullet_tracer_minigun"
│   └── ...
├── TextureDictionary  (embedded TXD с теxturesами для партиклов)
└── Various lookup tables
```

Это **сложный** формат. У CodeWalker есть read-only парсер, который умеет показать каждый partikel в 3D viewer'е. Но write back в `.ypt` после правок - **не реализовано** ни у одной открытой библиотеки.

Я начал писать свой parser. Полтора дня на чтение спецификации. **Семь** разных типов lookup tables - некоторые с pointer'ами на virtual memory, некоторые на physical, некоторые offset'ы байтовые, некоторые в 16-byte chunks. RSC7 формат был **жестоко** оптимизирован под загрузку в GTA-движок, не под человеческое чтение.

После 3 дней работы пришёл к выводу - полный write-обратно `.ypt` это **2-3 недели** работы для одного человека. И нет гарантии что GTA примет файл - там встроенные **внутренние** хеши которые ArchiveFix не считает (это другой формат checksum, специфичный для `.ypt`).

Сдался. Trace-режим не имеет смысла если open-source community не сделал это за десять лет существования модов.

## Попытка 2: переходник на JSON через CodeWalker

Думал - CodeWalker умеет export `.ypt` в **YPT XML** (свой формат). Можно:

1. Export `core.ypt` → `core.ypt.xml` через CodeWalker CLI.
2. Парсим XML, ищем `<ParticleEffect name="bullet_tracer">`, меняем `<color>` атрибуты.
3. Import XML обратно → новый `core.ypt`.

`CodeWalker.exe` принимает `-export` и `-import` параметры. Попробовал. Export работал - получил XML 40 МБ (это плотно сжатый бинарь, превратился в 40 МБ XML текста).

Import обратно - **не сработал**. CodeWalker write back `.ypt` тоже не реализовал, как и save в его GUI.

Тупик. CodeWalker не помогает.

## Попытка 3 (финальная): целая замена `core.ypt`

Признали - отдельные партиклы внутри `.ypt` менять **нельзя** инструментами которые у нас есть.

Решение - **замена всего файла** целиком. Мод имеет свой `core.ypt` (автор сделал его в каком-то проприетарном инструменте, например купленным dexyfex tool который умеет save - он есть private only). Мы берём этот `.ypt` от мода, и **заменяем** ванильный в `update.rpf` целиком.

Cost - все 200+ эффектов меняются на модовые. Юзер мог хотеть только трейсера, а получит ещё **modded** blood, explosion smoke, muzzle flashes и всё остальное. Это **неточный** компонент-pick - мы импортируем «трейсера» но привозим с собой полный effect library.

## UI implications

В UI явно tooltip:

> При импорте трейсеров может поменяться поведение других визуальных эффектов (кровь, искры, вспышки). Это **известное** ограничение - `core.ypt` нельзя редактировать частично.

Юзеры это понимают. Иногда жалуются «я ставил Allegri Trace, а кровь от Gucci». Объясняем что это они импортировали трейсера из Gucci, и она пришла с Gucci blood.

## Пользовательский Tracers Picker

Чтобы дать **больше** контроля - добавили **готовые** пресет-модели трейсеров:

```
additionals/tracers/
├── Uzi tracer/
│   └── core.ypt              ← Uzi-style тонкие трейсера
├── Плазма/
│   └── core.ypt              ← Plasma glow
├── Плазма Дым/
│   └── core.ypt
├── Растворяющийся/
│   └── core.ypt
├── Чёткие/
│   └── core.ypt
└── Чёткие с дымом/
    └── core.ypt
```

Каждый preset - **полный** `core.ypt` с заменёнными partikel'ами трейсеров (но **ваниль**'ными остальными - blood, smoke, etc).

Юзер выбирает в UI:

```tsx
const TRACER_MODELS: TracerModel[] = [
    { id: 'uzi',          folderName: 'Uzi tracer',      name: 'Uzi tracer',       preview: uziPng },
    { id: 'plasma',       folderName: 'Плазма',          name: 'Плазма',           preview: plasmaPng },
    { id: 'plasma-smoke', folderName: 'Плазма Дым',      name: 'Плазма Дым',       preview: plasmaSmokePng },
    { id: 'dissolving',   folderName: 'Растворяющийся',  name: 'Растворяющийся',   preview: dissolvingPng },
    { id: 'crisp',        folderName: 'Чёткие',          name: 'Чёткие',           preview: crispPng },
    { id: 'crisp-smoke',  folderName: 'Чёткие с дымом',  name: 'Чёткие с дымом',   preview: crispSmokePng },
];
```

Под каждым preset'ом - **RGB color picker**. Юзер выбирает «Чёткие + #FF0000» (красные чёткие трейсера).

Цвет применяется через **post-process** на этапе install:

```cs
// упрощённо
var basePtfx = File.ReadAllBytes(presetPtfxPath);
var modified = TracerColorMutator.ApplyRgb(basePtfx, r, g, b);
File.WriteAllBytes(workDir + "/core.ypt", modified);
```

`TracerColorMutator.ApplyRgb` - **наш** код который **знает** где конкретно в каждом preset'е лежат color-keyframe байты для трейсер-эмиттеров. Это **захардкоженные** offset'ы в файле. Не парсит format целиком - просто читает по offset'ам и заменяет 4 байта (RGB + alpha).

Это работает потому что preset'ы **наши** - мы знаем их structure точно. Для **чужих** `core.ypt` (import from другого редукса) - нельзя, мы не знаем где у автора лежат эти offset'ы.

## SmokeFixMutator

Дополнительная боль с пресетами «Плазма Дым» и «Чёткие с дымом» - там у эффекта smoke trail после трейсера. Если color у трейсера красный, дым **тоже** должен быть красным (иначе смотрится странно - красная пуля с белым дымом за ней).

`SmokeFixMutator.cs` - патчит дополнительный color keyframe smoke-emitter'а, синхронизирует с tracer color:

```cs title="MiamiGraphics.Core/Parser/SmokeFixMutator.cs (упрощено)"
public static byte[] ApplySmokeColor(byte[] ptfxBytes, int r, int g, int b)
{
    // offset'ы найдены экспериментально для каждого preset'а
    var smokeColorOffsets = new[] { 0x18A2C, 0x18B14, 0x18BFC };
    foreach (var off in smokeColorOffsets)
    {
        ptfxBytes[off + 0] = (byte)r;
        ptfxBytes[off + 1] = (byte)g;
        ptfxBytes[off + 2] = (byte)b;
        // alpha не трогаем - она для opacity smoke'а отдельно
    }
    return ptfxBytes;
}
```

Грубо, но работает.

## Итог

Tracers оказались самым **сложным** компонентом не из-за UI или сети, а из-за **самого формата**. Мы провели две недели в попытках обойти ограничения `.ypt`, в итоге пришли к компромиссу:

- **Preset'ы** дают юзеру 6 разных стилей трейсеров (от Uzi до плазмы);
- **RGB picker** даёт цветовую кастомизацию;
- **Import из донора** - для тех кто хочет специфические трейсера из конкретного мода (с пониманием что приедет вся `core.ypt`);
- **Никакого** «свой 100% custom трейсер с любым параметром».

Юзеры довольны - 95% выбирают preset + color, остальные знают что делают и импортируют. Решение которое **работает**, хоть и не идеальное.

[Дальше: конфликты ганов →](guns-conflicts.md)
