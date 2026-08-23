import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import { RotateCcw, Upload, Download, X, ShieldCheck, Palette, Check, Library } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import { Toggle3D } from '@/design/primitives/Toggle3D';
import { bridge } from '@/bridge';
import { useCustomizeStore, DEFAULT_MINIMAP_TWEAKS } from '@/store/customizeStore';
import { SaveBar } from './GenericComponentScreen';

const pageContainer: Variants = {
  hidden: { opacity: 1 },
  visible: { opacity: 1, transition: { delayChildren: 0.04, staggerChildren: 0.06 } },
};
const pageItem: Variants = {
  hidden: { opacity: 0, y: 12 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.42, ease: [0.22, 1, 0.36, 1] } },
};
const EASE = [0.22, 1, 0.36, 1] as const;

type MinimapMode = 'author' | 'personalize' | 'library' | 'import';

export function MinimapScreen() {
  const { t } = useTranslation();
  const draft        = useCustomizeStore(s => s.draft);
  const goManage     = useCustomizeStore(s => s.goManage);
  const setMinimap   = useCustomizeStore(s => s.setMinimap);
  const resetComp    = useCustomizeStore(s => s.resetComponent);
  const openImport   = useCustomizeStore(s => s.openImportPicker);
  const openLibrary  = useCustomizeStore(s => s.openLibraryPicker);

  const m = draft?.minimap;

  const [presets, setPresets] = useState<{ ratio: string; placement: string }[] | null>(null);
  useEffect(() => {
    let alive = true;
    bridge.minimapLayoutGetPresets?.()
      .then(rows => { if (alive) setPresets(rows?.length ? rows : null); })
      .catch(() => { if (alive) setPresets(null); });
    return () => { alive = false; };
  }, []);
  const ratios = presets ? [...new Set(presets.map(p => p.ratio))] : [];
  const places = presets ? [...new Set(presets.map(p => p.placement))] : [];

  const baselineRef = useRef(draft?.minimap);
  const onCancel = () => {
    if (baselineRef.current) setMinimap(baselineRef.current);
    goManage();
  };

  if (!m) return null;

  const onPickPng = async () => {
    const path = await bridge.openFileDialog('PNG / GIF', '*.png;*.gif');
    if (path) setMinimap({ pngOverlayPath: path });
  };

  const tw = m.tweaks ?? DEFAULT_MINIMAP_TWEAKS;
  const setTweaks = (patch: Partial<typeof tw>) => setMinimap({ tweaks: { ...tw, ...patch } });

  const mode: MinimapMode = !m.enabled
    ? 'author'
    : m.librarySource
      ? 'library'
      : m.importedFrom
        ? 'import'
        : 'personalize';

  const pickMode = (next: MinimapMode) => {
    if (next === 'author') {

      setMinimap({ enabled: false, importedFrom: null, librarySource: null });
      return;
    }
    if (next === 'personalize') {
      setMinimap({ enabled: true, importedFrom: null, librarySource: null });
      return;
    }
    if (next === 'library') {
      setMinimap({ enabled: true, importedFrom: null });
      openLibrary('minimap');
      return;
    }

    setMinimap({ enabled: true, librarySource: null });
    openImport('minimap');
  };

  return (
    <div className="relative h-full flex flex-col">
      <header className="px-8 pt-6 pb-2 flex items-center gap-4 shrink-0">
        <BackButton onClick={onCancel} label={t('customize.backToManage')} />
        <div className="flex-1" />
        <button
          type="button"
          onClick={() => resetComp('minimap')}
          title={t('customize.reset')}
          aria-label={t('customize.reset')}
          className="w-9 h-9 rounded-lg flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-bg-elevated transition-colors"
        >
          <RotateCcw size={14} />
        </button>
      </header>

      <div className="flex-1 overflow-y-auto px-8 pb-32">
        <motion.div
          className="max-w-[1280px] mx-auto flex flex-col gap-6"
          variants={pageContainer}
          initial="hidden"
          animate="visible"
        >
          <motion.div className="flex flex-col gap-1" variants={pageItem}>
            <span className="text-xs uppercase tracking-[0.2em] text-text-muted">{t('customize.section')}</span>
            <h2 className="font-display font-bold text-2xl text-text-primary uppercase tracking-wide">
              {t('customize.minimap.title')}
            </h2>
          </motion.div>

          {}
          <motion.div className="flex flex-col gap-2" variants={pageItem}>
            <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">
              {t('customize.minimap.modeSectionLabel')}
            </span>
            <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-3">
              <ModeCard
                active={mode === 'author'}
                icon={<ShieldCheck size={18} />}
                title={t('customize.minimap.modeAuthorTitle')}
                hint={t('customize.minimap.modeAuthorHint')}
                onClick={() => pickMode('author')}
              />
              <ModeCard
                active={mode === 'personalize'}
                icon={<Palette size={18} />}
                title={t('customize.minimap.modePersonalizeTitle')}
                hint={t('customize.minimap.modePersonalizeHint')}
                onClick={() => pickMode('personalize')}
              />
              <ModeCard
                active={mode === 'library'}
                icon={<Library size={18} />}
                title={t('customize.minimap.modeLibraryTitle', 'Из каталога')}
                hint={t('customize.minimap.modeLibraryHint', 'Готовая миникарта из каталога Miami Graphics')}
                onClick={() => pickMode('library')}
              />
              <ModeCard
                active={mode === 'import'}
                icon={<Download size={18} />}
                title={t('customize.minimap.modeImportTitle')}
                hint={t('customize.minimap.modeImportHint')}
                onClick={() => pickMode('import')}
              />
            </div>
          </motion.div>

          <motion.div
            className="flex flex-col gap-6"
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.42, ease: EASE }}
          >

          {}
          <AnimatePresence initial={false}>
          {m.importedFrom && (

            <motion.div
              key="import-banner"
              initial={{ opacity: 0, y: -8, height: 0 }}
              animate={{ opacity: 1, y: 0, height: 'auto' }}
              exit={{ opacity: 0, y: -8, height: 0 }}
              transition={{ duration: 0.32, ease: EASE }}
              className="overflow-hidden"
            >
              <div className="rounded-2xl bg-bg-surface border border-border-subtle p-4 flex items-center gap-3">
                <div className="w-10 h-10 rounded-lg bg-accent-soft flex items-center justify-center text-accent shrink-0">
                  <Download size={16} />
                </div>
                <div className="flex flex-col gap-0.5 min-w-0 flex-1">
                  <span className="text-sm font-medium text-text-primary">{t('customize.minimap.importActive')}</span>
                  <span className="text-xs text-text-muted truncate">
                    {t('customize.minimap.importSource')}{' '}
                    <span className="text-accent font-display font-bold tracking-wide uppercase">{m.importedFrom.reduxName}</span>
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => openImport('minimap')}
                  className="px-3 py-1.5 rounded-lg text-xs text-accent hover:bg-accent-soft transition-colors"
                  style={{ outline: 'none' }}
                >
                  {t('customize.minimap.changeImport')}
                </button>
                <button
                  type="button"
                  onClick={() => setMinimap({ importedFrom: null })}
                  title={t('customize.minimap.clearImport')}
                  aria-label={t('customize.minimap.clearImport')}
                  className="w-7 h-7 rounded-md flex items-center justify-center text-text-muted hover:text-status-error hover:bg-bg-elevated transition-colors"
                  style={{ outline: 'none' }}
                >
                  <X size={14} />
                </button>
              </div>
            </motion.div>
          )}
          </AnimatePresence>

          <AnimatePresence initial={false}>
          {m.librarySource && (
            <motion.div
              key="library-banner"
              initial={{ opacity: 0, y: -8, height: 0 }}
              animate={{ opacity: 1, y: 0, height: 'auto' }}
              exit={{ opacity: 0, y: -8, height: 0 }}
              transition={{ duration: 0.32, ease: EASE }}
              className="overflow-hidden"
            >
              <div className="rounded-2xl bg-bg-surface border border-border-subtle p-4 flex items-center gap-3">
                <div className="w-10 h-10 rounded-lg bg-accent-soft flex items-center justify-center text-accent shrink-0">
                  <Library size={16} />
                </div>
                <div className="flex flex-col gap-0.5 min-w-0 flex-1">
                  <span className="text-sm font-medium text-text-primary">
                    {t('customize.minimap.libraryActive', 'Миникарта из каталога')}
                  </span>
                  <span className="text-xs text-text-muted truncate">
                    {t('customize.minimap.librarySource', 'Источник:')}{' '}
                    <span className="text-accent font-display font-bold tracking-wide uppercase">{m.librarySource.libraryItemName}</span>
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => openLibrary('minimap')}
                  className="px-3 py-1.5 rounded-lg text-xs text-accent hover:bg-accent-soft transition-colors"
                  style={{ outline: 'none' }}
                >
                  {t('customize.minimap.changeLibrary', 'Сменить')}
                </button>
                <button
                  type="button"
                  onClick={() => setMinimap({ librarySource: null })}
                  title={t('customize.minimap.clearLibrary', 'Убрать миникарту из каталога')}
                  aria-label={t('customize.minimap.clearLibrary', 'Убрать миникарту из каталога')}
                  className="w-7 h-7 rounded-md flex items-center justify-center text-text-muted hover:text-status-error hover:bg-bg-elevated transition-colors"
                  style={{ outline: 'none' }}
                >
                  <X size={14} />
                </button>
              </div>
            </motion.div>
          )}
          </AnimatePresence>

          <AnimatePresence initial={false}>
          {mode === 'personalize' && (
            <motion.div
              key="personalize"
              initial={{ opacity: 0, y: -8, height: 0 }}
              animate={{ opacity: 1, y: 0, height: 'auto' }}
              exit={{ opacity: 0, y: -8, height: 0 }}
              transition={{ duration: 0.32, ease: EASE }}
              className="overflow-hidden"
            >
              <div className="rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-6">
                <div className="flex flex-col gap-4">
                  <div className="flex items-center justify-between gap-4">
                    <div className="flex flex-col gap-0.5">
                      <span className="text-sm font-medium text-text-primary">
                        {t('customize.minimap.colorsToggleTitle', 'Изменить цвета миникарты')}
                      </span>
                      <span className="text-xs text-text-muted">
                        {t('customize.minimap.colorsToggleHint', 'Выключено - остаются оригинальные цвета редукса')}
                      </span>
                    </div>
                    <Toggle3D
                      checked={m.colorsEnabled}
                      onChange={v => setMinimap({ colorsEnabled: v })}
                      ariaLabel={t('customize.minimap.colorsToggleTitle', 'Изменить цвета миникарты')}
                    />
                  </div>
                  <AnimatePresence initial={false}>
                    {m.colorsEnabled && (
                      <motion.div
                        key="colors"
                        className="grid grid-cols-1 md:grid-cols-2 gap-4 overflow-hidden"
                        initial={{ opacity: 0, height: 0 }}
                        animate={{ opacity: 1, height: 'auto' }}
                        exit={{ opacity: 0, height: 0 }}
                        transition={{ duration: 0.28, ease: EASE }}
                      >
                        <ColorField
                          label={t('customize.minimap.hpColor')}
                          value={m.hpColor}
                          onChange={v => setMinimap({ hpColor: v })}
                        />
                        <ColorField
                          label={t('customize.minimap.armorColor')}
                          value={m.armorColor}
                          onChange={v => setMinimap({ armorColor: v })}
                        />
                      </motion.div>
                    )}
                  </AnimatePresence>
                </div>

                <Field label={t('customize.minimap.pngOverlay')}>
                  {m.pngOverlayPath && (
                    <div className="flex items-center gap-3 mb-2 p-2 rounded-xl bg-bg-elevated border border-border-subtle">
                      <div className="w-16 h-16 rounded-lg bg-black/40 border border-white/10 overflow-hidden shrink-0 flex items-center justify-center">
                        <img
                          src={`file:///${m.pngOverlayPath.replace(/\\/g, '/')}`}
                          alt=""
                          className="max-w-full max-h-full object-contain"
                          style={{ transform: `scale(${(tw.hitScale ?? 100) / 100})` }}
                          onError={e => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                        />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="text-[11px] text-text-muted uppercase tracking-wider font-bold mb-1">
                          Масштаб вспышки: {tw.hitScale ?? 100}%
                        </div>
                        <input
                          type="range" min={10} max={400} step={5}
                          value={tw.hitScale ?? 100}
                          onChange={e => setTweaks({ hitScale: parseInt(e.target.value, 10) })}
                          style={{ accentColor: 'var(--accent)' }}
                          className="w-full cursor-pointer"
                        />
                      </div>
                    </div>
                  )}
                  <button
                    type="button"
                    onClick={onPickPng}
                    className="w-full h-16 rounded-xl border border-dashed border-border-strong hover:border-accent/70 hover:bg-bg-elevated/50 flex items-center justify-center gap-2 text-sm text-text-muted hover:text-text-primary transition-colors"
                    style={{ outline: 'none' }}
                  >
                    <Upload size={14} />
                    {m.pngOverlayPath
                      ? <span className="font-mono text-xs truncate max-w-md">{m.pngOverlayPath}</span>
                      : <span>{t('customize.minimap.pickPng')}</span>}
                  </button>
                  <AnimatePresence initial={false}>
                    {m.pngOverlayPath && (
                      <motion.button
                        key="clear-png"
                        type="button"
                        onClick={() => setMinimap({ pngOverlayPath: null })}
                        initial={{ opacity: 0, y: -4, height: 0 }}
                        animate={{ opacity: 1, y: 0,  height: 'auto' }}
                        exit={{ opacity: 0, y: -4, height: 0 }}
                        transition={{ duration: 0.22, ease: EASE }}
                        className="self-start text-xs text-text-muted hover:text-status-error transition-colors mt-1 overflow-hidden"
                        style={{ outline: 'none' }}
                      >
                        {t('customize.minimap.clearPng')}
                      </motion.button>
                    )}
                  </AnimatePresence>
                </Field>

                {presets && (
                  <div className="flex flex-col gap-4 pt-2 border-t border-border-subtle">
                    <SegmentRow
                      label={t('customize.minimap.sizeLabel', 'Размер миникарты')}
                      options={ratios.map(r => ({ value: r, label: r }))}
                      value={m.aspectRatio}
                      onChange={v => setMinimap({ aspectRatio: v as typeof m.aspectRatio })}
                    />
                    <SegmentRow
                      label={t('customize.minimap.posLabel', 'Положение на экране')}
                      options={places.map(p => ({
                        value: p,
                        label: p === 'center' ? 'По центру' : p === 'default' ? 'Как в игре' : p,
                      }))}
                      value={m.position}
                      onChange={v => setMinimap({ position: v as typeof m.position })}
                    />
                    <p className="text-[11px] text-text-muted leading-relaxed">
                      Размеры и координаты берутся из проверенных раскладок нашей базы.
                    </p>
                  </div>
                )}

                <p className="text-[11.5px] text-text-muted leading-relaxed pt-2 border-t border-border-subtle">
                  Цифры HP и брони и полоса настраиваются в «Мастерской» - там их можно
                  двигать мышью и сразу видеть результат. Там же можно поставить миникарту
                  в произвольное место, если фиксированных раскладок мало.
                </p>
              </div>
            </motion.div>
          )}
          </AnimatePresence>

          </motion.div>
        </motion.div>
      </div>

      <SaveBar onSave={goManage} onCancel={onCancel} />
    </div>
  );
}

