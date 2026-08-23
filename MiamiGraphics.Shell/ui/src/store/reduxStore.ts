import { create } from 'zustand';
import i18n from '@/i18n';
import type { ReduxItem } from '@/bridge/types';
import { bridge } from '@/bridge';
import { useSessionStore } from '@/store/sessionStore';
import { useBackupStore } from '@/store/backupStore';
import { readCache, writeCache, CacheKeys } from '@/store/catalogCache';
import { useGlobalToastStore } from '@/store/globalToastStore';
import { useReduxVersionsStore } from '@/store/reduxVersionsStore';
import { shuffled } from '@/utils/shuffle';

let fetchShuffleApplied = false;

function reduxNameForToast(items: ReduxItem[], id: string): string {
  return items.find(x => x.id === id)?.name ?? i18n.t('redux.fallbackName', 'Редукс');
}

function installedToast(items: ReduxItem[], id: string): string {
  return i18n.t('redux.installedToast', {
    name: reduxNameForToast(items, id),
    defaultValue: '{{name}} установлен',
  });
}

const backupLockError = () => i18n.t(
  'redux.backupLockError',
  'Сейчас идёт подготовка чистых файлов GTA. Дождись окончания (видно в Downloads справа) и попробуй снова.',
);
const backupRequiredError = () => i18n.t(
  'redux.backupRequiredError',
  'Сначала нужно подготовить чистый update.rpf - открываю экран подготовки.',
);

function rejectIfBackupInFlight(): { success: false; errorMessage: string } | null {
  return useBackupStore.getState().isBackupInProgress()
    ? { success: false, errorMessage: backupLockError() }
    : null;
}

function rejectIfBackupMissing(): { success: false; errorMessage: string } | null {
  return useBackupStore.getState().ensureBackupOrGate()
    ? null
    : { success: false, errorMessage: backupRequiredError() };
}

const FAV_LOCAL_KEY = 'hntgraph.favorites';
const INSTALLED_LOCAL_KEY = 'hntgraph.installedRedux';

function loadInstalledReduxId(): string | null {
  try {
    const v = window.localStorage.getItem(INSTALLED_LOCAL_KEY);
    return v && typeof v === 'string' ? v : null;
  } catch { return null; }
}
function persistInstalledReduxId(id: string | null) {
  try {
    if (id) window.localStorage.setItem(INSTALLED_LOCAL_KEY, id);
    else    window.localStorage.removeItem(INSTALLED_LOCAL_KEY);
  } catch {  }
}

function loadLocalFavorites(): Set<string> {
  try {
    const raw = window.localStorage.getItem(FAV_LOCAL_KEY);
    if (!raw) return new Set();
    const arr = JSON.parse(raw);
    return Array.isArray(arr) ? new Set(arr.filter(x => typeof x === 'string')) : new Set();
  } catch { return new Set(); }
}

function persistLocalFavorites(set: Set<string>) {
  window.localStorage.setItem(FAV_LOCAL_KEY, JSON.stringify([...set]));
}

const inflightOps = new Map<string, 'add' | 'remove'>();

export type ReduxSort = 'default' | 'newest' | 'oldest' | 'downloads' | 'rating';

interface ReduxState {
  items: ReduxItem[];
  loading: boolean;
  search: string;
  servers: string[];
  onlyFavorites: boolean;
  sort: ReduxSort;

  favorites: Set<string>;
  selectedId: string | null;
  installedReduxId: string | null;

  ratings: Record<string, { avg: number; count: number }>;

  load: () => Promise<void>;
  loadRatings: () => Promise<void>;
  setSearch: (q: string) => void;
  toggleServer: (s: string) => void;
  setOnlyFavorites: (v: boolean) => void;
  setSort: (s: ReduxSort) => void;

  select: (id: string | null) => void;

  toggleFavorite: (id: string, currentUserId: string | null) => Promise<void>;
  syncFavoritesFromCloud: (currentUserId: string) => Promise<void>;

  resetFavoritesToGuest: () => void;

  install: (id: string, versionId?: string | null) => Promise<{ success: boolean; errorMessage: string | null }>;
  installForceClean: (id: string, versionId?: string | null) => Promise<{ success: boolean; errorMessage: string | null }>;
  installPreserve: (id: string, versionId?: string | null) => Promise<{ success: boolean; errorMessage: string | null }>;
  markInstalled: (id: string) => void;
  clearInstalled: () => void;

