import { createContext, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import {
  Check, Layers, Crosshair, User, AlertTriangle, Slash, Eye, Shield,
  Building2, Map as MapIcon, Wind, Cloud, Droplet, ChevronDown, ImageOff,
  Volume2, Play, X, Search, Monitor,
  type LucideIcon,
} from 'lucide-react';
import { ArmorPreview3D } from '../armor/ArmorPreview3D';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { BackButton } from '@/components/BackButton';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useGtaSettingsStore } from '@/store/gtaSettingsStore';
import { useUserBuildsStore, gunInternalName, type GunSlotState, type ArmorSelection, type ArenaSelection, type MinimapSelection, type ReticleSelection, type SoundsSelection } from '@/store/userBuildsStore';
import { useSessionStore } from '@/store/sessionStore';
import { useLeaveGuardStore } from '@/store/leaveGuardStore';
import { bridge } from '@/bridge';
import { VideoModal } from '@/components/VideoModal';
import type { ReduxItem, Gunpack } from '@/bridge/types';
import { GunReplaceModal } from './GunReplaceModal';
import { ReduxDetail } from '../redux/ReduxDetail';
import { GunpackDetail } from '../guns/GunpackDetail';
import { GlbViewerModal } from '../guns/GlbViewerModal';
import { generateHntCode } from './hntCode';

interface Props {
  onCancel: () => void;
  onSaved: (newBuildId: string) => void;
}

type BigPreview = { url: string; title?: string; subtitle?: string; armor?: boolean };
const BigPreviewCtx = createContext<((p: BigPreview | null) => void) | null>(null);

function useHoverPreview(preview: { url: string | null | undefined; title?: string; subtitle?: string; armor?: boolean }) {
  const setPreview = useContext(BigPreviewCtx);
  const timer = useRef<number | null>(null);
  const clear = () => { if (timer.current != null) { clearTimeout(timer.current); timer.current = null; } };
  useEffect(() => clear, []);
  if (!setPreview || !preview.url) return {};
  const url = preview.url;
  return {
    onMouseEnter: () => {
      clear();
      timer.current = window.setTimeout(
        () => setPreview({ url, title: preview.title, subtitle: preview.subtitle, armor: preview.armor }), 1200);
    },
    onMouseLeave: () => { clear(); setPreview(null); },
  };
}

function BigPreviewOverlay({ preview }: { preview: BigPreview | null }) {
  return createPortal(
    <AnimatePresence>
      {preview && (
        <motion.div
          key="composer-big-preview"
          initial={{ opacity: 1 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.18, ease: 'easeOut' }}
          className="fixed inset-0 z-[400] pointer-events-none flex items-center justify-center p-10"
        >
          <div className="absolute inset-0 bg-black/55 backdrop-blur-lg" />
          {preview.armor ? (
            <motion.img
              src={preview.url}
              alt={preview.title || ''}
              draggable={false}
              initial={{ scale: 0.94 }}
              animate={{ scale: 1 }}
              transition={{ type: 'spring', stiffness: 280, damping: 26 }}
              className="relative max-w-[88vw] max-h-[80vh] w-auto h-auto object-contain select-none
                         drop-shadow-[0_24px_80px_rgba(0,0,0,0.85)]"
            />
          ) : (
            <motion.div
              initial={{ scale: 0.94 }}
              animate={{ scale: 1 }}
              transition={{ type: 'spring', stiffness: 280, damping: 26 }}
              className="relative w-[62vw] max-w-[940px] h-[60vh] max-h-[600px]"
            >
              <img
                src={preview.url}
                alt={preview.title || ''}
                draggable={false}
                className="w-full h-full object-contain select-none drop-shadow-[0_24px_80px_rgba(0,0,0,0.85)]"
              />
            </motion.div>
          )}
          {(preview.title || preview.subtitle) && (
            <div className="absolute bottom-10 inset-x-0 flex flex-col items-center gap-1 text-center px-6">
              {preview.title && <span className="text-lg font-bold text-white tracking-tight">{preview.title}</span>}
              {preview.subtitle && <span className="text-sm text-white/70">{preview.subtitle}</span>}
            </div>
          )}
        </motion.div>
      )}
    </AnimatePresence>,
    document.body,
  );
}

