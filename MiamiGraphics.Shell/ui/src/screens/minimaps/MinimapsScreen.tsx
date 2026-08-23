import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Map as MapIcon, Eye, ImageOff, Loader2, Download, X, Check, Sparkles, ArrowRight,
  CheckCircle2, Star,
} from 'lucide-react';
import { useReduxStore } from '@/store/reduxStore';
import { CarbonSurface, EASE_DEPTH } from '@/design';
import { ScreenHero } from '@/screens/ScreenHero';
import { useInstallCounts, useDuplicateNames, installCountFor } from '@/hooks/useInstallCounts';
import { useItemFavorites } from '@/hooks/useItemFavorites';
import { CatalogSortSelect, CatalogFavToggle, type CatalogSort } from '@/screens/catalog/CatalogControls';
import { bridge } from '@/bridge';
import type { ReduxItem, LibraryComponent, CurrentMinimapInfo } from '@/bridge/types';
import { ensureBackupOrGate } from '@/store/installGate';
import { Toast } from '@/components/Toast';
import { ConfirmModal } from '@/components/ConfirmModal';
import { LazyImage } from '@/components/LazyImage';
import { CatalogPreviewGate } from '@/components/CatalogPreviewGate';
import { shuffled } from '@/utils/shuffle';
import { getCachedLibrary, setCachedLibrary } from '@/store/libraryCache';

interface Props {
  onOpenRedux: (reduxId: string) => void;
}

let cachedMinimapOrder: { signature: string; entries: unknown[] } | null = null;

let savedScrollTop = 0;

