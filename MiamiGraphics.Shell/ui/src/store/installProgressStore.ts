import { create } from 'zustand';
import { bridge } from '@/bridge';
import i18n from '@/i18n';
import type { ReduxInstallProgress } from '@/bridge/IAppBridge';
import { useGunpackStore } from '@/store/gunpackStore';
import { useRu2QueueStore } from '@/store/ru2QueueStore';

const hntInstallName   = (code: string) => i18n.t('progress.hntInstallName',   { code, defaultValue: 'Установка из HNT-кода {{code}}' });
const buildInstallName = (name: string) => i18n.t('progress.buildInstallName', { name, defaultValue: 'Установка Сборки: {{name}}' });
const customizeName    = ()             => i18n.t('progress.customizeName',    'Кастомизация Redux');
const preparingText    = ()             => i18n.t('progress.preparing',        'Подготовка...');
const doneText         = ()             => i18n.t('common.done',               'Готово');

export interface InstallEntry extends Omit<ReduxInstallProgress, 'phase'> {
  phase: string;
  hideAt: number | null;
}

export interface InstallHistoryEntry {
  uid:           string;
  sourceId:      string;
  name:          string;
  phase:         'done' | 'error';
  errorMessage:  string | null;
  finishedAt:    number;
}

const HISTORY_LOCAL_KEY = 'hntgraph.installHistory';
const HISTORY_CAP       = 100;

function loadHistory(): InstallHistoryEntry[] {
  try {
    const raw = window.localStorage.getItem(HISTORY_LOCAL_KEY);
    if (!raw) return [];
    const arr = JSON.parse(raw);
    if (!Array.isArray(arr)) return [];

    return arr.filter((x: unknown): x is InstallHistoryEntry =>
      !!x && typeof x === 'object'
        && typeof (x as InstallHistoryEntry).uid === 'string'
        && typeof (x as InstallHistoryEntry).sourceId === 'string'
        && typeof (x as InstallHistoryEntry).name === 'string'
        && ((x as InstallHistoryEntry).phase === 'done' || (x as InstallHistoryEntry).phase === 'error')
        && typeof (x as InstallHistoryEntry).finishedAt === 'number'
    ).slice(0, HISTORY_CAP);
  } catch { return []; }
}
function persistHistory(list: InstallHistoryEntry[]): void {
  try {
    window.localStorage.setItem(HISTORY_LOCAL_KEY, JSON.stringify(list.slice(0, HISTORY_CAP)));
  } catch {  }
}

interface InstallProgressState {
  byId: Record<string, InstallEntry>;
  dismissedIds: Set<string>;
  silencedIds: Set<string>;
  history: InstallHistoryEntry[];
  dismiss: (entryId: string) => void;
  silenceProgress:   (entryId: string) => void;
  unsilenceProgress: (entryId: string) => void;
  prune: () => void;
  pushHistory: (e: InstallHistoryEntry) => void;
  clearHistory: () => void;
  reportLocalOp: (
    id: string,
    name: string,
    phase: string,
    detail?: string | null,
    errorMessage?: string | null,
  ) => void;
}

