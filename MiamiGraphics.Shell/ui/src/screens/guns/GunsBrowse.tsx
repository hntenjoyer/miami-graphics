import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Crosshair, Layers, Loader2, Star, Sparkles, Clock, Download as DownloadIcon, Palette } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { useGunpackStore } from '@/store/gunpackStore';
import { useCustomGunStore } from '@/store/customGunStore';
import { EASE_DEPTH, GlassDropdown, type GlassDropdownOption } from '@/design';
import { GunpackCard } from './GunpackCard';
import { CustomBrowse } from './custom/CustomBrowse';
import { useCanSeeCustomGuns } from '@/store/sessionStore';
import { ScreenHero } from '@/screens/ScreenHero';
import { CatalogPreviewGate } from '@/components/CatalogPreviewGate';
import { shuffled } from '@/utils/shuffle';

type GunsSubTab = 'gunpacks' | 'guns' | 'custom';

const savedScrollTops: Record<GunsSubTab, number> = { gunpacks: 0, guns: 0, custom: 0 };
let savedRenderLimit = 0;

let cachedGunpackOrder: { sig: string; ids: string[] } | null = null;

type GunpackSort = 'default' | 'new' | 'old' | 'downloads';

let savedSortBy: GunpackSort = 'default';
let savedSearch = '';
let savedOnlyFavorites = false;

const CATEGORY_LABEL: Record<string, string> = {
  assault: 'Штурмовая',
  shotgun: 'Дробовик',
  sniper:  'Снайперская',
  mg:      'Пулемёт',
  pistol:  'Пистолет',
  smg:     'ПП',
};

interface Props {
  onOpenPack:          (id: string) => void;
  onOpenWhitelistGun:  (internalName: string) => void;
}

