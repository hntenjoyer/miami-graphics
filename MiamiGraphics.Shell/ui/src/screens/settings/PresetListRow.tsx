import type { GtaPreset } from '@/bridge/types';

interface Props {
  preset:     GtaPreset;
  active:     boolean;
  installed:  boolean;
  onSelect:   () => void;
}

export function PresetListRow({ preset, active, installed, onSelect }: Props) {
  const hasFps = preset.expectedFpsLow !== null && preset.expectedFpsHigh !== null;
  return (
    <button
      type="button"
      onClick={onSelect}
      style={{ outline: 'none' }}
      className={
        'group w-full text-left px-4 py-3 rounded-lg border ' +
        'transition-[background-color,border-color,box-shadow] duration-300 ease-depth ' +
        (active
          ? 'bg-bg-elevated border-white/[0.16] ' +
            'shadow-[inset_0_1px_0_rgba(255,255,255,0.06),0_8px_22px_-12px_rgba(0,0,0,0.6)]'
          : 'border-transparent hover:bg-white/[0.04] hover:border-white/[0.10]')
      }
    >
      <div className="flex items-center gap-3">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <span className="text-[14px] font-semibold tracking-tight truncate text-text-primary">
              {preset.name}
            </span>
            {}
            {installed && (
              <span
                aria-label="Установлено"
                title="Установлено"
                className="shrink-0 w-1.5 h-1.5 rounded-full bg-status-success"
              />
            )}
            {}
            {preset.isTournament && (
              <span
                aria-label="Турнирный"
                title="Турнирный"
                className="shrink-0 text-[10px] text-text-muted tracking-wide"
              >
                ★
              </span>
            )}
          </div>
          {preset.author && (
            <div className="mt-0.5 text-[12px] text-text-muted truncate">
              {preset.author}
            </div>
          )}
        </div>
        {hasFps ? (
          <span
            className={
              'shrink-0 text-[13px] font-semibold tabular-nums ' +
              (active ? 'text-text-primary' : 'text-text-secondary')
            }
          >
            {preset.expectedFpsLow}–{preset.expectedFpsHigh}
            <span className="ml-1 text-[10px] font-medium text-text-muted uppercase tracking-wide">fps</span>
          </span>
        ) : null}
      </div>
    </button>
  );
}
