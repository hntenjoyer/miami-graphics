import { create } from 'zustand';
import type {
  ReduxItem, CustomizationDraftBridge,
  GenericSettingBridge, MinimapSettingBridge, TracerSettingBridge, MinimapTweaks,
} from '@/bridge/types';
import { bridge } from '@/bridge';
import i18n from '@/i18n';
import { ensureBackupOrGate } from '@/store/installGate';
import { useReduxStore } from '@/store/reduxStore';
import { getCachedLibrary } from '@/store/libraryCache';
import { getArmorLibraryCache } from '@/store/armorLibraryCache';
import { startCustomizeInstall, finishCustomizeInstall } from '@/store/installProgressStore';

type LibKind = 'minimap' | 'crosshair' | 'tracers' | 'bloodfx' | 'timecycle' | 'armor' | 'arena';
interface NameResolvers {
  redux: (id: string) => string;
  lib:   (kind: LibKind, id: string) => string;
  armor: (id: string) => string;
}

export type CustomizeComponentName =
  | 'bloodfx'
  | 'crosshair'
  | 'minimap'
  | 'timecycle'
  | 'armor'
  | 'tracers'
  | 'arena';

export const MINIMAP_ASPECT_RATIOS = ['16:9', '4:3', '1:1', '5:4', '5:3', '3:2'] as const;
export type MinimapAspectRatio = typeof MINIMAP_ASPECT_RATIOS[number];

export const MINIMAP_POSITIONS = ['default', 'center'] as const;
export type MinimapPosition = typeof MINIMAP_POSITIONS[number];

import gucciPng       from '@/assets/tracers/gucci.png';
import miniPng        from '@/assets/tracers/mini.png';
import uziPng         from '@/assets/tracers/uzi.png';
import plasmaPng      from '@/assets/tracers/plasma.png';
import plasmaSmokePng from '@/assets/tracers/plasma-smoke.png';
import dissolvingPng  from '@/assets/tracers/dissolving.png';
import crispPng       from '@/assets/tracers/crisp.png';
import crispSmokePng  from '@/assets/tracers/crisp-smoke.png';

export interface TracerModelDescriptor {
  id:         string;
  folderName: string;
  name:       string;
  preview:    string;
}

export const TRACER_MODELS: TracerModelDescriptor[] = [
  { id: 'gucci',        folderName: 'gucci',         name: 'Gucci',            preview: gucciPng       },
  { id: 'mini',         folderName: 'mini',          name: 'Mini tracer',      preview: miniPng        },
  { id: 'uzi',          folderName: 'uzi',           name: 'Uzi tracer',       preview: uziPng         },
  { id: 'plasma',       folderName: 'plasma',
    get name() { return i18n.t('customize.tracers.models.plasma', 'Плазма'); },
    preview: plasmaPng      },
  { id: 'plasma-smoke', folderName: 'plasma-smoke',
    get name() { return i18n.t('customize.tracers.models.plasmaSmoke', 'Плазма Дым'); },
    preview: plasmaSmokePng },
  { id: 'dissolving',   folderName: 'dissolving',
    get name() { return i18n.t('customize.tracers.models.dissolving', 'Растворяющийся'); },
    preview: dissolvingPng  },
  { id: 'crisp',        folderName: 'crisp',
    get name() { return i18n.t('customize.tracers.models.crisp', 'Чёткие'); },
    preview: crispPng       },
  { id: 'crisp-smoke',  folderName: 'crisp-smoke',
    get name() { return i18n.t('customize.tracers.models.crispSmoke', 'Чёткие с дымом'); },
    preview: crispSmokePng  },
];

export type GenericSetting =
  | { kind: 'default' }
  | { kind: 'library'; libraryItemId: string; libraryItemName: string; libraryItemAuthor: string }
  | { kind: 'import'; donorReduxId: string; donorReduxName: string;
      donorVersionId: string | null;
      donorVersionLabel: string | null;
    }
  | { kind: 'armorLibrary'; armorLibraryId: string; armorLibraryName: string }
  | { kind: 'clear' };

export interface MinimapSetting {
  enabled: boolean;
  colorsEnabled: boolean;
  hpColor: string;
  armorColor: string;
  aspectRatio: MinimapAspectRatio;
  position: MinimapPosition;
  pngOverlayPath: string | null;
  tweaks: MinimapTweaks;
  importedFrom: {
    reduxId: string;
    reduxName: string;
    versionId: string | null;
    versionLabel: string | null;
  } | null;
  librarySource: { libraryItemId: string; libraryItemName: string } | null;
}

