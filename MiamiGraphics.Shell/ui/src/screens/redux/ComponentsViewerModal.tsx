import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  X, ShieldCheck, Building2, Map as MapIcon, Crosshair, Wind, Droplet, Cloud,
  Eye, Slash, ImageOff, Image as ImageIcon, Box,
  type LucideIcon,
} from 'lucide-react';
import { GlassPanel, EASE_DEPTH, CarbonSurface, AccentLoader } from '@/design';
import { prefetchGlb } from '@/utils/glbPrefetch';
import type { ComponentInfo } from '@/bridge/types';
import { GlbViewerModal } from '../guns/GlbViewerModal';
import { ArmorPreview3D } from '../armor/ArmorPreview3D';

const COMPONENT_ORDER = [
  'armor', 'arena', 'minimap', 'crosshair', 'tracers', 'bloodfx', 'timecycle',
] as const;

type ComponentKey = typeof COMPONENT_ORDER[number];

const COMPONENT_ICONS: Record<ComponentKey, LucideIcon> = {
  armor:     ShieldCheck,
  arena:     Building2,
  minimap:   MapIcon,
  crosshair: Crosshair,
  tracers:   Wind,
  bloodfx:   Droplet,
  timecycle: Cloud,
};

const COMPONENT_LABELS_RU: Record<ComponentKey, string> = {
  armor:     'Броник',
  arena:     'Арена',
  minimap:   'Минимапа',
  crosshair: 'Прицел',
  tracers:   'Трейсера',
  bloodfx:   'Кровь',
  timecycle: 'Таймцикл',
};

const COMPONENT_DESCRIPTIONS: Record<ComponentKey, string> = {
  armor:     'Кастомная модель брони',
  arena:     'Кастомная арена / гонки',
  minimap:   'Перекраска и иконки мини-карты',
  crosshair: 'Свой прицел',
  tracers:   'Кастомные трейсера',
  bloodfx:   'Эффект попадания / крови',
  timecycle: 'Цветокор и атмосфера',
};

interface Props {
  components: Record<string, ComponentInfo | undefined>;
  componentScreenshots?: Record<string, string>;
  reduxName: string;
  onClose: () => void;
}

