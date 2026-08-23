import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { Check, Loader2, Trash2, Download, AlertTriangle, Play, Fuel, Search, X, RefreshCw, HardDrive, Flower2 } from 'lucide-react';
import { EASE_DEPTH, ENV_CARD_H } from '@/design/tokens';
import { BackButton } from '@/components/BackButton';
import { VideoModal } from '@/components/VideoModal';
import { useNavStore } from '@/store/navStore';
import { useImprovementsStore } from '@/store/improvementsStore';
import type { Improvement } from '@/bridge/types';

interface Props {
  category?: string;
  title?: string;
  icon?: typeof Fuel;
  embedded?: boolean;
}

export function ImprovementsScreen({ category, title, icon: Icon = Fuel, embedded = false }: Props) {
  const { t } = useTranslation();
  const heading   = title ?? t('environment.improvements', 'Заправки');
  const navigate  = useNavStore(s => s.requestNavigate);
  const list      = useImprovementsStore(s => s.list);
  const loading   = useImprovementsStore(s => s.loading);
  const busyId    = useImprovementsStore(s => s.busyId);
  const error     = useImprovementsStore(s => s.error);
  const load      = useImprovementsStore(s => s.load);
  const install   = useImprovementsStore(s => s.install);
  const remove    = useImprovementsStore(s => s.remove);
  const clearError= useImprovementsStore(s => s.clearError);

  const [video, setVideo] = useState<{ url: string; title: string; poster: string } | null>(null);
  const [query, setQuery] = useState('');

  useEffect(() => { void load(); }, [load]);

  const inCategory = useMemo(
    () => (category ? list.filter(x => x.category === category) : list),
    [list, category],
  );
  const shown = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return inCategory;
    return inCategory.filter(x =>
      x.name.toLowerCase().includes(q) || x.description.toLowerCase().includes(q));
  }, [inCategory, query]);

  const installed = inCategory.filter(x => x.installed);

  const replaces = (x: Improvement) =>
    x.exclusiveGroup
      ? installed.find(y => y.id !== x.id && y.exclusiveGroup === x.exclusiveGroup) ?? null
      : null;

  if (embedded && shown.length === 0) return null;

  return (
    <div className={embedded ? '' : 'h-full overflow-y-auto'}>
      <div className={embedded ? '' : 'max-w-[1760px] w-full mx-auto px-8 pt-6 pb-10'}>
        <div className="flex items-center gap-3 mb-6">
          {!embedded && (
            <BackButton onClick={() => navigate('environment')} label={t('common.back', 'Назад')} />
          )}
          <div className="flex items-center gap-2">
            <Icon size={18} strokeWidth={1.8} className="text-text-muted" />
            <h1 className={embedded
              ? 'text-[15px] font-bold uppercase tracking-[0.14em] text-text-secondary'
              : 'text-[22px] font-semibold tracking-tight text-text-primary'}>{heading}</h1>
          </div>

          <label className="group relative flex items-center flex-1 min-w-[180px] max-w-[300px] h-10 ml-2
                            rounded-xl border border-white/[0.07] bg-white/[0.03]
                            focus-within:border-white/[0.18] transition-colors">
            <Search size={14} className="absolute left-3 text-text-muted" />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={t('common.searchByName', 'Поиск по названию')}
              className="w-full h-full pl-10 pr-9 bg-transparent rounded-xl text-[13px]
                         text-text-primary placeholder:text-text-muted focus:outline-none"
            />
            {query && (
              <button
                type="button"
                onClick={() => setQuery('')}
                aria-label={t('common.clear', 'Очистить')}
                className="absolute right-1.5 w-7 h-7 rounded-md flex items-center justify-center
                           text-text-muted hover:text-text-primary hover:bg-white/[0.06]"
              >
                <X size={13} />
              </button>
            )}
          </label>
        </div>

        {error && (
          <div className="mb-5 flex items-start gap-2 rounded-xl border border-amber-500/30
                          bg-amber-500/10 px-3 py-2.5">
            <AlertTriangle size={14} className="mt-0.5 shrink-0 text-amber-400" />
            <div className="flex-1 text-[12px] text-text-primary">{error}</div>
            <button type="button" onClick={clearError}
                    className="text-[11px] text-text-secondary hover:text-text-primary">
              {t('improvements.hideError', 'скрыть')}
            </button>
          </div>
        )}

        {loading && shown.length === 0 && (
          <div className="flex items-center gap-2 text-[12px] text-text-secondary">
            <Loader2 size={14} className="animate-spin" /> {t('improvements.loading', 'Загружаю каталог')}
          </div>
        )}

        {!loading && shown.length === 0 && !error && (
          <div className="rounded-2xl border border-border-subtle bg-bg-elevated/40 px-4 py-6
                          text-[12px] text-text-secondary">
            {t('improvements.empty', 'Пока пусто.')}
          </div>
        )}

        <div className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(440px,1fr))]">
          {shown.map((x, i) => (
            <ImprovementTile
              key={x.id}
              item={x}
              index={i}
              icon={Icon}
              replaces={replaces(x)}
              busy={busyId === x.id}
              otherBusy={busyId !== null && busyId !== x.id}
              onInstall={() => void install(x.id)}
              onRemove={() => void remove(x.id)}
              onPlay={() => setVideo({ url: x.videoUrl, title: x.name, poster: x.previewUrl })}
            />
          ))}
        </div>
      </div>

      {video && (
        <VideoModal url={video.url} title={video.title} poster={video.poster} onClose={() => setVideo(null)} />
      )}
    </div>
  );
}

