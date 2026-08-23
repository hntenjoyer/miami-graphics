import { useEffect, useMemo, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Trans, useTranslation } from 'react-i18next';
import { AlignCenterHorizontal, AlignCenterVertical, Check, Crosshair, Eraser, Image as ImageIcon, Loader2, Map as MapIcon, Move, Play, RotateCcw, Rows3, Save, Sliders, Square, X } from 'lucide-react';
import type { MinimapLayoutPreset, MinimapSave, MinimapTweaks } from '@/bridge/types';
import { bridge } from '@/bridge';
import { DEFAULT_MINIMAP_TWEAKS, STOCK_MINIMAP_TWEAKS, MINIMAP_ASPECT_RATIOS } from '@/store/customizeStore';

const STAGE_W = 191;
const STAGE_H = 136;
const MAP = { x: 6, y: 8, w: 179, h: 114 };
const BAR_W = 179, BAR_UNITS = 6;
const BAR_VERT_UNITS = 114;
const DIGIT_EM = 17, FONT_VIS = 1;
const GAME_FONT = "'MGFont2Cond', sans-serif";
const FONT_PREVIEW: Record<string, string> = {
  chalet:    "'MGFont2', sans-serif",
  fixednum:  "'MGFontNum', monospace",
  pricedown: "'MGFontCash', sans-serif",
  script:    "'MGFontScript', cursive",
  tag:       "'MGFontTag', sans-serif",
};
const NO_CYRILLIC = new Set(['fixednum', 'tag']);
const hasCyrillic = (s: string | null | undefined) =>
  !!s && /[Ѐ-ӿ]/.test(s);
const EMBEDDED_FONTS = new Set(['pricedown', 'script', 'tag']);
const fontStyle = (id: string | null): { fontFamily: string } => ({
  fontFamily: (id && id !== 'stock' ? FONT_PREVIEW[id] : null) ?? GAME_FONT,
});
const STOCK_CAP = 892;
const FONT_CAP: Record<string, number> = {
  chalet: 980, fixednum: 980, pricedown: 808, script: 780, tag: 892,
};
const digitScale = (id: string | null): number =>
  id && id !== 'stock' && FONT_CAP[id] ? STOCK_CAP / FONT_CAP[id] : 1;

const blCache = new Map<string, number>();
const BL_FALLBACK = 0.8;
function measureBaselineRatio(family: string): number {
  const hit = blCache.get(family);
  if (hit !== undefined) return hit;
  let b = BL_FALLBACK;
  try {
    const host = document.createElement('div');
    host.style.cssText = 'position:absolute;left:-9999px;top:0;visibility:hidden;pointer-events:none';
    const line = document.createElement('div');
    line.style.cssText = `font-family:${family};font-size:100px;line-height:1;white-space:nowrap`;
    const strut = document.createElement('span');
    strut.style.cssText = 'display:inline-block;width:0;height:0';
    line.append(strut, document.createTextNode('100'));
    host.append(line);
    document.body.append(host);
    const r = (strut.getBoundingClientRect().bottom - line.getBoundingClientRect().top) / 100;
    host.remove();
    if (r > 0.4 && r < 1.6) b = r;
  } catch {  }
  blCache.set(family, b);
  return b;
}
function useBaselineRatio(family: string): number {
  const [b, setB] = useState(() => measureBaselineRatio(family));
  useEffect(() => {
    let alive = true;
    setB(measureBaselineRatio(family));
    Promise.resolve(document.fonts?.load?.(`100px ${family}`, '100'))
      .catch(() => {})
      .then(() => {
        if (!alive) return;
        blCache.delete(family);
        setB(measureBaselineRatio(family));
      });
    return () => { alive = false; };
  }, [family]);
  return b;
}

const inkDropPx = (leadPx: number, fsPx: number, b: number): number =>
  leadPx + fsPx * (1 - b);

const round1 = (v: number) => Math.round(v * 10) / 10;
const r4 = (v: number) => Math.round(v * 10000) / 10000;

const MINIMAP_FONT_UI = false;
const MINIMAP_BLIPS_UI = false;
const Z = 3.4;
const px = (v: number) => v * Z;

const VAN = { posX: -0.0045, posY: 0.002, sizeX: 0.150, sizeY: 0.188888 };

const SAFE_STEP = 0.005;
const SAFE_FALLBACK = 0.05;
const SAFE_MAX_N = 10;
const SAFE_LS_KEY = 'mg.minimap.safe';
const readSavedSafe = (): number => {
  try {
    const v = parseFloat(window.localStorage.getItem(SAFE_LS_KEY) ?? '');
    return Number.isFinite(v) && v >= 0 && v <= SAFE_MAX_N * SAFE_STEP ? v : SAFE_FALLBACK;
  } catch { return SAFE_FALLBACK; }
};
const saveSafe = (v: number) => { try { window.localStorage.setItem(SAFE_LS_KEY, String(v)); } catch {  } };

const SCREENS: { label: string; ar: number }[] = [
  { label: '16:9',  ar: 16 / 9 },
  { label: '21:9',  ar: 3440 / 1440 },
  { label: '16:10', ar: 16 / 10 },
  { label: '4:3',   ar: 4 / 3 },
  { label: '5:4',   ar: 5 / 4 },
  { label: '32:9',  ar: 32 / 9 },
];

type Sel = 'digitHp' | 'digitAr' | 'bar' | 'popup' | 'popupAr' | 'text' | 'hit' | null;
type Tab = 'content' | 'position';

export type MinimapLayout = { ratio: string; posX: number; posY: number; transparent: boolean };

