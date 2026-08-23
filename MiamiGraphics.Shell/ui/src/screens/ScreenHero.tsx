import { type ReactNode, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Search, X } from 'lucide-react';
import { useNavStore } from '@/store/navStore';

interface ScreenHeroProps {
  title: string;
  subtitle?: string;
  search?: {
    value: string;
    onChange: (next: string) => void;
    placeholder: string;
    clearLabel?: string;
  };
  trailing?: ReactNode;
}

export function ScreenHero({ title, subtitle, search, trailing }: ScreenHeroProps) {
  const { t } = useTranslation();
  const setHint = useNavStore(s => s.setSectionHint);
  useEffect(() => {
    setHint({ title, subtitle: subtitle ?? null });
    return () => setHint(null);
  }, [title, subtitle, setHint]);

  return (
    <div className="flex items-center gap-3 flex-wrap shrink-0">
      {search && (
        <label
          className="group relative flex items-center flex-1 min-w-[180px] max-w-[300px] h-10 rounded-xl
                     bg-white/[0.04] border border-white/[0.06]
                     transition-[background-color,border-color,box-shadow] duration-300 ease-smooth
                     hover:bg-white/[0.07] hover:border-white/[0.12]
                     focus-within:bg-white/[0.07] focus-within:border-white/[0.30]
                     focus-within:shadow-[0_0_0_3px_rgba(255,255,255,0.08)]"
        >
          <Search
            size={14}
            strokeWidth={2}
            className="absolute left-3.5 text-text-muted pointer-events-none
                       group-focus-within:text-text-primary transition-colors"
          />
          <input
            type="text"
            value={search.value}
            onChange={(e) => search.onChange(e.target.value)}
            placeholder={search.placeholder}
            className="w-full h-full pl-10 pr-9 bg-transparent rounded-xl
                       text-[13px] text-text-primary placeholder:text-text-muted
                       outline-none"
          />
          {search.value && (
            <button
              type="button"
              onClick={() => search.onChange('')}
              className="absolute right-1.5 w-7 h-7 rounded-md flex items-center justify-center
                         text-text-muted hover:text-text-primary hover:bg-white/[0.10]
                         transition-colors"
              aria-label={search.clearLabel ?? t('common.clearSearch', 'Очистить поиск')}
              style={{ outline: 'none' }}
            >
              <X size={12} />
            </button>
          )}
        </label>
      )}

      {trailing && (
        <div className="flex items-center gap-2 ml-auto">
          {trailing}
        </div>
      )}
    </div>
  );
}
