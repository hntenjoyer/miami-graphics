import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Download, CheckCircle2, AlertTriangle, Loader2, Trash2,
  Map as MapIcon, ExternalLink, RefreshCcw,
  Move3d,
} from 'lucide-react';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import { BackButton } from '@/components/BackButton';
import { Toast } from '@/components/Toast';
import { useBigMapStore } from '@/store/bigMapStore';
import { useInstallProgressStore } from '@/store/installProgressStore';
import { ensureBackupOrGate } from '@/store/installGate';
import { bridge } from '@/bridge';
import { BigMapReviewsSection } from './BigMapReviewsSection';
import { BigMap3DViewer } from './BigMap3DViewer';
import { bigMapVectorPreview } from './bigmapPreviews';

const detailContainer: Variants = {
  hidden: { opacity: 1 },
  visible: { opacity: 1, transition: { delayChildren: 0.05, staggerChildren: 0.07 } },
};
const detailItem: Variants = {
  hidden: { opacity: 0, y: 14 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.45, ease: [0.22, 1, 0.36, 1] } },
};

function extractYouTubeId(url: string): string | null {
  const m = url.match(/(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^"&?\/\s]{11})/);
  return m ? m[1] : null;
}

interface Props {
  onBack: () => void;
}

export function BigMapDetail({ onBack }: Props) {
  const { t } = useTranslation();

  const map          = useBigMapStore(s => s.selectedMap);
  const state        = useBigMapStore(s => s.state);
  const installing   = useBigMapStore(s => s.installing);
  const uninstalling = useBigMapStore(s => s.uninstalling);
  const install      = useBigMapStore(s => s.install);
  const uninstall    = useBigMapStore(s => s.uninstall);
  const refreshState = useBigMapStore(s => s.refreshState);
  const loadList     = useBigMapStore(s => s.loadList);

  useEffect(() => {
    void loadList();
    void refreshState();
  }, [loadList, refreshState]);

  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [show3D, setShow3D] = useState(false);

  useEffect(() => {
    if (!map?.id) return;
    bridge.bigMapPreviewGlb(map.id).catch(() => {  });
  }, [map?.id]);

  const progressEntry = useInstallProgressStore(s => s.byId['redux:bigmap']);
  const busy = installing || uninstalling;

  const isThisInstalled  = !!map && state.enabled && state.id === map.id;
  const isOtherInstalled = !!map && state.enabled && state.id !== null && state.id !== map.id;

  const onInstall = async () => {
    if (!map || busy) return;
    if (!ensureBackupOrGate()) return;
    try {
      const r = await install(map.id);
      if (r.success) {
        setToast({ tone: 'success', message: t('bigmap.installedToast', 'Карта «{{name}}» установлена.', { name: map.name }) });
        void bridge.activityLog('bigmap_install', `большую карту «${map.name}»`);
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      } else {
        setToast({ tone: 'error', message: t('bigmap.failToast', 'Не удалось установить карту.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    }
  };

  const onUninstall = async () => {
    if (busy) return;
    if (!ensureBackupOrGate()) return;
    try {
      const r = await uninstall();
      if (r.success) {
        setToast({ tone: 'success', message: t('bigmap.removedToast', 'Карта убрана - вернулась стандартная.') });
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      } else {
        setToast({ tone: 'error', message: t('bigmap.removeFailToast', 'Не удалось убрать карту.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    }
  };

  const photos = useMemo(() => {
    const all = [map?.previewUrl, ...(map?.galleryUrls ?? [])].filter(
      (u): u is string => !!u && u.trim().length > 0,
    );
    return Array.from(new Set(all)).slice(0, 7);
  }, [map]);
  const hero = (map ? bigMapVectorPreview(map.id) : null) ?? photos[0] ?? null;
  const videoId = map?.videoUrl ? extractYouTubeId(map.videoUrl) : null;

  if (!map) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-text-muted gap-2">
        <AlertTriangle size={36} className="opacity-40" />
        <p className="text-sm">{t('bigmap.notFound', 'Карта не найдена.')}</p>
        <button onClick={onBack} className="text-accent text-sm hover:underline">
          {t('bigmap.backToCatalog', 'Назад в каталог')}
        </button>
      </div>
    );
  }

  const sizeM = (map.sizeBytes / (1024 * 1024)).toFixed(0);

  const notice = isThisInstalled
    ? null
    : isOtherInstalled
      ? {
          tone: 'accent' as const,
          title: t('bigmap.noticeReplaceTitle', 'Уже установлена другая карта'),
          text: t(
            'bigmap.noticeReplaceText',
            'Сейчас установлена «{{name}}». Установка этой карты заменит её на новую.',
            { name: state.name ?? '' },
          ),
        }
      : state.foreignDetected
        ? {
            tone: 'warning' as const,
            title: t('bigmap.noticeForeignTitle', 'Карта установлена не через лаунчер'),
            text: t(
              'bigmap.noticeForeignText',
              'Сейчас в игре стоит большая карта, поставленная вручную (не через Miami Graphics). Установка отсюда заменит её на выбранную.',
            ),
          }
        : null;

  return (
    <motion.div
      className="h-full flex flex-col"
      variants={detailContainer}
      initial="hidden"
      animate="visible"
    >
      <motion.div className="shrink-0 px-8 pt-5 pb-4" variants={detailItem}>
        <BackButton onClick={onBack} label={t('common.back')} className="mb-4" />

        <div className="flex items-end justify-between gap-4 flex-wrap">
          <div className="min-w-0">
            <span className="text-[10px] uppercase tracking-wider text-text-muted">
              {sizeM} MB · {map.downloadCount} {t('bigmap.downloadsLabel', 'установок')}
            </span>
            <h1 className="mt-1 font-display text-3xl lg:text-4xl font-bold text-text-primary uppercase tracking-wide leading-tight">
              {map.name}
            </h1>
          </div>

          <div className="flex flex-col items-end gap-2 shrink-0">
            {isThisInstalled ? (
              <div className="flex items-center gap-2">
                <span className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl
                                 bg-green-500/20 text-green-300 text-sm font-medium">
                  <CheckCircle2 size={14} /> {t('bigmap.installed', 'Установлена')}
                </span>
                <button
                  type="button"
                  onClick={() => void onUninstall()}
                  disabled={busy}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-red-500/15 text-red-300 hover:bg-red-500/25
                             border border-red-500/30 hover:border-red-500/50
                             disabled:opacity-60 transition-colors text-sm font-medium"
                >
                  {uninstalling ? <Loader2 size={14} className="animate-spin" /> : <Trash2 size={14} />}
                  <span>{uninstalling ? t('bigmap.removing', 'Убираем…') : t('bigmap.remove', 'Убрать карту')}</span>
                </button>
              </div>
            ) : (
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => void onInstall()}
                  disabled={busy}
                  className="inline-flex items-center gap-2 px-5 h-11 rounded-xl
                             bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                             hover:bg-bg-elevated/75 hover:border-white/[0.18]
                             disabled:opacity-50 disabled:cursor-wait
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {installing
                    ? <Loader2 size={16} className="animate-spin" />
                    : isOtherInstalled ? <RefreshCcw size={16} /> : <Download size={16} />}
                  <span>
                    {installing
                      ? t('bigmap.installing', 'Установка…')
                      : isOtherInstalled
                        ? t('bigmap.replace', 'Заменить')
                        : t('bigmap.install', 'Установить')}
                  </span>
                </button>
                {state.enabled && (
                  <button
                    type="button"
                    onClick={() => void onUninstall()}
                    disabled={busy}
                    title={t('bigmap.removeHint', 'Убрать текущую карту и вернуть стандартную')}
                    className="inline-flex items-center gap-2 px-4 h-11 rounded-xl
                               bg-red-500/15 text-red-300 hover:bg-red-500/25
                               border border-red-500/30 hover:border-red-500/50
                               disabled:opacity-60 transition-colors text-sm font-medium"
                    style={{ outline: 'none' }}
                  >
                    {uninstalling ? <Loader2 size={14} className="animate-spin" /> : <Trash2 size={14} />}
                    <span>{uninstalling ? t('bigmap.removing', 'Убираем…') : t('bigmap.remove', 'Убрать карту')}</span>
                  </button>
                )}
              </div>
            )}
            {busy && progressEntry && progressEntry.phase !== 'done' && progressEntry.phase !== 'error' && (
              <span className="text-[11px] tabular-nums text-text-muted">
                {progressEntry.detailMessage ?? t('bigmap.installing', 'Установка…')}
                {progressEntry.percent > 0 ? ` · ${Math.round(progressEntry.percent)}%` : ''}
              </span>
            )}
          </div>
        </div>
      </motion.div>

      <div className="flex-1 min-h-0 overflow-y-auto [scrollbar-gutter:stable]">
        <div className="px-8 pt-2 pb-8 flex flex-col min-h-full">

          {notice && (
            <motion.div
              variants={detailItem}
              className="shrink-0 mb-5 flex items-start gap-3 rounded-2xl px-4 py-3"
              style={{
                background: notice.tone === 'warning'
                  ? 'color-mix(in srgb, var(--status-warning) 14%, var(--bg-elevated))'
                  : 'color-mix(in srgb, var(--accent) 13%, var(--bg-elevated))',
                border: notice.tone === 'warning'
                  ? '1px solid color-mix(in srgb, var(--status-warning) 42%, transparent)'
                  : '1px solid color-mix(in srgb, var(--accent) 40%, transparent)',
              }}
            >
              {notice.tone === 'warning' && (
                <span className="shrink-0 mt-0.5" style={{ color: 'var(--status-warning)' }}>
                  <AlertTriangle size={17} />
                </span>
              )}
              <div className="flex-1 min-w-0">
                <div className="text-[13px] font-bold text-text-primary leading-tight">
                  {notice.title}
                </div>
                <div className="text-[12px] text-text-secondary leading-snug mt-1">
                  {notice.text}
                </div>
              </div>
            </motion.div>
          )}

          <motion.section variants={detailItem} className="w-full">
            {hero ? (
              <button
                type="button"
                onClick={() => setShow3D(true)}
                title={t('bigmap.view3d', '3D-просмотр')}
                style={{ outline: 'none', background: '#0a0e14' }}
                className="group relative block w-full h-[min(62vh,720px)] min-h-[380px]
                           rounded-2xl overflow-hidden
                           border border-glass-border
                           hover:border-accent/55 hover:shadow-z2
                           transition-[border-color,box-shadow] duration-300 ease-smooth"
              >
                <img
                  src={hero}
                  alt=""
                  draggable={false}
                  loading="lazy"
                  onError={e => (e.currentTarget.style.display = 'none')}
                  className="absolute inset-0 w-full h-full object-contain object-center select-none
                             transition-transform duration-700 ease-smooth group-hover:scale-[1.04]"
                />
                <span
                  aria-hidden
                  className="pointer-events-none absolute bottom-4 right-4 inline-flex items-center gap-2
                             px-4 h-11 rounded-xl
                             bg-black/60 backdrop-blur-md text-white border border-white/[0.16]
                             group-hover:bg-accent/80 group-hover:border-accent
                             transition-colors text-sm font-bold uppercase tracking-wider"
                >
                  <Move3d size={16} />
                  {t('bigmap.view3d', '3D-просмотр')}
                </span>
              </button>
            ) : (
              <div className="relative w-full h-[min(62vh,720px)] min-h-[380px] rounded-2xl
                              bg-bg-elevated border border-glass-border
                              flex flex-col items-center justify-center gap-4">
                <MapIcon size={44} className="text-text-muted opacity-25" />
                <button
                  type="button"
                  onClick={() => setShow3D(true)}
                  className="inline-flex items-center gap-2 px-4 h-11 rounded-xl
                             bg-bg-base/70 text-text-primary border border-white/[0.1]
                             hover:bg-accent/80 hover:text-white hover:border-accent
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  <Move3d size={16} />
                  {t('bigmap.view3d', '3D-просмотр')}
                </button>
              </div>
            )}

            {map.description ? (
              <p className="mt-5 text-sm leading-relaxed text-text-secondary whitespace-pre-line">
                {map.description}
              </p>
            ) : (
              <p className="mt-5 text-sm text-text-muted italic">
                {t('bigmap.noDescription', 'Описание не добавлено.')}
              </p>
            )}
          </motion.section>

          {map.videoUrl && (
            <motion.section className="mt-8" variants={detailItem}>
              <div className="flex items-center gap-2 mb-3">
                <span className="text-[11px] uppercase tracking-[0.22em] text-text-muted font-bold">
                  {t('bigmap.video', 'Видео')}
                </span>
              </div>
              {videoId ? (
                <div className="relative w-full max-w-[860px] aspect-video rounded-2xl overflow-hidden
                                border border-glass-border bg-black">
                  <iframe
                    src={`https://www.youtube.com/embed/${videoId}`}
                    title={map.name}
                    className="absolute inset-0 w-full h-full"
                    allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                    allowFullScreen
                  />
                </div>
              ) : (
                <a
                  href={map.videoUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="inline-flex items-center gap-2 text-sm text-accent hover:underline"
                >
                  <ExternalLink size={13} />
                  {map.videoUrl}
                </a>
              )}
            </motion.section>
          )}
        </div>

        <motion.section className="px-8 pt-8 pb-10" variants={detailItem}>
          <BigMapReviewsSection mapId={map.id} />
        </motion.section>
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 4000}
      />

      <AnimatePresence>
        {show3D && (
          <BigMap3DViewer
            mapId={map.id}
            mapName={map.name}
            previewSrc={hero}
            onClose={() => setShow3D(false)}
          />
        )}
      </AnimatePresence>
    </motion.div>
  );
}
