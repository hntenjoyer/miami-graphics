# Headless рендер на серверной стороне

Когда админ заливает гана-пак, мы конвертируем `.ydr/.ydd` → `.glb` ([см. предыдущий раздел](glb-viewer.md)). Но для **карточек в каталоге** нужны статичные PNG-превью, не интерактивный 3D в каждой ячейке грида (это убьёт GPU).

Делаем рендер в headless Chrome через **Puppeteer + локальный Node-сервер с Three.js**. Результат - PNG-файл сохраняем в R2.

## Зачем headless

Альтернативы которые отверг:

| Подход | Минус |
|---|---|
| Server-side Three.js в Node без браузера | Three.js требует WebGL, в Node его нет (есть `node-canvas` но без шейдеров) |
| Скриншот UI через C# WebView2 → PNG | Запускать UI в admin pipeline сложно, требует видимое окно или off-screen WebView2 (нестабильно) |
| Просто использовать первое preview-изображение от автора мода | Авторы не всегда дают, не унифицировано - где-то 4К где-то 200×200 |
| Online сервис типа model-viewer.dev | Загружать наш `.glb` на внешний сервис - приватные данные + зависимость |

Headless Chrome - единственный надёжный путь. У нас есть **полноценный** WebGL контекст без видимого окна.

## Реализация

`Renderer/` - отдельная папка рядом с лаунчером. Контейнирует:

```
Renderer/
├── node.exe                ← portable Node.js, ~30 МБ
├── package.json
├── package-lock.json
├── render.html             ← страница со скриптом
├── render.js               ← bootstrap скрипт что запускается через node
└── node_modules/
    ├── puppeteer-core/
    ├── three/
    └── puppeteer/.local-chromium/  ← headless Chrome
```

При admin upload'е C# code запускает:

```cs
var psi = new ProcessStartInfo
{
    FileName = Path.Combine(rendererDir, "node.exe"),
    Arguments = "render.js " + JsonEscape(args),
    UseShellExecute = false,
    RedirectStandardOutput = true,
    WorkingDirectory = rendererDir,
};
var p = Process.Start(psi);
// читаем stdout: PNG base64 string
```

## render.html

Минимальная страница которая создаёт canvas и грузит GLB. Не отображается - Chrome работает в headless режиме:

```html title="MiamiGraphics.Core/Renderer/render.html"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Miami Graphics Renderer</title>
<style>
  html, body { margin: 0; padding: 0; background: transparent; }
  canvas { display: block; }
</style>
<script type="importmap">
{
  "imports": {
    "three": "./node_modules/three/build/three.module.js",
    "three/addons/": "./node_modules/three/examples/jsm/"
  }
}
</script>
</head>
<body>
<script type="module">
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';

// Принимает base64 GLB + размеры, возвращает dataURL PNG.
window.renderGlb = async function(glbBase64, width, height) {
    const binStr = atob(glbBase64);
    const buf = new ArrayBuffer(binStr.length);
    const view = new Uint8Array(buf);
    for (let i = 0; i < binStr.length; i++) view[i] = binStr.charCodeAt(i);

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    document.body.appendChild(canvas);

    const renderer = new THREE.WebGLRenderer({
        canvas,
        antialias: true,
        alpha: true,
        preserveDrawingBuffer: true   // важно - без этого toDataURL вернёт пустой PNG
    });
    renderer.setSize(width, height, false);
    renderer.setPixelRatio(1);
    renderer.setClearColor(0x000000, 0);
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.0;

    // ... настройка сцены, света, камеры ...
    // ... loader.parse(buf, '', resolve) ...
    // ... auto-fit камеры по bounding box ...
    renderer.render(scene, camera);

    return canvas.toDataURL('image/png');
};
</script>
</body>
</html>
```

`preserveDrawingBuffer: true` - критичная настройка. Без неё `toDataURL()` после `render()` вернёт **пустой** или последнего кадра. WebGL по умолчанию сразу после `present` очищает back buffer.

## render.js (Node side)

