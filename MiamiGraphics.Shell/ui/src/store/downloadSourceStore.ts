import { create } from 'zustand';
import { bridge } from '@/bridge';

export type DownloadSourceCode = 'eu' | 'ru2';

interface DownloadSourceState {
  source: DownloadSourceCode;
  loaded: boolean;
  queueEnabled: boolean;
  load: () => Promise<void>;
  setSource: (source: DownloadSourceCode) => Promise<void>;
}

export const useDownloadSourceStore = create<DownloadSourceState>((set) => ({
  source: 'eu',
  loaded: false,
  queueEnabled: false,

  load: async () => {
    try {
      const st = await bridge.downloadSourceGet();
      const s: DownloadSourceCode = st.source === 'ru2' ? 'ru2' : 'eu';
      set({ source: s, queueEnabled: s === 'ru2', loaded: true });
    } catch (err) {
      console.warn('[downloadSourceStore] load failed', err);
      set({ loaded: true });
    }
  },

  setSource: async (source) => {
    set({ source, queueEnabled: source === 'ru2' });
    try {
      await bridge.downloadSourceSet(source);
    } catch (err) {
      console.error('[downloadSourceStore] save failed', err);
    }
  },
}));
