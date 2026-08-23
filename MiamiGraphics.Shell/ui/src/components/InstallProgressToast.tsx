import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Loader2, CheckCircle2, AlertTriangle, X } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { useInstallProgressStore, type InstallEntry } from '@/store/installProgressStore';
import { useNavStore } from '@/store/navStore';
import { useToastPresenceStore } from '@/store/toastPresenceStore';

const HIDDEN_IDS = new Set(['redux:bigmap3d']);

const isTerminal = (e: InstallEntry) => e.phase === 'done' || e.phase === 'error';

export function InstallProgressToast() {
  const { t } = useTranslation();
  const byId            = useInstallProgressStore(s => s.byId);
  const activeSection   = useNavStore(s => s.activeId);
  const requestNavigate = useNavStore(s => s.requestNavigate);
  const toastOpen       = useToastPresenceStore(s => s.count > 0);

  const list = useMemo(
    () => Object.values(byId).filter(e => !HIDDEN_IDS.has(e.reduxId)),
    [byId],
  );
  const active = useMemo(() => list.filter(e => !isTerminal(e)), [list]);
  const failed = useMemo(() => list.find(e => e.phase === 'error'), [list]);

  type State = 'running' | 'done' | 'error' | 'hidden';
  const state: State = (() => {
    if (active.length > 0) return 'running';
    if (failed)            return 'error';
    if (list.length > 0)   return 'done';
    return 'hidden';
  })();

  const [dismissed, setDismissed] = useState(false);
  useEffect(() => {
    if (state === 'running') { setDismissed(false); return; }
    if (state === 'hidden') return;
    const id = window.setTimeout(() => setDismissed(true), state === 'error' ? 12_000 : 5_000);
    return () => window.clearTimeout(id);
  }, [state]);

  const visible = state !== 'hidden'
    && !dismissed
    && activeSection !== 'downloads';

  const percent = useMemo(() => {
    if (state === 'done') return 100;
    if (active.length === 0) return 0;
    const sum = active.reduce((acc, e) => acc + Math.min(100, Math.max(0, e.percent)), 0);
    return Math.round(sum / active.length);
  }, [active, state]);

  const label = useMemo(() => {
    if (state === 'error')  return failed?.name ?? t('progress.install.errorTitle', 'Ошибка установки');
    if (state === 'done')   return list.length === 1 ? list[0].name : t('common.done', 'Готово');
    if (active.length === 1) return active[0].name;
    return t('progress.install.multiTitle', 'Устанавливаю: {{n}}', { n: active.length });
  }, [state, active, failed, list, t]);

  const sub = useMemo(() => {
    if (state === 'error') return failed?.errorMessage ?? t('progress.install.openDownloads', 'Открой «Загрузки»');
    if (state === 'done')  return t('progress.install.doneSub', 'Установлено');
    if (active.length === 1) return active[0].detailMessage ?? t('progress.install.runningSub', 'Идёт установка...');
    return t('progress.install.multiSub', 'Идут несколько установок');
  }, [state, active, failed, t]);

  const accent = state === 'done'
    ? 'var(--status-success)'
    : state === 'error'
      ? 'var(--status-error)'
      : 'var(--accent)';

  const bottomClass = toastOpen ? 'bottom-28' : 'bottom-6';
  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          key="install-toast"
          initial={{ opacity: 0, y: 18, scale: 0.97 }}
          animate={{ opacity: 1, y: 0,  scale: 1    }}
          exit   ={{ opacity: 0, y: 18, scale: 0.97 }}
          transition={{ duration: 0.34, ease: [0.22, 1, 0.36, 1] }}
          className={`fixed right-6 ${bottomClass} z-[60] w-[320px]`}
        >
          <div
            role="button"
            tabIndex={0}
            onClick={() => requestNavigate('downloads')}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') requestNavigate('downloads'); }}
            className="relative flex flex-col gap-2 px-4 py-3.5 rounded-2xl backdrop-blur-glass-heavy
                       cursor-pointer transition-transform duration-150 hover:-translate-y-0.5"
            style={{
              background: 'color-mix(in srgb, var(--bg-elevated) 92%, transparent)',
              boxShadow:
                `0 0 0 1px color-mix(in srgb, ${accent} 38%, transparent), `
                + `0 0 0 5px color-mix(in srgb, ${accent} 7%, transparent), `
                + `0 22px 50px -18px color-mix(in srgb, ${accent} 40%, transparent), `
                + `0 10px 22px -12px rgba(0,0,0,0.45), `
                + `inset 0 1px 0 rgba(255,255,255,0.06)`,
              outline: 'none',
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
                {state === 'done'    && <CheckCircle2  size={16} strokeWidth={2.5} />}
                {state === 'error'   && <AlertTriangle size={16} strokeWidth={2.5} />}
                {state === 'running' && <Loader2 size={16} strokeWidth={2.5} className="animate-spin" />}
              </span>

              <div className="flex-1 min-w-0">
                <div className="text-[13px] font-semibold text-text-primary leading-tight truncate">
                  {label}
                </div>
                <div className="text-[11px] text-text-muted leading-tight mt-0.5 truncate">
                  {sub}
                </div>
              </div>

              <div
                className="tabular-nums text-[11px] font-semibold shrink-0 min-w-[34px] text-right"
                style={{ color: accent }}
              >
                {state === 'running' && `${percent}%`}
              </div>

              {state !== 'running' && (
                <button
                  type="button"
                  onClick={(e) => { e.stopPropagation(); setDismissed(true); }}
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

            {state === 'running' && (
              <div className="h-1 rounded-full bg-white/[0.06] overflow-hidden">
                <motion.div
                  className="h-full rounded-full"
                  style={{ background: accent }}
                  animate={{ width: `${percent}%` }}
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
