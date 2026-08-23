import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import {
  Loader2, Check, Lock,
  Crosshair, Droplet, Map, Shield, Sun, Trophy, Wind,
  type LucideIcon,
} from 'lucide-react';
import { useReduxStore } from '@/store/reduxStore';
import {
  useCustomizeStore,
  type CustomizationDraft,
  type CustomizeComponentName,
  TRACER_MODELS,
} from '@/store/customizeStore';
import { useSubmitDraftStore } from '@/store/submitDraftStore';
import { useNavStore } from '@/store/navStore';
import { useDirtyConfirmStore } from '@/store/dirtyConfirmStore';
import { useGlobalToastStore } from '@/store/globalToastStore';
import { useAdminStore } from '@/store/adminStore';
import { Toast } from '@/components/Toast';
import { ConfirmModal } from '@/components/ConfirmModal';
import { BackButton } from '@/components/BackButton';
import { bridge } from '@/bridge';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { SlideIntro } from './SlideIntro';

const COMPONENT_META: Record<CustomizeComponentName, {
  icon: LucideIcon;
  labelRu: string;
  flags: string[];
}> = {
  bloodfx:   { icon: Droplet,   labelRu: 'Эффекты',   flags: ['replaceable', 'importable'] },
  crosshair: { icon: Crosshair, labelRu: 'Прицел',    flags: ['replaceable', 'importable'] },
  minimap:   { icon: Map,       labelRu: 'Минимапа',  flags: ['replaceable', 'importable'] },
  timecycle: { icon: Sun,       labelRu: 'Таймциклы', flags: ['replaceable', 'importable'] },
  armor:     { icon: Shield,    labelRu: 'Броник',    flags: ['transferable'] },
  tracers:   { icon: Wind,      labelRu: 'Трейсера',  flags: ['replaceable', 'importable'] },
  arena:     { icon: Trophy,    labelRu: 'Арена',     flags: ['transferable'] },
};

const ORDER: CustomizeComponentName[] = [
  'minimap', 'crosshair', 'tracers', 'timecycle', 'armor', 'arena',
];

