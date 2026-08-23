import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
  CheckCircle2, FolderOpen, ChevronRight,
  Folder, Cpu, ShieldAlert,
} from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { bridge } from '@/bridge';
import { useSessionStore } from '@/store/sessionStore';
import { GlassPanel, AccentLoader, EASE_DEPTH } from '@/design';

const fade = {
  initial: { opacity: 0, y: 16, scale: 0.97, filter: 'blur(8px)' },
  animate: { opacity: 1, y: 0,  scale: 1,    filter: 'blur(0px)' },
  exit:    { opacity: 0, y: -10, scale: 0.975, filter: 'blur(8px)' },
  transition: { duration: 0.5, ease: EASE_DEPTH },
};

const group = {
  initial: {},
  animate: { transition: { staggerChildren: 0.075, delayChildren: 0.12 } },
};
const item = {
  initial: { opacity: 0, y: 12 },
  animate: { opacity: 1, y: 0, transition: { duration: 0.44, ease: EASE_DEPTH } },
};

interface FirstRunScreenProps {
  onContinue: () => void;
}

export function FirstRunScreen({ onContinue }: FirstRunScreenProps) {
  const { t } = useTranslation();
  const systemInfo = useSessionStore(s => s.systemInfo);
  const loading = useSessionStore(s => s.systemInfoLoading);
  const loadSystemInfo = useSessionStore(s => s.loadSystemInfo);
  const updateGtaPath = useSessionStore(s => s.updateGtaPath);

  const [pathError, setPathError] = useState<string | null>(null);
  const [picking, setPicking] = useState(false);

  useEffect(() => { void loadSystemInfo(); }, [loadSystemInfo]);

  const handleChangePath = async () => {
    if (picking) return;
    setPicking(true);
    try {
      const path = await bridge.openFolderDialog();
      if (!path) return;
      try {
        await updateGtaPath(path);
        setPathError(null);
      } catch {
        setPathError(t('firstRun.invalidGtaPath'));
      }
    } finally {
      setPicking(false);
    }
  };

  let state: 'loading' | 'found' | 'notfound' = 'loading';
  if (!loading && systemInfo) state = systemInfo.isGtaFound ? 'found' : 'notfound';

  const haloVar =
    state === 'notfound' ? 'var(--status-warning)' : 'var(--accent)';

  return (
    <div className="relative w-full h-full overflow-hidden flex items-center justify-center px-6">
      {}
      <motion.div
        aria-hidden="true"
        initial={{ opacity: 0 }}
        animate={{ opacity: 0.5 }}
        transition={{ duration: 0.55, ease: EASE_DEPTH, delay: 0.05 }}
        className="absolute pointer-events-none w-[640px] h-[640px] -z-0 blur-3xl transition-colors duration-700"
        style={{
          background: `radial-gradient(circle at 50% 50%, ${haloVar} 0%, transparent 65%)`,
        }}
      />

      <div className="relative z-10 w-full max-w-[560px]">
        <AnimatePresence mode="wait">

          {state === 'loading' && (
            <motion.div key="loading" {...fade}>
              <GlassPanel depth="z3" tint="strong" rounded="3xl" className="overflow-hidden">
                <div className="px-7 pt-7 pb-7 flex flex-col items-center gap-3">
                  <AccentLoader size={40} />
                  <p className="text-text-secondary text-sm">{t('firstRun.searching')}</p>
                </div>
              </GlassPanel>
            </motion.div>
          )}

          {state === 'found' && systemInfo && (
            <motion.div key="found" {...fade}>
              <GlassPanel
                depth="z3" tint="ultra" rounded="3xl" highlight edge
                className="relative overflow-hidden border border-white/[0.08]"
              >
                <span
                  aria-hidden
                  className="absolute top-0 inset-x-0 h-px pointer-events-none
                             bg-gradient-to-r from-transparent via-white/45 to-transparent"
                />
                <span
                  aria-hidden
                  className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl"
                  style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
                />

                <motion.div variants={group} initial="initial" animate="animate">
                  <motion.div
                    variants={item}
                    className="px-7 pt-6 pb-5 border-b border-glass-border flex items-center gap-4"
                  >
                    <StatusBadge status="found" />
                    <div className="flex-1 min-w-0">
                      <h1 className="font-display text-[22px] font-bold text-text-primary tracking-tight leading-tight">
                        {t('firstRun.foundTitle')}
                      </h1>
                      <p className="text-[13px] text-text-secondary mt-1 leading-relaxed">
                        {t('firstRun.foundSubtitle', { defaultValue: 'Готово к работе' })}
                      </p>
                    </div>
                  </motion.div>

                  <motion.div variants={item} className="px-5 pt-5 pb-1">
                    <div className="rounded-2xl bg-bg-elevated/30 border border-glass-border divide-y divide-glass-border overflow-hidden">
                      <Row
                        icon={<Folder size={15} />}
                        label={t('firstRun.gtaPath')}
                        value={systemInfo.gtaPath ?? ''}
                        valueMono
                        trailing={
                          <button
                            type="button"
                            onClick={handleChangePath}
                            disabled={picking}
                            title={t('firstRun.changePath')}
                            aria-label={t('firstRun.changePath')}
                            style={{ outline: 'none' }}
                            className="w-8 h-8 -mr-1 rounded-lg flex items-center justify-center
                                       text-text-muted hover:text-accent hover:bg-glass
                                       transition-colors duration-200
                                       disabled:opacity-60"
                          >
                            <FolderOpen size={14} />
                          </button>
                        }
                      />
                      {systemInfo.gpuName && (
                        <Row
                          icon={<Cpu size={15} />}
                          label={t('firstRun.gpu')}
                          value={systemInfo.gpuName}
                        />
                      )}
                    </div>

                    {pathError && (
                      <p className="mt-3 text-xs px-1" style={{ color: 'var(--status-error)' }}>
                        {pathError}
                      </p>
                    )}
                  </motion.div>

                  <motion.div variants={item} className="px-7 pt-4 pb-6 flex items-center justify-end">
                    <ContinueButton
                      label={t('firstRun.continue')}
                      onClick={onContinue}
                    />
                  </motion.div>
                </motion.div>
              </GlassPanel>
            </motion.div>
          )}

          {state === 'notfound' && (
            <motion.div key="notfound" {...fade}>
              <GlassPanel
                depth="z3" tint="ultra" rounded="3xl" highlight edge
                className="relative overflow-hidden border border-white/[0.08]"
              >
                <span
                  aria-hidden
                  className="absolute top-0 inset-x-0 h-px pointer-events-none
                             bg-gradient-to-r from-transparent via-white/40 to-transparent"
                />
                <motion.div variants={group} initial="initial" animate="animate">
                  <motion.div
                    variants={item}
                    className="px-7 pt-6 pb-5 border-b border-glass-border flex items-center gap-4"
                  >
                    <StatusBadge status="notfound" />
                    <div className="flex-1 min-w-0">
                      <h1 className="font-display text-[22px] font-bold text-text-primary tracking-tight leading-tight">
                        {t('firstRun.notFoundTitle')}
                      </h1>
                      <p className="text-[13px] text-text-secondary mt-1 leading-relaxed">
                        {t('firstRun.notFoundDescription')}
                      </p>
                    </div>
                  </motion.div>

                  <motion.div variants={item} className="px-7 py-6 flex flex-col gap-3">
                    {pathError && (
                      <p className="text-xs" style={{ color: 'var(--status-error)' }}>
                        {pathError}
                      </p>
                    )}
                    <ContinueButton
                      label={t('firstRun.selectFolder')}
                      leadingIcon={<FolderOpen size={16} className="text-accent" />}
                      busy={picking}
                      onClick={handleChangePath}
                    />
                  </motion.div>
                </motion.div>
              </GlassPanel>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: 'found' | 'notfound' }) {
  const isFound = status === 'found';
  const tintClass = isFound
    ? 'bg-accent-soft'
    : 'bg-[color-mix(in_srgb,var(--status-warning)_22%,transparent)]';
  const ringColor = isFound ? 'var(--accent)' : 'var(--status-warning)';
  return (
    <div className="relative w-12 h-12 shrink-0">
      <motion.span
        aria-hidden
        className="absolute inset-0 rounded-2xl blur-md"
        style={{ background: `radial-gradient(circle, ${ringColor} 0%, transparent 70%)` }}
        initial={{ opacity: 0.3, scale: 0.85 }}
        animate={{ opacity: [0.3, 0.55, 0.3], scale: [0.85, 1.12, 0.85] }}
        transition={{ duration: isFound ? 2.8 : 2.0, ease: 'easeInOut', repeat: Infinity }}
      />
      <div className={
        'relative w-12 h-12 rounded-2xl flex items-center justify-center ' +
        'border border-white/[0.08] ' + tintClass
      }>
        {isFound
          ? <CheckCircle2 size={20} className="text-accent" />
          : <ShieldAlert size={20} style={{ color: 'var(--status-warning)' }} />}
      </div>
    </div>
  );
}

function Row({
  icon, label, value, valueMono, trailing,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  valueMono?: boolean;
  trailing?: React.ReactNode;
}) {
  return (
    <div className="px-4 py-3.5 flex items-center gap-3">
      <span className="w-8 h-8 rounded-lg bg-glass-strong text-text-secondary flex items-center justify-center shrink-0">
        {icon}
      </span>
      <div className="flex-1 min-w-0 flex flex-col">
        <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">
          {label}
        </span>
        <span className={
          'text-text-primary truncate ' +
          (valueMono ? 'font-mono text-xs' : 'text-sm font-medium')
        }>
          {value}
        </span>
      </div>
      {trailing ? (
        <div className="shrink-0">{trailing}</div>
      ) : (
        <ChevronRight size={14} className="shrink-0 text-text-muted/40" />
      )}
    </div>
  );
}

function ContinueButton({
  label, onClick, busy, leadingIcon,
}: {
  label: string;
  onClick: () => void;
  busy?: boolean;
  leadingIcon?: React.ReactNode;
}) {
  return (
    <motion.button
      type="button"
      onClick={onClick}
      disabled={busy}
      initial={false}
      whileHover={busy ? undefined : { scale: 1.02 }}
      whileTap={busy ? undefined : { scale: 0.985 }}
      transition={{ type: 'spring', stiffness: 380, damping: 24 }}
      style={{ outline: 'none' }}
      className={
        'group relative inline-flex items-center gap-3 ' +
        'pl-7 pr-5 py-3 rounded-2xl ' +
        'bg-[color-mix(in_srgb,var(--accent-soft)_55%,transparent)] ' +
        'backdrop-blur-glass ' +
        'border border-[color-mix(in_srgb,var(--accent)_35%,transparent)] ' +
        'hover:border-[color-mix(in_srgb,var(--accent)_70%,transparent)] ' +
        'transition-[border-color,box-shadow,opacity] duration-300 ease-smooth ' +
        'shadow-[0_8px_32px_-12px_color-mix(in_srgb,var(--accent)_40%,transparent)] ' +
        'hover:shadow-[0_12px_44px_-12px_color-mix(in_srgb,var(--accent)_70%,transparent)] ' +
        'disabled:opacity-60 disabled:cursor-wait'
      }
    >
      {}
      <span
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 rounded-2xl
                   bg-[radial-gradient(circle_at_50%_50%,color-mix(in_srgb,var(--accent)_25%,transparent),transparent_70%)]
                   opacity-60 group-hover:opacity-90 transition-opacity duration-500"
      />

      {leadingIcon && (
        <span className="relative w-9 h-9 rounded-xl flex items-center justify-center shrink-0
                         bg-[color-mix(in_srgb,var(--accent)_18%,transparent)]
                         border border-[color-mix(in_srgb,var(--accent)_25%,transparent)]">
          {leadingIcon}
        </span>
      )}

      <span className="relative font-display font-bold text-[15px] uppercase tracking-[0.08em] text-text-primary">
        {label}
      </span>

      {!leadingIcon && (
        <span
          className="relative w-9 h-9 rounded-xl flex items-center justify-center shrink-0
                     bg-[color-mix(in_srgb,var(--accent)_18%,transparent)]
                     border border-[color-mix(in_srgb,var(--accent)_25%,transparent)]
                     transition-transform duration-300 ease-smooth
                     group-hover:translate-x-1.5"
        >
          {busy
            ? <span className="block w-3 h-3 rounded-full border-2 border-accent border-t-transparent animate-spin" />
            : <ChevronRight size={18} strokeWidth={2.4} className="text-accent" />}
        </span>
      )}
    </motion.button>
  );
}
