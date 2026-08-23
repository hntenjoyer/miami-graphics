import {
  Layers, Crosshair, Settings as SettingsIcon, Users, HelpCircle,
  type LucideIcon,
} from 'lucide-react';

export interface HomeCategory {
  id: 'redux' | 'guns' | 'settings' | 'players' | 'faq';
  icon: LucideIcon;
  labelKey: string;
  descriptionKey: string;
  countKey?: string;
  navTarget?: string;
}

export const HOME_CATEGORIES: HomeCategory[] = [
  { id: 'redux',    icon: Layers,       labelKey: 'home.cat.redux.label',    descriptionKey: 'home.cat.redux.desc',    countKey: 'home.cat.redux.count' },
  { id: 'guns',     icon: Crosshair,    labelKey: 'home.cat.guns.label',     descriptionKey: 'home.cat.guns.desc',     countKey: 'home.cat.guns.count' },
  { id: 'settings', icon: SettingsIcon, labelKey: 'home.cat.settings.label', descriptionKey: 'home.cat.settings.desc', countKey: 'home.cat.settings.count' },
  { id: 'players',  icon: Users,        labelKey: 'home.cat.players.label',  descriptionKey: 'home.cat.players.desc',  countKey: 'home.cat.players.count', navTarget: 'builds-hub' },
  { id: 'faq',      icon: HelpCircle,   labelKey: 'home.cat.faq.label',      descriptionKey: 'home.cat.faq.desc' },
];
