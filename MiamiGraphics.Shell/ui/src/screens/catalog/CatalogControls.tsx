import { useTranslation } from 'react-i18next';
import { Star, Sparkles, Download as DownloadIcon, ArrowDownAZ } from 'lucide-react';
import clsx from 'clsx';
import { GlassDropdown, type GlassDropdownOption } from '@/design';

export type CatalogSort = 'default' | 'installs' | 'name';

export function CatalogFavToggle({ active, onClick, label }: { active: boolean; onClick: () => void; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={label}
      aria-label={label}
      aria-pressed={active}
      style={{ outline: 'none' }}
      className={clsx(
        'inline-flex items-center justify-center w-10 h-10 rounded-xl border transition-all duration-300 ease-smooth',
        active
          ? 'bg-white text-black border-white shadow-pill-active'
          : 'bg-white/[0.04] border-white/[0.06] text-text-muted hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]',
      )}
    >
      <Star size={15} fill={active ? 'currentColor' : 'none'} strokeWidth={active ? 2 : 1.8} />
    </button>
  );
}

export function CatalogSortSelect({ value, onChange }: { value: CatalogSort; onChange: (v: CatalogSort) => void }) {
  const { t } = useTranslation();
  const options: GlassDropdownOption<CatalogSort>[] = [
    { value: 'default',  label: t('catalog.sort.default', 'По умолчанию'),  icon: Sparkles },
    { value: 'installs', label: t('catalog.sort.installs', 'По установкам'), icon: DownloadIcon },
    { value: 'name',     label: t('catalog.sort.name', 'По алфавиту'),      icon: ArrowDownAZ },
  ];
  return (
    <GlassDropdown<CatalogSort>
      value={value}
      options={options}
      onChange={onChange}
      ariaLabel={t('catalog.sort.label', 'Сортировка')}
      title={t('catalog.sort.label', 'Сортировка')}
      width={200}
    />
  );
}
