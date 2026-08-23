import { create } from 'zustand';
import { bridge } from '@/bridge';
import i18n from '@/i18n';
import type { CustomGun, CustomGunPatch } from '@/bridge/types';

interface CustomGunsReviewState {
  pending: CustomGun[];
  loadingPending: boolean;
  errorPending: string | null;

  loadPending: () => Promise<void>;

  approve: (id: string, reviewerUserId: string) => Promise<CustomGun>;

  reject: (id: string, reviewerUserId: string, reason: string) => Promise<CustomGun>;

  manage: CustomGun[];
  loadingManage: boolean;
  errorManage: string | null;
  manageStatus: string;
  manageSearch: string;
  setManageFilter: (status: string, search: string) => void;
  loadManage: () => Promise<void>;
  adminPatch: (id: string, patch: CustomGunPatch) => Promise<void>;
  adminDelete: (id: string, reason: string, hard: boolean) => Promise<void>;
}

export const useCustomGunsStore = create<CustomGunsReviewState>((set) => ({
  pending: [],
  loadingPending: false,
  errorPending: null,

  loadPending: async () => {
    set({ loadingPending: true, errorPending: null });
    try {
      const list = await bridge.customGunListPending();
      set({ pending: list, loadingPending: false, errorPending: null });
    } catch (e) {
      console.warn('[customGunsStore] loadPending failed', e);
      set({
        loadingPending: false,
        errorPending: e instanceof Error
          ? e.message
          : i18n.t('customGuns.loadPendingFail', 'Не удалось загрузить очередь модерации.'),
      });
    }
  },

  approve: async (id, reviewerUserId) => {
    const saved = await bridge.customGunApprove(id, reviewerUserId);
    set(s => ({ pending: s.pending.filter(g => g.id !== id) }));
    return saved;
  },

  reject: async (id, reviewerUserId, reason) => {
    const saved = await bridge.customGunReject(id, reviewerUserId, reason);
    set(s => ({ pending: s.pending.filter(g => g.id !== id) }));
    return saved;
  },

  manage: [],
  loadingManage: false,
  errorManage: null,
  manageStatus: '',
  manageSearch: '',

  setManageFilter: (status, search) => set({ manageStatus: status, manageSearch: search }),

  loadManage: async () => {
    const { manageStatus, manageSearch } = useCustomGunsStore.getState();
    set({ loadingManage: true, errorManage: null });
    try {
      const list = await bridge.customGunAdminList(manageStatus || null, manageSearch || null);
      set({ manage: list, loadingManage: false, errorManage: null });
    } catch (e) {
      console.warn('[customGunsStore] loadManage failed', e);
      set({
        loadingManage: false,
        errorManage: e instanceof Error
          ? e.message
          : i18n.t('customGuns.loadManageFail', 'Не удалось загрузить список ганов.'),
      });
    }
  },

  adminPatch: async (id, patch) => {
    const saved = await bridge.customGunAdminPatch(id, patch);
    set(s => ({ manage: s.manage.map(g => (g.id === id ? saved : g)) }));
  },

  adminDelete: async (id, reason, hard) => {
    const saved = await bridge.customGunAdminDelete(id, reason, hard);
    set(s => ({
      manage: hard
        ? s.manage.filter(g => g.id !== id)
        : s.manage.map(g => (g.id === id ? saved : g)).filter(g => s.manageStatus === 'all' || g.status !== 'removed'),
      pending: s.pending.filter(g => g.id !== id),
    }));
  },
}));
