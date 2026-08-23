import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { AlertTriangle, Loader2 } from 'lucide-react';
import { bridge } from '@/bridge';
import { GlassPanel, EASE_DEPTH } from '@/design';

export function ExitBlockedModal() {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [closing, setClosing] = useState(false);

  useEffect(() => {
    const handler = () => setOpen(true);
    bridge.events.on('app:criticalOpExitBlocked', handler);
    return () => bridge.events.off('app:criticalOpExitBlocked', handler);
  }, []);

  const onForceClose = async () => {
    setClosing(true);
    try {
      await bridge.forceExit();

    } catch (e) {
      console.warn('[exit-blocked] forceExit failed', e);
      setClosing(false);
    }
  };

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.24 }}
          className="fixed inset-0 z-[100] bg-black/60 backdrop-blur-sm flex items-center justify-center p-6"
          onClick={() => !closing && setOpen(false)}
        >
          <motion.div
            initial={{ opacity: 0, scale: 0.96, y: 8 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96, y: 8 }}
            transition={{ duration: 0.32, ease: EASE_DEPTH }}
            onClick={(e) => e.stopPropagation()}
          >
            <GlassPanel
              depth="z3"
              tint="ultra"
              rounded="3xl"
              highlight
              edge
              className="relative overflow-hidden border border-white/[0.08] w-full max-w-[480px]"
            >
              <span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
              />
              <span
                aria-hidden
                className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
                style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
              />
              <div className="relative p-6 flex flex-col gap-4">
              <div className="flex items-start gap-3">
                <AlertTriangle size={22} style={{ color: 'var(--status-warning)' }} className="shrink-0 mt-0.5" />
                <div className="flex-1 min-w-0">
                  <h2 className="text-base font-bold text-text-primary mb-1.5">
                    {t('exitBlocked.title', 'Сейчас идёт критическая операция')}
                  </h2>
                  <p className="text-sm text-text-secondary leading-relaxed">
                    {t(
                      'exitBlocked.body',
                      'Лаунчер скачивает или записывает файлы GTA. Закрытие прямо сейчас может оставить недокачанные файлы - в следующий раз бэкап начнёт с нуля.',
                    )}
                  </p>
                  <p className="text-sm text-text-secondary leading-relaxed mt-2">
                    {t(
                      'exitBlocked.hint',
                      'Подожди завершения (3D-индикатор внизу справа), или закрой принудительно - операция будет отменена.',
                    )}
                  </p>
                </div>
              </div>

              <div className="flex flex-col gap-2 pt-1">
                <button
                  type="button"
                  onClick={() => setOpen(false)}
                  disabled={closing}
                  className="px-4 py-3 rounded-xl bg-accent text-text-on-accent text-sm
                             hover:bg-accent-hover text-center
                             shadow-z2 transition-all duration-200 ease-depth
                             disabled:opacity-50"
                >
                  <span className="font-bold uppercase tracking-wider">{t('exitBlocked.wait', 'Подождать')}</span>
                </button>
                <button
                  type="button"
                  onClick={onForceClose}
                  disabled={closing}
                  className="px-4 py-3 rounded-xl border border-glass-border bg-glass text-sm
                             text-text-primary hover:bg-glass-strong text-center
                             transition-colors duration-200
                             disabled:opacity-50
                             inline-flex items-center justify-center gap-2"
                >
                  {closing && <Loader2 size={14} className="animate-spin" />}
                  <span className="font-bold uppercase tracking-wider">
                    {closing
                      ? t('exitBlocked.closing', 'Закрываем...')
                      : t('exitBlocked.forceClose', 'Принудительно закрыть')}
                  </span>
                </button>
              </div>
              </div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
