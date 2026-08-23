import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Trash2, RotateCcw, Wand2, TriangleAlert, Crosshair } from 'lucide-react';
import { bridge } from '@/bridge';
import {
  STUDIO_GUNS, CHANNEL_SIDE_EFFECTS, ALLOC_ORDER,
  allocateChannels, decodeGunConfigs, encodeGunConfigs, defaultLook,
  type GunLook, type StudioGunConfig,
} from './tracerStudioCodec';

export function TracerStudioPanel() {
  const { t } = useTranslation();
  const [guns, setGuns] = useState<StudioGunConfig[]>([]);
  const [sel, setSel] = useState(0);
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [wasEnabled, setWasEnabled] = useState(false);

  useEffect(() => {
    let dead = false;
    bridge.otherGetTracerStudio().then(st => {
      if (dead) return;
      const gs = decodeGunConfigs(st.settings);
      if (gs.length) setGuns(gs);
      setWasEnabled(st.enabled);
      setLoaded(true);
    }).catch(() => setLoaded(true));
    return () => { dead = true; };
  }, []);

  const cur = guns[sel] as StudioGunConfig | undefined;
  const alloc = allocateChannels(guns);
  const curChannel = cur ? alloc.gunChannel.get(cur.weapon) : undefined;
  const distinctLooks = alloc.channelLook.size;

  const patchLook = (next: Partial<GunLook>) =>
    setGuns(prev => prev.map((g, i) => i === sel ? { ...g, look: { ...g.look, ...next } } : g));
  const patchGun = (next: Partial<StudioGunConfig>) =>
    setGuns(prev => prev.map((g, i) => i === sel ? { ...g, ...next } : g));

  const addGun = () => {
    const used = new Set(guns.map(g => g.weapon));
    const free = STUDIO_GUNS.find(g => !used.has(g.id));
    if (!free) return;
    setGuns([...guns, { weapon: free.id, chance: 1, look: defaultLook() }]);
    setSel(guns.length);
  };

  const encoded = encodeGunConfigs(guns);
  const isEmpty = encoded === '';

  const apply = async () => {
    setBusy(true); setNote(null);
    try {
      const res = await bridge.otherSetTracerStudio(encoded);
      if (!res.success) setNote(res.errorMessage ?? t('customize.tracerStudio.failed', 'Не применилось.'));
      else {
        setWasEnabled(!isEmpty);
        if (res.errorMessage) setNote(res.errorMessage);
      }
    } catch (e) { setNote(String(e)); }
    finally { setBusy(false); }
  };

  const resetAll = async () => {
    setBusy(true); setNote(null);
    try {
      const res = await bridge.otherSetTracerStudio('');
      if (!res.success) setNote(res.errorMessage ?? t('customize.tracerStudio.failed', 'Не применилось.'));
      else { setGuns([]); setSel(0); setWasEnabled(false); }
    } catch (e) { setNote(String(e)); }
    finally { setBusy(false); }
  };

  return (
    <div className="w-full max-w-[1000px] flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        {guns.map((g, i) => {
          const label = STUDIO_GUNS.find(x => x.id === g.weapon)?.label ?? g.weapon;
          const over = alloc.overflow.includes(g.weapon);
          return (
            <button
              key={g.weapon}
              type="button"
              onClick={() => setSel(i)}
              className={
                'px-3.5 py-2 rounded-xl text-sm border transition-colors inline-flex items-center gap-2 ' +
                (i === sel
                  ? 'border-accent bg-bg-elevated text-text-primary shadow-glow-accent'
                  : 'border-border-subtle bg-bg-surface text-text-secondary hover:border-accent/50')
              }
            >
              <span
                className="w-2.5 h-2.5 rounded-full border border-black/30"
                style={{ background: over ? '#f87171' : `#${g.look.gradient[1]}` }}
              />
              {label}
            </button>
          );
        })}
        <button
          type="button"
          onClick={addGun}
          className="px-3.5 py-2 rounded-xl text-sm border border-dashed border-border-subtle text-accent hover:border-accent/60 inline-flex items-center gap-1.5"
        >
          <Plus size={14} />
          {t('customize.tracerStudio.addGun', 'ствол')}
        </button>
        <div className="flex-1" />
        <span className="text-[11px] text-text-muted">
          {t('customize.tracerStudio.looksUsed', 'видов')}: {distinctLooks}/{ALLOC_ORDER.length}
        </span>
      </div>

      {guns.length === 0 && (
        <div className="rounded-2xl bg-bg-surface border border-border-subtle p-8 text-center text-sm text-text-muted">
          <Crosshair size={22} className="mx-auto mb-2 opacity-50" />
          {t('customize.tracerStudio.empty', 'Добавь ствол - и настрой ему собственный трейсер.')}
        </div>
      )}

      {cur && (
        <>
          <StudioPreview look={cur.look} />

          {alloc.overflow.includes(cur.weapon) && (
            <div className="flex items-start gap-2 text-[12px] text-red-400 leading-snug">
              <TriangleAlert size={13} className="shrink-0 mt-[1px]" />
              <span>
                {t('customize.tracerStudio.overflow',
                  'Разных видов уже 4 - это потолок движка. Сделай вид как у другого ствола или упрости один из них.')}
              </span>
            </div>
          )}

          <div className="rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-6">
            <div className="flex flex-col gap-3">
              <div className="flex items-center gap-3">
                <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">
                  {t('customize.tracerStudio.gradient', 'Цвет по полёту')}
                </span>
                <div
                  className="flex-1 h-2.5 rounded-full border border-border-subtle"
                  style={{ background: `linear-gradient(90deg, #${cur.look.gradient[0]}, #${cur.look.gradient[1]} 30%, #${cur.look.gradient[2]})` }}
                />
                <button
                  type="button"
                  className="text-[11px] text-text-muted hover:text-text-primary inline-flex items-center gap-1"
                  onClick={() => {
                    const c = cur.look.gradient[1];
                    patchLook({ gradient: [c, c, c] });
                  }}
                >
                  <Wand2 size={11} />
                  {t('customize.tracerStudio.solid', 'одним цветом')}
                </button>
              </div>
              {([0, 1, 2] as const).map(i => (
                <ColorStopRow
                  key={i}
                  label={i === 0
                    ? t('customize.tracerStudio.stopStart', 'Вылет')
                    : i === 1
                      ? t('customize.tracerStudio.stopPeak', 'Полёт')
                      : t('customize.tracerStudio.stopTail', 'Затухание')}
                  value={cur.look.gradient[i]}
                  onChange={hex => {
                    const g = [...cur.look.gradient] as [string, string, string];
                    g[i] = hex;
                    patchLook({ gradient: g });
                  }}
                />
              ))}
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
              <MultSlider
                label={t('customize.tracerStudio.thickness', 'Толщина')}
                value={cur.look.thickness} min={0.25} max={4}
                onChange={v => patchLook({ thickness: v })}
              />
              <MultSlider
                label={t('customize.tracerStudio.length', 'Длина')}
                value={cur.look.length} min={0.25} max={20}
                onChange={v => patchLook({ length: v })}
              />
            </div>

            <div className="flex flex-wrap items-center gap-x-8 gap-y-4">
              <div className="flex items-center gap-2.5">
                <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">
                  {t('customize.tracerStudio.smoke', 'Дым')}
                </span>
                {cur.look.smoke ? (
                  <>
                    <input
                      type="color"
                      value={'#' + cur.look.smoke}
                      onChange={e => patchLook({ smoke: e.target.value.slice(1).toUpperCase() })}
                      className="w-8 h-8 rounded-lg cursor-pointer border-0 bg-transparent"
                    />
                    <button type="button" className="text-[11px] text-text-muted hover:text-text-primary"
                      onClick={() => patchLook({ smoke: null })}>
                      {t('customize.tracerStudio.smokeOff', 'убрать')}
                    </button>
                  </>
                ) : (
                  <button type="button" className="text-[11px] text-accent hover:underline"
                    onClick={() => patchLook({ smoke: 'D4D4D4' })}>
                    {t('customize.tracerStudio.smokeOn', 'покрасить')}
                  </button>
                )}
              </div>

              <label className="flex items-center gap-2 text-[11px] text-text-muted">
                {t('customize.tracerStudio.chance', 'частота трейсера')}
                <input
                  type="range" min={5} max={100} step={5}
                  value={Math.round(cur.chance * 100)}
                  onChange={e => patchGun({ chance: parseInt(e.target.value, 10) / 100 })}
                  className="w-28"
                />
                <span className="w-9 font-mono text-right text-text-primary">{Math.round(cur.chance * 100)}%</span>
              </label>

              <div className="flex-1" />

              <button
                type="button"
                className="text-[11px] text-text-muted hover:text-text-primary inline-flex items-center gap-1"
                onClick={() => patchLook({ gradient: ['FFFFFF', 'FFFFFF', 'FFFFFF'], thickness: 1, length: 1, smoke: null })}
              >
                <RotateCcw size={11} />
                {t('customize.tracerStudio.resetGun', 'сбросить вид')}
              </button>
              <button
                type="button"
                onClick={() => {
                  setGuns(guns.filter((_, i) => i !== sel));
                  setSel(Math.max(0, sel - 1));
                }}
                className="w-8 h-8 rounded-lg flex items-center justify-center text-text-muted hover:text-red-400 hover:bg-bg-elevated"
                aria-label={t('customize.tracerStudio.removeGun', 'убрать ствол')}
              >
                <Trash2 size={14} />
              </button>
            </div>

            {curChannel !== undefined && curChannel !== '' && CHANNEL_SIDE_EFFECTS[curChannel] && (
              <div className="flex items-start gap-2 text-[11px] text-text-muted leading-snug">
                <TriangleAlert size={12} className="shrink-0 mt-[1px] text-amber-400/70" />
                <span>{CHANNEL_SIDE_EFFECTS[curChannel]}</span>
              </div>
            )}
            {curChannel === '' && (
              <div className="text-[11px] text-text-muted leading-snug">
                {t('customize.tracerStudio.vanillaLook',
                  'Вид не менялся - у ствола останется родной трейсер, поменяется только частота.')}
              </div>
            )}
          </div>
        </>
      )}

      {note && <div className="text-[12px] text-amber-400/90 leading-snug">{note}</div>}

      <div className="flex items-center gap-3">
        <button
          type="button"
          disabled={busy || !loaded || alloc.overflow.length > 0 || (isEmpty && !wasEnabled)}
          onClick={apply}
          className="px-5 py-2.5 rounded-xl bg-accent text-text-on-accent text-sm font-medium disabled:opacity-40 hover:brightness-110 transition"
        >
          {busy
            ? t('customize.tracerStudio.applying', 'Применяю...')
            : t('customize.tracerStudio.apply', 'Применить в игру')}
        </button>
        <button
          type="button"
          disabled={busy || !wasEnabled}
          onClick={resetAll}
          className="px-5 py-2.5 rounded-xl border border-border-subtle text-sm text-text-secondary disabled:opacity-40 hover:text-text-primary hover:border-accent/50 transition"
        >
          {t('customize.tracerStudio.resetAll', 'Вернуть как было')}
        </button>
        <span className="text-[11px] text-text-muted">
          {t('customize.tracerStudio.closeGame', 'Перед применением закрой игру.')}
        </span>
      </div>
    </div>
  );
}

