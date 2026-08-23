import { useEffect, useMemo, useState } from 'react';
import { motion, type Variants } from 'framer-motion';
import {
  Inbox, Check, X, Loader2, RefreshCw, Layers, Crosshair, Mouse, Keyboard,
  Monitor, Headphones, Gauge, Video, FileText, Link2, ExternalLink, Trophy,
} from 'lucide-react';
import { GlassPanel } from '@/design';
import { useUserBuildsStore } from '@/store/userBuildsStore';
import { useSessionStore } from '@/store/sessionStore';
import { Toast, type ToastTone } from '@/components/Toast';
import type { UserBuild } from '@/store/userBuildsStore';

const itemV: Variants = {
  hidden:  { opacity: 0, y: 8 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.32, ease: [0.22, 1, 0.36, 1] } },
};

export function PendingBuildsSection() {
  const pending          = useUserBuildsStore(s => s.pending);
  const loading          = useUserBuildsStore(s => s.loadingPending);
  const loadPending      = useUserBuildsStore(s => s.loadPending);
  const approveBuild     = useUserBuildsStore(s => s.approve);
  const rejectBuild      = useUserBuildsStore(s => s.reject);

  const auth = useSessionStore(s => s.auth);
  const reviewerId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

  const [busyId, setBusyId] = useState<string | null>(null);
  const [rejectingId, setRejectingId] = useState<string | null>(null);
  const [toast, setToast] = useState<{ tone: ToastTone; message: string } | null>(null);

  useEffect(() => { void loadPending(); }, [loadPending]);

  const onApprove = async (build: UserBuild, tier: number | null) => {
    if (!reviewerId) {
      setToast({ tone: 'error', message: 'Не залогинен - некому одобрить.' });
      return;
    }
    setBusyId(build.id);
    try {
      await approveBuild(build.id, reviewerId, tier);
      setToast({ tone: 'success', message: `«${build.name}» одобрена.` });
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : 'Не удалось одобрить.' });
    } finally {
      setBusyId(null);
    }
  };

  const onReject = async (build: UserBuild, reason: string) => {
    if (!reviewerId) {
      setToast({ tone: 'error', message: 'Не залогинен - некому отклонить.' });
      return;
    }
    setBusyId(build.id);
    try {
      await rejectBuild(build.id, reviewerId, reason);
      setToast({ tone: 'success', message: `«${build.name}» отклонена.` });
      setRejectingId(null);
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : 'Не удалось отклонить.' });
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div className="h-full flex flex-col">
      <header className="h-14 px-6 flex items-center gap-3 border-b border-border-subtle shrink-0">
        <Inbox size={16} className="text-text-secondary" />
        <h1 className="text-base font-bold text-text-primary">Заявки на сборки</h1>
        <span className="text-xs text-text-muted tabular-nums">
          {pending.length} {plural(pending.length, 'заявка', 'заявки', 'заявок')}
        </span>
        <div className="flex-1" />
        <button
          type="button"
          onClick={() => void loadPending()}
          disabled={loading}
          className="inline-flex items-center gap-2 px-3 h-8 rounded-lg
                     bg-bg-elevated-soft hover:bg-bg-elevated
                     border border-border-subtle hover:border-border-strong
                     text-[12px] text-text-secondary hover:text-text-primary
                     disabled:opacity-50 transition-colors"
        >
          <RefreshCw size={11} className={loading ? 'animate-spin' : ''} />
          {loading ? 'Загружаем…' : 'Обновить'}
        </button>
      </header>

      <div className="flex-1 overflow-auto px-6 py-4">
        {loading && pending.length === 0 && (
          <div className="py-12 flex items-center justify-center text-text-muted gap-2">
            <Loader2 size={14} className="animate-spin" />
            <span className="text-[12px]">Загружаем заявки…</span>
          </div>
        )}
        {!loading && pending.length === 0 && (
          <div className="py-16 text-center text-text-muted">
            <Inbox size={36} className="mx-auto mb-3 opacity-40" />
            <div className="text-[14px] font-medium text-text-primary mb-1">Очередь пуста</div>
            <div className="text-[12px]">Когда юзер отправит заявку, она появится здесь.</div>
          </div>
        )}
        {pending.length > 0 && (
          <div className="grid grid-cols-1 gap-4 max-w-[1080px] mx-auto">
            {pending.map(build => (
              <motion.div key={build.id} variants={itemV} initial="hidden" animate="visible">
                <BuildReviewCard
                  build={build}
                  busy={busyId === build.id}
                  rejecting={rejectingId === build.id}
                  onStartReject={() => setRejectingId(build.id)}
                  onCancelReject={() => setRejectingId(null)}
                  onReject={(reason) => void onReject(build, reason)}
                  onApprove={(tier) => void onApprove(build, tier)}
                />
              </motion.div>
            ))}
          </div>
        )}
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'info'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={4000}
      />
    </div>
  );
}

