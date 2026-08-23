import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Map as MapIcon, LayoutGrid, Sparkles,
  Download as DownloadIcon, Star as StarIcon,
} from 'lucide-react';
import { clsx } from 'clsx';
import { ScreenHero } from '@/screens/ScreenHero';
import { GlassDropdown, type GlassDropdownOption } from '@/design';
import { useBigMapStore, type BigMapSort } from '@/store/bigMapStore';
import type { BigMap } from '@/bridge/types';
import { BigMapCard } from './BigMapCard';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';

interface Props {
  onOpenMap: (id: string) => void;
}

export function BigMapBrowse({ onOpenMap }: Props) {
  const { t } = useTranslation();

  const list         = useBigMapStore(s => s.list);
  const loading      = useBigMapStore(s => s.loadingList);
  const loadList     = useBigMapStore(s => s.loadList);
  const state        = useBigMapStore(s => s.state);
  const refreshState = useBigMapStore(s => s.refreshState);
  const filter       = useBigMapStore(s => s.serverFilter);
  const setFilter    = useBigMapStore(s => s.setServerFilter);
  const sort         = useBigMapStore(s => s.sort);
  const setSort      = useBigMapStore(s => s.setSort);
  const ratings      = useBigMapStore(s => s.ratings);
  const loadRatings  = useBigMapStore(s => s.loadRatings);

  const [search, setSearch] = useState('');

  useEffect(() => {
    void loadList();
    void refreshState();
    void loadRatings();
  }, [loadList, refreshState, loadRatings]);

  const filtered = useMemo(() => {
    let result = filter === 'all' ? list : list.filter(m => m.supportedServers.includes(filter));
    const q = search.trim().toLowerCase();
    if (q) {
      result = result.filter(m =>
        m.name.toLowerCase().includes(q) || m.author.toLowerCase().includes(q));
    }
    return sortMaps(result, sort, ratings);
  }, [list, filter, search, sort, ratings]);

  return (
    <div className="h-full overflow-y-auto">
      <div className="max-w-7xl 2xl:max-w-[1700px] mx-auto px-5 pt-3 pb-6 flex flex-col gap-3">
        <ScreenHero
          title={t('bigmap.heroTitle', 'БОЛЬШАЯ КАРТА')}
          subtitle={t('bigmap.heroSubtitle', 'Каталог больших векторных карт. Жми «Установить» - мы заменим карту в игре, вернуть стандартную можно в один клик.')}
          search={{
            value: search,
            onChange: setSearch,
            placeholder: t('bigmap.searchPlaceholder', 'Поиск карты...'),
            clearLabel: t('bigmap.searchClear', 'Очистить'),
          }}
          trailing={
            <>
              <FilterPill
                active={filter === 'all'}
                label={t('bigmap.filterAll', 'Все')}
                icon={<LayoutGrid size={14} strokeWidth={2} />}
                onClick={() => setFilter('all')}
              />
              <FilterPill
                active={filter === 'gta5rp'}
                label="5RP"
                logo={gta5rpLogo}
                onClick={() => setFilter(filter === 'gta5rp' ? 'all' : 'gta5rp')}
              />
              <FilterPill
                active={filter === 'majestic'}
                label="Majestic"
                logo={majesticLogo}
                onClick={() => setFilter(filter === 'majestic' ? 'all' : 'majestic')}
              />
              <span
                aria-hidden
                className="hidden sm:block h-7 w-px self-center
                           bg-gradient-to-b from-transparent via-white/15 to-transparent"
              />
              <SortSelect value={sort} onChange={setSort} />
              <span className="inline-flex items-center gap-1.5 px-3 h-9 rounded-xl
                               bg-white/[0.04] border border-white/[0.06]
                               text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted shrink-0">
                <span className="tabular-nums text-text-primary">{filtered.length}</span>
                <span>{t('bigmap.countLabel', 'карт')}</span>
              </span>
            </>
          }
        />

        {}
        {loading && list.length === 0 ? (
          <div className="py-16 flex items-center justify-center text-text-muted gap-2">
            <MapIcon size={16} className="opacity-50" />
            <span className="text-sm">{t('bigmap.loading', 'Загружаем каталог…')}</span>
          </div>
        ) : filtered.length === 0 ? (
          <div className="py-20 flex flex-col items-center justify-center text-text-muted gap-2">
            <MapIcon size={48} className="opacity-30" />
            <p className="text-sm">
              {list.length === 0
                ? t('bigmap.emptyCatalog', 'Пока нет ни одной опубликованной карты.')
                : t('bigmap.noMatch', 'Под выбранный сервер карт не нашлось.')}
            </p>
          </div>
        ) : (
          <motion.div layout className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-6">
            <AnimatePresence mode="popLayout">
              {filtered.map((m, i) => (
                <motion.div
                  key={m.id}
                  layout
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{
                    duration: 0.32,
                    ease: [0.22, 1, 0.36, 1],
                    delay: Math.min(i, 16) * 0.04,
                  }}
                >
                  <BigMapCard
                    map={m}
                    index={i}
                    isInstalled={state.enabled && state.id === m.id}
                    onClick={() => onOpenMap(m.id)}
                  />
                </motion.div>
              ))}
            </AnimatePresence>
          </motion.div>
        )}
      </div>
    </div>
  );
}

function SortSelect({ value, onChange }: { value: BigMapSort; onChange: (v: BigMapSort) => void }) {
  const { t } = useTranslation();

  const options: GlassDropdownOption<BigMapSort>[] = [
    { value: 'default',   label: t('redux.sort.default', 'По умолчанию'),   icon: Sparkles },
    { value: 'downloads', label: t('redux.sort.downloads', 'По скачиваниям'), icon: DownloadIcon },
    { value: 'rating',    label: t('redux.sort.rating', 'По рейтингу'),       icon: StarIcon },
  ];
  return (
    <GlassDropdown<BigMapSort>
      value={value}
      options={options}
      onChange={onChange}
      ariaLabel={t('redux.sort.label', 'Сортировка')}
      title={t('redux.sort.label', 'Сортировка')}
      width={210}
    />
  );
}

function sortMaps(
  list: BigMap[],
  sort: BigMapSort,
  ratings: Record<string, { avg: number; count: number }>,
): BigMap[] {
  if (sort === 'default') return list;
  const out = [...list];
  if (sort === 'downloads') {
    out.sort((a, b) => b.downloadCount - a.downloadCount);
  } else if (sort === 'rating') {
    out.sort((a, b) => {
      const ra = ratings[a.id];
      const rb = ratings[b.id];
      if (ra && rb) return rb.avg - ra.avg || rb.count - ra.count;
      if (ra) return -1;
      if (rb) return 1;
      return b.downloadCount - a.downloadCount;
    });
  }
  return out;
}

function FilterPill({ active, label, logo, icon, onClick }: {
  active: boolean; label: string; logo?: string; icon?: React.ReactNode; onClick: () => void;
}) {
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
          ? 'bg-white text-black border-white shadow-pill-active'
          : 'bg-white/[0.04] text-text-secondary border-white/[0.06] ' +
            'hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]',
      )}
    >
      <span
        className={clsx(
          'relative shrink-0 w-6 h-6 rounded-md flex items-center justify-center',
          active ? 'bg-black/[0.06]' : 'bg-white/[0.06]',
        )}
      >
        {logo ? <img src={logo} alt="" className="w-4 h-4 object-contain" /> : icon}
      </span>
      <span>{label}</span>
    </button>
  );
}
