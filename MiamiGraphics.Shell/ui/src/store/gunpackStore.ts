import { create } from 'zustand';
import { bridge } from '@/bridge';
import type {
  Gunpack, GunpackGun, GunpackWhitelistEntry, GunpackQueueItem, GunpackUploadRequest,
  GunpackPatch, GunpackGunPatch, GunpackVariant, GunpackVariantPatch,
  GunpackInstalledState, SelectedGun,
} from '@/bridge/types';
import { readCache, writeCache, CacheKeys } from '@/store/catalogCache';
import { shuffled } from '@/utils/shuffle';

let packsFetchShuffleApplied = false;

interface RecentPatch {
  until: number;
  patch: import('@/bridge/types').GunpackPatch;
  deleted?: boolean;
}
const RECENT_PATCH_TTL_MS = 30_000;
const recentlyPatchedPacks = new Map<string, RecentPatch>();

function notePatch(id: string, patch: import('@/bridge/types').GunpackPatch): void {
  const prev = recentlyPatchedPacks.get(id);
  recentlyPatchedPacks.set(id, {
    until: Date.now() + RECENT_PATCH_TTL_MS,
    patch: { ...(prev?.patch ?? {}), ...patch },
  });
  cleanupExpired();
}
function noteDelete(id: string): void {
  recentlyPatchedPacks.set(id, {
    until: Date.now() + RECENT_PATCH_TTL_MS,
    patch: {},
    deleted: true,
  });
  cleanupExpired();
}
function cleanupExpired(): void {
  const now = Date.now();
  for (const [k, v] of recentlyPatchedPacks) if (now > v.until) recentlyPatchedPacks.delete(k);
}

function shieldList<T extends Gunpack>(
  serverRows: T[],
  opts: {
    shouldGhost?: (patch: import('@/bridge/types').GunpackPatch) => boolean;
    ghostSource?: T[];
  } = {},
): T[] {
  cleanupExpired();
  if (recentlyPatchedPacks.size === 0) return serverRows;

  const out: T[] = [];
  const seen = new Set<string>();
  for (const row of serverRows) {
    const recent = recentlyPatchedPacks.get(row.id);
    if (!recent) { out.push(row); seen.add(row.id); continue; }
    if (recent.deleted) continue;
    out.push({ ...row, ...recent.patch });
    seen.add(row.id);
  }
  if (opts.shouldGhost && opts.ghostSource) {
    for (const [id, recent] of recentlyPatchedPacks) {
      if (recent.deleted) continue;
      if (seen.has(id)) continue;
      if (!opts.shouldGhost(recent.patch)) continue;
      const src = opts.ghostSource.find(p => p.id === id);
      if (src) out.unshift({ ...src, ...recent.patch });
    }
  }
  return out;
}

let publicPacksInFlight: Promise<void> | null = null;

let allGunsLoadedAt = 0;
const ALL_GUNS_TTL_MS = 5 * 60_000;
function staleAllGuns(): void { allGunsLoadedAt = 0; }

const GUNPACK_FAV_LOCAL_KEY = 'hntgraph.gunpackFavorites';
function loadGunpackFavorites(): Set<string> {
  try {
    const raw = window.localStorage.getItem(GUNPACK_FAV_LOCAL_KEY);
    if (!raw) return new Set();
    const arr = JSON.parse(raw);
    return Array.isArray(arr) ? new Set(arr.filter((x: unknown) => typeof x === 'string')) : new Set();
  } catch { return new Set(); }
}
function persistGunpackFavorites(set: Set<string>): void {
  try { window.localStorage.setItem(GUNPACK_FAV_LOCAL_KEY, JSON.stringify([...set])); }
  catch {  }
}

export interface FlatGun {
  packId:       string;
  packName:     string;
  packAuthor:   string | null;
  gunId:        string;
  baseName:     string;
  displayName:  string | null;
  category:     string;
  weaponPrefix: string;
  glbUrl:       string | null;
  previewUrl:   string | null;
}

interface GunpackState {

