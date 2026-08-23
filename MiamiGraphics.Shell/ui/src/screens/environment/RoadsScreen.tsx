import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { AnimatePresence, motion } from 'framer-motion';
import {
  X, Route, Eye, Download, ChevronLeft, ChevronRight,
  HardDrive, AlertTriangle, ShieldCheck, Check,
} from 'lucide-react';
import type { RoadsFixStatus } from '@/bridge/types';
import { bridge } from '@/bridge';
import { useNavStore } from '@/store/navStore';
import { BackButton } from '@/components/BackButton';
import { AccentLoader, GlassPanel, EASE_DEPTH, ENV_CARD_H } from '@/design';
import { ensureBackupOrGate } from '@/store/installGate';
import { Toast } from '@/components/Toast';

const ROAD_PACKS: ReadonlyArray<{
  id: string; name: string; photos?: number; skipFirst?: boolean; coverPhoto?: number; sizeBytes: number;
}> = [
  { id: 'la1', name: 'LA ROADS',   photos: 8, sizeBytes: 2_741_265_920 },
  { id: 'la2', name: 'LA ROADS 2', photos: 8, sizeBytes: 2_195_779_584 },
  { id: 'eu1', name: 'EU ROADS',   photos: 8, sizeBytes: 3_592_171_008 },
  { id: 'eu2', name: 'EU ROADS 2', photos: 8, sizeBytes: 3_361_652_224, coverPhoto: 3 },
];

const formatSize = (bytes: number, t: TFunction): string =>
  t('common.sizeGB', { defaultValue: '{{value}} ГБ', value: (bytes / 1024 ** 3).toFixed(1) });

const ROAD_SHOT_BASE = 'https://miamigraphicsstorage.uk/environment/roads';
const shotFor = (id: string) => `${ROAD_SHOT_BASE}/${id}.webp`;
const shotN = (id: string, n: number) => (n <= 1 ? shotFor(id) : `${ROAD_SHOT_BASE}/${id}_${n}.webp`);
const photosFor = (id: string, count = 1, skipFirst = false): string[] => {
  const start = skipFirst ? 2 : 1;
  const end = Math.max(count, start);
  const urls = Array.from({ length: end - start + 1 }, (_, i) => shotN(id, start + i));
  return urls.length ? urls : [shotFor(id)];
};

const ROADS_INSTALL_NS = 'roads_install';

