import { create } from 'zustand';
import { bridge } from '@/bridge';
import type { BigMap, BigMapState, InjectResult } from '@/bridge/types';

export type BigMapServerFilter = 'all' | 'gta5rp' | 'majestic';

export type BigMapSort = 'default' | 'downloads' | 'rating';

interface BigMapStoreState {
  list:        BigMap[];
  loadingList: boolean;
  loadList:    () => Promise<void>;

  selectedId:  string | null;
  selectedMap: BigMap | null;
  select:      (id: string | null) => void;

  state:        BigMapState;
  stateLoaded:  boolean;
  refreshState: () => Promise<void>;

  installing:   boolean;
  uninstalling: boolean;
  install:      (id: string) => Promise<InjectResult>;
  uninstall:    () => Promise<InjectResult>;

  serverFilter:    BigMapServerFilter;
  setServerFilter: (f: BigMapServerFilter) => void;

  sort:    BigMapSort;
  setSort: (s: BigMapSort) => void;

  ratings:     Record<string, { avg: number; count: number }>;
  loadRatings: () => Promise<void>;
}

export const useBigMapStore = create<BigMapStoreState>((set, get) => ({
  list: [],
  loadingList: false,
  selectedId: null,
  selectedMap: null,
  state: { enabled: false, id: null, name: null, foreignDetected: false },
  stateLoaded: false,
  installing: false,
  uninstalling: false,
  serverFilter: 'all',

  loadList: async () => {
    if (get().loadingList) return;
    set({ loadingList: get().list.length === 0 });
    try {
      const list = await bridge.bigMapList();
      set(s => ({
        list,
        loadingList: false,
        selectedMap: s.selectedId
          ? list.find(m => m.id === s.selectedId) ?? s.selectedMap
          : null,
      }));
    } catch (e) {
      console.warn('[bigMapStore] list load failed', e);
      set({ loadingList: false });
    }
  },

  select: (id) => {
    set(s => ({
      selectedId: id,
      selectedMap: id ? s.list.find(m => m.id === id) ?? null : null,
    }));
  },

  refreshState: async () => {
    try {
      const state = await bridge.bigMapGetState();
      set({ state, stateLoaded: true });
    } catch (e) {
      console.warn('[bigMapStore] state load failed', e);
      set({ stateLoaded: true });
    }
  },

  install: async (id) => {
    set({ installing: true });
    try {
      const res = await bridge.bigMapInstall(id);
      if (res.success) await get().refreshState();
      return res;
    } finally {
      set({ installing: false });
    }
  },

  uninstall: async () => {
    set({ uninstalling: true });
    try {
      const res = await bridge.bigMapUninstall();
      if (res.success) await get().refreshState();
      return res;
    } finally {
      set({ uninstalling: false });
    }
  },

  setServerFilter: (f) => set({ serverFilter: f }),

  sort: 'default',
  setSort: (s) => set({ sort: s }),

  ratings: {},
  loadRatings: async () => {
    try {
      const ratings = await bridge.bigMapRatingsAggregate();
      set({ ratings: ratings ?? {} });
    } catch (e) {
      console.warn('[bigMapStore] ratings aggregate failed', e);
    }
  },
}));
