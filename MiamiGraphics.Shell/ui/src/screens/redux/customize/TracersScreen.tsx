import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { RotateCcw, Download, Check, Sparkles, Lock } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import {
  useCustomizeStore,
  TRACER_MODELS,
  type TracerModelDescriptor,
} from '@/store/customizeStore';
import { EASE_DEPTH } from '@/design';
import { PrimaryChoiceCard, DefaultGlyph, SaveBar } from './GenericComponentScreen';

export function TracersScreen() {
  const { t } = useTranslation();
  const draft        = useCustomizeStore(s => s.draft);
  const goManage     = useCustomizeStore(s => s.goManage);
  const setTracer    = useCustomizeStore(s => s.setTracer);
  const resetComp    = useCustomizeStore(s => s.resetComponent);
  const openImport   = useCustomizeStore(s => s.openImportPicker);

  const tr = draft?.tracers;

  const baselineRef = useRef(draft?.tracers);
  const onCancel = () => {
    if (baselineRef.current) setTracer(baselineRef.current);
    goManage();
  };

  const [presetsMode, setPresetsMode] = useState(draft?.tracers?.source.kind === 'model');

  if (!tr) return null;

  const selectedModelId = tr.source.kind === 'model' ? tr.source.modelId : null;
  const importedFrom    = tr.source.kind === 'import' ? tr.source : null;

  const isDefault = tr.source.kind === 'default' && !presetsMode;
  const isPresets = presetsMode || tr.source.kind === 'model';
  const isImport  = tr.source.kind === 'import'  && !presetsMode;

  const onPickDefault = () => {
    setPresetsMode(false);
    setTracer({ source: { kind: 'default' } });
  };
  const onPickPresets = () => {
    setPresetsMode(true);
  };
  const onPickImport = () => {
    setPresetsMode(false);
    openImport('tracers');
  };
  const onPickModel = (m: TracerModelDescriptor) => {
    setPresetsMode(true);
    setTracer({ source: { kind: 'model', modelId: m.id } });
  };

  const setRgb = (next: Partial<{ r: number; g: number; b: number }>) =>
    setTracer({ rgb: { ...tr.rgb, ...next } });

  const colorLocked = isImport && !(tr.overrideColor ?? false);

  return (
    <div className="relative h-full flex flex-col">
      <header className="px-8 pt-6 pb-2 flex items-center gap-4 shrink-0">
        <BackButton onClick={onCancel} label={t('customize.backToManage')} />
        <div className="flex-1" />
        <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted">
          {t('customize.tracers.title')}
        </span>
        <button
          type="button"
          onClick={() => resetComp('tracers')}
          title={t('customize.reset')}
          aria-label={t('customize.reset')}
          className="w-9 h-9 rounded-lg flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-bg-elevated transition-colors"
        >
          <RotateCcw size={14} />
        </button>
      </header>

      <div className="flex-1 overflow-y-auto px-8 pb-32">
        <div className="max-w-[1280px] mx-auto flex flex-col items-center gap-10 py-8">

          {}
          <div className="w-full flex justify-center">
            <div className="w-full grid gap-5 grid-cols-1 md:grid-cols-3 max-w-[1000px]">
              <motion.div
                initial={{ opacity: 0, y: 14 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.4, delay: 0.02, ease: EASE_DEPTH }}
              >
                <PrimaryChoiceCard
                  icon={<DefaultGlyph />}
                  title={t('customize.defaultName')}
                  hint={t('customize.defaultHint')}
                  selected={isDefault}
                  onClick={onPickDefault}
                />
              </motion.div>

              <motion.div
                initial={{ opacity: 0, y: 14 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.4, delay: 0.06, ease: EASE_DEPTH }}
              >
                <PrimaryChoiceCard
                  icon={<Sparkles size={36} strokeWidth={1.4} className="text-accent" />}
                  title={t('customize.tracers.presetsTitle')}
                  hint={
                    selectedModelId
                      ? (TRACER_MODELS.find(m => m.id === selectedModelId)?.name
                         ?? t('customize.tracers.presetsHint'))
                      : t('customize.tracers.presetsHint')
                  }
                  tone="accent"
                  selected={isPresets}
                  onClick={onPickPresets}
                />
              </motion.div>

              <motion.div
                initial={{ opacity: 0, y: 14 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.4, delay: 0.10, ease: EASE_DEPTH }}
              >
                <PrimaryChoiceCard
                  icon={<Download size={36} strokeWidth={1.4} className="text-accent" />}
                  title={t('customize.importTitle')}
                  hint={importedFrom ? importedFrom.donorReduxName : t('customize.importHint')}
                  tone="accent"
                  selected={isImport}
                  onClick={onPickImport}
                />
              </motion.div>

            </div>
          </div>

          {}
          <AnimatePresence initial={false}>
            {isPresets && (
              <motion.div
                key="presets-grid"
                initial={{ opacity: 0, y: -6, height: 0 }}
                animate={{ opacity: 1, y: 0,  height: 'auto' }}
                exit={{ opacity: 0, y: -6, height: 0 }}
                transition={{ duration: 0.32, ease: EASE_DEPTH }}
                style={{ overflow: 'hidden' }}
                className="w-full max-w-[1000px]"
              >
                <div className="flex flex-col items-center gap-3 w-full pt-2">
                  <span className="text-[10px] uppercase tracking-[0.28em] text-text-muted inline-flex items-center gap-2">
                    <Sparkles size={11} className="text-accent" />
                    {t('customize.tracers.modelPickerLabel')}
                  </span>
                  <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-5 gap-3 w-full">
                    {TRACER_MODELS.map((m, i) => (
                      <motion.div
                        key={m.id}
                        initial={{ opacity: 0, y: 8 }}
                        animate={{ opacity: 1, y: 0 }}
                        transition={{ duration: 0.32, delay: i * 0.025, ease: EASE_DEPTH }}
                      >
                        <ModelTile
                          model={m}
                          selected={selectedModelId === m.id}
                          onClick={() => onPickModel(m)}
                        />
                      </motion.div>
                    ))}
                  </div>
                </div>
              </motion.div>
            )}
          </AnimatePresence>

          <AnimatePresence initial={false}>
            {isImport && (
              <motion.div
                key="import-options"
                initial={{ opacity: 0, y: -6, height: 0 }}
                animate={{ opacity: 1, y: 0,  height: 'auto' }}
                exit={{ opacity: 0, y: -6, height: 0 }}
                transition={{ duration: 0.32, ease: EASE_DEPTH }}
                style={{ overflow: 'hidden' }}
                className="w-full max-w-[720px]"
              >
                <div className="w-full rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4">
                  <span className="text-[10px] uppercase tracking-[0.28em] text-text-muted inline-flex items-center gap-2">
                    <Download size={11} className="text-accent" />
                    {t('customize.tracers.importOptionsLabel')}
                  </span>
                  <ToggleRow
                    title={t('customize.tracers.cleanEffectsTitle')}
                    hint={t('customize.tracers.cleanEffectsHint')}
                    checked={tr.useCleanEffects ?? false}
                    onChange={v => setTracer({ useCleanEffects: v })}
                  />
                  <ToggleRow
                    title={t('customize.tracers.takeBloodTitle')}
                    hint={t('customize.tracers.takeBloodHint')}
                    checked={tr.takeDonorBlood ?? false}
                    onChange={v => setTracer({ takeDonorBlood: v })}
                  />
                  <ToggleRow
                    title={t('customize.tracers.personalizeColorTitle')}
                    hint={t('customize.tracers.personalizeColorHint')}
                    checked={tr.overrideColor ?? false}
                    onChange={v => setTracer({ overrideColor: v })}
                  />
                </div>
              </motion.div>
            )}
          </AnimatePresence>

          {}
          <div className="relative w-full max-w-[720px] rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-5">
            {colorLocked && (
              <div className="absolute inset-0 z-10 rounded-2xl bg-bg-surface/80 flex items-center justify-center px-6">
                <div className="flex items-center gap-2.5 px-4 py-2.5 rounded-xl bg-bg-elevated border border-border-subtle text-text-secondary text-sm text-center shadow-z2">
                  <Lock size={14} className="text-text-muted shrink-0" />
                  <span>{t('customize.tracers.donorColorLockLabel')}</span>
                </div>
              </div>
            )}
            <div className="flex items-center gap-3">
              <div
                className="w-12 h-12 rounded-xl border border-border-subtle shadow-inner"
                style={{ background: `rgb(${tr.rgb.r}, ${tr.rgb.g}, ${tr.rgb.b})` }}
              />
              <div className="flex flex-col gap-0.5">
                <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">
                  {t('customize.tracers.colorLabel')}
                </span>
                <span className="font-mono text-sm text-text-primary tracking-wider">
                  {rgbToHex(tr.rgb)}{' '}
                  <span className="text-text-muted">·  RGB({tr.rgb.r}, {tr.rgb.g}, {tr.rgb.b})</span>
                </span>
              </div>
              <div className="flex-1" />
              <input
                type="color"
                value={rgbToHex(tr.rgb)}
                onChange={e => {
                  const c = hexToRgb(e.target.value);
                  if (c) setTracer({ rgb: c });
                }}
                className="w-12 h-12 rounded-xl cursor-pointer border-0 bg-transparent"
                aria-label={t('customize.tracers.colorLabel')}
              />
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <ChannelSlider label="R" channel="r" value={tr.rgb.r} onChange={v => setRgb({ r: v })} accent="rgb(255 80 80)" />
              <ChannelSlider label="G" channel="g" value={tr.rgb.g} onChange={v => setRgb({ g: v })} accent="rgb(80 220 120)" />
              <ChannelSlider label="B" channel="b" value={tr.rgb.b} onChange={v => setRgb({ b: v })} accent="rgb(80 140 255)" />
            </div>

            <div className="flex flex-wrap gap-2">
              {PRESETS.map(p => (
                <button
                  key={p.hex}
                  type="button"
                  onClick={() => {
                    const c = hexToRgb(p.hex);
                    if (c) setTracer({ rgb: c });
                  }}
                  title={p.name}
                  className="w-7 h-7 rounded-md border border-border-subtle hover:scale-110 transition-transform"
                  style={{ background: p.hex }}
                  aria-label={p.name}
                />
              ))}
            </div>
          </div>
        </div>
      </div>

      <SaveBar onSave={goManage} onCancel={onCancel} />
    </div>
  );
}

