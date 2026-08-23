import { create } from 'zustand';
import { bridge } from '@/bridge';
import i18n from '@/i18n';
import type { ReticleSpec, ReticleWeaponGroup, ReticleWeaponOverride, CurrentReticleInfo } from '@/bridge/types';
import { DEFAULT_RETICLE, decodeReticle } from '@/utils/knkCode';

interface ReticleBuilderState {
  spec:        ReticleSpec;
  adsPreview:  boolean;
  scene:       'day' | 'night';
  applying:    boolean;
  current:     CurrentReticleInfo | null;

  set:  <K extends keyof ReticleSpec>(key: K, value: ReticleSpec[K]) => void;
  patch: (partial: Partial<ReticleSpec>) => void;
  reset: () => void;
  patchOverride: (weapon: ReticleWeaponGroup, partial: Partial<Omit<ReticleWeaponOverride, 'weapon'>>) => void;
  removeOverride: (weapon: ReticleWeaponGroup) => void;
  setAdsPreview: (v: boolean) => void;
  setScene: (s: 'day' | 'night') => void;

  loadSpec: (spec: ReticleSpec) => void;
  loadFromLegacyCode: (code: string) => boolean;
  refreshCurrent: () => Promise<void>;
  apply: () => Promise<{ success: boolean; error?: string }>;
}

export const useReticleBuilderStore = create<ReticleBuilderState>((set, get) => ({
  spec:       DEFAULT_RETICLE,
  adsPreview: false,
  scene:      'day',
  applying:   false,
  current:    null,

  set: (key, value) => set(st => ({ spec: { ...st.spec, [key]: value, code: key === 'code' ? (value as string) : '' } })),
  patch: (partial) => set(st => ({ spec: { ...st.spec, ...partial } })),
  reset: () => set({ spec: DEFAULT_RETICLE }),

  patchOverride: (weapon, partial) => set(st => {
    const list = st.spec.weaponOverrides ?? [];
    const existing = list.find(o => o.weapon === weapon);
    const next: ReticleWeaponOverride = existing
      ? { ...existing, ...partial }
      : {
          weapon,
          dot: st.spec.dot, dotSize: st.spec.dotSize, gap: st.spec.gap,
          length: st.spec.length, thickness: st.spec.thickness, tilt: st.spec.tilt,
          outline: st.spec.outline, outlineWidth: st.spec.outlineWidth,
          ring: st.spec.ring, ringRadius: st.spec.ringRadius, ringThickness: st.spec.ringThickness,
          colorMain: st.spec.colorMain,
          ...partial,
        };
    return { spec: { ...st.spec, code: '', weaponOverrides: [...list.filter(o => o.weapon !== weapon), next] } };
  }),
  removeOverride: (weapon) => set(st => {
    const rest = (st.spec.weaponOverrides ?? []).filter(o => o.weapon !== weapon);
    return { spec: { ...st.spec, code: '', weaponOverrides: rest.length > 0 ? rest : null } };
  }),
  setAdsPreview: (v) => set({ adsPreview: v }),
  setScene: (s) => set({ scene: s }),

  loadSpec: (spec) => set({ spec }),
  loadFromLegacyCode: (code) => {
    const decoded = decodeReticle(code);
    if (!decoded) return false;
    set({ spec: { ...decoded, code: '' } });
    return true;
  },

  refreshCurrent: async () => {
    try { set({ current: await bridge.getCurrentReticleInfo() }); }
    catch (e) { console.warn('[reticleBuilder.current] fail:', e); }
  },

  apply: async () => {
    set({ applying: true });
    try {
      const spec = get().spec;
      const r = await bridge.reticleApplyCustom(spec);
      if (r.success) { await get().refreshCurrent(); return { success: true }; }
      return {
        success: false,
        error: r.errorMessage ?? i18n.t('reticleBuilder.applyFail', 'Не удалось применить прицел'),
      };
    } catch (e) {
      return { success: false, error: (e as Error).message };
    } finally {
      set({ applying: false });
    }
  },
}));