export const useInstallProgressStore = create<InstallProgressState>(set => ({
  byId: {},
  dismissedIds: new Set<string>(),
  silencedIds: new Set<string>(),
  history: loadHistory(),
  silenceProgress: (entryId) =>
    set(s => {
      const next = new Set(s.silencedIds);
      next.add(entryId);

      const byId = { ...s.byId };
      delete byId[entryId];
      return { silencedIds: next, byId };
    }),
  unsilenceProgress: (entryId) =>
    set(s => {
      const next = new Set(s.silencedIds);
      next.delete(entryId);
      return { silencedIds: next };
    }),
  dismiss: (entryId) =>
    set(s => {

      const nextDismissed = new Set(s.dismissedIds);
      nextDismissed.add(entryId);
      if (!s.byId[entryId]) return { dismissedIds: nextDismissed };
      const next = { ...s.byId };
      delete next[entryId];
      return { byId: next, dismissedIds: nextDismissed };
    }),
  prune: () =>
    set(s => {
      const now = Date.now();
      let changed = false;
      const next: Record<string, InstallEntry> = {};
      for (const [k, v] of Object.entries(s.byId)) {
        if (v.hideAt && now > v.hideAt) { changed = true; continue; }
        next[k] = v;
      }
      return changed ? { byId: next } : s;
    }),
  pushHistory: (e) =>
    set(s => {

      const cur = Array.isArray(s.history) ? s.history : [];

      if (cur.some(h => h.uid === e.uid)) return s;
      const next = [e, ...cur].slice(0, HISTORY_CAP);
      persistHistory(next);
      return { history: next };
    }),
  clearHistory: () =>
    set(() => {
      persistHistory([]);
      return { history: [] };
    }),
  reportLocalOp: (id, name, phase, detail, errorMessage) =>
    set(s => {
      const isTerminal = phase === 'done' || phase === 'error';

      if (s.dismissedIds.has(id)) {
        if (phase === 'starting') {
          const dismissed = new Set(s.dismissedIds);
          dismissed.delete(id);

          s = { ...s, dismissedIds: dismissed };
        } else {
          if (isTerminal) {
            queueMicrotask(() =>
              maybePushHistory(id, name, phase as 'done' | 'error', errorMessage ?? null));
          }
          return s;
        }
      }

      const hideAt: number | null = isTerminal
        ? Date.now() + (phase === 'error' ? 12_000 : 6_000)
        : null;
      const next = sanitizeProgressEvent(
        { reduxId: id, name, phase, percent: isTerminal ? 100 : 5,
          errorMessage: errorMessage ?? null, detailMessage: detail ?? null },
        s.byId[id], id, hideAt);

      clearTerminalMemoryIfNonTerminal(id, phase);

      const updated = {
        dismissedIds: s.dismissedIds,
        byId: { ...s.byId, [id]: next },
      };
      if (isTerminal) {

        queueMicrotask(() =>
          maybePushHistory(id, name, phase as 'done' | 'error', errorMessage ?? null));
      }
      return updated;
    }),
}));

function sanitizeProgressEvent(
  p: { reduxId: string; name: string; phase: string;
       percent: number; errorMessage: string | null; detailMessage?: string | null | undefined },
  prev: InstallEntry | undefined,
  id: string,
  hideAt: number | null,
): InstallEntry {
  const isTerminal = p.phase === 'done' || p.phase === 'error';

  let percent = p.percent;
  if (!isTerminal) {

    if (percent < 0) percent = prev?.percent ?? 0;

    if (prev && prev.phase !== 'done' && prev.phase !== 'error'
        && percent < prev.percent) {
      if (import.meta.env.DEV) {
        console.warn(
          `[progress.regress] ${id}: phase='${p.phase}' пришёл ${percent}% после ` +
          `${prev.percent}% - бар заморожен. Вложенный шаг должен получать полосу от вызывающего.`,
        );
      }
      percent = prev.percent;
    }
  }

  return {
    reduxId:       id,
    name:          p.name,
    phase:         p.phase,
    percent,
    errorMessage:  p.errorMessage,
    detailMessage: p.detailMessage ?? null,
    hideAt,
  };
}

let booted = false;

const lastTerminalPhase: Map<string, 'done' | 'error'> = new Map();

interface BuildInstallContext {
  buildId:   string;
  buildName: string;
  reduxId:   string;
  gunpackId: string;
  armorDonorReduxId: string | null;
  hasArmorClear: boolean;
  armorLibraryId: string | null;
  selgunsPlanned: number;
  selgunsCompleted: number;
  reduxDone: boolean;
}
let activeBuildContext: BuildInstallContext | null = null;

let buildHistorySuppressed = false;
export function setBuildHistorySuppressed(on: boolean) { buildHistorySuppressed = on; }

interface HntImportContext {
  code:           string;
  totalSteps:     number;
  completedSteps: number;
  currentKey:     string | null;
}
let activeHntImportContext: HntImportContext | null = null;