export function MinimapsScreen({ onOpenRedux: _onOpenRedux }: Props) {
  const { t } = useTranslation();
  const items   = useReduxStore(s => s.items);
  const loading = useReduxStore(s => s.loading);
  const load    = useReduxStore(s => s.load);

  useEffect(() => { if (items.length === 0) void load(); }, []);

  const scrollRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    const el = scrollRef.current;
    if (el && savedScrollTop > 0) {
      requestAnimationFrame(() => requestAnimationFrame(() => {
        if (scrollRef.current) scrollRef.current.scrollTop = savedScrollTop;
      }));
    }
    return () => {
      if (scrollRef.current) savedScrollTop = scrollRef.current.scrollTop;
    };

  }, []);

  const [libraryMinimaps, setLibraryMinimaps] = useState<LibraryComponent[]>(
    getCachedLibrary('minimap') ?? [],
  );
  const [currentMinimap, setCurrentMinimap]   = useState<CurrentMinimapInfo | null>(null);

  const refreshCurrent = async () => {
    try { setCurrentMinimap(await bridge.getCurrentMinimapInfo()); }
    catch (e) { console.warn('[minimaps.current] fail:', e); }
  };

  useEffect(() => {
    let alive = true;
    bridge.libraryList('minimap')
      .then(rows => {
        if (!alive) return;
        const next = rows ?? [];
        setCachedLibrary('minimap', next);
        setLibraryMinimaps(next);
      })
      .catch(e => console.warn('[minimaps.libraryList] fail:', e));
    void refreshCurrent();
    return () => { alive = false; };
  }, []);

  const [search, setSearch] = useState('');
  const installCounts = useInstallCounts('minimap_install');
  const [sort, setSort] = useState<CatalogSort>('default');
  const [onlyFav, setOnlyFav] = useState(false);
  const { favorites, toggle: toggleFav, canFavorite } = useItemFavorites('minimap');
  const [installingId, setInstallingId] = useState<string | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error' | 'info'; message: string } | null>(null);

  const [replacePrompt, setReplacePrompt] = useState<{
    current:  CurrentMinimapInfo;
    incoming: { source: 'redux' | 'library'; id: string; name: string; screenshot: string };
  } | null>(null);

  const [keepRingsPrompt, setKeepRingsPrompt] = useState<{
    radii: number[]; mapName: string; thumb: string;
  } | null>(null);
  const [keepingRings, setKeepingRings] = useState(false);

  type MinimapEntry =
    | { kind: 'redux';   redux: ReduxItem;          screenshot: string }
    | { kind: 'library'; library: LibraryComponent; screenshot: string };

  const minimaps = useMemo<MinimapEntry[]>(() => {
    const reduxEntries: MinimapEntry[] = items
      .filter(r => r.status === 'published')
      .filter(r => r.components?.minimap?.isFound)
      .map(r => ({
        kind:       'redux' as const,
        redux:      r,
        screenshot: r.componentScreenshots?.minimap ?? '',
      }));
    const libraryEntries: MinimapEntry[] = libraryMinimaps.map(l => ({
      kind:       'library' as const,
      library:    l,
      screenshot: l.previewUrl ?? '',
    }));
    const all = [...libraryEntries, ...reduxEntries];
    const signature = all
      .map(e => e.kind === 'redux' ? `r:${e.redux.id}` : `l:${e.library.id}`)
      .sort()
      .join('|');
    if (cachedMinimapOrder?.signature === signature) {
      return cachedMinimapOrder.entries as MinimapEntry[];
    }
    const withImage = all.filter(e => !!e.screenshot);
    const noImage   = all.filter(e => !e.screenshot);
    const entries   = [...shuffled(withImage), ...noImage];
    cachedMinimapOrder = { signature, entries };
    return entries;
  }, [items, libraryMinimaps]);

  const minimapNames = useMemo(
    () => minimaps.map(e => (e.kind === 'redux' ? e.redux.name : e.library.name)),
    [minimaps],
  );
  const dupNames = useDuplicateNames(minimapNames);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const subj = (m: MinimapEntry) => m.kind === 'redux' ? m.redux : m.library;
    let list = !q ? minimaps : minimaps.filter(m => {
      const s = subj(m);
      return s.name.toLowerCase().includes(q)
          || s.author.toLowerCase().includes(q)
          || s.id.toLowerCase().includes(q);
    });
    if (onlyFav) list = list.filter(m => favorites.has(subj(m).id));
    if (sort === 'installs') {
      list = [...list].sort((a, b) =>
        installCountFor(installCounts, dupNames, subj(b)) - installCountFor(installCounts, dupNames, subj(a)));
    } else if (sort === 'name') {
      list = [...list].sort((a, b) => subj(a).name.localeCompare(subj(b).name));
    }
    return list;
  }, [minimaps, search, onlyFav, favorites, sort, installCounts, dupNames]);

  const previewUrls = useMemo(
    () => filtered.slice(0, 16).map(e => e.screenshot).filter(Boolean),
    [filtered],
  );

  const PAGE = 12;
  const [visibleCount, setVisibleCount] = useState(PAGE);
  useEffect(() => { setVisibleCount(PAGE); }, [search, sort, onlyFav]);
  const visible = useMemo(() => filtered.slice(0, visibleCount), [filtered, visibleCount]);
  const onGridScroll = (e: React.UIEvent<HTMLDivElement>) => {
    const el = e.currentTarget;
    if (el.scrollHeight - el.scrollTop - el.clientHeight < 700) {
      setVisibleCount(c => (c < filtered.length ? c + PAGE : c));
    }
  };

  const handleInstall = async (entry: MinimapEntry) => {
    if (installingId || replacePrompt) return;

    const incoming = entry.kind === 'redux'
      ? { source: 'redux'   as const, id: entry.redux.id,   name: entry.redux.name,   screenshot: entry.screenshot }
      : { source: 'library' as const, id: entry.library.id, name: entry.library.name, screenshot: entry.screenshot };

    if (entry.kind === 'library' && !entry.library.r2Url) {
      setToast({
        tone: 'error',
        message: t('minimaps.previewOnly'),
      });
      return;
    }

    if (currentMinimap && currentMinimap.id !== incoming.id) {
      setReplacePrompt({ current: currentMinimap, incoming });
      return;
    }
    if (currentMinimap && currentMinimap.id === incoming.id) {
      setToast({ tone: 'info', message: t('minimaps.alreadyInstalled') });
      return;
    }
    void dispatchInstall(incoming);
  };

  const dispatchInstall = async (incoming: { source: 'redux' | 'library'; id: string; name: string; screenshot?: string }) => {

    if (!ensureBackupOrGate()) return;
    const priorRings = await bridge.minimapGetRangeRings().catch(() => [] as number[]);
    setInstallingId(incoming.id);
    try {
      const r = await bridge.reduxApplyMinimap(incoming.source, incoming.id, incoming.name);
      if (r.success) {
        setToast({ tone: 'success', message: t('minimaps.installedToast', { name: incoming.name }) });
        void bridge.activityLog('minimap_install', `миникарту «${incoming.name}»`, incoming.id);
        await refreshCurrent();
        if (priorRings.length > 0) {
          setKeepRingsPrompt({
            radii: priorRings,
            mapName: incoming.name,
            thumb: incoming.screenshot ?? '',
          });
        }
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setInstallingId(null);
    }
  };

  const confirmKeepRings = async () => {
    if (!keepRingsPrompt || keepingRings) return;
    const radii = keepRingsPrompt.radii;
    setKeepingRings(true);
    try {
      const r = await bridge.minimapSetRangeRings(radii);
      if (r.success) {
        setToast({ tone: 'success', message: t('minimaps.ringsKeptToast', 'Круги нанесены на новую миникарту.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('minimaps.ringsKeepFail', 'Не удалось нанести круги.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setKeepingRings(false);
      setKeepRingsPrompt(null);
    }
  };

  return (
    <div ref={scrollRef} onScroll={onGridScroll} className="h-full overflow-y-auto">
      <div className="max-w-7xl 2xl:max-w-[1700px] mx-auto px-5 pt-3 pb-6 flex flex-col gap-3">
        <ScreenHero
          title={t('minimaps.heroTitle')}
          subtitle={t('minimaps.heroSubtitle')}
          search={{
            value: search,
            onChange: setSearch,
            placeholder: t('minimaps.searchPlaceholder'),
            clearLabel: t('armor.searchClear'),
          }}
          trailing={
            <>
              {canFavorite && (
                <CatalogFavToggle
                  active={onlyFav}
                  onClick={() => setOnlyFav(v => !v)}
                  label={t('redux.favoritesOnly', 'Только избранное')}
                />
              )}
              <CatalogSortSelect value={sort} onChange={setSort} />
              <span className="inline-flex items-center gap-1.5 px-3 h-9 rounded-xl
                               bg-white/[0.04] border border-white/[0.06]
                               text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted shrink-0">
                <span className="tabular-nums text-text-primary">{filtered.length}</span>
                <span>{t('redux.count', { count: filtered.length }).replace(/^\d+\s*/, '')}</span>
              </span>
            </>
          }
        />

        {}

        {loading && minimaps.length === 0 ? (
          <div className="flex items-center gap-2 text-sm text-text-muted py-12 justify-center">
            <Loader2 size={14} className="animate-spin text-accent" />
            <span>{t('catalog.loading')}</span>
          </div>
        ) : filtered.length === 0 ? (
          <div className="flex flex-col items-center justify-center gap-2 py-16 text-text-muted">
            <ImageOff size={28} />
            <p className="text-sm">
              {minimaps.length === 0
                ? t('minimaps.emptyCatalog')
                : t('catalog.noResults')}
            </p>
          </div>
        ) : (
          <CatalogPreviewGate urls={previewUrls} ready={!loading || minimaps.length > 0}>
            <motion.div
              initial="hidden"
              animate="show"
              variants={{
                hidden: { opacity: 0 },
                show:   { opacity: 1, transition: { staggerChildren: 0.04, delayChildren: 0.06 } },
              }}
              className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4"
            >
              {visible.map((entry, i) => {
                const subject = entry.kind === 'redux' ? entry.redux : entry.library;
                const installed = currentMinimap?.id === subject.id;
                const installing = installingId === subject.id;
                return (
                  <MinimapCard
                    installs={installCountFor(installCounts, dupNames, subject)}
                    favorite={favorites.has(subject.id)}
                    onToggleFavorite={canFavorite ? () => toggleFav(subject.id) : undefined}
                    key={(entry.kind === 'redux' ? 'r:' : 'l:') + subject.id}
                    title={subject.name}
                    screenshot={entry.screenshot}
                    installed={installed}
                    installing={installing}
                    index={i}
                    onInstall={() => void handleInstall(entry)}
                  />
                );
              })}
            </motion.div>
          </CatalogPreviewGate>
        )}
      </div>

      <AnimatePresence>
        {replacePrompt && (() => {

          const cur = replacePrompt.current;
          const currentScreenshot =
            cur.kind === 'library'
              ? (libraryMinimaps.find(l => l.id === cur.id)?.previewUrl ?? '')
              : (items.find(r => r.id === cur.id)?.componentScreenshots?.minimap ?? '');
          return (
            <ReplaceConfirmModal
              current={replacePrompt.current}
              currentScreenshot={currentScreenshot}
              incoming={replacePrompt.incoming}
              onKeep={() => {
                setReplacePrompt(null);
                setToast({ tone: 'info', message: t('minimaps.keptCurrentToast') });
              }}
              onReplace={() => {
                const incoming = replacePrompt.incoming;
                setReplacePrompt(null);
                void dispatchInstall(incoming);
              }}
            />
          );
        })()}
      </AnimatePresence>

      <ConfirmModal
        open={keepRingsPrompt !== null}
        title={t('minimaps.keepRingsTitle', 'Сохранить кольца?')}
        message={keepRingsPrompt
          ? `${keepRingsPrompt.mapName}\n\n${t('minimaps.keepRingsBody',
              'Круги дальности ({{radii}} м) слетели при смене миникарты. Нанести их заново на новую миникарту?',
              { radii: keepRingsPrompt.radii.join(' / ') })}`
          : ''}
        confirmLabel={keepingRings
          ? t('minimaps.keepRingsBusy', 'Наношу...')
          : t('minimaps.keepRingsConfirm', 'Сохранить')}
        cancelLabel={t('minimaps.keepRingsCancel', 'Без колец')}
        hideConfirmArrow
        imageUrl={keepRingsPrompt ? (keepRingsPrompt.thumb || null) : undefined}
        onConfirm={() => void confirmKeepRings()}
        onCancel={() => { if (!keepingRings) setKeepRingsPrompt(null); }}
      />

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 3500}
      />
    </div>
  );
}

function MinimapCard({
  title, installs, favorite, onToggleFavorite, screenshot, installed, installing, index, onInstall,
}: {
  title: string;
  installs: number;
  favorite: boolean;
  onToggleFavorite?: () => void;
  screenshot: string;
  installed: boolean;
  installing: boolean;
  index?: number;
  onInstall: () => void;
}) {
  const { t } = useTranslation();
  const [lightbox, setLightbox] = useState(false);
  const [imgBroken, setImgBroken] = useState(false);
  const showImage = !!screenshot && !imgBroken;
  void index;
  const eagerImage = true;

  return (
    <>
      <motion.div
        variants={{
          hidden: { opacity: 0, y: 16, scale: 0.98 },
          show:   { opacity: 1, y: 0,  scale: 1 },
        }}
        transition={{ type: 'spring', stiffness: 280, damping: 26, mass: 0.7 }}
        whileHover={{ y: -3, transition: { duration: 0.22, ease: EASE_DEPTH } }}
        className={
          'group relative rounded-3xl overflow-hidden border flex flex-col will-change-transform ' +
          'bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid border-white/[0.08] ' +
          'transition-[border-color,box-shadow] duration-300 ease-smooth ' +
          'hover:border-[color-mix(in_srgb,var(--accent)_45%,transparent)]'
        }
      >
        {onToggleFavorite && (
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onToggleFavorite(); }}
            title={favorite
              ? t('catalog.favoriteRemove', 'Убрать из избранного')
              : t('catalog.favoriteAdd', 'В избранное')}
            style={{ outline: 'none' }}
            className="absolute top-2.5 left-2.5 z-30 w-8 h-8 rounded-lg flex items-center justify-center
                       bg-black/45 backdrop-blur-sm border border-white/10
                       text-white/70 hover:text-white hover:bg-black/60 transition-colors"
          >
            <Star size={14} strokeWidth={2} fill={favorite ? 'currentColor' : 'none'}
                  className={favorite ? 'text-amber-300' : ''} />
          </button>
        )}
        <span
          aria-hidden
          className="absolute top-0 inset-x-0 h-px pointer-events-none z-20
                     bg-gradient-to-r from-transparent via-white/40 to-transparent"
        />
        <span
          aria-hidden
          className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl
                     opacity-60 group-hover:opacity-100 transition-opacity duration-500"
          style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
        />
        <div className="relative aspect-[16/9] overflow-hidden">
          <CarbonSurface weaveOpacity={0.45} glowIntensity={0.5} vignetteIntensity={0.55} />
          {showImage ? (
            <>
              {}
              <LazyImage
                src={screenshot}
                eager={eagerImage}
                alt=""
                aria-hidden
                className="absolute inset-0 w-full h-full object-cover"
                style={{
                  filter: 'blur(20px) saturate(115%) brightness(0.6)',
                  transform: 'scale(1.18)',
                }}
              />
              <LazyImage
                src={screenshot}
                eager={eagerImage}
                alt=""
                onError={() => setImgBroken(true)}
                className="absolute inset-0 w-full h-full object-contain transition-transform duration-700 ease-smooth group-hover:scale-[1.04]"
              />
            </>
          ) : (
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-1 text-text-muted/70">
              <ImageOff size={22} strokeWidth={1.5} />
              <span className="text-[10px] uppercase tracking-wider">{t('catalog.noScreenshot')}</span>
            </div>
          )}
          {showImage && (
            <button
              type="button"
              onClick={() => setLightbox(true)}
              className="absolute top-2 right-2 inline-flex items-center gap-1.5 h-8 px-2.5
                         rounded-full bg-black/55 backdrop-blur-md text-white
                         text-[10.5px] font-bold uppercase tracking-wider
                         opacity-0 group-hover:opacity-100 hover:bg-black/80
                         transition-opacity duration-150 z-10"
              title={t('catalog.viewFull')}
            >
              <Eye size={12} strokeWidth={2.4} />
              {t('catalog.view')}
            </button>
          )}
        </div>

        <div className="relative flex items-center gap-3 p-3">
          <div className="min-w-0 flex-1">
            <span className="text-sm font-bold text-text-primary truncate uppercase tracking-wide block">{title}</span>
            {installs > 0 && (
              <span className="text-[10px] text-text-muted tabular-nums block mt-0.5">
                {t('catalog.installsCount', {
                  count: installs,
                  defaultValue_one: '{{count}} установка',
                  defaultValue_few: '{{count}} установки',
                  defaultValue_many: '{{count}} установок',
                  defaultValue: '{{count}} установок',
                })}
              </span>
            )}
          </div>
          <motion.button
            type="button"
            onClick={onInstall}
            disabled={installing || installed}
            whileHover={(installing || installed) ? undefined : { scale: 1.02, transition: { duration: 0.18, ease: EASE_DEPTH } }}
            whileTap={(installing || installed) ? undefined : { scale: 0.98, transition: { duration: 0.12, ease: EASE_DEPTH } }}
            style={{ outline: 'none' }}
            className={
              'inline-flex items-center justify-center gap-2 h-10 px-5 rounded-xl ' +
              'text-[12px] font-bold uppercase tracking-[0.08em] transition-colors ' +
              'disabled:cursor-not-allowed ' +
              (installed
                ? 'bg-status-success/15 text-status-success border border-transparent'
                : 'disabled:opacity-55 bg-bg-elevated/55 text-text-primary ' +
                  'border border-white/[0.08] ' +
                  'hover:bg-bg-elevated/75 hover:border-white/[0.18]')
            }
            title={installed ? t('catalog.alreadyInstalledTitle') : t('minimaps.installTitle')}
          >
            {installing
              ? <Loader2 size={13} className="animate-spin" />
              : installed
                ? <Check size={13} strokeWidth={2.4} />
                : <Download size={13} strokeWidth={2.4} />}
            <span>
              {installing
                ? t('catalog.installing')
                : installed
                  ? t('catalog.badgeInstalled')
                  : t('catalog.install')}
            </span>
          </motion.button>
        </div>
      </motion.div>

      <AnimatePresence>
        {lightbox && (
          <MinimapLightbox
            url={screenshot}
            title={t('minimaps.lightboxTitle', { title })}
            onClose={() => setLightbox(false)}
          />
        )}
      </AnimatePresence>
    </>
  );
}

function ReplaceConfirmModal({
  current, currentScreenshot, incoming, onKeep, onReplace,
}: {
  current:           CurrentMinimapInfo;
  currentScreenshot: string;
  incoming:          { source: 'redux' | 'library'; id: string; name: string; screenshot: string };
  onKeep:    () => void;
  onReplace: () => void;
}) {
  const { t } = useTranslation();
  const [picked, setPicked] = useState<'current' | 'incoming'>('incoming');

  useEffect(() => { setPicked('incoming'); }, []);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onKeep(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onKeep]);

  const handleContinue = () => {
    if (picked === 'current') onKeep();
    else                       onReplace();
  };

  const currentKindLabel  = current.kind === 'library'    ? t('catalog.kindCustom') : t('catalog.kindRedux');
  const incomingKindLabel = incoming.source === 'library' ? t('catalog.kindCustom') : t('catalog.kindRedux');

  return (
    <motion.div
      key="minimap-replace-modal"
      className="fixed inset-0 z-[100] flex items-center justify-center p-6"
      initial={{ opacity: 0, pointerEvents: 'none' as const }}
      animate={{ opacity: 1, pointerEvents: 'auto' as const }}
      exit   ={{ opacity: 0, pointerEvents: 'none' as const }}
      transition={{ duration: 0.24, ease: EASE_DEPTH }}
      onClick={onKeep}
      style={{
        background:
          'radial-gradient(ellipse at center, rgba(20,20,28,0.78) 0%, rgba(0,0,0,0.86) 75%)',
        backdropFilter: 'blur(14px) saturate(140%)',
        WebkitBackdropFilter: 'blur(14px) saturate(140%)',
      }}
    >
      <motion.div
        initial={{ opacity: 0, scale: 0.94, y: 14 }}
        animate={{ opacity: 1, scale: 1,    y: 0 }}
        exit   ={{ opacity: 0, scale: 0.96, y: 8 }}
        transition={{ duration: 0.34, ease: EASE_DEPTH }}
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-[880px]"
        style={{
          filter:
            'drop-shadow(0 26px 60px rgba(255,255,255,0.10)) drop-shadow(0 6px 18px rgba(0,0,0,0.62))',
        }}
      >
        <div
          className="relative overflow-hidden rounded-3xl p-8 flex flex-col gap-7
                     border border-white/[0.08]"
          style={{
            background: 'color-mix(in srgb, var(--bg-elevated) 55%, transparent)',
            boxShadow:
              'inset 0 1px 0 rgba(255,255,255,0.10), ' +
              '0 24px 56px rgba(0,0,0,0.55)',
            backdropFilter: 'blur(22px) saturate(140%)',
            WebkitBackdropFilter: 'blur(22px) saturate(140%)',
          }}
        >
          {}
          <span
            aria-hidden
            className="absolute top-0 inset-x-0 h-px pointer-events-none
                       bg-gradient-to-r from-transparent via-white/40 to-transparent"
          />
          {}
          <span
            aria-hidden
            className="pointer-events-none absolute -top-24 -left-24 w-72 h-72 rounded-full opacity-30 blur-3xl"
            style={{ background: 'radial-gradient(circle, rgba(255,255,255,0.5), transparent 70%)' }}
          />
          <span
            aria-hidden
            className="pointer-events-none absolute -bottom-32 -right-20 w-80 h-80 rounded-full opacity-20 blur-3xl"
            style={{ background: 'radial-gradient(circle, rgba(255,255,255,0.4), transparent 70%)' }}
          />

          <button
            type="button"
            onClick={onKeep}
            aria-label={t('common.close')}
            className="absolute top-4 right-4 w-9 h-9 rounded-xl flex items-center justify-center
                       bg-white/[0.04] border border-white/[0.06] text-text-secondary
                       hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                       transition-colors"
          >
            <X size={14} />
          </button>

          <header className="flex flex-col gap-2 text-center max-w-2xl mx-auto">
            <span className="text-[11px] uppercase tracking-[0.28em] text-text-muted font-semibold">
              {t('minimaps.replaceEyebrow')}
            </span>
            <h2 className="font-display text-2xl font-bold uppercase tracking-wide text-text-primary leading-tight">
              {t('minimaps.replaceTitle')}
            </h2>
            <p className="text-sm text-text-secondary leading-relaxed">
              {t('minimaps.replaceHint')}
            </p>
          </header>

          <div className="grid items-stretch gap-4 grid-cols-1 md:grid-cols-[1fr_auto_1fr]">
            <MinimapPreviewTile
              variant="current"
              selected={picked === 'current'}
              label={t('catalog.tileCurrent')}
              badgeIcon={<CheckCircle2 size={11} />}
              badgeText={t('catalog.badgeInstalled')}
              name={current.name || current.id}
              kindLabel={currentKindLabel}
              screenshot={currentScreenshot}
              onPick={() => setPicked('current')}
            />
            <ArrowDivider />
            <MinimapPreviewTile
              variant="incoming"
              selected={picked === 'incoming'}
              label={t('catalog.tileNew')}
              badgeIcon={<Sparkles size={11} />}
              badgeText={t('catalog.badgeCandidate')}
              name={incoming.name || incoming.id}
              kindLabel={incomingKindLabel}
              screenshot={incoming.screenshot}
              onPick={() => setPicked('incoming')}
            />
          </div>

          <div className="flex flex-col gap-2.5 items-center">
            <motion.button
              type="button"
              onClick={handleContinue}
              whileHover={{ y: -1 }}
              whileTap={{ scale: 0.98 }}
              transition={{ duration: 0.15, ease: EASE_DEPTH }}
              className="h-12 w-full max-w-[420px] rounded-2xl overflow-hidden
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/80 hover:border-white/[0.20]
                         text-sm font-bold uppercase tracking-[0.16em]
                         transition-colors flex items-center justify-center gap-2"
              style={{ outline: 'none' }}
            >
              {picked === 'current' ? t('catalog.keepCurrent') : t('catalog.installNew')}
            </motion.button>
            <button
              type="button"
              onClick={onKeep}
              className="text-[11px] uppercase tracking-[0.22em] text-text-muted
                         hover:text-text-primary transition-colors"
            >
              {t('common.cancel')}
            </button>
          </div>
        </div>
      </motion.div>
    </motion.div>
  );
}

