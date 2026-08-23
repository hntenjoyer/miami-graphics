import { useId } from 'react';
import { motion, LayoutGroup } from 'framer-motion';
import type { LucideIcon } from 'lucide-react';

export interface SegmentedOption<T extends string> {
  value: T;
  label: string;
  icon?: LucideIcon;
}

interface SegmentedControlProps<T extends string> {
  options:  readonly SegmentedOption<T>[];
  value:    T;
  onChange: (next: T) => void;
  ariaLabel: string;
}

export function SegmentedControl<T extends string>({
  options, value, onChange, ariaLabel,
}: SegmentedControlProps<T>) {

  const groupId = useId();

  return (
    <LayoutGroup id={groupId}>
      <div
        role="radiogroup"
        aria-label={ariaLabel}
        className="inline-flex items-center gap-1 p-1 rounded-xl
                   bg-bg-elevated border border-border-subtle"
      >
        {options.map(opt => {
          const Icon = opt.icon;
          const active = opt.value === value;
          return (
            <button
              key={opt.value}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => onChange(opt.value)}
              className={
                'relative flex items-center gap-2 px-3 py-1.5 rounded-lg text-sm font-medium ' +
                'transition-colors duration-200 ease-depth outline-none ' +
                'focus-visible:shadow-[0_0_0_3px_var(--accent-soft)] ' +
                (active
                  ? 'text-text-primary'
                  : 'text-text-secondary hover:text-text-primary')
              }
            >
              {active && (
                <motion.span
                  layoutId={`segmented-pill-${groupId}`}
                  aria-hidden
                  className="absolute inset-0 rounded-lg bg-bg-surface"
                  style={{
                    boxShadow:
                      '0 0 0 1px color-mix(in srgb, var(--accent) 28%, transparent),' +
                      '0 4px 14px -4px color-mix(in srgb, var(--accent) 35%, transparent)',
                  }}
                  transition={{ type: 'spring', stiffness: 480, damping: 36, mass: 0.7 }}
                />
              )}
              <span className="relative z-10 inline-flex items-center gap-2">
                {Icon && <Icon size={15} className="shrink-0" />}
                <span>{opt.label}</span>
              </span>
            </button>
          );
        })}
      </div>
    </LayoutGroup>
  );
}
