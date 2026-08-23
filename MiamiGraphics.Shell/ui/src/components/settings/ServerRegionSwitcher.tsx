import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { SegmentedControl, type SegmentedOption } from './SegmentedControl';
import { bridge } from '@/bridge';
import type { ServerRegionPing } from '@/bridge/IAppBridge';
import { useRegionStore, type RegionCode } from '@/store/regionStore';

export function ServerRegionSwitcher() {
  const { t } = useTranslation();
  const region    = useRegionStore(s => s.region);
  const loadRegion = useRegionStore(s => s.load);
  const setRegion  = useRegionStore(s => s.setRegion);
  const [ping,    setPing]    = useState<ServerRegionPing | null>(null);
  const [pinging, setPinging] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        await loadRegion();
        const measured = await bridge.serverRegionPing();
        if (!cancelled) setPing(measured);
      } catch (err) {
        console.warn('[ServerRegionSwitcher] mount load failed', err);
        if (!cancelled) setPing({ euMs: null, ruMs: null });
      } finally {
        if (!cancelled) setPinging(false);
      }
    })();
    return () => { cancelled = true; };
  }, [loadRegion]);

  const change = async (next: RegionCode) => {
    if (next === region) return;
    await setRegion(next);
  };

  const options: SegmentedOption<RegionCode>[] = [
    { value: 'eu', label: t('settings.serverRegion.eu') },
    { value: 'ru', label: t('settings.serverRegion.ru') },
  ];

  return (
    <div className="flex flex-col items-end gap-2">
      <SegmentedControl<RegionCode>
        options={options}
        value={region ?? 'eu'}
        onChange={change}
        ariaLabel={t('settings.serverRegion.label')}
      />
      {}
      <div className="text-[10px] font-mono uppercase tracking-[0.14em] text-text-muted">
        EU&nbsp;
        <span style={{ color: pingColor(ping?.euMs, pinging) }}>
          {pinging ? '…' : ping?.euMs === null ? '-' : `${ping?.euMs}ms`}
        </span>
        <span className="opacity-40 mx-1.5">/</span>
        RU&nbsp;
        <span style={{ color: pingColor(ping?.ruMs, pinging) }}>
          {pinging ? '…' : ping?.ruMs === null ? '-' : `${ping?.ruMs}ms`}
        </span>
      </div>
    </div>
  );
}

function pingColor(ms: number | null | undefined, pinging: boolean) {
  if (pinging)        return 'rgba(255,255,255,0.4)';
  if (ms === null)    return 'rgba(255,180,180,0.85)';
  if (ms === undefined) return 'rgba(255,255,255,0.4)';
  if (ms < 80)        return 'rgba(110,231,183,0.95)';
  if (ms < 200)       return 'rgba(255,255,255,0.85)';
  return 'rgba(255,210,150,0.95)';
}
