import { create } from 'zustand';
import { bridge } from '@/bridge';
import type { PcDiagReport, PcDiagJournalEntry, PcDiagTweak } from '@/bridge/IAppBridge';
import { readCache, writeCache } from './catalogCache';

const STALE_MS = 24 * 3600e3;

const CACHE_KEY = 'pcdiag.report';

interface CachedSnapshot {
  report:  PcDiagReport;
  takenAt: number;
}

interface PcDiagState {
  report:  PcDiagReport | null;
  takenAt: number | null;
  journal: PcDiagJournalEntry[];
  tweaks:  PcDiagTweak[];
  scanning: boolean;
  error:   string | null;

  ensureSnapshot: (force?: boolean) => Promise<void>;
  refreshState: () => Promise<void>;
}

let inFlight: Promise<void> | null = null;

function isUsable(s: CachedSnapshot | null): s is CachedSnapshot {
  const r = s?.report as PcDiagReport | undefined;
  return !!r
    && typeof s?.takenAt === 'number'
    && Array.isArray(r.findings)
    && Array.isArray(r.gpus)
    && Array.isArray(r.disks)
    && Array.isArray(r.ramSticks)
    && Array.isArray(r.background)
    && Array.isArray(r.sensorErrors);
}

const raw = readCache<CachedSnapshot>(CACHE_KEY);
const cached = isUsable(raw) ? raw : null;

export const usePcDiagStore = create<PcDiagState>((set, get) => ({
  report:   cached?.report  ?? null,
  takenAt:  cached?.takenAt ?? null,
  journal:  [],
  tweaks:   [],
  scanning: false,
  error:    null,

  ensureSnapshot: async (force = false) => {
    const { report, takenAt } = get();
    const fresh = report != null && takenAt != null && Date.now() - takenAt < STALE_MS;
    if (fresh && !force) return;
    if (inFlight) return inFlight;

    set({ scanning: true, error: null });
    inFlight = (async () => {
      try {
        const rep = await bridge.pcDiagReport();
        const takenAt = Date.now();
        set({ report: rep, takenAt, error: null });
        writeCache<CachedSnapshot>(CACHE_KEY, { report: rep, takenAt });
      } catch (e) {
        set({ error: e instanceof Error ? e.message : String(e) });
      } finally {
        set({ scanning: false });
        inFlight = null;
      }
    })();
    return inFlight;
  },

  refreshState: async () => {
    try {
      const [jr, tw] = await Promise.all([bridge.pcDiagJournal(), bridge.pcDiagTweaks()]);
      set({ journal: jr, tweaks: tw });
    } catch {
    }
  },
}));
