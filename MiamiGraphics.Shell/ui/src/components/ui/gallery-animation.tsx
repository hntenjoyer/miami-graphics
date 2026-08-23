import { useState } from 'react';
import { motion } from 'framer-motion';
import { ArrowUpRight, type LucideIcon } from 'lucide-react';
import { EASE_DEPTH } from '@/design';

export interface GallerySlide {
  key: string;
  title: string;
  subtitle?: string;
  image?: string | null;
  icon?: LucideIcon;
}

interface ExpandableGalleryProps {
  slides: GallerySlide[];
  onSelect: (key: string) => void;
  actionLabel?: string;
  minHeightClass?: string;
  className?: string;
}

export function ExpandableGallery({
  slides,
  onSelect,
  actionLabel,
  minHeightClass = 'min-h-[360px]',
  className = '',
}: ExpandableGalleryProps) {
  const [hovered, setHovered] = useState<number | null>(null);
  const spread = slides.length >= 4 ? { on: 1.8, off: 0.8 } : { on: 2.2, off: 0.7 };
  const flexFor = (i: number) =>
    hovered === null ? 1 : hovered === i ? spread.on : spread.off;

  return (
    <div className={`flex gap-3 w-full h-full ${minHeightClass} ${className}`}>
      {slides.map((s, i) => {
        const Icon = s.icon;
        const active = hovered === i;
        return (
          <motion.button
            type="button"
            key={s.key}
            onMouseEnter={() => setHovered(i)}
            onMouseLeave={() => setHovered(null)}
            onClick={() => onSelect(s.key)}
            className="group relative overflow-hidden rounded-3xl text-left min-w-0
                       border border-white/[0.06] bg-bg-elevated shadow-z2
                       transform-gpu
                       hover:border-[color-mix(in_srgb,var(--accent)_55%,transparent)]
                       hover:shadow-glow-accent
                       focus-visible:outline-none
                       focus-visible:border-[color-mix(in_srgb,var(--accent)_55%,transparent)]
                       focus-visible:shadow-glow-accent"
            style={{ flex: 1, willChange: 'flex' }}
            animate={{ flex: flexFor(i) }}
            transition={{ duration: 0.5, ease: EASE_DEPTH }}
          >
            {s.image ? (
              <img
                src={s.image}
                alt=""
                aria-hidden="true"
                draggable={false}
                className="absolute inset-0 w-full h-full object-cover
                           transition-transform duration-[1100ms] ease-smooth
                           group-hover:scale-[1.04]"
              />
            ) : (
              <div className="absolute inset-0 bg-gradient-to-br from-white/[0.05] via-transparent to-black/40" />
            )}

            {!s.image && Icon && (
              <Icon
                size={160}
                strokeWidth={1}
                className="absolute -right-4 -bottom-4 text-white/[0.06]
                           transition-[transform,color] duration-700
                           group-hover:scale-110 group-hover:text-white/[0.10]"
              />
            )}

            <div
              className="absolute inset-0 bg-black transition-opacity duration-300"
              style={{ opacity: active ? 0.05 : 0.3 }}
            />
            <div className="absolute inset-x-0 bottom-0 h-1/2 bg-gradient-to-t from-black/80 via-black/25 to-transparent pointer-events-none" />

            <div className="absolute inset-0 p-6 flex flex-col justify-end">
              <div
                className="flex items-center gap-2 mb-2 text-accent whitespace-nowrap
                           opacity-0 -translate-y-1
                           group-hover:opacity-100 group-hover:translate-y-0
                           transition-[opacity,transform] duration-300"
              >
                {Icon && <Icon size={16} strokeWidth={2} className="shrink-0" />}
                {actionLabel && (
                  <span className="text-[10px] font-bold uppercase tracking-[0.22em]">
                    {actionLabel}
                  </span>
                )}
                <ArrowUpRight size={14} strokeWidth={2.4} className="shrink-0" />
              </div>
              <div
                className="font-display font-bold text-white uppercase tracking-wide truncate
                           text-xl drop-shadow-[0_2px_10px_rgba(0,0,0,0.6)]"
              >
                {s.title}
              </div>
              {s.subtitle && (
                <div
                  className="mt-1 text-[12px] text-white/70 max-w-full truncate
                             opacity-0 group-hover:opacity-100 transition-opacity duration-300"
                >
                  {s.subtitle}
                </div>
              )}
            </div>
          </motion.button>
        );
      })}
    </div>
  );
}
