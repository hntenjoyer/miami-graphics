import { motion } from 'framer-motion';
import { Check, ChevronRight } from 'lucide-react';
import type { ReactNode } from 'react';

export type ArrowButtonProps = {
  label: string;
  onClick?: () => void;
  type?: 'button' | 'submit';
  isFinish?: boolean;
  leadingIcon?: ReactNode;
  busy?: boolean;
  disabled?: boolean;
  fullWidth?: boolean;
};

export function ArrowButton({
  label, onClick, type = 'button',
  isFinish, leadingIcon, busy, disabled, fullWidth,
}: ArrowButtonProps) {
  const inert = busy || disabled;
  return (
    <motion.button
      type={type}
      onClick={onClick}
      disabled={inert}
      initial={false}
      whileHover={inert ? undefined : { scale: 1.02 }}
      whileTap={inert ? undefined : { scale: 0.985 }}
      transition={{ type: 'spring', stiffness: 380, damping: 24 }}
      style={{ outline: 'none' }}
      className={
        'group relative inline-flex items-center justify-center gap-3 ' +
        (fullWidth ? 'w-full ' : '') +
        'pl-7 pr-5 py-3 rounded-2xl ' +
        'bg-[color-mix(in_srgb,var(--accent-soft)_55%,transparent)] ' +
        'backdrop-blur-glass ' +
        'border border-[color-mix(in_srgb,var(--accent)_35%,transparent)] ' +
        'hover:border-[color-mix(in_srgb,var(--accent)_70%,transparent)] ' +
        'transition-[border-color,box-shadow,opacity] duration-300 ease-smooth ' +
        'shadow-[0_8px_32px_-12px_color-mix(in_srgb,var(--accent)_40%,transparent)] ' +
        'hover:shadow-[0_12px_44px_-12px_color-mix(in_srgb,var(--accent)_70%,transparent)] ' +
        'disabled:opacity-60 disabled:cursor-wait'
      }
    >
      {}
      <span
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 rounded-2xl
                   bg-[radial-gradient(circle_at_50%_50%,color-mix(in_srgb,var(--accent)_25%,transparent),transparent_70%)]
                   opacity-60 group-hover:opacity-90 transition-opacity duration-500"
      />

      {}
      {leadingIcon && (
        <span className="relative w-9 h-9 rounded-xl flex items-center justify-center shrink-0
                         bg-[color-mix(in_srgb,var(--accent)_18%,transparent)]
                         border border-[color-mix(in_srgb,var(--accent)_25%,transparent)]
                         text-accent">
          {leadingIcon}
        </span>
      )}

      <span className="relative font-display font-bold text-[15px] uppercase tracking-[0.08em] text-text-primary">
        {label}
      </span>

      {}
      {!leadingIcon && (
        <span
          className={
            'relative w-9 h-9 rounded-xl flex items-center justify-center shrink-0 ' +
            'bg-[color-mix(in_srgb,var(--accent)_18%,transparent)] ' +
            'border border-[color-mix(in_srgb,var(--accent)_25%,transparent)] ' +
            'transition-transform duration-300 ease-smooth ' +
            (busy
              ? ''
              : isFinish
                ? 'group-hover:scale-105'
                : 'group-hover:translate-x-1.5')
          }
        >
          {busy
            ? <span className="block w-3.5 h-3.5 rounded-full border-2 border-accent border-t-transparent animate-spin" />
            : isFinish
              ? <Check size={16} strokeWidth={2.6} className="text-accent" />
              : <ChevronRight size={18} strokeWidth={2.4} className="text-accent" />}
        </span>
      )}
    </motion.button>
  );
}
