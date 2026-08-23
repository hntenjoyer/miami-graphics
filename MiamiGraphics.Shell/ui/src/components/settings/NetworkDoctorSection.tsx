import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { Stethoscope, Loader2, Play, Check } from 'lucide-react';
import { SettingsSection } from '@/components/settings/SettingsSection';
import { bridge } from '@/bridge';
import type { NetworkDoctorReport, NetworkDoctorNode } from '@/bridge/IAppBridge';

function fmtSpeed(kbPerSec: number, t: TFunction): string {
  if (kbPerSec <= 0) return '-';
  return kbPerSec >= 1024
    ? t('settings.networkDoctor.speedMb', { defaultValue: '{{v}} МБ/с', v: (kbPerSec / 1024).toFixed(1) })
    : t('settings.networkDoctor.speedKb', { defaultValue: '{{v}} КБ/с', v: Math.round(kbPerSec) });
}

function NodeRow({ n, index }: { n: NetworkDoctorNode; index: number }) {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-1 px-3 py-2.5 rounded-xl bg-white/[0.03] border border-white/[0.06]">
      <div className="flex items-center gap-3">
        <span
          className="shrink-0 w-7 h-7 rounded-lg flex items-center justify-center"
          style={{ background: 'color-mix(in srgb, var(--status-success) 18%, transparent)' }}
        >
          <Check size={14} style={{ color: 'var(--status-success)' }} />
        </span>

        <div className="flex-1 min-w-0">
          <div className="text-[13px] font-semibold text-text-primary leading-tight">
            {t('settings.networkDoctor.nodeNumbered', { defaultValue: 'Сервер {{n}}', n: index + 1 })}
          </div>
        </div>

        <div className="shrink-0 text-right">
          <div className="text-[13px] font-bold tabular-nums text-text-primary">
            {fmtSpeed(n.kbPerSec, t)}
          </div>
          <div className="text-[10px] text-text-muted tabular-nums">
            {t('settings.networkDoctor.ttfbMs', { defaultValue: '{{ms}} мс', ms: n.ttfbMs })}
          </div>
        </div>
      </div>

      <div className="ml-10 text-[11px] font-mono leading-snug text-text-secondary">
        <span>{t('settings.networkDoctor.streams', { defaultValue: 'соединений {{n}}/8', n: n.streamsAccepted })}</span>
        {' · '}
        <span>{n.rangeOk
          ? t('settings.networkDoctor.rangeOk', 'куски файла отдаёт')
          : t('settings.networkDoctor.rangeFail', 'куски файла НЕ отдаёт')}</span>
      </div>
    </div>
  );
}

export function NetworkDoctorSection() {
  const { t } = useTranslation();
  const [report, setReport] = useState<NetworkDoctorReport | null>(null);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const handler = (payload: NetworkDoctorReport) => {
      setReport(payload);
      setRunning(false);
    };
    bridge.events.on('download:diagnosis', handler);
    return () => bridge.events.off('download:diagnosis', handler);
  }, []);

  const liveNodes = report ? report.nodes.filter(n => n.ok) : [];
  const bestIndex = liveNodes.reduce(
    (best, n, i) => (n.kbPerSec > (liveNodes[best]?.kbPerSec ?? -1) ? i : best), 0);

  const run = async () => {
    setRunning(true);
    setError(null);
    try {
      setReport(await bridge.networkDoctorRun(null));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setRunning(false);
    }
  };

  return (
    <SettingsSection
      icon={Stethoscope}
      title={t('settings.networkDoctor.title', 'Проверка загрузок')}
      description={t('settings.networkDoctor.description', 'Прогоняет все наши сервера и показывает, почему мод не качается: где режет провайдер, какой сервер отдаёт быстрее всех и не упирается ли скачивание в лимиты. Запусти сразу после неудачной загрузки, тогда проверится именно тот мод.')}
    >
      <div className="flex flex-col gap-3 px-4 py-3">
        <button
          type="button"
          onClick={() => void run()}
          disabled={running}
          className="inline-flex items-center justify-center gap-2 px-4 py-2 rounded-xl self-start
                     bg-accent-soft text-text-primary border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                     hover:bg-[color-mix(in_srgb,var(--accent)_20%,transparent)] hover:border-accent
                     disabled:opacity-50 disabled:cursor-wait
                     transition-colors text-sm font-bold uppercase tracking-wider"
          style={{ outline: 'none' }}
        >
          {running ? <Loader2 size={14} className="animate-spin" /> : <Play size={14} />}
          <span>{running
            ? t('settings.networkDoctor.runningButton', 'Проверяю сервера…')
            : t('settings.networkDoctor.runButton', 'Проверить загрузки')}</span>
        </button>

        {running && (
          <div className="text-[11px] text-text-muted">
            {t('settings.networkDoctor.runningHint', 'Это занимает до минуты: каждый сервер проверяется на скорость, на куски файла и на то, сколько соединений он пускает.')}
          </div>
        )}

        {error && (
          <div className="text-[12px] text-status-error">
            {t('settings.networkDoctor.startFailed', { defaultValue: 'Тест не запустился: {{error}}', error })}
          </div>
        )}

        {report && (
          <div className="flex flex-col gap-3 mt-1">
            <div
              className="px-3 py-2.5 rounded-xl text-[12px] leading-relaxed"
              style={{
                background: liveNodes.length > 0
                  ? 'color-mix(in srgb, var(--status-success) 12%, transparent)'
                  : 'color-mix(in srgb, var(--accent) 12%, transparent)',
              }}
            >
              <div className="font-semibold text-text-primary">
                {liveNodes.length === 0
                  ? t('settings.networkDoctor.summaryNone', 'Ни один сервер не ответил - похоже, дело в интернете или в VPN.')
                  : t('settings.networkDoctor.summaryOk', {
                      defaultValue: 'Проверено серверов: {{count}}. Быстрее всех - Сервер {{best}} ({{speed}}).',
                      count: liveNodes.length,
                      best: bestIndex + 1,
                      speed: fmtSpeed(liveNodes[bestIndex]?.kbPerSec ?? 0, t),
                    })}
              </div>
            </div>

            <div className="flex flex-col gap-2">
              {liveNodes.map((n, i) => <NodeRow key={n.id} n={n} index={i} />)}
            </div>

            <button
              type="button"
              onClick={() => void navigator.clipboard.writeText(JSON.stringify(report, null, 2))}
              className="self-start px-3 h-8 rounded-lg text-[11px] font-bold uppercase tracking-wider
                         bg-white/[0.05] text-text-primary border border-white/[0.08]
                         hover:bg-white/[0.10] hover:border-white/[0.18] transition-colors"
              style={{ outline: 'none' }}
            >
              {t('settings.networkDoctor.copyReport', 'Скопировать отчёт')}
            </button>
          </div>
        )}
      </div>
    </SettingsSection>
  );
}