```js title="Renderer/render.js (упрощённо)"
const puppeteer = require('puppeteer');
const fs = require('fs');

(async () => {
    const args = JSON.parse(process.argv[2]);   // { glbPath, outPath, width, height }
    const glbB64 = fs.readFileSync(args.glbPath).toString('base64');

    const browser = await puppeteer.launch({
        headless: 'new',
        args: [
            '--no-sandbox',
            '--disable-setuid-sandbox',
            '--use-gl=swiftshader',   // software WebGL для безголовой машины без GPU
        ],
    });
    const page = await browser.newPage();
    await page.goto('file://' + __dirname + '/render.html');

    const dataUrl = await page.evaluate(
        async (b64, w, h) => await window.renderGlb(b64, w, h),
        glbB64, args.width, args.height
    );

    // dataUrl это "data:image/png;base64,iVBOR..."
    const base64 = dataUrl.replace(/^data:image\/png;base64,/, '');
    fs.writeFileSync(args.outPath, Buffer.from(base64, 'base64'));

    await browser.close();
    console.log('OK');
})();
```

`--use-gl=swiftshader` - SwiftShader это software WebGL implementation от Google. Работает без видеокарты (для виртуальных серверов или CI), и **не зависит** от драйверов конкретной GPU. Достаточен для рендера ганов и брони - текстуры умеет, шейдеры умеет, антиалиасинг да.

Чуть медленнее чем GPU-render (1-3 секунды на ствол вместо 100 мс), но в фоне на админ-stage'е это норма.

## Bootstrapper

Renderer не приходит с инсталлером, это **on-demand** download. Он 73 МБ (node.exe + node_modules), а 99% юзеров никогда не админят.

Полный bootstrap делится на два шага. Первый качает архив с R2, второй докачивает Chromium на машину админа.

### Шаг 1: распаковка Renderer.zip

```cs title="MiamiGraphics.Shell/Services/RendererBootstrapper.cs"
public sealed class RendererBootstrapper
{
    private const string RendererZipUrl =
        "https://miamigraphicsstorage.uk/releases/MiamiGraphicsRenderer_5.zip";
    private const string RendererSha256 =
        "87C1EB43A4AF2CAF510329D42E64EB86841702E6A23C5E8DE74C1B1D3486FAB3";
    private const string ExpectedRendererVersion = "5";

    public async Task<EnsureResult> EnsureInstalledAsync(...)
    {
        if (IsAlreadyInstalled())
            return new EnsureResult(true, alreadyInstalled: true, ...);

        if (IsRendererFilesInstalled() && !IsChromiumInstalled())
        {
            var chromeOnly = await EnsureChromiumInstalledAsync(progress, ct);
            return chromeOnly.Success
                ? new EnsureResult(true, false, RendererDir, 0, null)
                : new EnsureResult(false, false, RendererDir, 0, chromeOnly.ErrorMessage);
        }

        // download + verify SHA + extract
        var chromeResult = await EnsureChromiumInstalledAsync(progress, ct);
        if (!chromeResult.Success)
            return new EnsureResult(false, ..., chromeResult.ErrorMessage);

        return new EnsureResult(true, false, RendererDir, downloaded, null);
    }
}
```

### Шаг 2: установка chrome-headless-shell

Puppeteer 23.x не качает Chromium при `import` библиотеки. Бинарь ожидается в `~/.cache/puppeteer/` (его кладёт `npm install` postinstall). В zip'е мы его не несём, иначе архив раздулся бы до 200+ МБ.

Поэтому после распаковки бутстрапер запускает `node node_modules/puppeteer/install.mjs` локально на машине админа, перенаправив cache в `Renderer/.cache/puppeteer/`:

```cs
public async Task<ChromiumInstallResult> EnsureChromiumInstalledAsync(...)
{
    if (IsChromiumInstalled()) return new(true, true, null);

    var psi = new ProcessStartInfo
    {
        FileName = NodeExePath,
        WorkingDirectory = RendererDir,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };
    psi.ArgumentList.Add(PuppeteerInstallScript);
    psi.Environment["PUPPETEER_CACHE_DIR"] = PuppeteerCacheDir;

    using var proc = Process.Start(psi);
    // ... читаем stdout async (ReadLineAsync), парсим Downloading/Extracting для прогресса ...
    var exited = proc.WaitForExit(300_000);
    if (!exited) { proc.Kill(entireProcessTree: true); return new(false, false, "timeout"); }

    if (proc.ExitCode != 0) return new(false, false, $"exit={proc.ExitCode}: {stderrTail}");
    if (!IsChromiumInstalled()) return new(false, false, "install.mjs ok, но shell не найден");

    return new(true, false, null);
}
```