export function startHntImport(code: string, totalSteps: number): void {
  activeHntImportContext = {
    code,
    totalSteps: Math.max(1, totalSteps),
    completedSteps: 0,
    currentKey: null,
  };
  const id = `hnt:${code}`;
  lastTerminalPhase.delete(id);
  useInstallProgressStore.setState(s => {
    const dismissed = new Set(s.dismissedIds);
    dismissed.delete(id);
    return {
      dismissedIds: dismissed,
      byId: {
        ...s.byId,
        [id]: {
          reduxId:       id,
          name:          hntInstallName(code),
          phase:         'starting',
          percent:       0,
          errorMessage:  null,
          detailMessage: preparingText(),
          hideAt:        null,
        },
      },
    };
  });
}

export function finishHntImport(
  code: string,
  phase: 'done' | 'error',
  errorMessage: string | null = null,
): void {
  const ctx = activeHntImportContext;
  if (!ctx || ctx.code !== code) return;
  activeHntImportContext = null;
  const id     = `hnt:${code}`;
  const name   = hntInstallName(code);
  const hideAt = Date.now() + (phase === 'error' ? 12_000 : 6_000);

  if (useInstallProgressStore.getState().dismissedIds.has(id)) {
    maybePushHistory(id, name, phase, errorMessage);
    return;
  }
  useInstallProgressStore.setState(s => ({
    byId: {
      ...s.byId,
      [id]: {
        reduxId:       id,
        name,
        phase,
        percent:       100,
        errorMessage,
        detailMessage: phase === 'done' ? i18n.t('progress.hntDone', 'Сборка из HNT-кода установлена') : null,
        hideAt,
      },
    },
  }));
  maybePushHistory(id, name, phase, errorMessage);
}

function tryRedirectToHnt(
  channel:   string,
  rawId:     string,
  childName: string,
  phase:     string,
  rawPercent: number,
  errorMsg:  string | null,
  detail:    string | null | undefined,
): boolean {
  const ctx = activeHntImportContext;
  if (!ctx) return false;

  const key        = `${channel}:${rawId}`;
  const id         = `hnt:${ctx.code}`;
  const name       = hntInstallName(ctx.code);
  const isTerminal = phase === 'done' || phase === 'error';

  if (!isTerminal) {
    ctx.currentKey = key;
  } else {
    ctx.completedSteps = Math.min(ctx.completedSteps + 1, ctx.totalSteps);
    ctx.currentKey = null;
  }

  if (useInstallProgressStore.getState().dismissedIds.has(id)) return true;

  const sub     = isTerminal ? 100 : Math.max(0, Math.min(100, rawPercent));
  const stepped = isTerminal ? ctx.completedSteps : ctx.completedSteps + sub / 100;
  const percent = Math.min(97, Math.round((stepped / ctx.totalSteps) * 100));
  const detailLine = detail ? `${childName} · ${detail}` : childName;

  useInstallProgressStore.setState(s => {
    const prev = s.byId[id];
    const next = sanitizeProgressEvent({
      reduxId:       id,
      name,
      phase:         'downloading',
      percent:       rawPercent < 0 && !isTerminal ? -1 : percent,
      errorMessage:  null,
      detailMessage: phase === 'error' && errorMsg
        ? i18n.t('progress.stepError', { name: childName, error: errorMsg, defaultValue: '{{name}} · ошибка: {{error}}' })
        : detailLine,
    }, prev, id, null);
    return { byId: { ...s.byId, [id]: next } };
  });
  return true;
}

function computeBuildPhaseRange(
  phase: 'redux' | 'armor' | 'gunpack' | 'selguns',
): { start: number; end: number } {
  switch (phase) {
    case 'redux':   return { start: 0,  end: 30 };
    case 'armor':   return { start: 30, end: 45 };
    case 'gunpack': return { start: 45, end: 75 };
    case 'selguns': return { start: 75, end: 100 };
  }
}

