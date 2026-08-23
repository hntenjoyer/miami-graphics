import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Check, Image as ImageIcon, Loader2, Move, RotateCcw, X } from 'lucide-react';

const VAN = { posX: -0.0045, posY: 0.002, sizeX: 0.150, sizeY: 0.188888 };

const RESOLUTIONS: [number, number][] = [
  [1920, 1080], [2560, 1440], [1366, 768], [1600, 900], [1280, 1024], [3440, 1440],
];

const POS_PRESETS: { labelKey: string; labelDefault: string; posX: number; posY: number }[] = [
  { labelKey: 'minimap.position.presetVanilla',      labelDefault: 'Ваниль',    posX: VAN.posX, posY: VAN.posY },
  { labelKey: 'minimap.position.presetBottomCenter', labelDefault: 'Центр-низ', posX: 0.4109,   posY: -0.10 },
  { labelKey: 'minimap.position.presetBottomRight',  labelDefault: 'Право-низ', posX: 0.8545,   posY: VAN.posY },
];

export function MinimapPositionEditor({ open, initial, busy, onClose, onApply }: {
  open: boolean;
  initial: { posX: number | null; posY: number | null; transparent: boolean };
  busy: boolean;
  onClose: () => void;
  onApply: (posX: number, posY: number, transparent: boolean) => void;
}) {
  const { t } = useTranslation();
  const [res, setRes] = useState<[number, number]>([1920, 1080]);
  const [pos, setPos] = useState<{ posX: number; posY: number }>({ posX: VAN.posX, posY: VAN.posY });
  const [transparent, setTransparent] = useState(false);
  const [bgUrl, setBgUrl] = useState<string | null>(null);
  const stageRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{ dx: number; dy: number } | null>(null);

  useEffect(() => {
    if (!open) return;
    setPos({ posX: initial.posX ?? VAN.posX, posY: initial.posY ?? VAN.posY });
    setTransparent(initial.transparent);
  }, [open, initial.posX, initial.posY, initial.transparent]);

  useEffect(() => () => { if (bgUrl) URL.revokeObjectURL(bgUrl); }, [bgUrl]);

  const stageW = 640;
  const aspect = res[0] / res[1];
  const stageH = Math.round(stageW / aspect);
  const boxW = VAN.sizeX * stageW;
  const boxH = VAN.sizeY * stageH;

  const left = pos.posX * stageW;
  const top = stageH - boxH + pos.posY * stageH;

  const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, v));

  const moveTo = (clientX: number, clientY: number) => {
    const st = stageRef.current;
    const d = dragRef.current;
    if (!st || !d) return;
    const r = st.getBoundingClientRect();
    const l = clamp(clientX - r.left - d.dx, -0.02 * stageW, stageW - boxW + 0.02 * stageW);
    const t = clamp(clientY - r.top - d.dy, 0, stageH - boxH + 0.02 * stageH);
    setPos({
      posX: Math.round((l / stageW) * 10000) / 10000,
      posY: Math.round(((t - stageH + boxH) / stageH) * 10000) / 10000,
    });
  };

  const label = useMemo(() => `${res[0]}×${res[1]}`, [res]);

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm p-4"
          onMouseDown={e => { if (e.target === e.currentTarget && !busy) onClose(); }}
        >
          <motion.div
            initial={{ opacity: 0, scale: 0.96, y: 10 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96, y: 10 }}
            className="w-full max-w-[720px] rounded-2xl border border-white/[0.1] bg-bg-elevated shadow-2xl overflow-hidden"
          >
            <div className="flex items-center gap-2.5 px-4 h-12 border-b border-white/[0.06]">
              <Move size={15} className="text-accent" />
              <h3 className="text-[12.5px] font-bold uppercase tracking-[0.13em] text-text-primary">
                {t('minimap.position.title', 'Позиция миникарты - вручную')}
              </h3>
              <span className="ml-auto text-[10.5px] text-text-muted">{label}</span>
              <button onClick={() => !busy && onClose()} className="text-text-muted hover:text-text-primary transition-colors">
                <X size={16} />
              </button>
            </div>

            <div className="p-4 flex flex-col gap-3">
              <div className="flex flex-wrap items-center gap-1.5">
                {RESOLUTIONS.map(([w, h]) => (
                  <button key={`${w}x${h}`} onClick={() => setRes([w, h])}
                    className={'text-[10.5px] font-bold tracking-wider border rounded-lg px-2 py-1.5 transition-colors ' +
                      (res[0] === w && res[1] === h
                        ? 'text-accent border-accent/50 bg-accent/10'
                        : 'text-text-secondary border-white/[0.08] hover:text-text-primary')}>
                    {w}×{h}
                  </button>
                ))}
                <label className="ml-auto inline-flex items-center gap-1.5 text-[10.5px] font-bold uppercase tracking-wider
                                  text-text-secondary border border-white/[0.08] rounded-lg px-2 py-1.5 cursor-pointer hover:text-text-primary transition-colors">
                  <ImageIcon size={13} />
                  {bgUrl
                    ? t('minimap.position.changeScreenshot', 'Сменить скрин')
                    : t('minimap.position.addScreenshot', 'Подложить скрин игры')}
                  <input type="file" accept="image/*" className="hidden"
                    onChange={e => {
                      const f = e.target.files?.[0];
                      if (f) { if (bgUrl) URL.revokeObjectURL(bgUrl); setBgUrl(URL.createObjectURL(f)); }
                    }} />
                </label>
              </div>

              <div
                ref={stageRef}
                className="relative mx-auto rounded-xl overflow-hidden border border-white/[0.12] select-none touch-none"
                style={{
                  width: stageW, height: stageH,
                  background: bgUrl
                    ? `url(${bgUrl}) center / cover no-repeat`
                    : 'linear-gradient(180deg,#233047 0%,#2c3a52 40%,#3a4257 55%,#2a2f3d 75%,#1a1e29 100%)',
                }}
                onPointerMove={e => { if (dragRef.current) moveTo(e.clientX, e.clientY); }}
                onPointerUp={() => { dragRef.current = null; }}
                onPointerLeave={() => { dragRef.current = null; }}
              >
                {!bgUrl && (
                  <>
                    <HudZone style={{ left: '1.5%', top: '3%', width: '26%', height: '22%' }} label={t('minimap.position.hudChat', 'чат')} />
                    <HudZone style={{ right: '1.5%', top: '3%', width: '12%', height: '9%' }} label={t('minimap.position.hudOnline', 'онлайн / ID')} />
                    <HudZone style={{ right: '1.5%', bottom: '4%', width: '15%', height: '18%' }} label={t('minimap.position.hudSpeedometer', 'спидометр')} />
                    <HudZone style={{ left: '38%', bottom: '3%', width: '24%', height: '7%' }} label={t('minimap.position.hudHints', 'подсказки')} />
                  </>
                )}

                <div
                  className="absolute cursor-grab active:cursor-grabbing"
                  style={{ left, top, width: boxW, height: boxH }}
                  onPointerDown={e => {
                    (e.currentTarget.parentElement as HTMLElement).setPointerCapture?.(e.pointerId);
                    const r = e.currentTarget.getBoundingClientRect();
                    dragRef.current = { dx: e.clientX - r.left, dy: e.clientY - r.top };
                  }}
                >
                  {!transparent && (
                    <div className="absolute -inset-[6%] rounded-md bg-black/45 blur-[2px]" aria-hidden />
                  )}
                  <div className="absolute inset-x-[8%] top-[10%] bottom-[20%] rounded-sm overflow-hidden border border-white/20"
                    style={{ background: 'radial-gradient(120% 120% at 30% 30%, #4c5a3f 0%, #37432f 45%, #2b3328 100%)' }}>
                    <div className="absolute left-1/2 top-1/2 w-2 h-2 -translate-x-1/2 -translate-y-1/2 rotate-45 bg-white/90" />
                    <div className="absolute left-[15%] top-[20%] w-[60%] h-[3px] bg-white/25 rotate-[24deg]" />
                    <div className="absolute left-[5%] top-[55%] w-[80%] h-[3px] bg-white/20 -rotate-[10deg]" />
                  </div>
                  <div className="absolute inset-x-[8%] bottom-[10%] h-[8%] flex gap-[2%]">
                    <div className="flex-1 rounded-sm bg-emerald-400/90" />
                    <div className="flex-1 rounded-sm bg-sky-400/90" />
                  </div>
                </div>

                <div className="absolute bottom-1.5 right-2 text-[9.5px] font-mono text-white/50 pointer-events-none">
                  posX {pos.posX.toFixed(4)} · posY {pos.posY.toFixed(4)}
                </div>
              </div>

              <p className="text-[10.5px] text-text-muted leading-snug">
                {t('minimap.position.note', 'Зоны худа - примерные (реальный худ Majestic подключим позже). Можно подложить свой скриншот игры и выставить позицию по нему. Позиция задаётся в долях экрана - работает на любом разрешении твоего формата.')}
              </p>

              <div className="flex flex-wrap items-center gap-1.5">
                {POS_PRESETS.map(p => (
                  <button key={p.labelKey} onClick={() => setPos({ posX: p.posX, posY: p.posY })}
                    className="text-[10.5px] font-bold uppercase tracking-wider text-text-secondary border border-white/[0.08]
                               rounded-lg px-2 py-1.5 hover:text-text-primary transition-colors">
                    {t(p.labelKey, p.labelDefault)}
                  </button>
                ))}
                <button onClick={() => setPos({ posX: initial.posX ?? VAN.posX, posY: initial.posY ?? VAN.posY })}
                  className="inline-flex items-center gap-1 text-[10.5px] font-bold uppercase tracking-wider text-text-secondary
                             border border-white/[0.08] rounded-lg px-2 py-1.5 hover:text-text-primary transition-colors">
                  <RotateCcw size={12} /> {t('minimap.position.resetCurrent', 'Как сейчас')}
                </button>
                <label className="ml-auto inline-flex items-center gap-2 text-[11px] text-text-secondary cursor-pointer">
                  <input type="checkbox" checked={transparent} onChange={e => setTransparent(e.target.checked)}
                    style={{ accentColor: 'var(--accent)' }} />
                  {t('minimap.position.transparentBg', 'Прозрачный фон')}
                </label>
              </div>

              <div className="flex gap-2">
                <button onClick={() => !busy && onClose()} disabled={busy}
                  className="flex-1 text-[12px] font-bold uppercase tracking-wider text-text-secondary bg-white/[0.04]
                             border border-white/[0.08] rounded-xl py-2.5 hover:text-text-primary transition-colors disabled:opacity-50">
                  {t('common.cancel', 'Отмена')}
                </button>
                <button onClick={() => onApply(pos.posX, pos.posY, transparent)} disabled={busy}
                  className="flex-1 inline-flex items-center justify-center gap-2 text-[12px] font-extrabold uppercase tracking-wider
                             text-black bg-accent rounded-xl py-2.5 hover:brightness-110 transition disabled:opacity-60">
                  {busy ? <Loader2 size={14} className="animate-spin" /> : <Check size={14} strokeWidth={2.6} />}
                  {t('common.apply', 'Применить')}
                </button>
              </div>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function HudZone({ style, label }: { style: React.CSSProperties; label: string }) {
  return (
    <div className="absolute rounded-md border border-dashed border-white/15 bg-white/[0.03] pointer-events-none
                    flex items-end p-1" style={style} aria-hidden>
      <span className="text-[8.5px] uppercase tracking-wider text-white/30">{label}</span>
    </div>
  );
}
