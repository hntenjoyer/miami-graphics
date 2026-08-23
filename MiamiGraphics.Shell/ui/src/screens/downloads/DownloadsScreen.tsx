import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Download, CheckCircle2, AlertCircle, Trash2, Package, History,
  Hourglass, Hammer, Sparkles, Box, Truck, Wrench, ArchiveRestore,
  PlugZap, FileSearch2, ShieldCheck, Layers, Zap,
  Crosshair, Volume2, RotateCcw, Loader2, BadgeCheck,
  Map as MapIcon, Shield as ShieldIcon, Target,
  Droplets, SunMedium, Trees as TreesIcon, Route as RoadsIcon, Shapes as ShapesIcon,
  Car,
  type LucideIcon,
} from 'lucide-react';
import { useInstallProgressStore, type InstallEntry } from '@/store/installProgressStore';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useLastBuildInstallStore } from '@/store/lastBuildInstallStore';
import { useKeepOverlaysStore } from '@/store/keepOverlaysStore';
import { TRACER_MODELS } from '@/store/customizeStore';
import { getCachedLibrary } from '@/store/libraryCache';
import { getArmorLibraryCache } from '@/store/armorLibraryCache';
import { useRu2QueueStore } from '@/store/ru2QueueStore';
import type { CustomizationDraftBridge } from '@/bridge/types';
import { bridge } from '@/bridge';
import type { CustomSkinApplied } from '@/bridge/types';
import { formatNoTracerScope } from '@/util/notracerScope';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { Boxes, ChevronDown, Building2, CircleDashed, Footprints, Wind, ZapOff, FastForward, Trees, Share2, Ticket, Copy, Fuel, Backpack } from 'lucide-react';
import { useHntInstallStore, type HntComponentSnap } from '@/store/hntInstallStore';
import { Toast, type ToastTone } from '@/components/Toast';
import { ArmorRollbackChoiceModal } from '@/screens/armor/ArmorRollbackChoiceModal';
import { useSessionStore } from '@/store/sessionStore';
import { HntExportModal } from '@/screens/installed/HntExportModal';
import { GunShareModal } from '@/screens/guns/GunShareModal';
import { HntImportModal } from '@/screens/installed/HntImportModal';
import { HntMyCodesPanel } from '@/screens/installed/HntMyCodesPanel';
import { useGtaSettingsStore } from '@/store/gtaSettingsStore';

function prettyReduxName(id: string | null | undefined): string {
  if (!id) return '-';
  return id.replace(/[_-]+/g, ' ').trim() || id;
}

interface CustoEntry {
  key:   string;
  icon:  LucideIcon;
  label: string;
  value: string;
}

