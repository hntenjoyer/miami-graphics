import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Crosshair } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { GlbViewerModal } from './GlbViewerModal';
import { CarbonSurface } from '@/design';
import { LazyImage } from '@/components/LazyImage';
import { prefetchGlb } from '@/utils/glbPrefetch';

const CATEGORY_LABEL: Record<string, { key: string; ru: string }> = {
  assault: { key: 'guns.category.assault', ru: 'Штурмовая' },
  shotgun: { key: 'guns.category.shotgun', ru: 'Дробовик' },
  sniper:  { key: 'guns.category.sniper',  ru: 'Снайперская' },
  mg:      { key: 'guns.category.mg',      ru: 'Пулемёт' },
  pistol:  { key: 'guns.category.pistol',  ru: 'Пистолет' },
  smg:     { key: 'guns.category.smg',     ru: 'ПП' },
};

interface Props {
  baseName:    string;
  displayName: string | null;
  category:    string;
  weaponPrefix: string;
  glbUrl:      string | null;
  previewUrl:  string | null;
  packLabel?:  string;
  index?: number;
}

export function GunCard({ baseName, displayName, category, weaponPrefix: _weaponPrefix, glbUrl, previewUrl, packLabel, index }: Props) {
  const { t } = useTranslation();
  const [viewerOpen, setViewerOpen] = useState(false);
  const label = displayName ?? baseName;
  const categoryEntry = CATEGORY_LABEL[category];
  const categoryLabel = categoryEntry ? t(categoryEntry.key, categoryEntry.ru) : category;
  const eagerImage = index === undefined || index < 16;

  return (
    <>
      <motion.button
        type="button"
        onClick={() => setViewerOpen(true)}
        onPointerEnter={() => prefetchGlb(glbUrl)}
        onFocus={() => prefetchGlb(glbUrl)}
        title={t('guns.card.view3dTitle', '3D-модель')}
        aria-label={t('guns.card.view3dAria', 'Показать 3D-модель')}
        whileHover={{ y: -4 }}
        whileTap={{ scale: 0.985 }}
        transition={{ type: 'spring', stiffness: 320, damping: 22 }}
        style={{ outline: 'none' }}
        className="group relative aspect-[4/5] w-full text-left cursor-pointer rounded-2xl overflow-hidden
                   bg-glass-strong backdrop-blur-glass
                   border border-glass-border
                   transition-[border-color,box-shadow] duration-500 ease-smooth
                   hover:border-[color-mix(in_srgb,var(--accent)_70%,transparent)]
                   hover:shadow-[0_22px_56px_-16px_color-mix(in_srgb,var(--accent)_45%,transparent)]"
      >
        {}
        <div
          aria-hidden
          className="absolute inset-x-0 top-0 bottom-[34%] overflow-hidden pointer-events-none"
        >
          <CarbonSurface
            weaveOpacity={0.18}
            glowIntensity={0.7}
            vignetteIntensity={0.35}
          />
        </div>

        {}
        <div
          aria-hidden
          className="absolute inset-x-2 top-2 h-[58%] rounded-xl pointer-events-none
                     opacity-60 group-hover:opacity-100
                     transition-opacity duration-700 ease-smooth"
          style={{
            background:
              'radial-gradient(ellipse at 50% 60%, rgba(255,255,255,0.22), transparent 70%)',
            filter: 'blur(16px)',
          }}
        />

        {}
        <div
          aria-hidden
          className="absolute inset-x-0 top-0 h-1/2 pointer-events-none
                     bg-gradient-to-b from-white/[0.04] to-transparent"
        />

        {}
        {}
        <div className="absolute inset-x-0 top-0 bottom-[34%] flex items-center justify-center px-2">
          {previewUrl ? (
            <LazyImage
              src={previewUrl}
              eager={eagerImage}
              alt=""
              draggable={false}
              className="relative w-full h-full object-contain select-none
                         drop-shadow-[0_8px_18px_rgba(0,0,0,0.45)]
                         transition-transform duration-700 ease-smooth
                         group-hover:scale-[1.07] group-hover:-translate-y-0.5"
              style={{ transformOrigin: '50% 50%', objectPosition: '50% 50%' }}
              onError={e => (e.currentTarget.style.display = 'none')}
            />
          ) : (
            <Crosshair size={44} className="text-text-muted opacity-40" />
          )}
        </div>

        {}
        <div
          aria-hidden
          style={{ height: '32%' }}
          className="absolute inset-x-0 bottom-0 pointer-events-none
                     bg-gradient-to-t from-black/85 via-black/40 to-transparent"
        />
        <div className="absolute inset-x-0 bottom-0 px-3 pb-3 pt-2 flex flex-col gap-1.5">
          <div className="font-display font-bold text-[13px] uppercase tracking-[0.04em] text-white truncate leading-tight" title={label}>
            {label}
          </div>
          <div className="flex items-center gap-1.5 text-[10px]">
            {}
            <span className="inline-flex items-center px-1.5 py-0.5 rounded-md uppercase tracking-[0.12em]
                             bg-[color-mix(in_srgb,var(--accent)_22%,transparent)]
                             border border-[color-mix(in_srgb,var(--accent)_28%,transparent)]
                             text-[9px] font-bold text-text-primary">
              {categoryLabel}
            </span>
            {packLabel && (
              <span className="text-white/55 truncate uppercase tracking-wider">
                {packLabel}
              </span>
            )}
          </div>
        </div>

        <div
          aria-hidden
          className="absolute top-2 right-2 inline-flex items-center justify-center gap-1
                     h-7 px-1.5 rounded-lg pointer-events-none
                     bg-black/55 backdrop-blur-md text-white
                     opacity-85 group-hover:opacity-100
                     group-hover:bg-[color-mix(in_srgb,var(--accent)_25%,rgba(0,0,0,0.55))]
                     group-hover:scale-[1.06]
                     transition-[opacity,background-color,transform] duration-200 ease-depth"
        >
          <Box size={12} />
          <span className="text-[9px] font-bold uppercase tracking-[0.18em] hidden group-hover:inline">3D</span>
        </div>
      </motion.button>

      {}
      <AnimatePresence>
        {viewerOpen && (
          <GlbViewerModal
            glbUrl={glbUrl}
            title={label}
            onClose={() => setViewerOpen(false)}
          />
        )}
      </AnimatePresence>
    </>
  );
}
