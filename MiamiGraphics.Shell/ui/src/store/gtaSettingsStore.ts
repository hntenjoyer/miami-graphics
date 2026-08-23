import { create } from 'zustand';
import { bridge } from '@/bridge';
import { shuffled } from '@/utils/shuffle';
import type {
  GtaPreset, GtaPresetUploadRequest, GtaPresetPatch,
  GtaPresetApplyResult, GtaSettingsAnalysis,
} from '@/bridge/types';

const INSTALLED_PRESET_KEY = 'hntgraph.installedPresetId';

let presetsShuffleApplied = false;

function loadInstalledPresetId(): string | null {
  try {
    const v = window.localStorage.getItem(INSTALLED_PRESET_KEY);
    return v && typeof v === 'string' ? v : null;
  } catch { return null; }
}
function persistInstalledPresetId(id: string | null) {
  try {
    if (id) window.localStorage.setItem(INSTALLED_PRESET_KEY, id);
    else    window.localStorage.removeItem(INSTALLED_PRESET_KEY);
  } catch {  }
}

interface GtaSettingsState {

  publicPresets:        GtaPreset[];
  loadingPublic:        boolean;
  loadPublicPresets:    (search?: string | null) => Promise<void>;

  applyingId:           string | null;
  lastApplyResult:      GtaPresetApplyResult | null;
  applyPreset:          (id: string) => Promise<GtaPresetApplyResult>;

  installedPresetId:    string | null;
  setInstalledPreset:   (id: string | null) => void;

  adminPresets:         GtaPreset[];
  loadingAdmin:         boolean;
  loadAdminPresets:     () => Promise<void>;

  uploadPreset:         (req: GtaPresetUploadRequest) => Promise<GtaPreset>;
  patchPreset:          (id: string, patch: GtaPresetPatch) => Promise<void>;
  deletePreset:         (id: string) => Promise<void>;

  analyzeXml:           (sourceXmlPath: string) => Promise<GtaSettingsAnalysis>;
}

export const useGtaSettingsStore = create<GtaSettingsState>((set, get) => ({
  publicPresets: [],
  loadingPublic: false,
  applyingId: null,
  lastApplyResult: null,
  adminPresets: [],
  loadingAdmin: false,
  installedPresetId: loadInstalledPresetId(),

  setInstalledPreset: (id) => {
    persistInstalledPresetId(id);
    set({ installedPresetId: id });
  },

  loadPublicPresets: async (search = null) => {
    if (presetsShuffleApplied && !search && get().publicPresets.length > 0) return;

    set({ loadingPublic: true });
    try {
      const presets = await bridge.gtaPresetsList(search);
      const finalPresets = !search ? shuffled(presets) : presets;
      if (!search && presets.length > 0) presetsShuffleApplied = true;
      set({ publicPresets: finalPresets, loadingPublic: false });
    } catch (e) {
      console.warn('[gtaSettingsStore] load public presets failed', e);
      set({ loadingPublic: false });
    }
  },

  applyPreset: async (id) => {
    set({ applyingId: id });
    try {
      const result = await bridge.gtaPresetApply(id);

      if (result.success) {
        const preset = get().publicPresets.find(p => p.id === id);
        if (preset) void bridge.activityLog('preset_install', `пресет настроек «${preset.name}»`, id);
        persistInstalledPresetId(id);
        set({ applyingId: null, lastApplyResult: result, installedPresetId: id });
      } else {
        set({ applyingId: null, lastApplyResult: result });
      }

      if (result.success) {
        try {
          const newTotal = await bridge.gtaPresetIncrementDownloads(id);
          set(state => ({
            publicPresets: state.publicPresets.map(p =>
              p.id === id ? { ...p, downloadCount: newTotal } : p),
          }));
        } catch {  }
      }
      return result;
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      const result: GtaPresetApplyResult = {
        success: false,
        errorMessage: msg,
        targetPath: '',
        backupPath: null,
        gameWasRunning: false,
      };
      set({ applyingId: null, lastApplyResult: result });
      return result;
    }
  },

  loadAdminPresets: async () => {
    set({ loadingAdmin: true });
    try {
      const presets = await bridge.adminGtaPresetList();
      set({ adminPresets: presets, loadingAdmin: false });
    } catch (e) {
      console.warn('[gtaSettingsStore] load admin presets failed', e);
      set({ loadingAdmin: false });
    }
  },

  uploadPreset: async (req) => {
    const row = await bridge.adminGtaPresetUpload(req);
    set(state => ({
      adminPresets: [row, ...state.adminPresets.filter(p => p.id !== row.id)],
    }));
    return row;
  },

  patchPreset: async (id, patch) => {
    await bridge.adminGtaPresetPatch(id, patch);

    const apply = (p: GtaPreset): GtaPreset => p.id === id ? { ...p, ...stripUndefined(patch) } as GtaPreset : p;
    set(state => ({
      adminPresets:  state.adminPresets.map(apply),
      publicPresets: state.publicPresets.map(apply),
    }));
  },

  deletePreset: async (id) => {
    await bridge.adminGtaPresetDelete(id);
    set(state => ({
      adminPresets:  state.adminPresets.filter(p => p.id !== id),
      publicPresets: state.publicPresets.filter(p => p.id !== id),
    }));
  },

  analyzeXml: (sourceXmlPath) => bridge.adminGtaPresetAnalyze(sourceXmlPath),
}));

function stripUndefined<T extends object>(obj: T): Partial<T> {
  const out: Partial<T> = {};
  for (const k of Object.keys(obj) as (keyof T)[]) {
    if (obj[k] !== undefined) out[k] = obj[k];
  }
  return out;
}