export function CreateBuildScreen({ onCancel, onSaved }: Props) {
  const { t } = useTranslation();

  const reduxList = useReduxStore(s => s.items) ?? [];
  const loadReduxes = useReduxStore(s => s.load);
  const selectRedux = useReduxStore(s => s.select);
  const publicPacks = useGunpackStore(s => s.publicPacks) ?? [];
  const loadPublicPacks = useGunpackStore(s => s.loadPublicPacks);

  const selectedGuns = useGunpackStore(s => s.selectedGuns) ?? [];
  const loadingPackDetail = useGunpackStore(s => s.loadingDetail);
  const selectPack = useGunpackStore(s => s.selectPack);
  const loadAllGuns = useGunpackStore(s => s.loadAllGuns);
  const whitelist = useGunpackStore(s => s.whitelist) ?? [];
  const loadWhitelist = useGunpackStore(s => s.loadWhitelist);

  const author = useSessionStore(s => s.profile?.username ?? s.auth?.username) ?? t('userBuilds.guestAuthor', 'Гость');
  const authorUserId = useSessionStore(s => s.profile?.id ?? null);
  const addBuild = useUserBuildsStore(s => s.add);
  const presets = useGtaSettingsStore(s => s.publicPresets) ?? [];
  const loadPresets = useGtaSettingsStore(s => s.loadPublicPresets);
  useEffect(() => { void loadPresets(); }, [loadPresets]);

  const [reduxId, setReduxId] = useState<string | null>(null);
  const [packId,  setPackId]  = useState<string | null>(null);
  const [gunSlots, setGunSlots] = useState<Record<string, GunSlotState>>({});
  const [name, setName] = useState<string>('');
  const [replaceTarget, setReplaceTarget] = useState<{ internalName: string; displayName: string; category: string } | null>(null);
  const [packPickerOpen, setPackPickerOpen] = useState(false);
  const [packQuery, setPackQuery] = useState('');
  const [openStep, setOpenStep] = useState(1);
  const [previewReduxId, setPreviewReduxId] = useState<string | null>(null);
  const [previewGunpackId, setPreviewGunpackId] = useState<string | null>(null);
  const [bigPreview, setBigPreview] = useState<BigPreview | null>(null);
  const [query, setQuery] = useState('');
  const toggleStep = (n: number) => { setQuery(''); setOpenStep(s => (s === n ? 0 : n)); };
  const q = query.trim().toLowerCase();
  const matchesQuery = (...vals: (string | null | undefined)[]) =>
    !q || vals.some(v => (v ?? '').toLowerCase().includes(q));

  const [armor, setArmor] = useState<ArmorSelection | null>(null);

  const [arena, setArena] = useState<ArenaSelection | null>(null);

  const [minimap, setMinimap] = useState<MinimapSelection | null>(null);

  const [minimapLibraryItems, setMinimapLibraryItems] =
    useState<import('@/bridge/types').LibraryComponent[]>([]);
  useEffect(() => {
    let alive = true;
    bridge.libraryList('minimap')
      .then(rows => { if (alive) setMinimapLibraryItems(rows ?? []); })
      .catch(e => console.warn('[createBuild.minimapLibrary] fail:', e));
    return () => { alive = false; };
  }, []);

  const [reticle, setReticle] = useState<ReticleSelection | null>(null);
  const [reticleLibraryItems, setReticleLibraryItems] =
    useState<import('@/bridge/types').LibraryComponent[]>([]);
  useEffect(() => {
    let alive = true;
    bridge.libraryList('crosshair')
      .then(rows => { if (alive) setReticleLibraryItems(rows ?? []); })
      .catch(e => console.warn('[createBuild.reticleLibrary] fail:', e));
    return () => { alive = false; };
  }, []);

  const [settingsPresetId, setSettingsPresetId] = useState<string | null>(null);

  const [sounds, setSounds] = useState<SoundsSelection | null>(null);
  const [soundsQuery, setSoundsQuery] = useState('');
  const [soundsLibraryItems, setSoundsLibraryItems] =
    useState<import('@/bridge/types').LibraryComponent[]>([]);
  useEffect(() => {
    let alive = true;
    bridge.libraryList('sounds')
      .then(rows => { if (alive) setSoundsLibraryItems(rows ?? []); })
      .catch(e => console.warn('[createBuild.soundsLibrary] fail:', e));
    return () => { alive = false; };
  }, []);

  const [glbView, setGlbView] = useState<{ url: string | null; title: string; kind: 'gun' | 'armor' } | null>(null);

  useEffect(() => {
    if (reduxList.length === 0)  void loadReduxes();
    if (publicPacks.length === 0) void loadPublicPacks();

    void loadAllGuns();
    void loadWhitelist();
  }, []);

  const setLeaveBlocked = useLeaveGuardStore(s => s.setBlocked);
  const attemptLeave    = useLeaveGuardStore(s => s.attempt);
  const dirty =
    !!reduxId || !!packId || name.trim() !== '' ||
    Object.keys(gunSlots).length > 0 ||
    !!armor || !!arena || !!minimap || !!reticle || !!settingsPresetId || !!sounds;
  useEffect(() => {
    setLeaveBlocked(dirty);
    return () => setLeaveBlocked(false);
  }, [dirty, setLeaveBlocked]);

  const guardedCancel = () => attemptLeave(onCancel);

  useEffect(() => {
    if (!packId) return;
    void selectPack(packId);

    setGunSlots({});
  }, [packId]);

  useEffect(() => {
    return () => { void selectPack(null); };
  }, [selectPack]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (useLeaveGuardStore.getState().pending) return;
      if (e.key === 'Escape' && !replaceTarget) guardedCancel();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onCancel, replaceTarget]);

  const reduxById = useMemo(() => {
    const m = new Map<string, ReduxItem>();
    for (const r of reduxList) m.set(r.id, r);
    return m;
  }, [reduxList]);
  const packById = useMemo(() => {
    const m = new Map<string, Gunpack>();
    for (const p of publicPacks) m.set(p.id, p);
    return m;
  }, [publicPacks]);
  const allGuns = useGunpackStore(s => s.allGuns) ?? [];
  const flatById = useMemo(() => {
    const m = new Map<string, typeof allGuns[number]>();
    for (const g of allGuns) m.set(`${g.packId}::${g.gunId}`, g);
    return m;
  }, [allGuns]);

  const publishedReduxes = useMemo(
    () => reduxList.filter(r => r.status === 'published'),
    [reduxList]);

  const armorDonors = useMemo(
    () => reduxList.filter(r =>
      r.components?.armor?.glbUrl
      && !r.armorStandaloneInstallHidden),
    [reduxList]);

  const arenaDonors = useMemo(
    () => reduxList.filter(r =>
      r.status === 'published'
      && r.components?.arena?.isFound),
    [reduxList]);

  const minimapDonors = useMemo(
    () => reduxList.filter(r =>
      r.status === 'published'
      && r.components?.minimap?.isFound),
    [reduxList]);

  const reticleDonors = useMemo(
    () => reduxList.filter(r =>
      r.status === 'published'
      && r.components?.crosshair?.isFound
      && !!r.componentScreenshots?.crosshair),
    [reduxList]);

  const [armorLibraryItems, setArmorLibraryItems] = useState<import('@/bridge/types').ArmorLibraryItem[]>([]);
  useEffect(() => {
    let alive = true;
    bridge.armorLibraryList()
      .then(rows => { if (alive) setArmorLibraryItems(rows ?? []); })
      .catch(e => console.warn('[createBuild.armorLibrary] fail:', e));
    return () => { alive = false; };
  }, []);

  type ArmorChoice =
    | { kind: 'library'; library: import('@/bridge/types').ArmorLibraryItem }
    | { kind: 'redux';   redux:   ReduxItem };
  const armorChoices = useMemo<ArmorChoice[]>(() => {
    const lib: ArmorChoice[] = armorLibraryItems.map(a => ({ kind: 'library', library: a }));
    const rdx: ArmorChoice[] = armorDonors.map(r => ({ kind: 'redux', redux: r }));
    return [...lib, ...rdx];
  }, [armorLibraryItems, armorDonors]);

  const visibleGuns = useMemo(
    () => selectedGuns.filter(g => !g.isHidden).sort((a, b) => a.sortOrder - b.sortOrder),
    [selectedGuns]);

  const canSave = reduxId !== null &&
    (packId !== null || Object.values(gunSlots).some(v => v.kind === 'override'));

  const setSlotVanilla = (internal: string) =>
    setGunSlots(s => ({ ...s, [internal]: { kind: 'vanilla' } }));
  const setSlotOverride = (internal: string, gunpackId: string, gunId: string) =>
    setGunSlots(s => ({ ...s, [internal]: { kind: 'override', gunpackId, gunId } }));
  const clearSlot = (internal: string) =>
    setGunSlots(s => {
      const next = { ...s };
      delete next[internal];
      return next;
    });

  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [coverUrl, setCoverUrl] = useState<string | null>(null);
  const [uploadingCover, setUploadingCover] = useState(false);
  const [coverError, setCoverError] = useState<string | null>(null);
  const handlePickCover = async () => {
    if (uploadingCover) return;
    setCoverError(null);
    try {
      const path = await bridge.openFileDialog(t('userBuilds.fileDialogImage', 'Изображение'), '*.png;*.jpg;*.jpeg;*.webp;*.gif');
      if (!path) return;
      setUploadingCover(true);
      const url = await bridge.userBuildUploadCover(path);
      setCoverUrl(url);
    } catch (e) {
      setCoverError(e instanceof Error ? e.message : t('userBuilds.coverUploadFailed', 'Не удалось загрузить обложку'));
    } finally {
      setUploadingCover(false);
    }
  };

  const onSave = async () => {
    if (!canSave || saving) return;
    const r = reduxById.get(reduxId!);
    const p = packId ? packById.get(packId) : undefined;
    const reduxName = r?.name ?? '';
    const packName  = p?.name ?? '';
    const finalName = name.trim() || (packName ? `${reduxName} + ${packName}` : reduxName);
    setSaving(true);
    setSaveError(null);
    try {
      const saved = await addBuild({
        name:                finalName,
        author,
        authorUserId,

        hntCode:             generateHntCode(),
        reduxId:             reduxId!,
        gunpackId:           packId ?? '',
        gunSlots,
        armor,
        arena,
        minimap,
        reticle,
        sounds,
        reduxNameSnapshot:   reduxName,
        gunpackNameSnapshot: packName,
        coverUrl,
        settingsXmlUrl:      settingsPresetId
          ? (presets.find(p => p.id === settingsPresetId)?.xmlUrl ?? null)
          : null,
      });
      onSaved(saved.id);
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : t('userBuilds.saveFailed', 'Не удалось сохранить сборку'));
    } finally {
      setSaving(false);
    }
  };

  const pageV: Variants = {
    hidden:  { opacity: 0, y: 12 },
    visible: { opacity: 1, y: 0, transition: { duration: 0.32, ease: EASE_DEPTH, staggerChildren: 0.08 } },
  };
  const sectionV: Variants = {
    hidden:  { opacity: 0, y: 14 },
    visible: { opacity: 1, y: 0, transition: { duration: 0.4, ease: EASE_DEPTH } },
  };
  const gridV: Variants = {
    hidden:  { opacity: 1 },
    visible: { opacity: 1, transition: { staggerChildren: 0.04, delayChildren: 0.1 } },
  };
  const cardV: Variants = {
    hidden:  { opacity: 0, y: 10, scale: 0.96 },
    visible: { opacity: 1, y: 0, scale: 1, transition: { duration: 0.32, ease: EASE_DEPTH } },
  };

  const dash = '-';
  const reduxSummary   = reduxId ? (reduxById.get(reduxId)?.name ?? dash) : t('userBuilds.sumNone', 'не выбран');
  const overridesN     = Object.values(gunSlots).filter(v => v.kind === 'override').length;
  const vanillaN       = Object.values(gunSlots).filter(v => v.kind === 'vanilla').length;
  const fromBase       = t('userBuilds.sumFromBase', 'от базы');
  const armorSummary   = armor === null ? t('userBuilds.armorBase', 'базовая')
    : armor.kind === 'none'     ? t('userBuilds.armorNone', 'без брони')
    : armor.kind === 'override' ? (reduxById.get(armor.reduxId)?.name ?? dash)
    : (armorLibraryItems.find(a => a.id === armor.armorLibraryId)?.name ?? dash);
  const arenaSummary   = arena ? (reduxById.get(arena.reduxId)?.name ?? dash) : fromBase;
  const minimapSummary = minimap === null ? fromBase
    : minimap.kind === 'override' ? (reduxById.get(minimap.reduxId)?.name ?? dash)
    : (minimapLibraryItems.find(l => l.id === minimap.minimapLibraryId)?.name ?? dash);
  const reticleSummary = reticle === null ? fromBase
    : reticle.kind === 'override' ? (reduxById.get(reticle.reduxId)?.name ?? dash)
    : (reticleLibraryItems.find(l => l.id === reticle.reticleLibraryId)?.name ?? dash);
  const soundsSummary  = sounds ? (soundsLibraryItems.find(l => l.id === sounds.soundsLibraryId)?.name ?? dash)
    : t('userBuilds.sumNoSounds', 'без звуков');
  const settingsSummary = settingsPresetId
    ? (presets.find(p => p.id === settingsPresetId)?.name ?? dash)
    : t('userBuilds.sumNone', 'не выбран');

  const baseGunEntries = packId
    ? visibleGuns.map(g => ({
        internalName: gunInternalName(g),
        displayName:  g.displayName || g.baseName,
        category:     g.category,
        cover:        g.previewUrl as string | null,
        glb:          g.glbUrl as string | null,
      }))
    : whitelist.map(w => ({
        internalName: w.internalName,
        displayName:  w.displayName,
        category:     w.category,
        cover:        w.previewUrl as string | null,
        glb:          null as string | null,
      }));
  const gunsSectionHead = packId
    ? overridesN
      ? t('userBuilds.packSwapsSummary', {
          count: overridesN,
          name: packById.get(packId)?.name ?? dash,
          defaultValue: '{{name}} · {{count}} замен',
        })
      : (packById.get(packId)?.name ?? dash)
    : overridesN
      ? t('userBuilds.sumManualGunsCount', { count: overridesN, defaultValue: 'вручную · {{count}} замен' })
      : t('userBuilds.sumVanillaGuns', 'ванильные пушки');
  const gunsSectionSummary = vanillaN
    ? gunsSectionHead + ' · ' + t('userBuilds.vanillaCount', { count: vanillaN, defaultValue: '{{count}} ванилы' })
    : gunsSectionHead;

  if (previewReduxId) {
    return (
      <ReduxDetail
        onBack={() => { setPreviewReduxId(null); selectRedux(null); }}
        onPickForBuild={(id) => { setReduxId(id); setPreviewReduxId(null); selectRedux(null); }}
      />
    );
  }

  if (previewGunpackId) {
    return (
      <GunpackDetail
        packId={previewGunpackId}
        onBack={() => { setPreviewGunpackId(null); if (packId) void selectPack(packId); }}
      />
    );
  }

  return (
    <BigPreviewCtx.Provider value={setBigPreview}>
    <motion.div
      variants={pageV}
      initial="hidden"
      animate="visible"
      className="h-full overflow-y-auto"
    >
      <div className="max-w-[1280px] mx-auto px-12 pt-10 pb-16 flex flex-col gap-3">
        {}
        <motion.div variants={sectionV} className="flex items-center gap-3 flex-wrap">
          <BackButton onClick={guardedCancel} label={t('userBuilds.back', 'Назад к сборкам')} />
          <div className="flex-1 min-w-0">
            <h1 className="text-[24px] font-semibold tracking-tight text-text-primary">
              {t('userBuilds.createTitle', 'Соберите свою связку')}
            </h1>
            <p className="mt-1 text-[13px] text-text-muted">
              {t('userBuilds.createSubtitle', 'Базовый редукс, ган-пак и опциональные замены пушек.')}
            </p>
          </div>
        </motion.div>

        {}
        <motion.div variants={sectionV} className="flex flex-wrap gap-2">
          <StepChip n={1} icon={<Layers size={15} />}    title={t('userBuilds.reduxSection', 'Редукс')}     summary={reduxSummary}       active={openStep === 1} onClick={() => toggleStep(1)} />
          <StepChip n={2} icon={<Crosshair size={15} />} title={t('userBuilds.gunsMergedTitle', 'Оружие')}  summary={gunsSectionSummary} active={openStep === 2} onClick={() => toggleStep(2)} />
          <StepChip n={3} icon={<Shield size={15} />}    title={t('userBuilds.armorSection', 'Бронежилет')} summary={armorSummary}       active={openStep === 3} onClick={() => toggleStep(3)} />
          <StepChip n={4} icon={<Building2 size={15} />} title={t('userBuilds.arenaSection', 'Арена')}      summary={arenaSummary}       active={openStep === 4} onClick={() => toggleStep(4)} />
          <StepChip n={5} icon={<MapIcon size={15} />}   title={t('userBuilds.minimapSection', 'Миникарта')} summary={minimapSummary}     active={openStep === 5} onClick={() => toggleStep(5)} />
          <StepChip n={6} icon={<Crosshair size={15} />} title={t('userBuilds.reticleSection', 'Прицел')}    summary={reticleSummary}     active={openStep === 6} onClick={() => toggleStep(6)} />
          <StepChip n={7} icon={<Volume2 size={15} />}   title={t('userBuilds.soundsSection', 'Звуки')}      summary={soundsSummary}      active={openStep === 7} onClick={() => toggleStep(7)} />
          <StepChip n={8} icon={<Monitor size={15} />}   title={t('userBuilds.settingsSectionChip', 'Сеттингс')} summary={settingsSummary}    active={openStep === 8} onClick={() => toggleStep(8)} />
        </motion.div>

        <div className="flex flex-col">
        <motion.div variants={sectionV}>
          <Section
            step={1}
            open={openStep === 1}
            onToggle={() => toggleStep(1)}
            icon={<Layers size={14} className="text-accent" />}
            title={t('userBuilds.reduxSection', 'База редукса')}
            hint={t('userBuilds.reduxHint', 'Поверх неё будет работать ганпак.')}
            summary={reduxSummary}
          >
            <SearchBox value={query} onChange={setQuery} placeholder={t('userBuilds.searchPlaceholder', 'Поиск по названию')} />
            {publishedReduxes.length === 0 ? (
              <EmptyHint text={t('userBuilds.noReduxes', 'В каталоге нет редуксов. Зайдите в раздел Редуксы и обновите список.')} />
            ) : (
              <motion.div
                variants={gridV}
                initial="hidden"
                animate="visible"
                className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-5 gap-2.5"
              >
                {publishedReduxes.filter(r => matchesQuery(r.name, r.author)).map((r) => (
                  <motion.div key={r.id} variants={cardV}>
                    <ReduxBigPickerCard
                      redux={r}
                      selected={reduxId === r.id}
                      onClick={() => { selectRedux(r.id); setPreviewReduxId(r.id); }}
                    />
                  </motion.div>
                ))}
              </motion.div>
            )}
          </Section>
        </motion.div>

        {}
        <motion.div variants={sectionV}>
          <Section
            step={2}
            open={openStep === 2}
            onToggle={() => toggleStep(2)}
            icon={<Crosshair size={14} className="text-accent" />}
            title={t('userBuilds.gunsMergedTitle', 'Оружие')}
            hint={t('userBuilds.gunsMergedHint', 'Кликни по любой пушке - заменишь её на вариант из любого пака или сделаешь ванильной. Или возьми базу ганпака и потом точечно правь.')}
            summary={gunsSectionSummary}
            headerAction={
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setPackPickerOpen(o => !o)}
                  style={{ outline: 'none' }}
                  className="inline-flex items-center gap-2 h-9 px-3 rounded-lg text-[12px] font-bold uppercase tracking-wider
                             bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                             hover:bg-bg-elevated/80 hover:border-white/[0.20] transition-colors"
                >
                  <Crosshair size={13} />
                  {packId
                    ? t('userBuilds.basePackIs', 'База: {{name}}', { name: packById.get(packId)?.name ?? '' })
                    : t('userBuilds.takeBasePack', 'Взять базу ганпака')}
                  <ChevronDown size={14} className={'transition-transform ' + (packPickerOpen ? 'rotate-180' : '')} />
                </button>
                {packId && (
                  <button
                    type="button"
                    onClick={() => { setPackId(null); setGunSlots({}); }}
                    style={{ outline: 'none' }}
                    className="inline-flex items-center gap-1.5 h-9 px-3 rounded-lg text-[11.5px] text-text-muted
                               hover:text-status-error hover:bg-status-error/10 transition-colors"
                  >
                    <X size={12} /> {t('userBuilds.clearBasePack', 'Сбросить базу')}
                  </button>
                )}
              </div>
            }
          >
            <AnimatePresence initial={false}>
              {packPickerOpen && (
                <motion.div
                  key="packpicker"
                  initial={{ height: 0, opacity: 0 }}
                  animate={{ height: 'auto', opacity: 1 }}
                  exit={{ height: 0, opacity: 0 }}
                  transition={{ duration: 0.24, ease: EASE_DEPTH }}
                  className="overflow-hidden"
                >
                  {publicPacks.length === 0 ? (
                    <EmptyHint text={t('userBuilds.noPacks', 'Опубликованных ганпаков ещё нет. Дождитесь обновления каталога.')} />
                  ) : (
                    <div className="mb-3">
                      <SearchBox value={packQuery} onChange={setPackQuery} placeholder={t('userBuilds.searchPackPlaceholder', 'Поиск ганпака')} />
                      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-5 gap-2.5">
                        {publicPacks
                          .filter(p => {
                            const q = packQuery.trim().toLowerCase();
                            return !q || (p.name ?? '').toLowerCase().includes(q) || (p.author ?? '').toLowerCase().includes(q);
                          })
                          .map((p) => (
                            <PickerCard
                              key={p.id}
                              selected={packId === p.id}
                              onClick={() => { setPackId(p.id); setPackPickerOpen(false); }}
                              name={p.name}
                              subtitle={p.author}
                              coverUrl={p.coverKind === 'image' ? p.coverUrl : null}
                              fallbackIcon={<Crosshair size={22} />}
                            />
                          ))}
                      </div>
                    </div>
                  )}
                </motion.div>
              )}
            </AnimatePresence>

            {packId && loadingPackDetail ? (
              <EmptyHint text={t('userBuilds.gunsLoading', 'Загружаем содержимое пака...')} />
            ) : baseGunEntries.length === 0 ? (
              <EmptyHint text={t('userBuilds.gunsEmpty', 'Список пушек пуст.')} />
            ) : (
              <motion.div
                variants={gridV}
                initial="hidden"
                animate="visible"
                className="grid grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-2.5"
              >
                {baseGunEntries.map((e) => {
                  const slot = gunSlots[e.internalName] ?? null;
                  let displayCover = e.cover;
                  let displayName  = e.displayName;
                  let displayGlb   = e.glb;
                  let badgePack    = packId ? (packById.get(packId)?.name ?? '') : t('userBuilds.vanillaBadge', 'ванила');
                  if (slot?.kind === 'override') {
                    const override = flatById.get(`${slot.gunpackId}::${slot.gunId}`);
                    if (override) {
                      displayCover = override.previewUrl;
                      displayName  = override.displayName || override.baseName;
                      displayGlb   = override.glbUrl;
                      badgePack    = override.packName;
                    }
                  }
                  return (
                    <motion.div key={e.internalName} variants={cardV} layout>
                      <SlotGunCard
                        displayName={displayName}
                        category={e.category}
                        previewUrl={displayCover}
                        slotState={slot}
                        sourcePackName={badgePack}
                        onClick={() => setReplaceTarget({ internalName: e.internalName, displayName: e.displayName, category: e.category })}
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
          </Section>
        </motion.div>

        {}
        <motion.div variants={sectionV}>
          <Section
            step={3}
            open={openStep === 3}
            onToggle={() => toggleStep(3)}
            icon={<Shield size={14} className="text-accent" />}
            title={t('userBuilds.armorSection', 'Бронежилет')}
            summary={armorSummary}
            hint={
              armor === null
                ? t('userBuilds.armorHintDefault', 'По умолчанию - броня выбранного редукса. Можно взять броню от другого редукса или вовсе её отключить.')
                : armor.kind === 'none'
                  ? t('userBuilds.armorHintNone', 'Броня не устанавливается, остаётся ванильная GTA.')
                  : t('userBuilds.armorHintOverride', 'Броня берётся из другого редукса.')
            }
          >
            {(() => {
              const baseRedux = reduxId ? reduxById.get(reduxId) : null;
              const baseHasArmor = !!baseRedux?.components?.armor?.glbUrl;
              const isDefault  = armor === null;
              const isNone     = armor?.kind === 'none';
              const overrideId = armor?.kind === 'override' ? armor.reduxId : null;
              const libraryId  = armor?.kind === 'library' ? armor.armorLibraryId : null;

              return (
                <motion.div
                  variants={gridV}
                  initial="hidden"
                  animate="visible"
                  className="flex flex-col gap-3"
                >
                  {}
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-2.5">
                    <motion.div variants={cardV}>
                      <ArmorSpecialCard
                        icon={<Shield size={16} />}
                        title={t('userBuilds.armorUseDefault', 'По умолчанию')}
                        subtitle={
                          reduxId === null
                            ? t('userBuilds.armorPickReduxFirst', 'Сначала выберите редукс выше')
                            : baseHasArmor
                              ? t('userBuilds.armorFromBase', 'Броня из выбранного редукса')
                              : t('userBuilds.armorBaseHasNone', 'У выбранного редукса нет своей брони')
                        }
                        selected={isDefault}
                        onClick={() => setArmor(null)}
                      />
                    </motion.div>
                    <motion.div variants={cardV}>
                      <ArmorSpecialCard
                        icon={<Slash size={16} />}
                        title={t('userBuilds.armorNone', 'Без брони')}
                        subtitle={t('userBuilds.armorNoneHint', 'Никакой брони, останется ванильная GTA')}
                        selected={isNone}
                        onClick={() => setArmor({ kind: 'none' })}
                        tone="muted"
                      />
                    </motion.div>
                  </div>

                  <SearchBox value={query} onChange={setQuery} placeholder={t('userBuilds.searchPlaceholder', 'Поиск по названию')} />
                  {armorChoices.length === 0 ? (
                    <EmptyHint text={t('userBuilds.armorNoDonors', 'Пока нет редуксов с моделью брони. Загрузите каталог Редуксы и вернитесь.')} />
                  ) : (
                    <div className="grid grid-cols-2 md:grid-cols-3 2xl:grid-cols-4 gap-3">
                      {armorChoices.filter(c => matchesQuery(c.kind === 'library' ? c.library.name : c.redux.name, c.kind === 'library' ? c.library.author : c.redux.author)).map((c) => {
                        if (c.kind === 'library') {
                          const a = c.library;
                          return (
                            <motion.div key={'lib:' + a.id} variants={cardV}>
                              <ArmorLibraryCard
                                item={a}
                                selected={libraryId === a.id}
                                onClick={() => setArmor({ kind: 'library', armorLibraryId: a.id })}
                                onPreview3D={
                                  a.glbUrl
                                    ? () => setGlbView({
                                        url:   a.glbUrl,
                                        title: t('userBuilds.armorViewerTitle', 'Броня · {{name}}', { name: a.name }),
                                        kind:  'armor',
                                      })
                                    : null
                                }
                              />
                            </motion.div>
                          );
                        }
                        const r = c.redux;
                        const donorGlbUrl = r.components?.armor?.glbUrl ?? null;
                        return (
                          <motion.div key={'rdx:' + r.id} variants={cardV}>
                            <ArmorDonorCard
                              redux={r}
                              isBase={r.id === reduxId}
                              selected={overrideId === r.id}
                              onClick={() => setArmor({ kind: 'override', reduxId: r.id })}
                              onPreview3D={
                                donorGlbUrl
                                  ? () => setGlbView({
                                      url:   donorGlbUrl,
                                      title: t('userBuilds.armorViewerTitle', 'Броня · {{name}}', { name: r.name }),
                                      kind:  'armor',
                                    })
                                  : null
                              }
                            />
                          </motion.div>
                        );
                      })}
                    </div>
                  )}
                </motion.div>
              );
            })()}
          </Section>
        </motion.div>

        {}
        <motion.div variants={sectionV}>
          <Section
            step={4}
            open={openStep === 4}
            onToggle={() => toggleStep(4)}
            icon={<Building2 size={14} className="text-accent" />}
            title={t('userBuilds.arenaSection', 'Арена')}
            summary={arenaSummary}
            hint={
              arena === null
                ? t('userBuilds.arenaHintDefault', 'По умолчанию - арена выбранного редукса. Можно взять арену от другого редукса.')
                : t('userBuilds.arenaHintOverride', 'Арена берётся из другого редукса.')
            }
          >
            {(() => {
              const baseRedux = reduxId ? reduxById.get(reduxId) : null;
              const baseHasArena = !!baseRedux?.components?.arena?.isFound;
              const isDefault = arena === null;
              const overrideId = arena?.kind === 'override' ? arena.reduxId : null;

              return (
                <motion.div
                  variants={gridV}
                  initial="hidden"
                  animate="visible"
                  className="flex flex-col gap-3"
                >
                  <div className="grid grid-cols-1 gap-2.5">
                    <motion.div variants={cardV}>
                      <ArmorSpecialCard
                        icon={<Building2 size={16} />}
                        title={t('userBuilds.arenaUseDefault', 'По умолчанию')}
                        subtitle={
                          reduxId === null
                            ? t('userBuilds.arenaPickReduxFirst', 'Сначала выберите редукс выше')
                            : baseHasArena
                              ? t('userBuilds.arenaFromBase', 'Арена из выбранного редукса')
                              : t('userBuilds.arenaBaseHasNone', 'У выбранного редукса нет своей арены')
                        }
                        selected={isDefault}
                        onClick={() => setArena(null)}
                      />
                    </motion.div>
                  </div>

                  <SearchBox value={query} onChange={setQuery} placeholder={t('userBuilds.searchPlaceholder', 'Поиск по названию')} />
                  {arenaDonors.length === 0 ? (
                    <EmptyHint text={t('userBuilds.arenaNoDonors', 'Пока нет редуксов с ареной. Загрузите каталог Редуксы и вернитесь.')} />
                  ) : (
                    <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-5 gap-2.5">
                      {arenaDonors.filter(r => matchesQuery(r.name, r.author)).map((r) => (
                        <motion.div key={'arn:' + r.id} variants={cardV}>
                          <ArenaDonorCard
                            redux={r}
                            isBase={r.id === reduxId}
                            selected={overrideId === r.id}
                            onClick={() => setArena({ kind: 'override', reduxId: r.id })}
                          />
                        </motion.div>
                      ))}
                    </div>
                  )}
                </motion.div>
              );
            })()}
          </Section>
        </motion.div>

        {}
        <motion.div variants={sectionV}>
          <Section
            step={5}
            open={openStep === 5}
            onToggle={() => toggleStep(5)}
            icon={<MapIcon size={14} className="text-accent" />}
            title={t('userBuilds.minimapSection', 'Миникарта')}
            summary={minimapSummary}
            hint={
              minimap === null
                ? t('userBuilds.minimapHintDefault', 'По умолчанию - мини-карта выбранного редукса. Можно взять из другого редукса или из библиотеки кастомных миникарт.')
                : minimap.kind === 'library'
                  ? t('userBuilds.minimapHintLibrary', 'Используется кастомная миникарта из библиотеки.')
                  : t('userBuilds.minimapHintOverride', 'Миникарта берётся из другого редукса.')
            }
          >
            {(() => {
              const baseRedux = reduxId ? reduxById.get(reduxId) : null;
              const baseHasMinimap = !!baseRedux?.components?.minimap?.isFound;
              const isDefault = minimap === null;
              const overrideId = minimap?.kind === 'override' ? minimap.reduxId : null;
              const libraryId  = minimap?.kind === 'library'  ? minimap.minimapLibraryId : null;

              return (
                <motion.div
                  variants={gridV}
                  initial="hidden"
                  animate="visible"
                  className="flex flex-col gap-3"
                >
                  <div className="grid grid-cols-1 gap-2.5">
                    <motion.div variants={cardV}>
                      <ArmorSpecialCard
                        icon={<MapIcon size={16} />}
                        title={t('userBuilds.useDefault', 'По умолчанию')}
                        subtitle={
                          reduxId === null
                            ? t('userBuilds.pickReduxFirst', 'Сначала выберите редукс выше')
                            : baseHasMinimap
                              ? t('userBuilds.minimapFromBase', 'Мини-карта из выбранного редукса')
                              : t('userBuilds.minimapBaseHasNone', 'У выбранного редукса нет мини-карты')
                        }
                        selected={isDefault}
                        onClick={() => setMinimap(null)}
                      />
                    </motion.div>
                  </div>

                  <SearchBox value={query} onChange={setQuery} placeholder={t('userBuilds.searchPlaceholder', 'Поиск по названию')} />
                  {minimapLibraryItems.length === 0 && minimapDonors.length === 0 ? (
                    <EmptyHint text={t('userBuilds.minimapNoDonors', 'Пока нет ни одной миникарты - ни в кастомной библиотеке, ни в редуксах с распознанной мини-картой.')} />
                  ) : (
                    <div className="grid grid-cols-2 md:grid-cols-3 2xl:grid-cols-4 gap-2.5">
                      {}
                      {minimapLibraryItems.filter(lib => matchesQuery(lib.name, lib.author)).map((lib) => (
                        <motion.div key={'lib:' + lib.id} variants={cardV}>
                          <MinimapPickerCard
                            title={lib.name}
                            subtitle={lib.author || t('userBuilds.badgeCustom', 'кастомный')}
                            badge={t('userBuilds.badgeCustom', 'кастомный')}
                            previewUrl={lib.previewUrl || null}
                            selected={libraryId === lib.id}
                            onClick={() => setMinimap({ kind: 'library', minimapLibraryId: lib.id })}
                          />
                        </motion.div>
                      ))}
                      {minimapDonors.filter(r => matchesQuery(r.name, r.author)).map((r) => (
                        <motion.div key={'rdx:' + r.id} variants={cardV}>
                          <MinimapPickerCard
                            title={r.name}
                            subtitle={r.author || '-'}
                            badge={r.id === reduxId ? t('userBuilds.badgeBase', '★ база') : t('userBuilds.badgeFromRedux', 'из редукса')}
                            previewUrl={r.componentScreenshots?.minimap || r.previewUrl || null}
                            selected={overrideId === r.id}
                            onClick={() => setMinimap({ kind: 'override', reduxId: r.id })}
                          />
                        </motion.div>
                      ))}
                    </div>
                  )}
                </motion.div>
              );
            })()}
          </Section>
        </motion.div>

        {}
        <motion.div variants={sectionV}>
          <Section
            step={6}
            open={openStep === 6}
            onToggle={() => toggleStep(6)}
            icon={<Crosshair size={14} className="text-accent" />}
            title={t('userBuilds.reticleSection', 'Прицел')}
            summary={reticleSummary}
            hint={
              reticle === null
                ? t('userBuilds.reticleHintDefault', 'По умолчанию - прицел выбранного редукса. Можно взять из другого редукса или из библиотеки кастомных прицелов.')
                : reticle.kind === 'library'
                  ? t('userBuilds.reticleHintLibrary', 'Используется кастомный прицел из библиотеки.')
                  : t('userBuilds.reticleHintOverride', 'Прицел берётся из другого редукса.')
            }
          >
            {(() => {
              const baseRedux = reduxId ? reduxById.get(reduxId) : null;
              const baseHasReticle = !!baseRedux?.components?.crosshair?.isFound;
              const isDefault = reticle === null;
              const overrideId = reticle?.kind === 'override' ? reticle.reduxId : null;
              const libraryId  = reticle?.kind === 'library'  ? reticle.reticleLibraryId : null;

              return (
                <motion.div
                  variants={gridV}
                  initial="hidden"
                  animate="visible"
                  className="flex flex-col gap-3"
                >
                  <div className="grid grid-cols-1 gap-2.5">
                    <motion.div variants={cardV}>
                      <ArmorSpecialCard
                        icon={<Crosshair size={16} />}
                        title={t('userBuilds.useDefault', 'По умолчанию')}
                        subtitle={
                          reduxId === null
                            ? t('userBuilds.pickReduxFirst', 'Сначала выберите редукс выше')
                            : baseHasReticle
                              ? t('userBuilds.reticleFromBase', 'Прицел из выбранного редукса')
                              : t('userBuilds.reticleBaseHasNone', 'У выбранного редукса нет прицела')
                        }
                        selected={isDefault}
                        onClick={() => setReticle(null)}
                      />
                    </motion.div>
                  </div>

                  <SearchBox value={query} onChange={setQuery} placeholder={t('userBuilds.searchPlaceholder', 'Поиск по названию')} />
                  {reticleLibraryItems.length === 0 && reticleDonors.length === 0 ? (
                    <EmptyHint text={t('userBuilds.reticleNoDonors', 'Пока нет ни одного прицела - ни в кастомной библиотеке, ни в редуксах с прикреплённым фото прицела.')} />
                  ) : (
                    <div className="grid grid-cols-2 md:grid-cols-3 2xl:grid-cols-4 gap-2.5">
                      {reticleLibraryItems.filter(lib => matchesQuery(lib.name, lib.author)).map((lib) => (
                        <motion.div key={'lib:' + lib.id} variants={cardV}>
                          <MinimapPickerCard
                            title={lib.name}
                            subtitle={lib.author || t('userBuilds.badgeCustom', 'кастомный')}
                            badge={t('userBuilds.badgeCustom', 'кастомный')}
                            previewUrl={lib.previewUrl || null}
                            selected={libraryId === lib.id}
                            onClick={() => setReticle({ kind: 'library', reticleLibraryId: lib.id })}
                          />
                        </motion.div>
                      ))}
                      {reticleDonors.filter(r => matchesQuery(r.name, r.author)).map((r) => (
                        <motion.div key={'rdx:' + r.id} variants={cardV}>
                          <MinimapPickerCard
                            title={r.name}
                            subtitle={r.author || '-'}
                            badge={r.id === reduxId ? t('userBuilds.badgeBase', '★ база') : t('userBuilds.badgeFromRedux', 'из редукса')}
                            previewUrl={r.componentScreenshots?.crosshair || r.previewUrl || null}
                            selected={overrideId === r.id}
                            onClick={() => setReticle({ kind: 'override', reduxId: r.id })}
                          />
                        </motion.div>
                      ))}
                    </div>
                  )}
                </motion.div>
              );
            })()}
          </Section>
        </motion.div>

        {}
        <motion.div variants={sectionV}>
          <Section
            step={7}
            open={openStep === 7}
            onToggle={() => toggleStep(7)}
            icon={<Volume2 size={14} className="text-accent" />}
            title={t('userBuilds.soundsSection', 'Звуки')}
            summary={soundsSummary}
            hint={
              sounds === null
                ? t('userBuilds.soundsHintDefault', 'По умолчанию - стандартные звуки GTA. Можно подменить пак из библиотеки кастомных звуков.')
                : t('userBuilds.soundsHintLibrary', 'Используется кастомный пак звуков из библиотеки.')
            }
          >
            {(() => {
              const isDefault = sounds === null;
              const libraryId = sounds?.kind === 'library' ? sounds.soundsLibraryId : null;
              const q = soundsQuery.trim().toLowerCase();
              const visibleSounds = !q
                ? soundsLibraryItems
                : soundsLibraryItems.filter(l =>
                    l.name.toLowerCase().includes(q)
                    || (l.author ?? '').toLowerCase().includes(q));
              return (
                <motion.div
                  variants={gridV}
                  initial="hidden"
                  animate="visible"
                  className="flex flex-col gap-3"
                >
                  <div className="grid grid-cols-1 gap-2.5">
                    <motion.div variants={cardV}>
                      <ArmorSpecialCard
                        icon={<Volume2 size={16} />}
                        title={t('userBuilds.useDefault', 'По умолчанию')}
                        subtitle={t('userBuilds.soundsDefaultHint', 'Без подмены звуков')}
                        selected={isDefault}
                        onClick={() => setSounds(null)}
                      />
                    </motion.div>
                  </div>

                  {soundsLibraryItems.length === 0 ? (
                    <EmptyHint text={t('userBuilds.soundsNoDonors', 'Пока в библиотеке нет ни одного пака звуков. Залить можно через Admin → Library → Загрузить звуки.')} />
                  ) : (
                    <>
                      <SearchBox
                        value={soundsQuery}
                        onChange={setSoundsQuery}
                        placeholder={t('userBuilds.soundsSearchPlaceholder', 'Поиск звуков')}
                      />
                      {visibleSounds.length === 0 ? (
                        <EmptyHint text={t('userBuilds.soundsNoMatch', 'По запросу ничего не нашлось.')} />
                      ) : (
                        <div className="grid grid-cols-2 md:grid-cols-3 2xl:grid-cols-4 gap-2.5">
                          {visibleSounds.map((lib) => (
                            <motion.div key={'snd:' + lib.id} variants={cardV}>
                              <MinimapPickerCard
                                title={lib.name}
                                subtitle={lib.author || `${(lib.sizeBytes / 1024 / 1024).toFixed(0)} MB`}
                                badge={t('userBuilds.badgeCustom', 'кастомный')}
                                previewUrl={lib.previewUrl || null}
                                videoUrl={lib.previewVideoUrl || null}
                                selected={libraryId === lib.id}
                                onClick={() => setSounds({ kind: 'library', soundsLibraryId: lib.id })}
                                disablePreview
                              />
                            </motion.div>
                          ))}
                        </div>
                      )}
                    </>
                  )}
                </motion.div>
              );
            })()}
          </Section>
        </motion.div>

        <motion.div variants={sectionV}>
          <Section
            step={8}
            open={openStep === 8}
            onToggle={() => toggleStep(8)}
            icon={<Monitor size={14} className="text-accent" />}
            title={t('userBuilds.settingsSection', 'Сеттингс (графический пресет)')}
            hint={t('userBuilds.settingsHint', 'Применится к настройкам GTA при установке сборки. Можно не выбирать.')}
            summary={settingsSummary}
          >
            <SearchBox value={query} onChange={setQuery} placeholder={t('userBuilds.settingsSearchPlaceholder', 'Поиск пресета')} />
            <div className="flex flex-col gap-1.5 mt-1">
              <button
                type="button"
                onClick={() => setSettingsPresetId(null)}
                style={{ outline: 'none' }}
                className={
                  'w-full flex items-center justify-between gap-3 px-3.5 h-11 rounded-xl border text-sm transition-all duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] ' +
                  (settingsPresetId === null
                    ? 'bg-bg-elevated/80 border-white/[0.20] text-text-primary'
                    : 'bg-bg-elevated/55 border-white/[0.08] text-text-secondary hover:bg-bg-elevated/75 hover:border-white/[0.18]')
                }
              >
                <span className="font-semibold">{t('userBuilds.settingsNone', 'Без сеттингов')}</span>
                <AnimatePresence initial={false}>
                  {settingsPresetId === null && (
                    <motion.span
                      key="check-none"
                      initial={{ opacity: 0, scale: 0.5 }}
                      animate={{ opacity: 1, scale: 1 }}
                      exit={{ opacity: 0, scale: 0.5 }}
                      transition={{ duration: 0.2, ease: EASE_DEPTH }}
                      className="shrink-0 text-accent"
                    >
                      <Check size={15} />
                    </motion.span>
                  )}
                </AnimatePresence>
              </button>
              {presets.filter(p => matchesQuery(p.name, p.author)).map((p) => {
                const sel = settingsPresetId === p.id;
                return (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => setSettingsPresetId(p.id)}
                    style={{ outline: 'none' }}
                    className={
                      'w-full flex items-center justify-between gap-3 px-3.5 h-12 rounded-xl border text-sm transition-all duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] ' +
                      (sel
                        ? 'bg-bg-elevated/80 border-white/[0.20] text-text-primary'
                        : 'bg-bg-elevated/55 border-white/[0.08] text-text-secondary hover:bg-bg-elevated/75 hover:border-white/[0.18]')
                    }
                  >
                    <span className="min-w-0 flex flex-col items-start text-left">
                      <span className="font-semibold text-text-primary truncate w-full">{p.name}</span>
                      <span className="text-[11px] text-text-muted truncate w-full">
                        {[p.author, p.computedGainPercent ? `+${p.computedGainPercent}% FPS` : null].filter(Boolean).join(' · ')}
                      </span>
                    </span>
                    <AnimatePresence initial={false}>
                      {sel && (
                        <motion.span
                          key="check"
                          initial={{ opacity: 0, scale: 0.5 }}
                          animate={{ opacity: 1, scale: 1 }}
                          exit={{ opacity: 0, scale: 0.5 }}
                          transition={{ duration: 0.2, ease: EASE_DEPTH }}
                          className="shrink-0 text-accent"
                        >
                          <Check size={15} />
                        </motion.span>
                      )}
                    </AnimatePresence>
                  </button>
                );
              })}
              {presets.length === 0 && (
                <EmptyHint text={t('userBuilds.settingsNoPresets', 'В каталоге нет пресетов. Зайдите в «Настройки игры» и обновите список.')} />
              )}
            </div>
          </Section>
        </motion.div>
        </div>

        <motion.div variants={sectionV} className="pt-2">
          <GlassPanel
            depth="z3" tint="ultra" rounded="3xl" highlight edge
            className="relative overflow-hidden border border-white/[0.08]"
          >
            <span aria-hidden className="absolute inset-0 pointer-events-none bg-bg-elevated/55" />
            <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                                         bg-gradient-to-r from-transparent via-white/40 to-transparent" />
            <span
              aria-hidden
              className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
              style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
            />
            <div className="relative px-5 py-4 flex items-center gap-4">
            {}
            <button
              type="button"
              onClick={handlePickCover}
              disabled={uploadingCover}
              title={coverUrl ? t('userBuilds.coverChange', 'Сменить обложку') : t('userBuilds.coverUpload', 'Загрузить обложку (необязательно)')}
              style={{
                outline: 'none',
                background: coverUrl
                  ? `url(${coverUrl}) center / cover no-repeat`
                  : 'color-mix(in srgb, var(--accent) 8%, var(--bg-base))',
                boxShadow: coverUrl
                  ? '0 0 0 1px color-mix(in srgb, var(--accent) 28%, transparent)'
                  : 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 18%, transparent)',
              }}
              className="shrink-0 w-16 h-20 rounded-lg overflow-hidden relative
                         transition-[box-shadow] duration-200 hover:shadow-lg
                         disabled:cursor-wait"
            >
              {!coverUrl && (
                <span className="absolute inset-0 flex flex-col items-center justify-center gap-0.5
                                 text-[9px] uppercase tracking-[0.16em] text-accent font-bold">
                  <ImageOff size={14} />
                  <span>{t('userBuilds.coverPhoto', 'фото')}</span>
                </span>
              )}
              {uploadingCover && (
                <span className="absolute inset-0 flex items-center justify-center
                                 bg-black/60 text-[9px] uppercase tracking-[0.18em] font-bold text-white">
                  ...
                </span>
              )}
            </button>
            <div className="flex-1 min-w-0 flex flex-col gap-1.5">
              <label className="text-[11px] text-text-muted">
                {t('userBuilds.nameLabel', 'Название (необязательно)')}
              </label>
              <input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t('userBuilds.namePlaceholder', 'Моя боевая сборка')}
                style={{ outline: 'none' }}
                className="h-9 px-3 rounded-lg text-[13px] text-text-primary placeholder:text-text-muted
                           bg-bg-base hover:bg-bg-base
                           border border-border-subtle hover:border-border-strong
                           focus:border-accent
                           transition-colors"
              />
              {coverError && (
                <span className="text-[11px] text-status-error">{coverError}</span>
              )}
            </div>
            <div className="shrink-0 flex flex-col gap-1.5">
              <label className="text-[11px] text-text-muted">
                {t('userBuilds.authorLabel', 'Автор')}
              </label>
              <span className="inline-flex items-center gap-2 h-9 px-3 rounded-lg
                               bg-accent-soft text-accent text-[13px] font-medium">
                <User size={13} />
                {author}
              </span>
            </div>
            </div>
          </GlassPanel>
        </motion.div>

        {}
        <motion.div variants={sectionV} className="pt-2">
          <GlassPanel
            depth="z3" tint="ultra" rounded="3xl" highlight edge
            className="relative overflow-hidden border border-white/[0.08]"
          >
            <span aria-hidden className="absolute inset-0 pointer-events-none bg-bg-elevated/55" />
            <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                                         bg-gradient-to-r from-transparent via-white/40 to-transparent" />
            <span
              aria-hidden
              className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
              style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
            />
            <div className="relative px-5 py-4 flex items-center gap-4">
            <div className="flex-1 min-w-0 flex flex-col gap-0.5">
              <span className="text-[11px] text-text-muted">
                {t('userBuilds.readyEyebrow', 'Готово к сохранению')}
              </span>
              <span className="text-[13px] text-text-secondary leading-relaxed">
                {!reduxId
                  ? t('userBuilds.readyMissingRedux', 'Шаг 1 - выберите базу редукса.')
                  : (!packId && overridesN === 0)
                    ? t('userBuilds.readyMissingPack', 'Шаг 2 - выберите ганпак или добавьте пушки.')
                    : t('userBuilds.readyAllSet', 'Все шаги пройдены. Можно сохранять - сборка появится в списке.')}
              </span>
              {saveError && (
                <span className="text-[12px] text-status-error leading-snug mt-1">{saveError}</span>
              )}
            </div>
            <button
              type="button"
              onClick={onSave}
              disabled={!canSave || saving}
              style={{ outline: 'none' }}
              className="shrink-0 inline-flex items-center gap-2 px-5 h-11 rounded-xl text-sm font-bold uppercase tracking-wider
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/75 hover:border-white/[0.18]
                         disabled:opacity-40 disabled:cursor-not-allowed
                         transition-colors"
            >
              {saving ? t('userBuilds.saving', 'Сохранение…') : t('userBuilds.save', 'Сохранить сборку')}
            </button>
            </div>
          </GlassPanel>
        </motion.div>
      </div>

      {}
      {glbView && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            zIndex: 300,
          }}
        >
          <GlbViewerModal
            glbUrl={glbView.url}
            title={glbView.title}
            subjectKind={glbView.kind}
            onClose={() => setGlbView(null)}
          />
        </div>
      )}

      {}
      <GunReplaceModal
        open={replaceTarget !== null}
        internalName={replaceTarget?.internalName ?? null}
        displayName={replaceTarget?.displayName || ''}
        category={replaceTarget?.category || ''}
        basePackId={packId ?? ''}
        current={replaceTarget ? gunSlots[replaceTarget.internalName] ?? null : null}
        onPickOverride={(gunpackId, gunId) => {
          if (!replaceTarget) return;
          setSlotOverride(replaceTarget.internalName, gunpackId, gunId);
          setReplaceTarget(null);
        }}
        onUseDefault={() => {
          if (!replaceTarget) return;
          clearSlot(replaceTarget.internalName);
          setReplaceTarget(null);
        }}
        onUseVanilla={() => {
          if (!replaceTarget) return;
          setSlotVanilla(replaceTarget.internalName);
          setReplaceTarget(null);
        }}
        onClose={() => setReplaceTarget(null)}
        onOpenGunpack={(gpId) => { setReplaceTarget(null); setPreviewGunpackId(gpId); }}
      />
    </motion.div>
    <BigPreviewOverlay preview={bigPreview} />
    </BigPreviewCtx.Provider>
  );
}

