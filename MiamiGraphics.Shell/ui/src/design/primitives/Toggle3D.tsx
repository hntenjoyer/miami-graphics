import { motion } from 'framer-motion';
import { clsx } from 'clsx';
import { SPRING_TOGGLE } from '../tokens';

interface Toggle3DProps {
  checked:    boolean;
  onChange:   (next: boolean) => void;
  disabled?:  boolean;
  ariaLabel?: string;
}

export function Toggle3D({ checked, onChange, disabled, ariaLabel }: Toggle3DProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={ariaLabel}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={clsx(
        'relative inline-flex shrink-0 items-center',
        'w-11 h-6 rounded-full',
        'transition-colors duration-300 ease-depth',
        'shadow-z1',

        'before:absolute before:inset-0 before:rounded-full',
        'before:shadow-[inset_0_1px_2px_rgba(0,0,0,0.20)] before:pointer-events-none',

        checked
          ? 'bg-accent shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_55%,transparent),0_4px_14px_-2px_color-mix(in_srgb,var(--accent)_55%,transparent)]'
          : 'bg-glass-strong border border-[color-mix(in_srgb,var(--accent)_22%,transparent)] hover:border-[color-mix(in_srgb,var(--accent)_45%,transparent)]',
        disabled && 'opacity-50 cursor-not-allowed',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-offset-2 focus-visible:ring-offset-bg-base',
      )}
    >
      <motion.span
        aria-hidden
        layout
        animate={{ x: checked ? 22 : 2 }}
        transition={SPRING_TOGGLE}
        className={clsx(
          'absolute top-0.5 left-0',
          'w-5 h-5 rounded-full',
          'bg-white',
          'shadow-z1',

          'before:absolute before:inset-0 before:rounded-full',
          'before:shadow-[inset_0_1px_0_rgba(255,255,255,0.6),inset_0_-1px_2px_rgba(0,0,0,0.10)]',
        )}
      />
    </button>
  );
}
