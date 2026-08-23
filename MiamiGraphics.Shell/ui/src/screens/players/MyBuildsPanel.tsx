import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  ChevronDown, ChevronUp, AlertTriangle, Clock, RotateCcw, Loader2, FileText,
  CheckCircle2, XCircle,
} from 'lucide-react';
import { GlassPanel } from '@/design';
import { useUserBuildsStore } from '@/store/userBuildsStore';
import type { UserBuild } from '@/store/userBuildsStore';

interface Props {
  userId: string;
}

export function MyBuildsPanel({ userId }: Props) {
  const { t } = useTranslation();
  const myPending          = useUserBuildsStore(s => s.myPending);
  const loading            = useUserBuildsStore(s => s.loadingMyPending);
  const loadMyPending      = useUserBuildsStore(s => s.loadMyPending);
  const resubmit           = useUserBuildsStore(s => s.resubmit);

  const [open, setOpen]    = useState(false);
  const [busyId, setBusyId]= useState<string | null>(null);

  useEffect(() => {
    if (open) void loadMyPending(userId);
  }, [open, userId, loadMyPending]);

  useEffect(() => {
    void loadMyPending(userId);
  }, [userId, loadMyPending]);

  if (myPending.length === 0 && !loading) return null;

  const onResubmit = async (build: UserBuild) => {
    if (busyId) return;
    setBusyId(build.id);
    try {
      await resubmit(build.id);
    } catch (e) {

      console.warn('[my-builds] resubmit failed', e);
    } finally {
      setBusyId(null);
    }
  };

  return (
    <GlassPanel depth="z1" tint="soft" rounded="2xl" className="overflow-hidden">
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        className="w-full px-5 py-3 flex items-center gap-3 text-left
                   hover:bg-glass-strong transition-colors"
      >
        <div className="inline-flex items-center justify-center w-8 h-8 rounded-lg
                        bg-status-warning-soft border border-status-warning-border text-status-warning">
          <Clock size={14} />
        </div>
        <div className="flex-1 min-w-0">
          <div className="text-[13px] font-semibold text-text-primary">{t('common.myRequests')}</div>
          <div className="text-[11px] text-text-muted">
            {t('players.myBuilds.pendingCount', {
              count: myPending.length,
              defaultValue:      '{{count}} заявок в работе',
              defaultValue_one:  '{{count}} заявка в работе',
              defaultValue_few:  '{{count}} заявки в работе',
              defaultValue_many: '{{count}} заявок в работе',
            })}
          </div>
        </div>
        {open ? <ChevronUp size={14} className="text-text-muted" /> : <ChevronDown size={14} className="text-text-muted" />}
      </button>

      <AnimatePresence initial={false}>
        {open && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit   ={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
            style={{ overflow: 'hidden' }}
          >
            <div className="border-t border-border-subtle">
              {loading && myPending.length === 0 && (
                <div className="px-5 py-6 flex items-center gap-2 text-text-muted text-[12px]">
                  <Loader2 size={12} className="animate-spin" /> {t('players.myBuilds.loading', 'Загружаем…')}
                </div>
              )}
              <ul className="divide-y divide-border-faint">
                {myPending.map(build => (
                  <li key={build.id} className="px-5 py-3">
                    <MyBuildRow
                      build={build}
                      busy={busyId === build.id}
                      onResubmit={() => void onResubmit(build)}
                    />
                  </li>
                ))}
              </ul>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </GlassPanel>
  );
}

function MyBuildRow({ build, busy, onResubmit }: {
  build: UserBuild; busy: boolean; onResubmit: () => void;
}) {
  const { t } = useTranslation();
  const isRejected = build.status === 'rejected';
  return (
    <div className="flex items-start gap-3">
      <div className={
        'shrink-0 inline-flex items-center justify-center w-6 h-6 rounded-md mt-0.5 ' +
        (isRejected
          ? 'bg-status-error/15 text-status-error'
          : 'bg-status-warning-soft text-status-warning')
      }>
        {isRejected ? <XCircle size={12} /> : <Clock size={12} />}
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <span className="text-[13px] font-semibold text-text-primary truncate">{build.name}</span>
          <code className="text-[10px] text-text-muted font-mono shrink-0">{build.hntCode}</code>
        </div>
        <div className="mt-0.5 text-[11px] text-text-muted">
          {isRejected
            ? t('players.myBuilds.statusRejected', 'Отклонена админом')
            : t('players.myBuilds.statusPending', 'На рассмотрении')}
        </div>
        {isRejected && build.rejectReason && (
          <div className="mt-2 px-3 py-2 rounded-lg bg-status-error/8 border border-status-error/20
                          text-[12px] text-text-secondary leading-relaxed flex items-start gap-2">
            <AlertTriangle size={12} className="text-status-error shrink-0 mt-0.5" />
            <span className="whitespace-pre-line">{build.rejectReason}</span>
          </div>
        )}
        {build.settingsXmlUrl && (
          <div className="mt-1.5 text-[11px]">
            <a href={build.settingsXmlUrl} target="_blank" rel="noopener noreferrer"
               className="inline-flex items-center gap-1.5 text-text-muted hover:text-accent transition-colors">
              <FileText size={11} /> settings.xml
            </a>
          </div>
        )}
      </div>
      {isRejected && (
        <button
          type="button"
          onClick={onResubmit}
          disabled={busy}
          title={t('players.myBuilds.resubmitTitle', 'Отправить заявку заново')}
          className="shrink-0 inline-flex items-center gap-1.5 px-3 h-8 rounded-lg text-[12px] font-medium
                     bg-accent/15 text-accent hover:bg-accent/25 border border-accent/30
                     disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {busy ? <Loader2 size={11} className="animate-spin" /> : <RotateCcw size={11} />}
          {t('players.myBuilds.resubmit', 'Отправить заново')}
        </button>
      )}
      {build.status === 'approved' && (

        <span className="shrink-0 inline-flex items-center gap-1 text-[12px] text-status-success">
          <CheckCircle2 size={12} /> {t('players.myBuilds.statusApproved', 'одобрено')}
        </span>
      )}
    </div>
  );
}
