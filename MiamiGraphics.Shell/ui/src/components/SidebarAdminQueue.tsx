import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Upload, CloudUpload, CheckCircle2, AlertCircle, Clock, Loader2 } from 'lucide-react';
import { useAdminStore } from '@/store/adminStore';
import { useGunpackStore } from '@/store/gunpackStore';
import type { QueueItem, GunpackQueueItem } from '@/bridge/types';
import { EASE_DEPTH } from '@/design';

interface Props {
  collapsed: boolean;
}

interface Row {
  id:           string;
  name:         string;
  kind:         'redux' | 'gunpack';
  status:       'pending' | 'processing' | 'done' | 'error';
  percent:      number | null;
  errorMessage: string | null;
  phaseLabel:   string | null;
  hideAt:       number | null;
}

export function SidebarAdminQueue({ collapsed }: Props) {
  const { t } = useTranslation();
  const reduxQueue   = useAdminStore(s => s.queue);
  const gunpackQueue = useGunpackStore(s => s.queue);

  const [hideMap, setHideMap] = useState<Record<string, number>>({});

  const rows: Row[] = [
    ...reduxQueue.map(reduxRow(t)),
    ...gunpackQueue.map(gunpackRow(t)),
  ];

  useEffect(() => {
    setHideMap(prev => {
      const now = Date.now();
      const next = { ...prev };
      let changed = false;
      for (const r of rows) {
        if (r.status === 'done' || r.status === 'error') {
          if (next[r.id] == null) {
            next[r.id] = now + (r.status === 'error' ? 10_000 : 5_000);
            changed = true;
          }
        } else if (next[r.id] != null) {
          delete next[r.id];
          changed = true;
        }
      }

      for (const k of Object.keys(next)) {
        if (!rows.find(r => r.id === k)) {
          delete next[k];
          changed = true;
        }
      }
      return changed ? next : prev;
    });

  }, [reduxQueue, gunpackQueue]);

  useEffect(() => {
    const id = window.setInterval(() => {
      setHideMap(prev => {
        const now = Date.now();
        let changed = false;
        const next: Record<string, number> = {};
        for (const [k, ts] of Object.entries(prev)) {
          if (ts > now) next[k] = ts;
          else changed = true;
        }
        return changed ? next : prev;
      });
    }, 1000);
    return () => window.clearInterval(id);
  }, []);

  const visible = rows.filter(r => {
    if (r.status === 'pending' || r.status === 'processing') return true;
    const ts = hideMap[r.id];
    return ts != null && ts > Date.now();
  });

  if (visible.length === 0) return null;

  if (collapsed) {
    return (
      <div className="px-3 pb-2">
        <div className="relative w-11 h-11 mx-auto rounded-2xl bg-glass-strong
                        border border-sky-400/30 flex items-center justify-center
                        text-sky-300 shadow-z1">
          <CloudUpload size={18} />
          <span className="absolute -top-1 -right-1 min-w-[18px] h-[18px] px-1 rounded-full
                           bg-sky-500 text-white text-[10px] font-bold
                           flex items-center justify-center tabular-nums">
            {visible.length}
          </span>
        </div>
      </div>
    );
  }

  return (
    <motion.div
      initial={{ opacity: 0, y: -4, height: 0 }}
      animate={{ opacity: 1, y: 0, height: 'auto' }}
      exit={{ opacity: 0, y: -4, height: 0 }}
      transition={{ duration: 0.32, ease: EASE_DEPTH }}
      className="px-3 pb-3 overflow-hidden"
    >
      {}
      <div className="flex items-center justify-between gap-2 px-3 pt-1 pb-2">
        <span className="inline-flex items-center gap-2 text-[10px] uppercase
                         tracking-[0.22em] font-bold text-text-muted">
          <Upload size={11} className="text-sky-400" />
          {t('sidebar.adminUpload')}
        </span>
        <span className="px-1.5 h-4 rounded-full bg-sky-500/20 text-sky-300
                         text-[10px] font-bold tabular-nums leading-4 inline-block">
          {visible.length}
        </span>
      </div>

      <div className="flex flex-col gap-1.5">
        <AnimatePresence initial={false}>
          {visible.map(row => (
            <motion.div
              key={row.id}
              initial={{ opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: 'auto' }}
              exit={{ opacity: 0, height: 0 }}
              transition={{ duration: 0.28, ease: EASE_DEPTH }}
              className="overflow-hidden"
            >
              <UploadRow row={row} t={t} />
            </motion.div>
          ))}
        </AnimatePresence>
      </div>
    </motion.div>
  );
}

