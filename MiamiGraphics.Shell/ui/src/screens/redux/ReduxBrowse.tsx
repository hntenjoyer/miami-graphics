import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Star,
  Download as DownloadIcon, Star as StarIcon, Clock, History, Sparkles,
} from 'lucide-react';
import { clsx } from 'clsx';
import { useReduxStore, type ReduxSort } from '@/store/reduxStore';
import { globalMeanRating, bayesianScore } from '@/utils/rating';
import type { ReduxItem } from '@/bridge/types';
import { ReduxCard } from './ReduxCard';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';
import kaptLogo from '@/assets/logo/kapt.png';
import rpLogo from '@/assets/logo/rp.png';
import { GlassDropdown, type GlassDropdownOption } from '@/design';
import { ScreenHero } from '@/screens/ScreenHero';
import { CatalogPreviewGate } from '@/components/CatalogPreviewGate';
import { shuffled } from '@/utils/shuffle';

let cachedReduxOrder: { sig: string; ids: string[] } | null = null;

let savedScrollTop = 0;
let savedRenderLimit = 0;

export function ReduxBrowse() {
  const { t } = useTranslation();
  const items = useReduxStore(s => s.items);
  const loading = useReduxStore(s => s.loading);
  const search = useReduxStore(s => s.search);
  const servers = useReduxStore(s => s.servers);
  const onlyFavorites = useReduxStore(s => s.onlyFavorites);
  const favorites = useReduxStore(s => s.favorites);
  const sort = useReduxStore(s => s.sort);
  const ratings = useReduxStore(s => s.ratings);
  const setSearch = useReduxStore(s => s.setSearch);
  const toggleServer = useReduxStore(s => s.toggleServer);
  const setOnlyFavorites = useReduxStore(s => s.setOnlyFavorites);
  const setSort = useReduxStore(s => s.setSort);

  const scrollRef = useRef<HTMLDivElement | null>(null);
  const renderLimitRef = useRef(0);
  const restoringRef = useRef(false);
  useLayoutEffect(() => {
    const el = scrollRef.current;
    const target = savedScrollTop;
    if (el && target > 0) el.scrollTop = target;
    if (target <= 0) {
      return () => { savedRenderLimit = renderLimitRef.current; };
    }
    restoringRef.current = true;
    const RESTORE_MS = 400;
    const startedAt = Date.now();
    let raf = 0;
    const tick = () => {
      const node = scrollRef.current;
      if (node) {
        node.scrollTop = target;
        if (Math.abs(node.scrollTop - target) > 2 && Date.now() - startedAt < RESTORE_MS) {
          raf = requestAnimationFrame(tick);
          return;
        }
      }
      restoringRef.current = false;
    };
    raf = requestAnimationFrame(tick);
    return () => {
      cancelAnimationFrame(raf);
      restoringRef.current = false;
      savedRenderLimit = renderLimitRef.current;
    };
  }, []);

  const shuffledDefault = useMemo(() => {
    if (items.length === 0) return items;
    const sig = items.map(i => i.id).slice().sort().join('|');
    if (cachedReduxOrder?.sig !== sig) {
      cachedReduxOrder = { sig, ids: shuffled(items).map(i => i.id) };
    }
    const pos = new Map(cachedReduxOrder.ids.map((id, i) => [id, i]));
    return items.slice().sort((a, b) => (pos.get(a.id) ?? 0) - (pos.get(b.id) ?? 0));
  }, [items]);

  const visible = useMemo(
    () => {
      const q = search.trim().toLowerCase();
      const base = sort === 'default' ? shuffledDefault : items;
      let filtered = onlyFavorites ? base.filter(i => favorites.has(i.id)) : base;
      if (servers.length > 0) {
        filtered = filtered.filter(i => servers.every(s => i.supportedServers.includes(s)));
      }
      if (q) {

        filtered = filtered.filter(i =>
          i.name.toLowerCase().includes(q)
          || i.id.toLowerCase().includes(q)
          || (i.author ?? '').toLowerCase().includes(q));
      }
      return sortItems(filtered, sort, ratings);
    },
    [items, shuffledDefault, onlyFavorites, favorites, sort, search, ratings, servers],
  );

  const previewUrls = useMemo(
    () => visible.slice(0, 16).map(it => it.previewUrl ?? '').filter(Boolean),
    [visible],
  );

  const PAGE_SIZE = 18;
  const [renderLimit, setRenderLimit] = useState(() => Math.max(PAGE_SIZE, savedRenderLimit));
  renderLimitRef.current = renderLimit;
  const resetSkip = useRef(true);
  useEffect(() => {
    if (resetSkip.current) { resetSkip.current = false; return; }
    setRenderLimit(PAGE_SIZE);
  }, [visible.length, search, servers, onlyFavorites, sort]);
  const rendered = useMemo(
    () => visible.slice(0, renderLimit),
    [visible, renderLimit],
  );
  const sentinelRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    if (renderLimit >= visible.length) return;
    const el = sentinelRef.current;
    if (!el || typeof IntersectionObserver === 'undefined') return;
    const io = new IntersectionObserver(
      entries => {
        if (entries[0]?.isIntersecting) {
          setRenderLimit(c => Math.min(visible.length, c + PAGE_SIZE));
        }
      },
      { root: scrollRef.current, rootMargin: '600px' },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [renderLimit, visible.length]);

  return (
    <div className="h-full flex flex-col">
      {}
      <header className="px-5 pt-3 pb-3 shrink-0">
        <ScreenHero
          title={t('redux.heroTitle')}
          subtitle={t('redux.heroSubtitle')}
          search={{
            value: search,
            onChange: setSearch,
            placeholder: t('redux.searchPlaceholder'),
            clearLabel: t('armor.searchClear'),
          }}
          trailing={
            <>
              <FavToggle
                active={onlyFavorites}
                onClick={() => setOnlyFavorites(!onlyFavorites)}
                label={t('redux.favoritesOnly')}
              />
              <span
                aria-hidden
                className="hidden sm:block h-7 w-px self-center
                           bg-gradient-to-b from-transparent via-white/15 to-transparent"
              />
              <ServerPill
                active={servers.includes('majestic')}
                label="Majestic"
                logo={majesticLogo}
                onClick={() => toggleServer('majestic')}
              />
              <ServerPill
                active={servers.includes('gta5rp')}
                label="GTA5RP"
                logo={gta5rpLogo}
                onClick={() => toggleServer('gta5rp')}
              />
              <ServerPill
                active={servers.includes('kapt')}
                label="КАПТ"
                logo={kaptLogo}
                invertOnDark
                onClick={() => toggleServer('kapt')}
              />
              <ServerPill
                active={servers.includes('rp')}
                label="РП"
                logo={rpLogo}
                onClick={() => toggleServer('rp')}
              />
              <span
                aria-hidden
                className="hidden sm:block h-7 w-px self-center
                           bg-gradient-to-b from-transparent via-white/15 to-transparent"
              />
              <SortSelect value={sort} onChange={setSort} />
              <span className="inline-flex items-center gap-1.5 px-3 h-9 rounded-xl
                               bg-white/[0.04] border border-white/[0.06]
                               text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted">
                <span className="tabular-nums text-text-primary">{visible.length}</span>
                <span>{t('redux.count', { count: visible.length }).replace(/^\d+\s*/, '')}</span>
              </span>
            </>
          }
        />
      </header>

      <div
        ref={scrollRef}
        onScroll={(e) => { if (!restoringRef.current) savedScrollTop = e.currentTarget.scrollTop; }}
        className="flex-1 overflow-y-auto px-5 pt-2 pb-8"
      >
        {loading && items.length === 0 ? (
          <div className="text-center text-text-muted py-12">{t('redux.loading')}</div>
        ) : visible.length === 0 ? (
          <div className="text-center text-text-muted py-12">
            {items.length === 0
              ? t('redux.emptyCatalog')
              : onlyFavorites && favorites.size === 0
                ? t('redux.emptyFavorites')
                : t('catalog.noResults')}
          </div>
        ) : (

          <CatalogPreviewGate urls={previewUrls} ready={!loading || items.length > 0}>
            <motion.div layout className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-5">
              <AnimatePresence mode="popLayout">
                {rendered.map((it, i) => (
                  <motion.div
                    key={it.id}
                    layout
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    exit={{ opacity: 0 }}
                    transition={{
                      duration: 0.32,
                      ease: [0.22, 1, 0.36, 1],
                      delay: i < PAGE_SIZE ? Math.min(i, 16) * 0.04 : 0,
                    }}
                  >
                    <ReduxCard item={it} index={i} />
                  </motion.div>
                ))}
              </AnimatePresence>
            </motion.div>
            {renderLimit < visible.length && (
              <div ref={sentinelRef} aria-hidden className="h-4 w-full" />
            )}
          </CatalogPreviewGate>
        )}
      </div>
    </div>
  );
}

