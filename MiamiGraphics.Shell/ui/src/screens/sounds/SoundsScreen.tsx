import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Volume2, Eye, ImageOff, Loader2, Download, X, Check, Sparkles, ArrowRight,
  CheckCircle2, ChevronLeft, ChevronRight, Trash2, Play, Star,
} from 'lucide-react';
import { CarbonSurface, EASE_DEPTH } from '@/design';
import { ScreenHero } from '@/screens/ScreenHero';
import { useInstallCounts, useDuplicateNames, installCountFor } from '@/hooks/useInstallCounts';
import { useItemFavorites } from '@/hooks/useItemFavorites';
import { CatalogSortSelect, CatalogFavToggle, type CatalogSort } from '@/screens/catalog/CatalogControls';
import { bridge } from '@/bridge';
import type { LibraryComponent, CurrentSoundPackInfo } from '@/bridge/types';
import { ensureBackupOrGate } from '@/store/installGate';
import { Toast } from '@/components/Toast';
import { VideoModal } from '@/components/VideoModal';
import { CatalogPreviewGate } from '@/components/CatalogPreviewGate';
import { shuffled } from '@/utils/shuffle';
import { getCachedLibrary, setCachedLibrary } from '@/store/libraryCache';

let savedScrollTop = 0;
let cachedSoundOrder: { signature: string; entries: unknown[] } | null = null;

