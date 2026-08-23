import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { SegmentedControl, type SegmentedOption } from './SegmentedControl';
import { bridge } from '@/bridge';
import type { DownloadSourceEval } from '@/bridge/IAppBridge';
import { useDownloadSourceStore, type DownloadSourceCode } from '@/store/downloadSourceStore';

function getZapretPath(): string | null {
  try { return localStorage.getItem('hntgraph.zapretPath'); } catch { return null; }
}

export function DownloadSourceSwitcher() {
  const { t } = useTranslation();
  const source   = useDownloadSourceStore(s => s.source);
  const loaded   = useDownloadSourceStore(s => s.loaded);
  const load     = useDownloadSourceStore(s => s.load);
  const setSource = useDownloadSourceStore(s => s.setSource);

  const [checking, setChecking] = useState(false);
  const [evalRes,  setEvalRes]  = useState<DownloadSourceEval | null>(null);

  useEffect(() => { if (!loaded) load(); }, [loaded, load]);

  const change = async (next: DownloadSourceCode) => {
    setEvalRes(null);
    if (next === 'eu') { await setSource('eu'); return; }
    setChecking(true);
    try {
      const r = await bridge.downloadSourceEvaluateEu(getZapretPath());
      setEvalRes(r);
      if (!r.euWorks) await setSource('ru2');
    } catch (err) {
      console.error('[DownloadSourceSwitcher] eval failed', err);
      await setSource('ru2');
    } finally {
      setChecking(false);
    }
  };

  const forceRu = async () => { setEvalRes(null); await setSource('ru2'); };
  const keepEu  = async () => { setEvalRes(null); await setSource('eu'); };

  const options: SegmentedOption<DownloadSourceCode>[] = [
    { value: 'eu',  label: t('settings.downloadSource.eu') },
    { value: 'ru2', label: t('settings.downloadSource.ru') },
  ];

  const mbps = (evalRes?.mbps ?? 0).toFixed(1);

  return (
    <div className="flex flex-col items-end gap-2">
      <SegmentedControl<DownloadSourceCode>
        options={options}
        value={source}
        onChange={change}
        ariaLabel={t('settings.downloadSource.label')}
      />

      {checking && (
        <div className="text-[10px] font-mono uppercase tracking-[0.14em] text-text-muted">
          {t('settings.downloadSource.checking')}
        </div>
      )}

      {!checking && evalRes?.euWorks && (
        <div className="flex flex-col items-end gap-1.5 max-w-[280px] text-right">
          <div className="text-[11px] leading-snug text-text-muted">
            {t('settings.downloadSource.euRecommended', { mbps })}
          </div>
          <div className="flex gap-2">
            <button
              onClick={keepEu}
              className="text-[11px] px-2.5 py-1 rounded-md border border-emerald-400/40 bg-emerald-400/10 text-emerald-200 hover:bg-emerald-400/20 transition-colors"
            >
              {t('settings.downloadSource.keepEu')}
            </button>
            <button
              onClick={forceRu}
              className="text-[11px] px-2.5 py-1 rounded-md border border-white/15 text-text-muted hover:bg-white/5 transition-colors"
            >
              {t('settings.downloadSource.forceRu')}
            </button>
          </div>
        </div>
      )}

      {!checking && evalRes && !evalRes.euWorks && (
        <div className="text-[11px] leading-snug text-right max-w-[280px] text-amber-200/90">
          {t('settings.downloadSource.switchedRu', { mbps })}
        </div>
      )}

      {!checking && !evalRes && source === 'ru2' && (
        <div className="text-[10px] font-mono uppercase tracking-[0.14em] text-amber-200/70">
          {t('settings.downloadSource.queueNote')}
        </div>
      )}
    </div>
  );
}