export function ManageScreen() {
  const { t } = useTranslation();
  const close          = useCustomizeStore(s => s.close);
  const openComponent  = useCustomizeStore(s => s.openComponent);
  const apply          = useCustomizeStore(s => s.apply);
  const draft          = useCustomizeStore(s => s.draft);
  const reduxName      = useCustomizeStore(s => s.activeReduxName);
  const activeReduxId  = useCustomizeStore(s => s.activeReduxId);
  const openBigMapPicker = useCustomizeStore(s => s.openBigMapPicker);
  const items          = useReduxStore(s => s.items);
  const selectRedux    = useReduxStore(s => s.select);

  const goBackToRedux = () => {
    if (activeReduxId) selectRedux(activeReduxId);
    close();
  };

  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [applyFailMsg, setApplyFailMsg] = useState<string | null>(null);
  const introSeen     = useCustomizeStore(s => s.manageIntroSeen);
  const markIntroSeen = useCustomizeStore(s => s.markManageIntroSeen);

  useEffect(() => {
    const mountedAt = Date.now();
    return () => {
      if (Date.now() - mountedAt > 200) markIntroSeen();
    };
  }, [markIntroSeen]);

  const installedReduxId = useReduxStore(s => s.installedReduxId);

  const item = items.find(x => x.id === activeReduxId) ?? null;
  const cards = ORDER.filter(name => {
    const info = item?.components?.[name];
    if (!info) return false;
    if (!info.isFound) return false;
    const need = COMPONENT_META[name].flags;
    const flags = info.flags ?? [];
    return need.some(f => flags.includes(f));
  });

  const hasAnyCustomization = cards.some(
    name => describeStatus(name, draft, t).kind === 'custom',
  ) || !!draft?.bigMap;
  const applyDisabled = busy || (cards.length === 0 && !draft?.bigMap);

  const submitPickingFor = useSubmitDraftStore(s => s.pickingFor);
  const submitReturnTo   = useSubmitDraftStore(s => s.returnTo);
  const submitFinishPick = useSubmitDraftStore(s => s.finishPick);
  const reqNavigate      = useNavStore(s => s.requestNavigate);
  const reqAdminSection  = useAdminStore(s => s.requestSectionChange);
  const isPickMode = submitPickingFor === 'redux';
  const onUseInBuild = () => {
    if (!activeReduxId) return;
    submitFinishPick('redux', activeReduxId, reduxName ?? activeReduxId);
    if (submitReturnTo === 'admin') {
      reqAdminSection('proPlayers');
      reqNavigate('admin');
    } else {
      reqNavigate('players');
    }
  };

  const onApply = async () => {
    if (draft && installedReduxId && draft.reduxId !== installedReduxId && draft.armor.kind === 'default') {
      const cur = await bridge.getCurrentArmorInfo().catch(() => null);
      if (cur && cur.kind === 'library') {
        useDirtyConfirmStore.getState().open({
          title: t('customize.carryArmorTitle', 'Перенести броник?'),
          message: t(
            'customize.carryArmorMessage',
            'Сейчас установлен броник «{{name}}». Перенести его на эту кастомизацию?',
            { name: cur.name || cur.id },
          ),
          cancelLabel: t('settings.cache.cancel', 'Отмена'),
          actions: [
            {
              label: t('customize.carryArmorYes', 'Перенести'),
              hint:  t('customize.carryArmorYesHint', 'Броник останется поверх новой кастомизации'),
              kind: 'accent',
              run: () => { void runApply(); },
            },
            {
              label: t('customize.carryArmorNo', 'Не переносить'),
              hint:  t('customize.carryArmorNoHint', 'Останется броник редукса / стандартный'),
              kind: 'neutral',
              run: async () => {
                await bridge.reduxDeferArmorReapplyOnce().catch(() => {});
                void runApply();
              },
            },
          ],
        });
        return;
      }
    }
    await runApply();
  };

  const finishAndGoBack = (message: string) => {
    useGlobalToastStore.getState().push('success', message);
    goBackToRedux();
  };

  const runApply = async () => {
    setBusy(true);
    try {
      const r = await apply();

      if (!r.ok && r.message === 'DIRTY_FILES_NEED_CONFIRM') {
        useDirtyConfirmStore.getState().open({
          title: t('customize.dirtyConfirmTitle', 'Файлы GTA модифицированы'),
          message: t(
            'customize.dirtyConfirmMessage',
            'В update.rpf уже есть сторонние моды (не наши). Перед применением кастомизации мы восстановим чистый update.rpf из бекапа лаунчера - твои текущие моды в нём пропадут.\n\nПродолжить?',
          ),
          cancelLabel: t('settings.cache.cancel', 'Отмена'),
          actions: [{
            label: t('customize.dirtyConfirmAction', 'Восстановить и применить'),
            kind: 'danger',
            run: onConfirmRestoreAndApply,
          }],
        });
        return;
      }
      if (!r.ok && r.message !== 'BACKUP_REQUIRED') {
        setApplyFailMsg(r.message);
        return;
      }
      if (r.ok) finishAndGoBack(r.message);
    } catch (e) {
      setApplyFailMsg((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const onConfirmRestoreAndApply = async () => {
    setBusy(true);
    try {
      const ok = await bridge.backupRestoreClean();
      if (!ok) {
        setToast({
          tone: 'error',
          message: t(
            'customize.cleanMissing',
            'Чистый update.rpf не найден в локальном бекапе. Запусти бекап со стартового экрана.',
          ),
        });
        return;
      }
      const r = await apply();
      if (r.ok) finishAndGoBack(r.message);
      else if (r.message !== 'BACKUP_REQUIRED') setApplyFailMsg(r.message);
    } catch (e) {
      setApplyFailMsg((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const ManageInner = (
    <>
      {}
      <div className="relative h-full flex flex-col">
        <header className="px-8 pt-6 pb-4 flex items-center gap-4 shrink-0">
          <BackButton onClick={close} label={t('customize.back')} />
          <div className="flex-1" />
          <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted">
            {t('customize.manageTitle')}
          </span>
        </header>

        {}
        <div className="flex-1 overflow-y-auto px-8 pb-32">
          <div className="max-w-[1280px] mx-auto flex flex-col gap-6">

            {(
              <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-4 mt-4">
                {cards.map((name, i) => {
                  const blocked = (item?.components?.[name]?.flags ?? []).includes('personalizeBlocked');
                  return (
                    <motion.div
                      key={name}
                      initial={{ opacity: 0, y: 10 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ duration: 0.36, delay: 0.05 + i * 0.04, ease: EASE_DEPTH }}
                    >
                      <ComponentCard
                        name={name}
                        draft={draft}
                        disabled={blocked}
                        onClick={() => openComponent(name)}
                      />
                    </motion.div>
                  );
                })}
                <motion.div
                  key="__bigmap"
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.36, delay: 0.05 + cards.length * 0.04, ease: EASE_DEPTH }}
                >
                  <BigMapCard
                    selectedName={draft?.bigMap?.name ?? null}
                    blocked={false}
                    onClick={openBigMapPicker}
                  />
                </motion.div>
              </div>
            )}
          </div>
        </div>

        {}
        <div className="absolute left-0 right-0 bottom-0 px-8 pb-5 pt-4
                        bg-gradient-to-t from-bg-base via-bg-base/95 to-transparent">
          <div className="max-w-[1280px] mx-auto">
            <div className="p-2 rounded-2xl bg-glass-strong backdrop-blur-glass
                            border border-glass-border shadow-z2 flex flex-col gap-2">
              {}
              {isPickMode && (
                <button
                  type="button"
                  onClick={onUseInBuild}
                  disabled={!activeReduxId}
                  style={{ outline: 'none' }}
                  className="w-full inline-flex items-center justify-center gap-2 h-12 rounded-xl
                             bg-accent text-text-on-accent
                             border border-accent
                             hover:opacity-90
                             disabled:opacity-50 disabled:cursor-not-allowed
                             transition-opacity text-[12.5px] font-bold uppercase tracking-[0.14em]
                             shadow-[0_8px_28px_-12px_color-mix(in_srgb,var(--accent)_70%,transparent)]"
                >
                  <Trophy size={14} />
                  <span>{t('customize.useInBuild', 'Использовать в сборке')}</span>
                </button>
              )}
              {}
              {hasAnyCustomization ? (
                <button
                  type="button"
                  onClick={onApply}
                  disabled={applyDisabled}
                  style={{
                    outline: 'none',
                    background: (applyDisabled)
                      ? 'var(--bg-elevated)'
                      : 'color-mix(in srgb, var(--accent) 18%, var(--bg-elevated))',
                    color: (applyDisabled)
                      ? 'var(--text-muted)'
                      : 'var(--text-primary)',
                    cursor: (applyDisabled) ? 'not-allowed' : 'pointer',
                    boxShadow: (applyDisabled)
                      ? 'inset 0 0 0 1px var(--border-subtle)'
                      : 'inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 70%, transparent), '
                        + '0 8px 22px -10px color-mix(in srgb, var(--accent) 70%, transparent)',
                  }}
                  className="w-full inline-flex items-center justify-center gap-2 h-12 rounded-xl
                             text-[12.5px] font-bold uppercase tracking-[0.14em]
                             transition-[background-color,box-shadow,color] duration-200 ease-depth"
                >
                  {busy && <Loader2 size={14} className="animate-spin" />}
                  {t('customize.installButton')}
                </button>
              ) : (
                <button
                  type="button"
                  onClick={goBackToRedux}
                  style={{ outline: 'none' }}
                  className="w-full inline-flex items-center justify-center gap-2 h-12 rounded-xl
                             text-[12.5px] font-bold uppercase tracking-[0.14em]
                             bg-bg-elevated text-text-secondary
                             shadow-[inset_0_0_0_1px_var(--border-subtle)]
                             transition-[background-color,box-shadow,color,transform] duration-200 ease-depth
                             hover:text-text-primary
                             hover:bg-[color-mix(in_srgb,var(--accent)_10%,var(--bg-elevated))]
                             hover:shadow-[inset_0_0_0_1.5px_color-mix(in_srgb,var(--accent)_55%,transparent),0_8px_22px_-10px_color-mix(in_srgb,var(--accent)_55%,transparent)]
                             hover:-translate-y-0.5"
                >
                  {t('customize.goBack', 'Вернуться назад')}
                </button>
              )}
            </div>
          </div>
        </div>

        <Toast
          open={!!toast}
          tone={toast?.tone ?? 'success'}
          message={toast?.message ?? ''}
          onClose={() => setToast(null)}
          autoCloseMs={5000}
        />

        {}
        <ConfirmModal
          open={!!applyFailMsg}
          title={t('customize.applyFailTitle', 'Применилось не всё')}
          message={applyFailMsg ?? ''}
          confirmLabel={t('customize.applyFailOk', 'Понятно')}
          cancelLabel={t('customize.applyFailClose', 'Закрыть')}
          destructive
          hideConfirmArrow
          onConfirm={() => setApplyFailMsg(null)}
          onCancel={() => setApplyFailMsg(null)}
        />
      </div>
    </>
  );

  if (introSeen) return ManageInner;

  return (
    <SlideIntro
      resetKey="manage"
      title={t('customize.manageHeroTitle', 'Что вы хотите кастомизировать?')}
      subtitle={
        <span className="inline-flex items-center gap-2">
          <span className="text-text-muted">·</span>
          <span className="text-accent font-bold tracking-wider uppercase text-xs">
            {reduxName}
          </span>
          <span className="text-text-muted">·</span>
        </span>
      }
    >
      {ManageInner}
    </SlideIntro>
  );
}

function ComponentCard({
  name, draft, onClick, disabled = false,
}: {
  name: CustomizeComponentName;
  draft: CustomizationDraft | null;
  onClick: () => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  const meta = COMPONENT_META[name];
  const Icon = meta.icon;
  const status = describeStatus(name, draft, t);
  const isCustomized = status.kind !== 'default' && !disabled;

  return (
    <button
      type="button"
      onClick={disabled ? undefined : onClick}
      disabled={disabled}
      aria-disabled={disabled}
      title={disabled ? t('customize.componentLocked', 'Персонализация этого элемента отключена') : undefined}
      className={
        'group relative w-full text-left transform-gpu will-change-transform rounded-3xl ' +
        'transition-[transform,box-shadow] duration-[500ms] ease-smooth ' +
        (disabled
          ? 'cursor-not-allowed'
          : 'hover:-translate-y-1 hover:shadow-glow-accent focus-visible:outline-none focus-visible:shadow-glow-accent')
      }
    >
      <GlassPanel
        depth="z2"
        tint="soft"
        rounded="3xl"
        className={
          'aspect-[5/4] flex flex-col items-center justify-center text-center gap-4 px-5 py-6 ' +
          'border transition-colors duration-[500ms] ease-smooth ' +
          (disabled
            ? 'border-glass-border opacity-45 grayscale'
            : isCustomized
              ? 'border-accent/60 shadow-[inset_0_0_0_1px_var(--accent-soft)]'
              : 'border-glass-border group-hover:border-accent/50')
        }
      >
        {}
        {isCustomized && (
          <div className="absolute top-3 right-3 w-6 h-6 rounded-full bg-accent
                          flex items-center justify-center shadow-glow-accent">
            <Check size={12} className="text-text-on-accent" strokeWidth={3} />
          </div>
        )}
        {}
        {disabled && (
          <div className="absolute top-3 right-3 w-6 h-6 rounded-full bg-glass-strong
                          border border-glass-border flex items-center justify-center">
            <Lock size={11} className="text-text-muted" strokeWidth={2.4} />
          </div>
        )}

        {}
        <div className={
          'w-16 h-16 rounded-2xl flex items-center justify-center ' +
          'transition-[background-color,color,transform] duration-[500ms] ease-smooth ' +
          (disabled
            ? 'bg-glass-strong text-text-muted'
            : isCustomized
              ? 'bg-accent-soft text-accent'
              : 'bg-glass-strong text-text-secondary group-hover:bg-accent-soft group-hover:text-accent group-hover:scale-105')
        }>
          <Icon size={30} strokeWidth={1.5} />
        </div>

        {}
        <div className="flex flex-col items-center gap-1 min-w-0 w-full">
          <span className="font-display font-bold text-base text-text-primary
                           uppercase tracking-[0.06em] truncate max-w-full">
            {t(`customize.componentNames.${name}`, meta.labelRu)}
          </span>
          {disabled ? (
            <span className="text-[11px] truncate max-w-full text-text-muted font-semibold uppercase tracking-wider">
              {t('customize.componentLockedShort', 'Недоступно')}
            </span>
          ) : isCustomized && (
            <span className="text-[11px] truncate max-w-full text-accent font-semibold">
              {status.label}
            </span>
          )}
        </div>
      </GlassPanel>
    </button>
  );
}

function BigMapCard({
  selectedName, blocked, onClick,
}: {
  selectedName: string | null;
  blocked: boolean;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  const picked = !!selectedName && !blocked;
  return (
    <button
      type="button"
      onClick={blocked ? undefined : onClick}
      disabled={blocked}
      aria-disabled={blocked}
      title={blocked
        ? t('customize.bigMap.blockedTitle', 'У этого редукса своя миникарта - большая карта её перезапишет')
        : undefined}
      className={
        'group relative w-full text-left transform-gpu will-change-transform rounded-3xl ' +
        'transition-[transform,box-shadow] duration-[500ms] ease-smooth ' +
        (blocked
          ? 'cursor-not-allowed'
          : 'hover:-translate-y-1 hover:shadow-glow-accent focus-visible:outline-none focus-visible:shadow-glow-accent')
      }
    >
      <GlassPanel
        depth="z2"
        tint="soft"
        rounded="3xl"
        className={
          'aspect-[5/4] flex flex-col items-center justify-center text-center gap-4 px-5 py-6 ' +
          'border transition-colors duration-[500ms] ease-smooth ' +
          (blocked
            ? 'border-glass-border opacity-45 grayscale'
            : picked
              ? 'border-accent/60 shadow-[inset_0_0_0_1px_var(--accent-soft)]'
              : 'border-glass-border group-hover:border-accent/50')
        }
      >
        {picked && (
          <div className="absolute top-3 right-3 w-6 h-6 rounded-full bg-accent
                          flex items-center justify-center shadow-glow-accent">
            <Check size={12} className="text-text-on-accent" strokeWidth={3} />
          </div>
        )}
        {blocked && (
          <div className="absolute top-3 right-3 w-6 h-6 rounded-full bg-glass-strong
                          border border-glass-border flex items-center justify-center">
            <Lock size={11} className="text-text-muted" strokeWidth={2.4} />
          </div>
        )}

        <div className={
          'w-16 h-16 rounded-2xl flex items-center justify-center ' +
          'transition-[background-color,color,transform] duration-[500ms] ease-smooth ' +
          (blocked
            ? 'bg-glass-strong text-text-muted'
            : picked
              ? 'bg-accent-soft text-accent'
              : 'bg-glass-strong text-text-secondary group-hover:bg-accent-soft group-hover:text-accent group-hover:scale-105')
        }>
          <Map size={30} strokeWidth={1.5} />
        </div>

        <div className="flex flex-col items-center gap-1 min-w-0 w-full">
          <span className="font-display font-bold text-base text-text-primary
                           uppercase tracking-[0.06em] truncate max-w-full">
            {t('customize.bigMap.title', 'Большая карта')}
          </span>
          {blocked ? (
            <span className="text-[11px] truncate max-w-full text-text-muted font-semibold uppercase tracking-wider">
              {t('customize.bigMap.alreadyInRedux', 'Уже в редуксе')}
            </span>
          ) : picked ? (
            <span className="text-[11px] truncate max-w-full text-accent font-semibold">
              {selectedName}
            </span>
          ) : null}
        </div>
      </GlassPanel>
    </button>
  );
}

function describeStatus(
  name: CustomizeComponentName,
  draft: CustomizationDraft | null,
  t: (k: string) => string,
): { label: string; kind: 'default' | 'custom' } {
  if (!draft) return { label: t('customize.statusDefault'), kind: 'default' };

  if (name === 'minimap') {
    const m = draft.minimap;
    if (!m.enabled) return { label: t('customize.statusDefault'), kind: 'default' };
    if (m.librarySource) return { label: m.librarySource.libraryItemName, kind: 'custom' };
    if (m.importedFrom) return { label: m.importedFrom.reduxName, kind: 'custom' };
    return { label: t('customize.statusCustom'), kind: 'custom' };
  }

  if (name === 'tracers') {
    const tr = draft.tracers;
    if (tr.source.kind === 'default') {
      const { r, g, b } = tr.rgb;
      if (r !== 255 || g !== 255 || b !== 255) {
        return { label: t('customize.statusCustom'), kind: 'custom' };
      }
      return { label: t('customize.statusDefault'), kind: 'default' };
    }
    if (tr.source.kind === 'import') {
      return { label: tr.source.donorReduxName, kind: 'custom' };
    }

    const src = tr.source;
    const model = TRACER_MODELS.find(m => m.id === src.modelId)?.name ?? src.modelId;
    return { label: model, kind: 'custom' };
  }

  const g = draft[name];
  if (g.kind === 'default')      return { label: t('customize.statusDefault'), kind: 'default' };
  if (g.kind === 'library')      return { label: g.libraryItemName,            kind: 'custom' };
  if (g.kind === 'import')       return { label: g.donorReduxName,             kind: 'custom' };
  if (g.kind === 'armorLibrary') return { label: g.armorLibraryName,           kind: 'custom' };
      return { label: t('customize.statusClear'),    kind: 'custom' };
}
