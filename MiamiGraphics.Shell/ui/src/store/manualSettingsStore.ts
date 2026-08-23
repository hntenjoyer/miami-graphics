import { create } from 'zustand';
import { bridge } from '@/bridge';
import i18n from '@/i18n';
import type { GtaSettingsModel, GtaPresetApplyResult } from '@/bridge/types';
import { useGtaSettingsStore } from './gtaSettingsStore';

export type ManualCategory = 'display' | 'quality' | 'antiAliasing' | 'world' | 'advanced';

const ALL_CATEGORIES: readonly ManualCategory[] = ['display', 'quality', 'antiAliasing', 'world', 'advanced'];

interface DirtyComputation {
  keys: Set<string>;
  counts: Record<ManualCategory, number>;
  total: number;
}

const ZERO_COUNTS: Record<ManualCategory, number> = {
  display: 0, quality: 0, antiAliasing: 0, world: 0, advanced: 0,
};

function recomputeDirty(
  draft:    GtaSettingsModel | null,
  baseline: GtaSettingsModel | null,
): DirtyComputation {
  const keys = new Set<string>();
  const counts: Record<ManualCategory, number> = { ...ZERO_COUNTS };
  if (!draft || !baseline) return { keys, counts, total: 0 };
  let total = 0;
  for (const cat of ALL_CATEGORIES) {
    const d = draft[cat]    as unknown as Record<string, unknown>;
    const b = baseline[cat] as unknown as Record<string, unknown>;
    for (const k of Object.keys(d)) {
      if (!Object.is(d[k], b[k])) {
        keys.add(`${cat}.${k}`);
        counts[cat]++;
        total++;
      }
    }
  }
  return { keys, counts, total };
}

interface ManualSettingsState {

  baseline:        GtaSettingsModel | null;
  baselineExisted: boolean;
  sourcePath:      string;
  loading:         boolean;
  loadError:       string | null;

  draft:           GtaSettingsModel | null;

  liveGainPercent:     number;
  baselineGainPercent: number;

  dirtyKeys:             Set<string>;
  dirtyCountByCategory:  Record<ManualCategory, number>;
  dirtyTotal:            number;

  applying:        boolean;
  lastApplyResult: GtaPresetApplyResult | null;

  load:    () => Promise<void>;
  patch:   <K extends ManualCategory>(category: K, partial: Partial<GtaSettingsModel[K]>) => void;
  reset:   () => void;
  apply:   () => Promise<GtaPresetApplyResult>;
}

export const useManualSettingsStore = create<ManualSettingsState>((set, get) => {

  let analyzeTimer: number | null = null;
  const scheduleAnalyze = () => {
    if (analyzeTimer !== null) window.clearTimeout(analyzeTimer);
    analyzeTimer = window.setTimeout(async () => {
      analyzeTimer = null;
      const { draft, baselineGainPercent } = get();
      if (!draft) return;
      try {
        const result = await bridge.gtaSettingsAnalyzeModel(draft);

        if (get().draft === draft) {

          const delta = Math.max(0, result.gainPercent - baselineGainPercent);
          set({ liveGainPercent: delta });
        }
      } catch (e) {
        console.warn('[manualSettings] analyze failed', e);
      }
    }, 200);
  };

  return {
    baseline:           null,
    baselineExisted:    false,
    sourcePath:         '',
    loading:            false,
    loadError:          null,
    draft:              null,
    liveGainPercent:    0,
    baselineGainPercent: 0,
    dirtyKeys:           new Set<string>(),
    dirtyCountByCategory: { ...ZERO_COUNTS },
    dirtyTotal:          0,
    applying:           false,
    lastApplyResult:    null,

    load: async () => {
      set({ loading: true, loadError: null });
      try {
        const result = await bridge.gtaSettingsRead();
        set({
          baseline:        result.model,
          baselineExisted: result.existedOnDisk,
          sourcePath:      result.sourcePath,
          draft:           structuredClone(result.model),
          loading:         false,

          liveGainPercent: 0,

          dirtyKeys:            new Set<string>(),
          dirtyCountByCategory: { ...ZERO_COUNTS },
          dirtyTotal:           0,
        });

        try {
          const baselineResult = await bridge.gtaSettingsAnalyzeModel(result.model);
          set({ baselineGainPercent: baselineResult.gainPercent });
        } catch (e) {
          console.warn('[manualSettings] baseline analyze failed (delta will be off)', e);
          set({ baselineGainPercent: 0 });
        }

        scheduleAnalyze();
      } catch (e) {
        set({ loading: false, loadError: e instanceof Error ? e.message : String(e) });
      }
    },

    patch: (category, partial) => {
      const { draft, baseline } = get();
      if (!draft) return;

      const nextSection = { ...(draft[category] as object), ...partial } as GtaSettingsModel[typeof category];
      const nextDraft: GtaSettingsModel = { ...draft, [category]: nextSection };
      const dirty = recomputeDirty(nextDraft, baseline);
      set({
        draft: nextDraft,
        dirtyKeys:            dirty.keys,
        dirtyCountByCategory: dirty.counts,
        dirtyTotal:           dirty.total,
      });
      scheduleAnalyze();
    },

    reset: () => {
      const { baseline } = get();
      if (!baseline) return;
      set({
        draft: structuredClone(baseline),
        dirtyKeys:            new Set<string>(),
        dirtyCountByCategory: { ...ZERO_COUNTS },
        dirtyTotal:           0,
      });
      scheduleAnalyze();
    },

    apply: async () => {
      const { draft } = get();
      if (!draft) {
        const fail: GtaPresetApplyResult = {
          success: false,
          errorMessage: i18n.t('manualSettings.noDataToWrite', 'Нет данных для записи.'),
          targetPath: '', backupPath: null, gameWasRunning: false,
        };
        return fail;
      }
      set({ applying: true });
      try {
        const result = await bridge.gtaSettingsWrite(draft);
        if (result.success) {

          set({
            baseline:        structuredClone(draft),
            baselineExisted: true,
            applying:        false,
            lastApplyResult: result,
            liveGainPercent: 0,

            dirtyKeys:            new Set<string>(),
            dirtyCountByCategory: { ...ZERO_COUNTS },
            dirtyTotal:           0,
          });

          useGtaSettingsStore.getState().setInstalledPreset(null);
          try {
            const baselineResult = await bridge.gtaSettingsAnalyzeModel(draft);
            set({ baselineGainPercent: baselineResult.gainPercent });
          } catch {  }
        } else {
          set({ applying: false, lastApplyResult: result });
        }
        return result;
      } catch (e) {
        const fail: GtaPresetApplyResult = {
          success: false,
          errorMessage: e instanceof Error ? e.message : String(e),
          targetPath: '', backupPath: null, gameWasRunning: false,
        };
        set({ applying: false, lastApplyResult: fail });
        return fail;
      }
    },
  };
});

export function modelsEqual(a: GtaSettingsModel | null, b: GtaSettingsModel | null): boolean {
  if (a === b) return true;
  if (!a || !b) return false;

  return JSON.stringify(a) === JSON.stringify(b);
}
