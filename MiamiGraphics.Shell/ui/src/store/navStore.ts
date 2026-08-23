import { create } from 'zustand';
import type { NavItemId } from '@/data/navigation';

type Navigator = (id: NavItemId) => void;

export interface SectionHint {
  title:    string;
  subtitle: string | null;
}

interface NavState {
  activeId: NavItemId | null;
  setActiveId: (id: NavItemId | null) => void;

  _navigator: Navigator | null;
  setNavigator: (fn: Navigator | null) => void;
  requestNavigate: (id: NavItemId) => void;

  sectionHint: SectionHint | null;
  setSectionHint: (hint: SectionHint | null) => void;
}

export const useNavStore = create<NavState>((set, get) => ({
  activeId: null,
  setActiveId: (activeId) => set({ activeId }),

  _navigator: null,
  setNavigator: (fn) => set({ _navigator: fn }),
  requestNavigate: (id) => {
    const fn = get()._navigator;
    if (fn) fn(id);
  },

  sectionHint: null,
  setSectionHint: (sectionHint) => set({ sectionHint }),
}));