function MinimapPreviewTile({
  variant, selected, label, badgeIcon, badgeText, name, kindLabel, screenshot, onPick,
}: {
  variant:    'current' | 'incoming';
  selected:   boolean;
  label:      string;
  badgeIcon:  React.ReactNode;
  badgeText:  string;
  name:       string;
  kindLabel:  string;
  screenshot: string;
  onPick:     () => void;
}) {
  const isIncoming = variant === 'incoming';
  return (
    <button
      type="button"
      onClick={onPick}
      className="group flex flex-col gap-2.5 text-left p-0 rounded-2xl transition"
      style={{ outline: 'none' }}
    >
      <div className="flex items-center justify-between px-1">
        <span className="text-[10px] uppercase tracking-[0.24em] text-text-muted font-semibold">
          {label}
        </span>
        <span
          className={
            'inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] uppercase tracking-wider font-semibold ' +
            (isIncoming
              ? 'bg-white/[0.10] text-text-primary border border-white/[0.18]'
              : 'bg-status-success/20 text-status-success')
          }
        >
          {badgeIcon}
          {badgeText}
        </span>
      </div>

      {}
      <div
        className={
          'relative aspect-square rounded-2xl overflow-hidden transition ' +
          (selected
            ? 'ring-2 ring-white ring-offset-0'
            : 'ring-1 ring-white/[0.08] group-hover:ring-white/[0.25]')
        }
        style={{
          background: 'linear-gradient(155deg, rgba(255,255,255,0.03), rgba(255,255,255,0))',
          boxShadow: selected
            ? 'inset 0 0 60px rgba(255,255,255,0.10), 0 10px 28px rgba(255,255,255,0.18)'
            : 'inset 0 0 50px rgba(0,0,0,0.4)',
        }}
      >
        <div className="absolute inset-0 bg-glass-strong" />
        {screenshot ? (
          <>
            {}
            <div
              aria-hidden
              className="absolute inset-0"
              style={{
                background: `url(${screenshot}) center / cover no-repeat`,
                filter: 'blur(28px) brightness(0.55) saturate(1.2)',
                transform: 'scale(1.18)',
              }}
            />
            <div className="absolute inset-0 flex items-center justify-center p-4">
              <img
                src={screenshot}
                alt=""
                draggable={false}
                className="max-w-full max-h-full w-auto h-auto object-contain rounded-md"
              />
            </div>
          </>
        ) : (

          <div
            aria-hidden
            className="absolute inset-0 flex items-center justify-center text-text-muted"
            style={{
              background: 'linear-gradient(135deg, rgba(255,255,255,0.08), rgba(20,20,28,1))',
            }}
          >
            <MapIcon size={36} className="opacity-40" />
          </div>
        )}
        {}
        {selected && (
          <span
            className="absolute top-3 right-3 w-7 h-7 rounded-full flex items-center justify-center
                       bg-white text-black border border-white
                       shadow-[0_6px_18px_rgba(255,255,255,0.45),inset_0_1px_0_rgba(255,255,255,0.85)]"
          >
            <Check size={14} strokeWidth={3} />
          </span>
        )}
      </div>

      <div className="flex flex-col gap-0.5 pt-1">
        <h3
          className={
            'text-base font-bold truncate uppercase tracking-wide text-center transition-colors ' +
            (selected ? 'text-text-primary' : 'text-text-secondary group-hover:text-text-primary')
          }
        >
          {name}
        </h3>
        <span className="text-[10px] uppercase tracking-[0.18em] text-text-muted text-center">
          {kindLabel}
        </span>
      </div>
    </button>
  );
}

