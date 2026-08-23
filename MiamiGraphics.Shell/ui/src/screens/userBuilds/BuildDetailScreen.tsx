import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import {
  Boxes, Layers, Crosshair, Shield, Slash, User, Download,
  Copy, Check, Trash2, AlertTriangle, Eye, Building2, Map as MapIcon,
  Mouse, Keyboard, Monitor, Headphones, Gauge, Video, FileText, ExternalLink,
  Volume2, Play, Star,
} from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { BackButton } from '@/components/BackButton';
import { VideoModal } from '@/components/VideoModal';
import { useUserBuildsStore } from '@/store/userBuildsStore';
import { useSessionStore } from '@/store/sessionStore';
import { ConfirmModal } from '@/components/ConfirmModal';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useGtaSettingsStore } from '@/store/gtaSettingsStore';
import { startBuildInstall, finishBuildInstall, setBuildHistorySuppressed } from '@/store/installProgressStore';
import { useDirtyConfirmStore } from '@/store/dirtyConfirmStore';
import { useLastBuildInstallStore } from '@/store/lastBuildInstallStore';
import { bridge } from '@/bridge';
import { ArmorPreview3D } from '../armor/ArmorPreview3D';
import { BuildReviewsSection } from './BuildReviewsSection';
import { GlbViewerModal } from '../guns/GlbViewerModal';
import { LazyEmbedVideo, videoSlotForUrl } from '@/utils/videoEmbeds';

interface Props {
  buildId: string;
  onBack: () => void;
}

type BigPreview = { url: string; title: string };

