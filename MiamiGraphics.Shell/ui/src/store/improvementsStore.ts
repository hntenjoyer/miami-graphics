import { create } from 'zustand';
import { bridge } from '@/bridge';
import type { Improvement, InjectResult } from '@/bridge/types';

interface ImprovementsState {
  list: Improvement[];
  loading: boolean;
  busyId: string | null;
  error: string | null;

  load: () => Promise<void>;
  install: (id: string) => Promise<InjectResult>;
  remove: (id: string) => Promise<InjectResult>;
  clearError: () => void;
}

export const useImprovementsStore = create<ImprovementsState>((set, get) => ({
  list: [],
  loading: false,
  busyId: null,
  error: null,

  clearError: () => set({ error: null }),

  async load() {
    set({ loading: true });
    try {
      const list = await bridge.improvementsList();
      set({ list, loading: false });
    } catch (e) {
      set({ loading: false, error: e instanceof Error ? e.message : String(e) });
    }
  },

  async install(id) {
    if (get().busyId) return { success: false, errorMessage: 'Уже идёт установка', workDir: null };
    set({ busyId: id, error: null });
    try {
      const res = await bridge.improvementInstall(id);
      if (res.success) {
        const item = get().list.find(i => i.id === id);
        if (item) void bridge.activityLog('improvement_install', `улучшение «${item.name}»`, id);
      }
      if (!res.success) set({ error: res.errorMessage ?? 'Не удалось установить' });
      await get().load();
      return res;
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      set({ error: msg });
      return { success: false, errorMessage: msg, workDir: null };
    } finally {
      set({ busyId: null });
    }
  },

  async remove(id) {
    if (get().busyId) return { success: false, errorMessage: 'Уже идёт установка', workDir: null };
    set({ busyId: id, error: null });
    try {
      const res = await bridge.improvementRemove(id);
      if (!res.success) set({ error: res.errorMessage ?? 'Не удалось снять' });
      await get().load();
      return res;
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      set({ error: msg });
      return { success: false, errorMessage: msg, workDir: null };
    } finally {
      set({ busyId: null });
    }
  },
}));
