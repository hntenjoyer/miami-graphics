import { Sparkles, Award } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { ReduxItem } from '@/bridge/types';

interface TagsBadgeProps {
  item: Pick<ReduxItem, 'tagNew' | 'tagBest'>;
  size?: 'sm' | 'md';
  stacked?: boolean;
  className?: string;
}

export function TagsBadge({ item, size = 'sm', stacked = false, className }: TagsBadgeProps) {
  const { t } = useTranslation();

  if (!item.tagNew && !item.tagBest) return null;

  const dim = size === 'md'
    ? { padX: 'px-3.5', padY: 'py-2',   text: 'text-[11px]',  icon: 14, gap: 'gap-1.5' }
    : { padX: 'px-2',   padY: 'py-1',   text: 'text-[10px]',  icon: 11, gap: 'gap-1'   };

  return (
    <div className={
      (stacked ? 'flex flex-col items-start gap-1.5' : 'flex flex-wrap items-center gap-2') +
      (className ? ' ' + className : '')
    }>
      {item.tagNew && (

        <span className={
          'inline-flex items-center rounded-xl uppercase font-bold tracking-wider ' +
          'bg-black/85 text-accent ' +
          'shadow-[0_2px_8px_rgba(0,0,0,0.45)] ' +
          dim.padX + ' ' + dim.padY + ' ' + dim.text + ' ' + dim.gap
        }>
          <Sparkles size={dim.icon} className="shrink-0" />
          {t('redux.tag.new')}
        </span>
      )}
      {item.tagBest && (
        <span className={
          'inline-flex items-center rounded-xl uppercase font-bold tracking-wider ' +
          'bg-accent text-text-on-accent shadow-glow-accent ' +
          dim.padX + ' ' + dim.padY + ' ' + dim.text + ' ' + dim.gap
        }>
          <Award size={dim.icon} className="shrink-0" />
          {t('redux.tag.best')}
        </span>
      )}
    </div>
  );
}
