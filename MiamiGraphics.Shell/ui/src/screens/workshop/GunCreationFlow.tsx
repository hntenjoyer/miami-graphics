import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Crosshair, Package, Boxes, Wand2, X, ArrowRight, Lock } from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { ConfirmModal } from '@/components/ConfirmModal';
import { bridge } from '@/bridge';
import type {
  GunpackGun, GunpackWhitelistEntry, WorkshopFlow, WorkshopFlowLimits,
} from '@/bridge/types';
import { useGunpackStore, type FlatGun } from '@/store/gunpackStore';
import { useCustomGunStore, currentUserIdentity } from '@/store/customGunStore';
import { GunpackCard } from '@/screens/guns/GunpackCard';
import { GunpackDetail } from '@/screens/guns/GunpackDetail';

const CATEGORY_LABEL: Record<string, string> = {
  assault: 'Штурмовая', shotgun: 'Дробовик', sniper: 'Снайперская',
  mg: 'Пулемёт', pistol: 'Пистолет', smg: 'ПП',
};
const categoryLabel = (t: TFunction, cat: string) =>
  t(`guns.category.${cat}`, { defaultValue: CATEGORY_LABEL[cat] ?? cat });

type Step =
  | { k: 'choose' }
  | { k: 'std-grid' }
  | { k: 'own-grid' }
  | { k: 'pack-grid' }
  | { k: 'pack-detail'; packId: string };

interface Props {
  onFullBleed?: (v: boolean) => void;
  onStepBack?: (fn: (() => void) | null) => void;
}

