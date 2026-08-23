import { create } from 'zustand';
import type { BackupStatus, BackupResult, BackupProgress, BackupPhase } from '@/bridge/types';
import { bridge } from '@/bridge';

interface BackupStoreState {
  status: BackupStatus | null;
  progress: BackupProgress | null;
  backupGateOpen: boolean;
  triggeredByGate: boolean;
  backupSkippedForSession: boolean;
  markBackupSkipped: () => void;
  liveStats: {
    bytesPerSec:    number;
    etaSec:         number;
    doneBytes:      number;
    totalBytes:     number;
    remainingBytes: number;
    stalled:        boolean;
  } | null;

  lastActivePhase: BackupPhase | null;
  result: BackupResult | null;
  error: string | null;

  killingLockers: boolean;
  cancelling: boolean;
  openBackupGate: () => void;
  closeBackupGate: () => void;
  ensureBackupOrGate: () => boolean;
  loadStatus: () => Promise<void>;
  runBackup: () => Promise<void>;
  cancelBackup: () => Promise<void>;
  isBackupInProgress: () => boolean;
  killLockersAndRetry: () => Promise<void>;
  restoreClean: () => Promise<boolean>;
  restoreSnapshot: () => Promise<boolean>;
  clearProgress: () => void;
}

const PHASE_ORDER: BackupPhase[] = [
  'detecting', 'hashing_user_update', 'comparing', 'snapshot_user_update',
  'downloading_clean_update', 'writing_working_update', 'snapshot_dlc',
  'downloading_clean_dlc', 'writing_working_dlc', 'writing_manifest', 'done',
];
const phaseOrderIndex = (p: BackupPhase): number => {
  const i = PHASE_ORDER.indexOf(p);
  return i < 0 ? -1 : i;
};

export function formatBytes(b: number): string {
  if (b >= 1024 ** 3) return `${(b / 1024 ** 3).toFixed(1)} GB`;
  if (b >= 1024 ** 2) return `${(b / 1024 ** 2).toFixed(0)} MB`;
  if (b >= 1024)      return `${(b / 1024).toFixed(0)} KB`;
  return `${b} B`;
}
export function formatBytesPerSec(bps: number): string {
  if (bps <= 0) return '-';
  if (bps >= 1024 ** 2) return `${(bps / 1024 ** 2).toFixed(1)} MB/s`;
  if (bps >= 1024)      return `${(bps / 1024).toFixed(0)} KB/s`;
  return `${bps.toFixed(0)} B/s`;
}
export function formatEta(seconds: number): string {
  if (!isFinite(seconds) || seconds <= 0) return '-';
  const s = Math.round(seconds);
  if (s < 60) return `${s}s`;
  const m = Math.floor(s / 60);
  const rs = s % 60;
  return `${m}:${String(rs).padStart(2, '0')}`;
}

const SPEED_WINDOW_MS = 3000;
const STALL_GRACE_MS  = 2000;
const TICK_MS         = 1000;
const EMA_ALPHA       = 0.3;
const MIN_SHOWN_BPS   = 1024;

const DOWNLOAD_PHASES = new Set<BackupPhase>([
  'downloading_clean_update', 'downloading_clean_dlc',
]);

