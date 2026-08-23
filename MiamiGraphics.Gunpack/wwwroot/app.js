import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

const $ = (id) => document.getElementById(id);
const enc = encodeURIComponent;
const canvas3d = $('canvas3d');
const wrap = $('canvas3d-wrap');
const tex2d = $('tex2d');

let currentPack = null, currentGun = null, detail = null;
let mode = 'orbit';
let prevPaintMode = 'brush';
let activeTex = null;
const texState = new Map();
let matsByTex = new Map();
let gunMeshes = [];
let meshTexName = new Map();
let meshBucket = new Map();
let uvOverlayCache = new Map();
let layerIdSeq = 1;
let selLayerId = null;
let overlayOnTop = true;

const brush = { color: '#ff2d95', size: 40, alpha: 1, style: 'normal' };
let attachKind = 'pendant', attachSizeCm = 10, attachDepth = 0.18;
const anim = { mode: 'uv', sizeCm: 12, depth: 0.18, scrollU: 1, scrollV: 0,
  axis: 'z', amplitude: 20, period: 2.0, bone: '' };
let animPreview = null;
const animClock = new THREE.Clock();
let fillTolerance = 48;
const STAMP_MODES = new Set(['sticker', 'attach']);
const PAINT_MODES = new Set(['brush', 'eraser', 'fill', 'picker', 'alpha']);