export function startBuildInstall(opts: {
  buildId: string;
  buildName: string;
  reduxId: string;
  gunpackId: string;
  armorDonorReduxId?: string | null;
  hasArmorClear?: boolean;
  armorLibraryId?: string | null;
  selgunsPlanned?: number;
}): void {
  buildHistorySuppressed = true;
  activeBuildContext = {
    buildId:           opts.buildId,
    buildName:         opts.buildName,
    reduxId:           opts.reduxId,
    gunpackId:         opts.gunpackId,
    armorDonorReduxId: opts.armorDonorReduxId ?? null,
    hasArmorClear:     opts.hasArmorClear ?? false,
    armorLibraryId:    opts.armorLibraryId ?? null,
    selgunsPlanned:    opts.selgunsPlanned ?? 0,
    selgunsCompleted:  0,
    reduxDone:         false,
  };
  const id = `build:${opts.buildId}`;

  lastTerminalPhase.delete(id);

  useInstallProgressStore.setState(s => {
    const dismissed = new Set(s.dismissedIds);
    dismissed.delete(id);
    return {
      dismissedIds: dismissed,
      byId: {
        ...s.byId,
        [id]: {
          reduxId:       id,
          name:          buildInstallName(opts.buildName),
          phase:         'starting',
          percent:       0,
          errorMessage:  null,
          detailMessage: preparingText(),
          hideAt:        null,
        },
      },
    };
  });
}

export function finishBuildInstall(
  buildId: string,
  phase:   'done' | 'error',
  errorMessage: string | null = null,
): void {
  const ctx = activeBuildContext;
  if (!ctx || ctx.buildId !== buildId) return;
  const id     = `build:${buildId}`;
  const name   = buildInstallName(ctx.buildName);
  const hideAt = Date.now() + (phase === 'error' ? 12_000 : 6_000);

  if (useInstallProgressStore.getState().dismissedIds.has(id)) {
    maybePushHistory(id, name, phase, errorMessage);
    activeBuildContext = null;
    return;
  }
  useInstallProgressStore.setState(s => ({
    byId: {
      ...s.byId,
      [id]: {
        reduxId:       id,
        name,
        phase,
        percent:       100,
        errorMessage,
        detailMessage: phase === 'done' ? doneText() : null,
        hideAt,
      },
    },
  }));
  maybePushHistory(id, name, phase, errorMessage);
  activeBuildContext = null;
}

