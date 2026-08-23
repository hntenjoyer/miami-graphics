import { useEffect, useMemo } from 'react';
import { useTranslation, Trans } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  X, Trophy, Mouse, Keyboard, Monitor, Headphones, Gauge,
  FileText, Layers, Crosshair, ExternalLink,
} from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import type { UserBuild } from '@/store/userBuildsStore';
import { LazyEmbedVideo, videoSlotForUrl, type VideoSlot } from '@/utils/videoEmbeds';

interface Props {
  build:    UserBuild;
  onClose:  () => void;
}

export function CommunityBuildDetail({ build, onClose }: Props) {
  const { t } = useTranslation();

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  const videoSlot = useMemo<VideoSlot | null>(() => {
    if (!build.videoUrl) return null;
    return videoSlotForUrl(build.videoUrl);
  }, [build.videoUrl]);

  return (
    <AnimatePresence>
      <motion.div
        key="scrim"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        transition={{ duration: 0.18 }}
        onClick={onClose}
        className="fixed inset-0 z-50 bg-black/55 backdrop-blur-[2px] flex items-center justify-center p-6"
      >
        <motion.div
          key="sheet"
          initial={{ opacity: 0, scale: 0.97, y: 12 }}
          animate={{ opacity: 1, scale: 1, y: 0 }}
          exit={{ opacity: 0, scale: 0.97, y: 12 }}
          transition={{ duration: 0.24, ease: EASE_DEPTH }}
          onClick={(e) => e.stopPropagation()}
          className="w-full max-w-[720px] max-h-[88vh] overflow-y-auto"
        >
          <GlassPanel depth="z3" tint="strong" rounded="3xl" className="overflow-hidden">
            {}
            <header className="px-7 pt-6 pb-5 flex items-start gap-4 border-b border-border-subtle">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 mb-2">
                  {build.tier !== null && (
                    <span className={
                      'inline-flex items-center gap-1 px-2 h-5 rounded-md text-[10px] uppercase tracking-wider font-bold ' +
                      (build.tier === 1
                        ? 'bg-accent/20 text-accent border border-accent/30'
                        : 'bg-bg-elevated-soft text-text-secondary border border-border-subtle')
                    }>
                      <Trophy size={9} /> {t('players.tierLabel', 'TIER')} {build.tier}
                    </span>
                  )}
                  <code className="text-[10px] text-text-muted font-mono">{build.hntCode}</code>
                </div>
                <h1 className="font-display text-[26px] font-bold text-text-primary tracking-tight leading-tight">
                  {build.name}
                </h1>
                <p className="mt-1 text-[13px] text-text-muted">
                  <Trans
                    i18nKey="players.detail.byAuthor"
                    defaults="от <author>{{name}}</author>"
                    values={{ name: build.author }}
                    components={{ author: <span className="text-text-secondary" /> }}
                  />
                </p>
              </div>
              <button
                type="button"
                onClick={onClose}
                aria-label={t('common.close', 'Закрыть')}
                className="shrink-0 inline-flex items-center justify-center w-9 h-9 rounded-xl
                           text-text-muted hover:text-text-primary hover:bg-glass-strong transition-colors"
              >
                <X size={16} />
              </button>
            </header>

            {}
            {videoSlot && (
              <div className="aspect-video bg-black overflow-hidden">
                {videoSlot.kind === 'embed' ? (
                  <LazyEmbedVideo
                    slot={videoSlot}
                    title={build.name}
                    className="w-full h-full"
                  />
                ) : (
                  <video
                    src={videoSlot.url}
                    controls
                    preload="metadata"
                    playsInline
                    className="w-full h-full object-contain"
                  />
                )}
              </div>
            )}

            {}
            <section className="px-7 py-4 grid grid-cols-2 gap-x-6 gap-y-2 border-b border-border-subtle">
              <Ref icon={<Layers     size={12} />} label={t('players.submit.reduxLabel', 'Редукс')}  value={build.reduxNameSnapshot} />
              <Ref icon={<Crosshair  size={12} />} label={t('players.submit.gunpackLabel', 'Ган-пак')} value={build.gunpackNameSnapshot} />
            </section>

            {}
            {(build.devices.mouse || build.devices.keyboard || build.devices.monitor || build.devices.headset) && (
              <section className="px-7 py-4 grid grid-cols-2 gap-x-6 gap-y-2 border-b border-border-subtle">
                {build.devices.mouse    && <Ref icon={<Mouse      size={12} />} label={t('players.submit.mouse', 'Мышь')}       value={build.devices.mouse.name} />}
                {build.devices.keyboard && <Ref icon={<Keyboard   size={12} />} label={t('players.submit.keyboard', 'Клавиатура')} value={build.devices.keyboard.name} />}
                {build.devices.monitor  && <Ref icon={<Monitor    size={12} />} label={t('players.submit.monitor', 'Монитор')}
                                                value={`${build.devices.monitor.name}${build.devices.monitor.hz ? ` · ${build.devices.monitor.hz} Hz` : ''}`} />}
                {build.devices.headset  && <Ref icon={<Headphones size={12} />} label={t('players.submit.headset', 'Гарнитура')}  value={build.devices.headset.name} />}
              </section>
            )}

            {}
            {(build.sensitivity !== null || build.dpi !== null || build.resolution) && (
              <section className="px-7 py-4 flex items-center gap-6 text-[13px] text-text-secondary border-b border-border-subtle">
                {build.sensitivity !== null && (
                  <span className="inline-flex items-center gap-1.5">
                    <Gauge size={12} className="text-text-muted" />
                    <span className="text-[10px] uppercase tracking-wider text-text-muted">{t('common.sensitivity', 'Чувств.')}</span>
                    <span className="tabular-nums text-text-primary">{build.sensitivity}</span>
                  </span>
                )}
                {build.dpi !== null && (
                  <span className="inline-flex items-center gap-1.5">
                    <span className="text-[10px] uppercase tracking-wider text-text-muted">DPI</span>
                    <span className="tabular-nums text-text-primary">{build.dpi}</span>
                  </span>
                )}
                {build.resolution && (
                  <span className="inline-flex items-center gap-1.5">
                    <Monitor size={12} className="text-text-muted" />
                    <span className="tabular-nums text-text-primary">{build.resolution}</span>
                  </span>
                )}
              </section>
            )}

            {}
            {build.description && (
              <section className="px-7 py-4 border-b border-border-subtle">
                <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-1.5">{t('common.description', 'Описание')}</div>
                <p className="text-[13px] text-text-secondary leading-[1.65] whitespace-pre-line">
                  {build.description}
                </p>
              </section>
            )}

            {}
            {build.settingsXmlUrl && (
              <section className="px-7 py-4">
                <a
                  href={build.settingsXmlUrl}
                  target="_blank" rel="noopener noreferrer"
                  className="inline-flex items-center gap-2 px-3.5 h-9 rounded-lg
                             bg-bg-elevated-soft hover:bg-bg-elevated
                             border border-border-subtle hover:border-border-strong
                             text-[12px] text-text-secondary hover:text-text-primary
                             transition-colors"
                >
                  <FileText size={12} />
                  {t('players.detail.openSettingsXml', 'Открыть settings.xml')}
                  <ExternalLink size={11} />
                </a>
              </section>
            )}
          </GlassPanel>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}

function Ref({ icon, label, value }: {
  icon: React.ReactNode; label: string; value: string;
}) {
  return (
    <div className="flex items-baseline gap-2 min-w-0">
      <span className="shrink-0 text-text-muted">{icon}</span>
      <span className="text-[10px] uppercase tracking-wider text-text-muted shrink-0">{label}</span>
      <span className="text-[13px] text-text-primary truncate" title={value}>{value || '-'}</span>
    </div>
  );
}