function ModeCard({
  active, icon, title, hint, onClick,
}: {
  active: boolean;
  icon: React.ReactNode;
  title: string;
  hint: string;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      style={{ outline: 'none' }}
      className={
        'relative flex flex-col items-start gap-2 p-4 rounded-2xl border text-left ' +
        'transition-[background-color,border-color,box-shadow,transform] duration-300 ease-out ' +
        (active
          ? 'bg-accent-soft/50 border-transparent -translate-y-0.5'
          : 'bg-bg-surface border-border-subtle hover:border-border-strong hover:-translate-y-0.5')
      }
    >
      <div className="flex items-center gap-3 w-full">
        <motion.div
          layout
          className={
            'w-10 h-10 rounded-lg flex items-center justify-center shrink-0 transition-colors duration-300 ' +
            (active ? 'bg-accent text-text-on-accent' : 'bg-bg-elevated text-text-muted')
          }
        >
          {icon}
        </motion.div>
        <span className="text-sm font-semibold leading-snug flex-1 text-text-primary">
          {title}
        </span>

        {}
        <AnimatePresence>
          {active && (
            <motion.span
              key="check"
              initial={{ opacity: 0, scale: 0.5 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.5 }}
              transition={{ type: 'spring', stiffness: 450, damping: 22 }}
              className="w-6 h-6 rounded-full bg-accent text-text-on-accent flex items-center justify-center shrink-0 shadow-z1"
            >
              <Check size={13} strokeWidth={3} />
            </motion.span>
          )}
        </AnimatePresence>
      </div>
      <span className="text-xs text-text-muted leading-relaxed">
        {hint}
      </span>
    </button>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-2">
      <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">{label}</span>
      {children}
    </label>
  );
}