export interface TracerSetting {
  source:
    | { kind: 'default' }
    | { kind: 'model'; modelId: string }
    | { kind: 'import'; donorReduxId: string; donorReduxName: string;
        donorVersionId: string | null;
        donorVersionLabel: string | null;
      };
  rgb: { r: number; g: number; b: number };
  takeDonorBlood?: boolean;
  useCleanEffects?: boolean;
  overrideColor?: boolean;
}

export interface CustomizationDraft {
  reduxId:   string;
  baseVersionId?: string;
  bloodfx:   GenericSetting;
  crosshair: GenericSetting;
  timecycle: GenericSetting;
  armor:     GenericSetting;
  arena:     GenericSetting;
  minimap:   MinimapSetting;
  tracers:   TracerSetting;
  bigMap: { id: string; name: string } | null;
}

type View =
  | { kind: 'manage' }
  | { kind: 'component';      component: CustomizeComponentName }
  | { kind: 'import-picker';  forComponent: CustomizeComponentName }
  | { kind: 'import-preview'; forComponent: CustomizeComponentName; donorReduxId: string }
  | { kind: 'armor-library-preview'; armorLibraryId: string; armorLibraryName: string;
      previewUrl: string | null; glbUrl: string | null }
  | { kind: 'library-picker'; forComponent: CustomizeComponentName }
  | { kind: 'bigmap-picker' };

interface CustomizeState {

  activeReduxId:   string | null;
  activeReduxName: string | null;

  activeRedux:     ReduxItem | null;
  draft: CustomizationDraft | null;
  view: View;
  manageIntroSeen: boolean;
  markManageIntroSeen: () => void;

  componentIntroSeen: Partial<Record<CustomizeComponentName, boolean>>;
  markComponentIntroSeen: (component: CustomizeComponentName) => void;

  open:  (item: ReduxItem, baseVersionId?: string) => void;
  close: () => void;

  goManage:           () => void;
  openComponent:      (component: CustomizeComponentName) => void;
  openImportPicker:   (forComponent: CustomizeComponentName) => void;
  openImportPreview:  (forComponent: CustomizeComponentName, donorReduxId: string) => void;
  openArmorLibraryPreview: (armor: { id: string; name: string; previewUrl: string | null; glbUrl: string | null }) => void;
  openLibraryPicker:  (forComponent: CustomizeComponentName) => void;
  openBigMapPicker:   () => void;
  setBigMap:          (v: { id: string; name: string } | null) => void;

  setGeneric: (
    component: 'bloodfx' | 'crosshair' | 'timecycle' | 'armor' | 'arena',
    setting: GenericSetting,
  ) => void;
  setMinimap: (next: Partial<MinimapSetting>) => void;
  setTracer:  (next: Partial<TracerSetting>) => void;

  resetComponent: (component: CustomizeComponentName) => void;
  resetAll: () => void;

  apply: () => Promise<{ ok: boolean; message: string }>;
}

const DEFAULT_GENERIC: GenericSetting = { kind: 'default' };

export const STOCK_MINIMAP_TWEAKS: MinimapTweaks = {
  digits: false, digitsHpColor: null, digitsArmorColor: null,
  digitsX: 10, digitsY: 103, digitsScale: 100,
  digitsHpDx: 0, digitsHpDy: 0, digitsArmorDx: 22, digitsArmorDy: 0,
  digitsBigDx: 0, digitsBigDy: 0,
  damagePopup: false, damageColor: '#FF4040',
  healPopup: false, healColor: '#34D399',
  popupSize: 18, popupSeconds: 1.0, popupX: 46, popupY: 34,
  lowHpThreshold: null, lowHpColor: null,
  hitAlpha: null, hitFadeSeconds: null, hitScale: null,
  barPosition: 'default', barOffsetX: 0, barOffsetY: 0,
  armorPopup: false, armorPopupColor: '#60A5FA', armorPopupX: 46, armorPopupY: 54,
  hitPngPath: null,
  customText: null, customTextColor: '#FFFFFF', customTextX: 4, customTextY: 2, customTextScale: 100,
  barScale: 100, barHpColor: null, barArmorColor: null, barPulseLowHp: false, barHpGradient: false,
  barGradFullColor: null, barGradMidColor: null, barGradLowColor: null,
  barScaleY: null,
  barHpTroughColor: null, barArmorTroughColor: null,
  hideNorth: false,
  digitsFont: null,
  hitX: null, hitY: null,
  arrowPngPath: null, gpsPngPath: null,
};