function Section({
  open, title, hint, headerAction, children,
}: {
  step: number;
  icon: React.ReactNode;
  title: string;
  hint?: string;
  summary?: React.ReactNode;
  open: boolean;
  onToggle: () => void;
  headerAction?: React.ReactNode;
  children: React.ReactNode;
}) {
  if (!open) return null;
  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.26, ease: EASE_DEPTH }}
    >
      <GlassPanel
        depth="z3" tint="ultra" rounded="3xl" highlight edge
        className="relative overflow-hidden border border-white/[0.08]"
      >
        <span aria-hidden className="absolute inset-0 pointer-events-none bg-bg-elevated/55" />
        <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                                     bg-gradient-to-r from-transparent via-white/40 to-transparent" />
        <span
          aria-hidden
          className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
          style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 16%, transparent) 0%, transparent 70%)' }}
        />
        <div className="relative p-5 flex flex-col gap-3">
          <div className="flex items-start justify-between gap-3">
            <div className="flex flex-col gap-0.5 min-w-0">
              <span className="text-[12.5px] font-bold uppercase tracking-[0.16em] text-text-primary">{title}</span>
              {hint && <span className="text-[11px] text-text-muted leading-snug">{hint}</span>}
            </div>
            {headerAction && <div className="shrink-0">{headerAction}</div>}
          </div>
          {children}
        </div>
      </GlassPanel>
    </motion.div>
  );
}

