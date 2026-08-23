import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from '@/i18n';
import { AnimatePresence, motion } from 'framer-motion';
import { X, Trees, Eye, Download, ChevronLeft, ChevronRight, HardDrive, Check, RefreshCw } from 'lucide-react';
import { bridge } from '@/bridge';
import { useNavStore } from '@/store/navStore';
import { BackButton } from '@/components/BackButton';
import { useImprovementsStore } from '@/store/improvementsStore';
import { AccentLoader, ENV_CARD_H } from '@/design';
import { ensureBackupOrGate } from '@/store/installGate';
import { Toast } from '@/components/Toast';
import { VideoPlayer } from '@/components/VideoPlayer';
import { shuffled } from '@/utils/shuffle';
import type { Improvement } from '@/bridge/types';

const TREE_PACKS: ReadonlyArray<{
  id: string; name: string; photos?: number; skipFirst?: boolean; sizeBytes: number;
  descKey: string; descRu: string;
}> = [
  { id: 'pink', name: 'PINK', photos: 8, sizeBytes: 856_293_888,
    descKey: 'environment.treePacks.pink.desc',
    descRu: 'Розовое цветение по всему штату: кроны деревьев и кусты в городе, на холмах и вдоль трасс.' },
  { id: 'blue', name: 'BLUE', photos: 8, skipFirst: true, sizeBytes: 951_615_488,
    descKey: 'environment.treePacks.blue.desc',
    descRu: 'Синяя листва по всему штату: холодные кроны в городе, на холмах и вдоль трасс.' },
  { id: 'grey', name: 'GREY', photos: 8, skipFirst: true, sizeBytes: 908_111_360,
    descKey: 'environment.treePacks.grey.desc',
    descRu: 'Серо-стальная листва: приглушённые кроны без цветного шума, город выглядит строже.' },
  { id: 'autumn', name: 'AUTUMN', photos: 8, sizeBytes: 822_541_312,
    descKey: 'environment.treePacks.autumn.desc',
    descRu: 'Осень круглый год: рыжие и жёлтые кроны в городе, на холмах и вдоль трасс.' },
  { id: 'green', name: 'GREEN', photos: 8, sizeBytes: 325_688_320,
    descKey: 'environment.treePacks.green.desc',
    descRu: 'Сочная зелень вместо выцветшей ванильной листвы: плотные кроны и живые кусты.' },
  { id: 'realistic', name: 'REALISTIC', photos: 8, sizeBytes: 335_998_976,
    descKey: 'environment.treePacks.realistic.desc',
    descRu: 'Реалистичная растительность: естественный оттенок зелени и более плотные кроны.' },
];

const formatSize = (bytes: number): string => {
  const gb = bytes / 1024 ** 3;
  return gb >= 1
    ? i18n.t('common.sizeGB', { value: gb.toFixed(1), defaultValue: '{{value}} ГБ' })
    : i18n.t('common.sizeMB', { value: Math.round(bytes / 1024 ** 2), defaultValue: '{{value}} МБ' });
};

const TREE_SHOT_BASE = 'https://miamigraphicsstorage.uk/environment/trees';
const shotFor = (id: string) => `${TREE_SHOT_BASE}/${id}.webp`;
const shotN = (id: string, n: number) => (n <= 1 ? shotFor(id) : `${TREE_SHOT_BASE}/${id}_${n}.webp`);
const photosFor = (id: string, count = 1, skipFirst = false): string[] => {
  const start = skipFirst ? 2 : 1;
  const end = Math.max(count, start);
  const urls = Array.from({ length: end - start + 1 }, (_, i) => shotN(id, start + i));
  return urls.length ? urls : [shotFor(id)];
};

const TREES_INSTALL_NS = 'trees_install';

interface Lightbox {
  kind: 'pack' | 'improvement';
  id: string;
  name: string;
  description?: string;
  videoUrl?: string | null;
  photos: string[];
  index: number;
}

type TreeTile =
  | { kind: 'pack';        id: string; pack: (typeof TREE_PACKS)[number] }
  | { kind: 'improvement'; id: string; item: Improvement };

let cachedTreeOrder: { signature: string; entries: TreeTile[] } | null = null;