function BigPreviewOverlay({ preview }: { preview: BigPreview | null }) {
  return createPortal(
    <AnimatePresence>
      {preview && (
        <motion.div
          key="detail-big-preview"
          initial={{ opacity: 1 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          transition={{ duration: 0.18 }}
          className="fixed inset-0 z-[400] pointer-events-none flex items-center justify-center p-10"
        >
          <div className="absolute inset-0 bg-black/55 backdrop-blur-lg" />
          <motion.div
            initial={{ scale: 0.94 }} animate={{ scale: 1 }}
            transition={{ type: 'spring', stiffness: 280, damping: 26 }}
            className="relative w-[62vw] max-w-[940px] h-[60vh] max-h-[600px]"
          >
            <img src={preview.url} alt={preview.title} draggable={false}
                 className="w-full h-full object-contain select-none drop-shadow-[0_24px_80px_rgba(0,0,0,0.85)]" />
          </motion.div>
          <div className="absolute bottom-10 inset-x-0 flex justify-center px-6">
            <span className="text-lg font-bold text-white tracking-tight">{preview.title}</span>
          </div>
        </motion.div>
      )}
    </AnimatePresence>,
    document.body,
  );
}

export function BuildDetailScreen({ buildId, onBack }: Props) {
  const { t } = useTranslation();
  const builds = useUserBuildsStore(s => s.builds);
  const removeBuild = useUserBuildsStore(s => s.remove);
  const incrementDownloads = useUserBuildsStore(s => s.incrementDownloads);
  const incrementViews = useUserBuildsStore(s => s.incrementViews);
  const setLastBuildInstall = useLastBuildInstallStore(s => s.set);
  const build = useMemo(() => builds.find(b => b.id === buildId) ?? null, [builds, buildId]);

  const auth = useSessionStore(s => s.auth);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const canDelete = userId !== null && build?.authorUserId === userId;

  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const [rating, setRating] = useState<{ avg: number; count: number }>({ avg: 0, count: 0 });
  useEffect(() => {
    let cancelled = false;
    bridge.userBuildReviewsList(buildId)
      .then(list => {
        if (cancelled) return;
        const count = list.length;
        const avg = count ? list.reduce((s, r) => s + r.rating, 0) / count : 0;
        setRating({ avg, count });
      })
      .catch(() => {  });
    return () => { cancelled = true; };
  }, [buildId]);
  const onConfirmDelete = async () => {
    if (!build || deleting) return;
    setDeleting(true);
    try {
      await removeBuild(build.id);
      setConfirmDelete(false);
      onBack();
    } catch (err) {
      console.warn('[buildDetail] delete failed', err);
      setDeleting(false);
    }
  };

  const reduxList = useReduxStore(s => s.items) ?? [];
  const loadReduxes = useReduxStore(s => s.load);
  const installRedux = useReduxStore(s => s.install);

  const markReduxInstalled = useReduxStore(s => s.markInstalled);
  const publicPacks = useGunpackStore(s => s.publicPacks) ?? [];
  const loadPublicPacks = useGunpackStore(s => s.loadPublicPacks);
  const selectPack = useGunpackStore(s => s.selectPack);
  const selectedGuns = useGunpackStore(s => s.selectedGuns) ?? [];
  const loadAllGuns = useGunpackStore(s => s.loadAllGuns);
  const allGuns = useGunpackStore(s => s.allGuns) ?? [];
  const settingsPresets = useGtaSettingsStore(s => s.publicPresets) ?? [];
  const loadSettingsPresets = useGtaSettingsStore(s => s.loadPublicPresets);

  useEffect(() => {
    if (reduxList.length === 0)  void loadReduxes();
    if (publicPacks.length === 0) void loadPublicPacks();
    if (settingsPresets.length === 0) void loadSettingsPresets();
    void loadAllGuns();
  }, []);

  const [allReduxes, setAllReduxes] = useState<typeof reduxList>([]);
  useEffect(() => {
    let cancelled = false;
    bridge.reduxList(undefined, undefined)
      .then(list => { if (!cancelled) setAllReduxes(list); })
      .catch(e => console.warn('[build-detail] reduxList(all) failed', e));
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (build?.gunpackId) void selectPack(build.gunpackId);
    return () => { void selectPack(null); };
  }, [build?.gunpackId]);

  useEffect(() => {
    const id = build?.id;
    if (!id) return;
    const KEY = 'hg.viewedBuilds';
    let seen: string[] = [];
    try { seen = JSON.parse(localStorage.getItem(KEY) || '[]'); } catch { seen = []; }
    if (seen.includes(id)) return;
    seen.push(id);
    try { localStorage.setItem(KEY, JSON.stringify(seen.slice(-500))); } catch {  }
    void incrementViews(id);
  }, [build?.id]);

  const reduxById = useMemo(() => {
    const m = new Map<string, typeof reduxList[number]>();
    const source = allReduxes.length > 0 ? allReduxes : reduxList;
    for (const r of source) m.set(r.id, r);
    return m;
  }, [allReduxes, reduxList]);
  const flatById = useMemo(() => {
    const m = new Map<string, typeof allGuns[number]>();
    for (const g of allGuns) m.set(`${g.packId}::${g.gunId}`, g);
    return m;
  }, [allGuns]);

  const [hntCopied, setHntCopied] = useState(false);
  const [installing, setInstalling] = useState(false);
  const [installResult, setInstallResult] = useState<{ ok: boolean; message: string } | null>(null);
  const [glbView, setGlbView] = useState<{ url: string | null; title: string; kind: 'gun' | 'armor' } | null>(null);

  const [sectionOn, setSectionOn] = useState({
    gunpack: true, armor: true, arena: true, minimap: true, reticle: true, sounds: true,
  });
  const toggleSection = (k: keyof typeof sectionOn) =>
    setSectionOn(s => ({ ...s, [k]: !s[k] }));

  const [armorLibraryItems, setArmorLibraryItems] = useState<import('@/bridge/types').ArmorLibraryItem[]>([]);
  useEffect(() => {
    let alive = true;
    bridge.armorLibraryList()
      .then(rows => { if (alive) setArmorLibraryItems(rows ?? []); })
      .catch(e => console.warn('[buildDetail.armorLibrary] fail:', e));
    return () => { alive = false; };
  }, []);

  const [minimapLibraryItems, setMinimapLibraryItems] = useState<import('@/bridge/types').LibraryComponent[]>([]);
  useEffect(() => {
    let alive = true;
    bridge.libraryList('minimap')
      .then(rows => { if (alive) setMinimapLibraryItems(rows ?? []); })
      .catch(e => console.warn('[buildDetail.minimapLibrary] fail:', e));
    return () => { alive = false; };
  }, []);
  const [reticleLibraryItems, setReticleLibraryItems] = useState<import('@/bridge/types').LibraryComponent[]>([]);
  useEffect(() => {
    let alive = true;
    bridge.libraryList('crosshair')
      .then(rows => { if (alive) setReticleLibraryItems(rows ?? []); })
      .catch(e => console.warn('[buildDetail.reticleLibrary] fail:', e));
    return () => { alive = false; };
  }, []);
  const [soundsLibraryItems, setSoundsLibraryItems] = useState<import('@/bridge/types').LibraryComponent[]>([]);
  useEffect(() => {
    let alive = true;
    bridge.libraryList('sounds')
      .then(rows => { if (alive) setSoundsLibraryItems(rows ?? []); })
      .catch(e => console.warn('[buildDetail.soundsLibrary] fail:', e));
    return () => { alive = false; };
  }, []);
  const [bigPreview, setBigPreview] = useState<BigPreview | null>(null);
  const hoverTimer = useRef<number | null>(null);
  useEffect(() => () => { if (hoverTimer.current != null) clearTimeout(hoverTimer.current); }, []);

  if (!build) {
    return (
      <div className="h-full overflow-y-auto">
        <div className="max-w-3xl mx-auto px-8 py-10 flex flex-col gap-4">
          <BackButton
            onClick={onBack}
            label={t('userBuilds.back', 'Назад к сборкам')}
            className="self-start"
          />
          <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-10 text-center">
            <AlertTriangle size={28} className="mx-auto text-status-warning" />
            <h2 className="text-lg font-bold text-text-primary mt-3">
              {t('userBuilds.detailMissing', 'Сборка не найдена')}
            </h2>
            <p className="text-sm text-text-muted mt-1">
              {t('userBuilds.detailMissingHint', 'Возможно, она была удалена. Вернитесь к списку сборок.')}
            </p>
          </GlassPanel>
        </div>
      </div>
    );
  }

  const liveRedux = reduxById.get(build.reduxId);
  const livePack  = publicPacks.find(p => p.id === build.gunpackId);
  const reduxAvailable = !!liveRedux;
  const packAvailable  = !build.gunpackId || !!livePack;
  const reduxName = liveRedux?.name ?? build.reduxNameSnapshot;
  const packName  = livePack?.name ?? build.gunpackNameSnapshot;

  const reduxCover = build.coverUrl || liveRedux?.previewUrl || null;

  const armorSel = build.armor ?? null;
  const armorDonor =
    armorSel?.kind === 'override' ? reduxById.get(armorSel.reduxId) ?? null : null;
  const armorLibraryItem =
    armorSel?.kind === 'library'
      ? armorLibraryItems.find(a => a.id === armorSel.armorLibraryId) ?? null
      : null;

  const armorGlbUrl =
    armorSel?.kind === 'library'
      ? armorLibraryItem?.glbUrl ?? null
    : armorSel?.kind === 'none'
      ? null
    : (armorSel?.kind === 'override' ? armorDonor : liveRedux)?.components?.armor?.glbUrl ?? null;
  const armorPreviewUrl =
    armorSel?.kind === 'library'
      ? armorLibraryItem?.previewUrl ?? null
    : armorSel?.kind === 'none'
      ? null
    : (armorSel?.kind === 'override' ? armorDonor : liveRedux)?.componentScreenshots?.armor ?? null;
  const armorLabel =
    armorSel?.kind === 'none'
      ? t('userBuilds.armorNone', 'без брони')
      : armorSel?.kind === 'override'
        ? t('userBuilds.armorFromName', 'броня из: {{name}}', { name: armorDonor?.name ?? t('userBuilds.armorOtherRedux', 'броня из другого редукса') })
      : armorSel?.kind === 'library'
        ? t('userBuilds.armorCustomName', 'кастомная: {{name}}', { name: armorLibraryItem?.name ?? armorSel.armorLibraryId })
        : t('userBuilds.armorFromName', 'броня из: {{name}}', { name: reduxName });

  type ArmorCardKind = 'override' | 'library' | 'base';
  const armorCardKind: ArmorCardKind =
    armorSel?.kind === 'override' ? 'override'
    : armorSel?.kind === 'library' ? 'library'
    : 'base';
  const armorRedux =
    armorSel?.kind === 'override' ? armorDonor
    : armorSel?.kind === 'library' ? null
    : armorSel?.kind === 'none'    ? null
    : liveRedux ?? null;
  const armorDisplayName: string =
    armorSel?.kind === 'library'
      ? (armorLibraryItem?.name ?? armorSel.armorLibraryId)
      : (armorRedux?.name ?? '-');
  const armorDisplayAuthor: string | null =
    armorSel?.kind === 'library'
      ? (armorLibraryItem?.author ?? null)
      : (armorRedux?.author ?? null);

  const arenaSel = build.arena ?? null;
  const arenaDonor =
    arenaSel?.kind === 'override' ? reduxById.get(arenaSel.reduxId) ?? null : null;
  const arenaRedux = arenaSel?.kind === 'override' ? arenaDonor : liveRedux ?? null;
  const arenaLabel =
    arenaSel?.kind === 'override'
      ? t('userBuilds.arenaFromName', 'арена из: {{name}}', { name: arenaDonor?.name ?? t('userBuilds.arenaOtherRedux', 'арена из другого редукса') })
      : t('userBuilds.arenaFromName', 'арена из: {{name}}', { name: reduxName });

  const minimapSel = build.minimap ?? null;
  const minimapDonor =
    minimapSel?.kind === 'override' ? reduxById.get(minimapSel.reduxId) ?? null : null;
  const minimapLib =
    minimapSel?.kind === 'library'
      ? minimapLibraryItems.find(l => l.id === minimapSel.minimapLibraryId) ?? null
      : null;

  const minimapDisplay = {
    name: minimapDonor?.name ?? minimapLib?.name ?? '-',
    author: minimapDonor?.author ?? minimapLib?.author ?? null,
    coverUrl:
      minimapDonor?.componentScreenshots?.minimap
      || minimapLib?.previewUrl
      || minimapDonor?.previewUrl
      || null,
  };

  const reticleSel = build.reticle ?? null;
  const reticleDonor =
    reticleSel?.kind === 'override' ? reduxById.get(reticleSel.reduxId) ?? null : null;
  const reticleLib =
    reticleSel?.kind === 'library'
      ? reticleLibraryItems.find(l => l.id === reticleSel.reticleLibraryId) ?? null
      : null;

  const reticleDisplay = {
    name: reticleDonor?.name ?? reticleLib?.name ?? '-',
    author: reticleDonor?.author ?? reticleLib?.author ?? null,
    coverUrl:
      reticleDonor?.componentScreenshots?.crosshair
      || reticleLib?.previewUrl
      || reticleDonor?.previewUrl
      || null,
  };

  const makeHover = (url: string | null, title: string) =>
    url ? {
      onMouseEnter: () => {
        if (hoverTimer.current != null) clearTimeout(hoverTimer.current);
        hoverTimer.current = window.setTimeout(() => setBigPreview({ url, title }), 2000);
      },
      onMouseLeave: () => {
        if (hoverTimer.current != null) clearTimeout(hoverTimer.current);
        setBigPreview(null);
      },
    } : {};
  const minimapHover = makeHover(minimapDisplay.coverUrl, minimapDisplay.name);
  const reticleHover = makeHover(reticleDisplay.coverUrl, reticleDisplay.name);

  const soundsSel = build.sounds ?? null;
  const soundsLib =
    soundsSel?.kind === 'library'
      ? soundsLibraryItems.find(l => l.id === soundsSel.soundsLibraryId) ?? null
      : null;

  const soundsDisplay = {
    name:    soundsLib?.name ?? (soundsSel?.kind === 'library' ? soundsSel.soundsLibraryId : '-'),
    author:  soundsLib?.author ?? null,
    coverUrl: soundsLib?.previewUrl || null,
    videoUrl: soundsLib?.previewVideoUrl || null,
  };

  const visibleGuns = selectedGuns.filter(g => !g.isHidden).sort((a, b) => a.sortOrder - b.sortOrder);

  const hntCode = build.hntCode;

  const handleCopyHnt = async () => {
    try {
      await navigator.clipboard.writeText(hntCode);
      setHntCopied(true);
      window.setTimeout(() => setHntCopied(false), 2000);
    } catch {

    }
  };

  const handleInstall = async () => {
    if (installing) return;
    if (!reduxAvailable || (sectionOn.gunpack && !packAvailable)) {
      setInstallResult({
        ok: false,
        message: t('userBuilds.installCatalogMissing', 'Редукс или ганпак из этой сборки недоступен в каталоге.'),
      });
      return;
    }
    setInstalling(true);
    setInstallResult(null);

    const slotsSnapshot = build.gunSlots ?? {};
    const crossPackOverridesPlanned = sectionOn.gunpack ? visibleGuns.filter(g => {
      const internal = `${g.weaponPrefix}${g.baseName}`;
      const slot = slotsSnapshot[internal];
      return slot?.kind === 'override' && slot.gunpackId !== build.gunpackId;
    }).length : 0;
    const armorDonorReduxId =
      sectionOn.armor && armorSel?.kind === 'override' && armorSel.reduxId !== build.reduxId
        ? armorSel.reduxId
        : null;
    const hasArmorClear = sectionOn.armor && armorSel?.kind === 'none';
    const armorLibraryId =
      sectionOn.armor && armorSel?.kind === 'library' ? armorSel.armorLibraryId : null;

    startBuildInstall({
      buildId:           build.id,
      buildName:         build.name,
      reduxId:           build.reduxId,
      gunpackId:         build.gunpackId,
      armorDonorReduxId,
      hasArmorClear,
      armorLibraryId,
      selgunsPlanned:    crossPackOverridesPlanned,
    });
    try {

      const arenaDonorId =
        sectionOn.arena && build.arena?.kind === 'override' && build.arena.reduxId !== build.reduxId
          ? build.arena.reduxId
          : null;

      const minimapDonorReduxId =
        sectionOn.minimap && build.minimap?.kind === 'override' && build.minimap.reduxId !== build.reduxId
          ? build.minimap.reduxId
          : null;
      const minimapLibraryId =
        sectionOn.minimap && build.minimap?.kind === 'library' ? build.minimap.minimapLibraryId : null;

      const reticleDonorReduxId =
        sectionOn.reticle && build.reticle?.kind === 'override' && build.reticle.reduxId !== build.reduxId
          ? build.reticle.reduxId
          : null;
      const reticleLibraryId =
        sectionOn.reticle && build.reticle?.kind === 'library' ? build.reticle.reticleLibraryId : null;

      const needsCustomize = !!arenaDonorId
        || !!minimapDonorReduxId || !!minimapLibraryId
        || !!reticleDonorReduxId || !!reticleLibraryId;

      console.log('[buildInstall] customize diagnostic:', {
        buildId:       build.id,
        buildName:     build.name,
        buildReduxId:  build.reduxId,
        rawArena:      build.arena,
        rawMinimap:    build.minimap,
        rawReticle:    build.reticle,
        arenaDonorId,
        minimapDonorReduxId,
        minimapLibraryId,
        reticleDonorReduxId,
        reticleLibraryId,
        willCustomize: needsCustomize,
      });

      let reduxRes: { success: boolean; errorMessage: string | null; };
      if (needsCustomize) {
        const draft = {
          reduxId:   build.reduxId,
          bloodfx:   { kind: 'default' as const },

          crosshair: reticleLibraryId
            ? { kind: 'library' as const, libraryItemId: reticleLibraryId }
            : reticleDonorReduxId
              ? { kind: 'import'  as const, donorReduxId:  reticleDonorReduxId }
              : { kind: 'default' as const },
          timecycle: { kind: 'default' as const },
          armor:     { kind: 'default' as const },
          arena: arenaDonorId
            ? { kind: 'import' as const, donorReduxId: arenaDonorId }
            : { kind: 'default' as const },
          minimap: {

            enabled:             !!(minimapDonorReduxId || minimapLibraryId),
            hpColor:             '#34D399',
            armorColor:          '#60A5FA',
            aspectRatio:         '16:9',
            position:            'default',
            pngOverlayPath:      null,
            importedFromReduxId: minimapDonorReduxId,
            donorVersionId:      null,
            libraryItemId:       minimapLibraryId,
          },
          tracers: {
            sourceKind:      'default' as const,
            modelFolderName: null,
            donorReduxId:    null,
            r: 255, g: 255, b: 255,
            donorVersionId:  null,
          },
        };
        reduxRes = await bridge.reduxCustomizeApply(build.reduxId, draft);

        if (reduxRes.success) markReduxInstalled(build.reduxId);
      } else {
        reduxRes = await installRedux(build.reduxId);
      }
      if (!reduxRes.success) {
        if (reduxRes.errorMessage === 'DIRTY_FILES_NEED_CONFIRM') {
          const waitMsg = t(
            'userBuilds.dirtyCard',
            'update.rpf изменён вне лаунчера - нужно подтверждение сброса к чистой GTA.',
          );
          finishBuildInstall(build.id, 'error', waitMsg);
          useDirtyConfirmStore.getState().open({
            title: t('userBuilds.dirtyTitle', 'Файлы GTA модифицированы'),
            message: t(
              'userBuilds.dirtyMessage',
              'В update.rpf уже есть сторонние моды (не наши). Перед установкой сборки мы восстановим чистый update.rpf из бекапа лаунчера - твои текущие моды в нём пропадут.\n\nПродолжить?',
            ),
            cancelLabel: t('settings.cache.cancel', 'Отмена'),
            actions: [{
              label: t('userBuilds.dirtyAction', 'Восстановить и установить'),
              kind: 'danger',
              run: async () => {
                const ok = await bridge.backupRestoreClean();
                if (!ok) {
                  setInstallResult({
                    ok: false,
                    message: t(
                      'userBuilds.cleanMissing',
                      'Чистый update.rpf не найден в локальном бекапе. Запусти бекап со стартового экрана.',
                    ),
                  });
                  return;
                }
                void handleInstall();
              },
            }],
          });
          return;
        }
        const msg = reduxRes.errorMessage ?? t('userBuilds.errReduxInstall', 'Ошибка установки редукса');
        finishBuildInstall(build.id, 'error', msg);
        setInstallResult({ ok: false, message: msg });
        return;
      }

      if (sectionOn.armor) {
        if (armorSel?.kind === 'override' && armorSel.reduxId !== build.reduxId) {
          const r = await bridge.reduxApplyArmorSwap(armorSel.reduxId, null);
          if (!r.success) {
            const msg = r.errorMessage ?? t('userBuilds.errArmorSwap', 'Ошибка свапа брони');
            finishBuildInstall(build.id, 'error', msg);
            setInstallResult({ ok: false, message: msg });
            return;
          }
        } else if (armorSel?.kind === 'none') {
          const r = await bridge.reduxClearArmor();
          if (!r.success) {
            const msg = r.errorMessage ?? t('userBuilds.errArmorClear', 'Ошибка очистки брони');
            finishBuildInstall(build.id, 'error', msg);
            setInstallResult({ ok: false, message: msg });
            return;
          }
        } else if (armorSel?.kind === 'library') {

          const r = await bridge.armorLibraryInstall(armorSel.armorLibraryId, true);
          if (!r.success) {
            const msg = r.errorMessage ?? t('userBuilds.errArmorLibrary', 'Ошибка установки кастомной брони');
            finishBuildInstall(build.id, 'error', msg);
            setInstallResult({ ok: false, message: msg });
            return;
          }
        }
      }

      if (sectionOn.gunpack) {
      if (build.gunpackId) {
        const slots = build.gunSlots ?? {};
        const gunIds: string[] = [];
        for (const g of visibleGuns) {
          const internal = `${g.weaponPrefix}${g.baseName}`;
          const slot = slots[internal];
          if (!slot) {
            gunIds.push(g.id);
          } else if (slot.kind === 'override' && slot.gunpackId === build.gunpackId) {
            gunIds.push(slot.gunId);
          }
        }
        const packRes = await bridge.gunpackInstallSelected(build.gunpackId, gunIds);
        if (!packRes.success) {
          const msg = packRes.errorMessage ?? t('userBuilds.errGunsInstall', 'Ошибка установки пушек');
          finishBuildInstall(build.id, 'error', msg);
          setInstallResult({ ok: false, message: msg });
          return;
        }
      }

      const crossPackOverrides = visibleGuns
        .map(g => ({ g, internal: `${g.weaponPrefix}${g.baseName}` }))
        .map(({ internal }) => {
          const slot = slotsSnapshot[internal];
          if (slot?.kind === 'override' && slot.gunpackId !== build.gunpackId) {
            return { internal, donorPackId: slot.gunpackId };
          }
          return null;
        })
        .filter((x): x is { internal: string; donorPackId: string } => x !== null);

      try { await bridge.selectedGunsUninstallAll(); } catch {  }

      for (const ov of crossPackOverrides) {
        const r = await bridge.selectedGunsInstall(ov.donorPackId, ov.internal);
        if (!r.success) {
          const msg = r.errorMessage ?? t('userBuilds.errCrossPack', 'Ошибка cross-pack override ({{slot}})', { slot: ov.internal });
          finishBuildInstall(build.id, 'error', msg);
          setInstallResult({ ok: false, message: msg });
          return;
        }
      }
      }

      if (sectionOn.sounds && build.sounds?.kind === 'library') {
        const soundsLibName = soundsLib?.name ?? build.sounds.soundsLibraryId;
        const sr = await bridge.soundPackInstall(build.sounds.soundsLibraryId, soundsLibName);
        if (!sr.success) {
          const msg = sr.errorMessage ?? t('userBuilds.errSoundsInstall', 'Ошибка установки звуков');
          finishBuildInstall(build.id, 'error', msg);
          setInstallResult({ ok: false, message: msg });
          return;
        }
      }

      if (build.settingsXmlUrl) {
        const sr = await bridge.gtaSettingsApplyFromUrl(build.settingsXmlUrl);
        if (!sr.success) {
          const msg = sr.errorMessage ?? t('userBuilds.errSettingsApply', 'Ошибка применения сеттингов');
          finishBuildInstall(build.id, 'error', msg);
          setInstallResult({ ok: false, message: msg });
          return;
        }
      }

      finishBuildInstall(build.id, 'done', null);
      void bridge.activityLog('build_install', `сборка «${build.name}»`);
      incrementDownloads(build.id);

      const snapMinimapKind: 'redux' | 'library' | null = build.minimap?.kind === 'override'
        ? 'redux'
        : build.minimap?.kind === 'library'
          ? 'library'
          : null;
      const snapMinimapId: string | null = build.minimap?.kind === 'override'
        ? build.minimap.reduxId
        : build.minimap?.kind === 'library'
          ? build.minimap.minimapLibraryId
          : null;
      const snapReticleKind: 'redux' | 'library' | null = build.reticle?.kind === 'override'
        ? 'redux'
        : build.reticle?.kind === 'library'
          ? 'library'
          : null;
      const snapReticleId: string | null = build.reticle?.kind === 'override'
        ? build.reticle.reduxId
        : build.reticle?.kind === 'library'
          ? build.reticle.reticleLibraryId
          : null;

      const snapArenaKind: 'redux' | null = build.arena?.kind === 'override' ? 'redux' : null;
      const snapArenaId: string | null    = build.arena?.kind === 'override' ? build.arena.reduxId : null;
      const snapArenaName: string | null  = build.arena?.kind === 'override'
        ? (arenaRedux?.name ?? null)
        : null;
      setLastBuildInstall({
        buildId:           build.id,
        buildName:         build.name,
        reduxId:           build.reduxId,
        reduxName:         reduxName ?? null,
        gunpackId:         build.gunpackId,
        gunpackName:       packName ?? null,
        armorLibraryId:    armorLibraryId,
        armorName:         armorSel?.kind === 'library' ? (armorLibraryItem?.name ?? null) : null,
        soundsLibraryId:   build.sounds?.kind === 'library' ? build.sounds.soundsLibraryId : null,
        soundsLibraryName: soundsLib?.name ?? null,
        minimapKind:       snapMinimapKind,
        minimapId:         snapMinimapId,
        minimapName:       minimapDisplay.name === '-' ? null : minimapDisplay.name,
        reticleKind:       snapReticleKind,
        reticleId:         snapReticleId,
        reticleName:       reticleDisplay.name === '-' ? null : reticleDisplay.name,
        arenaKind:         snapArenaKind,
        arenaId:           snapArenaId,
        arenaName:         snapArenaName,
        selgunsCount:      crossPackOverridesPlanned,
        installedAt:       Date.now(),
      });
      setInstallResult({
        ok: true,
        message: t('userBuilds.installDone', 'Сборка установлена. Заходи в игру!'),
      });
    } catch (e) {
      const msg = e instanceof Error ? e.message : t('common.somethingWentWrong', 'Что-то пошло не так');
      finishBuildInstall(build.id, 'error', msg);
      setInstallResult({ ok: false, message: msg });
    } finally {
      setInstalling(false);
      setBuildHistorySuppressed(false);
    }
  };

  const installBlocked = installing || !reduxAvailable || (sectionOn.gunpack && !packAvailable);
  const excludedSections = [
    !sectionOn.gunpack && t('userBuilds.gunpackSection', 'Ганпак'),
    !sectionOn.armor && t('userBuilds.armorSection', 'Бронежилет'),
    !sectionOn.arena && t('userBuilds.arenaSection', 'Арена'),
    !sectionOn.minimap && build.minimap && t('userBuilds.minimapTag', 'Миникарта'),
    !sectionOn.reticle && build.reticle && t('userBuilds.reticleTag', 'Прицел'),
    !sectionOn.sounds && build.sounds && t('userBuilds.soundsTag', 'Звуки'),
  ].filter(Boolean) as string[];

  const pageV: Variants = {
    hidden:  { opacity: 0, y: 12 },
    visible: { opacity: 1, y: 0, transition: { duration: 0.32, ease: EASE_DEPTH, staggerChildren: 0.08 } },
  };
  const sectionV: Variants = {
    hidden:  { opacity: 0, y: 14 },
    visible: { opacity: 1, y: 0, transition: { duration: 0.4, ease: EASE_DEPTH } },
  };
  const cardV: Variants = {
    hidden:  { opacity: 0, y: 8, scale: 0.97 },
    visible: { opacity: 1, y: 0, scale: 1, transition: { duration: 0.3, ease: EASE_DEPTH } },
  };
  const gunGridV: Variants = {
    hidden:  {},
    visible: { transition: { staggerChildren: 0.045, delayChildren: 0.04 } },
  };

  return (
    <motion.div
      variants={pageV}
      initial="hidden"
      animate="visible"
      className="h-full overflow-y-auto"
    >
      <div className="max-w-7xl 2xl:max-w-[1700px] mx-auto px-8 py-6 flex flex-col gap-5">
        {}
        <motion.div variants={sectionV} className="flex items-center justify-between gap-3">
          <BackButton onClick={onBack} label={t('userBuilds.back', 'Назад к сборкам')} />
          {canDelete && (
            <button
              type="button"
              onClick={() => setConfirmDelete(true)}
              style={{ outline: 'none' }}
              className="shrink-0 inline-flex items-center gap-1.5 h-9 px-3 rounded-lg
                         text-[11.5px] text-text-muted hover:text-status-error hover:bg-status-error/10
                         border border-transparent hover:border-status-error/20
                         transition-colors"
              title={t('userBuilds.delete', 'Удалить сборку')}
            >
              <Trash2 size={12} />
              <span className="hidden sm:inline">{t('userBuilds.delete', 'Удалить сборку')}</span>
            </button>
          )}
        </motion.div>

        {}
        <motion.div variants={sectionV}>
          <div
            className="relative overflow-hidden rounded-3xl"
            style={{
              background:
                'linear-gradient(135deg, color-mix(in srgb, var(--accent) 9%, var(--bg-elevated)) 0%, var(--bg-elevated) 60%)',
              boxShadow:
                '0 0 0 1px color-mix(in srgb, var(--accent) 22%, transparent), '
                + '0 0 0 6px color-mix(in srgb, var(--accent) 5%, transparent), '
                + '0 32px 70px -28px color-mix(in srgb, var(--accent) 50%, transparent), '
                + '0 16px 32px -16px rgba(0,0,0,0.55)',
            }}
          >
            {}
            {reduxCover && (
              <div
                aria-hidden
                className="absolute inset-0 pointer-events-none"
                style={{
                  background: `url(${reduxCover}) center / cover no-repeat`,
                  filter: 'blur(40px) brightness(0.55) saturate(1.1)',
                  opacity: 0.45,
                  transform: 'scale(1.15)',
                }}
              />
            )}
            {}
            <span
              aria-hidden
              className="pointer-events-none absolute -top-24 -left-24 w-[520px] h-[520px] rounded-full"
              style={{
                background:
                  'radial-gradient(circle at 35% 35%, color-mix(in srgb, var(--accent) 28%, transparent), transparent 70%)',
                filter: 'blur(60px)',
              }}
            />
            {}
            <span
              aria-hidden
              className="pointer-events-none absolute inset-x-0 bottom-0 h-3/4"
              style={{
                background:
                  'linear-gradient(to top, var(--bg-elevated) 0%, color-mix(in srgb, var(--bg-elevated) 80%, transparent) 35%, transparent 100%)',
              }}
            />

            <div className="relative flex flex-col gap-5 p-5">
              <div
                className="relative w-full rounded-xl overflow-hidden bg-glass"
                style={{
                  boxShadow:
                    'inset 0 0 0 1px color-mix(in srgb, var(--accent) 28%, transparent), '
                    + '0 14px 30px -16px rgba(0,0,0,0.55)',
                }}
              >
                {reduxCover ? (
                  <img
                    src={reduxCover}
                    alt={build.name}
                    draggable={false}
                    className="block w-full max-h-[56vh] object-cover select-none"
                  />
                ) : (
                  <div className="w-full aspect-[16/6] flex items-center justify-center text-text-muted"
                       style={{ background: 'linear-gradient(135deg, color-mix(in srgb, var(--accent) 30%, #0a0a14), #0a0a14)' }}>
                    <Layers size={32} strokeWidth={1.2} />
                  </div>
                )}
                {}
                <span aria-hidden className="absolute inset-0 pointer-events-none"
                  style={{ boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.06)' }} />
                {}
                <span
                  className="absolute top-2 left-2 inline-flex items-center gap-1 h-5 px-2
                             rounded-full text-[9px] font-bold uppercase tracking-[0.18em]
                             bg-black/55 backdrop-blur-md text-white
                             border border-white/15"
                >
                  <Boxes size={9} strokeWidth={2.5} />
                  {t('userBuilds.heroEyebrow', 'Сборка')}
                </span>
              </div>

              {}
              <div className="flex flex-col gap-3.5 justify-center min-w-0">
                <div className="flex flex-col gap-1 min-w-0">
                  <span className="text-[9px] uppercase tracking-[0.28em] text-accent font-bold">
                    {t('userBuilds.heroKicker', 'Пользовательская сборка')}
                  </span>
                  <h1
                    className="text-[22px] md:text-[26px] font-bold tracking-tight text-text-primary leading-[1.1] break-words"
                    style={{ textShadow: '0 1px 6px rgba(0,0,0,0.45)' }}
                  >
                    {build.name}
                  </h1>
                  <span className="inline-flex items-center gap-1.5 text-[12px] text-text-secondary mt-0.5">
                    <User size={11} className="opacity-70" />
                    <span className="truncate">{build.author}</span>
                    <span className="opacity-40">·</span>
                    <Download size={11} className="opacity-70" />
                    <span className="text-text-muted">
                      {t('userBuilds.installs', '{{n}} установок', { n: build.downloadCount })}
                    </span>
                    <span className="opacity-40">·</span>
                    <Eye size={11} className="opacity-70" />
                    <span className="text-text-muted">
                      {t('userBuilds.views', '{{n}} просмотров', { n: build.viewCount })}
                    </span>
                    <span className="opacity-40">·</span>
                    <Star size={11} className={rating.count ? 'text-yellow-400 fill-current' : 'opacity-70'} />
                    <span className="text-text-muted">
                      {rating.count > 0
                        ? t('userBuilds.ratingValue', '{{avg}} ({{n}})', { avg: rating.avg.toFixed(1), n: rating.count })
                        : t('userBuilds.ratingNone', 'нет оценок')}
                    </span>
                  </span>
                </div>

                {}
                <div className="flex flex-wrap items-stretch gap-2">
                  <HeroMetaChip
                    icon={<Layers size={14} strokeWidth={2.4} />}
                    label={t('userBuilds.reduxSection', 'Редукс')}
                    value={reduxName}
                    sub={null}
                    warn={!reduxAvailable}
                    warnHint={t('userBuilds.detailReduxMissing', 'Редукс не найден в каталоге.')}
                  />
                  <HeroMetaChip
                    icon={<Crosshair size={14} strokeWidth={2.4} />}
                    label={t('userBuilds.gunpackSection', 'Ганпак')}
                    value={packName}
                    sub={null}
                    warn={!packAvailable}
                    warnHint={t('userBuilds.detailPackMissing', 'Ганпак не найден в каталоге.')}
                  />
                  <button
                    type="button"
                    onClick={handleCopyHnt}
                    style={{ outline: 'none' }}
                    className={
                      'group/hnt relative inline-flex items-center gap-2 h-[44px] px-3 rounded-xl '
                      + 'transition-[background-color,box-shadow] duration-200'
                    }
                    title={hntCode}
                  >
                    <span
                      aria-hidden
                      className="absolute inset-0 rounded-xl pointer-events-none"
                      style={{
                        background: hntCopied
                          ? 'color-mix(in srgb, var(--status-success) 18%, transparent)'
                          : 'color-mix(in srgb, var(--accent) 14%, transparent)',
                        boxShadow: hntCopied
                          ? 'inset 0 0 0 1px color-mix(in srgb, var(--status-success) 60%, transparent)'
                          : 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 32%, transparent)',
                      }}
                    />
                    <span className={'relative shrink-0 ' + (hntCopied ? 'text-status-success' : 'text-accent')}>
                      {hntCopied ? <Check size={12} strokeWidth={2.6} /> : <Copy size={12} strokeWidth={2.4} />}
                    </span>
                    <span className="relative flex flex-col items-start gap-0 text-left">
                      <span className="text-[8.5px] uppercase tracking-[0.18em] text-text-muted font-semibold">
                        {hntCopied
                          ? t('userBuilds.hntCopied', 'Скопировано')
                          : t('userBuilds.hntCopy',   'HNT-код · клик')}
                      </span>
                      <code
                        className="font-mono text-[11px] text-text-primary truncate max-w-[140px] select-all"
                        style={{ wordBreak: 'break-all' }}
                      >
                        {hntCode}
                      </code>
                    </span>
                  </button>
                  <button
                    type="button"
                    onClick={handleInstall}
                    disabled={installBlocked}
                    style={{ outline: 'none' }}
                    className="ml-auto shrink-0 inline-flex items-center gap-2 px-5 h-[44px] rounded-xl
                               text-[11.5px] font-bold uppercase tracking-wider
                               bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                               hover:bg-bg-elevated/80 hover:border-white/[0.20]
                               disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                  >
                    <Download size={14} strokeWidth={2.4} />
                    {installing ? t('userBuilds.installing', 'Ставим...') : t('userBuilds.installFull', 'Поставить в GTA')}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </motion.div>

        <motion.div variants={sectionV}>
          <GlassPanel depth="z2" tint="ultra" highlight rounded="3xl" className="p-5 md:p-6 flex flex-col gap-6">

        {}
        <motion.div variants={sectionV}>
          <SectionHeader
            icon={<Boxes size={14} className="text-accent" />}
            title={t('userBuilds.gunsSection', 'Заменяемые пушки')}
            subtitle={
              !packAvailable
                ? t('userBuilds.detailPackMissing', 'Ганпак не найден в каталоге.')
                : visibleGuns.length === 0
                  ? t('userBuilds.gunsLoading', 'Загружаем содержимое пака...')
                  : t('userBuilds.detailGunsCount', '{{n}} пушек в паке', { n: visibleGuns.length })
            }
            enabled={sectionOn.gunpack}
            onToggle={() => toggleSection('gunpack')}
          />
          {packAvailable && visibleGuns.length > 0 && (
            <motion.div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-2.5 mt-3"
                 style={{ opacity: sectionOn.gunpack ? 1 : 0.4, filter: sectionOn.gunpack ? undefined : 'grayscale(1)', transition: 'opacity .2s, filter .2s' }}
                 variants={gunGridV} initial="hidden" animate="visible">
              {visibleGuns.map((g) => {
                const internal = `${g.weaponPrefix}${g.baseName}`;
                const slot = (build.gunSlots ?? {})[internal] ?? null;
                let displayName  = g.displayName || g.baseName;
                let displayCover = g.previewUrl;
                let displayGlb   = g.glbUrl;
                let stateText    = t('userBuilds.slotDefault', 'базовый');
                let stateTone    = 'text-text-secondary';
                if (slot?.kind === 'vanilla') {
                  stateText = t('userBuilds.slotVanilla', 'Ванильная');
                  stateTone = 'text-text-muted';
                } else if (slot?.kind === 'override') {
                  const override = flatById.get(`${slot.gunpackId}::${slot.gunId}`);
                  if (override) {
                    displayName  = override.displayName || override.baseName;
                    displayCover = override.previewUrl;
                    displayGlb   = override.glbUrl;
                    stateText    = t('userBuilds.slotFromName', 'из {{name}}', { name: override.packName });
                    stateTone    = 'text-accent font-semibold';
                  } else {
                    stateText = t('userBuilds.slotOverrideMissing', 'override недоступен');
                    stateTone = 'text-status-warning';
                  }
                }
                return (
                  <motion.div key={g.id} variants={cardV}>
                    <SlotPreviewCard
                      displayName={displayName}
                      category={g.category}
                      previewUrl={displayCover}
                      isVanilla={slot?.kind === 'vanilla'}
                      isDefaultState={!slot}
                      stateText={stateText}
                      stateTone={stateTone}
                      onPreview3D={
                        displayGlb && slot?.kind !== 'vanilla'
                          ? () => setGlbView({ url: displayGlb, title: displayName, kind: 'gun' })
                          : null
                      }
                    />
                  </motion.div>
                );
              })}
            </motion.div>
          )}
        </motion.div>

        {}
        <motion.div variants={sectionV}>
          <SectionHeader
            icon={<Shield size={14} className="text-accent" />}
            title={t('userBuilds.armorSection', 'Бронежилет')}
            subtitle={armorLabel}
            enabled={sectionOn.armor}
            onToggle={() => toggleSection('armor')}
          />
          <motion.div variants={cardV} className="mt-3" style={{ opacity: sectionOn.armor ? 1 : 0.4, filter: sectionOn.armor ? undefined : 'grayscale(1)', transition: 'opacity .2s, filter .2s' }}>
            <ArmorSummaryCard
              isNone={armorSel?.kind === 'none'}
              kind={armorCardKind}
              donorName={armorDisplayName}
              donorAuthor={armorDisplayAuthor}
              glbUrl={armorGlbUrl}
              previewUrl={armorPreviewUrl}
              onPreview3D={armorGlbUrl ? () => setGlbView({ url: armorGlbUrl, title: t('userBuilds.armorViewerTitle', 'Броня · {{name}}', { name: armorDisplayName }), kind: 'armor' }) : null}
            />
          </motion.div>
        </motion.div>

        {}
        <motion.div variants={sectionV}>
          <SectionHeader
            icon={<Building2 size={14} className="text-accent" />}
            title={t('userBuilds.arenaSection', 'Арена')}
            subtitle={arenaLabel}
            enabled={sectionOn.arena}
            onToggle={() => toggleSection('arena')}
          />
          <motion.div variants={cardV} className="mt-3" style={{ opacity: sectionOn.arena ? 1 : 0.4, filter: sectionOn.arena ? undefined : 'grayscale(1)', transition: 'opacity .2s, filter .2s' }}>
            <ArenaSummaryCard
              donorName={arenaRedux?.name ?? '-'}
              donorAuthor={arenaRedux?.author ?? null}

              coverUrl={
                arenaRedux?.componentScreenshots?.arena
                || arenaRedux?.previewUrl
                || null
              }
            />
          </motion.div>
        </motion.div>

        {}
        {(build.minimap || build.reticle || build.sounds) && (
          <motion.div variants={sectionV}>
            <SectionHeader
              icon={<MapIcon size={14} />}
              title={t('userBuilds.hudClusterTitle', 'HUD · звуки')}
              subtitle={t('userBuilds.hudClusterSubtitle', 'Миникарта, прицел и пак звуков')}
            />
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-3.5 mt-3">
              {build.minimap && (
                <motion.div variants={cardV} className="flex flex-col gap-1.5">
                  <div className="flex items-center justify-between px-1">
                    <span className="text-[10px] uppercase tracking-wider text-text-muted font-bold">
                      {t('userBuilds.minimapTag', 'Миникарта')}
                    </span>
                    <InstallToggle compact enabled={sectionOn.minimap} onToggle={() => toggleSection('minimap')} />
                  </div>
                  <div {...minimapHover} style={{ opacity: sectionOn.minimap ? 1 : 0.4, filter: sectionOn.minimap ? undefined : 'grayscale(1)', transition: 'opacity .2s, filter .2s' }}>
                    <ArenaSummaryCard
                      donorName={minimapDisplay.name}
                      donorAuthor={minimapDisplay.author}
                      coverUrl={minimapDisplay.coverUrl}
                      kind="minimap"
                      compact
                    />
                  </div>
                </motion.div>
              )}
              {build.reticle && (
                <motion.div variants={cardV} className="flex flex-col gap-1.5">
                  <div className="flex items-center justify-between px-1">
                    <span className="text-[10px] uppercase tracking-wider text-text-muted font-bold">
                      {t('userBuilds.reticleTag', 'Прицел')}
                    </span>
                    <InstallToggle compact enabled={sectionOn.reticle} onToggle={() => toggleSection('reticle')} />
                  </div>
                  <div {...reticleHover} style={{ opacity: sectionOn.reticle ? 1 : 0.4, filter: sectionOn.reticle ? undefined : 'grayscale(1)', transition: 'opacity .2s, filter .2s' }}>
                    <ArenaSummaryCard
                      donorName={reticleDisplay.name}
                      donorAuthor={reticleDisplay.author}
                      coverUrl={reticleDisplay.coverUrl}
                      kind="reticle"
                      compact
                    />
                  </div>
                </motion.div>
              )}
              {build.sounds && (
                <motion.div variants={cardV} className="flex flex-col gap-1.5">
                  <div className="flex items-center justify-between px-1">
                    <span className="text-[10px] uppercase tracking-wider text-text-muted font-bold">
                      {t('userBuilds.soundsTag', 'Звуки')}
                    </span>
                    <InstallToggle compact enabled={sectionOn.sounds} onToggle={() => toggleSection('sounds')} />
                  </div>
                  <div style={{ opacity: sectionOn.sounds ? 1 : 0.4, filter: sectionOn.sounds ? undefined : 'grayscale(1)', transition: 'opacity .2s, filter .2s' }}>
                    <ArenaSummaryCard
                      donorName={soundsDisplay.name}
                      donorAuthor={soundsDisplay.author}
                      coverUrl={soundsDisplay.coverUrl}
                      videoUrl={soundsDisplay.videoUrl}
                      kind="sounds"
                      compact
                    />
                  </div>
                </motion.div>
              )}
            </div>
          </motion.div>
        )}

        {}

        {}
        {(build.devices.mouse || build.devices.keyboard
          || build.devices.monitor || build.devices.headset) && (
          <motion.div variants={sectionV}>
            <SectionHeader
              icon={<Mouse size={14} className="text-accent" />}
              title={t('userBuilds.devicesTitle', 'Девайсы')}
              subtitle={t('userBuilds.devicesSubtitle', 'Что играет автор сборки')}
            />
            <motion.div variants={cardV} className="mt-3">
              <div className="px-5 py-4 rounded-xl bg-bg-elevated-soft border border-border-subtle">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-3">
                  {build.devices.mouse && (
                    <DeviceLine icon={<Mouse      size={12} />} label={t('userBuilds.deviceMouse', 'Мышь')}       value={build.devices.mouse.name} />
                  )}
                  {build.devices.keyboard && (
                    <DeviceLine icon={<Keyboard   size={12} />} label={t('userBuilds.deviceKeyboard', 'Клавиатура')} value={build.devices.keyboard.name} />
                  )}
                  {build.devices.monitor && (
                    <DeviceLine icon={<Monitor    size={12} />} label={t('userBuilds.deviceMonitor', 'Монитор')}
                                value={build.devices.monitor.hz
                                  ? t('userBuilds.monitorWithHz', '{{name}} · {{hz}} Hz', { name: build.devices.monitor.name, hz: build.devices.monitor.hz })
                                  : build.devices.monitor.name} />
                  )}
                  {build.devices.headset && (
                    <DeviceLine icon={<Headphones size={12} />} label={t('userBuilds.deviceHeadset', 'Гарнитура')}  value={build.devices.headset.name} />
                  )}
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}

        {}
        {(build.sensitivity !== null || build.dpi !== null || build.resolution) && (
          <motion.div variants={sectionV}>
            <SectionHeader
              icon={<Gauge size={14} className="text-accent" />}
              title={t('userBuilds.gameSettingsTitle', 'Игровые настройки')}
              subtitle={t('userBuilds.gameSettingsSubtitle', 'Сенса, DPI, разрешение')}
            />
            <motion.div variants={cardV} className="mt-3">
              <div className="px-5 py-4 rounded-xl bg-bg-elevated-soft border border-border-subtle
                              flex items-center gap-6 flex-wrap">
                {build.sensitivity !== null && (
                  <Stat label={t('userBuilds.statSensitivity', 'Чувствительность')} value={String(build.sensitivity)} />
                )}
                {build.dpi !== null && (
                  <>
                    <Divider />
                    <Stat label={t('userBuilds.statDpi', 'DPI мыши')} value={String(build.dpi)} />
                  </>
                )}
                {build.resolution && (
                  <>
                    <Divider />
                    <Stat label={t('userBuilds.statResolution', 'Разрешение')} value={build.resolution} />
                  </>
                )}
              </div>
            </motion.div>
          </motion.div>
        )}

        {}
        {build.videoUrl && (
          <motion.div variants={sectionV}>
            <SectionHeader
              icon={<Video size={14} className="text-accent" />}
              title={t('userBuilds.videoTitle', 'Ролик')}
              subtitle={t('userBuilds.videoSubtitle', 'Запись игры автора')}
            />
            <motion.div variants={cardV} className="mt-3">
              <BuildVideo url={build.videoUrl} title={build.name} />
            </motion.div>
          </motion.div>
        )}

        {}
        {build.settingsXmlUrl && (() => {
          const presetName = settingsPresets.find(p => p.xmlUrl === build.settingsXmlUrl)?.name ?? null;
          return (
          <motion.div variants={sectionV}>
            <SectionHeader
              icon={<FileText size={14} className="text-accent" />}
              title={presetName ?? t('userBuilds.settingsFileTitle', 'Файл настроек GTA')}
              subtitle={presetName
                ? t('userBuilds.settingsFilePreset', 'Графический пресет · settings.xml')
                : t('userBuilds.settingsFileAuthor', 'settings.xml автора')}
            />
            <motion.div variants={cardV} className="mt-3">
              <a
                href={build.settingsXmlUrl}
                target="_blank" rel="noopener noreferrer"
                className="inline-flex items-center gap-2 px-4 h-10 rounded-xl
                           bg-bg-elevated-soft hover:bg-bg-elevated
                           border border-border-subtle hover:border-border-strong
                           text-[13px] text-text-secondary hover:text-text-primary
                           transition-colors"
              >
                <FileText size={13} />
                {t('userBuilds.settingsFileOpen', 'Открыть settings.xml')}
                <ExternalLink size={12} />
              </a>
            </motion.div>
          </motion.div>
          );
        })()}

        {}
        <motion.div variants={sectionV} className="pt-3">
          <div
            className="relative overflow-hidden rounded-3xl"
            style={{
              background:
                'linear-gradient(135deg, color-mix(in srgb, var(--accent) 8%, var(--bg-elevated)) 0%, var(--bg-elevated) 60%)',
              boxShadow: installResult?.ok
                ? '0 0 0 1px color-mix(in srgb, var(--status-success) 32%, transparent), '
                  + '0 24px 50px -22px color-mix(in srgb, var(--status-success) 40%, transparent), '
                  + '0 12px 24px -12px rgba(0,0,0,0.55)'
                : installResult && !installResult.ok
                  ? '0 0 0 1px color-mix(in srgb, var(--status-error) 32%, transparent), '
                    + '0 24px 50px -22px color-mix(in srgb, var(--status-error) 36%, transparent), '
                    + '0 12px 24px -12px rgba(0,0,0,0.55)'
                  : '0 0 0 1px color-mix(in srgb, var(--accent) 22%, transparent), '
                    + '0 0 0 6px color-mix(in srgb, var(--accent) 5%, transparent), '
                    + '0 28px 60px -22px color-mix(in srgb, var(--accent) 42%, transparent), '
                    + '0 12px 24px -12px rgba(0,0,0,0.55)',
            }}
          >
            {}
            <span
              aria-hidden
              className="pointer-events-none absolute -top-24 -right-24 w-[480px] h-[480px] rounded-full"
              style={{
                background:
                  'radial-gradient(circle at 65% 35%, color-mix(in srgb, var(--accent) 24%, transparent), transparent 70%)',
                filter: 'blur(60px)',
              }}
            />
            <div className="relative flex items-center gap-4 p-5 flex-wrap">
              <span
                className="shrink-0 w-10 h-10 rounded-xl flex items-center justify-center"
                style={{
                  background: installResult?.ok
                    ? 'color-mix(in srgb, var(--status-success) 16%, transparent)'
                    : installResult && !installResult.ok
                      ? 'color-mix(in srgb, var(--status-error) 16%, transparent)'
                      : 'color-mix(in srgb, var(--accent) 14%, transparent)',
                  color: installResult?.ok
                    ? 'var(--status-success)'
                    : installResult && !installResult.ok
                      ? 'var(--status-error)'
                      : 'var(--accent)',
                  boxShadow: installResult?.ok
                    ? 'inset 0 0 0 1px color-mix(in srgb, var(--status-success) 35%, transparent)'
                    : installResult && !installResult.ok
                      ? 'inset 0 0 0 1px color-mix(in srgb, var(--status-error) 35%, transparent)'
                      : 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 32%, transparent)',
                }}
              >
                {installResult?.ok
                  ? <Check size={16} strokeWidth={2.4} />
                  : installResult && !installResult.ok
                    ? <AlertTriangle size={16} strokeWidth={2.2} />
                    : <Download size={16} strokeWidth={2.2} />}
              </span>
              <div className="flex-1 min-w-0 flex flex-col gap-0.5">
                <span className="text-[9px] uppercase tracking-[0.28em] text-accent font-bold">
                  {t('userBuilds.installEyebrow', 'Установка в GTA')}
                </span>
                <span className={'text-[13px] leading-snug ' +
                  (installResult
                    ? installResult.ok ? 'text-status-success' : 'text-status-error'
                    : 'text-text-primary')}>
                  {installResult
                    ? installResult.message
                    : !reduxAvailable || (sectionOn.gunpack && !packAvailable)
                      ? t('userBuilds.installCatalogMissing', 'Редукс или ганпак из этой сборки недоступен в каталоге.')
                      : excludedSections.length > 0
                        ? t('userBuilds.installReadyExcept', 'Поставит выбранные секции. Исключено: {{list}}.', { list: excludedSections.join(', ') })
                        : t('userBuilds.installReadyAll', 'Установит всю сборку целиком.')}
                </span>
              </div>
              {}
              <button
                type="button"
                onClick={handleInstall}
                disabled={installBlocked}
                style={{ outline: 'none' }}
                className="shrink-0 inline-flex items-center gap-2 px-5 h-11 rounded-xl
                           text-[11.5px] font-bold uppercase tracking-[0.16em]
                           bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                           hover:bg-bg-elevated/80 hover:border-white/[0.20]
                           disabled:opacity-40 disabled:cursor-not-allowed
                           transition-colors"
              >
                <Download size={13} strokeWidth={2.4} />
                {installing
                  ? t('userBuilds.installing', 'Ставим...')
                  : t('userBuilds.installFull', 'Поставить в GTA')}
              </button>
            </div>
          </div>
        </motion.div>

          </GlassPanel>
        </motion.div>

        <motion.div variants={sectionV} className="pt-2">
          <BuildReviewsSection buildId={build.id} />
        </motion.div>
      </div>

      <BigPreviewOverlay preview={bigPreview} />

      {}
      {glbView && (
        <div style={{ position: 'fixed', inset: 0, zIndex: 300 }}>
          <GlbViewerModal
            glbUrl={glbView.url}
            title={glbView.title}
            subjectKind={glbView.kind}
            onClose={() => setGlbView(null)}
          />
        </div>
      )}

      <ConfirmModal
        open={confirmDelete}
        title={t('userBuilds.deleteConfirmTitle', 'Удалить сборку?')}
        message={t(
          'userBuilds.deleteConfirmMessage',
          '«{{name}}»\n\nЭто действие нельзя отменить - сборка пропадёт у всех, кто видит её через каталог по HNT-коду.',
          { name: build?.name || build?.hntCode || '' },
        )}
        confirmLabel={deleting ? t('userBuilds.deleting', 'Удаляем…') : t('common.delete', 'Удалить')}
        cancelLabel={t('userBuilds.cancel', 'Отмена')}
        destructive
        onConfirm={() => { void onConfirmDelete(); }}
        onCancel={() => { if (!deleting) setConfirmDelete(false); }}
      />
    </motion.div>
  );
}

function SectionHeader({
  icon, title, subtitle, enabled, onToggle,
}: {
  icon: React.ReactNode;
  title: string;
  subtitle?: string;
  enabled?: boolean;
  onToggle?: () => void;
}) {
  return (
    <header className="flex items-end gap-4 px-1 pb-1">
      <span
        className="shrink-0 inline-flex items-center justify-center w-9 h-9 rounded-xl text-accent"
        style={{
          background: 'color-mix(in srgb, var(--accent) 12%, transparent)',
          boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 32%, transparent)',
        }}
      >
        {icon}
      </span>
      <div className="flex-1 min-w-0 flex flex-col gap-0.5">
        <span className="text-[9.5px] uppercase tracking-[0.32em] text-accent font-bold">
          {title}
        </span>
        {subtitle && (
          <span className="text-[12.5px] text-text-secondary leading-snug truncate">{subtitle}</span>
        )}
      </div>
      {onToggle && <InstallToggle enabled={!!enabled} onToggle={onToggle} />}
      {}
      <span
        aria-hidden
        className="hidden md:block flex-1 h-px self-center max-w-[120px]"
        style={{
          background:
            'linear-gradient(to right, color-mix(in srgb, var(--accent) 30%, transparent), transparent)',
        }}
      />
    </header>
  );
}

function InstallToggle({ enabled, onToggle, compact }: {
  enabled: boolean;
  onToggle: () => void;
  compact?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <button
      type="button"
      onClick={onToggle}
      style={{ outline: 'none' }}
      className={
        'shrink-0 inline-flex items-center gap-2 rounded-full transition-colors duration-200 ' +
        (compact ? 'h-6 pl-1.5 pr-2.5 ' : 'h-7 pl-1.5 pr-3 ') +
        (enabled
          ? 'bg-accent/20 text-accent border border-transparent'
          : 'bg-glass text-text-muted border border-glass-border hover:text-text-secondary')
      }
      title={enabled
        ? t('userBuilds.sectionOnHint', 'Секция будет установлена. Клик - исключить.')
        : t('userBuilds.sectionOffHint', 'Секция исключена из установки. Клик - вернуть.')}
      aria-pressed={enabled}
    >
      <span
        aria-hidden
        className={
          'relative w-7 h-4 rounded-full transition-colors duration-200 ' +
          (enabled ? 'bg-accent' : 'bg-border-strong')
        }
      >
        <span
          className="absolute top-0.5 w-3 h-3 rounded-full bg-white transition-all duration-200"
          style={{ left: enabled ? '14px' : '2px' }}
        />
      </span>
      {!compact && (
        <span className="text-[10px] font-bold uppercase tracking-wider">
          {enabled
            ? t('userBuilds.sectionOn', 'Ставим')
            : t('userBuilds.sectionOff', 'Пропуск')}
        </span>
      )}
    </button>
  );
}

function HeroMetaChip({
  icon, label, value, sub, warn, warnHint,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  sub: string | null;
  warn: boolean;
  warnHint: string;
}) {
  const accentMix = warn ? 'var(--status-warning)' : 'var(--accent)';

  void sub;
  return (
    <span
      className="relative inline-flex items-center gap-2.5 h-[44px] px-3.5 rounded-xl min-w-0 max-w-[320px]"
      title={warn ? warnHint : `${label}: ${value}`}
    >
      <span
        aria-hidden
        className="absolute inset-0 rounded-xl pointer-events-none"
        style={{
          background: `color-mix(in srgb, ${accentMix} 10%, transparent)`,
          boxShadow: `inset 0 0 0 1px color-mix(in srgb, ${accentMix} 28%, transparent)`,
        }}
      />
      <span
        className="relative shrink-0 inline-flex items-center justify-center w-6 h-6 rounded-md"
        style={{
          background: warn
            ? 'color-mix(in srgb, var(--status-warning) 18%, transparent)'
            : 'color-mix(in srgb, var(--accent) 16%, transparent)',
          color: warn ? 'var(--status-warning)' : 'var(--accent)',
        }}
      >
        {warn ? <AlertTriangle size={13} strokeWidth={2.4} /> : icon}
      </span>
      <span className="relative flex flex-col items-start gap-0 min-w-0 text-left">
        <span className="text-[8.5px] uppercase tracking-[0.18em] text-text-muted font-semibold truncate w-full">
          {label}
        </span>
        <span className="text-[12px] font-semibold text-text-primary truncate max-w-[240px]">
          {value}
        </span>
      </span>
    </span>
  );
}

function SlotPreviewCard({
  displayName, category, previewUrl, isVanilla, isDefaultState, stateText, stateTone, onPreview3D,
}: {
  displayName: string;
  category:    string;
  previewUrl:  string | null;
  isVanilla:   boolean;
  isDefaultState: boolean;
  stateText:   string;
  stateTone:   string;
  onPreview3D: (() => void) | null;
}) {
  const { t } = useTranslation();
  return (
    <div
      className="group relative w-full flex flex-col rounded-2xl overflow-hidden"
      style={{
        background: 'var(--bg-elevated)',
        boxShadow: '0 8px 22px -12px rgba(0,0,0,0.45)',
        opacity: isVanilla ? 0.7 : 1,
      }}
    >
      <div className="relative aspect-[4/3] w-full bg-glass flex items-center justify-center overflow-hidden">
        {}
        {!isVanilla && (
          <span
            aria-hidden
            className="absolute inset-x-2 top-2 bottom-2 rounded-2xl pointer-events-none
                       opacity-55 group-hover:opacity-85
                       transition-opacity duration-500 ease-smooth"
            style={{
              background:
                'radial-gradient(ellipse at 50% 55%, color-mix(in srgb, var(--accent) 34%, transparent), transparent 72%)',
              filter: 'blur(18px)',
            }}
          />
        )}
        {isVanilla ? (
          <Slash size={32} strokeWidth={1.4} className="relative text-text-muted" />
        ) : previewUrl ? (
          <img
            src={previewUrl}
            alt={displayName}
            draggable={false}
            className="relative max-w-full max-h-full w-auto h-auto object-contain select-none p-3
                       drop-shadow-[0_8px_18px_rgba(0,0,0,0.45)]"
          />
        ) : (
          <Crosshair size={32} strokeWidth={1.4} className="relative text-text-muted" />
        )}
        {onPreview3D && (
          <button
            type="button"
            onClick={onPreview3D}
            className="absolute top-2 right-2 inline-flex items-center gap-1.5 h-8 px-2.5
                       rounded-full bg-black/55 backdrop-blur-md text-white
                       text-[10.5px] font-bold uppercase tracking-wider
                       opacity-0 group-hover:opacity-100 hover:bg-black/70 transition-opacity duration-150"
            style={{ outline: 'none' }}
            title={t('userBuilds.preview3D', '3D просмотр')}
          >
            <Eye size={12} strokeWidth={2.4} />
            3D
          </button>
        )}
        {}
        {(stateText && !isDefaultState) && (
          <span
            className={
              'absolute top-2 left-2 inline-flex items-center gap-1 h-6 px-2 rounded-full ' +
              'text-[10px] font-bold uppercase tracking-[0.08em] backdrop-blur-md ' + stateTone
            }
            style={{
              background: stateTone.includes('accent')
                ? 'color-mix(in srgb, var(--accent) 22%, rgba(0,0,0,0.45))'
                : 'rgba(0,0,0,0.55)',
              boxShadow: stateTone.includes('accent')
                ? '0 0 0 1px color-mix(in srgb, var(--accent) 45%, transparent)'
                : '0 0 0 1px rgba(255,255,255,0.06)',
            }}
          >
            {stateText}
          </span>
        )}
      </div>
      <div className="flex flex-col gap-0.5 p-3">
        <span className="text-[13px] font-bold text-text-primary truncate leading-tight">{displayName}</span>
        <div className="flex items-center justify-between gap-2">
          <span className="text-[10px] uppercase tracking-wider text-text-muted truncate">{category}</span>
          {}
          {stateText && isDefaultState && (
            <span className="text-[10.5px] text-text-secondary truncate">
              {stateText}
            </span>
          )}
        </div>
      </div>
    </div>
  );
}

const SUMMARY_CARD_SHADOW =
  'inset 0 1px 0 0 rgba(255,255,255,0.12), '
  + 'inset 0 0 0 1px rgba(255,255,255,0.07), '
  + '0 24px 48px -22px rgba(0,0,0,0.55), '
  + '0 10px 20px -10px rgba(0,0,0,0.45)';

const SUMMARY_CARD_SHADOW_HOVER =
  'inset 0 1px 0 0 rgba(255,255,255,0.16), '
  + 'inset 0 0 0 1px rgba(255,255,255,0.11), '
  + '0 30px 60px -22px rgba(0,0,0,0.65), '
  + '0 14px 28px -12px rgba(0,0,0,0.55)';

function SummaryKicker({ icon, label }: { icon: React.ReactNode; label: string }) {
  return (
    <span
      className="inline-flex items-center gap-1.5 self-start px-2.5 py-1 rounded-full
                 text-[10px] font-bold uppercase tracking-[0.22em] text-accent"
      style={{
        background: 'color-mix(in srgb, var(--accent) 12%, transparent)',
        boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 32%, transparent)',
      }}
    >
      <span className="text-accent shrink-0">{icon}</span>
      {label}
    </span>
  );
}

