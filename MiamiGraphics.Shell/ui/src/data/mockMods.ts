export interface MockMod {
  id: string;
  name: string;
  type: 'redux' | 'gunpack' | 'crosshair' | 'effects' | 'minimap';
  description: string;
  rating: number;
  downloads: number;
  isHot?: boolean;
  isViewed?: boolean;
  isMiamiOriginal?: boolean;
  publishedAt: string;
  bgGradient: string;
}

export const POPULAR_MODS: MockMod[] = [
  {
    id: 'gucci_v3',
    name: 'GUCCI V3 MAJ',
    type: 'redux',
    description: 'Специальная сборка модов, которая кардинально улучшает графику...',
    rating: 1.0,
    downloads: 128,
    isViewed: true,
    isHot: true,
    publishedAt: '5 АПР. 2026 г.',
    bgGradient: 'from-stone-700 via-stone-800 to-stone-900',
  },
  {
    id: 'zombie',
    name: 'ZOMBIE | REDUX',
    type: 'redux',
    description: 'Атмосферный мод про апокалипсис в ночном городе...',
    rating: 5.0,
    downloads: 274,
    isHot: true,
    publishedAt: '4 АПР. 2026 г.',
    bgGradient: 'from-amber-900 via-red-900 to-stone-950',
  },
  {
    id: 'miami_redux',
    name: 'MIAMI | REDUX',
    type: 'redux',
    description: 'Фирменная сборка от команды Miami Mods, чистый PvP-настрой...',
    rating: 4.6,
    downloads: 102,
    isMiamiOriginal: true,
    publishedAt: '15 ФЕВР. 2026 г.',
    bgGradient: 'from-violet-900 via-purple-950 to-fuchsia-950',
  },
  {
    id: 'allegri_v3',
    name: 'ALLEGRI V3 | REDUX',
    type: 'redux',
    description: 'Продвинутая сборка с переработанным светом и тенями...',
    rating: 5.0,
    downloads: 239,
    isViewed: true,
    isHot: true,
    publishedAt: '6 ФЕВР. 2026 г.',
    bgGradient: 'from-rose-800 via-red-900 to-stone-950',
  },
];
