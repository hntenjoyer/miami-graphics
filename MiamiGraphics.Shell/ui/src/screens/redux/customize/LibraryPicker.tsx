import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { BookOpen, Search, ImageOff, Check, Eye, X } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import { bridge } from '@/bridge';
import type { LibraryComponent } from '@/bridge/types';
import {
  useCustomizeStore,
  type CustomizeComponentName,
} from '@/store/customizeStore';
import { AccentLoader, EASE_DEPTH } from '@/design';
import { SlideIntro } from './SlideIntro';

interface Props { forComponent: CustomizeComponentName }

const SLIDE_TITLE_RU: Record<CustomizeComponentName, string> = {
  minimap:   'Выбери миникарту из нашей библиотеки',
  crosshair: 'Выбери прицел из нашей библиотеки',
  tracers:   'Выбери трейсера из нашей библиотеки',
  bloodfx:   'Выбери эффекты из нашей библиотеки',
  timecycle: 'Выбери таймциклы из нашей библиотеки',
  armor:     'Выбери броник из нашей библиотеки',
  arena:     'Выбери арену из нашей библиотеки',
};

const SUBTITLE_LABEL_RU: Record<CustomizeComponentName, string> = {
  minimap:   'миникарты',
  crosshair: 'прицелы',
  tracers:   'трейсера',
  bloodfx:   'эффекты',
  timecycle: 'таймциклы',
  armor:     'броники',
  arena:     'арена',
};

const slideTitle = (t: TFunction, c: CustomizeComponentName) =>
  t(`customize.library.title.${c}`, { defaultValue: SLIDE_TITLE_RU[c] });

const subtitleLabel = (t: TFunction, c: CustomizeComponentName) =>
  t(`customize.library.subtitle.${c}`, { defaultValue: SUBTITLE_LABEL_RU[c] });

