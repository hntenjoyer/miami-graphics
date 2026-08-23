import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Loader2, Footprints, Wind, Check, FastForward, Trees, Backpack, Map, Car } from 'lucide-react';
import { ScreenHero } from '@/screens/ScreenHero';
import { EASE_DEPTH } from '@/design';
import { bridge } from '@/bridge';
import type { BackpackStatus } from '@/bridge/types';
import { ensureBackupOrGate } from '@/store/installGate';
import { Toast } from '@/components/Toast';

const CAR_BRANDS = [
  'Alfa Romeo', 'Audi', 'BMW', 'Bugatti', 'Chevrolet', 'Dodge',
  'Ferrari', 'Ford', 'Lamborghini', 'Lexus', 'Mercedes', 'Porsche',
] as const;

export function OtherScreen() {
  const { t } = useTranslation();
  const [loaded, setLoaded]   = useState(false);
  const [toast, setToast]     = useState<{ tone: 'success' | 'error'; message: string } | null>(null);

  const [zalazy, setZalazy]             = useState(false);
  const [zalazyServer, setZalazyServer] = useState<'gta5rp' | 'majestic'>('gta5rp');
  const [zalazyBusy, setZalazyBusy]     = useState(false);
  const [zalazyLoaded, setZalazyLoaded] = useState(false);

  const [foreignZalazy, setForeignZalazy]     = useState(false);
  const [foreignGreenZone, setForeignGreenZone] = useState(false);
  const [foreignBackpack, setForeignBackpack] = useState(false);
  const [foreignLoaded, setForeignLoaded]     = useState(false);
  const [foreignBusy, setForeignBusy]         = useState<null | 'zalazy' | 'greenzone' | 'backpack'>(null);

  const [fastJoin, setFastJoin]             = useState(false);
  const [fastJoinFromRedux, setFastJoinFromRedux] = useState(false);
  const [fastJoinBusy, setFastJoinBusy]     = useState(false);
  const [fastJoinLoaded, setFastJoinLoaded] = useState(false);

  const [greenZone, setGreenZone]             = useState(false);
  const [greenZoneBusy, setGreenZoneBusy]     = useState(false);
  const [greenZoneLoaded, setGreenZoneLoaded] = useState(false);

  const [rukzak, setRukzak]             = useState(false);
  const [rukzakBusy, setRukzakBusy]     = useState(false);
  const [rukzakLoaded, setRukzakLoaded] = useState(false);
  const [backpackDlc, setBackpackDlc]   = useState<BackpackStatus['state'] | null>(null);
  const foreignDlcRemoved = backpackDlc === 'removed-foreign';

  const [layoutPresets, setLayoutPresets] = useState<{ ratio: string; placement: string }[] | null>(null);
  const [layout, setLayout]             = useState<{ ratio: string; placement: string }>({ ratio: '16:9', placement: 'default' });
  const [layoutTransparent, setLayoutTransparent] = useState(false);
  const [layoutSaved, setLayoutSaved]   = useState<{ ratio: string; placement: string; transparent: boolean } | null>(null);
  const [layoutBusy, setLayoutBusy]     = useState(false);
  const [layoutLoaded, setLayoutLoaded] = useState(false);
  const [layoutModalOpen, setLayoutModalOpen] = useState(false);

  const [smoke, setSmoke]             = useState(false);
  const [smokeBusy, setSmokeBusy]     = useState(false);
  const [smokeLoaded, setSmokeLoaded] = useState(false);

  const [carLogos, setCarLogos]             = useState(false);
  const [carLogosBusy, setCarLogosBusy]     = useState(false);
  const [carLogosLoaded, setCarLogosLoaded] = useState(false);
  const [foreignLogos, setForeignLogos]     = useState(false);
  const [foreignLogoHits, setForeignLogoHits] = useState<string[]>([]);

  useEffect(() => {
    let alive = true;
    type Snap = {
      zalazy: { enabled: boolean; server: 'gta5rp' | 'majestic' } | null;
      overlays: { foreignZalazy: boolean; foreignGreenZone: boolean; foreignBackpack: boolean } | null;
      fastJoin: { active: boolean; fromRedux: boolean } | null;
      greenZone: boolean | null;
      rukzak: boolean | null;
      smoke: boolean | null;
      carLogos: { installed: boolean; foreignPresent: boolean; foreignHits: string[] } | null;
      backpackDlc: BackpackStatus['state'] | null;
    };
    const applySnap = (s: Snap) => {
      setLoaded(true);
      if (s.zalazy) { setZalazy(s.zalazy.enabled); setZalazyServer(s.zalazy.server); }
      setZalazyLoaded(true);
      if (s.overlays) { setForeignZalazy(s.overlays.foreignZalazy); setForeignGreenZone(s.overlays.foreignGreenZone); setForeignBackpack(!!s.overlays.foreignBackpack); }
      setForeignLoaded(true);
      if (s.fastJoin) { setFastJoin(s.fastJoin.active); setFastJoinFromRedux(s.fastJoin.fromRedux); }
      setFastJoinLoaded(true);
      if (s.greenZone !== null) setGreenZone(s.greenZone);
      setGreenZoneLoaded(true);
      if (s.backpackDlc) setRukzak(s.backpackDlc === 'removed');
      else if (s.rukzak !== null) setRukzak(s.rukzak);
      setRukzakLoaded(true);
      if (s.smoke !== null) setSmoke(s.smoke);
      setSmokeLoaded(true);
      if (s.carLogos) {
        setCarLogos(s.carLogos.installed);
        setForeignLogos(s.carLogos.foreignPresent);
        setForeignLogoHits(s.carLogos.foreignHits ?? []);
      }
      setCarLogosLoaded(true);
      if (s.backpackDlc) setBackpackDlc(s.backpackDlc);
    };
    void (async () => {
      const [zalazyR, overlaysR, fastR, greenR, rukzakR, smokeR, logosR, bpDlcR] = await Promise.allSettled([
        bridge.otherGetZalazy(),
        Promise.resolve(bridge.otherDetectOverlays?.()),
        typeof bridge.otherGetFastJoinStatus === 'function'
          ? bridge.otherGetFastJoinStatus().then(s => ({ active: !!s?.active, fromRedux: !!s?.active && !s?.userInstalled }))
          : bridge.otherGetFastJoin().then(v => ({ active: v, fromRedux: false })),
        bridge.otherGetGreenZone(),
        bridge.otherGetRukzak(),
        bridge.otherGetSmoke(),
        bridge.otherGetCarLogos(),
        typeof bridge.otherGetBackpackStatus === 'function'
          ? bridge.otherGetBackpackStatus().then(st => st?.state ?? null)
          : Promise.resolve(null),
      ]);
      if (!alive) return;
      const val = <T,>(r: PromiseSettledResult<T>): T | null => r.status === 'fulfilled' ? r.value : null;
      const ov = val(overlaysR);
      const snap: Snap = {
        zalazy: val(zalazyR),
        overlays: ov ? { foreignZalazy: ov.foreignZalazy, foreignGreenZone: ov.foreignGreenZone, foreignBackpack: !!ov.foreignBackpack } : null,
        fastJoin: val(fastR),
        greenZone: val(greenR),
        rukzak: val(rukzakR),
        smoke: val(smokeR),
        carLogos: val(logosR),
        backpackDlc: val(bpDlcR),
      };
      applySnap(snap);
    })();
    return () => { alive = false; };
  }, []);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const [presets, cur] = await Promise.all([
          bridge.minimapLayoutGetPresets?.() ?? Promise.resolve([]),
          bridge.minimapLayoutGet?.() ?? Promise.resolve(null),
        ]);
        if (!alive) return;
        setLayoutPresets(presets?.length ? presets.map(p => ({ ratio: p.ratio, placement: p.placement })) : null);
        if (cur) {
          const next = { ratio: cur.ratio || '16:9', placement: cur.placement || 'default' };
          setLayout(next);
          setLayoutTransparent(!!cur.transparent);
          setLayoutSaved({ ...next, transparent: !!cur.transparent });
        }
      } catch { if (alive) setLayoutPresets(null); }
      finally { if (alive) setLayoutLoaded(true); }
    })();
    return () => { alive = false; };
  }, []);

  const layoutRatios = layoutPresets ? [...new Set(layoutPresets.map(p => p.ratio))] : [];
  const layoutPlaces = layoutPresets ? [...new Set(layoutPresets.map(p => p.placement))] : [];
  const layoutDirty = layoutSaved === null
    || layout.ratio !== layoutSaved.ratio
    || layout.placement !== layoutSaved.placement
    || layoutTransparent !== layoutSaved.transparent;

  const openLayoutModal = () => {
    if (layoutBusy || !layoutLoaded) return;
    if (!ensureBackupOrGate()) return;
    setLayoutModalOpen(true);
  };
  const applyLayout = async (tp: boolean) => {
    setLayoutModalOpen(false);
    if (layoutBusy || !layoutLoaded) return;
    setLayoutBusy(true);
    try {
      const r = await bridge.minimapLayoutApply(layout.ratio, layout.placement, tp);
      if (r.success) {
        setLayoutTransparent(tp);
        setLayoutSaved({ ...layout, transparent: tp });
        setToast({ tone: 'success', message: t('other.layout.appliedToast', 'Положение миникарты применено.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('other.layout.failToast', 'Не удалось применить положение миникарты.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setLayoutBusy(false);
    }
  };

  const applyZalazy = async (enable: boolean, server?: 'gta5rp' | 'majestic') => {
    if (zalazyBusy || !zalazyLoaded) return;
    if (enable && !ensureBackupOrGate()) return;
    const srv = server ?? zalazyServer;
    setZalazyBusy(true);
    try {
      const r = await bridge.otherSetZalazy(enable, srv);
      if (r.success) {
        setZalazy(enable);
        setZalazyServer(srv);
        setToast({ tone: 'success', message: enable
          ? t('other.zalazy.appliedToast', 'Залазы добавлены в игру.')
          : t('other.zalazy.removedToast', 'Залазы убраны.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('other.zalazy.failToast', 'Не удалось применить залазы.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setZalazyBusy(false);
    }
  };

  const selectZalazyServer = (s: 'gta5rp' | 'majestic') => {
    if (s === zalazyServer || zalazyBusy || !zalazyLoaded || zalazy) return;
    setZalazyServer(s);
  };

  const removeForeignOverlay = async (kind: 'zalazy' | 'greenzone' | 'backpack') => {
    if (foreignBusy) return;
    if (!ensureBackupOrGate()) return;
    setForeignBusy(kind);
    try {
      const r = await bridge.otherRemoveForeignOverlay(kind);
      if (r.success) {
        try {
          const v = await bridge.otherDetectOverlays();
          setForeignZalazy(v.foreignZalazy);
          setForeignGreenZone(v.foreignGreenZone);
          setForeignBackpack(!!v.foreignBackpack);
        } catch {  }
        setToast({ tone: 'success', message: kind === 'zalazy'
          ? t('other.zalazy.foreignRemovedToast', 'Чужие залазы убраны из игры.')
          : t('other.greenzone.foreignRemovedToast', 'Чужие зелёные зоны убраны из игры.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('other.foreignRemoveFail', 'Не удалось убрать чужой мод.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setForeignBusy(null);
    }
  };

  const applyGreenZone = async (enable: boolean) => {
    if (greenZoneBusy || !greenZoneLoaded) return;
    if (enable && !ensureBackupOrGate()) return;
    setGreenZoneBusy(true);
    try {
      const r = await bridge.otherSetGreenZone(enable);
      if (r.success) {
        setGreenZone(enable);
        setToast({ tone: 'success', message: enable
          ? t('other.greenzone.appliedToast', 'Зелёные зоны добавлены в игру.')
          : t('other.greenzone.removedToast', 'Зелёные зоны убраны.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('other.greenzone.failToast', 'Не удалось применить зелёные зоны.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setGreenZoneBusy(false);
    }
  };

  const applyRukzak = async (enable: boolean) => {
    if (rukzakBusy || !rukzakLoaded) return;
    if (enable && !ensureBackupOrGate()) return;
    setRukzakBusy(true);
    try {
      const r = await bridge.otherApplyBackpack(enable ? 'remove' : 'vanilla');
      if (r.success) {
        setRukzak(enable);
        setBackpackDlc(enable ? 'removed' : 'vanilla');
        setToast({ tone: 'success', message: enable
          ? t('other.rukzak.appliedToast', 'Рюкзаки убраны из игры.')
          : t('other.rukzak.removedToast', 'Рюкзаки возвращены.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('other.rukzak.failToast', 'Не удалось изменить рюкзаки.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setRukzakBusy(false);
    }
  };

  const restoreVanillaBackpacks = async () => {
    if (rukzakBusy) return;
    if (!ensureBackupOrGate()) return;
    setRukzakBusy(true);
    try {
      const r = await bridge.otherApplyBackpack('vanilla');
      if (r.success) {
        setBackpackDlc('vanilla');
        setToast({ tone: 'success', message: t('other.rukzak.removedToast', 'Рюкзаки возвращены.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('other.rukzak.failToast', 'Не удалось изменить рюкзаки.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setRukzakBusy(false);
    }
  };

  const applyFastJoin = async (enable: boolean) => {
    if (fastJoinBusy || !fastJoinLoaded) return;
    if (enable && !ensureBackupOrGate()) return;
    setFastJoinBusy(true);
    try {
      const r = await bridge.otherSetFastJoin(enable);
      if (r.success) {
        setFastJoin(enable);
        setFastJoinFromRedux(false);
        setToast({ tone: 'success', message: enable
          ? t('other.fastjoin.appliedToast', 'Фаст заход включён.')
          : t('other.fastjoin.removedToast', 'Фаст заход выключен.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('other.fastjoin.failToast', 'Не удалось применить фаст заход.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setFastJoinBusy(false);
    }
  };

  const applySmoke = async (enable: boolean) => {
    if (smokeBusy || !smokeLoaded) return;
    if (enable && !ensureBackupOrGate()) return;
    setSmokeBusy(true);
    try {
      const r = await bridge.otherSetSmoke(enable);
      if (r.success) {
        setSmoke(enable);
        setToast({ tone: 'success', message: enable
          ? t('other.smoke.appliedToast', 'Дым установлен в игру.')
          : t('other.smoke.removedToast', 'Стандартный дым возвращён.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('other.smoke.failToast', 'Не удалось применить дым.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setSmokeBusy(false);
    }
  };

  const applyCarLogos = async (enable: boolean) => {
    if (carLogosBusy || !carLogosLoaded) return;
    if (enable && !ensureBackupOrGate()) return;
    setCarLogosBusy(true);
    try {
      const r = await bridge.otherSetCarLogos(enable);
      if (r.success) {
        setCarLogos(enable);
        setToast({ tone: 'success', message: enable
          ? t('other.carlogos.appliedToast', 'Логотипы авто добавлены.')
          : t('other.carlogos.removedToast', 'Логотипы авто убраны.') });
      } else {
        setToast({ tone: 'error', message: r.errorMessage ?? t('other.carlogos.failToast', 'Не удалось применить логотипы.') });
      }
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setCarLogosBusy(false);
    }
  };

  const allDetectsLoaded = loaded && zalazyLoaded && foreignLoaded
    && fastJoinLoaded && greenZoneLoaded && rukzakLoaded && smokeLoaded && carLogosLoaded;

  if (!allDetectsLoaded) {
    return (
      <div className="h-full overflow-hidden flex flex-col">
        <ScreenHero
          title={t('other.heroTitle', 'Другое')}
          subtitle={t('other.heroSubtitle', 'Дополнительные твики поверх установленной графики.')}
        />
        <div className="flex-1 flex flex-col items-center justify-center gap-3 text-text-muted">
          <Loader2 size={26} className="animate-spin text-accent" />
          <p className="text-sm text-text-secondary">
            {t('other.detecting', 'Проверяю, что установлено в игре…')}
          </p>
          <p className="text-[11px] text-text-muted opacity-70">
            {t('other.detectingHint', 'Читаю update.rpf - это занимает несколько секунд.')}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="h-full overflow-hidden flex flex-col">
      <ScreenHero
        title={t('other.heroTitle', 'Другое')}
        subtitle={t('other.heroSubtitle', 'Дополнительные твики поверх установленной графики.')}
      />
      <div className="w-full px-6 pt-1 pb-3 flex-1 min-h-0 flex flex-col">

        <div className="flex-1 min-h-0 overflow-y-auto pr-1 [scrollbar-gutter:stable]">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-3 lg:min-h-full">
        <div className="flex flex-col gap-3 lg:h-full">
        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.05 }}
          className="relative overflow-hidden rounded-3xl border border-white/[0.08]
                     bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid px-4 py-2.5 flex flex-col gap-2 flex-1 min-h-fit"
        >
          <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none
                     bg-gradient-to-r from-transparent via-white/40 to-transparent" />

          <div className="flex items-start gap-3">
            <div className="w-9 h-9 rounded-xl bg-white/[0.06] border border-white/[0.10]
                            flex items-center justify-center text-accent shrink-0">
              <Footprints size={17} strokeWidth={2} />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-[13px] font-display font-bold text-text-primary uppercase tracking-[0.06em]">
                {zalazyServer === 'majestic'
                  ? t('other.zalazy.titleMajestic', 'Залазы + запретки + мапинг')
                  : t('other.zalazy.title5rp', 'Залазы ВЗП + запретки')}
              </h2>
              <p className="text-[11.5px] text-text-secondary leading-snug mt-0.5">
                {t('other.zalazy.description', 'Добавляет в игру кастомные точки залаза (паркур) и запретки. Прописывается прямо в GTA. Переустанавливается автоматически при смене миникарты или редукса.')}
              </p>
              <div className="inline-flex items-center rounded-lg border border-white/[0.08] bg-white/[0.03] p-0.5 mt-2">
                {(['gta5rp', 'majestic'] as const).map(s => {
                  const on = zalazyServer === s;
                  const lockedByInstall = zalazy && !on;
                  return (
                    <button
                      key={s}
                      type="button"
                      onClick={() => selectZalazyServer(s)}
                      disabled={zalazyBusy || !zalazyLoaded || foreignZalazy || lockedByInstall}
                      title={lockedByInstall
                        ? t('other.zalazy.serverLocked', 'Сначала убери текущие залазы, чтобы сменить сервер')
                        : undefined}
                      style={{ outline: 'none' }}
                      className={
                        'px-3 h-7 rounded-md text-[11px] font-bold uppercase tracking-wider transition-colors ' +
                        'disabled:cursor-not-allowed ' +
                        (on
                          ? 'bg-accent text-text-on-accent shadow-glow-accent'
                          : 'text-text-muted hover:text-text-primary')
                      }
                    >
                      {s === 'gta5rp' ? 'GTA5RP' : 'Majestic'}
                    </button>
                  );
                })}
              </div>
            </div>
          </div>

          <div className="h-px bg-gradient-to-r from-transparent via-white/12 to-transparent mt-auto" aria-hidden />

          <div className="flex items-end justify-between gap-4 flex-wrap">
            <p className="text-[11px] leading-relaxed max-w-md">
              {foreignZalazy ? (
                <span className="text-status-warning">
                  {t('other.zalazy.foreignHint', 'В игре уже стоят ЧУЖИЕ залазы (вшиты сервером/другим модом). Убери их, чтобы поставить наши.')}
                </span>
              ) : (
                <span className="text-text-muted">
                  {t('other.zalazy.hint', 'Чтобы убрать залазы - откати их здесь или в разделе «Загрузки».')}
                </span>
              )}
            </p>
            <div className="flex items-center gap-2.5">
              {foreignZalazy ? (
                <motion.button
                  type="button"
                  onClick={() => removeForeignOverlay('zalazy')}
                  disabled={foreignBusy !== null}
                  whileHover={foreignBusy ? undefined : { scale: 1.02 }}
                  whileTap={foreignBusy ? undefined : { scale: 0.98 }}
                  className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                             bg-[color-mix(in_srgb,var(--status-error)_14%,transparent)]
                             text-status-error border border-[color-mix(in_srgb,var(--status-error)_40%,transparent)]
                             hover:bg-[color-mix(in_srgb,var(--status-error)_22%,transparent)]
                             disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                             text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {foreignBusy === 'zalazy' ? <Loader2 size={15} className="animate-spin" /> : <Footprints size={15} strokeWidth={2.4} />}
                  <span>{foreignBusy === 'zalazy'
                    ? t('other.zalazy.foreignRemoving', 'Убираю чужие...')
                    : t('other.zalazy.foreignRemove', 'Убрать чужие залазы')}</span>
                </motion.button>
              ) : (
                <>
                  {zalazy && (
                    <button
                      type="button"
                      onClick={() => applyZalazy(false)}
                      disabled={zalazyBusy || !zalazyLoaded}
                      className="inline-flex items-center gap-2 px-4 h-9 rounded-lg
                                 bg-white/[0.04] text-text-secondary border border-white/[0.08]
                                 hover:text-status-error hover:border-[color-mix(in_srgb,var(--status-error)_40%,transparent)]
                                 disabled:opacity-40 disabled:cursor-not-allowed transition-colors text-sm font-bold uppercase tracking-wider"
                      style={{ outline: 'none' }}
                    >
                      <span>{t('other.zalazy.remove', 'Убрать')}</span>
                    </button>
                  )}
                  <motion.button
                    type="button"
                    onClick={() => applyZalazy(true)}
                    disabled={zalazyBusy || !zalazyLoaded || zalazy || !foreignLoaded}
                    whileHover={zalazyBusy || zalazy ? undefined : { scale: 1.02 }}
                    whileTap={zalazyBusy || zalazy ? undefined : { scale: 0.98 }}
                    className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                               bg-white/[0.04] text-text-secondary border border-white/[0.08]
                               hover:text-text-primary hover:bg-white/[0.08] hover:border-white/[0.18]
                               disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                               text-sm font-bold uppercase tracking-wider"
                    style={{ outline: 'none' }}
                  >
                    {zalazyBusy ? <Loader2 size={15} className="animate-spin" /> : <Footprints size={15} strokeWidth={2.4} />}
                    <span>
                      {zalazyBusy
                        ? t('other.zalazy.applying', 'Применяю...')
                        : zalazy
                          ? t('other.zalazy.applied', 'Установлено')
                          : t('other.zalazy.apply', 'Добавить')}
                    </span>
                  </motion.button>
                </>
              )}
            </div>
          </div>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.07 }}
          className="relative overflow-hidden rounded-3xl border border-white/[0.08]
                     bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid px-4 py-2.5 flex flex-col gap-2 flex-1"
        >
          <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none
                     bg-gradient-to-r from-transparent via-white/40 to-transparent" />

          <div className="flex items-start gap-3">
            <div className="w-9 h-9 rounded-xl bg-white/[0.06] border border-white/[0.10]
                            flex items-center justify-center text-accent shrink-0">
              <Car size={17} strokeWidth={2} />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-[13px] font-display font-bold text-text-primary uppercase tracking-[0.06em]">
                {t('other.carlogos.title', 'Логотипы авто')}
              </h2>
              <p className="text-[11.5px] text-text-secondary leading-snug mt-0.5">
                {t('other.carlogos.description', 'Ставит настоящие значки марок на машины вместо выдуманных. Прописывается прямо в GTA, переустанавливается автоматически при смене редукса.')}
              </p>
              <div className="flex flex-wrap gap-1 mt-2">
                {CAR_BRANDS.map(b => (
                  <span
                    key={b}
                    className="px-2 h-[19px] inline-flex items-center rounded-md border border-white/[0.08]
                               bg-white/[0.03] text-[10px] font-semibold tracking-wide text-text-muted"
                  >
                    {b}
                  </span>
                ))}
              </div>
            </div>
          </div>

          <div className="h-px bg-gradient-to-r from-transparent via-white/12 to-transparent mt-auto" aria-hidden />

          <div className="flex items-end justify-between gap-4 flex-wrap">
            <p className="text-[11px] leading-relaxed max-w-md">
              {foreignLogos && !carLogos ? (
                <span className="text-status-warning">
                  {t('other.carlogos.foreignHint', 'В игре уже есть логотипы марок из чужого пака ({{sample}}). Наш встанет поверх.', {
                    sample: foreignLogoHits.slice(0, 2).join(', ') || '—',
                  })}
                </span>
              ) : carLogos ? (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg
                                 bg-accent-soft border border-[color-mix(in_srgb,var(--accent)_35%,transparent)]
                                 text-[11px] font-bold text-text-primary">
                  <Check size={12} className="text-accent" strokeWidth={3} />
                  {t('other.carlogos.activeCount', 'Марок в игре: {{n}}', { n: CAR_BRANDS.length })}
                </span>
              ) : (
                <span className="text-text-muted">
                  {t('other.carlogos.hint', 'Чтобы убрать логотипы - откати их здесь или в разделе «Загрузки».')}
                </span>
              )}
            </p>
            <div className="flex items-center gap-2.5">
              {carLogos && (
                <button
                  type="button"
                  onClick={() => applyCarLogos(false)}
                  disabled={carLogosBusy || !carLogosLoaded}
                  className="inline-flex items-center gap-2 px-4 h-9 rounded-lg
                             bg-white/[0.04] text-text-secondary border border-white/[0.08]
                             hover:text-status-error hover:border-[color-mix(in_srgb,var(--status-error)_40%,transparent)]
                             disabled:opacity-40 disabled:cursor-not-allowed transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  <span>{t('other.carlogos.remove', 'Убрать')}</span>
                </button>
              )}
              <motion.button
                type="button"
                onClick={() => applyCarLogos(true)}
                disabled={carLogosBusy || !carLogosLoaded || carLogos}
                whileHover={carLogosBusy || carLogos ? undefined : { scale: 1.02 }}
                whileTap={carLogosBusy || carLogos ? undefined : { scale: 0.98 }}
                className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                           bg-white/[0.04] text-text-secondary border border-white/[0.08]
                           hover:text-text-primary hover:bg-white/[0.08] hover:border-white/[0.18]
                           disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                           text-sm font-bold uppercase tracking-wider"
                style={{ outline: 'none' }}
              >
                {carLogosBusy ? <Loader2 size={15} className="animate-spin" /> : <Car size={15} strokeWidth={2.4} />}
                <span>
                  {carLogosBusy
                    ? t('other.carlogos.applying', 'Применяю...')
                    : carLogos
                      ? t('other.carlogos.applied', 'Установлено')
                      : t('other.carlogos.apply', 'Добавить')}
                </span>
              </motion.button>
            </div>
          </div>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.06 }}
          className="relative overflow-hidden rounded-3xl border border-white/[0.08]
                     bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid px-4 py-2.5 flex flex-col gap-2 flex-1 min-h-fit"
        >
          <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none
                     bg-gradient-to-r from-transparent via-white/40 to-transparent" />

          <div className="flex items-start gap-3">
            <div className="w-9 h-9 rounded-xl bg-white/[0.06] border border-white/[0.10]
                            flex items-center justify-center text-accent shrink-0">
              <Trees size={17} strokeWidth={2} />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-[13px] font-display font-bold text-text-primary uppercase tracking-[0.06em]">
                {t('other.greenzone.title', 'Зелёные зоны')}
                <span className="ml-1.5 text-[10px] font-semibold normal-case tracking-normal text-text-muted">
                  {t('other.greenzone.byAuthor', { defaultValue: 'by {{author}}', author: 'mirz' })}
                </span>
              </h2>
              <p className="text-[11.5px] text-text-secondary leading-snug mt-0.5">
                {t('other.greenzone.description', 'Добавляет в игру подсветку зелёных (безопасных) зон на карте. Прописывается прямо в GTA, переустанавливается автоматически при смене редукса.')}
              </p>
            </div>
          </div>

          <div className="h-px bg-gradient-to-r from-transparent via-white/12 to-transparent mt-auto" aria-hidden />

          <div className="flex items-end justify-between gap-4 flex-wrap">
            <p className="text-[11px] leading-relaxed max-w-md">
              {foreignGreenZone ? (
                <span className="text-status-warning">
                  {t('other.greenzone.foreignHint', 'В игре уже стоят ЧУЖИЕ зелёные зоны (вшиты сервером/другим модом). Убери их, чтобы поставить наши.')}
                </span>
              ) : (
                <span className="text-text-muted">
                  {t('other.greenzone.hint', 'Чтобы убрать зелёные зоны - откати их здесь или в разделе «Загрузки».')}
                </span>
              )}
            </p>
            <div className="flex items-center gap-2.5">
              {foreignGreenZone ? (
                <motion.button
                  type="button"
                  onClick={() => removeForeignOverlay('greenzone')}
                  disabled={foreignBusy !== null}
                  whileHover={foreignBusy ? undefined : { scale: 1.02 }}
                  whileTap={foreignBusy ? undefined : { scale: 0.98 }}
                  className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                             bg-[color-mix(in_srgb,var(--status-error)_14%,transparent)]
                             text-status-error border border-[color-mix(in_srgb,var(--status-error)_40%,transparent)]
                             hover:bg-[color-mix(in_srgb,var(--status-error)_22%,transparent)]
                             disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                             text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {foreignBusy === 'greenzone' ? <Loader2 size={15} className="animate-spin" /> : <Trees size={15} strokeWidth={2.4} />}
                  <span>{foreignBusy === 'greenzone'
                    ? t('other.greenzone.foreignRemoving', 'Убираю чужие...')
                    : t('other.greenzone.foreignRemove', 'Убрать чужие зоны')}</span>
                </motion.button>
              ) : (
                <>
                  {greenZone && (
                    <button
                      type="button"
                      onClick={() => applyGreenZone(false)}
                      disabled={greenZoneBusy || !greenZoneLoaded}
                      className="inline-flex items-center gap-2 px-4 h-9 rounded-lg
                                 bg-white/[0.04] text-text-secondary border border-white/[0.08]
                                 hover:text-status-error hover:border-[color-mix(in_srgb,var(--status-error)_40%,transparent)]
                                 disabled:opacity-40 disabled:cursor-not-allowed transition-colors text-sm font-bold uppercase tracking-wider"
                      style={{ outline: 'none' }}
                    >
                      <span>{t('other.greenzone.remove', 'Убрать')}</span>
                    </button>
                  )}
                  <motion.button
                    type="button"
                    onClick={() => applyGreenZone(true)}
                    disabled={greenZoneBusy || !greenZoneLoaded || greenZone || !foreignLoaded}
                    whileHover={greenZoneBusy || greenZone ? undefined : { scale: 1.02 }}
                    whileTap={greenZoneBusy || greenZone ? undefined : { scale: 0.98 }}
                    className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                               bg-white/[0.04] text-text-secondary border border-white/[0.08]
                               hover:text-text-primary hover:bg-white/[0.08] hover:border-white/[0.18]
                               disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                               text-sm font-bold uppercase tracking-wider"
                    style={{ outline: 'none' }}
                  >
                    {greenZoneBusy ? <Loader2 size={15} className="animate-spin" /> : <Trees size={15} strokeWidth={2.4} />}
                    <span>
                      {greenZoneBusy
                        ? t('other.greenzone.applying', 'Применяю...')
                        : greenZone
                          ? t('other.greenzone.applied', 'Установлено')
                          : t('other.greenzone.apply', 'Добавить')}
                    </span>
                  </motion.button>
                </>
              )}
            </div>
          </div>
        </motion.div>

        </div>
        <div className="flex flex-col gap-3 lg:h-full">
        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.075 }}
          className="relative overflow-hidden rounded-3xl border border-white/[0.08]
                     bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid px-4 py-2.5 flex flex-col gap-2 flex-1 min-h-fit"
        >
          <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none
                     bg-gradient-to-r from-transparent via-white/40 to-transparent" />

          <div className="flex items-start gap-3">
            <div className="w-9 h-9 rounded-xl bg-white/[0.06] border border-white/[0.10]
                            flex items-center justify-center text-accent shrink-0">
              <FastForward size={17} strokeWidth={2} />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-[13px] font-display font-bold text-text-primary uppercase tracking-[0.06em]">
                {t('other.fastjoin.title', 'Фаст заход')}
              </h2>
              <p className="text-[11.5px] text-text-secondary leading-snug mt-0.5">
                {t('other.fastjoin.description', 'Пропускает пролёт камеры к персонажу при заходе на сервер - экономит около 15 секунд каждый спавн. Прописывается прямо в GTA, переустанавливается автоматически при смене редукса.')}
              </p>
            </div>
          </div>

          <div className="h-px bg-gradient-to-r from-transparent via-white/12 to-transparent mt-auto" aria-hidden />

          <div className="flex items-end justify-between gap-4 flex-wrap">
            <p className="text-[11px] text-text-muted leading-relaxed max-w-md">
              {fastJoinFromRedux
                ? t('other.fastjoin.hintFromRedux', 'Уже включён - входит в состав редукса, отдельно ставить не нужно. Чтобы вернуть обычный заход с камерой - выключи здесь.')
                : t('other.fastjoin.hint', 'Чтобы вернуть обычный заход с камерой - выключи здесь.')}
            </p>
            <div className="flex items-center gap-2.5">
              {fastJoin && (
                <button
                  type="button"
                  onClick={() => applyFastJoin(false)}
                  disabled={fastJoinBusy || !fastJoinLoaded}
                  className="inline-flex items-center gap-2 px-4 h-9 rounded-lg
                             bg-white/[0.04] text-text-secondary border border-white/[0.08]
                             hover:text-status-error hover:border-[color-mix(in_srgb,var(--status-error)_40%,transparent)]
                             disabled:opacity-40 disabled:cursor-not-allowed transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  <span>{t('other.fastjoin.remove', 'Выключить')}</span>
                </button>
              )}
              <motion.button
                type="button"
                onClick={() => applyFastJoin(true)}
                disabled={fastJoinBusy || !fastJoinLoaded || fastJoin}
                whileHover={fastJoinBusy || fastJoin ? undefined : { scale: 1.02 }}
                whileTap={fastJoinBusy || fastJoin ? undefined : { scale: 0.98 }}
                className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                           bg-white/[0.04] text-text-secondary border border-white/[0.08]
                           hover:text-text-primary hover:bg-white/[0.08] hover:border-white/[0.18]
                           disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                           text-sm font-bold uppercase tracking-wider"
                style={{ outline: 'none' }}
              >
                {fastJoinBusy ? <Loader2 size={15} className="animate-spin" /> : <FastForward size={15} strokeWidth={2.4} />}
                <span>
                  {fastJoinBusy
                    ? t('other.fastjoin.applying', 'Применяю...')
                    : fastJoinFromRedux
                      ? t('other.fastjoin.fromRedux', 'Входит в редукс')
                      : fastJoin
                        ? t('other.fastjoin.applied', 'Включено')
                        : t('other.fastjoin.apply', 'Включить')}
                </span>
              </motion.button>
            </div>
          </div>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.1 }}
          className="relative overflow-hidden rounded-3xl border border-white/[0.08]
                     bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid px-4 py-2.5 flex flex-col gap-2 flex-1 min-h-fit"
        >
          <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none
                     bg-gradient-to-r from-transparent via-white/40 to-transparent" />

          <div className="flex items-start gap-3">
            <div className="w-9 h-9 rounded-xl bg-white/[0.06] border border-white/[0.10]
                            flex items-center justify-center text-accent shrink-0">
              <Wind size={17} strokeWidth={2} />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-[13px] font-display font-bold text-text-primary uppercase tracking-[0.06em]">
                {t('other.smoke.title', 'Вернуть дым')}
              </h2>
              <p className="text-[11.5px] text-text-secondary leading-snug mt-0.5">
                {t('other.smoke.description', 'Ставит кастомные текстуры дыма частиц в игру (правит core.ypt поверх редукса). Переустанавливается автоматически при смене миникарты или редукса.')}
              </p>
            </div>
          </div>

          <div className="h-px bg-gradient-to-r from-transparent via-white/12 to-transparent mt-auto" aria-hidden />

          <div className="flex items-end justify-between gap-4 flex-wrap">
            <p className="text-[11px] text-text-muted leading-relaxed max-w-md">
              {t('other.smoke.hint', 'Чтобы вернуть стандартный дым - откати здесь или в разделе «Загрузки».')}
            </p>
            <div className="flex items-center gap-2.5">
              {smoke && (
                <button
                  type="button"
                  onClick={() => applySmoke(false)}
                  disabled={smokeBusy || !smokeLoaded}
                  className="inline-flex items-center gap-2 px-4 h-9 rounded-lg
                             bg-white/[0.04] text-text-secondary border border-white/[0.08]
                             hover:text-status-error hover:border-[color-mix(in_srgb,var(--status-error)_40%,transparent)]
                             disabled:opacity-40 disabled:cursor-not-allowed transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  <span>{t('other.smoke.remove', 'Убрать')}</span>
                </button>
              )}
              <motion.button
                type="button"
                onClick={() => applySmoke(true)}
                disabled={smokeBusy || !smokeLoaded || smoke}
                whileHover={smokeBusy || smoke ? undefined : { scale: 1.02 }}
                whileTap={smokeBusy || smoke ? undefined : { scale: 0.98 }}
                className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                           bg-white/[0.04] text-text-secondary border border-white/[0.08]
                           hover:text-text-primary hover:bg-white/[0.08] hover:border-white/[0.18]
                           disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                           text-sm font-bold uppercase tracking-wider"
                style={{ outline: 'none' }}
              >
                {smokeBusy ? <Loader2 size={15} className="animate-spin" /> : <Wind size={15} strokeWidth={2.4} />}
                <span>
                  {smokeBusy
                    ? t('other.smoke.applying', 'Возвращаю...')
                    : smoke
                      ? t('other.smoke.applied', 'Возвращено')
                      : t('other.smoke.apply', 'Вернуть')}
                </span>
              </motion.button>
            </div>
          </div>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.1 }}
          className="relative overflow-hidden rounded-3xl border border-white/[0.08]
                     bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid px-4 py-2.5 flex flex-col gap-2 flex-1 min-h-fit"
        >
          <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none
                     bg-gradient-to-r from-transparent via-white/40 to-transparent" />

          <div className="flex items-start gap-3">
            <div className="w-9 h-9 rounded-xl bg-white/[0.06] border border-white/[0.10]
                            flex items-center justify-center text-accent shrink-0">
              <Backpack size={17} strokeWidth={2} />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-[13px] font-display font-bold text-text-primary uppercase tracking-[0.06em]">
                {t('other.rukzak.title', 'Удаление рюкзаков')}
              </h2>
              <p className="text-[11.5px] text-text-secondary leading-snug mt-0.5">
                {t('other.rukzak.description', 'Убирает рюкзаки и сумки с персонажа в игре (оверлей в update.rpf, без пересборки). Переустанавливается автоматически при смене миникарты или редукса.')}
              </p>
            </div>
          </div>

          <div className="h-px bg-gradient-to-r from-transparent via-white/12 to-transparent mt-auto" aria-hidden />

          <div className="flex items-end justify-between gap-4 flex-wrap">
            <p className="text-[11px] leading-relaxed max-w-md">
              {foreignDlcRemoved ? (
                <span className="text-status-warning">
                  {t('other.rukzak.foreignDlcHint', 'Рюкзаки уже убраны сторонним модом - он подменил файл игры целиком. Убирать нечего; можно только вернуть рюкзаки обратно.')}
                </span>
              ) : foreignBackpack ? (
                <span className="text-status-warning">
                  {t('other.rukzak.foreignHint', 'Рюкзаки у тебя уже убраны ЧУЖИМ модом. Убери его, иначе наш оверлей ляжет поверх и они будут спорить.')}
                </span>
              ) : (
                <span className="text-text-muted">
                  {t('other.rukzak.hint', 'Чтобы вернуть рюкзаки - нажми «Вернуть» здесь.')}
                </span>
              )}
            </p>
            <div className="flex items-center gap-2.5">
              {foreignBackpack && !foreignDlcRemoved && (
                <motion.button
                  type="button"
                  onClick={() => removeForeignOverlay('backpack')}
                  disabled={foreignBusy !== null}
                  whileHover={foreignBusy ? undefined : { scale: 1.02 }}
                  whileTap={foreignBusy ? undefined : { scale: 0.98 }}
                  className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                             bg-[color-mix(in_srgb,var(--status-error)_14%,transparent)]
                             text-status-error border border-[color-mix(in_srgb,var(--status-error)_40%,transparent)]
                             hover:bg-[color-mix(in_srgb,var(--status-error)_22%,transparent)]
                             disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                             text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {foreignBusy === 'backpack' ? <Loader2 size={15} className="animate-spin" /> : <Backpack size={15} strokeWidth={2.4} />}
                  <span>{foreignBusy === 'backpack'
                    ? t('other.rukzak.foreignRemoving', 'Убираю чужой...')
                    : t('other.rukzak.foreignRemove', 'Убрать чужой мод')}</span>
                </motion.button>
              )}
              {foreignDlcRemoved && (
                <motion.button
                  type="button"
                  onClick={() => void restoreVanillaBackpacks()}
                  disabled={rukzakBusy}
                  whileHover={rukzakBusy ? undefined : { scale: 1.02 }}
                  whileTap={rukzakBusy ? undefined : { scale: 0.98 }}
                  className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                             bg-white/[0.04] text-text-secondary border border-white/[0.08]
                             hover:text-text-primary hover:bg-white/[0.08] hover:border-white/[0.18]
                             disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                             text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {rukzakBusy ? <Loader2 size={15} className="animate-spin" /> : <Backpack size={15} strokeWidth={2.4} />}
                  <span>{rukzakBusy
                    ? t('other.rukzak.restoring', 'Возвращаю...')
                    : t('other.rukzak.remove', 'Вернуть')}</span>
                </motion.button>
              )}
              {rukzak && !foreignBackpack && !foreignDlcRemoved && (
                <button
                  type="button"
                  onClick={() => applyRukzak(false)}
                  disabled={rukzakBusy || !rukzakLoaded}
                  className="inline-flex items-center gap-2 px-4 h-9 rounded-lg
                             bg-white/[0.04] text-text-secondary border border-white/[0.08]
                             hover:text-status-error hover:border-[color-mix(in_srgb,var(--status-error)_40%,transparent)]
                             disabled:opacity-40 disabled:cursor-not-allowed transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  <span>{t('other.rukzak.remove', 'Вернуть')}</span>
                </button>
              )}
              {!foreignDlcRemoved && (
              <motion.button
                type="button"
                onClick={() => applyRukzak(true)}
                disabled={rukzakBusy || !rukzakLoaded || rukzak || foreignBackpack}
                whileHover={rukzakBusy || rukzak || foreignBackpack ? undefined : { scale: 1.02 }}
                whileTap={rukzakBusy || rukzak || foreignBackpack ? undefined : { scale: 0.98 }}
                className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                           bg-white/[0.04] text-text-secondary border border-white/[0.08]
                           hover:text-text-primary hover:bg-white/[0.08] hover:border-white/[0.18]
                           disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                           text-sm font-bold uppercase tracking-wider"
                style={{ outline: 'none' }}
              >
                {rukzakBusy ? <Loader2 size={15} className="animate-spin" /> : <Backpack size={15} strokeWidth={2.4} />}
                <span>
                  {rukzakBusy
                    ? t('other.rukzak.applying', 'Убираю...')
                    : rukzak
                      ? t('other.rukzak.applied', 'Убрано')
                      : t('other.rukzak.apply', 'Убрать')}
                </span>
              </motion.button>
              )}
            </div>
          </div>
        </motion.div>

        {layoutPresets && (
        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.12 }}
          className="relative overflow-hidden rounded-3xl border border-white/[0.08]
                     bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid px-4 py-2.5 flex flex-col gap-2.5 flex-1 min-h-fit"
        >
          <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none
                     bg-gradient-to-r from-transparent via-white/40 to-transparent" />

          <div className="flex items-start gap-3">
            <div className="w-9 h-9 rounded-xl bg-white/[0.06] border border-white/[0.10]
                            flex items-center justify-center text-accent shrink-0">
              <Map size={17} strokeWidth={2} />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-[13px] font-display font-bold text-text-primary uppercase tracking-[0.06em]">
                {t('other.layout.title', 'Положение миникарты')}
              </h2>
              <p className="text-[11.5px] text-text-secondary leading-snug mt-0.5">
                {t('other.layout.description', 'Готовые локации для твоего формата экрана. Живая правка поверх текущей сборки, без пересборки редукса. Произвольное место мышью - в «Мастерской».')}
              </p>
            </div>
          </div>

          <div className="flex flex-col gap-2">
            <span className="text-[10.5px] uppercase tracking-[0.18em] text-text-muted">
              {t('other.layout.sizeLabel', 'Размер')}
            </span>
            <div className="flex flex-wrap gap-1.5">
              {layoutRatios.map(r => (
                <LayoutPill key={r} active={layout.ratio === r} disabled={layoutBusy}
                  label={r} onClick={() => setLayout(l => ({ ...l, ratio: r }))} />
              ))}
            </div>
          </div>

          <div className="flex flex-col gap-2">
            <span className="text-[10.5px] uppercase tracking-[0.18em] text-text-muted">
              {t('other.layout.posLabel', 'Положение')}
            </span>
            <div className="flex flex-wrap gap-1.5">
              {layoutPlaces.map(p => (
                <LayoutPill key={p} active={layout.placement === p} disabled={layoutBusy}
                  label={p === 'center' ? t('other.layout.center', 'По центру') : p === 'default' ? t('other.layout.default', 'По дефолту') : p}
                  onClick={() => setLayout(l => ({ ...l, placement: p }))} />
              ))}
            </div>
          </div>

          <div className="h-px bg-gradient-to-r from-transparent via-white/12 to-transparent" aria-hidden />

          <div className="flex items-center justify-between gap-4 flex-wrap">
            <p className="text-[11px] text-text-muted leading-relaxed max-w-md">
              {t('other.layout.hint', 'Применение - пара минут: перешифровка файлов игры.')}
            </p>
            <motion.button
              type="button"
              onClick={openLayoutModal}
              disabled={layoutBusy || !layoutLoaded || !layoutDirty}
              whileHover={layoutBusy || !layoutDirty ? undefined : { scale: 1.02 }}
              whileTap={layoutBusy || !layoutDirty ? undefined : { scale: 0.98 }}
              className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0
                         bg-white/[0.04] text-text-secondary border border-white/[0.08]
                         hover:text-text-primary hover:bg-white/[0.08] hover:border-white/[0.18]
                         disabled:opacity-40 disabled:cursor-not-allowed transition-colors
                         text-sm font-bold uppercase tracking-wider"
              style={{ outline: 'none' }}
            >
              {layoutBusy ? <Loader2 size={15} className="animate-spin" /> : <Map size={15} strokeWidth={2.4} />}
              <span>{layoutBusy
                ? t('other.layout.applying', 'Применяю...')
                : !layoutDirty
                  ? t('other.layout.appliedState', 'Применено')
                  : t('other.layout.apply', 'Применить')}</span>
            </motion.button>
          </div>
        </motion.div>
        )}

        </div>
        </div>
        </div>
      </div>

      <AnimatePresence>
        {layoutModalOpen && (
          <motion.div
            className="fixed inset-0 z-[120] flex items-center justify-center p-6"
            initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
          >
            <div className="absolute inset-0 bg-black/60 backdrop-blur-sm"
                 onClick={() => !layoutBusy && setLayoutModalOpen(false)} />
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 8 }}
              transition={{ duration: 0.28, ease: EASE_DEPTH }}
              className="relative w-full max-w-[460px] rounded-3xl border border-white/[0.10]
                         bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid p-6 flex flex-col gap-4"
            >
              <div className="flex items-start gap-3">
                <div className="w-10 h-10 rounded-xl bg-white/[0.06] border border-white/[0.10]
                                flex items-center justify-center text-accent shrink-0">
                  <Map size={18} strokeWidth={2} />
                </div>
                <div className="flex-1 min-w-0">
                  <h3 className="text-[15px] font-display font-bold text-text-primary uppercase tracking-[0.05em]">
                    {t('other.layout.modalTitle', 'Применить положение')}
                  </h3>
                  <p className="text-[12px] text-text-secondary leading-snug mt-1">
                    {t('other.layout.modalSubtitle', 'Выбери фон миникарты. Применение - пара минут (перешифровка файлов игры).')}
                  </p>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-2.5">
                {[
                  { tp: false, title: t('other.layout.keepBg', 'Оставить фон'),
                    desc: t('other.layout.keepBgDesc', 'Подложка миникарты остаётся: карта, обозначения и фон под ними.') },
                  { tp: true, title: t('other.layout.makeTransparent', 'Сделать прозрачной'),
                    desc: t('other.layout.makeTransparentDesc', 'Убирает подложку: остаётся только карта с метками и HP-бар поверх, без фона.') },
                ].map(o => (
                  <button
                    key={String(o.tp)}
                    type="button"
                    disabled={layoutBusy}
                    onClick={() => setLayoutTransparent(o.tp)}
                    style={{ outline: 'none' }}
                    className={
                      'flex flex-col gap-1 p-3 rounded-2xl border text-left transition-colors duration-200 ' +
                      (layoutTransparent === o.tp
                        ? 'bg-accent-soft/50 border-transparent'
                        : 'bg-white/[0.03] border-white/[0.08] hover:border-white/[0.16]')
                    }
                  >
                    <span className="text-[12.5px] font-semibold text-text-primary">{o.title}</span>
                    <span className="text-[10.5px] text-text-muted leading-snug">{o.desc}</span>
                  </button>
                ))}
              </div>

              <div className="flex items-center justify-end gap-2.5 pt-1">
                <button
                  type="button"
                  disabled={layoutBusy}
                  onClick={() => setLayoutModalOpen(false)}
                  className="px-4 h-9 rounded-lg bg-white/[0.04] text-text-secondary border border-white/[0.08]
                             hover:text-text-primary hover:bg-white/[0.08] transition-colors
                             text-sm font-bold uppercase tracking-wider disabled:opacity-40"
                  style={{ outline: 'none' }}
                >
                  {t('common.cancel', 'Отмена')}
                </button>
                <motion.button
                  type="button"
                  disabled={layoutBusy}
                  onClick={() => applyLayout(layoutTransparent)}
                  whileHover={layoutBusy ? undefined : { scale: 1.02 }}
                  whileTap={layoutBusy ? undefined : { scale: 0.98 }}
                  className="inline-flex items-center gap-2 px-5 h-9 rounded-lg
                             bg-accent text-text-on-accent border border-transparent
                             hover:brightness-110 transition-all text-sm font-bold uppercase tracking-wider
                             disabled:opacity-40 disabled:cursor-not-allowed"
                  style={{ outline: 'none' }}
                >
                  {layoutBusy ? <Loader2 size={15} className="animate-spin" /> : <Check size={15} strokeWidth={2.6} />}
                  <span>{t('other.layout.apply', 'Применить')}</span>
                </motion.button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 3500}
      />
    </div>
  );
}

function LayoutPill({ active, disabled, label, onClick }: {
  active: boolean; disabled: boolean; label: string; onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={active}
      style={{ outline: 'none' }}
      className={
        'px-3.5 h-8 rounded-lg border text-[11.5px] font-bold uppercase tracking-[0.08em] ' +
        'transition-[background-color,border-color,color] duration-200 disabled:opacity-40 ' +
        (active
          ? 'bg-white/[0.13] border-white/[0.30] text-text-primary ' +
            'shadow-[inset_0_1px_0_rgba(255,255,255,0.10)]'
          : 'bg-white/[0.03] border-white/[0.08] text-text-secondary hover:text-text-primary hover:border-white/[0.16]')
      }
    >
      {label}
    </button>
  );
}
