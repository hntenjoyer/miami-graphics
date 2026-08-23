import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Download, CheckCircle2, AlertCircle } from 'lucide-react';
import {
  useInstallProgressStore,
  type InstallEntry,
} from '@/store/installProgressStore';
import { EASE_DEPTH } from '@/design';

interface Props {
  collapsed: boolean;
}

export function SidebarDownloads({ collapsed }: Props) {
  const { t } = useTranslation();
  const entries = useInstallProgressStore(s => s.byId);

  const list = useMemo(
    () => Object.values(entries).sort((a, b) => a.name.localeCompare(b.name)),
    [entries],
  );

  if (list.length === 0) return null;

  if (collapsed) {
    return (
      <div className="px-3 pb-2">
        <div className="relative w-11 h-11 mx-auto rounded-2xl bg-glass-strong
                        border border-glass-border flex items-center justify-center
                        text-accent shadow-z1">
          <Download size={18} />
          <span className="absolute -top-1 -right-1 min-w-[18px] h-[18px] px-1 rounded-full
                           bg-accent text-text-on-accent text-[10px] font-bold
                           flex items-center justify-center tabular-nums">
            {list.length}
          </span>
        </div>
      </div>
    );
  }

  return (
    <motion.div
      initial={{ opacity: 0, y: -4, height: 0 }}
      animate={{ opacity: 1, y: 0, height: 'auto' }}
      exit   ={{ opacity: 0, y: -4, height: 0 }}
      transition={{ duration: 0.32, ease: EASE_DEPTH }}
      className="px-3 pb-3 overflow-hidden"
    >
      {}
      <div className="flex items-center justify-between gap-2 px-3 pt-1 pb-2">
        <span className="inline-flex items-center gap-2 text-[10px] uppercase
                         tracking-[0.22em] font-bold text-text-muted">
          <Download size={11} className="text-accent" />
          {t('sidebar.downloads')}
        </span>
        <span className="px-1.5 h-4 rounded-full bg-accent-soft text-accent
                         text-[10px] font-bold tabular-nums leading-4 inline-block">
          {list.length}
        </span>
      </div>

      <div className="flex flex-col gap-1.5">
        <AnimatePresence initial={false}>
          {list.map(entry => (
            <motion.div
              key={entry.reduxId}
              initial={{ opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: 'auto' }}
              exit   ={{ opacity: 0, height: 0 }}
              transition={{ duration: 0.28, ease: EASE_DEPTH }}
              className="overflow-hidden"
            >
              <DownloadRow entry={entry} />
            </motion.div>
          ))}
        </AnimatePresence>
      </div>
    </motion.div>
  );
}

function DownloadRow({ entry }: { entry: InstallEntry }) {
  const { t } = useTranslation();
  const isError = entry.phase === 'error';
  const isDone  = entry.phase === 'done';
  const isActive = !isError && !isDone;

  const target = isDone ? 100 : isError ? Math.max(8, entry.percent) : entry.percent;
  const [displayed, setDisplayed] = useState(target);

  useEffect(() => {

    if (!isActive) { setDisplayed(target); return; }
    let frameId = 0;
    let last = performance.now();
    const tick = (now: number) => {
      const dt = Math.max(0, (now - last) / 1000);
      last = now;
      setDisplayed(prev => {
        const diff = target - prev;
        if (Math.abs(diff) < 0.05) return target;

        const next = prev + diff * Math.min(1, 3.5 * dt);
        return next;
      });
      frameId = requestAnimationFrame(tick);
    };
    frameId = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frameId);
  }, [target, isActive]);

  const statusText = isDone
    ? t('sidebar.downloadDone')
    : isError
      ? t('sidebar.downloadFailed')
      : `${Math.floor(displayed)}%`;

  return (
    <div
      className={

        'relative rounded-xl px-3 py-2.5 flex flex-col gap-2 ' +
        'bg-glass-strong transition-colors duration-300 ease-depth'
      }
    >
      <div className="flex items-center gap-2 min-w-0">
        {}
        <span className="shrink-0 w-4 h-4 flex items-center justify-center">
          {isDone   ? <CheckCircle2 size={13} className="text-status-success" />
           : isError ? <AlertCircle  size={13} className="text-status-error" />
           :          <Download     size={13} className="text-accent" />}
        </span>
        <span className="flex-1 min-w-0 text-xs font-semibold text-text-primary truncate"
              title={entry.name}>
          {entry.name}
        </span>
        <span className={
          'shrink-0 text-[10px] font-bold tabular-nums uppercase tracking-wider ' +
          (isDone   ? 'text-status-success'
           : isError ? 'text-status-error'
           :          'text-accent')
        }>
          {statusText}
        </span>
      </div>

      {}
      <div className="h-1 rounded-full bg-glass border border-glass-border overflow-hidden">
        <motion.div
          className={
            'h-full rounded-full ' +
            (isError ? 'bg-status-error'
             : isDone  ? 'bg-status-success'
             :          'bg-accent')
          }
          initial={false}
          animate={{ width: `${displayed}%` }}

          transition={{ duration: 0.12, ease: EASE_DEPTH }}
        />
      </div>

      {}
      {isActive && entry.detailMessage && (
        <p className="text-[10px] text-text-muted leading-snug truncate">
          {entry.detailMessage}
        </p>
      )}
    </div>
  );
}