export function TreesScreen() {
  const { t } = useTranslation();
  const navigate = useNavStore(s => s.requestNavigate);

  const improvements  = useImprovementsStore(s => s.list);
  const loadImps      = useImprovementsStore(s => s.load);
  const impBusyId     = useImprovementsStore(s => s.busyId);
  const installImp    = useImprovementsStore(s => s.install);
  const removeImp     = useImprovementsStore(s => s.remove);
  const vegetation    = useMemo(
    () => improvements.filter(x => x.category === 'vegetation'), [improvements]);

  const tiles: TreeTile[] = useMemo(() => {
    const merged: TreeTile[] = [
      ...TREE_PACKS.map(pack => ({ kind: 'pack' as const, id: pack.id, pack })),
      ...vegetation.map(item => ({ kind: 'improvement' as const, id: item.id, item })),
    ];
    const signature = merged.map(x => `${x.kind}:${x.id}`).sort().join('|');
    if (cachedTreeOrder?.signature === signature) {
      const byKey = new Map(merged.map(x => [`${x.kind}:${x.id}`, x]));
      return cachedTreeOrder.entries.map(old => byKey.get(`${old.kind}:${old.id}`)!);
    }
    const entries = shuffled(merged);
    cachedTreeOrder = { signature, entries };
    return entries;
  }, [vegetation]);

  const [picks, setPicks] = useState<Record<string, number>>({});
  const [lightbox, setLightbox] = useState<Lightbox | null>(null);
  const [installingId, setInstallingId] = useState<string | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error' | 'info'; message: string } | null>(null);
  const [currentPack, setCurrentPack] = useState<{ id: string; name: string } | null>(null);

  const refreshPack = useCallback(() => {
    if (typeof bridge.getCurrentTreesInfo !== 'function') return;
    bridge.getCurrentTreesInfo()
      .then(i => setCurrentPack(i ? { id: i.id, name: i.name || i.id } : null))
      .catch(() => {  });
  }, []);

  useEffect(() => { void loadImps(); refreshPack(); }, [loadImps, refreshPack]);

  const activeTree: { kind: 'pack' | 'improvement'; id: string; name: string } | null =
    currentPack ? { kind: 'pack', ...currentPack }
    : (() => {
        const v = vegetation.find(x => x.installed);
        return v ? { kind: 'improvement' as const, id: v.id, name: v.name } : null;
      })();

  const replacedBy = (kind: 'pack' | 'improvement', id: string) =>
    activeTree && !(activeTree.kind === kind && activeTree.id === id) ? activeTree : null;

  const clearOtherTrees = async (keepKind: 'pack' | 'improvement', keepId: string) => {
    for (const v of vegetation) {
      if (!v.installed) continue;
      if (keepKind === 'improvement' && v.id === keepId) continue;
      await removeImp(v.id);
    }
    if (keepKind === 'improvement' && currentPack) {
      const r = await bridge.treesRestore();
      if (!r.success) throw new Error(r.errorMessage
        ?? t('environment.treesRestoreFail', 'Не удалось снять установленные деревья.'));
      refreshPack();
    }
  };

  const stepPhoto = useCallback((delta: number) => {
    setLightbox(lb => {
      if (!lb) return lb;
      const n = (lb.videoUrl ? 1 : 0) + lb.photos.length;
      if (n < 2) return lb;
      return { ...lb, index: (lb.index + delta + n) % n };
    });
  }, []);

  useEffect(() => {
    if (!lightbox) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight') stepPhoto(1);
      else if (e.key === 'ArrowLeft') stepPhoto(-1);
      else if (e.key === 'Escape') setLightbox(null);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [lightbox, stepPhoto]);

  const openId = lightbox?.id;
  useEffect(() => {
    if (!lightbox) return;
    for (const src of lightbox.photos) { const im = new Image(); im.src = src; }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openId]);

  useEffect(() => {
    let cancelled = false;
    if (typeof bridge.donorPickCounts !== 'function') return;
    bridge.donorPickCounts(TREES_INSTALL_NS)
      .then(m => { if (!cancelled) setPicks(m ?? {}); })
      .catch(() => {  });
    return () => { cancelled = true; };
  }, []);

  const dispatchInstall = async (id: string, name: string) => {
    if (installingId || impBusyId) return;
    if (!ensureBackupOrGate()) return;
    setInstallingId(id);
    try {
      await clearOtherTrees('pack', id);
      const r = await bridge.treesInstall(id, name);
      if (r.success) {
        refreshPack();
        setToast({ tone: 'success', message: t('environment.treesInstalledToast', 'Деревья «{{name}}» установлены.', { name }) });
        void bridge.activityLog('trees_install', `деревья «${name}»`);
        setPicks(p => ({ ...p, [id]: (p[id] ?? 0) + 1 }));
        if (typeof bridge.donorPickIncrement === 'function')
          void bridge.donorPickIncrement(id, TREES_INSTALL_NS).catch(() => {  });
        setLightbox(null);
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setInstallingId(null);
    }
  };

  const dispatchImprovement = async (id: string, name: string) => {
    if (impBusyId || installingId) return;
    try { await clearOtherTrees('improvement', id); }
    catch (e) { setToast({ tone: 'error', message: (e as Error).message }); return; }
    const r = await installImp(id);
    if (r.success) {
      setToast({
        tone: 'success',
        message: t('environment.graphicsInstalledToast', 'Мод «{{name}}» установлен.', { name }),
      });
      setLightbox(null);
    } else if (r.errorMessage) {
      setToast({ tone: 'error', message: r.errorMessage });
    }
  };

  return (
    <div className="relative h-full flex flex-col">
      <header className="px-8 pt-6 pb-3 shrink-0">
        <div className="max-w-[1280px] mx-auto flex items-center gap-3 flex-wrap">
          <BackButton onClick={() => navigate('environment')} label={t('environment.back', 'К окружению')} />
          <div className="min-w-0">
            <h1 className="text-[20px] font-semibold tracking-tight text-text-primary flex items-center gap-2">
              <Trees size={18} className="text-accent" />
              {t('environment.trees', 'Деревья')}
            </h1>
            <p className="text-[12px] text-text-muted">
              {t('environment.treesSub', 'Растительность и листва')}
            </p>
          </div>
        </div>
      </header>

      <div className="flex-1 overflow-y-auto px-8 pb-12">
        <div className="max-w-[1280px] mx-auto pt-2">
          <div className="grid grid-cols-1 sm:grid-cols-2 2xl:grid-cols-3 gap-5">
            {tiles.map(tile => {
              if (tile.kind === 'pack') {
                const { pack } = tile;
                const photos = photosFor(pack.id, pack.photos, pack.skipFirst);
                const lastIndex = photos.length - 1;
                return (
                  <TreeCard
                    key={`pack:${pack.id}`}
                    name={pack.name}
                    shot={photos[lastIndex]}
                    sizeLabel={formatSize(pack.sizeBytes)}
                    installCount={picks[pack.id] ?? 0}
                    onClick={() => setLightbox({
                      kind: 'pack', id: pack.id, name: pack.name,
                      description: t(pack.descKey, pack.descRu), photos, index: lastIndex,
                    })}
                  />
                );
              }
              const x = tile.item;
              const photos = [x.previewUrl, ...x.galleryUrls].filter(Boolean);
              return (
                <TreeCard
                  key={`improvement:${x.id}`}
                  name={x.name}
                  shot={photos[0] ?? ''}
                  sizeLabel={formatSize(x.sizeBytes)}
                  installCount={x.popularity}
                  onClick={() => setLightbox({
                    kind: 'improvement', id: x.id, name: x.name,
                    description: x.description, videoUrl: x.videoUrl, photos, index: 0,
                  })}
                />
              );
            })}
          </div>

        </div>
      </div>

      <AnimatePresence>
        {lightbox && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/85 backdrop-blur-sm p-6"
            onClick={() => setLightbox(null)}
          >
            <button
              type="button"
              onClick={() => setLightbox(null)}
              aria-label={t('environment.close', 'Закрыть')}
              className="absolute top-4 right-4 text-white/80 hover:text-white transition-colors"
              style={{ outline: 'none' }}
            >
              <X size={26} />
            </button>
            <div
              className="relative w-[92vw] h-[86vh] flex items-center justify-center"
              onClick={e => e.stopPropagation()}
            >
              {lightbox.videoUrl && lightbox.index === 0 ? (
                <div className="relative w-full max-w-[min(92vw,1280px)] aspect-[16/9] max-h-full
                                rounded-xl overflow-hidden bg-black shadow-2xl">
                  <VideoPlayer
                    url={lightbox.videoUrl}
                    poster={lightbox.photos[0]}
                    title={lightbox.name}
                  />
                </div>
              ) : (
                <img
                  src={lightbox.photos[lightbox.index - (lightbox.videoUrl ? 1 : 0)]}
                  alt={lightbox.name}
                  draggable={false}
                  className="max-w-full max-h-full object-contain rounded-xl shadow-2xl select-none"
                />
              )}

              {(lightbox.videoUrl ? 1 : 0) + lightbox.photos.length > 1 && (
                <>
                  <motion.button
                    type="button"
                    onClick={e => { e.stopPropagation(); stepPhoto(-1); }}
                    aria-label={t('environment.prevPhoto', 'Предыдущее фото')}
                    whileTap={{ scale: 0.92 }}
                    whileHover={{ scale: 1.06 }}
                    transition={{ type: 'spring', stiffness: 400, damping: 22 }}
                    className="absolute left-2 sm:left-4 top-0 bottom-0 my-auto z-10 w-12 h-12 rounded-full
                               flex items-center justify-center bg-black/50 backdrop-blur-md border border-white/25
                               text-white/90 hover:text-white hover:bg-accent hover:border-accent
                               transition-colors shadow-2xl"
                    style={{ outline: 'none' }}
                  >
                    <ChevronLeft size={26} />
                  </motion.button>
                  <motion.button
                    type="button"
                    onClick={e => { e.stopPropagation(); stepPhoto(1); }}
                    aria-label={t('environment.nextPhoto', 'Следующее фото')}
                    whileTap={{ scale: 0.92 }}
                    whileHover={{ scale: 1.06 }}
                    transition={{ type: 'spring', stiffness: 400, damping: 22 }}
                    className="absolute right-2 sm:right-4 top-0 bottom-0 my-auto z-10 w-12 h-12 rounded-full
                               flex items-center justify-center bg-black/50 backdrop-blur-md border border-white/25
                               text-white/90 hover:text-white hover:bg-accent hover:border-accent
                               transition-colors shadow-2xl"
                    style={{ outline: 'none' }}
                  >
                    <ChevronRight size={26} />
                  </motion.button>
                  <div
                    className="absolute top-2 left-1/2 -translate-x-1/2 z-10 px-3 py-1 rounded-full
                               bg-black/55 backdrop-blur-md text-white/90 text-[12px] font-semibold tabular-nums"
                  >
                    {lightbox.index + 1} / {(lightbox.videoUrl ? 1 : 0) + lightbox.photos.length}
                  </div>
                </>
              )}
            </div>

            {(() => {
              const imp = lightbox.kind === 'improvement'
                ? vegetation.find(v => v.id === lightbox.id) ?? null
                : null;
              const installed = lightbox.kind === 'improvement'
                ? !!imp?.installed
                : currentPack?.id === lightbox.id;
              const busy = lightbox.kind === 'improvement'
                ? impBusyId === lightbox.id
                : installingId === lightbox.id;
              const blocked = impBusyId !== null || installingId !== null;
              const replaces = installed ? null : replacedBy(lightbox.kind, lightbox.id);

              return (
                <div
                  onClick={e => e.stopPropagation()}
                  className="absolute bottom-5 left-1/2 -translate-x-1/2 flex items-center gap-3
                             px-4 py-2 rounded-xl bg-white/10 backdrop-blur-sm max-w-[92vw]"
                >
                  <div className="flex flex-col leading-tight min-w-0">
                    <span className="text-white text-sm font-semibold truncate">{lightbox.name}</span>
                    {lightbox.description && (
                      <span className="text-white/60 text-[11px] truncate">{lightbox.description}</span>
                    )}
                    {replaces && (
                      <span className="text-amber-300 text-[11px] flex items-center gap-1.5 mt-0.5">
                        <RefreshCw size={11} strokeWidth={2.2} />
                        {t('environment.treesWillReplace', 'Заменит «{{name}}» - деревья ставятся по одному.',
                          { name: replaces.name })}
                      </span>
                    )}
                  </div>
                  <button
                    type="button"
                    disabled={blocked || installed}
                    onClick={() => (lightbox.kind === 'improvement'
                      ? void dispatchImprovement(lightbox.id, lightbox.name)
                      : void dispatchInstall(lightbox.id, lightbox.name))}
                    className={
                      'shrink-0 inline-flex items-center gap-2 h-9 px-4 rounded-lg ' +
                      'bg-bg-elevated/70 border border-white/[0.10] text-text-primary ' +
                      'text-[12px] font-bold uppercase tracking-wider transition-all ' +
                      'disabled:opacity-50 disabled:cursor-not-allowed ' +
                      (installed
                        ? ''
                        : 'hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)] hover:shadow-glow-accent')
                    }
                    style={{ outline: 'none' }}
                  >
                    {busy ? <AccentLoader size={12} />
                      : installed ? <Check size={12} strokeWidth={3} className="text-accent" />
                      : replaces ? <RefreshCw size={12} className="text-accent" />
                      : <Download size={12} className="text-accent" />}
                    {busy ? t('environment.installing', 'Устанавливаю…')
                      : installed ? t('catalog.installedBadge', 'установлено')
                      : replaces ? t('improvements.replace', 'Заменить')
                      : t('environment.install', 'Установить')}
                  </button>
                </div>
              );
            })()}
          </motion.div>
        )}
      </AnimatePresence>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 3500}
      />
    </div>
  );
}

