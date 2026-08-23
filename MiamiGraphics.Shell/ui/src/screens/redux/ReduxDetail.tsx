import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Download, ShieldCheck, Star, Shield,
  Sliders, HardDrive, Trash2, CheckCircle2,
  Loader2, Play, X,
  type LucideIcon,
} from 'lucide-react';
import { useSecurityStore } from '@/store/securityStore';
import { useReduxStore } from '@/store/reduxStore';
import { useReduxVersionsStore } from '@/store/reduxVersionsStore';
import { useSessionStore, useCanSeeTesterFeature } from '@/store/sessionStore';
import { useCustomizeStore } from '@/store/customizeStore';
import { useSubmitDraftStore } from '@/store/submitDraftStore';
import { useNavStore } from '@/store/navStore';
import { useAdminStore } from '@/store/adminStore';
import { Toast } from '@/components/Toast';
import { ConfirmModal } from '@/components/ConfirmModal';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { BackButton } from '@/components/BackButton';
import { ReviewsSection } from './ReviewsSection';
import { ComponentsViewerModal } from './ComponentsViewerModal';
import { Eye, Layers } from 'lucide-react';
import { useKeepOverlaysStore } from '@/store/keepOverlaysStore';
import { useDirtyConfirmStore } from '@/store/dirtyConfirmStore';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import { ArmorReplaceConfirmModal } from '../armor/ArmorReplaceConfirmModal';
import { bridge } from '@/bridge';
import type { CurrentArmorInfo, ReduxItem } from '@/bridge/types';
import { getArmorLibraryCache } from '@/store/armorLibraryCache';
import { LazyEmbedVideo, videoSlotForUrl, type VideoSlot } from '@/utils/videoEmbeds';

const detailContainer: Variants = {
  hidden: { opacity: 1 },
  visible: {
    opacity: 1,
    transition: {
      delayChildren: 0.05,
      staggerChildren: 0.07,
    },
  },
};
const detailItem: Variants = {
  hidden: { opacity: 0, y: 14 },
  visible: {
    opacity: 1,
    y: 0,
    transition: { duration: 0.45, ease: [0.22, 1, 0.36, 1] },
  },
};

const COMPONENT_LABELS_RU: Record<string, string> = {
  minimap:   'Минимапа',
  crosshair: 'Прицел',
  tracers:   'Трейсера',
  bloodfx:   'Эффекты',
  timecycle: 'Таймциклы',
  arena:     'Арена',
  armor:     'Броник',
};

