import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Check, ChevronDown } from 'lucide-react';
import { useManualSettingsStore } from '@/store/manualSettingsStore';
import type { ManualCategory } from '@/store/manualSettingsStore';

interface FieldProps {
  label:     string;
  hint?:     string;
  children:  React.ReactNode;
  category?: ManualCategory;
  fieldKey?: string;
  inline?:   boolean;
}

export function Field({ label, hint, children, category, fieldKey }: FieldProps) {
  const { t } = useTranslation();
  const dirty = useManualSettingsStore(s =>
    category && fieldKey ? s.dirtyKeys.has(`${category}.${fieldKey}`) : false
  );
  return (
    <div className="grid grid-cols-[1fr_280px] gap-8 items-start py-4 border-b border-border-subtle last:border-b-0">
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <span className="text-[14px] text-text-primary">{label}</span>
          {dirty && (
            <span
              aria-label={t('manual.field.changed', 'изменено')}
              title={t('manual.field.changedTitle', 'Изменено относительно загруженного')}
              className="w-1 h-1 rounded-full bg-status-warning"
            />
          )}
        </div>
        {hint && (
          <p className="mt-1 text-[12px] text-text-muted leading-[1.55] max-w-md">{hint}</p>
        )}
      </div>
      <div className="pt-0.5 flex justify-end items-start">{children}</div>
    </div>
  );
}

interface ToggleProps {
  checked:  boolean;
  onChange: (next: boolean) => void;
  ariaLabel?: string;
}

export function Toggle({ checked, onChange, ariaLabel }: ToggleProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={ariaLabel}
      onClick={() => onChange(!checked)}
      style={{ outline: 'none' }}
      className={
        'relative inline-flex w-9 h-[22px] rounded-full transition-colors duration-150 ease-out ' +
        (checked ? 'bg-accent' : 'bg-track')
      }
    >
      <span
        aria-hidden
        className={
          'absolute top-[2px] block w-[18px] h-[18px] rounded-full bg-white ' +
          'shadow-[0_1px_3px_rgba(0,0,0,0.18),0_0_0_0.5px_rgba(0,0,0,0.08)] ' +
          'transition-[left] duration-150 ease-out ' +
          (checked ? 'left-[19px]' : 'left-[2px]')
        }
      />
    </button>
  );
}

interface OptionPillsProps<T extends string | number> {
  options:  ReadonlyArray<{ value: T; label: string }>;
  value:    T;
  onChange: (next: T) => void;
}

export function OptionPills<T extends string | number>({
  options, value, onChange,
}: OptionPillsProps<T>) {
  return (
    <div className="inline-flex items-center rounded-md bg-bg-elevated-soft border border-border-subtle p-0.5">
      {options.map(opt => {
        const active = opt.value === value;
        return (
          <button
            key={String(opt.value)}
            type="button"
            onClick={() => onChange(opt.value)}
            style={{ outline: 'none' }}
            className={
              'px-2.5 h-7 inline-flex items-center text-[12px] rounded-[5px] transition-colors duration-150 ' +
              (active
                ? 'bg-bg-base text-text-primary shadow-sm'
                : 'text-text-muted hover:text-text-secondary')
            }
          >
            {opt.label}
          </button>
        );
      })}
    </div>
  );
}

interface SelectProps<T extends string | number> {
  options:  ReadonlyArray<{ value: T; label: string }>;
  value:    T;
  onChange: (next: T) => void;
  type?:    'string' | 'number';
}

