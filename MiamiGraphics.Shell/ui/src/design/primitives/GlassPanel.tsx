import { forwardRef, type HTMLAttributes } from 'react';
import { clsx } from 'clsx';

interface GlassPanelProps extends HTMLAttributes<HTMLDivElement> {
  depth?: 'z1' | 'z2' | 'z3';
  tint?: 'soft' | 'strong' | 'ultra' | 'tinted';
  highlight?: boolean;
  edge?: boolean;
  rounded?: 'md' | 'lg' | 'xl' | '2xl' | '3xl';
}

const ROUNDED = {
  'md':  'rounded-xl',
  'lg':  'rounded-2xl',
  'xl':  'rounded-[20px]',
  '2xl': 'rounded-3xl',
  '3xl': 'rounded-[32px]',
};

const SHADOW = {
  z1: 'shadow-z1',
  z2: 'shadow-z2',
  z3: 'shadow-z3',
};

const TINT_BG = {
  soft:   'bg-glass',
  strong: 'bg-glass-strong',
  ultra:  'bg-glass-ultra',
  tinted: 'bg-glass-tinted',
};

const TINT_BLUR = {
  soft:   'backdrop-blur-glass backdrop-saturate-150',
  strong: 'backdrop-blur-glass backdrop-saturate-150',

  ultra:  'backdrop-blur-glass-ultra backdrop-saturate-liquid',
  tinted: 'backdrop-blur-glass-heavy backdrop-saturate-liquid',
};

const TINT_BORDER = {
  soft:   'border border-glass-border',
  strong: 'border border-glass-border',

  ultra:  '',
  tinted: '',
};

export const GlassPanel = forwardRef<HTMLDivElement, GlassPanelProps>(function GlassPanel(
  {
    depth = 'z2',
    tint = 'soft',
    highlight = true,
    edge,
    rounded = '2xl',
    className,
    children,
    ...rest
  },
  ref,
) {

  const showEdge = edge ?? (tint === 'ultra' || tint === 'tinted');
  return (
    <div
      ref={ref}
      className={clsx(
        'relative isolate',
        ROUNDED[rounded],
        SHADOW[depth],
        TINT_BLUR[tint],
        TINT_BG[tint],
        TINT_BORDER[tint],
        className,
      )}
      {...rest}
    >
      {}
      {highlight && (
        <span
          aria-hidden
          className={clsx(
            'pointer-events-none absolute inset-x-0 top-0 h-px',
            ROUNDED[rounded],
            tint === 'ultra' || tint === 'tinted'
              ? 'bg-gradient-to-r from-transparent via-white/30 to-transparent'
              : 'bg-gradient-to-r from-transparent via-glass-highlight to-transparent',
          )}
        />
      )}
      {}
      {showEdge && (
        <span
          aria-hidden
          className={clsx(
            'pointer-events-none absolute inset-x-0 bottom-0 h-px',
            ROUNDED[rounded],
            'bg-gradient-to-r from-transparent via-glass-edge-dim to-transparent',
          )}
        />
      )}
      {children}
    </div>
  );
});
