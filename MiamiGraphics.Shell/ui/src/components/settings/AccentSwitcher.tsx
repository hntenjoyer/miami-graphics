import { Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useUiStore } from '@/store/uiStore';
import type { AccentColor } from '@/bridge/types';

const CURATED: { value: AccentColor; swatch: string; labelKey: string }[] = [
  { value: 'violet', swatch: '#7c3aed', labelKey: 'settings.accent.violet' },
  { value: 'blue',   swatch: '#3b82f6', labelKey: 'settings.accent.blue'   },
  { value: 'slate',  swatch: '#94a3b8', labelKey: 'settings.accent.slate'  },
];

export function AccentSwitcher() {
  const { t } = useTranslation();
  const accent    = useUiStore(s => s.settings.accentColor);
  const setAccent = useUiStore(s => s.setAccent);

  return (
    <div
      role="radiogroup"
      aria-label={t('settings.accent.ariaLabel')}
      className="inline-flex items-center gap-2 p-1 rounded-xl bg-bg-elevated border border-glass-border"
    >
      {CURATED.map(opt => {
        const isActive = accent === opt.value;
        return (
          <button
            key={opt.value}
            type="button"
            role="radio"
            aria-checked={isActive}
            aria-label={t(opt.labelKey)}
            title={t(opt.labelKey)}
            onClick={() => { void setAccent(opt.value); }}
            style={{ outline: 'none' }}
            className={
              'relative w-8 h-8 rounded-lg flex items-center justify-center ' +
              'transition-transform duration-200 ease-smooth ' +
              (isActive
                ? 'scale-110'
                : 'hover:scale-105 opacity-65 hover:opacity-100')
            }
          >
            <span
              aria-hidden={!isActive}
              className="relative flex items-center justify-center w-6 h-6 rounded-md
                         shadow-[inset_0_1px_2px_rgba(0,0,0,0.3),0_2px_8px_-2px_rgba(0,0,0,0.4)]"
              style={{ background: opt.swatch }}
            >
              {isActive && (
                <Check size={12} strokeWidth={3} className="text-white drop-shadow" />
              )}
            </span>
          </button>
        );
      })}
    </div>
  );
}
