import { clsx } from 'clsx';
import { useTranslation } from 'react-i18next';

interface AccentLoaderProps {
  size?:      number;
  color?:     string;
  className?: string;
}

export function AccentLoader({ size = 20, color, className }: AccentLoaderProps) {
  const { t } = useTranslation();

  const dotR  = Math.max(2, size * 0.10);
  const ringR = 20;

  return (
    <span
      role="status"
      aria-label={t('common.loading', 'Загрузка')}
      className={clsx('accent-loader', className)}
      style={{
        width: size,
        height: size,
        ...(color ? { color } : null),
      }}
    >
      <svg viewBox="0 0 50 50">
        {}
        <circle
          className="accent-loader-ring"
          cx="25" cy="25" r={ringR}
          fill="none"
          stroke="currentColor"
          strokeWidth="1.4"
        />

        {}
        <g className="accent-loader-orbit">
          {}
          <circle className="accent-loader-dot" cx="25" cy="5" r={dotR}
                  fill="currentColor" fillOpacity="0.95" />
          {}
          <circle className="accent-loader-dot" cx="42.32" cy="35" r={dotR * 0.85}
                  fill="currentColor" fillOpacity="0.55" />
          {}
          <circle className="accent-loader-dot" cx="7.68" cy="35" r={dotR * 0.7}
                  fill="currentColor" fillOpacity="0.30" />
        </g>
      </svg>
    </span>
  );
}
