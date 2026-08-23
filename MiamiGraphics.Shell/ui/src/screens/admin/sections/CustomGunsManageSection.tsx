import { useEffect, useMemo, useState } from 'react';
import {
  Palette, Loader2, RefreshCw, Search, Pencil, Trash2, EyeOff, Box,
  Download, User, Check, X, AlertTriangle,
} from 'lucide-react';
import { AnimatePresence } from 'framer-motion';
import { GlassPanel } from '@/design';
import { useCustomGunsStore } from '@/store/customGunsStore';
import { Toast, type ToastTone } from '@/components/Toast';
import { GlbViewerModal } from '@/screens/guns/GlbViewerModal';
import type { CustomGun } from '@/bridge/types';

const STATUS_TABS: Array<{ id: string; label: string }> = [
  { id: '',          label: 'Активные' },
  { id: 'published', label: 'В каталоге' },
  { id: 'pending',   label: 'На модерации' },
  { id: 'rejected',  label: 'Отклонённые' },
  { id: 'saved',     label: 'Черновики' },
  { id: 'all',       label: 'Все + снятые' },
];

const CATEGORIES: Array<[string, string]> = [
  ['assault', 'Штурмовая'], ['smg', 'ПП'], ['shotgun', 'Дробовик'],
  ['sniper', 'Снайперская'], ['pistol', 'Пистолет'], ['mg', 'Пулемёт'],
  ['heavy', 'Тяжёлое'], ['melee', 'Ближний бой'],
];

const STATUS_STYLE: Record<string, string> = {
  published: 'bg-status-success/15 text-status-success',
  pending:   'bg-status-warning/15 text-status-warning',
  rejected:  'bg-status-error/15 text-status-error',
  saved:     'bg-white/[0.06] text-text-secondary',
  removed:   'bg-white/[0.06] text-text-muted line-through',
};

const STATUS_LABEL: Record<string, string> = {
  published: 'в каталоге', pending: 'на модерации', rejected: 'отклонён',
  saved: 'черновик', removed: 'снят',
};

