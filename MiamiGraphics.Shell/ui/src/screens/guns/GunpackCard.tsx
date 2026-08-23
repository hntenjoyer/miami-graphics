import { Crosshair, CheckCircle2, Download, HardDrive, Star } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { Gunpack } from '@/bridge/types';
import { useGunpackStore } from '@/store/gunpackStore';
import { LazyImage } from '@/components/LazyImage';

interface Props {
  pack: Gunpack;
  onClick: () => void;
  index?: number;
}

export function GunpackCard({ pack, onClick, index }: Props) {
  const { t } = useTranslation();
  const sizeM = (pack.weaponsRpfSize / (1024 * 1024)).toFixed(0);
  const hasImage = pack.coverKind === 'image' && !!pack.coverUrl;
  const eagerImage = index === undefined || index < 18;

  const isFav  = useGunpackStore(s => s.gunpackFavorites.has(pack.id));
  const toggle = useGunpackStore(s => s.toggleGunpackFavorite);
  const onFavClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    toggle(pack.id);
  };

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
        'border border-transparent hover:border-accent/60 ' +
        'shadow-z2 hover:shadow-glow-accent ' +
        'hover:-translate-y-1 ' +
        'focus-visible:outline-none focus-visible:shadow-glow-accent focus-visible:border-accent/60'
      }
    >
      {}
      {hasImage ? (
        <LazyImage
          src={pack.coverUrl!}
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
          <Crosshair size={56} className="opacity-25" />
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
        {pack.isVerified && (
          <span className="px-1.5 py-1 rounded-md bg-accent/85 backdrop-blur-md flex items-center" title={t('redux.verified', 'Проверено')}>
            <CheckCircle2 size={13} className="text-white" />
          </span>
        )}
        <span className="px-1.5 py-1 rounded-md bg-black/70 backdrop-blur-md text-xs text-white font-bold tabular-nums">
          {t('guns.sizeMb', { defaultValue: '{{size}} МБ', size: sizeM })}
        </span>
      </div>

      {}
      <button
        type="button"
        onClick={onFavClick}
        title={isFav ? t('redux.unfavorite', 'Удалить из избранного') : t('redux.favorite', 'В избранное')}
        aria-label={isFav ? t('redux.unfavorite', 'Удалить из избранного') : t('redux.favorite', 'В избранное')}
        style={{ outline: 'none' }}
        className="absolute top-3 left-3 z-10 w-[34px] h-[34px] rounded-lg flex items-center justify-center
                   bg-black/45 backdrop-blur-sm border border-white/10 text-white/70
                   hover:text-white hover:bg-black/60 hover:scale-105 active:scale-95
                   transition-[transform,background-color,color] duration-200 ease-depth"
      >
        <Star size={15} fill={isFav ? 'currentColor' : 'none'} className={isFav ? 'text-amber-300' : ''} />
      </button>

      {}
      <div className="absolute bottom-0 left-0 right-0 px-4 pb-3.5 z-10 flex flex-col">
        <div className="font-display font-bold text-white text-lg uppercase tracking-wide truncate leading-tight
                        [text-shadow:0_2px_4px_rgba(0,0,0,0.95),0_0_18px_rgba(0,0,0,0.6)]">
          {pack.name}
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
              {pack.author && (
                <div className="text-xs text-white/75 truncate">
                  {t('guns.byAuthor', { defaultValue: 'от {{author}}', author: pack.author })}
                </div>
              )}
              <div className="flex items-center gap-3 text-xs font-bold tabular-nums text-white/90">
                <span className="inline-flex items-center gap-1.5 text-accent">
                  <Download size={12} />
                  {formatDownloads(pack.downloadCount)}
                </span>
                <span className="inline-flex items-center gap-1.5 text-white/75">
                  <HardDrive size={12} />
                  {t('guns.sizeMb', { defaultValue: '{{size}} МБ', size: sizeM })}
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
