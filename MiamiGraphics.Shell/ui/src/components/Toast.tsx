import { useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { Check, X, AlertTriangle, Info } from 'lucide-react';
import { EASE_DEPTH } from '@/design';
import { useToastPresenceStore } from '@/store/toastPresenceStore';

export type ToastTone = 'success' | 'error' | 'warning' | 'info';

interface ToastProps {
  open: boolean;
  tone?: ToastTone;
  message: string;
  onClose: () => void;
  autoCloseMs?: number;
}

const ICONS = {
  success: Check,
  error:   X,
  warning: AlertTriangle,
  info:    Info,
};

const TONE_VAR: Record<ToastTone, string> = {
  success: 'var(--status-success)',
  error:   'var(--status-error)',
  warning: 'var(--status-warning)',
  info:    'var(--status-info)',
};

export function Toast({ open, tone = 'success', message, onClose, autoCloseMs = 4000 }: ToastProps) {
  const { t } = useTranslation();
  useEffect(() => {
    if (!open) return;
    const id = window.setTimeout(onClose, autoCloseMs);
    return () => window.clearTimeout(id);
  }, [open, onClose, autoCloseMs]);

  useEffect(() => {
    if (!open) return;
    useToastPresenceStore.getState().enter();
    return () => useToastPresenceStore.getState().leave();
  }, [open]);

  const Icon = ICONS[tone];
  const accent = TONE_VAR[tone];

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ opacity: 0, y: 18, scale: 0.97 }}
          animate={{ opacity: 1, y: 0,  scale: 1    }}
          exit   ={{ opacity: 0, y: 18, scale: 0.97 }}
          transition={{ duration: 0.34, ease: EASE_DEPTH }}
          className="fixed bottom-6 right-6 z-[140] max-w-[420px] min-w-[320px]"
        >
          <div
            className="relative flex items-center gap-3 px-4 py-3.5 rounded-2xl backdrop-blur-glass-heavy"
            style={{
              background: 'color-mix(in srgb, var(--bg-elevated) 92%, transparent)',
              boxShadow:
                `0 0 0 1px color-mix(in srgb, ${accent} 38%, transparent), `
                + `0 0 0 5px color-mix(in srgb, ${accent} 7%, transparent), `
                + `0 22px 50px -18px color-mix(in srgb, ${accent} 40%, transparent), `
                + `0 10px 22px -12px rgba(0,0,0,0.45), `
                + `inset 0 1px 0 rgba(255,255,255,0.06)`,
            }}
          >
            {}
            <span
              className="shrink-0 inline-flex items-center justify-center w-9 h-9 rounded-xl"
              style={{
                background: `color-mix(in srgb, ${accent} 18%, transparent)`,
                boxShadow: `inset 0 0 0 1px color-mix(in srgb, ${accent} 38%, transparent)`,
                color: accent,
              }}
            >
              <Icon size={16} strokeWidth={2.5} />
            </span>

            {}
            <span className="flex-1 min-w-0 text-[13px] text-text-primary leading-snug pr-1">
              {message}
            </span>

            {}
            <button
              type="button"
              onClick={onClose}
              aria-label={t('common.close', 'Закрыть')}
              style={{ outline: 'none' }}
              className="shrink-0 inline-flex items-center justify-center w-7 h-7 rounded-lg
                         text-text-muted
                         hover:bg-white/[0.08] hover:text-text-primary
                         transition-colors duration-150"
            >
              <X size={13} />
            </button>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