interface ServerPillProps {
  active:  boolean;
  label:   string;
  logo?:   string;
  invertOnDark?: boolean;
  onClick: () => void;
}

function ServerPill({ active, label, logo, invertOnDark, onClick }: ServerPillProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={label}
      aria-label={label}
      aria-pressed={active}
      style={{ outline: 'none' }}
      className={clsx(
        'group relative inline-flex items-center gap-2 h-10 pl-2.5 pr-3.5 rounded-xl',
        'text-[12px] font-bold uppercase tracking-[0.12em]',
        'border transition-all duration-300 ease-smooth overflow-hidden',
        active
          ? 'bg-white text-black border-white ' +
            'shadow-pill-active'
          : 'bg-white/[0.04] text-text-secondary border-white/[0.06] ' +
            'hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]',
      )}
    >
      {}
      <span
        className={clsx(
          'relative shrink-0 w-6 h-6 rounded-md flex items-center justify-center',
          active ? 'bg-black/[0.06]' : 'bg-white/[0.06]',
        )}
      >
        {logo
          ? <img
              src={logo}
              alt=""
              className="w-4 h-4 object-contain"
              style={invertOnDark && !active ? { filter: 'invert(1)' } : undefined}
            />
          : <span className="text-[9px] font-bold leading-none tracking-tight">{label.slice(0, 2)}</span>}
      </span>
      <span>{label}</span>
    </button>
  );
}