export function GunsBrowse({ onOpenPack, onOpenWhitelistGun }: Props) {
  const { t } = useTranslation();

  const sortOptions = useMemo<GlassDropdownOption<GunpackSort>[]>(() => [
    { value: 'default',   label: t('redux.sort.default', 'По умолчанию'),     icon: Sparkles },
    { value: 'new',       label: t('redux.sort.newest', 'Новее'),             icon: Clock },
    { value: 'old',       label: t('redux.sort.oldest', 'Старее'),            icon: Clock },
    { value: 'downloads', label: t('redux.sort.downloads', 'По скачиваниям'), icon: DownloadIcon },
  ], [t]);

  const canSeeCustom = useCanSeeCustomGuns();

  const sub    = useGunpackStore(s => s.gunsSubTab);
  const setSub = useGunpackStore(s => s.setGunsSubTab);
  const [search, setSearch] = useState(savedSearch);
  const [onlyFavorites, setOnlyFavorites] = useState(savedOnlyFavorites);
  const [sortBy, setSortBy] = useState<GunpackSort>(savedSortBy);
  useEffect(() => { savedSortBy = sortBy; }, [sortBy]);
  useEffect(() => { savedSearch = search; }, [search]);
  useEffect(() => { savedOnlyFavorites = onlyFavorites; }, [onlyFavorites]);
  const publicPacks = useGunpackStore(s => s.publicPacks);
  const loadingPacks = useGunpackStore(s => s.loadingPublic);
  const loadPublicPacks = useGunpackStore(s => s.loadPublicPacks);
  const favorites = useGunpackStore(s => s.gunpackFavorites);

  const whitelist = useGunpackStore(s => s.whitelist);
  const loadWhitelist = useGunpackStore(s => s.loadWhitelist);

  const customView  = useCustomGunStore(s => s.view);
  const customAll   = useCustomGunStore(s => s.all);
  const customMine  = useCustomGunStore(s => s.mine);
  const loadCustom  = useCustomGunStore(s => s.load);

  const scrollRef = useRef<HTMLDivElement | null>(null);
  const restoringRef = useRef(false);
  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const target = savedScrollTops[sub] ?? 0;
    el.scrollTop = target;
    if (target <= 0) return;
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
    return () => { cancelAnimationFrame(raf); restoringRef.current = false; };
  }, [sub]);

  const allGuns                = useGunpackStore(s => s.allGuns);
  const loadAllGuns            = useGunpackStore(s => s.loadAllGuns);
  const installedGunpack       = useGunpackStore(s => s.installedGunpack);
  const installedSelectedGuns  = useGunpackStore(s => s.installedSelectedGuns);
  const loadInstallState       = useGunpackStore(s => s.loadInstallState);

  useEffect(() => {
    void loadPublicPacks();
    void loadWhitelist();
    void loadAllGuns();
    void loadInstallState();
    if (canSeeCustom) void loadCustom();
  }, [loadPublicPacks, loadWhitelist, loadAllGuns, loadInstallState, loadCustom, canSeeCustom]);

  useEffect(() => {
    if (sub === 'custom' && !canSeeCustom) setSub('gunpacks');
  }, [sub, canSeeCustom, setSub]);

  useEffect(() => {
    if (sub !== 'gunpacks' && onlyFavorites) setOnlyFavorites(false);
  }, [sub, onlyFavorites]);

  const overrides = useMemo(() => {
    const out: Record<string, { previewUrl: string | null; packName: string }> = {};
    const internalNameOf = (g: typeof allGuns[number]) => `${g.weaponPrefix ?? ''}${g.baseName}`;

    if (installedGunpack.activeGunpackId) {
      const packGuns = allGuns.filter(g => g.packId === installedGunpack.activeGunpackId);
      const packName = installedGunpack.activeGunpackName ?? '';
      for (const g of packGuns) {
        const name = internalNameOf(g);
        if (name) {
          out[name] = { previewUrl: g.previewUrl, packName };
        }
      }
    }

    for (const sg of installedSelectedGuns) {
      const sourceGun = allGuns.find(
        g => g.packId === sg.gunpackId && internalNameOf(g) === sg.internalName,
      );
      if (sourceGun) {
        out[sg.internalName] = {
          previewUrl: sourceGun.previewUrl,
          packName: sg.gunpackName,
        };
      }
    }
    return out;
  }, [installedGunpack, installedSelectedGuns, allGuns]);

  const shuffledPacks = useMemo(() => {
    if (publicPacks.length === 0) return publicPacks;
    const sig = publicPacks.map(p => p.id).slice().sort().join('|');
    if (cachedGunpackOrder?.sig !== sig) {
      cachedGunpackOrder = { sig, ids: shuffled(publicPacks).map(p => p.id) };
    }
    const pos = new Map(cachedGunpackOrder.ids.map((id, i) => [id, i]));
    return publicPacks.slice().sort((a, b) => (pos.get(a.id) ?? 0) - (pos.get(b.id) ?? 0));
  }, [publicPacks]);

  const orderedPacks = useMemo(() => {
    if (sortBy === 'default') return shuffledPacks;
    if (sortBy === 'downloads') {
      return publicPacks.slice().sort((a, b) => (b.downloadCount ?? 0) - (a.downloadCount ?? 0));
    }
    return publicPacks.slice().sort((a, b) => {
      const ta = Date.parse(a.uploadedAt ?? '') || 0;
      const tb = Date.parse(b.uploadedAt ?? '') || 0;
      return sortBy === 'old' ? ta - tb : tb - ta;
    });
  }, [sortBy, shuffledPacks, publicPacks]);

  const filteredPacks = useMemo(() => {
    const q = search.trim().toLowerCase();
    let list = orderedPacks;
    if (onlyFavorites) list = list.filter(p => favorites.has(p.id));
    if (q) {
      list = list.filter(p =>
        p.name.toLowerCase().includes(q)
        || (p.author ?? '').toLowerCase().includes(q)
        || p.id.toLowerCase().includes(q));
    }
    return list;
  }, [orderedPacks, search, onlyFavorites, favorites]);

  const filteredWhitelist = useMemo(() => {
    const q = search.trim().toLowerCase();
    const list = q
      ? whitelist.filter(w =>
          w.displayName.toLowerCase().includes(q)
          || w.internalName.toLowerCase().includes(q))
      : whitelist;
    return [...list].sort((a, b) => a.sortOrder - b.sortOrder);
  }, [whitelist, search]);

  const customCount = useMemo(() => {
    const src = customView === 'mine' ? customMine : customAll;
    const q = search.trim().toLowerCase();
    if (!q) return src.length;
    return src.filter(g =>
      g.displayName.toLowerCase().includes(q)
      || g.ownerName.toLowerCase().includes(q)).length;
  }, [customView, customAll, customMine, search]);

  const activeCount = sub === 'gunpacks' ? filteredPacks.length
    : sub === 'guns' ? filteredWhitelist.length
    : customCount;

  const hasSearch = search.trim().length > 0;

  return (
    <div className="h-full flex flex-col">
      {}
      <header className="px-5 pt-3 pb-3 shrink-0 flex flex-col gap-3">
        <ScreenHero
          title={t('guns.heroTitle')}
          subtitle={t('guns.heroSubtitle')}
          search={{
            value: search,
            onChange: setSearch,
            placeholder: sub === 'gunpacks' ? t('guns.searchPlaceholderPacks', 'Поиск по имени, автору')
              : sub === 'guns' ? t('guns.searchPlaceholderGuns', 'Поиск по пушкам')
              : t('guns.searchPlaceholderCustom', 'Поиск по скинам, автору'),
            clearLabel: t('armor.searchClear'),
          }}
          trailing={
            <>
              <SubPill
                active={sub === 'gunpacks'}
                onClick={() => setSub('gunpacks')}
                icon={<Layers size={14} strokeWidth={2} />}
                label={t('guns.tabGunpacks', 'Ганпаки')}
                count={filteredPacks.length}
              />
              <SubPill
                active={sub === 'guns'}
                onClick={() => setSub('guns')}
                icon={<Crosshair size={14} strokeWidth={2} />}
                label={t('guns.tabSelective', 'Выборочная')}
                count={filteredWhitelist.length}
              />
              {canSeeCustom && (
                <SubPill
                  active={sub === 'custom'}
                  onClick={() => setSub('custom')}
                  icon={<Palette size={14} strokeWidth={2} />}
                  label={t('guns.tabCustom', 'Кастомные')}
                  count={customCount}
                />
              )}
              {}
              <AnimatePresence initial={false}>
                {sub === 'gunpacks' && (
                  <motion.div
                    key="fav-toggle-inline"
                    initial={{ opacity: 0, scale: 0.85, width: 0, marginRight: -10 }}
                    animate={{ opacity: 1, scale: 1, width: 44, marginRight: 0 }}
                    exit={{ opacity: 0, scale: 0.85, width: 0, marginRight: -10 }}
                    transition={{ duration: 0.28, ease: EASE_DEPTH }}
                    className="overflow-hidden shrink-0"
                  >
                    <button
                      type="button"
                      onClick={() => setOnlyFavorites(!onlyFavorites)}
                      title={t('guns.onlyFavorites', 'Только избранные')}
                      aria-label={t('guns.onlyFavorites', 'Только избранные')}
                      style={{ outline: 'none' }}
                      className={
                        'inline-flex items-center justify-center w-11 h-11 rounded-2xl border shrink-0 ' +
                        'transition-[background-color,color,border-color] duration-300 ease-smooth ' +
                        (onlyFavorites
                          ? 'bg-bg-elevated text-accent border-white/[0.16] ' +
                            'shadow-[inset_0_1px_0_rgba(255,255,255,0.08)]'
                          : 'bg-white/[0.03] border-white/[0.07] text-text-muted ' +
                            'hover:bg-white/[0.07] hover:text-text-primary hover:border-white/[0.16]')
                      }
                    >
                      <Star size={15} fill={onlyFavorites ? 'currentColor' : 'none'} strokeWidth={2} />
                    </button>
                  </motion.div>
                )}
              </AnimatePresence>
              {sub === 'gunpacks' && publicPacks.length > 0 && (
                <>
                  <span
                    aria-hidden
                    className="hidden sm:block h-7 w-px self-center shrink-0
                               bg-gradient-to-b from-transparent via-white/15 to-transparent"
                  />
                  <div className="shrink-0">
                    <GlassDropdown<GunpackSort>
                      value={sortBy}
                      options={sortOptions}
                      onChange={setSortBy}
                      ariaLabel={t('redux.sort.label', 'Сортировка')}
                      title={t('redux.sort.label', 'Сортировка')}
                      width={210}
                    />
                  </div>
                </>
              )}
              <span className="inline-flex items-center gap-1.5 px-3.5 h-11 rounded-2xl
                               bg-white/[0.03] border border-white/[0.07]
                               text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted shrink-0">
                <span className="tabular-nums text-text-primary text-right inline-block min-w-[2.6ch]">
                  {activeCount}
                </span>
                <span>{sub === 'gunpacks'
                  ? t('guns.countPacks', { count: activeCount, defaultValue: 'паков' })
                  : sub === 'guns'
                    ? t('guns.countGuns', { count: activeCount, defaultValue: 'пушек' })
                    : t('guns.countSkins', { count: activeCount, defaultValue: 'скинов' })}</span>
              </span>
            </>
          }
        />
      </header>

      {}
      <div
        ref={scrollRef}
        onScroll={(e) => { if (!restoringRef.current) savedScrollTops[sub] = e.currentTarget.scrollTop; }}
        className="flex-1 overflow-y-auto px-5 pt-2 pb-8"
      >
        <AnimatePresence mode="wait" initial={false}>
          <motion.div
            key={sub}
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -8, transition: { duration: 0.12, ease: 'easeOut' } }}
            transition={{ duration: 0.32, ease: [0.22, 1, 0.36, 1] }}
          >
            {sub === 'gunpacks' && (
              <PackGrid
                packs={filteredPacks}
                total={publicPacks.length}
                loading={loadingPacks}
                onOpenPack={onOpenPack}
                onlyFavorites={onlyFavorites}
                onShowAll={() => setOnlyFavorites(false)}
                hasSearch={hasSearch}
                onClearSearch={() => setSearch('')}
              />
            )}
            {sub === 'guns' && (
              <WhitelistGunGrid
                entries={filteredWhitelist}
                onOpen={onOpenWhitelistGun}
                overrides={overrides}
                hasSearch={hasSearch}
                onClearSearch={() => setSearch('')}
              />
            )}
            {canSeeCustom && sub === 'custom' && (
              <CustomBrowse search={search} />
            )}
          </motion.div>
        </AnimatePresence>
      </div>
    </div>
  );
}

