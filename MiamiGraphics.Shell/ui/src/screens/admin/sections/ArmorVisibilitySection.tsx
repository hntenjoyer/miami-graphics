import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { Shield, Eye, Lock, Loader2, Search, Trash2, Image as ImageIcon, RefreshCw, Check, X, BadgeCheck } from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { Toast } from '@/components/Toast';
import { useAdminStore } from '@/store/adminStore';
import { bridge } from '@/bridge';
import type { ReduxItem, ArmorLibraryItem } from '@/bridge/types';

type ArmorRow =
  | { kind: 'redux';   redux: ReduxItem }
  | { kind: 'library'; library: ArmorLibraryItem };

function rowId(r: ArmorRow): string {
  return r.kind === 'redux' ? r.redux.id : r.library.id;
}
function rowName(r: ArmorRow): string {
  return r.kind === 'redux' ? (r.redux.name || r.redux.id) : (r.library.name || r.library.id);
}
function rowAuthor(r: ArmorRow): string {
  return r.kind === 'redux' ? (r.redux.author ?? '') : (r.library.author ?? '');
}
function rowHidden(r: ArmorRow): boolean {
  return r.kind === 'redux'
    ? !!r.redux.armorStandaloneInstallHidden
    : r.library.status !== 'published';
}
function rowVerified(r: ArmorRow): boolean {
  return r.kind === 'redux' ? !!r.redux.isVerified : !!r.library.isVerified;
}