export function ComponentsViewerModal({
  components, componentScreenshots, reduxName, onClose,
}: Props) {
  const { t } = useTranslation();

  const [glbPreview, setGlbPreview] = useState<{ key: ComponentKey; url: string | null } | null>(null);
  const [lightbox, setLightbox] = useState<{ key: ComponentKey; url: string } | null>(null);

  const previewUrls = COMPONENT_ORDER
    .filter(k => k !== 'armor')
    .map(k => (componentScreenshots ?? {})[k] ?? '')
    .filter(Boolean);

  const [loadedCount, setLoadedCount] = useState(0);
  const photosReady = loadedCount >= previewUrls.length;

  useEffect(() => {
    if (previewUrls.length === 0) return;
    let alive = true;
    const tick = () => { if (alive) setLoadedCount(c => c + 1); };
    const imgs = previewUrls.map(url => {
      const img = new Image();
      img.onload = tick;
      img.onerror = tick;
      img.src = url;
      return img;
    });
    return () => {
      alive = false;
      imgs.forEach(img => { img.onload = null; img.onerror = null; });
    };

  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      if (lightbox)        { setLightbox(null);   return; }
      if (glbPreview)      { setGlbPreview(null); return; }
      onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose, lightbox, glbPreview]);

  return (
    <>
      <motion.div
        key="components-backdrop"
        className="fixed inset-0 z-40 flex items-center justify-center p-6"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        transition={{ duration: 0.28, ease: EASE_DEPTH }}
        onClick={onClose}
        style={{
          background: 'rgba(0, 0, 0, 0.55)',
          backdropFilter: 'blur(28px) saturate(160%)',
          WebkitBackdropFilter: 'blur(28px) saturate(160%)',
        }}
      >
          <motion.div
            initial={{ opacity: 0, scale: 0.96, y: 14 }}
            animate={{ opacity: 1, scale: 1,    y: 0 }}
            exit   ={{ opacity: 0, scale: 0.94, y: 12 }}
            transition={{ duration: 0.32, ease: EASE_DEPTH }}

            className="w-full max-w-[600px]"
            onClick={(e) => e.stopPropagation()}
            style={{
              filter: [
                'drop-shadow(0 0 0 rgba(255,255,255,0.10))',
                'drop-shadow(0 1px 0 rgba(255,255,255,0.08))',
                'drop-shadow(0 12px 28px rgba(0,0,0,0.45))',
                'drop-shadow(0 36px 72px rgba(0,0,0,0.55))',
              ].join(' '),
            }}
          >
            <GlassPanel
              depth="z3"
              tint="ultra"
              highlight
              edge
              rounded="3xl"
              className="relative overflow-hidden flex flex-col gap-4 max-h-[85vh]"
              style={{ padding: '22px 22px 20px' }}
            >
              <motion.span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
                initial={{ opacity: 0, scaleX: 0.4 }}
                animate={{ opacity: 1, scaleX: 1 }}
                transition={{ duration: 0.6, ease: EASE_DEPTH, delay: 0.1 }}
              />
              <span
                aria-hidden
                className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
                style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
              />

              <header className="relative flex items-start justify-between gap-4">
                <div className="min-w-0">
                  <h2 className="text-[18px] font-semibold text-text-primary tracking-[-0.01em] truncate">
                    {t('redux.componentsModalTitle')}
                  </h2>
                  <p className="text-[11.5px] text-text-secondary mt-1 leading-snug">
                    {t('redux.componentsModalSubtitle')}
                  </p>
                </div>
                <button
                  type="button"
                  onClick={onClose}
                  aria-label={t('common.back', 'Назад')}
                  className="shrink-0 w-8 h-8 rounded-full flex items-center justify-center
                             bg-white/[0.06] text-text-secondary hover:text-text-primary
                             hover:bg-white/[0.12] transition-colors duration-150"
                  style={{ outline: 'none' }}
                >
                  <X size={14} strokeWidth={2.5} />
                </button>
              </header>

              <div className="relative">
              <motion.div

                className="overflow-y-auto -mr-2 pr-2 px-0.5 py-1 flex flex-col gap-2"
                initial="hidden"
                animate="show"
                variants={{
                  hidden: { opacity: 0 },
                  show:   { opacity: 1, transition: { staggerChildren: 0.05, delayChildren: 0.08 } },
                }}
                style={{
                  filter: photosReady ? 'none' : 'blur(14px) saturate(120%)',
                  transition: 'filter 0.45s cubic-bezier(0.22, 1, 0.36, 1)',
                  pointerEvents: photosReady ? 'auto' : 'none',
                }}
              >
                {COMPONENT_ORDER.map((key, i) => {
                  const info = components[key];
                  const Icon = COMPONENT_ICONS[key];
                  const included = info?.isFound === true;
                  const screenshotUrl = (componentScreenshots ?? {})[key] ?? '';
                  const description = t(
                    `redux.componentDescription.${key}`,
                    COMPONENT_DESCRIPTIONS[key]);

                  return (
                    <ComponentListRow
                      key={key}
                      rowIndex={i}
                      icon={Icon}
                      label={t(`redux.componentLabel.${key}`, COMPONENT_LABELS_RU[key])}
                      description={description}
                      included={included}
                      isArmor={key === 'armor'}
                      armorGlbUrl={key === 'armor' ? (info?.glbUrl ?? null) : null}
                      screenshotUrl={screenshotUrl}
                      onOpen3D={
                        key === 'armor'
                          ? () => setGlbPreview({ key, url: info?.glbUrl ?? null })
                          : null
                      }
                      onOpenScreenshot={
                        key !== 'armor' && screenshotUrl
                          ? () => setLightbox({ key, url: screenshotUrl })
                          : null
                      }
                    />
                  );
                })}
              </motion.div>

              <AnimatePresence>
                {!photosReady && (
                  <motion.div
                    key="components-loader"
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    exit   ={{ opacity: 0 }}
                    transition={{ duration: 0.3, ease: EASE_DEPTH }}
                    className="absolute inset-0 z-10 flex flex-col items-center justify-center gap-3 pointer-events-none"
                  >
                    <AccentLoader size={28} />
                    <span className="text-[11px] uppercase tracking-[0.22em] text-text-muted font-semibold">
                      {t('redux.previewLoading', 'Загружаем превью…')}
                    </span>
                  </motion.div>
                )}
              </AnimatePresence>
              </div>
            </GlassPanel>
          </motion.div>
      </motion.div>

      {}
      {glbPreview && (
        <GlbViewerModal
          glbUrl={glbPreview.url}
          title={t('redux.componentViewerTitle', {
            defaultValue: '{{component}} - {{name}}',
            component: t(`redux.componentLabel.${glbPreview.key}`, COMPONENT_LABELS_RU[glbPreview.key]),
            name: reduxName,
          })}
          subjectKind="armor"
          onClose={() => setGlbPreview(null)}
        />
      )}

      {}
      <AnimatePresence>
        {lightbox && (
          <ScreenshotLightbox
            key={lightbox.key + ':' + lightbox.url}
            url={lightbox.url}
            title={t('redux.componentViewerTitle', {
              defaultValue: '{{component}} - {{name}}',
              component: t(`redux.componentLabel.${lightbox.key}`, COMPONENT_LABELS_RU[lightbox.key]),
              name: reduxName,
            })}
            onClose={() => setLightbox(null)}
          />
        )}
      </AnimatePresence>
    </>
  );
}

