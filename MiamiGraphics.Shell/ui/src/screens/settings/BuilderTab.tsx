import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Loader2, AlertTriangle, Zap, ChevronDown } from 'lucide-react';
import { EASE_DEPTH } from '@/design';
import { bridge } from '@/bridge';
import type {
  OptimizationCatalog, OptimizationGroup, OptimizationSelection,
  OptimizationScanProgress, OptimizationInteriorState,
} from '@/bridge/types';
import { Toast, type ToastTone } from '@/components/Toast';

type Selections = Record<string, number | null>;

const STRIP_GAP = 10;

export function BuilderTab() {
  const [catalog, setCatalog]   = useState<OptimizationCatalog | null>(null);
  const [applied, setApplied]   = useState<Selections>({});
  const [markers, setMarkers]   = useState<Record<string, string>>({});
  const [scan, setScan]         = useState<OptimizationScanProgress | null>(null);
  const [draft, setDraft]       = useState<Selections>({});
  const [loading, setLoading]   = useState(true);
  const [error, setError]       = useState<string | null>(null);
  const [applying, setApplying] = useState(false);
  const [focusKey, setFocusKey] = useState<string | null>(null);
  const [openKey, setOpenKey]   = useState<string | null>(null);
  const [hover, setHover]       = useState<{ groupKey: string; idx: number } | null>(null);
  const lastPreview             = useRef<string | null>(null);
  const stripRef                = useRef<HTMLDivElement | null>(null);
  const [floorH, setFloorH]     = useState<number | null>(null);
  const [floors, setFloors]     = useState(1);
  const [floor, setFloor]       = useState(0);
  const [toast, setToast] = useState<{ open: boolean; tone: ToastTone; message: string }>({
    open: false, tone: 'success', message: '',
  });

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [cat, state] = await Promise.all([
        bridge.optimizationCatalogGet(),
        bridge.optimizationStateGet(),
      ]);
      setCatalog(cat);
      setApplied(state.selections);
      setDraft(state.selections);
      setMarkers(state.markers ?? {});
      setFocusKey(cat.groups[0]?.key ?? null);
    } catch (e) {
      setCatalog(null);
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    const onScan = (p: OptimizationScanProgress) => setScan(p);
    bridge.events.on('optimization:scanProgress', onScan);
    return () => bridge.events.off('optimization:scanProgress', onScan);
  }, []);

  useEffect(() => {
    const onInterior = (p: OptimizationInteriorState) => {
      setApplied(a => ({ ...a, [p.groupKey]: p.optionIdx }));
      setDraft(d => (d[p.groupKey] == null ? { ...d, [p.groupKey]: p.optionIdx } : d));
      setMarkers(m => {
        const next = { ...m };
        if (p.marker) next[p.groupKey] = p.marker; else delete next[p.groupKey];
        return next;
      });
    };
    bridge.events.on('optimization:interiorState', onInterior);
    return () => bridge.events.off('optimization:interiorState', onInterior);
  }, []);

  useEffect(() => {
    const measure = () => {
      const el = stripRef.current;
      const card = el?.firstElementChild as HTMLElement | null;
      if (!el || !card) return;
      const h = card.offsetHeight;
      setFloorH(h);
      setFloors(Math.max(1, Math.round(el.scrollHeight / (h + STRIP_GAP))));
    };
    measure();
    const ro = new ResizeObserver(measure);
    if (stripRef.current) ro.observe(stripRef.current);
    window.addEventListener('resize', measure);
    return () => { ro.disconnect(); window.removeEventListener('resize', measure); };
  }, [catalog]);

  useEffect(() => {
    if (!openKey) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { setOpenKey(null); setHover(null); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [openKey]);

  const dirty = useMemo(
    () => Object.keys(draft).filter(k => draft[k] !== applied[k]),
    [draft, applied],
  );

  const totalFps = useMemo(() => {
    if (!catalog) return 0;
    return catalog.groups.reduce((sum, g) => {
      const o = g.options.find(x => x.idx === draft[g.key]);
      const n = parseInt((o?.fpsLabel ?? '').replace('+', ''), 10);
      return sum + (Number.isFinite(n) ? n : 0);
    }, 0);
  }, [catalog, draft]);

  const cycle = (group: OptimizationGroup) => {
    setFocusKey(group.key);
    setDraft(d => {
      const cur = d[group.key];
      const i = cur === null || cur === undefined
        ? 0
        : (group.options.findIndex(o => o.idx === cur) + 1) % group.options.length;
      return { ...d, [group.key]: group.options[i]?.idx ?? group.options[0].idx };
    });
  };

  const activate = (group: OptimizationGroup) => {
    setFocusKey(group.key);
    const unknown = draft[group.key] === null || draft[group.key] === undefined;
    if (group.options.length > 2 || unknown) setOpenKey(k => (k === group.key ? null : group.key));
    else cycle(group);
  };

  const pick = (group: OptimizationGroup, idx: number | null) => {
    setDraft(d => ({ ...d, [group.key]: idx }));
    setOpenKey(null);
    setHover(null);
  };

  const apply = async () => {
    if (dirty.length === 0 || applying) return;
    setApplying(true);
    try {
      const payload: OptimizationSelection[] = dirty.map(k => ({ groupKey: k, optionIdx: draft[k] }));
      const res = await bridge.optimizationApply(payload);
      if (!res.success) {
        setToast({ open: true, tone: 'error', message: res.errorMessage ?? 'Применить не вышло' });
        return;
      }
      setApplied({ ...draft });
      const n = res.changes.length;
      setToast({
        open: true, tone: 'success',
        message: (n === 0
          ? 'Настройки уже были такими - файл не трогали'
          : `Записано ${n} ${plural(n, 'параметр', 'параметра', 'параметров')}`)
          + (n === 0 ? '' : res.gameWasRunning
              ? '. GTA запущена - подействует после перезапуска игры'
              : '. Подействует при следующем запуске игры'),
      });
    } catch (e) {
      setToast({ open: true, tone: 'error', message: String(e) });
    } finally {
      setApplying(false);
    }
  };

  if (loading) return <ScanCard progress={scan} />;
  if (error || !catalog || catalog.groups.length === 0) {
    const empty = !error && catalog?.groups.length === 0;
    return (
      <div className="h-full flex items-center justify-center px-8">
        <div className="max-w-[52ch] text-center flex flex-col items-center gap-3">
          <AlertTriangle className="w-6 h-6 text-amber-400" />
          <h3 className="text-[15px] font-semibold text-text-primary">
            {empty ? 'Каталог оптимизаций пуст' : 'Конструктор не загрузился'}
          </h3>
          <p className="text-[13px] leading-relaxed text-text-secondary">
            {empty
              ? 'Групп в каталоге нет. Их заводят в админке - пока таблица не заполнена, конструктору нечего показывать.'
              : 'Лаунчер не смог получить каталог. Чаще всего это значит, что запущена сборка без нужных методов моста, либо нет связи с базой.'}
          </p>
          {error && (
            <code className="max-w-full overflow-x-auto rounded-lg bg-white/[0.05] px-3 py-2
                             text-[11.5px] text-text-secondary whitespace-pre-wrap break-words">
              {error}
            </code>
          )}
          <button
            type="button"
            onClick={() => void load()}
            style={{ outline: 'none' }}
            className="mt-1 h-9 px-4 rounded-xl border border-white/[0.10] bg-white/[0.05]
                       text-[12.5px] text-text-primary hover:bg-white/[0.10] transition-colors duration-200"
          >
            Попробовать снова
          </button>
        </div>
      </div>
    );
  }

  const focus = catalog.groups.find(g => g.key === focusKey) ?? catalog.groups[0];
  const shownIdx = hover && focus && hover.groupKey === focus.key ? hover.idx : draft[focus?.key ?? ''];
  const focusOption = focus?.options.find(o => o.idx === shownIdx) ?? null;

  const fallbackOption =
    focus?.options.find(o => o.idx === focus.resetIndex && o.previewUrl) ??
    focus?.options.find(o => o.previewUrl) ?? null;
  const previewOption = focusOption?.previewUrl ? focusOption : fallbackOption;
  const previewIsStandIn = previewOption !== focusOption;
  if (previewOption?.previewUrl) lastPreview.current = previewOption.previewUrl;
  const previewSrc = previewOption?.previewUrl ?? lastPreview.current;
  const previewIsUnrelated = !previewOption?.previewUrl && !!previewSrc;
  const openGroup = catalog.groups.find(g => g.key === openKey) ?? null;

  return (
    <div className="h-full relative overflow-hidden rounded-2xl border border-white/[0.07] bg-[#0b0d10]">
      <AnimatePresence initial={false}>
        {previewSrc && (
          <motion.img
            key={previewSrc}
            src={previewSrc}
            alt=""
            initial={{ opacity: 0, scale: 1.02 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.35, ease: EASE_DEPTH }}
            className="absolute inset-0 w-full h-full object-cover"
          />
        )}
      </AnimatePresence>
      <div
        className="absolute inset-0 pointer-events-none"
        style={{
          background:
            'linear-gradient(to top, rgba(0,0,0,0.80) 0%, rgba(0,0,0,0.34) 16%, rgba(0,0,0,0) 38%), ' +
            'linear-gradient(to bottom, rgba(0,0,0,0.42) 0%, rgba(0,0,0,0) 14%)',
        }}
      />

      <div className="absolute top-0 inset-x-0 p-4 flex items-start gap-3">
        <div className="flex-1" />

        <AnimatePresence>
          {dirty.length > 0 && (
            <motion.div
              initial={{ opacity: 0, y: -6 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -6 }}
              transition={{ duration: 0.2, ease: EASE_DEPTH }}
              className="flex items-center gap-2"
            >
              {totalFps !== 0 && (
                <span className={'inline-flex items-center gap-1.5 h-9 px-3 rounded-xl text-black ' +
                                 (totalFps < 0 ? 'bg-rose-500/90' : 'bg-emerald-500/90')}>
                  <Zap className="w-3.5 h-3.5" />
                  <span className="text-[13px] font-bold tabular-nums">
                    {totalFps > 0 ? `+${totalFps}` : totalFps} FPS
                  </span>
                </span>
              )}
              <span className="hidden sm:inline text-[12.5px] text-white/80 px-1">
                Примени изменения, чтобы они попали в игру
              </span>
              <button
                type="button"
                onClick={() => setDraft({ ...applied })}
                style={{ outline: 'none' }}
                className="h-9 px-3 rounded-xl text-[12.5px] text-white/70 hover:text-white transition-colors duration-200"
              >
                Отменить
              </button>
              <button
                type="button"
                disabled={applying}
                onClick={apply}
                style={{ outline: 'none' }}
                className="h-9 px-5 rounded-xl bg-white text-black text-[12.5px] font-bold
                           inline-flex items-center gap-2 hover:opacity-90 transition-opacity duration-200
                           disabled:opacity-60 disabled:cursor-not-allowed"
              >
                {applying && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                Применить
              </button>
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      {catalog.problems.length > 0 && (
        <div className="absolute top-16 left-4 right-4 rounded-xl border border-amber-500/40 bg-black/70 px-4 py-3">
          <div className="flex items-start gap-2.5">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0 text-amber-400" />
            <div className="min-w-0">
              <div className="text-[11.5px] font-bold uppercase tracking-[0.1em] text-amber-300">
                Каталог противоречив
              </div>
              <ul className="mt-1 space-y-0.5 text-[12.5px] text-white/70">
                {catalog.problems.slice(0, 3).map(p => <li key={p}>{p}</li>)}
              </ul>
            </div>
          </div>
        </div>
      )}

      {openKey && (
        <div
          className="absolute inset-0 z-10"
          onClick={() => { setOpenKey(null); setHover(null); }}
        />
      )}

      <div className="absolute bottom-0 inset-x-0 z-10 flex flex-col gap-3 p-4">
        <AnimatePresence mode="wait" initial={false}>
          <motion.p
            key={focus?.key ?? 'none'}
            initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}
            transition={{ duration: 0.18, ease: EASE_DEPTH }}
            className="px-1 text-[13px] leading-snug text-white/90 max-w-[92ch]"
          >
            {focus?.description}
            {previewIsUnrelated
              ? <span className="text-white/45"> · кадра для этой группы пока нет</span>
              : previewIsStandIn && previewOption && (
                  <span className="text-white/45"> · на фото: «{previewOption.name}»</span>
                )}
          </motion.p>
        </AnimatePresence>

        <AnimatePresence>
          {openGroup && (
            <OptionStrip
              key={openGroup.key}
              group={openGroup}
              value={draft[openGroup.key] ?? null}
              inGame={applied[openGroup.key] ?? null}
              onPick={idx => pick(openGroup, idx)}
              onHoverOption={idx =>
                setHover(idx === null ? null : { groupKey: openGroup.key, idx })}
            />
          )}
        </AnimatePresence>

        <div className="flex items-start gap-2.5">
        <div
          ref={stripRef}
          style={floorH ? { maxHeight: floorH } : undefined}
          onScroll={e => {
            if (!floorH) return;
            setFloor(Math.round(e.currentTarget.scrollTop / (floorH + STRIP_GAP)));
          }}
          className="flex-1 min-w-0 flex flex-wrap gap-2.5 overflow-y-auto snap-y snap-mandatory
                     [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
        >
          {catalog.groups.map(group => (
            <GroupCard
              key={group.key}
              group={group}
              value={draft[group.key] ?? null}
              inGame={applied[group.key] ?? null}
              marker={markers[group.key]}
              changed={draft[group.key] !== applied[group.key]}
              focused={group.key === focus?.key}
              open={openKey === group.key}
              onHover={() => { if (!openKey) setFocusKey(group.key); }}
              onClick={() => activate(group)}
            />
          ))}
        </div>

        {floors > 1 && floorH && (
          <div className="shrink-0 flex flex-col gap-1.5 pt-1">
            {Array.from({ length: floors }, (_, i) => (
              <button
                key={i}
                type="button"
                aria-label={`Ряд ${i + 1}`}
                onClick={() => stripRef.current?.scrollTo({
                  top: i * (floorH + STRIP_GAP), behavior: 'smooth',
                })}
                style={{ outline: 'none' }}
                className={
                  'w-1.5 rounded-full transition-all duration-250 ' +
                  (i === floor ? 'h-5 bg-white/80' : 'h-1.5 bg-white/25 hover:bg-white/50')
                }
              />
            ))}
          </div>
        )}
        </div>

      </div>

      <Toast
        open={toast.open}
        tone={toast.tone}
        message={toast.message}
        onClose={() => setToast(s => ({ ...s, open: false }))}
      />
    </div>
  );
}

function OptionStrip({ group, value, inGame, onPick, onHoverOption }: {
  group: OptimizationGroup;
  value: number | null;
  inGame: number | null;
  onPick: (idx: number | null) => void;
  onHoverOption: (idx: number | null) => void;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: 8 }}
      transition={{ duration: 0.18, ease: EASE_DEPTH }}
      onMouseLeave={() => onHoverOption(null)}
      className="flex gap-2 overflow-x-auto pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
    >
      {group.options.map(o => {
        const active = o.idx === value;
        const oFps = o.fpsLabel && o.fpsLabel !== '0' ? o.fpsLabel : null;
        return (
          <button
            key={o.idx}
            type="button"
            onClick={() => onPick(o.idx)}
            onMouseEnter={() => onHoverOption(o.idx)}
            onFocus={() => onHoverOption(o.idx)}
            style={{ outline: 'none' }}
            className={
              'shrink-0 flex items-center gap-2.5 rounded-xl border px-2.5 py-2 ' +
              'backdrop-blur-md transition-all duration-200 ' +
              (active
                ? 'bg-white/[0.18] border-white/40'
                : 'bg-black/55 border-white/[0.10] hover:bg-white/[0.10] hover:border-white/30')
            }
          >
            {o.previewUrl
              ? <img src={o.previewUrl} alt="" className="w-14 h-9 rounded-lg object-cover" />
              : <span className="w-14 h-9 rounded-lg bg-white/10" />}
            <span className="pr-1 text-left">
              <span className="flex items-center gap-1.5">
                <span className={'text-[12.5px] leading-tight ' + (active ? 'text-white font-semibold' : 'text-white/80')}>
                  {o.name}
                </span>
                {o.idx === inGame && (
                  <span className="shrink-0 rounded px-1 py-px text-[9.5px] font-bold uppercase
                                   tracking-[0.08em] bg-white/15 text-white/70">
                    ваше
                  </span>
                )}
              </span>
              {oFps && (
                <span className={'block text-[11px] font-bold tabular-nums ' + fpsTone(oFps)}>
                  {oFps} FPS
                </span>
              )}
            </span>
          </button>
        );
      })}
    </motion.div>
  );
}

function GroupCard({ group, value, inGame, marker, changed, focused, open, onHover, onClick }: {
  group: OptimizationGroup;
  value: number | null;
  inGame: number | null;
  marker: string | undefined;
  changed: boolean;
  focused: boolean;
  open: boolean;
  onHover: () => void;
  onClick: () => void;
}) {
  const current = group.options.find(o => o.idx === value) ?? null;
  const label = current?.name ?? (marker === 'custom' ? 'чужие файлы' : 'своё значение');
  const fps = current?.fpsLabel && current.fpsLabel !== '0' && current.fpsLabel !== ''
    ? current.fpsLabel : null;
  const multi = group.options.length > 2;

  return (
    <div className="relative snap-start flex-1 basis-[228px] min-w-[190px] max-w-[400px]">
      <button
        type="button"
        onMouseEnter={onHover}
        onFocus={onHover}
        onClick={onClick}
        style={{ outline: 'none' }}
        className={
          'group relative w-full text-left rounded-xl border px-4 py-3.5 ' +
          'backdrop-blur-md transition-all duration-250 ' +
          (focused || open
            ? 'bg-white/[0.16] border-white/35'
            : 'bg-black/45 border-white/[0.10] hover:bg-white/[0.10] hover:border-white/25')
        }
      >
        {fps && (
          <span className={'absolute top-3 right-3.5 text-[12.5px] font-bold tabular-nums ' + fpsTone(fps)}>
            {fps} FPS
          </span>
        )}
        {changed && (
          <span className="absolute bottom-2.5 right-3 w-1.5 h-1.5 rounded-full bg-white" />
        )}

        {group.iconUrl && (
          <div className="h-6 flex items-center">
            <img src={group.iconUrl} alt="" className="w-[18px] h-[18px] opacity-80" />
          </div>
        )}

        <div className="min-w-0">
          <div className="text-[15px] font-semibold text-white leading-snug truncate">
            {group.title}
          </div>
          <div className="mt-1 flex items-center gap-1.5">
            <span className="text-[13.5px] truncate text-white/60">
              {label}
            </span>
            {value !== null && value === inGame && (
              <span className="shrink-0 rounded px-1.5 py-px text-[10.5px] font-bold uppercase
                               tracking-[0.08em] bg-white/15 text-white/70">
                ваше
              </span>
            )}
            {value === null && marker === 'custom' && (
              <span className="shrink-0 rounded px-1.5 py-px text-[10.5px] font-bold uppercase
                               tracking-[0.08em] bg-amber-400/20 text-amber-200/90">
                у вас кастомный
              </span>
            )}
            {multi && (
              <ChevronDown
                className={'w-3 h-3 shrink-0 text-white/40 transition-transform duration-200 ' +
                           (open ? 'rotate-180' : '')}
              />
            )}
          </div>
        </div>
      </button>
    </div>
  );
}

function fpsTone(label: string) {
  return label.trim().startsWith('-') ? 'text-rose-400' : 'text-emerald-400';
}

function plural(n: number, one: string, few: string, many: string) {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}

const SCAN_STAGES: { key: OptimizationScanProgress['stage']; label: string }[] = [
  { key: 'catalog',   label: 'Каталог оптимизаций' },
  { key: 'settings',  label: 'Настройки графики' },
  { key: 'archive',   label: 'Файлы в update.rpf' },
  { key: 'gamefiles', label: 'Архивы игры и DLC' },
];

function ScanCard({ progress }: { progress: OptimizationScanProgress | null }) {
  const pct = progress?.percent ?? 0;
  const stage = progress?.stage ?? 'catalog';
  const idx = SCAN_STAGES.findIndex(s => s.key === stage);
  const current = SCAN_STAGES[idx]?.label ?? 'Готовимся к проверке';

  return (
    <div className="h-full flex items-center justify-center px-8">
      <div className="w-full max-w-[520px] rounded-2xl border border-white/[0.08] bg-black/40 p-6">
        <div className="flex items-center gap-4">
          <Loader2 className="w-5 h-5 shrink-0 animate-spin text-white/70" />
          <div className="min-w-0 flex-1">
            <h3 className="text-[15px] font-semibold text-white">Смотрим, что уже стоит в игре</h3>
            <p className="text-[12.5px] text-white/55 truncate">
              {current}{progress?.detail ? ` · ${progress.detail}` : ''}…
            </p>
          </div>
          <span className="text-3xl font-bold tabular-nums text-white shrink-0">
            {pct}<span className="text-base text-white/40">%</span>
          </span>
        </div>

        <div className="mt-4 h-2 rounded-full bg-white/[0.08] overflow-hidden">
          <motion.div
            className="h-full rounded-full bg-white"
            animate={{ width: `${pct}%` }}
            transition={{ duration: 0.3, ease: EASE_DEPTH }}
          />
        </div>

        <div className="mt-4 flex flex-col gap-1.5">
          {SCAN_STAGES.map((s, i) => {
            const done = stage === 'done' || i < idx;
            const now = i === idx && stage !== 'done';
            return (
              <div key={s.key} className="flex items-center gap-2.5">
                <span
                  className={'w-1.5 h-1.5 rounded-full shrink-0 transition-colors duration-300 ' +
                             (done ? 'bg-white' : now ? 'bg-white animate-pulse' : 'bg-white/20')}
                />
                <span className={'text-[12.5px] transition-colors duration-300 ' +
                                 (now ? 'text-white' : done ? 'text-white/55' : 'text-white/30')}>
                  {s.label}
                </span>
              </div>
            );
          })}
        </div>

        {stage === 'gamefiles' && (
          <p className="mt-4 text-[11.5px] text-white/40">
            Первый заход дольше обычного - смотрим все DLC-паки. Дальше проверка мгновенная.
          </p>
        )}
      </div>
    </div>
  );
}
