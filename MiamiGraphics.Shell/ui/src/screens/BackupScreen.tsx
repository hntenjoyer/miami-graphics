import { useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Check, Info, AlertTriangle, X, Loader2, Power, RotateCw, ShieldCheck } from 'lucide-react';
import { useBackupStore, formatBytes, formatBytesPerSec, formatEta } from '@/store/backupStore';
import { GlassPanel, Glow3DButton, EASE_DEPTH } from '@/design';
import type { BackupPhase } from '@/bridge/types';

interface Props {
  onDone: (success: boolean) => void;
}

type StepStatus = 'pending' | 'in-progress' | 'done' | 'warning' | 'error';

interface StepDef {
  id: string;
  phasesCovered: BackupPhase[];
}

const STEPS: StepDef[] = [
  { id: 'detect',          phasesCovered: ['detecting'] },
  { id: 'verify_update',   phasesCovered: ['hashing_user_update', 'comparing'] },
  { id: 'snapshot',        phasesCovered: ['snapshot_user_update'] },
  { id: 'download_update', phasesCovered: ['downloading_clean_update'] },
  { id: 'prepare_dlc',     phasesCovered: ['snapshot_dlc', 'downloading_clean_dlc'] },
  { id: 'finalize',        phasesCovered: ['writing_manifest'] },
  { id: 'done',            phasesCovered: ['done'] },
];

function phaseOrder(phase: BackupPhase): number {
  const order: BackupPhase[] = [
    'detecting', 'hashing_user_update', 'comparing', 'snapshot_user_update',
    'downloading_clean_update', 'snapshot_dlc',
    'downloading_clean_dlc', 'writing_manifest', 'done',
  ];
  const i = order.indexOf(phase);
  return i < 0 ? -1 : i;
}