  packs:    Gunpack[];
  loading:  boolean;
  error:    string | null;
  loadPacks: () => Promise<void>;

  publicPacks:    Gunpack[];
  loadingPublic:  boolean;
  loadPublicPacks: () => Promise<void>;

  allGuns:        FlatGun[];
  loadingAllGuns: boolean;
  loadAllGuns:    () => Promise<void>;
  invalidateAllGuns: () => void;

  selectedId:   string | null;
  selectedPack: Gunpack | null;
  selectedGuns: GunpackGun[];
  selectedVariants: GunpackVariant[];
  loadingDetail: boolean;
  selectPack:   (id: string | null) => Promise<void>;
  refreshSelected: () => Promise<void>;

  selectedWhitelistName: string | null;
  selectWhitelistGun:    (name: string | null) => void;

  gunsSubTab:    'gunpacks' | 'guns' | 'custom';
  setGunsSubTab: (sub: 'gunpacks' | 'guns' | 'custom') => void;

  whitelist: GunpackWhitelistEntry[];
  loadWhitelist: () => Promise<void>;

  gunpackFavorites: Set<string>;
  toggleGunpackFavorite: (id: string) => void;

  queue:           GunpackQueueItem[];
  loadQueue:       () => Promise<void>;
  startUpload:     (req: GunpackUploadRequest) => Promise<GunpackQueueItem>;
  removeQueueItem: (tempId: string) => Promise<void>;

  patchPack:    (id: string, patch: GunpackPatch) => Promise<void>;
  deletePack:   (id: string) => Promise<void>;
  patchGun:     (gunId: string, patch: GunpackGunPatch) => Promise<void>;
  deleteGun:    (gunId: string) => Promise<void>;

  loadSelectedVariants:   () => Promise<void>;
  patchVariant:           (variantId: string, patch: GunpackVariantPatch) => Promise<void>;
  deleteVariant:          (variantId: string) => Promise<void>;
  setDefaultVariant:      (variantId: string) => Promise<void>;
  uploadVariant:          (packId: string, name: string, sourceRpfPath: string, coverImagePath?: string | null) => Promise<GunpackQueueItem>;

  installedGunpack:         GunpackInstalledState;
  installedSelectedGuns:    SelectedGun[];
  installStateLoaded:       boolean;
  loadInstallState:         () => Promise<void>;
  setInstalledGunpack:      (next: GunpackInstalledState) => void;
  setInstalledSelectedGuns: (next: SelectedGun[]) => void;

  applyQueueUpdate: (snap: GunpackQueueItem) => void;
}

const cachedPublicPacksRaw = readCache<Gunpack[]>(CacheKeys.gunpackPublic);
const cachedPublicPacks = cachedPublicPacksRaw
  ? shuffled(cachedPublicPacksRaw)
  : null;

