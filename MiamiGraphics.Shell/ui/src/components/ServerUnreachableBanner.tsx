import { useEffect, useState } from 'react';
import { useTranslation, Trans } from 'react-i18next';
import { AlertTriangle, X } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { useUiStore } from '@/store/uiStore';
import { useInstallProgressStore } from '@/store/installProgressStore';
import { bridge } from '@/bridge';

export function ServerUnreachableBanner({ onOpenSettings }: { onOpenSettings?: () => void }) {
  const { t } = useTranslation();
  const initialized = useUiStore(s => s.initialized);

  const [slowBoot, setSlowBoot] = useState(false);
  useEffect(() => {
    if (initialized) return;
    const id = window.setTimeout(() => setSlowBoot(true), 25_000);
    return () => window.clearTimeout(id);
  }, [initialized]);

  const [failStreak, setFailStreak] = useState(0);
  useEffect(() => {
    if (!initialized) return;
    let cancelled = false;
    const check = async () => {
      const busy = Object.values(useInstallProgressStore.getState().byId)
        .some(e => e.phase !== 'done' && e.phase !== 'error');
      if (busy) return;
      try {
        const status = await bridge.getServerStatus();
        if (cancelled) return;
        if (status.reachable) setFailStreak(0);
        else setFailStreak(n => n + 1);
      } catch {
        if (!cancelled) setFailStreak(n => n + 1);
      }
    };
    void check();
    const id = window.setInterval(check, 30_000);
    return () => { cancelled = true; window.clearInterval(id); };
  }, [initialized]);

  const [dismissed, setDismissed] = useState(false);

  const active = !dismissed && (
    (slowBoot && !initialized) ||
    (initialized && failStreak >= 2)
  );

  return (
    <AnimatePresence>
      {active && (
        <motion.div
          key="server-warn"
          initial={{ opacity: 0, y: -16 }}
          animate={{ opacity: 1, y: 0 }}
          exit   ={{ opacity: 0, y: -16 }}
          transition={{ duration: 0.28 }}
          className="fixed top-12 left-1/2 -translate-x-1/2 z-[90]
                     w-[min(640px,90vw)] rounded-2xl px-4 py-3
                     flex items-start gap-3 shadow-z3 backdrop-blur-glass-ultra"
          style={{
            background: 'color-mix(in srgb, var(--status-warning) 16%, var(--bg-elevated))',
            border: '1px solid color-mix(in srgb, var(--status-warning) 45%, transparent)',
          }}
        >
          <AlertTriangle size={16} className="shrink-0 mt-0.5"
                         style={{ color: 'var(--status-warning)' }} />
          <div className="flex-1 min-w-0">
            <div className="text-[13px] font-bold text-text-primary leading-tight">
              {t('serverBanner.title', 'Не получается связаться с нашим сервером')}
            </div>
            <div className="text-[11.5px] text-text-secondary leading-snug mt-0.5">
              <Trans
                i18nKey="serverBanner.body"
                defaults="Лаунчер не достучался ни до основного (европейского) хоста, ни до запасного. Чаще всего путь до нашего домена режет провайдер, DPI или включённый VPN - сам сервер при этом жив. Проверь интернет и выключи VPN, а потом открой <b>Настройки → Серверы и сеть</b>: там видно, какой хост отвечает, и там же меняется регион (в России - <b>RU</b>)."
                components={{ b: <b /> }}
              />
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {onOpenSettings && (
              <button
                type="button"
                onClick={() => { onOpenSettings(); setDismissed(true); }}
                className="text-[11px] font-bold uppercase tracking-wider px-2.5 py-1 rounded-md
                           bg-accent-soft text-text-primary hover:bg-[color-mix(in_srgb,var(--accent)_22%,transparent)]
                           transition-colors"
                style={{ outline: 'none' }}
              >
                {t('serverBanner.settings', 'Настройки')}
              </button>
            )}
            <button
              type="button"
              onClick={() => setDismissed(true)}
              aria-label={t('common.hide', 'Скрыть')}
              className="w-7 h-7 rounded-md flex items-center justify-center
                         text-text-muted hover:text-text-primary hover:bg-bg-elevated
                         transition-colors"
              style={{ outline: 'none' }}
            >
              <X size={13} />
            </button>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
