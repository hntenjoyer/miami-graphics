import { create } from 'zustand';
import type { FeaturedPick } from '@/bridge/types';
import { bridge } from '@/bridge';

interface FeaturedState {
  picks: FeaturedPick[];
  loading: boolean;
  initialized: boolean;
  error: string | null;

  load:    (force?: boolean) => Promise<void>;
  setSlot: (slotIndex: number, reduxId: string) => Promise<void>;
  clear:   (slotIndex: number) => Promise<void>;
}

export const useFeaturedStore = create<FeaturedState>((set, get) => ({
  picks:       [],
  loading:     false,
  initialized: false,
  error:       null,

  load: async (force = false) => {
    if (!force && get().initialized) return;
    set({ loading: true, error: null });
    try {
      const picks = await bridge.featuredPicksList();
      set({ picks, loading: false, initialized: true });
    } catch (e) {
      console.error('[featured] load failed', e);
      set({ loading: false, initialized: true, error: (e as Error).message });
    }
  },

  setSlot: async (slotIndex, reduxId) => {
    await bridge.adminFeaturedPickSet(slotIndex, reduxId);
    await get().load(true);
  },

  clear: async (slotIndex) => {
    await bridge.adminFeaturedPickDelete(slotIndex);
    await get().load(true);
  },
}));