export function MinimapTweaksEditor({ open, initial, initialLayout, initialRings, busy, onClose, onApply }: {
  open: boolean;
  initial: MinimapTweaks | null;
  initialLayout: { ratio: string; posX: number | null; posY: number | null; transparent: boolean };
  initialRings: { on: boolean; external: boolean };
  busy: boolean;
  onClose: () => void;
  onApply: (t: MinimapTweaks, layout: MinimapLayout | null, rings: boolean | null) => void;
}) {
  const { t } = useTranslation();
  const [tw, setTw] = useState<MinimapTweaks>(initial ?? DEFAULT_MINIMAP_TWEAKS);

  const [mySave, setMySave] = useState<MinimapSave | null>(null);
  const [saveName, setSaveName] = useState('');
  const [saveNote, setSaveNote] = useState<string | null>(null);
  useEffect(() => {
    if (!open) return;
    let alive = true;
    bridge.minimapGetSave?.()
      .then(s => { if (alive) setMySave(s ?? null); })
      .catch(() => {  });
    return () => { alive = false; };
  }, [open]);
  const writeMySave = async (name: string) => {
    try {
      if (typeof bridge.minimapWriteSave !== 'function') { setSaveNote('Сохранение недоступно в этой версии'); return; }
      const saved = await bridge.minimapWriteSave(name || 'Моя миникарта', tw);
      if (saved) { setMySave(saved); setSaveNote('Сохранено'); }
      else setSaveNote('Не удалось сохранить: пустой ответ');
    } catch (e) {
      setSaveNote('Не удалось сохранить: ' + (e instanceof Error ? e.message : String(e)));
    }
  };
  const clearMySave = async () => {
    try { await bridge.minimapClearSave?.(); setMySave(null); setSaveName(''); setSaveNote(null); }
    catch (e) { setSaveNote('Не удалось удалить: ' + (e instanceof Error ? e.message : String(e))); }
  };
  const smallBtn =
    'px-2.5 h-8 rounded-lg border text-[11px] font-semibold transition-colors ' +
    'bg-white/[0.04] text-text-secondary border-white/[0.08] hover:text-text-primary hover:bg-white/[0.08]';
  const [ringsOn, setRingsOn] = useState(initialRings.on);
  const [tab, setTab] = useState<Tab>('content');
  const [sel, setSel] = useState<Sel>('digitHp');
  const [hitPreview, setHitPreview] = useState(false);

  const [ratio, setRatio] = useState<string>('16:9');
  const [pos, setPos] = useState<{ posX: number; posY: number }>({ posX: VAN.posX, posY: VAN.posY });
  const [screenPick, setScreenPick] = useState<string | null>(null);
  const [screenReal, setScreenReal] = useState<import('@/bridge/types').MinimapScreen | null>(null);
  const [transparent, setTransparent] = useState(false);
  const [posBg, setPosBg] = useState<string | null>(null);
  const [posBgDim, setPosBgDim] = useState<{ w: number; h: number } | null>(null);
  const [safe, setSafe] = useState(readSavedSafe);
  const [safeProfileN, setSafeProfileN] = useState<number | null>(null);
  const [safeRead, setSafeRead] = useState<'pending' | 'ok' | 'fail'>('pending');
  const [presets, setPresets] = useState<MinimapLayoutPreset[]>([]);
  const [presetsState, setPresetsState] = useState<'pending' | 'ok' | 'fail'>('pending');
  const posStageRef = useRef<HTMLDivElement>(null);
  const posDragRef = useRef<{ dx: number; dy: number } | null>(null);

  const [demo, setDemo] = useState<{ hp: number; ar: number } | null>(null);
  const [fx, setFx] = useState<{ id: number; kind: 'hp' | 'heal' | 'armor'; text: string } | null>(null);
  const [flashId, setFlashId] = useState(0);
  const timersRef = useRef<number[]>([]);
  const fxIdRef = useRef(0);

  const stopDemo = () => {
    timersRef.current.forEach(clearTimeout);
    timersRef.current = [];
    setDemo(null);
    setFx(null);
  };
  const playDemo = () => {
    if (demo) { stopDemo(); return; }
    setDemo({ hp: 100, ar: 100 });
    const at = (ms: number, f: () => void) => timersRef.current.push(window.setTimeout(f, ms));
    const pop = (kind: 'hp' | 'heal' | 'armor', text: string) => setFx({ id: ++fxIdRef.current, kind, text });
    at(500,  () => { setFlashId(i => i + 1); setDemo({ hp: 100, ar: 60 }); if (tw.armorPopup) pop('armor', '-40'); });
    at(1800, () => { setFlashId(i => i + 1); setDemo({ hp: 82, ar: 0 });
      if (tw.armorPopup) pop('armor', '-60'); else if (tw.damagePopup) pop('hp', '-18'); });
    at(3100, () => { setFlashId(i => i + 1); setDemo({ hp: 45, ar: 0 }); if (tw.damagePopup) pop('hp', '-37'); });
    at(4400, () => { setFlashId(i => i + 1); setDemo({ hp: 16, ar: 0 }); if (tw.damagePopup) pop('hp', '-29'); });
    at(5900, () => { setDemo({ hp: 100, ar: 0 }); if (tw.healPopup) pop('heal', '+84'); });
    at(7400, stopDemo);
  };
  useEffect(() => stopDemo, []);
  useEffect(() => { if (!open) stopDemo(); }, [open]);

  const hitPngUrl = usePngDataUrl(tw.hitPngPath);
  const arrowUrl = usePngDataUrl(tw.arrowPngPath);
  const gpsUrl = usePngDataUrl(tw.gpsPngPath);

  const pickPng = () => bridge.openFileDialog(t('minimap.editor.dlgImage', 'Картинка (PNG, GIF, JPG, WEBP, BMP)'),
    '*.png;*.gif;*.jpg;*.jpeg;*.webp;*.bmp');
  const isAnimatedPick = (p: string | null | undefined) =>
    !!p && /\.(gif|webp|apng)$/i.test(p);
  const pickHitPng = async () => {
    const p = await pickPng();
    if (p) set({ hitPngPath: p });
  };
  const pickArrowPng = async () => {
    const p = await pickPng();
    if (p) set({ arrowPngPath: p });
  };
  const pickGpsPng = async () => {
    const p = await pickPng();
    if (p) set({ gpsPngPath: p });
  };

  const [fontState, setFontState] = useState<import('@/bridge/types').MinimapFontState | null>(null);
  const [fontPick, setFontPick] = useState<string | null>(null);
  const [fontSlot, setFontSlot] = useState<'auto' | 'efigs' | 'russian'>('auto');
  const [fontBusy, setFontBusy] = useState(false);
  const [fontMsg, setFontMsg] = useState<string | null>(null);
  useEffect(() => {
    if (!open) return;
    let alive = true;
    bridge.minimapGetFontState?.().then(s => { if (alive) setFontState(s); }).catch(() => {});
    return () => { alive = false; };
  }, [open]);

  const [fontOpts, setFontOpts] = useState<import('@/bridge/types').MinimapFontOption[]>([]);
  useEffect(() => {
    if (!open) return;
    let alive = true;
    bridge.minimapGetFontOptions?.()
      .then(o => { if (alive && Array.isArray(o)) setFontOpts(o); })
      .catch(() => {});
    return () => { alive = false; };
  }, [open]);
  const pickFont = async () => {
    const p = await bridge.openFileDialog(t('minimap.editor.dlgFont', 'Шрифт Scaleform (font_lib_*.gfx)'), '*.gfx');
    if (p) { setFontPick(p); setFontMsg(null); }
  };
  const applyFont = async () => {
    if (!fontPick || fontBusy) return;
    setFontBusy(true); setFontMsg(null);
    try {
      const slot = fontSlot === 'efigs' ? 'font_lib_efigs_pc.gfx'
        : fontSlot === 'russian' ? 'font_lib_russian_pc.gfx' : null;
      const r = await bridge.minimapInstallFont(fontPick, slot);
      setFontMsg(r.success
        ? t('minimap.editor.fontInstalled', 'Шрифт установлен. Перезапусти GTA.')
        : (r.errorMessage ?? t('minimap.editor.fontInstallError', 'Ошибка установки шрифта.')));
      if (r.success) { setFontPick(null); bridge.minimapGetFontState?.().then(setFontState).catch(() => {}); }
    } catch (e) {
      setFontMsg(String(e));
    } finally { setFontBusy(false); }
  };
  const restoreFont = async () => {
    if (fontBusy) return;
    setFontBusy(true); setFontMsg(null);
    try {
      const r = await bridge.minimapRestoreFont();
      setFontMsg(r.success
        ? t('minimap.editor.fontRestored', 'Стоковый шрифт возвращён. Перезапусти GTA.')
        : (r.errorMessage ?? t('minimap.editor.fontRestoreError', 'Ошибка возврата шрифта.')));
      if (r.success) bridge.minimapGetFontState?.().then(setFontState).catch(() => {});
    } catch (e) {
      setFontMsg(String(e));
    } finally { setFontBusy(false); }
  };
  const stageRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{ what: Exclude<Sel, null>; dx: number; dy: number; w: number; h: number } | null>(null);
  const elRefs = useRef<Partial<Record<Exclude<Sel, null>, HTMLElement | null>>>({});
  const reg = (what: Exclude<Sel, null>) => (el: HTMLElement | null) => { elRefs.current[what] = el; };

  useEffect(() => {
    if (!open) return;
    setTw(initial ?? DEFAULT_MINIMAP_TWEAKS);
    setRingsOn(initialRings.on);
    setRatio(initialLayout.ratio || '16:9');
    setPos({ posX: initialLayout.posX ?? VAN.posX, posY: initialLayout.posY ?? VAN.posY });
    setTransparent(initialLayout.transparent);
    setTab('content');
    setSafe(readSavedSafe());
    setSafeProfileN(null);
    setSafeRead('pending');
    setPresetsState('pending');
    setScreenPick(null);
    let alive = true;
    bridge.minimapLayoutGetPresets?.()
      .then(rows => {
        if (!alive) return;
        if (rows?.length) { setPresets(rows); setPresetsState('ok'); } else setPresetsState('fail');
      })
      .catch(() => { if (alive) setPresetsState('fail'); });
    bridge.minimapGetSafezone?.()
      .then(n => {
        if (!alive) return;
        if (typeof n === 'number' && Number.isFinite(n)) { setSafeProfileN(n); setSafeRead('ok'); }
        else setSafeRead('fail');
      })
      .catch(() => { if (alive) setSafeRead('fail'); });
    bridge.minimapGetScreen?.()
      .then(s => { if (alive && s && s.width > 0 && s.height > 0) setScreenReal(s); })
      .catch(() => {});
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initial, initialLayout.ratio, initialLayout.posX, initialLayout.posY, initialLayout.transparent]);

  useEffect(() => () => { if (posBg) URL.revokeObjectURL(posBg); }, [posBg]);

  const set = (p: Partial<MinimapTweaks>) => setTw(t => ({ ...t, ...p }));
  const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));

  const preset = (r: string, placement: string): MinimapLayoutPreset =>
    presets.find(p => p.ratio === r && p.placement === placement)
      ?? { ratio: r, placement, posX: VAN.posX, posY: VAN.posY, sizeX: VAN.sizeX, sizeY: VAN.sizeY };

  const barXY = () => {
    const thickY = (tw.barScaleY ?? tw.barScale) / 100;
    switch (tw.barPosition) {
      case 'top':    return { x: 6 + tw.barOffsetX,   y: 3.5 + tw.barOffsetY, vert: false, boxDx: 0 };
      case 'bottom': return { x: 6 + tw.barOffsetX,   y: 126 + tw.barOffsetY, vert: false, boxDx: 0 };
      case 'left':   return { x: 10 + tw.barOffsetX,  y: 8 + tw.barOffsetY,   vert: true,  boxDx: -BAR_UNITS * thickY };
      case 'right':  return { x: 181 + tw.barOffsetX, y: 8 + tw.barOffsetY,   vert: true,  boxDx: -BAR_UNITS * thickY };
      default:       return { x: 6 + tw.barOffsetX,   y: 126 + tw.barOffsetY, vert: false, boxDx: 0 };
    }
  };

  const [grid, setGrid] = useState(true);
  const [snapGuides, setSnapGuides] = useState<{ x?: number; y?: number }>({});
  const V_GUIDES = [MAP.x, MAP.x + MAP.w / 2, MAP.x + MAP.w];
  const H_GUIDES = [MAP.y, MAP.y + MAP.h / 2, MAP.y + MAP.h, 126];
  const SNAP = 1.5;

  const onMove = (cx: number, cy: number, free = false) => {
    const st = stageRef.current, d = dragRef.current;
    if (!st || !d) return;
    const r = st.getBoundingClientRect();
    let gx = clamp((cx - r.left - d.dx) / Z, -20, STAGE_W + 20);
    let gy = clamp((cy - r.top - d.dy) / Z, 0, STAGE_H + 20);
    let sx: number | undefined, sy: number | undefined;
    if (!free) {
      for (const g of V_GUIDES) if (Math.abs(gx - g) <= SNAP) { gx = g; sx = g; break; }
      if (sx === undefined && d.w > 0) {
        const cxu = gx + d.w / 2, mid = MAP.x + MAP.w / 2;
        if (Math.abs(cxu - mid) <= SNAP) { gx = mid - d.w / 2; sx = mid; }
      }
      for (const g of H_GUIDES) if (Math.abs(gy - g) <= SNAP) { gy = g; sy = g; break; }
      if (sy === undefined && d.h > 0) {
        const cyu = gy + d.h / 2, mid = MAP.y + MAP.h / 2;
        if (Math.abs(cyu - mid) <= SNAP) { gy = mid - d.h / 2; sy = mid; }
      }
    }
    setSnapGuides({ x: sx, y: sy });
    const rx = Math.round(gx), ry = Math.round(gy);
    const dk = Math.max(0.05, tw.digitsScale / 100);
    const tk = Math.max(0.05, tw.customTextScale / 100);
    if (d.what === 'digitHp') set({ digitsHpDx: Math.round((gx - tw.digitsX) / dk - 2), digitsHpDy: Math.round((gy - tw.digitsY) / dk - 2) });
    else if (d.what === 'digitAr') set({ digitsArmorDx: Math.round((gx - tw.digitsX) / dk - 2), digitsArmorDy: Math.round((gy - tw.digitsY) / dk - 2) });
    else if (d.what === 'popup') set({ popupX: rx, popupY: ry });
    else if (d.what === 'popupAr') set({ armorPopupX: rx, armorPopupY: ry });
    else if (d.what === 'text') set({ customTextX: Math.round(gx - 2 * tk), customTextY: Math.round(gy - 2 * tk) });
    else if (d.what === 'hit') set({ hitX: Math.round(gx + d.w / 2), hitY: Math.round(gy + d.h / 2) });
    else {
      const b = barXY();
      set({ barOffsetX: Math.round(tw.barOffsetX + (rx - b.boxDx - b.x)), barOffsetY: Math.round(tw.barOffsetY + (ry - b.y)) });
    }
  };

  const startDrag = (what: Exclude<Sel, null>) => (e: React.PointerEvent) => {
    e.stopPropagation();
    setSel(what);
    const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
    dragRef.current = { what, dx: e.clientX - r.left, dy: e.clientY - r.top, w: r.width / Z, h: r.height / Z };
    stageRef.current?.setPointerCapture?.(e.pointerId);
  };

  const nudge = (what: Exclude<Sel, null>, dx: number, dy: number) => {
    if (what === 'digitHp') set({ digitsHpDx: tw.digitsHpDx + dx, digitsHpDy: tw.digitsHpDy + dy });
    else if (what === 'digitAr') set({ digitsArmorDx: tw.digitsArmorDx + dx, digitsArmorDy: tw.digitsArmorDy + dy });
    else if (what === 'popup') set({ popupX: tw.popupX + dx, popupY: tw.popupY + dy });
    else if (what === 'popupAr') set({ armorPopupX: tw.armorPopupX + dx, armorPopupY: tw.armorPopupY + dy });
    else if (what === 'text') set({ customTextX: tw.customTextX + dx, customTextY: tw.customTextY + dy });
    else if (what === 'hit') set({ hitX: (tw.hitX ?? (MAP.x + MAP.w / 2)) + dx, hitY: (tw.hitY ?? (MAP.y + MAP.h / 2)) + dy });
    else set({ barOffsetX: tw.barOffsetX + dx, barOffsetY: tw.barOffsetY + dy });
  };

  const MID_X = MAP.x + MAP.w / 2, MID_Y = MAP.y + MAP.h / 2;

  const placeBox = (what: Exclude<Sel, null>, gx: number, gy: number, doX: boolean, doY: boolean) => {
    const dk = Math.max(0.05, tw.digitsScale / 100);
    const tk = Math.max(0.05, tw.customTextScale / 100);
    const p: Partial<MinimapTweaks> = {};
    if (what === 'digitHp') { if (doX) p.digitsHpDx = Math.round((gx - tw.digitsX) / dk - 2); if (doY) p.digitsHpDy = Math.round((gy - tw.digitsY) / dk - 2); }
    else if (what === 'digitAr') { if (doX) p.digitsArmorDx = Math.round((gx - tw.digitsX) / dk - 2); if (doY) p.digitsArmorDy = Math.round((gy - tw.digitsY) / dk - 2); }
    else if (what === 'popup') { if (doX) p.popupX = Math.round(gx); if (doY) p.popupY = Math.round(gy); }
    else if (what === 'popupAr') { if (doX) p.armorPopupX = Math.round(gx); if (doY) p.armorPopupY = Math.round(gy); }
    else if (what === 'text') { if (doX) p.customTextX = Math.round(gx - 2 * tk); if (doY) p.customTextY = Math.round(gy - 2 * tk); }
    else if (what !== 'hit') { const b = barXY(); if (doX) p.barOffsetX = Math.round(tw.barOffsetX + (gx - b.boxDx - b.x)); if (doY) p.barOffsetY = Math.round(tw.barOffsetY + (gy - b.y)); }
    set(p);
  };

  const centerSel = (axis: 'both' | 'h' | 'v') => {
    if (!sel) return;
    const doX = axis !== 'v', doY = axis !== 'h';
    if (sel === 'hit') {
      set({ hitX: Math.round(doX ? MID_X : (tw.hitX ?? MID_X)), hitY: Math.round(doY ? MID_Y : (tw.hitY ?? MID_Y)) });
      return;
    }
    const el = elRefs.current[sel];
    if (!el) return;
    const rc = el.getBoundingClientRect();
    placeBox(sel, MID_X - rc.width / Z / 2, MID_Y - rc.height / Z / 2, doX, doY);
  };

  const canCenterOnBar = sel === 'digitHp' || sel === 'digitAr';
  const centerOnBar = () => {
    if (!canCenterOnBar) return;
    const el = elRefs.current[sel!];
    if (!el) return;
    const rc = el.getBoundingClientRect();
    const w = rc.width / Z, h = rc.height / Z;

    const b = barXY();
    const isHp = sel === 'digitHp';
    const left = b.x + (b.vert ? b.boxDx : 0);
    const top  = b.y;
    const bw = b.vert ? BAR_UNITS * barKy : BAR_W * barKx;
    const bh = b.vert ? BAR_VERT_UNITS * barKx : BAR_UNITS * barKy;
    const cx = b.vert ? left + bw / 2 : left + bw * (isHp ? 0.25 : 0.75);
    const cy = b.vert ? top + bh * (isHp ? 0.25 : 0.75) : top + bh / 2;
    placeBox(sel!, cx - w / 2, cy - h / 2, true, true);
  };

  const resetSelPos = () => {
    if (!sel) return;
    const D = DEFAULT_MINIMAP_TWEAKS;
    if (sel === 'digitHp') set({ digitsHpDx: D.digitsHpDx, digitsHpDy: D.digitsHpDy });
    else if (sel === 'digitAr') set({ digitsArmorDx: D.digitsArmorDx, digitsArmorDy: D.digitsArmorDy });
    else if (sel === 'popup') set({ popupX: D.popupX, popupY: D.popupY });
    else if (sel === 'popupAr') set({ armorPopupX: D.armorPopupX, armorPopupY: D.armorPopupY });
    else if (sel === 'text') set({ customTextX: D.customTextX, customTextY: D.customTextY });
    else if (sel === 'hit') set({ hitX: D.hitX, hitY: D.hitY });
    else set({ barOffsetX: D.barOffsetX, barOffsetY: D.barOffsetY });
  };

  const [selSize, setSelSize] = useState<{ w: number; h: number } | null>(null);
  useEffect(() => {
    if (!open || !sel || sel === 'hit') { setSelSize(null); return; }
    const el = elRefs.current[sel];
    if (!el) { setSelSize(null); return; }
    const rc = el.getBoundingClientRect();
    setSelSize({ w: rc.width / Z, h: rc.height / Z });
  }, [open, sel, tw]);
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (tab !== 'content' || !sel) return;
      const tag = (document.activeElement as HTMLElement | null)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;
      if (!e.ctrlKey && !e.metaKey && !e.altKey && (e.key === 'c' || e.key === 'C' || e.key === 'с' || e.key === 'С')) {
        e.preventDefault();
        centerSel('both');
        return;
      }
      if (!e.ctrlKey && !e.metaKey && !e.altKey && (e.key === 'b' || e.key === 'B' || e.key === 'и' || e.key === 'И')) {
        e.preventDefault();
        centerOnBar();
        return;
      }
      const step = e.shiftKey ? 10 : 1;
      let dx = 0, dy = 0;
      if (e.key === 'ArrowLeft') dx = -step;
      else if (e.key === 'ArrowRight') dx = step;
      else if (e.key === 'ArrowUp') dy = -step;
      else if (e.key === 'ArrowDown') dy = step;
      if (!dx && !dy) return;
      e.preventDefault();
      nudge(sel, dx, dy);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, tab, sel, tw]);

  const posStageW = 820;
  const screenAuto = screenReal && screenReal.fromSettingsXml && screenReal.width > 0 && screenReal.height > 0
    ? screenReal.width / screenReal.height : null;
  const screenAr = posBg && posBgDim
    ? posBgDim.w / posBgDim.h
    : (screenPick ? (SCREENS.find(s => s.label === screenPick)?.ar ?? 16 / 9) : (screenAuto ?? 16 / 9));
  const posStageH = Math.round(posStageW / screenAr);
  const sizeRow = preset(ratio, 'default');
  const sizeKnown = presetsState === 'ok' && presets.some(p => p.ratio === ratio && p.placement === 'default');
  const posBoxW = sizeRow.sizeX * posStageW;
  const posBoxH = sizeRow.sizeY * posStageH;
  const posLeft = (pos.posX + safe) * posStageW;
  const posTop = posStageH - posBoxH + (pos.posY - safe) * posStageH;
  const posOut = {
    right:  pos.posX + safe + sizeRow.sizeX > 1.0005,
    left:   pos.posX + safe < -0.0005,
    top:    1 + pos.posY - safe - sizeRow.sizeY < -0.0005,
    bottom: 1 + pos.posY - safe > 1.0005,
  };
  const posOutAny = posOut.right || posOut.left || posOut.top || posOut.bottom;

  const posMove = (cx: number, cy: number) => {
    const st = posStageRef.current, d = posDragRef.current;
    if (!st || !d) return;
    const r = st.getBoundingClientRect();
    const slack = Math.max(SAFE_STEP, safe);
    const l = clamp(cx - r.left - d.dx, -slack * posStageW, posStageW - posBoxW + slack * posStageW);
    const t = clamp(cy - r.top - d.dy, -slack * posStageH, posStageH - posBoxH + slack * posStageH);
    setPos({
      posX: r4(l / posStageW - safe),
      posY: r4((t - posStageH + posBoxH) / posStageH + safe),
    });
  };

  const layoutDirty =
    ratio !== (initialLayout.ratio || '16:9') ||
    Math.abs(pos.posX - (initialLayout.posX ?? VAN.posX)) > 1e-6 ||
    Math.abs(pos.posY - (initialLayout.posY ?? VAN.posY)) > 1e-6 ||
    transparent !== initialLayout.transparent;

  const bar = barXY();

  const blDigits = useBaselineRatio(fontStyle(tw.digitsFont).fontFamily);
  const blStock = useBaselineRatio(GAME_FONT);
  const digitFsPx = px(DIGIT_EM * FONT_VIS * digitScale(tw.digitsFont) * tw.digitsScale / 100);
  const digitDrop = inkDropPx(0, digitFsPx, blDigits);
  const textFsPx = px(DIGIT_EM * FONT_VIS * digitScale(tw.digitsFont) * tw.customTextScale / 100);
  const textDrop = inkDropPx(0, textFsPx, blDigits);
  const popupFsPx = px(tw.popupSize * 0.94 * FONT_VIS);
  const popupDrop = inkDropPx(px(2 * tw.popupSize / 18), popupFsPx, blStock);

  const iconBtn = 'inline-flex items-center justify-center w-8 h-8 rounded-lg transition-colors ' +
    'text-text-secondary bg-bg-elevated/55 hover:text-accent hover:bg-bg-elevated/80 ' +
    'disabled:opacity-35 disabled:pointer-events-none';

  const dHp = demo?.hp ?? 100;
  const dAr = demo?.ar ?? 100;
  const lowNow = tw.lowHpThreshold !== null && dHp <= tw.lowHpThreshold;
  const hpBase = tw.barHpColor ?? '#34D399';
  const arColor = tw.barArmorColor ?? '#60A5FA';
  const troughOf = (own: string | null, fill: string | null): string | null => {
    if (own) return own;
    if (!fill) return null;
    const h = fill.replace('#', '');
    if (h.length !== 6) return null;
    const d = (i: number) => Math.round(parseInt(h.slice(i, i + 2), 16) * 0.22);
    return `rgb(${d(0)}, ${d(2)}, ${d(4)})`;
  };
  const hpTroughBg = troughOf(tw.barHpTroughColor, tw.barHpColor);
  const arTroughBg = troughOf(tw.barArmorTroughColor, tw.barArmorColor);
  const barKx = tw.barScale / 100;
  const barKy = (tw.barScaleY ?? tw.barScale) / 100;
  const gradStops = useMemo(() => ({
    full: tw.barGradFullColor ?? tw.barHpColor ?? '#34D399',
    mid:  tw.barGradMidColor ?? '#FFD400',
    low:  tw.barGradLowColor ?? tw.lowHpColor ?? '#FF3B3B',
  }), [tw.barGradFullColor, tw.barGradMidColor, tw.barGradLowColor, tw.barHpColor, tw.lowHpColor]);
  const hpGradientColor = (p: number): string => {
    const cp = Math.max(0, Math.min(100, p));
    const rgb = (h: string): [number, number, number] => {
      const s = h.replace('#', '');
      return [parseInt(s.slice(0, 2), 16), parseInt(s.slice(2, 4), 16), parseInt(s.slice(4, 6), 16)];
    };
    const full = rgb(gradStops.full), mid = rgb(gradStops.mid), low = rgb(gradStops.low);
    const L = (a: number, b: number, t: number) => Math.round(a + (b - a) * t);
    const [a, b] = cp >= 50 ? [mid, full] as const : [low, mid] as const;
    const t = cp >= 50 ? (cp - 50) / 50 : cp / 50;
    return `rgb(${L(a[0], b[0], t)},${L(a[1], b[1], t)},${L(a[2], b[2], t)})`;
  };
  const hpFillBg = tw.barHpGradient
    ? (demo ? hpGradientColor(dHp) : `linear-gradient(to right, ${gradStops.low}, ${gradStops.mid}, ${gradStops.full})`)
    : (lowNow ? (tw.lowHpColor ?? '#FF4040') : hpBase);

  const hitCx = tw.hitX ?? (MAP.x + MAP.w / 2);
  const hitCy = tw.hitY ?? (MAP.y + MAP.h / 2);
  const hitW = px(MAP.w * (tw.hitScale ?? 100) / 100);
  const hitH = px(MAP.h * (tw.hitScale ?? 100) / 100);

  const flashVisual = (
    <div className="absolute pointer-events-none overflow-hidden"
      style={{ left: px(MAP.x), top: px(MAP.y), width: px(MAP.w), height: px(MAP.h) }}>
      <div className="absolute"
        style={{ left: px(hitCx - MAP.x) - hitW / 2, top: px(hitCy - MAP.y) - hitH / 2, width: hitW, height: hitH }}>
        {hitPngUrl ? (
          <img src={hitPngUrl} alt="" className="max-w-none w-full h-full" style={{ objectFit: 'contain' }} />
        ) : (
          <div className="rounded-full w-full h-full"
            style={{ background: 'radial-gradient(circle, rgba(255,40,40,0.85) 0%, rgba(255,40,40,0) 70%)' }} />
        )}
      </div>
    </div>
  );

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          transition={{ duration: 0.2 }}
          className="fixed inset-0 z-[130] flex flex-col bg-bg-base"
        >
          <div className="shrink-0 flex items-center gap-3 px-5 h-14 border-b border-white/[0.07] bg-bg-elevated">
            <Move size={16} className="text-accent" />
            <h3 className="text-[13px] font-bold uppercase tracking-[0.13em] text-text-primary whitespace-nowrap">
              {t('minimap.editor.title', 'Редактор миникарты')}
            </h3>
            <div className="flex items-center gap-1.5 ml-4">
              {([['content', t('minimap.editor.tabContent', 'Элементы'), Sliders],
                 ['position', t('minimap.editor.tabPosition', 'Позиция на экране'), MapIcon]] as const).map(([k, l, Icon]) => (
                <button key={k} onClick={() => setTab(k)}
                  className={'btn-install btn-install--sm' + (tab === k ? ' btn-install--on' : '')}>
                  <Icon size={13} />
                  {l}
                </button>
              ))}
            </div>
            <div className="flex-1" />
            <button onClick={() => setTw(DEFAULT_MINIMAP_TWEAKS)} disabled={busy}
              className="btn-install btn-install--sm">
              <RotateCcw size={13} /> {t('minimap.editor.reset', 'Сброс')}
            </button>
            <button onClick={() => onApply(STOCK_MINIMAP_TWEAKS, null, initialRings.on ? false : null)} disabled={busy}
              title={t('minimap.editor.removeAllTitle', 'Вернуть миникарту без правок конструктора')}
              className="btn-install btn-install--sm">
              <Eraser size={13} /> {t('minimap.editor.removeAll', 'Снять всё')}
            </button>
            <button onClick={() => !busy && onClose()} disabled={busy}
              className="btn-install btn-install--sm">
              {t('common.cancel', 'Отмена')}
            </button>
            <button onClick={() => onApply(tw, layoutDirty ? { ratio, posX: pos.posX, posY: pos.posY, transparent } : null,
              ringsOn !== initialRings.on ? ringsOn : null)} disabled={busy}
              className="btn-install btn-install--sm">
              {busy ? <Loader2 size={14} className="animate-spin" /> : <Check size={14} strokeWidth={2.6} />}
              {layoutDirty
                ? t('minimap.editor.applyWithLayout', 'Применить (карту и позицию)')
                : t('minimap.editor.apply', 'Применить')}
            </button>
            <button onClick={() => !busy && onClose()} className="ml-1 text-text-muted hover:text-text-primary">
              <X size={18} />
            </button>
          </div>

          {tab === 'content' ? (
          <div className="flex-1 min-h-0 flex">
            <div className="flex-1 min-w-0 flex flex-col items-center justify-center gap-3 p-6 overflow-auto">
              <div className="flex items-center gap-2" style={{ width: px(STAGE_W) }}>
                <button onClick={() => setHitPreview(v => !v)}
                  className={'btn-install btn-install--sm' + (hitPreview ? ' btn-install--on' : '')}>
                  {t('minimap.editor.hitFlash', 'Вспышка урона')}
                </button>
                <button onClick={() => setGrid(v => !v)}
                  title={t('minimap.editor.gridTitle', 'Сетка 10 юнитов + оси центра радара; при драге элементы прилипают к краям и центру')}
                  className={'btn-install btn-install--sm' + (grid ? ' btn-install--on' : '')}>
                  {t('minimap.editor.grid', 'Сетка')}
                </button>
                <div className="flex items-center gap-1 pl-2 ml-0.5 border-l border-white/[0.08]">
                  <button disabled={!sel} onClick={() => centerSel('both')} className={iconBtn}
                    title={t('minimap.editor.centerBothTitle', 'Отцентрировать по центру карты (клавиша C)')}>
                    <Crosshair size={14} />
                  </button>
                  <button disabled={!sel} onClick={() => centerSel('h')} className={iconBtn}
                    title={t('minimap.editor.centerHTitle', 'Отцентрировать по горизонтали')}>
                    <AlignCenterVertical size={14} />
                  </button>
                  <button disabled={!sel} onClick={() => centerSel('v')} className={iconBtn}
                    title={t('minimap.editor.centerVTitle', 'Отцентрировать по вертикали')}>
                    <AlignCenterHorizontal size={14} />
                  </button>
                  <button disabled={!canCenterOnBar} onClick={centerOnBar} className={iconBtn}
                    title={t('minimap.editor.centerBarTitle', 'Отцентрировать по своей полосе (клавиша B)')}>
                    <Rows3 size={14} />
                  </button>
                  <button disabled={!sel} onClick={resetSelPos} className={iconBtn}
                    title={t('minimap.editor.resetSelPosTitle', 'Сбросить позицию выбранного к дефолту')}>
                    <RotateCcw size={13} />
                  </button>
                </div>
                <button onClick={playDemo}
                  className={'btn-install btn-install--sm ml-auto' + (demo ? ' btn-install--on' : '')}>
                  {demo ? <Square size={11} strokeWidth={3} /> : <Play size={11} strokeWidth={3} />}
                  {demo ? t('minimap.editor.demoStop', 'Стоп') : t('minimap.editor.demoPlay', 'Демо')}
                </button>
              </div>
              <div
                ref={stageRef}
                className="relative rounded-2xl overflow-hidden border border-white/[0.14] select-none touch-none shadow-2xl shrink-0"
                style={{
                  width: px(STAGE_W), height: px(STAGE_H),
                  backgroundColor: '#16191c',
                  backgroundImage:
                    'linear-gradient(45deg, #1d2126 25%, transparent 25%, transparent 75%, #1d2126 75%), ' +
                    'linear-gradient(45deg, #1d2126 25%, transparent 25%, transparent 75%, #1d2126 75%)',
                  backgroundSize: '16px 16px',
                  backgroundPosition: '0 0, 8px 8px',
                }}
                onPointerMove={e => { if (dragRef.current) onMove(e.clientX, e.clientY, e.altKey || e.ctrlKey); }}
                onPointerUp={() => { dragRef.current = null; setSnapGuides({}); }}
                onPointerLeave={() => { dragRef.current = null; setSnapGuides({}); }}
                onPointerDown={() => setSel(null)}
              >
                <div className="absolute rounded-[3px] border border-white/[0.10] pointer-events-none"
                  style={{
                    left: px(MAP.x), top: px(MAP.y), width: px(MAP.w), height: px(MAP.h),
                    background: 'radial-gradient(120% 120% at 30% 25%, #46523c 0%, #38412f 45%, #2a3126 100%)',
                  }} />

                {grid && (
                  <>
                    <div className="absolute inset-0 pointer-events-none" style={{
                      backgroundImage:
                        `repeating-linear-gradient(to right, rgba(255,255,255,0.055) 0, rgba(255,255,255,0.055) 1px, transparent 1px, transparent ${px(10)}px), ` +
                        `repeating-linear-gradient(to bottom, rgba(255,255,255,0.055) 0, rgba(255,255,255,0.055) 1px, transparent 1px, transparent ${px(10)}px)`,
                    }} />
                    <div className="absolute border-l border-dashed border-white/20 pointer-events-none"
                      style={{ left: px(MAP.x + MAP.w / 2), top: px(MAP.y), height: px(MAP.h) }} />
                    <div className="absolute border-t border-dashed border-white/20 pointer-events-none"
                      style={{ top: px(MAP.y + MAP.h / 2), left: px(MAP.x), width: px(MAP.w) }} />
                  </>
                )}

                {ringsOn && [26, 32.5].map(rr => (
                  <div key={rr} className="absolute rounded-full border border-dashed border-white/35 pointer-events-none"
                    style={{
                      left: px(MAP.x + MAP.w / 2 - rr), top: px(MAP.y + MAP.h / 2 - rr),
                      width: px(rr * 2), height: px(rr * 2),
                    }} />
                ))}

                {arrowUrl ? (
                  <img src={arrowUrl} alt="" className="absolute pointer-events-none object-contain drop-shadow"
                    style={{ left: px(MAP.x + MAP.w / 2 - 5), top: px(MAP.y + MAP.h / 2 - 5), width: px(10), height: px(10) }} />
                ) : (
                  <div className="absolute pointer-events-none bg-white/90 drop-shadow"
                    style={{
                      left: px(MAP.x + MAP.w / 2 - 3.5), top: px(MAP.y + MAP.h / 2 - 3.5),
                      width: px(7), height: px(7),
                      clipPath: 'polygon(50% 0, 100% 100%, 50% 76%, 0 100%)',
                    }} />
                )}
                {gpsUrl && (
                  <img src={gpsUrl} alt="" className="absolute pointer-events-none object-contain drop-shadow"
                    style={{ left: px(MAP.x + MAP.w * 0.72 - 5), top: px(MAP.y + MAP.h * 0.3 - 5), width: px(10), height: px(10) }} />
                )}

                {snapGuides.x !== undefined && (
                  <div className="absolute top-0 bottom-0 w-px bg-accent/90 pointer-events-none" style={{ left: px(snapGuides.x) }} />
                )}
                {snapGuides.y !== undefined && (
                  <div className="absolute left-0 right-0 h-px bg-accent/90 pointer-events-none" style={{ top: px(snapGuides.y) }} />
                )}

                <div
                  ref={reg('bar')}
                  onPointerDown={startDrag('bar')}
                  className={'absolute cursor-grab active:cursor-grabbing ring-offset-0 ' +
                    (sel === 'bar' ? 'ring-2 ring-accent' : 'hover:ring-1 hover:ring-white/40')}
                  style={bar.vert
                    ? { left: px(bar.x + bar.boxDx), top: px(bar.y), width: px(BAR_UNITS * barKy), height: px(BAR_VERT_UNITS * barKx) }
                    : { left: px(bar.x), top: px(bar.y), width: px(BAR_W * barKx), height: px(BAR_UNITS * barKy) }}
                  title={t('minimap.editor.elBar', 'Полоска HP / брони')}
                >
                  {!bar.vert && (
                    <div className="absolute rounded-[2px] bg-black/50 pointer-events-none"
                      style={{ left: -px(1), top: -px(1), width: px(BAR_W * barKx + 2), height: px(BAR_UNITS * barKy + 2) }} />
                  )}
                  <div className={'absolute inset-0 flex ' + (bar.vert ? 'flex-col' : 'flex-row') + ' gap-[2px]'}>
                    <div className={'flex-1 relative rounded-sm overflow-hidden' + (hpTroughBg ? '' : ' bg-black/45')}
                      style={hpTroughBg ? { background: hpTroughBg, transition: 'background 200ms' } : undefined}>
                      <div className="absolute left-0 top-0 rounded-sm"
                        style={bar.vert
                          ? { width: '100%', height: `${dHp}%`, background: hpFillBg, transition: 'height 260ms ease-out, background 200ms' }
                          : { height: '100%', width: `${dHp}%`, background: hpFillBg, transition: 'width 260ms ease-out, background 200ms' }} />
                    </div>
                    <div className={'flex-1 relative rounded-sm overflow-hidden' + (arTroughBg ? '' : ' bg-black/45')}
                      style={arTroughBg ? { background: arTroughBg, transition: 'background 200ms' } : undefined}>
                      <div className="absolute left-0 top-0 rounded-sm"
                        style={bar.vert
                          ? { width: '100%', height: `${dAr}%`, background: arColor, transition: 'height 260ms ease-out' }
                          : { height: '100%', width: `${dAr}%`, background: arColor, transition: 'width 260ms ease-out' }} />
                    </div>
                  </div>
                </div>

                {hitPreview && !demo && (
                  <div style={{ opacity: (tw.hitAlpha ?? 75) / 100 }} className="absolute inset-0 pointer-events-none">
                    {flashVisual}
                  </div>
                )}
                <AnimatePresence>
                  {demo && flashId > 0 && (
                    <motion.div key={flashId}
                      className="absolute inset-0 pointer-events-none"
                      initial={{ opacity: (tw.hitAlpha ?? 75) / 100 }}
                      animate={{ opacity: 0 }}
                      exit={{ opacity: 0 }}
                      transition={{ duration: tw.hitFadeSeconds ?? 1.5, ease: 'easeOut' }}>
                      {flashVisual}
                    </motion.div>
                  )}
                </AnimatePresence>

                {!demo && tw.hitAlpha !== null && tw.hitAlpha !== 0 && (
                  <div
                    onPointerDown={startDrag('hit')}
                    className={'absolute cursor-grab active:cursor-grabbing rounded-full border-2 ' +
                      (sel === 'hit' ? 'border-accent' : 'border-white/50 hover:border-white/80')}
                    style={{ left: px(hitCx) - 7, top: px(hitCy) - 7, width: 14, height: 14 }}
                    title={t('minimap.editor.elHitCenter', 'Центр вспышки урона')}
                  />
                )}

                {tw.digits && (
                  <div
                    ref={reg('digitHp')}
                    onPointerDown={startDrag('digitHp')}
                    className={'absolute cursor-grab active:cursor-grabbing rounded ' +
                      (sel === 'digitHp' ? 'ring-2 ring-accent' : 'hover:ring-1 hover:ring-white/40')}
                    style={{
                      left: px(tw.digitsX + (tw.digitsHpDx + 2) * tw.digitsScale / 100),
                      top:  px(tw.digitsY + (tw.digitsHpDy + 2) * tw.digitsScale / 100),
                    }}
                    title={t('minimap.editor.elDigitHp', 'Цифра HP')}
                  >
                    <span className="leading-none drop-shadow"
                      style={{ position: 'relative', top: digitDrop, fontSize: digitFsPx, ...fontStyle(tw.digitsFont), color: tw.digitsHpColor ?? '#FFFFFF' }}>{Math.round(dHp)}</span>
                  </div>
                )}
                {tw.digits && (
                  <div
                    ref={reg('digitAr')}
                    onPointerDown={startDrag('digitAr')}
                    className={'absolute cursor-grab active:cursor-grabbing rounded ' +
                      (sel === 'digitAr' ? 'ring-2 ring-accent' : 'hover:ring-1 hover:ring-white/40')}
                    style={{
                      left: px(tw.digitsX + (tw.digitsArmorDx + 2) * tw.digitsScale / 100),
                      top:  px(tw.digitsY + (tw.digitsArmorDy + 2) * tw.digitsScale / 100),
                    }}
                    title={t('minimap.editor.elDigitArmor', 'Цифра брони')}
                  >
                    <span className="leading-none drop-shadow"
                      style={{ position: 'relative', top: digitDrop, fontSize: digitFsPx, ...fontStyle(tw.digitsFont), color: tw.digitsArmorColor ?? '#FFFFFF' }}>{Math.round(dAr)}</span>
                  </div>
                )}

                {!demo && (tw.damagePopup || tw.healPopup) && (
                  <div
                    ref={reg('popup')}
                    onPointerDown={startDrag('popup')}
                    className={'absolute cursor-grab active:cursor-grabbing px-1 rounded ' +
                      (sel === 'popup' ? 'ring-2 ring-accent' : 'hover:ring-1 hover:ring-white/40')}
                    style={{ left: px(tw.popupX), top: px(tw.popupY) }}
                    title={t('minimap.editor.elPopupHp', 'Всплывающий урон HP')}
                  >
                    <span className="leading-none drop-shadow"
                      style={{ position: 'relative', top: popupDrop, color: tw.damagePopup ? tw.damageColor : tw.healColor, fontFamily: GAME_FONT, fontSize: popupFsPx }}>
                      {tw.damagePopup ? '-15' : '+15'}
                    </span>
                  </div>
                )}
                {!demo && tw.armorPopup && (
                  <div
                    ref={reg('popupAr')}
                    onPointerDown={startDrag('popupAr')}
                    className={'absolute cursor-grab active:cursor-grabbing px-1 rounded ' +
                      (sel === 'popupAr' ? 'ring-2 ring-accent' : 'hover:ring-1 hover:ring-white/40')}
                    style={{ left: px(tw.armorPopupX), top: px(tw.armorPopupY) }}
                    title={t('minimap.editor.elPopupArmor', 'Всплывающий урон по броне')}
                  >
                    <span className="leading-none drop-shadow"
                      style={{ position: 'relative', top: popupDrop, color: tw.armorPopupColor, fontFamily: GAME_FONT, fontSize: popupFsPx }}>
                      -30
                    </span>
                  </div>
                )}

                {tw.customText && (
                  <div
                    ref={reg('text')}
                    onPointerDown={startDrag('text')}
                    className={'absolute cursor-grab active:cursor-grabbing rounded ' +
                      (sel === 'text' ? 'ring-2 ring-accent' : 'hover:ring-1 hover:ring-white/40')}
                    style={{
                      left: px(tw.customTextX + 2 * tw.customTextScale / 100),
                      top: px(tw.customTextY + 2 * tw.customTextScale / 100),
                    }}
                    title={t('minimap.editor.elText', 'Текст на миникарте')}
                  >
                    <span className="leading-none drop-shadow whitespace-nowrap"
                      style={{ position: 'relative', top: textDrop, color: tw.customTextColor, ...fontStyle(tw.digitsFont), fontSize: textFsPx }}>
                      {tw.customText}
                    </span>
                  </div>
                )}

                <AnimatePresence>
                  {fx && (
                    <motion.div key={fx.id}
                      className="absolute pointer-events-none"
                      style={{
                        left: px(fx.kind === 'armor' ? tw.armorPopupX : tw.popupX),
                        top:  px(fx.kind === 'armor' ? tw.armorPopupY : tw.popupY),
                      }}
                      initial={{ opacity: 1, y: 0 }}
                      animate={{ opacity: 0, y: -px(8) }}
                      exit={{ opacity: 0 }}
                      transition={{ duration: tw.popupSeconds, ease: 'linear' }}>
                      <span className="leading-none drop-shadow"
                        style={{
                          position: 'relative', top: popupDrop,
                          color: fx.kind === 'hp' ? tw.damageColor : fx.kind === 'heal' ? tw.healColor : tw.armorPopupColor,
                          fontFamily: GAME_FONT, fontSize: popupFsPx,
                        }}>
                        {fx.text}
                      </span>
                    </motion.div>
                  )}
                </AnimatePresence>

                {sel && (() => {
                  const dk2 = tw.digitsScale / 100;
                  const b2 = barXY();
                  const c =
                    sel === 'digitHp' ? { l: t('minimap.editor.roDigitHp', 'цифра HP'), x: tw.digitsX + (tw.digitsHpDx + 2) * dk2, y: tw.digitsY + (tw.digitsHpDy + 2) * dk2 }
                    : sel === 'digitAr' ? { l: t('minimap.editor.roDigitArmor', 'цифра брони'), x: tw.digitsX + (tw.digitsArmorDx + 2) * dk2, y: tw.digitsY + (tw.digitsArmorDy + 2) * dk2 }
                    : sel === 'popup' ? { l: t('minimap.editor.roPopupHp', 'попап HP'), x: tw.popupX, y: tw.popupY }
                    : sel === 'popupAr' ? { l: t('minimap.editor.roPopupArmor', 'попап брони'), x: tw.armorPopupX, y: tw.armorPopupY }
                    : sel === 'text' ? { l: t('minimap.editor.roText', 'текст'), x: tw.customTextX, y: tw.customTextY }
                    : { l: t('minimap.editor.roBar', 'полоса'), x: b2.x, y: b2.y };
                  return (
                    <div className="absolute bottom-1.5 left-2 text-[10px] font-mono text-accent/90 pointer-events-none">
                      {c.l}: x {Math.round(c.x * 10) / 10} · y {Math.round(c.y * 10) / 10}
                      {selSize && ` · ${Math.round(selSize.w)}×${Math.round(selSize.h)}`}
                    </div>
                  );
                })()}
                <div className="absolute bottom-1.5 right-2 text-[10px] font-mono text-white/45 pointer-events-none">
                  {t('minimap.editor.stageInfo', {
                    w: STAGE_W, h: STAGE_H, mw: MAP.w, mh: MAP.h,
                    defaultValue: 'сцена {{w}}×{{h}} · радар {{mw}}×{{mh}}',
                  })}
                </div>
              </div>
              <p className="text-[11px] text-text-muted leading-snug" style={{ width: px(STAGE_W) }}>
                <Trans
                  i18nKey="minimap.editor.sceneHint"
                  defaults="Тащи элементы мышью - они прилипают к краям и центру карты (подсветится линия); зажми <b>Alt</b>, чтобы двигать свободно, без прилипания.<b> Центр</b> (кнопки в тулбаре или клавиша <b>C</b>) ставит выбранный элемент ровно в центр карты. Точная подгонка: <b>стрелки</b> - на 1 юнит,<b> Shift+стрелки</b> - на 10. Шахматка - прозрачная зона (в игре там мир). Выше верхнего края поставить нельзя: игра обрезает всё, что за кадром."
                  components={{ b: <b /> }}
                />
              </p>
            </div>

            <div className="w-[400px] shrink-0 border-l border-white/[0.07] bg-bg-elevated overflow-y-auto">
              <div className="p-4 flex flex-col gap-3">

                <Section title={t('minimap.editor.cardDigits', 'Цифры HP и брони')} on={tw.digits} onToggle={() => set({ digits: !tw.digits })}>
                  <Two>
                    <Col label={t('minimap.editor.colorHp', 'Цвет HP')}><Color v={tw.digitsHpColor ?? '#FFFFFF'} on={v => set({ digitsHpColor: v })} /></Col>
                    <Col label={t('minimap.editor.colorArmor', 'Цвет брони')}><Color v={tw.digitsArmorColor ?? '#FFFFFF'} on={v => set({ digitsArmorColor: v })} /></Col>
                  </Two>
                  <Num label={t('minimap.editor.size', 'Размер')} v={tw.digitsScale} min={40} max={300} step={5} suffix="%"
                    on={v => {
                      const k1 = Math.max(0.05, tw.digitsScale / 100), k2 = Math.max(0.05, v / 100);
                      const conv = (d: number) => Math.round((((d + 2) * k1) / k2 - 2) * 10) / 10;
                      set({
                        digitsScale: v,
                        digitsHpDx: conv(tw.digitsHpDx), digitsHpDy: conv(tw.digitsHpDy),
                        digitsArmorDx: conv(tw.digitsArmorDx), digitsArmorDy: conv(tw.digitsArmorDy),
                      });
                    }} />
                </Section>

                <Card title={t('minimap.editor.cardSave', 'Моё сохранение')}>
                  {mySave ? (
                    <>
                      <div className="flex items-center gap-2 min-w-0">
                        <Save size={13} className="text-accent shrink-0" />
                        <span className="text-[12px] text-text-primary truncate flex-1">{mySave.name}</span>
                        <span className="text-[10.5px] text-text-muted shrink-0">
                          {new Date(mySave.savedAt).toLocaleDateString()}
                        </span>
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        <button type="button" className={smallBtn}
                          onClick={() => { setTw(mySave.tweaks); setSaveNote('Сохранение загружено, нажми «Применить»'); }}>
                          {t('minimap.editor.saveLoad', 'Загрузить')}
                        </button>
                        <button type="button" className={smallBtn} onClick={() => void writeMySave(mySave.name)}>
                          {t('minimap.editor.saveOverwrite', 'Перезаписать')}
                        </button>
                        <button type="button" className={smallBtn} onClick={() => void clearMySave()}>
                          {t('minimap.editor.saveDelete', 'Удалить')}
                        </button>
                      </div>
                    </>
                  ) : (
                    <>
                      <input
                        type="text"
                        value={saveName}
                        onChange={e => setSaveName(e.target.value.slice(0, 40))}
                        placeholder={t('minimap.editor.saveNamePlaceholder', 'Название, до 40 символов')}
                        className="w-full h-9 px-2.5 rounded-lg bg-bg-elevated border border-border-subtle
                                   text-[12px] text-text-primary outline-none focus:border-[color:var(--accent)]"
                      />
                      <button type="button" className={smallBtn} onClick={() => void writeMySave(saveName)}>
                        {t('minimap.editor.saveWrite', 'Сохранить как своё')}
                      </button>
                    </>
                  )}
                  <p className="text-[10.5px] text-text-muted leading-snug">
                    {saveNote ?? t('minimap.editor.saveHint',
                      'Слот один и лежит на твоём компьютере. Никуда не публикуется - это способ вернуть вид, если его сбросил редукс или «Снять всё».')}
                  </p>
                </Card>

                <Card title={t('minimap.editor.cardBar', 'Полоска HP / брони')}>
                  <div className="flex items-stretch p-1 rounded-xl bg-bg-surface border border-border-subtle">
                    {([['default', t('minimap.editor.barPosDefault', 'Сток')],
                       ['top', t('minimap.editor.barPosTop', 'Сверху')],
                       ['bottom', t('minimap.editor.barPosBottom', 'Снизу')],
                       ['left', t('minimap.editor.barPosLeft', 'Слева')],
                       ['right', t('minimap.editor.barPosRight', 'Справа')]] as const)
                      .map(([k, l]) => (
                        <button key={k} onClick={() => set({ barPosition: k, barOffsetX: 0, barOffsetY: 0 })}
                          className={'flex-1 h-8 rounded-lg text-[11.5px] font-medium transition-colors ' +
                            (tw.barPosition === k ? 'bg-accent text-text-on-accent' : 'text-text-secondary hover:text-text-primary')}>
                          {l}
                        </button>
                      ))}
                  </div>
                  <Row label={t('minimap.editor.ownHpColor', 'Свой цвет HP')} on={tw.barHpColor !== null}
                    disabled={tw.barHpColor === null && tw.barHpGradient}
                    hint={t('minimap.editor.ownHpColorHint', 'Недоступно: включён градиент - он сам красит полосу HP')}
                    onToggle={() => set({ barHpColor: tw.barHpColor === null ? '#34D399' : null })} />
                  {tw.barHpColor !== null && (
                    <Col label={t('minimap.editor.barHpColor', 'Цвет полосы HP')}><Color v={tw.barHpColor} on={v => set({ barHpColor: v })} /></Col>
                  )}
                  <Row label={t('minimap.editor.ownArmorColor', 'Свой цвет брони')} on={tw.barArmorColor !== null}
                    onToggle={() => set({ barArmorColor: tw.barArmorColor === null ? '#60A5FA' : null })} />
                  {tw.barArmorColor !== null && (
                    <Col label={t('minimap.editor.barArmorColor', 'Цвет полосы брони')}><Color v={tw.barArmorColor} on={v => set({ barArmorColor: v })} /></Col>
                  )}
                  <Row label={t('minimap.editor.ownHpTrough', 'Свой цвет пустой части HP')} on={tw.barHpTroughColor !== null}
                    onToggle={() => set({ barHpTroughColor: tw.barHpTroughColor === null ? '#383838' : null })} />
                  {tw.barHpTroughColor !== null && (
                    <Col label={t('minimap.editor.barHpTroughColor', 'Пустая часть HP')}><Color v={tw.barHpTroughColor} on={v => set({ barHpTroughColor: v })} /></Col>
                  )}
                  <Row label={t('minimap.editor.ownArmorTrough', 'Свой цвет пустой части брони')} on={tw.barArmorTroughColor !== null}
                    onToggle={() => set({ barArmorTroughColor: tw.barArmorTroughColor === null ? '#383838' : null })} />
                  {tw.barArmorTroughColor !== null && (
                    <Col label={t('minimap.editor.barArmorTroughColor', 'Пустая часть брони')}><Color v={tw.barArmorTroughColor} on={v => set({ barArmorTroughColor: v })} /></Col>
                  )}
                  <Num label={t('minimap.editor.barWidth', 'Ширина полосы')} v={tw.barScale} min={40} max={160} step={1} on={v => set({ barScale: v })} suffix="%" />
                  <Num label={t('minimap.editor.barThickness', 'Толщина полосы')} v={tw.barScaleY ?? tw.barScale} min={40} max={160} step={1} on={v => set({ barScaleY: v })} suffix="%" />
                  <Row label={t('minimap.editor.hpGradient', 'Плавный цвет HP (градиент)')} on={tw.barHpGradient}
                    disabled={!tw.barHpGradient && (tw.barHpColor !== null || tw.lowHpThreshold !== null)}
                    hint={t('minimap.editor.hpGradientHint', 'Недоступно: выключи «Свой цвет HP» и «Порог малого HP»')}
                    onToggle={() => set({ barHpGradient: !tw.barHpGradient })} />
                  {tw.barHpGradient && (
                    <div className="flex flex-col gap-2 pl-2 border-l-2 border-white/[0.06]">
                      <div className="h-2.5 rounded-full border border-white/15"
                        style={{ background: `linear-gradient(to right, ${gradStops.low}, ${gradStops.mid}, ${gradStops.full})` }} />
                      <Two>
                        <Col label={t('minimap.editor.gradLow', 'Мало HP')}><Color v={gradStops.low} on={v => set({ barGradLowColor: v })} /></Col>
                        <Col label={t('minimap.editor.gradMid', 'Середина')}><Color v={gradStops.mid} on={v => set({ barGradMidColor: v })} /></Col>
                      </Two>
                      <Col label={t('minimap.editor.gradFull', 'Полное HP')}><Color v={gradStops.full} on={v => set({ barGradFullColor: v })} /></Col>
                      <div className="text-[10.5px] text-text-muted">
                        {t('minimap.editor.gradNote', 'Цвет полосы плавно меняется по проценту HP. Нажми «Демо», чтобы увидеть в динамике.')}
                      </div>
                    </div>
                  )}
                  <Row label={t('minimap.editor.pulseLowHp', 'Пульс при малом HP')} on={tw.barPulseLowHp}
                    onToggle={() => set({ barPulseLowHp: !tw.barPulseLowHp })} />
                  {tw.barPulseLowHp && (
                    <div className="text-[10.5px] text-text-muted">
                      {t('minimap.editor.pulseLowHpNote', 'Полоса HP мигает, когда здоровье ниже значения из «Порог малого HP» (без него берётся 25%).')}
                    </div>
                  )}
                </Card>

                <Card title={t('minimap.editor.cardPopups', 'Всплывающий урон')}>
                  <Row label={t('minimap.editor.popupDamage', 'Урон HP «−15»')} on={tw.damagePopup} onToggle={() => set({ damagePopup: !tw.damagePopup })} />
                  <Row label={t('minimap.editor.popupHeal', 'Лечение «+X»')} on={tw.healPopup} onToggle={() => set({ healPopup: !tw.healPopup })} />
                  <Row label={t('minimap.editor.popupArmor', 'Урон по броне, отдельно')} on={tw.armorPopup} onToggle={() => set({ armorPopup: !tw.armorPopup })} />
                  {(tw.damagePopup || tw.healPopup || tw.armorPopup) && (
                    <>
                      <Two>
                        <Col label={t('minimap.editor.colorDamage', 'Цвет урона HP')}><Color v={tw.damageColor} on={v => set({ damageColor: v })} /></Col>
                        <Col label={t('minimap.editor.colorHeal', 'Цвет лечения')}><Color v={tw.healColor} on={v => set({ healColor: v })} /></Col>
                      </Two>
                      {tw.armorPopup && (
                        <Col label={t('minimap.editor.colorArmorDamage', 'Цвет урона по броне')}><Color v={tw.armorPopupColor} on={v => set({ armorPopupColor: v })} /></Col>
                      )}
                      <Num label={t('minimap.editor.size', 'Размер')} v={tw.popupSize} min={8} max={48} step={1} on={v => set({ popupSize: v })} suffix="px" />
                      <Num label={t('minimap.editor.popupHold', 'Держится')} v={tw.popupSeconds} min={0.3} max={4} step={0.1} on={v => set({ popupSeconds: v })} suffix="с" />
                    </>
                  )}
                </Card>

                <Card title={t('minimap.editor.cardFont', 'Шрифт миникарты')}>
                  <FontPick
                    label={t('minimap.editor.fontFamily', 'Гарнитура')}
                    v={tw.digitsFont}
                    opts={fontOpts}
                    on={v => set({ digitsFont: v })}
                    note={t('minimap.editor.fontNote', 'Шрифты самой GTA: работают у всех, ничего не качается. Превью рисуется ими же - что видите, то и будет в игре. Общий для цифр и текста.')}
                  />
                  <div
                    className="rounded-lg border border-border-subtle bg-bg-surface px-3 py-2.5
                               text-text-primary leading-none overflow-hidden"
                    style={{ ...fontStyle(tw.digitsFont), fontSize: 22 }}
                  >
                    {tw.customText?.trim() || '100  ARMOR  99'}
                  </div>
                  {EMBEDDED_FONTS.has(tw.digitsFont ?? '') && (
                    <div className="text-[11px] leading-snug text-text-secondary">
                      {t('minimap.editor.fontEmbedNote', 'Этого шрифта нет среди импортов миникарты - его глифы вшиваются прямо в minimap.gfx (+6…37 КБ).')}
                    </div>
                  )}
                  {NO_CYRILLIC.has(tw.digitsFont ?? '') && hasCyrillic(tw.customText) && (
                    <div className="text-[11px] leading-snug text-[color:var(--danger,#f87171)]">
                      {t('minimap.editor.fontNoCyrillic', 'У этой гарнитуры нет русских букв - в игре текст не отобразится. Кириллицу умеют «Обычный», «Как логотип GTA» и «Рукописный».')}
                    </div>
                  )}
                </Card>

                <Card title={t('minimap.editor.cardText', 'Текст на миникарте')}>
                  <input
                    value={tw.customText ?? ''}
                    onChange={e => set({
                      customText: e.target.value.replace(/[^\x20-\x7E]/g, '').slice(0, 32) || null
                    })}
                    placeholder={t('minimap.editor.textPlaceholder', 'Ник или тег, латиница, до 32')}
                    className="font-mono text-[12px] text-text-primary bg-bg-surface border border-border-subtle rounded-lg px-2.5 py-2 outline-none focus:border-[color:var(--accent)]"
                  />
                  {tw.customText && (
                    <>
                      <Col label={t('minimap.editor.colorText', 'Цвет текста')}><Color v={tw.customTextColor} on={v => set({ customTextColor: v })} /></Col>
                      <Num label={t('minimap.editor.size', 'Размер')} v={tw.customTextScale} min={20} max={400} step={5} on={v => set({ customTextScale: v })} suffix="%" />
                    </>
                  )}
                </Card>

                <Section title={t('minimap.editor.cardLowHp', 'Порог малого HP')} on={tw.lowHpThreshold !== null}
                  disabled={tw.lowHpThreshold === null && tw.barHpGradient}
                  hint={t('minimap.editor.lowHpHint', 'Недоступно: включён градиент - цвет HP уже плавно меняется по проценту')}
                  onToggle={() => set({
                    lowHpThreshold: tw.lowHpThreshold === null ? 25 : null,
                    lowHpColor: tw.lowHpThreshold === null ? (tw.lowHpColor ?? '#FF4040') : null,
                  })}>
                  <Num label={t('minimap.editor.lowHpValue', 'Порог')} v={tw.lowHpThreshold ?? 25} min={1} max={99} step={1}
                    on={v => set({ lowHpThreshold: Math.round(v) })} suffix="% HP" />
                  <Col label={t('minimap.editor.lowHpColor', 'Цвет ХП при меньшем пороге')}><Color v={tw.lowHpColor ?? '#FF4040'} on={v => set({ lowHpColor: v })} /></Col>
                </Section>

                <Section title={t('minimap.editor.cardHit', 'Настроить вспышку урона')} on={tw.hitAlpha !== null}
                  onToggle={() => set({
                    hitAlpha: tw.hitAlpha === null ? 75 : null,
                    hitFadeSeconds: tw.hitAlpha === null ? 1.5 : null,
                    hitScale: tw.hitAlpha === null ? 100 : null,
                    hitPngPath: tw.hitAlpha === null ? tw.hitPngPath : null,
                  })}>
                  <Row label={t('minimap.editor.hitOff', 'Отключить вспышку урона совсем')} on={tw.hitAlpha === 0}
                    onToggle={() => set({ hitAlpha: tw.hitAlpha === 0 ? 75 : 0 })} />
                  {tw.hitPngPath && (
                    <div className="text-[10.5px] text-text-muted">
                      {t('minimap.editor.hitPngNote', 'Свой PNG показывается в родных цветах - красная тонировка игры снимается автоматически.')}
                    </div>
                  )}
                  {isAnimatedPick(tw.hitPngPath) && (
                    <div className="text-[10.5px] text-status-warning">
                      {t('minimap.editor.hitGifNote',
                        'Гифка будет статичной: в игру уходит один кадр - анимацию вспышки игра не умеет.')}
                    </div>
                  )}
                  {tw.hitAlpha !== 0 && (
                  <Num label={t('minimap.editor.hitAlpha', 'Сила')} v={tw.hitAlpha ?? 75} min={1} max={100} step={1} on={v => set({ hitAlpha: Math.round(v) })} suffix="%" />
                  )}
                  {tw.hitAlpha !== 0 && (<>
                  <Num label={t('minimap.editor.hitFade', 'Затухание')} v={tw.hitFadeSeconds ?? 1.5} min={0.1} max={5} step={0.1} on={v => set({ hitFadeSeconds: v })} suffix="с" />
                  <div className="flex items-center gap-2">
                    <button onClick={() => void pickHitPng()}
                      className="inline-flex items-center gap-1.5 text-[10.5px] font-bold uppercase tracking-wider text-text-secondary
                                 border border-white/[0.08] rounded-lg px-2 py-1.5 hover:text-text-primary transition-colors">
                      <ImageIcon size={12} />
                      {tw.hitPngPath
                        ? t('minimap.editor.hitPngChange', 'Сменить PNG/GIF')
                        : t('minimap.editor.hitPngPick', 'Свой PNG/GIF')}
                    </button>
                    {tw.hitPngPath && (
                      <>
                        {hitPngUrl && <img src={hitPngUrl} alt="" className="w-7 h-7 object-contain rounded border border-white/15 bg-black/40" />}
                        <span className="font-mono text-[10px] text-text-muted truncate max-w-[130px]"
                          title={tw.hitPngPath}>
                          {tw.hitPngPath.split(/[\\/]/).pop()}
                        </span>
                        <button onClick={() => set({ hitPngPath: null })}
                          className="text-text-muted hover:text-status-error transition-colors" title={t('minimap.editor.hitPngClear', 'Убрать картинку')}>
                          <X size={13} />
                        </button>
                      </>
                    )}
                  </div>
                  <Num label={t('minimap.editor.hitScale', 'Масштаб')} v={tw.hitScale ?? 100} min={10} max={400} step={5} on={v => set({ hitScale: v })} suffix="%" />
                  <Num label={t('minimap.editor.hitX', 'Вспышка по горизонтали')} v={hitCx} min={MAP.x} max={MAP.x + MAP.w} step={1} suffix=""
                    on={v => set({ hitX: Math.round(v), hitY: Math.round(hitCy) })} />
                  <Num label={t('minimap.editor.hitY', 'Вспышка по вертикали')} v={hitCy} min={0} max={STAGE_H} step={1} suffix=""
                    on={v => set({ hitX: Math.round(hitCx), hitY: Math.round(v) })} />
                  <div className="flex items-center gap-2">
                    <button type="button"
                      onClick={() => set({ hitX: round1(MAP.x + MAP.w / 2), hitY: round1(MAP.y + MAP.h / 2) })}
                      className="h-7 px-2.5 rounded-lg text-[11px] border border-border-subtle
                                 text-text-secondary hover:text-text-primary hover:border-[color:var(--accent)] transition-colors">
                      {t('minimap.editor.hitToCenter', 'В центр карты')}
                    </button>
                    {(tw.hitX !== null || tw.hitY !== null) && (
                      <button type="button" onClick={() => set({ hitX: null, hitY: null })}
                        className="h-7 px-2.5 rounded-lg text-[11px] border border-border-subtle
                                   text-text-secondary hover:text-text-primary transition-colors">
                        {t('minimap.editor.hitAsRedux', 'Как в редуксе')}
                      </button>
                    )}
                    <span className="text-[10px] text-text-muted leading-snug">
                      {t('minimap.editor.hitDragNote', 'Или тяни белый кружок прямо на сцене.')}
                    </span>
                  </div>
                  </>)}
                </Section>

                <Card title={t('minimap.editor.cardRings', 'Круги и компас')}>
                  <Row label={t('minimap.editor.rings', 'Круги 100 и 125 м')} on={ringsOn} onToggle={() => setRingsOn(v => !v)} />
                  <div className="text-[10.5px] text-text-muted">
                    {initialRings.external
                      ? t('minimap.editor.ringsExternal', 'Круги пришли вместе с редуксом. Выключи тумблер, чтобы убрать их при применении.')
                      : t('minimap.editor.ringsNote', 'Рисуются вокруг игрока поверх текущей миникарты. Наносятся при «Применить».')}
                  </div>
                  <Row label={t('minimap.editor.hideNorth', 'Убрать букву N (север)')} on={tw.hideNorth}
                    onToggle={() => set({ hideNorth: !tw.hideNorth })} />
                </Card>

                {MINIMAP_BLIPS_UI && (
                <Card title={t('minimap.editor.cardBlips', 'Стрелка игрока и метка GPS')}>
                  <Col label={t('minimap.editor.blipArrow', 'Стрелка игрока')}>
                    <PngPick path={tw.arrowPngPath} url={arrowUrl} empty={t('minimap.editor.blipOwnImage', 'Своя картинка')}
                      onPick={() => void pickArrowPng()} onClear={() => set({ arrowPngPath: null })} />
                  </Col>
                  <Col label={t('minimap.editor.blipGps', 'Метка GPS (точка маршрута)')}>
                    <PngPick path={tw.gpsPngPath} url={gpsUrl} empty={t('minimap.editor.blipOwnImage', 'Своя картинка')}
                      onPick={() => void pickGpsPng()} onClear={() => set({ gpsPngPath: null })} />
                  </Col>
                  <div className="text-[10.5px] text-text-muted leading-snug">
                    {t('minimap.editor.blipsNote', 'Картинка подменяет спрайт в самом minimap.gfx (radar_centre / radar_waypoint): место и размер остаются родными, PNG вписывается без искажений. Стрелка крутится по курсу игрока - рисуй её носом вверх, лучше с прозрачным фоном.')}
                  </div>
                </Card>
                )}

                {MINIMAP_FONT_UI && (
                <Card title={t('minimap.editor.cardFont', 'Шрифт миникарты')}>
                  <div className="text-[10.5px] text-text-muted">
                    {fontState?.installed
                      ? <Trans
                          i18nKey="minimap.editor.gfxInstalled"
                          defaults="Стоит кастомный: <file>{{name}}</file>"
                          values={{ name: fontState.sourceFile ?? fontState.slot }}
                          components={{ file: <span className="font-mono text-text-secondary" /> }}
                        />
                      : t('minimap.editor.gfxStock', 'Сейчас стоковый шрифт. Файл font_lib_*.gfx (FontLab и т.п.) заменит шрифт цифр и текста.')}
                  </div>
                  <div className="flex items-center gap-2">
                    <button onClick={() => void pickFont()} disabled={fontBusy}
                      className="inline-flex items-center gap-1.5 text-[10.5px] font-bold uppercase tracking-wider text-text-secondary
                                 border border-white/[0.08] rounded-lg px-2 py-1.5 hover:text-text-primary transition-colors disabled:opacity-50">
                      <ImageIcon size={12} />
                      {fontPick
                        ? t('minimap.editor.gfxChange', 'Сменить .gfx')
                        : t('minimap.editor.gfxPick', 'Выбрать .gfx')}
                    </button>
                    {fontPick && (
                      <span className="font-mono text-[10px] text-text-muted truncate max-w-[150px]" title={fontPick}>
                        {fontPick.split(/[\\/]/).pop()}
                      </span>
                    )}
                  </div>
                  {fontPick && (
                    <>
                      <div className="flex items-stretch p-1 rounded-xl bg-bg-surface border border-border-subtle">
                        {([['auto', t('minimap.editor.slotAuto', 'Авто')],
                           ['efigs', t('minimap.editor.slotLatin', 'Латиница')],
                           ['russian', t('minimap.editor.slotCyrillic', 'Кириллица')]] as const).map(([k, l]) => (
                          <button key={k} onClick={() => setFontSlot(k)}
                            className={'flex-1 h-7 rounded-lg text-[11px] font-medium transition-colors ' +
                              (fontSlot === k ? 'bg-accent text-text-on-accent' : 'text-text-secondary hover:text-text-primary')}>
                            {l}
                          </button>
                        ))}
                      </div>
                      <button onClick={() => void applyFont()} disabled={fontBusy}
                        className="inline-flex items-center justify-center gap-1.5 h-9 rounded-lg text-[12px] font-bold
                                   text-black bg-accent hover:brightness-110 disabled:opacity-60 transition-all">
                        {fontBusy ? <Loader2 size={13} className="animate-spin" /> : <Check size={13} strokeWidth={2.6} />}
                        {t('minimap.editor.gfxInstall', 'Поставить шрифт (пара минут)')}
                      </button>
                    </>
                  )}
                  {fontState?.installed && !fontPick && (
                    <button onClick={() => void restoreFont()} disabled={fontBusy}
                      className="inline-flex items-center justify-center gap-1.5 h-8 rounded-lg text-[11.5px] font-medium
                                 text-text-secondary border border-white/[0.08] hover:text-text-primary disabled:opacity-50 transition-colors">
                      {fontBusy ? <Loader2 size={13} className="animate-spin" /> : <RotateCcw size={13} />}
                      {t('minimap.editor.gfxRestore', 'Вернуть стоковый шрифт')}
                    </button>
                  )}
                  {fontMsg && <div className="text-[10.5px] text-text-muted">{fontMsg}</div>}
                </Card>
                )}
              </div>
            </div>
          </div>
          ) : (
          <div className="flex-1 min-h-0 flex">
            <div className="flex-1 min-w-0 flex flex-col items-center justify-center gap-3 p-6 overflow-auto">
              <div className="flex flex-wrap items-center gap-1.5" style={{ width: posStageW }}>
                <span className="text-[10px] uppercase tracking-[0.18em] text-text-muted font-bold mr-1">{t('minimap.editor.screen', 'Экран')}</span>
                <button onClick={() => setScreenPick(null)}
                  disabled={!!(posBg && posBgDim) || !screenAuto}
                  title={screenAuto
                    ? t('minimap.editor.screenAutoTitle', { w: screenReal!.width, h: screenReal!.height, defaultValue: 'Из настроек GTA: {{w}}x{{h}}' })
                    : t('minimap.editor.screenNoSettings', 'settings.xml не прочитан - выбери формат кнопкой')}
                  className={'text-[11px] font-bold tracking-wider border rounded-lg px-2.5 py-1.5 transition-colors disabled:opacity-40 disabled:pointer-events-none ' +
                    (!screenPick && screenAuto
                      ? 'text-accent border-accent/50 bg-accent/10'
                      : 'text-text-secondary border-white/[0.08] hover:text-text-primary')}>
                  {screenAuto
                    ? t('minimap.editor.screenYours', { w: screenReal!.width, h: screenReal!.height, defaultValue: 'Твой {{w}}×{{h}}' })
                    : t('minimap.editor.screenYoursPlain', 'Твой экран')}
                </button>
                {SCREENS.map(s => (
                  <button key={s.label} onClick={() => setScreenPick(s.label)}
                    disabled={!!(posBg && posBgDim)}
                    title={posBg && posBgDim
                      ? t('minimap.editor.screenFromShot', { w: posBgDim.w, h: posBgDim.h, defaultValue: 'Формат взят из скрина {{w}}x{{h}}' })
                      : undefined}
                    className={'text-[11px] font-bold tracking-wider border rounded-lg px-2.5 py-1.5 transition-colors disabled:opacity-40 disabled:pointer-events-none ' +
                      (screenPick === s.label || (!screenPick && !screenAuto && s.ar === 16 / 9)
                        ? 'text-accent border-accent/50 bg-accent/10'
                        : 'text-text-secondary border-white/[0.08] hover:text-text-primary')}>
                    {s.label}
                  </button>
                ))}
                <label className="ml-auto inline-flex items-center gap-1.5 text-[10.5px] font-bold uppercase tracking-wider
                                  text-text-secondary border border-white/[0.08] rounded-lg px-2 py-1.5 cursor-pointer hover:text-text-primary transition-colors">
                  <ImageIcon size={13} />
                  {posBg
                    ? t('minimap.position.changeScreenshot', 'Сменить скрин')
                    : t('minimap.position.addScreenshot', 'Подложить скрин игры')}
                  <input type="file" accept="image/*" className="hidden"
                    onChange={e => {
                      const f = e.target.files?.[0];
                      if (!f) return;
                      if (posBg) URL.revokeObjectURL(posBg);
                      const url = URL.createObjectURL(f);
                      setPosBg(url);
                      setPosBgDim(null);
                      const img = new Image();
                      img.onload = () => setPosBgDim({ w: img.naturalWidth, h: img.naturalHeight });
                      img.src = url;
                    }} />
                </label>
                {posBg && (
                  <button onClick={() => { URL.revokeObjectURL(posBg); setPosBg(null); setPosBgDim(null); }}
                    className="inline-flex items-center gap-1 text-[10.5px] font-bold uppercase tracking-wider text-text-secondary
                               border border-white/[0.08] rounded-lg px-2 py-1.5 hover:text-text-primary transition-colors">
                    <X size={12} /> {t('minimap.editor.shotRemove', 'Убрать')}
                  </button>
                )}
              </div>

              <div
                ref={posStageRef}
                className="relative rounded-2xl overflow-hidden border border-white/[0.12] select-none touch-none shadow-2xl shrink-0"
                style={{
                  width: posStageW, height: posStageH,
                  background: posBg
                    ? `url(${posBg}) center / 100% 100% no-repeat`
                    : 'linear-gradient(180deg,#233047 0%,#2c3a52 40%,#3a4257 55%,#2a2f3d 75%,#1a1e29 100%)',
                }}
                onPointerMove={e => { if (posDragRef.current) posMove(e.clientX, e.clientY); }}
                onPointerUp={() => { posDragRef.current = null; }}
                onPointerLeave={() => { posDragRef.current = null; }}
              >
                {!posBg && (
                  <>
                    <HudZone style={{ left: '1.5%', top: '3%', width: '26%', height: '22%' }} label={t('minimap.position.hudChat', 'чат')} />
                    <HudZone style={{ right: '1.5%', top: '3%', width: '12%', height: '9%' }} label={t('minimap.position.hudOnline', 'онлайн / ID')} />
                    <HudZone style={{ right: '1.5%', bottom: '4%', width: '15%', height: '18%' }} label={t('minimap.position.hudSpeedometer', 'спидометр')} />
                    <HudZone style={{ left: '38%', bottom: '3%', width: '24%', height: '7%' }} label={t('minimap.position.hudHints', 'подсказки')} />
                  </>
                )}

                <div
                  className="absolute cursor-grab active:cursor-grabbing"
                  style={{ left: posLeft, top: posTop, width: posBoxW, height: posBoxH }}
                  onPointerDown={e => {
                    (e.currentTarget.parentElement as HTMLElement).setPointerCapture?.(e.pointerId);
                    const r = e.currentTarget.getBoundingClientRect();
                    posDragRef.current = { dx: e.clientX - r.left, dy: e.clientY - r.top };
                  }}
                >
                  {!transparent && <div className="absolute -inset-[4%] rounded-[3px] bg-black/40 blur-[1px]" aria-hidden />}
                  <div className="absolute inset-0 overflow-hidden">
                    <PreviewMinimap tw={tw} gradFull={gradStops.full} boxW={posBoxW} arrowUrl={arrowUrl} />
                  </div>
                </div>

                <div className="absolute bottom-1.5 right-2 text-[10px] font-mono text-white/50 pointer-events-none">
                  posX {pos.posX.toFixed(4)} · posY {pos.posY.toFixed(4)}
                </div>
              </div>
              <p className="text-[11px] text-text-muted leading-snug" style={{ width: posStageW }}>
                {t('minimap.editor.posHint', 'Вы можете таскать хп бар и любой худ. Позиция в долях экрана, работает на любом разрешении твоего формата. Отсчёт идёт от края безопасной зоны, а не от края экрана - её отступ задаётся справа.')}
              </p>
              {posOutAny && (
                <p className="text-[11px] text-amber-300/90 leading-snug" style={{ width: posStageW }}>
                  {t('minimap.editor.outOfFrame', {
                    edges: [
                      posOut.right ? t('minimap.editor.edgeRight', 'правый край') : null,
                      posOut.left ? t('minimap.editor.edgeLeft', 'левый край') : null,
                      posOut.top ? t('minimap.editor.edgeTop', 'верх') : null,
                      posOut.bottom ? t('minimap.editor.edgeBottom', 'низ') : null,
                    ].filter(Boolean).join(' '),
                    defaultValue: 'Карта не влезает в кадр: в игре её обрежет {{edges}} экрана. Подвинь коробку внутрь.',
                  })}
                </p>
              )}
              {!sizeKnown && (
                <p className="text-[11px] text-amber-300/90 leading-snug" style={{ width: posStageW }}>
                  {t('minimap.editor.sizesNotLoaded', {
                    w: VAN.sizeX, h: VAN.sizeY,
                    defaultValue: 'Размеры раскладок из базы не загрузились: коробка нарисована ванильной ({{w}}×{{h}}), а в игру уйдёт размер из базы - для форматов, кроме 16:9, это разные числа. Позицию лучше настраивать после переоткрытия редактора со связью.',
                  })}
                </p>
              )}
            </div>

            <div className="w-[400px] shrink-0 border-l border-white/[0.07] bg-bg-elevated overflow-y-auto">
              <div className="p-4 flex flex-col gap-3">
                <Card title={t('minimap.editor.cardMapSize', 'Размер миникарты')}>
                  <div className="grid grid-cols-3 gap-1.5">
                    {MINIMAP_ASPECT_RATIOS.map(r => (
                      <button key={r} onClick={() => setRatio(r)}
                        className={'h-9 rounded-lg text-[12.5px] font-medium border transition-colors ' +
                          (ratio === r ? 'bg-accent text-text-on-accent border-accent'
                                       : 'text-text-secondary border-white/[0.08] hover:text-text-primary')}>
                        {r}
                      </button>
                    ))}
                  </div>
                  <div className="text-[10.5px] text-text-muted">
                    {sizeKnown
                      ? t('minimap.editor.sizesFromDb', 'Размеры берутся из проверенных раскладок нашей базы.')
                      : t('minimap.editor.sizesFallback', 'Раскладки из базы не загрузились: показан ванильный размер, в игру уйдёт размер из базы.')}
                  </div>
                </Card>

                <Card title={t('minimap.editor.cardSafezone', 'Безопасная зона')}>
                  <Num label={t('minimap.editor.safeMargin', 'Отступ от края')} v={Math.round(safe / SAFE_STEP)} min={0} max={SAFE_MAX_N} step={1}
                    on={v => { const s = clamp(Math.round(v), 0, SAFE_MAX_N) * SAFE_STEP; setSafe(s); saveSafe(s); }}
                    suffix={t('minimap.editor.safeSuffix', { pct: (safe * 100).toFixed(1), defaultValue: 'делений по 0.5 % экрана (сейчас {{pct}} %)' })} />
                  <div className="text-[10.5px] text-text-muted">
                    {screenAuto
                      ? t('minimap.editor.safeNowWithPx', {
                          pct: (safe * 100).toFixed(1),
                          x: Math.round(safe * screenReal!.width),
                          y: Math.round(safe * screenReal!.height),
                          defaultValue: 'Сейчас {{pct}} % экрана с каждой стороны ({{x}} px по горизонтали и {{y}} px по вертикали на твоём экране). Игра отсчитывает позицию худа от края этой зоны: пока она не совпадает с твоей, превью и игра будут разъезжаться. Если миникарта в игре встала не туда, где нарисована здесь, подвинь этот ползунок и примени заново.',
                        })
                      : t('minimap.editor.safeNow', {
                          pct: (safe * 100).toFixed(1),
                          defaultValue: 'Сейчас {{pct}} % экрана с каждой стороны. Игра отсчитывает позицию худа от края этой зоны: пока она не совпадает с твоей, превью и игра будут разъезжаться. Если миникарта в игре встала не туда, где нарисована здесь, подвинь этот ползунок и примени заново.',
                        })}
                  </div>
                  <div className="text-[10.5px] text-text-muted">
                    {safeRead === 'ok'
                      ? t('minimap.editor.safeProfileOk', { n: safeProfileN, defaultValue: 'В профиле GTA ползунок «Безопасная зона» стоит на {{n}}. В отступ мы это число пока не переводим: у двух тестеров при одном и том же шаге замерены 1.5 % и 5 %, в какую сторону считает игра - проверяем.' })
                      : safeRead === 'fail'
                        ? t('minimap.editor.safeProfileFail', { pct: (safe * 100).toFixed(1), defaultValue: 'Настройку из профиля GTA прочитать не вышло (игра ещё не запускалась или профиль не найден) - отступ стоит на {{pct}} %, это твоя ручная настройка или запасные 5 % (максимум шкалы GTA).' })
                        : t('minimap.editor.safeProfilePending', 'Читаем настройку из профиля GTA…')}
                  </div>
                  <div className="text-[10.5px] text-text-muted">
                    {t('minimap.editor.safeChangeNote', 'Сменишь ползунок «Безопасная зона» в самой GTA - миникарта уедет, раскладку надо будет применить заново: в файл игры пишутся координаты от края зоны.')}
                  </div>
                </Card>

                <Card title={t('minimap.editor.cardQuickPos', 'Быстрые позиции')}>
                  <div className="grid grid-cols-2 gap-1.5">
                    <button onClick={() => { const p = preset(ratio, 'default'); setPos({ posX: p.posX, posY: p.posY }); }}
                      className="h-9 rounded-lg text-[12px] font-medium text-text-secondary border border-white/[0.08] hover:text-text-primary transition-colors">
                      {t('minimap.editor.posDefault', 'Дефолт')}
                    </button>
                    <button onClick={() => {
                      setPos({
                        posX: r4(0.5 - sizeRow.sizeX / 2 - safe),
                        posY: r4(safe - 0.5 + 0.5220588 * sizeRow.sizeY),
                      });
                    }}
                      className="h-9 rounded-lg text-[12px] font-medium text-text-secondary border border-white/[0.08] hover:text-text-primary transition-colors">
                      {t('minimap.editor.posCenter', 'По центру')}
                    </button>
                  </div>
                  <button onClick={() => setPos({ posX: initialLayout.posX ?? VAN.posX, posY: initialLayout.posY ?? VAN.posY })}
                    className="inline-flex items-center justify-center gap-1.5 h-9 rounded-lg text-[12px] font-medium text-text-secondary border border-white/[0.08] hover:text-text-primary transition-colors">
                    <RotateCcw size={13} /> {t('minimap.position.resetCurrent', 'Как сейчас')}
                  </button>
                </Card>

                <Card title={t('minimap.editor.cardBg', 'Фон миникарты')}>
                  <Row label={t('minimap.position.transparentBg', 'Прозрачный фон')} on={transparent} onToggle={() => setTransparent(v => !v)} />
                  <div className="text-[10.5px] text-text-muted">
                    {t('minimap.editor.transparentNote', 'Прозрачный: без подложки, остаются карта с метками и полоски.')}
                  </div>
                </Card>
              </div>
            </div>
          </div>
          )}
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function PreviewMinimap({ tw, gradFull, boxW, arrowUrl }: {
  tw: MinimapTweaks; gradFull: string; boxW: number;
  arrowUrl?: string | null;
}) {
  const hp = tw.barHpGradient ? gradFull : (tw.barHpColor ?? '#34D399');
  const ar = tw.barArmorColor ?? '#60A5FA';
  const kx = tw.barScale / 100;
  const ky = (tw.barScaleY ?? tw.barScale) / 100;
  const vert = tw.barPosition === 'left' || tw.barPosition === 'right';
  const bx = (tw.barPosition === 'left' ? 10 : tw.barPosition === 'right' ? 181 : 6) + tw.barOffsetX;
  const by = (tw.barPosition === 'top' ? 3.5 : vert ? 8 : 126) + tw.barOffsetY;
  const PX = (v: number) => `${(v / STAGE_W) * 100}%`;
  const PY = (v: number) => `${(v / STAGE_H) * 100}%`;
  const f = boxW / STAGE_W;
  const digitFs = Math.max(4, DIGIT_EM * FONT_VIS * digitScale(tw.digitsFont) * (tw.digitsScale / 100) * f);
  const textFs = Math.max(4, DIGIT_EM * FONT_VIS * digitScale(tw.digitsFont) * (tw.customTextScale / 100) * f);
  const bl = useBaselineRatio(fontStyle(tw.digitsFont).fontFamily);
  const digitDrop = inkDropPx(0, digitFs, bl);
  const textDrop = inkDropPx(0, textFs, bl);
  const digit = (x: number, y: number, color: string) => (
    <span className="absolute leading-none drop-shadow-sm"
      style={{ left: PX(x), top: PY(y), marginTop: digitDrop, fontSize: digitFs, ...fontStyle(tw.digitsFont), color }}>100</span>
  );
  return (
    <>
      <div className="absolute overflow-hidden rounded-[2px] border border-white/15"
        style={{
          left: PX(MAP.x), top: PY(MAP.y), width: PX(MAP.w), height: PY(MAP.h),
          background: 'radial-gradient(120% 120% at 30% 25%, #46523c 0%, #38412f 45%, #2a3126 100%)',
        }}>
        <div className="absolute left-[12%] top-[28%] w-[70%] h-[2px] bg-white/25 rotate-[18deg]" />
        <div className="absolute left-[46%] top-[6%] w-[2px] h-[82%] bg-white/20" />
        <div className="absolute left-[6%] top-[62%] w-[82%] h-[2px] bg-white/15 -rotate-[8deg]" />
        {arrowUrl ? (
          <img src={arrowUrl} alt=""
            className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 object-contain"
            style={{ width: Math.max(4, 9 * f), height: Math.max(4, 9 * f) }} />
        ) : (
          <div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 rotate-45 bg-white/90"
            style={{ width: Math.max(3, 5 * f), height: Math.max(3, 5 * f) }} />
        )}
      </div>
      {vert ? (
        <div className="absolute flex flex-col gap-[1px]"
          style={{ left: PX(bx - BAR_UNITS * ky), top: PY(by), width: PX(BAR_UNITS * ky), height: PY(BAR_VERT_UNITS * kx) }}>
          <div className="flex-1 rounded-[1px]" style={{ background: hp }} />
          <div className="flex-1 rounded-[1px]" style={{ background: ar }} />
        </div>
      ) : (
        <>
          <div className="absolute bg-black/50 rounded-[1px]"
            style={{ left: PX(bx - 1), top: PY(by - 1), width: PX(BAR_W * kx + 2), height: PY(BAR_UNITS * ky + 2) }} />
          <div className="absolute flex flex-row gap-[2px]"
            style={{ left: PX(bx), top: PY(by), width: PX(BAR_W * kx), height: PY(BAR_UNITS * ky) }}>
            <div className="flex-1 rounded-[1px]" style={{ background: hp }} />
            <div className="flex-1 rounded-[1px]" style={{ background: ar }} />
          </div>
        </>
      )}
      {tw.digits && digit(tw.digitsX + (tw.digitsHpDx + 2) * tw.digitsScale / 100,
        tw.digitsY + (tw.digitsHpDy + 2) * tw.digitsScale / 100, tw.digitsHpColor ?? '#fff')}
      {tw.digits && digit(tw.digitsX + (tw.digitsArmorDx + 2) * tw.digitsScale / 100,
        tw.digitsY + (tw.digitsArmorDy + 2) * tw.digitsScale / 100, tw.digitsArmorColor ?? '#fff')}
      {tw.customText && (
        <span className="absolute leading-none whitespace-nowrap drop-shadow-sm"
          style={{ left: PX(tw.customTextX + 2 * tw.customTextScale / 100), top: PY(tw.customTextY + 2 * tw.customTextScale / 100), marginTop: textDrop, fontSize: textFs, ...fontStyle(tw.digitsFont), color: tw.customTextColor }}>
          {tw.customText}
        </span>
      )}
    </>
  );
}

