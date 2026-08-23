import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';

export interface BuildInstallSnapshot {
  buildId:           string;
  buildName:         string;
  reduxId:           string;
  reduxName:         string | null;
  gunpackId:         string;
  gunpackName:       string | null;
  armorLibraryId:    string | null;
  armorName:         string | null;
  soundsLibraryId:   string | null;
  soundsLibraryName: string | null;
  minimapKind:       'redux' | 'library' | null;
  minimapId:         string | null;
  minimapName:       string | null;
  reticleKind:       'redux' | 'library' | null;
  reticleId:         string | null;
  reticleName:       string | null;
  arenaKind:         'redux' | null;
  arenaId:           string | null;
  arenaName:         string | null;
  selgunsCount:      number;
  installedAt:       number;
}

interface LastBuildInstallStore {
  snapshot: BuildInstallSnapshot | null;
  set:   (snap: BuildInstallSnapshot) => void;
  clear: () => void;
}

export const useLastBuildInstallStore = create<LastBuildInstallStore>()(
  persist(
    (set) => ({
      snapshot: null,
      set:   (snap) => set({ snapshot: snap }),
      clear: ()     => set({ snapshot: null }),
    }),
    {
      name: 'hntgraph.lastBuildInstall',
      storage: createJSONStorage(() => window.localStorage),

      partialize: (s) => ({ snapshot: s.snapshot }),
    },
  ),
);