function StepChip({
  title, summary, active, onClick,
}: {
  n: number;
  icon: React.ReactNode;
  title: string;
  summary: React.ReactNode;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{ outline: 'none' }}
      className={
        'group relative flex flex-col items-center justify-center text-center gap-1.5 ' +
        'px-4 py-4 rounded-2xl border min-h-[76px] min-w-[120px] flex-1 backdrop-blur-xl transition-colors ' +
        (active
          ? 'bg-bg-elevated/95 border-white/30'
          : 'bg-bg-elevated/95 border-white/[0.08] hover:bg-bg-elevated hover:border-white/[0.18]')
      }
    >
      <span className={'text-[15px] font-bold uppercase tracking-wide leading-none whitespace-nowrap '
        + (active ? 'text-white' : 'text-text-primary')}>{title}</span>
      {summary && (
        <span className={'text-[10.5px] leading-tight truncate max-w-full '
          + (active ? 'text-white/70' : 'text-text-muted')}>{summary}</span>
      )}
    </button>
  );
}

function EmptyHint({ text }: { text: string }) {
  return (
    <div className="rounded-xl bg-glass/40 p-4 text-center text-xs text-text-muted inline-flex items-center justify-center gap-2 w-full">
      <AlertTriangle size={12} />
      {text}
    </div>
  );
}