function summarizeCustomizations(
  draft: CustomizationDraftBridge,
  reduxNameOf: (id: string) => string,
  t: (key: string, defaultValue?: string) => string,
): CustoEntry[] {
  type LibKind = 'minimap' | 'crosshair' | 'tracers' | 'bloodfx' | 'timecycle' | 'armor' | 'arena';
  const libNameOf = (kind: LibKind, id: string) =>
    getCachedLibrary(kind)?.find(x => x.id === id)?.name || t('downloads.custo.fromLibrary', 'из библиотеки');
  const armorNameOf = (id: string) =>
    getArmorLibraryCache().find(a => a.id === id)?.name || t('downloads.custo.fromLibrary', 'из библиотеки');

  const entries: CustoEntry[] = [];

  const generic = (key: 'bloodfx' | 'crosshair' | 'timecycle' | 'armor' | 'arena', icon: LucideIcon, label: string) => {
    const g = draft[key];
    if (!g || g.kind === 'default') return;
    let value: string;
    if (g.kind === 'import' && g.donorReduxId)              value = reduxNameOf(g.donorReduxId);
    else if (g.kind === 'library' && g.libraryItemId)       value = libNameOf(key, g.libraryItemId);
    else if (g.kind === 'armorLibrary' && g.armorLibraryId) value = armorNameOf(g.armorLibraryId);
    else if (g.kind === 'custom')                           value = t('downloads.custo.ownBuild', 'свой (конструктор)');
    else if (g.kind === 'clear')                            value = t('downloads.custo.cleared', 'убрано');
    else return;
    entries.push({ key, icon, label, value });
  };

  generic('bloodfx',   Droplets,   t('downloads.custo.bloodfx',   'Кровь'));
  generic('crosshair', Target,     t('downloads.custo.crosshair', 'Прицел'));
  generic('timecycle', SunMedium,  t('downloads.custo.timecycle', 'Тайм-цикл'));
  generic('armor',     ShieldIcon, t('downloads.custo.armor',     'Бронежилет'));
  generic('arena',     Building2,  t('downloads.custo.arena',     'Арена'));

  const m = draft.minimap;
  if (m) {
    const parts: string[] = [];
    if (m.libraryItemId) parts.push(libNameOf('minimap', m.libraryItemId));
    else if (m.importedFromReduxId) parts.push(reduxNameOf(m.importedFromReduxId));
    if (m.pngOverlayPath) parts.push(t('downloads.custo.ownPng', 'свой PNG'));
    if (m.enabled) {
      const colorsCustom =
        m.hpColor?.toUpperCase()    !== '#34D399' ||
        m.armorColor?.toUpperCase() !== '#60A5FA';
      if (colorsCustom) parts.push(t('downloads.custo.colors', 'цвета'));
    }
    if (parts.length > 0) {
      entries.push({
        key: 'minimap', icon: MapIcon, label: t('downloads.custo.minimap', 'Миникарта'),
        value: parts.join(' · '),
      });
    }
  }

  const tr = draft.tracers;
  if (tr && tr.sourceKind !== 'default') {
    const hex = '#' + [tr.r, tr.g, tr.b]
      .map(v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0'))
      .join('').toUpperCase();
    let value: string;
    if (tr.sourceKind === 'model') {
      const model = TRACER_MODELS.find(md => md.folderName === tr.modelFolderName);
      value = `${model?.name ?? tr.modelFolderName ?? ''} · ${hex}`;
    } else {
      const donor = tr.donorReduxId ? reduxNameOf(tr.donorReduxId) : '';
      value = tr.overrideColor ? `${donor} · ${hex}` : donor;
    }
    entries.push({ key: 'tracers', icon: Zap, label: t('downloads.custo.tracers', 'Трейсеры'), value });
  }

  if (draft.bigMapEnabled && draft.bigMapId) {
    entries.push({
      key: 'bigmap', icon: MapIcon, label: t('downloads.custo.bigmap', 'Большая карта'),
      value: draft.bigMapName || t('downloads.custo.installed', 'установлена'),
    });
  }

  return entries;
}

interface PhaseCopy { icon: LucideIcon; labelKey: string; }

const PHASE_COPY: Record<string, PhaseCopy> = {
  queued:                  { icon: Hourglass,      labelKey: 'downloads.phase.queued'                },
  starting:                { icon: Hourglass,      labelKey: 'downloads.phase.starting'              },
  downloading:             { icon: Truck,          labelKey: 'downloads.phase.downloading'           },
  verifying:               { icon: ShieldCheck,    labelKey: 'downloads.phase.verifying'             },
  extracting:              { icon: ArchiveRestore, labelKey: 'downloads.phase.extracting'            },
  injecting:               { icon: Hammer,         labelKey: 'downloads.phase.injecting'             },
  restoring_old_state:     { icon: Layers,         labelKey: 'downloads.phase.restoring_old_state'   },
  computing_diff:          { icon: FileSearch2,    labelKey: 'downloads.phase.computing_diff'        },
  downloading_new:         { icon: Truck,          labelKey: 'downloads.phase.downloading_new'       },
  installing_new:          { icon: Hammer,         labelKey: 'downloads.phase.installing_new'        },
  applying_user_changes:   { icon: Sparkles,       labelKey: 'downloads.phase.applying_user_changes' },
  resolving_version:       { icon: FileSearch2,    labelKey: 'downloads.phase.resolving_version'     },
  downloading_template:    { icon: Box,            labelKey: 'downloads.phase.downloading_template'  },
  downloading_pack:        { icon: Truck,          labelKey: 'downloads.phase.downloading_pack'      },
  preparing:               { icon: Wrench,         labelKey: 'downloads.phase.preparing'             },
  installing:              { icon: Hammer,         labelKey: 'downloads.phase.installing'            },
  registering:             { icon: PlugZap,        labelKey: 'downloads.phase.registering'           },
  restoring:               { icon: ArchiveRestore, labelKey: 'downloads.phase.restoring'             },
};

const FALLBACK_COPY: PhaseCopy = { icon: Download, labelKey: 'downloads.phase.fallback' };

function copyFor(phase: string): PhaseCopy {
  return PHASE_COPY[phase] ?? FALLBACK_COPY;
}

type OperationKind = 'install' | 'uninstall' | 'customize' | 'restore' | 'unknown';

function inferOperationKind(name: string | undefined | null): OperationKind {
  if (!name) return 'unknown';
  const n = name.toLowerCase();
  if (n.startsWith('откат') || n.startsWith('удаление')) return 'uninstall';
  if (n.startsWith('кастомизация')) return 'customize';
  if (n.startsWith('восстановление')) return 'restore';
  if (n.startsWith('установка') || n.startsWith('броня из')) return 'install';
  return 'unknown';
}

interface KindCopy {
  doneLabelKey: string;
}

const KIND_COPY: Record<OperationKind, KindCopy> = {
  install:   { doneLabelKey: 'downloads.done.install' },
  uninstall: { doneLabelKey: 'downloads.done.uninstall' },
  customize: { doneLabelKey: 'downloads.done.customize' },
  restore:   { doneLabelKey: 'downloads.done.restore' },
  unknown:   { doneLabelKey: 'downloads.done.unknown' },
};

function loadDlCache<T>(key: string): T | null {
  try { const raw = window.localStorage.getItem('hntgraph.dl.' + key); return raw ? (JSON.parse(raw) as T) : null; }
  catch { return null; }
}
function saveDlCache(key: string, val: unknown): void {
  try {
    if (val == null) window.localStorage.removeItem('hntgraph.dl.' + key);
    else window.localStorage.setItem('hntgraph.dl.' + key, JSON.stringify(val));
  } catch {  }
}

export function DownloadsScreen() {
  const { t } = useTranslation();
  const byId = useInstallProgressStore(s => s.byId);
  const dismiss = useInstallProgressStore(s => s.dismiss);
  const dismissEntry = (entry: InstallEntry) => {
    const isTerminal = entry.phase === 'done' || entry.phase === 'error';
    if (!isTerminal) void bridge.installCancel(entry.reduxId).catch(() => {});
    dismiss(entry.reduxId);
  };
  const history = useInstallProgressStore(s => s.history) ?? [];
  const clearHistory = useInstallProgressStore(s => s.clearHistory);

  const silenceProgress   = useInstallProgressStore(s => s.silenceProgress);
  const unsilenceProgress = useInstallProgressStore(s => s.unsilenceProgress);

  const reduxItems         = useReduxStore(s => s.items);
  const installedReduxId   = useReduxStore(s => s.installedReduxId);
  const reduxLoad          = useReduxStore(s => s.load);

  const reduxUninstall     = useReduxStore(s => s.uninstallForceClean);
  const reduxInstall       = useReduxStore(s => s.installForceClean);

  const installedGunpack      = useGunpackStore(s => s.installedGunpack);
  const installedSelectedGuns = useGunpackStore(s => s.installedSelectedGuns);
  const loadInstallState      = useGunpackStore(s => s.loadInstallState);

  const [soundPack, setSoundPack] = useState(() => loadDlCache<{ id: string; name: string }>('sounds'));
  const [minimap,  setMinimap]  = useState(() => loadDlCache<{ kind: 'redux' | 'library'; id: string; name: string }>('minimap'));
  const [reticle,  setReticle]  = useState(() => loadDlCache<{ kind: 'redux' | 'library' | 'custom'; id: string; name: string }>('reticle'));
  const [timecycle, setTimecycle] = useState(() => loadDlCache<{ kind: 'redux' | 'library'; id: string; name: string }>('timecycle'));
  const [trees,    setTrees]    = useState(() => loadDlCache<{ id: string; name: string }>('trees'));
  const [roads,    setRoads]    = useState(() => loadDlCache<{ id: string; name: string }>('roads'));
  const [graphicsMods, setGraphicsMods] = useState<{ id: string; name: string; variantLabel: string }[]>(() => loadDlCache<{ id: string; name: string; variantLabel: string }[]>('graphicsmods') ?? []);
  const [graphicsBusy, setGraphicsBusy] = useState<Record<string, boolean>>({});
  const [armor,    setArmor]    = useState(() => loadDlCache<{ id: string; name: string; kind: string }>('armor'));
  const [rings,    setRings]    = useState<number[]>(() => loadDlCache<number[]>('rings') ?? []);
  const [customSkins, setCustomSkins] = useState<CustomSkinApplied[]>([]);
  const [customSkinBusy, setCustomSkinBusy] = useState(false);
  const [knkSharedCode, setKnkSharedCode] = useState<string | null>(null);
  const [gunShareOpen, setGunShareOpen] = useState(false);
  const [custoDraft, setCustoDraft] = useState(() => loadDlCache<CustomizationDraftBridge>('custodraft'));
  const [minimapLayout, setMinimapLayout] = useState(() => loadDlCache<{ ratio: string; placement: string; transparent?: boolean }>('mmlayout'));
  const [zalazy,   setZalazy]   = useState(false);
  const [zalazyServer, setZalazyServer] = useState<'gta5rp' | 'majestic'>('gta5rp');
  const [bigMap,   setBigMap]   = useState(() => loadDlCache<{ id: string; name: string }>('bigmap'));
  const [fastJoin, setFastJoin] = useState(false);
  const [carLogos, setCarLogos] = useState(false);
  const refreshCarLogos = async () => {
    try {
      const st = typeof bridge.otherGetCarLogos === 'function' ? await bridge.otherGetCarLogos() : null;
      setCarLogos(!!st?.installed);
    }
    catch { setCarLogos(false); }
  };
  const [greenZone, setGreenZone] = useState(false);
  const [backpacks, setBackpacks] = useState(false);
  const [smoke,    setSmoke]    = useState(false);
  const [noTracer, setNoTracer] = useState(false);
  const [noTracerScope, setNoTracerScope] = useState('');

  const refreshSoundPack = async () => {
    try {
      const info = await bridge.getCurrentSoundPackInfo();
      const v = info ? { id: info.id, name: info.name } : null;
      setSoundPack(v); saveDlCache('sounds', v);
    } catch { setSoundPack(null); }
  };
  const refreshMinimap = async () => {
    try {
      const info = await bridge.getCurrentMinimapInfo();
      const v = info ? { kind: info.kind, id: info.id, name: info.name } : null;
      setMinimap(v); saveDlCache('minimap', v);
    } catch { setMinimap(null); }
  };
  const refreshReticle = async () => {
    try {
      const info = await bridge.getCurrentReticleInfo();
      const v = info ? { kind: info.kind, id: info.id, name: info.name } : null;
      setReticle(v); saveDlCache('reticle', v);
    } catch { setReticle(null); }
  };
  const refreshTimecycle = async () => {
    try {
      const info = typeof bridge.getCurrentTimecycleInfo === 'function' ? await bridge.getCurrentTimecycleInfo() : null;
      const v = info ? { kind: info.kind, id: info.id, name: info.name } : null;
      setTimecycle(v); saveDlCache('timecycle', v);
    } catch { setTimecycle(null); }
  };
  const refreshTrees = async () => {
    try {
      const info = typeof bridge.getCurrentTreesInfo === 'function' ? await bridge.getCurrentTreesInfo() : null;
      const v = info ? { id: info.id, name: info.name } : null;
      setTrees(v); saveDlCache('trees', v);
    } catch { setTrees(null); }
  };
  const refreshRoads = async () => {
    try {
      const info = typeof bridge.getCurrentRoadsInfo === 'function' ? await bridge.getCurrentRoadsInfo() : null;
      const v = info ? { id: info.id, name: info.name } : null;
      setRoads(v); saveDlCache('roads', v);
    } catch { setRoads(null); }
  };
  const refreshGraphics = async () => {
    try {
      const list = typeof bridge.getInstalledGraphicsMods === 'function' ? await bridge.getInstalledGraphicsMods() : [];
      const v = (list ?? []).map(m => ({ id: m.id, name: m.name, variantLabel: m.variantLabel }));
      setGraphicsMods(v); saveDlCache('graphicsmods', v);
    } catch { setGraphicsMods([]); }
  };
  const refreshArmor = async () => {
    try {
      const info = await bridge.getCurrentArmorInfo();
      const v = info ? { id: info.id, name: info.name, kind: info.kind } : null;
      setArmor(v); saveDlCache('armor', v);
    } catch { setArmor(null); }
  };
  const refreshRings = async () => {
    try { const r = await bridge.minimapGetRangeRings(); setRings(r); saveDlCache('rings', r); }
    catch { setRings([]); }
  };
  const refreshCustoDraft = async () => {
    try {
      const d = typeof bridge.getInstalledDraft === 'function' ? await bridge.getInstalledDraft() : null;
      setCustoDraft(d ?? null); saveDlCache('custodraft', d ?? null);
    } catch { setCustoDraft(null); }
    try {
      const l = typeof bridge.minimapLayoutGet === 'function' ? await bridge.minimapLayoutGet() : null;
      setMinimapLayout(l ?? null); saveDlCache('mmlayout', l ?? null);
    } catch { setMinimapLayout(null); }
  };
  const refreshZalazy = async () => {
    try { const v = await bridge.otherGetZalazy(); setZalazy(v.enabled); setZalazyServer(v.server); }
    catch { setZalazy(false); }
  };
  const refreshBigMap = async () => {
    try {
      const s = await bridge.bigMapGetState();
      const v = s.enabled && s.id ? { id: s.id, name: s.name || s.id } : null;
      setBigMap(v); saveDlCache('bigmap', v);
    } catch { setBigMap(null); }
  };
  const refreshFastJoin = async () => {
    try {
      if (typeof bridge.otherGetFastJoinStatus === 'function') {
        const s = await bridge.otherGetFastJoinStatus();
        setFastJoin(!!s?.userInstalled);
      } else {
        setFastJoin(typeof bridge.otherGetFastJoin === 'function' ? await bridge.otherGetFastJoin() : false);
      }
    } catch { setFastJoin(false); }
  };
  const refreshGreenZone = async () => {
    try { setGreenZone(typeof bridge.otherGetGreenZone === 'function' ? await bridge.otherGetGreenZone() : false); }
    catch { setGreenZone(false); }
  };
  const refreshBackpacks = async () => {
    try {
      const st = typeof bridge.otherGetBackpackStatus === 'function'
        ? await bridge.otherGetBackpackStatus() : null;
      setBackpacks(st?.state === 'removed' || st?.state === 'removed-foreign');
    } catch { setBackpacks(false); }
  };
  const refreshSmoke = async () => {
    try { setSmoke(await bridge.otherGetSmoke()); }
    catch { setSmoke(false); }
  };
  const refreshNoTracer = async () => {
    try {
      const s = await bridge.otherGetNoTracer();
      setNoTracer(s.enabled);
      setNoTracerScope(s.enabled ? formatNoTracerScope(s.categories, s.keepSnipers) : '');
    }
    catch { setNoTracer(false); setNoTracerScope(''); }
  };

  const [stateLoaded, setStateLoaded] = useState(false);

  const [improvements, setImprovements] = useState<import('@/bridge/types').Improvement[]>([]);
  const reloadImprovements = useCallback(async () => {
    try { setImprovements((await bridge.improvementsList()).filter(x => x.installed)); }
    catch {  }
  }, []);

  useEffect(() => {
    let alive = true;
    if (reduxItems.length === 0) void reduxLoad();
    Promise.all([
      loadInstallState(),
      refreshSoundPack(),
      refreshMinimap(),
      refreshReticle(),
      refreshTimecycle(),
      refreshTrees(),
      refreshRoads(),
      refreshGraphics(),
      refreshArmor(),
      refreshRings(),
      refreshCustoDraft(),
      refreshZalazy(),
      refreshBigMap(),
      refreshFastJoin(),
      refreshGreenZone(), refreshCarLogos(),
      refreshBackpacks(),
      refreshSmoke(),
      refreshNoTracer(),
      reloadImprovements(),
      bridge.customSkinApplied().then(s => { if (alive) setCustomSkins(s ?? []); }).catch(() => {}),
    ]).finally(() => {
      if (alive) setStateLoaded(true);
    });
    return () => { alive = false; };
  }, [reduxItems.length, reduxLoad, loadInstallState]);

  const activeCount = Object.values(byId)
    .filter(e => e.phase !== 'done' && e.phase !== 'error').length;
  const prevActiveCountRef = useRef(activeCount);
  useEffect(() => {
    if (prevActiveCountRef.current > 0 && activeCount === 0) {
      void loadInstallState();
      void Promise.all([refreshSoundPack(), refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor(), refreshRings(), refreshCustoDraft(), refreshZalazy(), refreshBigMap(), refreshFastJoin(), refreshGreenZone(), refreshCarLogos(), refreshSmoke(), refreshNoTracer(), refreshBackpacks(), reloadImprovements()]);
    }
    prevActiveCountRef.current = activeCount;

  }, [activeCount]);

  const overlaysReapplyTick = useKeepOverlaysStore(s => s.reapplyTick);
  useEffect(() => {
    if (overlaysReapplyTick === 0) return;
    void loadInstallState();
    void Promise.all([refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor(), refreshRings(), refreshCustoDraft(), refreshZalazy(), refreshBigMap(), refreshFastJoin(), refreshGreenZone(), refreshCarLogos(), refreshSmoke(), refreshNoTracer(), refreshBackpacks(), reloadImprovements()]);

  }, [overlaysReapplyTick]);

  const prevHistoryLenRef = useRef(history.length);
  useEffect(() => {
    if (history.length > prevHistoryLenRef.current) {
      void loadInstallState();
      void Promise.all([refreshSoundPack(), refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor(), refreshRings(), refreshCustoDraft(), refreshZalazy(), refreshBigMap(), refreshFastJoin(), refreshGreenZone(), refreshCarLogos(), refreshSmoke(), refreshNoTracer(), refreshBackpacks(), reloadImprovements()]);
    }
    prevHistoryLenRef.current = history.length;
  }, [history.length]);

  const anyActive = activeCount > 0;
  useEffect(() => {
    if (!anyActive) return;
    const iv = window.setInterval(() => {
      void loadInstallState();
      void refreshTrees();
      void refreshRoads();
      void refreshGraphics();
    }, 1500);
    return () => window.clearInterval(iv);

  }, [anyActive]);

  const [busy, setBusy] = useState<{
    redux?: boolean; gunpack?: boolean; guns?: boolean; sounds?: boolean;
    build?: boolean; all?: boolean; improvement?: boolean;
    minimap?: boolean; reticle?: boolean; timecycle?: boolean; trees?: boolean; roads?: boolean; armor?: boolean; rings?: boolean; mmlayout?: boolean; zalazy?: boolean; bigmap?: boolean; fastjoin?: boolean; greenzone?: boolean; carlogos?: boolean; smoke?: boolean; notracer?: boolean; backpacks?: boolean;
  }>({});

  const [toast, setToast] = useState<{ open: boolean; tone: ToastTone; message: string }>({
    open: false, tone: 'success', message: '',
  });

  const [removingImprovementId, setRemovingImprovementId] = useState<string | null>(null);

  const rollbackQueueRef = useRef<Promise<void>>(Promise.resolve());
  const rollbackBusyRef = useRef(false);
  const enqueueRollback = (
    key: 'redux' | 'gunpack' | 'guns' | 'sounds' | 'minimap' | 'reticle' | 'timecycle' | 'trees' | 'roads' | 'armor' | 'rings' | 'mmlayout' | 'zalazy' | 'bigmap' | 'fastjoin' | 'greenzone' | 'carlogos' | 'smoke' | 'notracer' | 'backpacks' | 'improvement' | 'build' | 'all',
    op: () => Promise<void>,
  ) => {
    if (rollbackBusyRef.current || busy[key]) return;
    rollbackBusyRef.current = true;
    setBusy(b => ({ ...b, [key]: true }));
    rollbackQueueRef.current = rollbackQueueRef.current.then(async () => {
      try { await op(); }
      catch (e) {
        console.warn(`[downloads.rollback.${key}]`, e);
        setToast({
          open: true, tone: 'error',
          message: e instanceof Error ? e.message : t('downloads.toastRollbackKeyFail', { key }),
        });
      }
      finally {
        setBusy(b => ({ ...b, [key]: false }));
        rollbackBusyRef.current = false;
      }
    });
  };
  const anyRollbackBusy = Object.values(busy).some(Boolean);

  const buildSnap   = useLastBuildInstallStore(s => s.snapshot);
  const clearBuildSnap = useLastBuildInstallStore(s => s.clear);
  const [buildExpanded, setBuildExpanded] = useState(false);

  const installedReduxItem = installedReduxId
    ? reduxItems.find(i => i.id === installedReduxId) ?? null
    : null;

  const reduxCusto = useMemo<CustoEntry[]>(() => {
    if (!custoDraft || !installedReduxId || custoDraft.reduxId !== installedReduxId) return [];
    const nameOf = (id: string) => reduxItems.find(i => i.id === id)?.name || prettyReduxName(id);
    return summarizeCustomizations(custoDraft, nameOf, (k, d) => t(k, d ?? k));
  }, [custoDraft, installedReduxId, reduxItems, t]);
  const reduxIsCustomized = reduxCusto.length > 0;
  const reduxKicker = reduxIsCustomized
    ? t('downloads.kind.reduxCustomized', 'Кастомизированный редукс')
    : t('downloads.kind.redux');

  const draftCovers = useMemo(() => {
    const d = custoDraft && installedReduxId && custoDraft.reduxId === installedReduxId ? custoDraft : null;
    return {
      reticle: !!d && d.crosshair?.kind !== 'default',
      armor:   !!d && d.armor?.kind     !== 'default',
      timecycle: !!d && d.timecycle?.kind !== 'default',
      minimap: !!d && !!(d.minimap?.importedFromReduxId || d.minimap?.libraryItemId || d.minimap?.pngOverlayPath),
      bigMap: !!d && !!d.bigMapEnabled && !!d.bigMapId,
    };
  }, [custoDraft, installedReduxId]);

  const { active, settling, settledDone, settledErr } = useMemo(() => {
    const active: InstallEntry[] = [];
    const settling: InstallEntry[] = [];
    let settledDone = 0;
    let settledErr = 0;
    for (const e of Object.values(byId)) {
      if (e.phase === 'done') { settling.push(e); settledDone++; }
      else if (e.phase === 'error') { settling.push(e); settledErr++; }
      else active.push(e);
    }
    return { active, settling, settledDone, settledErr };
  }, [byId]);

  const auth = useSessionStore(s => s.auth);
  const shareUserId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const [exportOpen, setExportOpen] = useState(false);
  const [hntExpanded, setHntExpanded] = useState(false);
  const [codesRefreshKey, setCodesRefreshKey] = useState(0);
  const onShareClick = () => {
    if (!shareUserId) {
      setToast({ open: true, tone: 'info',
        message: t('downloads.shareAuthRequired', 'Войди в аккаунт, чтобы поделиться сборкой по HNT-коду.') });
      return;
    }
    setExportOpen(true);
  };

  const [importOpen, setImportOpen] = useState(false);
  const onImportClick = () => {
    if (!shareUserId) {
      setToast({ open: true, tone: 'info',
        message: t('downloads.importAuthRequired', 'Войди в аккаунт, чтобы установить сборку по HNT-коду.') });
      return;
    }
    setImportOpen(true);
  };
  const onAfterHntImport = () => {
    void reduxLoad();
    void loadInstallState();
    void Promise.all([
      refreshSoundPack(), refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor(),
      refreshRings(), refreshCustoDraft(), refreshBigMap(),
    ]);
  };

  const layoutCustom = !!minimapLayout && (
    (!!minimapLayout.ratio && minimapLayout.ratio !== '16:9')
    || minimapLayout.placement === 'center'
    || !!minimapLayout.transparent
  );
  const layoutLabel = !minimapLayout ? '' : [
    minimapLayout.ratio && minimapLayout.ratio !== '16:9' ? minimapLayout.ratio : null,
    minimapLayout.placement === 'center' ? t('downloads.custo.positionCenter', 'по центру') : null,
    minimapLayout.transparent ? t('downloads.layoutTransparent', 'прозрачная') : null,
  ].filter(Boolean).join(' · ');

  const hasInstalled = !!installedReduxId
    || !!installedGunpack.activeGunpackId
    || installedSelectedGuns.length > 0
    || !!soundPack
    || !!minimap
    || !!reticle
    || !!timecycle
    || !!trees
    || !!roads
    || graphicsMods.length > 0
    || !!armor
    || rings.length > 0
    || layoutCustom
    || zalazy
    || !!bigMap
    || fastJoin
    || greenZone
    || carLogos
    || smoke
    || backpacks
    || noTracer
    || improvements.length > 0;

  const isCustomMinimap = !!minimap
    && (minimap.kind === 'library' || minimap.id !== installedReduxId);
  const isCustomReticle = !!reticle
    && (reticle.kind === 'library' || reticle.id !== installedReduxId);
  const isCustomTimecycle = !!timecycle
    && (timecycle.kind === 'library' || timecycle.id !== installedReduxId);
  const isCustomArmor   = !!armor;

  const buildIntact = !!buildSnap
    && buildSnap.reduxId === installedReduxId
    && buildSnap.gunpackId === installedGunpack.activeGunpackId;

  const hntSnap      = useHntInstallStore(s => s.snapshot);
  const clearHntSnap = useHntInstallStore(s => s.clear);
  const hntIntact = !!hntSnap
    && (!hntSnap.reduxId   || hntSnap.reduxId === installedReduxId)
    && (!hntSnap.gunpackId || hntSnap.gunpackId === installedGunpack.activeGunpackId)
    && (hntSnap.selgunsCount === 0 || installedSelectedGuns.length > 0)
    && hntSnap.components.every(c => {
      switch (c.key) {
        case 'armor':   return armor?.id     === c.id;
        case 'minimap': return minimap?.id   === c.id;
        case 'reticle': return reticle?.id   === c.id;
        case 'sounds':  return soundPack?.id === c.id;
        case 'bigMap':  return bigMap?.id    === c.id;
        default:        return false;
      }
    });
  const hntActive = hntIntact && !!hntSnap;
  const hntHas = (key: HntComponentSnap['key']) =>
    hntActive && hntSnap!.components.some(c => c.key === key);
  const hntCovers = {
    redux:   hntActive && !!hntSnap!.reduxId,
    gunpack: hntActive && !!hntSnap!.gunpackId,
    guns:    hntActive && hntSnap!.selgunsCount > 0,
    armor:   hntHas('armor'),
    minimap: hntHas('minimap'),
    reticle: hntHas('reticle'),
    sounds:  hntHas('sounds'),
    bigMap:  hntHas('bigMap'),
  };

  useEffect(() => {
    if (!stateLoaded) return;
    if (hntSnap && !hntIntact) clearHntSnap();
  }, [stateLoaded, hntSnap, hntIntact, clearHntSnap]);

  useEffect(() => {
    if (!stateLoaded || !buildSnap) return;
    console.debug('[downloads.buildIntact]', {
      intact: buildIntact,
      reduxMatch:    buildSnap.reduxId === installedReduxId,
      gunpackMatch:  buildSnap.gunpackId === installedGunpack.activeGunpackId,
      snap: {
        reduxId:           buildSnap.reduxId,
        gunpackId:         buildSnap.gunpackId,
        minimapId:         buildSnap.minimapId,
        reticleId:         buildSnap.reticleId,
        armorLibraryId:    buildSnap.armorLibraryId,
        soundsLibraryId:   buildSnap.soundsLibraryId,
        selgunsCount:      buildSnap.selgunsCount,
      },
      live: {
        installedReduxId,
        gunpackId:         installedGunpack.activeGunpackId,
        minimapId:         minimap?.id ?? null,
        reticleId:         reticle?.id ?? null,
        armorId:           armor?.id ?? null,
        armorKind:         armor?.kind ?? null,
        soundPackId:       soundPack?.id ?? null,
        selgunsCount:      installedSelectedGuns.length,
      },
    });

  }, [stateLoaded, buildSnap?.buildId]);

  useEffect(() => {
    if (!stateLoaded) return;
    if (buildSnap && !buildIntact) clearBuildSnap();
  }, [stateLoaded, buildSnap, buildIntact, clearBuildSnap]);

  const ensureOk = (r: { success: boolean; errorMessage: string | null }, fallback: string): void => {
    if (!r.success) throw new Error(r.errorMessage ?? fallback);
  };

  const handleRollbackRedux = () => enqueueRollback('redux', async () => {

    const preservedMinimap = minimap;
    const preservedReticle = reticle;
    const preservedReticleSpecJson = reticle?.kind === 'custom'
      ? (custoDraft?.crosshair?.customSpecJson ?? null)
      : null;
    const preservedLibraryArmor = armor && armor.kind === 'library' ? armor : null;
    const reduxIdBefore = installedReduxId;

    ensureOk(await reduxUninstall(), t('downloads.toast.reduxRollbackFail'));

    const silenceTargets = [
      'redux:vanilla',
      reduxIdBefore ? `redux:${reduxIdBefore}` : null,
    ].filter(Boolean) as string[];
    silenceTargets.forEach(silenceProgress);

    const ok: { minimap?: boolean; reticle?: boolean; armor?: boolean } = {};
    const failures: string[] = [];

    try {
      if (preservedMinimap) {
        try {
          const r = await bridge.reduxApplyMinimap(preservedMinimap.kind, preservedMinimap.id, preservedMinimap.name);
          ok.minimap = r.success;
          if (!r.success) failures.push(`${t('downloads.partName.minimap')} (${r.errorMessage ?? t('downloads.errorWord')})`);
        } catch (e) {
          failures.push(`${t('downloads.partName.minimap')} (${e instanceof Error ? e.message : String(e)})`);
        }
      }
      if (preservedReticle) {
        try {
          const r = preservedReticle.kind === 'custom'
            ? await (async () => {
                if (!preservedReticleSpecJson) return { success: false, errorMessage: t('downloads.reticleSpecLost', 'спека прицела потеряна'), workDir: null };
                const spec = JSON.parse(preservedReticleSpecJson) as import('@/bridge/types').ReticleSpec;
                return bridge.reticleApplyCustom(spec);
              })()
            : await bridge.reduxApplyReticle(preservedReticle.kind, preservedReticle.id, preservedReticle.name);
          ok.reticle = r.success;
          if (!r.success) failures.push(`${t('downloads.partName.reticle')} (${r.errorMessage ?? t('downloads.errorWord')})`);
        } catch (e) {
          failures.push(`${t('downloads.partName.reticle')} (${e instanceof Error ? e.message : String(e)})`);
        }
      }
      if (preservedLibraryArmor) {
        try {
          const r = await bridge.armorLibraryInstall(preservedLibraryArmor.id, true);
          ok.armor = r.success;
          if (!r.success) failures.push(`${t('downloads.partName.armor')} (${r.errorMessage ?? t('downloads.errorWord')})`);
        } catch (e) {
          failures.push(`${t('downloads.partName.armor')} (${e instanceof Error ? e.message : String(e)})`);
        }
      }
    } finally {
      silenceTargets.forEach(unsilenceProgress);
    }

    await Promise.all([refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor()]);
    clearBuildSnap();

    const survived: string[] = [];
    if (ok.minimap) survived.push(t('downloads.partName.minimap'));
    if (ok.reticle) survived.push(t('downloads.partName.reticle'));
    if (ok.armor)   survived.push(t('downloads.partName.armor'));
    let message: string;
    let tone: ToastTone = 'success';
    if (survived.length > 0 && failures.length === 0) {
      message = t('downloads.toastReduxSaved', { list: survived.join(', ') });
    } else if (survived.length > 0 && failures.length > 0) {
      message = t('downloads.toastReduxSavedPartial', { saved: survived.join(', '), failed: failures.join('; ') });
      tone = 'warning';
    } else if (failures.length > 0) {
      message = t('downloads.toastReduxSaveFailed', { failed: failures.join('; ') });
      tone = 'warning';
    } else {
      message = t('downloads.toast.reduxRolledBack');
    }
    setToast({ open: true, tone, message });
  });
  const handleRollbackGunpack = () => enqueueRollback('gunpack', async () => {
    const ok = await bridge.gunpackUninstall();
    if (!ok) throw new Error(t('downloads.toast.gunpackRollbackFail'));
    await loadInstallState();
    clearBuildSnap();
    setToast({ open: true, tone: 'success', message: t('downloads.toast.gunpackRolledBack') });
  });
  const handleRemoveImprovement = (id: string, name: string) => enqueueRollback('improvement', async () => {
    setRemovingImprovementId(id);
    try {
      const r = await bridge.improvementRemove(id);
      if (!r.success) throw new Error(r.errorMessage ?? t('downloads.toast.improvementRemoveFail', 'Не удалось снять улучшение'));
      await reloadImprovements();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.improvementRemoved', { name, defaultValue: '«{{name}}» снято.' }) });
    } finally {
      setRemovingImprovementId(null);
    }
  });
  const handleRollbackGuns = () => enqueueRollback('guns', async () => {
    ensureOk(await bridge.selectedGunsUninstallAll(), t('downloads.toast.selgunsRemoveFail'));
    await loadInstallState();
    clearBuildSnap();
    setToast({ open: true, tone: 'success', message: t('downloads.toast.selgunsRemoved') });
  });
  const handleRollbackSounds = () => enqueueRollback('sounds', async () => {
    ensureOk(await bridge.soundPackUninstall(), t('downloads.toast.soundsRollbackFail'));
    await refreshSoundPack();
    clearBuildSnap();
    setToast({ open: true, tone: 'success', message: t('downloads.toast.soundsRolledBack') });
  });

  const handleRollbackMinimap = () => {
    if (!installedReduxId) {
      enqueueRollback('minimap', async () => {
        ensureOk(await bridge.minimapRestoreVanilla(), t('downloads.toast.minimapRollbackFail'));
        await Promise.all([refreshMinimap(), refreshRings()]);
        setToast({ open: true, tone: 'success', message: t('downloads.toast.minimapRolledBack') });
      });
      return;
    }
    const reduxRowId = `redux:${installedReduxId}`;
    enqueueRollback('minimap', async () => {
      silenceProgress(reduxRowId);
      try {
        ensureOk(await reduxInstall(installedReduxId), t('downloads.toast.minimapRollbackFail'));
        await Promise.all([refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor()]);
        clearBuildSnap();
        setToast({ open: true, tone: 'success', message: t('downloads.toast.minimapRolledBack') });
      } finally {
        unsilenceProgress(reduxRowId);
      }
    });
  };
  const handleRollbackTimecycle = () => {
    if (!installedReduxId) {
      enqueueRollback('timecycle', async () => {
        ensureOk(await bridge.timecycleRestoreVanilla(),
          t('downloads.toast.timecycleRollbackFail', 'Не удалось вернуть таймцикл.'));
        await refreshTimecycle();
        setToast({ open: true, tone: 'success',
          message: t('downloads.toast.timecycleRolledBack', 'Таймцикл возвращён к стандартному.') });
      });
      return;
    }
    const reduxRowId = `redux:${installedReduxId}`;
    enqueueRollback('timecycle', async () => {
      silenceProgress(reduxRowId);
      try {
        ensureOk(await reduxInstall(installedReduxId),
          t('downloads.toast.timecycleRollbackFail', 'Не удалось вернуть таймцикл.'));
        await Promise.all([refreshTimecycle(), refreshMinimap(), refreshReticle(), refreshArmor()]);
        clearBuildSnap();
        setToast({ open: true, tone: 'success',
          message: t('downloads.toast.timecycleRolledBack', 'Таймцикл возвращён к стандартному.') });
      } finally {
        unsilenceProgress(reduxRowId);
      }
    });
  };
  const handleRollbackTrees = () => {
    if (!trees) return;
    enqueueRollback('trees', async () => {
      ensureOk(await bridge.treesRestore(),
        t('downloads.toast.treesRollbackFail', 'Не удалось вернуть деревья.'));
      await refreshTrees();
      setToast({ open: true, tone: 'success',
        message: t('downloads.toast.treesRolledBack', 'Деревья возвращены к стандартным.') });
    });
  };
  const handleRollbackRoads = () => {
    if (!roads) return;
    enqueueRollback('roads', async () => {
      ensureOk(await bridge.roadsRestore(),
        t('downloads.toast.roadsRollbackFail', 'Не удалось вернуть дороги.'));
      await refreshRoads();
      setToast({ open: true, tone: 'success',
        message: t('downloads.toast.roadsRolledBack', 'Дороги возвращены к стандартным.') });
    });
  };
  const handleRollbackGraphics = async (id: string, name: string) => {
    if (graphicsBusy[id]) return;
    setGraphicsBusy(b => ({ ...b, [id]: true }));
    try {
      ensureOk(await bridge.graphicsModRestore(id),
        t('downloads.toast.graphicsRollbackFail', 'Не удалось удалить мод.'));
      await refreshGraphics();
      setToast({ open: true, tone: 'success',
        message: t('downloads.toast.graphicsRolledBack', 'Мод «{{name}}» удалён.', { name }) });
    } catch (e) {
      setToast({ open: true, tone: 'error', message: (e as Error).message });
    } finally {
      setGraphicsBusy(b => ({ ...b, [id]: false }));
    }
  };
  const handleRollbackRings = () => {
    if (rings.length === 0) return;
    enqueueRollback('rings', async () => {
      ensureOk(await bridge.minimapSetRangeRings([]), t('downloads.toast.ringsRollbackFail', 'Не удалось убрать круги.'));
      await refreshRings();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.ringsRolledBack', 'Круги дальности убраны.') });
    });
  };
  const handleRollbackLayout = () => {
    enqueueRollback('mmlayout', async () => {
      ensureOk(await bridge.minimapLayoutApply('16:9', 'default', false),
        t('downloads.toast.layoutRollbackFail', 'Не удалось вернуть положение миникарты.'));
      await refreshCustoDraft();
      setToast({ open: true, tone: 'success',
        message: t('downloads.toast.layoutRolledBack', 'Положение миникарты возвращено к стандартному.') });
    });
  };
  const handleRollbackZalazy = () => {
    if (!zalazy) return;
    enqueueRollback('zalazy', async () => {
      ensureOk(await bridge.otherSetZalazy(false, zalazyServer), t('downloads.toast.zalazyRollbackFail', 'Не удалось убрать залазы.'));
      await refreshZalazy();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.zalazyRolledBack', 'Залазы убраны.') });
    });
  };
  const handleRollbackBigMap = () => {
    if (!bigMap) return;
    enqueueRollback('bigmap', async () => {
      ensureOk(await bridge.bigMapUninstall(), t('downloads.toast.bigmapRollbackFail', 'Не удалось убрать большую карту.'));
      await refreshBigMap();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.bigmapRolledBack', 'Большая карта убрана - вернулась стандартная.') });
    });
  };
  const handleRollbackFastJoin = () => {
    if (!fastJoin) return;
    enqueueRollback('fastjoin', async () => {
      ensureOk(await bridge.otherSetFastJoin(false), t('downloads.toast.fastjoinRollbackFail', 'Не удалось выключить фаст заход.'));
      await refreshFastJoin();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.fastjoinRolledBack', 'Фаст заход выключен.') });
    });
  };
  const handleRollbackCarLogos = () => {
    if (!carLogos) return;
    enqueueRollback('carlogos', async () => {
      ensureOk(await bridge.otherSetCarLogos(false), t('downloads.toast.carlogosRollbackFail', 'Не удалось убрать логотипы авто.'));
      await refreshCarLogos();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.carlogosRolledBack', 'Логотипы авто убраны.') });
    });
  };
  const handleRollbackGreenZone = () => {
    if (!greenZone) return;
    enqueueRollback('greenzone', async () => {
      ensureOk(await bridge.otherSetGreenZone(false), t('downloads.toast.greenzoneRollbackFail', 'Не удалось убрать зелёные зоны.'));
      await refreshGreenZone();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.greenzoneRolledBack', 'Зелёные зоны убраны.') });
    });
  };
  const handleRollbackBackpacks = () => {
    if (!backpacks) return;
    enqueueRollback('backpacks', async () => {
      ensureOk(await bridge.otherApplyBackpack('vanilla'), t('downloads.toast.backpacksRollbackFail', 'Не удалось вернуть рюкзаки.'));
      await refreshBackpacks();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.backpacksRolledBack', 'Рюкзаки возвращены.') });
    });
  };
  const handleRollbackSmoke = () => {
    if (!smoke) return;
    enqueueRollback('smoke', async () => {
      ensureOk(await bridge.otherSetSmoke(false), t('downloads.toast.smokeRollbackFail', 'Не удалось вернуть стандартный дым.'));
      await refreshSmoke();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.smokeRolledBack', 'Стандартный дым возвращён.') });
    });
  };
  const handleRollbackNoTracer = () => {
    if (!noTracer) return;
    enqueueRollback('notracer', async () => {
      ensureOk(await bridge.otherSetNoTracer(false), t('downloads.toast.notracerRollbackFail', 'Не удалось вернуть трейсер.'));
      await refreshNoTracer();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.notracerRolledBack', 'Трейсер возвращён.') });
    });
  };
  const shareKnkFromDraft = async () => {
    const specJson = custoDraft?.crosshair?.customSpecJson;
    if (!specJson) {
      setToast({ open: true, tone: 'error', message: t('downloads.knk.specMissing', 'Спека прицела не найдена - пересобери в конструкторе') });
      return;
    }
    try {
      const raw = JSON.parse(specJson) as Record<string, unknown>;
      const existing = (raw.code ?? raw.Code) as string | undefined;
      const cached = existing || knkSharedCode;
      if (cached) {
        void navigator.clipboard?.writeText(cached).catch(() => {});
        setToast({ open: true, tone: 'success', message: t('downloads.knk.copied', { code: cached, defaultValue: 'KNK-код скопирован: {{code}}' }) });
        return;
      }
      if (!shareUserId) {
        setToast({ open: true, tone: 'info', message: t('downloads.knk.authRequired', 'Войди в аккаунт, чтобы получить KNK-код') });
        return;
      }
      const code = await bridge.knkShare(shareUserId, raw as unknown as import('@/bridge/types').ReticleSpec);
      setKnkSharedCode(code);
      void navigator.clipboard?.writeText(code).catch(() => {});
      setToast({ open: true, tone: 'success', message: t('downloads.knk.copied', { code, defaultValue: 'KNK-код скопирован: {{code}}' }) });
    } catch (e) {
      setToast({ open: true, tone: 'error', message: t('downloads.knk.createFail', { error: e instanceof Error ? e.message : String(e), defaultValue: 'Не удалось создать KNK-код: {{error}}' }) });
    }
  };

  const handleRollbackReticle = () => {
    if (!installedReduxId) return;
    const reduxRowId = `redux:${installedReduxId}`;
    enqueueRollback('reticle', async () => {
      silenceProgress(reduxRowId);
      try {
        ensureOk(await reduxInstall(installedReduxId), t('downloads.toast.reticleRollbackFail'));
        await Promise.all([refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor()]);
        clearBuildSnap();
        setToast({ open: true, tone: 'success', message: t('downloads.toast.reticleRolledBack') });
      } finally {
        unsilenceProgress(reduxRowId);
      }
    });
  };
  const handleRollbackArmor = () => {
    if (installedReduxId && installedReduxItem?.components?.armor) {
      setArmorRollbackOpen(true);
      return;
    }
    void dispatchArmorRollback('clear');
  };

  const dispatchArmorRollback = (mode: 'revert-to-redux' | 'clear') => {
    if (mode === 'revert-to-redux' && !installedReduxId) return Promise.resolve();
    const reduxRowId = installedReduxId ? `redux:${installedReduxId}` : null;
    setArmorRollbackOpen(false);
    return new Promise<void>((resolve) => {
      enqueueRollback('armor', async () => {
        const willReinstallRedux = mode === 'revert-to-redux';
        if (willReinstallRedux && reduxRowId) silenceProgress(reduxRowId);
        try {
          if (willReinstallRedux) {
            ensureOk(await reduxInstall(installedReduxId!), t('downloads.toast.armorReturnFail'));
          } else {
            ensureOk(await bridge.reduxClearArmor(), t('downloads.toast.armorClearFail'));
          }
          await Promise.all([refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor()]);
          clearBuildSnap();
          setToast({
            open: true, tone: 'success',
            message: willReinstallRedux ? t('downloads.toast.armorToRedux') : t('downloads.toast.armorToStock'),
          });
        } finally {
          if (willReinstallRedux && reduxRowId) unsilenceProgress(reduxRowId);
          resolve();
        }
      });
    });
  };

  const [armorRollbackOpen, setArmorRollbackOpen] = useState(false);

  const handleRollbackBuild = () => {
    if (!buildSnap) return;
    enqueueRollback('build', async () => {

      if (installedReduxId) {
        ensureOk(await reduxUninstall(), t('downloads.toast.reduxRemoveFail'));
      }
      if (installedGunpack.activeGunpackId) {
        const ok = await bridge.gunpackUninstall();
        if (!ok) throw new Error(t('downloads.toast.gunpackRemoveFail'));
      }
      if (installedSelectedGuns.length > 0) {
        ensureOk(await bridge.selectedGunsUninstallAll(), t('downloads.toast.crossPackRemoveFail'));
      }
      if (soundPack) {
        ensureOk(await bridge.soundPackUninstall(), t('downloads.toast.soundsReturnFail'));
      }
      await loadInstallState();
      await Promise.all([refreshSoundPack(), refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor()]);
      clearBuildSnap();
      setToast({ open: true, tone: 'success', message: t('downloads.toast.buildRolledBack') });
    });
  };

  const handleRollbackAll = () => enqueueRollback('all', async () => {
    const restored = await bridge.backupRestoreClean();
    if (restored) {
      useReduxStore.getState().clearInstalled();
      useGtaSettingsStore.getState().setInstalledPreset(null);
      useGunpackStore.getState().setInstalledGunpack({
        activeGunpackId: null, activeGunpackName: null, weaponsRpfSha256: null, installedAt: null,
      });
      useGunpackStore.getState().setInstalledSelectedGuns([]);
    } else {
      if (armor) {
        ensureOk(await bridge.reduxClearArmor(), t('downloads.toast.armorClearFail'));
      }
      if (installedReduxId) {
        ensureOk(await reduxUninstall(), t('downloads.toast.reduxRemoveFail'));
      }
      if (installedGunpack.activeGunpackId) {
        const ok = await bridge.gunpackUninstall();
        if (!ok) throw new Error(t('downloads.toast.gunpackRemoveFail'));
      }
      if (installedSelectedGuns.length > 0) {
        ensureOk(await bridge.selectedGunsUninstallAll(), t('downloads.toast.crossPackRemoveFail'));
      }
      if (soundPack) {
        ensureOk(await bridge.soundPackUninstall(), t('downloads.toast.soundsReturnFail'));
      }
    }
    const extraFails: string[] = [];
    if (trees) {
      try { ensureOk(await bridge.treesRestore(), 'trees'); }
      catch { extraFails.push(t('downloads.kind.trees', 'Деревья')); }
    }
    if (roads) {
      try { ensureOk(await bridge.roadsRestore(), 'roads'); }
      catch { extraFails.push(t('downloads.kind.roads', 'Дороги')); }
    }
    for (const gm of graphicsMods) {
      try { ensureOk(await bridge.graphicsModRestore(gm.id), 'graphics'); }
      catch { extraFails.push(gm.name); }
    }
    await loadInstallState();
    await Promise.all([
      refreshSoundPack(), refreshMinimap(), refreshReticle(), refreshTimecycle(), refreshTrees(), refreshRoads(), refreshGraphics(), refreshArmor(),
      refreshRings(), refreshCustoDraft(), refreshZalazy(), refreshBigMap(),
      refreshFastJoin(), refreshGreenZone(), refreshCarLogos(), refreshSmoke(), refreshNoTracer(), refreshBackpacks(),
    ]);
    clearBuildSnap();
    if (extraFails.length > 0) {
      setToast({ open: true, tone: 'error', message: t('downloads.toast.allPartial',
        'Откатили почти всё, но не удалось: {{items}}. Попробуй ещё раз.', { items: extraFails.join(', ') }) });
    } else {
      setToast({ open: true, tone: 'success', message: restored
        ? t('downloads.toast.allWiped', 'Всё стёрто - GTA в чистом vanilla-состоянии.')
        : t('downloads.toast.allRolledBack', 'Всё откатили - GTA в исходном состоянии.') });
    }
  });

  const queueAvg = active.length === 0
    ? 0
    : Math.floor(active.reduce((s, e) => s + e.percent, 0) / active.length);

  const ru2Queue = useRu2QueueStore();

  return (
    <div className="h-full overflow-y-auto">
      <div className="max-w-6xl mx-auto px-8 pt-7 pb-10 flex flex-col gap-6">
        {}
        <header className="flex items-end justify-between gap-4 flex-wrap shrink-0">
          <div className="min-w-0">
            <h1 className="font-display font-bold text-3xl uppercase tracking-wide
                           text-text-primary leading-tight">
              {t('downloads.title', 'Загрузки')}
            </h1>
            <p className="text-sm text-text-secondary mt-1.5 max-w-2xl">
              {t('downloads.subtitle', 'Идущие установки и история того, что уже накатили.')}
            </p>
          </div>

          {}
          <div className="flex items-center gap-2">
            <StatChip
              icon={Zap}
              label={t('downloads.statActive')}
              value={active.length}
              tone="active"
              progress={queueAvg}
            />
            <StatChip
              icon={CheckCircle2}
              label={t('downloads.statDone')}
              value={settledDone + history.filter(h => h.phase === 'done').length}
              tone="done"
            />
            {(settledErr > 0 || history.some(h => h.phase === 'error')) && (
              <StatChip
                icon={AlertCircle}
                label={t('downloads.statErrors')}
                value={settledErr + history.filter(h => h.phase === 'error').length}
                tone="error"
              />
            )}
          </div>
        </header>

        {ru2Queue.active && (
          <div className="flex items-center gap-3 rounded-xl border border-amber-400/30 bg-amber-400/10 px-4 py-3 text-sm text-amber-100 shrink-0">
            <Zap size={16} className="shrink-0 animate-pulse" />
            <span>
              {t('downloads.queue.position', {
                n: ru2Queue.position,
                min: Math.max(1, Math.round(ru2Queue.etaSec / 60)),
                defaultValue: 'Очередь на RU-сервер: {{n}}-й · ~{{min}} мин. На EU очереди нет.',
              })}
            </span>
          </div>
        )}

        {}
        <span
          aria-hidden
          className="block h-px shrink-0 -my-1
                     bg-gradient-to-r from-transparent via-white/12 to-transparent"
        />

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-x-6 gap-y-6 items-start">
          <div className="flex flex-col gap-6 min-w-0">
        {}
        <Section
          icon={Download}
          title={t('downloads.activeTitle', 'В процессе')}
          empty={t('downloads.activeEmpty', 'Сейчас ничего не качается.')}
          isEmpty={active.length === 0}
          emptyIcon={Hourglass}
          accent={active.length > 0}
        >
          <AnimatePresence initial={false}>
            {active.map(entry => (
              <motion.div
                key={entry.reduxId}
                layout
                initial={{ opacity: 0, y: -6, scale: 0.98 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: 14, scale: 0.96 }}
                transition={{ duration: 0.32, ease: EASE_DEPTH }}
              >
                <ActiveRow entry={entry} onDismiss={() => dismissEntry(entry)} />
              </motion.div>
            ))}
          </AnimatePresence>
        </Section>

        {}
        {settling.length > 0 && (
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.42, ease: EASE_DEPTH }}
          >
          <Section
            icon={Sparkles}
            title={t('downloads.settlingTitle', 'Только что завершилось')}
          >
            <AnimatePresence initial={false}>
              {settling.map(entry => (
                <motion.div
                  key={entry.reduxId}
                  layout
                  initial={{ opacity: 0, y: -8 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: 24, scale: 0.96 }}
                  transition={{ duration: 0.42, ease: EASE_DEPTH }}
                >
                  <TerminalRow
                    name={entry.name}
                    phase={entry.phase as 'done' | 'error'}
                    errorMessage={entry.errorMessage}
                    fresh
                  />
                </motion.div>
              ))}
            </AnimatePresence>
          </Section>
          </motion.div>
        )}

        {shareUserId && (
          <HntMyCodesPanel userId={shareUserId} refreshKey={codesRefreshKey} />
        )}

          </div>

          <div className="flex flex-col gap-6 min-w-0">
        <Section
            icon={BadgeCheck}
            title={t('downloads.installedTitle', 'Сейчас установлено')}
            isEmpty={!hasInstalled && customSkins.length === 0}
            empty={t('downloads.installedEmpty')}
            emptyIcon={Package}
            rightAction={
                <div className="flex items-center gap-1.5">
                  <button
                    type="button"
                    onClick={onImportClick}
                    disabled={anyRollbackBusy}
                    className="inline-flex items-center gap-1 h-7 px-2.5 rounded-md border shrink-0 whitespace-nowrap
                               bg-white/[0.04] border-white/[0.06] text-text-muted
                               text-[10px] font-bold uppercase tracking-[0.18em]
                               hover:bg-accent/15 hover:text-accent hover:border-accent/40
                               disabled:opacity-50 disabled:cursor-not-allowed
                               transition-colors"
                    style={{ outline: 'none' }}
                    title={t('downloads.importHntHint', 'Установить сборку по HNT-коду')}
                    aria-label={t('downloads.importHnt', 'Ввести код')}
                  >
                    <Download size={11} />
                    <span>{t('downloads.importHnt', 'Ввести код')}</span>
                  </button>
                  {hasInstalled && (<>
                  <button
                    type="button"
                    onClick={onShareClick}
                    disabled={anyRollbackBusy}
                    className="inline-flex items-center gap-1 h-7 px-2.5 rounded-md border shrink-0 whitespace-nowrap
                               bg-white/[0.04] border-white/[0.06] text-text-muted
                               text-[10px] font-bold uppercase tracking-[0.18em]
                               hover:bg-accent/15 hover:text-accent hover:border-accent/40
                               disabled:opacity-50 disabled:cursor-not-allowed
                               transition-colors"
                    style={{ outline: 'none' }}
                    title={t('downloads.shareHntHint', 'Поделиться установленной сборкой по HNT-коду')}
                    aria-label={t('downloads.shareHnt', 'Поделиться')}
                  >
                    <Share2 size={11} />
                    <span>{t('downloads.shareHnt', 'Поделиться')}</span>
                  </button>
                  <button
                    type="button"
                    onClick={handleRollbackAll}
                    disabled={anyRollbackBusy}
                    className="inline-flex items-center gap-1 h-7 px-2.5 rounded-md border shrink-0 whitespace-nowrap
                               bg-white/[0.04] border-white/[0.06] text-text-muted
                               text-[10px] font-bold uppercase tracking-[0.18em]
                               hover:bg-status-error/15 hover:text-status-error hover:border-status-error/40
                               disabled:opacity-50 disabled:cursor-not-allowed
                               transition-colors"
                    style={{ outline: 'none' }}
                    title={t('downloads.rollbackAllHint', 'Откатить все моды и вернуть GTA в исходное состояние')}
                    aria-label={t('downloads.rollbackAll', 'Откатить всё')}
                  >
                    <RotateCcw size={11} className={busy.all ? 'animate-spin' : ''} />
                    <span>{t('downloads.rollbackAll', 'Откатить всё')}</span>
                  </button>
                  </>)}
                </div>
            }
          >
            <div className="flex flex-col gap-2">
              {customSkins.map((skin) => (
                <CustomSkinRow
                  key={skin.internalName}
                  skin={skin}
                  busy={customSkinBusy || anyRollbackBusy}
                  onRemove={async () => {
                    setCustomSkinBusy(true);
                    try {
                      const r = await bridge.customSkinRemove(skin.internalName);
                      if (r.success) {
                        setCustomSkins(prev => prev.filter(c => c.internalName !== skin.internalName));
                      }
                      setToast({ open: true, tone: r.success ? 'success' : 'error',
                        message: r.success ? (r.errorMessage || t('downloads.skin.removed', 'Скин убран')) : (r.errorMessage || t('downloads.skin.removeFail', 'Не удалось убрать скин')) });
                    } catch (e) {
                      setToast({ open: true, tone: 'error', message: e instanceof Error ? e.message : t('downloads.statusError', 'Ошибка') });
                    } finally { setCustomSkinBusy(false); }
                  }}
                />
              ))}
              {hntActive && hntSnap && (
                <HntInstalledRow
                  code={hntSnap.code}
                  installedAt={hntSnap.installedAt}
                  reduxName={hntSnap.reduxId
                    ? (hntSnap.reduxName ?? installedReduxItem?.name ?? prettyReduxName(hntSnap.reduxId))
                    : null}
                  reduxKicker={reduxKicker}
                  reduxCustomizations={reduxIsCustomized ? reduxCusto : undefined}
                  gunpackName={hntSnap.gunpackId
                    ? (hntSnap.gunpackName ?? installedGunpack.activeGunpackName ?? hntSnap.gunpackId)
                    : null}
                  selgunsCount={hntSnap.selgunsCount}
                  components={hntSnap.components}
                  expanded={hntExpanded}
                  onToggleExpand={() => setHntExpanded(e => !e)}
                  busy={!!busy.all}
                  globalBusy={anyRollbackBusy}
                  onRollbackAll={handleRollbackAll}
                  childRollback={{
                    redux:   hntSnap.reduxId  ? { busy: !!busy.redux,   onRollback: handleRollbackRedux }   : null,
                    gunpack: hntSnap.gunpackId ? { busy: !!busy.gunpack, onRollback: handleRollbackGunpack } : null,
                    guns:    hntSnap.selgunsCount > 0 ? { busy: !!busy.guns, onRollback: handleRollbackGuns } : null,
                    armor:   { busy: !!busy.armor,   onRollback: handleRollbackArmor },
                    minimap: { busy: !!busy.minimap, onRollback: handleRollbackMinimap },
                    reticle: { busy: !!busy.reticle, onRollback: handleRollbackReticle },
                    sounds:  { busy: !!busy.sounds,  onRollback: handleRollbackSounds },
                    bigMap:  { busy: !!busy.bigmap,  onRollback: handleRollbackBigMap },
                  }}
                />
              )}
              {}
              {!hntActive && buildIntact && buildSnap ? (
                <BuildInstalledRow
                  buildName={buildSnap.buildName}
                  reduxName={buildSnap.reduxName ?? installedReduxItem?.name ?? prettyReduxName(buildSnap.reduxId)}
                  reduxKicker={reduxKicker}
                  reduxCustomizations={reduxIsCustomized ? reduxCusto : undefined}
                  gunpackName={buildSnap.gunpackName ?? installedGunpack.activeGunpackName ?? buildSnap.gunpackId}
                  selgunsCount={buildSnap.selgunsCount}
                  soundsName={buildSnap.soundsLibraryName ?? soundPack?.name ?? null}
                  minimapName={isCustomMinimap && !draftCovers.minimap ? (buildSnap.minimapName ?? minimap?.name ?? null) : null}
                  reticleName={isCustomReticle && !draftCovers.reticle ? (buildSnap.reticleName ?? reticle?.name ?? null) : null}
                  armorName={isCustomArmor && !draftCovers.armor ? (buildSnap.armorName   ?? armor?.name   ?? null) : null}
                  arenaName={buildSnap.arenaName ?? null}
                  installedAt={buildSnap.installedAt}
                  expanded={buildExpanded}
                  onToggleExpand={() => setBuildExpanded(e => !e)}
                  busy={!!busy.build}
                  globalBusy={anyRollbackBusy}
                  onRollbackBuild={handleRollbackBuild}
                  childRollback={{
                    redux:   { busy: !!busy.redux,   onRollback: handleRollbackRedux },
                    gunpack: { busy: !!busy.gunpack, onRollback: handleRollbackGunpack },
                    guns:    installedSelectedGuns.length > 0
                      ? { busy: !!busy.guns,    onRollback: handleRollbackGuns }
                      : null,
                    sounds:  soundPack
                      ? { busy: !!busy.sounds,  onRollback: handleRollbackSounds }
                      : null,
                    minimap: isCustomMinimap && !draftCovers.minimap
                      ? { busy: !!busy.minimap, onRollback: handleRollbackMinimap }
                      : null,
                    reticle: isCustomReticle && !draftCovers.reticle
                      ? { busy: !!busy.reticle, onRollback: handleRollbackReticle }
                      : null,
                    armor:   isCustomArmor && !draftCovers.armor
                      ? { busy: !!busy.armor,   onRollback: handleRollbackArmor }
                      : null,
                  }}
                />
              ) : (

                <>
                  {installedReduxId && !hntCovers.redux && (
                    <InstalledRow
                      icon={Layers}
                      kicker={reduxKicker}
                      kickerAccent={reduxIsCustomized}
                      name={installedReduxItem?.name ?? prettyReduxName(installedReduxId)}
                      customizations={reduxIsCustomized ? reduxCusto : undefined}
                      busy={!!busy.redux}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackRedux}
                    />
                  )}
                  {}
                  {isCustomMinimap && minimap && !draftCovers.minimap && !hntCovers.minimap && (
                    <InstalledRow
                      icon={MapIcon}
                      kicker={t('downloads.kind.minimap')}
                      name={minimap.name || minimap.id}
                      busy={!!busy.minimap}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackMinimap}
                    />
                  )}
                  {reticle?.kind === 'custom' ? (
                    <InstalledRow
                      icon={Target}
                      kicker={t('downloads.customReticleKicker', 'Свой прицел · Конструктор')}
                      kickerAccent
                      name={t('downloads.customReticleName', 'Свой прицел')}
                      detail={t('downloads.customReticleDetail', 'Нажми «Код», чтобы получить KNK-код и поделиться')}
                      busy={!!busy.reticle}
                      globalBusy={anyRollbackBusy}
                      onRollback={installedReduxId ? handleRollbackReticle : undefined}
                      hint={installedReduxId ? undefined : t('downloads.customReticleHint', 'меняется в конструкторе')}
                      onCopy={() => void shareKnkFromDraft()}
                    />
                  ) : (isCustomReticle && reticle && !draftCovers.reticle && !hntCovers.reticle && (
                    <InstalledRow
                      icon={Target}
                      kicker={t('downloads.kind.reticle')}
                      name={reticle.name || reticle.id}
                      busy={!!busy.reticle}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackReticle}
                    />
                  ))}
                  {isCustomTimecycle && timecycle && !draftCovers.timecycle && (
                    <InstalledRow
                      icon={SunMedium}
                      kicker={t('downloads.kind.timecycle', 'Таймцикл')}
                      name={timecycle.name || timecycle.id}
                      busy={!!busy.timecycle}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackTimecycle}
                    />
                  )}
                  {trees && (
                    <InstalledRow
                      icon={TreesIcon}
                      kicker={t('downloads.kind.trees', 'Деревья')}
                      name={trees.name || trees.id}
                      busy={!!busy.trees}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackTrees}
                    />
                  )}
                  {roads && (
                    <InstalledRow
                      icon={RoadsIcon}
                      kicker={t('downloads.kind.roads', 'Дороги')}
                      name={roads.name || roads.id}
                      busy={!!busy.roads}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackRoads}
                    />
                  )}
                  {graphicsMods.map(gm => (
                    <InstalledRow
                      key={gm.id}
                      icon={ShapesIcon}
                      kicker={t('downloads.kind.graphics', 'Разное')}
                      name={gm.variantLabel ? `${gm.name} · ${gm.variantLabel}` : gm.name}
                      busy={!!graphicsBusy[gm.id]}
                      globalBusy={anyRollbackBusy}
                      onRollback={() => void handleRollbackGraphics(gm.id, gm.name)}
                    />
                  ))}
                  {isCustomArmor && armor && !draftCovers.armor && !hntCovers.armor && (
                    <InstalledRow
                      icon={ShieldIcon}
                      kicker={t('downloads.kind.armor')}
                      name={armor.name || armor.id}
                      busy={!!busy.armor}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackArmor}
                    />
                  )}
                  {installedGunpack.activeGunpackId && !hntCovers.gunpack && (
                    <InstalledRow
                      icon={Crosshair}
                      kicker={t('downloads.kind.gunpack')}
                      name={installedGunpack.activeGunpackName ?? installedGunpack.activeGunpackId}
                      busy={!!busy.gunpack}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackGunpack}
                    />
                  )}
                  {improvements.map(imp => (
                    <InstalledRow
                      key={imp.id}
                      icon={improvementIcon(imp.category)}
                      kicker={t(improvementKicker(imp.category).key, improvementKicker(imp.category).def)}
                      name={imp.name}
                      busy={removingImprovementId === imp.id}
                      globalBusy={anyRollbackBusy}
                      onRollback={() => void handleRemoveImprovement(imp.id, imp.name)}
                    />
                  ))}
                  {installedSelectedGuns.length > 0 && !hntCovers.guns && (
                    <InstalledRow
                      icon={Box}
                      kicker={t('downloads.kind.selguns')}
                      name={t('downloads.gunsFromOtherPacks', { count: installedSelectedGuns.length })}
                      busy={!!busy.guns}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackGuns}
                      onCopy={() => {
                        if (!shareUserId) {
                          setToast({ open: true, tone: 'info', message: t('downloads.gunsShareAuthRequired', 'Войди в аккаунт, чтобы поделиться ганами') });
                          return;
                        }
                        setGunShareOpen(true);
                      }}
                      expandTitle={t('downloads.selgunsExpand', 'Показать выбранные пушки')}
                      expandItems={installedSelectedGuns.map(g => ({
                        key:   g.gunId,
                        icon:  Crosshair,
                        label: g.gunpackName,
                        value: g.displayName,
                      }))}
                    />
                  )}
                  {soundPack && !hntCovers.sounds && (
                    <InstalledRow
                      icon={Volume2}
                      kicker={t('downloads.kind.sounds')}
                      name={soundPack.name}
                      busy={!!busy.sounds}
                      globalBusy={anyRollbackBusy}
                      onRollback={handleRollbackSounds}
                    />
                  )}
                </>
              )}
              {rings.length > 0 && (
                <InstalledRow
                  icon={CircleDashed}
                  kicker={t('downloads.kind.rings', 'Круги дальности')}
                  name={`${rings.join(' / ')} ${t('downloads.ringsMetres', 'м')}`}
                  busy={!!busy.rings}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackRings}
                />
              )}
              {layoutCustom && (
                <InstalledRow
                  icon={MapIcon}
                  kicker={t('downloads.kind.minimap')}
                  name={`${t('downloads.layoutName', 'Положение')}: ${layoutLabel}`}
                  busy={!!busy.mmlayout}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackLayout}
                />
              )}
              {bigMap && !hntCovers.bigMap && !draftCovers.bigMap && (
                <InstalledRow
                  icon={MapIcon}
                  kicker={t('downloads.kind.bigmap', 'Большая карта')}
                  name={bigMap.name}
                  busy={!!busy.bigmap}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackBigMap}
                />
              )}
              {zalazy && (
                <InstalledRow
                  icon={Footprints}
                  kicker={t('downloads.kind.zalazy', 'Другое')}
                  name={zalazyServer === 'majestic'
                    ? t('downloads.zalazyNameMajestic', 'Залазы + запретки + мапинг (Majestic)')
                    : t('downloads.zalazyName5rp', 'Залазы ВЗП + запретки (GTA5RP)')}
                  busy={!!busy.zalazy}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackZalazy}
                />
              )}
              {fastJoin && (
                <InstalledRow
                  icon={FastForward}
                  kicker={t('downloads.kind.fastjoin', 'Другое')}
                  name={t('downloads.fastjoinName', 'Фаст заход')}
                  busy={!!busy.fastjoin}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackFastJoin}
                />
              )}
              {carLogos && (
                <InstalledRow
                  icon={Car}
                  kicker={t('downloads.kind.carlogos', 'Другое')}
                  name={t('downloads.carlogosName', 'Логотипы авто')}
                  busy={!!busy.carlogos}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackCarLogos}
                />
              )}
              {greenZone && (
                <InstalledRow
                  icon={Trees}
                  kicker={t('downloads.kind.greenzone', 'Другое')}
                  name={t('downloads.greenzoneName', 'Зелёные зоны')}
                  busy={!!busy.greenzone}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackGreenZone}
                />
              )}
              {backpacks && (
                <InstalledRow
                  icon={Backpack}
                  kicker={t('downloads.kind.backpacks', 'Другое')}
                  name={t('downloads.backpacksName', 'Удаление рюкзаков')}
                  busy={!!busy.backpacks}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackBackpacks}
                />
              )}
              {smoke && (
                <InstalledRow
                  icon={Wind}
                  kicker={t('downloads.kind.smoke', 'Другое')}
                  name={t('downloads.smokeName', 'Вернуть дым')}
                  busy={!!busy.smoke}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackSmoke}
                />
              )}
              {noTracer && (
                <InstalledRow
                  icon={ZapOff}
                  kicker={t('downloads.kind.notracer', 'Другое')}
                  name={t('downloads.notracerName', 'Убрать трайсер')}
                  detail={noTracerScope || undefined}
                  busy={!!busy.notracer}
                  globalBusy={anyRollbackBusy}
                  onRollback={handleRollbackNoTracer}
                />
              )}
            </div>
          </Section>
          </div>
        </div>

        {}
        <Section
          icon={History}
          title={t('downloads.historyTitle', 'История')}
          empty={t('downloads.historyEmpty', 'Тут будет список того, что ты уже устанавливал.')}
          isEmpty={history.length === 0}
          emptyIcon={Package}
          rightAction={
            history.length > 0 ? (
              <button
                type="button"
                onClick={clearHistory}
                className="inline-flex items-center gap-1 h-7 px-2.5 rounded-md border
                           bg-white/[0.04] border-white/[0.06] text-text-muted
                           text-[10px] font-bold uppercase tracking-[0.18em]
                           hover:bg-status-error/15 hover:text-status-error hover:border-status-error/40
                           transition-colors"
                style={{ outline: 'none' }}
                title={t('downloads.clearHistory')}
                aria-label={t('downloads.clearHistory')}
              >
                <Trash2 size={11} />
                <span>{t('common.clear', 'Очистить')}</span>
              </button>
            ) : null
          }
        >
          <AnimatePresence initial={false}>
            {history.map(h => (
              <motion.div
                key={h.uid}
                layout
                initial={{ opacity: 0, y: -4 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.28, ease: EASE_DEPTH }}
              >
                <TerminalRow
                  name={h.name}
                  phase={h.phase}
                  errorMessage={h.errorMessage}
                  finishedAt={h.finishedAt}
                />
              </motion.div>
            ))}
          </AnimatePresence>
        </Section>

      </div>

      {}
      <Toast
        open={toast.open}
        tone={toast.tone}
        message={toast.message}
        autoCloseMs={toast.tone === 'error' ? 6000 : 3500}
        onClose={() => setToast(t => ({ ...t, open: false }))}
      />

      <ArmorRollbackChoiceModal
        open={armorRollbackOpen}
        reduxName={installedReduxItem?.name ?? prettyReduxName(installedReduxId)}
        currentArmorName={armor?.name ?? ''}
        onRevertToRedux={() => void dispatchArmorRollback('revert-to-redux')}
        onClearArmor={() => void dispatchArmorRollback('clear')}
        onCancel={() => setArmorRollbackOpen(false)}
      />

      {exportOpen && shareUserId && (
        <HntExportModal
          userId={shareUserId}
          autoAll
          onClose={() => { setExportOpen(false); setCodesRefreshKey(k => k + 1); }}
        />
      )}

      {gunShareOpen && shareUserId && (
        <GunShareModal
          userId={shareUserId}
          onClose={() => { setGunShareOpen(false); setCodesRefreshKey(k => k + 1); }}
        />
      )}

      {importOpen && (
        <HntImportModal
          onAppliedRefresh={onAfterHntImport}
          onClose={() => setImportOpen(false)}
        />
      )}
    </div>
  );
}