function tryRedirectToBuild(
  channel:    'redux' | 'gunpack' | 'selguns',
  rawId:      string,
  phase:      string,
  rawPercent: number,
  errorMsg:   string | null,
  detail:     string | null | undefined,
  selgunDisplayName?: string | null,
): boolean {
  const ctx = activeBuildContext;
  if (!ctx) return false;

  let buildPhase: 'redux' | 'armor' | 'gunpack' | 'selguns' | null = null;
  if (channel === 'redux') {
    if (rawId === ctx.reduxId) buildPhase = 'redux';
    else if (ctx.armorDonorReduxId && rawId === ctx.armorDonorReduxId) buildPhase = 'armor';
    else if (ctx.hasArmorClear && rawId === '__clear_armor__') buildPhase = 'armor';
    else if (ctx.armorLibraryId && rawId === ctx.armorLibraryId) buildPhase = 'armor';
  } else if (channel === 'gunpack') {
    if (rawId === ctx.gunpackId) buildPhase = 'gunpack';
  } else if (channel === 'selguns') {

    if (ctx.selgunsPlanned > 0) buildPhase = 'selguns';
  }
  if (buildPhase === null) return false;

  const id    = `build:${ctx.buildId}`;
  const name  = buildInstallName(ctx.buildName);
  const range = computeBuildPhaseRange(buildPhase);

  if (useInstallProgressStore.getState().dismissedIds.has(id)) {
    if (phase === 'error') {
      maybePushHistory(id, name, 'error', errorMsg ?? null);
      activeBuildContext = null;
    } else if (phase === 'done' && (
      buildPhase === 'gunpack' && ctx.selgunsPlanned === 0
      || buildPhase === 'selguns' && ctx.selgunsCompleted + 1 >= ctx.selgunsPlanned
    )) {
      maybePushHistory(id, name, 'done', null);
      activeBuildContext = null;
    }
    return true;
  }

  if (phase === 'error') {
    const phaseError =
      buildPhase === 'redux'   ? i18n.t('progress.buildErrorRedux',   'Ошибка установки редукса') :
      buildPhase === 'armor'   ? i18n.t('progress.buildErrorArmor',   'Ошибка установки брони') :
      buildPhase === 'gunpack' ? i18n.t('progress.buildErrorGunpack', 'Ошибка установки ганпака') :
                                 i18n.t('progress.buildErrorSelgun',  'Ошибка установки override-пушки');
    finishBuildInstall(ctx.buildId, 'error', errorMsg ?? phaseError);
    return true;
  }

  if (phase === 'done') {
    if (buildPhase === 'redux') {
      ctx.reduxDone = true;

      writeBuildEntryFromRange(id, name, range, 100, i18n.t('progress.reduxInstalled', 'Редукс установлен.'));
      return true;
    }
    if (buildPhase === 'armor') {
      writeBuildEntryFromRange(id, name, range, 100, i18n.t('progress.armorApplied', 'Броня применена.'));
      return true;
    }
    if (buildPhase === 'gunpack') {

      if (ctx.selgunsPlanned > 0) {
        writeBuildEntryFromRange(id, name, range, 100, i18n.t('progress.gunpackInstalled', 'Ганпак установлен.'));
        return true;
      }
      finishBuildInstall(ctx.buildId, 'done', null);
      return true;
    }
    if (buildPhase === 'selguns') {

      ctx.selgunsCompleted += 1;
      if (ctx.selgunsCompleted >= ctx.selgunsPlanned) {
        finishBuildInstall(ctx.buildId, 'done', null);
        return true;
      }

      writeBuildEntryFromSelgunSlot(id, name, ctx, 100, i18n.t('progress.overrideInstalled', 'Override установлен.'));
      return true;
    }
  }

  if (rawPercent < 0) {
    const detailLine = composeDetailLine(buildPhase, detail, selgunDisplayName);
    useInstallProgressStore.setState(s => {
      const prev = s.byId[id];
      const next = sanitizeProgressEvent({
        reduxId:       id,
        name,
        phase:         'downloading',
        percent:       -1,
        errorMessage:  errorMsg,
        detailMessage: detailLine,
      }, prev, id, null);
      return { byId: { ...s.byId, [id]: next } };
    });
    return true;
  }

  if (buildPhase === 'selguns') {
    writeBuildEntryFromSelgunSlot(id, name, ctx, rawPercent,
      composeDetailLine('selguns', detail, selgunDisplayName));
    return true;
  }

  writeBuildEntryFromRange(id, name, range, rawPercent,
    composeDetailLine(buildPhase, detail, null));
  return true;
}

function composeDetailLine(
  buildPhase: 'redux' | 'armor' | 'gunpack' | 'selguns',
  rawDetail:  string | null | undefined,
  selgunName: string | null | undefined,
): string {
  const head =
    buildPhase === 'redux'   ? i18n.t('progress.detailRedux',   'Качаем редукс...') :
    buildPhase === 'armor'   ? i18n.t('progress.detailArmor',   'Накатываем броню...') :
    buildPhase === 'gunpack' ? i18n.t('progress.detailGunpack', 'Накатываем ганпак...') :
     (selgunName
       ? i18n.t('progress.detailOverrideNamed', { name: selgunName, defaultValue: 'Override: {{name}}' })
       : i18n.t('progress.detailOverride', 'Override-пушка...'));
  return rawDetail ? `${head} ${rawDetail}` : head;
}

function writeBuildEntryFromRange(
  id:         string,
  name:       string,
  range:      { start: number; end: number },
  subPercent: number,
  detail:     string,
): void {
  const sub      = Math.max(0, Math.min(100, subPercent)) / 100;
  const buildPct = Math.round(range.start + (range.end - range.start) * sub);
  useInstallProgressStore.setState(s => {
    const prev = s.byId[id];
    const next = sanitizeProgressEvent({
      reduxId:       id,
      name,
      phase:         'downloading',
      percent:       buildPct,
      errorMessage:  null,
      detailMessage: detail,
    }, prev, id, null);
    return { byId: { ...s.byId, [id]: next } };
  });
}

