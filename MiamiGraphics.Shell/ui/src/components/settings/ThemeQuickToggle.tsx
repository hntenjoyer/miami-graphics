import { Sun, Moon, type LucideIcon } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useTheme, type ThemeMode } from '@/contexts/SettingsContext';
import { motion } from 'framer-motion';
import { EASE_DEPTH } from '@/design';

interface ThemeQuickToggleProps {
  size?: 'compact' | 'comfortable';
}

const ORDER: { value: ThemeMode; icon: LucideIcon; key: string }[] = [
  { value: 'light', icon: Sun,  key: 'settings.theme.light' },
  { value: 'dark',  icon: Moon, key: 'settings.theme.dark'  },
];

export function ThemeQuickToggle({ size = 'compact' }: ThemeQuickToggleProps) {
  const { t } = useTranslation();
  const { theme, setTheme } = useTheme();

  const btn = size === 'compact' ? 'w-7 h-6' : 'w-9 h-8';
  const ico = size === 'compact' ? 12 : 14;

  return (
    <div
      role="radiogroup"
      aria-label={t('settings.theme.ariaLabel')}
      className="inline-flex items-center gap-0.5 p-0.5 rounded-lg
                 bg-glass-strong border border-glass-border"
    >
      {ORDER.map(opt => {
        const Icon = opt.icon;
        const active = opt.value === theme;
        return (
          <motion.button
            key={opt.value}
            type="button"
            role="radio"
            aria-checked={active}
            title={t(opt.key)}
            onClick={() => setTheme(opt.value)}
            whileTap={{ scale: 0.92 }}
            transition={{ duration: 0.12, ease: EASE_DEPTH }}
            className={
              btn + ' rounded-md flex items-center justify-center ' +
              'transition-colors duration-200 ease-depth ' +
              (active
                ? 'bg-accent text-text-on-accent shadow-glow-accent'
                : 'text-text-muted hover:text-text-primary hover:bg-glass')
            }
          >
            <Icon size={ico} />
          </motion.button>
        );
      })}
    </div>
  );
}