export function tweaksDifferFromStock(t: MinimapTweaks | null | undefined): boolean {
  if (!t) return false;
  const stock = STOCK_MINIMAP_TWEAKS as unknown as Record<string, unknown>;
  const cur = t as unknown as Record<string, unknown>;
  return Object.keys(stock).some(k => cur[k] !== undefined && cur[k] !== stock[k]);
}

export const DEFAULT_MINIMAP_TWEAKS: MinimapTweaks = {
  ...STOCK_MINIMAP_TWEAKS,
  barPosition: 'top',
  barScaleY: 80,
  barHpColor: '#FFFFFF',
  barArmorColor: '#97DCFF',
};

const DEFAULT_MINIMAP: MinimapSetting = {
  enabled:     false,
  colorsEnabled: false,
  hpColor:     '#34D399',
  armorColor:  '#60A5FA',
  aspectRatio: '16:9',
  position:    'default',
  pngOverlayPath: null,
  tweaks:      DEFAULT_MINIMAP_TWEAKS,
  importedFrom:   null,
  librarySource:  null,
};

const DEFAULT_TRACER: TracerSetting = {
  source: { kind: 'default' },
  rgb: { r: 255, g: 255, b: 255 },
  takeDonorBlood: false,
  useCleanEffects: false,
  overrideColor: false,
};

function freshDraft(reduxId: string, baseVersionId?: string): CustomizationDraft {
  return {
    reduxId,
    baseVersionId,
    bloodfx:   DEFAULT_GENERIC,
    crosshair: DEFAULT_GENERIC,
    timecycle: DEFAULT_GENERIC,
    armor:     DEFAULT_GENERIC,
    arena:     DEFAULT_GENERIC,
    minimap:   { ...DEFAULT_MINIMAP },
    tracers:   { ...DEFAULT_TRACER, source: { ...DEFAULT_TRACER.source }, rgb: { ...DEFAULT_TRACER.rgb } },
    bigMap:    null,
  };
}

function inflateGeneric(g: GenericSettingBridge, kind: LibKind, r: NameResolvers): GenericSetting {
  if (g.kind === 'library' && g.libraryItemId)
    return { kind: 'library', libraryItemId: g.libraryItemId, libraryItemName: r.lib(kind, g.libraryItemId), libraryItemAuthor: '' };
  if (g.kind === 'import' && g.donorReduxId)
    return { kind: 'import', donorReduxId: g.donorReduxId, donorReduxName: r.redux(g.donorReduxId),
             donorVersionId: g.donorVersionId ?? null, donorVersionLabel: null };
  if (g.kind === 'armorLibrary' && g.armorLibraryId)
    return { kind: 'armorLibrary', armorLibraryId: g.armorLibraryId, armorLibraryName: r.armor(g.armorLibraryId) };
  if (g.kind === 'clear') return { kind: 'clear' };
  return { kind: 'default' };
}

function inflateMinimap(m: MinimapSettingBridge, r: NameResolvers): MinimapSetting {
  const aspect = (MINIMAP_ASPECT_RATIOS as readonly string[]).includes(m.aspectRatio)
    ? (m.aspectRatio as MinimapAspectRatio) : '16:9';
  const position = (MINIMAP_POSITIONS as readonly string[]).includes(m.position)
    ? (m.position as MinimapPosition) : 'default';
  const colorsEnabled =
    m.hpColor?.toUpperCase()    !== '#34D399' ||
    m.armorColor?.toUpperCase() !== '#60A5FA';
  return {
    enabled:     m.enabled,
    colorsEnabled,
    hpColor:     m.hpColor,
    armorColor:  m.armorColor,
    aspectRatio: aspect,
    position,
    pngOverlayPath: m.pngOverlayPath ?? null,
    tweaks: m.tweaks ? { ...STOCK_MINIMAP_TWEAKS, ...m.tweaks } : DEFAULT_MINIMAP_TWEAKS,
    importedFrom: m.importedFromReduxId
      ? { reduxId: m.importedFromReduxId, reduxName: r.redux(m.importedFromReduxId),
          versionId: m.donorVersionId ?? null, versionLabel: null }
      : null,
    librarySource: m.libraryItemId
      ? { libraryItemId: m.libraryItemId, libraryItemName: r.lib('minimap', m.libraryItemId) }
      : null,
  };
}