export function ReduxDetail({ onPickForBuild, onBack }: {
  onPickForBuild?: (reduxId: string) => void;
  onBack?: () => void;
} = {}) {
  const { t } = useTranslation();
  const items = useReduxStore(s => s.items);
  const selectedId = useReduxStore(s => s.selectedId);
  const select = useReduxStore(s => s.select);
  const favorites = useReduxStore(s => s.favorites);
  const toggleFavorite = useReduxStore(s => s.toggleFavorite);
  const install = useReduxStore(s => s.install);
  const installForceClean = useReduxStore(s => s.installForceClean);
  const installPreserve = useReduxStore(s => s.installPreserve);
  const uninstall = useReduxStore(s => s.uninstall);
  const uninstallForceClean = useReduxStore(s => s.uninstallForceClean);
  const uninstallPreserve = useReduxStore(s => s.uninstallPreserve);
  const installedReduxId = useReduxStore(s => s.installedReduxId);
  const auth = useSessionStore(s => s.auth);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const canSeeSecurity = useCanSeeTesterFeature();
  const openCustomize = useCustomizeStore(s => s.open);

  const [installing, setInstalling] = useState(false);
  const [uninstalling, setUninstalling] = useState(false);
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [galleryIdx, setGalleryIdx] = useState(0);

  const [selectedVersionId, setSelectedVersionId] = useState<string | null>(null);

  const [componentsOpen, setComponentsOpen] = useState(false);

  const [armorReplacePrompt, setArmorReplacePrompt] = useState<CurrentArmorInfo | null>(null);
  const [minimapConflict, setMinimapConflict] = useState<string | null>(null);

  const priorOverlaysRef = useRef<{
    rings: number[];
    minimap: { id: string; name: string } | null;
    armor: { id: string; name: string } | null;
    fastJoin: boolean;
  }>({ rings: [], minimap: null, armor: null, fastJoin: false });

  const liveItem = useMemo(() => items.find(i => i.id === selectedId) ?? null, [items, selectedId]);
  const lastItemRef = useRef<typeof liveItem>(null);
  if (liveItem) lastItemRef.current = liveItem;
  const item = liveItem ?? lastItemRef.current;

  const reduxId = item?.id ?? null;
  const versions = useReduxVersionsStore(s => (reduxId ? s.byRedux[reduxId] : undefined));
  const loadVersions = useReduxVersionsStore(s => s.load);
  useEffect(() => {
    if (reduxId) void loadVersions(reduxId);
  }, [reduxId, loadVersions]);

  useEffect(() => { setSelectedVersionId(null); }, [reduxId]);

  const versionList = versions ?? [];
  const showVersionSelector = versionList.length > 1;

  const effectiveVersionId = selectedVersionId
    ?? (versionList.length > 0 ? versionList[0].id : null);

  const gallerySlots = useMemo<GallerySlot[]>(() => {
    if (!item) return [];
    const slots: GallerySlot[] = [];
    const rawVideo = (item.videoUrl ?? '').trim();
    if (rawVideo) {
      slots.push(videoSlotForUrl(rawVideo));
    }
    const imgs = [item.previewUrl, ...(item.galleryUrls ?? [])]
      .filter((x): x is string => typeof x === 'string' && x.length > 0);
    for (const url of new Set(imgs)) slots.push({ kind: 'image', url });
    return slots;
  }, [item]);

  const activeVersion = useMemo(
    () => versionList.find(v => v.id === effectiveVersionId) ?? versionList[0] ?? null,
    [versionList, effectiveVersionId],
  );

  const submitPickingFor = useSubmitDraftStore(s => s.pickingFor);
  const submitReturnTo   = useSubmitDraftStore(s => s.returnTo);
  const submitFinishPick = useSubmitDraftStore(s => s.finishPick);
  const reqNavigate      = useNavStore(s => s.requestNavigate);
  const reqAdminSection  = useAdminStore(s => s.requestSectionChange);
  const effComponents = activeVersion?.components ?? item?.components ?? {};
  const customizable = useMemo(() => {
    const seen = new Set<string>();
    const out: { name: string; label: string }[] = [];
    for (const [name, info] of Object.entries(effComponents)) {
      if (!info?.isFound) continue;
      const flags = info.flags ?? [];
      const c = flags.includes('replaceable') || flags.includes('importable') || flags.includes('transferable');
      if (!c) continue;
      const label = t(
        `customize.componentNames.${name}`,
        COMPONENT_LABELS_RU[name] ?? name.charAt(0).toUpperCase() + name.slice(1));
      if (seen.has(label)) continue;
      seen.add(label);
      out.push({ name, label });
    }
    return out;
  }, [effComponents, t]);

  if (!item) {

    return null;
  }

  const isFav       = favorites.has(item.id);
  const effSize     = activeVersion?.patchSizeBytes ?? item.patchSizeBytes;
  const sizeLabel   = formatSize(effSize);
  const downloads   = formatDownloads(item.downloadCount);
  const safeIdx     = Math.min(galleryIdx, Math.max(0, gallerySlots.length - 1));
  const activeSlot  = gallerySlots[safeIdx];

  const toastInstallResult = (r: { success: boolean; errorMessage: string | null }) => {
    if (r.success) {
      setToast({ tone: 'success', message: t('redux.installDone') });
      const ov = priorOverlaysRef.current;
      if (ov.rings.length > 0 || ov.minimap || ov.armor || ov.fastJoin) {
        useKeepOverlaysStore.getState().open({
          reduxName:  item.name || item.id,
          reduxThumb: item.componentScreenshots?.minimap || item.previewUrl || null,
          rings:      ov.rings,
          minimap:    ov.minimap,
          armor:      ov.armor,
          fastJoin:   ov.fastJoin,
        });
        priorOverlaysRef.current = { rings: [], minimap: null, armor: null, fastJoin: false };
      }
    } else if (r.errorMessage && r.errorMessage !== 'DIRTY_FILES_NEED_CONFIRM') {
      setToast({ tone: 'error', message: r.errorMessage });
    }
  };

  const reduxHasArmor = !!effComponents.armor?.isFound;

  const fireInstall = async () => {
    setInstalling(true);
    try {
      const r = await install(item.id, effectiveVersionId);
      if (!r.success && r.errorMessage === 'DIRTY_FILES_NEED_CONFIRM') openInstallDirtyConfirm();
      else toastInstallResult(r);
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setInstalling(false);
    }
  };

  const isReduxPickMode  = submitPickingFor === 'redux';
  const onUseInBuild = () => {
    if (!item) return;
    submitFinishPick('redux', item.id, item.name);
    if (submitReturnTo === 'admin') {

      reqAdminSection('proPlayers');
      reqNavigate('admin');
    } else {
      reqNavigate('players');
    }
  };

  const onInstall = async () => {
    if (installing) return;

    const priorRings = await bridge.minimapGetRangeRings().catch(() => [] as number[]);
    const curMap = await bridge.getCurrentMinimapInfo().catch(() => null);
    const curArmor = await bridge.getCurrentArmorInfo().catch(() => null);
    const priorFastJoin = typeof bridge.otherGetFastJoin === 'function'
      ? await bridge.otherGetFastJoin().catch(() => false)
      : false;
    priorOverlaysRef.current = {
      rings: priorRings,
      minimap: curMap && curMap.kind === 'library' ? { id: curMap.id, name: curMap.name } : null,
      armor: curArmor && curArmor.kind === 'library' ? { id: curArmor.id, name: curArmor.name } : null,
      fastJoin: priorFastJoin,
    };
    if (priorOverlaysRef.current.armor) {
      await bridge.reduxDeferArmorReapplyOnce().catch(() => {});
    }
    if (priorFastJoin) {
      await bridge.reduxDeferFastJoinReapplyOnce().catch(() => {});
    }

    if (reduxHasArmor) {
      try {
        const cur = await bridge.getCurrentArmorInfo();
        const sameSource = cur && cur.kind === 'redux' && cur.id === item.id;
        if (cur && !sameSource) {
          setArmorReplacePrompt(cur);
          return;
        }
      } catch (e) {
        console.warn('[redux.install] armor preflight failed:', e);
      }
    }

    try {
      const ownDonor = curMap && !(curMap.kind === 'redux' && curMap.id === item.id);
      let ownTweaks = false;
      if (typeof bridge.minimapGetTweaks === 'function') {
        const tw = await bridge.minimapGetTweaks().catch(() => null);
        ownTweaks = !!tw && Object.values(tw).some(v =>
          v !== null && v !== undefined && v !== false && v !== '');
      }
      if (ownDonor || ownTweaks) {
        setMinimapConflict(
          ownDonor && curMap?.name ? curMap.name : t('redux.minimapConflictYours', 'настроенная тобой'));
        return;
      }
    } catch (e) {
      console.warn('[redux.install] minimap preflight failed:', e);
    }

    void fireInstall();
  };

  const onMinimapKeepMine = () => {
    setMinimapConflict(null);
    void fireInstall();
  };

  const onMinimapTakeRedux = async () => {
    setMinimapConflict(null);
    await bridge.reduxDeferMinimapReapplyOnce().catch(() => {});
    void fireInstall();
  };

  const onArmorReplaceKeep = () => {
    setArmorReplacePrompt(null);
    setToast({ tone: 'success', message: t('armor.replaceToastKeepRedux') });
  };
  const onArmorReplaceInstall = () => {
    setArmorReplacePrompt(null);
    void fireInstall();
  };
  const onForceClean = async () => {
    setInstalling(true);
    try {
      const r = await installForceClean(item.id, effectiveVersionId);
      toastInstallResult(r);
    }
    catch (e) { setToast({ tone: 'error', message: (e as Error).message }); }
    finally { setInstalling(false); }
  };
  const onPreserve = async () => {
    setInstalling(true);
    try {
      const r = await installPreserve(item.id, effectiveVersionId);
      toastInstallResult(r);
    }
    catch (e) { setToast({ tone: 'error', message: (e as Error).message }); }
    finally { setInstalling(false); }
  };

  const openInstallDirtyConfirm = () => useDirtyConfirmStore.getState().open({
    title: t('redux.dirtyTitle'),
    message: t('redux.dirtyMessage'),
    cancelLabel: t('redux.dirtyCancel'),
    actions: [
      { label: t('redux.dirtyPreserve'),   hint: t('redux.dirtyPreserveHint'),   kind: 'accent',  run: onPreserve },
      { label: t('redux.dirtyForceClean'), hint: t('redux.dirtyForceCleanHint'), kind: 'neutral', run: onForceClean },
    ],
  });

  const isInstalled = installedReduxId === item.id;

  const toastUninstallResult = (r: { success: boolean; errorMessage: string | null }) => {
    if (r.success) {
      setToast({ tone: 'success', message: t('redux.uninstallDone') });
    } else if (r.errorMessage && r.errorMessage !== 'DIRTY_FILES_NEED_CONFIRM') {
      setToast({ tone: 'error', message: r.errorMessage });
    }
  };
  const onUninstall = async () => {
    if (uninstalling) return;
    setUninstalling(true);
    try {
      const r = await uninstall();
      if (!r.success && r.errorMessage === 'DIRTY_FILES_NEED_CONFIRM') openUninstallDirtyConfirm();
      else toastUninstallResult(r);
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setUninstalling(false);
    }
  };
  const onUninstallForceClean = async () => {
    setUninstalling(true);
    try { toastUninstallResult(await uninstallForceClean()); }
    catch (e) { setToast({ tone: 'error', message: (e as Error).message }); }
    finally { setUninstalling(false); }
  };
  const onUninstallPreserve = async () => {
    setUninstalling(true);
    try { toastUninstallResult(await uninstallPreserve()); }
    catch (e) { setToast({ tone: 'error', message: (e as Error).message }); }
    finally { setUninstalling(false); }
  };

  const openUninstallDirtyConfirm = () => useDirtyConfirmStore.getState().open({
    title: t('redux.uninstallDirtyTitle'),
    message: t('redux.uninstallDirtyMessage'),
    cancelLabel: t('redux.dirtyCancel'),
    actions: [
      { label: t('redux.uninstallDirtyPreserve'),   hint: t('redux.uninstallDirtyPreserveHint'),   kind: 'accent', run: onUninstallPreserve },
      { label: t('redux.uninstallDirtyForceClean'), hint: t('redux.uninstallDirtyForceCleanHint'), kind: 'danger', run: onUninstallForceClean },
    ],
  });

  return (
    <div className="h-full overflow-y-auto">
      {}
      <motion.div
        className="max-w-[1280px] 2xl:max-w-[1600px] mx-auto px-8 py-4 flex flex-col gap-4"
        variants={detailContainer}
        initial="hidden"
        animate="visible"
      >

        {}
        <motion.div className="flex items-start gap-4 flex-wrap" variants={detailItem}>
          <BackButton
            onClick={onBack ?? (() => select(null))}
            label={t('redux.back')}
            className="shrink-0 mt-1"
          />
          <div className="min-w-0 flex-1">
            <h1 className="font-display font-bold text-3xl uppercase tracking-wide text-text-primary leading-tight">
              {item.name || item.id}
            </h1>
            <div className="mt-1.5 flex items-center gap-3 text-sm text-text-secondary flex-wrap">
              <ReduxAuthorLine reduxId={item.id} fallback={item.author || '-'} />
              {item.isVerified && (
                <span className="inline-flex items-center gap-1 text-status-success" title={t('redux.verified')}>
                  <ShieldCheck size={14} />
                  {t('redux.verified')}
                </span>
              )}
            </div>
          </div>
          {canSeeSecurity && (
            <button
              type="button"
              onClick={() => {
                useSecurityStore.getState().setPreselect({ reduxId: item.id, name: item.name || item.id });
                useNavStore.getState().requestNavigate('security');
              }}
              className="shrink-0 w-10 h-10 rounded-xl flex items-center justify-center
                         text-text-muted hover:text-accent hover:bg-glass
                         transition-colors duration-200 border border-glass-border bg-glass mt-1"
              title={t('redux.checkLegitTitle', 'Проверить на легит')}
            >
              <Shield size={16} />
            </button>
          )}
          <button
            type="button"
            onClick={() => void toggleFavorite(item.id, userId)}
            className="shrink-0 w-10 h-10 rounded-xl flex items-center justify-center
                       text-text-muted hover:text-accent hover:bg-glass
                       transition-colors duration-200 border border-glass-border bg-glass mt-1"
            title={isFav ? t('redux.unfavorite') : t('redux.favorite')}
          >
            <Star size={16} fill={isFav ? 'currentColor' : 'none'} className={isFav ? 'text-accent' : ''} />
          </button>
        </motion.div>

        {}
        <motion.div className="flex flex-col lg:flex-row gap-4 items-stretch" variants={detailItem}>

          {}
          <div className="lg:flex-[7] flex flex-col gap-3 min-w-0">
            {gallerySlots.length > 0 && activeSlot ? (
              <>
                <div className="relative flex-1 min-h-[420px]">
                  <GlassPanel
                    depth="z3" tint="ultra" highlight rounded="3xl"
                    className="h-full w-full overflow-hidden"
                  >
                    <AnimatePresence mode="popLayout" initial={false}>
                      <motion.div
                        key={`${activeSlot.kind}:${activeSlot.url}`}
                        initial={{ opacity: 0, scale: 1.02 }}
                        animate={{ opacity: 1, scale: 1 }}
                        exit={{ opacity: 0, scale: 0.99 }}
                        transition={{ duration: 0.32, ease: EASE_DEPTH }}
                        className="absolute inset-0"
                      >
                        {activeSlot.kind === 'image' && (
                          <>
                            <img
                              src={activeSlot.url}
                              alt={item.name || item.id}
                              draggable={false}
                              className="absolute inset-0 w-full h-full object-cover object-center select-none"
                              style={{ imageRendering: 'auto' }}
                            />
                            {}
                            {}
                          </>
                        )}
                        {activeSlot.kind === 'video' && (

                          <video
                            key={activeSlot.url}
                            src={activeSlot.url}
                            controls
                            preload="metadata"
                            playsInline
                            className="absolute inset-0 w-full h-full object-contain bg-black"
                          />
                        )}
                        {activeSlot.kind === 'embed' && (
                          <LazyEmbedVideo
                            key={activeSlot.url}
                            slot={activeSlot}
                            title={item.name || item.id}
                            className="absolute inset-0 w-full h-full"
                          />
                        )}
                      </motion.div>
                    </AnimatePresence>
                  </GlassPanel>

                  {}
                  {gallerySlots.length > 1 && (
                    <div className="absolute top-4 right-4 z-10
                                    inline-flex items-center px-3 py-1.5 rounded-full
                                    text-xs font-bold tabular-nums
                                    bg-bg-elevated/80 backdrop-blur-md
                                    border border-glass-border text-text-primary
                                    shadow-[0_2px_8px_rgba(0,0,0,0.35)]">
                      {safeIdx + 1} <span className="opacity-50 px-1">/</span> {gallerySlots.length}
                    </div>
                  )}
                </div>

                {}
                {gallerySlots.length > 1 && (

                  <div className="shrink-0 flex gap-2 overflow-x-auto overflow-y-visible pt-3 pb-1 -mx-1 px-1
                                  scrollbar-thin scrollbar-thumb-glass-strong">
                    {gallerySlots.map((slot, idx) => {
                      const active = idx === safeIdx;
                      const isMedia = slot.kind !== 'image';

                      const posterUrl = isMedia ? item.previewUrl : slot.url;

                      const ringClass = active
                        ? 'ring-2 ring-accent'
                        : 'opacity-65 hover:opacity-100 ring-1 ring-glass-border';
                      return (
                        <button
                          key={`${slot.kind}:${slot.url}:${idx}`}
                          type="button"
                          onClick={() => setGalleryIdx(idx)}
                          aria-label={isMedia
                            ? t('common.video', 'Видео')
                            : t('redux.galleryAlt', {
                                defaultValue: 'Скриншот {{n}} «{{name}}»',
                                n: idx + 1,
                                name: item.name || item.id,
                              })}
                          aria-current={active}
                          className={
                            'relative shrink-0 w-24 h-14 rounded-xl overflow-hidden ' +
                            'bg-cover bg-center transition-all duration-200 ease-depth ' +
                            ringClass
                          }
                          style={posterUrl ? { backgroundImage: `url("${posterUrl}")` } : undefined}
                        >
                          {isMedia && (
                            <span className="absolute inset-0 flex items-center justify-center
                                             bg-black/35 backdrop-blur-[1px] text-white">
                              <Play size={14} fill="currentColor" />
                            </span>
                          )}
                        </button>
                      );
                    })}
                  </div>
                )}
              </>
            ) : (
              <GlassPanel
                depth="z3" tint="ultra" highlight rounded="3xl"
                className="flex-1 min-h-[420px] w-full overflow-hidden"
              >
                <PlaceholderHint>{t('redux.noPreview')}</PlaceholderHint>
              </GlassPanel>
            )}
          </div>

          {}
          <div className="lg:flex-[5] flex flex-col gap-4 min-w-0">

            {onPickForBuild ? (
              <GlassPanel
                depth="z2" tint="ultra" highlight rounded="3xl"
                className="relative overflow-hidden p-6 flex flex-col gap-5"
              >
                <span
                  aria-hidden
                  className="absolute -top-16 -right-12 w-48 h-48 pointer-events-none blur-3xl"
                  style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 16%, transparent) 0%, transparent 70%)' }}
                />
                <div className="relative flex flex-col gap-3">
                  <span
                    className="inline-flex items-center justify-center w-12 h-12 rounded-2xl text-accent"
                    style={{
                      background: 'color-mix(in srgb, var(--accent) 14%, transparent)',
                      boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 32%, transparent)',
                    }}
                  >
                    <Layers size={24} strokeWidth={2} />
                  </span>
                  <div className="flex flex-col gap-1.5">
                    <span className="text-[10px] uppercase tracking-[0.28em] text-accent font-bold">
                      {t('redux.pickEyebrow', 'Основа сборки')}
                    </span>
                    <h3 className="text-lg font-bold text-text-primary tracking-tight leading-snug">
                      {t('redux.pickTitle', 'Сделать этот редукс базой')}
                    </h3>
                    <p className="text-[12.5px] text-text-secondary leading-relaxed">
                      {t('redux.pickBody', 'Поверх него встанут ганпак, броня, арена, миникарта и остальные настройки - всё одним пакетом.')}
                    </p>
                  </div>
                </div>
                <div className="relative grid grid-cols-3 gap-2">
                  {[
                    { Icon: HardDrive, label: t('redux.statSize'),      value: sizeLabel },
                    { Icon: Sliders,   label: t('redux.canCustomize'),  value: String(customizable.length) },
                    { Icon: Download,  label: t('redux.statDownloads'), value: downloads },
                  ].map((s, i) => (
                    <div
                      key={i}
                      className="flex flex-col items-center gap-1.5 py-3 px-2 rounded-2xl
                                 bg-white/[0.04] border border-white/[0.07]"
                    >
                      <s.Icon size={15} className="text-text-muted" />
                      <span className="text-sm font-bold tabular-nums text-text-primary leading-none">{s.value}</span>
                      <span className="text-[9.5px] uppercase tracking-[0.12em] text-text-muted text-center leading-tight">{s.label}</span>
                    </div>
                  ))}
                </div>
                <div className="relative flex flex-col gap-2">
                  <button
                    type="button"
                    onClick={() => item && onPickForBuild(item.id)}
                    className="w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                               bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                               hover:bg-bg-elevated/75 hover:border-white/[0.18]
                               transition-colors text-sm font-bold uppercase tracking-wider"
                    style={{ outline: 'none' }}
                  >
                    <CheckCircle2 size={16} />
                    <span>{t('redux.pickCta', 'Выбрать для связки')}</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => setComponentsOpen(true)}
                    className="w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                               bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                               hover:bg-bg-elevated/75 hover:border-white/[0.18]
                               transition-colors text-sm font-bold uppercase tracking-wider"
                    style={{ outline: 'none' }}
                  >
                    <Eye size={16} />
                    <span>{t('redux.viewComponentsButton')}</span>
                  </button>
                </div>
              </GlassPanel>
            ) : (
            <>

            {}
            <GlassPanel
              depth="z2" tint="ultra" highlight rounded="3xl"
              className="p-5 flex flex-col justify-between gap-3"
            >
              <div className="flex items-center justify-between gap-3">
                <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted">
                  {t('redux.statSize')}
                </span>
                <span className="text-base font-bold tabular-nums text-accent">
                  {sizeLabel}
                </span>
              </div>
              {isInstalled ? (
                <>
                  <div className="w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                                  bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                                  text-sm font-bold uppercase tracking-wider">
                    <CheckCircle2 size={16} className="text-status-success" />
                    {t('redux.installedPill')}
                  </div>
                  <button
                    type="button"
                    onClick={() => void onUninstall()}
                    disabled={uninstalling}
                    style={{ outline: 'none' }}
                    className="w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                               bg-bg-elevated/55 text-red-300 border border-white/[0.08]
                               hover:bg-bg-elevated/75 hover:border-red-500/40
                               disabled:opacity-60 transition-colors text-sm font-bold uppercase tracking-wider"
                  >
                    <Trash2 size={16} />
                    <span>{uninstalling ? t('redux.uninstalling') : t('redux.uninstallButton')}</span>
                  </button>
                </>
              ) : (

                <div className="flex flex-col gap-2">
                  {isReduxPickMode && (
                    <button
                      type="button"
                      onClick={onUseInBuild}
                      className="w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                                 bg-accent text-text-on-accent
                                 hover:bg-accent-hover shadow-glow-accent
                                 transition-colors text-sm font-bold uppercase tracking-wider"
                      style={{ outline: 'none' }}
                    >
                      <span>{t('guns.detail.useInBuild', 'Использовать в сборке')}</span>
                    </button>
                  )}
                  {installing ? (

                    <div className="w-full flex items-stretch gap-2">
                      <div className="flex-1 inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                                      bg-accent-soft text-text-primary opacity-70
                                      border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                                      text-sm font-bold uppercase tracking-wider">
                        <Loader2 size={16} className="animate-spin" />
                        <span>{t('redux.installing')}</span>
                      </div>
                      <button
                        type="button"
                        onClick={async () => {
                          try { await bridge.reduxInstallCancel(); }
                          catch (e) { console.warn('[redux.cancel] failed', e); }
                        }}
                        title={t('redux.installCancelTitle', 'Отменить установку')}
                        className="inline-flex items-center justify-center gap-1.5 h-12 px-4 rounded-xl
                                   bg-bg-elevated-soft hover:bg-bg-elevated
                                   border border-border-subtle hover:border-[color-mix(in_srgb,#ef4444_50%,transparent)]
                                   text-[12.5px] text-text-secondary hover:text-[#ef4444]
                                   transition-colors font-bold uppercase tracking-wider"
                        style={{ outline: 'none' }}
                      >
                        <X size={14} strokeWidth={2.4} />
                        <span>{t('common.cancelAction', 'Отменить')}</span>
                      </button>
                    </div>
                  ) : (
                    <button
                      type="button"
                      onClick={onInstall}
                      className="w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                                 bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                                 hover:bg-bg-elevated/75 hover:border-white/[0.18]
                                 transition-colors text-sm font-bold uppercase tracking-wider"
                      style={{ outline: 'none' }}
                    >
                      <Download size={16} />
                      <span>{t('redux.installButton')}</span>
                    </button>
                  )}
                </div>
              )}

              {showVersionSelector && (
                <motion.div
                  className="pt-1 overflow-hidden"
                  initial={{ height: 0 }}
                  animate={{ height: 'auto' }}
                  transition={{ duration: 0.38, ease: EASE_DEPTH }}
                >
                <motion.div
                  className="flex flex-wrap gap-1.5"
                  initial={{ opacity: 0, y: 6, scale: 0.98, filter: 'blur(4px)' }}
                  animate={{ opacity: 1, y: 0, scale: 1, filter: 'blur(0px)' }}
                  transition={{ duration: 0.38, ease: EASE_DEPTH }}
                >
                  {versionList.map(v => {
                    const active = v.id === effectiveVersionId;
                    const desc = (v.label || '')
                      .replace(/^v\s*\d+\s*[:.)\-]?\s*/i, '')
                      .replace(/^\((.*)\)$/, '$1')
                      .trim();
                    return (
                      <button
                        key={v.id}
                        type="button"
                        onClick={() => setSelectedVersionId(v.id)}
                        title={v.label}
                        className={
                          'flex-1 min-w-[64px] inline-flex items-center justify-center gap-2 h-11 px-4 rounded-xl border text-xs font-bold uppercase tracking-wider transition-colors ' +
                          (active
                            ? 'bg-bg-elevated/80 border-white/[0.20] text-text-primary'
                            : 'bg-bg-elevated/55 border-white/[0.08] text-text-secondary hover:bg-bg-elevated/75 hover:border-white/[0.18] hover:text-text-primary')
                        }
                      >
                        <span className="tabular-nums shrink-0">V{v.slot}</span>
                        {desc && <span className="truncate opacity-80">{desc}</span>}
                      </button>
                    );
                  })}
                </motion.div>
                </motion.div>
              )}
            </GlassPanel>

            {}
            {!onPickForBuild && (
            <GlassPanel
              depth="z2" tint="ultra" highlight rounded="3xl"
              className="p-5 flex flex-col justify-between gap-3"
            >
              <div className="flex items-center justify-between gap-3">
                <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted">
                  {t('redux.canCustomize')}
                </span>
                <span className="text-base font-bold tabular-nums text-text-primary">
                  {customizable.length}
                </span>
              </div>
              <button
                type="button"
                onClick={() => {
                  openCustomize(item, effectiveVersionId ?? undefined);
                  reqNavigate('redux-customize');
                }}
                style={{ outline: 'none' }}
                className="w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                           bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                           hover:bg-bg-elevated/75 hover:border-white/[0.18]
                           transition-colors text-sm font-bold uppercase tracking-wider"
              >
                <Sliders size={16} />
                <span>{t('redux.customizeButton')}</span>
              </button>
            </GlassPanel>
            )}

            {}
            <GlassPanel
              depth="z2" tint="ultra" highlight rounded="3xl"
              className="p-5 flex flex-col justify-between gap-3"
            >
              <div className="flex items-center justify-between gap-3">
                <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted">
                  {t('redux.viewComponentsButton')}
                </span>
              </div>
              <button
                type="button"
                onClick={() => setComponentsOpen(true)}
                style={{ outline: 'none' }}
                className="w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                           bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                           hover:bg-bg-elevated/75 hover:border-white/[0.18]
                           transition-colors text-sm font-bold uppercase tracking-wider"
              >
                <Eye size={16} />
                <span>{t('redux.viewComponentsButton')}</span>
              </button>
            </GlassPanel>
            </>
            )}

          </div>
        </motion.div>

        {}
        <motion.div variants={detailItem}>
          <GlassPanel depth="z1" tint="ultra" highlight rounded="3xl" className="p-6 flex flex-col gap-4">
            <div className="grid grid-cols-1 md:grid-cols-12 md:gap-x-6 gap-y-4">
              <div className="md:col-span-8 flex flex-col gap-3 md:pr-6 md:border-r md:border-glass-border">
                <h2 className="text-xs uppercase tracking-[0.2em] text-text-muted">
                  {t('redux.descriptionTitle')}
                </h2>
                {item.description ? (
                  <p className="text-sm text-text-secondary leading-relaxed whitespace-pre-line">
                    {item.description}
                  </p>
                ) : (
                  <p className="text-sm text-text-muted italic">
                    {t('redux.descriptionEmpty')}
                  </p>
                )}
              </div>

              <div className="md:col-span-4 flex flex-col gap-3">
                <h2 className="text-xs uppercase tracking-[0.2em] text-text-muted">
                  {t('redux.infoCardTitle')}
                </h2>
                <div className="grid grid-cols-2 md:grid-cols-1 gap-3 text-sm">
                  <InfoRow icon={Download}  label={t('redux.statDownloads')} value={downloads} />
                  <InfoRow icon={HardDrive} label={t('redux.statSize')}      value={sizeLabel} />
                </div>
              </div>
            </div>

          </GlassPanel>
        </motion.div>

        {}
        <motion.div variants={detailItem}>
          <ReviewsSection reduxId={item.id} />
        </motion.div>
      </motion.div>

      <AnimatePresence>
        {componentsOpen && (
          <ComponentsViewerModal
            components={effComponents}
            componentScreenshots={item.componentScreenshots}
            reduxName={item.name || item.id}
            onClose={() => setComponentsOpen(false)}
          />
        )}
      </AnimatePresence>

      <ConfirmModal
        open={minimapConflict !== null}
        title={t('redux.minimapConflictTitle', 'Какую миникарту оставить?')}
        message={t('redux.minimapConflictBody', 'У тебя стоит своя миникарта ({{name}}), а сборка везёт свою. Что оставить?', { name: minimapConflict ?? '' })}
        confirmLabel={t('redux.minimapConflictKeep', 'Оставить мою')}
        cancelLabel={t('redux.minimapConflictTake', 'Взять из сборки')}
        hideConfirmArrow
        onConfirm={onMinimapKeepMine}
        onCancel={() => { void onMinimapTakeRedux(); }}
      />

      <ArmorReplaceConfirmModal
        open={armorReplacePrompt !== null}
        current={{
          name:       armorReplacePrompt?.name ?? '',
          screenshot: armorReplacePrompt ? currentArmorPhoto(armorReplacePrompt, items) : null,
          kindLabel:  armorReplacePrompt?.kind === 'library'
            ? t('catalog.kindCustom')
            : t('catalog.kindRedux'),
        }}
        incoming={{
          name:       item.name || item.id,
          screenshot: item.componentScreenshots?.armor ?? null,
          kindLabel:  t('catalog.kindRedux'),
        }}
        busy={installing}
        onKeepCurrent={onArmorReplaceKeep}
        onInstallNew={onArmorReplaceInstall}
        onCancel={() => setArmorReplacePrompt(null)}
      />

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 12000 : 4000}
      />

    </div>
  );
}