export function BackupScreen({ onDone }: Props) {
  const { t } = useTranslation();
  const progress = useBackupStore(s => s.progress);
  const result = useBackupStore(s => s.result);
  const runBackup = useBackupStore(s => s.runBackup);
  const cancelBackup = useBackupStore(s => s.cancelBackup);
  const markBackupSkipped = useBackupStore(s => s.markBackupSkipped);
  const cancelling = useBackupStore(s => s.cancelling);

  const lastActivePhase = useBackupStore(s => s.lastActivePhase);

  const lockers = useBackupStore(s => s.result?.lockers ?? null);
  const killingLockers = useBackupStore(s => s.killingLockers);
  const killLockersAndRetry = useBackupStore(s => s.killLockersAndRetry);
  const hasLockers = !!lockers && lockers.length > 0;

  const liveStats = useBackupStore(s => s.liveStats);
  const isDownloadingNow = progress?.phase === 'downloading_clean_update'
                        || progress?.phase === 'downloading_clean_dlc';

  useEffect(() => {
    void runBackup();

  }, []);

  const triggeredByGate = useBackupStore(s => s.triggeredByGate);
  useEffect(() => {
    if (!progress) return;
    if (triggeredByGate) return;
    const downloadPhases: BackupPhase[] = [
      'downloading_clean_update', 'writing_working_update',
      'snapshot_dlc', 'downloading_clean_dlc', 'writing_working_dlc',
      'writing_manifest', 'done',
    ];
    if (downloadPhases.includes(progress.phase)) {
      onDone(false);
    }

  }, [progress?.phase, triggeredByGate]);

  useEffect(() => {
    if (result?.success) {
      const id = window.setTimeout(() => onDone(true), 600);
      return () => window.clearTimeout(id);
    }
  }, [result, onDone]);

  const isError = result ? !result.success : progress?.phase === 'error';
  const isDone  = !!result?.success;
  const errorCode = (result?.errorCode) ?? (isError ? progress?.errorCode : null) ?? 'UNKNOWN';
  const errorMessage = (result?.errorMessage) ?? (isError ? progress?.errorMessage : null);

  const stepStatuses = useMemo<Record<string, StepStatus>>(() => {
    const out: Record<string, StepStatus> = {};
    if (!progress && !result) {
      STEPS.forEach(s => { out[s.id] = 'pending'; });
      return out;
    }

    const successOverride = result?.success === true;
    const currentPhase = successOverride
      ? 'done'
      : progress?.phase ?? (result?.success === false ? 'error' : 'detecting');
    const isErrorPhase = currentPhase === 'error';
    const orderingPhase: BackupPhase = isErrorPhase
      ? (lastActivePhase ?? 'detecting')
      : (currentPhase as BackupPhase);
    const currentOrder = phaseOrder(orderingPhase);

    for (const s of STEPS) {
      const stepFirstOrder = phaseOrder(s.phasesCovered[0]);
      const stepLastOrder = phaseOrder(s.phasesCovered[s.phasesCovered.length - 1]);

      if (isErrorPhase) {
        if (currentOrder > stepLastOrder)                                out[s.id] = 'done';
        else if (currentOrder >= stepFirstOrder && currentOrder <= stepLastOrder) out[s.id] = 'error';
        else                                                             out[s.id] = 'pending';
        continue;
      }

      if (currentOrder > stepLastOrder) out[s.id] = 'done';
      else if (currentOrder >= stepFirstOrder && currentOrder <= stepLastOrder) out[s.id] = 'in-progress';
      else out[s.id] = 'pending';
    }

    if (result?.versionUnsupported && out.verify_update === 'done') out.verify_update = 'warning';
    return out;
  }, [progress, result, lastActivePhase]);

  const overallPercent = useMemo(() => {
    if (isDone) return 100;
    const totalSteps = STEPS.length;
    let doneCount = 0;
    let activeFraction = 0;
    for (const s of STEPS) {
      const st = stepStatuses[s.id];
      if (st === 'done' || st === 'warning') doneCount++;
      else if (st === 'in-progress') {
        activeFraction = (progress?.percent ?? 0) / 100;
      }
    }
    return Math.min(100, Math.round(((doneCount + activeFraction) / totalSteps) * 100));
  }, [stepStatuses, progress?.percent, isDone]);

  const downloadingDirty = progress?.phase === 'downloading_clean_update';
  const isRunning = !!progress && !isError && !isDone;

  const headerStatus: 'running' | 'done' | 'error' =
    isDone ? 'done' : isError ? 'error' : 'running';

  return (
    <div className="relative w-full h-full overflow-hidden flex items-center justify-center px-6">
      {}
      <div
        aria-hidden="true"
        className="absolute pointer-events-none w-[640px] h-[640px] -z-0 blur-3xl opacity-50 transition-colors duration-700"
        style={{
          background:
            headerStatus === 'error'
              ? 'radial-gradient(circle at 50% 50%, var(--status-error) 0%, transparent 65%)'
              : headerStatus === 'done'
                ? 'radial-gradient(circle at 50% 50%, var(--status-success) 0%, transparent 65%)'
                : 'radial-gradient(circle at 50% 50%, var(--accent) 0%, transparent 65%)',
        }}
      />

      <div className="relative z-10 flex flex-col items-center w-full max-w-[560px]">
        <motion.div
          initial={{ opacity: 0, y: 14, scale: 0.97 }}
          animate={{ opacity: 1, y: 0,  scale: 1    }}
          transition={{ duration: 0.5, ease: EASE_DEPTH }}
          className="w-full"
        >
          <GlassPanel depth="z3" tint="ultra" rounded="3xl" highlight edge className="relative overflow-hidden border border-white/[0.08]">
            <motion.span
              aria-hidden
              className="absolute top-0 inset-x-0 h-px pointer-events-none z-20
                         bg-gradient-to-r from-transparent via-white/45 to-transparent"
              initial={{ opacity: 0, scaleX: 0.4 }}
              animate={{ opacity: 1, scaleX: 1 }}
              transition={{ duration: 0.7, ease: EASE_DEPTH, delay: 0.15 }}
            />
            <motion.span
              aria-hidden
              className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl"
              style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 20%, transparent) 0%, transparent 70%)' }}
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ duration: 0.9, ease: 'easeOut', delay: 0.2 }}
            />
            {}
            <div className="relative z-[1] px-7 pt-7 pb-6 border-b border-white/[0.06] flex flex-col gap-5">
              <div className="flex items-center gap-4">
                <StatusBadge status={headerStatus} />
                <div className="flex-1 min-w-0">
                  <h1 className="font-display text-[22px] font-bold text-text-primary tracking-tight leading-tight">
                    {t('backup.title')}
                  </h1>
                  <p className="text-[12.5px] text-text-muted mt-1 leading-snug">
                    {t('backup.subtitle')}
                  </p>
                </div>
                <div className="text-right shrink-0">
                  <div className="font-display text-[22px] font-bold text-text-primary tabular-nums leading-none">
                    {overallPercent}<span className="text-[14px] text-text-muted font-semibold ml-0.5">%</span>
                  </div>
                </div>
              </div>

              {}
              <div className="relative h-2 rounded-full bg-bg-elevated/60 overflow-hidden border border-glass-border">
                <motion.div
                  className="relative h-full rounded-full overflow-hidden"
                  style={{
                    background:
                      headerStatus === 'error'
                        ? 'linear-gradient(90deg, var(--status-error), color-mix(in srgb, var(--status-error) 60%, white))'
                        : headerStatus === 'done'
                          ? 'linear-gradient(90deg, var(--status-success), color-mix(in srgb, var(--status-success) 60%, white))'
                          : 'linear-gradient(90deg, var(--accent), color-mix(in srgb, var(--accent) 55%, white))',
                    boxShadow:
                      headerStatus === 'error'
                        ? '0 0 16px -2px color-mix(in srgb, var(--status-error) 70%, transparent)'
                        : headerStatus === 'done'
                          ? '0 0 16px -2px color-mix(in srgb, var(--status-success) 70%, transparent)'
                          : '0 0 16px -2px color-mix(in srgb, var(--accent) 70%, transparent)',
                  }}
                  initial={false}
                  animate={{ width: `${overallPercent}%` }}
                  transition={{ duration: 0.5, ease: EASE_DEPTH }}
                >
                  {headerStatus === 'running' && (
                    <motion.span
                      aria-hidden
                      className="absolute inset-y-0 w-1/3"
                      style={{
                        background:
                          'linear-gradient(90deg, transparent 0%, rgba(255,255,255,0.35) 50%, transparent 100%)',
                      }}
                      animate={{ x: ['-100%', '300%'] }}
                      transition={{ duration: 1.8, repeat: Infinity, ease: 'linear' }}
                    />
                  )}
                </motion.div>
              </div>

              {}
              {isDownloadingNow && liveStats && (
                <div className="flex items-center justify-between gap-4 text-[11px] font-mono text-text-secondary">
                  <span className="tabular-nums text-accent">
                    {formatBytesPerSec(liveStats.bytesPerSec)}
                  </span>
                  <span className="tabular-nums">
                    {liveStats.etaSec > 0 && liveStats.etaSec < 60 * 60
                      ? t('backup.eta', { defaultValue: 'ETA {{time}}', time: formatEta(liveStats.etaSec) })
                      : '-'}
                  </span>
                  <span className="tabular-nums text-text-muted">
                    {formatBytes(liveStats.doneBytes)} / {formatBytes(liveStats.totalBytes)}
                  </span>
                </div>
              )}
            </div>

            {}
            <div className="relative z-[1] px-5 pt-5 pb-1">
              <div className="rounded-2xl bg-bg-elevated/40 border border-white/[0.07] divide-y divide-white/[0.06]">
                {STEPS.map((step, idx) => {
                  const status = stepStatuses[step.id];
                  const isCurrent = status === 'in-progress';
                  const isDl = isCurrent && step.id === 'download_update' && progress?.phase === 'downloading_clean_update';
                  return (
                    <BackupStepRow
                      key={step.id}
                      number={idx + 1}
                      label={t(`backup.step.${step.id}`)}
                      status={status}
                      percent={isCurrent ? progress?.percent ?? 0 : undefined}
                      showBar={isDl}
                    />
                  );
                })}
              </div>
            </div>

            {}
            <AnimatePresence initial={false}>
              {downloadingDirty && result?.hadDirtyUpdate !== false && (
                <Banner key="dirty" tone="info">
                  <span className="text-sm">{t('backup.warning.dirty')}</span>
                </Banner>
              )}
              {result?.versionUnsupported && (
                <Banner key="vunsup" tone="warning">
                  <span className="text-sm">{t('backup.warning.versionUnsupported')}</span>
                </Banner>
              )}
            </AnimatePresence>

            {}
            <AnimatePresence initial={false}>
              {isError && (
                <motion.div
                  key="err-footer"
                  initial={{ opacity: 0, height: 0 }}
                  animate={{ opacity: 1, height: 'auto' }}
                  exit={{ opacity: 0, height: 0 }}
                  transition={{ duration: 0.32, ease: EASE_DEPTH }}
                  style={{ overflow: 'hidden' }}
                >
                  <div className="px-7 pt-3 pb-6 flex flex-col gap-3">
                    <Banner tone="error" inline>
                      <span className="text-sm text-text-primary">
                        {}
                        {errorCode === 'UNKNOWN' && errorMessage
                          ? errorMessage
                          : t(`backup.error.${errorCode}`, { defaultValue: errorMessage ?? t('backup.error.UNKNOWN') })}
                      </span>
                    </Banner>

                    {}
                    {hasLockers && (
                      <LockerList
                        lockers={lockers!}
                        warning={t('backup.lockers.warning')}
                        emptyHint={t('backup.lockers.unknownProcess')}
                      />
                    )}

                    <div className="flex items-center gap-3">
                      {}
                      {hasLockers ? (
                        <button
                          type="button"
                          onClick={() => void killLockersAndRetry()}
                          disabled={killingLockers}
                          style={{
                            outline: 'none',
                            background: killingLockers
                              ? 'var(--bg-elevated)'
                              : 'color-mix(in srgb, var(--accent) 18%, var(--bg-elevated))',
                            color: killingLockers
                              ? 'var(--text-muted)'
                              : 'var(--text-primary)',
                            cursor: killingLockers ? 'not-allowed' : 'pointer',
                            boxShadow: killingLockers
                              ? 'inset 0 0 0 1px var(--border-subtle)'
                              : 'inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 70%, transparent), '
                                + '0 8px 22px -10px color-mix(in srgb, var(--accent) 70%, transparent)',
                          }}
                          className="flex-1 inline-flex items-center justify-center gap-2 px-5 h-11 rounded-xl
                                     text-[12.5px] font-bold uppercase tracking-[0.14em]
                                     transition-[background-color,box-shadow,color] duration-200 ease-depth"
                        >
                          {killingLockers
                            ? <Loader2 size={14} strokeWidth={2.4} className="animate-spin" />
                            : <Power size={14} strokeWidth={2.4} />}
                          {t('backup.lockers.closeAndRetry')}
                        </button>
                      ) : (
                        <button
                          type="button"
                          onClick={() => void runBackup()}
                          style={{
                            outline: 'none',
                            background: 'color-mix(in srgb, var(--accent) 18%, var(--bg-elevated))',
                            color: 'var(--text-primary)',
                            cursor: 'pointer',
                            boxShadow: 'inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 70%, transparent), '
                              + '0 8px 22px -10px color-mix(in srgb, var(--accent) 70%, transparent)',
                          }}
                          className="flex-1 inline-flex items-center justify-center gap-2 px-5 h-11 rounded-xl
                                     text-[12.5px] font-bold uppercase tracking-[0.14em]
                                     transition-[background-color,box-shadow,color] duration-200 ease-depth"
                        >
                          <RotateCw size={14} strokeWidth={2.4} />
                          {t('backup.retry')}
                        </button>
                      )}
                      <Glow3DButton variant="ghost" size="sm" onClick={() => { markBackupSkipped(); onDone(false); }}>
                        {t('backup.skip')}
                      </Glow3DButton>
                    </div>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>

            <AnimatePresence initial={false}>
              {isRunning && (
                <motion.div
                  key="running-footer"
                  initial={{ opacity: 0, height: 0 }}
                  animate={{ opacity: 1, height: 'auto' }}
                  exit={{ opacity: 0, height: 0 }}
                  transition={{ duration: 0.28, ease: EASE_DEPTH }}
                  style={{ overflow: 'hidden' }}
                >
                  <div className="px-5 pt-3 pb-5 flex items-center justify-between gap-3">
                    <div
                      className="flex-1 min-w-0 flex items-start gap-2.5 px-3.5 py-2.5 rounded-xl"
                      style={{
                        background: 'color-mix(in srgb, var(--status-info) 8%, transparent)',
                        border: '1px solid color-mix(in srgb, var(--status-info) 22%, transparent)',
                      }}
                    >
                      <Info
                        size={15}
                        strokeWidth={2.2}
                        className="mt-0.5 shrink-0"
                        style={{ color: 'var(--status-info)' }}
                      />
                      <span className="text-[12px] leading-snug text-text-secondary whitespace-nowrap">
                        {t('backup.dontClose', 'Не закрывай приложение во время загрузки.')}
                      </span>
                    </div>
                    <button
                      type="button"
                      onClick={() => void cancelBackup()}
                      disabled={cancelling}
                      style={{ outline: 'none' }}
                      className="shrink-0 inline-flex items-center justify-center gap-2 h-10 px-4 rounded-xl
                                 bg-glass border border-glass-border text-text-secondary hover:text-status-error
                                 hover:border-[color-mix(in_srgb,var(--status-error)_45%,transparent)]
                                 hover:bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)]
                                 disabled:opacity-60 disabled:cursor-not-allowed transition-colors"
                    >
                      {cancelling ? <Loader2 size={14} className="animate-spin" /> : <X size={14} />}
                      <span className="text-[11px] font-bold uppercase tracking-[0.14em]">
                        {cancelling ? t('backup.stopping', 'Останавливаем...') : t('backup.stop', 'Остановить')}
                      </span>
                    </button>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>

            {}

            {}
            {!isError && !isRunning && (progress || result) && (
              <div className="h-5" />
            )}
          </GlassPanel>
        </motion.div>
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: 'running' | 'done' | 'error' }) {
  const isDone    = status === 'done';
  const isError   = status === 'error';
  const colorVar = isError ? 'var(--status-error)'
                  : isDone ? 'var(--status-success)'
                  : 'var(--accent)';

  return (
    <div className="relative shrink-0">
      {}
      <div
        aria-hidden
        className="absolute inset-0 rounded-2xl blur-md"
        style={{
          background: `color-mix(in srgb, ${colorVar} 35%, transparent)`,
          transform: 'scale(1.2)',
        }}
      />
      <div
        className="relative w-14 h-14 rounded-2xl flex items-center justify-center"
        style={{
          background: `color-mix(in srgb, ${colorVar} 18%, var(--bg-elevated))`,
          boxShadow:
            `inset 0 0 0 1.5px color-mix(in srgb, ${colorVar} 45%, transparent),` +
            ` 0 8px 20px -10px color-mix(in srgb, ${colorVar} 70%, transparent)`,
        }}
      >
        {isError ? (
          <X size={22} strokeWidth={2.4} style={{ color: colorVar }} />
        ) : isDone ? (
          <Check size={22} strokeWidth={2.6} style={{ color: colorVar }} />
        ) : (
          <ShieldCheck size={22} strokeWidth={2.2} style={{ color: colorVar }} />
        )}
      </div>
    </div>
  );
}