const SWATCHES = [
  'FFFFFF', 'FFD66B', 'FF8A2B', 'FF4D4D', 'F472B6', 'C084FC',
  '7C5CFF', '60A5FA', '22D3EE', '34D399', 'A3E635', 'FBBF24',
];

function ColorStopRow({ label, value, onChange }: {
  label: string; value: string; onChange: (hex: string) => void;
}) {
  const [text, setText] = useState(value);
  useEffect(() => setText(value), [value]);

  const commit = (raw: string) => {
    const h = raw.trim().replace(/^#/, '').toUpperCase();
    if (/^[0-9A-F]{6}$/.test(h)) onChange(h);
    else setText(value);
  };

  return (
    <div className="flex flex-wrap items-center gap-2.5">
      <span className="w-[86px] text-[11px] text-text-muted">{label}</span>
      <input
        type="color"
        value={'#' + value}
        onChange={e => onChange(e.target.value.slice(1).toUpperCase())}
        className="w-9 h-9 rounded-lg cursor-pointer border-0 bg-transparent"
        aria-label={label}
      />
      <input
        type="text"
        value={text}
        onChange={e => setText(e.target.value)}
        onBlur={e => commit(e.target.value)}
        onKeyDown={e => { if (e.key === 'Enter') commit((e.target as HTMLInputElement).value); }}
        className="w-[84px] px-2 py-1.5 bg-bg-elevated border border-border-subtle rounded-lg text-text-primary font-mono text-xs outline-none focus:border-accent"
        spellCheck={false}
      />
      <div className="flex gap-1.5">
        {SWATCHES.map(s => (
          <button
            key={s}
            type="button"
            onClick={() => onChange(s)}
            className={
              'w-6 h-6 rounded-md border transition-transform hover:scale-110 ' +
              (s === value ? 'border-accent ring-1 ring-accent' : 'border-border-subtle')
            }
            style={{ background: '#' + s }}
            aria-label={s}
          />
        ))}
      </div>
    </div>
  );
}

function StudioPreview({ look }: { look: GunLook }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const stateRef = useRef(look);
  stateRef.current = look;

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const W = canvas.width, H = canvas.height;

    interface Shot { t: number }
    interface Puff { x: number; y: number; t: number }
    const shots: Shot[] = [];
    const puffs: Puff[] = [];
    let last = 0, spawn = 0.5, raf = 0;

    const hex = (h: string): [number, number, number] => [
      parseInt(h.slice(0, 2), 16) || 0,
      parseInt(h.slice(2, 4), 16) || 0,
      parseInt(h.slice(4, 6), 16) || 0,
    ];

    const draw = (ts: number) => {
      const lk = stateRef.current;
      const dt = Math.min(0.05, (ts - last) / 1000 || 0);
      last = ts;
      spawn += dt;
      if (spawn > 0.55) { spawn = 0; shots.push({ t: 0 }); }

      ctx.fillStyle = '#0b0e12';
      ctx.fillRect(0, 0, W, H);
      ctx.fillStyle = '#141920';
      ctx.fillRect(0, H * 0.78, W, H * 0.22);
      ctx.fillStyle = '#10151b';
      for (let i = 0; i < 6; i++) ctx.fillRect(60 + i * 170, H * 0.3, 110, H * 0.48);

      const cols = lk.gradient.map(hex);
      const muzX = 70, muzY = H * 0.55;
      const thick = 5 * Math.sqrt(Math.max(0.25, lk.thickness));
      const lenPx = Math.min(W * 0.72, 160 * Math.sqrt(Math.max(0.25, lk.length)));
      const life = 0.55;

      ctx.globalCompositeOperation = 'source-over';
      if (lk.smoke) {
        const sc = hex(lk.smoke);
        for (let q = puffs.length - 1; q >= 0; q--) {
          const p = puffs[q]; p.t += dt;
          const pp = p.t / 1.0;
          if (pp > 1) { puffs.splice(q, 1); continue; }
          const a = pp < 0.08 ? pp / 0.08 * 0.45 : 0.45 * (1 - (pp - 0.08) / 0.92);
          ctx.globalAlpha = Math.max(0, a * 0.6);
          ctx.fillStyle = `rgb(${sc[0]},${sc[1]},${sc[2]})`;
          ctx.beginPath();
          ctx.arc(p.x, p.y - pp * 9, 5 + pp * 22, 0, 6.3);
          ctx.fill();
        }
      } else {
        puffs.length = 0;
      }

      ctx.globalCompositeOperation = 'lighter';
      for (let k = shots.length - 1; k >= 0; k--) {
        const s = shots[k]; s.t += dt;
        const p = s.t / life;
        if (p > 1) { shots.splice(k, 1); continue; }
        const head = muzX + p * (W - muzX - 40);
        const tail = Math.max(muzX, head - lenPx);
        if (lk.smoke && Math.random() < 0.5) {
          puffs.push({ x: tail + Math.random() * 30, y: muzY + (Math.random() - 0.5) * 6, t: 0 });
        }
        const g = ctx.createLinearGradient(tail, 0, head, 0);
        for (let n = 0; n <= 8; n++) {
          const lp = n / 8;
          const phase = Math.max(0, Math.min(1, p - (1 - lp) * 0.35));
          const idxF = phase <= 0.25 ? phase / 0.25 : 1 + (phase - 0.25) / 0.75;
          const i0 = Math.min(1, Math.floor(idxF));
          const frac = idxF - i0;
          const c0 = cols[i0], c1 = cols[Math.min(2, i0 + 1)];
          const r = Math.round(c0[0] + (c1[0] - c0[0]) * frac);
          const gg = Math.round(c0[1] + (c1[1] - c0[1]) * frac);
          const b = Math.round(c0[2] + (c1[2] - c0[2]) * frac);
          const alpha = 0.15 + 0.85 * Math.sin(Math.min(1, lp + 0.15) * Math.PI * 0.5);
          g.addColorStop(lp, `rgba(${r},${gg},${b},${alpha.toFixed(3)})`);
        }
        ctx.globalAlpha = 1;
        ctx.fillStyle = g;
        ctx.fillRect(tail, muzY - thick / 2, head - tail, thick);
        ctx.globalAlpha = 0.3;
        ctx.fillRect(tail, muzY - thick * 1.4, head - tail, thick * 2.8);
        if (p < 0.1) {
          ctx.globalAlpha = (1 - p / 0.1) * 0.8;
          ctx.fillStyle = 'rgba(255,238,200,1)';
          ctx.beginPath();
          ctx.arc(muzX, muzY, 10 + 16 * (1 - p / 0.1), 0, 6.3);
          ctx.fill();
        }
      }
      ctx.globalAlpha = 1;
      ctx.globalCompositeOperation = 'source-over';
      raf = requestAnimationFrame(draw);
    };
    raf = requestAnimationFrame(draw);
    return () => cancelAnimationFrame(raf);
  }, []);

  return (
    <canvas
      ref={canvasRef}
      width={1000}
      height={230}
      className="w-full rounded-2xl border border-border-subtle"
      style={{ background: '#0b0e12' }}
    />
  );
}

function MultSlider({ label, value, min, max, onChange }: {
  label: string; value: number; min: number; max: number; onChange: (v: number) => void;
}) {
  const toPos = (v: number) => {
    const lmin = Math.log(min), lmax = Math.log(max);
    return Math.round(((Math.log(Math.max(min, Math.min(max, v))) - lmin) / (lmax - lmin)) * 1000);
  };
  const fromPos = (p: number) => {
    const lmin = Math.log(min), lmax = Math.log(max);
    const v = Math.exp(lmin + (p / 1000) * (lmax - lmin));
    return Math.abs(v - 1) < 0.06 ? 1 : Math.round(v * 100) / 100;
  };
  return (
    <label className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between text-xs">
        <span className="text-text-muted uppercase tracking-wider">{label}</span>
        <span className={'font-mono ' + (Math.abs(value - 1) < 0.001 ? 'text-text-muted' : 'text-text-primary')}>
          ×{value}
        </span>
      </div>
      <input
        type="range" min={0} max={1000}
        value={toPos(value)}
        onChange={e => onChange(fromPos(parseInt(e.target.value, 10)))}
        className="w-full"
      />
    </label>
  );
}
