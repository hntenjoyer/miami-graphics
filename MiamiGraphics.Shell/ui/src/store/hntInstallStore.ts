import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';

export interface HntComponentSnap {
  key:  'armor' | 'minimap' | 'reticle' | 'sounds' | 'bigMap';
  id:   string;
  name: string;
}

export interface HntInstallSnapshot {
  code:           string;
  reduxId:        string | null;
  reduxName:      string | null;
  gunpackId:      string | null;
  gunpackName:    string | null;
  selgunsCount:   number;
  customizeCount: number;
  components:     HntComponentSnap[];
  installedAt:    number;
}

interface HntInstallStore {
  snapshot: HntInstallSnapshot | null;
  set:   (snap: HntInstallSnapshot) => void;
  clear: () => void;
}

export const useHntInstallStore = create<HntInstallStore>()(
  persist(
    (set) => ({
      snapshot: null,
      set:   (snap) => set({ snapshot: snap }),
      clear: ()     => set({ snapshot: null }),
    }),
    {
      name: 'hntgraph.lastHntInstall',
      storage: createJSONStorage(() => window.localStorage),
      partialize: (s) => ({ snapshot: s.snapshot }),
    },
  ),
);
