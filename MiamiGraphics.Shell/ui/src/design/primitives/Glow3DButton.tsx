import { motion } from 'framer-motion';
import { clsx } from 'clsx';
import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { EASE_DEPTH, SPRING_PRESS } from '../tokens';
import { AccentLoader } from './AccentLoader';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
type Size    = 'sm' | 'md' | 'lg';

interface Glow3DButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  children:  ReactNode;
  variant?:  Variant;
  size?:     Size;
  busy?:     boolean;
  leading?:  ReactNode;
  trailing?: ReactNode;
  fullWidth?: boolean;
}

const SIZE = {
  sm: 'h-9  px-4 text-sm   font-semibold rounded-xl',
  md: 'h-11 px-5 text-sm   font-semibold rounded-2xl',

  lg: 'h-14 px-8 text-base font-bold     rounded-2xl tracking-tight',
};

const VARIANT_BASE: Record<Variant, string> = {

  primary: clsx(
    'text-text-on-accent',
    'bg-[color-mix(in_srgb,var(--accent)_82%,transparent)]',
    'border border-[color-mix(in_srgb,var(--accent)_70%,transparent)]',
    'shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_38%,transparent),0_8px_28px_-6px_color-mix(in_srgb,var(--accent)_55%,transparent),0_2px_8px_-2px_rgba(0,0,0,0.30)]',
    'hover:bg-[color-mix(in_srgb,var(--accent)_92%,transparent)]',
    'hover:border-[color-mix(in_srgb,var(--accent)_92%,transparent)]',
    'hover:shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_60%,transparent),0_14px_42px_-6px_color-mix(in_srgb,var(--accent)_75%,transparent),0_2px_10px_-2px_rgba(0,0,0,0.35)]',
  ),

  secondary: clsx(
    'text-text-primary bg-glass-strong backdrop-blur-glass backdrop-saturate-150',
    'border border-[color-mix(in_srgb,var(--accent)_42%,transparent)]',
    'shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_18%,transparent),0_6px_22px_-8px_color-mix(in_srgb,var(--accent)_45%,transparent)]',
    'hover:border-[color-mix(in_srgb,var(--accent)_75%,transparent)]',
    'hover:shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_45%,transparent),0_12px_36px_-8px_color-mix(in_srgb,var(--accent)_70%,transparent)]',
  ),

  ghost: clsx(
    'text-text-secondary border border-transparent',
    'hover:text-text-primary hover:bg-glass',
    'hover:border-[color-mix(in_srgb,var(--accent)_35%,transparent)]',
    'hover:shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_20%,transparent),0_8px_24px_-10px_color-mix(in_srgb,var(--accent)_45%,transparent)]',
  ),

  danger: clsx(
    'text-text-primary bg-glass-strong backdrop-blur-glass backdrop-saturate-150',
    'border border-[color-mix(in_srgb,var(--status-error)_50%,transparent)]',
    'shadow-[0_0_0_1px_color-mix(in_srgb,var(--status-error)_20%,transparent),0_6px_22px_-8px_color-mix(in_srgb,var(--status-error)_45%,transparent)]',
    'hover:border-[color-mix(in_srgb,var(--status-error)_82%,transparent)]',
    'hover:shadow-[0_0_0_1px_color-mix(in_srgb,var(--status-error)_50%,transparent),0_12px_36px_-8px_color-mix(in_srgb,var(--status-error)_70%,transparent)]',
  ),
};

export const Glow3DButton = forwardRef<HTMLButtonElement, Glow3DButtonProps>(function Glow3DButton(
  {
    children, className, variant = 'primary', size = 'md', busy, leading, trailing,
    fullWidth, disabled, ...rest
  },
  ref,
) {
  const isDisabled = disabled || busy;
  return (
    <motion.button
      ref={ref}
      disabled={isDisabled}
      whileHover={!isDisabled ? { y: -2, transition: { duration: 0.22, ease: EASE_DEPTH } } : undefined}
      whileTap={!isDisabled ? { y: 1, scale: 0.985, transition: SPRING_PRESS } : undefined}
      className={clsx(
        'group relative inline-flex items-center justify-center gap-2 overflow-hidden',
        'whitespace-nowrap',
        'transition-[box-shadow,background-color,border-color,color] duration-300 ease-depth',
        'focus-visible:outline-none focus-visible:shadow-[0_0_0_3px_var(--accent-soft),0_0_0_1px_var(--accent)]',
        'disabled:opacity-50 disabled:cursor-not-allowed',
        SIZE[size],
        VARIANT_BASE[variant],
        fullWidth && 'w-full',
        className,
      )}
      {...rest as object}
    >
      {}
      {(variant === 'primary' || variant === 'danger') && !isDisabled && (
        <span
          aria-hidden
          className="pointer-events-none absolute inset-0 rounded-[inherit] overflow-hidden"
        >
          <span
            className="absolute inset-y-0 -left-1/3 w-1/3 -skew-x-12
                       bg-gradient-to-r from-transparent via-white/18 to-transparent
                       opacity-0 group-hover:opacity-100
                       transition-transform duration-700 ease-depth
                       translate-x-[-100%] group-hover:translate-x-[400%]"
          />
        </span>
      )}

      {}
      {variant === 'primary' && (
        <span
          aria-hidden
          className="pointer-events-none absolute inset-0 rounded-[inherit]"
          style={{
            boxShadow:
              'inset 0 1px 0 color-mix(in srgb, var(--accent-hover) 55%, transparent),' +
              'inset 0 -10px 24px -8px color-mix(in srgb, var(--accent-pressed) 65%, transparent)',
          }}
        />
      )}

      {}
      <span className="relative z-10 inline-flex items-center gap-2">
        {busy ? (

          <AccentLoader size={18} color="currentColor" />
        ) : (
          <>
            {leading}
            <span>{children}</span>
            {trailing}
          </>
        )}
      </span>
    </motion.button>
  );
});
