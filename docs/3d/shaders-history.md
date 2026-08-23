# История с шейдерами

Когда мы начинали показывать 3D-модели оружия в UI - почти всегда они выглядели **криво**. Не как в игре, не как на скриншотах от автора мода. Странные цвета, пропавшие детали, прозрачные части совсем непрозрачные или наоборот мутные.

Это занимало много времени отладки. Расскажу что было и как пришли к финальному решению.

## Проблема: GTA-шейдеры ≠ Three.js материалы

В `.ydr` моделях каждый submesh имеет **shader type** - название из 100+ предустановленных шейдеров RAGE. Примеры:

| Shader | Назначение | Каналы |
|---|---|---|
| `default` | базовый opaque | diffuse |
| `normal` | базовый с normal map | diffuse + normal |
| `normal_spec` | + specular | diffuse + normal + specular |
| `normal_spec_reflect` | + cubemap reflection | diffuse + normal + specular + cubemap |
| `weapon_normal_spec_palette` | weapon-specific (camo palettes) | diffuse + normal + specular + palette texture |
| `alpha` | прозрачные с альфа-каналом | diffuse RGBA |
| `cutout` | binary alpha (детали типа сетки) | diffuse + cutout |
| `decal` | наклейки поверх | diffuse + decal alpha |
| `glass` | стекло (для оптики снайперок) | diffuse + alpha + cubemap |
| `gta_blend` | многослойная (terrain) | 4 diffuse + 4 normal |

Каждый шейдер ждёт **точно определённый** набор текстур и параметров. GLTFLoader Three.js по умолчанию накладывает PBR-материал - `MeshStandardMaterial` с metalness/roughness. Это **не** RAGE-шейдеры.

Когда мы конвертируем `.ydr → .glb`, надо как-то перевести shader type в Three.js material. Что мы пробовали:

### Попытка 1: всё в `MeshStandardMaterial`

Простой mapping - diffuse texture в `material.map`, normal → `material.normalMap`, specular → `material.metalnessMap` (что неточно но похоже).

Что получилось:

- **Прозрачные ганпаки** (RGB+camo paint поверх) выглядели как мутное пятно. PBR roughness/metalness не понимали что прозрачные слои надо смешивать иначе.
- **Camo paterns** на нашивках не отображались - `weapon_normal_spec_palette` использует **палитровую** текстуру (256-цветный палет применяется к greyscale base), Three.js так не умеет без custom shader.
- **Optic glass** на снайперках был непрозрачный или совсем невидимый.

### Попытка 2: разные материалы по shader name

```js
const shaderMap = {
    'default':           THREE.MeshLambertMaterial,
    'normal':            THREE.MeshPhongMaterial,
    'normal_spec':       THREE.MeshPhongMaterial,
    'alpha':             THREE.MeshBasicMaterial,  // с transparent: true
    'cutout':            THREE.MeshLambertMaterial, // с alphaTest
    'glass':             THREE.MeshPhysicalMaterial, // с transmission
    // ...
};
```

Лучше - прозрачные стали полупрозрачными, cutout (сетки) перестали быть «толстыми». Но camo всё ещё ломался, и в Three.js Phong/Lambert не выглядят как RAGE - освещение по-разному.

### Попытка 3 (финальная): свой набор шейдеров

Написали 4 кастомных GLSL шейдера которые **повторяют поведение** RAGE для самых частых случаев:

| Наш shader | Замена для RAGE shaders |
|---|---|
| `gta_solid.vert/frag` | `default`, `normal`, `normal_spec` |
| `gta_alpha.vert/frag` | `alpha`, `cutout`, `decal` |
| `gta_glass.vert/frag` | `glass`, `glass_emissive` |
| `gta_palette.vert/frag` | `weapon_normal_spec_palette` (с palette texture) |

Они лежат в `Renderer/shaders/` и применяются через `RawShaderMaterial` в Three.js.

