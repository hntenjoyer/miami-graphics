import { create } from 'zustand';
import { bridge } from '@/bridge';

export type RegionCode = 'eu' | 'ru';

interface RegionState {
  region: RegionCode | null;
  loaded: boolean;
  load: () => Promise<void>;
  setRegion: (region: RegionCode) => Promise<void>;
}

export const useRegionStore = create<RegionState>((set) => ({
  region: null,
  loaded: false,

  load: async () => {
    try {
      const status = await bridge.serverRegionGet();
      const r: RegionCode | null =
        status.region === 'ru' ? 'ru' : status.region === 'eu' ? 'eu' : null;
      set({ region: r, loaded: true });
    } catch (err) {
      console.warn('[regionStore] load failed', err);
      set({ loaded: true });
    }
  },

  setRegion: async (region) => {
    set({ region });
    try {
      await bridge.serverRegionSet(region);
    } catch (err) {
      console.error('[regionStore] save failed', err);
    }
  },
}));