const renderer = new THREE.WebGLRenderer({ canvas: canvas3d, antialias: true, alpha: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.outputColorSpace = THREE.SRGBColorSpace;
const maxAniso = renderer.capabilities.getMaxAnisotropy();

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(35, 1, 0.01, 100);
camera.position.set(0, 0, 2.4);
scene.environment = new THREE.PMREMGenerator(renderer).fromScene(new RoomEnvironment(), 0.04).texture;
scene.add(new THREE.AmbientLight(0xffffff, 0.3));
for (const [p, i] of [[[1.5, 1.5, 1.5], 1.5], [[-1.5, 0, 0.75], 0.5], [[0, 0.75, -2.25], 0.7]]) {
  const dl = new THREE.DirectionalLight(0xffffff, i); dl.position.set(...p); scene.add(dl);
}
const controls = new OrbitControls(camera, canvas3d);
controls.enableDamping = true; controls.dampingFactor = 0.08;

let model = null;
const raycaster = new THREE.Raycaster();

function resizeViewport() {
  const w = wrap.clientWidth, h = wrap.clientHeight;
  if (!w || !h) return;
  renderer.setSize(w, h, false);
  camera.aspect = w / h; camera.updateProjectionMatrix();
}
new ResizeObserver(resizeViewport).observe(wrap);
addEventListener('resize', resizeViewport);
requestAnimationFrame(resizeViewport);

(function loop() { requestAnimationFrame(loop); if (window.__pauseRender) return; controls.update(); updateAnimPreview(); renderer.render(scene, camera); })();

function updateAnimPreview() {
  if (!animPreview) return;
  const t = animClock.getElapsedTime();
  const m = animPreview.meta, T = Math.max(0.05, m.periodSec || 2);
  const phase = (t % T) / T;
  if (m.mode === 'uv') {
    const ox = (m.scrollU || 0) * phase, oy = (m.scrollV || 0) * phase;
    for (const mat of animPreview.materials) {
      for (const map of [mat.map, mat.emissiveMap]) {
        if (!map) continue;
        map.wrapS = map.wrapT = THREE.RepeatWrapping;
        map.offset.set(ox, oy);
      }
    }
  } else {
    const ax = animPreview.axisGlb;
    let ang;
    if (m.mode === 'spin') ang = phase * Math.PI * 2;
    else ang = (m.amplitudeDeg || 0) * Math.PI / 180 * Math.sin(phase * Math.PI * 2);
    animPreview.obj.quaternion.setFromAxisAngle(ax, ang);
  }
}
function clearAnimPreview() {
  if (!animPreview) return;
  animPreview.obj.parent?.remove(animPreview.obj);
  try { disposeTree(animPreview.obj); } catch { }
  animPreview = null;
}

const setStatus = (t) => $('status').textContent = t;
function log(msg, cls = '') {
  const d = document.createElement('div');
  if (cls) d.className = cls;
  d.textContent = `${new Date().toLocaleTimeString()} ${msg}`;
  $('log').prepend(d);
}
const spin = (on) => $('spinner').hidden = !on;
const api = (p) => fetch(p).then(r => { if (!r.ok) throw new Error(`${r.status} ${p}`); return r; });
const texUrl = (name, size) => `/api/texture?pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}${size ? `&size=${size}` : ''}&v=${Date.now()}`;
const makeCanvas = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };
const copyCanvas = (s) => { const c = makeCanvas(s.width, s.height); c.getContext('2d').drawImage(s, 0, 0); return c; };
function hexToRgb(hex) { const n = parseInt(hex.slice(1), 16); return [n >> 16 & 255, n >> 8 & 255, n & 255]; }

let allPacks = [];
async function loadCatalog() {
  allPacks = await (await api('/api/packs')).json();
  renderCatalog('');
}
function renderCatalog(filter) {
  const root = $('packs'); root.innerHTML = '';
  const f = filter.trim().toLowerCase();
  for (const p of allPacks) {
    if (f && !p.name.toLowerCase().includes(f)) continue;
    const pd = document.createElement('div');
    pd.className = 'pack' + (f ? ' open' : '');
    pd.innerHTML = `<div class="pack-head"><span>${p.name}</span><span class="cnt">${p.guns.length}</span></div><div class="guns"></div>`;
    pd.querySelector('.pack-head').onclick = () => pd.classList.toggle('open');
    const guns = pd.querySelector('.guns');
    for (const g of p.guns) {
      const gi = document.createElement('div');
      gi.className = 'gun-item';
      gi.dataset.pack = p.name; gi.dataset.gun = g.name;
      gi.innerHTML = `<span>${g.name}</span>${g.edited ? '<span class="badge">EDIT</span>' : ''}`;
      gi.onclick = () => loadGun(p.name, g.name);
      guns.appendChild(gi);
    }
    root.appendChild(pd);
  }
}
$('search').oninput = (e) => renderCatalog(e.target.value);

async function loadGun(pack, gun) {
  spin(true); setStatus(`загрузка ${pack}/${gun}…`);
  try {
    detail = await (await api(`/api/gun?pack=${enc(pack)}&gun=${enc(gun)}`)).json();
    currentPack = pack; currentGun = gun;
    if (pendingObj) cancelImported();
    clearAnimPreview();
    for (const st of texState.values()) { st.tex3?.dispose?.(); st.chrome?.tex?.dispose?.(); }
    texState.clear(); matsByTex.clear(); meshTexName.clear(); meshBucket.clear(); uvOverlayCache.clear();
    activeTex = null; selLayerId = null;

    const glb = await new Promise((res, rej) =>
      new GLTFLoader().load(`/api/glb?pack=${enc(pack)}&gun=${enc(gun)}&v=${Date.now()}`, res, undefined, rej));
    if (model) { scene.remove(model); disposeTree(model); }
    model = glb.scene; scene.add(model);

    gunMeshes = [];
    model.traverse(o => {
      if (!o.isMesh) return;
      gunMeshes.push(o);
      for (const m of (Array.isArray(o.material) ? o.material : [o.material])) {
        const mi = /^Mat_(\d+)$/.exec(m.name || ''); if (!mi) continue;
        const sh = detail.shaders[+mi[1]]; if (!sh || !sh.diffuse) continue;
        (matsByTex.get(sh.diffuse) || matsByTex.set(sh.diffuse, []).get(sh.diffuse)).push(m);
        meshTexName.set(o.uuid, sh.diffuse);
        meshBucket.set(o.uuid, sh.bucket ?? 0);
      }
    });

    frameCamera();
    buildTexList();
    $('dropHint').style.display = 'none';
    $('gunLabel').textContent = `${pack} / ${gun} · ${detail.verts.toLocaleString()} верт · ${detail.textures.length} текстур${detail.edited ? ' · ПРАВЛЕН' : ''}`;
    $('btnReset').disabled = !detail.edited;
    $('btnSave').disabled = true; $('btnUndo').disabled = true;
    document.querySelectorAll('.gun-item').forEach(el => el.classList.toggle('active', el.dataset.pack === pack && el.dataset.gun === gun));

    const best = [...matsByTex.keys()]
      .map(n => detail.textures.find(t => t.name.toLowerCase() === n.toLowerCase()))
      .filter(Boolean).sort((a, b) => b.width * b.height - a.width * a.height)[0];
    if (best) await selectTexture(best.name);

    rebuildLayerList(); renderOpts(); updateHud();
    setStatus('готов'); log(`загружен ${pack}/${gun}`, 'ok');
  } catch (e) { setStatus('ошибка'); log(`загрузка: ${e.message}`, 'err'); }
  finally { spin(false); }
}

function frameCamera() {
  const box = new THREE.Box3().setFromObject(model);
  const size = box.getSize(new THREE.Vector3()), center = box.getCenter(new THREE.Vector3());
  const dist = Math.max(size.x, size.y, size.z) / (2 * Math.tan((camera.fov * Math.PI / 180) / 2)) * 1.25;
  camera.position.set(center.x, center.y, center.z + dist);
  camera.near = dist / 100; camera.far = dist * 100; camera.updateProjectionMatrix();
  controls.target.copy(center); controls.update();
}
function disposeTree(root) {
  root.traverse(o => {
    o.geometry?.dispose?.();
    for (const m of (Array.isArray(o.material) ? o.material : [o.material])) {
      if (!m) continue;
      for (const s of ['map', 'normalMap', 'emissiveMap', 'metalnessMap', 'roughnessMap']) m[s]?.dispose?.();
      m.dispose?.();
    }
  });
}

function loadImage(url) {
  return new Promise((res, rej) => {
    const img = new Image();
    img.onload = () => res(img);
    img.onerror = () => rej(new Error('img load failed: ' + url.slice(0, 80)));
    img.src = url;
  });
}
async function ensureTexState(name) {
  if (texState.has(name)) return texState.get(name);
  const img = await loadImage(texUrl(name));
  const orig = makeCanvas(img.naturalWidth, img.naturalHeight);
  orig.getContext('2d').drawImage(img, 0, 0);
  const base = copyCanvas(orig), comp = copyCanvas(orig);
  const tex3 = new THREE.CanvasTexture(comp);
  tex3.flipY = false;
  tex3.colorSpace = THREE.SRGBColorSpace;
  tex3.wrapS = tex3.wrapT = THREE.ClampToEdgeWrapping;
  tex3.generateMipmaps = true;
  tex3.minFilter = THREE.LinearMipmapLinearFilter;
  tex3.magFilter = THREE.LinearFilter;
  tex3.anisotropy = maxAniso;
  const st = {
    name, orig, base, comp,
    ctxB: base.getContext('2d', { willReadFrequently: true }), ctxC: comp.getContext('2d'),
    layers: [], tex3, chrome: null, dirty: false, undo: [], origPattern: null, alphaOn: false,
    glass: { on: false, color: '#7fdfff', opacity: 0.4 },
  };
  texState.set(name, st);
  return st;
}
const activeSt = () => activeTex ? texState.get(activeTex) : null;
const selLayer = () => activeSt()?.layers.find(l => l.id === selLayerId) || null;

async function selectTexture(name) {
  const st = await ensureTexState(name);
  activeTex = name; selLayerId = null;
  for (const m of (matsByTex.get(name) || [])) {
    m.map = st.tex3;
    if (m.emissiveMap) m.emissiveMap = st.tex3;
    if (st.chrome) { m.metalnessMap = st.chrome.tex; m.metalness = 1; }
    if (st.alphaOn) { m.transparent = true; m.side = THREE.DoubleSide; m.depthWrite = false; }
    m.needsUpdate = true;
  }
  if (st.glass?.on) applyGlass(st);
  document.querySelectorAll('.tex-item').forEach(el => el.classList.toggle('active', el.dataset.name === name));
  rebuildLayerList(); updateHud(); draw2D();
}

function drawLayer(ctx, l) {
  ctx.save();
  ctx.globalAlpha = l.opacity;
  ctx.globalCompositeOperation = l.blend || 'source-over';
  ctx.translate(l.x, l.y); ctx.rotate(l.rot);
  if (l.type === 'stamp') ctx.drawImage(l.img, -l.w / 2, -l.h / 2, l.w, l.h);
  else {
    ctx.font = `${l.bold ? '900' : '400'} ${l.size}px ${l.font}`;
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    if (l.outlineW > 0) { ctx.lineWidth = l.outlineW; ctx.strokeStyle = l.outline; ctx.lineJoin = 'round'; ctx.strokeText(l.text, 0, 0); }
    ctx.fillStyle = l.color; ctx.fillText(l.text, 0, 0);
  }
  ctx.restore();
}
function composite(st) {
  st.ctxC.clearRect(0, 0, st.comp.width, st.comp.height);
  st.ctxC.drawImage(st.base, 0, 0);
  for (const l of st.layers) drawLayer(st.ctxC, l);
  st.tex3.needsUpdate = true;
}
function markDirty(st) {
  if (st.dirty) return;
  st.dirty = true; $('btnSave').disabled = false;
  document.querySelectorAll('.tex-item').forEach(el => { const s = texState.get(el.dataset.name); el.querySelector('.dirty').hidden = !(s && s.dirty); });
}
function afterEdit(st) { composite(st); markDirty(st); draw2D(); }

let checkerPat = null;
function getChecker(ctx) {
  if (checkerPat) return checkerPat;
  const c = makeCanvas(16, 16), x = c.getContext('2d');
  x.fillStyle = '#12151d'; x.fillRect(0, 0, 16, 16);
  x.fillStyle = '#1c2130'; x.fillRect(0, 0, 8, 8); x.fillRect(8, 8, 8, 8);
  return (checkerPat = ctx.createPattern(c, 'repeat'));
}
let draw2DQueued = false;
function draw2D() {
  if (draw2DQueued) return; draw2DQueued = true;
  requestAnimationFrame(() => {
    draw2DQueued = false;
    const st = activeSt(); if (!st) return;
    const w = 512, h = Math.round(w * st.comp.height / st.comp.width);
    if (tex2d.width !== w || tex2d.height !== h) { tex2d.width = w; tex2d.height = h; }
    const ctx = tex2d.getContext('2d');
    ctx.imageSmoothingQuality = 'high';
    ctx.fillStyle = getChecker(ctx); ctx.fillRect(0, 0, w, h);
    ctx.drawImage(st.comp, 0, 0, w, h);
    if ($('uvOverlay').checked) ctx.drawImage(getUvOverlay(activeTex), 0, 0, w, h);
    const l = selLayer();
    if (l) drawGizmo(ctx, l, w / st.comp.width);
    if (panel2dCursor && PAINT_MODES.has(mode) && mode !== 'picker' && mode !== 'fill') {
      const s = w / st.comp.width, rr = brush.size * s / 2;
      ctx.beginPath(); ctx.arc(panel2dCursor.x * s, panel2dCursor.y * s, Math.max(2, rr), 0, 7);
      ctx.strokeStyle = 'rgba(45,212,255,.9)'; ctx.lineWidth = 1.5; ctx.stroke();
    }
  });
}
let panel2dCursor = null;
tex2d.addEventListener('pointermove', (ev) => {
  const st = activeSt(); if (!st) return;
  const r = tex2d.getBoundingClientRect();
  panel2dCursor = { x: (ev.clientX - r.left) / r.width * st.comp.width, y: (ev.clientY - r.top) / r.height * st.comp.height };
  if (!painting2d && !gizmoDrag) draw2D();
});
tex2d.addEventListener('pointerleave', () => { panel2dCursor = null; draw2D(); });
function drawGizmo(ctx, l, s) {
  const cx = l.x * s, cy = l.y * s, hw = l.w * s / 2, hh = l.h * s / 2;
  ctx.save(); ctx.translate(cx, cy); ctx.rotate(l.rot);
  ctx.strokeStyle = '#2dd4ff'; ctx.lineWidth = 1.5; ctx.setLineDash([5, 4]);
  ctx.strokeRect(-hw, -hh, hw * 2, hh * 2); ctx.setLineDash([]);
  ctx.fillStyle = '#2dd4ff';
  for (const [x, y] of [[-hw, -hh], [hw, -hh], [hw, hh], [-hw, hh]]) ctx.fillRect(x - 4, y - 4, 8, 8);
  ctx.restore();
}
function getUvOverlay(name) {
  if (uvOverlayCache.has(name)) return uvOverlayCache.get(name);
  const c = makeCanvas(1024, 1024), ctx = c.getContext('2d');
  ctx.strokeStyle = 'rgba(45,212,255,0.35)'; ctx.lineWidth = 1;
  for (const mesh of gunMeshes) {
    if (meshTexName.get(mesh.uuid) !== name) continue;
    const uv = mesh.geometry.attributes.uv, idx = mesh.geometry.index; if (!uv) continue;
    ctx.beginPath();
    const n = idx ? idx.count : uv.count;
    for (let i = 0; i + 2 < n; i += 3) {
      const a = idx ? idx.getX(i) : i, b = idx ? idx.getX(i + 1) : i + 1, cc = idx ? idx.getX(i + 2) : i + 2;
      let ax = uv.getX(a), ay = uv.getY(a), bx = uv.getX(b), by = uv.getY(b), cx = uv.getX(cc), cy = uv.getY(cc);
      const ox = Math.floor((ax + bx + cx) / 3), oy = Math.floor((ay + by + cy) / 3);
      ax -= ox; bx -= ox; cx -= ox; ay -= oy; by -= oy; cy -= oy;
      ctx.moveTo(ax * 1024, ay * 1024); ctx.lineTo(bx * 1024, by * 1024); ctx.lineTo(cx * 1024, cy * 1024); ctx.lineTo(ax * 1024, ay * 1024);
    }
    ctx.stroke();
  }
  uvOverlayCache.set(name, c); return c;
}
$('uvOverlay').onchange = draw2D;

function buildTexList() {
  const root = $('texList'); root.innerHTML = '';
  $('texCount').textContent = `${detail.textures.length}`;
  for (const t of detail.textures) {
    const paintable = matsByTex.has(t.name);
    const el = document.createElement('div');
    el.className = 'tex-item'; el.dataset.name = t.name;
    el.innerHTML = `<img loading="lazy" src="${texUrl(t.name, 64)}">
      <div style="min-width:0"><div class="nm">${t.name}</div>
      <div class="dm">${t.width}×${t.height}${paintable ? '' : ' · не на модели'}</div></div>
      <span class="dirty" hidden>●</span>`;
    el.onclick = () => selectTexture(t.name);
    root.appendChild(el);
  }
}

function addLayer(st, layer) {
  pushUndo(st, false);
  layer.id = layerIdSeq++;
  st.layers.push(layer);
  selLayerId = layer.id;
  afterEdit(st); rebuildLayerList(); updateHud();
  return layer;
}
function deleteSelLayer() {
  const st = activeSt(), l = selLayer(); if (!st || !l) return;
  pushUndo(st, false);
  st.layers = st.layers.filter(x => x.id !== l.id); selLayerId = null;
  afterEdit(st); rebuildLayerList(); updateHud();
}
function moveLayerOrder(dir) {
  const st = activeSt(), l = selLayer(); if (!st || !l) return;
  const i = st.layers.indexOf(l), j = i + dir; if (j < 0 || j >= st.layers.length) return;
  pushUndo(st, false);
  st.layers.splice(i, 1); st.layers.splice(j, 0, l);
  afterEdit(st); rebuildLayerList();
}
function rebuildLayerList() {
  const st = activeSt(), root = $('layerList'); root.innerHTML = '';
  if (!st || !st.layers.length) { root.innerHTML = '<div class="layer-empty">пусто — добавь наклейку или текст</div>'; return; }
  [...st.layers].reverse().forEach(l => {
    const el = document.createElement('div');
    el.className = 'layer-item' + (l.id === selLayerId ? ' active' : '');
    const nm = l.type === 'text' ? `🅰 ${l.text.slice(0, 16)}` : `🖼 ${l.name || 'наклейка'}`;
    el.innerHTML = `<span class="li-name">${nm}</span>
      <button class="li-btn" data-a="up" title="выше">▲</button>
      <button class="li-btn" data-a="down" title="ниже">▼</button>
      <button class="li-btn" data-a="del" title="удалить">🗑</button>`;
    el.querySelector('.li-name').onclick = () => {
      selLayerId = l.id;
      setMode(l.type === 'text' ? 'text' : 'sticker');
      rebuildLayerList(); updateHud(); draw2D();
    };
    el.querySelector('[data-a=up]').onclick = (e) => { e.stopPropagation(); selLayerId = l.id; moveLayerOrder(1); };
    el.querySelector('[data-a=down]').onclick = (e) => { e.stopPropagation(); selLayerId = l.id; moveLayerOrder(-1); };
    el.querySelector('[data-a=del]').onclick = (e) => { e.stopPropagation(); selLayerId = l.id; deleteSelLayer(); };
    root.appendChild(el);
  });
}

function updateHud() {
  const l = selLayer();
  $('hud').hidden = !l;
  if (!l) return;
  $('hudName').textContent = l.type === 'text' ? `Текст «${l.text.slice(0, 14)}»` : `Наклейка ${l.name || ''}`;
  $('hudScale').value = Math.round((l.baseScale ? l.w / l.baseScale : 1) * 100);
  $('hudRot').value = Math.round(l.rot * 180 / Math.PI);
  $('hudOpacity').value = Math.round(l.opacity * 100);
}
$('hudDel').onclick = deleteSelLayer;
$('hudScale').oninput = (e) => {
  const st = activeSt(), l = selLayer(); if (!l) return;
  const f = +e.target.value / 100;
  if (l.type === 'stamp') { l.w = l.baseScale * f; l.h = l.baseH * f; }
  else { l.size = Math.max(6, Math.round(l.baseSize * f)); measureTextLayer(l); }
  afterEdit(st);
};
$('hudRot').oninput = (e) => { const st = activeSt(), l = selLayer(); if (!l) return; l.rot = +e.target.value * Math.PI / 180; afterEdit(st); };
$('hudOpacity').oninput = (e) => { const st = activeSt(), l = selLayer(); if (!l) return; l.opacity = +e.target.value / 100; afterEdit(st); };

const stamps = [];
let activeStamp = 0;
function makeStamp(name, fn) { const c = makeCanvas(256, 256); fn(c.getContext('2d')); stamps.push({ name, img: c }); }
makeStamp('star', ctx => { ctx.fillStyle = '#ffd600'; ctx.strokeStyle = '#3a2c00'; ctx.lineWidth = 8; ctx.beginPath(); for (let i = 0; i < 10; i++) { const r = i % 2 ? 52 : 120, a = -Math.PI / 2 + i * Math.PI / 5; ctx[i ? 'lineTo' : 'moveTo'](128 + r * Math.cos(a), 128 + r * Math.sin(a)); } ctx.closePath(); ctx.fill(); ctx.stroke(); });
makeStamp('heart', ctx => { ctx.fillStyle = '#ff2d55'; ctx.beginPath(); ctx.moveTo(128, 224); ctx.bezierCurveTo(-40, 110, 52, -18, 128, 74); ctx.bezierCurveTo(204, -18, 296, 110, 128, 224); ctx.fill(); });
makeStamp('smile', ctx => { ctx.fillStyle = '#ffde34'; ctx.beginPath(); ctx.arc(128, 128, 110, 0, 7); ctx.fill(); ctx.fillStyle = '#222'; ctx.beginPath(); ctx.arc(88, 100, 14, 0, 7); ctx.fill(); ctx.beginPath(); ctx.arc(168, 100, 14, 0, 7); ctx.fill(); ctx.strokeStyle = '#222'; ctx.lineWidth = 12; ctx.lineCap = 'round'; ctx.beginPath(); ctx.arc(128, 128, 62, .35, Math.PI - .35); ctx.stroke(); });
makeStamp('MIAMI', ctx => { ctx.font = '900 60px Impact, sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.lineWidth = 10; ctx.strokeStyle = '#000'; ctx.strokeText('MIAMI', 128, 128); ctx.fillStyle = '#ff2d95'; ctx.fillText('MIAMI', 128, 128); });
makeStamp('paw', ctx => { ctx.fillStyle = '#1b1b1b'; const p = (x, y, rx, ry) => { ctx.beginPath(); ctx.ellipse(x, y, rx, ry, 0, 0, 7); ctx.fill(); }; p(128, 160, 58, 48); p(66, 92, 22, 30); p(112, 66, 22, 30); p(158, 66, 22, 30); p(200, 92, 22, 30); });

function stampGridHtml() {
  return `<div class="stamp-grid">${stamps.map((s, i) =>
    `<img class="stamp-thumb${i === activeStamp ? ' active' : ''}" data-i="${i}" title="${s.name}" src="${s.img.toDataURL ? s.img.toDataURL() : s.img.src}">`).join('')}</div>
    <label class="file-btn">＋ загрузить PNG<input type="file" id="stampFile" accept="image/png,image/webp,image/jpeg" hidden></label>`;
}
function wireStampGrid() {
  document.querySelectorAll('.stamp-thumb').forEach(im => im.onclick = () => { activeStamp = +im.dataset.i; renderOpts(); });
  const f = $('stampFile');
  if (f) f.onchange = async (e) => {
    const file = e.target.files[0]; if (!file) return;
    const img = new Image(); img.src = URL.createObjectURL(file); await img.decode();
    const c = makeCanvas(img.naturalWidth, img.naturalHeight); c.getContext('2d').drawImage(img, 0, 0);
    stamps.push({ name: file.name.replace(/\.\w+$/, ''), img: c });
    activeStamp = stamps.length - 1; renderOpts();
    log(`картинка загружена: ${file.name}`, 'ok');
  };
}

const TOOL_TITLES = { orbit: 'Осмотр', brush: 'Кисть', eraser: 'Ластик', fill: 'Заливка', picker: 'Пипетка', alpha: 'Прозрачность', glass: 'Стекло', sticker: 'Наклейка', text: 'Текст', attach: '3D-модель', anim: 'Аним-деталь' };
const SWATCHES = ['#ffffff', '#000000', '#ff2d95', '#2dd4ff', '#ffd600', '#38d878', '#ff5062', '#b9bec7'];
const swatchHtml = () => `<div class="swatches">${SWATCHES.map(c => `<i class="swatch" data-c="${c}" style="background:${c}"></i>`).join('')}</div>`;
const sizeRow = () => `<div class="field"><label>Размер</label><input type="range" id="oSize" min="2" max="250" value="${brush.size}"><span class="val" id="oSizeV">${brush.size}</span></div>`;
const strengthRow = () => `<div class="field"><label>Сила</label><input type="range" id="oAlpha" min="5" max="100" value="${Math.round(brush.alpha * 100)}"><span class="val" id="oAlphaV">${Math.round(brush.alpha * 100)}%</span></div>`;
const colorRow = () => `<div class="row"><label style="color:var(--muted);font-size:12px">Цвет</label><input type="color" id="oColor" value="${brush.color}">${swatchHtml()}</div>`;

function renderOpts() {
  $('optsTitle').textContent = 'Инструмент · ' + (TOOL_TITLES[mode] || '');
  const b = $('optsBody');
  if (mode === 'orbit') { b.innerHTML = `<div class="hint">ЛКМ — вращать · ПКМ — двигать · колесо — зум.<br>Выбери инструмент слева, чтобы редактировать.</div>`; return; }
  if (mode === 'brush') {
    b.innerHTML = colorRow() +
      `<div class="field"><label>Стиль</label><select id="oStyle">
        <option value="normal">Обычная</option><option value="soft">Мягкая</option>
        <option value="spray">Спрей</option><option value="marker">Маркер</option></select><span></span></div>` +
      sizeRow() + strengthRow() +
      `<div class="hint">Рисуй прямо по гану или по развёртке снизу.</div>`;
    $('oStyle').value = brush.style;
    $('oStyle').onchange = e => brush.style = e.target.value;
  } else if (mode === 'eraser') {
    b.innerHTML = sizeRow() + strengthRow() + `<div class="hint">Возвращает пиксели оригинала.</div>`;
  } else if (mode === 'fill') {
    b.innerHTML = colorRow() +
      `<div class="field"><label>Порог</label><input type="range" id="oTol" min="1" max="120" value="${fillTolerance}"><span class="val" id="oTolV">${fillTolerance}</span></div>
       <div class="hint">Клик по гану — залить похожую область цветом. Порог больше = захватывает больше оттенков.</div>`;
    $('oTol').oninput = e => { fillTolerance = +e.target.value; $('oTolV').textContent = e.target.value; };
  } else if (mode === 'picker') {
    b.innerHTML = `<div class="hint">Клик по гану — взять цвет в кисть.</div>`;
  } else if (mode === 'alpha') {
    b.innerHTML = sizeRow() + strengthRow() +
      `<div class="hint warn">Протирает текстуру до прозрачности. В игре видно только на деталях с alpha-шейдером (магазин/флажок мопса, bucket ≥ 1); на корпусе (bucket 0) — нет.</div>`;
  } else if (mode === 'glass') {
    const st = activeSt(); const g = st?.glass || { on: false, color: '#7fdfff', opacity: 0.4 };
    b.innerHTML =
      `<div class="row"><label style="color:var(--muted);font-size:12px">Цвет стекла</label><input type="color" id="oGlassColor" value="${g.color}"></div>` +
      swatchHtml() +
      `<div class="field"><label>Прозрачность</label><input type="range" id="oGlassOp" min="10" max="92" value="${Math.round(g.opacity * 100)}"><span class="val" id="oGlassOpV">${Math.round(g.opacity * 100)}%</span></div>
       <div class="opt-btns"><button class="btn ${g.on ? 'on' : ''}" id="oGlassToggle">${g.on ? '✓ стекло включено' : 'Сделать стеклом'}</button></div>
       <div class="hint">Тонированное стекло на текущей текстуре (деталях, где она). Цвет — палитрой. Превью; в игре нужен glass-шейдер.</div>`;
    $('oGlassColor').oninput = e => { if (!st) return; st.glass.color = e.target.value; if (st.glass.on) applyGlass(st); };
    $('oGlassOp').oninput = e => { $('oGlassOpV').textContent = e.target.value + '%'; if (!st) return; st.glass.opacity = +e.target.value / 100; if (st.glass.on) applyGlass(st); };
    $('oGlassToggle').onclick = () => {
      if (!st) { log('сначала выбери ган и текстуру', 'err'); return; }
      st.glass.on = !st.glass.on; applyGlass(st); renderOpts();
      log(st.glass.on ? `🔷 стекло включено (${st.name})` : 'стекло выключено', 'ok');
    };
  } else if (mode === 'sticker') {
    b.innerHTML = stampGridHtml() +
      `<label class="mini"><input type="checkbox" id="oTop" ${overlayOnTop ? 'checked' : ''}> клеить поверх (на лого-слой)</label>
       <div class="opt-btns"><button class="btn primary" id="oAdd">＋ Добавить на ган</button></div>
       <div class="hint">Добавь → тащи по гану чтобы двигать · колесо = размер · крутилки внизу.</div>`;
    wireStampGrid();
    $('oTop').onchange = e => overlayOnTop = e.target.checked;
    $('oAdd').onclick = () => placeSticker();
  } else if (mode === 'text') {
    const sel = selLayer()?.type === 'text' ? selLayer() : null;
    const cur = sel || { text: 'MIAMI', font: 'Unbounded', size: 160, bold: true, color: '#ff2d95', outline: '#000000', outlineW: 8 };
    const fonts = ['Unbounded', 'Impact', 'Arial Black', 'Manrope', 'Georgia', 'Consolas'];
    b.innerHTML =
      `<input type="text" id="oText" placeholder="твой текст" value="${cur.text.replace(/"/g, '&quot;')}">
       <div class="row"><select id="oFont" style="flex:1">${fonts.map(f => `<option ${f === cur.font ? 'selected' : ''}>${f}</option>`).join('')}</select>
       <label class="mini"><input type="checkbox" id="oBold" ${cur.bold ? 'checked' : ''}> жирный</label></div>
       <div class="field"><label>Размер</label><input type="range" id="oTSize" min="24" max="800" value="${cur.size}"><span class="val" id="oTSizeV">${cur.size}</span></div>
       <div class="row"><label style="color:var(--muted);font-size:12px">Цвет</label><input type="color" id="oTColor" value="${cur.color}">
       <label style="color:var(--muted);font-size:12px;margin-left:8px">Обводка</label><input type="color" id="oTOutline" value="${cur.outline}">
       <input type="number" id="oTOutlineW" min="0" max="60" value="${cur.outlineW}" style="width:50px" ${cur.outlineW === 0 ? 'disabled' : ''}></div>
       <label class="mini"><input type="checkbox" id="oNoOutline" ${cur.outlineW === 0 ? 'checked' : ''}> без обводки</label>
       <div class="opt-btns"><button class="btn primary" id="oAddText">＋ Добавить текст на ган</button></div>
       <div class="hint">${sel ? 'Правишь выбранный текст — на гане меняется сразу.' : 'Настрой и «Добавить». Выберешь текст-слой (в «Слоях» или тапом) — правки пойдут вживую.'}</div>`;
    const live = () => {
      const l = selLayer(); if (l?.type !== 'text') return;
      Object.assign(l, textCfgFromOpts()); l.baseSize = l.size; measureTextLayer(l);
      afterEdit(activeSt()); updateHud();
    };
    for (const id of ['oText', 'oFont', 'oBold', 'oTColor', 'oTOutline', 'oTOutlineW']) $(id).oninput = live;
    $('oTSize').oninput = e => { $('oTSizeV').textContent = e.target.value; live(); };
    $('oNoOutline').onchange = e => { $('oTOutlineW').disabled = e.target.checked; live(); };
    $('oAddText').onclick = () => placeText();
  } else if (mode === 'attach') {
    attachKind = 'pendant';
    const imp = pendingObj
      ? `<div class="field"><label>Размер</label><input type="range" id="oImpScale" min="10" max="400" value="${Math.round(pendingScale * 100)}"><span class="val" id="oImpScaleV">${Math.round(pendingScale * 100)}%</span></div>
         <div class="field"><label>Поворот</label><input type="range" id="oImpRot" min="-180" max="180" value="${Math.round(pendingObj.rotation.y * 180 / Math.PI)}"><span class="val" id="oImpRotV">${Math.round(pendingObj.rotation.y * 180 / Math.PI)}°</span></div>
         <div class="opt-btns"><button class="btn primary" id="oImpBake">✓ Вшить в ган</button><button class="btn" id="oImpCancel">Отмена</button></div>
         <div class="hint">Тащи модель по гану — двигать · колесо — размер. «Вшить» запечёт её в .ydr.</div>`
      : `<label class="file-btn">＋ загрузить .glb модель<input type="file" id="glbFile" accept=".glb,model/gltf-binary" hidden></label>
         <div class="hint">Импортируй свою 3D-модель (.glb). Бесплатные CC0-модели: <a href="https://poly.pizza" target="_blank" style="color:var(--accent)">poly.pizza</a>, <a href="https://quaternius.com" target="_blank" style="color:var(--accent)">quaternius.com</a>. Скачай .glb → загрузи сюда → двигай по гану → «Вшить».</div>`;
    b.innerHTML =
      `<div class="card-h" style="margin:0 0 6px">Брелок из картинки</div>` +
      stampGridHtml() +
      `<div class="field"><label>Размер</label><input type="range" id="oAtt" min="2" max="40" value="${attachSizeCm}"><span class="val" id="oAttV">${attachSizeCm}см</span></div>
       <div class="field"><label>Толщина</label><input type="range" id="oDepth" min="5" max="45" value="${Math.round(attachDepth * 100)}"><span class="val" id="oDepthV">${Math.round(attachDepth * 100)}%</span></div>
       <div class="hint">Форма режется по контуру PNG (фон прозрачный!) и выдавливается. Клик по гану — повиснет вниз.</div>
       <div class="card-h" style="margin:10px 0 6px">Своя 3D-модель</div>
       ${imp}
       <div class="card-h" style="margin:10px 0 4px">Вшитые 3D-модели</div>
       <div id="accList" class="layer-list"><div class="layer-empty">загрузка…</div></div>`;
    wireStampGrid();
    $('oAtt').oninput = e => { attachSizeCm = +e.target.value; $('oAttV').textContent = e.target.value + 'см'; };
    $('oDepth').oninput = e => { attachDepth = +e.target.value / 100; $('oDepthV').textContent = e.target.value + '%'; };
    if (pendingObj) {
      $('oImpScale').oninput = e => { pendingScale = +e.target.value / 100; applyPendingTransform(); $('oImpScaleV').textContent = e.target.value + '%'; };
      $('oImpRot').oninput = e => { pendingObj.rotation.y = +e.target.value * Math.PI / 180; $('oImpRotV').textContent = e.target.value + '°'; };
      $('oImpBake').onclick = () => bakeImported();
      $('oImpCancel').onclick = () => cancelImported();
    } else {
      $('glbFile').onchange = e => importGlb(e.target.files[0]);
    }
    loadAccessories();
  } else if (mode === 'anim') {
    const bones = detail?.boneNames || [];
    if (!anim.bone || !bones.includes(anim.bone))
      anim.bone = bones.includes('Gun_Main_Bone') ? 'Gun_Main_Bone' : (bones[0] || 'Gun_Main_Bone');
    const boneOpts = bones.length
      ? bones.map(bn => `<option ${bn === anim.bone ? 'selected' : ''}>${bn}</option>`).join('')
      : `<option>Gun_Main_Bone</option>`;

    let modeParams = '';
    if (anim.mode === 'uv') {
      modeParams =
        `<div class="field"><label>Скролл U</label><input type="range" id="oScrollU" min="-4" max="4" step="0.5" value="${anim.scrollU}"><span class="val" id="oScrollUV">${anim.scrollU}</span></div>
         <div class="field"><label>Скролл V</label><input type="range" id="oScrollV" min="-4" max="4" step="0.5" value="${anim.scrollV}"><span class="val" id="oScrollVV">${anim.scrollV}</span></div>
         <div class="hint">Бегущая текстура (как эталонный «anima»). Число = сколько раз текстура прокрутится за период.</div>`;
    } else {
      const axisSel = `<div class="field"><label>Ось</label><select id="oAxis">
          <option value="x" ${anim.axis === 'x' ? 'selected' : ''}>X</option>
          <option value="y" ${anim.axis === 'y' ? 'selected' : ''}>Y</option>
          <option value="z" ${anim.axis === 'z' ? 'selected' : ''}>Z</option></select><span></span></div>`;
      modeParams = axisSel + (anim.mode === 'swing'
        ? `<div class="field"><label>Амплитуда</label><input type="range" id="oAmp" min="5" max="90" value="${anim.amplitude}"><span class="val" id="oAmpV">${anim.amplitude}°</span></div>
           <div class="hint">Качание кости туда-сюда вокруг оси (маятник). Геометрия висит от точки крепления.</div>`
        : `<div class="hint">Полное вращение кости на 360° вокруг оси за период.</div>`);
    }

    b.innerHTML =
      `<div class="card-h" style="margin:0 0 6px">Деталь из картинки</div>` +
      stampGridHtml() +
      `<div class="field"><label>Размер</label><input type="range" id="oAnimSize" min="2" max="40" value="${anim.sizeCm}"><span class="val" id="oAnimSizeV">${anim.sizeCm}см</span></div>
       <div class="field"><label>Толщина</label><input type="range" id="oAnimDepth" min="5" max="45" value="${Math.round(anim.depth * 100)}"><span class="val" id="oAnimDepthV">${Math.round(anim.depth * 100)}%</span></div>
       <div class="card-h" style="margin:10px 0 6px">Анимация</div>
       <div class="field"><label>Тип</label><select id="oAnimMode">
          <option value="uv" ${anim.mode === 'uv' ? 'selected' : ''}>Бегущая текстура</option>
          <option value="swing" ${anim.mode === 'swing' ? 'selected' : ''}>Качание</option>
          <option value="spin" ${anim.mode === 'spin' ? 'selected' : ''}>Вращение</option></select><span></span></div>
       ${modeParams}
       <div class="field"><label>Период</label><input type="range" id="oPeriod" min="0.3" max="8" step="0.1" value="${anim.period}"><span class="val" id="oPeriodV">${anim.period}с</span></div>
       <div class="card-h" style="margin:10px 0 6px">Крепление</div>
       <div class="field"><label>Кость</label><select id="oBone">${boneOpts}</select><span></span></div>
       <div class="opt-btns"><button class="btn primary" id="oAnimGen">✨ Собрать деталь + превью</button></div>
       <div class="hint">Форма режется по контуру PNG (фон прозрачный!). Генерит ydr+ycd+ytyp+meta для «аним ганпака» и показывает анимацию в 3D. В игре нужна упаковка в пак.</div>
       <div id="animInfo" class="hint" style="display:none"></div>`;
    wireStampGrid();
    $('oAnimSize').oninput = e => { anim.sizeCm = +e.target.value; $('oAnimSizeV').textContent = e.target.value + 'см'; };
    $('oAnimDepth').oninput = e => { anim.depth = +e.target.value / 100; $('oAnimDepthV').textContent = e.target.value + '%'; };
    $('oAnimMode').onchange = e => { anim.mode = e.target.value; renderOpts(); };
    $('oPeriod').oninput = e => { anim.period = +e.target.value; $('oPeriodV').textContent = e.target.value + 'с'; if (animPreview) animPreview.meta.periodSec = anim.period; };
    $('oBone').onchange = e => { anim.bone = e.target.value; };
    if ($('oScrollU')) $('oScrollU').oninput = e => { anim.scrollU = +e.target.value; $('oScrollUV').textContent = e.target.value; };
    if ($('oScrollV')) $('oScrollV').oninput = e => { anim.scrollV = +e.target.value; $('oScrollVV').textContent = e.target.value; };
    if ($('oAxis')) $('oAxis').onchange = e => { anim.axis = e.target.value; };
    if ($('oAmp')) $('oAmp').oninput = e => { anim.amplitude = +e.target.value; $('oAmpV').textContent = e.target.value + '°'; };
    $('oAnimGen').onclick = () => generateAnim();
  }
  const os = $('oSize'); if (os) os.oninput = e => { brush.size = +e.target.value; $('oSizeV').textContent = e.target.value; };
  const oa = $('oAlpha'); if (oa) oa.oninput = e => { brush.alpha = +e.target.value / 100; $('oAlphaV').textContent = e.target.value + '%'; };
  const oc = $('oColor'); if (oc) oc.oninput = e => brush.color = e.target.value;
  document.querySelectorAll('#optsBody .swatch').forEach(s => s.onclick = () => {
    const c = s.dataset.c;
    if (mode === 'glass') { const st = activeSt(); if (st) { st.glass.color = c; const gi = $('oGlassColor'); if (gi) gi.value = c; if (st.glass.on) applyGlass(st); } }
    else { brush.color = c; const ci = $('oColor'); if (ci) ci.value = c; }
  });
}

function textCfgFromOpts() {
  const noOutline = $('oNoOutline')?.checked;
  return {
    text: $('oText')?.value || 'MIAMI', font: $('oFont')?.value || 'Unbounded',
    size: +($('oTSize')?.value || 160), bold: $('oBold')?.checked ?? true,
    color: $('oTColor')?.value || '#ff2d95', outline: $('oTOutline')?.value || '#000000',
    outlineW: noOutline ? 0 : +($('oTOutlineW')?.value || 8),
  };
}

async function loadAccessories() {
  const root = $('accList'); if (!root) return;
  try {
    const list = await (await api(`/api/accessories?pack=${enc(currentPack)}&gun=${enc(currentGun)}`)).json();
    if (!list.length) { root.innerHTML = '<div class="layer-empty">пока ничего не вшито</div>'; return; }
    root.innerHTML = '';
    list.forEach(name => {
      const el = document.createElement('div');
      el.className = 'layer-item';
      el.innerHTML = `<span class="li-name">📌 ${name}</span><button class="li-btn" title="удалить из .ydr">🗑</button>`;
      el.querySelector('.li-btn').onclick = () => removeAccessory(name);
      root.appendChild(el);
    });
  } catch (e) { root.innerHTML = `<div class="layer-empty">ошибка: ${e.message}</div>`; }
}
async function removeAccessory(name) {
  spin(true); setStatus('удаляю 3D…');
  try {
    const j = await (await fetch(`/api/accessory/remove?pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}`, { method: 'POST' })).json();
    if (!j.ok) throw new Error(j.error);
    log(`🗑 удалено: ${name} (${j.patched.join(', ')})`, 'ok');
    await loadGun(currentPack, currentGun); setMode('attach');
  } catch (e) { setStatus('ошибка'); log(`удаление 3D: ${e.message}`, 'err'); } finally { spin(false); }
}
function measureTextLayer(l) {
  const c = makeCanvas(8, 8).getContext('2d');
  c.font = `${l.bold ? '900' : '400'} ${l.size}px ${l.font}`;
  l.w = Math.max(8, c.measureText(l.text).width + l.outlineW * 2);
  l.h = l.size * 1.3 + l.outlineW * 2;
}
function applyTextToSelected() {
  const st = activeSt(), l = selLayer(); if (!st || l?.type !== 'text') { log('выбери текст-слой', 'err'); return; }
  pushUndo(st, false);
  Object.assign(l, textCfgFromOpts()); l.baseSize = l.size; measureTextLayer(l);
  afterEdit(st); rebuildLayerList(); updateHud();
}

function screenCenterUV() {
  raycaster.setFromCamera(new THREE.Vector2(0, 0), camera);
  const h = raycaster.intersectObjects(gunMeshes, false).find(x => x.uv && meshTexName.get(x.object.uuid));
  return h ? { uv: h.uv, texName: meshTexName.get(h.object.uuid) } : null;
}
async function placePoint() {
  const hit = screenCenterUV();
  if (hit) { if (hit.texName !== activeTex) await selectTexture(hit.texName); const st = activeSt(); return { st, ...uvToPx(st, hit.uv) }; }
  const st = activeSt(); if (!st) return null;
  return { st, x: st.comp.width / 2, y: st.comp.height / 2 };
}
async function placeSticker() {
  const at = await placePoint();
  if (!at) { log('сначала выбери ган и текстуру', 'err'); return; }
  const { st } = at, s = stamps[activeStamp];
  const w = st.comp.width * 0.15, iw = s.img.width, ih = s.img.height;
  addLayer(st, { type: 'stamp', name: s.name, img: s.img, x: at.x, y: at.y, w, h: w * ih / iw, baseScale: w, baseH: w * ih / iw, rot: 0, opacity: brush.alpha, blend: 'source-over' });
  log(`наклейка «${s.name}» добавлена — тащи по гану`, 'ok');
}
async function placeText() {
  const at = await placePoint();
  if (!at) { log('сначала выбери ган и текстуру', 'err'); return; }
  const { st } = at, cfg = textCfgFromOpts();
  const l = addLayer(st, { type: 'text', ...cfg, baseSize: cfg.size, x: at.x, y: at.y, rot: 0, opacity: brush.alpha, blend: 'source-over' });
  measureTextLayer(l); afterEdit(st); updateHud();
  log(`текст «${cfg.text}» добавлен — тащи по гану`, 'ok');
}

function rawHit(ev, preferOverlay = false) {
  const r = canvas3d.getBoundingClientRect();
  const ndc = new THREE.Vector2(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
  raycaster.setFromCamera(ndc, camera);
  const hits = raycaster.intersectObjects(gunMeshes, false).filter(h => h.uv && meshTexName.get(h.object.uuid));
  if (!hits.length) return null;
  let hit = hits[0];
  if (preferOverlay) {
    const near = hits.filter(h => h.distance - hits[0].distance < 0.02);
    const ov = near.find(h => (meshBucket.get(h.object.uuid) ?? 0) > 0);
    if (ov) hit = ov;
  }
  return hit;
}
function raycastHit(ev, preferOverlay = false) {
  const hit = rawHit(ev, preferOverlay);
  if (!hit) return null;
  return { uv: hit.uv, texName: meshTexName.get(hit.object.uuid), bucket: meshBucket.get(hit.object.uuid) ?? 0, objId: hit.object.uuid };
}
function uvToPx(st, uv) {
  const fx = ((uv.x % 1) + 1) % 1, fy = ((uv.y % 1) + 1) % 1;
  return { x: fx * st.comp.width, y: fy * st.comp.height };
}

let painting = false, lastPx = null, lastScreen = null, strokeObjId = null;

const _v3a = new THREE.Vector3(), _v3b = new THREE.Vector3(), _u2a = new THREE.Vector2(), _u2b = new THREE.Vector2();
function brushScreenDiameter(hit, st) {
  const geo = hit.object.geometry, f = hit.face; if (!f) return null;
  const pos = geo.attributes.position, uv = geo.attributes.uv; if (!uv) return null;
  _v3a.fromBufferAttribute(pos, f.a).applyMatrix4(hit.object.matrixWorld);
  _v3b.fromBufferAttribute(pos, f.b).applyMatrix4(hit.object.matrixWorld);
  _u2a.fromBufferAttribute(uv, f.a); _u2b.fromBufferAttribute(uv, f.b);
  const dUV = _u2a.distanceTo(_u2b) || 1e-4, dW = _v3a.distanceTo(_v3b);
  const worldPerUV = dW / dUV;
  const worldR = worldPerUV * (brush.size / st.comp.width) / 2;
  const right = new THREE.Vector3().setFromMatrixColumn(camera.matrixWorld, 0).multiplyScalar(worldR);
  const s0 = hit.point.clone().project(camera), s1 = hit.point.clone().add(right).project(camera);
  const rect = canvas3d.getBoundingClientRect();
  const d = Math.hypot((s1.x - s0.x) * rect.width / 2, (s1.y - s0.y) * rect.height / 2);
  return d * 2;
}
function updateBrushRing(ev) {
  const ring = $('brushRing');
  if (!PAINT_MODES.has(mode) || mode === 'picker' || mode === 'fill') { ring.hidden = true; return; }
  const hit = rawHit(ev, mode === 'alpha' ? overlayOnTop : false);
  const tn = hit && meshTexName.get(hit.object.uuid);
  const st = (tn && texState.get(tn)) || activeSt();
  if (!hit || !tn || !st) { ring.hidden = true; return; }
  const dia = brushScreenDiameter(hit, st);
  if (!dia || !isFinite(dia)) { ring.hidden = true; return; }
  const rect = canvas3d.getBoundingClientRect();
  ring.style.left = (ev.clientX - rect.left) + 'px';
  ring.style.top = (ev.clientY - rect.top) + 'px';
  ring.style.width = ring.style.height = Math.max(6, Math.min(dia, 2000)) + 'px';
  ring.hidden = false;
}
function softDab(ctx, x, y, size, color, alpha) {
  const g = ctx.createRadialGradient(x, y, 0, x, y, size / 2), [r, gg, b] = hexToRgb(color);
  g.addColorStop(0, `rgba(${r},${gg},${b},${alpha})`); g.addColorStop(1, `rgba(${r},${gg},${b},0)`);
  ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, size / 2, 0, 7); ctx.fill();
}
function sprayDab(ctx, x, y, size, color, alpha) {
  ctx.fillStyle = color; ctx.globalAlpha = alpha * 0.5;
  const n = Math.max(6, size | 0);
  for (let i = 0; i < n; i++) { const a = Math.random() * 7, rr = Math.sqrt(Math.random()) * size / 2; ctx.beginPath(); ctx.arc(x + rr * Math.cos(a), y + rr * Math.sin(a), 0.8 + Math.random() * 1.4, 0, 7); ctx.fill(); }
  ctx.globalAlpha = 1;
}
function strokeOrDot(ctx, a, p, size) {
  ctx.lineWidth = size; ctx.lineCap = ctx.lineJoin = 'round';
  if (a) { ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(p.x, p.y); ctx.stroke(); }
  else { ctx.beginPath(); ctx.arc(p.x, p.y, size / 2, 0, 7); ctx.fill(); }
}
function dabsAlong(a, p, spacing, fn) {
  if (!a) { fn(p.x, p.y); return; }
  const d = Math.hypot(p.x - a.x, p.y - a.y), n = Math.max(1, Math.ceil(d / Math.max(1, spacing)));
  for (let i = 1; i <= n; i++) fn(a.x + (p.x - a.x) * i / n, a.y + (p.y - a.y) * i / n);
}
function paintStep(st, p) {
  const ctx = st.ctxB, size = brush.size;
  const jump = lastPx && Math.hypot(p.x - lastPx.x, p.y - lastPx.y) > size * 4;
  if (jump) { lastPx = null; return; }
  const a = lastPx || null;
  ctx.save();
  if (mode === 'eraser') { st.origPattern ||= ctx.createPattern(st.orig, 'no-repeat'); ctx.globalAlpha = brush.alpha; ctx.strokeStyle = ctx.fillStyle = st.origPattern; strokeOrDot(ctx, a, p, size); }
  else if (mode === 'alpha') { ctx.globalCompositeOperation = 'destination-out'; ctx.globalAlpha = brush.alpha; ctx.strokeStyle = ctx.fillStyle = '#000'; strokeOrDot(ctx, a, p, size); }
  else if (brush.style === 'soft') dabsAlong(a, p, size / 5, (x, y) => softDab(ctx, x, y, size, brush.color, brush.alpha * 0.35));
  else if (brush.style === 'spray') dabsAlong(a, p, size / 3, (x, y) => sprayDab(ctx, x, y, size, brush.color, brush.alpha));
  else { if (brush.style === 'marker') { ctx.globalCompositeOperation = 'multiply'; ctx.globalAlpha = brush.alpha * 0.6; } else ctx.globalAlpha = brush.alpha; ctx.strokeStyle = ctx.fillStyle = brush.color; strokeOrDot(ctx, a, p, size); }
  ctx.restore();
  lastPx = p; afterEdit(st);
}
function ensureChrome(st) {
  if (st.chrome) return st.chrome;
  const cw = Math.min(2048, st.comp.width), ch = Math.round(cw * st.comp.height / st.comp.width);
  const canvas = makeCanvas(cw, ch), ctx = canvas.getContext('2d');
  ctx.fillStyle = '#666'; ctx.fillRect(0, 0, cw, ch);
  const tex = new THREE.CanvasTexture(canvas); tex.flipY = false;
  st.chrome = { canvas, ctx, tex, k: cw / st.comp.width };
  for (const m of (matsByTex.get(st.name) || [])) { m.metalnessMap = tex; m.metalness = 1; m.roughness = Math.min(m.roughness ?? .5, .4); m.needsUpdate = true; }
  return st.chrome;
}
function paintChrome(st, a, p, size) {
  const c = ensureChrome(st); c.ctx.save(); c.ctx.strokeStyle = c.ctx.fillStyle = '#fff'; c.ctx.globalAlpha = brush.alpha;
  strokeOrDot(c.ctx, a ? { x: a.x * c.k, y: a.y * c.k } : null, { x: p.x * c.k, y: p.y * c.k }, size * c.k);
  c.ctx.restore(); c.tex.needsUpdate = true;
}
function floodFill(st, px, py) {
  const w = st.base.width, h = st.base.height;
  if (w * h > 4096 * 4096) { log('слишком большая текстура для заливки', 'err'); return; }
  const img = st.ctxB.getImageData(0, 0, w, h), buf = new Uint32Array(img.data.buffer);
  const x0 = px | 0, y0 = py | 0, target = buf[y0 * w + x0], [r, g, b] = hexToRgb(brush.color);
  const fill = (255 << 24) | (b << 16) | (g << 8) | r; if (target === fill) return;
  const tr = target & 255, tg = target >> 8 & 255, tb = target >> 16 & 255, ta = target >>> 24, tol = fillTolerance;
  const match = v => Math.abs((v & 255) - tr) + Math.abs((v >> 8 & 255) - tg) + Math.abs((v >> 16 & 255) - tb) + Math.abs((v >>> 24) - ta) <= tol * 3;
  const stack = [y0 * w + x0], seen = new Uint8Array(w * h); seen[y0 * w + x0] = 1;
  while (stack.length) { const i = stack.pop(); buf[i] = fill; const x = i % w, y = (i / w) | 0;
    if (x > 0 && !seen[i - 1] && match(buf[i - 1])) { seen[i - 1] = 1; stack.push(i - 1); }
    if (x < w - 1 && !seen[i + 1] && match(buf[i + 1])) { seen[i + 1] = 1; stack.push(i + 1); }
    if (y > 0 && !seen[i - w] && match(buf[i - w])) { seen[i - w] = 1; stack.push(i - w); }
    if (y < h - 1 && !seen[i + w] && match(buf[i + w])) { seen[i + w] = 1; stack.push(i + w); } }
  st.ctxB.putImageData(img, 0, 0);
}
function pickColor(st, p) {
  const d = st.ctxC.getImageData(p.x | 0, p.y | 0, 1, 1).data;
  brush.color = '#' + [d[0], d[1], d[2]].map(v => v.toString(16).padStart(2, '0')).join('');
  log(`пипетка: ${brush.color}`, 'ok'); setMode(prevPaintMode);
}
function enableAlpha(st, bucket) {
  if (!st.alphaOn) {
    st.alphaOn = true;
    for (const m of (matsByTex.get(st.name) || [])) { m.transparent = true; m.side = THREE.DoubleSide; m.depthWrite = false; m.needsUpdate = true; }
    log(bucket === 0 ? '⚠ корпус opaque — в игре дыра не появится (но в редакторе видно). Alpha работает на bucket ≥ 1.' : `✓ alpha-шейдер (bucket ${bucket}) — сработает и в игре`, bucket === 0 ? 'warn' : 'ok');
  }
}

function applyGlass(st) {
  const g = st.glass;
  for (const m of (matsByTex.get(st.name) || [])) {
    if (!m.userData._orig) m.userData._orig = {
      transparent: m.transparent, opacity: m.opacity, roughness: m.roughness,
      metalness: m.metalness, color: m.color.getHex(), side: m.side,
      depthWrite: m.depthWrite, envMapIntensity: m.envMapIntensity ?? 1,
    };
    if (g.on) {
      m.transparent = true; m.opacity = g.opacity; m.roughness = 0.05; m.metalness = 0;
      m.side = THREE.DoubleSide; m.depthWrite = false; m.envMapIntensity = 1.7;
      m.color.set(g.color);
    } else {
      const o = m.userData._orig;
      m.transparent = o.transparent; m.opacity = o.opacity; m.roughness = o.roughness;
      m.metalness = o.metalness; m.side = o.side; m.depthWrite = o.depthWrite;
      m.envMapIntensity = o.envMapIntensity; m.color.setHex(o.color);
    }
    m.needsUpdate = true;
  }
}

let dragLayer = null;
canvas3d.addEventListener('pointerdown', async (ev) => {
  if (mode === 'orbit' || mode === 'glass' || mode === 'anim' || ev.button !== 0) return;
  if (mode === 'attach') {
    if (pendingObj) { ev.preventDefault(); draggingPending = true; movePendingTo(ev); }
    else await attachAt(ev);
    return;
  }

  if (mode === 'sticker' || mode === 'text') {
    const l = selLayer(); if (!l) return;
    const hit = raycastHit(ev, overlayOnTop);
    if (!hit || hit.texName !== activeTex) return;
    ev.preventDefault();
    const st = activeSt(); pushUndo(st, false);
    const p = uvToPx(st, hit.uv); l.x = p.x; l.y = p.y;
    dragLayer = l; afterEdit(st);
    return;
  }

  const hit = raycastHit(ev); if (!hit) return;
  ev.preventDefault();
  if (hit.texName !== activeTex) await selectTexture(hit.texName);
  const st = activeSt(), p = uvToPx(st, hit.uv);
  if (mode === 'fill') { pushUndo(st, true); floodFill(st, p.x, p.y); afterEdit(st); return; }
  if (mode === 'picker') { pickColor(st, p); return; }
  if (mode === 'alpha') enableAlpha(st, hit.bucket);
  strokeObjId = hit.objId;
  pushUndo(st, true); painting = true; lastPx = null; lastScreen = { x: ev.clientX, y: ev.clientY };
  paintStep(st, p);
});
canvas3d.addEventListener('pointermove', (ev) => {
  updateBrushRing(ev);
  if (draggingPending) { movePendingTo(ev); return; }
  if (dragLayer) {
    const hit = raycastHit(ev, overlayOnTop); if (!hit || hit.texName !== activeTex) return;
    const st = activeSt(), p = uvToPx(st, hit.uv); dragLayer.x = p.x; dragLayer.y = p.y; afterEdit(st);
    return;
  }
  if (!painting) return;
  const cur = { x: ev.clientX, y: ev.clientY }, from = lastScreen ?? cur;
  const steps = Math.max(1, Math.ceil(Math.hypot(cur.x - from.x, cur.y - from.y) / 6));
  for (let i = 1; i <= steps; i++) {
    const hit = raycastHit({ clientX: from.x + (cur.x - from.x) * i / steps, clientY: from.y + (cur.y - from.y) * i / steps });
    if (!hit || hit.texName !== activeTex || hit.objId !== strokeObjId) { lastPx = null; continue; }
    paintStep(activeSt(), uvToPx(activeSt(), hit.uv));
  }
  lastScreen = cur;
});
canvas3d.addEventListener('pointerleave', () => { $('brushRing').hidden = true; });
addEventListener('pointerup', () => { painting = false; lastPx = null; lastScreen = null; dragLayer = null; gizmoDrag = null; strokeObjId = null; draggingPending = false; });

canvas3d.addEventListener('wheel', (ev) => {
  if (mode === 'attach' && pendingObj) {
    ev.preventDefault();
    pendingScale = Math.max(0.05, pendingScale * (ev.deltaY < 0 ? 1.08 : 1 / 1.08));
    applyPendingTransform();
    const s = $('oImpScale'); if (s) { s.value = Math.round(pendingScale * 100); $('oImpScaleV').textContent = s.value + '%'; }
    return;
  }
  const l = selLayer(); if (!l || !(mode === 'sticker' || mode === 'text')) return;
  ev.preventDefault();
  const f = ev.deltaY < 0 ? 1.08 : 1 / 1.08;
  if (l.type === 'stamp') { l.w *= f; l.h *= f; l.baseScale *= f; l.baseH *= f; }
  else { l.baseSize = l.size = Math.max(6, Math.round(l.size * f)); measureTextLayer(l); }
  afterEdit(activeSt()); updateHud();
}, { passive: false });

let painting2d = false, gizmoDrag = null;
const evTexPt = (ev, st) => { const r = tex2d.getBoundingClientRect(); return { x: (ev.clientX - r.left) / r.width * st.comp.width, y: (ev.clientY - r.top) / r.height * st.comp.height }; };
function hitLayerAt(st, p) {
  for (let i = st.layers.length - 1; i >= 0; i--) {
    const l = st.layers[i], dx = p.x - l.x, dy = p.y - l.y, cos = Math.cos(-l.rot), sin = Math.sin(-l.rot);
    if (Math.abs(dx * cos - dy * sin) <= l.w / 2 && Math.abs(dx * sin + dy * cos) <= l.h / 2) return l;
  }
  return null;
}
tex2d.addEventListener('pointerdown', (ev) => {
  if (!activeTex || mode === 'orbit' || ev.button !== 0) return;
  const st = activeSt(), p = evTexPt(ev, st);
  if (mode === 'sticker' || mode === 'text') {
    const l = hitLayerAt(st, p) || selLayer();
    if (l) { selLayerId = l.id; pushUndo(st, false); gizmoDrag = { l, sx: p.x - l.x, sy: p.y - l.y }; rebuildLayerList(); updateHud(); draw2D(); }
    return;
  }
  if (mode === 'fill') { pushUndo(st, true); floodFill(st, p.x, p.y); afterEdit(st); return; }
  if (mode === 'picker') { pickColor(st, p); return; }
  if (mode === 'alpha') enableAlpha(st, 1);
  pushUndo(st, true); painting2d = true; lastPx = null; paintStep(st, p);
});
tex2d.addEventListener('pointermove', (ev) => {
  const st = activeSt(); if (!st) return;
  if (gizmoDrag) { const p = evTexPt(ev, st); gizmoDrag.l.x = p.x - gizmoDrag.sx; gizmoDrag.l.y = p.y - gizmoDrag.sy; afterEdit(st); return; }
  if (painting2d) paintStep(st, evTexPt(ev, st));
});
addEventListener('pointerup', () => { painting2d = false; });

async function attachAt(ev) {
  const r = canvas3d.getBoundingClientRect();
  const ndc = new THREE.Vector2(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
  raycaster.setFromCamera(ndc, camera);
  const h = raycaster.intersectObjects(gunMeshes, false).find(x => x.face); if (!h) return;
  const p = h.point, n = h.face.normal.clone().transformDirection(h.object.matrixWorld).normalize();
  const sizeM = attachSizeCm / 100, s = stamps[activeStamp];
  const src = s.img.toDataURL ? s.img : (() => { const c = makeCanvas(s.img.naturalWidth, s.img.naturalHeight); c.getContext('2d').drawImage(s.img, 0, 0); return c; })();
  const blob = await new Promise(res => src.toBlob(res, 'image/png'));
  const name = `brelok_${Date.now() % 100000}`;
  const hadDirty = [...texState.values()].some(st => st.dirty);
  if (hadDirty && !(await saveDirty())) { log('привязка отменена: не удалось сохранить правки', 'err'); return; }
  spin(true); setStatus('прицепляю…');
  try {
    const q = `pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}&px=${p.x.toFixed(5)}&py=${p.y.toFixed(5)}&pz=${p.z.toFixed(5)}&nx=${n.x.toFixed(5)}&ny=${n.y.toFixed(5)}&nz=${n.z.toFixed(5)}&size=${sizeM}&kind=${attachKind}&depth=${attachDepth}`;
    const j = await (await fetch(`/api/attach?${q}`, { method: 'POST', body: blob })).json();
    if (!j.ok) throw new Error(j.error);
    log(`📌 3D-брелок вшит в ${j.patched.join(', ')}`, 'ok');
    await loadGun(currentPack, currentGun); setMode('attach');
  } catch (e) { setStatus('ошибка'); log(`3D: ${e.message}`, 'err'); } finally { spin(false); }
}

let pendingObj = null, pendingMerged = null, pendingTexCanvas = null;
let pendingScale = 1, pendingBaseScale = 1, draggingPending = false;

function buildImportMesh(root) {
  root.updateMatrixWorld(true);
  const meshes = [];
  root.traverse(o => { if (o.isMesh && o.geometry?.attributes?.position) meshes.push(o); });
  if (!meshes.length) throw new Error('в файле нет мешей');

  let mapImg = null;
  for (const m of meshes) { const mm = Array.isArray(m.material) ? m.material[0] : m.material; if (mm?.map?.image) { mapImg = mm.map.image; break; } }
  const textured = !!mapImg;
  const colors = [], colorIndex = new Map();
  if (!textured) for (const m of meshes) {
    const mm = Array.isArray(m.material) ? m.material[0] : m.material;
    const hex = mm?.color?.getHexString?.() || 'cccccc';
    if (!colorIndex.has(hex)) { colorIndex.set(hex, colors.length); colors.push(hex); }
  }

  const geos = [];
  for (const m of meshes) {
    let g = m.geometry.clone();
    if (!g.attributes.normal) g.computeVertexNormals();
    g.applyMatrix4(m.matrixWorld);
    if (g.index) g = g.toNonIndexed();
    const vc = g.attributes.position.count;
    let uvAttr = g.attributes.uv;
    if (textured) { if (!uvAttr) uvAttr = new THREE.BufferAttribute(new Float32Array(vc * 2), 2); }
    else {
      const mm = Array.isArray(m.material) ? m.material[0] : m.material;
      const hex = mm?.color?.getHexString?.() || 'cccccc';
      const slot = (colorIndex.get(hex) + 0.5) / Math.max(1, colors.length);
      const arr = new Float32Array(vc * 2); for (let i = 0; i < vc; i++) { arr[i * 2] = slot; arr[i * 2 + 1] = 0.5; }
      uvAttr = new THREE.BufferAttribute(arr, 2);
    }
    const clean = new THREE.BufferGeometry();
    clean.setAttribute('position', g.attributes.position);
    clean.setAttribute('normal', g.attributes.normal);
    clean.setAttribute('uv', uvAttr);
    geos.push(clean);
  }
  const merged = mergeGeometries(geos, false);
  if (!merged) throw new Error('не удалось объединить геометрию (разные атрибуты)');

  let texCanvas;
  if (textured) {
    const w = mapImg.width || mapImg.videoWidth || 256, h = mapImg.height || mapImg.videoHeight || 256;
    texCanvas = makeCanvas(w, h); texCanvas.getContext('2d').drawImage(mapImg, 0, 0, w, h);
  } else {
    texCanvas = makeCanvas(Math.max(1, colors.length) * 8, 8);
    const cx = texCanvas.getContext('2d'); colors.forEach((h, i) => { cx.fillStyle = '#' + h; cx.fillRect(i * 8, 0, 8, 8); });
  }
  return { geometry: merged, texCanvas };
}

async function importGlb(file) {
  if (!file) return;
  if (!model) { log('сначала выбери ган', 'err'); return; }
  spin(true); setStatus('импорт модели…');
  try {
    const buf = await file.arrayBuffer();
    const gltf = await new Promise((res, rej) => new GLTFLoader().parse(buf, '', res, rej));
    const built = buildImportMesh(gltf.scene);
    built.geometry.computeBoundingBox();
    const bb = built.geometry.boundingBox, size = new THREE.Vector3(); bb.getSize(size);
    const ctr = new THREE.Vector3(); bb.getCenter(ctr);
    built.geometry.translate(-ctr.x, -ctr.y, -ctr.z);
    const maxd = Math.max(size.x, size.y, size.z) || 1;
    pendingBaseScale = 0.12 / maxd;

    const tex = new THREE.CanvasTexture(built.texCanvas);
    tex.flipY = false; tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = maxAniso;
    const mat = new THREE.MeshStandardMaterial({ map: tex, roughness: 0.6, metalness: 0.1, side: THREE.DoubleSide });
    if (pendingObj) cancelImported();
    pendingObj = new THREE.Mesh(built.geometry, mat);
    pendingMerged = built.geometry; pendingTexCanvas = built.texCanvas; pendingScale = 1;
    const box = new THREE.Box3().setFromObject(model), gc = box.getCenter(new THREE.Vector3());
    pendingObj.position.copy(gc); applyPendingTransform();
    scene.add(pendingObj);
    setStatus('модель импортирована — двигай по гану, потом «Вшить»');
    log(`импорт: ${file.name} (${built.geometry.attributes.position.count} верт)`, 'ok');
    renderOpts();
  } catch (e) { setStatus('ошибка'); log(`импорт: ${e.message}`, 'err'); } finally { spin(false); }
}
function applyPendingTransform() { if (pendingObj) pendingObj.scale.setScalar(pendingBaseScale * pendingScale); }
function movePendingTo(ev) {
  const h = rawHit(ev); if (!h || !pendingObj) return;
  const n = h.face.normal.clone().transformDirection(h.object.matrixWorld).normalize();
  pendingObj.position.copy(h.point).addScaledVector(n, 0.01);
}
function cancelImported() {
  if (!pendingObj) return;
  scene.remove(pendingObj);
  pendingObj.geometry.dispose(); pendingObj.material.map?.dispose(); pendingObj.material.dispose();
  pendingObj = null; pendingMerged = null; pendingTexCanvas = null; draggingPending = false;
  renderOpts();
}
const _abToB64 = (ab) => { const b = new Uint8Array(ab); let s = ''; const c = 0x8000; for (let i = 0; i < b.length; i += c) s += String.fromCharCode.apply(null, b.subarray(i, i + c)); return btoa(s); };
const _taB64 = (ta) => _abToB64(ta.buffer.slice(ta.byteOffset, ta.byteOffset + ta.byteLength));
async function bakeImported() {
  if (!pendingObj) return;
  spin(true); setStatus('запекаю модель…');
  try {
    pendingObj.updateWorldMatrix(true, true);
    const g = pendingMerged, pos = g.attributes.position, nrm = g.attributes.normal, uv = g.attributes.uv;
    const mw = pendingObj.matrixWorld, nmat = new THREE.Matrix3().getNormalMatrix(mw), vc = pos.count;
    const P = new Float32Array(vc * 3), N = new Float32Array(vc * 3), U = new Float32Array(vc * 2), idx = new Int32Array(vc);
    const v = new THREE.Vector3(), n = new THREE.Vector3();
    for (let i = 0; i < vc; i++) {
      v.fromBufferAttribute(pos, i).applyMatrix4(mw);
      P[i * 3] = v.x; P[i * 3 + 1] = -v.z; P[i * 3 + 2] = v.y;
      n.fromBufferAttribute(nrm, i).applyMatrix3(nmat).normalize();
      N[i * 3] = n.x; N[i * 3 + 1] = -n.z; N[i * 3 + 2] = n.y;
      U[i * 2] = uv.getX(i); U[i * 2 + 1] = uv.getY(i);
      idx[i] = i;
    }
    if (vc > 65000) throw new Error(`модель слишком детальная (${vc} верт) — возьми low-poly`);
    const pngBlob = await new Promise(r => pendingTexCanvas.toBlob(r, 'image/png'));
    const png = await new Promise(res => { const fr = new FileReader(); fr.onload = () => res(fr.result.split(',')[1]); fr.readAsDataURL(pngBlob); });
    const body = JSON.stringify({ pos: _taB64(P), nrm: _taB64(N), uv: _taB64(U), idx: _taB64(idx), png });
    const name = `brelok_${Date.now() % 100000}`;
    if ([...texState.values()].some(s => s.dirty)) await saveDirty();
    const j = await (await fetch(`/api/attach-mesh?pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}`, { method: 'POST', body })).json();
    if (!j.ok) throw new Error(j.error);
    log(`✓ модель вшита в ${j.patched.join(', ')}`, 'ok');
    cancelImported();
    await loadGun(currentPack, currentGun); setMode('attach');
  } catch (e) { setStatus('ошибка'); log(`вшивание: ${e.message}`, 'err'); } finally { spin(false); }
}

function axisVecDrawable() {
  return anim.axis === 'x' ? [1, 0, 0] : anim.axis === 'y' ? [0, 1, 0] : [0, 0, 1];
}
async function generateAnim() {
  if (!currentGun) { log('сначала выбери ган', 'err'); return; }
  const s = stamps[activeStamp];
  if (!s) { log('выбери картинку для детали', 'err'); return; }
  const src = s.img.toDataURL ? s.img : (() => { const c = makeCanvas(s.img.naturalWidth, s.img.naturalHeight); c.getContext('2d').drawImage(s.img, 0, 0); return c; })();
  const png = src.toDataURL('image/png').split(',')[1];
  const [ax, ay, az] = axisVecDrawable();
  const body = JSON.stringify({
    sourceKind: 'pendant', png,
    size: anim.sizeCm / 100, depthFrac: anim.depth,
    animMode: anim.mode, scrollU: anim.scrollU, scrollV: anim.scrollV,
    axisX: ax, axisY: ay, axisZ: az, amplitudeDeg: anim.amplitude, periodSec: anim.period,
    attachBone: anim.bone, name: (s.name || 'anim'),
  });
  spin(true); setStatus('собираю анимированную деталь…');
  try {
    const j = await (await fetch(`/api/anim?pack=${enc(currentPack)}&gun=${enc(currentGun)}`, { method: 'POST', body })).json();
    if (!j.ok) throw new Error(j.error || 'fail');
    log(`✨ деталь собрана: ${j.files.join(', ')}`, 'ok');
    await showAnimPreview(j);
    const info = $('animInfo');
    if (info) {
      info.style.display = '';
      info.innerHTML = `Файлы в work\\_anim\\: <b>${j.detailModel}.ydr</b>, ${j.clipDict}.ycd, ${j.clipDict}.ytyp, ${j.detailModel}_comp.meta.<br>Компонент <b>${j.componentName}</b> на кости ${anim.bone}. Регистрация: см. _anim_manifest.json / _content_snippet.xml.`;
    }
    setStatus('превью анимации запущено');
  } catch (e) { setStatus('ошибка'); log(`аним-деталь: ${e.message}`, 'err'); }
  finally { spin(false); }
}

async function showAnimPreview(res) {
  clearAnimPreview();
  if (!res.previewGlb) { log('превью-GLB не собран', 'warn'); return; }
  const url = `/api/anim-file?pack=${enc(currentPack)}&gun=${enc(currentGun)}&file=${enc(res.previewGlb)}&v=${Date.now()}`;
  const glb = await new Promise((resolve, rej) => new GLTFLoader().load(url, resolve, undefined, rej));
  const obj = glb.scene;

  const materials = [];
  obj.traverse(o => {
    if (!o.isMesh) return;
    for (const m of (Array.isArray(o.material) ? o.material : [o.material])) if (m) materials.push(m);
  });

  let parent = model && anim.bone ? model.getObjectByName(anim.bone) : null;
  if (parent) { parent.add(obj); obj.position.set(0, 0, 0); }
  else if (model) {
    const box = new THREE.Box3().setFromObject(model), c = box.getCenter(new THREE.Vector3());
    obj.position.copy(c); scene.add(obj);
  } else scene.add(obj);
  obj.quaternion.identity();

  const [dx, dy, dz] = axisVecDrawable();
  const axisGlb = new THREE.Vector3(dx, dz, -dy).normalize();

  animClock.start();
  animPreview = {
    obj, materials,
    axisGlb,
    meta: { mode: res.anim.mode, periodSec: res.anim.periodSec, scrollU: res.anim.scrollU, scrollV: res.anim.scrollV, amplitudeDeg: res.anim.amplitudeDeg },
  };
}

function pushUndo(st, withBase) {
  const px = st.base.width * st.base.height, cap = px >= 8e6 ? 2 : px >= 2e6 ? 4 : 8;
  st.undo.push({ base: withBase ? copyCanvas(st.base) : null, layers: st.layers.map(l => ({ ...l })) });
  while (st.undo.length > cap) {
    let i = st.undo.findIndex((u, k) => !u.base && k < st.undo.length - 1);
    if (i < 0) i = 0;
    st.undo.splice(i, 1);
  }
  $('btnUndo').disabled = false;
}
$('btnUndo').onclick = () => {
  const st = activeSt(); if (!st) return;
  const snap = st.undo.pop(); if (!snap) return;
  if (snap.base) { st.ctxB.clearRect(0, 0, st.base.width, st.base.height); st.ctxB.drawImage(snap.base, 0, 0); }
  st.layers = snap.layers;
  if (!st.layers.some(l => l.id === selLayerId)) selLayerId = null;
  composite(st); draw2D(); rebuildLayerList(); updateHud();
  if (!st.undo.length) $('btnUndo').disabled = true;
};

async function saveDirty() {
  const dirty = [...texState.entries()].filter(([, st]) => st.dirty);
  if (!dirty.length) return true;
  spin(true); setStatus('сохранение…');
  try {
    for (const [name, st] of dirty) {
      composite(st);
      const blob = await new Promise(res => st.comp.toBlob(res, 'image/png'));
      const j = await (await fetch(`/api/texture?pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}`, { method: 'POST', body: blob })).json();
      if (!j.ok) throw new Error(j.error || 'fail');
      st.dirty = false; log(`✔ ${name} → ${j.patched.join(', ')}`, 'ok');
    }
    $('btnSave').disabled = true; $('btnReset').disabled = false;
    document.querySelectorAll('.tex-item .dirty').forEach(el => el.hidden = true);
    setStatus('сохранено в .ydr');
    return true;
  } catch (e) { setStatus('ошибка'); log(`сохранение: ${e.message}`, 'err'); return false; }
  finally { spin(false); }
}
$('btnSave').onclick = async () => {
  if (!(await saveDirty())) return;
  await loadCatalog();
  document.querySelectorAll('.gun-item').forEach(el => el.classList.toggle('active', el.dataset.pack === currentPack && el.dataset.gun === currentGun));
};
$('btnResetTex').onclick = () => {
  const st = activeSt(); if (!st) return;
  pushUndo(st, true);
  st.ctxB.clearRect(0, 0, st.base.width, st.base.height); st.ctxB.drawImage(st.orig, 0, 0);
  st.layers = []; selLayerId = null;
  if (st.chrome) { st.chrome.ctx.fillStyle = '#666'; st.chrome.ctx.fillRect(0, 0, st.chrome.canvas.width, st.chrome.canvas.height); st.chrome.tex.needsUpdate = true; }
  afterEdit(st); rebuildLayerList(); updateHud();
  log(`текстура «${st.name}» сброшена (нажми Сохранить)`, 'ok');
};
$('btnReset').onclick = async () => {
  if (!currentPack) return; spin(true);
  try { await fetch(`/api/reset?pack=${enc(currentPack)}&gun=${enc(currentGun)}`, { method: 'POST' }); log(`сброшено: ${currentPack}/${currentGun}`, 'ok'); await loadCatalog(); await loadGun(currentPack, currentGun); }
  finally { spin(false); }
};

function setMode(m) {
  if (PAINT_MODES.has(mode) && mode !== 'picker') prevPaintMode = mode;
  mode = m;
  document.querySelectorAll('.rtool').forEach(b => b.classList.toggle('active', b.dataset.mode === m));
  const leftRotates = (m === 'orbit' || m === 'glass' || m === 'anim');
  controls.mouseButtons = leftRotates
    ? { LEFT: THREE.MOUSE.ROTATE, MIDDLE: THREE.MOUSE.DOLLY, RIGHT: THREE.MOUSE.PAN }
    : { LEFT: -1, MIDDLE: THREE.MOUSE.DOLLY, RIGHT: THREE.MOUSE.ROTATE };
  canvas3d.style.cursor = leftRotates ? 'grab' : 'crosshair';
  $('brushRing').hidden = true;
  renderOpts(); updateHud();
}
document.querySelectorAll('.rtool').forEach(b => b.onclick = () => setMode(b.dataset.mode));
addEventListener('keydown', (ev) => {
  if (ev.target.tagName === 'INPUT' || ev.target.tagName === 'SELECT') return;
  if (ev.ctrlKey && ev.code === 'KeyZ') { ev.preventDefault(); $('btnUndo').click(); }
  else if (ev.ctrlKey && ev.code === 'KeyS') { ev.preventDefault(); $('btnSave').click(); }
  else if (ev.code === 'Delete' && selLayerId) deleteSelLayer();
  else if (ev.code === 'KeyO') setMode('orbit');
  else if (ev.code === 'KeyB') setMode('brush');
  else if (ev.code === 'KeyE') setMode('eraser');
  else if (ev.code === 'KeyT') setMode('text');
});

setMode('orbit');
loadCatalog().then(() => { log('каталог загружен', 'ok'); document.querySelector('.pack')?.classList.add('open'); });
