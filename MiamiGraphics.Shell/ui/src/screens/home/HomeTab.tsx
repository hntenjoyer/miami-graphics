import { useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { AnimatePresence, motion } from 'framer-motion';
import { ArrowUpRight, type LucideIcon } from 'lucide-react';
import { HOME_CATEGORIES } from '@/data/homeCategories';
import { useReduxStore } from '@/store/reduxStore';
import { useFeaturedStore } from '@/store/featuredStore';
import type { ReduxItem } from '@/bridge/types';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { ReduxCard } from '@/screens/redux/ReduxCard';

interface HomeTabProps {
  onNavigateToCategory?: (id: string) => void;
  onOpenMod?: (modId: string) => void;
}

const HOME_TAB_CATEGORY_IDS = ['redux', 'guns', 'settings', 'players'] as const;

export function HomeTab({ onNavigateToCategory, onOpenMod }: HomeTabProps) {
  const { t } = useTranslation();

  const categories = HOME_TAB_CATEGORY_IDS
    .map(id => HOME_CATEGORIES.find(c => c.id === id))
    .filter((c): c is typeof HOME_CATEGORIES[number] => Boolean(c));

  const items = useReduxStore(s => s.items);
  const loading = useReduxStore(s => s.loading);
  const load = useReduxStore(s => s.load);
  useEffect(() => { void load(); }, [load]);

  const featuredPicks       = useFeaturedStore(s => s.picks);
  const featuredInitialized = useFeaturedStore(s => s.initialized);
  const loadFeatured        = useFeaturedStore(s => s.load);
  useEffect(() => { void loadFeatured(); }, [loadFeatured]);

  const pool = useMemo<ReduxItem[]>(() => {
    if (items.length === 0) return [];

    if (featuredPicks.length > 0) {
      const sorted = [...featuredPicks].sort((a, b) => a.slotIndex - b.slotIndex);
      const resolved: ReduxItem[] = [];
      for (const pick of sorted) {
        const item = items.find(i => i.id === pick.reduxId);
        if (item && item.status !== 'hidden') resolved.push(item);
      }
      if (resolved.length > 0) return resolved;
    }

    const sorted = [...items].sort((a, b) => {
      if (a.viewerPriority !== b.viewerPriority) return b.viewerPriority - a.viewerPriority;
      return new Date(b.uploadedAt).getTime() - new Date(a.uploadedAt).getTime();
    });
    return sorted.slice(0, 9);
  }, [items, featuredPicks]);

  return (

    <div className="h-full flex flex-col">
      <div className="flex-1 min-h-0 overflow-hidden px-5 pt-5 pb-5 flex flex-col gap-5">

        {}
        <section className="flex-1 min-h-0 flex flex-col">
          <h2 className="text-sm font-bold text-text-primary mb-2 tracking-tight uppercase shrink-0">
            {t('home.popularThisMonth')}
          </h2>
          <div className="flex-1 min-h-0">
            <FeaturedCarousel
              pool={pool}
              loading={(loading && items.length === 0) || !featuredInitialized}
              onOpen={onOpenMod}
            />
          </div>
        </section>

        {}
        <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-5 shrink-0">
          {categories.map((cat, idx) => (
            <CategoryTile
              key={cat.id}
              index={idx}
              icon={cat.icon}
              label={t(cat.labelKey)}
              onClick={() => onNavigateToCategory?.(cat.navTarget ?? cat.id)}
            />
          ))}
        </div>
      </div>

    </div>
  );
}

interface FeaturedCarouselProps {
  pool:    ReduxItem[];
  loading: boolean;
  onOpen?: (id: string) => void;
}

function FeaturedCarousel({ pool, loading, onOpen }: FeaturedCarouselProps) {
  const { t } = useTranslation();

  const slots = useMemo(() => ({
    main:   pool[0] ?? null,
    top:    pool[1] ?? null,
    bottom: pool[2] ?? null,
  }), [pool]);

  if (loading) {
    return (
      <div className="grid grid-cols-3 grid-rows-2 gap-5 h-full min-h-[24rem]">
        <GlassPanel depth="z2" tint="soft" rounded="2xl"
                    className="col-span-2 row-span-2 animate-pulse" />
        <GlassPanel depth="z2" tint="soft" rounded="2xl"
                    className="col-start-3 row-start-1 animate-pulse" />
        <GlassPanel depth="z2" tint="soft" rounded="2xl"
                    className="col-start-3 row-start-2 animate-pulse" />
      </div>
    );
  }
  if (pool.length === 0) {
    return (
      <GlassPanel depth="z2" tint="soft" rounded="2xl"
                  className="h-full min-h-[24rem] flex items-center justify-center">
        <p className="text-sm text-text-muted">{t('redux.emptyCatalog')}</p>
      </GlassPanel>
    );
  }

  return (
    <div className="grid grid-cols-3 grid-rows-2 gap-5 h-full min-h-[24rem]">
      <div className="col-span-2 row-span-2 min-h-0">
        <FeaturedSlot item={slots.main} onOpen={onOpen} />
      </div>
      <div className="col-start-3 row-start-1 min-h-0">
        <FeaturedSlot item={slots.top} onOpen={onOpen} />
      </div>
      <div className="col-start-3 row-start-2 min-h-0">
        <FeaturedSlot item={slots.bottom} onOpen={onOpen} />
      </div>
    </div>
  );
}

interface FeaturedSlotProps {
  item:   ReduxItem | null;
  onOpen?: (id: string) => void;
}

function FeaturedSlot({ item, onOpen }: FeaturedSlotProps) {
  return (

    <AnimatePresence mode="wait">
      {item ? (
        <motion.div
          key={item.id}
          initial={{ opacity: 0, scale: 0.99 }}
          animate={{ opacity: 1, scale: 1    }}
          exit   ={{ opacity: 0, scale: 1.01 }}
          transition={{ duration: 0.45, ease: EASE_DEPTH }}
          className="h-full w-full"
        >
          <ReduxCard
            item={item}
            size="fill"
            onClick={() => onOpen?.(item.id)}
          />
        </motion.div>
      ) : (
        <motion.div
          key="placeholder"
          initial={{ opacity: 0 }}
          animate={{ opacity: 0.4 }}
          exit   ={{ opacity: 0 }}
          transition={{ duration: 0.3, ease: EASE_DEPTH }}
          className="h-full w-full"
        >
          <GlassPanel depth="z2" tint="soft" rounded="2xl" className="h-full" />
        </motion.div>
      )}
    </AnimatePresence>
  );
}

interface CategoryTileProps {
  index: number;
  icon: LucideIcon;
  label: string;
  onClick: () => void;
}

function CategoryTile({ index, icon: Icon, label, onClick }: CategoryTileProps) {

  return (
    <motion.button
      type="button"
      onClick={onClick}

      initial={{ opacity: 0, y: 22, scale: 0.97 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ duration: 0.55, ease: EASE_DEPTH, delay: 0.12 + index * 0.08 }}
      whileHover={{ y: -5, transition: { duration: 0.22, ease: EASE_DEPTH, delay: 0 } }}
      whileTap={{ scale: 0.985, transition: { duration: 0.12, ease: EASE_DEPTH } }}
      className="group relative w-full min-h-[12rem] text-left rounded-3xl overflow-hidden
                 isolation-isolate transform-gpu will-change-transform
                 bg-glass-strong backdrop-blur-glass-heavy
                 border border-white/[0.06]
                 shadow-z2
                 transition-[border-color,box-shadow] duration-300 ease-smooth
                 hover:border-white/[0.16]
                 focus-visible:outline-none focus-visible:border-white/[0.30]"
    >
      <span
        aria-hidden
        className="absolute inset-x-0 top-0 h-px pointer-events-none
                   bg-gradient-to-r from-transparent via-white/35 to-transparent"
      />

      <div className="relative h-full flex flex-col justify-between p-6">
        <div className="flex items-start justify-between">
          <div
            className="relative w-14 h-14 rounded-2xl overflow-hidden
                       flex items-center justify-center
                       bg-accent-soft border border-white/[0.10]
                       shadow-[inset_0_1px_0_rgba(255,255,255,0.18)]
                       text-accent"
          >
            <Icon size={26} strokeWidth={1.7} className="relative" />
          </div>

          <span
            aria-hidden
            className="w-10 h-10 rounded-full flex items-center justify-center shrink-0
                       bg-white/[0.04] border border-white/[0.06] text-text-secondary"
          >
            <ArrowUpRight size={16} strokeWidth={2} />
          </span>
        </div>

        <h3
          style={{ fontFamily: "'Inter', system-ui, -apple-system, sans-serif" }}
          className="font-bold text-text-primary
                     text-[22px] leading-[1.05] tracking-tight uppercase max-w-[14ch]">
          {label}
        </h3>
      </div>
    </motion.button>
  );
}
