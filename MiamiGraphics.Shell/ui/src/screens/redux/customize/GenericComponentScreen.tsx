import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AnimatePresence, motion } from 'framer-motion';
import { Box, Check, Download, RotateCcw, Loader2, Save, ShieldOff, BookOpen } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import { bridge } from '@/bridge';
import type { LibraryComponent } from '@/bridge/types';
import {
  useCustomizeStore,
  type CustomizeComponentName,
} from '@/store/customizeStore';
import { EASE_DEPTH } from '@/design';
import { GlbViewerModal } from '@/screens/guns/GlbViewerModal';
import { SlideIntro } from './SlideIntro';

type GenericComponent = Exclude<CustomizeComponentName, 'minimap' | 'tracers'>;

interface Props { component: GenericComponent }

const COMPONENT_LABEL_RU: Record<GenericComponent, string> = {
  bloodfx:   'Эффекты',
  crosshair: 'Прицел',
  timecycle: 'Таймциклы',
  armor:     'Броник',
  arena:     'Арена',
};

export function GenericComponentScreen({ component }: Props) {
  const { t } = useTranslation();
  const draft        = useCustomizeStore(s => s.draft);
  const goManage     = useCustomizeStore(s => s.goManage);
  const setGeneric   = useCustomizeStore(s => s.setGeneric);
  const resetComp    = useCustomizeStore(s => s.resetComponent);
  const openImport   = useCustomizeStore(s => s.openImportPicker);
  const openLibrary  = useCustomizeStore(s => s.openLibraryPicker);
  const introSeen     = useCustomizeStore(s => !!s.componentIntroSeen[component]);
  const markIntroSeen = useCustomizeStore(s => s.markComponentIntroSeen);

  useEffect(() => {
    const mountedAt = Date.now();
    return () => {
      if (Date.now() - mountedAt > 200) markIntroSeen(component);
    };
  }, [component, markIntroSeen]);

  const [items, setItems] = useState<LibraryComponent[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const activeRedux = useCustomizeStore(s => s.activeRedux);
  const activeArmor = activeRedux?.components?.armor ?? null;
  const activeArmorGlbUrl   = activeArmor?.glbUrl ?? null;
  const activeArmorHasModel = !!activeArmorGlbUrl;
  const [armorViewerOpen, setArmorViewerOpen] = useState(false);

  const baselineRef = useRef(draft?.[component]);
  const onCancel = () => {
    if (baselineRef.current) setGeneric(component, baselineRef.current);
    goManage();
  };

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setErr(null);
    bridge.libraryList(component)
      .then(list => { if (!cancelled) setItems(list); })
      .catch(e => { if (!cancelled) setErr((e as Error).message); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [component]);

  const current = draft?.[component] ?? { kind: 'default' as const };

  const presets = items ?? [];
  const hasPresets = presets.length > 0;

  const Inner = (
    <>
      {}
      <div className="relative h-full flex flex-col">
        <header className="px-8 pt-6 pb-2 flex items-center gap-4 shrink-0">
          <BackButton onClick={onCancel} label={t('customize.backToManage')} />
          <div className="flex-1" />
          <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted">
            {t(`customize.componentNames.${component}`, COMPONENT_LABEL_RU[component])}
          </span>
          <button
            type="button"
            onClick={() => resetComp(component)}
            title={t('customize.reset')}
            aria-label={t('customize.reset')}
            className="w-9 h-9 rounded-lg flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-bg-elevated transition-colors"
          >
            <RotateCcw size={14} />
          </button>
        </header>

        {}
        <div className="flex-1 overflow-y-auto px-8 pb-32 flex items-center justify-center">
          <div className="max-w-[1280px] w-full flex flex-col items-center gap-10 py-8">

            {}
            <div className="w-full flex justify-center">
              <div className={
                'w-full grid gap-5 ' +
                (component === 'armor'
                  ? 'grid-cols-1 md:grid-cols-3 max-w-3xl'

                  : hasPresets
                    ? 'grid-cols-1 md:grid-cols-3 max-w-[1080px]'
                    : 'grid-cols-1 md:grid-cols-2 max-w-[720px]')
              }>
                <motion.div
                  initial={{ opacity: 0, y: 14 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.4, delay: 0.02, ease: EASE_DEPTH }}
                  className="flex flex-col gap-2"
                >
                  <PrimaryChoiceCard
                    icon={<DefaultGlyph />}
                    title={t('customize.defaultName')}
                    hint={t('customize.defaultHint')}
                    selected={current.kind === 'default'}
                    onClick={() => setGeneric(component, { kind: 'default' })}
                  />
                  {}
                  {component === 'armor' && (
                    <ArmorViewerTrigger
                      hasModel={activeArmorHasModel}
                      onClick={() => setArmorViewerOpen(true)}
                    />
                  )}
                </motion.div>

                {component === 'armor' && (
                  <motion.div
                    initial={{ opacity: 0, y: 14 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.4, delay: 0.06, ease: EASE_DEPTH }}
                  >
                    <PrimaryChoiceCard
                      icon={<ShieldOff size={36} strokeWidth={1.4} className="text-status-error" />}
                      title={t('customize.armorClearTitle')}
                      hint={t('customize.armorClearHint')}
                      tone="danger"
                      selected={current.kind === 'clear'}
                      onClick={() => setGeneric(component, { kind: 'clear' })}
                    />
                  </motion.div>
                )}

                <motion.div
                  initial={{ opacity: 0, y: 14 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.4, delay: 0.10, ease: EASE_DEPTH }}
                >
                  <PrimaryChoiceCard
                    icon={<Download size={36} strokeWidth={1.4} className="text-accent" />}
                    title={t('customize.importTitle')}
                    hint={current.kind === 'import' ? current.donorReduxName : t('customize.importHint')}
                    tone="accent"
                    selected={current.kind === 'import'}
                    onClick={() => openImport(component)}
                  />
                </motion.div>

                {}
                {hasPresets && component !== 'armor' && (
                  <motion.div
                    initial={{ opacity: 0, y: 14 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.4, delay: 0.14, ease: EASE_DEPTH }}
                  >
                    <PrimaryChoiceCard
                      icon={<BookOpen size={36} strokeWidth={1.4} className="text-accent" />}
                      title={t('customize.libraryCardTitle', 'Из нашей библиотеки')}
                      hint={current.kind === 'library' && 'libraryItemName' in current && current.libraryItemName
                        ? current.libraryItemName
                        : t('customize.libraryCardHint', 'Выбрать из готовых пресетов')}
                      tone="accent"
                      selected={current.kind === 'library'}
                      onClick={() => openLibrary(component)}
                    />
                  </motion.div>
                )}
              </div>
            </div>

            {}

            {loading && (
              <div className="flex items-center justify-center gap-2 text-xs text-text-muted">
                <Loader2 size={12} className="animate-spin" /> {t('customize.loadingLibrary')}
              </div>
            )}
            {err && (
              <p className="text-xs text-status-error text-center">{err}</p>
            )}
          </div>
        </div>

        {}
        <SaveBar onSave={goManage} onCancel={onCancel} />
      </div>

      {}
      <AnimatePresence>
        {armorViewerOpen && (
          <GlbViewerModal
            glbUrl={activeArmorGlbUrl}
            title={t('customize.armorViewerTitle', 'Броник в этом редуксе')}
            subjectKind="armor"
            onClose={() => setArmorViewerOpen(false)}
          />
        )}
      </AnimatePresence>
    </>
  );

  if (introSeen) return Inner;

  return (
    <SlideIntro

      resetKey={`generic:${component}`}
      title={t('customize.chooseSourceTitle', 'Откуда взять {{component}}?', {
        component: t(`customize.componentNames.${component}`, COMPONENT_LABEL_RU[component]),
      })}
      subtitle={t('customize.chooseSourceHint', 'Оставьте стоковый или импортируйте из другого редукса.')}
    >
      {Inner}
    </SlideIntro>
  );
}

export function PrimaryChoiceCard({
  icon, title, hint, selected, onClick, tone,
}: {
  icon: React.ReactNode;
  title: string;
  hint: string;
  selected: boolean;
  onClick: () => void;
  tone?: 'accent' | 'danger';
}) {
  const ring =
    tone === 'danger' ? 'ring-status-error/40 hover:ring-status-error/70'
    : tone === 'accent' ? 'ring-accent/40 hover:ring-accent/70'
                        : 'ring-glass-border hover:ring-accent/60';

  const glow =
    tone === 'danger' ? 'shadow-[0_8px_36px_-12px_rgba(244,63,94,0.55)]'
    : tone === 'accent' ? 'shadow-glow-accent'
                        : 'shadow-z2';

  return (
    <button
      type="button"
      onClick={onClick}
      className={
        'group relative w-full aspect-square rounded-3xl overflow-hidden ' +
        'bg-glass-strong backdrop-blur-glass ring-1 ' +
        'transform-gpu will-change-transform ' +
        'transition-[transform,box-shadow] duration-[500ms] ease-smooth ' +
        'hover:-translate-y-1 ' +
        ring + ' ' +
        (selected
          ? 'ring-2 ring-accent shadow-glow-accent'
          : 'hover:' + glow)
      }
    >
      {}
      <div
        aria-hidden
        className={
          'absolute inset-0 pointer-events-none ' +
          (tone === 'danger'
            ? 'bg-[radial-gradient(circle_at_50%_30%,rgba(244,63,94,0.18),transparent_60%)]'
            : tone === 'accent'
              ? 'bg-[radial-gradient(circle_at_50%_30%,var(--accent-soft),transparent_60%)]'
              : 'bg-[radial-gradient(circle_at_50%_30%,rgba(255,255,255,0.05),transparent_60%)]')
        }
      />

      {selected && (
        <motion.div
          initial={{ scale: 0, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ duration: 0.28, ease: EASE_DEPTH }}
          className="absolute top-4 right-4 w-7 h-7 rounded-full bg-accent
                     flex items-center justify-center
                     shadow-[0_0_0_3px_var(--bg-base),0_0_0_4px_color-mix(in_srgb,var(--accent)_45%,transparent),0_8px_22px_-4px_color-mix(in_srgb,var(--accent)_70%,transparent)]"
        >
          <Check size={13} className="text-text-on-accent" strokeWidth={3} />
        </motion.div>
      )}

      <div className="relative h-full w-full flex flex-col items-center justify-center text-center px-6 gap-4">
        <div className="w-20 h-20 rounded-3xl bg-glass
                        flex items-center justify-center
                        transition-transform duration-[500ms] ease-smooth
                        group-hover:scale-[1.06]">
          {icon}
        </div>
        <div className="flex flex-col items-center gap-1.5">
          <span className="font-display font-bold text-lg text-text-primary uppercase tracking-[0.06em]">
            {title}
          </span>
          <span className="text-xs text-text-secondary max-w-[18ch] line-clamp-2">
            {hint}
          </span>
        </div>
      </div>
    </button>
  );
}

export function DefaultGlyph() {
  return (
    <span className="font-display font-bold text-3xl text-text-secondary leading-none">∅</span>
  );
}

export function SaveBar({ onSave, onCancel }: { onSave: () => void; onCancel: () => void }) {
  const { t } = useTranslation();

  return (
    <div className="absolute left-0 right-0 bottom-0 px-8 pb-5 pt-4
                    bg-gradient-to-t from-bg-base via-bg-base/95 to-transparent">
      <div className="max-w-[1280px] 2xl:max-w-[1600px] mx-auto">
        <div className="flex items-center gap-3 p-2 rounded-2xl
                        bg-glass-strong backdrop-blur-glass border border-glass-border
                        shadow-z2">
          <button
            type="button"
            onClick={onCancel}
            className="px-5 h-11 rounded-xl text-[12.5px] font-semibold uppercase tracking-[0.14em]
                       text-text-secondary
                       hover:text-text-primary hover:bg-glass
                       transition-colors duration-200 ease-depth"
          >
            {t('customize.cancel')}
          </button>
          <button
            type="button"
            onClick={onSave}
            style={{
              outline:    'none',
              background: 'var(--bg-elevated)',
              color:      'var(--text-primary)',
              boxShadow:  'inset 0 0 0 1px rgba(255, 255, 255, 0.10)',
            }}
            className="flex-1 inline-flex items-center justify-center gap-2 h-11 rounded-xl
                       text-[12.5px] font-bold uppercase tracking-[0.14em]
                       hover:[box-shadow:inset_0_0_0_1.5px_rgba(255,255,255,0.22)]
                       transition-[box-shadow,background-color,color] duration-200 ease-depth"
          >
            <Save size={14} strokeWidth={2.4} />
            {t('customize.save')}
          </button>
        </div>
      </div>
    </div>
  );
}

function ArmorViewerTrigger({
  hasModel, onClick,
}: { hasModel: boolean; onClick: () => void }) {
  const { t } = useTranslation();
  const label = hasModel
    ? t('customize.armorViewerLabel', '3D просмотр')
    : t('customize.armorViewerLabelMissing', '3D просмотр (нет модели)');
  return (
    <button
      type="button"
      onClick={onClick}
      title={hasModel
        ? t('customize.armorViewerTitle', 'Броник в этом редуксе')
        : t('customize.armorViewerMissingHint', 'Модель ещё не отрендерена - откроем оверлей с подсказкой')}
      className={
        'group inline-flex items-center justify-center gap-2 w-full h-10 rounded-xl ' +
        'bg-glass-strong backdrop-blur-glass border ' +
        'transition-[transform,box-shadow,border-color,color] duration-300 ease-depth ' +
        'focus-visible:outline-none focus-visible:shadow-glow-accent focus-visible:border-accent/60 ' +
        (hasModel
          ? 'border-glass-border text-text-secondary hover:text-text-primary hover:border-accent/60 hover:-translate-y-0.5 hover:shadow-glow-accent'
          : 'border-border-subtle text-text-muted hover:text-text-secondary hover:border-glass-border')
      }
    >
      <Box size={14} strokeWidth={1.8} className={hasModel ? 'text-accent' : ''} />
      <span className="text-xs uppercase tracking-[0.16em] font-bold">{label}</span>
    </button>
  );
}
