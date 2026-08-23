import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { useUiStore } from '@/store/uiStore';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useFeaturedStore } from '@/store/featuredStore';
import { useBackupStore } from '@/store/backupStore';
import { EASE_DEPTH } from '@/design';
import { useAppVersion } from '@/appVersion';
import { bridge } from '@/bridge';
import type { LibraryComponent } from '@/bridge';
import { setCachedLibrary, getCachedLibrary } from '@/store/libraryCache';
import { setArmorLibraryCache } from '@/store/armorLibraryCache';
import logoUrl from '@/assets/logo/favicon.png';

let warmupArmorLibrary: import('@/bridge').ArmorLibraryItem[] = [];

let warmupGunPreviewUrls: string[] = [];

interface Props {
  onDone: () => void;
}

interface Step {
  labelKey: string;
  labelRu: string;
  run: (onSub?: (cur: number, total: number) => void) => Promise<void>;
}

function LogoFill({ progress }: { progress: number }) {
  const SIZE = 240;
  const glowOpacity = 0.14 + (progress / 100) * 0.38;

  return (
    <div style={{ position: 'relative', width: SIZE, height: SIZE }}>
      <div style={{
        position: 'absolute', top: '50%', left: '50%',
        transform: 'translate(-50%, -50%)',
        width: SIZE * 1.9, height: SIZE * 1.1,
        borderRadius: '50%',
        background: `radial-gradient(ellipse, rgba(139,36,200,${glowOpacity}) 0%, transparent 70%)`,
        filter: 'blur(40px)', pointerEvents: 'none',
        transition: 'background 0.08s',
      }} />

      <img src={logoUrl} alt="" aria-hidden style={{
        position: 'absolute', inset: 0,
        width: '100%', height: '100%', objectFit: 'contain',
        opacity: 0.13, filter: 'brightness(0) invert(1)', pointerEvents: 'none',
      }} />

      <div style={{
        position: 'absolute', inset: 0,
        WebkitMaskImage: `url(${logoUrl})`,
        WebkitMaskSize: 'contain',
        WebkitMaskRepeat: 'no-repeat',
        WebkitMaskPosition: 'center',
        maskImage: `url(${logoUrl})`,
        maskSize: 'contain',
        maskRepeat: 'no-repeat',
        maskPosition: 'center',
      }}>
        <div style={{
          position: 'absolute', bottom: 0, left: 0, right: 0,
          height: `${progress}%`,
          background: 'linear-gradient(to top, #3d0f7a 0%, #9b35d8 45%, #cb77ef 80%, #e0a8f8 100%)',
          transition: 'height 0.18s cubic-bezier(0.22, 1, 0.36, 1)',
        }} />
      </div>

      <AnimatePresence>
        {progress >= 100 && (
          <motion.div key="flash-png"
            initial={{ opacity: 0 }}
            animate={{ opacity: [0, 0.55, 0] }}
            transition={{ duration: 0.55, times: [0, 0.2, 1] }}
            style={{
              position: 'absolute', inset: 0,
              WebkitMaskImage: `url(${logoUrl})`,
              WebkitMaskSize: 'contain',
              WebkitMaskRepeat: 'no-repeat',
              WebkitMaskPosition: 'center',
              maskImage: `url(${logoUrl})`,
              maskSize: 'contain',
              maskRepeat: 'no-repeat',
              maskPosition: 'center',
              background: 'white',
              pointerEvents: 'none',
            }}
          />
        )}
      </AnimatePresence>
    </div>
  );
}