interface CardProps {
  build:          UserBuild;
  busy:           boolean;
  rejecting:      boolean;
  onStartReject:  () => void;
  onCancelReject: () => void;
  onReject:       (reason: string) => void;
  onApprove:      (tier: number | null) => void;
}

function BuildReviewCard({
  build, busy, rejecting, onStartReject, onCancelReject, onReject, onApprove,
}: CardProps) {
  const [tier, setTier] = useState<number | null>(build.tier);
  const [rejectReason, setRejectReason] = useState('');
  const submittedAt = useMemo(() => new Date(build.createdAt).toLocaleString('ru-RU', {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  }), [build.createdAt]);

  const reduxIsExt = build.reduxId.startsWith('ext:');
  const packIsExt  = build.gunpackId.startsWith('ext:');
  const reduxLabel = reduxIsExt ? build.reduxId.slice(4) : build.reduxNameSnapshot;
  const packLabel  = packIsExt  ? build.gunpackId.slice(4) : build.gunpackNameSnapshot;

  return (
    <GlassPanel depth="z1" tint="soft" rounded="2xl" className="overflow-hidden">
      {}
      <header className="px-5 pt-4 pb-3 flex items-start gap-4 border-b border-border-subtle">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="text-[15px] font-semibold tracking-tight text-text-primary truncate">
              {build.name}
            </h3>
            <code className="text-[10px] text-text-muted font-mono shrink-0">
              {build.hntCode}
            </code>
          </div>
          <div className="mt-0.5 text-[12px] text-text-muted">
            от <span className="text-text-secondary">{build.author}</span>
            <span className="mx-1.5 opacity-50">·</span>
            {submittedAt}
          </div>
        </div>
      </header>

      {}
      <div className="px-5 py-3 grid grid-cols-2 gap-x-6 gap-y-2 border-b border-border-subtle">
        <BuildRef icon={<Layers size={12} />} label="Редукс"
                  value={reduxLabel} ext={reduxIsExt} />
        <BuildRef icon={<Crosshair size={12} />} label="Ган-пак"
                  value={packLabel} ext={packIsExt} />
      </div>

      {}
      <div className="px-5 py-4 space-y-3">
        {}
        {(build.devices.mouse || build.devices.keyboard || build.devices.monitor || build.devices.headset) && (
          <div className="grid grid-cols-2 gap-x-6 gap-y-2">
            {build.devices.mouse    && <DeviceRow icon={<Mouse      size={12} />} label="Мышь"        value={build.devices.mouse.name} />}
            {build.devices.keyboard && <DeviceRow icon={<Keyboard   size={12} />} label="Клавиатура"  value={build.devices.keyboard.name} />}
            {build.devices.monitor  && <DeviceRow icon={<Monitor    size={12} />} label="Монитор"
                                                  value={`${build.devices.monitor.name}${build.devices.monitor.hz ? ` · ${build.devices.monitor.hz} Hz` : ''}`} />}
            {build.devices.headset  && <DeviceRow icon={<Headphones size={12} />} label="Гарнитура"   value={build.devices.headset.name} />}
          </div>
        )}

        {}
        {(build.sensitivity !== null || build.dpi !== null || build.resolution) && (
          <div className="flex items-center gap-6 text-[12px] text-text-secondary">
            {build.sensitivity !== null && (
              <span className="inline-flex items-center gap-1.5">
                <Gauge size={11} className="text-text-muted" />
                <span className="tabular-nums">{build.sensitivity}</span>
              </span>
            )}
            {build.dpi !== null && (
              <span className="inline-flex items-center gap-1.5">
                <span className="text-[10px] uppercase tracking-wider text-text-muted">DPI</span>
                <span className="tabular-nums">{build.dpi}</span>
              </span>
            )}
            {build.resolution && (
              <span className="inline-flex items-center gap-1.5">
                <Monitor size={11} className="text-text-muted" />
                <span className="tabular-nums">{build.resolution}</span>
              </span>
            )}
          </div>
        )}

        {}
        {build.description && (
          <p className="text-[12px] text-text-secondary leading-relaxed whitespace-pre-line">
            {build.description}
          </p>
        )}

        {}
        {(build.videoUrl || build.settingsXmlUrl) && (
          <div className="flex items-center gap-3 text-[11px]">
            {build.videoUrl && (
              <a href={build.videoUrl} target="_blank" rel="noopener noreferrer"
                 className="inline-flex items-center gap-1.5 text-text-muted hover:text-accent transition-colors">
                <Video size={11} /> Ролик <ExternalLink size={10} />
              </a>
            )}
            {build.settingsXmlUrl && (
              <a href={build.settingsXmlUrl} target="_blank" rel="noopener noreferrer"
                 className="inline-flex items-center gap-1.5 text-text-muted hover:text-accent transition-colors">
                <FileText size={11} /> settings.xml <ExternalLink size={10} />
              </a>
            )}
          </div>
        )}
      </div>

      {}
      {!rejecting ? (
        <footer className="px-5 py-3 flex items-center gap-3 bg-bg-elevated-soft/60 border-t border-border-subtle">
          <div className="flex items-center gap-2">
            <span className="text-[11px] uppercase tracking-wider text-text-muted">Тир</span>
            {[null, 1, 2, 3].map(opt => (
              <button
                key={String(opt)}
                type="button"
                onClick={() => setTier(opt)}
                disabled={busy}
                className={
                  'inline-flex items-center justify-center w-7 h-7 rounded-md text-[12px] tabular-nums transition-colors ' +
                  (tier === opt
                    ? 'bg-accent text-text-on-accent'
                    : 'bg-glass border border-border-subtle text-text-muted hover:text-text-primary hover:border-border-strong')
                }
                title={opt === null ? 'Без тира' : `TIER ${opt}`}
              >
                {opt === null ? '-' : opt}
              </button>
            ))}
          </div>
          <div className="flex-1" />
          <button
            type="button"
            onClick={onStartReject}
            disabled={busy}
            className="inline-flex items-center gap-1.5 px-3 h-8 rounded-lg text-[12px]
                       text-text-muted hover:text-status-error hover:bg-status-error/10
                       disabled:opacity-50 transition-colors"
          >
            <X size={12} /> Отклонить
          </button>
          <button
            type="button"
            onClick={() => onApprove(tier)}
            disabled={busy}
            className="inline-flex items-center gap-1.5 px-4 h-8 rounded-lg text-[12px] font-medium
                       bg-accent text-text-on-accent hover:bg-accent-hover
                       disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {busy ? <Loader2 size={11} className="animate-spin" /> : <Check size={11} />}
            Одобрить
            {tier !== null && (
              <span className="inline-flex items-center gap-1 ml-1 px-1.5 h-4 rounded bg-text-on-accent/20 text-[10px] font-bold">
                <Trophy size={9} /> T{tier}
              </span>
            )}
          </button>
        </footer>
      ) : (
        <footer className="px-5 py-3 bg-bg-elevated-soft/60 border-t border-border-subtle space-y-2">
          <textarea
            rows={2}
            autoFocus
            value={rejectReason}
            onChange={e => setRejectReason(e.target.value)}
            placeholder="Причина отклонения - её увидит автор. Например: «Приложи settings.xml»."
            className="w-full px-3 py-2 rounded-lg
                       bg-bg-base border border-border-subtle hover:border-border-strong
                       focus:border-status-error focus:bg-bg-base
                       text-[12px] text-text-primary placeholder:text-text-muted
                       outline-none transition-colors leading-relaxed resize-none"
          />
          <div className="flex items-center gap-2">
            <div className="flex-1" />
            <button
              type="button"
              onClick={onCancelReject}
              disabled={busy}
              className="px-3 h-8 rounded-lg text-[12px] text-text-muted hover:text-text-primary transition-colors"
            >
              Отмена
            </button>
            <button
              type="button"
              onClick={() => onReject(rejectReason.trim())}
              disabled={busy || !rejectReason.trim()}
              className="inline-flex items-center gap-1.5 px-3 h-8 rounded-lg text-[12px] font-medium
                         bg-status-error text-white hover:opacity-90
                         disabled:opacity-50 disabled:cursor-not-allowed transition-opacity"
            >
              {busy ? <Loader2 size={11} className="animate-spin" /> : <X size={11} />}
              Отклонить
            </button>
          </div>
        </footer>
      )}
    </GlassPanel>
  );
}