function HudZone({ style, label }: { style: React.CSSProperties; label: string }) {
  return (
    <div className="absolute rounded-md border border-dashed border-white/15 bg-white/[0.03] pointer-events-none
                    flex items-end p-1" style={style} aria-hidden>
      <span className="text-[9px] uppercase tracking-wider text-white/30">{label}</span>
    </div>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl bg-bg-surface border border-border-subtle p-3 flex flex-col gap-2.5">
      <span className="text-[11px] uppercase tracking-[0.16em] text-text-muted font-bold">{title}</span>
      {children}
    </div>
  );
}

function Section({ title, on, onToggle, children, disabled, hint }: {
  title: string; on: boolean; onToggle: () => void; children: React.ReactNode;
  disabled?: boolean; hint?: string;
}) {
  return (
    <div className={'rounded-xl bg-bg-surface border border-border-subtle p-3 flex flex-col gap-2.5' +
      (disabled ? ' opacity-55' : '')} title={disabled ? hint : undefined}>
      <div className="flex items-center justify-between gap-3">
        <span className="text-[11px] uppercase tracking-[0.16em] text-text-muted font-bold">{title}</span>
        <Toggle on={on} onToggle={onToggle} disabled={disabled} />
      </div>
      {disabled && hint && <div className="text-[10.5px] text-text-muted">{hint}</div>}
      {on && children}
    </div>
  );
}

