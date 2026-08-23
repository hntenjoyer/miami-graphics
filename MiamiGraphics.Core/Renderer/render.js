import puppeteer from 'puppeteer';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const rawArgs = process.argv.slice(2);
const flagArgs = rawArgs.filter(a => a.startsWith('--'));
const args = rawArgs.filter(a => !a.startsWith('--'));
if (args.length < 2) {
    console.error('Usage: node render.js input.glb output.webp [width] [height] [view]');
    console.error('   or: node render.js input.glb outDir --views=front,back,left,right,fl,fr [width] [height]');
    process.exit(1);
}

const inputGlb = path.resolve(args[0]);
const outputPng = path.resolve(args[1]);
const width = parseInt(args[2] ?? '1024', 10);
const height = parseInt(args[3] ?? '512', 10);
const singleView = args[4] ?? null;

const viewsFlag = flagArgs.find(f => f.startsWith('--views'));
const multiViews = viewsFlag
    ? (viewsFlag.split('=')[1] || '').split(',').map(s => s.trim()).filter(Boolean)
    : null;

console.log('[render.js] Input:  ' + inputGlb);
console.log('[render.js] Output: ' + outputPng);
console.log('[render.js] Size:   ' + width + 'x' + height);
if (multiViews) console.log('[render.js] Views:  ' + multiViews.join(', '));
else if (singleView) console.log('[render.js] View:   ' + singleView);

if (!fs.existsSync(inputGlb)) {
    console.error('[render.js] Input GLB not found: ' + inputGlb);
    process.exit(1);
}

const glbBase64 = fs.readFileSync(inputGlb).toString('base64');
console.log('[render.js] GLB size: ' + (glbBase64.length * 0.75 / 1024).toFixed(1) + ' KB');

const htmlFile = singleView === 'map' ? 'render_map.html' : 'render.html';
const htmlPath = path.join(__dirname, htmlFile);
if (!fs.existsSync(htmlPath)) {
    console.error('[render.js] ' + htmlFile + ' not found at ' + htmlPath);
    process.exit(1);
}
const htmlUrl = 'file:///' + htmlPath.replace(/\\/g, '/');
console.log('[render.js] HTML: ' + htmlUrl);

function findExecutable(root, name) {
    if (!fs.existsSync(root)) return null;
    try {
        const entries = fs.readdirSync(root, { withFileTypes: true });
        for (const entry of entries) {
            const full = path.join(root, entry.name);
            if (entry.isFile() && entry.name.toLowerCase() === name.toLowerCase()) return full;
            if (entry.isDirectory()) {
                const found = findExecutable(full, name);
                if (found) return found;
            }
        }
    } catch { }
    return null;
}

function findFirstExisting(paths) {
    for (const p of paths) {
        if (!p) continue;
        try { if (fs.existsSync(p)) return p; } catch { }
    }
    return null;
}

function findSystemEdgeWebView2() {
    const programFiles  = process.env['ProgramFiles']      || 'C:\\Program Files';
    const programFiles86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';
    const localAppData  = process.env['LOCALAPPDATA']      || '';
    const userProfile   = process.env['USERPROFILE']       || '';
    const roots = [
        path.join(programFiles86, 'Microsoft', 'EdgeWebView', 'Application'),
        path.join(programFiles,   'Microsoft', 'EdgeWebView', 'Application'),
        path.join(localAppData,   'Microsoft', 'EdgeWebView', 'Application'),
        userProfile ? path.join(userProfile, 'AppData', 'Local', 'Microsoft', 'EdgeWebView', 'Application') : null,
    ].filter(Boolean);
    for (const root of roots) {
        const found = findExecutable(root, 'msedgewebview2.exe');
        if (found) return found;
    }
    return null;
}