export function GunCreationFlow({ onFullBleed, onStepBack }: Props) {
  const { t } = useTranslation();
  const openWorkshop = useCustomGunStore(s => s.openWorkshop);
  const ownPackTick  = useCustomGunStore(s => s.ownPackTick);

  const whitelist     = useGunpackStore(s => s.whitelist);
  const loadWhitelist = useGunpackStore(s => s.loadWhitelist);
  const publicPacks   = useGunpackStore(s => s.publicPacks);
  const loadPublicPacks = useGunpackStore(s => s.loadPublicPacks);
  const selectedPack  = useGunpackStore(s => s.selectedPack);
  const selectedGuns  = useGunpackStore(s => s.selectedGuns);
  const allGuns       = useGunpackStore(s => s.allGuns);
  const loadAllGuns   = useGunpackStore(s => s.loadAllGuns);

  const [step, setStep] = useState<Step>({ k: 'choose' });
  const [limits, setLimits] = useState<WorkshopFlowLimits | null>(null);
  const myPacks           = useCustomGunStore(s => s.myGunpacks);
  const refreshMyGunpacks = useCustomGunStore(s => s.refreshMyGunpacks);

  const [stdConfirm, setStdConfirm]   = useState<GunpackWhitelistEntry | null>(null);
  const [pickGunOpen, setPickGunOpen] = useState(false);
  const [packConfirm, setPackConfirm] = useState<GunpackGun | null>(null);
  const [ownBase, setOwnBase]         = useState<GunpackWhitelistEntry | null>(null);
  const [ownVariant, setOwnVariant]   = useState<GunpackWhitelistEntry | null>(null);

  useEffect(() => { void loadWhitelist(); void loadPublicPacks(); }, [loadWhitelist, loadPublicPacks]);
  useEffect(() => { onFullBleed?.(step.k === 'pack-detail'); }, [step.k, onFullBleed]);
  useEffect(() => () => onFullBleed?.(false), [onFullBleed]);
  useEffect(() => {
    if (!onStepBack) return;
    if (step.k === 'choose') { onStepBack(null); return; }
    const target: Step = step.k === 'pack-detail' ? { k: 'pack-grid' } : { k: 'choose' };
    onStepBack(() => setStep(target));
    return () => onStepBack(null);
  }, [step.k, onStepBack]);
  useEffect(() => {
    void bridge.workshopFlowLimits().then(setLimits).catch(() => {});
  }, [ownPackTick]);
  useEffect(() => { void refreshMyGunpacks(); }, [refreshMyGunpacks]);
  useEffect(() => {
    const onMsg = (e: MessageEvent) => {
      const d = e.data as { source?: string; type?: string } | null;
      if (d?.source === 'gunsmith' && d.type === 'ownpack-saved') void refreshMyGunpacks();
    };
    window.addEventListener('message', onMsg);
    return () => window.removeEventListener('message', onMsg);
  }, [refreshMyGunpacks]);

  const me = currentUserIdentity().id;
  const contPack = useMemo(() => {
    const mine = myPacks.filter(p => p.ownerId === me && p.guns.length < (limits?.ownPackGunCap ?? 3));
    return mine.length ? mine[0] : null;
  }, [myPacks, me, limits]);
  const gridPack = useMemo(() => {
    const mine = myPacks.filter(p => p.ownerId === me);
    return mine.length ? mine[0] : null;
  }, [myPacks, me]);

  const stdRemaining = (internalName: string) =>
    limits ? Math.max(0, limits.standardMaxPerGun - (limits.standardUsedPerGun[internalName] ?? 0)) : null;
  const packBaseRemaining = limits ? Math.max(0, limits.packBaseMax - limits.packBaseUsed) : null;
  const ownRemaining      = limits ? Math.max(0, limits.ownPackMax - limits.ownPackUsed) : null;
  const ownAvailable      = !!contPack || ownRemaining === null || ownRemaining > 0;

  const start = (flow: WorkshopFlow, pack: string, gun: string, packName: string, gunName: string) => {
    openWorkshop({
      flow, pack, gun, packName, gunName,
      session: crypto.randomUUID(),
      ...(flow === 'ownpack' && contPack
        ? { ownPackId: contPack.id, ownPackName: contPack.name }
        : {}),
    });
  };

  if (step.k === 'pack-detail') {
    return (
      <>
        <GunpackDetail
          packId={step.packId}
          onBack={() => setStep({ k: 'pack-grid' })}
          selectMode={{ label: t('workshop.flow.select', 'Выбрать'), onSelect: () => setPickGunOpen(true) }}
        />
        <GunPickModal
          open={pickGunOpen}
          packName={selectedPack?.name ?? ''}
          guns={selectedGuns.filter(g => !g.isHidden)}
          onPick={(g) => { setPickGunOpen(false); setPackConfirm(g); }}
          onClose={() => setPickGunOpen(false)}
        />
        <ConfirmModal
          open={packConfirm !== null}
          title={t('workshop.flow.packConfirmTitle', 'Кастомизация гана')}
          message={t('workshop.flow.packConfirmMessage', {
            defaultValue: 'Вы уверены, что хотите кастомизировать «{{gun}}» из ганпака «{{pack}}»?',
            gun: packConfirm?.displayName ?? packConfirm?.baseName ?? '',
            pack: selectedPack?.name ?? '',
          })}
          confirmLabel={t('common.continue', 'Продолжить')}
          cancelLabel={t('workshop.flow.cancel', 'Отменить')}
          imageUrl={packConfirm?.previewUrl ?? null}
          imageContain
          onCancel={() => { setPackConfirm(null); setPickGunOpen(true); }}
          onConfirm={() => {
            const g = packConfirm!; setPackConfirm(null);
            start('packbase', step.packId, g.weaponPrefix + g.baseName,
                  selectedPack?.name ?? '', g.displayName ?? g.baseName);
          }}
        />
      </>
    );
  }

  return (
    <div className="max-w-7xl 2xl:max-w-[1700px] mx-auto px-5 pb-6 h-full flex flex-col">
      <AnimatePresence mode="wait">
        {step.k === 'choose' && (
          <motion.div key="choose" {...fade} className="flex-1 min-h-0 flex flex-col">
            <p className="text-[13px] text-text-secondary mb-4 shrink-0">
              {t('workshop.flow.chooseIntro', 'Как соберём ган? Попытки обновляются каждый понедельник.')}
            </p>
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-5 flex-1 min-h-[420px]">
              <FlowCard
                index={0} Icon={Wand2}
                title={t('workshop.flow.stdTitle', 'Персонализировать стандартный ган')}
                desc={t('workshop.flow.stdDesc', 'Возьми обычное оружие GTA и раскрась его под себя. База достаётся прямо из твоей игры.')}
                cta={t('workshop.flow.stdCta', 'Выбрать оружие')}
                badge={limits ? t('workshop.flow.stdBadge', {
                  defaultValue: '{{count}} попытки на ствол в неделю',
                  count: limits.standardMaxPerGun,
                }) : null}
                onClick={() => setStep({ k: 'std-grid' })}
              />
              <FlowCard
                index={1} Icon={Package}
                title={t('workshop.flow.packTitle', 'Кастомизировать базу ганпака')}
                desc={t('workshop.flow.packDesc', 'Возьми ган из любого ганпака каталога и докрась поверх авторского скина.')}
                cta={t('workshop.flow.packCta', 'Выбрать ганпак')}
                badge={packBaseRemaining !== null ? t('workshop.flow.attemptsLeft', {
                  defaultValue: 'осталось попыток: {{left}} из {{max}}',
                  left: packBaseRemaining, max: limits!.packBaseMax,
                }) : null}
                locked={packBaseRemaining === 0}
                lockedHint={t('workshop.flow.weeklyLimitHint', 'Лимит недели исчерпан - сброс в понедельник')}
                onClick={() => setStep({ k: 'pack-grid' })}
              />
              <FlowCard
                index={2} Icon={Boxes}
                title={t('workshop.flow.ownTitle', 'Создать свой ганпак')}
                desc={contPack
                  ? t('workshop.flow.ownDescContinue', {
                      defaultValue: 'Продолжи «{{name}}»: {{done}}/{{cap}} ганов готово.',
                      name: contPack.name, done: contPack.guns.length, cap: limits?.ownPackGunCap ?? 3,
                    })
                  : t('workshop.flow.ownDescNew', 'Собери набор до 3 своих ганов - он сразу попадёт в общий каталог.')}
                cta={contPack
                  ? t('workshop.flow.ownCtaContinue', 'Продолжить ганпак')
                  : t('workshop.flow.ownCtaStart', 'Начать ганпак')}
                badge={ownRemaining !== null && !contPack ? t('workshop.flow.packsLeft', {
                  defaultValue: 'осталось паков: {{left}} из {{max}}',
                  left: ownRemaining, max: limits!.ownPackMax,
                }) : null}
                locked={!ownAvailable}
                lockedHint={t('workshop.flow.weeklyLimitHint', 'Лимит недели исчерпан - сброс в понедельник')}
                onClick={() => setStep({ k: 'own-grid' })}
              />
            </div>
          </motion.div>
        )}

        {(step.k === 'std-grid' || step.k === 'own-grid') && (
          <motion.div key={step.k} {...fade} className="shrink-0">
            {step.k === 'own-grid' && (
              <StepNote text={gridPack && gridPack.guns.length > 0
                ? t('workshop.flow.ownGridNoteContinue', 'Твои нарисованные ганы уже в сетке - выбирай следующий ствол или заверши ганпак снизу.')
                : t('workshop.flow.ownGridNoteNew', 'Выбери оружие для своего ганпака - нарисованный ган встанет в сетку вместо дефолтного.')} />
            )}
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-4">
              {whitelist.map((w, i) => {
                const rem = step.k === 'std-grid' ? stdRemaining(w.internalName) : null;
                const dead = rem === 0;
                const drawn = step.k === 'own-grid'
                  ? gridPack?.guns.find(g => g.internalName === w.internalName) ?? null
                  : null;
                return (
                  <StdGunTile
                    key={w.internalName} entry={w} index={i}
                    attemptsLabel={rem !== null ? t('workshop.flow.tileAttempts', {
                      defaultValue: 'попытки: {{left}}/{{max}}',
                      left: rem, max: limits!.standardMaxPerGun,
                    }) : null}
                    locked={dead}
                    custom={drawn ? { previewUrl: drawn.previewUrl, name: drawn.displayName || drawn.baseName } : null}
                    onClick={() => {
                      if (dead) return;
                      if (step.k === 'std-grid') setStdConfirm(w);
                      else setOwnBase(w);
                    }}
                  />
                );
              })}
              {whitelist.length === 0 && (
                <div className="col-span-full py-16 text-center text-[13px] text-text-muted">
                  {t('workshop.flow.weaponsLoading', 'Список оружия загружается…')}
                </div>
              )}
            </div>

            {step.k === 'own-grid' && gridPack && gridPack.guns.length > 0 && (
              <motion.div
                initial={{ opacity: 0, y: 14 }} animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.3, ease: EASE_DEPTH }}
                className="sticky bottom-3 z-20 mt-5"
              >
                <GlassPanel depth="z3" tint="strong" rounded="2xl" highlight edge
                            className="px-5 py-3.5 flex items-center gap-4 flex-wrap">
                  <div className="min-w-0 flex-1">
                    <p className="text-[13.5px] font-bold text-text-primary truncate">
                      {t('workshop.flow.packHeading', { defaultValue: 'Ганпак «{{name}}»', name: gridPack.name })}
                    </p>
                    <p className="text-[11px] text-text-muted mt-0.5">
                      {t('workshop.flow.packProgress', {
                        defaultValue: '{{done}}/{{cap}} ганов · уже в общем каталоге',
                        done: gridPack.guns.length, cap: limits?.ownPackGunCap ?? 3,
                      })}
                    </p>
                  </div>
                  <div className="flex items-center gap-1.5">
                    {Array.from({ length: limits?.ownPackGunCap ?? 3 }, (_, i) => (
                      <span key={i} className={'w-6 h-1.5 rounded-full ' +
                        (i < gridPack.guns.length ? 'bg-accent' : 'bg-white/[0.10]')} />
                    ))}
                  </div>
                  <button type="button" className="btn-install btn-install--sm"
                          onClick={() => setStep({ k: 'choose' })}>
                    {t('common.done', 'Готово')}
                  </button>
                </GlassPanel>
              </motion.div>
            )}
          </motion.div>
        )}

        {step.k === 'pack-grid' && (
          <motion.div key="pack-grid" {...fade} className="shrink-0">
            <StepNote text={t('workshop.flow.pickPackNote', 'Выбери ганпак - его ган станет базой')} />
            <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-4">
              {publicPacks.map((p, i) => (
                <GunpackCard key={p.id} pack={p} index={i} onClick={() => setStep({ k: 'pack-detail', packId: p.id })} />
              ))}
              {publicPacks.length === 0 && (
                <div className="col-span-full py-16 text-center text-[13px] text-text-muted">
                  {t('workshop.flow.packsLoading', 'Каталог ганпаков загружается…')}
                </div>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <ConfirmModal
        open={stdConfirm !== null}
        title={t('workshop.flow.stdConfirmTitle', 'Кастомизация стандартного оружия')}
        message={t('workshop.flow.stdConfirmMessage', {
          defaultValue: 'Вы уверены, что хотите кастомизировать стандартное оружие «{{gun}}»?',
          gun: stdConfirm?.displayName ?? '',
        })}
        confirmLabel={t('common.continue', 'Продолжить')}
        cancelLabel={t('workshop.flow.cancel', 'Отменить')}
        imageUrl={stdConfirm?.previewUrl ?? null}
        imageContain
        onCancel={() => setStdConfirm(null)}
        onConfirm={() => {
          const w = stdConfirm!; setStdConfirm(null);
          start('standard', '_vanilla', w.internalName, t('workshop.flow.standardPackName', 'Стандартное оружие'), w.displayName);
        }}
      />

      <BaseChoiceModal
        entry={ownBase}
        onStandard={() => {
          const w = ownBase!; setOwnBase(null);
          start('ownpack', '_vanilla', w.internalName, t('workshop.flow.standardPackName', 'Стандартное оружие'), w.displayName);
        }}
        onGunpack={() => {
          const w = ownBase!; setOwnBase(null); setOwnVariant(w);
          if (allGuns.length === 0) void loadAllGuns();
        }}
        onCancel={() => setOwnBase(null)}
      />

      <VariantPickModal
        entry={ownVariant}
        candidates={ownVariant
          ? allGuns.filter(g => g.weaponPrefix + g.baseName === ownVariant.internalName)
          : []}
        loading={allGuns.length === 0}
        onPick={(g) => {
          const w = ownVariant!; setOwnVariant(null);
          start('ownpack', g.packId, g.weaponPrefix + g.baseName, g.packName, g.displayName ?? w.displayName);
        }}
        onClose={() => setOwnVariant(null)}
      />
    </div>
  );
}

const fade = {
  initial: { opacity: 0, y: 10 },
  animate: { opacity: 1, y: 0 },
  exit:    { opacity: 0, y: -8 },
  transition: { duration: 0.25, ease: EASE_DEPTH },
};

function StepNote({ text }: { text: string }) {
  return <p className="text-[13px] text-text-secondary mb-4">{text}</p>;
}

function FlowCard({ index, Icon, title, desc, cta, badge, locked, lockedHint, onClick }: {
  index: number;
  Icon: typeof Package;
  title: string;
  desc: string;
  cta: string;
  badge?: string | null;
  locked?: boolean;
  lockedHint?: string;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  return (
    <motion.button
      type="button"
      onClick={locked ? undefined : onClick}
      disabled={locked}
      initial={{ opacity: 0, y: 18, scale: 0.97 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.05 + index * 0.08 }}
      whileHover={locked ? undefined : { y: -3, transition: { duration: 0.18, ease: EASE_DEPTH, delay: 0 } }}
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
        <div className="relative flex flex-col gap-3 h-full">
          <div className="w-11 h-11 rounded-xl flex items-center justify-center"
               style={{ background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
                        boxShadow: '0 0 0 1px color-mix(in srgb, var(--accent) 25%, transparent)' }}>
            {locked ? <Lock size={19} className="text-text-muted" /> : <Icon size={19} className="text-accent" />}
          </div>
          <div>
            <p className="text-[15px] font-semibold text-text-primary">{title}</p>
            <p className="mt-1 text-[12.5px] text-text-secondary leading-relaxed">{desc}</p>
            {badge && !locked && (
              <p className="mt-2 inline-block text-[10.5px] font-bold uppercase tracking-wide rounded-md px-2 py-1
                            text-accent bg-accent/10">{badge}</p>
            )}
            {locked && lockedHint && (
              <p className="mt-2 text-[11.5px] text-text-muted">{lockedHint}</p>
            )}
          </div>
          <span className={
            'mt-auto inline-flex items-center gap-2 self-start pl-3.5 pr-2.5 py-1.5 rounded-full border transition-colors duration-200 ' +
            (locked
              ? 'border-white/[0.08]'
              : 'bg-[color-mix(in_srgb,var(--accent-soft)_55%,transparent)] border-[color-mix(in_srgb,var(--accent)_35%,transparent)] group-hover:border-[color-mix(in_srgb,var(--accent)_70%,transparent)]')
          }>
            <span className="text-[11.5px] font-bold uppercase tracking-wider text-text-primary">
              {locked ? t('workshop.flow.lockedCta', 'Пока недоступно') : cta}
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

function StdGunTile({ entry, index, attemptsLabel, locked, custom, onClick }: {
  entry: GunpackWhitelistEntry;
  index: number;
  attemptsLabel?: string | null;
  locked?: boolean;
  custom?: { previewUrl: string | null; name: string } | null;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  return (
    <motion.button
      type="button" onClick={onClick} disabled={locked}
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3, ease: EASE_DEPTH, delay: Math.min(index * 0.03, 0.4) }}
      className={'relative text-left group rounded-2xl overflow-hidden border bg-bg-elevated/55 transition-colors ' +
        (locked
          ? 'border-white/[0.05] opacity-45 cursor-not-allowed'
          : 'border-white/[0.08] hover:border-[color-mix(in_srgb,var(--accent)_45%,transparent)]')}
      style={{ outline: 'none' }}
    >
      {custom && (
        <span className="absolute top-2 right-2 z-10 inline-flex items-center gap-1 text-[9.5px] font-bold uppercase tracking-wider text-accent bg-accent/15 backdrop-blur-sm rounded-md px-1.5 py-0.5">
          {t('workshop.flow.inPackBadge', '✓ в ганпаке')}
        </span>
      )}
      <div className="aspect-square w-full flex items-center justify-center bg-black/20 p-3">
        {(custom?.previewUrl || entry.previewUrl)
          ? <img src={custom?.previewUrl || entry.previewUrl!} alt="" loading="lazy"
                 className="max-w-full max-h-full object-contain transition-transform duration-300 group-hover:scale-[1.05]" />
          : <Crosshair size={28} className="text-text-muted" />}
      </div>
      <div className="px-3 py-2.5">
        <p className="text-[13px] font-semibold text-text-primary truncate">
          {custom ? custom.name : entry.displayName}
        </p>
        <div className="mt-0.5 flex items-center justify-between gap-2">
          <p className="text-[10.5px] uppercase tracking-wider text-text-muted">
            {custom ? entry.displayName : categoryLabel(t, entry.category)}
          </p>
          {attemptsLabel && (
            <p className={'text-[10px] font-bold ' + (locked ? 'text-status-error' : 'text-text-muted')}>
              {attemptsLabel}
            </p>
          )}
        </div>
      </div>
    </motion.button>
  );
}

function GunPickModal({ open, packName, guns, onPick, onClose }: {
  open: boolean;
  packName: string;
  guns: GunpackGun[];
  onPick: (g: GunpackGun) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[120] flex items-center justify-center p-6"
          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          style={{ background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(20px)' }}
          onClick={onClose}
        >
          <motion.div
            initial={{ opacity: 0, scale: 0.95, y: 14 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.97, y: 8 }}
            transition={{ duration: 0.28, ease: EASE_DEPTH }}
            className="w-full max-w-[1100px] max-h-[84vh] flex"
            onClick={(e) => e.stopPropagation()}
          >
            <GlassPanel depth="z3" tint="strong" rounded="3xl" highlight edge
                        className="flex-1 min-h-0 flex flex-col p-6 overflow-hidden">
              <div className="flex items-start justify-between gap-4 shrink-0">
                <div>
                  <h2 className="text-[17px] font-bold text-text-primary">
                    {t('workshop.flow.pickGunTitle', 'Выберите, какое оружие вы хотите кастомизировать')}
                  </h2>
                  <p className="mt-1 text-[12.5px] text-text-secondary">
                    {t('workshop.flow.pickGunSubtitle', {
                      defaultValue: 'Ганпак «{{pack}}» - ган откроется в редакторе с авторским скином как базой.',
                      pack: packName,
                    })}
                  </p>
                </div>
                <button type="button" onClick={onClose} aria-label={t('common.close', 'Закрыть')}
                        className="w-8 h-8 rounded-lg flex items-center justify-center text-text-muted
                                   hover:text-text-primary hover:bg-white/[0.06] transition-colors"
                        style={{ outline: 'none' }}>
                  <X size={16} />
                </button>
              </div>
              <div className="mt-4 min-h-0 overflow-y-auto pr-1">
                <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-3.5 pb-2">
                  {guns.map(g => (
                    <button
                      key={g.id} type="button" onClick={() => onPick(g)}
                      className="text-left group rounded-2xl overflow-hidden border border-white/[0.08]
                                 bg-white/[0.04] hover:border-[color-mix(in_srgb,var(--accent)_55%,transparent)]
                                 transition-colors"
                      style={{ outline: 'none' }}
                    >
                      <div className="aspect-[4/3] w-full flex items-center justify-center bg-black/25 p-2.5">
                        {g.previewUrl
                          ? <img src={g.previewUrl} alt="" loading="lazy"
                                 className="max-w-full max-h-full object-contain transition-transform duration-300 group-hover:scale-[1.06]" />
                          : <Crosshair size={24} className="text-text-muted" />}
                      </div>
                      <div className="px-3 py-2.5">
                        <p className="text-[12.5px] font-semibold text-text-primary uppercase truncate">
                          {g.displayName ?? g.baseName}
                        </p>
                        <p className="mt-1 inline-block text-[9.5px] font-bold uppercase tracking-wider
                                      text-text-secondary bg-white/[0.06] border border-white/[0.08]
                                      rounded px-1.5 py-0.5">
                          {categoryLabel(t, g.category)}
                        </p>
                      </div>
                    </button>
                  ))}
                  {guns.length === 0 && (
                    <div className="col-span-full py-14 text-center text-[13px] text-text-muted">
                      {t('workshop.flow.pickGunEmpty', 'В этом паке нет доступных ганов.')}
                    </div>
                  )}
                </div>
              </div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function BaseChoiceModal({ entry, onStandard, onGunpack, onCancel }: {
  entry: GunpackWhitelistEntry | null;
  onStandard: () => void;
  onGunpack: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  return (
    <AnimatePresence>
      {entry && (
        <motion.div
          className="fixed inset-0 z-[120] flex items-center justify-center p-6"
          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          style={{ background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(20px)' }}
          onClick={onCancel}
        >
          <motion.div
            initial={{ opacity: 0, scale: 0.94, y: 12 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.97, y: 8 }}
            transition={{ duration: 0.28, ease: EASE_DEPTH }}
            onClick={(e) => e.stopPropagation()}
          >
            <GlassPanel depth="z3" tint="strong" rounded="3xl" highlight edge className="w-[min(440px,92vw)] p-6">
              <h2 className="text-[16px] font-bold text-text-primary">
                {t('workshop.flow.baseTitle', { defaultValue: 'База для «{{gun}}»', gun: entry.displayName })}
              </h2>
              <p className="mt-1 mb-5 text-[12.5px] text-text-secondary">
                {t('workshop.flow.baseSubtitle', 'С чего начать рисовать этот ган в твоём ганпаке?')}
              </p>
              <div className="flex flex-col gap-2.5">
                <button type="button" onClick={onGunpack} className="btn-install btn-install--block">
                  {t('workshop.flow.baseFromPack', 'Кастомизировать на базе ганпака')}
                </button>
                <button type="button" onClick={onStandard} className="btn-install btn-install--block">
                  {t('workshop.flow.baseFromStandard', 'Кастомизировать на базе стандартного оружия')}
                </button>
                <button type="button" onClick={onCancel} className="btn-glow btn-glow--ghost btn-install--block !inline-flex"
                        style={{ outline: 'none' }}>
                  {t('common.cancel', 'Отмена')}
                </button>
              </div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function VariantPickModal({ entry, candidates, loading, onPick, onClose }: {
  entry: GunpackWhitelistEntry | null;
  candidates: FlatGun[];
  loading: boolean;
  onPick: (g: FlatGun) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  return (
    <AnimatePresence>
      {entry && (
        <motion.div
          className="fixed inset-0 z-[120] flex items-center justify-center p-6"
          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          style={{ background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(20px)' }}
          onClick={onClose}
        >
          <motion.div
            initial={{ opacity: 0, scale: 0.95, y: 14 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.97, y: 8 }}
            transition={{ duration: 0.28, ease: EASE_DEPTH }}
            className="w-full max-w-[900px] max-h-[80vh] flex"
            onClick={(e) => e.stopPropagation()}
          >
            <GlassPanel depth="z3" tint="strong" rounded="3xl" highlight edge
                        className="flex-1 min-h-0 flex flex-col p-6 overflow-hidden">
              <div className="flex items-start justify-between gap-4 shrink-0">
                <div>
                  <h2 className="text-[16px] font-bold text-text-primary">
                    {t('workshop.flow.variantTitle', { defaultValue: 'Чью базу «{{gun}}» возьмём?', gun: entry.displayName })}
                  </h2>
                  <p className="mt-1 text-[12.5px] text-text-secondary">
                    {t('workshop.flow.variantSubtitle', 'Скин выбранного ганпака станет отправной точкой рисунка.')}
                  </p>
                </div>
                <button type="button" onClick={onClose} aria-label={t('common.close', 'Закрыть')}
                        className="w-8 h-8 rounded-lg flex items-center justify-center text-text-muted
                                   hover:text-text-primary hover:bg-white/[0.06] transition-colors"
                        style={{ outline: 'none' }}>
                  <X size={16} />
                </button>
              </div>
              <div className="mt-4 min-h-0 overflow-y-auto pr-1">
                {candidates.length > 0 ? (
                  <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3.5 pb-2">
                    {candidates.map(g => (
                      <button
                        key={g.packId + g.gunId} type="button" onClick={() => onPick(g)}
                        className="text-left group rounded-2xl overflow-hidden border border-white/[0.08]
                                   bg-white/[0.04] hover:border-[color-mix(in_srgb,var(--accent)_55%,transparent)]
                                   transition-colors"
                        style={{ outline: 'none' }}
                      >
                        <div className="aspect-[4/3] w-full flex items-center justify-center bg-black/25 p-2.5">
                          {g.previewUrl
                            ? <img src={g.previewUrl} alt="" loading="lazy"
                                   className="max-w-full max-h-full object-contain transition-transform duration-300 group-hover:scale-[1.06]" />
                            : <Crosshair size={24} className="text-text-muted" />}
                        </div>
                        <div className="px-3 py-2.5">
                          <p className="text-[12.5px] font-bold text-text-primary truncate">{g.packName}</p>
                          <p className="mt-0.5 text-[11px] text-text-secondary truncate">
                            {g.displayName ?? g.baseName}
                          </p>
                        </div>
                      </button>
                    ))}
                  </div>
                ) : (
                  <div className="py-14 text-center text-[13px] text-text-muted">
                    {loading
                      ? t('workshop.flow.variantLoading', 'Собираем варианты из всех ганпаков…')
                      : t('workshop.flow.variantEmpty', 'Ни один ганпак не содержит этот ствол - возьми базу стандартного оружия.')}
                  </div>
                )}
              </div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