function ModelTile({
  model, selected, onClick,
}: { model: TracerModelDescriptor; selected: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        'group relative flex flex-col rounded-2xl overflow-hidden text-left ' +
        'bg-glass-strong backdrop-blur-glass border ' +
        'transform-gpu will-change-transform ' +
        'transition-[transform,box-shadow,border-color] duration-[420ms] ease-smooth ' +
        'hover:-translate-y-0.5 ' +
        (selected
          ? 'border-accent shadow-glow-accent'
          : 'border-glass-border hover:border-accent/60 hover:shadow-z2')
      }
    >
      {selected && (
        <motion.div
          initial={{ scale: 0, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ duration: 0.24, ease: EASE_DEPTH }}
          className="absolute top-2 right-2 z-10 w-6 h-6 rounded-full bg-accent flex items-center justify-center shadow-glow-accent ring-2 ring-bg-base"
        >
          <Check size={12} className="text-text-on-accent" strokeWidth={3} />
        </motion.div>
      )}
      {}
      <div className="aspect-[16/10] bg-black/40 flex items-center justify-center overflow-hidden">
        <img
          src={model.preview}
          alt={model.name}
          loading="lazy"
          className="max-w-full max-h-full object-contain transition-transform duration-[420ms] ease-smooth group-hover:scale-[1.04]"
        />
      </div>
      <div className="px-3 pt-2 pb-3 flex flex-col gap-0.5">
        <span className="font-medium text-sm text-text-primary truncate">{model.name}</span>
        <span className="text-[11px] text-text-muted truncate">{model.folderName}</span>
      </div>
    </button>
  );
}

