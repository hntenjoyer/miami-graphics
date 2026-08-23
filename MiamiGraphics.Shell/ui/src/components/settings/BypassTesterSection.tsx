import { useState } from 'react';
import { useTranslation, Trans } from 'react-i18next';
import { Activity, Check, X, Loader2, Play } from 'lucide-react';
import { SettingsSection } from '@/components/settings/SettingsSection';
import { bridge } from '@/bridge';
import type { BypassTestResult } from '@/bridge/IAppBridge';

export function BypassTesterSection() {
  const { t } = useTranslation();
  const [results, setResults] = useState<Record<number, BypassTestResult | 'running'>>({});
  const [runningAll, setRunningAll] = useState(false);

  const strategies: { id: number; label: string; hint: string }[] = [
    { id: 0,
      label: t('settings.bypassTester.strategy.baseline.label', 'Baseline (без обхода)'),
      hint:  t('settings.bypassTester.strategy.baseline.hint', 'cdn.miamigraphicsstorage.uk напрямую - контроль') },
    { id: 1,
      label: t('settings.bypassTester.strategy.frag3.label', 'TLS fragmentation 3×'),
      hint:  t('settings.bypassTester.strategy.frag3.hint', 'Текущий вариант в проде') },
    { id: 2,
      label: t('settings.bypassTester.strategy.frag8.label', 'TLS fragmentation 8×'),
      hint:  t('settings.bypassTester.strategy.frag8.hint', 'Агрессивная фрагментация SNI (8 частей)') },
    { id: 3,
      label: t('settings.bypassTester.strategy.dohCloudflare.label', 'DoH через Cloudflare (1.1.1.1)'),
      hint:  t('settings.bypassTester.strategy.dohCloudflare.hint', 'Резолвим через cloudflare-dns.com') },
    { id: 4,
      label: t('settings.bypassTester.strategy.dohQuad9.label', 'DoH через Quad9 (9.9.9.9)'),
      hint:  t('settings.bypassTester.strategy.dohQuad9.hint', 'Резолвим через dns.quad9.net - менее известный, чаще проходит') },
    { id: 5,
      label: t('settings.bypassTester.strategy.dohNextdns.label', 'DoH через NextDNS'),
      hint:  t('settings.bypassTester.strategy.dohNextdns.hint', 'Резолвим через anycast.dns.nextdns.io - anycast по миру') },
  ];

  const runOne = async (id: number) => {
    setResults(prev => ({ ...prev, [id]: 'running' }));
    try {
      const r = await bridge.bypassTestRun(id);
      setResults(prev => ({ ...prev, [id]: r }));
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      setResults(prev => ({
        ...prev,
        [id]: {
          strategyId: id,
          strategyLabel: strategies[id]?.label ?? '?',
          targetUrl: '',
          success: false,
          connectMs: 0, firstByteMs: 0, totalMs: 0,
          bytesReceived: 0, kbps: 0, httpStatusCode: 0,
          errorMessage: `Bridge error: ${msg}`,
        },
      }));
    }
  };

  const runAll = async () => {
    setRunningAll(true);
    setResults({});

    for (const s of strategies) {
      await runOne(s.id);
    }
    setRunningAll(false);
  };

  return (
    <SettingsSection
      icon={Activity}
      title={t('settings.bypassTester.title', '🧪 Тест обхода блокировок (временный)')}
      description={t('settings.bypassTester.description', 'Проверяет какие из 5 стратегий DPI bypass проходят через твою сеть. Качаем 256 KB начала installer\'а через каждый метод и меряем время + скорость. Полезно если сидишь в РФ и картинки/файлы не грузятся.')}
    >
      <div className="flex flex-col gap-3 px-4 py-3">
        <button
          type="button"
          onClick={() => void runAll()}
          disabled={runningAll}
          className="inline-flex items-center justify-center gap-2 px-4 py-2 rounded-xl self-start
                     bg-accent-soft text-text-primary border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                     hover:bg-[color-mix(in_srgb,var(--accent)_20%,transparent)] hover:border-accent
                     disabled:opacity-50 disabled:cursor-wait
                     transition-colors text-sm font-bold uppercase tracking-wider"
          style={{ outline: 'none' }}
        >
          {runningAll
            ? <Loader2 size={14} className="animate-spin" />
            : <Play size={14} />}
          <span>{runningAll
            ? t('settings.bypassTester.runningAll', 'Прогоняю все…')
            : t('settings.bypassTester.runAll', 'Прогнать все 5 по очереди')}</span>
        </button>

        <div className="flex flex-col gap-2 mt-2">
          {strategies.map(s => {
            const r = results[s.id];
            const isRunning = r === 'running';
            const data = (typeof r === 'object' && r !== null) ? r : null;

            return (
              <div
                key={s.id}
                className="flex flex-col gap-1.5 px-3 py-2.5 rounded-xl
                           bg-white/[0.03] border border-white/[0.06]"
              >
                <div className="flex items-center gap-3">
                  {}
                  <span className="shrink-0 w-7 h-7 rounded-lg flex items-center justify-center"
                        style={{
                          background:
                            data?.success ? 'color-mix(in srgb, var(--status-success) 18%, transparent)' :
                            data && !data.success ? 'color-mix(in srgb, var(--status-error) 18%, transparent)' :
                            'var(--accent-soft)',
                        }}>
                    {isRunning
                      ? <Loader2 size={14} className="animate-spin text-accent" />
                      : data?.success
                        ? <Check size={14} style={{ color: 'var(--status-success)' }} />
                        : data && !data.success
                          ? <X size={14} style={{ color: 'var(--status-error)' }} />
                          : <span className="text-[11px] font-bold text-text-muted">{s.id + 1}</span>}
                  </span>

                  {}
                  <div className="flex-1 min-w-0">
                    <div className="text-[13px] font-semibold text-text-primary leading-tight">
                      {s.label}
                    </div>
                    <div className="text-[11px] text-text-muted leading-tight mt-0.5 truncate">
                      {s.hint}
                    </div>
                  </div>

                  {}
                  <button
                    type="button"
                    onClick={() => void runOne(s.id)}
                    disabled={isRunning || runningAll}
                    className="shrink-0 inline-flex items-center justify-center gap-1.5
                               px-3 h-8 rounded-lg text-[11px] font-bold uppercase tracking-wider
                               bg-white/[0.05] text-text-primary border border-white/[0.08]
                               hover:bg-white/[0.10] hover:border-white/[0.18]
                               disabled:opacity-40 disabled:cursor-not-allowed
                               transition-colors"
                    style={{ outline: 'none' }}
                  >
                    {t('settings.bypassTester.run', 'Тест')}
                  </button>
                </div>

                {}
                {data && (
                  <div className="ml-10 text-[11px] font-mono leading-snug">
                    {data.success ? (
                      <span className="text-text-secondary">
                        <span className="text-status-success font-semibold">OK</span>
                        {' · '}HTTP {data.httpStatusCode}
                        {' · '}connect {data.connectMs} ms
                        {' · '}total {data.totalMs} ms
                        {' · '}<span className="tabular-nums">{Math.round(data.kbps)}</span> KB/s
                        {' · '}<span className="tabular-nums">{Math.round(data.bytesReceived / 1024)}</span> KB
                      </span>
                    ) : (
                      <span className="text-status-error">
                        FAIL{data.httpStatusCode ? ` · HTTP ${data.httpStatusCode}` : ''}
                        {' · '}<span className="text-text-muted">{data.errorMessage}</span>
                      </span>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>

        <div className="mt-2 text-[11px] text-text-muted leading-relaxed">
          <Trans
            i18nKey="settings.bypassTester.howToRead"
            defaults="<s>Как читать:</s> OK = стратегия работает. FAIL + Timeout = DPI режет. <br/>• <b>Baseline OK</b> → обход не нужен, ты не в РФ DPI-зоне. <br/>• <b>TLS frag 3×/8× OK</b> → текущий прод покрывает тебя, fragmentation хватает. <br/>• <b>Только DoH OK</b> → у тебя DNS poisoning от провайдера, нужно проксировать через DoH. Скажи нам через какого провайдера (Cloudflare / Quad9 / NextDNS) у тебя сработало - добавим его как fallback в прод-handler. <br/>• <b>ВСЁ FAIL</b> → провайдер режет на IP-уровне; нужен внешний обход (VPN и т.п.)."
            components={{
              s: <strong className="text-text-secondary" />,
              b: <strong />,
              br: <br />,
            }}
          />
        </div>
      </div>
    </SettingsSection>
  );
}