export const useGunpackStore = create<GunpackState>((set, get) => ({
  packs: [],
  loading: false,
  error: null,
  publicPacks: cachedPublicPacks ?? [],
  loadingPublic: false,
  allGuns: [],
  loadingAllGuns: false,
  selectedId: null,
  selectedPack: null,
  selectedGuns: [],
  selectedVariants: [],
  loadingDetail: false,
  selectedWhitelistName: null,
  installedGunpack: { activeGunpackId: null, activeGunpackName: null, weaponsRpfSha256: null, installedAt: null },
  installedSelectedGuns: [],
  installStateLoaded: false,
  whitelist: [],
  gunpackFavorites: loadGunpackFavorites(),
  toggleGunpackFavorite: (id) => {
    const next = new Set(get().gunpackFavorites);
    if (next.has(id)) next.delete(id);
    else              next.add(id);
    persistGunpackFavorites(next);
    set({ gunpackFavorites: next });
  },
  queue: [],

  loadPacks: async () => {
    set({ loading: true, error: null });
    try {
      const packs = await bridge.adminGunpackList();
      const shielded = shieldList(packs);
      set({ packs: shielded, loading: false });
    } catch (e) {
      set({ loading: false, error: (e as Error).message });
    }
  },

  loadPublicPacks: async () => {
    if (publicPacksInFlight) return publicPacksInFlight;

    const run = (async () => {
      const haveSnapshot = get().publicPacks.length > 0;
      set({ loadingPublic: !haveSnapshot });
      try {
        const serverPacks = await bridge.gunpacksList(undefined, 'published');

        const overlaid = shieldList(serverPacks, {
          shouldGhost: (patch) => patch.status === 'published',
          ghostSource: get().packs,
        });
        const publicPacks = overlaid.filter(p => p.status !== 'hidden');

        const visible = get().publicPacks;
        let finalPacks: Gunpack[];
        if (haveSnapshot) {
          const byId = new Map(publicPacks.map(p => [p.id, p]));
          const ordered: Gunpack[] = [];
          for (const v of visible) {
            const fresh = byId.get(v.id);
            if (fresh) { ordered.push(fresh); byId.delete(v.id); }
          }
          for (const p of byId.values()) ordered.push(p);
          finalPacks = ordered;
        } else {
          finalPacks = packsFetchShuffleApplied
            ? publicPacks
            : (packsFetchShuffleApplied = true, shuffled(publicPacks));
        }
        set({ publicPacks: finalPacks, loadingPublic: false });
        writeCache(CacheKeys.gunpackPublic, publicPacks);
      } catch (e) {
        console.warn('[gunpackStore] public packs load failed', e);
        set({ loadingPublic: false });
      }
    })();

    publicPacksInFlight = run;
    try { await run; } finally { publicPacksInFlight = null; }
  },

  loadAllGuns: async () => {

    if (get().loadingAllGuns) return;
    if (get().allGuns.length > 0 && Date.now() - allGunsLoadedAt < ALL_GUNS_TTL_MS) return;
    set({ loadingAllGuns: true });
    try {
      if (get().publicPacks.length === 0) await get().loadPublicPacks();
      const packs = get().publicPacks;
      const packById = new Map(packs.map(p => [p.id, p]));

      let flat: FlatGun[] | null = null;

      if (typeof bridge.gunpackAllGuns === 'function') {
        try {
          const rows = await bridge.gunpackAllGuns();
          flat = [];
          for (const g of rows) {
            const p = packById.get(g.gunpackId);
            if (!p) continue;
            flat.push({
              packId:       p.id,
              packName:     p.name,
              packAuthor:   p.author,
              gunId:        g.id,
              baseName:     g.baseName,
              displayName:  g.displayName,
              category:     g.category,
              weaponPrefix: g.weaponPrefix,
              glbUrl:       g.glbUrl,
              previewUrl:   g.previewUrl,
            });
          }
        } catch (err) {
          console.warn('[gunpackStore] gunpackAllGuns failed, откатываюсь на per-pack', err);
          flat = null;
        }
      }

      if (flat === null) {
        const fetchPack = async (p: typeof packs[number]): Promise<FlatGun[]> => {
          for (let attempt = 0; attempt < 2; attempt++) {
            try {
              const guns = await bridge.gunpackGuns(p.id);
              return guns
                .filter(g => !g.isHidden)
                .map<FlatGun>((g) => ({
                  packId:       p.id,
                  packName:     p.name,
                  packAuthor:   p.author,
                  gunId:        g.id,
                  baseName:     g.baseName,
                  displayName:  g.displayName,
                  category:     g.category,
                  weaponPrefix: g.weaponPrefix,
                  glbUrl:       g.glbUrl,
                  previewUrl:   g.previewUrl,
                }));
            } catch (err) {
              if (attempt === 1) {
                console.warn(`[gunpackStore] gunpackGuns(${p.id}) failed`, err);
                return [];
              }
            }
          }
          return [];
        };

        const CONCURRENCY = 8;
        const collected: FlatGun[] = [];
        let cursor = 0;
        const worker = async () => {
          while (cursor < packs.length) {
            const p = packs[cursor++];
            const rows = await fetchPack(p);
            for (const r of rows) collected.push(r);
          }
        };
        await Promise.all(Array.from({ length: Math.min(CONCURRENCY, packs.length) }, worker));
        flat = collected;
      }

      if (flat.length > 0) allGunsLoadedAt = Date.now();
      set(state => ({
        allGuns: flat!.length > 0 ? flat! : state.allGuns,
        loadingAllGuns: false,
      }));
    } catch (e) {
      console.warn('[gunpackStore] all-guns load failed', e);
      set({ loadingAllGuns: false });
    }
  },

  invalidateAllGuns: () => { allGunsLoadedAt = 0; set({ allGuns: [] }); },

  selectPack: async (id) => {
    set({ selectedId: id, selectedPack: null, selectedGuns: [], selectedVariants: [] });
    if (!id) return;
    set({ loadingDetail: true });
    try {
      const [pack, guns, variants] = await Promise.all([
        bridge.gunpackGet(id),
        bridge.gunpackGuns(id),
        bridge.gunpackVariantsList(id),
      ]);

      if (get().selectedId !== id) return;
      set({ selectedPack: pack, selectedGuns: guns, selectedVariants: variants, loadingDetail: false });
    } catch (e) {
      set({ loadingDetail: false, error: (e as Error).message });
    }
  },

  refreshSelected: async () => {
    const id = get().selectedId;
    if (id) await get().selectPack(id);
  },

  loadSelectedVariants: async () => {
    const id = get().selectedId;
    if (!id) return;
    try {
      const variants = await bridge.gunpackVariantsList(id);
      if (get().selectedId !== id) return;
      set({ selectedVariants: variants });
    } catch (e) {
      console.warn('[gunpackStore] loadSelectedVariants failed', e);
    }
  },

  selectWhitelistGun: (name) => set({ selectedWhitelistName: name }),

  gunsSubTab: 'gunpacks',
  setGunsSubTab: (sub) => set({ gunsSubTab: sub }),

  loadInstallState: async () => {

    try {
      const [installedGunpack, installedSelectedGuns] = await Promise.all([
        bridge.gunpackGetInstalledState(),
        bridge.selectedGunsList(),
      ]);
      set({ installedGunpack, installedSelectedGuns, installStateLoaded: true });
    } catch (e) {
      console.warn('[gunpackStore] install state load failed', e);
      set({ installStateLoaded: true });
    }
  },

  setInstalledGunpack:      (next) => set({ installedGunpack: next }),
  setInstalledSelectedGuns: (next) => set({ installedSelectedGuns: next }),

  loadWhitelist: async () => {
    if (get().whitelist.length > 0) return;
    try {
      const whitelist = await bridge.gunpackWhitelistList();
      set({ whitelist });
    } catch (e) {
      console.warn('[gunpackStore] whitelist load failed', e);
    }
  },

  loadQueue: async () => {
    try {
      const queue = await bridge.adminGunpackQueueList();
      set({ queue });
    } catch (e) {
      console.warn('[gunpackStore] queue load failed', e);
    }
  },

  startUpload: async (req) => {
    const item = await bridge.adminGunpackUpload(req);

    set(state => ({ queue: [...state.queue.filter(x => x.tempId !== item.tempId), item] }));
    return item;
  },

  removeQueueItem: async (tempId) => {
    await bridge.adminGunpackQueueRemove(tempId);
    set(state => ({ queue: state.queue.filter(x => x.tempId !== tempId) }));
  },

  patchPack: async (id, patch) => {
    await bridge.adminGunpackPatch(id, patch);

    notePatch(id, patch);

    set(state => {
      const nextPacks = state.packs.map(p => p.id === id ? { ...p, ...patch } as Gunpack : p);

      let nextPublicPacks = state.publicPacks;
      if ('status' in patch && patch.status !== undefined) {
        if (patch.status === 'hidden') {
          nextPublicPacks = state.publicPacks.filter(p => p.id !== id);
        } else if (patch.status === 'published') {
          const isAlreadyPublic = state.publicPacks.some(p => p.id === id);
          if (isAlreadyPublic) {
            nextPublicPacks = state.publicPacks.map(p => p.id === id ? { ...p, ...patch } as Gunpack : p);
          } else {
            const updatedFullPack = nextPacks.find(p => p.id === id);
            if (updatedFullPack) {
              nextPublicPacks = [updatedFullPack, ...state.publicPacks];
            }
          }
        }
      } else {
        nextPublicPacks = state.publicPacks.map(p =>
          p.id === id ? { ...p, ...patch } as Gunpack : p);
      }

      if (nextPublicPacks !== state.publicPacks) {
        writeCache(CacheKeys.gunpackPublic, nextPublicPacks);
      }

      return {
        packs: nextPacks,
        publicPacks: nextPublicPacks,
        selectedPack: state.selectedPack?.id === id
          ? { ...state.selectedPack, ...patch } as Gunpack
          : state.selectedPack,
      };
    });

  },

  deletePack: async (id) => {
    await bridge.adminGunpackDelete(id);
    staleAllGuns();

    noteDelete(id);

    set(state => {
      const nextPublicPacks = state.publicPacks.filter(p => p.id !== id);
      if (nextPublicPacks.length !== state.publicPacks.length) {
        writeCache(CacheKeys.gunpackPublic, nextPublicPacks);
      }
      return {
        packs:        state.packs.filter(p => p.id !== id),
        publicPacks:  nextPublicPacks,
        selectedId:   state.selectedId === id ? null : state.selectedId,
        selectedPack: state.selectedPack?.id === id ? null : state.selectedPack,
        selectedGuns: state.selectedPack?.id === id ? [] : state.selectedGuns,
      };
    });

  },

  patchGun: async (gunId, patch) => {
    await bridge.adminGunpackGunPatch(gunId, patch);
    staleAllGuns();
    set(state => ({
      selectedGuns: state.selectedGuns.map(g => g.id === gunId ? { ...g, ...patch } as GunpackGun : g),
    }));
  },

  deleteGun: async (gunId) => {
    await bridge.adminGunpackGunDelete(gunId);
    staleAllGuns();
    set(state => ({
      selectedGuns: state.selectedGuns.filter(g => g.id !== gunId),
    }));
  },

  patchVariant: async (variantId, patch) => {
    await bridge.adminGunpackVariantPatch(variantId, patch);
    set(state => ({
      selectedVariants: state.selectedVariants.map(v =>
        v.id === variantId ? { ...v, ...patch } as GunpackVariant : v),
    }));
  },

  deleteVariant: async (variantId) => {
    await bridge.adminGunpackVariantDelete(variantId);
    set(state => ({
      selectedVariants: state.selectedVariants.filter(v => v.id !== variantId),
    }));
  },

  setDefaultVariant: async (variantId) => {
    await bridge.adminGunpackVariantSetDefault(variantId);
    set(state => ({
      selectedVariants: state.selectedVariants.map(v => ({
        ...v,
        isDefault: v.id === variantId,
      })),
    }));
  },

  uploadVariant: async (packId, name, sourceRpfPath, coverImagePath) => {
    const queued = await bridge.adminGunpackVariantUpload(
      packId, name, sourceRpfPath, coverImagePath ?? undefined);
    set(state => ({
      queue: state.queue.some(x => x.tempId === queued.tempId)
        ? state.queue.map(x => x.tempId === queued.tempId ? queued : x)
        : [...state.queue, queued],
    }));
    return queued;
  },

  applyQueueUpdate: (snap) => {
    set(state => {
      const idx = state.queue.findIndex(x => x.tempId === snap.tempId);
      const queue = idx >= 0
        ? state.queue.map(x => x.tempId === snap.tempId ? snap : x)
        : [...state.queue, snap];

      if (snap.status === 'done' || snap.status === 'error') {
        staleAllGuns();
        queueMicrotask(() => {
          void get().loadPacks();
          const currentPackId = get().selectedId;
          if (currentPackId) void get().loadSelectedVariants();
        });
      }
      return { queue };
    });
  },
}));