export function WarmupScreen({ onDone }: Props) {
  const isDemo = typeof document !== 'undefined'
    && document.documentElement.dataset.demo === '1';

  const { t } = useTranslation();
  const appVersion = useAppVersion();
  const [index, setIndex] = useState(0);
  const [labels, setLabels] = useState<Array<{ key: string; ru: string }>>([]);
  const [sub, setSub] = useState<{ cur: number; total: number } | null>(null);
  const [exiting, setExiting] = useState(false);

  useEffect(() => {
    let cancelled = false;

    if (isDemo) {
      (async () => {
        try {
          await Promise.all([
            useUiStore.getState().initialize(),
            useBackupStore.getState().loadStatus(),
            useReduxStore.getState().load(),
            useGunpackStore.getState().loadPublicPacks(),
            useFeaturedStore.getState().load(),
          ]);
        } catch (e) { console.warn('[warmup:demo] load failed', e); }
        if (!cancelled) onDone();
      })();
      return () => { cancelled = true; };
    }

    const steps: Step[] = [
      {
        labelKey: 'warmup.stepBoot',
        labelRu: 'Запускаем приложение…',
        run: async () => {
          await Promise.all([
            useUiStore.getState().initialize(),
            useBackupStore.getState().loadStatus(),
          ]);
        },
      },
      {
        labelKey: 'warmup.stepReduxCatalog',
        labelRu: 'Загружаем каталог редуксов…',
        run: async () => { await useReduxStore.getState().load(); },
      },
      {
        labelKey: 'warmup.stepGunpacks',
        labelRu: 'Готовим оружие и ганпаки…',
        run: async () => { await useGunpackStore.getState().loadPublicPacks(); },
      },
      {
        labelKey: 'warmup.stepFeatured',
        labelRu: 'Тащим рекомендации с сервера…',
        run: async () => { await useFeaturedStore.getState().load(); },
      },
      {
        labelKey: 'warmup.stepLibraries',
        labelRu: 'Подгружаем библиотеки компонентов…',
        run: async () => {
          const kinds = ['minimap', 'crosshair', 'sounds', 'tracers', 'bloodfx', 'timecycle'] as const;
          const results = await Promise.all(kinds.map(async k => {
            const rows: LibraryComponent[] = await bridge.libraryList(k)
              .then(r => r ?? [])
              .catch(() => []);
            return [k, rows] as const;
          }));
          for (const [k, rows] of results) setCachedLibrary(k, rows);
        },
      },
      {
        labelKey: 'warmup.stepArmor',
        labelRu: 'Подгружаем библиотеку брони…',
        run: async () => {
          try {
            const [armorRows, gunUrls] = await Promise.all([
              bridge.armorLibraryList(),
              bridge.gunpackAllGunPreviewUrls(),
            ]);
            warmupArmorLibrary = armorRows ?? [];
            warmupGunPreviewUrls = gunUrls ?? [];
            setArmorLibraryCache(warmupArmorLibrary);
          } catch (e) {
            console.warn('[warmup] armor/gun preview fetch failed', e);
            warmupArmorLibrary = [];
            warmupGunPreviewUrls = [];
          }
        },
      },
      {
        labelKey: 'warmup.stepPreviews',
        labelRu: 'Прогреваем превью карточек…',
        run: async (onSub) => { await prefetchAllPreviews(onSub); },
      },
    ];
    setLabels(steps.map(s => ({ key: s.labelKey, ru: s.labelRu })));

    (async () => {
      for (let i = 0; i < steps.length; i++) {
        if (cancelled) return;
        setIndex(i);
        setSub(null);
        try {
          await steps[i].run((cur, total) => {
            if (!cancelled) setSub({ cur, total });
          });
        }
        catch (e) { console.warn(`[warmup] step ${i} (${steps[i].labelKey}) failed:`, e); }
      }
      if (cancelled) return;
      setIndex(steps.length);
      setSub(null);
      await new Promise(r => setTimeout(r, 600));
      if (cancelled) return;
      setExiting(true);
      await new Promise(r => setTimeout(r, 700));
      if (!cancelled) onDone();
    })();

    return () => { cancelled = true; };

  }, []);

  const totalSteps = labels.length;
  const overall = totalSteps === 0
    ? 0
    : index >= totalSteps
      ? 100
      : sub && sub.total > 0
        ? Math.min(99, Math.floor(((index + sub.cur / sub.total) / totalSteps) * 100))
        : Math.min(99, Math.floor((index / totalSteps) * 100));

  if (isDemo) return null;

  return (
    <motion.div
      className="fixed inset-0 z-[80] flex flex-col items-center justify-center"
      initial={{ opacity: 1, scale: 1 }}
      animate={exiting ? { opacity: 0, scale: 1.04 } : { opacity: 1, scale: 1 }}
      transition={{ duration: 0.65, ease: EASE_DEPTH }}
      style={{
        background: 'radial-gradient(ellipse at center, #14081E 0%, #08050F 70%, #06030B 100%)',
        pointerEvents: exiting ? 'none' : 'auto',
      }}
    >
      <motion.div
        initial={{ opacity: 0, scale: 0.93 }}
        animate={{ opacity: 1, scale: 1 }}
        transition={{ duration: 0.4, ease: EASE_DEPTH }}
        className="flex flex-col items-center"
      >
        <LogoFill progress={overall} />

        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ delay: 0.2 }}
          style={{
            marginTop: 20,
            fontFamily: '"JetBrains Mono", ui-monospace, monospace',
            fontSize: 11,
            fontWeight: 600,
            letterSpacing: '0.12em',
            color: `rgba(203,119,239,${0.3 + (overall / 100) * 0.6})`,
            width: 48,
            textAlign: 'center',
            transition: 'color 0.1s',
            fontVariantNumeric: 'tabular-nums',
          }}
        >
          {overall}%
        </motion.div>

        <div className="mt-6 h-5 relative w-[320px] flex items-center justify-center">
          <AnimatePresence mode="wait">
            <motion.span
              key={index}
              initial={{ opacity: 0, y: 6 }}
              animate={{ opacity: 1, y: 0 }}
              exit   ={{ opacity: 0, y: -6 }}
              transition={{ duration: 0.28, ease: EASE_DEPTH }}
              className="absolute text-[12px] tracking-wide text-text-secondary text-center px-2"
            >
              {index < totalSteps
                ? t(labels[index].key, labels[index].ru)
                : t('warmup.done', 'Готово')}
            </motion.span>
          </AnimatePresence>
        </div>

        <div className="mt-2 h-4">
          <AnimatePresence>
            {sub && sub.total > 0 && (
              <motion.span
                key="sub"
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit   ={{ opacity: 0 }}
                transition={{ duration: 0.18 }}
                className="text-[10px] uppercase tracking-[0.22em] text-text-muted tabular-nums"
              >
                {t('warmup.previewCounter', { defaultValue: '{{cur}} / {{total}} превью', cur: sub.cur, total: sub.total })}
              </motion.span>
            )}
          </AnimatePresence>
        </div>
      </motion.div>

      <span
        className="absolute bottom-8 text-[10px] uppercase tracking-[0.3em] text-text-muted/60"
        style={{ fontFamily: 'inherit' }}
      >
        Miami Graphics · {t('warmup.versionTag', { defaultValue: 'БЕТА {{version}}', version: appVersion })}
      </span>
    </motion.div>
  );
}