export function ArmorVisibilitySection() {
  const { t } = useTranslation();
  const catalog     = useAdminStore(s => s.catalog);
  const loadCatalog = useAdminStore(s => s.loadCatalog);
  const updateCatalog = useAdminStore(s => s.updateCatalog);

  const [search, setSearch] = useState('');
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast]   = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [previewFor, setPreviewFor] = useState<ArmorRow | null>(null);

  const [libraryItems, setLibraryItems] = useState<ArmorLibraryItem[]>([]);
  const [libraryLoaded, setLibraryLoaded] = useState(false);
  const [backfilling, setBackfilling] = useState(false);
  const reloadLibrary = async () => {
    try {
      const rows = await bridge.armorLibraryListAll();
      setLibraryItems(rows ?? []);
    } catch (e) {
      console.warn('[armorVisibility] libraryListAll fail:', e);
    } finally {
      setLibraryLoaded(true);
    }
  };

  useEffect(() => {
    if (catalog.length === 0) void loadCatalog();
    void reloadLibrary();
  }, []);

  const initialLoading = !libraryLoaded;

  const armorRows = useMemo<ArmorRow[]>(() => {
    const q = search.trim().toLowerCase();
    const matches = (name: string, author: string, id: string) => {
      if (!q) return true;
      return name.toLowerCase().includes(q)
          || author.toLowerCase().includes(q)
          || id.toLowerCase().includes(q);
    };

    const reduxRows: ArmorRow[] = catalog
      .filter(r => !!r.components?.armor?.isFound)
      .filter(r => matches(r.name ?? '', r.author ?? '', r.id))
      .map(r => ({ kind: 'redux', redux: r }));

    const libraryRows: ArmorRow[] = libraryItems
      .filter(a => matches(a.name ?? '', a.author ?? '', a.id))
      .map(a => ({ kind: 'library', library: a }));

    const all = [...reduxRows, ...libraryRows];
    all.sort((a, b) => {
      const aHidden = rowHidden(a) ? 1 : 0;
      const bHidden = rowHidden(b) ? 1 : 0;
      if (aHidden !== bHidden) return aHidden - bHidden;
      return rowName(a).localeCompare(rowName(b));
    });
    return all;
  }, [catalog, libraryItems, search]);

  const toggleRedux = async (item: ReduxItem) => {
    setBusyId(item.id);
    try {
      await updateCatalog({
        ...item,
        armorStandaloneInstallHidden: !item.armorStandaloneInstallHidden,
      });
      setToast({
        tone: 'success',
        message: !item.armorStandaloneInstallHidden
          ? t('admin.armorVisibility.toastHidden', { name: item.name || item.id })
          : t('admin.armorVisibility.toastShown',  { name: item.name || item.id }),
      });
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusyId(null);
    }
  };

  const toggleLibrary = async (item: ArmorLibraryItem) => {
    setBusyId(item.id);
    try {
      const wasVisible = item.status === 'published';
      const ok = await bridge.armorLibrarySetVisibility(item.id, !wasVisible);
      if (!ok) throw new Error('Bridge returned false');
      await reloadLibrary();
      setToast({
        tone: 'success',
        message: wasVisible
          ? t('admin.armorVisibility.toastHidden', { name: item.name || item.id })
          : t('admin.armorVisibility.toastShown',  { name: item.name || item.id }),
      });
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusyId(null);
    }
  };

  const toggle = (r: ArmorRow) => {
    if (busyId) return;
    if (r.kind === 'redux')   void toggleRedux(r.redux);
    if (r.kind === 'library') void toggleLibrary(r.library);
  };

  const deleteLibrary = async (item: ArmorLibraryItem) => {
    const ok = window.confirm(
      `Удалить «${item.name || item.id}» полностью? Файлы из R2 и запись из БД будут удалены безвозвратно.`,
    );
    if (!ok) return;
    setBusyId(item.id);
    try {
      await bridge.armorLibraryDelete(item.id);
      await reloadLibrary();
      setToast({
        tone: 'success',
        message: `«${item.name || item.id}» удалён.`,
      });
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusyId(null);
    }
  };

  const toggleLibraryServer = async (item: ArmorLibraryItem, server: string) => {
    if (busyId) return;
    const current = Array.isArray(item.supportedServers)
      ? item.supportedServers
      : ['majestic'];
    const has = current.includes(server);
    const next = has
      ? current.filter(s => s !== server)
      : [...current, server];
    if (next.length === 0) {
      setToast({
        tone: 'error',
        message: 'Нельзя убрать последний сервер - броник должен быть привязан хотя бы к одному.',
      });
      return;
    }
    setBusyId(item.id);
    try {
      const ok = await bridge.armorLibrarySetSupportedServers(item.id, next);
      if (!ok) throw new Error('Bridge returned false');
      await reloadLibrary();
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusyId(null);
    }
  };

  const visibleCount = armorRows.filter(r => !rowHidden(r)).length;
  const hiddenCount  = armorRows.length - visibleCount;

  const missingPreviewCount = useMemo(
    () => catalog.filter(r =>
      !!r.components?.armor?.isFound
      && !(r.componentScreenshots?.armor && r.componentScreenshots.armor.length > 0)
    ).length,
    [catalog],
  );

  const handleBackfill = async () => {
    if (backfilling) return;
    setBackfilling(true);
    try {
      const r = await bridge.reduxArmorBackfillPreviews();
      await loadCatalog();
      setToast({
        tone: r.rendered > 0 ? 'success' : 'error',
        message: `Превью брони: отрендерено ${r.rendered} из ${r.total}.`,
      });
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBackfilling(false);
    }
  };

  return (
    <div className="h-full overflow-y-auto px-8 py-6 space-y-5">
      <header className="flex items-center gap-4">
        <span className="shrink-0 w-11 h-11 rounded-2xl bg-accent-soft text-accent
                         flex items-center justify-center shadow-z1">
          <Shield size={18} />
        </span>
        <div className="min-w-0 flex-1">
          <h1 className="font-display text-xl font-bold tracking-[0.12em] text-text-primary uppercase leading-tight">
            {t('admin.armorVisibility.title')}
          </h1>
          <p className="text-xs text-text-secondary mt-0.5">
            {t('admin.armorVisibility.subtitle')}
          </p>
        </div>
      </header>

      <div className="flex flex-wrap items-stretch gap-3">
        <StatCard icon={<Shield size={14} />}
                  label="Всего"   value={initialLoading ? null : armorRows.length} tone="muted" />
        <StatCard icon={<Eye size={14} />}
                  label="Видны"   value={initialLoading ? null : visibleCount}     tone="ok" />
        <StatCard icon={<Lock size={14} />}
                  label="Скрыты"  value={initialLoading ? null : hiddenCount}      tone="muted" />
        <div className="flex-1" />

        {missingPreviewCount > 0 && (
          <button
            type="button"
            onClick={() => void handleBackfill()}
            disabled={backfilling}
            title="Отрендерить PNG-превью брони для редуксов, у которых каталог сейчас показывает inline 3D."
            style={{ outline: 'none' }}
            className="inline-flex items-center gap-2 h-11 px-4 rounded-2xl
                       bg-accent-soft text-accent
                       border border-[color-mix(in_srgb,var(--accent)_35%,transparent)]
                       hover:bg-[color-mix(in_srgb,var(--accent)_14%,transparent)]
                       hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                       text-[12px] font-bold uppercase tracking-[0.08em]
                       transition-colors disabled:opacity-60 disabled:cursor-wait"
          >
            {backfilling
              ? <Loader2 size={14} className="animate-spin" />
              : <ImageIcon size={14} />}
            {backfilling ? 'Рендерим…' : `Превью брони (${missingPreviewCount})`}
          </button>
        )}

        <label className="relative flex items-center w-[320px] max-w-full">
          <Search size={14} className="absolute left-3.5 text-text-muted pointer-events-none" />
          <input
            type="text" value={search} onChange={e => setSearch(e.target.value)}
            placeholder={t('admin.armorVisibility.searchPlaceholder')}
            className="w-full h-11 pl-10 pr-3 rounded-2xl bg-glass-strong border border-glass-border
                       text-sm text-text-primary placeholder:text-text-muted
                       outline-none transition-colors focus:border-accent"
          />
        </label>
      </div>

      <AnimatePresence mode="wait" initial={false}>
        {initialLoading ? (
          <motion.div
            key="skeletons"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.28, ease: EASE_DEPTH }}
            className="flex flex-col gap-3"
          >
            {Array.from({ length: 6 }).map((_, i) => <RowSkeleton key={i} delay={i * 60} />)}
          </motion.div>
        ) : armorRows.length === 0 ? (
          <motion.div
            key="empty"
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.34, ease: EASE_DEPTH }}
          >
            <GlassPanel depth="z1" tint="soft" rounded="2xl" className="py-16
                        flex flex-col items-center justify-center gap-3 text-text-muted">
              <Shield size={40} className="opacity-25" />
              <p className="text-sm">
                {search.trim()
                  ? t('admin.armorVisibility.emptySearch')
                  : t('admin.armorVisibility.empty')}
              </p>
            </GlassPanel>
          </motion.div>
        ) : (
          <motion.div
            key="list"
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.34, ease: EASE_DEPTH }}
            className="flex flex-col gap-3"
          >
            {armorRows.map(r => {
              const id = rowId(r);
              return (
                <UnifiedRow
                  key={r.kind + ':' + id}
                  row={r}
                  busy={busyId === id}
                  disabled={busyId !== null && busyId !== id}
                  onToggle={() => toggle(r)}
                  onDelete={r.kind === 'library' ? () => void deleteLibrary(r.library) : undefined}
                  onChangePreview={() => setPreviewFor(r)}
                  onToggleServer={r.kind === 'library'
                    ? (s) => void toggleLibraryServer(r.library, s)
                    : undefined}
                />
              );
            })}
          </motion.div>
        )}
      </AnimatePresence>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 3500}
      />

      {previewFor && (previewFor.kind === 'library' ? (
        <PreviewPickerModal
          title={previewFor.library.name || previewFor.library.id}
          initialVariants={previewFor.library.previewVariants ?? []}
          initialCurrent={previewFor.library.previewUrl ?? null}
          onRenderVariants={() => bridge.armorLibraryRenderVariants(previewFor.library.id)}
          onSetPreview={(url) => bridge.armorLibrarySetPreview(previewFor.library.id, url)}
          onClose={() => setPreviewFor(null)}
          onSaved={(msg) => { setToast({ tone: 'success', message: msg }); void reloadLibrary(); }}
          onError={(msg) => setToast({ tone: 'error', message: msg })}
        />
      ) : (
        <PreviewPickerModal
          title={previewFor.redux.name || previewFor.redux.id}
          initialVariants={[]}
          initialCurrent={previewFor.redux.componentScreenshots?.armor ?? null}
          onLoadVariants={() => bridge.reduxArmorVariantUrls(previewFor.redux.id)}
          onRenderVariants={() => bridge.reduxArmorRenderVariants(previewFor.redux.id)}
          onSetPreview={(url) => bridge.reduxArmorSetPreview(previewFor.redux.id, url)}
          onClose={() => setPreviewFor(null)}
          onSaved={(msg) => { setToast({ tone: 'success', message: msg }); void loadCatalog(); }}
          onError={(msg) => setToast({ tone: 'error', message: msg })}
        />
      ))}
    </div>
  );
}