interface BackupStepRowProps {
  number: number;
  label: string;
  status: StepStatus;
  percent?: number;
  showBar?: boolean;
}

function BackupStepRow({ number, label, status, percent, showBar }: BackupStepRowProps) {
  const isCurrent = status === 'in-progress';
  return (
    <div
      className="relative flex items-center gap-3.5 px-4 py-3.5 transition-colors"
      style={isCurrent ? {
        background:
          'linear-gradient(90deg, color-mix(in srgb, var(--accent) 14%, transparent),' +
          ' color-mix(in srgb, var(--accent) 4%, transparent))',
      } : undefined}
    >
      {}
      {isCurrent && (
        <span
          aria-hidden="true"
          className="absolute left-0 top-1.5 bottom-1.5 w-[3px] rounded-r-full bg-accent"
          style={{ boxShadow: '0 0 14px var(--accent), 0 0 4px var(--accent)' }}
        />
      )}

      {}
      <span className={
        'shrink-0 w-8 h-8 rounded-full flex items-center justify-center text-[11px] font-bold tabular-nums transition-all ' +
        (status === 'pending'
          ? 'bg-bg-elevated text-text-muted border border-glass-border'
          : status === 'in-progress'
            ? 'bg-accent text-text-on-accent'
            : status === 'done'
              ? 'bg-[color-mix(in_srgb,var(--status-success)_22%,transparent)] text-status-success border border-[color-mix(in_srgb,var(--status-success)_50%,transparent)]'
              : status === 'warning'
                ? 'bg-[color-mix(in_srgb,var(--status-warning)_22%,transparent)] text-status-warning border border-[color-mix(in_srgb,var(--status-warning)_50%,transparent)]'
                : 'bg-[color-mix(in_srgb,var(--status-error)_22%,transparent)] text-status-error border border-[color-mix(in_srgb,var(--status-error)_50%,transparent)]')
      }
      style={isCurrent ? {
        boxShadow: '0 0 0 4px color-mix(in srgb, var(--accent) 22%, transparent), 0 6px 18px -6px color-mix(in srgb, var(--accent) 65%, transparent)',
      } : undefined}
      >
        {status === 'pending'     ? number
         : status === 'in-progress' ? <Loader2 size={14} className="animate-spin" strokeWidth={2.6} />
         : status === 'done'      ? <Check size={14} strokeWidth={2.8} />
         : status === 'warning'   ? <AlertTriangle size={14} strokeWidth={2.4} />
         :                          <X size={14} strokeWidth={2.6} />}
      </span>

      <span className={
        'flex-1 text-[14px] leading-tight ' +
        (status === 'in-progress' ? 'text-text-primary font-bold' :
         status === 'done'        ? 'text-text-primary' :
         status === 'error'       ? 'text-status-error font-medium' :
         status === 'warning'     ? 'text-status-warning' :
         'text-text-muted')
      }>
        {label}
      </span>

      {}
      {showBar && percent !== undefined ? (
        <div className="w-28 h-1.5 rounded-full bg-glass-strong overflow-hidden border border-glass-border">
          <motion.div
            className="h-full rounded-full"
            style={{
              background: 'linear-gradient(90deg, var(--accent), color-mix(in srgb, var(--accent) 55%, white))',
            }}
            initial={false}
            animate={{ width: `${percent}%` }}
            transition={{ duration: 0.3, ease: EASE_DEPTH }}
          />
        </div>
      ) : isCurrent && percent !== undefined ? (
        <span className="text-[13px] tabular-nums font-bold text-accent">{percent}%</span>
      ) : null}
    </div>
  );
}

