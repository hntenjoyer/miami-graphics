import { create } from 'zustand';
import type { QueueItem, ReduxItem, ReduxAnalysis, BuildDevices } from '@/bridge/types';
import type { AdminSectionId } from '@/screens/admin/AdminSidebar';
import type { GunSlotState, UserBuild } from '@/store/userBuildsStore';
import { bridge } from '@/bridge';

export interface ProPlayerEditDraft {
  editingTarget:   UserBuild | 'new' | null;

  name:           string;
  authorUsername: string;
  family:         string;
  categoryLabel:  string;
  tier:           number;

  dpi:         string;
  sensitivity: string;
  resolution:  string;
  fpsAvg:      string;
  monitorHz:   string;
  videoUrl:    string;

  coverUrl:       string;
  settingsXmlUrl: string;

  reduxId:             string;
  reduxNameSnapshot:   string;
  gunpackId:           string;
  gunpackNameSnapshot: string;
  gunSlots:            Record<string, GunSlotState>;

  devices: BuildDevices;

  adminNotes: string;
}

const EMPTY_PRO_PLAYER_DRAFT: ProPlayerEditDraft = {
  editingTarget:       null,
  name:                '',
  authorUsername:      '',
  family:              '',
  categoryLabel:       'Элитный игрок',
  tier:                1,
  dpi:                 '',
  sensitivity:         '',
  resolution:          '',
  fpsAvg:              '',
  monitorHz:           '',
  videoUrl:            '',
  coverUrl:            '',
  settingsXmlUrl:      '',
  reduxId:             '',
  reduxNameSnapshot:   '',
  gunpackId:           '',
  gunpackNameSnapshot: '',
  gunSlots:            {},
  devices:             {},
  adminNotes:          '',
};

interface AdminState {
  queue: QueueItem[];
  catalog: ReduxItem[];
  analysis: ReduxAnalysis | null;
  analysisPending: boolean;
  analysisError: string | null;
  draftSourcePath: string | null;

  rewriteSeed: ReduxItem | null;
  setRewriteSeed: (item: ReduxItem | null) => void;

  appendVersionSeed: { parent: ReduxItem; nextSlot: number } | null;
  setAppendVersionSeed: (seed: { parent: ReduxItem; nextSlot: number } | null) => void;

  requestSection: AdminSectionId | null;
  requestSectionChange: (id: AdminSectionId) => void;
  consumeSectionRequest: () => void;

  proPlayerDraft: ProPlayerEditDraft;
  openProPlayerEditor: (target: UserBuild | 'new') => void;
  patchProPlayerDraft: (patch: Partial<ProPlayerEditDraft>) => void;
  closeProPlayerEditor: () => void;

  loadQueue: () => Promise<void>;
  addToQueue: (item: QueueItem) => Promise<void>;
  removeFromQueue: (id: string) => Promise<void>;
  runQueue: () => Promise<void>;
  cancelQueue: () => Promise<void>;

  loadCatalog: (search?: string, server?: string, status?: string) => Promise<void>;
  updateCatalog: (item: ReduxItem) => Promise<void>;
  deleteCatalog: (id: string) => Promise<void>;

  setDraftSource: (path: string | null) => void;
  setAnalysis: (a: ReduxAnalysis | null) => void;
  startBackgroundAnalyze: (path: string) => Promise<ReduxAnalysis>;
  setAnalysisError: (msg: string | null) => void;
  analyzeSource: (path: string) => Promise<ReduxAnalysis>;
  clearAnalysis: () => void;
}