function UploadRow({ row, t }: { row: Row; t: ReturnType<typeof useTranslation>['t'] }) {
  const isError  = row.status === 'error';
  const isDone   = row.status === 'done';
  const isActive = !isError && !isDone;

  const target = isDone ? 100 : isError ? Math.max(8, row.percent ?? 0) : (row.percent ?? 0);
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
        return prev + diff * Math.min(1, 3.5 * dt);
      });
      frameId = requestAnimationFrame(tick);
    };
    frameId = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frameId);
  }, [target, isActive]);

  const statusText = isDone
    ? t('sidebar.uploadDone')
    : isError
      ? t('sidebar.uploadFailed')
      : `${Math.floor(displayed)}%`;

  return (
    <div
      className={

        'relative rounded-xl px-3 py-2.5 flex flex-col gap-2 ' +
        'bg-glass-strong border transition-colors duration-300 ease-depth ' +
        (isError ? 'border-status-error/50' : 'border-glass-border')
      }
    >
      <div className="flex items-center gap-2 min-w-0">
        {}
        <span className="shrink-0 w-4 h-4 flex items-center justify-center">
          {isDone   ? <CheckCircle2 size={13} className="text-status-success" />
           : isError ? <AlertCircle  size={13} className="text-status-error" />
           : row.status === 'pending'
             ? <Clock        size={13} className="text-text-muted" />
             : <Loader2      size={13} className="text-sky-400 animate-spin" />}
        </span>

        {}
        <span className={
          'shrink-0 px-1.5 h-4 rounded text-[9px] font-bold tracking-wider uppercase leading-4 ' +
          (row.kind === 'redux'
            ? 'bg-sky-500/20 text-sky-200'
            : 'bg-violet-500/20 text-violet-200')
        }>
          {row.kind === 'redux' ? 'REDUX' : 'GUN'}
        </span>

        <span className="flex-1 min-w-0 text-xs font-semibold text-text-primary truncate"
              title={row.name}>
          {row.name}
        </span>
        <span className={
          'shrink-0 text-[10px] font-bold tabular-nums uppercase tracking-wider ' +
          (isDone   ? 'text-status-success'
           : isError ? 'text-status-error'
           :          'text-sky-300')
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
             :          'bg-sky-500')
          }
          initial={false}
          animate={{ width: `${displayed}%` }}
          transition={{ duration: 0.12, ease: EASE_DEPTH }}
        />
      </div>

      {}
      {isActive && row.phaseLabel && (
        <p className="text-[10px] text-text-muted leading-snug truncate">
          {row.phaseLabel}
        </p>
      )}
      {isError && row.errorMessage && (
        <p className="text-[10px] leading-snug truncate" style={{ color: 'var(--status-error)' }}
           title={row.errorMessage}>
          {row.errorMessage}
        </p>
      )}
    </div>
  );
}

function reduxRow(t: ReturnType<typeof useTranslation>['t']) {
  return (q: QueueItem): Row => ({
    id:           `redux:${q.tempId}`,
    name:         q.metadata?.name || q.metadata?.id || '-',
    kind:         'redux',
    status:       q.status,
    percent:      q.percent,
    errorMessage: q.errorMessage,
    phaseLabel:   q.currentPhase ? t(`admin.queue.phase.${q.currentPhase}`, { defaultValue: q.currentPhase }) : null,
    hideAt:       null,
  });
}

function gunpackRow(t: ReturnType<typeof useTranslation>['t']) {
  return (q: GunpackQueueItem): Row => ({
    id:           `gunpack:${q.tempId}`,
    name:         q.metadata?.name || '-',
    kind:         'gunpack',
    status:       q.status,
    percent:      q.percent,
    errorMessage: q.errorMessage,
    phaseLabel:   q.currentPhase ? t(`admin.guns.phase.${q.currentPhase}`, { defaultValue: q.currentPhase }) : null,
    hideAt:       null,
  });
}
