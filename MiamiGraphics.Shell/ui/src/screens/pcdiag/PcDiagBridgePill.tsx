import { Gauge, AlertTriangle } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useNavStore } from '@/store/navStore';
import { usePcDiagStore } from '@/store/pcDiagStore';

interface PcDiagBridgePillProps {
  floating?: boolean;
}

export function PcDiagBridgePill({ floating = true }: PcDiagBridgePillProps) {
  const { t } = useTranslation();
  const report = usePcDiagStore(s => s.report);
  const requestNavigate = useNavStore(s => s.requestNavigate);

  const critical = report?.findings.filter(f => f.severity === 'Critical').length ?? 0;
  const alarming = critical > 0;

  return (
    <button
      type="button"
      onClick={() => requestNavigate('pcdiag')}
      style={{ outline: 'none' }}
      className={
        'inline-flex items-center gap-1.5 h-9 px-3.5 rounded-xl border ' +
        'text-[11.5px] font-bold uppercase tracking-[0.12em] ' +
        'transition-all duration-300 ease-smooth ' +
        (floating ? 'backdrop-blur-md ' : '') +
        (alarming
          ? 'bg-amber-500/20 border-amber-400/40 text-amber-200 hover:bg-amber-500/30'
          : floating
            ? 'bg-black/55 border-white/[0.14] text-white/75 hover:bg-black/70 hover:text-white hover:border-white/30'
            : 'bg-white/[0.04] border-white/[0.12] text-white/70 hover:bg-white/[0.08] hover:text-white hover:border-white/25')
      }
    >
      {alarming ? <AlertTriangle size={13} /> : <Gauge size={13} />}
      {alarming
        ? t('pcdiag.bridge.critical', 'ПК: критичных {{n}}', { n: critical })
        : report
          ? t('pcdiag.bridge.open', 'Диагностика ПК')
          : t('pcdiag.bridge.check', 'Проверить компьютер')}
    </button>
  );
}