function CustomSkinRow({ skin, busy, onRemove }: {
  skin: CustomSkinApplied;
  busy: boolean;
  onRemove: () => void | Promise<void>;
}) {
  const { t } = useTranslation();
  return (
    <div className="flex items-center gap-3 rounded-xl border border-white/[0.07] bg-white/[0.03] p-3">
      <div className="w-9 h-9 rounded-lg flex items-center justify-center bg-accent-soft border border-accent-40 text-accent shrink-0">
        <Crosshair size={17} />
      </div>
      <div className="min-w-0 flex-1">
        <div className="text-[10px] font-bold uppercase tracking-[0.18em] text-accent">{t('downloads.skin.kicker', 'Кастомный скин оружия')}</div>
        <div className="text-sm font-semibold text-text-primary truncate">{skin.displayName || skin.internalName}</div>
      </div>
      <button
        type="button"
        onClick={() => onRemove()}
        disabled={busy}
        className="inline-flex items-center gap-1 h-7 px-2.5 rounded-md border shrink-0 whitespace-nowrap
                   bg-white/[0.04] border-white/[0.06] text-text-muted text-[10px] font-bold uppercase tracking-[0.18em]
                   hover:bg-status-error/15 hover:text-status-error hover:border-status-error/40
                   disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        style={{ outline: 'none' }}
        title={t('downloads.skin.removeTitle', 'Убрать скин и вернуть стандартное оружие')}
      >
        <RotateCcw size={11} className={busy ? 'animate-spin' : ''} />
        <span>{t('downloads.skin.removeButton', 'Убрать')}</span>
      </button>
    </div>
  );
}

