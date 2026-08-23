import { useTranslation } from 'react-i18next';
import { Map as MapIcon, CheckCircle2, Download, HardDrive, Star } from 'lucide-react';
import type { BigMap } from '@/bridge/types';
import { useBigMapStore } from '@/store/bigMapStore';
import { LazyImage } from '@/components/LazyImage';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';
import { bigMapVectorPreview } from './bigmapPreviews';

interface Props {
  map: BigMap;
  isInstalled: boolean;
  onClick: () => void;
  index?: number;
}

export function BigMapCard({ map, isInstalled, onClick, index }: Props) {
  const { t } = useTranslation();
  const sizeM = (map.sizeBytes / (1024 * 1024)).toFixed(0);
  const previewSrc = bigMapVectorPreview(map.id) ?? map.previewUrl;
  const hasImage = !!previewSrc;
  const eagerImage = index === undefined || index < 18;

  const aggregate = useBigMapStore(s => s.ratings[map.id]);
  const rating = aggregate?.avg ?? 0;
  const ratingCount = aggregate?.count ?? 0;
  const hasRating = ratingCount > 0;

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onClick}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onClick(); } }}
      className={
        'group relative h-56 w-full rounded-2xl overflow-hidden cursor-pointer ' +
        'bg-bg-elevated text-left transform-gpu will-change-transform ' +
        'transition-[transform,box-shadow,border-color] duration-[500ms] ease-smooth ' +
        'shadow-z2 hover:shadow-glow-accent ' +
        'hover:-translate-y-1 ' +
        'focus-visible:outline-none focus-visible:shadow-glow-accent focus-visible:border-accent/60 ' +
        (isInstalled
          ? 'border border-[color-mix(in_srgb,var(--accent)_45%,transparent)] hover:border-accent/70'
          : 'border border-transparent hover:border-accent/60')
      }
    >
      {}
      {hasImage ? (
        <LazyImage
          src={previewSrc}
          eager={eagerImage}
          alt=""
          aria-hidden="true"
          draggable={false}
          className="absolute inset-0 w-full h-full object-cover object-center select-none
                     transform-gpu transition-transform duration-[1100ms] ease-smooth
                     group-hover:scale-[1.045]"
          style={{ willChange: 'transform', backfaceVisibility: 'hidden' }}
          onError={e => (e.currentTarget.style.display = 'none')}
        />
      ) : (
        <div className="absolute inset-0 flex items-center justify-center
                        bg-gradient-to-br from-bg-elevated via-bg-base to-bg-elevated
                        text-text-muted">
          <MapIcon size={56} className="opacity-25" />
        </div>
      )}

      {}
      <div
        style={{ height: '32%' }}
        className="absolute inset-x-0 bottom-0 pointer-events-none
                   bg-gradient-to-t from-black/85 via-black/40 to-transparent"
      />

      {}
      <div className="absolute top-3 right-3 z-10 flex items-center gap-2">
        {map.isVerified && (
          <span className="px-1.5 py-1 rounded-md bg-accent/85 backdrop-blur-md flex items-center" title="Verified">
            <CheckCircle2 size={13} className="text-white" />
          </span>
        )}
        <span className="px-1.5 py-1 rounded-md bg-black/70 backdrop-blur-md text-xs text-white font-bold tabular-nums">
          {sizeM} MB
        </span>
      </div>

      {}
      <div className="absolute top-3 left-3 z-10 flex items-center gap-1.5">
        {isInstalled && (
          <span className="inline-flex items-center gap-1 px-2 py-1 rounded-md
                           bg-[color-mix(in_srgb,var(--accent)_28%,rgba(0,0,0,0.4))]
                           border border-[color-mix(in_srgb,var(--accent)_40%,transparent)]
                           backdrop-blur-md
                           text-[9px] font-bold uppercase tracking-[0.14em] text-white">
            <CheckCircle2 size={10} className="text-accent" />
            {t('bigmap.badgeInstalled', 'Установлена')}
          </span>
        )}
        {map.supportedServers.includes('gta5rp') && (
          <span className="w-[26px] h-[26px] rounded-md flex items-center justify-center
                           bg-black/70 backdrop-blur-md" title="GTA5RP">
            <img src={gta5rpLogo} alt="GTA5RP" className="w-4 h-4 object-contain" />
          </span>
        )}
        {map.supportedServers.includes('majestic') && (
          <span className="w-[26px] h-[26px] rounded-md flex items-center justify-center
                           bg-black/70 backdrop-blur-md" title="Majestic">
            <img src={majesticLogo} alt="Majestic" className="w-4 h-4 object-contain" />
          </span>
        )}
      </div>

      {}
      <div className="absolute bottom-0 left-0 right-0 px-4 pb-3.5 z-10 flex flex-col">
        <div className="font-display font-bold text-white text-lg uppercase tracking-wide truncate leading-tight
                        [text-shadow:0_2px_4px_rgba(0,0,0,0.95),0_0_18px_rgba(0,0,0,0.6)]">
          {map.name}
        </div>

        <div className="grid grid-rows-[0fr] group-hover:grid-rows-[1fr]
                        group-focus-visible:grid-rows-[1fr]
                        transition-[grid-template-rows] duration-[450ms] ease-smooth">
          <div className="overflow-hidden min-h-0">
            <div className="pt-1.5 flex flex-col gap-1
                            opacity-0 translate-y-1.5
                            group-hover:opacity-100 group-hover:translate-y-0
                            group-focus-visible:opacity-100 group-focus-visible:translate-y-0
                            transition-[opacity,transform] duration-[350ms] delay-[100ms] ease-smooth">
              {map.author && (
                <div className="text-xs text-white/75 truncate">
                  by <span className="text-white/85">{map.author}</span>
                </div>
              )}
              <div className="flex items-center gap-3 text-xs font-bold tabular-nums text-white/90">
                {hasRating ? (
                  <span className="inline-flex items-center gap-1 text-yellow-400" title={t('bigmap.ratingTitle', 'Средняя оценка')}>
                    <Star size={12} className="fill-current" />
                    {rating.toFixed(1)}
                    <span className="text-white/55 font-semibold">({ratingCount})</span>
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 text-white/45" title={t('bigmap.noRatingTitle', 'Оценок пока нет')}>
                    <Star size={12} />
                    -
                  </span>
                )}
                <span className="inline-flex items-center gap-1.5 text-accent">
                  <Download size={12} />
                  {formatDownloads(map.downloadCount)}
                </span>
                <span className="inline-flex items-center gap-1.5 text-white/75">
                  <HardDrive size={12} />
                  {sizeM} MB
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function formatDownloads(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
  if (n >= 1_000)     return (n / 1_000).toFixed(1) + 'k';
  return String(n);
}
