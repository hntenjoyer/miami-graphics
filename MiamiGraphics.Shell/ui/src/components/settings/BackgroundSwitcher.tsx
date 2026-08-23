import { useTranslation } from 'react-i18next';
import { Boxes, Sparkles, ZapOff } from 'lucide-react';
import { useBackground } from '@/contexts/SettingsContext';
import { SegmentedControl, type SegmentedOption } from './SegmentedControl';
import type { Background } from '@/bridge/types';

export function BackgroundSwitcher() {
  const { t } = useTranslation();
  const { background, setBackground } = useBackground();

  const value: Background = background === 'grid' ? 'aurora' : background;

  const options: SegmentedOption<Background>[] = [
    { value: 'cubes',  label: t('firstRun.bg.cubes'),  icon: Boxes    },
    { value: 'aurora', label: t('firstRun.bg.aurora'), icon: Sparkles },
    { value: 'off',    label: t('firstRun.bg.off'),    icon: ZapOff   },
  ];

  return (
    <SegmentedControl<Background>
      options={options}
      value={value}
      onChange={(bg) => { void setBackground(bg); }}
      ariaLabel={t('settings.appearance.backgroundAriaLabel')}
    />
  );
}