function writeBuildEntryFromSelgunSlot(
  id:         string,
  name:       string,
  ctx:        BuildInstallContext,
  subPercent: number,
  detail:     string,
): void {
  const range  = computeBuildPhaseRange('selguns');
  const span   = range.end - range.start;
  const slot   = ctx.selgunsCompleted;
  const N      = Math.max(1, ctx.selgunsPlanned);
  const sub    = Math.max(0, Math.min(100, subPercent)) / 100;
  const slotPct = (slot + sub) / N;
  const buildPct = Math.round(range.start + span * slotPct);
  useInstallProgressStore.setState(s => {
    const prev = s.byId[id];
    const next = sanitizeProgressEvent({
      reduxId:       id,
      name,
      phase:         'downloading',
      percent:       buildPct,
      errorMessage:  null,
      detailMessage: detail,
    }, prev, id, null);
    return { byId: { ...s.byId, [id]: next } };
  });
}

function maybePushHistory(id: string, name: string, phase: 'done' | 'error', errorMessage: string | null) {

  if (buildHistorySuppressed && !id.startsWith('build:')) return;

  if (useInstallProgressStore.getState().silencedIds.has(id)) {
    lastTerminalPhase.set(id, phase);
    return;
  }
  if (lastTerminalPhase.get(id) === phase) return;
  lastTerminalPhase.set(id, phase);
  const finishedAt = Date.now();
  useInstallProgressStore.getState().pushHistory({
    uid: `${id}@${finishedAt}`,
    sourceId: id,
    name,
    phase,
    errorMessage,
    finishedAt,
  });

}

function clearTerminalMemoryIfNonTerminal(id: string, phase: string) {

  if (phase !== 'done' && phase !== 'error') {
    lastTerminalPhase.delete(id);
  }
}

function shouldSkipDismissed(id: string, phase: string, percent = 0): boolean {
  const s = useInstallProgressStore.getState();
  if (s.silencedIds.has(id)) return true;
  if (!s.dismissedIds.has(id)) return false;
  if (phase === 'starting' && percent <= 0) {
    useInstallProgressStore.setState(s2 => {
      const next = new Set(s2.dismissedIds);
      next.delete(id);
      return { dismissedIds: next };
    });
    return false;
  }
  return true;
}

let activeCustomizeContext: { reduxId: string; name: string | null } | null = null;

export function startCustomizeInstall(reduxId: string): void {
  activeCustomizeContext = { reduxId, name: null };
  const id = `customize:${reduxId}`;
  lastTerminalPhase.delete(id);
  useInstallProgressStore.setState(s => {
    const dismissed = new Set(s.dismissedIds);
    dismissed.delete(id);
    return {
      dismissedIds: dismissed,
      byId: {
        ...s.byId,
        [id]: {
          reduxId:       id,
          name:          i18n.t('progress.customizeNameRunning', 'Кастомизация Redux…'),
          phase:         'starting',
          percent:       0,
          errorMessage:  null,
          detailMessage: preparingText(),
          hideAt:        null,
        },
      },
    };
  });
}

export function finishCustomizeInstall(
  reduxId: string,
  phase:   'done' | 'error',
  errorMessage: string | null = null,
): void {
  const ctx = activeCustomizeContext;
  if (!ctx || ctx.reduxId !== reduxId) return;
  const id     = `customize:${reduxId}`;
  const name   = ctx.name ?? customizeName();
  const hideAt = Date.now() + (phase === 'error' ? 12_000 : 6_000);
  activeCustomizeContext = null;
  if (useInstallProgressStore.getState().dismissedIds.has(id)) {
    maybePushHistory(id, name, phase, errorMessage);
    return;
  }
  useInstallProgressStore.setState(s => ({
    byId: {
      ...s.byId,
      [id]: {
        reduxId:       id,
        name,
        phase,
        percent:       100,
        errorMessage,
        detailMessage: phase === 'done' ? doneText() : null,
        hideAt,
      },
    },
  }));
  maybePushHistory(id, name, phase, errorMessage);
}

