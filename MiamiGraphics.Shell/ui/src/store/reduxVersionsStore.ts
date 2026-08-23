import { create } from 'zustand';
import type { ReduxVersion } from '@/bridge/types';
import { bridge } from '@/bridge';

interface VersionsState {
  byRedux: Record<string, ReduxVersion[]>;
  loading: Record<string, boolean>;
  error:   Record<string, string | null>;

  load:   (reduxId: string, force?: boolean) => Promise<void>;
  upsert: (version: ReduxVersion) => Promise<void>;
  remove: (versionId: string, reduxId: string) => Promise<void>;
}

export const useReduxVersionsStore = create<VersionsState>((set, get) => ({
  byRedux: {},
  loading: {},
  error:   {},

  load: async (reduxId, force = false) => {
    if (!force && get().byRedux[reduxId] !== undefined) return;
    set(s => ({ loading: { ...s.loading, [reduxId]: true }, error: { ...s.error, [reduxId]: null } }));
    try {
      const versions = await bridge.reduxVersions(reduxId);
      set(s => ({
        byRedux: { ...s.byRedux, [reduxId]: versions },
        loading: { ...s.loading, [reduxId]: false },
      }));
    } catch (e) {
      console.error('[versions] load failed', e);
      set(s => ({
        loading: { ...s.loading, [reduxId]: false },
        error:   { ...s.error,   [reduxId]: (e as Error).message },
      }));
    }
  },

  upsert: async (v) => {
    await bridge.adminVersionUpsert(v);
    await get().load(v.reduxId, true);
  },

  remove: async (id, reduxId) => {
    await bridge.adminVersionDelete(id);
    await get().load(reduxId, true);
  },
}));