  uninstall: () => Promise<{ success: boolean; errorMessage: string | null }>;
  uninstallForceClean: () => Promise<{ success: boolean; errorMessage: string | null }>;
  uninstallPreserve: () => Promise<{ success: boolean; errorMessage: string | null }>;
}

const cachedReduxItemsRaw = readCache<ReduxItem[]>(CacheKeys.reduxItems);
const cachedReduxItems = cachedReduxItemsRaw
  ? shuffled(cachedReduxItemsRaw)
  : null;

export const useReduxStore = create<ReduxState>((set, get) => ({
  items: cachedReduxItems ?? [],
  loading: false,
  servers: [],
  search: '',
  onlyFavorites: false,
  sort: 'default',

  favorites: loadLocalFavorites(),
  selectedId: null,
  installedReduxId: loadInstalledReduxId(),
  ratings: {},

  loadRatings: async () => {
    try {
      const ratings = await bridge.reduxRatingsAggregate();
      set({ ratings: ratings ?? {} });
    } catch (e) {
      console.warn('[redux] ratings aggregate failed', e);
    }
  },

  load: async () => {

    const haveSnapshot = get().items.length > 0;
    set({ loading: !haveSnapshot });

    let settled = false;
    const watchdog = window.setTimeout(() => {
      if (!settled) {
        console.warn('[redux] load watchdog: запрос не завершился за 5с, снимаю loading');
        set({ loading: false });
      }
    }, 5000);

    try {
      const backendId = (await bridge.getCurrentReduxId())?.trim() || null;
      if (backendId !== get().installedReduxId) {
        set({ installedReduxId: backendId });
        try {
          if (backendId) window.localStorage.setItem(INSTALLED_LOCAL_KEY, backendId);
          else window.localStorage.removeItem(INSTALLED_LOCAL_KEY);
        } catch {  }
      }
    } catch {  }

    try {
      const items = await bridge.reduxList(undefined, undefined);
      settled = true;
      window.clearTimeout(watchdog);

      const needVersions = items.filter(i => (i.patchSizeBytes ?? 0) === 0);
      if (needVersions.length > 0) {
        const enriched = await Promise.all(needVersions.map(async (item) => {
          try {
            const versions = await bridge.reduxVersions(item.id);
            if (versions.length === 0) return null;
            const v = [...versions].sort((a, b) => a.slot - b.slot)[0];
            return {
              id: item.id,
              patchSizeBytes:   v.patchSizeBytes,
              targetGtaVersion: v.targetGtaVersion ?? '',
              components:       v.components,
            };
          } catch (e) {
            console.warn(`[redux] version backfill failed for '${item.id}'`, e);
            return null;
          }
        }));
        const byId = new Map<string, NonNullable<typeof enriched[number]>>();
        for (const e of enriched) if (e) byId.set(e.id, e);
        const merged = items.map(i => {
          const e = byId.get(i.id);
          return e ? { ...i, patchSizeBytes: e.patchSizeBytes, targetGtaVersion: e.targetGtaVersion, components: e.components } : i;
        });
        const finalItems = fetchShuffleApplied ? merged : (fetchShuffleApplied = true, shuffled(merged));
        set({ items: finalItems, loading: false });
        writeCache(CacheKeys.reduxItems, merged);
      } else {
        const finalItems = fetchShuffleApplied ? items : (fetchShuffleApplied = true, shuffled(items));
        set({ items: finalItems, loading: false });
        writeCache(CacheKeys.reduxItems, items);
      }

      void get().loadRatings();
    } catch (e) {
      settled = true;
      window.clearTimeout(watchdog);
      console.error('[redux] load failed', e);
      set({ loading: false });
    }
  },

  setSearch: (q) => { set({ search: q }); },
  toggleServer: (s) => set(st => ({
    servers: st.servers.includes(s) ? st.servers.filter(x => x !== s) : [...st.servers, s],
  })),
  setOnlyFavorites: (v) => set({ onlyFavorites: v }),
  setSort: (s) => set({ sort: s }),

  select: (id) => {
    set({ selectedId: id });
    if (id) void useReduxVersionsStore.getState().load(id);
  },

  toggleFavorite: async (id, currentUserId) => {
    const prev = get().favorites;
    const next = new Set(prev);
    const wasFav = next.has(id);
    if (wasFav) next.delete(id); else next.add(id);

    set({ favorites: next });

    if (currentUserId) {

      const op: 'add' | 'remove' = wasFav ? 'remove' : 'add';
      inflightOps.set(id, op);
      try {
        if (op === 'remove') await bridge.reduxFavoriteRemove(currentUserId, id);
        else                 await bridge.reduxFavoriteAdd(currentUserId, id);
      } catch (e) {

        console.error('[redux] cloud favorite write failed - rolling back', e);

        const after = new Set(get().favorites);
        if (op === 'add') after.delete(id); else after.add(id);
        set({ favorites: after });
      } finally {

        if (inflightOps.get(id) === op) inflightOps.delete(id);
      }
    } else {

      persistLocalFavorites(next);
    }
  },

  syncFavoritesFromCloud: async (currentUserId) => {
    try {
      const cloudIds = await bridge.reduxFavoriteList(currentUserId);

      const merged = new Set(cloudIds);
      for (const [id, op] of inflightOps) {
        if (op === 'add') merged.add(id);
        else              merged.delete(id);
      }
      set({ favorites: merged });
    } catch (e) {
      console.warn('[redux] cloud favorite list failed', e);

    }
  },

  resetFavoritesToGuest: () => {

    set({ favorites: loadLocalFavorites() });
  },

  install: async (id, versionId) => {
    const locked = rejectIfBackupInFlight(); if (locked) return locked;
    const noBackup = rejectIfBackupMissing(); if (noBackup) return noBackup;
    const r = await bridge.reduxInstall(id, versionId ?? null);
    if (r.success) {
      recordInstallFor(get().items, id);
      persistInstalledReduxId(id);
      set({ installedReduxId: id });
      useGlobalToastStore.getState().push('success', installedToast(get().items, id));
      void get().load();
    }
    return { success: r.success, errorMessage: r.errorMessage };
  },

  installForceClean: async (id, versionId) => {
    const locked = rejectIfBackupInFlight(); if (locked) return locked;
    const noBackup = rejectIfBackupMissing(); if (noBackup) return noBackup;
    const r = await bridge.reduxInstallForceClean(id, versionId ?? null);
    if (r.success) {
      recordInstallFor(get().items, id);
      persistInstalledReduxId(id);
      set({ installedReduxId: id });
      useGlobalToastStore.getState().push('success', installedToast(get().items, id));
      void get().load();
    }
    return { success: r.success, errorMessage: r.errorMessage };
  },

  installPreserve: async (id, versionId) => {
    const locked = rejectIfBackupInFlight(); if (locked) return locked;
    const noBackup = rejectIfBackupMissing(); if (noBackup) return noBackup;
    const r = await bridge.reduxInstallPreserve(id, versionId ?? null);
    if (r.success) {
      recordInstallFor(get().items, id);
      persistInstalledReduxId(id);
      set({ installedReduxId: id });
      useGlobalToastStore.getState().push('success', installedToast(get().items, id));
      void get().load();
    }
    return { success: r.success, errorMessage: r.errorMessage };
  },

  markInstalled: (id) => {

    recordInstallFor(get().items, id);
    persistInstalledReduxId(id);
    set({ installedReduxId: id });
  },

  clearInstalled: () => {
    persistInstalledReduxId(null);
    set({ installedReduxId: null });
  },

  uninstall: async () => {
    const r = await bridge.reduxUninstall();
    if (r.success) {
      persistInstalledReduxId(null);
      set({ installedReduxId: null });
    }
    return { success: r.success, errorMessage: r.errorMessage };
  },
  uninstallForceClean: async () => {
    const r = await bridge.reduxUninstallForceClean();
    if (r.success) {
      persistInstalledReduxId(null);
      set({ installedReduxId: null });
    }
    return { success: r.success, errorMessage: r.errorMessage };
  },
  uninstallPreserve: async () => {
    const r = await bridge.reduxUninstallPreserve();
    if (r.success) {
      persistInstalledReduxId(null);
      set({ installedReduxId: null });
    }
    return { success: r.success, errorMessage: r.errorMessage };
  },
}));

function recordInstallFor(items: ReduxItem[], reduxId: string): void {
  const item = items.find(i => i.id === reduxId);
  if (!item) return;
  const auth = useSessionStore.getState().auth;
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

  if (!userId) return;
  bridge.installRecord(
    userId,
    item.id,
    item.name || item.id,
    item.author ?? '',
    item.previewUrl || null,
  ).catch(e => console.warn('[redux] installRecord failed (non-fatal)', e));
}