export function LibraryPicker({ forComponent }: Props) {
  const { t } = useTranslation();
  const draft        = useCustomizeStore(s => s.draft);
  const setGeneric   = useCustomizeStore(s => s.setGeneric);
  const setMinimap   = useCustomizeStore(s => s.setMinimap);
  const openComp     = useCustomizeStore(s => s.openComponent);

  const [items, setItems]     = useState<LibraryComponent[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr]         = useState<string | null>(null);
  const [query, setQuery]     = useState('');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setErr(null);
    bridge.libraryList(forComponent)
      .then(list => { if (!cancelled) setItems(list); })
      .catch(e => { if (!cancelled) setErr((e as Error).message); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [forComponent]);

  const presets = items ?? [];
  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return presets;
    return presets.filter(p =>
      p.name.toLowerCase().includes(q)
      || (p.author ?? '').toLowerCase().includes(q));
  }, [presets, query]);

  const current = forComponent === 'minimap' || forComponent === 'tracers'
    ? null
    : draft?.[forComponent] ?? null;
  const selectedId =
    forComponent === 'minimap'
      ? (draft?.minimap.librarySource?.libraryItemId ?? null)
      : current && current.kind === 'library'
        ? current.libraryItemId
        : null;

  const onPick = (item: LibraryComponent) => {
    if (forComponent === 'tracers') return;
    if (forComponent === 'minimap') {
      setMinimap({
        enabled:       true,
        importedFrom:  null,
        librarySource: { libraryItemId: item.id, libraryItemName: item.name },
      });
      openComp('minimap');
      return;
    }
    setGeneric(forComponent, {
      kind:               'library',
      libraryItemId:      item.id,
      libraryItemName:    item.name,
      libraryItemAuthor:  item.author,
    });

    openComp(forComponent);
  };

  return (
    <SlideIntro
      resetKey={`library-picker:${forComponent}`}
      title={slideTitle(t, forComponent)}
      subtitle={
        <span className="inline-flex items-center gap-2">
          <BookOpen size={12} className="text-accent" />
          <span className="text-accent font-bold tracking-wider uppercase text-xs">
            {subtitleLabel(t, forComponent)}
          </span>
        </span>
      }
    >
      <div className="relative h-full flex flex-col">
        <header className="px-8 pt-6 pb-2 flex items-center gap-4 shrink-0">
          <BackButton onClick={() => openComp(forComponent)} label={t('customize.back')} />
          <div className="flex-1" />
        </header>

        <div className="flex-1 overflow-y-auto px-8 pb-12">
          <div className="max-w-[1280px] mx-auto flex flex-col gap-6">
            {}
            <motion.div
              initial={{ opacity: 0, y: 6 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.32, ease: EASE_DEPTH }}
              className="flex items-center gap-3 flex-wrap mt-4"
            >
              <div className="relative flex-1 min-w-[220px] max-w-md">
                <Search size={14} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-text-muted" />
                <input
                  type="text"
                  value={query}
                  onChange={e => setQuery(e.target.value)}
                  placeholder={t('customize.library.searchPlaceholder', 'Поиск по названию или автору')}
                  className="w-full h-11 pl-10 pr-3 rounded-2xl
                             bg-glass-strong border border-glass-border
                             backdrop-blur-glass text-sm text-text-primary
                             outline-none focus:border-accent/60 focus:shadow-glow-accent
                             transition-[border-color,box-shadow] duration-300 ease-depth"
                />
              </div>
            </motion.div>

            {loading ? (
              <div className="flex items-center justify-center py-16 gap-3 text-sm text-text-muted">
                <AccentLoader size={20} />
                <span>{t('customize.library.loading', 'Загружаю библиотеку...')}</span>
              </div>
            ) : err ? (
              <p className="text-xs text-status-error text-center py-12">{err}</p>
            ) : filtered.length === 0 ? (
              <EmptyLibraryList />
            ) : (
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4">
                {filtered.map((it, i) => (
                  <motion.div
                    key={it.id}
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.32, delay: 0.04 + i * 0.025, ease: EASE_DEPTH }}
                  >
                    <LibraryCard
                      item={it}
                      selected={selectedId === it.id}
                      onPick={() => onPick(it)}
                    />
                  </motion.div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </SlideIntro>
  );
}

function EmptyLibraryList() {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col items-center justify-center py-20 gap-3 text-text-muted">
      <div className="w-14 h-14 rounded-2xl bg-glass-strong
                      flex items-center justify-center text-text-muted">
        <BookOpen size={22} strokeWidth={1.6} />
      </div>
      <p className="text-sm text-center max-w-sm">
        {t('customize.library.empty', 'Пока в библиотеке нет ни одного пресета этого типа. Залить можно через Admin → Library.')}
      </p>
    </div>
  );
}

function LibraryCard({
  item, selected, onPick,
}: { item: LibraryComponent; selected: boolean; onPick: () => void }) {
  const { t } = useTranslation();
  const preview = item.previewUrl ?? '';
  const [imgBroken, setImgBroken] = useState(false);
  const [lightbox, setLightbox] = useState(false);
  const showPhoto = !!preview && !imgBroken;

  return (
    <>
      <motion.div
        whileHover={{ y: -3, transition: { duration: 0.22, ease: EASE_DEPTH } }}
        className={
          'group relative rounded-3xl overflow-hidden border flex flex-col will-change-transform ' +
          'bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid ' +
          'transition-[border-color,box-shadow] duration-300 ease-smooth ' +
          (selected
            ? 'border-[color-mix(in_srgb,var(--accent)_60%,transparent)] ' +
              'shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_55%,transparent),0_12px_36px_-8px_color-mix(in_srgb,var(--accent)_60%,transparent)]'
            : 'border-white/[0.08] hover:border-[color-mix(in_srgb,var(--accent)_45%,transparent)]')
        }
      >
        <span
          aria-hidden
          className="absolute top-0 inset-x-0 h-px pointer-events-none z-20
                     bg-gradient-to-r from-transparent via-white/40 to-transparent"
        />
        <div className="relative aspect-[16/9] overflow-hidden">
          {showPhoto ? (
            <>
              <div
                aria-hidden
                className="absolute inset-0"
                style={{
                  background: `url(${preview}) center / cover no-repeat`,
                  filter: 'blur(20px) saturate(115%) brightness(0.6)',
                  transform: 'scale(1.18)',
                }}
              />
              <img
                src={preview}
                alt=""
                draggable={false}
                onError={() => setImgBroken(true)}
                className="absolute inset-0 w-full h-full object-contain
                           transition-transform duration-700 ease-smooth group-hover:scale-[1.04]"
              />
            </>
          ) : (
            <div
              className="absolute inset-0 flex flex-col items-center justify-center gap-1 text-text-muted/70
                         bg-gradient-to-br from-accent-soft via-bg-elevated to-bg-base"
            >
              <ImageOff size={22} strokeWidth={1.5} />
              <span className="text-[10px] uppercase tracking-wider">{t('redux.thumbNoPhoto', 'нет фото')}</span>
            </div>
          )}

          {showPhoto && (
            <button
              type="button"
              onClick={() => setLightbox(true)}
              title={t('catalog.viewFull', 'Подробнее')}
              aria-label={t('catalog.viewFull', 'Подробнее')}
              className="absolute top-2 right-2 inline-flex items-center justify-center w-8 h-8
                         rounded-full bg-black/55 backdrop-blur-md text-white z-10
                         opacity-0 group-hover:opacity-100 hover:bg-black/80
                         transition-opacity duration-150"
            >
              <Eye size={13} strokeWidth={2.4} />
            </button>
          )}

          {selected && (
            <motion.span
              initial={{ scale: 0, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              transition={{ duration: 0.28, ease: EASE_DEPTH }}
              className="absolute top-2 left-2 z-10 w-7 h-7 rounded-full bg-accent
                         flex items-center justify-center
                         shadow-[0_0_0_3px_var(--bg-base),0_0_0_4px_color-mix(in_srgb,var(--accent)_45%,transparent)]"
            >
              <Check size={13} className="text-text-on-accent" strokeWidth={3} />
            </motion.span>
          )}
        </div>

        <div className="relative flex items-center gap-3 p-3">
          <div className="min-w-0 flex-1">
            <span className="text-base font-bold text-text-primary truncate uppercase tracking-wide block">{item.name}</span>
          </div>
          <motion.button
            type="button"
            onClick={onPick}
            whileHover={{ scale: 1.02, transition: { duration: 0.18, ease: EASE_DEPTH } }}
            whileTap={{ scale: 0.98, transition: { duration: 0.12, ease: EASE_DEPTH } }}
            style={{ outline: 'none' }}
            className={
              'inline-flex items-center justify-center gap-2 h-10 px-4 rounded-xl shrink-0 ' +
              'text-[12px] font-bold uppercase tracking-[0.08em] transition-colors ' +
              (selected
                ? 'bg-accent text-text-on-accent border border-accent'
                : 'bg-bg-elevated/55 text-text-primary border border-white/[0.08] ' +
                  'hover:bg-bg-elevated/75 hover:border-white/[0.18]')
            }
          >
            {selected && <Check size={13} strokeWidth={2.6} />}
            <span>{selected ? t('customize.library.picked', 'Выбрано') : t('customize.library.pick', 'Выбрать для связки')}</span>
          </motion.button>
        </div>
      </motion.div>

      <AnimatePresence>
        {lightbox && (
          <LibraryLightbox url={preview} title={item.name} onClose={() => setLightbox(false)} />
        )}
      </AnimatePresence>
    </>
  );
}

function LibraryLightbox({
  url, title, onClose,
}: { url: string; title: string; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <motion.div
      key="library-lightbox"
      className="fixed inset-0 z-[100] bg-black/75 backdrop-blur-glass-ultra backdrop-saturate-liquid
                 flex items-center justify-center p-6"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.22, ease: EASE_DEPTH }}
      onClick={onClose}
    >
      <motion.div
        className="relative w-[min(90vw,1100px)] aspect-[16/9] max-h-[85vh] rounded-3xl overflow-hidden bg-glass-ultra"
        initial={{ opacity: 0, scale: 0.94, y: 16 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        exit   ={{ opacity: 0, scale: 0.96, y: 8 }}
        transition={{ duration: 0.3, ease: EASE_DEPTH }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="absolute top-0 inset-x-0 z-20 px-4 py-3 flex items-center gap-3
                        bg-gradient-to-b from-black/55 to-transparent">
          <h2 className="text-sm font-semibold text-white truncate flex-1 drop-shadow-[0_2px_4px_rgba(0,0,0,0.8)]">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            className="w-9 h-9 rounded-lg flex items-center justify-center
                       text-white/85 bg-black/50 hover:bg-black/80 hover:text-white transition-colors"
          >
            <X size={16} />
          </button>
        </div>
        <img
          src={url}
          alt=""
          aria-hidden
          className="absolute inset-0 w-full h-full object-cover"
          style={{ filter: 'blur(32px) saturate(120%) brightness(0.55)', transform: 'scale(1.15)' }}
        />
        <img
          src={url}
          alt=""
          draggable={false}
          className="absolute inset-0 w-full h-full object-contain"
        />
      </motion.div>
    </motion.div>
  );
}
