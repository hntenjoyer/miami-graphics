import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Crosshair, Loader2, Check, Download, Trash2, CheckCircle2, Search, X, Bookmark, Trophy } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import { AnimatePresence, motion, useMotionValue, useTransform } from 'framer-motion';
import { useGunpackStore } from '@/store/gunpackStore';
import type { FlatGun } from '@/store/gunpackStore';
import { useSubmitDraftStore } from '@/store/submitDraftStore';
import { useNavStore } from '@/store/navStore';
import { useAdminStore } from '@/store/adminStore';
import { bridge } from '@/bridge';
import { GlbViewerModal } from './GlbViewerModal';
import { Toast } from '@/components/Toast';
import { ConfirmModal } from '@/components/ConfirmModal';

const CATEGORY_LABEL: Record<string, { key: string; ru: string }> = {
  assault: { key: 'guns.category.assault', ru: 'Штурмовая' },
  shotgun: { key: 'guns.category.shotgun', ru: 'Дробовик' },
  sniper:  { key: 'guns.category.sniper',  ru: 'Снайперская' },
  mg:      { key: 'guns.category.mg',      ru: 'Пулемёт' },
  pistol:  { key: 'guns.category.pistol',  ru: 'Пистолет' },
  smg:     { key: 'guns.category.smg',     ru: 'ПП' },
};

interface Props {
  internalName: string;
  onBack:       () => void;
  onOpenPack:   (id: string) => void;
}

