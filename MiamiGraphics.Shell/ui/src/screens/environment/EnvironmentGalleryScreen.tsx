import { useTranslation } from 'react-i18next';
import { CloudSun, Route, Trees, Fuel, Shapes } from 'lucide-react';
import { useNavStore } from '@/store/navStore';
import {
  ExpandableGallery,
  type GallerySlide,
} from '@/components/ui/gallery-animation';

export function EnvironmentGalleryScreen() {
  const { t } = useTranslation();
  const navigate = useNavStore(s => s.requestNavigate);

  const IMG = 'https://miamigraphicsstorage.uk/environment';

  const slides: (GallerySlide & { target: string })[] = [
    {
      key: 'timecycles',
      target: 'timecycles',
      title: t('environment.timecycles', 'Небо'),
      subtitle: t('environment.timecyclesSub', 'Небо, погода и атмосфера'),
      icon: CloudSun,
      image: `${IMG}/nebo.webp`,
    },
    {
      key: 'roads',
      target: 'env-roads',
      title: t('environment.roads', 'Дороги'),
      subtitle: t('environment.roadsSub', 'Покрытие и разметка'),
      icon: Route,
      image: `${IMG}/roads.webp`,
    },
    {
      key: 'trees',
      target: 'env-trees',
      title: t('environment.trees', 'Деревья'),
      subtitle: t('environment.treesSub', 'Растительность и листва'),
      icon: Trees,
      image: `${IMG}/tree.webp`,
    },
    {
      key: 'improvements',
      target: 'improvements',
      title: t('environment.improvements', 'Заправки'),
      subtitle: t('environment.improvementsSub', 'Заправки и цветочные поля'),
      icon: Fuel,
      image: 'https://cdn.miamigraphicsstorage.uk/improvements/ls2_gasstation/preview.webp',
    },
    {
      key: 'misc',
      target: 'improvements-misc',
      title: t('environment.miscTitle', 'Разное'),
      subtitle: t('environment.miscSub', 'Растительность, уличный свет и прочее'),
      icon: Shapes,
      image: `${IMG}/misc.webp`,
    },
  ];

  return (
    <div className="h-full flex flex-col">
      <div className="max-w-[1760px] w-full mx-auto px-8 pt-10 pb-10 flex-1 min-h-0 flex flex-col gap-8">
        <header className="min-w-0 shrink-0">
          <h1 className="text-[26px] font-semibold tracking-tight text-text-primary">
            {t('environment.title', 'Измени своё окружение в один клик!')}
          </h1>
          <p className="mt-1 text-[13px] text-text-muted leading-relaxed max-w-[640px]">
            {t(
              'environment.subtitle',
              'Атмосфера мира: небо, дороги, деревья, заправки и фонари. Выберите категорию.',
            )}
          </p>
        </header>

        <ExpandableGallery
          slides={slides}
          minHeightClass="min-h-[260px]"
          className="flex-1 min-h-0"
          actionLabel={t('environment.open', 'Открыть')}
          onSelect={(key) => {
            const s = slides.find(x => x.key === key);
            if (s) navigate(s.target);
          }}
        />
      </div>
    </div>
  );
}
