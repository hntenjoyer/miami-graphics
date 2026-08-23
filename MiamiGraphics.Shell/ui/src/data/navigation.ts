import {
  Home, Layers, Crosshair, FileText, ShieldCheck, Sun,
  Sparkles, Shield, Map as MapIcon, MapPinned, LayoutGrid, Target, Volume2,
  Boxes, CircleDashed, Hammer, Gauge,
  type LucideIcon,
} from 'lucide-react';
import { ADMIN_BUILD } from '@/buildFlags';

export type UserRole = 'Guest' | 'User' | 'Moderator' | 'AdminL1' | 'AdminL2';

export interface NavItem {
  id: string;
  labelKey: string;
  icon: LucideIcon;
  rolesVisible?: UserRole[];
  testerOnly?: boolean;
  children?: NavItem[];
}

export const MODIFICATION_TABS: NavItem[] = [
  { id: 'redux',    labelKey: 'nav.redux',    icon: Layers   },
  { id: 'guns',     labelKey: 'nav.guns',     icon: Crosshair },
  { id: 'armor',    labelKey: 'nav.armor',    icon: Shield   },
  { id: 'minimaps', labelKey: 'nav.minimaps', icon: MapIcon  },
  { id: 'bigmap',   labelKey: 'nav.bigmap',   icon: MapPinned },
  { id: 'reticles', labelKey: 'nav.reticles', icon: Target   },
  { id: 'sounds',   labelKey: 'nav.sounds',   icon: Volume2  },
  { id: 'other',    labelKey: 'nav.other',    icon: CircleDashed },
  { id: 'settings', labelKey: 'nav.settingsShort', icon: FileText },
];
export const MODIFICATION_IDS: readonly string[] =
  MODIFICATION_TABS.map(t => t.id);

export const MODIFICATIONS_DEFAULT_ID = 'redux';

export const MAIN_NAV: NavItem[] = [
  { id: 'home',          labelKey: 'nav.home',          icon: Home },

  { id: 'modifications', labelKey: 'nav.modifications', icon: LayoutGrid },

  { id: 'builds-hub',    labelKey: 'nav.buildsHub',     icon: Boxes },

  { id: 'environment',   labelKey: 'nav.environment',   icon: Sun },

  { id: 'workshop',      labelKey: 'nav.workshop',      icon: Hammer },

  { id: 'pcdiag',        labelKey: 'nav.pcdiag',        icon: Gauge },

  ...(ADMIN_BUILD
    ? [{
        id: 'admin',
        labelKey: 'nav.admin',
        icon: ShieldCheck,
        rolesVisible: ['Moderator', 'AdminL1', 'AdminL2'] as UserRole[],
      }]
    : []),
];

export const SECURITY_NAV_ITEM: NavItem = {
  id: 'security',
  labelKey: 'nav.security',
  icon: Shield,
};

export const REDUX_CUSTOMIZE_CHILD: NavItem = {
  id: 'redux-customize',
  labelKey: 'nav.reduxCustomize',
  icon: Sparkles,
};

export type NavItemId = string;