function PreviewPickerModal({
  title, initialVariants, initialCurrent, onRenderVariants, onLoadVariants, onSetPreview,
  onClose, onSaved, onError,
}: {
  title: string;
  initialVariants: string[];
  initialCurrent: string | null;
  onRenderVariants: () => Promise<string[]>;
  onLoadVariants?: () => Promise<string[]>;
  onSetPreview: (url: string) => Promise<boolean>;
  onClose: () => void;
  onSaved: (msg: string) => void;
  onError: (msg: string) => void;
}) {
  const [variants, setVariants] = useState<string[]>(initialVariants);
  const [current, setCurrent]   = useState<string | null>(initialCurrent);
  const [rendering, setRendering] = useState(false);
  const [savingUrl, setSavingUrl] = useState<string | null>(null);
  const [bust, setBust]         = useState(0);

  useEffect(() => {
    if (!onLoadVariants || initialVariants.length > 0) return;
    let alive = true;
    void onLoadVariants()
      .then(urls => { if (alive && urls && urls.length > 0) setVariants(urls); })
      .catch(() => {  });
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const render = async () => {
    if (rendering) return;
    setRendering(true);
    try {
      const urls = await onRenderVariants();
      if (!urls || urls.length === 0) {
        onError('Рендерер не создал ракурсы. Открой Admin → Ганпаки и нажми «Установить» в карточке рендерера (скачает рендерер + chromium с нашего сервера, ~334 МБ). После установки повтори.');
      } else {
        setVariants(urls);
        setBust(b => b + 1);
        if (!current) setCurrent(urls[0]);
        onSaved('Ракурсы перерендерены - выбери основной.');
      }
    } catch (e) {
      onError(e instanceof Error ? e.message : String(e));
    } finally {
      setRendering(false);
    }
  };

  const pick = async (url: string) => {
    if (savingUrl) return;
    setSavingUrl(url);
    try {
      const ok = await onSetPreview(url);
      if (!ok) throw new Error('Bridge returned false');
      setCurrent(url);
      onSaved(`Превью «${title}» обновлено.`);
    } catch (e) {
      onError(e instanceof Error ? e.message : String(e));
    } finally {
      setSavingUrl(null);
    }
  };

  const viewLabel = (url: string): string => {
    const m = url.match(/variants\/([a-z]+)\.webp/i);
    const map: Record<string, string> = {
      front: 'Фас', back: 'Спина', left: 'Лево', right: 'Право',
      fl: '3/4 слева', fr: '3/4 справа',
    };
    return m ? (map[m[1].toLowerCase()] ?? m[1]) : 'Превью';
  };

  return createPortal(
    <div
      className="fixed inset-0 z-[140] flex items-center justify-center p-6"
      style={{ background: 'rgba(0,0,0,0.7)', backdropFilter: 'blur(6px)' }}
      onClick={onClose}
    >
      <div
        className="w-full max-w-[760px] max-h-[88vh] flex flex-col rounded-2xl overflow-hidden
                   bg-[#16161d] border border-white/[0.08] shadow-2xl"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-center gap-3 px-6 py-4 border-b border-white/[0.06] shrink-0">
          <span className="w-9 h-9 rounded-xl bg-accent-soft text-accent flex items-center justify-center">
            <ImageIcon size={17} />
          </span>
          <div className="min-w-0 flex-1">
            <h2 className="text-sm font-bold text-text-primary truncate">Превью: {title}</h2>
            <p className="text-[11px] text-text-muted">Выбери ракурс. Если вариантов нет или все «боком» - перерендери.</p>
          </div>
          <button
            type="button" onClick={onClose} aria-label="Закрыть" style={{ outline: 'none' }}
            className="w-8 h-8 rounded-lg flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-white/[0.06] transition-colors"
          >
            <X size={16} />
          </button>
        </div>

        <div className="px-6 py-5 overflow-y-auto">
          {variants.length === 0 ? (
            <div className="py-10 flex flex-col items-center gap-3 text-text-muted">
              <ImageIcon size={34} className="opacity-30" />
              <p className="text-xs text-center max-w-xs">
                Ракурсы ещё не отрендерены для этого броника. Нажми «Перерендерить
                ракурсы» - сгенерируем 6 углов из 3D-модели.
              </p>
            </div>
          ) : (
            <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
              {variants.map(url => {
                const isCurrent = current === url;
                const isSaving  = savingUrl === url;
                const src = bust > 0 ? `${url}?v=${bust}` : url;
                return (
                  <button
                    key={url}
                    type="button"
                    onClick={() => void pick(url)}
                    disabled={!!savingUrl}
                    style={{ outline: 'none' }}
                    className={
                      'relative rounded-xl overflow-hidden border-2 transition-colors group ' +
                      (isCurrent
                        ? 'border-accent'
                        : 'border-transparent hover:border-[color-mix(in_srgb,var(--accent)_50%,transparent)]') +
                      ' disabled:opacity-60'
                    }
                  >
                    <div className="aspect-square bg-bg-elevated/40 flex items-center justify-center">
                      <img src={src} alt={viewLabel(url)} className="w-full h-full object-contain" />
                    </div>
                    <div className="absolute bottom-0 inset-x-0 px-2 py-1 text-[10px] font-semibold
                                    bg-black/55 text-white flex items-center justify-between">
                      <span>{viewLabel(url)}</span>
                      {isSaving
                        ? <Loader2 size={12} className="animate-spin" />
                        : isCurrent && <Check size={12} className="text-accent" />}
                    </div>
                    {isCurrent && (
                      <span className="absolute top-1.5 right-1.5 text-[9px] uppercase tracking-wider
                                       px-1.5 py-0.5 rounded bg-accent text-text-on-accent font-bold">
                        текущее
                      </span>
                    )}
                  </button>
                );
              })}
            </div>
          )}
        </div>

        <div className="px-6 py-4 border-t border-white/[0.06] flex items-center justify-between gap-3 shrink-0">
          <p className="text-[11px] text-text-muted">
            Рендер идёт через Node/Chromium - может занять ~10-20 сек.
          </p>
          <button
            type="button"
            onClick={() => void render()}
            disabled={rendering}
            style={{ outline: 'none' }}
            className="inline-flex items-center gap-2 h-10 px-4 rounded-xl text-sm font-semibold
                       bg-accent-soft text-accent border border-[color-mix(in_srgb,var(--accent)_40%,transparent)]
                       hover:bg-[color-mix(in_srgb,var(--accent)_14%,transparent)]
                       transition-colors disabled:opacity-60 disabled:cursor-wait"
          >
            {rendering ? <Loader2 size={15} className="animate-spin" /> : <RefreshCw size={15} />}
            {rendering ? 'Рендерим ракурсы…' : 'Перерендерить ракурсы'}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

function StatCard({ icon, label, value, tone }:
  { icon: React.ReactNode; label: string; value: number | null; tone: 'ok' | 'muted' }) {
  const accentCls = tone === 'ok' ? 'text-green-300' : 'text-text-muted';
  return (
    <div className="flex items-center gap-3 px-4 h-11 rounded-2xl
                    bg-glass-strong border border-glass-border">
      <span className={`shrink-0 ${accentCls}`}>{icon}</span>
      <span className="text-[10px] uppercase tracking-[0.16em] text-text-muted">{label}</span>
      {value === null
        ? <span className="inline-block h-3.5 w-6 rounded-full bg-white/[0.08] animate-pulse" />
        : <span className="text-[15px] font-bold tabular-nums text-text-primary">{value}</span>}
    </div>
  );
}

function RowSkeleton({ delay = 0 }: { delay?: number }) {
  return (
    <div className="flex items-center gap-4 p-4 rounded-2xl bg-bg-elevated/35
                    animate-pulse" style={{ animationDelay: `${delay}ms` }}>
      <div className="w-14 h-14 rounded-xl bg-white/[0.05] shrink-0" />
      <div className="flex-1 flex flex-col gap-2">
        <div className="h-3 w-1/3 rounded-full bg-white/[0.05]" />
        <div className="h-2.5 w-1/2 rounded-full bg-white/[0.035]" />
        <div className="flex gap-1.5 mt-1">
          <div className="h-5 w-12 rounded-full bg-white/[0.035]" />
          <div className="h-5 w-10 rounded-full bg-white/[0.035]" />
        </div>
      </div>
      <div className="h-10 w-28 rounded-xl bg-white/[0.05] shrink-0" />
      <div className="h-10 w-10 rounded-xl bg-white/[0.035] shrink-0" />
      <div className="h-10 w-10 rounded-xl bg-white/[0.035] shrink-0" />
    </div>
  );
}

function UnifiedRow({
  row, busy, disabled, onToggle, onDelete, onChangePreview, onToggleServer,
}: {
  row: ArmorRow;
  busy: boolean;
  disabled: boolean;
  onToggle: () => void;
  onDelete?: () => void;
  onChangePreview?: () => void;
  onToggleServer?: (server: string) => void;
}) {
  const { t } = useTranslation();
  const hidden    = rowHidden(row);
  const isLibrary = row.kind === 'library';
  const previewUrl: string | null = row.kind === 'library'
    ? row.library.previewUrl
    : (row.redux.componentScreenshots?.armor || null);

  return (
    <div className={
      'group flex items-center gap-4 p-4 rounded-2xl border transition-[border-color,box-shadow,background-color] duration-300 ease-depth ' +
      (hidden
        ? 'bg-bg-elevated/30 border-white/[0.04] opacity-80'
        : 'bg-bg-elevated/55 border-white/[0.06] ') +
      'hover:bg-bg-elevated/75 hover:border-[color-mix(in_srgb,var(--accent)_45%,transparent)] ' +
      'hover:shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_18%,transparent),0_10px_28px_-12px_color-mix(in_srgb,var(--accent)_45%,transparent)]'
    }>
      <div className="relative shrink-0 w-14 h-14 rounded-xl overflow-hidden
                      bg-glass-strong border border-white/[0.06]
                      flex items-center justify-center">
        {previewUrl
          ? <img src={previewUrl} alt={rowName(row)} className="w-full h-full object-contain" />
          : <Shield size={22} className="text-text-muted" />}
        {hidden && (
          <span className="absolute inset-0 bg-black/55 flex items-center justify-center">
            <Lock size={14} className="text-text-muted" />
          </span>
        )}
      </div>

      <div className="min-w-0 flex-1 flex flex-col gap-1.5">
        <div className="flex items-center gap-2 flex-wrap min-w-0">
          <span className="text-[15px] font-semibold text-text-primary truncate">
            {rowName(row)}
          </span>
          {isLibrary && (
            <span className="shrink-0 text-[9px] font-bold uppercase tracking-[0.14em] px-2 py-0.5 rounded
                             bg-accent-soft text-accent border border-[color-mix(in_srgb,var(--accent)_30%,transparent)]">
              кастомный
            </span>
          )}
          {rowVerified(row) && (
            <span className="shrink-0 inline-flex items-center gap-1 text-[9px] font-bold uppercase tracking-[0.14em] px-2 py-0.5 rounded
                             bg-[color-mix(in_srgb,var(--status-success)_15%,transparent)]
                             text-[color-mix(in_srgb,var(--status-success)_85%,white)]
                             border border-[color-mix(in_srgb,var(--status-success)_30%,transparent)]">
              <BadgeCheck size={10} /> verified
            </span>
          )}
        </div>
        <div className="flex items-center gap-2 text-[11.5px] text-text-muted truncate min-w-0">
          {rowAuthor(row) && <span className="truncate">{rowAuthor(row)}</span>}
          {rowAuthor(row) && <span className="opacity-50">·</span>}
          <span className="font-mono opacity-70 truncate">{rowId(row)}</span>
        </div>
        {onToggleServer && row.kind === 'library' && (
          <ServerPills
            servers={Array.isArray(row.library.supportedServers)
              ? row.library.supportedServers
              : ['majestic']}
            disabled={busy || disabled}
            onToggle={onToggleServer}
          />
        )}
      </div>

      <div className="shrink-0 flex items-center gap-2">
        <button
          type="button"
          onClick={onToggle}
          disabled={busy || disabled}
          title={hidden
            ? t('admin.armorVisibility.actionShow')
            : t('admin.armorVisibility.actionHide')}
          style={{ outline: 'none' }}
          className={
            'h-10 px-4 rounded-xl inline-flex items-center gap-2 text-[12px] font-bold uppercase tracking-wider ' +
            'transition-colors disabled:opacity-50 disabled:cursor-not-allowed ' +
            (hidden
              ? 'bg-white/[0.04] text-text-secondary border border-white/[0.08] hover:bg-white/[0.08] hover:text-text-primary'
              : 'bg-[color-mix(in_srgb,var(--status-success)_18%,transparent)] text-[color-mix(in_srgb,var(--status-success)_90%,white)] ' +
                'border border-[color-mix(in_srgb,var(--status-success)_35%,transparent)] hover:bg-[color-mix(in_srgb,var(--status-success)_28%,transparent)]')
          }
        >
          {busy
            ? <Loader2 size={13} className="animate-spin" />
            : hidden
              ? <Lock size={13} />
              : <Eye size={13} />}
          {hidden
            ? t('admin.armorVisibility.statusHidden')
            : t('admin.armorVisibility.statusVisible')}
        </button>
        {onChangePreview && (
          <button
            type="button"
            onClick={onChangePreview}
            disabled={busy || disabled}
            title="Сменить превью (выбрать ракурс)"
            style={{ outline: 'none' }}
            className="h-10 w-10 rounded-xl inline-flex items-center justify-center
                       bg-accent-soft text-accent border border-[color-mix(in_srgb,var(--accent)_30%,transparent)]
                       hover:bg-[color-mix(in_srgb,var(--accent)_16%,transparent)] transition-colors
                       disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <ImageIcon size={15} />
          </button>
        )}
        {onDelete && (
          <button
            type="button"
            onClick={onDelete}
            disabled={busy || disabled}
            title="Удалить из каталога"
            style={{ outline: 'none' }}
            className="h-10 w-10 rounded-xl inline-flex items-center justify-center
                       bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)]
                       text-[color-mix(in_srgb,var(--status-error)_90%,white)]
                       border border-[color-mix(in_srgb,var(--status-error)_28%,transparent)]
                       hover:bg-[color-mix(in_srgb,var(--status-error)_20%,transparent)] transition-colors
                       disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <Trash2 size={15} />
          </button>
        )}
      </div>
    </div>
  );
}

const KNOWN_SERVERS: { id: string; label: string }[] = [
  { id: 'majestic', label: 'Маджестик' },
  { id: 'gta5rp',   label: '5РП' },
];
function ServerPills({
  servers, disabled, onToggle,
}: {
  servers: string[];
  disabled: boolean;
  onToggle: (server: string) => void;
}) {
  return (
    <div className="flex items-center gap-1.5 mt-1.5">
      {KNOWN_SERVERS.map(s => {
        const active = servers.includes(s.id);
        return (
          <button
            key={s.id}
            type="button"
            disabled={disabled}
            onClick={() => onToggle(s.id)}
            className={
              'h-6 px-2 rounded text-[10px] uppercase tracking-[0.16em] font-semibold ' +
              'border transition-[border-color,background-color,box-shadow,color] duration-300 ease-depth ' +
              'disabled:opacity-50 disabled:cursor-not-allowed ' +
              (active

                ? 'bg-accent-soft text-accent border-[color-mix(in_srgb,var(--accent)_60%,transparent)] ' +
                  'shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_30%,transparent),0_4px_14px_-4px_color-mix(in_srgb,var(--accent)_55%,transparent)] ' +
                  'hover:bg-accent/15'

                : 'bg-glass-strong text-text-muted border-transparent hover:text-text-secondary ' +
                  'hover:border-[color-mix(in_srgb,var(--accent)_30%,transparent)]')
            }
            style={{ outline: 'none' }}
            title={active
              ? 'Этот броник работает на «' + s.label + '» - клик отключит.'
              : 'Включить поддержку «' + s.label + '».'}
          >
            {s.label}
          </button>
        );
      })}
    </div>
  );
}