function inflateTracers(t: TracerSettingBridge, r: NameResolvers): TracerSetting {
  const rgb = { r: t.r, g: t.g, b: t.b };
  if (t.sourceKind === 'model' && t.modelFolderName) {
    const desc = TRACER_MODELS.find(m => m.folderName === t.modelFolderName);
    return { source: { kind: 'model', modelId: desc?.id ?? t.modelFolderName }, rgb };
  }
  if (t.sourceKind === 'import' && t.donorReduxId) {
    return { source: { kind: 'import', donorReduxId: t.donorReduxId, donorReduxName: r.redux(t.donorReduxId),
                       donorVersionId: t.donorVersionId ?? null, donorVersionLabel: null }, rgb,
             takeDonorBlood: t.takeDonorBlood ?? false, useCleanEffects: t.useCleanEffects ?? false,
             overrideColor: t.overrideColor ?? false };
  }
  return { source: { kind: 'default' }, rgb };
}

function inflateDraft(w: CustomizationDraftBridge, r: NameResolvers): CustomizationDraft {
  return {
    reduxId:   w.reduxId,
    baseVersionId: w.baseVersionId,
    bloodfx:   inflateGeneric(w.bloodfx,   'bloodfx',   r),
    crosshair: inflateGeneric(w.crosshair, 'crosshair', r),
    timecycle: inflateGeneric(w.timecycle, 'timecycle', r),
    armor:     inflateGeneric(w.armor,     'armor',     r),
    arena:     inflateGeneric(w.arena,     'arena',     r),
    minimap:   inflateMinimap(w.minimap, r),
    tracers:   inflateTracers(w.tracers, r),
    bigMap:    null,
  };
}

function buildNameResolvers(): NameResolvers {
  const reduxItems = useReduxStore.getState().items;
  return {
    redux: (id) => reduxItems.find(i => i.id === id)?.name || id,
    lib:   (kind, id) => getCachedLibrary(kind)?.find(i => i.id === id)?.name || id,
    armor: (id) => getArmorLibraryCache().find(a => a.id === id)?.name || id,
  };
}

function isUntouchedDefault(
  draft: CustomizationDraft | null, reduxId: string, baseVersionId?: string,
): boolean {
  if (!draft) return false;
  return JSON.stringify(draft) === JSON.stringify(freshDraft(reduxId, baseVersionId));
}