function ToggleRow({
  title, hint, checked, onChange,
}: {
  title: string;
  hint: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className="flex items-center gap-4 text-left rounded-xl px-1 py-1 group"
    >
      <div className="flex-1 flex flex-col gap-0.5">
        <span className="text-sm text-text-primary">{title}</span>
        <span className="text-[11px] text-text-muted leading-snug">{hint}</span>
      </div>
      <span
        className={
          'shrink-0 w-11 h-6 rounded-full relative transition-colors duration-200 ' +
          (checked ? 'bg-accent' : 'bg-bg-elevated border border-border-subtle')
        }
      >
        <span
          className={
            'absolute top-0.5 w-5 h-5 rounded-full bg-white shadow transition-all duration-200 ' +
            (checked ? 'left-[22px]' : 'left-0.5')
          }
        />
      </span>
    </button>
  );
}

function ChannelSlider({
  label, channel, value, onChange, accent,
}: {
  label: string;
  channel: 'r' | 'g' | 'b';
  value: number;
  onChange: (v: number) => void;
  accent: string;
}) {
  return (
    <label className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between text-xs">
        <span className="text-text-muted uppercase tracking-wider">{label}</span>
        <input
          type="number"
          min={0}
          max={255}
          value={value}
          onChange={e => {
            const v = Math.max(0, Math.min(255, parseInt(e.target.value, 10) || 0));
            onChange(v);
          }}
          className="w-14 px-2 py-0.5 bg-bg-elevated border border-border-subtle rounded text-text-primary font-mono text-xs text-right outline-none focus:border-accent"
        />
      </div>
      <input
        type="range"
        min={0}
        max={255}
        value={value}
        onChange={e => onChange(parseInt(e.target.value, 10))}
        className="w-full"
        style={{
          accentColor: accent,
        }}
        aria-label={`${label} channel`}
        data-channel={channel}
      />
    </label>
  );
}

const PRESETS = [
  { hex: '#FFFFFF', name: 'White' },
  { hex: '#FFD66B', name: 'Gold' },
  { hex: '#FF6B6B', name: 'Red'  },
  { hex: '#F472B6', name: 'Pink' },
  { hex: '#7C5CFF', name: 'Violet' },
  { hex: '#60A5FA', name: 'Blue' },
  { hex: '#22D3EE', name: 'Cyan' },
  { hex: '#34D399', name: 'Green' },
  { hex: '#FBBF24', name: 'Amber' },
];

function rgbToHex({ r, g, b }: { r: number; g: number; b: number }): string {
  const h = (n: number) => n.toString(16).padStart(2, '0').toUpperCase();
  return '#' + h(r) + h(g) + h(b);
}

function hexToRgb(hex: string): { r: number; g: number; b: number } | null {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex);
  if (!m) return null;
  const n = parseInt(m[1], 16);
  return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 };
}