function SubPill({
  active, onClick, icon, label, count,
}: {
  active: boolean;
  onClick: () => void;
  icon: React.ReactNode;
  label: string;
  count: number;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{ outline: 'none' }}
      className={
        'inline-flex items-center gap-2 pl-3.5 pr-2.5 h-11 rounded-2xl border ' +
        'text-[12px] font-bold uppercase tracking-[0.12em] ' +
        'transition-[background-color,color,border-color,box-shadow] duration-300 ease-smooth ' +
        (active
          ? 'bg-bg-elevated text-text-primary border-white/[0.16] ' +
            'shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_10px_26px_-12px_rgba(0,0,0,0.65)]'
          : 'bg-white/[0.03] border-white/[0.07] text-text-secondary ' +
            'hover:bg-white/[0.07] hover:text-text-primary hover:border-white/[0.16]')
      }
    >
      {icon}
      <span>{label}</span>
      <span className={
        'text-[10px] tabular-nums leading-none font-bold px-1.5 py-1 rounded-md ' +
        (active
          ? 'bg-white/[0.08] text-text-secondary'
          : 'bg-white/[0.06] text-text-muted')
      }>
        {count}
      </span>
    </button>
  );
}

function EmptyAction({
  icon, label, onClick,
}: { icon: React.ReactNode; label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{ outline: 'none' }}
      className="inline-flex items-center gap-2 px-4 h-10 rounded-2xl border
                 bg-white/[0.03] border-white/[0.07] text-text-secondary
                 text-[11px] font-bold uppercase tracking-[0.16em]
                 transition-[background-color,color,border-color] duration-300 ease-smooth
                 hover:bg-white/[0.07] hover:text-text-primary hover:border-white/[0.16]"
    >
      {icon}
      {label}
    </button>
  );
}