function BuildRef({ icon, label, value, ext }: {
  icon: React.ReactNode; label: string; value: string; ext: boolean;
}) {
  return (
    <div className="flex items-baseline gap-2 min-w-0">
      <span className="shrink-0 text-text-muted">{icon}</span>
      <span className="text-[10px] uppercase tracking-wider text-text-muted shrink-0">{label}</span>
      <span className="text-[13px] text-text-primary truncate" title={value}>{value || '-'}</span>
      {ext && (
        <span className="shrink-0 inline-flex items-center gap-1 px-1.5 h-4 rounded
                         bg-status-warning-soft text-status-warning border border-status-warning-border
                         text-[9px] uppercase font-bold tracking-wider"
              title="Юзер дал внешнюю ссылку - найди в каталоге или загрузи">
          <Link2 size={8} /> link
        </span>
      )}
    </div>
  );
}

function DeviceRow({ icon, label, value }: {
  icon: React.ReactNode; label: string; value: string;
}) {
  return (
    <div className="flex items-baseline gap-2 min-w-0">
      <span className="shrink-0 text-text-muted">{icon}</span>
      <span className="text-[10px] uppercase tracking-wider text-text-muted shrink-0">{label}</span>
      <span className="text-[13px] text-text-secondary truncate" title={value}>{value}</span>
    </div>
  );
}

function plural(n: number, one: string, few: string, many: string): string {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}