function tryRedirectToCustomize(
  rawId:      string,
  name:       string,
  phase:      string,
  rawPercent: number,
  errorMsg:   string | null,
  detail:     string | null | undefined,
): boolean {
  const ctx = activeCustomizeContext;
  if (!ctx) return false;
  const isRedux  = rawId === ctx.reduxId;
  const isBigMap = rawId === 'bigmap';
  if (!isRedux && !isBigMap) return false;
  if (isRedux && name) ctx.name = name;

  const id    = `customize:${ctx.reduxId}`;
  const band  = isBigMap ? { start: 80, end: 100 } : { start: 0, end: 80 };
  const clamp = Math.max(0, Math.min(100, rawPercent));
  const scaled = Math.round(band.start + (clamp / 100) * (band.end - band.start));

  let outPhase = phase, outPercent = scaled;
  if (phase === 'error')                 { outPhase = 'error';      outPercent = scaled; }
  else if (isBigMap && phase === 'done') { outPhase = 'done';       outPercent = 100;    }
  else if (isRedux  && phase === 'done') { outPhase = 'installing'; outPercent = 80;     }

  useInstallProgressStore.setState(s => {
    const prev = s.byId[id];
    return {
      byId: {
        ...s.byId,
        [id]: {
          reduxId:       id,
          name:          ctx.name ?? prev?.name ?? customizeName(),
          phase:         outPhase,
          percent:       Math.max(prev?.percent ?? 0, outPercent),
          errorMessage:  phase === 'error' ? errorMsg : null,
          detailMessage: detail ?? prev?.detailMessage ?? null,
          hideAt:        null,
        },
      },
    };
  });
  return true;
}