export function Select<T extends string | number>({
  options, value, onChange,
}: SelectProps<T>) {
  const [open, setOpen] = useState(false);
  const wrapperRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) return;
    const onDocClick = (e: MouseEvent) => {
      if (!wrapperRef.current) return;
      if (!wrapperRef.current.contains(e.target as Node)) setOpen(false);
    };
    const onEsc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onEsc);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onEsc);
    };
  }, [open]);

  const current = options.find(o => o.value === value);
  const currentLabel = current?.label ?? String(value);

  return (
    <div ref={wrapperRef} className="relative w-full">
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        aria-haspopup="listbox"
        aria-expanded={open}
        style={{ outline: 'none' }}
        className={
          'w-full h-8 pl-3 pr-8 rounded-md flex items-center text-left ' +
          'bg-bg-elevated-soft hover:bg-bg-elevated ' +
          'border ' +
          (open ? 'border-accent' : 'border-border-subtle hover:border-border-strong') +
          ' text-[13px] text-text-primary ' +
          'transition-colors duration-150'
        }
      >
        <span className="truncate">{currentLabel}</span>
        <ChevronDown
          size={11}
          className={
            'absolute right-2.5 top-1/2 -translate-y-1/2 text-text-muted ' +
            'transition-transform duration-150 ' +
            (open ? 'rotate-180 text-text-secondary' : '')
          }
        />
      </button>

      {open && (

        <div
          role="listbox"
          className="absolute left-0 right-0 top-[calc(100%+4px)] z-50
                     rounded-md border border-border-subtle
                     bg-bg-surface shadow-z3
                     overflow-hidden"
        >
          <div className="max-h-[320px] overflow-y-auto py-1">
            {options.map(opt => {
              const active = opt.value === value;
              return (
                <button
                  key={String(opt.value)}
                  type="button"
                  role="option"
                  aria-selected={active}
                  onClick={() => { onChange(opt.value); setOpen(false); }}
                  style={{ outline: 'none' }}
                  className={
                    'w-full h-8 px-3 flex items-center gap-2 text-left text-[13px] ' +
                    'transition-colors duration-100 ' +
                    (active
                      ? 'bg-accent-soft text-text-primary font-medium'
                      : 'text-text-secondary hover:bg-glass hover:text-text-primary')
                  }
                >
                  <span className="flex-1 truncate">{opt.label}</span>
                  {active && <Check size={11} className="shrink-0 text-accent" />}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}

interface SliderProps {
  value:    number;
  min:      number;
  max:      number;
  step:     number;
  onChange: (next: number) => void;
  ticks?:   ReadonlyArray<{ value: number; label?: string }>;
  format?:  (v: number) => string;
}

export function Slider({ value, min, max, step, onChange, ticks, format }: SliderProps) {
  const pct = ((value - min) / (max - min)) * 100;
  const display = format ? format(value) : value.toFixed(2);
  return (
    <div className="w-full space-y-1.5">
      <div className="flex items-center gap-3">
        <div className="relative flex-1 h-5 flex items-center">
          {}
          <div className="absolute inset-x-0 top-1/2 -translate-y-1/2 h-[3px] rounded-full bg-track overflow-hidden pointer-events-none">
            <div
              className="h-full bg-accent transition-[width] duration-75 ease-linear"
              style={{ width: `${pct}%` }}
            />
          </div>
          <span
            aria-hidden
            className="absolute top-1/2 -translate-y-1/2 -translate-x-1/2 w-[13px] h-[13px] rounded-full bg-white pointer-events-none transition-[left] duration-75 ease-linear"
            style={{
              left: `${pct}%`,
              boxShadow: '0 1px 3px rgba(0,0,0,0.25), 0 0 0 0.5px rgba(0,0,0,0.10)',
            }}
          />
          <input
            type="range"
            min={min}
            max={max}
            step={step}
            value={value}
            onChange={e => onChange(Number(e.target.value))}
            className="relative w-full h-5 opacity-0 cursor-pointer z-10"
            style={{ outline: 'none' }}
          />
        </div>
        <span className="shrink-0 w-12 text-right text-[12px] text-text-muted tabular-nums">
          {display}
        </span>
      </div>
      {ticks && ticks.length > 0 && (
        <div className="relative h-3 mt-0.5 mr-[60px]">
          {ticks.map(t => {
            const tickPct = ((t.value - min) / (max - min)) * 100;
            return (
              <span
                key={t.value}
                className="absolute -translate-x-1/2 text-[10px] text-text-muted tabular-nums"
                style={{ left: `${tickPct}%` }}
              >
                {t.label ?? t.value}
              </span>
            );
          })}
        </div>
      )}
    </div>
  );
}

interface NumberInputProps {
  value:    number;
  onChange: (next: number) => void;
  min?:     number;
  max?:     number;
  suffix?:  string;
}

export function NumberInput({ value, onChange, min, max, suffix }: NumberInputProps) {
  return (
    <div className="relative w-full">
      <input
        type="number"
        value={value}
        min={min}
        max={max}
        onChange={e => onChange(Number(e.target.value))}
        style={{ outline: 'none' }}
        className="w-full h-8 px-3 rounded-md
                   bg-bg-elevated-soft hover:bg-bg-elevated
                   border border-border-subtle hover:border-border-strong
                   text-[13px] text-text-primary tabular-nums
                   focus:border-accent focus:bg-bg-elevated
                   transition-colors duration-150"
      />
      {suffix && (
        <span className="absolute right-2.5 top-1/2 -translate-y-1/2 text-[10px] uppercase tracking-[0.14em] text-text-muted pointer-events-none">
          {suffix}
        </span>
      )}
    </div>
  );
}