export const useCustomizeStore = create<CustomizeState>((set, get) => ({
  activeReduxId:   null,
  activeReduxName: null,
  activeRedux:     null,
  draft: null,
  view: { kind: 'manage' },
  manageIntroSeen: false,
  markManageIntroSeen: () => set({ manageIntroSeen: true }),
  componentIntroSeen: {},
  markComponentIntroSeen: (component) => set(s => ({
    componentIntroSeen: { ...s.componentIntroSeen, [component]: true },
  })),

  open: (item, baseVersionId) => {
    set({
      activeReduxId:   item.id,
      activeReduxName: item.name || item.id,
      activeRedux:     item,
      draft: freshDraft(item.id, baseVersionId),
      view: { kind: 'manage' },
      manageIntroSeen: false,
      componentIntroSeen: {},
    });

    if (typeof bridge.getInstalledDraft !== 'function') return;
    void (async () => {
      try {
        const installed = await bridge.getInstalledDraft();
        if (!installed || installed.reduxId !== item.id) return;
        const s = get();
        if (s.activeReduxId !== item.id) return;
        if (!isUntouchedDefault(s.draft, item.id, baseVersionId)) return;
        set({ draft: inflateDraft(installed, buildNameResolvers()) });
      } catch (e) {
        console.warn('[customize] getInstalledDraft hydration failed', e);
      }
    })();
  },

  close: () => set({
    activeReduxId:   null,
    activeReduxName: null,
    activeRedux:     null,
    draft: null,
    view: { kind: 'manage' },
    manageIntroSeen: false,
    componentIntroSeen: {},
  }),

  goManage:           () => set({ view: { kind: 'manage' } }),
  openComponent:      (component) => set({ view: { kind: 'component', component } }),
  openImportPicker:   (forComponent) => set({ view: { kind: 'import-picker', forComponent } }),
  openImportPreview:  (forComponent, donorReduxId) =>
                      set({ view: { kind: 'import-preview', forComponent, donorReduxId } }),
  openArmorLibraryPreview: (armor) =>
                      set({ view: { kind: 'armor-library-preview', armorLibraryId: armor.id,
                                    armorLibraryName: armor.name, previewUrl: armor.previewUrl, glbUrl: armor.glbUrl } }),
  openLibraryPicker:  (forComponent) => set({ view: { kind: 'library-picker', forComponent } }),
  openBigMapPicker:   () => set({ view: { kind: 'bigmap-picker' } }),
  setBigMap: (v) => {
    const draft = get().draft;
    if (!draft) return;
    set({ draft: { ...draft, bigMap: v } });
  },

  setGeneric: (component, setting) => {
    const draft = get().draft;
    if (!draft) return;
    set({ draft: { ...draft, [component]: setting } });
  },

  setMinimap: (next) => {
    const draft = get().draft;
    if (!draft) return;
    set({ draft: { ...draft, minimap: { ...draft.minimap, ...next } } });
  },

  setTracer: (next) => {
    const draft = get().draft;
    if (!draft) return;
    set({ draft: { ...draft, tracers: { ...draft.tracers, ...next } } });
  },

  resetComponent: (component) => {
    const draft = get().draft;
    if (!draft) return;
    if (component === 'minimap') {
      set({ draft: { ...draft, minimap: { ...DEFAULT_MINIMAP } } });
      return;
    }
    if (component === 'tracers') {
      set({
        draft: {
          ...draft,
          tracers: { ...DEFAULT_TRACER, source: { ...DEFAULT_TRACER.source }, rgb: { ...DEFAULT_TRACER.rgb } },
        },
      });
      return;
    }
    set({ draft: { ...draft, [component]: DEFAULT_GENERIC } });
  },

  resetAll: () => {
    const draft = get().draft;
    if (!draft) return;
    set({ draft: freshDraft(draft.reduxId) });
  },

  apply: async () => {
    const draft = get().draft;
    if (!draft) return { ok: false, message: 'no draft' };

    if (!ensureBackupOrGate()) return { ok: false, message: 'BACKUP_REQUIRED' };

    const wire: CustomizationDraftBridge = {
      reduxId:   draft.reduxId,
      baseVersionId: draft.baseVersionId,
      bloodfx:   flatGeneric(draft.bloodfx),
      crosshair: flatGeneric(draft.crosshair),
      timecycle: flatGeneric(draft.timecycle),
      armor:     flatGeneric(draft.armor),
      arena:     flatGeneric(draft.arena),
      minimap: {
        enabled:             draft.minimap.enabled,
        hpColor:             draft.minimap.colorsEnabled ? draft.minimap.hpColor    : '#34D399',
        armorColor:          draft.minimap.colorsEnabled ? draft.minimap.armorColor : '#60A5FA',
        aspectRatio:         draft.minimap.aspectRatio,
        position:            draft.minimap.position,
        pngOverlayPath:      draft.minimap.pngOverlayPath,
        importedFromReduxId: draft.minimap.importedFrom?.reduxId ?? null,
        donorVersionId:      draft.minimap.importedFrom?.versionId ?? null,

        libraryItemId:       draft.minimap.librarySource?.libraryItemId ?? null,
        tweaks:              tweaksDifferFromStock(draft.minimap.tweaks) ? draft.minimap.tweaks : null,
      },
      tracers: flatTracers(draft.tracers),
    };

    const hasBigMap = !!(draft.bigMap && typeof bridge.bigMapInstall === 'function');
    if (hasBigMap) startCustomizeInstall(draft.reduxId);

    try {
      const r = await bridge.reduxCustomizeApply(draft.reduxId, wire);
      if (r.success) {
        useReduxStore.getState().markInstalled(draft.reduxId);
        bumpDonorPickCounters(draft);
        if (draft.bigMap && typeof bridge.bigMapInstall === 'function') {
          try {
            const bm = await bridge.bigMapInstall(draft.bigMap.id);
            if (!bm.success) {
              finishCustomizeInstall(draft.reduxId, 'error', bm.errorMessage ?? null);
              return { ok: false, message: bm.errorMessage
                ?? i18n.t(
                  'customize.bigMap.applyFailed',
                  'Редукс применён, но большая карта «{{name}}» не установилась.',
                  { name: draft.bigMap.name },
                ) };
            }
          } catch (e) {
            finishCustomizeInstall(draft.reduxId, 'error', (e as Error).message);
            return { ok: false, message: i18n.t(
              'customize.bigMap.applyFailedReason',
              'Редукс применён, но большая карта не установилась: {{reason}}',
              { reason: (e as Error).message },
            ) };
          }
        }
        if (hasBigMap) finishCustomizeInstall(draft.reduxId, 'done');
      } else if (hasBigMap) {
        finishCustomizeInstall(draft.reduxId, 'error', r.errorMessage ?? null);
      }
      return {
        ok: r.success,
        message: r.errorMessage ?? (r.success
          ? i18n.t('customize.applied', 'Кастомизация применена.')
          : i18n.t('customize.applyFailed', 'Не удалось применить кастомизацию.')),
      };
    } catch (e) {
      if (hasBigMap) finishCustomizeInstall(draft.reduxId, 'error', (e as Error).message);
      return { ok: false, message: (e as Error).message };
    }
  },
}));

