import { Star, Download, CheckCircle2 } from 'lucide-react';
import { useReduxStore } from '@/store/reduxStore';
import { useSessionStore } from '@/store/sessionStore';
import type { ReduxItem } from '@/bridge/types';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';
import { TagsBadge } from './TagsBadge';
import { LazyImage } from '@/components/LazyImage';

interface Props {
  item: ReduxItem;
  index?: number;
  onClick?: () => void;
  size?: 'default' | 'fill';
}

export function ReduxCard({ item, onClick, size = 'default', index }: Props) {
  const eagerImage = index === undefined || index < 18;
  const select = useReduxStore(s => s.select);
  const favorites = useReduxStore(s => s.favorites);
  const toggleFavorite = useReduxStore(s => s.toggleFavorite);
  const installedReduxId = useReduxStore(s => s.installedReduxId);
  const auth = useSessionStore(s => s.auth);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const handleOpen = onClick ?? (() => select(item.id));
  const sizeClass = size === 'fill' ? 'h-full' : 'h-56';

  const isFav = favorites.has(item.id);
  const isInstalled = installedReduxId === item.id;
  const sizeLabel = formatSize(item.patchSizeBytes);
  const showsMajestic = item.supportedServers.includes('majestic');
  const showsGta5rp   = item.supportedServers.includes('gta5rp');

  const aggregate = useReduxStore(s => s.ratings[item.id]);
  const rating = aggregate?.avg ?? 0;
  const ratingCount = aggregate?.count ?? 0;
  const hasRating = ratingCount > 0;

  const onFavClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    void toggleFavorite(item.id, userId);
  };

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={handleOpen}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') handleOpen(); }}
      className={
        `group relative ${sizeClass} w-full rounded-2xl overflow-hidden cursor-pointer ` +
        'bg-bg-elevated text-left transform-gpu will-change-transform ' +

        'transition-[transform,box-shadow,border-color] duration-[500ms] ease-smooth ' +
        'border border-transparent hover:border-accent/60 ' +
        'shadow-z2 hover:shadow-glow-accent ' +
        'hover:-translate-y-1 ' +
        'focus-visible:outline-none focus-visible:shadow-glow-accent focus-visible:border-accent/60'
      }
    >
      {}
      {item.previewUrl ? (
        <LazyImage
          src={item.previewUrl}
          eager={eagerImage}
          alt=""
          aria-hidden="true"
          draggable={false}
          className="absolute inset-0 w-full h-full object-cover object-center select-none
                     transform-gpu transition-transform duration-[1100ms] ease-smooth
                     group-hover:scale-[1.045]"
          style={{ willChange: 'transform', backfaceVisibility: 'hidden' }}
        />
      ) : (
        <div className="absolute inset-0 bg-gradient-to-br from-bg-elevated to-bg-base" />
      )}

      {}
      <div

        style={{ height: '32%' }}
        className="absolute inset-x-0 bottom-0 pointer-events-none
                   bg-gradient-to-t from-black/85 via-black/40 to-transparent"
      />

      {}
      <div className="absolute top-3 right-3 z-10 flex flex-col items-end gap-2">
        <div className="flex items-center gap-2">
          {showsGta5rp   && <ServerBadge logo={gta5rpLogo}   alt="GTA5RP" />}
          {showsMajestic && <ServerBadge logo={majesticLogo} alt="Majestic" />}
          <span className="px-1.5 py-1 rounded-md bg-black/70 backdrop-blur-md text-xs text-white font-bold tabular-nums">
            {sizeLabel}
          </span>
        </div>

        {}
        {(item.tagNew || item.tagBest) && (
          <TagsBadge item={item} size="sm" />
        )}
      </div>

      {}
      <button
        type="button"
        onClick={onFavClick}
        title={isFav ? 'Удалить из избранного' : 'В избранное'}
        aria-label={isFav ? 'Удалить из избранного' : 'В избранное'}
        className="absolute top-3 left-3 z-10 w-[34px] h-[34px] rounded-lg flex items-center justify-center
                   bg-black/45 backdrop-blur-sm border border-white/10 text-white/70
                   hover:text-white hover:bg-black/60 hover:scale-105 active:scale-95
                   transition-[transform,background-color,color] duration-200 ease-depth"
      >
        <Star size={15} fill={isFav ? 'currentColor' : 'none'} className={isFav ? 'text-amber-300' : ''} />
      </button>

      {}
      <div className="absolute bottom-0 left-0 right-0 px-4 pb-3.5 z-10 flex flex-col">
        {}
        <div
          className="font-display font-bold text-white text-lg uppercase tracking-wide truncate leading-tight
                     [text-shadow:0_2px_4px_rgba(0,0,0,0.95),0_0_18px_rgba(0,0,0,0.6)]"
        >
          {item.name || item.id}
        </div>

        {}
        <div className="grid grid-rows-[0fr] group-hover:grid-rows-[1fr]
                        group-focus-visible:grid-rows-[1fr]
                        transition-[grid-template-rows] duration-[450ms] ease-smooth">
          <div className="overflow-hidden min-h-0">
            <div className="pt-1.5 flex flex-col gap-1
                            opacity-0 translate-y-1.5
                            group-hover:opacity-100 group-hover:translate-y-0
                            group-focus-visible:opacity-100 group-focus-visible:translate-y-0
                            transition-[opacity,transform] duration-[350ms] delay-[100ms] ease-smooth">
              <div className="flex items-center gap-3 text-xs font-bold tabular-nums text-white/90">
                {hasRating ? (
                  <span className="inline-flex items-center gap-1 text-yellow-400">
                    <Star size={12} className="fill-current" />
                    {rating.toFixed(1)}
                    <span className="text-white/55 font-semibold">({ratingCount})</span>
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 text-white/45 font-semibold">
                    <Star size={12} />
                    Нет оценок
                  </span>
                )}
                <span className="inline-flex items-center gap-1 text-white/75">
                  <Download size={12} />
                  {formatDownloads(item.downloadCount)}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {}
      {isInstalled && (
        <div className="absolute top-3 left-[58px] z-10 inline-flex items-center gap-1.5
                        h-[34px] px-2.5 rounded-md bg-black/70 backdrop-blur-md
                        text-green-300 text-[10px] font-bold uppercase tracking-wider">
          <CheckCircle2 size={13} className="text-green-400" />
          <span>Установлен</span>
        </div>
      )}
    </div>
  );
}

function ServerBadge({ logo, alt, imgClass = 'w-4 h-4' }: { logo: string; alt: string; imgClass?: string }) {
  return (
    <div className="px-1.5 py-1 rounded-md bg-black/70 backdrop-blur-md flex items-center" title={alt}>
      <img src={logo} alt={alt} className={`${imgClass} object-contain`} />
    </div>
  );
}

function formatDownloads(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
  if (n >= 1_000)     return (n / 1_000).toFixed(1) + 'k';
  return String(n);
}

function formatSize(bytes: number): string {
  if (bytes <= 0) return '0 MB';
  const mb = bytes / (1024 * 1024);
  if (mb < 1024) return `${Math.round(mb)} MB`;
  return `${(mb / 1024).toFixed(1)} GB`;
}