export const useAdminStore = create<AdminState>((set) => {

  const onQueueProgress = (q: QueueItem) => {
    set(state => {
      const idx = state.queue.findIndex(x => x.tempId === q.tempId);
      if (idx < 0) return state;
      const next = [...state.queue];
      next[idx] = q;
      return { ...state, queue: next };
    });
  };
  bridge.events.on('admin:queueProgress', onQueueProgress);

  return {
    queue: [],
    catalog: [],
    analysis: null,
    analysisPending: false,
    analysisError: null,
    draftSourcePath: null,
    rewriteSeed: null,
    appendVersionSeed: null,
    requestSection: null,
    proPlayerDraft: EMPTY_PRO_PLAYER_DRAFT,

    setRewriteSeed: item => set({ rewriteSeed: item }),
    setAppendVersionSeed: seed => set({ appendVersionSeed: seed }),
    requestSectionChange: id => set({ requestSection: id }),
    consumeSectionRequest: () => set({ requestSection: null }),

    openProPlayerEditor: target => set({
      proPlayerDraft: hydrateProPlayerDraft(target),
    }),
    patchProPlayerDraft: patch => set(state => ({
      proPlayerDraft: { ...state.proPlayerDraft, ...patch },
    })),
    closeProPlayerEditor: () => set({ proPlayerDraft: EMPTY_PRO_PLAYER_DRAFT }),

    loadQueue: async () => set({ queue: await bridge.adminQueueList() }),
    addToQueue: async item => {
      const stored = await bridge.adminQueueAdd(item);
      set(state => ({ queue: [...state.queue, stored] }));
    },
    removeFromQueue: async id => {
      await bridge.adminQueueRemove(id);
      set(state => ({ queue: state.queue.filter(x => x.tempId !== id) }));
    },
    runQueue: async () => { await bridge.adminQueueRun(); },
    cancelQueue: async () => { await bridge.adminQueueCancel(); },

    loadCatalog: async (search, server, status) =>
      set({ catalog: await bridge.adminCatalogList(search, server, status) }),
    updateCatalog: async item => {
      await bridge.adminCatalogUpdate(item);
      set(state => ({
        catalog: state.catalog.map(x => x.id === item.id ? item : x),
      }));
    },
    deleteCatalog: async id => {
      await bridge.adminCatalogDelete(id);
      set(state => ({ catalog: state.catalog.filter(x => x.id !== id) }));
    },

    setDraftSource: path => set({ draftSourcePath: path }),
    setAnalysis: (a) => set({ analysis: a }),
    setAnalysisError: msg => set({ analysisError: msg }),

    startBackgroundAnalyze: async path => {

      set({ analysisPending: true, analysisError: null, analysis: null });
      try {
        const a = await bridge.adminReduxAnalyze(path);
        set({ analysis: a, analysisPending: false });
        return a;
      } catch (e) {
        const msg = (e as Error).message || 'Не удалось проанализировать файл.';
        set({ analysisPending: false, analysisError: msg });
        throw e;
      }
    },

    analyzeSource: async path => {
      const a = await bridge.adminReduxAnalyze(path);
      set({ analysis: a });
      return a;
    },
    clearAnalysis: () => set({
      analysis: null,
      analysisPending: false,
      analysisError: null,
      draftSourcePath: null,
      rewriteSeed: null,
      appendVersionSeed: null,
    }),
  };
});

function hydrateProPlayerDraft(target: UserBuild | 'new'): ProPlayerEditDraft {
  if (target === 'new') {
    return { ...EMPTY_PRO_PLAYER_DRAFT, editingTarget: 'new' };
  }
  return {
    editingTarget:       target,
    name:                target.name ?? '',
    authorUsername:      target.author ?? '',
    family:              target.family ?? '',
    categoryLabel:       target.categoryLabel ?? 'Элитный игрок',
    tier:                target.tier ?? 1,
    dpi:                 target.dpi?.toString() ?? '',
    sensitivity:         target.sensitivity?.toString() ?? '',
    resolution:          target.resolution ?? '',
    fpsAvg:              target.fpsAvg?.toString() ?? '',
    monitorHz:           target.monitorHz?.toString() ?? '',
    videoUrl:            target.videoUrl ?? '',
    coverUrl:            target.coverUrl ?? '',
    settingsXmlUrl:      target.settingsXmlUrl ?? '',
    reduxId:             target.reduxId ?? '',
    reduxNameSnapshot:   target.reduxNameSnapshot ?? '',
    gunpackId:           target.gunpackId ?? '',
    gunpackNameSnapshot: target.gunpackNameSnapshot ?? '',
    gunSlots:            target.gunSlots ?? {},
    devices:             (target.devices ?? {}) as BuildDevices,
    adminNotes:          target.adminNotes ?? '',
  };
}