function Toggle({ on, onToggle, disabled }: { on: boolean; onToggle: () => void; disabled?: boolean }) {
  return (
    <button onClick={disabled ? undefined : onToggle} disabled={disabled}
      className={'relative w-10 h-[23px] rounded-full border transition-colors shrink-0 ' +
        (on ? 'bg-accent/20 border-accent/50' : 'bg-bg-elevated border-white/[0.08]') +
        (disabled ? ' cursor-not-allowed opacity-60' : '')}>
      <span className={'absolute top-0.5 w-[17px] h-[17px] rounded-full transition-all ' +
        (on ? 'left-[19px] bg-accent' : 'left-0.5 bg-text-muted')} />
    </button>
  );
}

function Row({ label, on, onToggle, disabled, hint }: {
  label: string; on: boolean; onToggle: () => void; disabled?: boolean; hint?: string;
}) {
  return (
    <div className={'flex items-center justify-between gap-3' + (disabled ? ' opacity-55' : '')}
      title={disabled ? hint : undefined}>
      <span className="text-[12.5px] font-semibold text-text-secondary">{label}</span>
      <Toggle on={on} onToggle={onToggle} disabled={disabled} />
    </div>
  );
}

const Two = ({ children }: { children: React.ReactNode }) =>
  <div className="grid grid-cols-2 gap-2">{children}</div>;
