import { useEffect, useMemo, useState } from 'react';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import {
  Palette, Check, X, Loader2, RefreshCw, Download, Tag, User, Crosshair, Box,
} from 'lucide-react';
import { GlassPanel } from '@/design';
import { bridge } from '@/bridge';
import { GlbViewerModal } from '@/screens/guns/GlbViewerModal';
import { prefetchGlb } from '@/utils/glbPrefetch';
import { useCustomGunsStore } from '@/store/customGunsStore';
import { useSessionStore } from '@/store/sessionStore';
import { Toast, type ToastTone } from '@/components/Toast';
import type { CustomGun } from '@/bridge/types';

const itemV: Variants = {
  hidden:  { opacity: 0, y: 8 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.32, ease: [0.22, 1, 0.36, 1] } },
};

export function CustomGunsReviewSection() {
  const pending      = useCustomGunsStore(s => s.pending);
  const loading      = useCustomGunsStore(s => s.loadingPending);
  const loadError    = useCustomGunsStore(s => s.errorPending);
  const loadPending  = useCustomGunsStore(s => s.loadPending);
  const approveSkin  = useCustomGunsStore(s => s.approve);
  const rejectSkin   = useCustomGunsStore(s => s.reject);

  const auth = useSessionStore(s => s.auth);
  const reviewerId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

  const [busyId, setBusyId] = useState<string | null>(null);
  const [rejectingId, setRejectingId] = useState<string | null>(null);
  const [toast, setToast] = useState<{ tone: ToastTone; message: string } | null>(null);

  useEffect(() => { void loadPending(); }, [loadPending]);

  const onApprove = async (gun: CustomGun) => {
    if (!reviewerId) {
      setToast({ tone: 'error', message: 'Не залогинен - некому одобрить.' });
      return;
    }
    setBusyId(gun.id);
    try {
      await approveSkin(gun.id, reviewerId);
      setToast({ tone: 'success', message: `«${gun.displayName}» одобрен.` });
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : 'Не удалось одобрить.' });
    } finally {
      setBusyId(null);
    }
  };

  const onReject = async (gun: CustomGun, reason: string) => {
    if (!reviewerId) {
      setToast({ tone: 'error', message: 'Не залогинен - некому отклонить.' });
      return;
    }
    setBusyId(gun.id);
    try {
      await rejectSkin(gun.id, reviewerId, reason);
      setToast({ tone: 'success', message: `«${gun.displayName}» отклонён.` });
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
        <Palette size={16} className="text-text-secondary" />
        <h1 className="text-base font-bold text-text-primary">Заявки на скины</h1>
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
        {!loading && loadError && (
          <div className="py-16 text-center max-w-[520px] mx-auto">
            <Palette size={36} className="mx-auto mb-3 opacity-40 text-danger" />
            <div className="text-[14px] font-medium text-text-primary mb-1">Очередь не загрузилась</div>
            <div className="text-[12px] text-text-muted break-words">{loadError}</div>
            <button
              type="button"
              onClick={() => void loadPending()}
              className="mt-4 inline-flex items-center gap-2 px-3 h-8 rounded-lg
                         bg-bg-elevated-soft hover:bg-bg-elevated
                         border border-border-subtle hover:border-border-strong
                         text-[12px] text-text-secondary hover:text-text-primary transition-colors"
            >
              <RefreshCw size={11} />
              Попробовать снова
            </button>
          </div>
        )}
        {!loading && !loadError && pending.length === 0 && (
          <div className="py-16 text-center text-text-muted">
            <Palette size={36} className="mx-auto mb-3 opacity-40" />
            <div className="text-[14px] font-medium text-text-primary mb-1">Очередь пуста</div>
            <div className="text-[12px]">Когда игрок отправит скин на модерацию, он появится здесь.</div>
          </div>
        )}
        {pending.length > 0 && (
          <div className="grid grid-cols-1 gap-4 max-w-[1080px] mx-auto">
            {pending.map(gun => (
              <motion.div key={gun.id} variants={itemV} initial="hidden" animate="visible">
                <SkinReviewCard
                  gun={gun}
                  busy={busyId === gun.id}
                  rejecting={rejectingId === gun.id}
                  onStartReject={() => setRejectingId(gun.id)}
                  onCancelReject={() => setRejectingId(null)}
                  onReject={(reason) => void onReject(gun, reason)}
                  onApprove={() => void onApprove(gun)}
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
  gun:            CustomGun;
  busy:           boolean;
  rejecting:      boolean;
  onStartReject:  () => void;
  onCancelReject: () => void;
  onReject:       (reason: string) => void;
  onApprove:      () => void;
}

function SkinReviewCard({
  gun, busy, rejecting, onStartReject, onCancelReject, onReject, onApprove,
}: CardProps) {
  const [rejectReason, setRejectReason] = useState('');
  const [viewerOpen, setViewerOpen] = useState(false);
  const submittedAt = useMemo(() => new Date(gun.createdAt).toLocaleString('ru-RU', {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  }), [gun.createdAt]);

  return (
    <GlassPanel depth="z1" tint="soft" rounded="2xl" className="overflow-hidden">
      <header className="px-5 pt-4 pb-3 flex items-start gap-4 border-b border-border-subtle">
        {gun.previewUrl ? (
          <img
            src={gun.previewUrl}
            alt=""
            className="w-20 h-14 rounded-lg object-cover bg-bg-base shrink-0 border border-border-subtle"
          />
        ) : (
          <div className="w-20 h-14 rounded-lg bg-bg-base shrink-0 border border-border-subtle
                          flex items-center justify-center text-text-muted">
            <Crosshair size={18} />
          </div>
        )}
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="text-[15px] font-semibold tracking-tight text-text-primary truncate">
              {gun.displayName}
            </h3>
            <code className="text-[10px] text-text-muted font-mono shrink-0">{gun.internalName}</code>
          </div>
          <div className="mt-0.5 text-[12px] text-text-muted flex items-center gap-3 flex-wrap">
            <span className="inline-flex items-center gap-1.5">
              <User size={11} /> <span className="text-text-secondary">{gun.ownerName}</span>
            </span>
            <span className="inline-flex items-center gap-1.5">
              <Tag size={11} /> {gun.category}
            </span>
            <span className="inline-flex items-center gap-1.5">
              <Download size={11} /> <span className="tabular-nums">{gun.downloadCount}</span>
            </span>
            <span className="opacity-70">{submittedAt}</span>
          </div>
        </div>
        {gun.previewUrl && (
          <button
            type="button"
            onClick={() => void bridge.customGunPreviewDownload(
              gun.previewUrl!, `${gun.internalName}_${gun.displayName}`)}
            title="Скачать пререндер (WebP) в «Загрузки»"
            aria-label="Скачать пререндер"
            className="shrink-0 inline-flex items-center gap-1.5 px-3 h-8 rounded-lg text-[12px]
                       text-text-secondary hover:text-text-primary bg-white/[0.04] hover:bg-white/[0.08]
                       border border-border-subtle transition-colors"
            style={{ outline: 'none' }}
          >
            <Download size={13} /> Пререндер
          </button>
        )}
        {gun.glbUrl && (
          <button
            type="button"
            onClick={() => setViewerOpen(true)}
            onPointerEnter={() => prefetchGlb(gun.glbUrl)}
            title="3D-модель скина"
            aria-label="Показать 3D-модель"
            className="shrink-0 inline-flex items-center gap-1.5 px-3 h-8 rounded-lg text-[12px]
                       text-text-secondary hover:text-text-primary bg-white/[0.04] hover:bg-white/[0.08]
                       border border-border-subtle transition-colors"
            style={{ outline: 'none' }}
          >
            <Box size={13} /> 3D
          </button>
        )}
      </header>

      <AnimatePresence>
        {viewerOpen && (
          <GlbViewerModal
            glbUrl={gun.glbUrl}
            title={gun.displayName}
            onClose={() => setViewerOpen(false)}
          />
        )}
      </AnimatePresence>

      {gun.description && (
        <div className="px-5 py-4">
          <p className="text-[12px] text-text-secondary leading-relaxed whitespace-pre-line">
            {gun.description}
          </p>
        </div>
      )}

      {!rejecting ? (
        <footer className="px-5 py-3 flex items-center gap-3 bg-bg-elevated-soft/60 border-t border-border-subtle">
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
            onClick={onApprove}
            disabled={busy}
            className="inline-flex items-center gap-1.5 px-4 h-8 rounded-lg text-[12px] font-medium
                       bg-accent text-text-on-accent hover:bg-accent-hover
                       disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {busy ? <Loader2 size={11} className="animate-spin" /> : <Check size={11} />}
            Одобрить
          </button>
        </footer>
      ) : (
        <footer className="px-5 py-3 bg-bg-elevated-soft/60 border-t border-border-subtle space-y-2">
          <textarea
            rows={2}
            autoFocus
            value={rejectReason}
            onChange={e => setRejectReason(e.target.value)}
            placeholder="Причина отклонения - её увидит автор. Например: «Скин копирует чужую работу»."
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

function plural(n: number, one: string, few: string, many: string): string {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}