function PackGrid({
  packs, total, loading, onOpenPack, onlyFavorites, onShowAll, hasSearch, onClearSearch,
}: {
  packs: import('@/bridge/types').Gunpack[];
  total: number;
  loading: boolean;
  onOpenPack: (id: string) => void;
  onlyFavorites: boolean;
  onShowAll: () => void;
  hasSearch: boolean;
  onClearSearch: () => void;
}) {
  const { t } = useTranslation();

  const PAGE_SIZE = 18;
  const [renderLimit, setRenderLimit] = useState(() => Math.max(PAGE_SIZE, savedRenderLimit));
  const renderLimitRef = useRef(renderLimit);
  renderLimitRef.current = renderLimit;
  useEffect(() => () => { savedRenderLimit = renderLimitRef.current; }, []);
  const resetSkip = useRef(true);
  useEffect(() => {
    if (resetSkip.current) { resetSkip.current = false; return; }
    setRenderLimit(PAGE_SIZE);
  }, [packs.length]);
  const sentinelRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    if (renderLimit >= packs.length) return;
    const el = sentinelRef.current;
    if (!el || typeof IntersectionObserver === 'undefined') return;
    const io = new IntersectionObserver(
      entries => {
        if (entries[0]?.isIntersecting) {
          setRenderLimit(c => Math.min(packs.length, c + PAGE_SIZE));
        }
      },
      { rootMargin: '600px' },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [renderLimit, packs.length]);

  if (loading && packs.length === 0) {
    return (
      <div className="py-16 flex items-center justify-center text-text-muted gap-2">
        <Loader2 size={16} className="animate-spin" />
        <span className="text-sm">{t('armor.loading', 'Загружаем каталог…')}</span>
      </div>
    );
  }
  if (packs.length === 0) {
    const reason = total === 0 ? 'catalog' : hasSearch ? 'search' : 'favorites';
    return (
      <div className="py-20 flex flex-col items-center justify-center text-text-muted gap-3">
        <Crosshair size={48} className="opacity-30" />
        <p className="text-sm">
          {reason === 'catalog'
            ? t('guns.emptyCatalog', 'Пока нет ни одного опубликованного ганпака.')
            : reason === 'search'
              ? (onlyFavorites
                  ? t('guns.emptySearchFavorites', 'Среди избранного ничего не нашлось по запросу.')
                  : t('guns.emptySearch', 'Ничего не нашлось по запросу.'))
              : t('guns.emptyFavorites', 'В избранном пока пусто. Жми на звёздочку у понравившегося пака.')}
        </p>
        {(onlyFavorites || hasSearch) && (
          <div className="flex flex-wrap items-center justify-center gap-2">
            {hasSearch && (
              <EmptyAction
                icon={<Crosshair size={13} strokeWidth={2} />}
                label={t('guns.clearSearch', 'Сбросить поиск')}
                onClick={onClearSearch}
              />
            )}
            {onlyFavorites && (
              <EmptyAction
                icon={<Star size={13} strokeWidth={2} />}
                label={t('guns.showAll', 'Показать все')}
                onClick={onShowAll}
              />
            )}
          </div>
        )}
      </div>
    );
  }

  const previewUrls = packs.slice(0, 16)
    .map(p => (p.coverKind === 'image' ? p.coverUrl : null))
    .filter((u): u is string => !!u);

  const rendered = packs.slice(0, renderLimit);

  return (
    <CatalogPreviewGate urls={previewUrls} ready={!loading || packs.length > 0}>
      <motion.div
        layout
        className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-6 max-w-[1500px] 2xl:max-w-[1700px] mx-auto"
      >
        <AnimatePresence mode="popLayout">
          {rendered.map((p, i) => (
            <motion.div
              key={p.id}
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
              <GunpackCard pack={p} index={i} onClick={() => onOpenPack(p.id)} />
            </motion.div>
          ))}
        </AnimatePresence>
      </motion.div>
      {renderLimit < packs.length && (
        <div ref={sentinelRef} aria-hidden className="h-4 w-full" />
      )}
    </CatalogPreviewGate>
  );
}

