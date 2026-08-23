import { useEffect, useMemo, useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Package, Layers, Crosshair, Trash2, AlertTriangle,
  Upload, Download, Loader2, ArrowRight, SlidersHorizontal,
  Shield as ShieldIcon, Map as MapIcon, Target as TargetIcon, Volume2,
  RefreshCw, ChevronDown,
} from 'lucide-react';
import { AnimatePresence, motion, type Variants } from 'framer-motion';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useGtaSettingsStore } from '@/store/gtaSettingsStore';
import { useInstallProgressStore } from '@/store/installProgressStore';
import { useSessionStore } from '@/store/sessionStore';
import { GlassPanel } from '@/design';
import { Toast } from '@/components/Toast';
import { ConfirmModal } from '@/components/ConfirmModal';
import { ArmorRollbackChoiceModal } from '@/screens/armor/ArmorRollbackChoiceModal';
import { bridge } from '@/bridge';
import { HntExportModal } from './HntExportModal';
import { HntImportModal } from './HntImportModal';
import { HntMyCodesPanel } from './HntMyCodesPanel';

const containerVariants: Variants = {
  hidden:  { opacity: 1 },
  visible: { opacity: 1, transition: { delayChildren: 0.05, staggerChildren: 0.07 } },
};
const itemVariants: Variants = {
  hidden:  { opacity: 0, y: 14 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.45, ease: [0.22, 1, 0.36, 1] } },
};

interface Props {
  onNavigateTo: (tabId: string) => void;
}