function ArrowDivider() {
  return (
    <div className="hidden md:flex items-center justify-center px-2">
      <div
        className="w-10 h-10 rounded-full flex items-center justify-center
                   bg-white/[0.04] border border-white/[0.10] text-text-secondary"
        style={{ boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.10)' }}
      >
        <ArrowRight size={16} />
      </div>
    </div>
  );
}

function MinimapLightbox({
  url, title, onClose,
}: {
  url: string;
  title: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const [origin, setOrigin] = useState<{ x: number; y: number }>({ x: 50, y: 50 });
  const [zoomed, setZoomed] = useState(false);
  const onImageClick = (e: React.MouseEvent<HTMLImageElement>) => {
    if (zoomed) { setZoomed(false); return; }
    const rect = e.currentTarget.getBoundingClientRect();
    setOrigin({
      x: ((e.clientX - rect.left) / rect.width) * 100,
      y: ((e.clientY - rect.top) / rect.height) * 100,
    });
    setZoomed(true);
  };

  return (
    <motion.div
      key="minimap-lightbox"
      className="fixed inset-0 z-[100] bg-black/75 backdrop-blur-glass-ultra
                 backdrop-saturate-liquid flex items-center justify-center p-6"
      initial={{ opacity: 0, pointerEvents: 'none' as const }}
      animate={{ opacity: 1, pointerEvents: 'auto' as const }}
      exit={{ opacity: 0, pointerEvents: 'none' as const }}
      transition={{ duration: 0.24, ease: [0.22, 1, 0.36, 1] }}
      onClick={onClose}
    >
      <motion.div
        className="relative w-[min(90vw,1280px)] aspect-[16/9] max-h-[85vh] rounded-3xl overflow-hidden
                   bg-glass-ultra"
        initial={{ opacity: 0, scale: 0.92, y: 24, rotateX: -6 }}
        animate={{ opacity: 1, scale: 1,    y: 0,  rotateX: 0 }}
        exit   ={{ opacity: 0, scale: 0.96, y: 12, rotateX: 4 }}
        transition={{
          opacity:  { duration: 0.22 },
          rotateX:  { duration: 0.42, ease: [0.22, 1, 0.36, 1] },
          default:  { type: 'spring', stiffness: 300, damping: 28, mass: 0.8 },
        }}
        style={{ transformPerspective: 1200 }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="absolute top-0 inset-x-0 z-20 px-4 py-3 flex items-center gap-3
                        bg-gradient-to-b from-black/55 to-transparent pointer-events-none">
          <h2 className="text-sm font-semibold text-white truncate flex-1
                         drop-shadow-[0_2px_4px_rgba(0,0,0,0.8)]">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label={t('catalog.closeEsc')}
            className="pointer-events-auto w-9 h-9 rounded-lg flex items-center justify-center
                       text-white/85 bg-black/50 hover:bg-black/80 hover:text-white
                       transition-colors"
          >
            <X size={16} />
          </button>
        </div>
        <div className="absolute inset-0">
          <CarbonSurface weaveOpacity={0.7} glowIntensity={0.5} vignetteIntensity={0.6} />
          {}
          <img
            src={url}
            alt=""
            aria-hidden
            className="absolute inset-0 w-full h-full object-cover"
            style={{
              filter: 'blur(32px) saturate(120%) brightness(0.55)',
              transform: 'scale(1.15)',
            }}
          />
          <motion.img
            key={url}
            src={url}
            alt=""
            onClick={onImageClick}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ opacity: { duration: 0.32, ease: [0.22, 1, 0.36, 1] } }}
            draggable={false}
            className="absolute inset-0 w-full h-full object-contain will-change-transform select-none"
            style={{
              cursor: zoomed ? 'zoom-out' : 'zoom-in',
              transform: zoomed ? 'scale(2.4)' : 'scale(1)',
              transformOrigin: `${origin.x}% ${origin.y}%`,
              transition: 'transform 0.45s cubic-bezier(0.22, 1, 0.36, 1)',
            }}
          />
        </div>
      </motion.div>
    </motion.div>
  );
}
