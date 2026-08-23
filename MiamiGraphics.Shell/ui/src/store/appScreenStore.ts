import { create } from 'zustand';

export type AppScreen = 'onboarding' | 'firstrun' | 'welcome' | 'backup' | 'home';

interface AppScreenState {
  requested: AppScreen | null;
  request: (screen: AppScreen) => void;
  clear: () => void;
}

export const useAppScreenStore = create<AppScreenState>(set => ({
  requested: null,
  request: (screen) => set({ requested: screen }),
  clear:   () => set({ requested: null }),
}));