function ComponentListRow({
  rowIndex, icon: Icon, label, description, included, isArmor, armorGlbUrl, screenshotUrl,
  onOpen3D, onOpenScreenshot,
}: {
  rowIndex: number;
  icon: LucideIcon;
  label: string;
  description: string;
  included: boolean;
  isArmor: boolean;
  armorGlbUrl: string | null;
  screenshotUrl: string;
  onOpen3D: (() => void) | null;
  onOpenScreenshot: (() => void) | null;
}) {
  const { t } = useTranslation();
  const [imageBroken, setImageBroken] = useState(false);
  const showImageThumb = !!screenshotUrl && !imageBroken;
  const showArmor3DThumb = isArmor && included && !!armorGlbUrl && !showImageThumb;

  return (
    <motion.div
      variants={{
        hidden: { opacity: 0, x: -16, scale: 0.98 },
        show:   { opacity: 1, x: 0,   scale: 1 },
      }}
      transition={{ duration: 0.34, ease: EASE_DEPTH, delay: rowIndex * 0.02 }}
      whileHover={included ? { y: -1, scale: 1.005 } : undefined}
      className={
        'flex items-stretch gap-3 p-2 rounded-xl border transition-colors will-change-transform ' +
        (included
          ? 'bg-bg-elevated/40 border-white/[0.08] hover:border-white/[0.18]'
          : 'bg-bg-elevated/20 border-white/[0.05] opacity-60')
      }
    >
      {}
      <div className="relative shrink-0 w-[112px] h-[68px] rounded-lg overflow-hidden">
        <CarbonSurface
          weaveOpacity={0.45}
          glowIntensity={included ? 0.5 : 0.25}
          vignetteIntensity={0.55}
        />
        {showArmor3DThumb ? (
          <div className="absolute inset-0">
            <ArmorPreview3D glbUrl={armorGlbUrl} withBackground={false} />
          </div>
        ) : showImageThumb ? (
          <img
            src={screenshotUrl}
            alt=""
            onError={() => setImageBroken(true)}
            className="absolute inset-0 w-full h-full object-cover"
          />
        ) : (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-0.5 text-text-muted/70">
            <ImageOff size={16} strokeWidth={1.5} />
            <span className="text-[8.5px] uppercase tracking-wider">
              {included
                ? (isArmor
                    ? t('redux.thumbNo3d', 'нет 3D')
                    : t('redux.thumbNoPhoto', 'нет фото'))
                : t('redux.thumbNotIncluded', 'не входит')}
            </span>
          </div>
        )}
      </div>

      {}
      <div className="flex-1 min-w-0 flex items-center gap-3">
        <div className={
          'shrink-0 w-9 h-9 rounded-lg flex items-center justify-center ' +
          (included ? 'bg-white/[0.08] text-text-primary' : 'bg-white/[0.04] text-text-muted')
        }>
          <Icon size={16} />
        </div>
        <div className="min-w-0 flex flex-col">
          <span className="text-sm font-semibold text-text-primary truncate">{label}</span>
          <span className="text-[11.5px] text-text-muted truncate" title={description}>
            {included ? description : t('redux.componentNotIncluded', 'Не входит в этот редукс')}
          </span>
        </div>
      </div>

      {}
      <div className="shrink-0 flex items-center">
        {isArmor ? (
          <motion.button
            type="button"
            onClick={included ? (onOpen3D ?? undefined) : undefined}
            onPointerEnter={() => included && prefetchGlb(armorGlbUrl)}
            onFocus={() => included && prefetchGlb(armorGlbUrl)}
            disabled={!included}
            whileHover={included ? { scale: 1.04 } : undefined}
            whileTap={included ? { scale: 0.96 } : undefined}
            transition={{ type: 'spring', stiffness: 360, damping: 22 }}
            className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg
                       bg-bg-elevated/55 text-text-primary text-xs font-semibold
                       border border-white/[0.08] hover:bg-bg-elevated/75 hover:border-white/[0.18]
                       transition-colors
                       disabled:opacity-35 disabled:cursor-not-allowed"
            style={{ outline: 'none' }}
            title={included
              ? t('redux.open3dTitle', 'Открыть 3D-модель')
              : t('redux.armorNotIncludedTitle', 'Броник не входит в этот редукс')}
          >
            <Box size={12} />
            3D
          </motion.button>
        ) : onOpenScreenshot ? (
          <motion.button
            type="button"
            onClick={onOpenScreenshot}
            whileHover={{ scale: 1.04, y: -1 }}
            whileTap={{ scale: 0.96 }}
            transition={{ type: 'spring', stiffness: 360, damping: 22 }}
            className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg
                       bg-bg-elevated/55 text-text-primary text-xs font-semibold
                       border border-white/[0.08] hover:bg-bg-elevated/75 hover:border-white/[0.18]
                       transition-colors"
            style={{ outline: 'none' }}
            title={t('redux.openPhotoFullTitle', 'Открыть фото в полный размер')}
          >
            <Eye size={12} />
            {t('redux.viewPhoto', 'Посмотреть')}
          </motion.button>
        ) : (
          <span className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg
                           bg-white/[0.04] border border-white/[0.05] text-[11px] text-text-muted">
            <Slash size={10} />
            {t('redux.noPhotoBadge', 'Нет фото')}
          </span>
        )}
      </div>
    </motion.div>
  );
}

