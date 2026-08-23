import { useTranslation } from 'react-i18next';
import { useLanguage } from '@/contexts/SettingsContext';
import { SegmentedControl, type SegmentedOption } from './SegmentedControl';
import type { Language } from '@/bridge/types';

export function LanguageSwitcher() {
  const { t } = useTranslation();
  const { language, setLanguage } = useLanguage();

  const options: SegmentedOption<Language>[] = [
    { value: 'ru', label: t('settings.language.ru') },
    { value: 'en', label: t('settings.language.en') },
    { value: 'pl', label: t('settings.language.pl') },
  ];

  return (
    <SegmentedControl<Language>
      options={options}
      value={language}
      onChange={(lang) => { void setLanguage(lang); }}
      ariaLabel={t('settings.language.ariaLabel')}
    />
  );
}