function SearchBox({ value, onChange, placeholder }: {
  value: string;
  onChange: (v: string) => void;
  placeholder: string;
}) {
  const { t } = useTranslation();
  return (
    <div className="relative mb-3 overflow-hidden rounded-xl border border-white/[0.10]
                    bg-bg-elevated/55 backdrop-blur-xl
                    focus-within:border-white/[0.22] hover:border-white/[0.16] transition-colors">
      <span
        aria-hidden
        className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                   bg-gradient-to-r from-transparent via-white/40 to-transparent"
      />
      <span
        aria-hidden
        className="absolute -top-12 -right-8 w-32 h-32 pointer-events-none blur-3xl"
        style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 16%, transparent) 0%, transparent 70%)' }}
      />
      <Search size={15} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-text-muted pointer-events-none z-10" />
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        style={{ outline: 'none' }}
        className="relative z-10 w-full h-11 pl-10 pr-10 bg-transparent text-[13px]
                   text-text-primary placeholder:text-text-muted"
      />
      {value && (
        <button
          type="button"
          onClick={() => onChange('')}
          style={{ outline: 'none' }}
          className="absolute right-3 top-1/2 -translate-y-1/2 z-10 text-text-muted hover:text-text-primary transition-colors"
          title={t('userBuilds.clear', 'Очистить')}
        >
          <X size={14} />
        </button>
      )}
    </div>
  );
}

