import { useTranslation } from 'react-i18next';
import { Sun, Moon } from 'lucide-react';
import { useTheme, type ThemeMode } from '@/contexts/SettingsContext';
import { SegmentedControl, type SegmentedOption } from './SegmentedControl';

export function ThemeSwitcher() {
  const { t } = useTranslation();
  const { theme, setTheme } = useTheme();

  const options: SegmentedOption<ThemeMode>[] = [
    { value: 'light', label: t('settings.theme.light'), icon: Sun },
    { value: 'dark',  label: t('settings.theme.dark'),  icon: Moon },
  ];

  return (
    <SegmentedControl<ThemeMode>
      options={options}
      value={theme}
      onChange={setTheme}
      ariaLabel={t('settings.theme.ariaLabel')}
    />
  );
}
