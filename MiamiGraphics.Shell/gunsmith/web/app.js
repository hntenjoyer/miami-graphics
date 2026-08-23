import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

const $ = (id) => document.getElementById(id);
const enc = encodeURIComponent;

const HG_T = window.HG_T;
const HG_SETLANG = window.HG_SETLANG;
const HG_I18N = window.HGI18N;

const svg = (d, extra = '') => `<svg class="ic" viewBox="0 0 24 24">${d}${extra}</svg>`;
const ICON = {
  text:    svg('<path d="M4 20 12 4l8 16"/><path d="M7.5 14h9"/>'),
  sticker: svg('<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><path d="m21 15-5-5L5 21"/>'),
  trash:   svg('<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M6 6v14a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V6"/><path d="M10 11v6M14 11v6"/>'),
  pin:     svg('<path d="M12 17v5"/><path d="M9 10.8V4h6v6.8l2 3.2H7z"/>'),
  glass:   svg('<path d="m12 2 8 6-8 14L4 8z"/><path d="M4 8h16"/>'),
  save:    svg('<path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><path d="M17 21v-8H7v8M7 3v5h8"/>'),
  folder:  svg('<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>'),
  pack:    svg('<path d="m21 12-9 5-9-5"/><path d="m21 17-9 5-9-5"/><path d="m12 2 9 5-9 5-9-5z"/>'),
  shape:   svg('<rect x="3" y="3" width="10" height="10" rx="1"/><circle cx="16.5" cy="16.5" r="4.5"/><path d="M3 21 9 15"/>'),
};
const SHAPE_ICON = {
  line:   svg('<path d="M4 18 20 6"/>'),
  rect:   svg('<rect x="4" y="6" width="16" height="12" rx="1"/>'),
  circle: svg('<circle cx="12" cy="12" r="8"/>'),
  tri:    svg('<path d="M12 4 21 19H3z"/>'),
};
const canvas3d = $('canvas3d');
const wrap = $('canvas3d-wrap');
const tex2d = $('tex2d');

const HG = new URLSearchParams(location.search);
const OWNER = { id: HG.get('owner') || '', name: HG.get('ownerName') || '' };
const INIT = {
  pack: HG.get('pack') || '', gun: HG.get('gun') || '',
  packName: HG.get('packName') || '', gunName: HG.get('gunName') || '',
};
let currentPackName = null, currentGunName = null;
const postToHost = (type, extra) => { try { parent.postMessage({ source: 'gunsmith', type, ...extra }, '*'); } catch {} };

const FLOW = {
  mode:        HG.get('flow')        || '',
  session:     HG.get('session')     || '',
  gun:         HG.get('gun')         || '',
  ownPackId:   HG.get('ownPackId')   || '',
  ownPackName: HG.get('ownPackName') || '',
};
const flowQS = () => (FLOW.mode === 'standard' || FLOW.mode === 'packbase')
  ? `&flow=${enc(FLOW.mode)}&flowGun=${enc(FLOW.gun)}&session=${enc(FLOW.session)}`
  : '';

window.addEventListener('message', (e) => {
  const d = e.data;
  if (!d || d.source !== 'hg-host') return;
  if (d.type === 'lang' || d.lang) HG_SETLANG(d.lang);
  if (d.type === 'lang') return;
  if (d.type === 'open') {
    FLOW.mode        = d.flow        || '';
    FLOW.session     = d.session     || '';
    FLOW.gun         = d.gun         || '';
    FLOW.ownPackId   = d.ownPackId   || '';
    FLOW.ownPackName = d.ownPackName || '';
    applyFlowChrome();
    if (d.pack && d.gun) loadGun(d.pack, d.gun, d.packName || d.pack, d.gunName || d.gun);
  }
  if (d.type === 'ownpack-finish') dismissOwnPackSuccess();
});

function applyFlowChrome() {
  const own = FLOW.mode === 'ownpack';
  const set = (id, hidden) => { const b = $(id); if (b) b.style.display = hidden ? 'none' : ''; };
  set('btnSlots', own); set('btnApply', own); set('btnSave', own);
  set('btnFlowDone', !own); set('btnFlowCancel', !own);
  $('app')?.classList.toggle('no-catalog', !!FLOW.mode);
}

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
let uvMaskCache = new Map();
let layerIdSeq = 1;
let selLayerId = null;
let overlayOnTop = true;

const brush = { color: '#ff2d95', size: 40, alpha: 1, style: 'normal' };
let attachKind = 'pendant', attachSizeCm = 10, attachDepth = 0.18;
let fillTolerance = 48;
const anim = { mode: 'uv', sizeCm: 12, depth: 0.18, scrollU: 1, period: 2.0,
  bone: 'gun_root', rotX: 0, rotY: 0, rotZ: 0, offX: 0, offY: 0, offZ: 0, mirror: false };
let animPreview = null;
const animClock = new THREE.Clock();
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
        map.offset.set(ox, oy);
      }
    }
  } else {
    let ang;
    if (m.mode === 'spin') ang = phase * Math.PI * 2;
    else ang = (m.amplitudeDeg || 0) * Math.PI / 180 * Math.sin(phase * Math.PI * 2);
    animPreview.obj.quaternion.setFromAxisAngle(animPreview.axisGlb, ang);
  }
}
function clearAnimPreview() {
  if (!animPreview) return;
  animPreview.obj.parent?.remove(animPreview.obj);
  try { disposeTree(animPreview.obj); } catch { }
  animPreview = null;
}

let statusKey = 'status.ready', statusArgs = null;
const setStatus = (key, args) => { statusKey = key; statusArgs = args || null; $('status').textContent = HG_T(key, args); };
function log(msg, cls = '') {
  const d = document.createElement('div');
  if (cls) d.className = cls;
  d.textContent = `${new Date().toLocaleTimeString()} ${msg}`;
  $('log').prepend(d);
}
const spin = (on) => $('spinner').hidden = !on;

let loadPollTimer = null;
function showLoad(text, sub) {
  let ov = $('loadOverlay');
  if (!ov) {
    ov = document.createElement('div'); ov.id = 'loadOverlay';
    ov.innerHTML = `<div class="ld-card"><div class="ld-spin"></div>
      <div class="ld-txt" id="ldTxt"></div>
      <div class="ld-bar"><i id="ldBar"></i></div>
      <div class="ld-sub" id="ldSub"></div></div>`;
    (wrap || document.body).appendChild(ov);
  }
  ov.style.display = 'flex';
  $('ldTxt').textContent = text || HG_T('load.default');
  $('ldSub').textContent = sub || HG_T('load.subFirstGun');
  $('ldBar').style.width = '4%';
}
function setLoad(text, pct) {
  const t = $('ldTxt'); if (t && text) t.textContent = text;
  const b = $('ldBar'); if (b) b.style.width = (pct != null ? Math.max(4, Math.min(100, pct)) : 100) + '%';
}
function hideLoad() {
  const ov = $('loadOverlay'); if (ov) ov.style.display = 'none';
  if (loadPollTimer) { clearInterval(loadPollTimer); loadPollTimer = null; }
}
function startLoadPoll(pack, gun) {
  if (loadPollTimer) clearInterval(loadPollTimer);
  loadPollTimer = setInterval(async () => {
    try {
      const p = await (await fetch(`/api/progress?pack=${enc(pack)}&gun=${enc(gun)}&v=${Date.now()}`)).json();
      if (p.phase === 'download') setLoad(HG_T('load.download', { pct: p.pct || 0 }), p.pct || 0);
      else if (p.phase === 'extract') setLoad(HG_T('load.extract'), null);
      else if (p.phase === 'build') setLoad(HG_T('load.build'), null);
    } catch {}
  }, 250);
}
const api = async (p) => {
  const r = await fetch(p);
  if (r.ok) return r;
  let msg = '';
  try { const j = await r.clone().json(); if (j && j.error) msg = String(j.error); } catch { }
  if (!msg) { try { msg = (await r.text()).trim().slice(0, 400); } catch { } }
  throw new Error(msg || `${r.status} ${p}`);
};
const texUrl = (name, size) => `/api/texture?pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}${size ? `&size=${size}` : ''}&v=${Date.now()}`;
const makeCanvas = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };
const copyCanvas = (s) => { const c = makeCanvas(s.width, s.height); c.getContext('2d').drawImage(s, 0, 0); return c; };
function hexToRgb(hex) { const n = parseInt(hex.slice(1), 16); return [n >> 16 & 255, n >> 8 & 255, n & 255]; }

function bindPair(rangeId, numId, apply) {
  const r = $(rangeId), n = $(numId);
  if (r) r.oninput = e => { const v = +e.target.value; if (n) n.value = v; apply(v); };
  if (n) {
    n.oninput = e => { const v = +e.target.value; if (!isFinite(v)) return; if (r) r.value = v; apply(v); };
    n.onchange = e => {
      let v = +e.target.value;
      if (!isFinite(v)) v = r ? +r.value : 0;
      if (r) { v = Math.min(+r.max, Math.max(+r.min, v)); r.value = v; }
      n.value = v; apply(v);
    };
  }
}

let allPacks = [];
async function loadCatalog() {
  allPacks = await (await api('/api/packs')).json();
  const gunCount = allPacks.reduce((n, p) => n + ((p.guns || []).length), 0);
  log(HG_T('log.catalog', {
    packs: HG_T('count.packs', { count: allPacks.length }),
    guns:  HG_T('count.gunsCatalog', { count: gunCount }),
  }), allPacks.length ? 'ok' : 'warn');
  renderCatalog('');
}
function renderCatalog(filter) {
  const root = $('packs'); root.innerHTML = '';
  const f = filter.trim().toLowerCase();
  let shown = 0;
  for (const p of allPacks) {
    const packMatch = !f || (p.name || '').toLowerCase().includes(f);
    const guns = (p.guns || []).filter(g =>
      packMatch || (g.name || '').toLowerCase().includes(f) || (g.id || '').toLowerCase().includes(f));
    if (f && guns.length === 0) continue;
    const pd = document.createElement('div');
    pd.className = 'pack' + (f ? ' open' : '');
    pd.innerHTML = `<div class="pack-head"><span>${p.name}</span><span class="cnt">${guns.length}</span></div><div class="guns"></div>`;
    pd.querySelector('.pack-head').onclick = () => pd.classList.toggle('open');
    const gunsEl = pd.querySelector('.guns');
    for (const g of guns) {
      const gi = document.createElement('div');
      gi.className = 'gun-item';
      gi.dataset.pack = g.pack ?? p.id ?? p.name; gi.dataset.gun = g.id ?? g.name;
      gi.innerHTML = `<span>${g.name}</span>${g.edited ? '<span class="badge">EDIT</span>' : ''}`;
      gi.onclick = () => loadGun(gi.dataset.pack, gi.dataset.gun, p.name, g.name);
      gunsEl.appendChild(gi);
    }
    root.appendChild(pd); shown++;
  }
  if (shown === 0)
    root.innerHTML = `<div class="pack-empty">${f ? HG_T('catalog.empty.search') : (allPacks.length ? '' : HG_T('catalog.empty.none'))}</div>`;
}
let _searchT = 0;
$('search').oninput = (e) => { clearTimeout(_searchT); _searchT = setTimeout(() => renderCatalog(e.target.value), 120); };

async function loadGun(pack, gun, packName, gunName) {
  const label = gunName || gun;
  spin(true); showLoad(HG_T('load.opening', { name: label })); startLoadPoll(pack, gun); setStatus('status.loading', { name: label });
  try {
    detail = await (await api(`/api/gun?pack=${enc(pack)}&gun=${enc(gun)}`)).json();
    setLoad(HG_T('load.build'), null);
    currentPack = pack; currentGun = gun;
    currentPackName = packName || pack; currentGunName = gunName || gun;
    if (pendingObj) cancelImported();
    if (pendingAttach) cancelAttachPreview();
    clearAnimPreview();
    for (const st of texState.values()) { st.tex3?.dispose?.(); st.chrome?.tex?.dispose?.(); }
    texState.clear(); matsByTex.clear(); meshTexName.clear(); meshBucket.clear(); uvOverlayCache.clear();
    uvMaskCache.clear(); dropPaintBuf();
    painting = false; strokeMesh = null; strokeMaskEntry = null; strokeLabel = 0; lastPx = null; strokeStepTexAllow = 0;
    activeTex = null; selLayerId = null;

    const glb = await new Promise((res, rej) =>
      new GLTFLoader().load(`/api/glb?pack=${enc(pack)}&gun=${enc(gun)}&v=${Date.now()}`, res, undefined, rej));
    if (model) { scene.remove(model); disposeTree(model); }
    model = glb.scene; scene.add(model);

    const canonTex = new Map(detail.textures.map(t => [t.name.toLowerCase(), t.name]));
    const canon = (n) => canonTex.get((n || '').toLowerCase()) ?? n;

    gunMeshes = [];
    model.traverse(o => {
      if (!o.isMesh) return;
      gunMeshes.push(o);
      for (const m of (Array.isArray(o.material) ? o.material : [o.material])) {
        const mi = /^Mat_(\d+)$/.exec(m.name || ''); if (!mi) continue;
        const sh = detail.shaders[+mi[1]]; if (!sh || !sh.diffuse) continue;
        const tn = canon(sh.diffuse);
        (matsByTex.get(tn) || matsByTex.set(tn, []).get(tn)).push(m);
        meshTexName.set(o.uuid, tn);
        meshBucket.set(o.uuid, sh.bucket ?? 0);
      }
    });

    frameCamera();
    buildTexList();
    $('dropHint').style.display = 'none';
    renderGunLabel();
    $('btnReset').disabled = !detail.edited;
    $('btnSave').disabled = false; resetHist();
    const ba = $('btnApply'); if (ba) ba.disabled = false;
    document.querySelectorAll('.gun-item').forEach(el => el.classList.toggle('active', el.dataset.pack === pack && el.dataset.gun === gun));

    const best = [...matsByTex.keys()]
      .map(n => detail.textures.find(t => t.name.toLowerCase() === n.toLowerCase()))
      .filter(Boolean).sort((a, b) => b.width * b.height - a.width * a.height)[0];
    if (best) await selectTexture(best.name);

    const gtx = detail.glass && detail.glass.textures;
    if (gtx) for (const name of Object.keys(gtx)) {
      const st = await ensureTexState(name);
      st.glass.on = true;
      if (gtx[name].opacity != null) st.glass.opacity = gtx[name].opacity;
      if (gtx[name].color) st.glass.color = gtx[name].color;
      applyGlass(st);
    }

    rebuildLayerList(); renderOpts(); updateHud();
    setStatus('status.ready'); log(HG_T('log.gunLoaded', { pack, gun }), 'ok');
  } catch (e) { setStatus('status.error'); log(HG_T('log.loadFail', { msg: e.message }), 'err'); }
  finally { spin(false); hideLoad(); }
}

function renderGunLabel() {
  const el = $('gunLabel'); if (!el) return;
  if (!detail) { el.textContent = ''; return; }
  const line = HG_T('gun.label', {
    pack: currentPackName, gun: currentGunName,
    verts: detail.verts.toLocaleString(HG_I18N.getLang()),
    textures: HG_T('count.textures', { count: detail.textures.length }),
  });
  el.textContent = line + (detail.edited ? ' · ' + HG_T('gun.edited') : '');
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
  let w = img.naturalWidth, h = img.naturalHeight;
  const maxSide = Math.max(w, h);
  if (maxSide < 256) {
    const k = Math.ceil(512 / maxSide);
    w *= k; h *= k;
    log(HG_T('log.texUpscaled', { name, from: `${img.naturalWidth}×${img.naturalHeight}`, to: `${w}×${h}` }), 'ok');
  }
  const orig = makeCanvas(w, h);
  const octx = orig.getContext('2d');
  octx.imageSmoothingEnabled = false;
  octx.drawImage(img, 0, 0, w, h);
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
    layers: [], tex3, chrome: null, dirty: false, origPattern: null, alphaOn: false,
    glass: { on: false, color: '#7fdfff', opacity: 0.4 },
  };
  texState.set(name, st);
  return st;
}
const activeSt = () => activeTex ? texState.get(activeTex) : null;
const selLayer = () => activeSt()?.layers.find(l => l.id === selLayerId) || null;

let pulseToken = 0;
function pulseTexParts(name) {
  const mats = (matsByTex.get(name) || []).filter(m => m.emissive);
  if (!mats.length) return;
  const tok = ++pulseToken;
  const saved = mats.map(m => ({ m, e: m.emissive.clone(), i: m.emissiveIntensity }));
  const t0 = performance.now(), DUR = 800;
  const step = (t) => {
    if (tok !== pulseToken) return;
    const k = Math.min(1, (t - t0) / DUR);
    const a = Math.sin(k * Math.PI) * 0.5;
    for (const { m } of saved) { m.emissive.setRGB(a * 0.55, a * 0.4, a); m.emissiveIntensity = 1; }
    if (k < 1) requestAnimationFrame(step);
    else for (const { m, e, i } of saved) { m.emissive.copy(e); m.emissiveIntensity = i ?? 1; }
  };
  requestAnimationFrame(step);
}

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
  pulseTexParts(name);
  document.querySelectorAll('.tex-item').forEach(el => el.classList.toggle('active', el.dataset.name === name));
  document.querySelector('.tex-item.active')?.scrollIntoView({ block: 'nearest' });
  rebuildLayerList(); updateHud(); draw2D();
  if (mode === 'glass') renderOpts();
}