function PickerCard({
  selected, onClick, name, subtitle, coverUrl, fallbackIcon, footer,
}: {
  selected: boolean;
  onClick: () => void;
  name: string;
  subtitle: string | null;
  coverUrl: string | null;
  fallbackIcon: React.ReactNode;
  footer?: React.ReactNode;
}) {
  return (
    <motion.button
      type="button"
      onClick={onClick}
      whileTap={{ scale: 0.97 }}
      whileHover={{ y: -3 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}
      style={{ outline: 'none' }}
      className={
        'group relative w-full flex flex-col rounded-2xl text-left overflow-hidden border ' +
        'bg-white/[0.04] backdrop-blur-xl ' +
        'transition-[border-color,box-shadow,background-color] duration-300 ease-smooth ' +
        (selected
          ? 'border-accent shadow-glow-accent'
          : 'border-white/[0.08] hover:bg-white/[0.06] hover:border-[color-mix(in_srgb,var(--accent)_45%,transparent)]')
      }
    >
      <div className="relative aspect-[16/10] w-full bg-glass overflow-hidden">
        {coverUrl ? (
          <img src={coverUrl} alt={name} draggable={false} className="absolute inset-0 w-full h-full object-cover" />
        ) : (
          <div className="absolute inset-0 flex items-center justify-center text-text-muted/70">{fallbackIcon}</div>
        )}
        <span aria-hidden className="absolute inset-x-0 bottom-0 h-10 bg-gradient-to-t from-black/55 to-transparent" />
        {selected && (
          <motion.span
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: 'spring', stiffness: 420, damping: 26 }}
            className="absolute top-2 right-2 w-6 h-6 rounded-full bg-accent text-bg-primary flex items-center justify-center shadow-glow-accent"
          >
            <Check size={12} strokeWidth={3} />
          </motion.span>
        )}
      </div>
      <div className="flex flex-col gap-0.5 p-3">
        <span className={'text-[13px] font-bold truncate leading-tight ' + (selected ? 'text-accent' : 'text-text-primary')}>{name}</span>
        {subtitle && <span className="text-[10.5px] text-text-secondary truncate">{subtitle}</span>}
        {footer}
      </div>
    </motion.button>
  );
}

const COMPONENT_ICON: Record<string, LucideIcon> = {
  armor:     Shield,
  arena:     Building2,
  minimap:   MapIcon,
  crosshair: Crosshair,
  tracers:   Wind,
  bloodfx:   Droplet,
  timecycle: Cloud,
};
const COMPONENT_LABEL: Record<string, string> = {
  armor:     'Броня',
  arena:     'Арена',
  minimap:   'Минимапа',
  crosshair: 'Прицел',
  tracers:   'Трейсера',
  bloodfx:   'Эффекты',
  timecycle: 'Таймцикл',
};
const COMPONENT_ORDER = ['armor', 'arena', 'minimap', 'crosshair', 'tracers', 'bloodfx', 'timecycle'] as const;

function ReduxBigPickerCard({
  redux, selected, onClick,
}: {
  redux: ReduxItem;
  selected: boolean;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const present = COMPONENT_ORDER.filter(k => redux.components?.[k]?.isFound);

  return (
    <motion.button
      type="button"
      onClick={onClick}
      whileTap={{ scale: 0.985 }}
      whileHover={{ y: -3 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}

      className="group relative w-full h-[210px] text-left"
      style={{
        outline: 'none',
        borderRadius: 16,

        background: selected
          ? 'color-mix(in srgb, var(--accent) 28%, var(--bg-elevated))'
          : 'var(--bg-elevated)',
        boxShadow: selected
          ? '0 14px 36px -4px color-mix(in srgb, var(--accent) 55%, transparent), '
            + 'inset 0 0 0 2px color-mix(in srgb, var(--accent) 90%, transparent), '
            + '0 0 0 4px color-mix(in srgb, var(--accent) 18%, transparent)'
          : 'inset 0 0 0 1px rgba(255,255,255,0.04)',
      }}
    >
      {}
      <div className="absolute inset-0 rounded-2xl overflow-hidden">
        <div
          className="absolute inset-0 transition-transform duration-700 ease-out group-hover:scale-[1.05]"
          style={{
            background: redux.previewUrl
              ? `url(${redux.previewUrl}) center / cover no-repeat`
              : 'linear-gradient(135deg, color-mix(in srgb, var(--accent) 24%, #0a0a14), #0a0a14)',
          }}
        />
        <span aria-hidden style={{ height: '32%' }} className="pointer-events-none absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/40 to-transparent" />
      </div>

      {}
      {present.length > 0 && (
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); setDropdownOpen((x) => !x); }}
          className={
            'absolute top-2 right-2 z-20 inline-flex items-center gap-1.5 h-8 px-2.5 ' +
            'rounded-full bg-black/55 backdrop-blur-md text-white ' +
            'text-[10.5px] font-bold uppercase tracking-wider transition-opacity duration-150 ' +
            'hover:bg-black/70 ' +
            (dropdownOpen ? 'opacity-100' : 'opacity-0 group-hover:opacity-100')
          }
          style={{ outline: 'none' }}
          title={t('userBuilds.allComponentsTitle', 'Все компоненты редукса')}
        >
          <Layers size={11} strokeWidth={2.4} />
          {t('userBuilds.components', 'Компоненты')}
          <motion.span animate={{ rotate: dropdownOpen ? 180 : 0 }} transition={{ duration: 0.2 }}>
            <ChevronDown size={11} strokeWidth={2.4} />
          </motion.span>
        </button>
      )}

      {}
      <AnimatePresence>
        {dropdownOpen && present.length > 0 && (
          <motion.div
            key="components-dropdown"
            initial={{ opacity: 0, y: -8, scale: 0.96 }}
            animate={{ opacity: 1, y: 0,  scale: 1 }}
            exit={{ opacity: 0, y: -6, scale: 0.97 }}
            transition={{ duration: 0.22, ease: EASE_DEPTH }}
            onClick={(e) => e.stopPropagation()}
            className="absolute top-12 right-2 z-30 max-w-[80%] flex flex-wrap gap-1 p-2.5 rounded-xl"
            style={{
              background: 'rgba(15, 15, 25, 0.92)',
              backdropFilter: 'blur(18px) saturate(160%)',
              WebkitBackdropFilter: 'blur(18px) saturate(160%)',
              boxShadow: '0 12px 28px rgba(0,0,0,0.55), inset 0 0 0 1px rgba(255,255,255,0.06)',
            }}
          >
            {present.map((k) => {
              const Icon = COMPONENT_ICON[k];
              return (
                <span
                  key={k}
                  className="inline-flex items-center gap-1.5 px-2 py-1 rounded-md
                             text-[10px] font-semibold uppercase tracking-wider
                             text-text-secondary"
                  style={{
                    background: 'rgba(255,255,255,0.05)',
                    boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.05)',
                  }}
                >
                  <Icon size={11} strokeWidth={2.2} className="text-accent" />
                  {t('userBuilds.component_' + k, COMPONENT_LABEL[k])}
                </span>
              );
            })}
          </motion.div>
        )}
      </AnimatePresence>

      {}
      {selected && (
        <motion.span
          initial={{ scale: 0.6, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ type: 'spring', stiffness: 420, damping: 26 }}
          className="absolute top-2 left-2 z-20 w-7 h-7 rounded-full bg-accent text-bg-primary flex items-center justify-center shadow-[0_4px_12px_color-mix(in_srgb,var(--accent)_45%,transparent)]"
        >
          <Check size={13} strokeWidth={3} />
        </motion.span>
      )}

      {}
      <div className="absolute bottom-0 inset-x-0 z-10 p-3 flex flex-col gap-0.5">
        <span className="text-[15px] font-bold text-white tracking-tight truncate
                         drop-shadow-[0_2px_8px_rgba(0,0,0,0.7)]">
          {redux.name}
        </span>
      </div>
    </motion.button>
  );
}