const Col = ({ label, children }: { label: string; children: React.ReactNode }) => (
  <div className="flex flex-col gap-1">
    <span className="text-[10px] uppercase tracking-[0.14em] text-text-muted font-bold">{label}</span>
    {children}
  </div>
);

function FontPick({ label, v, opts, on, note }:
  { label: string; v: string | null; opts: { id: string; title: string }[];
    on: (v: string | null) => void; note?: string }) {
  if (opts.length === 0) return null;
  return (
    <Col label={label}>
      <select
        value={v ?? 'stock'}
        onChange={e => on(e.target.value === 'stock' ? null : e.target.value)}
        className="text-[12px] text-text-primary bg-bg-surface border border-border-subtle
                   rounded-lg px-2.5 py-2 outline-none focus:border-[color:var(--accent)]
                   cursor-pointer"
      >
        {opts.map(o => (
          <option key={o.id} value={o.id}>{o.title}</option>
        ))}
      </select>
      {note && <span className="text-[10.5px] leading-snug text-text-muted">{note}</span>}
    </Col>
  );
}

function usePngDataUrl(path: string | null | undefined): string | null {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let alive = true;
    if (!path) { setUrl(null); return; }
    bridge.fileToDataUrl?.(path)
      .then(u => { if (alive) setUrl(u); })
      .catch(() => { if (alive) setUrl(null); });
    return () => { alive = false; };
  }, [path]);
  return url;
}

