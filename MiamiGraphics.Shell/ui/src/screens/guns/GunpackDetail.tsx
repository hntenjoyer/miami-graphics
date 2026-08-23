import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ArrowLeft, ArrowRight, Download, CheckCircle2, AlertTriangle, Loader2, Trash2, Crosshair, X, Box, Check, Sparkles, Trophy } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import { motion, AnimatePresence, useMotionValue, useTransform, type Variants } from 'framer-motion';
import { useGunpackStore } from '@/store/gunpackStore';
import { useSubmitDraftStore } from '@/store/submitDraftStore';
import { useNavStore } from '@/store/navStore';
import { useAdminStore } from '@/store/adminStore';
import { bridge } from '@/bridge';
import { GunCard } from './GunCard';
import { Toast } from '@/components/Toast';
import { GlassPanel } from '@/design';
import { GlbViewerModal } from './GlbViewerModal';
import type { GunpackInstallConflict } from '@/bridge/types';

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
  packId?: string;
  onBack:  () => void;
  selectMode?: { label: string; onSelect: () => void };
}

export function GunpackDetail({ packId: forcedId, onBack, selectMode }: Props) {
  const { t } = useTranslation();
  const storeSelectedId = useGunpackStore(s => s.selectedId);
  const id = forcedId ?? storeSelectedId;
  const pack = useGunpackStore(s => s.selectedPack);
  const guns = useGunpackStore(s => s.selectedGuns);
  const variants = useGunpackStore(s => s.selectedVariants);
  const loading = useGunpackStore(s => s.loadingDetail);
  const selectPack = useGunpackStore(s => s.selectPack);

  const [selectedVariantId, setSelectedVariantId] = useState<string | null>(null);
  useEffect(() => {
    if (variants.length === 0) { setSelectedVariantId(null); return; }
    const def = variants.find(v => v.isDefault) ?? variants[0];
    setSelectedVariantId(def.id);
  }, [variants, pack?.id]);
  const selectedVariant = useMemo(
    () => variants.find(v => v.id === selectedVariantId) ?? null,
    [variants, selectedVariantId]);

  const installedGunpack = useGunpackStore(s => s.installedGunpack);
  const loadInstallState = useGunpackStore(s => s.loadInstallState);
  const isInstalled = !!pack && installedGunpack.activeGunpackId === pack.id;

  const [installing, setInstalling] = useState(false);
  const [uninstalling, setUninstalling] = useState(false);
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);

  const submitPickingFor = useSubmitDraftStore(s => s.pickingFor);
  const submitReturnTo   = useSubmitDraftStore(s => s.returnTo);
  const submitFinishPick = useSubmitDraftStore(s => s.finishPick);
  const reqNavigate = useNavStore(s => s.requestNavigate);
  const reqAdminSection = useAdminStore(s => s.requestSectionChange);
  const isPackPickMode = submitPickingFor === 'gunpack';
  const onUseInBuild = () => {
    if (!pack) return;
    submitFinishPick('gunpack', pack.id, pack.name);
    if (submitReturnTo === 'admin') {
      reqAdminSection('proPlayers');
      reqNavigate('admin');
    } else {
      reqNavigate('players');
    }
  };

  useEffect(() => {
    if (id) void selectPack(id);
  }, [id, selectPack]);

  const heroBg = useMemo(() => {
    if (selectedVariant?.coverUrl) return selectedVariant.coverUrl;
    if (!pack) return null;
    if (pack.coverKind === 'image' && pack.coverUrl) return pack.coverUrl;
    if (pack.coverKind === 'youtube' && pack.coverUrl) {
      const yid = extractYouTubeId(pack.coverUrl);
      if (yid) return `https://i.ytimg.com/vi/${yid}/maxresdefault.jpg`;
    }
    return null;
  }, [pack, selectedVariant]);

  const HERO_COLLAPSED = 150;
  const computeHeroFull = (count: number) => {
    const vh = typeof window !== 'undefined' ? window.innerHeight : 900;
    return count > 0 && count <= 6
      ? Math.min(Math.max(vh * 0.44, 280), 420)
      : Math.min(Math.max(vh * 0.30, 210), 300);
  };
  const [heroFull, setHeroFull] = useState(() => computeHeroFull(0));
  const scrollY = useMotionValue(0);
  const heroHeight = useTransform(scrollY, [0, Math.max(1, heroFull - HERO_COLLAPSED)], [heroFull, HERO_COLLAPSED]);

  const visibleGuns = useMemo(() => {
    const variantRoster = selectedVariant?.gunPreviews;
    if (!selectedVariant?.isDefault && variantRoster && Object.keys(variantRoster).length > 0) {
      return Object.entries(variantRoster).map(([baseName, vg]) => ({
        id:           `variant:${selectedVariant.id}:${baseName}`,
        gunpackId:    pack?.id ?? '',
        baseName,
        weaponPrefix: vg.weaponPrefix ?? '',
        category:     vg.category ?? '',
        displayName:  vg.displayName ?? baseName,
        glbUrl:       vg.glb ?? null,
        previewUrl:   vg.webp ?? null,
        files:        vg.files ?? [],
        sizeBytes:    vg.sizeBytes ?? 0,
        isHidden:     false,
        sortOrder:    vg.sortOrder ?? 0,
      })).sort((a, b) => a.sortOrder - b.sortOrder);
    }
    return guns.filter(g => !g.isHidden).sort((a, b) => a.sortOrder - b.sortOrder);
  }, [guns, selectedVariant, pack?.id]);

  useEffect(() => {
    const update = () => setHeroFull(computeHeroFull(visibleGuns.length));
    update();
    window.addEventListener('resize', update);
    return () => window.removeEventListener('resize', update);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visibleGuns.length]);

  const [conflicts, setConflicts] = useState<GunpackInstallConflict[]>([]);
  const [conflictModalOpen, setConflictModalOpen] = useState(false);
  const [resolutions, setResolutions] = useState<Record<string, 'pack' | 'selected'>>({});
  const [conflictIndex, setConflictIndex] = useState(0);
  const [viewerGlb, setViewerGlb] = useState<{ url: string; title: string } | null>(null);

  useEffect(() => {
    if (conflictModalOpen) setConflictIndex(0);
  }, [conflictModalOpen]);

  const runInstallWithResolutions = async (perGun: Record<string, string>) => {
    if (!pack) return;
    setConflictModalOpen(false);
    setInstalling(true);
    try {
      const res = await bridge.gunpackInstallAll(pack.id, perGun, selectedVariantId ?? undefined);
      if (res.success) {
        const hasVariant = !!selectedVariant && variants.length > 1;
        const variantSuffix = hasVariant ? ` (${selectedVariant!.name})` : '';
        setToast({
          tone: 'success',
          message: hasVariant
            ? t('guns.detail.installedToastVariant', { defaultValue: 'Ганпак «{{name}}» ({{variant}}) установлен.', name: pack.name, variant: selectedVariant!.name })
            : t('guns.detail.installedToast', { defaultValue: 'Ганпак «{{name}}» установлен.', name: pack.name }),
        });
        void bridge.activityLog('gunpack_install', `ганпак «${pack.name}»${variantSuffix}`);
        await loadInstallState();
      } else {
        setToast({ tone: 'error', message: res.errorMessage || t('guns.detail.installFailed', 'Не удалось установить ганпак.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setInstalling(false);
    }
  };

  const onInstall = async () => {
    if (!pack) return;
    setInstalling(true);
    try {
      const found = await bridge.gunpackCheckInstallConflicts(pack.id);
      if (found.length > 0) {
        setConflicts(found);
        setResolutions({});
        setConflictModalOpen(true);
        setInstalling(false);
        return;
      }
    } catch (e) {
      console.warn('[gunpack.install] conflict pre-check failed:', e);
    }
    setInstalling(false);
    void runInstallWithResolutions({});
  };

  const onUninstall = async () => {
    if (!pack) return;
    setUninstalling(true);
    try {
      const ok = await bridge.gunpackUninstall();
      if (ok) {
        setToast({ tone: 'success', message: t('guns.detail.uninstalledToast', { defaultValue: 'Ганпак «{{name}}» удалён.', name: pack.name }) });
        await loadInstallState();
      } else {
        setToast({ tone: 'error', message: t('guns.detail.uninstallFailed', 'Не удалось удалить ганпак.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setUninstalling(false);
    }
  };

  if (!id) return null;
  if (!pack && loading) {
    return (
      <div className="h-full flex items-center justify-center text-text-muted gap-2">
        <Loader2 size={20} className="animate-spin" />
        <span className="text-sm">{t('guns.detail.loadingPack', 'Загружаем пак…')}</span>
      </div>
    );
  }
  if (!pack) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-text-muted gap-2">
        <AlertTriangle size={36} className="opacity-40" />
        <p className="text-sm">{t('guns.detail.notFound', 'Пак не найден.')}</p>
        <button onClick={onBack} className="text-accent text-sm hover:underline">{t('guns.detail.backToCatalog', 'Назад в каталог')}</button>
      </div>
    );
  }

  return (
    <motion.div
      className="h-full flex flex-col"
      variants={detailContainer}
      initial="hidden"
      animate="visible"
    >
      <motion.div
        className="relative w-full overflow-hidden shrink-0 z-[6] bg-bg-base"
        style={{ height: heroHeight }}
        variants={detailItem}
      >
        {heroBg ? (
          <img
            key={heroBg}
            src={heroBg}
            alt=""
            className="absolute inset-0 w-full h-full object-cover select-none"
            draggable={false}
            onError={e => (e.currentTarget.style.display = 'none')}
            style={{
              maskImage:
                'linear-gradient(to right, transparent 0%, black 14%, black 86%, transparent 100%)',
              WebkitMaskImage:
                'linear-gradient(to right, transparent 0%, black 14%, black 86%, transparent 100%)',
            }}
          />
        ) : (
          <div className="absolute inset-0 bg-gradient-to-br from-bg-elevated to-bg-base" />
        )}
        <div
          aria-hidden
          style={{ height: '42%' }}
          className="absolute inset-x-0 bottom-0 pointer-events-none
                     bg-gradient-to-t from-bg-base via-bg-base/55 to-transparent"
        />

        <BackButton
          onClick={onBack}
          label={t('common.back')}
          className="absolute top-4 left-4 z-10"
        />

        <div className="absolute bottom-0 inset-x-0 px-8 pb-6 flex items-end gap-4">
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-1">
              {pack.isVerified && (
                <span className="px-1.5 py-0.5 rounded text-[10px] uppercase tracking-wider
                                 bg-accent/85 text-text-on-accent font-semibold inline-flex items-center gap-1">
                  <CheckCircle2 size={11} /> {t('redux.verified', 'Проверено')}
                </span>
              )}
              <span className="text-[10px] uppercase tracking-wider text-white/60">
                {t('guns.detail.gunsCount', { defaultValue: '{{count}} пушек', count: visibleGuns.length })} ·
                {' '}{t('guns.sizeMb', { defaultValue: '{{size}} МБ', size: (((selectedVariant && !selectedVariant.isDefault ? selectedVariant.weaponsRpfSize : pack.weaponsRpfSize) || 0) / (1024 * 1024)).toFixed(0) })}
              </span>
            </div>
            <h1 className="font-display text-3xl lg:text-4xl font-bold text-white uppercase tracking-wide leading-tight
                           drop-shadow-[0_2px_12px_rgba(0,0,0,0.7)]">
              {pack.name}
            </h1>
            {pack.author && (
              <div className="text-sm text-white/75 mt-1">{t('guns.byAuthor', { defaultValue: 'от {{author}}', author: pack.author })}</div>
            )}
          </div>

          <div className="flex flex-col items-end gap-2 shrink-0">
            {isPackPickMode && (
              <button
                type="button"
                onClick={onUseInBuild}
                className="inline-flex items-center gap-2 px-5 h-11 rounded-xl
                           bg-accent text-text-on-accent
                           border border-accent
                           hover:opacity-90
                           transition-opacity text-sm font-bold uppercase tracking-wider
                           shadow-[0_8px_28px_-12px_color-mix(in_srgb,var(--accent)_70%,transparent)]"
                style={{ outline: 'none' }}
              >
                <Trophy size={14} />
                <span>{t('guns.detail.useInBuild', 'Использовать в сборке')}</span>
              </button>
            )}
            {selectMode ? (
              <button
                type="button"
                onClick={selectMode.onSelect}
                className="inline-flex items-center gap-2 px-5 h-11 rounded-xl
                           bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                           hover:bg-bg-elevated/75 hover:border-white/[0.18]
                           transition-colors text-sm font-bold uppercase tracking-wider"
                style={{ outline: 'none' }}
              >
                <CheckCircle2 size={16} />
                <span>{selectMode.label}</span>
              </button>
            ) : isInstalled ? (
              <div className="flex items-center gap-2">
                <span className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl
                                 bg-green-500/20 text-green-300 text-sm font-medium">
                  <CheckCircle2 size={14} /> {t('guns.detail.installedBadge', 'Установлен')}
                </span>
                <button
                  type="button"
                  onClick={() => void onUninstall()}
                  disabled={uninstalling}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-red-500/15 text-red-300 hover:bg-red-500/25
                             border border-red-500/30 hover:border-red-500/50
                             disabled:opacity-60 transition-colors text-sm font-medium"
                >
                  {uninstalling ? <Loader2 size={14} className="animate-spin" /> : <Trash2 size={14} />}
                  <span>{uninstalling ? t('guns.detail.uninstalling', 'Удаляем…') : t('guns.detail.uninstall', 'Удалить')}</span>
                </button>
              </div>
            ) : (
              <button
                type="button"
                onClick={() => void onInstall()}
                disabled={installing}
                className="inline-flex items-center gap-2 px-5 h-11 rounded-xl
                           bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                           hover:bg-bg-elevated/75 hover:border-white/[0.18]
                           disabled:opacity-50 disabled:cursor-wait
                           transition-colors text-sm font-bold uppercase tracking-wider"
                style={{ outline: 'none' }}
              >
                {installing ? <Loader2 size={16} className="animate-spin" /> : <Download size={16} />}
                <span>{installing ? t('guns.detail.installing', 'Установка…') : t('guns.detail.install', 'Установить')}</span>
              </button>
            )}
          </div>
        </div>
      </motion.div>

      <div
        className="flex-1 min-h-0 overflow-y-auto [scrollbar-gutter:stable]"
        onScroll={(e) => scrollY.set(e.currentTarget.scrollTop)}
      >

      {variants.length > 1 && (
        <motion.section
          className="px-8 pt-4 pb-2 max-w-[1280px] mx-auto"
          variants={detailItem}
        >
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-[10px] uppercase tracking-[0.2em] text-text-muted font-bold mr-1">
              {t('guns.detail.variantLabel', 'Вариант')}
            </span>
            {variants.map(v => {
              const active = v.id === selectedVariantId;
              return (
                <button
                  key={v.id}
                  type="button"
                  onClick={() => setSelectedVariantId(v.id)}
                  className={
                    'inline-flex items-center gap-1.5 h-8 px-3 rounded-lg text-xs font-semibold uppercase tracking-wide ' +
                    'border transition-[background-color,border-color,color] duration-200 ease-depth ' +
                    (active
                      ? 'bg-accent-soft text-accent border-accent ' +
                        'shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_35%,transparent),0_6px_18px_-10px_color-mix(in_srgb,var(--accent)_60%,transparent)]'
                      : 'bg-white/[0.04] text-text-secondary border-white/[0.08] ' +
                        'hover:text-text-primary hover:bg-white/[0.08] hover:border-white/[0.18]')
                  }
                >
                  <span>{v.name}</span>
                </button>
              );
            })}
          </div>
        </motion.section>
      )}

      {pack.description && (
        <motion.section className="px-8 py-5 max-w-[1280px] mx-auto" variants={detailItem}>
          <p className="text-sm text-text-secondary whitespace-pre-line">{pack.description}</p>
        </motion.section>
      )}

      <motion.section className="px-8 pt-3 pb-8" variants={detailItem}>
        <div className="flex items-center gap-2 mb-3">
          <span className="text-[11px] uppercase tracking-[0.22em] text-text-muted font-bold">
            {t('guns.detail.selectiveTitle', 'Выборочная установка в паке')}
          </span>
          {visibleGuns.length > 0 && (
            <span className="inline-flex items-center px-1.5 py-0.5 rounded-md
                             bg-accent-soft text-accent
                             text-[10px] font-bold tabular-nums">
              {visibleGuns.length}
            </span>
          )}
        </div>
        {visibleGuns.length === 0 ? (
          <div className="py-12 text-center text-text-muted text-sm">
            {t('guns.detail.emptyPack', 'В этом паке пока нет пушек. Возможно админ перезаливает контент.')}
          </div>
        ) : (
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-3">
            {visibleGuns.map((g, i) => (
              <GunCard
                key={g.id}
                baseName={g.baseName}
                displayName={g.displayName}
                category={g.category}
                weaponPrefix={g.weaponPrefix}
                glbUrl={g.glbUrl}
                previewUrl={g.previewUrl}
                index={i}
              />
            ))}
          </div>
        )}
      </motion.section>
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 4000}
      />

      {conflictModalOpen && pack && conflicts.length > 0 && (() => {
        const isMulti        = conflicts.length > 1;
        const total          = conflicts.length;
        const idx            = Math.min(conflictIndex, total - 1);
        const current        = conflicts[idx];
        const currentChoice  = resolutions[current.internalName] ?? null;
        const decidedCount   = conflicts.filter(c => !!resolutions[c.internalName]).length;
        const allDecided     = decidedCount === total;
        const remainingUndecided = total - decidedCount;
        const isLast         = idx === total - 1;

        const applyToAllRemaining = (side: 'pack' | 'selected') => {
          setResolutions(r => {
            const next = { ...r };
            for (const c of conflicts) {
              if (!next[c.internalName]) next[c.internalName] = side;
            }
            return next;
          });
        };

        const goNext = () => {
          if (idx < total - 1) setConflictIndex(idx + 1);
        };
        const goPrev = () => {
          if (idx > 0) setConflictIndex(idx - 1);
        };

        const onPick = (side: 'pack' | 'selected') => {
          setResolutions(r => ({ ...r, [current.internalName]: side }));
        };

        const triggerInstall = () => {
          const map: Record<string, string> = {};
          for (const c of conflicts) {
            map[c.internalName] = resolutions[c.internalName] ?? 'selected';
          }
          void runInstallWithResolutions(map);
        };

        return (
          <div className="fixed inset-0 z-50 bg-black/65 backdrop-blur-md flex items-center justify-center p-6">
            <GlassPanel
              depth="z3"
              tint="ultra"
              rounded="3xl"
              highlight
              edge
              className="relative z-10 w-full max-w-[760px] flex flex-col max-h-[92vh] overflow-hidden border border-white/[0.08]"
            >
              <span
                aria-hidden="true"
                className="absolute top-0 inset-x-0 h-px pointer-events-none
                           bg-gradient-to-r from-transparent via-white/45 to-transparent"
              />
              <span
                aria-hidden="true"
                className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl"
                style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--status-warning) 22%, transparent) 0%, transparent 70%)' }}
              />

              <button
                type="button"
                onClick={() => setConflictModalOpen(false)}
                aria-label={t('common.close')}
                style={{ outline: 'none' }}
                className="absolute top-4 right-4 z-10 w-8 h-8 rounded-lg flex items-center justify-center
                           text-text-muted hover:text-text-primary hover:bg-glass-strong transition-colors"
              >
                <X size={16} />
              </button>

              <div className="relative px-8 pt-8 pb-2 flex flex-col items-center text-center">
                <div className="relative mb-4 w-14 h-14">
                  <motion.span
                    aria-hidden="true"
                    className="absolute inset-0 rounded-2xl blur-md"
                    style={{ background: 'radial-gradient(circle, var(--status-warning) 0%, transparent 70%)' }}
                    initial={{ opacity: 0.3, scale: 0.85 }}
                    animate={{ opacity: [0.3, 0.55, 0.3], scale: [0.85, 1.1, 0.85] }}
                    transition={{ duration: 2.8, ease: 'easeInOut', repeat: Infinity }}
                  />
                  <div
                    className="relative w-14 h-14 rounded-2xl border border-white/[0.08]
                               flex items-center justify-center"
                    style={{ background: 'color-mix(in srgb, var(--status-warning) 18%, transparent)' }}
                  >
                    <AlertTriangle size={24} style={{ color: 'var(--status-warning)' }} strokeWidth={2} />
                  </div>
                </div>
                <h2 className="font-display text-[23px] font-bold text-text-primary tracking-tight leading-tight">
                  {isMulti
                    ? t('guns.conflict.titleMulti', { defaultValue: 'Конфликты выбора · {{total}}', total })
                    : t('guns.conflict.title', 'Конфликт выбора')}
                </h2>
                <p className="mt-2 text-[13.5px] leading-relaxed text-text-secondary max-w-[520px]">
                  {isMulti
                    ? t('guns.conflict.subtitleMulti', 'Несколько пушек уже выбраны из других паков. Пройди по очереди и выбери, какую версию оставить - или применишь решение ко всем сразу.')
                    : t('guns.conflict.subtitle', 'Этот ган-пак включает пушку, уже выбранную из другого пака. Выбери, какую версию оставить.')}
                </p>
              </div>

              {isMulti && (
                <div className="px-6 pt-4 pb-2 flex items-center gap-3">
                  <div className="flex items-center gap-1.5">
                    {conflicts.map((c, i) => {
                      const decided = !!resolutions[c.internalName];
                      const isCurrent = i === idx;
                      return (
                        <button
                          key={c.internalName}
                          type="button"
                          onClick={() => setConflictIndex(i)}
                          title={`${i + 1}. ${c.displayName}${decided ? ' ✓' : ''}`}
                          aria-label={t('guns.conflict.stepAria', { defaultValue: 'Шаг {{n}}: {{name}}', n: i + 1, name: c.displayName })}
                          style={{ outline: 'none' }}
                          className={
                            'transition-all duration-300 ease-depth rounded-full ' +
                            (isCurrent
                              ? 'w-7 h-2 ' + (decided ? 'bg-accent shadow-glow-accent' : 'bg-accent/55 ring-2 ring-accent/35')
                              : 'w-2 h-2 ' + (decided ? 'bg-accent/85 hover:bg-accent' : 'bg-glass-border hover:bg-text-muted'))
                          }
                        />
                      );
                    })}
                  </div>
                  <span className="text-[10px] uppercase tracking-[0.18em] text-text-muted ml-auto tabular-nums">
                    {t('common.stepOf', { current: idx + 1, total })}
                  </span>
                  {decidedCount > 0 && (
                    <span className="text-[10px] uppercase tracking-[0.18em] font-bold text-accent tabular-nums">
                      <Check size={10} className="inline -mt-0.5 mr-0.5" />
                      {decidedCount}/{total}
                    </span>
                  )}
                </div>
              )}

              <div className="flex-1 px-6 py-4 overflow-y-auto">
                <AnimatePresence mode="wait" initial={false}>
                  <motion.div
                    key={current.internalName}
                    initial={isMulti ? { opacity: 0, x: 18 } : { opacity: 1 }}
                    animate={{ opacity: 1, x: 0 }}
                    exit={isMulti ? { opacity: 0, x: -18 } : { opacity: 0 }}
                    transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}
                  >
                    <div className="flex items-center justify-between gap-2 mb-3 px-0.5">
                      <span className="font-display font-bold text-sm uppercase tracking-wide text-text-primary truncate">
                        {current.displayName}
                      </span>
                      {currentChoice ? (
                        <span className="inline-flex items-center gap-1 text-[10px] uppercase tracking-[0.18em] text-accent font-bold">
                          <Check size={11} />
                          {currentChoice === 'pack'
                            ? t('guns.conflict.pickedPack', 'выбрано: новая')
                            : t('guns.conflict.pickedSelected', 'выбрано: текущая')}
                        </span>
                      ) : (
                        <span className="text-[10px] uppercase tracking-[0.18em] text-text-muted">
                          {t('guns.conflict.notPicked', 'не выбрано')}
                        </span>
                      )}
                    </div>
                    <div className="grid grid-cols-2 gap-3">
                      <ConflictTile
                        side="pack"
                        active={currentChoice === 'pack'}
                        previewUrl={current.gunpackPreviewUrl}
                        glbUrl={current.gunpackGlbUrl}
                        label={t('guns.conflict.fromThisPack', 'Из этого пака')}
                        subLabel={current.gunpackPackName}
                        onPick={() => onPick('pack')}
                        onView3D={current.gunpackGlbUrl
                          ? () => setViewerGlb({ url: current.gunpackGlbUrl!, title: `${current.displayName} · ${current.gunpackPackName}` })
                          : undefined}
                      />
                      <ConflictTile
                        side="selected"
                        active={currentChoice === 'selected'}
                        previewUrl={current.selectedPreviewUrl}
                        glbUrl={current.selectedGlbUrl}
                        label={current.selectedFromPackId === '_custom'
                          ? t('guns.conflict.yourSkin', 'Твоя раскраска')
                          : t('guns.conflict.yourCurrentPick', 'Твой текущий выбор')}
                        subLabel={current.selectedFromPackName}
                        onPick={() => onPick('selected')}
                        onView3D={current.selectedGlbUrl
                          ? () => setViewerGlb({ url: current.selectedGlbUrl!, title: `${current.displayName} · ${current.selectedFromPackName}` })
                          : undefined}
                      />
                    </div>
                  </motion.div>
                </AnimatePresence>

                <AnimatePresence initial={false}>
                  {isMulti && currentChoice && remainingUndecided > 1 && (
                    <motion.div
                      key="apply-to-all"
                      initial={{ opacity: 0, height: 0, marginTop: 0 }}
                      animate={{ opacity: 1, height: 'auto', marginTop: 16 }}
                      exit={{ opacity: 0, height: 0, marginTop: 0 }}
                      transition={{ duration: 0.32, ease: [0.22, 1, 0.36, 1] }}
                      style={{ overflow: 'hidden' }}
                    >
                      <button
                        type="button"
                        onClick={() => applyToAllRemaining(currentChoice)}
                        style={{ outline: 'none' }}
                        className="w-full inline-flex items-center justify-center gap-2.5 px-4 py-2.5 rounded-xl
                                   bg-[color-mix(in_srgb,var(--accent)_12%,transparent)]
                                   border border-[color-mix(in_srgb,var(--accent)_28%,transparent)]
                                   hover:bg-[color-mix(in_srgb,var(--accent)_18%,transparent)]
                                   hover:border-[color-mix(in_srgb,var(--accent)_50%,transparent)]
                                   text-sm font-semibold text-text-primary
                                   transition-[background-color,border-color] duration-200 ease-depth"
                      >
                        <Sparkles size={13} className="text-accent" />
                        <span>
                          {currentChoice === 'pack'
                            ? t('guns.conflict.applyAllPack', 'Применить «из этого пака» ко всем оставшимся')
                            : t('guns.conflict.applyAllSelected', 'Применить «текущий выбор» ко всем оставшимся')}
                        </span>
                        <span className="inline-flex items-center justify-center min-w-[22px] h-[22px] px-1.5 rounded-md
                                         bg-accent/20 text-accent text-[11px] font-bold tabular-nums">
                          {remainingUndecided - 1}
                        </span>
                      </button>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>

              <div className="relative px-6 py-4 flex items-center justify-between gap-3">
                <button
                  type="button"
                  onClick={() => setConflictModalOpen(false)}
                  className="px-4 py-2 text-sm font-semibold text-text-muted hover:text-text-primary transition-colors"
                >
                  {t('common.cancel')}
                </button>

                <div className="flex items-center gap-2">
                  {isMulti && (
                    <>
                      <button
                        type="button"
                        onClick={goPrev}
                        disabled={idx === 0}
                        aria-label={t('common.back')}
                        style={{ outline: 'none' }}
                        className="inline-flex items-center gap-1.5 h-10 px-3 rounded-xl text-sm font-semibold
                                   bg-glass-strong border border-glass-border text-text-secondary
                                   hover:text-text-primary hover:border-text-secondary/40
                                   disabled:opacity-30 disabled:cursor-not-allowed
                                   transition-colors"
                      >
                        <ArrowLeft size={14} />
                        <span className="hidden sm:inline">{t('common.back')}</span>
                      </button>
                      <button
                        type="button"
                        onClick={goNext}
                        disabled={isLast || !currentChoice}
                        aria-label={t('common.next')}
                        title={!currentChoice ? t('common.chooseVersionFirst', 'Сначала выбери версию') : undefined}
                        style={{ outline: 'none' }}
                        className="inline-flex items-center gap-1.5 h-10 px-3 rounded-xl text-sm font-semibold
                                   bg-glass-strong border border-glass-border text-text-secondary
                                   hover:text-text-primary hover:border-accent/50
                                   disabled:opacity-30 disabled:cursor-not-allowed
                                   transition-colors"
                      >
                        <span className="hidden sm:inline">{t('common.next')}</span>
                        <ArrowRight size={14} />
                      </button>
                    </>
                  )}

                  <button
                    type="button"
                    onClick={triggerInstall}
                    disabled={!allDecided}
                    title={!allDecided ? t('guns.conflict.remainingTitle', { defaultValue: 'Осталось выбрать: {{n}}', n: remainingUndecided }) : undefined}
                    style={{ outline: 'none' }}
                    className="inline-flex items-center gap-2.5 pl-5 pr-4 h-11 rounded-2xl text-sm font-display font-bold uppercase tracking-[0.06em]
                               bg-[color-mix(in_srgb,var(--accent-soft)_55%,transparent)]
                               text-text-primary
                               border border-[color-mix(in_srgb,var(--accent)_45%,transparent)]
                               hover:border-[color-mix(in_srgb,var(--accent)_80%,transparent)]
                               shadow-[0_8px_32px_-12px_color-mix(in_srgb,var(--accent)_50%,transparent)]
                               disabled:opacity-50 disabled:cursor-not-allowed
                               transition-[border-color,box-shadow] duration-300 ease-smooth"
                  >
                    <span>{allDecided
                      ? t('guns.detail.install', 'Установить')
                      : t('guns.conflict.remaining', { defaultValue: 'Осталось: {{n}}', n: remainingUndecided })}</span>
                    {allDecided && (
                      <span className="w-7 h-7 rounded-lg flex items-center justify-center
                                       bg-[color-mix(in_srgb,var(--accent)_22%,transparent)]
                                       border border-[color-mix(in_srgb,var(--accent)_30%,transparent)]">
                        <Download size={13} className="text-accent" />
                      </span>
                    )}
                  </button>
                </div>
              </div>
            </GlassPanel>

            <AnimatePresence>
              {viewerGlb && (
                <GlbViewerModal
                  glbUrl={viewerGlb.url}
                  title={viewerGlb.title}
                  onClose={() => setViewerGlb(null)}
                />
              )}
            </AnimatePresence>
          </div>
        );
      })()}
    </motion.div>
  );
}

function ConflictTile({
  side: _side, active, previewUrl, glbUrl, label, subLabel, onPick, onView3D,
}: {
  side: 'pack' | 'selected';
  active: boolean;
  previewUrl: string | null;
  glbUrl: string | null;
  label: string;
  subLabel: string;
  onPick: () => void;
  onView3D?: () => void;
}) {
  const { t } = useTranslation();
  return (
    <button
      type="button"
      onClick={onPick}
      className={
        'relative group aspect-square rounded-xl overflow-hidden text-left bg-transparent ' +
        'transition-[transform,box-shadow,border-color,background-color] duration-200 ease-depth ' +
        (active
          ? 'border-2 border-accent shadow-glow-accent ring-2 ring-accent/40 bg-[color-mix(in_srgb,var(--accent)_5%,transparent)]'
          : 'border border-glass-border hover:border-accent/55 hover:shadow-z2 hover:-translate-y-0.5 hover:bg-glass/40')
      }
    >
      <div className="absolute inset-0 bottom-[34%] bg-gradient-to-b from-black/15 to-transparent pointer-events-none" />

      {previewUrl ? (
        <img
          src={previewUrl}
          alt=""
          draggable={false}
          className="absolute inset-0 w-full h-full object-contain p-3 select-none
                     transition-transform duration-500 ease-smooth
                     group-hover:scale-[1.03]"
          onError={e => (e.currentTarget.style.display = 'none')}
        />
      ) : (
        <div className="absolute inset-0 flex items-center justify-center">
          <Crosshair size={32} className="text-text-muted opacity-30" />
        </div>
      )}

      {active && (
        <div className="absolute top-2 left-2 inline-flex items-center justify-center w-6 h-6 rounded-full
                        bg-accent text-text-on-accent shadow-glow-accent">
          <Check size={13} strokeWidth={3} />
        </div>
      )}

      {glbUrl && onView3D && (
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); onView3D(); }}
          aria-label={t('guns.view3d', '3D-просмотр')}
          title={t('guns.view3d', '3D-просмотр')}
          className="absolute top-2 right-2 w-7 h-7 rounded-md flex items-center justify-center
                     bg-black/55 backdrop-blur-md text-white
                     hover:bg-accent transition-colors"
        >
          <Box size={12} />
        </button>
      )}

      <div className="absolute bottom-0 inset-x-0 px-3 py-2 bg-gradient-to-t from-black/90 via-black/55 to-transparent">
        <div className="text-[9px] uppercase tracking-[0.18em] text-white/70 font-bold">{label}</div>
        <div className="text-xs font-semibold text-white truncate" title={subLabel}>{subLabel}</div>
      </div>
    </button>
  );
}