function ArmorTilePreview({ previewUrl, glbUrl }: { previewUrl: string | null; glbUrl: string | null }) {
  if (previewUrl) {
    return (
      <img
        src={previewUrl}
        alt=""
        draggable={false}
        className="w-full h-full object-contain select-none"
        onError={e => (e.currentTarget.style.display = 'none')}
      />
    );
  }
  return <ArmorPreview3D glbUrl={glbUrl} />;
}

function ArmorSpecialCard({
  icon, title, subtitle, selected, onClick, tone,
}: {
  icon:     React.ReactNode;
  title:    string;
  subtitle: string;
  selected: boolean;
  onClick:  () => void;
  tone?:    'muted';
}) {
  return (
    <motion.button
      type="button"
      onClick={onClick}
      whileTap={{ scale: 0.97 }}
      whileHover={{ y: -2 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}
      style={{ outline: 'none' }}
      className={
        'group relative w-full h-[78px] flex items-center gap-3 px-4 rounded-2xl text-left border ' +
        'backdrop-blur-xl transition-colors ' +
        (selected
          ? 'bg-white/[0.05] border-white/30'
          : 'bg-white/[0.05] border-white/[0.10] hover:bg-white/[0.08] hover:border-white/[0.20]')
      }
    >
      <span
        className={
          'shrink-0 w-10 h-10 rounded-xl flex items-center justify-center ' +
          (selected
            ? 'bg-white/[0.12] text-white'
            : tone === 'muted' ? 'bg-white/[0.08] text-text-muted' : 'bg-accent-soft text-accent')
        }
      >
        {icon}
      </span>
      <div className="flex-1 min-w-0 flex flex-col gap-0.5">
        <span className={'text-sm font-bold truncate leading-tight ' + (selected ? 'text-white' : 'text-text-primary')}>{title}</span>
        <span className="text-[10.5px] text-text-secondary truncate">{subtitle}</span>
      </div>
      {selected && (
        <motion.span
          initial={{ scale: 0.6, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ type: 'spring', stiffness: 420, damping: 26 }}
          className="shrink-0 w-6 h-6 rounded-full bg-white/15 text-white flex items-center justify-center"
        >
          <Check size={12} strokeWidth={3} />
        </motion.span>
      )}
    </motion.button>
  );
}

function ArmorDonorCard({
  redux, isBase, selected, onClick, onPreview3D,
}: {
  redux: ReduxItem;
  isBase: boolean;
  selected: boolean;
  onClick: () => void;
  onPreview3D: (() => void) | null;
}) {
  const { t } = useTranslation();
  const armorGlbUrl = redux.components?.armor?.glbUrl ?? null;
  const hover = useHoverPreview({ url: redux.componentScreenshots?.armor, title: redux.name, subtitle: redux.author, armor: true });
  return (
    <motion.button
      type="button"
      onClick={onClick}
      {...hover}
      whileTap={{ scale: 0.97 }}
      whileHover={{ y: -3 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}
      className="group relative w-full flex flex-col rounded-2xl text-left overflow-hidden"
      style={{
        outline: 'none',
        background: selected
          ? 'color-mix(in srgb, var(--accent) 28%, var(--bg-elevated))'
          : 'var(--bg-elevated)',
        boxShadow: selected
          ? '0 14px 36px -4px color-mix(in srgb, var(--accent) 55%, transparent), inset 0 0 0 2px color-mix(in srgb, var(--accent) 90%, transparent), 0 0 0 4px color-mix(in srgb, var(--accent) 18%, transparent)'
          : 'inset 0 0 0 1px rgba(255,255,255,0.04)',
      }}
    >
      {}
      <div className="relative aspect-[4/3] w-full bg-glass overflow-hidden">
        <div className="absolute inset-0 flex items-center justify-center">
          <ArmorTilePreview previewUrl={redux.componentScreenshots?.armor ?? null} glbUrl={armorGlbUrl} />
        </div>
        <span aria-hidden className="pointer-events-none absolute inset-x-0 top-0 h-px bg-white/10" />
        <span aria-hidden className="pointer-events-none absolute inset-x-0 bottom-0 h-12 bg-gradient-to-t from-black/55 to-transparent" />
        <ArmorServerBadges servers={redux.supportedServers} />
        {onPreview3D && (
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onPreview3D(); }}
            className="absolute top-2 right-2 inline-flex items-center gap-1.5 h-8 px-2.5
                       rounded-full bg-black/55 backdrop-blur-md text-white
                       text-[10.5px] font-bold uppercase tracking-wider
                       opacity-0 group-hover:opacity-100 hover:bg-black/70
                       transition-opacity duration-150 z-10"
            style={{ outline: 'none' }}
            title={t('userBuilds.armorPreview3D', '3D просмотр брони')}
          >
            <Eye size={12} strokeWidth={2.4} />
            3D
          </button>
        )}
      </div>
      <div className="flex items-center gap-2 p-3">
        <div className="flex-1 min-w-0 flex flex-col gap-0.5">
          <span className="text-[13px] font-bold text-text-primary truncate leading-tight">
            {redux.name}
          </span>
          <span className={'text-[10.5px] truncate ' + (isBase ? 'text-accent font-bold' : 'text-text-secondary')}>
            {isBase && '★ '}
            {redux.author}
          </span>
        </div>
        {selected && (
          <motion.span
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: 'spring', stiffness: 420, damping: 26 }}
            className="shrink-0 w-7 h-7 rounded-full bg-accent text-bg-primary flex items-center justify-center"
          >
            <Check size={13} strokeWidth={3} />
          </motion.span>
        )}
      </div>
    </motion.button>
  );
}

function ArmorServerBadges({ servers }: { servers: string[] | null | undefined }) {
  const list = Array.isArray(servers) ? servers : [];
  const showsGta5rp   = list.includes('gta5rp');
  const showsMajestic = list.includes('majestic');
  if (!showsGta5rp && !showsMajestic) return null;
  return (
    <div className="absolute top-2 left-2 z-10 flex items-center gap-1.5 pointer-events-none">
      {showsGta5rp   && <ArmorServerPill logo={gta5rpLogo}   alt="GTA5RP" />}
      {showsMajestic && <ArmorServerPill logo={majesticLogo} alt="Majestic" />}
    </div>
  );
}

function ArmorServerPill({ logo, alt }: { logo: string; alt: string }) {
  return (
    <div className="px-1.5 py-1 rounded-md bg-black/70 backdrop-blur-md flex items-center" title={alt}>
      <img src={logo} alt={alt} className="w-3.5 h-3.5 object-contain" />
    </div>
  );
}

function ArmorLibraryCard({
  item, selected, onClick, onPreview3D,
}: {
  item: import('@/bridge/types').ArmorLibraryItem;
  selected: boolean;
  onClick: () => void;
  onPreview3D: (() => void) | null;
}) {
  const { t } = useTranslation();
  const hover = useHoverPreview({ url: item.previewUrl, title: item.name, subtitle: item.author || t('userBuilds.badgeCustom', 'кастомный'), armor: true });
  return (
    <motion.button
      type="button"
      onClick={onClick}
      {...hover}
      whileTap={{ scale: 0.97 }}
      whileHover={{ y: -3 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}
      className="group relative w-full flex flex-col rounded-2xl text-left overflow-hidden
                 transition-[box-shadow,background-color] duration-300 ease-depth"
      style={{
        outline: 'none',

        background: selected
          ? 'color-mix(in srgb, var(--accent) 28%, var(--bg-elevated))'
          : 'var(--bg-elevated)',

        boxShadow: selected
          ? '0 10px 28px color-mix(in srgb, var(--accent) 32%, transparent), inset 0 0 0 1px color-mix(in srgb, var(--accent) 55%, transparent)'
          : 'none',
      }}
    >
      <div className="relative aspect-[4/3] w-full bg-glass overflow-hidden">
        <div className="absolute inset-0 flex items-center justify-center">
          <ArmorTilePreview previewUrl={item.previewUrl || null} glbUrl={item.glbUrl || null} />
        </div>
        {}
        <span aria-hidden className="pointer-events-none absolute inset-x-0 bottom-0 h-12 bg-gradient-to-t from-black/55 to-transparent" />
        <ArmorServerBadges servers={item.supportedServers} />
        {onPreview3D && (
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onPreview3D(); }}
            className="absolute top-2 right-2 inline-flex items-center gap-1.5 h-8 px-2.5
                       rounded-full bg-black/55 backdrop-blur-md text-white
                       text-[10.5px] font-bold uppercase tracking-wider
                       opacity-0 group-hover:opacity-100 hover:bg-black/70
                       transition-opacity duration-150 z-10"
            style={{ outline: 'none' }}
            title={t('userBuilds.armorPreview3D', '3D просмотр брони')}
          >
            <Eye size={12} strokeWidth={2.4} />
            3D
          </button>
        )}
      </div>
      <div className="flex items-center gap-2 p-3">
        <div className="flex-1 min-w-0 flex flex-col gap-0.5">
          <span className="text-[13px] font-bold text-text-primary truncate leading-tight">
            {item.name}
          </span>
          <span className="text-[10.5px] text-accent font-semibold truncate">
            {t('userBuilds.badgeCustom', 'кастомный')}
            {item.author && (
              <span className="text-text-secondary font-normal"> · {item.author}</span>
            )}
          </span>
        </div>
        {selected && (
          <motion.span
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: 'spring', stiffness: 420, damping: 26 }}
            className="shrink-0 w-7 h-7 rounded-full bg-accent text-bg-primary flex items-center justify-center"
          >
            <Check size={13} strokeWidth={3} />
          </motion.span>
        )}
      </div>
    </motion.button>
  );
}