function WhitelistGunGrid({
  entries, onOpen, overrides, hasSearch, onClearSearch,
}: {
  entries: import('@/bridge/types').GunpackWhitelistEntry[];
  onOpen: (internalName: string) => void;
  overrides: Record<string, { previewUrl: string | null; packName: string }>;
  hasSearch: boolean;
  onClearSearch: () => void;
}) {
  const { t } = useTranslation();
  if (entries.length === 0) {
    return (
      <div className="py-20 flex flex-col items-center justify-center text-text-muted gap-3">
        <Crosshair size={48} className="opacity-30" />
        <p className="text-sm">
          {hasSearch
            ? t('guns.emptySearch', 'Ничего не нашлось по запросу.')
            : t('guns.whitelistEmpty', 'Каталог пушек пока пуст. Загляни позже.')}
        </p>
        {hasSearch && (
          <EmptyAction
            icon={<Crosshair size={13} strokeWidth={2} />}
            label={t('guns.clearSearch', 'Сбросить поиск')}
            onClick={onClearSearch}
          />
        )}
      </div>
    );
  }
  return (
    <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-7 gap-3 max-w-[1500px] 2xl:max-w-[1700px] mx-auto">
      {entries.map(e => (
        <WhitelistGunTile
          key={e.internalName}
          entry={e}
          onOpen={() => onOpen(e.internalName)}
          override={overrides[e.internalName]}
        />
      ))}
    </div>
  );
}