function TreeCard({
  name, shot, sizeLabel, installCount, onClick,
}: {
  name: string;
  shot: string;
  sizeLabel: string;
  installCount: number;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  const [failed, setFailed] = useState(false);

  return (
    <button
      type="button"
      onClick={onClick}
      className={`group relative w-full ${ENV_CARD_H} overflow-hidden rounded-2xl bg-bg-elevated text-left
                 transform-gpu will-change-transform
                 border border-transparent hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                 shadow-z2 hover:shadow-glow-accent
                 transition-[transform,box-shadow,border-color] duration-500 ease-smooth
                 hover:-translate-y-1
                 focus-visible:outline-none focus-visible:shadow-glow-accent
                 focus-visible:border-[color-mix(in_srgb,var(--accent)_60%,transparent)]`}
    >
      {!failed ? (
        <img
          src={shot}
          alt=""
          aria-hidden="true"
          draggable={false}
          loading="lazy"
          onError={() => setFailed(true)}
          className="absolute inset-0 w-full h-full object-cover select-none
                     transform-gpu transition-transform duration-[1100ms] ease-smooth
                     group-hover:scale-[1.05]"
          style={{ backfaceVisibility: 'hidden' }}
        />
      ) : (
        <div className="absolute inset-0 bg-gradient-to-br from-bg-elevated to-bg-base flex items-center justify-center">
          <Trees size={48} strokeWidth={1.2} className="text-white/15" />
        </div>
      )}

      <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/10 to-black/20 pointer-events-none" />

      <div
        className="absolute top-3 left-3 z-10 inline-flex items-center gap-1.5
                   px-2 py-1 rounded-md bg-base-70 backdrop-blur-sm
                   text-[11px] font-bold tabular-nums text-white/90"
        title={t('environment.sizeTitle', { defaultValue: 'Размер: {{size}}', size: sizeLabel })}
      >
        <HardDrive size={11} className="text-accent" strokeWidth={2.2} />
        <span>{sizeLabel}</span>
      </div>

      {installCount > 0 && (
        <div
          className="absolute top-3 right-3 z-10 inline-flex items-center gap-1.5
                     px-2 py-1 rounded-md bg-base-70 backdrop-blur-sm
                     text-[11px] font-bold tabular-nums text-white/90"
          title={t('environment.installCountTitle', { defaultValue: 'Установок: {{n}}', n: installCount })}
        >
          <Download size={11} className="text-accent" strokeWidth={2.2} />
          <span>{installCount}</span>
        </div>
      )}

      <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
        <span
          className="w-14 h-14 rounded-full bg-black/45 backdrop-blur-md border border-white/25
                     flex items-center justify-center shadow-2xl
                     opacity-0 scale-90 group-hover:opacity-100 group-hover:scale-100
                     group-focus-visible:opacity-100 group-focus-visible:scale-100
                     transition-[opacity,transform,background-color,border-color] duration-300
                     group-hover:bg-accent group-hover:border-accent"
        >
          <Eye size={20} className="text-white group-hover:text-text-on-accent" />
        </span>
      </div>

      <div className="absolute inset-x-0 bottom-0 p-4 z-10">
        <div className="font-display font-bold text-white text-lg uppercase tracking-wide truncate
                        drop-shadow-[0_2px_8px_rgba(0,0,0,0.6)]">
          {name}
        </div>
      </div>
    </button>
  );
}