function ArenaDonorCard({
  redux, isBase, selected, onClick,
}: {
  redux: ReduxItem;
  isBase: boolean;
  selected: boolean;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  const arenaShot = redux.componentScreenshots?.arena;
  const tileBg = arenaShot || redux.previewUrl;
  const hover = useHoverPreview({ url: tileBg, title: redux.name, subtitle: redux.author });
  return (
    <motion.button
      type="button"
      onClick={onClick}
      {...hover}
      whileTap={{ scale: 0.97 }}
      whileHover={{ y: -3 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}
      className="group relative w-full h-[150px] text-left rounded-2xl overflow-hidden"
      style={{
        outline: 'none',
        background: selected
          ? 'color-mix(in srgb, var(--accent) 28%, var(--bg-elevated))'
          : 'var(--bg-elevated)',
        boxShadow: selected
          ? '0 14px 36px -4px color-mix(in srgb, var(--accent) 55%, transparent), inset 0 0 0 2px color-mix(in srgb, var(--accent) 90%, transparent), 0 0 0 4px color-mix(in srgb, var(--accent) 18%, transparent)'
          : 'inset 0 0 0 1px rgba(255,255,255,0.04)',
      }}
    >
      <div
        className="absolute inset-0 transition-transform duration-700 ease-out group-hover:scale-[1.05]"
        style={{
          background: tileBg
            ? `url(${tileBg}) center / cover no-repeat`
            : 'linear-gradient(135deg, color-mix(in srgb, var(--accent) 24%, #0a0a14), #0a0a14)',
        }}
      />
      <span aria-hidden style={{ height: '32%' }} className="pointer-events-none absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/40 to-transparent" />

      {selected && (
        <motion.span
          initial={{ scale: 0.6, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ type: 'spring', stiffness: 420, damping: 26 }}
          className="absolute top-2 left-2 z-20 w-7 h-7 rounded-full bg-accent text-bg-primary flex items-center justify-center shadow-[0_4px_12px_color-mix(in_srgb,var(--accent)_45%,transparent)]"
        >
          <Check size={13} strokeWidth={3} />
        </motion.span>
      )}

      <span className="absolute top-2 right-2 z-20 inline-flex items-center gap-1 h-6 px-2 rounded-full
                       bg-black/55 backdrop-blur-md text-white
                       text-[10px] font-bold uppercase tracking-[0.08em]">
        <Building2 size={11} strokeWidth={2.4} />
        {t('userBuilds.arenaBadge', 'Арена')}
      </span>

      <div className="absolute bottom-0 inset-x-0 z-10 p-3 flex flex-col gap-0.5">
        <span className="text-[14px] font-bold text-white tracking-tight truncate
                         drop-shadow-[0_2px_8px_rgba(0,0,0,0.7)]">
          {redux.name}
        </span>
        {redux.author && (
          <span className={'text-[10.5px] truncate drop-shadow-[0_1px_4px_rgba(0,0,0,0.7)] '
            + (isBase ? 'text-accent font-bold' : 'text-white/85')}>
            {isBase && '★ '}
            {redux.author}
          </span>
        )}
      </div>
    </motion.button>
  );
}

function MinimapPickerCard({
  title, subtitle, badge, previewUrl, videoUrl, selected, onClick, disablePreview,
}: {
  title: string;
  subtitle: string;
  badge: string;
  previewUrl: string | null;
  disablePreview?: boolean;
  videoUrl?: string | null;
  selected: boolean;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  const [videoOpen, setVideoOpen] = useState(false);
  const hasVideo = !!videoUrl;
  const hover = useHoverPreview({ url: disablePreview ? null : previewUrl, title, subtitle });
  return (
    <>
    <motion.button
      type="button"
      onClick={onClick}
      {...hover}
      whileTap={{ scale: 0.97 }}
      whileHover={{ y: -3 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}
      className="group relative w-full flex flex-col rounded-2xl text-left overflow-hidden"
      style={{
        outline: 'none',

        background: selected
          ? 'color-mix(in srgb, var(--accent) 28%, var(--bg-elevated))'
          : 'var(--bg-elevated)',
        boxShadow: selected
          ? '0 14px 36px -4px color-mix(in srgb, var(--accent) 55%, transparent), '
            + 'inset 0 0 0 2px color-mix(in srgb, var(--accent) 90%, transparent), '
            + '0 0 0 4px color-mix(in srgb, var(--accent) 18%, transparent)'
          : 'inset 0 0 0 1px rgba(255,255,255,0.04)',
      }}
    >
      <div className="relative aspect-[16/9] w-full bg-glass overflow-hidden">
        {previewUrl ? (
          <>
            <img
              src={previewUrl}
              alt=""
              aria-hidden
              className="absolute inset-0 w-full h-full object-cover"
              style={{
                filter: 'blur(20px) saturate(115%) brightness(0.6)',
                transform: 'scale(1.18)',
              }}
            />
            <img
              src={previewUrl}
              alt=""
              className="absolute inset-0 w-full h-full object-contain
                         transition-transform duration-700 ease-out group-hover:scale-[1.03]"
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
                    backdropFilter: 'blur(6px) saturate(120%)',
                    WebkitBackdropFilter: 'blur(6px) saturate(120%)',
                  }}
                />
                <div
                  className="pointer-events-none absolute inset-0 z-10 flex items-center justify-center
                             opacity-0 group-hover:opacity-100
                             transition-opacity duration-300 ease-out"
                >
                  <div
                    role="button"
                    tabIndex={0}
                    onClick={(e) => { e.stopPropagation(); setVideoOpen(true); }}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault(); e.stopPropagation(); setVideoOpen(true);
                      }
                    }}
                    className="pointer-events-none group-hover:pointer-events-auto cursor-pointer
                               focus-visible:outline-none
                               inline-flex items-center gap-2 px-3.5 h-9 rounded-full
                               bg-white/[0.10] border border-white/[0.30] text-white
                               backdrop-blur-md
                               shadow-[0_8px_24px_-6px_rgba(0,0,0,0.55)]"
                  >
                    <span
                      className="inline-flex items-center justify-center w-6 h-6 rounded-full
                                 bg-white text-black"
                    >
                      <Play size={11} strokeWidth={3} className="ml-0.5" />
                    </span>
                    <span className="text-[10px] font-bold uppercase tracking-[0.18em]">
                      {t('userBuilds.videoReview', 'Видео обзор · клик')}
                    </span>
                  </div>
                </div>
              </>
            )}
          </>
        ) : (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-1 text-text-muted/70">
            <ImageOff size={20} strokeWidth={1.5} />
            <span className="text-[10px] uppercase tracking-wider">{t('userBuilds.noPreview', 'нет превью')}</span>
          </div>
        )}
        <span aria-hidden className="absolute inset-x-0 bottom-0 h-12 bg-gradient-to-t from-black/55 to-transparent" />
        <span className="absolute top-2 left-2 inline-flex items-center px-2 h-5 rounded-md
                         bg-black/55 backdrop-blur-md text-white
                         text-[9px] font-bold uppercase tracking-[0.1em]">
          {badge}
        </span>
        {selected && (
          <motion.span
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: 'spring', stiffness: 420, damping: 26 }}
            className="absolute top-2 right-2 z-20 w-7 h-7 rounded-full bg-accent text-bg-primary
                       flex items-center justify-center
                       shadow-[0_0_0_3px_var(--bg-base),0_0_0_5px_color-mix(in_srgb,var(--accent)_45%,transparent),0_8px_22px_-4px_color-mix(in_srgb,var(--accent)_70%,transparent)]"
          >
            <Check size={13} strokeWidth={3} />
          </motion.span>
        )}
      </div>
      <div className="flex flex-col gap-0.5 p-3">
        <span className="text-[13px] font-bold text-text-primary truncate leading-tight">{title}</span>
        <span className="text-[10.5px] text-text-secondary truncate">{subtitle}</span>
      </div>
    </motion.button>

    <AnimatePresence>
      {videoOpen && hasVideo && (
        <VideoModal
          url={videoUrl!}
          title={title}
          onClose={() => setVideoOpen(false)}
        />
      )}
    </AnimatePresence>
    </>
  );
}

function SlotGunCard({
  displayName, category, previewUrl, slotState, sourcePackName, onClick, onPreview3D,
}: {
  displayName:    string;
  category:       string;
  previewUrl:     string | null;
  slotState:      GunSlotState | null;
  sourcePackName: string;
  onClick:        () => void;
  onPreview3D:    (() => void) | null;
}) {
  const { t } = useTranslation();
  const isVanilla  = slotState?.kind === 'vanilla';
  const isOverride = slotState?.kind === 'override';

  const stateText = isVanilla
    ? t('userBuilds.slotVanilla', 'Ванильная')
    : isOverride
      ? t('userBuilds.slotFromPack', 'Из {{name}}', { name: sourcePackName })
      : t('userBuilds.useDefault', 'По умолчанию');

  return (
    <motion.button
      type="button"
      onClick={onClick}
      layout
      whileTap={{ scale: 0.97 }}
      whileHover={{ y: -3 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}
      className={
        'group relative w-full flex flex-col rounded-3xl text-left overflow-hidden border ' +
        'bg-white/[0.04] backdrop-blur-xl ' +
        'transition-[border-color,box-shadow,background-color] duration-300 ease-smooth ' +
        (isOverride
          ? 'border-accent shadow-glow-accent'
          : 'border-white/[0.08] hover:bg-white/[0.06] hover:border-[color-mix(in_srgb,var(--accent)_45%,transparent)]')
      }
      style={{ outline: 'none', opacity: isVanilla ? 0.72 : 1 }}
    >
      <motion.div
        key={(previewUrl ?? '') + (slotState?.kind ?? 'default')}
        initial={{ scale: 0.94, opacity: 0.5 }}
        animate={{ scale: 1, opacity: 1 }}
        transition={{ type: 'spring', stiffness: 380, damping: 24 }}
        className="relative aspect-[4/3] w-full bg-glass overflow-hidden flex items-center justify-center"
      >
        {}
        {!isVanilla && (
          <span
            aria-hidden
            className="absolute inset-x-2 top-2 bottom-2 rounded-2xl pointer-events-none
                       opacity-55 group-hover:opacity-90
                       transition-opacity duration-500 ease-smooth"
            style={{
              background:
                'radial-gradient(ellipse at 50% 55%, color-mix(in srgb, var(--accent) 36%, transparent), transparent 72%)',
              filter: 'blur(18px)',
            }}
          />
        )}
        {previewUrl ? (
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
        <span aria-hidden className="absolute inset-x-0 bottom-0 h-12 bg-gradient-to-t from-black/45 to-transparent" />
        {}
        {(isOverride || isVanilla) && (
          <span
            className={
              'absolute top-2 left-2 inline-flex items-center gap-1 h-6 px-2 rounded-full ' +
              'text-[10px] font-bold uppercase tracking-[0.08em] backdrop-blur-md ' +
              (isOverride
                ? 'text-accent'
                : 'text-text-muted')
            }
            style={{
              background: isOverride
                ? 'color-mix(in srgb, var(--accent) 22%, rgba(0,0,0,0.45))'
                : 'rgba(0,0,0,0.55)',
              boxShadow: isOverride
                ? '0 0 0 1px color-mix(in srgb, var(--accent) 45%, transparent)'
                : '0 0 0 1px rgba(255,255,255,0.06)',
            }}
          >
            {stateText}
          </span>
        )}
        {}
        {onPreview3D && (
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onPreview3D(); }}
            className="absolute top-2 right-2 inline-flex items-center gap-1.5 h-8 px-2.5
                       rounded-full bg-black/55 backdrop-blur-md text-white
                       text-[10.5px] font-bold uppercase tracking-wider
                       opacity-0 group-hover:opacity-100 hover:bg-black/70
                       transition-opacity duration-150"
            style={{ outline: 'none' }}
            title={t('userBuilds.preview3D', '3D просмотр')}
          >
            <Eye size={12} strokeWidth={2.4} />
            3D
          </button>
        )}
        {}
        <span className="absolute bottom-2 left-2 inline-flex items-center gap-1 h-7 px-2.5
                         rounded-full bg-black/55 backdrop-blur-md text-white
                         text-[10px] font-bold uppercase tracking-wider
                         opacity-0 group-hover:opacity-100 transition-opacity duration-150">
          ↻ {' '}
          <span className="opacity-90">{t('userBuilds.replace', 'заменить')}</span>
        </span>
      </motion.div>

      <div className="flex flex-col gap-1 p-3">
        <span className="text-[13.5px] font-bold text-text-primary truncate leading-tight">{displayName}</span>
        <div className="flex items-center justify-between gap-2">
          <span className="text-[10px] uppercase tracking-wider text-text-muted truncate">{category}</span>
          {}
          {!isOverride && !isVanilla && (
            <span className="text-[10.5px] text-text-secondary truncate">
              {stateText}
            </span>
          )}
        </div>
      </div>
    </motion.button>
  );
}