function ImprovementTile({
  item, index, icon: Icon, replaces, busy, otherBusy, onInstall, onRemove, onPlay,
}: {
  item: Improvement;
  index: number;
  icon: typeof Fuel;
  replaces: Improvement | null;
  busy: boolean;
  otherBusy: boolean;
  onInstall: () => void;
  onRemove: () => void;
  onPlay: () => void;
}) {
  const { t } = useTranslation();
  const [broken, setBroken] = useState(false);
  const hasVideo = !!item.videoUrl;
  const showImage = !!item.previewUrl && !broken;
  const willReplace = !item.installed && !!replaces;
  const disabled = busy || otherBusy;
  const sizeLabel = t('common.sizeMB', '{{value}} МБ', { value: (item.sizeBytes / 1048576).toFixed(0) });

  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.36, ease: EASE_DEPTH, delay: Math.min(index * 0.06, 0.3) }}
      role={hasVideo ? 'button' : undefined}
      tabIndex={hasVideo ? 0 : undefined}
      onClick={hasVideo ? onPlay : undefined}
      onKeyDown={hasVideo
        ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onPlay(); } }
        : undefined}
      className={
        `group relative w-full ${ENV_CARD_H} overflow-hidden rounded-2xl bg-bg-elevated text-left ` +
        'transform-gpu will-change-transform shadow-z2 ' +
        'transition-[transform,box-shadow,border-color] duration-500 ease-smooth ' +
        'hover:-translate-y-1 hover:shadow-glow-accent border ' +
        (item.installed
          ? 'border-[color-mix(in_srgb,var(--accent)_60%,transparent)]'
          : 'border-transparent hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)]') +
        (hasVideo ? ' cursor-pointer' : '')
      }
    >
      {showImage ? (
        <img
          src={item.previewUrl}
          alt="" aria-hidden draggable={false} loading="lazy"
          onError={() => setBroken(true)}
          className="absolute inset-0 w-full h-full object-cover select-none
                     transform-gpu transition-transform duration-[1100ms] ease-smooth
                     group-hover:scale-[1.05]"
          style={{ backfaceVisibility: 'hidden' }}
        />
      ) : (
        <div className="absolute inset-0 bg-gradient-to-br from-bg-elevated to-bg-base
                        flex items-center justify-center">
          <Icon size={48} strokeWidth={1.2} className="text-white/15" />
        </div>
      )}

      <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-black/25 to-black/20
                      pointer-events-none" />

      <div className="absolute top-3 left-3 z-10 flex flex-col items-start gap-1.5">
        <span className="inline-flex items-center gap-1.5 px-2 py-1 rounded-md bg-base-70 backdrop-blur-sm
                         text-[11px] font-bold tabular-nums text-white/90"
              title={t('environment.sizeTitle', 'Размер: {{size}}', { size: sizeLabel })}>
          <HardDrive size={11} className="text-accent" strokeWidth={2.2} />
          {sizeLabel}
        </span>
        {item.installed && (
          <span className="inline-flex items-center gap-1.5
                           px-2 py-1 rounded-md bg-accent text-text-on-accent
                           text-[10px] font-bold uppercase tracking-[0.14em] shadow-lg">
            <Check size={11} strokeWidth={3} /> {t('catalog.installedBadge', 'установлено')}
          </span>
        )}
      </div>

      <div className="absolute top-3 right-3 z-10 flex items-center gap-1.5">

        {item.category === 'gas_stations' && (
          <span className="group/warn relative inline-flex items-center justify-center w-[26px] h-[26px]
                           rounded-md bg-amber-500/25 backdrop-blur-sm border border-amber-400/40
                           text-amber-300 cursor-help">
            <AlertTriangle size={13} strokeWidth={2.4} />
            <span className="pointer-events-none absolute right-0 top-full mt-1.5 z-30 w-[268px]
                             rounded-lg border border-amber-400/30 bg-[#12141a]/95 backdrop-blur-md
                             px-3 py-2 text-left text-[11.5px] leading-snug font-medium text-white/90
                             normal-case tracking-normal shadow-2xl
                             opacity-0 translate-y-[-4px] transition-[opacity,transform] duration-150
                             group-hover/warn:opacity-100 group-hover/warn:translate-y-0">
              {t('improvements.gasStationWarning',
                'На РП-проектах возможны баги с наличием стандартных заправок поверх модовых. На функционал никак не влияет, только на внешность.')}
            </span>
          </span>
        )}
        {item.id === 'hanami_trees' && (
          <span className="group/petals relative inline-flex items-center justify-center w-[26px] h-[26px]
                           rounded-md bg-pink-500/25 backdrop-blur-sm border border-pink-400/40
                           text-pink-200 cursor-help">
            <Flower2 size={13} strokeWidth={2.4} />
            <span className="pointer-events-none absolute right-0 top-full mt-1.5 z-30 w-[268px]
                             rounded-lg border border-pink-400/30 bg-[#12141a]/95 backdrop-blur-md
                             px-3 py-2 text-left text-[11.5px] leading-snug font-medium text-white/90
                             normal-case tracking-normal shadow-2xl
                             opacity-0 translate-y-[-4px] transition-[opacity,transform] duration-150
                             group-hover/petals:opacity-100 group-hover/petals:translate-y-0">
              {t('improvements.hanamiPetals', 'Анимированные лепестки')}
            </span>
          </span>
        )}
        {item.popularity > 0 && (
          <span className="inline-flex items-center gap-1.5 px-2 py-1 rounded-md bg-base-70 backdrop-blur-sm
                           text-[11px] font-bold tabular-nums text-white/90"
                title={t('environment.installCountTitle', 'Установок: {{n}}', {
                  n: item.popularity.toLocaleString('ru-RU'),
                })}>
            <Download size={11} strokeWidth={2.2} className="text-accent" />
            {item.popularity.toLocaleString('ru-RU')}
          </span>
        )}
      </div>

      {hasVideo && (
        <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
          <span className="w-14 h-14 rounded-full bg-black/45 backdrop-blur-md border border-white/25
                           flex items-center justify-center shadow-2xl
                           opacity-0 scale-90 group-hover:opacity-100 group-hover:scale-100
                           transition-[opacity,transform,background-color,border-color] duration-300
                           group-hover:bg-accent group-hover:border-accent">
            <Play size={20} strokeWidth={2.4} className="ml-0.5 text-white group-hover:text-text-on-accent" />
          </span>
        </div>
      )}

      <div className="absolute inset-x-0 bottom-0 p-4 z-10 flex items-end gap-3">
        <div className="min-w-0 flex-1">
          <div className="font-display font-bold text-white text-base uppercase tracking-wide truncate
                          drop-shadow-[0_2px_8px_rgba(0,0,0,0.6)]">
            {item.name}
          </div>
          {item.description && (
            <div className="text-[12px] text-white/75 mt-1 line-clamp-2">{item.description}</div>
          )}
          {willReplace && (
            <div className="text-[11.5px] text-amber-300 mt-1 flex items-center gap-1.5">
              <RefreshCw size={11} strokeWidth={2.2} />
              {t('improvements.willReplace', 'Заменит «{{name}}»', { name: replaces!.name })}
            </div>
          )}
        </div>

        <button
          type="button"
          disabled={disabled}
          onClick={(e) => { e.stopPropagation(); item.installed ? onRemove() : onInstall(); }}
          style={{ outline: 'none' }}
          className={
            'shrink-0 inline-flex h-12 items-center justify-center gap-2 px-4 rounded-xl ' +
            'text-sm font-bold uppercase tracking-wider ' +
            'bg-bg-elevated/55 border border-white/[0.08] backdrop-blur-md ' +
            'hover:bg-bg-elevated/75 transition-colors ' +
            'disabled:opacity-40 disabled:pointer-events-none ' +
            (item.installed
              ? 'text-red-300 hover:border-red-500/40'
              : 'text-text-primary hover:border-white/[0.18]')
          }
        >
          {busy
            ? <Loader2 size={16} className="animate-spin" />
            : item.installed ? <Trash2 size={16} />
            : willReplace ? <RefreshCw size={16} /> : <Download size={16} />}
          {busy
            ? (item.installed
                ? t('improvements.removing', 'Снимаю')
                : t('improvements.installing', 'Ставлю'))
            : item.installed ? t('improvements.remove', 'Снять')
            : willReplace ? t('improvements.replace', 'Заменить')
            : t('improvements.install', 'Установить')}
        </button>
      </div>
    </motion.div>
  );
}
