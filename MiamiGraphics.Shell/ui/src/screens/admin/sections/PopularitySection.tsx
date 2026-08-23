import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Star, Search, X, Save, Trash2 } from 'lucide-react';
import { useAdminStore } from '@/store/adminStore';
import { useFeaturedStore } from '@/store/featuredStore';
import { Toast } from '@/components/Toast';
import type { ReduxItem } from '@/bridge/types';

export function PopularitySection() {
  const { t } = useTranslation();
  const catalog = useAdminStore(s => s.catalog);
  const loadCatalog = useAdminStore(s => s.loadCatalog);
  const picks = useFeaturedStore(s => s.picks);
  const loadPicks = useFeaturedStore(s => s.load);
  const setSlot = useFeaturedStore(s => s.setSlot);
  const clearSlot = useFeaturedStore(s => s.clear);

  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [busySlot, setBusySlot] = useState<number | null>(null);

  useEffect(() => { void loadCatalog(); }, [loadCatalog]);
  useEffect(() => { void loadPicks(true); }, [loadPicks]);

  const picksBySlot = useMemo(() => {
    const m = new Map<number, string>();
    for (const p of picks) m.set(p.slotIndex, p.reduxId);
    return m;
  }, [picks]);

  const handleSet = async (slot: number, reduxId: string) => {
    setBusySlot(slot);
    try {
      await setSlot(slot, reduxId);
      setToast({ tone: 'success', message: t('admin.popularity.toastSaved', { slot }) });
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setBusySlot(null);
    }
  };

  const handleClear = async (slot: number) => {
    setBusySlot(slot);
    try {
      await clearSlot(slot);
      setToast({ tone: 'success', message: t('admin.popularity.toastCleared', { slot }) });
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setBusySlot(null);
    }
  };

  return (
    <div className="h-full flex flex-col">
      <header className="h-14 px-6 flex items-center gap-3 border-b border-border-subtle shrink-0">
        <Star size={16} className="text-accent" />
        <h1 className="text-base font-bold text-text-primary">{t('admin.popularity.title')}</h1>
      </header>

      <div className="flex-1 overflow-auto px-6 py-5 flex flex-col gap-3">
        <p className="text-sm text-text-secondary max-w-2xl">{t('admin.popularity.hint')}</p>

        {[1, 2, 3].map(slot => (
          <SlotRow
            key={slot}
            slot={slot}
            currentReduxId={picksBySlot.get(slot) ?? null}
            catalog={catalog}
            busy={busySlot === slot}
            onPick={(id) => void handleSet(slot, id)}
            onClear={() => void handleClear(slot)}
          />
        ))}
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={4000}
      />
    </div>
  );
}

function SlotRow({
  slot, currentReduxId, catalog, busy, onPick, onClear,
}: {
  slot:           number;
  currentReduxId: string | null;
  catalog:        ReduxItem[];
  busy:           boolean;
  onPick:         (reduxId: string) => void;
  onClear:        () => void;
}) {
  const items = catalog;
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState(false);

  const current = currentReduxId
    ? items.find(i => i.id === currentReduxId)
    : null;

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return items.slice(0, 30);
    return items
      .filter(i =>
        i.name.toLowerCase().includes(q) ||
        i.id.toLowerCase().includes(q) ||
        i.author.toLowerCase().includes(q))
      .slice(0, 30);
  }, [items, search]);

  return (
    <div className="rounded-xl bg-bg-surface border border-border-subtle p-4 flex flex-col gap-3">
      <div className="flex items-center gap-3">
        <span className="inline-flex items-center justify-center w-8 h-8 rounded-lg bg-accent-soft text-accent text-sm font-bold tabular-nums">
          {slot}
        </span>
        <span className="text-xs uppercase tracking-[0.18em] text-text-muted">
          {t('admin.popularity.slotLabel', { slot })}
        </span>

        <div className="flex-1" />

        {current && !busy && (
          <button
            type="button"
            onClick={onClear}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs text-text-muted hover:text-status-error hover:bg-bg-elevated transition-colors"
            title={t('admin.popularity.clear')}
          >
            <Trash2 size={12} />
            <span>{t('admin.popularity.clear')}</span>
          </button>
        )}
        {busy && <span className="text-xs text-text-muted">{t('admin.popularity.saving')}</span>}
      </div>

      {}
      <div className="flex items-center gap-3 px-3 py-2 rounded-lg bg-bg-elevated border border-border-subtle">
        {current ? (
          <>
            <div
              className="w-12 h-12 rounded-md bg-bg-base bg-cover bg-center shrink-0 border border-border-subtle"
              style={current.previewUrl ? { backgroundImage: `url("${current.previewUrl}")` } : undefined}
            />
            <div className="flex-1 min-w-0">
              <div className="text-sm font-medium text-text-primary truncate">{current.name}</div>
              <div className="text-xs text-text-muted truncate">{current.author || '-'} · {current.id}</div>
            </div>
          </>
        ) : (
          <div className="text-sm text-text-muted italic">{t('admin.popularity.empty')}</div>
        )}
        <button
          type="button"
          onClick={() => setOpen(o => !o)}
          className="px-3 py-1.5 rounded-lg border border-border-strong text-xs text-text-primary hover:bg-bg-base transition-colors shrink-0"
        >
          {open ? t('admin.popularity.cancel') : (current ? t('admin.popularity.change') : t('admin.popularity.pick'))}
        </button>
      </div>

      {}
      {open && (
        <div className="rounded-lg bg-bg-base border border-border-subtle p-3 flex flex-col gap-2">
          <div className="relative">
            <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-text-muted" />
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder={t('admin.popularity.searchPlaceholder')}
              className="w-full pl-9 pr-9 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent"
              autoFocus
            />
            {search.length > 0 && (
              <button
                type="button"
                onClick={() => setSearch('')}
                className="absolute right-2 top-1/2 -translate-y-1/2 p-1 text-text-muted hover:text-text-primary"
                aria-label="clear"
              >
                <X size={12} />
              </button>
            )}
          </div>
          <ul className="max-h-72 overflow-auto flex flex-col gap-1">
            {filtered.length === 0 && (
              <li className="text-xs text-text-muted italic px-2 py-1.5">{t('admin.popularity.noResults')}</li>
            )}
            {filtered.map(it => (
              <li key={it.id}>
                <button
                  type="button"
                  onClick={() => { onPick(it.id); setOpen(false); setSearch(''); }}
                  className="w-full flex items-center gap-3 px-2 py-2 rounded-md hover:bg-bg-elevated text-left transition-colors"
                >
                  <div
                    className="w-9 h-9 rounded-md bg-bg-elevated bg-cover bg-center shrink-0 border border-border-subtle"
                    style={it.previewUrl ? { backgroundImage: `url("${it.previewUrl}")` } : undefined}
                  />
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium text-text-primary truncate">{it.name}</div>
                    <div className="text-xs text-text-muted truncate">{it.author || '-'} · {it.id}</div>
                  </div>
                  {currentReduxId === it.id && (
                    <Save size={12} className="text-accent shrink-0" />
                  )}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