export function ensureInstallProgressBooted() {
  if (booted) return;
  booted = true;

  bridge.events.on('download:queue', (p: { active?: boolean; position?: number; etaSec?: number }) => {
    useRu2QueueStore.getState().set({
      active:   !!p?.active,
      position: p?.position ?? 0,
      etaSec:   p?.etaSec ?? 0,
    });
  });

  bridge.events.on('redux:installProgress', (p) => {

    if (tryRedirectToHnt('redux', p.reduxId, p.name, p.phase, p.percent,
                         p.errorMessage, p.detailMessage ?? null)) {
      return;
    }
    if (tryRedirectToBuild('redux', p.reduxId, p.phase, p.percent,
                           p.errorMessage, p.detailMessage ?? null)) {
      return;
    }
    if (tryRedirectToCustomize(p.reduxId, p.name, p.phase, p.percent,
                               p.errorMessage, p.detailMessage ?? null)) {
      return;
    }
    const isTerminal = p.phase === 'done' || p.phase === 'error';

    const isFinished = isTerminal || p.phase === 'cancelled';
    const hideAt = isFinished
      ? Date.now() + (p.phase === 'error' ? 12_000 : 6_000)
      : null;
    const id = `redux:${p.reduxId}`;
    if (shouldSkipDismissed(id, p.phase, p.percent)) return;
    clearTerminalMemoryIfNonTerminal(id, p.phase);
    useInstallProgressStore.setState(s => ({
      byId: { ...s.byId, [id]: sanitizeProgressEvent(p, s.byId[id], id, hideAt) },
    }));
    if (isTerminal) {
      maybePushHistory(id, p.name, p.phase as 'done' | 'error', p.errorMessage);
    }
  });

  bridge.events.on('selectedguns:installProgress', (p) => {
    const internalName = p.internalName ?? '_';
    const wl = useGunpackStore.getState().whitelist.find(w => w.internalName === p.internalName);
    const friendlyGun = wl?.displayName ?? p.internalName ?? null;

    const hntStepName = friendlyGun
      ? i18n.t('progress.hntStepGun', { name: friendlyGun, defaultValue: 'Ган: {{name}}' })
      : i18n.t('progress.hntStepGuns', 'Отдельные ганы');

    if (tryRedirectToHnt('selectedguns', internalName, hntStepName,
                         p.phase, p.percent, p.errorMessage, p.detailMessage ?? null)) {
      return;
    }
    if (p.internalName
        && tryRedirectToBuild('selguns', p.internalName, p.phase, p.percent,
                              p.errorMessage, p.detailMessage ?? null,
                              friendlyGun)) {
      return;
    }

    if (activeBuildContext && !p.internalName) return;

    const isTerminal = p.phase === 'done' || p.phase === 'error';
    const hideAt = isTerminal
      ? Date.now() + (p.phase === 'error' ? 12_000 : 6_000)
      : null;

    const id = `selectedguns:${internalName}`;
    if (shouldSkipDismissed(id, p.phase, p.percent)) return;

    const name = friendlyGun
      ? i18n.t('progress.selgunsInstallNamed', { name: friendlyGun, defaultValue: 'Установка выборочных оружий: {{name}}' })
      : i18n.t('progress.selgunsInstall', 'Установка выборочных оружий');

    clearTerminalMemoryIfNonTerminal(id, p.phase);
    useInstallProgressStore.setState(s => {
      const prev = s.byId[id];
      const next = sanitizeProgressEvent(
        { reduxId: id, name, phase: p.phase, percent: p.percent,
          errorMessage: p.errorMessage, detailMessage: p.detailMessage },
        prev, id, hideAt);
      return { byId: { ...s.byId, [id]: next } };
    });
    if (isTerminal) {
      maybePushHistory(id, name, p.phase as 'done' | 'error', p.errorMessage);
    }
  });

  bridge.events.on('gunpack:installProgress', (p) => {

    if (tryRedirectToHnt('gunpack', p.gunpackId ?? 'uninstall',
                         i18n.t('progress.hntStepGunpack', 'Ган-пак'),
                         p.phase, p.percent, p.errorMessage, p.detailMessage ?? null)) {
      return;
    }
    if (p.gunpackId
        && tryRedirectToBuild('gunpack', p.gunpackId, p.phase, p.percent,
                              p.errorMessage, p.detailMessage ?? null)) {
      return;
    }
    const isTerminal = p.phase === 'done' || p.phase === 'error';
    const hideAt = isTerminal
      ? Date.now() + (p.phase === 'error' ? 12_000 : 6_000)
      : null;

    const isUninstall = !p.gunpackId;
    let name: string;
    if (isUninstall) {
      name = i18n.t('progress.gunpackRollback', 'Откат установки Gunpack');
    } else {
      const gs = useGunpackStore.getState();
      const pack = gs.selectedPack?.id === p.gunpackId
        ? gs.selectedPack
        : (gs.publicPacks.find(x => x.id === p.gunpackId)
           ?? gs.packs.find(x => x.id === p.gunpackId)
           ?? null);
      const packName = pack?.name
        ?? i18n.t('progress.gunpackFallbackName', { id: p.gunpackId.slice(0, 8), defaultValue: 'Ганпак {{id}}…' });
      name = i18n.t('progress.gunpackInstallName', { name: packName, defaultValue: 'Установка Gunpack: {{name}}' });
    }

    const id = isUninstall ? 'gunpack:uninstall' : `gunpack:${p.gunpackId}`;
    if (shouldSkipDismissed(id, p.phase, p.percent)) return;
    clearTerminalMemoryIfNonTerminal(id, p.phase);

    useInstallProgressStore.setState(s => {
      const prev = s.byId[id];
      const next = sanitizeProgressEvent(
        { reduxId: id, name, phase: p.phase, percent: p.percent,
          errorMessage: p.errorMessage, detailMessage: p.detailMessage },
        prev, id, hideAt);
      return { byId: { ...s.byId, [id]: next } };
    });
    if (isTerminal) {
      maybePushHistory(id, name, p.phase as 'done' | 'error', p.errorMessage);
    }
  });

  if (tickerId !== null) {
    try { window.clearInterval(tickerId); } catch {  }
  }
  tickerId = window.setInterval(() => {
    useInstallProgressStore.getState().prune();
  }, 1_000);
}

let tickerId: number | null = null;