function bumpDonorPickCounters(draft: CustomizationDraft): void {
  if (typeof bridge.donorPickIncrement !== 'function') return;
  const picks: Array<[component: string, donorReduxId: string]> = [];
  const generic = (component: string, s: GenericSetting) => {
    if (s.kind === 'import' && s.donorReduxId) picks.push([component, s.donorReduxId]);
  };
  generic('bloodfx',   draft.bloodfx);
  generic('crosshair', draft.crosshair);
  generic('timecycle', draft.timecycle);
  generic('armor',     draft.armor);
  generic('arena',     draft.arena);
  if (draft.minimap.importedFrom?.reduxId) picks.push(['minimap', draft.minimap.importedFrom.reduxId]);
  if (draft.tracers.source.kind === 'import') picks.push(['tracers', draft.tracers.source.donorReduxId]);
  for (const [component, donor] of picks) {
    void bridge.donorPickIncrement(donor, component).catch(() => {});
  }
}

function flatGeneric(g: GenericSetting): {
  kind: GenericSetting['kind'];
  libraryItemId?: string;
  donorReduxId?: string;
  donorVersionId?: string | null;
  armorLibraryId?: string;
} {
  if (g.kind === 'library') return { kind: 'library', libraryItemId: g.libraryItemId };
  if (g.kind === 'import')  return { kind: 'import',  donorReduxId:  g.donorReduxId, donorVersionId: g.donorVersionId };
  if (g.kind === 'armorLibrary') return { kind: 'armorLibrary', armorLibraryId: g.armorLibraryId };
  if (g.kind === 'clear')   return { kind: 'clear' };
  return { kind: 'default' };
}

function flatTracers(t: TracerSetting): {
  sourceKind: 'default' | 'model' | 'import';
  modelFolderName: string | null;
  donorReduxId: string | null;
  r: number; g: number; b: number;
  donorVersionId: string | null;
  takeDonorBlood?: boolean;
  useCleanEffects?: boolean;
  overrideColor?: boolean;
} {
  const rgb = { r: t.rgb.r, g: t.rgb.g, b: t.rgb.b };
  if (t.source.kind === 'default') {
    return { sourceKind: 'default', modelFolderName: null, donorReduxId: null, donorVersionId: null, ...rgb };
  }
  if (t.source.kind === 'model') {

    const src = t.source;
    const desc = TRACER_MODELS.find(m => m.id === src.modelId);
    return {
      sourceKind: 'model',
      modelFolderName: desc?.folderName ?? null,
      donorReduxId: null,
      donorVersionId: null,
      ...rgb,
    };
  }
  return {
    sourceKind: 'import',
    modelFolderName: null,
    donorReduxId: t.source.donorReduxId,
    donorVersionId: t.source.donorVersionId,
    takeDonorBlood: t.takeDonorBlood ?? false,
    useCleanEffects: t.useCleanEffects ?? false,
    overrideColor: t.overrideColor ?? false,
    ...rgb,
  };
}
