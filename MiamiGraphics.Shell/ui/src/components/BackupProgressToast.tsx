import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Loader2, ShieldCheck, AlertTriangle, X } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { useBackupStore, formatBytes, formatBytesPerSec } from '@/store/backupStore';
import type { BackupPhase } from '@/bridge/types';
import { useToastPresenceStore } from '@/store/toastPresenceStore';

const PHASE_LABELS: Partial<Record<BackupPhase, [string, string]>> = {
  detecting:                ['backup.progressToast.phasePreparing', 'Подготовка'],
  hashing_user_update:      ['backup.progressToast.phasePreparing', 'Подготовка'],
  comparing:                ['backup.progressToast.phasePreparing', 'Подготовка'],
  snapshot_user_update:     ['backup.progressToast.phasePreparing', 'Подготовка'],
  downloading_clean_update: ['backup.progressToast.phaseCleanUpdate', 'Установка чистого апдейта'],
  writing_working_update:   ['backup.progressToast.phaseCleanUpdate', 'Установка чистого апдейта'],
  snapshot_dlc:             ['backup.progressToast.phaseCleanUpdate', 'Установка чистого апдейта'],
  downloading_clean_dlc:    ['backup.progressToast.phaseCleanDlc', 'Установка чистого DLC'],
  writing_working_dlc:      ['backup.progressToast.phaseCleanDlc', 'Установка чистого DLC'],
  writing_manifest:         ['backup.progressToast.phaseFinalizing', 'Финализация'],
  done:                     ['common.done', 'Готово'],
};

const PHASE_ORDER: BackupPhase[] = [
  'detecting', 'hashing_user_update', 'comparing', 'snapshot_user_update',
  'downloading_clean_update', 'writing_working_update', 'snapshot_dlc',
  'downloading_clean_dlc', 'writing_working_dlc', 'writing_manifest', 'done',
];

const HIDDEN_WHILE_BLOCKING_SCREEN = new Set<BackupPhase>([
  'detecting', 'hashing_user_update', 'comparing', 'snapshot_user_update',
]);

