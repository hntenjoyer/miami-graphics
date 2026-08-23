import { useCallback, useEffect, useState } from 'react';
import { motion } from 'framer-motion';
import { useTranslation, Trans } from 'react-i18next';
import { Hammer, Target, Crosshair, Map as MapIcon, ArrowLeft, ArrowRight, Lock } from 'lucide-react';
import { useReticleBuilderStore } from '@/store/reticleBuilderStore';
import { useCanSeeCustomGuns } from '@/store/sessionStore';
import { ReticleConstructor } from '@/screens/reticles/ReticleConstructor';
import { MinimapEditorHost } from './MinimapEditorHost';
import { GunCreationFlow } from './GunCreationFlow';
import { GlassPanel } from '@/design/primitives/GlassPanel';
import { EASE_DEPTH } from '@/design';

type Tool = 'reticle' | 'gunpack' | 'minimap';

export function WorkshopHubScreen() {
  const { t } = useTranslation();
  const canSeeReticle  = true;
  const canSeeGunSkins = useCanSeeCustomGuns();
  const canSeeMinimap  = true;

  const [tool, setTool] = useState<Tool | null>(null);
  const [gunFullBleed, setGunFullBleed] = useState(false);
  const [gunStepBack, setGunStepBack] = useState<(() => void) | null>(null);
  const handleGunStepBack = useCallback((fn: (() => void) | null) => setGunStepBack(() => fn), []);

  const current = useReticleBuilderStore(s => s.current);
  const refreshCurrent = useReticleBuilderStore(s => s.refreshCurrent);

  useEffect(() => { void refreshCurrent(); }, [refreshCurrent]);

  const onPick = (t: Tool) => setTool(t);

  return (
    <div className="h-full flex flex-col">
      <div className={'max-w-7xl 2xl:max-w-[1700px] w-full mx-auto px-5 pt-3 shrink-0 '
                      + (gunFullBleed ? 'hidden' : '')}>
        <div className="flex items-center gap-3 flex-wrap">
          {(tool === 'reticle' || tool === 'gunpack') && (
            <button
              type="button"
              onClick={() => { if (tool === 'gunpack' && gunStepBack) gunStepBack(); else setTool(null); }}
              className="w-9 h-9 rounded-xl flex items-center justify-center text-text-muted
                         hover:text-accent hover:bg-glass border border-glass-border bg-glass transition-colors"
              style={{ outline: 'none' }}
              title={tool === 'gunpack' && gunStepBack
                ? t('workshop.hub.stepBack', 'Шаг назад')
                : t('workshop.hub.backToPicker', 'К выбору')}
            >
              <ArrowLeft size={16} />
            </button>
          )}
          <div className="w-10 h-10 rounded-xl flex items-center justify-center shrink-0"
               style={{ background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
                        boxShadow: '0 0 0 1px color-mix(in srgb, var(--accent) 25%, transparent)' }}>
            <Hammer size={19} className="text-accent" />
          </div>
          <div>
            <div className="text-[10.5px] font-bold uppercase tracking-[0.26em] text-text-muted">
              {t('workshop.hub.eyebrow', 'Создать своё · GTA V')}
            </div>
            <h1 className="mt-0.5 text-[clamp(19px,2.2vw,24px)] font-extrabold uppercase tracking-[0.14em] text-text-primary">
              {tool === 'reticle'
                ? <Trans i18nKey="workshop.hub.titleReticle" defaults="Настройка <accent>прицела</accent>"
                         components={{ accent: <span className="text-accent" /> }} />
                : tool === 'gunpack'
                ? <Trans i18nKey="workshop.hub.titleGunpack" defaults="Создание <accent>ганпака</accent>"
                         components={{ accent: <span className="text-accent" /> }} />
                : <Trans i18nKey="workshop.hub.titleHub" defaults="Мастер<accent>ская</accent>"
                         components={{ accent: <span className="text-accent" /> }} />}
            </h1>
          </div>

          {tool === 'reticle' && current?.kind === 'custom' && (
            <span className="inline-flex items-center text-[10px] font-bold uppercase tracking-wide rounded-md px-2 py-1 text-accent bg-accent/15">
              ● {t('workshop.hub.customReticleActive', 'Свой прицел установлен')}
            </span>
          )}
        </div>
      </div>

      <div className={'flex-1 min-h-0 ' + (gunFullBleed ? '' : 'mt-2.5')}>
        {(tool === null || tool === 'minimap') && (
          <div className="h-full overflow-y-auto">
            <div className="max-w-7xl 2xl:max-w-[1700px] mx-auto px-5 pb-6 h-full flex flex-col">
              <p className="text-[13px] text-text-secondary mb-4 shrink-0">
                {t('workshop.hub.intro', 'Выбери, что хочешь собрать - всё создаётся прямо в лаунчере.')}
              </p>
              <div className="grid grid-cols-1 lg:grid-cols-3 gap-5 flex-1 min-h-[420px]">
                <ToolCard
                  index={0}
                  Icon={MapIcon}
                  title={t('workshop.hub.minimapTitle', 'Создать миникарту')}
                  desc={t('workshop.hub.minimapDesc', 'Свои цифры HP и брони, полоса урона, позиция и размер на экране, круги дальности.')}
                  cta={t('workshop.hub.minimapCta', 'Открыть редактор')}
                  locked={!canSeeMinimap}
                  onClick={() => onPick('minimap')}
                />
                <ToolCard
                  index={1}
                  Icon={Target}
                  title={t('workshop.hub.reticleTitle', 'Создать прицел')}
                  desc={t('workshop.hub.reticleDesc', 'Форма, цвет, размер и зазор - с живым превью в размере как в игре.')}
                  cta={t('workshop.hub.reticleCta', 'Открыть конструктор')}
                  locked={!canSeeReticle}
                  onClick={() => onPick('reticle')}
                />
                <ToolCard
                  index={2}
                  Icon={Crosshair}
                  title={t('workshop.hub.gunpackTitle', 'Создать ганпак')}
                  desc={t('workshop.hub.gunpackDesc', '3D-редактор: выбери пушку, покрась и опубликуй скин.')}
                  cta={t('workshop.hub.gunpackCta', 'Открыть мастерскую')}
                  locked={!canSeeGunSkins}
                  onClick={() => onPick('gunpack')}
                />
              </div>
            </div>
          </div>
        )}

        {tool === 'reticle' && (
          canSeeReticle ? (
            <div className="h-full overflow-y-auto">
              <div className="max-w-7xl 2xl:max-w-[1700px] mx-auto px-5 pb-6">
                <ReticleConstructor />
              </div>
            </div>
          ) : (
            <LockedNotice message={t('workshop.hub.lockedReticle', 'Конструктор прицела пока недоступен')} />
          )
        )}

        {tool === 'gunpack' && (
          canSeeGunSkins ? (
            <div className="h-full overflow-y-auto">
              <GunCreationFlow onFullBleed={setGunFullBleed} onStepBack={handleGunStepBack} />
            </div>
          ) : (
            <LockedNotice message={t('workshop.hub.lockedGuns', 'Создание ганов пока недоступно')} />
          )
        )}

        {tool === 'minimap' && canSeeMinimap && (
          <MinimapEditorHost onClose={() => setTool(null)} />
        )}
        {tool === 'minimap' && !canSeeMinimap && (
          <LockedNotice message={t('workshop.hub.lockedMinimap', 'Редактор миникарты пока недоступен')} />
        )}
      </div>
    </div>
  );
}