function Banner({
  tone, children, inline,
}: {
  tone: 'info' | 'warning' | 'error';
  children: React.ReactNode;
  inline?: boolean;
}) {
  const Icon = tone === 'error' ? X : tone === 'warning' ? AlertTriangle : Info;
  const colorVar =
    tone === 'error'   ? 'var(--status-error)'   :
    tone === 'warning' ? 'var(--status-warning)' :
                         'var(--status-info)';
  const bgClass =
    tone === 'error'
      ? 'bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)] border-[color-mix(in_srgb,var(--status-error)_30%,transparent)]'
      : tone === 'warning'
        ? 'bg-status-warning-soft border-status-warning-border'
        : 'bg-[color-mix(in_srgb,var(--status-info)_10%,transparent)] border-[color-mix(in_srgb,var(--status-info)_30%,transparent)]';
  const wrap = inline
    ? `flex items-start gap-2 p-3 rounded-xl border ${bgClass}`
    : `mx-7 mb-4 mt-2 flex items-start gap-2 p-3 rounded-xl border ${bgClass}`;
  return (
    <motion.div
      initial={{ opacity: 0, y: -4, height: 0 }}
      animate={{ opacity: 1, y: 0,  height: 'auto' }}
      exit={{ opacity: 0, y: -4, height: 0 }}
      transition={{ duration: 0.28, ease: EASE_DEPTH }}
      style={{ overflow: 'hidden' }}
    >
      <div className={wrap}>
        <Icon size={16} className="mt-0.5 shrink-0" style={{ color: colorVar }} />
        {children}
      </div>
    </motion.div>
  );
}

interface LockerListProps {
  lockers: import('@/bridge/types').LockerProcess[];
  warning: string;
  emptyHint: string;
}

function LockerList({ lockers, warning, emptyHint }: LockerListProps) {
  return (
    <div className="rounded-xl border border-glass-border bg-bg-elevated/40 px-3 py-2.5 flex flex-col gap-2">
      <div className="flex items-start gap-2">
        <Power size={14} className="mt-0.5 shrink-0 text-status-warning" />
        <span className="text-xs text-text-secondary leading-snug">{warning}</span>
      </div>
      <div className="flex flex-col gap-1 pl-5">
        {lockers.map((l, i) => (
          <div key={`${l.pid}-${i}`} className="flex items-center justify-between gap-3 text-xs">
            <span className="font-mono text-text-primary truncate">
              {l.friendlyName || l.processName || emptyHint}
            </span>
            <span className="shrink-0 px-1.5 py-0.5 rounded-md bg-glass-strong border border-glass-border tabular-nums text-[10px] text-text-muted">
              PID {l.pid}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
