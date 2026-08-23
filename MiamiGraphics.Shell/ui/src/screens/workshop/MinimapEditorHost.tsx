import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { MinimapTweaks } from '@/bridge/types';
import { bridge } from '@/bridge';
import { useBackupStore } from '@/store/backupStore';
import { MinimapTweaksEditor, type MinimapLayout } from '@/screens/other/MinimapTweaksEditor';
import { Toast, type ToastTone } from '@/components/Toast';

const RING_PRESETS = [100, 125] as const;

interface Props {
  onClose: () => void;
}

export function MinimapEditorHost({ onClose }: Props) {
  const { t } = useTranslation();
  const ensureBackupOrGate = useBackupStore(s => s.ensureBackupOrGate);

  const [tweaks, setTweaks] = useState<MinimapTweaks | null>(null);
  const [layout, setLayout] = useState<{ ratio: string; posX: number | null; posY: number | null; transparent: boolean }>(
    { ratio: '16:9', posX: null, posY: null, transparent: false });
  const [rings, setRings] = useState<{ on: boolean; external: boolean }>({ on: false, external: false });
  const [loaded, setLoaded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<{ tone: ToastTone; message: string } | null>(null);

  useEffect(() => {
    let alive = true;
    void (async () => {
      const [tw, lay, ringsDetected, ringsState] = await Promise.all([
        Promise.resolve(bridge.minimapGetTweaks?.()).catch(() => null),
        bridge.minimapLayoutGet().catch(() => null),
        bridge.minimapDetectRings().catch(() => false),
        bridge.minimapGetRangeRings().catch(() => [] as number[]),
      ]);
      if (!alive) return;
      if (tw) setTweaks(tw);
      if (lay) {
        setLayout({
          ratio: lay.ratio,
          posX: lay.posX ?? null,
          posY: lay.posY ?? null,
          transparent: !!lay.transparent,
        });
      }
      setRings({ on: !!ringsDetected, external: !!ringsDetected && (ringsState?.length ?? 0) === 0 });
      setLoaded(true);
    })();
    return () => { alive = false; };
  }, []);

  const onApply = async (tw: MinimapTweaks, lay: MinimapLayout | null, ringsWish: boolean | null) => {
    if (busy) return;
    if (!ensureBackupOrGate()) return;
    setBusy(true);
    try {
      const r = await bridge.minimapApplyTweaks(tw);
      if (!r.success) {
        setToast({ tone: 'error', message: r.errorMessage ?? t('workshop.minimap.applyFail', 'Не удалось применить настройки миникарты.') });
        return;
      }
      setTweaks(tw);

      if (lay) {
        const rl = await bridge.minimapLayoutApplyCustom(lay.ratio, lay.posX, lay.posY, lay.transparent);
        if (!rl.success) {
          setToast({ tone: 'error', message: rl.errorMessage ?? t('workshop.minimap.layoutFail', 'Настройки применены, но позицию не удалось.') });
          return;
        }
        setLayout({ ratio: lay.ratio, posX: lay.posX, posY: lay.posY, transparent: lay.transparent });
      }

      const wanted = ringsWish ?? rings.on;
      if (wanted || ringsWish === false) {
        const rr = await bridge.minimapSetRangeRings(wanted ? [...RING_PRESETS] : []);
        if (!rr.success) {
          setToast({ tone: 'error', message: rr.errorMessage ?? t('workshop.minimap.ringsFail', 'Настройки применены, но круги не удалось.') });
          return;
        }
        setRings({ on: wanted, external: wanted ? rings.external : false });
      }

      setToast({ tone: 'success', message: t('workshop.minimap.updated', 'Миникарта обновлена.') });
      onClose();
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <MinimapTweaksEditor
        open={loaded}
        initial={tweaks}
        initialLayout={layout}
        initialRings={rings}
        busy={busy}
        onClose={onClose}
        onApply={onApply}
      />
      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
      />
    </>
  );
}