function SummaryAuthor({ name }: { name: string | null }) {
  if (!name) return null;
  return (
    <span className="inline-flex items-center gap-1.5 text-[12.5px] text-text-muted truncate">
      <User size={12} strokeWidth={2} className="shrink-0 opacity-60" />
      <span className="truncate">{name}</span>
    </span>
  );
}

function ArmorSummaryCard({
  isNone, kind, donorName, donorAuthor, glbUrl, previewUrl, onPreview3D,
}: {
  isNone: boolean;
  kind: 'override' | 'library' | 'base';
  donorName: string;
  donorAuthor: string | null;
  glbUrl: string | null;
  previewUrl: string | null;
  onPreview3D: (() => void) | null;
}) {
  const { t } = useTranslation();
  if (isNone) {
    return (
      <div
        className="px-6 py-5 rounded-2xl flex items-center gap-4 min-h-[96px]"
        style={{
          background: 'var(--bg-elevated)',
          boxShadow:
            '0 0 0 1px color-mix(in srgb, var(--text-primary) 7%, transparent), ' +
            '0 8px 22px -12px rgba(0,0,0,0.45)',
        }}
      >
        <span
          className="shrink-0 w-12 h-12 rounded-xl flex items-center justify-center bg-glass-strong text-text-muted"
        >
          <Slash size={20} strokeWidth={1.5} />
        </span>
        <div className="flex flex-col gap-0.5">
          <span className="text-[10.5px] uppercase tracking-[0.22em] text-text-muted font-semibold">
            {t('userBuilds.armorNone', 'Без брони')}
          </span>
          <span className="text-[15px] text-text-secondary leading-tight">
            {t('userBuilds.armorNoneHint', 'Останется ванильная GTA')}
          </span>
        </div>
      </div>
    );
  }

  const kickerLabel =
    kind === 'library'  ? t('userBuilds.armorKickerLibrary',  'Кастомный броник') :
    kind === 'override' ? t('userBuilds.armorKickerOverride', 'Донор брони')      :
                          t('userBuilds.armorKickerBase',     'Броня редукса');
  return (
    <div
      className="group relative w-full grid grid-cols-1 md:grid-cols-[360px,1fr] gap-0 rounded-2xl overflow-hidden
                 backdrop-blur-xl transition-[box-shadow,transform] duration-400 ease-depth hover:-translate-y-0.5"
      style={{
        background:
          'color-mix(in srgb, var(--bg-elevated) 55%, transparent)',
        boxShadow: SUMMARY_CARD_SHADOW,
      }}
      onMouseEnter={e => { (e.currentTarget as HTMLDivElement).style.boxShadow = SUMMARY_CARD_SHADOW_HOVER; }}
      onMouseLeave={e => { (e.currentTarget as HTMLDivElement).style.boxShadow = SUMMARY_CARD_SHADOW; }}
    >
      <div className="relative aspect-[4/3] md:aspect-auto md:h-full md:min-h-[230px] w-full bg-glass overflow-hidden">
        {}
        <span
          aria-hidden
          className="absolute inset-x-2 top-2 bottom-2 rounded-2xl pointer-events-none
                     opacity-65 group-hover:opacity-95
                     transition-opacity duration-500 ease-smooth"
          style={{
            background:
              'radial-gradient(ellipse at 50% 55%, color-mix(in srgb, var(--accent) 38%, transparent), transparent 72%)',
            filter: 'blur(22px)',
          }}
        />
        <div className="absolute inset-0 flex items-center justify-center">
          {previewUrl ? (
            <img
              src={previewUrl}
              alt={donorName}
              draggable={false}
              className="max-w-full max-h-full w-auto h-auto object-contain select-none p-4
                         transition-transform duration-500 ease-out group-hover:scale-[1.04]"
            />
          ) : (
            <ArmorPreview3D glbUrl={glbUrl} />
          )}
        </div>
        {onPreview3D && (
          <button
            type="button"
            onClick={onPreview3D}
            className="absolute top-2 right-2 inline-flex items-center gap-1.5 h-8 px-2.5
                       rounded-full bg-black/55 backdrop-blur-md text-white
                       text-[10.5px] font-bold uppercase tracking-wider
                       opacity-0 group-hover:opacity-100 hover:bg-black/70 transition-opacity duration-150 z-10"
            style={{ outline: 'none' }}
          >
            <Eye size={12} strokeWidth={2.4} />
            3D
          </button>
        )}
      </div>
      {}
      <span
        aria-hidden
        className="hidden md:block absolute left-[360px] top-6 bottom-6 w-px"
        style={{ background: 'linear-gradient(to bottom, transparent, color-mix(in srgb, var(--accent) 28%, transparent), transparent)' }}
      />
      <div className="flex flex-col gap-2 p-7 justify-center">
        <SummaryKicker icon={<Shield size={11} strokeWidth={2.4} />} label={kickerLabel} />
        <span
          className="text-[24px] font-bold text-text-primary truncate leading-[1.1] tracking-tight"
          style={{ textShadow: '0 1px 2px rgba(0,0,0,0.35)' }}
        >
          {donorName}
        </span>
        <SummaryAuthor name={donorAuthor} />
      </div>
    </div>
  );
}

