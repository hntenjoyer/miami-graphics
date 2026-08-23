import i18n from '@/i18n';

export interface RoadmapItem {
  id: string;
  title: string;
  eta: string;
}

function item(id: string, titleKey: string, titleRu: string, etaKey: string, etaRu: string): RoadmapItem {
  return {
    id,
    get title() { return i18n.t(titleKey, titleRu); },
    get eta()   { return i18n.t(etaKey, etaRu); },
  };
}

export const ROADMAP: RoadmapItem[] = [
  item('r1', 'roadmap.r1.title', 'Свой каталог одежды и аксессуаров', 'roadmap.r1.eta', 'Май'),
  item('r2', 'roadmap.r2.title', 'Конфигурации под каждый RP-сервер', 'roadmap.r2.eta', 'Июнь'),
  item('r3', 'roadmap.r3.title', 'Авто-обновление модов в фоне', 'roadmap.r3.eta', 'Июнь'),
  item('r4', 'roadmap.r4.title', 'Mobile companion (Android/iOS)', 'roadmap.r4.eta', 'Лето'),
];