interface FavToggleProps {
  active:  boolean;
  onClick: () => void;
  label:   string;
}

function FavToggle({ active, onClick, label }: FavToggleProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={label}
      aria-label={label}
      aria-pressed={active}
      style={{ outline: 'none' }}
      className={clsx(
        'inline-flex items-center justify-center w-10 h-10 rounded-xl border',
        'transition-all duration-300 ease-smooth',
        active
          ? 'bg-white text-black border-white ' +
            'shadow-pill-active'
          : 'bg-white/[0.04] border-white/[0.06] text-text-muted ' +
            'hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]',
      )}
    >
      <Star size={15} fill={active ? 'currentColor' : 'none'} strokeWidth={active ? 2 : 1.8} />
    </button>
  );
}

function SortSelect({ value, onChange }: { value: ReduxSort; onChange: (v: ReduxSort) => void }) {
  const { t } = useTranslation();

  const options: GlassDropdownOption<ReduxSort>[] = [
    { value: 'default',   label: t('redux.sort.default', 'По умолчанию'), icon: Sparkles },
    { value: 'newest',    label: t('redux.sort.newest', 'Новее'),  icon: Clock },
    { value: 'oldest',    label: t('redux.sort.oldest', 'Старее'), icon: History },
    { value: 'downloads', label: t('redux.sort.downloads'), icon: DownloadIcon },
    { value: 'rating',    label: t('redux.sort.rating'),    icon: StarIcon },
  ];
  return (
    <GlassDropdown<ReduxSort>
      value={value}
      options={options}
      onChange={onChange}
      ariaLabel={t('redux.sort.label')}
      title={t('redux.sort.label')}
      width={210}
    />
  );
}

function sortItems(
  list: ReduxItem[],
  sort: ReduxSort,
  ratings: Record<string, { avg: number; count: number }>,
): ReduxItem[] {
  const out = [...list];
  if (sort === 'newest') {
    out.sort((a, b) => (b.uploadedAt ?? '').localeCompare(a.uploadedAt ?? ''));
  } else if (sort === 'oldest') {
    out.sort((a, b) => (a.uploadedAt ?? '').localeCompare(b.uploadedAt ?? ''));
  } else if (sort === 'downloads') {
    out.sort((a, b) => b.downloadCount - a.downloadCount);
  } else if (sort === 'rating') {
    const C = globalMeanRating(ratings);
    out.sort((a, b) => {
      const ra = ratings[a.id];
      const rb = ratings[b.id];
      if (ra?.count && rb?.count) {
        return bayesianScore(rb, C) - bayesianScore(ra, C) || rb.count - ra.count;
      }
      if (ra?.count) return -1;
      if (rb?.count) return 1;
      return b.downloadCount - a.downloadCount;
    });
  }
  return out;
}