function drawLayer(ctx, l) {
  ctx.save();
  ctx.globalAlpha = l.opacity;
  ctx.globalCompositeOperation = l.blend || 'source-over';
  ctx.translate(l.x, l.y); ctx.rotate(l.rot);
  if (l.type === 'stamp') ctx.drawImage(l.img, -l.w / 2, -l.h / 2, l.w, l.h);
  else if (l.type === 'shape') paintShape(ctx, l, l.w, l.h);
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
let _compRaf = 0, _compSt = null, _compLast = 0;
const compInterval = (st) => { const px = st.comp.width * st.comp.height; return px >= 16e6 ? 120 : px >= 4e6 ? 66 : 33; };
function strokeTexQuality(st, on) {
  const t = st?.tex3; if (!t) return;
  if (on) { if (t.generateMipmaps) { t.generateMipmaps = false; t.minFilter = THREE.LinearFilter; t.needsUpdate = true; } }
  else if (!t.generateMipmaps) { t.generateMipmaps = true; t.minFilter = THREE.LinearMipmapLinearFilter; t.needsUpdate = true; }
}
function flushComposite() {
  if (_compRaf) { cancelAnimationFrame(_compRaf); _compRaf = 0; }
  const s = _compSt; _compSt = null;
  if (s) { composite(s); draw2D(); _compLast = performance.now(); }
}
function afterEdit(st) {
  markDirty(st);
  _compSt = st;
  if (_compRaf) return;
  const tick = () => {
    _compRaf = 0;
    if (!_compSt) return;
    if (performance.now() - _compLast < compInterval(_compSt)) { _compRaf = requestAnimationFrame(tick); return; }
    const s = _compSt; _compSt = null;
    composite(s); draw2D(); _compLast = performance.now();
  };
  _compRaf = requestAnimationFrame(tick);
}

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
      const s = w / st.comp.width;
      const rectW = tex2d.getBoundingClientRect().width || w;
      const rr = (brush.size * (w / rectW)) / 2;
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
      <div class="dm">${t.width}×${t.height}${paintable ? '' : HG_T('tex.notOnModel')}</div></div>
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
  if (!st || !st.layers.length) { root.innerHTML = `<div class="layer-empty">${HG_T('layers.empty')}</div>`; return; }
  [...st.layers].reverse().forEach(l => {
    const el = document.createElement('div');
    el.className = 'layer-item' + (l.id === selLayerId ? ' active' : '');
    const nm = l.type === 'text'
      ? `${ICON.text} ${l.text.slice(0, 16)}`
      : l.type === 'shape'
        ? `${ICON.shape} ${l.name || shapeName(l.kind)}`
        : `${ICON.sticker} ${l.name || HG_T('layer.stickerDefault')}`;
    el.innerHTML = `<span class="li-name">${nm}</span>
      <button class="li-btn" data-a="up" title="${HG_T('layer.up')}">▲</button>
      <button class="li-btn" data-a="down" title="${HG_T('layer.down')}">▼</button>
      <button class="li-btn" data-a="del" title="${HG_T('layer.del')}" aria-label="${HG_T('layer.del')}">${ICON.trash}</button>`;
    el.querySelector('.li-name').onclick = () => {
      selLayerId = l.id;
      setMode(l.type === 'text' ? 'text' : l.type === 'shape' ? 'shape' : 'sticker');
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
  const show = !!l && (mode === 'sticker' || mode === 'text' || mode === 'shape');
  $('hud').hidden = !show;
  if (!show) return;
  $('hudName').textContent = l.type === 'text'
    ? HG_T('hud.textName', { text: l.text.slice(0, 14) })
    : l.type === 'shape'
      ? (l.name || shapeName(l.kind))
      : HG_T('hud.stickerName', { name: l.name || '' });
  const sc = Math.round((l.baseScale ? l.w / l.baseScale : 1) * 100);
  const rot = Math.round(l.rot * 180 / Math.PI);
  const op = Math.round(l.opacity * 100);
  $('hudScale').value = sc;   $('hudScaleN').value = sc;
  $('hudRot').value = rot;    $('hudRotN').value = rot;
  $('hudOpacity').value = op; $('hudOpacityN').value = op;
}
$('hudDel').onclick = deleteSelLayer;
bindPair('hudScale', 'hudScaleN', v => {
  const st = activeSt(), l = selLayer(); if (!l) return;
  const f = v / 100;
  if (l.type === 'stamp' || l.type === 'shape') { l.w = l.baseScale * f; l.h = l.baseH * f; }
  else { l.size = Math.max(6, Math.round(l.baseSize * f)); measureTextLayer(l); }
  afterEdit(st);
});
bindPair('hudRot', 'hudRotN', v => { const st = activeSt(), l = selLayer(); if (!l) return; l.rot = v * Math.PI / 180; afterEdit(st); });
bindPair('hudOpacity', 'hudOpacityN', v => { const st = activeSt(), l = selLayer(); if (!l) return; l.opacity = v / 100; afterEdit(st); });
enhanceNums($('hud'));
$('hudDone').onclick = () => {
  selLayerId = null;
  flushComposite(); rebuildLayerList(); updateHud(); draw2D();
  log(HG_T('log.layerDone'), 'ok');
};

function updateHud3d() {
  const el = $('hud3d'); if (!el) return;
  const isObj = !!pendingObj, isAtt = !pendingObj && !!pendingAttach;
  const show = mode === 'attach' && (isObj || isAtt);
  el.hidden = !show;
  if (!show) return;
  $('hud3dName').textContent = isObj ? HG_T('hud3d.ownModel') : HG_T('hud3d.pendant');
  const rot = isObj ? Math.round(pendingObj.rotation.y * 180 / Math.PI) : 0;
  const sc  = isObj ? Math.round(pendingScale * 100) : 0;
  $('hud3dRows').innerHTML = isObj
    ? `<label>${HG_T('field.size')}<input type="range" id="h3Scale" min="10" max="400" value="${sc}"><input type="number" class="num" id="h3ScaleN" min="10" max="400" value="${sc}"></label>
       <label>${HG_T('field.rot')}<input type="range" id="h3Rot" min="-180" max="180" value="${rot}"><input type="number" class="num" id="h3RotN" min="-180" max="180" value="${rot}"></label>`
    : `<label>${HG_T('field.size')}<input type="range" id="h3Size" min="2" max="40" value="${attachSizeCm}"><input type="number" class="num" id="h3SizeN" min="2" max="40" value="${attachSizeCm}"></label>
       <label>${HG_T('field.depth')}<input type="range" id="h3Depth" min="5" max="45" value="${Math.round(attachDepth * 100)}"><input type="number" class="num" id="h3DepthN" min="5" max="45" value="${Math.round(attachDepth * 100)}"></label>`;
  enhanceNums(el);
  if (isObj) {
    bindPair('h3Scale', 'h3ScaleN', v => { pendingScale = v / 100; applyPendingTransform(); });
    bindPair('h3Rot', 'h3RotN', v => { pendingObj.rotation.y = v * Math.PI / 180; });
  } else {
    bindPair('h3Size', 'h3SizeN', v => { attachSizeCm = v; updateAttachPreviewScale(); });
    bindPair('h3Depth', 'h3DepthN', v => attachDepth = v / 100);
  }
  $('hud3dApply').onclick = () => { if (isObj) bakeImported(); else bakeAttachPreview(); };
  $('hud3dDel').onclick   = () => { if (isObj) cancelImported(); else cancelAttachPreview(); };
}

const stamps = [];
let activeStamp = 0;
function makeStamp(name, fn) { const c = makeCanvas(256, 256); fn(c.getContext('2d')); stamps.push({ name, img: c }); }
makeStamp('star', ctx => { ctx.fillStyle = '#ffd600'; ctx.strokeStyle = '#3a2c00'; ctx.lineWidth = 8; ctx.beginPath(); for (let i = 0; i < 10; i++) { const r = i % 2 ? 52 : 120, a = -Math.PI / 2 + i * Math.PI / 5; ctx[i ? 'lineTo' : 'moveTo'](128 + r * Math.cos(a), 128 + r * Math.sin(a)); } ctx.closePath(); ctx.fill(); ctx.stroke(); });
makeStamp('heart', ctx => { ctx.fillStyle = '#ff2d55'; ctx.beginPath(); ctx.moveTo(128, 224); ctx.bezierCurveTo(-40, 110, 52, -18, 128, 74); ctx.bezierCurveTo(204, -18, 296, 110, 128, 224); ctx.fill(); });
makeStamp('smile', ctx => { ctx.fillStyle = '#ffde34'; ctx.beginPath(); ctx.arc(128, 128, 110, 0, 7); ctx.fill(); ctx.fillStyle = '#222'; ctx.beginPath(); ctx.arc(88, 100, 14, 0, 7); ctx.fill(); ctx.beginPath(); ctx.arc(168, 100, 14, 0, 7); ctx.fill(); ctx.strokeStyle = '#222'; ctx.lineWidth = 12; ctx.lineCap = 'round'; ctx.beginPath(); ctx.arc(128, 128, 62, .35, Math.PI - .35); ctx.stroke(); });
makeStamp('MIAMI', ctx => { ctx.font = '900 60px Impact, sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.lineWidth = 10; ctx.strokeStyle = '#000'; ctx.strokeText('MIAMI', 128, 128); ctx.fillStyle = '#ff2d95'; ctx.fillText('MIAMI', 128, 128); });
makeStamp('paw', ctx => { ctx.fillStyle = '#1b1b1b'; const p = (x, y, rx, ry) => { ctx.beginPath(); ctx.ellipse(x, y, rx, ry, 0, 0, 7); ctx.fill(); }; p(128, 160, 58, 48); p(66, 92, 22, 30); p(112, 66, 22, 30); p(158, 66, 22, 30); p(200, 92, 22, 30); });

function stampGridHtml() {
  return `<div class="stamp-grid">${stamps.map((s, i) => s.hidden ? '' :
    `<img class="stamp-thumb${i === activeStamp ? ' active' : ''}" data-i="${i}" title="${s.name}" src="${s.img.toDataURL ? s.img.toDataURL() : s.img.src}">`).join('')}</div>
    <label class="file-btn">${HG_T('stamp.uploadPng')}<input type="file" id="stampFile" accept="image/png,image/webp,image/jpeg" hidden></label>`;
}
function wireStampGrid() {
  document.querySelectorAll('.stamp-thumb').forEach(im => im.onclick = () => {
    activeStamp = +im.dataset.i;
    selLayerId = null;
    renderOpts();
  });
  const f = $('stampFile');
  if (f) f.onchange = async (e) => {
    const file = e.target.files[0]; if (!file) return;
    const img = new Image(); img.src = URL.createObjectURL(file); await img.decode();
    const c = makeCanvas(img.naturalWidth, img.naturalHeight); c.getContext('2d').drawImage(img, 0, 0);
    stamps.push({ name: file.name.replace(/\.\w+$/, ''), img: c });
    activeStamp = stamps.length - 1; renderOpts();
    log(HG_T('log.imgLoaded', { name: file.name }), 'ok');
  };
}

const SHAPE_KINDS = ['line', 'rect', 'circle', 'tri'];
const shapeCfg = { kind: 'rect', color: '#ff2d95', fill: false, lineW: 16, radius: 0 };
let shapeProject = true;
let shapeStampIdx = -1;

const shapeName = (kind) => HG_T('shape.kind.' + kind);

function shapePath(ctx, kind, w, h, radius) {
  const x = -w / 2, y = -h / 2;
  ctx.beginPath();
  if (kind === 'line') { ctx.moveTo(x, 0); ctx.lineTo(x + w, 0); return; }
  if (kind === 'circle') { ctx.ellipse(0, 0, Math.abs(w) / 2, Math.abs(h) / 2, 0, 0, Math.PI * 2); ctx.closePath(); return; }
  if (kind === 'tri') { ctx.moveTo(0, y); ctx.lineTo(x + w, y + h); ctx.lineTo(x, y + h); ctx.closePath(); return; }
  const r = Math.max(0, Math.min(radius, Math.min(Math.abs(w), Math.abs(h)) / 2));
  if (r > 0 && ctx.roundRect) ctx.roundRect(x, y, w, h, r);
  else ctx.rect(x, y, w, h);
  ctx.closePath();
}

function paintShape(ctx, cfg, w, h) {
  ctx.lineJoin = 'round'; ctx.lineCap = 'round';
  shapePath(ctx, cfg.kind, w, h, cfg.radius || 0);
  if (cfg.fill && cfg.kind !== 'line') { ctx.fillStyle = cfg.color; ctx.fill(); }
  else { ctx.strokeStyle = cfg.color; ctx.lineWidth = Math.max(1, cfg.lineW); ctx.stroke(); }
}

function shapeStampCanvas(cfg) {
  const S = 1024, pad = Math.round(S * 0.10), box = S - pad * 2;
  const lineH = Math.max(4, cfg.lineW * 4);
  const c = makeCanvas(S, cfg.kind === 'line' ? pad * 2 + lineH : S);
  const ctx = c.getContext('2d');
  ctx.translate(c.width / 2, c.height / 2);
  paintShape(ctx, { ...cfg, lineW: cfg.lineW * (box / 256) }, box, cfg.kind === 'line' ? lineH : box);
  return c;
}

function refreshShapeStamp() {
  const entry = { name: shapeName(shapeCfg.kind), img: shapeStampCanvas(shapeCfg), hidden: true };
  if (shapeStampIdx < 0) { stamps.push(entry); shapeStampIdx = stamps.length - 1; }
  else stamps[shapeStampIdx] = entry;
  return shapeStampIdx;
}

async function bakeShapeAtEvent(ev) {
  refreshShapeStamp();
  const prev = activeStamp;
  activeStamp = shapeStampIdx;
  try { await bakeAtEvent(ev); } finally { activeStamp = prev; }
}

function placeShapeAt(st, x, y) {
  const w = st.comp.width * 0.15;
  const h = shapeCfg.kind === 'line' ? Math.max(2, shapeCfg.lineW) : w;
  const l = addLayer(st, {
    type: 'shape', kind: shapeCfg.kind, name: shapeName(shapeCfg.kind),
    color: shapeCfg.color, fill: shapeCfg.fill, lineW: shapeCfg.lineW, radius: shapeCfg.radius,
    x, y, w, h, baseScale: w, baseH: h, rot: 0, opacity: brush.alpha, blend: 'source-over',
  });
  log(HG_T('log.shapePlaced', { name: shapeName(shapeCfg.kind) }), 'ok');
  return l;
}
async function placeShape() {
  const at = await placePoint();
  if (!at) { log(HG_T('log.needGunTex'), 'err'); return; }
  placeShapeAt(at.st, at.x, at.y);
}

const toolTitle = (m) => HG_T('tool.' + m);

function rgbToHex(r, g, b) {
  return '#' + [r, g, b].map(v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join('');
}
function hsvToRgb(h, s, v) {
  const i = Math.floor(h * 6), f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
  const [r, g, b] = [[v, t, p], [q, v, p], [p, v, t], [p, q, v], [t, p, v], [v, p, q]][i % 6];
  return [r * 255, g * 255, b * 255];
}
function rgbToHsv(r, g, b) {
  r /= 255; g /= 255; b /= 255;
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
  let h = 0;
  if (d) {
    if (mx === r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
    else if (mx === g) h = ((b - r) / d + 2) / 6;
    else h = ((r - g) / d + 4) / 6;
  }
  return [h, mx ? d / mx : 0, mx];
}

const _wheelCache = new Map();
function wheelCanvas(size) {
  if (_wheelCache.has(size)) return _wheelCache.get(size);
  const c = makeCanvas(size, size), ctx = c.getContext('2d');
  const img = ctx.createImageData(size, size), d = img.data, R = size / 2;
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const dx = x - R + 0.5, dy = y - R + 0.5, dist = Math.hypot(dx, dy), o = (y * size + x) * 4;
    if (dist > R) { d[o + 3] = 0; continue; }
    const h = (Math.atan2(dy, dx) / (Math.PI * 2) + 1) % 1;
    const [r, g, b] = hsvToRgb(h, Math.min(1, dist / R), 1);
    d[o] = r; d[o + 1] = g; d[o + 2] = b;
    d[o + 3] = dist > R - 1 ? Math.round(255 * (R - dist)) : 255;
  }
  ctx.putImageData(img, 0, 0);
  _wheelCache.set(size, c);
  return c;
}

const CW_SIZE = 132;
const colorWheelHtml = (id, label) => `
  <div class="cw" id="${id}">
    <div class="cw-head"><span class="cw-label">${label}</span>
      <span class="cw-chip" id="${id}_chip"></span>
      <input class="cw-hex" id="${id}_hex" maxlength="7" spellcheck="false"></div>
    <div class="cw-wrap"><canvas class="cw-wheel" id="${id}_wheel" width="${CW_SIZE}" height="${CW_SIZE}"></canvas>
      <i class="cw-dot" id="${id}_dot"></i></div>
    <div class="cw-val" id="${id}_val"><i class="cw-vdot" id="${id}_vdot"></i></div>
  </div>`;

function bindColorWheel(id, get, set) {
  const root = $(id); if (!root) return;
  const wheel = $(id + '_wheel'), dot = $(id + '_dot'), val = $(id + '_val'),
        vdot = $(id + '_vdot'), chip = $(id + '_chip'), hex = $(id + '_hex');
  const ctx = wheel.getContext('2d');
  let [h, s, v] = rgbToHsv(...hexToRgb(get() || '#ffffff'));

  const paint = () => {
    ctx.clearRect(0, 0, CW_SIZE, CW_SIZE);
    ctx.drawImage(wheelCanvas(CW_SIZE), 0, 0);
    if (v < 1) { ctx.save(); ctx.globalCompositeOperation = 'multiply';
      ctx.fillStyle = `rgba(0,0,0,${1 - v})`; ctx.beginPath();
      ctx.arc(CW_SIZE / 2, CW_SIZE / 2, CW_SIZE / 2, 0, 7); ctx.fill(); ctx.restore(); }
    const R = CW_SIZE / 2, a = h * Math.PI * 2, rr = s * (R - 1);
    dot.style.left = (R + Math.cos(a) * rr) + 'px';
    dot.style.top  = (R + Math.sin(a) * rr) + 'px';
    const hexNow = rgbToHex(...hsvToRgb(h, s, v));
    dot.style.background = hexNow;
    chip.style.background = hexNow;
    if (document.activeElement !== hex) hex.value = hexNow;
    val.style.background = `linear-gradient(90deg, #000, ${rgbToHex(...hsvToRgb(h, s, 1))})`;
    vdot.style.left = (v * 100) + '%';
    return hexNow;
  };
  const push = () => set(paint());

  const wheelAt = (ev) => {
    const r = wheel.getBoundingClientRect(), R = r.width / 2;
    const dx = ev.clientX - r.left - R, dy = ev.clientY - r.top - R;
    h = (Math.atan2(dy, dx) / (Math.PI * 2) + 1) % 1;
    s = Math.min(1, Math.hypot(dx, dy) / R);
    push();
  };
  const valAt = (ev) => {
    const r = val.getBoundingClientRect();
    v = Math.max(0, Math.min(1, (ev.clientX - r.left) / r.width));
    push();
  };
  const drag = (el, fn) => el.addEventListener('pointerdown', (ev) => {
    ev.preventDefault(); el.setPointerCapture(ev.pointerId); fn(ev);
    const mv = (e) => fn(e);
    const up = () => { el.removeEventListener('pointermove', mv); el.removeEventListener('pointerup', up); };
    el.addEventListener('pointermove', mv); el.addEventListener('pointerup', up);
  });
  drag(wheel, wheelAt);
  drag(val, valAt);
  hex.oninput = () => {
    const t = hex.value.trim();
    if (!/^#[0-9a-f]{6}$/i.test(t)) return;
    [h, s, v] = rgbToHsv(...hexToRgb(t));
    set(paint());
  };
  paint();
}
const sizeRow = () => `<div class="field"><label>${HG_T('field.size')}</label><input type="range" id="oSize" min="2" max="250" value="${brush.size}"><input type="number" class="num" id="oSizeN" min="2" max="250" value="${brush.size}"></div>`;
const strengthRow = () => `<div class="field"><label>${HG_T('field.strength')}</label><input type="range" id="oAlpha" min="5" max="100" value="${Math.round(brush.alpha * 100)}"><input type="number" class="num" id="oAlphaN" min="5" max="100" value="${Math.round(brush.alpha * 100)}"></div>`;
const colorRow = () => colorWheelHtml('oCW', HG_T('field.color'));

function renderOpts() {
  $('optsTitle').textContent = HG_T('opts.titleFmt', { tool: toolTitle(mode) });
  updateHud3d();
  const b = $('optsBody');
  if (mode === 'orbit') { b.innerHTML = `<div class="hint">${HG_T('hint.orbit')}</div>`; return; }
  if (mode === 'brush') {
    b.innerHTML = colorRow() +
      `<div class="field"><label>${HG_T('field.style')}</label><select id="oStyle">
        <option value="normal">${HG_T('brush.style.normal')}</option><option value="soft">${HG_T('brush.style.soft')}</option>
        <option value="spray">${HG_T('brush.style.spray')}</option><option value="marker">${HG_T('brush.style.marker')}</option></select><span></span></div>` +
      sizeRow() + strengthRow() +
      `<div class="hint">${HG_T('hint.brush1')}</div>
       <div class="hint">${HG_T('hint.brush2')}</div>`;
    $('oStyle').value = brush.style;
    $('oStyle').onchange = e => brush.style = e.target.value;
  } else if (mode === 'eraser') {
    b.innerHTML = sizeRow() + strengthRow() + `<div class="hint">${HG_T('hint.eraser')}</div>`;
  } else if (mode === 'fill') {
    b.innerHTML = colorRow() +
      `<div class="field"><label>${HG_T('field.tolerance')}</label><input type="range" id="oTol" min="1" max="120" value="${fillTolerance}"><input type="number" class="num" id="oTolN" min="1" max="120" value="${fillTolerance}"></div>
       <div class="hint">${HG_T('hint.fill')}</div>`;
    bindPair('oTol', 'oTolN', v => fillTolerance = v);
  } else if (mode === 'picker') {
    b.innerHTML = `<div class="hint">${HG_T('hint.picker')}</div>`;
  } else if (mode === 'alpha') {
    b.innerHTML = sizeRow() + strengthRow() +
      `<div class="hint warn">${HG_T('hint.alpha')}</div>`;
  } else if (mode === 'glass') {
    const st = activeSt(); const g = st?.glass || { on: false, color: '#7fdfff', opacity: 0.4 };
    const anyGlass = [...texState.values()].some(s => s.glass?.on);
    const escT = s => String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const applyTip = FLOW.mode === 'ownpack'
      ? HG_T('glass.applyTip.ownpack')
      : HG_T('glass.applyTip.game');
    b.innerHTML =
      colorWheelHtml('oGlassCW', HG_T('field.glassColor')) +
      `<div class="field"><label>${HG_T('field.opacity')}</label><input type="range" id="oGlassOp" min="10" max="92" value="${Math.round(g.opacity * 100)}"><input type="number" class="num" id="oGlassOpN" min="10" max="92" value="${Math.round(g.opacity * 100)}"></div>
       <div class="opt-btns"><button class="btn ${g.on ? 'on' : ''}" id="oGlassOne" ${activeTex ? '' : 'disabled'}>${ICON.pin} ${g.on ? HG_T('glass.btn.removeOne') : HG_T('glass.btn.addOne')}</button></div>
       <div class="opt-btns"><button class="btn primary" id="oGlassAll">${ICON.glass} ${HG_T('glass.btn.all')}</button></div>` +
      (anyGlass ? `<div class="opt-btns"><button class="btn" id="oGlassNone">${ICON.trash} ${HG_T('glass.btn.removeAll')}</button></div>` : '') +
      `<div class="hint">${activeTex
          ? HG_T('glass.hint.selected', { tex: escT(activeTex) })
          : HG_T('glass.hint.none')}
        ${HG_T('glass.hint.tail', { applyTip })}</div>`;
    bindColorWheel('oGlassCW',
      () => activeSt()?.glass?.color || '#7fdfff',
      (c) => { const s = activeSt(); if (!s) return; s.glass.color = c; applyGlassAllOn(c, null); persistGlassOn(); });
    bindPair('oGlassOp', 'oGlassOpN', v => { applyGlassAllOn(null, v / 100); });
    { const op = $('oGlassOp'), opn = $('oGlassOpN'); if (op) op.onchange = () => persistGlassOn(); if (opn) opn.onchange = () => persistGlassOn(); }
    $('oGlassOne').onclick = () => glassSetOne(activeTex, !g.on);
    $('oGlassAll').onclick = () => glassSetAll(true);
    const none = $('oGlassNone'); if (none) none.onclick = () => glassSetAll(false);
  } else if (mode === 'sticker') {
    const szPct = Math.round((bakeSess?.sizeFrac ?? 0.18) * 100);
    const rotDeg = Math.round(bakeSess?.rotDeg ?? 0);
    b.innerHTML = stampGridHtml() +
      `<label class="mini"><input type="checkbox" id="oProj" ${stickerProject ? 'checked' : ''}> ${HG_T('sticker.proj')}</label>` +
      (stickerProject
        ? `<div class="field"><label>${HG_T('field.size')}</label><input type="range" id="oPrSize" min="4" max="70" value="${szPct}"><input type="number" class="num" id="oPrSizeN" min="4" max="70" value="${szPct}"></div>
           <div class="field"><label>${HG_T('field.rot')}</label><input type="range" id="oPrRot" min="-180" max="180" value="${rotDeg}"><input type="number" class="num" id="oPrRotN" min="-180" max="180" value="${rotDeg}"></div>
           <div class="hint">${HG_T('hint.stickerProj')}</div>`
        : `<label class="mini"><input type="checkbox" id="oTop" ${overlayOnTop ? 'checked' : ''}> ${HG_T('sticker.onTop')}</label>
           <div class="opt-btns"><button class="btn" id="oAdd">${HG_T('btn.addCenter')}</button></div>
           <div class="hint">${HG_T('hint.stickerFlat')}</div>`);
    wireStampGrid();
    $('oProj').onchange = e => { stickerProject = e.target.checked; renderOpts(); };
    if (stickerProject) {
      bindPair('oPrSize', 'oPrSizeN', v => { if (bakeSess) { bakeSess.sizeFrac = v / 100; queueBake(); } });
      bindPair('oPrRot', 'oPrRotN', v => { if (bakeSess) { bakeSess.rotDeg = v; queueBake(); } });
    } else {
      $('oTop').onchange = e => overlayOnTop = e.target.checked;
      $('oAdd').onclick = () => placeSticker();
    }
  } else if (mode === 'shape') {
    const sel = selLayer()?.type === 'shape' ? selLayer() : null;
    const cur = sel || shapeCfg;
    const szPct = Math.round((bakeSess?.sizeFrac ?? 0.18) * 100);
    const rotDeg = Math.round(bakeSess?.rotDeg ?? 0);
    b.innerHTML =
      `<div class="shape-grid">${SHAPE_KINDS.map(k =>
        `<button class="btn shape-btn${k === cur.kind ? ' on' : ''}" data-k="${k}" title="${shapeName(k)}">${SHAPE_ICON[k]}</button>`).join('')}</div>` +
      colorWheelHtml('oShapeCW', HG_T('field.color')) +
      `<label class="mini"><input type="checkbox" id="oShFill" ${cur.fill ? 'checked' : ''} ${cur.kind === 'line' ? 'disabled' : ''}> ${HG_T('shape.fill')}</label>
       <div class="field"><label>${HG_T('shape.lineW')}</label><input type="range" id="oShW" min="1" max="80" value="${cur.lineW}"><input type="number" class="num" id="oShWN" min="1" max="80" value="${cur.lineW}"></div>` +
      (cur.kind === 'rect'
        ? `<div class="field"><label>${HG_T('shape.radius')}</label><input type="range" id="oShR" min="0" max="200" value="${cur.radius || 0}"><input type="number" class="num" id="oShRN" min="0" max="200" value="${cur.radius || 0}"></div>`
        : '') +
      `<label class="mini"><input type="checkbox" id="oShProj" ${shapeProject ? 'checked' : ''}> ${HG_T('shape.proj')}</label>` +
      (shapeProject
        ? `<div class="field"><label>${HG_T('field.size')}</label><input type="range" id="oShSize" min="4" max="70" value="${szPct}"><input type="number" class="num" id="oShSizeN" min="4" max="70" value="${szPct}"></div>
           <div class="field"><label>${HG_T('field.rot')}</label><input type="range" id="oShRot" min="-180" max="180" value="${rotDeg}"><input type="number" class="num" id="oShRotN" min="-180" max="180" value="${rotDeg}"></div>
           <div class="hint">${HG_T('hint.shapeProj')}</div>`
        : `<div class="opt-btns"><button class="btn" id="oShAdd">${HG_T('btn.addCenter')}</button></div>
           <div class="hint">${HG_T('hint.shapeFlat')}</div>`);

    const live = () => {
      const l = selLayer();
      if (l?.type === 'shape') {
        Object.assign(l, { kind: shapeCfg.kind, color: shapeCfg.color, fill: shapeCfg.fill,
                           lineW: shapeCfg.lineW, radius: shapeCfg.radius, name: shapeName(shapeCfg.kind) });
        afterEdit(activeSt()); rebuildLayerList(); updateHud();
      }
      if (shapeProject && bakeSess && bakeSess.imgIdx === shapeStampIdx) { refreshShapeStamp(); queueBake(); }
    };

    document.querySelectorAll('.shape-btn').forEach(btn => btn.onclick = () => {
      shapeCfg.kind = btn.dataset.k;
      if (shapeCfg.kind === 'line') shapeCfg.fill = false;
      live(); renderOpts();
    });
    bindColorWheel('oShapeCW', () => shapeCfg.color, (c) => { shapeCfg.color = c; live(); });
    $('oShFill').onchange = e => { shapeCfg.fill = e.target.checked; live(); };
    bindPair('oShW', 'oShWN', v => { shapeCfg.lineW = v; live(); });
    if ($('oShR')) bindPair('oShR', 'oShRN', v => { shapeCfg.radius = v; live(); });
    $('oShProj').onchange = e => { shapeProject = e.target.checked; renderOpts(); };
    if (shapeProject) {
      bindPair('oShSize', 'oShSizeN', v => { if (bakeSess) { bakeSess.sizeFrac = v / 100; queueBake(); } });
      bindPair('oShRot', 'oShRotN', v => { if (bakeSess) { bakeSess.rotDeg = v; queueBake(); } });
    } else {
      $('oShAdd').onclick = () => placeShape();
    }
  } else if (mode === 'text') {
    const sel = selLayer()?.type === 'text' ? selLayer() : null;
    const cur = sel || { text: 'MIAMI', font: 'Unbounded', size: 160, bold: true, color: '#ff2d95', outline: '#000000', outlineW: 8 };
    const fonts = ['Unbounded', 'Impact', 'Arial Black', 'Manrope', 'Georgia', 'Consolas'];
    b.innerHTML =
      `<input type="text" id="oText" placeholder="${HG_T('text.placeholder')}" value="${cur.text.replace(/"/g, '&quot;')}">
       <div class="row"><select id="oFont" style="flex:1">${fonts.map(f => `<option ${f === cur.font ? 'selected' : ''}>${f}</option>`).join('')}</select>
       <label class="mini"><input type="checkbox" id="oBold" ${cur.bold ? 'checked' : ''}> ${HG_T('text.bold')}</label></div>
       <div class="field"><label>${HG_T('field.size')}</label><input type="range" id="oTSize" min="24" max="800" value="${cur.size}"><input type="number" class="num" id="oTSizeN" min="24" max="800" value="${cur.size}"></div>
       <div class="row"><label style="color:var(--muted);font-size:12px">${HG_T('field.color')}</label><input type="color" id="oTColor" value="${cur.color}">
       <label style="color:var(--muted);font-size:12px;margin-left:8px">${HG_T('field.outline')}</label><input type="color" id="oTOutline" value="${cur.outline}">
       <input type="number" id="oTOutlineW" min="0" max="60" value="${cur.outlineW}" style="width:50px" ${cur.outlineW === 0 ? 'disabled' : ''}></div>
       <label class="mini"><input type="checkbox" id="oNoOutline" ${cur.outlineW === 0 ? 'checked' : ''}> ${HG_T('text.noOutline')}</label>
       <div class="opt-btns"><button class="btn" id="oAddText">${HG_T('btn.addCenter')}</button></div>
       <div class="hint">${sel ? HG_T('hint.textEdit') : HG_T('hint.textNew')}</div>`;
    const live = () => {
      const l = selLayer(); if (l?.type !== 'text') return;
      Object.assign(l, textCfgFromOpts()); l.baseSize = l.size; measureTextLayer(l);
      afterEdit(activeSt()); updateHud();
    };
    for (const id of ['oText', 'oFont', 'oBold', 'oTColor', 'oTOutline', 'oTOutlineW']) $(id).oninput = live;
    bindPair('oTSize', 'oTSizeN', () => live());
    $('oNoOutline').onchange = e => { $('oTOutlineW').disabled = e.target.checked; live(); };
    $('oAddText').onclick = () => placeText();
  } else if (mode === 'attach') {
    attachKind = 'pendant';
    const imp = pendingObj
      ? `<div class="hint">${HG_T('hint.glbPending')}</div>`
      : `<label class="file-btn">${HG_T('attach.uploadGlb')}<input type="file" id="glbFile" accept=".glb,model/gltf-binary" hidden></label>
         <div class="hint">${HG_T('hint.glbImport')}</div>`;
    const brelok = pendingAttach
      ? `<div class="hint">${HG_T('hint.pendantPending')}</div>`
      : stampGridHtml() +
        `<div class="hint">${HG_T('hint.pendantNew')}</div>`;
    b.innerHTML =
      `<div class="card-h" style="margin:0 0 6px">${HG_T('attach.h.pendant')}</div>` +
      brelok +
      `<div class="card-h" style="margin:10px 0 6px">${HG_T('attach.h.ownModel')}</div>
       ${imp}
       <div class="card-h" style="margin:10px 0 4px">${HG_T('attach.h.baked')}</div>
       <div id="accList" class="layer-list"><div class="layer-empty">${HG_T('common.loading')}</div></div>`;
    if (!pendingAttach) wireStampGrid();
    if (!pendingObj) $('glbFile').onchange = e => importGlb(e.target.files[0]);
    loadAccessories();
  } else if (mode === 'anim') {
    anim.mode = 'uv';
    if (!(anim.scrollU > 0)) anim.scrollU = 1;
    const bones = ['gun_root', ...(detail?.boneNames || [])];
    if (!bones.includes(anim.bone)) anim.bone = 'gun_root';
    const boneOpts = bones.map(bn => `<option ${bn === anim.bone ? 'selected' : ''}>${bn}</option>`).join('');
    b.innerHTML =
      stampGridHtml() +
      `<div class="field"><label>${HG_T('field.size')}</label><input type="range" id="oAnimSize" min="2" max="40" value="${anim.sizeCm}"><input type="number" class="num" id="oAnimSizeN" min="2" max="40" value="${anim.sizeCm}"></div>
       <div class="field"><label>${HG_T('field.depth')}</label><input type="range" id="oAnimDepth" min="5" max="45" value="${Math.round(anim.depth * 100)}"><input type="number" class="num" id="oAnimDepthN" min="5" max="45" value="${Math.round(anim.depth * 100)}"></div>
       <div class="field"><label>${HG_T('field.speed')}</label><input type="range" id="oScrollU" min="0.5" max="4" step="0.5" value="${anim.scrollU}"><input type="number" class="num" id="oScrollUN" min="0.5" max="4" step="0.5" value="${anim.scrollU}"></div>
       <div class="field"><label>${HG_T('field.period')}</label><input type="range" id="oPeriod" min="0.5" max="8" step="0.5" value="${anim.period}"><input type="number" class="num" id="oPeriodN" min="0.5" max="8" step="0.5" value="${anim.period}"></div>
       <div class="field"><label>${HG_T('field.bone')}</label><select id="oBone">${boneOpts}</select></div>
       <div class="field"><label>${HG_T('field.rotX')}</label><input type="range" id="oRotX" min="-180" max="180" step="5" value="${anim.rotX}"><input type="number" class="num" id="oRotXN" min="-180" max="180" value="${anim.rotX}"></div>
       <div class="field"><label>${HG_T('field.rotY')}</label><input type="range" id="oRotY" min="-180" max="180" step="5" value="${anim.rotY}"><input type="number" class="num" id="oRotYN" min="-180" max="180" value="${anim.rotY}"></div>
       <div class="field"><label>${HG_T('field.rotZ')}</label><input type="range" id="oRotZ" min="-180" max="180" step="5" value="${anim.rotZ}"><input type="number" class="num" id="oRotZN" min="-180" max="180" value="${anim.rotZ}"></div>
       <div class="field"><label>${HG_T('field.offX')}</label><input type="range" id="oOffX" min="-20" max="20" step="0.5" value="${anim.offX * 100}"><input type="number" class="num" id="oOffXN" min="-20" max="20" step="0.5" value="${anim.offX * 100}"></div>
       <div class="field"><label>${HG_T('field.offY')}</label><input type="range" id="oOffY" min="-20" max="20" step="0.5" value="${anim.offY * 100}"><input type="number" class="num" id="oOffYN" min="-20" max="20" step="0.5" value="${anim.offY * 100}"></div>
       <div class="field"><label>${HG_T('field.offZ')}</label><input type="range" id="oOffZ" min="-20" max="20" step="0.5" value="${anim.offZ * 100}"><input type="number" class="num" id="oOffZN" min="-20" max="20" step="0.5" value="${anim.offZ * 100}"></div>
       <label class="mini"><input type="checkbox" id="oMirror" ${anim.mirror ? 'checked' : ''}> ${HG_T('anim.mirror')}</label>
       <div class="opt-btns"><button class="btn primary" id="oAnimGen">${HG_T('btn.preview')}</button></div>
       <div class="hint">${HG_T('hint.anim')}</div>`;
    wireStampGrid();
    bindPair('oAnimSize', 'oAnimSizeN', v => anim.sizeCm = v);
    bindPair('oAnimDepth', 'oAnimDepthN', v => anim.depth = v / 100);
    bindPair('oScrollU', 'oScrollUN', v => anim.scrollU = v);
    bindPair('oPeriod', 'oPeriodN', v => { anim.period = v; if (animPreview) animPreview.meta.periodSec = v; });
    $('oBone').onchange = e => { anim.bone = e.target.value; };
    bindPair('oRotX', 'oRotXN', v => anim.rotX = v);
    bindPair('oRotY', 'oRotYN', v => anim.rotY = v);
    bindPair('oRotZ', 'oRotZN', v => anim.rotZ = v);
    bindPair('oOffX', 'oOffXN', v => anim.offX = v / 100);
    bindPair('oOffY', 'oOffYN', v => anim.offY = v / 100);
    bindPair('oOffZ', 'oOffZN', v => anim.offZ = v / 100);
    { const mc = $('oMirror'); if (mc) mc.onchange = e => { anim.mirror = e.target.checked; }; }
    $('oAnimGen').onclick = () => generateAnim();
  }
  bindPair('oSize', 'oSizeN', v => brush.size = v);
  bindPair('oAlpha', 'oAlphaN', v => brush.alpha = v / 100);
  bindColorWheel('oCW', () => brush.color, (c) => { brush.color = c; });
  enhanceNums($('optsBody'));
}

function enhanceNums(root) {
  (root || document).querySelectorAll('input.num:not([data-enh])').forEach(inp => {
    inp.setAttribute('data-enh', '1');
    const wrap = document.createElement('span');
    wrap.className = 'numwrap';
    inp.parentNode.insertBefore(wrap, inp);
    const dec = document.createElement('button'); dec.type = 'button'; dec.className = 'nstep dec'; dec.textContent = '−'; dec.tabIndex = -1;
    const inc = document.createElement('button'); inc.type = 'button'; inc.className = 'nstep inc'; inc.textContent = '+'; inc.tabIndex = -1;
    wrap.appendChild(dec); wrap.appendChild(inp); wrap.appendChild(inc);
    const step = parseFloat(inp.step) || 1;
    const bump = d => {
      const mn = inp.min !== '' ? parseFloat(inp.min) : -Infinity;
      const mx = inp.max !== '' ? parseFloat(inp.max) : Infinity;
      const cur = parseFloat(inp.value) || 0;
      const v = Math.min(mx, Math.max(mn, +(cur + d * step).toFixed(4)));
      inp.value = v;
      inp.dispatchEvent(new Event('input', { bubbles: true }));
    };
    dec.onclick = () => bump(-1);
    inc.onclick = () => bump(1);
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
    if (!list.length) { root.innerHTML = `<div class="layer-empty">${HG_T('acc.empty')}</div>`; return; }
    root.innerHTML = '';
    list.forEach(name => {
      const el = document.createElement('div');
      el.className = 'layer-item';
      el.innerHTML = `<span class="li-name">${ICON.pin} ${name}</span><button class="li-btn" title="${HG_T('acc.del')}" aria-label="${HG_T('layer.del')}">${ICON.trash}</button>`;
      el.querySelector('.li-btn').onclick = () => removeAccessory(name);
      root.appendChild(el);
    });
  } catch (e) { root.innerHTML = `<div class="layer-empty">${HG_T('common.errorFmt', { msg: e.message })}</div>`; }
}
async function removeAccessory(name) {
  spin(true); setStatus('status.removing3d');
  try {
    const j = await (await fetch(`/api/accessory/remove?pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}`, { method: 'POST' })).json();
    if (!j.ok) throw new Error(j.error);
    log(HG_T('log.accRemoved', { name, files: j.patched.join(', ') }), 'ok');
    await loadGun(currentPack, currentGun); setMode('attach');
  } catch (e) { setStatus('status.error'); log(HG_T('log.accRemoveFail', { msg: e.message }), 'err'); } finally { spin(false); }
}
function measureTextLayer(l) {
  const c = makeCanvas(8, 8).getContext('2d');
  c.font = `${l.bold ? '900' : '400'} ${l.size}px ${l.font}`;
  l.w = Math.max(8, c.measureText(l.text).width + l.outlineW * 2);
  l.h = l.size * 1.3 + l.outlineW * 2;
}
function applyTextToSelected() {
  const st = activeSt(), l = selLayer(); if (!st || l?.type !== 'text') { log(HG_T('log.pickTextLayer'), 'err'); return; }
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
function placeStickerAt(st, x, y) {
  const s = stamps[activeStamp];
  const w = st.comp.width * 0.15, iw = s.img.width, ih = s.img.height;
  const l = addLayer(st, { type: 'stamp', name: s.name, img: s.img, x, y, w, h: w * ih / iw, baseScale: w, baseH: w * ih / iw, rot: 0, opacity: brush.alpha, blend: 'source-over' });
  log(HG_T('log.stickerPlaced', { name: s.name }), 'ok');
  return l;
}
function placeTextAt(st, x, y) {
  const cfg = textCfgFromOpts();
  const l = addLayer(st, { type: 'text', ...cfg, baseSize: cfg.size, x, y, rot: 0, opacity: brush.alpha, blend: 'source-over' });
  measureTextLayer(l); afterEdit(st); updateHud();
  log(HG_T('log.textPlaced', { text: cfg.text }), 'ok');
  return l;
}
async function placeSticker() {
  const at = await placePoint();
  if (!at) { log(HG_T('log.needGunTex'), 'err'); return; }
  placeStickerAt(at.st, at.x, at.y);
}

let stickerProject = true;
let bakeSess = null;

function bakeAxes(sess) {
  const rot = sess.rotDeg * Math.PI / 180, cos = Math.cos(rot), sin = Math.sin(rot);
  const right = sess.rightBase.clone().multiplyScalar(cos).addScaledVector(sess.upBase, sin);
  const up = sess.upBase.clone().multiplyScalar(cos).addScaledVector(sess.rightBase, -sin);
  return { right, up };
}

async function runBake() {
  const sess = bakeSess; if (!sess || !model) return;
  const s = stamps[sess.imgIdx]; if (!s) return;
  const iw = s.img.width || s.img.naturalWidth, ih = s.img.height || s.img.naturalHeight;
  const srcC = makeCanvas(iw, ih); srcC.getContext('2d').drawImage(s.img, 0, 0, iw, ih);
  const src = srcC.getContext('2d').getImageData(0, 0, iw, ih).data;

  const diag = new THREE.Box3().setFromObject(model).getSize(new THREE.Vector3()).length();
  const dw = diag * sess.sizeFrac, dh = dw * ih / iw, depth = Math.max(dw, dh);
  const { right, up } = bakeAxes(sess);
  const fwd = sess.fwd, origin = sess.origin;
  const FADE = 0.04;

  const touched = new Map();
  const vA = new THREE.Vector3(), vB = new THREE.Vector3(), vC = new THREE.Vector3(), e1 = new THREE.Vector3(), e2 = new THREE.Vector3(), nrm = new THREE.Vector3();

  for (const mesh of gunMeshes) {
    const texName = meshTexName.get(mesh.uuid); if (!texName) continue;
    const geo = mesh.geometry, pos = geo?.attributes?.position, uv = geo?.attributes?.uv;
    if (!pos || !uv) continue;
    const st = await ensureTexState(texName);
    let dst = touched.get(texName);
    const tw = st.comp.width, th = st.comp.height;
    const idx = geo.index ? geo.index.array : null;
    const triCount = (idx ? idx.length : pos.count) / 3;
    mesh.updateWorldMatrix(true, false);
    const m = mesh.matrixWorld;

    for (let t = 0; t < triCount; t++) {
      const i0 = idx ? idx[t * 3] : t * 3, i1 = idx ? idx[t * 3 + 1] : t * 3 + 1, i2 = idx ? idx[t * 3 + 2] : t * 3 + 2;
      vA.fromBufferAttribute(pos, i0).applyMatrix4(m);
      vB.fromBufferAttribute(pos, i1).applyMatrix4(m);
      vC.fromBufferAttribute(pos, i2).applyMatrix4(m);
      const ax = vA.dot(right) - origin.dot(right), ay = vA.dot(up) - origin.dot(up), az = vA.dot(fwd) - origin.dot(fwd);
      const bx = vB.dot(right) - origin.dot(right), by = vB.dot(up) - origin.dot(up), bz = vB.dot(fwd) - origin.dot(fwd);
      const cx = vC.dot(right) - origin.dot(right), cy = vC.dot(up) - origin.dot(up), cz = vC.dot(fwd) - origin.dot(fwd);
      const dxA = ax / dw + 0.5, dyA = 0.5 - ay / dh, dxB = bx / dw + 0.5, dyB = 0.5 - by / dh, dxC = cx / dw + 0.5, dyC = 0.5 - cy / dh;
      if ((dxA < 0 && dxB < 0 && dxC < 0) || (dxA > 1 && dxB > 1 && dxC > 1)) continue;
      if ((dyA < 0 && dyB < 0 && dyC < 0) || (dyA > 1 && dyB > 1 && dyC > 1)) continue;
      const dLim = depth / 2;
      if ((az < -dLim && bz < -dLim && cz < -dLim) || (az > dLim && bz > dLim && cz > dLim)) continue;
      e1.subVectors(vB, vA); e2.subVectors(vC, vA); nrm.crossVectors(e1, e2);
      if (nrm.dot(fwd) >= 0) continue;

      if (!dst) { dst = { data: new ImageData(tw, th), tw, th }; touched.set(texName, dst); }
      const u0 = uv.getX(i0), v0 = uv.getY(i0), u1 = uv.getX(i1), v1 = uv.getY(i1), u2 = uv.getX(i2), v2 = uv.getY(i2);
      const ofU = Math.floor(u0), ofV = Math.floor(v0);
      const x0 = (u0 - ofU) * tw, y0 = (v0 - ofV) * th, x1 = (u1 - ofU) * tw, y1 = (v1 - ofV) * th, x2 = (u2 - ofU) * tw, y2 = (v2 - ofV) * th;
      const area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
      if (Math.abs(area) < 1e-6) continue;
      const minX = Math.max(0, Math.floor(Math.min(x0, x1, x2))), maxX = Math.min(tw - 1, Math.ceil(Math.max(x0, x1, x2)));
      const minY = Math.max(0, Math.floor(Math.min(y0, y1, y2))), maxY = Math.min(th - 1, Math.ceil(Math.max(y0, y1, y2)));
      for (let py = minY; py <= maxY; py++) {
        for (let px = minX; px <= maxX; px++) {
          const qx = px + 0.5, qy = py + 0.5;
          let w0 = ((x1 - qx) * (y2 - qy) - (x2 - qx) * (y1 - qy)) / area;
          let w1 = ((x2 - qx) * (y0 - qy) - (x0 - qx) * (y2 - qy)) / area;
          const w2 = 1 - w0 - w1;
          if (w0 < -1e-4 || w1 < -1e-4 || w2 < -1e-4) continue;
          const dx = dxA * w0 + dxB * w1 + dxC * w2;
          const dy = dyA * w0 + dyB * w1 + dyC * w2;
          const dz = az * w0 + bz * w1 + cz * w2;
          if (dx < 0 || dx > 1 || dy < 0 || dy > 1 || dz < -dLim || dz > dLim) continue;
          const fade = Math.min(1, dx / FADE, (1 - dx) / FADE, dy / FADE, (1 - dy) / FADE);
          const fx = dx * (iw - 1), fy = dy * (ih - 1);
          const x0i = fx | 0, y0i = fy | 0, x1i = Math.min(iw - 1, x0i + 1), y1i = Math.min(ih - 1, y0i + 1);
          const tx = fx - x0i, ty = fy - y0i;
          const o00 = (y0i * iw + x0i) * 4, o10 = (y0i * iw + x1i) * 4, o01 = (y1i * iw + x0i) * 4, o11 = (y1i * iw + x1i) * 4;
          const R = (src[o00] * (1 - tx) + src[o10] * tx) * (1 - ty) + (src[o01] * (1 - tx) + src[o11] * tx) * ty;
          const G = (src[o00 + 1] * (1 - tx) + src[o10 + 1] * tx) * (1 - ty) + (src[o01 + 1] * (1 - tx) + src[o11 + 1] * tx) * ty;
          const Bc = (src[o00 + 2] * (1 - tx) + src[o10 + 2] * tx) * (1 - ty) + (src[o01 + 2] * (1 - tx) + src[o11 + 2] * tx) * ty;
          const A = ((src[o00 + 3] * (1 - tx) + src[o10 + 3] * tx) * (1 - ty) + (src[o01 + 3] * (1 - tx) + src[o11 + 3] * tx) * ty) * fade;
          if (A < 2) continue;
          const off = (py * dst.tw + px) * 4;
          if (A > dst.data.data[off + 3]) {
            dst.data.data[off] = R; dst.data.data[off + 1] = G; dst.data.data[off + 2] = Bc; dst.data.data[off + 3] = A;
          }
        }
      }
    }
  }

  const newLayers = new Map();
  for (const [texName, dst] of touched) {
    const st = texState.get(texName);
    const c = makeCanvas(dst.tw, dst.th);
    c.getContext('2d').putImageData(dst.data, 0, 0);
    let l = sess.layers?.get(texName) ? st.layers.find(x => x.id === sess.layers.get(texName)) : null;
    if (l) { pushUndo(st, false); l.img = c; }
    else {
      pushUndo(st, false);
      l = addLayer(st, { type: 'stamp', name: HG_T('layer.photo3d'), img: c, x: dst.tw / 2, y: dst.th / 2, w: dst.tw, h: dst.th, baseScale: dst.tw, baseH: dst.th, rot: 0, opacity: brush.alpha, blend: 'source-over' });
    }
    newLayers.set(texName, l.id);
    afterEdit(st);
  }
  if (sess.layers) for (const [texName, layerId] of sess.layers) {
    if (newLayers.has(texName)) continue;
    const st = texState.get(texName); if (!st) continue;
    const i = st.layers.findIndex(x => x.id === layerId);
    if (i >= 0) { pushUndo(st, false); st.layers.splice(i, 1); afterEdit(st); }
  }
  sess.layers = newLayers;
  rebuildLayerList();
  log(HG_T('log.projected', { count: touched.size }), 'ok');
}

let _bakeBusy = false, _bakeAgain = false;
function queueBake() {
  if (_bakeBusy) { _bakeAgain = true; return; }
  _bakeBusy = true;
  void runBake().finally(() => {
    _bakeBusy = false;
    if (_bakeAgain) { _bakeAgain = false; queueBake(); }
  });
}

async function bakeAtEvent(ev) {
  const h = rawHit(ev); if (!h) { log(HG_T('log.clickGun'), 'err'); return; }
  const s = stamps[activeStamp]; if (!s) { log(HG_T('log.pickImage'), 'err'); return; }
  const rightBase = new THREE.Vector3().setFromMatrixColumn(camera.matrixWorld, 0).normalize();
  const upBase = new THREE.Vector3().setFromMatrixColumn(camera.matrixWorld, 1).normalize();
  const fwd = new THREE.Vector3(); camera.getWorldDirection(fwd);
  bakeSess = {
    origin: h.point.clone(), rightBase, upBase, fwd,
    sizeFrac: bakeSess?.sizeFrac ?? 0.18, rotDeg: bakeSess?.rotDeg ?? 0,
    imgIdx: activeStamp, layers: bakeSess?.layers ?? new Map(),
  };
  await runBake();
}
async function placeText() {
  const at = await placePoint();
  if (!at) { log(HG_T('log.needGunTex'), 'err'); return; }
  placeTextAt(at.st, at.x, at.y);
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
  return { uv: hit.uv, texName: meshTexName.get(hit.object.uuid), bucket: meshBucket.get(hit.object.uuid) ?? 0, objId: hit.object.uuid, raw: hit };
}
function placeHit(ev) {
  const r = canvas3d.getBoundingClientRect();
  _ndcV.set(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
  raycaster.setFromCamera(_ndcV, camera);
  for (const h of raycaster.intersectObjects(gunMeshes, false)) if (h.face) {
    h.worldN = h.face.normal.clone().transformDirection(h.object.matrixWorld).normalize();
    if (h.worldN.dot(raycaster.ray.direction) > 0) h.worldN.negate();
    return h;
  }
  return null;
}
function uvToPx(st, uv) {
  const fx = ((uv.x % 1) + 1) % 1, fy = ((uv.y % 1) + 1) % 1;
  return { x: fx * st.comp.width, y: fy * st.comp.height };
}
const _ndcV = new THREE.Vector2();
const STROKE_EXACT_MESH_CAP = 64;
function strokeRayHit(rect, cx, cy) {
  if (!strokeMesh) return null;
  _ndcV.set(((cx - rect.left) / rect.width) * 2 - 1, -((cy - rect.top) / rect.height) * 2 + 1);
  raycaster.setFromCamera(_ndcV, camera);
  const tex = meshTexName.get(strokeMesh.uuid);
  if (gunMeshes.length <= STROKE_EXACT_MESH_CAP) {
    const hits = raycaster.intersectObjects(gunMeshes, false);
    for (const h of hits) {
      if (!h.uv) continue;
      return meshTexName.get(h.object.uuid) === tex ? h : null;
    }
    return null;
  }
  for (const h of raycaster.intersectObject(strokeMesh, false)) if (h.uv) return h;
  const others = gunMeshes.filter(m => m !== strokeMesh && meshTexName.get(m.uuid) === tex);
  for (const h of raycaster.intersectObjects(others, false)) if (h.uv) return h;
  return null;
}

function buildUvMask(mesh, st) {
  const geo = mesh?.geometry, uv = geo?.attributes?.uv;
  if (!uv || !st) return null;
  const idx = geo.index, W = st.base.width, H = st.base.height;
  const mw = Math.min(2048, W), mh = Math.max(1, Math.round(mw * H / W)), k = mw / W;
  const c = makeCanvas(mw, mh), ctx = c.getContext('2d');
  ctx.fillStyle = '#fff';
  const n = idx ? idx.count : uv.count;
  ctx.beginPath();
  for (let i = 0; i + 2 < n; i += 3) {
    const ia = idx ? idx.getX(i) : i, ib = idx ? idx.getX(i + 1) : i + 1, ic = idx ? idx.getX(i + 2) : i + 2;
    let ax = uv.getX(ia), ay = uv.getY(ia), bx = uv.getX(ib), by = uv.getY(ib), cx = uv.getX(ic), cy = uv.getY(ic);
    const ox = Math.floor(Math.min(ax, bx, cx)), oy = Math.floor(Math.min(ay, by, cy));
    ax -= ox; bx -= ox; cx -= ox; ay -= oy; by -= oy; cy -= oy;
    if ((bx - ax) * (cy - ay) - (cx - ax) * (by - ay) < 0) { const tx = bx, ty = by; bx = cx; by = cy; cx = tx; cy = ty; }
    const tu = Math.min(4, Math.max(1, Math.ceil(Math.max(ax, bx, cx)))),
          tv = Math.min(4, Math.max(1, Math.ceil(Math.max(ay, by, cy))));
    for (let du = 0; du < tu; du++) for (let dv = 0; dv < tv; dv++) {
      const x0 = (ax - du) * mw, y0 = (ay - dv) * mh;
      ctx.moveTo(x0, y0);
      ctx.lineTo((bx - du) * mw, (by - dv) * mh);
      ctx.lineTo((cx - du) * mw, (cy - dv) * mh);
      ctx.lineTo(x0, y0);
    }
  }
  ctx.fill('nonzero');
  const pad = 2, snap = makeCanvas(mw, mh);
  snap.getContext('2d').drawImage(c, 0, 0);
  const labels = new Int16Array(mw * mh);
  {
    const a = snap.getContext('2d').getImageData(0, 0, mw, mh).data;
    let next = 0;
    const stack = new Int32Array(mw * mh);
    for (let i = 0; i < mw * mh; i++) {
      if (labels[i] || a[i * 4 + 3] <= 8) continue;
      next++;
      let top = 0; stack[top++] = i; labels[i] = next;
      while (top) {
        const q = stack[--top], x = q % mw, y = (q / mw) | 0;
        if (x > 0      && !labels[q - 1]  && a[(q - 1)  * 4 + 3] > 8) { labels[q - 1]  = next; stack[top++] = q - 1; }
        if (x < mw - 1 && !labels[q + 1]  && a[(q + 1)  * 4 + 3] > 8) { labels[q + 1]  = next; stack[top++] = q + 1; }
        if (y > 0      && !labels[q - mw] && a[(q - mw) * 4 + 3] > 8) { labels[q - mw] = next; stack[top++] = q - mw; }
        if (y < mh - 1 && !labels[q + mw] && a[(q + mw) * 4 + 3] > 8) { labels[q + mw] = next; stack[top++] = q + mw; }
      }
    }
  }
  for (const [dx, dy] of [[-pad, 0], [pad, 0], [0, -pad], [0, pad], [-pad, -pad], [pad, -pad], [-pad, pad], [pad, pad]])
    ctx.drawImage(snap, dx, dy);
  return { canvas: c, k, labels, mw, mh, islands: new Map() };
}

function islandLabelAt(m, px, py) {
  if (!m || !m.labels) return 0;
  const { labels, mw, mh, k } = m;
  const mx = Math.min(mw - 1, Math.max(0, (px * k) | 0));
  const my = Math.min(mh - 1, Math.max(0, (py * k) | 0));
  let L = labels[my * mw + mx];
  if (!L) {
    for (let r = 1; r <= 4 && !L; r++)
      for (let dy = -r; dy <= r && !L; dy++)
        for (let dx = -r; dx <= r && !L; dx++) {
          const x = mx + dx, y = my + dy;
          if (x >= 0 && x < mw && y >= 0 && y < mh) L = labels[y * mw + x];
        }
  }
  return L;
}

function islandCanvas(m, L) {
  let isl = m.islands.get(L);
  if (isl) return isl;
  const { labels, mw, mh, k } = m;
  const c = makeCanvas(mw, mh), ctx = c.getContext('2d');
  const img = ctx.createImageData(mw, mh), d = img.data;
  for (let i = 0; i < mw * mh; i++)
    if (labels[i] === L) { const o = i * 4; d[o] = d[o + 1] = d[o + 2] = d[o + 3] = 255; }
  ctx.putImageData(img, 0, 0);
  const pad = 2, snap = makeCanvas(mw, mh);
  snap.getContext('2d').drawImage(c, 0, 0);
  for (const [dx, dy] of [[-pad, 0], [pad, 0], [0, -pad], [0, pad], [-pad, -pad], [pad, -pad], [-pad, pad], [pad, pad]])
    ctx.drawImage(snap, dx, dy);
  isl = { canvas: c, k };
  m.islands.set(L, isl);
  if (m.islands.size > 8) m.islands.delete(m.islands.keys().next().value);
  return isl;
}

function islandMaskFor(mesh, st, px, py) {
  const m = uvMaskFor(mesh, st);
  if (!m || !m.labels) return m;
  const L = islandLabelAt(m, px, py);
  return L ? islandCanvas(m, L) : m;
}
const UV_MASK_KEEP = 4;
function uvMaskFor(mesh, st) {
  if (!mesh || !st) return null;
  const key = `${mesh.uuid}|${st.name}|${st.base.width}x${st.base.height}`;
  if (uvMaskCache.has(key)) {
    const hit = uvMaskCache.get(key);
    uvMaskCache.delete(key); uvMaskCache.set(key, hit);
    return hit;
  }
  let m = null;
  try { m = buildUvMask(mesh, st); } catch (e) { m = null; }
  uvMaskCache.set(key, m);
  while (uvMaskCache.size > UV_MASK_KEEP) uvMaskCache.delete(uvMaskCache.keys().next().value);
  return m;
}
let _mbuf = null, _mctx = null;
function paintBuf(w, h) {
  if (!_mbuf) { _mbuf = makeCanvas(w, h); _mctx = _mbuf.getContext('2d'); }
  else if (_mbuf.width < w || _mbuf.height < h) { _mbuf.width = Math.max(w, _mbuf.width); _mbuf.height = Math.max(h, _mbuf.height); }
  return _mctx;
}
function dropPaintBuf() { _mbuf = null; _mctx = null; }
const PAINT_BUF_KEEP = 1024;
function trimPaintBuf() { if (_mbuf && (_mbuf.width > PAINT_BUF_KEEP || _mbuf.height > PAINT_BUF_KEEP)) dropPaintBuf(); }

let painting = false, lastPx = null, lastScreen = null, strokeMesh = null;
let strokeMaskEntry = null;
let strokeLabel = 0;
let strokeReady = true;
let strokeFinishQueued = false;
let strokeTexSize = null;
let strokeStepTexAllow = 0;

const _v3a = new THREE.Vector3(), _v3b = new THREE.Vector3(), _v3r = new THREE.Vector3(),
      _u2a = new THREE.Vector2(), _u2b = new THREE.Vector2();
function texelSizeForHit(hit, st) {
  const geo = hit?.object?.geometry, f = hit?.face;
  const pos = geo?.attributes?.position, uv = geo?.attributes?.uv;
  if (!f || !pos || !uv || !st) return Math.max(1, brush.size);
  _v3a.fromBufferAttribute(pos, f.a).applyMatrix4(hit.object.matrixWorld);
  _v3b.fromBufferAttribute(pos, f.b).applyMatrix4(hit.object.matrixWorld);
  _u2a.fromBufferAttribute(uv, f.a); _u2b.fromBufferAttribute(uv, f.b);
  const dUV = _u2a.distanceTo(_u2b), dW = _v3a.distanceTo(_v3b);
  if (dUV < 1e-6 || dW < 1e-9) return Math.max(1, brush.size);
  const worldPerUV = dW / dUV;
  _v3r.setFromMatrixColumn(camera.matrixWorld, 0);
  const s0 = hit.point.clone().project(camera);
  const s1 = hit.point.clone().add(_v3r).project(camera);
  const rect = canvas3d.getBoundingClientRect();
  const pxPerWorld = Math.hypot((s1.x - s0.x) * rect.width / 2, (s1.y - s0.y) * rect.height / 2);
  if (!isFinite(pxPerWorld) || pxPerWorld < 1e-6) return Math.max(1, brush.size);
  const worldDia = brush.size / pxPerWorld;
  const texDia = (worldDia / worldPerUV) * st.comp.width;
  return Math.min(st.comp.width * 2, Math.max(1, texDia));
}
function updateBrushRing(ev) {
  const ring = $('brushRing');
  if (!PAINT_MODES.has(mode) || mode === 'picker' || mode === 'fill' || !model) { ring.hidden = true; return; }
  const rect = canvas3d.getBoundingClientRect();
  ring.style.left = (ev.clientX - rect.left) + 'px';
  ring.style.top = (ev.clientY - rect.top) + 'px';
  ring.style.width = ring.style.height = Math.max(4, brush.size) + 'px';
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
  const size = strokeTexSize || brush.size;
  let mask = null;
  if (strokeMaskEntry) {
    const L = strokeMaskEntry.labels ? islandLabelAt(strokeMaskEntry, p.x, p.y) : 0;
    if (L !== strokeLabel) { lastPx = null; strokeLabel = L; }
    mask = L ? islandCanvas(strokeMaskEntry, L) : strokeMaskEntry;
  }
  const allow = strokeStepTexAllow || size * 4;
  if (lastPx && Math.hypot(p.x - lastPx.x, p.y - lastPx.y) > allow) lastPx = null;
  const a = lastPx || null;
  let ctx = st.ctxB, ox = 0, oy = 0, bw = 0, bh = 0;
  if (mask) {
    const pad = size / 2 + 6;
    const x0 = Math.max(0, Math.floor(Math.min(a ? a.x : p.x, p.x) - pad));
    const y0 = Math.max(0, Math.floor(Math.min(a ? a.y : p.y, p.y) - pad));
    const x1 = Math.min(st.base.width, Math.ceil(Math.max(a ? a.x : p.x, p.x) + pad));
    const y1 = Math.min(st.base.height, Math.ceil(Math.max(a ? a.y : p.y, p.y) + pad));
    bw = x1 - x0; bh = y1 - y0;
    if (bw <= 0 || bh <= 0) { lastPx = p; return; }
    ox = x0; oy = y0;
    ctx = paintBuf(bw, bh);
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, bw, bh);
    ctx.setTransform(1, 0, 0, 1, -ox, -oy);
  }
  ctx.save();
  if (mode === 'eraser') { st.origPattern ||= st.ctxB.createPattern(st.orig, 'no-repeat'); ctx.globalAlpha = brush.alpha; ctx.strokeStyle = ctx.fillStyle = st.origPattern; strokeOrDot(ctx, a, p, size); }
  else if (mode === 'alpha') { if (!mask) ctx.globalCompositeOperation = 'destination-out'; ctx.globalAlpha = brush.alpha; ctx.strokeStyle = ctx.fillStyle = '#000'; strokeOrDot(ctx, a, p, size); }
  else if (brush.style === 'soft') dabsAlong(a, p, size / 5, (x, y) => softDab(ctx, x, y, size, brush.color, brush.alpha * 0.35));
  else if (brush.style === 'spray') dabsAlong(a, p, size / 3, (x, y) => sprayDab(ctx, x, y, size, brush.color, brush.alpha));
  else { if (brush.style === 'marker') { if (!mask) ctx.globalCompositeOperation = 'multiply'; ctx.globalAlpha = brush.alpha * 0.6; } else ctx.globalAlpha = brush.alpha; ctx.strokeStyle = ctx.fillStyle = brush.color; strokeOrDot(ctx, a, p, size); }
  ctx.restore();
  if (mask) {
    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalCompositeOperation = 'destination-in';
    ctx.drawImage(mask.canvas, ox * mask.k, oy * mask.k, bw * mask.k, bh * mask.k, 0, 0, bw, bh);
    ctx.restore();
    const d = st.ctxB;
    d.save();
    if (mode === 'alpha') d.globalCompositeOperation = 'destination-out';
    else if (mode !== 'eraser' && brush.style === 'marker') d.globalCompositeOperation = 'multiply';
    d.drawImage(_mbuf, 0, 0, bw, bh, ox, oy, bw, bh);
    d.restore();
  }
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
function floodFill(st, px, py, mask) {
  const w = st.base.width, h = st.base.height;
  if (w * h > 4096 * 4096) { log(HG_T('log.fillTooBig'), 'err'); return; }
  const img = st.ctxB.getImageData(0, 0, w, h), buf = new Uint32Array(img.data.buffer);
  const x0 = px | 0, y0 = py | 0, target = buf[y0 * w + x0], [r, g, b] = hexToRgb(brush.color);
  const fill = (255 << 24) | (b << 16) | (g << 8) | r; if (target === fill) return;
  const tr = target & 255, tg = target >> 8 & 255, tb = target >> 16 & 255, ta = target >>> 24, tol = fillTolerance;
  const match = v => Math.abs((v & 255) - tr) + Math.abs((v >> 8 & 255) - tg) + Math.abs((v >> 16 & 255) - tb) + Math.abs((v >>> 24) - ta) <= tol * 3;
  let inMask = null;
  if (mask) {
    const mw = mask.canvas.width, mh = mask.canvas.height, k = mask.k;
    const md = mask.canvas.getContext('2d').getImageData(0, 0, mw, mh).data;
    inMask = (x, y) => md[((Math.min(mh - 1, (y * k) | 0)) * mw + Math.min(mw - 1, (x * k) | 0)) * 4 + 3] > 8;
  }
  const ok = (i) => !inMask || inMask(i % w, (i / w) | 0);
  const stack = [y0 * w + x0], seen = new Uint8Array(w * h); seen[y0 * w + x0] = 1;
  while (stack.length) { const i = stack.pop(); buf[i] = fill; const x = i % w, y = (i / w) | 0;
    if (x > 0 && !seen[i - 1] && match(buf[i - 1]) && ok(i - 1)) { seen[i - 1] = 1; stack.push(i - 1); }
    if (x < w - 1 && !seen[i + 1] && match(buf[i + 1]) && ok(i + 1)) { seen[i + 1] = 1; stack.push(i + 1); }
    if (y > 0 && !seen[i - w] && match(buf[i - w]) && ok(i - w)) { seen[i - w] = 1; stack.push(i - w); }
    if (y < h - 1 && !seen[i + w] && match(buf[i + w]) && ok(i + w)) { seen[i + w] = 1; stack.push(i + w); } }
  st.ctxB.putImageData(img, 0, 0);
}
function pickColor(st, p) {
  flushComposite();
  const d = st.ctxC.getImageData(p.x | 0, p.y | 0, 1, 1).data;
  brush.color = '#' + [d[0], d[1], d[2]].map(v => v.toString(16).padStart(2, '0')).join('');
  log(HG_T('log.picked', { color: brush.color }), 'ok'); setMode(prevPaintMode);
}
function enableAlpha(st, bucket) {
  if (!st.alphaOn) {
    st.alphaOn = true;
    for (const m of (matsByTex.get(st.name) || [])) { m.transparent = true; m.side = THREE.DoubleSide; m.depthWrite = false; m.needsUpdate = true; }
  }
  if (bucket != null && !st.alphaWarned) {
    st.alphaWarned = true;
    log(bucket === 0 ? HG_T('log.alphaOpaque') : HG_T('log.alphaOk', { bucket }), bucket === 0 ? 'warn' : 'ok');
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
function applyGlassAllOn(color, opacity) {
  const act = activeSt();
  if (act) { if (color != null) act.glass.color = color; if (opacity != null) act.glass.opacity = opacity; }
  for (const s of texState.values()) {
    if (!s.glass?.on) continue;
    if (color != null) s.glass.color = color;
    if (opacity != null) s.glass.opacity = opacity;
    applyGlass(s);
  }
}
async function glassAllTextures(on, color, opacity) {
  for (const name of matsByTex.keys()) {
    const s = await ensureTexState(name);
    s.glass.on = on;
    if (color != null) s.glass.color = color;
    if (opacity != null) s.glass.opacity = opacity;
    applyGlass(s);
  }
}
async function postGlass(texNames, on, opacity, color) {
  if (!currentPack || !currentGun || !texNames || !texNames.length) return;
  try {
    await fetch(`/api/glass?pack=${enc(currentPack)}&gun=${enc(currentGun)}`,
      { method: 'POST', body: JSON.stringify({ textures: texNames, on, opacity, color }) });
  } catch (e) { log(HG_T('log.glassSaveFail', { msg: e.message }), 'err'); }
}
function persistGlassOn() {
  for (const [name, s] of texState) if (s.glass?.on) postGlass([name], true, s.glass.opacity, s.glass.color);
}

function glassColorNow(texName) {
  return activeSt()?.glass?.color || texState.get(texName)?.glass?.color || '#7fdfff';
}
function glassOpacityNow(texName) {
  const n = $('oGlassOpN');
  const v = n ? +n.value : Math.round((texState.get(texName)?.glass?.opacity ?? 0.4) * 100);
  return Math.min(92, Math.max(10, v || 40)) / 100;
}

async function glassSetOne(texName, on) {
  if (!texName) return;
  const c = glassColorNow(texName), o = glassOpacityNow(texName);
  if (texName !== activeTex) await selectTexture(texName);
  const s = await ensureTexState(texName);
  pushGlassUndo([texName]);
  s.glass.on = on;
  if (on) { s.glass.color = c; s.glass.opacity = o; }
  applyGlass(s);
  postGlass([texName], s.glass.on, s.glass.opacity, s.glass.color);
  log(on ? HG_T('log.glassOn', { name: texName }) : HG_T('log.glassOff', { name: texName }), 'ok');
  if (mode === 'glass') renderOpts();
}
async function glassSetAll(on, hintTex) {
  const c = glassColorNow(hintTex ?? activeTex), o = glassOpacityNow(hintTex ?? activeTex);
  spin(true);
  try {
    const names = [...matsByTex.keys()];
    for (const n of names) await ensureTexState(n);
    pushGlassUndo(names);
    await glassAllTextures(on, on ? c : null, on ? o : null);
    postGlass(names, on, o, c);
  }
  finally { spin(false); }
  log(on ? HG_T('log.glassAllOn') : HG_T('log.glassAllOff'), 'ok');
  if (mode === 'glass') renderOpts();
}

function openGlassDialog(texName) {
  const esc = s => String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
  const st = texState.get(texName);
  const isOn = !!st?.glass?.on;
  const anyGlass = [...texState.values()].some(s => s.glass?.on);

  document.getElementById('glassOverlay')?.remove();
  const ov = document.createElement('div'); ov.id = 'glassOverlay';
  ov.innerHTML = `
    <div class="pub-card">
      <div class="pub-h">${ICON.glass} ${HG_T('glass.dlg.title')}</div>
      <div class="pub-sub">${isOn ? HG_T('glass.dlg.subOn', { tex: esc(texName) }) : HG_T('glass.dlg.subOff', { tex: esc(texName) })}</div>
      <div class="glass-choice">
        <button class="btn" id="gOne">${ICON.pin}<span>${isOn ? HG_T('glass.dlg.oneOff') : HG_T('glass.dlg.oneOn')}
          <small>${isOn ? HG_T('glass.dlg.oneOffSmall') : HG_T('glass.dlg.oneOnSmall', { tex: esc(texName) })}</small></span></button>
        <button class="btn primary" id="gAll">${ICON.glass}<span>${HG_T('glass.dlg.all')}
          <small>${HG_T('glass.dlg.allSmall')}</small></span></button>
        ${anyGlass ? `<button class="btn" id="gNone">${ICON.trash}<span>${HG_T('glass.dlg.none')}</span></button>` : ''}
      </div>
      <div class="pub-btns"><button class="btn" id="gCancel">${HG_T('btn.cancel')}</button></div>
    </div>`;
  document.body.appendChild(ov);

  const close = () => { ov.remove(); removeEventListener('keydown', onKey, true); };
  const onKey = (e) => { if (e.code === 'Escape') { e.stopPropagation(); close(); } };
  addEventListener('keydown', onKey, true);
  ov.addEventListener('click', e => { if (e.target === ov) close(); });
  $('gCancel').onclick = close;

  $('gOne').onclick = () => { close(); void glassSetOne(texName, !isOn); };
  $('gAll').onclick = () => { close(); void glassSetAll(true, texName); };
  const gn = $('gNone');
  if (gn) gn.onclick = () => { close(); void glassSetAll(false, texName); };
}

let dragLayer = null, dragMeshes = [];

const CLICK_MODES = new Set(['orbit', 'glass']);
let camClick = null;
canvas3d.addEventListener('pointerup', (ev) => {
  try { if (canvas3d.hasPointerCapture(ev.pointerId)) canvas3d.releasePointerCapture(ev.pointerId); } catch {}
  const d0 = camClick; camClick = null;
  if (!d0 || ev.button !== 0 || !CLICK_MODES.has(mode)) return;
  if (Math.hypot(ev.clientX - d0.x, ev.clientY - d0.y) > 6) return;
  if (document.getElementById('glassOverlay')) return;
  const hit = raycastHit(ev);
  if (!hit) return;
  if (mode === 'glass') { openGlassDialog(hit.texName); return; }
  if (hit.texName === activeTex) { pulseTexParts(activeTex); return; }
  void selectTexture(hit.texName).then(() => log(HG_T('log.texSelected', { name: hit.texName }), 'ok'));
});

canvas3d.addEventListener('pointerdown', async (ev) => {
  if (CLICK_MODES.has(mode) && ev.button === 0) camClick = { x: ev.clientX, y: ev.clientY };
  if (mode === 'orbit' || mode === 'glass' || mode === 'anim' || ev.button !== 0) return;
  if (mode === 'attach') {
    ev.preventDefault();
    if (pendingObj || pendingAttach) { try { canvas3d.setPointerCapture(ev.pointerId); } catch {} }
    if (pendingObj) { draggingPending = true; movePendingTo(ev); }
    else if (pendingAttach) { draggingAttach = true; moveAttachPreviewTo(ev); }
    else createAttachPreview(ev);
    return;
  }

  if (mode === 'sticker' && stickerProject) {
    ev.preventDefault();
    await bakeAtEvent(ev);
    return;
  }

  if (mode === 'shape' && shapeProject) {
    ev.preventDefault();
    await bakeShapeAtEvent(ev);
    return;
  }

  if (mode === 'sticker' || mode === 'text' || mode === 'shape') {
    const hit = raycastHit(ev, overlayOnTop);
    if (!hit) return;
    ev.preventDefault();
    if (hit.texName !== activeTex) await selectTexture(hit.texName);
    const st = activeSt(); if (!st) return;
    const p = uvToPx(st, hit.uv);
    let l = selLayer();
    if (l) {
      pushUndo(st, false); l.x = p.x; l.y = p.y; afterEdit(st);
    } else {
      l = mode === 'sticker' ? placeStickerAt(st, p.x, p.y)
        : mode === 'shape'   ? placeShapeAt(st, p.x, p.y)
        :                      placeTextAt(st, p.x, p.y);
      if (!l) return;
    }
    dragLayer = l;
    dragMeshes = gunMeshes.filter(m => meshTexName.get(m.uuid) === activeTex);
    return;
  }

  const hit = raycastHit(ev); if (!hit) return;
  ev.preventDefault();
  if (mode === 'picker') { const stP = await ensureTexState(hit.texName); pickColor(stP, uvToPx(stP, hit.uv)); return; }
  painting = true; strokeReady = false; strokeFinishQueued = false;
  lastPx = null; lastScreen = { x: ev.clientX, y: ev.clientY };
  if (hit.texName !== activeTex) await selectTexture(hit.texName);
  const st = activeSt(), p = uvToPx(st, hit.uv);
  if (mode === 'fill') {
    painting = false; strokeReady = true;
    pushUndo(st, true); floodFill(st, p.x, p.y, islandMaskFor(hit.raw.object, st, p.x, p.y)); afterEdit(st); return;
  }
  if (mode === 'alpha') enableAlpha(st, hit.bucket);
  strokeMesh = hit.raw.object;
  strokeMaskEntry = uvMaskFor(strokeMesh, st);
  strokeLabel = islandLabelAt(strokeMaskEntry, p.x, p.y);
  strokeTexSize = texelSizeForHit(hit.raw, st);
  strokeTexQuality(st, true);
  pushUndo(st, true);
  paintStep(st, p);
  strokeReady = true;
  if (_paintQ.length && !_paintRaf) _paintRaf = requestAnimationFrame(processPaintQ);
  if (strokeFinishQueued) { strokeFinishQueued = false; endStroke3d(); }
});
let _ringEv = null, _ringRaf = 0;
function queueBrushRing(ev) {
  _ringEv = { clientX: ev.clientX, clientY: ev.clientY };
  if (_ringRaf) return;
  _ringRaf = requestAnimationFrame(() => { _ringRaf = 0; if (_ringEv) updateBrushRing(_ringEv); });
}
let _paintQ = [], _paintRaf = 0;
function processPaintQ() {
  _paintRaf = 0;
  if (!strokeReady) {
    if (painting && _paintQ.length) _paintRaf = requestAnimationFrame(processPaintQ);
    return;
  }
  const pts = _paintQ; _paintQ = [];
  if (!painting || !pts.length) return;
  const st = activeSt(); if (!st) return;
  const rect = canvas3d.getBoundingClientRect();
  let budget = 12;
  for (const cur of pts) {
    const from = lastScreen ?? cur;
    const dScreen = Math.hypot(cur.x - from.x, cur.y - from.y);
    const steps = Math.min(budget, Math.max(1, Math.ceil(dScreen / 6)));
    const texPerPx = (strokeTexSize || brush.size) / Math.max(1, brush.size);
    strokeStepTexAllow = Math.max((strokeTexSize || brush.size) * 1.5, (dScreen / steps) * texPerPx * 4);
    for (let i = 1; i <= steps; i++) {
      const hit = strokeRayHit(rect, from.x + (cur.x - from.x) * i / steps, from.y + (cur.y - from.y) * i / steps);
      if (!hit) { lastPx = null; continue; }
      if (hit.object !== strokeMesh) {
        strokeMesh = hit.object;
        strokeMaskEntry = uvMaskFor(strokeMesh, st);
        lastPx = null;
      }
      paintStep(st, uvToPx(st, hit.uv));
    }
    lastScreen = cur;
    budget -= steps;
    if (budget <= 0) { lastScreen = pts[pts.length - 1]; break; }
  }
}
let _dragEv = null, _dragRaf = 0;
function queueLayerDrag(ev) {
  _dragEv = { clientX: ev.clientX, clientY: ev.clientY };
  if (_dragRaf) return;
  _dragRaf = requestAnimationFrame(() => { _dragRaf = 0; if (dragLayer && _dragEv) doLayerDrag(_dragEv); });
}
function doLayerDrag(ev) {
  const st = activeSt(); if (!st || !dragLayer) return;
  const r = canvas3d.getBoundingClientRect();
  _ndcV.set(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
  raycaster.setFromCamera(_ndcV, camera);
  const hits = raycaster.intersectObjects(dragMeshes.length ? dragMeshes : gunMeshes, false).filter(h => h.uv);
  if (!hits.length) return;
  let hit = hits[0];
  if (overlayOnTop) {
    const near = hits.filter(h => h.distance - hits[0].distance < 0.02);
    const ov = near.find(h => (meshBucket.get(h.object.uuid) ?? 0) > 0);
    if (ov) hit = ov;
  }
  const p = uvToPx(st, hit.uv);
  dragLayer.x = p.x; dragLayer.y = p.y; afterEdit(st);
}
canvas3d.addEventListener('pointermove', (ev) => {
  queueBrushRing(ev);
  if (draggingAttach) { moveAttachPreviewTo(ev); return; }
  if (draggingPending) { movePendingTo(ev); return; }
  if (dragLayer) { queueLayerDrag(ev); return; }
  if (!painting) return;
  _paintQ.push({ x: ev.clientX, y: ev.clientY });
  if (!_paintRaf) _paintRaf = requestAnimationFrame(processPaintQ);
});
canvas3d.addEventListener('pointerleave', () => { $('brushRing').hidden = true; });
function endStroke3d() {
  if (painting && !strokeReady) { strokeFinishQueued = true; return; }
  if (_paintRaf) { cancelAnimationFrame(_paintRaf); _paintRaf = 0; processPaintQ(); }
  _paintQ = []; _dragEv = null;
  if (painting || dragLayer) { strokeTexQuality(activeSt(), false); flushComposite(); }
  painting = false; lastPx = null; lastScreen = null; dragLayer = null; dragMeshes = []; gizmoDrag = null; strokeMesh = null; strokeMaskEntry = null; strokeLabel = 0; strokeTexSize = null; strokeStepTexAllow = 0; draggingPending = false; draggingAttach = false;
  trimPaintBuf();
}
addEventListener('pointerup', endStroke3d);
addEventListener('pointercancel', endStroke3d);

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
  if (mode === 'sticker' || mode === 'text' || mode === 'shape') {
    let l = hitLayerAt(st, p) || selLayer();
    if (!l && mode === 'shape' && !shapeProject) l = placeShapeAt(st, p.x, p.y);
    if (l) { selLayerId = l.id; pushUndo(st, false); gizmoDrag = { l, sx: p.x - l.x, sy: p.y - l.y }; rebuildLayerList(); updateHud(); draw2D(); }
    return;
  }
  if (mode === 'fill') { pushUndo(st, true); floodFill(st, p.x, p.y, null); afterEdit(st); return; }
  if (mode === 'picker') { pickColor(st, p); return; }
  if (mode === 'alpha') enableAlpha(st, 1);
  const rct = tex2d.getBoundingClientRect();
  strokeMaskEntry = null; strokeLabel = 0; strokeStepTexAllow = 0;
  strokeTexSize = Math.max(1, brush.size * (st.comp.width / (rct.width || st.comp.width)));
  strokeTexQuality(st, true);
  pushUndo(st, true); painting2d = true; lastPx = null; paintStep(st, p);
});
tex2d.addEventListener('pointermove', (ev) => {
  const st = activeSt(); if (!st) return;
  if (gizmoDrag) { const p = evTexPt(ev, st); gizmoDrag.l.x = p.x - gizmoDrag.sx; gizmoDrag.l.y = p.y - gizmoDrag.sy; afterEdit(st); return; }
  if (painting2d) paintStep(st, evTexPt(ev, st));
});
function endStroke2d() {
  if (!painting2d) return;
  painting2d = false; lastPx = null; strokeTexSize = null;
  strokeTexQuality(activeSt(), false); flushComposite(); trimPaintBuf();
}
addEventListener('pointerup', endStroke2d);
addEventListener('pointercancel', endStroke2d);

let pendingAttach = null, draggingAttach = false;

function createAttachPreview(ev) {
  const h = placeHit(ev); if (!h) return;
  const s = stamps[activeStamp];
  const iw = s.img.width || s.img.naturalWidth, ih = s.img.height || s.img.naturalHeight;
  const c = makeCanvas(iw, ih); c.getContext('2d').drawImage(s.img, 0, 0);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = maxAniso;
  const w = attachSizeCm / 100, hgt = w * ih / iw;
  const geo = new THREE.PlaneGeometry(w, hgt);
  geo.translate(0, -hgt / 2, 0);
  const mat = new THREE.MeshStandardMaterial({ map: tex, transparent: true, alphaTest: 0.05, side: THREE.DoubleSide, roughness: .55, metalness: .05 });
  const mesh = new THREE.Mesh(geo, mat);
  pendingAttach = { mesh, point: h.point.clone(), normal: h.worldN.clone(), baseW: w, srcCanvas: c };
  positionAttachMesh(); scene.add(mesh);
  log(HG_T('log.pendantPreview'), 'ok');
  renderOpts();
}
function positionAttachMesh() {
  const { mesh, point, normal } = pendingAttach;
  const f = new THREE.Vector3(normal.x, 0, normal.z);
  if (f.lengthSq() < 1e-6) f.set(0, 0, 1);
  f.normalize();
  mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), f);
  mesh.position.copy(point).addScaledVector(normal, 0.004);
}
function moveAttachPreviewTo(ev) {
  if (!pendingAttach) return;
  const h = placeHit(ev); if (!h) return;
  pendingAttach.point.copy(h.point);
  pendingAttach.normal.copy(h.worldN);
  positionAttachMesh();
}
function updateAttachPreviewScale() {
  if (!pendingAttach) return;
  pendingAttach.mesh.scale.setScalar((attachSizeCm / 100) / pendingAttach.baseW);
}
function cancelAttachPreview() {
  if (!pendingAttach) return;
  scene.remove(pendingAttach.mesh);
  pendingAttach.mesh.geometry.dispose(); pendingAttach.mesh.material.map?.dispose(); pendingAttach.mesh.material.dispose();
  pendingAttach = null; draggingAttach = false;
  updateHud3d();
  if (mode === 'attach') renderOpts();
}
async function bakeAttachPreview() {
  if (!pendingAttach) return;
  const p = pendingAttach.point, n = pendingAttach.normal;
  const sizeM = attachSizeCm / 100;
  const blob = await new Promise(res => pendingAttach.srcCanvas.toBlob(res, 'image/png'));
  const name = `brelok_${Date.now() % 100000}`;
  const hadDirty = [...texState.values()].some(st => st.dirty);
  if (hadDirty && !(await saveDirty())) { log(HG_T('log.bakeAbort'), 'err'); return; }
  spin(true); setStatus('status.bakingPendant');
  try {
    const q = `pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}&px=${p.x.toFixed(5)}&py=${p.y.toFixed(5)}&pz=${p.z.toFixed(5)}&nx=${n.x.toFixed(5)}&ny=${n.y.toFixed(5)}&nz=${n.z.toFixed(5)}&size=${sizeM}&kind=${attachKind}&depth=${attachDepth}`;
    const j = await (await fetch(`/api/attach?${q}`, { method: 'POST', body: blob })).json();
    if (!j.ok) throw new Error(j.error);
    log(HG_T('log.pendantBaked', { files: j.patched.join(', ') }), 'ok');
    cancelAttachPreview();
    await loadGun(currentPack, currentGun); setMode('attach');
  } catch (e) { setStatus('status.error'); log(HG_T('log.attachFail', { msg: e.message }), 'err'); } finally { spin(false); }
}

let pendingObj = null, pendingMerged = null, pendingTexCanvas = null;
let pendingScale = 1, pendingBaseScale = 1, draggingPending = false;
let pendingSize = null;

function buildImportMesh(root) {
  root.updateMatrixWorld(true);
  const meshes = [];
  root.traverse(o => { if (o.isMesh && o.geometry?.attributes?.position) meshes.push(o); });
  if (!meshes.length) throw new Error(HG_T('err.noMeshes'));

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
  if (!merged) throw new Error(HG_T('err.mergeFail'));

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
  if (!model) { log(HG_T('log.needGun'), 'err'); return; }
  spin(true); setStatus('status.importing');
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
    pendingSize = size.clone();
    const box = new THREE.Box3().setFromObject(model), gc = box.getCenter(new THREE.Vector3());
    pendingObj.position.copy(gc); applyPendingTransform();
    scene.add(pendingObj);
    setStatus('status.imported');
    log(HG_T('log.imported', { name: file.name, verts: built.geometry.attributes.position.count }), 'ok');
    renderOpts();
  } catch (e) { setStatus('status.error'); log(HG_T('log.importFail', { msg: e.message }), 'err'); } finally { spin(false); }
}
function applyPendingTransform() { if (pendingObj) pendingObj.scale.setScalar(pendingBaseScale * pendingScale); }
function movePendingTo(ev) {
  const h = placeHit(ev); if (!h || !pendingObj) return;
  let off = 0.01;
  if (pendingSize) {
    const d = _v3r.copy(h.worldN).applyQuaternion(pendingObj.getWorldQuaternion(_q4a).invert());
    off = 0.5 * (Math.abs(pendingSize.x * d.x) + Math.abs(pendingSize.y * d.y) + Math.abs(pendingSize.z * d.z))
        * pendingObj.scale.x + 0.004;
  }
  pendingObj.position.copy(h.point).addScaledVector(h.worldN, off);
}
const _q4a = new THREE.Quaternion();
function cancelImported() {
  if (!pendingObj) return;
  scene.remove(pendingObj);
  pendingObj.geometry.dispose(); pendingObj.material.map?.dispose(); pendingObj.material.dispose();
  pendingObj = null; pendingMerged = null; pendingTexCanvas = null; pendingSize = null; draggingPending = false;
  updateHud3d();
  renderOpts();
}
const _abToB64 = (ab) => { const b = new Uint8Array(ab); let s = ''; const c = 0x8000; for (let i = 0; i < b.length; i += c) s += String.fromCharCode.apply(null, b.subarray(i, i + c)); return btoa(s); };
const _taB64 = (ta) => _abToB64(ta.buffer.slice(ta.byteOffset, ta.byteOffset + ta.byteLength));
async function bakeImported() {
  if (!pendingObj) return;
  spin(true); setStatus('status.baking');
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
    if (vc > 65000) throw new Error(HG_T('err.tooDetailed', { verts: vc }));
    const pngBlob = await new Promise(r => pendingTexCanvas.toBlob(r, 'image/png'));
    const png = await new Promise(res => { const fr = new FileReader(); fr.onload = () => res(fr.result.split(',')[1]); fr.readAsDataURL(pngBlob); });
    const body = JSON.stringify({ pos: _taB64(P), nrm: _taB64(N), uv: _taB64(U), idx: _taB64(idx), png });
    const name = `brelok_${Date.now() % 100000}`;
    if ([...texState.values()].some(s => s.dirty)) await saveDirty();
    const j = await (await fetch(`/api/attach-mesh?pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}`, { method: 'POST', body })).json();
    if (!j.ok) throw new Error(j.error);
    log(HG_T('log.modelBaked', { files: j.patched.join(', ') }), 'ok');
    cancelImported();
    await loadGun(currentPack, currentGun); setMode('attach');
  } catch (e) { setStatus('status.error'); log(HG_T('log.bakeFail', { msg: e.message }), 'err'); } finally { spin(false); }
}

function animBody() {
  const s = stamps[activeStamp];
  if (!s) return null;
  const src = s.img.toDataURL ? s.img : (() => { const c = makeCanvas(s.img.naturalWidth, s.img.naturalHeight); c.getContext('2d').drawImage(s.img, 0, 0); return c; })();
  const png = src.toDataURL('image/png').split(',')[1];
  return JSON.stringify({
    sourceKind: 'pendant', png,
    size: anim.sizeCm / 100, depthFrac: anim.depth,
    animMode: 'uv', scrollU: anim.scrollU, scrollV: 0, periodSec: anim.period,
    attachBone: anim.bone,
    rotX: anim.rotX, rotY: anim.rotY, rotZ: anim.rotZ,
    offX: anim.offX, offY: anim.offY, offZ: anim.offZ,
    mirror: anim.mirror,
    name: (s.name || 'anim'),
  });
}
const animSupportWarned = new Set();
async function warnAnimSupport() {
  const key = `${currentPack}/${currentGun}`;
  if (animSupportWarned.has(key)) return;
  animSupportWarned.add(key);
  try {
    const j = await (await fetch(`/api/anim-support?gun=${enc(currentGun)}`)).json();
    if (j.ok && !j.supported)
      log(HG_T('log.animUnsupported', { list: j.supportedList }), 'err');
  } catch {}
}

async function generateAnim() {
  if (!currentGun) { log(HG_T('log.needGun'), 'err'); return; }
  const body = animBody();
  if (!body) { log(HG_T('log.pickImage2'), 'err'); return; }
  await warnAnimSupport();
  spin(true); setStatus('status.buildingPreview');
  try {
    const j = await (await fetch(`/api/anim?pack=${enc(currentPack)}&gun=${enc(currentGun)}`, { method: 'POST', body })).json();
    if (!j.ok) throw new Error(j.error || 'fail');
    await showAnimPreview(j);
    setStatus('status.previewReady');
  } catch (e) { setStatus('status.error'); log(HG_T('log.previewFail', { msg: e.message }), 'err'); }
  finally { spin(false); }
}

async function showAnimPreview(res) {
  clearAnimPreview();
  if (!res.previewGlb) { log(HG_T('log.noPreviewGlb'), 'warn'); return; }
  const url = `/api/anim-file?pack=${enc(currentPack)}&gun=${enc(currentGun)}&file=${enc(res.previewGlb)}&v=${Date.now()}`;
  const glb = await new Promise((resolve, rej) => new GLTFLoader().load(url, resolve, undefined, rej));
  const obj = glb.scene;

  const materials = [];
  obj.traverse(o => {
    if (!o.isMesh) return;
    for (const m of (Array.isArray(o.material) ? o.material : [o.material])) if (m) materials.push(m);
  });
  if (res.anim.mode === 'uv')
    for (const mat of materials)
      for (const map of [mat.map, mat.emissiveMap])
        if (map) { map.wrapS = map.wrapT = THREE.RepeatWrapping; map.needsUpdate = true; }

  let parent = model ? model.getObjectByName('Gun_Main_Bone') : null;
  if (parent) { parent.add(obj); obj.position.set(0, 0, 0); }
  else if (model) {
    const box = new THREE.Box3().setFromObject(model), c = box.getCenter(new THREE.Vector3());
    obj.position.copy(c); scene.add(obj);
  } else scene.add(obj);
  obj.quaternion.identity();

  animClock.start();
  animPreview = {
    obj, materials,
    axisGlb: new THREE.Vector3(0, 0, 1),
    meta: { mode: res.anim.mode, periodSec: res.anim.periodSec, scrollU: res.anim.scrollU, scrollV: res.anim.scrollV, amplitudeDeg: res.anim.amplitudeDeg },
  };
}

const hist = { undo: [], redo: [] };
const HIST_BYTES = 96 * 1024 * 1024;

function entryBytes(e) { return e.kind === 'tex' && e.base ? e.base.width * e.base.height * 4 : 0; }
function trimHist(stack) {
  let total = stack.reduce((s, e) => s + entryBytes(e), 0);
  while (stack.length > 1 && (total > HIST_BYTES || stack.length > 60)) {
    total -= entryBytes(stack[0]); stack.shift();
  }
}
function refreshHistBtns() {
  const noUndo = !hist.undo.length, noRedo = !hist.redo.length;
  const bp = $('btnUndo'); if (bp) bp.disabled = noUndo;
  const bu = $('btnUndoBig'), br = $('btnRedoBig');
  if (bu) bu.disabled = noUndo;
  if (br) br.disabled = noRedo;
}
function pushHist(entry) {
  hist.undo.push(entry); trimHist(hist.undo);
  hist.redo.length = 0;
  refreshHistBtns();
}
function resetHist() { hist.undo.length = 0; hist.redo.length = 0; refreshHistBtns(); }

function pushUndo(st, withBase) {
  pushHist({
    kind: 'tex', tex: st.name,
    base: withBase ? copyCanvas(st.base) : null,
    layers: st.layers.map(l => ({ ...l })),
  });
}
const GLASS_DEFAULT = { on: false, color: '#7fdfff', opacity: 0.4 };
function glassEntry(names) {
  return { kind: 'glass', items: names.map(n => ({ tex: n, g: { ...(texState.get(n)?.glass || GLASS_DEFAULT) } })) };
}
function pushGlassUndo(names) { pushHist(glassEntry(names)); }
function persistGlassFor(names) {
  const groups = new Map();
  for (const n of names) {
    const s = texState.get(n); if (!s) continue;
    const k = `${s.glass.on}|${s.glass.opacity}|${s.glass.color}`;
    if (!groups.has(k)) groups.set(k, { on: s.glass.on, opacity: s.glass.opacity, color: s.glass.color, names: [] });
    groups.get(k).names.push(n);
  }
  for (const g of groups.values()) postGlass(g.names, g.on, g.opacity, g.color);
}

async function applyEntry(e) {
  if (e.kind === 'tex') {
    const st = texState.get(e.tex); if (!st) return null;
    const inv = {
      kind: 'tex', tex: e.tex,
      base: e.base ? copyCanvas(st.base) : null,
      layers: st.layers.map(l => ({ ...l })),
    };
    if (e.base) { st.ctxB.clearRect(0, 0, st.base.width, st.base.height); st.ctxB.drawImage(e.base, 0, 0); }
    st.layers = e.layers;
    if (!st.layers.some(l => l.id === selLayerId)) selLayerId = null;
    composite(st);
    if (e.tex !== activeTex) await selectTexture(e.tex);
    draw2D(); rebuildLayerList(); updateHud();
    return inv;
  }
  if (e.kind === 'glass') {
    const names = e.items.map(it => it.tex);
    const inv = glassEntry(names);
    for (const it of e.items) {
      const s = await ensureTexState(it.tex);
      s.glass = { ...it.g };
      applyGlass(s);
    }
    persistGlassFor(names);
    if (mode === 'glass') renderOpts();
    return inv;
  }
  return null;
}
let histBusy = false;
async function applyHistStep(from, to) {
  if (histBusy) return;
  const e = hist[from].pop(); if (!e) return;
  histBusy = true;
  try {
    const inv = await applyEntry(e);
    if (inv) { hist[to].push(inv); trimHist(hist[to]); }
  } finally { histBusy = false; refreshHistBtns(); }
}
const doUndo = () => { void applyHistStep('undo', 'redo'); };
const doRedo = () => { void applyHistStep('redo', 'undo'); };
{ const bp = $('btnUndo'); if (bp) bp.onclick = doUndo; }
{ const bu = $('btnUndoBig'), br = $('btnRedoBig'); if (bu) bu.onclick = doUndo; if (br) br.onclick = doRedo; }

async function saveDirty() {
  const dirty = [...texState.entries()].filter(([, st]) => st.dirty);
  if (!dirty.length) return true;
  spin(true); setStatus('status.saving');
  try {
    for (const [name, st] of dirty) {
      composite(st);
      const blob = await new Promise(res => st.comp.toBlob(res, 'image/png'));
      const j = await (await fetch(`/api/texture?pack=${enc(currentPack)}&gun=${enc(currentGun)}&name=${enc(name)}`, { method: 'POST', body: blob })).json();
      if (!j.ok) throw new Error(j.error || 'fail');
      st.dirty = false; log(`${name} → ${j.patched.join(', ')}`, 'ok');
    }
    $('btnReset').disabled = false;
    document.querySelectorAll('.tex-item .dirty').forEach(el => el.hidden = true);
    setStatus('status.savedYdr');
    return true;
  } catch (e) { setStatus('status.error'); log(HG_T('log.saveFail', { msg: e.message }), 'err'); return false; }
  finally { spin(false); }
}
$('btnSave').onclick = () => openSaveDialog();
$('btnResetTex').onclick = () => {
  const st = activeSt(); if (!st) return;
  pushUndo(st, true);
  st.ctxB.clearRect(0, 0, st.base.width, st.base.height); st.ctxB.drawImage(st.orig, 0, 0);
  st.layers = []; selLayerId = null;
  if (st.chrome) { st.chrome.ctx.fillStyle = '#666'; st.chrome.ctx.fillRect(0, 0, st.chrome.canvas.width, st.chrome.canvas.height); st.chrome.tex.needsUpdate = true; }
  afterEdit(st); rebuildLayerList(); updateHud();
  log(HG_T('log.texReset', { name: st.name }), 'ok');
};
$('btnReset').onclick = async () => {
  if (!currentPack) return; spin(true);
  try { await fetch(`/api/reset?pack=${enc(currentPack)}&gun=${enc(currentGun)}`, { method: 'POST' }); log(HG_T('log.gunReset', { name: currentGunName }), 'ok'); await loadCatalog(); await loadGun(currentPack, currentGun, currentPackName, currentGunName); }
  finally { spin(false); }
};

$('btnApply').onclick = async () => {
  if (!currentPack) { log(HG_T('log.needGun'), 'err'); return; }
  if (!(await saveDirty())) return;
  spin(true); setStatus('status.applying');
  try {
    const j = await (await fetch(`/api/apply?pack=${enc(currentPack)}&gun=${enc(currentGun)}`, { method: 'POST' })).json();
    if (!j.ok) throw new Error(j.error || HG_T('err.applyFail'));
    if (j.animError) log(HG_T('log.animNotInstalled', { msg: j.animError }), 'err');
    log(j.anim ? HG_T('log.appliedAnim') : HG_T('log.applied'), 'ok');
    setStatus('status.applied');
    postToHost('applied', { gun: currentGunName });
  } catch (e) { setStatus('status.error'); log(HG_T('log.applyFail', { msg: e.message }), 'err'); }
  finally { spin(false); }
};

async function openSaveDialog() {
  if (!currentPack) { log(HG_T('log.needGun'), 'err'); return; }

  let used = null, max = 5;
  try {
    const d = await (await fetch(`/api/saves?owner=${enc(OWNER.id || '')}`)).json();
    max = d.max || 5; used = d.used ?? (d.slots || []).length;
  } catch {}

  const editing = currentPack === '_custom';
  const atLimit = used !== null && used >= max && !editing;

  document.getElementById('pubOverlay')?.remove();
  const ov = document.createElement('div'); ov.id = 'pubOverlay';
  ov.innerHTML = `
    <div class="pub-card">
      <div class="pub-h">${ICON.save} ${HG_T('save.dlg.title')}</div>
      <div class="pub-sub">${editing ? HG_T('save.dlg.subEdit') : HG_T('save.dlg.subNew')}</div>
      <label class="pub-l">${HG_T('save.dlg.nameLabel')}</label>
      <input id="pubName" class="pub-in" maxlength="48" placeholder="${HG_T('save.dlg.namePh')}" value="${(currentGunName || '').replace(/"/g, '&quot;')}">
      <label class="pub-chk"><input type="checkbox" id="pubShare"> ${HG_T('save.dlg.share')}</label>
      <div id="pubDescWrap" style="display:none">
        <label class="pub-l">${HG_T('save.dlg.descLabel')} <span class="pub-opt">${HG_T('save.dlg.descOpt')}</span></label>
        <textarea id="pubDesc" class="pub-in" rows="2" maxlength="240" placeholder="${HG_T('save.dlg.descPh')}"></textarea>
        <div class="pub-sub">${HG_T('save.dlg.descNote')}</div>
      </div>
      ${used !== null ? `<div class="pub-sub">${HG_T('save.dlg.slots', { used, max })}${atLimit ? HG_T('save.dlg.atLimit') : ''}</div>` : ''}
      <div class="pub-btns">
        <button id="pubCancel" class="btn">${HG_T('btn.cancel')}</button>
        <button id="pubGo" class="btn primary" ${atLimit ? 'disabled' : ''}>${HG_T('btn.save')}</button>
      </div>
    </div>`;
  document.body.appendChild(ov);
  const close = () => ov.remove();
  ov.addEventListener('click', e => { if (e.target === ov) close(); });
  $('pubCancel').onclick = close;

  const share = $('pubShare'), go = $('pubGo');
  share.onchange = () => {
    $('pubDescWrap').style.display = share.checked ? '' : 'none';
    go.textContent = share.checked ? HG_T('save.dlg.goShare') : HG_T('btn.save');
    if (share.checked && !OWNER.id) log(HG_T('log.needLoginShare'), 'err');
  };

  go.onclick = async () => {
    const name = ($('pubName').value || '').trim() || currentGunName || HG_T('save.dlg.defaultName');
    const wantShare = share.checked;
    if (wantShare && !OWNER.id) { log(HG_T('log.needLoginShare2'), 'err'); return; }
    go.disabled = true; go.textContent = wantShare ? HG_T('save.dlg.sending') : HG_T('save.dlg.saving');
    if (!(await saveDirty())) { go.disabled = false; go.textContent = wantShare ? HG_T('save.dlg.goShare') : HG_T('btn.save'); return; }

    if (!wantShare) {
      close();
      await slotSave(editing ? currentGun : '', name);
      return;
    }

    spin(true); setStatus('status.publishing');
    try {
      const meta = {
        displayName: name,
        description: ($('pubDesc')?.value || '').trim(),
        category: '',
      };
      const q = `pack=${enc(currentPack)}&gun=${enc(currentGun)}&owner=${enc(OWNER.id)}&ownerName=${enc(OWNER.name)}` + flowQS();
      const j = await (await fetch(`/api/publish?${q}`, { method: 'POST', body: JSON.stringify(meta) })).json();
      if (!j.ok) throw new Error(j.error || HG_T('err.publishFail'));
      currentGunName = name;
      log(HG_T('log.sentModeration', { name }), 'ok'); setStatus('status.moderation');
      close(); postToHost('published', { id: j.id, name });
    } catch (e) {
      setStatus('status.error'); log(HG_T('log.publishFail', { msg: e.message }), 'err');
      go.disabled = false; go.textContent = HG_T('save.dlg.goShare');
    } finally { spin(false); }
  };
}

$('btnSlots').onclick = () => openSlotsDialog();

async function openSlotsDialog() {
  if (!currentPack) { log(HG_T('log.needGun'), 'err'); return; }
  spin(true);
  let data;
  try { data = await (await fetch(`/api/saves?owner=${enc(OWNER.id || '')}`)).json(); }
  catch (e) { spin(false); log(HG_T('log.slotsFail', { msg: e.message }), 'err'); return; }
  spin(false);
  const slots = data.slots || [], max = data.max || 5, used = data.used ?? slots.length;
  document.getElementById('slotOverlay')?.remove();
  const ov = document.createElement('div'); ov.id = 'slotOverlay';
  const esc = s => (s || '').replace(/</g, '&lt;').replace(/"/g, '&quot;');
  const rows = slots.map(s => `
    <div class="slot-row">
      <div class="slot-info"><div class="slot-name">${esc(s.name || s.gun)}</div><div class="slot-meta">${esc(s.gun)} · ${esc(s.savedAt)}</div></div>
      <button class="btn slot-load" data-slot="${s.slotId}">${HG_T('btn.open')}</button>
      <button class="btn slot-del" data-slot="${s.slotId}" title="${HG_T('btn.delete')}">✕</button>
    </div>`).join('');
  ov.innerHTML = `
    <div class="pub-card">
      <div class="pub-h">${ICON.folder} ${HG_T('slots.title')}</div>
      <div class="pub-sub">${HG_T('slots.sub', { used, max })}</div>
      ${rows || `<div class="pub-sub">${HG_T('slots.empty')}</div>`}
      <div class="pub-btns"><button id="slotClose" class="btn">${HG_T('btn.close')}</button></div>
    </div>`;
  document.body.appendChild(ov);
  const close = () => ov.remove();
  ov.addEventListener('click', e => { if (e.target === ov) close(); });
  $('slotClose').onclick = close;
  ov.querySelectorAll('.slot-del').forEach(b => b.onclick = async () => {
    try { await fetch(`/api/save-delete?slot=${enc(b.dataset.slot)}`, { method: 'POST' }); } catch {}
    openSlotsDialog();
  });
  ov.querySelectorAll('.slot-load').forEach(b => b.onclick = async () => { close(); await slotLoad(b.dataset.slot); });
}

async function slotSave(slotId, name) {
  if (!(await saveDirty())) return;
  spin(true); setStatus('status.saving2');
  try {
    if (name) currentGunName = name;
    const q = `pack=${enc(currentPack)}&gun=${enc(currentGun)}&slot=${enc(slotId)}&name=${enc(name || currentGunName || currentGun || '')}&owner=${enc(OWNER.id || '')}` + flowQS();
    const j = await (await fetch(`/api/save?${q}`, { method: 'POST' })).json();
    if (!j.ok) throw new Error(j.error || HG_T('err.saveFail'));
    if (j.slotId) { currentPack = '_custom'; currentGun = j.slotId; }
    log(HG_T('log.savedToMyGuns'), 'ok'); setStatus('status.saved');
  } catch (e) { setStatus('status.error'); log(HG_T('log.saveFail', { msg: e.message }), 'err'); }
  finally { spin(false); }
}

async function slotLoad(slotId) {
  spin(true); setStatus('status.loadingSlot');
  try {
    const j = await (await fetch(`/api/save-load?slot=${enc(slotId)}`, { method: 'POST' })).json();
    if (!j.ok) throw new Error(j.error || HG_T('err.loadFail'));
    await loadCatalog();
    await loadGun(j.pack, j.gun, currentPackName, j.gunName || j.gun);
    log(HG_T('log.slotLoaded'), 'ok'); setStatus('status.loaded');
  } catch (e) { setStatus('status.error'); log(HG_T('log.slotFail', { msg: e.message }), 'err'); }
  finally { spin(false); }
}

function openOwnPackDialog() {
  if (!currentGun) { log(HG_T('log.needGunPaint'), 'err'); return; }
  if (!OWNER.id)   { log(HG_T('log.needLogin'), 'err'); return; }
  document.getElementById('ownOverlay')?.remove();
  const ov = document.createElement('div');
  ov.id = 'ownOverlay';
  ov.innerHTML = `
    <div class="pub-card">
      <div class="pub-h">${ICON.pack} ${HG_T('own.dlg.title')}</div>
      <div class="pub-sub">${HG_T('own.dlg.sub')}</div>
      <label class="pub-l">${HG_T('own.dlg.nameLabel')}</label>
      <input id="opGunName" class="pub-in" maxlength="40" value="${(currentGunName || '').replace(/"/g, '&quot;')}">
      ${FLOW.ownPackId ? `<div class="pub-sub" style="margin-top:10px">${HG_T('own.dlg.pack', { name: FLOW.ownPackName || '…' })}</div>` : ''}
      <div class="pub-btns">
        <button id="opCancel" class="btn">${HG_T('btn.cancelAlt')}</button>
        <button id="opGo" class="btn primary">${HG_T('btn.continue')}</button>
      </div>
    </div>`;
  document.body.appendChild(ov);
  ov.onclick = (e) => { if (e.target === ov) ov.remove(); };
  $('opCancel').onclick = () => ov.remove();
  $('opGo').onclick = () => {
    const gunName  = ($('opGunName')?.value || '').trim() || currentGunName || currentGun;
    const packName = FLOW.ownPackId ? '' : HG_T('own.defaultPackName', { owner: OWNER.name || HG_T('own.defaultOwner') });
    ov.remove();
    void saveToOwnPack(gunName, packName);
  };
}

function startOwnPackPoll(pack, gun) {
  if (loadPollTimer) clearInterval(loadPollTimer);
  loadPollTimer = setInterval(async () => {
    try {
      const p = await (await fetch(`/api/progress?pack=${enc(pack)}&gun=${enc(gun)}&v=${Date.now()}`)).json();
      if (p.phase === 'upload')       setLoad(HG_T('own.phase.upload'), p.pct || 20);
      else if (p.phase === 'render')  setLoad(HG_T('own.phase.render'), p.pct || 60);
      else if (p.phase === 'publish') setLoad(HG_T('own.phase.publish'), p.pct || 92);
      else if (p.phase === 'done')    setLoad(HG_T('own.phase.done'), 100);
    } catch {}
  }, 300);
}

async function saveToOwnPack(gunName, packName) {
  const pack = currentPack, gun = currentGun;
  showLoad(HG_T('own.load.title'), HG_T('own.load.sub'));
  startOwnPackPoll(pack, gun);
  setStatus('status.savingOwnPack');
  try {
    if (!(await saveDirty())) throw new Error(HG_T('err.commitFail'));
    const q = `pack=${enc(pack)}&gun=${enc(gun)}&packId=${enc(FLOW.ownPackId)}`
            + `&packName=${enc(packName || '')}&name=${enc(gunName)}&owner=${enc(OWNER.id)}`;
    const j = await (await fetch(`/api/ownpack/save?${q}`, { method: 'POST' })).json();
    if (!j.ok) throw new Error(j.error || HG_T('err.ownPackSaveFail'));
    FLOW.ownPackId = j.packId; FLOW.ownPackName = j.packName;
    hideLoad();
    log(HG_T('log.addedToOwnPack', { gun: gunName, pack: j.packName, count: j.gunCount, cap: j.gunCap }), 'ok');
    setStatus('status.inOwnPack');
    showOwnPackSuccess(j);
  } catch (e) {
    hideLoad(); setStatus('status.error'); log(HG_T('log.ownPackFail', { msg: e.message }), 'err');
  }
}

let _okOverlayTimer = 0;
function dismissOwnPackSuccess() {
  clearTimeout(_okOverlayTimer); _okOverlayTimer = 0;
  document.getElementById('okOverlay')?.remove();
}
function showOwnPackSuccess(j) {
  dismissOwnPackSuccess();
  const ov = document.createElement('div');
  ov.id = 'okOverlay';
  ov.innerHTML = `
    <div class="ok-card">
      <svg class="ok-mark" viewBox="0 0 72 72">
        <circle class="ok-circle" cx="36" cy="36" r="31"/>
        <path class="ok-check" d="M22 37.5 L32 47.5 L51 27"/>
      </svg>
      <div class="ok-title">${HG_T('own.ok.title')}</div>
      <div class="ok-sub">${HG_T('own.ok.sub', {
        pack: j.packName, count: j.gunCount, cap: j.gunCap,
        gunsWord: HG_T('own.ok.gunsWord', { count: j.gunCap }),
      })}</div>
      <div class="ok-load"><span class="ok-spin"></span>${HG_T('own.ok.loading')}</div>
    </div>`;
  document.body.appendChild(ov);
  postToHost('ownpack-saved', {
    packId: j.packId, packName: j.packName,
    gunCount: j.gunCount, gunCap: j.gunCap, gunId: j.gunId,
  });
  _okOverlayTimer = setTimeout(dismissOwnPackSuccess, 45000);
}

function setMode(m) {
  if (PAINT_MODES.has(mode) && mode !== 'picker') prevPaintMode = mode;
  mode = m;
  if (m === 'alpha') { const st = activeSt(); if (st) enableAlpha(st, null); }
  if (m !== 'attach' && typeof pendingAttach !== 'undefined' && pendingAttach) cancelAttachPreview();
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
  if (ev.ctrlKey && ev.code === 'KeyZ' && ev.shiftKey) { ev.preventDefault(); doRedo(); }
  else if (ev.ctrlKey && ev.code === 'KeyZ') { ev.preventDefault(); doUndo(); }
  else if (ev.ctrlKey && ev.code === 'KeyY') { ev.preventDefault(); doRedo(); }
  else if (ev.ctrlKey && ev.code === 'KeyS') { ev.preventDefault(); $('btnSave').click(); }
  else if (ev.code === 'Delete' && selLayerId) deleteSelLayer();
  else if (ev.code === 'KeyO') setMode('orbit');
  else if (ev.code === 'KeyB') setMode('brush');
  else if (ev.code === 'KeyE') setMode('eraser');
  else if (ev.code === 'KeyT') setMode('text');
});

function relabelUi() {
  try {
    renderOpts();
    rebuildLayerList();
    updateHud();
    updateHud3d();
    if (detail) {
      buildTexList(); renderGunLabel();
      document.querySelectorAll('.tex-item').forEach(el => {
        const s = texState.get(el.dataset.name);
        el.querySelector('.dirty').hidden = !(s && s.dirty);
        el.classList.toggle('active', el.dataset.name === activeTex);
      });
    }
    if ($('packs').querySelector('.pack-empty')) renderCatalog($('search').value || '');
    $('status').textContent = HG_T(statusKey, statusArgs);
  } catch (e) { console.warn('[i18n] relabel failed', e); }
}
HG_I18N.onChange(relabelUi);

setMode('orbit');
applyFlowChrome();
const btnFlowDone = $('btnFlowDone'), btnFlowCancel = $('btnFlowCancel');
if (btnFlowDone)   btnFlowDone.onclick   = openOwnPackDialog;
if (btnFlowCancel) btnFlowCancel.onclick = () => postToHost('flow-cancel');
loadCatalog().then(() => {
  if (INIT.pack && INIT.gun) loadGun(INIT.pack, INIT.gun, INIT.packName, INIT.gunName);
  else document.querySelector('.pack')?.classList.add('open');
}).catch(e => { log(HG_T('log.catalogError', { msg: e.message }), 'err'); setStatus('status.catalogError'); })
  .finally(() => {
    requestAnimationFrame(() => requestAnimationFrame(() => postToHost('ready')));
  });
