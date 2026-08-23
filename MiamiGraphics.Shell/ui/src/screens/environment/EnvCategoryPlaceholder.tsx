import { useTranslation } from 'react-i18next';
import { Route, Trees, Shapes, type LucideIcon } from 'lucide-react';
import { useNavStore } from '@/store/navStore';
import { BackButton } from '@/components/BackButton';

export type EnvKind = 'roads' | 'trees' | 'other';

const META: Record<EnvKind, {
  icon: LucideIcon;
  title: [string, string];
  subtitle: [string, string];
}> = {
  roads: { icon: Route,    title: ['environment.roads', 'Дороги'],  subtitle: ['environment.roadsSub', 'Покрытие и разметка'] },
  trees: { icon: Trees,    title: ['environment.trees', 'Деревья'], subtitle: ['environment.treesSub', 'Растительность и листва'] },
  other: { icon: Shapes, title: ['environment.other', 'Разное'], subtitle: ['environment.otherSub', 'Прочие настройки'] },
};

export function EnvCategoryPlaceholder({ kind }: { kind: EnvKind }) {
  const { t } = useTranslation();
  const navigate = useNavStore(s => s.requestNavigate);
  const meta = META[kind];
  const Icon = meta.icon;

  return (
    <div className="h-full flex flex-col">
      <header className="px-8 pt-6 pb-3 shrink-0">
        <div className="max-w-[1280px] mx-auto flex items-center gap-3">
          <BackButton onClick={() => navigate('environment')} label={t('environment.back', 'К окружению')} />
          <h1 className="text-[20px] font-semibold tracking-tight text-text-primary flex items-center gap-2">
            <Icon size={18} className="text-accent" />
            {t(meta.title[0], meta.title[1])}
          </h1>
        </div>
      </header>

      <div className="flex-1 flex flex-col items-center justify-center gap-4 px-8 text-text-muted">
        <div className="w-16 h-16 rounded-2xl bg-white/[0.04] border border-white/[0.06] flex items-center justify-center">
          <Icon size={28} strokeWidth={1.4} className="text-text-secondary" />
        </div>
        <div className="text-center">
          <div className="text-[15px] font-semibold text-text-secondary">
            {t('environment.soonTitle', 'Раздел в разработке')}
          </div>
          <p className="mt-1 text-[13px] max-w-sm">
            {t(meta.subtitle[0], meta.subtitle[1])}
          </p>
        </div>
      </div>
    </div>
  );
}
