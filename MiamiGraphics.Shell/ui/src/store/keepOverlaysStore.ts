import { create } from 'zustand';

export interface KeepOverlaysPending {
  reduxName: string;
  reduxThumb: string | null;
  rings: number[];
  minimap: { id: string; name: string } | null;
  armor: { id: string; name: string } | null;
  fastJoin: boolean;
}

interface KeepOverlaysState {
  pending: KeepOverlaysPending | null;
  reapplyTick: number;
  open: (p: KeepOverlaysPending) => void;
  close: () => void;
  bumpReapplyTick: () => void;
}

export const useKeepOverlaysStore = create<KeepOverlaysState>(set => ({
  pending: null,
  reapplyTick: 0,
  open: (p) => set({ pending: p }),
  close: () => set({ pending: null }),
  bumpReapplyTick: () => set(s => ({ reapplyTick: s.reapplyTick + 1 })),
}));