function WhitelistGunTile({
  entry, onOpen, override,
}: {
  entry: import('@/bridge/types').GunpackWhitelistEntry;
  onOpen: () => void;
  override?: { previewUrl: string | null; packName: string };
}) {
  const { t } = useTranslation();
  const categoryLabel = t(`guns.category.${entry.category}`, CATEGORY_LABEL[entry.category] ?? entry.category);

  const effectivePreview = override?.previewUrl ?? entry.previewUrl;
  const isReplaced = !!override?.previewUrl;
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onOpen(); } }}
      className={
        'group relative aspect-square rounded-2xl overflow-hidden cursor-pointer ' +
        'bg-bg-elevated border ' +
        'transition-[border-color,box-shadow,transform] duration-300 ease-smooth ' +
        'hover:shadow-glow-accent hover:-translate-y-0.5 ' +
        (isReplaced
          ? 'border-[color-mix(in_srgb,var(--accent)_40%,transparent)] hover:border-[color-mix(in_srgb,var(--accent)_70%,transparent)]'
          : 'border-glass-border hover:border-accent/50')
      }
    >
      {}
      <div className="absolute inset-0 bottom-[34%] flex items-center justify-center bg-gradient-to-b from-black/20 to-transparent">
        {effectivePreview ? (
          <img
            src={effectivePreview}
            alt=""
            draggable={false}
            loading="lazy"
            className="w-full h-full object-contain px-3 pb-2 pt-1 select-none
                       transition-transform duration-700 ease-smooth
                       group-hover:scale-[1.04]"
            style={{ transformOrigin: '50% 92%', objectPosition: '50% 92%' }}
            onError={e => (e.currentTarget.style.display = 'none')}
          />
        ) : (
          <Crosshair size={42} className="text-text-muted opacity-40" />
        )}
      </div>

      {}
      {isReplaced && (
        <div className="absolute top-2 left-2 right-2 flex items-center justify-end pointer-events-none">
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md
                           bg-[color-mix(in_srgb,var(--accent)_28%,rgba(0,0,0,0.4))]
                           border border-[color-mix(in_srgb,var(--accent)_40%,transparent)]
                           backdrop-blur-md
                           text-[9px] font-bold uppercase tracking-[0.14em] text-white">
            <Sparkles size={9} className="text-accent" />
            <span className="truncate max-w-[120px]" title={override!.packName}>
              {override!.packName}
            </span>
          </span>
        </div>
      )}

      <div className="absolute bottom-0 inset-x-0 px-3 py-2.5 bg-gradient-to-t from-black/90 to-transparent">
        <div className="font-display text-sm font-semibold text-white truncate" title={entry.displayName}>
          {entry.displayName}
        </div>
        <div className="text-[10px] uppercase tracking-wider text-white/60">
          {categoryLabel}
        </div>
      </div>
    </div>
  );
}
