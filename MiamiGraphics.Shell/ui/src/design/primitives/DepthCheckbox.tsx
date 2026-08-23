import { motion } from 'framer-motion';
import { Check } from 'lucide-react';
import { clsx } from 'clsx';
import { type ReactNode } from 'react';
import { SPRING_PRESS, EASE_DEPTH } from '../tokens';

interface DepthCheckboxProps {
  checked:    boolean;
  onChange:   (next: boolean) => void;
  label?:     ReactNode;
  disabled?:  boolean;
  className?: string;
  size?:      'sm' | 'md';
}

export function DepthCheckbox({
  checked, onChange, label, disabled, className, size = 'sm',
}: DepthCheckboxProps) {
  const dim = size === 'sm'
    ? { box: 'w-4 h-4',         icon: 12, stroke: 3 }
    : { box: 'w-[18px] h-[18px]', icon: 14, stroke: 2.75 };

  return (
    <label
      className={clsx(
        'group inline-flex items-center gap-2.5 cursor-pointer select-none',
        disabled && 'opacity-60 cursor-not-allowed',
        className,
      )}
    >
      {}
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        disabled={disabled}
        className="sr-only peer"
      />

      {}
      <motion.span
        aria-hidden
        whileTap={!disabled ? { scale: 0.86 } : undefined}
        transition={SPRING_PRESS}
        className={clsx(
          'relative shrink-0 rounded-md flex items-center justify-center',
          dim.box,
          'transition-[background-color,border-color,box-shadow] duration-200 ease-depth',

          'peer-focus-visible:ring-2 peer-focus-visible:ring-accent peer-focus-visible:ring-offset-2 peer-focus-visible:ring-offset-bg-base',
          checked
            ? 'border border-accent ' +
              'bg-gradient-to-br from-accent to-accent-hover ' +

              'shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_55%,transparent),0_4px_14px_-2px_color-mix(in_srgb,var(--accent)_60%,transparent)]'
            : 'border border-[color-mix(in_srgb,var(--accent)_22%,transparent)] bg-glass-strong ' +
              'group-hover:border-[color-mix(in_srgb,var(--accent)_55%,transparent)] ' +
              'group-hover:shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_22%,transparent),0_6px_18px_-8px_color-mix(in_srgb,var(--accent)_45%,transparent)]',
        )}
      >
        {}
        {checked && (
          <motion.span
            initial={{ opacity: 0, scale: 0.4, rotate: -8 }}
            animate={{ opacity: 1, scale: 1,    rotate: 0 }}
            transition={{ type: 'spring', stiffness: 520, damping: 22 }}
            className="text-white drop-shadow-[0_0_2px_rgba(0,0,0,0.20)]"
          >
            <Check size={dim.icon} strokeWidth={dim.stroke} />
          </motion.span>
        )}
      </motion.span>

      {label && (
        <motion.span

          whileHover={!disabled ? { x: 1 } : undefined}
          transition={{ duration: 0.15, ease: EASE_DEPTH }}
          className={clsx(
            'text-sm transition-colors duration-200',
            checked ? 'text-text-primary' : 'text-text-secondary',
            'group-hover:text-text-primary',
          )}
        >
          {label}
        </motion.span>
      )}
    </label>
  );
}