export const useBackupStore = create<BackupStoreState>((set, get) => {

  let samples: { t: number; bytes: number }[] = [];
  let lastEventAt = 0;
  let lastBytes   = 0;
  let lastTotal   = 0;
  let emaBps      = 0;
  let curPhase: BackupPhase | null = null;
  let lastKey     = '';
  let tickId: number | null = null;

  const resetSpeed = () => {
    samples = [];
    lastEventAt = 0; lastBytes = 0; lastTotal = 0; emaBps = 0;
    curPhase = null; lastKey = '';
  };

  const recordSample = (p: BackupProgress) => {
    if (!DOWNLOAD_PHASES.has(p.phase)) {
      if (curPhase !== null) resetSpeed();
      return;
    }
    if (p.phase !== curPhase) { resetSpeed(); curPhase = p.phase; }
    if (p.bytesProcessed == null) return;
    if (p.bytesProcessed < lastBytes) {
      if (p.bytesProcessed * 2 > lastBytes) return;
      samples = []; emaBps = 0;
    }
    lastBytes = p.bytesProcessed;
    if (p.bytesTotal != null && p.bytesTotal > 0) lastTotal = p.bytesTotal;
    lastEventAt = performance.now();
    samples.push({ t: lastEventAt, bytes: lastBytes });
  };

  const tick = () => {
    const phase = get().progress?.phase ?? null;
    if (!phase || !DOWNLOAD_PHASES.has(phase) || lastTotal <= 0 || samples.length === 0) {
      if (get().liveStats !== null) { set({ liveStats: null }); lastKey = ''; }
      return;
    }
    const now = performance.now();
    if (now - lastEventAt > STALL_GRACE_MS) samples.push({ t: now, bytes: lastBytes });
    while (samples.length > 2 && now - samples[0].t > SPEED_WINDOW_MS) samples.shift();
    if (samples.length < 2) return;

    const first = samples[0];
    const last  = samples[samples.length - 1];
    const dt    = (last.t - first.t) / 1000;
    const raw   = dt > 0.2 ? Math.max(0, last.bytes - first.bytes) / dt : emaBps;
    emaBps = emaBps > 0 ? emaBps + EMA_ALPHA * (raw - emaBps) : raw;
    const shown = emaBps < MIN_SHOWN_BPS ? 0 : emaBps;

    const remainingBytes = Math.max(0, lastTotal - lastBytes);
    const etaSec = shown > 0 ? remainingBytes / shown : 0;

    const key = `${shown > 0 ? formatBytesPerSec(shown) : '-'}|`
              + `${formatBytes(remainingBytes)}|${Math.round(etaSec / 60)}`;
    if (key === lastKey) return;
    lastKey = key;
    set({
      liveStats: {
        bytesPerSec: shown, etaSec,
        doneBytes: lastBytes, totalBytes: lastTotal,
        remainingBytes, stalled: shown <= 0,
      },
    });
  };

  const startTicker = () => {
    if (tickId === null) tickId = window.setInterval(tick, TICK_MS);
  };
  const stopTicker = () => {
    if (tickId !== null) { window.clearInterval(tickId); tickId = null; }
  };

  const onProgress = (p: BackupProgress) => {
    if (p.phase === 'error') return;
    const last = get().lastActivePhase;
    const lastOrder = last ? phaseOrderIndex(last) : -1;
    const newOrder  = phaseOrderIndex(p.phase);
    if (newOrder >= 0 && newOrder < lastOrder) return;
    recordSample(p);
    set({ progress: p, lastActivePhase: p.phase });
  };

  return {
    status: null,
    progress: null,
    backupGateOpen: false,
    triggeredByGate: false,
    backupSkippedForSession: false,
    markBackupSkipped: () => set({ backupSkippedForSession: true, backupGateOpen: false, triggeredByGate: false }),
    liveStats: null,
    lastActivePhase: null,
    result: null,
    error: null,
    killingLockers: false,
    cancelling: false,

    openBackupGate: () => {
      if (!get().backupGateOpen) set({ backupGateOpen: true, triggeredByGate: true });
    },
    closeBackupGate: () => {
      if (get().backupGateOpen) set({ backupGateOpen: false });
    },
    ensureBackupOrGate: () => {

      const s = get();
      if (s.status?.cleanUpdatePresent) return true;
      if (s.backupSkippedForSession) return true;
      if (!s.backupGateOpen) set({ backupGateOpen: true, triggeredByGate: true });
      return false;
    },

    loadStatus: async () => {
      try {
        const status = await bridge.backupGetStatus();
        set({ status });
      } catch (e) {
        set({ error: (e as Error).message });
      }
    },

    runBackup: async () => {

      if (get().progress !== null && get().result === null) {
        return;
      }

      resetSpeed();
      stopTicker();
      bridge.events.off('backup:progress', onProgress);
      bridge.events.on('backup:progress', onProgress);
      set({
        progress: {
          phase: 'detecting', percent: 0, fileName: null,
          bytesProcessed: null, bytesTotal: null, errorCode: null, errorMessage: null,
        },
        lastActivePhase: 'detecting',
        liveStats: null,
        result: null, error: null, cancelling: false,
      });
      startTicker();

      const MAX_TRANSIENT_ATTEMPTS = 10;
      try {
        let attempt: number;
        let result: BackupResult | null = null;
        for (attempt = 0; attempt < MAX_TRANSIENT_ATTEMPTS; attempt++) {
          result = await bridge.backupRunFull();
          const isTransient =
            !result.success
            && ((result.errorCode === 'FILE_LOCKED'
                  && (!result.lockers || result.lockers.length === 0))
                || result.errorCode === 'ALREADY_RUNNING');
          if (!isTransient) break;
          if (attempt === MAX_TRANSIENT_ATTEMPTS - 1) break;

          const backoff = result.errorCode === 'ALREADY_RUNNING'
            ? 1500 + attempt * 700
            : 800 + attempt * 400;
          await new Promise(r => setTimeout(r, backoff));
        }
        set({ result, progress: null, liveStats: null, cancelling: false });
        if (result?.success) {

          set({ triggeredByGate: false, backupSkippedForSession: false });
          await get().loadStatus();

        } else {
          set({ error: result?.errorMessage ?? null });

        }
      } catch (e) {
        const msg = (e as Error).message;
        set({ error: msg, progress: null, liveStats: null, cancelling: false });

      } finally {
        stopTicker();
        resetSpeed();
        bridge.events.off('backup:progress', onProgress);
      }
    },

    isBackupInProgress: () => {
      const s = get();
      return s.progress !== null && s.result === null;
    },

    cancelBackup: async () => {
      if (!get().progress || get().cancelling) return;
      set({ cancelling: true });
      try {
        await bridge.backupCancel();
      } catch (e) {
        console.warn('[backup] cancel failed', e);
        set({ cancelling: false });
      }
    },

    killLockersAndRetry: async () => {
      const lockers = get().result?.lockers ?? [];
      if (lockers.length === 0) return;
      set({ killingLockers: true });
      try {

        await bridge.killProcessesByPid(lockers.map(l => l.pid));
        await new Promise(r => setTimeout(r, 600));
      } catch (e) {

        console.warn('[backup] killProcessesByPid failed', e);
      } finally {
        set({ killingLockers: false });
      }
      await get().runBackup();
    },

    restoreClean: async () => bridge.backupRestoreClean(),
    restoreSnapshot: async () => bridge.backupRestoreSnapshot(),

    clearProgress: () => {
      stopTicker();
      resetSpeed();
      set({ progress: null, result: null, error: null, lastActivePhase: null, liveStats: null, killingLockers: false, cancelling: false });
    },
  };
});
