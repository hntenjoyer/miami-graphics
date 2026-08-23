import { create } from 'zustand';
import { bridge } from '@/bridge';
import { useSessionStore } from '@/store/sessionStore';
import type {
  CustomGun, CustomGunLimits, CustomGunPatch, CustomGunSort,
  UserGunpack, WorkshopOpenRequest,
} from '@/bridge/types';

export function currentUserIdentity(): { id: string; name: string } {
  const auth = useSessionStore.getState().auth;
  const id = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : '';
  return { id, name: auth?.username ?? 'player' };
}

interface CustomGunState {
  view:        'all' | 'mine';
  setView:     (v: 'all' | 'mine') => void;

  all:         CustomGun[];
  mine:        CustomGun[];
  limits:      CustomGunLimits;
  loading:     boolean;
  sort:        CustomGunSort;
  setSort:     (s: CustomGunSort) => void;
  search:      string;
  setSearch:   (s: string) => void;

  load:        () => Promise<void>;
  patch:       (id: string, patch: CustomGunPatch) => Promise<void>;
  remove:      (id: string) => Promise<void>;
  install:     (id: string) => Promise<void>;

  workshopReq:   WorkshopOpenRequest | null;
  openWorkshop:  (req: WorkshopOpenRequest) => void;
  closeWorkshop: () => void;

  ownPackTick:     number;
  bumpOwnPackTick: () => void;

  myGunpacks:        UserGunpack[];
  refreshMyGunpacks: () => Promise<void>;
}

export const useCustomGunStore = create<CustomGunState>((set, get) => ({
  view: 'all',
  setView: (view) => set({ view }),

  all: [],
  mine: [],
  limits: { used: 0, max: 5 },
  loading: false,
  sort: 'new',
  setSort: (sort) => { set({ sort }); void get().load(); },
  search: '',
  setSearch: (search) => set({ search }),

  load: async () => {
    set({ loading: true });
    try {
      const u = currentUserIdentity();
      const [all, mine, limits] = await Promise.all([
        bridge.customGunsList(get().search || undefined, get().sort, u.id || undefined),
        u.id ? bridge.customGunsMine(u.id) : Promise.resolve([] as CustomGun[]),
        u.id ? bridge.customGunLimits(u.id) : Promise.resolve({ used: 0, max: 5 } as CustomGunLimits),
      ]);
      set({ all, mine, limits, loading: false });
    } catch (e) {
      console.warn('[customGunStore] load failed', e);
      set({ loading: false });
    }
  },

  patch: async (id, patch) => {
    await bridge.customGunPatch(id, patch);
    const apply = (g: CustomGun): CustomGun => g.id === id ? { ...g, ...patch } : g;
    set(s => ({ all: s.all.map(apply), mine: s.mine.map(apply) }));
  },

  remove: async (id) => {
    await bridge.customGunDelete(id);
    set(s => ({
      all:  s.all.filter(g => g.id !== id),
      mine: s.mine.filter(g => g.id !== id),
      limits: { ...s.limits, used: Math.max(0, s.limits.used - 1) },
    }));
  },

  install: async (id) => {
    await bridge.customGunInstall(id);
    const gun = get().all.find(g => g.id === id) ?? get().mine.find(g => g.id === id);
    if (gun) void bridge.activityLog('skin_install', `скин «${gun.displayName}»`, id);
    const bump = (g: CustomGun): CustomGun => g.id === id ? { ...g, downloadCount: g.downloadCount + 1 } : g;
    set(s => ({ all: s.all.map(bump), mine: s.mine.map(bump) }));
  },

  workshopReq: null,
  openWorkshop:  (req) => set({ workshopReq: req }),
  closeWorkshop: () => set({ workshopReq: null }),

  ownPackTick: 0,
  bumpOwnPackTick: () => set(s => ({ ownPackTick: s.ownPackTick + 1 })),

  myGunpacks: [],
  refreshMyGunpacks: () => {
    if (_myPacksInflight) return _myPacksInflight;
    _myPacksInflight = bridge.userGunpacksList()
      .then(list => { set({ myGunpacks: list }); })
      .catch(() => {})
      .finally(() => { _myPacksInflight = null; });
    return _myPacksInflight;
  },
}));

let _myPacksInflight: Promise<void> | null = null;