export function CustomGunsManageSection() {
  const rows        = useCustomGunsStore(s => s.manage);
  const loading     = useCustomGunsStore(s => s.loadingManage);
  const loadError   = useCustomGunsStore(s => s.errorManage);
  const status      = useCustomGunsStore(s => s.manageStatus);
  const setFilter   = useCustomGunsStore(s => s.setManageFilter);
  const loadManage  = useCustomGunsStore(s => s.loadManage);
  const adminPatch  = useCustomGunsStore(s => s.adminPatch);
  const adminDelete = useCustomGunsStore(s => s.adminDelete);

  const [query, setQuery]     = useState('');
  const [busyId, setBusyId]   = useState<string | null>(null);
  const [editing, setEditing] = useState<CustomGun | null>(null);
  const [deleting, setDeleting] = useState<CustomGun | null>(null);
  const [viewer, setViewer]   = useState<CustomGun | null>(null);
  const [toast, setToast]     = useState<{ tone: ToastTone; message: string } | null>(null);

  useEffect(() => { void loadManage(); }, [loadManage, status]);

  const shown = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return rows;
    return rows.filter(g =>
      g.displayName.toLowerCase().includes(q)
      || g.ownerName.toLowerCase().includes(q)
      || g.internalName.toLowerCase().includes(q));
  }, [rows, query]);

  const pickStatus = (id: string) => { setFilter(id, ''); };

  return (
    <div className="space-y-5">
      <header className="flex items-center gap-3 flex-wrap">
        <div className="flex items-center gap-2.5 mr-auto">
          <Palette size={18} className="text-accent" />
          <h2 className="text-[17px] font-semibold tracking-tight text-text-primary">Кастомные ганы</h2>
          <span className="text-[12px] text-text-muted tabular-nums">{shown.length}</span>
        </div>
        <div className="relative">
          <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-text-muted" />
          <input
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder="Название, автор, ствол…"
            className="h-8 w-[240px] pl-8 pr-3 rounded-lg text-[12px] bg-white/[0.04]
                       border border-border-subtle text-text-primary placeholder:text-text-muted
                       focus:outline-none focus:border-accent/50"
          />
        </div>
        <button
          type="button"
          onClick={() => void loadManage()}
          disabled={loading}
          className="inline-flex items-center gap-1.5 px-3 h-8 rounded-lg text-[12px]
                     text-text-secondary hover:text-text-primary bg-white/[0.04] hover:bg-white/[0.08]
                     border border-border-subtle disabled:opacity-50 transition-colors"
        >
          {loading ? <Loader2 size={13} className="animate-spin" /> : <RefreshCw size={13} />} Обновить
        </button>
      </header>

      <div className="flex items-center gap-1.5 flex-wrap">
        {STATUS_TABS.map(t => (
          <button
            key={t.id || 'active'}
            type="button"
            onClick={() => pickStatus(t.id)}
            className={'px-3 h-8 rounded-lg text-[12px] font-medium transition-colors '
              + (status === t.id
                ? 'bg-accent text-text-on-accent'
                : 'text-text-secondary hover:text-text-primary bg-white/[0.04] hover:bg-white/[0.08]')}
          >
            {t.label}
          </button>
        ))}
      </div>

      {loadError ? (
        <GlassPanel depth="z1" tint="soft" rounded="2xl" className="px-5 py-4 flex items-start gap-3">
          <AlertTriangle size={16} className="text-status-error shrink-0 mt-0.5" />
          <div>
            <p className="text-[13px] text-text-primary">Не удалось загрузить список.</p>
            <p className="text-[12px] text-text-muted mt-0.5">{loadError}</p>
          </div>
        </GlassPanel>
      ) : loading && rows.length === 0 ? (
        <div className="py-16 flex items-center justify-center gap-2 text-text-muted">
          <Loader2 size={16} className="animate-spin" /> <span className="text-sm">Загружаю…</span>
        </div>
      ) : shown.length === 0 ? (
        <div className="py-16 text-center text-text-muted text-sm">Ничего не нашлось.</div>
      ) : (
        <GlassPanel depth="z1" tint="soft" rounded="2xl" className="overflow-hidden divide-y divide-border-subtle">
          {shown.map(g => (
            <div key={g.id} className="px-4 py-3 flex items-center gap-3">
              {g.previewUrl ? (
                <img src={g.previewUrl} alt="" className="w-16 h-11 rounded-md object-cover bg-bg-base shrink-0 border border-border-subtle" />
              ) : (
                <div className="w-16 h-11 rounded-md bg-bg-base shrink-0 border border-border-subtle flex items-center justify-center text-text-muted">
                  <Palette size={14} />
                </div>
              )}

              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-[14px] font-medium text-text-primary truncate">{g.displayName || '(без названия)'}</span>
                  <span className={'px-1.5 py-0.5 rounded text-[10px] font-bold uppercase tracking-wider shrink-0 ' + (STATUS_STYLE[g.status] ?? '')}>
                    {STATUS_LABEL[g.status] ?? g.status}
                  </span>
                </div>
                <div className="mt-0.5 text-[11.5px] text-text-muted flex items-center gap-3 flex-wrap">
                  <span className="inline-flex items-center gap-1"><User size={10} />{g.ownerName}</span>
                  <code className="font-mono">{g.internalName}</code>
                  <span className="inline-flex items-center gap-1"><Download size={10} /><span className="tabular-nums">{g.downloadCount}</span></span>
                </div>
              </div>

              {g.glbUrl && (
                <button
                  type="button"
                  onClick={() => setViewer(g)}
                  title="3D-модель"
                  aria-label="3D-модель"
                  className="w-8 h-8 shrink-0 rounded-lg flex items-center justify-center border border-border-subtle
                             bg-white/[0.03] text-text-secondary hover:text-text-primary hover:bg-white/[0.07] transition-colors"
                  style={{ outline: 'none' }}
                >
                  <Box size={14} />
                </button>
              )}
              <button
                type="button"
                onClick={() => setEditing(g)}
                title="Переименовать"
                aria-label="Переименовать"
                className="w-8 h-8 shrink-0 rounded-lg flex items-center justify-center border border-border-subtle
                           bg-white/[0.03] text-text-secondary hover:text-text-primary hover:bg-white/[0.07] transition-colors"
                style={{ outline: 'none' }}
              >
                <Pencil size={14} />
              </button>
              <button
                type="button"
                onClick={() => setDeleting(g)}
                disabled={busyId === g.id}
                title="Снять / удалить"
                aria-label="Снять или удалить"
                className="w-8 h-8 shrink-0 rounded-lg flex items-center justify-center border border-border-subtle
                           bg-white/[0.03] text-text-secondary hover:text-status-error hover:border-status-error/40
                           disabled:opacity-50 transition-colors"
                style={{ outline: 'none' }}
              >
                {busyId === g.id ? <Loader2 size={14} className="animate-spin" /> : <Trash2 size={14} />}
              </button>
            </div>
          ))}
        </GlassPanel>
      )}

      {editing && (
        <EditDialog
          gun={editing}
          onClose={() => setEditing(null)}
          onSave={async (patch) => {
            setBusyId(editing.id);
            try {
              await adminPatch(editing.id, patch);
              setToast({ tone: 'success', message: 'Изменения сохранены.' });
              setEditing(null);
            } catch (e) {
              setToast({ tone: 'error', message: e instanceof Error ? e.message : 'Не удалось сохранить.' });
            } finally { setBusyId(null); }
          }}
        />
      )}

      {deleting && (
        <DeleteDialog
          gun={deleting}
          onClose={() => setDeleting(null)}
          onConfirm={async (reason, hard) => {
            setBusyId(deleting.id);
            try {
              await adminDelete(deleting.id, reason, hard);
              setToast({ tone: 'success', message: hard ? 'Ган удалён насовсем.' : 'Ган снят с публикации.' });
              setDeleting(null);
            } catch (e) {
              setToast({ tone: 'error', message: e instanceof Error ? e.message : 'Не удалось удалить.' });
            } finally { setBusyId(null); }
          }}
        />
      )}

      <AnimatePresence>
        {viewer && (
          <GlbViewerModal
            glbUrl={viewer.glbUrl}
            title={viewer.displayName}
            onClose={() => setViewer(null)}
          />
        )}
      </AnimatePresence>

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

function EditDialog({
  gun, onClose, onSave,
}: {
  gun: CustomGun;
  onClose: () => void;
  onSave: (patch: { displayName: string; description: string; category: string }) => Promise<void>;
}) {
  const [name, setName] = useState(gun.displayName);
  const [desc, setDesc] = useState(gun.description);
  const [cat, setCat]   = useState(gun.category);
  const [busy, setBusy] = useState(false);

  return (
    <Overlay onClose={onClose}>
      <div className="text-[15px] font-medium text-text-primary mb-1">Правка гана</div>
      <p className="text-[12px] text-text-muted mb-4">
        Автор: {gun.ownerName} · <code className="font-mono">{gun.internalName}</code>.
        Повторная модерация не запускается.
      </p>

      <label className="block text-[12px] text-text-secondary mb-1">Название</label>
      <input
        value={name}
        onChange={e => setName(e.target.value)}
        maxLength={48}
        className="w-full h-9 px-3 mb-3 rounded-lg text-[13px] bg-white/[0.04] border border-border-subtle
                   text-text-primary focus:outline-none focus:border-accent/50"
      />

      <label className="block text-[12px] text-text-secondary mb-1">Описание</label>
      <textarea
        value={desc}
        onChange={e => setDesc(e.target.value)}
        maxLength={240}
        rows={2}
        className="w-full px-3 py-2 mb-3 rounded-lg text-[13px] bg-white/[0.04] border border-border-subtle
                   text-text-primary resize-y focus:outline-none focus:border-accent/50"
      />

      <label className="block text-[12px] text-text-secondary mb-1">Категория</label>
      <select
        value={cat}
        onChange={e => setCat(e.target.value)}
        className="w-full h-9 px-2 mb-5 rounded-lg text-[13px] bg-bg-elevated border border-border-subtle
                   text-text-primary focus:outline-none focus:border-accent/50"
      >
        {CATEGORIES.map(([id, label]) => <option key={id} value={id}>{label}</option>)}
      </select>

      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          className="px-3 h-8 rounded-lg text-[12px] text-text-secondary hover:text-text-primary
                     bg-white/[0.04] hover:bg-white/[0.08] border border-border-subtle transition-colors"
        >
          Отмена
        </button>
        <button
          type="button"
          disabled={busy || !name.trim()}
          onClick={async () => {
            setBusy(true);
            try { await onSave({ displayName: name.trim(), description: desc, category: cat }); }
            finally { setBusy(false); }
          }}
          className="inline-flex items-center gap-1.5 px-4 h-8 rounded-lg text-[12px] font-medium
                     bg-accent text-text-on-accent hover:bg-accent-hover disabled:opacity-50 transition-colors"
        >
          {busy ? <Loader2 size={12} className="animate-spin" /> : <Check size={12} />} Сохранить
        </button>
      </div>
    </Overlay>
  );
}

function DeleteDialog({
  gun, onClose, onConfirm,
}: {
  gun: CustomGun;
  onClose: () => void;
  onConfirm: (reason: string, hard: boolean) => Promise<void>;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy]     = useState(false);

  const run = async (hard: boolean) => {
    setBusy(true);
    try { await onConfirm(reason.trim(), hard); }
    finally { setBusy(false); }
  };

  return (
    <Overlay onClose={onClose}>
      <div className="text-[15px] font-medium text-text-primary mb-1">Убрать «{gun.displayName}»?</div>
      <p className="text-[12px] text-text-muted mb-4">
        Автор: {gun.ownerName}. «Снять» прячет ган из каталога и из «Моих ганов» автора,
        слот освобождается, строка остаётся для разбора. «Удалить насовсем» стирает запись.
      </p>

      <label className="block text-[12px] text-text-secondary mb-1">Причина <span className="text-text-muted">(увидит автор)</span></label>
      <input
        value={reason}
        onChange={e => setReason(e.target.value)}
        maxLength={200}
        placeholder="Например: нецензурное название"
        className="w-full h-9 px-3 mb-5 rounded-lg text-[13px] bg-white/[0.04] border border-border-subtle
                   text-text-primary placeholder:text-text-muted focus:outline-none focus:border-accent/50"
      />

      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          className="px-3 h-8 rounded-lg text-[12px] text-text-secondary hover:text-text-primary
                     bg-white/[0.04] hover:bg-white/[0.08] border border-border-subtle transition-colors"
        >
          Отмена
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={() => void run(true)}
          className="inline-flex items-center gap-1.5 px-3 h-8 rounded-lg text-[12px]
                     text-status-error hover:bg-status-error/10 border border-status-error/40
                     disabled:opacity-50 transition-colors"
        >
          <X size={12} /> Удалить насовсем
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={() => void run(false)}
          className="inline-flex items-center gap-1.5 px-4 h-8 rounded-lg text-[12px] font-medium
                     bg-accent text-text-on-accent hover:bg-accent-hover disabled:opacity-50 transition-colors"
        >
          {busy ? <Loader2 size={12} className="animate-spin" /> : <EyeOff size={12} />} Снять
        </button>
      </div>
    </Overlay>
  );
}

function Overlay({ children, onClose }: { children: React.ReactNode; onClose: () => void }) {
  return (
    <div
      className="fixed inset-0 z-[95] bg-black/60 backdrop-blur-sm flex items-center justify-center p-6"
      onClick={onClose}
    >
      <div
        className="w-[min(92vw,460px)] rounded-2xl bg-bg-elevated border border-border-subtle p-5 shadow-2xl"
        onClick={e => e.stopPropagation()}
      >
        {children}
      </div>
    </div>
  );
}
