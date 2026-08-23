import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { ChevronDown, Check, type LucideIcon } from 'lucide-react';
import { AnimatePresence, motion } from 'framer-motion';
import { clsx } from 'clsx';
import { EASE_DEPTH } from '../tokens';

export interface GlassDropdownOption<T extends string> {
  value: T;
  label: string;
  icon?: LucideIcon;
}

interface Props<T extends string> {
  value:    T;
  options:  readonly GlassDropdownOption<T>[];
  onChange: (next: T) => void;
  ariaLabel: string;
  title?: string;
  width?: number | string;
  className?: string;
}

interface MenuRect {
  top:    number;
  left:   number;
  width:  number;
}

export function GlassDropdown<T extends string>({
  value, options, onChange, ariaLabel, title, width, className,
}: Props<T>) {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const [rect, setRect] = useState<MenuRect | null>(null);

  useLayoutEffect(() => {
    if (!open || !wrapRef.current) return;
    const update = () => {
      if (!wrapRef.current) return;
      const r = wrapRef.current.getBoundingClientRect();
      setRect({
        top:   r.bottom + 8,

        left:  r.left,
        width: r.width,
      });
    };
    update();
    window.addEventListener('resize', update);

    window.addEventListener('scroll', update, true);
    return () => {
      window.removeEventListener('resize', update);
      window.removeEventListener('scroll', update, true);
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: PointerEvent) => {

      const target = e.target as HTMLElement;
      if (wrapRef.current?.contains(target)) return;
      if (target.closest('[data-glass-dropdown-menu]')) return;
      setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    window.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('pointerdown', onPointerDown);
      window.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const current = options.find(o => o.value === value) ?? options[0];
  const CurrentIcon = current?.icon;

  return (
    <div
      ref={wrapRef}
      className={clsx('relative inline-block', className)}
      style={width !== undefined ? { width } : undefined}
    >
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        title={title ?? ariaLabel}
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        aria-expanded={open}
        style={{ outline: 'none' }}
        className={clsx(
          'group inline-flex items-center justify-between gap-2 w-full h-10 pl-3 pr-2.5',
          'rounded-xl border text-[12px] font-bold uppercase tracking-[0.12em]',
          'transition-all duration-300 ease-smooth cursor-pointer',
          open
            ? 'bg-white/[0.10] border-white/[0.30] text-text-primary ' +
              'shadow-[0_0_0_3px_rgba(255,255,255,0.08)]'
            : 'bg-white/[0.04] border-white/[0.06] text-text-secondary ' +
              'hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]',
        )}
      >
        <span className="flex items-center gap-2 min-w-0">
          {CurrentIcon && <CurrentIcon size={14} className="shrink-0" strokeWidth={2} />}
          <span className="truncate">{current?.label}</span>
        </span>
        <ChevronDown
          size={14}
          strokeWidth={2}
          className={clsx(
            'shrink-0 opacity-70 transition-transform duration-300 ease-smooth',
            open && 'rotate-180 opacity-100',
          )}
        />
      </button>

      {createPortal(
        <AnimatePresence>
          {open && rect && (
            <motion.div
              role="listbox"
              data-glass-dropdown-menu="true"
              initial={{ opacity: 0, y: -6, scale: 0.96 }}
              animate={{ opacity: 1, y:  0, scale: 1    }}
              exit   ={{ opacity: 0, y: -4, scale: 0.97 }}
              transition={{ duration: 0.18, ease: EASE_DEPTH }}
              style={{
                position: 'fixed',
                top:      rect.top,
                left:     rect.left,
                minWidth: rect.width,

                zIndex:   9999,
                transformOrigin: 'top left',
              }}
              className="rounded-2xl border border-white/[0.10] bg-glass-strong
                         backdrop-blur-glass-heavy backdrop-saturate-liquid
                         shadow-[0_22px_48px_-12px_rgba(0,0,0,0.55),inset_0_1px_0_rgba(255,255,255,0.08)]
                         overflow-hidden p-1.5"
            >
              {options.map(opt => {
                const Icon = opt.icon;
                const active = opt.value === value;
                return (
                  <button
                    key={opt.value}
                    type="button"
                    role="option"
                    aria-selected={active}
                    onClick={() => { onChange(opt.value); setOpen(false); }}
                    style={{ outline: 'none' }}
                    className={clsx(
                      'w-full flex items-center gap-2.5 h-9 px-3 rounded-xl text-[13px] text-left',
                      'transition-colors duration-150 ease-depth whitespace-nowrap',
                      active
                        ? 'bg-white text-black font-semibold ' +
                          'shadow-[inset_0_1px_0_rgba(255,255,255,0.85)]'
                        : 'text-text-secondary hover:bg-white/[0.08] hover:text-text-primary',
                    )}
                  >
                    {Icon && <Icon size={14} className="shrink-0" strokeWidth={2} />}
                    <span className="flex-1 truncate">{opt.label}</span>
                    {active && <Check size={14} className="shrink-0" strokeWidth={2.5} />}
                  </button>
                );
              })}
            </motion.div>
          )}
        </AnimatePresence>,
        document.body,
      )}
    </div>
  );
}
