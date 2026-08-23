import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { AlertCircle, X } from 'lucide-react';
import { bridge } from '@/bridge';
import { GlassPanel, EASE_DEPTH } from '@/design';

export function GameRunningModal() {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const handler = () => setOpen(true);
    bridge.events.on('app:gtaRunning', handler);
    return () => bridge.events.off('app:gtaRunning', handler);
  }, []);

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.24 }}
          className="fixed inset-0 z-[100] bg-black/60 backdrop-blur-sm flex items-center justify-center p-6"
          onClick={() => setOpen(false)}
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
              className="relative overflow-hidden border border-white/[0.08] w-full max-w-[420px]"
            >
              <span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
              />
              <span
                aria-hidden
                className="absolute -top-24 left-1/2 -translate-x-1/2 w-72 h-72 pointer-events-none blur-3xl"
                style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
              />

              <button
                type="button"
                onClick={() => setOpen(false)}
                aria-label={t('common.close', 'Закрыть')}
                className="absolute top-3 right-3 z-20 inline-flex items-center justify-center
                           w-8 h-8 rounded-lg text-text-muted hover:bg-white/[0.08]
                           hover:text-text-primary transition-colors duration-150"
                style={{ outline: 'none' }}
              >
                <X size={15} />
              </button>

              <div className="relative px-7 pt-9 pb-7 flex flex-col items-center text-center gap-1">
                <span
                  className="inline-flex items-center justify-center w-16 h-16 rounded-2xl mb-3"
                  style={{
                    background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
                    boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 34%, transparent)',
                    color: 'var(--text-primary)',
                  }}
                >
                  <AlertCircle size={30} strokeWidth={2.2} />
                </span>

                <h2 className="text-lg font-bold text-text-primary">{t('gameRunning.title', 'Внимание')}</h2>
                <p className="text-[12px] font-semibold text-text-secondary mb-3">
                  {t('gameRunning.subtitle', 'Запущен процесс GTA5.exe')}
                </p>
                <p className="text-sm text-text-secondary leading-relaxed max-w-[320px]">
                  {t('gameRunning.body', 'Мы заметили, что GTA V запущен прямо сейчас. Заверши игру, чтобы не столкнуться с ошибками при установке модификаций.')}
                </p>

                <button
                  type="button"
                  onClick={() => setOpen(false)}
                  className="mt-6 w-full px-4 py-3 rounded-xl bg-white text-black text-sm
                             hover:bg-white/90 transition-colors duration-200"
                >
                  <span className="font-bold">{t('common.continue', 'Продолжить')}</span>
                </button>
              </div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