export function WhitelistGunDetail({ internalName, onBack, onOpenPack }: Props) {
  const { t } = useTranslation();
  const whitelist             = useGunpackStore(s => s.whitelist);
  const allGuns               = useGunpackStore(s => s.allGuns);
  const loadingAllGuns        = useGunpackStore(s => s.loadingAllGuns);
  const loadAllGuns           = useGunpackStore(s => s.loadAllGuns);
  const installedSelectedGuns = useGunpackStore(s => s.installedSelectedGuns);
  const installedGunpack      = useGunpackStore(s => s.installedGunpack);
  const loadInstallState      = useGunpackStore(s => s.loadInstallState);

  const [selectedModKey, setSelectedModKey] = useState<string | null>(null);
  const [viewerGun, setViewerGun] = useState<FlatGun | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error' | 'info'; message: string } | null>(null);
  const [installing, setInstalling] = useState(false);
  const [removing, setRemoving] = useState(false);

  const [confirmReplace, setConfirmReplace] = useState(false);

  useEffect(() => { void loadAllGuns(); }, [loadAllGuns]);

  const entry = useMemo(
    () => whitelist.find(w => w.internalName === internalName) ?? null,
    [whitelist, internalName]);

  const mods = useMemo(
    () => {
      const norm = internalName.toLowerCase();
      return allGuns.filter(g =>
        (g.weaponPrefix + g.baseName).toLowerCase() === norm);
    },
    [allGuns, internalName]);

  const selectedMod = useMemo<FlatGun | null>(() => {
    if (!selectedModKey) return null;
    return mods.find(m => `${m.packId}::${m.gunId}` === selectedModKey) ?? null;
  }, [mods, selectedModKey]);

  useEffect(() => {
    if (selectedModKey && !selectedMod) setSelectedModKey(null);
  }, [selectedModKey, selectedMod]);

  const installedRecord = useMemo(
    () => installedSelectedGuns.find(g => g.internalName === internalName) ?? null,
    [installedSelectedGuns, internalName]);

  const [search, setSearch] = useState('');
  const [onlyMyPacks, setOnlyMyPacks] = useState(false);
  const myPackIds = useMemo(() => {
    const s = new Set<string>();
    for (const g of installedSelectedGuns) {
      if (g.gunpackId) s.add(g.gunpackId);
    }

    if (installedGunpack.activeGunpackId) {
      s.add(installedGunpack.activeGunpackId);
    }
    return s;
  }, [installedSelectedGuns, installedGunpack.activeGunpackId]);

  const myPackMatchCount = useMemo(
    () => mods.filter(m => myPackIds.has(m.packId)).length,
    [mods, myPackIds]);

  const visibleMods = useMemo(() => {
    let out = mods;
    if (onlyMyPacks) out = out.filter(m => myPackIds.has(m.packId));
    const q = search.trim().toLowerCase();
    if (q) {
      out = out.filter(m =>
        m.packName.toLowerCase().includes(q)
        || (m.packAuthor ?? '').toLowerCase().includes(q)
        || m.baseName.toLowerCase().includes(q),
      );
    }
    return out;
  }, [mods, onlyMyPacks, myPackIds, search]);

  const fullPackOccupant = useMemo<FlatGun | null>(() => {
    if (!installedGunpack.activeGunpackId) return null;
    return mods.find(m => m.packId === installedGunpack.activeGunpackId) ?? null;
  }, [mods, installedGunpack.activeGunpackId]);

  const installedKey = installedRecord
    ? `${installedRecord.gunpackId}::${installedRecord.gunId}`
    : fullPackOccupant
      ? `${fullPackOccupant.packId}::${fullPackOccupant.gunId}`
      : null;

  const currentSource = useMemo(() => {
    if (installedRecord) {
      return {
        kind: 'selected' as const,
        packName: installedRecord.gunpackName,
        key: `${installedRecord.gunpackId}::${installedRecord.gunId}`,
      };
    }
    if (fullPackOccupant) {
      return {
        kind: 'pack' as const,
        packName: installedGunpack.activeGunpackName ?? fullPackOccupant.packName,
        key: `${fullPackOccupant.packId}::${fullPackOccupant.gunId}`,
      };
    }
    return null;
  }, [installedRecord, fullPackOccupant, installedGunpack.activeGunpackName]);

  useEffect(() => {
    if (!installedKey) return;
    setSelectedModKey(prev => prev ?? installedKey);

  }, [internalName]);

  if (!entry) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-text-muted gap-2">
        <Crosshair size={48} className="opacity-30" />
        <p className="text-sm">{t('guns.gunDetail.notFound', 'Пушка не найдена в whitelist.')}</p>
        <button onClick={onBack} className="text-accent text-sm hover:underline">{t('common.back')}</button>
      </div>
    );
  }

  const categoryEntry = CATEGORY_LABEL[entry.category];
  const categoryLabel = categoryEntry ? t(categoryEntry.key, categoryEntry.ru) : entry.category;
  const heroPreviewUrl = selectedMod?.previewUrl ?? entry.previewUrl;

  const heroViewerTarget = selectedMod?.glbUrl ? selectedMod : null;

  const runInstall = async () => {
    if (!selectedMod) return;
    setInstalling(true);
    try {

      const res = await bridge.selectedGunsInstall(selectedMod.packId, internalName);
      if (res.success) {
        void bridge.activityLog('gun_install', `пушку «${selectedMod.packName}»`);
        setToast({ tone: 'success', message: t('guns.gunDetail.installedToast', { defaultValue: '«{{name}}» установлен.', name: selectedMod.packName }) });
        await loadInstallState();
      } else {
        setToast({ tone: 'error', message: res.errorMessage || t('guns.gunDetail.installFailed', 'Не удалось установить мод.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setInstalling(false);
    }
  };

  const onInstall = () => {
    if (!selectedMod) {
      setToast({
        tone: 'info',
        message: t('guns.detail.defaultSelectedInfo', 'Выбран Стандарт - это уже ванильное оружие, ничего ставить не нужно. Выбери мод одним кликом и нажми «Установить».'),
      });
      return;
    }
    const newKey = `${selectedMod.packId}::${selectedMod.gunId}`;
    if (currentSource && currentSource.key !== newKey) {
      setConfirmReplace(true);
      return;
    }
    void runInstall();
  };

  const submitPickingFor = useSubmitDraftStore(s => s.pickingFor);
  const submitReturnTo   = useSubmitDraftStore(s => s.returnTo);
  const submitFinishPick = useSubmitDraftStore(s => s.finishPick);
  const reqNavigate      = useNavStore(s => s.requestNavigate);
  const reqAdminSection  = useAdminStore(s => s.requestSectionChange);
  const isPackPickMode   = submitPickingFor === 'gunpack';
  const onUseInBuild = () => {
    if (!selectedMod) {
      setToast({
        tone: 'info',
        message: t('guns.detail.pickVariantForBuildInfo', 'Сначала выбери конкретный вариант (не «Стандарт») - его родительский ган-пак запомнится для сборки.'),
      });
      return;
    }
    submitFinishPick('gunpack', selectedMod.packId, selectedMod.packName);
    if (submitReturnTo === 'admin') {
      reqAdminSection('proPlayers');
      reqNavigate('admin');
    } else {
      reqNavigate('players');
    }
  };

  const onRemove = async () => {
    setRemoving(true);
    try {

      const res = await bridge.selectedGunsRemove(internalName);
      if (res.success) {
        setToast({ tone: 'success', message: t('guns.detail.removedToast', 'Мод удалён, активна стандартная пушка.') });
        await loadInstallState();

        setSelectedModKey(null);
      } else {
        setToast({ tone: 'error', message: res.errorMessage || t('guns.detail.removeFailed', 'Не удалось удалить мод.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setRemoving(false);
    }
  };

  const heroSubtitle = selectedMod
    ? `${selectedMod.packName}${selectedMod.packAuthor ? ` · ${selectedMod.packAuthor}` : ''}`
    : t('guns.detail.default', 'Стандарт');

  const heroScrollY = useMotionValue(0);
  const heroImgHeight = useTransform(heroScrollY, [0, 220], [410, 210]);

  return (
    <div className="h-full flex flex-col">
      {}
      <div className="shrink-0 px-8 pt-4 pb-3">
        <div className="max-w-[1280px] mx-auto flex flex-col gap-3">
          <BackButton onClick={onBack} label={t('common.back')} className="self-start" />

          {}
          <div className="relative">
            <motion.div
              className="w-full flex items-center justify-center overflow-hidden"
              style={{ height: heroImgHeight }}
            >
              {heroPreviewUrl ? (
                <img
                  key={heroPreviewUrl}
                  src={heroPreviewUrl}
                  alt=""
                  draggable={false}
                  className="w-full h-full object-contain select-none
                             transition-opacity duration-300 animate-[fadeIn_0.3s_ease-out]"
                  onError={e => (e.currentTarget.style.display = 'none')}
                />
              ) : (
                <Crosshair size={72} className="text-text-muted opacity-30" />
              )}
            </motion.div>

          {}
          {heroViewerTarget && (
            <div className="absolute top-2 right-2">
              <button
                type="button"
                onClick={() => setViewerGun(heroViewerTarget)}
                title={t('guns.detail.view3d', '3D-просмотр')}
                style={{ outline: 'none' }}
                className="inline-flex items-center gap-1.5 px-3 h-9 rounded-xl
                           bg-black/55 backdrop-blur-md text-white
                           border border-white/20
                           shadow-[0_6px_14px_-6px_rgba(0,0,0,0.55)]
                           hover:bg-white/[0.18] hover:border-white/40
                           transition-colors duration-200"
              >
                <Box size={14} />
                <span className="text-xs uppercase tracking-wider font-semibold">3D</span>
              </button>
            </div>
          )}
        </div>

        {}
        <div className="flex items-end justify-between gap-4 flex-wrap">
          <div className="min-w-0">
            <h1 className="font-display text-2xl font-bold text-text-primary uppercase tracking-tight truncate">
              {entry.displayName}
            </h1>
            <div className="text-xs uppercase tracking-wider text-text-muted mt-0.5 truncate">
              {categoryLabel} · <span className="text-text-secondary normal-case tracking-normal font-medium">{heroSubtitle}</span>
            </div>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            {}
            {isPackPickMode && (
              <button
                type="button"
                onClick={onUseInBuild}
                disabled={!selectedMod}
                title={selectedMod
                  ? t('guns.detail.useInBuildTitle', { defaultValue: 'Использовать ган-пак «{{name}}» в сборке', name: selectedMod.packName })
                  : t('guns.detail.pickVariantFirst', 'Сначала выбери вариант')}
                className="inline-flex items-center gap-2 px-4 h-10 rounded-xl
                           bg-accent text-text-on-accent
                           border border-accent
                           hover:opacity-90
                           disabled:opacity-50 disabled:cursor-not-allowed
                           transition-opacity text-xs uppercase tracking-wider font-bold
                           shadow-[0_8px_28px_-12px_color-mix(in_srgb,var(--accent)_70%,transparent)]"
                style={{ outline: 'none' }}
              >
                <Trophy size={14} />
                <span>{t('guns.detail.useInBuild', 'Использовать в сборке')}</span>
              </button>
            )}
            {}
            {selectedMod
              && selectedModKey !== installedKey
              && selectedModKey !== (currentSource?.key ?? null)
              && (
              <button
                type="button"
                onClick={() => void onInstall()}
                disabled={installing}
                title={t('guns.detail.installNamedTitle', { defaultValue: 'Установить «{{name}}»', name: selectedMod.packName })}
                className="inline-flex items-center gap-2 px-4 h-10 rounded-xl
                           bg-accent-soft text-text-primary
                           border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                           hover:bg-[color-mix(in_srgb,var(--accent)_20%,transparent)] hover:border-accent
                           disabled:opacity-50 disabled:cursor-wait
                           transition-colors text-xs uppercase tracking-wider font-bold"
                style={{ outline: 'none' }}
              >
                {installing ? <Loader2 size={14} className="animate-spin" /> : <Download size={14} />}
                <span>
                  {installing
                    ? t('guns.gunDetail.installing', 'Ставим…')
                    : (currentSource ? t('guns.detail.change', 'Сменить') : t('guns.detail.install', 'Установить'))}
                </span>
              </button>
            )}
            {}
            {installedRecord && (
              <>
                {selectedModKey === installedKey && (
                  <span className="inline-flex items-center gap-1.5 px-3 h-10 rounded-xl
                                   bg-green-500/20 text-green-300 text-xs uppercase tracking-wider font-semibold">
                    <CheckCircle2 size={12} /> {t('guns.detail.installedPill', 'Установлено')}
                  </span>
                )}
                <button
                  type="button"
                  onClick={() => void onRemove()}
                  disabled={removing}
                  title={t('guns.detail.removeTitle', 'Снять мод и вернуть стандартную пушку')}
                  style={{ outline: 'none' }}
                  className="inline-flex items-center gap-2 px-3 h-10 rounded-xl
                             bg-red-500/15 text-red-300 hover:bg-red-500/25
                             border border-red-500/30 hover:border-red-500/50
                             disabled:opacity-60 transition-colors"
                >
                  {removing ? <Loader2 size={12} className="animate-spin" /> : <Trash2 size={12} />}
                  <span className="text-xs uppercase tracking-wider font-semibold">
                    {removing ? t('guns.detail.removing', 'Удаляем…') : t('guns.detail.remove', 'Удалить')}
                  </span>
                </button>
              </>
            )}
            {}
            {!installedRecord && !selectedMod && (
              <button
                type="button"
                disabled
                title={t('guns.detail.pickModToInstall', 'Выбери мод чтобы установить')}
                className="inline-flex items-center gap-2 px-4 h-10 rounded-xl
                           bg-accent/40 text-text-on-accent/70 font-medium
                           opacity-50 cursor-not-allowed"
              >
                <Download size={14} />
                <span className="text-xs uppercase tracking-wider font-semibold">{t('guns.detail.install', 'Установить')}</span>
              </button>
            )}
          </div>
        </div>

        {}
        <div className="flex items-center gap-2 flex-wrap">
          <label
            className="group relative flex items-center w-[280px] max-w-full h-9 rounded-xl
                       bg-white/[0.04] border border-white/[0.06]
                       transition-[background-color,border-color] duration-300 ease-smooth
                       hover:bg-white/[0.07] hover:border-white/[0.12]
                       focus-within:bg-white/[0.07] focus-within:border-white/[0.30]"
          >
            <Search size={13} className="absolute left-3 text-text-muted pointer-events-none" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('guns.detail.searchPlaceholder', 'Поиск по паку, автору, имени')}
              className="w-full h-full pl-9 pr-8 bg-transparent rounded-xl
                         text-[12.5px] text-text-primary placeholder:text-text-muted
                         outline-none"
            />
            {search && (
              <button
                type="button"
                onClick={() => setSearch('')}
                className="absolute right-1 w-6 h-6 rounded-md flex items-center justify-center
                           text-text-muted hover:text-text-primary hover:bg-white/[0.10]
                           transition-colors"
                aria-label={t('common.clear', 'Очистить')}
                style={{ outline: 'none' }}
              >
                <X size={11} />
              </button>
            )}
          </label>
          <button
            type="button"
            onClick={() => setOnlyMyPacks(v => !v)}
            disabled={myPackIds.size === 0}
            title={myPackIds.size === 0
              ? t('guns.detail.myPacksDisabledTitle', 'Сначала установите хотя бы одну пушку в выборочной')
              : t('guns.detail.myPacksTitle', 'Показать модели из паков, которые у вас уже установлены в других слотах')}
            style={{ outline: 'none' }}
            className={
              'inline-flex items-center gap-1.5 h-9 px-3 rounded-xl border ' +
              'text-[11.5px] font-bold uppercase tracking-[0.14em] ' +
              'transition-[background-color,color,border-color] duration-300 ease-smooth ' +
              'disabled:opacity-40 disabled:cursor-not-allowed ' +
              (onlyMyPacks
                ? 'bg-white text-black border-white shadow-pill-active'
                : 'bg-white/[0.04] text-text-secondary border-white/[0.06] ' +
                  'hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]')
            }
          >
            <Bookmark size={12} fill={onlyMyPacks ? 'currentColor' : 'none'} strokeWidth={2} />
            <span>{t('guns.detail.myPacksFilter', 'Из моих паков')}</span>
            <span className={
              'tabular-nums px-1.5 rounded-md text-[10px] ' +
              (onlyMyPacks ? 'bg-black/10' : 'bg-white/[0.07] text-text-muted')
            }>
              {myPackMatchCount}
            </span>
          </button>
          <span className="ml-auto inline-flex items-center gap-1.5 px-3 h-9 rounded-xl
                           bg-white/[0.04] border border-white/[0.06]
                           text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted">
            <span className="tabular-nums text-text-primary">{visibleMods.length}</span>
            <span>{t('guns.detail.modelWord', {
              count: visibleMods.length,
              defaultValue_one: 'модель',
              defaultValue_few: 'модели',
              defaultValue_many: 'моделей',
              defaultValue: 'моделей',
            })}</span>
          </span>
        </div>
      </div>
      </div>

      {}
      <div
        className="flex-1 min-h-0 overflow-y-auto px-8 pb-8"
        onScroll={(e) => heroScrollY.set(e.currentTarget.scrollTop)}
      >
        <div className="max-w-[1280px] mx-auto">
        {}
        {loadingAllGuns && mods.length === 0 ? (
          <div className="py-10 flex items-center justify-center text-text-muted gap-2">
            <Loader2 size={16} className="animate-spin" />
            <span className="text-sm">{t('guns.detail.loadingMods', 'Ищем моды по этой пушке…')}</span>
          </div>
        ) : (
          <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6 xl:grid-cols-7 gap-1.5 mt-1">
            {}
            {!onlyMyPacks && search.trim() === '' && (
              <DefaultCard
                displayName={entry.displayName}
                previewUrl={entry.previewUrl}
                isSelected={!selectedModKey}
                onSelect={() => setSelectedModKey(null)}
              />
            )}
            {visibleMods.map(m => {
              const key = `${m.packId}::${m.gunId}`;
              return (
                <ModCard
                  key={key}
                  mod={m}
                  isSelected={selectedModKey === key}
                  isInstalled={installedKey === key}
                  onSelect={() => setSelectedModKey(key)}
                  onView3D={() => setViewerGun(m)}
                  onOpenPack={() => onOpenPack(m.packId)}
                />
              );
            })}
            {mods.length === 0 && (
              <div className="col-span-full py-8 flex flex-col items-center justify-center text-text-muted gap-1 text-center text-xs">
                <p>{t('guns.detail.emptyNoMods', 'Пока ни в одном паке нет мода на эту пушку.')}</p>
                <p className="opacity-60">{t('guns.detail.emptyNoModsHint', 'Когда админ зальёт пак с этой пушкой - он появится здесь.')}</p>
              </div>
            )}
            {mods.length > 0 && visibleMods.length === 0 && (
              <div className="col-span-full py-8 flex flex-col items-center justify-center text-text-muted gap-1 text-center text-xs">
                <p>{onlyMyPacks
                  ? t('guns.detail.emptyMyPacks', 'В ваших паках нет варианта этой пушки.')
                  : t('guns.detail.emptySearch', 'Ничего не найдено по запросу.')}</p>
                <p className="opacity-60">{t('guns.detail.emptyFilteredHint', 'Сбросьте фильтры или попробуйте другой запрос.')}</p>
              </div>
            )}
          </div>
        )}
        </div>
      </div>

      {}
      <AnimatePresence>
        {viewerGun && (
          <GlbViewerModal
            glbUrl={viewerGun.glbUrl}
            title={`${entry.displayName} · ${viewerGun.packName}`}
            onClose={() => setViewerGun(null)}
          />
        )}
      </AnimatePresence>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'info'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={6000}
      />

      {}
      {}
      <ConfirmModal
        open={confirmReplace}
        title={installedRecord
          ? t('guns.detail.replaceModTitle', 'Сменить мод?')
          : t('guns.detail.replaceFromPackTitle', 'Заменить пушку из пака?')}
        message={
          selectedMod && currentSource
            ? (currentSource.kind === 'pack'
                ? t('guns.detail.replaceMsgFromPack', {
                    defaultValue: '{{gun}}\n\nСейчас в этом слоте стоит «{{current}}» (из установленного ганпака). Хочешь заменить её на «{{next}}»?',
                    gun: entry.displayName,
                    current: currentSource.packName,
                    next: selectedMod.packName,
                  })
                : t('guns.detail.replaceMsgSelected', {
                    defaultValue: '{{gun}}\n\nСейчас в этом слоте стоит «{{current}}». Хочешь заменить её на «{{next}}»?',
                    gun: entry.displayName,
                    current: currentSource.packName,
                    next: selectedMod.packName,
                  }))
            : ''
        }
        confirmLabel={installing ? t('guns.gunDetail.installing', 'Ставим…') : t('guns.detail.replaceConfirm', 'Заменить')}
        cancelLabel={t('common.cancel', 'Отмена')}
        onConfirm={() => {
          setConfirmReplace(false);
          void runInstall();
        }}
        onCancel={() => setConfirmReplace(false)}
      />
    </div>
  );
}

function DefaultCard({
  displayName, previewUrl, isSelected, onSelect,
}: {
  displayName: string;
  previewUrl: string | null;
  isSelected: boolean;
  onSelect: () => void;
}) {
  const { t } = useTranslation();
  return (
    <button
      type="button"
      onClick={onSelect}
      style={{
        background: isSelected
          ? 'color-mix(in srgb, var(--accent) 14%, var(--bg-elevated))'
          : 'var(--bg-elevated)',

        boxShadow: isSelected
          ? 'inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 70%, transparent), '
            + '0 14px 30px -14px color-mix(in srgb, var(--accent) 55%, transparent), '
            + '0 6px 18px -10px rgba(0,0,0,0.45)'
          : '0 6px 16px -10px rgba(0,0,0,0.4)',
      }}
      className={
        'group relative flex flex-col gap-1 rounded-xl p-1.5 text-left '
        + 'transition-[background-color,box-shadow,transform] duration-300 ease-smooth '
        + 'hover:-translate-y-0.5'
      }
    >
      <div className="aspect-square flex items-center justify-center">
        {previewUrl ? (
          <img
            src={previewUrl}
            alt=""
            draggable={false}
            className="w-full h-full object-contain select-none"
            onError={e => (e.currentTarget.style.display = 'none')}
          />
        ) : (
          <Crosshair size={36} className="text-text-muted opacity-30" />
        )}
      </div>
      <div className="px-1 pb-0.5">
        {isSelected && (
          <div className="flex items-center gap-1 text-[9px] uppercase tracking-wider text-status-success font-bold">
            <Check size={9} />
            <span>{t('guns.detail.activePill', 'Активно')}</span>
          </div>
        )}
        <div className="font-display text-xs font-bold uppercase tracking-wide text-text-primary truncate">
          {t('guns.detail.default', 'Стандарт')}
        </div>
        <div className="text-[10px] text-text-muted truncate">
          {t('guns.detail.vanillaSubtitle', { defaultValue: 'Ванильная версия: {{name}}', name: displayName.toLowerCase() })}
        </div>
      </div>
    </button>
  );
}

function ModCard({
  mod, isSelected, isInstalled, onSelect, onView3D, onOpenPack,
}: {
  mod: FlatGun;
  isSelected: boolean;
  isInstalled: boolean;
  onSelect: () => void;
  onView3D: () => void;
  onOpenPack: () => void;
}) {
  const { t } = useTranslation();
  return (

    <div
      role="button"
      tabIndex={0}
      onClick={onSelect}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          onSelect();
        }
      }}
      style={{
        outline: 'none',
        background: isSelected
          ? 'color-mix(in srgb, var(--accent) 14%, var(--bg-elevated))'
          : 'var(--bg-elevated)',
        boxShadow: isSelected
          ? 'inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 70%, transparent), '
            + '0 14px 30px -14px color-mix(in srgb, var(--accent) 55%, transparent), '
            + '0 6px 18px -10px rgba(0,0,0,0.45)'
          : '0 6px 16px -10px rgba(0,0,0,0.4)',
      }}
      className={
        'group relative flex flex-col gap-1 rounded-xl p-1.5 text-left cursor-pointer '
        + 'transition-[background-color,box-shadow,transform] duration-300 ease-smooth '
        + 'hover:-translate-y-0.5'
      }
    >
      <div className="aspect-square flex items-center justify-center relative">
        {mod.previewUrl ? (
          <img
            src={mod.previewUrl}
            alt=""
            draggable={false}
            loading="lazy"
            className="w-full h-full object-contain select-none
                       transition-transform duration-500 ease-smooth
                       group-hover:scale-[1.04]"
            onError={e => (e.currentTarget.style.display = 'none')}
          />
        ) : (
          <Crosshair size={36} className="text-text-muted opacity-30" />
        )}

        {}
        {mod.glbUrl && (
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onView3D(); }}
            aria-label={t('guns.detail.view3d', '3D-просмотр')}
            title={t('guns.detail.view3d', '3D-просмотр')}
            style={{ outline: 'none' }}
            className="absolute top-1.5 right-1.5 w-8 h-8 rounded-lg flex items-center justify-center
                       bg-black/55 backdrop-blur-md text-white
                       border border-white/20
                       shadow-[0_6px_14px_-6px_rgba(0,0,0,0.55)]
                       hover:bg-white/[0.18] hover:border-white/40 hover:text-white
                       transition-colors duration-200"
          >
            <Box size={15} strokeWidth={2.2} />
          </button>
        )}
      </div>

      <div className="px-1 pb-0.5 flex flex-col gap-0.5">
        {}
        {isInstalled && (
          <div className="flex items-center gap-1 text-[9px] uppercase tracking-wider text-green-300 font-bold">
            <CheckCircle2 size={9} />
            <span>{t('guns.detail.installedPill', 'Установлено')}</span>
          </div>
        )}
        {isSelected && !isInstalled && (
          <div className="flex items-center gap-1 text-[9px] uppercase tracking-wider text-status-success font-bold">
            <Check size={9} />
            <span>{t('guns.detail.activePill', 'Активно')}</span>
          </div>
        )}
        {}
        <span
          role="button"
          tabIndex={-1}
          onClick={(e) => { e.stopPropagation(); onOpenPack(); }}
          className="font-display text-xs font-bold uppercase tracking-wide text-text-primary truncate hover:text-accent transition-colors cursor-pointer"
          title={t('guns.detail.openPackTitle', { defaultValue: 'Открыть пак {{name}}', name: mod.packName })}
        >
          {mod.packName}
        </span>
        {mod.packAuthor && (
          <div className="text-[10px] text-text-muted truncate">
            {t('guns.detail.byAuthor', { defaultValue: 'от {{author}}', author: mod.packAuthor })}
          </div>
        )}
      </div>
    </div>
  );
}