export function BackupProgressToast() {
  const { t } = useTranslation();
  const progress  = useBackupStore(s => s.progress);
  const result    = useBackupStore(s => s.result);
  const error     = useBackupStore(s => s.error);
  const liveStats = useBackupStore(s => s.liveStats);

  const isDownload = progress?.phase === 'downloading_clean_update'
                  || progress?.phase === 'downloading_clean_dlc';
  const isWriting  = progress?.phase === 'writing_working_update'
                  || progress?.phase === 'writing_working_dlc';
  const stats = isDownload ? liveStats : null;

  const overallPercent = useMemo(() => {
    if (result?.success) return 100;
    if (!progress) return 0;
    const idx = PHASE_ORDER.indexOf(progress.phase);
    if (idx < 0) return 0;
    const phaseFraction = Math.min(100, Math.max(0, progress.percent)) / 100;
    return Math.round(((idx + phaseFraction) / PHASE_ORDER.length) * 100);
  }, [progress, result]);

  type State = 'running' | 'done' | 'error' | 'hidden';
  const state: State = (() => {
    if (result?.success) return 'done';
    if (result?.success === false || error) return 'error';
    if (progress !== null && !HIDDEN_WHILE_BLOCKING_SCREEN.has(progress.phase)) return 'running';
    return 'hidden';
  })();

  const [dismissed, setDismissed] = useState(false);
  useEffect(() => {
    if (state === 'running') {
      setDismissed(false);
      return;
    }
    if (state === 'done') {
      const id = window.setTimeout(() => setDismissed(true), 4000);
      return () => window.clearTimeout(id);
    }
  }, [state]);

  const visible = state !== 'hidden' && !dismissed;

  useEffect(() => {
    if (!visible) return;
    useToastPresenceStore.getState().enter();
    return () => useToastPresenceStore.getState().leave();
  }, [visible]);

  const label = useMemo(() => {
    if (state === 'done')  return t('backup.progressToast.doneTitle', 'Чистая GTA готова');
    if (state === 'error') return t('backup.progressToast.errorTitle', 'Ошибка подготовки');
    if (!progress)         return '';
    const entry = PHASE_LABELS[progress.phase];
    return entry ? t(entry[0], entry[1]) : String(progress.phase);
  }, [state, progress, t]);

  const accent = state === 'done'
    ? 'var(--status-success)'
    : state === 'error'
      ? 'var(--status-error)'
      : 'var(--accent)';

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          key={state}
          initial={{ opacity: 0, y: 18, scale: 0.97 }}
          animate={{ opacity: 1, y: 0,  scale: 1    }}
          exit   ={{ opacity: 0, y: 18, scale: 0.97 }}
          transition={{ duration: 0.34, ease: [0.22, 1, 0.36, 1] }}
          className="fixed right-6 bottom-6 z-[60] w-[320px]"
        >
          <div
            className="relative flex flex-col gap-2 px-4 py-3.5 rounded-2xl backdrop-blur-glass-heavy"
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
            <div className="flex items-center gap-3">
              <span
                className="shrink-0 inline-flex items-center justify-center w-9 h-9 rounded-xl"
                style={{
                  background: `color-mix(in srgb, ${accent} 18%, transparent)`,
                  boxShadow: `inset 0 0 0 1px color-mix(in srgb, ${accent} 38%, transparent)`,
                  color: accent,
                }}
              >
                {state === 'done'    && <ShieldCheck   size={16} strokeWidth={2.5} />}
                {state === 'error'   && <AlertTriangle size={16} strokeWidth={2.5} />}
                {state === 'running' && <Loader2 size={16} strokeWidth={2.5} className="animate-spin" />}
              </span>

              <div className="flex-1 min-w-0">
                <div className="text-[13px] font-semibold text-text-primary leading-tight truncate">
                  {label}
                </div>
                <div className="text-[11px] text-text-muted leading-tight mt-0.5 truncate">
                  {state === 'running' && (isWriting
                    ? t('backup.progressToast.subWriting', 'сохраняем в кеш…')
                    : t('backup.progressToast.subBrowsing', 'можно ходить по каталогу'))}
                  {state === 'done'    && t('backup.progressToast.subDone', 'Можно ставить моды')}
                  {state === 'error'   && (error ?? result?.errorMessage ?? t('backup.progressToast.subError', 'Открой Downloads'))}
                </div>
              </div>

              <div
                className="tabular-nums text-[11px] font-semibold shrink-0 min-w-[34px] text-right"
                style={{ color: accent }}
              >
                {state === 'running' && `${overallPercent}%`}
              </div>

              {(state === 'done' || state === 'error') && (
                <button
                  type="button"
                  onClick={() => setDismissed(true)}
                  aria-label={t('common.hide', 'Скрыть')}
                  className="shrink-0 inline-flex items-center justify-center w-7 h-7 rounded-lg
                             text-text-muted hover:bg-white/[0.08] hover:text-text-primary
                             transition-colors duration-150"
                  style={{ outline: 'none' }}
                >
                  <X size={13} />
                </button>
              )}
            </div>

            {state === 'running' && stats && (
              <div className="flex items-baseline justify-between gap-2 pl-12
                              text-[11px] leading-[1.35] tabular-nums">
                <span
                  className="shrink-0 min-w-[64px] font-semibold"
                  style={{ color: stats.stalled ? 'var(--text-muted)' : accent }}
                >
                  {stats.stalled ? '-' : formatBytesPerSec(stats.bytesPerSec)}
                </span>
                <span className="shrink-0 text-text-muted">
                  {t('backup.progressToast.remaining', 'осталось {{size}}', { size: formatBytes(stats.remainingBytes) })}
                  {!stats.stalled && stats.etaSec > 0 && stats.etaSec < 3600 &&
                    ` · ${stats.etaSec < 90
                      ? t('backup.progressToast.etaUnder1Min', '<1 мин')
                      : t('backup.progressToast.etaMinutes', '~{{n}} мин', { n: Math.round(stats.etaSec / 60) })}`}
                </span>
              </div>
            )}

            {state === 'running' && (
              <div className="h-1 rounded-full bg-white/[0.06] overflow-hidden">
                <motion.div
                  className="h-full rounded-full"
                  style={{ background: accent }}
                  animate={{ width: `${overallPercent}%` }}
                  transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
                />
              </div>
            )}
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