function findSystemEdge() {
    const programFiles  = process.env['ProgramFiles']      || 'C:\\Program Files';
    const programFiles86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';
    return findFirstExisting([
        path.join(programFiles86, 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
        path.join(programFiles,   'Microsoft', 'Edge', 'Application', 'msedge.exe'),
    ]);
}

function findSystemChrome() {
    const programFiles  = process.env['ProgramFiles']      || 'C:\\Program Files';
    const programFiles86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';
    const localAppData  = process.env['LOCALAPPDATA']      || '';
    return findFirstExisting([
        path.join(programFiles,   'Google', 'Chrome', 'Application', 'chrome.exe'),
        path.join(programFiles86, 'Google', 'Chrome', 'Application', 'chrome.exe'),
        localAppData ? path.join(localAppData, 'Google', 'Chrome', 'Application', 'chrome.exe') : null,
    ]);
}

function probeAllBrowsers() {
    const results = [];
    const env = process.env.PUPPETEER_EXECUTABLE_PATH;
    results.push({ source: 'env:PUPPETEER_EXECUTABLE_PATH', found: env || null });

    const edge = findSystemEdge();
    results.push({ source: 'system Edge (msedge.exe)',      found: edge });
    const chrome = findSystemChrome();
    results.push({ source: 'system Chrome',                 found: chrome });

    const bundled1 = findExecutable(path.resolve(__dirname, '..', 'WebView2Runtime'), 'msedgewebview2.exe');
    results.push({ source: 'bundled ../WebView2Runtime',    found: bundled1 });
    const bundled2 = findExecutable(path.resolve(__dirname, 'WebView2Runtime'), 'msedgewebview2.exe');
    results.push({ source: 'bundled ./WebView2Runtime',     found: bundled2 });

    const ewv = findSystemEdgeWebView2();
    results.push({ source: 'system Edge WebView2 (last-resort, often fails)', found: ewv });

    return results;
}

function findBundledHeadlessShell() {
    const cacheDir = process.env.PUPPETEER_CACHE_DIR
        || path.join(__dirname, '.cache', 'puppeteer');
    const shellRoot = path.join(cacheDir, 'chrome-headless-shell');
    if (!fs.existsSync(shellRoot)) return null;
    try {
        const versions = fs.readdirSync(shellRoot, { withFileTypes: true })
            .filter(d => d.isDirectory()).map(d => d.name);
        for (const v of versions) {
            const found = findExecutable(path.join(shellRoot, v), 'chrome-headless-shell.exe');
            if (found) return found;
        }
    } catch { }
    return null;
}

const bundledHeadlessShell = findBundledHeadlessShell();
console.log('[render.js] bundled chrome-headless-shell: ' + (bundledHeadlessShell || 'NOT FOUND'));

const browserProbe = probeAllBrowsers();
console.log('[render.js] Browser probe:');
for (const r of browserProbe) {
    console.log('  ' + (r.found ? 'OK  ' : '--  ') + r.source + (r.found ? ' = ' + r.found : ''));
}

const systemBrowser = browserProbe.find(r => r.found)?.found || null;
const selectedBrowser = bundledHeadlessShell || systemBrowser;

if (selectedBrowser) {
    console.log('[render.js] Browser selected: ' + selectedBrowser
        + (bundledHeadlessShell ? ' (bundled)' : ' (system fallback)'));
} else {
    console.error('[render.js] FATAL: No browser found anywhere. ' +
                  'Install Microsoft Edge (https://microsoft.com/edge) or Google Chrome and retry. ' +
                  'Puppeteer cannot launch without a Chromium-based browser executable.');
    process.exit(2);
}

const os = await import('os');
const userDataDir = path.join(os.tmpdir(), 'puppeteer-' + Date.now() + '-' + Math.random().toString(36).slice(2, 8));
console.log('[render.js] User data dir: ' + userDataDir);

const commonArgs = [
    '--no-sandbox',
    '--disable-setuid-sandbox',
    '--disable-gpu',
    '--disable-dev-shm-usage',
    '--no-first-run',
    '--no-default-browser-check',
    '--no-zygote',
    '--disable-background-networking',
    '--disable-extensions',
    '--disable-features=TranslateUI,BlinkGenPropertyTrees',
    '--user-data-dir=' + userDataDir,
    '--allow-file-access-from-files',
    '--disable-web-security',
];

async function tryLaunch(opts, label) {
    try {
        console.log('[render.js] launch attempt: ' + label);
        const b = await puppeteer.launch(opts);
        console.log('[render.js] launch OK: ' + label);
        return b;
    } catch (e) {
        console.error('[render.js] launch FAIL (' + label + '): ' + e.message);
        return null;
    }
}

const baseOpts = {
    ...(selectedBrowser ? { executablePath: selectedBrowser } : {}),
    args: commonArgs,
    dumpio: true,
    timeout: 30000,
};

let browser = await tryLaunch({ ...baseOpts, headless: 'shell', pipe: true }, 'headless=shell pipe=true');
if (!browser) browser = await tryLaunch({ ...baseOpts, headless: true, pipe: true }, 'headless=true pipe=true');
if (!browser) browser = await tryLaunch({ ...baseOpts, headless: true, pipe: false }, 'headless=true pipe=false');
if (!browser) browser = await tryLaunch({ ...baseOpts, headless: 'new' }, 'headless=new (legacy)');

if (!browser) {
    console.error('[render.js] FATAL: все варианты puppeteer.launch упали. Закрой Microsoft Edge ' +
                  '(если открыт) и попробуй снова - часто Edge держит lock на профиле.');
    process.exit(3);
}

try {
    const page = await browser.newPage();
    page.on('console', msg => console.log('[chromium] ' + msg.text()));
    page.on('pageerror', err => console.error('[chromium/err] ' + err.message));
    await page.setViewport({ width, height });

    await page.goto(htmlUrl, { waitUntil: 'load' });

    await page.waitForFunction(() => window.__rendererReady === true, { timeout: 15000 });

    async function renderOne(view) {
        const dataUrl = view === 'map'
            ? await page.evaluate(
                async (b64, w, h) => await window.renderMapGlb(b64, w, h),
                glbBase64, width, height)
            : await page.evaluate(
                async (b64, w, h, v) => await window.renderGlb(b64, w, h, v),
                glbBase64, width, height, view);
        if (typeof dataUrl !== 'string' || !dataUrl.startsWith('data:image/webp;base64,')) {
            throw new Error('renderGlb did not return a WebP dataURL (view=' + (view || 'auto') + ')');
        }
        return Buffer.from(dataUrl.substring('data:image/webp;base64,'.length), 'base64');
    }

    if (multiViews) {
        fs.mkdirSync(outputPng, { recursive: true });
        for (const v of multiViews) {
            const buf = await renderOne(v);
            const f = path.join(outputPng, v + '.webp');
            fs.writeFileSync(f, buf);
            console.log('[render.js] Saved: ' + f + ' (' + (buf.length / 1024).toFixed(1) + ' KB)');
        }
    } else {
        const buf = await renderOne(singleView);
        fs.mkdirSync(path.dirname(outputPng), { recursive: true });
        fs.writeFileSync(outputPng, buf);
        console.log('[render.js] Saved: ' + outputPng + ' (' + (buf.length / 1024).toFixed(1) + ' KB)');
    }
} catch (err) {
    console.error('[render.js] FAILED: ' + (err && err.stack ? err.stack : err));
    process.exit(2);
} finally {
    try { await browser.close(); } catch { }
    try { fs.rmSync(userDataDir, { recursive: true, force: true }); } catch { }
}