function PngPick({ path, url, onPick, onClear, empty }: {
  path: string | null | undefined; url: string | null;
  onPick: () => void; onClear: () => void; empty: string;
}) {
  const { t } = useTranslation();
  return (
    <div className="flex items-center gap-2">
      <button onClick={onPick}
        className="inline-flex items-center gap-1.5 text-[10.5px] font-bold uppercase tracking-wider text-text-secondary
                   border border-white/[0.08] rounded-lg px-2 py-1.5 hover:text-text-primary transition-colors">
        <ImageIcon size={12} />
        {path ? t('minimap.editor.pngChange', 'Сменить') : empty}
      </button>
      {path && (
        <>
          {url && <img src={url} alt="" className="w-7 h-7 object-contain rounded border border-white/15 bg-black/40" />}
          <span className="font-mono text-[10px] text-text-muted truncate max-w-[130px]" title={path}>
            {path.split(/[\\/]/).pop()}
          </span>
          <button onClick={onClear}
            className="text-text-muted hover:text-status-error transition-colors" title={t('minimap.editor.pngRestore', 'Вернуть родную')}>
            <X size={13} />
          </button>
        </>
      )}
    </div>
  );
}

function Color({ v, on }: { v: string; on: (v: string) => void }) {
  const safe = /^#[0-9a-fA-F]{6}$/.test(v) ? v.toLowerCase() : '#ffffff';
  return (
    <label className="relative flex items-center gap-2 bg-bg-elevated border border-border-subtle rounded-lg p-1.5 cursor-pointer">
      <span className="w-6 h-6 rounded border border-white/20 shrink-0" style={{ background: safe }} />
      <span className="font-mono text-[11px] text-text-secondary">{safe.toUpperCase()}</span>
      <input type="color" value={safe} onChange={e => on(e.target.value)}
        className="absolute inset-0 w-full h-full opacity-0 cursor-pointer" />
    </label>
  );
}

