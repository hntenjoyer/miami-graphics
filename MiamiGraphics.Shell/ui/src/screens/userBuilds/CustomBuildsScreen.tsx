import { useEffect, useMemo, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { Boxes, Plus, Trash2, Layers, Crosshair, User, Download, Shield, Search, Loader2, Eye, Star, Clock, X } from 'lucide-react';
import { GlassPanel, GlassDropdown, type GlassDropdownOption, AccentLoader, EASE_DEPTH } from '@/design';
import { ConfirmModal } from '@/components/ConfirmModal';
import { bridge } from '@/bridge';
import { globalMeanRating, bayesianScore } from '@/utils/rating';
import { useUserBuildsStore } from '@/store/userBuildsStore';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useSessionStore } from '@/store/sessionStore';
import { CreateBuildScreen } from './CreateBuildScreen';
import { BuildDetailScreen } from './BuildDetailScreen';
import type { ReduxItem, Gunpack } from '@/bridge/types';

type BuildSort = 'new' | 'rating' | 'downloads';

export function CustomBuildsScreen() {
  const { t } = useTranslation();
  const buildSortOptions = useMemo<GlassDropdownOption<BuildSort>[]>(() => [
    { value: 'new',       label: t('userBuilds.sortNew', 'Новые'),            icon: Clock },
    { value: 'rating',    label: t('userBuilds.sortRating', 'Рейтинг'),       icon: Star },
    { value: 'downloads', label: t('userBuilds.sortDownloads', 'Скачивания'), icon: Download },
  ], [t]);
  const builds = useUserBuildsStore(s => s.builds);
  const loading = useUserBuildsStore(s => s.loading);
  const loadBuilds = useUserBuildsStore(s => s.load);
  const removeBuild = useUserBuildsStore(s => s.remove);

  const auth   = useSessionStore(s => s.auth);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const isGuest = !userId;

  const [deleteCandidate, setDeleteCandidate] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const candidate = useMemo(
    () => deleteCandidate ? builds.find(b => b.id === deleteCandidate) ?? null : null,
    [deleteCandidate, builds]);
  const onConfirmDelete = async () => {
    if (!deleteCandidate || deleting) return;
    setDeleting(true);
    try {
      await removeBuild(deleteCandidate);
      setDeleteCandidate(null);
    } catch (err) {
      console.warn('[builds] delete failed', err);
    } finally {
      setDeleting(false);
    }
  };

  const reduxList = useReduxStore(s => s.items) ?? [];
  const loadReduxes = useReduxStore(s => s.load);
  const publicPacks = useGunpackStore(s => s.publicPacks) ?? [];
  const loadPublicPacks = useGunpackStore(s => s.loadPublicPacks);

  const [initialLoaded, setInitialLoaded] = useState(false);
  useEffect(() => {
    if (reduxList.length === 0)  void loadReduxes();
    if (publicPacks.length === 0) void loadPublicPacks();
    void loadBuilds().finally(() => setInitialLoaded(true));
  }, []);

  const [allReduxes, setAllReduxes] = useState<ReduxItem[]>([]);
  useEffect(() => {
    let cancelled = false;
    bridge.reduxList(undefined, undefined)
      .then(list => { if (!cancelled) setAllReduxes(list); })
      .catch(e => console.warn('[builds] reduxList(all) failed', e));
    return () => { cancelled = true; };
  }, []);

  type View = { kind: 'list' } | { kind: 'create' } | { kind: 'detail'; buildId: string };
  const [view, setView] = useState<View>({ kind: 'list' });
  const [highlightId, setHighlightId] = useState<string | null>(null);

  const pendingDetailId = useUserBuildsStore(s => s.pendingDetailId);
  const clearPendingDetail = useUserBuildsStore(s => s.clearPendingDetail);
  useEffect(() => {
    if (pendingDetailId) {
      setView({ kind: 'detail', buildId: pendingDetailId });
      clearPendingDetail();
    }
  }, [pendingDetailId, clearPendingDetail]);

  const [ratings, setRatings] = useState<Record<string, { avg: number; count: number }>>({});
  const ratingsRequestedRef = useRef<Set<string>>(new Set());
  const fetchRatingsFor = (list: { id: string }[]) => {
    const missing = list.filter(b => !ratingsRequestedRef.current.has(b.id));
    if (missing.length === 0) return;
    for (const b of missing) ratingsRequestedRef.current.add(b.id);
    void Promise.all(missing.map(b =>
      bridge.userBuildReviewsList(b.id)
        .then(list => {
          const count = list.length;
          const avg = count ? list.reduce((s, r) => s + r.rating, 0) / count : 0;
          return [b.id, { avg, count }] as const;
        })
        .catch(() => [b.id, { avg: 0, count: 0 }] as const),
    )).then(entries => setRatings(prev => ({ ...prev, ...Object.fromEntries(entries) })));
  };

  const [sortBy, setSortBy] = useState<'new' | 'rating' | 'downloads'>('new');
  const [mineOnly, setMineOnly] = useState(false);
  const baseBuilds = useMemo(
    () => (mineOnly && userId ? builds.filter(b => b.authorUserId === userId) : builds),
    [builds, mineOnly, userId]);
  const mineCount = useMemo(
    () => (userId ? builds.filter(b => b.authorUserId === userId).length : 0),
    [builds, userId]);
  const sortedBuilds = useMemo(() => {
    const arr = [...baseBuilds];
    const C = globalMeanRating(ratings);
    arr.sort((a, b) => {
      if (sortBy === 'downloads') return b.downloadCount - a.downloadCount;
      if (sortBy === 'rating') {
        const d = bayesianScore(ratings[b.id], C) - bayesianScore(ratings[a.id], C);
        return Number.isFinite(d) && d !== 0
          ? d
          : (ratings[b.id]?.count ?? 0) - (ratings[a.id]?.count ?? 0);
      }
      return b.createdAt - a.createdAt;
    });
    return arr;
  }, [baseBuilds, sortBy, ratings]);
  const [searchValue, setSearchValue] = useState('');

  const PAGE_SIZE = 10;
  const INITIAL_VISIBLE = 14;
  const [visibleCount, setVisibleCount] = useState(INITIAL_VISIBLE);
  useEffect(() => { setVisibleCount(INITIAL_VISIBLE); }, [sortBy, searchValue, mineOnly]);
  const visibleBuilds = useMemo(
    () => sortedBuilds.slice(0, visibleCount),
    [sortedBuilds, visibleCount]);
  const hasMore = visibleCount < sortedBuilds.length;

  const ioRef = useRef<IntersectionObserver | null>(null);
  const sentinelElRef = useRef<HTMLDivElement | null>(null);
  const sentinelRef = (el: HTMLDivElement | null) => {
    ioRef.current?.disconnect();
    ioRef.current = null;
    sentinelElRef.current = el;
    if (!el) return;
    const io = new IntersectionObserver(entries => {
      if (entries.some(e => e.isIntersecting)) setVisibleCount(c => c + PAGE_SIZE);
    }, { rootMargin: '150px 0px' });
    io.observe(el);
    ioRef.current = io;
  };
  useEffect(() => {
    const io = ioRef.current, el = sentinelElRef.current;
    if (io && el) { io.unobserve(el); io.observe(el); }
  }, [visibleCount]);

  useEffect(() => { fetchRatingsFor(visibleBuilds); }, [visibleBuilds]);
  useEffect(() => { if (sortBy === 'rating') fetchRatingsFor(builds); }, [sortBy, builds]);

  useEffect(() => {
    const handle = window.setTimeout(() => {
      void loadBuilds(searchValue.trim() || null);
    }, 220);
    return () => window.clearTimeout(handle);
  }, [searchValue]);

  const reduxById = useMemo(() => {
    const m = new Map<string, ReduxItem>();
    const source = allReduxes.length > 0 ? allReduxes : reduxList;
    for (const r of source) m.set(r.id, r);
    return m;
  }, [allReduxes, reduxList]);
  const packById = useMemo(() => {
    const m = new Map<string, Gunpack>();
    for (const p of publicPacks) m.set(p.id, p);
    return m;
  }, [publicPacks]);

  if (view.kind === 'create') {
    return (
      <CreateBuildScreen
        onCancel={() => setView({ kind: 'list' })}
        onSaved={(id) => {
          setHighlightId(id);
          setView({ kind: 'list' });
          window.setTimeout(() => setHighlightId(null), 2400);
        }}
      />
    );
  }
  if (view.kind === 'detail') {
    return <BuildDetailScreen buildId={view.buildId} onBack={() => setView({ kind: 'list' })} />;
  }

  const isEmpty = initialLoaded && builds.length === 0 && !loading && !searchValue.trim();

  return (
    <div className="h-full overflow-y-auto">
      <div className="max-w-[1280px] mx-auto px-12 pt-10 pb-16 min-h-full flex flex-col gap-6">
        <header className="flex items-end gap-4 flex-wrap">
          <div className="flex-1 min-w-0">
            <h1 className="text-[24px] font-semibold tracking-tight text-text-primary">
              {t('userBuilds.title', 'Пользовательские сборки')}
            </h1>
            <p className="mt-1 text-[13px] text-text-muted leading-relaxed max-w-[640px]">
              {t('userBuilds.subtitle', 'Сохранённые комбинации редукса, ганпака и выбранных пушек для установки в один клик.')}
            </p>
          </div>
          <button
            type="button"
            disabled={isGuest}
            onClick={() => { if (!isGuest) setView({ kind: 'create' }); }}
            title={isGuest ? t('userBuilds.createAuthRequired', 'Войдите, чтобы создать сборку') : undefined}
            className={
              'shrink-0 inline-flex items-center gap-2 h-11 px-5 rounded-xl ' +
              'text-[13px] font-bold uppercase tracking-wider transition-colors ' +
              (isGuest
                ? 'bg-white/[0.04] border border-white/[0.06] text-text-muted cursor-not-allowed'
                : 'bg-bg-elevated/55 text-text-primary border border-white/[0.08] hover:bg-bg-elevated/80 hover:border-white/[0.20]')
            }
            style={{ outline: 'none' }}
          >
            <Plus size={14} />
            {t('userBuilds.createButton', 'Создать сборку')}
          </button>
        </header>

        <div className="flex items-center gap-3 flex-wrap">
          <label
            className="group relative flex items-center flex-1 min-w-[220px] max-w-[340px] h-11 rounded-2xl
                       bg-white/[0.04] border border-white/[0.06]
                       transition-[background-color,border-color,box-shadow] duration-300 ease-smooth
                       hover:bg-white/[0.07] hover:border-white/[0.12]
                       focus-within:bg-white/[0.07] focus-within:border-white/[0.30]
                       focus-within:shadow-[0_0_0_3px_rgba(255,255,255,0.08)]"
          >
            <Search
              size={14}
              strokeWidth={2}
              className="absolute left-3.5 text-text-muted pointer-events-none
                         group-focus-within:text-text-primary transition-colors"
            />
            <input
              type="text"
              value={searchValue}
              onChange={(e) => setSearchValue(e.target.value)}
              placeholder={t('userBuilds.searchPlaceholder', 'Поиск по названию, автору или HNT-коду')}
              className="w-full h-full pl-10 pr-9 bg-transparent rounded-2xl
                         text-[13px] text-text-primary placeholder:text-text-muted outline-none"
            />
            {searchValue && (
              <button
                type="button"
                onClick={() => setSearchValue('')}
                className="absolute right-1.5 w-7 h-7 rounded-md flex items-center justify-center
                           text-text-muted hover:text-text-primary hover:bg-white/[0.10] transition-colors"
                aria-label={t('userBuilds.searchClear', 'Очистить поиск')}
                style={{ outline: 'none' }}
              >
                <X size={12} />
              </button>
            )}
          </label>

          {!isGuest && (
            <div className="inline-flex rounded-2xl border border-white/[0.07] bg-white/[0.03] p-1 shrink-0">
              <button
                type="button"
                onClick={() => setMineOnly(false)}
                style={{ outline: 'none' }}
                className={
                  'focus-glow inline-flex items-center h-9 px-3.5 rounded-xl text-[12px] font-bold uppercase tracking-[0.1em] transition-colors ' +
                  (!mineOnly ? 'bg-bg-elevated text-text-primary' : 'text-text-secondary hover:text-text-primary')
                }
              >
                {t('userBuilds.filterAll', 'Все')}
              </button>
              <button
                type="button"
                onClick={() => setMineOnly(true)}
                style={{ outline: 'none' }}
                className={
                  'focus-glow inline-flex items-center gap-1.5 h-9 px-3.5 rounded-xl text-[12px] font-bold uppercase tracking-[0.1em] transition-colors ' +
                  (mineOnly ? 'bg-bg-elevated text-text-primary' : 'text-text-secondary hover:text-text-primary')
                }
              >
                {t('userBuilds.filterMine', 'Мои сборки')}
                <span className="tabular-nums text-[10px] px-1.5 py-0.5 rounded bg-white/[0.08] text-text-muted">{mineCount}</span>
              </button>
            </div>
          )}

          <div className="flex items-center gap-2 ml-auto">
            <span
              aria-hidden
              className="hidden sm:block h-7 w-px self-center shrink-0
                         bg-gradient-to-b from-transparent via-white/15 to-transparent"
            />
            <div className="shrink-0">
              <GlassDropdown<BuildSort>
                value={sortBy}
                options={buildSortOptions}
                onChange={setSortBy}
                ariaLabel={t('redux.sort.label', 'Сортировка')}
                title={t('redux.sort.label', 'Сортировка')}
                width={210}
              />
            </div>
            <span className="inline-flex items-center gap-1.5 px-3.5 h-11 rounded-2xl
                             bg-white/[0.03] border border-white/[0.07]
                             text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted shrink-0">
              <span className="tabular-nums text-text-primary text-right inline-block min-w-[2.6ch]">
                {sortedBuilds.length}
              </span>
              <span>{t('userBuilds.countWord', { count: sortedBuilds.length })}</span>
            </span>
          </div>
        </div>

        {!initialLoaded ? (
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.35, ease: EASE_DEPTH }}
            className="py-20 flex flex-col items-center justify-center gap-4"
          >
            <div className="relative flex items-center justify-center">
              <motion.span
                aria-hidden
                className="absolute w-24 h-24 rounded-full blur-2xl pointer-events-none"
                style={{ background: 'radial-gradient(circle, var(--accent) 0%, transparent 70%)' }}
                initial={{ opacity: 0.18, scale: 0.9 }}
                animate={{ opacity: [0.18, 0.38, 0.18], scale: [0.9, 1.1, 0.9] }}
                transition={{ duration: 2.4, ease: 'easeInOut', repeat: Infinity }}
              />
              <AccentLoader size={34} className="relative" />
            </div>
            <span className="text-[11px] font-bold uppercase tracking-[0.22em] text-text-muted">
              {t('userBuilds.loading', 'Загружаю сборки...')}
            </span>
          </motion.div>
        ) : isEmpty ? (

          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.32, ease: EASE_DEPTH, delay: 0.04 }}
            className="flex-1 flex"
          >
            <Island className="flex-1 w-full px-10 py-12 flex flex-col items-center justify-center text-center gap-3">
              <span className="inline-flex items-center justify-center w-14 h-14 rounded-2xl text-accent"
                    style={{
                      background: 'color-mix(in srgb, var(--accent) 12%, transparent)',
                      boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 30%, transparent)',
                    }}>
                <Boxes size={24} />
              </span>
              <h2 className="text-[17px] font-bold text-text-primary tracking-tight mt-1">
                {t('userBuilds.emptyTitle', 'Пока нет сборок')}
              </h2>
              <p className="text-[13px] text-text-secondary leading-relaxed">
                {t(
                  'userBuilds.emptyHint',
                  'Соберите свою связку: выберите базу редукса, ганпак и нужные пушки - она сохранится здесь и будет доступна одним кликом.',
                )}
              </p>
              <button
                type="button"
                disabled={isGuest}
                onClick={() => { if (!isGuest) setView({ kind: 'create' }); }}
                title={isGuest ? t('userBuilds.createAuthRequired', 'Войдите, чтобы создать сборку') : undefined}
                style={{ outline: 'none' }}
                className={
                  'mt-2 inline-flex items-center gap-2 px-6 h-12 rounded-xl ' +
                  'text-[13px] font-bold uppercase tracking-wider transition-colors ' +
                  (isGuest
                    ? 'bg-white/[0.04] border border-white/[0.06] text-text-muted cursor-not-allowed'
                    : 'bg-bg-elevated/55 text-text-primary border border-white/[0.08] hover:bg-bg-elevated/80 hover:border-white/[0.20]')
                }
              >
                <Plus size={14} />
                {t('userBuilds.createButton', 'Создать сборку')}
              </button>
            </Island>
          </motion.div>
        ) : sortedBuilds.length === 0 ? (
          <div className="py-16 flex flex-col items-center justify-center text-center text-text-muted gap-2">
            <Boxes size={40} className="opacity-25" />
            <p className="text-sm">
              {searchValue.trim()
                ? t('catalog.noResults', 'Ничего не найдено по запросу.')
                : mineOnly
                  ? t('userBuilds.emptyMine', 'У тебя пока нет своих сборок. Нажми «Создать сборку».')
                  : t('userBuilds.emptyShort', 'Пока нет сборок.')}
            </p>
          </div>
        ) : (
          <div className="grid gap-3 grid-cols-1 md:grid-cols-2">
            <AnimatePresence>
              {visibleBuilds.map((b, idx) => {
                const live = reduxById.get(b.reduxId);
                const liveName = live?.name ?? b.reduxNameSnapshot;

                const cover = b.coverUrl || live?.previewUrl || null;
                const pack = packById.get(b.gunpackId);
                const packName = pack?.name ?? b.gunpackNameSnapshot;
                const isHighlighted = highlightId === b.id;
                const slotEntries = Object.entries(b.gunSlots ?? {});
                const overrides   = slotEntries.filter(([, v]) => v.kind === 'override').length;
                const vanillaCount = slotEntries.filter(([, v]) => v.kind === 'vanilla').length;
                const gunSummary =
                  slotEntries.length === 0
                    ? t('userBuilds.allGunsLabel', 'все пушки пака')
                    : [
                        overrides ? t('userBuilds.swapsCount', { count: overrides }) : null,
                        vanillaCount ? t('userBuilds.vanillaCount', { count: vanillaCount }) : null,
                      ].filter(Boolean).join(' · ');
                const armorSel = b.armor ?? null;
                let armorLabel = t('userBuilds.armorBase', 'броня базового редукса');
                if (armorSel?.kind === 'none') armorLabel = t('userBuilds.armorNone', 'без брони');
                else if (armorSel?.kind === 'override') {
                  const donor = reduxById.get(armorSel.reduxId);
                  armorLabel = donor
                    ? t('userBuilds.armorFromDonor', { name: donor.name, defaultValue: 'броня · {{name}}' })
                    : t('userBuilds.armorOtherRedux', 'броня из другого редукса');
                }

                return (
                  <motion.div
                    key={b.id}
                    layout
                    initial={{ opacity: 0, y: 16 }}
                    animate={{
                      opacity: 1,
                      y: 0,
                      transition: { duration: 0.45, ease: EASE_DEPTH, delay: Math.min((idx % PAGE_SIZE) * 0.05, 0.4) },
                    }}
                    exit={{ opacity: 0, scale: 0.98 }}
                    transition={{ duration: 0.3, ease: EASE_DEPTH }}
                    whileHover={{ y: -3 }}
                    onClick={() => setView({ kind: 'detail', buildId: b.id })}
                    role="button"
                    tabIndex={0}
                    onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') setView({ kind: 'detail', buildId: b.id }); }}

                    className="group relative h-[260px] rounded-2xl overflow-hidden cursor-pointer text-left
                               transition-[box-shadow,transform] duration-400 ease-depth hover:-translate-y-0.5"
                    style={{
                      borderRadius: 16,

                      boxShadow: isHighlighted
                        ? '0 0 0 2px color-mix(in srgb, var(--accent) 70%, transparent), 0 14px 32px color-mix(in srgb, var(--accent) 30%, transparent)'
                        : '0 0 0 1px color-mix(in srgb, var(--accent) 22%, transparent), '
                          + '0 0 0 6px color-mix(in srgb, var(--accent) 4%, transparent), '
                          + '0 22px 50px -20px color-mix(in srgb, var(--accent) 32%, transparent), '
                          + '0 8px 22px rgba(0,0,0,0.45)',
                    }}
                    onMouseEnter={e => {
                      if (isHighlighted) return;
                      (e.currentTarget as HTMLDivElement).style.boxShadow =
                        '0 0 0 1.5px color-mix(in srgb, var(--accent) 50%, transparent), '
                        + '0 0 0 8px color-mix(in srgb, var(--accent) 6%, transparent), '
                        + '0 28px 60px -20px color-mix(in srgb, var(--accent) 50%, transparent), '
                        + '0 12px 26px rgba(0,0,0,0.5)';
                    }}
                    onMouseLeave={e => {
                      if (isHighlighted) return;
                      (e.currentTarget as HTMLDivElement).style.boxShadow =
                        '0 0 0 1px color-mix(in srgb, var(--accent) 22%, transparent), '
                        + '0 0 0 6px color-mix(in srgb, var(--accent) 4%, transparent), '
                        + '0 22px 50px -20px color-mix(in srgb, var(--accent) 32%, transparent), '
                        + '0 8px 22px rgba(0,0,0,0.45)';
                    }}
                  >
                    {}
                    <div
                      className="absolute inset-0 transition-transform duration-700 ease-out group-hover:scale-[1.04]"
                      style={{
                        background: cover
                          ? `url(${cover}) center / cover no-repeat`
                          : 'linear-gradient(135deg, color-mix(in srgb, var(--accent) 28%, #0a0a14), #0a0a14)',
                      }}
                    />
                    {}
                    <span aria-hidden
                          className="pointer-events-none absolute -top-16 -right-12 w-56 h-56 rounded-full
                                     opacity-30 group-hover:opacity-55 blur-3xl
                                     transition-opacity duration-500 ease-smooth"
                          style={{ background: 'radial-gradient(circle, var(--accent), transparent 70%)' }} />
                    {}
                    <span aria-hidden
                          className="pointer-events-none absolute inset-x-0 bottom-0 h-36
                                     bg-gradient-to-t from-black/90 via-black/55 to-transparent" />

                    {}
                    {!isGuest && b.authorUserId === userId && (
                      <button
                        type="button"
                        onClick={(e) => { e.stopPropagation(); setDeleteCandidate(b.id); }}
                        className="absolute top-3 right-3 z-20 w-9 h-9 rounded-lg flex items-center justify-center
                                   text-white/70 hover:text-rose-300 transition-colors
                                   opacity-0 group-hover:opacity-100"
                        style={{
                          background: 'rgba(0,0,0,0.45)',
                          backdropFilter: 'blur(12px) saturate(140%)',
                          boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.08)',
                        }}
                        title={t('userBuilds.delete', 'Удалить сборку')}
                      >
                        <Trash2 size={14} />
                      </button>
                    )}

                    {}
                    <div className="absolute top-3 left-3 z-10 flex items-center gap-2">
                      <span className="inline-flex items-center gap-1.5 h-7 px-2.5 rounded-full
                                       text-[10.5px] font-bold uppercase tracking-wider text-white/85"
                            style={{
                              background: 'rgba(0,0,0,0.45)',
                              backdropFilter: 'blur(12px) saturate(140%)',
                              boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.08)',
                            }}>
                        <User size={11} strokeWidth={2.4} />
                        {b.author}
                      </span>
                      <span className="inline-flex items-center gap-1.5 h-7 px-2.5 rounded-full
                                       text-[10.5px] font-bold uppercase tracking-wider text-white/85"
                            style={{
                              background: 'rgba(0,0,0,0.45)',
                              backdropFilter: 'blur(12px) saturate(140%)',
                              boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.08)',
                            }}>
                        <Download size={11} strokeWidth={2.4} />
                        {b.downloadCount}
                      </span>
                      <span className="inline-flex items-center gap-1.5 h-7 px-2.5 rounded-full
                                       text-[10.5px] font-bold uppercase tracking-wider text-white/85"
                            style={{
                              background: 'rgba(0,0,0,0.45)',
                              backdropFilter: 'blur(12px) saturate(140%)',
                              boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.08)',
                            }}>
                        <Eye size={11} strokeWidth={2.4} />
                        {b.viewCount}
                      </span>
                      {(ratings[b.id]?.count ?? 0) > 0 && (
                        <span className="inline-flex items-center gap-1.5 h-7 px-2.5 rounded-full
                                         text-[10.5px] font-bold uppercase tracking-wider text-white/90"
                              style={{
                                background: 'rgba(0,0,0,0.45)',
                                backdropFilter: 'blur(12px) saturate(140%)',
                                boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.08)',
                              }}>
                          <Star size={11} strokeWidth={2.4} className="text-yellow-400 fill-current" />
                          {ratings[b.id].avg.toFixed(1)}
                        </span>
                      )}
                    </div>

                    {}
                    <div className="absolute bottom-0 inset-x-0 z-10 p-4 flex flex-col gap-1.5">
                      <h3 className="text-base font-bold text-white tracking-tight truncate
                                     drop-shadow-[0_2px_8px_rgba(0,0,0,0.7)]">
                        {b.name}
                      </h3>
                      <div className="flex flex-wrap gap-1.5">
                        <Chip icon={<Layers size={10} />} label={liveName} accent />
                        <Chip icon={<Crosshair size={10} />} label={`${packName} · ${gunSummary}`} accent />
                        <Chip icon={<Shield size={10} />} label={armorLabel} accent />
                      </div>
                    </div>
                  </motion.div>
                );
              })}
            </AnimatePresence>
          </div>
        )}

        {initialLoaded && hasMore && (
          <div ref={sentinelRef} className="py-6 flex items-center justify-center text-text-muted">
            <Loader2 size={16} className="animate-spin" />
          </div>
        )}
      </div>

      <ConfirmModal
        open={deleteCandidate !== null}
        title={t('userBuilds.deleteConfirmTitle', 'Удалить сборку?')}
        message={t('userBuilds.deleteConfirmMessage', {
          name: candidate?.name || candidate?.hntCode || '',
          defaultValue: '«{{name}}»\n\nЭто действие нельзя отменить - сборка пропадёт у всех, кто видит её через каталог по HNT-коду.',
        })}
        confirmLabel={deleting ? t('userBuilds.deleting', 'Удаляем…') : t('common.delete', 'Удалить')}
        cancelLabel={t('common.cancel', 'Отмена')}
        destructive
        onConfirm={() => { void onConfirmDelete(); }}
        onCancel={() => { if (!deleting) setDeleteCandidate(null); }}
      />
    </div>
  );
}

function Chip({ icon, label, accent }: { icon: React.ReactNode; label: string; accent?: boolean }) {
  return (
    <span
      className="inline-flex items-center gap-1.5 h-6 px-2 rounded-md
                 text-[10.5px] font-semibold text-white/90 truncate max-w-full"
      style={{
        background: 'rgba(0,0,0,0.45)',
        backdropFilter: 'blur(12px) saturate(140%)',
        boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.08)',
      }}
    >
      <span className={accent ? 'text-accent' : 'text-white/70'}>{icon}</span>
      <span className="truncate">{label}</span>
    </span>
  );
}

function Island({ className = '', children }: { className?: string; children: React.ReactNode }) {
  return (
    <GlassPanel
      depth="z3" tint="ultra" rounded="3xl" highlight edge
      className="relative overflow-hidden border border-white/[0.08] h-full w-full"
    >
      <span aria-hidden className="absolute inset-0 pointer-events-none bg-bg-elevated/55" />
      <span
        aria-hidden
        className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                   bg-gradient-to-r from-transparent via-white/40 to-transparent"
      />
      <span
        aria-hidden
        className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
        style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
      />
      <div className={'relative h-full ' + className}>{children}</div>
    </GlassPanel>
  );
}