function ArenaSummaryCard({
  donorName, donorAuthor, coverUrl, kind = 'arena', videoUrl = null, compact = false,
}: {
  donorName: string;
  donorAuthor: string | null;
  coverUrl: string | null;
  kind?: 'arena' | 'minimap' | 'reticle' | 'sounds';
  videoUrl?: string | null;
  compact?: boolean;
}) {
  const { t } = useTranslation();
  const [videoOpen, setVideoOpen] = useState(false);
  const hasVideo = !!videoUrl;
  const kickerLabel =
    kind === 'minimap' ? t('userBuilds.donorKickerMinimap', 'Кастомная миникарта') :
    kind === 'reticle' ? t('userBuilds.donorKickerReticle', 'Кастомный прицел')    :
    kind === 'sounds'  ? t('userBuilds.donorKickerSounds',  'Кастомные звуки')     :
                         t('userBuilds.donorKickerArena',   'Донор арены');
  const KickerIcon =
    kind === 'minimap' ? MapIcon   :
    kind === 'reticle' ? Crosshair :
    kind === 'sounds'  ? Volume2   :
                         Building2;

  if (compact) {
    return (
      <div
        className="group relative w-full aspect-[4/3] rounded-2xl overflow-hidden
                   backdrop-blur-xl transition-[box-shadow,transform] duration-400 ease-depth hover:-translate-y-0.5"
        style={{
          background: 'color-mix(in srgb, var(--bg-elevated) 55%, transparent)',
          boxShadow: SUMMARY_CARD_SHADOW,
          cursor: hasVideo ? 'pointer' : undefined,
        }}
        onMouseEnter={e => { (e.currentTarget as HTMLDivElement).style.boxShadow = SUMMARY_CARD_SHADOW_HOVER; }}
        onMouseLeave={e => { (e.currentTarget as HTMLDivElement).style.boxShadow = SUMMARY_CARD_SHADOW; }}
        role={hasVideo ? 'button' : undefined}
        tabIndex={hasVideo ? 0 : undefined}
        onClick={hasVideo ? () => setVideoOpen(true) : undefined}
        onKeyDown={hasVideo
          ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setVideoOpen(true); } }
          : undefined}
      >
        {}
        <div
          aria-hidden
          className="absolute inset-0 transition-transform duration-700 ease-out group-hover:scale-[1.05]"
          style={{
            background: coverUrl
              ? `url(${coverUrl}) center / cover no-repeat`
              : 'linear-gradient(135deg, color-mix(in srgb, var(--accent) 30%, #0a0a14), #0a0a14)',
          }}
        />

        {}

        {}
        <span
          aria-hidden
          className="absolute inset-x-0 bottom-0 h-3/5 pointer-events-none"
          style={{
            background:
              'linear-gradient(to top, '
              + 'rgba(8,8,14,0.92) 0%, '
              + 'rgba(8,8,14,0.78) 30%, '
              + 'rgba(8,8,14,0.40) 65%, '
              + 'transparent 100%)',
          }}
        />

        {}
        <span
          className="absolute top-3 left-3 inline-flex items-center gap-1.5 h-7 px-2.5
                     rounded-full text-[10px] font-bold uppercase tracking-[0.2em]
                     text-white backdrop-blur-md"
          style={{
            background: 'color-mix(in srgb, var(--accent) 22%, rgba(0,0,0,0.45))',
            boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 50%, transparent)',
          }}
        >
          <KickerIcon size={11} strokeWidth={2.5} className="text-accent" />
          {kickerLabel}
        </span>

        {}
        {hasVideo && (
          <span
            className="absolute top-3 right-3 inline-flex items-center gap-1.5 h-7 px-2.5
                       rounded-full text-[10px] font-bold uppercase tracking-[0.18em]
                       text-white backdrop-blur-md
                       opacity-90 group-hover:opacity-100
                       transition-opacity duration-300"
            style={{
              background: 'rgba(0,0,0,0.55)',
              boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.18)',
            }}
          >
            <Play size={10} strokeWidth={3} className="text-white" />
            {t('userBuilds.videoBadge', 'Видео')}
          </span>
        )}

        {}
        <div className="absolute inset-x-0 bottom-0 p-5 flex flex-col gap-1">
          <span
            className="text-[20px] font-bold text-white truncate leading-[1.1] tracking-tight"
            style={{ textShadow: '0 2px 8px rgba(0,0,0,0.6)' }}
          >
            {donorName}
          </span>
          {donorAuthor && (
            <span className="inline-flex items-center gap-1.5 text-[11.5px] text-white/70 truncate">
              <User size={11} strokeWidth={2} className="shrink-0 opacity-70" />
              <span className="truncate">{donorAuthor}</span>
            </span>
          )}
        </div>

        {}
        {hasVideo && (
          <>
            <div
              aria-hidden
              className="pointer-events-none absolute inset-0
                         opacity-0 group-hover:opacity-100
                         transition-opacity duration-300 ease-out"
              style={{
                background:
                  'radial-gradient(ellipse at center, rgba(8,8,14,0.45) 0%, rgba(8,8,14,0.70) 75%)',
                backdropFilter: 'blur(6px) saturate(120%)',
                WebkitBackdropFilter: 'blur(6px) saturate(120%)',
              }}
            />
            <div
              aria-hidden
              className="pointer-events-none absolute inset-0 flex items-center justify-center
                         opacity-0 group-hover:opacity-100
                         transition-opacity duration-300 ease-out"
            >
              <div
                className="inline-flex items-center gap-2.5 px-4 h-11 rounded-full
                           bg-white/[0.10] border border-white/[0.30] text-white
                           backdrop-blur-md
                           shadow-[0_8px_28px_-6px_rgba(0,0,0,0.55)]"
              >
                <span className="inline-flex items-center justify-center w-7 h-7 rounded-full bg-white text-black">
                  <Play size={13} strokeWidth={3} className="ml-0.5" />
                </span>
                <span className="text-[11px] font-bold uppercase tracking-[0.18em]">
                  {t('userBuilds.videoReviewCta', 'Видео обзор · клик')}
                </span>
              </div>
            </div>
          </>
        )}

        <AnimatePresence>
          {videoOpen && hasVideo && (
            <VideoModal
              url={videoUrl!}
              title={`${kickerLabel} · ${donorName}`}
              onClose={() => setVideoOpen(false)}
            />
          )}
        </AnimatePresence>
      </div>
    );
  }

  return (
    <div
      className="group relative w-full grid grid-cols-1 md:grid-cols-[360px,1fr] gap-0 rounded-2xl overflow-hidden
                 backdrop-blur-xl transition-[box-shadow,transform] duration-400 ease-depth hover:-translate-y-0.5"
      style={{
        background:
          'color-mix(in srgb, var(--bg-elevated) 55%, transparent)',
        boxShadow: SUMMARY_CARD_SHADOW,
      }}
      onMouseEnter={e => { (e.currentTarget as HTMLDivElement).style.boxShadow = SUMMARY_CARD_SHADOW_HOVER; }}
      onMouseLeave={e => { (e.currentTarget as HTMLDivElement).style.boxShadow = SUMMARY_CARD_SHADOW; }}
    >
      <div
        className={
          'relative aspect-[4/3] md:aspect-auto md:h-full md:min-h-[230px] w-full bg-glass overflow-hidden '
          + (hasVideo ? 'cursor-pointer' : '')
        }
        role={hasVideo ? 'button' : undefined}
        tabIndex={hasVideo ? 0 : undefined}
        onClick={hasVideo ? () => setVideoOpen(true) : undefined}
        onKeyDown={hasVideo
          ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setVideoOpen(true); } }
          : undefined}
      >
        {}
        <div
          className="absolute inset-0 transition-transform duration-700 ease-out group-hover:scale-[1.05]"
          style={{
            background: coverUrl
              ? `url(${coverUrl}) center / cover no-repeat`
              : 'linear-gradient(135deg, color-mix(in srgb, var(--accent) 24%, #0a0a14), #0a0a14)',
          }}
        />
        {}

        {hasVideo && (
          <>
            <div
              aria-hidden
              className="pointer-events-none absolute inset-0
                         opacity-0 group-hover:opacity-100
                         transition-opacity duration-300 ease-out"
              style={{
                background:
                  'radial-gradient(ellipse at center, rgba(8,8,14,0.40) 0%, rgba(8,8,14,0.65) 75%)',
                backdropFilter: 'blur(8px) saturate(120%)',
                WebkitBackdropFilter: 'blur(8px) saturate(120%)',
              }}
            />
            <div
              aria-hidden
              className="pointer-events-none absolute inset-0 flex items-center justify-center
                         opacity-0 group-hover:opacity-100
                         transition-opacity duration-300 ease-out"
            >
              <div
                className="inline-flex items-center gap-2.5 px-4 h-11 rounded-full
                           bg-white/[0.10] border border-white/[0.30] text-white
                           backdrop-blur-md
                           shadow-[0_8px_28px_-6px_rgba(0,0,0,0.55)]"
              >
                <span className="inline-flex items-center justify-center w-7 h-7 rounded-full bg-white text-black">
                  <Play size={13} strokeWidth={3} className="ml-0.5" />
                </span>
                <span className="text-[11px] font-bold uppercase tracking-[0.18em]">
                  {t('userBuilds.videoReviewCta', 'Видео обзор · клик')}
                </span>
              </div>
            </div>
          </>
        )}
      </div>
      <span
        aria-hidden
        className="hidden md:block absolute left-[360px] top-6 bottom-6 w-px"
        style={{ background: 'linear-gradient(to bottom, transparent, color-mix(in srgb, var(--accent) 28%, transparent), transparent)' }}
      />
      <div className="flex flex-col gap-2 p-7 justify-center">
        <SummaryKicker icon={<KickerIcon size={11} strokeWidth={2.4} />} label={kickerLabel} />
        <span
          className="text-[24px] font-bold text-text-primary truncate leading-[1.1] tracking-tight"
          style={{ textShadow: '0 1px 2px rgba(0,0,0,0.35)' }}
        >
          {donorName}
        </span>
        <SummaryAuthor name={donorAuthor} />
      </div>

      <AnimatePresence>
        {videoOpen && hasVideo && (
          <VideoModal
            url={videoUrl!}
            title={`${kickerLabel} · ${donorName}`}
            onClose={() => setVideoOpen(false)}
          />
        )}
      </AnimatePresence>
    </div>
  );
}

function DeviceLine({ icon, label, value }: {
  icon: React.ReactNode; label: string; value: string;
}) {
  return (
    <div className="flex items-baseline gap-2 min-w-0">
      <span className="shrink-0 text-text-muted">{icon}</span>
      <span className="text-[10px] uppercase tracking-wider text-text-muted shrink-0">{label}</span>
      <span className="text-[13px] text-text-primary truncate" title={value}>{value}</span>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <span className="text-[11px] text-text-muted">{label}</span>
      <span className="mt-0.5 text-[16px] font-semibold text-text-primary tabular-nums">{value}</span>
    </div>
  );
}

function Divider() {
  return <span aria-hidden className="h-8 w-px bg-border-subtle" />;
}

function BuildVideo({ url, title }: { url: string; title: string }) {
  const slot = videoSlotForUrl(url);
  return (
    <div className="aspect-video rounded-xl overflow-hidden bg-black border border-border-subtle">
      {slot.kind === 'embed' ? (
        <LazyEmbedVideo
          slot={slot}
          title={title}
          className="w-full h-full"
        />
      ) : (
        <video
          src={slot.url}
          controls
          preload="metadata"
          playsInline
          className="w-full h-full object-contain"
        />
      )}
    </div>
  );
}