function improvementKicker(category: string): { key: string; def: string } {
  if (category === 'vegetation')   return { key: 'downloads.kind.trees',       def: 'Деревья' };
  if (category === 'misc')         return { key: 'downloads.kind.graphics',    def: 'Разное' };
  if (category === 'gas_stations') return { key: 'downloads.kind.gasStations', def: 'Заправки' };
  return { key: 'downloads.kind.improvement', def: 'Улучшение' };
}

function improvementIcon(category: string): LucideIcon {
  if (category === 'vegetation') return TreesIcon;
  if (category === 'misc')       return ShapesIcon;
  return Fuel;
}

function InstalledRow({
  icon: Icon, kicker, kickerAccent, name, busy, globalBusy, onRollback, hint, detail, customizations,
  expandItems, expandTitle, onCopy,
}: {
  icon: LucideIcon;
  kicker: string;
  kickerAccent?: boolean;
  name: string;
  detail?: string;
  globalBusy?: boolean;
  busy?:       boolean;
  onRollback?: () => void;
  hint?: string;
  customizations?: CustoEntry[];
  expandItems?: CustoEntry[];
  expandTitle?: string;
  onCopy?: () => void;
}) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const expandRows = (customizations && customizations.length > 0)
    ? customizations
    : (expandItems && expandItems.length > 0) ? expandItems : null;
  const expandable = !!expandRows;
  return (
    <div
      className="rounded-xl bg-white/[0.02] border border-white/[0.06]
                 hover:bg-white/[0.04] hover:border-white/[0.10]
                 transition-colors"
    >
      <div className="flex items-center gap-3 px-4 py-3">
        <span className="shrink-0 w-9 h-9 rounded-lg flex items-center justify-center
                         bg-white/[0.04] border border-white/[0.06] text-text-secondary">
          <Icon size={15} strokeWidth={2} />
        </span>
        <div className="flex-1 min-w-0 flex flex-col">
          <span className={'text-[10px] font-bold uppercase tracking-[0.22em] '
                           + (kickerAccent ? 'text-accent' : 'text-text-muted')}>
            {kicker}
          </span>
          <span className="text-[13px] font-semibold text-text-primary truncate uppercase">
            {name}
          </span>
          {detail && (
            <span className="text-[11px] text-text-muted truncate">{detail}</span>
          )}
        </div>
        {onCopy && (
          <button
            type="button"
            onClick={onCopy}
            title={t('downloads.shareCodeTitle', 'Поделиться кодом')}
            style={{ outline: 'none' }}
            className="shrink-0 inline-flex items-center gap-1.5 h-9 px-3 rounded-lg
                       bg-white/[0.04] border border-white/[0.10] text-text-secondary
                       text-[11px] uppercase tracking-[0.16em] font-bold
                       hover:bg-white/[0.08] hover:border-white/[0.18] hover:text-text-primary
                       transition-colors duration-200"
          >
            <Copy size={12} />
            <span>{t('downloads.codeButton', 'Код')}</span>
          </button>
        )}
        {onRollback ? (
          <button
            type="button"
            onClick={onRollback}
            disabled={busy || globalBusy}
            title={t('downloads.rollbackToStock')}
            style={{ outline: 'none' }}
            className="shrink-0 inline-flex items-center gap-1.5 h-9 px-3 rounded-lg
                       bg-white/[0.04] border border-white/[0.10] text-text-secondary
                       text-[11px] uppercase tracking-[0.16em] font-bold
                       hover:bg-status-error/15 hover:border-status-error/40 hover:text-status-error
                       disabled:opacity-60 disabled:cursor-wait
                       transition-colors duration-200"
          >
            {busy ? <Loader2 size={12} className="animate-spin" /> : <RotateCcw size={12} />}
            <span>{busy ? t('downloads.rollingBack') : t('downloads.rollback')}</span>
          </button>
        ) : hint ? (
          <span className="shrink-0 text-[10px] uppercase tracking-[0.16em] font-semibold text-text-muted/80">
            {hint}
          </span>
        ) : null}
        {expandable && (
          <button
            type="button"
            onClick={() => setExpanded(e => !e)}
            aria-label={expanded ? t('downloads.custo.collapse', 'Свернуть') : (expandTitle ?? t('downloads.custo.expand', 'Показать, что кастомизировано'))}
            title={expanded ? t('downloads.custo.collapse', 'Свернуть') : (expandTitle ?? t('downloads.custo.expand', 'Показать, что кастомизировано'))}
            style={{ outline: 'none' }}
            className="shrink-0 inline-flex items-center justify-center w-9 h-9 rounded-lg
                       bg-white/[0.04] border border-white/[0.10] text-text-secondary
                       hover:bg-white/[0.08] hover:border-white/[0.18] hover:text-text-primary
                       transition-colors duration-200"
          >
            <motion.span
              animate={{ rotate: expanded ? 180 : 0 }}
              transition={{ duration: 0.22, ease: EASE_DEPTH }}
              style={{ display: 'inline-flex' }}
            >
              <ChevronDown size={14} />
            </motion.span>
          </button>
        )}
      </div>

      {expandable && (
        <AnimatePresence initial={false}>
          {expanded && (
            <motion.div
              key="custo"
              initial={{ opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: 'auto' }}
              exit   ={{ opacity: 0, height: 0 }}
              transition={{ duration: 0.28, ease: EASE_DEPTH }}
              style={{ overflow: 'hidden' }}
            >
              <div className="px-3 pb-3 pt-0.5 flex flex-col gap-1.5">
                {expandRows!.map(c => {
                  const CIcon = c.icon;
                  return (
                    <div
                      key={c.key}
                      className="flex items-center gap-2.5 rounded-lg px-3 py-2
                                 bg-white/[0.02] border border-white/[0.05]"
                    >
                      <span className="shrink-0 w-6 h-6 rounded-md flex items-center justify-center
                                       bg-white/[0.04] border border-white/[0.06] text-text-muted">
                        <CIcon size={12} strokeWidth={2} />
                      </span>
                      <span className="shrink-0 text-[10px] font-bold uppercase tracking-[0.18em] text-text-muted">
                        {c.label}
                      </span>
                      <span className="ml-auto min-w-0 text-[11px] font-semibold text-text-primary uppercase truncate">
                        {c.value}
                      </span>
                    </div>
                  );
                })}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      )}
    </div>
  );
}

function BuildInstalledRow({
  buildName, reduxName, reduxKicker, reduxCustomizations, gunpackName, selgunsCount, soundsName,
  minimapName, reticleName, armorName, arenaName,
  installedAt,
  expanded, onToggleExpand, busy, globalBusy, onRollbackBuild, childRollback,
}: {
  buildName: string;
  reduxName: string;
  reduxKicker: string;
  reduxCustomizations?: CustoEntry[];
  gunpackName: string;
  selgunsCount: number;
  soundsName: string | null;
  minimapName: string | null;
  reticleName: string | null;
  armorName:   string | null;
  arenaName:   string | null;
  installedAt: number;
  expanded: boolean;
  onToggleExpand: () => void;
  busy: boolean;
  globalBusy?: boolean;
  onRollbackBuild: () => void;
  childRollback: {
    redux:   { busy: boolean; onRollback: () => void };
    gunpack: { busy: boolean; onRollback: () => void };
    guns:    { busy: boolean; onRollback: () => void } | null;
    sounds:  { busy: boolean; onRollback: () => void } | null;
    minimap: { busy: boolean; onRollback: () => void } | null;
    reticle: { busy: boolean; onRollback: () => void } | null;
    armor:   { busy: boolean; onRollback: () => void } | null;
  };
}) {
  const { t } = useTranslation();
  return (
    <div
      className="rounded-xl bg-white/[0.03] border border-white/[0.08]
                 transition-colors"
      style={{
        boxShadow:
          '0 0 0 1px color-mix(in srgb, var(--accent) 14%, transparent), '
          + '0 8px 22px -14px color-mix(in srgb, var(--accent) 26%, transparent)',
      }}
    >
      {}
      <div className="flex items-center gap-3 px-4 py-3.5">
        <span
          className="shrink-0 w-10 h-10 rounded-xl flex items-center justify-center"
          style={{
            background: 'color-mix(in srgb, var(--accent) 14%, transparent)',
            boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 32%, transparent)',
          }}
        >
          <Boxes size={16} className="text-accent" strokeWidth={2.2} />
        </span>
        <div className="flex-1 min-w-0 flex flex-col">
          <span className="text-[10px] font-bold uppercase tracking-[0.22em] text-accent">
            {t('downloads.installedByBuild', 'Установлено сборкой')}
          </span>
          <span className="text-[14px] font-semibold text-text-primary truncate uppercase">
            {buildName}
          </span>
          <span className="text-[10.5px] text-text-muted">{relativeTime(installedAt, t)}</span>
        </div>
        <button
          type="button"
          onClick={onRollbackBuild}
          disabled={busy || globalBusy}
          title={t('downloads.rollbackBuild')}
          style={{ outline: 'none' }}
          className="shrink-0 inline-flex items-center gap-1.5 h-9 px-3 rounded-lg
                     bg-white/[0.04] border border-white/[0.10] text-text-secondary
                     text-[11px] uppercase tracking-[0.16em] font-bold
                     hover:bg-status-error/15 hover:border-status-error/40 hover:text-status-error
                     disabled:opacity-60 disabled:cursor-wait
                     transition-colors duration-200"
        >
          {busy ? <Loader2 size={12} className="animate-spin" /> : <RotateCcw size={12} />}
          <span>{busy ? t('downloads.rollingBack') : t('downloads.rollbackAll')}</span>
        </button>
        <button
          type="button"
          onClick={onToggleExpand}
          aria-label={expanded ? t('downloads.collapse') : t('downloads.expand')}
          title={expanded ? t('downloads.collapse') : t('downloads.expandBuild')}
          style={{ outline: 'none' }}
          className="shrink-0 inline-flex items-center justify-center w-9 h-9 rounded-lg
                     bg-white/[0.04] border border-white/[0.10] text-text-secondary
                     hover:bg-white/[0.08] hover:border-white/[0.18] hover:text-text-primary
                     transition-colors duration-200"
        >
          <motion.span
            animate={{ rotate: expanded ? 180 : 0 }}
            transition={{ duration: 0.22, ease: EASE_DEPTH }}
            style={{ display: 'inline-flex' }}
          >
            <ChevronDown size={14} />
          </motion.span>
        </button>
      </div>

      {}
      <AnimatePresence initial={false}>
        {expanded && (
          <motion.div
            key="build-children"
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit   ={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.28, ease: EASE_DEPTH }}
            style={{ overflow: 'hidden' }}
          >
            <div className="px-3 pb-3 pt-1 flex flex-col gap-2">
              <InstalledRow
                icon={Layers}
                kicker={reduxKicker}
                kickerAccent={!!reduxCustomizations && reduxCustomizations.length > 0}
                name={reduxName}
                customizations={reduxCustomizations}
                busy={childRollback.redux.busy}
                globalBusy={globalBusy}
                onRollback={childRollback.redux.onRollback}
              />
              {}
              {minimapName && childRollback.minimap && (
                <InstalledRow
                  icon={MapIcon}
                  kicker={t('downloads.kind.minimap')}
                  name={minimapName}
                  busy={childRollback.minimap.busy}
                  globalBusy={globalBusy}
                  onRollback={childRollback.minimap.onRollback}
                />
              )}
              {reticleName && childRollback.reticle && (
                <InstalledRow
                  icon={Target}
                  kicker={t('downloads.kind.reticle')}
                  name={reticleName}
                  busy={childRollback.reticle.busy}
                  globalBusy={globalBusy}
                  onRollback={childRollback.reticle.onRollback}
                />
              )}
              {armorName && childRollback.armor && (
                <InstalledRow
                  icon={ShieldIcon}
                  kicker={t('downloads.kind.armor')}
                  name={armorName}
                  busy={childRollback.armor.busy}
                  globalBusy={globalBusy}
                  onRollback={childRollback.armor.onRollback}
                />
              )}
              {}
              {arenaName && (
                <InstalledRow
                  icon={Building2}
                  kicker={t('downloads.kind.arena')}
                  name={arenaName}
                  hint={t('downloads.partOfRedux')}
                />
              )}
              <InstalledRow
                icon={Crosshair}
                kicker={t('downloads.kind.gunpack')}
                name={gunpackName}
                busy={childRollback.gunpack.busy}
                globalBusy={globalBusy}
                onRollback={childRollback.gunpack.onRollback}
              />
              {childRollback.guns && (
                <InstalledRow
                  icon={Box}
                  kicker={t('downloads.kind.selguns')}
                  name={t('downloads.gunsFromOtherPacks', { count: selgunsCount })}
                  busy={childRollback.guns.busy}
                  globalBusy={globalBusy}
                  onRollback={childRollback.guns.onRollback}
                />
              )}
              {childRollback.sounds && soundsName && (
                <InstalledRow
                  icon={Volume2}
                  kicker={t('downloads.kind.sounds')}
                  name={soundsName}
                  busy={childRollback.sounds.busy}
                  globalBusy={globalBusy}
                  onRollback={childRollback.sounds.onRollback}
                />
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

type TFn = (key: string, opts?: Record<string, unknown>) => string;

function HntInstalledRow({
  code, installedAt,
  reduxName, reduxKicker, reduxCustomizations,
  gunpackName, selgunsCount, components,
  expanded, onToggleExpand, busy, globalBusy, onRollbackAll, childRollback,
}: {
  code: string;
  installedAt: number;
  reduxName: string | null;
  reduxKicker: string;
  reduxCustomizations?: CustoEntry[];
  gunpackName: string | null;
  selgunsCount: number;
  components: HntComponentSnap[];
  expanded: boolean;
  onToggleExpand: () => void;
  busy: boolean;
  globalBusy?: boolean;
  onRollbackAll: () => void;
  childRollback: {
    redux:   { busy: boolean; onRollback: () => void } | null;
    gunpack: { busy: boolean; onRollback: () => void } | null;
    guns:    { busy: boolean; onRollback: () => void } | null;
    armor:   { busy: boolean; onRollback: () => void };
    minimap: { busy: boolean; onRollback: () => void };
    reticle: { busy: boolean; onRollback: () => void };
    sounds:  { busy: boolean; onRollback: () => void };
    bigMap:  { busy: boolean; onRollback: () => void };
  };
}) {
  const { t } = useTranslation();
  const compMeta: Record<HntComponentSnap['key'], { icon: LucideIcon; kicker: string }> = {
    armor:   { icon: ShieldIcon, kicker: t('downloads.kind.armor') },
    minimap: { icon: MapIcon,    kicker: t('downloads.kind.minimap') },
    reticle: { icon: Target,     kicker: t('downloads.kind.reticle') },
    sounds:  { icon: Volume2,    kicker: t('downloads.kind.sounds') },
    bigMap:  { icon: MapIcon,    kicker: t('downloads.kind.bigmap', 'Большая карта') },
  };
  return (
    <div
      className="rounded-xl bg-white/[0.03] border border-white/[0.08] transition-colors"
      style={{
        boxShadow:
          '0 0 0 1px color-mix(in srgb, var(--accent) 14%, transparent), '
          + '0 8px 22px -14px color-mix(in srgb, var(--accent) 26%, transparent)',
      }}
    >
      <div className="flex items-center gap-3 px-4 py-3.5">
        <span
          className="shrink-0 w-10 h-10 rounded-xl flex items-center justify-center"
          style={{
            background: 'color-mix(in srgb, var(--accent) 14%, transparent)',
            boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 32%, transparent)',
          }}
        >
          <Ticket size={16} className="text-accent" strokeWidth={2.2} />
        </span>
        <div className="flex-1 min-w-0 flex flex-col">
          <span className="text-[10px] font-bold uppercase tracking-[0.22em] text-accent">
            {t('downloads.installedByHnt', 'Установлено по HNT-коду')}
          </span>
          <span className="text-[14px] font-semibold text-text-primary truncate font-mono tabular-nums tracking-[0.1em]">
            {code}
          </span>
          <span className="text-[10.5px] text-text-muted">{relativeTime(installedAt, t)}</span>
        </div>
        <button
          type="button"
          onClick={onRollbackAll}
          disabled={busy || globalBusy}
          title={t('downloads.rollbackAllHint', 'Откатить все моды и вернуть GTA в исходное состояние')}
          style={{ outline: 'none' }}
          className="shrink-0 inline-flex items-center gap-1.5 h-9 px-3 rounded-lg
                     bg-white/[0.04] border border-white/[0.10] text-text-secondary
                     text-[11px] uppercase tracking-[0.16em] font-bold
                     hover:bg-status-error/15 hover:border-status-error/40 hover:text-status-error
                     disabled:opacity-60 disabled:cursor-wait
                     transition-colors duration-200"
        >
          {busy ? <Loader2 size={12} className="animate-spin" /> : <RotateCcw size={12} />}
          <span>{busy ? t('downloads.rollingBack') : t('downloads.rollbackAll')}</span>
        </button>
        <button
          type="button"
          onClick={onToggleExpand}
          aria-label={expanded ? t('downloads.collapse') : t('downloads.expand')}
          title={expanded ? t('downloads.collapse') : t('downloads.expand')}
          style={{ outline: 'none' }}
          className="shrink-0 inline-flex items-center justify-center w-9 h-9 rounded-lg
                     bg-white/[0.04] border border-white/[0.10] text-text-secondary
                     hover:bg-white/[0.08] hover:border-white/[0.18] hover:text-text-primary
                     transition-colors duration-200"
        >
          <motion.span
            animate={{ rotate: expanded ? 180 : 0 }}
            transition={{ duration: 0.22, ease: EASE_DEPTH }}
            style={{ display: 'inline-flex' }}
          >
            <ChevronDown size={14} />
          </motion.span>
        </button>
      </div>

      <AnimatePresence initial={false}>
        {expanded && (
          <motion.div
            key="hnt-children"
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit   ={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.28, ease: EASE_DEPTH }}
            style={{ overflow: 'hidden' }}
          >
            <div className="px-3 pb-3 pt-1 flex flex-col gap-2">
              {reduxName && childRollback.redux && (
                <InstalledRow
                  icon={Layers}
                  kicker={reduxKicker}
                  kickerAccent={!!reduxCustomizations && reduxCustomizations.length > 0}
                  name={reduxName}
                  customizations={reduxCustomizations}
                  busy={childRollback.redux.busy}
                  globalBusy={globalBusy}
                  onRollback={childRollback.redux.onRollback}
                />
              )}
              {gunpackName && childRollback.gunpack && (
                <InstalledRow
                  icon={Crosshair}
                  kicker={t('downloads.kind.gunpack')}
                  name={gunpackName}
                  busy={childRollback.gunpack.busy}
                  globalBusy={globalBusy}
                  onRollback={childRollback.gunpack.onRollback}
                />
              )}
              {selgunsCount > 0 && childRollback.guns && (
                <InstalledRow
                  icon={Box}
                  kicker={t('downloads.kind.selguns')}
                  name={t('downloads.gunsFromOtherPacks', { count: selgunsCount })}
                  busy={childRollback.guns.busy}
                  globalBusy={globalBusy}
                  onRollback={childRollback.guns.onRollback}
                />
              )}
              {components.map(c => (
                <InstalledRow
                  key={c.key}
                  icon={compMeta[c.key].icon}
                  kicker={compMeta[c.key].kicker}
                  name={c.name}
                  busy={childRollback[c.key].busy}
                  globalBusy={globalBusy}
                  onRollback={childRollback[c.key].onRollback}
                />
              ))}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

function relativeTime(ts: number, t: TFn): string {
  const diff = Date.now() - ts;
  const mins = Math.floor(diff / 60_000);
  if (mins < 1) return t('downloads.timeJustNow');
  if (mins < 60) return t('downloads.timeMin', { n: mins });
  const hours = Math.floor(mins / 60);
  if (hours < 24) return t('downloads.timeHour', { n: hours });
  const days = Math.floor(hours / 24);
  return t('downloads.timeDay', { n: days });
}

type StatTone = 'active' | 'done' | 'error';

function StatChip({
  icon: Icon, label, value, tone, progress,
}: {
  icon: LucideIcon;
  label: string;
  value: number;
  tone: StatTone;
  progress?: number;
}) {
  const palette = {
    active: 'text-text-primary',
    done:   'text-status-success',
    error:  'text-status-error',
  }[tone];

  return (
    <motion.div
      layout
      className="relative inline-flex items-center gap-2 h-10 pl-2.5 pr-3 rounded-xl border
                 bg-white/[0.04] border-white/[0.06]"
    >
      {}
      <span className={'relative shrink-0 w-7 h-7 rounded-md flex items-center justify-center bg-white/[0.06] ' + palette}>
        {tone === 'active' && progress !== undefined && progress > 0 && (
          <svg
            aria-hidden
            className="absolute -inset-0.5"
            viewBox="0 0 32 32"
          >
            <circle cx="16" cy="16" r="14" fill="none" stroke="rgba(255,255,255,0.10)" strokeWidth="1.5" />
            <motion.circle
              cx="16" cy="16" r="14"
              fill="none" stroke="currentColor" strokeWidth="1.5"
              strokeDasharray={`${(progress / 100) * 87.96} 87.96`}
              transform="rotate(-90 16 16)"
              strokeLinecap="round"
              initial={false}
              animate={{ strokeDasharray: `${(progress / 100) * 87.96} 87.96` }}
              transition={{ duration: 0.4, ease: EASE_DEPTH }}
            />
          </svg>
        )}
        <Icon size={13} strokeWidth={2} className="relative" />
      </span>

      <span className="text-[10px] font-bold uppercase tracking-[0.18em] text-text-muted leading-none">
        {label}
      </span>
      <span className="text-sm font-bold tabular-nums leading-none text-text-primary">
        {value}
      </span>
    </motion.div>
  );
}

function Section({
  icon: Icon, title, isEmpty, empty, emptyIcon: EmptyIcon, children, rightAction, accent,
}: {
  icon: LucideIcon;
  title: string;
  isEmpty?: boolean;
  empty?: string;
  emptyIcon?: LucideIcon;
  children?: React.ReactNode;
  rightAction?: React.ReactNode;
  accent?: boolean;
}) {
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-2 px-1">
        <Icon size={13} className="text-text-muted" strokeWidth={2} />
        <span className="text-[11px] uppercase tracking-[0.22em] text-text-muted font-bold">
          {title}
        </span>
        {accent && (
          <motion.span
            aria-hidden
            animate={{ opacity: [0.6, 1, 0.6], scale: [1, 1.2, 1] }}
            transition={{ duration: 1.6, repeat: Infinity, ease: 'easeInOut' }}
            className="w-1.5 h-1.5 rounded-full bg-status-success
                       shadow-[0_0_8px_var(--status-success)]"
          />
        )}
        {rightAction && <span className="ml-auto">{rightAction}</span>}
      </div>
      {isEmpty ? (
        <div
          className="rounded-2xl px-5 py-6 flex items-center gap-3
                     bg-white/[0.025]
                     text-text-muted"
        >
          {EmptyIcon && (
            <span className="shrink-0 w-9 h-9 rounded-xl flex items-center justify-center
                             bg-white/[0.04] border border-white/[0.06]">
              <EmptyIcon size={15} strokeWidth={1.7} />
            </span>
          )}
          <span className="text-sm">{empty}</span>
        </div>
      ) : (
        <div className="flex flex-col gap-2.5">{children}</div>
      )}
    </div>
  );
}

function ActiveRow({ entry, onDismiss }: { entry: InstallEntry; onDismiss: () => void }) {
  const { t } = useTranslation();
  const c = copyFor(entry.phase);
  const Icon = c.icon;
  const pct = Math.max(0, Math.min(100, entry.percent));
  const isQueued = entry.phase === 'queued';

  return (
    <GlassPanel
      depth="z1" tint="soft" rounded="2xl"
      className={
        'relative overflow-hidden px-4 py-4 flex items-center gap-4 border border-white/[0.08]'
        + (isQueued ? ' opacity-55 grayscale' : '')
      }
    >
      {}
      <span
        aria-hidden
        className="absolute inset-x-0 top-0 h-px pointer-events-none
                   bg-gradient-to-r from-transparent via-white/30 to-transparent"
      />
      {}
      <span className="relative shrink-0 w-11 h-11 rounded-2xl flex items-center justify-center
                       bg-white/[0.06] border border-white/[0.10] text-text-primary
                       shadow-[inset_0_1px_0_rgba(255,255,255,0.18)]">
        <motion.span
          aria-hidden
          animate={{ opacity: [0.35, 0.75, 0.35], scale: [1, 1.18, 1] }}
          transition={{ duration: 1.8, repeat: Infinity, ease: 'easeInOut' }}
          className="absolute -inset-1.5 rounded-3xl pointer-events-none"
          style={{
            background: 'radial-gradient(circle, rgba(255,255,255,0.22), transparent 70%)',
            filter: 'blur(6px)',
          }}
        />
        <Icon size={18} strokeWidth={1.8} className="relative" />
      </span>

      <div className="flex-1 min-w-0 flex flex-col gap-2">
        {}
        <div className="flex items-center gap-3">
          <span className="font-display font-bold text-sm uppercase tracking-wide text-text-primary truncate">
            {entry.name}
          </span>
          <span className="ml-auto text-base font-bold tabular-nums text-text-primary leading-none">
            {Math.floor(pct)}<span className="text-text-muted">%</span>
          </span>
        </div>

        {}
        <span className="text-xs text-text-secondary leading-snug truncate flex items-center gap-2">
          <motion.span
            aria-hidden
            animate={{ opacity: [0.4, 1, 0.4] }}
            transition={{ duration: 1.2, repeat: Infinity, ease: 'easeInOut' }}
            className="inline-block w-1.5 h-1.5 rounded-full bg-text-primary"
          />
          {entry.detailMessage ?? t(c.labelKey)}
        </span>

        {}
        <div className="relative mt-1 h-2 rounded-full overflow-hidden
                        bg-white/[0.04] border border-white/[0.06]">
          <motion.div
            className="relative h-full rounded-full bg-white
                       shadow-[0_0_18px_-2px_rgba(255,255,255,0.45)]"
            initial={false}
            animate={{ width: `${pct}%` }}
            transition={{ duration: 0.45, ease: EASE_DEPTH }}
          >
            {}
            <span
              aria-hidden
              className="absolute inset-y-0 left-0 right-0 overflow-hidden"
              style={{ borderRadius: '999px' }}
            >
              <motion.span
                className="absolute inset-y-0 w-1/3
                           bg-gradient-to-r from-transparent via-black/20 to-transparent"
                initial={{ x: '-100%' }}
                animate={{ x: '300%' }}
                transition={{ duration: 1.6, repeat: Infinity, ease: 'linear' }}
              />
            </span>
          </motion.div>
        </div>
      </div>

      <button
        type="button"
        onClick={onDismiss}
        title={t('downloads.hide')}
        aria-label={t('downloads.hide')}
        className="shrink-0 w-8 h-8 rounded-lg flex items-center justify-center
                   bg-white/[0.04] border border-white/[0.06]
                   text-text-muted hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                   transition-colors"
        style={{ outline: 'none' }}
      >
        <Trash2 size={13} />
      </button>
    </GlassPanel>
  );
}

function TerminalRow({
  name, phase, errorMessage, fresh, finishedAt,
}: {
  name: string;
  phase: 'done' | 'error';
  errorMessage: string | null;
  fresh?: boolean;
  finishedAt?: number;
}) {
  const { t } = useTranslation();
  const isDone = phase === 'done';
  const Icon = isDone ? CheckCircle2 : AlertCircle;
  const tint = isDone ? 'text-status-success' : 'text-status-error';
  const tintBg = isDone
    ? 'bg-[color-mix(in_srgb,var(--status-success)_15%,transparent)] border-[color-mix(in_srgb,var(--status-success)_30%,transparent)]'
    : 'bg-[color-mix(in_srgb,var(--status-error)_15%,transparent)] border-[color-mix(in_srgb,var(--status-error)_30%,transparent)]';
  const kind = inferOperationKind(name);
  const doneLabel = t(KIND_COPY[kind].doneLabelKey);
  return (
    <GlassPanel
      depth="z1"
      tint={fresh ? 'strong' : 'soft'}
      rounded="2xl"
      className={
        'px-4 py-3.5 flex items-center gap-3 border ' +
        (fresh ? 'border-white/[0.10]' : 'border-white/[0.04]')
      }
    >
      <span className={'shrink-0 w-9 h-9 rounded-xl flex items-center justify-center border ' + tintBg}>
        <Icon size={15} className={tint} strokeWidth={2} />
      </span>
      <div className="flex-1 min-w-0 flex flex-col gap-0.5">
        <span className="font-semibold text-sm text-text-primary truncate uppercase">{name}</span>
        <span className="text-[11px] text-text-muted truncate flex items-center gap-1.5">
          <span>{isDone ? doneLabel : (errorMessage ?? t('downloads.statusError'))}</span>
          {isDone && finishedAt && (
            <>
              <span className="opacity-50">·</span>
              <span>{formatRelativeTime(finishedAt, t)}</span>
            </>
          )}
        </span>
      </div>
    </GlassPanel>
  );
}

function formatRelativeTime(when: number, t: TFn): string {
  const diffSec = Math.max(0, Math.floor((Date.now() - when) / 1000));
  if (diffSec < 60) return t('downloads.timeJustNow');
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return t('downloads.timeMin', { n: diffMin });
  const diffH = Math.floor(diffMin / 60);
  if (diffH < 24) return t('downloads.timeHour', { n: diffH });
  const diffD = Math.floor(diffH / 24);
  return t('downloads.timeDay', { n: diffD });
}