`install.mjs` качает `chrome-headless-shell` (~120 MB) с googleapis storage и распаковывает в `PUPPETEER_CACHE_DIR/chrome-headless-shell/<version>/`. После этого `puppeteer.launch()` находит его автоматически через стандартный path-resolver puppeteer'а.

### Шаг 3: render.js предпочитает bundled

```js title="MiamiGraphics.Core/Renderer/render.js"
function findBundledHeadlessShell() {
    const cacheDir = process.env.PUPPETEER_CACHE_DIR
        || path.join(__dirname, '.cache', 'puppeteer');
    const shellRoot = path.join(cacheDir, 'chrome-headless-shell');
    if (!fs.existsSync(shellRoot)) return null;
    for (const v of fs.readdirSync(shellRoot)) {
        const found = findExecutable(path.join(shellRoot, v), 'chrome-headless-shell.exe');
        if (found) return found;
    }
    return null;
}

const bundledHeadlessShell = findBundledHeadlessShell();
const systemBrowser = probeAllBrowsers().find(r => r.found)?.found;
const selectedBrowser = bundledHeadlessShell || systemBrowser;
```

Bundled Chromium идёт первым в приоритете. System Edge / Chrome / WebView2 остаются только как fallback на случай если что-то пошло не так с puppeteer install.

### Зачем выделять Chromium в отдельный шаг

В версии 1.0.12 рендер шёл через system Edge. Это работало для тестового рендера (одна модель, маленький viewport), но падало при пакетной заливке гана-пака:

- Edge с залоченным дефолтным профилем не даёт puppeteer'у создать второй headless экземпляр.
- Defender real-time scan на свежеустановленный `chrome.exe` в `%LocalAppData%\Miami.Graphics\app-*\` добавляет ~5 секунд на cold-start. При 5 последовательных запусках Edge timeout'ится на handshake.
- Версия системного Edge непредсказуема (Edge 128 / 131 / 142), CDP-API между версиями немного отличается.

Bundled `chrome-headless-shell` решает все три проблемы. Версия зафиксирована (та что в puppeteer 23.x), профиль изолирован, AV scan происходит один раз при install.mjs, не на каждый launch.

### Probe учитывает Chromium

```cs
public static bool IsChromiumInstalled()
{
    if (!Directory.Exists(ChromeHeadlessShellDir)) return false;
    foreach (var sub in Directory.EnumerateDirectories(ChromeHeadlessShellDir))
    {
        if (Directory.EnumerateFiles(sub, "chrome-headless-shell.exe", SearchOption.AllDirectories).Any())
            return true;
    }
    return false;
}
```

`Probe()` теперь возвращает `ChromiumInstalled: bool`. `IsUsable` = всё прежнее + Chromium. UI в `RendererHealthCard` показывает отдельную строку «Chromium (chrome-headless-shell)» в чек-листе. Если её нет, `actionableHint` подсказывает админу нажать «Установить».

UI триггерит установку Renderer'а в двух местах:

- Когда админ заходит в **Admin → Guns** (там нужен Renderer для preview generation).
- В `AdminGunpackUploadAsync` перед добавлением заявки в очередь: `EnsureRendererReadyForGunpackUploadAsync` проверяет наличие Renderer'а и Chromium, при отсутствии качает оба.

Обычные юзеры эти ~200 МБ не получают.

## Сборка Renderer.zip

`MiamiGraphicsRenderer_5.zip` собирается один раз вручную:

1. Создаём папку `Renderer/`.
2. Кладём portable Node.js Windows binary как `node.exe`.
3. `npm install` (по `package.json` ставит `puppeteer@^23.10.4` + `three@^0.169.0`).
4. Пишем в `version.txt` строку `5`.
5. Кладём `render.html`, `render.js`.
6. Зипуем `Renderer/`, считаем SHA-256.
7. Льём на R2: `https://miamigraphicsstorage.uk/releases/MiamiGraphicsRenderer_5.zip`.
8. Обновляем константы `RendererZipUrl`, `RendererSha256`, `ExpectedRendererVersion` в `RendererBootstrapper.cs`.

Chromium **внутрь zip'а не пакуется**. Он качается отдельно через `install.mjs` на машине админа (см. выше). Это держит zip ~73 МБ и снимает нагрузку с нашего R2.

Zip переcобирается редко: раз в полгода когда нужно обновить Three.js или Puppeteer.

[Дальше: история шейдеров →](shaders-history.md)