```js
// упрощённо - реальные шейдеры длиннее
import vs from './shaders/gta_palette.vert?raw';
import fs from './shaders/gta_palette.frag?raw';

const material = new THREE.RawShaderMaterial({
    vertexShader: vs,
    fragmentShader: fs,
    uniforms: {
        uDiffuse:  { value: diffuseTexture },
        uPalette:  { value: paletteTexture },
        uPaletteV: { value: paletteIndex },   // какая строка палитры (camo выбор)
    },
});
```

`uPaletteV` - это **строка** в палитровой текстуре. Для одной модели гана есть несколько вариантов camo (gold, urban, jungle, ...), все они хранятся в одном PNG как горизонтальные полосы. `uPaletteV` выбирает которую применять.

### Что получилось

| Аспект | До | После |
|---|---|---|
| Базовый ган без camo | Корректно | Корректно |
| Camo gold/urban/jungle | Серый/мутный | Корректно |
| Прозрачные части (optics, decoration) | Непрозрачно или невидимо | Корректно с blend'ом |
| Альфа-cutouts (сетки, мелкие отверстия) | «Толстые» | Тонкие, корректно |
| Стекло прицелов | Невидимо или непрозрачно | Полупрозрачное с reflection (cubemap) |

Около двух недель работы. Зато выглядит как в игре.

## Cubemap reflection

RAGE использует **environment cubemap** для отражений на металлических частях (ствол, магазин) и на стекле. Это не PBR roughness - это явный cubemap который накладывается через `cubeMapColor = textureCube(envMap, reflectDir)`.

Мы взяли **дефолтный environment** из RAGE (тот что используется для городских интерьеров) - это 6 PNG-картинок, `cubemap_default.png` через split на 6 граней.

```js
const cubeLoader = new THREE.CubeTextureLoader();
const cubemap = cubeLoader.load([
    './cubemap/px.png', './cubemap/nx.png',
    './cubemap/py.png', './cubemap/ny.png',
    './cubemap/pz.png', './cubemap/nz.png',
]);
```

Применяется во всех шейдерах что должны отражать. В UI viewer (`GlbViewerModalCanvas.tsx`) этот же cubemap используется как `scene.environment` - Three.js автоматически применит к стандартным материалам.

## Lighting

Финальная сцена для рендера превью:

```js
const scene = new THREE.Scene();

// окружение (ambient)
scene.environment = cubemap;
scene.background = null;   // прозрачный фон для PNG с alpha

// направленный свет (как солнце)
const sun = new THREE.DirectionalLight(0xffffff, 1.2);
sun.position.set(5, 10, 7);
scene.add(sun);

// fill свет с другой стороны (как небо)
const fill = new THREE.DirectionalLight(0x88aaff, 0.4);
fill.position.set(-5, -5, -3);
scene.add(fill);

// ambient (равномерный фон-свет)
const ambient = new THREE.AmbientLight(0x404060, 0.3);
scene.add(ambient);
```

Это «студийный» лайтинг - нейтральная подача, ган виден со всех сторон, нет глухих чёрных областей. В UI viewer похожая схема + `OrbitControls` чтобы пользователь сам крутил.

## Что мы НЕ делаем

- **Animation playback** - модели оружия статичные. Бронежилеты тоже не анимированы (PED skeleton не загружается).
- **Skinned mesh для одежды** - бронежилеты привязаны к PED-скелету. Мы их рендерим **без** скелета, как обычную статичную модель. Это «студийная» поза, не in-game.
- **Bloom / SSAO / postprocessing** - лишний CPU для не-критичного эффекта. Превью должно быть быстрым.

## Альтернативный путь который не пошли

Думали использовать **готовый model-viewer.dev** (Google's `<model-viewer>` web component). Он покрывает 90% случаев - PBR, environment, neutral lighting, controls. Был у нас несколько дней.

Не подошёл по двум причинам:

1. **Camo не работал** (PBR без палет);
2. **Нельзя кастомизировать** освещение под наш UI стиль (он требует exposure / env tone).

Свой viewer на R3F даёт полный контроль. Цена - 200 строк UI кода против 5 строк с `<model-viewer>`. Считаю что окупилось.

[Дальше: paths и detection →](../paths/gta-detection.md)