function ColorField({
  label, value, onChange,
}: { label: string; value: string; onChange: (next: string) => void }) {
  const safe = /^#[0-9a-f]{6}$/i.test(value) ? value : '#000000';
  return (
    <Field label={label}>
      <div className="flex items-center gap-2 px-3 h-12 rounded-xl bg-bg-elevated border border-border-subtle focus-within:border-accent transition-colors">
        <label className="relative w-7 h-7 rounded-md overflow-hidden cursor-pointer shrink-0" style={{ background: safe }}>
          <input
            type="color"
            value={safe}
            onChange={e => onChange(e.target.value.toUpperCase())}
            className="absolute inset-0 opacity-0 cursor-pointer"
            aria-label={label}
          />
        </label>
        <input
          type="text"
          value={value}
          onChange={e => {
            const next = e.target.value;

            if (/^#?[0-9a-f]{0,6}$/i.test(next)) {
              onChange(next.startsWith('#') ? next.toUpperCase() : ('#' + next).toUpperCase());
            }
          }}
          className="flex-1 bg-transparent outline-none text-sm font-mono text-text-primary tracking-wider"
          spellCheck={false}
        />
      </div>
    </Field>
  );
}

function SegmentRow({
  label, options, value, onChange,
}: {
  label: string;
  options: { value: string; label: string }[];
  value: string;
  onChange: (next: string) => void;
}) {
  return (
    <div className="flex flex-col gap-2">
      <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">{label}</span>
      <div className="flex flex-wrap gap-2">
        {options.map(o => {
          const active = o.value === value;
          return (
            <button
              key={o.value}
              type="button"
              onClick={() => onChange(o.value)}
              aria-pressed={active}
              style={{ outline: 'none' }}
              className={
                'px-4 h-10 rounded-xl border text-[12px] font-bold uppercase tracking-[0.1em] ' +
                'transition-[background-color,border-color,color] duration-300 ease-out ' +
                (active
                  ? 'bg-accent-soft/60 border-transparent text-text-primary'
                  : 'bg-bg-elevated border-border-subtle text-text-secondary hover:text-text-primary hover:border-border-strong')
              }
            >
              {o.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}