function ToolCard({ index, Icon, title, desc, cta, locked, onClick }: {
  index: number;
  Icon: typeof Target;
  title: string;
  desc: string;
  cta: string;
  locked?: boolean;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  return (
    <motion.button
      type="button"
      onClick={locked ? undefined : onClick}
      disabled={locked}
      initial={{ opacity: 0, y: 18, scale: 0.97, filter: 'blur(6px)' }}
      animate={{ opacity: 1, y: 0, scale: 1, filter: 'blur(0px)' }}
      transition={{ duration: 0.45, ease: EASE_DEPTH, delay: 0.06 + index * 0.09 }}
      whileHover={locked ? undefined : { y: -3, transition: { duration: 0.18, ease: EASE_DEPTH, delay: 0 } }}
      whileTap={locked ? undefined : { scale: 0.985, transition: { duration: 0.12 } }}
      className="text-left group disabled:cursor-not-allowed w-full h-full"
      style={{ outline: 'none' }}
    >
      <GlassPanel
        depth="z2" tint="ultra" rounded="2xl" highlight edge
        className={
          'relative overflow-hidden border h-full p-5 transition-colors duration-300 ' +
          (locked
            ? 'border-white/[0.06] opacity-55'
            : 'border-white/[0.08] group-hover:border-[color-mix(in_srgb,var(--accent)_45%,transparent)]')
        }
      >
        <span aria-hidden className="absolute inset-0 pointer-events-none bg-bg-elevated/55" />
        <span aria-hidden className="absolute -top-16 -right-10 w-48 h-48 pointer-events-none blur-3xl"
              style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 16%, transparent) 0%, transparent 70%)' }} />
        <div className="relative flex flex-col gap-3 h-full">
          <div className="w-11 h-11 rounded-xl flex items-center justify-center"
               style={{ background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
                        boxShadow: '0 0 0 1px color-mix(in srgb, var(--accent) 25%, transparent)' }}>
            {locked ? <Lock size={19} className="text-text-muted" /> : <Icon size={19} className="text-accent" />}
          </div>
          <div>
            <p className="text-[15px] font-semibold text-text-primary">{title}</p>
            <p className="mt-1 text-[12.5px] text-text-secondary leading-relaxed">{desc}</p>
          </div>
          <span
            className={
              'mt-auto inline-flex items-center gap-2 self-start pl-3.5 pr-2.5 py-1.5 rounded-full border transition-colors duration-200 ' +
              (locked
                ? 'border-white/[0.08]'
                : 'bg-[color-mix(in_srgb,var(--accent-soft)_55%,transparent)] border-[color-mix(in_srgb,var(--accent)_35%,transparent)] group-hover:border-[color-mix(in_srgb,var(--accent)_70%,transparent)]')
            }
          >
            <span className="text-[11.5px] font-bold uppercase tracking-wider text-text-primary">
              {locked ? t('workshop.hub.lockedBadge', 'Пока закрыто') : cta}
            </span>
            {!locked && (
              <span className="w-5 h-5 rounded-full flex items-center justify-center shrink-0
                               bg-[color-mix(in_srgb,var(--accent)_20%,transparent)]
                               transition-transform duration-200 group-hover:translate-x-0.5">
                <ArrowRight size={12} strokeWidth={2.6} className="text-accent" />
              </span>
            )}
          </span>
        </div>
      </GlassPanel>
    </motion.button>
  );
}

function LockedNotice({ message }: { message: string }) {
  const { t } = useTranslation();
  return (
    <div className="h-full flex items-center justify-center px-5">
      <div className="flex flex-col items-center gap-2 text-center">
        <Lock size={22} className="text-text-muted" />
        <p className="text-[14px] font-medium text-text-primary">{message}</p>
        <p className="text-[12.5px] text-text-secondary max-w-sm">
          {t('workshop.hub.lockedBody', 'Раздел ещё в закрытом тестировании - откроем для всех, когда доведём до ума.')}
        </p>
      </div>
    </div>
  );
}
