import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { AlertTriangle, Download, X } from 'lucide-react';
import { bridge } from '@/bridge';
import type { AppUpdateInfo } from '@/bridge/types';
import type { AppUpdateProgress } from '@/bridge/IAppBridge';
import { GlassPanel, EASE_DEPTH } from '@/design';

function formatBytes(bytes: number | null, t: TFunction): string | null {
  if (!bytes || bytes <= 0) return null;
  const mb = bytes / 1024 / 1024;
  if (mb < 1024) return t('common.unitMb', '{{value}} MB', { value: mb.toFixed(1) });
  return t('common.unitGb', '{{value}} GB', { value: (mb / 1024).toFixed(2) });
}

const stagger = {
  hidden: {},
  show: { transition: { staggerChildren: 0.07, delayChildren: 0.14 } },
};
const item = {
  hidden: { opacity: 0, y: 12 },
  show: { opacity: 1, y: 0, transition: { duration: 0.45, ease: EASE_DEPTH } },
};

export function AppUpdatePrompt() {
  const { t } = useTranslation();
  const [info, setInfo] = useState<AppUpdateInfo | null>(null);
  const [dismissed, setDismissed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<AppUpdateProgress | null>(null);

  useEffect(() => {
    const onProgress = (p: AppUpdateProgress) => setProgress(p);
    bridge.events.on('app:updateProgress', onProgress);
    return () => bridge.events.off('app:updateProgress', onProgress);
  }, []);

  useEffect(() => {
    let cancelled = false;
    let lastCheck = 0;
    const runCheck = () => {
      lastCheck = Date.now();
      bridge.appUpdateCheck()
        .then(next => {
          if (!cancelled && next.updateAvailable) setInfo(next);
        })
        .catch(err => console.warn('[app-update] check failed', err));
    };

    const initial = window.setTimeout(runCheck, 5000);
    const interval = window.setInterval(runCheck, 20 * 60 * 1000);
    const onFocus = () => { if (Date.now() - lastCheck > 2 * 60 * 1000) runCheck(); };
    window.addEventListener('focus', onFocus);

    return () => {
      cancelled = true;
      window.clearTimeout(initial);
      window.clearInterval(interval);
      window.removeEventListener('focus', onFocus);
    };
  }, []);

  const open = !!info && !dismissed;
  const size = info ? formatBytes(info.sizeBytes, t) : null;
  const required = info?.required;

  const install = async () => {
    if (!info?.latestVersion || busy) return;
    setBusy(true);
    setError(null);
    try {
      const result = await bridge.appUpdateInstall(info.latestVersion);
      if (!result.success) {
        setError(result.errorMessage || t('appUpdate.startFailed', 'Не удалось запустить обновление.'));
        setBusy(false);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      setBusy(false);
    }
  };

  return (
    <AnimatePresence>
      {open && info && (
        <motion.div
          key="app-update"
          className="fixed inset-0 z-[120] flex items-center justify-center p-6"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.3, ease: 'easeOut' }}
          style={{
            background:
              'radial-gradient(ellipse at center, rgba(10,10,16,0.62) 0%, rgba(0,0,0,0.82) 85%)',
            backdropFilter: 'blur(8px) saturate(130%)',
            WebkitBackdropFilter: 'blur(8px) saturate(130%)',
          }}
        >
          <motion.div
            className="w-full max-w-[540px]"
            initial={{ opacity: 0, scale: 0.92, y: 26, filter: 'blur(10px)' }}
            animate={{ opacity: 1, scale: 1, y: 0, filter: 'blur(0px)' }}
            exit={{ opacity: 0, scale: 0.96, y: 14, filter: 'blur(8px)' }}
            transition={{ type: 'spring', stiffness: 240, damping: 24, mass: 0.9 }}
          >
            <GlassPanel
              depth="z3" tint="ultra" rounded="3xl" highlight edge
              className="relative overflow-hidden border border-white/[0.08] flex flex-col max-h-[86vh]"
            >
              <motion.span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                           bg-gradient-to-r from-transparent via-white/45 to-transparent"
                initial={{ opacity: 0, scaleX: 0.4 }}
                animate={{ opacity: 1, scaleX: 1 }}
                transition={{ duration: 0.7, ease: EASE_DEPTH, delay: 0.15 }}
              />
              <motion.span
                aria-hidden
                className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl"
                style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                transition={{ duration: 0.9, ease: 'easeOut', delay: 0.2 }}
              />

              <motion.div variants={stagger} initial="hidden" animate="show" className="flex flex-col min-h-0">
                <motion.div variants={item} className="relative flex items-start gap-4 px-7 pt-6 pb-4 shrink-0">
                  <div className="relative w-12 h-12 shrink-0">
                    <motion.span
                      aria-hidden
                      className="absolute inset-0 rounded-2xl blur-md"
                      style={{ background: `radial-gradient(circle, ${required ? 'var(--status-warning)' : 'var(--accent)'} 0%, transparent 70%)` }}
                      initial={{ opacity: 0.3, scale: 0.85 }}
                      animate={{ opacity: [0.3, 0.5, 0.3], scale: [0.85, 1.1, 0.85] }}
                      transition={{ duration: 2.8, ease: 'easeInOut', repeat: Infinity }}
                    />
                    <div
                      className={
                        'relative w-12 h-12 rounded-2xl border border-white/[0.08] flex items-center justify-center ' +
                        (required ? 'bg-[color-mix(in_srgb,var(--status-warning)_18%,transparent)]' : 'bg-accent-soft')
                      }
                    >
                      {required
                        ? <AlertTriangle size={20} style={{ color: 'var(--status-warning)' }} />
                        : <Download size={20} className="text-accent" strokeWidth={2} />}
                    </div>
                  </div>

                  <div className="min-w-0 flex-1">
                    <div className="text-[10px] font-mono font-bold uppercase tracking-[0.18em] text-text-muted mb-1">
                      {required
                        ? t('appUpdate.requiredLabel', 'Обязательное обновление')
                        : t('appUpdate.availableLabel', 'Доступно обновление')}
                    </div>
                    <h2 className="font-display text-xl font-bold text-text-primary tracking-tight leading-tight">
                      Miami Graphics {info.latestVersion}
                    </h2>
                    <p className="text-[13px] text-text-secondary mt-1">
                      {size
                        ? t('appUpdate.currentWithSize', 'Установлена {{version}} · установщик {{size}}',
                            { version: info.currentVersion, size })
                        : t('appUpdate.current', 'Установлена {{version}}', { version: info.currentVersion })}
                    </p>
                  </div>

                  {!required && (
                    <button
                      type="button"
                      onClick={() => setDismissed(true)}
                      aria-label={t('common.close', 'Закрыть')}
                      style={{ outline: 'none' }}
                      className="shrink-0 w-9 h-9 rounded-lg flex items-center justify-center
                                 text-text-muted hover:text-text-primary hover:bg-white/[0.06] transition-colors"
                    >
                      <X size={18} />
                    </button>
                  )}
                </motion.div>

                <motion.div variants={item} className="relative px-7 flex-1 min-h-0 overflow-y-auto">
                  <div className="rounded-2xl bg-bg-elevated/30 border border-glass-border p-4">
                    {info.releaseNotes
                      ? <p className="text-[13px] leading-6 text-text-secondary whitespace-pre-line">{info.releaseNotes}</p>
                      : <p className="text-[13px] text-text-muted">
                          {t('appUpdate.noReleaseNotes', 'Описание изменений недоступно.')}
                        </p>}
                  </div>

                  {error && (
                    <div className="mt-3 rounded-xl px-3.5 py-2.5 text-[13px]
                                    bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)]
                                    border border-[color-mix(in_srgb,var(--status-error)_30%,transparent)]"
                         style={{ color: 'var(--status-error)' }}>
                      {error}
                    </div>
                  )}

                  {busy && !error && (
                    <div className="mt-3">
                      <div className="flex items-baseline justify-between gap-3 mb-2">
                        <span className="text-[13px] text-text-secondary truncate">
                          {progress?.detail || t('appUpdate.preparing', 'Готовлю обновление…')}
                        </span>
                        {!!progress && progress.percent > 0 && (
                          <span className="text-[13px] font-mono text-text-muted shrink-0 tabular-nums">
                            {progress.percent}%
                          </span>
                        )}
                      </div>
                      {!!progress && progress.percent > 0 && (
                        <div className="h-1.5 rounded-full overflow-hidden bg-white/[0.07]">
                          <div
                            className="h-full rounded-full bg-accent transition-[width] duration-300 ease-out"
                            style={{ width: `${Math.min(100, Math.max(0, progress.percent))}%` }}
                          />
                        </div>
                      )}
                    </div>
                  )}
                </motion.div>

                <motion.div variants={item} className="relative flex items-center justify-end gap-2 px-7 pt-4 pb-6 shrink-0">
                  {!required && (
                    <button
                      type="button"
                      onClick={() => setDismissed(true)}
                      disabled={busy}
                      style={{ outline: 'none' }}
                      className="inline-flex items-center justify-center h-12 px-5 rounded-xl
                                 text-sm font-medium text-text-secondary
                                 hover:text-text-primary hover:bg-white/[0.05]
                                 transition-colors disabled:opacity-50"
                    >
                      {t('appUpdate.later', 'Позже')}
                    </button>
                  )}
                  <button
                    type="button"
                    onClick={install}
                    disabled={busy}
                    style={{ outline: 'none' }}
                    className="inline-flex items-center justify-center gap-2 h-12 px-6 rounded-xl
                               bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                               hover:bg-bg-elevated/80 hover:border-white/[0.20]
                               transition-colors text-sm font-bold uppercase tracking-wider
                               disabled:opacity-60 disabled:cursor-wait"
                  >
                    {busy
                      ? <span aria-hidden className="w-4 h-4 rounded-full border-2 border-white/30 border-t-text-primary animate-spin" />
                      : <Download size={16} strokeWidth={2.4} />}
                    {busy
                      ? (progress && progress.percent > 0
                          ? `${t('appUpdate.downloading', 'Скачиваем…')} ${progress.percent}%`
                          : t('appUpdate.downloading', 'Скачиваем…'))
                      : t('appUpdate.updateNow', 'Обновить')}
                  </button>
                </motion.div>
              </motion.div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