export function SoundsScreen() {
  const { t } = useTranslation();

  const [libraryItems, setLibraryItems] = useState<LibraryComponent[]>(
    getCachedLibrary('sounds') ?? [],
  );
  const [loading, setLoading]           = useState(false);
  const [current, setCurrent]           = useState<CurrentSoundPackInfo | null>(null);

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

  const refreshCurrent = async () => {
    try { setCurrent(await bridge.getCurrentSoundPackInfo()); }
    catch (e) { console.warn('[sounds.current] fail:', e); }
  };

  useEffect(() => {
    let alive = true;
    setLoading(true);
    bridge.libraryList('sounds')
      .then(rows => {
        if (!alive) return;
        const next = rows ?? [];
        setCachedLibrary('sounds', next);
        setLibraryItems(next);
      })
      .catch(e => console.warn('[sounds.libraryList] fail:', e))
      .finally(() => { if (alive) setLoading(false); });
    void refreshCurrent();
    return () => { alive = false; };
  }, []);

  const [search, setSearch] = useState('');
  const installCounts = useInstallCounts('sounds_install');
  const [sort, setSort] = useState<CatalogSort>('default');
  const [onlyFav, setOnlyFav] = useState(false);
  const { favorites, toggle: toggleFav, canFavorite } = useItemFavorites('sounds');
  const [installingId, setInstallingId] = useState<string | null>(null);
  const [uninstalling, setUninstalling] = useState(false);
  const [toast, setToast] = useState<{ tone: 'success' | 'error' | 'info'; message: string } | null>(null);
  const [replacePrompt, setReplacePrompt] = useState<{
    current:  CurrentSoundPackInfo;
    incoming: { id: string; name: string; screenshot: string };
  } | null>(null);

  type SoundEntry = { library: LibraryComponent; photos: string[] };

  const items = useMemo<SoundEntry[]>(() => {
    const all = libraryItems.map(l => {
      const cover  = l.previewUrl ?? '';
      const photos = [cover, ...(l.galleryUrls ?? [])].filter((p, i, a) =>
        !!p && a.indexOf(p) === i);
      return { library: l, photos };
    });
    const signature = all.map(e => `l:${e.library.id}`).sort().join('|');
    if (cachedSoundOrder?.signature === signature) {
      return cachedSoundOrder.entries as SoundEntry[];
    }
    const withImage = all.filter(e => e.photos.length > 0);
    const noImage   = all.filter(e => e.photos.length === 0);
    const entries   = [...shuffled(withImage), ...noImage];
    cachedSoundOrder = { signature, entries };
    return entries;
  }, [libraryItems]);

  const soundNames = useMemo(() => items.map(e => e.library.name), [items]);
  const dupNames = useDuplicateNames(soundNames);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    let list = !q ? items : items.filter(m =>
      m.library.name.toLowerCase().includes(q)
      || m.library.author.toLowerCase().includes(q)
      || m.library.id.toLowerCase().includes(q));
    if (onlyFav) list = list.filter(m => favorites.has(m.library.id));
    if (sort === 'installs') {
      list = [...list].sort((a, b) =>
        installCountFor(installCounts, dupNames, b.library) - installCountFor(installCounts, dupNames, a.library));
    } else if (sort === 'name') {
      list = [...list].sort((a, b) => a.library.name.localeCompare(b.library.name));
    }
    return list;
  }, [items, search, onlyFav, favorites, sort, installCounts, dupNames]);

  const handleInstall = async (entry: SoundEntry) => {
    if (installingId || uninstalling || replacePrompt) return;
    const cover = entry.photos[0] ?? '';
    if (!entry.library.r2Url) {
      setToast({ tone: 'error', message: t('sounds.previewOnly') });
      return;
    }
    const incoming = { id: entry.library.id, name: entry.library.name, screenshot: cover };
    if (current && current.id !== incoming.id) {
      setReplacePrompt({ current, incoming });
      return;
    }
    if (current && current.id === incoming.id) {
      setToast({ tone: 'info', message: t('sounds.alreadyInstalled') });
      return;
    }
    void dispatchInstall(incoming);
  };

  const dispatchInstall = async (incoming: { id: string; name: string; screenshot?: string }) => {

    if (!ensureBackupOrGate()) return;
    setInstallingId(incoming.id);
    try {
      const r = await bridge.soundPackInstall(incoming.id, incoming.name);
      if (r.success) {
        setToast({ tone: 'success', message: r.errorMessage ?? t('sounds.installedToast', { name: incoming.name }) });
        void bridge.activityLog('sounds_install', `звуки «${incoming.name}»`, incoming.id);
        await refreshCurrent();
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setInstallingId(null);
    }
  };

  const handleUninstall = async () => {
    if (uninstalling || installingId || !current) return;
    setUninstalling(true);
    try {
      const r = await bridge.soundPackUninstall();
      if (r.success) {
        setToast({ tone: 'success', message: r.errorMessage ?? t('sounds.restoredStock') });
        await refreshCurrent();
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setUninstalling(false);
    }
  };

  const previewUrls = useMemo(
    () => filtered.slice(0, 16).map(e => e.photos[0] ?? '').filter(Boolean),
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

  return (
    <div ref={scrollRef} onScroll={onGridScroll} className="h-full overflow-y-auto">
      <div className="max-w-7xl 2xl:max-w-[1700px] mx-auto px-5 pt-3 pb-6 flex flex-col gap-3">
        <ScreenHero
          title={t('sounds.heroTitle')}
          subtitle={t('sounds.heroSubtitle')}
          search={{
            value: search,
            onChange: setSearch,
            placeholder: t('sounds.searchPlaceholder'),
            clearLabel: t('armor.searchClear'),
          }}
          trailing={
            <>
              {current && (
                <button
                  type="button"
                  onClick={handleUninstall}
                  disabled={uninstalling}
                  className="inline-flex items-center gap-2 h-10 px-4 rounded-xl
                             bg-white/[0.04] border border-white/[0.10] text-text-secondary
                             hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                             disabled:opacity-50 disabled:cursor-not-allowed
                             text-[11.5px] font-bold uppercase tracking-[0.14em]
                             transition-colors duration-200 ease-depth"
                  title={t('sounds.restoreStockTitle')}
                >
                  {uninstalling ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                  <span>{t('sounds.restoreStock', 'Восстановить стандартные')}</span>
                </button>
              )}
              <CatalogSortSelect value={sort} onChange={setSort} />
              {canFavorite && (
                <CatalogFavToggle
                  active={onlyFav}
                  onClick={() => setOnlyFav(v => !v)}
                  label={t('redux.favoritesOnly', 'Только избранное')}
                />
              )}
              <span className="inline-flex items-center gap-1.5 px-3 h-9 rounded-xl
                               bg-white/[0.04] border border-white/[0.06]
                               text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted shrink-0">
                <span className="tabular-nums text-text-primary">{filtered.length}</span>
                <span>{t('redux.count', { count: filtered.length }).replace(/^\d+\s*/, '')}</span>
              </span>
            </>
          }
        />

        {loading && libraryItems.length === 0 ? (
          <div className="flex items-center gap-2 text-sm text-text-muted py-12 justify-center">
            <Loader2 size={14} className="animate-spin text-accent" />
            <span>{t('catalog.loading', 'Загружаю каталог...')}</span>
          </div>
        ) : filtered.length === 0 ? (
          <div className="flex flex-col items-center justify-center gap-2 py-16 text-text-muted">
            <ImageOff size={28} />
            <p className="text-sm">
              {items.length === 0
                ? t('sounds.emptyCatalog')
                : t('catalog.noResults')}
            </p>
          </div>
        ) : (
          <CatalogPreviewGate urls={previewUrls} ready={!loading || libraryItems.length > 0}>
          <motion.div
            initial="hidden"
            animate="show"
            variants={{
              hidden: { opacity: 0 },
              show:   { opacity: 1, transition: { staggerChildren: 0.04, delayChildren: 0.06 } },
            }}
            className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4"
          >
            {visible.map(entry => {
              const installed  = current?.id === entry.library.id;
              const installing = installingId === entry.library.id;
              return (
                <SoundsCard
                  key={entry.library.id}
                  title={entry.library.name}
                  installs={installCountFor(installCounts, dupNames, entry.library)}
                  favorite={favorites.has(entry.library.id)}
                  onToggleFavorite={canFavorite ? () => toggleFav(entry.library.id) : undefined}
                  badge={t('common.unitMb', '{{value}} MB', {
                    value: (entry.library.sizeBytes / 1024 / 1024).toFixed(0),
                  })}
                  photos={entry.photos}
                  videoUrl={entry.library.previewVideoUrl ?? ''}
                  installed={installed}
                  installing={installing}
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
            libraryItems.find(l => l.id === cur.id)?.previewUrl ?? '';
          return (
            <ReplaceConfirmModal
              current={replacePrompt.current}
              currentScreenshot={currentScreenshot}
              incoming={replacePrompt.incoming}
              onKeep={() => {
                setReplacePrompt(null);
                setToast({ tone: 'info', message: t('sounds.keptCurrentToast') });
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

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 4500}
      />
    </div>
  );
}

function SoundsCard({
  title, badge, installs, favorite, onToggleFavorite, photos, videoUrl, installed, installing, onInstall,
}: {
  title: string;
  badge: string;
  installs: number;
  favorite: boolean;
  onToggleFavorite?: () => void;
  photos: string[];
  videoUrl: string;
  installed: boolean;
  installing: boolean;
  onInstall: () => void;
}) {
  const { t } = useTranslation();
  const [lightbox, setLightbox]   = useState(false);
  const [videoOpen, setVideoOpen] = useState(false);
  const [brokenIdx, setBrokenIdx] = useState<Set<number>>(() => new Set());
  const [active, setActive]       = useState(0);
  useEffect(() => { setActive(0); }, [photos.join('|')]);

  const hasVideo = !!videoUrl;

  const visibleCount = photos.length - brokenIdx.size;
  const safeActive   = Math.min(active, Math.max(0, photos.length - 1));
  const currentSrc   = photos[safeActive];
  const showImage    = !!currentSrc && !brokenIdx.has(safeActive);
  const hasMultiple  = photos.length > 1 && visibleCount > 1;

  const stepBy = (delta: 1 | -1) => {
    if (photos.length === 0) return;

    setActive((prev) => {
      let next = prev;
      for (let i = 0; i < photos.length; i++) {
        next = (next + delta + photos.length) % photos.length;
        if (!brokenIdx.has(next)) break;
      }
      return next;
    });
  };

  return (
    <>
      <motion.div
        variants={{
          hidden: { opacity: 0, y: 16, scale: 0.98 },
          show:   { opacity: 1, y: 0,  scale: 1 },
        }}
        transition={{ type: 'spring', stiffness: 280, damping: 26, mass: 0.7 }}
        whileHover={{ y: -3, transition: { type: 'tween', duration: 0.2, ease: [0.22, 1, 0.36, 1] } }}
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
        {}
        <div
          role={hasVideo ? 'button' : undefined}
          tabIndex={hasVideo ? 0 : undefined}
          onClick={hasVideo ? () => setVideoOpen(true) : undefined}
          onKeyDown={hasVideo
            ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setVideoOpen(true); } }
            : undefined}
          className={
            'relative aspect-[16/9] overflow-hidden focus-visible:outline-none ' +
            (hasVideo ? 'cursor-pointer' : '')
          }
        >
          <CarbonSurface weaveOpacity={0.45} glowIntensity={0.5} vignetteIntensity={0.55} />
          {showImage ? (
            <>
              <img
                src={currentSrc}
                alt=""
                aria-hidden
                className="absolute inset-0 w-full h-full object-cover"
                style={{
                  filter: 'blur(20px) saturate(115%) brightness(0.6)',
                  transform: 'scale(1.18)',
                }}
              />
              <AnimatePresence initial={false} mode="popLayout">
                <motion.img
                  key={safeActive + ':' + currentSrc}
                  src={currentSrc}
                  alt=""
                  onError={() => setBrokenIdx(prev => {
                    const next = new Set(prev);
                    next.add(safeActive);
                    return next;
                  })}
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit   ={{ opacity: 0 }}
                  transition={{ duration: 0.13, ease: EASE_DEPTH }}
                  className="absolute inset-0 w-full h-full object-cover will-change-transform
                             transition-[filter,transform] duration-[400ms] ease-out
                             group-hover:scale-[1.04]"
                  style={{

                    filter: hasVideo ? undefined : 'none',
                  }}
                />
              </AnimatePresence>

              {}
              {hasVideo && (
                <div
                  aria-hidden
                  className="pointer-events-none absolute inset-0 z-[5]
                             opacity-0 group-hover:opacity-100
                             transition-opacity duration-300 ease-out"
                  style={{
                    background:
                      'radial-gradient(ellipse at center, rgba(8,8,14,0.40) 0%, rgba(8,8,14,0.65) 75%)',
                    backdropFilter: 'blur(8px) saturate(120%)',
                    WebkitBackdropFilter: 'blur(8px) saturate(120%)',
                  }}
                />
              )}
            </>
          ) : (
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-1 text-text-muted/70">
              <Volume2 size={26} strokeWidth={1.5} />
              <span className="text-[10px] uppercase tracking-wider">{t('catalog.noPhoto', 'нет фото')}</span>
            </div>
          )}

          {}
          {hasVideo && (
            <motion.div
              initial={false}
              className="pointer-events-none absolute inset-0 z-10 flex items-center justify-center
                         opacity-0 group-hover:opacity-100
                         transition-opacity duration-300 ease-out"
            >
              <div
                className="inline-flex items-center gap-2.5 px-4 h-11 rounded-full
                           bg-white/[0.10] border border-white/[0.30] text-white
                           backdrop-blur-md
                           shadow-[0_8px_28px_-6px_rgba(0,0,0,0.55)]
                           transition-transform duration-300 ease-out
                           group-hover:scale-[1.04]"
              >
                <span
                  className="inline-flex items-center justify-center w-7 h-7 rounded-full
                             bg-white text-black"
                >
                  <Play size={13} strokeWidth={3} className="ml-0.5" />
                </span>
                <span className="text-[11px] font-bold uppercase tracking-[0.18em]">
                  {t('catalog.videoOverlay', 'Видео обзор · клик')}
                </span>
              </div>
            </motion.div>
          )}

          {}
          <span className="absolute top-2 right-2 z-20 inline-flex items-center px-2 py-1 rounded-md
                           bg-black/55 backdrop-blur-md text-white/85
                           text-[10px] uppercase tracking-wider font-bold">
            {badge}
          </span>
          {}
          {showImage && !hasVideo && (
            <button
              type="button"
              onClick={(e) => { e.stopPropagation(); setLightbox(true); }}
              className="absolute bottom-2 right-2 z-20 inline-flex items-center gap-1.5 h-8 px-2.5
                         rounded-full bg-black/55 backdrop-blur-md text-white
                         text-[10.5px] font-bold uppercase tracking-wider
                         opacity-0 group-hover:opacity-100 hover:bg-black/80
                         transition-opacity duration-150"
              title={t('catalog.viewFull')}
            >
              <Eye size={12} strokeWidth={2.4} />
              {t('catalog.view', 'Просмотр')}
            </button>
          )}

          {hasMultiple && (
            <>
              <button
                type="button"
                onClick={(e) => { e.stopPropagation(); stepBy(-1); }}
                aria-label={t('catalog.prevPhoto')}
                className="absolute left-2 top-0 bottom-0 my-auto z-30 w-9 h-9 rounded-full
                           flex items-center justify-center text-white
                           bg-black/55 backdrop-blur-md hover:bg-black/80
                           border border-white/[0.12]
                           opacity-0 group-hover:opacity-100
                           transition-opacity duration-150"
              >
                <ChevronLeft size={16} strokeWidth={2.4} />
              </button>
              <button
                type="button"
                onClick={(e) => { e.stopPropagation(); stepBy(1); }}
                aria-label={t('catalog.nextPhoto')}
                className="absolute right-2 top-0 bottom-0 my-auto z-30 w-9 h-9 rounded-full
                           flex items-center justify-center text-white
                           bg-black/55 backdrop-blur-md hover:bg-black/80
                           border border-white/[0.12]
                           opacity-0 group-hover:opacity-100
                           transition-opacity duration-150"
              >
                <ChevronRight size={16} strokeWidth={2.4} />
              </button>
              <div className="pointer-events-none absolute bottom-2 left-1/2 -translate-x-1/2 z-10
                              flex items-center gap-1.5">
                {photos.map((_, i) => (
                  <span
                    key={i}
                    className={
                      'block h-1.5 rounded-full transition-all duration-200 ' +
                      (i === safeActive ? 'w-4 bg-white' : 'w-1.5 bg-white/40')
                    }
                  />
                ))}
              </div>
            </>
          )}
        </div>

        <div className="relative flex items-center gap-3 p-3">
          <div className="min-w-0 flex-1">
            <span className="text-sm font-bold text-text-primary truncate uppercase tracking-wide block">{title}</span>
            {installs > 0 && (
              <span className="text-[10px] text-text-muted tabular-nums block mt-0.5">
                {t('catalog.installsCount', {
                  count: installs,
                  defaultValue:      '{{count}} установок',
                  defaultValue_one:  '{{count}} установка',
                  defaultValue_few:  '{{count}} установки',
                  defaultValue_many: '{{count}} установок',
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
            title={installed ? t('sounds.alreadyInstalledTitle') : t('sounds.installTitle')}
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
        {lightbox && currentSrc && (
          <SimpleLightbox
            url={currentSrc}
            title={t('sounds.lightboxTitle', { title })}
            onClose={() => setLightbox(false)}
          />
        )}
      </AnimatePresence>

      <AnimatePresence>
        {videoOpen && hasVideo && (
          <VideoModal
            url={videoUrl}
            title={t('sounds.lightboxTitle', { title })}
            onClose={() => setVideoOpen(false)}
          />
        )}
      </AnimatePresence>
    </>
  );
}

function ReplaceConfirmModal({
  current, currentScreenshot, incoming, onKeep, onReplace,
}: {
  current:           CurrentSoundPackInfo;
  currentScreenshot: string;
  incoming:          { id: string; name: string; screenshot: string };
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

  return (
    <motion.div
      key="sounds-replace-modal"
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
        className="w-full max-w-[760px]"
      >
        <div
          className="relative overflow-hidden rounded-3xl p-7 flex flex-col gap-6
                     border border-white/[0.08]"
          style={{
            background:
              'linear-gradient(155deg, rgba(40,40,52,0.75), rgba(28,28,38,0.85))',
            boxShadow:
              'inset 0 1px 0 rgba(255,255,255,0.10), ' +
              '0 24px 56px rgba(0,0,0,0.55)',
            backdropFilter: 'blur(22px) saturate(140%)',
            WebkitBackdropFilter: 'blur(22px) saturate(140%)',
          }}
        >
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

          <header className="flex flex-col gap-2 text-center">
            <span className="text-[11px] uppercase tracking-[0.28em] text-text-muted font-semibold">
              {t('sounds.replaceEyebrow', 'Замена пака звуков')}
            </span>
            <h2 className="font-display text-xl font-bold uppercase tracking-wide text-text-primary leading-tight">
              {t('sounds.replaceTitle', 'Заменить пак звуков?')}
            </h2>
            <p className="text-sm text-text-secondary leading-relaxed">
              {t('sounds.replaceHint',
                'Сейчас уже активен другой пак. Перед установкой нового мы автоматически вернём стандартные файлы поверх и поставим выбранный.')}
            </p>
          </header>

          <div className="grid items-stretch gap-4 grid-cols-1 md:grid-cols-[1fr_auto_1fr]">
            <SoundPackPreviewTile
              variant="current"
              selected={picked === 'current'}
              label={t('sounds.tileCurrent')}
              badgeIcon={<CheckCircle2 size={11} />}
              badgeText={t('sounds.badgeInstalled')}
              name={current.name || current.id}
              screenshot={currentScreenshot}
              onPick={() => setPicked('current')}
            />
            <ArrowDivider />
            <SoundPackPreviewTile
              variant="incoming"
              selected={picked === 'incoming'}
              label={t('sounds.tileNew')}
              badgeIcon={<Sparkles size={11} />}
              badgeText={t('catalog.badgeCandidate')}
              name={incoming.name || incoming.id}
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
              className="h-11 w-full max-w-[420px] rounded-2xl overflow-hidden
                         bg-white text-black border border-white
                         text-sm font-bold uppercase tracking-[0.16em]
                         shadow-[0_10px_28px_-10px_rgba(255,255,255,0.55),inset_0_1px_0_rgba(255,255,255,0.85)]
                         transition flex items-center justify-center gap-2"
              style={{ outline: 'none' }}
            >
              {picked === 'current' ? t('sounds.keepCurrent') : t('sounds.installNew')}
              <ArrowRight size={14} />
            </motion.button>
            <button
              type="button"
              onClick={onKeep}
              className="text-[11px] uppercase tracking-[0.22em] text-text-muted
                         hover:text-text-primary transition-colors"
            >
              {t('common.cancel', 'Отмена')}
            </button>
          </div>
        </div>
      </motion.div>
    </motion.div>
  );
}

function SoundPackPreviewTile({
  variant, selected, label, badgeIcon, badgeText, name, screenshot, onPick,
}: {
  variant:    'current' | 'incoming';
  selected:   boolean;
  label:      string;
  badgeIcon:  React.ReactNode;
  badgeText:  string;
  name:       string;
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
              : 'bg-status-success/20 text-status-success border border-status-success/30')
          }
        >
          {badgeIcon}
          {badgeText}
        </span>
      </div>
      <div
        className={
          'relative aspect-square rounded-2xl overflow-hidden transition ' +
          (selected
            ? 'ring-2 ring-white ring-offset-0'
            : 'ring-1 ring-white/[0.08] group-hover:ring-white/[0.25]')
        }
        style={{
          background: 'linear-gradient(155deg, rgba(255,255,255,0.03), rgba(255,255,255,0))',
        }}
      >
        <div className="absolute inset-0 bg-glass-strong" />
        {screenshot ? (
          <>
            <div
              aria-hidden
              className="absolute inset-0"
              style={{
                background: `url(${screenshot}) center / cover no-repeat`,
                filter: 'blur(28px) brightness(0.55) saturate(1.2)',
                transform: 'scale(1.18)',
              }}
            />
            <div className="absolute inset-0 flex items-center justify-center">
              <img
                src={screenshot}
                alt=""
                draggable={false}
                className="w-full h-full object-cover"
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
            <Volume2 size={36} className="opacity-40" />
          </div>
        )}
        {selected && (
          <span
            className="absolute top-3 right-3 w-7 h-7 rounded-full flex items-center justify-center
                       bg-white text-black border border-white"
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
      >
        <ArrowRight size={16} />
      </div>
    </div>
  );
}

function SimpleLightbox({ url, title, onClose }: { url: string; title: string; onClose: () => void }) {
  const { t } = useTranslation();
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <motion.div
      key="sounds-lightbox"
      className="fixed inset-0 z-[100] bg-black/75 backdrop-blur-glass-ultra
                 backdrop-saturate-liquid flex items-center justify-center p-6"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.24 }}
      onClick={onClose}
    >
      <motion.div
        className="relative w-[min(90vw,1280px)] aspect-[16/9] max-h-[85vh] rounded-3xl overflow-hidden bg-glass-ultra"
        initial={{ opacity: 0, scale: 0.92 }}
        animate={{ opacity: 1, scale: 1 }}
        exit   ={{ opacity: 0, scale: 0.96 }}
        transition={{ duration: 0.32, ease: EASE_DEPTH }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="absolute top-0 inset-x-0 z-20 px-4 py-3 flex items-center gap-3
                        bg-gradient-to-b from-black/55 to-transparent pointer-events-none">
          <h2 className="text-sm font-semibold text-white truncate flex-1
                         drop-shadow-[0_2px_4px_rgba(0,0,0,0.8)]">{title}</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label={t('catalog.closeEsc')}
            className="pointer-events-auto w-9 h-9 rounded-lg flex items-center justify-center
                       text-white/85 bg-black/50 hover:bg-black/80 hover:text-white"
          >
            <X size={16} />
          </button>
        </div>
        <div className="absolute inset-0">
          <CarbonSurface weaveOpacity={0.7} glowIntensity={0.5} vignetteIntensity={0.6} />
          <img
            src={url}
            alt=""
            aria-hidden
            className="absolute inset-0 w-full h-full object-cover"
            style={{ filter: 'blur(32px) saturate(120%) brightness(0.55)', transform: 'scale(1.15)' }}
          />
          <img
            src={url}
            alt=""
            className="absolute inset-0 w-full h-full object-contain"
          />
        </div>
      </motion.div>
    </motion.div>
  );
}