async function prefetchAllPreviews(onSub?: (cur: number, total: number) => void): Promise<void> {
  const urls = collectAllPreviewUrls();
  const unique = Array.from(new Set(urls));
  const total = unique.length;
  if (total === 0) { onSub?.(0, 0); return; }

  let cached: boolean[];
  try {
    cached = await bridge.assetCacheContains(unique);
    if (!Array.isArray(cached) || cached.length !== unique.length) {
      cached = unique.map(() => false);
    }
  } catch (e) {
    console.warn('[warmup] assetCacheContains failed, treating all as cold', e);
    cached = unique.map(() => false);
  }

  const cold: string[] = [];
  let warmCount = 0;
  for (let i = 0; i < unique.length; i++) {
    if (cached[i]) warmCount++; else cold.push(unique[i]);
  }
  let done = warmCount;
  onSub?.(done, total);

  if (cold.length === 0) return;

  const SMALL_TAIL = 10;
  let ticker: ReturnType<typeof setInterval> | null = null;
  try {
    ticker = setInterval(() => {
      if (done < total - 1) {
        done++;
        onSub?.(done, total);
      }
    }, 80);

    if (cold.length <= SMALL_TAIL) {
      void bridge.assetCachePrewarm(cold);
    } else {
      const split = Math.floor(cold.length * 0.95);
      const primary = cold.slice(0, split);
      const tail = cold.slice(split);
      await Promise.race([
        bridge.assetCachePrewarm(primary),
        new Promise<void>(res => setTimeout(res, 20_000)),
      ]);
      if (tail.length > 0) void bridge.assetCachePrewarm(tail);
    }
  } finally {
    if (ticker) clearInterval(ticker);
  }
  done = total;
  onSub?.(done, total);
}

function collectAllPreviewUrls(): string[] {
  const out: string[] = [];

  const items = useReduxStore.getState().items;
  for (const it of items) {
    if (it.previewUrl) out.push(it.previewUrl);
    const cs = it.componentScreenshots;
    if (cs) {
      for (const v of Object.values(cs)) {
        if (typeof v === 'string' && v) out.push(v);
      }
    }
  }

  const packs = useGunpackStore.getState().publicPacks;
  for (const p of packs) {
    if (p.coverKind === 'image' && p.coverUrl) out.push(p.coverUrl);
  }

  for (const a of warmupArmorLibrary) {
    if (a.previewUrl) out.push(a.previewUrl);
  }

  for (const u of warmupGunPreviewUrls) {
    if (u) out.push(u);
  }

  out.push(...readLibraryCacheUrls());

  return out;
}

function readLibraryCacheUrls(): string[] {
  const out: string[] = [];
  for (const kind of ['minimap','crosshair','sounds','tracers','bloodfx','timecycle'] as const) {
    const rows = getCachedLibrary(kind) ?? [];
    for (const r of rows) {
      if (r.previewUrl) out.push(r.previewUrl);
    }
  }
  return out;
}
