import puppeteer from 'puppeteer';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const args = process.argv.slice(2);
if (args.length < 2) {
    console.error('Usage: node render_armor.js input.glb output.webp [w] [h]');
    process.exit(1);
}

const inputGlb = path.resolve(args[0]);
const outputWebp = path.resolve(args[1]);
const width = parseInt(args[2] ?? '1024', 10);
const height = parseInt(args[3] ?? '1024', 10);

if (!fs.existsSync(inputGlb)) {
    console.error('GLB not found: ' + inputGlb);
    process.exit(1);
}
const glbBase64 = fs.readFileSync(inputGlb).toString('base64');

const htmlPath = path.join(__dirname, 'render_armor.html');
const htmlUrl = 'file:///' + htmlPath.split(path.sep).join('/');

const browser = await puppeteer.launch({ headless: 'shell', pipe: true });
const page = await browser.newPage();
page.on('console', m => console.log('[chromium]', m.text()));
await page.goto(htmlUrl);
await page.waitForFunction(() => window.__rendererReady === true, { timeout: 30000 });

const dataUrl = await page.evaluate(
    async (b64, w, h) => window.renderGlb(b64, w, h),
    glbBase64, width, height);

const base64 = dataUrl.replace(/^data:image\/\w+;base64,/, '');
fs.writeFileSync(outputWebp, Buffer.from(base64, 'base64'));
console.log('[render_armor.js] saved ' + outputWebp +
            ' (' + (fs.statSync(outputWebp).size / 1024).toFixed(1) + ' KB)');

await browser.close();