interface InfoRowProps {
  icon:  LucideIcon;
  label: string;
  value: string;
}

function InfoRow({ icon: Icon, label, value }: InfoRowProps) {
  return (
    <div className="flex items-center gap-2 min-w-0">
      <div className="shrink-0 w-7 h-7 rounded-lg bg-glass-strong text-text-secondary
                      flex items-center justify-center">
        <Icon size={13} />
      </div>
      <div className="min-w-0">
        <div className="text-[9px] uppercase tracking-wider text-text-muted leading-tight">
          {label}
        </div>
        <div className="text-xs font-bold tabular-nums text-text-primary truncate leading-tight">
          {value}
        </div>
      </div>
    </div>
  );
}

function PlaceholderHint({ children }: { children: React.ReactNode }) {
  return <div className="w-full h-full flex items-center justify-center text-text-muted text-sm">{children}</div>;
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

type GallerySlot = { kind: 'image'; url: string } | VideoSlot;

function currentArmorPhoto(info: CurrentArmorInfo, reduxItems: ReduxItem[]): string | null {
  if (info.kind === 'library') {
    return getArmorLibraryCache().find(a => a.id === info.id)?.previewUrl || null;
  }
  return reduxItems.find(r => r.id === info.id)?.componentScreenshots?.armor || null;
}

let _mmMapCache: Map<string, { promo: string; display: string }> | null = null;
let _mmMapPromise: Promise<void> | null = null;

function ReduxAuthorLine({ reduxId, fallback }: { reduxId: string; fallback: string }) {
  const { t } = useTranslation();
  const [mm, setMm] = useState<{ promo: string; display: string } | null>(
    () => _mmMapCache?.get('redux:' + reduxId) ?? null);

  useEffect(() => {
    let alive = true;
    if (_mmMapCache) { setMm(_mmMapCache.get('redux:' + reduxId) ?? null); return; }
    _mmMapPromise ??= bridge.modmakerMap().then(r => {
      _mmMapCache = new Map();
      if (r?.ok && r.map) for (const e of r.map)
        _mmMapCache.set(e.kind + ':' + e.id, { promo: e.promo, display: e.display });
    }).catch(() => { _mmMapCache = new Map(); });
    _mmMapPromise.then(() => {
      if (alive) setMm(_mmMapCache?.get('redux:' + reduxId) ?? null);
    });
    return () => { alive = false; };
  }, [reduxId]);

  return (
    <span>
      {t('redux.author')}:{' '}
      <strong className="text-text-primary">{mm ? mm.display : fallback}</strong>
    </span>
  );
}