function Num({ label, v, min, max, step, on, suffix }:
  { label: string; v: number; min: number; max: number; step: number; on: (v: number) => void; suffix: string }) {
  const clampN = (x: number) => Math.min(max, Math.max(min, Number.isFinite(x) ? x : 0));
  const [draft, setDraft] = useState<string | null>(null);
  const commit = () => {
    if (draft === null) return;
    const n = parseFloat(draft.replace(',', '.'));
    if (Number.isFinite(n)) on(clampN(n));
    setDraft(null);
  };
  return (
    <div className="grid grid-cols-[110px_1fr_58px] items-center gap-2">
      <span className="text-[11.5px] text-text-secondary">{label}</span>
      <input type="range" min={min} max={max} step={step} value={v}
        onChange={e => { setDraft(null); on(clampN(parseFloat(e.target.value))); }}
        style={{ accentColor: 'var(--accent)' }} className="w-full cursor-pointer" />
      <input type="text" inputMode="decimal" value={draft ?? String(v)}
        onChange={e => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={e => {
          if (e.key === 'Enter') { commit(); (e.target as HTMLInputElement).blur(); }
          if (e.key === 'Escape') setDraft(null);
        }}
        className="font-mono text-[11.5px] text-text-primary bg-bg-elevated border border-border-subtle rounded px-1 py-1 text-center outline-none focus:border-[color:var(--accent)]"
        title={suffix} />
    </div>
  );
}