export function InstalledModsScreen({ onNavigateTo }: Props) {
  const { t } = useTranslation();

  const reduxItems        = useReduxStore(s => s.items);
  const installedReduxId  = useReduxStore(s => s.installedReduxId);
  const reduxLoad         = useReduxStore(s => s.load);
  const uninstallRedux    = useReduxStore(s => s.uninstall);
  const reduxInstall      = useReduxStore(s => s.installForceClean);

  const installedGunpack       = useGunpackStore(s => s.installedGunpack);
  const publicPacks            = useGunpackStore(s => s.publicPacks);
  const loadPublicPacks        = useGunpackStore(s => s.loadPublicPacks);
  const installedSelectedGuns  = useGunpackStore(s => s.installedSelectedGuns);
  const loadInstallState       = useGunpackStore(s => s.loadInstallState);
  const selectPack             = useGunpackStore(s => s.selectPack);

  const allGuns                = useGunpackStore(s => s.allGuns);
  const loadAllGuns            = useGunpackStore(s => s.loadAllGuns);
  const selectWhitelistGun     = useGunpackStore(s => s.selectWhitelistGun);

  const installedPresetId      = useGtaSettingsStore(s => s.installedPresetId);
  const publicPresets          = useGtaSettingsStore(s => s.publicPresets);
  const loadPublicPresets      = useGtaSettingsStore(s => s.loadPublicPresets);
  const setInstalledPreset     = useGtaSettingsStore(s => s.setInstalledPreset);

  const auth = useSessionStore(s => s.auth);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const isGuest = !userId;

  const silenceProgress   = useInstallProgressStore(s => s.silenceProgress);
  const unsilenceProgress = useInstallProgressStore(s => s.unsilenceProgress);

  const [reduxBusy, setReduxBusy] = useState(false);
  const [gunpackBusy, setGunpackBusy] = useState(false);
  const [removingGun, setRemovingGun] = useState<string | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error' | 'info'; message: string } | null>(null);
  const [exportOpen, setExportOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [gunsExpanded, setGunsExpanded] = useState(false);

  const [codesRefreshKey, setCodesRefreshKey] = useState(0);

  const [soundPack, setSoundPack] = useState<{ id: string; name: string } | null>(null);
  const [minimap,   setMinimap]   = useState<{ kind: 'redux' | 'library'; id: string; name: string } | null>(null);
  const [reticle,   setReticle]   = useState<{ kind: 'redux' | 'library' | 'custom'; id: string; name: string } | null>(null);
  const [armor,     setArmor]     = useState<{ id: string; name: string; kind: string } | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const [soundBusy,    setSoundBusy]    = useState(false);
  const [minimapBusy,  setMinimapBusy]  = useState(false);
  const [reticleBusy,  setReticleBusy]  = useState(false);
  const [armorBusy,    setArmorBusy]    = useState(false);
  const [rollbackAllOpen,  setRollbackAllOpen]  = useState(false);
  const [rollbackAllBusy,  setRollbackAllBusy]  = useState(false);
  const [armorChoiceOpen,  setArmorChoiceOpen]  = useState(false);

  const refreshSoundPack = useCallback(async () => {
    try {
      const info = await bridge.getCurrentSoundPackInfo();
      setSoundPack(info ? { id: info.id, name: info.name } : null);
    } catch { setSoundPack(null); }
  }, []);
  const refreshMinimap = useCallback(async () => {
    try {
      const info = await bridge.getCurrentMinimapInfo();
      setMinimap(info ? { kind: info.kind, id: info.id, name: info.name } : null);
    } catch { setMinimap(null); }
  }, []);
  const refreshReticle = useCallback(async () => {
    try {
      const info = await bridge.getCurrentReticleInfo();
      setReticle(info ? { kind: info.kind, id: info.id, name: info.name } : null);
    } catch { setReticle(null); }
  }, []);
  const refreshArmor = useCallback(async () => {
    try {
      const info = await bridge.getCurrentArmorInfo();
      setArmor(info ? { id: info.id, name: info.name, kind: info.kind } : null);
    } catch { setArmor(null); }
  }, []);

  const refreshAll = useCallback(async () => {
    if (refreshing) return;
    setRefreshing(true);
    try {
      await Promise.all([
        reduxLoad(),
        loadPublicPresets(null),
        loadInstallState(),
        refreshSoundPack(),
        refreshMinimap(),
        refreshReticle(),
        refreshArmor(),
      ]);
    } finally {
      setRefreshing(false);
    }
  }, [refreshing, reduxLoad, loadPublicPresets, loadInstallState,
      refreshSoundPack, refreshMinimap, refreshReticle, refreshArmor]);

  useEffect(() => {
    void Promise.all([
      refreshSoundPack(),
      refreshMinimap(),
      refreshReticle(),
      refreshArmor(),
    ]).catch(e => console.warn('[installed] current-state refresh failed', e));
  }, [refreshSoundPack, refreshMinimap, refreshReticle, refreshArmor]);

  useEffect(() => {
    if (reduxItems.length === 0) void reduxLoad();
    if (publicPresets.length === 0) void loadPublicPresets(null);
    if (publicPacks.length === 0) void loadPublicPacks();
    void loadInstallState();
  }, [reduxItems.length, publicPresets.length, reduxLoad, loadPublicPresets, loadInstallState]);

  const installedReduxItem = useMemo(
    () => (installedReduxId ? reduxItems.find(i => i.id === installedReduxId) ?? null : null),
    [reduxItems, installedReduxId]);

  const installedGunpackItem = useMemo(
    () => (installedGunpack.activeGunpackId
      ? publicPacks.find(p => p.id === installedGunpack.activeGunpackId) ?? null
      : null),
    [publicPacks, installedGunpack.activeGunpackId]);

  useEffect(() => {
    if (installedSelectedGuns.length > 0 && allGuns.length === 0) {
      void loadAllGuns();
    }
  }, [installedSelectedGuns.length, allGuns.length, loadAllGuns]);

  const gunPreviewByKey = useMemo(() => {
    const m = new Map<string, string | null>();
    for (const g of allGuns) m.set(`${g.packId}::${g.gunId}`, g.previewUrl ?? null);
    return m;
  }, [allGuns]);

  const firstInstalledGunPreviewUrl = useMemo(() => {
    const first = installedSelectedGuns[0];
    if (!first) return null;
    return gunPreviewByKey.get(`${first.gunpackId}::${first.gunId}`) ?? null;
  }, [installedSelectedGuns, gunPreviewByKey]);

  const installedPresetItem = useMemo(
    () => (installedPresetId ? publicPresets.find(p => p.id === installedPresetId) ?? null : null),
    [publicPresets, installedPresetId]);

  const hasRedux   = !!installedReduxId;
  const hasGunpack = !!installedGunpack.activeGunpackId;
  const hasGuns    = installedSelectedGuns.length > 0;
  const hasPreset  = !!installedPresetId;
  const hasSounds  = !!soundPack;
  const hasMinimap = !!minimap;
  const hasReticle = !!reticle;
  const hasArmor   = !!armor;

  const hasAnything = hasRedux || hasGunpack || hasGuns || hasPreset
                    || hasSounds || hasMinimap || hasReticle || hasArmor;

  const hasAnythingExportable = hasRedux || hasGunpack || hasGuns;

  const onUninstallRedux = async () => {
    if (reduxBusy) return;
    setReduxBusy(true);
    try {
      const r = await uninstallRedux();
      if (r.success) {
        setToast({ tone: 'success', message: t('installed.toastReduxRemoved') });
      } else if (r.errorMessage === 'DIRTY_FILES_NEED_CONFIRM') {

        setToast({
          tone: 'info',
          message: t('installed.toastDirtyRedirect'),
        });
        if (installedReduxItem) {
          useReduxStore.getState().select(installedReduxItem.id);
          onNavigateTo('redux');
        }
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setReduxBusy(false);
    }
  };

  const onUninstallGunpack = async () => {
    if (gunpackBusy) return;
    setGunpackBusy(true);
    try {

      const { bridge } = await import('@/bridge');
      const ok = await bridge.gunpackUninstall();
      if (ok) {
        await loadInstallState();
        setToast({ tone: 'success', message: t('installed.toastGunpackRemoved') });
      } else {
        setToast({ tone: 'error', message: t('installed.toastGunpackFailed') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setGunpackBusy(false);
    }
  };

  const onRollbackSounds = async () => {
    if (soundBusy) return;
    setSoundBusy(true);
    try {
      const r = await bridge.soundPackUninstall();
      if (r.success) {
        await refreshSoundPack();
        setToast({ tone: 'success', message: t('installed.toastSoundsRolledBack') });
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally { setSoundBusy(false); }
  };

  const onRollbackMinimap = async () => {
    if (minimapBusy) return;
    if (!installedReduxId) {

      setToast({ tone: 'info', message: t('installed.toastNothingToRollback') });
      return;
    }
    setMinimapBusy(true);
    try {
      const r = await bridge.reduxResetCustomization('minimap');
      if (r.success) {
        await refreshMinimap();
        setToast({ tone: 'success', message: t('installed.toastMinimapRolledBack') });
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setMinimapBusy(false);
    }
  };

  const onRollbackReticle = async () => {
    if (reticleBusy) return;
    if (!installedReduxId) {
      setToast({ tone: 'info', message: t('installed.toastNothingToRollback') });
      return;
    }
    setReticleBusy(true);
    try {
      const r = await bridge.reduxResetCustomization('crosshair');
      if (r.success) {
        await refreshReticle();
        setToast({ tone: 'success', message: t('installed.toastReticleRolledBack') });
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setReticleBusy(false);
    }
  };

  const onArmorRollbackClick = () => {
    if (armorBusy) return;

    if (!installedReduxId) { void runArmorRollback('clear'); return; }
    const reduxHasOwnArmor = !!installedReduxItem?.components?.armor;
    if (reduxHasOwnArmor) { setArmorChoiceOpen(true); return; }
    void runArmorRollback('clear');
  };
  const runArmorRollback = async (mode: 'revert-to-redux' | 'clear') => {

    if (mode === 'revert-to-redux' && !installedReduxId) return;
    setArmorChoiceOpen(false);
    setArmorBusy(true);
    const willReinstall = mode === 'revert-to-redux';
    const reduxRowId = installedReduxId ? `redux:${installedReduxId}` : null;
    if (willReinstall && reduxRowId) silenceProgress(reduxRowId);
    try {
      const r = willReinstall && installedReduxId
        ? await reduxInstall(installedReduxId)
        : await bridge.reduxClearArmor();
      if (r.success) {
        await refreshArmor();
        setToast({
          tone: 'success',
          message: willReinstall ? t('installed.toastArmorToRedux') : t('installed.toastArmorToStock'),
        });
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      if (willReinstall && reduxRowId) unsilenceProgress(reduxRowId);
      setArmorBusy(false);
    }
  };

  const onRollbackAllConfirm = async () => {
    if (rollbackAllBusy) return;
    setRollbackAllBusy(true);
    let failures = 0;
    try {
      if (soundPack) {
        try { await bridge.soundPackUninstall(); } catch { failures++; }
      }
      if (installedSelectedGuns.length > 0) {
        try { await bridge.selectedGunsUninstallAll(); } catch { failures++; }
      }
      if (installedGunpack.activeGunpackId) {
        try { await bridge.gunpackUninstall(); } catch { failures++; }
      }
      if (installedReduxId) {
        try { await uninstallRedux(); } catch { failures++; }
      }
      await Promise.all([
        loadInstallState(),
        refreshSoundPack(),
        refreshMinimap(),
        refreshReticle(),
        refreshArmor(),
      ]);
      setRollbackAllOpen(false);
      setToast({
        tone: failures > 0 ? 'error' : 'success',
        message: failures > 0
          ? t('installed.toastRollbackAllPartial', { count: failures })
          : t('installed.toastRollbackAllDone'),
      });
    } finally {
      setRollbackAllBusy(false);
    }
  };

  const onRemoveSelectedGun = async (internalName: string) => {
    if (removingGun) return;
    setRemovingGun(internalName);
    try {
      const { bridge } = await import('@/bridge');
      const r = await bridge.selectedGunsRemove(internalName);
      if (r.success) {
        await loadInstallState();
        setToast({ tone: 'success', message: t('installed.toastGunRemoved') });
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setRemovingGun(null);
    }
  };

  const onExportClick = () => {
    if (isGuest) {
      setToast({ tone: 'info', message: t('installed.hntAuthRequired') });
      return;
    }
    if (!hasAnythingExportable) {
      setToast({ tone: 'info', message: t('installed.hntExportEmpty') });
      return;
    }
    setExportOpen(true);
  };
  const onImportClick = () => {
    if (isGuest) {
      setToast({ tone: 'info', message: t('installed.hntAuthRequired') });
      return;
    }
    setImportOpen(true);
  };
  const onAfterImport = async () => {

    await Promise.all([
      reduxLoad(),
      loadInstallState(),
    ]);
  };

  return (
    <motion.div
      className="h-full overflow-y-auto"
      variants={containerVariants}
      initial="hidden"
      animate="visible"
    >
      <div className="max-w-[1280px] mx-auto px-8 py-6 flex flex-col gap-5">

        {}
        <motion.div className="flex items-end justify-between gap-4 flex-wrap" variants={itemVariants}>
          <div className="flex items-center gap-3">
            <span
              className="shrink-0 inline-flex items-center justify-center w-10 h-10 rounded-xl
                         bg-accent/15 text-accent"
              aria-hidden
            >
              <Layers size={18} strokeWidth={2} />
            </span>
            <div>
              <h1 className="font-display font-bold text-2xl uppercase tracking-wide text-text-primary">
                {t('installed.title')}
              </h1>
              <p className="mt-1 text-sm text-text-secondary">
                {t('installed.subtitle')}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            {}
            <button
              type="button"
              onClick={() => void refreshAll()}
              disabled={refreshing}
              title={t('installed.refreshTitle')}
              className="inline-flex items-center justify-center w-10 h-10 rounded-xl
                         bg-glass border border-glass-border text-text-secondary
                         hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong
                         disabled:opacity-50 transition-colors"
              style={{ outline: 'none' }}
            >
              <RefreshCw size={14} className={refreshing ? 'animate-spin' : ''} />
            </button>
            {}
            <button
              type="button"
              onClick={() => setRollbackAllOpen(true)}
              disabled={!hasAnything || rollbackAllBusy}
              title={t('installed.rollbackAllTitle')}
              className="inline-flex items-center gap-2 px-3 h-10 rounded-xl
                         bg-red-500/15 text-red-300 border border-red-500/30
                         hover:bg-red-500/25 hover:border-red-500/50
                         disabled:opacity-40 disabled:cursor-not-allowed
                         transition-colors text-sm font-semibold"
              style={{ outline: 'none' }}
            >
              {rollbackAllBusy ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
              <span>{t('installed.rollbackAll')}</span>
            </button>
            <button
              type="button"
              onClick={onImportClick}
              title={isGuest ? t('installed.hntAuthRequired') : t('installed.importHnt')}
              className="inline-flex items-center gap-2 px-3 py-2 rounded-xl
                         bg-glass border border-glass-border text-sm text-text-secondary
                         hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong
                         transition-colors duration-200 focus:outline-none focus-visible:outline-none"
              style={{ outline: 'none' }}
            >
              <Download size={14} />
              <span>{t('installed.importHnt')}</span>
            </button>
            <button
              type="button"
              onClick={onExportClick}
              title={isGuest
                ? t('installed.hntAuthRequired')
                : (!hasAnythingExportable ? t('installed.hntExportEmpty') : t('installed.exportHnt'))}
              disabled={!hasAnythingExportable && !isGuest}
              className="inline-flex items-center gap-2 px-3 py-2 rounded-xl
                         bg-accent text-text-on-accent text-sm font-medium
                         hover:bg-accent-hover shadow-glow-accent
                         disabled:opacity-50 disabled:cursor-not-allowed
                         transition-colors duration-200 focus:outline-none focus-visible:outline-none"
              style={{ outline: 'none' }}
            >
              <Upload size={14} />
              <span>{t('installed.exportHnt')}</span>
            </button>
          </div>
        </motion.div>

        {}
        {!hasAnything && (
          <motion.div variants={itemVariants}>
            <GlassPanel depth="z1" tint="soft" rounded="3xl" className="p-10 text-center">
              <Package size={48} className="mx-auto mb-4 text-text-muted opacity-40" />
              <h2 className="text-base font-semibold text-text-primary mb-1">
                {t('installed.emptyTitle')}
              </h2>
              <p className="text-sm text-text-secondary mb-5">
                {t('installed.emptySubtitle')}
              </p>
              <div className="inline-flex items-center gap-3">
                <button
                  type="button"
                  onClick={() => onNavigateTo('redux')}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-accent text-text-on-accent text-sm font-semibold hover:bg-accent-hover
                             shadow-glow-accent transition-colors"
                >
                  <Layers size={14} /> {t('installed.goToRedux')}
                </button>
                <button
                  type="button"
                  onClick={() => onNavigateTo('guns')}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-glass border border-glass-border text-sm text-text-primary
                             hover:bg-glass-strong transition-colors"
                >
                  <Crosshair size={14} /> {t('installed.goToGuns')}
                </button>
              </div>
            </GlassPanel>
          </motion.div>
        )}

        {}
        {hasRedux && (
          <motion.div variants={itemVariants}>
            <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-5 flex items-center gap-4">
              {}
              <div className="shrink-0 w-16 h-16 rounded-xl overflow-hidden
                              bg-accent/15 text-accent flex items-center justify-center
                              border border-white/[0.06]">
                {installedReduxItem?.previewUrl ? (
                  <img
                    src={installedReduxItem.previewUrl}
                    alt=""
                    draggable={false}
                    className="w-full h-full object-cover"
                    onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                  />
                ) : (
                  <Layers size={22} />
                )}
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
                  {t('installed.reduxLabel')}
                </div>
                <div className="font-display font-bold text-base text-text-primary truncate">
                  {installedReduxItem?.name ?? installedReduxId}
                </div>
                {installedReduxItem?.author && (
                  <div className="text-xs text-text-muted truncate">
                    {t('installed.byAuthor', { author: installedReduxItem.author, defaultValue: 'by {{author}}' })}
                  </div>
                )}
              </div>
              <button
                type="button"
                onClick={() => {
                  if (installedReduxItem) {
                    useReduxStore.getState().select(installedReduxItem.id);
                    onNavigateTo('redux');
                  }
                }}
                disabled={!installedReduxItem}
                title={t('installed.openInCatalog')}
                className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl
                           bg-glass border border-glass-border text-sm text-text-secondary
                           hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong
                           disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <span>{t('installed.openInCatalog')}</span>
                <ArrowRight size={13} />
              </button>
              <button
                type="button"
                onClick={() => void onUninstallRedux()}
                disabled={reduxBusy}
                className="inline-flex items-center gap-2 px-3 py-2 rounded-xl
                           bg-red-500/15 text-red-300 hover:bg-red-500/25
                           border border-red-500/30 hover:border-red-500/50
                           disabled:opacity-60 transition-colors text-sm font-semibold"
              >
                {reduxBusy ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                <span>{reduxBusy ? t('installed.removing') : t('installed.remove')}</span>
              </button>
            </GlassPanel>
          </motion.div>
        )}

        {}
        {hasPreset && (
          <motion.div variants={itemVariants}>
            <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-5 flex items-center gap-4">
              <div className="shrink-0 w-12 h-12 rounded-xl bg-accent/15 text-accent
                              flex items-center justify-center">
                <SlidersHorizontal size={20} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
                  {t('installed.presetLabel')}
                </div>
                <div className="font-display font-bold text-base text-text-primary truncate">
                  {installedPresetItem?.name ?? installedPresetId}
                </div>
                {installedPresetItem && (
                  <div className="text-xs text-text-muted truncate">
                    {installedPresetItem.author && <>{t('installed.byAuthor', { author: installedPresetItem.author, defaultValue: 'by {{author}}' })}</>}
                    {installedPresetItem.expectedFpsLow != null && installedPresetItem.expectedFpsHigh != null && (
                      <>{installedPresetItem.author ? ' · ' : ''}{installedPresetItem.expectedFpsLow}–{installedPresetItem.expectedFpsHigh} FPS</>
                    )}
                  </div>
                )}
              </div>
              <button
                type="button"
                onClick={() => onNavigateTo('settings')}
                title={t('installed.openInCatalog')}
                className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl
                           bg-glass border border-glass-border text-sm text-text-secondary
                           hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong
                           transition-colors"
              >
                <span>{t('installed.openInCatalog')}</span>
                <ArrowRight size={13} />
              </button>
              <button
                type="button"
                onClick={() => {
                  setInstalledPreset(null);
                  setToast({ tone: 'success', message: t('installed.toastPresetRemoved') });
                }}
                title={t('installed.removePresetTitle')}
                className="inline-flex items-center gap-2 px-3 py-2 rounded-xl
                           bg-red-500/15 text-red-300 hover:bg-red-500/25
                           border border-red-500/30 hover:border-red-500/50
                           transition-colors text-sm font-semibold"
              >
                <Trash2 size={13} />
                <span>{t('installed.removePreset')}</span>
              </button>
            </GlassPanel>
          </motion.div>
        )}

        {}
        {hasGunpack && (
          <motion.div variants={itemVariants}>
            <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-5 flex items-center gap-4">
              {}
              <div className="shrink-0 w-16 h-16 rounded-xl overflow-hidden
                              bg-accent/15 text-accent flex items-center justify-center
                              border border-white/[0.06]">
                {installedGunpackItem?.coverKind === 'image' && installedGunpackItem.coverUrl ? (
                  <img
                    src={installedGunpackItem.coverUrl}
                    alt=""
                    draggable={false}
                    className="w-full h-full object-cover"
                    onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                  />
                ) : (
                  <Crosshair size={22} />
                )}
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
                  {t('installed.gunpackLabel')}
                </div>
                <div className="font-display font-bold text-base text-text-primary truncate">
                  {installedGunpack.activeGunpackName ?? installedGunpack.activeGunpackId}
                </div>
              </div>
              <button
                type="button"
                onClick={() => {
                  if (installedGunpack.activeGunpackId) {
                    void selectPack(installedGunpack.activeGunpackId);
                    onNavigateTo('guns');
                  }
                }}
                title={t('installed.openInCatalog')}
                className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl
                           bg-glass border border-glass-border text-sm text-text-secondary
                           hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong
                           transition-colors"
              >
                <span>{t('installed.openInCatalog')}</span>
                <ArrowRight size={13} />
              </button>
              <button
                type="button"
                onClick={() => void onUninstallGunpack()}
                disabled={gunpackBusy}
                className="inline-flex items-center gap-2 px-3 py-2 rounded-xl
                           bg-red-500/15 text-red-300 hover:bg-red-500/25
                           border border-red-500/30 hover:border-red-500/50
                           disabled:opacity-60 transition-colors text-sm font-semibold"
              >
                {gunpackBusy ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                <span>{gunpackBusy ? t('installed.removing') : t('installed.remove')}</span>
              </button>
            </GlassPanel>
          </motion.div>
        )}

        {}
        {hasGuns && (
          <motion.div variants={itemVariants}>
            <GlassPanel depth="z2" tint="strong" rounded="3xl" className="overflow-hidden">
              <button
                type="button"
                onClick={() => setGunsExpanded(open => !open)}
                aria-expanded={gunsExpanded}
                className="w-full p-5 flex items-center gap-4 text-left group"
              >
                <div className="shrink-0 w-16 h-16 rounded-xl overflow-hidden
                                bg-accent/15 text-accent flex items-center justify-center
                                border border-white/[0.06]">
                  {firstInstalledGunPreviewUrl ? (
                    <img
                      src={firstInstalledGunPreviewUrl}
                      alt=""
                      draggable={false}
                      className="w-full h-full object-contain"
                      onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                    />
                  ) : (
                    <Crosshair size={22} />
                  )}
                </div>
                <div className="flex-1 min-w-0">
                  <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
                    {t('installed.gunsLabel')}
                  </div>
                  <div className="font-display font-bold text-base text-text-primary truncate">
                    {t('installed.gunsCount', { count: installedSelectedGuns.length })}
                  </div>
                </div>
                <span className="hidden sm:inline-flex px-3 py-2 rounded-xl
                                 bg-glass border border-glass-border text-sm text-text-secondary
                                 group-hover:text-text-primary group-hover:border-accent/40
                                 group-hover:bg-glass-strong transition-colors">
                  {gunsExpanded ? t('installed.collapse') : t('installed.expand')}
                </span>
                <ChevronDown
                  size={18}
                  className={`shrink-0 text-text-muted transition-transform duration-200 ${
                    gunsExpanded ? 'rotate-180' : ''
                  }`}
                />
              </button>

              <AnimatePresence initial={false}>
                {gunsExpanded && (
                  <motion.div
                    initial={{ height: 0, opacity: 0 }}
                    animate={{ height: 'auto', opacity: 1 }}
                    exit={{ height: 0, opacity: 0 }}
                    transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
                    className="overflow-hidden"
                  >
                    <div className="px-5 pb-5 pt-1 flex flex-col gap-1.5">
                      {installedSelectedGuns.map(g => {
                  const removingThis = removingGun === g.internalName;
                  const previewUrl = gunPreviewByKey.get(`${g.gunpackId}::${g.gunId}`) ?? null;
                  return (
                    <div key={g.internalName}
                         className="flex items-center gap-3 px-3 py-2 rounded-xl
                                    bg-glass border border-glass-border">
                      {}
                      <div className="shrink-0 w-12 h-12 rounded-lg
                                      bg-bg-elevated/60 border border-white/[0.06]
                                      flex items-center justify-center overflow-hidden">
                        {previewUrl ? (
                          <img
                            src={previewUrl}
                            alt=""
                            draggable={false}
                            className="w-full h-full object-contain"
                            onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                          />
                        ) : (
                          <Crosshair size={16} className="text-text-muted opacity-50" />
                        )}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="font-display text-sm font-semibold text-text-primary truncate">
                          {g.displayName}
                        </div>
                        <div className="text-xs text-text-muted truncate">
                          {t('installed.fromPack', 'из пака')} · {g.gunpackName}
                        </div>
                      </div>
                      <button
                        type="button"
                        onClick={() => {

                          selectWhitelistGun(g.internalName);
                          onNavigateTo('guns');
                        }}
                        title={t('installed.openGunInCatalog')}
                        className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg
                                   bg-glass border border-glass-border text-xs text-text-secondary
                                   hover:text-text-primary hover:bg-glass-strong hover:border-accent/40
                                   transition-colors"
                      >
                        <ArrowRight size={11} />
                      </button>
                      <button
                        type="button"
                        onClick={() => void onRemoveSelectedGun(g.internalName)}
                        disabled={removingThis}
                        title={t('installed.remove')}
                        className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg
                                   bg-red-500/10 text-red-300 hover:bg-red-500/20
                                   border border-red-500/20 hover:border-red-500/40
                                   disabled:opacity-60 transition-colors text-xs font-semibold"
                      >
                        {removingThis ? <Loader2 size={11} className="animate-spin" /> : <Trash2 size={11} />}
                        <span>{removingThis ? t('installed.removing') : t('installed.remove')}</span>
                      </button>
                    </div>
                  );
                      })}
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>
            </GlassPanel>
          </motion.div>
        )}

        {}
        {hasMinimap && (
          <motion.div variants={itemVariants}>
            <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-5 flex items-center gap-4">
              <div className="shrink-0 w-12 h-12 rounded-xl bg-accent/15 text-accent
                              flex items-center justify-center">
                <MapIcon size={20} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
                  {t('installed.minimapLabel')}
                </div>
                <div className="font-display font-bold text-base text-text-primary truncate">
                  {minimap?.name}
                </div>
              </div>
              <button
                type="button"
                onClick={() => onNavigateTo('minimaps')}
                className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl
                           bg-glass border border-glass-border text-sm text-text-secondary
                           hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong
                           transition-colors"
              >
                <span>{t('installed.openInCatalog')}</span>
                <ArrowRight size={13} />
              </button>
              <button
                type="button"
                onClick={() => void onRollbackMinimap()}
                disabled={minimapBusy}
                title={t('installed.rollback')}
                className="inline-flex items-center gap-2 px-3 py-2 rounded-xl
                           bg-red-500/15 text-red-300 hover:bg-red-500/25
                           border border-red-500/30 hover:border-red-500/50
                           disabled:opacity-40 disabled:cursor-not-allowed
                           transition-colors text-sm font-semibold"
              >
                {minimapBusy ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                <span>{t('installed.rollback')}</span>
              </button>
            </GlassPanel>
          </motion.div>
        )}

        {hasReticle && (
          <motion.div variants={itemVariants}>
            <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-5 flex items-center gap-4">
              <div className="shrink-0 w-12 h-12 rounded-xl bg-accent/15 text-accent
                              flex items-center justify-center">
                <TargetIcon size={20} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
                  {t('installed.reticleLabel')}
                </div>
                <div className="font-display font-bold text-base text-text-primary truncate">
                  {reticle?.name}
                </div>
              </div>
              <button
                type="button"
                onClick={() => onNavigateTo('reticles')}
                className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl
                           bg-glass border border-glass-border text-sm text-text-secondary
                           hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong
                           transition-colors"
              >
                <span>{t('installed.openInCatalog')}</span>
                <ArrowRight size={13} />
              </button>
              <button
                type="button"
                onClick={() => void onRollbackReticle()}
                disabled={reticleBusy}
                title={t('installed.rollback')}
                className="inline-flex items-center gap-2 px-3 py-2 rounded-xl
                           bg-red-500/15 text-red-300 hover:bg-red-500/25
                           border border-red-500/30 hover:border-red-500/50
                           disabled:opacity-40 disabled:cursor-not-allowed
                           transition-colors text-sm font-semibold"
              >
                {reticleBusy ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                <span>{t('installed.rollback')}</span>
              </button>
            </GlassPanel>
          </motion.div>
        )}

        {hasArmor && (
          <motion.div variants={itemVariants}>
            <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-5 flex items-center gap-4">
              <div className="shrink-0 w-12 h-12 rounded-xl bg-accent/15 text-accent
                              flex items-center justify-center">
                <ShieldIcon size={20} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
                  {t('installed.armorLabel')}
                </div>
                <div className="font-display font-bold text-base text-text-primary truncate">
                  {armor?.name}
                </div>
              </div>
              <button
                type="button"
                onClick={() => onNavigateTo('armor')}
                className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl
                           bg-glass border border-glass-border text-sm text-text-secondary
                           hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong
                           transition-colors"
              >
                <span>{t('installed.openInCatalog')}</span>
                <ArrowRight size={13} />
              </button>
              <button
                type="button"
                onClick={onArmorRollbackClick}
                disabled={armorBusy}
                title={t('installed.rollback')}
                className="inline-flex items-center gap-2 px-3 py-2 rounded-xl
                           bg-red-500/15 text-red-300 hover:bg-red-500/25
                           border border-red-500/30 hover:border-red-500/50
                           disabled:opacity-40 disabled:cursor-not-allowed
                           transition-colors text-sm font-semibold"
              >
                {armorBusy ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                <span>{t('installed.rollback')}</span>
              </button>
            </GlassPanel>
          </motion.div>
        )}

        {hasSounds && (
          <motion.div variants={itemVariants}>
            <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-5 flex items-center gap-4">
              <div className="shrink-0 w-12 h-12 rounded-xl bg-accent/15 text-accent
                              flex items-center justify-center">
                <Volume2 size={20} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
                  {t('installed.soundsLabel')}
                </div>
                <div className="font-display font-bold text-base text-text-primary truncate">
                  {soundPack?.name}
                </div>
              </div>
              <button
                type="button"
                onClick={() => onNavigateTo('sounds')}
                className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl
                           bg-glass border border-glass-border text-sm text-text-secondary
                           hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong
                           transition-colors"
              >
                <span>{t('installed.openInCatalog')}</span>
                <ArrowRight size={13} />
              </button>
              <button
                type="button"
                onClick={() => void onRollbackSounds()}
                disabled={soundBusy}
                className="inline-flex items-center gap-2 px-3 py-2 rounded-xl
                           bg-red-500/15 text-red-300 hover:bg-red-500/25
                           border border-red-500/30 hover:border-red-500/50
                           disabled:opacity-60 transition-colors text-sm font-semibold"
              >
                {soundBusy ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                <span>{t('installed.rollback')}</span>
              </button>
            </GlassPanel>
          </motion.div>
        )}

        {}
        {userId && (
          <motion.div variants={itemVariants}>
            <HntMyCodesPanel userId={userId} refreshKey={codesRefreshKey} />
          </motion.div>
        )}

        {}
        {hasRedux && (
          <motion.div variants={itemVariants}>
            <div className="flex items-center gap-2 text-xs text-text-muted px-1">
              <AlertTriangle size={12} />
              <span>{t('installed.dirtyHint')}</span>
            </div>
          </motion.div>
        )}
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'info'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={6000}
      />

      {}
      {exportOpen && userId && (
        <HntExportModal
          userId={userId}
          onClose={() => {
            setExportOpen(false);

            setCodesRefreshKey(k => k + 1);
          }}
        />
      )}
      {importOpen && (
        <HntImportModal
          onAppliedRefresh={() => void onAfterImport()}
          onClose={() => setImportOpen(false)}
        />
      )}

      <ConfirmModal
        open={rollbackAllOpen}
        title={t('installed.confirmRollbackAllTitle')}
        message={t('installed.confirmRollbackAllMessage')}
        confirmLabel={rollbackAllBusy ? t('installed.rollingBackAll') : t('installed.rollbackAll')}
        cancelLabel={t('common.cancel')}
        destructive
        onConfirm={() => { void onRollbackAllConfirm(); }}
        onCancel={() => { if (!rollbackAllBusy) setRollbackAllOpen(false); }}
      />

      <ArmorRollbackChoiceModal
        open={armorChoiceOpen}
        reduxName={installedReduxItem?.name ?? installedReduxId ?? '-'}
        currentArmorName={armor?.name ?? ''}
        onRevertToRedux={() => { void runArmorRollback('revert-to-redux'); }}
        onClearArmor={() => { void runArmorRollback('clear'); }}
        onCancel={() => setArmorChoiceOpen(false)}
      />
    </motion.div>
  );
}