export function RoadsScreen() {
  const { t } = useTranslation();
  const navigate = useNavStore(s => s.requestNavigate);

  const [picks, setPicks] = useState<Record<string, number>>({});
  const [lightbox, setLightbox] = useState<{ id: string; name: string; photos: string[]; index: number } | null>(null);
  const [installingId, setInstallingId] = useState<string | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error' | 'info'; message: string } | null>(null);
  const [fixModalOpen, setFixModalOpen] = useState(false);
  const [fixStatus, setFixStatus] = useState<RoadsFixStatus | null>(null);
  const [applyingFix, setApplyingFix] = useState(false);

  const stepPhoto = useCallback((delta: number) => {
    setLightbox(lb => {
      if (!lb || lb.photos.length < 2) return lb;
      const n = lb.photos.length;
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
    bridge.donorPickCounts(ROADS_INSTALL_NS)
      .then(m => { if (!cancelled) setPicks(m ?? {}); })
      .catch(() => {  });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    let cancelled = false;
    if (typeof bridge.getRoadsFixStatus !== 'function') return;
    bridge.getRoadsFixStatus()
      .then(s => { if (!cancelled) setFixStatus(s); })
      .catch(() => {  });
    return () => { cancelled = true; };
  }, []);

  const dispatchInstall = async (id: string, name: string) => {
    if (installingId) return;
    if (!ensureBackupOrGate()) return;
    setInstallingId(id);
    try {
      const r = await bridge.roadsInstall(id, name);
      if (r.success) {
        setToast({ tone: 'success', message: t('environment.roadsInstalledToast', 'Дороги «{{name}}» установлены.', { name }) });
        void bridge.activityLog('roads_install', `дороги «${name}»`);
        setPicks(p => ({ ...p, [id]: (p[id] ?? 0) + 1 }));
        if (typeof bridge.donorPickIncrement === 'function')
          void bridge.donorPickIncrement(id, ROADS_INSTALL_NS).catch(() => {  });
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

  const applyFix = async () => {
    if (applyingFix) return;
    if (typeof bridge.roadsFixApply !== 'function') return;
    setApplyingFix(true);
    try {
      const r = await bridge.roadsFixApply();
      if (r.success) {
        const s = typeof bridge.getRoadsFixStatus === 'function' ? await bridge.getRoadsFixStatus() : null;
        if (s) setFixStatus(s);
        setToast({ tone: 'success', message: t('environment.roadsFixApplied', 'Фикс применён. Если GTA запущена - перезапусти её.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('environment.roadsFixFailed', 'Не удалось применить фикс.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setApplyingFix(false);
    }
  };

  return (
    <div className="relative h-full flex flex-col">
      <header className="px-8 pt-6 pb-3 shrink-0">
        <div className="max-w-[1280px] mx-auto flex items-center gap-3 flex-wrap">
          <BackButton onClick={() => navigate('environment')} label={t('environment.back', 'К окружению')} />
          <div className="min-w-0">
            <h1 className="text-[20px] font-semibold tracking-tight text-text-primary flex items-center gap-2">
              <Route size={18} className="text-accent" />
              {t('environment.roads', 'Дороги')}
            </h1>
            <p className="text-[12px] text-text-muted">
              {t('environment.roadsSub', 'Покрытие и разметка')}
            </p>
          </div>
        </div>
      </header>

      <div className="flex-1 overflow-y-auto px-8 pb-12">
        <div className="max-w-[1280px] mx-auto pt-2">
          <div className="grid grid-cols-1 sm:grid-cols-2 2xl:grid-cols-3 gap-5">
            {ROAD_PACKS.map(pack => {
              const photos = photosFor(pack.id, pack.photos, pack.skipFirst);
              const coverIndex = Math.min((pack.coverPhoto ?? 2) - 1, photos.length - 1);
              return (
                <RoadCard
                  key={pack.id}
                  name={pack.name}
                  shot={photos[coverIndex]}
                  sizeLabel={formatSize(pack.sizeBytes, t)}
                  installCount={picks[pack.id] ?? 0}
                  fixApplied={fixStatus?.applied ?? false}
                  onClick={() => setLightbox({ id: pack.id, name: pack.name, photos, index: coverIndex })}
                  onWarningClick={() => setFixModalOpen(true)}
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
              <img
                src={lightbox.photos[lightbox.index]}
                alt={lightbox.name}
                draggable={false}
                className="max-w-full max-h-full object-contain rounded-xl shadow-2xl select-none"
              />

              {lightbox.photos.length > 1 && (
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
                    {lightbox.index + 1} / {lightbox.photos.length}
                  </div>
                </>
              )}
            </div>

            <div
              onClick={e => e.stopPropagation()}
              className="absolute bottom-5 left-1/2 -translate-x-1/2 flex items-center gap-3
                         px-4 py-2 rounded-xl bg-white/10 backdrop-blur-sm"
            >
              <span className="text-white text-sm font-semibold">{lightbox.name}</span>
              <button
                type="button"
                disabled={installingId !== null}
                onClick={() => void dispatchInstall(lightbox.id, lightbox.name)}
                className="inline-flex items-center gap-2 h-9 px-4 rounded-lg
                           bg-bg-elevated/70 text-text-primary border border-white/[0.10]
                           hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)] hover:shadow-glow-accent
                           text-[12px] font-bold uppercase tracking-wider transition-all
                           disabled:opacity-50 disabled:cursor-not-allowed"
                style={{ outline: 'none' }}
              >
                {installingId === lightbox.id
                  ? <AccentLoader size={12} />
                  : <Download size={12} className="text-accent" />}
                {installingId === lightbox.id
                  ? t('environment.installing', 'Устанавливаю…')
                  : t('environment.install', 'Установить')}
              </button>
            </div>
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

      <RoadsGraphicsFixModal
        open={fixModalOpen}
        onClose={() => setFixModalOpen(false)}
        status={fixStatus}
        applying={applyingFix}
        onApply={() => void applyFix()}
      />
    </div>
  );
}

function RoadCard({
  name, shot, sizeLabel, installCount, fixApplied, onClick, onWarningClick,
}: {
  name: string;
  shot: string;
  sizeLabel: string;
  installCount: number;
  fixApplied: boolean;
  onClick: () => void;
  onWarningClick: () => void;
}) {
  const { t } = useTranslation();
  const [failed, setFailed] = useState(false);

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onClick}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onClick(); } }}
      className={`group relative w-full ${ENV_CARD_H} overflow-hidden rounded-2xl bg-bg-elevated text-left cursor-pointer
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
          <Route size={48} strokeWidth={1.2} className="text-white/15" />
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

      <div className="absolute top-3 right-3 z-10 flex items-center gap-1.5">
        {installCount > 0 && (
          <div
            className="inline-flex items-center gap-1.5 px-2 py-1 rounded-md bg-base-70 backdrop-blur-sm
                       text-[11px] font-bold tabular-nums text-white/90"
            title={t('environment.installCountTitle', { defaultValue: 'Установок: {{n}}', n: installCount })}
          >
            <Download size={11} className="text-accent" strokeWidth={2.2} />
            <span>{installCount}</span>
          </div>
        )}
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); onWarningClick(); }}
          aria-label={fixApplied
            ? t('environment.roadsFixActiveAria', 'Фикс жёлтых дорог активен (AF 16x)')
            : t('environment.roadsFixAskAria', 'Дороги вдали выглядят жёлтыми? Как исправить')}
          title={fixApplied
            ? t('environment.roadsFixActiveTitle', 'Фикс жёлтых дорог активен (анизотропная фильтрация 16x). Нажми для деталей')
            : t('environment.roadsFixAskTitle', 'Дороги вдали выглядят жёлтыми? Нажми, чтобы исправить')}
          className={`inline-flex items-center justify-center w-7 h-7 rounded-md
                      bg-base-70 backdrop-blur-sm border transition-colors hover:bg-black/60 ${
                        fixApplied
                          ? 'border-emerald-400/40 text-emerald-400 hover:text-emerald-300 hover:border-emerald-400/70'
                          : 'border-amber-400/30 text-amber-400 hover:text-amber-300 hover:border-amber-400/60'
                      }`}
          style={{ outline: 'none' }}
        >
          {fixApplied
            ? <ShieldCheck size={14} strokeWidth={2.2} />
            : <AlertTriangle size={14} strokeWidth={2.2} />}
        </button>
      </div>

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
    </div>
  );
}

function RoadsGraphicsFixModal({ open, onClose, status, applying, onApply }: {
  open: boolean;
  onClose: () => void;
  status: RoadsFixStatus | null;
  applying: boolean;
  onApply: () => void;
}) {
  const { t } = useTranslation();
  const applied = status?.applied ?? false;
  const detectable = status?.detectable ?? false;
  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[105] flex items-center justify-center p-6"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.2 }}
        >
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm" onClick={onClose} />

          <motion.div
            aria-hidden
            initial={{ opacity: 0 }}
            animate={{ opacity: 0.4 }}
            transition={{ duration: 0.55, ease: EASE_DEPTH, delay: 0.05 }}
            className="absolute pointer-events-none w-[560px] h-[560px] blur-3xl"
            style={{ background: 'radial-gradient(circle at 50% 50%, #f5a524 0%, transparent 65%)' }}
          />

          <motion.div
            className="relative w-full max-w-[560px] max-h-[88vh]"
            initial={{ opacity: 0, scale: 0.94, y: 12, filter: 'blur(8px)' }}
            animate={{ opacity: 1, scale: 1,    y: 0,  filter: 'blur(0px)' }}
            exit   ={{ opacity: 0, scale: 0.94, y: 12, filter: 'blur(8px)' }}
            transition={{ duration: 0.35, ease: EASE_DEPTH }}
          >
            <GlassPanel
              depth="z3" tint="ultra" rounded="3xl" highlight edge
              className="relative overflow-hidden border border-white/[0.08] max-h-[88vh] overflow-y-auto"
            >
              <button
                type="button"
                onClick={onClose}
                aria-label={t('environment.close', 'Закрыть')}
                className="absolute top-4 right-4 z-10 inline-flex items-center justify-center w-8 h-8 rounded-lg
                           text-text-muted hover:text-text-primary hover:bg-glass transition-colors"
                style={{ outline: 'none' }}
              >
                <X size={16} />
              </button>

              <div className="px-7 pt-6 pb-5 border-b border-glass-border flex items-center gap-4">
                <div className={`w-12 h-12 shrink-0 rounded-2xl flex items-center justify-center border ${
                  applied ? 'border-emerald-400/25 bg-emerald-400/10' : 'border-amber-400/25 bg-amber-400/10'
                }`}>
                  {applied
                    ? <ShieldCheck size={20} className="text-emerald-400" />
                    : <AlertTriangle size={20} className="text-amber-400" />}
                </div>
                <div className="flex-1 min-w-0 pr-6">
                  <h2 className="font-display text-[20px] font-bold text-text-primary tracking-tight leading-tight">
                    {applied
                      ? t('environment.roadsFixModalTitleApplied', 'Фикс жёлтых дорог')
                      : t('environment.roadsFixModalTitleAsk', 'Дороги вдали выглядят жёлтыми?')}
                  </h2>
                  <p className="text-[13px] text-text-secondary mt-1 leading-relaxed">
                    {t('environment.roadsFixModalDesc', 'Настройка анизотропной фильтрации 16x для GTA V - без неё дороги на дистанции жёлтые.')}
                  </p>
                </div>
              </div>

              <div className="px-5 pt-5 pb-6 flex flex-col gap-4">
                {applied ? (
                  <div className="flex items-start gap-2.5 px-4 py-3.5 rounded-2xl
                                  bg-emerald-500/10 border border-emerald-400/30 text-[13.5px] text-text-primary leading-relaxed">
                    <Check size={17} className="mt-0.5 shrink-0 text-emerald-400" strokeWidth={2.6} />
                    <div>
                      <span className="font-bold">{t('environment.roadsFixAppliedBold', 'Фикс уже внедрён в настройки вашей видеокарты.')}</span>{' '}
                      <span className="text-text-secondary">{t('environment.roadsFixAppliedRest', 'Можете спокойно устанавливать дороги.')}</span>
                    </div>
                  </div>
                ) : detectable ? (
                  <>
                    <div className="flex items-start gap-2.5 px-4 py-3.5 rounded-2xl
                                    bg-amber-500/10 border border-amber-400/30 text-[13.5px] text-text-primary leading-relaxed">
                      <AlertTriangle size={17} className="mt-0.5 shrink-0 text-amber-400" strokeWidth={2.4} />
                      <div>
                        <span className="font-bold">{t('environment.roadsFixNeededBold', 'Вам необходимо внедрить фикс в настройки вашей видеокарты.')}</span>{' '}
                        <span className="text-text-secondary">{t('environment.roadsFixNeededRest', 'Без него дороги вдали будут жёлтыми. Лаунчер настроит всё сам - нажмите кнопку ниже.')}</span>
                      </div>
                    </div>
                    <button
                      type="button"
                      onClick={onApply}
                      disabled={applying}
                      className="self-start inline-flex items-center gap-2.5 h-11 px-6 rounded-2xl
                                 bg-[color-mix(in_srgb,var(--accent-soft)_55%,transparent)]
                                 border border-[color-mix(in_srgb,var(--accent)_35%,transparent)]
                                 hover:border-[color-mix(in_srgb,var(--accent)_70%,transparent)] hover:shadow-glow-accent
                                 text-[13px] font-display font-bold uppercase tracking-[0.08em] text-text-primary
                                 transition-all disabled:opacity-60 disabled:cursor-not-allowed"
                      style={{ outline: 'none' }}
                    >
                      {applying
                        ? <AccentLoader size={14} />
                        : <ShieldCheck size={16} className="text-accent" />}
                      {applying
                        ? t('environment.roadsFixApplying', 'Применяю…')
                        : t('environment.roadsFixApplyBtn', 'Применить фикс')}
                    </button>
                  </>
                ) : (
                  <div className="flex items-start gap-2.5 px-4 py-3.5 rounded-2xl
                                  bg-bg-elevated/40 border border-glass-border text-[13px] text-text-secondary leading-relaxed">
                    <AlertTriangle size={16} className="mt-0.5 shrink-0 text-text-muted" strokeWidth={2.2} />
                    <div>
                      {t('environment.roadsFixManualNote', 'Не удалось автоматически определить настройки видеокарты. Убедитесь, что для GTA V включена анизотропная фильтрация 16x в панели управления драйвера.')}
                    </div>
                  </div>
                )}

                <button
                  type="button"
                  onClick={onClose}
                  className="self-start inline-flex items-center justify-center h-10 px-5 rounded-2xl
                             bg-glass hover:bg-glass-strong
                             border border-glass-border hover:border-border-strong
                             text-[12px] text-text-secondary hover:text-text-primary
                             transition-colors font-display font-bold uppercase tracking-[0.08em]"
                  style={{ outline: 'none' }}
                >
                  {applied ? t('common.gotIt', 'Понятно') : t('environment.close', 'Закрыть')}
                </button>
              </div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