function ScreenshotLightbox({
  url, title, onClose,
}: {
  url: string;
  title: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [origin, setOrigin] = useState<{ x: number; y: number }>({ x: 50, y: 50 });
  const [zoomed, setZoomed] = useState(false);

  const onImageClick = (e: React.MouseEvent<HTMLImageElement>) => {
    if (zoomed) { setZoomed(false); return; }
    const rect = e.currentTarget.getBoundingClientRect();
    const x = ((e.clientX - rect.left) / rect.width) * 100;
    const y = ((e.clientY - rect.top) / rect.height) * 100;
    setOrigin({ x, y });
    setZoomed(true);
  };

  return (
    <motion.div
      key="screenshot-lightbox"
      className="fixed inset-0 z-[100] bg-black/75 backdrop-blur-glass-ultra
                 backdrop-saturate-liquid flex items-center justify-center p-6"

      initial={{ opacity: 0, pointerEvents: 'none' as const }}
      animate={{ opacity: 1, pointerEvents: 'auto' as const }}
      exit={{ opacity: 0, pointerEvents: 'none' as const }}
      transition={{ duration: 0.24, ease: [0.22, 1, 0.36, 1] }}
      onClick={onClose}
    >
      {}
      <motion.div
        className="relative w-[min(90vw,1280px)] aspect-[16/9] max-h-[85vh] rounded-3xl overflow-hidden
                   bg-glass-ultra"

        initial={{ opacity: 0, scale: 0.92, y: 24, rotateX: -6 }}
        animate={{ opacity: 1, scale: 1,    y: 0,  rotateX: 0 }}
        exit   ={{ opacity: 0, scale: 0.96, y: 12, rotateX: 4 }}
        transition={{
          opacity:  { duration: 0.22, ease: [0.22, 1, 0.36, 1] },
          rotateX:  { duration: 0.42, ease: [0.22, 1, 0.36, 1] },
          default:  { type: 'spring', stiffness: 300, damping: 28, mass: 0.8 },
        }}
        style={{ transformPerspective: 1200 }}
        onClick={(e) => e.stopPropagation()}
      >
        {}
        <div className="absolute top-0 inset-x-0 z-20 px-4 py-3 flex items-center gap-3
                        bg-gradient-to-b from-black/55 to-transparent pointer-events-none">
          <ImageIcon size={14} className="text-white/80 shrink-0" />
          <h2 className="text-sm font-semibold text-white truncate flex-1
                         drop-shadow-[0_2px_4px_rgba(0,0,0,0.8)]">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label={t('catalog.closeEsc', 'Закрыть (Esc)')}
            className="pointer-events-auto w-9 h-9 rounded-lg flex items-center justify-center
                       text-white/85 bg-black/50 hover:bg-black/80 hover:text-white
                       transition-colors"
          >
            <X size={16} />
          </button>
        </div>

        {}
        <div className="absolute inset-0">
          <CarbonSurface weaveOpacity={0.7} glowIntensity={0.5} vignetteIntensity={0.6} />
          <img
            src={url}
            alt=""
            aria-hidden
            className="absolute inset-0 w-full h-full object-cover scale-110"
            style={{
              filter: 'blur(32px) saturate(120%) brightness(0.55)',
              transform: 'scale(1.15)',
            }}
          />
          <motion.img
            key={url}
            src={url}
            alt=""
            onClick={onImageClick}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit   ={{ opacity: 0 }}
            transition={{ opacity: { duration: 0.32, ease: [0.22, 1, 0.36, 1] } }}
            className="absolute inset-0 w-full h-full object-contain will-change-transform select-none"
            draggable={false}
            style={{
              cursor: zoomed ? 'zoom-out' : 'zoom-in',
              transform: zoomed ? 'scale(2.4)' : 'scale(1)',
              transformOrigin: `${origin.x}% ${origin.y}%`,
              transition: 'transform 0.45s cubic-bezier(0.22, 1, 0.36, 1)',
            }}
          />
        </div>
      </motion.div>
    </motion.div>
  );
}
